#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UIElements;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Hallowed Shield slot on the HUD skill row (KAN-192): the slot reads the ward effect (<see cref="ISkillCooldown"/>)
    /// every frame. Cooldown shade follows the time to the next ward (full right after a ward ends, empty when it rises),
    /// gold active ring + inlay class while the ward is up, both cleared on a run reset; a level-up glow during a ward
    /// hands the ring back after it ends; no GC per frame.
    /// </summary>
    public sealed class ShieldSlotCooldownTests
    {
        private const string SkillPath = "Assets/_Project/Data/Skills/Skill_TemporaryShield.asset";
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private SkillManager _manager;
        private SkillHud _hud;
        private SkillDefinition _skill;
        private bool _savedVibration;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GamePause.Clear();
            _savedVibration = GameSettings.Vibration;
            GameSettings.Vibration = false;
            Haptics.ResetState();

            var player = Track(new GameObject("Player"));
            var stats = player.AddComponent<PlayerStats>();
            var health = player.AddComponent<PlayerHealth>();
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), stats, null, health, player.transform, rngSeed: 192);
            _skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(SkillPath);
            Assert.That(_skill, Is.Not.Null, SkillPath);

            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            _hud = go.AddComponent<SkillHud>();
            _hud.Setup(_manager);
            go.SetActive(true);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            GamePause.Clear();
            _manager.ResetRun();
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GameSettings.Vibration = _savedVibration;
            Haptics.ResetState();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private ShieldWardEffect Grant()
        {
            Assert.That(_manager.Grant(_skill), Is.True);
            var ward = (ShieldWardEffect)_manager.OwnedSkills.Single().Effect;
            Assert.That(ward, Is.Not.Null, "effect prefab spawned under the player");
            return ward;
        }

        /// <summary>Ward and HUD frames in step (the HUD reads after the effect, like Update order would).</summary>
        private void Run(ShieldWardEffect ward, float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / Frame);
            for (int i = 0; i < frames; i++)
            {
                ward.Tick(Frame);
                _hud.Tick(Frame);
            }
        }

        private VisualElement Glow => _hud.GlowElement(0);

        [Test]
        public void ShieldSlot_ShadeFollowsCooldown_GoldWhileWardUp()
        {
            var ward = Grant();
            Assert.That(ward, Is.InstanceOf<ISkillCooldown>());
            _hud.Tick(0f);
            Assert.That(_hud.CooldownOf(0), Is.GreaterThan(0f).And.LessThan(0.1f), "1 s first delay of a 20 s cooldown: a sliver");
            Assert.That(_hud.IsActiveShown(0), Is.False);

            Run(ward, 1.1f); // first ward rises
            Assert.That(ward.IsActive, Is.True);
            Assert.That(_hud.CooldownOf(0), Is.Zero, "no shade while the ward is up");
            Assert.That(_hud.IsActiveShown(0), Is.True);
            Assert.That(_hud.SlotElement(0).ClassListContains("hud-skill--active"), Is.True);
            Assert.That(Glow.parent.ClassListContains("hud-skill-glow--active"), Is.True);
            Assert.That(Glow.style.opacity.value, Is.GreaterThan(0.5f), "steady gold ring");

            Run(ward, ward.Duration + 0.1f); // ward ends: full cooldown
            Assert.That(ward.IsActive, Is.False);
            Assert.That(_hud.IsActiveShown(0), Is.False);
            Assert.That(_hud.SlotElement(0).ClassListContains("hud-skill--active"), Is.False);
            Assert.That(Glow.style.opacity.value, Is.Zero, "ring off with the ward");
            Assert.That(_hud.CooldownOf(0), Is.GreaterThan(0.95f), "just dropped: shade nearly full");

            Run(ward, ward.Cooldown * 0.5f);
            Assert.That(_hud.CooldownOf(0), Is.EqualTo(0.5f).Within(0.05f), "half way");

            Run(ward, ward.Cooldown * 0.5f + 0.1f); // next ward
            Assert.That(ward.IsActive, Is.True);
            Assert.That(_hud.CooldownOf(0), Is.Zero);
            Assert.That(_hud.IsActiveShown(0), Is.True);
        }

        [Test]
        public void LevelUpGlowDuringWard_HandsRingBack()
        {
            var ward = Grant();
            Run(ward, 1.1f);
            Assert.That(_hud.IsActiveShown(0), Is.True);

            Assert.That(_manager.Upgrade(_skill, 1), Is.True); // level 2 while the ward is up
            ward.Tick(0f);
            _hud.Tick(0f); // starts the glow (no pause open)
            Assert.That(_hud.IsGlowing(0), Is.True);
            _hud.Tick(_hud.GlowTime + 0.05f); // glow ends, the same Tick puts the steady ring back
            Assert.That(_hud.IsGlowing(0), Is.False);
            Assert.That(ward.IsActive, Is.True);
            Assert.That(Glow.style.opacity.value, Is.GreaterThan(0.5f), "steady ring back after the level-up glow");
        }

        [Test]
        public void RunReset_ClearsShadeAndRing()
        {
            var ward = Grant();
            Run(ward, 1.1f);
            Assert.That(_hud.IsActiveShown(0), Is.True);
            _manager.ResetRun();
            _hud.Tick(0f);
            Assert.That(_hud.VisibleSlots, Is.Zero);
            Assert.That(_hud.CooldownOf(0), Is.Zero);
            Assert.That(Glow, Is.Null, "slot hidden");
        }

        [Test]
        public void PollPerFrame_NoGc()
        {
            var ward = Grant();
            Run(ward, 30f); // warm up: one full ward + cooldown cycle (JIT, style paths)
            Assert.That(() => Run(ward, 25f), Is.Not.AllocatingGCMemory());
        }
    }
}
#endif
