using System.Collections.Generic;
using System.Globalization;

namespace CTLife.Ventures
{
    /// <summary>
    /// All Ventures copy (English). CT-native and parody only: no real brands, people or projects. Voice notes come from the
    /// spec's archetype table (VC: "We're excited to back…", fleece vest; Doxxed Founder: "We're hiring"). Pure so tests can
    /// render every message. Server-provided text (names, aliases) is never put in a format string key.
    /// </summary>
    public static class VStrings
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // ---- apps
            ["app.ads"] = "Ad Desk", ["app.builder"] = "Builder Hills", ["app.vc"] = "VC Row",

            // ---- common
            ["common.retry"] = "Try again", ["common.cancel"] = "Cancel", ["common.close"] = "Close", ["common.back"] = "Back",
            ["common.loading"] = "Loading…", ["common.refresh"] = "Refresh", ["common.more"] = "Load more", ["common.done"] = "Done",
            ["common.none"] = "Nothing here yet.", ["common.skipped"] = "{0} entries could not be read and are hidden.",
            ["common.notAvailable"] = "Not available yet. This server does not have this feature switched on, so nothing is shown instead of made-up data.",
            ["common.you"] = "you", ["common.unknownStatus"] = "Unknown status",
            ["time.until"] = "until {0}", ["time.expiresIn"] = "expires in {0}", ["time.expired"] = "expired",

            // ---- errors
            ["err.generic"] = "Something went wrong.",
            ["err.network"] = "No connection. Check your signal and try again.",
            ["err.server"] = "The city server hiccuped. Try again in a moment.",
            ["err.invalidResponse"] = "The server sent something unexpected.",
            ["err.unknownOutcome"] = "We couldn't confirm that. Try again; the same request is replayed, so nothing happens twice.",
            ["err.unavailable"] = "Not available yet on this server.",
            ["err.insufficientCash"] = "Not enough Cash (short {0}).",
            ["err.validation"] = "The server didn't accept that. Check the name and numbers.",
            ["err.notFound"] = "Not found.", ["err.forbidden"] = "You can't do that.", ["err.blocked"] = "You can't reach this player.",
            ["err.rateLimited"] = "Slow down. Try again in {0}.", ["err.busy"] = "The city is busy. Retrying shortly.",
            ["err.session"] = "Your session expired. Sign in again.", ["err.outdated"] = "Update CT Life to continue.",
            ["err.paymentExpired"] = "That payment expired. Start again.", ["err.paymentUnderpaid"] = "The payment was short. Contact support with the signature.",
            ["err.walletDeclined"] = "You cancelled in the wallet.", ["err.walletFailed"] = "The wallet reported an error. Checking whether it went through…",
            ["err.conflict"] = "That changed while you were deciding. Refresh and try again.",
            ["err.slotTaken"] = "That window was just booked by someone else. Pick another start.",
            ["err.projectLimit"] = "You already run the most projects allowed ({0}).",
            ["err.quoteChanged"] = "The launch cost changed to {0}. Review it and confirm again.",
            ["err.soldOut"] = "Sold out.",

            // ---- payments
            ["pay.disabled"] = "Payments are switched off on this server right now.",
            ["pay.noCluster"] = "This environment has no Solana network, so it cannot take payments.",
            ["pay.devnetOnly"] = "Payments are devnet-only in this build. No real money can move here.",
            ["pay.noWallet"] = "Connect a wallet to pay. Your wallet is not available in this build.",
            ["pay.preparing"] = "Preparing your payment…",
            ["pay.review"] = "Pay {0} for this booking. The server set the amount; check it in your wallet before you approve.",
            ["pay.expiry"] = "Payment window: {0}",
            ["pay.sign"] = "Pay with wallet", ["pay.signing"] = "Waiting for your wallet…", ["pay.confirming"] = "Confirming on Solana…",
            ["pay.confirmed"] = "Paid. Your ad is in review.",
            ["pay.pending"] = "The payment hasn't confirmed yet. If you approved it, it will show up here.",
            ["pay.check"] = "Check again", ["pay.expired"] = "That payment window closed. Start a new booking.",
            ["pay.failed"] = "The payment failed. Nothing was charged to this booking.",
            ["pay.noIntent"] = "The server reserved the slot but did not send a payment, so it can't be paid from here yet. Cancel it below; it is not charged.",
            ["pay.noTx"] = "The server sent no transaction to sign.",
            ["pay.uncertain"] = "Your wallet reported an error after signing. We are checking whether it went through; do not pay again.",
            ["pay.statusUnavailable"] = "Payment status is not available on this server. The confirmation will arrive in the city feed.",
            ["pay.otherInFlight"] = "Finish or clear your other payment first.",
            ["pay.amount"] = "Amount", ["pay.to"] = "To",

            // ---- ads: desk
            ["ads.title"] = "Ad Desk",
            ["ads.hint"] = "Real projects buy billboards, Pixel Wall tiles, phone apps and Conference Week booths here. Every ad says Sponsored and is reviewed by a person before it shows. Paid in USDC on devnet.",
            ["ads.tab.slots"] = "Slots", ["ads.tab.mine"] = "My bookings",
            ["ads.rules"] = "Rules: no rewards for posting, liking or following on X, no real token airdrops through the game, no impersonation. A rejected ad is refunded.",
            ["ads.slot.nextFree"] = "Free from {0}", ["ads.slot.free"] = "Free now", ["ads.slot.noTerms"] = "No terms published.",
            ["ads.slot.book"] = "Book", ["ads.slot.term"] = "{0} days · {1}", ["ads.slot.termNoDays"] = "{0} · {1}", ["ads.slot.termNoPrice"] = "{0}",
            ["ads.filter.all"] = "All",
            ["ads.kind.billboard"] = "Billboard", ["ads.kind.pixel_wall"] = "Pixel Wall tile", ["ads.kind.pixel_tile"] = "Pixel Wall tile",
            ["ads.kind.phone_app"] = "Sponsored phone app", ["ads.kind.booth"] = "Conference Week booth", ["ads.kind.branded_venue"] = "Branded venue",
            ["ads.slots.loadFailed"] = "Couldn't load the slot list.",
            ["ads.slots.empty"] = "No ad slots on sale right now.",

            // ---- ads: booking sheet
            ["book.title"] = "Book {0}",
            ["book.availability.loading"] = "Checking what's booked…",
            ["book.availability.unavailable"] = "Availability isn't served yet, so booked windows and the creative size are unknown. The server checks the window when you book.",
            ["book.creativeSize"] = "Creative size: {0} x {1} px",
            ["book.booked"] = "Already booked:", ["book.booked.row"] = "{0} to {1}",
            ["book.terms"] = "Term", ["book.start"] = "Start date (UTC, YYYY-MM-DD). Leave empty for the next free window.",
            ["book.startNotForTerm"] = "This term has no fixed length, so it starts when the server schedules it.",
            ["book.link"] = "Link (https only, optional). Stored, never opened by the server.",
            ["book.creative"] = "Creative (PNG or JPEG, at most {0})",
            ["book.creative.path"] = "Path to your image file",
            ["book.creative.load"] = "Load image", ["book.creative.pick"] = "Choose from gallery", ["book.creative.clear"] = "Remove",
            ["book.creative.ok"] = "{0}: {1} x {2} px, {3}",
            ["book.creative.outsideRoot"] = "That path isn't readable by the game. Put the image in the game's data folder.",
            ["book.creative.aspect"] = "This image is {0} x {1}; the slot is {2} x {3}. A different shape may be rejected in review.",
            ["book.creative.previewSkipped"] = "Preview skipped for a large image.",
            ["book.submit"] = "Review and pay", ["book.uploading"] = "Uploading your creative…", ["book.booking"] = "Reserving the slot…",
            ["book.cancelBooking"] = "Cancel this booking", ["book.cancelling"] = "Cancelling…",
            ["book.next"] = "Next: the server prices it and builds a payment. You approve it in your wallet.",
            ["book.problem.NoSlot"] = "Pick a slot first.", ["book.problem.NoTerm"] = "Pick a term.", ["book.problem.UnknownTerm"] = "That term isn't offered for this slot.",
            ["book.problem.NoCreative"] = "Add your creative image.", ["book.problem.CreativeInvalid"] = "Fix the creative image.",
            ["book.problem.BadLink"] = "The link must start with https:// and have no spaces.",
            ["book.problem.BadStart"] = "Start date must look like 2026-10-20.", ["book.problem.StartInPast"] = "Start date is in the past.",
            ["book.problem.StartOverlaps"] = "That start overlaps a booked window.", ["book.problem.StartNotSupported"] = "This term can't take a start date.",
            ["creative.Empty"] = "That file is empty or unreadable.", ["creative.TooLarge"] = "Too large. The limit is {0}.",
            ["creative.NotPngOrJpeg"] = "Only PNG or JPEG images are accepted.", ["creative.MimeNotAccepted"] = "That image type isn't accepted.",
            ["creative.UnreadableHeader"] = "The image header is damaged.", ["creative.DecodeTooBig"] = "The image is too many pixels. Keep it under 4096 px a side.",

            // ---- ads: my bookings
            ["mine.title"] = "My bookings", ["mine.empty"] = "No bookings yet. Pick a slot to start.",
            ["mine.loadFailed"] = "Couldn't load your bookings.",
            ["mine.runs"] = "Runs {0} to {1}", ["mine.datesLater"] = "Dates are set when it is approved.",
            ["mine.amount"] = "Price: {0}", ["mine.link"] = "Link: {0}",
            ["mine.reason"] = "Why: {0}", ["mine.rejectedNote"] = "Rejected ads are refunded to your wallet.",
            ["mine.refund"] = "Refund {0}: {1}", ["mine.refund.pending"] = "pending", ["mine.refund.sent"] = "sent", ["mine.refund.failed"] = "failed, contact support",
            ["mine.waitingPay"] = "Waiting for payment. If the payment window closed, cancel and book again.",
            ["mine.cancel"] = "Cancel booking", ["mine.cancelled"] = "Booking cancelled.",
            ["mine.cancelOnlyBeforePay"] = "A booking can be cancelled only before it is paid.",
            ["mine.creative"] = "Creative {0} x {1}, {2}",
            ["ad.status.awaiting_payment"] = "Awaiting payment", ["ad.status.pending_review"] = "Pending review", ["ad.status.scheduled"] = "Approved",
            ["ad.status.live"] = "Live", ["ad.status.expired"] = "Ended", ["ad.status.rejected"] = "Rejected", ["ad.status.cancelled"] = "Cancelled",
            ["ad.status.refunded"] = "Refunded", ["ad.status.unknown"] = "Unknown",
            ["toast.ad.pending_review"] = "Ad paid. A human is reviewing it.", ["toast.ad.scheduled"] = "Ad approved. It goes live at its start time.",
            ["toast.ad.live"] = "Your ad is live.", ["toast.ad.expired"] = "Your ad run ended.", ["toast.ad.rejected"] = "Ad rejected. {0}",
            ["toast.ad.cancelled"] = "Booking cancelled.", ["toast.ad.refunded"] = "Ad refunded.", ["toast.ad.unknown"] = "An ad booking changed.",
            ["toast.ad.awaiting_payment"] = "Ad booking is waiting for payment.",

            // ---- projects: Builder Hills
            ["builder.title"] = "Builder Hills",
            ["builder.hint"] = "Found a project, hire people, fund it with game Cash, launch the token. The server rolls exploits, rug accusations and VC dumps. Game Cash never leaves the game.",
            ["builder.tab.mine"] = "My projects", ["builder.tab.hiring"] = "Hiring", ["builder.tab.offers"] = "Offers", ["builder.tab.found"] = "Found one",
            ["builder.mine.empty"] = "You aren't in a project yet. Found one or accept an offer.",
            ["builder.hiring.empty"] = "Nobody is hiring right now.",
            ["builder.hiring.note"] = "Founders send the offers. Tell one you're available and they'll invite you.",
            ["builder.loadFailed"] = "Couldn't load projects.",
            ["builder.scope.hiring"] = "Open roles", ["builder.scope.all"] = "Everything",
            ["project.card.line"] = "{0} · {1} · {2} people", ["project.card.people"] = "{0} people",
            ["project.card.treasury"] = "Treasury {0}", ["project.card.open"] = "Hiring: {0}", ["project.by"] = "Founder: {0}",
            ["project.open"] = "Open HQ", ["project.someProject"] = "A project",
            ["kind.memecoin"] = "Memecoin", ["kind.dex"] = "DEX", ["kind.nft_studio"] = "NFT studio", ["kind.perps_venue"] = "Perps venue", ["kind.l2"] = "L2",
            ["kindhint.memecoin"] = "A dog, a frog, or a frog in a hat. Community-owned, allegedly.",
            ["kindhint.dex"] = "Swap anything for anything. Audit status: pending.",
            ["kindhint.nft_studio"] = "Art with utility. Utility TBA.",
            ["kindhint.perps_venue"] = "Leverage for the people. Liquidations for the people.",
            ["kindhint.l2"] = "Your own chain, with a bridge. The bridge is the risk.",
            ["pstatus.founded"] = "Founded", ["pstatus.launching"] = "Launching", ["pstatus.live"] = "Live", ["pstatus.paused"] = "Paused", ["pstatus.rugged"] = "Rugged", ["pstatus.dead"] = "Dead",
            ["role.founder"] = "Founder", ["role.dev"] = "Dev", ["role.mod"] = "Mod", ["role.kol"] = "KOL", ["role.auditor"] = "Auditor",

            // ---- found
            ["found.title"] = "Found a project",
            ["found.kind"] = "What are you building?",
            ["found.name"] = "Project name (3 to 24 characters). Parody names only: no real brands, people or projects.",
            ["found.ticker"] = "Ticker (optional, 2 to 8 letters or digits)",
            ["found.cost"] = "The seed money comes out of your Cash and goes into the project treasury. The server tells you the amount and whether you can afford it.",
            ["found.submit"] = "Found it", ["found.sending"] = "Founding…", ["found.done"] = "{0} is founded.",
            ["found.problem.Empty"] = "Give it a name.", ["found.problem.TooShort"] = "At least 3 characters.", ["found.problem.TooLong"] = "At most 24 characters.",
            ["found.problem.BadChars"] = "Letters, digits, spaces and - _ . $ ' only.", ["found.problem.LooksLikeLink"] = "A name can't be a link.",
            ["found.problem.ticker"] = "Ticker: 2 to 8 capital letters or digits.",

            // ---- HQ
            ["hq.title"] = "{0}", ["hq.treasury"] = "Treasury", ["hq.payroll"] = "Payroll {0} a week", ["hq.nextPayroll"] = "Next payroll {0}",
            ["hq.members"] = "Team", ["hq.member.line"] = "{0} · {1} · {2} a week", ["hq.founderLine"] = "{0} · {1}",
            ["hq.noMembers"] = "Just the founder so far.",
            ["hq.fire"] = "Remove", ["hq.fired"] = "Removed from the team.",
            ["hq.vc"] = "VC stakes", ["hq.vc.line"] = "{0}: {1} for {2}, unlocks {3}", ["hq.vc.none"] = "No VC money in yet.",
            ["hq.events"] = "What happened", ["hq.events.none"] = "Quiet so far.",
            ["hq.enter"] = "Enter HQ room", ["hq.entering"] = "Opening the door…", ["hq.noPresenter"] = "The 3D HQ room isn't wired into this build yet.",
            ["hq.entered"] = "You're in the HQ.",
            ["hq.loadFailed"] = "Couldn't load this project.", ["hq.visitor"] = "You're not on this team.",
            ["hq.riskEvent"] = "Risk event",
            ["fund.title"] = "Fund the treasury", ["fund.hint"] = "Move game Cash from your wallet into the project. It pays salaries and the launch. It can't be taken back out as money.",
            ["fund.amount"] = "Cash to move ($)", ["fund.submit"] = "Fund", ["fund.done"] = "Treasury funded.",
            ["fund.problem.Invalid"] = "Enter an amount like 250 or 99.50.", ["fund.problem.NotEnoughCash"] = "You only have {0} of Cash.",
            ["hire.title"] = "Hire", ["hire.hint"] = "Send a role offer to a player. They accept or decline within 48 hours. Salaries come out of the treasury each week.",
            ["hire.player"] = "Player id", ["hire.role"] = "Role", ["hire.salary"] = "Weekly salary ($)", ["hire.submit"] = "Send offer", ["hire.done"] = "Offer sent.",
            ["hire.npcNote"] = "NPCs can't be hired yet: offers go to real players only.",
            ["hire.problem.NoPlayer"] = "Paste the player's id.", ["hire.problem.BadRole"] = "Pick a role.", ["hire.problem.BadSalary"] = "Enter a weekly salary like 40 or 12.50.",
            ["hire.problem.Self"] = "You can't hire yourself.",

            // ---- launch
            ["launch.title"] = "Launch the token",
            ["launch.hint"] = "Lists your token on the in-game launchpad (fictional, server-simulated). The cost is quoted by the server; you confirm that exact number.",
            ["launch.ticker"] = "Ticker", ["launch.quote"] = "Get launch quote", ["launch.cost"] = "Launch cost {0}", ["launch.discount"] = "Builder discount {0} already applied",
            ["launch.treasury"] = "Treasury {0}", ["launch.confirm"] = "Launch for {0}", ["launch.confirming"] = "Launch {0} at exactly {1}?", ["launch.sending"] = "Launching…",
            ["launch.done"] = "Launched. The coin lists shortly.",
            ["launch.blocker.insufficient_treasury"] = "The treasury is too small for the launch cost.",
            ["launch.blocker.wrong_status"] = "This project can't launch in its current status.",
            ["launch.blocker.no_dev_hired"] = "Hire a dev first.", ["launch.blocker.cooldown"] = "Launch is on cooldown.",
            ["launch.gate.BadTicker"] = "Ticker: 2 to 8 capital letters or digits.", ["launch.gate.NotEnoughTreasury"] = "Fund the treasury first.",
            ["launch.gate.Blocked"] = "The server says this can't launch yet.",

            // ---- offers inbox
            ["offers.title"] = "Role offers", ["offers.empty"] = "No offers waiting.",
            ["offers.line"] = "{0} wants you as {1} for {2} a week", ["offers.accept"] = "Accept", ["offers.decline"] = "Decline",
            ["offers.accepted"] = "You joined {0}.", ["offers.declined"] = "Offer declined.", ["offers.status"] = "Offer {0}",
            ["offers.status.accepted"] = "accepted", ["offers.status.declined"] = "declined", ["offers.status.expired"] = "expired", ["offers.status.withdrawn"] = "withdrawn", ["offers.status.pending"] = "pending",
            ["offers.line.hired"] = "We're hiring.",

            // ---- VC Row
            ["vc.review"] = "Review",
            ["vc.expiredTag"] = "Expired",
            ["vc.offer.short"] = "{0} for {1}",
            ["vc.investRow"] = "Investment",
            ["vc.equityRow"] = "Equity",
            ["vc.impliedRow"] = "Implied valuation",
            ["vc.unlockRow"] = "Unlocks",
            ["vc.title"] = "VC Row",
            ["vc.hint"] = "Fleece vests, big smiles. A VC puts money in your treasury for equity, then may dump at unlock. The server decides when. Taking VC money is your call.",
            ["vc.tab.offers"] = "Term sheets", ["vc.tab.deals"] = "Deals board",
            ["vc.offers.empty"] = "No term sheets. VCs circle projects that launch, and ones with a good story.",
            ["vc.noProjects"] = "Found a project in Builder Hills and VCs will find you.",
            ["vc.offer.line"] = "{0} offers {1} for {2}", ["vc.offer.implied"] = "Implies a {0} company (estimate)", ["vc.offer.unlock"] = "Unlocks {0}",
            ["vc.offer.project"] = "For {0}", ["vc.offer.expires"] = "Decide {0}",
            ["vc.offer.risk"] = "They may dump at unlock. The server rolls it and the city hears about it.",
            ["vc.accept"] = "Take the money", ["vc.decline"] = "Pass", ["vc.accepted"] = "Money in. Treasury updated.", ["vc.declined"] = "You passed.",
            ["vc.expired"] = "This term sheet expired.", ["vc.status"] = "Term sheet {0}",
            ["vc.status.pending"] = "pending", ["vc.status.accepted"] = "accepted", ["vc.status.declined"] = "declined", ["vc.status.expired"] = "expired",
            ["vc.loadFailed"] = "Couldn't load term sheets.", ["vc.deals.loadFailed"] = "Couldn't load the deals board.",
            ["vc.deals.empty"] = "No deals on the board yet.",
            ["vc.deal.line"] = "{0}: {1} for {2}", ["vc.deal.status.active"] = "holding", ["vc.deal.status.unlocked"] = "unlocked", ["vc.deal.status.dumped"] = "DUMPED",
            ["vc.name.vc"] = "A VC partner", ["vc.name.default"] = "A VC partner",
            ["toast.vcOffer"] = "{0}: \"We're excited to back you.\" {1} for {2}.", ["toast.hireOffer"] = "{0} wants you as {1}.",
            ["toast.vcDump"] = "VC dump: {0}. \"Monthly unlock, sold OTC.\"",

            // ---- project events (registry ids; unknown ids get a readable fallback)
            ["pevent.proj_founded"] = "Project founded", ["pevent.proj_member_joined"] = "Someone joined", ["pevent.proj_member_left"] = "Someone left",
            ["pevent.proj_launched"] = "Token launched", ["pevent.proj_audit_passed"] = "Audit passed (by a guy)", ["pevent.proj_exploit"] = "EXPLOIT. Funds are probably fine.",
            ["pevent.proj_rug_accusation"] = "Rug accusation", ["pevent.proj_vc_dump"] = "VC dumped at unlock", ["pevent.proj_payroll_missed"] = "Payroll missed",
            ["pevent.proj_paused"] = "Project paused", ["pevent.proj_dead"] = "Project dead",
            ["pevent.line"] = "{0}: {1}", ["pevent.delta"] = "{0}",
        };

        public static string T(string key) => key != null && En.TryGetValue(key, out var s) ? s : key ?? "";

        public static bool Has(string key) => key != null && En.ContainsKey(key);

        public static string F(string key, params object[] args)
        {
            try { return string.Format(CultureInfo.InvariantCulture, T(key), args ?? new object[0]); }
            catch (System.FormatException) { return T(key); }
        }

        public static IEnumerable<string> Keys => En.Keys;

        public static string Kind(string id) => Has("kind." + id) ? T("kind." + id) : VWire.Readable(id);
        public static string Role(string id) => Has("role." + id) ? T("role." + id) : VWire.Readable(id);
        public static string ProjectStatus(string id) => Has("pstatus." + id) ? T("pstatus." + id) : VWire.Readable(id);
        public static string AdKind(string id) => Has("ads.kind." + id) ? T("ads.kind." + id) : VWire.Readable(id);
        public static string AdStatusText(AdStatus s) => T("ad.status." + AdStatuses.Wire(s));
        public static string VcName(string id) => Has("vc.name." + id) ? T("vc.name." + id) : T("vc.name.default");

        /// <summary>A line for a project event: its own copy, or the readable id when the registry has grown past this build.</summary>
        public static string ProjectEventLine(ProjectEvent e)
        {
            if (e == null) return "";
            var what = Has("pevent." + e.EventId) ? T("pevent." + e.EventId) : VWire.Readable(e.EventId.StartsWith("proj_", System.StringComparison.Ordinal) ? e.EventId.Substring(5) : e.EventId);
            if (e.CashDeltaCents == 0) return what;
            return F("pevent.line", what, CTLife.Core.Money.Cash(e.CashDeltaCents));
        }

        public static string CreativeProblemText(CreativeProblem p, long maxBytes) =>
            p == CreativeProblem.TooLarge ? F("creative.TooLarge", Bytes(maxBytes)) : T("creative." + p);

        /// <summary>1048576 → "1 MB", 482113 → "471 KB".</summary>
        public static string Bytes(long n)
        {
            if (n >= 1024 * 1024) return (n / 1048576.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
            if (n >= 1024) return (n / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return n.ToString(CultureInfo.InvariantCulture) + " B";
        }
    }
}
