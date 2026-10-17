//! Platform seam contracts for the Rust companion (docs/plans/2026-10-10-rust-companion.md §3).
//!
//! This crate is **contracts only** — no implementation. It fixes the shapes the
//! engine talks to so the Windows and macOS backends can be swapped without the
//! engine knowing. The three methods mirror the C# seams exactly:
//!
//! | C# (reference)                                | Rust                          |
//! |-----------------------------------------------|-------------------------------|
//! | `WowWindow.Refresh` / `TryGetClientOrigin`    | [`Platform::find_wow`]        |
//! | `ScreenSampler` (BitBlt DIB)                  | [`Platform::capture`]         |
//! | `KeySender.SendToWindow` (PostMessage-only)   | [`Platform::post_key`]        |
//! | `KeySender.Send` mouse half (`SendInput`)     | [`Platform::post_mouse`]      |
//! | `GetForegroundWindow == target` gate          | [`Platform::is_foreground`]   |
//!
//! Safety rule carried by the contract: **keyboard** input is posted to one
//! window only (`PostMessage`-only); **mouse buttons and the wheel** have no
//! per-window message equivalent, so they go through `SendInput` to the focused
//! window behind a **mandatory foreground gate** — [`Platform::post_mouse`]
//! returns [`PlatformError::NotForeground`] and sends nothing when the target is
//! not in front (mirror `KeySender.cs:8-12`). No backend may read memory, inject
//! into another process or scrape credentials. A backend that cannot do this
//! returns [`PlatformError::Unsupported`].

#![forbid(unsafe_code)]

/// Opaque native window handle.
///
/// On Windows this wraps an `HWND`; the value means nothing outside the platform
/// backend. `0` is the "no window" sentinel (C# `IntPtr.Zero`).
#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
pub struct WindowId(pub u64);

impl WindowId {
    /// The null handle — "no window" (C# `IntPtr.Zero`).
    pub const NONE: WindowId = WindowId(0);

    /// True when this is the null handle.
    #[inline]
    pub const fn is_none(self) -> bool {
        self.0 == 0
    }
}

/// A pixel rectangle in **client coordinates** (top-left of the client area is
/// the origin). `width`/`height` are non-negative by contract.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, Hash)]
pub struct Rect {
    /// Left edge, client-space.
    pub x: i32,
    /// Top edge, client-space.
    pub y: i32,
    /// Width in pixels.
    pub width: i32,
    /// Height in pixels.
    pub height: i32,
}

impl Rect {
    /// Builds a rectangle from its components.
    #[inline]
    pub const fn new(x: i32, y: i32, width: i32, height: i32) -> Self {
        Self { x, y, width, height }
    }

    /// True when either dimension is zero or negative.
    #[inline]
    pub const fn is_empty(self) -> bool {
        self.width <= 0 || self.height <= 0
    }
}

/// A captured pixel block, **BGRA8888, top-down, tightly packed**.
///
/// Byte order matches the C# DIB section: four bytes per pixel `[B, G, R, X]`.
/// [`Frame::pixel`] returns `[R, G, B]`, which is what `ScreenSampler.ReadPixel`
/// reports (`Color.FromArgb(p[2], p[1], p[0])`).
#[derive(Clone, PartialEq, Eq, Debug, Default)]
pub struct Frame {
    /// Width in pixels.
    pub width: u32,
    /// Height in pixels.
    pub height: u32,
    /// Raw BGRA bytes, `width * height * 4` long.
    pub pixels: Vec<u8>,
}

impl Frame {
    /// Bytes per row (`width * 4`).
    #[inline]
    pub fn stride(&self) -> usize {
        self.width as usize * 4
    }

    /// True when there is no pixel content.
    #[inline]
    pub fn is_empty(&self) -> bool {
        self.width == 0 || self.height == 0 || self.pixels.is_empty()
    }

    /// Reads the pixel at `(x, y)` as `[R, G, B]`, or `None` when out of bounds.
    ///
    /// This is the safe port of the C# DIB pointer read; callers must not index
    /// [`Frame::pixels`] directly because the layout can change per backend.
    pub fn pixel(&self, x: u32, y: u32) -> Option<[u8; 3]> {
        if x >= self.width || y >= self.height {
            return None;
        }
        let i = y as usize * self.stride() + x as usize * 4;
        self.pixels
            .get(i..i + 3)
            .map(|p| [p[2], p[1], p[0]]) // B,G,R -> R,G,B
    }
}

/// A single keystroke with its modifiers, mirroring C# `KeyStroke`.
///
/// Mouse codes reuse their real virtual-key values (`0x01..0x06`) plus the two
/// wheel pseudo-codes (`0x07`, `0x0B`) exactly as `KeySender` defines them.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, Hash)]
pub struct Key {
    /// Windows virtual-key code (e.g. `0x10` shift, `0x41` 'A').
    pub virtual_key: u8,
    /// Control is held.
    pub ctrl: bool,
    /// Alt is held (routes through `WM_SYSKEY*`, mirroring C# `KeySender`).
    pub alt: bool,
    /// Shift is held.
    pub shift: bool,
}

impl Key {
    /// A plain (no-modifier) key.
    #[inline]
    pub const fn plain(virtual_key: u8) -> Self {
        Self { virtual_key, ctrl: false, alt: false, shift: false }
    }
}

/// A mouse button, the physical inputs WoW binds ("Interact with mouse" etc.).
///
/// These are exactly the five buttons `KeySender.cs:62-66` encodes as virtual
/// keys; the wheel is *not* a button — it is a [`MouseEvent::Wheel`] delta.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Hash)]
pub enum MouseButton {
    /// Physical button 1 (primary click).
    Left,
    /// Physical button 2 (secondary click).
    Right,
    /// Physical button 3 (middle click).
    Middle,
    /// Physical button 4 (`XBUTTON1`).
    X1,
    /// Physical button 5 (`XBUTTON2`).
    X2,
}

/// One notch of wheel rotation (`WHEEL_DELTA`), exactly `KeySender.cs:173`.
///
/// A [`MouseEvent::Wheel`] carries a signed multiple of this.
pub const WHEEL_DELTA: i32 = 120;

/// A single mouse action sent to the focused window.
///
/// This is the mouse half of the C# hybrid (`KeySender.Send` →
/// `AppendMouse`, `KeySender.cs:115-165`). Unlike [`Key`] it has **no
/// per-window message equivalent**, so a backend delivers it with `SendInput`
/// to the *focused* window and MUST gate on the foreground window first.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum MouseEvent {
    /// A button press.
    Down(MouseButton),
    /// A button release.
    Up(MouseButton),
    /// An absolute pointer move to **(x, y) screen pixels** (virtual desktop
    /// origin). Buttons and the wheel in the C# path move nothing (`dx = dy =
    /// 0`); this is the additive absolute-move path.
    Move {
        /// X in screen pixels (virtual-desktop coordinates).
        x: i32,
        /// Y in screen pixels (virtual-desktop coordinates).
        y: i32,
    },
    /// A wheel tick, signed: `+WHEEL_DELTA` is up, `-WHEEL_DELTA` is down
    /// (`KeySender.cs:158-163`).
    Wheel {
        /// Rotation in `WHEEL_DELTA` units.
        delta: i32,
    },
}

/// Errors a platform backend can return. Backends never panic on user input.
#[derive(Clone, PartialEq, Eq, Debug)]
pub enum PlatformError {
    /// The capability is not implemented on this backend (e.g. macOS stub).
    Unsupported(&'static str),
    /// No window matched the configured process name.
    WindowNotFound(String),
    /// The capture path failed (stale GDI surface, DIB allocation, ...).
    CaptureFailed(String),
    /// The input post failed or the target window is gone/foreign.
    PostFailed(String),
    /// A mouse event was refused because the target is not the foreground
    /// window. Mouse/wheel input has no per-window message equivalent, so a
    /// backend MUST send nothing in this case (mirror the C# engine's mandatory
    /// foreground gate, `KeySender.cs:8-12`).
    NotForeground(String),
    /// The caller passed an invalid argument (dead handle, empty region).
    InvalidArgument(String),
}

impl core::fmt::Display for PlatformError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            PlatformError::Unsupported(what) => write!(f, "unsupported on this platform: {what}"),
            PlatformError::WindowNotFound(name) => write!(f, "game window not found: {name}"),
            PlatformError::CaptureFailed(msg) => write!(f, "capture failed: {msg}"),
            PlatformError::PostFailed(msg) => write!(f, "post failed: {msg}"),
            PlatformError::NotForeground(msg) => {
                write!(f, "target is not the foreground window: {msg}")
            }
            PlatformError::InvalidArgument(msg) => write!(f, "invalid argument: {msg}"),
        }
    }
}

impl std::error::Error for PlatformError {}

/// Convenience result alias.
pub type PlatformResult<T> = Result<T, PlatformError>;

/// The platform seam. Implemented by `mdc-platform-win` and `mdc-platform-mac`.
///
/// `&self` (not `&mut self`) so a backend can be shared across the engine's
/// sampler and input paths without an exclusive borrow; backends use interior
/// synchronization if they cache state.
pub trait Platform {
    /// Finds the game window whose **process name** matches `process_name`
    /// (case-insensitive; a trailing `.exe` is ignored, so `Wow`, `WowClassic`
    /// and `Legion` all work). Mirrors `WowWindow.Refresh`.
    fn find_wow(&self, process_name: &str) -> PlatformResult<WindowId>;

    /// Captures `region` (client coordinates) of `window` and returns the raw
    /// BGRA block. Mirrors `ScreenSampler.Sample` (one BitBlt into a top-down
    /// 32bpp DIB, `SRCCOPY | CAPTUREBLT`).
    fn capture(&self, window: WindowId, region: Rect) -> PlatformResult<Frame>;

    /// Posts one keystroke to `window` only — **PostMessage-only**, never
    /// `SendInput`. Holds the key for `hold_ms` between down and up. Mirrors
    /// `KeySender.SendToWindow`.
    fn post_key(&self, window: WindowId, key: &Key, hold_ms: u32) -> PlatformResult<()>;

    /// Sends one mouse action to the **focused** window with `SendInput`.
    ///
    /// Mouse buttons and the wheel have no per-window message, so unlike
    /// [`Platform::post_key`] this cannot be targeted and MUST first check that
    /// `window` is the OS foreground window (mirror the C# engine's mandatory
    /// gate, `KeySender.cs:8-12`). When it is not, the backend sends nothing and
    /// returns [`PlatformError::NotForeground`]. Mirrors the mouse half of
    /// `KeySender.Send` (`KeySender.cs:105-165`).
    ///
    /// Defaulted to [`PlatformError::Unsupported`] so that adding it did not
    /// break out-of-tree backends; the shipped Win/Mac backends override it.
    fn post_mouse(&self, _window: WindowId, _event: &MouseEvent) -> PlatformResult<()> {
        Err(PlatformError::Unsupported("post_mouse"))
    }

    /// True when `window` is the current foreground window — the gate behind
    /// [`Platform::post_mouse`] (C# `Native.GetForegroundWindow`).
    ///
    /// Defaulted to [`PlatformError::Unsupported`] for additive compatibility;
    /// the shipped Win/Mac backends override it.
    fn is_foreground(&self, _window: WindowId) -> PlatformResult<bool> {
        Err(PlatformError::Unsupported("is_foreground"))
    }
}
