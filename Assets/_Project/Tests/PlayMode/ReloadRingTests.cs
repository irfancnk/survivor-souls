using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-42 acceptance (logic side; colour on snow and the feel need the device): every volley fires in the frame the
    /// sweep ends (0 frame error, also with the frame-time overshoot carried), long-run fire rate matches the attack
    /// interval, an attack speed change mid-sweep keeps the fraction (no jump) and speeds up the rest, a multishot volley is
    /// one sweep, no target / moving = calm full ring with no empty sweeps, pause freezes the sweep, flash and pulse,
    /// the Settings switch hides the ring, the shader exists, and a frame allocates nothing.
    /// </summary>
    public sealed class ReloadRingTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private PlayerMover _mover;
        private AutoShooter _shooter;
        private ReloadRing _ring;
        private MeshRenderer _quad;
        private int _fired, _completed;
        private bool _ringSetting;

        [SetUp]
        public void SetUp()
        {
            _ringSetting = GameSettings.ReloadRing;
            GameSettings.ReloadRing = true;
            Random.InitState(4242);

            _player = Track(new GameObject("Player"));
            _player.SetActive(false); // Awake after every component exists
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _mover = _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            _ring = _player.AddComponent<ReloadRing>();

            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quadGo.GetComponent<Collider>());
            quadGo.transform.SetParent(_player.transform, false);
            _quad = quadGo.GetComponent<MeshRenderer>();
            var shader = Shader.Find("SurvivorSouls/ReloadRing");
            if (shader != null) _quad.sharedMaterial = Track(new Material(shader));

            _player.SetActive(true);
            _ring.Setup(_shooter, _mover, _quad);
            _shooter.enabled = false; // most tests step exact frames by hand
            _ring.enabled = true;

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());

            _shooter.Fired += (_, __) => _fired++;
            _shooter.ReloadCompleted += () => _completed++;
        }

        [TearDown]
        public void TearDown()
        {
            FloatingJoystick.SimulatedValue = null;
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _fired = _completed = 0;
            GamePause.Clear();
            Time.timeScale = 1f;
            GameSettings.ReloadRing = _ringSetting;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy SpawnEnemy(Vector3 position)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { });
            return e;
        }

        /// <summary>One game frame in Update/LateUpdate order: shooter, then the ring draws.</summary>
        private void Step(float dt = Frame)
        {
            _shooter.Tick(dt);
            _ring.Tick(dt);
        }

        // ------------------------------------------------------------ Timing

        [Test]
        public void Volley_FiresInTheFrameTheSweepEnds()
        {
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            float interval = _shooter.CurrentInterval;
            Step(); // ready at spawn: first volley at once
            Assert.That(_fired, Is.EqualTo(1));

            int shotsBefore = _fired, sweepEnds = _completed;
            float last = -1f;
            for (int frame = 0; frame < 610; frame++) // 10.17 s
            {
                float before = _shooter.ReloadProgress;
                Step();
                bool fired = _fired != shotsBefore, ended = _completed != sweepEnds;
                Assert.That(fired, Is.EqualTo(ended), $"frame {frame}: fire and sweep end must share the frame");
                if (!fired)
                {
                    Assert.That(_shooter.ReloadProgress, Is.GreaterThan(before), "the sweep only grows between shots");
                    Assert.That(_ring.ShownProgress, Is.EqualTo(_shooter.ReloadProgress), "the ring draws the shooter's value");
                }
                shotsBefore = _fired;
                sweepEnds = _completed;
                if (fired) last = (frame + 1) * Frame;
            }
            // 10 s at 1 shot/s: overshoot is carried, so the rate does not drift with the frame time.
            Assert.That(_fired - 1, Is.EqualTo(10));
            Assert.That(last, Is.EqualTo(10f * interval).Within(Frame + 1e-4f));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Assert.That(_ring.EarlyShots, Is.Zero);
#endif
        }

        [Test]
        public void Volley_UnevenFrameTimes_StillEndsWithTheSweep()
        {
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            Step();
            float[] dts = { 0.011f, 0.033f, 0.016f, 0.05f, 0.008f };
            int prevFired = _fired, prevDone = _completed;
            for (int frame = 0; frame < 400; frame++)
            {
                Step(dts[frame % dts.Length]);
                Assert.That(_fired - prevFired, Is.EqualTo(_completed - prevDone));
                prevFired = _fired;
                prevDone = _completed;
            }
            Assert.That(_fired, Is.GreaterThan(5));
        }

        [Test]
        public void AttackSpeedChange_MidSweep_KeepsFractionAndSpeedsUp()
        {
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            Step(); // volley 1
            for (int i = 0; i < 30; i++) Step(); // half way at 1 s interval
            float mid = _shooter.ReloadProgress;
            Assert.That(mid, Is.EqualTo(0.5f).Within(0.02f));

            var quick = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            _stats.SetSource(quick, new[] { new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, 1f) }, 1); // 2 shots/s
            Assert.That(_shooter.CurrentInterval, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(_shooter.ReloadProgress, Is.EqualTo(mid), "changing the stat must not move the arc");

            Step();
            Assert.That(_shooter.ReloadProgress - mid, Is.EqualTo(Frame / 0.5f).Within(1e-4f), "same frame: twice the rate");

            // The other half at 0.5 s takes 0.25 s = 15 frames (one already done).
            int frames = 1;
            int fired = _fired;
            while (_fired == fired && frames < 100) { Step(); frames++; }
            Assert.That(frames, Is.InRange(15, 16));
            Assert.That(_completed, Is.EqualTo(_fired - 1), "the faster sweep still ends with its shot (volley 1 was ready at spawn)");

            // Next full sweep runs at the new interval: 30 frames.
            fired = _fired;
            frames = 0;
            while (_fired == fired && frames < 100) { Step(); frames++; }
            Assert.That(frames, Is.InRange(29, 31));
        }

        [Test]
        public void MultishotVolley_IsOneSweep()
        {
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            var fan = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            _stats.SetSource(fan, new[] { new StatModifier(StatId.Multishot, ModifierKind.Flat, 2f) }, 1);
            for (int i = 0; i < 130; i++) Step(); // volleys at t = 0, 1 s, 2 s (2.15 s run)
            Assert.That(_fired, Is.EqualTo(3), "Fired is raised once per volley");
            int bolts = 0;
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b.gameObject.scene.IsValid() && b.InFlight) bolts++;
            Assert.That(bolts, Is.GreaterThanOrEqualTo(3), "a volley is 3 bolts");
            Assert.That(_completed, Is.EqualTo(2), "one sweep per volley (the first volley was ready at spawn)");
        }

        // ------------------------------------------------------------ States

        [Test]
        public void NoTarget_CalmFullRing_NoEmptySweeps()
        {
            for (int i = 0; i < 180; i++) Step();
            Assert.That(_fired, Is.Zero);
            Assert.That(_completed, Is.Zero, "a ready ring does not run empty sweeps");
            Assert.That(_shooter.Engaged, Is.False);
            Assert.That(_ring.ShownProgress, Is.EqualTo(1f));
            Assert.That(_ring.Calm, Is.EqualTo(1f));
        }

        [Test]
        public void TargetAppears_SweepStartsWithTheFirstShot()
        {
            for (int i = 0; i < 60; i++) Step();
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            Step();
            Assert.That(_fired, Is.EqualTo(1), "ready ring fires at once when a target is lined up");
            Assert.That(_shooter.Engaged, Is.True);
            Assert.That(_ring.ShownProgress, Is.LessThan(0.05f), "the sweep starts again after the volley");
            for (int i = 0; i < 30; i++) Step();
            Assert.That(_ring.Calm, Is.Zero, "engaged arc is at full brightness");
        }

        [Test]
        public void ReadyFlashAndFirePulse_DecayOnTheirOwnTime()
        {
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            Step();
            Assert.That(_ring.Pulse, Is.GreaterThan(0.85f), "pulse on the shot");
            for (int i = 0; i < 12; i++) Step(); // 0.2 s > 0.16 s pulse
            Assert.That(_ring.Pulse, Is.Zero);
            while (_fired < 2) Step();
            Assert.That(_ring.Flash, Is.GreaterThan(0.8f), "flash when the sweep completes");
            for (int i = 0; i < 7; i++) Step(); // 0.117 s > 0.1 s flash
            Assert.That(_ring.Flash, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Moving_ShowsCalmRing_AndDoesNotShoot()
        {
            var stick = Track(new GameObject("Joystick")).AddComponent<FloatingJoystick>();
            _mover.SetJoystick(stick);
            FloatingJoystick.SimulatedValue = new Vector2(0f, 1f);
            SpawnEnemy(new Vector3(3f, 0f, -3f));
            _shooter.enabled = true;
            for (int i = 0; i < 40; i++) yield return null;
            Assert.That(_mover.Velocity.magnitude, Is.GreaterThan(1f));
            Assert.That(_fired, Is.Zero, "move to dodge: no shot");
            Assert.That(_shooter.Engaged, Is.False);
            Assert.That(_ring.Calm, Is.GreaterThan(0.9f));
        }

        [UnityTest]
        public IEnumerator Pause_FreezesTheSweep()
        {
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            _shooter.enabled = true;
            float t = 0f;
            while (_fired == 0 && t < 2f) { t += Time.unscaledDeltaTime; yield return null; }
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(_fired, Is.EqualTo(1));

            GamePause.Acquire();
            yield return null;
            float progress = _shooter.ReloadProgress, shown = _ring.ShownProgress, pulse = _ring.Pulse;
            for (int i = 0; i < 20; i++) yield return null;
            Assert.That(_shooter.ReloadProgress, Is.EqualTo(progress), "level-up / pause screen stops the sweep");
            Assert.That(_ring.ShownProgress, Is.EqualTo(shown));
            Assert.That(_ring.Pulse, Is.EqualTo(pulse));
            Assert.That(_fired, Is.EqualTo(1));

            GamePause.Release();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(_shooter.ReloadProgress, Is.GreaterThan(progress), "resumes from the same progress");
        }

        // ------------------------------------------------------------ Settings, assets, cost

        [Test]
        public void SettingsSwitch_HidesAndShowsTheRing()
        {
            Assert.That(_ring.Visible, Is.True);
            GameSettings.ReloadRing = false;
            Assert.That(_ring.Visible, Is.False);
            GameSettings.ReloadRing = true;
            Assert.That(_ring.Visible, Is.True);
        }

        [Test]
        public void Ring_IsWorldAligned_WhenTheKnightTurns()
        {
            _player.transform.rotation = Quaternion.Euler(0f, 130f, 0f);
            Step();
            Assert.That(Vector3.Angle(_quad.transform.up, Vector3.forward), Is.LessThan(0.01f), "12 o'clock stays at screen top");
            Assert.That(Vector3.Angle(-_quad.transform.forward, Vector3.up), Is.LessThan(0.01f), "flat, facing up");
        }

        [Test]
        public void Shader_Exists()
        {
            Assert.That(Shader.Find("SurvivorSouls/ReloadRing"), Is.Not.Null);
        }

        [Test]
        public void Frame_DoesNotAllocate()
        {
            SpawnEnemy(new Vector3(0f, 0f, 5f));
            Step(); // first volley (pool fill allocates once)
            Step();
            Assert.That(() => Step(), Is.Not.AllocatingGCMemory());
        }
    }
}
