using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Wallet (dock), laid out like a phone crypto wallet (<see cref="DuoTokenWallet"/>): balance, actions, tokens, in-game money. Observed token holdings come from
    /// the shared <see cref="WalletBalanceService"/> (it owns requests and wallet identity); Cash and debt from the
    /// profile. Top up opens the Bank's top-up flow.
    /// </summary>
    public sealed class WalletApp : PhoneApp
    {
        private DuoTokenWallet _view;

        /// <summary>Set by the Money feature (App/PhoneCatalog): the Latest transactions section with See all.</summary>
        public static System.Func<PhoneShell, VisualElement> LatestTransactions;

        public override string Id => "wallet";
        public override string Title => WalletStrings.T("title");
        public override DuoChrome Chrome => DuoChrome.Plain;

        public override VisualElement Open()
        {
            Closed();
            var page = new DuoPage(Title);
            page.name = "wallet-page";
            page.AddToClassList("duo-wallet");
            page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Phone?.Back(), "wallet-back");
            bool canTopUp = Phone == null || Phone.CurrentPhase >= 2;
            if (canTopUp) page.Nav.AddRight(DuoGlyph.Kind.Plus, Strings.T("wallet.card.topUp"), () => Phone?.Open("topup"), "wallet-plus");
            _view = new DuoTokenWallet(canTopUp ? () => Phone?.Open("topup") : (System.Action)null, () => Phone?.Open("repay"),
                Phone != null && Phone.CurrentPhase >= 2 ? () => Phone?.Open("pulse") : (System.Action)null);
            page.Body.Add(_view);
            // Latest transactions (GET /ledger) come from the Money feature, which registers the section here.
            var latest = LatestTransactions?.Invoke(Phone);
            if (latest != null) page.Body.Add(latest);
            page.Body.Add(Duo.Footnote(WalletStrings.T("cashNote")));
            return page;
        }

        public override void Closed()
        {
            _view?.Dispose();
            _view = null;
        }
    }
}
