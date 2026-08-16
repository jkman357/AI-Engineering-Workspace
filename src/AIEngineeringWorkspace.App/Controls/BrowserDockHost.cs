using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using AIEngineeringWorkspace.Browser;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Controls;

/// <summary>
/// rc21 Firefox launch-only control.
///
/// The HwndHost remains only as the Browser pane's WPF placeholder. After Firefox is launched
/// and its native top-level HWND is discovered, the Workspace records that HWND for lifecycle
/// ownership and diagnostics only. rc21 performs no Firefox parent/owner/style/geometry/
/// visibility/focus/input mutation.
/// </summary>
public sealed class BrowserDockHost : HwndHost
{
    private IntPtr _hostHwnd;
    private IntPtr _browserHwnd;
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
        if (_hostHwnd == IntPtr.Zero) NativeMethods.ThrowLastWin32Error("CreateWindowEx for Browser launch-only placeholder failed");

        _workspaceInputThreadId = NativeMethods.GetCurrentThreadId();
        RuntimeLog.Info($"Browser launch-only placeholder HWND created: 0x{_hostHwnd.ToInt64():X}; WorkspaceInputThread={_workspaceInputThreadId}");
        return new HandleRef(this, _hostHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        try { Detach(); }
        finally
        {
            if (hwnd.Handle != IntPtr.Zero && NativeMethods.IsWindow(hwnd.Handle) && !NativeMethods.DestroyWindow(hwnd.Handle))
                RuntimeLog.Warn($"DestroyWindow failed for Browser launch-only placeholder HWND=0x{hwnd.Handle.ToInt64():X}; Win32={Marshal.GetLastWin32Error()}");
            _hostHwnd = IntPtr.Zero;
        }
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        InputLanguageDiagnostics.LogWindowMessage("BrowserLaunchOnlyPlaceholder", hwnd, msg, wParam, lParam);
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
    {
        // rc21 control: intentionally do not translate WPF pane geometry into Firefox geometry.
        base.OnWindowPositionChanged(rcBoundingBox);
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        RuntimeLog.Debug("TabIntoCore ignored by rc21 launch-only control; Firefox focus is never mutated by Workspace.");
        return false;
    }

    public void Dock(IntPtr browserHwnd)
    {
        if (browserHwnd == IntPtr.Zero) throw new ArgumentException("Browser HWND cannot be zero.", nameof(browserHwnd));
        if (!NativeMethods.IsWindow(browserHwnd)) throw new InvalidOperationException($"Browser HWND 0x{browserHwnd.ToInt64():X} is no longer valid.");

        if (HasDockedWindow)
        {
            RuntimeLog.Info($"Launch-only tracking requested while another HWND is tracked. Existing=0x{_browserHwnd.ToInt64():X}; New=0x{browserHwnd.ToInt64():X}");
            Detach();
        }
        if (!BrowserDockRegistry.TryClaim(browserHwnd, this))
            throw new InvalidOperationException($"Browser HWND 0x{browserHwnd.ToInt64():X} is already assigned to another workspace tile.");

        _browserHwnd = browserHwnd;
        _wasForeground = false;
        FirefoxInputCoordinator.ObserveDock(browserHwnd, _workspaceInputThreadId, "BrowserDockHost.LaunchOnlyTrack");
        RuntimeLog.Info(
            $"Firefox launch-only tracking active. BrowserHWND=0x{browserHwnd.ToInt64():X}; " +
            "SetParentUsed=False; OwnerMutation=False; StyleMutation=False; GeometryMutation=False; " +
            "VisibilityMutation=False; FocusMutation=False; InputMutation=False; Workspace will not reposition, hide, show, focus, or reparent Firefox.");
    }

    public void Detach()
    {
        if (!HasDockedWindow) return;
        var hwnd = _browserHwnd;
        RuntimeLog.Info($"Launch-only tracking detach. BrowserHWND=0x{hwnd.ToInt64():X}; Firefox window is not modified.");
        ClearDockState();
    }

    public bool CheckDockedWindowHealth()
    {
        if (!HasDockedWindow) return false;
        if (NativeMethods.IsWindow(_browserHwnd)) return true;
        var lostHwnd = _browserHwnd;
        RuntimeLog.Warn($"Launch-only tracked Firefox window disappeared. BrowserHWND=0x{lostHwnd.ToInt64():X}");
        ClearDockState();
        DockedWindowLost?.Invoke(this, EventArgs.Empty);
        return false;
    }

    // Historical API names are retained so the rest of the Workspace can stay unchanged while
    // rc21 deliberately disables all Firefox geometry/visibility manipulation.
    public void ResizeDockedWindow() { }
    public void FinalizeResizeRepaint() { }
    public void SetPseudoDockVisible(bool visible) { }

    public void FocusBrowser() => FocusBrowser("BrowserDockHost.FocusBrowser");

    public void FocusBrowser(string reason)
    {
        if (!CheckDockedWindowHealth()) return;
        FirefoxInputCoordinator.SuppressFocusMutation(_browserHwnd, reason);
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

    public Task<bool> NavigateByKeyboardAsync(string url)
    {
        RuntimeLog.Warn("Workspace-driven Firefox keyboard navigation is disabled by the rc21 launch-only control. Use Firefox directly.");
        return Task.FromResult(false);
    }

    private void ClearDockState()
    {
        var previousBrowserHwnd = _browserHwnd;
        FirefoxInputCoordinator.Forget(previousBrowserHwnd);
        BrowserDockRegistry.Release(previousBrowserHwnd, this);
        _browserHwnd = IntPtr.Zero;
        _wasForeground = false;
    }
}
