using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// UI-only particles for milestone moments: gold coins (burst up, then gravity), confetti (falls with drift and spin),
    /// and a soft cloud puff. A fixed pool of plain VisualElements is created once; a tick moves them with struct style
    /// values, so a running burst allocates nothing per frame. The tick pauses when every particle is dead.
    /// Off under reduced motion (spec C1/C2: no confetti, no coins). Styles: Resources/UI/kit.uss.
    /// </summary>
    public sealed class CelebrationBurst : VisualElement
    {
        private enum Kind : byte { Coin, Confetti, Puff }

        private struct Particle
        {
            public float x, y, vx, vy, rot, vr, age, life, size, delay, drag;
            public Kind kind;
            public bool alive;
        }

        // Palette from the identity: plum, white, gold, Solana green, purple. Plus a dusty plum-grey for the debt shower.
        private static readonly Color[] Festive =
        {
            new Color32(58, 20, 64, 255), new Color32(255, 255, 255, 255), new Color32(255, 200, 60, 255),
            new Color32(20, 241, 149, 255), new Color32(146, 88, 220, 255),
        };
        private static readonly Color[] Muted = { new Color32(122, 98, 128, 255), new Color32(170, 152, 176, 255), new Color32(96, 78, 104, 255) };

        private const float Gravity = 2600f; // units/s², tuned for a 1080-unit-tall panel
        private readonly Particle[] _p;
        private readonly VisualElement[] _el;
        private readonly Kind[] _elKind;
        private IVisualElementScheduledItem _tick;
        private float _last;
        private int _seed = 12345;

        public CelebrationBurst(int coins = 32, int confetti = 120, int puffs = 6)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("kit-burst");
            int n = coins + confetti + puffs;
            _p = new Particle[n];
            _el = new VisualElement[n];
            _elKind = new Kind[n];
            int i = 0;
            for (int k = 0; k < coins; k++, i++) _el[i] = Make(Kind.Coin, i);
            for (int k = 0; k < confetti; k++, i++) _el[i] = Make(Kind.Confetti, i);
            for (int k = 0; k < puffs; k++, i++) _el[i] = Make(Kind.Puff, i);
        }

        private VisualElement Make(Kind kind, int index)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("kit-burst__p");
            e.AddToClassList(kind == Kind.Coin ? "kit-burst__coin" : kind == Kind.Confetti ? "kit-burst__confetti" : "kit-burst__puff");
            if (kind == Kind.Coin)
            {
                var hi = new VisualElement { pickingMode = PickingMode.Ignore };
                hi.AddToClassList("kit-burst__coin-hi");
                e.Add(hi);
            }
            e.style.display = DisplayStyle.None;
            _elKind[index] = kind;
            Add(e);
            return e;
        }

        /// <summary>`count` coins burst up and out from `origin` (panel units, local to this element).</summary>
        public void Coins(Vector2 origin, int count = 24)
        {
            if (DeviceFeel.ReducedMotion) return;
            for (int i = 0, used = 0; i < _p.Length && used < count; i++)
            {
                if (_elKind[i] != Kind.Coin || _p[i].alive) continue;
                float a = Mathf.Lerp(-150f, -30f, Rand()) * Mathf.Deg2Rad;
                float speed = Mathf.Lerp(900f, 1650f, Rand());
                Spawn(i, Kind.Coin, origin.x + Mathf.Lerp(-40f, 40f, Rand()), origin.y, Mathf.Cos(a) * speed, Mathf.Sin(a) * speed,
                    Mathf.Lerp(0.8f, 1.15f, Rand()), used * 0.012f, Mathf.Lerp(-540f, 540f, Rand()), Mathf.Lerp(0.8f, 1.2f, Rand()), 0f);
                used++;
            }
            Run();
        }

        /// <summary>`count` confetti pieces falling across `width` from just above the top. `slow` is the gentle debt shower.</summary>
        public void Confetti(int count = 60, bool muted = false, bool slow = false)
        {
            if (DeviceFeel.ReducedMotion) return;
            float w = resolvedStyle.width > 1f ? resolvedStyle.width : 2400f;
            var colours = muted ? Muted : Festive;
            for (int i = 0, used = 0; i < _p.Length && used < count; i++)
            {
                if (_elKind[i] != Kind.Confetti || _p[i].alive) continue;
                float x = Mathf.Lerp(0.05f, 0.95f, Rand()) * w;
                float vy = slow ? Mathf.Lerp(120f, 260f, Rand()) : Mathf.Lerp(-200f, 300f, Rand());
                Spawn(i, Kind.Confetti, x, Mathf.Lerp(-120f, -20f, Rand()), Mathf.Lerp(-160f, 160f, Rand()), vy,
                    slow ? Mathf.Lerp(1.6f, 2.2f, Rand()) : Mathf.Lerp(1.0f, 1.4f, Rand()), Rand() * (slow ? 0.6f : 0.35f),
                    Mathf.Lerp(-720f, 720f, Rand()), Mathf.Lerp(0.7f, 1.2f, Rand()), slow ? 3.5f : 1.2f);
                _el[i].style.backgroundColor = colours[(int)(Rand() * colours.Length) % colours.Length];
                used++;
            }
            Run();
        }

        /// <summary>A soft cloud puff at `origin`: a few white-grey circles that swell and fade (the debt "bonk").</summary>
        public void Puff(Vector2 origin)
        {
            if (DeviceFeel.ReducedMotion) return;
            int used = 0;
            for (int i = 0; i < _p.Length && used < 6; i++)
            {
                if (_elKind[i] != Kind.Puff || _p[i].alive) continue;
                float a = (used / 6f) * Mathf.PI * 2f + Rand() * 0.4f;
                Spawn(i, Kind.Puff, origin.x + Mathf.Cos(a) * 40f, origin.y + Mathf.Sin(a) * 26f, Mathf.Cos(a) * 260f, Mathf.Sin(a) * 120f - 60f,
                    0.7f, used * 0.02f, 0f, Mathf.Lerp(0.8f, 1.2f, Rand()), 4f);
                used++;
            }
            Run();
        }

        public void Stop()
        {
            for (int i = 0; i < _p.Length; i++)
            {
                _p[i].alive = false;
                _el[i].style.display = DisplayStyle.None;
            }
            _tick?.Pause();
        }

        private void Spawn(int i, Kind kind, float x, float y, float vx, float vy, float life, float delay, float vr, float size, float drag)
        {
            _p[i] = new Particle
            {
                x = x, y = y, vx = vx, vy = vy, life = life, delay = delay, vr = vr, size = size, drag = drag,
                rot = Rand() * 360f, age = 0f, kind = kind, alive = true,
            };
            var e = _el[i];
            e.style.display = DisplayStyle.None;
            e.style.opacity = 1f;
        }

        private void Run()
        {
            _last = Motion.Now;
            if (_tick == null) _tick = schedule.Execute(Tick).Every(16);
            else _tick.Resume();
        }

        private void Tick()
        {
            float now = Motion.Now;
            float dt = Mathf.Min(0.05f, now - _last);
            _last = now;
            bool any = false;
            for (int i = 0; i < _p.Length; i++)
            {
                if (!_p[i].alive) continue;
                ref var p = ref _p[i];
                if (p.delay > 0f) { p.delay -= dt; any = true; continue; }
                p.age += dt;
                if (p.age >= p.life) { p.alive = false; _el[i].style.display = DisplayStyle.None; continue; }
                any = true;
                float t = p.age / p.life;
                if (p.kind == Kind.Puff)
                {
                    p.vx *= 1f - Mathf.Min(1f, p.drag * dt);
                    p.vy *= 1f - Mathf.Min(1f, p.drag * dt);
                }
                else
                {
                    p.vy += Gravity * (p.kind == Kind.Confetti ? 0.18f : 1f) * dt;
                    if (p.drag > 0f) p.vx *= 1f - Mathf.Min(1f, p.drag * dt * 0.3f);
                    if (p.kind == Kind.Confetti) p.vy = Mathf.Min(p.vy, 520f); // flutter: confetti reaches a terminal speed
                }
                p.x += p.vx * dt;
                p.y += p.vy * dt;
                p.rot += p.vr * dt;
                var e = _el[i];
                e.style.display = DisplayStyle.Flex;
                e.style.translate = new Translate(p.x, p.y);
                float s = p.kind == Kind.Puff ? p.size * Mathf.Lerp(0.6f, 1.6f, Ease.OutCubic(t)) : p.size;
                // Confetti tumbles: its width scale swings to fake a 3D flip.
                float sx = p.kind == Kind.Confetti ? s * Mathf.Cos(p.rot * 2.3f * Mathf.Deg2Rad) : s;
                e.style.scale = new Scale(new Vector2(sx, s));
                e.style.rotate = new Rotate(new Angle(p.kind == Kind.Coin ? p.rot * 0.25f : p.rot));
                e.style.opacity = p.kind == Kind.Puff ? 0.9f * (1f - t) : t > 0.8f ? (1f - t) * 5f : 1f;
            }
            if (!any) _tick?.Pause();
        }

        private float Rand()
        {
            // xorshift: deterministic per instance (captures are repeatable) and allocation-free.
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            return (_seed & 0x7fffffff) / (float)int.MaxValue;
        }
    }
}
