#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Audio;
using CTLife.Core;
using UnityEngine;

namespace CTLife.App.Review
{
    /// <summary>Device playback checks against the imported clips and real Unity sources; not a listening review.</summary>
    internal static class AudioPresentationReview
    {
        public static async Task Run()
        {
            var audio = AudioManager.Instance;
            float musicVolume = audio.musicVolume, sfxVolume = audio.sfxVolume;
            try
            {
                audio.musicVolume = 0.7f;
                audio.sfxVolume = 1f;
                Bus.Publish(new VoiceActivitySignal { anyoneTalking = false });
                string[] keys = { "lobby", "timeline", "trenches", "perp_pit", "builder_hills", "dubai_marina", "clubs", "airdrop_farm", "terminal_cafe" };
                foreach (string key in keys)
                {
                    Require(audio.Map.Music.TryGetValue(key, out var entry) && !string.IsNullOrEmpty(entry.fullMix), "Missing full arrangement: " + key);
                    var clip = audio.Clip(entry.fullMix);
                    Require(clip != null && clip.length > 120f && clip.loadType == AudioClipLoadType.Streaming,
                        "Missing, truncated or nonstreaming arrangement: " + key);
                    audio.Music.SetDistrict(key);
                    await Awaitable.WaitForSecondsAsync(1.8f);
                    var source = ActiveMix(audio);
                    Require(source != null && source.clip == clip && source.isPlaying && source.volume > 0.1f,
                        "Arrangement did not begin: " + key + "; " + audio.Music.ReviewSnapshot());
                    Require(MusicSources(audio).Count(s => s.isPlaying) == 1,
                        "Previous music bank did not clear: " + key);
                    int phase = source.timeSamples;
                    await Awaitable.WaitForSecondsAsync(0.3f);
                    Require(source.timeSamples > phase + clip.frequency * 0.1f, "Stream did not advance: " + key);
                    Log.Info("review-audio", $"PASS full arrangement {key}: {clip.name}, {clip.length:0.00}s, {clip.loadType}; phase={source.time:0.00}.");
                }

                audio.Music.SetDistrict("lobby");
                await Awaitable.WaitForSecondsAsync(2f);
                var lobby = ActiveMix(audio);
                int lobbyPhase = lobby.timeSamples;
                audio.Music.SetMusic("timeline_square", "timeline");
                await Awaitable.WaitForSecondsAsync(0.3f);
                Require(ActiveMix(audio) == lobby && lobby.timeSamples > lobbyPhase + lobby.clip.frequency * 0.1f,
                    "Lobby to city restarted the shared composition.");
                Log.Info("review-audio", "PASS lobby → Timeline keeps source and playback phase.");

                audio.musicVolume = 0f;
                await Awaitable.NextFrameAsync();
                await Awaitable.EndOfFrameAsync();
                Require(MusicSources(audio).All(s => s.volume == 0f), "Live music mute left gain above zero.");
                audio.musicVolume = 0.7f;
                await Awaitable.NextFrameAsync();
                audio.Music.SetMusic("spaces_studio", "timeline");
                await Awaitable.WaitForSecondsAsync(1.5f);
                Require(MusicSources(audio).All(s => !s.isPlaying && s.clip == null),
                    "Spaces Studio retained district music.");
                Log.Info("review-audio", "PASS live music mute and voice-only Spaces Studio.");

                audio.Music.SetDistrict("timeline");
                await Awaitable.WaitForSecondsAsync(1.8f);
                var duckSource = ActiveMix(audio);
                float unduckedGain = duckSource.volume;
                float stingerLength = audio.Clip(audio.Map.Sfx["sfx_graduation"].files[0]).length;
                audio.Play("sfx_graduation");
                Require(Mathf.Abs(audio.MusicDuckDb + 9f) < 0.01f, "Stinger did not duck music.");
                await CheckDuckGain(duckSource, unduckedGain, -9f);
                Bus.Publish(new VoiceActivitySignal { anyoneTalking = true });
                Require(Mathf.Abs(audio.MusicDuckDb + 15f) < 0.01f, "Voice and stinger duck did not combine.");
                await CheckDuckGain(duckSource, unduckedGain, -15f);
                audio.sfxVolume = 0f;
                Require(Mathf.Abs(audio.MusicDuckDb + 6f) < 0.01f, "SFX mute did not release just the stinger duck.");
                await CheckDuckGain(duckSource, unduckedGain, -6f);
                Bus.Publish(new VoiceActivitySignal { anyoneTalking = false });
                Require(audio.MusicDuckDb == 0f, "Voice release left a duck while SFX muted.");
                await CheckDuckGain(duckSource, unduckedGain, 0f);
                await Awaitable.WaitForSecondsAsync(stingerLength + 0.2f);
                audio.sfxVolume = 1f;
                Require(audio.MusicDuckDb == 0f, "Finished stinger still ducks after unmute.");
                Log.Info("review-audio", "PASS stinger/voice overlap, SFX mute, voice release and duck expiry.");

                foreach (string id in new[] { "sfx_ui_tap", "sfx_ui_toast", "sfx_ui_toggle_on", "sfx_ui_toggle_off", "sfx_ui_success", "sfx_ui_error" })
                {
                    Require(audio.Map.Sfx.TryGetValue(id, out var entry) && entry.files.All(p => audio.Clip(p) != null), "Missing sourced UI cue " + id);
                    audio.Play(id);
                    audio.Play(id);
                }
                var snapshot = audio.ReviewSnapshot();
                Require(snapshot.Contains("uiPlaying=2/2"), "UI burst did not retain exactly two bounded voices: " + snapshot);
                Log.Info("review-audio", "PASS sourced UI burst: " + snapshot);
                var credits = Resources.Load<TextAsset>("Audio/Music-Credits");
                string[] titles = { "Funky Chunk", "Got Funk", "Getting it Done", "Pamgaea", "Paradise Found", "Disco con Tutti", "Easy Lemon", "Airport Lounge" };
                Require(credits != null && titles.All(title => credits.text.Contains(title)) &&
                    credits.text.Contains("Kevin MacLeod") && credits.text.Contains("https://creativecommons.org/licenses/by/4.0/"),
                    "Bundled music credits missing.");
                Log.Info("review-check", "PASS audio presentation device checks; playback state only, not subjective listening approval.");
            }
            finally
            {
                Bus.Publish(new VoiceActivitySignal { anyoneTalking = false });
                audio.musicVolume = musicVolume;
                audio.sfxVolume = sfxVolume;
                audio.Music.SetDistrict("timeline");
            }
        }

        // MusicDirector shares its GameObject with AudioManager. Its child traversal also
        // sees ambience/SFX; only the named music banks belong to this check.
        private static System.Collections.Generic.IEnumerable<AudioSource> MusicSources(AudioManager audio) =>
            audio.Music.GetComponentsInChildren<AudioSource>().Where(s => s.name.StartsWith("music_", StringComparison.Ordinal));
        private static AudioSource ActiveMix(AudioManager audio) => MusicSources(audio)
            .SingleOrDefault(s => s.name.EndsWith("_full_mix", StringComparison.Ordinal) && s.clip != null && s.isPlaying && s.volume > 0.1f);
        private static async Task CheckDuckGain(AudioSource source, float baseline, float db)
        {
            await Awaitable.NextFrameAsync();
            await Awaitable.EndOfFrameAsync();
            float expected = baseline * Mathf.Pow(10f, db / 20f);
            Require(Mathf.Abs(source.volume - expected) < 0.002f, $"Music source gain at {db}dB is {source.volume}, expected {expected}.");
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
#endif
