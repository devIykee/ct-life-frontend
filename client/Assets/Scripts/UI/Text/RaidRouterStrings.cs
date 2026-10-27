using CTLife.Core;
using System.Collections.Generic;
using System.Globalization;
namespace CTLife.UI
{
    public static class RaidRouterStrings
    {
        private static readonly Dictionary<string,string> En=new Dictionary<string,string>
        {
            ["title"]="Raid Router", ["eyebrow"]="DISCORD MOD · PRACTICE", ["practice"]="LOCAL PRACTICE · NO PAYOUT",
            ["brief"]="Tap a lane to move your shield. Block RAID. Let CHAT through.",
            ["rule"]="Each friendly chat you block cancels one raid save. No points for idle time.",
            ["timer"]="{0}s", ["wave"]="Wave {0} / 3", ["tally"]="{0} / 12 raids stopped",
            ["combo"]="Clean streak {0}", ["score"]="{0} / 100", ["scoreLabel"]="SCORE", ["lane.0"]="#general", ["lane.1"]="#builders", ["lane.2"]="#support",
            ["shield"]="PROTECTED", ["move"]="TAP TO PROTECT", ["park"]="Park shield", ["pause"]="Pause", ["resume"]="Resume",
            ["paused"]="Paused", ["pauseDetail"]="Your practice clock is on hold.",
            ["raid"]="RAID", ["chat"]="CHAT", ["waiting"]="Keep the conversation open.", ["stopped"]="Raid stopped", ["passed"]="Chat delivered",
            ["blocked"]="Friendly chat blocked", ["leaked"]="Raid got through", ["closeCall"]="Just in time!", ["clear"]="Inbox clear. Nice breathing room.",
            ["end"]="End practice", ["again"]="Play again", ["back"]="Back to city", ["done"]="Last completed round", ["interrupted"]="Practice interrupted",
            ["recover"]="No score was submitted. You can try again.",
            ["summary"]="Raids stopped: {0} · Chats blocked: {1}\nBest streak: {2} · Last-moment saves: {3}",
            ["review"]="REVIEW · SCRIPTED INPUT · NO PAYOUT",
            ["chat.0"]="build finally passed", ["chat.1"]="coffee, then deploy", ["chat.2"]="can we pin the docs?", ["chat.3"]="found the typo",
            ["chat.4"]="wrong conference room", ["chat.5"]="demo survived", ["chat.6"]="anyone seen my badge?", ["chat.7"]="fixed one more thing",
            ["raid.0"]="FREE CLAIM ×99", ["raid.1"]="WALLET CHECK ×99", ["raid.2"]="ADMIN DM ×99", ["raid.3"]="SECRET LINK ×99",
            ["raid.4"]="URGENT CLAIM ×99", ["raid.5"]="VERIFY NOW ×99", ["raid.6"]="MIRROR MINT ×99", ["raid.7"]="BONUS AIRDROP ×99"
        };
        public static string T(string key)=>En.TryGetValue(key,out var value)? NameTable.Apply(value) :key;
        public static string F(string key,params object[] args)=>string.Format(CultureInfo.CurrentCulture,T(key),args);
        public static string Summary(RaidRouterResult result)=>F("summary",result.RaidsStopped,result.ChatsBlocked,result.BestCombo,result.CloseCalls);
    }
}
