namespace AIEngineeringWorkspace.Controls;

internal static class BrowserDockRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<IntPtr, BrowserDockHost> Owners = new();

    public static bool TryClaim(IntPtr hwnd, BrowserDockHost owner)
    {
        lock (Sync)
        {
            if (Owners.TryGetValue(hwnd, out var existingOwner)) return ReferenceEquals(existingOwner, owner);
            Owners[hwnd] = owner;
            return true;
        }
    }

    public static void Release(IntPtr hwnd, BrowserDockHost owner)
    {
        if (hwnd == IntPtr.Zero) return;
        lock (Sync)
        {
            if (Owners.TryGetValue(hwnd, out var existingOwner) && ReferenceEquals(existingOwner, owner)) Owners.Remove(hwnd);
        }
    }
}
