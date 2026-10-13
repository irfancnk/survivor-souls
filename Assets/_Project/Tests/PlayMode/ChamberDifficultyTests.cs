using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-46 (KAN-40.2) acceptance: Chamber 1 plays the wave table as written; later Chambers scale enemy health, wave
    /// caps and spawn pace (clamped); the type rule (boss every 5th and the last, mini boss rule, boss wins); the layout
    /// seed differs per Chamber, run and Realm and is stable for the same inputs; a pinned seed wins; the spawner applies
    /// the multipliers (max health of each spawn, alive cap, prewarm); the world takes the seed before or after Awake;
    /// the real Game scene wires it all from <see cref="RunState"/>.
    /// Uses Realm <see cref="TestRealm"/> so the editor's real Realm 1 progress is never touched.
    /// </summary>
    public sealed class ChamberDifficultyTests
    {
        private const int TestRealm = 97;
        private const string LastRealmKey = "progress.lastRealm";

        private sealed class OpenGround : IWalkability
        {
            public bool IsBlocked(Vector3 position, float radius) => false;
        }

        private readonly List<Object> _objects = new List<Object>();
        private int _savedLastRealm;
        private bool _hadLastRealm;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(4601);
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            RunProgress.Clear(TestRealm);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Walkability = null;
            Enemy.LandMaxZ = float.PositiveInfinity;
            GamePause.Clear();
            Time.timeScale = 1f;
            RunProgress.Clear(TestRealm);
            if (_hadLastRealm) PlayerPrefs.SetInt(LastRealmKey, _savedLastRealm);
            else PlayerPrefs.DeleteKey(LastRealmKey);
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        // ------------------------------------------------------------ Rules

        [Test]
        public void ChamberOne_PlaysTheTableAsWritten()
        {
            var d = Track(ChamberDifficulty.Create());
            var plan = d.Plan(1, 1, 12345);
            Assert.That(plan.HealthMultiplier, Is.EqualTo(1f));
            Assert.That(plan.CapMultiplier, Is.EqualTo(1f));
            Assert.That(plan.IntervalMultiplier, Is.EqualTo(1f));
            Assert.That(plan.Type, Is.EqualTo(ChamberType.Normal));
            Assert.That(plan.IsLast, Is.False);
        }

        [Test]
        public void Multipliers_GrowPerChamber_AndClamp()
        {
            var d = Track(ChamberDifficulty.Create(chambers: 100, health: 0.15f, maxHealth: 4f, cap: 0.05f, maxCap: 1.35f,
                interval: 0.96f, minInterval: 0.7f));
            Assert.That(d.HealthMultiplier(3), Is.EqualTo(1.3f).Within(1e-4f));
            Assert.That(d.CapMultiplier(3), Is.EqualTo(1.1f).Within(1e-4f));
            Assert.That(d.IntervalMultiplier(3), Is.EqualTo(0.96f * 0.96f).Within(1e-4f));

            for (int c = 2; c <= 100; c++)
            {
                Assert.That(d.HealthMultiplier(c), Is.GreaterThanOrEqualTo(d.HealthMultiplier(c - 1)), $"health never drops (Chamber {c})");
                Assert.That(d.CapMultiplier(c), Is.GreaterThanOrEqualTo(d.CapMultiplier(c - 1)), $"cap never drops (Chamber {c})");
                Assert.That(d.IntervalMultiplier(c), Is.LessThanOrEqualTo(d.IntervalMultiplier(c - 1)), $"pace never slows (Chamber {c})");
            }
            Assert.That(d.HealthMultiplier(100), Is.EqualTo(4f));
            Assert.That(d.CapMultiplier(100), Is.EqualTo(1.35f));
            Assert.That(d.IntervalMultiplier(100), Is.EqualTo(0.7f));
        }

        [Test]
        public void TypeRule_BossEveryFifthAndLast_MiniBossRule_BossWins()
        {
            var d = Track(ChamberDifficulty.Create(chambers: 10, boss: 5));
            for (int c = 1; c <= 10; c++)
            {
                var expected = c == 5 || c == 10 ? ChamberType.Boss : ChamberType.Normal;
                Assert.That(d.TypeOf(c), Is.EqualTo(expected), $"Chamber {c}");
                Assert.That(d.Plan(1, c, 1).IsLast, Is.EqualTo(c == 10), $"Chamber {c} last");
            }

            var mini = Track(ChamberDifficulty.Create(chambers: 20, boss: 5, miniBoss: 3));
            Assert.That(mini.TypeOf(3), Is.EqualTo(ChamberType.MiniBoss));
            Assert.That(mini.TypeOf(6), Is.EqualTo(ChamberType.MiniBoss));
            Assert.That(mini.TypeOf(15), Is.EqualTo(ChamberType.Boss), "boss wins over mini boss");
            Assert.That(mini.TypeOf(4), Is.EqualTo(ChamberType.Normal));

            var lastOnly = Track(ChamberDifficulty.Create(chambers: 8, boss: 0));
            for (int c = 1; c < 8; c++) Assert.That(lastOnly.TypeOf(c), Is.EqualTo(ChamberType.Normal), $"Chamber {c}");
            Assert.That(lastOnly.TypeOf(8), Is.EqualTo(ChamberType.Boss));
        }

        [Test]
        public void LayoutSeed_DiffersPerChamberRunAndRealm_StableForSameInputs()
        {
            var d = Track(ChamberDifficulty.Create());
            var seen = new HashSet<int>();
            for (int c = 1; c <= 10; c++)
            {
                int seed = d.LayoutSeed(777, 1, c);
                Assert.That(seed, Is.Not.EqualTo(0));
                Assert.That(seen.Add(seed), Is.True, $"Chamber {c} has its own layout");
                Assert.That(d.LayoutSeed(777, 1, c), Is.EqualTo(seed), $"Chamber {c} is stable");
            }
            Assert.That(d.LayoutSeed(778, 1, 1), Is.Not.EqualTo(d.LayoutSeed(777, 1, 1)), "new run, new layout");
            Assert.That(d.LayoutSeed(777, 2, 1), Is.Not.EqualTo(d.LayoutSeed(777, 1, 1)), "other Realm, other layout");
            Assert.That(ChamberDifficulty.MixSeed(777, 1, 4), Is.EqualTo(d.LayoutSeed(777, 1, 4)));
        }

        [Test]
        public void LayoutSeed_PinnedSeedWins_ZeroFallsBack()
        {
            var d = Track(ChamberDifficulty.Create(seeds: new[] { 0, 4242 }));
            Assert.That(d.LayoutSeed(1, 1, 2), Is.EqualTo(4242));
            Assert.That(d.LayoutSeed(99, 1, 2), Is.EqualTo(4242), "pinned layout is the same in every run");
            Assert.That(d.LayoutSeed(1, 1, 1), Is.EqualTo(ChamberDifficulty.MixSeed(1, 1, 1)), "0 = run layout");
            Assert.That(d.LayoutSeed(1, 1, 3), Is.EqualTo(ChamberDifficulty.MixSeed(1, 1, 3)), "past the list = run layout");
        }

        [Test]
        public void RunState_NewRunPicksASeed_GivenSeedIsKept()
        {
            RunState.NewRun(TestRealm, 31337);
            Assert.That(RunState.RunSeed, Is.EqualTo(31337));
            RunState.JumpToChamber(4);
            Assert.That(RunState.Chamber, Is.EqualTo(4));
            Assert.That(RunState.RunSeed, Is.EqualTo(31337), "jumping keeps the run");
            RunState.NewRun(TestRealm);
            Assert.That(RunState.RunSeed, Is.Not.EqualTo(0));
            Assert.That(RunState.Chamber, Is.EqualTo(RunState.FirstChamber));
        }

        // ------------------------------------------------------------ Spawner and world

        private Camera Camera()
        {
            var cam = Track(new GameObject("Camera")).AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.aspect = 9f / 19.5f;
            cam.nearClipPlane = 5f;
            cam.farClipPlane = 90f;
            var rotation = Quaternion.Euler(55f, 0f, 0f);
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2.2f) - rotation * Vector3.forward * 30f, rotation);
            return cam;
        }

        private (EnemySpawner spawner, EnemyWaveTable table) Spawner(int maxAlive, int prewarm = 4)
        {
            var template = Track(new GameObject("ZombieTemplate"));
            template.SetActive(false);
            var prefab = template.AddComponent<Enemy>();
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(0f, prewarm, new[]
            {
                new EnemyWaveTable.Phase
                {
                    maxAlive = maxAlive, spawnInterval = 0.1f, groupSize = new Vector2Int(4, 4),
                    enemies = new[] { new EnemyWaveTable.EnemyEntry { prefab = prefab, weight = 1f } },
                },
            });
            // Active (enemies leave Enemy.Active on disable); sync tests never reach Start, Configure prewarms.
            var spawner = Track(new GameObject("Spawner")).AddComponent<EnemySpawner>();
            return (spawner, table);
        }

        [Test]
        public void Spawner_AppliesHealthAndCapMultipliers()
        {
            var (spawner, table) = Spawner(maxAlive: 10);
            var player = Track(new GameObject("Target")).transform;
            spawner.SetDifficulty(1.3f, 0.8f, 1.6f);
            spawner.Configure(table, player, Camera(), new OpenGround());

            Assert.That(spawner.Created, Is.GreaterThanOrEqualTo(13), "prewarm covers the scaled cap");
            for (int i = 0; i < 400 && spawner.InUse < 20; i++) spawner.Tick(0.05f);
            Assert.That(spawner.InUse, Is.EqualTo(13), "cap 10 x 1.3");

            foreach (var e in Enemy.Active)
            {
                Assert.That(e.MaxHealth, Is.EqualTo(e.BaseMaxHealth * 1.6f).Within(1e-3f));
                Assert.That(e.Health, Is.EqualTo(e.MaxHealth).Within(1e-3f), "spawns at full scaled health");
                Assert.That(e.IsHurt, Is.False, "no health bar on a fresh spawn");
            }
            var first = Enemy.Active[0];
            first.TakeDamage(first.BaseMaxHealth, Vector3.forward);
            Assert.That(first.IsAlive, Is.True, "one base-health hit no longer kills");
            Assert.That(first.IsHurt, Is.True);
        }

        [Test]
        public void Spawner_DefaultIsTheTableAsWritten()
        {
            var (spawner, table) = Spawner(maxAlive: 6);
            spawner.Configure(table, Track(new GameObject("Target")).transform, Camera(), new OpenGround());
            for (int i = 0; i < 400 && spawner.InUse < 20; i++) spawner.Tick(0.05f);
            Assert.That(spawner.InUse, Is.EqualTo(6));
            foreach (var e in Enemy.Active) Assert.That(e.MaxHealth, Is.EqualTo(e.BaseMaxHealth));
        }

        [Test]
        public void World_TakesTheSeedBeforeAndAfterAwake_FixedSeedWins()
        {
            var early = Track(new GameObject("WorldEarly"));
            early.SetActive(false);
            var a = early.AddComponent<WorldStreamer>();
            a.UseSeed(1234); // ChamberSetup runs first (execution order -300)
            early.SetActive(true);
            Assert.That(a.Seed, Is.EqualTo(1234), "Awake keeps the Chamber seed");

            var b = Track(new GameObject("WorldLate")).AddComponent<WorldStreamer>();
            b.UseSeed(1234);
            Assert.That(b.Seed, Is.EqualTo(1234));
            Assert.That(b.ExitCell, Is.EqualTo(a.ExitCell), "same seed, same exit");

            var exits = new HashSet<Vector2Int>();
            for (int s = 1; s <= 40; s++)
            {
                b.UseSeed(ChamberDifficulty.MixSeed(5, 1, s));
                exits.Add(b.ExitCell);
            }
            Assert.That(exits.Count, Is.GreaterThan(3), "Chamber seeds move the exit around");
        }

        [Test]
        public void Setup_AppliesPlanToSpawnerAndWorld_AndPublishesIt()
        {
            var d = Track(ChamberDifficulty.Create());
            var (spawner, table) = Spawner(maxAlive: 6);
            var world = Track(new GameObject("World")).AddComponent<WorldStreamer>();
            var go = Track(new GameObject("Chamber"));
            go.SetActive(false);
            var setup = go.AddComponent<ChamberSetup>();
            setup.Setup(d, world, spawner);
            ChamberPlan? published = null;
            Action<ChamberPlan> onPlanned = p => published = p;
            ChamberSetup.Planned += onPlanned;
            try
            {
                var plan = setup.Apply(TestRealm, 5, 2024);
                Assert.That(plan.Type, Is.EqualTo(ChamberType.Boss));
                Assert.That(spawner.HealthMultiplier, Is.EqualTo(d.HealthMultiplier(5)));
                Assert.That(spawner.CapMultiplier, Is.EqualTo(d.CapMultiplier(5)));
                Assert.That(spawner.IntervalMultiplier, Is.EqualTo(d.IntervalMultiplier(5)));
                Assert.That(world.Seed, Is.EqualTo(d.LayoutSeed(2024, TestRealm, 5)));
                Assert.That(ChamberSetup.Current.Chamber, Is.EqualTo(5));
                Assert.That(published.HasValue && published.Value.Chamber == 5, Is.True, "Planned raised");
            }
            finally
            {
                ChamberSetup.Planned -= onPlanned;
            }
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>The built Game scene: ChamberSetup is wired to the asset, world and spawner, and reads RunState.</summary>
        [UnityTest]
        public IEnumerator GameScene_UsesRunStateChamber()
        {
            var seeds = new Dictionary<int, int>();
            var exits = new Dictionary<int, Vector2Int>();
            foreach (int chamber in new[] { 3, 4, 3 })
            {
                RunState.NewRun(TestRealm, 9001);
                RunState.JumpToChamber(chamber);
                SceneManager.LoadScene("Game");
                yield return null;
                yield return null;
                var setup = Object.FindAnyObjectByType<ChamberSetup>();
                var world = Object.FindAnyObjectByType<WorldStreamer>();
                var spawner = Object.FindAnyObjectByType<EnemySpawner>();
                Assert.That(setup, Is.Not.Null, "ChamberSetup in the Game scene");
                Assert.That(setup.Difficulty, Is.Not.Null, "difficulty asset wired");
                Assert.That(ChamberSetup.Current.Chamber, Is.EqualTo(chamber));
                Assert.That(ChamberSetup.Current.Realm, Is.EqualTo(TestRealm));
                Assert.That(spawner.HealthMultiplier, Is.EqualTo(setup.Difficulty.HealthMultiplier(chamber)));
                Assert.That(spawner.CapMultiplier, Is.EqualTo(setup.Difficulty.CapMultiplier(chamber)));
                if (world.FixedSeed == 0)
                    Assert.That(world.Seed, Is.EqualTo(setup.Difficulty.LayoutSeed(9001, TestRealm, chamber)));
                if (seeds.TryGetValue(chamber, out int before))
                {
                    Assert.That(world.Seed, Is.EqualTo(before), "same Chamber of the same run, same layout");
                    Assert.That(world.ExitCell, Is.EqualTo(exits[chamber]));
                }
                seeds[chamber] = world.Seed;
                exits[chamber] = world.ExitCell;
            }
            Assert.That(seeds[3], Is.Not.EqualTo(seeds[4]), "next Chamber, new layout");

            var empty = SceneManager.CreateScene("ChamberDifficultyEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
