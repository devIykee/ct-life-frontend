using System;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The birth lottery (feature #3), forced once per account: Insider Baby, Exit Liquidity Baby, or (Seeker
    /// Genesis Token holders) Seeker Baby. The server rolls (`POST /v1/lottery/roll`); the client only animates.
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

        public LotteryScreen(bool isSeeker, Func<Task<PostLotteryRoll200Response>> roll, Action<PostLotteryRoll200Response> onDone)
        {
            _isSeeker = isSeeker;
            _roll = roll;
            _onDone = onDone;
        }

        protected override VisualElement Build()
        {
            _title = El.Text("Birth lottery", "ct-h1");
            _detail = El.Text("Where you start is luck. Where you end up is on you.", "ct-body");
            _error = El.Text("", "ct-error");
            _cards = El.Div("ct-lottery-cards",
                Card("insider", "Insider Baby", "$3,000 + a Studio"),
                Card("exit_liquidity", "Exit Liquidity Baby", "$300 and $720 of debt"));
            if (_isSeeker) _cards.Add(Card("seeker", "Seeker Baby", "$1,500 + Hacker House + badge"));
            _btn = El.Btn("Roll", OnRoll, "ct-btn ct-btn-primary ct-btn-big");
            return El.Div("ct-lottery", _title, _detail, _cards, _error, _btn);
        }

        private static VisualElement Card(string id, string name, string sub)
        {
            var c = El.Div("ct-lottery-card", El.Text(name, "ct-h2"), El.Text(sub, "ct-small"));
            c.name = "card_" + id;
            return c;
        }

        private async void OnRoll()
        {
            if (_result != null)
            {
                _onDone?.Invoke(_result);
                return;
            }
            _btn.SetEnabled(false);
            _error.text = "";
            var spin = Spin();
            try
            {
                _result = await _roll();
            }
            catch (Exception e)
            {
                _error.text = e is CTLife.Net.ApiException api ? Strings.Error(api.Code, api.ServerMessage) : e.Message;
                _btn.SetEnabled(true);
                return;
            }
            await spin;
            Reveal(_result);
            _btn.text = "Enter CT City";
            _btn.SetEnabled(true);
        }

        private async Task Spin()
        {
            var cards = new System.Collections.Generic.List<VisualElement>(_cards.Children());
            float t0 = Time.unscaledTime;
            int i = 0;
            while (Time.unscaledTime - t0 < 2.2f || _result == null)
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
            _title.text = id == "insider" ? "INSIDER BABY" : id == "seeker" ? "SEEKER BABY" : "EXIT LIQUIDITY BABY";
            _detail.text = $"You start with {Money.Cash(r.CashCents)}" + (r.DebtCents > 0 ? $" and owe {Money.Cash(r.DebtCents)}" : "") + $". Home: {r.HomeId}.";
            Bus.Publish(new SfxSignal(id == "exit_liquidity" ? "sfx_rug" : "sfx_moon_horn"));
        }
    }
}
