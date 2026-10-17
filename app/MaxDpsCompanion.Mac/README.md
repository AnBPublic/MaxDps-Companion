# MaxDPS Companion — macOS

The MaxDPS Companion for macOS: same decision brain as the Windows app
(`app/MaxDpsCompanion.Core`, a portable port of the C# rotation/decision
sources) with native macOS capture and input.

Target: **retail Midnight 12.1** + the **`MaxDpsBridgeExp`** addon
(`addon/MaxDpsBridgeExp`, `/mdbx`). The frozen legacy `MaxDpsBridge` is not the
target of the Mac client.

```
app/MaxDpsCompanion.Core/   portable brain (PixelProtocol, Decision,
                            Scheduler, Knowledge, Telemetry, RotationEngine)
app/MaxDpsCompanion.Mac/    macOS host: Avalonia UI, ScreenCaptureKit capture,
                            CGEvent input, CLI (--probe/--locate/--run/--bench-capture)
app/MaxDpsCompanion.Mac/native/MdcCapture.swift
                            the entire native capture surface (C ABI over
                            ScreenCaptureKit); built by build-mac.sh
```

## Build & install

```sh
./build-mac.sh                                   # arm64; RID=osx-x64 for Intel
rm -rf "/Applications/MaxDPS Companion.app"
cp -R "dist-mac/MaxDPS Companion.app" /Applications/
open "/Applications/MaxDPS Companion.app"
```

`build-mac.sh` compiles `native/MdcCapture.swift` into
`libmdc_capture.dylib`, builds Core + the host, assembles the bundle and signs
it with **`tools/mac-signing.sh`** (a local self-signed identity, created once
in the login keychain). The stable identity matters: macOS TCC keys the Screen
Recording / Accessibility grants on the signing identity, so with the local
identity your grants survive rebuilds — with an ad-hoc signature they would be
lost on every rebuild (which silently looks like "the companion stopped
working").

Before the app build the script also compiles and runs
`native/selftest.swift` against the fresh dylib (synthetic strip: capture,
43-cell pixel check, per-sample read cost). A compile error fails the build; a
runtime `FAIL` normally means the *terminal* has no Screen Recording grant —
grant it and re-run, or set `SELFTEST=0`. The bundle version comes from
`app/Version.props` (never hardcoded).

### Signing & notarization

| Path | Identity | Used for |
| :--- | :--- | :--- |
| local dev | `MaxDPS Companion Local` (self-signed, `tools/mac-signing.sh`) | your own machine; TCC grants survive rebuilds |
| release | `Developer ID Application: <Team> (<TEAMID>)` + hardened runtime | anything you hand to someone else; must be notarized |

Release steps (manual, on a Mac with the Developer ID in the keychain):

```sh
# 1. Hardened runtime + Developer ID (notarization rejects otherwise).
#    Add --entitlements <file> only if a runtime component needs JIT/unsigned
#    memory (the .NET runtime ships signed; no file is committed for this).
codesign --force --deep --options runtime \
  --sign "Developer ID Application: <Team Name> (<TEAMID>)" \
  "dist-mac/MaxDPS Companion.app"

# 2. Notarize the zipped bundle and staple the ticket
ditto -c -k --keepParent "dist-mac/MaxDPS Companion.app" dist-mac/MaxDPS-Companion.zip
xcrun notarytool submit dist-mac/MaxDPS-Companion.zip \
  --keychain-profile maxdps-notary --wait
xcrun stapler staple "dist-mac/MaxDPS Companion.app"

# 3. Verify what Gatekeeper will see
spctl -a -vvv -t exec "dist-mac/MaxDPS Companion.app"
```

`notarytool` needs an app-specific password (or API key) stored once:
`xcrun notarytool store-credentials maxdps-notary --apple-id <id> --team-id <TEAMID>`.
The entitlements file is only required if a runtime component is unsigned
(the .NET runtime ships signed); create it empty if `codesign` asks for it.
**Notarization is OWED** until it is actually run — a local ad-hoc/self-signed
build proves nothing about Gatekeeper.

## Permissions (required, both are per-app)

| Permission | Needed for | Where |
| :--- | :--- | :--- |
| **Screen Recording** | reading the pixel strip | System Settings → Privacy & Security → Screen Recording |
| **Accessibility** | sending the key strokes | System Settings → Privacy & Security → Accessibility |

The app prompts once via the OS when a capture is first attempted. `--probe`
never prompts. Without Screen Recording the stream cannot start (the app says
so; the capture backend reports `NOT granted`) and **Start is refused** — the
status line shows the missing grant and the engine is never started
(`--run` exits with code 2). Without Accessibility the rotation still starts
and holds every send until granted (system input is delivered per-process;
without the grant the events are dropped by TCC).

## Capture design (performance contract)

The macOS client must not cost the game frames. Concretely:

* **One persistent ScreenCaptureKit stream**, filtered to the **WoW window**
  (`SCContentFilter(desktopIndependentWindow:)`), never a screen recording.
* **Bounded region**: the stream is configured with a `sourceRect` that starts
  as a small locate band (1024×128 pt at the window's top-left corner) and
  **shrinks to the discovered strip rect** (43 cells × cellSize + margin —
  for the default 8 px cells that is ~352×16 pt ≈ 22 KB per frame). "As small
  as possible, as big as necessary."
* **No capture call on the tick path.** The window server pushes frames
  asynchronously; the engine's sample is a seqlock read of the newest frame
  (no syscall, no lock, no allocation). `queueDepth = 2`, so a busy consumer
  drops frames instead of back-pressuring capture — the game is never blocked.
* **Window-relative, screen-agnostic**: every coordinate is relative to the WoW
  window, so the same strip position works on the 1080p desktop monitor and on
  the MacBook panel, at any resolution or display scale (scale is read per
  display and applied as points → device pixels).
* **Adaptive**: window moves/resolution switches/display changes rebuild the
  stream within a second (supervisor); a changed strip (addon update, new cell
  size, new offset) is re-located by the escalation ladder
  (micro band → wide band → 20%-tall band → full window), and the stream
  shrinks back to the new strip rect.
* **Idle costs nothing**: the stream exists only while the engine runs.

### Escalation / adaptive regions

| Stage | Region | When |
| :--- | :--- | :--- |
| steady state | strip rect + 2 pt margin | after a successful locate |
| locate rung 0 | 1024×128 pt, window top-left | startup, or after the strip disappears |
| locate rung 1 | 2048×400 pt | rung 0 missed |
| locate rung 2 | 2048 pt wide, 20% tall | rungs 0–1 missed |
| locate rung 3 | full window | everything else missed |

Steps are one-shot captures run **only** while the strip is missing (the engine
throttles relocation to once per 5 s), and the rung is remembered so repeated
misses stay cheap.

## Colour pipeline note (macOS-specific)

macOS colour-manages window content through the display's ICC profile, and
saturated colours can be gamut-mapped: a channel that the addon painted as 0
can come back a few level-steps high (measured: a blue cell's green channel
+68/255 = 4 levels). That would fail the frame checksum and the companion
would sit on "no strip" forever.

`MacPixelRepair` handles it: when a frame does not decode, a small bounded set
of one-level corrections is tried on channels that look gamut-lifted, and the
first candidate whose frame **actually decodes (checksum valid)** wins.
Nothing is invented: the wire's own checksum is the acceptance oracle, and the
exact case never enters the path. `--probe` prints the repair count, so a
display that needs it is visible rather than silent.

## Input (key sending)

* Keyboard: **`CGEventPostToPid`** — the stroke goes into the game's own event
  stream. The game needs no focus and strokes can never leak into another app
  (the macOS counterpart of the Windows `PostMessage` route). There is **no
  global fallback**: a keyboard stroke with no target pid is dropped, and a VK
  the Mac cannot map is reported once per key (`unmapped VK n`) instead of
  being silently skipped.
* Mouse / wheel: global HID events (there is no per-process equivalent the game
  reads correctly), so the engine's foreground gate stays mandatory for them —
  identical to the Windows hybrid.

## CLI

```sh
"/Applications/MaxDPS Companion.app/Contents/MacOS/MaxDpsCompanion.Mac" --probe
```

| Flag | What it does |
| :--- | :--- |
| *(none)* / `--ui` | opens the Avalonia window (Start/Pause/Stop, Launch Game, Open Folder, a minimal settings block for the pause / target / interact key — saved to `settings.ini` on "Save settings" and on close, status) |
| `--probe` | read-only report → `~/Library/Application Support/MaxDpsCompanion/probe.txt`: process/window/scale, capture backend, permissions, stream stats, locate + decode result |
| `--locate` | strip sweep only |
| `--run` | headless rotation loop (Ctrl+C stops) |
| `--bench-capture[=seconds]` | hard numbers while playing: locate time, steady-state region size, per-sample µs (mean/p50/p95/p99/max), managed allocations, our CPU share, delivered fps |

Settings live in `~/Library/Application Support/MaxDpsCompanion/settings.ini`
(seeded from the bundle; the `dist-exp` defaults are used: `InGameConfigMode=1`).

## Troubleshooting

| Symptom | Check |
| :--- | :--- |
| `capture-backend=... NOT granted` | grant Screen Recording, then Start again |
| `locate=null` | is the Exp bridge loaded (`/mdbx status`)? is `MaxDpsBridge` (stable) disabled? — the Exp bridge is inert while the stable one is loaded |
| decode works but nothing fires | Accessibility grant; `IsForeground` for mouse slots; watch the status line's "why" |
| stutter / fps loss | run `--bench-capture`: steady-state region should be ~20-30 KB/frame; our CPU should stay near 1 % of one core |
| a frame decodes only sometimes | check the repair count in `--probe`; a high count means the display's ICC profile is distorting the strip |

## Status / owed

Offline: Core builds 0 warnings / 0 errors; the native library builds with
`swiftc`; the capture self-test (`native/selftest.swift`) verifies pattern
pixels, region reconfiguration and read cost against a synthetic window.

**Live retail checks stay OWED until run in a real client** (see
`docs/TESTING.md` §4): locate + decode + send in-game, focus behaviour,
repair counts on the real display, and the fade/overlay interactions.
