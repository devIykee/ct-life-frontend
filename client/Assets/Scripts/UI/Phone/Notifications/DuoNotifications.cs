using System;
using System.Collections.Generic;
using CTLife.Core;

namespace CTLife.UI
{
    /// <summary>One banner: a real event from a feature (incoming DM …). Text is shown literally (rich text off).</summary>
    public sealed class DuoNotification
    {
        /// <summary>Phone app id the banner belongs to (icon, per-app setting, foreground suppression).</summary>
        public string AppId;
        /// <summary>Conversation/screen inside the app (a DM peer id); null = the app as a whole.</summary>
        public string ThreadKey;
        public string Title, Body;
        /// <summary>Optional quiet line beside the title ("Message request").</summary>
        public string Detail;
        /// <summary>Tap: open the Duo at this thread/screen.</summary>
        public Action Open;

        public int Id { get; internal set; }
        public float PostedAt { get; internal set; }
        /// <summary>When the banner leaves; only the front banner's clock runs.</summary>
        public float Deadline { get; internal set; } = float.PositiveInfinity;
    }

    public enum DuoPostResult { Shown, Replaced, SuppressedForeground, SuppressedDoNotDisturb, SuppressedDisabled, Rejected }

    /// <summary>Banner preferences (per device): banners on/off, per app, and Do Not Disturb (silences banners, keeps badges).</summary>
    public sealed class DuoNotificationSettings
    {
        public const string BannersKey = "ctlife.duo.notify.banners", DndKey = "ctlife.duo.notify.dnd", AppKeyPrefix = "ctlife.duo.notify.app.";
        public event Action Changed;

        public bool Banners
        {
            get => DuoPrefs.GetBool(BannersKey, true);
            set { if (value == Banners) return; DuoPrefs.SetBool(BannersKey, value); Changed?.Invoke(); }
        }

        public bool DoNotDisturb
        {
            get => DuoPrefs.GetBool(DndKey, false);
            set { if (value == DoNotDisturb) return; DuoPrefs.SetBool(DndKey, value); Changed?.Invoke(); }
        }

        public bool AppBanners(string appId) => string.IsNullOrEmpty(appId) || DuoPrefs.GetBool(AppKeyPrefix + appId, true);

        public void SetAppBanners(string appId, bool on)
        {
            if (string.IsNullOrEmpty(appId) || on == AppBanners(appId)) return;
            DuoPrefs.SetBool(AppKeyPrefix + appId, on);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Banner stack and badge counts, free of UI so it is unit-tested. Newest banner on top; only the top banner's
    /// 4 s clock runs (the one peeking behind it starts its own when it comes to the front). A newer banner for the
    /// same conversation replaces the older one. No banner while the Duo shows that app/conversation.
    /// </summary>
    public sealed class DuoNotificationQueue
    {
        public const float Lifetime = 4f;
        public const int MaxStack = 4;

        private readonly List<DuoNotification> _stack = new List<DuoNotification>();
        private readonly Dictionary<string, int> _badges = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _nextId = 1;
        private bool _duoOpen;
        private string _foregroundApp, _foregroundThread;

        public DuoNotificationQueue(DuoNotificationSettings settings) { Settings = settings ?? new DuoNotificationSettings(); }

        public DuoNotificationSettings Settings { get; }
        /// <summary>Newest first. The UI renders the first and lets the second peek.</summary>
        public IReadOnlyList<DuoNotification> Stack => _stack;
        public DuoNotification Top => _stack.Count > 0 ? _stack[0] : null;
        public event Action Changed;
        public event Action BadgesChanged;

        public bool DuoOpen => _duoOpen;
        public string ForegroundApp => _foregroundApp;
        public string ForegroundThread => _foregroundThread;

        /// <summary>What the Duo shows now (PhoneShell): closed, at home (app null) or an app.</summary>
        public void SetForeground(bool duoOpen, string appId)
        {
            _duoOpen = duoOpen;
            if (!string.Equals(_foregroundApp, appId, StringComparison.Ordinal)) _foregroundThread = null;
            _foregroundApp = duoOpen ? appId : null;
            RemoveForeground();
        }

        /// <summary>The conversation the foreground app shows (null = its list). Ignored for an app not in front.</summary>
        public void SetForegroundThread(string appId, string threadKey)
        {
            if (!string.Equals(_foregroundApp, appId, StringComparison.Ordinal)) return;
            _foregroundThread = threadKey;
            RemoveForeground();
        }

        public bool IsForeground(string appId, string threadKey) =>
            _duoOpen && appId != null && string.Equals(_foregroundApp, appId, StringComparison.Ordinal)
            && (_foregroundThread == null || string.Equals(_foregroundThread, threadKey, StringComparison.Ordinal));

        public DuoPostResult Post(DuoNotification n, float now)
        {
            if (n == null || string.IsNullOrEmpty(n.AppId) || (string.IsNullOrWhiteSpace(n.Title) && string.IsNullOrWhiteSpace(n.Body)))
                return DuoPostResult.Rejected;
            if (IsForeground(n.AppId, n.ThreadKey)) return DuoPostResult.SuppressedForeground;
            if (Settings.DoNotDisturb) return DuoPostResult.SuppressedDoNotDisturb;
            if (!Settings.Banners || !Settings.AppBanners(n.AppId)) return DuoPostResult.SuppressedDisabled;
            bool replaced = false;
            if (n.ThreadKey != null)
                for (int i = _stack.Count - 1; i >= 0; i--)
                    if (_stack[i].AppId == n.AppId && _stack[i].ThreadKey == n.ThreadKey) { _stack.RemoveAt(i); replaced = true; }
            if (_stack.Count > 0) _stack[0].Deadline = float.PositiveInfinity; // pause the banner that now peeks
            n.Id = _nextId++;
            n.PostedAt = now;
            n.Deadline = now + Lifetime;
            _stack.Insert(0, n);
            while (_stack.Count > MaxStack) _stack.RemoveAt(_stack.Count - 1);
            Changed?.Invoke();
            return replaced ? DuoPostResult.Replaced : DuoPostResult.Shown;
        }

        /// <summary>Expires the front banner; the next one comes forward with a fresh lifetime.</summary>
        public bool Tick(float now)
        {
            var top = Top;
            if (top == null) return false;
            if (float.IsPositiveInfinity(top.Deadline)) { top.Deadline = now + Lifetime; return false; } // came forward
            if (now < top.Deadline) return false;
            _stack.RemoveAt(0);
            Promote(now);
            Changed?.Invoke();
            return true;
        }

        public bool Dismiss(int id, float now)
        {
            int index = _stack.FindIndex(x => x.Id == id);
            if (index < 0) return false;
            _stack.RemoveAt(index);
            if (index == 0) Promote(now);
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            if (_stack.Count == 0) return;
            _stack.Clear();
            Changed?.Invoke();
        }

        private void Promote(float now)
        {
            if (_stack.Count > 0 && float.IsPositiveInfinity(_stack[0].Deadline)) _stack[0].Deadline = now + Lifetime;
        }

        private void RemoveForeground()
        {
            // A banner that comes forward here keeps a paused clock; the next Tick starts it.
            if (_stack.RemoveAll(x => IsForeground(x.AppId, x.ThreadKey)) > 0) Changed?.Invoke();
        }

        // ------------------------------------------------------------- badges

        /// <summary>Unread count for an app, from the feature's own read state (cleared when read). Negative = 0.</summary>
        public void SetBadge(string appId, int count)
        {
            if (string.IsNullOrEmpty(appId)) return;
            count = Math.Max(0, count);
            if (Badge(appId) == count) return;
            if (count == 0) _badges.Remove(appId); else _badges[appId] = count;
            BadgesChanged?.Invoke();
        }

        public int Badge(string appId) => appId != null && _badges.TryGetValue(appId, out var c) ? c : 0;

        public int TotalBadge
        {
            get { int t = 0; foreach (var v in _badges.Values) t += v; return t; }
        }

        public void ClearBadges()
        {
            if (_badges.Count == 0) return;
            _badges.Clear();
            BadgesChanged?.Invoke();
        }

        /// <summary>"1" … "99", "99+"; empty for none.</summary>
        public static string BadgeText(int count) => count <= 0 ? "" : count > 99 ? "99+" : count.ToString();
    }

    /// <summary>Raised on the Bus when any Duo badge count changes (HUD phone button, lobby phone tile, icons).</summary>
    public struct DuoBadgesChangedSignal { public int total; }

    /// <summary>The game's one notification center. Features post real events; the banner layer renders the stack.</summary>
    public static class DuoNotificationCenter
    {
        private static DuoNotificationQueue _queue;

        public static DuoNotificationQueue Queue
        {
            get
            {
                if (_queue != null) return _queue;
                _queue = new DuoNotificationQueue(new DuoNotificationSettings());
                _queue.BadgesChanged += () => Bus.Publish(new DuoBadgesChangedSignal { total = _queue.TotalBadge });
                return _queue;
            }
        }

        public static DuoPostResult Post(DuoNotification n) => Queue.Post(n, UnityEngine.Time.unscaledTime);
        public static void SetBadge(string appId, int count) => Queue.SetBadge(appId, count);
        public static int Badge(string appId) => Queue.Badge(appId);
        public static int TotalBadge => Queue.TotalBadge;

        /// <summary>Sign-out: no banners or counts carry over to the next identity.</summary>
        public static void Reset()
        {
            if (_queue == null) return;
            _queue.Clear();
            _queue.ClearBadges();
        }
    }
}
