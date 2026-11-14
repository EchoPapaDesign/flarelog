#!/bin/bash
# Baut Flarelog.app für macOS und packt sie als ZIP – auf einem Mac oder in GitHub Actions (macos-latest).
#   ./build-mac.sh [Version] [osx-arm64|osx-x64]
# Ergebnis: publish/Flarelog-macOS-AppleSilicon.zip bzw. publish/Flarelog-macOS-Intel.zip
set -euo pipefail

VERSION="${1:-0.0.0-dev}"
RID="${2:-osx-arm64}"
SHORT="${VERSION%%-*}"
case "$RID" in
  osx-arm64) LABEL="AppleSilicon" ;;
  osx-x64)   LABEL="Intel" ;;
  *) echo "Unbekannte Plattform: $RID"; exit 1 ;;
esac

OUT="build-mac/$RID"
APP="$OUT/Flarelog.app"
rm -rf "$OUT"

# Native Bibliotheken (Photino-Fenster, .NET-Laufzeit) liegen als eigene Dateien in Contents/MacOS und werden
# einzeln signiert. Früher wurden sie bei jedem Start aus der Programmdatei in einen temporären Ordner entpackt –
# räumt macOS den auf, scheiterte auf Apple Silicon der erste Start danach („unerwartet beendet“).
dotnet publish src/AeroflyAcars.Mac -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -p:Version="$VERSION" -o "$OUT/bin"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$OUT/bin/Flarelog" "$APP/Contents/MacOS/Flarelog"
chmod +x "$APP/Contents/MacOS/Flarelog"
shopt -s nullglob
for lib in "$OUT/bin"/*.dylib; do
  cp "$lib" "$APP/Contents/MacOS/"
  codesign --force --sign - "$APP/Contents/MacOS/$(basename "$lib")"
done
shopt -u nullglob
echo "Mitgelieferte Bibliotheken:"; ls -1 "$APP/Contents/MacOS"
cp packaging/mac/Flarelog.icns "$APP/Contents/Resources/Flarelog.icns"
sed -e "s/__SHORTVERSION__/$SHORT/g" -e "s/__VERSION__/$VERSION/g" packaging/mac/Info.plist > "$APP/Contents/Info.plist"

# Ohne Apple-Entwicklerkonto nur „ad hoc“ signiert – auf Apple-Silicon-Macs startet sonst gar nichts.
codesign --force --deep --sign - "$APP"

mkdir -p publish
ditto -c -k --keepParent "$APP" "publish/Flarelog-macOS-$LABEL.zip"
echo "Fertig: publish/Flarelog-macOS-$LABEL.zip"
