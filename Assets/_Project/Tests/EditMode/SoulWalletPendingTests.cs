using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SurvivorSouls.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-101 pending souls: a saved amount reads back with its own checksum, a new save replaces the last one, zero or
    /// negative deletes it, a restore adds it to the balance once (a second restore adds nothing), a hand-edited pending
    /// amount is dropped, <see cref="SoulWallet.Clear"/> deletes it, and the pending checksum differs from the balance one
    /// (a balance pair copied to the pending keys fails). The editor's real wallet keys are saved and put back.
    /// </summary>
    public sealed class SoulWalletPendingTests
    {
        private static readonly string[] Keys =
        {
            SoulWallet.KeyBalance, SoulWallet.KeyCheck, SoulWallet.KeyPending, SoulWallet.KeyPendingCheck,
        };

        private readonly Dictionary<string, int> _saved = new Dictionary<string, int>();

        [SetUp]
        public void SetUp()
        {
            _saved.Clear();
            foreach (var k in Keys)
                if (PlayerPrefs.HasKey(k)) _saved[k] = PlayerPrefs.GetInt(k);
            SoulWallet.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var k in Keys)
            {
                if (_saved.TryGetValue(k, out int v)) PlayerPrefs.SetInt(k, v);
                else PlayerPrefs.DeleteKey(k);
            }
            PlayerPrefs.Save();
        }

        [Test]
        public void Fresh_NoPending_RestoreAddsNothing()
        {
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0));
            Assert.That(SoulWallet.RestorePending(), Is.EqualTo(0));
            Assert.That(SoulWallet.Balance, Is.EqualTo(0));
            Assert.That(PlayerPrefs.HasKey(SoulWallet.KeyPending), Is.False);
        }

        [Test]
        public void Save_ReadsBack_NewSaveReplaces_ZeroDeletes()
        {
            SoulWallet.SavePending(340);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(340));
            Assert.That(PlayerPrefs.HasKey(SoulWallet.KeyPendingCheck), Is.True);
            SoulWallet.SavePending(910);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(910), "the latest background save wins (not added)");
            Assert.That(SoulWallet.Balance, Is.EqualTo(0), "saving never touches the balance");
            SoulWallet.SavePending(0);
            Assert.That(PlayerPrefs.HasKey(SoulWallet.KeyPending), Is.False);
            Assert.That(PlayerPrefs.HasKey(SoulWallet.KeyPendingCheck), Is.False);
            SoulWallet.SavePending(50);
            SoulWallet.SavePending(-5);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0));
        }

        [Test]
        public void Restore_AddsOnce_FiresChanged()
        {
            SoulWallet.Earn(1000);
            SoulWallet.SavePending(240);
            int last = -1;
            void OnChanged(int b) => last = b;
            SoulWallet.Changed += OnChanged;
            try
            {
                LogAssert.Expect(LogType.Log, new Regex(@"\[SoulWallet\] Restored 240 pending souls"));
                Assert.That(SoulWallet.RestorePending(), Is.EqualTo(240));
                Assert.That(SoulWallet.Balance, Is.EqualTo(1240));
                Assert.That(last, Is.EqualTo(1240));
                Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0));
                Assert.That(SoulWallet.RestorePending(), Is.EqualTo(0), "a second start adds nothing");
                Assert.That(SoulWallet.Balance, Is.EqualTo(1240));
            }
            finally
            {
                SoulWallet.Changed -= OnChanged;
            }
        }

        [Test]
        public void HandEditedPending_Dropped()
        {
            SoulWallet.SavePending(100);
            PlayerPrefs.SetInt(SoulWallet.KeyPending, 999999); // edited without the checksum
            LogAssert.Expect(LogType.Warning, new Regex(@"\[SoulWallet\] Pending souls .*checksum"));
            Assert.That(SoulWallet.RestorePending(), Is.EqualTo(0));
            Assert.That(SoulWallet.Balance, Is.EqualTo(0));
            Assert.That(PlayerPrefs.HasKey(SoulWallet.KeyPending), Is.False);
        }

        [Test]
        public void BalancePairCopiedToPendingKeys_FailsCheck()
        {
            PlayerPrefs.SetInt(SoulWallet.KeyPending, 5000);
            PlayerPrefs.SetInt(SoulWallet.KeyPendingCheck, SoulWallet.Checksum(5000)); // the balance salt, not the pending one
            LogAssert.Expect(LogType.Warning, new Regex(@"\[SoulWallet\] Pending souls .*checksum"));
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0));
        }

        [Test]
        public void Clear_DeletesPending()
        {
            SoulWallet.SavePending(70);
            SoulWallet.Clear();
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0));
            Assert.That(PlayerPrefs.HasKey(SoulWallet.KeyPending), Is.False);
        }
    }
}
