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
- **Review caveat — headers disappear in vanilla mode.** The faction/negotiator names and the
  pinned column headers are drawn *inside* `FillMainRect.Prefix` (~lines 596-649, 678/722); vanilla's
  own copies were deleted by the transpiler. So bailing to vanilla yields a nameless, header-less
  grid on the mod's footer. Either draw a minimal header (faction names + column labels) before the
  `return true`, or accept and document the bare look. Decide during impl.
- **SP escape hatch:** if the toggle is MP-only, single-player has no way to fall back when the mod
  misbehaves. Consider showing it always (not just in MP) so it doubles as a general escape hatch.

## Feature 2 — Fix missing items in gift mode

Symptom: in gift mode some colony items have no row.

> **Review correction — the original diagnosis was wrong.** `Tradeable.CountHeldBy(Transactor)`
> is defined as `sum(TransactorThings(trans).stackCount)`, i.e. it is the *same test* as
> `thingsColony.Count > 0`. Swapping one for the other is a **no-op** and cannot make any row
> appear. No `Tradeable` has both sides empty (`AnyThing` errors otherwise). The pane split is not
> the cause.

- **Actual root cause (verified against decompiled 1.6):** items are filtered out **upstream** in
  vanilla `Dialog_Trade.CacheTradeables`, before the mod's prefix runs:
  ```
  cachedTradeables = from tr in TradeSession.deal.AllTradeables
      where !tr.IsCurrency && (tr.TraderWillTrade || !TradeSession.trader.TraderKind.hideThingsNotWillingToTrade)
      ...
  ```
  There is **no gift-mode branch** here. So in gift mode, colony items the trader "won't trade"
  are dropped for trader kinds with `hideThingsNotWillingToTrade` — exactly the low-value junk you'd
  gift for goodwill. Since the mod only iterates `___cachedTradeables`, no change in
  `FillMainRect.Prefix` can bring them back. (Also rule out the mod's own `hideUnwillingToBuy` /
  `filterInDealOnly` toggles and any search filter as the immediate cause.)
- **Step 1 — reproduce & confirm:** in a gift trade, log `TradeSession.deal.AllTradeables.Count`
  vs `___cachedTradeables.Count` vs rows drawn, to confirm items are missing at the cache stage
  (not the draw stage). Do this before writing the fix.
- **Fix options (pick after repro):**
  - (a) Harmony-patch `Dialog_Trade.CacheTradeables` (postfix/transpiler) to skip the
    `TraderWillTrade` filter **when `TradeSession.giftMode`** so all giftable colony items are
    included; or
  - (b) In gift mode, have the mod build its own row list from
    `TradeSession.deal.AllTradeables.Where(t => !t.IsCurrency && t.CountHeldBy(Transactor.Colony) > 0)`
    instead of trusting `___cachedTradeables`.
  - Option (a) is smaller and keeps sorting/caching centralized; prefer it unless it breaks sorters.
- Whatever the fix, in gift mode also **suppress the right (trader) pane** (it's a one-sided give)
  and keep the scroll-height calc in lockstep with the rows actually drawn.
- Verify: gift mode lists every giftable item incl. trader-won't-buy junk; goodwill number correct;
  normal trade unchanged; filters still behave; when `useVanillaRendering` is on, vanilla handles it.

## Feature 3 — Caravan weight / mass (caravan trades only)

Only active when trading from a caravan: guard on `TradeSession.playerNegotiator.GetCaravan()`
non-null (confirmed correct; already used ~line 173). Hide all mass UI for base/orbital trades.

> **Review correction — reuse vanilla's numbers; do not hand-roll the math.** Vanilla `Dialog_Trade`
> already computes both figures correctly via `CollectionsMassCalculator.MassUsageLeftAfterTradeableTransfer(...)`
> / `CapacityLeftAfterTradeableTransfer(...)`, exposed as the **private** props `MassUsage` /
> `MassCapacity`. These already handle **silver's own mass**, pawn/animal mass, minified buildings,
> and the **gift-mode sign flip** (`PositiveCountDirection` becomes `Destination`). A manual
> `CountToTransfer × unitMass` sign calc gets all of these wrong. Also: `CaravanMassUsageUtility`
> does **not** exist in 1.6 — use `CollectionsMassCalculator` + `RimWorld.MassUtility`.
>
> **Also note:** the `DoWindowContents` transpiler *retains* vanilla's `DrawCaravanInfo(...)` bar
> (drawn before the sorters), which already shows caravan mass usage/capacity **and** the
> projected-after-trade value. **Verify in-game what that bar renders first** — the user still wants
> usage/capacity + projected shown, so if the retained bar is present but easy to miss/clipped, the
> task is to surface it clearly (see below), not to compute a second, possibly-contradictory figure.

Implementation:
- **Read, don't recompute:** get `MassUsage` (this is the projected-after-trade value) and
  `MassCapacity` from `__instance` via `AccessTools` (they're private, cached behind dirty flags
  that vanilla refreshes in `CountToTransferChanged`). For the *current* (pre-trade) mass use the
  caravan directly: `caravan.MassUsage` / `caravan.MassCapacity`. This avoids the per-frame O(n)
  walk vanilla deliberately caches away.

Sub-features requested:
1. **Current + capacity** (kept per user): draw "Caravan: 1,240 / 1,800 kg" from `caravan.MassUsage`
   / `caravan.MassCapacity`, in the mod's header/status area, amber near / red over capacity.
2. **Projected after trade** (kept per user): draw "→ 1,510 / 1,800 kg" from the dialog's cached
   `MassUsage` prop, red when it exceeds capacity. If the retained vanilla bar already shows this,
   either rely on it or hide the vanilla bar to avoid duplicate figures — decide after the in-game
   check. Pair color with a glyph/word so it isn't color-only.
3. **Per-item weight column:** add a "Weight" column showing per-thing mass
   (`thing.GetStatValue(StatDefOf.Mass)` × count; label as item mass — note it excludes a pawn's
   carried gear, so don't sum it for totals). Add it in lockstep to `MyDrawTradableRow`,
   `DrawColumnHeaders`, and `MinRowWidth()`, gated on a **caravan predicate** (`GetCaravan() != null`)
   that is independent of the SP/MP `IsOpen<Dialog_Trade>()` check used for the bulk-button gap —
   otherwise it misaligns in MP caravan trades. Zero width in non-caravan trades so nothing shifts.
   Insert at a point common to all three row branches (trader-won't-trade / slavery / normal) so
   the no-trade rows don't misalign.
4. **Planned-items total:** derive from vanilla's cached figures (projected − current) rather than a
   separate sum, so it can't disagree with the header numbers. Show near the running-silver total.

Layout notes:
- The per-item Weight column widens rows; ensure header alignment + `MinRowWidth()` (horizontal
  scroll content width) include it, and that it re-verifies no overlap/scroll regression at narrow
  widths — this is the same alignment trap already fixed once.

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
