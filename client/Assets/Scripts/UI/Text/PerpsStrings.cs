using CTLife.Core;
using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    public static class PerpsStrings
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            ["title"] = "Perps", ["heading"] = "PERP PIT", ["tagline"] = "Conviction has a close button.",
            ["cash"] = "GAME CASH", ["fictional"] = "Fictional markets · Game Cash only", ["loading"] = "Checking positions and markets…",
            ["refresh"] = "Refresh", ["busy"] = "Refreshing…", ["snapshot"] = "Snapshot received {0}s ago",
            ["unknownAge"] = "No current price snapshot", ["stale"] = "This snapshot needs a refresh before an order can be reviewed.",
            ["network"] = "Couldn't reach the market. Any details below are your last received snapshot.",
            ["invalid"] = "Some market details are missing. Refresh before trading.",
            ["unavailable"] = "Perps aren't available yet.", ["unavailableDetail"] = "Return to the city or check again later.",
            ["failed"] = "The request wasn't confirmed. Refresh to check its status.",
            ["gate"] = "Trading is unavailable in this build. You can inspect positions and prepare an order; submitting it is disabled.",
            ["degraded"] = "The market is temporarily unavailable. Your positions will refresh when the service returns.",
            ["session"] = "Your session ended. Sign in again to view your positions.",
            ["market"] = "MARKET", ["choose"] = "Choose market", ["markets"] = "Choose a market", ["more"] = "More markets",
            ["emptyMarket"] = "No markets available.", ["emptyMarketDetail"] = "There is nothing to trade here yet. Check again later.",
            ["change"] = "{0}% · 5m", ["stage.new"] = "New", ["stage.final"] = "Final stretch", ["stage.graduated"] = "Graduated", ["stage.dead"] = "Closed market",
            ["price"] = "MARK PRICE", ["priceUnits"] = "Game Cash per unit", ["history"] = "Price history", ["noHistory"] = "No price history available yet.",
            ["onePoint"] = "One recorded price. More history is needed for a chart.", ["historyRange"] = "{0} — {1} UTC",
            ["order"] = "YOUR THESIS", ["long"] = "Long", ["short"] = "Short", ["longHint"] = "Price up is your thesis.", ["shortHint"] = "Price down is your thesis.",
            ["margin"] = "Margin · Game Cash", ["marginHint"] = "Use a decimal point, up to 2 places.", ["marginError"] = "Enter a positive amount within your available Game Cash.",
            ["percent"] = "{0}%", ["leverage"] = "Leverage · {0}×", ["cap"] = "Your skill + tribe limit: {0}×", ["capUnknown"] = "Your leverage limit couldn't be verified. Order review is unavailable.",
            ["preview"] = "Review order", ["review"] = "Review your position", ["terms"] = "The entry price is set when your order executes and may differ from this snapshot. Margin comes from your Game Cash.",
            ["summary"] = "{0} · {1} · {2}×", ["marginSummary"] = "Margin: {0} Game Cash", ["notional"] = "Exposure: {0} Game Cash", ["notionalNote"] = "Margin × leverage; this is exposure, not a payout.",
            ["confirm"] = "Open position", ["back"] = "Edit order", ["close"] = "Close position", ["closeReview"] = "Close this position?",
            ["closeTerms"] = "Your position closes at the execution price. The final result appears after confirmation.", ["closeConfirm"] = "Confirm close", ["cancel"] = "Cancel",
            ["openTab"] = "Open · {0}", ["historyTab"] = "Receipts · {0}", ["positions"] = "YOUR POSITIONS", ["emptyPositions"] = "No open positions returned.",
            ["emptyPositionsDetail"] = "Pick a market. Form a thesis. Keep some conviction in reserve.", ["emptyReceipts"] = "No closed or liquidated positions returned.",
            ["open"] = "OPEN", ["closed"] = "CLOSED", ["liquidated"] = "LIQUIDATED", ["entry"] = "ENTRY", ["liquidation"] = "LIQUIDATION", ["marginLabel"] = "MARGIN",
            ["realized"] = "Position result: {0} Game Cash", ["realizedMissing"] = "The result for this position is not available yet.",
            ["receiptNote"] = "Your balance at the top shows your available Game Cash.",
            ["closeTime"] = "Closed {0} UTC", ["positionId"] = "Position {0}", ["coinId"] = "Market {0}",
            ["unknownOpen"] = "Your open request wasn't confirmed. It may have reached the server. No repeat order has been sent. Refresh your positions; an identical-looking position cannot prove this request's outcome.",
            ["unknownClose"] = "Your close request wasn't confirmed. No repeat close has been sent. Refresh positions to check this exact position's status.",
            ["pendingKey"] = "Request {0}", ["pendingHold"] = "Further submissions are paused while this request is unresolved.",
            ["corruptJournal"] = "A retained request record couldn't be read. Trading is paused; refresh positions and contact support before submitting another order.",
            ["confirmed"] = "Position confirmed", ["dismiss"] = "Done", ["sending"] = "Waiting for confirmation…",
            ["rejected"] = "Order declined. Refresh your balance and limit before trying again.",
            ["balanceStale"] = "Receipt confirmed. Your Game Cash balance still needs a refresh.", ["unsupported"] = "Order submission isn't available in this build.",
            ["reviewOnly"] = "DEVELOPMENT REVIEW · SIMULATED RESPONSES · NO ORDERS", ["reviewAction"] = "Review only — no order can be submitted.",
            ["limitNote"] = "Showing recent positions. Older receipts may not be included.", ["scroll"] = "Scroll for order controls and positions.",
        };
        public static string T(string key) => Copy.TryGetValue(key, out var value) ? NameTable.Apply(value) : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
        public static string Cash(decimal cents) => (cents / 100m).ToString("N2", CultureInfo.CurrentCulture);
        public static string Price(long micros) => (micros / 1000000m).ToString("0.######", CultureInfo.CurrentCulture);
    }
}
