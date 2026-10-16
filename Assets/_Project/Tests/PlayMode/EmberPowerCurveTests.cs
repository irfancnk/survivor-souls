using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-188 acceptance: the power curve sees the KAN-77 Ember Bolts perks. Spread (L3) adds the burn on
    /// <see cref="PowerBase.SpreadFoes"/> neighbours of every bolt-lit fire to the crowd DPS; the death blast (L4) scales the
    /// crowd DPS by 1 + blast share x <see cref="PowerBase.BlastFoes"/> / <see cref="PowerBase.HitsToKill"/>. Neither
    /// touches the single-target DPS (a boss has no neighbours). With the assumptions at 0 the old formula comes back.
    /// The shipped Ember levels step the crowd DPS up (level 3 spread, level 4 blast); few random-pick runs reach them
    /// by level 20, so the shipped curve barely moves.
    /// </summary>
    public sealed class EmberPowerCurveTests
    {
        private const string ProgressionPath = "Assets/_Project/Data/Skills/SkillProgression_Run.asset";
        private const string EmberPath = "Assets/_Project/Data/Skills/Skill_EmberBolts.asset";

        private readonly List<Object> _objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private PlayerStats Stats(params StatModifier[] mods)
        {
            var go = new GameObject("Stats");
            _objects.Add(go);
            var stats = go.AddComponent<PlayerStats>();
            if (mods.Length > 0) stats.SetSource(go, mods, 1);
            return stats;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        [Test]
        public void Spread_AddsNeighbourBurnToCrowd_NotSingleTarget()
        {
            var b = PowerBase.Default;
            var burn = PowerSnapshot.Measure(Stats(Add(StatId.BurnDps, 55f)), b);
            var spread = PowerSnapshot.Measure(Stats(Add(StatId.BurnDps, 55f), Add(StatId.BurnSpread, 1f)), b);

            Assert.That(spread.SingleTargetDps, Is.EqualTo(burn.SingleTargetDps).Within(1e-3f), "no neighbours on a boss");
            Assert.That(spread.SpreadBurnDps, Is.EqualTo(55f * b.SpreadFoes).Within(1e-3f));
            // Bare volley: one bolt, one hit, so the crowd gains exactly the neighbour burn.
            Assert.That(spread.CrowdDps - burn.CrowdDps, Is.EqualTo(55f * b.SpreadFoes).Within(1e-2f));
        }

        [Test]
        public void Spread_FollowsTheVolley()
        {
            var b = PowerBase.Default;
            var mods = new[] { Add(StatId.BurnDps, 55f), Add(StatId.Pierce, 2f), Add(StatId.Multishot, 2f) };
            var burn = PowerSnapshot.Measure(Stats(mods), b);
            var spread = PowerSnapshot.Measure(Stats(Add(StatId.BurnDps, 55f), Add(StatId.Pierce, 2f), Add(StatId.Multishot, 2f),
                Add(StatId.BurnSpread, 1f)), b);
            float targets = burn.VolleyFactor * burn.HitFactor;
            Assert.That(targets, Is.GreaterThan(1.5f));
            Assert.That(spread.CrowdDps - burn.CrowdDps, Is.EqualTo(55f * b.SpreadFoes * targets).Within(0.05f),
                "every bolt-lit target spreads");
        }

        [Test]
        public void DeathBlast_ScalesCrowd_NotSingleTarget()
        {
            var b = PowerBase.Default;
            var burn = PowerSnapshot.Measure(Stats(Add(StatId.BurnDps, 75f), Add(StatId.BurnSpread, 1f)), b);
            var blast = PowerSnapshot.Measure(Stats(Add(StatId.BurnDps, 75f), Add(StatId.BurnSpread, 1f),
                Add(StatId.BurnDeathBlast, 0.6f)), b);

            float factor = 0.6f * b.BlastFoes / b.HitsToKill;
            Assert.That(blast.BlastFactor, Is.EqualTo(factor).Within(1e-4f));
            Assert.That(blast.CrowdDps / burn.CrowdDps, Is.EqualTo(1f + factor).Within(1e-4f));
            Assert.That(blast.SingleTargetDps, Is.EqualTo(burn.SingleTargetDps).Within(1e-3f));
        }

        [Test]
        public void Perks_WithoutBurn_AddNothing()
        {
            var bare = PowerSnapshot.Measure(Stats(), PowerBase.Default);
            var perksOnly = PowerSnapshot.Measure(Stats(Add(StatId.BurnSpread, 1f), Add(StatId.BurnDeathBlast, 0.6f)), PowerBase.Default);
            Assert.That(perksOnly.SpreadBurnDps, Is.EqualTo(0f));
            Assert.That(perksOnly.BlastFactor, Is.EqualTo(0f));
            Assert.That(perksOnly.CrowdDps, Is.EqualTo(bare.CrowdDps).Within(1e-3f));
        }

        [Test]
        public void ZeroAssumptions_GiveTheOldFormula()
        {
            var b = PowerBase.Default;
            b.SpreadFoes = 0f;
            b.BlastFoes = 0f;
            var s = PowerSnapshot.Measure(Stats(Add(StatId.BurnDps, 75f), Add(StatId.BurnSpread, 1f), Add(StatId.BurnDeathBlast, 0.6f),
                Add(StatId.Damage, 30f)), b);
            float old = (s.BoltDamage * s.ShotsPerSecond * s.CritFactor + s.BurnDps) * s.VolleyFactor * s.HitFactor;
            Assert.That(s.CrowdDps, Is.EqualTo(old).Within(1e-3f));
        }

#if UNITY_EDITOR
        [Test]
        public void ShippedEmberLevels_GrowCrowd_SingleTargetOnlyByBurn()
        {
            var ember = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(EmberPath);
            Assert.That(ember, Is.Not.Null);
            Assert.That(ember.MaxLevel, Is.EqualTo(7)); // KAN-220
            var crowd = new float[8];
            for (int level = 1; level <= 7; level++)
            {
                var stats = Stats();
                ember.ApplyLevel(stats, level);
                var s = PowerSnapshot.Measure(stats, PowerBase.Default);
                crowd[level] = s.CrowdDps;
                Assert.That(s.SingleTargetDps, Is.EqualTo(100f + s.BurnDps).Within(1e-2f), $"level {level}: no perk on a boss");
                if (level == 3) Assert.That(s.SpreadBurnDps, Is.GreaterThan(0f), "level 3 spreads");
                if (level == 4) Assert.That(s.BlastFactor, Is.GreaterThan(0f), "level 4 blasts");
            }
            Assert.That(crowd[2], Is.GreaterThan(crowd[1]));
            Assert.That(crowd[3], Is.GreaterThan(crowd[2] * 1.4f), "spread is a big crowd step");
            Assert.That(crowd[4], Is.GreaterThan(crowd[3] * 1.4f), "blast is a big crowd step");
            for (int level = 5; level <= 7; level++) Assert.That(crowd[level], Is.GreaterThan(crowd[level - 1]), $"level {level} grows the crowd");
            Assert.That(crowd[7] / crowd[6], Is.GreaterThan(crowd[6] / crowd[5]), "level 7 (spread x2, 100 % blast) is the milestone step");
        }

        [Test]
        public void ShippedCurve_SingleTargetUnchanged_CrowdNotLower()
        {
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            Assert.That(table, Is.Not.Null);
            var off = PowerBase.Default;
            off.SpreadFoes = 0f;
            off.BlastFoes = 0f;
            var with = SkillPowerCurve.Simulate(table, PowerBase.Default, 200, 20).AtLevel(20);
            var without = SkillPowerCurve.Simulate(table, off, 200, 20).AtLevel(20);

            Assert.That(with.SpreadBurn, Is.GreaterThanOrEqualTo(with.BurnSpread * 55f - 1e-3f),
                "spread burn kept per run: every spread run burns at least 55/s");
            Assert.That(with.CrowdDps, Is.GreaterThanOrEqualTo(without.CrowdDps));
            Assert.That(with.SingleTargetDps, Is.EqualTo(without.SingleTargetDps).Within(1e-2f));
        }
#endif
    }
}
