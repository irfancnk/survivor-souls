using System;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-235 (KAN-81.2) footstep clips: every surface set is short, soft (peak), low-passed (little energy up high),
    /// click-free, each variant different, left foot tone lower than right; the Game scene's <see cref="FootstepSounds"/>
    /// is wired (run the Game scene builder first).
    /// </summary>
    public sealed class FootstepSfxTests
    {
        private const string GameScene = "Assets/_Project/Scenes/Game.unity";
        private const int Rate = 44100;

        private static readonly (string name, int count, Func<int, float[]> make, float maxSeconds)[] Sets =
        {
            ("snow", SfxBuilder.StepSnowCount, SfxBuilder.StepSnow, 0.3f),
            ("shingle", SfxBuilder.StepOtherCount, SfxBuilder.StepShingle, 0.3f),
            ("wood", SfxBuilder.StepOtherCount, SfxBuilder.StepWood, 0.3f),
            ("stone", SfxBuilder.StepOtherCount, SfxBuilder.StepStone, 0.3f),
        };

        /// <summary>Energy of the first difference over the energy: ~0.49 for a 5 kHz sine, 2 for white noise. Low = dark.</summary>
        private static float Brightness(float[] b)
        {
            double e = 0, d = 0;
            for (int i = 1; i < b.Length; i++)
            {
                e += b[i] * b[i];
                float x = b[i] - b[i - 1];
                d += x * x;
            }
            return (float)(d / Math.Max(1e-9, e));
        }

        [Test]
        public void EverySet_ShortSoftLowPassed_ClickFree_VariantsDiffer()
        {
            foreach (var set in Sets)
            {
                Assert.That(set.count, Is.InRange(6, 8), $"{set.name}: 6-8 variants");
                float[] first = null;
                for (int v = 1; v <= set.count; v++)
                {
                    float[] b = set.make(v);
                    string id = $"{set.name} {v}";
                    Assert.That(b.Length, Is.InRange((int)(Rate * 0.12f), (int)(Rate * set.maxSeconds)), $"{id}: short");
                    float peak = 0f;
                    foreach (float x in b) peak = Mathf.Max(peak, Mathf.Abs(x));
                    Assert.That(peak, Is.InRange(0.5f, 0.81f), $"{id}: soft peak");
                    Assert.That(Mathf.Abs(b[0]), Is.LessThan(0.01f), $"{id}: no click at the start");
                    Assert.That(Mathf.Abs(b[b.Length - 1]), Is.LessThan(0.01f), $"{id}: no click at the end");
                    float bright = Brightness(b);
                    Assert.That(bright, Is.LessThan(0.5f), $"{id}: low-passed (brightness {bright:0.000})");
                    if (first != null) Assert.That(b, Is.Not.EqualTo(first), $"{id}: differs from variant 1");
                    else first = b;
                    Assert.That(set.make(v), Is.EqualTo(b), $"{id}: deterministic (same files on every rebuild)");
                }
            }
        }

        [Test]
        public void LeftFootTone_IsLowerThanRight()
        {
            Assert.That(SfxBuilder.StepIsLeft(1), Is.True, "variant 1 (clip index 0) = left");
            Assert.That(SfxBuilder.StepIsLeft(2), Is.False, "variant 2 (clip index 1) = right");
            foreach (var set in Sets)
            {
                float left = 0f, right = 0f;
                int nl = 0, nr = 0;
                for (int v = 1; v <= set.count; v++)
                {
                    float bright = Brightness(set.make(v));
                    if (SfxBuilder.StepIsLeft(v)) { left += bright; nl++; }
                    else { right += bright; nr++; }
                }
                left /= nl;
                right /= nr;
                Assert.That(left, Is.LessThan(right), $"{set.name}: left steps darker ({left:0.0000}) than right ({right:0.0000})");
            }
        }

        [Test]
        public void GameScene_FootstepSounds_Wired()
        {
            var scene = EditorSceneManager.OpenScene(GameScene, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                FootstepSounds sounds = null;
                GameAudio audio = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (sounds == null) sounds = root.GetComponentInChildren<FootstepSounds>(true);
                    if (audio == null) audio = root.GetComponentInChildren<GameAudio>(true);
                }
                Assert.That(sounds, Is.Not.Null, "Game scene has FootstepSounds");
                Assert.That(sounds.gameObject, Is.SameAs(audio.gameObject), "on the audio object");

                var so = new SerializedObject(sounds);
                (string field, int count)[] sets =
                {
                    ("snow", SfxBuilder.StepSnowCount), ("shingle", SfxBuilder.StepOtherCount),
                    ("wood", SfxBuilder.StepOtherCount), ("stone", SfxBuilder.StepOtherCount),
                };
                foreach (var (field, count) in sets)
                {
                    var clips = so.FindProperty(field);
                    Assert.That(clips.arraySize, Is.EqualTo(count), field);
                    for (int i = 0; i < count; i++)
                        Assert.That(clips.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"{field}[{i}]");
                }
                var body = so.FindProperty("body").objectReferenceValue as Transform;
                Assert.That(body, Is.Not.Null, "body = the Knight");
                Assert.That(so.FindProperty("mover").objectReferenceValue, Is.Not.Null, "mover");
                Assert.That(so.FindProperty("shooter").objectReferenceValue, Is.Not.Null, "shooter (fight ducking)");
                Assert.That(so.FindProperty("health").objectReferenceValue, Is.Not.Null, "health (fight ducking)");
                var footL = so.FindProperty("footL").objectReferenceValue as Transform;
                var footR = so.FindProperty("footR").objectReferenceValue as Transform;
                Assert.That(footL, Is.Not.Null, "left foot bone");
                Assert.That(footR, Is.Not.Null, "right foot bone");
                Assert.That(footL.IsChildOf(body) && footR.IsChildOf(body), Is.True, "feet are the Knight's");
                Assert.That(footL, Is.Not.SameAs(footR));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
