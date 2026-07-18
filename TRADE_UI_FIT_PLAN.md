# Plan: Fix Trade UI Fit (resizing, column sizing, horizontal scroll)

## Target
`Source/TradeMod/TradeUIRework.cs` (+ `TradeUIParameters.cs`). This mod Harmony-patches
vanilla `RimWorld.Dialog_Trade`. The screenshot is this mod, not Dynamic Trade Interface
(no search box / gear / sortable headers; vanilla "Sort by" bar and "Switch to map" are present).

**Scope: RimWorld 1.6 only for now.** Build and validate against 1.6; ignore the v1.3–v1.5
`Assemblies` folders for this pass. This matters because the mod uses **IL transpilers** on
`DoWindowContents`/`FillMainRect` (TradeUIRework.cs:28-29, 44-63) that pattern-match game IL,
which differs per game version — so a single DLL is not safely portable across versions and we
are not trying to make it so here. Point the csproj `HintPath`/publicized assembly and
`PostBuildEvent` copy at 1.6.

## Root causes of "doesn't fit"
1. **Columns overlap** – rows are drawn right-anchored with hardcoded pixel widths in
   `MyDrawTradableRow` (`COST_WIDTH = 90`, `TRANSFER_WIDTH = 160`, `OWNED_AMOUNT_WIDTH = 75`,
   lines ~568-570). When a half-pane is narrow, the item-name rect (`idRect`, width =
   remaining `xPosition`) collapses and the price/transfer/arrow blocks draw on top of each
   other. This is what you see on the right column ($27, $678, $79 sitting under the arrows).
2. **No horizontal scrollbar** – the scroll inner rects are pinned to the pane width:
   `leftInsideScrollRect`/`rightInnerRect` use `scrollRect.width - 16f` (lines ~461, 491),
   so content can never be wider than the pane and horizontal scroll never triggers.
3. **Window not resizable** – `Dialog_Trade` is not `resizeable`; the only sizing is
   `Harmony_DialogTrade_InitialSize` adding a fixed +360px (line ~1134). You can't drag it
   larger, and there's no persisted size.

---

## Change 1 — Resizable window (with persisted size)

- Set `resizeable` in the existing `Harmony_DialogTrade_PostOpen`. **Note it is a `Prefix`, not a
  postfix, and currently takes no args** (TradeUIRework.cs:1119-1126) — add a `Dialog_Trade
  __instance` parameter, then `__instance.resizeable = true;`. `Window.resizeable` draws/handles
  the resize grip via `WindowResizer` without needing `draggable`. (This Prefix does **not** fire
  in MP — the MP inner dialog is never stack-opened — which is exactly why Change 4 needs its own
  `TradingWindow` patch.)
- **Do NOT add a `PreClose`/`PostClose` patch.** `Dialog_Trade` does not declare those — they're
  inherited from `Verse.Window`, so `AccessTools.Method(typeof(Dialog_Trade), "PreClose")`
  resolves to the base method and the patch would fire on **every window in the game**. Instead
  capture the size each frame from inside `MyDoWindowContents` (which already has `__instance`,
  line ~77): `TradeUIParameters.windowSize = __instance.windowRect.size;`.
  - **Guard against zero.** Only write the static when the rect is real:
    `if (__instance.windowRect.width > 1f && __instance.windowRect.height > 1f)`. In SP the inner
    dialog is the stack window so `windowRect` is populated; but this same code path runs in MP
    where `__instance` is the inner dialog created via `NewObjectNoCtor` and **never stack-added**,
    so its `windowRect` stays `(0,0)`. Without the guard you'd overwrite the shared
    `windowSize` static with zero the moment MP is used. In MP, capture size from the actual
    `TradingWindow` instead (see Change 4).
- Persist size across opens:
  - Add `public static Vector2 windowSize = Vector2.zero;` to `TradeUIParameters`.
  - In the `Harmony_DialogTrade_InitialSize` postfix (line ~1128), if `windowSize != zero` return
    it clamped to `UI.screenWidth`/`UI.screenHeight`; **otherwise** apply the current `+360`
    default. Make it one branch or the other — don't add `+360` on top of a stored size.
- **Minimum size:** `WindowResizer`'s floor (`minWindowSize`, ~150×150) is not a clean per-window
  knob, so don't try to raise it. Clamp the returned `InitialSize` to a sane minimum and let
  horizontal scroll (Change 3) absorb any under-minimum width the user drags to — the columns
  won't crush because of Change 2.
- **Multiplayer is required (see the dedicated section below).** Setting `resizeable` on
  `Dialog_Trade` does nothing for the MP trade window, which is a separate
  `Multiplayer.Client.TradingWindow` (see `PatchTradingWindowWidth`, TradeUIRework.cs:1139-1163),
  so the same `resizeable`/size + column + scroll changes must also be applied there. **Window
  size and scroll position stay client-local** — they are pure UI state, so they must NOT go
  through MP's action sync; keeping them local is both correct and simpler (no determinism
  concerns). The two `+360` patches target different getters and do **not** conflict.
- Verify the layout reflows: `MyDoWindowContents` and the `FillMainRect` prefix both derive
  every rect from `inRect`/`mainRect` each frame inside the vanilla `GUI.BeginGroup(inRect)`
  (closed at line 209), so resizing already re-lays-out — the only thing that breaks at small
  widths is the fixed row columns (Change 2).

## Change 2 — Column sizing that can't overlap

Goal: columns keep readable fixed sizes; when the pane is too narrow to hold them plus a
minimum name width, fall back to horizontal scroll (Change 3) instead of overlapping.

- In `MyDrawTradableRow`, promote the magic numbers to named constants and add:
  - `NAME_MIN_WIDTH` (e.g. 140f) — minimum space reserved for icon + info button + label.
  - `ICON_INFO_WIDTH` (icon 27 + info button ~40 + padding).
- Compute a per-row **required content width**:
  `minRowWidth = ICON_INFO_WIDTH + NAME_MIN_WIDTH + OWNED_AMOUNT_WIDTH + COST_WIDTH + TRANSFER_WIDTH + extraIconWidth`.
  - **`extraIconWidth` must be measured, not a flat allowance.** `DoExtraIcons` (line ~644) and
    `DrawCaptiveTradeInfo` (line ~649) take `ref xPosition` and can consume 2-3 icons on
    animal (bond/ridable) and captive/ideology rows. Capture how much they subtract (diff
    `xPosition` before/after) and feed the max seen into `minRowWidth`, or compute per-row.
  - The `!TraderWillTrade` and slavery branches (lines ~572-597) net-consume only
    `TRANSFER_WIDTH` (they do `-= (TRANSFER+COST)` then `+= COST`), so the formula above is a
    safe upper bound for them — no special case needed.
  - Keep `TRANSFER_WIDTH ≥ ~137`: `DoCountAdjustInterfaceInternal` hardcodes a 120px+margins
    mini-rect anchored to `rect.xMax` (line ~690); below that the arrows/textbox overflow left.
- Draw the row into a rect whose width is `Max(paneInnerWidth, minRowWidth)` (this width comes
  from the scroll inner rect in Change 3), so the right-anchored blocks always have room and
  `idRect` (name) never goes negative.
- Clamp/guard: if `xPosition < ICON_INFO_WIDTH` after subtracting the right blocks, stop
  subtracting (defensive), so the name never renders at negative width.
- Apply the same treatment to the currency footer row `DrawCurrencyTradableRow`
  (lines ~213-277): it uses center-relative offsets (`center.x ± 240/300`) that also break on
  narrow windows. Re-anchor it to the same column model or at least clamp the offsets.
- (Optional stretch) User-draggable column dividers: store per-column widths in
  `TradeUIParameters`, draw a thin drag handle at each column boundary in the header area, and
  update the stored width on drag. Not required to "fit"; list as a follow-up.

## Change 3 — Horizontal scrollbars

In the `Harmony_DialogTrade_FillMainRect.Prefix` (lines ~460-518):

- Replace the inner-rect widths so content width is independent of pane width:
  ```
  float contentWidth = Mathf.Max(leftScrollRect.width - 16f, minRowWidth);
  Rect leftInsideScrollRect = new Rect(0, 0, contentWidth, leftHeight);
  ```
  (same for the right pane with `rightScrollRect`).
- `Widgets.BeginScrollView(outRect, ref scroll, viewRect, showScrollbars: true)` already draws a
  horizontal bar automatically when `viewRect.width > outRect.width`, so no extra call is needed —
  just make `viewRect` actually wider.
- Each row already draws at `leftInsideScrollRect.width`, so rows will span the full content
  width and scroll horizontally together.
- Reserve vertical space for the horizontal bar: when `contentWidth > paneWidth`, subtract ~16px
  from the usable height (or accept the bar overlapping the last row — RimWorld handles this, but
  reserving looks cleaner). Watch the feedback loop: reserving 16px of height can push content
  height past the viewport and trigger a vertical bar, which shrinks width, which re-triggers the
  horizontal bar. The existing `scrollRect.width - 16f` already hard-reserves the vertical bar, so
  `Mathf.Max(width - 16f, minRowWidth)` is only "correct" when `minRowWidth ≤ width - 16`; below
  that the horizontal bar is what saves you — verify both bars settle rather than oscillate.
- Horizontal scroll position persists for free: `BeginScrollView(..., ref scrollPositionLeft, ...)`
  passes the whole `Vector2` by ref, so `.x` is written back automatically once content overflows
  (no extra state needed in `TradeUIParameters`).
- Sanity-check the `num2/num3` vertical virtualization math still holds (it keys off `.y` only, so
  horizontal scroll doesn't affect it — confirmed correct).
- **Currency footer caveat:** `DrawCurrencyTradableRow` is drawn in the footer at full
  `inRect.width`, *outside* both scroll views, so it will not scroll/align with the columns once
  horizontal scroll is active. It also calls `DoCountAdjustInterface` **without setting**
  `TradeUIParameters.Singleton.isDrawingColonyItems` (line ~228), so the patched internal reads
  whatever the last row left — a pre-existing latent bug. Anything touching the footer should set
  that flag explicitly and re-anchor to the column model (see Change 2).

## Change 4 — Multiplayer parity (required)

The RimWorld Multiplayer mod (`rwmt.Multiplayer`) does not use `Dialog_Trade` on the window stack
— it opens its own `Multiplayer.Client.TradingWindow : Window` and, inside its
`DoWindowContents`, calls `dialog.DoWindowContents(inRect.AtZero())` on an inner `Dialog_Trade`
created via `NewObjectNoCtor` (verified against `rwmt/Multiplayer` `Source/Client/Persistent/
TradingUI.cs`). Two important consequences:

- **Changes 2 (columns) and 3 (horizontal scroll) already reach MP for free.** Because
  `TradingWindow` calls the inner `dialog.DoWindowContents`, the mod's `DoWindowContents` /
  `FillMainRect` transpilers *do* run in MP. `TradingWindow` does **not** reimplement row drawing.
  So Change 4 is smaller than it looks — no re-plumbing of the column/scroll code for MP.
- **What Change 4 actually needs:**
  1. **Resizable MP window + local size.** Patch the **`TradingWindow` constructor** (postfix
     adding `resizeable = true`) — *not* `PostOpen`: `TradingWindow` doesn't override `PostOpen`,
     so patching it would hit base `Verse.Window.PostOpen` and fire for every window (the same
     trap called out in Change 1). Do this alongside the existing `PatchTradingWindowWidth`.
  2. **Capture size from the right window.** The inner dialog's `windowRect` is `(0,0)` in MP
     (never stack-added), so the Change 1 capture must, under MP, read
     `Find.WindowStack.WindowOfType<TradingWindow>()?.windowRect` (reflection) instead of
     `__instance.windowRect`. Combined with the zero-guard from Change 1, this stops MP from
     zeroing the shared `windowSize` static.
  3. **Leave the count-widget guard returning vanilla under MP.** Do **not** relax
     `if (!Find.WindowStack.IsOpen<Dialog_Trade>()) return true;` (line ~670). MP disables count
     controls for non-negotiating factions by pattern-matching vanilla `Widgets.ButtonText` /
     `Widgets.TextFieldNumeric` labels; the mod's custom arrows use `DrawNormalButton` /
     `DrawGreyButton`, which MP can't detect — relaxing the guard would let a **non-negotiating**
     player edit the deal (session-ownership violation, though not a desync). Accepting the vanilla
     count widget in the MP window is the safe default. (If you later want the custom widget in MP,
     gate the interactive arrows on `MpTradeSession.current.NegotiatorFaction ==
     Multiplayer.RealPlayerFaction` via reflection.)
- **Determinism boundary — the key simplification:** window size, scroll position, and any column
  widths are display-only local UI. MP hashes deal/tradeable state (`MpTradeDeal.tradeables`,
  counts), not window geometry, so these need no MP sync and cannot desync. Verified: the fit
  paths don't touch `TradeSession.deal`; the row draw's existing `AdjustBy/AdjustTo` +
  `CountToTransferChanged` (lines ~481, 511, 612) already sync via MP's transferables marker set
  in `MpTradeSession.SetTradeSession` — unchanged by the fit fixes.
- **Gate all MP patches behind a load check.** Follow the existing `PatchTradingWindowWidth`
  pattern: `[HarmonyPatch]` + `Prepare()` (checks the `rwmt.Multiplayer` package) +
  `TargetMethod()` reflection, so the build never hard-references the MP assembly.
- **Investigate these pre-existing MP behaviors while here (test with two clients):**
  - *Gift-mode may not sync.* `MyDoWindowContents` sets `TradeSession.giftMode` + `deal.Reset()`
    directly (lines ~188, 200-201). Vanilla MP routes gift-mode through a `[SyncMethod]` via a
    transpiler on vanilla `Dialog_Trade.DoWindowContents` — but the mod's own transpiler deletes
    that IL range (line ~65), so MP never sees it. Confirm gift-mode toggles propagate.
  - *Transpiler ordering.* MP's `HandleToggleGiftMode` transpiler searches the same method and
    throws if its anchor is gone; this only works if MP patches before the mod. Check MP logs for
    Harmony patch failures — ordering is undefined, not guaranteed.

---

## Version control / Git workflow

- **Origin is the fork.** Already applied in this repo: `origin` →
  `https://github.com/raouldc/RimworldTradeUI.git`, `upstream` →
  `https://github.com/patrickdevarney/RimworldTradeUI.git`. (Commands below are idempotent —
  `remote add upstream` will harmlessly error "already exists" if re-run.)
  ```
  git remote set-url origin https://github.com/raouldc/RimworldTradeUI.git
  git remote add upstream https://github.com/patrickdevarney/RimworldTradeUI.git   # if not present
  git fetch upstream
  ```
  Note there is already an `origin/add-v1.6` branch upstream-side; base 1.6 work on current
  `main` (which includes the v1.6 merge) unless you specifically need that branch.
- **Branch per feature** off `main`, e.g. `feat/resizable-window`, `fix/column-overlap`,
  `feat/horizontal-scroll`, then the Phase 2 branches. Keeps each fit fix independently
  reviewable/revertible.
- **Commit granularity — one commit per logical change** so a regression can be bisected:
  1. `docs: add trade UI fit + UX plan`
  2. `feat: make trade window resizable with persisted size` (Change 1)
  3. `fix: prevent trade row column overlap via minRowWidth` (Change 2)
  4. `feat: add horizontal scrollbars to trade panes` (Change 3)
  5. `fix: anchor currency footer to column model` (footer)
  6. Phase 2 commits (button colors, running total, headers, bulk buttons, in-deal filter…),
     one per UX item.
- Push to the fork and open PRs against your own `main` (or straight commits to `main` if you're
  solo). Only PR to `upstream` if you intend to contribute the changes back.
- Reminder: pushing needs your GitHub credentials/token configured locally — the remote can be
  repointed and commits made offline, but `git push` will prompt for auth.

## Build & test (1.6 only)
1. Open `Source/TradeMod.sln`, restore RimWorld/Verse/Harmony refs. **The csproj currently has
   Windows-absolute paths that won't build here:** `HintPath` is a `C:\…` publicized
   `Assembly-CSharp` (line ~47) and `PostBuildEvent` copies to a `D:\…\v1.3\Assemblies` path
   (line ~73). Repoint `HintPath` to this machine's 1.6 publicized assembly and change the
   `PostBuildEvent` target to `TradeUI/v1.6/Assemblies/` (the `v1.6/Assemblies` folder already
   exists in the repo).
2. Because the mod transpiles game IL, the build must be validated against 1.6 specifically; do
   not assume the same DLL works on v1.3–v1.5 (out of scope this pass).
3. In-game checks (RimWorld 1.6):
   - Open a trade with a large-inventory trader (like the screenshot).
   - Shrink the window to minimum → columns stay separated, horizontal bar appears, prices
     readable; confirm the bars settle and don't oscillate.
   - Drag the window larger → panes grow, horizontal bar disappears when everything fits.
   - Reopen trade → window remembers the last size (and is clamped if resolution changed).
   - Test gift mode, "trader will not trade" rows, slavery-restricted rows, animal/captive rows
     (extra icons), and the silver footer row at both narrow and wide widths.
   - **Multiplayer (required):** run an actual MP session and verify resize, horizontal scroll,
     and column sizing all work in the `Multiplayer.Client.TradingWindow` (Change 4). Confirm
     window size / scroll stay local to each client and that no desync warning fires. Test with
     two clients where one resizes and the other does not.

## Risks / watch-outs
- `MyDoWindowContents` calls `GUI.EndGroup()` (line 209) to close the group opened by vanilla
  `DoWindowContents`; keep that balanced if you add any `BeginGroup`.
- Extra-icon width (`DoExtraIcons`, `DrawCaptiveTradeInfo`) is variable — fold a small allowance
  into `minRowWidth` or measure it, or animals/ideology rows can still crowd.
- Persisted size must be clamped to current screen (resolution can change between sessions).
- The `DoCountAdjustInterfaceInternal` prefix has its own fixed 120px transfer geometry
  (line ~690) that must stay consistent with `TRANSFER_WIDTH` (keep `TRANSFER_WIDTH ≥ ~137`).
- **State leaks:** new header/color code (UX-B/UX-E) must save/restore `GUI.color`, `Text.Anchor`,
  and `Text.WordWrap` itself — existing code leans on the next row / `GenUI.ResetLabelAlign()` to
  reset, and `MyDrawTransferableInfoSilver` leaves `Text.WordWrap = true` (line ~308). Don't add
  to that debt.
- The currency footer must set `TradeUIParameters.Singleton.isDrawingColonyItems` before calling
  `DoCountAdjustInterface` (pre-existing bug — see Change 3).

## Suggested order (fit fixes)
1. Change 1 (resizable + persistence) — quick win, lets you reproduce/verify at any size.
2. Change 2 (column constants + minRowWidth) — the actual overlap fix.
3. Change 3 (horizontal scroll) — depends on `minRowWidth` from Change 2.
4. Currency footer + optional draggable columns.
5. Change 4 (Multiplayer parity) — mirror 1–3 onto `TradingWindow`; do the MP render-path
   investigation early since it may affect how 2–3 are structured.

---

# Phase 2 — UX improvements

These build on the layout work above. Several are listed in the mod's own README as
"future plans." (Search box intentionally excluded.)

## UX-A — Bulk / "max money" buttons
The pain the README calls out: fiddling to find the exact quantity that zeroes out silver.
- **Ship the easy win first — "sell all / buy all" per row:** a visible button that calls
  `trad.AdjustTo(trad.GetMaximumToTransfer())`. Right-click on the arrows already does this in
  `DoCountAdjustInterfaceInternal` — just surface it as an explicit control.
- **"Max money" is a separate, bigger task — do not build it on `GetMaximumToTransfer()`.**
  `Tradeable.GetMaximumToTransfer()` bounds by **available stock on the source side, not by
  silver/affordability** (for player-buys it returns the trader's stock, which can far exceed
  what you can pay; the only silver check today is `DoesTraderHaveEnoughSilver()` at execute
  time, line ~146). A real "zero out silver" button needs per-unit price math — iterate the
  target quantity using `trad.GetPriceFor(...)`/`PriceFor` against the currency tradeable's
  running balance until silver hits ~0. Scope this as its own item, not a one-liner.
- Place any new button in the transfer block; fold its width into `minRowWidth` (Change 2) so it
  doesn't reintroduce overlap.
- **Multiplayer:** unlike the fit fixes, bulk/max buttons mutate the *deal*, so they **do** need
  MP sync like the vanilla adjust paths (route through the MP action layer). This is the one place
  MP sync is required — the resize/scroll/column work stays local (Change 4).

## UX-B — Column headers (+ optional click-to-sort)
Right now the panes have no labels, so nothing identifies qty vs price vs transfer amount.
- In the `FillMainRect` prefix, draw a one-line header row above each scroll view: `Item`,
  `Owned`, `Price`, `Trade` — anchored to the same column model as `MyDrawTradableRow`
  (reuse the `OWNED_AMOUNT_WIDTH` / `COST_WIDTH` / `TRANSFER_WIDTH` constants so headers line
  up with cells).
- Keep headers outside the scroll view so they stay pinned while rows scroll.
- (Optional) Make headers clickable to sort `___cachedTradeables` by that field, giving
  per-column sorting independent of the vanilla "Sort by" bar. Store the active sort in
  `TradeUIParameters` and apply it before the draw loop.

## UX-C — "In this deal" filter / highlight
Let the player review exactly what they're about to trade.
- Add a toggle button (near the footer) that, when on, skips rows where
  `CountToTransfer == 0` in both draw loops.
- Even without the toggle, visually mark active rows (rows with `CountToTransfer != 0`) — e.g.
  a subtle tint via `Widgets.DrawHighlight` in `MyDrawTradableRow` — so committed items stand
  out from the noise.

## UX-D — Live running total by the buttons
README complaint: the cost is far from Accept/Cancel.
- Compute the net silver delta of the current deal and draw it as a compact label directly to
  the left of/above the Accept button in `MyDoWindowContents` (the button geometry is already
  there around lines ~101-104).
- Color it red and/or disable Accept when the colony can't afford it
  (`TradeSession.deal.DoesTraderHaveEnoughSilver()` is already checked on Accept — surface that
  state continuously instead of only on click).

## UX-E — Persistence & button-color polish
- To remember `scrollPositionLeft/Right` across opens you must **gate the existing `Reset()`**:
  `Harmony_DialogTrade_PostOpen` (a Prefix, line ~1119) calls `TradeUIParameters.Singleton.Reset()`
  which zeroes both scroll positions on every open (TradeUIParameters.cs:19-24). This is the same
  code path Change 1 touches — either stop clearing scroll there or make it opt-in. Adding
  persistence elsewhere without changing `Reset()` will do nothing.
- Persist any user-set column widths (if UX-B/draggable columns land) in the same place, or in
  a `ModSettings` for cross-session storage. (`windowSize` from Change 1 lives here too.)
- Color the action buttons for clarity (README plan): Accept green, Cancel red, Reset neutral.
  `MyDoWindowContents` already draws these via `Widgets.ButtonText` — wrap each in a
  `GUI.color` set/restore. Cancel already reads red-ish; make it deliberate.

## Phase 2 suggested order
1. UX-E button colors (trivial) and UX-D running total (high clarity, low effort).
2. UX-B column headers — pairs directly with the Change 2 column work.
3. UX-A bulk/max buttons.
4. UX-C in-deal filter/highlight.
5. UX-B click-to-sort and column-width persistence (stretch).
