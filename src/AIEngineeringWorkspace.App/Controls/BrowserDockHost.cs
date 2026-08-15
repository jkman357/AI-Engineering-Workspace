using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Controls;

public sealed class BrowserDockHost : HwndHost
{
    private IntPtr _hostHwnd;
    private IntPtr _browserHwnd;
    private IntPtr _originalStyle;
    private IntPtr _originalExStyle;
    private NativeMethods.WINDOWPLACEMENT _originalPlacement;
    private bool _hasOriginalPlacement;
    private int _lastWidth = -1;
    private int _lastHeight = -1;

    public event EventHandler? DockedWindowLost;

    public bool HasDockedWindow => _browserHwnd != IntPtr.Zero;
    public bool IsDocked => _browserHwnd != IntPtr.Zero && NativeMethods.IsWindow(_browserHwnd);
    public IntPtr BrowserHwnd => _browserHwnd;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        const uint hostStyle = (uint)(NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS);

        _hostHwnd = NativeMethods.CreateWindowEx(
            0,
            "static",
            string.Empty,
            hostStyle,
            0,
            0,
            1,
            1,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hostHwnd == IntPtr.Zero)
        {
            NativeMethods.ThrowLastWin32Error("CreateWindowEx for browser host failed");
        }

        RuntimeLog.Info($"Browser host HWND created: 0x{_hostHwnd.ToInt64():X}");
        return new HandleRef(this, _hostHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        try
        {
            Detach();
        }
        finally
        {
            if (hwnd.Handle != IntPtr.Zero && NativeMethods.IsWindow(hwnd.Handle) && !NativeMethods.DestroyWindow(hwnd.Handle))
            {
                RuntimeLog.Warn($"DestroyWindow failed for host HWND=0x{hwnd.Handle.ToInt64():X}; Win32={Marshal.GetLastWin32Error()}");
            }

            _hostHwnd = IntPtr.Zero;
        }
    }

    protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeDockedWindow();
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        if (!IsDocked)
        {
            return false;
        }

        FocusBrowserContent();
        return true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (IsDocked)
        {
            FocusBrowserContent();
        }
    }

    public void Dock(IntPtr browserHwnd)
    {
        if (browserHwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Browser HWND cannot be zero.", nameof(browserHwnd));
        }

        if (!NativeMethods.IsWindow(browserHwnd))
        {
            throw new InvalidOperationException($"Browser HWND 0x{browserHwnd.ToInt64():X} is no longer valid.");
        }

        if (_hostHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hostHwnd))
        {
            throw new InvalidOperationException("Browser host HWND has not been created or is no longer valid.");
        }

        if (HasDockedWindow)
        {
            RuntimeLog.Info($"Dock requested while another HWND is tracked. Existing=0x{_browserHwnd.ToInt64():X}; New=0x{browserHwnd.ToInt64():X}");
            Detach();
        }

        if (!BrowserDockRegistry.TryClaim(browserHwnd, this))
        {
            throw new InvalidOperationException($"Browser HWND 0x{browserHwnd.ToInt64():X} is already docked by another workspace tile.");
        }

        _browserHwnd = browserHwnd;
        _lastWidth = -1;
        _lastHeight = -1;
        _originalStyle = NativeMethods.GetWindowLongPtr(browserHwnd, NativeMethods.GWL_STYLE);
        _originalExStyle = NativeMethods.GetWindowLongPtr(browserHwnd, NativeMethods.GWL_EXSTYLE);

        _originalPlacement = new NativeMethods.WINDOWPLACEMENT
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
        };
        _hasOriginalPlacement = NativeMethods.GetWindowPlacement(browserHwnd, ref _originalPlacement);

        RuntimeLog.Info($"Dock start. BrowserHWND=0x{browserHwnd.ToInt64():X}; HostHWND=0x{_hostHwnd.ToInt64():X}; Style=0x{_originalStyle.ToInt64():X}; ExStyle=0x{_originalExStyle.ToInt64():X}; PlacementSaved={_hasOriginalPlacement}");

        NativeMethods.ShowWindow(browserHwnd, NativeMethods.SW_RESTORE);

        var style = _originalStyle.ToInt64();
        style &= ~(NativeMethods.WS_POPUP |
                   NativeMethods.WS_CAPTION |
                   NativeMethods.WS_THICKFRAME |
                   NativeMethods.WS_MINIMIZEBOX |
                   NativeMethods.WS_MAXIMIZEBOX |
                   NativeMethods.WS_SYSMENU);
        style |= NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS;

        NativeMethods.SetWindowLongPtr(browserHwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

        Marshal.SetLastPInvokeError(0);
        var previousParent = NativeMethods.SetParent(browserHwnd, _hostHwnd);
        var setParentError = Marshal.GetLastPInvokeError();
        if (previousParent == IntPtr.Zero && setParentError != 0)
        {
            RestoreWindowStyleOnly(browserHwnd);
            ClearDockState();
            throw new Win32Exception(setParentError, "SetParent failed while docking Firefox window.");
        }

        if (!NativeMethods.SetWindowPos(
                browserHwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_FRAMECHANGED |
                NativeMethods.SWP_SHOWWINDOW))
        {
            RuntimeLog.Warn($"SetWindowPos(FRAMECHANGED) failed after dock. Win32={Marshal.GetLastWin32Error()}");
        }

        ResizeDockedWindow();
        FocusBrowserContent();
        RuntimeLog.Info($"Dock successful. BrowserHWND=0x{browserHwnd.ToInt64():X}; initial content-focus recovery requested.");
    }

    public void Detach()
    {
        if (!HasDockedWindow)
        {
            return;
        }

        var hwnd = _browserHwnd;
        RuntimeLog.Info($"Detach start. BrowserHWND=0x{hwnd.ToInt64():X}");

        if (!NativeMethods.IsWindow(hwnd))
        {
            RuntimeLog.Warn($"Detach skipped because BrowserHWND=0x{hwnd.ToInt64():X} no longer exists.");
            ClearDockState();
            return;
        }

        try
        {
            Marshal.SetLastPInvokeError(0);
            NativeMethods.SetParent(hwnd, IntPtr.Zero);
            var parentError = Marshal.GetLastPInvokeError();
            if (parentError != 0)
            {
                RuntimeLog.Warn($"SetParent(NULL) returned Win32={parentError} during detach.");
            }

            RestoreWindowStyleOnly(hwnd);

            if (!NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOZORDER |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_FRAMECHANGED |
                    NativeMethods.SWP_SHOWWINDOW))
            {
                RuntimeLog.Warn($"SetWindowPos(FRAMECHANGED) failed during detach. Win32={Marshal.GetLastWin32Error()}");
            }

            if (_hasOriginalPlacement)
            {
                var placement = _originalPlacement;
                if (!NativeMethods.SetWindowPlacement(hwnd, ref placement))
                {
                    RuntimeLog.Warn($"SetWindowPlacement failed during detach. Win32={Marshal.GetLastWin32Error()}");
                }
            }
            else
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNORMAL);
            }

            RuntimeLog.Info($"Detach completed. BrowserHWND=0x{hwnd.ToInt64():X}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"Detach failed for BrowserHWND=0x{hwnd.ToInt64():X}.", ex);
        }
        finally
        {
            ClearDockState();
        }
    }

    public bool CheckDockedWindowHealth()
    {
        if (!HasDockedWindow)
        {
            return false;
        }

        if (NativeMethods.IsWindow(_browserHwnd))
        {
            return true;
        }

        var lostHwnd = _browserHwnd;
        RuntimeLog.Warn($"Docked browser window disappeared. BrowserHWND=0x{lostHwnd.ToInt64():X}");
        ClearDockState();
        DockedWindowLost?.Invoke(this, EventArgs.Empty);
        return false;
    }

    public void ResizeDockedWindow()
    {
        if (!CheckDockedWindowHealth() || _hostHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hostHwnd))
        {
            return;
        }

        if (!NativeMethods.GetClientRect(_hostHwnd, out var rect))
        {
            RuntimeLog.Warn($"GetClientRect failed for host HWND=0x{_hostHwnd.ToInt64():X}; Win32={Marshal.GetLastWin32Error()}");
            return;
        }

        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);
        if (width == _lastWidth && height == _lastHeight)
        {
            return;
        }

        if (!NativeMethods.MoveWindow(_browserHwnd, 0, 0, width, height, true))
        {
            RuntimeLog.Warn($"MoveWindow failed. BrowserHWND=0x{_browserHwnd.ToInt64():X}; Size={width}x{height}; Win32={Marshal.GetLastWin32Error()}");
            return;
        }

        _lastWidth = width;
        _lastHeight = height;
        RuntimeLog.Debug($"Browser resize. HWND=0x{_browserHwnd.ToInt64():X}; Size={width}x{height}");
    }

    public void FocusBrowser()
        => FocusBrowserWindow(_browserHwnd, "browser-root");

    public void FocusBrowserContent()
    {
        if (!CheckDockedWindowHealth())
        {
            return;
        }

        var targetHwnd = FindPreferredContentHwnd();
        FocusBrowserWindow(targetHwnd, targetHwnd == _browserHwnd ? "browser-root-fallback" : "browser-content");
    }

    private void FocusBrowserWindow(IntPtr targetHwnd, string targetRole)
    {
        if (!CheckDockedWindowHealth())
        {
            return;
        }

        if (targetHwnd == IntPtr.Zero || !NativeMethods.IsWindow(targetHwnd))
        {
            targetHwnd = _browserHwnd;
            targetRole = "browser-root-fallback";
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var browserThread = NativeMethods.GetWindowThreadProcessId(targetHwnd, out var browserPid);
        if (browserThread == 0)
        {
            RuntimeLog.Warn($"Cannot focus target HWND=0x{targetHwnd.ToInt64():X}; Role={targetRole}; GetWindowThreadProcessId returned 0.");
            return;
        }

        var attached = false;

        try
        {
            if (currentThread != browserThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, browserThread, true);
                if (!attached)
                {
                    RuntimeLog.Warn($"AttachThreadInput failed. CurrentThread={currentThread}; BrowserThread={browserThread}; TargetHWND=0x{targetHwnd.ToInt64():X}; Role={targetRole}");
                }
            }

            Marshal.SetLastPInvokeError(0);
            var previousFocus = NativeMethods.SetFocus(targetHwnd);
            var focusError = Marshal.GetLastPInvokeError();
            var currentFocus = NativeMethods.GetFocus();
            var foreground = NativeMethods.GetForegroundWindow();
            RuntimeLog.Info($"Browser focus requested. BrowserHWND=0x{_browserHwnd.ToInt64():X}; TargetHWND=0x{targetHwnd.ToInt64():X}; Role={targetRole}; PID={browserPid}; CurrentThread={currentThread}; BrowserThread={browserThread}; ThreadInputAttached={attached}; PreviousFocus=0x{previousFocus.ToInt64():X}; CurrentFocus=0x{currentFocus.ToInt64():X}; Foreground=0x{foreground.ToInt64():X}; Win32={focusError}");
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, browserThread, false);
            }
        }
    }

    private IntPtr FindPreferredContentHwnd()
    {
        if (!CheckDockedWindowHealth())
        {
            return IntPtr.Zero;
        }

        IntPtr preferred = IntPtr.Zero;
        long preferredScore = long.MinValue;
        string preferredClass = string.Empty;

        NativeMethods.EnumChildWindows(
            _browserHwnd,
            (childHwnd, _) =>
            {
                if (!NativeMethods.IsWindow(childHwnd) || !NativeMethods.IsWindowVisible(childHwnd))
                {
                    return true;
                }

                var className = GetWindowClassName(childHwnd);
                var area = 0L;
                if (NativeMethods.GetWindowRect(childHwnd, out var rect))
                {
                    area = Math.Max(0L, (long)rect.Width * rect.Height);
                }

                var score = area;
                if (className.Contains("MozillaCompositorWindowClass", StringComparison.OrdinalIgnoreCase))
                {
                    score += 10_000_000_000L;
                }
                else if (className.Contains("Mozilla", StringComparison.OrdinalIgnoreCase))
                {
                    score += 5_000_000_000L;
                }

                if (score > preferredScore)
                {
                    preferred = childHwnd;
                    preferredScore = score;
                    preferredClass = className;
                }

                return true;
            },
            IntPtr.Zero);

        if (preferred == IntPtr.Zero)
        {
            RuntimeLog.Debug($"No visible Firefox child HWND selected for content focus. Falling back to root HWND=0x{_browserHwnd.ToInt64():X}.");
            return _browserHwnd;
        }

        RuntimeLog.Debug($"Firefox content-focus target selected. RootHWND=0x{_browserHwnd.ToInt64():X}; TargetHWND=0x{preferred.ToInt64():X}; Class='{preferredClass}'; Score={preferredScore}");
        return preferred;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    public async Task<bool> NavigateByKeyboardAsync(string url)
    {
        if (!CheckDockedWindowHealth())
        {
            return false;
        }

        var target = (url ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        FocusBrowser();
        await Task.Delay(60);

        SendVirtualKeyChord(NativeMethods.VK_CONTROL, NativeMethods.VK_L);
        await Task.Delay(80);
        SendUnicodeText(target);
        await Task.Delay(30);
        SendVirtualKey(NativeMethods.VK_RETURN);
        await Task.Delay(120);

        // Ctrl+L intentionally places Firefox keyboard focus in the address bar.
        // Move Win32 focus back toward the Firefox content surface after navigation
        // so later mouse clicks and text entry are not trapped in browser chrome.
        FocusBrowserContent();

        RuntimeLog.Info($"Browser keyboard navigation requested and content-focus recovery attempted. BrowserHWND=0x{_browserHwnd.ToInt64():X}; URL='{target}'");
        return true;
    }

    private static void SendVirtualKeyChord(ushort modifier, ushort key)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(modifier, false),
            CreateVirtualKeyInput(key, false),
            CreateVirtualKeyInput(key, true),
            CreateVirtualKeyInput(modifier, true)
        };

        SendInputs(inputs, $"virtual-key chord 0x{modifier:X}+0x{key:X}");
    }

    private static void SendVirtualKey(ushort key)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(key, false),
            CreateVirtualKeyInput(key, true)
        };

        SendInputs(inputs, $"virtual key 0x{key:X}");
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, false));
            inputs.Add(CreateUnicodeInput(character, true));
        }

        SendInputs(inputs.ToArray(), "Unicode URL text");
    }

    private static NativeMethods.INPUT CreateVirtualKeyInput(ushort key, bool keyUp)
        => new()
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Union = new NativeMethods.INPUTUNION
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = key,
                    ScanCode = 0,
                    Flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

    private static NativeMethods.INPUT CreateUnicodeInput(char character, bool keyUp)
        => new()
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Union = new NativeMethods.INPUTUNION
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

    private static void SendInputs(NativeMethods.INPUT[] inputs, string operation)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            RuntimeLog.Warn($"SendInput incomplete for {operation}. Requested={inputs.Length}; Sent={sent}; Win32={Marshal.GetLastWin32Error()}");
        }
    }

    private void RestoreWindowStyleOnly(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, _originalStyle);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, _originalExStyle);
    }

    private void ClearDockState()
    {
        var previousBrowserHwnd = _browserHwnd;
        BrowserDockRegistry.Release(previousBrowserHwnd, this);
        _browserHwnd = IntPtr.Zero;
        _originalStyle = IntPtr.Zero;
        _originalExStyle = IntPtr.Zero;
        _hasOriginalPlacement = false;
        _lastWidth = -1;
        _lastHeight = -1;
    }
}
