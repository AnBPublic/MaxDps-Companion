# MaxDps-Companion agent rules

Read order: `HANDOVER.md` → `ARCHITECTURE.md` → `docs/PROTOCOL.md`.
Then the file you are changing plus its counterpart spec.

## Ownership

- `addon/MaxDpsBridge/*.lua` and `app/MaxDpsCompanion/*.cs` are owned by
  other agents. This scaffold covers repo-level files only; do not edit
  implementation files unasked.
- One exe: `MaxDpsCompanion.exe`. No alternate shells, no second binary.

## Hard rules

- Upstream MaxDps in `vendor/` is **read-only**. Never edit it, never import
  it as a dependency — the bridge queries the live addon at runtime.
- The bridge never calls protected Lua and never automates gameplay
  decisions; it only encodes what MaxDps already suggests.
- The companion never injects, never reads memory, never scrapes
  credentials. `PostMessage` keys to the game window only.
- Never claim live validation without a retail run: static (parses/builds)
  ≠ automated test ≠ live in-game E2E. A passing build never closes a
  "needs retail run" item.
- After any architecture change, update `HANDOVER.md` (status/next) and
  `ARCHITECTURE.md` (pipeline/file map) in the same pass.
- `settings.ini` default stays tracked; per-machine edits belong next to
  the exe in `dist\`, never committed.
