using UnityEngine;

namespace TradeUI
{
    public class TradeUIParameters
    {
        public static TradeUIParameters Singleton
        {
            get
            {
                if (m_instance == null)
                    m_instance = new TradeUIParameters();
                return m_instance;
            }
        }

        private static TradeUIParameters m_instance;

        // Client-local persisted window size (Change 1). Pure UI state, never MP-synced.
        public static Vector2 windowSize = Vector2.zero;

        // Change 2: max width consumed by DoExtraIcons/DrawCaptiveTradeInfo across drawn rows,
        // measured live and fed into the minimum row width so extra icons never crowd the name.
        public static float maxExtraIconWidth = 0f;

        public void Reset()
        {
            // UX-E: scroll positions intentionally persist across trade opens. BeginScrollView clamps
            // a stale position to the new content each frame, so keeping it is safe (and remembered).
            isRightDown = false;
        }

        public bool isDrawingColonyItems;
        public Vector2 scrollPositionLeft;
        public Vector2 scrollPositionRight;
        public bool isRightDown = false;

        // UX-C: when true, only rows already in the deal (CountToTransfer != 0) are shown.
        public bool filterInDealOnly = false;

        // When true, hide rows the trader is not willing to trade (i.e. won't buy from the colony).
        public bool hideUnwillingToBuy = false;
    }
}
