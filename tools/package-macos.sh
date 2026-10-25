#!/bin/sh
# Package the macOS release assets for an EXISTING release tag.
#
# One tag, one version, both clients: this script refuses to build unless the
# checked-out commit is the tag commit and app/Version.props matches the tag.
# The app bundle is signed BEFORE it is zipped, BUILD-INFO.txt records the
# provenance, and the resulting zip is extracted + signature-verified so a
# release asset can never be silently broken by packaging.
#
# Usage:
#   ./tools/package-macos.sh --tag v4.4.0 --out dist-mac/release
#   ./tools/package-macos.sh --tag v4.4.0 --out dist-mac/release --no-build
#   MAC_SIGN_MODE=adhoc ./tools/package-macos.sh --tag v4.4.0 --no-build
#
# Output:
#   MaxDpsCompanion-<version>-macos-<arch>.zip   the signed .app bundle
#   MaxDpsCompanionBridge-<version>-addon.zip   the matching addon + installer
#   BUILD-INFO-macos.txt                        provenance / parity metadata
#   SHA256SUMS-macos.txt                        checksums for the three files
#
# The Windows build is NOT produced here. It is the same product version and
# the same source tag, built by the Windows toolchain.
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
TAG=""
OUT="$ROOT/dist-mac/release"
APP=""
DO_BUILD=1

usage() {
    sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
    case "$1" in
        --tag)
            [ $# -ge 2 ] || { echo "package-macos: --tag needs a value" >&2; exit 2; }
            TAG="$2"; shift 2 ;;
        --out)
            [ $# -ge 2 ] || { echo "package-macos: --out needs a value" >&2; exit 2; }
            OUT="$2"; shift 2 ;;
        --app)
            [ $# -ge 2 ] || { echo "package-macos: --app needs a value" >&2; exit 2; }
            APP="$2"; shift 2 ;;
        --no-build)
            DO_BUILD=0; shift ;;
        -h|--help)
            usage; exit 0 ;;
        *)
            echo "package-macos: unknown argument '$1'" >&2
            usage >&2
            exit 2 ;;
    esac
done

fail() { echo "package-macos: $*" >&2; exit 1; }

[ -n "$TAG" ] || fail "--tag is required (explicit provenance: no guessing from a branch)"

VERSION_PROPS="$ROOT/app/Version.props"
VERSION=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$VERSION_PROPS" | head -1 | tr -d '[:space:]')
CODENAME=$(sed -n 's:.*<Codename>\([^<]*\)</Codename>.*:\1:p' "$VERSION_PROPS" | head -1 | tr -d '[:space:]')
[ -n "$VERSION" ] || fail "cannot read <Version> from $VERSION_PROPS"
[ -n "$CODENAME" ] || fail "cannot read <Codename> from $VERSION_PROPS"

TAG_VERSION=${TAG#v}
[ "$TAG_VERSION" = "$VERSION" ] || fail "tag '$TAG' does not match app/Version.props <Version>$VERSION</Version>"

TAG_COMMIT=$(git -C "$ROOT" rev-parse -q --verify "refs/tags/$TAG^{commit}" || true)
[ -n "$TAG_COMMIT" ] || fail "tag '$TAG' not found; fetch tags (git fetch --tags) first"
HEAD_COMMIT=$(git -C "$ROOT" rev-parse HEAD)
[ "$HEAD_COMMIT" = "$TAG_COMMIT" ] || fail "HEAD is $HEAD_COMMIT, not '$TAG' ($TAG_COMMIT); check out the tag before packaging"

ADDON_SRC="$ROOT/addon/MaxDpsCompanionBridge"
ADDON_TOC="$ADDON_SRC/MaxDpsCompanionBridge.toc"
[ -f "$ADDON_TOC" ] || fail "addon TOC missing: $ADDON_TOC"
ADDON_VERSION=$(sed -n 's/^## Version:[[:space:]]*//p' "$ADDON_TOC" | head -1 | tr -d '[:space:]')
[ "$ADDON_VERSION" = "$VERSION" ] || fail "addon TOC version '$ADDON_VERSION' != product version '$VERSION'"

if [ "$DO_BUILD" = 1 ]; then
    echo "== building $TAG ($VERSION $CODENAME) =="
    ( cd "$ROOT" && ./build-mac.sh )
fi

[ -n "$APP" ] || APP="$ROOT/dist-mac/MaxDPS Companion.app"
[ -d "$APP" ] || fail "app bundle not found: $APP"
[ -f "$APP/Contents/Info.plist" ] || fail "not an app bundle: $APP"

# A release must ship the tracked default, never a developer's dist/settings.ini
# (CellSize/offsets/hotkeys are per-machine and would silently misconfigure a
# fresh install).
BUNDLE_SETTINGS="$APP/Contents/MacOS/settings.ini"
[ -f "$BUNDLE_SETTINGS" ] || fail "bundle has no settings.ini seed: $BUNDLE_SETTINGS"
if ! cmp -s "$BUNDLE_SETTINGS" "$ROOT/settings.ini"; then
    fail "bundle settings.ini differs from the tracked default; run 'cp settings.ini dist/settings.ini' and rebuild"
fi

mkdir -p "$OUT"
rm -rf "$OUT/.addon-stage"
xattr -cr "$APP" 2>/dev/null || true

ARCH=$(uname -m)
case "$ARCH" in
    arm64) ARCH_LABEL=arm64 ;;
    x86_64) ARCH_LABEL=x64 ;;
    *) fail "unsupported build architecture: $ARCH" ;;
esac

COMMIT_SHORT=$(git -C "$ROOT" rev-parse --short=7 HEAD)
BUILT_AT=$(date -u +%Y-%m-%dT%H:%M:%SZ)
BUILT_ON=$(sw_vers -productVersion 2>/dev/null || echo unknown)
XCODE=$(xcodebuild -version 2>/dev/null | head -1 || echo unknown)
SWIFT=$(swiftc --version 2>/dev/null | head -1 || echo unknown)
DOTNET=$(dotnet --version 2>/dev/null || echo unknown)
MIN_MACOS=$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$APP/Contents/Info.plist" 2>/dev/null || echo 14.0)
BUNDLE_VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$APP/Contents/Info.plist" 2>/dev/null || echo unknown)
BUNDLE_DISPLAY=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist" 2>/dev/null || echo unknown)
[ "$BUNDLE_VERSION" = "$VERSION" ] || fail "bundle CFBundleVersion '$BUNDLE_VERSION' != '$VERSION'"
case "$BUNDLE_DISPLAY" in
    "$VERSION $CODENAME"*) ;;
    *) fail "bundle CFBundleShortVersionString '$BUNDLE_DISPLAY' does not start with '$VERSION $CODENAME'" ;;
esac

if [ "${MAC_SIGN_MODE:-local}" = "adhoc" ]; then
    SIGNING="adhoc (not notarized; Gatekeeper requires a first-launch override)"
else
    SIGNING="local self-signed identity, or ad-hoc fallback if creation failed (not notarized)"
fi
PACKAGING_WORKFLOW_SHA=${PACKAGING_WORKFLOW_SHA:-unknown}
PACKAGING_RUN=${PACKAGING_RUN:-local}

INFO="$OUT/BUILD-INFO-macos.txt"
{
    echo "product=MaxDPS Companion"
    echo "version=$VERSION"
    echo "codename=$CODENAME"
    echo "channel=stable"
    echo "platform=macOS"
    echo "arch=$ARCH_LABEL"
    echo "minimum_macos=$MIN_MACOS"
    echo "source_tag=$TAG"
    echo "source_commit=$HEAD_COMMIT"
    echo "source_commit_short=$COMMIT_SHORT"
    echo "packaging_workflow_sha=$PACKAGING_WORKFLOW_SHA"
    echo "packaging_run=$PACKAGING_RUN"
    echo "built_at=$BUILT_AT"
    echo "built_on=macOS $BUILT_ON"
    echo "xcode=$XCODE"
    echo "swift=$SWIFT"
    echo "dotnet=$DOTNET"
    echo "signing=$SIGNING"
    echo "notarized=no"
    echo "addon=MaxDpsCompanionBridge $ADDON_VERSION"
    echo "wire=v5 + Ext2/Ext3/Ext4 (51 cells)"
    echo "windows_counterpart=MaxDpsCompanion.exe ${VERSION}.0 (v$VERSION $CODENAME)"
    echo "parity=same source tag, Core brain, wire protocol and addon as Windows; OS-specific binaries are not byte-identical"
} > "$INFO"

# BUILD-INFO lives inside the bundle too, so a user can identify a build after
# it has been moved to /Applications. Adding it invalidates the signature, so
# sign AFTER writing it.
cp "$INFO" "$APP/Contents/Resources/BUILD-INFO.txt"
xattr -cr "$APP" 2>/dev/null || true
"$ROOT/tools/mac-signing.sh" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

APP_ZIP="$OUT/MaxDpsCompanion-$VERSION-macos-$ARCH_LABEL.zip"
rm -f "$APP_ZIP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$APP_ZIP"

# Prove the shipped zip is intact: extract with ditto (preserves symlinks and
# exec bits), verify the signature again, and require the launcher executable.
TMP=$(mktemp -d "${TMPDIR:-/tmp}/mdc-package.XXXXXX")
trap 'rm -rf "$TMP"' EXIT INT TERM
ditto -x -k "$APP_ZIP" "$TMP"
EXTRACTED="$TMP/$(basename "$APP")"
[ -d "$EXTRACTED" ] || fail "zip did not contain $(basename "$APP")"
codesign --verify --deep --strict "$EXTRACTED"
[ -x "$EXTRACTED/Contents/MacOS/MaxDpsCompanion.Mac" ] || fail "launcher is not executable after unzip"
[ -f "$EXTRACTED/Contents/MacOS/libmdc_capture.dylib" ] || fail "capture library missing from the bundle"
[ -f "$EXTRACTED/Contents/Resources/BUILD-INFO.txt" ] || fail "BUILD-INFO missing from the bundle"
rm -rf "$TMP"
trap - EXIT INT TERM

# Addon package: same folder Windows installs, plus a double-click installer.
STAGE="$OUT/.addon-stage/MaxDpsCompanionBridge-$VERSION-addon"
mkdir -p "$STAGE"
cp -R "$ADDON_SRC" "$STAGE/"
cp "$ROOT/tools/install-addon-mac.sh" "$STAGE/install-addon-mac.sh"
chmod +x "$STAGE/install-addon-mac.sh"
cat > "$STAGE/Install Addon.command" <<'COMMAND'
#!/bin/sh
cd "$(dirname "$0")" || exit 1
./install-addon-mac.sh "$@"
status=$?
printf '\nPress Return to close...'
read _ || true
exit $status
COMMAND
chmod +x "$STAGE/Install Addon.command"
cat > "$STAGE/README-addon.txt" <<TEXT
MaxDps Companion Bridge $VERSION "$CODENAME" - macOS package

This is the SAME addon the Windows $VERSION companion uses (folder
MaxDpsCompanionBridge, TOC version $VERSION).

Install
  1. Double-click "Install Addon.command" (Terminal opens), or run:
       ./install-addon-mac.sh --addons "/Applications/World of Warcraft/_retail_/Interface/AddOns"
  2. In the WoW AddOns list, enable "MaxDps Companion Bridge" and disable the
     legacy "MaxDpsBridge" for that character if it is present.
  3. /reload, then /mcb help to confirm.

The companion cannot work without this addon loaded in the same client.
TEXT

ADDON_ZIP="$OUT/MaxDpsCompanionBridge-$VERSION-addon.zip"
rm -f "$ADDON_ZIP"
ditto -c -k --sequesterRsrc --keepParent "$STAGE" "$ADDON_ZIP"
rm -rf "$OUT/.addon-stage"

( cd "$OUT" && shasum -a 256 \
    "$(basename "$APP_ZIP")" \
    "$(basename "$ADDON_ZIP")" \
    "$(basename "$INFO")" > SHA256SUMS-macos.txt )

echo
echo "package-macos: $VERSION $CODENAME ($TAG @ $COMMIT_SHORT, macOS $ARCH_LABEL)"
ls -lh "$APP_ZIP" "$ADDON_ZIP" "$INFO" "$OUT/SHA256SUMS-macos.txt"
