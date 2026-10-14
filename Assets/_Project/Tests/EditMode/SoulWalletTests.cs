using NUnit.Framework;
using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-96 Souls wallet: fresh balance 0, earn (positive only, stops at int.MaxValue), spend (only when covered,
    /// negative refused, zero free), CanAfford, Changed with the new balance, saved in PlayerPrefs with a matching checksum
    /// (a "restart" reads the same balance), and a hand-edited balance is reset to 0 with TamperDetected.
    /// The editor's real wallet keys are saved before each test and put back after it.
    /// </summary>
    public sealed class SoulWalletTests
    {
        private bool _hadBalance, _hadCheck;
        private int _balance, _check;

        [SetUp]
        public void SetUp()
        {
            _hadBalance = PlayerPrefs.HasKey(SoulWallet.KeyBalance);
            _hadCheck = PlayerPrefs.HasKey(SoulWallet.KeyCheck);
            _balance = PlayerPrefs.GetInt(SoulWallet.KeyBalance, 0);
            _check = PlayerPrefs.GetInt(SoulWallet.KeyCheck, 0);
            PlayerPrefs.DeleteKey(SoulWallet.KeyBalance);
            PlayerPrefs.DeleteKey(SoulWallet.KeyCheck);
            SoulWallet.Clear(); // resets TamperDetected
            PlayerPrefs.DeleteKey(SoulWallet.KeyBalance);
            PlayerPrefs.DeleteKey(SoulWallet.KeyCheck);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadBalance) PlayerPrefs.SetInt(SoulWallet.KeyBalance, _balance);
            else PlayerPrefs.DeleteKey(SoulWallet.KeyBalance);
            if (_hadCheck) PlayerPrefs.SetInt(SoulWallet.KeyCheck, _check);
            else PlayerPrefs.DeleteKey(SoulWallet.KeyCheck);
            PlayerPrefs.Save();
        }

        [Test]
        public void Fresh_IsZero_NotTampered()
        {
            Assert.That(SoulWallet.Balance, Is.EqualTo(0));
            Assert.That(SoulWallet.TamperDetected, Is.False);
            Assert.That(SoulWallet.CanAfford(0), Is.True);
            Assert.That(SoulWallet.CanAfford(1), Is.False);
        }

        [Test]
        public void Earn_AddsPositiveOnly_FiresChanged()
        {
            int last = -1, calls = 0;
            void OnChanged(int b) { last = b; calls++; }
            SoulWallet.Changed += OnChanged;
            try
            {
                Assert.That(SoulWallet.Earn(1240), Is.EqualTo(1240));
                Assert.That(SoulWallet.Earn(60), Is.EqualTo(1300));
                Assert.That(SoulWallet.Earn(0), Is.EqualTo(1300));
                Assert.That(SoulWallet.Earn(-50), Is.EqualTo(1300));
                Assert.That(SoulWallet.Balance, Is.EqualTo(1300));
                Assert.That(calls, Is.EqualTo(2), "no event for an ignored amount");
                Assert.That(last, Is.EqualTo(1300));
            }
            finally
            {
                SoulWallet.Changed -= OnChanged;
            }
        }

        [Test]
        public void Earn_StopsAtIntMax()
        {
            SoulWallet.Earn(int.MaxValue - 10);
            Assert.That(SoulWallet.Earn(500), Is.EqualTo(int.MaxValue));
            Assert.That(SoulWallet.Balance, Is.EqualTo(int.MaxValue));
            Assert.That(SoulWallet.TamperDetected, Is.False);
        }

        [Test]
        public void TrySpend_OnlyWhenCovered()
        {
            SoulWallet.Earn(500);
            Assert.That(SoulWallet.CanAfford(500), Is.True);
            Assert.That(SoulWallet.CanAfford(501), Is.False);
            Assert.That(SoulWallet.TrySpend(501), Is.False);
            Assert.That(SoulWallet.Balance, Is.EqualTo(500), "a refused spend changes nothing");
            Assert.That(SoulWallet.TrySpend(-5), Is.False, "negative cost refused");
            Assert.That(SoulWallet.TrySpend(0), Is.True);
            Assert.That(SoulWallet.TrySpend(200), Is.True);
            Assert.That(SoulWallet.Balance, Is.EqualTo(300));
            Assert.That(SoulWallet.TrySpend(300), Is.True);
            Assert.That(SoulWallet.Balance, Is.EqualTo(0));
        }

        [Test]
        public void Saved_InPlayerPrefs_WithChecksum_ReadBackAfterRestart()
        {
            SoulWallet.Earn(777);
            Assert.That(PlayerPrefs.GetInt(SoulWallet.KeyBalance), Is.EqualTo(777));
            Assert.That(PlayerPrefs.GetInt(SoulWallet.KeyCheck), Is.EqualTo(SoulWallet.Checksum(777)));
            // A restart reads the same keys: the static class keeps no cache, so a fresh read is the saved value.
            Assert.That(SoulWallet.Balance, Is.EqualTo(777));
            Assert.That(SoulWallet.TamperDetected, Is.False);
        }

        [Test]
        public void HandEditedBalance_ResetToZero_TamperDetected()
        {
            SoulWallet.Earn(100);
            PlayerPrefs.SetInt(SoulWallet.KeyBalance, 999999); // edited without the checksum
            LogAssertExpectWarning();
            Assert.That(SoulWallet.Balance, Is.EqualTo(0));
            Assert.That(SoulWallet.TamperDetected, Is.True);
            Assert.That(PlayerPrefs.GetInt(SoulWallet.KeyCheck), Is.EqualTo(SoulWallet.Checksum(0)), "rewritten as a valid 0");
            Assert.That(SoulWallet.Earn(5), Is.EqualTo(5), "the wallet works again after the reset");
            SoulWallet.Clear();
            Assert.That(SoulWallet.TamperDetected, Is.False);
        }

        [Test]
        public void NegativeSavedBalance_IsTamper()
        {
            PlayerPrefs.SetInt(SoulWallet.KeyBalance, -5);
            PlayerPrefs.SetInt(SoulWallet.KeyCheck, SoulWallet.Checksum(-5));
            LogAssertExpectWarning();
            Assert.That(SoulWallet.Balance, Is.EqualTo(0));
            Assert.That(SoulWallet.TamperDetected, Is.True);
        }

        [Test]
        public void Checksum_Deterministic_DiffersPerValue()
        {
            Assert.That(SoulWallet.Checksum(1240), Is.EqualTo(SoulWallet.Checksum(1240)));
            Assert.That(SoulWallet.Checksum(0), Is.Not.EqualTo(SoulWallet.Checksum(1)));
            Assert.That(SoulWallet.Checksum(1000), Is.Not.EqualTo(SoulWallet.Checksum(1001)));
        }

        private static void LogAssertExpectWarning() =>
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[SoulWallet\].*checksum"));
    }
}
