using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
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
    /// KAN-61 (KAN-22.4) acceptance, logic side: phase 2 starts once at the phase share (faster walk, a heart drops),
    /// shortens wind-ups and cooldowns but never under 0.6 s; the fan warns at least 0.6 s with one lane per bolt, then
    /// fires the volley down the lanes; one volley hits once and never above the max-hit share; pillars stop bolts and end their lanes; the
    /// charge is phase 2 only, warns at least 0.6 s, dashes down its lane, hits once (capped), can be dodged and stops at
    /// a wall; in phase 2 fan and charge take turns; the kill clears bolts and warnings; no GC allocation per frame. The
    /// built Game scene wires the lanes and bolts on the boss. Look and feel: screenshots and a phone.
    /// </summary>
    public sealed class BossAttackTests
    {
        private const float Frame = 1f / 60f;
        private const int TestRealm = 97; // never touches the saved progress of a real Realm

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
        private readonly List<int> _phases = new List<int>();
        private readonly List<float> _boltHits = new List<float>();
        private readonly List<float> _charges = new List<float>();
        private int _fans, _blocked;

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
            Random.InitState(6161);

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
            _bolts.HitPlayer += (at, dealt) => _boltHits.Add(dealt);
            _bolts.Blocked += at => _blocked++;

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
            _phases.Clear();
            _boltHits.Clear();
            _charges.Clear();
            _fans = _blocked = 0;
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

        private BossData Data(int fanBolts = 5, bool charge = true, float fanDamage = 0.12f, float chargeDamage = 0.2f,
            float maxHitShare = 0.35f, float slamTelegraph = 1f, float fanTelegraph = 0.9f, float chargeTelegraph = 0.9f) =>
            Track(BossData.Create(_body, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f, slamTelegraph: slamTelegraph,
                maxHitShare: maxHitShare, slamRange: 4f, slamRadius: 3f, slamReach: 1.5f, firstSlamDelay: 0.5f,
                slamCooldown: 3f, slamRecover: 0.5f, fanBolts: fanBolts, fanTelegraph: fanTelegraph, fanDamage: fanDamage,
                charge: charge, chargeTelegraph: chargeTelegraph, chargeDamage: chargeDamage));

        /// <summary>Begin + intro + rise; then the boss stands at the origin facing +Z with the Knight 8 m north.</summary>
        private void StartFight(BossData data, float knightDistance = 8f)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts);
            _fight.PhaseChanged += p => _phases.Add(p);
            _fight.FanFired += (o, n) => _fans++;
            _fight.Charged += (end, dealt) => _charges.Add(dealt);
            Assert.That(_fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77)), Is.True);
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

        /// <summary>Runs until the fight reaches <paramref name="attack"/> (or <paramref name="limit"/> s pass).</summary>
        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame)
            {
                _fight.Tick(Frame);
                _bolts.Tick(Frame);
            }
        }

        private void HalfHealth() => _fight.Boss.TakeDamage(_fight.Boss.Health - _fight.Boss.MaxHealth * 0.5f, Vector3.zero);

        // ------------------------------------------------------------ Phase 2

        [Test]
        public void Phase2_StartsOnceAtTheShare_AndSpeedsTheWalk()
        {
            var data = Data();
            StartFight(data);
            Assert.That(_fight.Phase, Is.EqualTo(1));
            var boss = _fight.Boss;
            boss.TakeDamage(boss.MaxHealth * 0.49f, Vector3.zero);
            Assert.That(_fight.Phase, Is.EqualTo(1), "51 % is still phase 1");
            Assert.That(boss.SpeedBoost, Is.EqualTo(1f));
            boss.TakeDamage(boss.MaxHealth * 0.02f, Vector3.zero);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            Assert.That(_phases, Is.EqualTo(new[] { 2 }));
            Assert.That(boss.SpeedBoost, Is.EqualTo(data.Phase2Speed).Within(1e-4f));
            boss.TakeDamage(boss.MaxHealth * 0.1f, Vector3.zero);
            Assert.That(_phases.Count, Is.EqualTo(1), "phase 2 starts once");
        }

        [Test]
        public void Phase2_ShortensWindUpsAndCooldowns_ButNeverUnder06Seconds()
        {
            var data = Data(slamTelegraph: 1f);
            Assert.That(data.TelegraphFor(1f, 1), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(data.TelegraphFor(1f, 2), Is.EqualTo(data.Phase2Telegraph).Within(1e-4f));
            Assert.That(data.TelegraphFor(0.65f, 2), Is.EqualTo(BossData.MinTelegraph), "0.6 s floor in phase 2 too");
            Assert.That(data.CooldownFor(3f, 2), Is.EqualTo(3f * data.Phase2Cooldown).Within(1e-4f));
            Assert.That(data.CooldownFor(3f, 1), Is.EqualTo(3f));

            StartFight(data, knightDistance: 2.5f);
            HalfHealth();
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Slam));
            Assert.That(_fight.WindUpTime, Is.EqualTo(data.Phase2Telegraph).Within(1e-4f), "phase 2 slam warns shorter");
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
        }

        [Test]
        public void Phase2_DropsAHeart()
        {
            var data = Data();
            StartFight(data);
            var template = Track(new GameObject("HeartTemplate"));
            var prefab = template.AddComponent<HealingHeart>();
            template.SetActive(false);
            var hearts = Track(new GameObject("Hearts")).AddComponent<BossHearts>();
            hearts.Setup(Track(HeartRules.Create()), prefab, _player, _fight, 6161);
            int drops = 0;
            hearts.Dropped += (h, reason) => { if (reason == BossHearts.DropReason.Phase) drops++; };
            HalfHealth();
            Assert.That(drops, Is.EqualTo(1), "phase 2 drops one heart at once");
        }

        // ------------------------------------------------------------ Fan

        [Test]
        public void Fan_WarnsAtLeast06Seconds_ThenFiresOneBoltPerLane()
        {
            var data = Data(charge: false);
            StartFight(data);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Fan), "out of slam range: the fan");
            Assert.That(_fight.Boss.Halted, Is.True);
            Assert.That(_lines.IsShowing, Is.True);
            Assert.That(_lines.LaneCount, Is.EqualTo(data.FanBolts));
            Vector3 middle = _lines.LaneDirection(data.FanBolts / 2);
            Assert.That(Vector3.Angle(middle, Vector3.forward), Is.LessThan(0.5f), "middle lane aims at the Knight");
            Assert.That(Vector3.Angle(_lines.LaneDirection(0), _lines.LaneDirection(data.FanBolts - 1)),
                Is.EqualTo(data.FanSpread).Within(0.5f), "lanes cover the spread");

            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_bolts.Count, Is.EqualTo(0), "nothing flies inside the minimum warning");
            Run(data.FanTelegraph - BossData.MinTelegraph + 0.04f);
            Assert.That(_fans, Is.EqualTo(1));
            Assert.That(_bolts.Count, Is.EqualTo(data.FanBolts));
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover));
        }

        [Test]
        public void Fan_OneVolleyHitsOnce_AndNeverAboveTheMaxHitShare()
        {
            var data = Data(charge: false, fanDamage: 0.9f, maxHitShare: 0.3f);
            Assert.That(data.FanDamageFor(400f), Is.EqualTo(120f).Within(1e-3f), "no instant kill");
            StartFight(data, knightDistance: 5f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Fan));
            Run(data.FanTelegraph + 2f);
            Assert.That(_boltHits.Count, Is.EqualTo(1), "a close fan never stacks its bolts");
            Assert.That(_boltHits[0], Is.EqualTo(0.3f * _player.Max).Within(0.01f));
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.7f).Within(0.01f));
        }

        [Test]
        public void Fan_Dodged_WhenTheKnightLeavesTheLanes()
        {
            var data = Data(charge: false);
            StartFight(data);
            RunUntil(BossFight.Attack.WindUp);
            _player.transform.position = new Vector3(-8f, 0f, -3f); // behind the boss, out of every lane
            Run(data.FanTelegraph + 3f);
            Assert.That(_fans, Is.GreaterThanOrEqualTo(1));
            Assert.That(_boltHits, Is.Empty);
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Fan_PillarsStopTheBolts()
        {
            Enemy.Walkability = new PillarAhead();
            var data = Data(charge: false, fanBolts: 1);
            StartFight(data, knightDistance: 9f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Fan));
            Assert.That(_lines.LaneLength(0), Is.InRange(3.5f, 3.9f), "the lane ends at the pillar: cover reads before the volley");
            Run(data.FanTelegraph + 2f);
            Assert.That(_blocked, Is.EqualTo(1), "the bolt hit the pillar");
            Assert.That(_boltHits, Is.Empty, "cover works");
            Assert.That(_bolts.Count, Is.EqualTo(0));
        }

        [Test]
        public void DeadKnight_GetsNoFan()
        {
            var data = Data(charge: false);
            StartFight(data);
            _player.TakeDamage(1e6f);
            Run(6f);
            Assert.That(_fans, Is.EqualTo(0));
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
        }

        // ------------------------------------------------------------ Charge

        [Test]
        public void Charge_OnlyInPhase2()
        {
            var data = Data(fanBolts: 0, charge: true);
            StartFight(data);
            Run(8f);
            Assert.That(_charges, Is.Empty, "phase 1 has no charge");
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            HalfHealth();
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Charge));
        }

        [Test]
        public void Charge_WarnsAtLeast06Seconds_ThenDashesDownTheLane_AndHitsOnceCapped()
        {
            var data = Data(fanBolts: 0, charge: true, chargeDamage: 0.8f, maxHitShare: 0.3f);
            StartFight(data);
            HalfHealth();
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Charge));
            Assert.That(_lines.IsShowing, Is.True);
            Assert.That(_lines.LaneCount, Is.EqualTo(1));
            Assert.That(_lines.Width, Is.EqualTo(data.ChargeWidth).Within(1e-4f));
            Assert.That(_lines.Length, Is.EqualTo(data.ChargeDistance).Within(0.51f), "open ground: the full dash");
            Vector3 start = _fight.Boss.transform.position;

            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.WindUp));
            Assert.That(Vector3.Distance(_fight.Boss.transform.position, start), Is.LessThan(0.01f), "no move while it warns");
            Assert.That(_player.Current, Is.EqualTo(_player.Max));

            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_charges.Count, Is.EqualTo(1));
            Assert.That(_charges[0], Is.EqualTo(0.3f * _player.Max).Within(0.01f), "capped");
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.7f).Within(0.01f), "hit once");
            Assert.That(_fight.Boss.transform.position.z, Is.EqualTo(_lines.Length).Within(0.05f), "stopped at the lane end");
            Assert.That(_fight.Boss.Halted, Is.True, "punish window");
            Run(data.ChargeRecover + 2f * Frame);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_fight.Boss.Halted, Is.False);
        }

        [Test]
        public void Charge_Dodged_WhenTheKnightSidesteps()
        {
            var data = Data(fanBolts: 0, charge: true);
            StartFight(data);
            HalfHealth();
            RunUntil(BossFight.Attack.WindUp);
            _player.transform.position = new Vector3(data.ChargeWidth * 0.5f + 1.5f, 0f, 8f);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_charges, Is.EqualTo(new[] { 0f }));
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Charge_StopsBeforeAWall()
        {
            Enemy.Walkability = new WallNorth();
            float length = BossFight.ChargeLength(Vector3.zero, Vector3.forward, 12f);
            Assert.That(length, Is.LessThanOrEqualTo(6f - 1.1f + 1e-3f), "the boss body stays off the wall");
            Assert.That(length, Is.GreaterThan(4f));
            Assert.That(BossFight.ChargeLength(Vector3.zero, Vector3.back, 12f), Is.EqualTo(12f).Within(1e-3f));
        }

        [Test]
        public void Phase2_FanAndChargeTakeTurns()
        {
            var data = Data(fanBolts: 5, charge: true);
            StartFight(data);
            HalfHealth();
            var moves = new List<BossFight.Move>();
            for (int i = 0; i < 4; i++)
            {
                RunUntil(BossFight.Attack.WindUp);
                moves.Add(_fight.CurrentMove);
                RunUntil(BossFight.Attack.Chase);
                _fight.Boss.transform.position = Vector3.zero; // the charge moved it: same distance each round
                _player.transform.position = new Vector3(0f, 0f, 8f);
            }
            Assert.That(moves, Is.EqualTo(new[] { BossFight.Move.Charge, BossFight.Move.Fan, BossFight.Move.Charge, BossFight.Move.Fan }));
        }

        // ------------------------------------------------------------ Kill and cost

        [Test]
        public void Kill_ClearsBoltsAndWarnings()
        {
            var data = Data(charge: false);
            StartFight(data);
            RunUntil(BossFight.Attack.WindUp);
            Run(data.FanTelegraph + Frame);
            Assert.That(_bolts.Count, Is.GreaterThan(0));
            _fight.Boss.TakeDamage(1e9f, Vector3.zero);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_bolts.Count, Is.EqualTo(0), "no bolt lands after the kill");
            Assert.That(_fight.ForceAttack(BossFight.Move.Fan), Is.False);
        }

        [Test]
        public void KillDuringFanWindUp_HidesTheLanes()
        {
            StartFight(Data(charge: false));
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_lines.IsShowing, Is.True);
            _fight.Boss.TakeDamage(1e9f, Vector3.zero);
            Assert.That(_lines.IsShowing, Is.False);
        }

        [Test]
        public void FlyingBolts_DoNotAllocate()
        {
            StartFight(Data(charge: false));
            _player.transform.position = new Vector3(-8f, 0f, -3f);
            Assert.That(_fight.ForceAttack(BossFight.Move.Fan), Is.True);
            Run(_fight.WindUpTime + Frame); // fired; warm-up
            Assert.That(_bolts.Count, Is.GreaterThan(0));
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    _fight.Tick(Frame);
                    _bolts.Tick(Frame);
                    _lines.Tick(Frame);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Real scene

        // ------------------------------------------------------------ Charge sounds (KAN-168)

        private AudioClip Clip(string name) => Track(AudioClip.Create(name, 4410, 1, 44100, false));

        private ChargeSounds Sounds(out AudioClip rumble, out AudioClip scrape, out AudioClip dash, out AudioClip stop, out AudioClip crash)
        {
            rumble = Clip("rumble");
            scrape = Clip("scrape");
            dash = Clip("dash");
            stop = Clip("stop");
            crash = Clip("crash");
            var sounds = Track(new GameObject("ChargeSounds")).AddComponent<ChargeSounds>();
            sounds.Setup(_fight, rumble, new[] { scrape }, new[] { dash }, new[] { stop }, new[] { crash });
            return sounds;
        }

        private void RunWithSounds(ChargeSounds sounds, BossFight.Attack until, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != until; t += Frame)
            {
                _fight.Tick(Frame);
                _bolts.Tick(Frame);
                sounds.Tick();
            }
        }

        [Test]
        public void ChargeSounds_GrowlRisesWithTheFill_StopsAtTheDash_ThenTheStopThud()
        {
            var data = Data(fanBolts: 0, charge: true);
            StartFight(data);
            var sounds = Sounds(out var rumble, out var scrape, out var dash, out var stop, out _);
            HalfHealth();
            RunWithSounds(sounds, BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Charge));
            Assert.That(sounds.Rumbling, Is.True, "the warning starts the growl");
            Assert.That(sounds.Rumble.clip, Is.SameAs(rumble));
            Assert.That(sounds.Rumble.loop, Is.True);
            Assert.That(sounds.OneShots, Is.EqualTo(1));
            Assert.That(sounds.LastClip, Is.SameAs(scrape), "heavy foot-scrape with the warning");
            float pitch0 = sounds.Rumble.pitch, volume0 = sounds.Rumble.volume;

            for (float t = 0f; t < _fight.WindUpTime * 0.6f; t += Frame)
            {
                _fight.Tick(Frame);
                sounds.Tick();
            }
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.WindUp));
            Assert.That(_fight.WindUpProgress, Is.InRange(0.5f, 0.7f));
            Assert.That(sounds.Rumble.pitch, Is.GreaterThan(pitch0 + 0.2f), "pitch rises with the fill");
            Assert.That(sounds.Rumble.volume, Is.GreaterThan(volume0), "and it swells");

            RunWithSounds(sounds, BossFight.Attack.Dashing);
            Assert.That(sounds.Rumbling, Is.False, "the dash ends the growl");
            Assert.That(sounds.LastClip, Is.SameAs(dash));
            Assert.That(sounds.OneShots, Is.EqualTo(2));
            Assert.That(_fight.WindUpProgress, Is.EqualTo(0f));

            RunWithSounds(sounds, BossFight.Attack.Recover);
            Assert.That(_fight.DashBlocked, Is.False);
            Assert.That(sounds.LastClip, Is.SameAs(stop), "open ground: thud + skid, not the crash");
            Assert.That(sounds.OneShots, Is.EqualTo(3));
            Assert.That(sounds.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(2), "two voices per boss");
        }

        [Test]
        public void ChargeSounds_WallStop_PlaysTheCrash()
        {
            var data = Data(fanBolts: 0, charge: true);
            StartFight(data);
            Enemy.Walkability = new WallNorth();
            var sounds = Sounds(out _, out _, out _, out _, out var crash);
            HalfHealth();
            RunWithSounds(sounds, BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Charge));
            RunWithSounds(sounds, BossFight.Attack.Dashing);
            RunWithSounds(sounds, BossFight.Attack.Recover);
            Assert.That(_fight.DashBlocked, Is.True, "the wall cut the dash");
            Assert.That(sounds.LastClip, Is.SameAs(crash));
        }

        [Test]
        public void ChargeSounds_ManyCharges_ThreeOneShotsEach_NeverMoreVoices()
        {
            var data = Data(fanBolts: 0, charge: true);
            StartFight(data);
            var sounds = Sounds(out _, out _, out _, out _, out _);
            HalfHealth();
            int warnings = 0, dashes = 0;
            _fight.ChargeWarned += (o, d, l) => warnings++;
            _fight.DashStarted += (o, d) => dashes++;
            for (float t = 0f; t < 40f && _charges.Count < 4; t += Frame)
            {
                _player.Heal(1e6f);
                if (_fight.CurrentAttack == BossFight.Attack.Chase)
                    _fight.Boss.transform.position = Vector3.zero; // back in charge range (the dash ends next to the Knight)
                _fight.Tick(Frame);
                _bolts.Tick(Frame);
                sounds.Tick();
                if (_fight.CurrentAttack != BossFight.Attack.WindUp || _fight.CurrentMove != BossFight.Move.Charge)
                    Assert.That(sounds.Rumbling, Is.False, "the growl only plays during a charge warning");
            }
            Assert.That(_charges.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(dashes, Is.EqualTo(_charges.Count));
            Assert.That(sounds.OneShots, Is.EqualTo(warnings + dashes + _charges.Count), "one scrape, one dash, one end per charge");
            Assert.That(sounds.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(2));
        }

        [Test]
        public void ChargeSounds_KillDuringTheWarning_StopsTheGrowl_AndPauseKeepsIt()
        {
            var data = Data(fanBolts: 0, charge: true);
            StartFight(data);
            var sounds = Sounds(out _, out _, out _, out _, out _);
            HalfHealth();
            RunWithSounds(sounds, BossFight.Attack.WindUp);
            Assert.That(sounds.Rumbling, Is.True);
            Time.timeScale = 0f;
            float pitch = sounds.Rumble.pitch;
            sounds.Tick();
            Assert.That(sounds.Rumbling, Is.True, "a pause holds the growl, it does not end it");
            Assert.That(sounds.Rumble.pitch, Is.EqualTo(pitch).Within(1e-4f));
            Time.timeScale = 1f;
            _fight.Boss.TakeDamage(_fight.Boss.Health + 1f, Vector3.zero);
            _fight.Tick(Frame);
            sounds.Tick();
            Assert.That(sounds.Rumbling, Is.False, "the boss fell mid-warning");
        }

        /// <summary>The built Game scene: Chamber 5's boss has the lane telegraph and bolts wired; half health is phase 2 (KAN-108: the Butcher).</summary>
        [UnityTest]
        public IEnumerator GameScene_BossHasLanesBolts_AndPhase2()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 9001);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            Assert.That(fight.Lines, Is.Not.Null, "lane telegraph wired");
            Assert.That(fight.Lines.Capacity, Is.EqualTo(LineTelegraph.MaxLanes));
            Assert.That(fight.Projectiles, Is.Not.Null, "bolts wired");
            Assert.That(fight.Data.HasAttackList, Is.True, "KAN-108: the shipped boss (the Butcher) runs an attack list");
            Assert.That(fight.Data.Phase2At, Is.EqualTo(0.5f).Within(1e-4f));
            var chargeSounds = Object.FindAnyObjectByType<ChargeSounds>();
            Assert.That(chargeSounds, Is.Not.Null, "KAN-168: charge sounds in the scene");
            Assert.That(chargeSounds.Rumble.clip, Is.Not.Null, "growl loop wired");

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f;
            while (fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            var boss = fight.Boss;
            boss.TakeDamage(boss.Health - boss.MaxHealth * 0.5f, Vector3.zero);
            Assert.That(fight.Phase, Is.EqualTo(2));
            var hearts = fight.GetComponent<BossHearts>();
            Assert.That(hearts.OnGround.Count, Is.EqualTo(1), "phase 2 dropped a heart");

            // KAN-108: the Butcher has no fan; its chop lane in the arena ends at the ring or a pillar.
            float limit = 6f; // an automatic phase 2 attack may be running: wait for the chase
            while (!fight.ForceAttack(BossFight.Move.Lane) && limit > 0f)
            {
                limit -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.CurrentMove, Is.EqualTo(BossFight.Move.Lane));
            Assert.That(fight.Lines.IsShowing, Is.True);
            Vector3 end = fight.Lines.Origin + fight.Lines.LaneDirection(0) * fight.Lines.Length;
            Assert.That(BossArena.Active.IsBlocked(end, 0f), Is.False, "the lane ends inside the ring");

            boss.TakeDamage(boss.Health + 1f, Vector3.zero);
            Assert.That(fight.Projectiles.Count, Is.EqualTo(0));

            var empty = SceneManager.CreateScene("BossAttackEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
