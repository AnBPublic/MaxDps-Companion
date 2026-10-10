# 3.7.8 "Steadfast" — MAIN trusts the MaxDps core rotation (deny list removed)

## Goal / directive

**If MaxDps recommends an ability as its core rotation, press it as MAIN — no
exception.** Whatever the official MaxDps addon (core MaxDps + all
`MaxDps_<Class>` modules, every class) reports through `SpellsGlowing` /
`MaxDps.Spell` as the core rotation is what the MAIN slot encodes.

Do **not** confuse this with the other three slots: Offensive / Defensive /
Interrupt keep their own independent routing (`MDBX.FlagOffensiveExtra` for the
Offensive flags scan) and are unchanged. The directive is only about MAIN no
longer second-guessing MaxDps over a hand-maintained "big cooldown" list.

## What changed (Exp only)

- `addon/MaxDpsBridgeExp/Reader.lua` `GetMainSpellID`: **no deny scan.** It
  collects every glowing id (`On == 1`), sorts ascending, and applies only the
  game-truth power veto (below). It no longer skips denied ids in the
  `SpellsGlowing` scan and no longer rejects a `MaxDps.Spell` because it is
  major-flagged.
- `addon/MaxDpsBridgeExp/MajorCooldowns.lua`: the `MDBX.MajorCDDeny` table
  (`D[]`) is **deleted**. The file now holds only `MDBX.FlagOffensiveExtra`
  (the non-vendor ids `Reader.CategoryOf` classifies as `"offensive"`, so the
  `FirstFlagged("offensive")` scan still routes them). That routing is
  unchanged.
- `MDBX.FlagOffensiveExtra` and the per-spec `MainFallback` filler are
  unchanged.

## Contracts (do not break)

- **Only game-truth vetoes remain**: `C_Spell.IsSpellUsable(SpellID)` with the
  `usable == false AND noPower == true` power veto; secret / nil / throw fail
  OPEN. This is the only reason MAIN may skip a MaxDps glow.
- **Per-spec filler kept**: when every glow is power-starved (or none glows),
  `MDBX.MainFallback[specID | "CLASS:Spec"]` supplies a legal MAIN key so the
  slot is never dead (unlisted specs stay nil, idle by design).
- **No wire change**: `PROTOCOL` stays v5; every cell is byte-identical. No
  `Reader`/`Bridge` encoder field, no `docs/PROTOCOL.md`, no `PixelProtocol.cs`
  change.
- **Stable frozen**: `addon/MaxDpsBridge/` + `dist\` stay at their last state
  (3.7.7). The deny removal is Exp-only; stable keeps its frozen deny.

## Harness pins flipped

`tests/secret_harness.lua` previously pinned the deny behaviour (the T3
`MDB.MajorCDDeny` table checks and the "Ravager held out of Main" check). Those
pins are inverted for Exp 3.7.8: the deny table is gone and previously denied
ids (e.g. Combustion 190319, Avatar 107574, Ravager 228920) are now eligible as
MAIN. The Offensive `FlagOffensiveExtra` routing pins stay.

## Acceptance + live OWED

- **Offline / static**: `luac -p` all bridge files, `lua
  tests/secret_harness.lua`, `dotnet build/test -c Release`, `pwsh
  tools/ability_audit.ps1`. Offline evidence is static only — never live proof.
- **Live retail (OWED)**:
  1. Previously denied MaxDps core-rotation recommendations (e.g. Combustion
     190319, Avatar 107574, Ravager 228920) **are pressed as MAIN** when MaxDps
     glows them.
  2. Offensive routing is unchanged vs 3.7.7 (`FlagOffensiveExtra` ids still
     reach the Offensive slot).
  3. A power-starved glow falls through to the next glow or the per-spec
     filler — never a dead MAIN.
  4. Record + export + replay: 0 decision/policy mismatches.
  Static ≠ automated test ≠ live in-game.

## Publish steps

1. Land the code change (`Reader.lua` deny removal + `MajorCooldowns.lua` `D[]`
   deletion) and the flipped harness pins.
2. Bump identity to 3.7.8 / Steadfast across csproj, `Native.cs`, `MDBX.VERSION`,
   the Exp TOC, `MaxDpsBridgeExp/VERSION.txt`, repo `VERSION.txt` and
   `ReleaseIdentityTests`. Stable stays 3.7.7.
3. Run the offline bar; keep §3h of `docs/TESTING.md` marked OWED.
4. Deploy with `tools/install-addon-exp.ps1` + `dist-exp` publish; verify Exp
   hashes. `install-addon.ps1` / `dist\` are legacy-only.
