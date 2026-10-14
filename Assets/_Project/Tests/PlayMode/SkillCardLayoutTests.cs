#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Card layout on the real HUD (KAN-76): every shipped skill at every level, in level-up and chest mode. The name and
    /// the level text get all the height their wrapped lines need (no squashed label drawing over its neighbours), the
    /// text ends at least 12 px above the level pips, the pips sit inside the card, and the three cards of one offer stay the same height.
    /// The panel matches width (1080 reference), so wrapping is the same on every phone aspect.
    /// </summary>
    public sealed class SkillCardLayoutTests
    {
        private const string SkillDir = "Assets/_Project/Data/Skills";
        private const float Slack = 0.5f; // panel px
        /// <summary>Text box vs measured text: measuring rounds ~1 px apart from layout; a squashed line is ~40 px.</summary>
        private const float LineSlack = 4f;
        /// <summary>Clear space between the last text line and the pip row (the bug: 0 px, the line sat on the pips).</summary>
        private const float MinPipGap = 12f;

        private readonly List<Object> _objects = new List<Object>();
        private SkillManager _manager;
        private SkillChoiceScreen _screen;
        private UIDocument _doc;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var player = Track(new GameObject("Player"));
            var stats = player.AddComponent<PlayerStats>();
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), stats, null, null, player.transform, rngSeed: 76);

            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            _doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            _screen = go.AddComponent<SkillChoiceScreen>();
            _screen.Setup(_manager, 0f);
            go.SetActive(true);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static SkillDefinition[] Shipped() =>
            UnityEditor.AssetDatabase.FindAssets("t:SkillDefinition", new[] { SkillDir })
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .OrderByDescending(s => s.DisplayName.Length) // the longest names share a screen
                .ToArray();

        [UnityTest]
        public IEnumerator LevelUpCards_EverySkillEveryLevel_TextFitsAbovePips()
        {
            var skills = Shipped();
            Assert.That(skills.Length, Is.GreaterThanOrEqualTo(12));
            int checkedCards = 0;
            for (int g = 0; g < skills.Length; g += SkillChoiceScreen.MaxCards)
            {
                var group = skills.Skip(g).Take(SkillChoiceScreen.MaxCards).ToArray();
                _manager.ForceNextOffer(group);
                _screen.Present(2, () => { });
                _screen.Tick(1f); // cards landed: no rise / scale left in the world rects
                Assert.That(_screen.Options, Is.EqualTo(group));

                int top = group.Max(s => s.MaxLevel);
                for (int level = 1; level <= top; level++)
                {
                    for (int i = 0; i < group.Length; i++)
                        Card(i).Q<Label>(className: "skill-card__desc").text = group[i].DescriptionAt(level);
                    yield return null;
                    yield return null;
                    for (int i = 0; i < group.Length; i++, checkedCards++)
                        AssertFits(Card(i), $"{group[i].Id} L{Mathf.Min(level, group[i].MaxLevel)}");
                    AssertSameHeight(group.Length, group.Select(s => s.Id).Aggregate((a, b) => a + "," + b));
                }
                _screen.Tick(1f);
                Assert.That(_screen.Choose(0), Is.True);
                _screen.Tick(1f);
                _manager.ResetRun();
            }
            Assert.That(checkedCards, Is.GreaterThanOrEqualTo(56));
        }

        [UnityTest]
        public IEnumerator ChestCards_LongestTexts_FitAbovePips()
        {
            var skills = Shipped();
            var longest = skills
                .OrderByDescending(s => Enumerable.Range(2, Mathf.Max(1, s.MaxLevel - 1)).Max(l => s.DescriptionAt(l).Length) + s.DisplayName.Length)
                .Take(SkillChoiceScreen.MaxCards).ToArray();
            foreach (var s in longest) _manager.Grant(s);
            _manager.ForceNextOffer(longest);
            Assert.That(_screen.PresentUpgrade(new UpgradeOffer(3, 2, true), () => { }), Is.True);
            Assert.That(_screen.IsUpgrade, Is.True);
            _screen.Tick(1f);
            yield return null;
            yield return null;
            for (int i = 0; i < longest.Length; i++)
                AssertFits(Card(i), longest[i].Id + " chest +2");
            AssertSameHeight(longest.Length, "chest");
        }

        private Button Card(int i) => _doc.rootVisualElement.Q<Button>("skill-card-" + i);

        private static void AssertFits(Button card, string what)
        {
            var name = card.Q<Label>(className: "skill-card__name");
            var desc = card.Q<Label>(className: "skill-card__desc");
            var pips = card.Q(className: "skill-card__pips");
            Assert.That(desc.contentRect.width, Is.GreaterThan(100f), what + " card is laid out");
            Assert.That(name.contentRect.height, Is.GreaterThanOrEqualTo(Needed(name) - LineSlack), what + " name gets the height of its lines");
            Assert.That(desc.contentRect.height, Is.GreaterThanOrEqualTo(Needed(desc) - LineSlack), what + " text gets the height of its lines");
            Assert.That(name.worldBound.yMax, Is.LessThanOrEqualTo(desc.worldBound.yMin + Slack), what + " name above the text");
            Assert.That(pips.worldBound.yMin - desc.worldBound.yMax, Is.GreaterThanOrEqualTo(MinPipGap - Slack), what + " clear gap between text and pips");
            Assert.That(pips.worldBound.yMax, Is.LessThanOrEqualTo(card.worldBound.yMax + Slack), what + " pips inside the card");
        }

        private static float Needed(Label label) =>
            label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined).y;

        private void AssertSameHeight(int count, string what)
        {
            float h = Card(0).layout.height;
            for (int i = 1; i < count; i++)
                Assert.That(Card(i).layout.height, Is.EqualTo(h).Within(Slack), what + " cards of one offer share a height");
        }
    }
}
#endif
