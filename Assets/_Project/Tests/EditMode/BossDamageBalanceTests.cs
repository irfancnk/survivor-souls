using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SurvivorSouls.Editor.Enemies;
using SurvivorSouls.Gameplay.Boss;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests.EditMode
{
    /// <summary>
    /// KAN-163: boss attacks hit hard but stay fair. Reads the real boss assets (the builder never overwrites them):
    /// the new share table (x1.8 of KAN-108..111), max-hit cap 0.5, phase 2 x1.15, telegraph floor on every damaging attack,
    /// 3 clean hits of the biggest attack kill a fresh Knight, and contact damage stays below a telegraphed hit.
    /// Prints a "hits to kill" table (fresh Knight, phase 1 / phase 2) for the balance sheet.
    /// </summary>
    public sealed class BossDamageBalanceTests
    {
        private const float FreshKnight = 400f; // PlayerHealth.maxHealth
        private const float Cap = 0.5f;
        private const float Phase2Factor = 1.15f;

        private static readonly string[] Bosses =
        {
            "Boss_Realm1_Butcher", "Boss_PyreLord", "Boss_RottingColossus", "Boss_BoneMatriarch",
        };

        /// <summary>Share of max health per attack (KAN-163 table). 0 = no damage (summon, teleport).</summary>
        private static readonly Dictionary<string, Dictionary<string, float>> Table = new Dictionary<string, Dictionary<string, float>>
        {
            ["Boss_Realm1_Butcher"] = new Dictionary<string, float>
            {
                ["Cleaver Chop"] = 0.40f, ["Cleaver Spin"] = 0.14f, ["Charge"] = 0.36f, ["Chain Hook"] = 0.18f,
                ["Triple Charge"] = 0.30f, ["Black Fireballs"] = 0.11f,
            },
            ["Boss_PyreLord"] = new Dictionary<string, float>
            {
                ["Flame Slam"] = 0.36f, ["Fireball Fan"] = 0.22f, ["Fire Nova"] = 0.25f, ["Fire Spiral"] = 0.29f,
                ["Meteor Rain"] = 0.27f,
            },
            ["Boss_RottingColossus"] = new Dictionary<string, float>
            {
                ["Double Slam"] = 0.29f, ["Grab"] = 0.36f, ["Corpse Boulder"] = 0.36f, ["Boulder Volley"] = 0.36f,
                ["Summon Ghouls"] = 0f,
            },
            ["Boss_BoneMatriarch"] = new Dictionary<string, float>
            {
                ["Homing Skulls"] = 0.22f, ["Soul Ring"] = 0.25f, ["Raise the Dead"] = 0f, ["Cross Beams"] = 0.34f,
                ["Teleport"] = 0f,
            },
        };

        private static BossData Load(string name)
        {
            var data = AssetDatabase.LoadAssetAtPath<BossData>($"Assets/_Project/Data/Boss/{name}.asset");
            Assert.That(data, Is.Not.Null, name);
            return data;
        }

        private static IEnumerable<(string boss, BossData data)> All() => Bosses.Select(b => (b, Load(b)));

        [Test]
        public void EveryAttack_MatchesTheNewShareTable()
        {
            foreach (var (boss, data) in All())
            {
                var expected = Table[boss];
                Assert.That(data.Attacks.Select(a => a.label), Is.EquivalentTo(expected.Keys), boss + " attack list");
                foreach (var a in data.Attacks)
                    Assert.That(a.damage, Is.EqualTo(expected[a.label]).Within(1e-4f), $"{boss} {a.label}");
            }
        }

        [Test]
        public void Cap_IsHalf_Phase2HitsHarder_AndNoAttackIsClippedByTheCap()
        {
            foreach (var (boss, data) in All())
            {
                Assert.That(data.MaxHitShare, Is.EqualTo(Cap).Within(1e-4f), boss + " cap");
                Assert.That(data.Phase2Damage, Is.EqualTo(Phase2Factor).Within(1e-4f), boss + " phase 2 factor");
                foreach (var a in data.Attacks)
                {
                    if (a.damage <= 0f) continue;
                    if (a.InPhase(1))
                        Assert.That(data.ShareFor(a.damage, 1), Is.EqualTo(a.damage).Within(1e-4f), $"{boss} {a.label} phase 1");
                    if (a.InPhase(2))
                        Assert.That(a.damage * data.Phase2Damage, Is.LessThanOrEqualTo(Cap + 1e-4f),
                            $"{boss} {a.label}: phase 2 stays under the cap (the table decides, not the clip)");
                }
            }
        }

        [Test]
        public void DamageFor_AppliesThePhase2Factor_ThenTheCap()
        {
            var data = Load("Boss_Realm1_Butcher");
            Assert.That(data.DamageFor(0.4f, FreshKnight, 1), Is.EqualTo(160f).Within(1e-3f));
            Assert.That(data.DamageFor(0.4f, FreshKnight, 2), Is.EqualTo(184f).Within(1e-3f), "+15 %");
            Assert.That(data.DamageFor(0.48f, FreshKnight, 2), Is.EqualTo(200f).Within(1e-3f), "capped at half");
            Assert.That(data.DamageFor(0.9f, FreshKnight, 1), Is.EqualTo(200f).Within(1e-3f), "no instant kill");
            Assert.That(data.DamageFor(0.4f, FreshKnight), Is.EqualTo(160f).Within(1e-3f), "old overload = phase 1");

            var plain = BossData.Create(null);
            try
            {
                Assert.That(plain.Phase2Damage, Is.EqualTo(1f), "code-built test bosses keep plain phase 2 numbers");
            }
            finally
            {
                Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void EveryDamagingAttack_WarnsAtLeastTheFloor_InBothPhases()
        {
            foreach (var (boss, data) in All())
                foreach (var a in data.Attacks)
                {
                    if (a.damage <= 0f && !a.Burns) continue;
                    Assert.That(a.telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), $"{boss} {a.label} asset value");
                    for (int phase = 1; phase <= 2; phase++)
                        if (a.InPhase(phase))
                            Assert.That(data.TelegraphFor(a.Telegraph, phase), Is.GreaterThanOrEqualTo(BossData.MinTelegraph),
                                $"{boss} {a.label} phase {phase}");
                }
        }

        [Test]
        public void ThreeCleanHitsOfTheBiggestAttack_KillAFreshKnight()
        {
            foreach (var (boss, data) in All())
            {
                float biggest = data.Attacks.Max(a => a.InPhase(1) ? data.ShareFor(a.damage, 1) : data.ShareFor(a.damage, 2));
                Assert.That(3f * biggest * FreshKnight, Is.GreaterThanOrEqualTo(FreshKnight), boss);
                Assert.That(biggest, Is.LessThan(0.5f + 1e-4f), boss + ": never a one-shot or a two-shot from full");
            }
        }

        [Test]
        public void ContactDamage_IsRaised_ButBelowATelegraphedHit()
        {
            Assert.That(EnemyRosterBuilder.BossContactDps, Is.EqualTo(45f), "KAN-163: was 30");
            foreach (var (boss, data) in All())
            {
                Assert.That(data.Prefab, Is.Not.Null, boss + " body");
                float dps = data.Prefab.DamagePerSecond;
                if (boss == "Boss_BoneMatriarch")
                {
                    Assert.That(dps, Is.EqualTo(0f), "the Matriarch never melees (KAN-111)");
                    continue;
                }
                Assert.That(dps, Is.EqualTo(EnemyRosterBuilder.BossContactDps), boss + " body (rebuild the scene after a change)");
                float biggest = data.Attacks.Max(a => a.damage);
                Assert.That(dps / FreshKnight, Is.LessThan(biggest), $"{boss}: 1 s of contact hurts less than its biggest hit");
            }
        }

        [Test]
        public void HitsToKill_Table()
        {
            var sb = new StringBuilder("KAN-163 hits to kill a fresh Knight (400 HP): attack | share | phase 1 | phase 2\n");
            foreach (var (boss, data) in All())
                foreach (var a in data.Attacks)
                {
                    if (a.damage <= 0f) continue;
                    string p1 = a.InPhase(1) ? Mathf.CeilToInt(1f / data.ShareFor(a.damage, 1) - 1e-4f).ToString() : "-";
                    string p2 = a.InPhase(2) ? Mathf.CeilToInt(1f / data.ShareFor(a.damage, 2) - 1e-4f).ToString() : "-";
                    sb.AppendLine($"{boss} | {a.label} | {a.damage * 100f:0} % | {p1} | {p2}");
                }
            TestContext.WriteLine(sb.ToString());
            Debug.Log(sb.ToString());
            Assert.Pass();
        }
    }
}
