using System;
using System.Threading.Tasks;
using System.Linq;
using CTLife.Core;
using CTLife.Rest.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The birth lottery (feature #3), forced once per account: Insider Baby, Exit Liquidity Baby, or (Seeker
    /// Genesis Token holders) Seeker Baby. The server rolls (`POST /v1/lottery/roll`); the client only animates.
    /// The roll delegate returns null when the server shows the roll already happened (409 or reconciled /me).
    /// </summary>
    public sealed class LotteryScreen : UiScreen
    {
        private readonly Func<Task<PostLotteryRoll200Response>> _roll;
        private readonly Action<PostLotteryRoll200Response> _onDone;
        private readonly bool _isSeeker;
        private Label _title, _detail, _error;
        private VisualElement _cards;
        private Button _btn;
        private PostLotteryRoll200Response _result;
        private bool _rolling;

        public LotteryScreen(bool isSeeker, Func<Task<PostLotteryRoll200Response>> roll, Action<PostLotteryRoll200Response> onDone)
        {
            _isSeeker = isSeeker;
            _roll = roll;
            _onDone = onDone;
        }

        protected override VisualElement Build()
        {
            _title = El.Text(Strings.T("lottery.title"), "ct-h1");
            _detail = El.Text(Strings.T("lottery.body"), "ct-body ct-muted");
            _error = El.Text("", "ct-error");
            _cards = El.Div("ct-lottery-cards",
                Card("insider", Strings.T("lottery.insider"), Strings.T("lottery.insiderDetail")),
                Card("exit_liquidity", Strings.T("lottery.exit"), Strings.T("lottery.exitDetail")));
            if (_isSeeker) _cards.Add(Card("seeker", Strings.T("lottery.seeker"), Strings.T("lottery.seekerDetail")));
            _btn = El.Btn(Strings.T("lottery.roll"), OnRoll, "ct-btn ct-btn-primary ct-btn-big");
            return El.Div("ct-lottery", El.Text(Strings.T("lottery.eyebrow"), "ct-eyebrow"), _title, _detail, _cards,
                El.Text(Strings.T("lottery.note"), "ct-small ct-muted"), _error, _btn);
        }

        private static VisualElement Card(string id, string name, string sub)
        {
            var c = El.Div("ct-lottery-card", new UiIcon(id == "insider" ? "home" : id == "seeker" ? "phone" : "wallet"),
                El.Div("ct-grow", El.Text(name, "ct-h2"), El.Text(sub, "ct-small ct-muted")));
            c.name = "card_" + id;
            return c;
        }

        private async void OnRoll()
        {
            if (_rolling) return;
            if (_result != null)
            {
                _onDone?.Invoke(_result);
                return;
            }
            _btn.SetEnabled(false);
            _btn.text = Strings.T("lottery.rolling");
            _rolling = true;
            _error.text = "";
            var spin = Spin();
            try
            {
                _result = await _roll();
            }
            catch (Exception e)
            {
                _rolling = false;
                await spin;
                foreach (var card in _cards.Children()) card.EnableInClassList("ct-lottery-hot", false);
                _error.text = AppUi.Err(e);
                _btn.text = Strings.T("lottery.roll");
                _btn.SetEnabled(true);
                return;
            }
            await spin;
            _rolling = false;
            if (Element?.parent == null) return;
            if (_result == null)
            {
                // The roll had already happened (reconciled from /me). Nothing to reveal; continue onboarding.
                _onDone?.Invoke(null);
                return;
            }
            Reveal(_result);
            _btn.text = Strings.T("lottery.enter");
            _btn.SetEnabled(true);
        }

        private async Task Spin()
        {
            var cards = new System.Collections.Generic.List<VisualElement>(_cards.Children());
            float t0 = Time.unscaledTime;
            int i = 0;
            while (_rolling && (Time.unscaledTime - t0 < 2.2f || _result == null))
            {
                for (int k = 0; k < cards.Count; k++) cards[k].EnableInClassList("ct-lottery-hot", k == i % cards.Count);
                i++;
                Bus.Publish(new SfxSignal("sfx_ui_toggle", 0.5f));
                float elapsed = Time.unscaledTime - t0;
                await Awaitable.WaitForSecondsAsync(Mathf.Lerp(0.07f, 0.35f, Mathf.Clamp01(elapsed / 2.2f)));
                if (elapsed > 8f) break;
            }
        }

        private void Reveal(PostLotteryRoll200Response r)
        {
            string id = r.Outcome == PostLotteryRoll200Response.OutcomeEnum.Insider ? "insider" : r.Outcome == PostLotteryRoll200Response.OutcomeEnum.Seeker ? "seeker" : "exit_liquidity";
            foreach (var c in _cards.Children())
            {
                c.EnableInClassList("ct-lottery-hot", false);
                c.EnableInClassList("ct-lottery-win", c.name == "card_" + id);
                c.EnableInClassList("ct-lottery-lose", c.name != "card_" + id);
            }
            _title.text = Strings.T(id == "insider" ? "lottery.insider" : id == "seeker" ? "lottery.seeker" : "lottery.exit");
            var homes = Services.Get<ConfigStore>().Registries.Homes;
            var home = homes.FirstOrDefault(h => h.id == r.HomeId)?.name ?? Strings.ReadableId(r.HomeId);
            _detail.text = Strings.F("lottery.result", Money.Cash(r.CashCents), home) +
                (r.DebtCents > 0 ? "\n" + Strings.F("lottery.debt", Money.Cash(r.DebtCents)) : "");
            Bus.Publish(new SfxSignal(id == "exit_liquidity" ? "sfx_rug" : "sfx_moon_horn"));
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Development automation (`autocreate=1`): rolls, then continues after the reveal.</summary>
        public async void AutoRollForAutomation()
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(0.5f);
                OnRoll();
                while (_rolling) await Awaitable.WaitForSecondsAsync(0.25f);
                await Awaitable.WaitForSecondsAsync(1f);
                if (_result != null && Element?.parent != null) OnRoll();
            }
            catch (Exception e) { Log.Warn("lottery", "Automation roll failed: " + e.Message); }
        }
#endif

        public override void Close()
        {
            _rolling = false;
            base.Close();
        }
    }
}
