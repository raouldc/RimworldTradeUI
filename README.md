# Trade UI Revised (fork)

A fork of [Trade UI Revised / RimworldTradeUI](https://github.com/patrickdevarney/RimworldTradeUI)
by Hob Took, extended so the trade window fits any screen and adds several quality-of-life
improvements.

## Why

The original trade UI is hard to use: it's difficult to find the items you want to sell among the
trader's junk, negative vs positive numbers are confusing, and the trade cost sits far from the
Accept/Cancel buttons. The base mod reorganizes the trade screen into a clear two-column layout
(your colony on the left, the trader on the right) showing who's trading and what it costs.

## What this fork adds

**Fits your screen (the main fix)**

- Resizable trade window — drag it to any size; the size is remembered between opens (kept local
  per client in multiplayer).
- Columns are sized so they never overlap, no matter how narrow the window gets.
- Horizontal scrollbars appear when a pane is too narrow to show every column, so nothing is cut
  off.

**Quality-of-life**

- Pinned column headers (Item / Owned / Price / Trade) on both panes.
- Per-row bulk buttons: "All" (buy/sell the max) and "$" (aim to zero out silver on that row).
- A live running silver total next to the Accept button that turns red when the colony can't
  afford the deal.
- An "in-deal only" filter to hide rows you aren't trading, plus a highlight on active rows.
- Coloured action buttons — Accept green, Cancel red, Reset neutral.
- Scroll position is remembered across opens.

Several of these were on the original author's wishlist (max-money button, button colours).

## Compatibility

- **Version:** validated against **RimWorld 1.6** (the mod transpiles game IL, which differs per
  version).
- **Saves:** UI-only — safe to add or remove from existing save games.
- **Multiplayer:** supports the RimWorld Multiplayer mod. Window size and scroll position stay
  local to each client and are not synced (they can't cause desyncs).
- **Requires:** Harmony (load it before this mod).

## Building

Builds on macOS, Linux, or Windows with the .NET SDK — no Mono or Windows toolchain needed. See
[BUILD.md](BUILD.md) for full instructions. Quick version:

```
# put RimWorld's Managed DLLs in do_not_upload/Managed/ (git-ignored), then:
./build.sh              # native
./build.docker.sh       # or in a container
```

The build drops `TradeUI.dll` into `TradeUI/v1.6/Assemblies/`. To install, copy the `TradeUI/`
folder into your RimWorld `Mods/` directory and enable it (with Harmony) in-game.

## Credits

Original mod by Hob Took (patrickdevarney). This is a personal fork with the fit fixes and UX
additions above. Suggestions welcome.
