using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.App;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Duo iOS redesign wave 3 (`-ctlife-captureSet duoios3`): every remaining catalog app at Large and XXL (Rich List, League,
    /// Home, Predict, Shop, Business, Builder, Ads, ORE, Pulse, Week, Profile, Top up, Map, Cabal, Beef, Farm, VC Row, Chat,
    /// X link), the wave-2 fixes (Jobs titles, Perp Pit bar), key sheets at L, and the mini-game chrome. REST is an in-memory
    /// read-only fixture (writes rejected); every fixture row says REVIEW on screen. Games run in practice/review mode and
    /// are cancelled after the shot (no shift, no score sent).
    /// </summary>
    internal static class DuoIos3Capture
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Me = "00000000-0000-4000-8000-000000000091";
        private const string Kemi = "review-duo-kemi", Hiro = "review-duo-hiro", Amara = "review-duo-amara", Lucia = "review-duo-lucia", Tunde = "review-duo-tunde";

        private sealed class Rest : IRestClient
        {
            public IRestClient Inner;
            public int Writes;
            public readonly List<string> Reads = new List<string>();
            public readonly List<string> Missing = new List<string>();
            public Func<Task<bool>> RefreshHandler { get; set; }
            private static long Ago(double minutes) => (long)(ServerClock.NowMs - minutes * 60000);
            private static string Iso(double minutesAgo) => DateTimeOffset.FromUnixTimeMilliseconds(Ago(minutesAgo)).ToString("o");

            private static JObject Coin(string id, string ticker, string name, long priceMicros, int bps, long mcap, string stage = "graduated") => new JObject
            { ["coinId"] = id, ["ticker"] = ticker, ["name"] = name, ["stage"] = stage, ["priceMicros"] = priceMicros,
              ["change5mBps"] = bps, ["mcapCents"] = mcap, ["vol5mCents"] = mcap / 40, ["holders"] = 312, ["launchedAt"] = Iso(42) };

            private static JObject Detail(string coinId)
            {
                // REVIEW fixture series: 300 one-minute points, a deterministic walk.
                var candles = new JArray();
                double price = 196.0;
                var random = new System.Random(7);
                for (int i = 299; i >= 0; i--)
                {
                    price += 0.062 + (random.NextDouble() - 0.5) * 1.4;
                    candles.Add(new JObject { ["at"] = Iso(i), ["priceMicros"] = (long)(price * 1_000_000) });
                }
                return new JObject { ["coin"] = Coin(coinId, "FROGE", "Froge", (long)(price * 1_000_000), 444, 1234567800), ["candles1m"] = candles,
                    ["topHolders"] = new JArray(new JObject { ["alias"] = "kemi.sol", ["units"] = "88000000000" }, new JObject { ["alias"] = "Hiro", ["units"] = "41000000000" }),
                    ["riskHint"] = new JObject { ["level"] = "medium" } };
            }

            private static JObject Perp(string id, string coin, string ticker, string side, string status, int lev, long margin, long entry, long liq, long? realized)
            {
                var o = new JObject { ["perpId"] = id, ["coinId"] = coin, ["ticker"] = ticker, ["side"] = side, ["status"] = status, ["leverage"] = lev,
                    ["marginCents"] = margin, ["entryMicros"] = entry, ["liqMicros"] = liq };
                if (realized.HasValue) { o["realizedCents"] = realized.Value; o["closedAt"] = Iso(240); }
                return o;
            }

            private static JObject Board(string board, string scope)
            {
                // REVIEW fixture ranks and values.
                long[] values = board == "beef_elo" ? new long[] { 1612, 1544, 1490, 1422, 1388, 1301 } : board == "weekly_pnl"
                    ? new long[] { 412000, 256000, 98000, 12000, -3400, -88000 } : new long[] { 2840000, 1910000, 1225000, 640000, 410000, 303500 };
                // The generated Rich List client types userId as a Guid: REVIEW guids (…00a1–a5), the fixture player last.
                string[] ids = board == "beef_elo" || scope == "league" ? new[] { Kemi, Hiro, Amara, Lucia, Tunde, Me }
                    : new[] { "00000000-0000-4000-8000-0000000000a1", "00000000-0000-4000-8000-0000000000a2", "00000000-0000-4000-8000-0000000000a3",
                        "00000000-0000-4000-8000-0000000000a4", "00000000-0000-4000-8000-0000000000a5", Me };
                string[] aliases = { "kemi.sol", "Hiro", "Amara", "lucia.eth", "tunde", "dev46011" };
                var entries = new JArray();
                for (int i = 0; i < ids.Length; i++) entries.Add(new JObject { ["rank"] = i + 1, ["userId"] = ids[i], ["alias"] = aliases[i], ["value"] = values[i] });
                return new JObject { ["entries"] = entries, ["me"] = new JObject { ["rank"] = 6, ["userId"] = Me, ["alias"] = "dev46011", ["value"] = values[5] } };
            }

            private JToken Fixture(string path)
            {
                if (path.StartsWith("/leaderboards/", StringComparison.Ordinal))
                {
                    var rest = path.Substring("/leaderboards/".Length);
                    var board = rest.Split('?')[0];
                    return Board(board, rest.Contains("scope=league") ? "league" : "global");
                }
                switch (path)
                {
                    case "/me/league": return new JObject { ["seasonIso"] = "2026-W42", ["membership"] = new JObject { ["leagueId"] = "silver", ["rank"] = 6, ["netWorthCents"] = 303500 } };
                    case "/leagues": return new JObject { ["leagues"] = new JArray(
                        new JObject { ["slug"] = "bronze", ["label"] = "Bronze", ["minNetWorthCents"] = 0, ["maxMembers"] = 50, ["promoteToSlug"] = "silver", ["sortOrder"] = 1 },
                        new JObject { ["slug"] = "silver", ["label"] = "Silver", ["minNetWorthCents"] = 100000, ["maxMembers"] = 50, ["promoteToSlug"] = "gold", ["sortOrder"] = 2 },
                        new JObject { ["slug"] = "gold", ["label"] = "Gold", ["minNetWorthCents"] = 1000000, ["maxMembers"] = 30, ["sortOrder"] = 3 }) };
                    case "/tribes": return new JObject { ["tribes"] = new JArray(
                        new JObject { ["tribeId"] = "builder", ["name"] = "Builders", ["perk"] = "REVIEW · +5% project treasury yield", ["memberCount"] = 128, ["ownedDistricts"] = new JArray("builder_hills") },
                        new JObject { ["tribeId"] = "trencher", ["name"] = "Trenchers", ["perk"] = "REVIEW · cheaper Pulse fees", ["memberCount"] = 211, ["ownedDistricts"] = new JArray("trenches") }) };
                    case "/districts": return new JObject { ["districts"] = new JArray(
                        new JObject { ["districtId"] = "builder_hills", ["name"] = "Builder Hills", ["currentOwnerTribeId"] = "builder", ["controlledSince"] = Iso(3000) },
                        new JObject { ["districtId"] = "trenches", ["name"] = "The Trenches", ["currentOwnerTribeId"] = "trencher", ["controlledSince"] = Iso(900) }) };
                    case "/predictions": return new JObject { ["predictions"] = new JArray(
                        new JObject { ["predictionId"] = "review-pred-1", ["outcomeId"] = "btc-24h-up", ["stakeCents"] = 2500, ["status"] = "open", ["horizon"] = "daily", ["closesAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 5 * 3600000)).ToString("o"), ["createdAt"] = Iso(60) },
                        new JObject { ["predictionId"] = "review-pred-2", ["outcomeId"] = "btc-24h-up", ["stakeCents"] = 1000, ["status"] = "won", ["horizon"] = "1h", ["closesAt"] = Iso(600), ["createdAt"] = Iso(660) },
                        new JObject { ["predictionId"] = "review-pred-3", ["outcomeId"] = "btc-24h-up", ["stakeCents"] = 4000, ["status"] = "lost", ["horizon"] = "15m", ["closesAt"] = Iso(1500), ["createdAt"] = Iso(1515) }) };
                    case "/ore/board": return new JObject { ["roundId"] = "review-418", ["endsAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 42000)).ToString("o"),
                        ["motherlodeOre"] = "1240000000000", ["cityStakedOre"] = "88150000000000", ["priceUsd"] = "2.41",
                        ["squares"] = new JArray(Enumerable.Range(0, 25).Select(i => (JToken)((i * 7919 % 23 + 1) * 10000000L).ToString())) };
                    case "/ore/me": return new JObject { ["balance"] = "350000000000", ["staked"] = "1200000000000", ["stOre"] = "1180000000000", ["minerTier"] = "pickaxe" };
                    case "/perps?status=open": return new JObject { ["perps"] = new JArray(Perp("review-perp-1", "review-froge", "FROGE", "long", "open", 5, 50000, 205100000, 168200000, null)) };
                    case "/perps": return new JObject { ["perps"] = new JArray(Perp("review-perp-1", "review-froge", "FROGE", "long", "open", 5, 50000, 205100000, 168200000, null)) };
                    case "/careers": return new JObject { ["careers"] = new JArray(new JObject
                        { ["id"] = "discord_mod", ["name"] = "Discord Mod", ["level"] = 3, ["xp"] = 140, ["active"] = true, ["payCents"] = 7000, ["shiftSec"] = 45 }) };
                    case "/gigs": return new JObject { ["gigs"] = new JArray() };
                    case "/market/positions": return new JObject { ["positions"] = new JArray(
                        new JObject { ["positionId"] = "review-pos-1", ["coinId"] = "review-froge", ["ticker"] = "FROGE", ["units"] = "1200000000", ["costBasisCents"] = 24000, ["unrealizedCents"] = 1726, ["currentPriceMicros"] = 214380000 }) };
                    case "/shop": return new JObject { ["items"] = new JArray(
                        new JObject { ["itemId"] = "review-item-1", ["itemKey"] = "top_vneck_charcoal", ["slot"] = "top", ["name"] = "REVIEW · Charcoal V-neck", ["priceCents"] = 2500 },
                        new JObject { ["itemId"] = "review-item-2", ["itemKey"] = "shoes_sneakers_blue", ["slot"] = "shoes", ["name"] = "REVIEW · Blue sneakers", ["priceCents"] = 4000 },
                        new JObject { ["itemId"] = "review-item-3", ["itemKey"] = "hair_snow_bun", ["slot"] = "hair", ["name"] = "REVIEW · Snow bun", ["priceCents"] = 1500 }) };
                    case "/inventory": return new JObject { ["items"] = new JArray(new JObject { ["itemId"] = "review-item-3", ["itemKey"] = "hair_snow_bun", ["slot"] = "hair", ["name"] = "REVIEW · Snow bun" }) };
                    case "/businesses": return new JObject { ["owned"] = new JArray(new JObject { ["businessId"] = "review-biz-1", ["kindId"] = "kols_agency", ["pendingCents"] = 4800,
                        ["nextPayoutAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 3 * 3600000)).ToString("o"), ["sellBackCents"] = 15000 }) };
                    case "/meta/current": return new JObject { ["value"] = new JObject { ["bonus"] = "alpha_clue_prize_x3", ["weekIso"] = "2026-W42", ["perkCents"] = 5000 } };
                    case "/events/active": return new JObject { ["events"] = new JArray() };
                    case "/daily-clue": return new JObject { ["clueText"] = "REVIEW · I have keys but open no locks. Every degen loses me eventually.", ["date"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
                        ["expiresAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 6 * 3600000)).ToString("o"), ["prizeCents"] = 2000 };
                    case "/beef/elo": return new JObject { ["userId"] = Me, ["elo"] = 1301, ["wins"] = 4, ["losses"] = 3, ["draws"] = 1, ["matchesPlayed"] = 8 };
                    case "/airdrop/farm": return new JObject { ["dayDate"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"), ["baseCents"] = 500, ["sgtBonusBps"] = 2000, ["tribeBonusBps"] = 500,
                        ["todayAmountCents"] = 625, ["dailyCapCents"] = 1000, ["streakDays"] = 5, ["claimedToday"] = false };
                    case "/airdrop/claims": return new JObject { ["claims"] = new JArray(
                        new JObject { ["claimId"] = "review-claim-1", ["dayDate"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"), ["amountCents"] = 625, ["sgtBonus"] = true },
                        new JObject { ["claimId"] = "review-claim-2", ["dayDate"] = DateTimeOffset.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"), ["amountCents"] = 500, ["sgtBonus"] = false }) };
                    case "/me/sybil-status": return new JObject { ["jailed"] = false };
                    case "/me/cabal": return new JObject { ["member"] = true, ["since"] = Iso(20000), ["invitesRemaining"] = 2, ["hostGateCents"] = 1000000 };
                    case "/venues/dubai_marina/access": return new JObject { ["venueId"] = "dubai_marina", ["allowed"] = false, ["requiredNetWorthCents"] = 5000000, ["netWorthCents"] = 303500, ["reasonCode"] = "NET_WORTH_GATE" };
                    case "/venues/cabal_lounge/access": return new JObject { ["venueId"] = "cabal_lounge", ["allowed"] = true, ["isMember"] = true, ["netWorthCents"] = 303500 };
                    case "/llm/quota": return new JObject { ["perMinuteLimit"] = 5, ["remainingThisMinute"] = 5, ["perDayLimit"] = 50, ["remainingToday"] = 44 };
                    case "/x/link": return new JObject { ["link"] = null };
                    case "/ads/slots": return new JObject { ["slots"] = new JArray(
                        new JObject { ["slotId"] = "billboard_01", ["kind"] = "billboard", ["venueId"] = "timeline_square", ["terms"] = new JArray(new JObject { ["termId"] = "term_7d", ["days"] = 7, ["priceUsdc"] = "25000000" }) },
                        new JObject { ["slotId"] = "phone_app_01", ["kind"] = "phone_app", ["terms"] = new JArray(new JObject { ["termId"] = "term_30d", ["days"] = 30, ["priceUsdc"] = "60000000" }), ["nextFreeAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 4 * 86400000L)).ToString("o") }) };
                    case "/ads/bookings": return new JObject { ["bookings"] = new JArray() };
                    case "/vc-row/deals?limit=20": case "/vc-row/deals?": return new JObject { ["deals"] = new JArray(
                        new JObject { ["projectId"] = "review-proj-9", ["projectName"] = "REVIEW · Degen Index", ["vcArchetypeId"] = "vc_tourist", ["investCents"] = 500000, ["equityBps"] = 1500, ["status"] = "active", ["at"] = Iso(800) }) };
                    case "/me/project-offers?": case "/me/project-offers?limit=20": return new JObject { ["offers"] = new JArray() };
                }
                if (path.StartsWith("/market/coins/", StringComparison.Ordinal)) return Detail(path.Substring("/market/coins/".Length).Split('?')[0]);
                if (path.StartsWith("/market/coins", StringComparison.Ordinal))
                {
                    if (path.Contains("stage=new") || path.Contains("stage=final"))
                        return new JObject { ["coins"] = new JArray(Coin("review-new", "NEWT", "Newt", 41000, 1820, 6200000, "new"), Coin("review-fin", "FINL", "Final Boss", 98000, 420, 38000000, "final")) };
                    return new JObject { ["coins"] = new JArray(
                        Coin("review-froge", "FROGE", "Froge", 214380000, 444, 1234567800),
                        Coin("review-brrr", "BRRR", "Money Printer", 3412000, -215, 412000000),
                        Coin("review-cope", "COPE", "Copium", 88100, 1032, 98000000)) };
                }
                if (path.StartsWith("/projects?scope=", StringComparison.Ordinal)) return new JObject { ["projects"] = new JArray(
                    new JObject { ["projectId"] = "review-proj-1", ["name"] = "REVIEW · Froge Swap", ["kind"] = "defi_app", ["status"] = "live", ["ownerUserId"] = Me, ["ownerAlias"] = "dev46011",
                        ["memberCount"] = 3, ["treasuryCents"] = 182000, ["openRoles"] = new JArray("dev") },
                    new JObject { ["projectId"] = "review-proj-2", ["name"] = "REVIEW · Copium Labs", ["kind"] = "nft_collection", ["status"] = "founded", ["ownerUserId"] = Kemi, ["ownerAlias"] = "kemi.sol",
                        ["memberCount"] = 1, ["treasuryCents"] = 40000 }) };
                if (path.StartsWith("/projects/review-proj-1/vc-offers", StringComparison.Ordinal)) return new JObject { ["offers"] = new JArray(
                    new JObject { ["offerId"] = "review-vc-1", ["projectId"] = "review-proj-1", ["vcArchetypeId"] = "vc_tourist", ["investCents"] = 250000, ["equityBps"] = 1000, ["status"] = "pending",
                        ["expiresAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 2 * 3600000)).ToString("o") }) };
                if (path.StartsWith("/beef", StringComparison.Ordinal) && !path.StartsWith("/beef/", StringComparison.Ordinal)) return new JObject { ["matches"] = new JArray(
                    new JObject { ["matchId"] = "review-beef-1", ["challengerUserId"] = Me, ["accepterUserId"] = Kemi, ["topicId"] = "beef_perps_vs_spot", ["status"] = "voting", ["stakeCents"] = 2000, ["feeBps"] = 500,
                        ["phaseEndsAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 25 * 60000)).ToString("o"), ["openedAt"] = Iso(80) },
                    new JObject { ["matchId"] = "review-beef-2", ["challengerUserId"] = Hiro, ["invitedUserId"] = Me, ["topicId"] = "beef_memes_vs_utility", ["status"] = "open", ["stakeCents"] = 1000, ["feeBps"] = 500, ["openedAt"] = Iso(20) },
                    new JObject { ["matchId"] = "review-beef-3", ["challengerUserId"] = Me, ["accepterUserId"] = Amara, ["topicId"] = "beef_fast_chain_vs_l2", ["status"] = "closed", ["stakeCents"] = 1500, ["feeBps"] = 500,
                        ["winnerUserId"] = Me, ["eloChallengerPre"] = 1280, ["eloChallengerPost"] = 1301, ["settledAt"] = Iso(2000) }) };
                if (path.StartsWith("/cabal/invites?guestId=", StringComparison.Ordinal)) return new JObject { ["invites"] = new JArray(
                    new JObject { ["inviteId"] = "review-inv-1", ["hostUserId"] = Lucia, ["guestUserId"] = Me, ["status"] = "pending", ["expiresAt"] = DateTimeOffset.FromUnixTimeMilliseconds((long)(ServerClock.NowMs + 86400000)).ToString("o") }) };
                if (path.StartsWith("/cabal/invites?hostId=", StringComparison.Ordinal)) return new JObject { ["invites"] = new JArray(
                    new JObject { ["inviteId"] = "review-inv-2", ["hostUserId"] = Me, ["guestUserId"] = Tunde, ["status"] = "accepted", ["createdAt"] = Iso(4000) }) };
                if (path.StartsWith("/users/", StringComparison.Ordinal)) { var id = path.Substring(7); return new JObject { ["userId"] = id, ["alias"] = id.Replace("review-duo-", "") }; }
                return null;
            }

            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Reads.Add(path);
                var value = Fixture(path);
                if (value == null)
                {
                    Missing.Add(path);
                    return Inner != null ? Inner.Get<T>(path, ct) : Task.FromException<T>(new ApiException(404, "NOT_FOUND", "No review fixture for " + path));
                }
                return Task.FromResult(value is T exact ? exact : value.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings)));
            }

            private Task<T> Reject<T>() { Writes++; return Task.FromException<T>(new InvalidOperationException("Duo wave 3 capture rejected a write.")); }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => method == "GET" ? Get<T>(path, ct) : Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }

        public static Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            var report = new JObject { ["scope"] = "REVIEW FIXTURE: shipping catalog apps; in-memory read-only REST (writes rejected); games in review/practice mode, cancelled after the shot." };
            receipt["duoIos3"] = report;
            return LobbyFlowCapture.Run(shot, wait, expect, step, receipt, phone => Duo(phone, shot, wait, expect, step, report), hookDebtCents: 72000);
        }

        /// <summary>The pre-Duo box/control classes the sweep must not find inside an open app.</summary>
        private static readonly string[] LegacyClasses =
        {
            "ct-card", "ct-btn", "ct-chip", "ct-row-btn", "ct-app-page", "ct-app-tabs", "ct-field", "ct-badge", "ct-h1", "ct-h2",
            "ct-duo-status", "ct-duo-metric", "ct-duo-card-footer", "ct-duo-segment", "ct-money-card", "ctc-card", "ctc-btn",
        };

        private static void Tap(VisualElement e)
        {
            if (e == null) return;
            using (var ev = NavigationSubmitEvent.GetPooled()) { ev.target = e; e.SendEvent(ev); }
        }

        private static async Task Duo(PhoneShell catalogPhone, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject report)
        {
            var savedPrefs = DuoPrefs.Store;
            DuoPrefs.Store = new DuoPrefs.Memory();
            Services.TryGet<IRestClient>(out var previousRest);
            var rest = new Rest { Inner = previousRest };
            PhoneShell phone = null;
            var mark = El.Text("REVIEW FIXTURE · DUO WAVE 3");
            mark.pickingMode = PickingMode.Ignore;
            mark.style.position = Position.Absolute; mark.style.left = 36; mark.style.bottom = 12;
            mark.style.fontSize = 18; mark.style.color = new Color(.24f, .24f, .3f); mark.style.backgroundColor = Color.white;
            UiRoot.Instance.Root.Add(mark);
            var session = Services.Get<Session>();
            var accessToken = typeof(Session).GetProperty(nameof(Session.AccessToken));
            var savedToken = session.AccessToken;
            string heldKey = null;
            Api previousApi = null;
            try
            {
                accessToken.SetValue(session, "review-fixture-not-a-token");
                heldKey = "ctlife." + (Env.Current != null ? Env.Current.id : "env") + ".careers.held." + session.UserId;
                PlayerPrefs.SetString(heldKey, "discord_mod");
                catalogPhone.Dispose();
                Services.Register<IRestClient>(rest);
                // Rich List reads through the generated Api: point it at the fixture too (restored in finally).
                Services.TryGet<Api>(out previousApi);
                Services.Register(new Api(rest, Services.Get<ConfigStore>()));
                phone = new PhoneShell(UiRoot.Instance.PhoneLayer);
                PhoneCatalog.Register(phone, _ => { }, () => { });
                VisualElement Layer() => UiRoot.Instance.PhoneLayer;
                string[] apps = { "richlist", "league", "home", "predict", "shop", "biz", "builder", "ads", "ore", "pulse", "week", "profile",
                    "topup", "map", "cabal", "beef", "farm", "vc", "npcchat", "xlink", "jobs", "perps" };

                foreach (var size in new[] { DuoTextSize.L, DuoTextSize.XXL })
                {
                    string tag = size == DuoTextSize.L ? "L" : "XXL";
                    bool large = size == DuoTextSize.L;
                    DuoText.Set(size);
                    step("Duo wave 3 " + tag);
                    foreach (var id in apps)
                    {
                        phone.Open(id);
                        await wait(1.4f);
                        if (id == "perps")
                        {
                            Tap(Layer().Q<Button>("perps-coin-review-froge"));
                            await wait(1.2f);
                        }
                        await shot("duo-wave3-" + id + "-" + tag, 0.5f);
                        // Sweep: none of the old box styles inside the open app.
                        var body = Layer().Q(className: "ct-phone-body");
                        var found = new JArray();
                        int legacy = 0;
                        if (body == null) legacy = -1;
                        else foreach (var cls in LegacyClasses)
                        {
                            int n = body.Query(className: cls).ToList().Count;
                            if (n > 0) { legacy += n; found.Add(cls + "×" + n); }
                        }
                        report["legacy-" + id + "-" + tag] = found;
                        expect(legacy == 0, tag + " " + id + ": no old box styles (" + string.Join(", ", found) + ")");
                        if (!large) continue;
                        switch (id)
                        {
                            case "richlist":
                                expect(Layer().Q("duo-podium") != null, "Rich List podium from the top 3");
                                expect(Layer().Q("duo-board-pin") != null, "Rich List pins your own row");
                                break;
                            case "league":
                                expect(Layer().Q("league-hero") != null, "League hero rank");
                                break;
                            case "home":
                                expect(Layer().Q("citylife-needs-summary") != null, "Home needs summary");
                                Tap(Layer().Q<Button>("citylife-need-energy"));
                                await shot("duo-wave3-home-need-sheet-L", 0.9f);
                                expect(Layer().Q("citylife-need-sheet") != null, "a need row opens its sheet");
                                break;
                            case "predict":
                                Tap(Layer().Q<Button>("predict-bet-review-pred-1"));
                                await shot("duo-wave3-predict-sheet-L", 0.9f);
                                expect(Layer().Q("predict-bet-sheet") != null, "a bet row opens its sheet");
                                break;
                            case "pulse":
                                Tap(Layer().Q<Button>("pulse-coin-review-new"));
                                await wait(1.5f);
                                await shot("duo-wave3-pulse-coin-L", 0.5f);
                                Tap(Layer().Q<Button>("pulse-buy"));
                                await shot("duo-wave3-pulse-order-sheet-L", 0.9f);
                                expect(Layer().Q("pulse-order-sheet") != null, "Buy opens the order sheet");
                                break;
                            case "biz":
                                Tap(Layer().Q<Button>("business-kind-kols_agency"));
                                await shot("duo-wave3-business-sheet-L", 0.9f);
                                break;
                            case "beef":
                                Tap(Layer().Q<Button>("beef-start"));
                                await shot("duo-wave3-beef-form-sheet-L", 0.9f);
                                expect(Layer().Q("beef-form-sheet") != null, "+ opens the challenge sheet");
                                break;
                            case "vc":
                                Tap(Layer().Q<Button>("vc-review-review-vc-1"));
                                await shot("duo-wave3-vc-offer-sheet-L", 0.9f);
                                break;
                            case "league" + "-tribes":
                                break;
                        }
                    }
                    phone.Close();
                }

                // Mini-game chrome (review/practice entry points; cancelled after the shot).
                DuoText.Set(DuoTextSize.L);
                step("Duo wave 3 games");
                async Task Game(string name, Func<CancellationToken, Task> start, Action after = null)
                {
                    using (var cts = new CancellationTokenSource())
                    {
                        var task = start(cts.Token);
                        await wait(1.2f);
                        after?.Invoke();
                        if (after != null) await wait(0.8f);
                        await shot("duo-wave3-game-" + name, 0.4f);
                        var root = UiRoot.Instance.ModalLayer.Q(className: "duo-chrome");
                        expect(root != null, name + ": game host carries the Duo chrome");
                        cts.Cancel();
                        try { await task; } catch (Exception) { }
                        await wait(0.3f);
                    }
                }
                var def = new MiniGameDef { id = "discord_mod_ban", careerId = "discord_mod", name = "Mod Desk", maxScore = 100, durationSec = 60 };
                await Game("moddesk", ct => ModDesk.PlayPracticeReview(def, ct, 7));
                await Game("raidrouter", ct => RaidRouter.PlayReview(ct, 7));
                await Game("clipcut", ct => ClipCut.PlayReview(ct, 7));
                // The Careers engine host lives in an assembly the editor tools don't reference: reach it by reflection.
                var careers = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "CTLife.Features.Careers");
                Task RunEngine(CancellationToken ct)
                {
                    var catalog = careers.GetType("CTLife.Careers.GameCatalog").GetProperty("Instance").GetValue(null);
                    var game = catalog.GetType().GetMethod("Resolve").Invoke(catalog, new object[] { "auditor_report", null, null, null });
                    var playType = careers.GetType("CTLife.Careers.MiniGamePlay");
                    var play = Activator.CreateInstance(playType);
                    playType.GetField("Game").SetValue(play, game);
                    playType.GetField("Seed").SetValue(play, 7);
                    playType.GetField("Practice").SetValue(play, true);
                    playType.GetField("Context").SetValue(play, "REVIEW · practice");
                    return (Task)careers.GetType("CTLife.Careers.MiniGameRunner").GetMethod("Run").Invoke(null, new[] { play, (object)ct });
                }
                if (careers != null)
                {
                    await Game("engine-brief", RunEngine);
                    await Game("engine-play", RunEngine, () => Tap(UiRoot.Instance.ModalLayer.Q<Button>("careers-minigame-start")));
                }

                report["reads"] = new JArray(rest.Reads.Distinct().Take(120));
                report["missing"] = new JArray(rest.Missing.Distinct().Take(80));
                report["writes"] = rest.Writes;
                expect(rest.Writes == 0, "Duo wave 3 capture made zero writes");
            }
            finally
            {
                accessToken.SetValue(session, savedToken);
                if (heldKey != null) PlayerPrefs.DeleteKey(heldKey);
                DuoText.Set(DuoTextSize.L);
                phone?.Dispose();
                mark.RemoveFromHierarchy();
                PlayerPrefs.Save();
                DuoPrefs.Store = savedPrefs;
                if (previousRest != null) Services.Register(previousRest); else Services.Unregister<IRestClient>();
                if (previousApi != null) Services.Register(previousApi);
            }
        }
    }
}
