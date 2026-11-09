using System.Runtime.InteropServices;

namespace RobControl.App.Views;

/// <summary>
/// The Win32 calls behind <see cref="TrayIcon"/>: a hidden window to receive the icon's messages,
/// Shell_NotifyIcon, and a plain popup menu. Plain Win32 rather than WinForms' NotifyIcon so the app
/// does not take on UseWindowsForms (and its clashing implicit usings) for one icon.
/// </summary>
internal static class TrayNative
{
    public const int NimAdd = 0;
    public const int NimModify = 1;
    public const int NimDelete = 2;
    public const int NimSetVersion = 4;

    public const int NifMessage = 0x01;
    public const int NifIcon = 0x02;
    public const int NifTip = 0x04;
    public const int NifInfo = 0x10;
    public const int NifShowTip = 0x80;

    public const int NiifInfo = 0x01;
    public const int NotifyIconVersion4 = 4;

    public const uint WmNull = 0x0000;
    public const uint WmApp = 0x8000;
    public const uint WmContextMenu = 0x007B;
    public const uint WmLButtonDblClk = 0x0203;
    public const uint WmLButtonUp = 0x0202;
    public const uint NinSelect = 0x0400;
    public const uint NinKeySelect = 0x0401;

    public const uint MfString = 0x0000;
    public const uint MfSeparator = 0x0800;
    public const uint MfDefault = 0x1000;
    public const uint TpmRightButton = 0x0002;
    public const uint TpmReturnCmd = 0x0100;
    public const uint TpmNoNotify = 0x0080;

    public const int WsPopup = unchecked((int)0x80000000);
    public const int WsExToolWindow = 0x00000080;

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public int CbSize;
        public IntPtr HWnd;
        public int UId;
        public int UFlags;
        public int UCallbackMessage;
        public IntPtr HIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SzTip;

        public int DwState;
        public int DwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SzInfo;

        public int UVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SzInfoTitle;

        public int DwInfoFlags;
        public Guid GuidItem;
        public IntPtr HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClass
    {
        public int CbSize;
        public uint Style;
        public IntPtr LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
        public IntPtr HIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint icons);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", ExactSpelling = true)]
    public static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint RegisterWindowMessage(string message);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern IntPtr GetModuleHandle(string? module);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr parameters);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
