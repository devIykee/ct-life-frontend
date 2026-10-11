#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Careers;
using CTLife.CityLife;
using CTLife.Competition;
using CTLife.Core;
using CTLife.Economy;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.SocialFeatures;
using CTLife.Ventures;
using CTLife.World;
using RetainedKey = CTLife.SocialFeatures.RetainedKey;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.App.Dev
{
    /// <summary>
    /// Development-only feature pass against the real backend, through the same lane APIs the phone apps call.
    /// Runs in the editor (Tests/Integration) and on a dev APK (launch extra `integration=1`, optional `peer=<userId>`),
    /// writes `persistentDataPath/integration-features.json` and logs one `CTLIFE [probe]` line per feature.
    /// Result: pass | fail | blocked (the backend lacks or breaks what the feature needs; item = backend-dev-feedback #) |
    /// n/a (Phase 5/6 proposal route this backend does not serve yet: 404 without a contract body, 405 or 501, which the
    /// apps show as "Not available yet").
    /// Never signs a transaction and never fakes a server response.
    /// </summary>
    public static class FeatureProbe
    {
        public sealed class Row
        {
            public string feature, result, detail, item;
            public JObject ToJson() => new JObject { ["feature"] = feature, ["result"] = result, ["detail"] = detail, ["item"] = item };
        }

        private static readonly List<Row> Rows = new List<Row>();
        private static int _cityEvents;

        public static async Task<List<Row>> Run(string peerUserId, CancellationToken ct)
        {
            Rows.Clear();
            var subs = new Subscriptions();
            subs.On<EventMessage>(_ => _cityEvents++);
            var rest = Services.Get<IRestClient>();
            var money = new MoneyApi(rest);
            var city = new CityLifeApi(rest);
            var careers = new CareersApi(rest);
            var social = new SocialApi(rest);
            string Key() => Guid.NewGuid().ToString("D");
            long cash = Services.Get<PlayerProfile>().CashCents;

            await Check("money.topup_intent", async () =>
            {
                var i = await money.CreateIntent("topup", "ramen", "USDC", Key(), ct);
                return string.IsNullOrEmpty(i.Tx) ? Blocked("52", $"intent {i.IntentId} amount={i.Amount}: no tx to sign") : Pass("intent with tx " + i.IntentId);
            }, e => e.Status >= 500 ? "87" : null); // backend #87: the route builds a PublicKey from the user UUID (500)
            string coinId = null;
            await Check("money.trade_buy", async () =>
            {
                var page = await money.Coins("new", null, ct);
                coinId = page.Coins.FirstOrDefault()?.CoinId;
                if (coinId == null) return Fail("no coins listed");
                var r = await money.Trade(new JObject { ["coinId"] = coinId, ["side"] = "buy", ["cashCents"] = 500, ["maxSlippageBps"] = 500 }, Key(), ct);
                return Pass("bought " + coinId + ": " + Json.Serialize(r));
            }, e => e.Is("SLIPPAGE_EXCEEDED") ? "76" : null);
            await Check("money.perp_open_close", async () =>
            {
                if (coinId == null) return Fail("no coin");
                var open = await money.OpenPerp(new JObject { ["coinId"] = coinId, ["side"] = "long", ["leverage"] = 1, ["marginCents"] = 500 }, Key(), ct);
                var closed = await money.ClosePerp(open.PerpId, Key(), ct);
                return Pass($"perp {open.PerpId} opened and closed");
            });
            await Check("money.ore_stake_tx", async () =>
            {
                var tx = await money.StakeTx(new JObject { ["action"] = "deposit", ["amount"] = "1000000000" }, Key(), ct);
                return Pass("stake tx built (" + (tx?.Length ?? 0) + " chars); stops at the wallet prompt, not signed");
            }, e => e.Status >= 500 ? "55" : null);
            await Check("city.needs_eat", async () =>
            {
                var r = await city.NeedsAction("eat", Key(), ct);
                return Pass("hunger=" + r.needs?.Hunger + " cash=" + r.cashCents);
            });
            await Check("city.homes", async () => { await city.Homes(ct); return Pass("GET /homes"); }, e => RouteMissing(e) ? "40" : null);
            await Check("city.rent_studio", async () =>
            {
                var r = await city.Rent("studio", Key(), ct);
                return Pass($"leased {r.HomeId} rent={r.RentCents}");
            }, e => e.Status == 404 ? "75" : null);
            await Check("city.visit", async () =>
            {
                if (string.IsNullOrEmpty(peerUserId)) return Fail("no peer user id");
                var t = await city.Visit(peerUserId, Key(), ct);
                return t == null ? Blocked("75", "visit answered without a roomId (no home room)") : Pass("room " + t.RoomId);
            }, e => e.Status == 404 ? "75" : null);
            await Check("city.shop_buy", async () =>
            {
                var rows = await city.Shop(ct);
                var pick = rows.Where(r => !r.OnchainOnly && r.PriceCents > 0 && r.PriceCents <= Math.Max(0, cash - 1000)).OrderBy(r => r.PriceCents).FirstOrDefault();
                if (pick == null) return Pass($"shop listed {rows.Count} items; none affordable");
                var inv = await city.Purchase(pick.PurchaseId, Key(), ct);
                return Pass($"bought {pick.LookId} for {pick.PriceCents}; inventory {inv.SlotByLookId.Count}");
            });
            await Check("careers.list_and_gig", async () =>
            {
                var list = await careers.Careers(ct);
                var gigs = await careers.Gigs(ct);
                var gig = gigs.FirstOrDefault();
                if (gig == null) return Pass($"{list.Count} careers, no gigs");
                var g = await careers.RunGig(gig.Id, Key(), ct);
                return Pass($"{list.Count} careers; gig {gig.Id}: {Json.Serialize(g)}");
            });
            await Check("social.dm", async () =>
            {
                if (string.IsNullOrEmpty(peerUserId)) return Fail("no peer user id");
                await social.SendDm(peerUserId, "gm from the integration probe", new RetainedKey(), ct);
                var msgs = await social.Messages(peerUserId, null, ct);
                return msgs.Count > 0 ? Pass($"sent; thread has {msgs.Count}") : Fail("sent but thread empty");
            });
            await Check("social.feed_post", async () => { var p = await social.Post("gm (integration probe)", new RetainedKey(), ct); return Pass("posted"); });
            await Check("social.space_audio", async () =>
            {
                var space = await social.CreateSpace("integration probe", new RetainedKey(), ct);
                try { await social.RtcToken("space", space.Id, ct); return Pass("space " + space.Id + " with audio token"); }
                catch (ApiException e) when (RouteMissing(e)) { return Blocked("40", "space " + space.Id + " created; no POST /rtc/token for audio"); }
            });
            await Check("social.call_ring", async () =>
            {
                if (string.IsNullOrEmpty(peerUserId)) return Fail("no peer user id");
                var call = await social.StartVideoCall(new[] { peerUserId }, Services.Get<Session>().UserId, new RetainedKey(), ct);
                // 7243073: the start reply carries only callId (#68 fixed); the city room still never sends call_invite (#54).
                return Blocked("54", "call " + call.CallId + " created; city room never sends call_invite");
            });
            await Check("world.city_events", () => Task.FromResult(_cityEvents > 0 ? Pass(_cityEvents + " city events") :
                Blocked("64", "no city `event` during the run")));
            await Check("world.ads_live", async () =>
            {
                var ads = await rest.Get<JToken>("/ads/live", ct);
                var list = ads?["ads"] as JArray;
                if (list == null || list.Count == 0) return Pass("no live bookings");
                return list.All(a => a["imageUrl"] != null) ? Pass(list.Count + " creatives") : Blocked("66", "creatives without imageUrl");
            });
            await CheckVentures(rest, ct);
            await CheckCompetition(rest, peerUserId, ct);
            await CheckVenues(ct);
            subs.Dispose();
            Save();
            return new List<Row>(Rows);
        }

        /// <summary>Phase 5 (Ventures lane): Ad Desk, Builder Hills projects, VC Row, through the lane's own API classes.</summary>
        private static async Task CheckVentures(IRestClient rest, CancellationToken ct)
        {
            var ads = new AdsApi(rest, null);
            var projects = new ProjectsApi(rest);
            string Key() => Guid.NewGuid().ToString("D");
            await Proposal("ventures.ad_slots", async () => { var l = await ads.Slots(ct); return Pass(l.Items.Count + " slots, " + l.Skipped + " skipped"); });
            await Proposal("ventures.ad_bookings", async () => { var pg = await ads.Bookings(null, ct); return Pass("GET /ads/bookings: " + pg.Items.Count); });
            string projectId = null;
            await Proposal("ventures.project_create", async () =>
            {
                var created = await projects.Create(ProjectsApi.CreateBody("memecoin", "Probe " + DateTime.UtcNow.ToString("HHmmss"), null), Key(), ct);
                projectId = created.ProjectId;
                return Pass("project " + created.ProjectId + " (" + created.Kind + ")");
            }, e => e.Is("VALIDATION_FAILED") && ((string)e.Details?["reason"] ?? "").Contains("insufficient cash") ? "77" : null);
            await Proposal("ventures.projects_mine", async () => { var pg = await projects.List("mine", null, ct); return Pass(pg.Items.Count + " of mine"); });
            await Proposal("ventures.project_detail", async () =>
            {
                if (projectId == null) return NotYet("no project to read");
                var d = await projects.Detail(projectId, ct);
                return Pass("detail " + d.Project?.ProjectId);
            });
            await Proposal("ventures.hire_offers", async () => { var pg = await projects.MyOffers(null, ct); return Pass(pg.Items.Count + " offers"); });
            await Proposal("ventures.vc_deals", async () => { var pg = await projects.VcDeals(null, ct); return Pass(pg.Items.Count + " deals"); });
        }

        /// <summary>Phase 6 (Competition lane): league, tribes, districts, beef, farm/sybil, cabal, NPC chat quota, X link.</summary>
        private static async Task CheckCompetition(IRestClient rest, string peerUserId, CancellationToken ct)
        {
            var api = new CompetitionApi(rest);
            await Proposal("competition.tribes", async () => Pass((await api.Tribes(ct)).Count + " tribes"));
            await Proposal("competition.districts", async () => Pass((await api.Districts(ct)).Count + " districts"));
            await Proposal("competition.leagues", async () => Pass((await api.Leagues(ct)).Count + " leagues"));
            await Proposal("competition.my_league", async () => { var m = await api.MyLeague(ct); return Pass("league " + Json.Serialize(m)); });
            await Proposal("competition.beef_list", async () => { var pg = await api.BeefList("mine", null, null, ct); return Pass("GET /beef: " + Json.Serialize(pg)); });
            await Proposal("competition.beef_open", async () =>
            {
                if (string.IsNullOrEmpty(peerUserId)) return Fail("no peer user id");
                var body = CompetitionApi.BeefOpenBody(100, CompStrings.BeefTopics[0], peerUserId);
                var m = await api.OpenBeef(body, Guid.NewGuid().ToString("D"), ct);
                return Pass("beef " + m.MatchId + " " + m.Status);
            });
            await Proposal("competition.beef_elo", async () => Pass("elo " + Json.Serialize(await api.Elo(null, ct))));
            await Proposal("competition.farm", async () => Pass("farm " + Json.Serialize(await api.Farm(ct))));
            await Proposal("competition.sybil_status", async () => Pass("sybil " + Json.Serialize(await api.Sybil(ct))));
            await Proposal("competition.venue_access", async () => Pass("dubai_marina " + Json.Serialize(await api.Access("dubai_marina", ct))));
            await Proposal("competition.cabal", async () => Pass("cabal " + Json.Serialize(await api.MyCabal(ct))));
            await Proposal("competition.cabal_invites", async () => Pass((await api.CabalInvites("guest", ct)).Count + " invites"));
            await Proposal("competition.llm_quota", async () => Pass("quota " + Json.Serialize(await api.Quota(ct))));
            await Proposal("competition.x_link", async () => Pass("x link " + Json.Serialize(await api.XLink(ct))));
        }

        /// <summary>A Phase 5/6 proposal call: a route this backend does not serve yet is "n/a" (the app shows "Not available yet").</summary>
        private static Task Proposal(string feature, Func<Task<(string result, string detail, string item)>> body, Func<ApiException, string> blockedBy = null) =>
            Check(feature, async () =>
            {
                try { return await body(); }
                catch (ApiException e) when (RouteMissing(e) || e.Status == 405 || e.Status == 501) { return NotYet(e.Status + " " + e.ServerMessage); }
                catch (ApiException e) when (blockedBy?.Invoke(e) is string item) { return Blocked(item, e.Code + " " + e.Status + ": " + e.Details?.ToString(Newtonsoft.Json.Formatting.None)); }
            });

        private static async Task CheckVenues(CancellationToken ct)
        {
            var ids = VenueContent.VenueIds.ToList();
            var loaded = new List<string>();
            var failed = new List<string>();
            foreach (var id in ids)
            {
                try
                {
                    float t0 = Time.realtimeSinceStartup;
                    var visit = await VenueSession.Enter(id, joinRoom: false, owner: ct);
                    loaded.Add($"{id}:{Time.realtimeSinceStartup - t0:0.0}s");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { failed.Add(id + ":" + e.GetType().Name); }
            }
            string home = null;
            try { if (VenueSession.CanEnterOwnHome()) { await VenueSession.EnterOwnHome(ct); home = Services.Get<PlayerProfile>().Me?.HomeId; } }
            catch (Exception e) { failed.Add("own_home:" + e.GetType().Name); }
            Add("venues.load_offline", failed.Count == 0 ? "pass" : "fail",
                $"{loaded.Count}/{ids.Count} venues loaded; own home {(home ?? "not entered")}; " + string.Join(" ", loaded) +
                (failed.Count > 0 ? " FAILED " + string.Join(" ", failed) : ""), null);
            try
            {
                // Each venue must get its own room (filterBy venueId, item 39; fixed in backend 7243073).
                async Task<string> StateVenue(VenueChannel ch)
                {
                    for (int i = 0; i < 40 && ch != null && string.IsNullOrEmpty(ch.VenueId); i++) await Awaitable.WaitForSecondsAsync(0.05f, ct);
                    return ch?.VenueId;
                }
                var cafe = await VenueSession.Enter("terminal_cafe", joinRoom: true, owner: ct);
                string cafeRoom = cafe.Channel?.InstanceId, cafeVenue = await StateVenue(cafe.Channel);
                var back = await VenueSession.Enter("timeline_square", joinRoom: true, owner: ct);
                string tsRoom = back.Channel?.InstanceId, tsVenue = await StateVenue(back.Channel);
                bool split = cafeVenue == "terminal_cafe" && tsVenue == "timeline_square" && !string.IsNullOrEmpty(cafeRoom) && cafeRoom != tsRoom;
                Add("venues.rooms", split ? "pass" : "fail",
                    $"terminal_cafe → room {cafeRoom} (venue {cafeVenue}); timeline_square → room {tsRoom} (venue {tsVenue})", split ? null : "39");
            }
            catch (Exception e) { Add("venues.rooms", "fail", "venue rooms: " + e.Message, null); }
        }

        private static (string result, string detail, string item) Pass(string d) => ("pass", d, null);
        private static (string result, string detail, string item) Fail(string d) => ("fail", d, null);
        private static (string result, string detail, string item) Blocked(string item, string d) => ("blocked", d, item);
        private static (string result, string detail, string item) NotYet(string d) => ("n/a", d, null);

        private static bool RouteMissing(ApiException e) => e.Status == 404 && (e.ServerMessage ?? "").Contains("No route");

        private static async Task Check(string feature, Func<Task<(string result, string detail, string item)>> body, Func<ApiException, string> blockedBy = null)
        {
            try
            {
                var (result, detail, item) = await body();
                Add(feature, result, detail, item);
            }
            catch (ApiException e)
            {
                var item = blockedBy?.Invoke(e) ?? (RouteMissing(e) ? "40" : null);
                Add(feature, item != null ? "blocked" : "fail", e.Code + " " + e.Status + ": " + e.ServerMessage, item);
            }
            catch (Exception e) when (!(e is OperationCanceledException)) { Add(feature, "fail", e.GetType().Name + ": " + e.Message, null); }
        }

        private static void Add(string feature, string result, string detail, string item)
        {
            if (detail != null && detail.Length > 300) detail = detail.Substring(0, 300);
            Rows.Add(new Row { feature = feature, result = result, detail = detail, item = item });
            Log.Info("probe", $"{result.ToUpperInvariant()} {feature}: {detail}{(item != null ? " [backend #" + item + "]" : "")}");
        }

        private static void Save()
        {
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "integration-features.json");
                File.WriteAllText(path, new JArray(Rows.Select(r => r.ToJson())).ToString());
                Log.Info("probe", "wrote " + path);
            }
            catch (Exception e) { Log.Warn("probe", "save failed: " + e.Message); }
        }
    }
}
#endif
