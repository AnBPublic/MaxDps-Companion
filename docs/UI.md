# UI architecture — MaxDps Companion 2.8

The companion is one WinForms application (`MaxDpsCompanion.exe`, .NET 8,
x64, PerMonitorV2). The v2.8 pass ("Ethereal Glass × brass") rebuilt the shell
on a measured layout core after the v2.7 defects (empty Home cards, clipped
pills, an undocked ability list). The engine is untouched by the UI: the shell
reads engine status and writes only through the existing settings/policy
storage.

## Shell

```
MainForm (borderless; 2px state ring red stopped / green running;
          remembered size [Window] Width/Height; tray; pause hotkey)
  └─ AppShell
       ├─ ambient backdrop   cached radial glows (brass/teal) + 3% grain,
       │                     painted by the shell itself — never a child
       │                     control (a full-size child paints last in
       │                     DrawToBitmap and would cover the page tree)
       ├─ NavigationRail     hand-drawn 1.4px icons; brand mark; labelled rail
       │                     200px, icon-only compact under 880px; click,
       │                     Up/Down/Home/End, Ctrl+1..5, Esc→Home
       └─ PageHost           one visible page; short slide (suppressed while
                             the engine runs)
```

Class skills (`ClassSkillsView`) is unchanged and reachable from Configuration.

## Measured layout core (`Ui/Layout.cs`)

WinForms docking inside `FlowLayoutPanel` is unsupported and silently collapsed
the v2.7 Home rows to zero width; the shell therefore composes from explicit
measured containers:

| Container | Purpose |
| :--- | :--- |
| `VertStack` | top-down stack; children get width + measured height |
| `WrapFlow` | wrapping row flow (pills, chips, tiles, buttons) |
| `GridPanel` | equal-width columns with measured row heights |
| `BentoSplit` | asymmetric 58/42 split, single column under 980px |
| `UiMeasure` | height-by-width measurement; container-owned wrapping labels that re-measure on width change |

No screen uses Dock-inside-flow; `GlassCard` and every primitive implement
`IUiMeasured`, so page height is always derived from content (DPI-safe).

## Primitives

| Primitive | Notes |
| :--- | :--- |
| `GlassCard` | double-bezel: outer shell (blended surface + hairline ring) around a concentric inner core with a top highlight; tracked uppercase eyebrow, title, hairline rule; measured height |
| `KvRow` | owner-drawn caption + value row (no nested panels) |
| `StatusPill` / `MetricTile` / `FilterChip` | auto-sizing; `IUiTextFit` reports required text width for the smoke test |
| `FieldRowPanel` | measured caption + interactive control row |
| `ChamferButton` | pill buttons; primary carries a nested trailing-icon circle; press physics (0.98 scale, spring back) |
| `ToggleSwitch` / `SettingRow` | spring thumb; measured glass rows |
| `ToastHost` | transient non-blocking status feedback |
| `NavIcons` | hand-drawn 1.4px strokes (house/list/diamond/gear/heartbeat) — no font glyphs |

Type: bundled **Geist** (Regular/Medium/Bold, SIL OFL) loaded from embedded
resources (`Ui/UiFonts.cs`); the system variable-font chain is the fallback.
Design tokens (`Ui/DesignTokens.cs`): OLED ink `#07090B`, layered surfaces,
hairline borders, brass accent, radii 20/14/12/10, 4–40 spacing scale.

## Pages

| Page | Content |
| :--- | :--- |
| **Home** | responsive bento: status hero + live action/why (RotationEngine snapshots) + identity + intelligence health; single column under 980px |
| **Abilities** | virtualized owner-drawn rows (3279 entries never become controls), debounced search, wrapping filter/category chips, inline expansion, master-detail inspector, per-ability toggle |
| **Intelligence** | auto-sized coverage tiles, proportion bar, patch/catalog provenance; click a count to filter the explorer |
| **Configuration** | measured grouped cards (Automation/Combat/Safety/Targeting/Input/Bridge/Advanced) |
| **Diagnostics** | protocol/bridge, telemetry, calibration, strip, patch/audit, Battle.net tools |

## Honest structural smoke (`UiShellValidation`)

`--ui-smoke-test` builds the shell and all pages headlessly (STA) and checks —
without a `Visible` gate, because the headless form is never shown (that gate
made every v2.7 check vacuous):

1. no visible zero-size control; no negative layout;
2. owner-drawn primitives fit their measured text (`IUiTextFit`);
3. card content exists and lies inside its body (`IUiContentHost`); collapsed
   content in a card body is a finding;
4. non-autosize labels fit; container-owned wrapping labels cover their wrapped
   text height;
5. interactive controls are Tab-reachable and carry accessible names;
6. no unintended sibling overlap.

Findings print as `FINDING …`, are written to `ui-smoke.txt` (plus
`ui-smoke-layout.txt` / `ui-smoke-shell.txt` bounds trees), and make the
process exit 1. Each v2.8 invariant was proven to fail on the real defects
before the fix.

## Snapshots

`--ui-snapshot-page=<home|abilities|intelligence|configuration|diagnostics>`
with `--ui-snapshot=<path>`, `--ui-snapshot-width=<px>`, `--ui-snapshot-height=`
renders a page offscreen (asserted non-blank). The review set
(5 pages × 1440/1280/1024/880/480/360) lives in `dist/ui-snapshots` and is
regenerable; `build.ps1` preserves the folder on republish. Snapshots are
design artifacts — never behavioral proof, and not an honest DPI simulation
(real 100/150/200% runs remain on the live checklist).

## Rules

1. The UI never owns engine state and never sends input.
2. One canonical ability representation: explorer/inspector/CLI render the same
   `AbilityDefinition` + audit view.
3. Settings schema (`settings.ini` sections/keys) is unchanged.
4. No layout work on the engine tick; explorer filtering is cached and
   debounced; the ambient layer is painted once per resize.
