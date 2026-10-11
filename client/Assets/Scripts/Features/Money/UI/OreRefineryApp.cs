using System;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using FeatureFlags = CTLife.Core.Features;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// ORE Refinery client (feature #18): the live ORE board (GET /ore/board every 2 s, round timer), the player's ORE
    /// position and miner tier (GET /ore/me), and ore-stake deposit/withdraw/claim/compound through POST /ore/stake/tx and
    /// MWA. ORE is mainnet-only: every stake action moves real ORE, so it needs an explicit acknowledgement and a second,
    /// separate tap to open the wallet. No mining (Build Spec decision).
    /// </summary>
    public sealed class OreRefineryApp : MoneyApp
    {
        public override string Id => "ore";
        public override string Title => "ORE";
        public override string Color => "#fd9644";
        public override int Phase => 2;
        public override string Feature => FeatureFlags.OreReader;

        internal override MoneyView CreateView(MoneyHub hub) => new OreView2(hub);
    }

    internal sealed class OreView2 : MoneyView
    {
        private enum StakeStage { Idle, Building, Built, Signing, Sent }

        /// <summary>[Inference] A server-built transaction carries a recent blockhash that expires after roughly a minute; rebuild after that.</summary>
        private const double TxFreshSeconds = 60;

        private readonly VisualElement _board = El.Div("ct-col"), _me = El.Div("ct-col"), _stake = El.Div("ct-col");
        private readonly PollGate _boardGate = new PollGate(2, 30), _meGate = new PollGate(15, 60);
        private readonly JToken _oreConfig;
        private OreBoardRow _boardRow;
        private OreMeRow _meRow;
        private MoneyError _boardError, _meError, _stakeError;
        private Label _timer, _boardAge, _confirmLabel;
        private Toggle _ackToggle;
        private Button _buildButton;

        private string _action = "deposit", _amountInput = "", _tx, _signature;
        private bool _ack;
        private double _builtAt;
        private StakeStage _stage = StakeStage.Idle;
        private readonly RetainedWrite _buildWrite = new RetainedWrite();

        public OreView2(MoneyHub hub) : base(hub)
        {
            _oreConfig = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("ore") : null;
            Root.Add(MoneyUi.Text(MoneyStrings.T("ore.title"), "ct-h1"));
            Root.Add(MoneyUi.Notice(MoneyStrings.T("ore.mainnet"), "warn"));
            Root.Add(_board);
            Root.Add(_me);
            Root.Add(_stake);
            Root.Add(MoneyUi.Small(MoneyStrings.T("ore.noMining")));
            Subs.On<DegradedChangedSignal>(_ => { RenderBoard(); _boardGate.Kick(); });
            RenderBoard();
            RenderMe();
            RenderStake();
        }

        private static bool ReaderOn => FeatureFlags.IsAvailable(FeatureFlags.OreReader);
        private static bool WalletAvailable => Services.TryGet<IWalletSignIn>(out var w) && w.IsAvailable && Services.TryGet<ITransactionSigner>(out _);

        protected override void Tick()
        {
            double now = Now;
            if (ReaderOn && _boardGate.Due(now)) FetchBoard();
            if (_meGate.Due(now)) FetchMe();
            if (_timer != null && _boardRow != null)
            {
                var left = OreView.SecondsLeft(_boardRow.EndsAtMs, ServerClock.NowMs);
                _timer.text = !left.HasValue ? "—" : left.Value > 0 ? MoneyStrings.F("ore.endsIn", MoneyFormat.Countdown(left.Value)) : MoneyStrings.T("ore.ended");
            }
            if (_boardAge != null)
            {
                var age = _boardGate.Age(now);
                _boardAge.text = !double.IsNaN(age) && age > 10 ? MoneyStrings.F("common.stale", MoneyFormat.Ago(age)) : MoneyUi.Age(age);
            }
            if (_stage == StakeStage.Built && now - _builtAt > TxFreshSeconds)
            {
                // Stale transaction: drop it; the next "Prepare" builds a fresh one with a new key.
                _tx = null;
                _buildWrite.Complete();
                _stage = StakeStage.Idle;
                RenderStake();
            }
        }

        // ------------------------------------------------------------------ board

        private void FetchBoard()
        {
            _boardGate.Start();
            Run(ct => Hub.Api.OreBoard(ct), b =>
            {
                _boardGate.Succeeded(Now);
                _boardRow = b;
                _boardError = null;
                RenderBoard();
            }, err =>
            {
                _boardGate.Failed(Now);
                _boardError = err;
                RenderBoard();
            }, write: false);
        }

        private void RenderBoard()
        {
            _board.Clear();
            _timer = null;
            _boardAge = null;
            var card = MoneyUi.Card(MoneyUi.Text(MoneyStrings.T("ore.board"), "ct-h2"));
            _board.Add(card);
            if (!ReaderOn)
            {
                card.Add(MoneyUi.Notice(MoneyStrings.T("ore.readerOff"), "warn"));
                return;
            }
            _boardAge = MoneyUi.Small("");
            card.Add(_boardAge);
            if (_boardError != null) card.Add(MoneyUi.Notice(MoneyStrings.Err(_boardError), "error"));
            var b = _boardRow;
            if (b == null)
            {
                if (_boardError == null) card.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            _timer = MoneyUi.Text("", "ct-big-number");
            card.Add(MoneyUi.Row(MoneyUi.Text(MoneyStrings.F("ore.round", b.RoundId), "ct-money-title"), El.Div("ct-grow"), _timer));
            card.Add(MoneyUi.Text(MoneyStrings.F("ore.motherlode", b.MotherlodeOre != null ? OreView.Ore(b.MotherlodeOre) : "—")));
            card.Add(MoneyUi.Small(MoneyStrings.F("ore.cityStaked", b.CityStakedOre != null ? OreView.Ore(b.CityStakedOre) : "—")));
            if (!string.IsNullOrEmpty(b.PriceUsd)) card.Add(MoneyUi.Small(MoneyStrings.F("ore.price", b.PriceUsd)));
            card.Add(MoneyUi.Small(MoneyStrings.T("ore.squares")));
            if (b.Squares.Count != 25) card.Add(MoneyUi.Small(MoneyStrings.F("ore.shape", b.Squares.Count)));
            var grid = El.Div("ct-money-ore-grid");
            int busiest = OreView.Busiest(b.Squares);
            for (int i = 0; i < b.Squares.Count; i++)
            {
                var cell = El.Div("ct-money-ore-cell", MoneyUi.Text(OreView.Sol(b.Squares[i]), "ct-money-ore-sol"));
                cell.EnableInClassList("ct-money-ore-hot", i == busiest);
                grid.Add(cell);
            }
            card.Add(grid);
            Tick();
        }

        // ------------------------------------------------------------------ my ORE

        private void FetchMe()
        {
            _meGate.Start();
            Run(ct => Hub.Api.OreMe(ct), me =>
            {
                _meGate.Succeeded(Now);
                _meRow = me;
                _meError = null;
                RenderMe();
            }, err =>
            {
                _meGate.Failed(Now);
                _meError = err;
                RenderMe();
            }, write: false);
        }

        private void RenderMe()
        {
            _me.Clear();
            var card = MoneyUi.Card(MoneyUi.Text(MoneyStrings.T("ore.you"), "ct-h2"));
            _me.Add(card);
            if (_meError != null) card.Add(MoneyUi.Notice(MoneyStrings.Err(_meError), "error"));
            if (_meRow == null)
            {
                if (_meError == null) card.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            card.Add(MoneyUi.Pair(MoneyStrings.F("ore.balance", "").Trim(), OreView.Ore(_meRow.Balance)));
            card.Add(MoneyUi.Pair(MoneyStrings.F("ore.staked", "").Trim(), OreView.Ore(_meRow.Staked)));
            card.Add(MoneyUi.Pair(MoneyStrings.F("ore.stOre", "").Trim(), MoneyFormat.Token(_meRow.StOre, TokenUnits.OreDecimals, "stORE", 4)));
            card.Add(MoneyUi.Text(MoneyStrings.F("ore.tier", OreView.TierLabel(_oreConfig, _meRow.MinerTier))));
            if (OreView.HardHat(_oreConfig, _meRow.MinerTier)) card.Add(MoneyUi.Small(MoneyStrings.T("ore.hardhat")));
        }

        // ------------------------------------------------------------------ staking (mainnet)

        private void RenderStake()
        {
            _stake.Clear();
            var card = MoneyUi.Card();
            _stake.Add(card);
            bool locked = _stage != StakeStage.Idle && _stage != StakeStage.Sent;
            var actions = El.Div("ct-app-tabs ct-money-tabs");
            foreach (var a in new[] { "deposit", "withdraw", "claim", "compound" })
            {
                var action = a;
                var chip = MoneyUi.Chip(MoneyStrings.T("ore." + a), () => { _action = action; ResetStake(); }, _action == a);
                chip.SetEnabled(!locked);
                actions.Add(chip);
            }
            card.Add(actions);
            if (_action == "deposit" || _action == "withdraw")
            {
                var field = MoneyUi.Field(MoneyStrings.T("ore.amount"), _amountInput, v =>
                {
                    _amountInput = v;
                    if (_stage == StakeStage.Built)
                    {
                        ResetStake();
                        return;
                    }
                    // The acknowledgement is bound to the exact amount shown; editing the amount withdraws it.
                    _ack = false;
                    _ackToggle?.SetValueWithoutNotify(false);
                    _buildButton?.SetEnabled(false);
                    if (_confirmLabel != null) _confirmLabel.text = MoneyStrings.F("ore.confirmBody", ActionText());
                });
                field.SetEnabled(!locked || _stage == StakeStage.Built);
                card.Add(field);
            }
            if (!WalletAvailable) card.Add(MoneyUi.Notice(MoneyStrings.T("ore.walletOff"), "warn"));

            // Mainnet warning and the acknowledgement that gates "Prepare".
            card.Add(MoneyUi.Text(MoneyStrings.T("ore.confirmTitle"), "ct-section-title"));
            _confirmLabel = MoneyUi.Notice(MoneyStrings.F("ore.confirmBody", ActionText()), "warn");
            card.Add(_confirmLabel);
            var ack = MoneyUi.Ack(MoneyStrings.T("ore.confirmAck"), on => { _ack = on; RenderStake(); });
            ack.SetValueWithoutNotify(_ack);
            ack.SetEnabled(!locked);
            _ackToggle = ack;
            _buildButton = null;
            card.Add(ack);

            if (_stakeError != null) card.Add(MoneyUi.Notice(MoneyStrings.Err(_stakeError), "error"));
            switch (_stage)
            {
                case StakeStage.Idle:
                case StakeStage.Sent:
                {
                    var build = MoneyUi.Btn(MoneyStrings.T("ore.build"), Build, "ct-btn ct-btn-ghost");
                    build.SetEnabled(_ack && WalletAvailable);
                    _buildButton = build;
                    card.Add(build);
                    if (_stage == StakeStage.Sent && !string.IsNullOrEmpty(_signature))
                        card.Add(MoneyUi.Notice(MoneyStrings.F("ore.sent", MoneyFormat.Short(_signature, 6, 6))));
                    break;
                }
                case StakeStage.Building:
                    card.Add(MoneyUi.Small(MoneyStrings.T("ore.building")));
                    break;
                case StakeStage.Built:
                {
                    // The second, separate tap: only this one opens the wallet.
                    var sign = MoneyUi.Btn(MoneyStrings.T("ore.sign"), Sign, "ct-btn ct-btn-danger");
                    sign.SetEnabled(_ack && WalletAvailable);
                    card.Add(MoneyUi.Row(sign, MoneyUi.Btn(MoneyStrings.T("common.cancel"), ResetStake, "ct-btn ct-btn-ghost")));
                    break;
                }
                case StakeStage.Signing:
                    card.Add(MoneyUi.Small(MoneyStrings.T("ore.signing")));
                    break;
            }
        }

        private string ActionText()
        {
            switch (_action)
            {
                case "deposit":
                case "withdraw":
                    var amount = TokenUnits.TryParse(_amountInput, TokenUnits.OreDecimals, out var units) ? OreView.Ore(units) : "? ORE";
                    return MoneyStrings.F("ore.action." + _action, amount);
                default:
                    return MoneyStrings.T("ore.action." + _action);
            }
        }

        private void ResetStake()
        {
            if (_stage == StakeStage.Signing || _stage == StakeStage.Building) return;
            _tx = null;
            _buildWrite.Complete();
            _stakeError = null;
            _ack = false;
            _stage = StakeStage.Idle;
            RenderStake();
        }

        private void Build()
        {
            if (!_ack || (_stage != StakeStage.Idle && _stage != StakeStage.Sent)) return;
            var ticket = Tickets.Stake(_action, _amountInput, _meRow?.Balance, _meRow?.Staked);
            if (!ticket.Ok)
            {
                _stakeError = new MoneyError { Code = "VALIDATION_FAILED", Key = ticket.ErrorKey, Args = ticket.Args };
                RenderStake();
                return;
            }
            var body = ticket.Body;
            var key = _buildWrite.KeyFor("/ore/stake/tx", RetainedWrite.Body(body));
            _stage = StakeStage.Building;
            _stakeError = null;
            _signature = null;
            RenderStake();
            Run(ct => Hub.Api.StakeTx(body, key, ct), tx =>
            {
                _buildWrite.Complete();
                _tx = tx;
                _builtAt = Now;
                _stage = StakeStage.Built;
                RenderStake();
            }, err =>
            {
                if (!err.OutcomeUnknown) _buildWrite.Complete();
                _stakeError = err;
                _stage = StakeStage.Idle;
                RenderStake();
            }, write: true);
        }

        private void Sign()
        {
            if (_stage != StakeStage.Built || !_ack || string.IsNullOrEmpty(_tx) || !Services.TryGet<ITransactionSigner>(out var signer)) return;
            var tx = _tx;
            _tx = null; // One signature per built transaction.
            _stage = StakeStage.Signing;
            RenderStake();
            Run(ct => signer.SignAndSendBase64(tx, ct), sig =>
            {
                _signature = sig;
                _stage = StakeStage.Sent;
                _ack = false;
                Bus.Publish(new SfxSignal("sfx_ore_clink"));
                _meGate.Kick();
                RenderStake();
            }, err =>
            {
                _stakeError = err;
                // A non-declined wallet failure may still have broadcast: refresh the position before anything else.
                _stage = StakeStage.Idle;
                _ack = false;
                _meGate.Kick();
                RenderStake();
            }, write: true);
        }
    }
}
