using Verse;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using RimWorld.Planet;
using System.Reflection;
using System.Reflection.Emit;
using System;
using System.Linq;
using Verse.Sound;
using System.Collections;

/*
 * TODO list
 *
 */

namespace TradeUI
{
    [StaticConstructorOnStartup]
    static class TradeUIRework
    {
        static TradeUIRework()
        {
            //Harmony.DEBUG = true;
            Harmony harm = new Harmony("rimworld.hobtook.tradeui");
            harm.Patch(AccessTools.Method(typeof(RimWorld.Dialog_Trade), nameof(RimWorld.Dialog_Trade.DoWindowContents), null, null), null, null, new HarmonyMethod(typeof(TradeUIRework), nameof(TradeUIRework.DoWindowContentsTranspiler), null), null);
            harm.Patch(AccessTools.Method(typeof(RimWorld.Dialog_Trade), nameof(RimWorld.Dialog_Trade.FillMainRect), null, null), null, null, new HarmonyMethod(typeof(TradeUIRework), nameof(TradeUIRework.FillMainRectTranspiler), null), null);
            harm.PatchAll();
            //Harmony.DEBUG = false;
        }

        static IEnumerable<CodeInstruction> DoWindowContentsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            MethodInfo info = AccessTools.Method(typeof(TransferableUIUtility), nameof(TransferableUIUtility.DoTransferableSorters), new Type[]
            {
                typeof(TransferableSorterDef),
                typeof(TransferableSorterDef),
                typeof(Action<TransferableSorterDef>),
                typeof(Action<TransferableSorterDef>),
            }, null);
            int startIndex = list.FindIndex((CodeInstruction ins) => CodeInstructionExtensions.Calls(ins, info));
            startIndex++;
            bool foundFirstInstance = false;
            int endIndex = -1;
            for (int i = startIndex; i < list.Count; i++)
            {
                if (list[i].LoadsField(AccessTools.Field(typeof(TradeSession), nameof(TradeSession.giftMode))))
                {
                    if (foundFirstInstance)
                    {
                        endIndex = i - 1;
                        break;
                    }
                    else
                    {
                        foundFirstInstance = true;
                        continue;
                    }
                }
            }
            //list.RemoveRange(startIndex, endIndex - startIndex);
            list.RemoveRange(startIndex, list.Count - startIndex - 1);
            list.InsertRange(startIndex, new CodeInstruction[]
            {
                //new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradeUIRework), "MyDoWindowRect", new Type[]{typeof(Rect) }, null))
                new CodeInstruction(OpCodes.Ldarg_0, null),
                new CodeInstruction(OpCodes.Ldarga, 1), //Rect inRect
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradeUIRework), nameof(TradeUIRework.MyDoWindowContents))),
            });

            return list.AsEnumerable();
        }

        static void MyDoWindowContents(Dialog_Trade __instance, ref Rect inRect)
        {
            //Debug.LogError($"It works! Rect {inRect.ToString()}");

            // Change 1/4: capture the current window size each frame so it persists across opens.
            // In MP the inner dialog is created via NewObjectNoCtor and is never stack-added, so its
            // windowRect stays (0,0); read the real TradingWindow's rect instead. The zero-guards stop
            // MP from ever overwriting the shared static with (0,0).
            Vector2 mpSize = GetMultiplayerWindowSize();
            if (mpSize.x > 1f && mpSize.y > 1f)
            {
                TradeUIParameters.windowSize = mpSize;
            }
            else if (__instance.windowRect.width > 1f && __instance.windowRect.height > 1f)
            {
                TradeUIParameters.windowSize = __instance.windowRect.size;
            }

            // Calculate space for left/right rects
            const float FOOTER_HEIGHT = 110;
            const float BUTTON_HEIGHT = 55;
            Rect twoColumnRect = new Rect(0f, inRect.yMin + TransferableUIUtility.SortersHeight, inRect.width, inRect.height - FOOTER_HEIGHT - TransferableUIUtility.SortersHeight);
            //Debug.LogError(twoColumnRect.ToString());
            //Debug.LogError($"{twoColumnRect.width},{twoColumnRect.height} {twoColumnRect.xMin}:{twoColumnRect.xMax} {twoColumnRect.yMin}:{twoColumnRect.yMax}");
            // DRAW THE LEFT/RIGHT AREAS
            __instance.FillMainRect(twoColumnRect);

            // Draw footer (replaces original entirely)
            Rect footerSilverRect = new Rect(0f, inRect.height - FOOTER_HEIGHT + 3, inRect.width, FOOTER_HEIGHT - BUTTON_HEIGHT - 13);//FOOTER_HEIGHT - 55);
            if (__instance.cachedCurrencyTradeable != null)
            {
                GUI.color = Color.gray;
                Widgets.DrawLineHorizontal(0f, footerSilverRect.yMin, inRect.width);
                GUI.color = Color.white;
                DrawCurrencyTradableRow(new Rect(0f, footerSilverRect.yMin + 5, footerSilverRect.width, footerSilverRect.height), __instance.cachedCurrencyTradeable, true);
            }

            // Draw bottom buttons
            Text.Font = GameFont.Small;
            Rect buttonsRect = new Rect(inRect.width / 2f - Dialog_Trade.AcceptButtonSize.x / 2f,
                inRect.height - BUTTON_HEIGHT,
                Dialog_Trade.AcceptButtonSize.x,
                Dialog_Trade.AcceptButtonSize.y);

            // UX-D: live net-silver running total next to the Accept/Reset cluster. Red when the
            // colony can't afford the deal (same check the Accept path uses, surfaced continuously).
            if (__instance.cachedCurrencyTradeable != null)
            {
                int silverDelta = __instance.cachedCurrencyTradeable.CountToTransfer;
                bool canAfford = TradeSession.deal.DoesTraderHaveEnoughSilver();
                float resetLeft = buttonsRect.x - 10f - Dialog_Trade.OtherBottomButtonSize.x;
                const float totalX = 160f; // leave room for the UX-C filter toggle at the far left
                Rect totalRect = new Rect(totalX, buttonsRect.y, Mathf.Max(0f, resetLeft - 10f - totalX), Dialog_Trade.OtherBottomButtonSize.y);
                TextAnchor prevTotalAnchor = Text.Anchor;
                Color prevTotalColor = GUI.color;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = canAfford ? Color.white : new Color(1f, 0.4f, 0.4f);
                Widgets.Label(totalRect, "Silver: " + silverDelta.ToStringWithSign());
                Text.Anchor = prevTotalAnchor;
                GUI.color = prevTotalColor;
            }

            // end draw footer (replaces original code entirely)
            // WHAT IS THIS? start
            bool hasTradablesSet = false;
            foreach (Tradeable t in TradeSession.deal.tradeables)
            {
                if (t.ActionToDo == TradeAction.PlayerBuys || t.ActionToDo == TradeAction.PlayerSells)
                {
                    hasTradablesSet = true;
                    break;
                }
            }
            if (!hasTradablesSet)
            {
                DrawGreyButton(new Rect(buttonsRect.x - 10f - Dialog_Trade.OtherBottomButtonSize.x, buttonsRect.y, Dialog_Trade.OtherBottomButtonSize.x, Dialog_Trade.OtherBottomButtonSize.y), "ResetButton".Translate(), true, Color.gray);
                DrawGreyButton(buttonsRect, TradeSession.giftMode ? "OfferGifts".Translate() : "AcceptButton".Translate(), true, Color.gray);
            }
            else
            // WHAT IS THIS? end
            {
                // UX-E: Accept/Offer button = green.
                Color prevAcceptColor = GUI.color;
                GUI.color = new Color(0.55f, 0.9f, 0.55f);
                bool acceptClicked = Widgets.ButtonText(buttonsRect, TradeSession.giftMode ? ("OfferGifts".Translate() + " (" + FactionGiftUtility.GetGoodwillChange(TradeSession.deal.AllTradeables, TradeSession.trader.Faction).ToStringWithSign() + ")") : "AcceptButton".Translate(), true, true, true);
                GUI.color = prevAcceptColor;
                if (acceptClicked)
                {
                    System.Action action = delegate ()
                    {
                        bool flag;
                        if (TradeSession.deal.TryExecute(out flag))
                        {
                            if (flag)
                            {
                                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera(null);
                                Caravan caravan = TradeSession.playerNegotiator.GetCaravan();
                                if (caravan != null)
                                {
                                    caravan.RecacheInventory();
                                }
                                __instance.Close(false);
                                return;
                            }
                            __instance.Close(true);
                        }
                    };
                    if (TradeSession.deal.DoesTraderHaveEnoughSilver())
                    {
                        action();
                    }
                    else
                    {
                        __instance.FlashSilver();
                        SoundDefOf.ClickReject.PlayOneShotOnCamera(null);
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmTraderShortFunds".Translate(), action, false, null, WindowLayer.Dialog));
                    }
                    Event.current.Use();
                }

                if (Widgets.ButtonText(new Rect(buttonsRect.x - 10f - Dialog_Trade.OtherBottomButtonSize.x, buttonsRect.y, Dialog_Trade.OtherBottomButtonSize.x, Dialog_Trade.OtherBottomButtonSize.y), "ResetButton".Translate(), true, true, true))
                {
                    Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_Low, null);
                    TradeSession.deal.Reset();
                    __instance.CacheTradeables();
                    __instance.CountToTransferChanged();
                }
            }

            // UX-E: Cancel button = red (deliberate, was only incidentally reddish before).
            Color prevCancelColor = GUI.color;
            GUI.color = new Color(0.9f, 0.5f, 0.5f);
            bool cancelClicked = Widgets.ButtonText(new Rect(buttonsRect.xMax + 10f, buttonsRect.y, Dialog_Trade.OtherBottomButtonSize.x, Dialog_Trade.OtherBottomButtonSize.y), "CancelButton".Translate(), true, true, true);
            GUI.color = prevCancelColor;
            if (cancelClicked)
            {
                __instance.Close(true);
                Event.current.Use();
            }
            float y = Dialog_Trade.OtherBottomButtonSize.y;
            Rect rect5 = new Rect(inRect.width - y, buttonsRect.y, y, y);
            if (Widgets.ButtonImageWithBG(rect5, Dialog_Trade.ShowSellableItemsIcon, new Vector2?(new Vector2(32f, 32f))))
            {
                Find.WindowStack.Add(new Dialog_SellableItems(TradeSession.trader));
            }
            TooltipHandler.TipRegionByKey(rect5, "CommandShowSellableItemsDesc");
            Faction faction = TradeSession.trader.Faction;
            if (faction != null && !__instance.giftsOnly && !faction.def.permanentEnemy)
            {
                Rect rect6 = new Rect(rect5.x - y - 4f, buttonsRect.y, y, y);
                if (TradeSession.giftMode)
                {
                    if (Widgets.ButtonImageWithBG(rect6, Dialog_Trade.TradeModeIcon, new Vector2?(new Vector2(32f, 32f))))
                    {
                        TradeSession.giftMode = false;
                        TradeSession.deal.Reset();
                        __instance.CacheTradeables();
                        __instance.CountToTransferChanged();
                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                    }
                    TooltipHandler.TipRegionByKey(rect6, "TradeModeTip");
                }
                else
                {
                    if (Widgets.ButtonImageWithBG(rect6, Dialog_Trade.GiftModeIcon, new Vector2?(new Vector2(32f, 32f))))
                    {
                        TradeSession.giftMode = true;
                        TradeSession.deal.Reset();
                        __instance.CacheTradeables();
                        __instance.CountToTransferChanged();
                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                    }
                    TooltipHandler.TipRegionByKey(rect6, "GiftModeTip", faction.Name);
                }
            }

            // "Show" filter dropdown at the far left of the button row. Opens a menu of display
            // toggles (in-deal only, hide items the trader won't buy). Display-only local state,
            // so it is safe under MP (no deal mutation).
            Rect filterRect = new Rect(0f, buttonsRect.y, 150f, Dialog_Trade.OtherBottomButtonSize.y);
            bool inDealOnly = TradeUIParameters.Singleton.filterInDealOnly;
            bool hideUnwilling = TradeUIParameters.Singleton.hideUnwillingToBuy;
            string filterLabel = (inDealOnly || hideUnwilling) ? "Show: filtered" : "Show: all items";
            if (Widgets.ButtonText(filterRect, filterLabel, true, true, true))
            {
                List<FloatMenuOption> filterOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption((inDealOnly ? "✓ " : "     ") + "Only items in the deal", () =>
                    {
                        TradeUIParameters.Singleton.filterInDealOnly = !TradeUIParameters.Singleton.filterInDealOnly;
                    }),
                    new FloatMenuOption((hideUnwilling ? "✓ " : "     ") + "Hide items the trader won't buy", () =>
                    {
                        TradeUIParameters.Singleton.hideUnwillingToBuy = !TradeUIParameters.Singleton.hideUnwillingToBuy;
                    }),
                };
                Find.WindowStack.Add(new FloatMenu(filterOptions));
                Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
            }
            TooltipHandler.TipRegion(filterRect, new TipSignal("Filter which items are shown (in-deal only, hide items the trader won't buy)."));

            GUI.EndGroup();

        }

        public static void DrawCurrencyTradableRow(Rect rect, Tradeable trad, bool highlight)
        {
            if (highlight)
            {
                Widgets.DrawLightHighlight(rect);
            }

            // [        icon [i] Silver     my amount      < transfer amount        their amount            ]

            // Save GUI state so this footer never leaks Anchor/WordWrap/color into later draws.
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Small;
            GUI.BeginGroup(rect);

            // Footer bug fix (Change 3): DoCountAdjustInterfaceInternal reads isDrawingColonyItems,
            // which the footer never set - it inherited whatever the last row left. Set it explicitly.
            TradeUIParameters.Singleton.isDrawingColonyItems = true;

            float center = rect.width / 2f;
            // Re-anchor the centre-relative offsets to the column model and clamp them so the amounts
            // stay on-screen at narrow widths (Change 2).
            float sideOffset = Mathf.Min(300f, center - 60f);

            // Draw transfer amount
            Rect transferRect = new Rect(center - (240f / 2f), 0f, 240f, rect.height);
            bool flash = Time.time - Dialog_Trade.lastCurrencyFlashTime < 1f && trad.IsCurrency;
            TransferableUIUtility.DoCountAdjustInterface(transferRect, trad, 0, trad.GetMinimumToTransfer(), trad.GetMaximumToTransfer(), flash, null, false);
            //GUI.Label(transferRect, "||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||");

            // Draw owned amount
            int ourAmount = trad.CountHeldBy(Transactor.Colony);
            //if (ourAmount != 0)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                float ourAmountWidth = 100f;
                var ourRect = new Rect(center - (ourAmountWidth / 2f) - sideOffset, 0f, ourAmountWidth, rect.height);
                if (Mouse.IsOver(ourRect))
                {
                    Widgets.DrawHighlight(ourRect);
                }
                Rect rect8 = ourRect;
                rect8.xMin += 5f;
                rect8.xMax -= 5f;
                Widgets.Label(rect8, ourAmount.ToStringCached());
                TooltipHandler.TipRegionByKey(ourRect, "ColonyCount");
            }

            // Draw their amount
            int theirAmount = trad.CountHeldBy(Transactor.Trader);
            //if (theirAmount != 0 && trad.IsThing)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                float theirAmountWidth = 100f;
                var theirRect = new Rect(center - (theirAmountWidth / 2f) + sideOffset, 0f, theirAmountWidth, rect.height);
                if (Mouse.IsOver(theirRect))
                {
                    Widgets.DrawHighlight(theirRect);
                }
                Rect rect3 = theirRect;
                rect3.xMin += 5f;
                rect3.xMax -= 5f;
                Widgets.Label(rect3, theirAmount.ToStringCached());
                TooltipHandler.TipRegionByKey(theirRect, "TraderCount");
            }

            // Draw icon, info, name
            float num = rect.width;
            //Log.Message($" full silver rect = {rect.x}, {rect.y}, {rect.width}, {rect.height}");
            Rect idRect = new Rect(0f, 0, num, rect.height);
            //Log.Message($" silver id rect = {idRect.x}, {idRect.y}, {idRect.width}, {idRect.height}");
            //TransferableUIUtility.DrawTransferableInfo(trad, idRect, trad.TraderWillTrade ? Color.white : RimWorld.TradeUI.NoTradeColor);
            MyDrawTransferableInfoSilver(trad, idRect, trad.TraderWillTrade ? Color.white : RimWorld.TradeUI.NoTradeColor);
            //GUI.Label(idRect, "||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||");
            GenUI.ResetLabelAlign();
            GUI.EndGroup();

            // Restore state
            Text.Anchor = oldAnchor;
            Text.WordWrap = oldWrap;
            GUI.color = oldColor;
        }

        static void MyDrawTransferableInfoSilver(Transferable trad, Rect idRect, Color labelColor)
        {
            if (!trad.HasAnyThing && trad.IsThing)
            {
                return;
            }
            if (Mouse.IsOver(idRect))
            {
                Widgets.DrawHighlight(idRect);
            }
            Rect rect = new Rect(0f, (idRect.height - 27) / 2f, 27f, 27f);
            if (trad.IsThing)
            {
                Widgets.ThingIcon(rect, trad.AnyThing, 1f, null);
            }
            else
            {
                trad.DrawIcon(rect);
            }
            if (trad.IsThing)
            {
                Widgets.InfoCardButton(40f, (idRect.height / 2f) - 12f, trad.AnyThing);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect rect2 = new Rect(80f, 0f, idRect.width - 80f, idRect.height);
            Text.WordWrap = false;
            GUI.color = labelColor;
            Widgets.Label(rect2, trad.LabelCap);
            GUI.color = Color.white;
            Text.WordWrap = true;
            if (Mouse.IsOver(idRect))
            {
                Transferable localTrad = trad;
                TooltipHandler.TipRegion(idRect, new TipSignal(delegate ()
                {
                    if (!localTrad.HasAnyThing && localTrad.IsThing)
                    {
                        return "";
                    }
                    string text = localTrad.LabelCap;
                    string tipDescription = localTrad.TipDescription;
                    if (!tipDescription.NullOrEmpty())
                    {
                        text = text + ": " + tipDescription + TransferableUIUtility.ContentSourceDescription(localTrad.AnyThing);
                    }
                    return text;
                }, localTrad.GetHashCode()));
            }
        }

        static void DrawGreyButton(Rect rect, string label, bool drawBackground, Color textColor)
        {
            TextAnchor anchor = Text.Anchor;
            Color originalColor = GUI.color;
            if (drawBackground)
            {
                Texture2D atlas = Widgets.ButtonSubtleAtlas;

                var buttonRect = rect.ContractedBy(1);
                Widgets.DrawAtlas(buttonRect, atlas);
            }
            GUI.color = textColor;
            if (!drawBackground)
            {
                GUI.color = textColor;
                if (Mouse.IsOver(rect))
                {
                    GUI.color = Widgets.MouseoverOptionColor;
                }
            }
            if (drawBackground)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleLeft;
            }
            bool wordWrap = Text.WordWrap;
            if (rect.height < Text.LineHeight * 2f)
            {
                Text.WordWrap = false;
            }
            Widgets.Label(rect, label);
            Text.Anchor = anchor;
            GUI.color = originalColor;
            Text.WordWrap = wordWrap;
        }

        static IEnumerable<CodeInstruction> FillMainRectTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            // Calculate left/right header size
            // Draw headers
            // Draw colony name
            // Draw negotiator name
            // Draw trader name
            // Draw type of trader
            return list.AsEnumerable();
        }

        [HarmonyPatch(typeof(RimWorld.Dialog_Trade), "FillMainRect")]
        public static class Harmony_DialogTrade_FillMainRect
        {
            // Change 2: shared column widths so the row draw, the min-width calc, and (later)
            // the column headers all line up. Keep TRANSFER_WIDTH >= 137: DoCountAdjustInterfaceInternal
            // hardcodes a 120px+margins mini-rect anchored to rect.xMax.
            public const float COST_WIDTH = 90f;
            public const float TRANSFER_WIDTH = 160f;
            public const float OWNED_AMOUNT_WIDTH = 75f;
            public const float ICON_INFO_WIDTH = 80f;   // icon (27) + info button + padding; name starts at x=80
            public const float NAME_MIN_WIDTH = 140f;    // minimum readable space reserved for the label
            public const float BULK_WIDTH = 64f;         // UX-A: "All" + "$" bulk buttons (SP only)

            // Minimum content width a row needs so the right-anchored blocks always have room and the
            // name rect never collapses. extraIconWidth is measured live (see MyDrawTradableRow).
            public static float MinRowWidth()
            {
                return ICON_INFO_WIDTH + NAME_MIN_WIDTH + OWNED_AMOUNT_WIDTH + COST_WIDTH + TRANSFER_WIDTH + BULK_WIDTH
                    + TradeUIParameters.maxExtraIconWidth;
            }

            public const float COL_HEADER_HEIGHT = 20f;

            // UX-B: pinned column headers aligned to the shared column model. Kept outside the scroll
            // views so they stay put while rows scroll. rowRect is one pane's header strip.
            static void DrawColumnHeaders(Rect rowRect)
            {
                TextAnchor prevAnchor = Text.Anchor;
                GameFont prevFont = Text.Font;
                Color prevColor = GUI.color;
                bool prevWrap = Text.WordWrap;

                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);

                float x = rowRect.width;
                x -= TRANSFER_WIDTH;
                DrawHeaderCell(new Rect(rowRect.x + x, rowRect.y, TRANSFER_WIDTH, rowRect.height), "Trade", TextAnchor.MiddleCenter);
                // Match the row layout: in SP the bulk buttons sit between Trade and Price, so reserve
                // the same gap here or the Price/Owned headers drift right of their cells.
                if (Find.WindowStack.IsOpen<Dialog_Trade>())
                {
                    x -= BULK_WIDTH;
                }
                x -= COST_WIDTH;
                DrawHeaderCell(new Rect(rowRect.x + x, rowRect.y, COST_WIDTH, rowRect.height), "Price", TextAnchor.MiddleRight);
                x -= OWNED_AMOUNT_WIDTH;
                DrawHeaderCell(new Rect(rowRect.x + x, rowRect.y, OWNED_AMOUNT_WIDTH, rowRect.height), "Owned", TextAnchor.MiddleRight);
                DrawHeaderCell(new Rect(rowRect.x + ICON_INFO_WIDTH, rowRect.y, Mathf.Max(0f, x - ICON_INFO_WIDTH), rowRect.height), "Item", TextAnchor.MiddleLeft);

                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
                Text.WordWrap = prevWrap;
            }

            static void DrawHeaderCell(Rect rect, string label, TextAnchor anchor)
            {
                Text.Anchor = anchor;
                Widgets.Label(rect, label);
            }

            // UX-A: "All" sets the max transferable quantity (the reliable easy win). "$" tries to set
            // the quantity that spends/earns as close to all available silver as possible.
            static void DrawBulkButtons(Rect rect, Tradeable trad, bool isOurs)
            {
                float h = Mathf.Min(rect.height - 6f, 22f);
                float y = rect.y + (rect.height - h) / 2f;
                Rect allRect = new Rect(rect.x + 2f, y, 28f, h);
                Rect maxRect = new Rect(allRect.xMax + 2f, y, 28f, h);

                GameFont prevFont = Text.Font;
                Text.Font = GameFont.Tiny;
                if (Widgets.ButtonText(allRect, "All", true, true, true))
                {
                    trad.AdjustTo(trad.GetMaximumToTransfer());
                    Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                }
                TooltipHandler.TipRegion(allRect, new TipSignal("Sell/buy the maximum available quantity of this item."));

                if (Widgets.ButtonText(maxRect, "$", true, true, true))
                {
                    AdjustToMaxMoney(trad, isOurs);
                    Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                }
                TooltipHandler.TipRegion(maxRect, new TipSignal("Set the quantity that spends (or earns) as close to all available silver as possible."));
                Text.Font = prevFont;
            }

            // Per-unit price math (does NOT rely on GetMaximumToTransfer, which bounds by stock, not by
            // affordability). Feature 4 bug fix: the budget is the *remaining* silver after the rest of
            // the deal, not the colony's/trader's total holdings, so clicking "$" on several rows in a
            // row stays solvent. We read the currency tradeable's CountPostDealFor(...) - vanilla keeps
            // that in lockstep with the whole deal via UpdateCurrencyCount() (called every frame) - which
            // already equals holdings minus silver committed across all rows. We add back THIS row's own
            // currently-committed silver so re-clicking "$" on the same row is idempotent instead of
            // shrinking it to zero. "$" is SP-only (gated by IsOpen<Dialog_Trade>() at the call site).
            static void AdjustToMaxMoney(Tradeable trad, bool isOurs)
            {
                int max = trad.GetMaximumToTransfer();
                if (trad.IsCurrency)
                {
                    trad.AdjustTo(max);
                    return;
                }
                Dialog_Trade dlg = Find.WindowStack.WindowOfType<Dialog_Trade>();
                Tradeable currency = (dlg != null) ? dlg.cachedCurrencyTradeable : null;
                if (currency == null)
                {
                    trad.AdjustTo(max);
                    return;
                }
                TradeAction action = isOurs ? TradeAction.PlayerSells : TradeAction.PlayerBuys;
                float unitPrice = trad.GetPriceFor(action);
                if (unitPrice <= 0f)
                {
                    trad.AdjustTo(max);
                    return;
                }
                // Buying spends the colony's silver; selling is bounded by the trader's silver.
                Transactor budgetHolder = isOurs ? Transactor.Trader : Transactor.Colony;
                // Silver left for the buyer after everything currently in the deal.
                float remaining = currency.CountPostDealFor(budgetHolder);
                // Add back this row's own commitment so the button recomputes from a clean slate.
                remaining += Mathf.Abs(trad.CountToTransfer) * unitPrice;
                if (remaining < 0f)
                {
                    remaining = 0f;
                }
                int affordable = Mathf.FloorToInt(remaining / unitPrice);
                int stock = Mathf.Abs(max);
                int target = Mathf.Min(affordable, stock);
                // Preserve the transfer direction that GetMaximumToTransfer encodes.
                trad.AdjustTo(max >= 0 ? target : -target);
            }

            static bool Prefix(ref UnityEngine.Rect mainRect, ref List<Tradeable> ___cachedTradeables, ref Dialog_Trade __instance)
            {
                // Draw headers
                float halfWidth = mainRect.width / 2f;
                // Draw left header
                Rect leftHeaderRect = new Rect(0, mainRect.y, halfWidth, 85);
                //Log.Message($"[TradeUI] leftHeaderRect ({leftHeaderRect.x}, {leftHeaderRect.y},{leftHeaderRect.width},{leftHeaderRect.height})");
                // Draw colony name
                GUI.BeginGroup(leftHeaderRect);
                var colonyNameRect = new Rect(0, 0, leftHeaderRect.width, leftHeaderRect.height);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(colonyNameRect, Faction.OfPlayer.Name.Truncate(colonyNameRect.width, null));
                // Draw negotiator name
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(0, 30f, leftHeaderRect.width, leftHeaderRect.height - 30),
                    "NegotiatorTradeDialogInfo".Translate(TradeSession.playerNegotiator.Name.ToStringFull,
                    TradeSession.playerNegotiator.GetStatValue(StatDefOf.TradePriceImprovement,
                    true).ToStringPercent()));

                leftHeaderRect.height -= 30;

                // TODO: fix one pixel missing between left/right horizontal lines (but drawing it all in one go caused scroll bar to render on top of white line)
                GUI.color = Color.gray;
                Widgets.DrawLineHorizontal(0f, leftHeaderRect.height - 1, leftHeaderRect.width);
                GUI.color = Color.white;

                GUI.EndGroup();

                // Draw right header
                Rect rightHeaderRect = new Rect(halfWidth, mainRect.y, halfWidth, 85);
                //Log.Message($"[TradeUI] rightHeaderRect ({rightHeaderRect.x}, {rightHeaderRect.y},{rightHeaderRect.width},{rightHeaderRect.height})");
                // Draw trader name
                GUI.BeginGroup(rightHeaderRect);
                var traderNameRect = new Rect(0, 0, rightHeaderRect.width, rightHeaderRect.height);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperCenter;
                string text = TradeSession.trader.TraderName;
                if (Text.CalcSize(text).x > traderNameRect.width)
                {
                    Text.Font = GameFont.Small;
                    text = text.Truncate(traderNameRect.width, null);
                }
                Widgets.Label(traderNameRect, text);
                // Draw type of trader
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(0, 30f, rightHeaderRect.width, rightHeaderRect.height - 30), TradeSession.trader.TraderKind.LabelCap);

                rightHeaderRect.height -= 30;

                GUI.color = Color.gray;
                Widgets.DrawLineHorizontal(0f, rightHeaderRect.height - 1, rightHeaderRect.width);
                GUI.color = Color.white;

                GUI.EndGroup();

                // Draw vertical divider
                /*GUI.BeginGroup(mainRect);
                Widgets.DrawLineVertical(halfWidth - 1, leftHeaderRect.height, mainRect.height - leftHeaderRect.height);
                GUI.EndGroup();*/

                // Calculate scroll height
                // UX-C: when the in-deal filter is on, only count rows that are part of the deal so
                // the scroll height matches the (fewer) rows actually drawn.
                bool filterInDeal = TradeUIParameters.Singleton.filterInDealOnly;
                bool hideUnwilling = TradeUIParameters.Singleton.hideUnwillingToBuy;
                // Feature 2: in gift mode the trade is one-sided (colony gives), so suppress the trader
                // pane and let the colony pane span the full window. The scroll-height loop below still
                // stays in lockstep because it counts the same rows that get drawn.
                bool giftMode = TradeSession.giftMode;
                float leftPaneWidth = giftMode ? mainRect.width : halfWidth;
                float leftHeight = 6f;
                float rightHeight = 6f;
                foreach (var entry in ___cachedTradeables)
                {
                    bool inDeal = !filterInDeal || entry.CountToTransfer != 0;
                    bool willing = !hideUnwilling || entry.TraderWillTrade;
                    if (inDeal && willing && entry.thingsColony != null && entry.thingsColony.Count > 0)
                        leftHeight += 30f;
                    if (inDeal && willing && entry.thingsTrader != null && entry.thingsTrader.Count > 0)
                        rightHeight += 30f;
                }

                // Draw left view
                Text.Font = GameFont.Small;
                // UX-B: draw the pinned column headers just above the scroll view, then start the
                // scroll rect below them.
                Rect leftColHeaderRect = new Rect(0, mainRect.y + leftHeaderRect.height, leftPaneWidth - 16f, COL_HEADER_HEIGHT);
                DrawColumnHeaders(leftColHeaderRect);
                // Start scroll rect down a bit vertically
                Rect leftScrollRect = new Rect(0, mainRect.y + leftHeaderRect.height + COL_HEADER_HEIGHT, leftPaneWidth, mainRect.height - leftHeaderRect.height - COL_HEADER_HEIGHT);
                // Change 3: make the content width independent of the pane width so a horizontal
                // scrollbar appears (and the columns keep their fixed sizes) when the pane is narrow.
                float minRowWidth = MinRowWidth();
                float leftContentWidth = Mathf.Max(leftScrollRect.width - 16f, minRowWidth);
                Rect leftInsideScrollRect = new Rect(0, 0, leftContentWidth, leftHeight);
                Widgets.BeginScrollView(leftScrollRect, ref TradeUIParameters.Singleton.scrollPositionLeft, leftInsideScrollRect, true);
                float num = 6f;
                float num2 = TradeUIParameters.Singleton.scrollPositionLeft.y - 30f;
                float num3 = TradeUIParameters.Singleton.scrollPositionLeft.y + leftScrollRect.height;
                int num4 = 0;
                for (int i = 0; i < ___cachedTradeables.Count; i++)
                {
                    // Only draw stuff we have
                    if (___cachedTradeables[i].thingsColony == null || ___cachedTradeables[i].thingsColony.Count == 0)
                        continue;
                    // UX-C: skip rows not in the deal (without advancing num, so no blank gaps).
                    if (filterInDeal && ___cachedTradeables[i].CountToTransfer == 0)
                        continue;
                    // Skip items the trader won't buy when the hide checkbox is on.
                    if (hideUnwilling && !___cachedTradeables[i].TraderWillTrade)
                        continue;

                    if (num > num2 && num < num3)
                    {
                        Rect rect = new Rect(0, num, leftInsideScrollRect.width, 30f);
                        int countToTransfer = ___cachedTradeables[i].CountToTransfer;
                        //RimWorld.TradeUI.DrawTradeableRow(rect, ___cachedTradeables[i], num4);
                        MyDrawTradableRow(rect, ___cachedTradeables[i], num4, true);
                        if (countToTransfer != ___cachedTradeables[i].CountToTransfer)
                        {
                            __instance.CountToTransferChanged();
                        }
                    }
                    num += 30f;
                    num4++;
                }
                Widgets.EndScrollView();

                // Draw right view (trader pane) - suppressed entirely in gift mode.
                if (!giftMode)
                {
                // UX-B: pinned column headers for the trader pane.
                Rect rightColHeaderRect = new Rect(halfWidth, mainRect.y + rightHeaderRect.height, halfWidth - 16f, COL_HEADER_HEIGHT);
                DrawColumnHeaders(rightColHeaderRect);
                Rect rightScrollRect = new Rect(halfWidth, mainRect.y + rightHeaderRect.height + COL_HEADER_HEIGHT, halfWidth, mainRect.height - rightHeaderRect.height - COL_HEADER_HEIGHT);
                // Change 3: same as the left pane - independent content width drives the horizontal bar.
                float rightContentWidth = Mathf.Max(rightScrollRect.width - 16f, minRowWidth);
                Rect rightInnerRect = new Rect(0, 0, rightContentWidth, rightHeight);
                Widgets.BeginScrollView(rightScrollRect, ref TradeUIParameters.Singleton.scrollPositionRight, rightInnerRect, true);
                num = 6f;
                num2 = TradeUIParameters.Singleton.scrollPositionRight.y - 30f;
                num3 = TradeUIParameters.Singleton.scrollPositionRight.y + rightScrollRect.height;
                num4 = 0;
                for (int i = 0; i < ___cachedTradeables.Count; i++)
                {
                    // Only draw stuff they have
                    if (___cachedTradeables[i].thingsTrader == null || ___cachedTradeables[i].thingsTrader.Count == 0)
                        continue;
                    // UX-C: skip rows not in the deal (without advancing num, so no blank gaps).
                    if (filterInDeal && ___cachedTradeables[i].CountToTransfer == 0)
                        continue;

                    if (num > num2 && num < num3)
                    {
                        Rect rect = new Rect(0, num, rightInnerRect.width, 30f);
                        int countToTransfer = ___cachedTradeables[i].CountToTransfer;
                        MyDrawTradableRow(rect, ___cachedTradeables[i], num4, false);
                        //RimWorld.TradeUI.DrawTradeableRow(rect, ___cachedTradeables[i], num4);
                        if (countToTransfer != ___cachedTradeables[i].CountToTransfer)
                        {
                            __instance.CountToTransferChanged();
                        }

                    }
                    num += 30f;
                    num4++;
                }
                Widgets.EndScrollView();
                } // end if (!giftMode) right pane
                return false; // Skip vanilla behavior
            }

            /*static void BeginScrollViewForceDraw(Rect outRect, ref Vector2 scrollPosition, Rect viewRect, bool showScrollbars = true)
            {
                if (Widgets.mouseOverScrollViewStack.Count > 0)
                {
                    Widgets.mouseOverScrollViewStack.Push(Widgets.mouseOverScrollViewStack.Peek() && outRect.Contains(Event.current.mousePosition));
                }
                else
                {
                    Widgets.mouseOverScrollViewStack.Push(outRect.Contains(Event.current.mousePosition));
                }
                if (showScrollbars)
                {
                    scrollPosition = GUI.BeginScrollView(outRect, scrollPosition, viewRect, false, true);
                    return;
                }
                scrollPosition = GUI.BeginScrollView(outRect, scrollPosition, viewRect, GUIStyle.none, GUIStyle.none);
            }*/

            // TODO: change this to override DrawTradableRow in order to have Trade Helper support
            public static void MyDrawTradableRow(Rect mainRect, Tradeable trad, int index, bool isOurs)
            {
                if (Mathf.Abs(index) % 2 == 1)
                {
                    Widgets.DrawLightHighlight(mainRect);
                }

                // UX-C: emphasise rows that are part of the current deal so they stand out.
                if (trad.CountToTransfer != 0)
                {
                    Widgets.DrawHighlight(mainRect);
                }

                // Hack to prevent formatting for currency
                if (index < 0)
                    mainRect.width -= 16;

                Text.Font = GameFont.Small;
                GUI.BeginGroup(mainRect);
                float xPosition = mainRect.width;

                // Vanilla draws this right-left for some reason
                // our side, should read this
                // LEFT  ---------- RIGHT
                //Icon, info button, name, animal bond/ridability, owned amount, sell price, amount selling, arrows point right

                // their side should read
                // LEFT --------- RIGHT
                // Icon, info button, name, animal bond/ridability, owned amount, buy price, amount buying, arrows point left

                // TDOO: handle somewhere in the trade what happens when I select (sell 10 steel + buy 5 steel)
                // I think this would be an improvement. Split into two tradeables. This would probably affect a large amount of code (more multiplayer patches possibly to sync the new tradables lsit)

                // Change 2: column widths are now shared class constants (COST_WIDTH,
                // TRANSFER_WIDTH, OWNED_AMOUNT_WIDTH) so headers and min-width calc stay in sync.
                bool canBulk = false; // UX-A: only true on genuinely tradeable rows (has the arrows)
                if (!trad.TraderWillTrade)
                {
                    // Since no price will be shown, we will occupy more space
                    xPosition -= (TRANSFER_WIDTH + COST_WIDTH);
                    Rect rect5 = new Rect(xPosition, 0f, TRANSFER_WIDTH + COST_WIDTH, mainRect.height);
                    // But don't actually consume this space since the price will be "drawn" as empty
                    xPosition += COST_WIDTH;

                    // TODO: fix vanilla bug that an item the trader has will show up as "Trader is not willing to buy this". Instead, everything should read "Trader is not willing to trade this" or "Trader is not willing to sell this."
                    RimWorld.TradeUI.DrawWillNotTradeText(rect5, "TraderWillNotTrade".Translate());
                }
                else if (ModsConfig.IdeologyActive && TransferableUIUtility.TradeIsPlayerSellingToSlavery(trad, TradeSession.trader.Faction) && !new HistoryEvent(HistoryEventDefOf.SoldSlave, TradeSession.playerNegotiator.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
                {
                    // Since no price will be shown, will we occupy more space
                    xPosition -= (TRANSFER_WIDTH + COST_WIDTH);
                    Rect rect5 = new Rect(xPosition, 0f, TRANSFER_WIDTH + COST_WIDTH, mainRect.height);
                    // But don't actually consume this space since the price will be "drawn" as empty
                    xPosition += COST_WIDTH;

                    RimWorld.TradeUI.DrawWillNotTradeText(rect5, "NegotiatorWillNotTradeSlaves".Translate(TradeSession.playerNegotiator));
                    if (Mouse.IsOver(rect5))
                    {
                        Widgets.DrawHighlight(rect5);
                        TooltipHandler.TipRegion(rect5, "NegotiatorWillNotTradeSlavesTip".Translate(TradeSession.playerNegotiator, TradeSession.playerNegotiator.Ideo.name));
                    }
                }
                else
                {
                    canBulk = true;
                    xPosition -= TRANSFER_WIDTH;
                    Rect rect5 = new Rect(xPosition, 0f, TRANSFER_WIDTH, mainRect.height);
                    // Drawing left/right arrows and transfer amount
                    bool flash = Time.time - Dialog_Trade.lastCurrencyFlashTime < 1f && trad.IsCurrency;
                    if (isOurs)
                    {
                        TradeUIParameters.Singleton.isDrawingColonyItems = true;
                    }
                    else
                    {
                        TradeUIParameters.Singleton.isDrawingColonyItems = false;
                    }
                    TransferableUIUtility.DoCountAdjustInterface(rect5, trad, index, trad.GetMinimumToTransfer(), trad.GetMaximumToTransfer(), flash, null, false);
                }

                // UX-A: bulk buttons, just left of the transfer arrows. Gated to SP (Dialog_Trade on
                // the stack) - in MP the custom widget is replaced by the vanilla count widget and
                // adding interactive controls here could let a non-negotiating player edit the deal.
                // The AdjustTo path itself already syncs in MP the same way the arrows do.
                if (canBulk && !trad.IsCurrency && Find.WindowStack.IsOpen<Dialog_Trade>())
                {
                    xPosition -= BULK_WIDTH;
                    Rect bulkRect = new Rect(xPosition, 0f, BULK_WIDTH, mainRect.height);
                    DrawBulkButtons(bulkRect, trad, isOurs);
                }

                int ownedAmount = trad.CountHeldBy(isOurs ? Transactor.Colony : Transactor.Trader);
                if ((isOurs && ownedAmount != 0) || (!isOurs && ownedAmount != 0 && trad.IsThing))
                {
                    // draw sell/buy price
                    xPosition -= COST_WIDTH;
                    Rect rect6 = new Rect(xPosition, 0f, COST_WIDTH, mainRect.height);
                    Text.Anchor = TextAnchor.MiddleRight;
                    RimWorld.TradeUI.DrawPrice(rect6, trad, isOurs ? TradeAction.PlayerSells : TradeAction.PlayerBuys);

                    // draw owned amount
                    xPosition -= OWNED_AMOUNT_WIDTH;
                    Rect rect7 = new Rect(xPosition, 0f, OWNED_AMOUNT_WIDTH, mainRect.height);
                    if (Mouse.IsOver(rect7))
                    {
                        Widgets.DrawHighlight(rect7);
                    }
                    Text.Anchor = TextAnchor.MiddleRight;
                    Rect ownedAmountRect = rect7;
                    ownedAmountRect.xMin += 5f;
                    ownedAmountRect.xMax -= 5f;
                    Widgets.Label(ownedAmountRect, ownedAmount.ToStringCached());
                    TooltipHandler.TipRegionByKey(rect7, isOurs ? "ColonyCount" : "TraderCount");
                }
                else
                {
                    xPosition -= (OWNED_AMOUNT_WIDTH + COST_WIDTH);
                }

                // draw animal bond/ridability + ideology captive info.
                // Change 2: measure how much these consume so MinRowWidth() can reserve room for
                // them (animal bond/ridable + captive/ideology rows can eat 2-3 icons).
                float xBeforeExtras = xPosition;
                TransferableUIUtility.DoExtraIcons(trad, mainRect, ref xPosition);

                // draw Ideaology something
                if (ModsConfig.IdeologyActive)
                {
                    TransferableUIUtility.DrawCaptiveTradeInfo(trad, TradeSession.trader, mainRect, ref xPosition);
                }
                float extrasConsumed = xBeforeExtras - xPosition;
                if (extrasConsumed > TradeUIParameters.maxExtraIconWidth)
                {
                    TradeUIParameters.maxExtraIconWidth = extrasConsumed;
                }

                // Defensive clamp: never let the name rect go negative even if a pane is dragged
                // below the minimum before horizontal scroll (Change 3) catches up.
                if (xPosition < 0f)
                {
                    xPosition = 0f;
                }

                // draw icon, ID icon, name
                // Calculate rect for icon + info button + name
                Rect idRect = new Rect(0f, 0f, xPosition, mainRect.height);
                TransferableUIUtility.DrawTransferableInfo(trad, idRect, trad.TraderWillTrade ? Color.white : RimWorld.TradeUI.NoTradeColor);

                // Cleanup
                GenUI.ResetLabelAlign();
                GUI.EndGroup();
            }

            [HarmonyPatch(typeof(RimWorld.TransferableUIUtility), "DoCountAdjustInterfaceInternal")]
            static class Harmony_TransferableUIUtility_DoCountAdjustInterfaceInternal
            {
                static bool Prefix(Rect rect, Transferable trad, int index, int min, int max, bool flash, bool readOnly)
                {
                    //Log.Message("[TradeUI] TransferableUIUtility.DoCountAdjustInterfaceInternal prefix");

                    // Skip this behavior only if we aren't in a trade UI at all (e.g. transport pods).
                    // The trade UI is either the vanilla Dialog_Trade on the window stack (SP) OR the
                    // MP TradingWindow (under RimWorld Multiplayer, Dialog_Trade is drawn *inside* the
                    // TradingWindow and never added to the stack, so IsOpen<Dialog_Trade>() is false).
                    // Previously this guard returned true in MP, so vanilla count-button drawing ran and
                    // overlapped the mod's price column. Detect the MP window too so our custom widget
                    // (which stays inside the reserved TRANSFER_WIDTH rect) draws in MP as well.
                    if (!Find.WindowStack.IsOpen<Dialog_Trade>() && !MpTradeWindowOpen())
                    {
                        //Log.Message("[TradeUI] Not in a trade UI. Drawing vanilla UI buttons");
                        return true;
                    }

                    // Session ownership: in MP only the negotiating faction may edit the deal. Force the
                    // read-only display path (count label, no interactive arrows/textbox) for everyone
                    // else. Mirrors the MP mod's own gating for its vanilla widgets. No-op in SP.
                    readOnly = readOnly || MpTradeReadOnly();

                    rect = rect.Rounded();

                    const float EDGE_MARGIN = 7f;
                    const float ARROW_MARGIN = 10f;
                    // theirs = 135 wide
                    // [60 arrows][ARROW_MARGIN][60 text box][EDGE_MARGIN]
                    // ours
                    // [60 text box][ARROW_MARGIN][60 arrows][EDGE_MARGIN]
                    // Need a width of 125

                    // Theirs 
                    //[30 button][ARROW_MARGIN][60 text box][ARROW_MARGIN][30 button][EDGE_MARGIN]
                    // I am making this ARROW_MARGIN pixels wider than before

                    Rect miniRect = (!trad.Interactive || readOnly) ? rect : new Rect(rect.xMax - ARROW_MARGIN - EDGE_MARGIN - 120f, rect.center.y - 12.5f, 120f + ARROW_MARGIN, 25f).Rounded();

                    // TODO: WHAT IS THIS? Are these pixel coords correct?
                    /*if (flash)
                    {
                        if (TradeUIParameters.Singleton.isDrawingColonyItems)
                            GUI.DrawTexture(new Rect(rect.x, rect.center.y - 12.5f, 90f, 25f).Rounded(), TransferableUIUtility.FlashTex);
                        else
                            GUI.DrawTexture(miniRect, TransferableUIUtility.FlashTex);
                    }*/

                    TransferableOneWay transferableOneWay = trad as TransferableOneWay;
                    bool flag = transferableOneWay != null && transferableOneWay.HasAnyThing && transferableOneWay.AnyThing is Pawn && transferableOneWay.MaxCount == 1;
                    if (!trad.Interactive || readOnly)
                    {
                        if (flag)
                        {
                            bool flag2 = trad.CountToTransfer != 0;
                            Widgets.Checkbox(rect.position, ref flag2, 24f, true, false, null, null);
                        }
                        else
                        {
                            GUI.color = ((trad.CountToTransfer == 0) ? TransferableUIUtility.ZeroCountColor : Color.white);
                            Text.Anchor = TextAnchor.MiddleCenter;
                            // Make transfer amount always positive?
                            Widgets.Label(miniRect, Mathf.Abs(trad.CountToTransfer).ToStringCached());
                            //Widgets.Label(miniRect, trad.CountToTransfer.ToStringCached());
                            //GUI.Label(miniRect, "|||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||");
                        }
                    }
                    else if (flag)
                    {
                        bool flag3 = trad.CountToTransfer != 0;
                        bool flag4 = flag3;
                        Widgets.Checkbox(miniRect.position, ref flag4, 24f, false, true, null, null);
                        if (flag4 != flag3)
                        {
                            if (flag4)
                            {
                                trad.AdjustTo(trad.GetMaximumToTransfer());
                            }
                            else
                            {
                                trad.AdjustTo(trad.GetMinimumToTransfer());
                            }
                        }
                    }
                    else
                    {
                        Rect textRect = miniRect.ContractedBy(2f);
                        if (!TradeUIParameters.Singleton.isDrawingColonyItems)
                        {
                            // Leave room for arrows
                            textRect.xMin += 60f + ARROW_MARGIN;
                        }
                        textRect.width = 55f;

                        // Draw transfer amount
                        // TODO make this always show positive numbers
                        int countToTransfer = trad.CountToTransfer;
                        string editBuffer = trad.EditBuffer;
                        Widgets.TextFieldNumeric<int>(textRect, ref countToTransfer, ref editBuffer, (float)min, (float)max);
                        trad.AdjustTo(countToTransfer);
                        trad.EditBuffer = editBuffer;
                    }

                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                    if (trad.Interactive && !flag && !readOnly)
                    {
                        TransferablePositiveCountDirection positiveCountDirection = trad.PositiveCountDirection;
                        int num = (positiveCountDirection == TransferablePositiveCountDirection.Source) ? 1 : -1;
                        int num2 = GenUI.CurrentAdjustmentMultiplier();

                        // Fix VANILLA bug that items with durability cause "<<" and ">>" to appear even when there is only one of them
                        // e.g. I have "Flak Pants (normal) 98%" and they have "Flak Pants (normal)" the game will incorrectly show ">>" even though it stacks our pants in entirely different rows
                        bool canTradeRight = false;
                        bool canTradeLeft = false;
                        if (TradeUIParameters.Singleton.isDrawingColonyItems)
                        {
                            // Can trade right is we can modify trade amount by -1
                            canTradeRight = trad.CanAdjustBy(-num * num2).Accepted;
                            // Can trade left is we can modify trade amount by +1
                            canTradeLeft = trad.CanAdjustBy(num * num2).Accepted;
                        }
                        else
                        {
                            // Can trade right is we can modify trade amount by +1
                            canTradeRight = trad.CanAdjustBy(num * num2).Accepted;
                            // Can trade left is we can modify trade amount by -1
                            canTradeLeft = trad.CanAdjustBy(-num * num2).Accepted;
                        }

                        if (!TradeUIParameters.Singleton.isDrawingColonyItems)
                        {
                            Rect rightArrowRect = new Rect(miniRect.x, rect.y, 30f, rect.height);
                            Rect leftArrowRect = new Rect(rightArrowRect.x + rightArrowRect.width, rect.y, 30f, rect.height);
                            // Tooltips: trader's items are what you BUY. "<" adds, ">" removes.
                            TooltipHandler.TipRegion(rightArrowRect, "Buy more of this item from the trader.\nRight-click: buy the maximum.");
                            TooltipHandler.TipRegion(leftArrowRect, "Buy fewer of this item.\nRight-click: buy none.");
                            {
                                if (canTradeRight)
                                {
                                    var clickResult = DrawNormalButton(rightArrowRect, "<", true, true, Widgets.NormalOptionColor);
                                    switch (clickResult)
                                    {
                                        case MyDraggableResult.LeftPressed:
                                        case MyDraggableResult.LeftDraggedThenPressed:
                                            trad.AdjustBy(num * num2);
                                            Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                            break;
                                        case MyDraggableResult.RightPressed:
                                        case MyDraggableResult.RightDraggedThenPressed:
                                            //trad.AdjustBy(num * num2);
                                            trad.AdjustTo(trad.GetMaximumToTransfer());
                                            Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                            break;
                                    }
                                    /*if (Widgets.ButtonText(rightArrowRect, "<", true, true, true))
                                    {
                                        trad.AdjustBy(num * num2);
                                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                    }*/
                                }
                                else
                                {
                                    DrawGreyButton(rightArrowRect, "<", true, Color.gray);
                                }

                                if (canTradeLeft)
                                {
                                    var clickResult = DrawNormalButton(leftArrowRect, ">", true, true, Widgets.NormalOptionColor);
                                    switch (clickResult)
                                    {
                                        case MyDraggableResult.LeftPressed:
                                        case MyDraggableResult.LeftDraggedThenPressed:
                                            trad.AdjustBy(-num * num2);
                                            Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                            break;
                                        case MyDraggableResult.RightPressed:
                                        case MyDraggableResult.RightDraggedThenPressed:
                                            //trad.AdjustBy(num * num2);
                                            trad.AdjustTo(trad.GetMinimumToTransfer());
                                            Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                            break;
                                    }
                                    /*if (Widgets.ButtonText(leftArrowRect, ">", true, true, true))
                                    {
                                        trad.AdjustBy(-num * num2);
                                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_Low, null);
                                    }*/
                                }
                                else
                                {
                                    DrawGreyButton(leftArrowRect, ">", true, Color.gray);
                                }
                            }
                        }

                        if (TradeUIParameters.Singleton.isDrawingColonyItems)// && trad.CanAdjustBy(-num * num2).Accepted)
                        {
                            Rect leftArrowRect = new Rect(miniRect.x + 55f + EDGE_MARGIN + 10, rect.y, 30f, rect.height);
                            Rect rightArrowRect = new Rect(leftArrowRect.xMax, rect.y, 30f, rect.height);
                            // Tooltips: your colony's items are what you SELL. "<" adds, ">" removes.
                            TooltipHandler.TipRegion(leftArrowRect, "Sell more of this item to the trader.\nRight-click: sell all.");
                            TooltipHandler.TipRegion(rightArrowRect, "Sell fewer of this item.\nRight-click: sell none.");

                            if (canTradeLeft)
                            {
                                var clickResult = DrawNormalButton(leftArrowRect, "<", true, true, Widgets.NormalOptionColor);
                                switch (clickResult)
                                {
                                    case MyDraggableResult.LeftPressed:
                                    case MyDraggableResult.LeftDraggedThenPressed:
                                        trad.AdjustBy(num * num2);
                                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                        break;
                                    case MyDraggableResult.RightPressed:
                                    case MyDraggableResult.RightDraggedThenPressed:
                                        if (positiveCountDirection == TransferablePositiveCountDirection.Destination)
                                            trad.AdjustTo(trad.GetMinimumToTransfer());
                                        else
                                            trad.AdjustTo(trad.GetMaximumToTransfer());
                                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                        break;
                                }
                                /*if (Widgets.ButtonText(leftArrowRect, "<", true, true, true))
                                {
                                    trad.AdjustBy(num * num2);
                                    Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                }*/
                            }
                            else
                            {
                                DrawGreyButton(leftArrowRect, "<", true, Color.gray);
                            }

                            if (canTradeRight)
                            {
                                var clickResult = DrawNormalButton(rightArrowRect, ">", true, true, Widgets.NormalOptionColor);
                                switch (clickResult)
                                {
                                    case MyDraggableResult.LeftPressed:
                                    case MyDraggableResult.LeftDraggedThenPressed:
                                        trad.AdjustBy(-num * num2);
                                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                        break;
                                    case MyDraggableResult.RightPressed:
                                    case MyDraggableResult.RightDraggedThenPressed:
                                        //trad.AdjustBy(num * num2);
                                        if (positiveCountDirection == TransferablePositiveCountDirection.Destination)
                                            trad.AdjustTo(trad.GetMaximumToTransfer());
                                        else
                                            trad.AdjustTo(trad.GetMinimumToTransfer());
                                        Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High, null);
                                        break;
                                }
                                /*if (Widgets.ButtonText(rightArrowRect, ">", true, true, true))
                                {
                                    trad.AdjustBy(-num * num2);
                                    Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_Low, null);
                                }*/
                            }
                            else
                            {
                                DrawGreyButton(rightArrowRect, ">", true, Color.gray);
                            }
                        }
                    }

                    // Draw arrow texture
                    if (trad.CountToTransfer != 0)
                    {
                        float textBoxCenter = 0f;
                        if (!trad.Interactive || readOnly)
                        {
                            // TODO: shift this a little left/right to accomodate the larger text
                            textBoxCenter = miniRect.center.x;
                            if (trad.CountToTransfer > 0)
                                textBoxCenter -= 15;
                            else
                                textBoxCenter += 15;
                        }
                        else if (TradeUIParameters.Singleton.isDrawingColonyItems)
                        {
                            textBoxCenter = miniRect.xMin + 30f;
                        }
                        else
                        {
                            textBoxCenter = miniRect.xMax - 30f - 2.5f;
                        }
                        Rect position = new Rect(textBoxCenter - (float)(TransferableUIUtility.TradeArrow.width / 2),
                            miniRect.y + miniRect.height / 2f - (float)(TransferableUIUtility.TradeArrow.height / 2),
                            (float)TransferableUIUtility.TradeArrow.width,
                            (float)TransferableUIUtility.TradeArrow.height);
                        TransferablePositiveCountDirection positiveCountDirection2 = trad.PositiveCountDirection;
                        if ((positiveCountDirection2 == TransferablePositiveCountDirection.Source && trad.CountToTransfer > 0) || (positiveCountDirection2 == TransferablePositiveCountDirection.Destination && trad.CountToTransfer < 0))
                        {
                            position.x += position.width;
                            position.width *= -1f;
                        }
                        GUI.DrawTexture(position, TransferableUIUtility.TradeArrow);
                    }
                    //GUI.Label(miniRect, "|||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||");

                    // Skip vanilla behavior
                    return false;
                }
            }

            enum MyDraggableResult
            {
                Idle = 0,
                LeftPressed = 1,
                LeftDragged = 2,
                LeftDraggedThenPressed = 3,
                RightPressed = 4,
                RightDragged = 5,
                RightDraggedThenPressed = 6,
            }

            static MyDraggableResult DrawNormalButton(Rect rect, string label, bool drawBackground, bool doMouseoverSound, Color textColor, bool active = true, bool draggable = true)
            {
                var retVal = MyDraggableResult.Idle;
                TextAnchor anchor = Text.Anchor;
                Color color = GUI.color;
                if (drawBackground)
                {
                    Texture2D atlas = Widgets.ButtonBGAtlas;
                    if (Mouse.IsOver(rect))
                    {
                        atlas = Widgets.ButtonBGAtlasMouseover;
                        if (Input.GetMouseButton(0))
                        {
                            atlas = Widgets.ButtonBGAtlasClick;
                        }
                        else if (Input.GetMouseButton(1))
                        {
                            atlas = Widgets.ButtonBGAtlasClick;
                        }

                        /*if (Input.GetMouseButtonUp(0))
                        {
                            retVal = MyDraggableResult.LeftPressed;
                        }
                        else if (Input.GetMouseButtonUp(1))
                        {
                            retVal = MyDraggableResult.RightPressed;
                        }*/
                    }
                    Widgets.DrawAtlas(rect, atlas);
                }

                if (doMouseoverSound)
                {
                    Verse.Sound.MouseoverSounds.DoRegion(rect);
                }
                if (!drawBackground)
                {
                    GUI.color = textColor;
                    if (Mouse.IsOver(rect))
                    {
                        GUI.color = Widgets.MouseoverOptionColor;
                    }
                }
                if (drawBackground)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                }
                else
                {
                    Text.Anchor = TextAnchor.MiddleLeft;
                }
                bool wordWrap = Text.WordWrap;
                if (rect.height < Text.LineHeight * 2f)
                {
                    Text.WordWrap = false;
                }
                Widgets.Label(rect, label);
                Text.Anchor = anchor;
                GUI.color = color;
                Text.WordWrap = wordWrap;

                if (active && draggable)
                {
                    // Check for right mouse click first
                    if (Mouse.IsOver(rect) && Input.GetMouseButtonUp(1))
                    {
                        retVal = MyDraggableResult.RightPressed;
                    }
                    else
                    {
                        retVal = ButtonInvisibleDraggable(rect, false);
                        //if (retVal != MyDraggableResult.Idle)
                        //Log.Message("returning value " + retVal.ToString());
                    }

                    //return ButtonInvisibleDraggable(rect, false);
                }
                /*if (!active)
                {
                    return Widgets.DraggableResult.Idle;
                }
                if (!Widgets.ButtonInvisible(rect, false))
                {
                    return Widgets.DraggableResult.Idle;
                }*/

                return retVal;
            }

            static MyDraggableResult ButtonInvisibleDraggable(Rect rect, bool doMouseoverSound = false)
            {
                if (doMouseoverSound)
                {
                    Verse.Sound.MouseoverSounds.DoRegion(rect);
                }
                int controlID = GUIUtility.GetControlID(FocusType.Passive, rect);
                if (Mouse.IsOver(rect))
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        //Log.Message("resetting mouse start pos");
                        Widgets.buttonInvisibleDraggable_activeControl = controlID;
                        Widgets.buttonInvisibleDraggable_mouseStart = Input.mousePosition;
                        Widgets.buttonInvisibleDraggable_dragged = false;
                    }
                    /*else if (Input.GetMouseButtonDown(1))
                    {
                        Widgets.buttonInvisibleDraggable_activeControl = controlID;
                        Widgets.buttonInvisibleDraggable_mouseStart = Input.mousePosition;
                        Widgets.buttonInvisibleDraggable_dragged = false;
                        TradeUIParameters.Singleton.isRightDown = false;
                    }*/
                }

                if (Widgets.buttonInvisibleDraggable_activeControl == controlID)
                {
                    if (Input.GetMouseButtonUp(0))
                    {
                        // On the frame that the button is released
                        Widgets.buttonInvisibleDraggable_activeControl = 0;
                        if (!Mouse.IsOver(rect))
                        {
                            return MyDraggableResult.Idle;
                        }
                        if (!Widgets.buttonInvisibleDraggable_dragged)
                        {
                            // We are over the rect + no drag + released
                            return MyDraggableResult.LeftPressed;
                        }
                        // We are over the rect + dragged + released
                        return MyDraggableResult.LeftDraggedThenPressed;
                    }
                    else
                    {
                        if (!Input.GetMouseButton(0))
                        {
                            // Button not released + not down
                            Widgets.buttonInvisibleDraggable_activeControl = 0;
                            return MyDraggableResult.Idle;
                        }
                        if (!Widgets.buttonInvisibleDraggable_dragged && (Widgets.buttonInvisibleDraggable_mouseStart - Input.mousePosition).sqrMagnitude > Widgets.DragStartDistanceSquared)
                        {
                            // Button not released + button is down + hasn't started dragging + drag distance is met
                            Widgets.buttonInvisibleDraggable_dragged = true;
                            return MyDraggableResult.LeftDragged;
                        }
                    }
                }
                return MyDraggableResult.Idle;
            }
        }

        // Feature 2 (gift fix): vanilla Dialog_Trade.CacheTradeables drops colony items the trader
        // "won't trade" for trader kinds with hideThingsNotWillingToTrade - which is exactly the
        // low-value junk you would gift for goodwill. There is no gift-mode branch in vanilla, so in
        // gift mode those rows silently vanish. Rebuild the cache in gift mode to include EVERY
        // non-currency colony-held tradeable (matching the active search), using vanilla's own sorter
        // chain so ordering is preserved. Trader-won't-trade items sort last exactly as vanilla intends.
        [HarmonyPatch(typeof(RimWorld.Dialog_Trade), "CacheTradeables")]
        static class Harmony_DialogTrade_CacheTradeables
        {
            static void Postfix(ref List<Tradeable> ___cachedTradeables, TransferableSorterDef ___sorter1, TransferableSorterDef ___sorter2, QuickSearchWidget ___quickSearchWidget)
            {
                if (!TradeSession.giftMode)
                {
                    return;
                }
                // One-sided give: only colony-held items are giftable. Mirror vanilla's ordering chain.
                ___cachedTradeables = TradeSession.deal.AllTradeables
                    .Where(tr => !tr.IsCurrency
                        && tr.CountHeldBy(Transactor.Colony) > 0
                        && ___quickSearchWidget.filter.Matches(tr.Label))
                    .OrderByDescending(tr => (!tr.TraderWillTrade) ? -1 : 0)
                    .ThenBy(tr => tr, ___sorter1.Comparer)
                    .ThenBy(tr => tr, ___sorter2.Comparer)
                    .ThenBy(tr => TransferableUIUtility.DefaultListOrderPriority(tr))
                    .ThenBy(tr => tr.ThingDef.label)
                    .ThenBy(tr => tr.AnyThing.TryGetQuality(out QualityCategory qc) ? (int)qc : -1)
                    .ThenBy(tr => tr.AnyThing.HitPoints)
                    .ToList();
                ___quickSearchWidget.noResultsMatched = !___cachedTradeables.Any();
            }
        }

        // I think this is safe to keep as-is
        [HarmonyPatch(typeof(RimWorld.Dialog_Trade), "PostOpen")]
        static class Harmony_DialogTrade_PostOpen
        {
            static void Prefix(Dialog_Trade __instance)
            {
                // Change 1: allow the user to drag-resize the trade window. Window.resizeable
                // draws/handles the resize grip via WindowResizer without needing draggable.
                __instance.resizeable = true;
                TradeUIParameters.Singleton.Reset();
            }
        }

        [HarmonyPatch(typeof(RimWorld.Dialog_Trade), "InitialSize", MethodType.Getter)]
        static class Harmony_DialogTrade_InitialSize
        {
            static void Postfix(ref Vector2 __result)
            {
                // Change 1: restore the last client-local size if we have one, otherwise apply the
                // legacy +360 default. One branch or the other - never +360 on top of a stored size.
                Vector2 size;
                if (TradeUIParameters.windowSize != Vector2.zero)
                {
                    size = TradeUIParameters.windowSize;
                }
                else
                {
                    // Make screen wider (without being bigger than the user's screen)
                    size = new Vector2(__result.x + 360f, __result.y);
                }
                // Clamp to a sane minimum and to the current screen (resolution may have changed).
                size.x = Mathf.Clamp(size.x, 550f, UI.screenWidth);
                size.y = Mathf.Clamp(size.y, 500f, UI.screenHeight);
                __result = size;
            }
        }

        // --- Multiplayer helpers (Change 4) ---
        // All MP access goes through reflection so the build never hard-references the Multiplayer
        // assembly. Window size / scroll position stay client-local (display-only UI state) - they
        // are NOT routed through MP action sync and cannot desync.
        static bool s_mpTypeChecked;
        static System.Type s_mpTradingWindowType;

        static System.Type MpTradingWindowType()
        {
            if (!s_mpTypeChecked)
            {
                s_mpTypeChecked = true;
                bool mpLoaded = LoadedModManager.RunningModsListForReading.Any(
                    m => m.PackageId == "rwmt.Multiplayer".ToLowerInvariant());
                if (mpLoaded)
                {
                    s_mpTradingWindowType = System.Type.GetType("Multiplayer.Client.TradingWindow, Multiplayer");
                }
            }
            return s_mpTradingWindowType;
        }

        // Returns the real MP TradingWindow's size, or Vector2.zero when not in MP / not open.
        static Vector2 GetMultiplayerWindowSize()
        {
            System.Type type = MpTradingWindowType();
            if (type == null)
            {
                return Vector2.zero;
            }
            foreach (Window w in Find.WindowStack.Windows)
            {
                if (type.IsInstanceOfType(w))
                {
                    return w.windowRect.size;
                }
            }
            return Vector2.zero;
        }

        // True when the MP TradingWindow is currently on the window stack. Under RimWorld Multiplayer
        // the vanilla Dialog_Trade is NOT added to the stack - MP hosts an inner Dialog_Trade inside
        // TradingWindow and calls DoWindowContents directly - so Find.WindowStack.IsOpen<Dialog_Trade>()
        // is false during MP trading. We use this to detect the trade UI so our custom count widget runs.
        static bool MpTradeWindowOpen()
        {
            System.Type type = MpTradingWindowType();
            if (type == null)
            {
                return false;
            }
            foreach (Window w in Find.WindowStack.Windows)
            {
                if (type.IsInstanceOfType(w))
                {
                    return true;
                }
            }
            return false;
        }

        // --- MP session-ownership reflection (Change 5) ---
        // In MP only the negotiating faction may edit the deal. The MP mod itself gates its own
        // (vanilla) count widgets this way (see Multiplayer's TradingUI.cs
        // DisableTradeCountButtonsForOtherFactions:
        //   MpTradeSession.current.NegotiatorFaction == Multiplayer.RealPlayerFaction).
        // Our custom widget draws its own arrows/textbox (not vanilla Widgets.ButtonText), so MP's
        // guards don't cover them - we replicate the check by reflection and force the read-only path
        // for non-negotiating players. All lookups are cached and fully null/exception guarded, so a
        // missing MP assembly or member simply disables the gate (behaviour identical to SP).
        static bool s_mpMembersChecked;
        static FieldInfo s_mpDrawingTradeField;       // TradingWindow.drawingTrade (static)
        static FieldInfo s_mpCurrentSessionField;     // MpTradeSession.current (static)
        static PropertyInfo s_mpNegotiatorFactionProp; // MpTradeSession.NegotiatorFaction (instance)
        static PropertyInfo s_mpRealPlayerFactionProp; // Multiplayer.RealPlayerFaction (static)

        static void EnsureMpTradeMembers()
        {
            if (s_mpMembersChecked)
            {
                return;
            }
            s_mpMembersChecked = true;
            try
            {
                System.Type tradingWindowType = MpTradingWindowType();
                if (tradingWindowType == null)
                {
                    return;
                }
                System.Type tradeSessionType = System.Type.GetType("Multiplayer.Client.MpTradeSession, Multiplayer");
                System.Type multiplayerType = System.Type.GetType("Multiplayer.Client.Multiplayer, Multiplayer");
                if (tradeSessionType == null || multiplayerType == null)
                {
                    return;
                }

                s_mpDrawingTradeField = tradingWindowType.GetField("drawingTrade", BindingFlags.Public | BindingFlags.Static);
                s_mpCurrentSessionField = tradeSessionType.GetField("current", BindingFlags.Public | BindingFlags.Static);
                s_mpNegotiatorFactionProp = tradeSessionType.GetProperty("NegotiatorFaction", BindingFlags.Public | BindingFlags.Instance);
                s_mpRealPlayerFactionProp = multiplayerType.GetProperty("RealPlayerFaction", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception e)
            {
                Log.Warning("[TradeUI] Failed to resolve Multiplayer trade-session members; MP edit-gating disabled. " + e.Message);
                s_mpDrawingTradeField = null;
                s_mpCurrentSessionField = null;
                s_mpNegotiatorFactionProp = null;
                s_mpRealPlayerFactionProp = null;
            }
        }

        // Returns true when we are currently drawing the MP trade window for a player whose faction is
        // NOT the negotiating faction. Such players must see the read-only display (label only, no
        // interactive arrows/textbox). Returns false in SP, when MP isn't loaded, when any member is
        // missing, or when the local player IS the negotiator.
        static bool MpTradeReadOnly()
        {
            EnsureMpTradeMembers();
            if (s_mpDrawingTradeField == null || s_mpCurrentSessionField == null
                || s_mpNegotiatorFactionProp == null || s_mpRealPlayerFactionProp == null)
            {
                return false;
            }
            try
            {
                // Only gate while the MP trade window is actually being drawn (drawingTrade is set for
                // the duration of TradingWindow.DoWindowContents -> Dialog_Trade.DoWindowContents).
                if (s_mpDrawingTradeField.GetValue(null) == null)
                {
                    return false;
                }
                object currentSession = s_mpCurrentSessionField.GetValue(null);
                if (currentSession == null)
                {
                    return false;
                }
                Faction negotiatorFaction = s_mpNegotiatorFactionProp.GetValue(currentSession, null) as Faction;
                Faction realPlayerFaction = s_mpRealPlayerFactionProp.GetValue(null, null) as Faction;
                if (negotiatorFaction == null || realPlayerFaction == null)
                {
                    return false;
                }
                return negotiatorFaction != realPlayerFaction;
            }
            catch (Exception)
            {
                // Never let a reflection hiccup break rendering; fall back to interactive (SP-like).
                return false;
            }
        }

        // Change 4: make the MP trade window resizable too. Patch the TradingWindow *constructor* -
        // TradingWindow does not override PostOpen, so patching PostOpen would resolve to base
        // Verse.Window.PostOpen and fire for every window in the game.
        [HarmonyPatch]
        static class PatchTradingWindowResizeable
        {
            static void Postfix(Window __instance)
            {
                __instance.resizeable = true;
            }

            public static bool Prepare()
            {
                return LoadedModManager.RunningModsListForReading.Any(
                    m => m.PackageId == "rwmt.Multiplayer".ToLowerInvariant());
            }

            public static MethodBase TargetMethod()
            {
                System.Type multiplayerTradeUIType = System.Type.GetType("Multiplayer.Client.TradingWindow, Multiplayer");
                MethodBase ctor = AccessTools.GetDeclaredConstructors(multiplayerTradeUIType).FirstOrDefault();
                if (ctor == null)
                    Log.Error("[TradeUI] failed to find multiplayer TradingWindow constructor");
                return ctor;
            }
        }

        // TODO: might need to make this a transpiler to support TradeHelper
        [HarmonyPatch]
        static class PatchTradingWindowWidth
        {
            static void Postfix(ref Vector2 __result)
            {
                // Change 4: mirror the SP InitialSize logic so the MP TradingWindow also restores the
                // client-local persisted size (else the legacy +360 default), clamped to screen.
                Vector2 size;
                if (TradeUIParameters.windowSize != Vector2.zero)
                {
                    size = TradeUIParameters.windowSize;
                }
                else
                {
                    size = new Vector2(__result.x + 360f, __result.y);
                }
                size.x = Mathf.Clamp(size.x, 550f, UI.screenWidth);
                size.y = Mathf.Clamp(size.y, 500f, UI.screenHeight);
                __result = size;
            }

            public static bool Prepare()
            {
                //return ModLister.HasActiveModWithName("Multiplayer");
                return LoadedModManager.RunningModsListForReading.Any(
                    m => m.PackageId == "rwmt.Multiplayer".ToLowerInvariant()
                    );
            }

            public static MethodInfo TargetMethod()
            {
                System.Type multiplayerTradeUIType = System.Type.GetType("Multiplayer.Client.TradingWindow, Multiplayer");
                var methodInfo = AccessTools.Property(multiplayerTradeUIType, "InitialSize").GetGetMethod();
                if (methodInfo == null)
                    Log.Error("[TradeUI] failed to find multiplayer method info");
                return methodInfo;
            }
        }
    }
}