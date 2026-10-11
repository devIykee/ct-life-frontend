using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>
    /// Phone app "Chat" (id `npcchat`): talk to the two LLM NPCs, the AI Agent and the Sleuth. The model, the chat filter and the
    /// prompt-injection check run on the server; this screen is a message list and an input with a length limit. It shows the
    /// server's moderation states (filtered, refused), the rate limit and quota, and errors. Every NPC reply is labelled AI-generated.
    /// Tapping either NPC in the world calls <see cref="CompetitionLinks.OpenNpcChat"/>.
    /// </summary>
    public sealed class NpcChatApp : CompApp
    {
        public override string Id => CompetitionLinks.ChatApp;
        public override string Title => CompStrings.T("app.chat");
        public override string Monogram => "AI";
        public override string Color => "#0fb9b1";

        internal override CompScreen CreateView(CompetitionHub hub) => new ChatScreen(hub);
    }

    internal sealed class ChatScreen : CompScreen
    {
        private string _archetype;
        private string _npcId;
        private readonly VisualElement _tabs = El.Div(), _log = El.Div("duo-thread"), _form = El.Div("duo-composer");
        private TextField _input;
        private Label _status, _counter;
        private Button _send;
        private string _draft = "";
        private int _rendered;

        public ChatScreen(CompetitionHub hub) : base(hub, CompStrings.T("app.chat"))
        {
            CompetitionLinks.TakePendingChat(out var arch, out _npcId);
            _archetype = ChatSession.IsLlmNpc(arch) ? arch : ChatSession.Archetypes[0];
            Page.name = "npc-chat-page";
            // Messages' bubble and composer styles (board #4); the composer floats over a fade at the bottom.
            Page.AddToClassList("duo-messages");
            Page.AddToClassList("duo-npc-chat");
            Body.Add(_tabs);
            Body.Add(CompUi.Small(CompStrings.T("chat.safety")));
            Body.Add(_log);
            _form.style.backgroundImage = Background.FromTexture2D(Duo.Fade);
            Page.Add(_form);
            Select(_archetype);
        }

        private ChatSession Session => Hub.ChatFor(_archetype);

        private void Select(string archetype)
        {
            _archetype = archetype;
            _draft = "";
            _tabs.Clear();
            _tabs.Add(CompUi.Tabs(ChatSession.Archetypes.Select(a => CompStrings.T("npc." + a)).ToArray(), Array.IndexOf(ChatSession.Archetypes, archetype),
                i => { if (!Session.InFlight) Select(ChatSession.Archetypes[i]); }));
            BuildForm();
            RenderLog();
            LoadQuota();
        }

        private void LoadQuota()
        {
            var s = Session;
            var arch = _archetype;
            Run(ct => Hub.Api.Quota(ct), q => { s.SetQuota(q, Hub.NowMs); if (arch == _archetype) RefreshStatus(); }, e =>
            {
                s.Fail(null, e, Hub.NowMs);
                if (arch == _archetype) { RenderLog(); RefreshStatus(); }
            }, false);
        }

        private void BuildForm()
        {
            _form.Clear();
            _input = new TextField { value = _draft ?? "", isDelayed = false, maxLength = ChatSession.MaxChars, multiline = true };
            _input.name = "npc-chat-input";
            _input.AddToClassList("duo-composer-input");
            _input.textEdition.placeholder = CompStrings.F("chat.placeholder", CompStrings.T("npc." + _archetype));
            _input.RegisterValueChangedCallback(e => { _draft = e.newValue ?? ""; RefreshStatus(); });
            _counter = El.Text("", "duo-composer-error duo-composer-count");
            _status = El.Text("", "duo-composer-error duo-npc-status");
            _send = new Button(Send) { name = "npc-chat-send", tooltip = CompStrings.T("chat.send") };
            _send.AddToClassList("duo-composer-send");
            _send.Add(El.PassThrough(El.Div("duo-composer-send-face", new DuoGlyph(DuoGlyph.Kind.Send))));
            var field = El.Div("duo-composer-field");
            Duo.AddShadow(field, "pill");
            field.Add(El.Div("duo-composer-field-face", _input, _send));
            _form.Add(El.Div("duo-npc-meta", _status, _counter));
            _form.Add(El.Div("duo-composer-row", field));
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_status == null) return;
            var s = Session;
            long now = Hub.NowMs;
            var block = s.Check(_draft, now);
            _status.text = s.StatusLine(_draft, now);
            _counter.text = ChatSession.Normalize(_draft).Length + "/" + ChatSession.MaxChars;
            _send.SetEnabled(block == ChatBlock.None);
        }

        private void RenderLog()
        {
            _log.Clear();
            var s = Session;
            if (s.Lines.Count == 0) _log.Add(Duo.Footnote(CompStrings.F("chat.empty.thread", CompStrings.T("npc." + _archetype))));
            ChatWho? last = null;
            for (int i = 0; i < s.Lines.Count; i++)
            {
                var line = s.Lines[i];
                bool tail = i == s.Lines.Count - 1 || s.Lines[i + 1].Who != line.Who;
                _log.Add(Bubble(line, last != line.Who, tail));
                last = line.Who;
            }
            _rendered = s.Lines.Count;
            Root.schedule.Execute(() => Root.scrollOffset = new UnityEngine.Vector2(0, float.MaxValue));
        }

        private VisualElement Bubble(ChatLine l, bool gap, bool tail)
        {
            bool mine = l.Who == ChatWho.Me;
            var wrap = El.Div("duo-npc-line");
            if (gap && l.Who == ChatWho.Npc) wrap.Add(El.Text(CompStrings.T("npc." + _archetype) + "  ·  " + CompStrings.T("chat.ai"), "duo-npc-who"));
            var bubble = El.Div("duo-bubble " + (mine ? "duo-bubble-out" : "duo-bubble-in") + (tail ? " duo-bubble-tail" : "") + (l.Kind == ChatKind.Failed ? " duo-bubble-failed" : ""),
                CompUi.Text(l.Text, "duo-bubble-text"));
            if (l.Kind == ChatKind.Pending) bubble.AddToClassList("duo-npc-pending");
            wrap.Add(El.Div("duo-bubble-row " + (mine ? "duo-bubble-row-out" : "duo-bubble-row-in") + (gap ? " duo-bubble-row-gap" : ""), bubble));
            string status = null;
            switch (l.Kind)
            {
                case ChatKind.Filtered: status = CompStrings.T("chat.filtered"); break;
                case ChatKind.Pending: status = CompStrings.T("chat.thinking"); break;
                case ChatKind.Failed: status = CompStrings.T(l.ErrorKey ?? "err.generic").Replace("{0}", "—"); break;
            }
            if (status != null)
                wrap.Add(El.Text(status, "duo-bubble-status" + (mine ? " duo-bubble-status-out" : "") + (l.Kind == ChatKind.Failed ? " duo-npc-failed" : "")));
            if (l.Who == ChatWho.Npc && !string.IsNullOrEmpty(l.ReplyId))
            {
                var report = El.Btn(CompStrings.T("chat.report"), () => ReportSheet(l), "duo-link duo-npc-report");
                report.name = "npc-chat-report-" + l.ReplyId;
                wrap.Add(report);
            }
            return wrap;
        }

        private void ReportSheet(ChatLine l)
        {
            var sheet = new DuoSheet(CompStrings.T("chat.report"), CompStrings.T("chat.report.why"));
            sheet.name = "npc-chat-report-sheet";
            foreach (var reason in new[] { "spam", "scam", "harassment", "nsfw" })
            {
                var r = reason;
                sheet.AddOption(CompStrings.T("report." + r), () => SendReport(l, r), r == "scam" || r == "harassment");
            }
            sheet.AddSecondary(CompStrings.T("common.cancel"));
            ShowSheet(sheet);
        }

        private void SendReport(ChatLine l, string reasonId)
        {
            var key = Hub.KeyFor("chat:report:" + l.ReplyId);
            var k = key.KeyFor("POST", "/reports", l.ReplyId + ":" + reasonId);
            Run(ct => Hub.Api.ReportReply(l.ReplyId, reasonId, k, ct), _ =>
            {
                key.Complete();
                Bus.Publish(ToastSignal.Text(CompStrings.T("chat.reported"), ToastKind.Good));
                RenderLog();
            }, e =>
            {
                key.Settle(e);
                Bus.Publish(ToastSignal.Text(e.Message, ToastKind.Bad));
                RenderLog();
            }, true);
        }

        private void Send()
        {
            var s = Session;
            long now = Hub.NowMs;
            if (s.Check(_draft, now) != ChatBlock.None) { RefreshStatus(); return; }
            var text = ChatSession.Normalize(_draft);
            // One retained key per distinct message text: a retry after an unknown outcome replays instead of double-sending.
            var key = Hub.KeyFor("chat:" + _archetype);
            var k = key.KeyFor("POST", "/llm/chat", _archetype + ":" + text);
            var mine = s.Begin(text);
            _draft = "";
            _input.SetValueWithoutNotify("");
            var arch = _archetype;
            RenderLog();
            RefreshStatus();
            Run(ct => Hub.Api.Chat(arch, text, _npcId, k, ct), reply =>
            {
                key.Complete();
                s.Complete(mine, reply, Hub.NowMs);
                if (arch == _archetype) { RenderLog(); RefreshStatus(); }
            }, e =>
            {
                key.Settle(e);
                s.Fail(mine, e, Hub.NowMs);
                if (arch == _archetype) { RenderLog(); RefreshStatus(); }
            }, true);
        }

        protected override void Tick()
        {
            // Counts down a rate-limit cooldown and re-enables Send without a network call.
            if (Session.CooldownUntilMs > 0) RefreshStatus();
        }
    }
}
