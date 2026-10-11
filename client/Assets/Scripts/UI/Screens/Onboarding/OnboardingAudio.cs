using CTLife.Audio;

namespace CTLife.UI
{
    /// <summary>
    /// Music for the title screen and onboarding (device feedback 2026-10-09: the title and onboarding were silent; music only
    /// started at the lobby). Plays the title theme when the audio map has one: a music set keyed "title" or
    /// "mus_title" in sfx-map.json (the audio lane is making it). Until then it falls back to the lobby set, so first-time players hear
    /// music. If neither exists it stays silent without errors. Calling it again on the next screen keeps the same set
    /// playing (MusicDirector ignores a repeat), so the theme runs from the title through the creator into the lobby.
    /// </summary>
    public static class OnboardingAudio
    {
        public const string TitleKey = "title", TitleId = "mus_title", Fallback = "lobby";

        /// <summary>The music-set key to play ("title", then "mus_title", then the lobby set), or null for silence. Pure.</summary>
        public static string ThemeKey(System.Collections.Generic.ICollection<string> keys)
        {
            if (keys == null) return null;
            if (keys.Contains(TitleKey)) return TitleKey;
            if (keys.Contains(TitleId)) return TitleId;
            return keys.Contains(Fallback) ? Fallback : null;
        }

        /// <summary>
        /// The main theme (`mus_title`, audio lane 2026-10-09) via the audio API's idempotent <c>Music.PlayTitle()</c>: one
        /// continuous loop from the title through the lottery and creator, crossfading to the lobby set when the lobby opens.
        /// If a map ever lacks the title set, the lobby set plays instead (ThemeKey); with neither, silence, no error.
        /// </summary>
        public static void StartTheme()
        {
            var audio = AudioManager.Instance;
            if (audio == null || audio.Map == null) return;
            var key = ThemeKey(audio.Map.Music.Keys);
            if (key == TitleKey) Music.PlayTitle();
            else if (key != null) audio.Music?.SetDistrict(key);
        }
    }
}
