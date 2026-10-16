#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Skills;
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
    /// HUD skill row level pips and level-up glow (KAN-74) on the real HUD: one pip per level up to the max, lit to the
    /// owned level, max class at the cap; a level up of an owned skill glows only after the pause ends, all waiting slots
    /// together with one <see cref="SkillHud.UpgradeShown"/> and one haptic; new skills and Chamber restores do not glow.
    /// KAN-184: tier frame + rarity inlay + level shield, shine / spark glow, cooldown shade.
    /// KAN-194: glow rings in one underlay behind every slot, each on its slot's rect, tier colour = the slot's glow colour.
    /// </summary>
    public sealed class SkillHudTests
    {
        private const string SkillDir = "Assets/_Project/Data/Skills";

        private readonly List<Object> _objects = new List<Object>();
        private SkillManager _manager;
        private SkillHud _hud;
        private int _shown, _shownSlots;
        private bool _savedVibration;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GamePause.Clear();
            _shown = _shownSlots = 0; // NUnit reuses the fixture instance
            _savedVibration = GameSettings.Vibration;
            GameSettings.Vibration = true;
            Haptics.ResetState();

            var player = Track(new GameObject("Player"));
            var stats = player.AddComponent<PlayerStats>();
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), stats, null, null, player.transform, rngSeed: 74);

            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            _hud = go.AddComponent<SkillHud>();
            _hud.Setup(_manager);
            go.SetActive(true); // [Test]s run in one frame: no Update between steps, Tick drives the time
            _hud.UpgradeShown += n => { _shown++; _shownSlots += n; };
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            GamePause.Clear();
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

        /// <summary>Shipped skills with at least <paramref name="minLevels"/> levels, longest table first.</summary>
        private static SkillDefinition[] Shipped(int minLevels = 3) =>
            UnityEditor.AssetDatabase.FindAssets("t:SkillDefinition", new[] { SkillDir })
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s.MaxLevel >= minLevels)
                .OrderByDescending(s => s.MaxLevel).ThenBy(s => s.Id)
                .ToArray();

        private List<VisualElement> Pips(int slot) =>
            _hud.SlotElement(slot).Query(className: "hud-skill__pip").Where(p => p.resolvedStyle.display == DisplayStyle.Flex).ToList();

        private int Lit(int slot) => Pips(slot).Count(p => p.ClassListContains("hud-skill__pip--on"));
        private int[] Flashing(int slot) => Pips(slot).Select((p, i) => (p, i)).Where(t => t.p.ClassListContains("hud-skill__pip--new")).Select(t => t.i).ToArray();

        [UnityTest]
        public IEnumerator Pips_OnePerLevel_LitToOwnedLevel_MaxClassAtCap()
        {
            var skills = Shipped();
            Assert.That(skills.Length, Is.GreaterThanOrEqualTo(2));
            var a = skills[0];
            var b = skills[skills.Length - 1];
            _manager.Grant(a);
            _manager.Grant(b);
            _manager.Grant(a);
            yield return null;

            Assert.That(_hud.VisibleSlots, Is.EqualTo(2));
            Assert.That(Pips(0).Count, Is.EqualTo(a.MaxLevel), a.Id);
            Assert.That(Pips(1).Count, Is.EqualTo(b.MaxLevel), b.Id);
            Assert.That(Lit(0), Is.EqualTo(2));
            Assert.That(Lit(1), Is.EqualTo(1));
            Assert.That(_hud.SlotElement(0).ClassListContains("hud-skill--max"), Is.False);

            Assert.That(_manager.Upgrade(a, a.MaxLevel), Is.True);
            yield return null;
            Assert.That(Lit(0), Is.EqualTo(a.MaxLevel));
            Assert.That(_hud.SlotElement(0).ClassListContains("hud-skill--max"), Is.True);
            Assert.That(_hud.SlotElement(1).ClassListContains("hud-skill--max"), Is.False);
        }

        [UnityTest]
        public IEnumerator Pips_SitUnderTheSlot_InsideItsWidth()
        {
            var a = Shipped(6).FirstOrDefault() ?? Shipped()[0];
            _manager.Grant(a);
            yield return null;
            yield return null;
            var slot = _hud.SlotElement(0).worldBound;
            var pips = Pips(0);
            Assert.That(pips.Count, Is.EqualTo(a.MaxLevel));
            foreach (var p in pips)
            {
                var r = p.worldBound;
                Assert.That(r.yMin, Is.GreaterThanOrEqualTo(slot.yMax), "pip below the rim");
                Assert.That(r.xMin, Is.GreaterThanOrEqualTo(slot.xMin - 4f));
                Assert.That(r.xMax, Is.LessThanOrEqualTo(slot.xMax + 4f));
            }
        }

        [Test]
        public void NewSkill_DoesNotGlow()
        {
            _manager.Grant(Shipped()[0]);
            _hud.Tick(0.05f);
            Assert.That(_hud.PendingGlows, Is.Zero);
            Assert.That(_hud.IsGlowing(0), Is.False);
            Assert.That(_shown, Is.Zero);
        }

        [Test]
        public void Restore_DoesNotGlow_PipsShowLevel()
        {
            var a = Shipped()[0];
            _manager.Restore(a, 3);
            _hud.Tick(0.05f);
            Assert.That(_hud.PendingGlows, Is.Zero);
            Assert.That(_hud.IsGlowing(0), Is.False);
            Assert.That(Lit(0), Is.EqualTo(3));
            Assert.That(Haptics.PulseCount, Is.Zero);
        }

        [Test]
        public void Upgrade_WhilePaused_GlowsAfterRelease_WithOneChimeAndHaptic()
        {
            var a = Shipped()[0];
            _manager.Grant(a);
            GamePause.Acquire(); // the chest card screen
            Assert.That(_manager.Upgrade(a), Is.True);
            _hud.Tick(0.5f);
            Assert.That(_hud.PendingGlows, Is.EqualTo(1));
            Assert.That(_hud.IsGlowing(0), Is.False, "the screen covers the row: no glow yet");
            Assert.That(_shown, Is.Zero);
            Assert.That(Lit(0), Is.EqualTo(2), "pips already show the new level");

            GamePause.Release();
            _hud.Tick(0.01f);
            Assert.That(_hud.IsGlowing(0), Is.True);
            Assert.That(_hud.PendingGlows, Is.Zero);
            Assert.That(_shown, Is.EqualTo(1));
            Assert.That(_shownSlots, Is.EqualTo(1));
            Assert.That(Haptics.PulseCount, Is.EqualTo(1));
            Assert.That(Flashing(0), Is.EqualTo(new[] { 1 }), "only the pip the upgrade added flashes");

            _hud.Tick(_hud.GlowTime);
            Assert.That(_hud.IsGlowing(0), Is.False);
            Assert.That(Flashing(0), Is.Empty);
            Assert.That(Lit(0), Is.EqualTo(2));
            Assert.That(_hud.GlowElement(0).style.opacity.value, Is.Zero);
            Assert.That(_shown, Is.EqualTo(1), "one chime per level-up batch");
        }

        [Test]
        public void Upgrade_WithoutPause_GlowsOnNextTick()
        {
            var a = Shipped()[0];
            _manager.Grant(a);
            _manager.Upgrade(a);
            _hud.Tick(0.01f);
            Assert.That(_hud.IsGlowing(0), Is.True);
            Assert.That(_shown, Is.EqualTo(1));
        }

        [Test]
        public void OwnedSkillPickedOnLevelUp_AlsoGlows()
        {
            var a = Shipped()[0];
            _manager.Grant(a);
            GamePause.Acquire();
            _manager.Grant(a); // level-up card of an owned skill: LV 1 -> 2
            GamePause.Release();
            _hud.Tick(0.01f);
            Assert.That(_hud.IsGlowing(0), Is.True);
            Assert.That(Flashing(0), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void SeveralUpgradesInOnePause_GlowTogether_OneChime()
        {
            var skills = Shipped(4);
            var a = skills[0];
            var b = skills[1];
            _manager.Grant(a);
            _manager.Grant(b);
            GamePause.Acquire();
            _manager.Upgrade(a, 2); // Rare chest
            _manager.Upgrade(b);
            _manager.Upgrade(a); // queued second chest, same skill
            GamePause.Release();
            _hud.Tick(0.01f);

            Assert.That(_hud.IsGlowing(0), Is.True);
            Assert.That(_hud.IsGlowing(1), Is.True);
            Assert.That(_shown, Is.EqualTo(1));
            Assert.That(_shownSlots, Is.EqualTo(2));
            Assert.That(Haptics.PulseCount, Is.EqualTo(1));
            Assert.That(Flashing(0), Is.EqualTo(new[] { 1, 2, 3 }), "LV 1 -> 4 flashes the three new pips");
            Assert.That(Flashing(1), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void VibrationOff_GlowsWithoutHaptic()
        {
            GameSettings.Vibration = false;
            var a = Shipped()[0];
            _manager.Grant(a);
            _manager.Upgrade(a);
            _hud.Tick(0.01f);
            Assert.That(_hud.IsGlowing(0), Is.True);
            Assert.That(_shown, Is.EqualTo(1));
            Assert.That(Haptics.PulseCount, Is.Zero);
        }

        [Test]
        public void ResetRun_DropsWaitingAndRunningGlows()
        {
            var skills = Shipped();
            var a = skills[0];
            _manager.Grant(a);
            _manager.Upgrade(a);
            _hud.Tick(0.01f);
            Assert.That(_hud.IsGlowing(0), Is.True);

            GamePause.Acquire();
            _manager.Upgrade(a);
            _manager.ResetRun();
            GamePause.Release();
            _hud.Tick(0.01f);
            Assert.That(_hud.PendingGlows, Is.Zero);
            Assert.That(_hud.IsGlowing(0), Is.False);
            Assert.That(_hud.VisibleSlots, Is.Zero);
            Assert.That(_shown, Is.EqualTo(1));

            _manager.Grant(skills[1]); // new skill in the old slot: no leftover glow
            _hud.Tick(0.01f);
            Assert.That(_hud.IsGlowing(0), Is.False);
            Assert.That(Flashing(0), Is.Empty);
        }

        [UnityTest]
        public IEnumerator MedievalSlot_TierFrame_Inlay_LevelShield()
        {
            var skills = Shipped();
            var a = skills[0];
            var b = skills.First(s => s.Rarity != a.Rarity);
            _manager.Grant(a);
            _manager.Grant(a);
            _manager.Grant(b);
            yield return null;

            var slot = _hud.SlotElement(0);
            Assert.That(slot.Q(className: "hud-skill__inlay"), Is.Not.Null, "rarity inlay on the frame");
            Assert.That(slot.Q(className: "hud-skill__badge"), Is.Not.Null, "level shield");
            Assert.That(_hud.LevelLabel(0), Is.EqualTo("2"));
            Assert.That(_hud.LevelLabel(1), Is.EqualTo("1"));
            Assert.That(Pips(0).Count, Is.EqualTo(a.MaxLevel), "KAN-74 pips kept");

            string FrameOf(int i) => _hud.SlotElement(i).resolvedStyle.backgroundImage.texture?.name;
            Assert.That(FrameOf(0), Does.StartWith("T_UI_SkillSlot" + a.Rarity), "tier frame");
            Assert.That(FrameOf(1), Does.StartWith("T_UI_SkillSlot" + b.Rarity));
            var inlay = slot.Q(className: "hud-skill__inlay").resolvedStyle;
            Assert.That(inlay.backgroundImage.texture?.name, Does.StartWith("T_UI_SkillInlay" + a.Rarity));
            Color rim = inlay.unityBackgroundImageTintColor;
            Assert.That(rim, Is.Not.EqualTo(Color.white), "inlay tinted by rarity");

            _manager.Upgrade(a, a.MaxLevel);
            yield return null;
            Assert.That(_hud.LevelLabel(0), Is.EqualTo(a.MaxLevel.ToString()));
            Assert.That(slot.Q(className: "hud-skill__inlay").resolvedStyle.unityBackgroundImageTintColor, Is.Not.EqualTo(rim),
                "max level: the inlay burns in the glow colour");
            Assert.That(slot.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(slot.Query<VisualElement>().ToList().All(e => e.pickingMode == PickingMode.Ignore), "never blocks touches");
        }

        [Test]
        public void Glow_SweepsShine_TwinklesSparks_ThenClears()
        {
            var a = Shipped()[0];
            _manager.Grant(a);
            _manager.Upgrade(a);
            _hud.Tick(0.01f);
            var slot = _hud.SlotElement(0);
            var shine = slot.Q(className: "hud-skill__shine");
            var spark = slot.Q(className: "hud-skill__spark");
            _hud.Tick(_hud.GlowTime * 0.25f);
            Assert.That(shine.style.opacity.value, Is.EqualTo(1f), "shine sweeping");
            Assert.That(spark.style.opacity.value, Is.GreaterThan(0.3f), "spark twinkling");
            float x1 = shine.style.translate.value.x.value;
            _hud.Tick(_hud.GlowTime * 0.1f);
            Assert.That(shine.style.translate.value.x.value, Is.GreaterThan(x1), "shine moves left to right");
            _hud.Tick(_hud.GlowTime);
            Assert.That(_hud.IsGlowing(0), Is.False);
            Assert.That(shine.style.opacity.value, Is.Zero);
            Assert.That(slot.Query(className: "hud-skill__spark").ToList().All(e => e.style.opacity.value == 0f));
        }

        [Test]
        public void Cooldown_ShadeFromBottom_Quantised_ClearsOnReset_NoGc()
        {
            var skills = Shipped();
            var a = skills[0];
            _manager.Grant(a);
            Assert.That(_hud.SetCooldown(skills[1], 0.5f), Is.False, "not owned: no slot");
            Assert.That(_hud.SetCooldown(a, 0.5f), Is.True);
            Assert.That(_hud.CooldownOf(0), Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(_hud.SlotElement(0).ClassListContains("hud-skill--cooling"), Is.True);
            var shade = _hud.SlotElement(0).Q(className: "hud-skill__cooldown");
            Assert.That(shade.style.height.value.value, Is.EqualTo(50f).Within(2f));
            Assert.That(_hud.SetCooldown(a, 0.0001f), Is.True);
            Assert.That(_hud.CooldownOf(0), Is.GreaterThan(0f), "a sliver left still shows (ceil)");
            _hud.SetCooldown(a, 0f);
            Assert.That(_hud.CooldownOf(0), Is.Zero);
            Assert.That(_hud.SlotElement(0).ClassListContains("hud-skill--cooling"), Is.False);

            for (float r = 1f; r >= 0f; r -= 0.01f) _hud.SetCooldown(a, r); // warm up
            Assert.That(() => { for (float r = 1f; r >= 0f; r -= 0.01f) _hud.SetCooldown(a, r); }, Is.Not.AllocatingGCMemory());

            _hud.SetCooldown(a, 0.7f);
            _manager.ResetRun();
            _manager.Grant(skills[1]); // new skill in the old slot: no leftover shade
            Assert.That(_hud.CooldownOf(0), Is.Zero);
        }

        [UnityTest]
        public IEnumerator GlowRing_InUnderlay_BehindEverySlot_OnItsSlotRect()
        {
            var skills = Shipped();
            var a = skills[0];
            var b = skills.First(s => s.Rarity != a.Rarity);
            var c = skills.First(s => s != a && s != b);
            _manager.Grant(a);
            _manager.Grant(b);
            _manager.Grant(c);
            yield return null; // layout
            _manager.Upgrade(b); // middle slot: its ring reaches into both gaps
            _hud.Tick(0.01f);
            yield return null;
            _hud.Tick(_hud.GlowTime * 0.1f); // ring at full flare

            var row = _hud.SlotElement(0).parent;
            var glow = _hud.GlowElement(1);
            Assert.That(_hud.IsGlowing(1), Is.True);
            Assert.That(glow.style.opacity.value, Is.GreaterThan(0.5f));
            Assert.That(_hud.SlotElement(1).Contains(glow), Is.False, "the ring is not under its slot");
            var layer = row[0];
            Assert.That(layer.ClassListContains("hud-skills__glows"), Is.True, "underlay = first child of the row");
            Assert.That(layer.Contains(glow), Is.True);
            Assert.That(layer.resolvedStyle.position, Is.EqualTo(Position.Absolute), "out of the flex flow");
            for (int i = 0; i < _hud.VisibleSlots; i++)
            {
                Assert.That(row.IndexOf(_hud.SlotElement(i)), Is.GreaterThan(0), "every slot (frame, shield, pips) draws after every ring");
                // layout, not worldBound: the pick pop scales the newest slot for a moment.
                Rect slot = _hud.SlotElement(i).layout;
                Rect anchor = _hud.GlowElement(i).parent.layout;
                Assert.That(anchor.x + layer.layout.x, Is.EqualTo(slot.x).Within(0.5f), "anchor on slot " + i);
                Assert.That(anchor.y + layer.layout.y, Is.EqualTo(slot.y).Within(0.5f));
                Assert.That(anchor.width, Is.EqualTo(slot.width).Within(0.5f));
                Assert.That(anchor.height, Is.EqualTo(slot.height).Within(0.5f));
                Assert.That(_hud.GlowElement(i).pickingMode, Is.EqualTo(PickingMode.Ignore));
            }
            Assert.That(glow.layout.x, Is.LessThan(-10f), "ring still outside the rim");

            // Tier colour: same as the slot's --slot-glow (the max-level inlay tint), per tier.
            for (int i = 0; i < 2; i++)
            {
                var s = i == 0 ? a : b;
                _manager.Upgrade(s, s.MaxLevel);
            }
            yield return null;
            for (int i = 0; i < 2; i++)
            {
                Color tint = _hud.SlotElement(i).Q(className: "hud-skill__inlay").resolvedStyle.unityBackgroundImageTintColor;
                Assert.That(_hud.GlowElement(i).resolvedStyle.borderTopColor, Is.EqualTo(tint), "glow colour of slot " + i);
            }
            Assert.That(_hud.GlowElement(0).resolvedStyle.borderTopColor, Is.Not.EqualTo(_hud.GlowElement(1).resolvedStyle.borderTopColor));
        }

        [UnityTest]
        public IEnumerator GlowRing_FollowsSlot_WhenRowChanges()
        {
            var skills = Shipped();
            _manager.Grant(skills[0]);
            yield return null;
            _manager.ResetRun();
            _manager.Grant(skills[1]);
            _manager.Grant(skills[0]);
            yield return null;
            var layer = _hud.GetComponent<UIDocument>().rootVisualElement.Q(className: "hud-skills__glows");
            for (int i = 0; i < _hud.VisibleSlots; i++)
                Assert.That(_hud.GlowElement(i).parent.layout.x + layer.layout.x, Is.EqualTo(_hud.SlotElement(i).layout.x).Within(0.5f));
            _manager.ResetRun();
            yield return null;
            Assert.That(_hud.GlowElement(0), Is.Null);
            Assert.That(layer.Children().All(e => e.resolvedStyle.display == DisplayStyle.None), "hidden slots hide their ring anchor");
        }

        [Test]
        public void GlowTick_NoGcAlloc()
        {
            var a = Shipped(5)[0];
            _manager.Grant(a);
            _manager.Upgrade(a);
            for (float t = 0f; t < _hud.GlowTime + 0.1f; t += 0.02f) _hud.Tick(0.02f); // warm up styles and class lists
            _manager.Upgrade(a);
            _hud.Tick(0.01f);
            Assert.That(_hud.IsGlowing(0), Is.True);
            Assert.That(() => { for (int i = 0; i < 20; i++) _hud.Tick(0.02f); }, Is.Not.AllocatingGCMemory());
        }
    }
}
#endif
