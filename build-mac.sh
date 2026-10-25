#!/bin/sh
# Build the Mac companion: native capture library + Core + Avalonia UI,
# self-contained osx-arm64, assembled into a .app bundle under dist-mac/.
# The Windows build (WinForms) stays on Windows.
#
# Usage:  ./build-mac.sh            # arm64
#         RID=osx-x64 ./build-mac.sh
#         DOTNET=/path/to/dotnet ./build-mac.sh
#         SELFTEST=0 ./build-mac.sh # skip the native capture self-test
#
# Signing: tools/mac-signing.sh signs with the LOCAL self-signed identity (TCC
# grants survive rebuilds). A Developer ID + hardened-runtime + notarized
# release is a separate, manual step — see app/MaxDpsCompanion.Mac/README.md
# ("Signing & notarization"); notarization stays OWED until it is run.
#
# Live retail run OWED (static build != automated test != live in-game E2E).
set -euo pipefail

DOTNET=${DOTNET:-dotnet}
RID=${RID:-osx-arm64}
ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$ROOT/dist-mac/MaxDPS Companion.app"

# ---- version: single source of truth (app/Version.props), never hardcoded ----
VERSION_PROPS="$ROOT/app/Version.props"
APP_VERSION=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$VERSION_PROPS" | head -1 | tr -d '[:space:]')
CODENAME=$(sed -n 's:.*<Codename>\([^<]*\)</Codename>.*:\1:p' "$VERSION_PROPS" | head -1 | tr -d '[:space:]')
if [ -z "${APP_VERSION}" ] || [ -z "${CODENAME}" ]; then
  echo "build-mac: cannot read <Version>/<Codename> from $VERSION_PROPS" >&2
  exit 1
fi
echo "== version ${APP_VERSION} ${CODENAME} (from app/Version.props) =="

mkdir -p "$ROOT/dist-mac"

echo "== native capture library (ScreenCaptureKit) =="
# libmdc_capture.dylib is the whole Objective-C/ScreenCaptureKit surface. It
# is built from source on this machine (no prebuilt binary in the repo) and
# signed with the same identity as the app.
NATIVE_SRC="$ROOT/app/MaxDpsCompanion.Mac/native/MdcCapture.swift"
NATIVE_OUT="$ROOT/app/MaxDpsCompanion.Mac/native/libmdc_capture.dylib"
# -target pins the deployment target: an unpinned build inherits the builder's
# macOS and would fail to dlopen on the declared minimum (silently falling back
# to the obsolete blocking CGWindowListCreateImage path).
swiftc -O -emit-library -module-name mdc_capture \
  -target "$(uname -m)-apple-macos14.0" \
  -o "$NATIVE_OUT" "$NATIVE_SRC"
codesign --force --sign - "$NATIVE_OUT" >/dev/null 2>&1 || true

echo "== native self-test (synthetic strip: capture + pixel check + read cost) =="
# A compile failure is a real code error -> abort (set -e). A runtime FAIL
# usually means this terminal has no Screen Recording grant; that is reported
# loudly but does not block the build (SELFTEST=0 skips the step entirely).
if [ "${SELFTEST:-1}" != "0" ]; then
  SELFTEST_BIN="$ROOT/dist-mac/mdc-selftest"
  swiftc -O -target "$(uname -m)-apple-macos14.0" \
    -o "$SELFTEST_BIN" "$NATIVE_SRC" "$ROOT/app/MaxDpsCompanion.Mac/native/selftest.swift"
  # The self-test is a small AppKit app (it draws a synthetic window and
  # captures it), so bound it: a headless/CI session must never hang the build.
  "$SELFTEST_BIN" &
  selftest_pid=$!
  ( sleep 45; kill -9 "$selftest_pid" 2>/dev/null ) &
  watchdog_pid=$!
  if wait "$selftest_pid"; then
    echo "self-test: PASS"
  else
    echo "self-test: FAIL or timeout - grant Screen Recording to this terminal and re-run (SELFTEST=0 skips)" >&2
  fi
  kill "$watchdog_pid" 2>/dev/null || true
else
  echo "self-test: skipped (SELFTEST=0)"
fi

echo "== Core =="
"$DOTNET" build "$ROOT/app/MaxDpsCompanion.Core" -c Release --nologo -v q

echo "== Mac ($RID) =="
"$DOTNET" build "$ROOT/app/MaxDpsCompanion.Mac" -c Release -r "$RID" --nologo -v q

echo "== publish (self-contained) =="
"$DOTNET" publish "$ROOT/app/MaxDpsCompanion.Mac" \
  -c Release -r "$RID" --self-contained \
  -o "$ROOT/dist-mac/MaxDpsCompanion.Mac" --nologo -v q

echo "== assemble bundle =="
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$ROOT/dist-mac/MaxDpsCompanion.Mac/." "$APP/Contents/MacOS/"
cp "$NATIVE_OUT" "$APP/Contents/MacOS/libmdc_capture.dylib"
cp "$ROOT/app/MaxDpsCompanion.Mac/assets/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
cp "$ROOT/dist/settings.ini" "$APP/Contents/MacOS/settings.ini"

# Info.plist: version/codename expanded from app/Version.props (unquoted
# delimiter = shell expansion; the plist body contains no $ or backticks).
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>MaxDPS Companion</string>
  <key>CFBundleDisplayName</key><string>MaxDPS Companion</string>
  <key>CFBundleIdentifier</key><string>com.maxdps.companion</string>
  <key>CFBundleVersion</key><string>${APP_VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${APP_VERSION} ${CODENAME} (mac)</string>
  <key>CFBundleExecutable</key><string>MaxDpsCompanion.Mac</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSUIElement</key><false/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSScreenCaptureUsageDescription</key><string>MaxDPS Companion reads the MaxDpsBridge pixel strip from the WoW window to drive the rotation.</string>
</dict>
</plist>
PLIST

xattr -cr "$APP" 2>/dev/null || true
"$ROOT/tools/mac-signing.sh" "$APP"

echo "Built $APP"
echo "Install:  rm -rf \"/Applications/MaxDPS Companion.app\" && cp -R \"$APP\" /Applications/ && open \"/Applications/MaxDPS Companion.app\""
