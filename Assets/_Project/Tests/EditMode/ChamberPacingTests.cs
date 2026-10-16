using NUnit.Framework;
using SurvivorSouls.Editor.Chamber;
using SurvivorSouls.Editor.Chests;
using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Editor.Progression;
using SurvivorSouls.Editor.Skills;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-113 / KAN-232 acceptance on the shipped Realm 1 data: the pacing model gives 3-4 Hunter level picks in Chamber 1
    /// and 2-3 in every other regular 45 s Chamber of a 20-Chamber run; spawn pressure keeps a strong Knight's field fuller than the table alone; max alive stays
    /// inside the phone budget; the chest pity clock fits the 2 minute Chamber.
    /// </summary>
    public sealed class ChamberPacingTests
    {
        private static SkillPowerCurve _curve;

        private static SkillPowerCurve Curve()
        {
            if (_curve != null) return _curve;
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
            Assert.That(table, Is.Not.Null);
            return _curve = SkillPowerCurve.Simulate(table, PowerBase.Default, 200, BalanceReport.PacingLevels);
        }

        [Test]
        public void ShippedPacing_TwoToThreePicksPerChamber()
        {
            string report = BalanceReport.Pacing(Curve(), out var rows);
            Debug.Log("[ChamberPacingTests] " + report);
            Assert.That(rows, Is.Not.Null, report);
            int regular = 0;
            foreach (var r in rows)
            {
                if (r.Skipped) continue;
                regular++;
                if (r.Chamber == 1) Assert.That(r.Picks, Is.InRange(3, 4), $"Chamber 1: {r.Picks} picks\n{report}");
                else Assert.That(r.Picks, Is.InRange(2, 3), $"Chamber {r.Chamber} ({r.Type}): {r.Picks} picks\n{report}");
                Assert.That(r.PeakAlive, Is.LessThanOrEqualTo(50.5f), $"Chamber {r.Chamber}: phone budget (KAN-44)");
            }
            Assert.That(regular, Is.EqualTo(16), "Realm 1: 20 Chambers, 4 of them boss Chambers (KAN-232)");
        }

        [Test]
        public void ShippedPressure_KeepsAStrongFieldFuller()
        {
            var waves = AssetDatabase.LoadAssetAtPath<EnemyWaveTable>(CombatBuilder.WavesPath);
            var difficulty = AssetDatabase.LoadAssetAtPath<ChamberDifficulty>(ChamberBuilder.DifficultyPath);
            var rules = AssetDatabase.LoadAssetAtPath<ChamberRules>(ChamberBuilder.RulesPath);
            var xp = AssetDatabase.LoadAssetAtPath<XpCurve>(ProgressionBuilder.CurvePath);
            var shipped = AssetDatabase.LoadAssetAtPath<HordeScaling>(BalanceReport.HordeScalingPath);
            Assert.That(waves != null && difficulty != null && rules != null && xp != null && shipped != null);
            var flat = Object.Instantiate(shipped);
            try
            {
                flat.SetPressure(0f, 1f, 0f, 1f, shipped.MaxAliveBudget);
                var a = ChamberPacing.Assumptions.Default;
                a.Engagement = 1.5f; // a strong crowd build that clears what it meets
                var with = ChamberPacing.Simulate(waves, difficulty, shipped, Curve(), xp, rules.SurvivalSeconds, a);
                var without = ChamberPacing.Simulate(waves, difficulty, flat, Curve(), xp, rules.SurvivalSeconds, a);
                var last = with.Length - 1;
                while (last > 0 && with[last].Skipped) last--;
                Assert.That(with[last].Spawned, Is.GreaterThan(without[last].Spawned * 1.15f),
                    $"pressure refills the field: {with[last].Spawned:0} vs {without[last].Spawned:0} spawned");
                Assert.That(with[last].AverageAlive, Is.GreaterThan(without[last].AverageAlive), "the field stays fuller");
                Assert.That(with[last].CapFactorEnd, Is.GreaterThan(1.2f));
                Assert.That(with[last].IntervalFactorEnd, Is.LessThan(0.85f));
                Assert.That(with[0].Spawned, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(flat);
            }
        }

        [Test]
        public void ShippedChestPity_FitsTheChamberClock()
        {
            var config = AssetDatabase.LoadAssetAtPath<ChestDropConfig>(ChestBuilder.ConfigPath);
            var rules = AssetDatabase.LoadAssetAtPath<ChamberRules>(ChamberBuilder.RulesPath);
            Assert.That(config, Is.Not.Null);
            Assert.That(rules, Is.Not.Null);
            Assert.That(config.PitySeconds, Is.LessThanOrEqualTo(rules.SurvivalSeconds * 0.5f + 0.01f),
                "a Chamber without an elite chest still gets its pity chest early enough to use it");
            Assert.That(config.PitySeconds, Is.GreaterThanOrEqualTo(20f), "pity is a safety net, not the drop rate");
        }

        [Test]
        public void Model_NoXpCurveOrWavesIsEmpty()
        {
            var rows = ChamberPacing.Simulate(null, null, null, Curve(), null, 120f, ChamberPacing.Assumptions.Default, 3);
            Assert.That(rows.Length, Is.EqualTo(3));
            Assert.That(rows[0].Spawned, Is.EqualTo(0f));
        }
    }
}
