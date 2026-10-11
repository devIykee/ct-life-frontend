using System;
using CTLife.Core;
using CTLife.Net;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Settings, iOS Settings layout (board #8): profile header row, Display &amp; Text Size slider, grouped rows with
    /// coloured icon squares (Notifications, Sounds &amp; Haptics, Do Not Disturb, Controls, Wallet, Credits &amp; Licences),
    /// feature sections and Sign out. Every earlier setting keeps its behaviour; detail screens push inside the app.
    /// </summary>
    public sealed class SettingsApp : PhoneApp
    {
        private readonly Action _signOut;
        private VisualElement _root;
        private DuoPage _main;
        private Label _bannersValue;
        private DuoToggle _dnd;

        public SettingsApp(Action signOut) { _signOut = signOut; }

        /// <summary>An extra settings section from a feature lane (Phase 6 verified X link); null = none.</summary>
        public static Func<VisualElement> ExtraSection;
        public override string Id => "settings";
        public override string Title => Strings.T("app.settings");
        public override string Color => "#4b6584";
        public override DuoChrome Chrome => DuoChrome.Grouped;

        private static DuoNotificationSettings Notify => DuoNotificationCenter.Queue.Settings;

        public override VisualElement Open()
        {
            _root = El.Div("duo-stack");
            _root.name = "settings-root";
            _main = BuildMain();
            Show(_main);
            return _root;
        }

        public override void Closed()
        {
            _root = null;
            _main = null;
        }

        private void Show(DuoPage page)
        {
            if (_root == null) return;
            _root.Clear();
            _root.Add(page);
            RefreshMain();
        }

        private DuoPage Sub(string title)
        {
            var page = new DuoPage(title, largeTitle: true, grouped: true);
            page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Show(_main), "settings-sub-back");
            return page;
        }

        private void RefreshMain()
        {
            if (_bannersValue != null) _bannersValue.text = Strings.T(Notify.Banners ? "settings.banners" : "settings.off");
            _dnd?.SetValueWithoutNotify(Notify.DoNotDisturb);
        }

        // ------------------------------------------------------------------ main

        private DuoPage BuildMain()
        {
            var page = new DuoPage(Title, largeTitle: true, grouped: true);
            page.name = "settings-main";
            page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Phone?.Back(), "settings-back");
            var body = page.Body;

            body.Add(Duo.Group(ProfileRow()));

            body.Add(Duo.GroupHeader(Strings.T("duo.textSize.title")));
            var footer = Duo.GroupFooter("");
            void Footer(DuoTextSize s) => footer.text = Strings.F("duo.textSize.footer", Strings.T("duo.textSize." + s.ToString().ToLowerInvariant()));
            var slider = new DuoSlider(DuoText.Count, (int)DuoText.Current, Strings.T("duo.textSize.small"), Strings.T("duo.textSize.large"),
                Strings.T("duo.textSize.decrease"), Strings.T("duo.textSize.increase"));
            slider.name = "settings-text-size";
            slider.Changed += v => { DuoText.Set((DuoTextSize)v); Footer((DuoTextSize)v); };
            Footer(DuoText.Current);
            var sliderGroup = El.Div("duo-group duo-group-padded", slider);
            body.Add(sliderGroup);
            body.Add(footer);

            _bannersValue = null;
            var notifications = new DuoRow(() => Show(BuildNotifications())).Leading(Duo.IconSquare("duo-bell", "red"))
                .Title(Strings.T("settings.notifications"));
            notifications.name = "settings-notifications";
            notifications.Value("");
            _bannersValue = notifications.Q<Label>(className: "duo-row-value");
            notifications.Chevron();
            var sounds = new DuoRow(() => Show(BuildSounds())).Leading(Duo.IconSquare("duo-speaker-high", "pink"))
                .Title(Strings.T("settings.sounds")).Chevron();
            sounds.name = "settings-sounds";
            _dnd = new DuoToggle(Notify.DoNotDisturb, v => Notify.DoNotDisturb = v) { name = "settings-dnd-toggle", tooltip = Strings.T("settings.dnd") };
            var dnd = _dnd.InRow(Strings.T("settings.dnd"), Duo.IconSquare("duo-moon", "indigo"));
            dnd.name = "settings-dnd";
            body.Add(Duo.Group(notifications, sounds, dnd));

            body.Add(Duo.GroupHeader(Strings.T("controls.title")));
            body.Add(Controls());

            var wallet = new DuoRow(() => Phone?.Open("wallet")).Leading(Duo.IconSquare("wallet", "graphite"))
                .Title(Strings.T("settings.wallet")).Chevron();
            wallet.name = "settings-wallet";
            var credits = new DuoRow(() => Show(BuildCredits())).Leading(Duo.IconSquare("duo-info", "gray"))
                .Title(Strings.T("settings.credits")).Chevron();
            credits.name = "settings-credits";
            body.Add(Duo.Group(wallet, credits));

            var extra = ExtraSection?.Invoke();
            if (extra != null) body.Add(El.Div("duo-group duo-group-padded duo-group-legacy", extra));

            var signOut = new DuoRow(() => { Phone?.Close(); _signOut?.Invoke(); }).Title(Strings.T("settings.signout"), "duo-destructive duo-row-title-centered");
            signOut.name = "settings-signout";
            signOut.AddToClassList("duo-row-centered");
            body.Add(Duo.Group(signOut));
            body.Add(Duo.Footnote(Strings.F("settings.version", Semver.ClientVersion), "duo-settings-version"));
            return page;
        }

        private DuoRow ProfileRow()
        {
            string alias = "", line = "";
            if (Services.TryGet<PlayerProfile>(out var p))
            {
                alias = p.Alias ?? "";
                var me = p.Me;
                string tribe = null;
                if (Services.TryGet<ConfigStore>(out var cfg)) tribe = cfg.Registries?.Tribe(me?.Character?.TribeId)?.name;
                string wallet = Short(me?.User?.Wallet);
                line = string.IsNullOrEmpty(tribe) ? wallet ?? "" : string.IsNullOrEmpty(wallet) ? tribe : Strings.F("settings.profileLine", tribe, wallet);
            }
            var row = new DuoRow(() => Phone?.Open("profile")).Leading(Duo.Avatar(alias, alias, "duo-avatar-l"))
                .Title(alias, "duo-row-title-large").Subtitle(line).Chevron();
            row.name = "settings-profile";
            row.AddToClassList("duo-row-profile");
            return row;
        }

        private VisualElement Controls()
        {
            var help = Duo.GroupFooter("");
            var segmented = new DuoSegmented(new[] { Strings.T("controls.analog"), Strings.T("controls.arrows") },
                ControlSettings.MovementMode == MovementControlMode.Arrows ? 1 : 0);
            segmented.name = "settings-controls";
            void Apply(int index)
            {
                var mode = index == 1 ? MovementControlMode.Arrows : MovementControlMode.Analog;
                ControlSettings.MovementMode = mode;
                help.text = Strings.T(mode == MovementControlMode.Analog ? "controls.analogHelp" : "controls.arrowsHelp");
            }
            segmented.Changed += Apply;
            help.text = Strings.T(ControlSettings.MovementMode == MovementControlMode.Analog ? "controls.analogHelp" : "controls.arrowsHelp");
            return El.Div("duo-group-block", El.Div("duo-group duo-group-padded", segmented), help);
        }

        // ------------------------------------------------------------------ detail screens

        private DuoPage BuildNotifications()
        {
            var page = Sub(Strings.T("settings.notifications"));
            page.name = "settings-notifications-page";
            var appRows = new System.Collections.Generic.List<DuoRow>();
            void SyncApps() { foreach (var r in appRows) r.SetEnabled(Notify.Banners); }
            var banners = new DuoToggle(Notify.Banners, v => { Notify.Banners = v; SyncApps(); }) { name = "settings-banners-toggle", tooltip = Strings.T("settings.allowBanners") };
            page.Body.Add(Duo.Group(banners.InRow(Strings.T("settings.allowBanners"))));
            page.Body.Add(Duo.GroupFooter(Strings.T("settings.bannersFooter")));
            // Only apps that actually post banners are listed (Messages today).
            page.Body.Add(Duo.GroupHeader(Strings.T("settings.bannersFrom")));
            foreach (var appId in new[] { "messages" })
            {
                var id = appId;
                var toggle = new DuoToggle(Notify.AppBanners(id), v => Notify.SetAppBanners(id, v)) { name = "settings-banners-" + id };
                var icon = El.Div("duo-row-app-icon", new PhoneAppIcon(id));
                var row = toggle.InRow(AppTitle(id), El.PassThrough(icon));
                appRows.Add(row);
            }
            page.Body.Add(Duo.Group(appRows.ToArray()));
            SyncApps();
            var dnd = new DuoToggle(Notify.DoNotDisturb, v => Notify.DoNotDisturb = v) { name = "settings-dnd-toggle-detail" };
            page.Body.Add(Duo.Group(dnd.InRow(Strings.T("settings.dnd"), Duo.IconSquare("duo-moon", "indigo"))));
            page.Body.Add(Duo.GroupFooter(Strings.T("settings.dndFooter")));
            return page;
        }

        private string AppTitle(string id) => Phone?.AppTitle(id) ?? id;

        private DuoPage BuildSounds()
        {
            var page = Sub(Strings.T("settings.sounds"));
            page.name = "settings-sounds-page";
            var am = CTLife.Audio.AudioManager.Instance;
            if (am == null)
            {
                page.Body.Add(Duo.Empty(Strings.T("settings.audio")));
                return page;
            }
            page.Body.Add(Duo.Group(
                Volume(Strings.T("settings.master"), am.masterVolume, v => am.masterVolume = v),
                Volume(Strings.T("settings.music"), am.musicVolume, v => am.musicVolume = v),
                Volume(Strings.T("settings.sound"), am.sfxVolume, v => am.sfxVolume = v),
                Volume(Strings.T("settings.voice"), am.voiceVolume, v => am.voiceVolume = v),
                Volume(Strings.T("settings.ambience"), am.ambienceVolume, v => am.ambienceVolume = v)));
            page.Body.Add(Duo.GroupFooter(Strings.T("settings.note")));
            return page;
        }

        private DuoPage BuildCredits()
        {
            var page = Sub(Strings.T("settings.credits"));
            page.name = "settings-credits-page";
            var credits = AssetCredits.Build(expanded: true);
            credits.AddToClassList("duo-credits");
            page.Body.Add(El.Div("duo-group duo-group-padded", credits));
            return page;
        }

        /// <summary>
        /// Volume as a −/+ stepper (10% steps). Device QA: a Slider inside the scrolling settings page took vertical
        /// swipes that started near it as value drags (Sound effects jumped 100% → 50%). Buttons only react to taps.
        /// </summary>
        private static DuoRow Volume(string label, float value, Action<float> set)
        {
            float current = UnityEngine.Mathf.Clamp01(value);
            var readout = El.Text("", "duo-stepper-value");
            void Apply(float next)
            {
                current = VolumeStep.Clamp(next);
                readout.text = UnityEngine.Mathf.RoundToInt(current * 100f) + "%";
                set(current);
            }
            var minus = Duo.Pill("−", () => Apply(VolumeStep.Down(current)));
            var plus = Duo.Pill("+", () => Apply(VolumeStep.Up(current)));
            minus.AddToClassList("duo-stepper-button");
            plus.AddToClassList("duo-stepper-button");
            minus.tooltip = Strings.F("settings.decrease", label);
            plus.tooltip = Strings.F("settings.increase", label);
            readout.text = UnityEngine.Mathf.RoundToInt(current * 100f) + "%";
            var row = new DuoRow().Title(label).Trailing(El.Div("duo-stepper", minus, readout, plus));
            row.name = "volume-" + label;
            return row;
        }

        private static string Short(string w) => string.IsNullOrEmpty(w) || w.Length < 10 ? w : w.Substring(0, 4) + "…" + w.Substring(w.Length - 4);
    }
}
