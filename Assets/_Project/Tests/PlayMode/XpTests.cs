using System;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-21 acceptance (logic side; looks and feel need the device): curve from the asset, one level-up per threshold
    /// with carry-over, a big gain crossing several levels raises one event per level in order, run reset, every kill
    /// drops a wisp of the right tier, the magnet collects inside the pickup radius only, drift brings far wisps in (and
    /// stays off when disabled), a full pool merges XP instead of losing it, pickups tap the haptics once per burst,
    /// 60 wisps simulate + render with no GC allocation, and the level-up flow pauses, shows one choice per level in
    /// order and resumes.
    /// </summary>
    public sealed class XpTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private XpCurve _curve;
        private XpManager _xp;
        private SoulCollector _collector;
        private SoulWispField _field;
        private bool _savedVibration;

        [SetUp]
        public void SetUp()
        {
            _savedVibration = GameSettings.Vibration;
            GameSettings.Vibration = true;
            Haptics.ResetState();
            UnityEngine.Random.InitState(2110); // pop directions are random: keep runs reproducible

            _curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            _curve.Configure(100, 1f, 1); // flat 100 XP per level: easy to reason about

            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false); // Awake after the curve is set
            _xp = xpGo.AddComponent<XpManager>();
            _xp.Setup(_curve);
            xpGo.SetActive(true);

            _collector = Track(new GameObject("Player")).AddComponent<SoulCollector>();
            _collector.Setup(2.6f, 1f);
            _field = Track(new GameObject("Souls")).AddComponent<SoulWispField>();
            _field.Setup(_xp, _collector, 128);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
            Time.timeScale = 1f;
            GameSettings.Vibration = _savedVibration;
            Haptics.ResetState();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) _field.Tick(Frame);
        }

        // ------------------------------------------------------------ Curve + XpManager

        [Test]
        public void Curve_TableFirst_ThenRoundedFormula_Growing()
        {
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(50, 1.22f, 5, new[] { 30, 50, 70 });
            Assert.That(curve.XpToNext(1), Is.EqualTo(30));
            Assert.That(curve.XpToNext(2), Is.EqualTo(50));
            Assert.That(curve.XpToNext(3), Is.EqualTo(70));
            Assert.That(curve.XpToNext(4), Is.EqualTo(90)); // 50 * 1.22^3 = 90.8 -> 90
            for (int level = 4; level < 30; level++)
            {
                Assert.That(curve.XpToNext(level + 1), Is.GreaterThanOrEqualTo(curve.XpToNext(level)));
                Assert.That(curve.XpToNext(level) % 5, Is.EqualTo(0));
            }
        }

        [Test]
        public void AddXp_BelowThreshold_NoLevelUp()
        {
            int ups = 0;
            _xp.OnLevelUp += _ => ups++;
            Assert.That(_xp.AddXp(99), Is.EqualTo(0));
            Assert.That(ups, Is.EqualTo(0));
            Assert.That(_xp.Level, Is.EqualTo(1));
            Assert.That(_xp.Progress, Is.EqualTo(0.99f).Within(1e-4f));
        }

        [Test]
        public void AddXp_ExactThreshold_OneLevelUp_NoRemainder()
        {
            var levels = new List<int>();
            _xp.OnLevelUp += levels.Add;
            Assert.That(_xp.AddXp(100), Is.EqualTo(1));
            Assert.That(levels, Is.EqualTo(new[] { 2 }));
            Assert.That(_xp.Xp, Is.EqualTo(0));
        }

        [Test]
        public void AddXp_BigGain_OneEventPerLevel_InOrder_CarriesRemainder()
        {
            var levels = new List<int>();
            var levelDuringEvent = new List<int>();
            _xp.OnLevelUp += l => { levels.Add(l); levelDuringEvent.Add(_xp.Level); };
            Assert.That(_xp.AddXp(350), Is.EqualTo(3));
            Assert.That(levels, Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(levelDuringEvent, Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(_xp.Level, Is.EqualTo(4));
            Assert.That(_xp.Xp, Is.EqualTo(50));
            Assert.That(_xp.TotalXp, Is.EqualTo(350));
        }

        [Test]
        public void ResetRun_BackToLevelOne()
        {
            _xp.AddXp(250);
            _xp.ResetRun();
            Assert.That(_xp.Level, Is.EqualTo(1));
            Assert.That(_xp.Xp, Is.EqualTo(0));
            Assert.That(_xp.TotalXp, Is.EqualTo(0));
            Assert.That(_xp.XpToNext, Is.EqualTo(100));
        }

        // ------------------------------------------------------------ Wisps

        [Test]
        public void Kill_DropsOneWisp_WithEnemyXp_AndTierByValue()
        {
            var enemy = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            enemy.Spawn(new Vector3(9f, 0f, 0f), null, null);
            enemy.TakeDamage(1f, Vector3.forward);
            Assert.That(_field.Count, Is.EqualTo(0), "a non-lethal hit drops nothing");
            enemy.TakeDamage(1e6f, Vector3.forward);
            Assert.That(_field.Count, Is.EqualTo(1));
            Assert.That(_field.XpAt(0), Is.EqualTo(enemy.XpValue));
            Assert.That(_field.TierAt(0), Is.EqualTo(0), "normal enemy = small wisp");
            Assert.That(_field.TierOf(40), Is.EqualTo(1), "elite");
            Assert.That(_field.TierOf(150), Is.EqualTo(2), "boss orb");
        }

        [Test]
        public void WispInsidePickupRadius_FliesIn_AndAddsXp()
        {
            _field.Spawn(new Vector3(1.5f, 1f, 0f), 30);
            Run(2f);
            Assert.That(_field.Count, Is.EqualTo(0));
            Assert.That(_xp.TotalXp, Is.EqualTo(30));
            Assert.That(_field.CollectedXp, Is.EqualTo(30));
        }

        [Test]
        public void WispOutsideRadius_StaysPut_WhenDriftOff()
        {
            _field.DriftEnabled = false;
            _field.Spawn(new Vector3(9f, 1f, 0f), 10);
            Run(_field.DriftAfter + 5f);
            Assert.That(_field.Count, Is.EqualTo(1));
            Assert.That(_xp.TotalXp, Is.EqualTo(0));
        }

        [Test]
        public void WispOutsideRadius_DriftsIn_AfterTimeLimit()
        {
            _field.Spawn(new Vector3(9f, 1f, 0f), 10);
            Run(_field.DriftAfter - 0.5f);
            Assert.That(_field.Count, Is.EqualTo(1), "not before the time limit");
            Run(5f);
            Assert.That(_field.Count, Is.EqualTo(0));
            Assert.That(_xp.TotalXp, Is.EqualTo(10));
        }

        [Test]
        public void DeadPlayer_DoesNotCollect()
        {
            _collector.enabled = false;
            _field.Spawn(new Vector3(1f, 1f, 0f), 10);
            Run(_field.DriftAfter + 3f);
            Assert.That(_field.Count, Is.EqualTo(1));
        }

        [Test]
        public void FullPool_MergesXp_NothingLost()
        {
            _field.Setup(_xp, _collector, 16);
            for (int i = 0; i < 40; i++)
            {
                float a = i * 0.7f;
                _field.Spawn(new Vector3(Mathf.Cos(a) * 1.5f, 1f, Mathf.Sin(a) * 1.5f), 10);
            }
            Assert.That(_field.Count, Is.EqualTo(16));
            Assert.That(_field.SpawnedXp, Is.EqualTo(400));
            Run(3f);
            Assert.That(_field.Count, Is.EqualTo(0));
            Assert.That(_xp.TotalXp, Is.EqualTo(400));
        }

        [Test]
        public void PickupStream_HapticsRateLimited()
        {
            for (int i = 0; i < 12; i++) _field.Spawn(new Vector3(0.1f * i, 1f, 0f), 10);
            Run(2f);
            Assert.That(_field.Count, Is.EqualTo(0));
            // 12 wisps arrive within a few tenths of a second: a soft patter of taps, not one per wisp.
            Assert.That(Haptics.PulseCount, Is.InRange(1, 4));
        }

        [Test]
        public void SixtyWisps_TickAndRender_NoGcAlloc()
        {
            var bodies = Track(new GameObject("Bodies")).AddComponent<ParticleSystem>();
            var motes = Track(new GameObject("Motes")).AddComponent<ParticleSystem>();
            _field.Setup(_xp, _collector, 128, bodies, motes);
            for (int i = 0; i < 60; i++)
            {
                float a = i * 2.4f, d = i % 2 == 0 ? 1.5f : 6f; // half fly in, half rest and emit motes
                _field.Spawn(new Vector3(Mathf.Cos(a) * d, 1f, Mathf.Sin(a) * d), 10);
            }
            _field.Tick(Frame); // warm-up
            Assert.That(() => _field.Tick(Frame), Is.Not.AllocatingGCMemory());
            Run(0.6f);
            Assert.That(() => _field.Tick(Frame), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Level-up flow

        [Test]
        public void LevelUpFlow_TwoLevels_PausesShowsEachInOrder_ThenResumes()
        {
            var choice = Track(new GameObject("Choice")).AddComponent<RecordingLevelUpChoice>();
            var flow = Track(new GameObject("Flow")).AddComponent<LevelUpFlow>();
            flow.Setup(_xp, null, choice);
            var started = new List<int>();
            flow.Started += started.Add;

            _xp.AddXp(250); // level 3, two level-ups queued
            Assert.That(flow.PendingCount, Is.EqualTo(2));

            flow.Tick(0f);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "paused during the celebration");
            Assert.That(started, Is.EqualTo(new[] { 2 }));
            Assert.That(choice.Levels, Is.Empty, "choice opens after the celebration");
            flow.Tick(1f);
            Assert.That(choice.Levels, Is.EqualTo(new[] { 2 }));

            choice.Done();
            choice.Done(); // a double callback must not skip a level
            Assert.That(Time.timeScale, Is.EqualTo(0f), "stays paused: another level is queued");
            flow.Tick(1f); // gap -> level 3
            flow.Tick(1f); // celebration -> choice
            Assert.That(started, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(choice.Levels, Is.EqualTo(new[] { 2, 3 }));

            choice.Done();
            Assert.That(Time.timeScale, Is.EqualTo(1f), "resumes after the last pick");
            flow.Tick(1f);
            Assert.That(flow.IsBusy, Is.False);
            Assert.That(GamePause.IsPaused, Is.False);
        }
    }

    /// <summary>Test double for the KAN-19 skill screen: records the levels it was opened for, completes on demand.</summary>
    internal sealed class RecordingLevelUpChoice : LevelUpChoice
    {
        public readonly List<int> Levels = new List<int>();
        public Action Done;

        public override void Present(int level, Action onDone)
        {
            Levels.Add(level);
            Done = onDone;
        }
    }
}
