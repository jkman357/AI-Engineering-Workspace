namespace AIEngineeringWorkspace.Browser;

internal sealed record BrowserWindowInfo(IntPtr Hwnd, uint ProcessId, string Title, string ClassName)
{
    public string HwndHex => $"0x{Hwnd.ToInt64():X}";
}
