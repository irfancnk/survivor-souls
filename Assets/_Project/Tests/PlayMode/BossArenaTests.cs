using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
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
    /// KAN-59 (KAN-22.2) acceptance, logic side: the ring and the low pillars block walking and spawns, nothing outside the
    /// ring is walkable; only boss Chambers enter the arena (world streamer off, Knight at the south start facing north,
    /// spawner + enemies use the arena shape, camera bounded, light dimmed); other Chambers keep it hidden and leaving
    /// restores the light; the camera soft clamp never passes its limit, is monotonic and leaves the middle untouched;
    /// the gold arrow shows the arena portal only once it is open; the built Game scene puts boss Chamber 5 in the arena
    /// (boss rises inside the ring) and Chamber 4 on the shore. Look: Docs/screenshots/game-boss-arena-*.png.
    /// </summary>
    public sealed class BossArenaTests
    {
        private const int TestRealm = 97; // never touches the saved progress of a real Realm
        private static readonly Vector3 ArenaCentre = new Vector3(0f, 0f, -30f);

        private readonly List<Object> _objects = new List<Object>();
        private IWalkability _savedWalk;
        private Color _savedAmbient;
        private BossArena _arena;
        private WorldStreamer _world;
        private EnemySpawner _spawner;
        private Transform _player;
        private TopDownFollowCamera _camera;
        private ObjectiveArrow _arrow;
        private Light _sun;

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            _savedAmbient = RenderSettings.ambientSkyColor;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.6f, 0.7f);

            _world = Track(new GameObject("World")).AddComponent<WorldStreamer>();
            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false); // no Start: only its walkability is read
            _spawner = spawnerGo.AddComponent<EnemySpawner>();

            var playerGo = Track(new GameObject("Player"));
            playerGo.AddComponent<CharacterController>();
            _player = playerGo.transform;
            _player.position = new Vector3(3f, 0f, 2f);

            var camGo = Track(new GameObject("Camera"));
            camGo.AddComponent<Camera>();
            _camera = camGo.AddComponent<TopDownFollowCamera>();
            _camera.Target = _player;

            var arrowHolder = Track(new GameObject("Arrow Holder"));
            arrowHolder.SetActive(false);
            arrowHolder.transform.SetParent(_player, false);
            var arrowMesh = new GameObject("Arrow").transform;
            arrowMesh.SetParent(arrowHolder.transform, false);
            _arrow = arrowHolder.AddComponent<ObjectiveArrow>();
            _arrow.Setup(_world, arrowMesh);
            arrowHolder.SetActive(true);

            _sun = Track(new GameObject("Sun")).AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = 1f;
            RenderSettings.sun = _sun;

            var arenaGo = Track(new GameObject("Boss Arena"));
            arenaGo.transform.position = ArenaCentre;
            arenaGo.SetActive(false);
            _arena = arenaGo.AddComponent<BossArena>();
            _arena.Setup(16f, new[] { new BossArena.Pillar { centre = new Vector2(5f, 0f), radius = 0.8f } }, new Vector3(0f, 0f, -10f), null,
                _world, _spawner, _player, _camera, _arrow);
        }

        [TearDown]
        public void TearDown()
        {
            if (_arena != null) _arena.Leave();
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Walkability = _savedWalk;
            RenderSettings.ambientSkyColor = _savedAmbient;
            RenderSettings.sun = null;
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        // ------------------------------------------------------------ Walkability

        [Test]
        public void Ring_BlocksOutsideAndAtTheWall()
        {
            Assert.That(_arena.IsBlocked(ArenaCentre, 0.5f), Is.False, "middle is free");
            Assert.That(_arena.IsBlocked(ArenaCentre + new Vector3(0f, 0f, 15.4f), 0.5f), Is.False, "body just inside the wall");
            Assert.That(_arena.IsBlocked(ArenaCentre + new Vector3(0f, 0f, 15.4f), 0.7f), Is.True, "wider body touches the wall");
            Assert.That(_arena.IsBlocked(ArenaCentre + new Vector3(-11.5f, 0f, -11.5f), 0.3f), Is.True, "diagonal beyond the ring");
            Assert.That(_arena.IsBlocked(ArenaCentre + new Vector3(0f, 0f, 40f), 0.1f), Is.True, "far outside");
            Assert.That(_arena.IsBlocked(new Vector3(0f, 0f, 0f), 0.5f), Is.True, "the shore world is not part of the arena");
        }

        [Test]
        public void Pillars_Block()
        {
            Vector3 pillar = ArenaCentre + new Vector3(5f, 0f, 0f);
            Assert.That(_arena.IsBlocked(pillar, 0.1f), Is.True);
            Assert.That(_arena.IsBlocked(pillar + new Vector3(1.1f, 0f, 0f), 0.4f), Is.True, "body overlaps the pillar");
            Assert.That(_arena.IsBlocked(pillar + new Vector3(1.4f, 0f, 0f), 0.4f), Is.False, "body clears the pillar");
        }

        // ------------------------------------------------------------ Enter / leave

        [Test]
        public void Enter_TurnsWorldOff_PlacesKnight_UsesArenaShape()
        {
            _arena.Enter();
            Assert.That(_arena.gameObject.activeSelf, Is.True);
            Assert.That(BossArena.Active, Is.SameAs(_arena));
            Assert.That(_world.enabled, Is.False, "no shore tile streams in");
            Assert.That(Vector3.Distance(_player.position, ArenaCentre + new Vector3(0f, 0f, -10f)), Is.LessThan(0.01f), "Knight at the south start");
            Assert.That(Vector3.Angle(_player.forward, Vector3.forward), Is.LessThan(0.5f), "Knight faces the middle / north");
            Assert.That(_spawner.Walkability, Is.SameAs(_arena), "spawns test the ring");
            Assert.That(Enemy.Walkability, Is.SameAs(_arena), "enemies steer by the ring");
            Assert.That(_camera.Bounded, Is.True, "camera clamped to the ring");
            Assert.That(RenderSettings.ambientSkyColor.r, Is.LessThan(0.5f), "ambient dimmed");
            Assert.That(_sun.intensity, Is.LessThan(1f), "sun dimmed");
        }

        [Test]
        public void Leave_HidesArena_RestoresLight()
        {
            _arena.Enter();
            _arena.Leave();
            Assert.That(_arena.gameObject.activeSelf, Is.False);
            Assert.That(BossArena.Active, Is.Null);
            Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(new Color(0.5f, 0.6f, 0.7f)));
            Assert.That(_sun.intensity, Is.EqualTo(1f));
        }

        [Test]
        public void ChamberSetup_EntersOnlyInBossChambers()
        {
            var difficulty = Track(ChamberDifficulty.Create());
            var setup = Track(new GameObject("Chamber")).AddComponent<ChamberSetup>();
            setup.Setup(difficulty, _world, null, _arena);

            var normal = setup.Apply(TestRealm, 4, 1234);
            Assert.That(normal.Type, Is.Not.EqualTo(ChamberType.Boss));
            Assert.That(_arena.gameObject.activeSelf, Is.False, "Chamber 4 plays on the shore");
            Assert.That(_world.enabled, Is.True);

            var boss = setup.Apply(TestRealm, 5, 1234);
            Assert.That(boss.Type, Is.EqualTo(ChamberType.Boss));
            Assert.That(_arena.gameObject.activeSelf, Is.True, "Chamber 5 plays in the arena");
            Assert.That(BossArena.Active, Is.SameAs(_arena));
        }

        // ------------------------------------------------------------ Camera

        [Test]
        public void SoftClamp_MiddleUntouched_NeverPassesLimit_Monotonic()
        {
            const float radius = 13f, band = 4f;
            var inside = new Vector2(3f, -6f);
            Assert.That(TopDownFollowCamera.SoftClamp(inside, radius, band), Is.EqualTo(inside));
            float last = 0f;
            for (float d = 0f; d <= 60f; d += 0.25f)
            {
                var o = TopDownFollowCamera.SoftClamp(new Vector2(0.6f, 0.8f) * d, radius, band);
                Assert.That(o.magnitude, Is.LessThanOrEqualTo(radius + 1e-4f), $"d={d}");
                Assert.That(o.magnitude, Is.GreaterThanOrEqualTo(last - 1e-5f), $"monotonic at d={d}");
                Assert.That(Vector2.Angle(o, new Vector2(0.6f, 0.8f)) < 0.01f || d == 0f, Is.True, "keeps the direction");
                last = o.magnitude;
            }
            float atInner = TopDownFollowCamera.SoftClamp(new Vector2(radius - band + 0.001f, 0f), radius, band).magnitude;
            Assert.That(atInner, Is.EqualTo(radius - band).Within(0.01f), "no jump at the soft band start");
        }

        [UnityTest]
        public IEnumerator Camera_StaysOverTheRing_WhenKnightHugsTheWall()
        {
            _arena.Enter();
            _player.position = ArenaCentre + new Vector3(0f, 0f, 15.5f); // north wall, by the portal
            for (int i = 0; i < 90; i++) yield return null;
            // Look point = where the view axis meets the ground.
            var t = _camera.transform;
            float k = -t.position.y / t.forward.y;
            Vector3 look = t.position + t.forward * k;
            float fromCentre = new Vector2(look.x - ArenaCentre.x, look.z - ArenaCentre.z).magnitude;
            Assert.That(fromCentre, Is.LessThanOrEqualTo(_arena.CameraRadius + 0.3f), "look point inside the clamp (shake aside)");
            Assert.That(look.z, Is.GreaterThan(ArenaCentre.z + 6f), "camera still leans towards the Knight");
        }

        // ------------------------------------------------------------ Arrow

        [UnityTest]
        public IEnumerator Arrow_ShowsArenaPortal_OnlyWhenOpen()
        {
            var timer = Track(new GameObject("Timer")).AddComponent<SurvivalTimer>();
            var health = _player.gameObject.AddComponent<PlayerHealth>();
            timer.Setup(Track(ChamberRules.Create(600f)), _player, health);

            var gateGo = Track(new GameObject("Portal"));
            gateGo.transform.position = ArenaCentre + new Vector3(0f, 0f, 16.5f);
            var blocker = new GameObject("Door Block");
            blocker.transform.SetParent(gateGo.transform, false);
            var gate = gateGo.AddComponent<PortalGate>();
            gate.Setup(null, blocker.AddComponent<BoxCollider>(), Vector3.zero, 1f);
            _arrow.PointAt(gate);

            _player.position = ArenaCentre + new Vector3(0f, 0f, -10f);
            yield return null;
            Assert.That(_arrow.Showing, Is.False, "locked portal: no arrow during the fight");

            timer.Complete();
            for (int i = 0; i < 400 && gate.State != PortalState.Open; i++) gate.Tick(1f / 60f);
            Assert.That(gate.State, Is.EqualTo(PortalState.Open));
            yield return null;
            Assert.That(_arrow.Showing, Is.True, "open portal: the arrow leads to it");

            _player.position = gate.TriggerPosition + new Vector3(0f, 0f, -3f);
            yield return null;
            Assert.That(_arrow.Showing, Is.False, "hidden close to the portal");
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>The built Game scene: boss Chamber 5 is fought in the arena; the boss rises inside the ring; Chamber 4 stays on the shore.</summary>
        [UnityTest]
        public IEnumerator GameScene_BossChamberPlaysInTheArena()
        {
            // Leave the fixture's scene objects alone: this test uses the real scene's.
            TearDown();
            _arena = null;

            RunState.NewRun(TestRealm, 9001);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var arena = BossArena.Active;
            Assert.That(arena, Is.Not.Null, "boss Chamber entered the arena");
            Assert.That(arena.Portal, Is.Not.Null, "arena has its exit portal");
            Assert.That(arena.PillarCount, Is.GreaterThanOrEqualTo(4), "low pillars (and braziers) block walking");
            var world = Object.FindAnyObjectByType<WorldStreamer>();
            Assert.That(world.enabled, Is.False);
            Assert.That(world.transform.Find("Tile_Spawn(Clone)"), Is.Null, "no shore tile streamed in");
            var player = Object.FindAnyObjectByType<PlayerMover>().transform;
            Assert.That(arena.IsBlocked(player.position, 0.4f), Is.False, "Knight stands inside the ring");
            Assert.That(Object.FindAnyObjectByType<EnemySpawner>().Walkability, Is.SameAs(arena));
            Assert.That(Camera.main.GetComponent<TopDownFollowCamera>().Bounded, Is.True);

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f; // + the Chamber fade-in pause
            while (fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting), "boss rose");
            Assert.That(arena.IsBlocked(fight.Boss.transform.position, 0.5f), Is.False, "boss rose inside the ring");
            foreach (var e in Object.FindObjectsByType<Enemy>())
                if (e.isActiveAndEnabled && e.IsAlive)
                {
                    var d = e.transform.position - arena.Centre;
                    d.y = 0f;
                    Assert.That(d.magnitude, Is.LessThan(arena.Radius), $"{e.name} inside the ring");
                }

            RunState.NewRun(TestRealm, 9001);
            RunState.JumpToChamber(4);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            Assert.That(BossArena.Active, Is.Null, "Chamber 4: no arena");
            Assert.That(Object.FindAnyObjectByType<WorldStreamer>().enabled, Is.True, "Chamber 4 streams the shore");

            var empty = SceneManager.CreateScene("BossArenaEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
