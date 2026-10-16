using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-201 (KAN-134.3) Ember Bolts sounds, logic side (loudness needs the phone): an ignite plays a whoomp (rate limited,
    /// 3 voices), the hot shot layer plays only while Ember Bolts is owned, crackle loops follow the burning count (capped at
    /// 3, fade out on pause), the Ignited event and the crossbow shot reach the sounds, and no hit, shot or frame allocates.
    /// </summary>
    public sealed class EmberSoundsTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private SkillManager _manager;
        private EmberSounds _sounds;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(201);
            var player = Track(new GameObject("Player"));
            player.SetActive(false); // Awake after every component exists (readers grab PlayerStats in Awake)
            _stats = player.AddComponent<PlayerStats>();
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerMover>();
            _shooter = player.AddComponent<AutoShooter>();
            var health = player.AddComponent<PlayerHealth>();
            player.AddComponent<SoulCollector>();
            player.SetActive(true);
            _shooter.enabled = false; // tests fire by hand
            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, _shooter, health, player.transform, rngSeed: 201);

            _sounds = Track(new GameObject("Ember Sounds")).AddComponent<EmberSounds>();
            _sounds.Setup(_shooter, _stats, new[] { Clip("ignite", 1f) }, new[] { Clip("crackle", 2f) }, new[] { Clip("hot", 0.3f) });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private AudioClip Clip(string name, float seconds) => Track(AudioClip.Create(name, (int)(44100 * seconds), 1, 44100, false));

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        private BurnOnHitEffect GrantEmber(float dps)
        {
            var s = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("ember", "ember", "ember", SkillRarity.Legendary, 4, new[] { Add(StatId.BurnDps, dps) });
            var effect = Track(new GameObject("ember FX")).AddComponent<BurnOnHitEffect>();
            s.SetPresentation(null, effect);
            _manager.Grant(s);
            return (BurnOnHitEffect)_manager.OwnedSkills.Single().Effect;
        }

        private Enemy SpawnEnemy(Vector3 position)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { }, healthMultiplier: 50f); // tanky: never dies here
            return e;
        }

        private static Action<BoltHit> HitHandler(BurnOnHitEffect fx) =>
            (Action<BoltHit>)Delegate.CreateDelegate(typeof(Action<BoltHit>), fx,
                typeof(BurnOnHitEffect).GetMethod("OnHit", BindingFlags.NonPublic | BindingFlags.Instance)!);

        [Test]
        public void Ignite_RateLimited_AndVoiceCapped()
        {
            Assert.That(_sounds.IgniteVoices, Is.EqualTo(3), "voice cap");
            Assert.That(_sounds.PlayIgnite(0f, 10f), Is.True);
            Assert.That(_sounds.PlayIgnite(1f, 10.02f), Is.False, "two ignites in one moment give one whoomp");
            Assert.That(_sounds.PlayIgnite(1f, 10.2f), Is.True);
            Assert.That(_sounds.PlayIgnite(0.5f, 10.4f), Is.True);
            Assert.That(_sounds.PlayIgnite(0.5f, 10.6f), Is.False, "3 voices still ring (1 s clip): the 4th ignite is dropped");
            Assert.That(_sounds.PlayIgnite(0.5f, 11.2f), Is.True, "the first voice is free again");
            Assert.That(_sounds.IgnitesPlayed, Is.EqualTo(4));
            Assert.That(_sounds.IgnitesDropped, Is.EqualTo(2));
        }

        [Test]
        public void IgnitedEvent_PlaysTheWhoomp()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var ember = GrantEmber(30f);
            HitHandler(ember)(new BoltHit { Enemy = e, Point = e.AimPoint, Direction = Vector3.forward, Damage = 1f });
            Assert.That(ember.IgniteCount, Is.EqualTo(1));
            Assert.That(_sounds.IgnitesPlayed, Is.EqualTo(1), "BurnOnHitEffect.Ignited reaches the sound");
        }

        [Test]
        public void HotShot_OnlyWhileEmberIsOwned_AndOnTheCrossbowShot()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            Assert.That(_sounds.PlayHotShot(5f), Is.False, "no Ember Bolts: plain crossbow shot");
            _shooter.Fire(e);
            Assert.That(_sounds.HotShotsPlayed, Is.EqualTo(0));

            GrantEmber(30f);
            Assert.That(_sounds.PlayHotShot(6f), Is.True);
            Assert.That(_sounds.PlayHotShot(6.01f), Is.False, "rate limit");
            _shooter.Fire(e);
            Assert.That(_sounds.HotShotsPlayed, Is.EqualTo(2), "AutoShooter.Fired reaches the hot layer");
        }

        [Test]
        public void Heat_ScalesFromLevelOneToFourBurn()
        {
            Assert.That(_sounds.Heat(25f), Is.EqualTo(0f));
            Assert.That(_sounds.Heat(75f), Is.EqualTo(1f));
            Assert.That(_sounds.Heat(50f), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Crackle_FollowsTheBurningCount_CappedAt3_FadesOnPause()
        {
            Assert.That(_sounds.CrackleVoices, Is.EqualTo(3));
            _sounds.Tick(0, false, 0.1f);
            Assert.That(_sounds.CrackleLoops, Is.EqualTo(0), "nothing burns: silence");
            _sounds.Tick(2, false, 0.1f);
            Assert.That(_sounds.CrackleLoops, Is.EqualTo(2), "one loop per burning foe");
            _sounds.Tick(12, false, 0.1f);
            Assert.That(_sounds.CrackleLoops, Is.EqualTo(3), "a horde on fire: 3 loops at most");
            for (int i = 0; i < 20; i++) _sounds.Tick(12, true, 0.1f);
            Assert.That(_sounds.CrackleLoops, Is.EqualTo(0), "paused (level-up screen): crackle fades out");
            _sounds.Tick(1, false, 0.1f);
            for (int i = 0; i < 20; i++) _sounds.Tick(0, false, 0.1f);
            Assert.That(_sounds.CrackleLoops, Is.EqualTo(0), "fires out: the loop fades and stops");
        }

        [Test]
        public void IgniteShotAndCrackleFrame_AllocateNothing()
        {
            GrantEmber(30f);
            _sounds.PlayIgnite(0.5f, 1f); // warm up (JIT)
            _sounds.PlayHotShot(1f);
            _sounds.Tick(2, false, 0.016f);
            float now = 100f;
            Assert.That(() => { now += 2f; _sounds.PlayIgnite(0.5f, now); }, Is.Not.AllocatingGCMemory());
            Assert.That(() => { now += 2f; _sounds.PlayHotShot(now); }, Is.Not.AllocatingGCMemory());
            Assert.That(() => _sounds.Tick(3, false, 0.016f), Is.Not.AllocatingGCMemory());
        }
    }
}
