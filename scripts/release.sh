#!/bin/bash
# Baut Firmware, macOS-Installer und Windows-App und veröffentlicht sie als GitHub-Release.
#   ./scripts/release.sh 1.0.0            bauen + Release erstellen
#   ./scripts/release.sh 1.0.0 --dry-run  nur bauen (dist/)
#
# Voraussetzungen: PlatformIO (pio), Xcode, .NET 8 SDK, GitHub CLI (gh), angemeldet.
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:?Version angeben, z. B. 1.0.0}"
DRY="${2:-}"
REPO=$(git remote get-url origin | sed -E 's#.*github\.com[:/]##; s#\.git$##')
FW_VERSION=$(grep -m1 'define FW_VERSION' firmware/src/main.cpp | cut -d'"' -f2)
PIO=${PIO:-$(command -v pio || echo "$PWD/firmware/.venv/bin/pio")}
PIO=$(cd "$(dirname "$PIO")" && pwd)/$(basename "$PIO")
DOTNET=${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}

echo "Release v$VERSION · Firmware $FW_VERSION · $REPO"
rm -rf dist && mkdir -p dist

# 1. Firmware
(cd firmware && "$PIO" run -e display -s)
gzip -9 -n -c firmware/.pio/build/display/firmware.bin > dist/firmware.bin.gz
mkdir -p firmware/dist && cp dist/firmware.bin.gz firmware/dist/display.bin.gz
cp dist/firmware.bin.gz windows/Assets/firmware.bin.gz

# 2. macOS (Version in build.sh setzen)
sed -i '' -E "s/^VERSION=\".*\"/VERSION=\"$VERSION\"/" mac/build.sh
(cd mac && ./make_installer.sh)
mv "dist/Claude Display Installer.pkg" dist/ClaudeDisplay-mac.pkg

# 3. Windows
sed -i '' -E "s#<Version>.*</Version>#<Version>$VERSION</Version>#" windows/ClaudeDisplay.csproj
(cd windows && "$DOTNET" publish -c Release -r win-x64 -o publish -p:UpdateRepo="$REPO" -p:FirmwareVersion="$FW_VERSION" -v q)
cp windows/publish/ClaudeDisplay.exe dist/ClaudeDisplay.exe

# 4. Versionsinfo für den Updater
printf '{"app":"%s","firmware":"%s"}\n' "$VERSION" "$FW_VERSION" > dist/release.json
ls -la dist

if [ "$DRY" = "--dry-run" ]; then echo "Nur gebaut (dist/)."; exit 0; fi

# 5. GitHub-Release
gh release create "v$VERSION" --repo "$REPO" --title "Claude Display $VERSION" \
   --notes-file "${NOTES:-/dev/null}" \
   dist/ClaudeDisplay-mac.pkg dist/ClaudeDisplay.exe dist/firmware.bin.gz dist/release.json
echo "Veröffentlicht: https://github.com/$REPO/releases/tag/v$VERSION"
