using System.Runtime.InteropServices;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

/// <summary>
/// Coordinates the one-shot Win32 input-queue handoff required to focus a reparented
/// Firefox root HWND. The coordinator deliberately owns no dock-lifetime bridge state.
/// </summary>
internal static class FirefoxInputCoordinator
{
    private static readonly object Sync = new();

    internal static void FocusRoot(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd))
        {
            RuntimeLog.Warn($"Firefox focus handoff skipped because BrowserHWND=0x{browserHwnd.ToInt64():X} is invalid. Reason='{reason}'");
            return;
        }

        lock (Sync)
        {
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
            if (browserThreadId == 0)
            {
                RuntimeLog.Warn($"Firefox focus handoff could not resolve BrowserThread. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; Reason='{reason}'");
                return;
            }

            var bridgeRequired = workspaceThreadId != browserThreadId;
            var bridgeAttached = false;
            var bridgeDetached = !bridgeRequired;
            IntPtr previousFocus = IntPtr.Zero;
            IntPtr currentFocus = IntPtr.Zero;
            IntPtr foreground = IntPtr.Zero;
            var focusError = 0;

            try
            {
                if (bridgeRequired)
                {
                    Marshal.SetLastPInvokeError(0);
                    bridgeAttached = NativeMethods.AttachThreadInput(workspaceThreadId, browserThreadId, true);
                    if (!bridgeAttached)
                    {
                        RuntimeLog.Warn($"Temporary Firefox input bridge attach failed. WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={Marshal.GetLastWin32Error()}; Reason='{reason}'");
                    }
                }

                Marshal.SetLastPInvokeError(0);
                previousFocus = NativeMethods.SetFocus(browserHwnd);
                focusError = Marshal.GetLastPInvokeError();
                currentFocus = NativeMethods.GetFocus();
                foreground = NativeMethods.GetForegroundWindow();
            }
            finally
            {
                if (bridgeAttached)
                {
                    Marshal.SetLastPInvokeError(0);
                    bridgeDetached = NativeMethods.AttachThreadInput(workspaceThreadId, browserThreadId, false);
                    if (!bridgeDetached)
                    {
                        RuntimeLog.Warn($"Temporary Firefox input bridge detach failed. WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={Marshal.GetLastWin32Error()}; Reason='{reason}'");
                    }
                }
            }

            RuntimeLog.Info(
                $"Firefox transactional root focus handoff completed. BrowserHWND=0x{browserHwnd.ToInt64():X}; " +
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; " +
                $"TemporaryInputBridgeAttached={bridgeAttached}; TemporaryInputBridgeDetached={bridgeDetached}; " +
                $"PreviousFocus=0x{previousFocus.ToInt64():X}; CurrentFocus=0x{currentFocus.ToInt64():X}; " +
                $"Foreground=0x{foreground.ToInt64():X}; Win32={focusError}; Reason='{reason}'");
        }
    }
}
