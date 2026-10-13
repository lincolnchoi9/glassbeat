#!/bin/bash
# Builds mac/build/PrismPlay.app (universal, ad-hoc signed) and mac/build/PrismPlay-mac-<version>.zip.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(node -p "require('$ROOT/frontend/package.json').version")"

(cd "$ROOT/frontend" && npm ci && npm run build)

cd "$ROOT/mac"
swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"

APP="$ROOT/mac/build/PrismPlay.app"
rm -rf "$ROOT/mac/build"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN/PrismPlay" "$APP/Contents/MacOS/PrismPlay"
cp -R "$ROOT/frontend/dist" "$APP/Contents/Resources/Web"
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>io.github.lincolnchoi9.prismplay</string>
  <key>CFBundleName</key><string>PrismPlay</string>
  <key>CFBundleDisplayName</key><string>PrismPlay</string>
  <key>CFBundleExecutable</key><string>PrismPlay</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppleEventsUsageDescription</key><string>PrismPlay shows and controls what's playing in Spotify.</string>
</dict>
</plist>
EOF
plutil -lint "$APP/Contents/Info.plist"
codesign --force --deep -s - "$APP"
lipo -info "$APP/Contents/MacOS/PrismPlay"
ditto -c -k --keepParent "$APP" "$ROOT/mac/build/PrismPlay-mac-$VERSION.zip"
echo "Built $ROOT/mac/build/PrismPlay-mac-$VERSION.zip"
