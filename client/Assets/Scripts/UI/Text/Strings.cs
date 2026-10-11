using System.Collections.Generic;
using System.Globalization;
using CTLife.Core;

namespace CTLife.UI
{
    /// <summary>
    /// Player-facing words. toasts.json is "BE (FE writes the words)", so every toast id's text lives here, with
    /// {param} slots filled from the toast params. Money params ending in Cents are formatted as Cash.
    /// </summary>
    public static class Strings
    {
        public const string Brand = "CT LIFE";

        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            ["credits.title"] = "Credits & licenses",
            ["credits.snow"] = "Character art · Snow",
            ["credits.snowSummary"] = "Snow Rig (CC) Blender Foundation | studio.blender.org. Adapted proportions, rig and texture atlas. CC-BY 4.0.",
            ["credits.rain"] = "Character art · Rain",
            ["credits.rainSummary"] = "Rain Rig (CC) Blender Foundation | studio.blender.org. Adapted proportions, rig and texture atlas. CC-BY 4.0.",
            ["credits.vincent"] = "Character art · Vincent",
            ["credits.vincentSummary"] = "Vincent (CC) Blender Foundation | studio.blender.org. Adapted proportions, rig, facial shapes and texture atlas. CC-BY 4.0.",
            ["credits.manrope"] = "Typography · Manrope",
            ["credits.manropeSummary"] = "The Manrope Project Authors. SIL Open Font License 1.1.",
            ["credits.lilita"] = "Display typography · Lilita One",
            ["credits.lilitaSummary"] = "Juan Montoreano. SIL Open Font License 1.1.",
            ["credits.music"] = "Music · Kevin MacLeod",
            ["credits.musicSummary"] = "Music by Kevin MacLeod (incompetech.com), CC-BY 4.0. Full track credits and adaptation details below.",
            ["credits.phosphor"] = "Icons · Phosphor",
            ["credits.phosphorSummary"] = "Phosphor Icons. MIT license. Fill and bold icons adapted to raster images.",
            ["credits.source"] = "Visit original source",
            ["credits.read"] = "Read notice & license",
            ["credits.missing"] = "This notice could not be loaded.",
            ["welcome.eyebrow"] = "Life on the timeline",
            ["welcome.title"] = "A life of your own.",
            ["welcome.body"] = "Find your people. Make your name.",
            ["welcome.note"] = "Big dreams. Fictional coins. Real company.",
            ["welcome.wallet"] = "Sign in with Solana",
            ["welcome.walletNote"] = "Approve a sign-in request in your wallet to continue.",
            ["welcome.walletAndroid"] = "Open CT Life on Android to sign in with your wallet.",
            ["welcome.age"] = "18+ only. Cash is game money and cannot be withdrawn.",
            ["welcome.unavailable"] = "Sign-in is taking a breather. Please try again shortly.",
            ["welcome.busy"] = "Opening your wallet…",
            ["splash.gm"] = "gm. Your city is waking up.",
            ["splash.tagline"] = "A little chaos. A life of your own.",
            ["update.title"] = "A new chapter is here",
            ["update.body"] = "Update CT Life in the Solana dApp Store to return to the city.",
            ["update.version"] = "Your version: {0} · Latest required: {1}",
            ["creator.eyebrow"] = "Your character",
            ["creator.title"] = "Character studio",
            ["creator.intro"] = "A look, a little personality, and a big dream.",
            ["creator.preview"] = "Drag to turn",
            ["creator.step"] = "{0}  {1}",
            ["creator.step.0"] = "Look", ["creator.step.1"] = "Personality", ["creator.step.2"] = "Identity",
            ["creator.fullBody"] = "Full body", ["creator.face"] = "Face",
            ["creator.previousLook"] = "Previous look", ["creator.nextLook"] = "Next look",
            ["creator.lookCount"] = "{0} of {1} looks",
            ["creator.wholeLook"] = "Complete look",
            ["creator.look.breeze"] = "Breeze",
            ["creator.look.weekender"] = "Weekender",
            ["creator.look.everyday"] = "Everyday",
            ["creator.lookMissing"] = "No character preview available",
            ["creator.lookMissingDetail"] = "You can explore your personality and identity using the tabs above.",
            ["creator.lookPreviewOnly"] = "Preview look · not available to save yet",
            ["creator.lookReady"] = "A complete look, ready to wear.",
            ["creator.lookNotAvailable"] = "This look isn't available to use yet. Your choices haven't been saved.",
            ["creator.needLook"] = "A character look is needed before you can enter the city.",
            ["creator.lookGuidance"] = "Turn your character. Take a closer look at the face.",
            ["creator.toPersonality"] = "Make it personal",
            ["creator.toIdentity"] = "Choose my name",
            ["creator.personalityReady"] = "Your story is taking shape.",
            ["creator.yourStory"] = "Your story",
            ["creator.editHint"] = "Use the tabs above to change your choices.",
            ["creator.storyEmpty"] = "Choose your look and personality to start your story.",
            ["creator.finishPreview"] = "Finish preview",
            ["creator.previewComplete"] = "Preview complete",
            ["creator.previewNotice"] = "This is a preview. Your choices won't be saved.",
            ["creator.previewSavedNothing"] = "Preview complete. No character has been created.",
            ["creator.perk.launch"] = "Sees launches {0:0}s early",
            ["creator.perk.leverage"] = "Leverage cap +{0:0}x",
            ["creator.perk.prediction"] = "Prediction fee −{0:0}%",
            ["creator.perk.farm"] = "Farm yield +{0:0}%",
            ["creator.perk.reach"] = "Calls reach {0:0}x followers",
            ["creator.perk.launchCost"] = "Launch cost −{0:0}%",
            ["creator.perk.hidden"] = "Hidden from the rich list",
            ["creator.look"] = "Your look",
            ["creator.lookDetail"] = "Make yourself at home in your own skin.",
            ["creator.identity"] = "Your name on CT",
            ["creator.alias"] = "Alias",
            ["creator.aliasHelp"] = "3–20 characters. This is how the city knows you.",
            ["creator.tribe"] = "Find your people",
            ["creator.tribeDetail"] = "Choose one tribe. Each brings its own perk.",
            ["creator.traits"] = "A little personality",
            ["creator.traitsDetail"] = "Choose two traits that feel like you.",
            ["creator.traitsCount"] = "{0} of 2 selected",
            ["creator.dream"] = "Dream a little bigger",
            ["creator.dreamDetail"] = "What are you here to become?",
            ["creator.age"] = "I am 18 or older",
            ["creator.submit"] = "Start my story",
            ["creator.saving"] = "Making your entrance…",
            ["creator.needAlias"] = "Choose an alias with at least 3 characters.",
            ["creator.needTribe"] = "Choose your tribe to continue.",
            ["creator.needTraits"] = "Choose two personality traits.",
            ["creator.needDream"] = "Choose a dream for your new life.",
            ["creator.needAge"] = "Confirm you are 18 or older to continue.",
            ["creator.ready"] = "Your next chapter is ready.",
            ["slot.skin"] = "Skin tone", ["slot.head"] = "Face", ["slot.hair"] = "Hair",
            ["slot.top"] = "Top", ["slot.bottom"] = "Bottom", ["slot.shoes"] = "Shoes", ["slot.acc"] = "Extra",
            ["slot.previous"] = "Previous {0}", ["slot.next"] = "Next {0}",
            ["slot.skinTone"] = "Tone {0}", ["slot.faceA"] = "Face A", ["slot.faceB"] = "Face B", ["slot.none"] = "None",
            ["common.close"] = "Close", ["common.back"] = "Back", ["common.cancel"] = "Cancel",
            ["common.loading"] = "One moment…", ["common.unavailable"] = "Unavailable", ["common.soon"] = "Coming soon",
            ["common.empty"] = "Nothing here just yet.", ["common.retry"] = "Try again",
            ["common.error"] = "Something went wrong. Please try again.",
            ["hud.cash"] = "Game cash", ["hud.online"] = "{0:N0} in the city", ["hud.mood"] = "Market {0}",
            ["hud.lobby"] = "Lobby",
            ["lobby.eyebrow"] = "YOUR NEXT CHAPTER",
            ["lobby.title"] = "MAKE YOUR\nNEXT MOVE.",
            ["lobby.city"] = "CT CITY",
            ["lobby.enter"] = "ENTER THE CITY",
            ["lobby.enterBusy"] = "OPENING THE CITY…",
            ["lobby.story"] = "Your story",
            ["lobby.dream"] = "THE DREAM",
            ["lobby.home"] = "HOME BASE",
            ["lobby.noDream"] = "A story to write",
            ["lobby.noHome"] = "Your place in the city",
            ["lobby.noTribe"] = "City resident",
            ["lobby.energy"] = "ENERGY",
            ["lobby.cash"] = "GAME CASH",
            ["lobby.cashNote"] = "Game Cash stays in CT Life.",
            ["lobby.map"] = "Explore",
            ["lobby.jobs"] = "Jobs",
            ["lobby.bank"] = "Bank",
            ["lobby.profile"] = "Me",
            ["lobby.phone"] = "Phone",
            ["lobby.ore"] = "ORE",
            ["lobby.tokenNote"] = "ORE + SKR · part of your city",
            ["controls.title"] = "Movement",
            ["controls.analog"] = "Analog stick",
            ["controls.arrows"] = "Arrow buttons",
            ["controls.analogHelp"] = "Drag to walk. Pull farther to run.",
            ["controls.arrowsHelp"] = "Hold an arrow to walk. Drag past it to run.",
            ["controls.walk"] = "Walk", ["controls.run"] = "Run",
            ["controls.up"] = "Move forward", ["controls.down"] = "Move backward",
            ["controls.left"] = "Move left", ["controls.right"] = "Move right",
            ["hud.onlineWaiting"] = "City · —",
            ["hud.moodWaiting"] = "Market · —", ["hud.needs"] = "Your needs",
            ["hud.needsToggle"] = "Self-care", ["hud.needsGood"] = "Feeling good",
            ["hud.needsLow"] = "{0} · {1}", ["hud.needsShow"] = "Show your needs", ["hud.needsHide"] = "Hide your needs",
            ["hud.chat"] = "Chat", ["hud.emote"] = "Emotes", ["hud.micOff"] = "Mic off", ["hud.micOn"] = "Mic on",
            ["hud.voiceUnavailable"] = "Voice off", ["hud.voiceError"] = "Voice is temporarily unavailable.",
            ["hud.voiceHere"] = "Voice isn't available here yet.", ["hud.map"] = "Explore", ["hud.phone"] = "Phone",
            ["hud.chatPrompt"] = "Say something to the room", ["hud.say"] = "Say it", ["hud.wait"] = "Give it {0:0.0}s, then try again.",
            ["hud.emotesTitle"] = "A whole mood", ["hud.emotesEmpty"] = "Your emotes are on their way.",
            ["phone.title"] = "Phone", ["phone.eyebrow"] = "Your city",
            ["phone.greeting"] = "gm, {0}", ["phone.greetingDefault"] = "gm, neighbour",
            ["phone.subtitle"] = "Make a move. Find your people.", ["phone.apps"] = "Your apps",
            ["phone.later"] = "On the way", ["phone.soon"] = "{0} is coming to the city soon.",
            ["phone.unavailable"] = "{0} is taking a breather. Try again soon.",
            ["phone.error"] = "This app couldn't open. Head back and try again.",
            ["phone.home"] = "Your apps", ["phone.close"] = "Put phone away",
            ["map.title"] = "Where to next?", ["map.eyebrow"] = "EXPLORE CT CITY",
            ["map.body"] = "Good company, bad takes, and a few new beginnings.",
            ["map.openCount"] = "{0} places to drop into", ["map.openCountOne"] = "1 place to drop into", ["map.open"] = "Drop in", ["map.club"] = "Club",
            ["map.closed"] = "Opening soon", ["map.empty"] = "The city map is loading. Try again in a moment.",
            ["district.timeline"] = "The conversation starts here.",
            ["district.trenches"] = "Big conviction. Bigger plot twists.",
            ["district.builder_hills"] = "Make something worth talking about.",
            ["district.airdrop_farm"] = "A little patience. A little possibility.",
            ["district.dubai_marina"] = "Dress up. Dream big. Stay awhile.",
            ["lottery.eyebrow"] = "EVERY STORY STARTS SOMEWHERE",
            ["lottery.title"] = "Your origin story",
            ["lottery.body"] = "Where you start is luck. Where you go is yours.",
            ["lottery.note"] = "One beginning, just for you. All rewards are game Cash.",
            ["lottery.insider"] = "Insider Baby", ["lottery.insiderDetail"] = "$3,000 Cash\nA studio of your own",
            ["lottery.exit"] = "Exit Liquidity Baby", ["lottery.exitDetail"] = "$300 Cash\n$720 to pay back",
            ["lottery.seeker"] = "Seeker Baby", ["lottery.seekerDetail"] = "$1,500 Cash\nA Hacker House + badge",
            ["lottery.roll"] = "Discover my beginning", ["lottery.rolling"] = "Your story is unfolding…",
            ["lottery.enter"] = "Enter CT City", ["lottery.result"] = "{0} Cash · {1}",
            ["lottery.debt"] = "{0} to pay back. Plenty of story ahead.",
            ["app.map"] = "Map", ["app.bank"] = "Bank", ["app.richlist"] = "Rich List", ["app.home"] = "Home",
            ["app.profile"] = "Profile", ["app.settings"] = "Settings", ["app.jobs"] = "Jobs",
            ["bank.title"] = "A little room to dream", ["bank.balance"] = "YOUR CASH",
            ["bank.debt"] = "To pay back: {0}\n$120 is repaid each week.", ["bank.noDebt"] = "No debt. A fresh start.",
            ["bank.note"] = "Cash stays in CT Life. It can't be withdrawn.", ["bank.topup"] = "Cash top-ups are coming soon.",
            ["rich.title"] = "The city is watching", ["rich.netWorth"] = "Net worth", ["rich.weekly"] = "This week's PnL",
            ["rich.you"] = "Your place: #{0} · {1}", ["rich.empty"] = "A fresh leaderboard. Your story belongs here.",
            ["home.title"] = "A moment for yourself", ["home.fallback"] = "Your place", ["home.done"] = "That's better.",
            ["home.eat"] = "Grab a bite", ["home.sleep"] = "Get some rest", ["home.shower"] = "Freshen up", ["home.touch_grass"] = "Touch grass",
            ["profile.wallet"] = "Wallet · {0}", ["profile.miner"] = "Miner tier · {0}", ["profile.skills"] = "What you're good at",
            ["settings.music"] = "Music", ["settings.sound"] = "Sound effects", ["settings.signout"] = "Sign out",
            ["settings.version"] = "CT Life {0}", ["settings.note"] = "Set the mood. Make the city your own.",
            ["busy.title"] = "A full house", ["busy.body"] = "The city is busy. We'll keep trying for you.",
            ["busy.retry"] = "Trying again in {0}s", ["busy.retrying"] = "Trying again…",
            ["profile.npc"] = "CITY LOCAL", ["profile.player"] = "Player", ["profile.block"] = "Block", ["profile.report"] = "Report",
            ["profile.blocked"] = "Player blocked.", ["profile.reportTitle"] = "Report {0}",
            ["profile.reportWhy"] = "What happened?", ["profile.reportThanks"] = "Report sent. Thanks for letting us know.",
            ["profile.netWorth"] = "{0} · Net worth {1}", ["profile.dropWallet"] = "Drop wallet",
            ["profile.giveaway"] = "“Like, RT, drop your wallets, join my TG”",
            ["report.spam"] = "Spam", ["report.scam"] = "Scam", ["report.harassment"] = "Harassment",
            ["report.impersonation"] = "Impersonation", ["report.nsfw"] = "Sexual or graphic content",
            ["report.target.user"] = "player", ["report.target.message"] = "message", ["report.target.space"] = "Space",
            ["report.target.call"] = "call", ["report.target.post"] = "post", ["report.target.ad"] = "advert",
            ["report.target.other"] = "content", ["report.other"] = "Other concern",
            ["rekt.title"] = "REKT", ["rekt.body"] = "Liquidated. It's so over.", ["rekt.coin"] = "Coin {0}",
            ["rekt.share"] = "Share · soon", ["rekt.shareUnavailable"] = "Card sharing isn't available yet.",
            ["rekt.dismiss"] = "We're so back",
        };

        public static string T(string key) => Copy.TryGetValue(key, out var text) ? text : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
        public static string ReadableId(string id) => string.IsNullOrEmpty(id) ? "—" : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
        public static string DistrictDetail(string id) => Copy.TryGetValue("district." + id, out var text) ? text : "";
        public static string ReportReason(string id, string configuredName) => Copy.TryGetValue("report." + id, out var text)
            ? text : !string.IsNullOrEmpty(configuredName) && configuredName != id ? configuredName : T("report.other");
        public static string ReportTarget(string id) => Copy.TryGetValue("report.target." + id, out var text) ? text : T("report.target.other");

        private static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string>
        {
            ["top_hoodie_black"] = "Black hoodie", ["top_hoodie_white"] = "White hoodie", ["top_suit_black"] = "Black suit",
            ["acc_hard_hat"] = "Hard hat", ["acc_aviators"] = "Aviator shades",
            ["hair_mohawk_pink"] = "Pink mohawk", ["hair_villa_blonde"] = "Blonde hair", ["head_skull_visor"] = "Skull visor",
            ["bottom_jeans_indigo"] = "Indigo jeans", ["bottom_cargo_olive"] = "Olive cargos",
            ["shoes_sneakers_white"] = "White sneakers", ["shoes_boots_black"] = "Black boots",
        };

        public static string ItemName(string id, string configuredName = null)
        {
            if (!string.IsNullOrEmpty(configuredName) && configuredName != id) return configuredName;
            if (!string.IsNullOrEmpty(id) && ItemNames.TryGetValue(id, out var name)) return name;
            int separator = id?.IndexOf('_') ?? -1;
            return ReadableId(separator >= 0 ? id.Substring(separator + 1) : id);
        }

        private static readonly Dictionary<string, string> Toasts = new Dictionary<string, string>
        {
            ["toast_drained"] = "You got drained. -{amountCents}. Never drop your wallet.",
            ["toast_payment_confirmed"] = "Payment confirmed: +{cashCents} Cash",
            ["toast_promoted"] = "Promoted! {careerId} level {level}",
            ["toast_shift_paid"] = "Shift paid: +{payCents} · +{xp} XP",
            ["toast_rent_paid"] = "Rent paid: {rentCents}",
            ["toast_rent_missed"] = "Rent missed: {rentCents}. Landlord is not amused.",
            ["toast_debt_payment"] = "Debt payment {amountCents}. {remainingCents} to go.",
            ["toast_debt_missed"] = "Debt payment missed. Penalty {penaltyCents}.",
            ["toast_tax_paid"] = "Taxes paid: {taxCents}",
            ["toast_tip_received"] = "{fromAlias} tipped you {cashCents}",
            ["toast_needs_low"] = "Your {needId} is low ({value})",
            ["toast_spot_taken"] = "Someone is already there",
            ["toast_space_promoted"] = "You're a speaker now",
            ["toast_ad_approved"] = "Your ad on {slotId} is live",
            ["toast_ad_rejected"] = "Your ad on {slotId} was rejected",
        };

        private static readonly Dictionary<string, string> Errors = new Dictionary<string, string>
        {
            ["NETWORK_ERROR"] = "No connection. Check your network and try again.",
            ["INSUFFICIENT_CASH"] = "Not enough Cash.",
            ["ALIAS_TAKEN"] = "That alias is taken.",
            ["ALREADY_ROLLED"] = "You were already born. No re-rolls.",
            ["NONCE_INVALID"] = "Sign-in expired. Try again.",
            ["SIGNATURE_INVALID"] = "The wallet signature didn't check out. Try again.",
            ["TOKEN_EXPIRED"] = "Session expired. Sign in again.",
            ["SGT_ALREADY_LINKED"] = "This Seeker is already linked to another account.",
            ["CLIENT_OUTDATED"] = "Update required.",
            ["RATE_LIMITED"] = "Slow down a little.",
            ["VENUE_FULL"] = "This venue is full. Try again in a moment.",
            ["SPOT_TAKEN"] = "Someone is already there.",
            ["BLOCKED"] = "You can't interact with this player.",
            ["SHIFT_NOT_FINISHED"] = "Shift isn't over yet.",
            ["LEVERAGE_NOT_ALLOWED"] = "Your Trading skill doesn't allow that leverage yet.",
            ["SLIPPAGE_EXCEEDED"] = "Price moved too much. Try again.",
            ["TOPUP_CAP_REACHED"] = "Daily top-up cap reached.",
        };

        public static string Toast(string id, IReadOnlyDictionary<string, string> p)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (!Toasts.TryGetValue(id, out var t) && CTLife.Core.Services.TryGet<ConfigStore>(out var cfg) &&
                cfg.Registries.ToastTextIds.TryGetValue(id, out var mapped))
                Toasts.TryGetValue(mapped, out t);
            if (t == null)
            {
                Log.UnknownId("toasts", id);
                return id;
            }
            if (p == null) return t;
            foreach (var kv in p)
            {
                var v = kv.Value ?? "";
                if (kv.Key.EndsWith("Cents") && Money.TryParseCents(v, out var cents)) v = Money.Cash(cents);
                t = t.Replace("{" + kv.Key + "}", v);
            }
            return t;
        }

        public static string Error(string code, string serverMessage = null)
        {
            if (code != null && Errors.TryGetValue(code, out var m)) return m;
            return T("common.error");
        }

        public static string NeedName(string needId) => needId switch
        {
            "energy" => "Energy",
            "hunger" => "Hunger",
            "fun" => "Fun",
            "hygiene" => "Hygiene",
            "sanity" => "Sanity",
            _ => needId,
        };
    }
}
