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
    public sealed class NpcController : MonoBehaviour
    {
        public const float WalkSpeed = 1.6f;

        public string NpcId { get; private set; }
        public NpcState State { get; private set; }

        private AvatarView _view;
        private VenueSceneRoot _venue;
        private NavMeshAgent _agent;
        private Func<string, Transform> _findUser;
        private string _lastKey;
        private string _arriveAnim;
        private bool _arriveLoop;
        private bool _leaving;
        private Transform _followTarget;
        private float _nextIdleFidget;

        public void Init(string npcId, NpcState state, VenueSceneRoot venue, Func<string, Transform> findUser)
        {
            NpcId = npcId;
            _venue = venue;
            _findUser = findUser;
            _view = GetComponent<AvatarView>();
            _view.npcId = npcId;
            _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.speed = WalkSpeed;
            _agent.radius = 0.3f;
            _agent.height = 1.75f;
            _agent.angularSpeed = 540f;
            _agent.acceleration = 8f;
            _agent.stoppingDistance = 0.1f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            // Spawn at the state's spot (or the entrance) so a late joiner sees the NPC in place.
            Vector3 spawn = venue.EntrancePosition;
            if (!string.IsNullOrEmpty(state.spotId) && venue.TryGetSpot(state.spotId, out var sp, out _)) spawn = sp;
            if (NavMesh.SamplePosition(spawn, out var hit, 3f, NavMesh.AllAreas)) _agent.Warp(hit.position);
            else transform.position = spawn;
            Apply(state);
        }

        /// <summary>Called on every NpcState change.</summary>
        public void Apply(NpcState s)
        {
            State = s;
            var key = s.state + "|" + s.spotId + "|" + s.animId + "|" + s.lineId + "|" + s.startedAt;
            if (key == _lastKey) return;
            _lastKey = key;
            _followTarget = null;
            _arriveAnim = null;
            float since = ServerClock.IsSynced ? ServerClock.SecondsSince(s.startedAt) : 0f;

            switch (s.state)
            {
                case "idle":
                    Stop();
                    _view.SetBase("loco_idle");
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
                    Talk(s);
                    break;
                case "react":
                    Stop();
                    _view.PlayOneShot(s.animId, -since);
                    break;
                case "follow":
                    _followTarget = _findUser?.Invoke(s.targetUserId);
                    break;
                case "leave":
                    _leaving = true;
                    GoToSpot("exit", since, null, false);
                    break;
                default:
                    Log.UnknownId("npc_states", s.state);
                    _view.SetBase("loco_idle");
                    break;
            }

            if (!string.IsNullOrEmpty(s.emoteId) && s.state != "react") _view.PlayOneShot(s.emoteId, -since);
        }

        private void GoToSpot(string spotId, float secondsSinceStart, string thenAnim, bool loop)
        {
            if (!_venue.TryGetSpot(spotId, out var target, out var rot))
            {
                if (thenAnim != null) _view.SetBase(thenAnim);
                return;
            }
            _arriveAnim = thenAnim;
            _arriveLoop = loop;
            float dist = Vector3.Distance(transform.position, target);
            bool shouldHaveArrived = secondsSinceStart > dist / WalkSpeed + 0.5f;
            if (shouldHaveArrived || !_agent.isOnNavMesh)
            {
                if (_agent.isOnNavMesh) _agent.Warp(target);
                else transform.position = target;
                transform.rotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0);
                Arrive();
                return;
            }
            _agent.isStopped = false;
            _agent.SetDestination(target);
            _view.SetBase("loco_walk");
        }

        private void Arrive()
        {
            if (_leaving)
            {
                // The server removes the NPC from state shortly after; hide now so it does not linger.
                gameObject.SetActive(false);
                return;
            }
            if (_arriveAnim != null)
            {
                if (_arriveLoop) _view.SetBase(_arriveAnim);
                else _view.PlayOneShot(_arriveAnim);
                _arriveAnim = null;
            }
            else _view.SetBase("loco_idle");
        }

        private void Stop()
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
        }

        private void Talk(NpcState s)
        {
            string text = null;
            if (Services.TryGet<ConfigStore>(out var cfg)) text = cfg.Registries.FormatLine(s.lineId, s.lineParams, s.archetypeId);
            if (string.IsNullOrEmpty(text)) return;
            Bus.Publish(new BubbleSignal { anchor = _view.HeadAnchor, text = text, isNpc = true, seconds = 3.5f });
            Babble.Speak(this, _view.Voice, text, _view.archetypeId, _view.BabblePitch);
            _view.PlayOneShot("emote_point");
        }

        private void Update()
        {
            if (_agent == null) return;
            _view.SetSpeed(_agent.velocity.magnitude);

            if (_followTarget != null && _agent.isOnNavMesh)
            {
                var behind = _followTarget.position - _followTarget.forward * 2f;
                if ((behind - transform.position).sqrMagnitude > 0.5f) _agent.SetDestination(behind);
                _view.SetBase(_agent.velocity.sqrMagnitude > 0.04f ? "loco_walk" : "loco_idle");
                return;
            }

            if (_agent.isOnNavMesh && !_agent.isStopped && _agent.hasPath && !_agent.pathPending &&
                _agent.remainingDistance <= _agent.stoppingDistance + 0.05f)
            {
                _agent.ResetPath();
                if (State != null && _venue.TryGetSpot(State.spotId, out _, out var rot))
                    transform.rotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0);
                Arrive();
            }

            if (State != null && State.state == "idle" && Time.time > _nextIdleFidget)
            {
                _nextIdleFidget = Time.time + UnityEngine.Random.Range(6f, 12f);
                if (UnityEngine.Random.value < 0.3f) _view.PlayOneShot("emote_shrug");
            }
        }
    }
}
