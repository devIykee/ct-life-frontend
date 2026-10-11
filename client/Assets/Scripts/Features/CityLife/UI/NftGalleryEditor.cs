using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Pick which of my verified NFTs hang on which wall frame, then save (proposal routes; UNAVAILABLE → read-only note).
    /// NSFW/blocked items are listed as flagged and cannot be hung; pending items can be hung but only the owner sees them.
    /// </summary>
    internal sealed class NftGalleryEditor : IDisposable
    {
        public readonly VisualElement Root;
        private readonly CityLifeHub _hub;
        private readonly Func<bool> _alive;
        private readonly CancellationTokenSource _life;
        private readonly RetainedWriteFlow _save = new RetainedWriteFlow();
        private List<NftItem> _mine, _all;
        private List<FramePlacement> _frames = new List<FramePlacement>();
        private NftItem[] _slots = new NftItem[0];
        private CityError _error;
        private bool _loading, _disposed, _dirty;

        public NftGalleryEditor(CityLifeHub hub, Func<bool> alive, CancellationToken life)
        {
            _hub = hub;
            _alive = alive;
            _life = CancellationTokenSource.CreateLinkedTokenSource(life);
            Root = El.Div("duo-nft");
            _save.Changed += Render;
            Load();
        }

        private bool Alive => !_disposed && _alive();

        public static int SlotCount(CityLifeHub hub)
        {
            var me = hub.Profile?.Me;
            var home = hub.Homes.Find(h => h.Id == me?.HomeId);
            if (home == null) return 0;
            if (home.FrameSlots >= 0) return home.FrameSlots;
            return Math.Max(0, home.Rooms);
        }

        private async void Load()
        {
            if (_loading || !Alive) return;
            _loading = true;
            _error = null;
            Render();
            try
            {
                var mine = await _hub.Nfts.Mine(_life.Token);
                var (frames, items) = await _hub.Nfts.Gallery(_hub.UserId, _life.Token);
                if (!Alive) return;
                _mine = mine;
                _all = new List<NftItem>(mine);
                foreach (var i in items) if (_all.Find(x => x.AssetId == i.AssetId) == null) _all.Add(i);
                _frames = frames;
                _slots = GalleryLayout.Assign(frames, _all, SlotCount(_hub), viewerIsOwner: true);
                _dirty = false;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { if (!Alive) return; _error = CityErrors.From(e, false); }
            finally { _loading = false; }
            Render();
        }

        public void Refresh()
        {
            if (!Alive || _mine == null) return;
            int n = SlotCount(_hub);
            if (n == _slots.Length) return;
            // Until the player edits, the server's placements are the source; afterwards, what is on screen.
            var keep = _dirty ? GalleryLayout.ToPlacements(_slots) : _frames;
            _slots = GalleryLayout.Assign(keep, _all, n, true);
            Render();
        }

        private void Render()
        {
            if (!Alive) return;
            Root.Clear();
            Root.Add(Duo.GroupHeader(CityLifeStrings.T("nft.title")));
            var group = El.Div("duo-group duo-nft-group");
            Root.Add(group);
            if (_loading) { group.Add(CityUi.Small(CityLifeStrings.T("common.loading"))); return; }
            if (_error != null)
            {
                group.Add(CityUi.Small(_error.Is("UNAVAILABLE") ? CityLifeStrings.T("nft.notLive") : _error.Message));
                if (!_error.Is("UNAVAILABLE")) group.Add(CityUi.Row(CityUi.Btn(CityLifeStrings.T("common.retry"), Load, CityUi.Plain)));
                Root.Add(Duo.GroupFooter(CityLifeStrings.T("nft.hint")));
                return;
            }
            if (_mine == null) return;
            if (_slots.Length == 0) { group.Add(CityUi.Small(CityLifeStrings.T("nft.noWalls"))); Root.Add(Duo.GroupFooter(CityLifeStrings.T("nft.hint"))); return; }
            int used = 0;
            foreach (var s in _slots) if (s != null) used++;
            for (int i = 0; i < _slots.Length; i++)
            {
                int slot = i;
                var item = _slots[i];
                var row = new DuoRow().Title(CityLifeStrings.F("nft.slot", i + 1), "duo-row-title-strong");
                row.AddToClassList("duo-row-static");
                if (item == null) { row.Leading(El.Div("duo-nft-thumb duo-nft-empty")); row.Subtitle(CityLifeStrings.T("nft.emptySlot")); }
                else
                {
                    row.Leading(Thumb(item));
                    row.Subtitle(Label(item) + (item.Moderation == Moderation.Pending ? "\n" + CityLifeStrings.T("nft.pending") : ""), twoLines: true);
                    row.Trailing(Duo.Pill(CityLifeStrings.T("nft.remove"), () => { _slots[slot] = null; _dirty = true; Render(); }, Duo.PillKind.Destructive));
                }
                group.Add(row);
            }
            Duo.MarkLast(group);
            Root.Add(Duo.GroupFooter(CityLifeStrings.F("nft.slots", used, _slots.Length) + " · " + CityLifeStrings.T("nft.hint")));
            Root.Add(Duo.GroupHeader(CityLifeStrings.T("nft.mine")));
            var mine = El.Div("duo-group duo-nft-group");
            Root.Add(mine);
            if (_mine.Count == 0) mine.Add(CityUi.Small(CityLifeStrings.T("nft.empty")));
            foreach (var item in _mine)
            {
                bool hung = Array.Exists(_slots, s => s != null && s.AssetId == item.AssetId);
                var row = new DuoRow().Leading(Thumb(item)).Title(Label(item));
                row.AddToClassList("duo-row-static");
                var vis = NftItem.VisibilityFor(item, viewerIsOwner: true);
                if (vis == FrameVisibility.Hidden) row.Trailing(CityUi.Tag(CityLifeStrings.T("nft.flaggedTag"), "red")).Subtitle(CityLifeStrings.T("nft.flagged"));
                else if (hung) row.Trailing(CityUi.Tag(CityLifeStrings.T("nft.hungTag"), "blue"));
                else
                {
                    var pick = item;
                    var b = Duo.Pill(CityLifeStrings.T("nft.hang"), () => Hang(pick));
                    b.SetEnabled(Array.IndexOf(_slots, null) >= 0);
                    row.Trailing(b);
                }
                mine.Add(row);
            }
            Duo.MarkLast(mine);
            Root.Add(Duo.GroupFooter(CityLifeStrings.T("nft.visitorsNote")));
            if (_save.Error != null) Root.Add(Duo.Notice(_save.Error.Message, Duo.Tone.Danger));
            if (_save.Stage == WriteStage.Done && !_dirty) Root.Add(CityUi.Small(CityLifeStrings.T("nft.saved")));
            var save = CityUi.Btn(_save.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.T("nft.save"), Save);
            save.name = "citylife-nft-save";
            save.SetEnabled((_dirty || _save.Stage == WriteStage.Unknown) && _save.Stage != WriteStage.Sending);
            Root.Add(CityUi.Row(save));
        }

        private static string Label(NftItem item)
        {
            var n = string.IsNullOrEmpty(item.Name) ? item.AssetId.Substring(0, 6) + "…" : item.Name;
            return string.IsNullOrEmpty(item.Collection) ? n : n + " · " + item.Collection;
        }

        private VisualElement Thumb(NftItem item)
        {
            var img = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            img.AddToClassList("duo-nft-thumb");
            if (NftItem.VisibilityFor(item, true) == FrameVisibility.Show)
                _ = LoadThumb(img, string.IsNullOrEmpty(item.ThumbUrl) ? item.ImageUrl : item.ThumbUrl);
            return img;
        }

        private async Task LoadThumb(Image img, string url)
        {
            var tex = await _hub.Images.Get(url, _life.Token);
            if (Alive && tex != null) img.image = tex;
        }

        private void Hang(NftItem item)
        {
            int free = Array.IndexOf(_slots, null);
            if (free < 0 || NftItem.VisibilityFor(item, true) == FrameVisibility.Hidden) return;
            _slots[free] = item;
            _dirty = true;
            if (_save.Stage == WriteStage.Done || _save.Stage == WriteStage.Rejected) _save.Reset();
            Render();
        }

        private async void Save()
        {
            var frames = GalleryLayout.ToPlacements(_slots);
            var body = FramePlacement.ToJson(frames).ToString(Newtonsoft.Json.Formatting.None);
            if (_save.Stage == WriteStage.Done || _save.Stage == WriteStage.Rejected) _save.Reset();
            var key = _save.Begin("PUT", "/homes/me/gallery", body);
            if (key == null) return;
            try
            {
                await _hub.Nfts.SaveGallery(frames, key, _life.Token);
                if (!Alive) return;
                _dirty = false;
                _frames = frames;
                _save.Succeeded();
                Bus.Publish(new GalleryChangedSignal { ownerId = _hub.UserId });
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (Alive) _save.Failed(CityErrors.From(e, true)); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _save.Changed -= Render;
            _life.Cancel();
        }
    }

    /// <summary>The owner saved a new wall; the wall presenter reloads frames if that home is showing.</summary>
    public struct GalleryChangedSignal
    {
        public string ownerId;
    }
}
