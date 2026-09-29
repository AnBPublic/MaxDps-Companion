# MaxDps-Companion v3.0.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore a fast v1.3.9-style classic UI, keep all v2.8.1 functionality, and make Solo self-sustain fire reliably for every class including when the game hides HP.

**Architecture:** Three parallel workstreams with disjoint file ownership (UI = C# shell, Bridge = Lua encoder, Intel = C# decode/policy/knowledge), sharing the frozen Ext2 protocol contract committed on `v3/base`. Merge order C → B → A.

**Tech Stack:** .NET 8 WinForms (`net8.0-windows`), xunit, Lua 5.1 + retail 12.1 APIs, PowerShell 7.

**Spec:** This file is the spec. Protocol contract: `docs/PROTOCOL.md` § "Ext2 block (v3.0.0, additive)". Baseline numbers: `docs/plans/baseline-v2.8.1.txt`.

## Global Constraints

- `vendor/**` is read-only. Never edit it, never import from it.
- The bridge never calls protected Lua. Never compare, arithmetize, stringify, or table-key any possibly-secret value; every probe in `pcall` + `Scrubbed`.
- The companion sends keys only via `PostMessage` to the WoW window. No memory read, no injection.
- Build bar: `dotnet build -c Release` 0 errors. Pre-existing baseline has 2 warnings (CS0108 `Ui/Layout.cs:273-274`); introduce 0 new warnings.
- Every existing test stays green. Legacy replay fixtures replay with **0 mismatches without regeneration**.
- `--bench-scheduler` pin `sends=1620 sha256=b71a999d5e46570e`: if it changes, the report lists which scripted ticks changed and why. Never change the pin silently.
- `tools/ability_audit.ps1` exits 0 (Violations/Warnings/Missing/Stale all 0).
- `addon/MaxDpsBridge/Catalog.lua` is generated via `--gen-catalog`. Never hand-edit it.
- One exe. Tracked `settings.ini` keeps defaults; per-machine edits live in `dist\` only.
- `[Solo] Enabled` default stays 0.
- No pushes. Commit on your own branch only (`v3/ui`, `v3/bridge`, `v3/intel`).
- Stop rule: same error twice, or 3 iterations without progress → stop and return a handover packet (goal, what you tried, exact errors with file:line, ruled-out ideas, files touched, suggested next step).
- No live-validation claims: static ≠ automated test ≠ live. Report live tests as OWED.

---

## Phase 0 (done, on `v3/base` commit e368f91)

- 0.1 v2.8.1 baseline committed (`v2.8.1-baseline`: f9bfbc5 vendor, 1c40c48 tree).
- 0.2 Baseline numbers in `docs/plans/baseline-v2.8.1.txt` (446/446, 94/94, bench pin, 3 replays 0 mismatches).
- 0.3 Contract commit: PROTOCOL.md "Ext2 block" verbatim + `AppSettings.WindowLayout` (`[Window] Layout=`) + `AppSettings.HpCurve` (`[Intelligence] HpCurve=1`, default true) + round-trip tests (450/450 on v3/base).
- 0.4 Worktrees `../MaxDps-Companion-wt/{ui,bridge,intel}` on branches `v3/{ui,bridge,intel}`.

## Ext2 contract (normative, mirrors docs/PROTOCOL.md)

Frame width becomes 40 cells. Cells 0–34 are unchanged (v5, version nibble stays 5).
Cell 33 B: bit0 class/spec valid, bit1 buff block valid, bit2 EXT2 PRESENT, bit3 HP CURVE ACTIVE.
Cell 28 B: bits0-1 = SelfHeal2 range tri-state (0 unknown/1 in/2 out); was reserved 0.
Cell 35: HP curve via SetVertexColor(color:GetRGBA()), color = UnitHealthPercent("player", false, MDB.HpCurve), linear 0.0 -> (0,1,0,1), 1.0 -> (1,0,0,1). No checksum covers it. bit3=0 → black.
Cell 36: SelfHeal2 vk hi | vk lo | flags (same as slots 1-8). Cells 37-38: SelfHeal2 spell id (6 nibbles). Cell 39: R=0, G = sum R/G/B nibbles of 36-38 mod 16, B = commit (=heartbeat).
Decoder: bit2=0 → ignore 35-39 (old addon). Cell-39 checksum failure drops ONLY SelfHeal2. HP curve valid iff bit2 & bit3 & 14 <= nR+nG <= 16. band = nR (0..15); HpPct = round(nR*100/15); HpPctUpper = min(100, round((nR+0.5)*100/15)).
Selection: SelfHeal = first ready+bound entry; SelfHeal2 = next distinct ready+bound entry.
HP precedence (companion): plain cell 27 > curve > unknown.

---

### Workstream A: Classic UI (worktree `../MaxDps-Companion-wt/ui`, branch `v3/ui`)

**Files:**
- Owns: `app/MaxDpsCompanion/MainForm.cs`, `app/MaxDpsCompanion/UiControls.cs`, `app/MaxDpsCompanion/Ui/**`, `app/MaxDpsCompanion/ClassSkillsView.cs`, `app/MaxDpsCompanion/Program.cs` (UI flags only: `--ui-smoke-test`, `--ui-snapshot-*`, `--bench-ui`), `app/MaxDpsCompanion/MaxDpsCompanion.csproj` (font resources), `tests/MaxDpsCompanion.Tests/UiShellTests.cs`, new `tests/MaxDpsCompanion.Tests/ClassicUiTests.cs`, `docs/UI.md`.
- Must NOT touch: `AppSettings.cs`, `settings.ini`, protocol/engine/knowledge/scheduler/telemetry/decision code, `addon/**`.
- Reference: `git show cfc3776:app/MaxDpsCompanion/MainForm.cs` and `git show cfc3776:app/MaxDpsCompanion/UiControls.cs`. Restore `LinkLamp`, `StripView`, `ClassBadge`, `RoundedCard`, `GroupHeader`, `SettingRow`, `ChamferButton`, `ConsolePalette` from there.

**Interfaces:**
- Consumes: `AppSettings.WindowLayout` (string; if != "classic3", ignore saved Width/Height then save Layout=classic3), engine status snapshots (`EngineStatus`, `LastAction`/`CurrentPlanHead` reason text), existing `AbilityPolicy` toggle storage, `BattleNetLauncher.TryLaunch` (wrap in an injectable `Func<bool>` seam for tests).
- Produces: main window 660×920 default / 520×560 minimum; `Advanced…` popup (tabs Configuration|Diagnostics|Intelligence); `Abilities…` popup (tabs Class skills|Explorer); `--bench-ui` + bench-ui.txt.

- [ ] **Step A1: scroll regression test first (TDD, must FAIL on current tree)**
```csharp
// tests/MaxDpsCompanion.Tests/ClassicUiTests.cs — offscreen STA form like v2.8.1 mouse tests
form.ShowOffscreen(); OpenAdvancedDiagnostics(); scrollPanel.VerticalScroll.Value = 300;
for (int i = 0; i < 20; i++) form.RefreshStatus(ChangingSnapshot(i));
Assert.Equal(300, scrollPanel.VerticalScroll.Value);
```
Run: `dotnet test -c Release --filter ClassicUi_ScrollSurvivesRefresh`
Expected: FAIL (documents the NormalizeScroll defect)
- [ ] **Step A2: delete the scroll killer.** Remove `StackPage.NormalizeScroll` (`app/MaxDpsCompanion/Ui/Pages.cs:60-64`) and its call site (`MainForm.cs:1292`). Re-run A1 test → PASS. Rule, enforced for life: **no `AutoScrollPosition` writes on any timer path**.
- [ ] **Step A3: main window fixed layout (no AutoScroll, TableLayoutPanel, v1.3.9 heights)**
  - Default ClientSize 660×(content height, ≤ working area); `MinimumSize` 520×560; remembered `[Window]` size honored only when `WindowLayout == "classic3"`.
  - Top→bottom: title bar 48; hero `RoundedCard`: Status row 42 (LinkLamp + state + ClassBadge), Live row 42 (`Now: <action> — <why>`, ellipsis + tooltip), `StripView` row 40, "Spells" header + 4 two-toggle rows (Main|Offensive, Defensive|Interrupt, Self-heal|Mobility, Consumable|Trinket), "Modes" header + 2 two-toggle rows (Solo|Out of combat, Auto-target|Auto-interact; "Out of combat" = same key as v1.3.9 `_outOfCombat`); button row 1 (52): Start Primary | Stop Danger | Launch Game Ghost-brass; button row 2 (46): Recalibrate | Abilities… | Advanced… | Open Folder; one status message line.
  - Keep red/green frame ring, tray, pause hotkey, Esc.
- [ ] **Step A4: Advanced… popup (v1.3.8 style).** Dimmed scrim, centred card width `min(client-40, 620)`, height `client-60`, Back/Esc closes. Tabs Configuration|Diagnostics|Intelligence; each tab one `Panel{AutoScroll=true}` of fixed-height `RuleSection`s built lazily once, never rebuilt by timers. Must host EVERY v2.8.1 control/readout from Configuration+Diagnostics+Intelligence+Home (scheduler/intelligence toggles, targeting keys, pause hotkey, background keys, process name, protocol/bridge/slots/last key/decision/raw, telemetry record/Export/Replay, Calibrate/Reset/Find strip/Recalibrate/Tolerance, Poll/Key hold/Min gap, Cell size/Offset X/Y, audit, BNet path/Browse, 13 metric tiles + proportion bar, patch/catalog, coverage, identity). Write the old→new mapping checklist in the final report.
- [ ] **Step A5: Abilities… popup.** Full-frame overlay, tabs Class skills (existing `ClassSkillsView`) | Explorer (existing `AbilityExplorer`); inspector column only at width ≥ 900 else inline row details. Toggles keep writing via existing `AbilityPolicy`.
- [ ] **Step A6: perf rules.** No PerformLayout/Add/Remove on timer paths; text setters assign only on change; no per-character measuring (cache sizes); skip refresh while minimized/trayed; coverage report built off-UI-thread on first Intelligence open (`Task.Run` + `BeginInvoke`); add `WheelSafeNumeric : NumericUpDown` that forwards wheel to the scrollable parent unless focused; delete `AppShell`, `NavigationRail`, `PageHost`, `StackPage` pages, `AmbientBackground`, `GlassCard`, `KvRow`, `BentoSplit`, `UiFonts` + Geist resources/fonts if unused; keep `UiClickable` (v2.8.1 mouse fix).
- [ ] **Step A7: tests + benches.**
```powershell
dotnet test -c Release --filter ClassicUi
```
Required tests: scroll survives 20 refreshes; 0 Layout events across 50 value-only refreshes; default/min sizes; 7 bottom buttons exact labels + enabled-by-state; real mouse-message clicks on toggles/buttons/tabs; launcher delegate seam (no real Battle.net); every toggle writes its settings key; Esc closes popups; update `UiShellValidation`/`--ui-smoke-test` for the new tree. Add `--bench-ui` (2000× RefreshStatus → mean/p95 µs + startup-to-shown ms → bench-ui.txt): targets mean < 300 µs, p95 < 1 ms, startup < 500 ms. Snapshots `--ui-snapshot-page=main|advanced-config|advanced-diag|advanced-intel|abilities-class|abilities-explorer` at 660×920 and 520×560 (12 files).
- [ ] **Step A8: commit.** Prefix `ui:`. `git add` only owned files.
```bash
git add app/MaxDpsCompanion/MainForm.cs app/MaxDpsCompanion/UiControls.cs app/MaxDpsCompanion/Ui app/MaxDpsCompanion/ClassSkillsView.cs app/MaxDpsCompanion/Program.cs app/MaxDpsCompanion/MaxDpsCompanion.csproj tests/MaxDpsCompanion.Tests/UiShellTests.cs tests/MaxDpsCompanion.Tests/ClassicUiTests.cs docs/UI.md
git commit -m "ui: <task id> <summary>"
```

**A acceptance:** build 0 new warnings; `dotnet test` green; `--ui-smoke-test` exit 0; 12 snapshots; `--bench-ui` meets targets; control-mapping checklist complete.

---

### Workstream B: Bridge 3.0.0 (worktree `../MaxDps-Companion-wt/bridge`, branch `v3/bridge`)

**Files:**
- Owns: `addon/MaxDpsBridge/Reader.lua`, `Bridge.lua`, `Bars.lua`, `Keymap.lua`, `Options.lua`, `MaxDpsBridge.toc`, `VERSION.txt`, `tests/secret_harness.lua`.
- Must NOT touch: `addon/MaxDpsBridge/Catalog.lua` — the harness must STUB `MDB.SpellAliases` (e.g. `MDB.SpellAliases = { [202168] = {34428} }`); the real table is generated by workstream C. Must NOT touch any C# file.

**Interfaces:**
- Consumes: `MDB.SpellAliases` (generated map id → {alias ids}, may be nil → treat as {}).
- Produces: contract-conformant 40-cell strip; `/mdb heal`; `/mdb hpcurve on|off`; `/mdb status` shows `ext2=1 hpcurve=<on|off> sh2=<id|->`.

- [ ] **Step B1: spell variants.**
```lua
-- Reader.lua; every API call in pcall, results scrubbed, de-duplicated
function MDB.SpellVariants (SpellID) --> { id, override, base, aliases... }
-- GetOverrideSpell / FindBaseSpellByID / FindSpellOverrideByID + MDB.SpellAliases
```
Harness: known-override case, base-only case, alias-table case.
- [ ] **Step B2: binds by any variant.** `FindSpellOnActionBar`, `GetKeybindForSpell`, `TextureBinding`, `ResolveBinding` match any variant id; macros count when `GetActionInfo` type is `macro` and `GetMacroSpell` resolves to a variant. Harness: bar holds 34428 while list asks 202168 → bound; `/cast Victory Rush` macro → bound.
- [ ] **Step B3: extras selection.** `ExtraCandidates("selfHeal", 2)` → first two DISTINCT ready+bound entries; encode the variant actually known (`IsPlayerSpell`/`C_SpellBook.IsSpellKnown` in pcall).
- [ ] **Step B4: Ext2 encode per contract.** Strip width 40. HP-curve cell: `tex:SetColorTexture(1,1,1,1)` once, then per-frame `tex:SetVertexColor(c:GetRGBA())` inside pcall — NO reads/comparisons of `c`; missing API/setting → bit3=0 + black cell. SelfHeal2 cells 36-38 + range in cell 28 B + checksum cell 39 + presence bits 2/3. Old checksums (cells 10, 34) cover exactly the old ranges.
- [ ] **Step B5: harness.** `lua tests/secret_harness.lua`: a "secret" sentinel whose metatable ERRORS on compare/arithmetic/tostring must flow through GetRGBA→SetVertexColor untouched (assert no error + SetVertexColor got the sentinel); cell 36-38 checksum; bit2/bit3 truth table; count ≥ 94 + new checks, all passing.
- [ ] **Step B6: commands.** `/mdb heal` prints per selfHeal entry `id -> variant known=<y/n> ready=<y/n> usable=<y/n> key=<k|-> why=<reason>`; `/mdb hpcurve on|off` (SavedVariables, default on); `/mdb status` adds `ext2=1 hpcurve= sh2=`.
- [ ] **Step B7: EnsureEngine.** Wire `MDB.EnsureEngine()` into `Bridge.Update` throttled to 1/s while `MaxDps.Spells` is empty. Harness check. Bump bridge 3.0.0 (`.toc` Interface stays 120100).
- [ ] **Step B8: commit.** Prefix `bridge:`.
```bash
git add addon/MaxDpsBridge/Reader.lua addon/MaxDpsBridge/Bridge.lua addon/MaxDpsBridge/Bars.lua addon/MaxDpsBridge/Keymap.lua addon/MaxDpsBridge/Options.lua addon/MaxDpsBridge/MaxDpsBridge.toc addon/MaxDpsBridge/VERSION.txt tests/secret_harness.lua
git commit -m "bridge: <task id> <summary>"
```

**B acceptance:** `luac -p addon/MaxDpsBridge/*.lua` clean; harness all-pass (≥94 + new); report lists every new use of curve/secret values to prove secret-safety.

---

### Workstream C: Intelligence + decode (worktree `../MaxDps-Companion-wt/intel`, branch `v3/intel`)

**Files:**
- Owns: `PixelProtocol.cs`, `ScreenSampler.cs`, `BlockLocator.cs`, `ColorLearner.cs`, `RotationEngine.cs`, `Intelligence/**`, `Knowledge/**` (incl. `abilities.json` + generator), `Scheduler/**`, `Telemetry/**`, `Decision/**`, generated `addon/MaxDpsBridge/Catalog.lua` + `tests/**/fixtures/Catalog.lua`, all non-UI xunit tests, `docs/KNOWLEDGE.md`, `docs/TELEMETRY.md`.
- Must NOT touch: `MainForm.cs`, `Ui/**`, `UiControls.cs`, `ClassSkillsView.cs`, `Program.cs`, `AppSettings.cs`, `settings.ini`, `addon/**` except regenerating `Catalog.lua`.

**Interfaces:**
- Consumes: 40-cell frames; `AppSettings.HpCurve`; `MDB.SpellAliases` contract (generator emits it).
- Produces: regenerated `Catalog.lua` (+ fixture copy); `BridgeFrame.HpCurveValid/HpCurveBand/SelfHeal2`; `CombatContext.HpSource/HpPct/HpPctUpper`; telemetry `hpSrc` + `alt` fields.

- [ ] **Step C1: knowledge.** Add curated 34428 Victory Rush (SelfHeal, requiresTarget, melee; verify heal%/CD on wowhead/warcraft.wiki.gg, record source URL). Add `aliases` block (202168↔34428 + existing 19647↔119910) → generator emits `MDB.SpellAliases`. Expand `extras.selfHeal` per spec, cheap/frequent first, ceiling big-CDs last:

| Class | selfHeal list |
|---|---|
| DK | 45470, 48743 (`useBelowHpPct` 50) |
| DH Vengeance | 263648; Havoc/Devourer: none (documented) |
| Druid | Regrowth(**verify id; current 774 is Rejuvenation — fix to 8936 if confirmed**), 22842, 108238 |
| Evoker | 361469, 360995 |
| Hunter | 109304 |
| Mage | none (documented) |
| Monk | 322101, 116670 (+124682 MW) |
| Paladin | 85673, 19750, 633 (`useBelowHpPct` 20) |
| Priest | 19236, 2061 |
| Rogue | 185311 |
| Shaman | 8004 (+77472 Resto) |
| Warlock | 234153, 6789 |
| Warrior | 202168, 34428, 184364 (Fury), 190456 (Prot) |

Every entry: `healPct`, `useBelowHpPct` where apt, `source` + `sourceConfidence`; unverifiable heal → `healPct` 0 + low confidence. Run `--gen-catalog`. Tests: every spec except documented nones has ≥1 selfHeal; every extras id verified in `spell-verification.json` with matching name; `CatalogLuaSyncTests` green; `tools/ability_audit.ps1` exit 0.
- [ ] **Step C2: decode Ext2.** `ScreenSampler`/`BlockLocator`/`ColorLearner` capture 40 cells in one BitBlt. `BridgeFrame` += `HpCurveValid`, `HpCurveBand`, `SelfHeal2` (vk/id/flags/range). Cell-39 checksum failure → `SelfHeal2 = null`, frame kept. New `PixelProtocolExt2Tests`: valid; nR+nG out of range; bit2=0 with junk in 35-39; bit3=0; cell-39 failure; v5/v4/v1 decode byte-identical; 35-cell addon through 40-cell capture works.
- [ ] **Step C3: CombatContext.** `HpSource {Plain, Curve, Unknown}`, `HpPct`, `HpPctUpper`; precedence plain cell 27 > curve > unknown; `HpCurve=0` ignores curve; unknown cell-31-G urgency + valid curve → derive urgency from curve HP (≤30 Red, <50 Orange, <100 Yellow, 100 White). Tests per branch.
- [ ] **Step C4: SelfSustain provider.** Emergency/sustain gates use `HpPct`, overheal guard uses `HpPctUpper`; reasons carry source (e.g. `solo: HP ~40% (curve) below sustain 65%`). Alternate candidate: SelfHeal verdict ≠ Use + SelfHeal2 exists → evaluate alternate, Use wins the slot. Rank/one-action-per-tick/scheduler unchanged. Telemetry `hpSrc` + `alt` optional; legacy replays 0 mismatches.
- [ ] **Step C5: scenario tests + fixture.**
```powershell
dotnet test -c Release --filter "SelfSustain|Ext2|HpCurve|SoloHidden"
```
Warrior Arms hidden-HP + curve band 6 + Solo ON → Use IV + key; bar variant 34428 → Use 34428; Paladin Ret 50% → LoH held by ceiling, alternate WoG Use; Hunter 50% → Exhilaration Use via SelfHeal slot; dual-slotted same spell in one tick → single press; property test all specs: curve 40% Solo ON → first no-ceiling entry Use / 90% → Hold / Solo OFF 50% → Hold / policy OFF → never Use. New fixture `tests/MaxDpsCompanion.Tests/fixtures/solo-hidden-hp-warrior.jsonl` replays 0 mismatches via real `--replay`.
- [ ] **Step C6: commit.** Prefix `intel:`.
```bash
git add app/MaxDpsCompanion/PixelProtocol.cs app/MaxDpsCompanion/ScreenSampler.cs app/MaxDpsCompanion/BlockLocator.cs app/MaxDpsCompanion/ColorLearner.cs app/MaxDpsCompanion/RotationEngine.cs app/MaxDpsCompanion/Intelligence app/MaxDpsCompanion/Knowledge app/MaxDpsCompanion/Scheduler app/MaxDpsCompanion/Telemetry app/MaxDpsCompanion/Decision addon/MaxDpsBridge/Catalog.lua tests/MaxDpsCompanion.Tests docs/KNOWLEDGE.md docs/TELEMETRY.md
git commit -m "intel: <task id> <summary>"
```

**C acceptance:** build 0 new warnings; `dotnet test` green; audit exit 0; 3 legacy + 1 new fixtures replay 0 mismatches; bench pin unchanged or explained.

---

## Phase 2: Integration + review (router, then acceptance review)

- [ ] Merge `v3/intel` → `v3/base`, then `v3/bridge`, then `v3/ui` (Catalog.lua from C wins; B's harness runs against the real Catalog). Resolve conflicts; conflicting shared files decided by router.
- [ ] Full verify with quoted outputs: build, `dotnet test`, harness, `luac -p`, audit, 4 replays, `--bench-scheduler`, `--bench-ui`, `--ui-smoke-test`, snapshots.
- [ ] Acceptance review of the merged diff (review scope: Ext2 secret-safety, scroll/perf, contract conformance).
- [ ] Update `HANDOVER.md` (v3.0.0 status, live OWED), `ARCHITECTURE.md` (pipeline + file map), `PROTOCOL.md`, `README.md` (still describes protocol v2 — rewrite wire section). Version 3.0.0: csproj + VERSION.txt + bridge. Run `build.ps1` + `install-addon.ps1`. Commit on `v3/base` only after user OK. No push without approval.

## Phase 3: Live tests (user-owned, ~30 min) — OWED until run

- L0 (can run NOW on v2.8.1): Warrior, Solo ON, telemetry ON, take damage below 65% in combat, Export → router greps for `hpKnown:false` / `player HP unknown` to confirm/rule out H1.
- L1 (v3): /reload → `/mdb status` shows `ext2=1 hpcurve=on`; `/mdb heal` lists keys; window 660 wide, scroll sticks, Launch Game works; Warrior Solo <65% in combat → IV fires once per CD then rotation resumes; 34428-on-bar variant still fires; second class (Hunter Exhilaration or Paladin WoG); `/mdb hpcurve off` falls back; record+export+replay 0 mismatches.

## Prompt template per agent (fill in <WS>, <worktree path>, <branch>)

You are implementing Workstream <WS> of docs/plans/2026-09-29-maxdps-companion-v3.md in worktree <path> on branch <branch>. Read in order: AGENTS.md, HANDOVER.md (top section only), ARCHITECTURE.md, docs/PROTOCOL.md "Ext2 block", then your workstream section. Global Constraints are mandatory. Edit ONLY files your section owns (use `git status --porcelain` before every commit to prove it). Task-by-task: failing test first, confirm FAIL, implement, confirm PASS, commit. Never push, never touch vendor/, never hand-edit Catalog.lua (B stubs SpellAliases; only C regenerates). Final report ≤300 words + checklist: tasks with commit hashes, every acceptance command + last 15 output lines, deviations with reasons, open risks + what needs live test. Stop rule: same error twice / 3 iterations without progress → stop, return handover packet (goal, tried, exact errors file:line, ruled-out, files touched, next angle).
