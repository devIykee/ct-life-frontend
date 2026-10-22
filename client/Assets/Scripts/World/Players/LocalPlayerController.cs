using System.Collections.Generic;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// The local player on foot (Feel lane F1, 2026-10-17). Direct camera-relative movement on a CharacterController body
    /// (<see cref="CharacterBody"/>) that walks on whatever solid collision the world has: no NavMesh, no corridors, no
    /// walkable-rect clamp on CT Island. Human gaits: walk up to 1.6 m/s, jog 3.5 m/s, sprint 6 m/s with the stick at its
    /// edge. Seat walks (tap a seat, interactions) follow NavMesh corners where a NavMesh happens to exist (venue
    /// interiors), else a straight line; the body still collides either way.
    /// Sends `move {x, z, rotY}` at most 10 times a second (contract section 5) while inside a room's venue, plus one final
    /// update on arrival. Positions are venue-local metres and rotY is the yaw in radians; the sent position is paced to
    /// stay under the room's 6 m/s check while sprinting.
    /// </summary>
    [RequireComponent(typeof(AvatarView))]
    public sealed class LocalPlayerController : MonoBehaviour, IKnockdownOwner
    {
        public const float WalkSpeed = DirectMovementInput.WalkSpeed;
        public const float RunSpeed = DirectMovementInput.JogSpeed;
        public const float SprintSpeed = DirectMovementInput.SprintSpeed;
        /// <summary>Seat walks longer than this jog.</summary>
        public const float RunIfFartherThan = 9f;
        /// <summary>The room rejects moves faster than 6 m/s between two messages; the sent position never outruns this.</summary>
        public const float ServerPace = 5f;
        /// <summary>Path arrival: exact, or close and no longer getting closer (a seat's approach point inside a chair).</summary>
        public const float ArriveRadius = 0.06f, ArriveNear = 0.4f;

        private AvatarView _view;
        private VenueSceneRoot _venue;
        private VenueChannel _channel;
        private Vector3 _lastSentPos;
        private float _lastSentRot, _lastSentAt;
        private bool _dirty, _paceFresh = true;
        private string _pendingSpot;
        private string _seatedSpot;
        private bool _hasDestination;
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

        // Path walking (seat approaches).
        private readonly List<Vector3> _path = new List<Vector3>();
        private int _pathIndex, _replans;
        private float _pathSpeed, _pathBest, _pathBestAt;

        private readonly FootstepCadence _footstepCadence = new FootstepCadence();
        private Vector3 _lastFootstepPosition;
        private Vector2 _moveInput;
        private Gait _gait;
        private bool _directControl, _appPaused;
        private float _inputAt, _directSpeed;
        private string _locomotion = "loco_idle";

        public Vector3 ActualVelocity { get; private set; }
        public bool DirectInputActive => _directControl || (_view != null && _view.SeatPresentationActive && _moveInput.sqrMagnitude > DirectMovementInput.DeadZone * DirectMovementInput.DeadZone);
        /// <summary>Jogging or sprinting (the HUD's gait hint).</summary>
        public bool IsRunning => _gait >= Gait.Jog;
        public Gait Gait => _gait;
        /// <summary>
        /// CT Island (Phase B): no per-venue walkable rect, and the room/venue are the current zone's, swapped by
        /// <see cref="SetRoom"/> as the player walks between venues.
        /// </summary>
        public bool OpenWorld { get; set; }

        /// <summary>Rebind to another zone's venue and room (null channel on the street); publishes the position to it.</summary>
        public void SetRoom(VenueSceneRoot venue, VenueChannel channel)
        {
            CancelRequestedSeat();
            _venue = venue;
            _channel = channel;
            _dirty = channel != null;
            _paceFresh = true; // a new room has no previous position to pace from
            CharacterLayers.MarkTapTargets(venue);
        }
        private bool _braking;
        private float _turnVelocity;
        /// <summary>Feel probe and tests: coasting to a stop after the stick was released.</summary>
        public bool Braking => _braking;

        public int InputEpoch { get; private set; }
        private bool BodyReady => _body != null && _body.Ready && !_body.Suspended;

        public bool IsMoving => _hasDestination || ActualVelocity.sqrMagnitude > 0.01f;
        public string SeatedSpot => _seatedSpot;
        /// <summary>A walk is in progress (path mode).</summary>
        public bool HasDestination => _hasDestination;
        /// <summary>The seat this client holds changed (spot id, or null when standing). Server-confirmed or local-only.</summary>
        public event System.Action<string> SeatChanged;
        /// <summary>The server rejected this client's latest `sit`.</summary>
        public event System.Action SeatRejected;
        private bool _seatLocalOnly;
        // Physicality: the body and knockdowns (only big falls and, later, vehicles knock the player down).
        private CharacterBody _body;
        private bool _stunned;
        /// <summary>Show falls to the room through the existing `emote` message (react_liquidated is a registry anim).</summary>
        public static bool BroadcastFalls = true;
        public CharacterBody Body => _body;
        public bool Stunned => _stunned;

        /// <summary>Driving or flying (Feel lane F2): the vehicle carries the avatar; no walking, no `move` messages.</summary>
        public bool InVehicle { get; private set; }
        public bool Recovering { get; private set; }
        public void SetRecoveryPaused(bool paused)
        {
            Recovering = paused;
            CancelMovement();
            _body?.Suspend(paused || InVehicle || _stunned || !string.IsNullOrEmpty(_seatedSpot));
            ResetFootsteps();
        }

        /// <summary>Hand the avatar to a vehicle seat (VehicleSystem parents it there).</summary>
        public void EnterVehicleMode()
        {
            CancelMovement();
            StandIfSeated();
            InVehicle = true;
            _body?.Suspend(true);
            ResetFootsteps();
        }

        /// <summary>Back on foot at <paramref name="position"/>, facing <paramref name="yawDegrees"/>.</summary>
        public void ExitVehicleMode(Vector3 position, float yawDegrees)
        {
            InVehicle = false;
            var rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            if (_body != null) { _body.Suspend(false); _body.Teleport(position, rotation); }
            else transform.SetPositionAndRotation(position, rotation);
            _locomotion = "loco_idle";
            _view?.SetBase("loco_idle");
            _dirty = true; _paceFresh = true;
            ResetFootsteps();
            GetComponent<LocalPlayerMarker>()?.Pulse();
        }

        public void Init(VenueSceneRoot venue, VenueChannel channel)
        {
            _venue = venue;
            _channel = channel;
            CharacterLayers.MarkTapTargets(venue);
            _view = GetComponent<AvatarView>();
            if (venue != null) _view.BindSeatMotionProfile(venue.GetComponent<CafeSeatMotionProfile>());
            _view.isLocal = true;
            // The player never walks on a NavMeshAgent any more (a stale one would fight the body for the transform).
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) Destroy(agent);
            if (_body == null)
            {
                _body = GetComponent<CharacterBody>();
                if (_body == null) _body = gameObject.AddComponent<CharacterBody>();
                _body.Landed += OnLanded;
                _body.BumpedPerson += OnBumpedPerson;
            }
            _body.Suspend(false);
            _body.Teleport(transform.position, transform.rotation);
            _lastSentPos = transform.position;
            _lastSentRot = transform.eulerAngles.y;
            // Publish the spawn position once: until the first `move` the server shows this player at its default.
            _dirty = channel != null;
            _paceFresh = true;
            ResetFootsteps();
            BindListener();
            var safety = GetComponent<PlayerSafety>();
            if (safety == null) safety = gameObject.AddComponent<PlayerSafety>();
            safety.Init(this);
            if (OpenWorld) CTLife.World.Vehicles.VehicleSystem.Ensure(this);   // parked vehicles, the drone (Feel lane F2)
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
            ActualVelocity = Vector3.zero;
        }

        private void OnApplicationPause(bool paused) { _appPaused = paused; if (paused) { CancelMovement(); _view?.CancelSeatExitPresentation(); _seatExitWasActive = false; } }
        private void OnApplicationFocus(bool focused) { if (!focused) { CancelMovement(); _view?.CancelSeatExitPresentation(); _seatExitWasActive = false; } }

        /// <summary>HUD axes: right/up on screen. Refresh while held; a missing producer times out safely.</summary>
        /// <summary>Hop speed (m/s): about 0.6 m, a modest jump rather than a superhero leap.</summary>
        public const float JumpSpeed = 3.4f;

        /// <summary>City HUD v2 Jump: a small hop when standing free on the ground (not seated, driving, stunned or recovering).</summary>
        public bool RequestJump()
        {
            if (!string.IsNullOrEmpty(_seatedSpot) || InVehicle || Recovering || _stunned || _body == null || _view == null) return false;
            if (_view.SeatPresentationActive || _body.Airborne) return false;
            if (!_body.Jump(JumpSpeed)) return false;
            _view.SetBase("jump_air");
            _dirty = true;
            return true;
        }

        public void SetMoveInput(Vector2 input)
        {
            if (_appPaused || !isActiveAndEnabled || InVehicle || Recovering) return;
            if (_stunned || (_body != null && _body.Airborne)) { _moveInput = Vector2.zero; return; } // down or falling: no steering
            if (float.IsNaN(input.x) || float.IsInfinity(input.x) || float.IsNaN(input.y) || float.IsInfinity(input.y))
                input = Vector2.zero;
            input = Vector2.ClampMagnitude(input, 1f);
            if (input.magnitude <= DirectMovementInput.DeadZone)
            {
                // Releasing the stick brakes over a few frames (Locomotion.Brake) instead of stopping dead.
                if (_directControl && !(_view != null && _view.SeatPresentationActive)) { _braking = true; _moveInput = Vector2.zero; _gait = Gait.Stop; _inputAt = Time.unscaledTime; }
                else if (_directControl || (_view != null && _view.SeatPresentationActive)) StopDirectInput();
                return;
            }
            _braking = false;
            StandIfSeated(); // A confirmed seat suspends the body.
            if (_view != null && _view.SeatPresentationActive)
            {
                _moveInput = input; _inputAt = Time.unscaledTime;
                _exitPathQueued = false; _exitThenSit = null;
                return; // Accept/replace immediately; translating a seated body is not the response.
            }
            if (!BodyReady) return;
            if (!_directControl)
            {
                CancelRequestedSeat();
                ClearPath();
                _pendingSpot = null;
                _directControl = true;
            }
            _moveInput = input;
            _inputAt = Time.unscaledTime;
        }

        /// <summary>Release a thumb without cancelling an unrelated, intentional seat/review path.</summary>
        public void StopDirectInput()
        {
            _braking = false;
            _turnVelocity = 0f;
            _moveInput = Vector2.zero;
            _gait = Gait.Stop;
            if (!_directControl) return;
            _directSpeed = 0f;
            _directControl = false;
            _dirty = true; // Publish the final accepted position even after a very short movement.
        }

        /// <summary>UI, pause, travel, seat confirmation or correction cancels all pending local movement.</summary>
        public void CancelMovement()
        {
            CancelRequestedSeat();
            StopMovement();
        }

        private void StopMovement()
        {
            _exitPathQueued = false; _exitThenSit = null;
            InputEpoch++;
            StopDirectInput();
            _directSpeed = 0f;
            ClearPath();
            _pendingSpot = null;
        }

        private void ClearPath()
        {
            _hasDestination = false;
            _path.Clear();
            _pathIndex = 0;
        }

        /// <summary>
        /// Walk to a world point (inside the venue's walkable rectangles off the island). The route follows NavMesh corners
        /// when a NavMesh covers both ends, else a straight line; arrival optionally claims a seat.
        /// </summary>
        public bool MoveTo(Vector3 worldPoint, string thenSitAt = null)
        {
            if (Recovering) return false;
            if (_appPaused) { ObserveReviewCommand("path-rejected:paused"); return false; }
            if (InVehicle) { ObserveReviewCommand("path-rejected:in-vehicle"); return false; }
            StandIfSeated();
            if (!BodyReady) { ObserveReviewCommand("path-rejected:body-not-ready"); return false; }
            var target = worldPoint;
            if (CharacterBody.GroundAt(target, out float groundY)) target.y = groundY;
            if (!OpenWorld && _venue != null && !_venue.IsWalkable(target))
            { ObserveReviewCommand("path-rejected:outside-venue"); return false; }
            var route = Walkable(transform.position, PlanRoute(transform.position, target));
            ObserveReviewCommand($"path-planned:corners={route.Count}; actor={transform.position:F3}; target={target:F3}");
            if (_view != null && _view.SeatPresentationActive)
            {
                StopDirectInput(); _exitPathQueued = true; _exitDestination = target; _exitThenSit = thenSitAt;
                return true; // The newest path replaces pending direct/path intent while rise/step completes.
            }
            CancelRequestedSeat();
            StopMovement();
            _pendingSpot = thenSitAt;
            _destination = target;
            if (Flat(target - transform.position).magnitude <= ArriveRadius) return ArriveAtDestination();
            _path.AddRange(route);
            _pathIndex = 0;
            float length = 0f; var from = transform.position;
            foreach (var p in route) { length += Flat(p - from).magnitude; from = p; }
            _pathSpeed = length > RunIfFartherThan ? RunSpeed : WalkSpeed;
            _pathBest = float.MaxValue; _pathBestAt = Time.time; _replans = 0;
            _hasDestination = true;
            ObserveReviewCommand($"path-queued:frame={Time.frameCount}; target={_destination:F3}; spot={_pendingSpot}");
            return true;
        }

        /// <summary>Corners to walk through to <paramref name="to"/> (the last one is the target itself).</summary>
        public static List<Vector3> PlanRoute(Vector3 from, Vector3 to)
        {
            var route = new List<Vector3>();
            if (NavMesh.SamplePosition(from, out var a, 1f, NavMesh.AllAreas) && NavMesh.SamplePosition(to, out var b, 0.6f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    for (int i = 1; i < path.corners.Length - 1; i++) route.Add(path.corners[i]);
            }
            route.Add(to);
            return route;
        }

        /// <summary>The route with every leg a body capsule cannot walk straight replaced by a way round (FootPlanner).</summary>
        public static List<Vector3> Walkable(Vector3 from, List<Vector3> route)
        {
            var walkable = new List<Vector3>(route.Count + 4);
            var prev = from;
            for (int i = 0; i < route.Count; i++)
            {
                var corner = route[i];
                // A NavMesh corner inside an uncarved prop is no corner at all (casts starting inside it see nothing).
                if (i < route.Count - 1 && !FootPlanner.Free(corner)) continue;
                var detour = FootPlanner.Clear(prev, corner) ? null : FootPlanner.Route(prev, corner);
                if (detour != null) walkable.AddRange(detour); else walkable.Add(corner);
                prev = corner;
            }
            return walkable;
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
            if (_body == null || !_hasStandingPose) return false;
            if (!OpenWorld && _venue != null && !_venue.IsWalkable(_standingPosition)) return false;
            _body.Suspend(false);
            _body.Teleport(_standingPosition, Quaternion.Euler(0f, _standingYaw, 0f));
            ClearPath();
            _locomotion = "loco_idle";
            ResetFootsteps();
            // Keep _lastSentPos: it is the last walking coordinate actually offered to the channel.
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
            var rotation = Quaternion.Euler(0, rotYRad * Mathf.Rad2Deg, 0);
            if (_body != null) _body.Teleport(worldPos, rotation);
            else transform.SetPositionAndRotation(worldPos, rotation);
            _lastSentPos = transform.position;
            _lastSentRot = transform.eulerAngles.y;
            _lastSentAt = Time.unscaledTime;
            _dirty = false;
            ResetFootsteps();
            GetComponent<LocalPlayerMarker>()?.Pulse();
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
                if (BodyReady) RememberStandingPose(transform.position, transform.eulerAngles.y);
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
            _body?.Suspend(true);
            var seatRotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0);
            transform.SetPositionAndRotation(p, seatRotation);
            _view.SetSpeed(0f);
            // Sit down from where the player stands (approach point) instead of popping onto the seat.
            if (wasStanding) _view.BeginSeatEnter(fromPosition, fromRotation, p, seatRotation);
            var presentation = OccupiedSpotAnimation.Resolve(_venue, spotId, anim);
            _view.SetBase(string.IsNullOrEmpty(presentation) ? "act_sit" : presentation, string.IsNullOrEmpty(presentation) ? null : spotId);
            ResetFootsteps();
            if (wasStanding) CameraRig.Instance?.SettleBehind();
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
            if (BodyReady) RememberStandingPose(transform.position, transform.eulerAngles.y);
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
                if (_channel.Move(local.x, local.y, Mathf.Repeat(yaw - _venue.transform.eulerAngles.y, 360f) * Mathf.Deg2Rad))
                {
                    _lastSentPos = transform.position;
                    _lastSentRot = yaw;
                    _lastSentAt = Time.unscaledTime;
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
            if (string.IsNullOrEmpty(key) || !string.IsNullOrEmpty(_seatedSpot) || !BodyReady) return false;
            RememberStandingPose(transform.position, transform.eulerAngles.y);
            var fromPosition = _view.PresentationPosition; var fromRotation = transform.rotation;
            StopMovement();
            _seatedSpot = key; _seatLocalOnly = true; _dirty = false;
            _body.Suspend(true);
            var rotation = Quaternion.Euler(0, yawDegrees, 0);
            transform.SetPositionAndRotation(position, rotation);
            _view.SetSpeed(0f);
            _view.BeginSeatEnter(fromPosition, fromRotation, position, rotation, transitionState);
            _view.SetBase(string.IsNullOrEmpty(anim) ? "loco_idle" : anim);
            ResetFootsteps();
            CameraRig.Instance?.SettleBehind();
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

        private void MoveBody(Vector3 velocity, float dt)
        {
            if (_body == null) return;
            _body.Move(_body.Grounded ? Locomotion.Separate(transform, velocity) : velocity, dt);
        }

        private void Update()
        {
            // Local locomotion still animates when the venue is entered without realtime (offline/review).
            // Server-owned actions such as claiming a seat continue to require a connected channel.
            if (!string.IsNullOrEmpty(_seatedSpot) || InVehicle || Recovering) { ResetFootsteps(); return; }
            if (_stunned || _body == null || _view == null) { ResetFootsteps(); return; }
            if (_view.SeatPresentationActive)
            {
                ResetFootsteps();
                _view.SetSpeed(0f);
                return; // The logical standing position stays put; the presentation traverses independently.
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
            float dt = Time.deltaTime;
            if (_directControl) UpdateDirectMovement(dt);
            else if (_hasDestination) UpdatePathMovement(dt);
            else MoveBody(Vector3.zero, dt); // gravity and ground contact while standing
            ActualVelocity = _body.VelocityFrame == Time.frameCount ? _body.Velocity : Vector3.zero;
            // Warps have their own reset path; exclude unexpected correction spikes from animation/audio speed.
            if (ActualVelocity.magnitude > SprintSpeed + 1.5f) ActualVelocity = Vector3.zero;
            float speed = ActualVelocity.magnitude;
            var pos = transform.position;
            if (_body.Airborne)
            {
                ResetFootsteps();
                _view.SetSpeed(0f);
                _view.SetBase("jump_air");
            }
            else
            {
                // Different enter/exit thresholds avoid repeated crossfades at a wall or a gait boundary.
                if (speed < 0.12f) _locomotion = "loco_idle";
                else if (_locomotion == "loco_run" ? speed > 2.4f : speed > 2.8f) _locomotion = "loco_run";
                else if (speed > 0.22f) _locomotion = "loco_walk";
                _view.SetSpeed(speed);
                _view.SetBase(_locomotion);
                var travelled = pos - _lastFootstepPosition;
                travelled.y = 0f;
                _lastFootstepPosition = pos;
                if (_footstepCadence.AdvanceAtSpeed(travelled.magnitude, dt, speed))
                    AudioManager.Instance?.PlayFootstep(null, pos);   // null = surface under the foot (tag/material/venue floor)
            }
            CameraRig.Instance?.SetInterior(!OpenWorld);
            float yaw = transform.eulerAngles.y;
            if ((pos - _lastSentPos).sqrMagnitude > 0.0004f || Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentRot)) > 2f) _dirty = true;
            if (_dirty && _venue != null) ObserveReviewCommand("move");
            // Open world: the room only accepts positions inside its venue's walkable rect; on the street nothing is sent.
            if (_dirty && _venue != null && _channel != null && string.IsNullOrEmpty(_requestedSpot) && (!OpenWorld || _venue.IsWalkable(pos)))
            {
                var send = _paceFresh ? pos : PacedTowards(_lastSentPos, pos, Time.unscaledTime - _lastSentAt);
                var local = _venue.ToLocal(send);
                if (_channel.Move(local.x, local.y, Mathf.Repeat(yaw, 360f) * Mathf.Deg2Rad))
                {
                    _lastSentPos = send;
                    _lastSentRot = yaw;
                    _lastSentAt = Time.unscaledTime;
                    _paceFresh = false;
                    _dirty = Flat(pos - send).sqrMagnitude > 0.0004f; // still catching up after a sprint
                }
            }
        }

        /// <summary>
        /// The position to send after <paramref name="elapsed"/> seconds: the real one, or as far toward it as
        /// <see cref="ServerPace"/> allows from the last sent one (the room computes speed between two arrivals).
        /// </summary>
        public static Vector3 PacedTowards(Vector3 lastSent, Vector3 actual, float elapsed)
        {
            float max = ServerPace * Mathf.Clamp(elapsed, 0.1f, 0.5f);
            var d = Flat(actual - lastSent);
            if (d.magnitude <= max) return actual;
            var p = lastSent + d.normalized * max;
            p.y = actual.y;
            return p;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private void UpdatePathMovement(float dt)
        {
            if (_appPaused || _path.Count == 0) { ClearPath(); MoveBody(Vector3.zero, dt); return; }
            var pos = transform.position;
            bool last = _pathIndex >= _path.Count - 1;
            var to = Flat(_path[_pathIndex] - pos);
            float dist = to.magnitude;
            if (!last && dist < 0.15f) { _pathIndex++; to = Flat(_path[_pathIndex] - pos); dist = to.magnitude; last = _pathIndex >= _path.Count - 1; _pathBest = float.MaxValue; }
            if (last)
            {
                if (dist < _pathBest - 0.02f) { _pathBest = dist; _pathBestAt = Time.time; }
                bool stalled = Time.time - _pathBestAt > 0.3f;
                if (dist <= ArriveRadius || (dist <= ArriveNear && stalled))
                {
                    _directSpeed = 0f;
                    MoveBody(Vector3.zero, dt);
                    if (!ArriveAtDestination()) { ClearPath(); _pendingSpot = null; }
                    return;
                }
            }
            else if (dist < _pathBest - 0.02f) { _pathBest = dist; _pathBestAt = Time.time; }
            if (Time.time - _pathBestAt > 0.6f && _replans < 2)
            {
                // Held up by something the plan missed: plan again from here (FootPlanner sees what is round us now).
                _replans++;
                var goal = _path[_path.Count - 1];
                _path.Clear(); _path.AddRange(Walkable(pos, PlanRoute(pos, goal)));
                _pathIndex = 0; _pathBest = float.MaxValue; _pathBestAt = Time.time;
                ObserveReviewCommand($"path-replanned:{_replans}; corners={_path.Count}");
                MoveBody(Vector3.zero, dt);
                return;
            }
            if (Time.time - _pathBestAt > 1.5f)
            {
                // Blocked (a person, a prop the route did not know): give up; the interaction reports it.
                ObserveReviewCommand("path-stuck");
                ClearPath(); _pendingSpot = null; _directSpeed = 0f;
                MoveBody(Vector3.zero, dt);
                return;
            }
            if (dist > 1e-4f) to /= dist;
            // A route corner can sit behind a prop the NavMesh never carved (benches, planters): feel ahead and go round it.
            to = SteerAround(pos, to, Mathf.Min(dist, Mathf.Max(0.7f, _directSpeed * 0.45f)));
            float target = last ? Mathf.Min(_pathSpeed, Mathf.Max(0.5f, dist * 2.5f)) : _pathSpeed; // ease into the approach point
            _directSpeed = Locomotion.Accelerate(_directSpeed, target, dt);
            Turn(to, dt);
            var velocity = to * (_directSpeed * Mathf.Clamp01(Vector3.Dot(transform.forward, to) * 1.5f));
            MoveBody(velocity, dt);
        }

        private static readonly float[] SteerAngles = { 25f, 50f, 75f, 100f, 130f };

        /// <summary>
        /// The walking direction toward <paramref name="dir"/> that a body-sized capsule (above kerb height) can travel for
        /// <paramref name="ahead"/> metres: the direct one if clear, else the nearest clear bearing, turning first toward the
        /// side the obstacle's face leads to; the face's tangent when nothing is clear.
        /// </summary>
        public static Vector3 SteerAround(Vector3 pos, Vector3 dir, float ahead)
        {
            if (ahead <= 0.05f || dir.sqrMagnitude < 1e-6f) return dir;
            var bottom = pos + Vector3.up * (CharacterBody.StepOffset + CharacterBody.Radius);
            var top = pos + Vector3.up * (CharacterBody.Height - CharacterBody.Radius);
            float r = CharacterBody.Radius * 0.95f;
            if (!Physics.CapsuleCast(bottom, top, r, dir, out var hit, ahead, CharacterBody.Mask, QueryTriggerInteraction.Ignore)) return dir;
            var n = Flat(hit.normal);
            var tangent = n.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, n.normalized) : Vector3.Cross(Vector3.up, dir);
            if (Vector3.Dot(tangent, dir) < 0f) tangent = -tangent;
            float side = Vector3.SignedAngle(dir, tangent, Vector3.up) >= 0f ? 1f : -1f;
            foreach (float a in SteerAngles)
                foreach (float s in new[] { side, -side })
                {
                    var d = Quaternion.Euler(0f, s * a, 0f) * dir;
                    if (!Physics.CapsuleCast(bottom, top, r, d, ahead * 0.8f, CharacterBody.Mask, QueryTriggerInteraction.Ignore)) return d;
                }
            return tangent.normalized;
        }

        private bool ArriveAtDestination()
        {
            ClearPath();
            var spot = _pendingSpot;
            _pendingSpot = null;
            _dirty = true;
            if (spot == null) return true;
            RememberStandingPose(transform.position, transform.eulerAngles.y);
            _releasedSpots.Remove(SpotKey(spot));
            ObserveReviewCommand("sit:" + (_venue != null ? _venue.WireSpotId(spot) : spot));
            if (_channel != null && _channel.IsJoined)
            {
                _requestedSpot = spot;
                SendSit(_venue != null ? _venue.WireSpotId(spot) : spot);
            }
            return true;
        }

        private void Turn(Vector3 facing, float dt)
        {
            if (facing.sqrMagnitude < 1e-6f) return;
            float current = transform.eulerAngles.y, goal = Quaternion.LookRotation(facing).eulerAngles.y;
            float next = Mathf.SmoothDampAngle(current, goal, ref _turnVelocity, Locomotion.TurnSmoothTime, Locomotion.TurnRateAt(_directSpeed), dt);
            transform.rotation = Quaternion.Euler(0f, next, 0f);
        }

        private void UpdateDirectMovement(float dt)
        {
            if (_appPaused) { StopDirectInput(); MoveBody(Vector3.zero, dt); return; }
            Vector3 direction;
            if (_braking)
            {
                // Coast to a stop along the current heading (no input producer needed while braking).
                _directSpeed = Locomotion.Brake(_directSpeed, dt);
                if (_directSpeed <= Locomotion.StopBelow) { StopDirectInput(); MoveBody(Vector3.zero, dt); return; }
                direction = transform.forward;
            }
            else
            {
                if (Time.unscaledTime - _inputAt > 0.25f) { StopDirectInput(); MoveBody(Vector3.zero, dt); return; }
                float targetSpeed = DirectMovementInput.Speed(_moveInput.magnitude, ref _gait);
                var rig = CameraRig.Instance;
                float yaw = rig != null ? rig.yaw : (Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f);
                direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(_moveInput.x, 0f, _moveInput.y).normalized;
                // A hard reversal sheds speed first (plant and turn) instead of pivoting a sprint on the spot.
                if (_directSpeed > RunSpeed && Vector3.Dot(transform.forward, direction) < -0.2f) targetSpeed = Mathf.Min(targetSpeed, WalkSpeed);
                _directSpeed = Locomotion.Accelerate(_directSpeed, targetSpeed, dt);
            }
            var velocity = direction * _directSpeed;
            if (!OpenWorld && _venue != null && !_venue.IsWalkable(transform.position + velocity * dt))
            {
                // Homes: the room's walkable rectangle is the server's bound; glide along its edge rather than freezing.
                var from = transform.position; var vt = _venue.transform;
                var local = Locomotion.SlideAlongEdge(vt.InverseTransformDirection(velocity), v => _venue.IsWalkable(from + vt.TransformDirection(v) * dt));
                velocity = vt.TransformDirection(local);
            }
            var facing = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : direction;
            // Eased turn: the turn rate ramps in and out, capped lower at a sprint (wider arcs).
            Turn(facing, dt);
            // Turn before advancing, including when a wall changes the allowed direction into a slide.
            velocity *= Mathf.Clamp01(Vector3.Dot(transform.forward, facing));
            MoveBody(velocity, dt);
        }

        // ------------------------------------------------------------------ physicality

        void IKnockdownOwner.PlaceRoot(Vector3 position, Quaternion rotation)
        {
            if (_body != null) _body.Teleport(position, rotation);
            else transform.SetPositionAndRotation(position, rotation);
            ResetFootsteps();
            _dirty = true; // publish where the body ended up
        }

        void IKnockdownOwner.SetControl(bool enabled)
        {
            _stunned = !enabled;
            if (_stunned) CancelMovement();
            else { _locomotion = "loco_idle"; ResetFootsteps(); }
            if (_body != null && string.IsNullOrEmpty(_seatedSpot)) _body.Suspend(_stunned);
        }

        /// <summary>Walking into someone: a sprint nudges an NPC aside; the player only loses pace, never falls.</summary>
        private void OnBumpedPerson(AvatarView other, float speed)
        {
            if (_stunned || !string.IsNullOrEmpty(_seatedSpot) || other == null || other.isLocal) return;
            if (speed >= 4.5f && other.GetComponent<NpcController>() != null) Knockdown.For(other).Stagger(transform.forward);
            _directSpeed = Mathf.Min(_directSpeed, WalkSpeed);
        }

        /// <summary>
        /// Landing after a real fall (more than 0.5 m, see <see cref="FallTracker"/>): on the move the run simply carries on
        /// (a big one rolls); landing still absorbs briefly; only a very big fall knocks the player down.
        /// </summary>
        private void OnLanded(float height)
        {
            _dirty = true;
            if (height > CharacterBody.KnockdownLanding)
            {
                Knockdown.For(_view).Fall(Vector3.down * 2f, Vector3.zero);
                if (BroadcastFalls && _channel != null && _channel.IsJoined) _channel.Emote("react_liquidated");
                return;
            }
            bool moving = _directControl && _directSpeed > 1f;
            if (height > CharacterBody.HardLanding) _view.PlayOneShot(moving ? "react_roll" : "jump_land", 0f, moving ? -1f : 0.7f);
            else if (!moving) _view.PlayOneShot("jump_land", 0f, 0.45f);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string ReviewSnapshot() => $"mode={ControlSettings.MovementMode}; direct={_directControl}; " +
            $"input={_moveInput:F3}; gait={_gait}; speed={ActualVelocity.magnitude:F3}; position={transform.position:F3}; " +
            $"grounded={(_body != null && _body.Grounded)}; airborne={(_body != null && _body.Airborne)}; " +
            $"state={_locomotion}; inputEpoch={InputEpoch}; path={_hasDestination}";
#endif
    }
}
