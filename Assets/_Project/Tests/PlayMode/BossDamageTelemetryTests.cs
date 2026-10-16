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
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-163, fight side: a boss hit in phase 2 deals x <see cref="BossData.Phase2Damage"/> (still capped), phase 1 deals
    /// the plain share; every hit raises <see cref="BossFight.KnightHit"/> and tracks a BossHitTaken event (label, share %,
    /// health lost); the fight end tracks one BossFightResult: "death" when the Knight falls, "win" on the kill.
    /// </summary>
    public sealed class BossDamageTelemetryTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private readonly List<TelemetryEvent> _events = new List<TelemetryEvent>();
        private readonly List<string> _hits = new List<string>();
        private readonly List<string> _results = new List<string>();
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

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(1630);
            GameTelemetry.Clear();
            GameTelemetry.Tracked += OnTracked;

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
            GameTelemetry.Tracked -= OnTracked;
            GameTelemetry.Clear();
            if (_fight != null && _fight.Boss != null) Object.DestroyImmediate(_fight.Boss.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _events.Clear();
            _hits.Clear();
            _results.Clear();
            _fight = null;
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        private void OnTracked(TelemetryEvent e) => _events.Add(e);

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

        private static BossAttack Chop(float damage) =>
            BossAttack.Lane("Chop", BossPhases.Both, 0f, 6f, length: 6f, width: 1.6f, telegraph: 0.9f, damage: damage, recover: 0.5f,
                cooldown: 1f, priority: 1, clip: "Chop");

        private BossData Data(float damage, float phase2Damage = 1.15f) =>
            Track(BossData.CreateWithAttacks(_body, new[] { Chop(damage) }, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: 0.5f, maxHitShare: 0.5f, phase2Damage: phase2Damage, displayName: "TEST BOSS"));

        private void StartFight(BossData data)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null);
            _fight.KnightHit += (label, dealt) => _hits.Add(label);
            _fight.FightEnded += (result, seconds) => _results.Add(result);
            Assert.That(_fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77)), Is.True);
            Run(data.IntroDelay + data.SpawnWarning + 2f * Frame);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            _fight.Boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _player.transform.position = new Vector3(0f, 0f, 3f);
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
            {
                _fight.Tick(Frame);
                _bolts.Tick(Frame);
            }
        }

        /// <summary>Runs until the next chop has struck (wind-up, then recover).</summary>
        private void NextChop()
        {
            for (float t = 0f; t < 10f && _fight.CurrentAttack != BossFight.Attack.WindUp; t += Frame) Run(Frame);
            for (float t = 0f; t < 10f && _fight.CurrentAttack == BossFight.Attack.WindUp; t += Frame) Run(Frame);
        }

        private List<TelemetryEvent> Events(string name) => _events.FindAll(e => e.Name == name);

        [Test]
        public void Phase1Hit_DealsThePlainShare_AndTracksTheHit()
        {
            StartFight(Data(0.4f));
            NextChop();
            Assert.That(_player.Max - _player.Current, Is.EqualTo(160f).Within(0.01f), "40 % of 400");
            Assert.That(_hits, Is.EqualTo(new[] { "Chop" }));
            var hits = Events(GameTelemetry.BossHitTaken);
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].Subject, Is.EqualTo("Chop"));
            Assert.That(hits[0].From, Is.EqualTo(40), "share %");
            Assert.That(hits[0].To, Is.EqualTo(160), "health lost");
            Assert.That(hits[0].Source, Is.EqualTo("TEST BOSS"));
            Assert.That(Events(GameTelemetry.BossFightResult), Is.Empty, "the fight goes on");
        }

        [Test]
        public void Phase2Hit_IsHarder_ButStillCapped()
        {
            StartFight(Data(0.4f));
            _fight.Boss.TakeDamage(_fight.Boss.Health - _fight.Boss.MaxHealth * 0.4f, Vector3.zero);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            NextChop();
            Assert.That(_player.Max - _player.Current, Is.EqualTo(184f).Within(0.01f), "40 % x 1.15");
            Assert.That(Events(GameTelemetry.BossHitTaken)[0].From, Is.EqualTo(46));

            var capped = Data(0.48f);
            Assert.That(capped.DamageFor(0.48f, _player.Max, 2), Is.EqualTo(200f).Within(0.01f), "never above half");
            var plain = Data(0.4f, phase2Damage: 1f);
            Assert.That(plain.DamageFor(0.4f, _player.Max, 2), Is.EqualTo(160f).Within(0.01f), "factor 1 = phase 1 numbers");
        }

        [Test]
        public void KnightDeath_TracksOneDeathResult()
        {
            StartFight(Data(0.4f));
            _player.TakeDamage(_player.Max - 100f);
            NextChop();
            Assert.That(_player.IsDead, Is.True);
            Assert.That(_results, Is.EqualTo(new[] { BossFight.ResultDeath }));
            var results = Events(GameTelemetry.BossFightResult);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Subject, Is.EqualTo("TEST BOSS"));
            Assert.That(results[0].Source, Is.EqualTo(BossFight.ResultDeath));
            Assert.That(results[0].From, Is.EqualTo(1), "phase");
            Assert.That(results[0].To, Is.GreaterThanOrEqualTo(1), "fight seconds");
            Run(2f);
            Assert.That(Events(GameTelemetry.BossFightResult).Count, Is.EqualTo(1), "once");
        }

        [Test]
        public void BossKill_TracksOneWinResult()
        {
            StartFight(Data(0.4f));
            Run(1f);
            _fight.Boss.TakeDamage(_fight.Boss.Health + 1f, Vector3.zero);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_results, Is.EqualTo(new[] { BossFight.ResultWin }));
            var results = Events(GameTelemetry.BossFightResult);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Source, Is.EqualTo(BossFight.ResultWin));
            Run(3f);
            Assert.That(Events(GameTelemetry.BossFightResult).Count, Is.EqualTo(1), "once");
        }
    }
}
