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
BUILD_ROOT="$ROOT/.build/AppBuild"
LOG="$BUILD_ROOT/build-app.log"
mkdir -p "$BUILD_ROOT/ModuleCache"
export CLANG_MODULE_CACHE_PATH="$BUILD_ROOT/ModuleCache"
export SWIFTPM_MODULECACHE_OVERRIDE="$BUILD_ROOT/ModuleCache"
# This repository is a Swift package without an .xcodeproj or .xcworkspace.
# Xcode 27 does not accept the package directory as an xcodebuild project.
SWIFT_ARGS=(
  -c release
  --scratch-path "$BUILD_ROOT/SwiftPM"
  --cache-path "$BUILD_ROOT/cache"
  --config-path "$BUILD_ROOT/config"
  --security-path "$BUILD_ROOT/security"
  --manifest-cache local
)
if [[ "${MOCHI_RESTRICTED_BUILD:-0}" == "1" ]]; then
  SWIFT_ARGS+=(--disable-sandbox -debug-info-format none --disable-index-store)
fi
swift build "${SWIFT_ARGS[@]}" --product MochiIsland | tee "$LOG"
swift build "${SWIFT_ARGS[@]}" --product MochiHookCLI | tee -a "$LOG"
BIN="$BUILD_ROOT/SwiftPM/out/Products/Release"
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
version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")"
build="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$APP/Contents/Info.plist")"
echo "Built $APP"
echo "Version $version ($build)"
# Same bundle id would bring an older MochiIsland forward. Quit every copy, then open this path.
if pids="$(pgrep -x MochiIsland)"; then
  # shellcheck disable=SC2086
  kill $pids >/dev/null 2>&1 || true
  for _ in {1..50}; do
    pgrep -x MochiIsland >/dev/null || break
    sleep 0.1
  done
fi
if open "$APP"; then
  echo "Opened $APP"
else
  echo "Built successfully, but automatic opening failed; open $APP manually." >&2
fi
