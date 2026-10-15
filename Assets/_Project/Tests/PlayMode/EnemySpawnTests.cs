using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-29 acceptance: spawns are off screen (feet and head, any aspect / zoom), at least the minimum distance away,
    /// on walkable ground, spread out, biased to the movement direction; no spawn when there is no valid point; gates;
    /// recycling of outrun enemies; pooling without allocation; steering round walls; a Game scene soak run.
    /// Synthetic tests frame the camera the way <c>TopDownFollowCamera</c> does (pitch 55, FOV 40, 10.5 m wide).
    /// </summary>
    public sealed class EnemySpawnTests
    {
        private const float Pitch = 55f, Fov = 40f, ViewWidth = 10.5f, LookAhead = 2.2f;
        private const float Tall = 9f / 19.5f, Short = 9f / 16f, Tablet = 3f / 4f;

        private sealed class FakeWalk : IWalkability
        {
            private readonly Func<Vector3, float, bool> _blocked;
            public FakeWalk(Func<Vector3, float, bool> blocked) => _blocked = blocked;
            public bool IsBlocked(Vector3 position, float radius) => _blocked(position, radius);
        }

        private readonly List<Object> _objects = new List<Object>();
        private Camera _camera;
        private Transform _player;
        private Enemy _prefab;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(2029);
            _camera = Track(new GameObject("Camera")).AddComponent<Camera>();
            _player = Track(new GameObject("Player")).transform;
            var template = Track(new GameObject("ZombieTemplate"));
            template.SetActive(false);
            _prefab = template.AddComponent<Enemy>();
            Frame(Vector3.zero, Tall);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Walkability = null;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        /// <summary>Same framing as TopDownFollowCamera (followX 1): the look point sits LookAhead m up the screen from the player.</summary>
        private void Frame(Vector3 player, float aspect, float fov = Fov, float pitch = Pitch)
        {
            _player.position = player;
            _camera.fieldOfView = fov;
            _camera.aspect = aspect;
            _camera.nearClipPlane = 5f;
            _camera.farClipPlane = 90f;
            float halfV = fov * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            float distance = ViewWidth * 0.5f / Mathf.Tan(halfH);
            var rotation = Quaternion.Euler(pitch, 0f, 0f);
            var look = new Vector3(player.x, 0f, player.z + LookAhead);
            _camera.transform.SetPositionAndRotation(look - rotation * Vector3.forward * distance, rotation);
        }

        private EnemyWaveTable Table(int maxAlive = 0, float interval = 1f, Vector2Int group = default, int prewarm = 8, float firstDelay = 0f)
        {
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            var phases = maxAlive <= 0
                ? new EnemyWaveTable.Phase[0]
                : new[]
                {
                    new EnemyWaveTable.Phase
                    {
                        maxAlive = maxAlive, spawnInterval = interval, groupSize = group == default ? Vector2Int.one : group,
                        enemies = new[] { new EnemyWaveTable.EnemyEntry { prefab = _prefab, weight = 1f } },
                    },
                };
            table.Configure(firstDelay, prewarm, phases);
            return table;
        }

        private EnemySpawner Spawner(EnemyWaveTable table, IWalkability walk = null, EnemySpawner.Rules? rules = null)
        {
            var spawner = Track(new GameObject("Spawner")).AddComponent<EnemySpawner>();
            spawner.Configure(table, _player, _camera, walk, rules);
            return spawner;
        }

        private static bool InViewport(Camera cam, Vector3 p)
        {
            Vector3 v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        private static bool BodyVisible(Camera cam, Vector3 p, float height) =>
            InViewport(cam, p) || InViewport(cam, p + Vector3.up * height);

        private static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ------------------------------------------------------------ Where

        [TestCase(Tall, Fov, Pitch)]
        [TestCase(Short, Fov, Pitch)]
        [TestCase(Tablet, Fov, Pitch)]
        [TestCase(Tall, 55f, 62f)] // zoomed-out / boss arena style camera
        public void SpawnPoints_OffScreen_FarEnough_Walkable(float aspect, float fov, float pitch)
        {
            var spawner = Spawner(Table(), new FakeWalk((p, r) => false));
            var rules = spawner.SpawnRules;
            for (int i = 0; i < 400; i++)
            {
                Frame(new Vector3(Random.Range(-50f, 50f), 0f, Random.Range(-50f, 10f)), aspect, fov, pitch);
                Assert.That(spawner.TryFindSpawnPoint(out var pos), Is.True);
                Assert.That(BodyVisible(_camera, pos, rules.bodyHeight), Is.False, $"visible spawn at {pos}");
                Assert.That(Planar(pos, _player.position), Is.GreaterThanOrEqualTo(rules.minDistance));
                // Margin: even the body moved screenMargin - 0.1 m towards the player stays hidden.
                Vector3 toward = (_player.position - pos).normalized * (rules.screenMargin - 0.1f);
                Assert.That(BodyVisible(_camera, pos + new Vector3(toward.x, 0f, toward.z), rules.bodyHeight), Is.False, "inside the margin");
            }
            Assert.That(spawner.Stats.NoPoint, Is.EqualTo(0));
        }

        [Test]
        public void BlockedGround_NeverUsed()
        {
            // Sea north of z = 4 and a rock field: every 4th 3 m cell is blocked.
            var walk = new FakeWalk((p, r) => p.z + r > 4f || (Mathf.FloorToInt(p.x / 3f) + Mathf.FloorToInt(p.z / 3f)) % 4 == 0);
            var spawner = Spawner(Table(), walk);
            for (int i = 0; i < 300; i++)
            {
                Frame(new Vector3(Random.Range(-30f, 30f), 0f, Random.Range(-30f, -5f)), Short);
                if (!spawner.TryFindSpawnPoint(out var pos)) continue;
                Assert.That(walk.IsBlocked(pos, spawner.SpawnRules.bodyRadius), Is.False, $"blocked spawn at {pos}");
            }
            Assert.That(spawner.Stats.RejectedBlocked, Is.GreaterThan(0));
        }

        [Test]
        public void MovingPlayer_SpawnsMostlyAheadAndBeside()
        {
            var spawner = Spawner(Table(), new FakeWalk((p, r) => false));
            for (int i = 0; i < 30; i++)
            {
                Frame(_player.position + Vector3.right * 0.1f, Tall); // 6 m/s along +X
                spawner.Tick(1f / 60f);
            }
            int behind = 0, ahead = 0;
            const int n = 1000;
            for (int i = 0; i < n; i++)
            {
                Assert.That(spawner.TryFindSpawnPoint(out var pos), Is.True);
                float dot = (pos - _player.position).normalized.x;
                if (dot < -0.7f) behind++;
                if (dot > 0.7f) ahead++;
            }
            // Uniform would put ~25 % in each 90 degree cone.
            Assert.That(behind, Is.LessThan(n * 0.1f), $"behind {behind}");
            Assert.That(ahead, Is.GreaterThan(behind * 3), $"ahead {ahead} behind {behind}");
        }

        [Test]
        public void NoValidPoint_NoSpawn_RetriesLater()
        {
            var spawner = Spawner(Table(maxAlive: 4, interval: 0.2f), new FakeWalk((p, r) => true));
            for (int i = 0; i < 100; i++) spawner.Tick(0.05f);
            Assert.That(spawner.InUse, Is.EqualTo(0));
            Assert.That(spawner.Stats.Spawned, Is.EqualTo(0));
            Assert.That(spawner.Stats.NoPoint, Is.GreaterThan(5));
            Assert.That(spawner.Stats.NoPoint, Is.LessThan(30), "retry delay throttles the search");
        }

        [Test]
        public void Group_SpreadsOverSeveralPoints()
        {
            var rules = EnemySpawner.Rules.Default;
            rules.maxSpawnsPerFrame = 8;
            var spawner = Spawner(Table(maxAlive: 5, interval: 30f, group: new Vector2Int(5, 5)), new FakeWalk((p, r) => false), rules);
            var placed = new List<Vector3>();
            spawner.Placed += (e, p, recycled) => placed.Add(p);
            for (int i = 0; i < 3 && spawner.InUse < 5; i++) spawner.Tick(0.35f);
            Assert.That(placed.Count, Is.EqualTo(5));
            for (int a = 0; a < placed.Count; a++)
            for (int b = a + 1; b < placed.Count; b++)
                Assert.That(Planar(placed[a], placed[b]), Is.GreaterThanOrEqualTo(rules.crowdRadius));
        }

        [Test]
        public void Gate_UsedWhenTheRingIsBlocked()
        {
            var gate = Track(new GameObject("Gate")).AddComponent<SpawnGate>();
            gate.transform.position = new Vector3(4f, 0f, -20f); // behind the player: below the screen
            var walk = new FakeWalk((p, r) => Planar(p, gate.transform.position) > gate.Spread + 0.05f);
            var spawner = Spawner(Table(), walk);
            Assert.That(spawner.TryFindSpawnPoint(out var pos), Is.True);
            Assert.That(Planar(pos, gate.transform.position), Is.LessThanOrEqualTo(gate.Spread + 0.05f));
            Assert.That(BodyVisible(_camera, pos, spawner.SpawnRules.bodyHeight), Is.False);
        }

        [Test]
        public void Gate_OnScreen_Rejected()
        {
            var gate = Track(new GameObject("Gate")).AddComponent<SpawnGate>();
            gate.transform.position = new Vector3(0f, 0f, 14.5f); // up the screen, inside the view on a tall phone
            var walk = new FakeWalk((p, r) => Planar(p, gate.transform.position) > gate.Spread + 0.05f);
            var spawner = Spawner(Table(), walk);
            Assert.That(spawner.TryFindSpawnPoint(out _), Is.False);
            Assert.That(spawner.Stats.RejectedOnScreen, Is.GreaterThan(0));
        }

        [Test]
        public void OutrunEnemy_IsRecycledOffScreen_KeepingHealth()
        {
            var spawner = Spawner(Table(), new FakeWalk((p, r) => false));
            var enemy = spawner.SpawnAt(_prefab, new Vector3(0f, 0f, -15f));
            enemy.TakeDamage(40f, Vector3.zero, DamageKind.Burn);
            Frame(new Vector3(0f, 0f, 30f), Tall); // ran 45 m north
            spawner.Tick(spawner.SpawnRules.recycleInterval + 0.01f);

            Assert.That(spawner.Stats.Recycled, Is.EqualTo(1));
            Assert.That(Planar(enemy.transform.position, _player.position), Is.LessThan(spawner.SpawnRules.recycleDistance));
            Assert.That(BodyVisible(_camera, enemy.transform.position, spawner.SpawnRules.bodyHeight), Is.False);
            Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth - 40f).Within(0.001f));
            Assert.That(spawner.Stats.OnScreenSpawns, Is.EqualTo(0));
        }

        [Test]
        public void VisibleEnemy_IsNeverRecycled()
        {
            var rules = EnemySpawner.Rules.Default;
            rules.recycleDistance = 8f; // the top of a tall screen is ~21 m up: an enemy there is far but visible
            var spawner = Spawner(Table(), new FakeWalk((p, r) => false), rules);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("inside the visible screen")); // the audit sees the forced spawn
            var enemy = spawner.SpawnAt(_prefab, new Vector3(0f, 0f, 14f));
            spawner.Tick(rules.recycleInterval + 0.01f);
            Assert.That(spawner.Stats.Recycled, Is.EqualTo(0));
            Assert.That(enemy.transform.position, Is.EqualTo(new Vector3(0f, 0f, 14f)));
        }

        // ------------------------------------------------------------ Waves + pool

        [Test]
        public void Waves_FillToCap_FromPrewarmedPool_NoAlloc()
        {
            var spawner = Spawner(Table(maxAlive: 6, interval: 0.1f, prewarm: 8), new FakeWalk((p, r) => false));
            Assert.That(spawner.Created, Is.EqualTo(8), "prewarmed before play");
            for (int i = 0; i < 40; i++) spawner.Tick(0.1f);
            Assert.That(spawner.InUse, Is.EqualTo(6));
            Assert.That(spawner.Created, Is.EqualTo(8), "no Instantiate during play");

            spawner.Tick(0.1f); // warm
            Assert.That(() => spawner.Tick(0.1f), Is.Not.AllocatingGCMemory());
            Assert.That(() => { spawner.TryFindSpawnPoint(out _); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WaveTable_PhaseByTime_AndWeightedPick()
        {
            var other = Track(new GameObject("Other")).AddComponent<Enemy>();
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(1f, 4, new[]
            {
                new EnemyWaveTable.Phase { startTime = 0f },
                new EnemyWaveTable.Phase { startTime = 60f },
                new EnemyWaveTable.Phase { startTime = 30f },
            });
            Assert.That(table.PhaseAt(10f), Is.EqualTo(0));
            Assert.That(table.PhaseAt(45f), Is.EqualTo(2));
            Assert.That(table.PhaseAt(61f), Is.EqualTo(1));

            var phase = new EnemyWaveTable.Phase
            {
                enemies = new[]
                {
                    new EnemyWaveTable.EnemyEntry { prefab = _prefab, weight = 3f },
                    new EnemyWaveTable.EnemyEntry { prefab = other, weight = 1f },
                    new EnemyWaveTable.EnemyEntry { prefab = null, weight = 5f },
                },
            };
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0f), Is.SameAs(_prefab));
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.74f), Is.SameAs(_prefab));
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.76f), Is.SameAs(other));
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.9999f), Is.SameAs(other));
            Assert.That(EnemyWaveTable.PickEnemy(new EnemyWaveTable.Phase(), 0.5f), Is.Null);

            // KAN-155: a stock check moves the pick to a type that still has pooled bodies; none stocked = no pick.
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0f, stocked: e => e != _prefab), Is.SameAs(other));
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.9999f, stocked: e => e != other), Is.SameAs(_prefab));
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.5f, stocked: e => false), Is.Null);
        }

        [Test]
        public void Waves_DryTypePool_SwapsToStockedType_NoInstantiate()
        {
            // KAN-155: a luck streak on one type used to empty its pool and instantiate mid-fight (soak "pool covers the cap"
            // flakes, 87 > 86). Drain type A by hand, then let the waves fill the rest of the cap: every pick goes to B.
            var other = Track(new GameObject("Other")).AddComponent<Enemy>();
            other.gameObject.SetActive(false);
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(0f, 0, new[]
            {
                new EnemyWaveTable.Phase
                {
                    maxAlive = 20, spawnInterval = 0.1f, groupSize = Vector2Int.one,
                    enemies = new[]
                    {
                        new EnemyWaveTable.EnemyEntry { prefab = _prefab, weight = 1f },
                        new EnemyWaveTable.EnemyEntry { prefab = other, weight = 1f },
                    },
                },
            });
            int poolA = table.PrewarmFor(_prefab), poolB = table.PrewarmFor(other);
            Assert.That(poolA, Is.LessThan(20), "type A alone cannot fill the cap");
            var spawner = Spawner(table, new FakeWalk((p, r) => false));
            int made = spawner.Created;
            Assert.That(made, Is.EqualTo(poolA + poolB), "prewarmed before play");
            for (int i = 0; i < poolA; i++) spawner.SpawnAt(_prefab, new Vector3(-21f + 3f * i, 0f, -30f));

            var placed = new List<Enemy>();
            spawner.Placed += (enemy, pos, recycled) => { if (!recycled) placed.Add(enemy); };
            for (int i = 0; i < 60; i++) spawner.Tick(0.1f);

            Assert.That(spawner.InUse, Is.EqualTo(20), "the cap still fills");
            Assert.That(spawner.Created, Is.EqualTo(made), "no Instantiate during play");
            Assert.That(placed.Count, Is.EqualTo(20 - poolA));
            foreach (var e in placed) Assert.That(e.name, Does.StartWith(other.name), "dry type swapped to the stocked one");
            Assert.That(spawner.Stats.PoolSwaps, Is.GreaterThan(0), "at least one pick hit the dry type (seeded roll)");
        }

        // ------------------------------------------------------------ Steering

        [UnityTest]
        public IEnumerator Chaser_SteersAroundAWall()
        {
            // Wall 4 m wide, 1 m deep between the enemy and its target.
            Enemy.Walkability = new FakeWalk((p, r) => Mathf.Abs(p.x) < 2f + r && Mathf.Abs(p.z) < 0.5f + r);
            _player.position = new Vector3(0.3f, 0f, 5f);
            var go = Track(Object.Instantiate(_prefab.gameObject));
            var enemy = go.GetComponent<Enemy>();
            enemy.Spawn(new Vector3(0f, 0f, -4f), _player, null);
            Time.timeScale = 4f;
            float start = Time.realtimeSinceStartup;
            while (enemy.transform.position.z < 2f && Time.realtimeSinceStartup - start < 8f)
            {
                Vector3 p = enemy.transform.position;
                Assert.That(Mathf.Abs(p.x) < 2f && Mathf.Abs(p.z) < 0.5f, Is.False, $"walked into the wall at {p}");
                yield return null;
            }
            Assert.That(enemy.transform.position.z, Is.GreaterThan(2f), "got round the wall");
        }

        // ------------------------------------------------------------ Game scene soak

        [UnityTest]
        public IEnumerator GameScene_Soak_TallPhone() => Soak(Tall, 150f);

        [UnityTest]
        public IEnumerator GameScene_Soak_ShortPhone() => Soak(Short, 150f);

        /// <summary>The 10-minute acceptance run (per aspect). Slow: run on demand with -testCategory Soak.</summary>
        [UnityTest, Explicit, Category("Soak")]
        public IEnumerator GameScene_Soak_TenMinutes([Values(Tall, Short)] float aspect) => Soak(aspect, 600f);

        /// <summary>
        /// Loads the real Game scene, keeps the Knight alive and wandering (random stick, some standing), runs the clock fast
        /// and checks every placement: hidden, far enough, on walkable ground. Kills keep the pool cycling.
        /// </summary>
        private IEnumerator Soak(float aspect, float gameSeconds)
        {
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            var world = Object.FindAnyObjectByType<WorldStreamer>();
            var mover = Object.FindAnyObjectByType<PlayerMover>();
            Assert.That(spawner, Is.Not.Null);
            Assert.That(world, Is.Not.Null);
            var cam = spawner.ViewCamera;
            cam.aspect = aspect;
            foreach (var c in mover.GetComponents<ContactDamageReceiver>()) c.enabled = false; // immortal
            mover.GetComponent<PlayerHealth>().Invulnerable = true; // KAN-106: Bloater / Ghoul / Brute attacks hurt too; a dead Knight stops the run clock
            foreach (var c in mover.GetComponents<SoulCollector>()) c.enabled = false;          // no level-up pause
            // Chambers last 2 minutes (KAN-92): no portal mid-soak, so a wandering Knight never leaves the scene.
            SurvivorSouls.Gameplay.Chamber.SurvivalTimer.Active?.Stop();

            var rules = spawner.SpawnRules;
            var failures = new List<string>();
            int placements = 0;
            spawner.Placed += (enemy, pos, recycled) =>
            {
                placements++;
                if (BodyVisible(cam, pos, rules.bodyHeight)) failures.Add($"visible {pos}");
                if (Planar(pos, mover.transform.position) < rules.minDistance - 0.01f) failures.Add($"too close {pos}");
                if (world.IsBlocked(pos, rules.bodyRadius - 0.01f)) failures.Add($"blocked {pos}");
            };

            float startCreated = spawner.Created;
            float nextTurn = 0f;
            Time.timeScale = 8f;
            try
            {
                while (spawner.RunTime < gameSeconds)
                {
                    if (spawner.RunTime >= nextTurn)
                    {
                        nextTurn = spawner.RunTime + Random.Range(2f, 6f);
                        FloatingJoystick.SimulatedValue = Random.value < 0.2f ? (Vector2?)null : Random.insideUnitCircle.normalized;
                    }
                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = 1f;
                FloatingJoystick.SimulatedValue = null;
                GamePause.Clear();
            }

            var stats = spawner.Stats;
            int created = spawner.Created;
            Debug.Log($"[EnemySpawnTests] soak aspect={aspect:F3} t={gameSeconds}s placements={placements} created={created} {stats}");

            // Leave no Game scene objects (enemies in Enemy.Active, gates) behind for the next fixtures.
            var empty = SceneManager.CreateScene("SoakEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            Enemy.LandMaxZ = float.PositiveInfinity;

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            Assert.That(stats.OnScreenSpawns, Is.EqualTo(0));
            Assert.That(stats.Spawned, Is.GreaterThan(gameSeconds / 10f), "spawner kept the field populated");
            // KAN-155: a dry type's pick moves to a stocked type, so play instantiates only when every pool of the phase is
            // empty (alive + dying > the whole prewarm, ~82 bodies vs a ~36 cap). The old +4 slack hid luck-streak flakes.
            Assert.That(created, Is.EqualTo(startCreated), "pool covers the cap: no Instantiate in play");
        }
    }
}
