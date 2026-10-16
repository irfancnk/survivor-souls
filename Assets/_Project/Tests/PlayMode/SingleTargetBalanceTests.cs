#if UNITY_EDITOR
using NUnit.Framework;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-86 balance targets on the shipped skill table (KAN-75 power curve, 200 runs, random card of each offer): the
    /// Knight's single-target DPS keeps up for the bosses (level 10 at least 2.5x, level 20 at least 4x the bare Knight)
    /// while the crowd / single-target gap stays at most 6.5 (it was 9.8 before the pierce / bounce falloff). Crowd skills
    /// still pay off more than single-target ones. The seed is fixed, so the numbers repeat.
    /// </summary>
    public sealed class SingleTargetBalanceTests
    {
        private const string ProgressionPath = "Assets/_Project/Data/Skills/SkillProgression_Run.asset";

        private static SkillPowerCurve ShippedCurve()
        {
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            Assert.That(table, Is.Not.Null);
            return SkillPowerCurve.Simulate(table, PowerBase.Default, 200, 20);
        }

        [Test]
        public void ShippedCurve_SingleTargetKeepsUp_CrowdGapStaysInBounds()
        {
            var curve = ShippedCurve();
            Debug.Log("[SingleTargetBalanceTests] " + curve);
            var bare = curve.AtLevel(1);
            float Single(int level) => curve.AtLevel(level).SingleTargetDps / bare.SingleTargetDps;
            float Crowd(int level) => curve.AtLevel(level).CrowdDps / bare.CrowdDps;

            Assert.That(Single(5), Is.GreaterThan(1.75f), "early boss (Chamber 5): direct stat skills show");
            Assert.That(Single(10), Is.GreaterThanOrEqualTo(2.5f));
            Assert.That(Single(20), Is.GreaterThanOrEqualTo(4f), "KAN-86: was 2.1x when the ticket was written");
            Assert.That(Single(20), Is.LessThanOrEqualTo(6f), "bosses still take a fight");
            Assert.That(Crowd(20) / Single(20), Is.LessThanOrEqualTo(6.5f), "crowd / single-target gap");
            Assert.That(Crowd(20), Is.GreaterThan(Single(20) * 3f), "crowd skills still pay off big");
        }

        [Test]
        public void ShippedTable_DirectDamageSkillsAreGuaranteedEarly()
        {
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            Assert.That(table.GuaranteedAt(2)?.Id, Is.EqualTo("sharpened_bolts"), "first level-up always offers bolt damage");
            Assert.That(table.GuaranteedAt(8)?.Id, Is.EqualTo("quick_draw"));
        }

        [Test]
        public void PowerSnapshot_HitFactor_FollowsTheFalloff()
        {
            var s = new PowerSnapshot { Pierce = 2f, Ricochet = 1f, PierceFalloff = 0.33f, BounceFalloff = 0.3f };
            // 1 + 0.67 + 0.67^2 + 0.67^2 x 0.7
            Assert.That(s.HitFactor, Is.EqualTo(1f + 0.67f + 0.4489f + 0.4489f * 0.7f).Within(1e-4f));
            s.PierceFalloff = s.BounceFalloff = 0f;
            Assert.That(s.HitFactor, Is.EqualTo(4f).Within(1e-4f), "no falloff = 1 + pierce + bounce");
            s.Pierce = s.Ricochet = 0f;
            s.PierceFalloff = 0.5f;
            Assert.That(s.HitFactor, Is.EqualTo(1f), "one hit");
            Assert.That(PowerBase.Default.PierceFalloff, Is.EqualTo(BoltShot.DefaultPierceFalloff));
            Assert.That(PowerBase.Default.BounceFalloff, Is.EqualTo(BoltShot.DefaultBounceFalloff));
        }
    }
}
#endif
