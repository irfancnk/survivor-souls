using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Level-up re-roll (KAN-67): the run counter in RunState (per-run count from the table, bonus re-rolls, carries over
    /// Chambers, reset by a new run), the re-roll roll (shown cards left out while others remain, guaranteed skill not
    /// forced, counts for pity, tier shares unchanged over 1000 re-rolls) and the screen (button with count, arm delay,
    /// one re-roll per tap, flip to a new offer with tier reveals again, disabled at 0 or with nothing else to offer,
    /// hidden on the chest screen, picking after a re-roll) plus the shipped data.
    /// </summary>
    public sealed class SkillRerollTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private SkillManager _manager;
        private SkillProgression _table;

        [SetUp]
        public void SetUp()
        {
            RunState.NewRun(1, 4242);
            _table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(_table, null, null, null, _manager.transform, rngSeed: 67);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private SkillDefinition Skill(string id, SkillRarity rarity, int max = 3) =>
            Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure(id, id, id, rarity, max);

        private SkillDefinition[] Many(string prefix, SkillRarity rarity, int n) =>
            Enumerable.Range(0, n).Select(i => Skill(prefix + i, rarity)).ToArray();

        private static SkillProgression.Tier Tier(int from, float c, float r, float e, float l, params SkillDefinition[] unlocks) =>
            new SkillProgression.Tier { fromLevel = from, common = c, rare = r, epic = e, legendary = l, unlocks = unlocks };

        // ------------------------------------------------------------ Run counter

        [Test]
        public void RunState_RerollsPerRun_SpendAddCarryAndReset()
        {
            Assert.That(RunState.RerollsLeft(2), Is.EqualTo(2), "new run: the table's count");
            Assert.That(RunState.TryUseReroll(2), Is.True);
            Assert.That(RunState.RerollsLeft(2), Is.EqualTo(1));
            RunState.AdvanceChamber(new RunSnapshot());
            RunState.TakeCarry();
            Assert.That(RunState.RerollsLeft(2), Is.EqualTo(1), "carries into the next Chamber");
            Assert.That(RunState.TryUseReroll(2), Is.True);
            Assert.That(RunState.TryUseReroll(2), Is.False, "none left: nothing spent");
            Assert.That(RunState.RerollsLeft(2), Is.Zero);
            RunState.AddRerolls(2);
            RunState.AddRerolls(-5);
            Assert.That(RunState.RerollsLeft(2), Is.EqualTo(2), "AddRerolls gives more, never takes");
            Assert.That(RunState.RerollsLeft(0), Is.Zero, "never negative");
            RunState.NewRun();
            Assert.That(RunState.RerollsLeft(2), Is.EqualTo(2), "a new run resets used and bonus");
            Assert.That(RunState.BonusRerolls, Is.Zero);
        }

        [Test]
        public void Manager_RerollsLeft_ReadsTheTableCount()
        {
            _table.Configure(new[] { Tier(2, 1f, 0f, 0f, 0f) }, rerolls: 1);
            Assert.That(_manager.RerollsLeft, Is.EqualTo(1));
            RunState.AddRerolls(1);
            Assert.That(_manager.RerollsLeft, Is.EqualTo(2));
        }

        // ------------------------------------------------------------ Re-roll roll

        [Test]
        public void Reroll_LeavesOutTheShownCards_WhenOthersRemain()
        {
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("s", SkillRarity.Common, 6)) }, pity: 0);
            var offer = new List<SkillDefinition>();
            var shown = new List<SkillDefinition>();
            for (int n = 0; n < 200; n++)
            {
                _manager.Roll(3, 2, offer);
                shown.Clear();
                shown.AddRange(offer);
                Assert.That(_manager.Roll(3, 2, offer, shown), Is.EqualTo(3));
                Assert.That(offer.Intersect(shown), Is.Empty, "none of the shown cards again");
                Assert.That(offer.Distinct().Count(), Is.EqualTo(3));
            }
        }

        [Test]
        public void Reroll_SmallPool_LeavesOutAsManyAsPossible_InOrder()
        {
            var s = Many("s", SkillRarity.Common, 4);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, s) }, pity: 0);
            var offer = new List<SkillDefinition>();
            _manager.Roll(3, 2, offer, new[] { s[0], s[1], s[2] });
            Assert.That(offer, Has.No.Member(s[0]), "the first shown card goes");
            Assert.That(offer, Has.Member(s[3]), "the only other skill comes in");
            Assert.That(offer.Count, Is.EqualTo(3), "still a full offer");
        }

        [Test]
        public void Reroll_DoesNotForceTheGuaranteedSkillAgain()
        {
            var commons = Many("c", SkillRarity.Common, 6);
            var guaranteed = commons[0];
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, commons) },
                new[] { new SkillProgression.Guarantee { level = 2, skill = guaranteed } }, pity: 0);
            var offer = new List<SkillDefinition>();
            _manager.Roll(3, 2, offer);
            Assert.That(offer[0], Is.EqualTo(guaranteed), "normal roll: guaranteed first");
            for (int n = 0; n < 100; n++)
            {
                _manager.Roll(3, 2, offer, new[] { guaranteed, commons[1], commons[2] });
                Assert.That(offer, Has.No.Member(guaranteed));
            }
        }

        [Test]
        public void Reroll_CountsAsAnOfferForPity()
        {
            // Rare weight 0: pity has nothing to swap in, so the dry counter shows every offer.
            _table.Configure(new[] { Tier(2, 1f, 0f, 0f, 0f, Many("c", SkillRarity.Common, 6).Append(Skill("r", SkillRarity.Rare)).ToArray()) }, pity: 3);
            var offer = new List<SkillDefinition>();
            _manager.Roll(3, 2, offer);
            Assert.That(_manager.OffersWithoutRarePlus, Is.EqualTo(1));
            _manager.Roll(3, 2, offer, offer.ToArray());
            Assert.That(_manager.OffersWithoutRarePlus, Is.EqualTo(2), "a re-roll is one more offer, not a pity reset");
        }

        [Test]
        public void Reroll_TierSharesAndPity_SameAsANormalRoll_Over1000Offers()
        {
            var pool = Many("c", SkillRarity.Common, 8).Concat(Many("r", SkillRarity.Rare, 4))
                .Concat(Many("e", SkillRarity.Epic, 3)).Concat(Many("l", SkillRarity.Legendary, 2)).ToArray();
            _table.Configure(new[] { Tier(2, 55f, 30f, 12f, 3f, pool) }, pity: 3);
            int dryBefore = _manager.OffersWithoutRarePlus;
            var plain = _manager.SimulateOffers(1000, 2);
            var rerolled = _manager.SimulateOffers(1000, 2, reroll: true);
            Debug.Log($"[KAN-67] plain   {plain}\n[KAN-67] re-roll {rerolled}");
            Assert.That(rerolled.Share(SkillRarity.Common), Is.EqualTo(plain.Share(SkillRarity.Common)).Within(0.03f), "Common share kept");
            Assert.That(rerolled.Offers, Is.EqualTo(1000));
            float High(SkillManager.OfferStats st) => st.Share(SkillRarity.Epic) + st.Share(SkillRarity.Legendary);
            Assert.That(High(rerolled), Is.LessThanOrEqualTo(High(plain) + 0.02f), "a re-roll never makes Epic / Legendary likelier");
            Assert.That(High(rerolled), Is.GreaterThanOrEqualTo(High(plain) - 0.02f), "nor rarer");
            Assert.That(_manager.OffersWithoutRarePlus, Is.EqualTo(dryBefore), "the simulation puts the pity counter back");

            // Pity over every offer the player sees (first roll + its re-roll): never 3 in a row without Rare+.
            var offer = new List<SkillDefinition>();
            var shown = new List<SkillDefinition>();
            int dry = 0, longest = 0;
            void Count()
            {
                dry = offer.Any(x => SkillManager.IsRarePlus(x.Rarity)) ? 0 : dry + 1;
                longest = Mathf.Max(longest, dry);
            }
            for (int n = 0; n < 1000; n++)
            {
                _manager.Roll(3, 2, offer);
                Count();
                shown.Clear();
                shown.AddRange(offer);
                _manager.Roll(3, 2, offer, shown);
                Count();
            }
            Assert.That(longest, Is.LessThan(3), "pity holds with re-rolls in the stream");
        }

        [Test]
        public void OfferableCount_SkipsMaxedSkills()
        {
            var s = Many("s", SkillRarity.Common, 4);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, s) });
            Assert.That(_manager.OfferableCount(2), Is.EqualTo(4));
            _manager.SetLevel(s[0], s[0].MaxLevel);
            Assert.That(_manager.OfferableCount(2), Is.EqualTo(3));
            _manager.SetLevel(s[1], s[1].MaxLevel);
            _manager.SetLevel(s[2], s[2].MaxLevel);
            _manager.SetLevel(s[3], s[3].MaxLevel);
            Assert.That(_manager.OfferableCount(2), Is.Zero, "every skill maxed");
        }

        // ------------------------------------------------------------ Screen

#if UNITY_EDITOR
        private SkillChoiceScreen OpenScreen()
        {
            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var screen = go.AddComponent<SkillChoiceScreen>();
            screen.Setup(_manager, 0.4f);
            go.SetActive(true);
            return screen;
        }

        private static void Run(SkillChoiceScreen screen, float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f / 60f) screen.Tick(1f / 60f);
        }

        [UnityTest]
        public IEnumerator Screen_Reroll_CountFlipNewOfferGuardsAndPick()
        {
            var pool = Many("s", SkillRarity.Common, 9);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, pool) }, pity: 0, rerolls: 2);
            var screen = OpenScreen();
            yield return null;
            var reveals = new List<int>();
            var rerolledLeft = new List<int>();
            screen.CardRevealed += (i, s) => reveals.Add(i);
            screen.Rerolled += left => rerolledLeft.Add(left);
            bool done = false;
            screen.Present(2, () => done = true);

            var button = screen.GetComponent<UIDocument>().rootVisualElement.Q<Button>("skill-reroll");
            Assert.That(button, Is.Not.Null);
            Assert.That(screen.RerollShown, Is.True);
            Assert.That(button.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None));
            Assert.That(screen.RerollLabel, Is.EqualTo(SkillChoiceScreen.RerollText));
            Assert.That(screen.RerollCountLabel, Is.EqualTo("x2"));
            Assert.That(screen.RerollEnabled, Is.True);
            Assert.That(screen.Reroll(), Is.False, "taps in the first 0.4 s are ignored");

            Run(screen, 0.6f);
            Assert.That(reveals, Is.EqualTo(new[] { 0, 1, 2 }));
            var first = screen.Options.ToArray();
            Assert.That(screen.Reroll(), Is.True);
            Assert.That(screen.Reroll(), Is.False, "a double tap spends only one");
            Assert.That(rerolledLeft, Is.EqualTo(new[] { 1 }));
            Assert.That(screen.RerollCountLabel, Is.EqualTo("x1"));
            Assert.That(screen.IsFlipping, Is.True);
            Assert.That(screen.Choose(0), Is.False, "no pick while the cards flip");
            Assert.That(screen.Options, Is.EqualTo(first), "old cards stay until they are shut");

            Run(screen, 0.2f);
            Assert.That(screen.IsFlipping, Is.False);
            Assert.That(screen.Options.Count, Is.EqualTo(3));
            Assert.That(screen.Options.Intersect(first), Is.Empty, "a new offer without the shown cards");
            Run(screen, 0.4f);
            Assert.That(reveals.Count, Is.EqualTo(6), "the new cards reveal their tier again");
            var card0 = screen.GetComponent<UIDocument>().rootVisualElement.Q<Button>("skill-card-0");
            Assert.That(card0.style.scale.value.value.x, Is.EqualTo(1f).Within(0.02f), "flipped fully open");

            Assert.That(screen.Reroll(), Is.True, "second re-roll");
            Run(screen, 0.6f);
            Assert.That(screen.RerollCountLabel, Is.EqualTo("x0"));
            Assert.That(screen.RerollEnabled, Is.False, "disabled at 0");
            Assert.That(screen.Reroll(), Is.False);
            Assert.That(RunState.RerollsUsed, Is.EqualTo(2));

            var pick = screen.Options[1];
            Assert.That(screen.Choose(1), Is.True, "picking works after a re-roll");
            Assert.That(_manager.GetLevel(pick), Is.EqualTo(1));
            Run(screen, 0.5f);
            Assert.That(done, Is.True);

            RunState.AddRerolls(1); // debug menu / future reward
            screen.Present(3, () => { });
            Assert.That(screen.RerollCountLabel, Is.EqualTo("x1"), "count carries to the next level-up, bonus added");
            RunState.AddRerolls(1);
            screen.Tick(0.01f);
            Assert.That(screen.RerollCountLabel, Is.EqualTo("x2"), "an open screen shows a bonus re-roll at once");
        }

        [UnityTest]
        public IEnumerator Screen_NothingElseToOffer_DisabledWithHint()
        {
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("s", SkillRarity.Common, 3)) }, pity: 0);
            var screen = OpenScreen();
            yield return null;
            screen.Present(2, () => { });
            Run(screen, 0.6f);
            Assert.That(screen.RerollLabel, Is.EqualTo(SkillChoiceScreen.RerollNoneText));
            Assert.That(screen.RerollEnabled, Is.False);
            Assert.That(screen.Reroll(), Is.False);
            Assert.That(RunState.RerollsUsed, Is.Zero, "nothing spent");
        }

        [UnityTest]
        public IEnumerator Screen_ChestUpgrade_HasNoReroll()
        {
            var s = Many("s", SkillRarity.Common, 5);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, s) }, pity: 0);
            _manager.Grant(s[0]);
            _manager.Grant(s[1]);
            var screen = OpenScreen();
            yield return null;
            Assert.That(screen.PresentUpgrade(new SurvivorSouls.Gameplay.Progression.UpgradeOffer(3, 1, false), () => { }), Is.True);
            Run(screen, 0.6f);
            var button = screen.GetComponent<UIDocument>().rootVisualElement.Q<Button>("skill-reroll");
            Assert.That(button.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.RerollShown, Is.False);
            Assert.That(screen.Reroll(), Is.False);
        }

        [Test]
        public void ShippedData_TwoRerollsPerRun_ButtonInTheHud()
        {
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>("Assets/_Project/Data/Skills/SkillProgression_Run.asset");
            Assert.That(table, Is.Not.Null);
            Assert.That(table.RerollsPerRun, Is.EqualTo(2));
            var tree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var root = tree.Instantiate();
            Assert.That(root.Q<Button>("skill-reroll"), Is.Not.Null);
            Assert.That(root.Q<Label>("skill-reroll-count"), Is.Not.Null);
        }
#endif
    }
}
