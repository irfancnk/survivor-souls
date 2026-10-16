using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SurvivorSouls.Editor.Boss;
using SurvivorSouls.Gameplay.Boss;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests.EditMode
{
    /// <summary>
    /// KAN-190: the builder's attack specs (used only when a boss asset is missing) carry the same damage numbers as the
    /// shipped assets (KAN-163 shares, burn dps, max-hit cap 0.5, phase 2 x1.15). A deleted and rebuilt asset must not
    /// silently go back to the weak KAN-108..111 numbers. Other fields (ranges, clips, timings) may differ on purpose:
    /// the assets are designer-owned; differences are only logged.
    /// </summary>
    public sealed class BossBuilderSpecTests
    {
        private static readonly (string path, Func<BossAttack[]> specs)[] Bosses =
        {
            (BossBuilder.ButcherDataPath, BossBuilder.ButcherAttacks),
            (BossBuilder.PyreLordDataPath, BossBuilder.PyreLordAttacks),
            (BossBuilder.ColossusDataPath, BossBuilder.ColossusAttacks),
            (BossBuilder.MatriarchDataPath, BossBuilder.MatriarchAttacks),
        };

        private static BossData Load(string path)
        {
            var data = AssetDatabase.LoadAssetAtPath<BossData>(path);
            Assert.That(data, Is.Not.Null, path);
            return data;
        }

        [Test]
        public void BuilderSpecs_DealTheShippedDamage()
        {
            var drift = new StringBuilder();
            foreach (var (path, specs) in Bosses)
            {
                var shipped = Load(path).Attacks;
                var built = specs();
                Assert.That(built.Select(a => a.label), Is.EqualTo(shipped.Select(a => a.label)), path + " attack list + order");
                for (int i = 0; i < built.Length; i++)
                {
                    var b = built[i];
                    var s = shipped[i];
                    string at = $"{path} {b.label}";
                    Assert.That(b.type, Is.EqualTo(s.type), at + " type");
                    Assert.That(b.phases, Is.EqualTo(s.phases), at + " phases");
                    Assert.That(b.damage, Is.EqualTo(s.damage).Within(1e-4f), at + " damage share");
                    Assert.That(b.burnDps, Is.EqualTo(s.burnDps).Within(1e-4f), at + " burn dps share");
                    Assert.That(b.Burns, Is.EqualTo(s.Burns), at + " burns");
                    if (JsonUtility.ToJson(b) != JsonUtility.ToJson(s)) drift.AppendLine(at);
                }
            }
            if (drift.Length > 0) Debug.Log("KAN-190: non-damage fields differ from the builder spec (designer edits):\n" + drift);
        }

        [Test]
        public void FreshAsset_GetsTheShippedCapAndPhase2Factor()
        {
            foreach (var (path, specs) in Bosses)
            {
                var shipped = Load(path);
                var fresh = BossData.CreateWithAttacks(null, specs(), maxHitShare: BossBuilder.FreshMaxHitShare,
                    phase2Damage: BossBuilder.FreshPhase2Damage);
                try
                {
                    Assert.That(fresh.MaxHitShare, Is.EqualTo(shipped.MaxHitShare).Within(1e-4f), path + " cap");
                    Assert.That(fresh.Phase2Damage, Is.EqualTo(shipped.Phase2Damage).Within(1e-4f), path + " phase 2 factor");
                    foreach (var a in fresh.Attacks)
                        for (int phase = 1; phase <= 2; phase++)
                            if (a.InPhase(phase) && a.damage > 0f)
                                Assert.That(fresh.ShareFor(a.damage, phase), Is.EqualTo(shipped.ShareFor(a.damage, phase)).Within(1e-4f),
                                    $"{path} {a.label} phase {phase}");
                }
                finally
                {
                    Object.DestroyImmediate(fresh);
                }
            }
        }

        [Test]
        public void Samples_AndBlackFireballs_UseTheKan163Shares()
        {
            Assert.That(BossAttack.BlackFireballs().damage, Is.EqualTo(0.11f).Within(1e-4f), "Butcher black fireballs");
            Assert.That(BossAttack.Sample(BossFight.Move.Nova).damage, Is.EqualTo(0.25f).Within(1e-4f), "Fire Nova");
            Assert.That(BossAttack.Sample(BossFight.Move.Spiral).damage, Is.EqualTo(0.29f).Within(1e-4f), "Fire Spiral");
            Assert.That(BossAttack.Sample(BossFight.Move.Rain).damage, Is.EqualTo(0.27f).Within(1e-4f), "Meteor Rain");
            Assert.That(BossAttack.Sample(BossFight.Move.Lobbed).damage, Is.EqualTo(0.36f).Within(1e-4f), "Corpse Boulder");
            Assert.That(BossAttack.Sample(BossFight.Move.Shockwave).damage, Is.EqualTo(0.29f).Within(1e-4f), "Double Slam");
            Assert.That(BossAttack.Sample(BossFight.Move.Homing).damage, Is.EqualTo(0.22f).Within(1e-4f), "Homing Skulls");
            Assert.That(BossAttack.Sample(BossFight.Move.Beams).damage, Is.EqualTo(0.34f).Within(1e-4f), "Cross Beams");
            Assert.That(BossAttack.Sample(BossFight.Move.Summon).damage, Is.EqualTo(0f), "summon deals no damage");
            Assert.That(BossAttack.Sample(BossFight.Move.Teleport).damage, Is.EqualTo(0f), "teleport deals no damage");
        }
    }
}
