using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-92 acceptance: the power index (geometric mean of single-target and crowd DPS over the bare Knight, never
    /// below 1); zombie health x P ^ exponent x the Chamber clock ramp, clamped; the Knight's net edge still grows with
    /// power (skills stay strong); danger tier values, colour steps and step colours; shared tier materials (one per step,
    /// SRP Batcher friendly: no property block on a calm tinted zombie, respawn resets); the spawner scales regular spawns
    /// on top of the Chamber multiplier and never a boss; the live scaler follows stat changes and the clock; no GC
    /// allocation per spawn or per frame; 2 minute Chambers; the shipped balance table; the Game scene wiring.
    /// </summary>
    public sealed class HordeScalingTests
    {
        private const int TestRealm = 96;

        private sealed class OpenGround : IWalkability
        {
            public bool IsBlocked(Vector3 position, float radius) => false;
        }

        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            Random.InitState(9201);
            HordeTierMaterials.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            HordeTierMaterials.Clear();
            Enemy.Walkability = null;
            Enemy.LandMaxZ = float.PositiveInfinity;
            GamePause.Clear();
            Time.timeScale = 1f;
            RunProgress.Clear(TestRealm);
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private PlayerStats Stats() => Track(new GameObject("Stats")).AddComponent<PlayerStats>();

        private static void Give(PlayerStats stats, Object owner, StatId id, ModifierKind kind, float value) =>
            stats.SetSource(owner, new[] { new StatModifier(id, kind, value) }, 1);

        private static PowerSnapshot Bare => PowerSnapshot.Measure(null, PowerBase.Default);

        // ------------------------------------------------------------ Power index

        [Test]
        public void PowerIndex_BareKnightIsOne()
        {
            Assert.That(HordeScaling.PowerIndex(Bare, Bare), Is.EqualTo(1f).Within(1e-4f));
            var stats = Stats();
            Assert.That(HordeScaling.PowerIndex(PowerSnapshot.Measure(stats, PowerBase.Default), Bare), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void PowerIndex_IsTheGeometricMeanOfSingleAndCrowd()
        {
            var stats = Stats();
            var owner = Track(ScriptableObject.CreateInstance<HordeScaling>());
            Give(stats, owner, StatId.Damage, ModifierKind.Percent, 1f); // x2 single and crowd
            Assert.That(HordeScaling.PowerIndex(PowerSnapshot.Measure(stats, PowerBase.Default), Bare), Is.EqualTo(2f).Within(1e-3f));

            Give(stats, owner, StatId.Pierce, ModifierKind.Flat, 3f); // crowd x4, single x1
            var s = PowerSnapshot.Measure(stats, PowerBase.Default);
            Assert.That(s.SingleTargetDps / Bare.SingleTargetDps, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(s.CrowdDps / Bare.CrowdDps, Is.EqualTo(4f).Within(1e-3f));
            Assert.That(HordeScaling.PowerIndex(s, Bare), Is.EqualTo(2f).Within(1e-3f), "a crowd build counts at sqrt, not x4");
            Assert.That(HordeScaling.PowerIndex(s, Bare, 0f), Is.EqualTo(1f).Within(1e-3f), "weight 0: single target only");
            Assert.That(HordeScaling.PowerIndex(s, Bare, 1f), Is.EqualTo(4f).Within(1e-3f), "weight 1: crowd only");
            Assert.That(HordeScaling.PowerIndex(s, Bare, 0.25f), Is.EqualTo(Mathf.Pow(4f, 0.25f)).Within(1e-3f));
            var asset = Track(HordeScaling.Create(crowd: 0.25f));
            Assert.That(asset.PowerOf(s, Bare), Is.EqualTo(Mathf.Pow(4f, 0.25f)).Within(1e-3f));
        }

        [Test]
        public void PowerIndex_NeverBelowOne()
        {
            var stats = Stats();
            Give(stats, Track(ScriptableObject.CreateInstance<HordeScaling>()), StatId.Damage, ModifierKind.Percent, -0.5f);
            Assert.That(HordeScaling.PowerIndex(PowerSnapshot.Measure(stats, PowerBase.Default), Bare), Is.EqualTo(1f));
        }

        // ------------------------------------------------------------ Health scaling

        [Test]
        public void HealthScaling_PowerTimeAndClamp()
        {
            var s = Track(HordeScaling.Create(exponent: 0.5f, time: 0.3f, max: 3f));
            Assert.That(s.PowerMultiplier(1f), Is.EqualTo(1f));
            Assert.That(s.PowerMultiplier(4f), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(s.PowerMultiplier(0.2f), Is.EqualTo(1f), "below 1 counts as 1");
            Assert.That(s.TimeMultiplier(0f), Is.EqualTo(1f));
            Assert.That(s.TimeMultiplier(0.5f), Is.EqualTo(1.15f).Within(1e-4f));
            Assert.That(s.TimeMultiplier(1f), Is.EqualTo(1.3f).Within(1e-4f));
            Assert.That(s.TimeMultiplier(5f), Is.EqualTo(1.3f).Within(1e-4f), "clock progress clamps at 1");
            Assert.That(s.DynamicMultiplier(4f, 0.5f), Is.EqualTo(2.3f).Within(1e-4f));
            Assert.That(s.DynamicMultiplier(100f, 1f), Is.EqualTo(3f), "cap");
            Assert.That(s.DynamicMultiplier(1f, 0f), Is.EqualTo(1f));

            var off = Track(HordeScaling.Create(exponent: 0f, time: 0f));
            Assert.That(off.DynamicMultiplier(50f, 1f), Is.EqualTo(1f), "exponent 0 + no ramp = no scaling");
        }

        [Test]
        public void HealthScaling_SkillsStayStrong()
        {
            var s = Track(HordeScaling.Create());
            float lastEdge = 1f;
            for (float p = 1.25f; p <= 30f; p *= 1.25f)
            {
                float edge = p / s.DynamicMultiplier(p, 0f);
                Assert.That(edge, Is.GreaterThan(lastEdge), $"power {p:0.00}: each skill still pays off");
                Assert.That(s.DynamicMultiplier(p, 0f), Is.GreaterThan(1f), $"power {p:0.00}: zombies get tougher");
                lastEdge = edge;
            }
        }

        // ------------------------------------------------------------ Tiers

        [Test]
        public void Tiers_ValueStepsAndColours()
        {
            var colors = new[] { Color.white, Color.green, Color.red };
            var s = Track(HordeScaling.Create(thresholds: new[] { 1f, 2f, 4f }, colors: colors, steps: 2));
            Assert.That(s.TierCount, Is.EqualTo(3));
            Assert.That(s.StepCount, Is.EqualTo(5));
            Assert.That(s.TierValue(0.5f), Is.EqualTo(0f));
            Assert.That(s.TierValue(1f), Is.EqualTo(0f));
            Assert.That(s.TierValue(1.5f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(s.TierValue(2f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(s.TierValue(3f), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(s.TierValue(4f), Is.EqualTo(2f));
            Assert.That(s.TierValue(40f), Is.EqualTo(2f), "the last tier holds");
            Assert.That(s.StepOf(0f), Is.EqualTo(0));
            Assert.That(s.StepOf(0.49f), Is.EqualTo(0), "a step is reached, never rounded up");
            Assert.That(s.StepOf(0.5f), Is.EqualTo(1));
            Assert.That(s.StepOf(2f), Is.EqualTo(4));
            Assert.That(s.StepOf(9f), Is.EqualTo(4));
            Assert.That(s.StepColor(0), Is.EqualTo(Color.white));
            Assert.That(s.StepColor(1), Is.EqualTo(Color.Lerp(Color.white, Color.green, 0.5f)));
            Assert.That(s.StepColor(2), Is.EqualTo(Color.green));
            Assert.That(s.StepColor(4), Is.EqualTo(Color.red));
            Assert.That(s.StepColor(99), Is.EqualTo(Color.red));

            float last = -1f;
            for (float m = 0.5f; m < 10f; m += 0.05f)
            {
                float v = s.TierValue(m);
                Assert.That(v, Is.GreaterThanOrEqualTo(last), "tier never drops as health rises");
                last = v;
            }
        }

        [Test]
        public void ShippedTiers_FiveTiersFromCalm()
        {
#if UNITY_EDITOR
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<HordeScaling>("Assets/_Project/Data/Combat/HordeScaling_Realm1.asset");
            Assert.That(s, Is.Not.Null, "HordeScaling_Realm1 asset (ChamberBuilder)");
            Assert.That(s.TierCount, Is.EqualTo(5));
            Assert.That(s.StepColor(0), Is.EqualTo(Color.white), "calm = the type's own colour");
            Assert.That(s.TierValue(1f), Is.EqualTo(0f));
            Assert.That(s.PowerExponent, Is.InRange(0.3f, 0.8f));
            for (int i = 1; i < s.StepCount; i++)
                Assert.That(s.StepColor(i), Is.Not.EqualTo(s.StepColor(i - 1)), $"step {i} reads different");
#else
            Assert.Ignore("Editor only");
#endif
        }

        // ------------------------------------------------------------ Tier materials

        private Material SourceMaterial(Color color)
        {
            var m = Track(new Material(Shader.Find("Universal Render Pipeline/Simple Lit")));
            m.SetColor("_BaseColor", color);
            return m;
        }

        [Test]
        public void TierMaterials_SharedPerStepAndTinted()
        {
            var s = Track(HordeScaling.Create());
            var src = SourceMaterial(new Color(0.8f, 0.7f, 0.6f));
            var source = new[] { src, src };
            Assert.That(HordeTierMaterials.Get(source, s, 0), Is.SameAs(source), "step 0 = own materials");
            Assert.That(HordeTierMaterials.Get(source, null, 3), Is.SameAs(source), "no scaling = own materials");

            var a = HordeTierMaterials.Get(source, s, 3);
            var b = HordeTierMaterials.Get(source, s, 3);
            Assert.That(a, Is.SameAs(b), "one array per step");
            Assert.That(a[0], Is.SameAs(a[1]), "slots that share a material share the copy");
            Assert.That(a[0], Is.Not.SameAs(src));
            Assert.That(a[0].shader, Is.SameAs(src.shader), "same shader: SRP Batcher compatible");
            Assert.That(HordeTierMaterials.Count, Is.EqualTo(1));
            var expected = src.GetColor("_BaseColor") * s.StepColor(3);
            var got = a[0].GetColor("_BaseColor");
            Assert.That(Vector4.Distance(got, expected), Is.LessThan(1e-3f));

            HordeTierMaterials.Get(source, s, 5);
            Assert.That(HordeTierMaterials.Count, Is.EqualTo(2));
            HordeTierMaterials.Get(source, s, 999);
            Assert.That(HordeTierMaterials.Count, Is.EqualTo(3), "a step past the end clamps to the last one");
            HordeTierMaterials.Get(source, s, s.StepCount - 1);
            Assert.That(HordeTierMaterials.Count, Is.EqualTo(3));
        }

        private Enemy EnemyWithBody(Color tint, bool boss = false, float health = 100f)
        {
            var template = Track(new GameObject("ZombieTemplate"));
            template.SetActive(false);
            var body = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            body.transform.SetParent(template.transform, false);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = SourceMaterial(tint);
            var enemy = template.AddComponent<Enemy>();
            Set(enemy, "body", renderer);
            Set(enemy, "baseTint", tint);
            Set(enemy, "isBoss", boss);
            Set(enemy, "maxHealth", health);
            return enemy;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        private static Renderer Body(Enemy e) => (Renderer)typeof(Enemy).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(e);

        [Test]
        public void Enemy_DangerStepSwapsMaterialsWithoutPropertyBlock()
        {
            var s = Track(HordeScaling.Create());
            var tint = new Color(0.9f, 0.8f, 0.7f);
            var prefab = EnemyWithBody(tint);
            var enemy = Track(Object.Instantiate(prefab.gameObject)).GetComponent<Enemy>();
            var body = Body(enemy);
            var own = body.sharedMaterial;
            var target = Track(new GameObject("Target")).transform;

            enemy.Spawn(Vector3.zero, target, null);
            Assert.That(enemy.DangerStep, Is.EqualTo(0));
            Assert.That(enemy.CalmTint, Is.EqualTo(tint));

            enemy.SetDangerStep(s, 6);
            Assert.That(enemy.DangerStep, Is.EqualTo(6));
            Assert.That(body.sharedMaterial, Is.Not.SameAs(own));
            Assert.That(body.HasPropertyBlock(), Is.False, "calm tinted zombie: no property block, SRP Batcher stays on");
            Assert.That(enemy.CalmTint, Is.EqualTo(tint * s.StepColor(6)));

            enemy.TakeDamage(1f, Vector3.forward);
            enemy.TickStatus(0.01f);
            Assert.That(body.HasPropertyBlock(), Is.True, "hit flash uses a block");
            enemy.TickStatus(1f);
            Assert.That(body.HasPropertyBlock(), Is.False, "flash over: back to the tier material");

            enemy.SetDangerStep(s, 999);
            Assert.That(enemy.DangerStep, Is.EqualTo(s.StepCount - 1), "clamped to the last step");

            enemy.Spawn(Vector3.zero, target, null);
            Assert.That(enemy.DangerStep, Is.EqualTo(0), "a new spawn starts calm");
            Assert.That(body.sharedMaterial, Is.SameAs(own), "own material back");
            Assert.That(enemy.CalmTint, Is.EqualTo(tint));
            Enemy.Active.Remove(enemy);
        }

        // ------------------------------------------------------------ Spawner

        private Camera Camera()
        {
            var cam = Track(new GameObject("Cam")).AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.aspect = 9f / 19.5f;
            var rotation = Quaternion.Euler(55f, 0f, 0f);
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2.2f) - rotation * Vector3.forward * 30f, rotation);
            return cam;
        }

        private EnemySpawner Spawner(Enemy prefab, int maxAlive = 8)
        {
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(0f, maxAlive, new[]
            {
                new EnemyWaveTable.Phase
                {
                    maxAlive = maxAlive, spawnInterval = 0.1f, groupSize = new Vector2Int(4, 4),
                    enemies = new[] { new EnemyWaveTable.EnemyEntry { prefab = prefab, weight = 1f } },
                },
            });
            var spawner = Track(new GameObject("Spawner")).AddComponent<EnemySpawner>();
            spawner.Configure(table, Track(new GameObject("Target")).transform, Camera(), new OpenGround());
            return spawner;
        }

        [Test]
        public void Spawner_ScalesRegularSpawnsOnTopOfTheChamber()
        {
            var s = Track(HordeScaling.Create());
            var prefab = EnemyWithBody(Color.white);
            var spawner = Spawner(prefab);
            spawner.SetDifficulty(1f, 1f, 1.5f);
            spawner.SetHordeScale(s, 2f);
            Assert.That(spawner.SpawnHealthMultiplier, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(spawner.HealthMultiplier, Is.EqualTo(1.5f), "Chamber multiplier unchanged (KAN-46)");

            var e = spawner.SpawnAt(prefab, new Vector3(0f, 0f, -30f));
            Assert.That(e.MaxHealth, Is.EqualTo(300f).Within(1e-3f), "100 x Chamber 1.5 x power 2");
            Assert.That(e.Health, Is.EqualTo(e.MaxHealth));
            Assert.That(e.DangerStep, Is.EqualTo(s.StepOf(s.TierValue(3f))));
            Assert.That(e.DangerStep, Is.GreaterThan(0));
            Assert.That(spawner.DangerStep, Is.EqualTo(e.DangerStep));

            spawner.SetHordeScale(s, 0.2f);
            Assert.That(spawner.DynamicHealthMultiplier, Is.EqualTo(1f), "never below 1");
            spawner.SetDifficulty(1f, 1f, 1f);
            var calm = spawner.SpawnAt(prefab, new Vector3(5f, 0f, -30f));
            Assert.That(calm.DangerStep, Is.EqualTo(0));
            Assert.That(e.MaxHealth, Is.EqualTo(300f).Within(1e-3f), "zombies on the field keep their spawn health");
        }

        [Test]
        public void Spawner_NeverScalesOrTintsABoss()
        {
            var s = Track(HordeScaling.Create());
            var boss = EnemyWithBody(Color.white, boss: true, health: 1000f);
            var spawner = Spawner(EnemyWithBody(Color.white));
            spawner.SetDifficulty(1f, 1f, 2f);
            spawner.SetHordeScale(s, 4f);
            var e = spawner.SpawnAt(boss, new Vector3(0f, 0f, -30f));
            Assert.That(e.MaxHealth, Is.EqualTo(2000f).Within(1e-2f), "boss: Chamber multiplier only");
            Assert.That(e.DangerStep, Is.EqualTo(0));
        }

        [Test]
        public void Spawner_NoScalingIsTheOldBehaviour()
        {
            var prefab = EnemyWithBody(Color.white);
            var spawner = Spawner(prefab);
            spawner.SetDifficulty(1f, 1f, 1.6f);
            var e = spawner.SpawnAt(prefab, new Vector3(0f, 0f, -30f));
            Assert.That(e.MaxHealth, Is.EqualTo(160f).Within(1e-3f));
            Assert.That(e.DangerStep, Is.EqualTo(0));
            Assert.That(spawner.DangerStep, Is.EqualTo(0));
        }

        [Test]
        public void TieredRespawns_DoNotAllocate()
        {
            var s = Track(HordeScaling.Create());
            var prefab = EnemyWithBody(Color.white);
            var spawner = Spawner(prefab, maxAlive: 8);
            spawner.SetHordeScale(s, 3f);
            var live = new List<Enemy>(4);
            for (int i = 0; i < 4; i++) live.Add(spawner.SpawnAt(prefab, new Vector3(i * 2f, 0f, -30f))); // warm: first activation, tier material
            Enemy.Active.Capacity = 64; // list growth is not what this test measures
            var target = Track(new GameObject("Target")).transform;
            int step = spawner.DangerStep;
            Assert.That(step, Is.GreaterThan(0));
            for (int round = 0; round < 3; round++) live[0].SetDangerStep(s, (step + round) % s.StepCount); // make the 3 step materials
            Assert.That(() =>
            {
                for (int round = 0; round < 3; round++)
                    foreach (var e in live)
                    {
                        e.gameObject.SetActive(false); // back in the pool
                        e.Spawn(new Vector3(0f, 0f, -30f), target, null, spawner.SpawnHealthMultiplier);
                        e.SetDangerStep(s, (step + round) % s.StepCount); // calm -> tier -> other tier
                        e.TickStatus(0.02f);
                    }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(HordeTierMaterials.Count, Is.EqualTo(3), "one tier material per step reached");
        }

        // ------------------------------------------------------------ Live scaler

        [Test]
        public void Scaler_FollowsStatsAndTheClock()
        {
            var s = Track(HordeScaling.Create(exponent: 0.5f, time: 0.4f, max: 10f));
            var spawner = Spawner(EnemyWithBody(Color.white));
            var stats = Stats();
            var health = Track(new GameObject("Knight")).AddComponent<SurvivorSouls.Gameplay.Player.PlayerHealth>();
            var timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            timer.Setup(Track(ChamberRules.Create(120f)), health.transform, health);
            var scaler = timer.gameObject.AddComponent<HordeScaler>();
            scaler.Setup(s, spawner, stats, timer);

            Assert.That(scaler.Power, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(scaler.Multiplier, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(spawner.Scaling, Is.SameAs(s));

            Give(stats, s, StatId.Damage, ModifierKind.Percent, 3f); // x4
            scaler.Refresh();
            Assert.That(scaler.Power, Is.EqualTo(4f).Within(1e-3f), "stats.Changed re-measures");
            Assert.That(spawner.DynamicHealthMultiplier, Is.EqualTo(2f).Within(1e-3f), "4 ^ 0.5");

            timer.SetRemaining(60f);
            scaler.Refresh();
            Assert.That(scaler.Progress, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(spawner.DynamicHealthMultiplier, Is.EqualTo(2f * 1.2f).Within(1e-3f));

            scaler.PowerOverride = 16f;
            scaler.ProgressOverride = 1f;
            scaler.Refresh();
            Assert.That(spawner.DynamicHealthMultiplier, Is.EqualTo(4f * 1.4f).Within(1e-3f));
            scaler.PowerOverride = 0f;
            scaler.ProgressOverride = -1f;

            timer.UseRules(Track(ChamberRules.Create(120f, condition: OpenCondition.KillBoss)));
            scaler.Refresh();
            Assert.That(scaler.Progress, Is.EqualTo(0f), "boss Chamber: no clock ramp");

            stats.Clear();
            scaler.Refresh();
            Assert.That(scaler.Power, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(() => scaler.Refresh(), Is.Not.AllocatingGCMemory());
            Give(stats, s, StatId.Pierce, ModifierKind.Flat, 1f);
            Assert.That(() => scaler.Refresh(), Is.Not.AllocatingGCMemory(), "re-measure after a pick allocates nothing");
        }

        // ------------------------------------------------------------ Shipped data

        [Test]
        public void ShippedBalance_SkillsPayOffAndZombiesKeepUp()
        {
#if UNITY_EDITOR
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>("Assets/_Project/Data/Skills/SkillProgression_Run.asset");
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<HordeScaling>("Assets/_Project/Data/Combat/HordeScaling_Realm1.asset");
            Assert.That(table, Is.Not.Null);
            Assert.That(s, Is.Not.Null);
            var curve = SkillPowerCurve.Simulate(table, PowerBase.Default, 200, 20);
            Debug.Log("[HordeScalingTests] " + s.Table(curve));
            var bare = curve.AtLevel(1);
            float last = 1f;
            for (int level = 2; level <= 20; level++)
            {
                float p = s.PowerOf(curve.AtLevel(level), bare);
                Assert.That(p, Is.GreaterThanOrEqualTo(last * 0.98f), $"level {level}: power keeps rising");
                last = p;
            }
            var l20 = curve.AtLevel(20);
            float p20 = s.PowerOf(l20, bare);
            float m20 = s.DynamicMultiplier(p20, 0f);
            Assert.That(m20, Is.GreaterThan(1.5f), "level 20 zombies are clearly tougher");
            Assert.That(p20 / m20, Is.GreaterThan(1.5f), "level 20 Knight still nets a clear edge");
            Assert.That(l20.SingleTargetDps / bare.SingleTargetDps / m20, Is.GreaterThanOrEqualTo(1f), "single-target edge never lost");
            Assert.That(l20.CrowdDps / bare.CrowdDps / m20, Is.GreaterThan(4f), "crowd skills still pay off big");
            Assert.That(s.DynamicMultiplier(p20, 1f), Is.LessThan(s.MaxMultiplier), "the cap is a safety net, not the rule");
#else
            Assert.Ignore("Editor only");
#endif
        }

        [Test]
        public void Scaler_PermanentSoulTreeBonusesAreTheBaseline()
        {
            // KAN-98: bought soul tree levels make the Knight stronger than the horde; only in-run skills raise zombie health.
            var s = Track(HordeScaling.Create(exponent: 0.5f, time: 0f, max: 10f));
            var spawner = Spawner(EnemyWithBody(Color.white));
            var stats = Stats();
            var tree = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            stats.SetSource(tree, new[] { new StatModifier(StatId.Damage, ModifierKind.Percent, 1f) }, 1, permanent: true);
            var health = Track(new GameObject("Knight")).AddComponent<SurvivorSouls.Gameplay.Player.PlayerHealth>();
            var timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            timer.Setup(Track(ChamberRules.Create(120f)), health.transform, health);
            var scaler = timer.gameObject.AddComponent<HordeScaler>();
            scaler.Setup(s, spawner, stats, timer);
            Assert.That(scaler.Power, Is.EqualTo(1f).Within(1e-4f), "x2 damage from the tree: still the run-start Knight");
            Assert.That(spawner.DynamicHealthMultiplier, Is.EqualTo(1f).Within(1e-4f));

            Give(stats, s, StatId.Damage, ModifierKind.Percent, 1f); // in-run: x3 over bare, x1.5 over the baseline
            scaler.Refresh();
            Assert.That(scaler.Power, Is.EqualTo(1.5f).Within(1e-3f));
        }

        // ------------------------------------------------------------ Game scene

        [UnityTest]
        public IEnumerator GameScene_WiresTheHordeScaler()
        {
            RunState.NewRun(TestRealm, 9201);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var scaler = Object.FindAnyObjectByType<HordeScaler>();
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            var timer = Object.FindAnyObjectByType<SurvivalTimer>();
            Assert.That(scaler, Is.Not.Null, "HordeScaler on the Chamber object");
            Assert.That(scaler.Scaling, Is.Not.Null, "scaling asset wired");
            Assert.That(spawner.Scaling, Is.SameAs(scaler.Scaling), "the spawner got it");
            Assert.That(scaler.Power, Is.EqualTo(1f).Within(0.01f), "new run: bare Knight");
            Assert.That(timer.Duration, Is.EqualTo(120f), "2 minute Chamber");

            scaler.PowerOverride = 9f;
            scaler.Refresh();
            Assert.That(spawner.DynamicHealthMultiplier, Is.GreaterThan(1f));
            Assert.That(spawner.DangerStep, Is.GreaterThan(0));

            var empty = SceneManager.CreateScene("HordeScalingEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
