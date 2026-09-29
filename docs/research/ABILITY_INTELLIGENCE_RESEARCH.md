# Ability Intelligence Registry — Research Artifact (Retail Midnight 12.1)

- **Patch:** 12.1 (live Retail, Interface 120100; NOT PTR, NOT 12.1.5)
- **Date checked:** 2026-09-28
- **Local ground truth:** `app/MaxDpsCompanion/Knowledge/spell-verification.json` (3566 entries, 3515 verified),
  `vendor-abilities.json` (368 rows, 137 Offensive rows / 107 distinct Offensive ids),
  `abilities.json` (curated), `vendor/MaxDps/Cooldowns.lua` (vendor pin v11.3.49).
- **Rule applied:** every id below was checked against `spell-verification.json`. Ids absent there are marked
  MISS and excluded from `registry-research.json`. Web sources: Tavily snippets + warcraft.wiki.gg extracts +
  Blizzard patch notes (see §6). Wowhead direct fetch is blocked from this machine; Wowhead data below comes
  only from Tavily search snippets and is marked as such.
- **Precedence used on disagreement:** live DB2 verification > vendor tables > Blizzard docs > community docs.

---

## 1. Interrupt intelligence (highest priority)

PvE lockout durations are the **Midnight 12.0 values** (Blizzard pre-expansion notes, source [S3]):
all class interrupts were buffed for non-PvP combat (e.g. Pummel 3→5 s, Kick 3→6 s, Counterspell 5→7 s,
Wind Shear 2→4 s, Quell 4→6 s, Spell Lock 5→7 s). Community wiki tables that still show the old
3 s/4 s values are **stale** and marked as disagreements below. Patch 12.1.0 added a "missed" visual/sound
when an interrupt lands with no cast active (source [S3]/[S13]).

| SpellId | Name | Class(es) | Kind | Cooldown (s) | Range | Interrupts spells? | Break-on-damage? | Notes | Source | Confidence |
|---|---|---|---|---|---|---|---|---|---|---|
| 6552 | Pummel | Warrior (Arms/Fury/Prot) | Dedicated interrupt | 15 | Melee (~5 yd) | Yes — school lockout 5 s PvE | No (lockout, not CC) | Off-GCD, no cost. Verified live name "Pummel". | [S1] vendor classInterrupts; [S7] wiki Pummel (15 s, melee, 5 s); [S3] Blizzard 12.0 notes | High |
| 96231 | Rebuke | Paladin (Holy/Prot/Ret) | Dedicated interrupt | 15 | Melee (~5 yd) | Yes — 5 s PvE | No | Same profile as Pummel per wiki. | [S1]; [S3]; [S2] Liquipedia table | High |
| 47528 | Mind Freeze | Death Knight (Blood/Frost/Unholy) | Dedicated interrupt | 15 | 15 yd (extended melee) | Yes — 5 s PvE | No | Wowhead snippet: 15 yd, 15 s, 0 s GCD. Wiki prose confirms 15-yd range. | [S1]; [S6] wowhead snippet via Tavily; [S7] wiki Interrupt prose | High |
| 183752 | Disrupt | Demon Hunter (all incl. Devourer per vendor) | Dedicated interrupt | 15 | 10 yd (very short) | Yes — 5 s PvE | No | Generates Fury/Pain on success (Liquipedia). | [S1]; [S2]; [S3] | High |
| 106839 | Skull Bash | Druid (all forms; cat/bear) | Dedicated interrupt | 15 | 13 yd | Yes — 5 s PvE | No | Requires cat/bear form (wiki). | [S1]; [S7] wiki prose; [S3] | High |
| 351338 | Quell | Evoker (Devastation/Augmentation talent, row 3) | Dedicated interrupt | 20 | 25 yd | Yes — 6 s PvE | No | **Disagreement recorded:** wiki Interrupt *table* still shows 40 s CD / 4 s; the Quell *page* + Wowhead snippet show 20 s CD / 6 s (12.0 change: 40→20 s, 4→6 s). Prefer page/snippet. Not a base ability — talent-gated. | [S1]; [S8] wiki Quell page; [S9] wowhead Quell snippet via Tavily; [S3] | Medium-High |
| 147362 | Counter Shot | Hunter (BM/MM) | Dedicated interrupt | 24 | 40 yd | Yes — 5 s PvE | No | Long-range; old forum value "3 s lockout" is stale. | [S1]; [S3]; [S2] | High |
| 187707 | Muzzle | Hunter (Survival) | Dedicated interrupt | 15 | Melee (~5 yd) | Yes — 5 s PvE | No | Survival-only; melee-range unlike Counter Shot. | [S1]; [S3] | High |
| 2139 | Counterspell | Mage (Arcane/Fire/Frost) | Dedicated interrupt | 24 | 40 yd | Yes — 7 s PvE | No | **Minor disagreement:** one Wowhead snippet shows "25 sec cooldown"; wiki/Liquipedia say 24 s. Prefer 24 s. Longest PvE lockout. | [S1]; [S2]; [S9] wowhead snippet (25 s); [S3] (7 s) | Medium-High |
| 116705 | Spear Hand Strike | Monk (Brewmaster/Mistweaver/Windwalker) | Dedicated interrupt | 15 | Melee (~5 yd) | Yes — 5 s PvE | No | Same profile as Pummel/Rebuke. | [S1]; [S2]; [S3] | High |
| 15487 | Silence | Priest (Shadow only) | Silence | 45 (30 w/ Last Word talent) | 30 yd | Yes — silence + 5 s PvE interrupt | No | Doubles as full silence; longest CD of the set. | [S1]; [S2]; [S3] (5 s / 45 s) | High |
| 1766 | Kick | Rogue (Ass/Outlaw/Sub) | Dedicated interrupt | 15 | Melee (~5 yd) | Yes — 6 s PvE (best ratio) | No | Strongest standard lockout (6 s). | [S1]; [S3]; [S2] | High |
| 57994 | Wind Shear | Shaman (Ele/Enh/Resto) | Dedicated interrupt | 12 | 30 yd | Yes — 4 s PvE | No | Shortest CD interrupt; ranged. | [S1]; [S3]; [S2] | High |
| 19647 | Spell Lock (vendor id) | Warlock (Aff/Demo/Destro, Felhunter pet) | Silence | 24 | 30 yd | Yes — 6 s PvE + 3 s silence | No | **VERIFICATION FAILURE — disagreement:** 19647 is **absent** from `spell-verification.json` (MISS). Live DB2 verified id is **119910 "Spell Lock"** (icon spell_shadow_mindrot). Wiki: pet ability via Command Demon, usable while master is CC'd. JSON registry uses 119910, not 19647. | [S1] vendor (19647); [S10] wiki Spell Lock (24 s, 30 yd, 7 s PvE per [S3]); [S0] verification (19647 MISS / 119910 OK) | Medium (values) / High (drift finding) |

### Per-class interrupt gaps and spec differences

- **No interrupt:** Priest Discipline and Priest Holy — vendor `classInterrupts` blocks are empty (`{}`); confirmed
  in `vendor/MaxDps/Cooldowns.lua` lines ~788+. Only Shadow has Silence (15487). Source [S1]. Confidence: High.
- **Pet-based:** Warlock Spell Lock is cast by the Felhunter (works while the Warlock is stunned/silenced), and
  the vendor id (19647) does not match the live client (119910). Source [S10] + [S0]. Confidence: High.
- **Spec splits:** Hunter BM/MM Counter Shot (ranged 40 yd/24 s) vs Survival Muzzle (melee/15 s). All other
  classes share one interrupt across specs. Evoker Quell is talent-gated (not baseline). Druid Skull Bash needs
  cat/bear form. Confidence: High.

### Can an addon reliably know a target cast's spell identity/danger in Midnight 12.1?

**No — the existing project finding is CONFIRMED, not refuted.** Combat-log events are unavailable to addons
(`COMBAT_LOG_EVENT_UNFILTERED` removed for addon code in 12.0; combat-log chat is KStrings), and enemy combat
state (spellcasts, auras, health, power) is a "secret value": displayable but not knowable (source [S4]
warcraft.wiki.gg `Patch_12.0.0/Planned_API_changes`; source [S5] Blizzard "Combat Philosophy and Addon
Disarmament" via Blue Tracker). Blizzard's stated intent includes blocking "optimal rotation helpers" that key
off enemy state (source [S4]). Consequence for this project: the bridge's arg-blind
`UNIT_SPELLCAST_NOT_INTERRUPTIBLE` sensor (boolean in the event *name*, no payload read) plus the MaxDps
`GlowInteruptMidnight` flag is the maximum legitimate signal — "a cast is live / it is (not) interruptible",
never *which* spell or how dangerous it is. Confidence: High.

---

## 2. Offensive cooldown classes

Vendor Offensive bucket: 137 rows / 107 distinct ids (extracted 2026-09-28 via PowerShell from
`vendor-abilities.json`). Of those, **19 ids are absent from the live-client verification** and are listed in
§2b, not proposed. Curated `MajorOffensive` entries in `abilities.json` (191427, 1217605, 1221150, 1246167,
370965) overlap the vendor set except the three Devourer ids (also MISS — Devourer is too new for this DB2
export). Classified below: **51 entries** (cap 60 respected), one major per spec where a verified id exists.
Cooldowns are base values; Midnight hero talents frequently reduce them (e.g. Anger Management, The Beast
Within −60 s, Wingleader CDR) — noted, not modeled.

| SpellId | Name | Class/Spec | Class | Cooldown | Required target count | Requires own buff/proc | Notes | Source | Confidence |
|---|---|---|---|---|---|---|---|---|---|
| 1719 | Recklessness | Warrior/Fury | Warrior | 90 s | — | No | MajorBurst. Method 12.1: effective ~45 s w/ Anger Management; hold ≤10 s. Verified "Recklessness". | [S0]; [S1]; [S11] Method Fury 12.1 | Medium-High |
| 107574 | Avatar | Warrior/All | Warrior | 90 s | — | No | MajorBurst. Sources conflict 90/180 s; use 90 s. Verified. | [S0]; [S1]; ABILITY_RESEARCH §2 (conflict noted) | Low-Medium |
| 262161 | Warbreaker | Warrior/Arms | Warrior | 60 s | 1+ (cone) | No (pairs w/ Colossus Smash) | MajorBurst. Verified "Warbreaker". | [S0]; [S1] | Low-Medium |
| 31884 | Avenging Wrath | Paladin/All | Paladin | 120 s | — | No | MajorBurst. Wowhead snippet: 2 min / 20 s duration. 12.1 Ret builds reach ~60 s via Wake of Ashes synergy (Icy Veins via timesaver). | [S0]; [S1]; [S12] wowhead snippet; [S13] timesaver/Icy Veins 12.1 | Medium-High |
| 231895 | Crusade | Paladin/Ret | Paladin | 120 s | — | No | MajorBurst. **Name note:** verification file names 231895 "Avenging Wrath" (Crusade replaces AW on the bar). | [S0]; [S1] | Medium |
| 343721 | Final Reckoning | Paladin/Ret | Paladin | 60 s | — | No (sets up window) | WindowDriven. Damage-amp window for the burst. Verified. | [S0]; [S1] | Low-Medium |
| 375576 | Divine Toll | Paladin/Holy | Paladin | 60 s | 1+ | No | MajorBurst (healing+damage). Verified. | [S0]; [S1]; ABILITY_RESEARCH §1 | Medium |
| 389539 | Sentinel | Paladin/Prot | Paladin | 120 s | — | No | MajorBurst. Verified "Sentinel". | [S0]; [S1] | Low-Medium |
| 12472 | Icy Veins | Mage/Frost | Mage | 180 s | — | No | MajorBurst. Sources conflict 120/180 s; use 180 s. Verified. | [S0]; [S1]; [S13] timesaver ("60-second CD" refers to Ret AW, not IV — do not cross-apply) | Low-Medium |
| 190319 | Combustion | Mage/Fire | Mage | 120 s | — | No (spends Hot Streaks) | MajorBurst. 90/120 s conflict; use 120 s. Verified. | [S0]; [S1] | Low-Medium |
| 365350 | Arcane Surge | Mage/Arcane | Mage | 90 s | — | Yes — Touch of the Magi window ("Big Burn") | MajorBurst. Method + leprestore 12.1 agree: 90 s Surge / 45 s Touch. Verified. | [S0]; [S1]; [S14] Method Arcane 12.1; [S15] leprestore 12.1 | High |
| 12051 | Evocation | Mage/Arcane | Mage | 90 s | — | No (mana restore) | ResourceDriven. Verified. | [S0]; [S1] | Low-Medium |
| 194223 | Celestial Alignment | Druid/Balance | Druid | 180 s | — | No (alternates w/ Incarnation) | MajorBurst. TWW duration cut 20→15 s (Blizzard forum); CD 180 s. Verified. | [S0]; [S1]; [S16] Blizzard forum; [S17] Icy Veins Balance 12.1 | Low-Medium |
| 102560 | Incarnation: Chosen of Elune | Druid/Balance | Druid | 180 s | — | No | Transformation. 20 s duration (TWW change). Verified. | [S0]; [S1]; [S16] | Low-Medium |
| 106951 | Berserk | Druid/Feral | Druid | 180 s | — | No | MajorBurst. Verified "Berserk". | [S0]; [S1] | Low-Medium |
| 391528 | Convoke the Spirits | Druid/Resto | Druid | 120 s | — | No (needs safe channel) | MajorBurst. 4 s channel. Verified. | [S0]; [S1] | Low-Medium |
| 205180 | Summon Darkglare | Warlock/Aff | Warlock | 180 s | — | No | Summon. Verified. | [S0]; [S1] | Low-Medium |
| 1122 | Summon Infernal | Warlock/Destro | Warlock | 120 s | 1+ (impact AoE + stun) | Costs 1 Soul Shard | Summon. 2 min since 11.0 (was 3 min); Inferno talent −30 s. Verified. | [S0]; [S1]; [S18] wiki Summon Infernal | Medium |
| 265187 | Summon Demonic Tyrant | Warlock/Demo | Warlock | 60 s | — | Yes — extends existing demons | Summon. Verified. | [S0]; [S1] | Low-Medium |
| 196277 | Implosion | Warlock/Demo | Warlock | No CD (shard spender) | 2+ (AoE detonate) | Yes — needs imps out | AoeOnly. Popped per imps, not a timed burst. Verified. | [S0]; [S1] | Medium |
| 13750 | Adrenaline Rush | Rogue/Outlaw | Rogue | 180 s | — | No | MajorBurst. Midnight alpha: 15 s duration, +75% energy regen. Verified. | [S0]; [S1]; [S19] Wowhead alpha notes via Tavily | Medium |
| 121471 | Shadow Blades | Rogue/Sub | Rogue | 120 s | — | No | MajorBurst. Verified. | [S0]; [S1] | Low-Medium |
| 360194 | Deathmark | Rogue/Sin | Rogue | 120 s | Single target | No | SingleTargetOnly. Verified "Deathmark". | [S0]; [S1] | Low-Medium |
| 315508 | Roll the Bones | Rogue/Outlaw | Rogue | 45 s | — | Yes — keep good bones (proc logic, manual-adjacent) | ProcDriven. Short roll windows. Verified. | [S0]; [S1] | Medium |
| 384352 | Doom Winds | Shaman/Enh | Shaman | 90 s | — | No (summons a Nature Feral Spirit in Midnight) | MajorBurst. Verified. | [S0]; [S1]; [S19] alpha notes | Low-Medium |
| 114050 | Ascendance | Shaman/Ele (also 114051 Enh / 114052 Resto — same name, per-spec ids) | Shaman | 180 s | — | No | MajorBurst. 12.1 PTR: Ele overload bonus cut 75%→30% (flatter burst). Verified all three ids "Ascendance". | [S0]; [S1]; [S20] nexttier 12.1 PTR | Low-Medium |
| 375982 | Primordial Wave | Shaman/Resto | Shaman | 45 s | — | No | ShortCooldown. Verified. | [S0]; [S1] | Low-Medium |
| 98008 | Spirit Link Totem | Shaman/Resto | Shaman | 180 s | Group (raid DR) | No | DefensiveOffensiveHybrid. Healing/offensive vendor bucket but defensive use. Verified. | [S0]; [S1] | Medium |
| 123904 | Invoke Xuen, the White Tiger | Monk/WW | Monk | 120 s | — | No | MajorBurst (summon-like pet window; classed burst, not Summon, per registry practice). Verified. | [S0]; [S1] | Low-Medium |
| 387184 | Weapons of Order | Monk/All | Monk | 120 s | — | No | MajorBurst. Verified. | [S0]; [S1] | Low-Medium |
| 325197 | Invoke Chi-Ji, the Red Crane | Monk/MW | Monk | 180 s | — | No | MajorBurst (healing). Verified "Invoke Chi-Ji, the Red Crane". | [S0]; [S1] | Low-Medium |
| 42650 | Army of the Dead | DK/Unholy | Death Knight | 480 s | 1+ (AoE ghouls) | No (long rune setup) | Summon. 8-min army. Verified. | [S0]; ABILITY_RESEARCH §1 | Low-Medium |
| 49028 | Dancing Rune Weapon | DK/Blood | Death Knight | 180 s | — | No | MajorBurst. Insatiable Blade −30 s in Midnight. Verified. | [S0]; [S21] Blizzard Midnight notes | Low-Medium |
| 47568 | Empower Rune Weapon | DK/Blood/Frost | Death Knight | 120 s | — | Yes — pairs w/ Breath/Pillar windows | WindowDriven. Verified. | [S0]; [S1] | Low-Medium |
| 49206 | Summon Gargoyle | DK/Unholy | Death Knight | 180 s | Single target | No | Summon. Verified. | [S0]; [S1] | Low-Medium |
| 207289 | Unholy Assault | DK/Unholy | Death Knight | 75 s | — | No | ShortCooldown (opener-grade). Verified. | [S0]; [S1] | Low-Medium |
| 191427 | Metamorphosis | DH/Havoc | Demon Hunter | 240 s | — | No | Transformation. Verified. Vengeance twin 187827 is 120 s post-Midnight (Infernal Strike/Fracture notes) — do not cross-apply. | [S0]; [S1]; [S21] | Low-Medium |
| 370965 | The Hunt | DH/Havoc/Veng | Demon Hunter | 90 s | 1+ (charge + DoT) | No (doubles as gap closer — manual-aim risk) | MajorBurst. Verified. | [S0]; [S1] | Medium |
| 258860 | Essence Break | DH/Havoc | Demon Hunter | 40 s | 1+ (cone) | Yes — Chaos Strike window | WindowDriven. Verified. | [S0]; [S1] | Low-Medium |
| 403631 | Breath of Eons | Evoker/Aug | Evoker | 120 s | 1+ (line) | Yes — Ebon Might window; Wingleader CDR per target in 12.1 | MajorBurst. Verified. | [S0]; [S1]; [S22] skill-capped 12.1 notes | Low-Medium |
| 375087 | Dragonrage | Evoker/Dev | Evoker | 120 s | — | No | MajorBurst. Verified "Dragonrage". | [S0]; [S1] | Low-Medium |
| 357210 | Deep Breath | Evoker/Dev/Pres | Evoker | 30 s | 2+ (line AoE) | No | AoeOnly. Short flight; single-target loss. Verified. CD Low confidence — verify in game. | [S0]; [S1] | Low |
| 370553 | Tip the Scales | Evoker/All | Evoker | 120 s | — | Yes — next Empower instant/full | WindowDriven. Verified. | [S0]; [S1] | Low-Medium |
| 359844 | Call of the Wild | Hunter/BM | Hunter | 120 s | — | Yes — pet synergy | MajorBurst. Midnight BM main burst (Bestial Wrath 19574 still verified live but superseded in vendor by this). Verified. | [S0]; [S1]; [S21] | Low-Medium |
| 321530 | Bloodshed | Hunter/BM | Hunter | 60 s | Single target (bleed) | Yes — during Bestial Wrath | WindowDriven. Verified. | [S0]; [S1]; [S21] | Low-Medium |
| 288613 | Trueshot | Hunter/MM | Hunter | 120 s | — | No | MajorBurst. Verified "Trueshot". | [S0]; [S1] | Low-Medium |
| 360952 | Coordinated Assault | Hunter/Surv | Hunter | 120 s | — | Yes — pet synergy | MajorBurst. Verified. | [S0]; [S1] | Low-Medium |
| 228260 | Void Eruption (verified name "Voidform") | Priest/Shadow | Priest | 90 s | — | No | Transformation. **Name disagreement:** vendor "Void Eruption", live DB2 "Voidform". Verified true. | [S0]; [S1] | Low-Medium |
| 391109 | Dark Ascension | Priest/Shadow | Priest | 120 s | — | No | MajorBurst. Verified. | [S0]; [S1] | Low-Medium |
| 10060 | Power Infusion | Priest/All | Priest | 120 s | — | No (external-castable — manual by design for others) | WindowDriven. Verified. | [S0]; [S1] | Medium |
| 34433 | Shadowfiend | Priest/All | Priest | 180 s | Single target | No | Summon. Verified. | [S0]; [S1] | Low-Medium |

### §2b — Vendor Offensive ids NOT proposed (absent from live verification — dropped)

152279 (Breath of Sindragosa; live id is 1249658, verified), 193530 (Aspect of the Wild),
214621 (Schism; SpellData has 424509), 152173 (Serenity; SpellData has Tea of Serenity 393460),
320125 (Echoing Shock), 267217 (Nether Portal), 1276672/1276452/1276467 (Grimoire: Doomguard/Imp Lord/
Fel Ravager batch), 390279 (Vile Contagion), 201430 (Stampede), 1250646 (Takedown), 1264902 (Moonlight
Chakram), 375891 (Death Chakram), 343223 (Berserk: Frenzy), 385408 (Sepsis), 79140 (Vendetta — Classic/Mists
blocks only), 382411 (Eternity Surge), 1221150 (Collapsing Star), 1217605 (Void Metamorphosis),
1246167 (The Hunt Devourer), 116011 (Rune of Power), 1249625 (Zenith), 391112 (Dark Evangelism; SpellData has
391099). Plus 157153 (Cloudburst Totem — present but `verified:false`, empty name — omitted as unsafe).
Source: [S0] negative check + [S1]. These MUST NOT enter the registry until re-verified against a newer DB2 export.

---

## 3. Mobility intelligence (exact id set — 22 rows, all verified)

| SpellId | Name | Class | MobilityKind | Cooldown | Needs target? | Notes | Source | Confidence |
|---|---|---|---|---|---|---|---|---|
| 100 | Charge | Warrior | GapCloser | 20 s (classic sources 15 s — conflict, prefer curated/vendor-era 20 s) | Yes (enemy, 8–25 yd) | Stun on arrival. Verified "Charge". | [S0]; [S23] wiki Charge (15 s classic row); abilities.json (20 s) | Medium |
| 6544 | Heroic Leap | Warrior | GapCloser | 45 s | No (ground-targeted, 8–40 yd) | Deals AoE + resets Taunt (Prot). Verified. | [S0]; [S24] wiki Heroic Leap | High |
| 190925 | Harpoon | Hunter | GapCloser | 20–30 s (curated 20 s vs research 30 s — conflict) | Yes (enemy, 8–30 yd) | Survival core; root via talent. Verified. | [S0]; abilities.json; ABILITY_RESEARCH §1 | Low-Medium |
| 49576 | Death Grip | Death Knight | GapCloser | 25 s (research notes 15–25 s conflict) | Yes (enemy, 30 yd) | Taunt/grip; goto for peeled healers. Verified. | [S0]; abilities.json; ABILITY_RESEARCH §2 | Medium |
| 36554 | Shadowstep | Rogue | GapCloser | 30 s | Yes (enemy, 25 yd) | Teleports behind +70% speed 2 s. Vendor lists it as Offensive too — manual-aim risk. Verified. | [S0]; [S25] wiki Shadowstep snippet | Medium-High |
| 781 | Disengage | Hunter | Disengage | 20 s | No (backward leap) | Posthaste etc. talents. Verified. | [S0]; [S26] wiki Disengage (20 s modern row) | High |
| 1856 | Vanish | Rogue | Escape | 120 s | No | Stealth + threat drop; never automatic (curated). Verified. | [S0]; abilities.json | High |
| 198793 | Vengeful Retreat | Demon Hunter | Disengage | 25 s | No (backward dash) | Momentum synergy; 12.1 Hungering Slash grants temp charge (not reset). Verified. | [S0]; [S27] skill-capped 12.1 notes | Medium-High |
| 48020 | Demonic Circle: Teleport | Warlock | Teleport | 30 s (wiki infobox shows 10 s for the Circle entity — conflict, prefer curated 30 s) | No (pre-placed circle) | Removes slows. Verified "Demonic Circle: Teleport". | [S0]; [S28] wiki Demonic Circle; abilities.json | Medium |
| 1953 | Blink | Mage | Teleport | 20 s (wiki Midnight row 20 s vs curated 25 s — conflict, prefer wiki) | No (forward 20 yd) | Frees stuns/bonds. Verified. | [S0]; [S29] wiki Blink (20 s Midnight row) | Medium-High |
| 109132 | Roll | Monk | SpeedBurst | 15 s | No (directional) | Chi Torpedo shares slot philosophy. Verified. | [S0]; abilities.json | Medium |
| 115008 | Chi Torpedo | Monk | SpeedBurst | 15 s | No | +30% speed 10 s, stacks 2. Verified. | [S0]; [S30] wiki Chi Torpedo | Medium-High |
| 358267 | Hover | Evoker | SpeedBurst | UNKNOWN (curated 30 s, research "no CD" — conflict) | No | Cast-while-moving window. Verified "Hover". Cooldown left UNKNOWN deliberately. | [S0]; abilities.json; ABILITY_RESEARCH §1 | Low (UNKNOWN) |
| 212552 | Wraith Walk | Death Knight | SpeedBurst | 60 s | No | Phased 4 s reposition. Verified. | [S0]; abilities.json | Medium |
| 1850 | Dash | Druid | SpeedBurst | 120 s (research 120–180 s conflict) | No (cat form) | Verified. | [S0]; abilities.json | Low-Medium |
| 2983 | Sprint | Rogue | SpeedBurst | 60 s | No | Verified. | [S0]; abilities.json | Medium |
| 58875 | Spirit Walk | Shaman | SpeedBurst | 60 s | No | Enh defensive-adjacent. Verified "Spirit Walk". | [S0]; abilities.json | Medium |
| 190784 | Divine Steed | Paladin | SpeedBurst | 45 s | No | 3 s mount sprint. Verified. | [S0]; ABILITY_RESEARCH §1 | Medium-High |
| 192063 | Gust of Wind | Shaman | SpeedBurst | 30 s (curated `cdMs:30` is a unit bug — see §5) | No | Forward gust. Verified. | [S0]; abilities.json | Low-Medium |
| 121536 | Angelic Feather | Priest | SpeedBurst | 60 s | No (ground-targeted) | Place-and-pickup speed. Verified. | [S0]; abilities.json | Medium |
| 101643 | Transcendence | Monk | Teleport | UNKNOWN (place 10 s / Transfer 45 s per wiki — single id covers both) | No (spirit swap) | Swap w/ spirit; Transfer 40 yd/45 s. Verified "Transcendence". Left UNKNOWN deliberately. | [S0]; [S31] wiki Transcendence pages | Low (UNKNOWN) |
| 111771 | Demonic Gateway | Warlock | Teleport | 60 s | No (party gateway) | Recategorized Utility in Cooldown Manager (12.1 PTR). Verified. | [S0]; [S32] nexttier 12.1 notes | Medium |

---

## 4. Utility candidates (MANUAL-BY-DESIGN — 33 rows; every id verified with exact live name)

Purpose vocabulary: CrowdControl | Purge | Threat | Dispel. "Verification" column quotes the exact
`spell-verification.json` name/id match. Cooldown 0 = GCD-only / no cooldown (per wiki infobox "None/Global
Cooldown"). Null = UNKNOWN, deliberately not guessed.

| SpellId | Name | Class | Purpose | CD (s) | Target | Verification (live DB2) | Source | Confidence |
|---|---|---|---|---|---|---|---|---|
| 5246 | Intimidating Shout | Warrior | CrowdControl | 90 | Enemy | 5246 "Intimidating Shout" ✓ | [S0]; [S33] wiki (90 s, up from 60 s) | High |
| 355 | Taunt | Warrior | Threat | 8 | Enemy | 355 "Taunt" ✓ | [S0] (CD from general knowledge) | Low-Medium |
| 853 | Hammer of Justice | Paladin | CrowdControl | 60 | Enemy | 853 "Hammer of Justice" ✓ | [S0] | Medium |
| 20066 | Repentance | Paladin | CrowdControl | 60 | Enemy | 20066 "Repentance" ✓ | [S0]; [S34] wiki (1 min retail row, 6 s duration) | Medium-High |
| 4987 | Cleanse | Paladin | Dispel | 0 | Friendly | 4987 "Cleanse" ✓ | [S0] | Medium |
| 221562 | Asphyxiate | Death Knight | CrowdControl | 45 | Enemy | 221562 "Asphyxiate" ✓ | [S0] | Low-Medium |
| 111673 | Control Undead | Death Knight | CrowdControl | null (UNKNOWN) | Enemy | 111673 "Control Undead" ✓ | [S0] | Low (UNKNOWN CD) |
| 217832 | Imprison | Demon Hunter | CrowdControl | 45 | Enemy | 217832 "Imprison" ✓ | [S0]; [S35] wiki (45 s, demon/beast/humanoid, 1 min) | High |
| 278326 | Consume Magic | Demon Hunter | Purge | 10 | Enemy | 278326 "Consume Magic" ✓ | [S0]; [S36] wiki (10 s, 30 yd, 1 magic effect) | High |
| 33786 | Cyclone | Druid | CrowdControl | 0 | Enemy | 33786 "Cyclone" ✓ | [S0]; [S37] wiki (no CD, 20 yd, 5 s) | High |
| 339 | Entangling Roots | Druid | CrowdControl | 0 | Enemy | 339 "Entangling Roots" ✓ | [S0] | Medium |
| 2908 | Soothe | Druid | Dispel | 0 | Enemy | 2908 "Soothe" ✓ | [S0] | Medium |
| 360806 | Sleep Walk | Evoker | CrowdControl | 30 | Enemy | 360806 "Sleep Walk" ✓ | [S0] | Low-Medium |
| 372048 | Oppressing Roar | Evoker | CrowdControl | 60 | Enemy | 372048 "Oppressing Roar" ✓ | [S0] | Low-Medium |
| 365585 | Expunge | Evoker | Dispel | 8 | Friendly | 365585 "Expunge" ✓ | [S0] | Low-Medium |
| 187650 | Freezing Trap | Hunter | CrowdControl | 30 | Enemy | 187650 "Freezing Trap" ✓ | [S0]; [S38] wiki (30 s, 1 min incapacitate, breaks on damage) | High |
| 19801 | Tranquilizing Shot | Hunter | Purge | 10 | Enemy | 19801 "Tranquilizing Shot" ✓ | [S0]; [S39] wiki (10 s, 40 yd, enrage+magic) | High |
| 118 | Polymorph | Mage | CrowdControl | 0 | Enemy | 118 "Polymorph" ✓ | [S0]; [S40] wiki (no CD, 30 yd) | High |
| 30449 | Spellsteal | Mage | Purge | 0 | Enemy | 30449 "Spellsteal" ✓ | [S0] | Medium |
| 115078 | Paralysis | Monk | CrowdControl | 45 | Enemy | 115078 "Paralysis" ✓ | [S0]; [S41] wiki (45 s, 20 yd, 1 min) | High |
| 218164 | Detox | Monk | Dispel | 8 | Friendly | 218164 "Detox" ✓ | [S0] | Low-Medium |
| 116844 | Ring of Peace | Monk | CrowdControl | null (UNKNOWN) | Enemy | 116844 "Ring of Peace" ✓ | [S0] | Low (UNKNOWN CD) |
| 8122 | Psychic Scream | Priest | CrowdControl | 60 | Enemy | 8122 "Psychic Scream" ✓ | [S0] | Low-Medium |
| 528 | Dispel Magic | Priest | Dispel | 0 | Friendly | 528 "Dispel Magic" ✓ | [S0] | Medium |
| 2094 | Blind | Rogue | CrowdControl | 120 | Enemy | 2094 "Blind" ✓ | [S0] | Medium |
| 6770 | Sap | Rogue | CrowdControl | 0 (needs stealth) | Enemy | 6770 "Sap" ✓ | [S0] | Medium |
| 1776 | Gouge | Rogue | CrowdControl | null (UNKNOWN) | Enemy | 1776 "Gouge" ✓ | [S0] | Low (UNKNOWN CD) |
| 51514 | Hex | Shaman | CrowdControl | 30 | Enemy | 51514 "Hex" ✓ | [S0]; [S42] wiki (30 s, 30 yd, humanoid/beast) | High |
| 370 | Purge | Shaman | Purge | 0 | Enemy | 370 "Purge" ✓ | [S0] | High |
| 8143 | Tremor Totem | Shaman | Dispel | 60 | Friendly (pulse) | 8143 "Tremor Totem" ✓ | [S0]; abilities.json (60 s) | Medium |
| 710 | Banish | Warlock | CrowdControl | 0 (concentration) | Enemy | 710 "Banish" ✓ | [S0] | Medium |
| 5782 | Fear | Warlock | CrowdControl | 0 | Enemy | 5782 "Fear" ✓ | [S0] | Medium |
| 1098 | Subjugate Demon | Warlock | CrowdControl | 0 | Enemy | 1098 "Subjugate Demon" ✓ | [S0] | Medium |

No class lacks a candidate. Dropped (unverifiable, NOT proposed): 61304, 195036, 3355, 207685, 678, 118699,
121783, 122775, 108194, 91807, 91797, 44572, 77222, 204293, 10444, 81262, 119914, 122028, 8412, 98, 770, 186246,
371341, 374226, 23333, 23335, 183790, 19505, 272790, 221883, 205604, 28272, 212182, 281195, 6262 (all MISS in
[S0]). 316099 present but `verified:false`/empty name — dropped. 157153 same — dropped.

---

## 5. Key defensive/research corrections (corrections only, with sources)

1. **Gust of Wind 192063 — data bug in `abilities.json`.** `cdMs: 30` means 30 *milliseconds*; every sibling
   mobility row uses milliseconds (60000 = 60 s). Intended value is almost certainly `30000`. No web source
   confirms the exact 12.1 CD — propose the unit fix only. Source: [S0] + `abilities.json` line 154.
   Confidence: High (bug), Low (true CD value → JSON correction carries the unit fix with a note).
2. **Anti-Magic Shell 48707 — cooldown 60 s → 45 s.** Curated `cdMs:60000`; warcraft.wiki.gg infobox says
   45 s / 5 s duration (duration matches). Source: [S43] wiki Anti-Magic Shell. Confidence: Medium-High.
3. **Blink 1953 — cooldown 25 s → 20 s.** Curated `cdMs:25000`; wiki Midnight row says 20 s. Source: [S29].
   Confidence: Medium (single source; needs in-game confirm). Included in JSON corrections.
4. **Divine Shield 642 / Ice Block 45438 / Vanish 1856 — NO change.** Re-checked: DS 300 s/8 s, IB 240 s/10 s
   match curated rows; Vanish `neverAutomatic` (Escape) is correct behavior and stays. Sources: [S0] verification
   + ABILITY_RESEARCH §2. Confidence: Medium-High.
5. **Vendor↔live id disagreements (do NOT "fix" by editing vendor — vendor is read-only; fix at merge):**
   Spell Lock vendor 19647 → live 119910; Breath of Sindragosa vendor 152279 → live 1249658;
   Schism vendor 214621 → SpellData 424509; Eternity Surge vendor 382411 → SpellData 359073 (also drifted);
   Survival of the Fittest vendor 281195 → SpellData 264735 AND 281195 MISS in live DB2 (both suspect);
   Vendetta 79140 / Rune of Power 116011 / Stampede 201430 / Death Chakram 375891 / Serenity 152173 live only in
   Classic/Mists blocks or absent — treat as removed in 12.1. Source: [S0] + [S1]. Confidence: High.
6. **Crusade 231895 / Void Eruption 228260 / Abomination Limb 383269 — verification-name mismatches:**
   live names are "Avenging Wrath" / "Voidform" / "Graveyard". The abilities exist (verified:true) but any
   name-keyed merge must use the live names. Source: [S0]. Confidence: High.

---

## 6. Sources summary

| # | Site | URL | Date checked | Supported |
|---|---|---|---|---|
| S0 | Local live-client DB2 export | `app/MaxDpsCompanion/Knowledge/spell-verification.json` (this machine) | 2026-09-28 | Every id/name/verified flag; all MISS findings |
| S1 | Local vendor pin (read-only) | `vendor/MaxDps/Cooldowns.lua` (v11.3.49) + `vendor-abilities.json` (368 rows) | 2026-09-28 | All interrupt ids, all Offensive ids, class gaps (Disc/Holy empty) |
| S2 | Liquipedia | `https://liquipedia.net/worldofwarcraft/Interrupt` | 2026-09-28 | Interrupt CD/range table (Pummel/Rebuke/Kick 15 s melee; Disrupt 10 yd; Wind Shear 12 s/30 yd; Counter Shot/Counterspell 24 s/40 yd; Silence 45/30 s) |
| S3 | Blizzard News (official) | `https://news.blizzard.com/en-gb/article/24244455/midnight-pre-expansion-content-update-notes` | 2026-09-28 | Midnight 12.0 PvE lockout buffs for ALL interrupts; 12.1.0 missed-interrupt indicator; Quell 40→20 s; DRW −30 s; Bestial Wrath changes; Metamorphosis (Veng) 2 min |
| S4 | Warcraft Wiki (official-structure docs) | `https://warcraft.wiki.gg/wiki/Patch_12.0.0/Planned_API_changes` | 2026-09-28 | Combat-log removal for addons; enemy spellcast/aura/HP secret; rotation-helper blocking intent |
| S5 | Blizzard via Blue Tracker | `https://www.bluetracker.gg/wow/topic/eu-en/24246290-combat-philosophy-and-addon-disarmament-in-midnight` | 2026-09-28 | "Secret values" combat philosophy (displayable, not knowable) |
| S6 | Wowhead snippet via Tavily (direct fetch blocked) | `https://www.wowhead.com/spell=47528/mind-freeze` | 2026-09-28 | Mind Freeze 15 yd / 15 s / 0 s GCD |
| S7 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Pummel`, `/wiki/Interrupt` (prose) | 2026-09-28 | Pummel 15 s melee 5 s PvE; Mind Freeze 15 yd / Skull Bash 13 yd prose |
| S8 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Quell` | 2026-09-28 | Quell 20 s / 25 yd / 6 s PvE; row-3 Devastation/Augmentation talent |
| S9 | Wowhead snippets via Tavily | `https://www.wowhead.com/spell=351338/quell`, `https://www.wowhead.com/spell=2139/counterspell`, `https://www.wowhead.com/spell=31884/avenging-wrath` | 2026-09-28 | Quell 20 s/25 yd/6 s; Counterspell 25 s snippet (vs 24 s wiki — recorded); AW 2 min/20 s |
| S10 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Spell_Lock` | 2026-09-28 | Spell Lock 24 s / 30 yd / silence 3 s / lockout 6 s; Felhunter pet via Command Demon |
| S11 | Method (12.1 guide) | `https://www.method.gg/guides/fury-warrior/playstyle-and-rotation` | 2026-09-28 | Recklessness/Avatar hold ≤10 s; Anger Management ~45 s effective |
| S12 | (covered by S9) | — | — | — |
| S13 | Timesaver/Icy Veins (12.1) | `https://timesaver.gg/blog/wow-midnight-season-2-retribution-paladin-guide` | 2026-09-28 | 12.1 Ret: Wake of Ashes 30 s, Avenging Wrath ~60 s effective |
| S14 | Method (12.1 guide) | `https://www.method.gg/guides/arcane-mage/playstyle-and-rotation` | 2026-09-28 | Arcane Surge 90 s + Touch of the Magi 45 s ("Big Burn"/"Miniburn"); Ice Cold vs Ice Block |
| S15 | Leprestore (12.1 guide) | `https://leprestore.com/guides/wow/arcane-mage-guide` | 2026-09-28 | Arcane Surge 90 s / Touch 45 s corroboration |
| S16 | Blizzard forum | `https://us.forums.blizzard.com/en/wow/t/berserk-celestial-alignment-and-incarnation-durations-reduced/1835014` | 2026-09-28 | Celestial Alignment 15 s, Incarnation 20 s (TWW cuts) |
| S17 | Icy Veins (12.1) | `https://www.icy-veins.com/wow/balance-druid-pve-dps-rotation-cooldowns-abilities` | 2026-09-28 | Balance 12.1 burst pairing (CA/Incarnation + Force of Nature) |
| S18 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Summon_Infernal` | 2026-09-28 | Summon Infernal 2 min since 11.0.0 (was 3 min) |
| S19 | Wowhead news via Tavily | `https://www.wowhead.com/news/midnight-alpha-development-notes-378688` | 2026-09-28 | Adrenaline Rush 15 s/+75%; Doom Winds summons Nature Feral Spirit; Sundering 30 s |
| S20 | Nexttier (12.1 PTR) | `https://nexttier.pro/guide/wow-midnight-season-2-class-changes` | 2026-09-28 | 12.1 flatter-burst philosophy; Ele Ascendance 75%→30%; Demonic Gateway → Utility |
| S21 | Blizzard News (same as S3, second pass) | `https://news.blizzard.com/en-us/article/24244455/midnight-pre-expansion-content-update-notes` | 2026-09-28 | BM (Bestial Wrath/Bloodshed/Beast Cleave), DRW Insatiable Blade −30 s |
| S22 | Skill-capped (12.1 notes) | `https://www.skill-capped.com/wowarticles/general/patch-12-1-notes` | 2026-09-28 | Wingleader CDR on Deep Breath/Breath of Eons; Devourer tuning (Vengeful Retreat charge fix) |
| S23 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Charge` | 2026-09-28 | Charge 8–25 yd / 15 s classic row (conflict w/ curated 20 s — recorded) |
| S24 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Heroic_Leap` | 2026-09-28 | Heroic Leap 8–40 yd / 45 s |
| S25 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Shadowstep` | 2026-09-28 | Shadowstep 25 yd / 30 s / +70% speed 2 s |
| S26 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Disengage` | 2026-09-28 | Disengage 20 s modern row (25 s classic) |
| S27 | Skill-capped (12.1) | (same article as S22) | 2026-09-28 | Hungering Slash → temporary Vengeful Retreat charge |
| S28 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Demonic_Circle` | 2026-09-28 | Demonic Circle teleport + slow removal (CD conflict recorded) |
| S29 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Blink` | 2026-09-28 | Blink 20 yd / 20 s Midnight row (vs curated 25 s — recorded) |
| S30 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Chi_Torpedo` (+ wowpedia mirror) | 2026-09-28 | Chi Torpedo dash +30% speed 10 s, 2 stacks |
| S31 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Transcendence`, `/wiki/Transcendence:_Transfer` | 2026-09-28 | Transcendence place 10 s / Transfer 40 yd 45 s |
| S32 | (covered by S20) | — | — | — |
| S33 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Intimidating_Shout` | 2026-09-28 | Intimidating Shout 90 s (up from 60 s) |
| S34 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Repentance` | 2026-09-28 | Repentance 1 min / 20 yd / 6 s retail row |
| S35 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Imprison` | 2026-09-28 | Imprison 45 s / 20 yd / 1 min incapacitate |
| S36 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Consume_Magic` | 2026-09-28 | Consume Magic 10 s / 30 yd / 1 magic effect |
| S37 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Cyclone` | 2026-09-28 | Cyclone 20 yd / no CD / 5 s |
| S38 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Freezing_Trap` | 2026-09-28 | Freezing Trap 30 s / 1 min incapacitate, breaks on damage |
| S39 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Tranquilizing_Shot` | 2026-09-28 | Tranquilizing Shot 40 yd / 10 s / enrage+magic |
| S40 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Polymorph` | 2026-09-28 | Polymorph 30 yd / no CD |
| S41 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Paralysis` | 2026-09-28 | Paralysis 45 s / 20 yd / 1 min |
| S42 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Hex` | 2026-09-28 | Hex 30 s / 30 yd / humanoid+beast |
| S43 | Warcraft Wiki | `https://warcraft.wiki.gg/wiki/Anti-Magic_Shell` | 2026-09-28 | AMS 45 s / 5 s / 75% magic absorb |

**Failed sources:** Wowhead direct tooltip/page fetch (404/403 from this machine — used Tavily snippets only);
wago.tools DB2 CSV endpoints (not re-queried; relied on the checked-in `spell-verification.json` export);
`docs/research/ABILITY_RESEARCH.md` §2 rows older than 12.0 treated as legacy (used only for conflict notes).
