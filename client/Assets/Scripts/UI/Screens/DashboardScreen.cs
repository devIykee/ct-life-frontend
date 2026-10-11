using System;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>The signed-in game lobby. Values come from the profile/config; navigation uses existing apps.</summary>
    public sealed class DashboardScreen : UiScreen
    {
        private readonly Action _enterCity;
        private readonly Action<string> _openApp;
        private Label _alias, _tribe, _cash, _energy, _dream, _home;
        private Button _enter, _cashButton, _energyButton;
        private Subscriptions _subs;
        private ConfigStore _config;
        private bool _busy;

        public DashboardScreen(Action enterCity, Action<string> openApp)
        {
            _enterCity = enterCity;
            _openApp = openApp;
        }

        protected override VisualElement Build()
        {
            _alias = DataLabel("ct-lobby-alias");
            _tribe = DataLabel("ct-lobby-tribe");
            _cash = DataLabel("ct-lobby-cash");
            _energy = DataLabel("ct-lobby-energy");
            _dream = DataLabel("ct-lobby-story-value");
            _home = DataLabel("ct-lobby-story-value");

            var player = El.Btn("", () => OpenApp("profile"), "ct-lobby-player", sfx: null);
            player.Add(El.Div("ct-lobby-avatar", new UiIcon("profile")));
            player.Add(El.Div("ct-grow", _alias, _tribe));
            var settings = El.IconButton("settings", Strings.T("app.settings"), () => OpenApp("settings"), "ct-btn ct-btn-icon ct-lobby-settings", sfx: null);
            var header = El.Div("ct-lobby-header", player, settings);

            _cashButton = ResourceButton("cash", "lobby.cash", _cash, () => OpenApp("bank"), "ct-lobby-cash-resource");
            _energyButton = ResourceButton("energy", "lobby.energy", _energy, () => OpenApp("home"), "ct-lobby-energy-resource");
            var resources = El.Div("ct-lobby-resources", _cashButton, _energyButton);

            var cityImage = new Image { image = Resources.Load<Texture2D>("UI/Art/CityDiorama"), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            cityImage.AddToClassList("ct-lobby-island");
            var stage = El.Div("ct-lobby-stage", El.Text(Strings.T("lobby.eyebrow"), "ct-lobby-eyebrow"),
                El.Text(Strings.T("lobby.title"), "ct-lobby-title"), cityImage);

            var dreamCard = El.Btn("", () => OpenApp("profile"), "ct-lobby-story ct-lobby-dream", sfx: null);
            dreamCard.Add(new UiIcon("dream"));
            dreamCard.Add(El.Div("ct-grow", El.Text(Strings.T("lobby.dream"), "ct-lobby-story-label"), _dream));
            var homeCard = El.Btn("", () => OpenApp("home"), "ct-lobby-story ct-lobby-home", sfx: null);
            homeCard.Add(new UiIcon("home"));
            homeCard.Add(El.Div("ct-grow", El.Text(Strings.T("lobby.home"), "ct-lobby-story-label"), _home));
            var story = El.Div("ct-lobby-stories", dreamCard, homeCard);

            _enter = El.Btn(Strings.T("lobby.enter"), () => { if (!_busy) _enterCity?.Invoke(); }, "ct-btn ct-btn-primary ct-lobby-enter");
            var navigation = El.Div("ct-lobby-navigation",
                Nav("map", "lobby.map", "map", "ct-tone-cyan"),
                Nav("jobs", "lobby.jobs", "jobs", "ct-tone-coral"),
                Nav("bank", "lobby.bank", "bank", "ct-tone-gold"),
                Nav("profile", "lobby.profile", "profile", "ct-tone-violet"),
                Nav("phone", "lobby.phone", null, "ct-tone-green"));
            var top = El.Div("ct-lobby-top", header, resources);
            var aside = El.Div("ct-lobby-aside", story, _enter, navigation);
            var content = El.Div("ct-lobby-content", top, El.Div("ct-lobby-main", stage, aside));
            return El.Div("ct-lobby", Pattern(), content);
        }

        private static Label DataLabel(string classes)
        {
            var label = El.Text("", classes);
            label.enableRichText = false;
            return label;
        }

        private Button ResourceButton(string icon, string label, Label value, Action action, string classes)
        {
            var b = El.Btn("", action, "ct-lobby-resource " + classes, sfx: null);
            b.Add(El.Div("ct-lobby-resource-medallion", new UiIcon(icon)));
            b.Add(El.Div("ct-grow", El.Text(Strings.T(label), "ct-lobby-resource-label"), value));
            return b;
        }

        private Button Nav(string icon, string label, string app, string tone)
        {
            var b = El.Btn("", () => OpenApp(app), "ct-lobby-nav " + tone, sfx: null);
            b.tooltip = Strings.T(label);
            b.Add(new UiIcon(icon));
            b.Add(El.Text(Strings.T(label), "ct-lobby-nav-label"));
            return b;
        }

        private static VisualElement Pattern()
        {
            var pattern = El.Div("ct-game-pattern");
            for (int row = 0; row < 8; row++)
                for (int col = 0; col < 6; col++)
                {
                    var tile = El.Div("ct-game-pattern-tile");
                    tile.style.left = Length.Percent(col * 20f - (row % 2 == 0 ? 7f : 17f));
                    tile.style.top = Length.Percent(row * 15f - 4f);
                    pattern.Add(tile);
                }
            return El.PassThrough(pattern);
        }

        private void OpenApp(string app) => _openApp?.Invoke(app);

        protected override void OnShown()
        {
            if (_subs == null)
            {
                _subs = new Subscriptions();
                _subs.On<ProfileChangedSignal>(_ => Refresh());
            }
            if (_config == null && Services.TryGet<ConfigStore>(out var config))
            {
                _config = config;
                _config.Changed += Refresh;
            }
            Refresh();
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            Refresh();
        }

        private void Refresh()
        {
            if (_alias == null) return;
            Services.TryGet<PlayerProfile>(out var profile);
            var me = profile?.Me;
            var reg = _config?.Registries;
            _alias.text = profile?.Alias ?? Strings.T("phone.greetingDefault");
            _tribe.text = reg?.Tribe(me?.Character?.TribeId)?.name ?? Strings.T("lobby.noTribe");
            _cash.text = me != null ? Money.Cash(profile.CashCents) : "—";
            _energy.text = me?.Needs != null ? Mathf.Clamp(me.Needs.Energy, 0, 100) + "/100" : "—";
            _dream.text = reg?.Dreams.FirstOrDefault(d => d.id == me?.Character?.DreamId)?.name ?? Strings.T("lobby.noDream");
            _home.text = reg?.Homes.FirstOrDefault(h => h.id == me?.HomeId)?.name ?? Strings.T("lobby.noHome");
            _enter.text = Strings.T(_busy ? "lobby.enterBusy" : "lobby.enter");
            _enter.SetEnabled(!_busy && profile?.HasCharacter == true && _enterCity != null);
            _cashButton.SetEnabled(me != null && _openApp != null);
            _energyButton.SetEnabled(me?.Needs != null && _openApp != null);
        }

        public override void Close()
        {
            _subs?.Dispose();
            _subs = null;
            if (_config != null) _config.Changed -= Refresh;
            _config = null;
            base.Close();
        }
    }
}
