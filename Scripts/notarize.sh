#!/bin/zsh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
"$ROOT/Scripts/build-app.sh"
APP="$ROOT/island/MochiIsland.app"
DMG="$ROOT/island/MochiIsland.dmg"
if [[ -z "${DEVELOPER_ID_APP:-}" ]]; then
  echo "DEVELOPER_ID_APP is not set. build-app.sh only ad-hoc signs; that DMG will not pass Gatekeeper for other Macs."
  echo "Shipping checklist:"
  echo "  1. Apple Developer Program membership + Developer ID Application certificate in Keychain"
  echo "  2. export DEVELOPER_ID_APP='Developer ID Application: Name (TEAMID)'"
  echo "  3. xcrun notarytool store-credentials  # then export NOTARY_PROFILE=<that profile name>"
  echo "  4. Run this script again to sign, build the DMG, notarize, and staple"
  echo "Do not ship the ad-hoc signature from build-app.sh."
  exit 0
fi
codesign --force --deep --options runtime --timestamp --sign "$DEVELOPER_ID_APP" "$APP"
rm -f "$DMG"
hdiutil create -volname "Mochi Island" -srcfolder "$APP" -ov -format UDZO "$DMG"
if [[ -n "${NOTARY_PROFILE:-}" ]]; then
  xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG"
  echo "Notarized $DMG"
else
  echo "Signed $DMG. Set NOTARY_PROFILE to submit it to Apple."
fi
