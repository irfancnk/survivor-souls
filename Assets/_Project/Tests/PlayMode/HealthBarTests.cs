#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-82 medieval health bar on the real HUD document (<see cref="HealthBarView"/>): a hit flashes the lost part,
    /// pops "-N", cracks off shards (more for bigger hits), shakes, and the ghost trail drains only after its delay;
    /// horde contact gathers into few pulses (pools never overflow); a heal glows and pops "+N"; a max raise grows the
    /// frame and glows gold without a heal pop; low / critical health beat and light the cracks; "Reduce screen effects"
    /// keeps the number but drops shake, shards and flash; shield segment and death. PlayerHealth shield absorbs first.
    /// </summary>
    public sealed class HealthBarTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private VisualElement _root;
        private HealthBarView _bar;
        private HealthBarFeel _feel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var go = Track(new GameObject("HUD"));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            yield return null;
            _root = doc.rootVisualElement;
            _feel = new HealthBarFeel();
            _bar = new HealthBarView(_root, _feel);
            _bar.Reset(400f, 400f, 400f);
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

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) _bar.Tick(Frame);
        }

        private void Hit(float current, float amount)
        {
            _bar.Changed(current, 400f);
            _bar.Damaged(amount);
        }

        [Test]
        public void Bound_ShowsNumberOverMax()
        {
            Assert.That(_bar.IsBound, "HUD document has every health bar element");
            Assert.That(_bar.Text, Is.EqualTo("400 / 400"));
            Assert.That(_root.Q(HealthBarView.FxName).Query(className: HealthBarView.ShardClass).ToList().Count,
                Is.EqualTo(HealthBarView.MaxShards), "shards pooled up front");
        }

        [Test]
        public void Hit_FlashesLostPart_PopsNumber_Shards_Shake_TrailWaitsThenDrains()
        {
            Hit(300f, 100f);
            float maxShake = 0f;
            for (int i = 0; i < 6; i++)
            {
                _bar.Tick(Frame);
                maxShake = Mathf.Max(maxShake, Mathf.Abs(_bar.ShakeOffset));
            }
            Assert.That(_bar.Pulses, Is.EqualTo(1));
            Assert.That(_bar.LastPopText, Is.EqualTo("-100"));
            Assert.That(_bar.PopsAlive, Is.EqualTo(1));
            Assert.That(_bar.ShardsAlive, Is.EqualTo(4), "a quarter of max health: 4 shards");
            Assert.That(_bar.Text, Is.Not.EqualTo("300 / 400"), "the number counts down with the fill");
            Assert.That(_bar.LostFlash, Is.GreaterThan(0.3f), "white flash on the lost part");
            Assert.That(maxShake, Is.GreaterThan(1f), "bar shakes");

            Run(0.25f);
            Assert.That(_bar.TrailShown, Is.EqualTo(1f).Within(0.001f), "ghost trail waits");
            Assert.That(_bar.Shown, Is.LessThan(0.8f), "fill already eased down");

            Run(2f);
            Assert.That(_bar.TrailShown, Is.EqualTo(0.75f).Within(0.002f), "trail drained to the fill");
            Assert.That(_bar.Text, Is.EqualTo("300 / 400"));
            Assert.That(_bar.ShardsAlive, Is.Zero);
            Assert.That(_bar.PopsAlive, Is.Zero);
            Assert.That(_bar.LostFlash, Is.Zero);
            Assert.That(_bar.ShakeOffset, Is.Zero);
        }

        [Test]
        public void SmallHit_TwoShards()
        {
            Hit(392f, 8f);
            _bar.Tick(Frame);
            Assert.That(_bar.ShardsAlive, Is.EqualTo(2));
        }

        [Test]
        public void HordeContact_GathersIntoPulses_PoolsCapped()
        {
            float hp = 400f;
            int maxShards = 0, maxPops = 0;
            for (int i = 0; i < 60; i++) // 1 s of contact damage every frame
            {
                hp -= 2f;
                Hit(hp, 2f);
                _bar.Tick(Frame);
                maxShards = Mathf.Max(maxShards, _bar.ShardsAlive);
                maxPops = Mathf.Max(maxPops, _bar.PopsAlive);
            }
            Assert.That(_bar.Pulses, Is.InRange(4, 5), "one pulse per 0.25 s, not per frame");
            Assert.That(maxShards, Is.LessThanOrEqualTo(HealthBarView.MaxShards));
            Assert.That(maxPops, Is.LessThanOrEqualTo(HealthBarView.MaxPops));
        }

        [Test]
        public void Heal_GlowsAhead_PopsPlus_ThenSettles()
        {
            Hit(200f, 200f);
            Run(2f);
            _bar.Changed(300f, 400f);
            Run(0.05f);
            Assert.That(_bar.LastPopText, Is.EqualTo("+100"));
            Assert.That(_bar.HealGlow, Is.GreaterThan(0.5f));
            Run(2f);
            Assert.That(_bar.HealGlow, Is.Zero);
            Assert.That(_bar.Shown, Is.EqualTo(0.75f).Within(0.002f));
            Assert.That(_bar.TrailShown, Is.EqualTo(0.75f).Within(0.002f), "trail rides up with a heal");
        }

        [Test]
        public void MaxRaise_GrowsFrame_GlowsGold_NoHealPop()
        {
            float before = _bar.Width;
            _bar.Changed(500f, 500f); // Vigor: +100 max, heals by the gain (Archero rule)
            Run(0.1f);
            Assert.That(_bar.GrowGlow, Is.GreaterThan(0.3f), "new part glows gold");
            Assert.That(_bar.LastPopText, Is.Null, "the max gain is not a heal tick");
            Run(2f);
            Assert.That(_bar.Width, Is.GreaterThan(before + 20f), "frame grew");
            Assert.That(_bar.Width, Is.LessThanOrEqualTo(_feel.maxWidth));
            Assert.That(_bar.GrowGlow, Is.Zero);
            Assert.That(_bar.Text, Is.EqualTo("500 / 500"));
        }

        [Test]
        public void LowAndCritical_Heartbeat_CracksGlow()
        {
            Hit(100f, 300f);
            float maxCracks = 0f;
            for (int i = 0; i < 120; i++)
            {
                _bar.Tick(Frame);
                maxCracks = Mathf.Max(maxCracks, _bar.Cracks);
            }
            Assert.That(_bar.IsLow, Is.True);
            Assert.That(_bar.IsCritical, Is.False);
            Assert.That(maxCracks, Is.GreaterThan(0.3f), "cracks glow with the heartbeat");
            Assert.That(_root.Q(HealthBarView.RootName).ClassListContains(HealthBarView.LowClass));

            Hit(40f, 60f);
            Run(0.5f);
            Assert.That(_bar.IsCritical, Is.True);
            Assert.That(_root.Q(HealthBarView.RootName).ClassListContains(HealthBarView.CriticalClass));

            _bar.Changed(400f, 400f);
            Run(1f);
            Assert.That(_bar.IsLow, Is.False);
            Assert.That(_bar.Cracks, Is.Zero);
        }

        [Test]
        public void ReduceEffects_KeepsNumber_NoShakeShardsFlash()
        {
            _bar.ReduceEffects = true;
            Hit(300f, 100f);
            float maxShake = 0f;
            for (int i = 0; i < 10; i++)
            {
                _bar.Tick(Frame);
                maxShake = Mathf.Max(maxShake, Mathf.Abs(_bar.ShakeOffset));
            }
            Assert.That(_bar.LastPopText, Is.EqualTo("-100"));
            Assert.That(_bar.ShardsAlive, Is.Zero);
            Assert.That(maxShake, Is.Zero);
            Assert.That(_bar.LostFlash, Is.Zero);
        }

        [Test]
        public void Shield_SegmentShown_DeathGoesDark()
        {
            _bar.ShieldChanged(100f);
            Run(0.05f);
            Assert.That(_bar.ShieldShown, Is.EqualTo(0.25f).Within(0.001f));
            _bar.ShieldChanged(0f);
            Run(0.05f);
            Assert.That(_bar.ShieldShown, Is.Zero);

            Hit(0f, 400f);
            _bar.Died();
            Run(1.5f);
            Assert.That(_bar.Text, Is.EqualTo("0 / 400"));
            Assert.That(_root.Q(HealthBarView.RootName).ClassListContains(HealthBarView.DeadClass));
        }

        [Test]
        public void PlayerHealth_ShieldAbsorbsFirst()
        {
            var go = Track(new GameObject("Knight"));
            var health = go.AddComponent<PlayerHealth>();
            float raised = -1f;
            health.OnShieldChanged += v => raised = v;
            health.SetShield(50f);
            Assert.That(raised, Is.EqualTo(50f));
            float max = health.Max;

            Assert.That(health.TakeDamage(30f), Is.Zero, "shield takes it all");
            Assert.That(health.Shield, Is.EqualTo(20f));
            Assert.That(health.Current, Is.EqualTo(max));

            Assert.That(health.TakeDamage(50f), Is.EqualTo(30f), "the rest comes off health");
            Assert.That(health.Shield, Is.Zero);
            Assert.That(health.Current, Is.EqualTo(max - 30f));
        }

        [Test]
        public void Setting_ReduceScreenEffects_RoundTrips()
        {
            bool was = GameSettings.ReduceScreenEffects;
            try
            {
                GameSettings.ReduceScreenEffects = true;
                Assert.That(GameSettings.ReduceScreenEffects, Is.True);
                GameSettings.ReduceScreenEffects = false;
                Assert.That(GameSettings.ReduceScreenEffects, Is.False);
            }
            finally
            {
                GameSettings.ReduceScreenEffects = was;
            }
        }
    }
}
#endif
