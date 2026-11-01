#!/bin/zsh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
if ! xcodebuild -version >/dev/null 2>&1; then
  for candidate in \
    "/Applications/Xcode.app/Contents/Developer" \
    "/Applications/Xcode-beta.app/Contents/Developer" \
    "$HOME/Downloads/Xcode.app/Contents/Developer" \
    "$HOME/Downloads/Xcode-beta.app/Contents/Developer"
  do
    if [[ -x "$candidate/usr/bin/xcodebuild" ]]; then
      export DEVELOPER_DIR="$candidate"
      break
    fi
  done
fi
xcodebuild -version
DERIVED="$ROOT/.build/XcodeDerivedData"
LOG="$DERIVED/build-app.log"
mkdir -p "$DERIVED"
# Same compile as Xcode Cmd+B on Package.swift (not a SwiftPM-only substitute).
xcodebuild \
  -scheme MochiIsland \
  -destination "generic/platform=macOS" \
  -configuration Release \
  -derivedDataPath "$DERIVED" \
  -quiet \
  build | tee "$LOG"
xcodebuild \
  -scheme MochiHookCLI \
  -destination "generic/platform=macOS" \
  -configuration Release \
  -derivedDataPath "$DERIVED" \
  -quiet \
  build | tee -a "$LOG"
BIN="$DERIVED/Build/Products/Release"
APP="$ROOT/island/MochiIsland.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Web/pets" "$APP/Contents/Resources/Web/status"
cp "$BIN/MochiIsland" "$BIN/MochiHookCLI" "$APP/Contents/MacOS/"
cp "$ROOT/island/Info.plist" "$APP/Contents/Info.plist"
if [[ -f "$ROOT/island/AppIcon.icns" ]]; then
  cp "$ROOT/island/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
fi
cp "$ROOT/web/island.html" "$ROOT/web/island.css" "$ROOT/web/island.js" "$APP/Contents/Resources/Web/"
cp "$ROOT/web/pets/"* "$APP/Contents/Resources/Web/pets/"
cp "$ROOT/web/status/"*.gif "$APP/Contents/Resources/Web/status/"
chmod +x "$APP/Contents/MacOS/MochiIsland" "$APP/Contents/MacOS/MochiHookCLI"
codesign --force --deep --sign - "$APP" >/dev/null
echo "Built $APP"
