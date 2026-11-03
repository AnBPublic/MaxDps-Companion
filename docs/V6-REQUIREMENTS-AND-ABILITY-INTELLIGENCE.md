# Northstar v6.0.0 — all-ability intelligence and acceptance gates

**Target:** Retail Midnight **12.1.5** · addon TOC **120105**
· app v6.0.0 · **pixel wire v5/Ext5 unchanged** · 13 classes,
40 specializations (27 DPS / 6 tanks / 7 healer inventories).

## Reality check: all abilities are classified, NOT all abilities are mastered

Run the Windows executable with:

```powershell
.\MaxDpsCompanion.exe --ability-contracts=all-abilities.tsv
```

Each registered spell gets ONE stable ID row documenting: name,
class/spec membership, category, purpose, disposition, required
observable evidence, status, ownership, source patch, last validated
patch, current-patch live verification, Companion extra eligibility,
and a human-readable limit. No one is silently counted as "perfect."

| Disposition | Runtime meaning |
| --- | --- |
| MaxDpsDelegated | Upstream handles the rotation/when-to-use; Companion may veto unsafe context |
| SharedSafetyGate | MaxDps or bridge proposes, Companion evaluates HP/urgency/buff/overlap |
| CompanionConditional | Curated extra eligible only when its known triggers are observed |
| Manual | Must be decided by a player (e.g. unknown target ally/debuff/DR) |
| ResearchPending | Incomplete knowledge; never independently generated |
| Unobservable | Midnight does not expose sufficient safe facts; no independent action |

**Source gap:** catalog was pinned at MaxDps **11.3.49** and
curated spell data **12.1 / interface 120100**. Current upstream
**11.3.58** supports Retail **12.1.5** and was published 2026-11-01.
This release does NOT falsely upgrade the embedded source. The
12.1.5 client imposes secret-value restrictions, and 40-spec
inventory is not proof every active ability/talent variant was
live-tested or that raids are solved automatically.

## Eight combat-situation acceptance contracts

| Situation | Expected actual Northstar behavior | Verification |
| --- | --- | --- |
| Ordinary fight | Follow current usable MaxDps glow/selection in Main; never synthesize a second DPS rotation | Main provider, action scheduler, Lua main tests |
| Elite/boss | Preserve MaxDps's chosen offensive cooldown and optional Burst TTK logic. Ext5 boss-grade awareness requires enabling Ext5 and /reload | Ext5 boss tests; MUST field-test |
| Fast health loss | Existing v5 observed rapid, sustained and critical trend flags promote eligible known defensives; cannot predict hidden future damage | Pressure tests / coverage overlap |
| Enemy cast | Interrupt has top rank. Exact observed No cast / noninterruptible vetoes; where sensor is unknown MaxDps wire is delegated rather than made-up cast knowledge | Kick/interrupt tests; field signals uncertain |
| Critical health | Known emergency defensive/self-sustain prioritized, Solo/Group rules and individual HP limits still apply | Policy tests / v5 pressure regressions |
| Manual OFF | The chosen spell and all known curated spell variants are vetoed before ANY Main/emergency/CC pathway; conflicting Always cannot override | Northstar alias tests |
| Chat / switch window | Chat typing veto holds all sends. Foreground game HWND checked at capture AND each send (including target/interact), even with legacy background setting | Pixel protocol / OS SlotGate tests |
| No trusted MaxDps Main | Hold: static Fury/Arms/Ret fillers OFF by default; only explicit /mdb mainfallback on restores fallback | Lua secret harness strict case |

The ability registry holds **all known registered IDs** in this
source snapshot, *not the complete current live spell/talent corpus*.
Per-spell requirements without observable signals intentionally hold
or delegate to MaxDps; adding a guessed condition is a regression.

## Class/spec & leveling test checklist

Every one of 33 DPS/tank specs: new character if supported by the
class's starting level, early 10–20 / 30–50 / 60–80 / max 90,
multiple talent and hero-talent builds, class/spec switching,
unknown/unbound/unlearned buttons, one known disabled spell + its
variant, Main, offensive, health-pressure, self-heal, interrupt,
GCD and cast/channel state. Every role must be tested in both Solo
and Group; separate healer inventory requires its own ally-target
healing system before comprehensive healer support is claimed.

Level 1 is not available for every class (e.g. hero classes);
the realistic requirement is *the class's actual playable leveling
range up to 90*, not fabricated levels for every class.

## All-content scenario QA, not fabricated encounter intelligence

- Solo world/questing, elites and Aqir Invasions, story and worldboss
- Delves through hardest available challenge; Labyrinth of Kindo'jan
  nine-room gauntlet and Tier 11
- Follower/normal/heroic/Mythic dungeons and Mythic+ Keystone Mythic
- Raid Finder/Normal/Heroic/Mythic raid, especially Kith'ix
- Target dummy, low-HDR/high-HDR, 51/58-cell bridge frames, different
  UI scales, display resolutions, alt-tab, chat while keys are queued

Record actual button, spell ID, when/why, observed missing signals,
unexpected cooldown, missed kick, detected talent drift, wrong spec
and real FPS/CPU/GPU. Gear, positioning, mechanics, fellow players
and hidden data determine high-end success beyond any rotation helper.

## Current blockers to an honest 100% completion certificate

1. Re-extract and audit 11.3.58 live MaxDps abilities and all class
   modules; diff against current 11.3.49 pin without editing the
   authoritative upstream blindly.
2. Verify changed 12.1.5 spells and hero talents against current
   client DB2, not stale 12.1 spell verification.
3. Re-test EVERY actual spell/talent combination and each encounter
   with source and evidence; no offline suite establishes perfection.
4. Observe if secret API states are unknowable; manual or delegated
   remains the only safe disposition without those facts.
5. A/B test WoW-alone vs 5.0 vs 6.0 on matched hardware for average
   FPS, 1% lows, p95/p99 frametimes, spikes, desktop CPU/GPU.
6. Confirm input automation and addon use with Blizzard's current
   rules before use on a valued live account.

## Primary dated sources

- Official content patch (Blizzard, Oct 2026):
  https://worldofwarcraft.blizzard.com/en-us/news/24304162/
- Official 12.1.5 live feature overview:
  https://news.blizzard.com/en-us/article/24307306/
- Retail 12.1.5 interface 120105:
  https://warcraft.wiki.gg/wiki/Patch_12.1.5
- MaxDps upstream 11.3.58 released for Retail 12.1.5:
  https://www.curseforge.com/wow/addons/maxdps-rotation-helper/files/all?gameVersionTypeId=517&page=1
- Midnight secret-value limitations:
  https://news.blizzard.com/en-us/article/24246290/
