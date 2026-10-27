#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Dev builds only, launch extra <c>timelineRoute=1</c>: the world-feed Timeline device check. Travels to Launchpad Tower
    /// (The Trenches), marks "make events" (the host launches, posts and rugs a coin on the local backend), waits for the
    /// realtime cards, then opens the Timeline: For you, City (+ the Rugs chip), a coin card's Open (Pulse), and closes the
    /// phone for the HUD toast. Logs "CTLIFE [timeline-ui] ..." with "shot ..." marks.
    /// </summary>
    public static class TimelineRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [timeline-ui] " + what);

        public static async Task Run(Action<string> openApp, Action closePhone, CancellationToken ct)
        {
            try
            {
                float deadline = Time.realtimeSinceStartup + 300f;
                while (!ct.IsCancellationRequested && (VenueSession.Current == null || !VenueSession.Current.IsActive || !VenueSession.Current.IsIsland) && Time.realtimeSinceStartup < deadline)
                    await Awaitable.WaitForSecondsAsync(0.5f, ct);
                await Awaitable.WaitForSecondsAsync(4f, ct);
                VenueLinks.Open("venue:launchpad_tower");
                await Awaitable.WaitForSecondsAsync(5f, ct);
                var s = VenueSession.Current;
                Mark("in district " + (s?.Island?.DistrictAt(s.LocalPlayer.transform.position) ?? "?"));
                var hub = SocialHub.Current;
                if (hub == null) { Mark("FAIL no social hub"); return; }
                int before = hub.Timeline.Events.Count;
                Mark("make events");
                for (int i = 0; i < 120 && hub.Timeline.Events.Count(e => e.Kind == EventKinds.Rug) == 0; i++) await Awaitable.WaitForSecondsAsync(0.5f, ct);
                Mark($"events {hub.Timeline.Events.Count} (was {before}): {string.Join(",", hub.Timeline.Events.Take(6).Select(e => e.Kind))}");
                await Awaitable.WaitForSecondsAsync(1.0f, ct);
                Mark("shot toast");
                await Awaitable.WaitForSecondsAsync(3f, ct);

                openApp("timeline");
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                hub.Timeline.SetScope("global");
                await Awaitable.WaitForSecondsAsync(3.5f, ct);
                var rows = hub.Timeline.Rows;
                Mark($"for you rows {rows.Count}: {string.Join(",", rows.Take(8).Select(r => r.Kind))}");
                Mark("shot foryou");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                var root = UiRoot.Instance.Root;
                var scroll = root.Q<ScrollView>(className: "duo-scroll");
                var card = root.Q("timeline-coin-card");
                if (scroll != null && card != null) { scroll.ScrollTo(card); await Awaitable.WaitForSecondsAsync(1.2f, ct); Mark("shot coincard"); await Awaitable.WaitForSecondsAsync(3f, ct); }
                var sleuth = root.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("timeline-sleuth-")).First();
                if (scroll != null && sleuth != null) { scroll.ScrollTo(sleuth); await Awaitable.WaitForSecondsAsync(1.2f, ct); Mark("shot sleuth"); await Awaitable.WaitForSecondsAsync(3f, ct); }

                hub.Timeline.SetScope("city");
                await Awaitable.WaitForSecondsAsync(2f, ct);
                Mark($"city rows {hub.Timeline.Rows.Count}");
                Mark("shot city");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                hub.Timeline.SetCityFilter(EventKinds.Rug);
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                Mark($"city rugs {hub.Timeline.Rows.Count}");
                Mark("shot cityrugs");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                hub.Timeline.SetCityFilter(null);
                hub.Timeline.SetScope("global");
                await Awaitable.WaitForSecondsAsync(2.5f, ct);
                var open = UiRoot.Instance.Root.Q<Button>("timeline-open-coin");
                if (open != null)
                {
                    using (var e = NavigationSubmitEvent.GetPooled()) { e.target = open; open.SendEvent(e); }
                    await Awaitable.WaitForSecondsAsync(3f, ct);
                    Mark("shot pulse");
                    await Awaitable.WaitForSecondsAsync(3f, ct);
                }
                else Mark("no coin card Open button");
                closePhone?.Invoke();
                Mark("done");
            }
            catch (OperationCanceledException) { Mark("cancelled"); }
            catch (Exception e) { Mark("FAIL " + e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
        }
    }
}
#endif
