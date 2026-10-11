using System;
using System.Collections.Generic;

namespace CTLife.Audio
{
    public enum ClipLoad { DecompressOnLoad, CompressedInMemory, Streaming }

    /// <summary>Android import settings for one clip under Assets/Resources/Audio/ (applied by CTLife.Audio.Editor).</summary>
    public struct ClipImport
    {
        public string Group;
        public ClipLoad Load;
        /// <summary>Vorbis quality 0..1 (Unity scale). Every group uses Vorbis: smallest APK, decoded on load or on the fly.</summary>
        public float Quality;
        /// <summary>
        /// Always false: one-shots, footsteps and babble are authored mono, and Unity's Force To Mono downmix also
        /// peak-normalises ("Normalize", on by default), which would undo per-file levels (equal-RMS babble, quiet UI ticks).
        /// tools/ship_audio.py reports any stereo file in a mono group instead.
        /// </summary>
        public bool ForceMono;
        public bool Preload;
        public bool LoadInBackground;
        /// <summary>0 = keep the source rate (48 kHz).</summary>
        public int SampleRate;
    }

    /// <summary>
    /// One import policy per audio group (README "Unity import (Android)"):
    /// <list type="bullet">
    /// <item>music full mixes: Streaming, Vorbis 0.45, stereo — one decoder, no RAM copy.</item>
    /// <item>music stems/beds (adaptive layers): Compressed In Memory, Vorbis 0.45, stereo — five layers start on the
    /// same DSP tick; in-memory avoids five concurrent file streams.</item>
    /// <item>ambience: Streaming, Vorbis 0.35, stereo, background load.</item>
    /// <item>stingers (stereo, 2–6 s): Compressed In Memory, Vorbis 0.6.</item>
    /// <item>footsteps: Decompress On Load, Vorbis 0.5, 32 kHz (authored mono).</item>
    /// <item>babble syllables: Decompress On Load, Vorbis 0.5, 24 kHz (authored mono formant voice; nothing above 12 kHz matters).</item>
    /// <item>other one-shots (UI, trading, phone, voice room, ORE): Decompress On Load, Vorbis 0.6, preloaded (authored mono).</item>
    /// </list>
    /// </summary>
    public static class ImportPolicy
    {
        public const string Root = "Assets/Resources/Audio/";

        /// <summary>Stereo stinger files (sfx-map category "stinger").</summary>
        public static readonly HashSet<string> Stingers = new HashSet<string>
        {
            "sfx_liquidation", "sfx_rug", "sfx_graduation", "sfx_moon_horn", "sfx_rank_up", "sfx_sunday_unlock", "sfx_rekt_phonk",
        };

        /// <param name="assetPath">Unity asset path, e.g. "Assets/Resources/Audio/sfx/foot_wood_01.wav".</param>
        /// <returns>False when the path is not a CT Life audio clip.</returns>
        public static bool TryFor(string assetPath, out ClipImport import)
        {
            import = default;
            if (string.IsNullOrEmpty(assetPath)) return false;
            var p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Root, StringComparison.Ordinal)) return false;
            var rel = p.Substring(Root.Length);
            var file = rel.Substring(rel.LastIndexOf('/') + 1);
            int dot = file.LastIndexOf('.');
            var stem = dot > 0 ? file.Substring(0, dot) : file;
            var ext = dot > 0 ? file.Substring(dot).ToLowerInvariant() : "";
            if (ext != ".wav" && ext != ".ogg" && ext != ".mp3" && ext != ".aif" && ext != ".aiff" && ext != ".flac") return false;

            if (rel.StartsWith("music/", StringComparison.Ordinal))
            {
                bool layer = rel.StartsWith("music/stems/", StringComparison.Ordinal) || rel.StartsWith("music/mood/", StringComparison.Ordinal);
                import = layer
                    ? new ClipImport { Group = "music_layer", Load = ClipLoad.CompressedInMemory, Quality = 0.45f, LoadInBackground = true }
                    : new ClipImport { Group = "music", Load = ClipLoad.Streaming, Quality = 0.45f, LoadInBackground = true };
                return true;
            }
            if (rel.StartsWith("ambience/", StringComparison.Ordinal))
            {
                import = new ClipImport { Group = "ambience", Load = ClipLoad.Streaming, Quality = 0.35f, LoadInBackground = true };
                return true;
            }
            if (rel.StartsWith("babble/", StringComparison.Ordinal))
            {
                import = new ClipImport { Group = "babble", Load = ClipLoad.DecompressOnLoad, Quality = 0.5f, Preload = true, SampleRate = 24000 };
                return true;
            }
            if (stem.StartsWith("foot_", StringComparison.Ordinal))
            {
                import = new ClipImport { Group = "footstep", Load = ClipLoad.DecompressOnLoad, Quality = 0.5f, Preload = true, SampleRate = 32000 };
                return true;
            }
            if (Stingers.Contains(stem))
            {
                import = new ClipImport { Group = "stinger", Load = ClipLoad.CompressedInMemory, Quality = 0.6f, Preload = true };
                return true;
            }
            import = new ClipImport { Group = "sfx", Load = ClipLoad.DecompressOnLoad, Quality = 0.6f, Preload = true };
            return true;
        }
    }
}
