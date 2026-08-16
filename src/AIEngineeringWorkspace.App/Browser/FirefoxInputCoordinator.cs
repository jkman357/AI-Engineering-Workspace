using System.Runtime.InteropServices;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

/// <summary>
/// Central authority for docked Firefox native input ownership.
///
/// rc17 owns AttachThreadInput at the unique Firefox input-thread level rather than at
/// individual Browser-pane level. The first dock on a WorkspaceThread/FirefoxThread pair
/// acquires one persistent bridge; additional panes on the same Firefox thread only raise
/// its reference count. The bridge is released only after the last registered dock on that
/// thread pair is removed. Browser switching therefore changes only the Firefox root HWND
/// focus while the shared input-queue relationship remains stable.
///
/// Input-language synchronization remains request-based only. This coordinator never
/// synthesizes IME composition or guesses Firefox compositor/content child HWNDs.
/// </summary>
internal static class FirefoxInputCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<IntPtr, InputSnapshot> LastSnapshots = new();
    private static readonly Dictionary<IntPtr, DockRegistration> DockRegistrations = new();
    private static readonly Dictionary<BridgeKey, ThreadBridgeState> ThreadBridges = new();
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

    internal static bool RegisterDock(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd))
        {
            RuntimeLog.Warn($"Firefox dock registration skipped because BrowserHWND=0x{browserHwnd.ToInt64():X} is invalid. Reason='{reason}'");
            return false;
        }

        lock (Sync)
        {
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
            if (browserThreadId == 0)
            {
                RuntimeLog.Warn($"Firefox dock registration could not resolve BrowserThread. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; Reason='{reason}'");
                return false;
            }

            var key = new BridgeKey(workspaceThreadId, browserThreadId);
            if (DockRegistrations.TryGetValue(browserHwnd, out var existing))
            {
                if (existing.Key == key)
                {
                    var state = GetOrCreateBridgeStateLocked(key, browserPid, browserHwnd, reason);
                    EnsureBridgeAttachedLocked(key, state, browserPid, browserHwnd, $"{reason}.ExistingRegistration");
                    return !state.BridgeRequired || state.Attached;
                }
                ReleaseRegistrationLocked(browserHwnd, existing, $"{reason}.RegistrationChanged");
            }

            var bridge = GetOrCreateBridgeStateLocked(key, browserPid, browserHwnd, reason);
            bridge.RefCount++;
            DockRegistrations[browserHwnd] = new DockRegistration(key, browserPid);
            EnsureBridgeAttachedLocked(key, bridge, browserPid, browserHwnd, reason);

            RuntimeLog.Info(
                $"Persistent Firefox input bridge registration acquired. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; " +
                $"BrowserThread={browserThreadId}; PID={browserPid}; BridgeRequired={bridge.BridgeRequired}; BridgeAttached={bridge.Attached}; " +
                $"BridgeRefCount={bridge.RefCount}; RegisteredDockCount={DockRegistrations.Count}; Reason='{reason}'");
            return !bridge.BridgeRequired || bridge.Attached;
        }
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
                RuntimeLog.Info($"Active Firefox root changed. BrowserHWND=0x{browserHwnd.ToInt64():X}; Reason='{reason}'");
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
            RuntimeLog.Debug($"Active Firefox focus recovery skipped because no active docked Browser is recorded. Reason='{reason}'");
            return;
        }
        FocusRoot(active, workspaceThreadId, reason);
    }

    internal static void FocusRoot(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd))
        {
            RuntimeLog.Warn($"Firefox focus handoff skipped because BrowserHWND=0x{browserHwnd.ToInt64():X} is invalid. Reason='{reason}'");
            return;
        }

        lock (Sync)
        {
            _activeBrowserHwnd = browserHwnd;
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
            if (browserThreadId == 0)
            {
                RuntimeLog.Warn($"Firefox focus handoff could not resolve BrowserThread. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; Reason='{reason}'");
                return;
            }

            var key = new BridgeKey(workspaceThreadId, browserThreadId);
            if (!DockRegistrations.TryGetValue(browserHwnd, out var registration) || registration.Key != key)
            {
                RuntimeLog.Warn($"Firefox focus handoff found no matching persistent bridge registration. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; Reason='{reason}'");
                RegisterDock(browserHwnd, workspaceThreadId, $"{reason}.LazyRegister");
            }

            if (!ThreadBridges.TryGetValue(key, out var bridge))
            {
                RuntimeLog.Warn($"Firefox focus handoff has no thread bridge state after registration. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; Reason='{reason}'");
                return;
            }
            EnsureBridgeAttachedLocked(key, bridge, browserPid, browserHwnd, reason);

            var before = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
            Marshal.SetLastPInvokeError(0);
            var previousFocus = NativeMethods.SetFocus(browserHwnd);
            var focusError = Marshal.GetLastPInvokeError();
            var currentFocus = NativeMethods.GetFocus();
            var foreground = NativeMethods.GetForegroundWindow();

            var after = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
            LastSnapshots[browserHwnd] = after;
            var layoutSyncPosted = false;
            if (after.WorkspaceHkl != IntPtr.Zero && after.WorkspaceHkl != after.BrowserHkl)
            {
                layoutSyncPosted = RequestInputLanguageChangeLocked(browserHwnd, browserThreadId, browserPid, after.WorkspaceHkl, after.BrowserHkl, $"{reason}.FocusHklSync");
            }

            RuntimeLog.Info(
                $"Firefox persistent-bridge root focus handoff completed. BrowserHWND=0x{browserHwnd.ToInt64():X}; " +
                $"ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; " +
                $"PersistentInputBridgeRequired={bridge.BridgeRequired}; PersistentInputBridgeAttached={bridge.Attached}; BridgeRefCount={bridge.RefCount}; " +
                $"PreviousFocus=0x{previousFocus.ToInt64():X}; CurrentFocus=0x{currentFocus.ToInt64():X}; Foreground=0x{foreground.ToInt64():X}; Win32={focusError}; " +
                $"WorkspaceHKLBefore={InputLanguageDiagnostics.FormatHkl(before.WorkspaceHkl)}; BrowserHKLBefore={InputLanguageDiagnostics.FormatHkl(before.BrowserHkl)}; " +
                $"WorkspaceHKLAfter={InputLanguageDiagnostics.FormatHkl(after.WorkspaceHkl)}; BrowserHKLAfter={InputLanguageDiagnostics.FormatHkl(after.BrowserHkl)}; " +
                $"GuiActiveAfter=0x{after.GuiActive.ToInt64():X}; GuiFocusAfter=0x{after.GuiFocus.ToInt64():X}; GuiCaretAfter=0x{after.GuiCaret.ToInt64():X}; " +
                $"InputLanguageMismatchAfter={after.WorkspaceHkl != after.BrowserHkl}; InputLanguageSyncPosted={layoutSyncPosted}; Reason='{reason}'");
        }
    }

    internal static void SynchronizeActiveInputLanguage(uint workspaceThreadId, string reason)
    {
        var active = ActiveBrowserHwnd;
        if (active == IntPtr.Zero)
        {
            RuntimeLog.Debug($"Active Firefox input-language synchronization skipped because no active docked Browser is recorded. Reason='{reason}'");
            return;
        }
        SynchronizeInputLanguage(active, workspaceThreadId, reason);
    }

    internal static void SynchronizeInputLanguage(IntPtr browserHwnd, uint workspaceThreadId, string reason)
    {
        if (browserHwnd == IntPtr.Zero || !NativeMethods.IsWindow(browserHwnd)) return;
        lock (Sync)
        {
            workspaceThreadId = workspaceThreadId == 0 ? NativeMethods.GetCurrentThreadId() : workspaceThreadId;
            var browserThreadId = NativeMethods.GetWindowThreadProcessId(browserHwnd, out var browserPid);
            if (browserThreadId == 0) return;
            var workspaceHkl = NativeMethods.GetKeyboardLayout(workspaceThreadId);
            var browserHkl = NativeMethods.GetKeyboardLayout(browserThreadId);
            if (workspaceHkl == IntPtr.Zero || workspaceHkl == browserHkl)
            {
                RuntimeLog.Debug($"Firefox input-language synchronization not required. BrowserHWND=0x{browserHwnd.ToInt64():X}; WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(workspaceHkl)}; BrowserHKL={InputLanguageDiagnostics.FormatHkl(browserHkl)}; Reason='{reason}'");
                return;
            }
            RequestInputLanguageChangeLocked(browserHwnd, browserThreadId, browserPid, workspaceHkl, browserHkl, reason);
        }
    }

    private static ThreadBridgeState GetOrCreateBridgeStateLocked(BridgeKey key, uint browserPid, IntPtr browserHwnd, string reason)
    {
        if (ThreadBridges.TryGetValue(key, out var existing)) return existing;
        var state = new ThreadBridgeState(key.WorkspaceThreadId != key.BrowserThreadId, browserPid);
        ThreadBridges[key] = state;
        RuntimeLog.Info(
            $"Firefox input bridge state created. WorkspaceThread={key.WorkspaceThreadId}; BrowserThread={key.BrowserThreadId}; PID={browserPid}; " +
            $"BridgeRequired={state.BridgeRequired}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Reason='{reason}'");
        return state;
    }

    private static void EnsureBridgeAttachedLocked(BridgeKey key, ThreadBridgeState state, uint browserPid, IntPtr browserHwnd, string reason)
    {
        if (!state.BridgeRequired)
        {
            state.Attached = true;
            return;
        }
        if (state.Attached) return;

        Marshal.SetLastPInvokeError(0);
        state.Attached = NativeMethods.AttachThreadInput(key.WorkspaceThreadId, key.BrowserThreadId, true);
        var error = state.Attached ? 0 : Marshal.GetLastWin32Error();
        if (state.Attached)
        {
            RuntimeLog.Info(
                $"Persistent Firefox input bridge attached. WorkspaceThread={key.WorkspaceThreadId}; BrowserThread={key.BrowserThreadId}; PID={browserPid}; " +
                $"BridgeRefCount={state.RefCount}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Reason='{reason}'");
        }
        else
        {
            RuntimeLog.Warn(
                $"Persistent Firefox input bridge attach failed. WorkspaceThread={key.WorkspaceThreadId}; BrowserThread={key.BrowserThreadId}; PID={browserPid}; " +
                $"BridgeRefCount={state.RefCount}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={error}; Reason='{reason}'");
        }
    }

    private static void ReleaseRegistrationLocked(IntPtr browserHwnd, DockRegistration registration, string reason)
    {
        DockRegistrations.Remove(browserHwnd);
        if (!ThreadBridges.TryGetValue(registration.Key, out var bridge)) return;

        bridge.RefCount = Math.Max(0, bridge.RefCount - 1);
        if (bridge.RefCount > 0)
        {
            RuntimeLog.Info(
                $"Persistent Firefox input bridge registration released; bridge retained. BrowserHWND=0x{browserHwnd.ToInt64():X}; " +
                $"WorkspaceThread={registration.Key.WorkspaceThreadId}; BrowserThread={registration.Key.BrowserThreadId}; PID={registration.BrowserPid}; " +
                $"BridgeAttached={bridge.Attached}; BridgeRefCount={bridge.RefCount}; Reason='{reason}'");
            return;
        }

        var detached = !bridge.BridgeRequired || !bridge.Attached;
        var detachError = 0;
        if (bridge.BridgeRequired && bridge.Attached)
        {
            Marshal.SetLastPInvokeError(0);
            detached = NativeMethods.AttachThreadInput(registration.Key.WorkspaceThreadId, registration.Key.BrowserThreadId, false);
            detachError = detached ? 0 : Marshal.GetLastWin32Error();
        }
        ThreadBridges.Remove(registration.Key);

        if (detached)
        {
            RuntimeLog.Info(
                $"Persistent Firefox input bridge detached after last dock. WorkspaceThread={registration.Key.WorkspaceThreadId}; BrowserThread={registration.Key.BrowserThreadId}; " +
                $"PID={registration.BrowserPid}; BrowserHWND=0x{browserHwnd.ToInt64():X}; BridgeRefCount=0; Reason='{reason}'");
        }
        else
        {
            RuntimeLog.Warn(
                $"Persistent Firefox input bridge detach failed after last dock. WorkspaceThread={registration.Key.WorkspaceThreadId}; BrowserThread={registration.Key.BrowserThreadId}; " +
                $"PID={registration.BrowserPid}; BrowserHWND=0x{browserHwnd.ToInt64():X}; Win32={detachError}; Reason='{reason}'");
        }
    }

    private static bool RequestInputLanguageChangeLocked(IntPtr browserHwnd, uint browserThreadId, uint browserPid, IntPtr requestedHkl, IntPtr previousBrowserHkl, string reason)
    {
        Marshal.SetLastPInvokeError(0);
        var posted = NativeMethods.PostMessage(browserHwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, requestedHkl);
        var error = posted ? 0 : Marshal.GetLastWin32Error();
        RuntimeLog.Info(
            $"Firefox input-language request posted={posted}. BrowserHWND=0x{browserHwnd.ToInt64():X}; BrowserThread={browserThreadId}; PID={browserPid}; " +
            $"RequestedHKL={InputLanguageDiagnostics.FormatHkl(requestedHkl)}; PreviousBrowserHKL={InputLanguageDiagnostics.FormatHkl(previousBrowserHkl)}; Win32={error}; Reason='{reason}'");
        return posted;
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
            var key = new BridgeKey(workspaceThreadId, browserThreadId);
            ThreadBridges.TryGetValue(key, out var bridge);
            RuntimeLog.Info(
                $"Firefox input-state transition observed. BrowserHWND=0x{browserHwnd.ToInt64():X}; ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; PID={browserPid}; " +
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; " +
                $"PersistentInputBridgeAttached={bridge?.Attached ?? false}; BridgeRefCount={bridge?.RefCount ?? 0}; " +
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
            if (DockRegistrations.TryGetValue(browserHwnd, out var registration))
            {
                ReleaseRegistrationLocked(browserHwnd, registration, "FirefoxInputCoordinator.Forget");
            }
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

    private readonly record struct BridgeKey(uint WorkspaceThreadId, uint BrowserThreadId);
    private readonly record struct DockRegistration(BridgeKey Key, uint BrowserPid);

    private sealed class ThreadBridgeState
    {
        internal ThreadBridgeState(bool bridgeRequired, uint browserPid)
        {
            BridgeRequired = bridgeRequired;
            BrowserPid = browserPid;
        }

        internal bool BridgeRequired { get; }
        internal uint BrowserPid { get; }
        internal int RefCount { get; set; }
        internal bool Attached { get; set; }
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
