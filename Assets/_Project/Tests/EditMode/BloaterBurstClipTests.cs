using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor.Enemies;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-146: the Plague Bloater Burst clip holds the arms wider (Tools/Meshy/fix_bloater_burst.py), so the hands stay out
    /// of the apron skirt in the swell. A plain re-export of the Meshy clip (to_unity.py) brings the clip back: these tests
    /// catch that. Angle = upper arm (shoulder to elbow) away from the body's down axis, in the shoulder-line plane.
    /// Meshy clip: ~28-30 deg at 0.25-0.3 s; held 15 deg wider: ~41-44 deg.
    /// </summary>
    public sealed class BloaterBurstClipTests
    {
        private const float MinOutward = 36f;

        private static float Outward(Transform root, string side)
        {
            Vector3 P(string bone) => Find(root, bone).position;
            Vector3 lat = (P("LeftArm") - P("RightArm")).normalized;
            Vector3 up = Vector3.ProjectOnPlane(P("neck") - P("Hips"), lat).normalized;
            Vector3 arm = P(side + "ForeArm") - P(side + "Arm");
            return Mathf.Atan2(Mathf.Abs(Vector3.Dot(arm, lat)), -Vector3.Dot(arm, up)) * Mathf.Rad2Deg;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var hit = Find(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        [TestCase(0.25f)]
        [TestCase(0.3f)]
        public void BurstSwell_HoldsBothArmsWide(float time)
        {
            var spec = EnemyRosterBuilder.Find(SpecialMeleeBuilder.Bloater);
            var prefab = AssetDatabase.LoadAssetAtPath<Enemy>(spec.PrefabPath);
            Assert.That(prefab, Is.Not.Null, spec.PrefabPath);
            var clip = AssetDatabase.LoadAllAssetsAtPath(spec.ClipPath("Burst")).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var enemy = Object.Instantiate(prefab);
            try
            {
                enemy.Animator.enabled = false;
                clip.SampleAnimation(enemy.Animator.gameObject, time);
                foreach (var side in new[] { "Left", "Right" })
                    Assert.That(Outward(enemy.transform, side), Is.GreaterThan(MinOutward), $"{side} upper arm at {time} s");
            }
            finally
            {
                Object.DestroyImmediate(enemy.gameObject);
            }
        }

        [Test]
        public void BurstClip_KeepsItsLengthAndTake()
        {
            var spec = EnemyRosterBuilder.Find(SpecialMeleeBuilder.Bloater);
            var clips = AssetDatabase.LoadAllAssetsAtPath(spec.ClipPath("Burst")).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            Assert.That(clips.Select(c => c.name), Is.EquivalentTo(new[] { "Burst" }));
            Assert.That(clips[0].length, Is.EqualTo(68f / 24f).Within(0.05f), "68 frames at 24 fps, longer than the 1 s swell");
        }
    }
}
