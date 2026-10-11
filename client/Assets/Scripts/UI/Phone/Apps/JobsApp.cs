using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Careers and shifts (feature #9): apply, start a shift (`POST /v1/shifts`), play its mini-games, wait for
    /// `endsAt`, then `POST /v1/shifts/{id}/complete` with scores (the server caps them at maxScore).
    /// </summary>
    public sealed class JobsApp : PhoneApp
    {
        public override string Id => "jobs";
        public override string Title => "Jobs";
        public override string Color => "#0fb9b1";

        private VisualElement _list;

        public override VisualElement Open()
        {
            _list = El.Div("ct-col");
            Load();
            return AppUi.Page(El.Text("Careers", "ct-h1"), _list);
        }

        private async void Load()
        {
            _list.Clear();
            _list.Add(El.Text("Loading…", "ct-small"));
            try
            {
                var careers = await Services.Get<Api>().Careers();
                _list.Clear();
                foreach (var c in careers)
                {
                    var info = CareerInfo.From(c.Id);
                    if (info.phase > Phone.CurrentPhase) continue;
                    var lvl = info.Level(Math.Max(1, c.Level));
                    var row = El.Div("ct-card",
                        El.Text((c.Name ?? info.name ?? c.Id) + (c.Active ? $" · L{c.Level}" : ""), "ct-h2"),
                        El.Text(lvl != null ? $"{lvl.Str("title")} · {Money.Cash(lvl.Long("payCents"))} per shift · {lvl.Int("shiftSec")}s" : $"XP {c.Xp}", "ct-small"));
                    var career = c;
                    if (c.Active) row.Add(El.Btn("Work a shift", () => RunShift(career.Id), "ct-btn ct-btn-primary"));
                    else row.Add(El.Btn("Apply", () => Apply(career.Id), "ct-btn"));
                    _list.Add(row);
                }
            }
            catch (Exception e)
            {
                _list.Clear();
                _list.Add(El.Text(AppUi.Err(e), "ct-error"));
            }
        }

        /// <summary>Phase and level table for a career from config `careers` (GET /careers only has id/name/level/xp/active).</summary>
        private sealed class CareerInfo
        {
            public string name;
            public int phase = 1;
            public Newtonsoft.Json.Linq.JArray levels;

            public static CareerInfo From(string careerId)
            {
                var info = new CareerInfo();
                var cfg = Services.Get<ConfigStore>().Config("careers");
                var list = cfg?["careers"] as Newtonsoft.Json.Linq.JArray;
                Newtonsoft.Json.Linq.JToken c = null;
                if (list != null) c = list.FirstOrDefault(x => x.Str("id") == careerId);
                else if (cfg?[careerId] != null) c = cfg[careerId];
                if (c == null) return info;
                info.name = c.Str("name");
                info.phase = c.Int("phase", 1);
                info.levels = c["levels"] as Newtonsoft.Json.Linq.JArray;
                return info;
            }

            public Newtonsoft.Json.Linq.JToken Level(int level) => levels?.FirstOrDefault(l => l.Int("level") == level);
        }

        private async void Apply(string careerId)
        {
            try
            {
                var job = await Services.Get<Api>().ApplyCareer(careerId);
                var title = CareerInfo.From(job.CareerId).Level(Math.Max(1, job.Level))?.Str("title") ?? job.CareerId;
                Bus.Publish(ToastSignal.Text($"Hired: {title}", ToastKind.Good));
                Load();
            }
            catch (Exception e)
            {
                Bus.Publish(ToastSignal.Text(AppUi.Err(e), ToastKind.Bad));
            }
        }

        private async void RunShift(string careerId)
        {
            var api = Services.Get<Api>();
            var reg = Services.Get<ConfigStore>().Registries;
            Shift shift;
            try
            {
                shift = await api.StartShift(careerId);
            }
            catch (Exception e)
            {
                Bus.Publish(ToastSignal.Text(AppUi.Err(e), ToastKind.Bad));
                return;
            }

            var results = new List<PostShiftsShiftIdCompleteRequestResultsInner>();
            foreach (var mgId in shift.MiniGameIds ?? new List<string>())
            {
                reg.MiniGames.TryGetValue(mgId, out var def);
                def ??= new MiniGameDef { id = mgId, name = mgId, maxScore = 1000, durationSec = 30, careerId = careerId };
                int score = await MiniGameHost.Play(def);
                results.Add(new PostShiftsShiftIdCompleteRequestResultsInner(mgId, score));
            }

            var wait = (shift.EndsAt.ToUniversalTime() - DateTime.UtcNow).TotalSeconds;
            if (wait > 0) await Awaitable.WaitForSecondsAsync((float)Math.Min(wait + 0.5, 600));
            try
            {
                var r = await api.CompleteShift(shift.ShiftId, results);
                Bus.Publish(ToastSignal.Id("toast_shift_paid", new Dictionary<string, string> { ["payCents"] = r.PayCents.ToString(), ["xp"] = r.Xp.ToString() }, ToastKind.Good));
                if (r.Promoted) Bus.Publish(new SfxSignal("sfx_rank_up"));
                await Services.Get<PlayerProfile>().Refresh();
            }
            catch (Exception e)
            {
                Bus.Publish(ToastSignal.Text(AppUi.Err(e), ToastKind.Bad));
            }
        }
    }

    /// <summary>
    /// Full-screen mini-game runner. The base mechanic is "tap the right ones": targets pop up, good taps score,
    /// wrong taps cost. Each mini_games.json id gets its own theme here; the 9 Phase-1 games add bespoke rules on
    /// top of this host.
    /// </summary>
    public static class MiniGameHost
    {
        private sealed class Theme
        {
            public string good, bad, prompt;
        }

        private static Theme ThemeFor(MiniGameDef d)
        {
            var id = d.id ?? "";
            if (id.Contains("drainer") || id.Contains("mod")) return new Theme { good = "DM: \"support here, send seed\"", bad = "DM: \"gm ser\"", prompt = "Ban the drainers. Spare the real ones." };
            if (id.Contains("verify") || id.Contains("human")) return new Theme { good = "human", bad = "bot", prompt = "Verify humans only." };
            if (id.Contains("thread") || id.Contains("write")) return new Theme { good = "1/ alpha", bad = "ratio'd take", prompt = "Pick the bangers." };
            if (id.Contains("audit") || id.Contains("bug")) return new Theme { good = "reentrancy", bad = "false alarm", prompt = "Flag real bugs." };
            return new Theme { good = "✓", bad = "✗", prompt = d.name };
        }

        public static Task<int> Play(MiniGameDef def)
        {
            var tcs = new TaskCompletionSource<int>();
            var theme = ThemeFor(def);
            var arena = El.Div("ct-mg-arena");
            var scoreLabel = El.Text("0", "ct-mg-score");
            var timeLabel = El.Text(def.durationSec + "s", "ct-mg-time");
            var root = El.Div("ct-modal-backdrop ct-mg",
                El.Div("ct-row", El.Text(def.name, "ct-h2"), El.Div("ct-grow"), scoreLabel, timeLabel),
                El.Text(theme.prompt, "ct-body"),
                arena);
            root.pickingMode = PickingMode.Position;
            UiRoot.Instance.ModalLayer.Add(root);

            int good = 0, bad = 0, spawned = 0;
            float start = Time.unscaledTime, nextSpawn = start + 0.4f;
            var rng = new System.Random();
            var live = new List<(VisualElement el, float until)>();
            Action tick = null;
            tick = () =>
            {
                float t = Time.unscaledTime - start;
                timeLabel.text = Mathf.CeilToInt(Mathf.Max(0, def.durationSec - t)) + "s";
                if (Time.unscaledTime >= nextSpawn && t < def.durationSec)
                {
                    nextSpawn = Time.unscaledTime + Mathf.Lerp(0.9f, 0.45f, t / def.durationSec);
                    bool isGood = rng.NextDouble() < 0.6;
                    spawned++;
                    VisualElement el = null;
                    el = El.Btn(isGood ? theme.good : theme.bad, () =>
                    {
                        if (isGood) good++; else bad++;
                        scoreLabel.text = Score(def, good, bad, spawned).ToString();
                        el.RemoveFromHierarchy();
                    }, isGood ? "ct-mg-target ct-mg-good" : "ct-mg-target ct-mg-bad", isGood ? "sfx_ui_success" : "sfx_ui_error");
                    el.style.left = Length.Percent((float)rng.NextDouble() * 70f);
                    el.style.top = Length.Percent((float)rng.NextDouble() * 80f);
                    arena.Add(el);
                    live.Add((el, Time.unscaledTime + 1.6f));
                }
                for (int i = live.Count - 1; i >= 0; i--)
                    if (Time.unscaledTime > live[i].until) { live[i].el.RemoveFromHierarchy(); live.RemoveAt(i); }
                if (t >= def.durationSec)
                {
                    UiRoot.Instance.RemoveUpdater(tick);
                    root.RemoveFromHierarchy();
                    tcs.TrySetResult(Score(def, good, bad, spawned));
                }
            };
            UiRoot.Instance.AddUpdater(tick);
            return tcs.Task;
        }

        private static int Score(MiniGameDef d, int good, int bad, int spawned)
        {
            int expectedGood = Mathf.Max(1, Mathf.RoundToInt(spawned * 0.6f));
            float r = Mathf.Clamp01((good - bad * 0.5f) / expectedGood);
            return Mathf.RoundToInt(r * d.maxScore);
        }
    }
}
