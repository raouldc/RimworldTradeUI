# Reproducible build container for TradeUI.dll (targets .NET Framework 4.7.2).
#
# RimWorld's managed DLLs are NOT redistributable, so they are NOT baked into the image —
# mount them at runtime. Build with build.docker.sh, or manually:
#
#   docker build -t tradeui-build .
#   docker run --rm \
#     -v "$PWD":/src \
#     -v "/path/to/RimWorld/Managed":/rimworld/Managed:ro \
#     -w /src tradeui-build \
#     dotnet build Source/TradeMod/TradeUI.csproj -c Release -p:RimWorldManaged=/rimworld/Managed
#
# The built DLL is written to TradeUI/v1.6/Assemblies/TradeUI.dll on the mounted source volume.
FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /src
CMD ["dotnet", "build", "Source/TradeMod/TradeUI.csproj", "-c", "Release", "-p:RimWorldManaged=/rimworld/Managed"]
