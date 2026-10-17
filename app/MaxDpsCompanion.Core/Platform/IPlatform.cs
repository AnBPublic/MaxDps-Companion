using System.Drawing;

namespace MaxDpsCompanion;

// Platform seam (Core P0a, per architect ADR-P0): portable interfaces over the
// OS-bound services. The Windows app implements these with GDI/BitBlt,
// PostMessage/SendInput and the WoW/Battle.net lookup; the Mac stream
// implements them with its own capture/input/window primitives. The engine
// codes against the interfaces only.
//
// No Bitmap, no Forms, no DllImport here: System.Drawing primitives
// (Color, Point, Size) plus IntPtr window handles only. (Color/Point/Size
// live in System.Drawing.Primitives, part of the shared framework — no
// System.Drawing.Common package needed, so this still compiles standalone
// on any OS.)
//
// Behaviour contracts preserved from the live implementation:
// - 43-cell single capture (Ext3 width; a narrower stale addon decodes from
//   the widest capture with its extra cells read as background).
// - 5-tap median (centre + 4-neighbourhood) for cellSize >= 2px, exact
//   centre pixel below that (median would bleed into neighbours).
// - 1s window re-verify of the cached game handle.
// - 5s relocate throttle, 2s stale-capture recovery (engine cadence).
// - Background PostMessage keyboard + foreground mouse gate.

/// <summary>
/// Copies the addon's pixel strip off the screen and returns the median
/// colour of each cell. One 43-cell (Ext3) capture per tick; a 40-cell Ext2
/// or 35-cell core addon decodes from it with the extra cells as
/// background. The 5-tap centre+4-neighbourhood median (cellSize &gt;= 2px)
/// kills HDR dither and tolerates 1px misalignment; below 2px the exact
/// centre pixel is read instead.
/// </summary>
internal interface IScreenSampler
{
    /// <summary>Full-width (Ext3, 43-cell) capture at the block origin.</summary>
    Color[] Sample(Point origin, int cellSize);

    /// <summary>v1-width capture (8 cells) for the stale-addon fallback.</summary>
    Color[] SampleV1(Point origin, int cellSize);

    /// <summary>
    /// In-game 14-bit toggle mask, 4-bit epoch and 4-bit blocked nibble from
    /// the most recent capture. Portable shape of PixelProtocol.Ext3Block —
    /// P0b wires the real type. Null for a non-Ext3 addon, a missing/torn
    /// block, or before the first decode.
    /// </summary>
    (int Mask, int Epoch, int Blocked)? Ext3Mirror { get; }

    /// <summary>
    /// Raw Ext3 presence bit (cell 28 B bit2): true even when the block's
    /// checksum failed, so a consumer can tell "advertised but corrupt" from
    /// "no Ext3".
    /// </summary>
    bool Ext3MirrorPresent { get; }

    /// <summary>
    /// Drops the capture surface AND the cached screen resources so the next
    /// <see cref="Sample"/> re-acquires both; also clears the Ext3 mirror.
    /// Called on a capture failure and by the engine when the strip has been
    /// missing long enough that the chain must be assumed stale (2s
    /// recovery: content can freeze after a display change even while the
    /// capture call still "succeeds").
    /// </summary>
    void Reset();
}

/// <summary>
/// Locates the game window and the screen position of its client area
/// origin. The cached handle is re-validated against the process's CURRENT
/// main window every second (1s re-verify): after a relog / character
/// switch / display-mode change the game can recreate its window, leaving
/// the old handle alive but wrong — the periodic identity check re-resolves
/// within a second instead of going blind until restart.
/// </summary>
internal interface IGameWindow
{
    /// <summary>Cached game window handle, or zero when unresolved.</summary>
    IntPtr Handle { get; }

    /// <summary>True while the cached handle is a live window.</summary>
    bool IsValid { get; }

    /// <summary>True while the game window is the foreground window.</summary>
    bool IsForeground { get; }

    /// <summary>Owning process id of the attached window, for send-target checks.</summary>
    uint ProcessId { get; }

    /// <summary>True while <paramref name="candidate"/> is the attached game window.</summary>
    bool IsGameWindow(IntPtr candidate);

    /// <summary>
    /// Finds the game window by process name, matched case-insensitively
    /// without the .exe suffix ("Wow", "WowClassic", "Legion").
    /// </summary>
    bool Refresh(string processName);

    /// <summary>
    /// Screen coordinates of the client area's top-left corner (where WoW
    /// anchors the pixel block) plus the client size. False when the window
    /// is dead or the rect is empty.
    /// </summary>
    bool TryGetClientOrigin(out Point origin, out Size clientSize);

    /// <summary>Drops the cached handle so the next <see cref="Refresh"/> re-resolves from scratch.</summary>
    void Reset();
}

/// <summary>
/// Replays a keystroke into one window only. Keyboard input is posted as
/// WM_KEYDOWN/WM_KEYUP to the attached game window, so it can never leak
/// into whatever happens to be focused and the game needs neither focus nor
/// z-order. Mouse buttons and the wheel have no per-window message
/// equivalent and go to the focused window instead — the foreground gate in
/// the engine (game window must be foreground) stays mandatory for those.
/// Stroke type is Keys.KeyRef (same shape as PixelProtocol.KeyStroke); P0b
/// wires the real KeyStroke type to these touchpoints.
/// </summary>
internal interface IKeySender
{
    /// <summary>
    /// Sends a keyboard stroke to <paramref name="target"/>. Returns false
    /// and sends nothing when the handle is dead or belongs to another
    /// process (<paramref name="expectedProcessId"/> non-zero).
    /// </summary>
    bool SendToWindow(IntPtr target, uint expectedProcessId, Keys.KeyRef stroke, int holdMilliseconds);

    /// <summary>Sends a stroke (keyboard or mouse) to the focused window.</summary>
    void Send(Keys.KeyRef stroke, int holdMilliseconds);

    /// <summary>True for mouse codes: buttons 1-5 + the two wheel codes.</summary>
    bool IsMouse(byte virtualKey);

    /// <summary>Human-readable name incl. modifiers and mouse buttons (MB5 etc.).</summary>
    string DescribeStroke(Keys.KeyRef stroke);
}

/// <summary>Monotonic poll-loop clock plus the two sleeps the engine needs.</summary>
internal interface IClock
{
    /// <summary>Milliseconds since the clock started (Stopwatch parity).</summary>
    long ElapsedMs { get; }

    /// <summary>Unconditional sleep (Thread.Sleep parity), used for key-hold timing.</summary>
    void Sleep(int milliseconds);

    /// <summary>Poll-cadence wait point; platform impls may back it with a wait handle.</summary>
    void Wait(int milliseconds);
}

/// <summary>Live physical-key state, so a held movement key is never fought.</summary>
internal interface IPhysicalKeys
{
    /// <summary>True while <paramref name="virtualKey"/> is physically held down now.</summary>
    bool IsPhysicallyDown(byte virtualKey);
}

/// <summary>
/// Sweeps the game's client area for the addon's magic cell, then measures
/// the cell size off the pixels themselves. The engine calls this at most
/// every 5s (relocate throttle: a full client-area sweep + pixel scan is the
/// most expensive thing the process does, and a merely hidden strip would
/// otherwise pay it twice a second for nothing).
/// </summary>
internal interface IBlockScanner
{
    /// <summary>
    /// Scans <paramref name="area"/> (screen coordinates) and returns the
    /// block's position relative to <paramref name="origin"/>, or null if it
    /// is not there. Portable shape of BlockLocator.BlockLocation — P0b
    /// wires the real type.
    /// </summary>
    (int OffsetX, int OffsetY, int CellSize)? Locate(Point origin, Size area);
}

/// <summary>
/// Locates and launches the Battle.net desktop app. Resolution order:
/// settings override, WoW registry InstallPath sibling, well-known install
/// locations. Login always relies on the remembered-account auto-login —
/// credentials are never stored, read, or passed anywhere.
/// </summary>
internal interface ILauncher
{
    /// <summary>Auto-detected launcher exe path, or null when not found.</summary>
    string? InstallPath { get; }

    /// <summary>True when <see cref="InstallPath"/> resolved.</summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Launches the game. Returns false with a human-readable
    /// <paramref name="message"/> when it cannot be found or started.
    /// </summary>
    bool TryLaunch(string? overridePath, out string message);
}

/// <summary>Portable app-directory layout (exe dir, settings.ini, asset cache).</summary>
internal interface IAppPaths
{
    /// <summary>Directory holding the exe; settings and caches live next to it.</summary>
    string AppDir { get; }

    /// <summary>Full path to settings.ini under <see cref="AppDir"/>.</summary>
    string SettingsPath { get; }

    /// <summary>Full path to the assets dir (icon cache) under <see cref="AppDir"/>.</summary>
    string AssetDir { get; }
}

/// <summary>OS permission gates for capture and input (TCC on Mac).</summary>
internal interface IPermissions
{
    /// <summary>True when screen capture is allowed (prompts on Mac when needed).</summary>
    bool CheckCapturePermission();

    /// <summary>True when synthetic input is allowed (prompts on Mac when needed).</summary>
    bool CheckInputPermission();
}
