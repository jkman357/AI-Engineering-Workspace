using System.Runtime.InteropServices;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

/// <summary>
/// Central observer/activation helper for rc20 zero-mutation Firefox pseudo-docking.
///
/// Firefox is not reparented and normal input is never proxied. The Workspace only tracks
/// which native top-level Firefox root is active, records HKL/GUI-thread diagnostics, and
/// offers an explicit recovery action that activates the top-level window through the
/// normal Win32 foreground-window path. AttachThreadInput and SetFocus are intentionally
/// not used in rc20. Firefox owner/style are also intentionally left untouched.
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
            $"Firefox pseudo-dock registered. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; " +
            $"BrowserThread={browserThreadId}; PID={browserPid}; SetParentUsed=False; PersistentBridge=False; AutomaticRootFocus=False; " +
            $"InputLanguageSync=False; NativeTopLevel=True; OwnerMutation=False; StyleMutation=False; Reason='{reason}'");
    }

    internal static void MarkActiveRoot(IntPtr browserHwnd, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd)) return;
        lock (Sync)
        {
            var changed = _activeBrowserHwnd != browserHwnd;
            _activeBrowserHwnd = browserHwnd;
            if (changed)
                RuntimeLog.Info($"Active native Firefox top-level observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; NativeInputMode=ZeroMutationTopLevelPseudoDock; Reason='{reason}'");
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

    internal static void FocusActiveRoot(uint workspaceThreadId, string reason)
    {
        var active = ActiveBrowserHwnd;
        if (active == IntPtr.Zero)
        {
            RuntimeLog.Debug($"Explicit Firefox activation skipped because no active pseudo-docked Browser is recorded. Reason='{reason}'");
            return;
        }
        ActivateTopLevel(active, workspaceThreadId, reason);
    }

    internal static void FocusRoot(IntPtr browserHwnd, uint workspaceThreadId, string reason) =>
        ActivateTopLevel(browserHwnd, workspaceThreadId, reason);

    internal static void ActivateTopLevel(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd))
        {
            RuntimeLog.Warn($"Explicit Firefox top-level activation skipped because BrowserHWND=0x{browserHwnd.ToInt64():X} is invalid. Reason='{reason}'");
            return;
        }

        lock (Sync)
        {
            _activeBrowserHwnd = browserHwnd;
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
            var before = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);

            NativeMethods.ShowWindow(browserHwnd, NativeMethods.SW_RESTORE);
            Marshal.SetLastPInvokeError(0);
            var foregroundRequested = NativeMethods.SetForegroundWindow(browserHwnd);
            var foregroundError = foregroundRequested ? 0 : Marshal.GetLastWin32Error();

            var after = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
            LastSnapshots[browserHwnd] = after;
            RuntimeLog.Info(
                $"Explicit native Firefox top-level activation completed. BrowserHWND=0x{browserHwnd.ToInt64():X}; ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; " +
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; SetParentUsed=False; OwnerMutation=False; StyleMutation=False; AttachThreadInputUsed=False; SetFocusUsed=False; " +
                $"SetForegroundWindowResult={foregroundRequested}; ForegroundWin32={foregroundError}; ForegroundBefore=0x{before.Foreground.ToInt64():X}; ForegroundAfter=0x{after.Foreground.ToInt64():X}; " +
                $"GuiActiveAfter=0x{after.GuiActive.ToInt64():X}; GuiFocusAfter=0x{after.GuiFocus.ToInt64():X}; WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(after.WorkspaceHkl)}; " +
                $"BrowserHKL={InputLanguageDiagnostics.FormatHkl(after.BrowserHkl)}; InputLanguageMismatch={after.WorkspaceHkl != after.BrowserHkl}; InputLanguageSyncPosted=False; Reason='{reason}'");
        }
    }

    internal static void ObserveActiveInputLanguage(uint workspaceThreadId, string reason)
    {
        var active = ActiveBrowserHwnd;
        if (active == IntPtr.Zero)
        {
            RuntimeLog.Debug($"Active Firefox input-language observation skipped because no active pseudo-docked Browser is recorded. Reason='{reason}'");
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
                $"InputLanguageMismatch={workspaceHkl != browserHkl}; InputLanguageSyncPosted=False; NativeInputMode=ZeroMutationTopLevelPseudoDock; Reason='{reason}'");
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
                $"Firefox pseudo-dock input-state transition observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; PID={browserPid}; " +
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; SetParentUsed=False; OwnerMutation=False; StyleMutation=False; PersistentInputBridgeAttached=False; NativeInputMode=ZeroMutationTopLevelPseudoDock; " +
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
                RuntimeLog.Info($"Active native Firefox top-level forgotten during pseudo-dock teardown. BrowserHWND=0x{browserHwnd.ToInt64():X}");
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
            RuntimeLog.Debug($"GetGUIThreadInfo failed during Firefox pseudo-dock input probe. BrowserThread={browserThreadId}; PID={browserPid}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={error}");
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
