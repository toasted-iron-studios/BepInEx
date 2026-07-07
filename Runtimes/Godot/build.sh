#!/usr/bin/env bash
#
# Builds the BepInEx-for-Godot backend and assembles a ready-to-copy install tree.
#
#   dist/libgodot_doorstop.so     native GodotDoorstop preloader (LD_PRELOAD)
#   dist/BepInEx/core/*.dll       BepInEx runtime + the Godot backend
#
# Requirements: clang, dotnet SDK (>= net8). The generic preloader needs no engine
# assemblies; the engine-facing Lifecycle layer needs a GodotSharp.dll to build against
# (Godot 4.7's GodotSharp targets net8), so set GODOTSHARP_DLL to build it.
#
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"            # the BepInEx repo root
DOORSTOP="$HERE/Doorstop"
DIST="$HERE/dist"
CONFIG="${CONFIG:-Release}"

mkdir -p "$DIST/BepInEx/core"

echo "==> Ensuring GodotDoorstop submodule is checked out"
git -C "$ROOT" submodule update --init "$DOORSTOP"

echo "==> Building libgodot_doorstop.so"
# GodotDoorstop's nix sources rely on an include order its xmake build supplies implicitly;
# force-include the two headers that provide char_t / load_hostfxr_funcs.
( cd "$DOORSTOP" && clang -shared -fPIC -O2 -Isrc \
    -include src/util/util.h -include src/runtimes/hostfxr.h \
    -Wno-implicit-function-declaration -Wno-error=implicit-function-declaration \
    -Wno-int-conversion -Wno-unused-function \
    -o "$DIST/libgodot_doorstop.so" \
    src/bootstrap.c src/config/common.c src/nix/config.c src/nix/entrypoint.c \
    src/nix/util.c src/nix/plthook/plthook_elf.c src/runtimes/globals.c src/util/paths.c \
    -ldl )

echo "==> Building BepInEx.Godot preloader (generic core — no engine reference)"
dotnet build "$HERE/BepInEx.Godot/BepInEx.Godot.csproj" -c "$CONFIG" -o "$DIST/BepInEx/core"

if [[ -n "${GODOTSHARP_DLL:-}" ]]; then
    cp "$GODOTSHARP_DLL" "$HERE/BepInEx.Godot.Lifecycle/GodotSharp.dll"
    echo "==> Building BepInEx.Godot.Lifecycle (engine-facing core — SceneTree hook)"
    dotnet build "$HERE/BepInEx.Godot.Lifecycle/BepInEx.Godot.Lifecycle.csproj" -c "$CONFIG" -o "$DIST/BepInEx/core"
else
    echo "==> GODOTSHARP_DLL not set — skipping the engine-facing Lifecycle layer."
    echo "    Set GODOTSHARP_DLL=<export>/data_*/GodotSharp.dll to build scene-tree/node support."
fi

echo "==> Tidying install tree"
# GodotSharp lives in the game's own managed dir (on the CoreCLR TPA list); don't ship a copy.
rm -f "$DIST/BepInEx/core/GodotSharp.dll"

echo
echo "Done. Install tree is in: $DIST"
echo "Deploy with: ./install.sh <exported-game-dir>"
