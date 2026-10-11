using System;
using System.Collections.Generic;
using System.Globalization;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Realtime;
using CTLife.World;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Presentation of server-driven NPCs on top of World's NpcController (which keeps navigation and base animation).
    /// Via the NpcController seam it: rewrites each replicated state into a stable "run" (see NpcBeatInterpreter),
    /// shows every fresh line once (any state, not only talk) as bubble + babble + gesture, dresses archetype props,
    /// hides NPCs that are in another venue, and runs per-archetype staging cues.
    /// </summary>
    public sealed class NpcPresenter
    {
        private sealed class Presence
        {
            public readonly NpcBeatInterpreter Interp = new NpcBeatInterpreter();
            public NpcBeat Pending;
            public NpcBeat Last;
            public NpcState LastShown;
            public bool Dressed;
            public float NextIdleEmote;
            public string Archetype;
        }

        private readonly WorldLifeData _data;
        private readonly TimelineFeed _timeline;
        private readonly CrierQueue _crier;
        private readonly Dictionary<NpcController, Presence> _npcs = new Dictionary<NpcController, Presence>();
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<NpcController> _stale = new List<NpcController>();
        private SpotResolver _spots;
        private string _venueId;
        private VenueSceneRoot _root;

        /// <summary>Raised for archetype cues that need the venue stage (clones, siren).</summary>
        public event Action<NpcController, string> Cue;

        public NpcPresenter(WorldLifeData data, TimelineFeed timeline, CrierQueue crier)
        {
            _data = data;
            _timeline = timeline;
            _crier = crier;
        }

        public string VenueId => _venueId;

        /// <summary>
        /// Venue scope. VenueSession spawns a venue's NPCs before it publishes VenueChangedSignal, so Rewrite also calls
        /// this lazily; a repeat call for the same venue and scene root keeps the existing per-NPC state.
        /// </summary>
        public void SetVenue(string venueId, VenueSceneRoot root)
        {
            if (venueId == _venueId && root == _root && _spots != null) return;
            if (venueId != _venueId) _npcs.Clear();
            _venueId = venueId;
            _root = root;
            _spots = BuildResolver(venueId, root);
        }

        private void EnsureVenue()
        {
            var session = VenueSession.Current;
            if (session == null) { if (_spots == null) _spots = BuildResolver(_venueId, null); return; }
            SetVenue(session.VenueId, session.Venue);
        }

        private SpotResolver BuildResolver(string venueId, VenueSceneRoot root)
        {
            var points = new List<SpotPoint>();
            if (root != null)
            {
                foreach (var a in root.Anchors)
                {
                    if (a == null || string.IsNullOrEmpty(a.spotId)) continue;
                    var l = root.ToLocal(a.transform.position);
                    points.Add(new SpotPoint { Id = a.spotId, X = l.x, Z = l.y, HasPosition = true });
                }
                if (root.RegistrySpots != null)
                    foreach (var kv in root.RegistrySpots.spots)
                        points.Add(new SpotPoint { Id = kv.Value.id, X = kv.Value.x, Z = kv.Value.z, HasPosition = kv.Value.hasPosition });
            }
            return new SpotResolver(venueId, points, _data.VenueIds, SpotResolver.AliasesFrom(_data.Presentation, venueId));
        }

        private Presence Get(NpcController npc)
        {
            if (!_npcs.TryGetValue(npc, out var p)) _npcs[npc] = p = new Presence();
            return p;
        }

        // ------------------------------------------------------------------ seam

        public NpcState Rewrite(NpcController npc, NpcState raw)
        {
            if (npc == null || raw == null) return raw;
            var p = Get(npc);
            if (ReferenceEquals(raw, p.LastShown)) return raw; // NpcController re-applying our own output
            EnsureVenue();
            var beat = p.Interp.Next(ToSnapshot(raw), _spots, npc.NpcId, _data.EmoteAlias);
            p.Pending = Merge(p.Pending, beat);
            p.Last = beat;
            p.Archetype = _data.Lines.Canonical(raw.archetypeId);
            var shown = ToState(beat.Shown);
            p.LastShown = shown;
            return shown;
        }

        /// <summary>Lines are presented in <see cref="Applied"/> for every state; suppress the legacy talk bubble.</summary>
        public bool Talk(NpcController npc, NpcState s) => true;

        public void Applied(NpcController npc, NpcState raw, NpcState shown)
        {
            if (npc == null) return;
            var p = Get(npc);
            var view = npc.GetComponent<AvatarView>();
            var look = _data.Archetypes.Get(p.Archetype ?? raw?.archetypeId);
            if (!p.Dressed && view != null)
            {
                p.Dressed = true;
                if (look != null) PropDresser.Dress(view, look);
                else Report("archetype_looks", raw?.archetypeId);
            }
            var beat = p.Pending;
            p.Pending = null;
            if (beat != null)
            {
                if (beat.UnknownState) Report("npc_states", beat.RawState);
                if (beat.UnknownSpot) Report("spots", (_venueId ?? "?") + "." + beat.Spot.Raw);
                if (beat.Respawn) Respawn(npc, beat);
                if (beat.FreshLine && !beat.Offstage) PresentLine(npc, view, look, p, beat);
                foreach (var cue in Behaviours.Cues(look, beat, _data.BehavioursCfg, ClonesActive?.Invoke() ?? false))
                {
                    if (cue == Behaviours.FaceTarget) FaceUser(npc, beat.Shown.TargetUserId);
                    else if (cue == Behaviours.FaceRival) FaceRival(npc, p.Archetype);
                    else if (cue != Behaviours.PostToTimeline) Cue?.Invoke(npc, cue);
                }
            }
            if (p.Last != null && p.Last.Offstage) EnforceHidden(npc);
        }

        /// <summary>VenueSession asks this before re-activating a hidden NPC (seam hook, optional).</summary>
        public bool KeepHidden(NpcController npc, NpcState raw)
        {
            if (npc == null || raw == null || !_npcs.TryGetValue(npc, out var p) || !p.Interp.Hidden) return false;
            var state = NpcBeatInterpreter.NormalizeState(raw.state, out _);
            if (state == "leave") return true;
            bool positional = state == "goto" || state == "wander" || state == "act";
            return positional && _spots != null && _spots.Resolve(raw.spotId, npc.NpcId).Kind == SpotKind.Offstage;
        }

        public Func<bool> ClonesActive;

        // ------------------------------------------------------------------ presentation

        private void PresentLine(NpcController npc, AvatarView view, ArchetypeLook look, Presence p, NpcBeat beat)
        {
            var arch = p.Archetype;
            var seed = npc.NpcId + "|" + beat.RunStartedAt.ToString("R", CultureInfo.InvariantCulture);
            var res = _data.Lines.Resolve(arch, beat.LineId, beat.Shown.LineParams, seed);
            if (res == null) { Report("npc_lines", beat.LineId); return; }
            if (res.UnknownLineId) Report("npc_line_id", beat.LineId);
            if (arch == "town_crier") _crier.ServerLine(beat.LineId, Time.unscaledTime);
            Say(npc, view, look, res.Text, beat.Shown.State == "talk" || beat.Shown.State == "idle");
            if (look != null && look.Has("post_to_timeline"))
                _timeline.Add(new TimelineEntry
                {
                    Key = "npc:" + npc.NpcId + "|" + beat.LineId + "|" + beat.RunStartedAt.ToString("R", CultureInfo.InvariantCulture),
                    Kind = "npc", Author = Handle(look), Text = res.Text, At = ServerClock.IsSynced ? ServerClock.NowMs : ServerClock.LocalNowMs,
                });
        }

        /// <summary>Bubble + babble + talk gesture. Also used for client-staged crier shouts.</summary>
        public static void Say(NpcController npc, AvatarView view, ArchetypeLook look, string text, bool gesture)
        {
            if (view == null || string.IsNullOrEmpty(text) || !view.gameObject.activeInHierarchy) return;
            float babble = 0f;
            var archetype = look?.Id ?? view.archetypeId;
            var bank = AudioManager.Instance != null ? AudioManager.Instance.BabbleBankFor(archetype) : null;
            if (bank != null)
            {
                var player = view.GetComponent<BabblePlayer>();
                if (player == null) player = view.gameObject.AddComponent<BabblePlayer>();
                player.bank = bank;
                player.npcId = npc != null ? npc.NpcId : view.npcId;
                babble = player.Say(text);
            }
            else if (npc != null) Babble.Speak(npc, view.Voice, text, archetype, view.BabblePitch);
            float seconds = Mathf.Clamp(Mathf.Max(babble + 0.8f, 1.5f + text.Length / 15f), 3.5f, 6.5f);
            Bus.Publish(new BubbleSignal { anchor = view.HeadAnchor, text = text, isNpc = true, seconds = seconds });
            var g = look != null ? look.TalkGesture : "emote_point";
            if (gesture && !string.IsNullOrEmpty(g)) view.PlayOneShot(g);
        }

        private static string Handle(ArchetypeLook look) =>
            look == null ? "npc" : look.Id.Replace("_npc", "");

        private void Respawn(NpcController npc, NpcBeat beat)
        {
            var s = beat.Shown.State;
            if (s == "goto" || s == "wander" || s == "act" || s == "leave") return; // walks in from where it hid
            var root = VenueSession.Current != null ? VenueSession.Current.Venue : null;
            if (root == null) return;
            Vector3 pos;
            if (!(beat.Spot.Kind == SpotKind.Local && root.TryGetSpot(beat.Spot.SpotId, out pos, out _))) pos = root.EntrancePosition;
            var agent = npc.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && NavMesh.SamplePosition(pos, out var hit, 1f, agent.areaMask))
                agent.Warp(hit.position);
            else npc.transform.position = pos;
        }

        private static void EnforceHidden(NpcController npc)
        {
            if (!npc.gameObject.activeSelf) return;
            var agent = npc.GetComponent<NavMeshAgent>();
            bool walking = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && (agent.pathPending || agent.hasPath && agent.remainingDistance > 0.3f);
            if (!walking) npc.gameObject.SetActive(false);
        }

        private static void FaceUser(NpcController npc, string userId)
        {
            var target = VenueSession.Current != null ? VenueSession.Current.AvatarForUser(userId) : null;
            if (target != null) Face(npc.transform, target.transform.position);
        }

        private void FaceRival(NpcController npc, string archetype)
        {
            var session = VenueSession.Current;
            if (session == null) return;
            NpcController best = null;
            float bestD = float.MaxValue;
            foreach (var other in session.Npcs.Values)
            {
                if (other == null || other == npc || !other.gameObject.activeInHierarchy) continue;
                if (_data.Lines.Canonical(other.State?.archetypeId) != archetype) continue;
                float d = (other.transform.position - npc.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = other; }
            }
            if (best != null) { Face(npc.transform, best.transform.position); Face(best.transform, npc.transform.position); }
        }

        private static void Face(Transform t, Vector3 at)
        {
            var dir = at - t.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>Per-frame: archetype idle emotes for idle NPCs; prune destroyed controllers.</summary>
        public void Tick(float now)
        {
            _stale.Clear();
            foreach (var kv in _npcs)
            {
                var npc = kv.Key;
                if (npc == null) { _stale.Add(npc); continue; }
                var p = kv.Value;
                if (npc.State == null || p.Last == null || p.Last.Shown.State != "idle" || !npc.gameObject.activeInHierarchy) continue;
                if (p.NextIdleEmote <= 0f) { p.NextIdleEmote = now + UnityEngine.Random.Range(8f, 16f); continue; }
                if (now < p.NextIdleEmote) continue;
                p.NextIdleEmote = now + UnityEngine.Random.Range(9f, 18f);
                var look = _data.Archetypes.Get(p.Archetype);
                if (look == null || look.IdleEmotes.Count == 0 || UnityEngine.Random.value > 0.4f) continue;
                var view = npc.GetComponent<AvatarView>();
                if (view != null) view.PlayOneShot(look.IdleEmotes[UnityEngine.Random.Range(0, look.IdleEmotes.Count)]);
            }
            foreach (var s in _stale) _npcs.Remove(s);
        }

        public IEnumerable<CrowdMember> Crowd()
        {
            var session = VenueSession.Current;
            if (session == null) yield break;
            foreach (var kv in session.Npcs)
            {
                var npc = kv.Value;
                if (npc == null) continue;
                _npcs.TryGetValue(npc, out var p);
                yield return new CrowdMember
                {
                    NpcId = kv.Key,
                    ArchetypeId = _data.Lines.Canonical(npc.State?.archetypeId),
                    State = p?.Last?.Shown.State ?? NpcBeatInterpreter.NormalizeState(npc.State?.state, out _),
                    Hidden = !npc.gameObject.activeInHierarchy || (p != null && p.Interp.Hidden),
                };
            }
        }

        private void Report(string registry, string id)
        {
            if (string.IsNullOrEmpty(id) || !_reported.Add(registry + ":" + id)) return;
            Log.Warn("world-life", $"Contract gap: unknown {registry} id '{id}' from the server (presented with a fallback).");
        }

        // ------------------------------------------------------------------ conversions

        public static NpcSnapshot ToSnapshot(NpcState s)
        {
            var snap = new NpcSnapshot
            {
                ArchetypeId = s.archetypeId ?? "", State = s.state ?? "", SpotId = s.spotId ?? "", AnimId = s.animId ?? "",
                TargetUserId = s.targetUserId ?? "", LineId = s.lineId ?? "", EmoteId = s.emoteId ?? "", StartedAt = s.startedAt,
            };
            if (s.lineParams != null)
                foreach (var kv in s.lineParams)
                    if (!string.IsNullOrEmpty(kv.Key)) snap.LineParams[kv.Key] = kv.Value;
            return snap;
        }

        public static NpcState ToState(NpcSnapshot s)
        {
            var n = new NpcState
            {
                archetypeId = s.ArchetypeId, state = s.State, spotId = s.SpotId, animId = s.AnimId, targetUserId = s.TargetUserId,
                lineId = s.LineId, emoteId = s.EmoteId, startedAt = s.StartedAt,
            };
            var map = new Colyseus.Schema.MapSchema<string>();
            if (s.LineParams != null) foreach (var kv in s.LineParams) map[kv.Key] = kv.Value;
            n.lineParams = map;
            return n;
        }

        private static NpcBeat Merge(NpcBeat older, NpcBeat newer)
        {
            if (older == null) return newer;
            newer.FreshLine |= older.FreshLine && older.LineId == newer.LineId;
            newer.FreshEmote |= older.FreshEmote;
            newer.FreshAnim |= older.FreshAnim;
            newer.Respawn |= older.Respawn;
            newer.NewRun |= older.NewRun;
            return newer;
        }
    }
}
