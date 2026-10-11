using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>
    /// Phone app "X link" (id `xlink`): the verified X link screen for Settings. Linking opens the system browser at the URL the
    /// server provides (the OAuth round trip happens on the backend, never in a WebView and never in this app); afterwards the app
    /// shows the linked @handle and avatar and can unlink. Display only: no Sign in with X, no posting, no X action is ever rewarded.
    /// <see cref="XLinkPanel.Build"/> returns the same UI as a section for the Settings app.
    /// </summary>
    public sealed class XLinkApp : CompApp
    {
        public override string Id => CompetitionLinks.XLinkApp;
        public override string Title => CompStrings.T("app.xlink");
        public override string Monogram => "X";
        public override string Color => "#2d3436";

        internal override CompScreen CreateView(CompetitionHub hub)
        {
            var s = new XLinkScreen(hub, true);
            return s;
        }
    }

    /// <summary>Embeddable section for the Settings app (hook in docs/progress/requests.md).</summary>
    public static class XLinkPanel
    {
        public static VisualElement Build()
        {
            var hub = CompetitionHub.Acquire();
            var screen = new XLinkScreen(hub, false);
            // The screen owns its lifetime through the panel detach event; release the hub reference with it.
            screen.Root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == screen.Root) hub.Release(); });
            return screen.Root;
        }
    }

    internal sealed class XLinkScreen : CompScreen
    {
        /// <summary>Polls GET /x/link after the browser opens (every 3 s, at most 40 times) until `x_link_update` or a linked answer arrives.</summary>
        private const int PollEveryTicks = 3, MaxPolls = 40;

        private readonly VisualElement _body = El.Div();
        private int _ticks, _polls;
        private bool _waiting, _confirmUnlink;
        private Texture2D _avatar;
        private XLinkInfo _info;
        private Label _wait;

        public XLinkScreen(CompetitionHub hub, bool title) : base(hub)
        {
            if (title) Root.Add(CompUi.H1(CompStrings.T("app.xlink")));
            Root.Add(_body);
            Hub.Feed.XLinkEvent += OnUpdate;
            Reload();
        }

        protected override void OnDisposed()
        {
            Hub.Feed.XLinkEvent -= OnUpdate;
            if (_avatar != null) { UnityEngine.Object.Destroy(_avatar); _avatar = null; }
        }

        private void OnUpdate(XLinkUpdateMsg m)
        {
            if (!Alive) return;
            _waiting = false;
            Reload();
        }

        private void Reload()
        {
            Load(_body, ct => Hub.Api.XLink(ct), (b, info) => Render(b, info));
        }

        private void Render(VisualElement body, XLinkInfo info)
        {
            _info = info;
            _wait = null;
            body.Add(CompUi.Small(CompStrings.T("x.note")));
            var failed = Hub.Feed.LastXLink;
            if (failed != null && failed.Status == "failed")
                body.Add(CompUi.Error(failed.ReasonId == "X_ACCOUNT_ALREADY_LINKED" ? CompStrings.T("err.xAlreadyLinked") : CompStrings.T("x.failed")));
            if (info == null)
            {
                body.Add(CompUi.Card(CompUi.H2(CompStrings.T("x.notLinked")), CompUi.Text(CompStrings.T("x.why"))));
                var status = CompUi.Small("");
                body.Add(CompUi.Btn(CompStrings.T(_waiting ? "x.waiting.retry" : "x.link"), () => StartLink(status)));
                if (_waiting)
                {
                    _wait = CompUi.Small(CompStrings.T("x.waiting"));
                    body.Add(_wait);
                }
                body.Add(status);
                return;
            }
            _waiting = false;
            var card = CompUi.Card();
            var avatar = El.Div("ct-avatar");
            avatar.style.width = 56;
            avatar.style.height = 56;
            avatar.style.borderTopLeftRadius = avatar.style.borderTopRightRadius = avatar.style.borderBottomLeftRadius = avatar.style.borderBottomRightRadius = 28;
            avatar.style.backgroundColor = new Color(0.18f, 0.2f, 0.23f);
            var handle = XLinkRules.Display(info.XHandle);
            card.Add(CompUi.Row(avatar, CompUi.H2(handle ?? CompStrings.T("x.handleUnknown"))));
            card.Add(CompUi.Small(CompStrings.T("x.source")));
            if (info.VerifiedAtMs.HasValue) card.Add(CompUi.Pair(CompStrings.T("x.verified"), CompTime.Date(info.VerifiedAtMs.Value)));
            body.Add(card);
            if (info.XAvatarUrl != null) LoadAvatar(avatar, info.XAvatarUrl);
            var unlinkStatus = CompUi.Small("");
            if (!_confirmUnlink) body.Add(CompUi.Btn(CompStrings.T("x.unlink"), () => { _confirmUnlink = true; Reload(); }, "ct-btn ct-btn-danger"));
            else
            {
                body.Add(CompUi.Text(CompStrings.T("x.unlink.confirm")));
                body.Add(CompUi.Row(CompUi.Btn(CompStrings.T("x.unlink.yes"), () => Unlink(unlinkStatus), "ct-btn ct-btn-danger"),
                    CompUi.Btn(CompStrings.T("common.cancel"), () => { _confirmUnlink = false; Reload(); }, "ct-btn")));
            }
            body.Add(unlinkStatus);
        }

        private void StartLink(Label status)
        {
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.XLinkStart(ct), url =>
            {
                status.text = "";
                _waiting = true;
                _polls = 0;
                _ticks = 0;
                // The system browser (or Custom Tab), never a WebView: the player signs in to X on X's own page.
                Application.OpenURL(url);
                Reload();
            }, e => status.text = e.Message, false);
        }

        private void Unlink(Label status)
        {
            var key = Hub.KeyFor("x:unlink");
            var k = key.KeyFor("DELETE", "/x/link", "");
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.XUnlink(k, ct), _ =>
            {
                key.Complete();
                _confirmUnlink = false;
                Hub.Feed.LastXLink = null;
                Bus.Publish(ToastSignal.Text(CompStrings.T("feed.xUnlinked"), ToastKind.Info));
                Reload();
            }, e =>
            {
                key.Settle(e);
                // 404 on delete: nothing was linked any more, which is the state we wanted.
                if (e.Is("NOT_FOUND")) { key.Complete(); _confirmUnlink = false; Reload(); return; }
                status.text = e.Message;
            }, true);
        }

        protected override void Tick()
        {
            if (!_waiting || _info != null) return;
            if (++_ticks % PollEveryTicks != 0) return;
            if (++_polls > MaxPolls)
            {
                _waiting = false;
                Reload();
                return;
            }
            Run(ct => Hub.Api.XLink(ct), info =>
            {
                if (info != null) { _waiting = false; Reload(); }
            }, null, false);
        }

        private void LoadAvatar(VisualElement target, string url)
        {
            Run(async ct =>
            {
                using (var req = UnityWebRequest.Get(url))
                {
                    req.timeout = 15;
                    req.redirectLimit = 0;
                    var op = req.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (ct.IsCancellationRequested) { req.Abort(); ct.ThrowIfCancellationRequested(); }
                        // 512 KB: an avatar is small; refuse anything bigger than that while it streams.
                        if ((long)req.downloadedBytes > 512 * 1024) { req.Abort(); return (Texture2D)null; }
                        await Awaitable.NextFrameAsync(ct);
                    }
                    if (req.result != UnityWebRequest.Result.Success) return null;
                    var type = req.GetResponseHeader("Content-Type") ?? "";
                    if (!(type.StartsWith("image/png", StringComparison.OrdinalIgnoreCase) || type.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase))) return null;
                    var bytes = req.downloadHandler.data;
                    if (bytes == null || bytes.Length == 0 || bytes.Length > 512 * 1024) return null;
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "x_avatar" };
                    if (!tex.LoadImage(bytes, true) || tex.width > 1024 || tex.height > 1024) { UnityEngine.Object.Destroy(tex); return null; }
                    return tex;
                }
            }, tex =>
            {
                if (tex == null) return;
                if (_avatar != null) UnityEngine.Object.Destroy(_avatar);
                _avatar = tex;
                target.style.backgroundImage = new StyleBackground(tex);
            }, null, false);
        }
    }
}
