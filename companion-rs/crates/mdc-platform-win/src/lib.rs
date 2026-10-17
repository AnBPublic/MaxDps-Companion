//! Windows platform backend — real implementation, **dependency-free**.
//!
//! The Win32 subset used here is declared with `extern "system"` rather than the
//! heavyweight `windows` crate, so the crate builds offline (no registry cache
//! needed). The behaviour is a line-for-line port of the C# reference:
//!
//! - **Capture** mirrors `ScreenSampler.cs`: one `BitBlt` of the client region
//!   into a top-down 32bpp `CreateDIBSection`, `SRCCOPY | CAPTUREBLT`, bytes
//!   read straight from the mapped buffer (no per-pixel `GetPixel` syscalls).
//! - **Keyboard input** mirrors `KeySender.SendToWindow`: `WM_KEYDOWN/WM_KEYUP`
//!   (and the `WM_SYS*` variants when Alt is held) posted with `PostMessageW`.
//!   Keys are **PostMessage-only, never `SendInput`** — a key can never leak into
//!   the focused app.
//! - **Mouse input** mirrors the mouse half of `KeySender.Send` (`KeySender.cs:
//!   105-165`): buttons/wheel/absolute-move go through `SendInput` because they
//!   have **no per-window message equivalent**. That path *cannot be targeted*,
//!   so [`WinPlatform::post_mouse`] enforces the C# engine's **mandatory
//!   foreground gate** (`KeySender.cs:8-12,125,127`) first: unless the target is
//!   the OS foreground window, it returns [`PlatformError::NotForeground`] and
//!   sends nothing.
//! - **Finding** mirrors `WowWindow`: match the process name (case-insensitive,
//!   `.exe` ignored) and require a non-empty client area.
//!
//! # Safety exception
//!
//! Every other crate in the workspace carries `#![forbid(unsafe_code)]`. This
//! one is the **documented exception**: it is the sole FFI boundary, it uses
//! `unsafe` only for the Win32 `extern "system"` calls above and never calls
//! `ReadProcessMemory`/`WriteProcessMemory`, never injects into another process.
//! `OpenProcess` (in the finder) requests `PROCESS_QUERY_LIMITED_INFORMATION`
//! only, mirroring `WowWindow`.
//!
//! On a non-Windows host the crate still compiles and every method returns
//! [`mdc_platform::PlatformError::Unsupported`].

use mdc_platform::{
    Frame, Key, MouseEvent, Platform, PlatformError, PlatformResult, Rect, WindowId,
};

#[cfg(target_os = "windows")]
mod imp {
    use super::*;
    use core::ffi::c_void;
    use core::ptr;
    use mdc_platform::MouseButton;
    use std::time::Duration;

    // ---- Win32 scalar aliases -------------------------------------------------

    type Hwnd = *mut c_void;
    type Hdc = *mut c_void;
    type Hbitmap = *mut c_void;
    type Hgdiobj = *mut c_void;
    type Handle = *mut c_void;
    type Bool = i32;
    type Lparam = isize;
    type Wparam = usize;

    const NULL_HWND: Hwnd = ptr::null_mut();

    // GDI / ROP
    const SRCCOPY: u32 = 0x00CC_0020;
    const CAPTUREBLT: u32 = 0x4000_0000;
    const DIB_RGB_COLORS: u32 = 0;
    const BI_RGB: u32 = 0;

    // Process access
    const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;

    // Input messages
    const WM_KEYDOWN: u32 = 0x0100;
    const WM_KEYUP: u32 = 0x0101;
    const WM_SYSKEYDOWN: u32 = 0x0104;
    const WM_SYSKEYUP: u32 = 0x0105;
    const MAPVK_VK_TO_VSC: u32 = 0;

    // Modifier virtual keys (mirror KeySender.cs)
    const VK_SHIFT: u8 = 0x10;
    const VK_CONTROL: u8 = 0x11;
    const VK_MENU: u8 = 0x12;

    /// Extended keys (mirror KeySender.ExtendedKeys): page/home/end/arrows,
    /// insert, delete, numpad-divide, num-lock.
    const EXTENDED_KEYS: [u8; 10] =
        [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E];
    const VK_NUMPAD_DIVIDE: u8 = 0x6F;
    const VK_NUM_LOCK: u8 = 0x90;

    #[inline]
    fn is_extended(vk: u8) -> bool {
        EXTENDED_KEYS.contains(&vk) || vk == VK_NUMPAD_DIVIDE || vk == VK_NUM_LOCK
    }

    // ---- DPI awareness (mirror Native.cs:134-141) -----------------------------

    /// `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`, i.e. the Win32
    /// `(HANDLE)-4` sentinel (`Native.cs:138`).
    const DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: isize = -4;

    // ---- Mouse injection (mirror Native.cs:145-173, KeySender.AppendMouse) ----

    const INPUT_MOUSE: u32 = 0;
    const MOUSEEVENTF_MOVE: u32 = 0x0001;
    const MOUSEEVENTF_LEFTDOWN: u32 = 0x0002;
    const MOUSEEVENTF_LEFTUP: u32 = 0x0004;
    const MOUSEEVENTF_RIGHTDOWN: u32 = 0x0008;
    const MOUSEEVENTF_RIGHTUP: u32 = 0x0010;
    const MOUSEEVENTF_MIDDLEDOWN: u32 = 0x0020;
    const MOUSEEVENTF_MIDDLEUP: u32 = 0x0040;
    const MOUSEEVENTF_XDOWN: u32 = 0x0080;
    const MOUSEEVENTF_XUP: u32 = 0x0100;
    const MOUSEEVENTF_WHEEL: u32 = 0x0800;
    // Absolute move: (dx, dy) are normalised to the virtual desktop, not pixels,
    // unless VIRTUALDESK is combined in. See `encode_mouse`.
    const MOUSEEVENTF_ABSOLUTE: u32 = 0x8000;
    const MOUSEEVENTF_VIRTUALDESK: u32 = 0x4000;
    const XBUTTON1: u32 = 0x0001;
    const XBUTTON2: u32 = 0x0002;

    /// Virtual-screen metrics for absolute-pointer normalisation
    /// (`GetSystemMetrics`, `SM_*VIRTUALSCREEN`).
    const SM_XVIRTUALSCREEN: i32 = 76;
    const SM_YVIRTUALSCREEN: i32 = 77;
    const SM_CXVIRTUALSCREEN: i32 = 78;
    const SM_CYVIRTUALSCREEN: i32 = 79;

    // ---- Win32 structs --------------------------------------------------------

    #[repr(C)]
    #[derive(Clone, Copy, Default)]
    struct WinRect {
        left: i32,
        top: i32,
        right: i32,
        bottom: i32,
    }

    #[repr(C)]
    #[derive(Clone, Copy, Default)]
    struct WinPoint {
        x: i32,
        y: i32,
    }

    /// Win32 `MOUSEINPUT` (`Native.cs:176-184`).
    #[repr(C)]
    #[derive(Clone, Copy, Default, Debug)]
    struct MouseInput {
        dx: i32,
        dy: i32,
        mouse_data: u32,
        flags: u32,
        time: u32,
        extra_info: usize,
    }

    /// Win32 `INPUT` specialised for the mouse half (`Native.cs:186-192`).
    ///
    /// On x64 the union is 8-aligned (`LONG_PTR dwExtraInfo`), so a 4-byte pad
    /// sits between the tag and the payload — exactly C#'s `[FieldOffset(8)] mi`
    /// with `Size = 40`. `input_layout_is_40_bytes` pins this.
    #[repr(C)]
    #[derive(Clone, Copy, Default, Debug)]
    struct Input {
        input_type: u32,
        _pad: u32,
        mi: MouseInput,
    }

    /// Virtual-desktop bounds in physical pixels (`GetSystemMetrics`).
    #[derive(Clone, Copy, PartialEq, Eq, Debug)]
    struct ScreenMetrics {
        x: i32,
        y: i32,
        width: i32,
        height: i32,
    }

    #[repr(C)]
    #[derive(Clone, Copy, Default)]
    struct BitmapInfoHeader {
        bi_size: u32,
        bi_width: i32,
        bi_height: i32,
        bi_planes: u16,
        bi_bit_count: u16,
        bi_compression: u32,
        bi_size_image: u32,
        bi_x_pels_per_meter: i32,
        bi_y_pels_per_meter: i32,
        bi_clr_used: u32,
        bi_clr_important: u32,
    }

    #[repr(C)]
    #[derive(Clone, Copy)]
    struct BitmapInfo {
        header: BitmapInfoHeader,
        colors: [u32; 1],
    }

    // ---- Win32 imports --------------------------------------------------------

    #[link(name = "user32")]
    extern "system" {
        fn GetDC(hwnd: Hwnd) -> Hdc;
        fn ReleaseDC(hwnd: Hwnd, hdc: Hdc) -> i32;
        fn GetClientRect(hwnd: Hwnd, rect: *mut WinRect) -> Bool;
        fn ClientToScreen(hwnd: Hwnd, point: *mut WinPoint) -> Bool;
        fn IsWindow(hwnd: Hwnd) -> Bool;
        fn GetWindowThreadProcessId(hwnd: Hwnd, pid: *mut u32) -> u32;
        fn PostMessageW(hwnd: Hwnd, msg: u32, wparam: Wparam, lparam: Lparam) -> Bool;
        fn MapVirtualKeyW(code: u32, map_type: u32) -> u32;
        fn EnumWindows(callback: extern "system" fn(Hwnd, Lparam) -> Bool, lparam: Lparam) -> Bool;
        fn GetForegroundWindow() -> Hwnd;
        fn GetSystemMetrics(index: i32) -> i32;
        fn SendInput(count: u32, inputs: *const Input, size: i32) -> u32;
        fn SetProcessDpiAwarenessContext(context: isize) -> Bool;
    }

    #[link(name = "gdi32")]
    extern "system" {
        fn CreateCompatibleDC(hdc: Hdc) -> Hdc;
        fn CreateDIBSection(
            hdc: Hdc,
            pbmi: *const BitmapInfo,
            usage: u32,
            bits: *mut *mut c_void,
            section: Handle,
            offset: u32,
        ) -> Hbitmap;
        fn SelectObject(hdc: Hdc, obj: Hgdiobj) -> Hgdiobj;
        fn DeleteObject(obj: Hgdiobj) -> Bool;
        fn DeleteDC(hdc: Hdc) -> Bool;
        fn BitBlt(
            dst: Hdc,
            x: i32,
            y: i32,
            width: i32,
            height: i32,
            src: Hdc,
            src_x: i32,
            src_y: i32,
            rop: u32,
        ) -> Bool;
    }

    #[link(name = "kernel32")]
    extern "system" {
        fn OpenProcess(access: u32, inherit: Bool, pid: u32) -> Handle;
        fn CloseHandle(handle: Handle) -> Bool;
        fn QueryFullProcessImageNameW(
            process: Handle,
            flags: u32,
            name: *mut u16,
            size: *mut u32,
        ) -> Bool;
    }

    // ---- RAII guards ----------------------------------------------------------

    /// Owns a memory DC + DIB section and releases both on drop (no GDI leak on
    /// an early return, matching `ScreenSampler.ReleaseSurface`).
    struct DibSurface {
        mem_dc: Hdc,
        bitmap: Hbitmap,
        old_obj: Hgdiobj,
        bits: *mut c_void,
    }

    impl DibSurface {
        fn new(screen_dc: Hdc, width: i32, height: i32) -> Option<Self> {
            let header = BitmapInfoHeader {
                bi_size: core::mem::size_of::<BitmapInfoHeader>() as u32,
                bi_width: width,
                bi_height: -height, // top-down: y grows downward
                bi_planes: 1,
                bi_bit_count: 32,
                bi_compression: BI_RGB,
                ..Default::default()
            };
            let info = BitmapInfo { header, colors: [0] };

            let mem_dc = unsafe { CreateCompatibleDC(screen_dc) };
            if mem_dc.is_null() {
                return None;
            }

            let mut bits: *mut c_void = ptr::null_mut();
            let bitmap =
                unsafe { CreateDIBSection(screen_dc, &info, DIB_RGB_COLORS, &mut bits, ptr::null_mut(), 0) };
            if bitmap.is_null() || bits.is_null() {
                unsafe { DeleteDC(mem_dc) };
                return None;
            }

            let old_obj = unsafe { SelectObject(mem_dc, bitmap) };
            Some(Self { mem_dc, bitmap, old_obj, bits })
        }

        fn bits(&self) -> *const u8 {
            self.bits as *const u8
        }
    }

    impl Drop for DibSurface {
        fn drop(&mut self) {
            unsafe {
                if !self.old_obj.is_null() {
                    SelectObject(self.mem_dc, self.old_obj);
                }
                if !self.bitmap.is_null() {
                    DeleteObject(self.bitmap);
                }
                if !self.mem_dc.is_null() {
                    DeleteDC(self.mem_dc);
                }
            }
        }
    }

    /// Releases a screen DC on drop (C# `ReleaseDC(IntPtr.Zero, dc)`).
    struct ScreenDc(Hdc);

    impl ScreenDc {
        fn acquire() -> Option<Self> {
            let dc = unsafe { GetDC(NULL_HWND) };
            if dc.is_null() {
                None
            } else {
                Some(Self(dc))
            }
        }

        fn raw(&self) -> Hdc {
            self.0
        }
    }

    impl Drop for ScreenDc {
        fn drop(&mut self) {
            unsafe { ReleaseDC(NULL_HWND, self.0) };
        }
    }

    // ---- Window search --------------------------------------------------------

    /// Context handed to `EnumWindows` via `LPARAM`.
    struct FindCtx<'a> {
        wanted: &'a str,
        found: Hwnd,
    }

    extern "system" fn enum_proc(hwnd: Hwnd, lparam: Lparam) -> Bool {
        // SAFETY: `lparam` is the `&mut FindCtx` pointer we passed to
        // EnumWindows below and it outlives the synchronous callback (the
        // callback can only run while `EnumWindows` is on the stack).
        //
        // A 0 return stops enumeration early because the match was found. That
        // is a normal early-stop, NOT an error: `find_wow` never inspects
        // EnumWindows' own BOOL result, it reads `ctx.found`.
        let ctx = unsafe { &mut *(lparam as *mut FindCtx) };
        if unsafe { IsWindow(hwnd) } == 0 {
            return 1; // keep enumerating
        }

        let mut pid: u32 = 0;
        unsafe { GetWindowThreadProcessId(hwnd, &mut pid) };
        if pid == 0 {
            return 1;
        }

        let matches = process_stem(pid)
            .map(|stem| stem.eq_ignore_ascii_case(ctx.wanted))
            .unwrap_or(false);
        if !matches {
            return 1;
        }

        // Require a real (non-empty) client area, like WowWindow's
        // `MainWindowHandle != 0` + `TryGetClientOrigin` guard.
        let mut rect = WinRect::default();
        if unsafe { GetClientRect(hwnd, &mut rect) } != 0
            && rect.right > rect.left
            && rect.bottom > rect.top
        {
            ctx.found = hwnd;
            return 0; // stop enumeration
        }
        1
    }

    /// Process file stem for `pid` (e.g. `WowClassic`), or `None` if it cannot
    /// be queried (access denied / exited).
    fn process_stem(pid: u32) -> Option<String> {
        let handle = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) };
        if handle.is_null() {
            return None;
        }

        let mut buf = [0u16; 260];
        let mut len = buf.len() as u32;
        let ok = unsafe { QueryFullProcessImageNameW(handle, 0, buf.as_mut_ptr(), &mut len) };
        unsafe { CloseHandle(handle) };
        if ok == 0 || len == 0 {
            return None;
        }

        let path = String::from_utf16_lossy(&buf[..len as usize]);
        let file = path.rsplit(['\\', '/']).next().unwrap_or(&path);
        let stem = file.rsplit_once('.').map(|(s, _)| s).unwrap_or(file);
        if stem.is_empty() {
            None
        } else {
            Some(stem.to_string())
        }
    }

    fn normalise_process(name: &str) -> String {
        let trimmed = name.trim();
        let without_exe = trimmed
            .strip_suffix(".exe")
            .or_else(|| trimmed.strip_suffix(".EXE"))
            .unwrap_or(trimmed);
        without_exe.to_string()
    }

    // ---- Mouse encoding + foreground gate -------------------------------------

    /// Normalises a pixel coordinate to the Win32 absolute 0..=65535 range over
    /// `[origin, origin + size)`. `SM_*VIRTUALSCREEN` gives the origin and size
    /// of the whole desktop, matching `MOUSEEVENTF_VIRTUALDESK`.
    fn normalise_axis(value: i32, origin: i32, size: i32) -> i32 {
        if size <= 1 {
            return 0;
        }
        let clamped = value.clamp(origin, origin + size - 1);
        (((clamped - origin) as i64 * 65535) / (size - 1) as i64).clamp(0, 65535) as i32
    }

    /// The button flags + `mouseData` for one press/release, mirroring
    /// `KeySender.AppendMouse` (`KeySender.cs:134-165`).
    fn button_input(button: MouseButton, release: bool) -> MouseInput {
        let (down_flags, up_flags, data) = match button {
            MouseButton::Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0),
            MouseButton::Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0),
            MouseButton::Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0),
            MouseButton::X1 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1),
            MouseButton::X2 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2),
        };
        MouseInput {
            mouse_data: data,
            flags: if release { up_flags } else { down_flags },
            ..MouseInput::default()
        }
    }

    /// Pure encoder: turns a [`MouseEvent`] into the Win32 `INPUT` batch to send.
    ///
    /// No side effects, so the offline tests assert the exact flags/coords
    /// without touching the input queue. Buttons and wheel move nothing
    /// (`dx = dy = 0`, mirroring the C# `Mouse` helper); only [`MouseEvent::Move`]
    /// carries coordinates, normalised to the virtual desktop
    /// (`MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`).
    ///
    /// DPI note: since Windows 8.1 `SendInput` absolute coordinates are always
    /// interpreted in the physical virtual-desktop space *when the calling
    /// process is per-monitor DPI aware*. [`WinPlatform::new`] sets that
    /// awareness via `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`,
    /// mirroring `Native.cs:134-141`. Without it a scaled display would
    /// virtualise the coordinates and land the pointer in the wrong pixel.
    fn encode_mouse(event: &MouseEvent, metrics: ScreenMetrics) -> Vec<Input> {
        let into_input = |mi: MouseInput| Input { input_type: INPUT_MOUSE, _pad: 0, mi };

        match *event {
            MouseEvent::Down(button) => vec![into_input(button_input(button, false))],
            MouseEvent::Up(button) => vec![into_input(button_input(button, true))],
            MouseEvent::Wheel { delta } => vec![into_input(MouseInput {
                // `delta as u32` carries the two's-complement sign, matching
                // `unchecked((uint)(±WHEEL_DELTA))` in `KeySender.cs:159,162`.
                mouse_data: delta as u32,
                flags: MOUSEEVENTF_WHEEL,
                ..MouseInput::default()
            })],
            MouseEvent::Move { x, y } => vec![into_input(MouseInput {
                dx: normalise_axis(x, metrics.x, metrics.width),
                dy: normalise_axis(y, metrics.y, metrics.height),
                flags: MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                ..MouseInput::default()
            })],
        }
    }

    /// The side-effecting OS calls the mouse path needs, behind a trait so the
    /// **offline tests can drive the foreground gate and sink with a mock**.
    trait MouseHost {
        /// `IsWindow(hwnd)` for the target.
        fn is_window(&self, window: WindowId) -> bool;
        /// `GetForegroundWindow() == hwnd`.
        fn is_foreground(&self, window: WindowId) -> bool;
        /// `GetSystemMetrics` virtual-desktop bounds.
        fn screen_metrics(&self) -> ScreenMetrics;
        /// `SendInput` the batch, all-or-nothing.
        fn send_inputs(&self, inputs: &[Input]) -> PlatformResult<()>;
    }

    /// The real OS host, backed by the Win32 imports above.
    struct WinHost;

    impl MouseHost for WinHost {
        fn is_window(&self, window: WindowId) -> bool {
            let hwnd = hwnd_of(window);
            !hwnd.is_null() && unsafe { IsWindow(hwnd) } != 0
        }

        fn is_foreground(&self, window: WindowId) -> bool {
            let hwnd = hwnd_of(window);
            if hwnd.is_null() {
                return false;
            }
            let foreground = unsafe { GetForegroundWindow() };
            !foreground.is_null() && foreground == hwnd
        }

        fn screen_metrics(&self) -> ScreenMetrics {
            ScreenMetrics {
                x: unsafe { GetSystemMetrics(SM_XVIRTUALSCREEN) },
                y: unsafe { GetSystemMetrics(SM_YVIRTUALSCREEN) },
                width: unsafe { GetSystemMetrics(SM_CXVIRTUALSCREEN) },
                height: unsafe { GetSystemMetrics(SM_CYVIRTUALSCREEN) },
            }
        }

        fn send_inputs(&self, inputs: &[Input]) -> PlatformResult<()> {
            let count = inputs.len() as u32;
            let sent = unsafe {
                SendInput(count, inputs.as_ptr(), core::mem::size_of::<Input>() as i32)
            };
            if sent == count {
                Ok(())
            } else {
                Err(PlatformError::PostFailed(format!(
                    "SendInput inserted {sent}/{count} mouse events"
                )))
            }
        }
    }

    /// Shared implementation of [`Platform::post_mouse`], generic over the host so
    /// the offline tests can substitute a mock. **Foreground-gated**: a target
    /// that is gone is a `PostFailed`, a live target that is not in front is a
    /// [`PlatformError::NotForeground`] and **nothing is sent** (C# parity:
    /// `KeySender.cs:8-12`).
    fn post_mouse_with<H: MouseHost>(
        host: &H,
        window: WindowId,
        event: &MouseEvent,
    ) -> PlatformResult<()> {
        if !host.is_window(window) {
            return Err(PlatformError::PostFailed(format!(
                "target window {} is gone",
                window.0
            )));
        }
        if !host.is_foreground(window) {
            return Err(PlatformError::NotForeground(format!(
                "window {} is not in front; mouse input refused",
                window.0
            )));
        }

        let inputs = encode_mouse(event, host.screen_metrics());
        if inputs.is_empty() {
            return Err(PlatformError::InvalidArgument(
                "empty mouse input batch".to_string(),
            ));
        }
        host.send_inputs(&inputs)
    }

    // ---- Backend --------------------------------------------------------------

    /// Windows [`Platform`] backend.
    #[derive(Debug, Default, Clone, Copy)]
    pub struct WinPlatform;

    impl WinPlatform {
        /// Creates the backend.
        ///
        /// On first construction this opts the process into per-monitor-v2 DPI
        /// awareness, mirroring `Native.cs:134-141`. Without it Windows
        /// virtualises window coordinates on a scaled display and every sampled
        /// pixel lands in the wrong place. Set once per process (a second call
        /// would fail with `ERROR_ACCESS_DENIED` once awareness is already set);
        /// failure is non-fatal and only affects scaled displays.
        pub fn new() -> Self {
            static DPI_ONCE: std::sync::Once = std::sync::Once::new();
            DPI_ONCE.call_once(|| {
                // SAFETY: pure user32 call with a sentinel value, no pointers;
                // the BOOL result is advisory and deliberately ignored.
                unsafe {
                    let _ = SetProcessDpiAwarenessContext(
                        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
                    );
                }
            });
            Self
        }
    }

    fn hwnd_of(id: WindowId) -> Hwnd {
        id.0 as usize as Hwnd
    }

    impl Platform for WinPlatform {
        fn find_wow(&self, process_name: &str) -> PlatformResult<WindowId> {
            let wanted = normalise_process(process_name);
            if wanted.is_empty() {
                return Err(PlatformError::InvalidArgument(
                    "empty process name".to_string(),
                ));
            }

            let mut ctx = FindCtx { wanted: &wanted, found: NULL_HWND };
            // SAFETY: `ctx` lives for the duration of the synchronous call and
            // is only read/written from this thread. `FindCtx` outlives the
            // whole `EnumWindows` call, so the pointer handed to the callback
            // stays valid for every invocation.
            //
            // EnumWindows returns FALSE (0) when `enum_proc` stops early via a
            // 0 return; that is the normal "found it" path, not a failure, so
            // the BOOL result is deliberately ignored and `ctx.found` is the
            // sole signal.
            unsafe {
                EnumWindows(enum_proc, &mut ctx as *mut FindCtx as Lparam);
            }

            if ctx.found.is_null() {
                Err(PlatformError::WindowNotFound(wanted))
            } else {
                Ok(WindowId(ctx.found as usize as u64))
            }
        }

        fn capture(&self, window: WindowId, region: Rect) -> PlatformResult<Frame> {
            let hwnd = hwnd_of(window);
            if hwnd.is_null() || unsafe { IsWindow(hwnd) } == 0 {
                return Err(PlatformError::WindowNotFound(format!("handle {}", window.0)));
            }
            if region.is_empty() {
                return Err(PlatformError::InvalidArgument(
                    "capture region is empty".to_string(),
                ));
            }

            // Client origin in screen space (WowWindow.TryGetClientOrigin).
            let mut point = WinPoint::default();
            if unsafe { ClientToScreen(hwnd, &mut point) } == 0 {
                return Err(PlatformError::CaptureFailed(
                    "ClientToScreen failed".to_string(),
                ));
            }

            let screen = ScreenDc::acquire()
                .ok_or_else(|| PlatformError::CaptureFailed("GetDC(NULL) failed".to_string()))?;

            let surface = DibSurface::new(screen.raw(), region.width, region.height)
                .ok_or_else(|| PlatformError::CaptureFailed("CreateDIBSection failed".to_string()))?;

            let src_x = point.x + region.x;
            let src_y = point.y + region.y;
            let blt = unsafe {
                BitBlt(
                    surface.mem_dc,
                    0,
                    0,
                    region.width,
                    region.height,
                    screen.raw(),
                    src_x,
                    src_y,
                    SRCCOPY | CAPTUREBLT,
                )
            };
            if blt == 0 {
                return Err(PlatformError::CaptureFailed("BitBlt failed".to_string()));
            }

            let byte_len = region.width as usize * region.height as usize * 4;
            // 32-bpp top-down DIB: rows are DWORD-aligned, i.e. exactly
            // `width * 4` bytes (already a multiple of 4), so the buffer the
            // DIB hands back is precisely `stride * height` with no trailing
            // padding. Assert the invariant the raw slice relies on.
            let stride = region.width as usize * 4;
            debug_assert_eq!(
                byte_len,
                stride * region.height as usize,
                "DIB slice length must equal stride * height"
            );
            // SAFETY: the DIB is `width * height * 4` bytes by construction and
            // `bits()` points at its first byte.
            let pixels =
                unsafe { core::slice::from_raw_parts(surface.bits(), byte_len) }.to_vec();

            Ok(Frame {
                width: region.width as u32,
                height: region.height as u32,
                pixels,
            })
        }

        fn post_key(&self, window: WindowId, key: &Key, hold_ms: u32) -> PlatformResult<()> {
            let hwnd = hwnd_of(window);
            if hwnd.is_null() || unsafe { IsWindow(hwnd) } == 0 {
                return Err(PlatformError::PostFailed(format!(
                    "target window {} is gone",
                    window.0
                )));
            }

            // Ordered modifier list: ctrl, alt, shift, then the key itself
            // (mirror KeySender.SendToWindow). `alt` marks SYS messages.
            let mut keys: Vec<(u8, bool)> = Vec::with_capacity(4);
            if key.ctrl {
                keys.push((VK_CONTROL, false));
            }
            if key.alt {
                keys.push((VK_MENU, true));
            }
            if key.shift {
                keys.push((VK_SHIFT, false));
            }
            keys.push((key.virtual_key, key.alt));

            for &(vk, alt) in &keys {
                let scan = unsafe { MapVirtualKeyW(vk as u32, MAPVK_VK_TO_VSC) };
                let mut lparam = 1i32 | ((scan as i32) << 16);
                if is_extended(vk) {
                    lparam |= 1 << 24;
                }
                let msg = if key.alt || alt { WM_SYSKEYDOWN } else { WM_KEYDOWN };
                let posted = unsafe { PostMessageW(hwnd, msg, vk as Wparam, lparam as Lparam) };
                if posted == 0 {
                    return Err(PlatformError::PostFailed(format!(
                        "PostMessageW keydown 0x{vk:02X} failed for window {}",
                        window.0
                    )));
                }
            }

            if hold_ms > 0 {
                std::thread::sleep(Duration::from_millis(hold_ms as u64));
            }

            for &(vk, alt) in keys.iter().rev() {
                let scan = unsafe { MapVirtualKeyW(vk as u32, MAPVK_VK_TO_VSC) };
                let mut lparam = 1i32 | ((scan as i32) << 16) | (1 << 30) | (1i32 << 31);
                if is_extended(vk) {
                    lparam |= 1 << 24;
                }
                let msg = if key.alt || alt { WM_SYSKEYUP } else { WM_KEYUP };
                let posted = unsafe { PostMessageW(hwnd, msg, vk as Wparam, lparam as Lparam) };
                if posted == 0 {
                    return Err(PlatformError::PostFailed(format!(
                        "PostMessageW keyup 0x{vk:02X} failed for window {}",
                        window.0
                    )));
                }
            }

            Ok(())
        }

        fn post_mouse(&self, window: WindowId, event: &MouseEvent) -> PlatformResult<()> {
            post_mouse_with(&WinHost, window, event)
        }

        fn is_foreground(&self, window: WindowId) -> PlatformResult<bool> {
            Ok(WinHost.is_foreground(window))
        }
    }

    #[cfg(test)]
    mod tests {
        use super::*;
        use mdc_platform::WHEEL_DELTA;
        use std::cell::RefCell;

        /// Records what the gate/sink were asked to do, so the tests can assert
        /// the foreground gate refuses *before* any input is built or sent.
        #[derive(Default)]
        struct MockHost {
            window_ok: bool,
            foreground: bool,
            sent: RefCell<Vec<Input>>,
        }

        impl MouseHost for MockHost {
            fn is_window(&self, _window: WindowId) -> bool {
                self.window_ok
            }
            fn is_foreground(&self, _window: WindowId) -> bool {
                self.foreground
            }
            fn screen_metrics(&self) -> ScreenMetrics {
                ScreenMetrics { x: 0, y: 0, width: 1920, height: 1080 }
            }
            fn send_inputs(&self, inputs: &[Input]) -> PlatformResult<()> {
                self.sent.borrow_mut().extend_from_slice(inputs);
                Ok(())
            }
        }

        fn window() -> WindowId {
            WindowId(0x1234)
        }

        #[test]
        fn input_layout_is_40_bytes() {
            // C# INPUT is Size=40 on x64; SendInput's cbSize must match exactly.
            assert_eq!(core::mem::size_of::<Input>(), 40);
            assert_eq!(core::mem::size_of::<MouseInput>(), 32);
        }

        #[test]
        fn direction_buttons_encode_expected_flags() {
            let enc = |e: MouseEvent| encode_mouse(&e, ScreenMetrics { x: 0, y: 0, width: 100, height: 100 });
            assert_eq!(enc(MouseEvent::Down(MouseButton::Left))[0].mi.flags, MOUSEEVENTF_LEFTDOWN);
            assert_eq!(enc(MouseEvent::Up(MouseButton::Left))[0].mi.flags, MOUSEEVENTF_LEFTUP);
            assert_eq!(enc(MouseEvent::Down(MouseButton::Right))[0].mi.flags, MOUSEEVENTF_RIGHTDOWN);
            assert_eq!(enc(MouseEvent::Up(MouseButton::Right))[0].mi.flags, MOUSEEVENTF_RIGHTUP);
            assert_eq!(enc(MouseEvent::Down(MouseButton::Middle))[0].mi.flags, MOUSEEVENTF_MIDDLEDOWN);
            assert_eq!(enc(MouseEvent::Up(MouseButton::Middle))[0].mi.flags, MOUSEEVENTF_MIDDLEUP);
        }

        #[test]
        fn x_buttons_carry_xbutton_data_and_move_nothing() {
            let enc = |e: MouseEvent| encode_mouse(&e, ScreenMetrics { x: 0, y: 0, width: 100, height: 100 });
            let x1 = enc(MouseEvent::Down(MouseButton::X1))[0].mi;
            assert_eq!(x1.flags, MOUSEEVENTF_XDOWN);
            assert_eq!(x1.mouse_data, XBUTTON1);
            assert_eq!((x1.dx, x1.dy), (0, 0));
            let x2 = enc(MouseEvent::Up(MouseButton::X2))[0].mi;
            assert_eq!(x2.flags, MOUSEEVENTF_XUP);
            assert_eq!(x2.mouse_data, XBUTTON2);
        }

        #[test]
        fn wheel_uses_signed_delta_and_no_move() {
            let enc = |e: MouseEvent| encode_mouse(&e, ScreenMetrics { x: 0, y: 0, width: 100, height: 100 });
            let up = enc(MouseEvent::Wheel { delta: WHEEL_DELTA })[0].mi;
            assert_eq!(up.flags, MOUSEEVENTF_WHEEL);
            assert_eq!(up.mouse_data, WHEEL_DELTA as u32);
            let down = enc(MouseEvent::Wheel { delta: -WHEEL_DELTA })[0].mi;
            assert_eq!(down.mouse_data, (-WHEEL_DELTA) as u32); // 2^32 - 120, as C# unchecked
            assert_eq!((down.dx, down.dy), (0, 0));
        }

        #[test]
        fn move_normalises_to_virtual_desktop() {
            // Secondary monitor starting at x=1920: left edge -> 0, right edge -> 65535.
            let metrics = ScreenMetrics { x: 1920, y: 0, width: 1920, height: 1080 };
            let at = |x: i32, y: i32| encode_mouse(&MouseEvent::Move { x, y }, metrics)[0].mi;
            let left = at(1920, 0);
            assert_eq!((left.dx, left.dy), (0, 0));
            assert_eq!(left.flags, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
            let right = at(3839, 1079);
            assert_eq!((right.dx, right.dy), (65535, 65535));
            // Out-of-range clamps instead of wrapping.
            let clamped = at(0, 999_999);
            assert_eq!((clamped.dx, clamped.dy), (0, 65535));
        }

        #[test]
        fn post_mouse_refuses_when_not_foreground_and_sends_nothing() {
            let host = MockHost { window_ok: true, foreground: false, sent: RefCell::new(Vec::new()) };
            let err = post_mouse_with(&host, window(), &MouseEvent::Down(MouseButton::Left)).unwrap_err();
            assert!(matches!(err, PlatformError::NotForeground(_)), "got {err:?}");
            assert!(host.sent.borrow().is_empty(), "gate must refuse before building/sending input");
        }

        #[test]
        fn post_mouse_refuses_dead_window() {
            let host = MockHost { window_ok: false, foreground: false, sent: RefCell::new(Vec::new()) };
            let err = post_mouse_with(&host, window(), &MouseEvent::Wheel { delta: WHEEL_DELTA }).unwrap_err();
            assert!(matches!(err, PlatformError::PostFailed(_)), "got {err:?}");
            assert!(host.sent.borrow().is_empty());
        }

        #[test]
        fn post_mouse_sends_when_foreground() {
            let host = MockHost { window_ok: true, foreground: true, sent: RefCell::new(Vec::new()) };
            post_mouse_with(&host, window(), &MouseEvent::Down(MouseButton::Right)).unwrap();
            let sent = host.sent.borrow();
            assert_eq!(sent.len(), 1);
            assert_eq!(sent[0].input_type, INPUT_MOUSE);
            assert_eq!(sent[0].mi.flags, MOUSEEVENTF_RIGHTDOWN);
        }
    }
}

#[cfg(target_os = "windows")]
pub use imp::WinPlatform;

#[cfg(not(target_os = "windows"))]
mod stub {
    use super::*;

    /// Non-Windows host fallback: compiles, but every call is `Unsupported`.
    #[derive(Debug, Default, Clone, Copy)]
    pub struct WinPlatform;

    impl WinPlatform {
        /// Creates the fallback backend.
        pub fn new() -> Self {
            Self
        }
    }

    impl Platform for WinPlatform {
        fn find_wow(&self, _process_name: &str) -> PlatformResult<WindowId> {
            Err(PlatformError::Unsupported("Win32 (non-Windows build)"))
        }

        fn capture(&self, _window: WindowId, _region: Rect) -> PlatformResult<Frame> {
            Err(PlatformError::Unsupported("Win32 (non-Windows build)"))
        }

        fn post_key(&self, _window: WindowId, _key: &Key, _hold_ms: u32) -> PlatformResult<()> {
            Err(PlatformError::Unsupported("Win32 (non-Windows build)"))
        }

        fn post_mouse(&self, _window: WindowId, _event: &MouseEvent) -> PlatformResult<()> {
            Err(PlatformError::Unsupported("Win32 (non-Windows build)"))
        }

        fn is_foreground(&self, _window: WindowId) -> PlatformResult<bool> {
            Err(PlatformError::Unsupported("Win32 (non-Windows build)"))
        }
    }
}

#[cfg(not(target_os = "windows"))]
pub use stub::WinPlatform;
