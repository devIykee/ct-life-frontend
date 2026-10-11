using System;
using System.Collections.Generic;
using System.Threading;
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
    public sealed class ProfileCard : IDisposable
    {
        /// <summary>Set by the composition root (Phase 3 social): extra actions under Block/Report. (userId, name, close) → row or null.</summary>
        public static Func<string, string, Action, VisualElement> PlayerActions;
        /// <summary>Set by the composition root (City Life): knock on a player's home.</summary>
        public static Action<string, string> VisitHome;
        /// <summary>Extra NPC actions (archetypeId, npcId, close), e.g. Phase 6 "Talk" for the AI Agent and Sleuth; null = none.</summary>
        public static Func<string, string, Action, VisualElement> NpcActions;

        private readonly VisualElement _layer;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly Session _session;
        private readonly HashSet<VisualElement> _pending = new HashSet<VisualElement>();
        private string _userId;
        private bool _signedIn, _disposed;
        private VisualElement _open;
        private CancellationTokenSource _read;

        public ProfileCard(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _layer.RegisterCallback<DetachFromPanelEvent>(OnLayerDetached);
            _subs.On<AvatarTappedSignal>(OnTapped);
            if (Services.TryGet<Session>(out var session))
            {
                _session = session;
                _userId = session.UserId;
                _signedIn = session.IsSignedIn;
                session.Changed += OnSessionChanged;
            }
        }

        private bool Owns(VisualElement sheet) => !_disposed && _open != null &&
            sheet.parent == _open && _open.parent == _layer &&
            (_session == null || (_session.UserId == _userId && _session.IsSignedIn == _signedIn &&
                Services.TryGet<Session>(out var current) && ReferenceEquals(current, _session)));

        private void Close(VisualElement sheet) { if (Owns(sheet)) Close(); }

        private void Close()
        {
            var old = _open;
            _open = null;
            var read = _read;
            _read = null;
            // Reads are cancellable. Started Block/Report calls deliberately keep their original lifetime.
            read?.Cancel();
            if (old != null)
            {
                old.UnregisterCallback<DetachFromPanelEvent>(OnSheetDetached);
                old.RemoveFromHierarchy();
            }
        }

        private void OnLayerDetached(DetachFromPanelEvent e) { if (e.target == _layer) Dispose(); }
        private void OnSheetDetached(DetachFromPanelEvent e) { if (e.target == _open) Close(); }
        private void OnSessionChanged()
        {
            if (_disposed || (_userId == _session.UserId && _signedIn == _session.IsSignedIn)) return;
            _userId = _session.UserId;
            _signedIn = _session.IsSignedIn;
            Close();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subs.Dispose();
            _layer.UnregisterCallback<DetachFromPanelEvent>(OnLayerDetached);
            if (_session != null) _session.Changed -= OnSessionChanged;
            Close();
            _pending.Clear();
        }

        private VisualElement Sheet(params VisualElement[] children)
        {
            Close();
            var sheet = El.VerticalScroll("ct-sheet ct-profile-sheet");
            foreach (var child in children) if (child != null) sheet.Add(child);
            sheet.Insert(0, El.Div("ct-sheet-handle"));
            var backdrop = El.Div("ct-modal-backdrop ct-modal-bottom ct-profile-backdrop", sheet);
            backdrop.pickingMode = PickingMode.Position;
            backdrop.RegisterCallback<PointerDownEvent>(e => { if (e.target == backdrop) Close(sheet); });
            backdrop.RegisterCallback<DetachFromPanelEvent>(OnSheetDetached);
            _open = backdrop;
            _layer.Add(backdrop);
            return sheet;
        }

        private void OnTapped(AvatarTappedSignal s)
        {
            if (_disposed) return;
            if (!string.IsNullOrEmpty(s.npcId)) ShowNpc(s);
            else if (!string.IsNullOrEmpty(s.userId)) ShowPlayer(s.userId, s.displayName);
        }

        private void ShowNpc(AvatarTappedSignal s)
        {
            var reg = Services.Get<ConfigStore>().Registries;
            var def = reg.Archetype(s.archetypeId);
            var sheet = Sheet(El.Text(def?.name ?? s.displayName, "ct-h1"), El.Text(Strings.T("profile.npc"), "ct-badge"));
            if (s.archetypeId == "giveaway_guy")
            {
                sheet.Add(El.Text(Strings.T("profile.giveaway"), "ct-body"));
                sheet.Add(El.Btn(Strings.T("profile.dropWallet"), () =>
                {
                    if (!Owns(sheet)) return;
                    VenueSession.Current?.Channel?.NpcTap(s.npcId);
                    Close(sheet);
                }, "ct-btn ct-btn-danger"));
            }
            var talk = NpcActions?.Invoke(s.archetypeId, s.npcId, () => Close(sheet));
            if (talk != null) sheet.Add(talk);
            sheet.Add(El.Btn(Strings.T("common.close"), () => Close(sheet), "ct-btn ct-btn-ghost"));
        }

        private async void ShowPlayer(string userId, string name)
        {
            var title = El.Text(name ?? Strings.T("profile.player"), "ct-h1");
            var info = El.Text(Strings.T("common.loading"), "ct-small");
            var sheet = Sheet(title, info);
            var block = El.Btn(Strings.T("profile.block"), () => Block(userId, sheet), "ct-btn ct-btn-ghost");
            block.name = "profile-block";
            var report = El.Btn(Strings.T("profile.report"), () => { if (Owns(sheet)) ShowReport("user", userId, name); }, "ct-btn ct-btn-danger");
            report.name = "profile-report";
            sheet.Add(El.Div("ct-row", block, report));
            if (VisitHome != null && Phases.IsOpen(4))
                sheet.Add(El.Btn("Visit home", () => { if (!Owns(sheet)) return; VisitHome(userId, name); Close(sheet); }, "ct-btn"));
            var extra = PlayerActions?.Invoke(userId, name, () => Close(sheet));
            if (extra != null) sheet.Add(extra);
            sheet.Add(El.Btn(Strings.T("common.close"), () => Close(sheet), "ct-btn ct-btn-ghost"));
            var read = new CancellationTokenSource();
            _read = read;
            try
            {
                var p = await Services.Get<Api>().GetUser(userId, read.Token);
                if (!Owns(sheet) || read.IsCancellationRequested) return;
                title.text = p.Alias + (p.IsSeeker ? " ✓" : "");
                var tribe = Services.Get<ConfigStore>().Registries.Tribe(p.TribeId)?.name ?? p.TribeId;
                info.text = Strings.F("profile.netWorth", tribe, Money.Cash(p.NetWorthCents, true)) + (string.IsNullOrEmpty(p.SkrName) ? "" : $" · {p.SkrName}");
            }
            catch (OperationCanceledException) when (read.IsCancellationRequested) { }
            catch (Exception e)
            {
                Log.Exception("profile", e);
                if (Owns(sheet)) info.text = e is ApiException api ? Strings.Error(api.Code) : Strings.T("common.error");
            }
            finally
            {
                if (ReferenceEquals(_read, read)) _read = null;
                read.Dispose();
            }
        }

        private bool BeginMutation(VisualElement sheet)
        {
            if (!Owns(sheet) || !_pending.Add(sheet)) return false;
            foreach (var button in sheet.Query<Button>().ToList())
                if (button.name == "profile-block" || (button.name?.StartsWith("profile-reason-", StringComparison.Ordinal) == true))
                    button.SetEnabled(false);
            return true;
        }

        private void EndMutation(VisualElement sheet)
        {
            _pending.Remove(sheet);
            if (!Owns(sheet)) return;
            foreach (var button in sheet.Query<Button>().ToList())
                if (button.name == "profile-block" || (button.name?.StartsWith("profile-reason-", StringComparison.Ordinal) == true))
                    button.SetEnabled(true);
        }

        private async void Block(string userId, VisualElement sheet)
        {
            if (!BeginMutation(sheet)) return;
            try
            {
                await Services.Get<Api>().Block(userId);
                Bus.Publish(new UserBlockedSignal { userId = userId });
                if (!Owns(sheet)) return;
                Bus.Publish(ToastSignal.Text(Strings.T("profile.blocked"), ToastKind.Good));
                Close(sheet);
            }
            catch (Exception e)
            {
                Log.Exception("profile", e);
                if (Owns(sheet)) Bus.Publish(ToastSignal.Text(e is ApiException api ? Strings.Error(api.Code) : Strings.T("common.error"), ToastKind.Bad));
            }
            finally { EndMutation(sheet); }
        }

        /// <summary>Report sheet: reasons from report_reasons.json. targetType user | message | space | call | post | ad.</summary>
        public void ShowReport(string targetType, string targetId, string label)
        {
            if (_disposed) return;
            var reasons = Services.Get<ConfigStore>().Registries.ReportReasons;
            var sheet = Sheet(El.Text(Strings.F("profile.reportTitle", label ?? Strings.ReportTarget(targetType)), "ct-h1"), El.Text(Strings.T("profile.reportWhy"), "ct-body"));
            var list = new List<SimpleDef>(reasons);
            if (list.Count == 0) list.AddRange(new[] { new SimpleDef { id = "spam" }, new SimpleDef { id = "scam" }, new SimpleDef { id = "harassment" } });
            foreach (var r in list)
            {
                var reason = r;
                var button = El.Btn(Strings.ReportReason(reason.id, reason.name), async () =>
                {
                    if (!BeginMutation(sheet)) return;
                    try
                    {
                        await Services.Get<Api>().Report(targetType, targetId, reason.id);
                        if (Owns(sheet)) Bus.Publish(ToastSignal.Text(Strings.T("profile.reportThanks"), ToastKind.Good));
                    }
                    catch (Exception e)
                    {
                        Log.Exception("profile", e);
                        if (Owns(sheet)) Bus.Publish(ToastSignal.Text(e is ApiException api ? Strings.Error(api.Code) : Strings.T("common.error"), ToastKind.Bad));
                    }
                    finally { EndMutation(sheet); }
                    Close(sheet);
                }, "ct-btn");
                button.name = "profile-reason-" + reason.id;
                sheet.Add(button);
            }
            sheet.Add(El.Btn(Strings.T("common.cancel"), () => Close(sheet), "ct-btn ct-btn-ghost"));
        }
    }
}
