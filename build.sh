#!/usr/bin/env bash
#
# Build TradeUI.dll on macOS/Linux (targets .NET Framework 4.7.2, no Windows/Mono needed).
# Requires the .NET SDK (https://dotnet.microsoft.com/download) and RimWorld's managed DLLs.
#
# Usage:
#   ./build.sh                       # uses default macOS Steam Managed path
#   RIMWORLD_MANAGED=/path ./build.sh
#
set -euo pipefail
cd "$(dirname "$0")"

# Default to the repo's git-ignored do_not_upload/Managed folder; override with RIMWORLD_MANAGED.
DEFAULT_MANAGED="$PWD/do_not_upload/Managed"
RIMWORLD_MANAGED="${RIMWORLD_MANAGED:-$DEFAULT_MANAGED}"

if [ ! -f "$RIMWORLD_MANAGED/Assembly-CSharp.dll" ]; then
  echo "ERROR: Assembly-CSharp.dll not found under:"
  echo "  $RIMWORLD_MANAGED"
  echo
  echo "Set RIMWORLD_MANAGED to your RimWorld install's Managed folder. You can copy that folder"
  echo "from a Windows install (…\\RimWorldWin64_Data\\Managed) if you don't have RimWorld on this Mac."
  exit 1
fi

echo "Building against: $RIMWORLD_MANAGED"
dotnet build Source/TradeMod/TradeUI.csproj -c Release -p:RimWorldManaged="$RIMWORLD_MANAGED"
echo
echo "Done. DLL copied to TradeUI/v1.6/Assemblies/TradeUI.dll"
