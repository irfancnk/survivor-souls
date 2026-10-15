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
    /// KAN-116 acceptance, logic side: the Nova ring fires its bolts evenly round the body with its gaps, warns at least
    /// 0.6 s with a circle round the body and hits once; the Spiral fires its turning arms for its duration, stays inside
    /// the bolt pool and hits once in all; the Rain drops its circles one by one (the first on the Knight), each warns at
    /// least 0.6 s and hits only inside it, and gives every circle back to the shared pool (also when the boss or the Knight
    /// dies); no free circle = no rain; phase gating and cooldown hold; the Pyre Lord samples obey the warning floor and the
    /// hit cap; no GC per frame. Look and feel: screenshots (Docs/screenshots/kan116_*).
    /// </summary>
    public sealed class BossBulletPatternTests
    {
        private const float Frame = 1f / 60f;
        private const int Circles = 8;

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
        private Enemy _body;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<int> _started = new List<int>();
        private readonly List<int> _novas = new List<int>();
        private readonly List<int> _spirals = new List<int>();
        private readonly List<Vector3> _rainWarned = new List<Vector3>();
        private readonly List<float> _rainStruck = new List<float>();

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(1160);

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

            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false);
            _spawner = spawnerGo.AddComponent<EnemySpawner>();

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

            var circles = _circles = new GroundTelegraph[Circles];
            for (int i = 0; i < Circles; i++)
            {
                circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                circles[i].Setup(Quad(), Quad());
            }
            _pool = Track(new GameObject("Warnings")).AddComponent<EnemyTelegraphs>();
            _pool.Setup(new LineTelegraph[0], circles);

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
            _novas.Clear();
            _spirals.Clear();
            _rainWarned.Clear();
            _rainStruck.Clear();
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

        private static BossAttack Nova(int bolts = 24, int gaps = 0, float damage = 0.14f, float hitRadius = 0.6f,
            BossPhases phases = BossPhases.Both, float cooldown = 1f) =>
            BossAttack.Nova("Nova", phases, 9f, bolts, gaps, warnRadius: 3f, telegraph: 0.9f, speed: 8f, distance: 12f,
                hitRadius: hitRadius, damage: damage, recover: 0.5f, cooldown: cooldown);

        private static BossAttack Spiral(int arms = 2, float duration = 3f, float interval = 0.15f, float damage = 0.16f,
            float hitRadius = 0.55f, BossPhases phases = BossPhases.Both) =>
            BossAttack.Spiral("Spiral", phases, 12f, arms, turnSpeed: 110f, duration: duration, interval: interval, warnRadius: 3f,
                telegraph: 1f, speed: 6.5f, distance: 12f, hitRadius: hitRadius, damage: damage, recover: 1f, cooldown: 1f);

        private static BossAttack Rain(int circles = 6, float scatter = 2.5f, float telegraph = 1f, float damage = 0.1f) =>
            BossAttack.Rain("Rain", BossPhases.Both, 0f, 16f, circles, radius: 1.5f, scatter: scatter, interval: 0.35f,
                telegraph: telegraph, damage: damage, recover: 0.5f, cooldown: 1f);

        /// <summary>Begin + rise; then the boss stands at the origin facing +Z with the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance = 4f, EnemyTelegraphs pool = null)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null, pool ?? _pool);
            _fight.AttackStarted += i => _started.Add(i);
            _fight.NovaFired += (o, n) => _novas.Add(n);
            _fight.SpiralFired += (o, n) => _spirals.Add(n);
            _fight.RainWarned += (at, r) => _rainWarned.Add(at);
            _fight.RainStruck += (at, dealt) => _rainStruck.Add(dealt);
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
            _bolts.Tick(Frame);
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame) Step();
        }

        /// <summary>Angles (degrees, clockwise from +Z) of the live bolts round the origin, sorted.</summary>
        private List<float> BoltAngles()
        {
            var angles = new List<float>();
            for (int i = 0; i < _bolts.Count; i++)
            {
                Vector3 p = _bolts.PositionOf(i);
                float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
                angles.Add(a < 0f ? a + 360f : a);
            }
            angles.Sort();
            return angles;
        }

        /// <summary>Angular gaps between neighbouring bolts (wrapping round).</summary>
        private static List<float> Steps(List<float> sorted)
        {
            var steps = new List<float>();
            for (int i = 0; i < sorted.Count; i++)
                steps.Add(i + 1 < sorted.Count ? sorted[i + 1] - sorted[i] : sorted[0] + 360f - sorted[i]);
            return steps;
        }

        // ------------------------------------------------------------ Ring maths

        [Test]
        public void RingDirections_EvenRing_AndGapsOfTwoBolts()
        {
            var buffer = new Vector3[BossFight.MaxRingBolts];
            Assert.That(BossFight.RingDirections(buffer, 16, 0, 0f), Is.EqualTo(16));
            Assert.That(buffer[0].z, Is.EqualTo(1f).Within(1e-5f), "starts at +Z");
            Assert.That(Vector3.Angle(buffer[0], buffer[1]), Is.EqualTo(22.5f).Within(1e-3f));
            for (int i = 0; i < 16; i++) Assert.That(buffer[i].magnitude, Is.EqualTo(1f).Within(1e-5f));

            int n = BossFight.RingDirections(buffer, 24, 3, 0f);
            Assert.That(n, Is.EqualTo(24 - 3 * BossFight.GapBolts), "3 gaps of 2 bolts");
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Atan2(buffer[i].x, buffer[i].z) * Mathf.Rad2Deg;
                a = a < -0.01f ? a + 360f : a;
                foreach (float gap in new[] { 0f, 15f, 120f, 135f, 240f, 255f })
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(a, gap)), Is.GreaterThan(1f), $"slot at {gap} deg is a gap");
            }

            Assert.That(BossFight.RingDirections(buffer, 6, 5, 0f), Is.EqualTo(6 - 2 * BossFight.GapBolts),
                "never more gaps than the ring holds (bolts stay between them)");
            Assert.That(BossFight.RingDirections(buffer, 500, 0, 0f), Is.EqualTo(BossFight.MaxRingBolts), "capped");
            Assert.That(BossFight.RingDirections(buffer, 0, 0, 0f), Is.EqualTo(0));
        }

        // ------------------------------------------------------------ Nova

        [Test]
        public void Nova_WarnsAtLeast06Seconds_WithACircleRoundTheBody_ThenRingHitsOnce()
        {
            StartFight(Data(new[] { Nova(bolts: 24, damage: 0.9f, hitRadius: 1.5f) }, maxHitShare: 0.3f), knightDistance: 4f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Nova));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_telegraph.IsShowing, Is.True);
            Assert.That(_telegraph.Radius, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(new Vector2(_telegraph.Centre.x, _telegraph.Centre.z).magnitude, Is.LessThan(0.01f), "round the body");
            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_bolts.Count, Is.EqualTo(0), "no bolt while it warns");
            RunUntil(BossFight.Attack.Recover, 1f);
            Assert.That(_novas, Is.EqualTo(new[] { 24 }));
            Assert.That(_bolts.Count, Is.EqualTo(24));
            var steps = Steps(BoltAngles());
            foreach (float s in steps) Assert.That(s, Is.EqualTo(15f).Within(0.5f), "evenly round the body");
            Run(1.5f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.7f).Within(0.01f),
                "several bolts cross the Knight, one ring hits once (capped)");
        }

        [Test]
        public void Nova_Gaps_LeaveRoomToStand()
        {
            StartFight(Data(new[] { Nova(bolts: 24, gaps: 3) }));
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_novas, Is.EqualTo(new[] { 18 }));
            var steps = Steps(BoltAngles());
            int wide = steps.FindAll(s => s > 40f).Count;
            Assert.That(wide, Is.EqualTo(3), $"three gaps of 45 deg ({string.Join(",", steps)})");
        }

        [Test]
        public void Nova_DodgedOutsideItsReach_AndCooldownHolds()
        {
            StartFight(Data(new[] { Nova(cooldown: 2f) }), knightDistance: 4f);
            RunUntil(BossFight.Attack.Recover, 3f);
            _player.transform.position = new Vector3(0f, 0f, 14f); // bolts fly 12 m; out of the nova's 9 m range too
            Run(2f);
            Assert.That(_bolts.Count, Is.EqualTo(0), "every bolt flew out");
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "out of the bolts' reach");

            _player.transform.position = new Vector3(0f, 0f, 8f);
            _player.Invulnerable = true;
            RunUntil(BossFight.Attack.Recover, 3f); // the second nova
            Assert.That(_novas.Count, Is.EqualTo(2));
            RunUntil(BossFight.Attack.Chase, 2f);
            float chase = 0f;
            while (_fight.CurrentAttack == BossFight.Attack.Chase && chase < 5f)
            {
                _fight.Boss.transform.position = Vector3.zero;
                Step();
                chase += Frame;
            }
            Assert.That(chase, Is.EqualTo(2f).Within(0.05f), "cooldown 2 s before the next attack");
        }

        // ------------------------------------------------------------ Spiral

        [Test]
        public void Spiral_WarnsAtLeast06Seconds_FiresTurningArmsForItsDuration_HitsOnce()
        {
            StartFight(Data(new[] { Spiral(arms: 2, duration: 3f, interval: 0.15f, damage: 0.12f, hitRadius: 1.5f) }),
                knightDistance: 3f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Spiral));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_telegraph.IsShowing, Is.True);
            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_bolts.Count, Is.EqualTo(0), "no bolt while it warns");
            RunUntil(BossFight.Attack.Casting, 1f);
            Assert.That(_fight.Boss.Halted, Is.True, "stands while it casts");
            Quaternion before = _fight.Boss.transform.rotation;
            Run(0.5f);
            Assert.That(Quaternion.Angle(before, _fight.Boss.transform.rotation), Is.GreaterThan(30f), "the arms turn");
            Run(2.6f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover), "ends after its duration");
            Assert.That(_spirals.Count, Is.EqualTo(1));
            Assert.That(_spirals[0], Is.InRange(38, 42), "2 arms x 3 s / 0.15 s");
            Assert.That(_fight.SpiralBolts, Is.EqualTo(_spirals[0]));
            Run(2f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.88f).Within(0.01f), "a whole spiral hits once");
        }

        [Test]
        public void Spiral_StaysInsideTheBoltPool()
        {
            StartFight(Data(new[] { Spiral(arms: 6, duration: 3f, interval: 0.05f) }), knightDistance: 3f);
            _player.Invulnerable = true;
            RunUntil(BossFight.Attack.Casting, 3f);
            for (float t = 0f; t < 3.2f; t += Frame)
            {
                Step();
                Assert.That(_bolts.Count, Is.LessThanOrEqualTo(_bolts.Capacity));
            }
            Assert.That(_spirals.Count, Is.EqualTo(1));
            Assert.That(_spirals[0], Is.LessThan(6 * 60), "a full pool fires fewer");
        }

        [Test]
        public void Spiral_PhaseGating()
        {
            StartFight(Data(new[] { Spiral(phases: BossPhases.Phase2) }), knightDistance: 3f);
            Run(3f);
            Assert.That(_started, Is.Empty, "phase 2 only");
            _fight.Boss.TakeDamage(_fight.Boss.Health - _fight.Boss.MaxHealth * 0.5f, Vector3.zero);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            RunUntil(BossFight.Attack.WindUp, 3f);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Spiral));
        }

        // ------------------------------------------------------------ Rain

        [Test]
        public void Rain_CirclesFallOneByOne_FirstOnTheKnight_EachWarnsThenHitsInside()
        {
            StartFight(Data(new[] { Rain(circles: 6, damage: 0.05f) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Casting);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Rain));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), "each circle warns >= 0.6 s");
            Assert.That(_fight.Boss.Halted, Is.True);
            Step();
            Assert.That(_rainWarned.Count, Is.EqualTo(1));
            Assert.That(Vector3.Distance(_rainWarned[0], _player.transform.position), Is.LessThan(0.01f), "the first on the Knight");
            Assert.That(_pool.RentedCircles(), Is.EqualTo(1));
            Run(0.36f);
            Assert.That(_rainWarned.Count, Is.EqualTo(2), "one every 0.35 s");
            Run(BossData.MinTelegraph - 0.4f);
            Assert.That(_rainStruck, Is.Empty, "no hit while they warn");

            RunUntil(BossFight.Attack.Recover, 5f); // the Knight stood still
            Assert.That(_rainWarned.Count, Is.EqualTo(6));
            Assert.That(_rainStruck.Count, Is.EqualTo(6));
            for (int i = 0; i < 6; i++)
            {
                bool inside = Vector3.Distance(_rainWarned[i], _player.transform.position) <= 1.5f;
                Assert.That(_rainStruck[i] > 0f, Is.EqualTo(inside), $"circle {i} hits only inside");
                Assert.That(Vector3.Distance(_rainWarned[i], new Vector3(0f, 0f, 6f)), Is.LessThanOrEqualTo(2.5f + 1e-3f), "within the scatter");
            }
            Assert.That(_rainStruck[0], Is.EqualTo(0.05f * _player.Max).Within(0.01f));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0), "every circle back in the pool");
            Assert.That(_fight.RainLive, Is.EqualTo(0));
            RunUntil(BossFight.Attack.Chase, 2f);
            Assert.That(_fight.Boss.Halted, Is.False);
        }

        [Test]
        public void Rain_ARunningKnight_IsSafe()
        {
            StartFight(Data(new[] { Rain(circles: 4, scatter: 0f) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Casting);
            for (float t = 0f; t < 4f && _fight.CurrentAttack == BossFight.Attack.Casting; t += Frame)
            {
                _player.transform.position += new Vector3(4f * Frame, 0f, 0f); // 4 m/s sideways: out of each circle in time
                Step();
            }
            Assert.That(_rainStruck.Count, Is.EqualTo(4));
            Assert.That(_rainStruck, Is.All.EqualTo(0f));
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Rain_NoFreeCircle_NoRain()
        {
            var rented = new List<GroundTelegraph>();
            for (var c = _pool.RentCircle(); c != null; c = _pool.RentCircle()) rented.Add(c);
            Assert.That(rented.Count, Is.EqualTo(Circles));
            StartFight(Data(new[] { Rain() }), knightDistance: 6f);
            Run(3f);
            Assert.That(_started, Is.Empty, "never starts without its warning");
            foreach (var c in rented) _pool.Release(c);
            RunUntil(BossFight.Attack.Casting, 3f);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Rain));
        }

        [Test]
        public void Rain_KillOrDeadKnight_HidesAndReturnsTheCircles()
        {
            StartFight(Data(new[] { Rain() }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Casting);
            Run(0.8f);
            Assert.That(_pool.RentedCircles(), Is.GreaterThan(1));
            _fight.Boss.TakeDamage(1e9f, Vector3.zero);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0));
            Assert.That(_fight.RainLive, Is.EqualTo(0));

            TearDown();
            SetUp();
            StartFight(Data(new[] { Rain() }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Casting);
            Run(0.5f);
            _player.TakeDamage(1e6f);
            Step();
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0));
            Run(3f);
            Assert.That(_started.Count, Is.EqualTo(1), "nothing new on a dead Knight");
        }

        // ------------------------------------------------------------ Samples, tools, cost

        [Test]
        public void Samples_PyreLordNumbers_ObeyTheWarningFloorAndTheHitCap()
        {
            var nova = BossAttack.Sample(BossFight.Move.Nova);
            var spiral = BossAttack.Sample(BossFight.Move.Spiral);
            var rain = BossAttack.Sample(BossFight.Move.Rain);
            Assert.That(nova.type, Is.EqualTo(BossFight.Move.Nova));
            Assert.That(nova.count, Is.EqualTo(16), "ring of 16 fireballs (roster 3.13)");
            Assert.That(spiral.type, Is.EqualTo(BossFight.Move.Spiral));
            Assert.That(spiral.count, Is.EqualTo(2));
            Assert.That(spiral.duration, Is.EqualTo(3f));
            Assert.That(spiral.InPhase(1), Is.False);
            Assert.That(rain.type, Is.EqualTo(BossFight.Move.Rain));
            Assert.That(rain.count, Is.EqualTo(6));
            Assert.That(rain.Telegraph, Is.EqualTo(1f));
            var data = Data(new BossAttack[0]);
            foreach (var a in new[] { nova, spiral, rain })
            {
                Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(data.TelegraphFor(a.Telegraph, 2), Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(a.damage, Is.LessThanOrEqualTo(data.MaxHitShare), a.ToString());
            }
            int spiralBolts = Mathf.CeilToInt(spiral.duration / spiral.interval) * spiral.count;
            float airTime = spiral.length / spiral.speed;
            Assert.That(spiral.count / spiral.interval * airTime, Is.LessThanOrEqualTo(64), "fits the scene's 64-bolt pool");
            Assert.That(spiralBolts, Is.GreaterThan(0));
        }

        [Test]
        public void AddAttack_CopiesTheList_TheAssetStaysAsItIs()
        {
            var data = Data(new[] { Nova() });
            StartFight(data);
            int i = _fight.AddAttack(BossAttack.Sample(BossFight.Move.Rain));
            Assert.That(i, Is.EqualTo(1));
            Assert.That(_fight.Attacks.Length, Is.EqualTo(2));
            Assert.That(data.Attacks.Length, Is.EqualTo(1), "the asset's list is untouched");
            Assert.That(_fight.ForceAttack(i), Is.True);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Rain));
        }

        [Test]
        public void BulletPatternTicks_DoNotAllocate()
        {
            StartFight(Data(new[] { Nova(gaps: 3), Spiral(), Rain() }), knightDistance: 4f);
            _player.Invulnerable = true;
            _started.Capacity = _novas.Capacity = _spirals.Capacity = 512; // the test's own lists never grow
            _rainWarned.Capacity = _rainStruck.Capacity = 2048;
            foreach (var c in _circles) // each circle makes its colour block once, on first show
            {
                c.Show(Vector3.zero, 1f);
                c.Hide();
            }
            Cycle(1200); // warm-up: every attack type once (first calls JIT in the editor)
            Assert.That(_novas.Count + _spirals.Count, Is.GreaterThan(0));
            Assert.That(_rainWarned.Count, Is.GreaterThan(0));
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
                    _player.transform.position = new Vector3(0f, 0f, 4f);
                }
            }
        }
    }
}
