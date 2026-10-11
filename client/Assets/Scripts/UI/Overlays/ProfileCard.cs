using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.World;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Tap an avatar: players get a profile card (`GET /v1/users/{id}`) with Block and Report (feature #12;
    /// dApp Store publisher policy requires report and block on every avatar). NPCs get their archetype card;
    /// the Giveaway Guy offers "Drop wallet", which costs Cash by design (npc_tap → toast_drained).
    /// </summary>
    public sealed class ProfileCard
    {
        private readonly VisualElement _layer;
        private readonly Subscriptions _subs = new Subscriptions();
        private VisualElement _open;

        public ProfileCard(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _subs.On<AvatarTappedSignal>(OnTapped);
        }

        private void Close()
        {
            _open?.RemoveFromHierarchy();
            _open = null;
        }

        private VisualElement Sheet(params VisualElement[] children)
        {
            Close();
            var sheet = El.Div("ct-sheet", children);
            var backdrop = El.Div("ct-modal-backdrop ct-modal-bottom", sheet);
            backdrop.pickingMode = PickingMode.Position;
            backdrop.RegisterCallback<PointerDownEvent>(e => { if (e.target == backdrop) Close(); });
            _layer.Add(backdrop);
            _open = backdrop;
            return sheet;
        }

        private void OnTapped(AvatarTappedSignal s)
        {
            if (!string.IsNullOrEmpty(s.npcId)) ShowNpc(s);
            else if (!string.IsNullOrEmpty(s.userId)) ShowPlayer(s.userId, s.displayName);
        }

        private void ShowNpc(AvatarTappedSignal s)
        {
            var reg = Services.Get<ConfigStore>().Registries;
            var def = reg.Archetype(s.archetypeId);
            var sheet = Sheet(El.Text(def?.name ?? s.displayName, "ct-h1"), El.Text("NPC", "ct-badge"));
            if (s.archetypeId == "giveaway_guy")
            {
                sheet.Add(El.Text("\"Like, RT, drop your wallets, join my TG\"", "ct-body"));
                sheet.Add(El.Btn("Drop wallet", () =>
                {
                    VenueSession.Current?.Channel?.NpcTap(s.npcId);
                    Close();
                }, "ct-btn ct-btn-danger"));
            }
            sheet.Add(El.Btn("Close", Close, "ct-btn ct-btn-ghost"));
        }

        private async void ShowPlayer(string userId, string name)
        {
            var title = El.Text(name ?? "Player", "ct-h1");
            var info = El.Text("Loading…", "ct-small");
            var sheet = Sheet(title, info);
            sheet.Add(El.Div("ct-row",
                El.Btn("Block", () => Block(userId), "ct-btn ct-btn-ghost"),
                El.Btn("Report", () => ShowReport("user", userId, name), "ct-btn ct-btn-danger")));
            sheet.Add(El.Btn("Close", Close, "ct-btn ct-btn-ghost"));
            try
            {
                var p = await Services.Get<Api>().GetUser(userId);
                title.text = p.Alias + (p.IsSeeker ? " ✓" : "");
                var tribe = Services.Get<ConfigStore>().Registries.Tribe(p.TribeId)?.name ?? p.TribeId;
                info.text = $"{tribe} · Net worth {Money.Cash(p.NetWorthCents, true)}" + (string.IsNullOrEmpty(p.SkrName) ? "" : $" · {p.SkrName}");
            }
            catch (Exception e)
            {
                info.text = e is ApiException api ? Strings.Error(api.Code, api.ServerMessage) : e.Message;
            }
        }

        private async void Block(string userId)
        {
            try
            {
                await Services.Get<Api>().Block(userId);
                Bus.Publish(ToastSignal.Text("Blocked. You won't see their messages.", ToastKind.Good));
                Close();
            }
            catch (Exception e)
            {
                Bus.Publish(ToastSignal.Text(e is ApiException api ? Strings.Error(api.Code) : e.Message, ToastKind.Bad));
            }
        }

        /// <summary>Report sheet: reasons from report_reasons.json. targetType user | message | space | call | post | ad.</summary>
        public void ShowReport(string targetType, string targetId, string label)
        {
            var reasons = Services.Get<ConfigStore>().Registries.ReportReasons;
            var sheet = Sheet(El.Text("Report " + (label ?? targetType), "ct-h1"), El.Text("Why?", "ct-body"));
            var list = new List<SimpleDef>(reasons);
            if (list.Count == 0) list.AddRange(new[] { new SimpleDef { id = "spam" }, new SimpleDef { id = "scam" }, new SimpleDef { id = "harassment" } });
            foreach (var r in list)
            {
                var reason = r;
                sheet.Add(El.Btn(reason.name ?? reason.id, async () =>
                {
                    try
                    {
                        await Services.Get<Api>().Report(targetType, targetId, reason.id);
                        Bus.Publish(ToastSignal.Text("Thanks. Our moderators will review it.", ToastKind.Good));
                    }
                    catch (Exception e)
                    {
                        Bus.Publish(ToastSignal.Text(e is ApiException api ? Strings.Error(api.Code) : e.Message, ToastKind.Bad));
                    }
                    Close();
                }, "ct-btn"));
            }
            sheet.Add(El.Btn("Cancel", Close, "ct-btn ct-btn-ghost"));
        }
    }
}
