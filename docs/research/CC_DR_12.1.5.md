# Crowd-control diminishing returns — Midnight 12.1.5

Research for companion 4.2.0 "Nightblade" (2026-10-18). This file records the
rules the companion models and the sources used. The companion never reads the
game's live DR state — it only remembers what it (or the player) applied.

## Rules used

| Application | PvE stun | PvP stun |
|---|---|---|
| 1st | 100% | 100% |
| 2nd | 50% | 50% |
| 3rd | 25% | **immune** |
| 4th | 12.5% | immune |
| 5th+ | 6.25% (1/16 floor) | immune |

- **Reset:** 20 s after the last application **ends**. Patch 12.1.0 changed
  this from 16 s; 12.1.5 did not change it again.
- **PvE:** since 12.0 creatures no longer become fully immune to stuns after
  three applications; duration keeps halving to the 1/16 floor. Only stuns have
  meaningful PvE DR. A stun shorter than the companion's 1.0 s usefulness floor
  is not generated.
- **PvP:** fully immune after two applications. A third stun inside the window
  is wasted and is never generated.
- **Kidney Shot:** live duration is 2–6 s by combo points. The companion cannot
  see combo points, so it uses the conservative 2 s minimum.
- **Blind/Gouge/Sap/Polymorph/Fear/Hex/Repentance:** break on damage. The
  companion never auto-casts them; a manual press starts the attack guard.
- **Bosses / inherent immunity:** there is no safe API flag. The bridge already
  skips `worldboss` / boss-level targets for the auto CC pool; the companion
  additionally latches "stun ineffective" when a stun sent on a confirmed cast
  leaves the target still casting ~800 ms later.

## Sources

- Warcraft Wiki, *Diminishing returns* — Patch 12.1.0 (2026-08-11): DR
  categories reset after 20 s (was 16 s); Patch 12.0.0: creatures no longer
  immune after 3 stuns, duration halves to 1/16.
  <https://warcraft.wiki.gg/wiki/Diminishing_returns>
- Icy Veins, *Class Changes, Diminishing Returns: Midnight 12.1 PTR
  Development Notes, June 30th* — reset timer 16 s -> 20 s.
  <https://www.icy-veins.com/wow/news/class-changes-diminishing-returns-midnight-12-1-ptr-development-notes-june-30th>
- Maxroll, *Crowd Control Diminishing Returns 12.1.5* — application table and
  ability durations (Kidney Shot 2–6 s, Blind 8 s, Gouge 4 s, Sap 60 s PvE).
  Its "16 s" sentence is the stale 12.0 value; the 12.1 change above is newer.
  <https://maxroll.gg/wow/resources/crowd-control-diminishing-returns>
- Blizzard, *12.1.5 Content Update Notes* — no DR change in 12.1.5.
  <https://news.blizzard.com/en-us/article/24304162/12-1-5-content-update-notes>

## What is deliberately not claimed

- The game's native DR tracker (Options > Gameplay > Gameplay Enhancements >
  Diminishing Returns) is not readable by addons.
- Target auras are secret/display-only in instances in 12.1.5; the combat log
  is unavailable. Another player's Polymorph/Blind cannot be observed. The
  manual CC-hold hotkey/button is the honest fallback for party CC.
- DR memory is per category, not per target GUID. It clears on target loss and
  out of combat; a direct tab-target swap can carry the estimate to the next
  creature until the 20 s window expires. The two-stun conservative cap keeps
  that safe.
