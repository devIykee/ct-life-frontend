using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>The CTA the HUD shows now (empty = nothing to do here).</summary>
    public struct InteractionPromptSignal
    {
        public List<InteractionAction> actions;
        public InteractionKind? kind;
        public bool busy;
        public string targetId;
        public InteractionController owner;
        public InteractionPromptAnchor anchor;
    }

    /// <summary>Presentation only: a fixed spot, or an actor root (never an animated head/hand bone).</summary>
    public readonly struct InteractionPromptAnchor
    {
        public readonly Vector3 position;
        public readonly Transform actor;
        public readonly bool followsActor;
        public readonly bool valid;

        public InteractionPromptAnchor(Vector3 position, Transform actor = null, bool followsActor = false)
        { this.position = position; this.actor = actor; this.followsActor = followsActor; valid = true; }

        public bool TryGetPosition(out Vector3 world)
        {
            world = position;
            if (!valid || (followsActor && actor == null)) return false;
            if (followsActor) world += actor.position;
            return true;
        }

        public static InteractionPromptAnchor For(InteractionMachine machine, Interactable offered, Transform actor)
        {
            var target = machine.Phase == InteractionPhase.Idle ? offered : machine.Target;
            if (target == null) return default;
            if (machine.Phase == InteractionPhase.Engaged)
                return new InteractionPromptAnchor(Vector3.up * (target.Sits ? 1.65f : 2.1f), actor, true);
            return new InteractionPromptAnchor(target.position + Vector3.up * 0.8f);
        }
    }

    /// <summary>A short message for the player (UI Strings key), e.g. the seat could not be reached.</summary>
    public struct InteractionToastSignal { public string key; }

    /// <summary>The door interaction: leave through the map (the HUD's Explore action).</summary>
    public struct OpenMapRequestSignal { }

    /// <summary>
    /// Player-chosen interactions on the local player. Offers the best nearby interactable as a CTA (and highlights it in
    /// the world); nothing happens until the player presses it. Then: walk to the approach point, claim the spot
    /// (`sit`, so every client sees it through PlayerState.spotId/anim), sit down; "Stand up" releases (`stand`).
    /// Local-only kinds (counter) and rooms without a server engage on this client only. NPCs and other players
    /// occupying a seat make it unavailable.
    /// </summary>
    [RequireComponent(typeof(LocalPlayerController))]
    [DefaultExecutionOrder(100)] // Project the contextual prompt after CameraRig/AvatarView's default-order LateUpdate.
    public sealed class InteractionController : MonoBehaviour
    {
        public static InteractionController Current { get; private set; }
        public static event Action PresentationUpdated;

        public InteractionMachine Machine { get; } = new InteractionMachine();
        public Interactable Offered { get; private set; }
        public IReadOnlyList<Interactable> Catalog => _catalog;
        public SeatOccupancy Occupancy => _occupancy;

        private LocalPlayerController _local;
        private AvatarView _view;
        private VenueSceneRoot _venue;
        private VenueChannel _channel;
        private VenueSession _session;
        private List<Interactable> _catalog = new List<Interactable>();
        private SeatOccupancy _occupancy = new SeatOccupancy(null);
        private Interactable _focused;
        private InteractionHighlight _highlight;
        private float _nextScan, _nextOccupancy;
        private double _walkIssuedAt;
        private bool _wasDirect;
        private string _lastPromptKey;

        private double Now => Time.realtimeSinceStartupAsDouble;
        private bool Online => _channel != null && _channel.IsJoined;
        private string Me => _channel != null ? _channel.SessionId : "local";

        public void Init(VenueSceneRoot venue, VenueChannel channel, VenueSession session)
        {
            _local = GetComponent<LocalPlayerController>();
            _view = GetComponent<AvatarView>();
            _venue = venue; _channel = channel; _session = session;
            _catalog = InteractableCatalog.Build(venue);
            _occupancy = new SeatOccupancy(venue != null ? venue.venueId : null);
            _local.SeatChanged += OnSeatChanged;
            _local.SeatRejected += OnRejected;
            _highlight = InteractionHighlight.Create(transform.parent);
            Current = this;
            Publish(force: true);
        }

        private void OnDestroy()
        {
            if (_local != null) { _local.SeatChanged -= OnSeatChanged; _local.SeatRejected -= OnRejected; }
            if (_highlight != null) Destroy(_highlight.gameObject);
            if (Current == this)
            {
                Current = null;
                Bus.Publish(new InteractionPromptSignal { actions = new List<InteractionAction>() });
            }
        }

        // ------------------------------------------------------------------ player choices (HUD)

        /// <summary>A CTA button was pressed: "use", "stand", "cancel" or "emote:&lt;id&gt;".</summary>
        public void Perform(string actionId)
        {
            switch (actionId)
            {
                case "use": Exec(Machine.Use(Offered, Online, Now)); _focused = null; break;
                case "stand": Exec(Machine.StandUp()); break;
                case "cancel": Exec(Machine.Cancel()); break;
                default: Exec(Machine.Secondary(actionId)); break;
            }
            Publish(force: true);
        }

        /// <summary>A rendered button may outlive its offered target by one frame; never act on another object.</summary>
        public void Perform(string actionId, string expectedTargetId)
        {
            if (Current != this || !Machine.AcceptsPrompt(actionId, expectedTargetId, Offered)) return;
            if (actionId == "use")
            {
                RefreshOccupancy();
                if (!_occupancy.IsFree(Offered, Me)) { _nextScan = 0f; return; }
            }
            Perform(actionId);
        }

        /// <summary>A tapped seat/terminal becomes the offered target (from farther away) — it does not sit by itself.</summary>
        public bool Focus(SpotAnchor spot)
        {
            if (spot == null || Machine.Phase != InteractionPhase.Idle) return false;
            var key = _occupancy.Key(spot.spotId);
            foreach (var it in _catalog)
                if (it.id == key && _occupancy.IsFree(it, Me)) { _focused = it; _nextScan = 0f; return true; }
            return false;
        }

        public Interactable Find(string spotId)
        {
            var key = _occupancy.Key(spotId);
            foreach (var it in _catalog) if (it.id == key) return it;
            return null;
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            if (_local == null) return;
            Exec(Machine.Tick(Now));

            // The stick/arrows cancel an approach or a pending claim (a seated stand is LocalPlayerController's).
            bool direct = _local.DirectInputActive;
            if (direct && !_wasDirect && (Machine.Phase == InteractionPhase.Approaching || Machine.Phase == InteractionPhase.Requesting))
                Exec(Machine.PlayerMoved());
            _wasDirect = direct;

            if (Machine.Phase == InteractionPhase.Approaching && Machine.Target != null)
            {
                float d = InteractionFinder.Flat(transform.position, Machine.Target.approach);
                if (!_local.HasDestination && d < 0.4f) Exec(Machine.Arrived(Now));
                else if (!_local.HasDestination && !_local.IsMoving && Now - _walkIssuedAt > 0.75) Exec(Machine.WalkFailed());
            }

            if (Time.unscaledTime >= _nextOccupancy) { _nextOccupancy = Time.unscaledTime + 0.25f; RefreshOccupancy(); }
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.1f;
                if (_focused != null && (!_occupancy.IsFree(_focused, Me) || InteractionFinder.Flat(transform.position, _focused.approach) > InteractionFinder.FocusRange)) _focused = null;
                var fwd = transform.forward;
                Offered = Machine.Phase == InteractionPhase.Idle && !_view.SeatPresentationActive
                    ? InteractionFinder.Best(transform.position, fwd, _catalog, it => _occupancy.IsFree(it, Me), Offered, _focused)
                    : null;
                Publish(force: false);
            }
            UpdateHighlight();
        }

        private void LateUpdate()
        {
            if (Current == this) PresentationUpdated?.Invoke();
        }

        private void RefreshOccupancy()
        {
            if (_session == null) return;
            foreach (var kv in _session.Players)
                if (kv.Value != null && kv.Value.State != null) _occupancy.Set(kv.Key, kv.Value.State.spotId);
            foreach (var kv in _session.Npcs)
            {
                var s = kv.Value != null ? kv.Value.State : null;
                // An NPC heading to or acting at a spot holds it (the server allocates spots; we mirror it for the CTA).
                _occupancy.Set("npc:" + kv.Key, s != null && (s.state == "act" || s.state == "goto") ? s.spotId : null);
            }
            _occupancy.Set(Me, _local.SeatedSpot);
        }

        private void UpdateHighlight()
        {
            if (_highlight == null) return;
            var target = Machine.Phase == InteractionPhase.Idle ? Offered :
                Machine.Phase == InteractionPhase.Approaching || Machine.Phase == InteractionPhase.Requesting ? Machine.Target : null;
            _highlight.Show(target);
        }

        // ------------------------------------------------------------------ events

        private void OnSeatChanged(string spotId)
        {
            if (string.IsNullOrEmpty(spotId)) Exec(Machine.Stood());
            else Exec(Machine.ServerSpot(spotId, Find));
            Publish(force: true);
        }

        private void OnRejected()
        {
            Exec(Machine.Rejected());
            Publish(force: true);
        }

        private void Exec(List<InteractionCommand> commands)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                var c = commands[i];
                switch (c.kind)
                {
                    case InteractionCommandKind.WalkTo:
                        _walkIssuedAt = Now;
                        if (!_local.MoveTo(c.target.approach)) Exec(Machine.WalkFailed());
                        break;
                    case InteractionCommandKind.RequestSeat:
                        if (!_local.RequestSeat(c.target.id)) { Exec(Machine.Rejected()); Toast("interact.noResponse"); }
                        break;
                    case InteractionCommandKind.EngageLocal:
                        _local.EngageLocal(c.target.id, c.target.position, c.target.yaw, c.target.anim, c.target.Sits ? "act_sit_down" : null);
                        break;
                    case InteractionCommandKind.ReleaseSeat:
                    case InteractionCommandKind.Disengage:
                        _local.StandIfSeated();
                        break;
                    case InteractionCommandKind.CancelWalk:
                        _local.CancelMovement();
                        break;
                    case InteractionCommandKind.OpenMap:
                        Bus.Publish(new OpenMapRequestSignal());
                        break;
                    case InteractionCommandKind.Emote:
                        if (_channel != null && _channel.Emote(c.arg)) _view.PlayOneShot(c.arg);
                        break;
                    case InteractionCommandKind.Toast:
                        Toast(c.arg);
                        break;
                }
            }
        }

        private static void Toast(string key) => Bus.Publish(new InteractionToastSignal { key = key });

        private void Publish(bool force)
        {
            var actions = Machine.Actions(Offered);
            var key = Machine.Phase + "|" + string.Join(",", actions) + "|" + (Offered != null ? Offered.id : "");
            if (!force && key == _lastPromptKey) return;
            _lastPromptKey = key;
            Bus.Publish(GetPrompt());
        }

        /// <summary>Initial state for a HUD attached after the latest change signal.</summary>
        public InteractionPromptSignal GetPrompt()
        {
            var target = Machine.Phase == InteractionPhase.Idle ? Offered : Machine.Target;
            return new InteractionPromptSignal
            {
                actions = Machine.Actions(Offered), kind = target?.kind, targetId = target?.id, owner = this,
                anchor = InteractionPromptAnchor.For(Machine, Offered, transform),
                busy = Machine.Phase == InteractionPhase.Approaching || Machine.Phase == InteractionPhase.Requesting,
            };
        }
    }
}
