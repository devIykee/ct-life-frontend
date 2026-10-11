using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    public static class SpacesStrings
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            ["review"] = "DEVELOPMENT REVIEW · SIMULATED ROOMS · NO AUDIO",
            ["reviewReport"] = "Report submission is disabled in this visual review.",
            ["reviewTitle"] = "One less thing on fire", ["reviewOther"] = "After-hours table",
            ["reviewHost"] = "Sample host", ["reviewSpeaker"] = "Sample builder", ["reviewMe"] = "Sample listener",
            ["closeCompose"] = "Close draft", ["sessionEnded"] = "Your session ended. Sign in again to join a Space.",
            ["title"] = "Spaces", ["heading"] = "Pull up a chair.",
            ["intro"] = "Drop in, listen, or start a conversation.",
            ["live"] = "LIVE ROOMS", ["loading"] = "Checking the room list…", ["refresh"] = "Refresh",
            ["empty"] = "It's quiet here.", ["emptyDetail"] = "No conversations are open. Start one when you have something to share.",
            ["unavailable"] = "Spaces aren’t ready yet.",
            ["unavailableDetail"] = "Come back to the city and try again later.",
            ["retry"] = "Try again", ["listen"] = "Listen in", ["open"] = "Open room", ["listeners"] = "{0} listening",
            ["host"] = "Host", ["speaker"] = "Speaker", ["listener"] = "Listener", ["you"] = "{0} · you",
            ["nameFallback"] = "Player {0}", ["hostRoom"] = "Start a Space", ["topic"] = "What’s the conversation?",
            ["topicHint"] = "Give people a reason to stop scrolling.", ["create"] = "Open the room",
            ["createHint"] = "Your mic starts off. Leaving stops your audio; End Space closes the room.",
            ["createRetry"] = "Try again", ["createUncertain"] = "We couldn’t confirm your room opened. Check the room list before trying again.",
            ["topicRequired"] = "Give your Space a title first.", ["stage"] = "ON STAGE", ["queue"] = "HANDS UP · {0}",
            ["queueEmpty"] = "No hands up yet.", ["invite"] = "Invite to speak", ["audience"] = "Move to audience",
            ["full"] = "The stage is full. Move someone to the audience to make room.",
            ["raise"] = "Raise hand", ["lower"] = "Lower hand", ["handHint"] = "Raise your hand to ask for a turn. Your mic stays off.",
            ["speakingHint"] = "You’re on stage. You control your microphone.", ["micOn"] = "Turn mic on",
            ["micOff"] = "Turn mic off", ["micLive"] = "MIC ON", ["micSilent"] = "MIC OFF", ["speaking"] = "Speaking",
            ["connecting"] = "Joining audio…", ["connected"] = "Listening", ["disconnected"] = "Audio off",
            ["reconnect"] = "Join audio", ["audioHint"] = "Close Spaces to leave audio. Nearby voice pauses while you’re here.",
            ["mute"] = "Mute room", ["unmute"] = "Hear room", ["muted"] = "Room muted",
            ["leave"] = "Leave room", ["end"] = "End Space", ["endAsk"] = "End this Space for everyone?",
            ["endDetail"] = "This closes the conversation for everyone.", ["confirmEnd"] = "End for everyone",
            ["cancel"] = "Cancel", ["ended"] = "Space ended.", ["stale"] = "Refreshing the stage. Your mic is off.",
            ["busy"] = "One moment…", ["changed"] = "The stage changed. Refresh and try again.",
            ["failed"] = "We couldn’t confirm that. Refresh and check.",
            ["network"] = "Couldn’t refresh the room. Try again.",
            ["timeout"] = "We couldn’t confirm that. Refresh to see what changed before trying again.",
            ["invalid"] = "We couldn’t load this room. Please try again later.",
            ["micInterrupted"] = "Your mic stayed off. Turn it on again when you’re ready.",
            ["permission"] = "Microphone permission wasn't granted. You can still listen.",
            ["noMic"] = "No microphone was found on this device.", ["roleChanged"] = "Your mic is off. Refresh to check your place on stage.",
            ["audioFailed"] = "Audio couldn’t connect. Your mic is off. Try again.",
            ["report"] = "Report Space", ["reportTitle"] = "What happened?", ["reportSent"] = "Report received. Thank you.",
            ["reportUnavailable"] = "Reporting options are unavailable. You can leave the room now.",
            ["moreRooms"] = "Show more rooms", ["moreHands"] = "Show more hands", ["city"] = "Return to city", ["unavailableRoom"] = "This room is unavailable. Your mic is off.",
        };
        public static string T(string key) => Copy.TryGetValue(key, out var value) ? value : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
