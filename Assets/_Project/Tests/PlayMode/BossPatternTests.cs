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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-108 acceptance, logic side: the Warden's list is built from its old fields (its KAN-58 / KAN-61 tests stay green
    /// in BossTests / BossAttackTests); the pick takes the highest priority band in range, equal weights take turns, a
    /// higher weight comes round more often, phase gating holds; every new type warns at least 0.6 s; the lane hits only
    /// inside its rectangle, pillars shorten it, the hook drags the Knight in but not into the boss; the spin circle follows
    /// the Knight and hits at most once per interval; a multi-charge dashes n times (each warns), a blocked dash stuns and
    /// ends the combo; the phase 2 roar halts the boss once; the boss list picks a boss per boss Chamber; no GC per frame.
    /// The built Game scene runs The Butcher of Ashgrave with its clips. Look and feel: screenshots and a phone.
    /// </summary>
    public sealed class BossPatternTests
    {
        private const float Frame = 1f / 60f;
        private const int TestRealm = 96; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private SurvivalTimer _timer;
        private EnemySpawner _spawner;
        private SoulWispField _souls;
        private GroundTelegraph _telegraph;
        private LineTelegraph _lines;
        private BossProjectiles _bolts;
        private Enemy _body;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<int> _started = new List<int>();
        private readonly List<float> _lanes = new List<float>();
        private readonly List<float> _charges = new List<float>();
        private readonly List<float> _spinHits = new List<float>();

        /// <summary>One pillar (r 1) at (0, 0, 5): between a boss at the origin and a Knight further north.</summary>
        private sealed class PillarAhead : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r)
            {
                float dz = p.z - 5f;
                return p.x * p.x + dz * dz < (1f + r) * (1f + r);
            }
        }

        /// <summary>A wall: everything with z above 6 is blocked.</summary>
        private sealed class WallNorth : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r) => p.z > 6f - r;
        }

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(1080);

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
            _bolts.Setup(_player, null, 32);

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
            _lanes.Clear();
            _charges.Clear();
            _spinHits.Clear();
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

        private BossData Data(BossAttack[] attacks, float maxHitShare = 0.35f, float phaseRoar = 0f) =>
            Track(BossData.CreateWithAttacks(_body, attacks, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: 0.5f, maxHitShare: maxHitShare, phaseRoar: phaseRoar));

        private static BossAttack Chop(float damage = 0.2f, int priority = 1, float weight = 1f, string label = "Chop",
            BossPhases phases = BossPhases.Both, float maxRange = 6f) =>
            BossAttack.Lane(label, phases, 0f, maxRange, length: 6f, width: 1.6f, telegraph: 0.9f, damage: damage, recover: 0.5f,
                cooldown: 1f, priority: priority, weight: weight, clip: "Chop");

        private static BossAttack Hook(float pull = 4f) =>
            BossAttack.Lane("Hook", BossPhases.Both, 0f, 12f, length: 10f, width: 1f, telegraph: 0.8f, damage: 0.1f, recover: 0.7f,
                cooldown: 1f, pull: pull);

        private static BossAttack Spin(float damage = 0.08f) =>
            BossAttack.Spin("Spin", BossPhases.Both, 6f, radius: 3f, telegraph: 0.9f, duration: 2.5f, speed: 1.6f, interval: 0.6f,
                damage: damage, recover: 1f, cooldown: 1f, priority: 1);

        private static BossAttack Charge(int dashes = 1, float stun = 1f, BossPhases phases = BossPhases.Both) =>
            BossAttack.Charge("Charge", phases, 2f, 14f, telegraph: 0.75f, speed: 16f, distance: 10f, width: 2.6f, damage: 0.18f,
                recover: 1f, cooldown: 1f, dashes: dashes, stun: stun);

        /// <summary>Begin + rise; then the boss stands at the origin facing +Z with the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance = 3f, BossData[] roster = null, int chamber = 5)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, roster);
            _fight.AttackStarted += i => _started.Add(i);
            _fight.LaneStruck += (mid, dealt) => _lanes.Add(dealt);
            _fight.Charged += (end, dealt) => _charges.Add(dealt);
            _fight.SpinHit += (at, dealt) => _spinHits.Add(dealt);
            Assert.That(_fight.Begin(new ChamberPlan(1, chamber, ChamberType.Boss, false, 1f, 1f, 1f, 77)), Is.True);
            Run(data.IntroDelay + data.SpawnWarning + 2f * Frame);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            _fight.Boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _player.transform.position = new Vector3(0f, 0f, knightDistance);
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
            {
                _fight.Tick(Frame);
                _bolts.Tick(Frame);
            }
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame)
            {
                _fight.Tick(Frame);
                _bolts.Tick(Frame);
            }
        }

        private void HalfHealth() => _fight.Boss.TakeDamage(_fight.Boss.Health - _fight.Boss.MaxHealth * 0.5f, Vector3.zero);

        /// <summary>Lets every attack run to its end, keeping boss and Knight where they were, and records the pick order.</summary>
        private List<int> Picks(int count, float knightDistance)
        {
            var picks = new List<int>();
            for (int i = 0; i < count; i++)
            {
                RunUntil(BossFight.Attack.WindUp);
                Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.WindUp), $"attack {i} started");
                picks.Add(_fight.CurrentAttackIndex);
                RunUntil(BossFight.Attack.Chase);
                _fight.Boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _player.transform.position = new Vector3(0f, 0f, knightDistance);
                _player.Heal(1e6f);
            }
            return picks;
        }

        // ------------------------------------------------------------ Data and pick

        [Test]
        public void Warden_ListComesFromItsOldFields()
        {
            var data = Track(BossData.Create(_body, fanBolts: 5, charge: true));
            Assert.That(data.HasAttackList, Is.False);
            var list = data.Attacks;
            Assert.That(list.Length, Is.EqualTo(3));
            Assert.That(list[0].type, Is.EqualTo(BossFight.Move.Slam));
            Assert.That(list[0].priority, Is.GreaterThan(list[1].priority), "slam up close wins");
            Assert.That(list[0].maxRange, Is.EqualTo(data.SlamRange));
            Assert.That(list[0].radius, Is.EqualTo(data.SlamRadius));
            Assert.That(list[1].type, Is.EqualTo(BossFight.Move.Fan));
            Assert.That(list[1].count, Is.EqualTo(data.FanBolts));
            Assert.That(list[1].maxRange, Is.EqualTo(data.FanRange));
            Assert.That(list[2].type, Is.EqualTo(BossFight.Move.Charge));
            Assert.That(list[2].InPhase(1), Is.False, "the Warden charges in phase 2 only");
            Assert.That(list[2].InPhase(2), Is.True);
            Assert.That(list[2].minRange, Is.EqualTo(data.ChargeMinRange));

            var slamOnly = Track(BossData.Create(_body));
            Assert.That(slamOnly.Attacks.Length, Is.EqualTo(1), "no fan, no charge: slam only");
        }

        [Test]
        public void Pick_HigherPriorityBandWins_WhenInRange()
        {
            var far = Chop(priority: 0, label: "Far", maxRange: 20f);
            var close = Chop(priority: 1, label: "Close", maxRange: 4f);
            StartFight(Data(new[] { far, close }), knightDistance: 3f);
            Assert.That(Picks(2, 3f), Is.EqualTo(new[] { 1, 1 }), "up close: the priority 1 attack every time");
            _player.transform.position = new Vector3(0f, 0f, 8f);
            Assert.That(Picks(2, 8f), Is.EqualTo(new[] { 0, 0 }), "out of its range: the band below");
        }

        [Test]
        public void Pick_EqualWeightsTakeTurns_AndAHigherWeightComesRoundMoreOften()
        {
            StartFight(Data(new[] { Chop(label: "A"), Chop(label: "B") }));
            Assert.That(Picks(4, 3f), Is.EqualTo(new[] { 0, 1, 0, 1 }));
            TearDown();
            SetUp();
            StartFight(Data(new[] { Chop(label: "A", weight: 3f), Chop(label: "B") }));
            var picks = Picks(12, 3f);
            int a = picks.FindAll(i => i == 0).Count;
            Assert.That(a, Is.GreaterThanOrEqualTo(7), $"weight 3 vs 1: A most of the time ({string.Join(",", picks)})");
            Assert.That(picks, Has.Member(1), "B still comes round");
        }

        [Test]
        public void Pick_PhaseGating()
        {
            var phase1 = Chop(label: "P1", phases: BossPhases.Phase1);
            var phase2 = Chop(label: "P2", phases: BossPhases.Phase2);
            StartFight(Data(new[] { phase1, phase2 }));
            Assert.That(Picks(3, 3f), Is.EqualTo(new[] { 0, 0, 0 }));
            HalfHealth();
            Assert.That(Picks(3, 3f), Is.EqualTo(new[] { 1, 1, 1 }));
        }

        [Test]
        public void Pick_NothingInRange_KeepsChasing()
        {
            StartFight(Data(new[] { Chop(maxRange: 4f) }), knightDistance: 9f);
            Run(4f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_started, Is.Empty);
        }

        [Test]
        public void Roster_PicksABossPerBossChamber()
        {
            var a = Data(new[] { Chop() });
            var b = Data(new[] { Spin() });
            var fallback = Data(new[] { Hook() });
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(fallback, null, _timer, _spawner, _player, _souls, _telegraph, _lines, _bolts, new[] { a, b });
            Assert.That(_fight.BossFor(5), Is.SameAs(a), "1st boss Chamber");
            Assert.That(_fight.BossFor(10), Is.SameAs(b), "2nd");
            Assert.That(_fight.BossFor(15), Is.SameAs(a), "round");
            Assert.That(_fight.Begin(new ChamberPlan(1, 10, ChamberType.Boss, true, 1f, 1f, 1f, 7)), Is.True);
            Assert.That(_fight.Data, Is.SameAs(b));
            Assert.That(_fight.Attacks[0].type, Is.EqualTo(BossFight.Move.Spin));
        }

        // ------------------------------------------------------------ Lane (chop, hook)

        [Test]
        public void Lane_WarnsAtLeast06Seconds_HitsInsideOnce_Capped()
        {
            StartFight(Data(new[] { Chop(damage: 0.9f) }, maxHitShare: 0.3f));
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Lane));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_lines.IsShowing, Is.True);
            Assert.That(_lines.LaneCount, Is.EqualTo(1));
            Assert.That(_lines.Width, Is.EqualTo(1.6f).Within(1e-4f));
            Assert.That(_lines.Length, Is.EqualTo(6f).Within(0.26f));
            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "no damage while it warns");
            RunUntil(BossFight.Attack.Recover, 2f);
            Assert.That(_lanes.Count, Is.EqualTo(1));
            Assert.That(_lanes[0], Is.EqualTo(0.3f * _player.Max).Within(0.01f), "capped");
            Run(2f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.7f).Within(0.01f), "hit once");
        }

        [Test]
        public void Lane_Dodged_SidewaysOrBeyondItsEnd()
        {
            StartFight(Data(new[] { Chop() }));
            RunUntil(BossFight.Attack.WindUp);
            _player.transform.position = new Vector3(1.2f, 0f, 3f); // half width 0.8
            RunUntil(BossFight.Attack.Recover, 2f);
            Assert.That(_lanes, Is.EqualTo(new[] { 0f }));
            Assert.That(BossFight.InLane(Vector3.zero, Vector3.forward, 6f, 1.6f, new Vector3(0f, 0f, 6.5f)), Is.False, "past the end");
            Assert.That(BossFight.InLane(Vector3.zero, Vector3.forward, 6f, 1.6f, new Vector3(0f, 0f, -0.5f)), Is.False, "behind");
            Assert.That(BossFight.InLane(Vector3.zero, Vector3.forward, 6f, 1.6f, new Vector3(0.7f, 0f, 5.9f)), Is.True);
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Lane_APillarCutsIt_AndCovers()
        {
            Enemy.Walkability = new PillarAhead();
            StartFight(Data(new[] { Hook(pull: 0f) }), knightDistance: 8f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_lines.Length, Is.LessThan(4f), "the lane ends at the pillar");
            RunUntil(BossFight.Attack.Recover, 2f);
            Assert.That(_lanes, Is.EqualTo(new[] { 0f }), "the Knight behind the pillar is safe");
        }

        [Test]
        public void Hook_DragsTheKnightIn_ButNotIntoTheBoss()
        {
            StartFight(Data(new[] { Hook(pull: 4f) }), knightDistance: 8f);
            RunUntil(BossFight.Attack.WindUp);
            RunUntil(BossFight.Attack.Chase, 3f);
            Assert.That(_lanes.Count, Is.EqualTo(1));
            Assert.That(_lanes[0], Is.GreaterThan(0f));
            Assert.That(_player.transform.position.z, Is.EqualTo(4f).Within(0.05f), "pulled 4 m");

            _player.Heal(1e6f);
            _fight.Boss.transform.position = Vector3.zero;
            _player.transform.position = new Vector3(0f, 0f, 2f);
            RunUntil(BossFight.Attack.WindUp);
            RunUntil(BossFight.Attack.Chase, 3f);
            Assert.That(_lanes.Count, Is.EqualTo(2));
            Assert.That(_player.transform.position.z, Is.GreaterThanOrEqualTo(_fight.Boss.HitRadius + 0.79f), "stops outside the body");
        }

        // ------------------------------------------------------------ Spin

        [Test]
        public void Spin_WarnsAtLeast06Seconds_ThenFollowsTheKnight_AndHitsOncePerInterval()
        {
            StartFight(Data(new[] { Spin(damage: 0.05f) }), knightDistance: 2f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Spin));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_telegraph.IsShowing, Is.True);
            Assert.That(_telegraph.Radius, Is.EqualTo(3f).Within(1e-4f));
            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_spinHits, Is.Empty, "no damage while it warns");
            RunUntil(BossFight.Attack.Spinning, 1f);
            _player.transform.position = new Vector3(0f, 0f, 2.5f);
            Run(2.6f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover));
            Assert.That(_spinHits.Count, Is.InRange(4, 5), "2.5 s / 0.6 s interval");
            Assert.That(_fight.Boss.transform.position.z, Is.GreaterThan(0.5f), "the boss moved towards the Knight");
            Assert.That(_fight.Boss.transform.position.z, Is.LessThan(2.5f));
        }

        [Test]
        public void Spin_OutsideTheCircle_IsSafe_AndTheCircleMovesWithTheBoss()
        {
            StartFight(Data(new[] { Spin() }), knightDistance: 2f);
            RunUntil(BossFight.Attack.Spinning, 3f);
            _player.transform.position = new Vector3(0f, 0f, 9f); // the boss walks 1.6 m/s: 2.5 s = 4 m, never within 3 m of 9
            Run(1f);
            Assert.That(_telegraph.Centre.z, Is.EqualTo(_fight.Boss.transform.position.z).Within(0.01f));
            Run(2f);
            Assert.That(_spinHits, Is.Empty);
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        // ------------------------------------------------------------ Multi-charge, stun

        [Test]
        public void TripleCharge_DashesThreeTimes_EachWarns()
        {
            StartFight(Data(new[] { Charge(dashes: 3, stun: 1f) }), knightDistance: 8f);
            int warned = 0;
            _fight.ChargeWarned += (o, d, l) => warned++;
            for (float t = 0f; t < 10f && _charges.Count < 3; t += Frame)
            {
                if (_fight.CurrentAttack == BossFight.Attack.WindUp)
                    Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
                if (_fight.CurrentAttack == BossFight.Attack.Recover && _fight.DashesLeft > 0)
                {
                    _fight.Boss.transform.position = Vector3.zero; // keep room for the next dash
                    _player.transform.position = new Vector3(0f, 0f, 8f);
                }
                _fight.Tick(Frame);
            }
            Assert.That(_charges.Count, Is.EqualTo(3));
            Assert.That(_started.Count, Is.EqualTo(1), "one pick");
            Assert.That(_fight.DashesLeft, Is.EqualTo(0));
            RunUntil(BossFight.Attack.Chase, 3f);
            Assert.That(_charges.Count, Is.EqualTo(3));
        }

        [Test]
        public void Charge_IntoAWall_StunsTheBoss_AndEndsTheCombo()
        {
            Enemy.Walkability = new WallNorth();
            StartFight(Data(new[] { Charge(dashes: 3, stun: 1f) }), knightDistance: 4f);
            RunUntil(BossFight.Attack.WindUp);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_fight.DashBlocked, Is.True);
            Assert.That(_fight.DashesLeft, Is.EqualTo(0), "the crash ends the combo");
            Assert.That(_fight.RecoverTime, Is.EqualTo(2f).Within(1e-3f), "recover 1 s + stun 1 s");
            Assert.That(_fight.Boss.Halted, Is.True);
            Run(1.9f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover), "still stunned");
            Run(0.2f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_charges.Count, Is.EqualTo(1));
        }

        [Test]
        public void Charge_OnOpenGround_NoStun()
        {
            StartFight(Data(new[] { Charge(dashes: 1, stun: 1f) }), knightDistance: 8f);
            RunUntil(BossFight.Attack.WindUp);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_fight.DashBlocked, Is.False);
            Assert.That(_fight.RecoverTime, Is.EqualTo(1f).Within(1e-3f));
        }

        // ------------------------------------------------------------ Phase roar, kill, cost

        [Test]
        public void Phase2Roar_HaltsTheBossOnce_ThenTheFightGoesOn()
        {
            StartFight(Data(new[] { Chop() }, phaseRoar: 1f), knightDistance: 9f); // out of chop range: chasing
            HalfHealth();
            Assert.That(_fight.Phase, Is.EqualTo(2));
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover), "roaring");
            Assert.That(_fight.Boss.Halted, Is.True);
            Run(1f + 2f * Frame);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_fight.Boss.Halted, Is.False);
        }

        [Test]
        public void KillDuringSpin_HidesTheCircle()
        {
            StartFight(Data(new[] { Spin() }), knightDistance: 2f);
            RunUntil(BossFight.Attack.Spinning, 3f);
            Assert.That(_telegraph.IsShowing, Is.True);
            _fight.Boss.TakeDamage(1e9f, Vector3.zero);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_telegraph.IsShowing, Is.False);
        }

        [Test]
        public void DeadKnight_NoNewAttacks()
        {
            StartFight(Data(new[] { Chop(), Spin() }));
            _player.TakeDamage(1e6f);
            Run(5f);
            Assert.That(_started, Is.Empty);
        }

        [Test]
        public void PatternTicks_DoNotAllocate()
        {
            StartFight(Data(new[] { Spin(), Chop(), Hook(), Charge(dashes: 3) }), knightDistance: 2f);
            _player.Invulnerable = true;
            _started.Capacity = _lanes.Capacity = _charges.Capacity = _spinHits.Capacity = 512; // the test's own lists never grow
            Cycle(900); // warm-up: every attack type once (first calls JIT in the editor)
            Assert.That(() => Cycle(240), Is.Not.AllocatingGCMemory());
        }

        /// <summary>Ticks <paramref name="frames"/> frames; between attacks the Knight stands at 2, 5 or 8 m (every type comes up).</summary>
        private void Cycle(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _fight.Tick(Frame);
                if (_fight.CurrentAttack == BossFight.Attack.Chase)
                {
                    _fight.Boss.transform.position = Vector3.zero;
                    _player.transform.position = new Vector3(0f, 0f, 2f + (i / 60 % 3) * 3f);
                }
            }
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>
        /// The built Game scene: Chamber 5's boss is The Butcher of Ashgrave (roster body, attack list with lane, spin and
        /// charge, Roar intro); a forced chop plays the Chop state and its lane stays inside the arena.
        /// </summary>
        [UnityTest]
        public IEnumerator GameScene_Chamber5_IsTheButcher_WithItsClips()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 9002);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            Assert.That(fight.Data.DisplayName, Is.EqualTo("THE BUTCHER OF ASHGRAVE"));
            Assert.That(fight.Data.HasAttackList, Is.True);
            Assert.That(fight.Data.Prefab.name, Is.EqualTo("Enemy_AshgraveButcher"));
            Assert.That(fight.Data.MaxHealth, Is.EqualTo(8000f));
            var types = new HashSet<BossFight.Move>();
            foreach (var a in fight.Attacks)
            {
                types.Add(a.type);
                Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(a.damage, Is.LessThanOrEqualTo(fight.Data.MaxHitShare), a.ToString());
            }
            Assert.That(types, Is.SupersetOf(new[] { BossFight.Move.Lane, BossFight.Move.Spin, BossFight.Move.Charge }));

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f;
            while (fight.Current != BossFight.Stage.Intro && fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Intro));
            Assert.That(fight.Boss.AttackStateHash, Is.EqualTo(Animator.StringToHash("Roar")), "intro roar");
            wait = fight.Data.IntroTime + 2f;
            while (fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting));

            var knight = fight.Telegraph != null ? Object.FindAnyObjectByType<PlayerHealth>() : null;
            Assert.That(knight, Is.Not.Null);
            knight.Invulnerable = true;
            float limit = 8f; // an automatic attack may be running: wait for the chase
            while (!fight.ForceAttack(BossFight.Move.Lane) && limit > 0f)
            {
                limit -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.CurrentMove, Is.EqualTo(BossFight.Move.Lane));
            Assert.That(fight.Boss.AttackStateHash, Is.EqualTo(Animator.StringToHash("Chop")));
            Assert.That(fight.Lines.IsShowing, Is.True);
            Vector3 end = fight.Lines.Origin + fight.Lines.LaneDirection(0) * fight.Lines.Length;
            Assert.That(BossArena.Active.IsBlocked(end, 0f), Is.False, "the lane ends inside the ring");

            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(fight.Lines.IsShowing, Is.False);

            var empty = SceneManager.CreateScene("BossPatternEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
        /// <summary>
        /// KAN-162: in the built Game scene the Butcher's legs stop when a dash ends: at the Charged event the body already
        /// plays the Stand pose (open ground) or the Roar (crash stun), and the animator leaves the Charge run. Every attack of
        /// its list recovers on Stand / Roar, never on its own move clip looping in place.
        /// </summary>
        [UnityTest]
        public IEnumerator GameScene_Butcher_RecoversStanding_NotOnItsMoveClip()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 9003);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + fight.Data.IntroTime + 6f;
            while (fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            var knight = Object.FindAnyObjectByType<PlayerHealth>();
            knight.Invulnerable = true;
            var arena = BossArena.Active;
            Assert.That(arena, Is.Not.Null);

            int stand = Animator.StringToHash(Enemy.StandState), roar = Animator.StringToHash("Roar");
            int charge = Animator.StringToHash("Charge");
            int charged = 0, atEvent = 0;
            fight.Charged += (end, dealt) =>
            {
                charged++;
                atEvent = fight.Boss.AttackStateHash;
            };

            var attacks = fight.Attacks;
            for (int i = 0; i < attacks.Length; i++)
            {
                int dir = 0;
                float limit = 10f; // an automatic attack may be running: wait for the chase; a charge needs room
                while (limit > 0f)
                {
                    if (fight.CurrentAttack == BossFight.Attack.Chase)
                    {
                        float angle = (dir++ % 8) * 45f;
                        Vector3 d = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                        fight.Boss.transform.position = arena.Centre + d * 4f;
                        knight.transform.position = arena.Centre - d * 4f;
                        if (fight.ForceAttack(i)) break;
                    }
                    limit -= Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.That(fight.CurrentAttackIndex, Is.EqualTo(i), $"{attacks[i]} began");
                int before = charged;
                limit = 12f;
                while (fight.CurrentAttack != BossFight.Attack.Recover && limit > 0f)
                {
                    limit -= Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.That(fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover), $"{attacks[i]} recovers");
                int pose = fight.Boss.AttackStateHash;
                bool crashed = attacks[i].type == BossFight.Move.Charge && fight.DashBlocked;
                Assert.That(pose, Is.EqualTo(crashed ? roar : stand), $"{attacks[i]} recover pose (crash {crashed})");
                if (attacks[i].type == BossFight.Move.Charge)
                {
                    Assert.That(charged, Is.GreaterThan(before), "Charged fired");
                    Assert.That(atEvent, Is.Not.EqualTo(charge), "the run stops at the Charged event");
                    Assert.That(atEvent, Is.EqualTo(crashed ? roar : stand));
                }
                yield return null; // the animator takes the cross-fade on its next update
                var animator = fight.Boss.Animator;
                int playing = animator.IsInTransition(0)
                    ? animator.GetNextAnimatorStateInfo(0).shortNameHash
                    : animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
                Assert.That(playing, Is.EqualTo(pose), $"{attacks[i]}: the animator goes to the recover pose");
                limit = 12f;
                while (fight.CurrentAttack != BossFight.Attack.Chase && limit > 0f)
                {
                    limit -= Time.unscaledDeltaTime;
                    yield return null;
                }
                knight.Heal(1e6f);
            }

            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            var empty = SceneManager.CreateScene("BossRecoverEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
