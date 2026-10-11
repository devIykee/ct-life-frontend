using CTLife.Core;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class LaunchArgsTests
    {
        private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef";

        [Test]
        public void AutomationKeyMustMatchExactly()
        {
            Assert.IsTrue(LaunchArgs.KeyMatches(Key, Key));
            Assert.IsFalse(LaunchArgs.KeyMatches(Key, Key.Substring(0, 47) + "0"));
            Assert.IsFalse(LaunchArgs.KeyMatches(Key, Key + "0"));
            Assert.IsFalse(LaunchArgs.KeyMatches(Key, null), "no key in the intent");
            Assert.IsFalse(LaunchArgs.KeyMatches(null, Key), "no key file on the device");
            Assert.IsFalse(LaunchArgs.KeyMatches("short", "short"), "keys under 32 characters are refused");
            Assert.IsFalse(LaunchArgs.KeyMatches("", ""));
        }
    }
}
