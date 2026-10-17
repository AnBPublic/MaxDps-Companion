# Rust Companion (Windows 11 first) — identical functionality, pro-panel UI

Status: PLAN / spec only. No implementation code in this document.
Date: 2026-10-10
Targets: `MaxDpsBridgeExp` + `dist-exp` only (NOT stable/legacy `MaxDpsBridge` + `dist\`).
New directory: `companion-rs/` (side-by-side with `app/` until the parity gate).

## 1. Goal

Ship a Rust port of MaxDps-Companion on Windows 11 with **functionality identical**
to the C# WinForms companion. Behaviour parity is the acceptance bar; the UI is
re-skinned as a dense pro-panel tool-frame layout (tool rail, center canvas,
right panels) but every decision path, wire decode and safety rule must match
byte-for-byte behaviour.

Fidelity anchors (C# reference, line numbers approximate to current tree):

- Entry / CLI: `app/MaxDpsCompanion/Program.cs:18`
- Shell: `app/MaxDpsCompanion/MainForm.cs:5`
- Console home: `app/MaxDpsCompanion/Ui/ConsoleHome.cs:351`
- Class browser: `app/MaxDpsCompanion/Ui/ClassBrowserView.cs:41`
- Capture: `app/MaxDpsCompanion/ScreenSampler.cs:18` (BitBlt DIB)
- Engine tick: `app/MaxDpsCompanion/RotationEngine.cs:29`
- Window finding: `app/MaxDpsCompanion/WowWindow.cs:6`
- Scheduler: `app/MaxDpsCompanion/Scheduler/ActionScheduler.cs:50`
- Legacy decision path: `app/MaxDpsCompanion/Decision/DecisionEngine.cs:18`
- Input: `app/MaxDpsCompanion/KeySender.cs:14` — **hybrid**: keyboard is posted
  per-window as `WM_KEYDOWN`/`WM_KEYUP` (plus `WM_SYSKEY*` for Alt) so it never
  leaks into the focused app; mouse buttons + wheel have no per-window message
  and go through `SendInput` to the focused window, so the engine's foreground
  gate stays mandatory for those (`KeySender.cs:8-12,42,53,125,127`).
  - Parity note (updated 2026-10-10): Rust `platform-win` now mirrors the hybrid:
    keyboard is **PostMessage-only** (`Platform::post_key`, never `SendInput`)
    and the mouse/wheel `SendInput` path is **implemented**
    (`Platform::post_mouse` + `Platform::is_foreground`), with the mandatory
    foreground gate inside the backend (returns `PlatformError::NotForeground`
    and sends nothing when the target is not in front). Offline unit tests cover
    the gate + input encoder; **live retail validation stays OWED** (§6).
- Wire decode: `app/MaxDpsCompanion/PixelProtocol.cs:152` (`BridgeFrame` at `:64`)
- Settings: `app/MaxDpsCompanion/AppSettings.cs:47` (`settings.ini`)

Scope decisions owed to the user (see §8): directory name, replacement vs
side-by-side, and whether the one-exe rule (`MaxDpsCompanion.exe` only) extends to
the Rust build or a second binary is permitted during the port.

## 2. Interaction-design reference (researched)

- **Engine-first**: a pure-data document + command engine with a thin `egui`
  front-end; UI holds almost no logic.
- **One command registry**: 500+ commands; UI = CLI = JSON = `--control=MCP`
  (Model Context Protocol control surface).
- **Pro-panel layout**: top menus, left tool rail, center canvas, right panels
  (Histogram / Layers / etc.), bottom filmstrip, command palette (`Cmd/Ctrl+K`).
- **Shortcuts** mirror conventional desktop editing apps.
- **Themes**: named **dark pro-panel**, **airy studio**, **classic** — no hex
  values published.
- **Fonts**: system font + CJK fallback chain.
- **License posture**: permissive/reusable architecture; attribution file required
  for bundled assets; trademark material excluded.
- **Clean-room**: adopt the architecture/interaction model, not code or brand assets.

## 3. Crate layout (`companion-rs/`, Cargo workspace)

- `mdc-protocol` — decode v5/v4/v1; Ext2/Ext3; 35/40/43 cells; checksums 10/34/39/42;
  slots 1-8; golden protocol vectors for parity tests.
- `mdc-platform` — trait `find_wow` / `capture` / `post_key` / `post_mouse` /
  `is_foreground` (mouse types: `MouseButton`, `MouseEvent`).
  - `platform-win` — BitBlt DIB capture; keyboard PostMessage-only; mouse/wheel
    `SendInput` + mandatory foreground gate **implemented** (C# parity; see
    `KeySender.cs:8-12,105-165`). Live retail validation **OWED**.
  - `platform-mac` — stub returning `Unsupported` (ScreenCaptureKit / CGEvent later).
- `mdc-engine` — deterministic core with an **injected `Clock`**; candidate tracker,
  scheduler, decision, rotation, knowledge (mirrors `Scheduler/**`, `Decision/**`,
  `Knowledge/**`).
- `mdc-settings` — `settings.ini` compatibility; **preserve unknown keys** on rewrite.
- `mdc-telemetry` — JSONL, local-only, schema match to the C# telemetry events.
- `mdc-commands` — command registry: `register` + `invoke` (JSON); single source for
  UI, CLI and control surface.
- `mdc-cli` — `doctor` / `audit` / `replay` / `invoke` subcommands.
- `mdc-app` — thin `eframe`/`egui` shell; `egui_dock`; left rail; status bar.

## 4. Design mapping (C# → Rust UI)

- Console Home → center "canvas" equivalent.
- Class Browser → left library + center grid.
- Doctor / Telemetry / Settings → right panels.
- Top menus + `Ctrl+K` command palette → map every action to a **command ID**.
- Theme: **dark pro-panel approximation** (egui dark base + a single accent). No
  official hex values exist; document our approximation as non-normative.

## 5. Assets & attribution

- Copy `icon-256.png` / `companion.ico` into the Rust app resources; **do not modify
  the originals**.
- Font: Inter if bundled, else system font fallback chain (mirrors `DesignTokens`).
- Add `assets/ATTRIBUTION.md` + `NOTICE` covering the interaction-design derivation
  and any bundled assets.
- Interaction design informed by publicly available open-source references; no
  branding, names, logos, or assets carried over.

## 6. Build / verify

Rust bar:

- `cargo build --release --workspace` — **0 warnings**.
- `cargo clippy -- -D warnings`.
- `cargo test --workspace` + the **golden protocol test** (vectors vs C# decode).

Existing C#/Lua bar must still pass (shared repo, Exp client unchanged):

- `dotnet build -c Release` (0 warnings / 0 errors) and `dotnet test -c Release`
  from `tests\MaxDpsCompanion.Tests`.
- `lua tests/secret_harness.lua`; `luac -p addon/MaxDpsBridgeExp/*.lua`;
  `pwsh tools/ability_audit.ps1`.

Live retail (OWED, not closed by any build/test): real BitBlt capture of the Exp
strip, PostMessage key delivery into a live client, background-safe mouse/wheel
`SendInput` with the foreground gate (refusal when WoW is not focused), and a
30-minute stability run. DPI-aware absolute-move placement rides on this check.

## 7. Risks

- DPI scaling / window border / DIB stride shifts breaking pixel sampling.
- Scheduler tick jitter vs the C# fixed-cadence assumptions (`MainReprobeMs`,
  min-interval, GCD confirm).
- PostMessage focus semantics and background-window key delivery differences.
- **Dual-exe policy owed** — running `companion-rs` alongside `MaxDpsCompanion.exe`
  may violate the current one-exe rule until a decision is recorded.

## 8. Open questions for the user

1. Is `companion-rs/` an acceptable directory name?
2. Replacement (Rust becomes the shipped exe) or **side-by-side** until a parity gate?
3. `KeySender` is already **hybrid** (keyboard PostMessage + mouse/wheel
   SendInput); confirm the Rust port keeps that exact split.
