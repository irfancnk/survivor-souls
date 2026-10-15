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
    /// KAN-110 acceptance, logic side: The Rotting Colossus asset (Docs/ENEMY_ROSTER.md 3.14) has its attacks with the right
    /// phases, clips, warnings (&gt;= 0.6 s) and hit cap; it is the slowest boss and has the most HP. Up close it slams
    /// (double shockwave), at mid range it grabs or throws. The grab warns 1.2 s; a hit deals 20 % and drags the Knight in to
    /// the body; a side step dodges it. The corpse boulder's rubble blocks the ground (arena walkability) for 8 s, then
    /// stops blocking. Phase 2 throws 3 boulders in a row and the roar summons 4 Frenzied Ghouls, never more than 4 alive.
    /// The Game scene holds the Colossus in the fight library and <see cref="BossFight.ForcedBoss"/> picks it for Chamber 5.
    /// No GC per frame with rubble and summons. Look and feel: screenshots (Docs/screenshots/game-kan110-*).
    /// </summary>
    public sealed class RottingColossusTests
    {
        private const string ColossusPath = "Assets/_Project/Data/Boss/Boss_RottingColossus.asset";
        private const string ControllerPath = "Assets/_Project/Art/Characters/Bosses/RottingColossus/AC_RottingColossus.controller";
        private static readonly string[] OtherBosses =
        {
            "Assets/_Project/Data/Boss/Boss_Realm1_Butcher.asset",
            "Assets/_Project/Data/Boss/Boss_PyreLord.asset",
        };
        private const float Frame = 1f / 60f;
        private const float KnightSpeed = 5f; // PlayerMover base speed
        private const int Circles = 8;
        private const int Boulders = 6;

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private SurvivalTimer _timer;
        private EnemySpawner _spawner;
        private SoulWispField _souls;
        private GroundTelegraph _telegraph;
        private LineTelegraph _lines;
        private BossProjectiles _bolts;
        private EnemyTelegraphs _pool;
        private BossBoulders _rocks;
        private BossArena _arena;
        private Enemy _body;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private IWalkability _savedObstacles;
        private float _savedLandMaxZ;
        private readonly List<int> _started = new List<int>();
        private readonly List<Vector3> _thrown = new List<Vector3>();
        private readonly List<int> _summoned = new List<int>();

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedObstacles = BossArena.Obstacles;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            BossFight.ForcedBoss = null;
            Random.InitState(1100);

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

            // The summon type by its roster name (Enemy_<id>), like the Game scene's spawner extra type.
            var ghoulGo = Track(new GameObject("Enemy_FrenziedGhoul"));
            ghoulGo.SetActive(false);
            var ghoul = ghoulGo.AddComponent<Enemy>();
            // Active object, component off: no waves, but summons enable and leave Enemy.Active when destroyed.
            _spawner = Track(new GameObject("Enemies")).AddComponent<EnemySpawner>();
            _spawner.enabled = false;
            _spawner.SetExtraTypes(new[] { ghoul });

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

            var circles = new GroundTelegraph[Circles];
            for (int i = 0; i < Circles; i++)
            {
                circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                circles[i].Setup(Quad(), Quad());
            }
            _pool = Track(new GameObject("Warnings")).AddComponent<EnemyTelegraphs>();
            _pool.Setup(new LineTelegraph[0], circles);

            var bodies = new Transform[Boulders];
            for (int i = 0; i < Boulders; i++)
            {
                var rock = Track(new GameObject("Boulder " + i));
                rock.AddComponent<CapsuleCollider>().enabled = false;
                bodies[i] = rock.transform;
            }
            _rocks = Track(new GameObject("Boulders")).AddComponent<BossBoulders>();
            _rocks.Setup(_player, bodies);

            _arena = Track(new GameObject("Arena")).AddComponent<BossArena>();
            _arena.Setup(30f, new BossArena.Pillar[0], Vector3.zero, null, null, null, null, null, null);

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
            _summoned.Clear();
            _fight = null;
            Enemy.Walkability = _savedWalk;
            BossArena.Obstacles = _savedObstacles;
            Enemy.LandMaxZ = _savedLandMaxZ;
            BossFight.ForcedBoss = null;
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

        private static BossData Asset()
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>(ColossusPath);
            Assert.That(data, Is.Not.Null, ColossusPath);
            return data;
        }

        /// <summary>The shipped entry labelled <paramref name="label"/> (the asset's numbers).</summary>
        private static BossAttack Shipped(string label)
        {
            foreach (var a in Asset().Attacks)
                if (a.label == label) return a;
            Assert.Fail($"The Rotting Colossus has no '{label}'");
            return default;
        }

        /// <summary>A test boss on the dummy body that runs <paramref name="attacks"/> (same hit cap as the asset).</summary>
        private BossData Data(BossAttack[] attacks, float firstAttackDelay = 30f) =>
            Track(BossData.CreateWithAttacks(_body, attacks, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: firstAttackDelay, maxHitShare: Asset().MaxHitShare));

        /// <summary>Begin + rise; the boss stands at the origin facing +Z, the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null, _pool, _rocks);
            _fight.AttackStarted += i => _started.Add(i);
            _fight.BoulderThrown += (at, r) => _thrown.Add(at);
            _fight.Summoned += (at, n) => _summoned.Add(n);
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
            if (_fight.Boss != null) _fight.Boss.transform.position = Vector3.zero; // no chase drift
            _fight.Tick(Frame);
            _rocks.Tick(Frame);
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame) Step();
        }

        private static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ------------------------------------------------------------ Asset

        [Test]
        public void Asset_HasItsAttacks_Phases_Clips_WarningFloor_HitCap()
        {
            var data = Asset();
            Assert.That(data.DisplayName, Is.EqualTo("THE ROTTING COLOSSUS"));
            Assert.That(data.Prefab, Is.Not.Null);
            Assert.That(data.Prefab.name, Is.EqualTo("Enemy_RottingColossus"));
            Assert.That(data.Prefab.IsBoss, Is.True);
            Assert.That(data.RoarClip, Is.EqualTo("Roar"));
            Assert.That(data.Phase2At, Is.EqualTo(0.5f));
            Assert.That(data.IntroZoom, Is.GreaterThan(1.4f), "the 5 m body: the intro camera pulls out further");

            var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);
            var states = new HashSet<string>();
            foreach (var s in controller.layers[0].stateMachine.states) states.Add(s.state.name);
            Assert.That(states, Is.SupersetOf(new[] { "Slam", "Throw", "Grab", "Roar", "Walk", "Death" }));

            foreach (var a in data.Attacks)
            {
                Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(data.TelegraphFor(a.Telegraph, 2), Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a + " phase 2");
                Assert.That(a.damage, Is.LessThanOrEqualTo(data.MaxHitShare), a.ToString());
                Assert.That(states, Does.Contain(a.clip), a + " clip");
            }
            Assert.That(data.Attacks.Length, Is.EqualTo(5));

            var slam = Shipped("Double Slam");
            Assert.That(slam.type, Is.EqualTo(BossFight.Move.Shockwave));
            Assert.That(slam.count, Is.EqualTo(2), "two shockwave rings");
            Assert.That(slam.phases, Is.EqualTo(BossPhases.Both));
            Assert.That(slam.priority, Is.GreaterThan(0), "up close the slam goes first");

            var grab = Shipped("Grab");
            Assert.That(grab.type, Is.EqualTo(BossFight.Move.Lane));
            Assert.That(grab.Telegraph, Is.EqualTo(1.2f), "1.2 s reach lane");
            Assert.That(grab.damage, Is.EqualTo(0.2f), "20 %");
            Assert.That(grab.pull, Is.GreaterThan(0f), "pulls the Knight in");
            Assert.That(grab.phases, Is.EqualTo(BossPhases.Both));

            var boulder = Shipped("Corpse Boulder");
            Assert.That(boulder.type, Is.EqualTo(BossFight.Move.Lobbed));
            Assert.That(boulder.count, Is.EqualTo(1));
            Assert.That(boulder.radius, Is.EqualTo(2.5f), "landing circle r 2.5 m");
            Assert.That(boulder.obstacle, Is.EqualTo(8f), "rubble for 8 s");
            Assert.That(boulder.phases, Is.EqualTo(BossPhases.Phase1));

            var volley = Shipped("Boulder Volley");
            Assert.That(volley.type, Is.EqualTo(BossFight.Move.Lobbed));
            Assert.That(volley.count, Is.EqualTo(3), "3 boulders in a row");
            Assert.That(volley.obstacle, Is.EqualTo(8f));
            Assert.That(volley.phases, Is.EqualTo(BossPhases.Phase2));
            Assert.That(volley.count + 2, Is.LessThanOrEqualTo(BossBuilderBoulders), "the volley + older rubble fit the pool");

            var summon = Shipped("Summon Ghouls");
            Assert.That(summon.type, Is.EqualTo(BossFight.Move.Summon));
            Assert.That(summon.summon, Is.EqualTo("FrenziedGhoul"));
            Assert.That(summon.count, Is.EqualTo(4));
            Assert.That(summon.phases, Is.EqualTo(BossPhases.Phase2));
            Assert.That(summon.clip, Is.EqualTo("Roar"), "the roar summons");
        }

        /// <summary>Boulder bodies the Game scene builds (BossBuilder.BoulderCount; Editor assembly, so a copy here).</summary>
        private const int BossBuilderBoulders = 6;

        [Test]
        public void Body_IsTheSlowestBoss_WithTheMostHealth()
        {
            var data = Asset();
            float speed = new UnityEditor.SerializedObject(data.Prefab).FindProperty("moveSpeed").floatValue;
            foreach (var path in OtherBosses)
            {
                var other = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>(path);
                Assert.That(other, Is.Not.Null, path);
                Assert.That(data.MaxHealth, Is.GreaterThan(other.MaxHealth), "most HP: " + path);
                float otherSpeed = new UnityEditor.SerializedObject(other.Prefab).FindProperty("moveSpeed").floatValue;
                Assert.That(speed, Is.LessThan(otherSpeed), "slowest: " + path);
            }
            Assert.That(data.MaxHealth, Is.EqualTo(8200f), "doc 11000; 8200 keeps a no-pick Knight under 4 min (Balance test)");
        }

        // ------------------------------------------------------------ Attack choice by range

        [Test]
        public void UpClose_ItSlams_AtMidRange_ItGrabsOrThrows()
        {
            var data = Asset();
            StartFight(Data(data.Attacks, firstAttackDelay: 0.2f), knightDistance: 2.5f);
            _player.Invulnerable = true;
            for (float t = 0f; t < 20f && _started.Count < 3; t += Frame)
            {
                _player.transform.position = new Vector3(0f, 0f, 2.5f);
                Step();
            }
            Assert.That(_started.Count, Is.EqualTo(3));
            foreach (int i in _started) Assert.That(data.Attacks[i].label, Is.EqualTo("Double Slam"), "within 4 m: the slam band");

            _started.Clear();
            RunUntil(BossFight.Attack.Chase, 6f);
            for (float t = 0f; t < 30f && _started.Count < 6; t += Frame)
            {
                _player.transform.position = new Vector3(0f, 0f, 6f);
                Step();
            }
            Assert.That(_started.Count, Is.EqualTo(6));
            var seen = new HashSet<string>();
            foreach (int i in _started) seen.Add(data.Attacks[i].label);
            Assert.That(seen, Is.EquivalentTo(new[] { "Grab", "Corpse Boulder" }), "6 m in phase 1: grab and throw take turns");
        }

        // ------------------------------------------------------------ Grab

        [Test]
        public void Grab_Warns12s_HitDeals20Percent_AndDragsTheKnightInToTheBody()
        {
            var grab = Shipped("Grab");
            StartFight(Data(new[] { grab }), knightDistance: 6f);
            Assert.That(_fight.ForceAttack(0), Is.True);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Lane));
            Assert.That(_fight.WindUpTime, Is.EqualTo(1.2f).Within(1e-4f), "1.2 s reach lane");
            float warned = 0f;
            while (_fight.CurrentAttack == BossFight.Attack.WindUp && warned < 3f)
            {
                Step();
                warned += Frame;
            }
            Assert.That(warned, Is.GreaterThanOrEqualTo(1.2f - Frame));
            Assert.That(_player.Max - _player.Current, Is.EqualTo(0.2f * _player.Max).Within(0.01f), "20 % of max health");

            Run(1f); // the drag runs during the recovery
            float gap = Planar(_player.transform.position, Vector3.zero);
            Assert.That(gap, Is.LessThan(_fight.Boss.HitRadius + 1f), "dragged in to the body");
            Assert.That(gap, Is.GreaterThanOrEqualTo(_fight.Boss.HitRadius), "never into the body");
            Assert.That(_player.Max - _player.Current, Is.EqualTo(0.2f * _player.Max).Within(0.01f), "one hit");
        }

        [Test]
        public void Grab_SideStepOutOfTheLane_Dodges_NoPull()
        {
            var grab = Shipped("Grab");
            StartFight(Data(new[] { grab }), knightDistance: 5f);
            Assert.That(_fight.ForceAttack(0), Is.True);
            // The Knight sees the lane and walks sideways at Knight speed: out of the 1.4 m lane well inside 1.2 s.
            float needed = (grab.width * 0.5f + 0.5f) / KnightSpeed;
            Assert.That(needed, Is.LessThan(_fight.WindUpTime * 0.5f), "half the warning is enough to step out");
            while (_fight.CurrentAttack == BossFight.Attack.WindUp)
            {
                var p = _player.transform.position;
                if (p.x < grab.width * 0.5f + 0.5f) _player.transform.position = p + Vector3.right * KnightSpeed * Frame;
                Step();
            }
            Vector3 after = _player.transform.position;
            Run(1f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "missed");
            Assert.That(Planar(_player.transform.position, after), Is.LessThan(0.01f), "no drag on a miss");
        }

        // ------------------------------------------------------------ Boulders + rubble

        [Test]
        public void CorpseBoulder_Rubble_BlocksTheArenaGround_For8s_ThenStopsBlocking()
        {
            var boulder = Shipped("Corpse Boulder");
            boulder.cooldown = 30f; // one throw: the rubble alone afterwards
            StartFight(Data(new[] { boulder }), knightDistance: 8f);
            _player.Invulnerable = true;
            var spot = new Vector3(0f, 0f, 8f);
            Assert.That(_arena.IsBlocked(spot, 0.3f), Is.False, "open ground before the throw");
            Assert.That(_fight.ForceAttack(0), Is.True);
            for (float t = 0f; t < 5f && _rocks.RubbleCount == 0; t += Frame) Step();
            Assert.That(_rocks.RubbleCount, Is.EqualTo(1), "the boulder landed as rubble");
            Assert.That(_arena.IsBlocked(spot, 0.3f), Is.True, "rubble added to the arena walkability");
            Assert.That(_arena.IsBlocked(spot + Vector3.right * 2.5f, 0.3f), Is.False, "only its own circle");

            Run(boulder.obstacle - 0.3f);
            Assert.That(_arena.IsBlocked(spot, 0.3f), Is.True, "stays for its life");
            Run(0.5f);
            Assert.That(_rocks.RubbleCount, Is.EqualTo(0));
            Assert.That(_arena.IsBlocked(spot, 0.3f), Is.False, "removed after 8 s");
        }

        [Test]
        public void Phase2_BoulderVolley_ThreeInARow_SingleThrowIsOff()
        {
            var data = Asset();
            StartFight(Data(new[] { Shipped("Corpse Boulder"), Shipped("Boulder Volley") }, firstAttackDelay: 0.2f), knightDistance: 8f);
            _player.Invulnerable = true;
            _fight.Boss.TakeDamage(_fight.Boss.Health * 0.6f, Vector3.zero, DamageKind.Burst);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            for (float t = 0f; t < 15f && _started.Count < 2; t += Frame)
            {
                _player.transform.position = new Vector3(0f, 0f, 8f);
                Step();
            }
            Assert.That(_started, Is.EqualTo(new[] { 1, 1 }), "phase 2: only the volley");
            RunUntil(BossFight.Attack.Recover, 6f);
            Assert.That(_fight.Thrown, Is.EqualTo(3), "3 boulders in a row");
            Assert.That(_rocks.RubbleCount, Is.LessThanOrEqualTo(BossBuilderBoulders));
        }

        // ------------------------------------------------------------ Summon

        [Test]
        public void Phase2_RoarSummonsFourGhouls_NeverMoreThanFourAlive()
        {
            var summon = Shipped("Summon Ghouls");
            StartFight(Data(new[] { summon }, firstAttackDelay: 0.2f), knightDistance: 6f);
            Run(3f);
            Assert.That(_started, Is.Empty, "phase 1: no summon");
            _fight.Boss.TakeDamage(_fight.Boss.Health * 0.6f, Vector3.zero, DamageKind.Burst);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            for (float t = 0f; t < 8f && _summoned.Count == 0; t += Frame) Step();
            Assert.That(_summoned, Is.EqualTo(new[] { 4 }), "the roar raises 4 Frenzied Ghouls");
            Assert.That(_fight.SummonsAlive, Is.EqualTo(4));

            RunUntil(BossFight.Attack.Chase, 4f);
            Assert.That(_fight.ForceAttack(BossFight.Move.Summon), Is.False, "4 alive: cap reached");
            Run(summon.cooldown + 2f);
            Assert.That(_summoned.Count, Is.EqualTo(1), "no summon while 4 are alive");
            int killed = 0;
            foreach (Transform child in _spawner.transform)
            {
                var e = child.GetComponent<Enemy>();
                if (killed == 1 || e == null || !e.IsAlive) continue;
                e.TakeDamage(e.Health + 1f, Vector3.zero, DamageKind.Burst);
                killed++;
            }
            Assert.That(_fight.SummonsAlive, Is.EqualTo(3));
            for (float t = 0f; t < 10f && _summoned.Count < 2; t += Frame) Step();
            Assert.That(_summoned, Is.EqualTo(new[] { 4, 1 }), "only the missing one comes");
            Assert.That(_fight.SummonsAlive, Is.EqualTo(4));
        }

        // ------------------------------------------------------------ GC

        [Test]
        public void RubbleAndSummons_NoGcPerFrame()
        {
            StartFight(Data(new[] { Shipped("Corpse Boulder") }), knightDistance: 8f);
            _player.Invulnerable = true;
            Assert.That(_fight.ForceAttack(0), Is.True);
            for (float t = 0f; t < 5f && _rocks.RubbleCount == 0; t += Frame) Step();
            Assert.That(_rocks.RubbleCount, Is.EqualTo(1));
            Step();
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    Step();
                    _arena.IsBlocked(new Vector3(0f, 0f, 8f), 0.3f);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>The built Game scene: the Colossus sits in the fight library; the dev flag makes Chamber 5 its fight.</summary>
        [UnityTest]
        public IEnumerator GameScene_ForcedColossus_FightsInChamber5()
        {
            TearDown(); // this test uses the real scene's objects
            BossFight.ForcedBoss = "RottingColossus";
            RunState.NewRun(1, 9110);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            Assert.That(System.Array.Exists(fight.Library, d => d != null && d.name == "Boss_RottingColossus"), Is.True,
                "in the library");
            Assert.That(System.Array.Exists(fight.Bosses, d => d != null && d.name == "Boss_RottingColossus"), Is.False,
                "not in Realm 1's rotation");
            Assert.That(fight.Data.DisplayName, Is.EqualTo("THE ROTTING COLOSSUS"));
            Assert.That(fight.Data.Prefab.name, Is.EqualTo("Enemy_RottingColossus"));

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f;
            while (fight.Current != BossFight.Stage.Intro && fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Boss, Is.Not.Null);
            Assert.That(fight.Boss.AttackStateHash, Is.EqualTo(Animator.StringToHash("Roar")), "intro roar");
            Assert.That(fight.Boss.GetComponentInChildren<Animator>().HasState(0, Animator.StringToHash("Grab")), Is.True);

            BossFight.ForcedBoss = null;
            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            var empty = SceneManager.CreateScene("ColossusEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
