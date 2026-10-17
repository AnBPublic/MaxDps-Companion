using System.Runtime.InteropServices;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// Shell_NotifyIcon tray icon (P1). WinForms' NotifyIcon is gone, so this is the
/// raw Win32 equivalent with the same observable behavior as MainForm:
/// <list type="bullet">
///   <item>invisible at launch (<c>Visible = false</c>), shown only while the
///     window is minimized to the tray (MainForm.OnResize);</item>
///   <item>popup menu Show / Pause / Quit via CreatePopupMenu + TrackPopupMenu
///     (MainForm.BuildTray order);</item>
///   <item>double-click restores (MainForm.Restore).</item>
/// </list>
/// The icon bitmap is <c>assets/companion.ico</c> next to the exe, with the
/// stock application icon as fallback (MainForm.BuildTray semantics).
/// Callback messages arrive on <c>WM_APP+1</c> and are dispatched by the
/// ShellWindow message flow via <see cref="HandleMessage"/>.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Tray callback message (WM_APP + 1, same slot WinForms NotifyIcon used).</summary>
    public const uint WmTrayIcon = 0x8000 + 1;

    private const uint IconId = 1;

    private const uint NIM_ADD = 0x0;
    private const uint NIM_DELETE = 0x2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;

    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NULL = 0x0000;

    private const uint MF_STRING = 0x0;
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_RETURNCMD = 0x100;
    private const uint TPM_RIGHTBUTTON = 0x2;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;
    private const uint LR_DEFAULTSIZE = 0x40;
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    private const uint MenuShow = 1;
    private const uint MenuPause = 2;
    private const uint MenuQuit = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr hMenu, uint flags, uint idNewItem, string newItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    private readonly IntPtr _hwnd;
    private readonly string _tooltip;
    private IntPtr _icon;
    private bool _iconOwned;
    private bool _added;
    private bool _visible;
    private bool _disposed;

    /// <summary>Show (menu) or double-click on the icon: restore the window (MainForm.Restore).</summary>
    public event Action? RestoreRequested;

    /// <summary>Pause menu item (MainForm tray "Start/Stop" slot); hookup point for P2-C.</summary>
    public event Action? PauseRequested;

    /// <summary>Quit menu item (MainForm tray "Exit").</summary>
    public event Action? QuitRequested;

    public TrayIcon(IntPtr hwnd, string tooltip)
    {
        _hwnd = hwnd;
        _tooltip = tooltip;
        _icon = LoadTrayIcon();
    }

    /// <summary>Mirrors NotifyIcon.Visible: false at launch, true only while hidden in the tray.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value || _disposed) return;
            _visible = value;
            if (value) _added = Notify(NIM_ADD);
            else if (_added)
            {
                Notify(NIM_DELETE);
                _added = false;
            }
        }
    }

    /// <summary>
    /// Dispatches one window message. Returns true when the message was a tray
    /// callback (caller stops routing). lParam low word carries the mouse event,
    /// same protocol WinForms NotifyIcon used.
    /// </summary>
    public bool HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WmTrayIcon || (uint)wParam != IconId) return false;
        var notification = (uint)(lParam.ToInt64() & 0xFFFF);
        switch (notification)
        {
            case WM_LBUTTONDBLCLK:
                RestoreRequested?.Invoke();
                return true;
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowMenu();
                return true;
        }
        return false;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MF_STRING, MenuShow, "Show");
            AppendMenu(menu, MF_STRING, MenuPause, "Pause");
            AppendMenu(menu, MF_SEPARATOR, 0, string.Empty);
            AppendMenu(menu, MF_STRING, MenuQuit, "Quit");

            if (!GetCursorPos(out var point)) return;
            // MSDN tray menu contract: foreground before TrackPopupMenu, and a
            // posted no-op after so the menu dismisses on outside click.
            SetForegroundWindow(_hwnd);
            var command = (uint)TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case MenuShow: RestoreRequested?.Invoke(); break;
                case MenuPause: PauseRequested?.Invoke(); break;
                case MenuQuit: QuitRequested?.Invoke(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private bool Notify(uint message)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WmTrayIcon,
            hIcon = _icon,
            szTip = _tooltip,
        };
        return Shell_NotifyIcon(message, ref data);
    }

    /// <summary>companion.ico next to the exe, else the stock application icon (MainForm.BuildTray).</summary>
    private IntPtr LoadTrayIcon()
    {
        try
        {
            var path = Path.Combine(Program.AppDir, "assets", "companion.ico");
            if (File.Exists(path))
            {
                var icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                if (icon != IntPtr.Zero)
                {
                    _iconOwned = true;
                    return icon;
                }
            }
        }
        catch { /* loose art missing: stock icon below still gives a tray entry */ }
        return LoadIcon(IntPtr.Zero, IDI_APPLICATION);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Visible = false;
        _disposed = true;
        if (_iconOwned && _icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }
}
