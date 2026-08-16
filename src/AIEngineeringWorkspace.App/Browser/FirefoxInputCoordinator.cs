using System.Runtime.InteropServices;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

/// <summary>
/// Diagnostic-only Firefox observer for rc21 launch-only control.
///
/// rc21 must not alter Firefox focus, parent/owner/style, geometry, visibility, input queues,
/// keyboard layout, or IME state. This class only records foreground/focus/HKL evidence for the
/// native top-level Firefox HWND returned by the launch/discovery service.
/// </summary>
internal static class FirefoxInputCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<IntPtr, InputSnapshot> LastSnapshots = new();
    private static IntPtr _activeBrowserHwnd;

    internal static IntPtr ActiveBrowserHwnd
    {
        get
        {
            lock (Sync)
            {
                if (_activeBrowserHwnd != IntPtr.Zero && !NativeMethods.IsWindow(_activeBrowserHwnd))
                    _activeBrowserHwnd = IntPtr.Zero;
                return _activeBrowserHwnd;
            }
        }
    }

    internal static void ObserveDock(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd)) return;
        workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
        var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
        RuntimeLog.Info(
            $"Firefox launch-only HWND observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; " +
            $"BrowserThread={browserThreadId}; PID={browserPid}; NativeInputMode=LaunchOnlyControl; SetParentUsed=False; OwnerMutation=False; StyleMutation=False; " +
            $"GeometryMutation=False; VisibilityMutation=False; AttachThreadInputUsed=False; SetFocusUsed=False; SetForegroundWindowUsed=False; InputLanguageSync=False; Reason='{reason}'");
    }

    internal static void MarkActiveRoot(IntPtr browserHwnd, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd)) return;
        lock (Sync)
        {
            var changed = _activeBrowserHwnd != browserHwnd;
            _activeBrowserHwnd = browserHwnd;
            if (changed)
                RuntimeLog.Info($"Active native Firefox top-level observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; NativeInputMode=LaunchOnlyControl; Reason='{reason}'");
        }
    }

    internal static void ClearActiveRoot(string reason)
    {
        lock (Sync)
        {
            if (_activeBrowserHwnd == IntPtr.Zero) return;
            var previous = _activeBrowserHwnd;
            _activeBrowserHwnd = IntPtr.Zero;
            RuntimeLog.Info($"Active native Firefox top-level cleared. PreviousBrowserHWND=0x{previous.ToInt64():X}; Reason='{reason}'");
        }
    }

    internal static void SuppressFocusMutation(IntPtr browserHwnd, string reason)
    {
        RuntimeLog.Info(
            $"Firefox focus mutation suppressed by rc21 launch-only control. BrowserHWND=0x{browserHwnd.ToInt64():X}; " +
            $"SetForegroundWindowUsed=False; SetFocusUsed=False; AttachThreadInputUsed=False; Reason='{reason}'");
    }

    internal static void FocusActiveRoot(uint workspaceThreadId, string reason) =>
        SuppressFocusMutation(ActiveBrowserHwnd, reason);

    internal static void FocusRoot(IntPtr browserHwnd, uint workspaceThreadId, string reason) =>
        SuppressFocusMutation(browserHwnd, reason);

    internal static void ActivateTopLevel(IntPtr browserHwnd, uint workspaceThreadId, string reason) =>
        SuppressFocusMutation(browserHwnd, reason);

    internal static void ObserveActiveInputLanguage(uint workspaceThreadId, string reason)
    {
        var active = ActiveBrowserHwnd;
        if (active == IntPtr.Zero)
        {
            RuntimeLog.Debug($"Active Firefox input-language observation skipped because no launch-only Browser is recorded. Reason='{reason}'");
            return;
        }

        lock (Sync)
        {
            if (!NativeMethods.IsWindow(active)) return;
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(active, out var browserPid);
            if (browserThreadId == 0) return;
            var workspaceHkl = NativeMethods.GetKeyboardLayout(workspaceThreadId);
            var browserHkl = NativeMethods.GetKeyboardLayout(browserThreadId);
            RuntimeLog.Info(
                $"Firefox input-language state observed without synchronization. BrowserHWND=0x{active.ToInt64():X}; BrowserThread={browserThreadId}; PID={browserPid}; " +
                $"WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(workspaceHkl)}; BrowserHKL={InputLanguageDiagnostics.FormatHkl(browserHkl)}; " +
                $"InputLanguageMismatch={workspaceHkl != browserHkl}; InputLanguageSyncPosted=False; NativeInputMode=LaunchOnlyControl; Reason='{reason}'");
        }
    }

    internal static void ProbeState(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd)) return;

        lock (Sync)
        {
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
            if (browserThreadId == 0) return;

            var snapshot = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
            if (snapshot.Foreground == browserHwnd) _activeBrowserHwnd = browserHwnd;
            if (LastSnapshots.TryGetValue(browserHwnd, out var previous) && previous == snapshot) return;

            LastSnapshots[browserHwnd] = snapshot;
            RuntimeLog.Info(
                $"Firefox launch-only input-state transition observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; PID={browserPid}; " +
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; NativeInputMode=LaunchOnlyControl; SetParentUsed=False; OwnerMutation=False; StyleMutation=False; GeometryMutation=False; VisibilityMutation=False; " +
                $"PersistentInputBridgeAttached=False; WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(snapshot.WorkspaceHkl)}; BrowserHKL={InputLanguageDiagnostics.FormatHkl(snapshot.BrowserHkl)}; " +
                $"InputLanguageMismatch={snapshot.WorkspaceHkl != snapshot.BrowserHkl}; Foreground=0x{snapshot.Foreground.ToInt64():X}; GuiInfoOk={snapshot.GuiInfoOk}; " +
                $"GuiActive=0x{snapshot.GuiActive.ToInt64():X}; GuiFocus=0x{snapshot.GuiFocus.ToInt64():X}; GuiCaret=0x{snapshot.GuiCaret.ToInt64():X}; Reason='{reason}'");
        }
    }

    internal static void Forget(IntPtr browserHwnd)
    {
        if (browserHwnd == IntPtr.Zero) return;
        lock (Sync)
        {
            LastSnapshots.Remove(browserHwnd);
            if (_activeBrowserHwnd == browserHwnd)
            {
                _activeBrowserHwnd = IntPtr.Zero;
                RuntimeLog.Info($"Active native Firefox top-level forgotten during launch-only tracking teardown. BrowserHWND=0x{browserHwnd.ToInt64():X}");
            }
        }
    }

    private static InputSnapshot CaptureSnapshot(IntPtr browserHwnd, uint workspaceThreadId, uint browserThreadId, uint browserPid)
    {
        var gui = new NativeMethods.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        Marshal.SetLastPInvokeError(0);
        var guiInfoOk = browserThreadId != 0 && NativeMethods.GetGUIThreadInfo(browserThreadId, ref gui);
        if (!guiInfoOk && browserThreadId != 0)
        {
            var error = Marshal.GetLastWin32Error();
            RuntimeLog.Debug($"GetGUIThreadInfo failed during Firefox launch-only input probe. BrowserThread={browserThreadId}; PID={browserPid}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={error}");
        }

        return new InputSnapshot(
            NativeMethods.GetKeyboardLayout(workspaceThreadId),
            browserThreadId == 0 ? IntPtr.Zero : NativeMethods.GetKeyboardLayout(browserThreadId),
            NativeMethods.GetForegroundWindow(),
            guiInfoOk,
            gui.hwndActive,
            gui.hwndFocus,
            gui.hwndCaret);
    }

    private readonly record struct InputSnapshot(
        IntPtr WorkspaceHkl,
        IntPtr BrowserHkl,
        IntPtr Foreground,
        bool GuiInfoOk,
        IntPtr GuiActive,
        IntPtr GuiFocus,
        IntPtr GuiCaret);
}
