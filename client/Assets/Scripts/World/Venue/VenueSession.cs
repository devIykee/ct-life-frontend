using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// One visit to one venue: loads the diorama, joins the `venue` room, and keeps avatars in sync with
    /// VenueState (players by sessionId, NPCs by npcId). Owns the local player controller, chat bubbles,
    /// `rejected` handling and the venue's proximity-voice room.
    /// </summary>
    public sealed class VenueSession : MonoBehaviour
    {
        public static VenueSession Current { get; private set; }

        public string VenueId { get; private set; }
        public VenueSceneRoot Venue { get; private set; }
        public VenueChannel Channel { get; private set; }
        public LocalPlayerController LocalPlayer { get; private set; }

        private readonly Dictionary<string, RemotePlayer> _players = new Dictionary<string, RemotePlayer>();
        private readonly Dictionary<string, NpcController> _npcs = new Dictionary<string, NpcController>();
        private readonly Dictionary<string, AvatarView> _byUserId = new Dictionary<string, AvatarView>();
        private Transform _avatarRoot;
        private VenueLoader _loader;
        private Subscriptions _subs;

        public IReadOnlyDictionary<string, RemotePlayer> Players => _players;
        public IReadOnlyDictionary<string, NpcController> Npcs => _npcs;

        public static async Task<VenueSession> Enter(string venueId, bool joinRoom = true, bool voice = false)
        {
            if (Current != null) await Current.Exit();
            var go = new GameObject("VenueSession_" + venueId);
            DontDestroyOnLoad(go);
            var s = go.AddComponent<VenueSession>();
            Current = s;
            await s.Begin(venueId, joinRoom, voice);
            return s;
        }

        private async Task Begin(string venueId, bool joinRoom, bool voice)
        {
            VenueId = venueId;
            _loader = new VenueLoader();
            Venue = await _loader.Load(venueId);
            _avatarRoot = new GameObject("Avatars").transform;
            _avatarRoot.SetParent(transform, false);

            var rig = CameraRig.Ensure();
            rig.Bounds = Venue.cameraBounds;
            AvatarLod.Ensure();

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.SetAmbience(venueId);
                AudioManager.Instance.Music.SetDistrict(Venue.districtId);
            }

            _subs = new Subscriptions();
            WorldInput.GroundTapped += OnGroundTapped;
            WorldInput.SpotTapped += OnSpotTapped;

            if (joinRoom)
            {
                var rt = Services.Get<IRealtimeService>();
                Channel = await rt.JoinVenue(venueId);
                Channel.PlayerAdded += OnPlayerAdded;
                Channel.PlayerChanged += OnPlayerChanged;
                Channel.PlayerRemoved += OnPlayerRemoved;
                Channel.NpcAdded += OnNpcAdded;
                Channel.NpcChanged += OnNpcChanged;
                Channel.NpcRemoved += OnNpcRemoved;
                Channel.Said += OnSaid;
                Channel.Rejected += OnRejected;
                // State that arrived before the handlers were attached.
                if (Channel.State != null)
                {
                    foreach (var kv in Channel.State.players) if (!_players.ContainsKey(kv.Key)) OnPlayerAdded(kv.Key, kv.Value);
                    foreach (var kv in Channel.State.npcs) if (!_npcs.ContainsKey(kv.Key)) OnNpcAdded(kv.Key, kv.Value);
                }
                if (LocalPlayer == null) SpawnLocal(null);
                Bus.Publish(new VenueChangedSignal { venueId = venueId, instanceId = Channel.InstanceId });
                if (voice) _ = ConnectVoice();
            }
            else
            {
                SpawnLocal(null);
                Bus.Publish(new VenueChangedSignal { venueId = venueId });
            }
        }

        public async Task Exit()
        {
            WorldInput.GroundTapped -= OnGroundTapped;
            WorldInput.SpotTapped -= OnSpotTapped;
            _subs?.Dispose();
            if (Services.TryGet<VoiceService>(out var v)) await v.Disconnect();
            if (Channel != null && Services.TryGet<IRealtimeService>(out var rt)) await rt.LeaveVenue();
            Channel = null;
            foreach (var p in _players.Values) if (p != null) Destroy(p.gameObject);
            foreach (var n in _npcs.Values) if (n != null) Destroy(n.gameObject);
            _players.Clear();
            _npcs.Clear();
            _byUserId.Clear();
            if (LocalPlayer != null) Destroy(LocalPlayer.gameObject);
            if (_loader != null) await _loader.Unload();
            if (Current == this) Current = null;
            Bus.Publish(new VenueChangedSignal { venueId = null });
            Destroy(gameObject);
        }

        // ---------------------------------------------------------------- players

        private void SpawnLocal(PlayerState state)
        {
            var profile = Services.TryGet<PlayerProfile>(out var pp) ? pp : null;
            var look = state != null ? ToArray(state.look) : profile?.LookArray();
            var name = state?.name ?? profile?.Alias ?? "you";
            var view = AvatarFactory.Create(name, look, false, null, _avatarRoot);
            view.userId = state?.userId ?? Services.Get<Session>().UserId;
            view.sessionId = Channel?.SessionId;
            view.transform.position = state != null ? Venue.ToWorld((float)state.x, (float)state.z) : Venue.EntrancePosition;
            LocalPlayer = view.gameObject.AddComponent<LocalPlayerController>();
            LocalPlayer.Init(Venue, Channel);
            if (!string.IsNullOrEmpty(view.userId)) _byUserId[view.userId] = view;
            var rig = CameraRig.Ensure();
            rig.Target = view.transform;
            rig.SnapToTarget();
        }

        private void OnPlayerAdded(string sessionId, PlayerState p)
        {
            if (Channel != null && sessionId == Channel.SessionId)
            {
                if (LocalPlayer == null) SpawnLocal(p);
                else
                {
                    var v = LocalPlayer.GetComponent<AvatarView>();
                    v.displayName = p.name;
                    v.userId = p.userId;
                    v.isSeeker = p.isSeeker;
                    v.minerTier = p.minerTier;
                    _byUserId[p.userId] = v;
                }
                return;
            }
            if (_players.ContainsKey(sessionId)) return;
            var view = AvatarFactory.Create(p.name, ToArray(p.look), false, null, _avatarRoot);
            view.userId = p.userId;
            view.sessionId = sessionId;
            var rp = view.gameObject.AddComponent<RemotePlayer>();
            rp.Init(Venue, p);
            _players[sessionId] = rp;
            if (!string.IsNullOrEmpty(p.userId)) _byUserId[p.userId] = view;
        }

        private void OnPlayerChanged(string sessionId, PlayerState p)
        {
            if (Channel != null && sessionId == Channel.SessionId)
            {
                var v = LocalPlayer != null ? LocalPlayer.GetComponent<AvatarView>() : null;
                if (v != null)
                {
                    v.SetTalking(p.talking);
                    if (!string.IsNullOrEmpty(p.spotId) && LocalPlayer.SeatedSpot != p.spotId) LocalPlayer.OnServerSeat(p.spotId, p.anim);
                    // The local emote already played on tap (EmotePicker); the server echo is ignored here.
                }
                return;
            }
            if (_players.TryGetValue(sessionId, out var rp)) rp.Apply(p);
        }

        private void OnPlayerRemoved(string sessionId, PlayerState p)
        {
            if (_players.TryGetValue(sessionId, out var rp))
            {
                _players.Remove(sessionId);
                if (p != null && !string.IsNullOrEmpty(p.userId)) _byUserId.Remove(p.userId);
                if (rp != null) Destroy(rp.gameObject);
            }
        }

        // ---------------------------------------------------------------- NPCs

        private void OnNpcAdded(string npcId, NpcState n)
        {
            if (_npcs.ContainsKey(npcId)) return;
            var def = Services.Get<ConfigStore>().Registries.Archetype(n.archetypeId);
            var view = AvatarFactory.Create(def?.name ?? n.archetypeId, null, true, n.archetypeId, _avatarRoot);
            var npc = view.gameObject.AddComponent<NpcController>();
            npc.Init(npcId, n, Venue, FindUser);
            _npcs[npcId] = npc;
        }

        private void OnNpcChanged(string npcId, NpcState n)
        {
            if (_npcs.TryGetValue(npcId, out var npc) && npc != null)
            {
                if (!npc.gameObject.activeSelf && n.state != "leave") npc.gameObject.SetActive(true);
                npc.Apply(n);
            }
        }

        private void OnNpcRemoved(string npcId, NpcState n)
        {
            if (_npcs.TryGetValue(npcId, out var npc))
            {
                _npcs.Remove(npcId);
                if (npc != null) Destroy(npc.gameObject);
            }
        }

        private Transform FindUser(string userId) =>
            !string.IsNullOrEmpty(userId) && _byUserId.TryGetValue(userId, out var v) && v != null ? v.transform : null;

        public AvatarView AvatarForUser(string userId) =>
            !string.IsNullOrEmpty(userId) && _byUserId.TryGetValue(userId, out var v) ? v : null;

        // ---------------------------------------------------------------- chat, input, rejections

        private void OnSaid(SaidMessage m)
        {
            AvatarView v = null;
            if (Channel != null && m.sessionId == Channel.SessionId && LocalPlayer != null) v = LocalPlayer.GetComponent<AvatarView>();
            else if (_players.TryGetValue(m.sessionId ?? "", out var rp) && rp != null) v = rp.GetComponent<AvatarView>();
            if (v != null) Bus.Publish(new BubbleSignal { anchor = v.HeadAnchor, text = m.text, isNpc = false, seconds = 4.5f });
        }

        private void OnRejected(RejectedMessage r)
        {
            if (r.type == "move" && LocalPlayer != null && Channel?.State != null &&
                Channel.State.players.TryGetValue(Channel.SessionId, out var me))
                LocalPlayer.SnapTo(Venue.ToWorld((float)me.x, (float)me.z), (float)me.rotY);
            if (r.code == "SPOT_TAKEN") Bus.Publish(ToastSignal.Id("toast_spot_taken", new Dictionary<string, string>()));
            else if (r.code == "RATE_LIMITED") Bus.Publish(ToastSignal.Text("Slow down", ToastKind.Bad));
        }

        private void OnGroundTapped(Vector3 p)
        {
            if (LocalPlayer != null) LocalPlayer.MoveTo(p);
        }

        private void OnSpotTapped(SpotAnchor spot)
        {
            if (LocalPlayer != null) LocalPlayer.GoSit(spot.spotId);
        }

        // ---------------------------------------------------------------- voice

        public async Task<bool> ConnectVoice()
        {
            if (!Services.TryGet<VoiceService>(out var voice) || Channel == null) return false;
            if (!Features.IsAvailable(Features.Voice))
            {
                Log.Info("voice", "Voice is degraded server-side; not connecting");
                return false;
            }
            voice.SpeakerResolver = id => AvatarForUser(id)?.Voice;
            voice.PositionResolver = id => { var a = AvatarForUser(id); return a != null ? a.transform.position : (Vector3?)null; };
            voice.ListenerPosition = () => LocalPlayer != null ? LocalPlayer.transform.position : Vector3.zero;
            voice.LocalSpeakingChanged -= OnLocalSpeaking;
            voice.LocalSpeakingChanged += OnLocalSpeaking;
            try
            {
                var env = Env.Current;
                string url, token;
                if (!string.IsNullOrEmpty(env.liveKitDevToken))
                {
                    url = env.liveKitUrlOverride;
                    token = env.liveKitDevToken;
                }
                else
                {
                    var t = await Services.Get<Api>().RtcToken("venue", Channel.InstanceId);
                    url = string.IsNullOrEmpty(env.liveKitUrlOverride) ? t.Url : env.liveKitUrlOverride;
                    token = t.Token;
                }
                return await voice.Connect(url, token, "venue:" + Channel.InstanceId);
            }
            catch (Exception e)
            {
                Log.Warn("voice", "Voice unavailable: " + e.Message);
                return false;
            }
        }

        private void OnLocalSpeaking(bool on) => Channel?.Talking(on);

        private static string[] ToArray(Colyseus.Schema.ArraySchema<string> a)
        {
            var r = new string[7];
            if (a == null) return r;
            for (int i = 0; i < 7 && i < a.Count; i++) r[i] = a[i];
            return r;
        }
    }
}
