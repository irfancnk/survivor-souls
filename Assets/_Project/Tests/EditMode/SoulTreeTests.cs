using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Meta;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-98 soul tree: cost curve (rounded to 5, 0 outside 1..max), effect text, buy rules in order (unknown node, max
    /// level, prerequisites, wallet), a buy spends exactly the cost and saves the level, persistence with a per-node
    /// checksum (hand edit = 0 + TamperDetected, a pair copied to another node fails, levels above max clamp), applying
    /// the levels to <see cref="PlayerStats"/> as permanent sources (baseline read without in-run skills), refund reset,
    /// and the shipped tree asset (ids, prerequisites, total cost; the cost table is logged for the ticket).
    /// Test nodes use ids "test-*"; the editor's real wallet keys are saved before each test and put back after it.
    /// </summary>
    public sealed class SoulTreeTests
    {
        private const string TreePath = "Assets/_Project/Data/Economy/SoulTree.asset";
        private readonly List<Object> _objects = new List<Object>();
        private readonly Dictionary<string, int> _savedPrefs = new Dictionary<string, int>();
        private SoulTreeNode _might, _vigor, _draw;
        private SoulTree _tree;

        private static readonly string[] WalletKeys = { SoulWallet.KeyBalance, SoulWallet.KeyCheck };

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            _savedPrefs.Clear();
            foreach (var k in WalletKeys)
                if (PlayerPrefs.HasKey(k)) _savedPrefs[k] = PlayerPrefs.GetInt(k);
            SoulWallet.Clear();

            _might = Track(SoulTreeNode.Create("test-might", "Kindled Might", 0, 5, 250, 1.45f,
                new[] { new StatModifier(StatId.Damage, ModifierKind.Percent, 0.04f) }, "bolt damage"));
            _vigor = Track(SoulTreeNode.Create("test-vigor", "Ashen Vigor", 0, 2, 100, 2f,
                new[] { new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 40f) }, "max health"));
            _draw = Track(SoulTreeNode.Create("test-draw", "Quickened Draw", 1, 3, 400, 1.5f,
                new[] { new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, 0.03f) }, "fire rate", "",
                null, new SoulTreeRequirement(_might, 2)));
            _tree = Track(SoulTree.Create(_might, _vigor, _draw));
            DeleteNodeKeys();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteNodeKeys();
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            foreach (var k in WalletKeys)
            {
                if (_savedPrefs.TryGetValue(k, out int v)) PlayerPrefs.SetInt(k, v);
                else PlayerPrefs.DeleteKey(k);
            }
            PlayerPrefs.Save();
        }

        private void DeleteNodeKeys()
        {
            foreach (var n in new[] { _might, _vigor, _draw })
            {
                if (n == null) continue;
                PlayerPrefs.DeleteKey(SoulTreeProgress.KeyOf(n));
                PlayerPrefs.DeleteKey(SoulTreeProgress.KeyOf(n) + ".check");
            }
            SoulTreeProgress.ResetAll(null); // clears TamperDetected
        }

        // ------------------------------------------------------------ Data

        [Test]
        public void Cost_GrowsPerLevel_RoundedToFive_ZeroOutsideRange()
        {
            Assert.That(new[] { _might.CostOf(1), _might.CostOf(2), _might.CostOf(3), _might.CostOf(4), _might.CostOf(5) },
                Is.EqualTo(new[] { 250, 365, 525, 760, 1105 }));
            Assert.That(_might.CostOf(0), Is.EqualTo(0));
            Assert.That(_might.CostOf(6), Is.EqualTo(0), "no level past max");
            Assert.That(_might.TotalCost(5), Is.EqualTo(3005));
            Assert.That(_might.TotalCost(9), Is.EqualTo(3005), "capped at max");
            Assert.That(_tree.FullCost(), Is.EqualTo(3005 + 100 + 200 + 400 + 600 + 900));
        }

        [Test]
        public void EffectText_PercentChanceAndFlat()
        {
            Assert.That(_might.EffectText(2), Is.EqualTo("+8% bolt damage"));
            Assert.That(_vigor.EffectText(2), Is.EqualTo("+80 max health"));
            Assert.That(_might.EffectText(0), Is.EqualTo(""));
            var eye = Track(SoulTreeNode.Create("test-eye", "Hawk's Eye", 2, 5, 600, 1.45f,
                new[] { new StatModifier(StatId.CritChance, ModifierKind.Flat, 0.02f) }, "crit chance"));
            Assert.That(eye.EffectText(3), Is.EqualTo("+6% crit chance"), "flat 0..1 chance reads as percent points");
        }

        // ------------------------------------------------------------ Buy rules

        [Test]
        public void Buy_ChecksInOrder_SpendsTheCost_SavesTheLevel()
        {
            var bought = new List<(SoulTreeNode, int)>();
            void OnBought(SoulTreeNode n, int l) => bought.Add((n, l));
            SoulTree.Bought += OnBought;
            try
            {
                var stranger = Track(SoulTreeNode.Create("test-stranger", "Stranger", 0, 1, 10, 1f, null));
                Assert.That(_tree.Check(stranger), Is.EqualTo(SoulTreeBuyCheck.Unknown), "not in this tree");
                Assert.That(_tree.Check(null), Is.EqualTo(SoulTreeBuyCheck.Unknown));

                Assert.That(_tree.TryBuy(_might), Is.EqualTo(SoulTreeBuyCheck.NotEnoughSouls), "empty wallet");
                Assert.That(SoulTreeProgress.Level(_might), Is.EqualTo(0));
                Assert.That(_tree.Check(_draw), Is.EqualTo(SoulTreeBuyCheck.Locked), "prerequisite before wallet");

                SoulWallet.Earn(1000);
                Assert.That(_tree.TryBuy(_might), Is.EqualTo(SoulTreeBuyCheck.Ok));
                Assert.That(SoulWallet.Balance, Is.EqualTo(750));
                Assert.That(SoulTreeProgress.Level(_might), Is.EqualTo(1));
                Assert.That(_tree.TryBuy(_draw), Is.EqualTo(SoulTreeBuyCheck.Locked), "needs Might II");
                Assert.That(SoulWallet.Balance, Is.EqualTo(750), "a refused buy spends nothing");

                Assert.That(_tree.TryBuy(_might), Is.EqualTo(SoulTreeBuyCheck.Ok));
                Assert.That(SoulWallet.Balance, Is.EqualTo(385), "second level costs 365");
                Assert.That(_tree.Check(_draw), Is.EqualTo(SoulTreeBuyCheck.NotEnoughSouls), "open now, 400 > 385");
                Assert.That(SoulTree.IsUnlocked(_draw), Is.True);

                SoulWallet.Earn(15);
                Assert.That(_tree.TryBuy(_draw), Is.EqualTo(SoulTreeBuyCheck.Ok), "exact amount is enough");
                Assert.That(SoulWallet.Balance, Is.EqualTo(0));
                Assert.That(bought, Is.EqualTo(new[] { (_might, 1), (_might, 2), (_draw, 1) }));
            }
            finally
            {
                SoulTree.Bought -= OnBought;
            }
        }

        [Test]
        public void Buy_StopsAtMaxLevel()
        {
            SoulWallet.Earn(10000);
            Assert.That(_tree.TryBuy(_vigor), Is.EqualTo(SoulTreeBuyCheck.Ok));
            Assert.That(_tree.TryBuy(_vigor), Is.EqualTo(SoulTreeBuyCheck.Ok));
            Assert.That(SoulWallet.Balance, Is.EqualTo(10000 - 100 - 200));
            Assert.That(_tree.Check(_vigor), Is.EqualTo(SoulTreeBuyCheck.MaxLevel));
            Assert.That(_tree.TryBuy(_vigor), Is.EqualTo(SoulTreeBuyCheck.MaxLevel));
            Assert.That(SoulWallet.Balance, Is.EqualTo(9700), "max level spends nothing");
            Assert.That(SoulTree.NextCost(_vigor), Is.EqualTo(0));
            Assert.That(_tree.TotalSpent(), Is.EqualTo(300));
        }

        [Test]
        public void MissingRequirement_NamesTheFirstUnmet()
        {
            Assert.That(SoulTree.MissingRequirement(_draw).node, Is.SameAs(_might));
            Assert.That(SoulTree.MissingRequirement(_draw).level, Is.EqualTo(2));
            SoulTreeProgress.SetLevel(_might, 2);
            Assert.That(SoulTree.MissingRequirement(_draw).node, Is.Null);
            Assert.That(SoulTree.MissingRequirement(_might).node, Is.Null, "roots have none");
        }

        // ------------------------------------------------------------ Persistence

        [Test]
        public void Levels_SavedWithChecksum_TamperResetsToZero_CopiedPairFails_ClampedToMax()
        {
            SoulTreeProgress.SetLevel(_might, 3);
            string key = SoulTreeProgress.KeyOf(_might);
            Assert.That(key, Is.EqualTo("soultree.test-might"));
            Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(3));
            Assert.That(PlayerPrefs.GetInt(key + ".check"),
                Is.EqualTo(CheckedPrefInt.Checksum(3, SoulTreeProgress.SaltOf("test-might"))));
            Assert.That(SoulTreeProgress.Level(_might), Is.EqualTo(3), "a restart reads the same level");

            // A valid pair of one node copied to another fails: each node has its own salt.
            PlayerPrefs.SetInt(SoulTreeProgress.KeyOf(_vigor), 3);
            PlayerPrefs.SetInt(SoulTreeProgress.KeyOf(_vigor) + ".check", PlayerPrefs.GetInt(key + ".check"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("test-vigor.*checksum"));
            Assert.That(SoulTreeProgress.Level(_vigor), Is.EqualTo(0));
            Assert.That(SoulTreeProgress.TamperDetected, Is.True);
            Assert.That(PlayerPrefs.GetInt(SoulTreeProgress.KeyOf(_vigor)), Is.EqualTo(0), "written back as 0");

            // Hand edit of the level alone.
            PlayerPrefs.SetInt(key, 5);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("test-might.*checksum"));
            Assert.That(SoulTreeProgress.Level(_might), Is.EqualTo(0));

            // A valid save above the max (max lowered after release) reads as the max.
            SoulTreeProgress.ResetAll(_tree);
            Assert.That(SoulTreeProgress.TamperDetected, Is.False);
            new CheckedPrefInt(SoulTreeProgress.KeyOf(_vigor), SoulTreeProgress.SaltOf("test-vigor")).Write(9);
            Assert.That(SoulTreeProgress.Level(_vigor), Is.EqualTo(2));
            SoulTreeProgress.SetLevel(_vigor, 7);
            Assert.That(PlayerPrefs.GetInt(SoulTreeProgress.KeyOf(_vigor)), Is.EqualTo(2), "SetLevel clamps too");
        }

        [Test]
        public void ResetAndRefund_GivesBackEverySoulSpent()
        {
            SoulWallet.Earn(2000);
            _tree.TryBuy(_might);
            _tree.TryBuy(_might);
            _tree.TryBuy(_vigor);
            Assert.That(SoulWallet.Balance, Is.EqualTo(2000 - 250 - 365 - 100));
            Assert.That(_tree.ResetAndRefund(), Is.EqualTo(715));
            Assert.That(SoulWallet.Balance, Is.EqualTo(2000));
            Assert.That(SoulTreeProgress.Level(_might), Is.EqualTo(0));
            Assert.That(SoulTreeProgress.Level(_vigor), Is.EqualTo(0));
        }

        // ------------------------------------------------------------ Run start

        [Test]
        public void ApplyTo_AddsPermanentSources_BaselineIgnoresInRunSkills()
        {
            var go = Track(new GameObject("Knight"));
            var stats = go.AddComponent<PlayerStats>();
            SoulTreeProgress.SetLevel(_might, 3);
            SoulTreeProgress.SetLevel(_vigor, 2);
            Assert.That(_tree.ApplyTo(stats), Is.EqualTo(2), "two owned nodes");
            Assert.That(stats.Modify(StatId.Damage, 10f), Is.EqualTo(11.2f).Within(1e-4f), "+12 %");
            Assert.That(stats.Modify(StatId.MaxHealth, 400f), Is.EqualTo(480f).Within(1e-4f), "+80 flat");
            Assert.That(stats.Modify(StatId.Damage, 10f, permanentOnly: true), Is.EqualTo(11.2f).Within(1e-4f));

            // An in-run skill counts in the full value, not in the baseline.
            var skill = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            stats.SetSource(skill, new[] { new StatModifier(StatId.Damage, ModifierKind.Percent, 0.2f) }, 1);
            Assert.That(stats.Modify(StatId.Damage, 10f), Is.EqualTo(13.2f).Within(1e-4f), "percents add up");
            Assert.That(stats.Modify(StatId.Damage, 10f, permanentOnly: true), Is.EqualTo(11.2f).Within(1e-4f));
            var bare = PowerSnapshot.Measure(null, PowerBase.Default);
            var start = PowerSnapshot.Measure(stats, PowerBase.Default, permanentOnly: true);
            Assert.That(start.BoltDamage, Is.EqualTo(bare.BoltDamage * 1.12f).Within(1e-3f));
            Assert.That(PowerSnapshot.Measure(stats, PowerBase.Default).BoltDamage, Is.EqualTo(bare.BoltDamage * 1.32f).Within(1e-3f));

            // Back to 0: the source goes; the baseline is the bare Knight again.
            SoulTreeProgress.SetLevel(_might, 0);
            Assert.That(_tree.ApplyTo(stats), Is.EqualTo(1));
            Assert.That(stats.Modify(StatId.Damage, 10f, permanentOnly: true), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(stats.Modify(StatId.Damage, 10f), Is.EqualTo(12f).Within(1e-4f));
        }

        // ------------------------------------------------------------ Shipped asset

        [Test]
        public void ShippedTree_IdsUnique_PrerequisitesInTreeAndAbove_CostTableLogged()
        {
            var tree = UnityEditor.AssetDatabase.LoadAssetAtPath<SoulTree>(TreePath);
            Assert.That(tree, Is.Not.Null, TreePath + " (EconomyBuilder.LoadOrCreateSoulTree)");
            Assert.That(tree.Nodes.Count, Is.EqualTo(7));
            var ids = new HashSet<string>();
            var log = new StringBuilder("[SoulTree] shipped cost table:\n");
            int roots = 0;
            foreach (var node in tree.Nodes)
            {
                Assert.That(node, Is.Not.Null);
                Assert.That(ids.Add(node.Id), Is.True, "unique id " + node.Id);
                Assert.That(node.Modifiers.Length, Is.GreaterThan(0), node.Id);
                Assert.That(node.Icon, Is.Not.Null, node.Id + " icon");
                Assert.That(node.EffectText(1), Does.StartWith("+"), node.Id);
                if (node.Requires.Length == 0) roots++;
                foreach (var r in node.Requires)
                {
                    Assert.That(tree.Contains(r.node), Is.True, $"{node.Id} needs a node of this tree");
                    Assert.That(r.node.Tier, Is.LessThanOrEqualTo(node.Tier), $"{node.Id} needs a node above or beside it");
                    Assert.That(r.level, Is.InRange(1, r.node.MaxLevel), node.Id);
                }
                log.Append($"  {node.Id,-7} tier {node.Tier} max {node.MaxLevel}: ");
                for (int l = 1; l <= node.MaxLevel; l++) log.Append(node.CostOf(l)).Append(l < node.MaxLevel ? " / " : "");
                log.Append($" = {node.TotalCost(node.MaxLevel)}; max effect {node.EffectText(node.MaxLevel)}\n");
            }
            Assert.That(roots, Is.EqualTo(2), "two roots: Might and Vigor");
            Assert.That(tree.FullCost(), Is.InRange(15000, 40000), "whole tree = a few dozen runs of ~1-2k Souls");
            log.Append($"  full tree {tree.FullCost()} Souls");
            Debug.Log(log.ToString());
        }
    }
}
