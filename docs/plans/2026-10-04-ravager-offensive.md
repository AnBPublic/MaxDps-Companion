# Ravager to Arms offensive cooldowns via fork patch (2026-10-04)

## T0 mirror (done, read-only)
- Live AddOns MaxDps* tree 333/333 MANIFEST fileSha256 match, 0 diff: stock, NO mirror needed (local>github no-op). custom/out/ absent (sync never run). Live MaxDpsBridge toc 3.3.0 stale; MaxDpsBridgeExp 3.7.2-exp present.

## Parity findings (vendor read-only)
- vendor/MaxDps/Cooldowns.lua Arms :739-745 offensive = Avatar 107574 + Warbreaker 262161 (Colossus Smash/Bladestorm/Sweeping Strikes commented). Fury :756-761 = Recklessness 1719 + Avatar 107574 (Odyn's Fury/Onslaught commented). Ravager 228920 in NEITHER.
- User claim "Arms Avatar core / Fury Avatar offensive": NOT supported — Avatar is offensive-table in BOTH (Arms:740, Fury:759). Spec modules use GlowCooldown only, never setSpell, for Avatar (Arms :171 etc, Fury :154/:162/:266) and Ravager (Arms :169 etc, Fury :269).
- So the ONLY real gap vs Fury parity: Ravager missing from the offensive table (both specs lack it; scope = Arms per request). Bridge already compensates: MajorCooldowns.lua:84 deny-Main + :90 FlagOffensiveExtra[228920], Reader.lua:559-576 CategoryOf, Catalog.lua:94.

## Patch contract P-DATA-023 (custom/patches.json append, kind data)
- Anchor on unique commented Sweeping Strikes line (Cooldowns.lua:743), NOT Warbreaker (P-DATA-009 owns that regex; parsed regex matches once at :744, no conflict, but keep anchors disjoint).
- anchor regex: --\["Sweeping Strikes"\]\ =\ 260708, (confirm exact-once + CRLF/LF match).
- apply op replace, text: --["Sweeping Strikes"] = 260708,\n                ["Ravager"] = 228920, -- P-DATA-023 added-to-offensive (glow->offensive slot; bridge deny-list keeps it off Main)
- fixedWhen regex: \["Ravager"\]\s*=\s*228920 (idempotent, FIXED-UPSTREAM if upstream adds it).
- Counts: patches 75->76, data 22->23. Hard-coded counts live in custom/CUSTOM_FORK.md:72,88,107-108,136,192 + HANDOVER.md:275 (NOT in tests/sync or ability_audit) — update docs. CUSTOM_FORK move table "22 moves": Ravager is an ADDITION — add separate Additions row, do not bump moves.

## Tasks
- T1 patches.json append P-DATA-023 (JSON valid).
- T2 tests/tool only if count assertions fail: add fixture asserting exactly one ["Ravager"]=228920 in Arms block + second-run FIXED-UPSTREAM.
- T3 docs same pass: CUSTOM_FORK.md (76/23 + Additions row + stale reconciliation note if dry-run proves rich schema consumed), HANDOVER.md, ARCHITECTURE.md (pipeline/file-map). Nothing in addon/ or app/.
- T4 sync+publish: dry-run pwsh tools/Sync-CustomMaxDps.ps1 -NewUpstream custom\upstream-pristine -WhatIf (0 CONFLICT, P-DATA-023 APPLIED); build to $env:TEMP\AddOnsTest, inspect custom/out/MaxDps/Cooldowns.lua + SYNC-REPORT.md (Ravager inside Arms offensive, P-DATA-009 intact); publish -NewUpstream pristine -PublishTo live AddOns (script backs up to custom/out/_backup/<ts>/). Close WoW first. GitHub side = patches.json only (custom/out/ never committed).
- T5 verify publish: published Cooldowns.lua hash == custom/out; rg Ravager => exactly 1 hit in Arms block; luac -p published Cooldowns.lua.

## Acceptance
- dotnet build -c Release 0/0; dotnet test -c Release all pass; lua secret_harness pass; luac -p bridges clean; ability_audit exit 0; tests/sync/Sync-CustomMaxDps.Tests.ps1 pass. git diff --stat only patches.json + tests + docs (no vendor/, upstream-pristine/, custom/out/, addon/, app/).

## Risks + owed live
- Offensive loop (SpellFrame.lua:472) flagging unproven offline — OWED retail: Arms+Ravager talent, offensive slot sends Ravager key, Main does not. Spell id name-verified v11.3.49 only; confirm Ravager exists Arms retail 12.1. Fury/Prot Ravager out of scope (Catalog lists, table lacks) — separate patch if wanted. Avatar needs NO move (already offensive both specs).
