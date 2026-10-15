using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-97 Ember Shard sources in the game: the first run end grants the one-time first-run bonus (a later run, or
    /// counters without a config, give none), a Rare chest's shard roll earns into the wallet and fires GemsFound (a miss
    /// or no config gives nothing), the end screen wallet line with the bonus, the menu shard pill on the real UXML, and the
    /// shipped GemRewards asset. The suite's WalletPrefsGuard keeps the editor's real wallets.
    /// </summary>
    public sealed class GemSourceTests
    {
        private const int TestRealm = 95;
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            RunState.NewRun(TestRealm);
            SoulWallet.Clear();
            GemWallet.ResetAll();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            RunState.NewRun();
            GemWallet.ResetAll();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [Test]
        public void FirstRunEnd_GrantsBonusOnce()
        {
            var config = Track(GemRewardConfig.Create(firstRun: 25));
            var counters = Track(new GameObject("Chamber")).AddComponent<RunCounters>();
            counters.SetupGems(config);
            counters.Restore(3, 400);

            Assert.That(counters.BankToWallet(), Is.EqualTo(400));
            Assert.That(counters.BonusGems, Is.EqualTo(25));
            Assert.That(GemWallet.Balance, Is.EqualTo(25));
            Assert.That(GemWallet.HasBonus(GemBonus.FirstRun), Is.True);
            Assert.That(counters.BankToWallet(), Is.EqualTo(0), "same run banks once");
            Assert.That(GemWallet.Balance, Is.EqualTo(25));

            RunState.NewRun(TestRealm);
            Assert.That(counters.BankToWallet(), Is.EqualTo(400), "the next run banks its souls");
            Assert.That(counters.BonusGems, Is.EqualTo(0), "but gets no second bonus");
            Assert.That(GemWallet.Balance, Is.EqualTo(25));
        }

        [Test]
        public void NoConfig_NoFirstRunBonus()
        {
            var counters = Track(new GameObject("Chamber")).AddComponent<RunCounters>();
            counters.Restore(0, 100);
            counters.BankToWallet();
            Assert.That(counters.BonusGems, Is.EqualTo(0));
            Assert.That(GemWallet.Balance, Is.EqualTo(0));
            Assert.That(GemWallet.HasBonus(GemBonus.FirstRun), Is.False, "a scene without the config does not use up the bonus");
        }

        [Test]
        public void RareChestRoll_EarnsShards_FiresGemsFound()
        {
            var config = Track(GemRewardConfig.Create(chestChance: 0.5f, chestMin: 3, chestMax: 8));
            var rewards = Track(new GameObject("Chests")).AddComponent<ChestRewards>();
            rewards.Setup(null, null, null, null, null, config);
            int events = 0, lastAmount = 0;
            Vector3 lastAt = default;
            rewards.GemsFound += (at, n) => { events++; lastAmount = n; lastAt = at; };

            var at0 = new Vector3(2f, 0f, 5f);
            Assert.That(rewards.GiveChestGems(at0, 0.9f, 0.5f), Is.EqualTo(0), "miss");
            Assert.That(events, Is.EqualTo(0));
            Assert.That(rewards.GiveChestGems(at0, 0.1f, 0f), Is.EqualTo(3));
            Assert.That(rewards.GiveChestGems(at0, 0.1f, 0.99f), Is.EqualTo(8));
            Assert.That(GemWallet.Balance, Is.EqualTo(11));
            Assert.That(rewards.GemsGiven, Is.EqualTo(11));
            Assert.That(events, Is.EqualTo(2));
            Assert.That(lastAmount, Is.EqualTo(8));
            Assert.That(lastAt, Is.EqualTo(at0));

            rewards.Setup(null, null, null, null, null);
            Assert.That(rewards.GiveChestGems(at0, 0f, 0f), Is.EqualTo(0), "no config, no shards");
            Assert.That(GemWallet.Balance, Is.EqualTo(11));
        }

        [Test]
        public void WalletLine_WithBonus()
        {
            Assert.That(PlayerHealthHud.WalletLine(1240, 12480, 0), Is.EqualTo("+1,240 BANKED · 12,480 SOULS HELD"));
            Assert.That(PlayerHealthHud.WalletLine(1240, 12480, 25),
                Is.EqualTo("+1,240 BANKED · 12,480 SOULS HELD\n+25 EMBER SHARDS · FIRST RUN BONUS"));
        }

#if UNITY_EDITOR
        [Test]
        public void MenuUxml_HasShardPill_ShippedAsset()
        {
            var menu = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            Assert.That(menu, Is.Not.Null);
            var root = menu.Instantiate();
            Assert.That(root.Q("lbl-wallet-souls"), Is.Not.Null);
            Assert.That(root.Q("lbl-wallet-gems"), Is.Not.Null);

            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GemRewardConfig>("Assets/_Project/Data/Economy/GemRewards.asset");
            Assert.That(asset, Is.Not.Null, "made by the scene builders");
            Assert.That(asset.WelcomeBonus, Is.GreaterThan(0));
            Assert.That(asset.FirstRunBonus, Is.GreaterThan(0));
            Assert.That(asset.RareChestChance, Is.InRange(0.01f, 1f));
            Assert.That(asset.RareChestMax, Is.GreaterThanOrEqualTo(asset.RareChestMin));
        }
#endif
    }
}
