# Building on macOS (runs on Windows)

`TradeUI.dll` is a .NET Framework 4.7.2 assembly. That's platform-independent IL, so a DLL built
on macOS runs unchanged in RimWorld on Windows. You only need RimWorld's managed DLLs available on
the Mac to compile against.

## What you need
- **.NET SDK** (8.0+) — https://dotnet.microsoft.com/download
- **RimWorld's `Managed` folder.** These DLLs are not redistributable. Either install RimWorld on
  the Mac, or copy the `Managed` folder from your Windows install:
  `…\RimWorld\RimWorldWin64_Data\Managed\` → anywhere on the Mac.
  (The managed assemblies are identical across OSes for the same game version, so the Windows
  copy is fine to build against.)
- **Docker Desktop** — only if you want the containerized build.

The build uses two NuGet packages so no Windows/Mono is required:
`Microsoft.NETFramework.ReferenceAssemblies` (net472 reference assemblies) and `Krafs.Publicizer`
(publicizes `Assembly-CSharp` at build time — you do **not** need a pre-made
`Assembly-CSharp_publicized.dll`). Harmony comes from `Lib.Harmony`.

## Option A — native (no Docker)
```
# default macOS Steam path is assumed; otherwise set RIMWORLD_MANAGED
RIMWORLD_MANAGED="/path/to/RimWorld/Managed" ./build.sh
```
Or directly:
```
dotnet build Source/TradeMod/TradeUI.csproj -c Release -p:RimWorldManaged="/path/to/Managed"
```

## Option B — Docker
```
RIMWORLD_MANAGED="/path/to/RimWorld/Managed" ./build.docker.sh
```
The `Managed` folder is mounted read-only; nothing game-owned is baked into the image.

## Output
Both routes drop the DLL into `TradeUI/v1.6/Assemblies/TradeUI.dll` (the loadable mod folder).

## Installing / running in RimWorld (Windows)
1. Copy the whole `TradeUI/` folder into `…\RimWorld\Mods\` (so you have
   `…\RimWorld\Mods\TradeUI\About\About.xml`), including the freshly built
   `v1.6\Assemblies\TradeUI.dll`.
2. In-game → Mods: enable **Harmony** and **Trade UI Revised**, with **Harmony first**. Restart.
3. Open a trade to test.

## Notes
- The project is validated against **RimWorld 1.6** only (it transpiles game IL, which differs per
  version). Build against a 1.6 `Managed` folder.
- If `dotnet build` reports unknown members, most likely two APIs used by the new code need
  confirming for your game version: `Find.WindowStack.Windows` and `Tradeable.GetPriceFor(...)`.
