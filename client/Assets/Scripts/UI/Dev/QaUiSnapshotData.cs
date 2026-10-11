#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CTLife.UI.Dev
{
    public static class QaUiText
    {
        // Values of *all* editable controls and their internal text children are omitted.
        // Static labels survive; credential-looking diagnostics are conservatively redacted.
        private static readonly System.Text.RegularExpressions.Regex Secrets = new System.Text.RegularExpressions.Regex(
            @"(?i)\bbearer\s+|(?:password|passwd|secret|token|authorization|api[_-]?key|automation[_-]?key)\s*[:=]\s*\S|\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|[A-Za-z0-9_+/=-]{32,}");
        public static bool SensitiveName(string name)
        {
            var key = (name ?? "").Replace("-", "").Replace("_", "").ToLowerInvariant();
            return key.Contains("password") || key.Contains("secret") || key.Contains("username") ||
                key.Contains("authorization") || key.Contains("accesstoken") || key.Contains("refreshtoken") ||
                key.Contains("livekittoken") || key.Contains("automationkey") || key.Contains("apikey");
        }
        public static string Safe(string text, bool input)
        {
            if (input) return "[redacted]";
            if (string.IsNullOrEmpty(text)) return "";
            if (Secrets.IsMatch(text)) return "[redacted]";
            return text.Length > 256 ? text.Substring(0, 256) : text;
        }
    }

    public static class QaUiGeometry
    {
        // Rectangles are [x,y,width,height]; viewport is in panel coordinates, not safe-area coordinates.
        public static double[] ClipAndMap(double[] rect, double[] clip, double[] viewport, int width, int height)
        {
            foreach (var r in new[] { rect, clip, viewport })
            {
                if (r == null || r.Length != 4 || r[2] <= 0 || r[3] <= 0) return null;
                foreach (var v in r) if (double.IsNaN(v) || double.IsInfinity(v)) return null;
            }
            if (width <= 0 || height <= 0) return null;
            double x = System.Math.Max(rect[0], System.Math.Max(clip[0], viewport[0]));
            double y = System.Math.Max(rect[1], System.Math.Max(clip[1], viewport[1]));
            double right = System.Math.Min(rect[0] + rect[2], System.Math.Min(clip[0] + clip[2], viewport[0] + viewport[2]));
            double bottom = System.Math.Min(rect[1] + rect[3], System.Math.Min(clip[1] + clip[3], viewport[1] + viewport[3]));
            if (right <= x || bottom <= y) return null;
            return new[] { (x - viewport[0]) * width / viewport[2], (y - viewport[1]) * height / viewport[3],
                (right - x) * width / viewport[2], (bottom - y) * height / viewport[3] };
        }
    }

    public static class QaUiSnapshotFile
    {
        public static void Write(string path, string json)
        {
            string temp = path + ".tmp";
            try
            {
                System.IO.File.WriteAllText(temp, json, new System.Text.UTF8Encoding(false));
                // Same-directory rename; never delete the old file before publishing the new one.
                if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
                else System.IO.File.Move(temp, path);
            }
            finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
        }
    }

    public sealed class QaUiNode
    {
        public string id, parentId, type, name, label, picking;
        public string[] ancestors;
        public double[] panelBounds, screenBounds, tapPoint, scrollOffset;
        public bool clipKnown, visible, disabled, input, tapCandidate, occlusionKnown, occluded, centerOccluded;
        public bool? toggle;
    }

    public sealed class QaUiAudio
    {
        public int id;
        public string clip;
        public bool playing, enabled, mute, loop;
        public float volume, pitch, time, length;
    }

    // Pure data/geometry code also exercised outside Unity by the offline NUnit runner.
    public sealed class QaUiSnapshot
    {
        public int schemaVersion = 1;
        public string coordinateOrigin = "top-left";
        public string session, utc;
        public long sequence;
        public int frame, screenWidth, screenHeight;
        public double realtime;
        public double[] panelViewport;
        public List<QaUiNode> nodes = new List<QaUiNode>();
        public List<QaUiAudio> audio = new List<QaUiAudio>();
        public string ToJson() => JsonConvert.SerializeObject(this);
    }
}
#endif
