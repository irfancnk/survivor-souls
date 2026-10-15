#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
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
    /// KAN-112 roster in the run: wave entries open from their Chamber (<see cref="EnemyWaveTable.EnemyEntry.fromChamber"/>),
    /// the shooter cap (share of the phase cap, absolute most), the spawner gates picks and prewarm by Chamber and never
    /// holds more shooters than the cap (no GC), the shipped Realm 1 table (melee-only first 45 s, gates 2 / 2 / 3 / 4, all
    /// 8 kept zombies from Chamber 4, cap 20 % / 6), the boss rotation asset (Butcher Chamber 5, Rotting Colossus Chamber 10)
    /// and the Game scene wiring (ChamberSetup gives the spawner its Chamber).
    /// </summary>
    public sealed class ChamberSpawnTableTests
    {
        private const string WavesPath = "Assets/_Project/Data/Combat/EnemyWaves_Realm1.asset";
        private const string RotationPath = "Assets/_Project/Data/Boss/BossRotation_Realm1.asset";
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";
        private static readonly string[] Shooters = { "PyreThrower", "BileSpitter", "HexboundShaman" };
        private static readonly string[] Kept =
        {
            "GraveyardLaborer", "RustedWatchman", "PlagueBloater", "PyreThrower", "BileSpitter", "FrenziedGhoul",
            "GraveBrute", "HexboundShaman",
        };

        private sealed class OpenGround : IWalkability
        {
            public bool IsBlocked(Vector3 position, float radius) => false;
        }

        private readonly List<Object> _objects = new List<Object>();
        private Enemy _melee, _shooter, _late;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(112);
            _melee = Template("Melee");
            _shooter = Template("Shooter");
            _shooter.SetHoldBand(6f, 10f);
            _late = Template("Late");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Walkability = null;
            Enemy.LandMaxZ = float.PositiveInfinity;
            BossFight.ForcedBoss = null;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy Template(string name)
        {
            var go = Track(new GameObject(name));
            go.SetActive(false);
            return go.AddComponent<Enemy>();
        }

        private static EnemyWaveTable.EnemyEntry Entry(Enemy prefab, float weight, int from = 0) =>
            new EnemyWaveTable.EnemyEntry { prefab = prefab, weight = weight, fromChamber = from };

        // ------------------------------------------------------------ Table rules

        [Test]
        public void Pick_SkipsEntries_NotOpenInTheChamber_AndRenormalises()
        {
            var phase = new EnemyWaveTable.Phase { enemies = new[] { Entry(_melee, 1f), Entry(_late, 3f, from: 3) } };
            for (int c = 1; c <= 2; c++)
                for (float r = 0f; r < 1f; r += 0.05f)
                    Assert.That(EnemyWaveTable.PickEnemy(phase, r, c), Is.SameAs(_melee), $"Chamber {c} roll {r}");
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.2f, 3), Is.SameAs(_melee), "Chamber 3: first quarter melee");
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.3f, 3), Is.SameAs(_late), "Chamber 3: the late type joins");
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.3f), Is.SameAs(_late), "no Chamber given = every entry open");
            Assert.That(Entry(_melee, 1f, 0).OpenIn(1) && Entry(_melee, 1f, 1).OpenIn(1), Is.True, "0 and 1 = from Chamber 1");

            var onlyLate = new EnemyWaveTable.Phase { enemies = new[] { Entry(_late, 1f, from: 4) } };
            Assert.That(EnemyWaveTable.PickEnemy(onlyLate, 0.5f, 2), Is.Null, "nothing open = no spawn");
        }

        [Test]
        public void Pick_WithoutShooters_NeverReturnsAShooter_AndCapIsShareOrMost()
        {
            Assert.That(EnemyWaveTable.IsShooter(_shooter), Is.True);
            Assert.That(EnemyWaveTable.IsShooter(_melee), Is.False);
            var phase = new EnemyWaveTable.Phase { enemies = new[] { Entry(_shooter, 5f), Entry(_melee, 1f) } };
            for (float r = 0f; r < 1f; r += 0.05f)
                Assert.That(EnemyWaveTable.PickEnemy(phase, r, 1, shooters: false), Is.SameAs(_melee), $"roll {r}");
            Assert.That(EnemyWaveTable.PickEnemy(phase, 0.1f, 1, shooters: true), Is.SameAs(_shooter));

            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.SetShooterCap(0.2f, 6);
            Assert.That(table.MaxShootersOf(24), Is.EqualTo(4), "20 % of 24");
            Assert.That(table.MaxShootersOf(25), Is.EqualTo(5), "20 % of 25 (no float loss)");
            Assert.That(table.MaxShootersOf(48), Is.EqualTo(6), "the absolute most wins");
            Assert.That(table.MaxShootersOf(4), Is.Zero, "a tiny cap allows none");
            table.SetShooterCap(0.5f, 0);
            Assert.That(table.MaxShootersOf(30), Is.Zero, "most 0 = no shooters");
        }

        [Test]
        public void PrewarmFor_CountsShares_AsTheChamberPicks()
        {
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(0f, 1, new[]
            {
                new EnemyWaveTable.Phase { maxAlive = 20, enemies = new[] { Entry(_melee, 1f), Entry(_late, 1f, from: 3) } },
            });
            Assert.That(table.PrewarmFor(_melee, 1), Is.EqualTo(25), "Chamber 1: all melee, 1.25 x the cap");
            Assert.That(table.PrewarmFor(_melee, 3), Is.EqualTo(15), "Chamber 3: half, x 1.5");
            Assert.That(table.PrewarmFor(_late, 1), Is.EqualTo(1), "closed type: only the floor");
        }

        // ------------------------------------------------------------ Spawner

        private EnemySpawner Spawner(EnemyWaveTable table, int chamber)
        {
            var camera = Track(new GameObject("Camera")).AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 14f, -8f), Quaternion.Euler(55f, 0f, 0f));
            camera.fieldOfView = 40f;
            camera.aspect = 9f / 19.5f;
            var player = Track(new GameObject("Player")).transform;
            var spawner = Track(new GameObject("Spawner")).AddComponent<EnemySpawner>();
            spawner.SetChamber(chamber); // before Configure: Configure prewarms
            spawner.Configure(table, player, camera, new OpenGround());
            return spawner;
        }

        private EnemyWaveTable MixedTable(int maxAlive)
        {
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(0f, 4, new[]
            {
                new EnemyWaveTable.Phase
                {
                    maxAlive = maxAlive, spawnInterval = 0.1f, groupSize = new Vector2Int(3, 3),
                    enemies = new[] { Entry(_melee, 1f), Entry(_shooter, 3f, from: 2), Entry(_late, 1f, from: 5) },
                },
            });
            table.SetShooterCap(0.2f, 6);
            return table;
        }

        private static int Alive(EnemySpawner spawner, Enemy prefab) =>
            spawner.GetComponentsInChildren<Enemy>(false).Count(e => e.name.StartsWith(prefab.name));

        [Test]
        public void Spawner_Chamber1_SpawnsNoClosedType_AndPrewarmsNone()
        {
            var spawner = Spawner(MixedTable(20), 1);
            Assert.That(spawner.Chamber, Is.EqualTo(1));
            int made = spawner.Created;
            for (int i = 0; i < 80; i++) spawner.Tick(0.1f);
            Assert.That(spawner.InUse, Is.EqualTo(20), "filled to the cap");
            Assert.That(Alive(spawner, _shooter) + Alive(spawner, _late), Is.Zero, "only open types");
            Assert.That(made, Is.EqualTo(25), "prewarm: the melee pool only (closed types skipped)");
        }

        [Test]
        public void Spawner_ShootersStayUnderTheCap_MeleeFillsTheRest_NoGC()
        {
            var spawner = Spawner(MixedTable(20), 5);
            int peak = 0;
            for (int i = 0; i < 120; i++)
            {
                spawner.Tick(0.1f);
                peak = Mathf.Max(peak, spawner.ShootersInUse);
                Assert.That(spawner.ShootersInUse, Is.LessThanOrEqualTo(4), $"tick {i}: 20 % of 20");
            }
            Assert.That(peak, Is.EqualTo(4), "shooters (weight 3 of 5) reach the cap");
            Assert.That(spawner.InUse, Is.EqualTo(20), "melee and the late type fill the rest");
            Assert.That(Alive(spawner, _late), Is.GreaterThan(0), "Chamber 5: the late type spawns");
            spawner.Tick(0.1f);
            Assert.That(() => spawner.Tick(0.1f), Is.Not.AllocatingGCMemory());
            Assert.That(() => { _ = spawner.ShootersInUse; }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Shipped data

        private static Enemy Prefab(string id) => AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + id + ".prefab");

        private static EnemyWaveTable ShippedWaves()
        {
            var table = AssetDatabase.LoadAssetAtPath<EnemyWaveTable>(WavesPath);
            Assert.That(table, Is.Not.Null, WavesPath);
            return table;
        }

        private static IEnumerable<EnemyWaveTable.EnemyEntry> Entries(EnemyWaveTable table) =>
            Enumerable.Range(0, table.PhaseCount).SelectMany(i => table.GetPhase(i).enemies);

        [Test]
        public void ShippedTable_GatesShootersAndLateTypes_ByChamber()
        {
            var table = ShippedWaves();
            foreach (var id in Shooters)
                Assert.That(EnemyWaveTable.IsShooter(Prefab(id)), Is.True, id + " keeps its distance");
            foreach (var id in Kept.Except(Shooters))
                Assert.That(EnemyWaveTable.IsShooter(Prefab(id)), Is.False, id + " is melee");

            var gates = new Dictionary<string, int> { { "PyreThrower", 2 }, { "BileSpitter", 2 }, { "PlagueBloater", 3 }, { "HexboundShaman", 4 } };
            foreach (var e in Entries(table))
            {
                string id = e.prefab.name.Replace("Enemy_", "");
                int want = gates.TryGetValue(id, out int g) ? g : 1;
                Assert.That(Mathf.Max(1, e.fromChamber), Is.EqualTo(want), id);
            }
            var first = table.GetPhase(table.PhaseAt(0f));
            Assert.That(first.enemies.Any(e => EnemyWaveTable.IsShooter(e.prefab)), Is.False, "first 45 s melee only");

            for (int c = 1; c <= 10; c++)
            {
                var open = new HashSet<string>(Entries(table).Where(e => e.OpenIn(c) && e.weight > 0f).Select(e => e.prefab.name.Replace("Enemy_", "")));
                Assert.That(open.Contains("PyreThrower"), Is.EqualTo(c >= 2), $"Chamber {c} Pyre Thrower");
                Assert.That(open.Contains("PlagueBloater"), Is.EqualTo(c >= 3), $"Chamber {c} Bloater");
                Assert.That(open.Contains("HexboundShaman"), Is.EqualTo(c >= 4), $"Chamber {c} Shaman");
                if (c >= 4)
                    foreach (var id in Kept) Assert.That(open, Has.Member(id), $"Chamber {c}: all 8 kept zombies");
            }

            Assert.That(table.MaxShooterShare, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(table.MaxShooters, Is.EqualTo(6));
            for (int i = 0; i < table.PhaseCount; i++)
            {
                float shooters = table.GetPhase(i).enemies.Where(e => EnemyWaveTable.IsShooter(e.prefab)).Sum(e => e.weight);
                float total = table.GetPhase(i).enemies.Sum(e => e.weight);
                Assert.That(shooters / total, Is.LessThan(0.2f), $"phase {i}: shooter weight under the cap share");
            }
        }

        [Test]
        public void ShippedRotation_Butcher5_Colossus10_RoundAfter()
        {
            var rotation = AssetDatabase.LoadAssetAtPath<BossRotation>(RotationPath);
            Assert.That(rotation, Is.Not.Null, RotationPath);
            Assert.That(rotation.Bosses.Select(b => b.name), Is.EqualTo(new[] { "Boss_Realm1_Butcher", "Boss_RottingColossus" }));
            Assert.That(rotation.For(2), Is.SameAs(rotation.For(0)), "round");
            Assert.That(rotation.For(-1), Is.SameAs(rotation.For(1)), "never out of range");
            Assert.That(BossRotation.Create(new BossData[0]).For(0), Is.Null, "empty = keep the default boss");

            var fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            fight.Setup(null, null, null, null, null, null, null, roster: rotation.Bosses);
            Assert.That(fight.BossFor(5).name, Is.EqualTo("Boss_Realm1_Butcher"), "Chamber 5");
            Assert.That(fight.BossFor(10).name, Is.EqualTo("Boss_RottingColossus"), "Chamber 10");
        }

        [UnityTest]
        public IEnumerator GameScene_ChamberGivesTheSpawnerItsChamber_AndTheRotationAsset()
        {
            BossFight.ForcedBoss = null;
            RunState.NewRun(1, 1120);
            RunState.JumpToChamber(3);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var spawner = EnemySpawner.Current;
            Assert.That(spawner, Is.Not.Null);
            Assert.That(spawner.Chamber, Is.EqualTo(3));
            Assert.That(spawner.Waves, Is.SameAs(AssetDatabase.LoadAssetAtPath<EnemyWaveTable>(WavesPath)));
            var fight = Object.FindAnyObjectByType<BossFight>(FindObjectsInactive.Include);
            Assert.That(fight, Is.Not.Null);
            Assert.That(fight.Rotation, Is.SameAs(AssetDatabase.LoadAssetAtPath<BossRotation>(RotationPath)));
            Assert.That(fight.Library.Select(b => b.name), Has.No.Member("Boss_RottingColossus"), "the Colossus moved to the rotation");
            Assert.That(fight.FindBoss("RottingColossus"), Is.Not.Null, "the dev flag still finds it");
            RunState.NewRun(1);
        }
    }
}
#endif
