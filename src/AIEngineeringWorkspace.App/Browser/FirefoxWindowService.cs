using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

internal sealed class FirefoxWindowService
{
    private const string DefaultUrl = "https://www.google.com/";
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);

    public IReadOnlyList<BrowserWindowInfo> FindFirefoxWindows()
    {
        var windows = new List<BrowserWindowInfo>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                {
                    return true;
                }

                using var process = Process.GetProcessById((int)pid);
                if (!string.Equals(process.ProcessName, "firefox", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var title = ReadWindowText(hwnd);
                var className = ReadClassName(hwnd);

                if (!string.Equals(className, "MozillaWindowClass", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                windows.Add(new BrowserWindowInfo(hwnd, pid, title, className));
            }
            catch (Exception ex)
            {
                RuntimeLog.Debug($"Skipping HWND=0x{hwnd.ToInt64():X} during Firefox enumeration: {ex.Message}");
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public BrowserWindowInfo? FindSingleExistingWindow()
    {
        var windows = FindFirefoxWindows();
        if (windows.Count == 1)
        {
            return windows[0];
        }

        if (windows.Count > 1)
        {
            RuntimeLog.Warn($"Dock Existing refused because {windows.Count} visible Firefox windows were found. Candidates={DescribeWindows(windows)}");
        }

        return null;
    }

    public async Task<BrowserWindowInfo?> LaunchAndFindNewWindowAsync(
        string? url,
        CancellationToken cancellationToken,
        string requester = "BrowserTile")
    {
        RuntimeLog.Info($"[{requester}] Waiting for Firefox launch serialization gate.");
        await LaunchGate.WaitAsync(cancellationToken);

        try
        {
            RuntimeLog.Info($"[{requester}] Firefox launch serialization gate acquired.");

            var targetUrl = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();
            var beforeWindows = FindFirefoxWindows();
            var before = beforeWindows.Select(w => w.Hwnd).ToHashSet();
            var firefoxPath = ResolveFirefoxPath();

            RuntimeLog.Info($"[{requester}] Launching Firefox. Executable='{firefoxPath}', URL='{targetUrl}', ExistingTopLevelWindows={beforeWindows.Count}, Existing={DescribeWindows(beforeWindows)}");

            var safeUrl = targetUrl.Replace("\"", string.Empty, StringComparison.Ordinal);
            var startInfo = new ProcessStartInfo
            {
                FileName = firefoxPath,
                Arguments = $"--new-window \"{safeUrl}\"",
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            RuntimeLog.Info($"[{requester}] Firefox launch requested. LauncherPID={(process is null ? "n/a" : process.Id)}");

            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(200, cancellationToken);

                var windows = FindFirefoxWindows();
                var newWindows = windows.Where(w => !before.Contains(w.Hwnd)).ToList();
                if (newWindows.Count == 1)
                {
                    var newWindow = newWindows[0];
                    RuntimeLog.Info($"[{requester}] New Firefox window isolated. PID={newWindow.ProcessId}; HWND={newWindow.HwndHex}; Title='{newWindow.Title}'");
                    return newWindow;
                }

                if (newWindows.Count > 1)
                {
                    RuntimeLog.Warn($"[{requester}] Launch produced multiple new Firefox HWND candidates; refusing to guess. Candidates={DescribeWindows(newWindows)}");
                    return null;
                }
            }

            var afterWindows = FindFirefoxWindows();
            if (beforeWindows.Count == 0 && afterWindows.Count == 1)
            {
                var onlyWindow = afterWindows[0];
                RuntimeLog.Warn($"[{requester}] No new-HWND transition was observed, but exactly one Firefox window exists after launch. Using safe single candidate HWND={onlyWindow.HwndHex}; PID={onlyWindow.ProcessId}; Title='{onlyWindow.Title}'.");
                return onlyWindow;
            }

            RuntimeLog.Error($"[{requester}] Could not safely isolate the Firefox window created by launch. Before={beforeWindows.Count}; After={afterWindows.Count}; AfterCandidates={DescribeWindows(afterWindows)}");
            return null;
        }
        finally
        {
            LaunchGate.Release();
            RuntimeLog.Info($"[{requester}] Firefox launch serialization gate released.");
        }
    }

    public bool RequestCloseWindow(BrowserWindowInfo window, string requester)
    {
        var hwnd = window.Hwnd;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            RuntimeLog.Info($"[{requester}] Workspace-launched Firefox already closed. PID={window.ProcessId}; HWND={window.HwndHex}");
            return true;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var currentPid);
        if (currentPid != window.ProcessId)
        {
            RuntimeLog.Warn($"[{requester}] Refusing shutdown close because HWND ownership changed. HWND={window.HwndHex}; ExpectedPID={window.ProcessId}; CurrentPID={currentPid}");
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)currentPid);
            var className = ReadClassName(hwnd);
            if (!string.Equals(process.ProcessName, "firefox", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(className, "MozillaWindowClass", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeLog.Warn($"[{requester}] Refusing shutdown close because HWND no longer identifies the expected Firefox top-level window. PID={currentPid}; HWND={window.HwndHex}; Process='{process.ProcessName}'; Class='{className}'");
                return false;
            }
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn($"[{requester}] Refusing shutdown close because Firefox HWND identity validation failed. PID={currentPid}; HWND={window.HwndHex}; Error={ex.Message}");
            return false;
        }

        RuntimeLog.Info($"[{requester}] Requesting graceful close for Workspace-launched Firefox window. PID={window.ProcessId}; HWND={window.HwndHex}; Title='{window.Title}'");
        Marshal.SetLastPInvokeError(0);
        var sendResult = NativeMethods.SendMessageTimeout(
            hwnd,
            NativeMethods.WM_CLOSE,
            UIntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG,
            2000,
            out _);

        if (sendResult == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            RuntimeLog.Warn($"[{requester}] WM_CLOSE request failed or timed out. PID={window.ProcessId}; HWND={window.HwndHex}; Win32={error}");
            return false;
        }

        RuntimeLog.Info($"[{requester}] WM_CLOSE delivered to Workspace-launched Firefox. PID={window.ProcessId}; HWND={window.HwndHex}");
        return true;
    }

    public static string ResolveFirefoxPath()
    {
        var candidates = new List<string>();

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"));
        }

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe"));
        }

        var found = candidates.FirstOrDefault(File.Exists);
        return found ?? "firefox.exe";
    }

    private static string DescribeWindows(IEnumerable<BrowserWindowInfo> windows)
        => string.Join(" | ", windows.Select(w => $"PID={w.ProcessId},HWND={w.HwndHex},Title='{w.Title}'"));

    private static string ReadWindowText(IntPtr hwnd)
    {
        var sb = new StringBuilder(1024);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
