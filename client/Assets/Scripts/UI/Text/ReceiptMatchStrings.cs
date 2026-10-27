using CTLife.Core;
using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    public static class ReceiptMatchStrings
    {
        private static readonly Dictionary<string,string> En = new Dictionary<string,string>
        {
            ["title"]="Receipt Match", ["practice"]="LOCAL PRACTICE · FICTIONAL CASH · NO PAYOUT",
            ["review"]="REVIEW · SCRIPTED INPUT · FICTIONAL CASH · NO PAYOUT", ["score"]="{0} / 100", ["timer"]="{0}s",
            ["board"]="File {0} / 5", ["allFiled"]="All five files submitted", ["statement"]="STATEMENT", ["receipts"]="RECEIPTS",
            ["instruction"]="Tap a receipt, then its statement row. Match amount, reference and date.",
            ["selected"]="Receipt {0} selected. Tap a row; existing pairs will swap.",
            ["ready"]="{0} / 4 paired · Tap a receipt to place it, or tap a paired row to undo.",
            ["filed"]="Filed · {0} / 4 pairs matched · +{1}", ["waiting"]="All filed. Review the last matches while the practice clock finishes.",
            ["amount"]="{0} Cash", ["date"]="Oct {0:00}", ["reference"]="Ref {0}", ["row"]="{0:00}", ["receipt"]="{0}",
            ["unpaired"]="Not paired", ["paired"]="Receipt {0}", ["matched"]="{0} · matched", ["correctReceipt"]="Use receipt {0}",
            ["loose"]="Unpaired", ["receiptRow"]="Row {0}", ["held"]="Selected", ["receiptTip"]="Receipt {0}: {1}, {2}, {3}, {4}",
            ["rowTip"]="Statement row {0}: {1}, {2}, {3}. {4}", ["clear"]="Clear pairs", ["file"]="File matches", ["next"]="Next file",
            ["pause"]="Pause", ["resume"]="Resume", ["end"]="End practice", ["paused"]="Paused", ["pauseDetail"]="Your practice clock is on hold. Return when you’re ready.",
            ["again"]="Play again", ["back"]="Back to city", ["complete"]="Practice complete",
            ["summary"]="Matched: {0} / 20 · Files submitted: {1}\nWrong pairs: {2} · Unfiled or unpaired rows: {3}",
            ["board.0.title"]="Coffee is infrastructure", ["board.0.joke"]="The community ran on coffee. Someone still needs the receipts.",
            ["board.0.item.0"]="Opening coffee", ["board.0.item.1"]="Refill coffee", ["board.0.item.2"]="Spare cables", ["board.0.item.3"]="More spare cables",
            ["board.1.title"]="The quick community call", ["board.1.joke"]="One quick call. Two days of studio receipts.",
            ["board.1.item.0"]="Studio booking", ["board.1.item.1"]="Studio overtime", ["board.1.item.2"]="Mic rental", ["board.1.item.3"]="Mic extension",
            ["board.2.title"]="It worked in the demo", ["board.2.joke"]="Same vendor reference. Different days. Very different boxes.",
            ["board.2.item.0"]="Projector rental", ["board.2.item.1"]="Projector extra day", ["board.2.item.2"]="Backup display", ["board.2.item.3"]="Display extra day",
            ["board.3.title"]="Badges before product", ["board.3.joke"]="The stickers shipped on time. The feature is still in review.",
            ["board.3.item.0"]="Badge blanks", ["board.3.item.1"]="Badge clips", ["board.3.item.2"]="Sticker sheets", ["board.3.item.3"]="Sticker reprint",
            ["board.4.title"]="Launch-week closeout", ["board.4.joke"]="Nobody is expensing conviction. Check the actual numbers.",
            ["board.4.item.0"]="Backdrop hire", ["board.4.item.1"]="Camera deposit", ["board.4.item.2"]="Backdrop delivery", ["board.4.item.3"]="Camera hire"
        };
        public static string T(string key)=>En.TryGetValue(key,out var value)? NameTable.Apply(value) :key;
        public static string F(string key,params object[] args)=>string.Format(CultureInfo.CurrentCulture,T(key),args);
        public static string Board(ReceiptMatchBoard board,string key)=>T("board."+board.Story+"."+key);
        public static string Letter(int receipt)=>((char)('A'+receipt)).ToString();
        public static string Summary(ReceiptMatchResult result)=>F("summary",result.CorrectPairs,result.FiledBoards,result.WrongPairs,result.UnpairedRows);
    }
}
