using System;
using CTLife.Audio;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Rekt Burger's restaurant floor (on the rekt_burger interior prefab, built by IslandV2Interiors): while the player is
    /// inside, <see cref="RektBurgerHooks.SetOnFloor"/> is true and the restaurant's recorded ambience plays (tile footsteps);
    /// at the counter the HUD prompt offers the shift ("Clock in", or "Rekt Burger is hiring" while the job is locked),
    /// which opens the job through <see cref="RektBurgerHooks.Open"/>. On shift, <see cref="RektBurgerWork"/> works the line
    /// with the player's avatar and <see cref="RektBurgerCustomers"/> brings people in (both added here).
    /// </summary>
    [RequireComponent(typeof(InteriorRoom))]
    public sealed class RektBurgerFloor : MonoBehaviour
    {
        private InteriorRoom _room;
        private Func<Vector3, WorldOffer> _source;
        private readonly WorldOffer[] _offers = new WorldOffer[4];
        private string _prevAmbience;
        private bool _ambienceOn;

        private void Awake()
        {
            _room = GetComponent<InteriorRoom>();
            _source = Offer;
            if (GetComponent<RektBurgerWork>() == null) gameObject.AddComponent<RektBurgerWork>();
            if (GetComponent<RektBurgerCustomers>() == null) gameObject.AddComponent<RektBurgerCustomers>();
        }

        private void OnEnable()
        {
            WorldOffers.Add(_source);
            IslandInteriors.Entered += OnEntered;
            IslandInteriors.Exited += OnExited;
        }

        private void OnDisable()
        {
            WorldOffers.Remove(_source);
            IslandInteriors.Entered -= OnEntered;
            IslandInteriors.Exited -= OnExited;
            if (_ambienceOn) Ambience(false);
            RektBurgerHooks.SetOnFloor(false);
        }

        private void OnEntered(int door, string unit, InteriorRoom room)
        {
            if (room != _room) return;
            RektBurgerHooks.SetOnFloor(true);
            Ambience(true);
        }

        private void OnExited(int door, string unit, InteriorRoom room)
        {
            if (room != _room) return;
            RektBurgerHooks.SetOnFloor(false);
            Ambience(false);
        }

        /// <summary>amb_rekt_burger and tile footsteps while inside; what played before comes back on the way out.</summary>
        private void Ambience(bool on)
        {
            var am = AudioManager.Instance;
            if (am == null || on == _ambienceOn) return;
            _ambienceOn = on;
            if (on) { _prevAmbience = am.VenueId; am.SetAmbience(RektBurgerHooks.RoomId); Footsteps.SetSurface("neon_tile"); }
            else { am.SetAmbience(_prevAmbience); Footsteps.SetSurface(null); }
        }

        private WorldOffer Offer(Vector3 player)
        {
            var ii = IslandInteriors.Current;
            if (ii == null || !ii.PlayerInside || ii.ActiveRoom != _room || _room.spots == null) return null;
            WorldOffer best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _room.spots.Length && i < _offers.Length; i++)
            {
                var s = _room.spots[i];
                var at = transform.TransformPoint(s.position);
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(player.x, player.z));
                float range = s.range > 0f ? s.range : 2.2f;
                if (d > range || d >= bestD) continue;
                var o = _offers[i] ??= new WorldOffer();
                string what = string.IsNullOrEmpty(s.game) ? RektBurgerHooks.Shift : s.game;
                bool open = RektBurgerHooks.Unlocked;
                o.id = "rekt:" + s.id;
                o.verbKey = what == RektBurgerHooks.Shift ? (open ? "rekt.shift" : "rekt.hiring") : "rekt.info";
                o.icon = "jobs";
                o.tagKey = "rekt.tag";
                o.tagLocked = what == RektBurgerHooks.Shift && !open;
                o.primary = true;
                o.position = at + Vector3.up * 1.6f;
                o.follow = null;
                o.range = range;
                o.perform = () =>
                {
                    Debug.Log("CTLIFE [rekt] counter opens " + what + (open ? "" : " (locked)"));
                    RektBurgerHooks.Open?.Invoke(what);
                };
                o.sub = o.subTone = o.style = null; o.disabled = o.busy = false;
                o.secondaryVerbKey = null; o.secondaryPerform = null;
                var p = what == RektBurgerHooks.Shift ? RektBurgerHooks.Prompt?.Invoke() : null;
                if (p.HasValue)
                {
                    // the approved clock-in prompt: an orange job key with the pay facts under it, Job info beside it
                    var v = p.Value;
                    o.verbKey = v.verbKey ?? o.verbKey;
                    o.icon = v.icon ?? "clock";
                    o.sub = v.sub; o.subTone = v.subTone; o.style = v.style ?? "job";
                    o.disabled = v.disabled; o.busy = v.busy; o.tagLocked = v.locked;
                    o.tagKey = "rekt.tagCounter";
                    if (RektBurgerHooks.JobInfo != null)
                    {
                        o.secondaryVerbKey = "rekt.jobinfo"; o.secondaryIcon = "jobs";
                        o.secondaryPerform = () => { Debug.Log("CTLIFE [rekt] counter: job info"); RektBurgerHooks.JobInfo?.Invoke(); };
                    }
                }
                best = o; bestD = d;
            }
            return best;
        }
    }
}
