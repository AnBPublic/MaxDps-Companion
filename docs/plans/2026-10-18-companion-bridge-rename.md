# 4.3.0 "Namesake" — MaxDps Companion Bridge rename (spec + implementation plan)

> **For agentic workers:** REQUIRED SUB-SKILL: use `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retire the "experimental" split: the in-game-config bridge and companion shell become the
norm under the names **MaxDps Companion** / **MaxDps Companion Bridge**, with the frozen 3.7.x
stable bridge and legacy companion archived under `legacy/` names.

**Architecture:** Mechanical rename across repo layout, addon identity (folder, TOC, SavedVariables,
command, namespace `MDBX` → `MDCB`), in-game key bindings, companion mode defaults, scripts, CI and
living docs. No wire, policy or gameplay behavior changes.

**Tech Stack:** Lua addon (WoW 12.1, interface 120100), C# / .NET 8 WinUI 3 companion, PowerShell 7
tooling, GitHub Actions.

**Spec:** this document, §Design (approved by the user 2026-10-18).

## Global constraints

- **No wire change.** PROTOCOL stays v5 + Ext2/Ext3/Ext4, 51 cells, every cell byte-identical.
- `vendor/` is read-only. `addon/legacy/MaxDpsBridge/` is **frozen history** — never edited.
- One exe rule: only `MaxDpsCompanion.exe` at the top of `dist/`.
- `dist/settings.ini` is per-machine; publish must never delete or rewrite it.
- Never reuse a release codename; 4.3.0 is **"Namesake"**.
- Historical records (`docs/plans/*`, `docs/archive/*`, old `VERSION.txt` entries,
  `HANDOVER-history.md`) are NOT rewritten — the sweep only covers living files.

---

## Design (approved)

Decisions (user-approved, a–d):

- **(a)** Internal Lua namespace `MDBX` → `MDCB` everywhere (Lua, `Bindings.xml`, harness, fixtures).
- **(b)** Version **4.3.0 "Namesake"**; `-exp` suffix dropped from the addon version string.
- **(c)** `InGameConfigMode` **defaults to 1**; `--exp` stays accepted (legacy alias, still forces
  on — harmless with the new default); `InGameConfigMode=0` is the classic-shell opt-out.
- **(d)** Deep C# internals (`ExpTheme`, `BuildExpContent`, …) are a follow-up; **user-visible
  strings change now** ("exp mode: …" → "in-game-config shell: …").

Rename map:

| From | To |
| --- | --- |
| `addon/MaxDpsBridgeExp/` | `addon/MaxDpsCompanionBridge/` |
| `MaxDpsBridgeExp.toc` ("MaxDps Bridge Exp") | `MaxDpsCompanionBridge.toc` ("MaxDps Companion Bridge") |
| `addon/MaxDpsBridgeExp/Exp/` + `Exp.xml` | `InGame/` + `InGame.xml` |
| `addon/MaxDpsBridge/` (frozen) | `addon/legacy/MaxDpsBridge/` (frozen) |
| `dist-exp` convention | `dist` (`dist-legacy/` on machines keeps the old output) |
| `tools/publish-exp.ps1`, `tools/install-addon-exp.ps1` | `tools/publish.ps1`, `tools/install-addon.ps1` |
| DB `MaxDpsBridgeExpDB` / `MaxDpsBridgeExpCharDB` | `MaxDpsCompanionBridgeDB` / `MaxDpsCompanionBridgeCharDB` |
| Slash `/mdbx` | `/mcb` primary + `/mdb`, `/mdbx` aliases |
| Bindings header "MaxDps Bridge Exp", `MDBX_TOGGLE_*` | "MaxDps Companion Bridge", `MDCB_TOGGLE_*` |
| Version `4.2.0-exp` | `4.3.0` (+ `X-Codename: Namesake`) |

Machine migration (post-merge, this machine):

1. Primary checkout: `dist\` (pinned legacy exe/pdb/ini) → `dist-legacy\`; `dist-exp\` content
   (settings.ini included) → `dist\`.
2. WoW: `AddOns\MaxDpsBridgeExp` → `AddOns-Disabled\`; install `AddOns\MaxDpsCompanionBridge`.
3. **SavedVariables:** WoW loads SV per addon folder name, so the old file is orphaned after the
   rename. Copy `WTF\...\SavedVariables\MaxDpsBridgeExp.lua` →
   `MaxDpsCompanionBridge.lua` (account + per-character files), rewriting the global tokens
   `MaxDpsBridgeExpDB`→`MaxDpsCompanionBridgeDB` and `MaxDpsBridgeExpCharDB`→
   `MaxDpsCompanionBridgeCharDB`, with `.bak` backups.
4. **Key bindings:** rewrite `WTF\...\bindings-cache.wtf` entries `MDBX_TOGGLE_*`→
   `MDCB_TOGGLE_*` (and `MDBX`→`MDCB` where present), with `.bak`, so assigned toggle keys survive.
5. Shortcuts: Taskbar pin + Start Menu shortcut retargeted to `dist\MaxDpsCompanion.exe`,
   relabelled "MaxDps Companion" (pin updated in place; if Windows drops the pin, re-pin manually).

Risks: (1) missed `MaxDpsBridgeExp` token → swept by rg in Task 6; (2) SV/keybind migration only
verifiable live → OWED list; (3) `MDCB` collisions — none, token is unique; (4) CI audit exit code
changes 2 → 0 → CI step updated in Task 4.

---

## File map (touched units)

| Unit | Files |
| --- | --- |
| Addon layout | `addon/MaxDpsCompanionBridge/**` (renamed), `addon/legacy/MaxDpsBridge/**`, `tests/secret_harness.lua`, `tests/MaxDpsCompanion.Tests/fixtures/Catalog.lua` |
| Addon identity | `MaxDpsCompanionBridge.toc`, `Bridge.lua`, `Bindings.xml`, `InGame/*.lua` (incl. `Settings.lua` category, `Overlay.lua` frame/icon paths), `VERSION.txt`, `HotbarWatch.lua` |
| Companion | `app/MaxDpsCompanion/Program.cs`, `app/MaxDpsCompanion.Core/AppSettings.cs`, `Ui/Shell/ShellWindow.Cli.cs`, `tests/.../ExpModeTests.cs`, `tools/ui_smoke.ps1`, `build-mac.sh` |
| Publish/install | `tools/publish.ps1`, `tools/install-addon.ps1`, `.gitignore` |
| CI/audit | `.github/workflows/ci.yml`, `tools/ability_audit.ps1`, `tools/fetch_assets.ps1` |
| Identity/version | `app/MaxDpsCompanion/MaxDpsCompanion.csproj`, `app/Version.props`, `app/MaxDpsCompanion/Native.cs`, `tests/.../ReleaseIdentityTests.cs`, `VERSION.txt`, `README.md` |
| Docs | `AGENTS.md`, `LEGACY.md`, `ARCHITECTURE.md`, `HANDOVER.md`, `docs/KNOWLEDGE.md`, `docs/TESTING.md`, `docs/PROTOCOL.md`, `docs/research/CC_DR_12.1.5.md` |

---

## Tasks

### Task 1: Repo layout — rename the addon, archive the frozen bridge

**Files:** `addon/MaxDpsBridgeExp/**` → `addon/MaxDpsCompanionBridge/**`;
`addon/MaxDpsBridge/**` → `addon/legacy/MaxDpsBridge/**`; references in
`tests/secret_harness.lua`, `tools/fetch_assets.ps1`, `tools/ability_audit.ps1`,
`.github/workflows/ci.yml`.

- [ ] Branch: `git checkout -b release/v4.3.0-namesake origin/master`.
- [ ] `git mv addon/MaxDpsBridgeExp addon/MaxDpsCompanionBridge`;
      `git mv addon/MaxDpsCompanionBridge/MaxDpsBridgeExp.toc addon/MaxDpsCompanionBridge/MaxDpsCompanionBridge.toc`.
- [ ] `git mv addon/MaxDpsCompanionBridge/Exp addon/MaxDpsCompanionBridge/InGame`;
      `git mv addon/MaxDpsCompanionBridge/Exp.xml addon/MaxDpsCompanionBridge/InGame.xml`;
      update the `file="Exp/..."` references inside `InGame.xml` (`rg -n 'Exp/' addon/MaxDpsCompanionBridge`).
- [ ] `git mv addon/MaxDpsBridge addon/legacy/MaxDpsBridge` (create `addon/legacy/`).
- [ ] Replace path tokens: `MaxDpsBridgeExp` → `MaxDpsCompanionBridge` in
      `tests/secret_harness.lua`, `tools/fetch_assets.ps1`, `.github/workflows/ci.yml`,
      `tools/ability_audit.ps1` (audit's `$addonCatalog` becomes
      `addon/MaxDpsCompanionBridge/Catalog.lua`), and `Exp/`-path refs in the harness.
- [ ] Verify: `luac -p addon/MaxDpsCompanionBridge/*.lua` (9 files OK);
      `lua tests/secret_harness.lua` (409 pass); `dotnet test` fixture tests still green.
- [ ] Commit: `rename(addon): MaxDpsBridgeExp -> MaxDpsCompanionBridge, freeze legacy under addon/legacy`.

### Task 2: Addon identity — TOC, DB globals, namespace, bindings, command

**Files:** `addon/MaxDpsCompanionBridge/*` (toc, Bridge.lua, Bindings.xml, InGame/*.lua),
`tests/secret_harness.lua`, `tests/MaxDpsCompanion.Tests/fixtures/Catalog.lua` (if transformed),
`tests/MaxDpsCompanion.Tests/KnowledgeExtrasTests.cs`.

- [ ] TOC: `Title: MaxDps Companion Bridge`; `## Version: 4.3.0`; `## X-Codename: Namesake`;
      `## SavedVariables: MaxDpsCompanionBridgeDB`; `## SavedVariablesPerCharacter: MaxDpsCompanionBridgeCharDB`;
      keep `InGame.xml` first line.
- [ ] Global token pass (longest first):
      `MaxDpsBridgeExpCharDB`→`MaxDpsCompanionBridgeCharDB`, `MaxDpsBridgeExpDB`→`MaxDpsCompanionBridgeDB`,
      `MaxDpsBridgeExp`→`MaxDpsCompanionBridge`, `MDBX`→`MDCB`, `mdbx`→`mcb` (Lua + harness + Bindings.xml).
      `rg -n 'MDBX|mdbx|MaxDpsBridgeExp' addon/MaxDpsCompanionBridge tests/secret_harness.lua` must be empty.
- [ ] Strings: `BINDING_HEADER_MDCB = "MaxDps Companion Bridge"`; header comment names.
- [ ] Overlay/settings branding: `FRAME_NAME = "MaxDpsCompanionBridgeOverlay"`;
      `ICON_PATH = "Interface\\AddOns\\MaxDpsCompanionBridge\\InGame\\Assets\\"` (**functional** — must
      match the new folder or overlay icons break); `CATEGORY_NAME = "MaxDps Companion Bridge"`
      (Game Options > AddOns page); chat prefix becomes `MDCB.PREFIX = "MDCB"`; the inert message keeps
      the "stable MaxDpsBridge is loaded" wording (it refers to the archived addon).
- [ ] Command: keep `SLASH_MDCB1 = "/mcb"`, add aliases `SLASH_MDCB2 = "/mdb"`, `SLASH_MDCB3 = "/mdbx"`.
- [ ] Stable-detection: the inert check resolves the frozen addon name `MaxDpsBridge` — keep as-is
      (still correct: the archived legacy addon is named `MaxDpsBridge` if ever loaded again).
- [ ] Harness: load paths under `addon/MaxDpsCompanionBridge`; namespace `MDCB`; check labels.
- [ ] Verify: `lua tests/secret_harness.lua`; `dotnet test -c Release` (fixtures/generator tests).
- [ ] Commit: `addon: MaxDps Companion Bridge identity (DB, namespace MDCB, /mcb + aliases, bindings)`.

### Task 3: Companion — default shell, `--exp` alias, dist paths, script renames

**Files:** `app/MaxDpsCompanion.Core/AppSettings.cs`, `app/MaxDpsCompanion/Program.cs`,
`app/MaxDpsCompanion/Ui/Shell/ShellWindow.Cli.cs`, `app/MaxDpsCompanion/Ui/Shell/UiCliRunner.cs`,
`tests/MaxDpsCompanion.Tests/ExpModeTests.cs` (rename → `InGameConfigModeTests.cs`),
`tools/ui_smoke.ps1`, `tools/publish.ps1` (renamed), `tools/install-addon.ps1` (renamed),
`build-mac.sh`, `.gitignore`.

- [ ] `AppSettings.InGameConfigMode` default `true`; update doc comments (`--exp` = legacy alias,
      `InGameConfigMode=0` = classic shell).
- [ ] `Program.cs`: comment updates; `ApplyExpFlag` keeps force-on semantics (now a no-op in
      practice); settings.ini template comment updated.
- [ ] CLI string: `"exp mode: …"` → `"in-game-config shell: …"` (`UiCliRunner.cs`).
- [ ] Tests: rename `ExpModeTests.cs`; assert new default (true), `--exp` still forces true with
      `InGameConfigMode=0` in INI, and `InGameConfigMode=0` opts into the classic shell.
- [ ] `git mv tools/publish-exp.ps1 tools/publish.ps1`, `git mv tools/install-addon-exp.ps1 tools/install-addon.ps1`;
      inside: `dist-exp` → `dist`, addon source/target `MaxDpsBridgeExp` → `MaxDpsCompanionBridge`,
      message prefixes `publish-exp:` → `publish:`; keep the settings.ini hash guard.
- [ ] `.gitignore`: drop `dist-exp/*` + keep `dist/*` (and `!dist/.gitkeep`); `build-mac.sh` reads
      `dist/settings.ini`; `tools/ui_smoke.ps1` loses `-Exp`/`--exp` (default shell now).
- [ ] Verify: `dotnet build -c Release` 0/0; `dotnet test -c Release`; `MaxDpsCompanion.exe --ui-smoke-test`
      PASS with no `--exp`.
- [ ] Commit: `companion: in-game-config shell is the default; dist/publish/install scripts de-suffixed`.

### Task 4: CI, audit, assets

**Files:** `.github/workflows/ci.yml`, `tools/ability_audit.ps1`, `tools/fetch_assets.ps1`.

- [ ] CI: luac path `addon/MaxDpsCompanionBridge`; audit step now expects exit **0** (drop the
      accepted-exit-2 wrapper); catalog note updated (new addon matches; legacy informational).
- [ ] Audit: compare generated vs `addon/MaxDpsCompanionBridge/Catalog.lua`; print an informational
      "legacy snapshot: addon/legacy/MaxDpsBridge (frozen)" line; never fail on legacy drift.
- [ ] `fetch_assets.ps1` output path + messages → `addon/MaxDpsCompanionBridge/InGame/Assets`.
- [ ] Verify: `pwsh tools/ability_audit.ps1` exits 0, violations 0.
- [ ] Commit: `ci/tools: target addon/MaxDpsCompanionBridge; audit exit 0 with informational legacy snapshot`.

### Task 5: Release identity — 4.3.0 Namesake

**Files:** `app/MaxDpsCompanion/MaxDpsCompanion.csproj`, `app/Version.props`,
`app/MaxDpsCompanion/Native.cs`, `tests/MaxDpsCompanion.Tests/ReleaseIdentityTests.cs`,
`addon/MaxDpsCompanionBridge/VERSION.txt`, repo `VERSION.txt`, `README.md`.

- [ ] `<Version>4.3.0</Version>`, `<Codename>Namesake</Codename>` in csproj + `Version.props`; Native
      fallback strings updated.
- [ ] `ReleaseIdentityTests`: expect `4.3.0`, `v4.3.0 Namesake`, title
      `MaxDPS Companion v4.3.0 Namesake` (title casing stays as-is until the branding follow-up).
- [ ] Addon runtime defaults (`InGame/Core.lua`): `MDCB.VERSION` default `"3.7.1-exp"` → `"4.3.0"`,
      `MDCB.CODENAME` `"Nightblade"` → `"Namesake"`, `MDCB.ADDON` fallback → `MaxDpsCompanionBridge`.
- [ ] Addon `VERSION.txt`: new top entry `4.3.0` / `date: 2026-10-18 "Namesake"` / rename summary;
      old entries stay. Repo `VERSION.txt` header 4.3.0.
- [ ] Verify: build; tests; `--ui-smoke-test` shows the new title; `pwsh tools/publish.ps1` (later, machine).
- [ ] Commit: `release: 4.3.0 Namesake identity`.

### Task 6: Docs sweep (living files only)

**Files:** `AGENTS.md`, `LEGACY.md`, `ARCHITECTURE.md`, `HANDOVER.md`, `README.md`,
`docs/KNOWLEDGE.md`, `docs/TESTING.md`, `docs/PROTOCOL.md`, `docs/research/CC_DR_12.1.5.md`.

- [ ] `AGENTS.md`: hard rules flip — legacy = `addon/legacy/MaxDpsBridge` + `dist-legacy`; working
      target = `addon/MaxDpsCompanionBridge` + `dist`; architect-trigger path `addon/MaxDpsCompanionBridge/*.lua`;
      offline bar uses `tools/publish.ps1` / `tools/install-addon.ps1`.
- [ ] `LEGACY.md`: point at `addon/legacy/MaxDpsBridge`; note the archive rule.
- [ ] Sweep: `rg -n 'MaxDpsBridgeExp|dist-exp|MDBX|/mdbx|MaxDpsBridgeExpDB'` over living files must
      return only history sections that are explicitly marked as such (fix anything else).
- [ ] `docs/PROTOCOL.md`: slot-6 note keeps its meaning; update addon name references.
- [ ] Commit: `docs: 4.3.0 Namesake living-doc sweep (legacy archive pointers, no-exp naming)`.

### Task 7: Verification bar (offline)

- [ ] `dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release` — 0 warnings / 0 errors.
- [ ] `dotnet test -c Release` from `tests/MaxDpsCompanion.Tests`.
- [ ] `lua tests/secret_harness.lua` — PASS set, 0 failed.
- [ ] `luac -p addon/MaxDpsCompanionBridge/*.lua` — 9 files OK.
- [ ] `pwsh tools/ability_audit.ps1` — exit 0, violations 0.
- [ ] `MaxDpsCompanion.exe --ui-smoke-test` — PASS, title `MaxDPS Companion v4.3.0 Namesake`.
- [ ] Exp catalog copy byte-matches the regenerated generator output (audit does this).
- [ ] PR to master; CI green.

### Task 8: Machine rollout (after merge)

- [ ] Primary checkout fast-forward to the merge commit.
- [ ] `dist\` → `dist-legacy\`; `dist-exp\` → `dist\` (settings.ini travels with it).
- [ ] `pwsh tools/publish.ps1` → writes `dist\` (settings.ini hash-guarded).
- [ ] `AddOns\MaxDpsBridgeExp` → `AddOns-Disabled\MaxDpsBridgeExp`; `pwsh tools/install-addon.ps1`.
- [ ] SavedVariables migration (account + per-character), backups `*.pre-4.3.0.bak`:
      rename file to `MaxDpsCompanionBridge.lua`; rewrite `MaxDpsBridgeExpDB` →
      `MaxDpsCompanionBridgeDB`, `MaxDpsBridgeExpCharDB` → `MaxDpsCompanionBridgeCharDB`.
- [ ] Keybinds migration: `bindings-cache.wtf` (`MDBX_TOGGLE_` → `MDCB_TOGGLE_`, `BINDING_` refs if
      present) with `.bak`.
- [ ] Shortcuts: retarget Taskbar pin + Start Menu lnk to `dist\MaxDpsCompanion.exe`; relabel
      "MaxDps Companion".
- [ ] Live OWED (retail): addon lists as *MaxDps Companion Bridge*; `/mcb help` works; `/mdbx`
      alias works; overlay + toggles intact (SV migration); toggle keybinds still act (cache
      migration); companion title 4.3.0; CC guard checks from TESTING §3o unchanged.

---

## Acceptance criteria

- No living file mentions `MaxDpsBridgeExp`, `dist-exp`, `MDBX`, `MaxDpsBridgeExpDB` (history
  sections excepted); `addon/legacy/MaxDpsBridge` is byte-identical to the pre-rename stable
  addon (verify with `git diff 84480d5 -- addon/legacy` after the move = pure rename).
- Overlay and Settings surface the new branding: Settings page category "MaxDps Companion Bridge",
  overlay icons resolve from `Interface\AddOns\MaxDpsCompanionBridge\InGame\Assets\` (no reference
  to the old folder remains in any Lua), chat prefix prints `MDCB`.
- The addon loads as *MaxDps Companion Bridge*; `/mcb`, `/mdb`, `/mdbx` all open the help/status;
  in-game Key Bindings shows the new header and toggle actions.
- Companion defaults to the in-game-config shell; `--exp` and `InGameConfigMode=0` behave as
  designed; title shows 4.3.0 Namesake.
- Offline bar (Task 7) fully green; wire cells unchanged (no change under `PixelProtocol`/`Reader`
  encode paths — diff review).

## Verification commands (copy-paste)

```powershell
dotnet build app/MaxDpsCompanion/MaxDpsCompanion.csproj -c Release
Set-Location tests/MaxDpsCompanion.Tests; dotnet test -c Release; Set-Location ../..
lua tests/secret_harness.lua
luac -p addon/MaxDpsCompanionBridge/*.lua
pwsh tools/ability_audit.ps1; echo "audit exit=$LASTEXITCODE"
app/MaxDpsCompanion/bin/Release/**/MaxDpsCompanion.exe --ui-smoke-test
```

**Static ≠ automated ≠ live:** the in-game items in Task 8 stay OWED until run in retail.
