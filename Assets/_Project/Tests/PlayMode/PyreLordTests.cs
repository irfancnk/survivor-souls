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
    /// KAN-109 acceptance, logic side: The Pyre Lord asset (Docs/ENEMY_ROSTER.md 3.13) has its five attacks with the right
    /// phases, clips, warnings (&gt;= 0.6 s) and hit cap; its body glows (baked lava emission). The flame slam warns round the
    /// body, a Knight running out at Knight speed is safe, a Knight that stays is hit once, and the slam leaves a burning
    /// patch that hurts only inside and goes out after its life. Meteors leave small patches (capped per boss). A path through
    /// the nova and through the spiral exists at Knight speed (searched on the real fight, the Knight stays near the boss).
    /// The Game scene holds the Pyre Lord in the fight library and <see cref="BossFight.ForcedBoss"/> picks it for Chamber 5.
    /// No GC per frame with patches burning. Look and feel: screenshots (Docs/screenshots/game-kan109-*).
    /// </summary>
    public sealed class PyreLordTests
    {
        private const string PyreLordPath = "Assets/_Project/Data/Boss/Boss_PyreLord.asset";
        private const string ControllerPath = "Assets/_Project/Art/Characters/Bosses/PyreLord/AC_PyreLord.controller";
        private const string MaterialPath = "Assets/_Project/Art/Characters/Bosses/PyreLord/M_PyreLord.mat";
        private const float Frame = 1f / 60f;
        private const float KnightSpeed = 5f; // PlayerMover base speed
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
        private GroundHazards _hazards;
        private Enemy _body;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<float> _burnRadii = new List<float>();

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            BossFight.ForcedBoss = null;
            Random.InitState(1090);

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

            var circles = new GroundTelegraph[Circles];
            for (int i = 0; i < Circles; i++)
            {
                circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                circles[i].Setup(Quad(), Quad());
            }
            _pool = Track(new GameObject("Warnings")).AddComponent<EnemyTelegraphs>();
            _pool.Setup(new LineTelegraph[0], circles);

            _hazards = Track(new GameObject("Hazards")).AddComponent<GroundHazards>(); // GroundHazards.Current
            _hazards.Setup(_player, null, 24);

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
            _burnRadii.Clear();
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
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>(PyreLordPath);
            Assert.That(data, Is.Not.Null, PyreLordPath);
            return data;
        }

        /// <summary>The shipped entry of <paramref name="type"/> (the asset's numbers).</summary>
        private static BossAttack Shipped(BossFight.Move type)
        {
            foreach (var a in Asset().Attacks)
                if (a.type == type) return a;
            Assert.Fail($"The Pyre Lord has no {type}");
            return default;
        }

        /// <summary>A test boss on the dummy body that runs the shipped <paramref name="attacks"/> (same hit cap as the asset).</summary>
        private BossData Data(params BossAttack[] attacks) =>
            Track(BossData.CreateWithAttacks(_body, attacks, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: 30f, maxHitShare: Asset().MaxHitShare));

        /// <summary>Begin + rise; the boss stands at the origin facing +Z, the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null, _pool);
            _fight.BurnPlaced += (at, r) => _burnRadii.Add(r);
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
            if (_fight.Boss != null) _fight.Boss.transform.position = Vector3.zero; // the boss stands (no chase drift)
            _fight.Tick(Frame);
            _bolts.Tick(Frame);
            _hazards.Tick(Frame);
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame) Step();
        }

        private void Heal() => _player.Heal(_player.Max);

        // ------------------------------------------------------------ Asset

        [Test]
        public void Asset_HasItsFiveAttacks_Phases_Clips_WarningFloor_HitCap()
        {
            var data = Asset();
            Assert.That(data.DisplayName, Is.EqualTo("THE PYRE LORD"));
            Assert.That(data.MaxHealth, Is.EqualTo(8000f), "doc 9000; 8000 keeps a no-pick Knight under 4 min (Balance test)");
            Assert.That(data.Prefab, Is.Not.Null);
            Assert.That(data.Prefab.name, Is.EqualTo("Enemy_PyreLord"));
            Assert.That(data.Prefab.IsBoss, Is.True);
            Assert.That(data.RoarClip, Is.EqualTo("Roar"));
            Assert.That(data.Phase2At, Is.EqualTo(0.5f));

            var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);
            var states = new HashSet<string>();
            foreach (var s in controller.layers[0].stateMachine.states) states.Add(s.state.name);
            Assert.That(states, Is.SupersetOf(new[] { "CastFan", "CastNova", "Slam", "Roar", "Walk", "Death" }));

            var byType = new Dictionary<BossFight.Move, BossAttack>();
            foreach (var a in data.Attacks)
            {
                byType[a.type] = a;
                Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a.ToString());
                Assert.That(data.TelegraphFor(a.Telegraph, 2), Is.GreaterThanOrEqualTo(BossData.MinTelegraph), a + " phase 2");
                Assert.That(a.damage, Is.LessThanOrEqualTo(data.MaxHitShare), a.ToString());
                Assert.That(states, Does.Contain(a.clip), a + " clip");
            }
            Assert.That(byType.Keys, Is.EquivalentTo(new[]
                { BossFight.Move.Slam, BossFight.Move.Fan, BossFight.Move.Nova, BossFight.Move.Spiral, BossFight.Move.Rain }));
            Assert.That(byType[BossFight.Move.Fan].count, Is.EqualTo(7));
            Assert.That(byType[BossFight.Move.Fan].spread, Is.EqualTo(70f));
            Assert.That(byType[BossFight.Move.Nova].count, Is.EqualTo(16));
            Assert.That(byType[BossFight.Move.Nova].gaps, Is.EqualTo(2));
            Assert.That(byType[BossFight.Move.Slam].radius, Is.EqualTo(4f));
            Assert.That(byType[BossFight.Move.Spiral].count, Is.EqualTo(2));
            Assert.That(byType[BossFight.Move.Spiral].duration, Is.EqualTo(3f));
            Assert.That(byType[BossFight.Move.Rain].count, Is.EqualTo(6));
            Assert.That(byType[BossFight.Move.Rain].Telegraph, Is.EqualTo(1f));
            foreach (var t in new[] { BossFight.Move.Slam, BossFight.Move.Fan, BossFight.Move.Nova })
                Assert.That(byType[t].phases, Is.EqualTo(BossPhases.Both), t.ToString());
            foreach (var t in new[] { BossFight.Move.Spiral, BossFight.Move.Rain })
                Assert.That(byType[t].phases, Is.EqualTo(BossPhases.Phase2), t + " joins in phase 2");
            Assert.That(byType[BossFight.Move.Slam].Burns, Is.True, "flame circle leaves a burning patch");
            Assert.That(byType[BossFight.Move.Rain].Burns, Is.True, "meteors leave burning patches");
            var spiral = byType[BossFight.Move.Spiral];
            Assert.That(spiral.count * Mathf.CeilToInt(spiral.duration / spiral.interval), Is.LessThanOrEqualTo(64),
                "the spiral fits the 64 bolt pool");
        }

        [Test]
        public void Body_GlowsWithBakedLavaEmission()
        {
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.That(mat, Is.Not.Null);
            Assert.That(mat.IsKeywordEnabled("_EMISSION"), Is.True);
            Assert.That(mat.GetTexture("_EmissionMap"), Is.Not.Null);
            Assert.That(mat.GetTexture("_EmissionMap").name, Is.EqualTo("T_PyreLord_Emission"));
        }

        // ------------------------------------------------------------ Flame slam + burning patch

        [Test]
        public void FlameSlam_WarnsRoundTheBody_RunningOutIsSafe_StayingIsHitOnce()
        {
            var slam = Shipped(BossFight.Move.Slam);
            slam.cooldown = 30f; // one slam: the patch alone afterwards
            StartFight(Data(slam), knightDistance: 2f);
            Assert.That(_fight.ForceAttack(0), Is.True);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Slam));
            Assert.That(_fight.Boss.AttackStateHash, Is.EqualTo(Animator.StringToHash("Slam")).Or.EqualTo(0));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_telegraph.Radius, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(new Vector2(_telegraph.Centre.x, _telegraph.Centre.z).magnitude, Is.LessThan(0.01f), "round the body");

            // Run straight out at Knight speed from 2 m: out of the 4 m circle well before the strike.
            float needed = (slam.radius - 2f) / KnightSpeed;
            Assert.That(needed, Is.LessThan(_fight.WindUpTime * 0.5f), "half the warning is enough to get out");
            while (_fight.CurrentAttack == BossFight.Attack.WindUp)
            {
                var p = _player.transform.position;
                if (p.z < slam.radius + 0.5f) _player.transform.position = p + Vector3.forward * KnightSpeed * Frame;
                Step();
            }
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "ran out of the circle");

            // The patch burns at the centre: r 2.2 m, outside it the Knight is safe.
            Assert.That(_burnRadii, Is.EqualTo(new[] { slam.burnRadius }));
            Assert.That(_hazards.Count, Is.EqualTo(1));
            Assert.That(_hazards.SpecOf(0).kind, Is.EqualTo(HazardKind.Fire));
            Assert.That(_hazards.OwnerOf(0), Is.EqualTo(BossFight.BurnOwner));
            Run(1f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "outside the patch");

            // Inside the patch: burnDps share of max health per second, until it goes out.
            _player.transform.position = new Vector3(0.5f, 0f, 0.5f);
            Run(1f);
            float lost = _player.Max - _player.Current;
            Assert.That(lost, Is.EqualTo(slam.burnDps * _player.Max).Within(_player.Max * 0.01f), "1 s in the fire");
            Run(slam.burnLife);
            Assert.That(_hazards.Count, Is.EqualTo(0), "the patch went out after its life");
            float after = _player.Current;
            Run(0.5f);
            Assert.That(_player.Current, Is.EqualTo(after), "no burn after it went out");
        }

        [Test]
        public void FlameSlam_KnightThatStays_IsHitOnceByTheShare()
        {
            var slam = Shipped(BossFight.Move.Slam);
            StartFight(Data(slam), knightDistance: 3f); // outside the patch, inside the circle
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_player.Max - _player.Current, Is.EqualTo(slam.damage * _player.Max).Within(0.01f));
            Run(0.5f);
            Assert.That(_player.Max - _player.Current, Is.EqualTo(slam.damage * _player.Max).Within(0.01f),
                "outside the 2.2 m patch: no burn");
        }

        // ------------------------------------------------------------ Meteor rain patches

        [Test]
        public void MeteorRain_EachMeteorLeavesAPatch_CappedPerBoss()
        {
            var rain = Shipped(BossFight.Move.Rain);
            StartFight(Data(rain), knightDistance: 6f);
            _player.Invulnerable = true;
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 6f);
            Assert.That(_burnRadii.Count, Is.EqualTo(rain.count), "one patch per meteor");
            Assert.That(_burnRadii, Has.All.EqualTo(rain.burnRadius));
            Assert.That(_hazards.CountOwned(BossFight.BurnOwner), Is.EqualTo(rain.count));

            RunUntil(BossFight.Attack.Chase, 3f);
            Assert.That(_fight.ForceAttack(0), Is.True);
            for (float t = 0f; t < 6f && _burnRadii.Count < rain.count + 3; t += Frame) Step(); // 3 more fall while the first still burn
            Assert.That(_hazards.CountOwned(BossFight.BurnOwner), Is.LessThanOrEqualTo(BossFight.MaxBurns));
        }

        // ------------------------------------------------------------ Dodgeable (a path exists at Knight speed)

        [Test]
        public void FireNova_StepIntoTheNearestGap_AtKnightSpeed_IsSafe()
        {
            var nova = Shipped(BossFight.Move.Nova);
            for (int cast = 0; cast < 5; cast++) // random ring turn per cast
            {
                if (_fight != null) Object.DestroyImmediate(_fight.Boss.gameObject);
                StartFight(Data(nova), knightDistance: 5f);
                Heal();
                Assert.That(_fight.ForceAttack(0), Is.True);
                RunUntil(BossFight.Attack.Recover, 3f); // fired: the bolts' angles are known
                Assert.That(_bolts.Count, Is.EqualTo(nova.count - nova.gaps * BossFight.GapBolts));

                // The Knight sees the ring leave the body and steps to the middle of the nearest gap (chord at Knight speed).
                float target = NearestGapMiddle(0f);
                Vector3 goal = new Vector3(Mathf.Sin(target * Mathf.Deg2Rad), 0f, Mathf.Cos(target * Mathf.Deg2Rad)) * 5f;
                for (float t = 0f; t < 2.5f; t += Frame)
                {
                    _player.transform.position = Vector3.MoveTowards(_player.transform.position, goal, KnightSpeed * Frame);
                    Step();
                }
                Assert.That(_player.Current, Is.EqualTo(_player.Max), $"cast {cast}: the gap at {target:F1} deg is safe");
            }
        }

        /// <summary>Middle (deg, clockwise from +Z) of the bolt gap nearest <paramref name="angle"/>.</summary>
        private float NearestGapMiddle(float angle)
        {
            var a = new List<float>();
            for (int i = 0; i < _bolts.Count; i++)
            {
                Vector3 p = _bolts.PositionOf(i);
                a.Add(Mathf.Repeat(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg, 360f));
            }
            a.Sort();
            float best = 0f, bestDelta = float.MaxValue;
            for (int i = 0; i < a.Count; i++)
            {
                float next = i + 1 < a.Count ? a[i + 1] : a[0] + 360f;
                float mid = (a[i] + next) * 0.5f;
                float delta = Mathf.Abs(Mathf.DeltaAngle(angle, mid));
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = mid;
                }
            }
            return best;
        }

        [Test]
        public void FireSpiral_APathThroughTheArmsExists_AtKnightSpeed_NearTheBoss()
        {
            var spiral = Shipped(BossFight.Move.Spiral);
            const float start = 8f, leash = 10f; // the Knight stays within 10 m of the boss (no running out of range)
            var found = new List<string>();
            int tried = 0;
            StartFight(Data(spiral), knightDistance: start);
            for (int heading = 0; heading < 360; heading += 30)
            {
                for (float walk = 0.2f; walk <= 1.21f; walk += 0.2f)
                {
                    tried++;
                    RunUntil(BossFight.Attack.Chase, 6f);
                    _bolts.Clear();
                    _player.transform.position = new Vector3(0f, 0f, start);
                    Heal();
                    Assert.That(_fight.ForceAttack(0), Is.True);
                    RunUntil(BossFight.Attack.Casting, 3f);
                    // From the first bolt: walk `walk` s at Knight speed on `heading` (0 = straight out), then stand.
                    float rad = heading * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                    float worst = 0f;
                    for (float t = 0f; t < spiral.duration + spiral.length / spiral.speed + 0.5f; t += Frame)
                    {
                        if (t < walk) _player.transform.position += dir * KnightSpeed * Frame;
                        worst = Mathf.Max(worst, new Vector2(_player.transform.position.x, _player.transform.position.z).magnitude);
                        Step();
                    }
                    if (worst <= leash && _player.Current >= _player.Max) found.Add($"{heading} deg for {walk:F1} s");
                }
            }
            Debug.Log($"[PyreLordTests] spiral: {found.Count}/{tried} paths safe within {leash} m: {string.Join(", ", found)}");
            Assert.That(found, Is.Not.Empty, "some walk through the arms takes no hit");
            Assert.That(found.Count, Is.LessThan(tried), "the spiral is a threat: standing badly gets hit");
        }

        // ------------------------------------------------------------ GC

        [Test]
        public void BurningPatches_NoGcPerFrame()
        {
            var slam = Shipped(BossFight.Move.Slam);
            StartFight(Data(slam), knightDistance: 1.5f);
            _player.Invulnerable = true;
            Assert.That(_fight.ForceAttack(0), Is.True);
            RunUntil(BossFight.Attack.Recover, 3f);
            Assert.That(_hazards.Count, Is.EqualTo(1));
            Step();
            Assert.That(() => { for (int i = 0; i < 30; i++) Step(); }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>The built Game scene: the Pyre Lord sits in the fight library; the dev flag makes Chamber 5 its fight.</summary>
        [UnityTest]
        public IEnumerator GameScene_ForcedPyreLord_FightsInChamber5()
        {
            TearDown(); // this test uses the real scene's objects
            BossFight.ForcedBoss = "PyreLord";
            RunState.NewRun(1, 9109);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            Assert.That(System.Array.Exists(fight.Library, d => d != null && d.name == "Boss_PyreLord"), Is.True, "in the library");
            Assert.That(System.Array.Exists(fight.Bosses, d => d != null && d.name == "Boss_PyreLord"), Is.False,
                "not in Realm 1's rotation");
            Assert.That(fight.Data.DisplayName, Is.EqualTo("THE PYRE LORD"));
            Assert.That(fight.Data.Prefab.name, Is.EqualTo("Enemy_PyreLord"));

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f;
            while (fight.Current != BossFight.Stage.Intro && fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Boss, Is.Not.Null);
            Assert.That(fight.Boss.AttackStateHash, Is.EqualTo(Animator.StringToHash("Roar")), "intro roar");
            Assert.That(GroundHazards.Current, Is.Not.Null, "patches have a pool in the Game scene");

            BossFight.ForcedBoss = null;
            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            var empty = SceneManager.CreateScene("PyreLordEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        [Test]
        public void ForcedBoss_UnknownId_KeepsTheNormalPick()
        {
            var a = Track(BossData.CreateWithAttacks(_body, new[] { Shipped(BossFight.Move.Slam) }));
            var fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            fight.Setup(a, null, _timer, _spawner, _player, _souls, _telegraph);
            fight.SetLibrary(new[] { Asset() });
            BossFight.ForcedBoss = "NoSuchBoss";
            Assert.That(fight.BossFor(5), Is.Null, "no list, no match: keep data");
            BossFight.ForcedBoss = "PyreLord";
            Assert.That(fight.BossFor(5), Is.SameAs(Asset()));
            BossFight.ForcedBoss = "boss_pyrelord";
            Assert.That(fight.BossFor(5), Is.SameAs(Asset()), "asset name works too");
        }
    }
}
