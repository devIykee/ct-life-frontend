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
    public sealed class InteractionController : MonoBehaviour
    {
        public static InteractionController Current { get; private set; }

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

        /// <summary>Follows the visit onto a rejoined venue room; seat occupancy restarts from the new room's state.</summary>
        public void Rebind(VenueChannel channel)
        {
            _channel = channel;
            _occupancy = new SeatOccupancy(_venue != null ? _venue.venueId : null);
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

        /// <summary>A tapped seat/terminal becomes the offered target (from farther away) - it does not sit by itself.</summary>
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
            var kind = Machine.Target != null ? Machine.Target.kind : Offered != null ? Offered.kind : (InteractionKind?)null;
            var labels = new List<InteractionAction>(actions.Count);
            foreach (var a in actions) labels.Add(a);
            Bus.Publish(new InteractionPromptSignal
            {
                actions = labels, kind = kind,
                busy = Machine.Phase == InteractionPhase.Approaching || Machine.Phase == InteractionPhase.Requesting,
            });
        }
    }
}
