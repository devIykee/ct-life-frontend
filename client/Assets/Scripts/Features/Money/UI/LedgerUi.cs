using System;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// The Cash ledger on the phone (GET /ledger, backend eeef49d): one row per entry (our title and icon for reason codes we
    /// know, the server's label for the rest, a signed amount in green or red), and the See all sheet that pages back 30 at a
    /// time until the server's `next` runs out. Shared by the Bank and the Wallet.
    /// </summary>
    public static class LedgerUi
    {
        public static VisualElement Row(LedgerItem item)
        {
            var key = LedgerKinds.TitleKey(item.Kind);
            string title = key != null ? MoneyStrings.T(key) : (string.IsNullOrWhiteSpace(item.Label) ? MoneyStrings.T("ledger.other") : item.Label);
            var tone = SignedMoney.ToneOf(item.AmountCents);
            var row = new DuoRow().Leading(Duo.IconSquare(LedgerKinds.Icon(item.Kind), tone == SignedMoney.Tone.Up ? "green" : tone == SignedMoney.Tone.Down ? "red" : "gray"))
                .Title(title);
            if (item.AtMs.HasValue) row.Subtitle(MoneyFormat.Ago(Math.Max(0, ServerClock.NowMs - item.AtMs.Value) / 1000.0));
            row.Value(SignedMoney.Text(item.AmountCents), tone == SignedMoney.Tone.Up ? "duo-value-positive" : tone == SignedMoney.Tone.Down ? "duo-value-negative" : null);
            row.name = "ledger-" + item.Id;
            return row;
        }

        /// <summary>See all: the whole history in a sheet over <paramref name="host"/>.</summary>
        public static DuoSheet OpenAll(MoneyHub hub, VisualElement host)
        {
            var list = El.Div("duo-list");
            list.name = "ledger-all";
            var footer = El.Div("duo-ledger-footer");
            var scroll = El.VerticalScroll("duo-sheet-scroll");
            scroll.Add(list); scroll.Add(footer);
            var sheet = new DuoSheet(MoneyStrings.T("ledger.title"));
            sheet.name = "ledger-sheet";
            sheet.AddContent(scroll);
            bool open = true, loading = false;
            sheet.Closed += () => open = false;
            string next = null; int count = 0;
            async void More()
            {
                if (loading || !open || hub == null || hub.Disposed) return;
                loading = true;
                footer.Clear();
                footer.Add(Duo.Footnote(MoneyStrings.T("common.loading")));
                LedgerPage page = null;
                try { page = await hub.Api.Ledger(next, 30, hub.Life); }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { Log.Exception("money", e); }
                loading = false;
                if (!open) return;
                footer.Clear();
                if (page == null) { footer.Add(Duo.Pill(MoneyStrings.T("common.retry"), More)); return; }
                foreach (var item in page.Rows.Items) { list.Add(Row(item)); count++; }
                Duo.MarkLast(list);
                next = page.Next;
                if (count == 0) footer.Add(Duo.Empty(MoneyStrings.T("ledger.empty")));
                else if (next != null) { var more = Duo.Pill(MoneyStrings.T("ledger.more"), More); more.name = "ledger-more"; footer.Add(more); }
                else footer.Add(Duo.Footnote(MoneyStrings.T("ledger.end")));
                if (page.Rows.Skipped > 0) footer.Add(Duo.Footnote(MoneyStrings.F("ledger.skipped", page.Rows.Skipped)));
            }
            More();
            sheet.Show(host);
            return sheet;
        }
    }

    /// <summary>The Wallet's Latest transactions (registered into <see cref="WalletApp.LatestTransactions"/> by the phone catalog).</summary>
    public sealed class MoneyLedgerSection : VisualElement
    {
        public const int Count = 5;
        private MoneyHub _hub;
        private readonly Subscriptions _subs = new Subscriptions();
        private DuoSheet _sheet;
        private int _gen;

        public static VisualElement Create(PhoneShell phone) => Services.TryGet<Session>(out _) ? new MoneyLedgerSection() : null;

        private MoneyLedgerSection()
        {
            name = "wallet-latest";
            AddToClassList("duo-latest");
            RegisterCallback<AttachToPanelEvent>(e =>
            {
                if (e.target != this || _hub != null) return;
                _hub = MoneyHub.Acquire();
                _subs.On<ProfileChangedSignal>(_ => Load());   // money moved (a repay, a top-up, a payday): newest rows
                Load();
            });
            RegisterCallback<DetachFromPanelEvent>(e =>
            {
                if (e.target != this) return;
                _subs.Dispose();
                _sheet?.Close();
                _hub?.Release();
                _hub = null;
            });
        }

        private async void Load()
        {
            var hub = _hub;
            if (hub == null || hub.Disposed) return;
            int gen = ++_gen;
            LedgerPage page = null; bool failed = false;
            try { page = await hub.Api.Ledger(null, Count, hub.Life); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Log.Exception("money", e); failed = true; }
            if (gen != _gen || _hub != hub) return;
            Clear();
            bool rows = page != null && page.Rows.Items.Count > 0;
            if (!rows && !failed) return;
            Add(Duo.SectionHeader(Strings.T("wallet.latest"), rows ? MoneyStrings.T("ledger.seeAll") : null, rows ? (Action)(() => _sheet = LedgerUi.OpenAll(_hub, this)) : null));
            var list = El.Div("duo-list");
            if (rows) foreach (var item in page.Rows.Items) list.Add(LedgerUi.Row(item));
            else list.Add(new DuoRow(Load).Title(MoneyStrings.T("ledger.unavailable")).Subtitle(MoneyStrings.T("common.retry")));
            Duo.MarkLast(list);
            Add(list);
        }
    }
}
