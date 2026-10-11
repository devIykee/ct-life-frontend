using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CTLife.Core;
using CTLife.Net;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Home needs: one owned request at a time, with no local reward prediction.</summary>
    public sealed class HomeApp : PhoneApp
    {
        private sealed class View
        {
            public VisualElement Page;
            public Label Status;
            public readonly List<Button> Buttons = new List<Button>();
            public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            public Session Session;
            public PlayerProfile Profile;
            public string UserId;
            public Action SessionChanged;
            public Button Visit;
            public bool Returning;
        }

        private readonly Action _enterHome, _returnToCity;
        private readonly Func<bool> _canEnterHome, _atOwnHome;
        public HomeApp(Action enterHome = null, Action returnToCity = null, Func<bool> canEnterHome = null, Func<bool> atOwnHome = null)
        { _enterHome = enterHome; _returnToCity = returnToCity; _canEnterHome = canEnterHome; _atOwnHome = atOwnHome; }

        private View _view;
        private CancellationTokenSource _request;
        public override string Id => "home";
        public override string Title => Strings.T("app.home");
        public override string Color => "#fa8231";

        public override VisualElement Open()
        {
            Closed();
            var profile = Services.Get<PlayerProfile>();
            var config = Services.Get<ConfigStore>();
            var home = config.Registries.Homes.FirstOrDefault(h => h.id == profile.Me?.HomeId)?.name;
            var page = AppUi.Page(El.Text(Strings.T("home.title"), "ct-h1"),
                El.Text(home ?? Strings.T("home.fallback"), "ct-body ct-muted"));
            var view = new View { Page = page, Status = El.Text("", "ct-small"), Session = Services.Get<Session>(),
                Profile = profile, UserId = Services.Get<Session>().UserId };
            _view = view;
            view.SessionChanged = () => { if (!Owns(view)) CloseView(view); };
            view.Session.Changed += view.SessionChanged;
            page.RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, page)) CloseView(view); });
            if (_enterHome != null && _returnToCity != null)
            {
                view.Returning = _atOwnHome?.Invoke() == true;
                view.Visit = El.Btn(HomeStrings.T(view.Returning ? "returnCity" : "enterHome"), () => Visit(view), "ct-btn ct-btn-primary");
                view.Visit.name = view.Returning ? "home-return-city" : "home-enter-own";
                page.Add(El.Div("ct-card", view.Visit,
                    El.Text(HomeStrings.T(view.Returning || _canEnterHome?.Invoke() == true ? "explore" : "roomUnavailable"), "ct-small ct-muted")));
            }
            if (!HomeActions.TryRead(config.Config("needs"), out var actions, out var reason))
            {
                Log.Warn("home", "Needs configuration unavailable: " + reason);
                page.Add(El.Text(HomeStrings.T("unavailable"), "ct-error"));
                page.Add(view.Status); SetControls(); return page;
            }
            foreach (var definition in actions)
            {
                string id = definition.Id;
                var button = El.Btn(Label(id) + (definition.CostCents > 0 ? $"  ({Money.Cash(definition.CostCents)})" : ""),
                    () => Perform(view, id), "ct-row-btn");
                button.name = "home-action-" + id;
                view.Buttons.Add(button);
                page.Add(button);
            }
            page.Add(view.Status);
            SetControls();
            return page;
        }

        public override void Closed() => CloseView(_view);

        private void CloseView(View view)
        {
            if (view == null || !ReferenceEquals(_view, view)) return;
            _view = null; // invalidate before cancellation or detach callbacks can run
            view.Session.Changed -= view.SessionChanged;
            foreach (var button in view.Buttons) button.SetEnabled(false);
            view.Visit?.SetEnabled(false);
            view.Lifetime.Cancel();
            view.Lifetime.Dispose();
        }

        private bool Owns(View view) => view != null && ReferenceEquals(_view, view)
            && !view.Lifetime.IsCancellationRequested && view.Session.IsSignedIn
            && !string.IsNullOrEmpty(view.UserId) && view.Session.UserId == view.UserId
            && view.Profile.UserId == view.UserId;

        private void SetControls()
        {
            var view = _view;
            if (view == null) return;
            bool ready = Owns(view) && _request == null;
            foreach (var button in view.Buttons) button.SetEnabled(ready);
            view.Visit?.SetEnabled(ready && (view.Returning ? _atOwnHome?.Invoke() == true : _canEnterHome?.Invoke() == true));
        }

        private void Visit(View view)
        {
            if (!Owns(view) || _request != null) return;
            bool available = view.Returning ? _atOwnHome?.Invoke() == true : _canEnterHome?.Invoke() == true;
            if (!available) { SetControls(); return; }
            // The flow takes the shared travel gate and then closes the phone. Its owner is the
            // gameplay generation, not the view token which closing this page immediately cancels.
            (view.Returning ? _returnToCity : _enterHome)?.Invoke();
        }

        // UI callback: all request faults are handled here; the phone shell cannot catch async-void faults.
        private async void Perform(View view, string action)
        {
            if (!Owns(view) || _request != null) return;
            CancellationTokenSource request = null;
            try
            {
                request = CancellationTokenSource.CreateLinkedTokenSource(view.Lifetime.Token);
                _request = request; // gate is held even if the phone closes and opens during cancellation
                SetControls();
                view.Status.text = HomeStrings.T("working");
                request.CancelAfter(TimeSpan.FromSeconds(30));
                var response = await Services.Get<Api>().NeedsAction(action, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (!Owns(view)) return;
                if (response?.Needs == null) throw new InvalidOperationException("Needs action returned no needs receipt.");
                view.Profile.SetNeeds(response.Needs);
                if (!Owns(view)) return;
                view.Profile.SetCash(response.CashCents);
                if (!Owns(view)) return;
                view.Status.text = Strings.T("home.done");
                Bus.Publish(new SfxSignal("sfx_ui_success"));
            }
            catch (OperationCanceledException)
            {
                if (Owns(view)) view.Status.text = HomeStrings.T("unconfirmed");
            }
            catch (Exception e)
            {
                if (Owns(view))
                {
                    // A lost or malformed receipt does not establish whether the server applied the action.
                    if (!(e is ApiException api) || api.IsNetwork || api.Status >= 500 || api.Code == "DECODE_FAILED")
                    {
                        Log.Warn("home", "Needs action unconfirmed: " + e.GetType().Name);
                        view.Status.text = HomeStrings.T("unconfirmed");
                    }
                    else view.Status.text = Strings.Error(api.Code, api.ServerMessage);
                }
            }
            finally
            {
                if (ReferenceEquals(_request, request)) _request = null;
                request?.Dispose();
                SetControls();
            }
        }

        private static string Label(string a) => a switch
        {
            "eat" => Strings.T("home.eat"), "sleep" => Strings.T("home.sleep"),
            "shower" => Strings.T("home.shower"), "touch_grass" => Strings.T("home.touch_grass"),
            _ => Strings.ReadableId(a),
        };
    }
}
