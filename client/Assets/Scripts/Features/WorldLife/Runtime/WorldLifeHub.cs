using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.World;
using UnityEngine;

namespace CTLife.WorldLife
{
    /// <summary>
    /// World life lane composition root. Created once by FlowController (requests.md hook 3) and disposed with the
    /// other overlays. Listens to the Bus only (city `event`, Market Mood, venue changes, toasts), binds the NPC
    /// presentation seam, and owns the venue stage and live surfaces. Never sends anything to the server.
    /// </summary>
    public sealed class WorldLifeHub : IDisposable
    {
        private readonly WorldLifeData _data = new WorldLifeData();
        private readonly TimelineFeed _timeline = new TimelineFeed();
        private readonly CrierQueue _crier = new CrierQueue();
        private readonly MoodModel _mood = new MoodModel();
        private readonly NpcPresenter _presenter;
        private readonly MomentStage _stage;
        private readonly LiveSurfaces _surfaces;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly ConfigStore _config;
        private readonly HashSet<string> _seenEvents = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _seenOrder = new Queue<string>();
        private string _venueId;
        private bool _disposed;

        public TimelineFeed Timeline => _timeline;
        public NpcPresenter Presenter => _presenter;

        public WorldLifeHub()
        {
            _data.LoadLocal();
            if (Services.TryGet<ConfigStore>(out var cfg))
            {
                _config = cfg;
                cfg.Changed += OnConfigChanged;
            }
            _data.BindConfig(_config);
            _presenter = new NpcPresenter(_data, _timeline, _crier);
            _stage = MomentStage.Create();
            _presenter.ClonesActive = () => _stage != null && _stage.ClonesActive;
            _presenter.Cue += OnCue;
            NpcHookBinder.Bind(_presenter);
            Services.TryGet<IRestClient>(out var rest);
            _surfaces = new LiveSurfaces(_data, _timeline, rest != null ? new WorldLifeApi(rest) : null, _stage.transform);

            _subs.On<EventMessage>(OnEvent);
            _subs.On<MoodSignal>(OnMood);
            _subs.On<VenueChangedSignal>(OnVenue);
            _subs.On<ToastSignal>(OnToast);
            Runner.Ticked += Tick;

            // Already inside a venue (hub created late, e.g. after a reload).
            var session = VenueSession.Current;
            if (session != null && session.IsActive) OnVenue(new VenueChangedSignal { venueId = session.VenueId });
        }

        private void OnConfigChanged()
        {
            if (_disposed) return;
            try { _data.BindConfig(_config); }
            catch (Exception e) { Log.Exception("world-life", e); }
        }

        // ------------------------------------------------------------------ venue

        private void OnVenue(VenueChangedSignal s)
        {
            if (_disposed) return;
            _venueId = s.venueId;
            _stage.Clear();
            _crier.Clear();
            var session = VenueSession.Current;
            var root = session != null && session.VenueId == s.venueId ? session.Venue : null;
            _presenter.SetVenue(s.venueId, root);
            if (s.venueId == null || root == null) _surfaces.Exit();
            else _surfaces.Enter(s.venueId, root);
            if (WorldLifeHooks.LastMood is MarketMoodSignal m) WorldLifeHooks.PublishMood(m);
        }

        // ------------------------------------------------------------------ city events

        private void OnEvent(EventMessage m)
        {
            if (_disposed || m == null) return;
            var e = new CityEvent { EventId = m.eventId, At = m.at, Params = m.@params != null ? new Dictionary<string, string>(m.@params) : new Dictionary<string, string>() };
            var key = e.Key;
            if (!_seenEvents.Add(key)) return; // reconnect replays
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > 64) _seenEvents.Remove(_seenOrder.Dequeue());

            var crowd = _presenter.Crowd().ToList();
            var plan = EventMoments.Plan(e, _venueId, _data.Moments, _data.Lines, crowd, ServerClock.NowMs, ServerClock.IsSynced);
            if (plan == null)
            {
                if (_data.Moments?[m.eventId ?? ""] == null) Log.UnknownId("events", m.eventId);
                return; // malformed or implausible timestamp
            }
            if (!string.IsNullOrEmpty(plan.TimelineText))
                _timeline.Add(new TimelineEntry { Key = "evt:" + key, Kind = "event", Text = plan.TimelineText, At = e.At });
            if (e.EventId == "evt_mood_change")
                foreach (var k in new[] { "moodBps", "bps", "mood" })
                    if (e.Params.TryGetValue(k, out var v) && int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var bps))
                    { ApplyMood(bps); break; }
            if (!plan.Live || _venueId == null) return;

            float now = Time.unscaledTime;
            if (!string.IsNullOrEmpty(plan.CrierText)) _crier.Enqueue(key, e.EventId, plan.CrierText, now);
            if (plan.Crowd.Count > 0) _stage.QueueCrowd(plan.Crowd);
            var root = VenueSession.Current != null ? VenueSession.Current.Venue : null;
            var at = _stage.FxPosition(plan.VenueFx, root);
            if (plan.VenueFx != null)
            {
                _stage.Play(plan.VenueFx, at, root);
                var cfg = _data.Moments?[e.EventId]?["venueSfx"]?[plan.VenueFx];
                WorldSfx.PlayAt(plan.MomentId, plan.VenueFx, WireText.Strs(cfg), at, 6f);
            }
            Bus.Publish(new WorldMomentSignal
            {
                MomentId = plan.MomentId, EventId = plan.EventId, VenueId = _venueId, StingerSfxId = plan.StingerSfxId,
                VenueFx = plan.VenueFx, Position = at, CrierText = plan.CrierText, At = e.At,
            });
        }

        // ------------------------------------------------------------------ mood

        private void OnMood(MoodSignal s)
        {
            if (_disposed) return;
            ApplyMood(s.bps);
        }

        private void ApplyMood(int bps)
        {
            bool first = !_mood.HasValue;
            bool changed = _mood.Update(bps);
            if (!changed && !first) return;
            var rgb = _mood.Tint(out var strength);
            WorldLifeHooks.PublishMood(new MarketMoodSignal
            {
                Band = _mood.Band, Bps = _mood.Bps, Normalized = _mood.Normalized, Tint = new Color(rgb.R, rgb.G, rgb.B, 1f), Strength = strength,
            });
        }

        // ------------------------------------------------------------------ Giveaway Guy drain gag

        private void OnToast(ToastSignal t)
        {
            if (_disposed || (t.textId != "toast_drained" && t.textId != "ui.toast.drained")) return;
            var session = VenueSession.Current;
            if (session == null) return;
            var guy = FindNpc("giveaway_guy");
            if (guy == null) return;
            var view = guy.GetComponent<AvatarView>();
            var look = _data.Archetypes.Get("giveaway_guy");
            var line = _data.Lines.Resolve("giveaway_guy", "giveaway_drained", null, guy.NpcId + "|" + Time.frameCount);
            NpcPresenter.Say(guy, view, look, line?.Text, false);
            if (view != null) view.PlayOneShot("emote_laugh");
            if (session.LocalPlayer != null) _stage.CoinBurst(session.LocalPlayer.transform.position, guy.transform.position);
        }

        private NpcController FindNpc(string archetype)
        {
            var session = VenueSession.Current;
            if (session == null) return null;
            foreach (var npc in session.Npcs.Values)
                if (npc != null && npc.gameObject.activeInHierarchy && _data.Lines.Canonical(npc.State?.archetypeId) == archetype) return npc;
            return null;
        }

        // ------------------------------------------------------------------ archetype cues

        private void OnCue(NpcController npc, string cue)
        {
            if (_disposed || npc == null) return;
            switch (cue)
            {
                case Behaviours.SplitClones:
                {
                    var cfg = _data.BehavioursCfg?["sybil_split"];
                    _stage.SplitClones(npc, (int)WireText.Num(cfg, "clones", 10), (float)WireText.Num(cfg, "maxSeconds", 90));
                    break;
                }
                case Behaviours.ClearClones:
                    _stage.ClearClones(true);
                    break;
                case Behaviours.Siren:
                    _stage.Play("siren", npc.transform.position, VenueSession.Current != null ? VenueSession.Current.Venue : null);
                    WorldSfx.PlayAt("liquidation", "siren", WireText.Strs(_data.Moments?["evt_liquidation"]?["venueSfx"]?["siren"]), npc.transform.position, 6f);
                    break;
            }
        }

        // ------------------------------------------------------------------ frame

        private void Tick()
        {
            if (_disposed) return;
            float now = Time.unscaledTime;
            try
            {
                _presenter.Tick(now);
                var text = _crier.Due(now, out _);
                if (text != null)
                {
                    var crier = FindNpc("town_crier");
                    if (crier != null) NpcPresenter.Say(crier, crier.GetComponent<AvatarView>(), _data.Archetypes.Get("town_crier"), text, true);
                }
                _surfaces.Tick(now);
            }
            catch (Exception e) { Log.Exception("world-life", e); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Runner.Ticked -= Tick;
            _subs.Dispose();
            if (_config != null) _config.Changed -= OnConfigChanged;
            NpcHookBinder.Unbind();
            _surfaces.Dispose();
            if (_stage != null) UnityEngine.Object.Destroy(_stage.gameObject);
        }
    }
}
