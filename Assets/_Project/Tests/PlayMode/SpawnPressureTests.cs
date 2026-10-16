using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-113 acceptance: spawn pressure from the power index (max alive and burst size up, intervals down, clamped);
    /// pressure never lifts max alive past the phone budget and never cuts the table's cap; the spawner refills faster
    /// under pressure and nothing changes without it (old behaviour); the live scaler pushes the factors with the power;
    /// the shipped asset keeps max alive inside 50 in every Chamber of Realm 1.
    /// </summary>
    public sealed class SpawnPressureTests
    {
        private sealed class OpenGround : IWalkability
        {
            public bool IsBlocked(Vector3 position, float radius) => false;
        }

        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            Random.InitState(1130);
            HordeTierMaterials.Clear();
            Enemy.Active.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            HordeTierMaterials.Clear();
            Enemy.Active.Clear();
            Enemy.Walkability = null;
            Enemy.LandMaxZ = float.PositiveInfinity;
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static HordeScaling Pressure(HordeScaling s) => s.SetPressure(0.5f, 1.6f, 0.25f, 0.7f, 50);

        // ------------------------------------------------------------ Rules

        [Test]
        public void Factors_FollowPowerAndClamp()
        {
            var s = Track(Pressure(HordeScaling.Create()));
            Assert.That(s.CapFactor(1f), Is.EqualTo(1f));
            Assert.That(s.IntervalFactor(1f), Is.EqualTo(1f));
            Assert.That(s.CapFactor(0.2f), Is.EqualTo(1f), "a debuff never thins the horde");
            Assert.That(s.IntervalFactor(0.2f), Is.EqualTo(1f));
            Assert.That(s.CapFactor(2f), Is.EqualTo(Mathf.Sqrt(2f)).Within(1e-4f), "2 ^ 0.5");
            Assert.That(s.IntervalFactor(2f), Is.EqualTo(Mathf.Pow(2f, -0.25f)).Within(1e-4f), "2 ^ -0.25");
            Assert.That(s.CapFactor(16f), Is.EqualTo(1.6f).Within(1e-4f), "capped (16 ^ 0.5 = 4)");
            Assert.That(s.IntervalFactor(64f), Is.EqualTo(0.7f).Within(1e-4f), "floored (64 ^ -0.25 = 0.35)");
        }

        [Test]
        public void Create_HasNoPressure()
        {
            var s = Track(HordeScaling.Create());
            Assert.That(s.CapFactor(9f), Is.EqualTo(1f), "older tests and tools keep the table's caps");
            Assert.That(s.IntervalFactor(9f), Is.EqualTo(1f));
        }

        [Test]
        public void MaxAlive_InsideTheBudgetNeverBelowTheTable()
        {
            var s = Track(Pressure(HordeScaling.Create()));
            Assert.That(s.PressuredMaxAlive(24, 1.5f), Is.EqualTo(36));
            Assert.That(s.PressuredMaxAlive(40, 1.6f), Is.EqualTo(50), "64 -> phone budget");
            Assert.That(s.PressuredMaxAlive(60, 1.6f), Is.EqualTo(60), "the table's own cap is never cut");
            Assert.That(s.PressuredMaxAlive(24, 0.5f), Is.EqualTo(24), "below 1 = no pressure");
        }

        // ------------------------------------------------------------ Spawner

        private Camera Camera()
        {
            var cam = Track(new GameObject("Cam")).AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.aspect = 9f / 19.5f;
            var rotation = Quaternion.Euler(55f, 0f, 0f);
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2.2f) - rotation * Vector3.forward * 30f, rotation);
            return cam;
        }

        private Enemy Zombie()
        {
            var template = Track(new GameObject("ZombieTemplate"));
            template.SetActive(false);
            var body = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            body.transform.SetParent(template.transform, false);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            var renderer = body.GetComponent<MeshRenderer>();
            var m = Track(new Material(Shader.Find("Universal Render Pipeline/Simple Lit")));
            renderer.sharedMaterial = m;
            var enemy = template.AddComponent<Enemy>();
            typeof(Enemy).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(enemy, renderer);
            return enemy;
        }

        /// <summary>One phase: 10 alive, a group of 1 every 1 s, a burst of 4 every 5 s.</summary>
        private EnemySpawner Spawner(int maxAlive = 10)
        {
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(0f, 40, new[]
            {
                new EnemyWaveTable.Phase
                {
                    maxAlive = maxAlive, spawnInterval = 1f, groupSize = new Vector2Int(1, 1), burstInterval = 5f, burstSize = 4,
                    enemies = new[] { new EnemyWaveTable.EnemyEntry { prefab = Zombie(), weight = 1f } },
                },
            });
            var spawner = Track(new GameObject("Spawner")).AddComponent<EnemySpawner>();
            spawner.Configure(table, Track(new GameObject("Target")).transform, Camera(), new OpenGround());
            return spawner;
        }

        private static int SpawnedIn(EnemySpawner spawner, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.05f) spawner.Tick(0.05f);
            return spawner.Stats.Spawned;
        }

        [Test]
        public void Spawner_MaxAliveFollowsPressure()
        {
            var s = Track(Pressure(HordeScaling.Create()));
            var spawner = Spawner(maxAlive: 20);
            var phase = spawner.Waves.GetPhase(0);
            Assert.That(spawner.MaxAliveOf(phase), Is.EqualTo(20));
            spawner.SetDifficulty(1.2f, 1f, 1f);
            spawner.SetHordeScale(s, 1f);
            spawner.SetHordePressure(1.5f, 0.8f);
            Assert.That(spawner.MaxAliveOf(phase), Is.EqualTo(36), "20 x Chamber 1.2 x pressure 1.5");
            spawner.SetHordePressure(3f, 0.8f);
            Assert.That(spawner.MaxAliveOf(phase), Is.EqualTo(50), "phone budget");
            spawner.SetHordeScale(null, 1f);
            Assert.That(spawner.MaxAliveOf(phase), Is.EqualTo(24), "no scaling asset: the Chamber cap only");
        }

        [Test]
        public void Spawner_RefillsFasterUnderPressure()
        {
            var calm = Spawner();
            int calmCount = SpawnedIn(calm, 12f);
            foreach (var e in new List<Enemy>(Enemy.Active)) e.gameObject.SetActive(false);
            Enemy.Active.Clear();

            var s = Track(Pressure(HordeScaling.Create()));
            var pressured = Spawner();
            pressured.SetHordeScale(s, 1f);
            pressured.SetHordePressure(1.5f, 0.5f);
            int pressuredCount = SpawnedIn(pressured, 12f);
            Assert.That(calmCount, Is.EqualTo(10), "calm: capped at 10 alive (nobody dies here)");
            Assert.That(pressuredCount, Is.EqualTo(15), "pressure 1.5: capped at 15 alive");
            Assert.That(pressured.PressureCapFactor, Is.EqualTo(1.5f));
            Assert.That(pressured.PressureIntervalFactor, Is.EqualTo(0.5f));
        }

        [Test]
        public void Spawner_NoPressureIsTheOldBehaviour()
        {
            var spawner = Spawner();
            Assert.That(spawner.PressureCapFactor, Is.EqualTo(1f));
            Assert.That(spawner.PressureIntervalFactor, Is.EqualTo(1f));
            spawner.SetHordePressure(0.3f, 4f);
            Assert.That(spawner.PressureCapFactor, Is.EqualTo(1f), "never thins the horde");
            Assert.That(spawner.PressureIntervalFactor, Is.EqualTo(1f), "never slows the refill");
            Assert.That(SpawnedIn(spawner, 12f), Is.EqualTo(10), "calm cap");
        }

        [Test]
        public void Spawner_IntervalPressureSpeedsTheGroups()
        {
            var calm = Spawner(maxAlive: 40);
            int calmCount = SpawnedIn(calm, 3.25f); // groups at 0, 1, 2, 3 s; first burst at 5 s
            foreach (var e in new List<Enemy>(Enemy.Active)) e.gameObject.SetActive(false);
            Enemy.Active.Clear();

            var s = Track(Pressure(HordeScaling.Create()));
            var spawner = Spawner(maxAlive: 40);
            spawner.SetHordeScale(s, 1f);
            spawner.SetHordePressure(1f, 0.5f);
            int count = SpawnedIn(spawner, 3.25f); // groups every 0.5 s (7) + the first burst at 2.5 s (4)
            Assert.That(calmCount, Is.InRange(3, 4));
            Assert.That(count, Is.GreaterThanOrEqualTo(2 * calmCount), $"interval x0.5: {count} vs {calmCount}");
            Assert.That(count, Is.LessThanOrEqualTo(11));
        }

        // ------------------------------------------------------------ Live scaler

        [Test]
        public void Scaler_PushesPressureWithThePower()
        {
            var s = Track(Pressure(HordeScaling.Create(exponent: 0.5f, time: 0f, max: 10f)));
            var spawner = Spawner();
            var stats = Track(new GameObject("Stats")).AddComponent<PlayerStats>();
            var health = Track(new GameObject("Knight")).AddComponent<SurvivorSouls.Gameplay.Player.PlayerHealth>();
            var timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            timer.Setup(Track(ChamberRules.Create(120f)), health.transform, health);
            var scaler = timer.gameObject.AddComponent<HordeScaler>();
            scaler.Setup(s, spawner, stats, timer);
            Assert.That(scaler.CapFactor, Is.EqualTo(1f));
            Assert.That(spawner.PressureCapFactor, Is.EqualTo(1f));

            stats.SetSource(s, new[] { new StatModifier(StatId.Damage, ModifierKind.Percent, 1f) }, 1); // x2
            scaler.Refresh();
            Assert.That(scaler.Power, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(spawner.PressureCapFactor, Is.EqualTo(s.CapFactor(2f)).Within(1e-4f));
            Assert.That(spawner.PressureIntervalFactor, Is.EqualTo(s.IntervalFactor(2f)).Within(1e-4f));
            Assert.That(scaler.IntervalFactor, Is.LessThan(1f));

            scaler.PowerOverride = 100f;
            scaler.Refresh();
            Assert.That(spawner.PressureCapFactor, Is.EqualTo(1.6f).Within(1e-4f), "debug power override drives pressure too");
            Assert.That(() => scaler.Refresh(), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Shipped data

        [Test]
        public void ShippedPressure_InsideThePhoneBudget()
        {
#if UNITY_EDITOR
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<HordeScaling>("Assets/_Project/Data/Combat/HordeScaling_Realm1.asset");
            var waves = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyWaveTable>("Assets/_Project/Data/Combat/EnemyWaves_Realm1.asset");
            var difficulty = UnityEditor.AssetDatabase.LoadAssetAtPath<ChamberDifficulty>("Assets/_Project/Data/Chamber/ChamberDifficulty_Realm1.asset");
            Assert.That(s, Is.Not.Null);
            Assert.That(waves, Is.Not.Null);
            Assert.That(difficulty, Is.Not.Null);
            Assert.That(s.MaxAliveBudget, Is.LessThanOrEqualTo(50), "KAN-44 phone budget");
            Assert.That(s.CapFactor(4f), Is.GreaterThan(1.1f), "a strong build clearly raises the field");
            Assert.That(s.IntervalFactor(4f), Is.LessThan(0.9f), "and the refill rate");
            float strongest = s.CapFactor(1000f);
            for (int c = 1; c <= difficulty.ChambersInRealm; c++)
                for (int i = 0; i < waves.PhaseCount; i++)
                {
                    int table = Mathf.RoundToInt(waves.GetPhase(i).maxAlive * difficulty.CapMultiplier(c));
                    int pressured = s.PressuredMaxAlive(table, strongest);
                    Assert.That(pressured, Is.LessThanOrEqualTo(Mathf.Max(table, 50)), $"Chamber {c} phase {i}");
                }
#else
            Assert.Ignore("Editor only");
#endif
        }
    }
}
