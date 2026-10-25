using System;
using CTLife.Audio;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The Rug Room's floor (on the rug_room interior prefab, built by IslandV2Interiors): while the player stands on it,
    /// <see cref="CasinoHooks.SetOnFloor"/> is true (City Life plays the floor ambience and polls challenges), and near the
    /// slot bank, the Moon or Rug screen or the coinflip table the HUD prompt offers the game (WorldOffers), which opens the
    /// casino through <see cref="CasinoHooks.Open"/>.
    /// </summary>
    [RequireComponent(typeof(InteriorRoom))]
    public sealed class CasinoFloor : MonoBehaviour
    {
        private InteriorRoom _room;
        private Func<Vector3, WorldOffer> _source;
        private readonly WorldOffer[] _offers = new WorldOffer[8];

        private void Awake()
        {
            _room = GetComponent<InteriorRoom>();
            _source = Offer;
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
            CasinoHooks.SetOnFloor(false);
        }

        private string _prevAmbience;
        private bool _ambienceOn;

        private void OnEntered(int door, string unit, InteriorRoom room)
        {
            if (room != _room) return;
            CasinoHooks.SetOnFloor(true);
            Ambience(true);
        }

        private void OnExited(int door, string unit, InteriorRoom room)
        {
            if (room != _room) return;
            CasinoHooks.SetOnFloor(false);
            Ambience(false);
        }

        /// <summary>The floor's recorded ambience (amb_rug_room) and carpet footsteps while inside; what played before comes back.</summary>
        private void Ambience(bool on)
        {
            var am = AudioManager.Instance;
            if (am == null || on == _ambienceOn) return;
            _ambienceOn = on;
            if (on) { _prevAmbience = am.VenueId; am.SetAmbience("rug_room"); Footsteps.SetSurface("carpet"); }
            else { am.SetAmbience(_prevAmbience); Footsteps.SetSurface(null); }
        }

        public static string VerbKey(string game) => "casino.play." + game;

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
                string game = s.game;
                o.id = "casino:" + s.id;
                o.verbKey = VerbKey(game);
                o.icon = game == CasinoHooks.Crash ? "perps" : game == CasinoHooks.Coinflip ? "cash" : "dream";
                o.tagKey = "casino.tag";
                o.tagLocked = false;
                o.primary = true;
                o.position = at + Vector3.up * 1.6f;
                o.follow = null;
                o.range = range;
                o.perform = () =>
                {
                    Debug.Log("CTLIFE [casino] floor opens " + game);
                    CasinoHooks.Open?.Invoke(game);
                };
                best = o; bestD = d;
            }
            return best;
        }
    }
}
