using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-39 acceptance (logic side; the look and the feel need screenshots and a phone): the survival clock counts scaled
    /// time only (stops in a GamePause and at timeScale 0), its length comes from the rules data, warnings fire once each
    /// in order (a debug jump does not replay them), it finishes exactly once at 0 and stops when the player dies, boss
    /// Chambers do not count; the portal is locked and blocks until the clock ends, then opens over the sequence length
    /// (frozen by a pause), lets the player in once, and a pooled portal enabled after the end is open at once;
    /// the HUD text format; camera shake decays to zero; nothing allocates per frame.
    /// </summary>
    public sealed class ChamberTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private SurvivalTimer _timer;
        private Transform _player;
        private PlayerHealth _health;
        private readonly List<int> _warnings = new List<int>();
        private int _finished;

        [SetUp]
        public void SetUp()
        {
            var playerGo = Track(new GameObject("Player"));
            _player = playerGo.transform;
            _player.position = new Vector3(0f, 0f, -20f);
            _health = playerGo.AddComponent<PlayerHealth>();
            _timer = NewTimer(ChamberRules.Create(600f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _warnings.Clear();
            _finished = 0;
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private SurvivalTimer NewTimer(ChamberRules rules)
        {
            if (_timer != null) Object.DestroyImmediate(_timer.gameObject);
            Track(rules);
            var timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            timer.Setup(rules, _player, _health);
            timer.enabled = false; // tests tick by hand unless they need Update
            timer.enabled = true;
            timer.WarningReached += s => _warnings.Add(s);
            timer.Finished += () => _finished++;
            return timer;
        }

        private PortalGate NewPortal(Vector3 position, out BoxCollider blocker)
        {
            var go = Track(new GameObject("Portal"));
            go.transform.position = position;
            var block = new GameObject("Door Block");
            block.transform.SetParent(go.transform, false);
            blocker = block.AddComponent<BoxCollider>();
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(go.transform, false);
            var shader = Shader.Find(WorldShaders.Portal);
            if (shader != null) quad.GetComponent<MeshRenderer>().sharedMaterial = Track(new Material(shader));
            var gate = go.AddComponent<PortalGate>();
            gate.Setup(quad.GetComponent<MeshRenderer>(), blocker, Vector3.zero, 1f);
            return gate;
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame) _timer.Tick(Frame);
        }

        [Test]
        public void Duration_ComesFromRules()
        {
            _timer = NewTimer(ChamberRules.Create(42f));
            Assert.That(_timer.Duration, Is.EqualTo(42f));
            Assert.That(_timer.Remaining, Is.EqualTo(42f));
            Assert.That(_timer.IsRunning, Is.True);
            Assert.That(TimerHud.Format(Mathf.CeilToInt(_timer.Remaining)), Is.EqualTo("00:42"));
        }

        [Test]
        public void CountsDown_ByScaledSeconds()
        {
            Run(10f);
            Assert.That(_timer.Remaining, Is.EqualTo(590f).Within(0.01f));
            Assert.That(_finished, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Pause_StopsTheClock()
        {
            float before = _timer.Remaining;
            GamePause.Acquire();
            for (int i = 0; i < 20; i++) yield return null;
            Assert.That(_timer.Remaining, Is.EqualTo(before), "level-up / pause must not eat survival time");
            GamePause.Release();
            yield return null;
            yield return null;
            Assert.That(_timer.Remaining, Is.LessThan(before), "clock runs again after the pause");
        }

        [Test]
        public void Warnings_FireOnceEach_InOrder()
        {
            _timer.SetRemaining(305f);
            Run(10f); // crosses 300
            Assert.That(_warnings, Is.EqualTo(new[] { 300 }));
            _timer.Tick(240f); // one huge step crosses 120 and 60
            Assert.That(_warnings, Is.EqualTo(new[] { 300, 120, 60 }));
            Run(5f);
            Assert.That(_warnings.Count, Is.EqualTo(3), "no repeats");
        }

        [Test]
        public void DebugJump_DoesNotReplaySkippedWarnings()
        {
            _timer.SetRemaining(10f);
            Assert.That(_warnings, Is.Empty);
            Assert.That(_timer.InFinalCountdown, Is.True);
            Run(9f);
            Assert.That(_warnings, Is.Empty);
            Assert.That(_finished, Is.Zero);
        }

        [Test]
        public void Finishes_ExactlyOnce_AtZero()
        {
            _timer.SetRemaining(1f);
            Run(0.5f);
            Assert.That(_finished, Is.Zero);
            Run(0.6f);
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_timer.Remaining, Is.EqualTo(0f));
            Assert.That(_timer.IsFinished, Is.True);
            Assert.That(_timer.IsRunning, Is.False);
            Run(2f);
            _timer.Complete();
            _timer.SetRemaining(5f);
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_timer.Remaining, Is.EqualTo(0f));
            Assert.That(TimerHud.Format(Mathf.CeilToInt(_timer.Remaining)), Is.EqualTo("00:00"));
        }

        [Test]
        public void PlayerDeath_StopsTheClock()
        {
            Run(1f);
            _health.TakeDamage(_health.Max);
            Assert.That(_health.IsDead, Is.True);
            float left = _timer.Remaining;
            Run(5f);
            Assert.That(_timer.Remaining, Is.EqualTo(left));
            Assert.That(_finished, Is.Zero);
        }

        [Test]
        public void BossChamber_HasNoClock_OpensOnComplete()
        {
            _timer = NewTimer(ChamberRules.Create(600f, condition: OpenCondition.KillBoss));
            Assert.That(_timer.UsesTimer, Is.False);
            Run(5f);
            Assert.That(_timer.Remaining, Is.EqualTo(600f));
            _timer.Complete(); // boss died
            Assert.That(_finished, Is.EqualTo(1));
        }

        [Test]
        public void Portal_LockedUntilTheClockEnds_ThenOpensOverTheSequence()
        {
            _timer = NewTimer(ChamberRules.Create(5f, openSequence: 2f));
            var portal = NewPortal(Vector3.zero, out var blocker);
            Assert.That(portal.State, Is.EqualTo(PortalState.Locked));
            Assert.That(blocker.enabled, Is.True);

            _player.position = portal.TriggerPosition; // standing in the arch while locked: nothing happens
            int entered = 0;
            System.Action<PortalGate> onEnter = _ => entered++;
            PortalGate.PlayerEntered += onEnter;
            try
            {
                for (int i = 0; i < 120; i++)
                {
                    _timer.Tick(Frame);
                    portal.Tick(Frame);
                }
                Assert.That(entered, Is.Zero);
                Assert.That(portal.State, Is.EqualTo(PortalState.Locked));

                _player.position = new Vector3(0f, 0f, -20f);
                for (int i = 0; i < 200 && !_timer.IsFinished; i++) _timer.Tick(Frame);
                portal.Tick(Frame); // sees the finished clock
                Assert.That(portal.State, Is.EqualTo(PortalState.Opening));
                Assert.That(blocker.enabled, Is.True, "cannot walk in during the sequence");

                for (int i = 0; i < 60; i++) portal.Tick(Frame);
                Assert.That(portal.Progress, Is.EqualTo(0.5f).Within(0.02f));
                for (int i = 0; i < 61; i++) portal.Tick(Frame);
                Assert.That(portal.State, Is.EqualTo(PortalState.Open));
                Assert.That(blocker.enabled, Is.False);

                portal.Tick(Frame);
                Assert.That(entered, Is.Zero, "player still far away");
                _player.position = portal.TriggerPosition + new Vector3(0.5f, 0f, 0.3f);
                portal.Tick(Frame);
                portal.Tick(Frame);
                Assert.That(entered, Is.EqualTo(1), "enters once");
            }
            finally
            {
                PortalGate.PlayerEntered -= onEnter;
            }
        }

        [Test]
        public void Portal_OpeningFreezesWithThePause()
        {
            _timer.Complete();
            var portal = NewPortal(Vector3.zero, out _);
            Assert.That(portal.State, Is.EqualTo(PortalState.Open), "enabled after the end: open at once");

            Object.DestroyImmediate(portal.gameObject);
            _timer = NewTimer(ChamberRules.Create(1f, openSequence: 2f));
            portal = NewPortal(Vector3.zero, out _);
            _timer.Complete();
            portal.Tick(Frame);
            portal.Tick(0.5f);
            float p = portal.Progress;
            GamePause.Acquire();
            portal.Tick(Time.deltaTime * Time.timeScale); // what Update passes while paused
            portal.Tick(0f);
            Assert.That(portal.Progress, Is.EqualTo(p));
            Assert.That(portal.State, Is.EqualTo(PortalState.Opening));
        }

        [Test]
        public void PooledPortal_ReEnabledAfterTheEnd_IsOpen()
        {
            var portal = NewPortal(Vector3.zero, out var blocker);
            portal.gameObject.SetActive(false); // tile released by the streamer
            _timer.Complete();
            portal.gameObject.SetActive(true);  // tile streamed in again
            Assert.That(portal.State, Is.EqualTo(PortalState.Open));
            Assert.That(blocker.enabled, Is.False);
            Assert.That(portal.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void HudText_Format()
        {
            Assert.That(TimerHud.Format(600), Is.EqualTo("10:00"));
            Assert.That(TimerHud.Format(Mathf.CeilToInt(599.2f)), Is.EqualTo("10:00"));
            Assert.That(TimerHud.Format(Mathf.CeilToInt(9.01f)), Is.EqualTo("00:10"));
            Assert.That(TimerHud.Format(61), Is.EqualTo("01:01"));
            Assert.That(TimerHud.Format(-3), Is.EqualTo("00:00"));
            Assert.That(TimerHud.Format(125), Is.SameAs(TimerHud.Format(125)), "cached");
            Assert.That(TimerHud.WarningText(300), Is.EqualTo("5 MINUTES REMAIN"));
            Assert.That(TimerHud.WarningText(60), Is.EqualTo("1 MINUTE REMAINS"));
            Assert.That(TimerHud.WarningText(30), Is.EqualTo("30 SECONDS REMAIN"));
        }

        [UnityTest]
        public IEnumerator CameraShake_DecaysToZero()
        {
            var camGo = Track(new GameObject("Cam"));
            camGo.AddComponent<Camera>();
            var follow = camGo.AddComponent<TopDownFollowCamera>();
            follow.Target = _player;
            yield return null;
            follow.Shake(0.2f, 0.3f);
            Assert.That(follow.ShakeOffset.magnitude, Is.LessThanOrEqualTo(0.2f * 1.8f));
            float end = Time.unscaledTime + 0.45f;
            while (Time.unscaledTime < end) yield return null;
            Assert.That(follow.ShakeOffset, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Shaders_Exist()
        {
            Assert.That(Shader.Find(WorldShaders.Portal), Is.Not.Null);
            Assert.That(Shader.Find(WorldShaders.PortalGlow), Is.Not.Null);
        }

        [Test]
        public void Frame_DoesNotAllocate()
        {
            _timer = NewTimer(ChamberRules.Create(5f, openSequence: 2f));
            var portal = NewPortal(Vector3.zero, out _);
            _timer.Tick(Frame);
            portal.Tick(Frame);
            TimerHud.Format(5);
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    _timer.Tick(Frame);
                    portal.Tick(Frame);
                    TimerHud.Format(Mathf.CeilToInt(_timer.Remaining));
                }
            }, Is.Not.AllocatingGCMemory());
        }

#if UNITY_EDITOR
        [Test]
        public void ShippedRules_AreTenMinutes()
        {
            var rules = UnityEditor.AssetDatabase.LoadAssetAtPath<ChamberRules>("Assets/_Project/Data/Chamber/ChamberRules_Realm1.asset");
            Assert.That(rules, Is.Not.Null);
            Assert.That(rules.OpenCondition, Is.EqualTo(OpenCondition.Survive));
            Assert.That(rules.SurvivalSeconds, Is.EqualTo(600f));
            Assert.That(rules.Warnings, Is.EquivalentTo(new[] { 300, 120, 60 }));
        }
#endif

        /// <summary>Shader names used by the portal (kept here so the tests do not reference the editor assembly).</summary>
        private static class WorldShaders
        {
            public const string Portal = "SurvivorSouls/Portal";
            public const string PortalGlow = "SurvivorSouls/PortalGlow";
        }
    }
}
