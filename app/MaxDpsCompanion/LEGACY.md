# LEGACY — C# WinForms client

`app/MaxDpsCompanion/` (the `MaxDpsCompanion.exe` WinForms shell) is **FROZEN**.

- **No future rollouts, features or refactors.** Only critical security fixes,
  and only if the user explicitly asks.
- **All new work targets `companion-rs/`** — the future companion/client.
- Current target addon + distribution is **`MaxDpsBridgeExp` + `dist-exp`**;
  the legacy `MaxDpsBridge` (stable) + `dist\` pair is also frozen.
- This directory is kept only as the behavioural fidelity reference for the
  Rust port (line-by-line decision/wire/safety parity).
- Do not add files, do not port features here, do not deploy it.

See:
- `docs/plans/2026-10-10-rust-companion.md` — the Rust port plan/spec.
- `HANDOVER.md` — current status and next steps.
