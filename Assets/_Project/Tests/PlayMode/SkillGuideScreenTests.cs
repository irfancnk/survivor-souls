using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-64 in play mode: the Skill Guide Book on the real MainMenu.uxml. GRIMOIRE opens it (hidden without a skill
    /// table); one card per skill of the shipped run table with icon, name, tier, card text, type and max level pips; a card
    /// tap opens the detail page with one level row per level whose numbers are the guide numbers; type and tier filters;
    /// arrows and swipes walk the filtered list and wrap; Back closes the detail first, then the page; "hide unfound" shows
    /// "???" until <see cref="SkillDiscovery"/> marks a skill; a skill added to a code-built table shows up with no UI work.
    /// </summary>
    public sealed class SkillGuideScreenTests
    {
        private const string TablePath = "Assets/_Project/Data/Skills/SkillProgression_Run.asset";
        private const string StylesPath = "Assets/_Project/Data/Skills/SkillRarityStyles.asset";
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<string> _savedFound = new List<string>();
        private SkillProgression _table;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            _table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(TablePath);
            Assert.That(_table, Is.Not.Null);
            _savedFound.Clear();
            foreach (var s in SkillGuideText.Collect(_table))
                if (SkillDiscovery.IsFound(s.Id)) _savedFound.Add(s.Id);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            foreach (var s in SkillGuideText.Collect(_table))
                if (_savedFound.Contains(s.Id)) SkillDiscovery.Mark(s.Id);
                else SkillDiscovery.Forget(s.Id);
        }

        private MainMenuController Menu(out VisualElement root, SkillProgression table)
        {
            var go = Track(new GameObject("Menu"));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            go.SetActive(false);
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(null);
            menu.SetupGuide(table, UnityEditor.AssetDatabase.LoadAssetAtPath<SkillRarityStyles>(StylesPath));
            go.SetActive(true);
            root = doc.rootVisualElement;
            return menu;
        }

        private static void Press(Button button)
        {
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = button;
            button.SendEvent(e);
        }

        private static string TextOf(VisualElement card, string className) => card.Q<Label>(className: className)?.text;

        [UnityTest]
        public IEnumerator Grimoire_OpensTheList_OneCardPerSkill_WithIconTierTextAndMaxLevel()
        {
            var menu = Menu(out var root, _table);
            yield return null;
            var page = menu.GuidePage;
            Assert.That(page, Is.Not.Null);
            Assert.That(root.Q(SkillGuideScreen.ScreenName).ClassListContains("ss-hidden"), Is.True, "closed until GRIMOIRE");
            Press(root.Q<Button>(MainMenuController.GuideButtonName));
            yield return null;
            yield return null;
            Assert.That(page.IsOpen, Is.True);
            Assert.That(page.DetailOpen, Is.False);

            var skills = SkillGuideText.Collect(_table);
            Assert.That(page.Skills.Count, Is.EqualTo(skills.Count));
            Assert.That(root.Q(className: "sg-grid").childCount, Is.EqualTo(skills.Count));
            foreach (var skill in skills)
            {
                var card = page.CardOf(skill.Id);
                Assert.That(card, Is.Not.Null, skill.Id);
                Assert.That(page.CardVisible(skill.Id), Is.True);
                Assert.That(TextOf(card, "sg-card__name"), Is.EqualTo(skill.DisplayName));
                Assert.That(TextOf(card, "sg-card__desc"), Is.EqualTo(skill.Description));
                Assert.That(TextOf(card, "sg-card__tier"), Is.EqualTo(UnityEditor.AssetDatabase.LoadAssetAtPath<SkillRarityStyles>(StylesPath).Of(skill.Rarity).label));
                Assert.That(TextOf(card, "sg-card__max"), Is.EqualTo($"MAX LV {skill.MaxLevel}"));
                Assert.That(TextOf(card, "sg-card__kind"), Is.EqualTo(SkillGuideText.KindName(skill.Kind)));
                Assert.That(card.Query(className: "sg-pip").ToList().Count, Is.EqualTo(skill.MaxLevel), $"{skill.Id} pips");
                Assert.That(card.ClassListContains($"sg-tier--{skill.Rarity.ToString().ToLowerInvariant()}"), Is.True, $"{skill.Id} tier colour");
                Assert.That(card.Q(className: "sg-icon").resolvedStyle.backgroundImage.texture, Is.EqualTo(skill.Icon), $"{skill.Id} icon");
            }
            Assert.That(root.Q<Label>("sg-count").text, Is.EqualTo($"{skills.Count} SKILLS"));
            Assert.That(menu.Back(), Is.True);
            Assert.That(page.IsOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator CardTap_OpensDetail_LevelRowsShowTheGuideNumbers_BackReturnsToList()
        {
            var menu = Menu(out var root, _table);
            yield return null;
            var page = menu.GuidePage;
            menu.OpenGuide();
            yield return null;
            var effects = new List<SkillGuideEffect>();
            foreach (var skill in page.Skills)
            {
                Press(page.CardOf(skill.Id));
                yield return null;
                Assert.That(page.DetailSkill, Is.EqualTo(skill), $"{skill.Id} opened by its card");
                Assert.That(root.Q("sg-list-page").ClassListContains("ss-hidden"), Is.True);
                Assert.That(root.Q<Label>("sg-detail-name").text, Is.EqualTo(skill.DisplayName));
                Assert.That(page.LevelRowCount, Is.EqualTo(skill.MaxLevel), $"{skill.Id} one row per level");
                for (int level = 1; level <= skill.MaxLevel; level++)
                {
                    string row = page.LevelRowText(level);
                    Assert.That(row, Does.StartWith($"LV {level} {skill.DescriptionAt(level)}"));
                    SkillGuideText.EffectsAt(skill, level, effects);
                    foreach (var e in effects) Assert.That(row, Does.Contain($"{e.Label} {e.ValueText}"), $"{skill.Id} L{level}");
                    int changed = 0;
                    foreach (var e in effects) if (e.Changed) changed++;
                    Assert.That(page.ChangedEffectsAt(level), Is.EqualTo(changed), $"{skill.Id} L{level} highlighted numbers");
                }
                Assert.That(page.HowItWorksVisible, Is.True, $"{skill.Id} rules text");
                Assert.That(page.HowItWorksText, Is.EqualTo(skill.GuideText));
                Assert.That(page.HowToGetText, Does.Contain("Reliquary chests"));
                Assert.That(root.Q("sg-combo-block").ClassListContains("ss-hidden"), Is.True, "'Works well with' hidden until KAN-65 data");
                Assert.That(page.Back(), Is.True, "back: detail first");
                Assert.That(page.DetailOpen, Is.False);
                Assert.That(page.IsOpen, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator Filters_ArrowsAndSwipes_WalkTheFilteredList()
        {
            var menu = Menu(out var root, _table);
            yield return null;
            var page = menu.GuidePage;
            menu.OpenGuide();
            yield return null;

            Press(root.Q<Button>(SkillGuideScreen.KindChipPrefix + "attack"));
            Assert.That(page.KindFilter, Is.EqualTo(SkillKind.Attack));
            Assert.That(page.VisibleSkills.Count, Is.GreaterThan(1));
            foreach (var s in page.Skills)
                Assert.That(page.CardVisible(s.Id), Is.EqualTo(s.Kind == SkillKind.Attack), s.Id);
            Assert.That(root.Q<Button>(SkillGuideScreen.KindChipPrefix + "attack").ClassListContains("sg-chip--on"), Is.True);

            Press(root.Q<Button>(SkillGuideScreen.TierChipPrefix + "legendary"));
            foreach (var s in page.VisibleSkills)
                Assert.That(s.Kind == SkillKind.Attack && s.Rarity == SkillRarity.Legendary, Is.True, s.Id);
            Press(root.Q<Button>(SkillGuideScreen.TierChipPrefix + "all"));
            Assert.That(page.TierFilter, Is.Null);

            var visible = new List<SkillDefinition>(page.VisibleSkills);
            Assert.That(page.ShowDetail(visible[0].Id), Is.True);
            Press(root.Q<Button>("sg-next"));
            Assert.That(page.DetailSkill, Is.EqualTo(visible[1]), "next arrow");
            Assert.That(page.Swipe(-SkillGuideScreen.SwipePixels - 10f), Is.True, "swipe left = next");
            Assert.That(page.DetailSkill, Is.EqualTo(visible[2 % visible.Count]));
            Assert.That(page.Swipe(30f), Is.False, "a short drag is no swipe");
            page.ShowDetail(visible[0].Id);
            Press(root.Q<Button>("sg-prev"));
            Assert.That(page.DetailSkill, Is.EqualTo(visible[visible.Count - 1]), "previous wraps to the last");
            Assert.That(root.Q<Label>("sg-pos").text, Is.EqualTo($"{visible.Count} / {visible.Count}"));

            Assert.That(menu.Back(), Is.True);
            Assert.That(page.DetailOpen, Is.False);
            Assert.That(menu.Back(), Is.True);
            Assert.That(page.IsOpen, Is.False);
            Assert.That(menu.Back(), Is.False, "nothing open");
        }

        [UnityTest]
        public IEnumerator HideUnfound_ShowsQuestionMarks_UntilTheSkillIsTaken()
        {
            var vigor = SkillGuideText.Collect(_table).Find(s => s.Id == "vigor");
            Assert.That(vigor, Is.Not.Null);
            SkillDiscovery.Forget(vigor.Id);
            var menu = Menu(out _, _table);
            yield return null;
            var page = menu.GuidePage;
            menu.OpenGuide();
            Assert.That(TextOf(page.CardOf("vigor"), "sg-card__name"), Is.EqualTo("Vigor"), "every skill shows by default");
            page.HideUnfound = true;
            Assert.That(TextOf(page.CardOf("vigor"), "sg-card__name"), Is.EqualTo("???"));
            Assert.That(page.CardOf("vigor").ClassListContains("sg-card--unknown"), Is.True);
            Assert.That(page.ShowDetail("vigor"), Is.False, "an unfound skill does not open");

            // Taking the skill in a run marks it found.
            var go = Track(new GameObject("Skills"));
            var stats = go.AddComponent<PlayerStats>();
            var manager = go.AddComponent<SkillManager>();
            manager.Setup(_table, stats, null, null, go.transform);
            Assert.That(manager.Grant(vigor), Is.True);
            Assert.That(SkillDiscovery.IsFound("vigor"), Is.True);
            page.Close();
            yield return null;
            menu.OpenGuide();
            Assert.That(TextOf(page.CardOf("vigor"), "sg-card__name"), Is.EqualTo("Vigor"), "found after the grant");
            Assert.That(page.ShowDetail("vigor"), Is.True);
        }

        [UnityTest]
        public IEnumerator NewSkillInTheTable_GetsACard_AndNoTableHidesTheButton()
        {
            var extra = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("test_new_boon", "New Boon", "+5% damage", SkillRarity.Rare, 3, new StatModifier(StatId.Damage, ModifierKind.Percent, 0.05f));
            var table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, unlocks = new[] { extra } } });
            var menu = Menu(out var root, table);
            yield return null;
            menu.OpenGuide();
            Assert.That(menu.GuidePage.CardOf("test_new_boon"), Is.Not.Null);
            Assert.That(menu.GuidePage.ShowDetail("test_new_boon"), Is.True);
            Assert.That(menu.GuidePage.LevelRowCount, Is.EqualTo(3));
            Assert.That(menu.GuidePage.LevelRowText(3), Does.Contain("Bolt damage +15%"), "no table: modifiers x level");
            Assert.That(menu.GuidePage.HowItWorksVisible, Is.False, "no rules text: section hidden");
            Assert.That(root.Q<Button>(MainMenuController.GuideButtonName).ClassListContains("ss-hidden"), Is.False);

            var noTable = Menu(out var root2, null);
            yield return null;
            Assert.That(noTable.GuidePage, Is.Null);
            Assert.That(root2.Q<Button>(MainMenuController.GuideButtonName).ClassListContains("ss-hidden"), Is.True);
        }
    }
}
