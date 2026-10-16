using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-134.2 (KAN-200) blazing bolts: without Ember Bolts a bolt keeps its normal look; with BurnDps the bolt shows the
    /// hot head, the fire trail material and a longer, wider trail, hotter at level 4 than at level 1; blazing bolts in
    /// flight are listed for the shared spark system and leave the list when they land; the look goes back to normal when the
    /// skill is gone; the spark system emits only for blazing bolts; a shot and a spark frame allocate nothing; the shipped
    /// bolt prefab and Ember Bolts effect prefab carry the new parts.
    /// </summary>
    public sealed class BlazingBoltTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private Material _normalTrail;
        private Material _blazeTrail;
        private Object _source;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(200);
            _player = Track(new GameObject("Player"));
            _player.SetActive(false);
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            _player.AddComponent<PlayerHealth>();
            _player.SetActive(true);
            _shooter.enabled = false; // tests fire by hand

            _normalTrail = Track(new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")));
            _blazeTrail = Track(new Material(_normalTrail));
            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            var trail = template.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _normalTrail;
            trail.time = 0.16f;
            var head = new GameObject("Blaze Head");
            head.transform.SetParent(template.transform, false);
            head.SetActive(false);
            var bolt = template.AddComponent<Bolt>();
            Set(bolt, "trail", trail);
            Set(bolt, "blazeHead", head.transform);
            Set(bolt, "blazeTrailMaterial", _blazeTrail);
            _shooter.SetBoltPrefab(bolt);
            _source = Track(ScriptableObject.CreateInstance<SkillDefinition>());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var e in Enemy.Active.ToArray())
                if (e != null) Object.DestroyImmediate(e.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        private void SetBurn(float dps) =>
            _stats.SetSource(_source, dps > 0f ? new[] { new StatModifier(StatId.BurnDps, ModifierKind.Flat, dps) } : null, dps > 0f ? 1 : 0);

        private Enemy SpawnEnemy(Vector3 position)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { }, healthMultiplier: 50f); // tanky: bolts never kill it
            return e;
        }

        private static Bolt[] InFlight() =>
            Resources.FindObjectsOfTypeAll<Bolt>().Where(b => b.gameObject.scene.IsValid() && b.InFlight).ToArray();

        private static void Fly(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                foreach (var b in InFlight())
                    b.Tick(Frame);
        }

        private static Transform Head(Bolt b) => b.transform.Find("Blaze Head");

        [Test]
        public void NoEmber_NormalLook()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 6f));
            Assert.That(_shooter.BoltHeat, Is.Zero);
            _shooter.Fire(e);
            var b = InFlight().Single();
            Assert.That(b.IsBlazing, Is.False);
            Assert.That(Head(b).gameObject.activeSelf, Is.False);
            Assert.That(b.GetComponent<TrailRenderer>().sharedMaterial, Is.SameAs(_normalTrail));
            Assert.That(Bolt.Blazing, Is.Empty);
        }

        [Test]
        public void EmberLevels_BlazingLook_HotterAtLevelFour()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 20f)); // out of reach in 0.3 s: the bolt stays in flight
            SetBurn(25f); // level 1
            Assert.That(_shooter.BoltHeat, Is.EqualTo(AutoShooter.MinBlazeHeat).Within(1e-4f));
            _shooter.Fire(e);
            var b = InFlight().Single();
            var trail = b.GetComponent<TrailRenderer>();
            Assert.That(b.IsBlazing, Is.True);
            Assert.That(Head(b).gameObject.activeSelf, Is.True, "hot head on");
            Assert.That(trail.sharedMaterial, Is.SameAs(_blazeTrail), "fire trail material");
            Assert.That(trail.time, Is.GreaterThan(0.16f), "longer trail");
            Assert.That(Bolt.Blazing, Is.EquivalentTo(new[] { b }));
            float coolTime = trail.time, coolWidth = trail.widthMultiplier, coolHead = Head(b).localScale.x;

            Fly(1.5f); // lifetime over: back to the pool, off the list
            Assert.That(b.InFlight, Is.False);
            Assert.That(Bolt.Blazing, Is.Empty);

            SetBurn(75f); // level 4
            Assert.That(_shooter.BoltHeat, Is.EqualTo(1f).Within(1e-4f));
            _shooter.Fire(e);
            Assert.That(InFlight().Single(), Is.SameAs(b), "pooled bolt reused");
            Assert.That(b.Heat, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(trail.time, Is.GreaterThan(coolTime));
            Assert.That(trail.widthMultiplier, Is.GreaterThan(coolWidth));
            Assert.That(Head(b).localScale.x, Is.GreaterThan(coolHead), "brighter, bigger head at level 4");

            Fly(1.5f);
            SetBurn(0f); // skill gone (run reset)
            _shooter.Fire(e);
            Assert.That(b.IsBlazing, Is.False);
            Assert.That(Head(b).gameObject.activeSelf, Is.False);
            Assert.That(trail.sharedMaterial, Is.SameAs(_normalTrail), "normal trail back");
            Assert.That(trail.time, Is.EqualTo(0.16f).Within(1e-4f));
            Assert.That(Bolt.Blazing, Is.Empty);
        }

        [Test]
        public void BlazingBolt_LeavesTheList_OnHit()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            SetBurn(40f);
            _shooter.Fire(e);
            Assert.That(Bolt.Blazing.Count, Is.EqualTo(1));
            Fly(0.5f);
            Assert.That(InFlight(), Is.Empty, "bolt landed");
            Assert.That(Bolt.Blazing, Is.Empty);
        }

        [Test]
        public void Sparks_OnlyForBlazingBolts()
        {
            var fx = Track(new GameObject("Ember FX"));
            var sparks = fx.AddComponent<BlazingBoltSparks>();
            var ps = new GameObject("Bolt Sparks").AddComponent<ParticleSystem>();
            ps.transform.SetParent(fx.transform, false);
            var main = ps.main;
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            Set(sparks, "sparks", ps);

            var e = SpawnEnemy(new Vector3(0f, 0f, 20f));
            _shooter.Fire(e);
            for (int i = 0; i < 10; i++) sparks.Emit(Frame);
            Assert.That(sparks.Emitted, Is.Zero, "normal bolts shed no sparks");

            Fly(1.5f);
            SetBurn(75f);
            _shooter.Fire(e);
            for (int i = 0; i < 10; i++) sparks.Emit(Frame);
            Assert.That(sparks.Emitted, Is.GreaterThan(0));
            Assert.That(ps.particleCount, Is.GreaterThan(0));
        }

        [Test]
        public void BlazingShot_AndSparkFrame_AllocateNothing()
        {
            var fx = Track(new GameObject("Ember FX"));
            var sparks = fx.AddComponent<BlazingBoltSparks>();
            var ps = new GameObject("Bolt Sparks").AddComponent<ParticleSystem>();
            ps.transform.SetParent(fx.transform, false);
            var emission = ps.emission;
            emission.enabled = false;
            Set(sparks, "sparks", ps);
            var e = SpawnEnemy(new Vector3(0f, 0f, 20f));
            SetBurn(55f);

            // Warm up: pool bolts exist and wear the blazing look, first-call JIT done.
            for (int i = 0; i < 3; i++)
            {
                _shooter.Fire(e);
                sparks.Emit(Frame);
                Fly(1.5f);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            _shooter.Fire(e);
            sparks.Emit(Frame);
            sparks.Emit(Frame);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(Bolt.Blazing.Count, Is.EqualTo(1));
            Assert.That(bytes, Is.Zero, "same-heat blazing shot + spark frames allocate nothing");
        }

#if UNITY_EDITOR
        [Test]
        public void ShippedPrefabs_CarryTheBlazingParts()
        {
            var bolt = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Combat/Bolt.prefab");
            if (bolt == null)
                bolt = UnityEditor.AssetDatabase.FindAssets("Bolt t:Prefab")
                    .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(g => g != null && g.GetComponent<Bolt>() != null);
            Assert.That(bolt, Is.Not.Null);
            var b = bolt.GetComponent<Bolt>();
            var head = (Transform)typeof(Bolt).GetField("blazeHead", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(b);
            Assert.That(head, Is.Not.Null, "hot head child");
            Assert.That(head.gameObject.activeSelf, Is.False, "off until a blazing shot");
            Assert.That(head.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
            Assert.That(typeof(Bolt).GetField("blazeTrailMaterial", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(b), Is.Not.Null);

            var ember = UnityEditor.AssetDatabase.FindAssets("FX_Skill_EmberBolts t:Prefab")
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Single();
            var sparks = ember.GetComponent<BlazingBoltSparks>();
            Assert.That(sparks, Is.Not.Null);
            Assert.That(typeof(BlazingBoltSparks).GetField("sparks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sparks), Is.Not.Null);
        }
#endif
    }
}
