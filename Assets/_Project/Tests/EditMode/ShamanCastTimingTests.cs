using NUnit.Framework;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Editor.Enemies;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-138 Hexbound Shaman feel, asset side (run the Game scene builder first): the Cast / Summon states play so the
    /// clip's hand peak lands on the release of the orb ring (0.7 s) and of the hex / raise (0.8 s); the procedural cast
    /// sounds exist with sane lengths.
    /// </summary>
    public sealed class ShamanCastTimingTests
    {
        private const string Controller = "Assets/_Project/Art/Characters/Enemies/HexboundShaman/AC_HexboundShaman.controller";

        private static AnimatorState State(string name)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            Assert.That(controller, Is.Not.Null, Controller);
            foreach (var child in controller.layers[0].stateMachine.states)
                if (child.state.name == name) return child.state;
            Assert.Fail($"state {name} missing");
            return null;
        }

        [TestCase("Cast")]
        [TestCase("Summon")]
        public void CastState_HandPeakLandsOnTheRelease(string name)
        {
            var state = State(name);
            var clip = (AnimationClip)state.motion;
            float windUp = ShamanBuilder.WindUpOf(name);
            Assert.That(windUp, Is.GreaterThan(0f));
            float peak = EnemyRosterBuilder.StrikeTime(AssetDatabase.GetAssetPath(clip), handsOnly: true);
            Assert.That(peak, Is.GreaterThan(0f).And.LessThanOrEqualTo(clip.length));
            Assert.That(state.speed, Is.EqualTo(ShamanBuilder.CastSpeed(peak, windUp)).Within(1e-3f));
            Assert.That(state.speed, Is.InRange(ShamanBuilder.MinCastSpeed, ShamanBuilder.MaxCastSpeed));
            if (state.speed > ShamanBuilder.MinCastSpeed && state.speed < ShamanBuilder.MaxCastSpeed)
                Assert.That(peak / state.speed, Is.EqualTo(windUp).Within(0.02f), "hand peak at the release");
        }

        [Test]
        public void CastSpeed_PutsThePeakOnTheWindUp_Clamped()
        {
            Assert.That(ShamanBuilder.CastSpeed(1.4f, 0.7f), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(ShamanBuilder.CastSpeed(0.4f, 0.8f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(ShamanBuilder.CastSpeed(0.01f, 0.8f), Is.EqualTo(ShamanBuilder.MinCastSpeed));
            Assert.That(ShamanBuilder.CastSpeed(9f, 0.7f), Is.EqualTo(ShamanBuilder.MaxCastSpeed));
            Assert.That(ShamanBuilder.WindUpOf("Cast"), Is.EqualTo(ShamanBuilder.Spec.orbWindUp));
            Assert.That(ShamanBuilder.WindUpOf("Summon"), Is.EqualTo(ShamanBuilder.Spec.castWindUp));
            Assert.That(ShamanBuilder.WindUpOf("Attack"), Is.Zero, "the melee Attack state keeps its speed");
        }

        [TestCase("sfx_shaman_hum", 0.75f, 0.85f)]
        [TestCase("sfx_shaman_orb_whoosh", 0.45f, 0.55f)]
        [TestCase("sfx_shaman_hex_chime", 0.75f, 0.85f)]
        [TestCase("sfx_shaman_raise_crack", 0.65f, 0.75f)]
        [TestCase("sfx_shaman_blink_pop", 0.25f, 0.31f)]
        public void Sounds_Exist_WithTheirLength(string stem, float min, float max)
        {
            for (int i = 1; i <= 2; i++)
            {
                string path = $"{SfxBuilder.Dir}/{stem}_{i:00}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.That(clip, Is.Not.Null, path);
                Assert.That(clip.length, Is.InRange(min, max), path);
                Assert.That(clip.channels, Is.EqualTo(1), path);
            }
        }
    }
}
