using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    public static class LiveCuesStrings
    {
        private static readonly Dictionary<string,string> En = new Dictionary<string,string>
        {
            ["title"]="Live Cues", ["eyebrow"]="THE TIMELINE TONIGHT", ["score"]="{0} / 100", ["timer"]="{0}s",
            ["brief"]="Match the cue in the gold window.",
            ["onAir"]="ON AIR", ["onHold"]="HOLD", ["nextAction"]="NEXT · {0}",
            ["rule"]="One tap per cue · 0.8-second window · +5 for the right control on time",
            ["camera"]="Camera", ["caption"]="Caption", ["applause"]="Applause",
            ["cue"]="CUE {0:00} / 20", ["next"]="UP NEXT", ["standby"]="STAND BY", ["go"]="GO NOW", ["rolling"]="SHOW WRAPPED",
            ["in"]="In {0:0.0}s", ["window"]="{0:0.0}s left", ["windowLabel"]="0.8s · +5", ["railStart"]="ONE TAP PER CUE", ["railEnd"]="0.8s WINDOW",
            ["ready"]="Follow the cue. The window opens in gold.", ["hit"]="On cue · +5", ["early"]="Too early · wait for the gold window",
            ["wrong"]="Wrong control · this cue needed {0}", ["missed"]="Missed cue · this cue needed {0}", ["wrapped"]="Twenty cues. That’s a wrap.",
            ["tally"]="{0} / 20 on cue", ["combo"]="{0} cue streak", ["padReady"]="READY", ["padWait"]="STAND BY", ["padGo"]="ON YOUR CUE",
            ["pause"]="Pause", ["resume"]="Resume", ["end"]="End practice", ["paused"]="Show on hold",
            ["pauseDetail"]="Your practice clock is paused. Resume when you’re ready for the next cue.",
            ["practice"]="Practice · fictional show · no payout", ["review"]="Review · scripted fictional show · no payout",
            ["again"]="Play again", ["back"]="Back to city", ["complete"]="Practice complete",
            ["summary"]="On cue: {0} / 20 · Best streak: {1}\nEarly: {2} · Wrong controls: {3} · Missed: {4}",
            ["camera.0"]="Cut to the host. Finally unmuted.", ["camera.1"]="The demo is ready. The cable is not.",
            ["camera.2"]="Zoom in on the roadmap. It’s a napkin.", ["camera.3"]="Close-up: three backup laptops.",
            ["camera.4"]="Wide shot. The panel outgrew the sofa.", ["camera.5"]="The mascot has taken the host’s chair.",
            ["camera.6"]="The chart expert is holding it upside down.", ["camera.7"]="Our correspondent has reached the coffee queue.",
            ["camera.8"]="The mystery box contains another adapter.", ["camera.9"]="The product finally loads. Get the shot.",
            ["caption.0"]="Add the guest’s title: Still Shipping.", ["caption.1"]="Label the demo: Recorded After Six Attempts.",
            ["caption.2"]="The source is a spreadsheet. Put that on screen.", ["caption.3"]="Add the correction: Tuesday, Not Soon.",
            ["caption.4"]="The chart starts at zero. Keep the label visible.", ["caption.5"]="The sponsor sent snacks. Credit the snacks.",
            ["caption.6"]="Show the agenda: One Question, Eventually.", ["caption.7"]="Add the disclaimer: Mascot Cannot Code.",
            ["caption.8"]="Name the segment: Where Did the Tab Go?", ["caption.9"]="Put the launch date up before it changes again.",
            ["applause.0"]="Someone answered the question in one sentence.", ["applause.1"]="The demo worked without a restart.",
            ["applause.2"]="The guest admitted the estimate was wrong.", ["applause.3"]="The host reached the point by slide three.",
            ["applause.4"]="The roadmap gained an actual date.", ["applause.5"]="The community call ended on time.",
            ["applause.6"]="The bug report included steps to reproduce.", ["applause.7"]="The panel agreed to define one acronym.",
            ["applause.8"]="The backup cable has saved the show.", ["applause.9"]="The last slide contains a working link."
        };
        public static string T(string key) => En.TryGetValue(key, out var value) ? value : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
        public static string Action(LiveCueAction action) => T(action.ToString().ToLowerInvariant());
        public static string Cue(LiveCue cue) => T(cue.Action.ToString().ToLowerInvariant()+"."+cue.Copy);
        public static string Summary(LiveCuesResult result) => F("summary", result.Hits, result.BestCombo, result.Early, result.Wrong, result.Missed);
    }
}
