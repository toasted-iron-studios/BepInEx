#!/usr/bin/env bash
#
# Copies the built install tree (dist/) into an exported Godot game directory.
#
#   ./install.sh <path-to-exported-game-dir>
#
# The exported game dir is the folder containing the game executable and its
# data_<name>_<platform>/ managed folder.
#
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST="$HERE/dist"
TARGET="${1:?usage: install.sh <exported-game-dir>}"

[[ -f "$DIST/libgodot_doorstop.so" ]] || { echo "dist/ not built — run ./build.sh first." >&2; exit 1; }
[[ -d "$TARGET" ]] || { echo "Not a directory: $TARGET" >&2; exit 1; }

cp "$DIST/libgodot_doorstop.so" "$TARGET/"
mkdir -p "$TARGET/BepInEx"
cp -r "$DIST/BepInEx/." "$TARGET/BepInEx/"

echo "Installed BepInEx-for-Godot into: $TARGET"
echo "Launch with: $HERE/run.sh $TARGET"
