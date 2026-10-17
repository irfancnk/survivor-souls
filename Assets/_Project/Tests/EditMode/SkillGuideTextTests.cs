using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SurvivorSouls.Editor.Skills;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.MainMenu;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-64 Skill Guide Book data: the guide lists every skill of the shipped run table (sorted by tier), its per-level
    /// numbers are exactly what <see cref="SkillDefinition.ApplyLevel"/> writes to <see cref="PlayerStats"/> for every skill
    /// and level, every shipped skill has a type and rules text (filled from <see cref="SkillGuideSeed"/>), the stat units
    /// format as expected and "how to find it" reads the table. A new skill in a table shows up with no UI change.
    /// </summary>
    public sealed class SkillGuideTextTests
    {
        private static readonly Regex Number = new Regex(@"[-+]?\d+(\.\d+)?", RegexOptions.CultureInvariant);
        private readonly List<Object> _objects = new List<Object>();

        private static SkillProgression Table()
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
            Assert.That(table, Is.Not.Null, SkillsBuilder.ProgressionPath);
            return table;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        [Test]
        public void Collect_HasEverySkillOfTheRunTable_OnceEach_SortedByTier()
        {
            var table = Table();
            var expected = new HashSet<SkillDefinition>();
            foreach (var tier in table.Tiers)
                foreach (var s in tier.unlocks)
                    if (s != null) expected.Add(s);
            for (int level = 1; level <= 100; level++)
                if (table.GuaranteedAt(level) != null) expected.Add(table.GuaranteedAt(level));

            var list = SkillGuideText.Collect(table);
            Assert.That(list, Is.EquivalentTo(expected));
            Assert.That(list.Count, Is.GreaterThanOrEqualTo(17), "17 skills ship today");
            for (int i = 1; i < list.Count; i++)
                Assert.That(list[i].Rarity, Is.GreaterThanOrEqualTo(list[i - 1].Rarity), $"{list[i].Id} sorted by tier");
        }

        [Test]
        public void EffectsAt_MatchPlayerStats_ForEverySkillAndLevel()
        {
            var effects = new List<SkillGuideEffect>();
            var go = Track(new GameObject("Stats"));
            var stats = go.AddComponent<PlayerStats>();
            foreach (var skill in SkillGuideText.Collect(Table()))
            {
                for (int level = 1; level <= skill.MaxLevel; level++)
                {
                    stats.Clear();
                    skill.ApplyLevel(stats, level);
                    SkillGuideText.EffectsAt(skill, level, effects);
                    var shown = new HashSet<(StatId, ModifierKind)>();
                    foreach (var e in effects)
                    {
                        shown.Add((e.Stat, e.Kind));
                        float real = e.Kind == ModifierKind.Flat ? stats.Flat(e.Stat) : stats.Percent(e.Stat);
                        Assert.That(e.Value, Is.EqualTo(real).Within(1e-4f), $"{skill.Id} L{level} {e.Stat} {e.Kind}");
                        // The text shows the same number (percent shares x100), rounded to 2 decimals at most.
                        var m = Number.Match(e.ValueText);
                        Assert.That(m.Success, Is.True, $"{skill.Id} L{level} '{e.ValueText}' has a number");
                        float text = float.Parse(m.Value, CultureInfo.InvariantCulture);
                        float want = SkillGuideText.DisplayNumber(e.Stat, e.Kind, real);
                        if (e.ValueText.StartsWith("every")) Assert.That(text, Is.EqualTo(Mathf.Round(want)), $"{skill.Id} L{level} {e.ValueText}");
                        else Assert.That(text, Is.EqualTo(want).Within(0.006f), $"{skill.Id} L{level} {e.Label} {e.ValueText}");
                        Assert.That(e.Label, Is.Not.Empty);
                    }
                    // Every stat the level really changes is in the guide.
                    for (int i = 0; i < PlayerStats.StatCount; i++)
                    {
                        var id = (StatId)i;
                        if (!Mathf.Approximately(stats.Flat(id), 0f))
                            Assert.That(shown.Contains((id, ModifierKind.Flat)), Is.True, $"{skill.Id} L{level} shows flat {id}");
                        if (!Mathf.Approximately(stats.Percent(id), 0f))
                            Assert.That(shown.Contains((id, ModifierKind.Percent)), Is.True, $"{skill.Id} L{level} shows percent {id}");
                    }
                }
            }
        }

        [Test]
        public void EffectsAt_MarksTheChangedNumbers()
        {
            var vigor = SkillGuideText.Collect(Table()).Find(s => s.Id == "vigor");
            Assert.That(vigor, Is.Not.Null);
            var effects = new List<SkillGuideEffect>();
            SkillGuideText.EffectsAt(vigor, 1, effects);
            Assert.That(effects.Count, Is.EqualTo(1));
            Assert.That(effects[0].Changed, Is.True, "level 1: everything is new");
            Assert.That(effects[0].ValueText, Is.EqualTo("+80"));
            SkillGuideText.EffectsAt(vigor, 2, effects);
            Assert.That(effects[0].Previous, Is.EqualTo(80f));
            Assert.That(effects[0].Value, Is.EqualTo(150f));
            Assert.That(effects[0].Changed, Is.True);

            var stacking = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("test_stack", "Test", "+10% damage", SkillRarity.Common, 3, new StatModifier(StatId.Damage, ModifierKind.Percent, 0.1f));
            SkillGuideText.EffectsAt(stacking, 3, effects);
            Assert.That(effects[0].Value, Is.EqualTo(0.3f).Within(1e-5f), "no table: modifiers x level");
            Assert.That(effects[0].ValueText, Is.EqualTo("+30%"));
        }

        [Test]
        public void EveryShippedSkill_HasATypeAndRulesText_AndASeedEntry()
        {
            foreach (var skill in SkillGuideText.Collect(Table()))
            {
                Assert.That(SkillGuideSeed.KindOf(skill.Id), Is.Not.EqualTo(SkillKind.Unset), $"{skill.Id} has a seed entry");
                Assert.That(skill.Kind, Is.Not.EqualTo(SkillKind.Unset), $"{skill.Id} kind (rebuild the Game scene to fill it)");
                Assert.That(skill.GuideText, Is.Not.Empty, $"{skill.Id} guide text");
                Assert.That(SkillGuideText.KindName(skill.Kind), Is.Not.Empty);
            }
        }

        [Test]
        public void FormatValue_Units()
        {
            Assert.That(SkillGuideText.FormatValue(StatId.Damage, ModifierKind.Percent, 0.2f), Is.EqualTo("+20%"));
            Assert.That(SkillGuideText.FormatValue(StatId.CritChance, ModifierKind.Flat, 0.1f), Is.EqualTo("+10%"));
            Assert.That(SkillGuideText.FormatValue(StatId.ChestChance, ModifierKind.Flat, 0.015f), Is.EqualTo("+1.5%"));
            Assert.That(SkillGuideText.FormatValue(StatId.MaxHealth, ModifierKind.Flat, 80f), Is.EqualTo("+80"));
            Assert.That(SkillGuideText.FormatValue(StatId.Pierce, ModifierKind.Flat, 2f), Is.EqualTo("+2"));
            Assert.That(SkillGuideText.FormatValue(StatId.WardCooldown, ModifierKind.Flat, -3f), Is.EqualTo("-3 s"));
            Assert.That(SkillGuideText.FormatValue(StatId.BurstRadius, ModifierKind.Flat, 0.6f), Is.EqualTo("+0.6 m"));
            Assert.That(SkillGuideText.FormatValue(StatId.BurnDps, ModifierKind.Flat, 40f), Is.EqualTo("+40/s"));
            Assert.That(SkillGuideText.FormatValue(StatId.FreezeEvery, ModifierKind.Flat, 3f), Is.EqualTo("every 3rd"));
            Assert.That(SkillGuideText.FormatValue(StatId.FreezeEvery, ModifierKind.Flat, 2f), Is.EqualTo("every 2nd"));
            Assert.That(SkillGuideText.FormatValue(StatId.RareChestEvery, ModifierKind.Flat, 5f), Is.EqualTo("every 5th"));
            Assert.That(SkillGuideText.FormatValue(StatId.PierceFalloff, ModifierKind.Flat, -0.18f), Is.EqualTo("-18%"));
            Assert.That(SkillGuideText.LabelOf(StatId.BounceFalloff), Is.EqualTo("Bounce loss"), "label table covers the last stat");
        }

        [Test]
        public void HowToGet_ReadsTheTable()
        {
            var table = Table();
            var skills = SkillGuideText.Collect(table);
            var bolts = skills.Find(s => s.Id == "sharpened_bolts");
            var hunter = skills.Find(s => s.Id == "reliquary_hunter");
            var doubleShot = skills.Find(s => s.Id == "double_shot");
            Assert.That(string.Join(" ", SkillGuideText.HowToGet(bolts, table)),
                Does.Contain($"from level {SkillGuideText.UnlockLevel(table, bolts)}"));
            Assert.That(string.Join(" ", SkillGuideText.HowToGet(hunter, table)), Does.Contain("Offered only while"));
            Assert.That(string.Join(" ", SkillGuideText.HowToGet(doubleShot, table)), Does.Contain("rarely"), "upgrade weight 0.35");
            for (int level = 1; level <= 100; level++)
            {
                var g = table.GuaranteedAt(level);
                if (g != null)
                    Assert.That(SkillGuideText.HowToGet(g, table), Does.Contain($"Always offered at level {level}."), g.Id);
            }
        }

        [Test]
        public void NewSkillInATable_ShowsUp()
        {
            var a = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("test_a", "Alpha", "A", SkillRarity.Epic, 2, new StatModifier(StatId.Damage, ModifierKind.Percent, 0.1f));
            var b = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("test_b", "Beta", "B", SkillRarity.Common, 1, new StatModifier(StatId.MoveSpeed, ModifierKind.Percent, 0.1f));
            var table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, unlocks = new[] { a, b } } });
            Assert.That(SkillGuideText.Collect(table), Is.EqualTo(new[] { b, a }), "Common before Epic");
            Assert.That(SkillGuideText.KindName(a.Kind), Is.Empty, "unsorted skill: no type tag");
        }
    }
}
