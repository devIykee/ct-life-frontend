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
            // Embedded in a Settings group: the bare scroll view, without the Duo frame's nav padding.
            screen.Root.RemoveFromHierarchy();
            screen.Root.contentContainer.RemoveFromClassList("duo-scroll-content");
            screen.Root.AddToClassList("duo-xlink-embedded");
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
        private bool _waiting;
        private Texture2D _avatar;
        private XLinkInfo _info;
        private Label _wait;

        public XLinkScreen(CompetitionHub hub, bool title) : base(hub, title ? CompStrings.T("app.xlink") : "")
        {
            Page.name = "xlink-page";
            Page.AddToClassList("duo-xlink");
            Body.Add(_body);
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
            var failed = Hub.Feed.LastXLink;
            if (failed != null && failed.Status == "failed")
                body.Add(Duo.Notice(failed.ReasonId == "X_ACCOUNT_ALREADY_LINKED" ? CompStrings.T("err.xAlreadyLinked") : CompStrings.T("x.failed"), Duo.Tone.Danger));
            if (info == null)
            {
                var status = CompUi.Small("");
                var empty = Duo.Empty(CompStrings.T("x.notLinked"), CompStrings.T("x.why"));
                var link = CompUi.Btn(CompStrings.T(_waiting ? "x.waiting.retry" : "x.link"), () => StartLink(status));
                link.name = "xlink-link";
                empty.Add(link);
                body.Add(empty);
                if (_waiting)
                {
                    _wait = CompUi.Small(CompStrings.T("x.waiting"));
                    body.Add(_wait);
                }
                body.Add(status);
                body.Add(CompUi.Small(CompStrings.T("x.note")));
                return;
            }
            _waiting = false;
            var avatar = El.Div("duo-xlink-avatar");
            avatar.pickingMode = PickingMode.Ignore;
            var handle = XLinkRules.Display(info.XHandle);
            var row = new DuoRow().Leading(avatar).Title(handle ?? CompStrings.T("x.handleUnknown"), "duo-row-title-large").Subtitle(CompStrings.T("x.source"), twoLines: true);
            row.AddToClassList("duo-row-static");
            row.AddToClassList("duo-row-profile");
            row.name = "xlink-account";
            row.Trailing(CompUi.Tag(CompStrings.T("x.tag.linked"), "green"));
            body.Add(row);
            if (info.VerifiedAtMs.HasValue) body.Add(CompUi.Pair(CompStrings.T("x.verified"), CompTime.Date(info.VerifiedAtMs.Value)));
            if (info.XAvatarUrl != null) LoadAvatar(avatar, info.XAvatarUrl);
            var unlink = CompUi.Btn(CompStrings.T("x.unlink"), UnlinkSheet, CompUi.Danger);
            unlink.name = "xlink-unlink";
            body.Add(CompUi.Row(unlink));
            body.Add(CompUi.Small(CompStrings.T("x.note")));
        }

        private void UnlinkSheet()
        {
            var sheet = new DuoSheet(CompStrings.T("x.unlink"), CompStrings.T("x.unlink.confirm"));
            sheet.name = "xlink-unlink-sheet";
            var status = CompUi.Small("");
            sheet.AddContent(status);
            Button go = null;
            go = CompUi.Btn(CompStrings.T("x.unlink.yes"), () => { go.SetEnabled(false); Unlink(status, sheet, go); }, CompUi.Danger);
            sheet.AddContent(go);
            sheet.AddSecondary(CompStrings.T("common.cancel"));
            ShowSheet(sheet);
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

        private void Unlink(Label status, DuoSheet sheet, Button go)
        {
            var key = Hub.KeyFor("x:unlink");
            var k = key.KeyFor("DELETE", "/x/link", "");
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.XUnlink(k, ct), _ =>
            {
                key.Complete();
                sheet.Close();
                Hub.Feed.LastXLink = null;
                Bus.Publish(ToastSignal.Text(CompStrings.T("feed.xUnlinked"), ToastKind.Info));
                Reload();
            }, e =>
            {
                key.Settle(e);
                // 404 on delete: nothing was linked any more, which is the state we wanted.
                if (e.Is("NOT_FOUND")) { key.Complete(); sheet.Close(); Reload(); return; }
                status.text = e.Message;
                go.SetEnabled(true);
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
