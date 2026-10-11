using System.IO;
using CTLife.UI;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>
    /// The squircle title button bodies (Resources/UI/Title/btn-*-blank-sq.png) are 9-sliced with side caps of
    /// SignInScreen.SquircleSideSlice of the height. The caps must hold every curved pixel of the painted corner, or the
    /// stretch distorts it at wide buttons. Measured from the PNG itself (decoded here, not the compressed import).
    /// </summary>
    public class TitleButtonArtTests
    {
        private const string Folder = "Assets/Resources/UI/Title/";

        [TestCase("green")]
        [TestCase("purple")]
        public void SquircleCornersFitInsideTheSideSlices(string tone)
        {
            var path = Folder + "btn-" + tone + "-blank-sq.png";
            if (!File.Exists(path)) Assert.Ignore(path + " not delivered yet (the pill body is sliced at half its height)");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(path)), "PNG decode");
                int w = tex.width, h = tex.height;
                int side = Mathf.Min(h / 2 - 1, Mathf.CeilToInt(h * SignInScreen.SquircleSideSlice));
                int corner = CornerExtent(tex);
                TestContext.WriteLine($"{path}: {w}x{h}, painted corner {corner} px, side slice {side} px");
                Assert.LessOrEqual(corner, side, "corner curve reaches into the stretched middle");
                Assert.Less(side * 2, w, "side slices overlap");
            }
            finally { Object.DestroyImmediate(tex); }
        }

        /// <summary>
        /// How far in from the left or right edge the body's outline is still curving: the outermost column at which the
        /// top or bottom edge of the opaque body is within 1 px of the flat edge measured at the centre column.
        /// </summary>
        private static int CornerExtent(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            bool Opaque(int x, int y) => px[y * w + x].a > 128;
            int Top(int x) { for (int y = h - 1; y >= 0; y--) if (Opaque(x, y)) return y; return -1; }
            int Bottom(int x) { for (int y = 0; y < h; y++) if (Opaque(x, y)) return y; return h; }
            int topFlat = Top(w / 2), bottomFlat = Bottom(w / 2);
            int extent = 0;
            for (int x = 0; x < w / 2; x++)
            {
                bool flatLeft = Top(x) >= topFlat - 1 && Bottom(x) <= bottomFlat + 1;
                bool flatRight = Top(w - 1 - x) >= topFlat - 1 && Bottom(w - 1 - x) <= bottomFlat + 1;
                if (flatLeft && flatRight) { extent = x; break; }
                extent = x;
            }
            return extent;
        }
    }
}
