#!/bin/sh
# Stable code signing for the Mac companion.
#
# WHY: macOS TCC (Screen Recording / Accessibility) keys an app's grant on its
# code-signing identity. An AD-HOC signature ("codesign -s -") has no stable
# identity, so the grant is bound to the build's cdhash — every rebuild looks
# like a brand-new app and the user has to re-grant (or silently loses capture,
# which reads as "the companion stopped working").
#
# This script creates (once) a local self-signed code-signing certificate in
# the login keychain and reuses it for every build. The certificate never
# leaves this machine, is not a secret, and is only used to sign this app
# bundle. If creation is not possible (locked keychain, sandbox), callers fall
# back to ad-hoc signing with a warning.
#
# Usage:  tools/mac-signing.sh <app-bundle>       # signs, prints identity
#         tools/mac-signing.sh --identity         # prints the SHA-1 hash
#
# RELEASE SIGNING (separate, manual; NOT done by this script):
#   The local identity above is for development only — it is not trusted by
#   Gatekeeper and cannot be notarized. A distributable build uses an Apple
#   Developer ID (add --entitlements <file> only if a runtime component needs
#   JIT/unsigned memory; the .NET runtime ships signed):
#     codesign --force --deep --options runtime \
#       --sign "Developer ID Application: <Team Name> (<TEAMID>)" \
#       "dist-mac/MaxDPS Companion.app"
#     xcrun notarytool submit <zip-or-dmg> --keychain-profile maxdps-notary --wait
#     xcrun stapler staple "dist-mac/MaxDPS Companion.app"
#   Hardened runtime (--options runtime) is REQUIRED for notarization.
#   Notarization itself stays OWED until it is run on a Mac with the Developer
#   ID in the keychain. See app/MaxDpsCompanion.Mac/README.md.
set -eu

IDENTITY_NAME="MaxDPS Companion Local"
KEYCHAIN="${HOME}/Library/Keychains/login.keychain-db"
MARKER="${HOME}/.maxdps-mac-signing-identity"

# Identity hashes (SHA-1) of every codesigning identity with our name.
# NOTE: a self-signed certificate is never "valid" for `find-identity -v`
# (it has no trust chain), so the non-verbose listing is the correct query.
list_hashes() {
    security find-identity -p codesigning 2>/dev/null \
        | grep -F "\"${IDENTITY_NAME}\"" \
        | sed -E 's/^[[:space:]]*[0-9]+\)[[:space:]]*([0-9A-Fa-f]+).*/\1/' \
        | grep -E '^[0-9A-Fa-f]+$' || true
}

create_identity() {
    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    openssl req -x509 -newkey rsa:2048 -sha256 -days 3650 -nodes \
        -keyout "$tmp/key.pem" -out "$tmp/cert.pem" \
        -subj "/CN=${IDENTITY_NAME}/O=MaxDPS Companion/C=US" \
        -addext "keyUsage=critical,digitalSignature" \
        -addext "extendedKeyUsage=critical,codeSigning" >/dev/null 2>&1 || return 1
    if ! openssl pkcs12 -export -legacy -out "$tmp/id.p12" \
            -inkey "$tmp/key.pem" -in "$tmp/cert.pem" -passout pass:maxdps >/dev/null 2>&1; then
        openssl pkcs12 -export -out "$tmp/id.p12" \
            -inkey "$tmp/key.pem" -in "$tmp/cert.pem" -passout pass:maxdps >/dev/null 2>&1 || return 1
    fi
    # -T codesign: the key may be used by codesign; -A: without prompting.
    security import "$tmp/id.p12" -k "$KEYCHAIN" -P maxdps -T /usr/bin/codesign -A >/dev/null 2>&1 || return 1
    : > "$MARKER"
    return 0
}

hashes=$(list_hashes)
count=$(printf '%s\n' "$hashes" | grep -c . || true)

if [ "$count" -eq 0 ]; then
    if [ "${1:-}" = "--identity" ]; then
        printf ''
        exit 0
    fi
    if create_identity; then
        hashes=$(list_hashes)
        count=$(printf '%s\n' "$hashes" | grep -c . || true)
        [ "$count" -gt 0 ] && echo "mac-signing: created local code-signing identity '${IDENTITY_NAME}'" >&2
    fi
elif [ "$count" -gt 1 ] && [ -f "$MARKER" ]; then
    # Duplicates of an identity we created: keep the first, drop the rest so
    # signing by name stays unambiguous for other tools.
    first=$(printf '%s\n' "$hashes" | head -1)
    printf '%s\n' "$hashes" | tail -n +2 | while read -r h; do
        [ -n "$h" ] && security delete-identity -c "$IDENTITY_NAME" -Z "$h" "$KEYCHAIN" >/dev/null 2>&1 || true
    done
    hashes="$first"
    count=1
fi

identity=$(printf '%s\n' "$hashes" | head -1)

if [ "${1:-}" = "--identity" ]; then
    printf '%s' "$identity"
    exit 0
fi

app="${1:-}"
if [ -z "$app" ]; then
    echo "usage: tools/mac-signing.sh <app-bundle>" >&2
    exit 2
fi

if [ -n "$identity" ]; then
    # Sign by SHA-1: unambiguous even if a same-named identity ever exists.
    if codesign --force --deep --sign "$identity" "$app" >/dev/null 2>&1; then
        echo "mac-signing: signed with '${IDENTITY_NAME}' (${identity:0:8}) - TCC grants survive rebuilds"
        exit 0
    fi
    echo "mac-signing: signing with '${IDENTITY_NAME}' failed; falling back to ad-hoc" >&2
fi

codesign --force --deep --sign - "$app" >/dev/null 2>&1 || true
echo "mac-signing: AD-HOC signed (Screen Recording must be re-granted after each rebuild)" >&2
exit 0
