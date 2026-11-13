#!/usr/bin/env bash
# Sign and notarize Mixdeck.app when Apple credentials are present.
# Without them the package stays unsigned and this script exits 0.
set -euo pipefail

app="${1:-artifacts/Mixdeck.app}"

if [[ -z "${APPLE_SIGNING_IDENTITY:-}" ]]; then
  echo "No APPLE_SIGNING_IDENTITY. The macOS package is unsigned."
  exit 0
fi

if [[ ! -d "$app" ]]; then
  echo "App not found: $app" >&2
  exit 1
fi

echo "Signing $app with $APPLE_SIGNING_IDENTITY"
codesign --force --options runtime --timestamp --deep --sign "$APPLE_SIGNING_IDENTITY" "$app"
codesign --verify --strict --verbose=2 "$app"

if [[ -z "${APPLE_NOTARY_KEY:-}" || -z "${APPLE_NOTARY_KEY_ID:-}" || -z "${APPLE_NOTARY_ISSUER:-}" ]]; then
  echo "Signed, not notarized. Set APPLE_NOTARY_KEY, APPLE_NOTARY_KEY_ID, and APPLE_NOTARY_ISSUER to notarize."
  exit 0
fi

zip="artifacts/Mixdeck-notarize.zip"
rm -f "$zip"
ditto -c -k --keepParent "$app" "$zip"
xcrun notarytool submit "$zip" \
  --key "$APPLE_NOTARY_KEY" \
  --key-id "$APPLE_NOTARY_KEY_ID" \
  --issuer "$APPLE_NOTARY_ISSUER" \
  --wait
xcrun stapler staple "$app"
echo "Notarized: $app"
