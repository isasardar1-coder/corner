#!/bin/bash
# Builds Corner.app (native Swift, one universal binary for Apple Silicon and Intel), signs it ad hoc, zips it to
# out/Corner-Mac-Swift.zip, then renders the panel in a set of states to out/demo-<mode>.png (test renders, each in
# a throwaway data folder). Run on a Mac with Xcode 15 or later:  bash package.sh   (DEMOS=0 skips the renders)
set -euo pipefail
cd "$(dirname "$0")"

swift build -c release --arch arm64 --arch x86_64 --product Corner
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"

rm -rf out
APP=out/Corner.app
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN/Corner" "$APP/Contents/MacOS/Corner"
cp Sources/CornerApp/Resources/*.ttf Sources/CornerApp/Resources/ping.wav Sources/CornerApp/Resources/logo.png "$APP/Contents/Resources/"

# the icon: drawn by the app itself, then cut into an .icns
ICON=out/icon
mkdir -p "$ICON.iconset"
"$APP/Contents/MacOS/Corner" --icon "$ICON.png"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ICON.png" --out "$ICON.iconset/icon_${s}x${s}.png" >/dev/null
  sips -z $((s * 2)) $((s * 2)) "$ICON.png" --out "$ICON.iconset/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICON.iconset" -o "$APP/Contents/Resources/Corner.icns"
rm -rf "$ICON.iconset" "$ICON.png"

VERSION="${CORNER_VERSION:-1.0}"
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Corner</string>
  <key>CFBundleDisplayName</key><string>Corner</string>
  <key>CFBundleIdentifier</key><string>app.corner.todo.swift</string>
  <key>CFBundleExecutable</key><string>Corner</string>
  <key>CFBundleIconFile</key><string>Corner</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Corner</string>
</dict>
</plist>
EOF

codesign --force --deep --sign - "$APP"
ditto -c -k --keepParent "$APP" out/Corner-Mac-Swift.zip
lipo -info "$APP/Contents/MacOS/Corner"
ls -la out

[ "${DEMOS:-1}" = "0" ] && exit 0

# test renders: each mode starts from an empty data folder. Those from "launch" on show an animation as frames side by side.
MODES="${DEMO_MODES:-tasks all adding energy settings checkin feedback timeup debt nap glass rest full fullenergy fullplan fullapplied fullroutines fullsettings priority paused order habits planning onboard0 onboard1 onboard2 onboard3 rise launch launchclose hoveropen hoverclose fullswitch fullexit cycleopen cycleclose hoverback hoveragain fullearly hoverbottom}"
fail=0
for mode in $MODES; do
  data="$(mktemp -d)"
  png="$PWD/out/demo-$mode.png"
  CORNER_DATA="$data" "$APP/Contents/MacOS/Corner" --demo "$png" "$mode" & pid=$!
  for i in $(seq 1 45); do kill -0 $pid 2>/dev/null || break; sleep 1; done
  kill $pid 2>/dev/null || true
  wait $pid 2>/dev/null || true
  if [ -s "$png" ]; then echo "rendered $mode"; else echo "no render: $mode"; fail=1; fi
  [ -f "$data/error.log" ] && { echo "--- error.log ($mode)"; cat "$data/error.log"; }
  rm -rf "$data"
done
exit $fail
