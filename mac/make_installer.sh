#!/bin/bash
# Baut das Installationsprogramm "Claude Display Installer.pkg" in ../dist
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(grep -m1 '^VERSION=' build.sh | cut -d'"' -f2)
OUT="../dist/Claude Display Installer.pkg"
WORK=build/installer

./build.sh --no-install

rm -rf "$WORK"
mkdir -p "$WORK/root/Applications" "$WORK/resources"
cp -R "build/Claude Display.app" "$WORK/root/Applications/"
cp installer/resources/*.html "$WORK/resources/"
/usr/local/bin/python3 installer/make_backgrounds.py "$WORK/resources" >/dev/null
chmod +x installer/scripts/*

# Komponente: App nach /Applications, Skripte beenden/starten die App
pkgbuild --root "$WORK/root" --identifier local.claude-display.mac \
         --version "$VERSION" --install-location / \
         --scripts installer/scripts "$WORK/ClaudeDisplay.pkg" >/dev/null

cat > "$WORK/distribution.xml" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<installer-gui-script minSpecVersion="2">
    <title>Claude Display</title>
    <welcome file="welcome.html" mime-type="text/html"/>
    <conclusion file="conclusion.html" mime-type="text/html"/>
    <background file="background.png" alignment="bottomleft" scaling="proportional"/>
    <background-darkAqua file="background-dark.png" alignment="bottomleft" scaling="proportional"/>
    <options customize="never" require-scripts="false" hostArchitectures="arm64,x86_64"/>
    <domains enable_localSystem="true"/>
    <volume-check><allowed-os-versions><os-version min="14.0"/></allowed-os-versions></volume-check>
    <choices-outline><line choice="app"/></choices-outline>
    <choice id="app" visible="false"><pkg-ref id="local.claude-display.mac"/></choice>
    <pkg-ref id="local.claude-display.mac" version="$VERSION" onConclusion="none">ClaudeDisplay.pkg</pkg-ref>
</installer-gui-script>
EOF

mkdir -p ../dist
productbuild --distribution "$WORK/distribution.xml" --resources "$WORK/resources" \
             --package-path "$WORK" "$OUT" >/dev/null
echo "Installer: $(cd ../dist && pwd)/$(basename "$OUT")"
