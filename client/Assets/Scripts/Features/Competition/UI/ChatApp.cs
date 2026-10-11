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
        private readonly VisualElement _tabs = El.Div(), _log = El.Div(), _form = El.Div();
        private TextField _input;
        private Label _status, _counter;
        private Button _send;
        private string _draft = "";
        private int _rendered;
        private string _reporting;

        public ChatScreen(CompetitionHub hub) : base(hub)
        {
            CompetitionLinks.TakePendingChat(out var arch, out _npcId);
            _archetype = ChatSession.IsLlmNpc(arch) ? arch : ChatSession.Archetypes[0];
            Root.Add(CompUi.H1(CompStrings.T("app.chat")));
            Root.Add(_tabs);
            Root.Add(CompUi.Small(CompStrings.T("chat.safety")));
            Root.Add(_log);
            Root.Add(_form);
            Select(_archetype);
        }

        private ChatSession Session => Hub.ChatFor(_archetype);

        private void Select(string archetype)
        {
            _archetype = archetype;
            _draft = "";
            _reporting = null;
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
            _input = CompUi.Field(CompStrings.T("chat.input"), _draft, v => { _draft = v; RefreshStatus(); }, ChatSession.MaxChars);
            _counter = CompUi.Small("");
            _status = CompUi.Small("");
            _send = CompUi.Btn(CompStrings.T("chat.send"), Send);
            _form.Add(_input);
            _form.Add(_counter);
            _form.Add(_send);
            _form.Add(_status);
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
            if (s.Lines.Count == 0) _log.Add(CompUi.Small(CompStrings.F("chat.empty.thread", CompStrings.T("npc." + _archetype))));
            foreach (var line in s.Lines) _log.Add(Bubble(line));
            _rendered = s.Lines.Count;
        }

        private VisualElement Bubble(ChatLine l)
        {
            var who = l.Who == ChatWho.Me ? CompStrings.T("common.you") : l.Who == ChatWho.Npc ? CompStrings.T("npc." + _archetype) : "";
            var card = CompUi.Card();
            if (!string.IsNullOrEmpty(who)) card.Add(CompUi.Small(l.Who == ChatWho.Npc ? who + "  ·  " + CompStrings.T("chat.ai") : who));
            card.Add(CompUi.Text(l.Text, l.Kind == ChatKind.Pending ? "ct-body ct-muted" : "ct-body"));
            switch (l.Kind)
            {
                case ChatKind.Filtered: card.Add(CompUi.Small(CompStrings.T("chat.filtered"))); break;
                case ChatKind.Pending: card.Add(CompUi.Small(CompStrings.T("chat.thinking"))); break;
                case ChatKind.Failed: card.Add(CompUi.Error(CompStrings.T(l.ErrorKey ?? "err.generic").Replace("{0}", "-"))); break;
            }
            if (l.Who == ChatWho.Npc && !string.IsNullOrEmpty(l.ReplyId)) card.Add(ReportRow(l));
            return card;
        }

        private VisualElement ReportRow(ChatLine l)
        {
            if (_reporting != l.ReplyId)
                return CompUi.Btn(CompStrings.T("chat.report"), () => { _reporting = l.ReplyId; RenderLog(); }, "ct-btn");
            var row = El.Div("ct-app-tabs");
            row.Add(CompUi.Small(CompStrings.T("chat.report.why")));
            foreach (var reason in new[] { "spam", "scam", "harassment", "nsfw" })
            {
                var r = reason;
                row.Add(CompUi.Chip(CompStrings.T("report." + r), () => SendReport(l, r), false));
            }
            return row;
        }

        private void SendReport(ChatLine l, string reasonId)
        {
            var key = Hub.KeyFor("chat:report:" + l.ReplyId);
            var k = key.KeyFor("POST", "/reports", l.ReplyId + ":" + reasonId);
            Run(ct => Hub.Api.ReportReply(l.ReplyId, reasonId, k, ct), _ =>
            {
                key.Complete();
                _reporting = null;
                Bus.Publish(ToastSignal.Text(CompStrings.T("chat.reported"), ToastKind.Good));
                RenderLog();
            }, e =>
            {
                key.Settle(e);
                _reporting = null;
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
