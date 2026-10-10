# LEGACY — frozen components

The following are **LEGACY / FROZEN** and receive **no updates**:

- `app/MaxDpsCompanion/` — the C# WinForms client (`MaxDpsCompanion.exe`).
- `dist/` — the legacy stable distribution.
- `addon/MaxDpsBridge/` — the legacy *stable* bridge.

**Future rollouts target only:**

- `companion-rs/` — the Rust companion (the future).
- `addon/MaxDpsBridgeExp/` + `dist-exp/` — the current addon + distribution.

The old client receives no updates except critical security fixes, and only if
explicitly requested. New work must not target the legacy paths above.

See `app/MaxDpsCompanion/LEGACY.md` and
`docs/plans/2026-10-10-rust-companion.md`.
