using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-245 / KAN-247 island Chambers, logic side: the room polygon with its two chamfered corners, rotated pools, wall
    /// runs and round cover block walking and spawns; entering the island turns the streamed world off, hides the world's
    /// ground / sea / weather, places the Knight at the SW start facing the NE gate, hands walkability to the spawner and
    /// enemies, clamps the camera to a rectangle and points the gold arrow at the gate (also while locked); the Realm 1
    /// schedule picks the theme by Chamber (wraps, skips boss Chambers, ignores other Realms, dev override wins); the built
    /// Game scene plays Realm 1 Chamber 1 on the Drowned Cathedral island and the NE gate is reachable from the start.
    /// Look: Docs/screenshots/kan247-*.png next to Docs/screenshots/kan245-concepts/.
    /// </summary>
    public sealed class IslandChamberTests
    {
        private const int TestRealm = 96; // never touches the saved progress of a real Realm
        private static readonly Vector3 IslandCentre = new Vector3(0f, 0f, -400f);

        private readonly List<Object> _objects = new List<Object>();
        private IWalkability _savedWalk;
        private IslandChamber _island;
        private WorldStreamer _world;
        private EnemySpawner _spawner;
        private Transform _player;
        private TopDownFollowCamera _camera;
        private ObjectiveArrow _arrow;
        private GameObject _worldGround;

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            ChamberSetup.ForcedTheme = null;

            _world = Track(new GameObject("World")).AddComponent<WorldStreamer>();
            _worldGround = new GameObject("Ground");
            _worldGround.transform.SetParent(_world.transform, false);
            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false); // no Start: only its walkability is read
            _spawner = spawnerGo.AddComponent<EnemySpawner>();

            var playerGo = Track(new GameObject("Player"));
            playerGo.AddComponent<CharacterController>();
            _player = playerGo.transform;

            var camGo = Track(new GameObject("Camera"));
            camGo.AddComponent<Camera>();
            _camera = camGo.AddComponent<TopDownFollowCamera>();
            _camera.Target = _player;

            var arrowHolder = Track(new GameObject("Arrow Holder"));
            arrowHolder.SetActive(false);
            arrowHolder.transform.SetParent(_player, false);
            _arrow = arrowHolder.AddComponent<ObjectiveArrow>();
            _arrow.Setup(_world);
            arrowHolder.SetActive(true);

            var islandGo = Track(new GameObject("Island"));
            islandGo.transform.position = IslandCentre;
            islandGo.SetActive(false);
            _island = islandGo.AddComponent<IslandChamber>();
            _island.Setup("test_theme", new Vector2(18f, 30f), 7.5f,
                new[] { new IslandChamber.Pond { centre = new Vector2(5f, 0f), radii = new Vector2(1.5f, 4f), yaw = 30f, rect = true } },
                new[] { new IslandChamber.WallRun { a = new Vector2(-14f, 14f), b = new Vector2(-7f, 14f), radius = 0.5f } },
                new[] { new IslandChamber.Round { centre = new Vector2(-10f, 0f), radius = 0.8f } },
                new Vector3(-11f, 0f, -21.5f), null, _world, _spawner, _player, _camera, _arrow, new[] { _worldGround });
        }

        [TearDown]
        public void TearDown()
        {
            if (_island != null) _island.Leave();
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Walkability = _savedWalk;
            ChamberSetup.ForcedTheme = null;
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static Vector3 W(float x, float z) => IslandCentre + new Vector3(x, 0f, z);

        // ------------------------------------------------------------ Walkability

        [Test]
        public void Room_BlocksOutside_TheWalls_AndBothChamfers()
        {
            Assert.That(_island.IsBlocked(W(0f, -10f), 0.45f), Is.False, "open floor");
            Assert.That(_island.IsBlocked(W(17.4f, 0f), 0.45f), Is.False, "body just inside the east wall");
            Assert.That(_island.IsBlocked(W(17.8f, 0f), 0.45f), Is.True, "body touches the east wall");
            Assert.That(_island.IsBlocked(W(0f, 30.5f), 0.1f), Is.True, "beyond the north wall");
            Assert.That(_island.IsBlocked(W(17f, 29f), 0.3f), Is.True, "NE corner is cut off (gate chamfer)");
            Assert.That(_island.IsBlocked(W(-17f, -29f), 0.3f), Is.True, "SW corner is cut off (entry chamfer)");
            Assert.That(_island.IsBlocked(W(-17f, 29f), 0.3f), Is.False, "NW corner is square");
            Assert.That(_island.IsBlocked(W(17f, -29f), 0.3f), Is.False, "SE corner is square");
            Assert.That(_island.IsBlocked(Vector3.zero, 0.3f), Is.True, "the shore world is not part of the island");
        }

        [Test]
        public void Pool_WallRun_AndCover_Block()
        {
            // Rotated pool: 30 degrees (Unity yaw); a point along its long axis 3 m from the centre is inside.
            var along = Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;
            Assert.That(_island.IsBlocked(W(5f, 0f) + along * 3f, 0.1f), Is.True, "inside the pool along its axis");
            var side = Quaternion.Euler(0f, 30f, 0f) * Vector3.right;
            Assert.That(_island.IsBlocked(W(5f, 0f) + side * 2.2f, 0.3f), Is.False, "clear beside the pool");
            Assert.That(_island.IsBlocked(W(5f, 0f) + side * 1.7f, 0.3f), Is.True, "body overlaps the pool edge");

            Assert.That(_island.IsBlocked(W(-10f, 14.6f), 0.3f), Is.True, "against the wall run");
            Assert.That(_island.IsBlocked(W(-10f, 15.5f), 0.3f), Is.False, "behind the wall run, clear");
            Assert.That(_island.IsBlocked(W(-6.35f, 14f), 0.3f), Is.True, "round wall end");
            Assert.That(_island.IsBlocked(W(-6.1f, 14f), 0.3f), Is.False, "clear past the wall end");

            Assert.That(_island.IsBlocked(W(-10f, 1f), 0.3f), Is.True, "column");
            Assert.That(_island.IsBlocked(W(-10f, 1.3f), 0.3f), Is.False, "clear of the column");
        }

        [Test]
        public void InPond_Ellipse_UsesItsRadii()
        {
            var p = new IslandChamber.Pond { centre = Vector2.zero, radii = new Vector2(3f, 1.5f), yaw = 0f, rect = false };
            Assert.That(IslandChamber.InPond(p, 2.9f, 0f, 0f), Is.True);
            Assert.That(IslandChamber.InPond(p, 0f, 1.6f, 0f), Is.False);
            Assert.That(IslandChamber.InPond(p, 0f, 1.6f, 0.2f), Is.True, "grown by the body radius");
            Assert.That(IslandChamber.InPond(p, 2.2f, 1.2f, 0f), Is.False, "outside the ellipse corner");
        }

        // ------------------------------------------------------------ Enter / leave

        [Test]
        public void Enter_TurnsWorldOff_HidesGround_PlacesKnight_UsesIslandShape()
        {
            _island.Enter();
            Assert.That(_island.gameObject.activeSelf, Is.True);
            Assert.That(IslandChamber.Active, Is.SameAs(_island));
            Assert.That(_world.enabled, Is.False, "no shore tile streams in");
            Assert.That(_worldGround.activeSelf, Is.False, "world snow ground hidden (it would cover the sea)");
            Assert.That(Vector3.Distance(_player.position, W(-11f, -21.5f)), Is.LessThan(0.01f), "Knight at the SW start");
            Vector3 toGate = _island.ExitPosition - _player.position;
            toGate.y = 0f;
            Assert.That(Vector3.Angle(_player.forward, toGate), Is.LessThan(1f), "Knight faces the NE gate");
            Assert.That(_spawner.Walkability, Is.SameAs(_island), "spawns test the island");
            Assert.That(Enemy.Walkability, Is.SameAs(_island), "enemies steer by the island");
            Assert.That(_camera.RectBounded, Is.True, "camera clamped to the room");

            _island.Leave();
            Assert.That(_island.gameObject.activeSelf, Is.False);
            Assert.That(IslandChamber.Active, Is.Null);
            Assert.That(_world.enabled, Is.True, "leaving gives the world back");
            Assert.That(_worldGround.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator Camera_StaysInsideTheRoomRectangle()
        {
            _island.Enter();
            _player.position = W(17f, -28f); // far SE corner
            for (int i = 0; i < 90; i++) yield return null;
            var t = _camera.transform;
            float k = -t.position.y / t.forward.y;
            Vector3 look = t.position + t.forward * k;
            Assert.That(look.x, Is.LessThanOrEqualTo(_island.CameraMax.x + 0.3f), "look point inside the east limit");
            Assert.That(look.z, Is.GreaterThanOrEqualTo(_island.CameraMin.y - 0.3f), "look point inside the south limit");
        }

        [UnityTest]
        public IEnumerator Arrow_FollowsTheGate_EvenWhileLocked()
        {
            var timer = Track(new GameObject("Timer")).AddComponent<SurvivalTimer>();
            var health = _player.gameObject.AddComponent<PlayerHealth>();
            timer.Setup(Track(ChamberRules.Create(600f)), _player, health);
            var gateGo = Track(new GameObject("Portal"));
            gateGo.transform.position = W(14f, 26f);
            var blocker = new GameObject("Door Block");
            blocker.transform.SetParent(gateGo.transform, false);
            var gate = gateGo.AddComponent<PortalGate>();
            gate.Setup(null, blocker.AddComponent<BoxCollider>(), Vector3.zero, 1f);
            _arrow.Follow(gate);
            _player.position = W(-11f, -21.5f);
            yield return null;
            Assert.That(_arrow.HasTarget, Is.True, "locked gate: the arrow still shows the way (dim)");
            Assert.That(_arrow.Open, Is.False);
            Assert.That(Vector3.Distance(_arrow.Target, gate.TriggerPosition), Is.LessThan(0.01f));
        }

        // ------------------------------------------------------------ Schedule

        [Test]
        public void Schedule_PicksByChamber_Wraps_OnlyItsRealm()
        {
            var s = Track(ChamberThemeSchedule.Create(1, 10,
                new ChamberThemeSchedule.Entry { chamber = 1, theme = "a" }, new ChamberThemeSchedule.Entry { chamber = 3, theme = "c" }));
            Assert.That(s.ThemeFor(1, 1), Is.EqualTo("a"));
            Assert.That(s.ThemeFor(1, 2), Is.Null, "no entry: streamed shore");
            Assert.That(s.ThemeFor(1, 3), Is.EqualTo("c"));
            Assert.That(s.ThemeFor(1, 11), Is.EqualTo("a"), "a 20-Chamber Realm repeats the order");
            Assert.That(s.ThemeFor(2, 1), Is.Null, "other Realms keep the shore");
        }

        [Test]
        public void ChamberSetup_EntersTheScheduledIsland_NotInBossChambers_DevOverrideWins()
        {
            var difficulty = Track(ChamberDifficulty.Create());
            var setup = Track(new GameObject("Chamber")).AddComponent<ChamberSetup>();
            setup.Setup(difficulty, _world, null);
            setup.SetupIslands(Track(ChamberThemeSchedule.Create(TestRealm, 10, new ChamberThemeSchedule.Entry { chamber = 1, theme = "test_theme" },
                new ChamberThemeSchedule.Entry { chamber = 5, theme = "test_theme" })), _island);

            setup.Apply(TestRealm, 1, 1234);
            Assert.That(IslandChamber.Active, Is.SameAs(_island), "Chamber 1 plays on its island");
            Assert.That(ChamberSetup.CurrentTheme, Is.EqualTo("test_theme"));

            setup.Apply(TestRealm, 2, 1234);
            Assert.That(_island.gameObject.activeSelf, Is.False, "Chamber 2 has no theme: island hidden");
            Assert.That(_world.enabled, Is.True, "and the shore streams again");
            Assert.That(ChamberSetup.CurrentTheme, Is.Null);

            var boss = setup.Apply(TestRealm, 5, 1234);
            Assert.That(boss.Type, Is.EqualTo(ChamberType.Boss));
            Assert.That(_island.gameObject.activeSelf, Is.False, "a boss Chamber never uses a theme");

            ChamberSetup.ForcedTheme = "test_theme";
            setup.Apply(TestRealm, 2, 1234);
            Assert.That(IslandChamber.Active, Is.SameAs(_island), "-ss-theme forces the island");
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>
        /// The built Game scene: Realm 1 Chamber 1 plays on the Drowned Cathedral island (world off, Knight inside, spawner and
        /// camera on the island, gate portal present); the NE gate is reachable from the SW start around the pools, pews and
        /// wall runs; enemies spawn on walkable floor; Chamber 4 streams the shore.
        /// </summary>
        [UnityTest]
        public IEnumerator GameScene_Chamber1_PlaysOnTheCathedralIsland_GateReachable()
        {
            TearDown();
            _island = null;

            RunState.NewRun(1, 9245);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var island = IslandChamber.Active;
            Assert.That(island, Is.Not.Null, "Chamber 1 entered an island");
            Assert.That(island.ThemeId, Is.EqualTo("drowned_cathedral"));
            Assert.That(island.Portal, Is.Not.Null, "island has its gate portal");
            Assert.That(island.PondCount, Is.GreaterThanOrEqualTo(2), "two pools");
            Assert.That(island.WallCount, Is.GreaterThanOrEqualTo(4), "wall runs + pew blocks");
            Assert.That(Object.FindAnyObjectByType<WorldStreamer>().enabled, Is.False);
            var player = Object.FindAnyObjectByType<PlayerMover>().transform;
            Assert.That(island.IsBlocked(player.position, 0.45f), Is.False, "Knight stands on open floor");
            Assert.That(Object.FindAnyObjectByType<EnemySpawner>().Walkability, Is.SameAs(island));
            Assert.That(Camera.main.GetComponent<TopDownFollowCamera>().RectBounded, Is.True);

            Assert.That(Reachable(island, player.position, island.Portal.TriggerPosition, 0.45f), Is.True,
                "a body of 0.45 m can walk from the SW start to the NE gate");

            float wait = 8f;
            while (wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            int alive = 0;
            foreach (var e in Object.FindObjectsByType<Enemy>())
                if (e.isActiveAndEnabled && e.IsAlive)
                {
                    alive++;
                    Assert.That(island.IsBlocked(e.transform.position, 0.05f), Is.False, $"{e.name} stands on walkable floor");
                }
            Assert.That(alive, Is.GreaterThan(0), "the horde spawns on the island");

            RunState.NewRun(1, 9245);
            RunState.JumpToChamber(4);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            Assert.That(IslandChamber.Active, Is.Null, "Chamber 4: no island yet (KAN-248 / KAN-249)");
            Assert.That(Object.FindAnyObjectByType<WorldStreamer>().enabled, Is.True, "Chamber 4 streams the shore");

            var empty = SceneManager.CreateScene("IslandEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        /// <summary>Breadth-first search over a 0.5 m grid of the island: can a body of <paramref name="radius"/> go from a to b?</summary>
        private static bool Reachable(IslandChamber island, Vector3 from, Vector3 to, float radius)
        {
            const float step = 0.5f;
            Vector3 c = island.Centre;
            int nx = Mathf.CeilToInt(island.HalfSize.x * 2f / step) + 1, nz = Mathf.CeilToInt(island.HalfSize.y * 2f / step) + 1;
            (int, int) Cell(Vector3 p) => (Mathf.RoundToInt((p.x - c.x + island.HalfSize.x) / step), Mathf.RoundToInt((p.z - c.z + island.HalfSize.y) / step));
            Vector3 Pos(int i, int j) => new Vector3(c.x - island.HalfSize.x + i * step, 0f, c.z - island.HalfSize.y + j * step);
            var seen = new bool[nx, nz];
            var queue = new Queue<(int, int)>();
            var start = Cell(from);
            queue.Enqueue(start);
            seen[start.Item1, start.Item2] = true;
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue();
                var p = Pos(i, j);
                if (new Vector2(p.x - to.x, p.z - to.z).magnitude < 1.5f) return true;
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int a = i + di, b = j + dj;
                    if (a < 0 || b < 0 || a >= nx || b >= nz || seen[a, b]) continue;
                    seen[a, b] = true;
                    // the trigger stands in the gate opening on the room edge: let the last steps reach it
                    if (island.IsBlocked(Pos(a, b), radius) && new Vector2(Pos(a, b).x - to.x, Pos(a, b).z - to.z).magnitude > 1.6f) continue;
                    queue.Enqueue((a, b));
                }
            }
            return false;
        }
    }
}
