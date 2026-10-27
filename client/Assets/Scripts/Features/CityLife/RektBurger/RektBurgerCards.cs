using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI;
using CTLife.UI.DS;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>
    /// The Rekt Burger card on top of the Jobs app's Careers list (CareersApp.ExtraTopCard): unlocked, pay and today's room,
    /// Work a shift, or what opens it while locked; Hang up the apron while employed (the Glow-up post when net worth recovered).
    /// </summary>
    public static class RektBurgerCard
    {
        public static VisualElement Build()
        {
            var svc = RektBurgerService.Current;
            if (svc == null) return null;
            var card = El.Div("duo-feature");
            card.name = "rektburger-card";
            card.style.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
            var logo = El.Div();
            logo.style.height = 84; logo.style.width = 230; logo.style.marginBottom = 6;
            var tex = Resources.Load<Texture2D>("UI/RektBurger/logo");
            if (tex != null) { logo.style.backgroundImage = new StyleBackground(tex); logo.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit; }
            card.Add(logo);
            card.Add(El.Text(RbText.T("card.kicker"), "duo-feature-kicker"));
            var s = svc.State;
            if (s == null)
            {
                card.Add(El.Text(RbText.T("tagline"), "duo-feature-meta"));
                _ = svc.Refresh();
                return card;
            }
            if (!s.Unlocked)
            {
                card.Add(El.Text(RbText.T("card.locked"), "duo-feature-title"));
                card.Add(El.Text(RbText.F("card.lockedHint", RbText.Money(s.LiquidationCashBelow > 0 ? s.LiquidationCashBelow : 2000)), "duo-feature-meta"));
                return card;
            }
            card.Add(El.Text(RbText.T("name"), "duo-feature-title"));
            string line = s.CapReached
                ? RbText.F("card.cap", DateTimeOffset.FromUnixTimeMilliseconds(s.ResetsAtMs).ToLocalTime().ToString("HH:mm"))
                : RbText.F("card.unlocked", RbText.Money(s.RatePerMinuteCents * 60), RbText.Money(s.RemainingTodayCents));
            card.Add(El.Text(line, "duo-feature-meta"));
            if (!string.IsNullOrEmpty(s.Reason)) card.Add(El.Text(RbText.T("reason." + s.Reason), "duo-feature-meta"));
            if (s.Employed) card.Add(El.Text(RbText.F("card.employed", s.StintShifts), "duo-feature-meta"));
            card.Add(El.Text(RbText.T("card.where"), "duo-feature-meta"));
            var actions = El.Div("duo-feature-actions");
            bool onShift = svc.Shift != null && svc.Shift.Active;
            if (onShift || !s.CapReached)
            {
                var work = Duo.Pill(RbText.T(onShift ? "card.clockout" : "card.work"), () => { if (onShift) _ = svc.ClockOut(); else _ = svc.ClockIn(); CloseDuo?.Invoke(); }, Duo.PillKind.Primary);
                work.name = onShift ? "rektburger-clockout" : "rektburger-work";
                work.AddToClassList("duo-feature-pill");
                actions.Add(work);
            }
            if (s.Employed)
            {
                var leave = Duo.Pill(RbText.T("card.leave"), () => Leave(svc), Duo.PillKind.Plain);
                leave.name = "rektburger-leave";
                actions.Add(leave);
            }
            card.Add(actions);
            return card;
        }

        /// <summary>Closes the Duo before the full-screen shift opens (set by the app).</summary>
        public static Action CloseDuo;

        private static string _leaveKey;

        private static async void Leave(RektBurgerService svc)
        {
            _leaveKey ??= Guid.NewGuid().ToString();
            try
            {
                var (glow, _) = await svc.Api.Leave(_leaveKey, svc.Life);
                _leaveKey = null;
                Log.Info("rektburger", "left the job, glow-up " + glow);
                Bus.Publish(ToastSignal.Text(RbText.T(glow ? "leave.glow" : "leave.done"), glow ? ToastKind.Good : ToastKind.Info));
                _ = svc.Refresh();
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _leaveKey = null;
                Bus.Publish(ToastSignal.Text(err.Message, ToastKind.Bad));
            }
        }
    }

    /// <summary>
    /// `career_promoted` over the game: the new rank in a mint medallion, the title, the pay bonus, newly opened venue areas and
    /// wardrobe items; View career opens the Jobs app on its Career tab (set by the app through <see cref="OpenCareer"/>).
    /// </summary>
    public static class PromotionCard
    {
        public static Action OpenCareer;
        private static VisualElement _open;

        public static void Show(CareerPromotion p)
        {
            var ui = UiRoot.Instance;
            if (p == null || ui?.Modals == null) return;
            if (_open != null) ui.Modals.Close(_open);
            // design-system dialog: trophy hero, the new title, rank line, what it opens as chips, View career / Nice
            var chips = Ds.Chips(
                p.PayBonusBps > 0 ? Ds.Chip(RbText.F("promo.bonus", (p.PayBonusBps / 100.0).ToString("0.#"), p.LadderName ?? ""), Tone.Accent, "coins") : null,
                p.VenueAccess.Count > 0 ? Ds.Chip(RbText.F("promo.access", string.Join(", ", p.VenueAccess.ConvertAll(Area))), Tone.Neutral, "door-open") : null,
                p.CosmeticIds.Count > 0 ? Ds.Chip(RbText.T("promo.cosmetic"), Tone.Neutral, "t-shirt") : null);
            VisualElement dialog = null;
            dialog = Ds.Dialog(new DialogSpec
            {
                Name = "career-promotion",
                Hero = Ds.Hero("trophy", Tone.Accent),
                Title = p.Title,
                Subtitle = RbText.F("promo.rank", p.ToRank, p.LadderName ?? ""),
                Body = chips.childCount > 0 ? chips : null,
                Primary = new DsAction { Label = RbText.T("promo.view"), Name = "career-promotion-view", OnClick = () => { ui.Modals.Close(dialog); _open = null; OpenCareer?.Invoke(); } },
                Secondary = new DsAction { Label = RbText.T("promo.ok"), Kind = BtnKind.Text, Name = "career-promotion-ok", OnClick = () => { ui.Modals.Close(dialog); _open = null; } },
            });
            var burst = new CelebrationBurst(coins: 0, confetti: 90, puffs: 0);
            burst.pickingMode = PickingMode.Ignore;
            dialog.Add(burst);
            _open = dialog;
            ui.Modals.Show(dialog, DismissMode.Back, () => _open = null);
            dialog.schedule.Execute(() => burst.Confetti(90)).StartingIn(200);
            Cues.Play("sfx_rank_up");
            DeviceFeel.Confirm();
        }

        private static string Area(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return "";
            var parts = flag.Split(':');
            string v = parts[0].Replace('_', ' ');
            v = v.Length > 0 ? char.ToUpperInvariant(v[0]) + v.Substring(1) : v;
            return parts.Length > 1 ? v + " " + parts[1].Replace('_', ' ') : v;
        }
    }
}
