#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-243 on the real HUD document (<see cref="HealthBarView"/>): the fill is green above 50 %, orange from 20 % to
    /// 50 %, red below 20 % (critical heartbeat = red), with a hysteresis band so contact damage at a threshold never
    /// flickers, and the tint blends in 0.15 s; a hit drops the fill at once and the lost health shows as a white chunk
    /// that holds 0.35 s and drains in 0.3 s; a second hit keeps the chunk's edge (never jumps back right) and restarts
    /// the hold; the shake grows with the hit (6 px small, 12 px big); Reduce Effects keeps the chunk, drops the shake.
    /// In the Game scene the overhead bar over the Knight takes the same colour.
    /// </summary>
    public sealed class HealthBarColourTests
    {
        private const float Frame = 1f / 60f;
        private const float Max = 400f;

        private readonly List<Object> _objects = new List<Object>();
        private VisualElement _root;
        private HealthBarView _bar;
        private HealthBarFeel _feel;
        private float _hp;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var go = new GameObject("HUD");
            _objects.Add(go);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            yield return null;
            _root = doc.rootVisualElement;
            _feel = new HealthBarFeel();
            _bar = new HealthBarView(_root, _feel);
            _hp = Max;
            _bar.Reset(Max, Max, Max);
            yield return null; // layout pass
            Run(0.1f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) _bar.Tick(Frame);
        }

        private void SetHealth(float hp)
        {
            float delta = _hp - hp;
            _hp = hp;
            _bar.Changed(hp, Max);
            if (delta > 0f) _bar.Damaged(delta);
        }

        private static float Distance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        [Test]
        public void Colour_GreenAboveHalf_OrangeToTwentyPercent_RedBelow_TintAndClass()
        {
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Green));
            Assert.That(Distance(_bar.FillColour, _feel.green), Is.LessThan(1e-3f));
            var root = _root.Q(HealthBarView.RootName);
            Assert.That(root.ClassListContains("hud-hp--green"), Is.True);
            Assert.That(_root.Q(HealthBarView.FillName).resolvedStyle.backgroundImage.texture, Is.Not.Null, "neutral fill texture");

            SetHealth(0.5f * Max); // exactly half: still green
            Run(0.3f);
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Green));
            SetHealth(0.45f * Max);
            Run(0.3f);
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Orange));
            Assert.That(Distance(_bar.FillColour, _feel.orange), Is.LessThan(1e-3f));
            Assert.That(root.ClassListContains("hud-hp--orange") && !root.ClassListContains("hud-hp--green"), Is.True);
            Assert.That(_bar.IsCritical, Is.False);

            SetHealth(0.15f * Max);
            Run(0.3f);
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Red));
            Assert.That(Distance(_bar.FillColour, _feel.red), Is.LessThan(1e-3f));
            Assert.That(_bar.IsCritical, Is.True, "critical heartbeat starts with red");

            _hp = 0.9f * Max;
            _bar.Changed(_hp, Max); // a big heal
            Run(1f);
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Green));
        }

        [Test]
        public void Colour_BlendsOverBlendTime()
        {
            SetHealth(0.4f * Max);
            _bar.Tick(Frame);
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Orange));
            float early = Distance(_bar.FillColour, _feel.orange);
            Assert.That(early, Is.GreaterThan(0.05f), "not snapped: blending");
            Assert.That(Distance(_bar.FillColour, _feel.green), Is.GreaterThan(0.05f), "already on the way");
            Run(_feel.colourBlendTime);
            Assert.That(Distance(_bar.FillColour, _feel.orange), Is.LessThan(1e-3f), "blend done in 0.15 s");
        }

        [Test]
        public void Colour_Hysteresis_NoFlickerAtTheThreshold()
        {
            SetHealth(0.48f * Max);
            Run(0.5f);
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Orange));
            int changes = 0;
            var last = _bar.ColourState;
            for (int i = 0; i < 120; i++) // 2 s of tiny heals and hits around 50 %
            {
                float hp = (i % 2 == 0 ? 0.504f : 0.496f) * Max;
                if (hp > _hp)
                {
                    _hp = hp;
                    _bar.Changed(hp, Max);
                }
                else SetHealth(hp);
                _bar.Tick(Frame);
                if (_bar.ColourState != last) changes++;
                last = _bar.ColourState;
            }
            Assert.That(changes, Is.Zero, "inside the band the colour holds");
            _hp = 0.52f * Max;
            _bar.Changed(_hp, Max);
            Run(0.3f);
            Assert.That(_bar.ColourState, Is.EqualTo(HealthColour.Green), "past the band it turns back");

            Assert.That(HealthBarView.ColourFor(0.199f, null, _feel), Is.EqualTo(HealthColour.Red));
            Assert.That(HealthBarView.ColourFor(0.195f, HealthColour.Orange, _feel), Is.EqualTo(HealthColour.Orange), "drop 1 % below to turn red");
            Assert.That(HealthBarView.ColourFor(0.205f, HealthColour.Red, _feel), Is.EqualTo(HealthColour.Red), "climb 1 % above to leave red");
            Assert.That(HealthBarView.ColourFor(0.212f, HealthColour.Red, _feel), Is.EqualTo(HealthColour.Orange));
        }

        [Test]
        public void Chunk_HoldsThenDrains_InTime()
        {
            SetHealth(300f);
            _bar.Tick(Frame);
            Assert.That(_bar.Shown, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(_bar.Chunk, Is.EqualTo(0.25f).Within(1e-4f));
            Run(_feel.chunkHold - 0.05f);
            Assert.That(_bar.Chunk, Is.EqualTo(0.25f).Within(1e-4f), "holds 0.35 s");
            Assert.That(_bar.ChunkDraining, Is.False);
            Run(0.05f + _feel.chunkDrain * 0.5f);
            Assert.That(_bar.ChunkDraining, Is.True);
            Assert.That(_bar.Chunk, Is.GreaterThan(0.01f).And.LessThan(0.2f), "half way: ease-out, more than half gone");
            Run(_feel.chunkDrain * 0.5f + 0.05f);
            Assert.That(_bar.Chunk, Is.Zero, "drained in 0.3 s");
        }

        [Test]
        public void SecondHit_KeepsTheChunkEdge_RestartsTheHold_NeverJumpsRight()
        {
            SetHealth(300f);
            Run(_feel.chunkHold + _feel.chunkDrain * 0.4f); // draining
            float edge = _bar.TrailShown;
            Assert.That(edge, Is.LessThan(1f).And.GreaterThan(0.75f));
            SetHealth(250f);
            _bar.Tick(Frame);
            Assert.That(_bar.TrailShown, Is.EqualTo(edge).Within(1e-4f), "keeps its edge, no jump back to 100 %");
            Assert.That(_bar.Shown, Is.EqualTo(0.625f).Within(1e-4f));
            Assert.That(_bar.ChunkDraining, Is.False, "hold restarted");
            Run(_feel.chunkHold - 0.05f);
            Assert.That(_bar.TrailShown, Is.EqualTo(edge).Within(1e-4f));
            Run(_feel.chunkDrain + 0.2f);
            Assert.That(_bar.Chunk, Is.Zero);
            Assert.That(_bar.TrailShown, Is.EqualTo(0.625f).Within(1e-4f));
        }

        [Test]
        public void Shake_GrowsWithTheHit_SmallSixBigTwelve_FadesFast()
        {
            SetHealth(Max - 0.02f * Max); // 2 %: small
            _bar.Tick(Frame);
            Assert.That(_bar.ShakeAmplitude, Is.EqualTo(_feel.shakeSmall).Within(1e-3f));
            Run(0.5f);
            Assert.That(_bar.ShakeOffset, Is.Zero, "gone after the 0.25 s decay");

            SetHealth(_hp - 0.085f * Max); // between 5 % and 12 %
            _bar.Tick(Frame);
            Assert.That(_bar.ShakeAmplitude, Is.GreaterThan(_feel.shakeSmall).And.LessThan(_feel.shakeBig));
            Run(0.5f);

            SetHealth(_hp - 0.25f * Max); // big
            float peak = 0f;
            for (int i = 0; i < 10; i++)
            {
                _bar.Tick(Frame);
                peak = Mathf.Max(peak, Mathf.Abs(_bar.ShakeOffset));
            }
            Assert.That(_bar.ShakeAmplitude, Is.EqualTo(_feel.shakeBig).Within(1e-3f));
            Assert.That(peak, Is.GreaterThan(_feel.shakeBig * 0.4f).And.LessThanOrEqualTo(_feel.shakeBig + 1e-3f));
        }

        [Test]
        public void HordeContact_OneShakePerPulse_ChunkGrowsAndHolds()
        {
            int pulses = _bar.Pulses;
            for (int i = 0; i < 60; i++) // 1 s of 2 hp per frame
            {
                SetHealth(_hp - 2f);
                _bar.Tick(Frame);
                Assert.That(_bar.ChunkDraining, Is.False, "keeps holding while hits keep coming");
            }
            Assert.That(_bar.Pulses - pulses, Is.InRange(4, 5), "one shake per 0.25 s pulse");
            Assert.That(_bar.Chunk, Is.EqualTo(120f / Max).Within(0.003f), "the chunk shows the whole second of contact");
        }

        [UnityTest]
        public IEnumerator GameScene_OverheadBar_TakesTheSameColour()
        {
            RunState.NewRun(97, 9431);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var hud = Object.FindAnyObjectByType<PlayerHealthHud>();
            var health = Object.FindAnyObjectByType<PlayerHealth>();
            Assert.That(hud, Is.Not.Null);
            yield return null;
            Assert.That(Distance(hud.OverheadColour, hud.Bar.FillColour), Is.LessThan(1e-3f));
            Assert.That(hud.Bar.ColourState, Is.EqualTo(HealthColour.Green));
            health.TakeDamage(health.Max * 0.7f);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.That(hud.Bar.ColourState, Is.EqualTo(HealthColour.Orange));
            Assert.That(Distance(hud.OverheadColour, hud.Bar.FillColour), Is.LessThan(1e-3f), "overhead bar follows the top bar");
            var empty = SceneManager.CreateScene("HpColourEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
#endif
