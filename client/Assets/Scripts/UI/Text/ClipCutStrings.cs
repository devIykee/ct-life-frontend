using System.Collections.Generic;
using System.Globalization;
namespace CTLife.UI
{
    public static class ClipCutStrings
    {
        private static readonly Dictionary<string,string> En=new Dictionary<string,string>
        {
            ["title"]="Clip Cut",["score"]="{0} / 100",["timer"]="{0}s",["reel"]="REEL {0} / 5",
            ["brief"]="Keep the whole joke. Trim the waiting and side tracks.",
            ["moveIn"]="Move IN",["moveOut"]="Move OUT",["placingIn"]="Tap a frame to place IN.",["placingOut"]="Tap a frame to place OUT.",
            ["range"]="IN {0:00}  -  OUT {1:00}",["kept"]="{0} / 8 frames kept",["frame"]="{0:00}",["in"]="IN",["out"]="OUT",["both"]="IN / OUT",
            ["setup"]="Setup",["beat"]="Beat",["payoff"]="Payoff",["deadair"]="Dead air",["sidetrack"]="Side track",
            ["frameCaption"]="Frame {0:00}: {1}",["previewNumber"]="FRAME {0:00}",
            ["preview"]="Preview cut",["stop"]="Stop preview",["previewLabel"]="STORYBOARD PREVIEW",["previewFrame"]="Frame {0:00} · {1}",
            ["reset"]="Reset trim",["cut"]="Cut reel",["next"]="Next reel",["clean"]="Clean cut · +20 points",
            ["wrong"]="Cut saved · 0 points",["missingSetup"]="The setup was trimmed out.",["missingPayoff"]="The payoff was trimmed out.",
            ["extra"]="The cut includes waiting or a side track.",["answer"]="Keep frames {0:00}–{1:00}.",
            ["pause"]="Pause",["resume"]="Resume",["end"]="End practice",["paused"]="Edit on hold",["pauseDetail"]="The practice clock and storyboard preview are paused. Resume when you’re ready.",
            ["practice"]="LOCAL PRACTICE · ORIGINAL STORYBOARDS · NO POSTING OR PAYOUT",["review"]="REVIEW · SCRIPTED INPUT · ORIGINAL STORYBOARDS · NO PAYOUT",
            ["again"]="Play again",["back"]="Back to city",["complete"]="Practice complete",["summary"]="Clean cuts: {0} / 5 · Reels cut: {1}\nOriginal storyboards only. Nothing was posted.",
            ["story.0.title"]="One-minute update",["story.0.0"]="Empty stage",["story.0.1"]="Sponsor mug close-up",["story.0.2"]="Host: one minute.",["story.0.3"]="The agenda unfolds.",["story.0.4"]="Another page unfolds.",["story.0.5"]="Timer: forty minutes.",["story.0.6"]="Silent end card",["story.0.7"]="Merch link",
            ["story.1.title"]="The live demo",["story.1.0"]="Loading screen",["story.1.1"]="Presenter: live demo.",["story.1.2"]="Cursor stays still.",["story.1.3"]="Video controls appear.",["story.1.4"]="Button says Replay.",["story.1.5"]="Sponsor price slide",["story.1.6"]="Muted outro",["story.1.7"]="Empty browser tab",
            ["story.2.title"]="Community consensus",["story.2.0"]="Sticker pack",["story.2.1"]="Holding slide",["story.2.2"]="Poll: ship or delay?",["story.2.3"]="Votes arrive.",["story.2.4"]="Delay takes the lead.",["story.2.5"]="Host asks for a recount.",["story.2.6"]="New poll: trust the poll?",["story.2.7"]="Silent outro",
            ["story.3.title"]="Roadmap reveal",["story.3.0"]="Three milestones.",["story.3.1"]="Task: make roadmap.",["story.3.2"]="All milestones green.",["story.3.3"]="Blank summary slide",["story.3.4"]="Sticker close-up",["story.3.5"]="Holding slide",["story.3.6"]="Coffee break",["story.3.7"]="Empty title slide",
            ["story.4.title"]="Support ticket",["story.4.0"]="Mascot montage",["story.4.1"]="Blank screen",["story.4.2"]="Logo animation",["story.4.3"]="Waiting room",["story.4.4"]="User: app is broken.",["story.4.5"]="Support: which button?",["story.4.6"]="Screenshot arrives.",["story.4.7"]="It’s the calculator."
        };
        public static string T(string key)=>En.TryGetValue(key,out var value)?value:key;
        public static string F(string key,params object[] args)=>string.Format(CultureInfo.CurrentCulture,T(key),args);
        public static string Title(ClipReel reel)=>T("story."+reel.Story+".title");
        public static string Frame(ClipReel reel,int frame)=>T("story."+reel.Story+"."+frame);
        public static string Kind(ClipBeatKind kind)=>T(kind.ToString().ToLowerInvariant());
        public static string Summary(ClipCutResult result)=>F("summary",result.Clean,result.Cuts);
    }
}
