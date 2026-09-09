# vendor/ — pinned upstream MaxDps snapshot (read-only)

`vendor/` holds a pinned snapshot copy of kaminaris **MaxDps core + all
class modules**, copied from the retail install at snapshot time:

```
<WoW>\_retail_\Interface\AddOns\MaxDps*  →  vendor\
```

Expected folders (see `pin-versions.txt`):

- `MaxDps` (core, v11.3.43)
- `MaxDps_DeathKnight`, `MaxDps_DemonHunter`, `MaxDps_Druid`,
  `MaxDps_Evoker`, `MaxDps_Hunter`, `MaxDps_Mage`, `MaxDps_Monk`,
  `MaxDps_Paladin`, `MaxDps_Priest`, `MaxDps_Rogue`, `MaxDps_Shaman`,
  `MaxDps_Warlock`, `MaxDps_Warrior`, `MaxDps_MacroSupport`

Purpose: offline reference for the bridge's `MaxDps.Spell` queries and for
reviewing upstream rotation changes. The bridge and companion contain
**zero rotation intelligence** — they only transport what the live addon
already suggests.

**Do NOT copy the ~8.6 MB snapshot through agent context.** The lead runs
robocopy at install time. This directory ships with only this README plus
`pin-versions.txt` until then.
