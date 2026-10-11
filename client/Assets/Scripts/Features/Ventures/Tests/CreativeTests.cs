using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Ventures.Tests
{
    public sealed class CreativeCheckTests
    {
        private readonly CreativeLimits _limits = new CreativeLimits();

        [Test]
        public void ValidPng_ReportsMimeAndSize()
        {
            var r = CreativeCheck.Check(Kit.Png(1024, 512), "a.png", _limits, 1024, 512);
            Assert.IsTrue(r.Ok);
            Assert.AreEqual("image/png", r.Mime);
            Assert.AreEqual(1024, r.Width);
            Assert.AreEqual(512, r.Height);
            Assert.IsFalse(r.AspectWarning);
        }

        [Test]
        public void ValidJpeg_ReadsTheFrameHeaderPastOtherSegments()
        {
            var r = CreativeCheck.Check(Kit.Jpeg(800, 400), "a.jpg", _limits);
            Assert.IsTrue(r.Ok);
            Assert.AreEqual("image/jpeg", r.Mime);
            Assert.AreEqual(800, r.Width);
            Assert.AreEqual(400, r.Height);
        }

        [Test]
        public void JpegWithoutAFrameHeader_IsUnreadable()
        {
            Assert.AreEqual(CreativeProblem.UnreadableHeader, CreativeCheck.Check(Kit.Jpeg(10, 10, sof: false), "a.jpg", _limits).Problem);
        }

        [Test]
        public void EmptyAndNull() 
        {
            Assert.AreEqual(CreativeProblem.Empty, CreativeCheck.Check(null, "a", _limits).Problem);
            Assert.AreEqual(CreativeProblem.Empty, CreativeCheck.Check(new byte[0], "a", _limits).Problem);
        }

        [Test]
        public void OverTheByteCap_IsTooLarge_AtTheExactBoundary()
        {
            Assert.IsTrue(CreativeCheck.Check(Kit.Png(10, 10, (int)CreativeLimits.DefaultMaxBytes), "a.png", _limits).Ok, "exactly 1 MB is allowed");
            Assert.AreEqual(CreativeProblem.TooLarge, CreativeCheck.Check(Kit.Png(10, 10, (int)CreativeLimits.DefaultMaxBytes + 1), "a.png", _limits).Problem);
        }

        [Test]
        public void NotAnImage_ByMagicBytes_NotByName()
        {
            var text = System.Text.Encoding.ASCII.GetBytes("GIF89a......................................");
            Assert.AreEqual(CreativeProblem.NotPngOrJpeg, CreativeCheck.Check(text, "evil.png", _limits).Problem);
        }

        [Test]
        public void ADecompressionBomb_IsRefusedFromTheHeader()
        {
            Assert.AreEqual(CreativeProblem.DecodeTooBig, CreativeCheck.Check(Kit.Png(30000, 30000), "a.png", _limits).Problem);
            Assert.AreEqual(CreativeProblem.DecodeTooBig, CreativeCheck.Check(Kit.Png(4097, 10), "a.png", _limits).Problem);
            Assert.AreEqual(CreativeProblem.DecodeTooBig, CreativeCheck.Check(Kit.Png(4096, 4096), "a.png", _limits).Problem, "16 Mpx is over the pixel cap");
            Assert.IsTrue(CreativeCheck.Check(Kit.Png(4096, 2048), "a.png", _limits).Ok, "8 Mpx is the cap");
        }

        [Test]
        public void ZeroSizedHeader_IsUnreadable() =>
            Assert.AreEqual(CreativeProblem.UnreadableHeader, CreativeCheck.Check(Kit.Png(0, 0), "a.png", _limits).Problem);

        [Test]
        public void AspectWarning_OnlyWhenTheSlotPublishesASize()
        {
            Assert.IsTrue(CreativeCheck.Check(Kit.Png(500, 500), "a.png", _limits, 1024, 512).AspectWarning);
            Assert.IsFalse(CreativeCheck.Check(Kit.Png(500, 500), "a.png", _limits, 0, 0).AspectWarning);
            Assert.IsFalse(CreativeCheck.Check(Kit.Png(2048, 1024), "a.png", _limits, 1024, 512).AspectWarning, "same shape, other scale");
            Assert.IsFalse(CreativeCheck.Check(Kit.Png(1024, 505), "a.png", _limits, 1024, 512).AspectWarning, "within 2%");
        }

        [Test]
        public void MimeNotInTheConfiguredList_IsRefused()
        {
            var only = new CreativeLimits { AcceptMime = new HashSet<string> { "image/jpeg" } };
            Assert.AreEqual(CreativeProblem.MimeNotAccepted, CreativeCheck.Check(Kit.Png(10, 10), "a.png", only).Problem);
        }

        [Test]
        public void FileNameIsCleaned() => Assert.AreEqual("ab", CreativeCheck.Check(Kit.Png(10, 10), "a\nb", _limits).FileName);

        [Test]
        public void LimitsFromConfig_NeverWidenBeyondWhatTheClientCanVerify()
        {
            var l = CreativeLimits.FromConfig(JToken.Parse(@"{""creative"":{""maxSizeMB"":2,""acceptMime"":[""image/png"",""image/gif"",""image/svg+xml""]}}"));
            Assert.AreEqual(2 * 1024 * 1024, l.MaxBytes);
            CollectionAssert.AreEquivalent(new[] { "image/png" }, l.AcceptMime);
            var d = CreativeLimits.FromConfig(null);
            Assert.AreEqual(CreativeLimits.DefaultMaxBytes, d.MaxBytes);
            Assert.AreEqual(2, d.AcceptMime.Count);
            Assert.AreEqual(CreativeLimits.DefaultMaxBytes, CreativeLimits.FromConfig(JToken.Parse(@"{""creative"":{""maxSizeMB"":9999}}")).MaxBytes, "absurd limits are ignored");
            Assert.AreEqual(2, CreativeLimits.FromConfig(JToken.Parse(@"{""creative"":{""acceptMime"":[""image/gif""]}}")).AcceptMime.Count);
        }

        [TestCase("", true)]
        [TestCase(null, true)]
        [TestCase("https://example.org/launch?x=1", true)]
        [TestCase("http://example.org", false)]
        [TestCase("https://", false)]
        [TestCase("https://ex ample.org", false)]
        [TestCase("https://example.org/a\nb", false)]
        [TestCase("ftp://example.org", false)]
        [TestCase("javascript:alert(1)", false)]
        public void Link(string link, bool ok) => Assert.AreEqual(ok, CreativeCheck.IsAllowedLink(link));

        [Test]
        public void Link_TooLong() => Assert.IsFalse(CreativeCheck.IsAllowedLink("https://example.org/" + new string('a', 2100)));

        [Test]
        public void Fingerprint_IdentifiesTheSameBytes()
        {
            var a = Kit.Png(10, 10);
            var b = Kit.Png(10, 10);
            var c = Kit.Png(10, 11);
            Assert.AreEqual(CreativeCheck.Fingerprint(a), CreativeCheck.Fingerprint(b));
            Assert.AreNotEqual(CreativeCheck.Fingerprint(a), CreativeCheck.Fingerprint(c));
        }
    }

    public sealed class CreativeFileTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ctl-ventures-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        [Test]
        public void ReadsASmallFile()
        {
            var p = Path.Combine(_dir, "a.png");
            File.WriteAllBytes(p, Kit.Png(10, 10));
            var bytes = CreativeFile.ReadBounded(p, 1000, out var problem);
            Assert.AreEqual(CreativeProblem.None, problem);
            Assert.AreEqual(64, bytes.Length);
        }

        [Test]
        public void RefusesAFileOverTheLimitWithoutReadingIt()
        {
            var p = Path.Combine(_dir, "big.png");
            File.WriteAllBytes(p, new byte[5000]);
            Assert.IsNull(CreativeFile.ReadBounded(p, 4000, out var problem));
            Assert.AreEqual(CreativeProblem.TooLarge, problem);
        }

        [Test]
        public void MissingAndEmptyFiles()
        {
            Assert.IsNull(CreativeFile.ReadBounded(Path.Combine(_dir, "nope"), 100, out var p1));
            Assert.AreEqual(CreativeProblem.Empty, p1);
            var e = Path.Combine(_dir, "empty");
            File.WriteAllBytes(e, new byte[0]);
            Assert.IsNull(CreativeFile.ReadBounded(e, 100, out var p2));
            Assert.AreEqual(CreativeProblem.Empty, p2);
        }

        [Test]
        public void Roots_AllowOnlyPathsInside()
        {
            var roots = new[] { _dir };
            Assert.IsTrue(CreativeFile.IsUnderAny(Path.Combine(_dir, "a.png"), roots));
            Assert.IsTrue(CreativeFile.IsUnderAny(Path.Combine(_dir, "sub", "a.png"), roots));
            Assert.IsFalse(CreativeFile.IsUnderAny(_dir + "-evil/a.png", roots), "a sibling that shares the prefix is outside");
            Assert.IsFalse(CreativeFile.IsUnderAny(Path.Combine(_dir, "..", "x.png"), roots), ".. escapes are resolved first");
            Assert.IsFalse(CreativeFile.IsUnderAny("/etc/passwd", roots));
            Assert.IsFalse(CreativeFile.IsUnderAny("", roots));
            Assert.IsFalse(CreativeFile.IsUnderAny(Path.Combine(_dir, "a.png"), new string[0]));
        }
    }
}
