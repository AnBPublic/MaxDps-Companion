#!/bin/sh
# Install the MaxDps Companion Bridge addon on macOS.
#
# Works from a repo checkout (addon/MaxDpsCompanionBridge) or from the release
# addon zip (MaxDpsCompanionBridge next to this script). It only replaces the
# MaxDpsCompanionBridge folder; every other addon, including the legacy
# MaxDpsBridge and its SavedVariables, is left untouched.
#
# Usage:
#   ./tools/install-addon-mac.sh
#   ./tools/install-addon-mac.sh --addons "/Applications/World of Warcraft/_retail_/Interface/AddOns"
#   ./tools/install-addon-mac.sh --dry-run
#
# Exit codes: 0 installed, 2 bad usage / AddOns folder or addon source missing.
set -eu

ADDONS=""
DRY_RUN=0
SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)

usage() {
    sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
    case "$1" in
        --addons)
            [ $# -ge 2 ] || { echo "install-addon-mac: --addons needs a path" >&2; exit 2; }
            ADDONS="$2"; shift 2 ;;
        --dry-run)
            DRY_RUN=1; shift ;;
        -h|--help)
            usage; exit 0 ;;
        *)
            echo "install-addon-mac: unknown argument '$1'" >&2
            usage >&2
            exit 2 ;;
    esac
done

if [ -d "$SCRIPT_DIR/MaxDpsCompanionBridge" ]; then
    SOURCE="$SCRIPT_DIR/MaxDpsCompanionBridge"
elif [ -d "$SCRIPT_DIR/../addon/MaxDpsCompanionBridge" ]; then
    SOURCE="$SCRIPT_DIR/../addon/MaxDpsCompanionBridge"
else
    echo "install-addon-mac: MaxDpsCompanionBridge folder not found next to this script" >&2
    echo "  run it from the release addon zip or from a repo checkout (tools/)" >&2
    exit 2
fi

SOURCE_TOC="$SOURCE/MaxDpsCompanionBridge.toc"
[ -f "$SOURCE_TOC" ] || { echo "install-addon-mac: missing $SOURCE_TOC" >&2; exit 2; }
VERSION=$(sed -n 's/^## Version:[[:space:]]*//p' "$SOURCE_TOC" | head -1 | tr -d '[:space:]')
INTERFACE=$(sed -n 's/^## Interface:[[:space:]]*//p' "$SOURCE_TOC" | head -1 | tr -d '[:space:]')

if [ -z "$ADDONS" ]; then
    for candidate in \
        "/Applications/World of Warcraft/_retail_/Interface/AddOns" \
        "$HOME/Applications/World of Warcraft/_retail_/Interface/AddOns"
    do
        if [ -d "$candidate" ]; then
            ADDONS="$candidate"
            break
        fi
    done
fi

if [ -z "$ADDONS" ] || [ ! -d "$ADDONS" ]; then
    echo "install-addon-mac: WoW AddOns folder not found." >&2
    echo "  Pass it explicitly, for example:" >&2
    echo "    ./install-addon-mac.sh --addons \"/Applications/World of Warcraft/_retail_/Interface/AddOns\"" >&2
    echo >&2
    echo "  Detected Interface/AddOns folders (if any):" >&2
    for root in /Applications "$HOME/Applications"; do
        [ -d "$root" ] && find "$root" -maxdepth 5 -type d -name AddOns 2>/dev/null || true
    done
    exit 2
fi

TARGET="$ADDONS/MaxDpsCompanionBridge"
echo "Addon:  MaxDpsCompanionBridge $VERSION (Interface $INTERFACE)"
echo "From:   $SOURCE"
echo "To:     $TARGET"

if [ "$DRY_RUN" = 1 ]; then
    echo "Dry run: nothing copied."
    exit 0
fi

mkdir -p "$TARGET"
if ! cp -R "$SOURCE/." "$TARGET/"; then
    echo "install-addon-mac: copy failed (permissions?). Re-run with a writable --addons path or sudo." >&2
    exit 1
fi

[ -f "$TARGET/MaxDpsCompanionBridge.toc" ] || {
    echo "install-addon-mac: install produced no TOC at $TARGET" >&2
    exit 1
}

echo "Installed MaxDpsCompanionBridge $VERSION."
if [ -d "$ADDONS/MaxDpsBridge" ]; then
    echo "Legacy MaxDpsBridge is present (left untouched). Disable it for this character in the AddOns list."
fi
echo "Next:"
echo "  1. Enable 'MaxDps Companion Bridge' in the WoW AddOns list."
echo "  2. /reload"
echo "  3. /mcb help   (legacy aliases /mdb and /mdbx still work)"
