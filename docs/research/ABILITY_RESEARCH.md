# Ability Research — Retail Midnight 12.1

## Scope, date, method

- **Scope:** Toolkit facts for a deterministic (non-LLM) C# combat policy engine: defensives,
  offensive cooldowns, interrupts, mobility, self-heals, dispels/utility for all 13 classes / 40 specs.
- **Target game version:** World of Warcraft Retail "Midnight" 12.1 (Interface 120100, live per repo).
- **Date / access date for all web sources:** 2026-09-27.
- **Vendor ground truth:** `vendor/MaxDps` snapshot, core **v11.3.49** (2026-09-21, Interface 120100),
  `Cooldowns.lua` (`classCooldowns`, `classInterrupts`) + retail block of `SpellData.lua`
  (`ns.classSpellData`, lines 41–8220, `IsRetailWow()` branch). Vendor is read-only and authoritative
  for THIS repo version for spell IDs and cooldown categories. Class-module version v11.2.11 could not
  be confirmed from vendor files (no version stamp found) — treated as unverified.
- **Method:** (1) Read `Cooldowns.lua` fully and the retail block of `SpellData.lua` for every
  requested ability name. (2) Web cross-checks via Wowhead spell pages, Warcraft Wiki
  (warcraft.wiki.gg), Icy Veins / Maxroll Midnight (12.x) guides, Blizzard forums. (3) Every
  cooldown/duration/GCD/range value below is either vendor-attested (IDs, categories) or web-sourced;
  patch lineage is stated per row. Values from pre-Midnight sources (10.x Dragonflight, 11.x The War
  Within) are marked **legacy source — verify in game**. Anything unconfirmable is **UNVERIFIED**.
- **NEVER invented IDs:** all numeric IDs come from the vendor tables. A few web IDs are quoted only
  to document vendor drift, and are labeled as such.

**Source keys used in tables:** `[V]` vendor tables · `[W]` Wowhead · `[G]` warcraft.wiki.gg ·
`[IV]` Icy Veins · `[M]` Maxroll · `[B]` Blizzard forums/official · `[R]` HeroRotation-adjacent
research-agent web synthesis (single source — lower confidence).

**GCD shorthand:** `off` = off-GCD (GCD 0 s / category n/a) · `on` = on-GCD · `?` = UNVERIFIED.

---

## v2.6.0 ADDENDUM (2026-09-28): machine-readable research staging

The tables below remain the human research record. The machine-readable
staging for the v2.6.0 ability intelligence registry is
`docs/research/ABILITY_INTELLIGENCE_RESEARCH.md` +
`docs/research/registry-research.json` (12.1 research records, checked
2026-09-28; full tables live there, not duplicated here). The registry
reference is `docs/research/ABILITY_REGISTRY.md`; the generated audit is
`docs/research/ABILITY_REGISTRY_AUDIT.md`.

- Interrupt table pointer: 14 rows, 12.1 kinds (13 Dedicated + Silence for
  Shadow Priest 15487 and Felhunter Spell Lock 119910). Live id 119910
  Replaces vendor 19647 (vendor is read-only; fixed at merge).
- Classification counts: 51 offensive cooldowns classified, 22 mobility
  kinds, 33 manual-by-design utilities (CC/purge/threat/dispel).
- Corrections applied from that pass: Gust of Wind 192063 cdMs 30 -> 30000
  (unit bug: 30 meant 30 ms); Blink 1953 25s -> 20s; Anti-Magic Shell 48707
  60s -> 45s; live Spell Lock 119910 vs vendor 19647; Quell 351338 20s/6s
  vs the stale wiki table (40s/4s); name-keyed merges forbidden - 231895 DB2
  name "Avenging Wrath", 228260 "Voidform", 383269 "Graveyard"
  (id-keyed merges only).

---

## Section 1 — Per-class/spec toolkit summary

Vendor IDs in parentheses. `CD-off`/`CD-def` = vendor `classCooldowns` offensive/defensive lists.

### Warrior — Arms
- Defensives: Die by the Sword (118038), Rallying Cry (97462), Spell Reflection (23920),
  Shield Wall (871, sword+board talents). (CD-def)
- Major offensive: Avatar (107574), Warbreaker (262161). (CD-off)
- Interrupt: Pummel (6552), melee, 15 s. (classInterrupts)
- Mobility/gap-closers: Charge (100), Heroic Leap (6544), Intervene (3411). (SpellData)
- Self-heal/sustain: Impending Victory (202168), Enraged Regeneration (184364). (SpellData)
- Dispels/utility: Shattering Throw (64382, breaks immunities); Piercing Howl (12323, snare).

### Warrior — Fury
- Defensives: Enraged Regeneration (184364), Rallying Cry (97462), Spell Reflection (23920). (CD-def)
- Major offensive: Recklessness (1719), Avatar (107574). (CD-off)
- Interrupt: Pummel (6552). (classInterrupts)
- Mobility: Charge (100), Heroic Leap (6544), Intervene (3411).
- Self-heal: Impending Victory (202168), Enraged Regeneration (184364), Bloodthirst-based sustain.
- Utility: Shattering Throw (64382); Piercing Howl (12323).

### Warrior — Protection
- Defensives: Shield Wall (871), Last Stand (12975), Ignore Pain (190456), Shield Block (2565),
  Rallying Cry (97462), Spell Reflection (23920). (CD-def)
- Major offensive: Avatar (107574), Demoralizing Shout (1160), Shield Charge (385952). (CD-off)
- Interrupt: Pummel (6552). (classInterrupts)
- Mobility: Charge (100), Heroic Leap (6544), Intervene (3411), Shield Charge (385952).
- Self-heal: Impending Victory (202168).
- Utility: Shattering Throw (64382); Demoralizing Shout (1160, AoE AP debuff).

### Paladin — Holy
- Defensives/self: Divine Shield (642), Divine Protection (498), Lay on Hands (633),
  Shield of Vengeance (184662), Aura Mastery (31821). (CD-def)
- Major offensive: Avenging Wrath (31884), Avenging Crusader (216331), Divine Toll (375576). (CD-off)
- Interrupt: Rebuke (96231), melee. (classInterrupts)
- Mobility: Divine Steed (190784).
- Self-heal: Word of Glory (85673), Flash of Light (19750), Holy Prism (114165), Divine Favor (460422).
- Dispels/utility: Cleanse (disease/poison/magic per talents); Blessing of Freedom (1044);
  Blessing of Protection (1022); Blessing of Sacrifice (6940, SpellData only).

### Paladin — Protection
- Defensives: Ardent Defender (31850), Guardian of Ancient Kings (86659), Divine Shield (642),
  Lay on Hands (633), Blessing of Protection (1022), Blessing of Spellwarding (204018),
  Divine Protection (498), Shield of the Righteous (53600). (CD-def)
- Major offensive: Avenging Wrath (31884), Sentinel (389539). (CD-off)
- Interrupt: Rebuke (96231). (classInterrupts)
- Mobility: Divine Steed (190784).
- Self-heal: Word of Glory (85673), Flash of Light (19750).
- Utility: Blessing of Freedom (1044); Hammer of Wrath (24275, execute); Wake of Ashes (255937).

### Paladin — Retribution
- Defensives: Divine Shield (642), Shield of Vengeance (184662), Divine Protection (403876),
  Lay on Hands (633), Blessing of Protection (1022). (CD-def)
- Major offensive: Avenging Wrath (31884), Crusade (231895). (CD-off)
- Interrupt: Rebuke (96231). (classInterrupts)
- Mobility: Divine Steed (190784).
- Self-heal: Word of Glory (85673), Flash of Light (19750).
- Utility: Freedom (1044); Final Reckoning (343721), Execution Sentence (343527), Wake of Ashes (255937).

### Death Knight — Blood
- Defensives: Vampiric Blood (55233), Icebound Fortitude (48792), Rune Tap (194679),
  Anti-Magic Shell (48707), Anti-Magic Zone (51052), Lichborne (49039), Death Pact (48743),
  Tombstone (219809). (CD-def)
- Major offensive: Empower Rune Weapon (47568), Abomination Limb (383269, CD table only),
  Blooddrinker (206931), Bonestorm (194844). (CD-off)
- Interrupt: Mind Freeze (47528). (classInterrupts)
- Mobility: Death's Advance (48265 Blood / 124285 others), Wraith Walk (212552), Death Grip (49576).
- Self-heal: Death Strike (49998), Vampiric Blood (55233), Death Pact (48743).
- Utility: Raise Dead (46585); Dancing Rune Weapon (49028), Army of the Dead (42650) in SpellData.

### Death Knight — Frost
- Defensives: Icebound Fortitude (48792), Anti-Magic Shell (48707), Lichborne (49039),
  Anti-Magic Zone (51052), Death Pact (48743). (CD-def)
- Major offensive: Empower Rune Weapon (47568), Breath of Sindragosa (1249658),
  Abomination Limb (383269, CD table only). (CD-off)
- Interrupt: Mind Freeze (47528). (classInterrupts)
- Mobility: Death's Advance (124285), Wraith Walk (212552), Death Grip (49576).
- Self-heal: Death Strike (49998), Death Pact (48743).
- Utility: Pillar of Frost (51271), Frostwyrm's Fury (279302), Glacial Advance (194913) in SpellData.

### Death Knight — Unholy
- Defensives: same five as Frost (48792/48707/49039/51052/48743). (CD-def)
- Major offensive: Unholy Assault (207289), Summon Gargoyle (49206),
  Abomination Limb (383269, CD table only), Vile Contagion — see §2 (absent), Soul Reaper (343294). (CD-off)
- Interrupt: Mind Freeze (47528). (classInterrupts)
- Mobility: Death's Advance (124285), Wraith Walk (212552), Death Grip (49576).
- Self-heal: Death Strike (49998), Death Pact (48743).
- Utility: Dark Transformation (63560), Army of the Dead (42650), Raise Dead (46584).

### Demon Hunter — Devourer (NEW in Midnight; SpellData retail block has NO Devourer section)
- Defensives: Blur (198589), Darkness (196718), Netherwalk (196555). (CD-def, Devourer block)
- Major offensive: Void Metamorphosis (1217605, resourceless — 50 Soul Fragments, 35 w/ talent),
  Collapsing Star (1221150), The Hunt (1246167). (CD-off, Devourer block; IDs Cooldowns-only)
- Interrupt: Disrupt (183752). (classInterrupts)
- Mobility: Fel Rush (195072), Vengeful Retreat (198793) in SpellData (Havoc/Vengeance rows).
- Self-heal/sustain: soul-fragment consumption; spec is new — details UNVERIFIED, verify in game.
- Utility: Sigil of Misery (207684) in SpellData.

### Demon Hunter — Havoc
- Defensives: Blur (198589), Darkness (196718), Netherwalk (196555). (CD-def)
- Major offensive: Metamorphosis (191427), Essence Break (258860), Fel Barrage (258925),
  The Hunt (370965), Glaive Tempest (342817). (CD-off)
- Interrupt: Disrupt (183752). (classInterrupts)
- Mobility: Fel Rush (195072), Vengeful Retreat (198793). (SpellData)
- Self-heal: Darkness-adjacent avoidance; Shattered Restoration-type talents (verify).
- Utility: Sigil of Misery (207684), Soul Carver (207407), Sigil of Flame (204596) in SpellData.

### Demon Hunter — Vengeance
- Defensives: Demon Spikes (203720), Fiery Brand (204021), Darkness (196718),
  Fel Devastation (212084, hybrid), Soul Barrier (263648), Bulk Extraction (320341). (CD-def)
- Major offensive: Metamorphosis (187827), Fel Devastation (212084), The Hunt (370965). (CD-off)
- Interrupt: Disrupt (183752). (classInterrupts)
- Mobility: Fel Rush (195072), Vengeful Retreat (198793).
- Self-heal: Fel Devastation (212084), Bulk Extraction (320341), Soul Barrier (263648, absorb).
- Utility: Sigils (Flame 204596 / Misery 207684); Soul Carver (207407).

### Druid — Balance
- Defensives: Barkskin (22812), Renewal (108238), Ironbark (102342). (CD-def)
- Major offensive: Celestial Alignment (194223), Incarnation: Chosen of Elune (102560). (CD-off)
- Interrupt: Skull Bash (106839). (classInterrupts)
- Mobility: Dash (1850), Stampeding Roar (106898), Wild Charge (102401). (SpellData)
- Self-heal: Renewal (108238), Frenzied Regeneration (22842, SpellData).
- Utility: Typhoon (132469, knockback); Innervate (29166); Nature's Swiftness (132158, SpellData).

### Druid — Feral
- Defensives: Barkskin (22812), Survival Instincts (61336), Renewal (108238). (CD-def)
- Major offensive: Berserk (106951), Incarnation: Avatar of Ashamane (102543),
  Tiger's Fury (5217), Berserk: Frenzy (343223). (CD-off)
- Interrupt: Skull Bash (106839). (classInterrupts)
- Mobility: Dash (1850), Stampeding Roar (106898), Wild Charge (102401).
- Self-heal: Renewal (108238), Frenzied Regeneration (22842).
- Utility: Typhoon (132469); Skull Bash charges in DnD-shape utility.

### Druid — Guardian
- Defensives: Barkskin (22812), Survival Instincts (61336), Ironfur (192081),
  Frenzied Regeneration (22842), Renewal (108238). (CD-def)
- Major offensive: Incarnation: Guardian of Ursoc (102558), Berserk (50334),
  Rage of the Sleeper (200851). (CD-off)
- Interrupt: Skull Bash (106839). (classInterrupts)
- Mobility: Dash (1850), Stampeding Roar (106898), Wild Charge (102401).
- Self-heal: Frenzied Regeneration (22842), Renewal (108238).
- Utility: Typhoon (132469); Ironfur (192081, stacking armor).

### Druid — Restoration
- Defensives: Ironbark (102342), Barkskin (22812), Renewal (108238). (CD-def)
- Major offensive/healing: Convoke the Spirits (391528), Flourish (197721),
  Incarnation: Tree of Life (33891), Tranquility (740). (CD-off)
- Interrupt: Skull Bash (106839). (classInterrupts)
- Mobility: Dash (1850), Stampeding Roar (106898), Wild Charge (102401).
- Self-heal: Renewal (108238), Frenzied Regeneration (22842).
- Utility: Innervate (29166); Typhoon (132469).

### Evoker — Devastation
- Defensives: Obsidian Scales (363916), Renewing Blaze (374348), Zephyr (374227),
  Verdant Embrace (360995). (CD-def)
- Major offensive: Dragonrage (375087), Deep Breath (357210), Fire Breath (382266),
  Tip the Scales (370553), Time Skip (404977). (CD-off)
- Interrupt: Quell (351338). (classInterrupts)
- Mobility: Hover (358267). (SpellData)
- Self-heal: Renewing Blaze (374348), Verdant Embrace (360995), Living Flame (361469), Emerald Communion (370960).
- Utility: Rescue (370665); Eternity Surge (359073), Ebon Might (395152) in SpellData.

### Evoker — Preservation
- Defensives: Rewind (363534), Time Dilation (357170), Obsidian Scales (363916),
  Renewing Blaze (374348), Zephyr (374227), Verdant Embrace (360995),
  Emerald Communion (370960). (CD-def)
- Major offensive/healing: Tip the Scales (370553), Deep Breath (357210),
  Fire Breath (382266), Eternity Surge (382411* — SpellData has 359073; ID drift, see §2). (CD-off)
- Interrupt: Quell (351338). (classInterrupts)
- Mobility: Hover (358267).
- Self-heal: same as Devastation + Rewind (363534), Time Dilation (357170).
- Utility: Rescue (370665); Stasis (370537), Temporal Anomaly (373861) in SpellData.

### Evoker — Augmentation
- Defensives: Obsidian Scales (363916), Renewing Blaze (374348), Zephyr (374227),
  Verdant Embrace (360995). (CD-def; note Defy Fate 404195 in SpellData, absent from CD-def)
- Major offensive: Breath of Eons (403631), Tip the Scales (370553), Eternity Surge (359073). (CD-off)
- Interrupt: Quell (351338). (classInterrupts)
- Mobility: Hover (358267).
- Self-heal: Renewing Blaze (374348), Verdant Embrace (360995), Living Flame (361469).
- Utility: Rescue (370665); Ebon Might (395152), Defy Fate (404195, cheat-death).

### Hunter — Beast Mastery
- Defensives: Exhilaration (109304), Aspect of the Turtle (186265),
  Fortitude of the Bear (388035, CD table only), Survival of the Fittest (281195 CD table;
  SpellData has 264735 — ID drift, see §2). (CD-def)
- Major offensive: Call of the Wild (359844), Aspect of the Wild — see §2 (absent),
  Stampede (201430 CD table; SpellData retail absent), Bloodshed (321530). (CD-off)
- Interrupt: Counter Shot (147362). (classInterrupts)
- Mobility: Aspect of the Cheetah (186257), Disengage (781), Harpoon (190925). (SpellData)
- Self-heal: Exhilaration (109304); Mend Pet (136, Classic/Mists blocks only — see §2).
- Utility: Roar of Sacrifice (53480), Camouflage (199483), Feign Death (5384) in SpellData.

### Hunter — Marksmanship
- Defensives: Exhilaration (109304), Aspect of the Turtle (186265),
  Survival of the Fittest (281195 CD table / 264735 SpellData), Camouflage (199483). (CD-def)
- Major offensive: Trueshot (288613), Death Chakram (375891 CD table only),
  Moonlight Chakram (1264902 CD table only). (CD-off)
- Interrupt: Counter Shot (147362). (classInterrupts)
- Mobility: Cheetah (186257), Disengage (781), Harpoon (190925).
- Self-heal: Exhilaration (109304).
- Utility: Roar of Sacrifice (53480); Volley (260243), Sentinel (450369) in SpellData.

### Hunter — Survival
- Defensives: Exhilaration (109304), Aspect of the Turtle (186265),
  Survival of the Fittest (281195/264735), Camouflage (199483). (CD-def)
- Major offensive: Coordinated Assault (360952), Death Chakram (375891 CD table only),
  Stampede (201430 CD table only), Takedown (1250646 CD table only),
  Moonlight Chakram (1264902 CD table only). (CD-off)
- Interrupt: Muzzle (187707). (classInterrupts)
- Mobility: Cheetah (186257), Disengage (781), Harpoon (190925, core gap-closer here).
- Self-heal: Exhilaration (109304).
- Utility: Roar of Sacrifice (53480); Sentinel (450369) in SpellData.

### Mage — Arcane
- Defensives: Prismatic Barrier (235450), Ice Block (45438), Mirror Image (55342),
  Alter Time (342245), Greater Invisibility (110959). (CD-def)
- Major offensive: Arcane Surge (365350), Evocation (12051). (CD-off)
- Interrupt: Counterspell (2139). (classInterrupts)
- Mobility: Blink (1953), Shimmer (212653), Displacement (389713, SpellData; legacy — verify). (SpellData)
- Self-heal: Alter Time (342245, rewind), Ice Cold (414659, SpellData; values UNVERIFIED).
- Utility: Mass Barrier (414660); Touch of the Magi (321507) in SpellData.

### Mage — Fire
- Defensives: Blazing Barrier (235313), Ice Block (45438), Alter Time (342245). (CD-def)
- Major offensive: Combustion (190319), Rune of Power — see §2 (absent). (CD-off)
- Interrupt: Counterspell (2139). (classInterrupts)
- Mobility: Blink (1953), Shimmer (212653), Displacement (389713, verify).
- Self-heal: Cauterize (86949, passive cheat-death), Alter Time (342245), Ice Cold (414659).
- Utility: Phoenix Flames (257541) in SpellData.

### Mage — Frost
- Defensives: Ice Barrier (11426), Ice Block (45438), Cold Snap (235219), Alter Time (342245). (CD-def)
- Major offensive: Icy Veins (12472). (CD-off)
- Interrupt: Counterspell (2139). (classInterrupts)
- Mobility: Blink (1953), Shimmer (212653), Displacement (389713, verify).
- Self-heal: Alter Time (342245), Ice Cold (414659), Cold Snap (235219, resets Barriers/Block).
- Utility: Frozen Orb (84714), Ray of Frost (205021), Comet Storm (153595) in SpellData.

### Monk — Brewmaster
- Defensives: Fortifying Brew (115203), Dampen Harm (122278), Diffuse Magic (122783),
  Celestial Brew (322507), Purifying Brew (119582), Zen Meditation — see §2 (absent),
  Expel Harm (322101), Invoke Niuzao (132578). (CD-def)
- Major offensive: Weapons of Order (387184), Bonedust Brew — see §2 (absent),
  Exploding Keg (325153, SpellData). (CD-off)
- Interrupt: Spear Hand Strike (116705). (classInterrupts)
- Mobility: Roll (109132), Chi Torpedo (115008), Tiger's Lust (116841), Transcendence (101643).
- Self-heal: Expel Harm (322101), Celestial Brew (322507, absorb), Vivify (116670).
- Utility: Transcendence: Transfer (101643); Midnight: Diffuse Magic folded into Fort Brew (see §2).

### Monk — Mistweaver
- Defensives: Life Cocoon (116849), Fortifying Brew (243435 MW variant),
  Dampen Harm (122278), Diffuse Magic (122783), Revival (115310), Expel Harm (322101). (CD-def)
- Major offensive/healing: Weapons of Order (387184), Chi-Ji (325197 Invoke),
  Yu'lon (322118) — SpellData has Yulon's Knowledge/Grace/Whisper, exact 322118 UNVERIFIED,
  Bonedust Brew (absent). (CD-off)
- Interrupt: Spear Hand Strike (116705). (classInterrupts)
- Mobility: Roll (109132), Chi Torpedo (115008), Tiger's Lust (116841), Transcendence (101643).
- Self-heal: Expel Harm (322101), Vivify (116670), Renewing Mist (115151), Celestial Conduit (443028).
- Utility: Life Cocoon (116849, external); Revival (115310, raid dispel+heal).

### Monk — Windwalker
- Defensives: Touch of Karma (122470), Fortifying Brew (243435 WW variant),
  Dampen Harm (122278), Diffuse Magic (122783), Expel Harm (322101). (CD-def)
- Major offensive: Serenity — see §2 (exact name absent; TeaofSerenity 393460 exists),
  Weapons of Order (387184), Invoke Xuen (123904), Bonedust Brew (absent),
  Zenith — see §2 (absent). (CD-off)
- Interrupt: Spear Hand Strike (116705). (classInterrupts)
- Mobility: Roll (109132), Chi Torpedo (115008), Tiger's Lust (116841), Transcendence (101643).
- Self-heal: Expel Harm (322101), Touch of Karma (122470, redirect+absorb), Vivify (116670).
- Utility: Storm, Earth, and Fire (137639), Dance of Chi-Ji (325201) in SpellData.

### Priest — Discipline
- Defensives: Pain Suppression (33206), Power Word: Barrier (62618),
  Desperate Prayer (19236), Fade (586), Power Word: Life (373481). (CD-def)
- Major offensive: Schism (424509 SpellData / 214621 CD table — drift, see §2),
  Mindbender (123040), Shadowfiend (34433), Power Infusion (10060). (CD-off)
- Interrupt: none (classInterrupts empty for Discipline). (V)
- Mobility: Angelic Feather (121536), Body and Soul (64129, SpellData).
- Self-heal: Desperate Prayer (19236), Power Word: Shield (17), Penance (47540, atonement).
- Utility: Rapture — see §2 (priest Rapture absent; only warlock Malefic Rapture);
  Leap of Faith (73325), Symbol of Hope (64901), Divine Word (372760) in SpellData.

### Priest — Holy
- Defensives: Guardian Spirit (47788), Holy Word: Serenity (2050), Holy Word: Sanctify (34861),
  Desperate Prayer (19236), Symbol of Hope (64901), Fade (586). (CD-def)
- Major offensive/healing: Holy Word: Chastise (88625), Divine Word (372760),
  Shadowfiend (34433), Power Infusion (10060). (CD-off)
- Interrupt: none (classInterrupts empty for Holy). (V)
- Mobility: Angelic Feather (121536), Body and Soul (64129).
- Self-heal: Desperate Prayer (19236), Flash Heal / Heal (2060) in SpellData.
- Utility: Leap of Faith (73325); Divine Hymn (64843), Halo (120517) in SpellData.

### Priest — Shadow
- Defensives: Dispersion (47585), Desperate Prayer (19236), Fade (586),
  Vampiric Embrace (15286). (CD-def)
- Major offensive: Void Eruption (228260), Dark Ascension (391109), Power Infusion (10060),
  Mindbender (200174), Shadowfiend (34433), Dark Evangelism (391099 SpellData; CD table 391112 — drift). (CD-off)
- Interrupt: Silence (15487). (classInterrupts)
- Mobility: Angelic Feather (121536), Body and Soul (64129).
- Self-heal: Dispersion (47585), Vampiric Embrace (15286), Devouring Plague-adjacent (verify).
- Utility: Psychic Scream (8122); Leap of Faith (73325); Void Torrent (263165) in SpellData.

### Rogue — Assassination
- Defensives: Evasion (5277), Feint (1966), Cloak of Shadows (31224),
  Crimson Vial (185311), Vanish (1856), Smoke Bomb (212182 CD table; SpellData has Smoke 441247). (CD-def)
- Major offensive: Vendetta — see §2 (Classic/Mists blocks only, 79140),
  Deathmark (360194), Sepsis — see §2 (absent), Thistle Tea (381623). (CD-off)
- Interrupt: Kick (1766). (classInterrupts)
- Mobility: Sprint (2983), Shadowstep (36554, SpellData), Grappling Hook (195457, SpellData).
- Self-heal: Crimson Vial (185311).
- Utility: Gouge (1776, incap); Cheat Death (31230, passive) in SpellData.

### Rogue — Outlaw
- Defensives: same five + Smoke as Assassination. (CD-def)
- Major offensive: Adrenaline Rush (13750), Roll the Bones (315508),
  Thistle Tea (381623), Shadowstep (36554, listed offensive in CD table). (CD-off)
- Interrupt: Kick (1766). (classInterrupts)
- Mobility: Sprint (2983), Grappling Hook (195457), Shadowstep (36554).
- Self-heal: Crimson Vial (185311).
- Utility: Gouge (1776); Between the Eyes (315341), Killing Spree (51690),
  Blade Flurry (13877), Cold Blood (382245), Kingsbane (385627) in SpellData.

### Rogue — Subtlety
- Defensives: same five + Smoke as Assassination. (CD-def)
- Major offensive: Shadow Blades (121471), Thistle Tea (381623), Shadowstep (36554). (CD-off)
- Interrupt: Kick (1766). (classInterrupts)
- Mobility: Sprint (2983), Shadowstep (36554), Grappling Hook (195457).
- Self-heal: Crimson Vial (185311).
- Utility: Gouge (1776); Shadow Dance (185313), Symbols of Death (212283),
  Secret Technique (280719) in SpellData.

### Shaman — Elemental
- Defensives: Astral Shift (108271), Earth Elemental (198103),
  Healing Stream Totem (5394), Healing Tide Totem (108280). (CD-def)
- Major offensive: Ascendance (114050), Echoing Shock — see §2 (absent; SpellData has Echo 364343,
  evoker). (CD-off; Stormkeeper 191634 / Fire Ele 198067 / Storm Ele 192249 in SpellData, commented out of CD)
- Interrupt: Wind Shear (57994), 12 s — shortest standard interrupt. (classInterrupts)
- Mobility: Ghost Wolf (2645), Spiritwalker's Grace (79206, SpellData), Gust of Wind (192063, SpellData).
- Self-heal: Healing Surge (8004), Chain Heal (1064), Healing Stream Totem (5394).
- Utility: Thunderstorm (51490), Capacitor Totem (192058), Tremor Totem (8143),
  Stone Bulwark Totem (108270), Nature's Guardian (30884) in SpellData; Grounding Totem absent.

### Shaman — Enhancement
- Defensives: Astral Shift (108271), Earth Elemental (198103),
  Healing Stream Totem (5394), Spirit Walk (58875). (CD-def)
- Major offensive: Doom Winds (384352), Ascendance (114051). (CD-off;
  Feral Spirit 51533, Sundering 197214, Primordial Wave 375982 in SpellData)
- Interrupt: Wind Shear (57994). (classInterrupts)
- Mobility: Ghost Wolf (2645), Spirit Walk (58875), Gust of Wind (192063).
- Self-heal: Healing Surge (8004), Chain Heal (1064).
- Utility: Capacitor (192058), Tremor (8143), Crash-Lightning-adjacent AoE (verify).

### Shaman — Restoration
- Defensives: Spirit Link Totem (98008, hybrid), Healing Tide Totem (108280),
  Earth Elemental (198103), Astral Shift (108271). (CD-def)
- Major offensive/healing: Cloudburst Totem (157153), Primordial Wave (375982),
  Ascendance (114052), Spirit Link Totem (98008). (CD-off)
- Interrupt: Wind Shear (57994). (classInterrupts)
- Mobility: Ghost Wolf (2645), Spiritwalker's Grace (79206, SpellData), Gust of Wind (192063).
- Self-heal: Healing Surge (8004), Chain Heal (1064), Ancestral Guidance — see §2 (absent).
- Utility: Mana Tide (1217525 ManaTide in SpellData), Ancestral Protection Totem (207399) in SpellData.

### Warlock — Affliction
- Defensives: Unending Resolve (104773), Dark Pact (108416), Mortal Coil (6789),
  Healthstone (6262 CD table; SpellData has CreateHealthstone 6201), Drain Life (234153). (CD-def)
- Major offensive: Summon Darkglare (205180), Soulburn (385899). (CD-off;
  Soul Rot 386997, Vile Taint 278350, Phantom Singularity 205179, Haunt 48181 in SpellData)
- Interrupt: Spell Lock (19647, felhunter pet). (classInterrupts; SpellData has SpellLock 119910 — drift)
- Mobility: Burning Rush (111400), Demonic Circle: Teleport (48020, SpellData),
  Demonic Gateway (111771, SpellData).
- Self-heal: Drain Life (234153), Healthstone, Soul Leech (108370, SpellData), Fel Armor (386124).
- Utility: Havoc (80240), Fear-adjacent CC (verify); Fel Domination (333889) in SpellData.

### Warlock — Demonology
- Defensives: same five as Affliction. (CD-def)
- Major offensive: Summon Demonic Tyrant (265187), Nether Portal — see §2 (absent),
  Soulburn (385899), Implosion (196277), Summon Doomguard (1276672 CD table only),
  Grimoire: Imp Lord (1276452 CD table only), Grimoire: Fel Ravager (1276467 CD table only). (CD-off)
- Interrupt: Spell Lock (19647). (classInterrupts)
- Mobility: Burning Rush (111400), Demonic Circle: Teleport (48020), Demonic Gateway (111771).
- Self-heal: Drain Life (234153), Healthstone, Soul Leech (108370).
- Utility: Power Siphon (264130), Demonic Strength (267171), Grimoire: Felguard (111898) in SpellData.

### Warlock — Destruction
- Defensives: same five as Affliction. (CD-def)
- Major offensive: Summon Infernal (1122), Soulburn (385899). (CD-off;
  Cataclysm 152108, Soul Fire 6353, Channel Demonfire 196447 in SpellData)
- Interrupt: Spell Lock (19647). (classInterrupts)
- Mobility: Burning Rush (111400), Demonic Circle: Teleport (48020), Demonic Gateway (111771).
- Self-heal: Drain Life (234153), Healthstone, Soul Leech (108370).
- Utility: Havoc (80240), Chaos Bolt (116858); Darkglare (205180), Malevolence (442726) in SpellData.

---

## Section 2 — Deep-dive ability table

Column key: **CD** cooldown · **Dur** duration/buff · **GCD** off/on/? · **Use** use-now conditions ·
**Hold** do-not-fire conditions · **Emerg** emergency-fire conditions · **Conf** confidence.
Patch tag: `[12]` Midnight-12.x verified · `[11]` TWW 11.x (legacy — verify) · `[10]` older (legacy).

### Warrior

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 23920 | Spell Reflection | All | Reflect | 25 s | 5 s | off | self | enemy caster mid-cast; predictable magic burst | no spell incoming; physical-only damage | big magic hit <2 s out | [V][W][R] | High |
| 871 | Shield Wall | Arms/Prot (tal) | MajDR | 240 s (some src 180 s — conflict) | 8 s (some src 12 s — conflict) | off | self | telegraphed tank-buster / raid-wide | trivial damage; fight nearly over | HP <30% + damage inc | [V][R] | Low |
| 118038 | Die by the Sword | All (Arms) | MajDR | 180 s | 8 s | off | self | melee burst window; parry value high | casters only; kiting (no parry) | HP <35% melee inc | [V][W][R] | Medium |
| 2565 | Shield Block | Prot (all in V) | MinDR | 2 chg, ~6 s recharge (verify) | 6 s | off | melee | upkeep vs melee; rage available | magic-only damage; rage starved | stacking melee hits | [V][R] | Low |
| 97462 | Rallying Cry | All | RaidDR+HP | 180 s | 10 s | off | self, 40 yd | raid-wide burst; pre-pull HP buffer | solo, no party benefit; others' rally active | group HP <40% AoE | [V][W][R] | Medium |
| 202168 | Impending Victory | All | SelfHeal | 25 s | instant | off | melee | HP 50–80% + GCD free; lethal setup soon | HP full; burst CDs rolling (waste) | HP <40% + no healer | [V][W][R] | Medium |
| 184364 | Enraged Regeneration | All (Fury) | SelfHeal | 120 s | 8 s | off | self | sustained rot damage; enrage synergy | HP full; one-shot mechanics (won't save) | HP <50% + damage cont | [V][W][R] | Medium |
| 1719 | Recklessness | Fury (all in V) | MajOff | 90 s | 12 s | off | self | burst window + target uptime | target about to die/immune; moving, no uptime | execute race; lust overlap | [V][W][R] | Medium |
| 107574 | Avatar | All | MajOff | 90 s (some src 180 s — conflict) | 20 s (some src 24 s — conflict) | off | self | burst + root/snare break needed | CC'd full duration; downtime transit | burn phase | [V][R] | Low |
| 227847 | Bladestorm | All | MajOff | 60–90 s by spec (verify) | 6 s | on | self/PBAoE | AoE burst + slow break; devastation window | single target w/o adds; must move/cancel | swarmed + no escape | [V][W][R] | Low |
| 228920 | Ravager | Arms (all in V) | MinOff | 90 s (verify) | ~12 s (verify) | on | ground 40 yd? (verify) | stacked AoE at location | mobile targets; single target | adds pile + CDs ready | [V][R] | Low |
| 100 | Charge | All | GapClose | 20 s | — | on | 8–25 yd, target | opener; re-engage; root break (tal) | target in melee; path blocked; snare-field landing | fleeing kill target | [V][W][R] | High |
| 6544 | Heroic Leap | All | Move | 45 s | — | off | 8–40 yd ground | gap/escape; reposition over hazard | mid-air lock danger; tiny gain | knockback/void-zone escape | [V][W][R] | High |
| 3411 | Intervene | All | Move/External | 30 s (verify) | 6 s? (verify) | off | 25 yd ally | peel to ally; intercept + DR (tal) | no ally in range; ally safe already | ally focused, you mobile | [V][R] | Low |
| 6552 | Pummel | All | Interrupt | 15 s | 4 s lockout | off | melee | priority cast <1 s; healer casts | non-critical casts; lockout DR | lethal heal/cast must stop | [V][W][R] | High |
| 64382 | Shattering Throw | All | Utility | UNVERIFIED (long) | — | on? (verify) | ~30 yd (verify) | immune-shield burn phase | no immunity present; DPS loss window | boss immunity = wipe | [V][W][R] | UNVERIFIED |

### Paladin

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 642 | Divine Shield | All | Immunity | 300 s | 8 s | on [11] (verify 12) | self | lethal mechanic; debuff clear | Forbearance active; DPS uptime loss fatal | certain death <3 s | [V][W][R] | Medium |
| 1022 | Blessing of Protection | All | Immunity (phys) | 300 s | 10 s | on [11] (verify) | 40 yd ally | ally physical burst; bleed clear | Forbearance on target; magic-only hit | ally <20% phys inc | [V][W][R] | Medium |
| 1044 | Blessing of Freedom | All | Dispel (move) | 25 s | 8 s | on [11] (verify) | 40 yd | rooted ally/self; kite window | no impair; dispel-protect needed | rooted in lethal | [V][W][R] | Medium |
| 6940 | Blessing of Sacrifice | All (SpellData; CD-def Holy commented) | External | 120 s | 12 s | off [11] (verify) | 40 yd | tank/ally sustained burst | self HP <30% (transfer kills) | ally dying, you safe | [V][W][R] | Medium |
| 86659 | Guardian of Ancient Kings | All (Prot) | MajDR | 180 s [11] (verify) | 8 s | off | self/30 yd | tank-buster; big magic+phys | small hits; overlapping wall | HP <30% any inc | [V][W][R] | Medium |
| 31850 | Ardent Defender | All (Prot) | MajDR+CheatDeath | 90–120 s (verify) | 8–12 s (verify) | off | self | lethal-hit insurance window | HP full + no spike | HP <25% spike inc | [V][W][R] | Low |
| 633 | Lay on Hands | All | EmergHeal | 600 s | instant | off | 40 yd | ally/self <15% dying | Forbearance on target; HP >50% | any death <2 s | [V][W][R] | High |
| 85673 | Word of Glory | All | SelfHeal | HP spender | instant | on | 40 yd/self | 3+ HP + HP <80% | HP full; EF/SotR priority | HP <40% + HP banked | [V][W][R] | High |
| 19750 | Flash of Light | All | SelfHeal | cast time | — | on | 40 yd | Infusion proc; steady top-up | moving; locked out Holy | OOM-healer cover | [V][W][R] | High |
| 53600 | Shield of the Righteous | Prot (Holy 415091) | MinDR | HP spender | ~3–4.5 s armor | on | melee | active-mitigation upkeep | HP needed for WoG emerg | melee burst + HP banked | [V][W][R] | Medium |
| 31884 | Avenging Wrath | All | MajOff | 120 s | 20 s | off | self | burst + uptime; lust stack | downtime; fight ends pre-value | burn/execute phase | [V][W][R] | Medium |
| 375576 | Divine Toll | All (Holy) | MajOff | 60 s | instant | on | 30 yd | multi-target holy-shock burst | single, no value; silence risk | burst check + CDs up | [V][W][R] | Medium |
| 96231 | Rebuke | All | Interrupt | 15 s | 4–5 s lockout (verify) | off | melee | priority cast | non-critical; lockout DR | lethal cast must stop | [V][W][R] | High |
| 190784 | Divine Steed | All | Move | 45 s | 3 s | off | self | reposition; kite; carry flag | tiny distance; mounted already | escape lethal ground | [V][W][R] | High |

### Death Knight

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 48792 | Icebound Fortitude | All | MajDR | 180 s | 8 s [12] (was 12 s [11]) | off | self | stun + burst overlap; DR window | no damage; stun-immune already | stunned + HP falling | [V][G][R] | Medium |
| 48707 | Anti-Magic Shell | All | Absorb (magic) | 45–60 s (conflict) | 5 s | off | self | magic burst; debuff-block walk | physical-only; RP waste | magic one-shot inc | [V][G][R] | Low |
| 51052 | Anti-Magic Zone | All | RaidDR (magic) | 240 s? (verify) | 6–10 s? (verify) | off | ground 30 yd | raid magic burst | spread group; phys-only | group magic lethal | [V][G][R] | Low |
| 55233 | Vampiric Blood | All (Blood) | SelfHeal+HP | 90 s | 10 s | off | self | pre-burst HP buffer; healer cover | HP full; no inc | HP <50% + inc | [V][G][R] | Medium |
| 194679 | Rune Tap | All (Blood) | MinDR | 25 s | 4 s | off | self | frequent small DR; rune avail | no damage; rune starve | HP dip + melee inc | [V][G][R] | Medium |
| 49039 | Lichborne | All | Utility/CC-break | 120 s | 10 s | off | self | fear/charm/sleep break + leech | no CC threat; healer CC'd (no self-heal) | chained CC + dying | [V][G][R] | Medium |
| 48743 | Death Pact | All | EmergHeal | 120 s | instant (50%+; verify) | off | self | HP <35% no healer | HP >60%; absorb-shield follow | HP <25% any state | [V][G][R] | Medium |
| 219809 | Tombstone | Blood (all in V) | Absorb | 60 s | 8 s | off | self | pre-burst shield from wounds | no wounds banked | burst + wounds ready | [V][G][R] | Medium |
| 49998 | Death Strike | All | SelfHeal | RP cost, no CD | instant | on | melee | RP banked + damage taken pool | RP needed for burst; HP full | HP <50% + RP avail | [V][G][R] | High |
| 194878 | Icy Talons | All | Passive | passive (proc) | — | — | self | N/A (talent passive) | N/A | N/A | [V] | High |
| 51271 | Pillar of Frost | Frost (all in V) | MajOff | 60 s | 12 s | off | self | burst window + Breath stack | downtime; no RP bank | burn phase + adds | [V][G][R] | Medium |
| 1249658 | Breath of Sindragosa | Frost (all in V) | MajOff | 90 s (verify; web 152279) | RP-drain channel | on? (verify) | frontal cone | RP capped + uptime | low RP; movement fight | execute cleave | [V][G][R] | Low |
| 49206 | Summon Gargoyle | Unholy (all in V) | MajOff | 180 s (verify) | ~25–30 s (verify) | off | 30 yd | burst + army window | target dying; heavy move | burn phase | [V][G][R] | Low |
| 47528 | Mind Freeze | All | Interrupt | 15 s | 3–4 s lockout (verify) | on?/off? (conflict) | 15 yd melee | priority cast | non-critical casts | lethal cast | [V][G][R] | Medium |
| 124285 | Death's Advance | Frost/Unh (Blood 48265) | Move | 45 s (verify; may be passive [12]) | 8–10 s | off | self | slow/root wall; kite grip | no impair; gap already closed | rooted in lethal | [V][G][R] | Low |
| 212552 | Wraith Walk | All | Move | 60 s (verify) | 4 s | off | self | phased reposition; break pathing | tiny distance; may be talent-replaced | escape + DA down | [V][G][R] | Low |
| 49576 | Death Grip | All | GapClose | 15–25 s (conflict) | taunt/grip | on | 30 yd | caster/kiter pull-in; add gather | boss-immune; grip breaks CC | healer attacked | [V][G][R] | Low |

### Demon Hunter

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 198589 | Blur | Hav/Dev (Veng SpellData) | MajDR | 60 s | 10 s | off | self | dodge physical burst; aggro dump-ish | magic-only; warranted later | HP <40% melee inc | [V][W][R] | Medium |
| 196718 | Darkness | All | RaidDR | 180 s | 8 s | off | self/ground | raid burst; party stack | spread group; solo trivial | group lethal <5 s | [V][W][R] | Medium |
| 196555 | Netherwalk | Hav/Dev | Immunity | 180 s (verify) | 5–6 s (verify) | off | self | lethal soak; debuff immune | DPS-loss window; Blur suffices | certain death | [V][W] | Low |
| — | Glimpse | — | — | not present in vendor v11.3.49 tables (only DivineGlimpse 387805, paladin) | — | — | — | — | — | — | [V] | UNVERIFIED |
| 191427 | Metamorphosis | Havoc | MajOff | 240 s (verify) | 30 s (verify) | off | self/leap | burst + uptime; AoE pack | downtime; fight ends | burn phase | [V][W][R] | Low |
| 1217605 | Void Metamorphosis | Devourer | MajOff | NONE — 50 fragments (35 w/ talent); drains Fury [12] | Fury-dependent | off? (verify) | self | fragments capped; Hunt-smuggle ready | <50 frags; forced downtime soon | N/A (builder-gated) | [V][W] | Medium |
| 370965 | The Hunt (Hav/Veng) | Hav/Veng | MinOff+GapClose | 90 s | charge+DoT | on? (verify) | 25–50 yd charge (verify) | burst + engage; AoE (Dev) | no uptime after; path blocked | execute dash kill | [V][W] | Low |
| 1246167 | The Hunt (Dev) | Devourer | MinOff | 90 s? (verify) | as above | ? | as above | on CD in AoE (do NOT hold for Meta) | single + nothing else | adds + Meta near | [V][W] | Low |
| 212084 | Fel Devastation | Veng (all in V) | SelfHeal+Off | 60 s (verify) | 2–3 s channel | on? (verify) | frontal | HP low + pack stacked | HP full; must move | HP <40% + targets | [V][W][R] | Low |
| 204021 | Fiery Brand | Veng (Hav 320962) | MinDR+External | 60 s (verify) | 8–10 s (verify) | off | 30 yd? (verify) | tank target / focus damage | target dying; brand overwrite | focus-target burst | [V][W][R] | Low |
| 203720 | Demon Spikes | Veng (all in V) | MinDR | 2 chg ~20 s (verify) | 6 s | off | self | upkeep vs melee; pain avail | magic-only; pain starve | melee burst + pain | [V][W][R] | Low |
| 183752 | Disrupt | All | Interrupt | 15 s (verify) | 3–4 s (verify) | off | 10–15 yd (verify) | priority cast in range | out of range; trivial cast | lethal cast | [V][W][R] | Medium |
| 195072 | Fel Rush | Hav/Veng | Move | 2 chg ~10 s (verify) | — | on? (verify) | short dash | engage/escape; VRT combo | cliff/void landing; capped sigil-synergy loss | escape lethal | [V][W] | Low |
| 198793 | Vengeful Retreat | Hav/Veng | Escape | 25 s (verify) | — | ? | back-dash | disengage + momentum (tal) | corner trap; no follow-up | melee train escape | [V][W] | Low |
| 207684 | Sigil of Misery | Hav/Veng | CC | 60–90 s (verify) | ~30 s fear? (verify) | on? (verify) | ground 30 yd | add control; stop casts | CC-break risk; boss immune | overwhelmed by adds | [V][W] | Low |

### Druid

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 61336 | Survival Instincts | Feral/Guard (+all V) | MajDR | 180 s (2 chg Guard? verify) | 6 s | off | self | 50% DR lethal window | trivial damage; Bear suffices | HP <30% any inc | [V][W][R] | Medium |
| 22812 | Barkskin | All | MinDR | 60 s | 8–12 s (verify) | off | self | frequent DR; usable shifted/stunned? (verify) | overlap w/ SI; no inc | HP dip + cont damage | [V][W][R] | Medium |
| 102342 | Ironbark | Bal/Resto (+all V) | External | 60–90 s (verify) | 12 s (verify) | off? (verify) | 40 yd | tank/ally burst | target safe; self needier | ally <25% burst | [V][W][R] | Low |
| 22842 | Frenzied Regeneration | All (Guard) | SelfHeal | no CD (rage; verify) | ~3 s HoT | off? (verify) | self | damage-taken pool banked | rage needed for Ironfur; HP full | HP <50% + rage | [V][W][R] | Low |
| 132158 | Nature's Swiftness | All (druid) | Utility | 60 s | next-cast | off | self | instant clone/cyclone/regrowth | no cast need; locked Nature | instant CC/heal saves | [V][W][R] | Medium |
| 29166 | Innervate | All | External (mana) | 180 s | 10 s | off | 40 yd | healer mana burn phase | mana stable; self needier | healer OOM + burst | [V][W][R] | Medium |
| 102560 | Incarnation: Chosen of Elune | Balance | MajOff | 180 s | 30 s | off | self | burst + eclipse alignment | movement/downtime | burn phase | [V][W][R] | Medium |
| 106951 | Berserk / 502 ards vary | Feral 106951; Guard 50334 | MajOff | 180 s | 15–20 s (verify) | off | self | burst + bleed window | target dying; CC chain | execute + lust | [V][W][R] | Low |
| 391528 | Convoke the Spirits | All (Resto) | MajOff/Heal | 120 s | 4 s channel | on? (verify) | 40 yd? | burst + shapeshift cover | pushback/los risk; kicked = loss | burn + safe spot | [V][W][R] | Low |
| 194223 | Celestial Alignment | Bal (all in V) | MajOff | 180 s | 20 s (verify) | off | self | burst + Incarn alternate | CA/Incarn overlap; downtime | burn phase | [V][W][R] | Low |
| 106839 | Skull Bash | All | Interrupt | 15 s | 4 s? (verify) | off | 13 yd melee | priority cast; charge-interrupt | trivial casts; form-locked | lethal cast | [V][W][R] | Medium |
| 1850 | Dash | All | Move | 120–180 s (verify) | 15 s? (verify) | off | self (cat) | kite/chase; flag run | tiny gain; Stampede up | escape lethal | [V][W][R] | Low |
| 106898 | Stampeding Roar | All | Move (group) | 120–180 s (verify) | 8–15 s (verify) | off? (verify) | group | group move/retreat; kiting | solo; snare-immune need | group escape | [V][W][R] | Low |
| 102401 | Wild Charge | All | GapClose | 15 s (verify) | — | ? | 8–25 yd (verify) | form-based engage/retreat | wrong form; path blocked | chase/escape | [V][W][R] | Low |
| 132469 | Typhoon | All | CC/knockback | 30 s | knockback+slow | on? (verify) | frontal | add knock-off; interrupt mass | position ruins tank; immune | overwhelmed | [V][W][R] | Medium |

### Evoker

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 363916 | Obsidian Scales | All | MajDR | 90–150 s (verify) | 12 s (verify) | off | self | 30% DR burst window | trivial; Renewing suffices | HP <35% any inc | [V][W] | Low |
| 374348 | Renewing Blaze | All | SelfHeal | 120 s? (verify) | 8 s (100% taken healed back) | off | self | sustained rot; big hit lands | HP full; one-shot mech | HP <50% + cont | [V][W][B] | Medium |
| 357170 | Time Dilation | All (Pres) | External/Absorb | 120 s? (verify) | ~6 s + Leech? (verify) | off? (verify) | 40 yd? | ally burst redirect | target safe; self needier | ally dying | [V][W] | Low |
| 404195 | Defy Fate | Aug (SpellData) | CheatDeath | passive (verify) | — | — | self | N/A (cheat-death talent) | N/A | auto-saves lethal | [V][W] | Low |
| 370960 | Emerald Communion | All (Pres) | SelfHeal | 180 s? (verify) | channel (verify) | on? (verify) | self | big self-top + hover-move | interrupt risk; HP ok | HP <40% + safe | [V][W] | Low |
| 374227 | Zephyr | All | RaidDR+Move | 120 s? (verify) | 8 s? (verify) | off? (verify) | group | group move + DR; gale cover | solo; no move need | group lethal + move phase | [V][W] | Low |
| 370665 | Rescue | All | Utility | 60–90 s? (verify) | reposition pair | off? (verify) | 25–40 yd ally? (verify) | save ally from mechanic | no danger; disorient risk | ally stuck in lethal | [V][W] | Low |
| 357210 | Deep Breath | All (Dev/Pres) | MajOff | 60–120 s? (verify) | ~2 s flight | on? (verify) | line AoE | stacked AoE burst | single; movement loss | adds + safe flight | [V][W] | Low |
| 370553 | Tip the Scales | All | Utility | 120 s? (verify) | next empower | off | self | instant full-empower burst | no empower ready; waste | burst + empower up | [V][W] | Low |
| 351338 | Quell | All | Interrupt | 40 s? (verify; longer than melee) | 3–4 s (verify) | off | 25–30 yd (verify) | priority ranged cast | trivial; melee can cover | lethal ranged cast | [V][W] | Low |
| 358267 | Hover | All | Move | no CD (verify) | 8–10 s? (verify) | off? (verify) | self | cast-while-moving window | no move need; dismount risk | move + cast burst | [V][W] | Low |
| 360995 | Verdant Embrace | All | SelfHeal | cast/mana (verify) | instant + HoT? | on | 40 yd/self | ally/self top-up; movement heal | OOM; interrupt bait | ally <30% + mobile | [V][W] | Low |
| 361469 | Living Flame (self) | All | SelfHeal | cast (verify) | — | on | 25–40 yd | filler + self-heal (tal) | burst-priority target | sustain chip + low HP | [V][W] | Low |

### Hunter

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 186265 | Aspect of the Turtle | All | Immunity | 180 s | 8 s | off | self | lethal soak; reflect-shield walk | DPS-loss window; Blur-type suffices | certain death | [V][W] | Medium |
| 281195 | Survival of the Fittest | All (CD table; SpellData 264735 — drift, use vendor CD value) | MinDR | 120–180 s (verify) | 6 s (verify) | off | self | 20% DR frequent window | trivial; Turtle needed later | HP dip + cont | [V][W] | Low |
| 109304 | Exhilaration | All | SelfHeal | 120 s | 30%+ (verify) | off | self | HP 40–70% rot damage | HP full; one-shot mech | HP <50% + no healer | [V][W] | Medium |
| — | Mend Pet | — | — | NOT in retail vendor block (MendPet 136 lives in Cata/Mists blocks only) | — | — | — | — | — | — | [V] | UNVERIFIED |
| 53480 | Roar of Sacrifice | All (SpellData) | External | 60 s (verify) | 12 s (verify) | off? (verify) | 40 yd (pet) | ally burst redirect (pet) | pet dead/dismissed; self needier | ally focused | [V][W] | Low |
| 147362 | Counter Shot | BM/MM | Interrupt | 15 s (verify; 24 s w/o talent — verify) | 3 s (verify) | off | 40 yd | priority ranged cast | trivial; kick-DR overlap | lethal cast at range | [V][W] | Medium |
| 187707 | Muzzle | Survival | Interrupt | 15 s | 3 s (verify) | off | melee | priority melee cast | trivial casts | lethal cast | [V][W] | Medium |
| 186257 | Aspect of the Cheetah | All | Move | 180 s | 4–8 s? (verify) | off | self | engage/escape sprint | daze risk in combat; tiny gain | flee/chase + root-free | [V][W] | Low |
| 781 | Disengage | All | Escape | 20 s | leap back | off? (verify) | self | knockback-escape; gap open | cliff/void behind; melee uptime loss | lethal ground + melee train | [V][W] | Medium |
| 190925 | Harpoon | All (Surv core) | GapClose | 30 s | root? (verify) | on? (verify) | 8–30 yd? | engage + root (tal) | target melee; path blocked | fleeing kill target | [V][W] | Low |
| — | Death Chakram | — | — | 375891 in Cooldowns.lua but NOT in retail SpellData block | — | — | — | — | — | — | [V] | UNVERIFIED |
| 360952 | Coordinated Assault | Surv (all in V) | MajOff | 120 s | 20 s | off | self/pet | burst + pet synergy | pet dead; downtime | burn + adds | [V][W] | Low |
| 288613 | Trueshot | MM (all in V) | MajOff | 120 s | 15–18 s (verify) | off | self | burst + aimed-window | movement-heavy; no uptime | burn phase | [V][W] | Low |
| 19574 | Bestial Wrath | BM (all in V) | MajOff | 90 s | 15 s (verify) | off | self/pet | burst + pet burst stack | pet CC'd/dead; downtime | burn + lust | [V][W] | Low |

### Mage

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 45438 | Ice Block | All | Immunity | 240 s | 10 s | off | self | lethal soak; debuff clear | DPS-loss; Hypothermia active | certain death | [V][W] | Medium |
| 110959 | Greater Invisibility | All (Arc) | MajDR+Threat | 120 s | 3 s? (verify) | off | self | threat drop + DR window | trivial; invis breaks plan | aggro + burst | [V][W] | Low |
| 55342 | Mirror Image | All (Arc) | MinDR+Threat | 120 s | 40 s? (verify) | off? (verify) | self | threat redirect; images tank | solo trivial; burst needier | adds + aggro | [V][W] | Low |
| 235313 | Blazing Barrier | Fire (+all V) | Absorb | 25 s (verify) | 60 s/15 s? (verify) | off? (verify) | self | upkeep absorb; Blazing Soul synergy | no damage; bigger wall ready | HP dip + melee inc | [V][W] | Low |
| 11426 | Ice Barrier | Frost (+all V) | Absorb | 25 s (verify) | 60 s/15 s? (verify) | off? (verify) | self | upkeep absorb pre-burst | no damage; Block needed | HP dip + cont | [V][W] | Low |
| 235450 | Prismatic Barrier | Arc (+all V) | Absorb | 25 s (verify) | 60 s? (verify) | off? (verify) | self | upkeep magic+phys absorb | as above | HP dip + burst | [V][W] | Low |
| 414660 | Mass Barrier | All (SpellData) | RaidAbsorb | 180 s? (verify) | ~10 s? (verify) | ? | group | group burst buffer | spread; solo | group lethal | [V] | UNVERIFIED |
| 342245 | Alter Time | All | SelfHeal/rewind | 60 s | 10 s (verify) | off | self | pre-burst rewind anchor | no damage; movement lock | HP crash = snapback | [V][W] | Medium |
| — | Temporal Shield | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| — | Arcane Power | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| 190319 | Combustion | Fire (+all V) | MajOff | 90–120 s (verify) | 10–12 s (verify) | off | self | crit-window burst + FB chain | downtime; no-stacking | burn phase | [V][W] | Low |
| 12472 | Icy Veins | Frost (+all V) | MajOff | 120–180 s (verify) | 20–25 s (verify) | off | self | haste-window burst + orb | movement-heavy; downtime | burn phase + lust | [V][W] | Low |
| 365350 | Arcane Surge | Arc (+all V) | MajOff | 90 s | 15 s? (verify) | off? (verify) | self | mana-burn burst window | low mana; downtime | burn phase | [V][W] | Low |
| — | Power Infusion (mage ref) | — | — | priest spell (10060); mage row = cross-ref only | — | — | — | — | — | — | [V] | Medium |
| 2139 | Counterspell | All | Interrupt | 24 s | 6 s lockout (verify) | off | 40 yd | priority ranged cast | trivial; DR overlap | lethal cast at range | [V][W] | High |
| 1953 | Blink | All | Move | 15 s | — | off | self/20 yd | escape/snare-break; reposition | path blocked; Shimmer need | lethal ground + root | [V][W] | High |
| 212653 | Shimmer | All | Move | 2 chg? (verify) | — | off | self/20 yd | cast-while-moving blink | no move need; Blink suffices | move + cast burst | [V][W] | Low |
| 389713 | Displacement | All (SpellData; legacy — verify 12) | Move | UNVERIFIED (legacy talent) | — | ? | self | return-to-anchor plays | removed/changed in 12; tiny gain | escape setup | [V] | UNVERIFIED |
| 414659 | Ice Cold | All (SpellData) | MajDR? | UNVERIFIED (values unknown — verify) | ? | ? | self | hypothermia-window DR (verify) | Block available; no inc | HP crash + Block down | [V] | UNVERIFIED |
| 86949 | Cauterize | All (SpellData) | CheatDeath | passive proc (verify) | DoT after (verify) | — | self | N/A (auto-saves lethal fire) | N/A | auto — heal through DoT | [V][W] | Low |
| 235219 | Cold Snap | Frost (+all V) | Utility | 300 s? (verify) | resets Block/Barriers | off? (verify) | self | second Block/barrier window | Block unused; no need | back-to-back lethal | [V][W] | Low |

### Monk

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 115203 | Fortifying Brew (Brew) | Brew | MajDR | 360 s base; ~half w/ Keg/Palm CDR (Brews) | 15 s? (verify) | off | self | tank-buster; big DR+HP | trivial; Niuzao covers | HP <35% + burst | [V][IV] | Medium |
| 243435 | Fortifying Brew (MW/WW) | MW/WW | MinDR | 90–180 s? (verify) | 15 s? (verify) | off | self | DR window + Diffuse rider [12] | trivial; Karma/Cocoon better | HP dip + cont | [V][IV][M] | Low |
| 122783 | Diffuse Magic | All (CD-def; [12] folded into Fort Brew as choice node) | MinDR (magic) | 90 s [11]; [12] = Fort-Brew rider (verify) | 6 s [11] (verify) | off | self | magic burst; dot-redirect back [12] | physical-only; Fort holding | magic burst + debuffs | [V][W][M] | Low |
| 122278 | Dampen Harm | All (CD-def) | MinDR | 120 s [11] (verify 12) | 10 s? (verify) | off | self | 3 big hits incoming | trivial rot; Diffuse better (magic) | HP spike train | [V][W] | Low |
| — | Guard | — | — | not present in vendor v11.3.49 tables (only Vanguard/InspiredGuard) | — | — | — | — | — | — | [V] | UNVERIFIED |
| 322101 | Expel Harm | All (CD-def) | SelfHeal | 30 s? (verify) | instant + orb (verify) | on? (verify) | self/melee | HP dip + orbs ready | HP full; burst-priority GCD | HP <50% + orbs | [V][W] | Low |
| 116670 | Vivify | All | SelfHeal | cast/mana | — | on | 40 yd | steady top-up; cleave heal | OOM; moving (no instant) | ally/self <50% | [V][W] | Medium |
| 115151 | Renewing Mist | All (MW core) | SelfHeal/HoT | 9 s? (verify) | ~20 s HoT (verify) | on | 40 yd | upkeep HoT; Vivify synergy | no damage; mana burn | spread damage | [V][W] | Low |
| 116849 | Life Cocoon | MW (all in V) | External/Absorb | 120 s | ~12 s absorb (verify) | off? (verify) | 40 yd | ally/self lethal buffer | target safe; Revival better | ally <20% burst | [V][W][M] | Medium |
| 122470 | Touch of Karma | WW (all in V) | Absorb+Off | 90 s | 6–10 s (verify) | off | self/melee | redirect burst + burst window | no damage; CC-full duration | HP crash + boss hitting | [V][W] | Low |
| 123904 | Invoke Xuen | WW (all in V) | MajOff | 120 s | 20 s? (verify) | off? (verify) | pet | burst + cleave add | target dying; heavy move | burn + adds | [V][W] | Low |
| 137639 | Storm, Earth, and Fire | All (WW off commented) | MajOff | 90–120 s (verify) | 15–20 s (verify) | off? (verify) | self | burst + split burst | Serenity chosen; downtime | burn phase | [V][W] | Low |
| — | Serenity | — | — | exact name absent (TeaofSerenity 393460 exists) | — | — | — | — | — | — | [V] | UNVERIFIED |
| 443028 | Celestial Conduit | All (SpellData; MW CD-off has 325197/322118 instead) | MajOff/Heal | 90 s [12] (verify) | ~8 s channel, recastable (verify) | on? (verify) | allies | group burst heal + move-channel | interrupt risk; CDs better | group crash + safe | [V][M] | Low |
| 116705 | Spear Hand Strike | All | Interrupt | 15 s | 4 s (verify) | off | melee | priority cast; silence rider (tal) | trivial; DR overlap | lethal cast | [V][W] | Medium |
| 109132 | Roll | All | Move | 2 chg ~20 s (verify) | — | off | self | short reposition; Chi Torp alt | cliff/void; tiny gain | escape + root-free | [V][W] | Low |
| 115008 | Chi Torpedo | All | Move | 2 chg ~25 s (verify) | heal+move? (verify) | off? (verify) | self | heal-move combo; group push | Roll suffices; no heal need | escape + chip | [V][W] | Low |
| 116841 | Tiger's Lust | All | Move/Dispel | 30 s | 6 s (verify) | off | 40 yd/self | root/snare clear + sprint | no impair; Freedom-type better | rooted in lethal | [V][W] | Medium |
| 101643 | Transcendence | All | Move | 45 s? (verify) | 15 min anchor (verify) | off? (verify) | 40 yd anchor | pre-place + swap escapes | anchor in bad spot; no setup | lethal + anchor safe | [V][W] | Low |

### Priest

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 19236 | Desperate Prayer | All | SelfHeal | 90 s | 30%+ (verify) | off | self | HP <60% rot; no healer | HP full; bigger CD ready | HP <40% + damage | [V][W] | Medium |
| 586 | Fade | All | Threat/MinDR | 30 s | 10 s? (verify) | off | self | threat drop + DR (tal) | tanking; no aggro | aggro + burst | [V][W] | Medium |
| 47585 | Dispersion | Shadow (+all V) | MajDR | 120 s | 6 s | off | self | 75% DR + snare-clear window | must DPS full duration; trivial | HP <30% any inc | [V][W] | Medium |
| 17 | Power Word: Shield | All | Absorb | 7.5 s | 15 s | on | 40 yd | upkeep absorb; Body-and-Soul sprint | weakened-soul; mana burn | HP dip + burst | [V][W] | High |
| 33206 | Pain Suppression | All (Disc) | External | 180 s | 8 s | off | 40 yd | tank/ally lethal 40% DR | target safe; self needier | ally <25% burst | [V][W] | Medium |
| 47788 | Guardian Spirit | All (Holy) | External/CheatDeath | 180 s | 10 s | off | 40 yd | lethal-insurance on tank/ally | target safe; no spike | ally dying + spike | [V][W] | Medium |
| 64843 | Divine Hymn | All (SpellData; Holy CD-def has Symbol of Hope instead) | RaidHeal | 180 s | 8 s channel (verify) | on? (verify) | group | group crash heal | interrupt risk; CDs cover | group <40% + safe | [V][W] | Low |
| — | Rapture (priest) | — | — | priest Rapture NOT in vendor (only warlock MaleficRapture 324536) | — | — | — | — | — | — | [V] | UNVERIFIED |
| 200183 | Apotheosis | All (SpellData) | MajOff/Heal | 120 s? (verify) | 20 s? (verify) | off? (verify) | self | holy-word burst window | no words ready; downtime | burn + words banked | [V][W] | Low |
| 421453 | Ultimate Penitence | All (SpellData) | MajOff/Heal | 120 s? (verify) | channel (verify) | on? (verify) | 40 yd? | burst + atonement dump | movement/interrupt risk | burn + safe | [V][W] | Low |
| 228260 | Void Eruption | Shadow (+all V) | MajOff | 90–120 s (verify) | insanity window | off? (verify) | self/AoE | burst + voidform entry | downtime; Dark Asc chosen | burn phase | [V][W] | Low |
| 391109 | Dark Ascension | Shadow (+all V) | MajOff | 120 s | 20 s? (verify) | off? (verify) | self | burst + insanity gen | Eruption chosen; downtime | burn phase | [V][W] | Low |
| 120517 | Halo | Disc/Holy (Shadow 120644) | MinOff/Heal | 40 s? (verify) | nova | on? (verify) | 30 yd nova | stacked-group burst | spread; pull risk | group crash + stacked | [V][W] | Low |
| 8122 | Psychic Scream | All | CC | 60 s? (verify) | 8 s fear (verify) | on? (verify) | PBAoE | add peel; mass interrupt | break risk; fear-immune | overwhelmed | [V][W] | Low |
| 15487 | Silence | Shadow (+all V) | Interrupt | 45 s | 5 s (verify) | off? (verify) | 30 yd | ranged-priority cast stop | trivial; kick-DR overlap | lethal ranged cast | [V][W] | Medium |
| 73325 | Leap of Faith | All | Utility | 90–150 s (verify) | grip ally | off? (verify) | 40 yd | save ally from mechanic | grip breaks plan; no danger | ally stuck in lethal | [V][W] | Low |
| 121536 | Angelic Feather | All | Move | 20 s? (verify) | 8 s? (verify) | on? (verify) | ground/self | sprint setup; ally taxi | no move need; feather waste | kite/escape + path | [V][W] | Low |
| 64129 | Body and Soul | All | Move | passive (verify) | sprint on shield | — | self | shield-sprint synergy | no shield; no move | kite + shield up | [V][W] | Low |

### Rogue

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 31224 | Cloak of Shadows | All | Immunity (magic) | 120 s | 5 s | off | self | magic burst + spell-clear | physical-only; Vanish better | magic lethal <3 s | [V][W] | High |
| 5277 | Evasion | All | MajDR (dodge) | 120 s | 10 s | off | self | melee burst dodge window | casters-only; Cloak better | HP <40% melee inc | [V][W] | Medium |
| 1966 | Feint | All | MinDR | 15 s? (verify) | 6 s (verify) | off? (verify) | self | frequent AoE DR upkeep | energy starve; no AoE | AoE burst + energy | [V][W] | Low |
| 185311 | Crimson Vial | All | SelfHeal | 30 s | 30%+ over 4 s? (verify) | off? (verify) | self | HP dip + energy float | HP full; burst-GCD need | HP <50% + no healer | [V][W] | Low |
| 31230 | Cheat Death | All (SpellData; CD-def commented) | CheatDeath | passive proc (verify) | — | — | self | N/A (auto-saves lethal) | N/A | auto — follow with Cloak | [V][W] | Medium |
| 1856 | Vanish | All | Threat/Escape | 120 s | 3 s stealth (verify) | off | self | threat drop; stealth-opener reset |-trivial; Subterfuge waste | aggro + lethal | [V][W] | Medium |
| 441247 | Smoke (Bomb) | Out/Sub CD 212182 (SpellData Smoke 441247) | Utility | 180 s? (verify) | ~8 s cloud? (verify) | ? | ground 30 yd? | ally peel; cast-cover | solo; no ally value | ally focused | [V][W] | Low |
| — | Deterrence | — | — | not present in vendor v11.3.49 tables (hunter legacy name) | — | — | — | — | — | — | [V] | UNVERIFIED |
| 13877 | Blade Flurry | Outlaw (+all V) | MinOff | no CD (toggle; verify) | while active | on? (verify) | cleave | 2+ targets cleave upkeep | single target; energy loss | adds + uptime | [V][W] | Low |
| 13750 | Adrenaline Rush | Outlaw (+all V) | MajOff | 180 s | 20 s | off | self | burst + energy flood | downtime; energy capped waste | burn + lust | [V][W] | Medium |
| 121471 | Shadow Blades | Sub (+all V) | MajOff | 180 s | ~12–20 s (verify) | off | self/melee | burst + combo-gen | downtime; target dying | burn phase | [V][W] | Low |
| 360194 | Deathmark | Assn (+all V) | MajOff | 120 s? (verify) | ~16 s? (verify) | off? (verify) | melee? | burst + dot-copy window (assn) | target dying fast | burn + bleeds rolling | [V][W] | Low |
| 185313 | Shadow Dance | All (Sub core; CD-off commented) | MajOff | no CD (stealth-gated; verify) | ~4–8 s (verify) | off? (verify) | self | stealth-window burst | no stealth access; waste | burn + dance ready | [V][W] | Low |
| 212283 | Symbols of Death | All (Sub core; CD-off commented) | MinOff | 30–45 s? (verify) | ~10–12 s (verify) | off? (verify) | self | upkeep damage amp | downtime; Dance clash | burn + upkeep down | [V][W] | Low |
| 1766 | Kick | All | Interrupt | 15 s | 5 s (verify) | off | melee | priority cast | trivial; DR overlap | lethal cast | [V][W] | High |
| 2983 | Sprint | All | Move | 60 s | 8 s | off | self | sprint engage/escape | root (can't fix); tiny gain | chase/flee + path | [V][W] | Medium |
| 36554 | Shadowstep | Assn/Sub (+Out CD-off) | GapClose | 30 s | — | off | 25 yd target | engage + behind-target | target melee; path blocked | fleeing kill target | [V][W] | Medium |
| 195457 | Grappling Hook | All (SpellData) | Move | 30–60 s? (verify) | — | ? | 40 yd ground (verify) | Outlaw gap/escape | hook-path blocked; Step better | escape + Step down | [V][W] | Low |
| 1776 | Gouge | All | CC | 15 s? (verify) | ~4–6 s incap (verify) | on? (verify) | melee | single-add stop; cast peel | breaks on damage; boss immune | healer attacked | [V][W] | Low |

### Shaman

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 108271 | Astral Shift | All | MinDR | 90 s | 8 s (verify) | off | self | 40% DR frequent window | trivial; SLT/HTT better | HP dip + burst | [V][W] | Medium |
| 198103 | Earth Elemental | All | External/tank | 300 s | 60 s (verify) | on? (verify) | 30 yd? | tank-add + wall (tal) | trivial; boss-immune taunt | tank down + adds | [V][W] | Low |
| 108270 | Stone Bulwark Totem | All (SpellData) | Absorb | 180 s? (verify) | ~10 s shield (verify) | on? (verify) | self/20 yd | pre-burst shield upkeep | no damage; SLT better | HP dip + burst | [V][W] | Low |
| 30884 | Nature's Guardian | All (SpellData) | CheatDeath+HP | passive proc (verify) | — | — | self | N/A (auto-save + HP) | N/A | auto — follow w/ Shift | [V][W] | Low |
| — | Burrow | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| 5394 | Healing Stream Totem | All | SelfHeal/HoT | 30 s | 15 s (verify) | on? (verify) | 40 yd group | upkeep group trickle | no damage; Tide better | group chip + stacked | [V][W] | Medium |
| 8004 | Healing Surge | All | SelfHeal | cast/mana | — | on | 40 yd | Maelstrom-instant (Enh); top-up | OOM; pushback zone | self/ally <50% | [V][W] | Medium |
| 1064 | Chain Heal (self) | All | SelfHeal | cast/mana | — | on | 40 yd jump | stacked-group heal | spread; single better | group crash + stacked | [V][W] | Medium |
| — | Ancestral Guidance | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| 114050 | Ascendance (Ele) | Ele | MajOff | 180 s | 15 s | off | self | burst + lava-window | movement; downtime | burn phase | [V][W] | Medium |
| 114051 | Ascendance (Enh) | Enh | MajOff | 180 s | 15 s | off | self | burst + stormstrike-window | downtime; wolves clash | burn + wolves | [V][W] | Medium |
| — | Elemental Mastery | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| 198067 | Fire Elemental | All (SpellData; CD-off commented) | MajOff | 150–300 s (verify) | 30–60 s (verify) | on? (verify) | pet | burst + sustained pet | target dying; ele-despawn move | burn + uptime | [V][W] | Low |
| 192249 | Storm Elemental | All (SpellData; CD-off commented) | MajOff | 150–300 s (verify) | 30–60 s (verify) | on? (verify) | pet | burst + ranged pet | as above | burn + uptime | [V][W] | Low |
| 51533 | Feral Spirit | Enh (+all V) | MajOff | 120 s | 15 s (verify) | off? (verify) | wolves | burst + maelstrom gen | downtime; Asc clash | burn + Asc stack | [V][W] | Medium |
| 384352 | Doom Winds | Enh (+all V) | MajOff | 60–90 s (verify) | ~8–12 s (verify) | off? (verify) | self | windfury-window burst | no uptime; wolves down | burn + wolves up | [V][W] | Low |
| 57994 | Wind Shear | All | Interrupt | 12 s | 3 s (verify) | off | 30 yd | priority ranged cast (shortest) | trivial; DR overlap | lethal cast at range | [V][W] | High |
| 2645 | Ghost Wolf | All | Move | no CD (form) | while active | off? (verify) | self | travel/kiting form | DPS-form need; snare stays | escape + no cast | [V][W] | Medium |
| 58875 | Spirit Walk | Enh (+all V) | Move | 60–120 s (verify) | ~6–8 s? (verify) | off | self | root-break + sprint | no impair; GW suffices | rooted in lethal | [V][W] | Low |
| 192063 | Gust of Wind | All (SpellData) | Move | 30–60 s? (verify) | leap forward | off? (verify) | self | gap/escape leap | cliff/void; GW better | escape + GW down | [V][W] | Low |
| 51490 | Thunderstorm | All | CC/knockback | 30–45 s? (verify) | knockback+slow | on? (verify) | PBAoE | add knock-off; mass stop | position ruins; immune | overwhelmed | [V][W] | Low |
| 192058 | Capacitor Totem | All | CC/stun | 60 s? (verify) | 3–5 s stun (verify) | on? (verify) | ground | add stun setup | stun-DR; boss immune | adds + cast-cover | [V][W] | Low |
| 8143 | Tremor Totem | All | Dispel (fear) | 60 s? (verify) | 10 s pulse (verify) | on? (verify) | 30 yd group | fear/charm/sleep pulse-clear | no fear threat | feared group + lethal | [V][W] | Medium |
| — | Grounding Totem | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |

### Warlock

| ID | Name | Specs | Purpose | CD | Dur | GCD | Range | Use | Hold | Emerg | Src | Conf |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 104773 | Unending Resolve | All | MajDR | 180 s | 8 s | off | self | 40–50% DR + interrupt-immune | trivial; Dark Pact better | HP <35% + burst | [V][IV] | Medium |
| 108416 | Dark Pact | All | Absorb | 60 s | 20 s (verify) | off | self | frequent shield upkeep | HP full; UR needed later | HP dip + burst | [V][IV] | Medium |
| 6789 | Mortal Coil | All | CC+SelfHeal | 45 s | 3 s horror+fear-path (verify) | on? (verify) | 20 yd | single-stop + heal (tal) | breaks plan; boss immune | healer attacked + HP low | [V][W] | Low |
| 48020 | Demonic Circle: Teleport | All (SpellData) | Move | 30 s (verify) | anchor 15 min (verify) | off? (verify) | 40 yd anchor | pre-place + blink escape | anchor bad; no setup | lethal + anchor safe | [V][W] | Medium |
| 385899 | Soulburn | All | Utility | 30–60 s? (verify) | empowers demon (verify) | off? (verify) | self/pet | instant-summon burst setup | no shards; waste window | burn + shards ready | [V][IV] | Low |
| 6262 | Healthstone (use) | All (CD 6262; SpellData Create 6201) | SelfHeal | 120 s shared? (verify) | 35%+ (verify) | off? (verify) | self | HP <50% + no healer | HP high; potion clash | HP <30% any state | [V][W] | Medium |
| 234153 | Drain Life | All | SelfHeal | channel/mana | while channeled | on | 30 yd enemy | HP low + safe channel | interrupt zone; burst-GCD need | HP <40% + cover | [V][W] | Medium |
| — | Fel Armor / Demon Armor | — | — | FelArmor 386124 in SpellData (passive armor); Nether Ward / Dark Bargain absent | — | — | — | — | — | — | [V] | Low |
| — | Nether Ward | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| — | Dark Bargain | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| 1122 | Summon Infernal | Destr (+all V) | MajOff | 120 s (90 s w/ Inferno [12]) | 25–30 s (verify) | on? (verify) | 40 yd | burst + meteor stun | target dying; move-heavy | burn + shards | [V][IV][M] | Medium |
| 1276672 | Summon Doomguard | Demo (CD table only) | MajOff | UNVERIFIED (verify; single-target big pet) | ? | ? | ? | single-target burn (verify) | AoE/Infernal better | burn + safe | [V] | UNVERIFIED |
| 1276452 | Grimoire: Imp Lord | Demo (CD table only) | MajOff | UNVERIFIED (verify; hero-tree pet) | ? | ? | ? | burst + imp-swarm (verify) | no shards/setup | burn phase | [V] | UNVERIFIED |
| 1276467 | Grimoire: Fel Ravager | Demo (CD table only) | MajOff | UNVERIFIED (verify; hero-tree pet) | ? | ? | ? | burst + cleave (verify) | single/burst clash | burn phase | [V] | UNVERIFIED |
| 205180 | Summon Darkglare | Affl (+all V) | MajOff | 180 s | 20–25 s? (verify) | on? (verify) | 40 yd | burst + dot-extend | no dots; target dying | burn + dots rolling | [V][IV] | Low |
| — | Malevolence | — | — | 442726 in SpellData (Affl row); not a castable nuke — talent amp (verify) | — | — | — | — | — | — | [V] | Low |
| — | Chaos Bolt (ref row) | — | — | 116858 in SpellData; rotational spender, NOT a cooldown — listed for cross-ref | — | — | — | — | — | — | [V] | High |
| 80240 | Havoc | All (SpellData) | Utility | 20 s? (verify) | ~8–15 s? (verify) | on? (verify) | 40 yd | cleave-copy window | single target; banish clash | 2-target burn | [V][W] | Low |
| — | Dark Soul: Misery | — | — | not present in vendor v11.3.49 tables | — | — | — | — | — | — | [V] | UNVERIFIED |
| 19647 | Spell Lock (CD table) | All (pet; SpellData 119910 — drift) | Interrupt | 24 s | 6 s (verify) | off | 40 yd (pet) | priority ranged cast via pet | pet dead/CC'd; trivial | lethal cast + pet up | [V][W] | Medium |
| 111400 | Burning Rush | All | Move | no CD (HP drain) | while active | off? (verify) | self | sustained sprint + port setup | HP low; tiny gain | escape + HP safe | [V][W] | Medium |
| 111771 | Demonic Gateway | All | Move (group) | 90–120 s? (verify) | 15 s portal (verify) | on? (verify) | 40 yd pair | group taxi/escape | no setup time; solo | group move + time | [V][W] | Low |
| — | Axe Toss | — | — | not present in vendor v11.3.49 tables (Wrathguard pet stun) | — | — | — | — | — | — | [V] | UNVERIFIED |
| 333889 | Fel Domination | All (SpellData) | Utility | 180 s? (verify) | next summon instant | off? (verify) | self | instant pet-rez mid-fight | pet alive; no need | pet dead + burst | [V][W] | Low |

---

## Section 3 — Cross-cutting rules of thumb

### Off-GCD vs on-GCD
- **Interrupts are near-universally off-GCD** (Pummel, Rebuke, Kick, Wind Shear, Counterspell,
  Disrupt, Skull Bash, Spear Hand Strike, Silence, Quell, Spell Lock, Muzzle [verify each in game];
  Mind Freeze flagged on/off conflict — UNVERIFIED). Policy: interrupts never wait for the GCD;
  the C# engine gates them on range + observed interruptibility + lockout-DR only
  (cast importance is NOT observable - see Section 4 and the v2.1 addendum).
- **Defensives are near-universally off-GCD** (walls, DRs, absorbs, immunities, externals, cheat-deaths).
  Exceptions are active-mitigation spenders and channeled heals that ride the GCD.
- **On-GCD by default:** rotational self-heals (Word of Glory, Flash of Light, Vivify, Healing Surge,
  Chain Heal, Drain Life, Death Strike), placed utility (Capacitor, Tremor, Smoke, Gateways, Sigils),
  gap-closers that deal damage (Charge, Harpoon, Fel Rush [verify]), channeled bursts
  (Bladestorm, Convoke, Emerald Communion, Celestial Conduit, Fel Devastation [verify]).
- **No-GCD / passive:** cheat-deaths (Cheat Death, Cauterize, Nature's Guardian, Defy Fate),
  Icy Talons, Body and Soul, Burning Rush (HP drain), Ghost Wolf (form), procs.
- **Verify-in-game list (GCD):** Divine Shield/BoP/Freedom (10.x sources say on-GCD — relic or live?),
  Displacement, Mass Barrier, Mind Freeze, Fel Rush, Vengeful Retreat, Sigils, Convoke, Fel Devastation,
  Breath of Sindragosa, Summon Infernal/Darkglare, Capacitor/Tremor/Smoke/Gateway totems.

### Mobility while casting / channeling
- **Usable while casting (no cast-bar lock):** Blink, Shimmer (cast-while-moving blink), Disengage,
  Heroic Leap, Demonic Circle: Teleport, Transcendence: Transfer, Hover (explicitly enables
  cast-while-moving), Spirit Walk, Tiger's Lust, Angelic Feather pickup, Fel Rush (verify — dash
  cancels some channels), Charge (verify — requires target, breaks some channels).
- **Breaks or blocks channels:** Bladestorm (can't recast during spin), Convoke (movement cancels),
  Emerald Communion (movement/interrupt risk), Celestial Conduit (moving channel — allowed, verify),
  Fel Devastation (rooted channel, verify), Drain Life (moving breaks), Deep Breath (flight locks).
- **Rule:** never auto-fire movement that cancels a burst channel unless the emergency (lethal ground)
  outweighs the burst loss; engine needs a `cancelsChannel` flag per mobility row.

### Self-heals usable while stunned / CC'd
- **Usable while stunned (no cast, no target, off-GCD):** Renewing Blaze (verify — instant self-buff),
  Darkness (verify), Barkskin (verify — historically usable while stunned), Survival Instincts (verify),
  Icebound Fortitude (stun-break-adjacent DR, verify), Desperate Prayer (verify), Healthstone (verify),
  Crimson Vial (verify), Exhilaration (verify), Enraged Regeneration (verify).
- **NOT usable while stunned:** anything with a cast bar (Flash of Light, Healing Surge, Vivify,
  Drain Life, Chain Heal, Verdant Embrace), channeled top-ups (Emerald Communion), Death Strike
  (needs melee swing + GCD), Impending Victory (melee + GCD), Expel Harm (GCD, verify).
- **Rule:** the stunned state restricts the policy to instant off-GCD self-buffs + Healthstone;
  everything else must wait for the stun to clear. All stunned-usability flags are UNVERIFIED —
  confirm each in game before encoding.

### Dispel rules
- **Defensive dispel (self):** Cloak of Shadows (clears+resists spells, 5 s), Ice Block (clears, 10 s),
  Divine Shield (clears all, 8 s), Blur+Netherwalk (Netherwalk immunity, verify), Lichborne (undead
  breaks fear/charm/sleep), Tiger's Lust (root/snare clear), Blessing of Freedom (movement-impair
  clear), Stone Bulwark-adjacent absorbs (no dispel), Tremor Totem (fear/charm/sleep pulse-clear),
  Revival (raid magic dispel + heal).
- **Offensive dispel / steal:** Shattering Throw (immunity-break, verify), Spell Reflection (reflect,
  not dispel), Diffuse Magic [12] (redirects harmful magic back, verify), Mass Dispel (not covered —
  priest utility, verify), Gouge/Psychic Scream (CC, not dispel).
- **Rule:** dispels must check debuff-type match (magic vs poison vs disease vs curse vs bleed vs
  enrage) before firing; firing Freedom on a stun or Cloak on physical damage is a wasted cooldown.
  The engine cannot see debuff types today (see §4) — dispels stay manual/HOLD by default.

---

## Section 4 — Honest limitations: what an addon cannot observe

1. **Enemy cast identity and importance.** CORRECTED in v2.1 (see the addendum): in Midnight 12.x
   `UnitCastingInfo`/`UnitChannelInfo` are secret for non-player units and combat-log events are not
   available to addons at all, so neither the spell identity, the school, nor whether a
   cast will kill the group. Kick-everything policies cause lockout-DR waste; kick-nothing policies
   wipe. A curated dangerous-cast list cannot be observed, so no such policy exists (v2.1) per dungeon/raid — out of scope for this file.
2. **Secret / server-side values.** Exact absorb remainders, boss energy/threat tables, DR-stack
   counters, cheat-death internal cooldowns, proc seeds, and M+ affix scaling are not exposed to Lua.
   Cooldown-vs-cooldown stacking decisions must use visible buffs + HP deltas only.
3. **Movement / position state.** The addon cannot read XYZ, facing, ground-effect polygons, or
   pathing. Gap-closers, leaps, teleports, and knockback utilities (Charge, Harpoon, Blink, Heroic
   Leap, Disengage, Fel Rush, Wild Charge, Hover, Gateway, Transcendence, Rescue, Leap of Faith)
   cannot be auto-aimed; firing them blind risks cliffs, void zones, and pull accidents. HOLD by default.
4. **Ally state beyond the party frame.** HP percentages and visible buffs/debuffs are readable; exact
   incoming-heal prediction, healer mana intent, and off-screen ally positions are not. Externals
   (Ironbark, Pain Suppression, Guardian Spirit, Blessing of Sacrifice/Protection, Life Cocoon,
   Time Dilation, Intervene, Leap of Faith) must be conservative: fire only on explicit low-HP +
   damage-aura evidence, else HOLD.
5. **GCD / lockout / resource truth.** The companion sees the decoded MaxDps frame (suggestions +
   in-combat/on-GCD/has-target flags), not the spellbook cooldown sweep or rune/HP/RP/chi state.
   Double-firing a spender-gated heal (Word of Glory, Shield of the Righteous, Death Strike) or a
   stance-gated defensive is a real risk; the engine must track its own send history (it does —
   scheduler/duplicate-suppression) and prefer off-GCD, resourceless defensives when blind.
6. **Talent / build variance.** Every row above assumes the ability is talented/learned. The engine
   cannot reliably enumerate the player's build; unlearned-profiles already caused a locator failure
   (see VERSION.txt). Policy defaults must degrade gracefully when a suggested ability is absent.
7. **What this means for automation:** interrupts on curated-dangerous casts; defensives on HP-gated
   emergencies with off-GCD preference; mobility/dispel/externals default HOLD; burst cooldowns
   follow the upstream rotation (Main/Offensive slots) rather than independent timers; nothing in
   §2 fires without a target/range/HP precondition the engine can actually observe.

---

## Section 5 — Sources table

| # | URL | Title / what it verified | Accessed |
|---|---|---|---|
| 1 | https://www.wowhead.com/spell=335255/spell-reflection | Wowhead — Spell Reflection (Warrior CD/dur/GCD/range baseline) | 2026-09-27 |
| 2 | https://wowpedia.fandom.com/wiki/Shield_Wall | Wowpedia — Shield Wall (CD/dur conflict source) | 2026-09-27 |
| 3 | https://wowpedia.fandom.com/wiki/Die_by_the_Sword | Wowpedia — Die by the Sword (180 s / 8 s) | 2026-09-27 |
| 4 | https://www.wowhead.com/spell=2565/shield-block | Wowhead — Shield Block | 2026-09-27 |
| 5 | https://www.wowhead.com/spell=97462/rallying-cry | Wowhead — Rallying Cry (180 s / 10 s) | 2026-09-27 |
| 6 | https://www.wowhead.com/spell=202168/impending-victory | Wowhead — Impending Victory | 2026-09-27 |
| 7 | https://www.wowhead.com/spell=184364/enraged-regeneration | Wowhead — Enraged Regeneration (120 s / 8 s) | 2026-09-27 |
| 8 | https://www.wowhead.com/spell=1719/recklessness | Wowhead — Recklessness (90 s / 12 s) | 2026-09-27 |
| 9 | https://www.wowhead.com/spell=227847/bladestorm | Wowhead — Bladestorm | 2026-09-27 |
| 10 | https://wowpedia.fandom.com/wiki/Charge | Wowpedia — Charge (20 s / 8–25 yd) | 2026-09-27 |
| 11 | https://warcraft.wiki.gg/wiki/Heroic_Leap | Warcraft Wiki — Heroic Leap (45 s / 8–40 yd) | 2026-09-27 |
| 12 | https://www.wowhead.com/spell=394352/shattering-throw | Wowhead — Shattering Throw (range ref) | 2026-09-27 |
| 13 | https://www.wowhead.com/spell=642/divine-shield | Wowhead — Divine Shield (10.x legacy values) | 2026-09-27 |
| 14 | https://www.wowhead.com/spell=1022/blessing-of-protection | Wowhead — Blessing of Protection (10.x legacy) | 2026-09-27 |
| 15 | https://warcraft.wiki.gg/wiki/Blessing_of_Freedom | Warcraft Wiki — Blessing of Freedom | 2026-09-27 |
| 16 | https://www.wowhead.com/spell=6940/blessing-of-sacrifice | Wowhead — Blessing of Sacrifice | 2026-09-27 |
| 17 | https://www.wowhead.com/spell=86659/guardian-of-ancient-kings | Wowhead — Guardian of Ancient Kings | 2026-09-27 |
| 18 | https://www.wowhead.com/spell=31850/ardent-defender | Wowhead — Ardent Defender | 2026-09-27 |
| 19 | https://warcraft.wiki.gg/wiki/Lay_on_Hands | Warcraft Wiki — Lay on Hands | 2026-09-27 |
| 20 | https://warcraft.wiki.gg/wiki/Word_of_Glory | Warcraft Wiki — Word of Glory | 2026-09-27 |
| 21 | https://www.wowhead.com/spell=19750/flash-of-light | Wowhead — Flash of Light | 2026-09-27 |
| 22 | https://www.wowhead.com/spell=415091/shield-of-the-righteous | Wowhead — Shield of the Righteous | 2026-09-27 |
| 23 | https://warcraft.wiki.gg/wiki/Avenging_Wrath | Warcraft Wiki — Avenging Wrath | 2026-09-27 |
| 24 | https://warcraft.wiki.gg/wiki/Divine_Toll | Warcraft Wiki — Divine Toll | 2026-09-27 |
| 25 | https://www.wowhead.com/spell=96231/rebuke | Wowhead — Rebuke (15 s interrupt) | 2026-09-27 |
| 26 | https://www.wowhead.com/spell=190784/divine-steed | Wowhead — Divine Steed | 2026-09-27 |
| 27 | https://warcraft.wiki.gg/wiki/Icebound_Fortitude | Warcraft Wiki — Icebound Fortitude (12 s → 8 s in 12.x) | 2026-09-27 |
| 28 | https://warcraft.wiki.gg/wiki/Anti-Magic_Shell | Warcraft Wiki — Anti-Magic Shell | 2026-09-27 |
| 29 | https://warcraft.wiki.gg/wiki/Anti-Magic_Zone | Warcraft Wiki — Anti-Magic Zone | 2026-09-27 |
| 30 | https://warcraft.wiki.gg/wiki/Vampiric_Blood | Warcraft Wiki — Vampiric Blood | 2026-09-27 |
| 31 | https://warcraft.wiki.gg/wiki/Lichborne | Warcraft Wiki — Lichborne | 2026-09-27 |
| 32 | https://warcraft.wiki.gg/wiki/Death_Pact | Warcraft Wiki — Death Pact | 2026-09-27 |
| 33 | https://warcraft.wiki.gg/wiki/Death_Strike | Warcraft Wiki — Death Strike (on-GCD, RP-gated) | 2026-09-27 |
| 34 | https://warcraft.wiki.gg/wiki/Pillar_of_Frost | Warcraft Wiki — Pillar of Frost (60 s / 12 s) | 2026-09-27 |
| 35 | https://warcraft.wiki.gg/wiki/Breath_of_Sindragosa | Warcraft Wiki — Breath of Sindragosa | 2026-09-27 |
| 36 | https://warcraft.wiki.gg/wiki/Summon_Gargoyle | Warcraft Wiki — Summon Gargoyle | 2026-09-27 |
| 37 | https://www.wowhead.com/spell=1242147/summon-gargoyle | Wowhead — Summon Gargoyle (web ID vs vendor 49206 drift) | 2026-09-27 |
| 38 | https://warcraft.wiki.gg/wiki/Mind_Freeze | Warcraft Wiki — Mind Freeze (GCD conflict source) | 2026-09-27 |
| 39 | https://warcraft.wiki.gg/wiki/Death_Grip | Warcraft Wiki — Death Grip | 2026-09-27 |
| 40 | https://warcraft.wiki.gg/wiki/Wraith_Walk | Warcraft Wiki — Wraith Walk | 2026-09-27 |
| 41 | https://www.wowhead.com/guide/classes/demon-hunter/devourer/rotation-cooldowns-pve-dps | Wowhead Midnight — Devourer rotation (Void Meta resourceless, Hunt-on-CD) | 2026-09-27 |
| 42 | https://www.wowhead.com/spell=196555/netherwalk | Wowhead — Netherwalk (Blur-linked immunity ref) | 2026-09-27 |
| 43 | https://www.wowhead.com/spell=22812/barkskin | Wowhead — Barkskin (60 s / 8–12 s conflict) | 2026-09-27 |
| 44 | https://www.wowhead.com/spell=61336/survival-instincts | Wowhead — Survival Instincts (6 s, off-GCD spell data) | 2026-09-27 |
| 45 | https://www.wowhead.com/guide/classes/druid/guardian/abilities-talents-pve-tank | Wowhead Midnight — Guardian abilities (12.1.0, 2026-08-12) | 2026-09-27 |
| 46 | https://www.wowhead.com/guide/classes/druid/feral/abilities-talents-pve-dps | Wowhead Midnight — Feral abilities (12.1.0, 2026-08-12) | 2026-09-27 |
| 47 | https://www.wowhead.com/spell=374348/renewing-blaze | Wowhead — Renewing Blaze (8 s, 100% healed back) | 2026-09-27 |
| 48 | https://www.wowhead.com/spell=363916/obsidian-scales | Wowhead — Obsidian Scales (30% DR) | 2026-09-27 |
| 49 | https://us.forums.blizzard.com/en/wow/t/noob-evoker-question/1646755 | Blizzard forums — Renewing Blaze behavior thread | 2026-09-27 |
| 50 | https://www.wowhead.com/guide/classes/evoker/preservation/abilities-talents-pve-healer | Wowhead Midnight — Preservation abilities | 2026-09-27 |
| 51 | https://www.wowhead.com/spell=19574/bestial-wrath | Wowhead — Bestial Wrath (90 s CD) | 2026-09-27 |
| 52 | https://www.wowhead.com/spell=45438/ice-block | Wowhead — Ice Block (10 s immunity) | 2026-09-27 |
| 53 | https://www.wowhead.com/guide/classes/mage/frost/rotation-cooldowns-pve-dps | Wowhead Midnight — Frost Mage rotation (Ray/Frozen Orb prio) | 2026-09-27 |
| 54 | https://warcraft.wiki.gg/wiki/Diffuse_Magic | Warcraft Wiki — Diffuse Magic folded into Fort Brew (12.0.0, 2026-01-20) | 2026-09-27 |
| 55 | https://www.wowhead.com/spell=1243287/diffuse-magic | Wowhead — Diffuse Magic (redirect-back rider) | 2026-09-27 |
| 56 | https://www.icy-veins.com/wow/brewmaster-monk-pve-tank-rotation-cooldowns-abilities | Icy Veins — Brewmaster 12.1 (Fort Brew CDR, Ironshell/Determination) | 2026-09-27 |
| 57 | https://maxroll.gg/wow/class-guides/mistweaver-monk-mythic-plus-guide | Maxroll — Mistweaver 12.1 Midnight (Celestial Conduit 1.5 min) | 2026-09-27 |
| 58 | https://www.wowhead.com/spell=17/power-word-shield | Wowhead — Power Word: Shield (7.5 s CD, 15 s, on-GCD) | 2026-09-27 |
| 59 | https://www.wowhead.com/guide/classes/priest/discipline/abilities-talents-pve-healer | Wowhead Midnight — Discipline abilities | 2026-09-27 |
| 60 | https://www.wowhead.com/spell=31224/cloak-of-shadows | Wowhead — Cloak of Shadows (120 s / 5 s, off-GCD) | 2026-09-27 |
| 61 | https://www.wowhead.com/guide/classes/rogue/subtlety/abilities-talents-pve-dps | Wowhead Midnight — Subtlety abilities | 2026-09-27 |
| 62 | https://www.wowhead.com/guide/classes/shaman/enhancement/rotation-cooldowns-pve-dps | Wowhead Midnight — Enhancement rotation (Doom Winds, Ascendance) | 2026-09-27 |
| 63 | https://www.wowhead.com/guide/classes/shaman/elemental/abilities-talents-pve-dps | Wowhead Midnight — Elemental abilities | 2026-09-27 |
| 64 | https://warcraft.wiki.gg/wiki/Shaman_abilities | Warcraft Wiki — Shaman abilities/talent map | 2026-09-27 |
| 65 | https://maxroll.gg/wow/class-guides/destruction-warlock-raid-guide | Maxroll — Destruction 12.0.7 (Inferno: Infernal 90 s) | 2026-09-27 |
| 66 | https://www.icy-veins.com/wow/demonology-warlock-pve-dps-spell-summary | Icy Veins — Demonology 12.1 spell glossary (UR, Tyrant, Soulburn) | 2026-09-27 |
| 67 | https://www.wowhead.com/guide/classes/warlock/demonology/abilities-talents-pve-dps | Wowhead Midnight — Demonology abilities (UR shield-wall note) | 2026-09-27 |
| 68 | https://www.wowhead.com/guide/classes/warlock/destruction/abilities-talents-pve-dps | Wowhead Midnight — Destruction abilities | 2026-09-27 |
| 69 | https://www.wowhead.com/news/shaman-talent-tree-updates-on-war-within-beta-343984 | Wowhead — Shaman TWW beta (Ascendance 15 s ref, legacy) | 2026-09-27 |
| 70 | `vendor/MaxDps/Cooldowns.lua` + `vendor/MaxDps/SpellData.lua` (retail block) + `vendor/MaxDps/MaxDps.toc` | Repo vendor — core v11.3.49 (2026-09-21), IDs + CD categories + Interface 120100 | 2026-09-27 |

*Confidence rule used throughout: vendor ID/category = High; two independent web sources agreeing on
a Midnight-12.x value = High; one web source or legacy-patch source = Medium/Low as marked;
single research-agent synthesis with no second source = Low; no source = UNVERIFIED.*

---

## Section 6 — Vendor defensive-urgency discovery (v2.3 audit, 2026-09-28)

Target: **live retail Midnight 12.1**, not PTR. Access date 2026-09-28. Unlike
§2's web rows, every fact below was traced through the pinned vendor snapshot
in this repo (`vendor/MaxDps`, core v11.3.49) and is the source of truth the
bridge mirrors; no external page is claimed.

**There is no discrete defensive colour enum in MaxDps.** The whole colour
is computed per spell by `MaxDps:GlowDefensiveHPMidnight(spellId)`
(`vendor/MaxDps/Buttons.lua:1056-1110`). It builds two linear colour curves
once (lines 1067-1080):

| Curve | Control points | Used for |
| :--- | :--- | :--- |
| `GlowDcurve` | `0.3 → (1,0,0,1)` red · `0.5 → (1,1,0,0.5)` yellow · `1.0 → (0,1,0,0)` transparent | HP curve (all defensives) |
| `ReverseGlowCurve` | `0.3 → (0,1,0,1)` green · `0.5 → (1,1,0,0.5)` yellow · `1.0 → (1,0,0,1)` red | stagger curve (one spell) |

The colour is `UnitHealthPercent("player", false, GlowDcurve)` (line 1091-1093)
unless the spell is **119582 (Purifying Brew)**, whose branch reads
`UnitStagger("player") / UnitHealthMax("player")` through `ReverseGlowCurve`
when neither value is secret (lines 1082-1090); when the stagger read is
secret the vendor itself falls back to the HP curve
(`if not color then color = UnitHealthPercent(...)`, line 1091). The curve's
alpha is then multiplied by the spell's remaining-cooldown colour
(`C_Spell.GetSpellCooldownDuration(spellId):EvaluateRemainingDuration(GlowDcurve)`,
lines 1094-1096). For every usable defensive while the player is alive it sets
`self.Flags[spellId] = true` and applies the glow (lines 1100-1103); skills
not usable or the player dead/ghost clear the flag (lines 1046-1049,
1105-1108).

Spec files call it for each `classCooldowns[class][spec].defensive` entry
gated by `CheckSpellUsable`, e.g. `vendor/MaxDps_Warrior/Specialization/Fury.lua:18-25`.

**Derived interpretation (what the bridge/policy encode).** "Recommended"
means the flag is true (alive + usable + enabled); "urgency" is the rendered
colour. MaxDps has no white/green state other than "no glow":

| Rendered stage | Vendor curve meaning | HP condition | Stagger condition (119582) |
| :--- | :--- | :--- | :--- |
| Red | red anchor (alpha 1) | HP ≤ 30% | stagger ≥ 100% |
| Orange | red→yellow blend | 30% < HP < 50% | 50% ≤ stagger < 100% |
| Yellow | yellow→transparent fade | 50% ≤ HP < 100% | 30% ≤ stagger < 50% |
| White | transparent end (no glow) | HP = 100% | stagger < 30% |

The bridge stages this at the curve's own control points and encodes a nibble
`0`-`4` (Reader.lua `UrgencyFromFraction`/`UrgencyFromStagger`/
`GetDefensiveUrgency`, `addon/MaxDpsBridge/Reader.lua:1393-1442`); a secret
or failed read is `0` = UNKNOWN, never compared. This is an interpretation of
the vendor's continuous curve, not a colour enum read from the game — the
boundaries are authored by the curve anchors, which is why they are used
rather than invented HP bands. The urgency nibbles are **additive on the v5
wire**: cell 10 R stays 5 and the nibbles occupy bits that were reserved and
always `0` before bridge 2.3, so an updated addon still decodes with an older
companion exe.

**Defensive gap-fill.** `MDB.GetDefensiveCandidate()`
(`Reader.lua:1444-1467`) returns MaxDps's flagged + ready + bound defensive
first; only when MaxDps names none AND `GetDefensiveUrgency(nil) == RED`
does it fall back to `MDB.Extras[class][spec].defensive`, the catalog-derived
list, and mark the source. The bridge encodes the source bit and both urgency
nibbles (`Bridge.lua:531-539, 555-562, 576-577, 594-600`). The whole path
stays inside MaxDps's own `enableDefensives` switch (line 1458). The derived
list itself (`AbilityCatalog.DefensiveGapFill`) is documented in
`docs/KNOWLEDGE.md`.

## Addendum (v2.1 audit, 2026-09-27): live API constraints that shaped the policy

Added during the v2.1 hostile audit. Access date 2026-09-27; target retail
Midnight 12.1. Wiki page headers during the audit read `12.1.5`; facts that
are page annotations rather than Blizzard statements may track the newest
branch, so they are marked Medium below.

| Fact | Source | Confidence |
| :--- | :--- | :--- |
| Secret Values: comparison, arithmetic and boolean tests on a secret throw in tainted code; `issecretvalue`/`canaccessvalue` are the guards | `https://warcraft.wiki.gg/wiki/Secret_Values` (12.0.0 system) | High |
| `C_Spell.GetSpellCooldown` / `GetSpellCharges` are `SecretWhenCooldownsRestricted`; `isActive`/`isEnabled`/`isOnGCD` (and `maxCharges`) are `NeverSecret` | `API:C_Spell.GetSpellCooldown`, `API:C_Spell.GetSpellCharges` (ed. Aug-Sep 2026) | High |
| `UnitCastingInfo`/`UnitChannelInfo` are `SecretWhenUnitSpellCastRestricted` (secret when the unit is not the player or pet); `isTradeskill`/`castBarID`/`delayTimeMs` are NeverSecret; `notInterruptible` carries no NeverSecret tag | `API:UnitCastingInfo`, `API:UnitChannelInfo` | High |
| Enemy cast spell identity is NOT branchable: the `UNIT_SPELLCAST_START` payload `spellID` is secret-conditional; only `castBarID` is NeverSecret | `Event:UNIT_SPELLCAST_START` (ed. 13 Aug 2026) | High |
| Combat-log events are no longer available to addons ("Combat Log Events are no longer available to addons", Oct 2025 planned-changes post) | `Patch_12.0.0/Planned_API_changes`; corroborating Wowhead news | High for the removal, Medium for exact 12.1 enforcement |
| `UNIT_SPELLCAST_INTERRUPTIBLE` / `UNIT_SPELLCAST_NOT_INTERRUPTIBLE` carry the state in the event name; their pages list no secret predicate and the payload is only `unitTarget` | `Event:UNIT_SPELLCAST_INTERRUPTIBLE` (ed. 25 Aug 2026) | Medium (neither page individually verified; the reddit community signal is Low and cited only as absence-of-support) |
| `UnitHealth` returns secrets unconditionally | `API:UnitHealth` (ed. 10 Aug 2026) | High |
| `GetUnitSpeed` returns `SecretWhenUnitStatsRestricted`; `UnitPosition` is `#noinstance` (outdoors, player/party/raid only) | `API_GetUnitSpeed`, `API:UnitPosition` | High |
| `C_Spell.IsSpellInRange` is tainted-callable and returns a plain `boolean?` | `API:C_Spell.IsSpellInRange` (ed. 10 Aug 2026) | High for the annotation, Medium that live 12.1 matches |
| `CheckInteractDistance` is `#nocombat`-restricted with an enemy-unit rollback; not a general range check | `API:CheckInteractDistance` (restriction from 10.2.0) | High |
| `GetSpecialization` / `GetSpecializationInfo` are deprecated (11.2.0); `C_SpecializationInfo` is the replacement | `API:GetSpecialization` (ed. 6 Sep 2026) | High |
| Spell Reflection reflects "the first spell cast on you" and gives 20% magic DR for 5 s; reflectability is per-ability data, not a general single-target rule | `https://www.wowhead.com/spell=23920/spell-reflection`; Maxroll Spell Reflect guide (patch 12.1) | High for the wording, Medium for the AoE inference |
| Impending Victory: 30% max HP heal, 25 s CD, melee, instant, Arms/Fury talent replacing Victory Rush; no source places it in the base rotation | `https://www.wowhead.com/spell=202168/impending-victory` | High for the numbers; absence-of-evidence for rotation membership |

Implications actually encoded in the bridge/companion (v2.1):

- Interruptibility comes ONLY from the two `UNIT_SPELLCAST_*` event names
  (plain booleans, no payload ever read); everything else about an enemy cast
  is unknown. A kick is vetoed on an explicit NOT_INTERRUPTIBLE observation.
- Reflect filtering cannot assert "reflectable": it can only require that a
  cast is live. The client cannot distinguish AoE/unreflectable casts from
  reflectable ones — this is an API limit, not an unbuilt feature.
- Combat-log-based "dangerous cast" logic is impossible, not merely unbuilt.
- `C_Spell` NeverSecret fields + Duration objects remain the only cooldown
  truth; enemy `notInterruptible`, enemy spell ids and player cast remaining
  time stay unread.
- `abilities.json` updated: Impending Victory `healPct` 30, `cdMs` 25000,
  with the source recorded on the entry.

---

## Addendum (v2.2 self-sustain audit, 2026-09-28): live ability verification + per-spec classification

Target: **live retail Midnight 12.1**, not PTR. Access date 2026-09-28.
Wowhead rendered the live legacy page for 202168; a separate 12.1.5 PTR page
(`spell=1235382`) exists and was deliberately NOT used. Warcraft Wiki pages
were read on the same date.

| Ability | Verified live values | Source | Confidence |
| :--- | :--- | :--- | :--- |
| Impending Victory (202168) | 10 Rage, Melee 5 yd, Instant, 25 s cooldown, Normal GCD (1.5 s), heals 30% max HP; killing an enemy resets the CD and makes it free; Warrior talent replacing Victory Rush (Arms/Fury/Prot) | `https://www.wowhead.com/spell=202168/impending-victory` | High for the values (live page: cost/range/GCD/heal/CD) |
| Impending Victory heal effect (202166) | Self-range, no cost, GCD 0 s, heal 30% of total health — the heal aura the cast applies | `https://www.wowhead.com/spell=202166/impending-victory` | Medium (same site, effect row; explains why the GCD rides the cast, not the heal) |
| Exhilaration (109304) | Level 9 hunter, 45 yd, 2 min CD, instant, heals self 30% / pet 100% | `https://warcraft.wiki.gg/wiki/Exhilaration` | Medium-High |
| Desperate Prayer (19236) | Row 7 priest talent, 1.5 min CD, instant, +25% max HP for 10 s and heals for that amount (was 30% before BfA) | `https://warcraft.wiki.gg/wiki/Desperate_Prayer` | Medium-High |
| Crimson Vial (185311) | Level 8 rogue, 20 Energy, 30 s CD, instant, heals 20% max HP over 4 s | `https://warcraft.wiki.gg/wiki/Crimson_Vial` | Medium-High |
| Death Pact (48743) | Row 4 DK talent, 2 min CD, instant, heals 50% max HP but absorbs 30% max HP of incoming healing for 15 s (drawback curated as `useBelowHpPct:50`) | `https://warcraft.wiki.gg/wiki/Death_Pact` | Medium-High |

Curated corrections from this pass: Desperate Prayer `healPct` 30 → 25
(modern value); Death Pact gains `useBelowHpPct: 50`; Impending Victory gains
`requiresTarget: true` (melee attack) and the live 2026-09-28 source; Death
Strike and Drain Life gain `requiresTarget: true` (melee/ranged attack heals).
`healPct` remains a static overheal heuristic (see docs/KNOWLEDGE.md), never a
live read.

### §5 — Per-spec self-sustain classification (all 40 specs)

Categories: **EXTRA** = MaxDps never surfaces it, wired into the generated
per-spec SelfHeal list (bridge offers it when ready + bound; Solo policy
decides); **MAXDPS** = the ability IS in MaxDps's own vendor tables
(Defensive bucket) and is handled through the Defensive slot by the same
policy — deliberately NOT duplicated into extras (two candidate slots for one
ability would only add noise); **MANUAL** = catalogued but deliberately never
automatic (reason given); **NONE** = no safe automatic self-sustain for that
spec.

| Spec | EXTRA (companion self-heal list) | MAXDPS-surfaced sustain | MANUAL / notes |
| :--- | :--- | :--- | :--- |
| Warrior Arms / Fury | 202168 Impending Victory | 184364 Enraged Regeneration (Fury) | — |
| Warrior Protection | 202168, 190456 Ignore Pain | 190456 also vendor Defensive | — |
| Paladin Holy / Ret / Prot | 85673 Word of Glory, 19750 Flash of Light | 633 Lay on Hands (`useBelowHpPct:40`) | — |
| Death Knight Blood / Frost / Unholy | 45470 Death Strike | 48743 Death Pact (`useBelowHpPct:50`, absorb drawback) | 49039 Lichborne leech (CC-break semantics) |
| Demon Hunter Vengeance | 263648 (absorb, also vendor Defensive) | 212084 Fel Devastation | — |
| Demon Hunter Havoc / Devourer | NONE | 198589 Blur, 196555 Netherwalk | — |
| Druid Balance / Feral / Guardian / Resto | 774 Regrowth | 108238 Renewal, 22842 Frenzied Regeneration | — |
| Evoker Aug / Dev / Pres | 361469 Living Flame | 374348 Renewing Blaze | 370960 Emerald Communion (`neverAutomatic`: burst channel) |
| Hunter BM / MM / Survival | NONE | 109304 Exhilaration, 281195 Survival of the Fittest | — |
| Mage Arcane / Fire / Frost | NONE (absorb/immunity only) | 235450 / 235313 / 11426 barriers, 45438 Ice Block | 342245 Alter Time (positional rewind) |
| Monk Brewmaster / Windwalker | 116670 Vivify | 322101 Expel Harm | — |
| Monk Mistweaver | 116670 Vivify, 124682 Enveloping Mist | 322101 Expel Harm | — |
| Priest Disc / Holy / Shadow | 2061 Flash Heal | 19236 Desperate Prayer | 15286 Vampiric Embrace (raid heal) |
| Rogue Assn / Outlaw / Sub | NONE | 185311 Crimson Vial, 31224 Cloak, 5277 Evasion | — |
| Shaman Ele / Enh / Resto | 8004 Healing Surge, 77472 Greater Healing Wave (Resto) | 108271 Astral Shift | 5394 Healing Stream Totem (group trickle), 108280 Healing Tide (`neverAutomatic`) |
| Warlock Aff / Demo / Destro | 234153 Drain Life | 104773 Unending Resolve, 108416 Dark Pact | 6789 Mortal Coil (fear can scatter mobs), 6262 Healthstone (item) |

The classification is a policy decision, not a claim that every class is
equal: specs whose only sustain is a damage-reduction cooldown deliberately
have an EMPTY extra list (Mage, Hunter, Rogue, Havoc/Devourer DH) — the
Defensive slot is the correct (and already wired) path for them, and the
companion's own `SelfHeal` policy would otherwise compete with MaxDps's own
defensive gating. `Impending Victory` is the acceptance case precisely
because it is NOT in any vendor table: no MaxDps slot can ever carry it.

---

## Addendum (class-spells layer, 2026-09-28)

The Class skills screen's ability book is built from the **retail block of
`vendor/MaxDps/SpellData.lua`** (`ns.classSpellData`, the `IsRetailWow()`
branch, lines ~41–8220; the file's Cata/Mists blocks are ignored). It is a
flat `class -> spec -> token -> spell id` map extracted by
`tools/Extract-ClassSpells.ps1` into `class-spells.json`: **8070 entries,
3566 distinct spell ids, 13 classes, 39 class/spec pairs**. Demon Hunter
Devourer has no rows in this vendor pin, matching Section 1's note.

This table is **not** a curated Cooldowns table: MaxDps records no name, no
category and **no active/passive flag** for these rows. The tokens mix real
castable abilities with passive talents, professions, riding, heirlooms and
old-content perks. The companion therefore treats the layer as best-effort —
`ClassSpellBook` decodes CamelCase tokens into display labels (plus a small
alias map) and `IsJunk` drops the token/name patterns that are clearly not
castable — and merges the survivors strictly below the curated and vendor
layers (curated > vendor `Cooldowns` > class spells). The bridge's defensive
gap-fill ignores the layer entirely.

Honest limits: the name decode and category heuristics are modeled, not
authoritative, so a passive talent that evades the filter can appear on the
screen (its toggle is inert because such a spell never arrives as a
suggestion); filtered rows stay in the JSON but are never shown; and no live
in-game validation is claimed here.

### Verification outcome against the live client (2026-09-28)

The class-spells layer is the only knowledge source with no names or
active/passive flag, so `tools/Verify-ClassSpells.ps1` cross-checks it against
the live client's own data instead of web pages:

- **Sources.** Three [wago.tools](https://wago.tools) DB2 CSV exports —
  `SpellName` (official `Name_lang`), `SpellMisc` (`SpellIconFileDataID`) and
  `ManifestInterfaceData` (fileDataID → `.blp`/slug) — plus Blizzard's render
  CDN (`render.worldofwarcraft.com/us/icons/56/{slug}.jpg`) and the zamimg
  mirror. Output: `app/MaxDpsCompanion/Knowledge/spell-verification.json`,
  deterministic (byte-identical reruns).
- **Why Wowhead was unusable.** The Wowhead tooltip endpoints (the icon-slug
  discovery path the runtime `SpellIconCache` falls back to) are blocked from
  this machine, so they cannot be a build-time dependency. wago.tools plus the
  Blizzard render CDN are the working official sources.
- **Column gotcha (live-verified).** `SpellMisc`'s `ID` column is the
  *misc-record* id, not the spell id; the real spell id is the trailing
  `SpellID` column. Joining on `ID` yields wrong icons (e.g. spell 53 Backstab
  → `Ability_Defend`). The script joins on `SpellID`, validated against six
  known spells (`5277` Evasion, `871` Shield Wall, `193315` Sinister Strike,
  `53` Backstab, `100` Charge, `1766` Kick) and fails loud on any mismatch.
  Manifest file names can embed spaces (`Warlock_ Healthstone`); the CDN
  request strips them.
- **Counts and semantics.** 3566 entries: **3515 verified / 51 absent**
  (removed in 12.1). An entry is verified when the id exists in `SpellName`
  and its name is not marked deprecated. Unverified ids are **not** merged
  into the ability knowledge: `ClassSpellBook` marks `Verified`,
  `AbilityCatalog.MergeClassSpells` skips them, and the screen uses the
  client's official name/icon in place of the CamelCase decode.
- **Icons.** 3510 official icons were downloaded into
  `dist/assets/icons/{spellId}.jpg` and the dev bin cache (0 failures). The
  runtime `SpellIconCache` now prefers the verified official slug (one request)
  and only falls back to tooltip discovery; `build.ps1` preserves
  `settings.ini` and `assets/` across publishes.

This is verification of the *modeled class-spell ids against the client's own
data*, not an in-game behavioural validation: no live combat result is claimed
here, and the screen's category heuristics remain modeled (see the honest
limits above).