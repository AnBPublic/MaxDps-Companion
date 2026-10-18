using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>
/// Raw Win32 plumbing for the borderless shell (P1). One HWND subclass
/// (<c>SetWindowSubclass</c>) owns:
/// <list type="bullet">
///   <item>WM_NCHITTEST edge-resize for the borderless window (8 px grip,
///     DPI-scaled; same code path MainForm.WndProc used);</item>
///   <item>WM_GETMINMAXINFO minimum tracking size (520x560 DIP, DPI-scaled);</item>
///   <item>WM_HOTKEY dispatch for the global pause hotkey.</item>
/// </list>
/// The DWM calls reproduce the classic chrome: rounded corners
/// (DWMWA_WINDOW_CORNER_PREFERENCE, same P/Invoke approach as
/// <c>UiControls.WindowChrome.EnableModernCorners</c>) and no frame border on
/// the Win11 rounded window. All coordinates in this file are PHYSICAL pixels;
/// callers pass DIPs through <see cref="Scale"/>.
/// </summary>
internal sealed class Win32Host : IDisposable
{
    /// <summary>Same hotkey id MainForm used (0xA71 = "A71").</summary>
    public const int PauseHotkeyId = 0xA71;

    /// <summary>v4.2.0 manual party-CC hold hotkey id.</summary>
    public const int CcHoldHotkeyId = 0xA72;

    // Shell geometry in DIPs (WinUI layout units); scaled to physical pixels.
    public const int MinWidthDip = 520;
    public const int MinHeightDip = 560;

    private const int EdgeGripDip = 8;

    private const uint WmNcHitTest = 0x0084;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmHotKey = 0x0312;

    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public Native.POINT ptReserved;
        public Native.POINT ptMaxSize;
        public Native.POINT ptMaxPosition;
        public Native.POINT ptMinTrackSize;
        public Native.POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc; // rooted: native code holds this pointer
    private readonly IntPtr _subclassId = new(1);
    private bool _installed;
    private bool _hotkeyRegistered;
    private bool _ccHotkeyRegistered;
    private bool _disposed;

    /// <summary>Fires on WM_HOTKEY for <see cref="PauseHotkeyId"/>. P1 keeps this as the public hookup point; engine wiring lands in P2-C.</summary>
    public event Action? PauseHotkeyPressed;

    /// <summary>Fires on WM_HOTKEY for <see cref="CcHoldHotkeyId"/> (v4.2.0 manual CC hold).</summary>
    public event Action? CcHoldHotkeyPressed;

    /// <summary>
    /// Last-chance message filter (e.g. the tray callback message). Returning
    /// true consumes the message; the window procedure then returns 0.
    /// </summary>
    public Func<uint, IntPtr, IntPtr, bool>? MessageFilter { get; set; }

    public Win32Host(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = WndProc;
        _installed = SetWindowSubclass(hwnd, _proc, _subclassId, IntPtr.Zero);
        if (!_installed)
        {
            // Resize/min-size/hotkey need the subclass; launch continues so a
            // missing comctl32 can never brick the shell (Windows 10+ has it).
            Debug.WriteLine($"SetWindowSubclass failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }
        EnableModernCorners(hwnd);
        SuppressFrameBorder(hwnd);
    }

    public IntPtr Handle => _hwnd;

    /// <summary>True when the HWND subclass is installed (edge resize + WM_HOTKEY live).</summary>
    public bool Installed => _installed;

    /// <summary>Current window DPI (96 fallback; pre-1607 GetDpiForWindow returns 0).</summary>
    public uint Dpi
    {
        get
        {
            var dpi = GetDpiForWindow(_hwnd);
            return dpi == 0 ? 96u : dpi;
        }
    }

    /// <summary>DIP -> physical pixels for this window's monitor.</summary>
    public static int Scale(int dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);

    /// <summary>
    /// Registers the global pause hotkey (same grammar + id as MainForm:
    /// MovementGuard.TryParseHotkey -> RegisterHotKey). Any previous binding on
    /// this window is removed first, so the settings box can re-register.
    /// Returns false on an unparseable combo or a taken hotkey.
    /// </summary>
    public bool RegisterPauseHotkey(string spec)
    {
        UnregisterPauseHotkey();
        if (!MovementGuard.TryParseHotkey(spec, out var modifiers, out var vk)) return false;
        _hotkeyRegistered = Native.RegisterHotKey(_hwnd, PauseHotkeyId, modifiers, vk);
        return _hotkeyRegistered;
    }

    public void UnregisterPauseHotkey()
    {
        if (!_hotkeyRegistered) return;
        Native.UnregisterHotKey(_hwnd, PauseHotkeyId);
        _hotkeyRegistered = false;
    }

    /// <summary>Registers the v4.2.0 manual party-CC hold hotkey.</summary>
    public bool RegisterCcHoldHotkey(string spec)
    {
        UnregisterCcHoldHotkey();
        if (!MovementGuard.TryParseHotkey(spec, out var modifiers, out var vk)) return false;
        _ccHotkeyRegistered = Native.RegisterHotKey(_hwnd, CcHoldHotkeyId, modifiers, vk);
        return _ccHotkeyRegistered;
    }

    public void UnregisterCcHoldHotkey()
    {
        if (!_ccHotkeyRegistered) return;
        Native.UnregisterHotKey(_hwnd, CcHoldHotkeyId);
        _ccHotkeyRegistered = false;
    }

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND (classic v3 chrome).</summary>
    public static void EnableModernCorners(IntPtr hwnd)
    {
        var preference = DwmwcpRound;
        try { DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int)); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>Removes the Win11 frame line around the WS_THICKFRAME borderless window (DWMWA_BORDER_COLOR = none).</summary>
    public static void SuppressFrameBorder(IntPtr hwnd)
    {
        var none = DwmwaColorNone;
        try { DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int)); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (msg)
        {
            case WmNcHitTest:
            {
                // MainForm parity: let the default handler decide first; only a
                // client-area result near an edge becomes a resize hit.
                var result = DefSubclassProc(hWnd, msg, wParam, lParam);
                if (result == (IntPtr)HtClient)
                {
                    var hit = EdgeHitTest(lParam);
                    if (hit != HtClient) return (IntPtr)hit;
                }
                return result;
            }
            case WmGetMinMaxInfo:
            {
                DefSubclassProc(hWnd, msg, wParam, lParam);
                if (lParam != IntPtr.Zero)
                {
                    var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    info.ptMinTrackSize.X = Scale(MinWidthDip, Dpi);
                    info.ptMinTrackSize.Y = Scale(MinHeightDip, Dpi);
                    Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
                }
                return IntPtr.Zero;
            }
            case WmHotKey:
                if (wParam.ToInt32() == PauseHotkeyId)
                {
                    PauseHotkeyPressed?.Invoke();
                    return IntPtr.Zero;
                }
                if (wParam.ToInt32() == CcHoldHotkeyId)
                {
                    CcHoldHotkeyPressed?.Invoke();
                    return IntPtr.Zero;
                }
                break;
        }
        if (MessageFilter is { } filter && filter(msg, wParam, lParam)) return IntPtr.Zero;
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>Physical-pixel edge hit test (lParam carries signed screen coords).</summary>
    private int EdgeHitTest(IntPtr lParam)
    {
        if (!GetWindowRect(_hwnd, out var rect)) return HtClient;
        var lp = lParam.ToInt64();
        var screenX = (short)(lp & 0xFFFF);
        var screenY = (short)((lp >> 16) & 0xFFFF);
        var x = screenX - rect.Left;
        var y = screenY - rect.Top;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var grip = Scale(EdgeGripDip, Dpi);
        var corner = grip * 2;
        var left = x < grip;
        var right = x >= width - grip;
        var top = y < grip;
        var bottom = y >= height - grip;

        if (top) return left ? HtTopLeft : right ? HtTopRight : HtTop;
        if (bottom) return left ? HtBottomLeft : right ? HtBottomRight : HtBottom;
        if (left) return HtLeft;
        if (right) return HtRight;
        return HtClient;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterPauseHotkey();
        UnregisterCcHoldHotkey();
        if (_installed)
        {
            RemoveWindowSubclass(_hwnd, _proc, _subclassId);
            _installed = false;
        }
    }
}
