using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using AIEngineeringWorkspace.Browser;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Controls;

/// <summary>
/// rc20 zero-mutation pseudo-dock baseline.
///
/// The HwndHost is only a native geometry anchor inside WPF. Firefox remains an otherwise
/// untouched native top-level HWND: no SetParent, no owner reassignment, and no style or
/// extended-style mutation. The Workspace only mirrors the anchor rectangle with SetWindowPos
/// and may show/hide the window with Workspace visibility. This isolates native Firefox
/// keyboard/TSF/IME behavior from owner/style experiments.
/// </summary>
public sealed class BrowserDockHost : HwndHost
{
    private IntPtr _hostHwnd;
    private IntPtr _browserHwnd;
    private IntPtr _workspaceTopLevelHwnd;
    private NativeMethods.WINDOWPLACEMENT _originalPlacement;
    private bool _hasOriginalPlacement;
    private int _lastLeft = int.MinValue;
    private int _lastTop = int.MinValue;
    private int _lastWidth = -1;
    private int _lastHeight = -1;
    private bool _pseudoDockVisible = true;
    private bool _wasForeground;
    private uint _workspaceInputThreadId;

    public event EventHandler? DockedWindowLost;
    public event EventHandler? NativeBrowserActivated;

    public bool HasDockedWindow => _browserHwnd != IntPtr.Zero;
    public bool IsDocked => _browserHwnd != IntPtr.Zero && NativeMethods.IsWindow(_browserHwnd);
    public IntPtr BrowserHwnd => _browserHwnd;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        const uint hostStyle = (uint)(NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS);
        _hostHwnd = NativeMethods.CreateWindowEx(0, "static", string.Empty, hostStyle, 0, 0, 1, 1,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hostHwnd == IntPtr.Zero) NativeMethods.ThrowLastWin32Error("CreateWindowEx for browser geometry anchor failed");

        _workspaceInputThreadId = NativeMethods.GetCurrentThreadId();
        _workspaceTopLevelHwnd = NativeMethods.GetAncestor(_hostHwnd, NativeMethods.GA_ROOT);
        RuntimeLog.Info($"Browser pseudo-dock anchor HWND created: 0x{_hostHwnd.ToInt64():X}; WorkspaceTopLevelHWND=0x{_workspaceTopLevelHwnd.ToInt64():X}; WorkspaceInputThread={_workspaceInputThreadId}");
        return new HandleRef(this, _hostHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        try { Detach(); }
        finally
        {
            if (hwnd.Handle != IntPtr.Zero && NativeMethods.IsWindow(hwnd.Handle) && !NativeMethods.DestroyWindow(hwnd.Handle))
                RuntimeLog.Warn($"DestroyWindow failed for pseudo-dock anchor HWND=0x{hwnd.Handle.ToInt64():X}; Win32={Marshal.GetLastWin32Error()}");
            _hostHwnd = IntPtr.Zero;
            _workspaceTopLevelHwnd = IntPtr.Zero;
        }
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The anchor is not Firefox's parent in rc20. Firefox owner/style are untouched, so
        // normal Firefox mouse/IME messages do not traverse this WndProc. Diagnostics here are
        // limited to the WPF-owned geometry anchor.
        InputLanguageDiagnostics.LogWindowMessage("BrowserPseudoDockAnchor", hwnd, msg, wParam, lParam);
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeDockedWindow();
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        if (!IsDocked) return false;
        FocusBrowser("BrowserDockHost.TabIntoCore");
        return true;
    }

    public void Dock(IntPtr browserHwnd)
    {
        if (browserHwnd == IntPtr.Zero) throw new ArgumentException("Browser HWND cannot be zero.", nameof(browserHwnd));
        if (!NativeMethods.IsWindow(browserHwnd)) throw new InvalidOperationException($"Browser HWND 0x{browserHwnd.ToInt64():X} is no longer valid.");
        if (_hostHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hostHwnd)) throw new InvalidOperationException("Browser pseudo-dock anchor HWND has not been created or is no longer valid.");

        if (HasDockedWindow)
        {
            RuntimeLog.Info($"Pseudo-dock requested while another HWND is tracked. Existing=0x{_browserHwnd.ToInt64():X}; New=0x{browserHwnd.ToInt64():X}");
            Detach();
        }
        if (!BrowserDockRegistry.TryClaim(browserHwnd, this))
            throw new InvalidOperationException($"Browser HWND 0x{browserHwnd.ToInt64():X} is already assigned to another workspace tile.");

        _browserHwnd = browserHwnd;
        _lastLeft = _lastTop = int.MinValue;
        _lastWidth = _lastHeight = -1;
        _wasForeground = false;
        _originalPlacement = new NativeMethods.WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        _hasOriginalPlacement = NativeMethods.GetWindowPlacement(browserHwnd, ref _originalPlacement);

        RuntimeLog.Info($"Zero-mutation pseudo-dock start. BrowserHWND=0x{browserHwnd.ToInt64():X}; AnchorHWND=0x{_hostHwnd.ToInt64():X}; PlacementSaved={_hasOriginalPlacement}; SetParentUsed=False; OwnerMutation=False; StyleMutation=False; ExtendedStyleMutation=False");

        // rc20 control group: do not alter Firefox parent, owner, GWL_STYLE, or GWL_EXSTYLE.
        // The Firefox title bar/frame intentionally remain visible. Geometry is the only dock
        // transformation and is performed through SetWindowPos(... SWP_NOACTIVATE ...).
        ResizeDockedWindow();
        FirefoxInputCoordinator.ObserveDock(browserHwnd, _workspaceInputThreadId, "BrowserDockHost.ZeroMutationPseudoDock");
        RuntimeLog.Info($"Zero-mutation pseudo-dock successful. BrowserHWND=0x{browserHwnd.ToInt64():X}; SetParentUsed=False; OwnerMutation=False; StyleMutation=False; InputMutation=False; Firefox remains an unmodified native top-level window except geometry/visibility.");
    }

    public void Detach()
    {
        if (!HasDockedWindow) return;
        var hwnd = _browserHwnd;
        RuntimeLog.Info($"Pseudo-dock detach start. BrowserHWND=0x{hwnd.ToInt64():X}");
        if (!NativeMethods.IsWindow(hwnd))
        {
            RuntimeLog.Warn($"Pseudo-dock detach skipped because BrowserHWND=0x{hwnd.ToInt64():X} no longer exists.");
            ClearDockState();
            return;
        }

        try
        {
            // Parent/owner/style were never changed in rc20; only restore the pre-dock placement.
            if (_hasOriginalPlacement)
            {
                var placement = _originalPlacement;
                if (!NativeMethods.SetWindowPlacement(hwnd, ref placement))
                    RuntimeLog.Warn($"SetWindowPlacement failed during pseudo-dock detach. Win32={Marshal.GetLastWin32Error()}");
            }
            else NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNORMAL);

            RuntimeLog.Info($"Pseudo-dock detach completed. BrowserHWND=0x{hwnd.ToInt64():X}");
        }
        catch (Exception ex) { RuntimeLog.Error($"Pseudo-dock detach failed for BrowserHWND=0x{hwnd.ToInt64():X}.", ex); }
        finally { ClearDockState(); }
    }

    public bool CheckDockedWindowHealth()
    {
        if (!HasDockedWindow) return false;
        if (NativeMethods.IsWindow(_browserHwnd)) return true;
        var lostHwnd = _browserHwnd;
        RuntimeLog.Warn($"Pseudo-docked browser window disappeared. BrowserHWND=0x{lostHwnd.ToInt64():X}");
        ClearDockState();
        DockedWindowLost?.Invoke(this, EventArgs.Empty);
        return false;
    }

    /// <summary>
    /// Historical API name retained for callers. In rc20 this synchronizes an otherwise
    /// untouched native top-level Firefox window to the WPF anchor screen rectangle.
    /// </summary>
    public void ResizeDockedWindow()
    {
        if (!CheckDockedWindowHealth() || _hostHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hostHwnd)) return;

        if (!_pseudoDockVisible || !NativeMethods.IsWindowVisible(_hostHwnd))
        {
            NativeMethods.ShowWindow(_browserHwnd, NativeMethods.SW_HIDE);
            return;
        }

        if (!NativeMethods.GetWindowRect(_hostHwnd, out var rect))
        {
            RuntimeLog.Warn($"GetWindowRect failed for pseudo-dock anchor HWND=0x{_hostHwnd.ToInt64():X}; Win32={Marshal.GetLastWin32Error()}");
            return;
        }

        var left = rect.Left;
        var top = rect.Top;
        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);
        if (left == _lastLeft && top == _lastTop && width == _lastWidth && height == _lastHeight) return;

        if (!NativeMethods.SetWindowPos(_browserHwnd, IntPtr.Zero, left, top, width, height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
        {
            RuntimeLog.Warn($"SetWindowPos pseudo-dock sync failed. BrowserHWND=0x{_browserHwnd.ToInt64():X}; Rect={left},{top},{width}x{height}; Win32={Marshal.GetLastWin32Error()}");
            return;
        }

        _lastLeft = left;
        _lastTop = top;
        _lastWidth = width;
        _lastHeight = height;
        RuntimeLog.Debug($"Zero-mutation pseudo-dock geometry synchronized. BrowserHWND=0x{_browserHwnd.ToInt64():X}; ScreenRect={left},{top},{width}x{height}; SetParentUsed=False; OwnerMutation=False; StyleMutation=False");
    }

    public void FinalizeResizeRepaint()
    {
        if (!CheckDockedWindowHealth()) return;
        _lastLeft = _lastTop = int.MinValue;
        _lastWidth = _lastHeight = -1;
        ResizeDockedWindow();
        RequestBrowserRedraw(true);
        RuntimeLog.Debug($"Pseudo-dock final repaint completed. BrowserHWND=0x{_browserHwnd.ToInt64():X}; AnchorHWND=0x{_hostHwnd.ToInt64():X}");
    }

    public void SetPseudoDockVisible(bool visible)
    {
        _pseudoDockVisible = visible;
        if (!CheckDockedWindowHealth()) return;
        if (!visible)
        {
            NativeMethods.ShowWindow(_browserHwnd, NativeMethods.SW_HIDE);
            RuntimeLog.Debug($"Pseudo-dock hidden with pane/window visibility. BrowserHWND=0x{_browserHwnd.ToInt64():X}");
            return;
        }

        _lastLeft = _lastTop = int.MinValue;
        _lastWidth = _lastHeight = -1;
        ResizeDockedWindow();
    }

    private void RequestBrowserRedraw(bool immediate)
    {
        if (_browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_browserHwnd)) return;
        var flags = NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ALLCHILDREN | NativeMethods.RDW_FRAME;
        if (immediate) flags |= NativeMethods.RDW_ERASE | NativeMethods.RDW_ERASENOW | NativeMethods.RDW_UPDATENOW;
        NativeMethods.InvalidateRect(_browserHwnd, IntPtr.Zero, true);
        NativeMethods.RedrawWindow(_browserHwnd, IntPtr.Zero, IntPtr.Zero, flags);
        if (immediate) NativeMethods.UpdateWindow(_browserHwnd);
        InvalidateVisual();
    }

    public void FocusBrowser() => FocusBrowser("BrowserDockHost.FocusBrowser");

    public void FocusBrowser(string reason)
    {
        if (!CheckDockedWindowHealth()) return;
        FirefoxInputCoordinator.ActivateTopLevel(
            _browserHwnd,
            _workspaceInputThreadId,
            string.IsNullOrWhiteSpace(reason) ? "BrowserDockHost.FocusBrowser" : reason);
    }

    public void FocusBrowserContent() => FocusBrowser();

    public void MarkActive(string reason)
    {
        if (!CheckDockedWindowHealth()) return;
        FirefoxInputCoordinator.MarkActiveRoot(_browserHwnd, string.IsNullOrWhiteSpace(reason) ? "BrowserDockHost.MarkActive" : reason);
    }

    public void ProbeInputState(string reason)
    {
        if (!CheckDockedWindowHealth()) return;
        var isForeground = NativeMethods.GetForegroundWindow() == _browserHwnd;
        if (isForeground && !_wasForeground)
        {
            FirefoxInputCoordinator.MarkActiveRoot(_browserHwnd, $"{reason}.ForegroundObserved");
            NativeBrowserActivated?.Invoke(this, EventArgs.Empty);
        }
        _wasForeground = isForeground;
        FirefoxInputCoordinator.ProbeState(_browserHwnd, _workspaceInputThreadId, reason);
    }

    public async Task<bool> NavigateByKeyboardAsync(string url)
    {
        if (!CheckDockedWindowHealth()) return false;
        var target = (url ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return false;

        FocusBrowser("BrowserDockHost.NavigateByKeyboardAsync");
        await Task.Delay(100);
        SendVirtualKeyChord(NativeMethods.VK_CONTROL, NativeMethods.VK_L);
        await Task.Delay(80);
        SendUnicodeText(target);
        await Task.Delay(30);
        SendVirtualKey(NativeMethods.VK_RETURN);
        await Task.Delay(120);
        RuntimeLog.Info($"Browser keyboard navigation requested. BrowserHWND=0x{_browserHwnd.ToInt64():X}; URL='{target}'");
        return true;
    }

    private static void SendVirtualKeyChord(ushort modifier, ushort key)
    {
        var inputs = new[] { CreateVirtualKeyInput(modifier, false), CreateVirtualKeyInput(key, false), CreateVirtualKeyInput(key, true), CreateVirtualKeyInput(modifier, true) };
        SendInputs(inputs, $"virtual-key chord 0x{modifier:X}+0x{key:X}");
    }

    private static void SendVirtualKey(ushort key)
    {
        var inputs = new[] { CreateVirtualKeyInput(key, false), CreateVirtualKeyInput(key, true) };
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

    private static NativeMethods.INPUT CreateVirtualKeyInput(ushort key, bool keyUp) => new()
    {
        Type = NativeMethods.INPUT_KEYBOARD,
        Union = new NativeMethods.INPUTUNION { Keyboard = new NativeMethods.KEYBDINPUT { VirtualKey = key, ScanCode = 0, Flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0, Time = 0, ExtraInfo = UIntPtr.Zero } }
    };

    private static NativeMethods.INPUT CreateUnicodeInput(char character, bool keyUp) => new()
    {
        Type = NativeMethods.INPUT_KEYBOARD,
        Union = new NativeMethods.INPUTUNION { Keyboard = new NativeMethods.KEYBDINPUT { VirtualKey = 0, ScanCode = character, Flags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0), Time = 0, ExtraInfo = UIntPtr.Zero } }
    };

    private static void SendInputs(NativeMethods.INPUT[] inputs, string operation)
    {
        if (inputs.Length == 0) return;
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length) RuntimeLog.Warn($"SendInput incomplete for {operation}. Requested={inputs.Length}; Sent={sent}; Win32={Marshal.GetLastWin32Error()}");
    }

    private void ClearDockState()
    {
        var previousBrowserHwnd = _browserHwnd;
        FirefoxInputCoordinator.Forget(previousBrowserHwnd);
        BrowserDockRegistry.Release(previousBrowserHwnd, this);
        _browserHwnd = IntPtr.Zero;
        _hasOriginalPlacement = false;
        _lastLeft = _lastTop = int.MinValue;
        _lastWidth = _lastHeight = -1;
        _wasForeground = false;
    }
}
