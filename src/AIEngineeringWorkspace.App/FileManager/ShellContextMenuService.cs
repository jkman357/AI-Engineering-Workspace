using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AIEngineeringWorkspace.Infrastructure;

namespace AIEngineeringWorkspace.FileManager;

internal static class ShellContextMenuService
{
    private const uint CmdFirst = 1;
    private const uint CmdLast = 0x7FFF;
    private const uint CmfNormal = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const int SwShowNormal = 1;

    private const int WmDrawItem = 0x002B;
    private const int WmMeasureItem = 0x002C;
    private const int WmInitMenuPopup = 0x0117;
    private const int WmMenuChar = 0x0120;

    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid BhidSfUiObject = new("3981E225-F559-11D3-8E3A-00C04F6837D5");
    private static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");

    internal static bool Show(Window owner, string path, Point screenPoint, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            error = "The selected Shell item no longer exists.";
            return false;
        }

        var ownerHwnd = new WindowInteropHelper(owner).Handle;
        if (ownerHwnd == IntPtr.Zero)
        {
            error = "The Workspace window handle is unavailable.";
            return false;
        }

        IShellItem? shellItem = null;
        object? contextObject = null;
        IntPtr menu = IntPtr.Zero;
        HwndSource? source = null;
        HwndSourceHook? hook = null;

        try
        {
            var iid = IidShellItem;
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out shellItem);
            Marshal.ThrowExceptionForHR(hr);

            var bhid = BhidSfUiObject;
            var contextIid = IidContextMenu;
            hr = shellItem.BindToHandler(IntPtr.Zero, ref bhid, ref contextIid, out var shellUiObject);
            contextObject = shellUiObject;
            Marshal.ThrowExceptionForHR(hr);

            if (contextObject is not IContextMenu contextMenu)
            {
                error = "Windows Shell did not provide IContextMenu for the selected item.";
                return false;
            }

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                error = $"CreatePopupMenu failed. Win32={Marshal.GetLastWin32Error()}";
                return false;
            }

            hr = contextMenu.QueryContextMenu(menu, 0, CmdFirst, CmdLast, CmfNormal);
            Marshal.ThrowExceptionForHR(hr);

            source = HwndSource.FromHwnd(ownerHwnd);
            if (source is not null)
            {
                hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    try
                    {
                        if (msg is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar))
                        {
                            return IntPtr.Zero;
                        }

                        if (contextObject is IContextMenu3 contextMenu3)
                        {
                            var messageHr = contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out var result);
                            if (messageHr >= 0)
                            {
                                handled = true;
                                return result;
                            }
                        }
                        else if (contextObject is IContextMenu2 contextMenu2)
                        {
                            var messageHr = contextMenu2.HandleMenuMsg((uint)msg, wParam, lParam);
                            if (messageHr >= 0)
                            {
                                handled = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RuntimeLog.Warn($"Shell context-menu message forwarding failed. Message=0x{msg:X}; Error='{ex.Message}'");
                    }

                    return IntPtr.Zero;
                };
                source.AddHook(hook);
            }

            var x = (int)Math.Round(screenPoint.X);
            var y = (int)Math.Round(screenPoint.Y);
            SetForegroundWindow(ownerHwnd);
            var selectedCommand = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, x, y, ownerHwnd, IntPtr.Zero);
            if (selectedCommand == 0)
            {
                return true;
            }

            var invoke = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                fMask = 0,
                hwnd = ownerHwnd,
                lpVerb = new IntPtr(unchecked((int)(selectedCommand - CmdFirst))),
                lpParameters = IntPtr.Zero,
                lpDirectory = IntPtr.Zero,
                nShow = SwShowNormal,
                dwHotKey = 0,
                hIcon = IntPtr.Zero
            };

            hr = contextMenu.InvokeCommand(ref invoke);
            Marshal.ThrowExceptionForHR(hr);
            RuntimeLog.Info($"Native Windows Shell context-menu command invoked. Path='{path}'; CommandId={selectedCommand}; Offset={selectedCommand - CmdFirst}");
            return true;
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"Native Windows Shell context menu failed. Path='{path}'", ex);
            error = ex.Message;
            return false;
        }
        finally
        {
            if (source is not null && hook is not null)
            {
                source.RemoveHook(hook);
            }

            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (contextObject is not null && Marshal.IsComObject(contextObject))
            {
                Marshal.FinalReleaseComObject(contextObject);
            }

            if (shellItem is not null && Marshal.IsComObject(shellItem))
            {
                Marshal.FinalReleaseComObject(shellItem);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        out IShellItem ppv);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
