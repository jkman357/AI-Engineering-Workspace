using System.Runtime.InteropServices;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

/// <summary>
/// Central observer/recovery helper for docked Firefox native input.
///
/// rc18 deliberately keeps normal mouse/keyboard handling in pass-through mode:
/// Browser clicks are observed to track the active pane, but the Workspace does not call
/// SetFocus, AttachThreadInput, change Firefox HKLs, or guess Firefox child HWNDs during
/// ordinary interaction. Firefox remains responsible for its own address-bar, content,
/// text-edit and IME focus transitions.
///
/// Cross-thread SetFocus is retained only as an explicit recovery primitive for TabIntoCore,
/// the Focus toolbar button, and Workspace-driven keyboard navigation. That recovery uses a
/// temporary AttachThreadInput transaction and always detaches immediately in finally.
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
                {
                    _activeBrowserHwnd = IntPtr.Zero;
                }
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
            $"Firefox dock registered for native input pass-through. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; " +
            $"BrowserThread={browserThreadId}; PID={browserPid}; PersistentBridge=False; AutomaticRootFocus=False; InputLanguageSync=False; Reason='{reason}'");
    }

    internal static void MarkActiveRoot(IntPtr browserHwnd, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd)) return;
        lock (Sync)
        {
            var changed = _activeBrowserHwnd != browserHwnd;
            _activeBrowserHwnd = browserHwnd;
            if (changed)
            {
                RuntimeLog.Info($"Active Firefox root observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; NativeInputMode=PassThrough; Reason='{reason}'");
            }
        }
    }

    internal static void ClearActiveRoot(string reason)
    {
        lock (Sync)
        {
            if (_activeBrowserHwnd == IntPtr.Zero) return;
            var previous = _activeBrowserHwnd;
            _activeBrowserHwnd = IntPtr.Zero;
            RuntimeLog.Info($"Active Firefox root cleared. PreviousBrowserHWND=0x{previous.ToInt64():X}; Reason='{reason}'");
        }
    }

    internal static void FocusActiveRoot(uint workspaceThreadId, string reason)
    {
        var active = ActiveBrowserHwnd;
        if (active == IntPtr.Zero)
        {
            RuntimeLog.Debug($"Explicit Firefox focus recovery skipped because no active docked Browser is recorded. Reason='{reason}'");
            return;
        }
        FocusRoot(active, workspaceThreadId, reason);
    }

    internal static void FocusRoot(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd))
        {
            RuntimeLog.Warn($"Explicit Firefox focus recovery skipped because BrowserHWND=0x{browserHwnd.ToInt64():X} is invalid. Reason='{reason}'");
            return;
        }

        lock (Sync)
        {
            _activeBrowserHwnd = browserHwnd;
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
            if (browserThreadId == 0)
            {
                RuntimeLog.Warn($"Explicit Firefox focus recovery could not resolve BrowserThread. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; Reason='{reason}'");
                return;
            }

            var before = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
            var bridgeRequired = workspaceThreadId != browserThreadId;
            var bridgeAttached = false;
            var bridgeDetached = !bridgeRequired;
            var attachError = 0;
            var detachError = 0;
            IntPtr previousFocus = IntPtr.Zero;
            IntPtr currentFocusWhileAttached = IntPtr.Zero;
            var focusError = 0;

            try
            {
                if (bridgeRequired)
                {
                    Marshal.SetLastPInvokeError(0);
                    bridgeAttached = NativeMethods.AttachThreadInput(workspaceThreadId, browserThreadId, true);
                    attachError = bridgeAttached ? 0 : Marshal.GetLastWin32Error();
                    if (!bridgeAttached)
                    {
                        RuntimeLog.Warn(
                            $"Temporary Firefox recovery bridge attach failed. WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; " +
                            $"BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={attachError}; Reason='{reason}'");
                        return;
                    }
                }

                Marshal.SetLastPInvokeError(0);
                previousFocus = NativeMethods.SetFocus(browserHwnd);
                focusError = Marshal.GetLastPInvokeError();
                currentFocusWhileAttached = NativeMethods.GetFocus();
            }
            finally
            {
                if (bridgeRequired && bridgeAttached)
                {
                    Marshal.SetLastPInvokeError(0);
                    bridgeDetached = NativeMethods.AttachThreadInput(workspaceThreadId, browserThreadId, false);
                    detachError = bridgeDetached ? 0 : Marshal.GetLastWin32Error();
                    if (!bridgeDetached)
                    {
                        RuntimeLog.Warn(
                            $"Temporary Firefox recovery bridge detach failed. WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; " +
                            $"BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={detachError}; Reason='{reason}'");
                    }
                }
            }

            var after = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
            LastSnapshots[browserHwnd] = after;
            RuntimeLog.Info(
                $"Explicit Firefox root focus recovery completed. BrowserHWND=0x{browserHwnd.ToInt64():X}; ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; " +
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; BridgeRequired={bridgeRequired}; " +
                $"TemporaryInputBridgeAttached={bridgeAttached}; TemporaryInputBridgeDetached={bridgeDetached}; AttachWin32={attachError}; DetachWin32={detachError}; " +
                $"PreviousFocus=0x{previousFocus.ToInt64():X}; FocusWhileAttached=0x{currentFocusWhileAttached.ToInt64():X}; FocusWin32={focusError}; " +
                $"ForegroundAfter=0x{after.Foreground.ToInt64():X}; GuiActiveAfter=0x{after.GuiActive.ToInt64():X}; GuiFocusAfter=0x{after.GuiFocus.ToInt64():X}; " +
                $"WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(after.WorkspaceHkl)}; BrowserHKL={InputLanguageDiagnostics.FormatHkl(after.BrowserHkl)}; " +
                $"InputLanguageMismatch={after.WorkspaceHkl != after.BrowserHkl}; InputLanguageSyncPosted=False; Reason='{reason}'");
        }
    }

    internal static void ObserveActiveInputLanguage(uint workspaceThreadId, string reason)
    {
        var active = ActiveBrowserHwnd;
        if (active == IntPtr.Zero)
        {
            RuntimeLog.Debug($"Active Firefox input-language observation skipped because no active docked Browser is recorded. Reason='{reason}'");
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
                $"InputLanguageMismatch={workspaceHkl != browserHkl}; InputLanguageSyncPosted=False; NativeInputMode=PassThrough; Reason='{reason}'");
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
            if (LastSnapshots.TryGetValue(browserHwnd, out var previous) && previous == snapshot) return;

            LastSnapshots[browserHwnd] = snapshot;
            RuntimeLog.Info(
                $"Firefox input-state transition observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; PID={browserPid}; " +
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PersistentInputBridgeAttached=False; NativeInputMode=PassThrough; " +
                $"WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(snapshot.WorkspaceHkl)}; BrowserHKL={InputLanguageDiagnostics.FormatHkl(snapshot.BrowserHkl)}; " +
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
                RuntimeLog.Info($"Active Firefox root forgotten during dock teardown. BrowserHWND=0x{browserHwnd.ToInt64():X}");
            }
        }
    }

    private static InputSnapshot CaptureSnapshot(IntPtr browserHwnd, uint workspaceThreadId, uint browserThreadId, uint browserPid)
    {
        var gui = new NativeMethods.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        Marshal.SetLastPInvokeError(0);
        var guiInfoOk = NativeMethods.GetGUIThreadInfo(browserThreadId, ref gui);
        if (!guiInfoOk)
        {
            var error = Marshal.GetLastWin32Error();
            RuntimeLog.Debug($"GetGUIThreadInfo failed during Firefox input probe. BrowserThread={browserThreadId}; PID={browserPid}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={error}");
        }

        return new InputSnapshot(
            NativeMethods.GetKeyboardLayout(workspaceThreadId),
            NativeMethods.GetKeyboardLayout(browserThreadId),
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
