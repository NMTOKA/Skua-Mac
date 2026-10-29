#!/bin/bash
# Builds a double-clickable "Skua Mac.app" (the VibeSkua client) for Apple Silicon.
#
#   ./build-mac.sh            -> Build/macOS/Skua Mac.app
#   ./build-mac.sh --dmg      -> also Build/macOS/Skua-Mac-<version>.dmg (what users download)
#
# Requires the .NET 10 SDK and Node.js. The app is self-contained (no .NET install needed to run it),
# bundles both game hosts, and is ad-hoc signed. The Electron host's PepperFlash still comes from the user's
# Artix Game Launcher install at runtime and needs Rosetta 2; the Ruffle host (Skua.RuffleHost) needs neither.
set -euo pipefail

MAKE_DMG=0
[ "${1:-}" = "--dmg" ] && MAKE_DMG=1

ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="${SKUA_OUT:-$ROOT/Build/macOS}"
# Assemble and sign outside the repo: iCloud-synced folders (e.g. Desktop) add file attributes
# that codesign rejects. The finished bundle is copied to $OUT at the end.
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
APP_NAME="Skua Mac"
APP="$STAGE/$APP_NAME.app"
CONTENTS="$APP/Contents"
MACOS="$CONTENTS/MacOS"
RID="osx-arm64"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"

echo "==> Building $APP_NAME $VERSION ($RID)"
mkdir -p "$MACOS" "$CONTENTS/Resources" "$OUT"

"$DOTNET" publish "$ROOT/Skua.App.Avalonia/Skua.App.Avalonia.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -p:DebugType=None \
  -o "$MACOS" -v q -nologo

echo "==> Adding Skua Manager (accounts)"
# Same folder as the client: the shared libraries are identical, so only the Manager's own files are added.
MANAGER_OUT="$STAGE/manager"
"$DOTNET" publish "$ROOT/Skua.Manager.Avalonia/Skua.Manager.Avalonia.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -p:DebugType=None \
  -o "$MANAGER_OUT" -v q -nologo
rsync -a --ignore-existing "$MANAGER_OUT"/ "$MACOS"/

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

echo "==> Bundling the Ruffle game host"
"$ROOT/Skua.RuffleHost/build.sh" "$MACOS/RuffleHost"

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
  <key>CFBundleName</key><string>$APP_NAME</string>
  <key>CFBundleDisplayName</key><string>$APP_NAME</string>
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

if pgrep -f "$OUT/.*\.app/Contents/MacOS/VibeSkua\$" >/dev/null; then
  echo "error: $APP_NAME is running from $OUT. Quit it and run this again." >&2
  exit 1
fi
# iCloud Drive keeps a replaced bundle as a conflict copy ("Skua Mac 2.app"). Drop those, and bundles from
# before the rename (VibeSkua.app), so only the current build is left to open.
rm -rf "$OUT/$APP_NAME.app" "$OUT/$APP_NAME "[0-9]*.app "$OUT/VibeSkua.app" "$OUT"/VibeSkua\ [0-9]*.app
ditto "$APP" "$OUT/$APP_NAME.app"
echo "==> Done: $OUT/$APP_NAME.app ($(du -sh "$OUT/$APP_NAME.app" | cut -f1))"

if [ "$MAKE_DMG" = 1 ]; then
  echo "==> Disk image"
  DMG_SRC="$STAGE/dmg"
  mkdir -p "$DMG_SRC"
  ditto "$APP" "$DMG_SRC/$APP_NAME.app"
  ln -s /Applications "$DMG_SRC/Applications"
  cp "$ROOT/packaging/macos/HOW TO OPEN.txt" "$DMG_SRC/HOW TO OPEN.txt"
  DMG="$OUT/Skua-Mac-$VERSION.dmg"
  rm -f "$DMG"
  hdiutil create -volname "$APP_NAME" -srcfolder "$DMG_SRC" -ov -format UDZO "$DMG" >/dev/null
  echo "==> Done: $DMG ($(du -sh "$DMG" | cut -f1))"
fi
