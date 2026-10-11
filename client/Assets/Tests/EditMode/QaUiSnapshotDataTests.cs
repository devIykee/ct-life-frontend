#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace CTLife.Tests
{
    public class QaUiSnapshotDataTests
    {
        [Test]
        public void TextFilterKeepsTargetsButDropsInputsAndCredentials()
        {
            var type = Type.GetType("CTLife.UI.Dev.QaUiText, CTLife.UI");
            Assert.That(type, Is.Not.Null, "The snapshot text filter is missing");
            var method = type.GetMethod("Safe");
            string Safe(string text, bool input = false) => (string)method.Invoke(null, new object[] { text, input });
            Assert.That(Safe("ENTER THE CITY"), Is.EqualTo("ENTER THE CITY"));
            Assert.That(Safe("Username"), Is.EqualTo("Username"), "static field labels remain targetable");
            Assert.That(Safe("private-user", true), Is.EqualTo("[redacted]"));
            Assert.That(Safe("Bearer abcd-efgh.1234"), Is.EqualTo("[redacted]"));
            Assert.That(Safe("access_token=not-a-long-token"), Is.EqualTo("[redacted]"));
            Assert.That(Safe("Password: secret"), Is.EqualTo("[redacted]"));
            Assert.That(Safe("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abcdefghijklmnop"), Is.EqualTo("[redacted]"));
            Assert.That(Safe(new string('a', 64)), Is.EqualTo("[redacted]"));
            Assert.That(Safe(new string('x', 400) + " tail").Length, Is.LessThanOrEqualTo(256));
        }

        [Test]
        public void GeometryClipsBeforeMappingTopLeftScreenPixels()
        {
            var type = Type.GetType("CTLife.UI.Dev.QaUiGeometry, CTLife.UI");
            Assert.That(type, Is.Not.Null, "The geometry mapper is missing");
            var method = type.GetMethod("ClipAndMap");
            double[] Map(double[] rect, double[] clip) => (double[])method.Invoke(null,
                new object[] { rect, clip, new double[] { 10, 20, 100, 200 }, 1000, 2000 });
            Assert.That(Map(new double[] { 5, 10, 40, 40 }, new double[] { 20, 25, 50, 100 }),
                Is.EqualTo(new double[] { 100, 50, 250, 250 }));
            Assert.That(Map(new double[] { 0, 0, 5, 5 }, new double[] { 0, 0, 500, 500 }), Is.Null);
            Assert.That(Map(new double[] { 0, 0, double.NaN, 5 }, new double[] { 0, 0, 500, 500 }), Is.Null);
            Assert.That(Map(new double[] { 15, 25, 0, 5 }, new double[] { 0, 0, 500, 500 }), Is.Null);
            Assert.That(Map(new double[] { 15, 25, 5, 5 }, new double[] { 0, 0, 0, 0 }), Is.Null);
            Assert.That(method.Invoke(null, new object[] { new double[] { 0, 0, 1, 1 },
                new double[] { 0, 0, 1, 1 }, new double[] { 0, 0, 0, 0 }, 100, 100 }), Is.Null);
        }

        [Test]
        public void AtomicWriterReplacesCompleteJsonWithoutTemporaryResidue()
        {
            var type = Type.GetType("CTLife.UI.Dev.QaUiSnapshotFile, CTLife.UI");
            Assert.That(type, Is.Not.Null, "The atomic snapshot writer is missing");
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                var write = type.GetMethod("Write");
                write.Invoke(null, new object[] { path, "{\"sequence\":1}" });
                write.Invoke(null, new object[] { path, "{\"sequence\":2}" });
                Assert.That((int)JObject.Parse(System.IO.File.ReadAllText(path))["sequence"], Is.EqualTo(2));
                Assert.That(System.IO.File.Exists(path + ".tmp"), Is.False);
            }
            finally { System.IO.File.Delete(path); System.IO.File.Delete(path + ".tmp"); }
        }

        [Test]
        public void CredentialContextsAreRedactedEvenForShortNonTokenText()
        {
            var type = Type.GetType("CTLife.UI.Dev.QaUiText, CTLife.UI");
            var method = type.GetMethod("SensitiveName");
            Assert.That(method, Is.Not.Null, "Named credential displays need ancestor redaction");
            foreach (var name in new[] { "access-token", "password-value", "automationKey", "username", "api_key", "secret" })
                Assert.That((bool)method.Invoke(null, new object[] { name }), Is.True, name);
            foreach (var name in new[] { "token-market", "buy-token", "login-button", "city-home" })
                Assert.That((bool)method.Invoke(null, new object[] { name }), Is.False, name);
        }

        [Test]
        public void SnapshotSerializesVersionAndEmptyCollections()
        {
            var type = Type.GetType("CTLife.UI.Dev.QaUiSnapshot, CTLife.UI");
            Assert.That(type, Is.Not.Null, "The dev-only snapshot schema is missing");
            var value = Activator.CreateInstance(type);
            var json = (string)type.GetMethod("ToJson").Invoke(value, null);
            var parsed = JObject.Parse(json);
            Assert.That((int)parsed["schemaVersion"], Is.EqualTo(1));
            Assert.That((string)parsed["coordinateOrigin"], Is.EqualTo("top-left"));
            Assert.That(parsed["nodes"], Is.TypeOf<JArray>());
            Assert.That(parsed["audio"], Is.TypeOf<JArray>());
        }
    }
}
#endif
