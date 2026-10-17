#!/bin/sh
# Build the Mac companion: native capture library + Core + Avalonia UI,
# self-contained osx-arm64, assembled into a .app bundle under dist-mac/.
# The Windows build (WinForms) stays on Windows.
#
# Usage:  ./build-mac.sh            # arm64
#         RID=osx-x64 ./build-mac.sh
# Live retail run OWED (static build != automated test != live in-game E2E).
set -euo pipefail

DOTNET=${DOTNET:-/Users/andreas/.dotnet/dotnet}
RID=${RID:-osx-arm64}
ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$ROOT/dist-mac/MaxDPS Companion.app"

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
cp "$ROOT/dist-exp/settings.ini" "$APP/Contents/MacOS/settings.ini"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>MaxDPS Companion</string>
  <key>CFBundleDisplayName</key><string>MaxDPS Companion</string>
  <key>CFBundleIdentifier</key><string>com.maxdps.companion</string>
  <key>CFBundleVersion</key><string>3.7.9</string>
  <key>CFBundleShortVersionString</key><string>3.7.9 Unyielding (mac)</string>
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
