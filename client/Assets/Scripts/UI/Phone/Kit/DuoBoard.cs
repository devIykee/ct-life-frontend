using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>One leaderboard line as the server sent it: rank, alias, a seed for the avatar colour, the formatted value.</summary>
    public readonly struct DuoRank
    {
        public readonly int Rank;
        public readonly string Alias, Seed, Value;
        public readonly bool Me;

        public DuoRank(int rank, string alias, string seed, string value, bool me)
        {
            Rank = rank; Alias = alias ?? ""; Seed = seed ?? alias ?? ""; Value = value ?? ""; Me = me;
        }
    }

    /// <summary>
    /// Leaderboards on the Duo (spec "Rich List / League"): a podium for the top three, rank rows with the value trailing,
    /// and the player's own row pinned at the bottom in glass. Only server ranks and values are shown.
    /// </summary>
    public static class DuoBoard
    {
        /// <summary>
        /// The podium takes the leading entries ranked 1–3 (at most three, in server order); every other entry is a row.
        /// A board whose first entry is not rank 1 (a page further down) gets no podium.
        /// </summary>
        public static void Split(IReadOnlyList<DuoRank> entries, List<DuoRank> podium, List<DuoRank> rest)
        {
            podium.Clear();
            rest.Clear();
            if (entries == null) return;
            bool top = entries.Count > 0 && entries[0].Rank == 1;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (top && podium.Count < 3 && i == podium.Count && e.Rank >= 1 && e.Rank <= 3) podium.Add(e);
                else rest.Add(e);
            }
        }

        /// <summary>True when the player's own line should be pinned: the server sent one.</summary>
        public static bool ShouldPin(DuoRank? me) => me.HasValue && me.Value.Rank > 0;

        /// <summary>Podium (when the board starts at #1), then rank rows. Returns the element holding both.</summary>
        public static VisualElement Board(IReadOnlyList<DuoRank> entries, string name = null)
        {
            var podium = new List<DuoRank>();
            var rest = new List<DuoRank>();
            Split(entries, podium, rest);
            var board = El.Div("duo-board");
            if (name != null) board.name = name;
            if (podium.Count > 0) board.Add(Podium(podium));
            if (rest.Count > 0)
            {
                var list = El.Div("duo-list duo-board-rows");
                foreach (var r in rest) list.Add(Row(r));
                Duo.MarkLast(list);
                board.Add(list);
            }
            return board;
        }

        /// <summary>Second, first, third left to right; first stands tallest.</summary>
        public static VisualElement Podium(IReadOnlyList<DuoRank> top)
        {
            var p = El.Div("duo-podium");
            p.name = "duo-podium";
            int[] order = { 1, 0, 2 };
            foreach (int i in order)
            {
                if (i >= top.Count) { p.Add(El.Div("duo-podium-spot duo-podium-empty")); continue; }
                var r = top[i];
                string place = r.Rank == 1 ? "first" : r.Rank == 2 ? "second" : "third";
                var badge = El.Text(r.Rank.ToString(CultureInfo.InvariantCulture), "duo-podium-badge");
                var face = El.Div("duo-podium-face", Duo.Avatar(r.Alias, r.Seed, r.Rank == 1 ? "duo-avatar-podium-1" : "duo-avatar-podium"), badge);
                var spot = El.Div("duo-podium-spot duo-podium-" + place + (r.Me ? " duo-podium-mine" : ""),
                    face,
                    El.Text(r.Alias, "duo-podium-name" + (r.Me ? " duo-podium-me" : "")),
                    El.Text(r.Value, "duo-podium-value"),
                    El.Div("duo-podium-step", El.Text(r.Rank.ToString(CultureInfo.InvariantCulture), "duo-podium-step-rank")));
                spot.pickingMode = PickingMode.Ignore;
                p.Add(spot);
            }
            return p;
        }

        /// <summary>Rank, small avatar, alias, value trailing; your own row is bold.</summary>
        public static DuoRow Row(DuoRank r)
        {
            var row = new DuoRow().Leading(El.Div("duo-rank-lead",
                    El.Text(r.Rank > 0 ? r.Rank.ToString(CultureInfo.InvariantCulture) : "—", "duo-rank-number"),
                    Duo.Avatar(r.Alias, r.Seed, "duo-avatar-s")))
                .Title(r.Alias, r.Me ? "duo-row-title-strong" : null)
                .Value(r.Value, "duo-rank-value");
            row.AddToClassList("duo-row-static");
            row.EnableInClassList("duo-rank-me", r.Me);
            return row;
        }

        /// <summary>
        /// iOS rule for the pinned own row: show it only while your own rank row (or podium spot) is not visible above it,
        /// and never while the podium reaches into the pin's band. Rects are in one space (panel/world); null = not on the page.
        /// </summary>
        public static bool PinVisible(Rect viewport, Rect pinZone, Rect? ownRow, Rect? podium)
        {
            if (podium.HasValue && podium.Value.yMax > pinZone.yMin && podium.Value.yMin < pinZone.yMax && podium.Value.yMax > viewport.yMin)
                return false;
            if (ownRow.HasValue)
            {
                var r = ownRow.Value;
                // Visible = some of it shows between the top of the viewport and the top of the pin band.
                float top = Mathf.Max(r.yMin, viewport.yMin), bottom = Mathf.Min(r.yMax, pinZone.yMin);
                if (bottom - top > 1f) return false;
            }
            return true;
        }

        private static Rect? Bound(VisualElement e) =>
            e == null || e.panel == null || float.IsNaN(e.worldBound.height) || e.worldBound.height <= 0 ? (Rect?)null : e.worldBound;

        /// <summary>Re-evaluates <see cref="PinVisible"/> for a page's pin (scroll, layout and data changes call this).</summary>
        public static void SyncPin(VisualElement page)
        {
            var pin = page?.Q(className: "duo-board-pin");
            var scroll = page?.Q<ScrollView>(className: "duo-scroll");
            if (pin == null || scroll == null) return;
            var viewport = Bound(scroll.contentViewport);
            // The pin's resting band (its layout, without the hide slide) so hiding can't change the answer.
            if (!viewport.HasValue || pin.parent == null || float.IsNaN(pin.layout.height) || pin.layout.height <= 0) return;
            var zone = pin.parent.LocalToWorld(pin.layout);
            var board = scroll.contentContainer;
            var podiumEl = board.Q("duo-podium");
            var podium = Bound(podiumEl);
            if (podiumEl != null && !podium.HasValue) return; // not laid out yet: stay as is (hidden at first)
            var own = board.Q(className: "duo-podium-mine") ?? board.Q<DuoRow>(className: "duo-rank-me");
            bool show = PinVisible(viewport.Value, zone, Bound(own), podium);
            pin.EnableInClassList("duo-board-pin-hidden", !show);
            pin.pickingMode = PickingMode.Ignore;
        }

        /// <summary>The player's own line in a glass capsule docked at the bottom of <paramref name="page"/>.</summary>
        public static VisualElement PinMe(VisualElement page, DuoRank me, string youLabel)
        {
            page.Q(className: "duo-board-pin")?.RemoveFromHierarchy();
            var row = Row(new DuoRank(me.Rank, string.IsNullOrEmpty(youLabel) ? me.Alias : youLabel, me.Seed, me.Value, true));
            var pin = El.Div("duo-board-pin");
            pin.name = "duo-board-pin";
            var dock = El.Div("duo-bottom-bar-dock");
            dock.pickingMode = PickingMode.Ignore;
            dock.style.backgroundImage = Background.FromTexture2D(Duo.Fade);
            pin.Add(dock);
            Duo.AddShadow(pin, "bar");
            pin.Add(El.Div("duo-board-pin-face", row));
            page.AddToClassList("duo-has-pin");
            page.Add(pin);
            // Hidden until the first layout says whether your own row or the podium is in view.
            pin.AddToClassList("duo-board-pin-hidden");
            var scroll = page.Q<ScrollView>(className: "duo-scroll");
            if (scroll != null)
            {
                System.Action<float> onScroll = _ => SyncPin(page);
                EventCallback<GeometryChangedEvent> onLayout = _ => SyncPin(page);
                scroll.verticalScroller.valueChanged += onScroll;
                scroll.contentContainer.RegisterCallback(onLayout);
                pin.RegisterCallback<GeometryChangedEvent>(onLayout);
                // Layout can settle after the first geometry pass (text, avatars, the page fade-in): re-check while shown.
                pin.schedule.Execute(() => SyncPin(page)).Every(200);
                pin.RegisterCallback<DetachFromPanelEvent>(e =>
                {
                    if (e.target != pin) return;
                    scroll.verticalScroller.valueChanged -= onScroll;
                    scroll.contentContainer.UnregisterCallback(onLayout);
                });
            }
            return pin;
        }

        public static void Unpin(VisualElement page)
        {
            page?.Q(className: "duo-board-pin")?.RemoveFromHierarchy();
            page?.RemoveFromClassList("duo-has-pin");
        }
    }
}
