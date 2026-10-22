#!/bin/bash
# Builds the Ruffle game host into Skua.RuffleHost/build (or $1): the SkuaRuffleHost executable (arm64),
# host.html and the Ruffle web player files. FlashUtil.Unix.cs finds it there during development, and
# build-mac.sh copies it into the app bundle. Requires Xcode's command line tools (swiftc) and Node.js.
set -euo pipefail

SRC="$(cd "$(dirname "$0")" && pwd)"
OUT="${1:-$SRC/build}"
mkdir -p "$OUT/ruffle"

if [ ! -f "$SRC/node_modules/@ruffle-rs/ruffle/ruffle.js" ]; then
  (cd "$SRC" && npm ci --no-audit --no-fund)
fi
RUFFLE="$SRC/node_modules/@ruffle-rs/ruffle"
rm -f "$OUT/ruffle/"*
cp "$RUFFLE"/ruffle.js "$RUFFLE"/core.ruffle.*.js "$RUFFLE"/*.wasm "$RUFFLE"/LICENSE_* "$OUT/ruffle/"
rm -f "$OUT/ruffle/"*.map
cp "$SRC/host.html" "$OUT/"

swiftc -O -target arm64-apple-macos13.0 -framework AppKit -framework WebKit \
  "$SRC/RuffleHost.swift" -o "$OUT/SkuaRuffleHost"
echo "==> Ruffle host: $OUT"
