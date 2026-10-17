//! macOS platform backend — **stub**.
//!
//! The plan (docs/plans/2026-10-10-rust-companion.md §3) defers the real macOS
//! backend to a later pass using **ScreenCaptureKit** for capture and
//! **CGEvent** for input. Until then every call returns
//! [`PlatformError::Unsupported`] so the engine can be wired and tested on
//! Windows without a macOS implementation.
//!
//! Input contract to honour when this is filled in: PostMessage-equivalent
//! per-window delivery only — no global event taps that leak into the focused
//! app, no injection, no memory reads.

#![forbid(unsafe_code)]

use mdc_platform::{
    Frame, Key, MouseEvent, Platform, PlatformError, PlatformResult, Rect, WindowId,
};

/// macOS [`Platform`] backend (stub).
#[derive(Debug, Default, Clone, Copy)]
pub struct MacPlatform;

impl MacPlatform {
    /// Creates the stub backend.
    pub fn new() -> Self {
        Self
    }
}

impl Platform for MacPlatform {
    fn find_wow(&self, _process_name: &str) -> PlatformResult<WindowId> {
        Err(PlatformError::Unsupported("macOS find_wow (ScreenCaptureKit/CGWindowList)"))
    }

    fn capture(&self, _window: WindowId, _region: Rect) -> PlatformResult<Frame> {
        Err(PlatformError::Unsupported("macOS capture (ScreenCaptureKit)"))
    }

    fn post_key(&self, _window: WindowId, _key: &Key, _hold_ms: u32) -> PlatformResult<()> {
        Err(PlatformError::Unsupported("macOS post_key (CGEvent)"))
    }

    fn post_mouse(&self, _window: WindowId, _event: &MouseEvent) -> PlatformResult<()> {
        Err(PlatformError::Unsupported("macOS post_mouse (CGEvent + foreground gate)"))
    }

    fn is_foreground(&self, _window: WindowId) -> PlatformResult<bool> {
        Err(PlatformError::Unsupported("macOS is_foreground (CGWindowList)"))
    }
}
