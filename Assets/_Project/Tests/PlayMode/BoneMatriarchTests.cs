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
    /// KAN-111 acceptance, logic side: The Bone Matriarch asset (Docs/ENEMY_ROSTER.md 3.15) has its attacks with the right
    /// phases, clips, warnings (&gt;= 0.6 s) and hit cap; she has the least HP of all bosses, never melees (no contact
    /// damage), keeps 6-9 m from the Knight and floats (<see cref="HoverBob"/> on the model). Homing skulls turn at a capped
    /// rate and die after 4 s or on a wall; the soul ring of 24 has 3 gaps a Knight can stand in; the cross beams start with
    /// the Knight in a gap, sweep over a Knight who stands still (one hit), miss one who walks with the gap or stands past
    /// their length; the phase 2 teleport is timed (every 8 s, never in phase 1) and lands on walkable ground inside her
    /// band; Raise the Dead summons Graveyard Laborers (never more than 4). The Game scene holds her in the fight library
    /// and <see cref="BossFight.ForcedBoss"/> picks her for Chamber 5. No GC per frame. Look: Docs/screenshots/game-kan111-*.
    /// </summary>
    public sealed class BoneMatriarchTests
    {
        private const string MatriarchPath = "Assets/_Project/Data/Boss/Boss_BoneMatriarch.asset";
        private const string ControllerPath = "Assets/_Project/Art/Characters/Bosses/BoneMatriarch/AC_BoneMatriarch.controller";
        private static readonly string[] OtherBosses =
        {
            "Assets/_Project/Data/Boss/Boss_Realm1_Butcher.asset",
            "Assets/_Project/Data/Boss/Boss_PyreLord.asset",
            "Assets/_Project/Data/Boss/Boss_RottingColossus.asset",
        };
        private const float Frame = 1f / 60f;
        private const float KnightSpeed = 5f; // PlayerMover base speed
        private const float BodyClear = 1.4f; // BossFight.SpawnClearRadius: ground a landing boss body needs
        private const int Circles = 8;

        /// <summary>Arena ring of 13 m with a pillar (r 2 m) 6 m east of the centre: the teleport must miss both.</summary>
        private sealed class RingWithPillar : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r)
            {
                if (p.x * p.x + p.z * p.z > (13f - r) * (13f - r)) return true;
                float dx = p.x - 6f;
                return dx * dx + p.z * p.z < (2f + r) * (2f + r);
            }
        }

        /// <summary>A wall across the ground at z = 4 m (bolts flying north stop there).</summary>
        private sealed class WallNorth : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r) => p.z + r > 4f;
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
        private Enemy _body;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private bool _pin = true;
        private readonly List<int> _started = new List<int>();
        private readonly List<float> _startedAt = new List<float>();
        private readonly List<int> _summoned = new List<int>();
        private readonly List<int> _novas = new List<int>();
        private readonly List<Vector3> _landed = new List<Vector3>();

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            BossFight.ForcedBoss = null;
            Random.InitState(1110);
            _pin = true;

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

            // The summon type by its roster name (Enemy_<id>), like the Game scene's wave type.
            var laborerGo = Track(new GameObject("Enemy_GraveyardLaborer"));
            laborerGo.SetActive(false);
            var laborer = laborerGo.AddComponent<Enemy>();
            _spawner = Track(new GameObject("Enemies")).AddComponent<EnemySpawner>();
            _spawner.enabled = false;
            _spawner.SetExtraTypes(new[] { laborer });

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
            _startedAt.Clear();
            _summoned.Clear();
            _novas.Clear();
            _landed.Clear();
            _fight = null;
            Enemy.Walkability = _savedWalk;
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
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>(MatriarchPath);
            Assert.That(data, Is.Not.Null, MatriarchPath);
            return data;
        }

        /// <summary>The shipped entry labelled <paramref name="label"/> (the asset's numbers).</summary>
        private static BossAttack Shipped(string label)
        {
            foreach (var a in Asset().Attacks)
                if (a.label == label) return a;
            Assert.Fail($"The Bone Matriarch has no '{label}'");
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
                _player, _souls, _telegraph, _lines, _bolts, null, _pool, null);
            _fight.AttackStarted += i =>
            {
                _started.Add(i);
                _startedAt.Add(_fight.FightTime);
            };
            _fight.Summoned += (at, n) => _summoned.Add(n);
            _fight.NovaFired += (at, n) => _novas.Add(n);
            _fight.Teleported += (from, to, dealt) => _landed.Add(to);
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
            if (_pin && _fight.Boss != null) _fight.Boss.transform.position = Vector3.zero; // no chase drift
            _fight.Tick(Frame);
            _bolts.Tick(Frame);
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame) Step();
        }

        private void EnterPhase2() => _fight.Boss.TakeDamage(_fight.Boss.Health * 0.6f, Vector3.zero, DamageKind.Burst);

        private static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ------------------------------------------------------------ Asset + body

        [Test]
        public void Asset_HasItsAttacks_Phases_Clips_WarningFloor_HitCap()
        {
            var data = Asset();
            Assert.That(data.DisplayName, Is.EqualTo("THE BONE MATRIARCH"));
            Assert.That(data.Prefab, Is.Not.Null);
            Assert.That(data.Prefab.name, Is.EqualTo("Enemy_BoneMatriarch"));
            Assert.That(data.Prefab.IsBoss, Is.True);
            Assert.That(data.RoarClip, Is.EqualTo("Roar"));
            Assert.That(data.Phase2At, Is.EqualTo(0.5f));

            var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);
            var states = new HashSet<string>();
            foreach (var s in controller.layers[0].stateMachine.states) states.Add(s.state.name);
            Assert.That(states, Is.SupersetOf(new[] { "CastOrbs", "CastRing", "Summon", "Roar", "Walking", "Death" }));

            foreach (var a in data.Attacks)
            {
                Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(data.TelegraphFor(a.Telegraph, 2), Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a + " phase 2");
                Assert.That(a.damage, Is.LessThanOrEqualTo(data.MaxHitShare), a.ToString());
                Assert.That(states, Does.Contain(a.clip), a + " clip");
            }
            Assert.That(data.Attacks.Length, Is.EqualTo(5));

            var skulls = Shipped("Homing Skulls");
            Assert.That(skulls.type, Is.EqualTo(BossFight.Move.Homing));
            Assert.That(skulls.count, Is.EqualTo(3), "3 at a time");
            Assert.That(skulls.length / skulls.speed, Is.EqualTo(4f).Within(1e-3f), "die after 4 s");
            Assert.That(skulls.speed, Is.LessThan(KnightSpeed), "slow");
            Assert.That(skulls.phases, Is.EqualTo(BossPhases.Both));

            var ring = Shipped("Soul Ring");
            Assert.That(ring.type, Is.EqualTo(BossFight.Move.Nova));
            Assert.That(ring.count, Is.EqualTo(24));
            Assert.That(ring.gaps, Is.EqualTo(3));
            Assert.That(ring.phases, Is.EqualTo(BossPhases.Both));

            var raise = Shipped("Raise the Dead");
            Assert.That(raise.type, Is.EqualTo(BossFight.Move.Summon));
            Assert.That(raise.summon, Is.EqualTo("GraveyardLaborer"), "the Bone Archer is cut (KAN-128)");
            Assert.That(raise.count, Is.EqualTo(4));
            Assert.That(raise.clip, Is.EqualTo("Summon"));

            var beams = Shipped("Cross Beams");
            Assert.That(beams.type, Is.EqualTo(BossFight.Move.Beams));
            Assert.That(beams.count, Is.EqualTo(4));
            Assert.That(beams.duration, Is.EqualTo(4f), "rotate for 4 s");
            Assert.That(beams.phases, Is.EqualTo(BossPhases.Phase2));

            var teleport = Shipped("Teleport");
            Assert.That(teleport.type, Is.EqualTo(BossFight.Move.Teleport));
            Assert.That(teleport.Timed, Is.True);
            Assert.That(teleport.every, Is.EqualTo(8f), "every 8 s");
            Assert.That(teleport.phases, Is.EqualTo(BossPhases.Phase2));
            Assert.That(teleport.radius, Is.GreaterThan(0f), "a warning circle where she lands");

            foreach (var a in data.Attacks)
                if (a.type != BossFight.Move.Teleport) Assert.That(a.Timed, Is.False, a + " is picked by score");
        }

        [Test]
        public void Body_NeverMelees_HoldsSixToNineMetres_Hovers_LeastHealth()
        {
            var data = Asset();
            var prefab = data.Prefab;
            Assert.That(prefab.DamagePerSecond, Is.EqualTo(0f), "never melee: no contact damage");
            Assert.That(prefab.KeepsDistance, Is.True, "hard to reach: keeps a band");
            Assert.That(prefab.HoldMin, Is.EqualTo(6f));
            Assert.That(prefab.HoldMax, Is.EqualTo(9f));
            Assert.That(Shipped("Teleport").length, Is.InRange(prefab.HoldMin, prefab.HoldMax), "lands inside her band");
            Assert.That(Shipped("Cross Beams").length, Is.GreaterThan(prefab.HoldMax), "beams reach a Knight at her band");

            var hover = prefab.GetComponentInChildren<HoverBob>(true);
            Assert.That(hover, Is.Not.Null, "floats");
            Assert.That(hover.transform, Is.Not.EqualTo(prefab.transform), "on the model: root + shadow stay on the ground");
            Assert.That(hover.GetComponentInChildren<SkinnedMeshRenderer>(true), Is.Not.Null, "the body mesh is under it");
            Assert.That(hover.Lift, Is.GreaterThan(0f));
            Assert.That(hover.Amplitude, Is.GreaterThan(0f).And.LessThan(0.5f), "a small bob");

            foreach (var path in OtherBosses)
            {
                var other = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>(path);
                Assert.That(other, Is.Not.Null, path);
                Assert.That(data.MaxHealth, Is.LessThan(other.MaxHealth), "least HP: " + path);
                Assert.That(other.Prefab.DamagePerSecond, Is.GreaterThan(0f), "the others still hurt on contact: " + path);
            }
            Assert.That(data.MaxHealth, Is.EqualTo(7000f), "doc 3.15");
        }

        [Test]
        public void HoverBob_LiftsTheModel_BobsInItsRange_HoldsStillWhenPaused()
        {
            var root = Track(new GameObject("Root"));
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.SetActive(false);
            var hover = model.AddComponent<HoverBob>();
            hover.Configure(0.3f, 0.12f, 2.6f);
            model.SetActive(true);
            Assert.That(hover.Offset, Is.EqualTo(0.3f).Within(1e-4f), "lifted at once");
            float low = float.MaxValue, high = float.MinValue;
            for (float t = 0f; t < 2.6f; t += Frame)
            {
                hover.Tick(Frame);
                low = Mathf.Min(low, hover.Offset);
                high = Mathf.Max(high, hover.Offset);
            }
            Assert.That(low, Is.EqualTo(0.3f).Within(0.002f));
            Assert.That(high, Is.EqualTo(0.42f).Within(0.002f), "lift + bob");
            Assert.That(root.transform.position.y, Is.EqualTo(0f), "the root stays on the ground");
            float now = hover.Offset;
            hover.Tick(0f);
            Assert.That(hover.Offset, Is.EqualTo(now), "paused: no bob");
        }

        // ------------------------------------------------------------ Homing skulls

        [Test]
        public void HomingSkulls_TurnRateIsCapped_AndTheyDieAfter4Seconds()
        {
            var skulls = Shipped("Homing Skulls");
            skulls.cooldown = 30f;
            StartFight(Data(new[] { skulls }), knightDistance: 7f);
            _player.Invulnerable = true;
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_bolts.Count, Is.EqualTo(3), "3 skulls");

            _player.transform.position = new Vector3(0f, 0f, -7f); // behind her: the skulls must turn round
            var before = new Vector3[3];
            for (int i = 0; i < 3; i++) before[i] = _bolts.VelocityOf(i);
            Step();
            for (int i = 0; i < 3; i++)
                Assert.That(Vector3.Angle(before[i], _bolts.VelocityOf(i)), Is.LessThanOrEqualTo(skulls.turn * Frame + 0.01f),
                    "turn rate cap");

            // The Knight circles far off so no skull reaches it: they fly their full life.
            float alive = Frame;
            for (float t = 0f; t < 6f && _bolts.Count > 0; t += Frame)
            {
                float a = t * 2f;
                _player.transform.position = new Vector3(Mathf.Sin(a) * 30f, 0f, Mathf.Cos(a) * 30f);
                Step();
                alive += Frame;
            }
            Assert.That(_bolts.Count, Is.EqualTo(0));
            Assert.That(alive, Is.EqualTo(4f).Within(0.1f), "the skulls die after 4 s");
        }

        [Test]
        public void HomingSkulls_AWallStopsThem()
        {
            var skulls = Shipped("Homing Skulls");
            skulls.cooldown = 30f;
            Enemy.Walkability = new WallNorth();
            StartFight(Data(new[] { skulls }), knightDistance: 7f);
            _player.Invulnerable = true;
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_bolts.Count, Is.EqualTo(3));
            Run(2f); // 3.6 m/s: the 4 m wall is reached well inside their 4 s
            Assert.That(_bolts.Count, Is.EqualTo(0), "gone on the wall");
            for (int i = 0; i < _bolts.Count; i++) Assert.That(_bolts.PositionOf(i).z, Is.LessThanOrEqualTo(4f));
        }

        // ------------------------------------------------------------ Soul ring

        [Test]
        public void SoulRing_24WithThreeGaps_AKnightInAGapIsSafe_OnABoltIsHitOnce()
        {
            var ring = Shipped("Soul Ring");
            ring.cooldown = 30f;
            StartFight(Data(new[] { ring }), knightDistance: 6f);
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_novas, Is.EqualTo(new[] { 24 - 3 * BossFight.GapBolts }), "24 slots, 3 gaps of 2 missing orbs");
            var yaws = new List<float>();
            for (int i = 0; i < _bolts.Count; i++) yaws.Add(Mathf.Repeat(Yaw(_bolts.VelocityOf(i)), 360f));
            yaws.Sort();
            int gaps = 0;
            float gapYaw = 0f;
            for (int i = 0; i < yaws.Count; i++)
            {
                float next = i + 1 < yaws.Count ? yaws[i + 1] : yaws[0] + 360f;
                if (next - yaws[i] < 40f) continue;
                gaps++;
                gapYaw = (yaws[i] + next) * 0.5f;
            }
            Assert.That(gaps, Is.EqualTo(3), "three wide gaps");

            float rad = gapYaw * Mathf.Deg2Rad; // stand in the middle of a gap before the ring gets there
            _player.transform.position = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * 6f;
            Run(3f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "the gap is safe");

            // Next cast: the Knight stands right on an orb's path.
            _novas.Clear();
            RunUntil(BossFight.Attack.Chase, 3f);
            _bolts.Clear();
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_bolts.Count, Is.GreaterThan(0));
            Vector3 path = _bolts.VelocityOf(0).normalized;
            _player.transform.position = path * 6f;
            Run(3f);
            Assert.That(_player.Max - _player.Current, Is.EqualTo(ring.damage * _player.Max).Within(0.01f), "one ring hits once");
        }

        // ------------------------------------------------------------ Cross beams

        /// <summary>Phase 2 Cross Beams: starts the cast with the Knight <paramref name="distance"/> m north (in a gap).</summary>
        private BossAttack StartBeams(float distance)
        {
            var beams = Shipped("Cross Beams");
            beams.cooldown = 30f;
            StartFight(Data(new[] { beams }), knightDistance: distance);
            EnterPhase2();
            RunUntil(BossFight.Attack.Chase, 3f); // the phase 2 roar
            Assert.That(_fight.ForceAttack(0), Is.True);
            Assert.That(_lines.LaneCount, Is.EqualTo(4));
            for (int i = 0; i < 4; i++)
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(Yaw(_lines.LaneDirection(i)), 0f)), Is.GreaterThanOrEqualTo(44f),
                    "the Knight starts in a gap");
            RunUntil(BossFight.Attack.Casting, 2f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "no hit while they warn");
            return beams;
        }

        [Test]
        public void CrossBeams_SweepOverAKnightWhoStandsStill_HitOnce()
        {
            var beams = StartBeams(6f);
            Assert.That(beams.spread * beams.duration, Is.GreaterThan(45f), "a lane turns past the gap middle");
            RunUntil(BossFight.Attack.Recover, beams.duration + 0.5f);
            Assert.That(_player.Max - _player.Current, Is.EqualTo(beams.damage * _player.Max).Within(0.01f), "swept once");
        }

        [Test]
        public void CrossBeams_AKnightWalkingWithTheGap_OrPastTheirLength_IsSafe()
        {
            var beams = StartBeams(6f);
            // The gap turns 45 deg/s: at 6 m that is under the Knight's run speed, so it can always keep a safe sector.
            Assert.That(beams.spread * Mathf.Deg2Rad * 6f, Is.LessThan(KnightSpeed));
            float lane0 = Yaw(_lines.LaneDirection(0));
            while (_fight.CurrentAttack == BossFight.Attack.Casting)
            {
                Step();
                float turned = Mathf.DeltaAngle(lane0, Yaw(_lines.LaneDirection(0)));
                lane0 = Yaw(_lines.LaneDirection(0));
                float knight = Yaw(_player.transform.position) + turned;
                float rad = knight * Mathf.Deg2Rad;
                _player.transform.position = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * 6f;
            }
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "walked with the gap");

            TearDown();
            SetUp();
            beams = StartBeams(6f);
            _player.transform.position = new Vector3(0f, 0f, beams.length + 1f); // stands past their reach
            RunUntil(BossFight.Attack.Recover, beams.duration + 0.5f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "outside their length");
        }

        // ------------------------------------------------------------ Teleport

        [Test]
        public void Teleport_IsTimed_Every8sInPhase2_NeverInPhase1()
        {
            var teleport = Shipped("Teleport");
            StartFight(Data(new[] { teleport }, firstAttackDelay: 0.2f), knightDistance: 7.5f);
            _player.Invulnerable = true;
            Run(12f);
            Assert.That(_started, Is.Empty, "phase 1: no teleport");

            EnterPhase2();
            float phase2 = _fight.FightTime;
            for (float t = 0f; t < 26f; t += Frame)
            {
                _player.transform.position = new Vector3(0f, 0f, 7.5f);
                Step();
            }
            Assert.That(_started.Count, Is.EqualTo(3), "8 s, 16 s, 24 s into phase 2");
            Assert.That(_startedAt[0] - phase2, Is.EqualTo(8f).Within(2f * Frame), "the timer starts with phase 2");
            for (int i = 1; i < _startedAt.Count; i++)
                Assert.That(_startedAt[i] - _startedAt[i - 1], Is.EqualTo(8f).Within(2f * Frame), "every 8 s");
        }

        [Test]
        public void Teleport_WhenDue_ComesBeforeTheOtherAttacks()
        {
            var skulls = Shipped("Homing Skulls");
            var teleport = Shipped("Teleport");
            StartFight(Data(new[] { skulls, teleport }, firstAttackDelay: 0.2f), knightDistance: 7.5f);
            _player.Invulnerable = true;
            EnterPhase2();
            float phase2 = _fight.FightTime;
            for (float t = 0f; t < 30f; t += Frame)
            {
                _player.transform.position = new Vector3(0f, 0f, 7.5f);
                Step();
            }
            var at = new List<float>();
            for (int i = 0; i < _started.Count; i++)
                if (_started[i] == 1) at.Add(_startedAt[i]);
            Assert.That(_started, Does.Contain(0), "the skulls fill the time in between");
            Assert.That(at.Count, Is.InRange(2, 3));
            Assert.That(at[0] - phase2, Is.InRange(8f - Frame, 8f + 5f), "at the first free pick once due");
            for (int i = 1; i < at.Count; i++)
                Assert.That(at[i] - at[i - 1], Is.InRange(8f - Frame, 8f + 5f), "about every 8 s, never sooner");
        }

        [Test]
        public void Teleport_LandsOnWalkableGround_InsideHerBand()
        {
            var teleport = Shipped("Teleport");
            teleport.every = 0f; // forced casts here
            Enemy.Walkability = new RingWithPillar();
            var walk = Enemy.Walkability;
            StartFight(Data(new[] { teleport }), knightDistance: 4f);
            _pin = false;
            _player.Invulnerable = true;
            for (int cast = 0; cast < 12; cast++)
            {
                _player.transform.position = new Vector3(cast % 2 == 0 ? 2f : -2f, 0f, cast % 3 - 1f);
                RunUntil(BossFight.Attack.Chase, 4f);
                Assert.That(_fight.ForceAttack(0), Is.True, "cast " + cast);
                Assert.That(_telegraph.IsShowing, Is.True, "a circle warns where she lands");
                RunUntil(BossFight.Attack.Chase, 4f);
            }
            Assert.That(_landed.Count, Is.EqualTo(12));
            for (int i = 0; i < _landed.Count; i++)
                Assert.That(walk.IsBlocked(_landed[i], BodyClear), Is.False, $"landing {i} at {_landed[i]} is walkable");
            var prefab = Asset().Prefab;
            Assert.That(teleport.length, Is.InRange(prefab.HoldMin, prefab.HoldMax));
        }

        // ------------------------------------------------------------ Raise the dead

        [Test]
        public void RaiseTheDead_SummonsFourGraveyardLaborers_NeverMoreThanFour()
        {
            var raise = Shipped("Raise the Dead");
            StartFight(Data(new[] { raise }, firstAttackDelay: 0.2f), knightDistance: 7f);
            _player.Invulnerable = true;
            for (float t = 0f; t < 8f && _summoned.Count == 0; t += Frame) Step();
            Assert.That(_summoned, Is.EqualTo(new[] { 4 }), "phase 1 already: 4 rise");
            Assert.That(_fight.SummonsAlive, Is.EqualTo(4));
            int laborers = 0;
            foreach (Transform child in _spawner.transform)
            {
                var e = child.GetComponent<Enemy>();
                if (e != null && e.IsAlive && child.name.StartsWith("Enemy_GraveyardLaborer")) laborers++;
            }
            Assert.That(laborers, Is.EqualTo(4), "the summon type is the Graveyard Laborer");
            RunUntil(BossFight.Attack.Chase, 4f);
            Assert.That(_fight.ForceAttack(BossFight.Move.Summon), Is.False, "4 alive: cap reached");
        }

        // ------------------------------------------------------------ GC

        [Test]
        public void SkullsBeamsAndHover_NoGcPerFrame()
        {
            var hover = Track(new GameObject("Model")).AddComponent<HoverBob>();
            var skulls = Shipped("Homing Skulls");
            StartFight(Data(new[] { skulls, Shipped("Cross Beams") }), knightDistance: 7f);
            _player.Invulnerable = true;
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Step();
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    Step();
                    hover.Tick(Frame);
                }
            }, Is.Not.AllocatingGCMemory());
            RunUntil(BossFight.Attack.Chase, 6f);
            Assert.That(_fight.ForceAttack(1), Is.True);
            RunUntil(BossFight.Attack.Casting, 2f);
            Step();
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++) Step();
            }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>The built Game scene: the Matriarch sits in the fight library; the dev flag makes Chamber 5 her fight.</summary>
        [UnityTest]
        public IEnumerator GameScene_ForcedMatriarch_FightsInChamber5_Floating()
        {
            TearDown(); // this test uses the real scene's objects
            BossFight.ForcedBoss = "BoneMatriarch";
            RunState.NewRun(1, 9111);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            Assert.That(System.Array.Exists(fight.Library, d => d != null && d.name == "Boss_BoneMatriarch"), Is.True,
                "in the library");
            Assert.That(System.Array.Exists(fight.Bosses, d => d != null && d.name == "Boss_BoneMatriarch"), Is.False,
                "not in Realm 1's rotation");
            Assert.That(fight.Data.DisplayName, Is.EqualTo("THE BONE MATRIARCH"));
            Assert.That(fight.Data.Prefab.name, Is.EqualTo("Enemy_BoneMatriarch"));
            Assert.That(Object.FindAnyObjectByType<EnemySpawner>().FindType("Enemy_GraveyardLaborer"), Is.Not.Null,
                "her summon type is in the scene");

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f;
            while (fight.Current != BossFight.Stage.Intro && fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Boss, Is.Not.Null);
            yield return null;
            Assert.That(fight.Boss.AttackStateHash, Is.EqualTo(Animator.StringToHash("Roar")), "intro roar");
            Assert.That(fight.Boss.GetComponentInChildren<Animator>().HasState(0, Animator.StringToHash("CastRing")), Is.True);
            Assert.That(fight.Boss.DamagePerSecond, Is.EqualTo(0f));
            var hover = fight.Boss.GetComponentInChildren<HoverBob>();
            Assert.That(hover, Is.Not.Null);
            Assert.That(hover.Offset, Is.GreaterThanOrEqualTo(hover.Lift - 0.001f), "she floats");

            BossFight.ForcedBoss = null;
            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            var empty = SceneManager.CreateScene("MatriarchEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
