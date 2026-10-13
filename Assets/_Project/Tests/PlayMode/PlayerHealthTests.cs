using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-18 acceptance: contact damage rate, stacking, range exit, death exactly once, no allocations.
    /// Steps <see cref="ContactDamageReceiver.Tick"/> directly with a fixed delta, so results do not depend on frame time.
    /// </summary>
    public sealed class PlayerHealthTests
    {
        private const float Dt = 1f / 60f;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private PlayerHealth _health;
        private ContactDamageReceiver _receiver;

        [SetUp]
        public void SetUp()
        {
            var player = Track(new GameObject("Player"));
            _health = player.AddComponent<PlayerHealth>();
            _receiver = player.AddComponent<ContactDamageReceiver>();
            _receiver.enabled = false; // stepped by the test only
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

        /// <summary>Spawns an enemy <paramref name="distance"/> m from the player at <paramref name="angle"/> degrees.</summary>
        private Enemy SpawnEnemy(float distance, float angle)
        {
            var enemy = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            Vector3 pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
            enemy.Spawn(pos, _health.transform, null); // [Test] runs in one frame: Enemy.Update never moves it
            return enemy;
        }

        private float Drain(int frames)
        {
            float before = _health.Current;
            for (int i = 0; i < frames; i++) _receiver.Tick(Dt);
            return before - _health.Current;
        }

        [Test]
        public void OneEnemyInRange_DrainsAtItsDps()
        {
            var e = SpawnEnemy(1f, 0f);
            float lost = Drain(60);
            Assert.That(lost, Is.EqualTo(e.DamagePerSecond).Within(0.01f));
            Assert.That(_receiver.EnemiesInRange, Is.EqualTo(1));
        }

        [Test]
        public void ThreeEnemiesInRange_DrainThreeTimesFaster()
        {
            var e = SpawnEnemy(1f, 0f);
            SpawnEnemy(1f, 120f);
            SpawnEnemy(1f, 240f);
            float lost = Drain(60);
            Assert.That(lost, Is.EqualTo(3f * e.DamagePerSecond).Within(0.01f));
            Assert.That(_receiver.EnemiesInRange, Is.EqualTo(3));
        }

        [Test]
        public void MovingOutOfRange_StopsDamage()
        {
            var e = SpawnEnemy(1f, 0f);
            Drain(30);
            e.transform.position = Vector3.forward * (e.ContactRange + 0.05f);
            Assert.That(Drain(60), Is.EqualTo(0f));
            Assert.That(_receiver.EnemiesInRange, Is.EqualTo(0));
        }

        [Test]
        public void DeathFiresOnceAtExactlyZero()
        {
            int deaths = 0;
            _health.OnDeath += () => deaths++;
            var e = SpawnEnemy(1f, 0f);
            SpawnEnemy(1f, 180f);
            int frames = Mathf.CeilToInt(_health.Max / (2f * e.DamagePerSecond) / Dt) + 120; // past the lethal frame
            Drain(frames);
            _health.TakeDamage(50f);
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(0f));
            Assert.That(_health.IsDead, Is.True);
        }

        [Test]
        public void Heal_ClampsToMax_AndIsIgnoredWhenDead()
        {
            _health.TakeDamage(100f);
            Assert.That(_health.Heal(500f), Is.EqualTo(100f));
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
            _health.TakeDamage(_health.Max);
            Assert.That(_health.Heal(50f), Is.EqualTo(0f));
            Assert.That(_health.Current, Is.EqualTo(0f));
        }

        [Test]
        public void DamageLoop_DoesNotAllocate()
        {
            for (int i = 0; i < 6; i++) SpawnEnemy(1f, i * 60f);
            _receiver.Tick(Dt); // warm up
            Assert.That(() => _receiver.Tick(Dt), Is.Not.AllocatingGCMemory());
        }
    }
}
