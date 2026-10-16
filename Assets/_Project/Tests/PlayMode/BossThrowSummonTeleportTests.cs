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
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-119 acceptance, logic side: the Lobbed boulder warns with its landing circle from the wind-up (at least 0.6 s),
    /// hits only inside the circle, once per boulder, a volley throws one boulder per interval at the Knight and each warns
    /// again; rubble blocks the ground (walkability through BossArena.Obstacles, a charge, the Knight's collider), pushes a
    /// Knight standing there out and sinks after its life; no free circle = no throw; a kill clears boulders and circles.
    /// Summon warns with circles on a ring round the boss, spawns the type, never keeps more than its count alive and only
    /// sends the missing ones; an unknown type is never picked; phase gating holds. Teleport warns at a walkable point near
    /// the Knight, shrinks the boss away, moves it there and grows it back (also after a kill), hits on arrival only with
    /// damage set, never starts without a free point. Colossus / Matriarch samples obey the warning floor and the hit cap.
    /// No GC per frame. Look and feel: screenshots (Docs/screenshots/game-boss-boulder*, -summon*, -teleport*).
    /// </summary>
    public sealed class BossThrowSummonTeleportTests
    {
        private const float Frame = 1f / 60f;
        private const int Circles = 8;
        private const int Boulders = 6;
        private const string GhoulName = "Enemy_TestGhoul";

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
        private BossBoulders _rocks;
        private Transform[] _rockBodies;
        private Enemy _body;
        private Enemy _ghoul;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<int> _started = new List<int>();
        private readonly List<Vector3> _thrown = new List<Vector3>();
        private readonly List<float> _landed = new List<float>();
        private readonly List<int> _summoned = new List<int>();
        private readonly List<float> _teleported = new List<float>();

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(1190);

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

            var ghoulGo = Track(new GameObject(GhoulName));
            ghoulGo.SetActive(false);
            _ghoul = ghoulGo.AddComponent<Enemy>();

            // Active object, component off: no Start / Update (no waves), but summons live under an active parent, so they
            // enable and leave Enemy.Active again when destroyed.
            _spawner = Track(new GameObject("Enemies")).AddComponent<EnemySpawner>();
            _spawner.enabled = false;
            _spawner.SetExtraTypes(new[] { _ghoul });

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

            _rockBodies = new Transform[Boulders];
            for (int i = 0; i < Boulders; i++)
            {
                var rock = Track(new GameObject("Boulder " + i));
                var solid = rock.AddComponent<CapsuleCollider>();
                solid.enabled = false;
                _rockBodies[i] = rock.transform;
            }
            _rocks = Track(new GameObject("Boulders")).AddComponent<BossBoulders>();
            _rocks.Setup(_player, _rockBodies);
            _rocks.Landed += (at, dealt) => _landed.Add(dealt);

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
            _landed.Clear();
            _summoned.Clear();
            _teleported.Clear();
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

        private BossData Data(BossAttack[] attacks, float maxHitShare = 0.35f) =>
            Track(BossData.CreateWithAttacks(_body, attacks, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: 0.5f, maxHitShare: maxHitShare));

        private static BossAttack Lobbed(int boulders = 1, float flight = 1.1f, float interval = 0.6f, float life = 8f,
            float damage = 0.2f, float telegraph = 0.8f) =>
            BossAttack.Lobbed("Boulder", BossPhases.Both, 0f, 16f, boulders, impactRadius: 2.5f, flight: flight, interval: interval,
                rubbleRadius: 0.9f, rubbleLife: life, telegraph: telegraph, damage: damage, recover: 0.5f, cooldown: 1f);

        private static BossAttack Summon(string type = "TestGhoul", int count = 4, BossPhases phases = BossPhases.Both) =>
            BossAttack.Summon("Summon", phases, 0f, 30f, type, count, ring: 2.6f, telegraph: 1.2f, recover: 0.5f, cooldown: 1f);

        private static BossAttack Teleport(float damage = 0f, float distance = 7f) =>
            BossAttack.Teleport("Teleport", BossPhases.Both, 0f, 30f, distance, warnRadius: 1.6f, telegraph: 0.8f, damage: damage,
                recover: 0.4f, cooldown: 1f);

        /// <summary>Begin + rise; then the boss stands at the origin facing +Z with the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance = 6f)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null, _pool, _rocks);
            _fight.AttackStarted += i => _started.Add(i);
            _fight.BoulderThrown += (at, r) => _thrown.Add(at);
            _fight.Summoned += (at, n) => _summoned.Add(n);
            _fight.Teleported += (from, to, dealt) => _teleported.Add(dealt);
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
            _rocks.Tick(Frame);
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame) Step();
        }

        private static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ------------------------------------------------------------ Lobbed

        [Test]
        public void Lobbed_CircleWarnsOnTheKnightFromTheWindUp_AtLeast06s_ThenHitsInsideOnce()
        {
            StartFight(Data(new[] { Lobbed(damage: 0.2f) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Lobbed));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(1), "the landing circle shows during the wind-up");
            var circle = System.Array.Find(_circles, c => c.IsShowing);
            Assert.That(Planar(circle.Centre, new Vector3(0f, 0f, 6f)), Is.LessThan(0.01f), "on the Knight");
            Assert.That(circle.Radius, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(_rocks.Flying, Is.EqualTo(0), "nothing thrown while it winds up");

            float warned = 0f;
            while (_landed.Count == 0 && warned < 5f)
            {
                Step();
                warned += Frame;
            }
            Assert.That(warned, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), "circle shown >= 0.6 s before the hit");
            Assert.That(warned, Is.EqualTo(_fight.WindUpTime + 1.1f).Within(3f * Frame), "wind-up + flight");
            Assert.That(_thrown.Count, Is.EqualTo(1));
            Assert.That(_landed, Is.EqualTo(new[] { 80f }), "20 % of 400, inside the circle");
            Assert.That(_player.Current, Is.EqualTo(320f).Within(0.01f));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0), "the circle went back");
            RunUntil(BossFight.Attack.Chase, 3f);
            Assert.That(_player.Current, Is.EqualTo(320f).Within(0.01f), "one boulder hits once");
        }

        [Test]
        public void Lobbed_KnightWalksOutOfTheCircle_Dodges()
        {
            StartFight(Data(new[] { Lobbed() }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Casting, 3f);
            _player.transform.position = new Vector3(3f, 0f, 6f); // 3 m: outside the 2.5 m circle
            Run(1.5f);
            Assert.That(_landed, Is.EqualTo(new[] { 0f }));
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Lobbed_Rubble_BlocksTheGround_PushesTheKnightOut_ThenSinksAfterItsLife()
        {
            StartFight(Data(new[] { Lobbed(life: 3f) }), knightDistance: 6f);
            _player.Invulnerable = true;
            RunUntil(BossFight.Attack.Casting, 3f);
            Run(1.2f);
            Assert.That(_landed.Count, Is.EqualTo(1));
            var spot = new Vector3(0f, 0f, 6f);
            Assert.That(_rocks.RubbleCount, Is.EqualTo(1));
            Assert.That(_rocks.IsBlocked(spot, 0.3f), Is.True, "rubble blocks the ground");
            Assert.That(_rocks.IsBlocked(spot + Vector3.right * 2f, 0.3f), Is.False, "only its own circle");
            Assert.That(ReferenceEquals(BossArena.Obstacles, _rocks), Is.True, "the arena asks the boulders");
            Assert.That(_rocks.SolidEnabled(0), Is.True, "its collider blocks the Knight");
            Assert.That(Planar(_player.transform.position, spot), Is.GreaterThanOrEqualTo(0.9f + BossBoulders.KnightRadius - 1e-3f),
                "the Knight was pushed out of the rubble");

            Run(3f - 0.2f);
            Assert.That(_rocks.RubbleCount, Is.EqualTo(1), "stays for its life");
            Run(0.3f);
            Assert.That(_rocks.RubbleCount, Is.EqualTo(0));
            Assert.That(_rocks.IsBlocked(spot, 0.3f), Is.False, "sinking rubble blocks nothing");
            Assert.That(_rocks.SolidEnabled(0), Is.False);
            Run(BossBoulders.SinkTime + 0.1f);
            Assert.That(_rockBodies[0].gameObject.activeSelf, Is.False, "back in the pool");
        }

        [Test]
        public void Lobbed_Rubble_IsAnArenaObstacle_AndCutsACharge()
        {
            var arena = Track(new GameObject("Arena")).AddComponent<BossArena>();
            arena.Setup(30f, new BossArena.Pillar[0], Vector3.zero, null, null, null, null, null, null);
            StartFight(Data(new[] { Lobbed(life: 8f) }), knightDistance: 6f);
            _player.Invulnerable = true;
            Assert.That(arena.IsBlocked(new Vector3(0f, 0f, 6f), 0.3f), Is.False);
            RunUntil(BossFight.Attack.Casting, 3f);
            Run(1.2f);
            Assert.That(arena.IsBlocked(new Vector3(0f, 0f, 6f), 0.3f), Is.True, "rubble = a pillar for the arena walkability");
            Enemy.Walkability = arena;
            float charge = BossFight.ChargeLength(Vector3.zero, Vector3.forward, 12f);
            Assert.That(charge, Is.LessThan(6f - 0.9f), "a charge stops at the rubble");
        }

        [Test]
        public void Lobbed_ThreeInARow_OnePerInterval_EachWarnsAndEachHits()
        {
            StartFight(Data(new[] { Lobbed(boulders: 3, interval: 0.6f, life: 0f, damage: 0.1f) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Casting, 3f);
            Assert.That(_thrown.Count, Is.EqualTo(1));
            Run(0.6f + Frame);
            Assert.That(_thrown.Count, Is.EqualTo(2), "the next one after the interval");
            Run(0.6f + Frame);
            Assert.That(_thrown.Count, Is.EqualTo(3));
            Assert.That(_pool.RentedCircles(), Is.GreaterThanOrEqualTo(1), "each boulder shows its own circle");
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_fight.Thrown, Is.EqualTo(3));
            Assert.That(_landed, Is.EqualTo(new[] { 40f, 40f, 40f }), "a standing Knight takes each boulder");
            Assert.That(_rocks.RubbleCount, Is.EqualTo(0), "life 0: no rubble");
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0));
        }

        [Test]
        public void Lobbed_NoFreeCircle_NoThrow()
        {
            StartFight(Data(new[] { Lobbed() }), knightDistance: 6f);
            var rented = new List<GroundTelegraph>();
            GroundTelegraph c;
            while ((c = _pool.RentCircle()) != null) rented.Add(c);
            Assert.That(_fight.ForceAttack(BossFight.Move.Lobbed), Is.False, "nothing lands without its warning");
            Run(3f);
            Assert.That(_started, Is.Empty);
            _pool.Release(rented[0]);
            Assert.That(_fight.ForceAttack(BossFight.Move.Lobbed), Is.True);
        }

        [Test]
        public void Lobbed_BossKilled_ClearsBouldersRubbleAndCircles()
        {
            StartFight(Data(new[] { Lobbed(boulders: 3, life: 8f) }), knightDistance: 6f);
            _player.Invulnerable = true;
            RunUntil(BossFight.Attack.Casting, 3f);
            Run(0.7f);
            Assert.That(_rocks.Flying, Is.GreaterThan(0));
            _fight.Boss.TakeDamage(_fight.Boss.Health + 1f, Vector3.zero, DamageKind.Burst);
            Assert.That(_rocks.Flying, Is.EqualTo(0));
            Assert.That(_rocks.RubbleCount, Is.EqualTo(0));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0));
            Run(2f);
            Assert.That(_landed, Is.Empty, "no boulder lands after the kill");
        }

        [Test]
        public void Lobbed_KnightDiesInTheWindUp_CircleGoesBack()
        {
            StartFight(Data(new[] { Lobbed() }), knightDistance: 6f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_pool.RentedCircles(), Is.EqualTo(1));
            _player.TakeDamage(_player.Max * 10f);
            Step();
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0));
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
        }

        // ------------------------------------------------------------ Summon

        [Test]
        public void Summon_WarnsWithCirclesOnARing_ThenSpawnsItsCountOfTheType()
        {
            StartFight(Data(new[] { Summon(count: 4) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Summon));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(4), "one circle where each will rise");
            foreach (var c in _circles)
                if (c.IsShowing) Assert.That(Planar(c.Centre, Vector3.zero), Is.EqualTo(2.6f).Within(0.01f), "on the ring");
            Assert.That(_spawner.InUse, Is.EqualTo(0), "nothing rises while it warns");
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_summoned, Is.EqualTo(new[] { 4 }));
            Assert.That(_spawner.InUse, Is.EqualTo(4));
            Assert.That(_fight.SummonsAlive, Is.EqualTo(4));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0), "circles went back");
            foreach (Transform child in _spawner.transform)
                Assert.That(child.name, Does.StartWith(GhoulName));
        }

        [Test]
        public void Summon_NeverMoreThanItsCountAlive_OnlyTheMissingOnesCome()
        {
            StartFight(Data(new[] { Summon(count: 4) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Recover, 3f);
            RunUntil(BossFight.Attack.Chase, 3f);
            Assert.That(_fight.ForceAttack(BossFight.Move.Summon), Is.False, "4 alive: no summon");
            Run(3f);
            Assert.That(_summoned.Count, Is.EqualTo(1));

            int killed = 0;
            foreach (Transform child in _spawner.transform)
            {
                var e = child.GetComponent<Enemy>();
                if (killed == 2 || !e.IsAlive) continue;
                e.TakeDamage(e.Health + 1f, Vector3.zero, DamageKind.Burst);
                killed++;
            }
            Assert.That(_fight.SummonsAlive, Is.EqualTo(2));
            RunUntil(BossFight.Attack.Recover, 5f);
            Assert.That(_summoned, Is.EqualTo(new[] { 4, 2 }), "only the two missing ones");
            Assert.That(_fight.SummonsAlive, Is.EqualTo(4));
        }

        [Test]
        public void Summon_UnknownType_IsNeverPicked()
        {
            StartFight(Data(new[] { Summon(type: "NoSuchZombie") }), knightDistance: 6f);
            Assert.That(_fight.ForceAttack(BossFight.Move.Summon), Is.False);
            Run(3f);
            Assert.That(_started, Is.Empty);
            Assert.That(_spawner.InUse, Is.EqualTo(0));
        }

        [Test]
        public void Summon_Phase2Only_WaitsForPhase2()
        {
            StartFight(Data(new[] { Summon(phases: BossPhases.Phase2) }), knightDistance: 6f);
            Run(3f);
            Assert.That(_started, Is.Empty, "phase 1: not on");
            _fight.Boss.TakeDamage(_fight.Boss.Health * 0.6f, Vector3.zero, DamageKind.Burst);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            RunUntil(BossFight.Attack.Recover, 5f);
            Assert.That(_summoned, Is.EqualTo(new[] { 4 }));
        }

        // ------------------------------------------------------------ Teleport

        [Test]
        public void Teleport_WarnsNearTheKnight_ShrinksAway_Reappears_GrowsBack()
        {
            StartFight(Data(new[] { Teleport() }), knightDistance: 6f);
            Vector3 size = _fight.Boss.transform.localScale;
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Teleport));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Vector3 to = _fight.TeleportTarget;
            Assert.That(_telegraph.IsShowing, Is.True);
            Assert.That(Planar(_telegraph.Centre, to), Is.LessThan(0.01f), "the circle warns where it comes back");
            Assert.That(Planar(to, _player.transform.position), Is.EqualTo(7f).Within(0.01f), "7 m from the Knight");
            Assert.That(Planar(to, Vector3.zero), Is.GreaterThanOrEqualTo(3f), "a real move");
            Assert.That(Mathf.Abs(to.x - _player.transform.position.x), Is.LessThanOrEqualTo(BossFight.TeleportMaxSide + 1e-3f),
                "in front of or behind the Knight: stays on the portrait screen");
            Assert.That(_fight.Boss.transform.localScale.x, Is.EqualTo(size.x).Within(1e-4f), "full size early in the wind-up");

            Run(_fight.WindUpTime - 3f * Frame);
            Assert.That(_fight.Boss.transform.localScale.x, Is.LessThan(size.x * 0.5f), "shrinks away before it goes");
            Assert.That(Planar(_fight.Boss.transform.position, Vector3.zero), Is.LessThan(0.01f), "still at the old spot");
            RunUntil(BossFight.Attack.Recover, 1f);
            Assert.That(_teleported, Is.EqualTo(new[] { 0f }));
            Assert.That(Planar(_fight.Boss.transform.position, to), Is.LessThan(0.01f), "reappears at the circle");
            Run(BossFight.VanishTime + 2f * Frame);
            Assert.That(_fight.Boss.transform.localScale.x, Is.EqualTo(size.x).Within(1e-4f), "grown back");
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "no damage when the entry has none");
        }

        [Test]
        public void Teleport_PicksOnlyWalkablePoints_AndNeedsOne()
        {
            Enemy.Walkability = new HalfBlocked { MaxX = 0f };
            StartFight(Data(new[] { Teleport() }), knightDistance: 6f);
            _player.Invulnerable = true;
            for (int i = 0; i < 6; i++)
            {
                RunUntil(BossFight.Attack.WindUp, 5f);
                Assert.That(_fight.TeleportTarget.x + 1.4f, Is.LessThanOrEqualTo(0.001f), "walkable side only");
                RunUntil(BossFight.Attack.Chase, 5f);
                _fight.Boss.transform.position = Vector3.zero;
                _player.transform.position = new Vector3(0f, 0f, 6f);
            }

            RunUntil(BossFight.Attack.Chase, 5f);
            Enemy.Walkability = new HalfBlocked { MaxX = -100f };
            Assert.That(_fight.ForceAttack(BossFight.Move.Teleport), Is.False, "no free point: no teleport");
        }

        [Test]
        public void Teleport_WithDamage_HitsOnArrivalInsideTheCircle()
        {
            StartFight(Data(new[] { Teleport(damage: 0.15f) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.WindUp);
            _player.transform.position = _fight.TeleportTarget + Vector3.right * 0.5f;
            RunUntil(BossFight.Attack.Recover, 2f);
            Assert.That(_teleported.Count, Is.EqualTo(1));
            Assert.That(_teleported[0], Is.EqualTo(60f).Within(0.01f), "15 % of 400");
        }

        [Test]
        public void Teleport_BossKilledWhileVanished_GetsItsSizeBack()
        {
            StartFight(Data(new[] { Teleport() }), knightDistance: 6f);
            Vector3 size = _fight.Boss.transform.localScale;
            RunUntil(BossFight.Attack.WindUp);
            Run(_fight.WindUpTime - 3f * Frame);
            Assert.That(_fight.Boss.transform.localScale.x, Is.LessThan(size.x * 0.5f));
            _fight.Boss.TakeDamage(_fight.Boss.Health + 1f, Vector3.zero, DamageKind.Burst);
            Assert.That(_fight.Boss.transform.localScale.x, Is.EqualTo(size.x).Within(1e-4f));
            Assert.That(_telegraph.IsShowing, Is.False);
        }

        // ------------------------------------------------------------ Data

        [Test]
        public void Samples_ObeyTheWarningFloor_TheHitCap_AndThePools()
        {
            var lob = BossAttack.Sample(BossFight.Move.Lobbed);
            var summon = BossAttack.Sample(BossFight.Move.Summon);
            var teleport = BossAttack.Sample(BossFight.Move.Teleport);
            var data = Data(new[] { lob, summon, teleport }, maxHitShare: 0.5f); // KAN-190: KAN-163 shares vs the shipped cap
            foreach (var a in data.Attacks)
            {
                Assert.That(data.TelegraphFor(a.Telegraph, 2), Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(a.damage, Is.LessThanOrEqualTo(data.MaxHitShare), a.ToString());
            }
            Assert.That(lob.type, Is.EqualTo(BossFight.Move.Lobbed));
            Assert.That(lob.duration, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), "a later boulder's warning = its flight");
            Assert.That(lob.radius, Is.EqualTo(2.5f), "Colossus boulder circle r 2.5 m (roster 3.14)");
            Assert.That(lob.obstacle, Is.EqualTo(8f), "rubble stays 8 s");
            Assert.That(summon.summon, Is.EqualTo("FrenziedGhoul"));
            Assert.That(summon.count, Is.EqualTo(4).And.LessThanOrEqualTo(BossFight.MaxSummons));
            Assert.That(teleport.damage, Is.EqualTo(0f));
        }

        [Test]
        public void AddAttack_ResolvesTheSummonType()
        {
            StartFight(Data(new[] { Teleport() }), knightDistance: 6f);
            int i = _fight.AddAttack(Summon(count: 2));
            Assert.That(_fight.ForceAttack(i), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_summoned, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void ThrowAndTeleportTicks_DoNotAllocate()
        {
            StartFight(Data(new[] { Lobbed(boulders: 3, life: 1f), Teleport() }), knightDistance: 6f);
            _player.Invulnerable = true;
            _started.Capacity = _teleported.Capacity = 512;
            _thrown.Capacity = _landed.Capacity = 2048;
            foreach (var c in _circles) // each circle makes its colour block once, on first show
            {
                c.Show(Vector3.zero, 1f);
                c.Hide();
            }
            Cycle(1200); // warm-up: both attack types (first calls JIT in the editor)
            Assert.That(_thrown.Count, Is.GreaterThan(0));
            Assert.That(_teleported.Count, Is.GreaterThan(0));
            Assert.That(() => Cycle(600), Is.Not.AllocatingGCMemory());
        }

        private void Cycle(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Step();
                if (_fight.CurrentAttack == BossFight.Attack.Chase)
                {
                    _fight.Boss.transform.position = Vector3.zero;
                    _player.transform.position = new Vector3(0f, 0f, 6f);
                }
            }
        }
    }
}
