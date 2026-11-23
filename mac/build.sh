#!/bin/bash
# Baut "Claude Display.app" und installiert sie nach /Applications.
#   ./build.sh            bauen + installieren
#   ./build.sh --no-install
set -euo pipefail
cd "$(dirname "$0")"

APP="build/Claude Display.app"
VERSION="1.4.0"
# Update-Kanal: GitHub-Repo aus dem Git-Remote (owner/name), Firmware-Version aus dem Quelltext
REPO=$(git -C .. remote get-url origin 2>/dev/null | sed -E 's#.*github\.com[:/]##; s#\.git$##' || true)
FW_VERSION=$(grep -m1 'define FW_VERSION' ../firmware/src/main.cpp | cut -d'"' -f2)
# Python mit Pillow (für das App-Symbol): bevorzugt /usr/local/bin/python3, sonst python3 aus dem PATH
PY=${PYTHON:-$( [ -x /usr/local/bin/python3 ] && echo /usr/local/bin/python3 || command -v python3 )}

# Universal: läuft auf Apple-Silicon- und Intel-Macs
swift build -c release --arch arm64 --arch x86_64
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/apple/Products/Release/ClaudeDisplay "$APP/Contents/MacOS/ClaudeDisplay"
# Debug-Symbole entfernen – sie enthalten die lokalen Build-Pfade (inkl. Benutzername)
strip -S -x "$APP/Contents/MacOS/ClaudeDisplay"
if LC_ALL=C /usr/bin/grep -aqF "$HOME" "$APP/Contents/MacOS/ClaudeDisplay"; then
    echo "Fehler: Die App enthält noch lokale Pfade ($HOME)." >&2; exit 1
fi

# App-Symbol
if [ ! -f build/AppIcon.icns ]; then
    "$PY" make_icon.py build/icon_1024.png
    ICONSET=build/AppIcon.iconset
    mkdir -p "$ICONSET"
    for s in 16 32 128 256 512; do
        sips -z $s $s build/icon_1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
        sips -z $((s * 2)) $((s * 2)) build/icon_1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
    done
    iconutil -c icns "$ICONSET" -o build/AppIcon.icns
fi
cp build/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

# aktuelle Display-Firmware mitliefern
cp ../firmware/dist/display.bin.gz "$APP/Contents/Resources/firmware.bin.gz"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Claude Display</string>
    <key>CFBundleDisplayName</key><string>Claude Display</string>
    <key>CFBundleIdentifier</key><string>local.claude-display.mac</string>
    <key>CFBundleExecutable</key><string>ClaudeDisplay</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>LSMinimumSystemVersion</key><string>14.0</string>
    <key>LSUIElement</key><true/>
    <key>CDUpdateRepo</key><string>$REPO</string>
    <key>CDFirmwareVersion</key><string>$FW_VERSION</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSLocalNetworkUsageDescription</key>
    <string>Claude Display schickt den Status deiner Claude-Code-Sitzungen an das Display in deinem WLAN.</string>
</dict>
</plist>
EOF

codesign --force --deep --sign - "$APP"
echo "Gebaut: $APP"

if [ "${1:-}" != "--no-install" ]; then
    pkill -x ClaudeDisplay 2>/dev/null || true
    rm -rf "/Applications/Claude Display.app"
    cp -R "$APP" /Applications/
    echo "Installiert: /Applications/Claude Display.app"
fi
