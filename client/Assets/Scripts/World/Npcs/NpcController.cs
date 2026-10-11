using System;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Realtime;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// Client half of the NPC split (Build Spec "NPCs and AI"): the server sends `{state, spotId, animId,
    /// targetUserId, lineId, lineParams, emoteId, startedAt}`; the client walks the NPC to the named spot on the
    /// NavMesh and plays the state's animation, speech bubble and babble. Late joiners snap to where the NPC
    /// should already be, using `startedAt` in server time.
    /// </summary>
    [RequireComponent(typeof(AvatarView))]
    public sealed class NpcController : MonoBehaviour, IKnockdownOwner
    {
        public const float WalkSpeed = 1.6f;

        public string NpcId { get; private set; }
        public NpcState State { get; private set; }

        /// <summary>
        /// Presentation seam (Features/WorldLife binds these; all null = the behaviour below, unchanged).
        /// PresentRewrite maps a replicated state to the state to play (spot aliases, stable run start, sticky anims).
        /// PresentTalk returns true when it presented the line itself. PresentApplied runs after every Apply with the
        /// raw and the played state. PresentKeepHidden is asked by VenueSession before re-activating a hidden NPC.
        /// </summary>
        public static Func<NpcController, NpcState, NpcState> PresentRewrite;
        public static Func<NpcController, NpcState, bool> PresentTalk;
        public static Action<NpcController, NpcState, NpcState> PresentApplied;
        public static Func<NpcController, NpcState, bool> PresentKeepHidden;

        private AvatarView _view;
        private VenueSceneRoot _venue;
        private NavMeshAgent _agent;
        private Func<string, Transform> _findUser;
        private string _lastKey;
        private string _lastEmoteId;
        private NpcState _pendingState;
        private string _arriveAnim;
        private bool _arriveLoop;
        private bool _leaving;
        private Transform _followTarget;
        private float _nextIdleFidget;
        private float _nextFollowPath;
        private float _nextGroundCheck;
        private float _arriveRotation;
        private Vector3 _pathTarget;
        private bool _travelling;
        private bool _followPathRequested;
        private float _nextBindAttempt;
        private NavMeshPath _validatedPath;
        // Seat presentation: an NPC acting at a seat-like spot walks to its approach, then sits down onto the seat.
        private bool _arriveSeated;
        private Vector3 _seatPosition, _seatApproach;
        private bool _seated;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private bool _reportedStopOrder;
        public bool ReviewHasLeaveReceipt { get; private set; }
        public Vector3 ReviewLeaveRoot { get; private set; }
        public Vector3 ReviewLeaveNavigation { get; private set; }
        public Vector3 ReviewLeaveTarget { get; private set; }
        public string ReviewLeaveSnapshot { get; private set; }
#endif
        private bool AgentReady => _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh;

        public void Init(string npcId, NpcState state, VenueSceneRoot venue, Func<string, Transform> findUser)
        {
            NpcId = npcId;
            _venue = venue;
            _findUser = findUser;
            _view = GetComponent<AvatarView>();
            _view.npcId = npcId;
            CTLife.Audio.Footsteps.Attach(gameObject, 0.6f);
            AvatarFactory.AddBodyCollider(gameObject);
            // NavMeshPath allocates native state and cannot run in a MonoBehaviour constructor.
            _validatedPath = new NavMeshPath();
            // Binding starts when the component is added. Place the actor first, rather than
            // creating an agent at the prefab origin and assuming a same-frame Warp has bound it.
            // No server spot yet: the NPC's own home spot, not the entrance (the entrance is the player's spawn).
            Vector3 spawn = venue.NpcHome(npcId);
            var spawnState = Rewrite(state);
            if (!string.IsNullOrEmpty(spawnState.spotId) && venue.TryGetSpot(spawnState.spotId, out var sp, out _)) spawn = sp;
            bool sampledSpawn = NavMesh.SamplePosition(spawn, out var hit, 3f, NavMesh.AllAreas);
            transform.position = sampledSpawn ? hit.position : spawn;
            _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.speed = WalkSpeed;
            _agent.radius = 0.3f;
            _agent.height = 1.75f;
            _agent.angularSpeed = 540f;
            _agent.acceleration = 8f;
            _agent.stoppingDistance = 0.1f;
            _agent.baseOffset = 0f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            RefreshGrounding();
            Apply(state);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            Log.Info("npc-nav", $"Init sampledSpawn={sampledSpawn}; requested={spawn:F3}; {ReviewSnapshot()}");
#endif
        }

        private void OnDestroy()
        {
            if (_venue != null) _venue.ReleaseNpcHome(NpcId);
        }

        /// <summary>Called on every NpcState change.</summary>
        public void Apply(NpcState s)
        {
            if (s == null) return;
            var raw = s;
            ApplyState(Rewrite(raw));
            if (PresentApplied == null) return;
            try { PresentApplied(this, raw, State); }
            catch (Exception e) { Log.Exception("npc-present", e); }
        }

        private NpcState Rewrite(NpcState raw)
        {
            if (PresentRewrite == null || raw == null) return raw;
            try { return PresentRewrite(this, raw) ?? raw; }
            catch (Exception e) { Log.Exception("npc-present", e); return raw; }
        }

        private void ApplyState(NpcState s)
        {
            State = s;
            if (_knocked) { _lastKey = null; return; } // applied when it is back on its feet
            // A newly enabled agent can bind on the next navigation tick. Keep the latest
            // replicated state pending; do not deduplicate an entry whose Stop/path never ran.
            if (!AgentReady)
            {
                _pendingState = s;
                _lastKey = null;
                Stop();
                _view?.SetBase("loco_idle");
                return;
            }
            _pendingState = null;
            var key = s.state + "|" + s.spotId + "|" + s.animId + "|" + s.lineId + "|" + s.startedAt +
                "|" + s.targetUserId;
            bool newEmote = s.emoteId != _lastEmoteId;
            _lastEmoteId = s.emoteId;
            if (key == _lastKey)
            {
                // An overlay update is not a fresh journey. Re-entering GoToSpot here would
                // compare the old start time to the remaining trip and can teleport mid-walk.
                if (newEmote && !string.IsNullOrEmpty(s.emoteId) && s.state != "react")
                    _view.PlayOneShot(s.emoteId);
                return;
            }
            _lastKey = key;
            // Leaving a seat: stand up there, then step to the approach before any new path starts.
            if (_seated && !(s.state == "act" && IsSeatAnim(s.animId))) StandFromSeat();
            // A state owns its path. Do not resume a stale destination after talk/idle, or carry
            // a pending leave into the next state and hide the NPC at an unrelated arrival.
            Stop();
            _followTarget = null;
            _arriveAnim = null;
            _leaving = s.state == "leave";
            _nextFollowPath = 0f;
            float since = ServerClock.IsSynced ? ServerClock.SecondsSince(s.startedAt) : 0f;

            switch (s.state)
            {
                case "idle":
                    Stop();
                    _view.SetBase("loco_idle");
                    _nextIdleFidget = Time.time + UnityEngine.Random.Range(6f, 12f);
                    break;
                case "wander":
                case "goto":
                    GoToSpot(s.spotId, since, null, false);
                    break;
                case "act":
                    GoToSpot(s.spotId, since, s.animId, true);
                    break;
                case "talk":
                    Stop();
                    _view.SetBase("loco_idle");
                    Talk(s);
                    break;
                case "react":
                    Stop();
                    _view.SetBase("loco_idle");
                    _view.PlayOneShot(s.animId, -since);
                    break;
                case "follow":
                    _followTarget = _findUser?.Invoke(s.targetUserId);
                    _view.SetBase("loco_idle");
                    break;
                case "leave":
                    GoToSpot("exit", since, null, false);
                    break;
                default:
                    Log.UnknownId("npc_states", s.state);
                    _view.SetBase("loco_idle");
                    break;
            }

            if (!string.IsNullOrEmpty(s.emoteId) && s.state != "react") _view.PlayOneShot(s.emoteId, -since);
        }

        private static bool IsSeatAnim(string anim) => anim == "act_sit" || anim == "act_type" || anim == "act_frantic_type" ||
                                                        anim == "act_lean_back" || anim == "act_head_in_hands" || anim == "act_sit_talk";

        private void StandFromSeat()
        {
            _seated = false;
            var seatRotation = transform.rotation;
            if (_agent != null && _agent.isActiveAndEnabled && NavMesh.SamplePosition(_seatApproach, out var hit, 0.75f, _agent.areaMask))
            {
                _agent.Warp(hit.position);
                _view.BeginSeatLeave(_seatPosition, seatRotation, hit.position, seatRotation);
            }
        }

        private void GoToSpot(string spotId, float secondsSinceStart, string thenAnim, bool loop)
        {
            if (!_venue.TryGetSpot(spotId, out var target, out var rot))
            {
                FailPath("missing spot " + spotId);
                return;
            }
            // Seat-like acts path to the authored approach, then sit down onto the seat (SeatPresenter).
            Vector3 approach = target;
            _arriveSeated = loop && IsSeatAnim(thenAnim) && _venue.TryGetSpotApproach(spotId, out approach) &&
                            (approach - target).sqrMagnitude > 0.01f;
            if (_arriveSeated) { _seatPosition = target; _seatApproach = approach; target = approach; }
            if (!AgentReady || !NavMesh.SamplePosition(target, out var navTarget, 0.75f, _agent.areaMask))
            {
                FailPath("no navigation at spot " + spotId);
                return;
            }
            // Sampling proves that the target lies on *a* mesh, not that this agent can reach it.
            if (!NavMesh.CalculatePath(_agent.nextPosition, navTarget.position, _agent.areaMask, _validatedPath) ||
                _validatedPath.status != NavMeshPathStatus.PathComplete)
            {
                FailPath("unreachable spot " + spotId + " (" + _validatedPath.status + ")");
                return;
            }
            _arriveAnim = thenAnim;
            _arriveLoop = loop;
            _arriveRotation = rot;
            _pathTarget = navTarget.position;
            float dist = Vector3.Distance(_agent.nextPosition, navTarget.position);
            float pathLength = 0f;
            var corners = _validatedPath.corners;
            for (int i = 1; i < corners.Length; i++) pathLength += Vector3.Distance(corners[i - 1], corners[i]);
            bool shouldHaveArrived = secondsSinceStart > pathLength / WalkSpeed + 0.5f;
            if (shouldHaveArrived || dist <= _agent.stoppingDistance + 0.05f)
            {
                if (_agent.Warp(navTarget.position))
                {
                    _nextGroundCheck = 0f;
                    RefreshGrounding();
                    Arrive(snapped: shouldHaveArrived); // late joiners see the NPC already seated
                }
                else FailPath("warp rejected at " + spotId);
                return;
            }
            _agent.isStopped = false;
            if (!_agent.SetPath(_validatedPath))
            {
                FailPath("path rejected at " + spotId);
                return;
            }
            _travelling = true;
            _view.SetBase("loco_walk");
        }

        private void Arrive(bool snapped = false)
        {
            var arrivedFrom = _view.PresentationPosition; var arrivedRotation = transform.rotation;
            Stop();
            transform.rotation = Quaternion.Euler(0, _arriveRotation * Mathf.Rad2Deg, 0);
            if (_leaving)
            {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                // Read navigation while the agent is still bound. Deactivation makes those
                // native getters unavailable; retain the actual last position for the fixture.
                ReviewHasLeaveReceipt = true;
                ReviewLeaveRoot = transform.position;
                ReviewLeaveNavigation = _agent.nextPosition;
                ReviewLeaveTarget = _pathTarget;
                _venue.TryGetSpot("exit", out var rawExit, out _);
                ReviewLeaveSnapshot = $"rawExit={rawExit:F3}; pathTarget={_pathTarget:F3}; " + ReviewSnapshot();
                Log.Info("npc-nav", "Leave before hide: " + ReviewLeaveSnapshot);
#endif
                // The server removes the NPC from state shortly after; hide now so it does not linger.
                gameObject.SetActive(false);
                return;
            }
            if (_arriveAnim != null)
            {
                if (_arriveSeated)
                {
                    // From the approach onto the seat: the actor's logical root goes to the seat; the model sits down.
                    var seatRotation = transform.rotation;
                    if (!_agent.Warp(_seatPosition)) transform.position = _seatPosition;
                    if (!snapped) _view.BeginSeatEnter(arrivedFrom, arrivedRotation, _seatPosition, seatRotation);
                    _seated = true;
                    _arriveSeated = false;
                }
                if (_arriveLoop) _view.SetBase(_arriveAnim);
                else _view.PlayOneShot(_arriveAnim);
                _arriveAnim = null;
            }
            else _view.SetBase("loco_idle");
        }

        private void Stop()
        {
            _travelling = false;
            _followPathRequested = false;
            if (AgentReady)
            {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                // One diagnostic captures the formerly failing operation order on the real engine.
                _agent.isStopped = true;
                bool afterSet = _agent.isStopped;
#endif
                _agent.ResetPath();
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                bool afterReset = _agent.isStopped;
#endif
                _agent.velocity = Vector3.zero;
                // Path/manual-velocity operations precede the final authoritative stopped state,
                // matching the already device-checked LocalPlayer.CancelMovement ordering.
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                bool afterVelocity = _agent.isStopped;
#endif
                _agent.isStopped = true;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if (!_reportedStopOrder)
                {
                    _reportedStopOrder = true;
                    Log.Info("npc-nav", $"Stop order npc={NpcId}; afterSet={afterSet}; afterReset={afterReset}; " +
                        $"afterVelocity={afterVelocity}; final={_agent.isStopped}");
                }
#endif
            }
            _view?.SetSpeed(0f);
        }

        private void FailPath(string reason)
        {
            Stop();
            _arriveAnim = null;
            _view.SetBase("loco_idle");
            Log.Warn("npc-nav", NpcId + ": " + reason + "; waiting for a new server state.");
        }

        private void Talk(NpcState s)
        {
            if (PresentTalk != null)
            {
                try { if (PresentTalk(this, s)) return; }
                catch (Exception e) { Log.Exception("npc-present", e); }
            }
            string text = null;
            if (Services.TryGet<ConfigStore>(out var cfg)) text = cfg.Registries.FormatLine(s.lineId, s.lineParams, s.archetypeId);
            if (string.IsNullOrEmpty(text)) return;
            Bus.Publish(new BubbleSignal { anchor = _view.HeadAnchor, text = text, isNpc = true, seconds = 3.5f });
            var bank = AudioManager.Instance != null ? AudioManager.Instance.BabbleBankFor(_view.archetypeId) : null;
            if (bank != null)
            {
                var player = GetComponent<BabblePlayer>();
                if (player == null) player = gameObject.AddComponent<BabblePlayer>();
                player.bank = bank;
                player.npcId = NpcId;
                player.Say(text);
            }
            else Babble.Speak(this, _view.Voice, text, _view.archetypeId, _view.BabblePitch);
            _view.PlayOneShot("emote_point");
        }

        private bool _knocked;

        void IKnockdownOwner.PlaceRoot(Vector3 position, Quaternion rotation)
        {
            if (AgentReady && NavMesh.SamplePosition(position, out var hit, 1.2f, _agent.areaMask)) _agent.Warp(hit.position);
            else transform.position = position;
            transform.rotation = rotation;
        }

        void IKnockdownOwner.SetControl(bool enabled)
        {
            _knocked = !enabled;
            if (_knocked) { Stop(); return; }
            // Back on its feet: carry on with the latest replicated state from here.
            _lastKey = null;
            if (State != null) ApplyState(State);
        }

        private void Update()
        {
            if (_agent == null || _knocked) return;
            if (!AgentReady)
            {
                _view.SetSpeed(0f);
                _view.SetBase("loco_idle");
                if (_pendingState == null && State != null)
                {
                    _pendingState = State;
                    _lastKey = null;
                }
                // If navigation is installed after the actor, retry binding at its current
                // position. Respect an externally disabled agent; never enable it here.
                if (_agent.isActiveAndEnabled && Time.unscaledTime >= _nextBindAttempt)
                {
                    _nextBindAttempt = Time.unscaledTime + 0.2f;
                    if (NavMesh.SamplePosition(transform.position, out var bind, 0.75f, _agent.areaMask))
                        _agent.Warp(bind.position);
                }
                if (!AgentReady) return;
            }
            if (_pendingState != null)
            {
                ApplyState(_pendingState);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                Log.Info("npc-nav", "Deferred state applied; " + ReviewSnapshot());
#endif
                if (!gameObject.activeInHierarchy) return;
            }
            RefreshGrounding();
            _view.SetSpeed(_agent.velocity.magnitude);

            if (State?.state == "follow")
            {
                // A followed player may be replicated after the NPC. Resolve again until available.
                if (_followTarget == null) _followTarget = _findUser?.Invoke(State.targetUserId);
                if (_followTarget == null || !AgentReady)
                {
                    Stop();
                    _view.SetBase("loco_idle");
                    return;
                }
                if (Time.unscaledTime >= _nextFollowPath)
                {
                    _nextFollowPath = Time.unscaledTime + 0.2f;
                    var behind = _followTarget.position - _followTarget.forward * 2f;
                    if ((behind - transform.position).sqrMagnitude <= 0.36f) Stop();
                    else if (NavMesh.SamplePosition(behind, out var hit, 0.75f, _agent.areaMask) &&
                             _venue.IsWalkable(hit.position))
                    {
                        _agent.isStopped = false;
                        _followPathRequested = _agent.SetDestination(hit.position);
                        if (!_followPathRequested) Stop();
                    }
                    else Stop();
                }
                if (_followPathRequested && !_agent.pathPending && _agent.pathStatus != NavMeshPathStatus.PathComplete)
                    Stop();
                _view.SetBase(_agent.velocity.sqrMagnitude > 0.04f ? "loco_walk" : "loco_idle");
                return;
            }

            if (_travelling && !_agent.pathPending)
            {
                if (_agent.pathStatus != NavMeshPathStatus.PathComplete || !_agent.hasPath)
                    FailPath("path became unavailable");
                else if (_agent.remainingDistance <= _agent.stoppingDistance + 0.05f)
                {
                    // A partial endpoint must never become an act/leave arrival.
                    var delta = _agent.nextPosition - _pathTarget;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= 0.25f * 0.25f) Arrive();
                    else FailPath("path ended before target");
                }
            }

            if (State != null && State.state == "idle" && Time.time > _nextIdleFidget)
            {
                _nextIdleFidget = Time.time + UnityEngine.Random.Range(6f, 12f);
                if (UnityEngine.Random.value < 0.3f) _view.PlayOneShot("emote_shrug");
            }
        }

        private void RefreshGrounding()
        {
            if (!AgentReady || Time.unscaledTime < _nextGroundCheck) return;
            _nextGroundCheck = Time.unscaledTime + 0.2f;
            // Match the local player's collider-based correction for voxelized navigation height.
            if (!NavMesh.SamplePosition(transform.position, out var nav, 0.75f, _agent.areaMask) ||
                !Physics.Raycast(nav.position + Vector3.up * 0.35f, Vector3.down, out var floor, 0.85f,
                    _venue.groundMask.value & CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.7f) return;
            float offset = floor.point.y - nav.position.y;
            if (Mathf.Abs(offset) <= 0.35f) _agent.baseOffset = offset;
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        public string ReviewSnapshot()
        {
            string basic = $"npc={NpcId}; state={State?.state}; active={gameObject.activeInHierarchy}; " +
                $"agentEnabled={_agent != null && _agent.isActiveAndEnabled}; ready={AgentReady}; " +
                $"pendingState={_pendingState != null}; position={transform.position:F3}; target={_pathTarget:F3}";
            return !AgentReady ? basic : basic + $"; stopped={_agent.isStopped}; path={_agent.hasPath}; " +
                $"pathPending={_agent.pathPending}; pathStatus={_agent.pathStatus}; " +
                $"velocity={_agent.velocity.magnitude:F3}; navPosition={_agent.nextPosition:F3}; " +
                $"baseOffset={_agent.baseOffset:F4}; travelling={_travelling}";
        }
#endif
    }
}
