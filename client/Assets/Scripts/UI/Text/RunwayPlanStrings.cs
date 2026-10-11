using System.Collections.Generic;
using System.Globalization;
namespace CTLife.UI
{
    public static class RunwayPlanStrings
    {
        private static readonly Dictionary<string,string> En = new Dictionary<string,string>
        {
            ["title"]="Runway Plan", ["score"]="{0} / 100", ["timer"]="{0}s", ["board"]="PLAN {0} / 4",
            ["instruction"]="Make up to three changes. Meet the budget and both service minimums, then File.",
            ["cash"]="{0} / {1} Cash", ["cashLabel"]="MONTHLY BUDGET", ["capacity"]="{0} / {1} minimum", ["edits"]="{0} / 3 changes",
            ["full"]="Full", ["lean"]="Lean", ["minimum"]="Basic", ["preset"]="{0} · {1} Cash", ["presetCost"]="{0} Cash", ["service"]="{0} {1}", ["optional"]="No service capacity",
            ["undo"]="Undo change", ["file"]="File plan", ["next"]="Next plan", ["ready"]="Budget and services met. Ready to file.",
            ["budgetFail"]="Reduce spending by {0} Cash.", ["serviceFail"]="Restore {0} to at least {1}.", ["filed"]="Filed · +25 points", ["rejected"]="Filed · 0 points",
            ["pause"]="Pause", ["resume"]="Resume", ["end"]="End practice", ["paused"]="Plan on hold", ["pauseDetail"]="Your practice clock is paused. Resume when you’re ready.",
            ["practice"]="LOCAL PRACTICE · FICTIONAL CASH · NO PAYOUT", ["review"]="REVIEW · SCRIPTED INPUT · FICTIONAL CASH · NO PAYOUT",
            ["again"]="Play again", ["back"]="Back to city", ["complete"]="Practice complete", ["summary"]="Valid plans: {0} / 4 · Filed: {1}\nFictional budgets only. No Game Cash or XP awarded.",
            ["board.0.title"]="Ship night", ["board.0.a"]="build slots", ["board.0.b"]="help slots",
            ["board.0.line.0"]="Build machines", ["board.0.line.1"]="Support desk", ["board.0.line.2"]="Studio room", ["board.0.line.3"]="Launch swag", ["board.0.line.4"]="Meeting tools",
            ["board.0.joke"]="The feature still ships. The embossed lanyards can wait.",
            ["board.1.title"]="Demo morning", ["board.1.a"]="rehearsals", ["board.1.b"]="test slots",
            ["board.1.line.0"]="Stream studio", ["board.1.line.1"]="QA desk", ["board.1.line.2"]="Booth display", ["board.1.line.3"]="Sticker run", ["board.1.line.4"]="Cable extras",
            ["board.1.joke"]="Enough rehearsal to avoid calling every bug a live experiment.",
            ["board.2.title"]="The community call", ["board.2.a"]="stage seats", ["board.2.b"]="mod slots",
            ["board.2.line.0"]="Stage room", ["board.2.line.1"]="Call helpers", ["board.2.line.2"]="Mood lights", ["board.2.line.3"]="Guest merch", ["board.2.line.4"]="Afterparty",
            ["board.2.joke"]="Everyone gets a turn. Nobody needs a laser entrance.",
            ["board.3.title"]="Launch-week closeout", ["board.3.a"]="deploy slots", ["board.3.b"]="reply slots",
            ["board.3.line.0"]="Deploy runners", ["board.3.line.1"]="Inbox team", ["board.3.line.2"]="Launch party", ["board.3.line.3"]="Promo clips", ["board.3.line.4"]="Coffee tier",
            ["board.3.joke"]="The launch budget now contains a budget for the actual launch."
        };
        public static string T(string key)=>En.TryGetValue(key,out var value)?value:key;
        public static string F(string key,params object[] args)=>string.Format(CultureInfo.CurrentCulture,T(key),args);
        public static string Board(RunwayBoard board,string key)=>T("board."+board.Story+"."+key);
        public static string Level(int preset)=>T(preset==0?"full":preset==1?"lean":"minimum");
        public static string Summary(RunwayPlanResult result)=>F("summary",result.Valid,result.Filed);
    }
}
