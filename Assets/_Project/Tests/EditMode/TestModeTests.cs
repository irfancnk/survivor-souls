using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-144 Test Mode rules: off by default; on opens every Realm (<see cref="RunProgress.IsUnlocked"/>) without
    /// writing a completed flag, off restores the saved locks; runs do not record progress while on; the Souls grant tops
    /// the balance up to 999,999 on the first switch-on only (a bigger balance is kept, off keeps the Souls); the hidden
    /// row reveal (dev build at once, else the 7th title tap, or while on). The editor's real keys (Test Mode, grant flag,
    /// Souls wallet) are saved before each test and put back after it; test Realms 91-93 only.
    /// </summary>
    public sealed class TestModeTests
    {
        private const int First = 91; // 90 is the "previous" Realm of 91
        private static readonly string[] SavedKeys =
        {
            GameSettings.KeyTestMode, TestMode.KeyGranted, SoulWallet.KeyBalance, SoulWallet.KeyCheck,
        };
        private readonly int?[] _saved = new int?[SavedKeys.Length];

        [SetUp]
        public void SetUp()
        {
            for (int i = 0; i < SavedKeys.Length; i++)
                _saved[i] = PlayerPrefs.HasKey(SavedKeys[i]) ? PlayerPrefs.GetInt(SavedKeys[i]) : (int?)null;
            TestMode.ResetAll();
            SoulWallet.Clear();
            for (int r = First - 1; r <= First + 2; r++) RunProgress.Clear(r);
        }

        [TearDown]
        public void TearDown()
        {
            for (int r = First - 1; r <= First + 2; r++) RunProgress.Clear(r);
            for (int i = 0; i < SavedKeys.Length; i++)
            {
                if (_saved[i].HasValue) PlayerPrefs.SetInt(SavedKeys[i], _saved[i].Value);
                else PlayerPrefs.DeleteKey(SavedKeys[i]);
            }
            PlayerPrefs.Save();
        }

        [Test]
        public void OffByDefault()
        {
            Assert.That(TestMode.IsOn, Is.False);
            Assert.That(GameSettings.TestMode, Is.False);
            Assert.That(TestMode.SoulsGranted, Is.False);
            Assert.That(RunProgress.Recording, Is.True);
        }

        [Test]
        public void IsUnlocked_OnOpensEveryRealm_OffRestoresSavedLocks()
        {
            RunProgress.MarkCompleted(First - 1); // real save: 91 open, 92 and 93 locked
            Assert.That(RunProgress.IsUnlocked(First), Is.True);
            Assert.That(RunProgress.IsUnlocked(First + 1), Is.False);
            Assert.That(RunProgress.StateOf(First + 2), Is.EqualTo(RealmState.Locked));

            TestMode.SetOn(true);
            Assert.That(TestMode.IsOn, Is.True);
            for (int r = First; r <= First + 2; r++)
            {
                Assert.That(RunProgress.IsUnlocked(r), Is.True, $"Realm {r} open in Test Mode");
                Assert.That(RunProgress.StateOf(r), Is.EqualTo(RealmState.Unlocked), $"Realm {r} not marked completed");
                Assert.That(RunProgress.IsCompleted(r), Is.False, "no fake completed flag written");
            }
            Assert.That(RunProgress.Recording, Is.False, "Test Mode runs do not write progress");

            TestMode.SetOn(false);
            Assert.That(RunProgress.IsUnlocked(First), Is.True);
            Assert.That(RunProgress.IsUnlocked(First + 1), Is.False, "locks come back from the real save");
            Assert.That(RunProgress.StateOf(First + 2), Is.EqualTo(RealmState.Locked));
            Assert.That(RunProgress.Recording, Is.True);
        }

        [Test]
        public void SoulsGrant_TopsUpTo999999_OnlyOnce_OffKeepsSouls()
        {
            SoulWallet.Earn(100);
            Assert.That(TestMode.SetOn(true), Is.EqualTo(TestMode.SoulGrant - 100));
            Assert.That(SoulWallet.Balance, Is.EqualTo(999_999));
            Assert.That(TestMode.SoulsGranted, Is.True);

            Assert.That(TestMode.SetOn(false), Is.EqualTo(0));
            Assert.That(SoulWallet.Balance, Is.EqualTo(999_999), "granted Souls stay when Test Mode goes off");

            SoulWallet.TrySpend(500_000);
            Assert.That(TestMode.SetOn(true), Is.EqualTo(0), "a second switch-on adds nothing");
            Assert.That(SoulWallet.Balance, Is.EqualTo(499_999));
            Assert.That(TestMode.SetOn(true), Is.EqualTo(0), "on while on adds nothing");
        }

        [Test]
        public void SoulsGrant_BigBalanceKept_StillCountsAsGranted()
        {
            SoulWallet.Earn(2_000_000);
            Assert.That(TestMode.SetOn(true), Is.EqualTo(0));
            Assert.That(SoulWallet.Balance, Is.EqualTo(2_000_000), "never lowers a balance");
            Assert.That(TestMode.SoulsGranted, Is.True);
            SoulWallet.TrySpend(1_999_000);
            TestMode.SetOn(false);
            Assert.That(TestMode.SetOn(true), Is.EqualTo(0), "grant used up, even though it added nothing");
        }

        [Test]
        public void SwitchOff_FirstDoesNotGrant()
        {
            Assert.That(TestMode.SetOn(false), Is.EqualTo(0));
            Assert.That(TestMode.SoulsGranted, Is.False, "only a switch-on uses the grant");
            Assert.That(SoulWallet.Balance, Is.EqualTo(0));
        }

        [Test]
        public void Reveal_DevBuildAtOnce_OtherBuildsOnSeventhTap_OrWhileOn()
        {
            var dev = new TestModeReveal(devBuild: true);
            Assert.That(dev.Visible, Is.True);
            Assert.That(dev.Tap(), Is.False, "nothing to reveal");

            var store = new TestModeReveal(devBuild: false);
            Assert.That(store.Visible, Is.False);
            for (int i = 1; i < TestMode.RevealTaps; i++)
            {
                Assert.That(store.Tap(), Is.False, $"tap {i}");
                Assert.That(store.Visible, Is.False, $"hidden after {i} taps");
            }
            Assert.That(store.Tap(), Is.True, "7th tap reveals");
            Assert.That(store.Visible, Is.True);
            Assert.That(store.Tap(), Is.False, "already shown");

            var on = new TestModeReveal(devBuild: false);
            TestMode.SetOn(true);
            Assert.That(on.Visible, Is.True, "always shown while on, so it can be turned off");
        }
    }
}
