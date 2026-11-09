using System.Runtime.InteropServices;

namespace RobControl.App.Views;

/// <summary>
/// RobControl's icon in the notification area, shown while the window is closed but the app is still
/// doing something on its own - scheduled backups, trend recording, a backup in progress. Before
/// 0.7.2 closing the window ended the process, and with it every schedule.
///
/// <para>Double-click (or click) the icon to bring the window back; right-click for Open and Exit.
/// Events are raised on the thread that created the icon - the UI thread - because the hidden window
/// that receives the icon's messages is pumped by WPF's own dispatcher loop.</para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int IconId = 1;
    private const uint CallbackMessage = TrayNative.WmApp + 1;
    private const int OpenCommand = 1;
    private const int ExitCommand = 2;

    private readonly TrayNative.WndProc _wndProc;
    private readonly string _className = "RobControl.Tray." + Guid.NewGuid().ToString("N");
    private readonly IntPtr _instance;
    private readonly IntPtr _window;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly uint _taskbarCreated;
    private string _text;
    private bool _visible;
    private bool _disposed;

    public TrayIcon(string text)
    {
        _text = text;
        _instance = TrayNative.GetModuleHandle(null);

        // Kept in a field: the window holds only a function pointer, and a collected delegate behind
        // it is a crash the first time Windows calls back.
        _wndProc = OnMessage;
        var windowClass = new TrayNative.WindowClass
        {
            CbSize = Marshal.SizeOf<TrayNative.WindowClass>(),
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            HInstance = _instance,
            LpszClassName = _className,
        };
        if (TrayNative.RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"The notification-area icon could not be set up (RegisterClassEx error {Marshal.GetLastPInvokeError()}).");
        }

        // A hidden top-level window rather than a message-only one: only top-level windows hear the
        // TaskbarCreated broadcast (Explorer restarted), and the popup menu needs a window it can bring
        // to the foreground so that clicking elsewhere closes it.
        _window = TrayNative.CreateWindowEx(TrayNative.WsExToolWindow, _className, "RobControl tray", TrayNative.WsPopup,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            TrayNative.UnregisterClass(_className, _instance);
            throw new InvalidOperationException($"The notification-area icon could not be set up (CreateWindowEx error {error}).");
        }

        _taskbarCreated = TrayNative.RegisterWindowMessage("TaskbarCreated");
        (_icon, _ownsIcon) = LoadAppIcon();
    }

    /// <summary>Click, double-click or Open on the menu.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Exit on the menu.</summary>
    public event EventHandler? ExitRequested;

    public bool IsVisible => _visible;

    /// <summary>The tooltip. Windows keeps 127 characters of it.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            if (_visible)
            {
                var data = Data(TrayNative.NifTip | TrayNative.NifShowTip);
                TrayNative.ShellNotifyIcon(TrayNative.NimModify, ref data);
            }
        }
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_visible)
        {
            return;
        }

        var data = Data(TrayNative.NifMessage | TrayNative.NifIcon | TrayNative.NifTip | TrayNative.NifShowTip);
        _visible = TrayNative.ShellNotifyIcon(TrayNative.NimAdd, ref data);
        if (_visible)
        {
            data.UVersion = TrayNative.NotifyIconVersion4;
            TrayNative.ShellNotifyIcon(TrayNative.NimSetVersion, ref data);
        }
    }

    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        var data = Data(0);
        TrayNative.ShellNotifyIcon(TrayNative.NimDelete, ref data);
        _visible = false;
    }

    /// <summary>A notification beside the icon (a toast on Windows 10 and 11).</summary>
    public void Notify(string title, string message)
    {
        if (!_visible)
        {
            return;
        }

        var data = Data(TrayNative.NifInfo);
        data.SzInfoTitle = Clip(title, 63);
        data.SzInfo = Clip(message, 255);
        data.DwInfoFlags = TrayNative.NiifInfo;
        TrayNative.ShellNotifyIcon(TrayNative.NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
        TrayNative.DestroyWindow(_window);
        TrayNative.UnregisterClass(_className, _instance);
        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            TrayNative.DestroyIcon(_icon);
        }
    }

    private TrayNative.NotifyIconData Data(int flags) => new()
    {
        CbSize = Marshal.SizeOf<TrayNative.NotifyIconData>(),
        HWnd = _window,
        UId = IconId,
        UFlags = flags,
        UCallbackMessage = (int)CallbackMessage,
        HIcon = _icon,
        SzTip = Clip(_text, 127),
        SzInfo = string.Empty,
        SzInfoTitle = string.Empty,
    };

    private IntPtr OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: the event is in the low word of lParam.
            uint what = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (what)
            {
                case TrayNative.WmLButtonDblClk:
                case TrayNative.NinSelect:
                case TrayNative.NinKeySelect:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case TrayNative.WmContextMenu:
                    ShowMenu();
                    break;
            }

            return IntPtr.Zero;
        }

        if (msg == _taskbarCreated && _taskbarCreated != 0 && _visible)
        {
            // Explorer restarted and forgot every icon: put ours back.
            _visible = false;
            Show();
            return IntPtr.Zero;
        }

        return TrayNative.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = TrayNative.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            TrayNative.AppendMenu(menu, TrayNative.MfString | TrayNative.MfDefault, OpenCommand, "&Open RobControl");
            TrayNative.AppendMenu(menu, TrayNative.MfSeparator, UIntPtr.Zero, null);
            TrayNative.AppendMenu(menu, TrayNative.MfString, ExitCommand, "E&xit (stops scheduled backups)");
            TrayNative.GetCursorPos(out TrayNative.Point at);

            // Without the foreground window the menu would not close when the person clicks away -
            // the documented quirk of TrackPopupMenu from a notification icon, and the WM_NULL after.
            TrayNative.SetForegroundWindow(_window);
            int chosen = TrayNative.TrackPopupMenuEx(menu, TrayNative.TpmRightButton | TrayNative.TpmReturnCmd | TrayNative.TpmNoNotify,
                at.X, at.Y, _window, IntPtr.Zero);
            TrayNative.PostMessage(_window, TrayNative.WmNull, IntPtr.Zero, IntPtr.Zero);

            if (chosen == OpenCommand)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (chosen == ExitCommand)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            TrayNative.DestroyMenu(menu);
        }
    }

    /// <summary>The exe's own icon, small size; Windows' generic application icon if that fails.</summary>
    private static (IntPtr Icon, bool Owned) LoadAppIcon()
    {
        if (Environment.ProcessPath is { } exe)
        {
            var small = new IntPtr[1];
            if (TrayNative.ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
            {
                return (small[0], true);
            }
        }

        return (TrayNative.LoadIcon(IntPtr.Zero, new IntPtr(32512)), false);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
