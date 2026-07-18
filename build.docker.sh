#!/usr/bin/env bash
#
# Build TradeUI.dll inside a Linux container (Docker Desktop on macOS works fine).
# RimWorld's managed DLLs are mounted read-only; nothing game-owned is copied into the image.
#
# Usage:
#   ./build.docker.sh
#   RIMWORLD_MANAGED=/path/to/Managed ./build.docker.sh
#
set -euo pipefail
cd "$(dirname "$0")"

# Default to the repo's git-ignored do_not_upload/Managed folder; override with RIMWORLD_MANAGED.
DEFAULT_MANAGED="$PWD/do_not_upload/Managed"
RIMWORLD_MANAGED="${RIMWORLD_MANAGED:-$DEFAULT_MANAGED}"

if [ ! -f "$RIMWORLD_MANAGED/Assembly-CSharp.dll" ]; then
  echo "ERROR: Assembly-CSharp.dll not found under: $RIMWORLD_MANAGED"
  echo "Set RIMWORLD_MANAGED to your RimWorld install's Managed folder (or a copy of it)."
  exit 1
fi

docker build -t tradeui-build .
docker run --rm \
  -v "$PWD":/src \
  -v "$RIMWORLD_MANAGED":/rimworld/Managed:ro \
  -w /src tradeui-build

echo
echo "Done. DLL copied to TradeUI/v1.6/Assemblies/TradeUI.dll"
