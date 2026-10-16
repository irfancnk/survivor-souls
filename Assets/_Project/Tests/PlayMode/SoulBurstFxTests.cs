using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-66 Soul Burst look (logic side; the feel needs the phone): the shipped prefab has six layers inside the
    /// ~40-particle budget, the ground ring ends on the damage radius at every level, chain depth fades the look, same-frame
    /// bursts merge, the live-particle cap falls back to flash + ring, only a deep chain shakes, no GC per burst, and the
    /// burst sound's rate limit.
    /// </summary>
    public sealed class SoulBurstFxTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Skills/FX_Skill_SoulBurst.prefab";

        private readonly List<Object> _objects = new List<Object>();
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private SkillManager _manager;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(66);
            var player = Track(new GameObject("Player"));
            player.SetActive(false);
            _stats = player.AddComponent<PlayerStats>();
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerMover>();
            _shooter = player.AddComponent<AutoShooter>();
            var health = player.AddComponent<PlayerHealth>();
            player.SetActive(true);
            _shooter.enabled = false;
            var table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(table, _stats, _shooter, health, player.transform, rngSeed: 66);
        }

        [TearDown]
        public void TearDown()
        {
            if (_manager != null) _manager.ResetRun();
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        private static KillBurstEffect ShippedPrefab()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            return prefab.GetComponent<KillBurstEffect>();
        }

        /// <summary>Grants Soul Burst with the shipped effect prefab; <paramref name="share"/> 0 = visuals only (no chain).</summary>
        private KillBurstEffect Grant(float share, float extraRadius = 0f)
        {
            var skill = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("burst", "burst", "burst", SkillRarity.Legendary, 4, Add(StatId.KillBurst, share), Add(StatId.BurstRadius, extraRadius));
            skill.SetPresentation(null, ShippedPrefab());
            _manager.Grant(skill);
            return (KillBurstEffect)_manager.OwnedSkills[0].Effect;
        }

        private Enemy SpawnEnemy(Vector3 position)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { });
            return e;
        }

        private static void Kill(Enemy e) => e.TakeDamage(e.Health + 1f, Vector3.zero);

        [Test]
        public void ShippedPrefab_HasSixLayers_WithinTheBudget()
        {
            var fx = ShippedPrefab();
            Assert.That(fx.LayerCount, Is.EqualTo(6), "flash, ring, shell, fire + smoke, shards, rune mark");
            Assert.That(fx.MaxBurstParticles, Is.LessThanOrEqualTo(40), "ticket budget: about 40 particles per burst");
            Assert.That(fx.ParticleCap, Is.EqualTo(400));
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
            {
                Assert.That(ps.emission.enabled, Is.False, $"{ps.name}: Emit only (pooled, no spawn)");
                Assert.That(ps.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World), ps.name);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Assert.That(r.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off), ps.name);
                Assert.That(r.sharedMaterial, Is.Not.Null, ps.name);
            }
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void GroundRing_EndsOnTheDamageRadius(float extra)
        {
            var burst = Grant(0f, extra);
            Assert.That(burst.Radius, Is.EqualTo(2.6f + extra).Within(1e-4f));
            var ring = burst.RingLayer;
            var curve = ring.sizeOverLifetime.size;
            float end = curve.curveMultiplier * curve.curve.Evaluate(1f);
            Assert.That(end, Is.EqualTo(1f).Within(1e-3f), "ring grows to its full start size by the end of its life");

            Kill(SpawnEnemy(new Vector3(0f, 0f, 3f)));
            burst.Tick();
            var particles = new ParticleSystem.Particle[ring.main.maxParticles];
            int n = ring.GetParticles(particles);
            Assert.That(n, Is.EqualTo(1));
            float lineRadius = particles[0].startSize * end * KillBurstEffect.RingTextureRadius;
            Assert.That(lineRadius, Is.EqualTo(burst.Radius).Within(1e-3f), "visible ring = damage radius");
        }

        [Test]
        public void ChainScale_FadesWithDepth_AndStopsAtTheFloor()
        {
            Assert.That(KillBurstEffect.ChainScale(0), Is.EqualTo(1f));
            Assert.That(KillBurstEffect.ChainScale(1), Is.LessThan(1f));
            Assert.That(KillBurstEffect.ChainScale(3), Is.LessThan(KillBurstEffect.ChainScale(1)));
            Assert.That(KillBurstEffect.ChainScale(20), Is.EqualTo(KillBurstEffect.MinChainScale));
        }

        [Test]
        public void Chain_CountsDepth_AndOnlyADeepChainShakes()
        {
            var burst = Grant(5f); // one burst kills the next foe
            var line = new Enemy[7];
            for (int i = 0; i < line.Length; i++) line[i] = SpawnEnemy(new Vector3(i * 2.2f, 0f, 3f));
            Kill(line[0]);
            burst.Tick();
            Assert.That(burst.LastDepth, Is.EqualTo(0), "a kill by the Knight");
            Assert.That(line[1].IsAlive, Is.False);
            for (int k = 1; k <= 3; k++)
            {
                burst.Tick();
                Assert.That(burst.LastDepth, Is.EqualTo(k));
                Assert.That(burst.Shakes, Is.Zero, "no shake for a short chain");
            }
            burst.Tick();
            Assert.That(burst.LastDepth, Is.EqualTo(4));
            Assert.That(burst.Shakes, Is.EqualTo(1), "a deep chain gives one small shake");
            Assert.That(burst.LastBurstParticles, Is.LessThan(burst.MaxBurstParticles), "deep links emit fewer particles");
        }

        [Test]
        public void SameFrameBursts_CloseTogether_MergeIntoOneVisual()
        {
            var burst = Grant(0f);
            Kill(SpawnEnemy(new Vector3(0f, 0f, 3f)));
            Kill(SpawnEnemy(new Vector3(0.5f, 0f, 3.3f)));
            Kill(SpawnEnemy(new Vector3(-0.4f, 0f, 2.8f)));
            Kill(SpawnEnemy(new Vector3(8f, 0f, 3f))); // far: its own burst
            burst.Tick();
            Assert.That(burst.BurstCount, Is.EqualTo(4), "every death still bursts for damage");
            Assert.That(burst.VisualCount, Is.EqualTo(2));
            Assert.That(burst.MergedCount, Is.EqualTo(2));
        }

        [Test]
        public void ParticleCap_FallsBackToFlashAndRing()
        {
            var burst = Grant(0f);
            for (int i = 0; i < 30; i++)
            {
                Kill(SpawnEnemy(new Vector3(i * 4f, 0f, 3f))); // apart: no merging
                burst.Tick();
                Assert.That(burst.LiveBudget, Is.LessThanOrEqualTo(burst.ParticleCap), $"burst {i}");
            }
            Assert.That(burst.DegradedCount, Is.GreaterThan(0), "30 bursts in one moment pass the cap");
            Assert.That(burst.LastBurstParticles, Is.EqualTo(2), "over the cap: core flash + ring only");
        }

        [Test]
        public void Burst_DoesNotAllocate()
        {
            var burst = Grant(0f);
            Kill(SpawnEnemy(new Vector3(0f, 0f, 3f)));
            burst.Tick(); // warm up (JIT, first Emit)
            Kill(SpawnEnemy(new Vector3(6f, 0f, 3f)));
            Assert.That(() => burst.Tick(), Is.Not.AllocatingGCMemory());
            Assert.That(burst.VisualCount, Is.EqualTo(2));
        }

        [Test]
        public void BurstSound_RateLimited_AndQuieterDownTheChain()
        {
            var sounds = Track(new GameObject("Soul Burst Sounds")).AddComponent<SoulBurstSounds>();
            var clip = AudioClip.Create("burst", 4410, 1, 44100, false);
            Track(clip);
            sounds.Setup(new[] { clip });
            Assert.That(sounds.Voices, Is.EqualTo(3), "voice cap");
            Assert.That(sounds.Play(0, 10f), Is.True);
            Assert.That(sounds.Play(1, 10.01f), Is.False, "two bursts in one moment give one sound");
            Assert.That(sounds.Play(1, 10.2f), Is.True);
            Assert.That(sounds.Played, Is.EqualTo(2));
            Assert.That(sounds.Dropped, Is.EqualTo(1));
        }
    }
}
