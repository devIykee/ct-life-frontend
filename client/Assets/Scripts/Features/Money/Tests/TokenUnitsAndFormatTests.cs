using NUnit.Framework;

namespace CTLife.Economy.Tests
{
    public sealed class TokenUnitsTests
    {
        [TestCase("10000000", 6, "10")]
        [TestCase("1500000", 6, "1.5")]
        [TestCase("1", 6, "0.000001")]
        [TestCase("0", 9, "0")]
        [TestCase("1000000000", 9, "1")]
        [TestCase("55000000000", 11, "0.55")]
        [TestCase("120000000000", 11, "1.2")]
        [TestCase("1234567000000", 6, "1,234,567")]
        [TestCase("-2500000", 6, "-2.5")]
        [TestCase("150000", 5, "1.5")]
        [TestCase("123", 0, "123")]
        public void Format_BaseUnits(string units, int decimals, string expected) =>
            Assert.AreEqual(expected, TokenUnits.Format(units, decimals));

        [TestCase("123456789", 9, 4, "0.1234")]
        [TestCase("1", 9, 4, "<0.0001")]
        [TestCase("999999999", 9, 2, "0.99")]
        [TestCase("55000000000", 11, 4, "0.55")]
        public void Format_TruncatesNeverRoundsUp(string units, int decimals, int maxFraction, string expected) =>
            Assert.AreEqual(expected, TokenUnits.Format(units, decimals, maxFraction));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0.000010")]
        [TestCase("1e9")]
        [TestCase(" 1")]
        [TestCase("-")]
        [TestCase("12a")]
        public void Format_RejectsNonBaseUnits(string units) => Assert.IsNull(TokenUnits.Format(units, 6));

        [Test]
        public void Format_HandlesValuesBeyondInt64()
        {
            Assert.AreEqual("300,000,000,000", TokenUnits.Format("30000000000000000000000", 11));
        }

        [TestCase("1.25", 11, "125000000000")]
        [TestCase("0.5", 11, "50000000000")]
        [TestCase("1", 6, "1000000")]
        [TestCase(".5", 6, "500000")]
        [TestCase("  2.0 ", 9, "2000000000")]
        [TestCase("0.00000000001", 11, "1")]
        public void TryParse_Accepts(string input, int decimals, string expected)
        {
            Assert.IsTrue(TokenUnits.TryParse(input, decimals, out var units));
            Assert.AreEqual(expected, units);
        }

        [TestCase("0")]
        [TestCase("0.0")]
        [TestCase("-1")]
        [TestCase("1.2.3")]
        [TestCase("1,5")]
        [TestCase("1e3")]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("0.000000000001")]
        [TestCase(null)]
        public void TryParse_Rejects(string input) => Assert.IsFalse(TokenUnits.TryParse(input, 11, out _));

        [Test]
        public void Compare_And_Fraction()
        {
            Assert.AreEqual(-1, TokenUnits.Compare("999", "1000"));
            Assert.AreEqual(0, TokenUnits.Compare("1000", "1000"));
            Assert.AreEqual(1, TokenUnits.Compare("30000000000000000000000", "29999999999999999999999"));
            Assert.IsNull(TokenUnits.Compare("1.0", "1"));
            Assert.AreEqual("250", TokenUnits.Fraction("1000", 25));
            Assert.AreEqual("330", TokenUnits.Fraction("1000", 33));
            Assert.AreEqual("499", TokenUnits.Fraction("999", 50), "floor, never more than held");
            Assert.AreEqual("1000", TokenUnits.Fraction("1000", 100));
            Assert.IsNull(TokenUnits.Fraction("1000", 0));
            Assert.IsNull(TokenUnits.Fraction("0", 50));
            Assert.IsTrue(TokenUnits.IsPositive("0001"));
            Assert.IsFalse(TokenUnits.IsPositive("000"));
            Assert.IsFalse(TokenUnits.IsPositive("-5"));
        }
    }

    public sealed class MoneyFormatTests
    {
        [Test]
        public void Cash_And_SignedCash()
        {
            Assert.AreEqual("$1,234.56", MoneyFormat.Cash(123456));
            Assert.AreEqual("+$1.00", MoneyFormat.SignedCash(100));
            Assert.AreEqual("-$1.00", MoneyFormat.SignedCash(-100));
            Assert.AreEqual("$0.00", MoneyFormat.SignedCash(0));
        }

        [Test]
        public void Micros_And_Bps()
        {
            Assert.AreEqual("$0.0150", MoneyFormat.Micros(15000));
            Assert.AreEqual("$0.001000", MoneyFormat.Micros(1000));
            Assert.AreEqual("$1.50", MoneyFormat.Micros(1500000));
            Assert.AreEqual("-3.10%", MoneyFormat.Bps(-310));
            Assert.AreEqual("+1.50%", MoneyFormat.Bps(150));
            Assert.AreEqual("2.00%", MoneyFormat.Bps(200, false));
        }

        [Test]
        public void Token_ShowsRawWhenNotBaseUnits()
        {
            Assert.AreEqual("10 USDC", MoneyFormat.Token("10000000", 6, "USDC"));
            Assert.AreEqual("0.000010 (raw) USDC", MoneyFormat.Token("0.000010", 6, "USDC"));
            Assert.AreEqual("2.5", MoneyFormat.CoinUnits("2500000"));
        }

        [TestCase(0, "0:00")]
        [TestCase(-5, "0:00")]
        [TestCase(0.2, "0:01")]
        [TestCase(65, "1:05")]
        [TestCase(3725, "1:02:05")]
        public void Countdown(double seconds, string expected) => Assert.AreEqual(expected, MoneyFormat.Countdown(seconds));

        [Test]
        public void Ago_Short_Ratio()
        {
            Assert.AreEqual("now", MoneyFormat.Ago(2));
            Assert.AreEqual("12s", MoneyFormat.Ago(12));
            Assert.AreEqual("3m", MoneyFormat.Ago(190));
            Assert.AreEqual("2h", MoneyFormat.Ago(7300));
            Assert.AreEqual("Dgw3…ABCD", MoneyFormat.Short("Dgw3c4GbQxxxxABCD"));
            Assert.AreEqual("62%", MoneyFormat.Ratio(0.62));
            Assert.AreEqual("—", MoneyFormat.Ratio(double.NaN));
        }
    }
}
