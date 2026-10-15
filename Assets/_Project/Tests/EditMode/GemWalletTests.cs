using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-97 Ember Shards wallet: fresh 0, earn / spend rules, saved with its own checksum (a Souls value + checksum pair
    /// copied into the gem keys fails), hand-edited balance reset with TamperDetected, each one-time bonus fires once and
    /// on its own, a zero amount neither grants nor marks, a tampered bonus set counts as all granted, ResetAll, the Rare
    /// chest roll (miss, min, max, no chance) and the Souls checksum still equal to the KAN-96 formula (old saves valid).
    /// The editor's real wallet keys are saved before each test and put back after it.
    /// </summary>
    public sealed class GemWalletTests
    {
        private readonly Dictionary<string, int> _saved = new Dictionary<string, int>();

        private static IEnumerable<string> AllKeys()
        {
            foreach (var k in GemWallet.Keys) yield return k;
            yield return SoulWallet.KeyBalance;
            yield return SoulWallet.KeyCheck;
        }

        [SetUp]
        public void SetUp()
        {
            _saved.Clear();
            foreach (var k in AllKeys())
            {
                if (PlayerPrefs.HasKey(k)) _saved[k] = PlayerPrefs.GetInt(k);
                PlayerPrefs.DeleteKey(k);
            }
            GemWallet.Clear(); // resets TamperDetected
            foreach (var k in GemWallet.Keys) PlayerPrefs.DeleteKey(k);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var k in AllKeys())
            {
                if (_saved.TryGetValue(k, out int v)) PlayerPrefs.SetInt(k, v);
                else PlayerPrefs.DeleteKey(k);
            }
            PlayerPrefs.Save();
        }

        [Test]
        public void Fresh_IsZero_NoBonus()
        {
            Assert.That(GemWallet.Balance, Is.EqualTo(0));
            Assert.That(GemWallet.Granted, Is.EqualTo(GemBonus.None));
            Assert.That(GemWallet.TamperDetected, Is.False);
            Assert.That(GemWallet.CanAfford(1), Is.False);
        }

        [Test]
        public void EarnSpend_Rules_FireChanged()
        {
            int last = -1, calls = 0;
            void OnChanged(int b) { last = b; calls++; }
            GemWallet.Changed += OnChanged;
            try
            {
                Assert.That(GemWallet.Earn(120), Is.EqualTo(120));
                Assert.That(GemWallet.Earn(0), Is.EqualTo(120));
                Assert.That(GemWallet.Earn(-4), Is.EqualTo(120));
                Assert.That(GemWallet.TrySpend(121), Is.False);
                Assert.That(GemWallet.TrySpend(-1), Is.False);
                Assert.That(GemWallet.TrySpend(20), Is.True);
                Assert.That(GemWallet.Balance, Is.EqualTo(100));
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(last, Is.EqualTo(100));
                GemWallet.Earn(int.MaxValue);
                Assert.That(GemWallet.Balance, Is.EqualTo(int.MaxValue));
            }
            finally
            {
                GemWallet.Changed -= OnChanged;
            }
        }

        [Test]
        public void Saved_WithOwnChecksum_SoulPairCopiedFails()
        {
            GemWallet.Earn(64);
            Assert.That(PlayerPrefs.GetInt(GemWallet.KeyBalance), Is.EqualTo(64));
            Assert.That(PlayerPrefs.GetInt(GemWallet.KeyCheck), Is.EqualTo(GemWallet.Checksum(64)));
            Assert.That(GemWallet.Balance, Is.EqualTo(64), "a restart reads the saved value");
            Assert.That(GemWallet.Checksum(500), Is.Not.EqualTo(SoulWallet.Checksum(500)), "own salt");

            // Copy a valid Souls pair into the gem keys: the checksum belongs to another salt, so it is a tamper.
            PlayerPrefs.SetInt(GemWallet.KeyBalance, 500);
            PlayerPrefs.SetInt(GemWallet.KeyCheck, SoulWallet.Checksum(500));
            ExpectWarning();
            Assert.That(GemWallet.Balance, Is.EqualTo(0));
            Assert.That(GemWallet.TamperDetected, Is.True);
        }

        [Test]
        public void HandEditedBalance_ResetToZero()
        {
            GemWallet.Earn(10);
            PlayerPrefs.SetInt(GemWallet.KeyBalance, 99999);
            ExpectWarning();
            Assert.That(GemWallet.Balance, Is.EqualTo(0));
            Assert.That(GemWallet.TamperDetected, Is.True);
            Assert.That(PlayerPrefs.GetInt(GemWallet.KeyCheck), Is.EqualTo(GemWallet.Checksum(0)), "rewritten as a valid 0");
            Assert.That(GemWallet.Earn(3), Is.EqualTo(3));
            GemWallet.Clear();
            Assert.That(GemWallet.TamperDetected, Is.False);
        }

        [Test]
        public void EachBonus_FiresOnce_OnItsOwn()
        {
            var events = new List<(GemBonus, int, int)>();
            void OnBonus(GemBonus b, int amount, int balance) => events.Add((b, amount, balance));
            GemWallet.BonusGranted += OnBonus;
            try
            {
                Assert.That(GemWallet.TryGrantBonus(GemBonus.Welcome, 50), Is.True);
                Assert.That(GemWallet.TryGrantBonus(GemBonus.Welcome, 50), Is.False, "welcome only once");
                Assert.That(GemWallet.Balance, Is.EqualTo(50));
                Assert.That(GemWallet.HasBonus(GemBonus.Welcome), Is.True);
                Assert.That(GemWallet.HasBonus(GemBonus.FirstRun), Is.False, "bonuses are independent");

                Assert.That(GemWallet.TryGrantBonus(GemBonus.FirstRun, 25), Is.True);
                Assert.That(GemWallet.TryGrantBonus(GemBonus.FirstRun, 25), Is.False, "first run only once");
                Assert.That(GemWallet.Balance, Is.EqualTo(75));
                Assert.That(GemWallet.Granted, Is.EqualTo(GemBonus.Welcome | GemBonus.FirstRun));

                Assert.That(events, Is.EqualTo(new List<(GemBonus, int, int)> { (GemBonus.Welcome, 50, 50), (GemBonus.FirstRun, 25, 75) }));

                // Spending does not bring a bonus back; Clear keeps them granted.
                GemWallet.TrySpend(75);
                GemWallet.Clear();
                Assert.That(GemWallet.TryGrantBonus(GemBonus.Welcome, 50), Is.False);
                Assert.That(GemWallet.Balance, Is.EqualTo(0));
            }
            finally
            {
                GemWallet.BonusGranted -= OnBonus;
            }
        }

        [Test]
        public void ZeroAmountOrNone_NotGranted_NotMarked()
        {
            Assert.That(GemWallet.TryGrantBonus(GemBonus.Welcome, 0), Is.False);
            Assert.That(GemWallet.TryGrantBonus(GemBonus.None, 10), Is.False);
            Assert.That(GemWallet.TryGrantBonus((GemBonus)64, 10), Is.False, "unknown bonus bit");
            Assert.That(GemWallet.HasBonus(GemBonus.Welcome), Is.False);
            Assert.That(GemWallet.TryGrantBonus(GemBonus.Welcome, 40), Is.True, "a later config with an amount still grants");
        }

        [Test]
        public void TamperedBonusSet_CountsAsAllGranted()
        {
            PlayerPrefs.SetInt(GemWallet.KeyBonus, 0); // edited back to "nothing granted" without the checksum
            PlayerPrefs.SetInt(GemWallet.KeyBonusCheck, 12345);
            ExpectWarning();
            Assert.That(GemWallet.TryGrantBonus(GemBonus.Welcome, 50), Is.False);
            Assert.That(GemWallet.TryGrantBonus(GemBonus.FirstRun, 25), Is.False);
            Assert.That(GemWallet.Balance, Is.EqualTo(0));
            Assert.That(GemWallet.TamperDetected, Is.True);
        }

        [Test]
        public void ResetAll_GrantsAgain()
        {
            GemWallet.TryGrantBonus(GemBonus.Welcome, 50);
            GemWallet.ResetAll();
            Assert.That(GemWallet.Balance, Is.EqualTo(0));
            Assert.That(GemWallet.Granted, Is.EqualTo(GemBonus.None));
            Assert.That(GemWallet.TryGrantBonus(GemBonus.Welcome, 50), Is.True);
        }

        [Test]
        public void RareChestRoll_ChanceAndRange()
        {
            var config = GemRewardConfig.Create(welcome: 50, firstRun: 25, chestChance: 0.35f, chestMin: 3, chestMax: 8);
            try
            {
                Assert.That(config.RollRareChest(0.35f, 0.5f), Is.EqualTo(0), "roll at the chance misses");
                Assert.That(config.RollRareChest(0.9f, 0.5f), Is.EqualTo(0));
                Assert.That(config.RollRareChest(0f, 0f), Is.EqualTo(3), "lowest amount roll = min");
                Assert.That(config.RollRareChest(0.34f, 0.999f), Is.EqualTo(8), "highest amount roll = max");
                Assert.That(config.RollRareChest(0.1f, 1f), Is.EqualTo(8), "a roll of exactly 1 stays in range");
                for (float a = 0f; a < 1f; a += 0.01f)
                    Assert.That(config.RollRareChest(0f, a), Is.InRange(3, 8));
                Assert.That(config.WelcomeBonus, Is.EqualTo(50));
                Assert.That(config.FirstRunBonus, Is.EqualTo(25));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }

            var none = GemRewardConfig.Create(chestChance: 0f);
            try
            {
                Assert.That(none.RollRareChest(0f, 0.5f), Is.EqualTo(0), "no chance = never");
            }
            finally
            {
                Object.DestroyImmediate(none);
            }
        }

        [Test]
        public void SoulChecksum_SameAsKan96Formula()
        {
            foreach (int v in new[] { 0, 1, 1240, 12480, int.MaxValue })
                Assert.That(SoulWallet.Checksum(v), Is.EqualTo(Kan96Checksum(v)), $"value {v}");
        }

        /// <summary>The KAN-96 SoulWallet checksum, copied as it shipped, so saved Souls stay valid after the refactor.</summary>
        private static int Kan96Checksum(int value)
        {
            unchecked
            {
                const uint salt = 0x5EED50F1u;
                uint hash = 2166136261u;
                uint v = (uint)value ^ salt;
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (v >> (i * 8)) & 0xFFu;
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        private static void ExpectWarning() =>
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[GemWallet\].*checksum"));
    }
}
