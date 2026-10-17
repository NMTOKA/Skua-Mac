#!/bin/bash
# Builds a double-clickable VibeSkua.app for Apple Silicon.
#
#   ./build-mac.sh            -> Build/macOS/VibeSkua.app
#
# Requires the .NET 10 SDK and Node.js. The app is self-contained (no .NET install needed to run it),
# bundles the Electron game host, and is ad-hoc signed. PepperFlash still comes from the user's
# Artix Game Launcher install at runtime, and Rosetta 2 must be installed.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/Build/macOS"
# Assemble and sign outside the repo: iCloud-synced folders (e.g. Desktop) add file attributes
# that codesign rejects. The finished bundle is copied to $OUT at the end.
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/VibeSkua.app"
CONTENTS="$APP/Contents"
MACOS="$CONTENTS/MacOS"
RID="osx-arm64"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"

echo "==> Building VibeSkua $VERSION ($RID)"
mkdir -p "$MACOS" "$CONTENTS/Resources" "$OUT"

"$DOTNET" publish "$ROOT/Skua.App.Avalonia/Skua.App.Avalonia.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -p:DebugType=None \
  -o "$MACOS" -v q -nologo

echo "==> Bundling the Flash host"
HOST_SRC="$ROOT/Skua.FlashHost"
HOST_DST="$MACOS/FlashHost"
if [ ! -x "$HOST_SRC/node_modules/electron/dist/Electron.app/Contents/MacOS/Electron" ]; then
  (cd "$HOST_SRC" && npm_config_arch=x64 npm install --no-audit --no-fund)
fi
mkdir -p "$HOST_DST/node_modules/electron"
cp "$HOST_SRC/main.js" "$HOST_SRC/preload.js" "$HOST_SRC/host.html" "$HOST_SRC/package.json" "$HOST_DST/"
# ditto keeps the Electron framework symlinks intact.
ditto "$HOST_SRC/node_modules/electron/dist" "$HOST_DST/node_modules/electron/dist"

echo "==> Icon"
ICONSET="$STAGE/VibeSkua.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
SRC_PNG="$ROOT/Skua.Shared.Avalonia/Assets/SkuaImage.png"
for size in 16 32 128 256 512; do
  sips -z $size $size "$SRC_PNG" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$SRC_PNG" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/VibeSkua.icns"
rm -rf "$ICONSET"

cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>VibeSkua</string>
  <key>CFBundleDisplayName</key><string>VibeSkua</string>
  <key>CFBundleIdentifier</key><string>com.vibeskua.client</string>
  <key>CFBundleExecutable</key><string>VibeSkua</string>
  <key>CFBundleIconFile</key><string>VibeSkua</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

echo "==> Signing (ad-hoc)"
# Downloaded files (Electron from npm) carry extended attributes that codesign rejects.
xattr -cr "$APP"
codesign --force --deep --sign - "$APP"
codesign --verify --deep "$APP"

if pgrep -f "$OUT/VibeSkua.*\.app/Contents/MacOS/VibeSkua\$" >/dev/null; then
  echo "error: VibeSkua is running from $OUT. Quit it and run this again." >&2
  exit 1
fi
# iCloud Drive keeps a replaced bundle as a conflict copy ("VibeSkua 2.app"); drop those so only the
# current build is left to open.
rm -rf "$OUT/VibeSkua.app" "$OUT"/VibeSkua\ [0-9]*.app
ditto "$APP" "$OUT/VibeSkua.app"
echo "==> Done: $OUT/VibeSkua.app ($(du -sh "$OUT/VibeSkua.app" | cut -f1))"
