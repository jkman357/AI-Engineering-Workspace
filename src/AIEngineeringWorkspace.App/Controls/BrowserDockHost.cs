using System.ComponentModel;
using System.Runtime.InteropServices;
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
        RuntimeLog.Info($"Dock successful. BrowserHWND=0x{browserHwnd.ToInt64():X}");
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
    {
        if (!CheckDockedWindowHealth())
        {
            return;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var browserThread = NativeMethods.GetWindowThreadProcessId(_browserHwnd, out var browserPid);
        if (browserThread == 0)
        {
            RuntimeLog.Warn($"Cannot focus BrowserHWND=0x{_browserHwnd.ToInt64():X}; GetWindowThreadProcessId returned 0.");
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
                    RuntimeLog.Warn($"AttachThreadInput failed. CurrentThread={currentThread}; BrowserThread={browserThread}");
                }
            }

            Marshal.SetLastPInvokeError(0);
            NativeMethods.SetFocus(_browserHwnd);
            var focusError = Marshal.GetLastPInvokeError();
            RuntimeLog.Info($"Browser focus requested. BrowserHWND=0x{_browserHwnd.ToInt64():X}; PID={browserPid}; CurrentThread={currentThread}; BrowserThread={browserThread}; ThreadInputAttached={attached}; Win32={focusError}");
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, browserThread, false);
            }
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
