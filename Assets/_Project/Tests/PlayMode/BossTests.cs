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
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-58 (KAN-22 first slice) acceptance, logic side: only boss Chambers start a fight; a boss Chamber has no clock
    /// and a thinner horde; the boss rises after the intro delay and a ground warning, with BossData health x the Chamber
    /// multiplier; the slam always warns at least 0.6 s, hits only inside its circle, never more than the max-hit share,
    /// and never a dead Knight; the kill drops the XP reward as gold wisps and opens the portal once; the spawn point
    /// avoids blocked ground; a halted enemy does not walk. Look and feel: screenshots and a phone.
    /// </summary>
    public sealed class BossTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private SurvivalTimer _timer;
        private EnemySpawner _spawner;
        private SoulWispField _souls;
        private GroundTelegraph _telegraph;
        private Enemy _body;
        private BossData _data;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private int _spawned, _defeated, _finished;
        private readonly List<float> _slams = new List<float>();

        private sealed class BlockedAhead : IWalkability
        {
            // Everything with z > 3 and x in -2..2 is a wall (straight ahead of a Knight at the origin facing +Z).
            public bool IsBlocked(Vector3 p, float r) => p.z > 3f - r && p.x > -2f - r && p.x < 2f + r;
        }

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(5858);

            var playerGo = Track(new GameObject("Player"));
            _player = playerGo.AddComponent<PlayerHealth>();
            var collector = playerGo.AddComponent<SoulCollector>();
            collector.Setup(0.01f, 1f); // nothing flies in: the reward stays on the ground to be counted

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
            _timer.Finished += () => _finished++;

            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false); // no Start: the spawner is only read for its multipliers
            _spawner = spawnerGo.AddComponent<EnemySpawner>();

            var telegraphGo = Track(new GameObject("Telegraph"));
            _telegraph = telegraphGo.AddComponent<GroundTelegraph>();
            var ring = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            var fill = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            Object.DestroyImmediate(fill.GetComponent<Collider>());
            _telegraph.Setup(ring, fill);

            var bodyGo = Track(new GameObject("BossBody"));
            bodyGo.SetActive(false);
            _body = bodyGo.AddComponent<Enemy>(); // type max health 150

            _data = Track(BossData.Create(_body, health: 3000f, introDelay: 2f, spawnWarning: 1f, slamTelegraph: 1f,
                slamDamage: 0.2f, maxHitShare: 0.35f, rewardXp: 600, rewardWisps: 6, slamRange: 4f, slamRadius: 3f,
                slamReach: 1.5f, firstSlamDelay: 0.5f, slamCooldown: 3f, slamRecover: 0.5f));
            _fight = NewFight(_data);
        }

        [TearDown]
        public void TearDown()
        {
            if (_fight != null && _fight.Boss != null) Object.DestroyImmediate(_fight.Boss.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _slams.Clear();
            _spawned = _defeated = _finished = 0;
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

        private BossFight NewFight(BossData data)
        {
            var fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner, _player, _souls, _telegraph);
            fight.Spawned += e => _spawned++;
            fight.Defeated += () => _defeated++;
            fight.Slammed += (c, dealt) => _slams.Add(dealt);
            return fight;
        }

        private static ChamberPlan Plan(ChamberType type, float health = 1.6f, float cap = 1.2f, float interval = 0.9f) =>
            new ChamberPlan(1, 5, type, false, health, cap, interval, 77);

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) _fight.Tick(Frame);
        }

        /// <summary>Begin + intro + warning: the boss stands, Knight at the origin facing +Z.</summary>
        private void StartFight()
        {
            Assert.That(_fight.Begin(Plan(ChamberType.Boss)), Is.True);
            Run(_data.IntroDelay + _data.SpawnWarning + 2f * Frame);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
        }

        // ------------------------------------------------------------ Chamber flow

        [Test]
        public void NormalChamber_StartsNoFight_AndKeepsTheClock()
        {
            Assert.That(_fight.Begin(Plan(ChamberType.Normal)), Is.False);
            Assert.That(_fight.Begin(Plan(ChamberType.MiniBoss)), Is.False);
            Run(10f);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Inactive));
            Assert.That(_fight.Boss, Is.Null);
            Assert.That(_timer.UsesTimer, Is.True);
            Assert.That(_timer.IsRunning, Is.True);
        }

        [Test]
        public void BossChamber_HasNoClock_AndAThinnerHorde()
        {
            var plan = Plan(ChamberType.Boss);
            Assert.That(_fight.Begin(plan), Is.True);
            Assert.That(_fight.Begin(plan), Is.False, "begins once");
            Assert.That(_timer.UsesTimer, Is.False);
            Assert.That(_timer.IsRunning, Is.False);
            _timer.Tick(10000f);
            Assert.That(_timer.IsFinished, Is.False, "only the boss death opens the portal");
            Assert.That(_spawner.CapMultiplier, Is.EqualTo(plan.CapMultiplier * _data.HordeCap).Within(1e-4f));
            Assert.That(_spawner.IntervalMultiplier, Is.EqualTo(plan.IntervalMultiplier * _data.HordeInterval).Within(1e-4f));
            Assert.That(_spawner.HealthMultiplier, Is.EqualTo(plan.HealthMultiplier).Within(1e-4f));
        }

        [Test]
        public void Boss_RisesAfterIntroAndWarning_WithDataHealthTimesChamber()
        {
            var plan = Plan(ChamberType.Boss, health: 1.6f);
            _fight.Begin(plan);
            Run(_data.IntroDelay - 0.05f);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Waiting));
            Assert.That(_telegraph.IsShowing, Is.False);
            Run(0.1f);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Rising));
            Assert.That(_telegraph.IsShowing, Is.True, "ground warning where the boss will stand");
            Assert.That(_spawned, Is.EqualTo(0));
            Run(_data.SpawnWarning);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            Assert.That(_spawned, Is.EqualTo(1));
            Assert.That(_fight.Boss.IsAlive, Is.True);
            Assert.That(_fight.Boss.MaxHealth, Is.EqualTo(3000f * 1.6f).Within(0.01f));
            Assert.That(_fight.HealthFraction, Is.EqualTo(1f));
            Assert.That(Enemy.Active, Does.Contain(_fight.Boss), "targetable like any enemy (bolts, contact damage)");
            float d = Vector3.Distance(_fight.Boss.transform.position, _player.transform.position);
            Assert.That(d, Is.EqualTo(_data.SpawnDistance).Within(0.01f));
        }

        [Test]
        public void Pause_FreezesTheFight()
        {
            _fight.Begin(Plan(ChamberType.Boss));
            for (int i = 0; i < 600; i++) _fight.Tick(0f); // timeScale 0 gives dt 0
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Waiting));
        }

        // ------------------------------------------------------------ Slam

        private void PutPlayerNextToBoss()
        {
            var boss = _fight.Boss.transform.position;
            _player.transform.position = boss + new Vector3(0f, 0f, -2.5f); // inside slam range, in the slam circle
        }

        [Test]
        public void Slam_WarnsAtLeast06Seconds_ThenHitsInsideTheCircle()
        {
            StartFight();
            PutPlayerNextToBoss();
            Run(_data.FirstSlamDelay + 2f * Frame);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.WindUp));
            Assert.That(_fight.Boss.Halted, Is.True, "the boss stands still while it winds up");
            Assert.That(_telegraph.IsShowing, Is.True);
            Assert.That(_telegraph.Radius, Is.EqualTo(_data.SlamRadius).Within(1e-4f));

            Run(BossData.MinTelegraph - 0.02f);
            Assert.That(_slams, Is.Empty, "no damage inside the minimum warning");
            Assert.That(_player.Current, Is.EqualTo(_player.Max));

            Run(_data.SlamTelegraph - BossData.MinTelegraph + 0.04f); // just past the wind-up
            Assert.That(_slams.Count, Is.EqualTo(1));
            float expected = 0.2f * _player.Max;
            Assert.That(_slams[0], Is.EqualTo(expected).Within(0.01f));
            Assert.That(_player.Current, Is.EqualTo(_player.Max - expected).Within(0.01f));
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover));

            Run(_data.SlamRecover + 2f * Frame);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_fight.Boss.Halted, Is.False, "the chase resumes");
            Run(_data.SlamCooldown - 0.1f);
            Assert.That(_slams.Count, Is.EqualTo(1), "cooldown between slams");
        }

        [Test]
        public void Slam_Dodged_WhenTheKnightLeavesTheCircle()
        {
            StartFight();
            PutPlayerNextToBoss();
            Run(_data.FirstSlamDelay + 2f * Frame);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.WindUp));
            _player.transform.position += new Vector3(_data.SlamRadius + 3f, 0f, 0f); // sidestep out
            Run(_data.SlamTelegraph + Frame);
            Assert.That(_slams.Count, Is.EqualTo(1), "the slam still lands");
            Assert.That(_slams[0], Is.EqualTo(0f));
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Slam_NeverOutOfRange()
        {
            StartFight();
            _player.transform.position = _fight.Boss.transform.position + new Vector3(0f, 0f, -(_data.SlamRange + 1f));
            Run(5f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
            Assert.That(_slams, Is.Empty);
        }

        [Test]
        public void SlamDamage_IsCappedByMaxHitShare_AndTelegraphHasAFloor()
        {
            var brutal = Track(BossData.Create(_body, slamDamage: 0.9f, maxHitShare: 0.35f, slamTelegraph: 0.1f));
            Assert.That(brutal.SlamDamageFor(400f), Is.EqualTo(140f).Within(1e-3f), "no instant kill");
            Assert.That(brutal.SlamTelegraph, Is.EqualTo(BossData.MinTelegraph), "never under 0.6 s of warning");
        }

        [Test]
        public void DeadKnight_IsNotSlammed()
        {
            StartFight();
            PutPlayerNextToBoss();
            _player.TakeDamage(1e6f);
            Run(6f);
            Assert.That(_slams, Is.Empty);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
        }

        // ------------------------------------------------------------ Kill

        [Test]
        public void BossDeath_DropsGoldReward_AndOpensThePortalOnce()
        {
            StartFight();
            var boss = _fight.Boss;
            boss.TakeDamage(boss.MaxHealth * 0.5f, Vector3.forward);
            Assert.That(_fight.HealthFraction, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(_finished, Is.EqualTo(0));
            boss.TakeDamage(1e9f, Vector3.forward);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_fight.HealthFraction, Is.EqualTo(0f));
            Assert.That(_defeated, Is.EqualTo(1));
            Assert.That(_timer.IsFinished, Is.True, "portal opens");
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_souls.SpawnedXp, Is.EqualTo(600 + boss.XpValue));
            Assert.That(_souls.Count, Is.GreaterThanOrEqualTo(_data.RewardWisps));
            int gold = 0;
            for (int i = 0; i < _souls.Count; i++)
                if (_souls.XpAt(i) == 100) { gold++; Assert.That(_souls.TierAt(i), Is.EqualTo(2), "gold boss tier"); }
            Assert.That(gold, Is.EqualTo(6));
            Run(5f);
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_slams, Is.Empty);
        }

        [Test]
        public void KillDuringWindUp_CancelsTheSlam()
        {
            StartFight();
            PutPlayerNextToBoss();
            Run(_data.FirstSlamDelay + 2f * Frame);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.WindUp));
            _fight.Boss.TakeDamage(1e9f, Vector3.forward);
            Assert.That(_telegraph.IsShowing, Is.False);
            Run(3f);
            Assert.That(_slams, Is.Empty);
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void OtherEnemyKills_DoNotEndTheFight()
        {
            StartFight();
            var zombie = Track(new GameObject("Zombie")).AddComponent<Enemy>();
            zombie.Spawn(new Vector3(20f, 0f, 0f), null, null);
            zombie.TakeDamage(1e6f, Vector3.forward);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            Assert.That(_timer.IsFinished, Is.False);
        }

        // ------------------------------------------------------------ Spawn point, halt

        [Test]
        public void SpawnPoint_AvoidsBlockedGround()
        {
            Assert.That(BossFight.FindSpawnPoint(Vector3.zero, Vector3.forward, 8f), Is.EqualTo(new Vector3(0f, 0f, 8f)));
            var walk = new BlockedAhead();
            Enemy.Walkability = walk;
            var p = BossFight.FindSpawnPoint(Vector3.zero, Vector3.forward, 8f);
            Assert.That(walk.IsBlocked(p, 1.4f), Is.False);
            Assert.That(new Vector2(p.x, p.z).magnitude, Is.EqualTo(8f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator HaltedEnemy_DoesNotWalk_ThenResumes()
        {
            var target = Track(new GameObject("Target")).transform;
            target.position = new Vector3(0f, 0f, 10f);
            var enemy = Track(new GameObject("Walker")).AddComponent<Enemy>();
            enemy.Spawn(Vector3.zero, target, null);
            enemy.Halted = true;
            yield return new WaitForSeconds(0.3f);
            Assert.That(enemy.transform.position.z, Is.EqualTo(0f).Within(1e-4f));
            enemy.Halted = false;
            yield return new WaitForSeconds(0.3f);
            Assert.That(enemy.transform.position.z, Is.GreaterThan(0.2f), "walks toward the target again");
        }

        [Test]
        public void Telegraph_FlashThenHides()
        {
            _telegraph.Show(Vector3.zero, 2f);
            _telegraph.SetProgress(0.5f);
            Assert.That(_telegraph.Progress, Is.EqualTo(0.5f));
            _telegraph.Flash();
            Assert.That(_telegraph.IsShowing, Is.True);
            _telegraph.Tick(1f);
            Assert.That(_telegraph.IsShowing, Is.False);
        }
    }
}
