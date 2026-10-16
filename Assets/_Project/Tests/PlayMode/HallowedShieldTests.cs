#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Hallowed Shield (KAN-93, Temporary Shield): the shipped skill with its effect prefab. First ward 1 s after the pick,
    /// ward length and cooldown per level (3 / 20, 4 / 17, 5 / 14 s), cooldown counted from the ward's end, no damage at all
    /// while up (shield pool untouched), warning flicker, bubble on / off, events, death, reset, the boss-intro flag kept,
    /// and no GC per frame.
    /// </summary>
    public sealed class HallowedShieldTests
    {
        private const string SkillPath = "Assets/_Project/Data/Skills/Skill_TemporaryShield.asset";
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private PlayerHealth _health;
        private SkillManager _manager;
        private SkillDefinition _skill;
        private int _raised, _ended, _blocked;

        [SetUp]
        public void SetUp()
        {
            _player = Track(new GameObject("Player"));
            _stats = _player.AddComponent<PlayerStats>();
            _health = _player.AddComponent<PlayerHealth>();
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, null, _health, _player.transform, rngSeed: 93);
            _skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(SkillPath);
            Assert.That(_skill, Is.Not.Null, SkillPath);
            _raised = _ended = _blocked = 0;
            ShieldWardEffect.WardRaised += OnRaised;
            ShieldWardEffect.WardEnded += OnEnded;
            ShieldWardEffect.WardBlocked += OnBlocked;
        }

        [TearDown]
        public void TearDown()
        {
            ShieldWardEffect.WardRaised -= OnRaised;
            ShieldWardEffect.WardEnded -= OnEnded;
            ShieldWardEffect.WardBlocked -= OnBlocked;
            _manager.ResetRun();
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private void OnRaised() => _raised++;
        private void OnEnded() => _ended++;
        private void OnBlocked() => _blocked++;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private ShieldWardEffect Grant(int level = 1)
        {
            Assert.That(_manager.Grant(_skill), Is.True);
            if (level > 1) Assert.That(_manager.Upgrade(_skill, level - 1), Is.True);
            var ward = (ShieldWardEffect)_manager.OwnedSkills.Single().Effect;
            Assert.That(ward, Is.Not.Null, "effect prefab spawned under the player");
            return ward;
        }

        private static void Run(ShieldWardEffect ward, float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / Frame);
            for (int i = 0; i < frames; i++) ward.Tick(Frame);
        }

        private static float Alpha(ShieldWardEffect ward)
        {
            var mpb = new MaterialPropertyBlock();
            ward.Bubble.GetPropertyBlock(mpb);
            return mpb.GetFloat("_Alpha");
        }

        [Test]
        public void ShippedSkill_IsEpic_ThreeLevels_InTheLevelSixTier()
        {
            Assert.That(_skill.Id, Is.EqualTo("temporary_shield"));
            Assert.That(_skill.DisplayName, Is.EqualTo("Hallowed Shield"));
            Assert.That(_skill.Rarity, Is.EqualTo(SkillRarity.Epic));
            Assert.That(_skill.MaxLevel, Is.EqualTo(3));
            Assert.That(_skill.Icon, Is.Not.Null);
            Assert.That(_skill.EffectPrefab, Is.InstanceOf<ShieldWardEffect>());
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>("Assets/_Project/Data/Skills/SkillProgression_Run.asset");
            var tier = table.Tiers.Single(t => t.unlocks.Contains(_skill));
            Assert.That(tier.fromLevel, Is.EqualTo(6));
            Assert.That(_skill.Weight, Is.EqualTo(0.6f).Within(1e-4f), "defensive card offered less often");
        }

        [TestCase(1, 3f, 20f)]
        [TestCase(2, 4f, 17f)]
        [TestCase(3, 5f, 14f)]
        public void LevelValues_DurationAndCooldown(int level, float duration, float cooldown)
        {
            var ward = Grant(level);
            Assert.That(ward.Duration, Is.EqualTo(duration).Within(1e-4f));
            Assert.That(ward.Cooldown, Is.EqualTo(cooldown).Within(1e-4f));
        }

        [Test]
        public void FirstWard_OneSecondAfterThePick_LastsItsDuration_NextAfterTheCooldown()
        {
            var ward = Grant();
            Run(ward, 0.9f);
            Assert.That(ward.IsActive, Is.False, "not yet");
            Run(ward, 0.2f);
            Assert.That(ward.IsActive, Is.True, "first ward 1 s after the pick");
            Assert.That(_health.Warded, Is.True);
            Assert.That(_raised, Is.EqualTo(1));

            Run(ward, 2.9f);
            Assert.That(ward.IsActive, Is.True, "still up at 2.9 s");
            Run(ward, 0.2f);
            Assert.That(ward.IsActive, Is.False, "down after 3 s");
            Assert.That(_health.Warded, Is.False);
            Assert.That(_ended, Is.EqualTo(1));
            Assert.That(ward.CooldownLeft, Is.EqualTo(20f).Within(0.25f), "cooldown counts from the end");

            Run(ward, 19.7f);
            Assert.That(ward.IsActive, Is.False);
            Run(ward, 0.4f);
            Assert.That(ward.IsActive, Is.True, "second ward 20 s after the first ended");
            Assert.That(ward.RaisedCount, Is.EqualTo(2));
        }

        [Test]
        public void UpgradeWhileCooling_NextCooldownIsShorter()
        {
            var ward = Grant();
            Run(ward, 1.1f + 3.1f);
            Assert.That(ward.IsActive, Is.False);
            _manager.Upgrade(_skill, 2);
            Assert.That(ward.Cooldown, Is.EqualTo(14f).Within(1e-4f));
            Run(ward, 20f);
            Assert.That(ward.IsActive, Is.True, "the running 20 s cooldown is kept");
            while (ward.IsActive) ward.Tick(Frame);
            Assert.That(ward.CooldownLeft, Is.EqualTo(14f).Within(2f * Frame), "next cooldown is the level 3 one");
            int frames = 0;
            while (!ward.IsActive) { ward.Tick(Frame); frames++; }
            while (ward.IsActive) { ward.Tick(Frame); frames++; }
            Assert.That(frames * Frame, Is.EqualTo(14f + 5f).Within(3f * Frame), "level 3: 14 s down, 5 s up");
        }

        [Test]
        public void WardUp_BlocksAllDamage_ShieldPoolUntouched_DownAgain_DamageLands()
        {
            var ward = Grant();
            _health.SetShield(50f);
            float max = _health.Max;
            int hurt = 0;
            _health.OnHurt += () => hurt++;
            Run(ward, 1.1f);
            Assert.That(ward.IsActive, Is.True);

            Assert.That(_health.TakeDamage(500f), Is.Zero);
            Assert.That(_health.TakeDamage(9999f), Is.Zero, "even a lethal hit");
            Assert.That(_health.Current, Is.EqualTo(max));
            Assert.That(_health.Shield, Is.EqualTo(50f), "the shield pool is not used while warded");
            Assert.That(hurt, Is.Zero, "no hurt pulse");
            Assert.That(ward.BlockedDamage, Is.EqualTo(500f + 9999f).Within(1e-2f));
            Assert.That(_blocked, Is.EqualTo(1), "block feedback once per 0.2 s (same frame here)");

            Run(ward, 3.1f);
            Assert.That(ward.IsActive, Is.False);
            Assert.That(_health.TakeDamage(80f), Is.EqualTo(30f), "shield 50 first, then health");
            Assert.That(_health.Current, Is.EqualTo(max - 30f));
        }

        [Test]
        public void WardedDiscreteHit_RaisesNoOnHit_NoHurtFeedback()
        {
            var ward = Grant();
            int hits = 0;
            _health.OnHit += _ => hits++;
            Run(ward, 1.1f);
            Assert.That(ward.IsActive, Is.True);
            Assert.That(_health.TakeDamage(120f, _player.transform.position + Vector3.right * 5f), Is.Zero);
            Assert.That(hits, Is.Zero, "KAN-187 hit arc / hit-stop / shake stay off for a blocked hit");
            Assert.That(ward.BlockedDamage, Is.EqualTo(120f).Within(1e-3f));
            Run(ward, 3.1f);
            Assert.That(_health.TakeDamage(120f, _player.transform.position + Vector3.right * 5f), Is.EqualTo(120f));
            Assert.That(hits, Is.EqualTo(1), "after the ward a discrete hit raises OnHit again");
        }

        [Test]
        public void Bubble_ShownWhileUp_FlickersBeforeTheEnd_HiddenAfterTheFade()
        {
            var ward = Grant();
            Assert.That(ward.Bubble, Is.Not.Null);
            Assert.That(ward.Bubble.enabled, Is.False, "no draw call before the first ward");
            Run(ward, 1.5f);
            Assert.That(ward.Bubble.enabled, Is.True);
            Assert.That(ward.Warning, Is.False);
            Assert.That(Alpha(ward), Is.EqualTo(1f).Within(1e-3f), "full after the pop");

            Run(ward, 1.7f); // 0.8 s left: in the 0.9 s warning
            Assert.That(ward.Warning, Is.True);
            var seen = new HashSet<float>();
            for (int i = 0; i < 30; i++)
            {
                ward.Tick(Frame);
                if (ward.IsActive) seen.Add(Mathf.Round(Alpha(ward) * 100f) / 100f);
            }
            Assert.That(seen, Has.Member(1f), "flicker: full");
            Assert.That(seen, Has.Member(0.3f), "flicker: dim");

            Run(ward, 1f);
            Assert.That(ward.IsActive, Is.False);
            Assert.That(ward.Bubble.enabled, Is.False, "renderer off after the fade-out");
        }

        [Test]
        public void Death_DropsTheWardSilently_NoNewWard()
        {
            var ward = Grant();
            Run(ward, 4.5f); // first ward over
            Assert.That(_health.TakeDamage(_health.Max + 10f), Is.GreaterThan(0f));
            Assert.That(_health.IsDead, Is.True);
            Run(ward, 30f);
            Assert.That(ward.RaisedCount, Is.EqualTo(1), "a dead Knight gets no ward");
            Assert.That(_health.Warded, Is.False);
        }

        [UnityTest]
        public IEnumerator ResetRun_RemovesTheEffect_AndClearsTheWard_BossIntroFlagKept()
        {
            var ward = Grant();
            Run(ward, 1.1f);
            Assert.That(_health.Warded, Is.True);
            _health.Invulnerable = true; // boss intro (KAN-62) holds its own flag
            _manager.ResetRun();
            yield return null; // Destroy runs at the end of the frame
            Assert.That(ward == null, Is.True, "effect destroyed");
            Assert.That(_health.Warded, Is.False);
            Assert.That(_health.Invulnerable, Is.True, "the skill never clears the boss-intro flag");
            _health.Invulnerable = false;
        }

        [Test]
        public void ActiveWard_BlockedHits_NoGcPerFrame()
        {
            var ward = Grant();
            Run(ward, 1.5f);
            Assert.That(ward.IsActive, Is.True);
            _health.TakeDamage(10f); // warm-up: first block + flash
            ward.Tick(Frame);
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    _health.TakeDamage(10f);
                    ward.Tick(Frame);
                }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
#endif
