using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.MiniBosses;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-212 (KAN-23.2) mini boss special attack: the slam circle and the charge lane show before every hit, the wind-up is
    /// never under <see cref="MiniBossData.MinWindUp"/>, a Knight outside the circle / lane at the hit takes no damage, one
    /// hit per attack, the cooldown, the range, the type's own attacks are off while it runs and back after, the loop stops
    /// with the mini boss (director wiring), the shipped asset, and no GC allocation per frame. Stepped by hand (Tick).
    /// </summary>
    public sealed class MiniBossAttackTests
    {
        private const float Frame = 1f / 60f;
        private const string DataPath = "Assets/_Project/Data/MiniBoss/MiniBoss_Realm1_Gravewarden.asset";

        private readonly List<Object> _objects = new List<Object>();
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private PlayerHealth _knight;
        private GroundTelegraph _circle;
        private LineTelegraph _lane;
        private MiniBossAttack _attack;

        [SetUp]
        public void SetUp()
        {
            Enemy.Active.Clear();
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;

            _knight = Track(new GameObject("Knight")).AddComponent<PlayerHealth>();
            _circle = Track(new GameObject("Slam Warning")).AddComponent<GroundTelegraph>();
            _circle.Setup(null, null);
            _lane = Track(new GameObject("Charge Lane")).AddComponent<LineTelegraph>();
            _lane.Setup(new Renderer[1], new Renderer[1]);
            var go = Track(new GameObject("MiniBoss"));
            go.SetActive(false); // no Update: stepped by hand
            _attack = go.AddComponent<MiniBossAttack>();
            _attack.Setup(_circle, _lane);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
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

        private Enemy Boss(Vector3 at, bool withSlam = false)
        {
            var go = Track(new GameObject("Boss"));
            go.SetActive(false);
            var enemy = go.AddComponent<Enemy>();
            if (withSlam) go.AddComponent<BruteSlam>().Configure(new BruteSlam.Settings
            {
                triggerRange = 3f, windUp = 0.9f, radius = 4f, width = 0.8f, waveTime = 0.5f, damageShare = 0.1f, recover = 0.5f, cooldown = 4f,
            });
            enemy.Spawn(at, _knight.transform, _ => { });
            return enemy;
        }

        private MiniBossData Data(MiniBossAttackKind kind, float windUp = 1f, float cooldown = 3f, float range = 8f) =>
            Track(MiniBossData.Create("Boss")).WithAttack(kind, range: range, windUp: windUp, cooldown: cooldown, firstDelay: 0f,
                damage: 0.2f, recover: 0.5f, radius: 3f, length: 7f, width: 1.6f, dashTime: 0.4f);

        /// <summary>Steps <paramref name="seconds"/> in frames.</summary>
        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame) _attack.Tick(Frame);
        }

        private float Lost => _knight.Max - _knight.Current;

        // ------------------------------------------------------------ Slam

        [Test]
        public void Slam_CircleFillsFirst_ThenHitsTheKnightInside()
        {
            var boss = Boss(Vector3.zero);
            _knight.transform.position = new Vector3(2f, 0f, 0f);
            _attack.Begin(boss, Data(MiniBossAttackKind.Slam), _knight);

            _attack.Tick(Frame);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.WindUp));
            Assert.That(_circle.IsShowing, Is.True, "warning before the hit");
            Assert.That(_circle.Radius, Is.EqualTo(3f).Within(0.01f));
            Assert.That(boss.Halted, Is.True, "stands still in the wind-up");

            Run(0.9f);
            Assert.That(Lost, Is.EqualTo(0f), "no damage in the wind-up");
            Run(0.15f);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Recover));
            Assert.That(Lost, Is.EqualTo(0.2f * _knight.Max).Within(0.01f));
            Assert.That(_attack.HitThisAttack, Is.True);
        }

        [Test]
        public void Slam_KnightWalkedOutOfTheCircle_NoDamage()
        {
            var boss = Boss(Vector3.zero);
            _knight.transform.position = new Vector3(2f, 0f, 0f);
            _attack.Begin(boss, Data(MiniBossAttackKind.Slam), _knight);
            _attack.Tick(Frame);
            Run(0.5f);
            _knight.transform.position = new Vector3(3.3f, 0f, 0f); // just outside the 3 m circle
            Run(0.6f);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Recover));
            Assert.That(Lost, Is.EqualTo(0f));
            Assert.That(_attack.HitThisAttack, Is.False);
        }

        [Test]
        public void WindUp_NeverUnderTheMinimum()
        {
            var data = Data(MiniBossAttackKind.Slam, windUp: 0.2f);
            Assert.That(data.AttackWindUp, Is.EqualTo(MiniBossData.MinWindUp));
            var boss = Boss(Vector3.zero);
            _knight.transform.position = new Vector3(1f, 0f, 0f);
            _attack.Begin(boss, data, _knight);
            _attack.Tick(Frame);
            Run(MiniBossData.MinWindUp - 0.05f);
            Assert.That(Lost, Is.EqualTo(0f), "a 0.2 s wind-up still warns 0.9 s");
            Assert.That(_circle.IsShowing && _circle.Progress < 1f, Is.True);
        }

        // ------------------------------------------------------------ Charge

        [Test]
        public void Charge_LaneLockedTowardsTheKnight_DashHitsInsideTheLaneOnce()
        {
            var boss = Boss(Vector3.zero);
            _knight.transform.position = new Vector3(0f, 0f, 5f);
            _attack.Begin(boss, Data(MiniBossAttackKind.Charge), _knight);
            _attack.Tick(Frame);
            Assert.That(_lane.IsShowing, Is.True, "lane before the dash");
            Assert.That(_lane.LaneCount, Is.EqualTo(1));
            Assert.That(Vector3.Dot(_attack.Direction, Vector3.forward), Is.GreaterThan(0.99f));
            Assert.That(_attack.ChargeLength, Is.EqualTo(7f).Within(0.01f));

            Run(0.95f);
            Assert.That(boss.transform.position.z, Is.LessThan(0.1f), "no move in the wind-up");
            Assert.That(Lost, Is.EqualTo(0f));
            Run(0.1f);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Strike));
            Run(0.45f);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Recover));
            Assert.That(boss.transform.position.z, Is.EqualTo(7f).Within(0.05f), "dashed the full lane");
            Assert.That(Lost, Is.EqualTo(0.2f * _knight.Max).Within(0.01f), "one hit");
        }

        [Test]
        public void Charge_SideStepOutOfTheLane_NoDamage()
        {
            var boss = Boss(Vector3.zero);
            _knight.transform.position = new Vector3(0f, 0f, 5f);
            _attack.Begin(boss, Data(MiniBossAttackKind.Charge), _knight);
            _attack.Tick(Frame);
            Run(0.6f);
            _knight.transform.position = new Vector3(1f, 0f, 5f); // lane half width 0.8: just outside, the direction stays locked
            Run(1f);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Recover));
            Assert.That(Lost, Is.EqualTo(0f));
        }

        [Test]
        public void Charge_WallCutsTheLane_NoRoom_NoCharge()
        {
            Enemy.LandMaxZ = 1f; // the sea edge 1 m ahead
            var boss = Boss(Vector3.zero);
            _knight.transform.position = new Vector3(0f, 0f, 3f);
            _attack.Begin(boss, Data(MiniBossAttackKind.Charge), _knight);
            _attack.Tick(Frame);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Idle), "1 m free < shortest charge");
            Assert.That(_lane.IsShowing, Is.False);
        }

        // ------------------------------------------------------------ Loop

        [Test]
        public void Loop_RangeAndCooldown_TypeAttackOffWhileItRuns()
        {
            var boss = Boss(Vector3.zero, withSlam: true);
            var slam = boss.GetComponent<BruteSlam>();
            _knight.transform.position = new Vector3(20f, 0f, 0f);
            _attack.Begin(boss, Data(MiniBossAttackKind.Slam, cooldown: 3f), _knight);
            Run(1f);
            Assert.That(_attack.Attacks, Is.EqualTo(0), "out of range");

            _knight.transform.position = new Vector3(2f, 0f, 0f);
            _attack.Tick(Frame);
            Assert.That(_attack.Attacks, Is.EqualTo(1));
            Assert.That(slam.enabled, Is.False, "the type's slam is off in the attack");
            Run(1.6f); // wind-up 1 + recover 0.5
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Idle));
            Assert.That(boss.Halted, Is.False, "walks again");
            Assert.That(slam.enabled, Is.True, "the type's slam is back");
            Assert.That(_attack.Cooldown, Is.EqualTo(3f).Within(0.1f));

            Run(2.8f);
            Assert.That(_attack.Attacks, Is.EqualTo(1), "cooldown");
            Run(0.3f);
            Assert.That(_attack.Attacks, Is.EqualTo(2), "next attack after the cooldown");
        }

        [Test]
        public void Loop_BossDies_StopsAndHidesTheWarning()
        {
            var boss = Boss(Vector3.zero, withSlam: true);
            _knight.transform.position = new Vector3(2f, 0f, 0f);
            _attack.Begin(boss, Data(MiniBossAttackKind.Slam), _knight);
            _attack.Tick(Frame);
            Assert.That(_circle.IsShowing, Is.True);
            boss.TakeDamage(1e9f, Vector3.forward);
            _attack.Tick(Frame);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Off));
            Assert.That(_circle.IsShowing, Is.False);
            Assert.That(boss.GetComponent<BruteSlam>().enabled, Is.True, "the pooled body keeps its own slam");
        }

        [Test]
        public void Loop_DirectorStartsItAtTheRise_StopsItAtTheKill()
        {
            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false);
            var spawner = spawnerGo.AddComponent<EnemySpawner>();
            var template = Track(new GameObject("Boss"));
            template.SetActive(false);
            spawner.SetExtraTypes(new[] { template.AddComponent<Enemy>() });
            var go = Track(new GameObject("Director"));
            go.SetActive(false);
            var director = go.AddComponent<MiniBossDirector>();
            director.Setup(Data(MiniBossAttackKind.Charge), spawner, _knight, null, null, null, null, null, null, null, _attack);
            go.SetActive(true);

            Assert.That(director.Arm(0f), Is.True);
            director.Tick(0.01f);
            director.Tick(director.Data.WarningTime + 0.01f);
            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Alive));
            Assert.That(_attack.State, Is.Not.EqualTo(MiniBossAttack.Phase.Off));
            Assert.That(_attack.Kind, Is.EqualTo(MiniBossAttackKind.Charge));

            director.Boss.TakeDamage(1e9f, Vector3.forward);
            Assert.That(_attack.State, Is.EqualTo(MiniBossAttack.Phase.Off));
        }

        [Test]
        public void ShippedAsset_ChargeWithADodgeableWarning()
        {
#if UNITY_EDITOR
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<MiniBossData>(DataPath);
            Assert.That(data, Is.Not.Null);
            Assert.That(data.Attack, Is.EqualTo(MiniBossAttackKind.Charge));
            Assert.That(data.AttackWindUp, Is.GreaterThanOrEqualTo(MiniBossData.MinWindUp));
            Assert.That(data.AttackDamage, Is.InRange(0.05f, 0.35f));
            Assert.That(data.AttackCooldown, Is.GreaterThan(data.AttackWindUp));
#else
            Assert.Ignore("Editor only");
#endif
        }

        [Test]
        public void SlamAndCharge_NoGcPerFrame()
        {
            var boss = Boss(Vector3.zero, withSlam: true);
            _knight.transform.position = new Vector3(0f, 0f, 3f);
            var data = Data(MiniBossAttackKind.Charge, cooldown: 0.5f);
            _attack.Begin(boss, data, _knight);
            Run(3f); // warm-up: one full charge (first JIT of every path)
            data.WithAttack(MiniBossAttackKind.Slam, windUp: 1f, cooldown: 0.5f, firstDelay: 0f, radius: 3f);
            _attack.Begin(boss, data, _knight);
            Run(3f);

            _knight.Heal(1e6f);
            _attack.Begin(boss, data, _knight);
            Assert.That(() => Run(2f), Is.Not.AllocatingGCMemory());
            data.WithAttack(MiniBossAttackKind.Charge, windUp: 1f, cooldown: 0.5f, firstDelay: 0f);
            _attack.Begin(boss, data, _knight);
            Assert.That(() => Run(2f), Is.Not.AllocatingGCMemory());
        }
    }
}
