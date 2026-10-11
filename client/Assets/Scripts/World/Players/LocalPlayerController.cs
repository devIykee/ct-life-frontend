using System.Collections.Generic;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// Direct camera-relative movement on the venue NavMesh, sends `move {x, z, rotY}` at most 10 times a
    /// second (contract section 5) while moving plus one final update on arrival. Positions are venue-local metres
    /// and rotY is the yaw in radians. Walk 2.2 m/s, run 4.5 m/s for long trips (server max 6 m/s; it derives the
    /// anim from speed: walk above 0.2, run above 3.5).
    /// </summary>
    [RequireComponent(typeof(AvatarView))]
    public sealed class LocalPlayerController : MonoBehaviour, IKnockdownOwner
    {
        public const float WalkSpeed = DirectMovementInput.WalkSpeed;
        public const float RunSpeed = DirectMovementInput.RunSpeed;
        public const float RunIfFartherThan = 9f;

        private NavMeshAgent _agent;
        private AvatarView _view;
        private VenueSceneRoot _venue;
        private VenueChannel _channel;
        private Vector3 _lastSentPos;
        private float _lastSentRot;
        private bool _dirty;
        private string _pendingSpot;
        private string _seatedSpot;
        private bool _hasDestination, _pathSubmissionQueued;
        private string _requestedSpot;
        // A locally released claim must not be revived by an older state echo. A new explicit claim
        // removes its key. The protocol has no request ID, so overlapping same-seat claims are not correlated.
        private readonly HashSet<string> _releasedSpots = new HashSet<string>();
        private Vector3 _destination, _standingPosition;
        private float _standingYaw;
        private bool _hasStandingPose;
        private bool _seatExitWasActive, _exitPathQueued;
        private Vector3 _exitDestination;
        private string _exitThenSit;

        private readonly FootstepCadence _footstepCadence = new FootstepCadence();
        private Vector3 _lastFootstepPosition;
        private Vector3 _lastMotionPosition;
        private Vector2 _moveInput;
        private bool _directControl, _running, _appPaused;
        private float _inputAt, _directSpeed;
        private string _locomotion = "loco_idle";
        private float _groundCheckAt;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float _groundY, _navigationY;
        private bool _groundFound;
#endif

        public Vector3 ActualVelocity { get; private set; }
        public bool DirectInputActive => _directControl || (_view != null && _view.SeatPresentationActive && _moveInput.sqrMagnitude > DirectMovementInput.DeadZone * DirectMovementInput.DeadZone);
        public bool IsRunning => _running;
        public int InputEpoch { get; private set; }
        private bool AgentReady => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        public bool IsMoving => AgentReady && (ActualVelocity.sqrMagnitude > 0.01f || _agent.velocity.sqrMagnitude > 0.01f);
        public string SeatedSpot => _seatedSpot;
        /// <summary>A walk is queued or in progress (path mode).</summary>
        public bool HasDestination => _hasDestination || _pathSubmissionQueued;
        /// <summary>The seat this client holds changed (spot id, or null when standing). Server-confirmed or local-only.</summary>
        public event System.Action<string> SeatChanged;
        /// <summary>The server rejected this client's latest `sit`.</summary>
        public event System.Action SeatRejected;
        private bool _seatLocalOnly;
        // Physicality: the CharacterController body (direct control, ledges, impacts) and knockdowns.
        private CharacterBody _body;
        private bool _stunned;
        /// <summary>Show falls to the room through the existing `emote` message (react_liquidated is a registry anim).</summary>
        public static bool BroadcastFalls = true;
        public const float StaggerSpeed = 2.4f, FallSpeed = 3.4f;
        public CharacterBody Body => _body;
        public bool Stunned => _stunned;

        public void Init(VenueSceneRoot venue, VenueChannel channel)
        {
            _venue = venue;
            _channel = channel;
            _view = GetComponent<AvatarView>();
            if (venue != null) _view.BindSeatMotionProfile(venue.GetComponent<CafeSeatMotionProfile>());
            _view.isLocal = true;
            _agent = gameObject.GetComponent<NavMeshAgent>();
            // Unity's missing native-component wrapper compares equal to null in Editor,
            // but is not CLR-null. The ?? operator can retain that invalid wrapper.
            if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.speed = WalkSpeed;
            _agent.acceleration = 14f;
            _agent.angularSpeed = 720f;
            _agent.stoppingDistance = 0.05f;
            _agent.radius = 0.3f;
            _agent.height = 1.75f;
            _agent.baseOffset = 0f;
            _agent.autoBraking = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) _agent.Warp(hit.position);
            if (_body == null)
            {
                _body = GetComponent<CharacterBody>();
                if (_body == null) _body = gameObject.AddComponent<CharacterBody>();
                _body.Impact += OnImpact;
                _body.Landed += OnLanded;
            }
            RefreshGrounding(true);
            _lastSentPos = transform.position;
            _lastSentRot = transform.eulerAngles.y;
            // Publish the spawn position once: until the first `move` the server shows this player at its default.
            _dirty = channel != null;
            ResetFootsteps();
            BindListener();
        }

        private void BindListener()
        {
            if (_view != null)
                AudioManager.Instance?.BindPlayerListener(transform, _view.Voice != null ? _view.Voice.transform : transform);
        }

        private void OnEnable()
        {
            ControlSettings.Changed += CancelMovement;
            ResetFootsteps();
            BindListener();
        }
        private void OnDisable()
        {
            ControlSettings.Changed -= CancelMovement;
            _view?.CancelSeatExitPresentation(false);
            _seatExitWasActive = false;
            CancelMovement();
            ResetFootsteps();
            AudioManager.Instance?.ReleasePlayerListener(transform);
        }

        private void ResetFootsteps()
        {
            _footstepCadence.Reset();
            _lastFootstepPosition = transform.position;
            _lastMotionPosition = transform.position;
            ActualVelocity = Vector3.zero;
        }

        private void OnApplicationPause(bool paused) { _appPaused = paused; if (paused) { CancelMovement(); _view?.CancelSeatExitPresentation(); _seatExitWasActive = false; } }
        private void OnApplicationFocus(bool focused) { if (!focused) { CancelMovement(); _view?.CancelSeatExitPresentation(); _seatExitWasActive = false; } }

        /// <summary>HUD axes: right/up on screen. Refresh while held; a missing producer times out safely.</summary>
        public void SetMoveInput(Vector2 input)
        {
            if (_appPaused || !isActiveAndEnabled) return;
            if (_stunned || (_body != null && _body.Airborne)) { _moveInput = Vector2.zero; return; } // down or falling: no steering
            if (float.IsNaN(input.x) || float.IsInfinity(input.x) || float.IsNaN(input.y) || float.IsInfinity(input.y))
                input = Vector2.zero;
            input = Vector2.ClampMagnitude(input, 1f);
            if (input.magnitude <= DirectMovementInput.DeadZone)
            {
                if (_directControl || (_view != null && _view.SeatPresentationActive)) StopDirectInput();
                return;
            }
            StandIfSeated(); // A confirmed seat deliberately disables the agent.
            if (_view != null && _view.SeatPresentationActive)
            {
                _moveInput = input; _inputAt = Time.unscaledTime;
                _exitPathQueued = false; _exitThenSit = null;
                return; // Accept/replace immediately; translating a seated body is not the response.
            }
            if (!AgentReady) return;
            if (!_directControl)
            {
                CancelRequestedSeat();
                _agent.ResetPath();
                _agent.velocity = Vector3.zero;
                _hasDestination = false; _pathSubmissionQueued = false;
                _pendingSpot = null;
                _directControl = true;
                _agent.speed = RunSpeed;
                _agent.updateRotation = false;
                _agent.isStopped = false;
                _body?.BeginDirect();
            }
            _moveInput = input;
            _inputAt = Time.unscaledTime;
        }

        /// <summary>Release a thumb without cancelling an unrelated, intentional seat/review path.</summary>
        public void StopDirectInput()
        {
            _moveInput = Vector2.zero;
            _running = false;
            _directSpeed = 0f;
            if (!_directControl) return;
            _directControl = false;
            _body?.EndDirect();
            if (AgentReady)
            {
                _agent.velocity = Vector3.zero;
                _agent.isStopped = true;
                _agent.updateRotation = true;
            }
            _dirty = true; // Publish the final accepted position even after a very short movement.
        }

        /// <summary>UI, pause, travel, seat confirmation or correction cancels all pending local movement.</summary>
        public void CancelMovement()
        {
            CancelRequestedSeat();
            StopMovement();
        }

        private void StopMovement(bool resetPath = true)
        {
            _exitPathQueued = false; _exitThenSit = null;
            InputEpoch++;
            StopDirectInput();
            _hasDestination = false; _pathSubmissionQueued = false;
            _pendingSpot = null;
            if (AgentReady)
            {
                if (resetPath) _agent.ResetPath();
                _agent.velocity = Vector3.zero;
                _agent.isStopped = true;
            }
        }

        /// <summary>Walk to a world point if it is on the NavMesh and inside the venue's walkable rectangles.</summary>
        public bool MoveTo(Vector3 worldPoint, string thenSitAt = null)
        {
            if (_appPaused) { ObserveReviewCommand("path-rejected:paused"); return false; }
            StandIfSeated();
            if (!AgentReady) { ObserveReviewCommand("path-rejected:agent-not-ready"); return false; }
            if (!NavMesh.SamplePosition(worldPoint, out var hit, thenSitAt != null ? 0.35f : 1.5f, _agent.areaMask))
            { ObserveReviewCommand("path-rejected:no-sample"); return false; }
            if (_venue != null && !_venue.IsWalkable(hit.position))
            { ObserveReviewCommand("path-rejected:outside-venue"); return false; }
            var path = new NavMeshPath();
            bool calculated = _agent.CalculatePath(hit.position, path);
            ObserveReviewCommand($"path-calculated:{calculated}; status={path.status}; corners={path.corners.Length}; " +
                $"actor={transform.position:F6}; next={_agent.nextPosition:F6}; hit={hit.position:F6}; stopped={_agent.isStopped}");
            if (!calculated || path.status != NavMeshPathStatus.PathComplete) return false;
            if (_view != null && _view.SeatPresentationActive)
            {
                StopDirectInput(); _exitPathQueued = true; _exitDestination = hit.position; _exitThenSit = thenSitAt;
                _agent.isStopped = true;
                return true; // The newest path replaces pending direct/path intent while rise/step completes.
            }
            CancelRequestedSeat();
            // Pause the old path immediately, but coalesce native path replacement in Update.
            // Repeated valid intents in this frame replace only these owned queued fields.
            StopMovement(resetPath: false);
            _pendingSpot = thenSitAt;
            _destination = hit.position;
            float dist = Vector3.Distance(transform.position, hit.position);
            _agent.speed = dist > RunIfFartherThan ? RunSpeed : WalkSpeed;
            _agent.updateRotation = true;
            // Native proof: a complete one-corner path at the current approach cannot be assigned
            // with SetPath. It is an arrival, still subject to the same fresh complete-path check.
            if (AtDestination())
            {
                _agent.isStopped = true;
                bool arrived = ArriveAtDestination();
                if (!arrived)
                {
                    // This immediate request returned false; do not retain a queued seat intent.
                    _hasDestination = false; _pathSubmissionQueued = false;
                    _pendingSpot = null;
                }
                return arrived;
            }
            _hasDestination = true;
            _pathSubmissionQueued = true;
            ObserveReviewCommand($"path-queued:frame={Time.frameCount}; target={_destination:F6}; spot={_pendingSpot}");
            return true; // Accepted local intent; native submission happens once in Update.
        }

        /// <summary>Walk to the frontend approach, then send the unchanged authoritative spot ID.</summary>
        public bool GoSit(string spotId)
        {
            if (_venue == null || !_venue.TryGetSpotApproach(spotId, out var pos)) return false;
            return MoveTo(pos, spotId);
        }

        private string SpotKey(string spotId) => _venue != null ? _venue.LocalSpotId(spotId) : spotId;

        private void CancelRequestedSeat()
        {
            if (string.IsNullOrEmpty(_requestedSpot)) return;
            _releasedSpots.Add(SpotKey(_requestedSpot));
            _requestedSpot = null;
            ObserveReviewCommand("stand");
            _channel?.Stand();
        }

        public void StandIfSeated()
        {
            CancelRequestedSeat();
            if (string.IsNullOrEmpty(_seatedSpot)) return;
            // Restore before releasing the local seat. If the navigation was removed, remain seated
            // instead of publishing an off-mesh anchor as a standing point. Travel destroys this object.
            var seatedPosition = transform.position; var seatedRotation = transform.rotation;
            if (!RestoreStandingPose()) return;
            _seatExitWasActive = BeginLeavePresentation(_seatedSpot, seatedPosition, seatedRotation);
            _releasedSpots.Add(SpotKey(_seatedSpot));
            bool localOnly = _seatLocalOnly;
            _seatedSpot = null; _seatLocalOnly = false;
            ObserveReviewCommand("stand");
            if (!localOnly) _channel?.Stand();
            _view.SetBase("loco_idle");
            SeatChanged?.Invoke(null);
        }

        private void RememberStandingPose(Vector3 position, float yaw)
        {
            _standingPosition = position;
            _standingYaw = yaw;
            _hasStandingPose = true;
        }

        private bool RestoreStandingPose()
        {
            if (_agent == null || !_hasStandingPose ||
                !NavMesh.SamplePosition(_standingPosition, out var hit, 0.75f, _agent.areaMask) ||
                (_venue != null && !_venue.IsWalkable(hit.position))) return false;
            var previousPosition = transform.position;
            var previousRotation = transform.rotation;
            _agent.enabled = false;
            transform.position = hit.position;
            transform.rotation = Quaternion.Euler(0f, _standingYaw, 0f);
            _agent.enabled = true;
            if (!_agent.isOnNavMesh || !_agent.Warp(hit.position))
            {
                _agent.enabled = false;
                transform.SetPositionAndRotation(previousPosition, previousRotation);
                return false;
            }
            _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            _agent.isStopped = true;
            _agent.updateRotation = true;
            _locomotion = "loco_idle";
            RefreshGrounding(true);
            ResetFootsteps();
            // Keep _lastSentPos: it is the last walking coordinate actually offered to the channel.
            // A restored approach may still owe the final throttled walking update.
            _dirty = true;
            return true;
        }

        /// <summary>A move correction updates the walking pose, never a confirmed seated visual anchor.</summary>
        public void SnapTo(Vector3 worldPos, float rotYRad)
        {
            RememberStandingPose(worldPos, rotYRad * Mathf.Rad2Deg);
            if (!string.IsNullOrEmpty(_seatedSpot))
            {
                _lastSentPos = worldPos;
                _lastSentRot = rotYRad * Mathf.Rad2Deg;
                _dirty = false;
                return;
            }
            CancelMovement();
            _view?.CancelSeatExitPresentation(); _seatExitWasActive = false;
            if (AgentReady) _agent.Warp(worldPos);
            else transform.position = worldPos;
            transform.rotation = Quaternion.Euler(0, rotYRad * Mathf.Rad2Deg, 0);
            _lastSentPos = worldPos;
            _lastSentRot = transform.eulerAngles.y;
            _dirty = false;
            RefreshGrounding(true);
            ResetFootsteps();
        }

        /// <summary>Reconcile every local spot state, including empty and same-seat animation changes.</summary>
        public void OnServerSeat(string spotId, string anim)
        {
            if (string.IsNullOrEmpty(spotId))
            {
                if (string.IsNullOrEmpty(_seatedSpot)) return;
                if (_seatLocalOnly) return; // a local-only engagement is not the server's to end
                var seatedPosition = transform.position; var seatedRotation = transform.rotation;
                if (!RestoreStandingPose()) return;
                _seatExitWasActive = BeginLeavePresentation(_seatedSpot, seatedPosition, seatedRotation);
                _seatedSpot = null;
                _view.SetBase("loco_idle");
                SeatChanged?.Invoke(null);
                return; // An authoritative stand must not echo another Stand request.
            }
            if (_releasedSpots.Contains(SpotKey(spotId))) return;
            if (_venue == null || !_venue.TryGetSpot(spotId, out var p, out var rot)) return;
            if (string.IsNullOrEmpty(_seatedSpot))
            {
                if (AgentReady) RememberStandingPose(_agent.nextPosition, transform.eulerAngles.y);
                else if (!_hasStandingPose) return;
            }
            _view.CancelSeatExitPresentation(false); _seatExitWasActive = false;
            bool wasStanding = string.IsNullOrEmpty(_seatedSpot);
            var fromPosition = _view.PresentationPosition; var fromRotation = transform.rotation;
            StopMovement();
            _requestedSpot = null;
            bool changed = _seatedSpot != spotId;
            _seatedSpot = spotId; _seatLocalOnly = false;
            _dirty = false;
            if (_agent != null) _agent.enabled = false;
            var seatRotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0);
            transform.SetPositionAndRotation(p, seatRotation);
            _view.SetSpeed(0f);
            // Sit down from where the player stands (approach point) instead of popping onto the seat.
            if (wasStanding) _view.BeginSeatEnter(fromPosition, fromRotation, p, seatRotation);
            var presentation = OccupiedSpotAnimation.Resolve(_venue, spotId, anim);
            _view.SetBase(string.IsNullOrEmpty(presentation) ? "act_sit" : presentation, string.IsNullOrEmpty(presentation) ? null : spotId);
            ResetFootsteps();
            if (changed) SeatChanged?.Invoke(spotId);
        }

        public void OnSeatRejected()
        {
            // A delayed rejection of a cancelled claim must not cancel a newer approach that has
            // not dispatched any request. The protocol cannot correlate two already-dispatched claims.
            if (string.IsNullOrEmpty(_requestedSpot)) return;
            _releasedSpots.Add(SpotKey(_requestedSpot));
            _requestedSpot = null;
            _pendingSpot = null;
            SeatRejected?.Invoke();
        }

        /// <summary>Claim a spot from where the player stands now (the interaction system walked here first).</summary>
        public bool RequestSeat(string spotId)
        {
            if (string.IsNullOrEmpty(spotId) || _channel == null || !_channel.IsJoined || !string.IsNullOrEmpty(_seatedSpot)) return false;
            if (AgentReady) RememberStandingPose(_agent.nextPosition, transform.eulerAngles.y);
            _releasedSpots.Remove(SpotKey(spotId));
            _requestedSpot = spotId;
            var wire = _venue != null ? _venue.WireSpotId(spotId) : spotId;
            ObserveReviewCommand("sit:" + wire);
            SendSit(wire);
            return true;
        }

        /// <summary>
        /// Claims a spot. The venue room stands a seated player up on any later `move` ("If seated, stand up"), so the last
        /// position goes out first and no `move` is sent while the claim is in flight (Update). Without this the player sat
        /// and stood straight back up on the Seeker (2026-10-08). Stick input cancels the claim and moves resume.
        /// </summary>
        private void SendSit(string wireSpotId)
        {
            if (_dirty && _venue != null && _channel != null)
            {
                var local = _venue.ToLocal(transform.position);
                float yaw = transform.eulerAngles.y;
                if (_channel.Move(local.x, local.y, Mathf.Repeat(yaw, 360f) * Mathf.Deg2Rad))
                {
                    _lastSentPos = transform.position;
                    _lastSentRot = yaw;
                    _dirty = false;
                }
            }
            _channel.Sit(wireSpotId);
        }

        /// <summary>
        /// Local-only engagement (no contract message, or no room): present the actor at a point, facing yaw, playing
        /// <paramref name="anim"/>; the stick or "Step away" ends it. Never sent to the server.
        /// </summary>
        public bool EngageLocal(string key, Vector3 position, float yawDegrees, string anim, string transitionState)
        {
            if (string.IsNullOrEmpty(key) || !string.IsNullOrEmpty(_seatedSpot) || !AgentReady) return false;
            RememberStandingPose(_agent.nextPosition, transform.eulerAngles.y);
            var fromPosition = _view.PresentationPosition; var fromRotation = transform.rotation;
            StopMovement();
            _seatedSpot = key; _seatLocalOnly = true; _dirty = false;
            _agent.enabled = false;
            var rotation = Quaternion.Euler(0, yawDegrees, 0);
            transform.SetPositionAndRotation(position, rotation);
            _view.SetSpeed(0f);
            _view.BeginSeatEnter(fromPosition, fromRotation, position, rotation, transitionState);
            _view.SetBase(string.IsNullOrEmpty(anim) ? "loco_idle" : anim);
            ResetFootsteps();
            SeatChanged?.Invoke(key);
            return true;
        }

        public bool SeatLocalOnly => _seatLocalOnly;

        private bool BeginLeavePresentation(string spotId, Vector3 seatedPosition, Quaternion seatedRotation)
        {
            if (_view.BeginSeatExitPresentation(spotId, seatedPosition, seatedRotation)) return true; // Codex's fitted café exits
            string clip = _seatLocalOnly ? null : "act_stand_up";
            return _view.BeginSeatLeave(seatedPosition, seatedRotation, transform.position, transform.rotation, clip);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void ObserveReviewCommand(string command)
        {
#if UNITY_EDITOR
            ReviewCommandObserved?.Invoke(command);
#endif
        }
#if UNITY_EDITOR
        /// <summary>Editor-only intent observation; no transport override or fabricated acknowledgement.</summary>
        public event System.Action<string> ReviewCommandObserved;
#endif

        private void Update()
        {
            // Local locomotion still animates when the venue is entered without realtime (offline/review).
            // Server-owned actions such as claiming a seat continue to require a connected channel.
            if (!string.IsNullOrEmpty(_seatedSpot)) { ResetFootsteps(); return; }
            if (_body != null && _body.Airborne) { ResetFootsteps(); _view.SetSpeed(0f); _view.SetBase("jump_air"); return; }
            if (_stunned) { ResetFootsteps(); return; }
            if (!AgentReady) { ResetFootsteps(); return; }
            if (_view.SeatPresentationActive)
            {
                _agent.isStopped = true; _agent.velocity = Vector3.zero;
                ResetFootsteps();
                _view.SetSpeed(0f);
                return; // Logical standing position stays server/NavMesh-owned; presentation traverses independently.
            }
            if (_seatExitWasActive)
            {
                _seatExitWasActive = false;
                ResetFootsteps();
                if (_exitPathQueued)
                {
                    var target = _exitDestination; var spot = _exitThenSit;
                    _exitPathQueued = false; _exitThenSit = null;
                    MoveTo(target, spot);
                }
                else if (!_appPaused && Time.unscaledTime - _inputAt <= .25f && _moveInput.sqrMagnitude > DirectMovementInput.DeadZone * DirectMovementInput.DeadZone)
                    SetMoveInput(_moveInput);
                else StopDirectInput();
            }
            SubmitQueuedDestination();
            var displacement = transform.position - _lastMotionPosition;
            displacement.y = 0f;
            _lastMotionPosition = transform.position;
            ActualVelocity = Time.deltaTime > 0.00001f ? displacement / Time.deltaTime : Vector3.zero;
            // Direct moves happen after this measurement (previous frame, its own delta time): use the body's exact
            // velocity, or uneven frame times read as speed spikes, trip the warp guard and flicker idle/run.
            if (_body != null && _body.Direct && _body.VelocityFrame >= Time.frameCount - 1) ActualVelocity = _body.Velocity;
            // Warps have their own reset path; exclude unexpected correction spikes from animation/audio speed.
            if (ActualVelocity.magnitude > RunSpeed + 1f) ActualVelocity = Vector3.zero;
            float speed = ActualVelocity.magnitude;
            if (_directControl) UpdateDirectMovement();
            RefreshGrounding(false);
            _view.SetSpeed(speed);
            if (_seatedSpot == null)
            {
                // Different enter/exit thresholds avoid repeated crossfades at a wall or the walk/run boundary.
                if (speed < 0.12f) _locomotion = "loco_idle";
                else if (_locomotion == "loco_run" ? speed > 3.25f : speed > 3.65f) _locomotion = "loco_run";
                else if (speed > 0.22f) _locomotion = "loco_walk";
                _view.SetBase(_locomotion);
            }

            var pos = transform.position;
            var travelled = pos - _lastFootstepPosition;
            travelled.y = 0f;
            _lastFootstepPosition = pos;
            if (_footstepCadence.AdvanceAtSpeed(travelled.magnitude, Time.deltaTime, _seatedSpot == null ? speed : 0f))
                AudioManager.Instance?.PlayFootstep(null, pos);   // null = surface under the foot (tag/material/venue floor)
            float yaw = transform.eulerAngles.y;
            if ((pos - _lastSentPos).sqrMagnitude > 0.0004f || Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentRot)) > 2f) _dirty = true;

            if (_dirty && _venue != null)
            {
                ObserveReviewCommand("move");
            }
            if (_dirty && _venue != null && _channel != null && string.IsNullOrEmpty(_requestedSpot))
            {
                var local = _venue.ToLocal(pos);
                if (_channel.Move(local.x, local.y, Mathf.Repeat(yaw, 360f) * Mathf.Deg2Rad))
                {
                    _lastSentPos = pos;
                    _lastSentRot = yaw;
                    _dirty = false;
                }
            }

            if (_hasDestination && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.02f)
            {
                if (_agent.pathStatus == NavMeshPathStatus.PathComplete) ArriveAtDestination();
                else
                {
                    _hasDestination = false; _pathSubmissionQueued = false;
                    _pendingSpot = null;
                }
            }
        }

        private void SubmitQueuedDestination()
        {
            if (!_pathSubmissionQueued) return;
            _pathSubmissionQueued = false; // At most one submission; no per-frame retry loop.
            if (!_hasDestination || _appPaused || _directControl || !AgentReady || !string.IsNullOrEmpty(_seatedSpot))
            {
                _hasDestination = false; _pendingSpot = null;
                return;
            }
            // Revalidate from the actual current point after any intervening callback/correction.
            var path = new NavMeshPath();
            if (!_agent.CalculatePath(_destination, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                _hasDestination = false; _pendingSpot = null;
                _agent.ResetPath(); _agent.isStopped = true;
                ObserveReviewCommand("path-rejected:queued-preflight");
                return;
            }
            if (AtDestination())
            {
                _agent.isStopped = true;
                if (!ArriveAtDestination()) { _hasDestination = false; _pendingSpot = null; }
                return;
            }
            _agent.ResetPath();
            _agent.isStopped = false;
            // Native SetDestination accepts a request, not proof of a completed path. Update still
            // waits for pathPending to clear and arrival keeps its independent complete-path check.
            _hasDestination = _agent.SetDestination(_destination);
            ObserveReviewCommand($"path-assigned:{_hasDestination}; frame={Time.frameCount}; target={_destination:F6}; spot={_pendingSpot}; status={_agent.pathStatus}; hasPath={_agent.hasPath}; " +
                $"remaining={_agent.remainingDistance:F6}; pending={_agent.pathPending}");
            if (!_hasDestination) _pendingSpot = null;
        }

        private bool AtDestination()
        {
            if (!AgentReady) return false;
            var delta = _agent.nextPosition - _destination;
            delta.y = 0f;
            float radius = Mathf.Min(0.1f, _agent.stoppingDistance + 0.02f);
            return delta.sqrMagnitude <= radius * radius;
        }

        private bool ArriveAtDestination()
        {
            if (!AtDestination()) return false;
            var check = new NavMeshPath();
            if (!_agent.CalculatePath(_destination, check) || check.status != NavMeshPathStatus.PathComplete) return false;
            // An early or unvalidated arrival must leave the accepted request available to retry.
            _hasDestination = false; _pathSubmissionQueued = false;
            var spot = _pendingSpot;
            _pendingSpot = null; // Consume only a validated arrival, including an immediate one.
            if (spot == null) return true;
            RememberStandingPose(_agent.nextPosition, transform.eulerAngles.y);
            _releasedSpots.Remove(SpotKey(spot));
            ObserveReviewCommand("sit:" + (_venue != null ? _venue.WireSpotId(spot) : spot));
            if (_channel != null && _channel.IsJoined)
            {
                _requestedSpot = spot;
                SendSit(_venue != null ? _venue.WireSpotId(spot) : spot);
            }
            return true;
        }

        private void UpdateDirectMovement()
        {
            if (_appPaused || Time.unscaledTime - _inputAt > 0.25f) { StopDirectInput(); return; }
            float targetSpeed = DirectMovementInput.Speed(_moveInput.magnitude, ref _running);
            var rig = CameraRig.Instance;
            float yaw = rig != null ? rig.yaw : (Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f);
            var direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(_moveInput.x, 0f, _moveInput.y).normalized;
            _directSpeed = Mathf.MoveTowards(_directSpeed, targetSpeed, 14f * Time.deltaTime);
            var velocity = direction * _directSpeed;
            float dt = Mathf.Max(Time.deltaTime, 1f / 120f);
            var target = _agent.nextPosition + velocity * dt;
            if (_agent.Raycast(target, out var edge))
            {
                // A drop beyond the edge (not a wall): step off it and fall with gravity.
                if (_body != null && velocity.sqrMagnitude > 0.25f && _body.IsLedge(edge.position, velocity, out _, out var stepTo))
                {
                    var fall = velocity;
                    StopDirectInput();
                    _body.BeginAirborne(fall, stepTo);
                    _view.SetBase("jump_air");
                    return;
                }
                // Try moving along the edge instead of oscillating a path destination against a wall.
                velocity = Vector3.ProjectOnPlane(velocity, edge.normal);
                velocity.y = 0f;
                target = _agent.nextPosition + velocity * dt;
                if (_agent.Raycast(target, out _) || velocity.sqrMagnitude < 0.0001f) velocity = Vector3.zero;
            }
            if (_venue != null && !_venue.IsWalkable(_agent.nextPosition + velocity * dt)) velocity = Vector3.zero;
            var facing = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : direction;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 720f * Time.deltaTime);
            // Turn before advancing, including when a wall changes the allowed direction into a slide.
            velocity *= Mathf.Clamp01(Vector3.Dot(transform.forward, facing));
            // Unity's supported player-control path remains constrained to the existing baked NavMesh; the body collides
            // with every solid collider on top of that (props that the NavMesh does not carve).
            if (_body != null && _body.Ready) _body.MoveDirect(velocity, Time.deltaTime);
            else _agent.velocity = velocity;
        }

        private void RefreshGrounding(bool force)
        {
            if (!AgentReady || (!force && Time.unscaledTime < _groundCheckAt)) return;
            _groundCheckAt = Time.unscaledTime + 0.2f;
            // The baked navigation plane is voxelized (~8cm above the authored Timeline paving). Obtain the
            // independent collider height instead of changing shared meshes, authoritative XZ, or source clips.
            if (!NavMesh.SamplePosition(transform.position, out var nav, 0.75f, _agent.areaMask)) return;
            int mask = (_venue != null ? _venue.groundMask.value : ~0) & CharacterLayers.WorldMask;
            if (!Physics.Raycast(nav.position + Vector3.up * 0.35f, Vector3.down, out var floor, 0.85f,
                    mask, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.7f) return;
            float offset = floor.point.y - nav.position.y;
            if (Mathf.Abs(offset) > 0.35f) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _navigationY = nav.position.y;
            _groundY = floor.point.y;
            _groundFound = true;
#endif
            _agent.baseOffset = offset;
        }

        // ------------------------------------------------------------------ physicality

        void IKnockdownOwner.PlaceRoot(Vector3 position, Quaternion rotation)
        {
            if (AgentReady && NavMesh.SamplePosition(position, out var hit, 1.2f, _agent.areaMask)) _agent.Warp(hit.position);
            else transform.position = position;
            transform.rotation = rotation;
            RefreshGrounding(true);
            ResetFootsteps();
            _dirty = true; // publish where the body ended up
        }

        void IKnockdownOwner.SetControl(bool enabled)
        {
            _stunned = !enabled;
            if (_stunned) CancelMovement();
            else { _locomotion = "loco_idle"; ResetFootsteps(); }
        }

        /// <summary>Running into something: a bump staggers, a sprint into it knocks the player down (trips over low props).</summary>
        private void OnImpact(CharacterBody.ImpactInfo hit)
        {
            if (_stunned || !string.IsNullOrEmpty(_seatedSpot)) return;
            var me = Knockdown.For(_view);
            var forward = transform.forward;
            if (hit.other != null && !hit.other.isLocal)
            {
                var them = Knockdown.For(hit.other);
                bool npc = hit.other.GetComponent<NpcController>() != null;
                if (hit.speed >= FallSpeed && npc) them.Fall(forward * hit.speed * 0.5f, forward * 120f);
                else them.Stagger(forward);
                me.Stagger(-hit.normal);
                return;
            }
            if (hit.speed >= FallSpeed)
            {
                bool low = hit.obstacleHeight < 0.9f; // trip over a bench/planter edge; bounce off a wall/counter
                var velocity = low ? forward * hit.speed : hit.normal * 1.2f + Vector3.up * 0.4f;
                var impulse = low ? (forward * 90f + Vector3.down * 40f) : hit.normal * 160f;
                me.Fall(velocity, impulse);
                if (BroadcastFalls && _channel != null && _channel.IsJoined) _channel.Emote("react_liquidated");
            }
            else if (hit.speed >= StaggerSpeed) me.Stagger(hit.normal);
        }

        /// <summary>Landing from a drop: soft (absorb), hard (roll), or a fall that knocks the player down.</summary>
        private void OnLanded(float height)
        {
            _locomotion = "loco_idle";
            _view.SetBase("loco_idle");
            if (height > CharacterBody.HardLanding + 1.0f) Knockdown.For(_view).Fall(Vector3.down * 2f, Vector3.zero);
            else if (height > CharacterBody.HardLanding) _view.PlayOneShot("react_roll");
            else if (height > 0.3f) _view.PlayOneShot("jump_land");
            _dirty = true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string ReviewSnapshot() => $"mode={ControlSettings.MovementMode}; direct={_directControl}; " +
            $"input={_moveInput:F3}; running={_running}; speed={ActualVelocity.magnitude:F3}; position={transform.position:F3}; " +
            $"groundFound={_groundFound}; groundY={_groundY:F4}; navY={_navigationY:F4}; " +
            $"baseOffset={(_agent != null ? _agent.baseOffset : 0f):F4}; groundGap={(transform.position.y - _groundY):F4}; " +
            $"state={_locomotion}; inputEpoch={InputEpoch}; path={_hasDestination}";
#endif
    }
}
