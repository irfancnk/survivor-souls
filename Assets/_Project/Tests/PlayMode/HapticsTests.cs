using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-12 acceptance (the parts a test can see; the tap itself needs a real phone): one pulse per kill, none for a
    /// non-lethal hit, a group kill in one frame gives one pulse, pulses 50 ms apart both fire, the Settings toggle
    /// silences everything, the editor backend is a silent no-op, and a kill allocates nothing.
    /// Kills are real <see cref="Enemy.TakeDamage"/> calls (the Enemy.Damaged event) heard by <see cref="KillHaptics"/>.
    /// </summary>
    public sealed class HapticsTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private bool _savedVibration;

        [SetUp]
        public void SetUp()
        {
            _savedVibration = GameSettings.Vibration;
            GameSettings.Vibration = true;
            Haptics.ResetState();
            Track(new GameObject("Game")).AddComponent<KillHaptics>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            GameSettings.Vibration = _savedVibration;
            Haptics.ResetState();
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

        private static void Kill(Enemy e) => e.TakeDamage(1e6f, Vector3.forward);

        [Test]
        public void Kill_FiresOnePulse()
        {
            Kill(SpawnEnemy(0f));
            Assert.That(Haptics.PulseCount, Is.EqualTo(1));
        }

        [Test]
        public void NonLethalHit_FiresNothing()
        {
            var e = SpawnEnemy(0f);
            e.TakeDamage(1f, Vector3.forward);
            Assert.That(e.IsAlive, Is.True);
            Assert.That(Haptics.PulseCount, Is.EqualTo(0));
        }

        [Test]
        public void GroupKillInOneFrame_FiresOnePulse()
        {
            for (int i = 0; i < 5; i++) Kill(SpawnEnemy(i));
            Assert.That(Haptics.PulseCount, Is.EqualTo(1));
        }

        [Test]
        public void RateLimit_CollapsesInsideWindow_AllowsAfter()
        {
            Assert.That(Haptics.Light(10f), Is.True);
            Assert.That(Haptics.Light(10f + Haptics.MinInterval * 0.5f), Is.False);
            Assert.That(Haptics.Light(10f + Haptics.MinInterval), Is.True);
            Assert.That(Haptics.PulseCount, Is.EqualTo(2));
        }

        [Test]
        public void VibrationOff_FiresNothing()
        {
            GameSettings.Vibration = false;
            Kill(SpawnEnemy(0f));
            Assert.That(Haptics.Light(100f), Is.False);
            Assert.That(Haptics.PulseCount, Is.EqualTo(0));
        }

        [Test]
        public void EditorBackend_IsSilentNoOp()
        {
            Assert.DoesNotThrow(() =>
            {
                Haptics.Warmup();
                Haptics.Light(200f);
            });
            Assert.That(Haptics.PulseCount, Is.EqualTo(1));
        }

        [Test]
        public void Kill_DoesNotAllocate()
        {
            var e = SpawnEnemy(0f);
            Kill(SpawnEnemy(1f)); // warm the path once
            Haptics.ResetState();
            Assert.That(() => Kill(e), Is.Not.AllocatingGCMemory());
            Assert.That(Haptics.PulseCount, Is.EqualTo(1));
        }
    }
}
