using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Multi-level skills (KAN-50.1 = KAN-70): a level table row is the total effect at that level (not a sum), the old
    /// stacking fallback without a table, <see cref="SkillManager.Upgrade(SkillDefinition, int)"/> (owned only, +N capped,
    /// event), lookups by id, Restore without events, effects told the level, maxed table skills never offered, and the
    /// level-up card (NEW vs "LV N → N+1", text of the next level, pips up to max level).
    /// </summary>
    public sealed class SkillLevelTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<(SkillDefinition skill, int from, int to)> _changes = new List<(SkillDefinition, int, int)>();
        private GameObject _player;
        private PlayerStats _stats;
        private SkillManager _manager;
        private SkillProgression _table;

        /// <summary>Records the level the manager hands to a behaviour skill's effect.</summary>
        private sealed class LevelProbe : SkillEffect
        {
            public readonly List<int> Levels = new List<int>();
            protected override void Subscribe() { }
            protected override void Unsubscribe() { }
            protected override void OnStacksChanged(int stacks) => Levels.Add(stacks);
        }

        [SetUp]
        public void SetUp()
        {
            _player = Track(new GameObject("Player"));
            _stats = _player.AddComponent<PlayerStats>();
            _table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(_table, _stats, null, null, _player.transform, rngSeed: 50);
            _manager.SkillChanged += (s, a, b) => _changes.Add((s, a, b));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _changes.Clear();
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        private SkillDefinition Stacking(string id, int max, params StatModifier[] mods) =>
            Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure(id, id, id + " text", SkillRarity.Common, max, mods);

        /// <summary>Damage +10 / +25 / +50, level 3 also pierces one enemy.</summary>
        private SkillDefinition Laddered(string id = "ladder") =>
            Stacking(id, 1).SetLevels(
                new SkillLevelData("+10 damage", Add(StatId.Damage, 10f)),
                new SkillLevelData("+25 damage", Add(StatId.Damage, 25f)),
                new SkillLevelData("", Add(StatId.Damage, 50f), Add(StatId.Pierce, 1f)));

        // ------------------------------------------------------------ Data model

        [Test]
        public void LevelTable_EachLevelAppliesItsOwnRow_NotASum()
        {
            var s = Laddered();
            Assert.That(s.HasLevelTable, Is.True);
            Assert.That(s.MaxLevel, Is.EqualTo(3), "table length wins over max stack");

            Assert.That(_manager.Grant(s), Is.True);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(110f).Within(1e-4f));
            Assert.That(_manager.Grant(s), Is.True);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(125f).Within(1e-4f), "row 2 replaces row 1");
            Assert.That(_stats.ModifyInt(StatId.Pierce, 0), Is.Zero);
            Assert.That(_manager.Grant(s), Is.True);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(150f).Within(1e-4f));
            Assert.That(_stats.ModifyInt(StatId.Pierce, 0), Is.EqualTo(1), "a level can add a new stat");
            Assert.That(_manager.Grant(s), Is.False, "max level");
            Assert.That(_manager.GetLevel(s), Is.EqualTo(3));

            Assert.That(s.DescriptionAt(1), Is.EqualTo("+10 damage"));
            Assert.That(s.DescriptionAt(2), Is.EqualTo("+25 damage"));
            Assert.That(s.DescriptionAt(3), Is.EqualTo("ladder text"), "empty row text falls back to the description");
            Assert.That(s.DescriptionAt(9), Is.EqualTo("ladder text"), "clamped to max");
        }

        [Test]
        public void NoTable_OldStacking_ModifiersTimesLevel()
        {
            var s = Stacking("old", 3, Add(StatId.Damage, 10f));
            Assert.That(s.HasLevelTable, Is.False);
            Assert.That(s.MaxLevel, Is.EqualTo(3));
            _manager.Grant(s);
            _manager.Grant(s);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(120f).Within(1e-4f));
            Assert.That(s.DescriptionAt(2), Is.EqualTo("old text"));
        }

        [Test]
        public void ApplyLevel_ZeroRemoves_AboveMaxClamps()
        {
            var s = Laddered();
            s.ApplyLevel(_stats, 7);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(150f).Within(1e-4f));
            s.ApplyLevel(_stats, 0);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(100f).Within(1e-4f));
            Assert.DoesNotThrow(() => s.ApplyLevel(null, 2));
        }

        // ------------------------------------------------------------ Upgrade API

        [Test]
        public void Upgrade_OnlyOwned_ByN_CappedAtMax_RaisesSkillChangedNotGranted()
        {
            var s = Laddered();
            int granted = 0;
            _manager.Granted += (_, __) => granted++;

            Assert.That(_manager.CanUpgrade(s), Is.False, "not owned: a chest cannot give a new skill");
            Assert.That(_manager.Upgrade(s), Is.False);
            Assert.That(_manager.GetLevel(s), Is.Zero);

            _manager.Grant(s);
            Assert.That(_manager.CanUpgrade(s), Is.True);
            Assert.That(_manager.Upgrade(s, 0), Is.False, "zero levels is no upgrade");
            Assert.That(_manager.Upgrade(s, 5), Is.True);
            Assert.That(_manager.GetLevel(s), Is.EqualTo(3), "capped at max level");
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(150f).Within(1e-4f), "stats follow the new level");
            Assert.That(_manager.CanUpgrade(s), Is.False);
            Assert.That(_manager.Upgrade(s), Is.False, "maxed");

            Assert.That(granted, Is.EqualTo(1), "only the level-up pick raises Granted");
            Assert.That(_changes, Is.EqualTo(new[] { (s, 0, 1), (s, 1, 3) }));
        }

        [Test]
        public void Upgrade_ByOne_FromLevelOne_RaisesExactlyOne()
        {
            var s = Laddered();
            _manager.Grant(s);
            Assert.That(_manager.Upgrade(s), Is.True);
            Assert.That(_manager.GetLevel(s), Is.EqualTo(2));
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(125f).Within(1e-4f));
        }

        [Test]
        public void ById_FindGrantUpgradeAndLevel()
        {
            var inTable = Laddered("table_skill");
            var outside = Laddered("outside");
            _table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, unlocks = new[] { inTable } } });

            Assert.That(_manager.FindSkill("table_skill"), Is.SameAs(inTable), "found in the progression table");
            Assert.That(_manager.FindSkill("outside"), Is.Null, "not in the table and not owned");
            Assert.That(_manager.FindSkill("nope"), Is.Null);
            Assert.That(_manager.FindSkill(null), Is.Null);
            Assert.That(_manager.GetLevel("table_skill"), Is.Zero);

            Assert.That(_manager.Grant("table_skill"), Is.True);
            Assert.That(_manager.Upgrade("table_skill"), Is.True);
            Assert.That(_manager.GetLevel("table_skill"), Is.EqualTo(2));
            Assert.That(_manager.Grant("nope"), Is.False);
            Assert.That(_manager.Upgrade("nope"), Is.False);

            _manager.Grant(outside);
            Assert.That(_manager.FindSkill("outside"), Is.SameAs(outside), "owned skills are found too");
        }

        [Test]
        public void Restore_SetsLevelFromTable_Capped_NoSkillChanged()
        {
            var s = Laddered();
            int changed = 0;
            _manager.Changed += () => changed++;
            _manager.Restore(s, 2);
            Assert.That(_manager.GetLevel(s), Is.EqualTo(2));
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(125f).Within(1e-4f));
            _manager.Restore(s, 9);
            Assert.That(_manager.GetLevel(s), Is.EqualTo(3));
            Assert.That(_changes, Is.Empty, "a Chamber carry is not an upgrade");
            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void BehaviourSkill_EffectSpawnsOnce_AndIsToldEachLevel()
        {
            var s = Laddered();
            s.SetPresentation(null, Track(new GameObject("Probe FX")).AddComponent<LevelProbe>());
            _manager.Grant(s);
            _manager.Upgrade(s, 2);
            var probes = _player.GetComponentsInChildren<LevelProbe>();
            Assert.That(probes.Length, Is.EqualTo(1), "one effect per skill");
            Assert.That(probes[0].Levels, Is.EqualTo(new[] { 1, 3 }));
            _manager.ResetRun();
            Assert.That(_manager.GetLevel(s), Is.Zero);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(100f).Within(1e-4f));
        }

        [Test]
        public void Roll_LeavesMaxedTableSkillsOut()
        {
            var a = Laddered("a");
            var b = Laddered("b");
            _table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, unlocks = new[] { a, b } } });
            _manager.Grant(a);
            _manager.Upgrade(a, 2);
            var offer = new List<SkillDefinition>();
            for (int i = 0; i < 20; i++)
            {
                _manager.Roll(3, 2, offer);
                Assert.That(offer, Is.EqualTo(new[] { b }));
            }
        }

        // ------------------------------------------------------------ Card screen

#if UNITY_EDITOR
        [Test]
        public void BadgeText_NewOrLevelStep()
        {
            Assert.That(SkillChoiceScreen.BadgeText(0), Is.EqualTo("NEW"));
            Assert.That(SkillChoiceScreen.BadgeText(1), Is.EqualTo("LV 1 → 2"));
            Assert.That(SkillChoiceScreen.BadgeText(4), Is.EqualTo("LV 4 → 5"));
        }

        [UnityTest]
        public IEnumerator CardScreen_OwnedSkillShowsLevelStepAndNextLevelText_NewSkillShowsNew()
        {
            var owned = Laddered("owned");
            var fresh = Laddered("fresh");
            _table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, unlocks = new[] { owned, fresh } } });
            _manager.Grant(owned);

            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var screen = go.AddComponent<SkillChoiceScreen>();
            screen.Setup(_manager, 0f);
            go.SetActive(true);
            yield return null;

            _manager.ForceNextOffer(owned, fresh);
            screen.Present(3, () => { });
            var root = doc.rootVisualElement;
            var up = root.Q<Button>("skill-card-0");
            var neu = root.Q<Button>("skill-card-1");

            var upBadge = up.Q<Label>(className: "skill-card__badge");
            Assert.That(upBadge.text, Is.EqualTo("LV 1 → 2"));
            Assert.That(upBadge.ClassListContains("skill-card__badge--up"), Is.True);
            Assert.That(upBadge.ClassListContains("hud-hidden"), Is.False, "upgrade badge is shown");
            Assert.That(up.Q<Label>(className: "skill-card__desc").text, Is.EqualTo("+25 damage"), "text of the level this pick gives");

            var newBadge = neu.Q<Label>(className: "skill-card__badge");
            Assert.That(newBadge.text, Is.EqualTo("NEW"));
            Assert.That(newBadge.ClassListContains("skill-card__badge--up"), Is.False);
            Assert.That(neu.Q<Label>(className: "skill-card__desc").text, Is.EqualTo("+10 damage"));

            int Pips(Button card) => card.Q(className: "skill-card__pips").Children().Count(p => p.style.display != DisplayStyle.None);
            Assert.That(Pips(up), Is.EqualTo(3), "one pip per level of the table");
            Assert.That(up.Q(className: "skill-card__pips").Children().Count(p => p.ClassListContains("skill-pip--on")), Is.EqualTo(1));

            screen.Tick(0.05f);
            Assert.That(screen.Choose(0), Is.True);
            Assert.That(_manager.GetLevel(owned), Is.EqualTo(2), "picking the card raises the owned skill by one");
            Assert.That(_changes.Last(), Is.EqualTo((owned, 1, 2)));
        }
#endif
    }
}
