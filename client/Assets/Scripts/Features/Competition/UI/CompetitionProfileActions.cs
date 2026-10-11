using System;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>
    /// Extra buttons for another player's profile card: "Call out to a beef" and "Invite to the Cabal". Compose it with the other lanes'
    /// profile actions in the composition root (hook in docs/progress/requests.md). Both only open the phone app with that player
    /// preselected; the server decides whether the beef or invite is allowed.
    /// </summary>
    public static class CompetitionProfileActions
    {
        public static VisualElement Build(string userId, string name, Action close)
        {
            if (!CompWire.IsSafeId(userId)) return null;
            var row = El.Div("ct-row");
            row.Add(El.Btn(CompStrings.T("profile.beef"), () => { close?.Invoke(); CompetitionLinks.ChallengeToBeef(userId, name); }, "ct-btn"));
            row.Add(El.Btn(CompStrings.T("profile.cabal"), () => { close?.Invoke(); CompetitionLinks.InviteToCabal(userId, name); }, "ct-btn"));
            return row;
        }

        /// <summary>The "Talk" button for the AI Agent and Sleuth on an NPC profile card; null for any other NPC.</summary>
        public static VisualElement BuildNpc(string archetypeId, string npcId, Action close)
        {
            if (!ChatSession.IsLlmNpc(archetypeId)) return null;
            return El.Btn(CompStrings.T("profile.talk"), () => { close?.Invoke(); CompetitionLinks.OpenNpcChat(archetypeId, npcId); }, "ct-btn");
        }
    }
}
