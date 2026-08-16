using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AIEngineeringWorkspace.Infrastructure;

namespace AIEngineeringWorkspace.FileManager;

internal static class ShellIconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    internal static BitmapSource? GetSmallIcon(string path)
    {
        try
        {
            var result = SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiSmallIcon);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex)
        {
            RuntimeLog.Debug($"Shell icon lookup failed. Path='{path}'; Error={ex.Message}");
            return null;
        }
    }
}
