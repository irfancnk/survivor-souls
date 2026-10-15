using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-174 acceptance, logic side: the Butcher's black fireballs (a <see cref="BossFight.Move.Barrage"/> side cast).
    /// One throw per interval while the boss fights, to a random point near the Knight (inside the entry's offset band,
    /// never right on it, off blocked ground); the landing circle warns for the whole flight (>= 0.6 s) and the shot hits
    /// once inside it; a Knight that walks out dodges; it runs next to other attacks and is never picked; no free circle =
    /// it waits; phase gating; no throws at a dead Knight; a kill clears shots and circles. Look: the mesh turns along the
    /// flight, sparks trail and burst on landing. No GC per frame. Game scene: the Butcher (first boss of the Realm 1
    /// rotation) throws them with the Blender mesh look. Screenshots: Docs/screenshots/game-boss-butcher-fireball*.png.
    /// </summary>
    public sealed class BossFireballTests
    {
        private const float Frame = 1f / 60f;
        private const int Circles = 8;
        private const int TestRealm = 96; // never touches the saved progress of a real Realm

        /// <summary>Walkability for tests: blocks every point with x above <see cref="MaxX"/>.</summary>
        private sealed class HalfBlocked : IWalkability
        {
            public float MaxX;
            public bool IsBlocked(Vector3 position, float radius) => position.x + radius > MaxX;
        }

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private SurvivalTimer _timer;
        private EnemySpawner _spawner;
        private SoulWispField _souls;
        private GroundTelegraph _telegraph;
        private LineTelegraph _lines;
        private BossProjectiles _bolts;
        private EnemyTelegraphs _pool;
        private GroundTelegraph[] _circles;
        private EnemyProjectiles _shots;
        private ParticleSystem _meshes, _sparks;
        private Enemy _body;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<int> _started = new List<int>();
        private readonly List<Vector3> _thrown = new List<Vector3>();
        private readonly List<BossFight.Attack> _thrownIn = new List<BossFight.Attack>();
        private readonly List<float> _hits = new List<float>();

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(1740);
            _started.Capacity = _thrown.Capacity = _thrownIn.Capacity = _hits.Capacity = 512; // no list growth in the GC test

            var playerGo = Track(new GameObject("Player"));
            _player = playerGo.AddComponent<PlayerHealth>(); // max 400
            var collector = playerGo.AddComponent<SoulCollector>();
            collector.Setup(0.01f, 1f);

            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false);
            var xp = xpGo.AddComponent<XpManager>();
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100, 1f, 1);
            xp.Setup(curve);
            xpGo.SetActive(true);
            _souls = Track(new GameObject("Souls")).AddComponent<SoulWispField>();
            _souls.Setup(xp, collector, 64);
            _souls.DriftEnabled = false;

            _timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            _timer.Setup(Track(ChamberRules.Create(600f)), _player.transform, _player);

            _spawner = Track(new GameObject("Enemies")).AddComponent<EnemySpawner>();
            _spawner.enabled = false;

            _telegraph = Track(new GameObject("Telegraph")).AddComponent<GroundTelegraph>();
            _telegraph.Setup(Quad(), Quad());

            _lines = Track(new GameObject("Lanes")).AddComponent<LineTelegraph>();
            var edges = new Renderer[LineTelegraph.MaxLanes];
            var fills = new Renderer[LineTelegraph.MaxLanes];
            for (int i = 0; i < LineTelegraph.MaxLanes; i++)
            {
                edges[i] = Quad();
                fills[i] = Quad();
            }
            _lines.Setup(edges, fills);

            _bolts = Track(new GameObject("Bolts")).AddComponent<BossProjectiles>();
            _bolts.Setup(_player, null, 64);

            _circles = new GroundTelegraph[Circles];
            for (int i = 0; i < Circles; i++)
            {
                _circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                _circles[i].Setup(Quad(), Quad());
            }
            _pool = Track(new GameObject("Warnings")).AddComponent<EnemyTelegraphs>();
            _pool.Setup(new LineTelegraph[0], _circles);

            _meshes = ScriptParticles("Black Fireballs", 16);
            _meshes.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Mesh;
            _sparks = ScriptParticles("Sparks", 256);
            var looks = new EnemyProjectiles.Look[(int)ShotKind.BlackFireball + 1];
            looks[(int)ShotKind.BlackFireball] = new EnemyProjectiles.Look
            {
                sprites = _meshes, size = 0.8f, color = Color.white, mesh = true, spin = 220f, trail = _sparks, trailRate = 34f,
                impactSparks = 16, trailColor = Color.magenta,
            };
            _shots = Track(new GameObject("Enemy Shots")).AddComponent<EnemyProjectiles>();
            _shots.Setup(_player, null, _pool, 16, looks);
            _shots.HitPlayer += (at, dealt) => _hits.Add(dealt);

            var bodyGo = Track(new GameObject("BossBody"));
            bodyGo.SetActive(false);
            _body = bodyGo.AddComponent<Enemy>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_fight != null && _fight.Boss != null) Object.DestroyImmediate(_fight.Boss.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _started.Clear();
            _thrown.Clear();
            _thrownIn.Clear();
            _hits.Clear();
            _fight = null;
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Renderer Quad()
        {
            var go = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.GetComponent<Renderer>();
        }

        private ParticleSystem ScriptParticles(string name, int max)
        {
            var ps = Track(new GameObject(name)).AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.maxParticles = max;
            main.startRotation3D = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            ps.Play();
            return ps;
        }

        private BossData Data(BossAttack[] attacks) =>
            Track(BossData.CreateWithAttacks(_body, attacks, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: 0.5f, maxHitShare: 0.35f));

        /// <summary>A barrage that lands right round the Knight (0..0.3 m), every <paramref name="interval"/> s.</summary>
        private static BossAttack OnTheKnight(float interval = 5f, BossPhases phases = BossPhases.Both) =>
            BossAttack.Barrage("Black Fireballs", phases, 0f, 30f, interval, minOffset: 0f, maxOffset: 0.3f, impactRadius: 1f,
                flight: 0.9f, damage: 0.06f);

        /// <summary>The Butcher's numbers with a 5 s clock: one forced throw is alone in the air.</summary>
        private static BossAttack Slow()
        {
            var a = BossAttack.BlackFireballs();
            a.interval = 5f;
            return a;
        }

        private static BossAttack Lane() =>
            BossAttack.Lane("Chop", BossPhases.Both, 0f, 30f, length: 6f, width: 1.6f, telegraph: 0.9f, damage: 0.2f,
                recover: 0.8f, cooldown: 0.5f);

        /// <summary>Begin + rise; then the boss stands at the origin facing +Z with the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance = 6f)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null, _pool, null, _shots);
            _fight.AttackStarted += i => _started.Add(i);
            _fight.FireballThrown += (at, r) =>
            {
                _thrown.Add(at);
                _thrownIn.Add(_fight.CurrentAttack);
            };
            Assert.That(_fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77)), Is.True);
            Run(data.IntroDelay + data.SpawnWarning + 2f * Frame);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            _fight.Boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _player.transform.position = new Vector3(0f, 0f, knightDistance);
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) Step();
        }

        private void Step()
        {
            _fight.Tick(Frame);
            _shots.Tick(Frame);
            foreach (var c in _circles) c.Tick(Frame); // the landing flash ends and frees the circle (no Update in [Test])
        }

        private static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [Test]
        public void BlackFireballs_Numbers_WarnAtLeast06s_UnderTheHitCap()
        {
            var a = BossAttack.BlackFireballs();
            Assert.That(a.type, Is.EqualTo(BossFight.Move.Barrage));
            Assert.That(a.phases, Is.EqualTo(BossPhases.Both));
            Assert.That(a.interval, Is.EqualTo(1f), "about one per second");
            Assert.That(a.length, Is.GreaterThan(0f), "never aimed right at the Knight");
            Assert.That(a.reach, Is.GreaterThan(a.length));
            Assert.That(Mathf.Max(BossData.MinTelegraph, a.duration), Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(a.damage, Is.LessThanOrEqualTo(0.1f), "a light hit: it comes every second");
            Assert.That(BossAttack.Sample(BossFight.Move.Barrage).label, Is.EqualTo(a.label));
        }

        [Test]
        public void Barrage_OnePerInterval_RandomPointsNearTheKnight_NeverOnIt()
        {
            StartFight(Data(new[] { BossAttack.BlackFireballs() }), knightDistance: 6f);
            _player.Invulnerable = true;
            var knight = _player.transform.position;
            Run(1f - 6f * Frame);
            Assert.That(_thrown.Count, Is.EqualTo(0), "the first one after one interval");
            Run(8f * Frame);
            Assert.That(_thrown.Count, Is.EqualTo(1));
            Run(4f);
            Assert.That(_thrown.Count, Is.EqualTo(5), "one per second");
            Assert.That(_fight.Fireballs, Is.EqualTo(5));
            float spread = 0f;
            foreach (var p in _thrown)
            {
                float d = Planar(p, knight);
                Assert.That(d, Is.InRange(0.6f - 1e-3f, 3f + 1e-3f), "inside the offset band, never on the Knight");
                foreach (var q in _thrown) spread = Mathf.Max(spread, Planar(p, q));
            }
            Assert.That(spread, Is.GreaterThan(1.5f), "random directions, not one spot");
            Assert.That(_started, Is.Empty, "a side cast is never picked as an attack");
        }

        [Test]
        public void Barrage_CircleWarnsTheWholeFlight_AtLeast06s_ThenHitsInsideOnce()
        {
            StartFight(Data(new[] { OnTheKnight() }), knightDistance: 6f);
            Assert.That(_fight.ForceAttack(0), Is.True, "the debug start throws one now");
            Assert.That(_thrown.Count, Is.EqualTo(1));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(1), "its circle shows at the throw");
            var circle = System.Array.Find(_circles, c => c.IsShowing);
            Assert.That(Planar(circle.Centre, _thrown[0]), Is.LessThan(0.01f));
            Assert.That(circle.Radius, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(_shots.CountOf(ShotKind.BlackFireball), Is.EqualTo(1));
            Assert.That(_shots.IsLobbed(0), Is.True);

            float warned = 0f;
            while (_hits.Count == 0 && warned < 3f)
            {
                Step();
                warned += Frame;
            }
            Assert.That(warned, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), "circle shown >= 0.6 s before the hit");
            Assert.That(warned, Is.EqualTo(0.9f).Within(2f * Frame), "warning = flight");
            Assert.That(_hits, Is.EqualTo(new[] { 24f }), "6 % of 400");
            Assert.That(_player.Current, Is.EqualTo(376f).Within(0.01f));
            Assert.That(_shots.CountOf(ShotKind.BlackFireball), Is.EqualTo(0));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0), "the circle went back");
            Run(1f);
            Assert.That(_player.Current, Is.EqualTo(376f).Within(0.01f), "one fireball hits once");
        }

        [Test]
        public void Barrage_KnightWalksOutOfTheCircle_Dodges()
        {
            StartFight(Data(new[] { OnTheKnight() }), knightDistance: 6f);
            Assert.That(_fight.ForceAttack(0), Is.True);
            Run(0.3f);
            _player.transform.position = _thrown[0] + new Vector3(1.6f, 0f, 0f); // outside the 1 m circle
            Run(1f);
            Assert.That(_hits, Is.Empty);
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0));
        }

        [Test]
        public void Barrage_RunsNextToOtherAttacks_DuringTheirWindUpAndRecover()
        {
            StartFight(Data(new[] { Lane(), BossAttack.BlackFireballs() }), knightDistance: 4f);
            _player.Invulnerable = true;
            Run(8f);
            Assert.That(_started, Is.Not.Empty, "the lane attack still comes");
            Assert.That(_started, Has.None.EqualTo(1), "the barrage is never picked");
            Assert.That(_thrown.Count, Is.InRange(7, 8), "the clock does not stop for the other attack");
            Assert.That(_thrownIn, Has.Some.Not.EqualTo(BossFight.Attack.Chase), "thrown while the boss winds up or recovers");
        }

        [Test]
        public void Barrage_NoFreeCircle_Waits_ThenThrowsAtOnce()
        {
            StartFight(Data(new[] { BossAttack.BlackFireballs() }), knightDistance: 6f);
            var rented = new List<GroundTelegraph>();
            while (_pool.FreeCircles() > 0) rented.Add(_pool.RentCircle());
            Run(2f);
            Assert.That(_thrown, Is.Empty, "nothing lands without its warning");
            Assert.That(_shots.Count, Is.EqualTo(0));
            _pool.Release(rented[0]);
            Step();
            Assert.That(_thrown.Count, Is.EqualTo(1), "overdue: it throws on the next frame");
            foreach (var c in rented) _pool.Release(c);
        }

        [Test]
        public void Barrage_BlockedGround_NeverATarget()
        {
            Enemy.Walkability = new HalfBlocked { MaxX = 0f };
            StartFight(Data(new[] { BossAttack.BlackFireballs() }), knightDistance: 6f);
            _player.Invulnerable = true;
            Run(6f);
            Assert.That(_thrown.Count, Is.GreaterThanOrEqualTo(4));
            foreach (var p in _thrown) Assert.That(p.x + 0.5f, Is.LessThanOrEqualTo(0f), "off pillars and the wall");
        }

        [Test]
        public void Barrage_PhaseGating_Phase2Only_StartsAtHalfHealth()
        {
            StartFight(Data(new[] { OnTheKnight(1f, BossPhases.Phase2) }), knightDistance: 6f);
            _player.Invulnerable = true;
            Run(3f);
            Assert.That(_thrown, Is.Empty, "phase 1: off");
            _fight.Boss.TakeDamage(_fight.Boss.Health - _fight.Boss.MaxHealth * 0.45f, Vector3.zero);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            Run(2.2f);
            Assert.That(_thrown.Count, Is.InRange(1, 2), "phase 2: one per interval from the phase start");
        }

        [Test]
        public void Barrage_DeadKnight_NoThrows()
        {
            StartFight(Data(new[] { BossAttack.BlackFireballs() }), knightDistance: 6f);
            _player.TakeDamage(1e6f);
            Assert.That(_player.IsDead, Is.True);
            Run(3f);
            Assert.That(_thrown, Is.Empty);
        }

        [Test]
        public void Barrage_Kill_ClearsFireballsAndCircles_NoMoreThrows()
        {
            StartFight(Data(new[] { OnTheKnight(1f) }), knightDistance: 6f);
            Assert.That(_fight.ForceAttack(0), Is.True);
            Run(0.3f);
            Assert.That(_shots.CountOf(ShotKind.BlackFireball), Is.EqualTo(1));
            _fight.Boss.TakeDamage(_fight.Boss.Health + 1f, Vector3.zero);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_shots.CountOf(ShotKind.BlackFireball), Is.EqualTo(0), "nothing lands after the kill");
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0));
            int thrown = _thrown.Count;
            Run(3f);
            Assert.That(_thrown.Count, Is.EqualTo(thrown));
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Look_MeshTurnsAlongTheFlight_SparksTrailAndBurstOnLanding()
        {
            StartFight(Data(new[] { Slow() }), knightDistance: 6f);
            _player.Invulnerable = true;
            Assert.That(_fight.ForceAttack(0), Is.True);
            Run(0.25f);
            var buffer = new ParticleSystem.Particle[4];
            int n = _meshes.GetParticles(buffer);
            Assert.That(n, Is.EqualTo(1), "one mesh particle per fireball, no ghosts");
            Vector3 head = Quaternion.Euler(buffer[0].rotation3D) * Vector3.forward;
            Vector3 flight = _shots.TargetOf(0) - _fight.Boss.transform.position;
            flight.y = 0f;
            head.y = 0f;
            Assert.That(Vector3.Dot(head.normalized, flight.normalized), Is.GreaterThan(0.9f), "the head leads (mesh +Z along the flight)");
            Assert.That(buffer[0].startSize, Is.EqualTo(0.8f).Within(0.08f));
            int trail = _shots.SparksEmitted;
            Assert.That(trail, Is.InRange(6, 10), "34 sparks per second behind it");
            Run(0.7f);
            Assert.That(_shots.CountOf(ShotKind.BlackFireball), Is.EqualTo(0), "landed");
            Assert.That(_shots.SparksEmitted - trail, Is.GreaterThanOrEqualTo(16), "trail + landing burst");
        }

        [Test]
        public void Barrage_NoGcPerFrame()
        {
            StartFight(Data(new[] { Lane(), BossAttack.BlackFireballs() }), knightDistance: 4f);
            _player.Invulnerable = true;
            Run(6f); // warm-up: throws, lands, sparks, the lane (first calls JIT in the editor)
            Assert.That(_thrown.Count, Is.GreaterThan(3));
            Assert.That(() => Run(4f), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Game scene

        /// <summary>
        /// The built Game scene: Chamber 5 (first boss of the Realm 1 rotation) is the Butcher, its list has the black
        /// fireballs, the shot pool draws them with the Blender mesh (one mesh particle system, no shadows) and the fight
        /// throws them about once per second; a kill clears them.
        /// </summary>
        [UnityTest]
        public IEnumerator GameScene_Butcher_ThrowsBlackFireballs_WithTheBlenderMesh()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 9174);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            Assert.That(fight.Data.DisplayName, Is.EqualTo("THE BUTCHER OF ASHGRAVE"));
            int barrage = -1;
            for (int i = 0; i < fight.Attacks.Length; i++)
                if (fight.Attacks[i].type == BossFight.Move.Barrage) barrage = i;
            Assert.That(barrage, Is.GreaterThanOrEqualTo(0), "the Butcher asset has the black fireballs");
            Assert.That(fight.Attacks[barrage].interval, Is.EqualTo(1f));

            var shots = fight.Shots;
            Assert.That(shots, Is.Not.Null, "the scene's shot pool");
            var look = shots.LookOf(ShotKind.BlackFireball);
            Assert.That(look.mesh, Is.True);
            Assert.That(look.trail, Is.Not.Null);
            var r = look.sprites.GetComponent<ParticleSystemRenderer>();
            Assert.That(r.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh));
            Assert.That(r.mesh, Is.Not.Null);
            Assert.That(r.mesh.name, Does.Contain("BlackFireball"));
            Assert.That(r.mesh.isReadable, Is.True, "CPU mesh particles read the vertices");
            Assert.That(r.mesh.triangles.Length / 3, Is.InRange(300, 1500), "mobile budget");
            Assert.That(r.sharedMaterial.shader.name, Is.EqualTo("SurvivorSouls/BlackFireball"));
            Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(look.sprites.main.startRotation3D, Is.True);

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + fight.Data.IntroTime + 6f;
            while (fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            var knight = Object.FindAnyObjectByType<PlayerHealth>();
            knight.Invulnerable = true;
            var levelUp = Object.FindAnyObjectByType<LevelUpFlow>(); // no level-up pause in this test (KAN-169)
            if (levelUp != null) levelUp.enabled = false;
            GamePause.Clear();

            int before = fight.Fireballs;
            float watch = 0f;
            while (fight.Fireballs < before + 3 && watch < 8f)
            {
                watch += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Fireballs, Is.GreaterThanOrEqualTo(before + 3), "about one per second");

            while (shots.CountOf(ShotKind.BlackFireball) == 0 && watch < 10f)
            {
                watch += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(shots.CountOf(ShotKind.BlackFireball), Is.GreaterThan(0));
            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            Assert.That(shots.CountOf(ShotKind.BlackFireball), Is.EqualTo(0), "the kill clears them");

            var empty = SceneManager.CreateScene("BossFireballEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
