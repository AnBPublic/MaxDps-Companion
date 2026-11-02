# Vanguard v5.0.0 — 40-spec, all-content acceptance and evidence ledger

**Date:** 2026-11-02 · **Target Retail:** Midnight 12.1.5 (`120105`)
· **Candidate:** v5.0.0-rc.1 · **Source branch:** `release/v5.0.0-vanguard`

## 0. Source of truth — verified versus modeled

1. **Client:** Official [Blizzard 12.1.5 content notes](https://worldofwarcraft.blizzard.com/en-us/news/24304162/)
   confirm The Unbinding of Kith'ix raid, nine-chamber Labyrinth of Kindo'jan,
   Tier 11 solo mode, mid-season Mythic Keystone, outdoor Aqir Invasions.
   Interface `120105` is documented by Warcraft Wiki's
   [12.1.5 addon API changes](https://warcraft.wiki.gg/wiki/Patch_12.1.5/API_changes).
2. **MaxDps source:** The repo embeds a `MaxDps 11.3.49` vendor snapshot,
   `gamePatch: "12.1"`, `interface: 120100` and older
   `spell-verification.json` records. These are **not** complete new
   12.1.5 research. The release does **not** assert updated hero talents
   or changed 12.1.5 spell definitions without a new audited extraction.
   Check the actual upstream MaxDps API / rotation files and latest
   current build before refreshing the vendored snapshot.
3. **Every-spec inventory:** C# `SpecCoverageMatrix.Build` iterates all
   40 protocol-known specs and separately counts registered, truly
   live-verified, pending, delegated, manual, known-spell book,
   and talent-annotation entries. Do not interpret registered count as
   "available at level N" or "safe to auto-use".
4. **Midnight secret combat values:** [Blizzard's official addon
   disarmament policy](https://news.blizzard.com/en-us/article/24246290/combat-philosophy-and-addon-disarmament-in-midnight)
   hides many signals from addon decision logic. UNKNOWN must remain
   UNKNOWN; don't infer cast identity, school, ally state, enemy count,
   debuff contents, encounter timer or future damage. No source of
   truth means advisory/hold, never invent data.
5. **Account/platform compliance:** review companion-driven automatic
   key input with Blizzard's game rules separately. A green offline CI
   cannot establish that any automation is permitted.

## 1. All specialization identities (not all specializations live-verified)

| Class | DPS specs | Tank specs | Healer specs |
|---|---|---|---|
| Death Knight | Frost, Unholy | Blood | — |
| Demon Hunter | Devourer, Havoc | Vengeance | — |
| Druid | Balance, Feral | Guardian | Restoration |
| Evoker | Augmentation, Devastation | — | Preservation |
| Hunter | Beast Mastery, Marksmanship, Survival | — | — |
| Mage | Arcane, Fire, Frost | — | — |
| Monk | Windwalker | Brewmaster | Mistweaver |
| Paladin | Retribution | Protection | Holy |
| Priest | Shadow | — | Discipline, Holy |
| Rogue | Assassination, Outlaw, Subtlety | — | — |
| Shaman | Elemental, Enhancement | — | Restoration |
| Warlock | Affliction, Demonology, Destruction | — | — |
| Warrior | Arms, Fury | Protection | — |

**Totals:** 13 classes, 27 DPS, 6 tank, 7 healer, 40 specs.
The player's requested **DPS + tank** scope is 33 specs;
healers have identity/inventory coverage only, NOT a claim of autonomous
healer rotation or group healing intelligence.

## 2. Seven acceptance gates

| Requirement | v5 candidate code | Still mandatory for production |
|---|---|---|
| Main rotation | MaxDps suggestion remains authoritative; learned/bound/readiness checks and rejection recovery retained | Real patch-12.1.5 MaxDps source/spec/talent parity; each spec at every leveling breakpoint; confirm no missed spells or false sends |
| Survival | Bounded trusted short-horizon HP trend; existing minor/major/immunity/overlap rules; existing tank mitigation | Real effects/duration, external damage and tank-specific buffs, boss and dungeon mechanics, proof of conservative activation |
| Cooldowns | Existing TTK, Burst, boss-grade and conflict memory; no fabricated enemy count | Test on 3s trash, 30s elites, 2m boss, 8m raid; check both too-early use and indefinite conservation |
| Interrupt/utility | Existing interruptibility/CC and DR vetoes; unknown data cannot magically identify spells | Verify cast start, interruptible/immune, kick availability, spell reflect, stuns/CC, migration between adds, failed attempts |
| Modes | Solo survival focus, Group conservative, strict per-spell OFF and manual holds | Explicitly test 5-player dungeons, Mythic+, story/normal/heroic/mythic raid, world events and group Delves |
| FPS/CPU | 4.8 Lua reductions and 4.9 3-row 58-cell BitBlt; forecast no new game API/capture | Equal-scene A/B with no companion, 4.9 and 5.0; p95/p99/1% lows, Windows GDI capture timing and Lua UI costs |
| Spec/level/talents | Static inventory covers all 40; known-spell/talent readiness guard inherited | Level 1–90 learned/removed skill transitions, spec changes, talent/hero-talent swaps, gear/build variations, each binding and macro |

## 3. Gameplay-context regression matrix

**World content:** open-world trash, elites, rare spawns, named worldboss,
Aqir Invasions, Prey encounters and solo quest progression. Check short
trash cooldown conservation, target swapping, outdoor movement, death,
respawn and cross-zone transitions.

**Delves and Labyrinth:** each supported tier and seasonal mechanic,
especially Tier 11; solo and party runs; dangerous channel/casts,
interrupt backups, low health, self-healing, healer-follower interactions
and long boss burn windows. Validate with both tank and DPS specs.

**Dungeons and Mythic Keystone:** all current-season dungeons at normal,
heroic, mythic and M+; trash pack burst, route interrupts, tank active
mitigation, party-CC breaks, affixes and prioritizing survival vs damage.
Cannot infer other players' cooldowns or ally healing from hidden state.

**Raids:** Story/LFR/Normal/Heroic/Mythic raid; raid boss priority and adds,
movement-phase channels, important on-demand utility, interrupts, opener
release, burn windows, long multi-minute target TTK, wipe/restart.
Include `The Unbinding of Kith'ix` and earlier Season 2 encounters.

**Solo/Group mode comparison:** for the same spec and combat event, verify
expected changes only in permitted Solo-specific self-sustain/gap-fill;
main rotation never becomes a second independent rotation. Every
explicitly OFF ability stays OFF. No key is sent while chat is open,
the user is typing, WoW is minimized, not focused (when configured),
or a different app owns input.

**Talent / class specs:** for each of 40 specs, test a default loadout,
a distinct hero-talent choice if available, and known tricky talent
variants; ensure spell IDs are recognized/known, bound and ready; verify
per-ability disable and macro alias. No fake GCD/unknown cooldown
assumptions. High-risk manual skills remain manual by default.

**Level progression:** sample at levels 1, 10, 20, 30, 40, 50, 60, 70,
80, 85 and 90, plus any level where the client actually grants,
replaces or removes an active ability. Perform talent swap while
logged in, logout/login, death, zone and loading screen. This is an
acceptance *plan*, NOT proof leveling has been played.

## 4. Required metrics and release gate

- **Offline:** C# tests 0 failing, Lua secret harness 0 failing,
  `luac -p` clean, ability audit 0 violations/missing/stale per its
  source snapshot; Windows + macOS/Core builds, release identity synced.
- **Behavioral:** 0 incorrect unbound sends, 0 user OFF violations,
  0 unknown-target interrupt assumptions, 0 phantom active mitigation,
  0 stalled Main while normal legal input is available. Reproduce each
  failure with telemetry and fix rather than suppress.
- **Performance:** A/B no-companion vs 4.9 vs 5.0 at identical
  settings/HDR/Frame Generation and with replicated encounter scenes,
  ≥60–120s warm captures. Document average FPS, 1% low, p95/p99,
  >50ms spikes, CPU/GPU/process utilization. No numerical FPS claim
  without these samples.
- **Compatibility:** client version/realm, interface, all addons,
  UI scale/HDR, screen geometry, MaxDps vendor version and user settings.
- **Go/no-go:** v5 is **RC until actual cross-spec and content results
  are attached**. 100% perfection is not a test result; each requirement
  must have explicit, reproducible evidence, not a blank PASS.
