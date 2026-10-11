using System.Collections.Generic;
using System.Globalization;

namespace CTLife.CityLife
{
    /// <summary>
    /// All City Life copy (English). CT-native, parody only: no real brands, people or projects. Flavour lines are grounded
    /// in the research recorded in docs/progress/city-life.md ("Research and sources"). Pure so tests can render messages.
    /// </summary>
    public static class CityLifeStrings
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // ---- time / common
            ["time.now"] = "now",
            ["common.retry"] = "Try again",
            ["common.cancel"] = "Cancel",
            ["common.close"] = "Close",
            ["common.back"] = "Back",
            ["common.loading"] = "Loading…",
            ["common.copied"] = "Copied. Paste it anywhere.",
            ["common.phase4"] = "Opens in Phase 4.",
            ["common.notLive"] = "Not live on this server yet.",
            ["common.estimate"] = "estimate",
            ["common.updated"] = "Updated {0} ago",

            // ---- errors
            ["err.generic"] = "Something went wrong.",
            ["err.network"] = "No connection. Check your signal and try again.",
            ["err.server"] = "The city server hiccuped. Try again in a moment.",
            ["err.invalidResponse"] = "The server sent something unexpected.",
            ["err.unknownOutcome"] = "We couldn't confirm that. Try again - it won't be charged twice.",
            ["err.unavailable"] = "Not live on this server yet.",
            ["err.insufficientCash"] = "Not enough Cash (short {0}).",
            ["err.validation"] = "The server didn't accept that.",
            ["err.notFound"] = "Not found.",
            ["err.forbidden"] = "You can't do that.",
            ["err.blocked"] = "You can't reach this player.",
            ["err.rateLimited"] = "Slow down. Try again in {0}.",
            ["err.busy"] = "The city is busy. Retrying shortly.",
            ["err.session"] = "Your session expired. Sign in again.",
            ["err.outdated"] = "Update CT Life to continue.",
            ["err.paymentExpired"] = "That payment expired. Start again.",
            ["err.paymentUnderpaid"] = "The payment was short. Contact support with the signature.",
            ["err.walletDeclined"] = "You cancelled in the wallet.",
            ["err.walletFailed"] = "The wallet reported an error. Checking whether it went through…",
            ["err.alreadyAnswered"] = "You've used today's guess. New clue tomorrow.",
            ["err.homeFull"] = "That home is full right now.",

            // ---- needs
            ["need.energy"] = "Energy", ["need.hunger"] = "Hunger", ["need.fun"] = "Fun", ["need.hygiene"] = "Hygiene", ["need.sanity"] = "Sanity",
            ["needs.title"] = "Needs",
            ["needs.hint"] = "Needs drop every hour, even while you're offline. Bars update between server reads.",
            ["needs.emptyIn"] = "{0} hits zero in {1}",
            ["needs.allGood"] = "All needs fine.",
            ["needs.low.energy"] = "Running on fumes. Log off and sleep before your next shift.",
            ["needs.low.hunger"] = "Last meal was a conference protein bar. Eat something.",
            ["needs.low.fun"] = "All charts, no fun. Go somewhere with music.",
            ["needs.low.hygiene"] = "Third day in the same hoodie. Shower.",
            ["needs.low.sanity"] = "You've refreshed the chart 400 times today. Touch grass.",
            ["needs.critical"] = "{0} is critical ({1}). {2}",
            ["needs.empty"] = "{0} is empty. {1}",
            ["needs.remedy"] = "Fix: {0}.",
            ["needs.clubOnly"] = "Parties only happen at clubs: {0}.",
            ["needs.done"] = "Done. Needs updated by the server.",
            ["needs.unconfirmed"] = "That wasn't confirmed. Your needs may have changed - refreshing.",
            ["needs.working"] = "Taking a moment…",
            ["needs.unavailable"] = "Needs config isn't loaded yet. Close and reopen to check again.",

            ["action.eat"] = "Eat", ["action.sleep"] = "Sleep", ["action.shower"] = "Shower", ["action.touch_grass"] = "Touch grass", ["action.party"] = "Party",

            // ---- furniture (home scene taps)
            ["furniture.title"] = "{0}",
            ["furniture.bed"] = "Bed", ["furniture.sofa"] = "Sofa", ["furniture.desk"] = "Desk", ["furniture.fridge"] = "Fridge",
            ["furniture.kitchen"] = "Kitchen", ["furniture.shower"] = "Shower", ["furniture.bath"] = "Bath", ["furniture.plant"] = "Plant",
            ["furniture.balcony"] = "Balcony", ["furniture.garden"] = "Garden", ["furniture.speaker"] = "Speaker",
            ["furniture.bed.sleep"] = "Sleep (actually log off)",
            ["furniture.sofa.sleep"] = "Nap on the sofa",
            ["furniture.sofa.party"] = "Throw a house party",
            ["furniture.speaker.party"] = "Throw a house party",
            ["furniture.desk.eat"] = "Instant ramen at the desk",
            ["furniture.fridge.eat"] = "Raid the fridge",
            ["furniture.kitchen.eat"] = "Cook something real",
            ["furniture.shower.shower"] = "Shower (the group chat can wait)",
            ["furniture.bath.shower"] = "Long bath, phone face-down",
            ["furniture.plant.touch_grass"] = "Touch the one plant you own",
            ["furniture.balcony.touch_grass"] = "Step out on the balcony",
            ["furniture.garden.touch_grass"] = "Touch actual grass",
            ["furniture.visitorOnly"] = "Only the owner can use this.",

            // ---- homes
            ["home.title"] = "Home",
            ["home.current"] = "You live in {0}",
            ["home.unknown"] = "Home",
            ["home.enter"] = "Go home",
            ["home.return"] = "Return to city",
            ["home.explore"] = "Walk around. Tap the bed, desk or sofa to use them.",
            ["home.sceneMissing"] = "This home's interior isn't in this build yet. Phone actions still work.",
            ["home.entryFailed"] = "Couldn't open your home. Try again from Home.",
            ["home.rentPerWeek"] = "{0} / week",
            ["home.rooms"] = "{0} rooms",
            ["home.youLiveHere"] = "You live here",
            ["home.move"] = "Move here",
            ["home.downsize"] = "Downsize here",
            ["home.renew"] = "Pay rent now",
            ["home.noPrice"] = "No price from the server",
            ["home.movesTitle"] = "Homes",
            ["home.movesHint"] = "Rent is due at Sunday Unlock (Sun 20:00 UTC). Moving signs a new lease.",
            ["home.confirmMove"] = "Move to {0}? Rent {1} a week, charged by the server.",
            ["home.confirmRenew"] = "Pay {0} rent for {1} now?",
            ["home.signing"] = "Signing the lease…",
            ["home.moved"] = "Lease signed: {0}.",
            ["home.renewed"] = "Rent paid for {0}.",
            ["home.cantAffordHint"] = "Your Cash ({0}) is below this rent.",
            ["home.flavor.parents_basement"] = "Mum thinks you \"work in IT\". The Wi-Fi drops when the kettle's on.",
            ["home.flavor.hacker_house"] = "Six founders, one fridge, a whiteboard that says SHIP IT. Nobody settles the rent split on time.",
            ["home.flavor.studio"] = "Ring light, triple monitors, a printed PnL card taped to the mirror.",
            ["home.flavor.penthouse"] = "Skyline view, standing desk, wall space for the PFP you actually paid for.",
            ["home.flavor.marina_villa"] = "The pitch was 0% personal income tax. The supercar out front is rented.",

            // ---- rent / debt
            ["rent.title"] = "Rent",
            ["rent.dueIn"] = "Due in {0} ({1})",
            ["rent.lotteryDebt"] = "Birth-lottery debt: {0}. {1} is due each Sunday Unlock; missing it adds {2}.",
            ["rent.debt"] = "Debt: {0}",
            ["rent.shortfall"] = "You're {0} short.",
            ["rent.weeksLeft"] = "{0} week(s) before eviction.",
            ["rent.state.unknown"] = "Rent status unknown",
            ["rent.state.paid"] = "Paid up",
            ["rent.state.duesoon"] = "Due soon",
            ["rent.state.shortofcash"] = "Due soon - not enough Cash",
            ["rent.state.duenow"] = "Due now - Sunday Unlock is settling",
            ["rent.state.overdue"] = "Overdue",
            ["rent.state.evictionrisk"] = "Eviction risk",
            ["rent.state.evicted"] = "Evicted",
            ["rent.toast.evicted"] = "Evicted. The landlord changed the locks.",
            ["rent.toast.evictionRisk"] = "Last warning: pay rent or you're out.",
            ["rent.toast.overdue"] = "Rent is overdue.",
            ["rent.toast.debtUp"] = "Your debt went up at Sunday Unlock.",
            ["rent.toast.settled"] = "Sunday Unlock: rent settled.",
            ["rent.toast.short"] = "Rent is due soon and your Cash won't cover it.",

            // ---- visits
            ["visit.title"] = "Visits",
            ["visit.hint"] = "Tap a player and choose Visit home, or enter their player id.",
            ["visit.idLabel"] = "Player id",
            ["visit.go"] = "Knock",
            ["visit.myRoom"] = "Open my home to visitors",
            ["visit.requesting"] = "Knocking…",
            ["visit.joining"] = "Walking in…",
            ["visit.inside"] = "Inside {0}'s home · {1} here",
            ["visit.insideOwn"] = "Your home is open · {0} here",
            ["visit.leave"] = "Leave",
            ["visit.leaving"] = "Leaving…",
            ["visit.closed"] = "The door closed. The owner ended the visit or the room dropped.",
            ["visit.noRoom"] = "This server logs visits but has no home room yet.",
            ["visit.joinFailed"] = "Couldn't get in. Try knocking again.",
            ["visit.denied.visits_closed"] = "They're not taking visitors.",
            ["visit.denied.followers_only"] = "Followers only.",
            ["visit.denied.blocked"] = "You can't visit this player.",
            ["visit.presenceOnly"] = "You're in the room. The 3D interior for visits arrives with the World hook.",
            ["visit.policy"] = "Who can visit",
            ["visit.policy.open"] = "Anyone",
            ["visit.policy.followers"] = "Followers",
            ["visit.policy.closed"] = "Nobody",
            ["visit.policySaved"] = "Saved.",
            ["visit.visitorsHere"] = "Here now: {0}",

            // ---- week / Sunday Unlock / meta / mood
            ["week.title"] = "This week",
            ["week.unlockIn"] = "Sunday Unlock in {0}",
            ["week.unlockAt"] = "Rent, the gas bill and the Weekly Meta rotate at {0}.",
            ["week.resetIn"] = "Weekly boards and tax reset in {0}",
            ["week.metaNow"] = "Weekly Meta: {0}",
            ["week.metaNext"] = "Next week: {0}",
            ["week.metaUnknown"] = "Weekly Meta not announced yet.",
            ["week.metaPerk"] = "Last week's clue winners got {0} each.",
            ["week.mood"] = "Market Mood: {0} ({1} SOL 24h)",
            ["week.moodOffline"] = "Market Mood: offline (city room not joined)",
            ["week.events"] = "Happening now",
            ["week.noEvents"] = "Quiet timeline. For now.",
            ["week.recap"] = "Since you last checked",
            ["week.share"] = "Copy share card",

            ["meta.rent_half.title"] = "Landlord got rugged",
            ["meta.rent_half.body"] = "Rent is half price this Sunday Unlock.",
            ["meta.trading_fee_half.title"] = "Fee holiday",
            ["meta.trading_fee_half.body"] = "Terminal Café trading fees are halved.",
            ["meta.double_tip.title"] = "Tip season",
            ["meta.double_tip.body"] = "Tips count double this week.",
            ["meta.npc_density_double.title"] = "Conference week",
            ["meta.npc_density_double.body"] = "COIN2050 Singapura is in town: twice the NPCs, a thousand side events, and someone pitching tokenized stocks in the lift.",
            ["meta.leverage_cap_plus_2x.title"] = "Degen week",
            ["meta.leverage_cap_plus_2x.body"] = "+2x on your leverage cap. The Rekt Clinic added beds.",
            ["meta.cosmetics_sale.title"] = "Drip sale",
            ["meta.cosmetics_sale.body"] = "Cosmetics are on sale this week.",
            ["meta.alpha_clue_prize_x3.title"] = "Alpha week",
            ["meta.alpha_clue_prize_x3.body"] = "Daily clue prizes are tripled.",

            ["mood.capitulation"] = "It's so over - three optimists left on CT",
            ["mood.red"] = "Cooked - longs getting liquidated, the Doomposter is out",
            ["mood.crab"] = "Crab market - everyone is \"accumulating\"",
            ["mood.green"] = "We're so back - shorts getting squeezed",
            ["mood.euphoria"] = "Euphoria - a memecoin will flip Bitcoin, apparently",

            ["event.evt_liquidation"] = "Liquidations",
            ["event.evt_rug"] = "Rug",
            ["event.evt_coin_launch"] = "New launch",
            ["event.evt_mood_change"] = "Mood shift",
            ["event.evt_exploit"] = "Exploit on Audit Row",
            ["event.evt_ore_motherlode"] = "ORE motherlode",

            ["recap.first"] = "First check-in this week. Come back after Sunday Unlock for a recap.",
            ["recap.cashUp"] = "Cash up {0}",
            ["recap.cashDown"] = "Cash down {0}",
            ["recap.debtUp"] = "Debt up {0}",
            ["recap.debtDown"] = "Debt paid down {0}",
            ["recap.moved"] = "Moved to {0}",
            ["recap.meta"] = "Meta: {0}",
            ["recap.mood"] = "Mood: {0} ({1})",
            ["recap.rent"] = "Rent: {0}",
            ["share.weekTitle"] = "{0} · CT City {1}",
            ["share.footer"] = "Survived another Sunday Unlock in CT Life.",

            // ---- daily clue
            ["clue.title"] = "Daily alpha",
            ["clue.hint"] = "One guess a day. Right answers pay Cash.",
            ["clue.expires"] = "Expires in {0}",
            ["clue.answer"] = "Your answer",
            ["clue.submit"] = "Lock it in",
            ["clue.checking"] = "Checking…",
            ["clue.correct"] = "Correct. +{0} Cash.",
            ["clue.wrong"] = "Faded. New clue tomorrow.",
            ["clue.already"] = "You've used today's guess.",
            ["clue.none"] = "No clue today. The alpha group chat went quiet.",
            ["clue.expired"] = "Today's clue expired.",
            ["clue.unknown"] = "We couldn't confirm your answer. Send the same answer again - it counts once.",
            ["clue.alphaWeek"] = "Alpha week: prizes are tripled.",

            // ---- shop / wardrobe / season pass
            ["shop.title"] = "Shop",
            ["shop.tab.shop"] = "Shop",
            ["shop.tab.wardrobe"] = "Wardrobe",
            ["shop.tab.pass"] = "Season pass",
            ["shop.hint"] = "Cash items only. Cosmetics bought with Cash stay off-chain.",
            ["shop.slot.skin"] = "Skin", ["shop.slot.head"] = "Head", ["shop.slot.hair"] = "Hair", ["shop.slot.top"] = "Top",
            ["shop.slot.bottom"] = "Bottom", ["shop.slot.shoes"] = "Shoes", ["shop.slot.acc"] = "Accessory",
            ["shop.buy"] = "Buy {0}",
            ["shop.owned"] = "Owned",
            ["shop.onchain"] = "Real-money only",
            ["shop.bought"] = "Bought {0}.",
            ["shop.empty"] = "Nothing in this aisle.",
            ["shop.sale"] = "Drip sale is on this week.",
            ["wardrobe.wearing"] = "Wearing",
            ["wardrobe.wear"] = "Wear",
            ["wardrobe.save"] = "Save look",
            ["wardrobe.revert"] = "Revert",
            ["wardrobe.saving"] = "Saving…",
            ["wardrobe.saved"] = "Look saved. Players see it next time you enter a venue.",
            ["wardrobe.unknown"] = "We couldn't confirm the save. Save again - it's the same request.",
            ["wardrobe.starter"] = "Starter",
            ["pass.title"] = "Season pass",
            ["pass.none"] = "No season pass yet.",
            ["pass.active"] = "Season pass active until {0}.",
            ["pass.expired"] = "Your season pass expired {0}.",
            ["pass.unknown"] = "Couldn't read your pass.",
            ["pass.unavailable"] = "Season pass status isn't live on this server yet.",
            ["pass.buy"] = "Buy with {0}",
            ["pass.priceFromIntent"] = "The price and amount come from the payment request before you sign.",
            ["pass.review"] = "Pay {0} {1}. Sign in your wallet to continue.",
            ["pass.sign"] = "Sign in wallet",
            ["pay.creating"] = "Preparing the payment…",
            ["pay.signing"] = "Waiting for your wallet…",
            ["pay.confirming"] = "Confirming on-chain…",
            ["pay.confirmed"] = "Payment confirmed.",
            ["pay.pending"] = "Payment pending. We'll confirm it when the network does.",
            ["pay.expired"] = "That payment request expired.",
            ["pay.failed"] = "The payment failed.",
            ["pay.noTx"] = "This server didn't build a wallet transaction. Solana Pay link copied instead.",
            ["pay.unavailable"] = "Payments are unavailable right now.",
            ["pay.copyLink"] = "Copy Solana Pay link",
            ["pay.mainnetAck"] = "I understand this spends real USDC.",

            // ---- businesses
            ["biz.title"] = "Businesses",
            ["biz.hint"] = "Buy a business with Cash. It pays out over time - and has bad days.",
            ["biz.notLive"] = "Businesses aren't live on this server yet. Browse the catalog; buying opens when the server ships it.",
            ["biz.cost"] = "Costs {0}",
            ["biz.income"] = "Pays {0}–{1} per payout",
            ["biz.badDay"] = "Bad day {0}: pays {1}",
            ["biz.expected"] = "≈ {0} per payout ({1})",
            ["biz.breakEven"] = "≈ {0} payouts to break even ({1})",
            ["biz.sellBack"] = "Sells back for {0}",
            ["biz.buy"] = "Buy",
            ["biz.collect"] = "Collect {0}",
            ["biz.sell"] = "Sell back",
            ["biz.owned"] = "Yours",
            ["biz.collected"] = "Collected {0}.",
            ["biz.collectedBad"] = "Collected {0}. {1} bad day(s) in there.",
            ["biz.sold"] = "Sold for {0}.",
            ["biz.bought"] = "You own a {0}.",
            ["biz.lastDay"] = "Last day: {0}",
            ["biz.day.normal"] = "Normal day", ["biz.day.great_day"] = "Great day", ["biz.day.bad_day"] = "Bad day",
            ["biz.flavor.kols_agency"] = "Rent out reply guys and ring lights. Clients pay for engagement, not results.",
            ["biz.flavor.audit_firm"] = "Sign off on contracts you skimmed. Pray nothing gets exploited.",
            ["biz.flavor.memecoin_lab"] = "Launch parody coins on a bonding curve. Most of them bleed.",
            ["biz.flavor.lounge"] = "Bottle service for whoever's up this week.",
            ["biz.bd.kol_exposed"] = "A sleuth thread says your top KOL faked his PnL screenshots. Clients paused.",
            ["biz.bd.reply_farm_flagged"] = "Your clients' wallets showed up in a public KOL wallet database. Every \"organic\" call is now a screenshot.",
            ["biz.bd.audit_exploited"] = "A protocol you audited got exploited on Audit Row. A bot called Yoinker front-ran the hacker; your reputation didn't make it.",
            ["biz.bd.dev_faked_death"] = "Your dev faked his own death on stream and rugged.",
            ["biz.bd.launch_bundled"] = "Sleuth thread: your launch was \"fully bundled\".",
            ["biz.bd.flash_crash"] = "Oil shock: hundreds of millions in longs gone in ten minutes. Nobody's ordering bottles.",
            ["biz.bd.generic"] = "Bad day. The timeline turned on you.",
            ["biz.bd.kol_wallets_leaked"] = "Your clients' wallets showed up in a public KOL wallet database.",
            ["biz.bd.exchange_hack"] = "Bitgot froze withdrawals after a \"maintenance event\". Your treasury is stuck.",
            ["biz.bd.holder_rewards_change"] = "Pomp.fun changed its rewards overnight. Your lab's launch plan is cooked.",

            // ---- NFT gallery
            ["nft.title"] = "Gallery wall",
            ["nft.hint"] = "Frame NFTs from your wallet on your walls. The server checks you still hold them.",
            ["nft.notLive"] = "The gallery isn't live on this server yet.",
            ["nft.empty"] = "No NFTs found in your signed-in wallet.",
            ["nft.slots"] = "{0} of {1} frames used",
            ["nft.slot"] = "Frame {0}",
            ["nft.emptySlot"] = "Empty frame",
            ["nft.hang"] = "Hang",
            ["nft.remove"] = "Take down",
            ["nft.save"] = "Save wall",
            ["nft.saved"] = "Wall saved.",
            ["nft.pending"] = "In review - only you see this frame.",
            ["nft.flagged"] = "Flagged by moderation. It can't be hung.",
            ["nft.loadFailed"] = "Image unavailable",
            ["nft.visitorsNote"] = "Visitors only see approved art.",
            ["nft.noWalls"] = "This home has no wall spots for frames in this build.",

            // ---- apps
            ["app.home"] = "Home",
            ["app.week"] = "Week",
            ["app.shop"] = "Shop",
            ["app.biz"] = "Business",
        };

        public static string T(string key) => key != null && En.TryGetValue(key, out var v) ? v : key;

        public static string F(string key, params object[] args)
        {
            var t = T(key);
            if (args == null || args.Length == 0) return t;
            try { return string.Format(CultureInfo.InvariantCulture, t, args); }
            catch (System.FormatException) { return t; }
        }

        public static bool Has(string key) => key != null && En.ContainsKey(key);

        public static string NeedName(string need) => T("need." + need);

        public static string ActionLabel(string action) => Has("action." + action) ? T("action." + action) : Readable(action);

        public static string FurnitureLabel(string kind, string action) =>
            Has("furniture." + kind + "." + action) ? T("furniture." + kind + "." + action) : ActionLabel(action);

        public static string MetaTitle(string bonusId) => Has("meta." + bonusId + ".title") ? T("meta." + bonusId + ".title") : Readable(bonusId);

        public static string MetaBody(string bonusId) => Has("meta." + bonusId + ".body") ? T("meta." + bonusId + ".body") : "";

        public static string EventLabel(string eventId) => Has("event." + eventId) ? T("event." + eventId) : Readable(eventId);

        public static string BadDay(string eventId) => eventId != null && Has("biz.bd." + eventId) ? T("biz.bd." + eventId) : T("biz.bd.generic");

        public static string Readable(string id)
        {
            if (string.IsNullOrEmpty(id)) return "-";
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
        }
    }
}
