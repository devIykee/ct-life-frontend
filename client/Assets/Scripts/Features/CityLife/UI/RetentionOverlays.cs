using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife
{
    /// <summary>
    /// The daily loop while signed in (2026-10-24): one <see cref="RetentionService"/> per user and the quest toast over the
    /// game. Created by FlowController next to the other overlays; rebinds on sign-in and sign-out.
    /// </summary>
    public sealed class RetentionOverlays : IDisposable
    {
        private readonly Session _session;
        private readonly VisualElement _toastLayer;
        private RetentionService _svc;
        private QuestToast _toast;
        private bool _disposed;

        public RetentionOverlays(VisualElement toastLayer)
        {
            _toastLayer = toastLayer;
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            RetentionService.QuestMoved += OnQuestMoved;
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            _svc?.Dispose();
            _svc = string.IsNullOrEmpty(user) ? null : new RetentionService(user);
        }

        private void OnQuestMoved(Quest q, QuestBoard board)
        {
            if (_disposed || q == null || board == null) return;
            if (_toast == null || _toast.parent == null) { _toast = new QuestToast(); _toastLayer.Add(_toast); }
            _toast.Show(q, board);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            RetentionService.QuestMoved -= OnQuestMoved;
            _toast?.RemoveFromHierarchy();
            _svc?.Dispose();
            _svc = null;
        }
    }

    /// <summary>
    /// "Quest 2/3: Make a trade ✓" over the game: a compact HUD-glass capsule under the top bar with a dartboard, the quest,
    /// a thin bar that fills to the new value, and a check when it completes. One event can move several quests (a trade
    /// moves the daily and the weekly one), so toasts queue: a completion holds 2.6 s, progress 1.8 s; a newer update for a
    /// queued quest replaces it. Tap opens Quests.
    /// </summary>
    public sealed class QuestToast : VisualElement
    {
        private struct Item { public string Id, Title, Count; public float From, To; public bool Done; }

        private readonly Label _title, _count;
        private readonly VisualElement _fill, _check;
        private readonly List<Item> _queue = new List<Item>();
        private readonly Dictionary<string, float> _lastFraction = new Dictionary<string, float>();
        private IVisualElementScheduledItem _next;
        private Tween _bar;
        private bool _showing;

        public QuestToast()
        {
            name = "quest-toast";
            AddToClassList("quest-toast");
            var sheet = Resources.Load<StyleSheet>("UI/retention");
            if (sheet != null) styleSheets.Add(sheet);
            _title = El.Text("", "quest-toast-title");
            _title.enableRichText = false;
            _count = El.Text("", "quest-toast-count");
            _fill = El.Div("quest-toast-fill");
            _check = El.Div("quest-toast-check", El.Text("✓", "quest-toast-check-mark"));
            var copy = El.Div("quest-toast-copy", El.Div("quest-toast-row", _title, _count), El.Div("quest-toast-track", _fill));
            Add(LobbyArt.Icon("goal", "quest-toast-icon"));
            Add(copy);
            Add(_check);
            foreach (var e in this.Query<VisualElement>().ToList()) if (e != this) e.pickingMode = PickingMode.Ignore;
            RegisterCallback<PointerUpEvent>(_ => { RetentionService.Route("ctlife://" + DeepLinks.QuestsApp); _queue.Clear(); Hide(); });
            style.display = DisplayStyle.None;
        }

        public void Show(Quest q, QuestBoard board)
        {
            bool weekly = q.Weekly;
            int done = weekly ? board.WeeklyDone : board.DailyDone, total = weekly ? board.Weekly.Count : board.Daily.Count;
            string name = string.IsNullOrEmpty(q.Title) ? q.Description : q.Title;
            float to = q.Fraction;
            float from = _lastFraction.TryGetValue(q.Id, out var f) ? f : Mathf.Clamp01((q.Progress - 1) / (float)q.Target);
            _lastFraction[q.Id] = to;
            var item = new Item
            {
                Id = q.Id, Done = q.Completed, From = from, To = to,
                Title = q.Completed ? RetentionText.F(weekly ? "toast.weekly" : "toast.daily", done, Math.Max(total, 1), name) : name,
                Count = q.Completed ? "" : q.Progress + "/" + q.Target,
            };
            int i = _queue.FindIndex(x => x.Id == q.Id);
            if (i >= 0) { item.From = _queue[i].From; _queue[i] = item; }
            else if (item.Done) _queue.Insert(0, item);   // completions first
            else _queue.Add(item);
            if (_queue.Count > 3) _queue.RemoveAt(_queue.Count - 1);
            if (!_showing) Pump();
        }

        private void Pump()
        {
            _next?.Pause();
            if (_queue.Count == 0) { Hide(); return; }
            var it = _queue[0];
            _queue.RemoveAt(0);
            bool fresh = !_showing;
            _showing = true;
            _title.text = it.Title;
            _count.text = it.Count;
            _check.Show(it.Done);
            EnableInClassList("quest-toast--done", it.Done);
            style.display = DisplayStyle.Flex;
            if (fresh) Motion.Enter(this, 0f, -40f, 0.3f);
            else Motion.Enter(this, 0f, -12f, 0.18f);
            _bar?.Cancel();
            float from = it.From, to = it.To;
            _fill.style.width = Length.Percent(from * 100f);
            _bar = Tween.Run(_fill, 0.5f, t => _fill.style.width = Length.Percent(Mathf.Lerp(from, to, t) * 100f), Ease.OutCubic);
            if (it.Done) { Cues.Play("sfx_ui_success"); DeviceFeel.Confirm(); }
            else Cues.Play("sfx_ui_toast", 0.8f);
            _next = schedule.Execute(Pump).StartingIn(it.Done ? 2600 : 1800);
        }

        private void Hide()
        {
            _next?.Pause(); _next = null;
            if (!_showing) return;
            _showing = false;
            Motion.Fade(this, 1f, 0f, 0.25f);
            schedule.Execute(() => { if (!_showing) style.display = DisplayStyle.None; }).StartingIn(280);
        }
    }

    /// <summary>
    /// The push pre-prompt (2026-10-24): after the first chest claim, a lobby card says why before the Android 13+ system
    /// dialog appears (asked once per install; "Not now" never re-asks on its own).
    /// </summary>
    public sealed class PushPrePrompt : VisualElement
    {
        public static void Show(RetentionService svc)
        {
            if (UiRoot.Instance == null || svc == null) return;
            UiRoot.Instance.ModalLayer.Add(new PushPrePrompt(svc));
            Log.Info("push", "pre-prompt shown");
        }

        private PushPrePrompt(RetentionService svc)
        {
            name = "push-preprompt";
            AddToClassList("gm-sheet");
            AddToClassList("push-pre");
            foreach (var s in new[] { "UI/kit", "UI/home", "UI/retention" })
            {
                var sheet = Resources.Load<StyleSheet>(s);
                if (sheet != null) styleSheets.Add(sheet);
            }
            var scrim = El.Div("gm-scrim");
            Add(scrim);
            var card = new SkewBox("push-card") { Fill = new Color32(0x1e, 0x09, 0x3a, 0xf2), Border = new Color(1f, 1f, 1f, 0.14f), BorderWidth = 3.6f }.Shape(0f, 26f);
            var bell = El.Div("push-bell", El.Div("push-bell-glow"), LobbyArt.Icon("bell", "push-bell-icon"));
            var on = new Button(() => { DeviceFeel.Tick(); Cues.Play(Cues.SfxTap); Done(); svc.AskPushPermission(); }) { name = "push-turn-on" };
            on.AddToClassList("gm-collect");
            on.AddToClassList("push-on");
            var cta = new SkewBox("gm-collect-box") { Fill = new Color32(0x3d, 0xdc, 0x84, 0xff), Lip = new Color32(0x17, 0x8a, 0x52, 0xff), LipHeight = 12f }.Shape(14f, 14f);
            cta.pickingMode = PickingMode.Ignore;
            var label = El.Text(RetentionText.T("push.on"), "gm-collect-label");
            label.pickingMode = PickingMode.Ignore;
            cta.Add(label);
            on.Add(cta);
            var later = El.Btn(RetentionText.T("push.later"), () => { RetentionService.DeclinePush(); Done(); }, "push-later");
            later.name = "push-not-now";
            var copy = El.Div("push-copy",
                LobbyArt.Outlined(RetentionText.T("push.title"), "push-title"),
                El.Text(RetentionText.T("push.body"), "push-body"),
                El.Text(RetentionText.T("push.small"), "push-small"),
                El.Div("push-actions", later, on));
            card.Add(bell);
            card.Add(copy);
            Add(card);
            scrim.style.opacity = 0f;
            Motion.Fade(scrim, 0f, 1f, 0.25f);
            Motion.Enter(card, 0f, 60f, 0.36f);
            Cues.Play("sfx_ui_open");
        }

        private void Done() => RemoveFromHierarchy();
    }
}
