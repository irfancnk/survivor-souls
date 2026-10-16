using System;
using System.Collections;
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
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-91 Freeze Arrow: every Nth bolt hit that leaves the foe alive freezes it (kills do not count), a frost hit on a
    /// foe on its refreeze delay keeps the charge for the next hit, the freeze timer runs out and the foe walks on, bosses
    /// freeze for a 35 % share and elites 60 %, a frozen foe does not bite, the freeze burst and rime emit into the shared
    /// system, the shipped asset has the 4-level table and sits in the level 4 tier, the frost sound is voice capped, and a
    /// frost hit + a rime frame allocate nothing.
    /// </summary>
    public sealed class FreezeArrowTests
    {
        private const float Frame = 1f / 60f;
        private const string AssetPath = "Assets/_Project/Data/Skills/Skill_FreezeArrow.asset";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private SkillManager _manager;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(91);
            _player = Track(new GameObject("Player"));
            _player.SetActive(false); // Awake after every component exists (readers grab PlayerStats in Awake)
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            var health = _player.AddComponent<PlayerHealth>();
            _player.AddComponent<SoulCollector>();
            _player.SetActive(true);
            _shooter.enabled = false; // tests hit by hand

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());

            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, _shooter, health, _player.transform, rngSeed: 91);
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

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        private FreezeArrowEffect GrantFrost(int every = 3, float seconds = 1.5f)
        {
            var s = Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure("frost", "frost", "frost", SkillRarity.Rare, 4,
                Add(StatId.FreezeDuration, seconds), Add(StatId.FreezeEvery, every));
            var fx = Track(new GameObject("frost FX"));
            var effect = fx.AddComponent<FreezeArrowEffect>();
            Set(effect, "frost", NewParticles(fx, "Frost"));
            s.SetPresentation(null, effect);
            _manager.Grant(s);
            return (FreezeArrowEffect)_manager.OwnedSkills.Single().Effect;
        }

        private static ParticleSystem NewParticles(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = 500;
            main.startLifetime = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            return ps;
        }

        private static ParticleSystem Frost(FreezeArrowEffect fx) =>
            (ParticleSystem)typeof(FreezeArrowEffect).GetField("frost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx);

        private Enemy SpawnEnemy(Vector3 position, bool boss = false, int xp = 10, Transform target = null)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            if (boss) Set(e, "isBoss", true);
            Set(e, "xpValue", xp);
            e.Spawn(position, target, _ => { }, healthMultiplier: 50f); // tanky: hits never kill it here
            return e;
        }

        /// <summary>The effect's own bolt hit handler (no bolt flight: deterministic, allocation free).</summary>
        private static Action<BoltHit> HitHandler(FreezeArrowEffect fx) =>
            (Action<BoltHit>)Delegate.CreateDelegate(typeof(Action<BoltHit>), fx,
                typeof(FreezeArrowEffect).GetMethod("OnHit", BindingFlags.NonPublic | BindingFlags.Instance)!);

        private static void Tick(Enemy e, float seconds)
        {
            for (float t = 0f; t < seconds - 1e-5f; t += 0.05f) e.TickStatus(0.05f);
        }

        // ------------------------------------------------------------ Counter

        [Test]
        public void EveryThirdHit_Freezes_KillsDoNotCount()
        {
            var frost = GrantFrost(every: 3, seconds: 1.5f);
            var hit = HitHandler(frost);
            var a = SpawnEnemy(new Vector3(-1f, 0f, 4f));
            var b = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var c = SpawnEnemy(new Vector3(1f, 0f, 4f));
            hit(new BoltHit { Enemy = a });
            hit(new BoltHit { Enemy = b, Killed = true }); // a kill: not counted
            hit(new BoltHit { Enemy = b });
            Assert.That(frost.Charge, Is.EqualTo(2));
            Assert.That(a.IsFrozen || b.IsFrozen, Is.False, "hits 1 and 2: no frost yet");
            hit(new BoltHit { Enemy = c });
            Assert.That(c.IsFrozen, Is.True, "the 3rd counted hit is a frost hit");
            Assert.That(c.FreezeLeft, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(frost.Charge, Is.EqualTo(0));
            Assert.That(frost.FreezeCount, Is.EqualTo(1));
        }

        [Test]
        public void FrostHit_OnARefreezeDelayFoe_KeepsTheCharge_ForTheNextFoe()
        {
            var frost = GrantFrost(every: 2, seconds: 1f);
            var hit = HitHandler(frost);
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var b = SpawnEnemy(new Vector3(1f, 0f, 4f));
            hit(new BoltHit { Enemy = a });
            hit(new BoltHit { Enemy = a });
            Assert.That(a.IsFrozen, Is.True);
            hit(new BoltHit { Enemy = a });
            hit(new BoltHit { Enemy = a }); // frost hit on a frozen foe: refused, charge waits
            Assert.That(frost.RefusedCount, Is.EqualTo(1));
            Assert.That(frost.Charge, Is.EqualTo(2));
            hit(new BoltHit { Enemy = b });
            Assert.That(b.IsFrozen, Is.True, "the waiting charge freezes the next foe");
            Assert.That(frost.FreezeCount, Is.EqualTo(2));
        }

        // ------------------------------------------------------------ Timer and refreeze delay

        [Test]
        public void Freeze_RunsOut_ThenTheRefreezeDelay_ThenFreezesAgain()
        {
            var frost = GrantFrost(every: 1, seconds: 1.5f);
            var hit = HitHandler(frost);
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            hit(new BoltHit { Enemy = e });
            Assert.That(e.IsFrozen, Is.True);
            Assert.That(e.FreezeCooldown, Is.EqualTo(1.5f + frost.RefreezeDelay).Within(1e-4f));
            Tick(e, 1.4f);
            Assert.That(e.IsFrozen, Is.True, "1.4 s of 1.5 s");
            Tick(e, 0.2f);
            Assert.That(e.IsFrozen, Is.False, "thawed after 1.5 s");
            hit(new BoltHit { Enemy = e });
            Assert.That(e.IsFrozen, Is.False, "refreeze delay: 1 s after the thaw");
            Tick(e, 1f);
            hit(new BoltHit { Enemy = e });
            Assert.That(e.IsFrozen, Is.True, "free to freeze again");
        }

        [Test]
        public void Bosses_FreezeAShare_Elites_FreezeLess_DeathThaws()
        {
            var frost = GrantFrost(every: 1, seconds: 2f);
            var hit = HitHandler(frost);
            var boss = SpawnEnemy(new Vector3(0f, 0f, 6f), boss: true, xp: 0);
            var elite = SpawnEnemy(new Vector3(2f, 0f, 4f), xp: 30);
            var normal = SpawnEnemy(new Vector3(-2f, 0f, 4f), xp: 10);
            hit(new BoltHit { Enemy = boss });
            hit(new BoltHit { Enemy = elite });
            hit(new BoltHit { Enemy = normal });
            Assert.That(boss.FreezeLeft, Is.EqualTo(2f * frost.BossShare).Within(1e-4f), "boss: 35 %");
            Assert.That(elite.FreezeLeft, Is.EqualTo(2f * frost.EliteShare).Within(1e-4f), "elite: 60 %");
            Assert.That(normal.FreezeLeft, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(boss.IsFrozenSolid, Is.False, "a boss is only rooted (its patterns keep their timing)");
            Assert.That(normal.IsFrozenSolid, Is.True);
            Assert.That(boss.FreezeCooldown, Is.GreaterThan(normal.FreezeCooldown), "bosses wait longer to refreeze");

            normal.TakeDamage(1e6f, Vector3.forward);
            Assert.That(normal.IsFrozen, Is.False, "a dying foe thaws");
        }

        [Test]
        public void FrozenFoe_DoesNotBite_ThawedFoeDoes()
        {
            var receiver = _player.AddComponent<ContactDamageReceiver>();
            var e = SpawnEnemy(_player.transform.position + new Vector3(0.3f, 0f, 0f));
            Assert.That(e.Freeze(0.5f, 0.5f), Is.True);
            receiver.Tick(Frame);
            Assert.That(receiver.IncomingDps, Is.EqualTo(0f), "frozen: no contact damage");
            Tick(e, 0.6f);
            receiver.Tick(Frame);
            Assert.That(receiver.IncomingDps, Is.GreaterThan(0f), "thawed: bites again");
        }

        [UnityTest]
        public IEnumerator FrozenFoe_Stands_ThenWalksOn()
        {
            var target = Track(new GameObject("Target")).transform;
            target.position = new Vector3(0f, 0f, 20f);
            var e = SpawnEnemy(Vector3.zero, target: target);
            Assert.That(e.Freeze(0.3f, 1f), Is.True);
            Vector3 start = e.transform.position;
            for (float t = 0f; t < 0.15f; t += Time.deltaTime) yield return null;
            Assert.That(Vector3.Distance(e.transform.position, start), Is.LessThan(1e-4f), "frozen: no chase step");
            for (float t = 0f; e.IsFrozen && t < 2f; t += Time.deltaTime) yield return null;
            // Walk for a bit of game time, not a frame count: batch-mode frames can be ~0.2 ms long.
            for (float walk = 0f; walk < 0.5f && e.transform.position.z <= start.z + 1e-3f; walk += Time.deltaTime)
                yield return null;
            Assert.That(e.transform.position.z, Is.GreaterThan(start.z + 1e-3f), "walks on after the thaw");
        }

        // ------------------------------------------------------------ Look and sound

        [Test]
        public void Freeze_EmitsTheBurst_RimeOnFrozenFoes_AndRaisesFrozen()
        {
            var frost = GrantFrost(every: 1, seconds: 1.5f);
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            int frozen = 0;
            Action<Enemy> count = _ => frozen++;
            FreezeArrowEffect.Frozen += count;
            try
            {
                HitHandler(frost)(new BoltHit { Enemy = e, Point = e.AimPoint });
                Assert.That(frozen, Is.EqualTo(1));
                var ps = Frost(frost);
                int burst = ps.particleCount;
                Assert.That(burst, Is.GreaterThanOrEqualTo(10), "flash + shards + mist");
                frost.EmitRime(0.5f);
                Assert.That(ps.particleCount, Is.GreaterThan(burst), "rime drifts off the frozen foe");
                Tick(e, 1.6f);
                int after = ps.particleCount;
                frost.EmitRime(0.5f);
                Assert.That(ps.particleCount, Is.EqualTo(after), "no rime once thawed");
            }
            finally
            {
                FreezeArrowEffect.Frozen -= count;
            }
        }

        [Test]
        public void FrostSound_RateLimited_AndVoiceCapped()
        {
            var sounds = Track(new GameObject("Frost Sounds")).AddComponent<FrostSounds>();
            sounds.Setup(new[] { Track(AudioClip.Create("freeze", 44100, 1, 44100, false)) });
            float now = Time.unscaledTime + 100f;
            Assert.That(sounds.Play(false, now), Is.True);
            Assert.That(sounds.Play(false, now + 0.01f), Is.False, "two freezes 10 ms apart: one sound");
            Assert.That(sounds.Play(true, now + 0.1f), Is.True);
            Assert.That(sounds.Play(false, now + 0.2f), Is.True);
            Assert.That(sounds.Play(false, now + 0.3f), Is.False, "3 voices ring: the 4th is dropped");
            Assert.That(sounds.Played, Is.EqualTo(3));
            Assert.That(sounds.Dropped, Is.EqualTo(2));
            Assert.That(sounds.Play(false, now + 1.2f), Is.True, "a voice is free after the clip");
        }

        // ------------------------------------------------------------ Shipped asset

        [Test]
        public void ShippedFreezeArrow_RareFourLevels_EffectPrefab_InTheLevel4Tier()
        {
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetPath);
            Assert.That(skill, Is.Not.Null);
            Assert.That(skill.Id, Is.EqualTo("freeze_arrow"));
            Assert.That(skill.Rarity, Is.EqualTo(SkillRarity.Rare));
            Assert.That(skill.MaxLevel, Is.EqualTo(4));
            Assert.That(skill.Icon, Is.Not.Null);
            Assert.That(skill.EffectPrefab, Is.InstanceOf<FreezeArrowEffect>());
            Assert.That(Frost((FreezeArrowEffect)skill.EffectPrefab), Is.Not.Null, "shared frost system wired");

            var expected = new[] { (1.5f, 3f), (2f, 3f), (2f, 2f), (2.5f, 2f) };
            for (int level = 1; level <= 4; level++)
            {
                var mods = skill.Levels[level - 1].modifiers;
                Assert.That(mods.Single(m => m.stat == StatId.FreezeDuration).value, Is.EqualTo(expected[level - 1].Item1), $"L{level} seconds");
                Assert.That(mods.Single(m => m.stat == StatId.FreezeEvery).value, Is.EqualTo(expected[level - 1].Item2), $"L{level} every");
            }

            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>("Assets/_Project/Data/Skills/SkillProgression_Run.asset");
            var tier = table.Tiers.Single(t => t.unlocks.Contains(skill));
            Assert.That(tier.fromLevel, Is.EqualTo(4));
        }

        // ------------------------------------------------------------ No GC

        [Test]
        public void FrostHit_RimeFrame_AndFrozenTick_AllocateNothing()
        {
            var foes = new Enemy[6];
            for (int i = 0; i < foes.Length; i++) foes[i] = SpawnEnemy(new Vector3(-2f + i * 0.8f, 0f, 4f));
            var frost = GrantFrost(every: 1, seconds: 1.5f);
            var hit = HitHandler(frost);
            hit(new BoltHit { Enemy = foes[0] }); // warm up (JIT)
            frost.EmitRime(0.5f);
            foes[0].TickStatus(Frame);
            var bolt = new BoltHit { Enemy = foes[1] };
            Assert.That(() =>
            {
                hit(bolt);
                frost.EmitRime(0.5f);
                foes[1].TickStatus(Frame);
            }, Is.Not.AllocatingGCMemory());
            Assert.That(frost.FreezeCount, Is.EqualTo(2));
        }
    }
}
