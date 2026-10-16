using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.MiniBosses;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Mini bosses (KAN-23.1 = KAN-211): the Chamber type rule with an offset and the shipped Realm 1 schedule (Chambers 2
    /// and 7), the promotion (health, size, tint flag, undone by the next spawn of the pooled body), the director (armed
    /// only in a mini boss Chamber, delay, warning, one rise per Chamber), the kill reward (bonus wisps, exactly one chest of
    /// the data tier, no extra skill chest, +1 upgrade with Reliquary Hunter, no chest without an upgradable skill) and the
    /// shipped data asset.
    /// </summary>
    public sealed class MiniBossTests
    {
        private const string DifficultyPath = "Assets/_Project/Data/Chamber/ChamberDifficulty_Realm1.asset";
        private const string DataPath = "Assets/_Project/Data/MiniBoss/MiniBoss_Realm1_Gravewarden.asset";
        private const string TypeName = "Enemy_TestBrute";

        private readonly List<Object> _objects = new List<Object>();
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private PlayerHealth _player;
        private PlayerStats _stats;
        private SoulWispField _souls;
        private EnemySpawner _spawner;
        private Enemy _prefab;
        private ChestDropper _chests;
        private ChestDropConfig _config;
        private GroundTelegraph _warning;

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;

            var playerGo = Track(new GameObject("Player"));
            _player = playerGo.AddComponent<PlayerHealth>();
            _stats = playerGo.AddComponent<PlayerStats>();
            var collector = playerGo.AddComponent<SoulCollector>();
            collector.Setup(0.01f, 1f); // nothing flies in: dropped XP stays countable

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

            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false); // no Start: only its pool is used
            _spawner = spawnerGo.AddComponent<EnemySpawner>();
            var template = Track(new GameObject(TypeName));
            template.SetActive(false);
            _prefab = template.AddComponent<Enemy>(); // type max health 150, 10 XP
            _spawner.SetExtraTypes(new[] { _prefab });

            _config = Track(ChestDropConfig.Create());
            _chests = Track(new GameObject("Chests")).AddComponent<ChestDropper>();
            _chests.Setup(_config, _stats, MakeChestPrefab(), rngSeed: 23);

            _warning = Track(new GameObject("Warning")).AddComponent<GroundTelegraph>();
            var ring = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            var fill = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            Object.DestroyImmediate(fill.GetComponent<Collider>());
            _warning.Setup(ring, fill);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
            GamePause.Clear();
            Time.timeScale = 1f;
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private ReliquaryChest MakeChestPrefab()
        {
            var root = Track(new GameObject("Chest Template"));
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var beam = new GameObject("Beam").AddComponent<MeshRenderer>();
            beam.transform.SetParent(root.transform, false);
            var glow = new GameObject("Glow").AddComponent<MeshRenderer>();
            glow.transform.SetParent(root.transform, false);
            var chest = root.AddComponent<ReliquaryChest>();
            chest.Setup(body.transform, beam, glow);
            root.SetActive(false);
            return chest;
        }

        private MiniBossDirector NewDirector(MiniBossData data, SkillManager skills = null, MiniBossAura aura = null)
        {
            var go = Track(new GameObject("MiniBoss"));
            go.SetActive(false); // no Start before the wiring
            var director = go.AddComponent<MiniBossDirector>();
            director.Setup(data, _spawner, _player, _chests, skills, null, null, _souls, _warning, aura);
            go.SetActive(true);
            return director;
        }

        private MiniBossData Data(float delay = 2f, float warning = 1f, ChestTier tier = ChestTier.Light, int upgrades = 1) =>
            Track(MiniBossData.Create(TypeName, health: 4f, size: 1.5f, delay: delay, warning: warning, xp: 60, wisps: 6,
                tier: tier, choices: 3, upgrades: upgrades, hunterBonus: 1));

        /// <summary>Arms and runs the director to the rise.</summary>
        private static Enemy Rise(MiniBossDirector director)
        {
            Assert.That(director.Arm(), Is.True);
            director.Tick(director.Data.SpawnDelay + 0.01f);
            director.Tick(director.Data.WarningTime + 0.01f);
            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Alive));
            return director.Boss;
        }

        // ------------------------------------------------------------ Schedule

        [Test]
        public void TypeRule_EveryFiveOffsetTwo_IsChambersTwoAndSeven_BossWins()
        {
            var d = Track(ChamberDifficulty.Create(chambers: 10, boss: 5, miniBoss: 5, miniBossOffset: 2));
            for (int c = 1; c <= 10; c++)
            {
                var expected = c == 5 || c == 10 ? ChamberType.Boss : c == 2 || c == 7 ? ChamberType.MiniBoss : ChamberType.Normal;
                Assert.That(d.TypeOf(c), Is.EqualTo(expected), $"Chamber {c}");
            }
            var clash = Track(ChamberDifficulty.Create(chambers: 20, boss: 5, miniBoss: 5, miniBossOffset: 0));
            Assert.That(clash.TypeOf(10), Is.EqualTo(ChamberType.Boss), "boss wins over mini boss");
        }

        [Test]
        public void ShippedRealm1_MiniBossInChambersTwoAndSeven()
        {
            var d = UnityEditor.AssetDatabase.LoadAssetAtPath<ChamberDifficulty>(DifficultyPath);
            Assert.That(d, Is.Not.Null, DifficultyPath);
            var minis = new List<int>();
            for (int c = 1; c <= d.ChambersInRealm; c++)
                if (d.TypeOf(c) == ChamberType.MiniBoss) minis.Add(c);
            Assert.That(minis, Is.EqualTo(new[] { 2, 7 }));
        }

        [Test]
        public void ShippedData_GraveBruteInTheRealm1Waves_SaneNumbers()
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<MiniBossData>(DataPath);
            Assert.That(data, Is.Not.Null, DataPath);
            Assert.That(data.EnemyType, Is.EqualTo("Enemy_GraveBrute"));
            var waves = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyWaveTable>("Assets/_Project/Data/Combat/EnemyWaves_Realm1.asset");
            Assert.That(waves, Is.Not.Null);
            bool found = false;
            for (int i = 0; i < waves.PhaseCount && !found; i++)
                foreach (var e in waves.GetPhase(i).enemies)
                    if (e.prefab != null && e.prefab.name == data.EnemyType) found = true;
            Assert.That(found, Is.True, "the spawner can only raise a type of its wave table");
            Assert.That(data.HealthMultiplier, Is.InRange(2f, 8f));
            Assert.That(data.SizeMultiplier, Is.InRange(1.1f, 1.8f));
            Assert.That(data.WarningTime, Is.GreaterThanOrEqualTo(0.9f), "a readable warning");
            Assert.That(data.BonusXp, Is.GreaterThan(0));
            Assert.That(data.HunterSkillId, Is.EqualTo("reliquary_hunter"));
        }

        // ------------------------------------------------------------ Promotion

        [Test]
        public void Promote_HealthSizeFlag_UndoneByTheNextSpawn()
        {
            var enemy = Track(Object.Instantiate(_prefab));
            enemy.Spawn(Vector3.zero, null, null, 1.5f);
            float health = enemy.MaxHealth, scale = enemy.Scale, radius = enemy.HitRadius;
            Assert.That(enemy.IsMiniBoss, Is.False);

            enemy.PromoteToMiniBoss(4f, 1.5f, new Color(1.2f, 0.5f, 0.4f));
            Assert.That(enemy.IsMiniBoss, Is.True);
            Assert.That(enemy.MaxHealth, Is.EqualTo(health * 4f).Within(0.01f));
            Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth));
            Assert.That(enemy.Scale, Is.EqualTo(scale * 1.5f).Within(0.001f));
            Assert.That(enemy.HitRadius, Is.EqualTo(radius * 1.5f).Within(0.001f));
            Assert.That(enemy.transform.localScale.x, Is.EqualTo(enemy.Scale).Within(0.001f));

            enemy.Spawn(Vector3.zero, null, null, 1.5f); // the pool hands the body out again
            Assert.That(enemy.IsMiniBoss, Is.False);
            Assert.That(enemy.MaxHealth, Is.EqualTo(health).Within(0.01f));
            Assert.That(enemy.Scale, Is.EqualTo(scale).Within(0.001f));
        }

        // ------------------------------------------------------------ Director

        [UnityTest]
        public IEnumerator Director_ArmsOnlyInAMiniBossChamber()
        {
            var setup = Track(new GameObject("Chamber")).AddComponent<ChamberSetup>();
            setup.Setup(Track(ChamberDifficulty.Create(chambers: 10, boss: 5, miniBoss: 5, miniBossOffset: 2)), null, null);

            setup.Apply(1, 3, 7);
            var normal = NewDirector(Data());
            yield return null; // Start
            Assert.That(normal.State, Is.EqualTo(MiniBossDirector.Phase.Off), "normal Chamber: no mini boss");

            setup.Apply(1, 2, 7);
            var mini = NewDirector(Data());
            yield return null;
            Assert.That(mini.State, Is.EqualTo(MiniBossDirector.Phase.Waiting), "mini boss Chamber: armed");
            setup.Apply(1, 1, 7); // leave a plain Chamber 1 plan for the next tests
        }

        [Test]
        public void Director_DelayWarningRise_OncePerChamber()
        {
            int warned = 0, spawned = 0;
            var director = NewDirector(Data(delay: 2f, warning: 1f));
            director.WarningShown += _ => warned++;
            director.Spawned += _ => spawned++;
            Assert.That(director.Arm(), Is.True);

            director.Tick(1.9f);
            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Waiting));
            Assert.That(_warning.IsShowing, Is.False);
            director.Tick(0.2f);
            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Warning));
            Assert.That(_warning.IsShowing, Is.True);
            Assert.That(warned, Is.EqualTo(1));
            Assert.That(Vector3.Distance(director.Spot, _player.transform.position), Is.EqualTo(director.Data.SpawnDistance).Within(0.01f));
            director.Tick(0.5f);
            Assert.That(_warning.Progress, Is.InRange(0.4f, 0.6f));
            Assert.That(director.Boss, Is.Null, "nothing rises during the warning");
            director.Tick(0.6f);

            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Alive));
            Assert.That(spawned, Is.EqualTo(1));
            var boss = director.Boss;
            Assert.That(boss, Is.Not.Null);
            Assert.That(boss.IsMiniBoss, Is.True);
            Assert.That(boss.MaxHealth, Is.EqualTo(_prefab.BaseMaxHealth * 4f).Within(0.5f));
            Assert.That(Vector3.Distance(boss.transform.position, director.Spot), Is.LessThan(0.01f));
            Assert.That(director.Arm(), Is.False, "one mini boss per Chamber");
        }

        [Test]
        public void Director_WaitsWhileTheKnightIsDead()
        {
            var director = NewDirector(Data(delay: 1f));
            director.Arm();
            _player.TakeDamage(1e6f);
            Assert.That(_player.IsDead, Is.True);
            director.Tick(5f);
            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Waiting));
        }

        [Test]
        public void Spot_AvoidsBlockedGroundAhead()
        {
            Enemy.Walkability = new BlockedAhead();
            var director = NewDirector(Data(delay: 0f));
            director.Arm();
            director.Tick(0.01f);
            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Warning));
            Assert.That(Enemy.Walkability.IsBlocked(director.Spot, 1f), Is.False);
        }

        private sealed class BlockedAhead : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r) => p.z > 3f - r && p.x > -2f - r && p.x < 2f + r;
        }

        // ------------------------------------------------------------ Reward

        [Test]
        public void Kill_BonusWisps_ExactlyOneChestOfTheTier_NoSkillChest()
        {
            _stats.SetSource(_config, new[]
            {
                new StatModifier(StatId.ChestChance, ModifierKind.Flat, 1f),
                new StatModifier(StatId.ChestEliteChance, ModifierKind.Flat, 1f),
            }, 1); // a skill chest on every kill, if the mini boss were rolled
            var dropped = new List<ReliquaryChest>();
            _chests.Dropped += dropped.Add;
            var director = NewDirector(Data(tier: ChestTier.Rare, upgrades: 2));
            int killed = 0;
            director.Killed += _ => killed++;
            var boss = Rise(director);
            int xpBefore = _souls.SpawnedXp;

            boss.TakeDamage(1e9f, Vector3.forward);

            Assert.That(killed, Is.EqualTo(1));
            Assert.That(dropped.Count, Is.EqualTo(1), "exactly one chest");
            Assert.That(dropped[0], Is.SameAs(director.Chest));
            Assert.That(dropped[0].Tier, Is.EqualTo(ChestTier.Rare));
            Assert.That(dropped[0].Choices, Is.EqualTo(3));
            Assert.That(dropped[0].Upgrades, Is.EqualTo(2));
            Assert.That(_souls.SpawnedXp - xpBefore, Is.EqualTo(60 + _prefab.XpValue), "bonus XP + the type's own wisp");
            Assert.That(director.State, Is.EqualTo(MiniBossDirector.Phase.Done));
            Assert.That(director.Boss, Is.Null);
            _chests.Tick(1000f);
            Assert.That(_chests.Active, Has.Member(dropped[0]), "the reward chest never fades");
        }

        [Test]
        public void Kill_ReliquaryHunterOwned_AddsOneUpgrade()
        {
            var hunter = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("reliquary_hunter", "Hunter", "chests", SkillRarity.Common, 5);
            var ladder = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("ladder", "Ladder", "three levels", SkillRarity.Common, 3, new StatModifier(StatId.Damage, ModifierKind.Flat, 1f));
            var skills = NewSkills(hunter, ladder);
            skills.Grant(ladder);
            var director = NewDirector(Data(upgrades: 1), skills);
            Assert.That(director.Upgrades, Is.EqualTo(1), "no hunter: data value");
            skills.Grant(hunter);
            Assert.That(director.Upgrades, Is.EqualTo(2), "hunter L1+: +1");

            Rise(director).TakeDamage(1e9f, Vector3.forward);
            Assert.That(director.Chest, Is.Not.Null);
            Assert.That(director.Chest.Upgrades, Is.EqualTo(2));
        }

        [Test]
        public void Kill_NothingUpgradable_NoChest()
        {
            var small = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("small", "Small", "one level", SkillRarity.Common, 1, new StatModifier(StatId.Damage, ModifierKind.Flat, 1f));
            var skills = NewSkills(small);
            skills.Grant(small); // maxed: a chest would have nothing to raise
            var dropped = new List<ReliquaryChest>();
            _chests.Dropped += dropped.Add;
            var director = NewDirector(Data(), skills);

            Rise(director).TakeDamage(1e9f, Vector3.forward);
            Assert.That(dropped, Is.Empty);
            Assert.That(director.Chest, Is.Null);
        }

        [Test]
        public void Aura_FollowsTheMiniBoss_HidesOnTheKill()
        {
            var auraGo = Track(new GameObject("Aura"));
            var aura = auraGo.AddComponent<MiniBossAura>();
            var ring = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            var disc = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            aura.Setup(ring, disc);
            var director = NewDirector(Data(), null, aura);

            var boss = Rise(director);
            Assert.That(aura.Target, Is.SameAs(boss));
            Assert.That(ring.enabled && disc.enabled, Is.True);
            Assert.That(Vector3.Distance(Flat(aura.transform.position), Flat(boss.transform.position)), Is.LessThan(0.01f));

            boss.TakeDamage(1e9f, Vector3.forward);
            Assert.That(aura.IsShowing, Is.False);
            Assert.That(ring.enabled || disc.enabled, Is.False);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private SkillManager NewSkills(params SkillDefinition[] pool)
        {
            var table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, common = 1f, rare = 0f, epic = 0f, unlocks = pool } }, pity: 0);
            var manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            manager.Setup(table, _stats, null, null, _stats.transform, rngSeed: 3);
            return manager;
        }
    }
}
