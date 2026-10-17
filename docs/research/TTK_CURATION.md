# TTK curation — sources and audit trail (v3.2.0, Workstream T-B)

Scope: the `minTtkSec` / `executeBelowPct` / `executeFavored` fields in
`app/MaxDpsCompanion/Knowledge/abilities.json`, plus the B1 cooldown/duration
verification of every MajorOffensive / Transformation / Summon entry.
Companion-side only; nothing here changes the wire protocol or `Catalog.lua`.

Search dates: **2026-09-29**. Tools: Tavily web search; sources are Wowhead,
Warcraft Wiki (warcraft.wiki.gg), Icy Veins, Maxroll, Method and Blizzard
Midnight patch notes — all targeting the 12.1 / Midnight season.

## 1. B1 — cooldown / duration verification

Every curated entry whose `purpose` is `MajorOffensive` or whose
`offensiveUsage` is `Transformation`/`Summon` was checked. Only five carry a
`cdMs`/`durMs` in the curated layer; the rest are vendor-derived MaxDps-backed
rows where MaxDps owns the timing (T2's two-uses rule skips an absent
cooldown).

| Id | Name | Curated before | Verified | Action |
|---|---|---|---|---|
| 228920 | Ravager (WARRIOR) | `cdMs 90000`, `durMs 12000` | Wowhead: 1.5 min CD, 12 s duration | **confirmed, no change** |
| 191427 | Metamorphosis (DEMONHUNTER/Havoc) | `cdMs 240000`, `durMs 20000` | Blizzard Midnight pre-expansion notes: "Metamorphosis cooldown reduced to 2 minutes"; Icy Veins 12.1 Havoc guide: 2 min, 20 s | **corrected `cdMs` 240000 → 120000** (+`sourceUrl`) |
| 370965 | The Hunt (DEMONHUNTER/Havoc) | `cdMs 90000` | Icy Veins 12.1: "1.5-minute cooldown" | **confirmed, no change** |
| 1217605 | Void Metamorphosis (DEMONHUNTER/Devourer) | `cdMs 120000`, `durMs 20000` | Wowhead/Icy Veins Devourer 12.1: no cooldown, no set duration — gated on 50 Soul Fragments (35 with Soul Glutton), duration is Fury-bar driven | **corrected: timer fields removed; usage classed Transformation** (+`sourceUrl`) |
| 1246167 | The Hunt (DEMONHUNTER/Devourer) | `cdMs 90000` | The Hunt baseline is 1.5 min (Icy Veins 12.1) | **confirmed, no change** |
| 1221150 | Collapsing Star (DEMONHUNTER/Devourer) | no timer | 2.5 s cast used inside Void Metamorphosis, no cooldown | **confirmed, no change** |

Entries with no curated `cdMs`/`durMs` (Summon Infernal, Shadowfiend, Army of
the Dead, Summon Gargoyle, Summon Darkglare, Summon Demonic Tyrant, and the
bulk of the MajorBurst/WindowDriven rows) were not given new timer values:
the T-B ownership rule allows **TTK fields only**, and adding cooldowns is
T-A/T-B follow-up work. Where a cooldown is absent the T2 two-uses rule is
documented to skip; the T1 waste guard still fires from `minTtkSec`.

Source URLs used for B1:

- Ravager — <https://www.wowhead.com/spell=228920/ravager>
- Metamorphosis (Havoc) — <https://www.icy-veins.com/wow/havoc-demon-hunter-pve-dps-rotation-cooldowns-abilities>
  and the Blizzard Midnight pre-expansion content update notes
- The Hunt — <https://www.icy-veins.com/wow/havoc-demon-hunter-pve-dps-rotation-cooldowns-abilities>
- Void Metamorphosis — <https://www.wowhead.com/guide/classes/demon-hunter/devourer/rotation-cooldowns-pve-dps>
- Warcraft Wiki cross-check — <https://warcraft.wiki.gg/wiki/Metamorphosis_(Havoc)>
  (wiki still showed the pre-12.1 3-minute CD; the live Blizzard note and Icy
  Veins 12.1 both say 2 minutes, so the newer source wins).

## 2. B2 — `minTtkSec` curation

Defaults come from plan §3.3 and are only overridden where a source justifies
it. Explicit values:

| Id | Name | Usage | `minTtkSec` | Why |
|---|---|---|---|---|
| 42650 | Army of the Dead | Summon | 30 | 8 min CD + long rune setup; a short target wastes the whole summon |
| 1122 | Summon Infernal | Summon | 20 | document the summon tier |
| 34433 | Shadowfiend | Summon | 20 | document the summon tier |
| 49206 | Summon Gargoyle | Summon | 20 | document the summon tier |
| 205180 | Summon Darkglare | Summon | 20 | document the summon tier |
| 265187 | Summon Demonic Tyrant | Summon | 20 | document the summon tier |
| 207289 | Unholy Assault | ShortCooldown | 5 | 75 s CD, waste-safe at the short tier |
| 375982 | Primordial Wave | ShortCooldown | 5 | 45 s CD, waste-safe at the short tier |

Everything else in the major set takes its documented tier default (MajorBurst
12, Transformation 20, Summon 20, WindowDriven 10, ProcDriven 5, AoeOnly 5,
SingleTargetOnly 5, ResourceDriven 5, unknown 10). The conformance test
(`TtkCurationTests`) fails if a curated major entry is neither explicit nor on
that documented-default table.

## 3. B2 — execute synergy (sparse)

| Id | Name | `executeBelowPct` | `executeFavored` | Source |
|---|---|---|---|---|
| 360194 | Deathmark (ROGUE/Assassination) | 35 | true | Maxroll Assassination Raid Guide 12.1, "Zoldyck Recipe": *"If you can only get 1 more use of Deathmark for the remainder of the fight, it is advised that you save it for when the boss hits 35% or below."* <https://maxroll.gg/wow/class-guides/assassination-rogue-raid-guide> |

No other ability is marked favored-into-execute. Checked and **rejected**:
Warrior Recklessness (1719) / Avatar (107574) — Method and Icy Veins 12.1 both
say "use on cooldown, do not hold more than ~10 s"; there is no execute-phase
hold. The execute rule is therefore enabled for exactly one ability, matching
the plan's "sparse, default OFF" instruction.

## 4. B4 — target-band freshness (no Lua change)

The target HP band is re-read every bridge tick; there is no not-ready cache.
Evidence:

- `addon/MaxDpsBridge/Reader.lua:1323-1341` — `HealthPct(Unit)` calls
  `UnitHealth`/`UnitHealthMax` through a fresh `pcall` on every invocation and
  scrubs the result; nothing is memoized.
- `addon/MaxDpsBridge/Reader.lua:1397-1402` — `MDB.GetTargetContext()` calls
  `HealthPct("target")` and derives the 0..14 band each call.
- `addon/MaxDpsBridge/Bridge.lua:407-415` — `WriteTarget()` calls
  `MDB.GetTargetContext()` with no caching.
- `addon/MaxDpsBridge/Bridge.lua:573` (`Update`) → `Bridge.lua:740`
  (`WriteTarget()`) — the call sits directly in the per-tick `OnUpdate` path.

Conclusion: **no Lua change needed**; the harness stays at 153 checks with no
new failing test required.

## 5. Known limits

- Many vendor-derived offensive rows have no curated cooldown, so the T2
  two-uses bypass cannot evaluate for them until a cooldown is curated.
- Void Metamorphosis is resource-gated; a linear TTK estimator cannot model
  its Soul-Fragment availability.
- Live retail validation of the TTK gates remains **OWED** (see
  `docs/TESTING.md` §3): offline curation and conformance tests are not a
  live in-game pass.
