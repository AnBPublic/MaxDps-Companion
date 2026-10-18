# LEGACY - frozen components (updated for 4.3.0 "Namesake")

**Decision (2026-10-17): C# is the long-term base.** The earlier 2026-10-10
declaration that the C# client is frozen and Rust is the future is **withdrawn**.

Current (maintained):

- `app/MaxDpsCompanion/` - the WinUI 3 Windows client (4.x).
- `app/MaxDpsCompanion.Core/` - the shared portable brain (Windows consumes it
  as a project reference since 4.1.0; the Mac client uses it too).
- `app/MaxDpsCompanion.Mac/` - the macOS client.
- `addon/MaxDpsCompanionBridge/` + `dist/` - the current addon + distribution.

Frozen (no updates except critical security fixes, only if explicitly requested):

- `addon/legacy/MaxDpsBridge/` - the legacy *stable* bridge (archived at 4.3.0).
- `dist-legacy/` and `build.ps1` - the legacy 3.7.9 WinForms distribution.
- `companion-rs/` - the Rust port. Source is kept for reference; it does not
  decode Ext4 and is not kept in step with the C# client. Do not extend it
  unless the user re-opens that decision.

The wire contract (`docs/PROTOCOL.md`) is the only thing every implementation
must agree on; any change to it needs an architect pass (see `AGENTS.md`).
