using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-13 acceptance: every hit shows a marker, a kill shows the kill marker, repeated hits restart one marker instead
    /// of stacking, the pool never grows, markers expire, and hits allocate nothing.
    /// Markers are driven by real <see cref="Enemy.TakeDamage"/> calls (the Enemy.Damaged event) and stepped with
    /// <see cref="HitMarkerHud.Tick"/>. The HUD has no panel here, so nothing is placed on screen; the state is what is tested.
    /// </summary>
    public sealed class HitMarkerTests
    {
        private const float Dt = 1f / 60f;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private HitMarkerHud _hud;

        [SetUp]
        public void SetUp()
        {
            _hud = Track(new GameObject("HUD")).AddComponent<HitMarkerHud>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _objects.Add(go);
            return go;
        }

        private Enemy SpawnEnemy(float x)
        {
            var enemy = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            enemy.Spawn(new Vector3(x, 0f, 5f), null, null);
            return enemy;
        }

        private void Step(float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) _hud.Tick(Dt);
        }

        [Test]
        public void Hit_ShowsWhiteMarker()
        {
            var e = SpawnEnemy(0f);
            e.TakeDamage(1f, Vector3.forward);
            Assert.That(_hud.ActiveCount, Is.EqualTo(1));
            Assert.That(_hud.TryGetMarker(e, out bool kill), Is.True);
            Assert.That(kill, Is.False);
        }

        [Test]
        public void KillingBlow_ShowsKillMarker()
        {
            var e = SpawnEnemy(0f);
            e.TakeDamage(e.MaxHealth, Vector3.forward);
            Assert.That(_hud.TryGetMarker(e, out bool kill), Is.True);
            Assert.That(kill, Is.True);
        }

        [Test]
        public void RepeatedHitsOnOneEnemy_ReuseOneMarker()
        {
            var e = SpawnEnemy(0f);
            for (int i = 0; i < 5; i++)
            {
                e.TakeDamage(1f, Vector3.forward);
                Step(0.05f);
            }
            Assert.That(_hud.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void HitThenKill_UpgradesSameMarker()
        {
            var e = SpawnEnemy(0f);
            e.TakeDamage(1f, Vector3.forward);
            e.TakeDamage(e.MaxHealth, Vector3.forward);
            Assert.That(_hud.ActiveCount, Is.EqualTo(1));
            Assert.That(_hud.TryGetMarker(e, out bool kill) && kill, Is.True);
        }

        [Test]
        public void MoreTargetsThanCapacity_PoolDoesNotGrow()
        {
            for (int i = 0; i < _hud.Capacity + 5; i++) SpawnEnemy(i).TakeDamage(1f, Vector3.forward);
            Assert.That(_hud.ActiveCount, Is.EqualTo(_hud.Capacity));
        }

        [Test]
        public void Markers_ExpireAfterLifetime_KillLastsLonger()
        {
            var hit = SpawnEnemy(0f);
            var killed = SpawnEnemy(2f);
            hit.TakeDamage(1f, Vector3.forward);
            killed.TakeDamage(killed.MaxHealth, Vector3.forward);
            Step(0.25f); // past the hit lifetime, inside the kill lifetime
            Assert.That(_hud.TryGetMarker(hit, out _), Is.False);
            Assert.That(_hud.TryGetMarker(killed, out _), Is.True);
            Step(0.3f);
            Assert.That(_hud.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void HitAndTick_DoNotAllocate()
        {
            var e = SpawnEnemy(0f);
            // Warm-up: first style writes create the inline style storage.
            e.TakeDamage(1f, Vector3.forward);
            Step(0.5f);
            e.TakeDamage(1f, Vector3.forward);
            _hud.Tick(Dt);

            Assert.That(() =>
            {
                e.TakeDamage(1f, Vector3.forward);
                _hud.Tick(Dt);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
