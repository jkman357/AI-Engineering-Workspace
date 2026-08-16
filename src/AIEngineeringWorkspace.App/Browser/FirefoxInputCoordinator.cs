using System.Runtime.InteropServices;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

/// <summary>
/// Central authority for docked Firefox native input ownership.
///
/// Cross-thread focus is always a one-shot transaction: temporary AttachThreadInput,
/// root-HWND SetFocus, then detach in finally. rc16 additionally tracks the last active
/// docked Firefox root HWND so native Browser clicks and WPF-owned layout transitions can
/// recover the same Browser deterministically. Input-language synchronization is limited
/// to WM_INPUTLANGCHANGEREQUEST; this coordinator never synthesizes IME composition.
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

            var before = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
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

            var after = CaptureSnapshot(browserHwnd, workspaceThreadId, browserThreadId, browserPid);
            LastSnapshots[browserHwnd] = after;
            var layoutSyncPosted = false;
            if (after.WorkspaceHkl != IntPtr.Zero && after.WorkspaceHkl != after.BrowserHkl)
            {
                layoutSyncPosted = RequestInputLanguageChangeLocked(browserHwnd, browserThreadId, browserPid, after.WorkspaceHkl, after.BrowserHkl, $"{reason}.FocusHklSync");
            }

            RuntimeLog.Info(
                $"Firefox transactional root focus handoff completed. BrowserHWND=0x{browserHwnd.ToInt64():X}; " +
                $"ActiveBrowserHWND=0x{_activeBrowserHwnd.ToInt64():X}; WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; PID={browserPid}; " +
                $"TemporaryInputBridgeAttached={bridgeAttached}; TemporaryInputBridgeDetached={bridgeDetached}; " +
                $"PreviousFocus=0x{previousFocus.ToInt64():X}; CurrentFocus=0x{currentFocus.ToInt64():X}; " +
                $"Foreground=0x{foreground.ToInt64():X}; Win32={focusError}; " +
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

    /// <summary>
    /// Called by the existing one-second Browser health loop. It logs only when observable
    /// input state changes, so focus ownership and English/number -> Zhuyin transitions are
    /// visible without flooding the runtime log every second.
    /// </summary>
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
                $"WorkspaceThread={workspaceThreadId}; BrowserThread={browserThreadId}; " +
                $"WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(snapshot.WorkspaceHkl)}; BrowserHKL={InputLanguageDiagnostics.FormatHkl(snapshot.BrowserHkl)}; " +
                $"InputLanguageMismatch={snapshot.WorkspaceHkl != snapshot.BrowserHkl}; " +
                $"Foreground=0x{snapshot.Foreground.ToInt64():X}; GuiInfoOk={snapshot.GuiInfoOk}; " +
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
