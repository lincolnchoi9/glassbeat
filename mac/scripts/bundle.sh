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
# App icon from assets/icon.png: Apple's grid (824 px tile on a transparent 1024 canvas), every iconset size, then the .icns.
ICONSET="$ROOT/mac/build/AppIcon.iconset"
mkdir -p "$ICONSET"
swift "$ROOT/mac/scripts/icon-canvas.swift" "$ROOT/assets/icon.png" "$ROOT/mac/build/icon-1024.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$ROOT/mac/build/icon-1024.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z "$((size * 2))" "$((size * 2))" "$ROOT/mac/build/icon-1024.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>io.github.lincolnchoi9.prismplay</string>
  <key>CFBundleName</key><string>PrismPlay</string>
  <key>CFBundleDisplayName</key><string>PrismPlay</string>
  <key>CFBundleExecutable</key><string>PrismPlay</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppleEventsUsageDescription</key><string>PrismPlay shows and controls what's playing in Spotify.</string>
  <key>NSAudioCaptureUsageDescription</key><string>PrismPlay reads Spotify's audio to draw the waveform. Nothing is recorded or sent.</string>
</dict>
</plist>
EOF
plutil -lint "$APP/Contents/Info.plist"
codesign --force --deep -s - "$APP"
lipo -info "$APP/Contents/MacOS/PrismPlay"
ditto -c -k --keepParent "$APP" "$ROOT/mac/build/PrismPlay-mac-$VERSION.zip"
echo "Built $ROOT/mac/build/PrismPlay-mac-$VERSION.zip"
