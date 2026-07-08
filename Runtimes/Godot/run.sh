#!/usr/bin/env bash
#
# Launches an exported Godot game with GodotDoorstop + BepInEx enabled.
#
#   ./run.sh <exported-game-dir> [game-executable-name] [-- extra game args]
#
# If the executable name is omitted, the first executable file in the dir is used.
#
set -euo pipefail

GAME_DIR="${1:?usage: run.sh <exported-game-dir> [exe] [-- args...]}"
shift || true

EXE=""
if [[ "${1:-}" != "--" && -n "${1:-}" ]]; then
    EXE="$1"; shift || true
fi
[[ "${1:-}" == "--" ]] && shift || true

if [[ -z "$EXE" ]]; then
    EXE="$(find "$GAME_DIR" -maxdepth 1 -type f -perm -u+x -printf '%f\n' | head -n1)"
fi
[[ -n "$EXE" && -x "$GAME_DIR/$EXE" ]] || { echo "Could not find game executable in $GAME_DIR" >&2; exit 1; }

PRELOADER="$GAME_DIR/BepInEx/core/BepInEx.Godot.Preloader.dll"
[[ -f "$PRELOADER" ]] || { echo "BepInEx not installed in $GAME_DIR (run install.sh first)" >&2; exit 1; }

cd "$GAME_DIR"
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY="$PRELOADER"
export LD_LIBRARY_PATH="$GAME_DIR:${LD_LIBRARY_PATH:-}"
export LD_PRELOAD="libgodot_doorstop.so:${LD_PRELOAD:-}"

exec "./$EXE" "$@"
