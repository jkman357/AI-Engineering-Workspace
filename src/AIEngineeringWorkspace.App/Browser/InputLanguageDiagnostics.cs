using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;

namespace AIEngineeringWorkspace.Browser;

/// <summary>
/// Read-only diagnostics for the Windows input-language / IME boundary around the
/// reparented Firefox HWND. rc14 intentionally observes these messages; it does not
/// synthesize IME composition messages or force a keyboard layout into Firefox.
/// </summary>
internal static class InputLanguageDiagnostics
{
    internal static bool IsTrackedMessage(int message)
        => (uint)message is NativeMethods.WM_INPUTLANGCHANGEREQUEST
            or NativeMethods.WM_INPUTLANGCHANGE
            or NativeMethods.WM_IME_SETCONTEXT
            or NativeMethods.WM_IME_STARTCOMPOSITION
            or NativeMethods.WM_IME_COMPOSITION
            or NativeMethods.WM_IME_ENDCOMPOSITION;

    internal static string GetMessageName(int message)
        => (uint)message switch
        {
            NativeMethods.WM_INPUTLANGCHANGEREQUEST => "WM_INPUTLANGCHANGEREQUEST",
            NativeMethods.WM_INPUTLANGCHANGE => "WM_INPUTLANGCHANGE",
            NativeMethods.WM_IME_SETCONTEXT => "WM_IME_SETCONTEXT",
            NativeMethods.WM_IME_STARTCOMPOSITION => "WM_IME_STARTCOMPOSITION",
            NativeMethods.WM_IME_COMPOSITION => "WM_IME_COMPOSITION",
            NativeMethods.WM_IME_ENDCOMPOSITION => "WM_IME_ENDCOMPOSITION",
            _ => $"0x{message:X}"
        };

    internal static void LogWindowMessage(string scope, IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (!IsTrackedMessage(message))
        {
            return;
        }

        RuntimeLog.Info(
            $"Input-language/IME message observed. Scope='{scope}'; HWND=0x{hwnd.ToInt64():X}; " +
            $"Message={GetMessageName(message)}(0x{message:X}); WParam=0x{wParam.ToInt64():X}; LParam=0x{lParam.ToInt64():X}; " +
            $"CurrentThread={NativeMethods.GetCurrentThreadId()}; CurrentThreadHKL={FormatHkl(NativeMethods.GetKeyboardLayout(0))}");
    }

    internal static string FormatHkl(IntPtr hkl)
        => $"0x{hkl.ToInt64():X}";
}
