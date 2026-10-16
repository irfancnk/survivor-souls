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
    /// KAN-121 acceptance, logic side: the Shockwave warns at least 0.6 s with a circle round the slam point, then its rings
    /// grow one by one, each band hits once as it passes the Knight, a Knight outside the circle (or behind a passed front)
    /// is safe, every ring circle goes back to the shared pool (also on a kill), no free circles = no shockwave. Homing orbs
    /// warn with a circle round the body, turn towards the Knight at most their turn rate, a running Knight outpaces them,
    /// one volley hits once. Beams warn with lanes round the body (the Knight between two), then turn as live beams for
    /// their duration and hit once; outside their length is safe; a kill hides them. Cooldown and phase gating hold for each
    /// type; the Colossus / Matriarch samples obey the warning floor and the hit cap; no GC per frame. Look and feel:
    /// screenshots (Docs/screenshots/game-boss-shockwave*, -homing*, -beams*).
    /// </summary>
    public sealed class BossWaveHomingBeamTests
    {
        private const float Frame = 1f / 60f;
        private const int Circles = 8;
        private const float KnightSpeed = 5f; // PlayerMover top speed

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
        private readonly List<float> _waveHits = new List<float>();
        private readonly List<int> _shockwaves = new List<int>();
        private readonly List<int> _homing = new List<int>();
        private readonly List<int> _beamsWarned = new List<int>();
        private readonly List<float> _beamsEnded = new List<float>();

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(1210);

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
            _waveHits.Clear();
            _shockwaves.Clear();
            _homing.Clear();
            _beamsWarned.Clear();
            _beamsEnded.Clear();
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

        private static BossAttack Shockwave(int rings = 2, float radius = 5f, float reach = 1f, float interval = 0.6f,
            float damage = 0.1f, BossPhases phases = BossPhases.Both, float cooldown = 1f, float speed = 7f) =>
            BossAttack.Shockwave("Shockwave", phases, 0f, 30f, rings, radius, reach, speed: speed, band: 0.8f, interval: interval,
                telegraph: 1f, damage: damage, recover: 0.5f, cooldown: cooldown);

        private static BossAttack Homing(int orbs = 3, float turn = 90f, float speed = 4f, float distance = 20f,
            float damage = 0.12f, BossPhases phases = BossPhases.Both, float cooldown = 1f) =>
            BossAttack.Homing("Homing", phases, 0f, 30f, orbs, spread: 60f, turn: turn, speed: speed, distance: distance,
                hitRadius: 0.6f, warnRadius: 2.2f, telegraph: 0.8f, damage: damage, recover: 0.5f, cooldown: cooldown);

        private static BossAttack Beams(int lanes = 4, float length = 9f, float duration = 4f, float damage = 0.16f,
            BossPhases phases = BossPhases.Both, float cooldown = 1f) =>
            BossAttack.Beams("Beams", phases, 0f, 30f, lanes, length, width: 0.9f, turnSpeed: 45f, duration: duration,
                telegraph: 1f, damage: damage, recover: 0.5f, cooldown: cooldown);

        private static BossAttack OfType(BossFight.Move move, BossPhases phases = BossPhases.Both, float cooldown = 1f)
        {
            switch (move)
            {
                case BossFight.Move.Shockwave: return Shockwave(phases: phases, cooldown: cooldown);
                case BossFight.Move.Homing: return Homing(phases: phases, cooldown: cooldown);
                default: return Beams(phases: phases, cooldown: cooldown, duration: 1f);
            }
        }

        /// <summary>Begin + rise; then the boss stands at the origin facing +Z with the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance = 4f)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null, _pool);
            _fight.AttackStarted += i => _started.Add(i);
            _fight.WaveHit += (at, dealt) => _waveHits.Add(dealt);
            _fight.ShockwaveEnded += (c, rings, dealt) => _shockwaves.Add(rings);
            _fight.HomingFired += (o, n) => _homing.Add(n);
            _fight.BeamsWarned += (o, n) => _beamsWarned.Add(n);
            _fight.BeamsEnded += (o, dealt) => _beamsEnded.Add(dealt);
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

        private static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ------------------------------------------------------------ Shockwave

        [Test]
        public void Shockwave_WarnsAtLeast06Seconds_ThenRingsGrowOneByOne_EachHitsOnce()
        {
            StartFight(Data(new[] { Shockwave(rings: 2, radius: 5f, reach: 1f, interval: 0.6f, damage: 0.1f, speed: 5f) }),
                knightDistance: 3f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Shockwave));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_telegraph.IsShowing, Is.True);
            Assert.That(_telegraph.Radius, Is.EqualTo(5f).Within(1e-4f), "the circle is the whole danger area");
            Assert.That(Vector3.Distance(_telegraph.Centre, new Vector3(0f, 0.05f, 1f)), Is.LessThan(0.01f), "reach in front of the boss");
            Assert.That(_fight.Boss.Halted, Is.True);
            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_fight.WavesLive, Is.EqualTo(0), "no ring while it warns");
            Assert.That(_player.Current, Is.EqualTo(_player.Max));

            RunUntil(BossFight.Attack.Casting, 1f);
            Step();
            Assert.That(_fight.WavesLive, Is.EqualTo(1), "the first ring at once");
            Assert.That(_pool.RentedCircles(), Is.EqualTo(1));
            Assert.That(System.Array.Exists(_circles, c => c.IsShowing), Is.True, "the ring shows");
            Run(0.3f);
            Assert.That(_fight.WaveFront(1), Is.EqualTo(-1f), "not yet");
            Run(0.32f);
            Assert.That(_fight.WaveFront(1), Is.GreaterThanOrEqualTo(0f), "the second 0.6 s later");
            Assert.That(_fight.WaveFront(0), Is.GreaterThan(_fight.WaveFront(1)), "the first ring is ahead");

            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_shockwaves, Is.EqualTo(new[] { 2 }));
            Assert.That(_waveHits.Count, Is.EqualTo(2), "a Knight standing 2 m from the centre: each ring hits once");
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.8f).Within(0.01f));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0), "every ring circle back in the pool");
            Assert.That(_fight.WavesLive, Is.EqualTo(0));
            RunUntil(BossFight.Attack.Chase, 2f);
            Assert.That(_fight.Boss.Halted, Is.False);
        }

        [Test]
        public void Shockwave_OutsideTheCircle_IsSafe_AndAPassedFrontNeverHits()
        {
            StartFight(Data(new[] { Shockwave(rings: 2, radius: 5f, reach: 1f) }), knightDistance: 3f);
            RunUntil(BossFight.Attack.Casting);
            _player.transform.position = new Vector3(0f, 0f, 1f + 5.3f); // just outside the warning circle
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_shockwaves.Count, Is.EqualTo(1));
            Assert.That(_waveHits, Is.Empty, "outside the circle is safe");
            Assert.That(_player.Current, Is.EqualTo(_player.Max));

            TearDown();
            SetUp();
            StartFight(Data(new[] { Shockwave(rings: 1, radius: 5f, reach: 1f) }), knightDistance: 6.5f);
            RunUntil(BossFight.Attack.Casting);
            while (_fight.WaveFront(0) < 3f && _fight.CurrentAttack == BossFight.Attack.Casting) Step();
            _player.transform.position = new Vector3(0f, 0f, 1f + 1f); // steps in behind the front
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_waveHits, Is.Empty, "the band already passed that ground");
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Shockwave_NoFreeCircles_NeverStarts_KillReturnsTheRings()
        {
            var rented = new List<GroundTelegraph>();
            for (int i = 0; i < Circles - 1; i++) rented.Add(_pool.RentCircle()); // one left, two rings need two
            StartFight(Data(new[] { Shockwave(rings: 2) }), knightDistance: 3f);
            Run(3f);
            Assert.That(_started, Is.Empty, "never starts without a circle for every ring");
            foreach (var c in rented) _pool.Release(c);
            RunUntil(BossFight.Attack.Casting, 3f);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Shockwave));
            Run(0.62f);
            Assert.That(_pool.RentedCircles(), Is.EqualTo(2));
            _fight.Boss.TakeDamage(1e9f, Vector3.zero);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_pool.RentedCircles(), Is.EqualTo(0), "a kill returns the rings");
            Assert.That(_fight.WavesLive, Is.EqualTo(0));
            foreach (var c in _circles) Assert.That(c.IsShowing, Is.False);
        }

        // ------------------------------------------------------------ Homing

        [Test]
        public void Homing_WarnsAtLeast06Seconds_ThenOrbsTurnTowardsTheKnight_VolleyHitsOnce()
        {
            StartFight(Data(new[] { Homing(orbs: 3, turn: 90f, speed: 4f, damage: 0.12f, cooldown: 30f) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Homing));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_telegraph.IsShowing, Is.True);
            Assert.That(_telegraph.Radius, Is.EqualTo(2.2f).Within(1e-4f), "a circle round the body");
            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_bolts.Count, Is.EqualTo(0), "no orb while it warns");
            RunUntil(BossFight.Attack.Recover, 1f);
            Assert.That(_homing, Is.EqualTo(new[] { 3 }));
            Assert.That(_bolts.Count, Is.EqualTo(3));
            var yaws = new List<float>();
            for (int i = 0; i < 3; i++) yaws.Add(Yaw(_bolts.VelocityOf(i)));
            yaws.Sort();
            Assert.That(yaws[0], Is.EqualTo(-30f).Within(2f), "spread over 60 degrees at the Knight");
            Assert.That(yaws[2], Is.EqualTo(30f).Within(2f));

            _player.transform.position = new Vector3(6f, 0f, 3f); // steps east: the orbs follow
            Run(0.5f);
            for (int i = 0; i < _bolts.Count; i++) Assert.That(_bolts.VelocityOf(i).x, Is.GreaterThan(0f), $"orb {i} turns east");
            for (int i = 0; i < _bolts.Count; i++)
                Assert.That(_bolts.VelocityOf(i).magnitude, Is.EqualTo(4f).Within(1e-3f), "speed kept while turning");
            Run(4f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.88f).Within(0.01f), "the orbs converge, one volley hits once");
        }

        [Test]
        public void Homing_TurnRateIsCapped_AndARunningKnightOutpacesThem()
        {
            StartFight(Data(new[] { Homing(orbs: 3, turn: 60f, speed: 3.6f, distance: 16f, cooldown: 30f) }), knightDistance: 6f);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_bolts.Count, Is.EqualTo(3));
            _player.transform.position = new Vector3(0f, 0f, -6f); // right behind the boss: the orbs must turn round
            var before = new Vector3[3];
            for (int i = 0; i < 3; i++) before[i] = _bolts.VelocityOf(i);
            Step();
            for (int i = 0; i < 3; i++)
            {
                Assert.That(Vector3.Angle(before[i], _bolts.VelocityOf(i)), Is.LessThanOrEqualTo(60f * Frame + 0.01f), "turn rate cap");
                Assert.That(_bolts.VelocityOf(i).y, Is.EqualTo(0f), "stays on the ground plane");
            }

            _player.transform.position = new Vector3(0f, 0f, 8f);
            for (float t = 0f; t < 5f; t += Frame) // runs north at the Knight's top speed
            {
                _player.transform.position += new Vector3(0f, 0f, KnightSpeed * Frame);
                _bolts.Tick(Frame);
            }
            Assert.That(_bolts.Count, Is.EqualTo(0), "the orbs flew their 16 m");
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "3.6 m/s orbs never catch a Knight running at 5 m/s");
        }

        // ------------------------------------------------------------ Beams

        [Test]
        public void Beams_LanesWarnRoundTheBody_KnightBetweenTwo_ThenTurnAndHitOnce()
        {
            StartFight(Data(new[] { Beams(lanes: 4, length: 9f, duration: 4f, damage: 0.16f) }), knightDistance: 4f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Beams));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_beamsWarned, Is.EqualTo(new[] { 4 }));
            Assert.That(_lines.IsShowing, Is.True);
            Assert.That(_lines.LaneCount, Is.EqualTo(4));
            var yaws = new List<float>();
            for (int i = 0; i < 4; i++) yaws.Add(Yaw(_lines.LaneDirection(i)));
            foreach (float y in yaws) Assert.That(Mathf.Abs(Mathf.DeltaAngle(y, 0f)), Is.GreaterThanOrEqualTo(44f), "the Knight starts in a gap");
            yaws.Sort();
            for (int i = 1; i < 4; i++) Assert.That(yaws[i] - yaws[i - 1], Is.EqualTo(90f).Within(0.5f), "evenly round the body");
            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "no hit while they warn");

            RunUntil(BossFight.Attack.Casting, 1f);
            Assert.That(_lines.IsHeld, Is.True, "live beams");
            Assert.That(Mathf.Abs(_fight.BeamTurn), Is.EqualTo(45f));
            float lane0 = Yaw(_lines.LaneDirection(0));
            Run(0.5f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(lane0, Yaw(_lines.LaneDirection(0)))), Is.EqualTo(22.5f).Within(1.5f), "45 deg/s");
            Assert.That(_fight.Boss.Halted, Is.True);
            RunUntil(BossFight.Attack.Recover, 4f);
            Assert.That(_beamsEnded.Count, Is.EqualTo(1));
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.84f).Within(0.01f),
                "a beam sweeps over the standing Knight (more than once in 4 s), the cast hits once");
            Assert.That(_beamsEnded[0], Is.EqualTo(_player.Max * 0.16f).Within(0.01f));
            Assert.That(_lines.IsHeld, Is.False, "the beams fade out");
        }

        [Test]
        public void Beams_OutsideTheirLength_IsSafe_OddCountStartsBetweenLanes()
        {
            StartFight(Data(new[] { Beams(lanes: 3, length: 9f) }), knightDistance: 10f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_lines.LaneCount, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(Yaw(_lines.LaneDirection(i)), 0f)), Is.GreaterThanOrEqualTo(59f), "no lane on the Knight");
            RunUntil(BossFight.Attack.Recover, 6f);
            Assert.That(_beamsEnded, Is.EqualTo(new[] { 0f }), "10 m away, beams of 9 m");
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Beams_KillOrDeadKnight_HidesThem()
        {
            StartFight(Data(new[] { Beams() }), knightDistance: 12f);
            RunUntil(BossFight.Attack.Casting);
            _fight.Boss.TakeDamage(1e9f, Vector3.zero);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_lines.IsShowing, Is.False);

            TearDown();
            SetUp();
            StartFight(Data(new[] { Beams() }), knightDistance: 12f);
            RunUntil(BossFight.Attack.Casting);
            _player.TakeDamage(1e6f);
            Step();
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_lines.IsShowing, Is.False);
            Run(3f);
            Assert.That(_started.Count, Is.EqualTo(1), "nothing new on a dead Knight");
        }

        // ------------------------------------------------------------ Every type: cooldown, phase gating

        [TestCase(BossFight.Move.Shockwave)]
        [TestCase(BossFight.Move.Homing)]
        [TestCase(BossFight.Move.Beams)]
        public void EachType_CooldownHolds(BossFight.Move move)
        {
            StartFight(Data(new[] { OfType(move, cooldown: 2f) }), knightDistance: 12f); // out of every hit area
            _player.Invulnerable = true;
            RunUntil(BossFight.Attack.Recover, 8f);
            Assert.That(_fight.CurrentMove, Is.EqualTo(move));
            RunUntil(BossFight.Attack.Chase, 3f);
            float chase = 0f;
            while (_fight.CurrentAttack == BossFight.Attack.Chase && chase < 5f)
            {
                _fight.Boss.transform.position = Vector3.zero;
                _player.transform.position = new Vector3(0f, 0f, 12f);
                Step();
                chase += Frame;
            }
            Assert.That(chase, Is.EqualTo(2f).Within(0.05f), "cooldown 2 s before the next attack");
            Assert.That(_started.Count, Is.EqualTo(2));
        }

        [TestCase(BossFight.Move.Shockwave)]
        [TestCase(BossFight.Move.Homing)]
        [TestCase(BossFight.Move.Beams)]
        public void EachType_PhaseGating(BossFight.Move move)
        {
            StartFight(Data(new[] { OfType(move, BossPhases.Phase2) }), knightDistance: 4f);
            Run(3f);
            Assert.That(_started, Is.Empty, "phase 2 only");
            _fight.Boss.TakeDamage(_fight.Boss.Health - _fight.Boss.MaxHealth * 0.5f, Vector3.zero);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            RunUntil(BossFight.Attack.WindUp, 3f);
            Assert.That(_fight.CurrentMove, Is.EqualTo(move));
        }

        // ------------------------------------------------------------ Samples, tools, cost

        [Test]
        public void Samples_ColossusAndMatriarchNumbers_ObeyTheWarningFloorAndTheHitCap()
        {
            var wave = BossAttack.Sample(BossFight.Move.Shockwave);
            var homing = BossAttack.Sample(BossFight.Move.Homing);
            var beams = BossAttack.Sample(BossFight.Move.Beams);
            Assert.That(wave.type, Is.EqualTo(BossFight.Move.Shockwave));
            Assert.That(wave.count, Is.EqualTo(2), "double slam: two rings (roster 3.14)");
            Assert.That(homing.type, Is.EqualTo(BossFight.Move.Homing));
            Assert.That(homing.count, Is.EqualTo(3), "3 skulls at a time (roster 3.15)");
            Assert.That(homing.speed, Is.LessThan(KnightSpeed), "dodgeable at the Knight's speed");
            Assert.That(homing.turn, Is.GreaterThan(0f));
            Assert.That(beams.type, Is.EqualTo(BossFight.Move.Beams));
            Assert.That(beams.count, Is.EqualTo(4));
            Assert.That(beams.duration, Is.EqualTo(4f));
            Assert.That(beams.InPhase(1), Is.False, "phase 2 only");
            var data = Data(new BossAttack[0], maxHitShare: 0.5f); // KAN-190: samples carry the KAN-163 shares, checked against the shipped cap
            foreach (var a in new[] { wave, homing, beams })
            {
                Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(data.TelegraphFor(a.Telegraph, 2), Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(a.damage, Is.LessThanOrEqualTo(data.MaxHitShare), a.ToString());
            }
            Assert.That(wave.count, Is.LessThanOrEqualTo(BossFight.MaxWaves));
            Assert.That(beams.count, Is.LessThanOrEqualTo(LineTelegraph.MaxLanes));
        }

        [Test]
        public void WaveHomingBeamTicks_DoNotAllocate()
        {
            StartFight(Data(new[] { Shockwave(), Homing(), Beams(duration: 1.5f) }), knightDistance: 4f);
            _player.Invulnerable = true;
            _started.Capacity = _shockwaves.Capacity = _homing.Capacity = _beamsWarned.Capacity = 512; // the test's own lists never grow
            _waveHits.Capacity = _beamsEnded.Capacity = 2048;
            foreach (var c in _circles) // each circle makes its colour block once, on first show
            {
                c.ShowWave(Vector3.zero, 1f, 0f);
                c.Show(Vector3.zero, 1f);
                c.Hide();
            }
            Cycle(1500); // warm-up: every attack type once (first calls JIT in the editor)
            Assert.That(_shockwaves.Count, Is.GreaterThan(0));
            Assert.That(_homing.Count, Is.GreaterThan(0));
            Assert.That(_beamsEnded.Count, Is.GreaterThan(0));
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
