# Plan: Trade UI — vanilla toggle, gift fix, caravan weight

Target: `Source/TradeMod/TradeUIRework.cs`, `Source/TradeMod/TradeUIParameters.cs`.
Scope: RimWorld 1.6. Build via `./build.sh` (SDK build, `do_not_upload/Managed`).

Decisions locked in:
- Vanilla switch = **in-trade toggle button** (not a settings screen).
- Gift fix = **fix the mod's own gift rendering** (show every giftable item).
- Weight (caravan trades only) = **all four**: current+capacity, projected-after-trade,
  per-item weight column, and planned-items total.

---

## Feature 1 — In-trade "use vanilla window" toggle

Goal: a button in the trade window that flips the mod's custom rendering off and lets RimWorld
draw its normal trade UI — primarily to escape multiplayer issues.

- **State:** add `public static bool useVanillaRendering;` to `TradeUIParameters` (client-local,
  like the other UI toggles; no MP sync). Optionally persist across sessions later; not required.
- **Button:** draw a small toggle in the footer button row of `MyDoWindowContents` (next to the
  existing "Show" dropdown), e.g. icon/text "Vanilla UI: on/off". Since the stated purpose is MP,
  only show it when in multiplayer (reuse the reflection-gated `MpTradeWindowOpen()` helper); or
  show always — decide during impl. Play the usual tick sound on click.
- **Make the drawing bail to vanilla when on:**
  - `Harmony_DialogTrade_FillMainRect.Prefix`: at the top, `if (TradeUIParameters.useVanillaRendering) return true;`
    → vanilla draws item headers + rows.
  - `Harmony_TransferableUIUtility_DoCountAdjustInterfaceInternal.Prefix`: add
    `useVanillaRendering` to the existing bail condition → vanilla count controls (`<< < > >>`).
- **Transpiler caveat (important):** `DoWindowContents` is rewritten by a *compile-time*
  transpiler that deletes vanilla's footer/main-rect call and injects `MyDoWindowContents`; it
  can't be switched off at runtime. So in "vanilla" mode you get: vanilla sorters (kept by the
  transpiler) + vanilla `FillMainRect` rows (via the prefix) + **the mod's footer reimplementation**
  (accept/reset/cancel/silver, which already mirrors vanilla). That's effectively vanilla for the
  part that matters (the item grid + count widget) — the footer just stays the mod's near-identical
  version. If a 100%-vanilla footer is required, that's a bigger change (untranspile / separate
  patch) — call out as out of scope unless you want it.
- **Result:** in MP, clicking the toggle gives you vanilla item rows + vanilla `<< < > >>` controls
  inside the MP `TradingWindow`, sidestepping any mod-specific MP rendering problems.

## Feature 2 — Fix missing items in gift mode

Symptom: in gift mode some colony items have no row in either pane.

- **Root cause (to confirm in code):** the pane split decides where to draw a row using
  `entry.thingsColony.Count > 0` (left) and `entry.thingsTrader.Count > 0` (right)
  (FillMainRect prefix, ~lines 663-745). In gift mode the giftable tradeables don't populate
  `thingsColony`/`thingsTrader` the way normal trade does (gift deals use a different transferable
  shape), so a giftable item can have **both** lists empty → skipped by both loops → invisible.
- **Fix:** decide the pane by "does this side actually hold any?" rather than the raw things-lists:
  use `entry.CountHeldBy(Transactor.Colony) > 0` / `entry.CountHeldBy(Transactor.Trader) > 0`
  (the currency row already uses `CountHeldBy`, so it's available and reliable). In **gift mode**,
  route every giftable item to the colony (left) pane regardless, since gifting is one-sided.
- Apply the same corrected condition in the scroll-height calculation so heights match the rows
  actually drawn (no blank gaps / clipped list).
- Verify against: normal trade (rows unchanged), gift mode (all giftable items now listed),
  items both sides hold (still appear on the correct side), pawns/animals as gifts, and the
  in-deal / hide-unwilling filters still behave.
- Keep it consistent with Feature 1: when `useVanillaRendering` is on, vanilla handles gifting too.

## Feature 3 — Caravan weight / mass (caravan trades only)

Only active when trading from a caravan: guard everything on
`TradeSession.playerNegotiator.GetCaravan()` being non-null (already used at ~line 173). Hide all
mass UI for base/orbital trades.

Data sources (RimWorld):
- Current caravan mass + capacity: `CollectionsMassCalculator` / `MassUtility` — e.g.
  `CaravanMassUsage`/`MassUsage` and `MassCapacity` for the caravan's pawns+inventory. Confirm the
  exact 1.6 helper during impl (candidates: `MassUtility.GearAndInventoryMass`,
  `CollectionsMassCalculator.MassUsage(...)`, `CaravanMassUsageUtility`).
- Per-thing mass: `thing.GetStatValue(StatDefOf.Mass)` (× stack count).

Sub-features (all four requested):
1. **Current + capacity in the header:** draw "Caravan: 1,240 / 1,800 kg" in/near the header
   area of the FillMainRect prefix (or the top of `MyDoWindowContents`), colored red when over
   capacity.
2. **Projected mass after trade:** sum the mass delta of the current deal —
   for each tradeable, `CountToTransfer × unitMass` with the correct sign (buying adds to the
   caravan, selling removes) — and show "→ 1,510 / 1,800 kg" next to the current figure, red if the
   projection exceeds capacity. Recompute cheaply each frame from `___cachedTradeables`.
3. **Per-item weight column:** add a "Weight" column to the row model (a new fixed-width column in
   `MyDrawTradableRow` + a matching header in `DrawColumnHeaders`), showing unit or stack mass.
   Fold its width into `MinRowWidth()` so it never reintroduces overlap; only render it in caravan
   trades (so base trades keep the current columns).
4. **Planned-items total:** show the added/removed mass for just the items currently in the deal
   (e.g. "Trade load: +270 kg") near the footer/running-silver total.

Layout notes:
- The per-item Weight column widens rows; make sure the header alignment and the horizontal-scroll
  content width (`MinRowWidth()`) include it, and that it's omitted (zero width) in non-caravan
  trades so nothing shifts there.
- Prefer a compact unit (kg) and RimWorld's number formatting; right-align like Price.

---

## Shared / infra notes
- No ModSettings screen is needed for these (the vanilla switch is an in-window toggle). If you
  later want any of these in the mod-settings menu, that requires adding `Mod`/`ModSettings`
  scaffolding — flag as a separate task.
- MP safety: all new state (vanilla toggle, any weight display) is display-only/local; nothing
  mutates `TradeSession.deal`, so no MP sync and no desync risk. Weight math only *reads* the deal.
- Build can't be visually verified here; after building, test in-game: MP vanilla toggle, gift mode
  item completeness, and caravan mass (current/projected/column/total) both under and over capacity.

## Suggested order
1. Feature 2 (gift fix) — smallest, self-contained, high value.
2. Feature 1 (vanilla toggle) — reuses existing prefixes + MP detection.
3. Feature 3 (weight) — largest; do the header current/capacity first, then projected, then the
   per-item column, then the planned-items total.

## Open questions / confirm during build
- Should the vanilla toggle show only in MP, or always? (Plan assumes MP-only; easy to change.)
- Per-item weight: unit mass or full-stack mass in the column? (Plan: show stack mass, tooltip unit.)
- Should the vanilla-toggle choice persist across sessions, or reset each trade? (Plan: session-local.)
