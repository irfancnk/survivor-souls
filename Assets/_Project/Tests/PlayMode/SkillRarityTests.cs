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
    /// Skill rarity tiers (KAN-37): Legendary weight bucket, pity rule (Rare+ at least every N offers, never over the
    /// guaranteed card, never a zero-weight tier), forced offers, the offer statistics (shares follow the table), the
    /// card screen (tier class, name, 1-4 gems, reveal event once per card, Legendary shine + burst moving) and the
    /// shipped data (tiers of the 12 skills, odds table, style asset with 4 reveal sounds).
    /// </summary>
    public sealed class SkillRarityTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private SkillManager _manager;
        private SkillProgression _table;

        [SetUp]
        public void SetUp()
        {
            _table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(_table, null, null, null, _manager.transform, rngSeed: 37);
        }

        [TearDown]
        public void TearDown()
        {
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

        private SkillDefinition Skill(string id, SkillRarity rarity, int max = 3) =>
            Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure(id, id, id, rarity, max);

        private static SkillProgression.Tier Tier(int from, float c, float r, float e, float l, params SkillDefinition[] unlocks) =>
            new SkillProgression.Tier { fromLevel = from, common = c, rare = r, epic = e, legendary = l, unlocks = unlocks };

        private SkillDefinition[] Many(string prefix, SkillRarity rarity, int n) =>
            Enumerable.Range(0, n).Select(i => Skill(prefix + i, rarity)).ToArray();

        // ------------------------------------------------------------ Odds

        [Test]
        public void Tier_WeightOf_MapsEveryRarity_LegendaryDefaultsToZero()
        {
            var t = Tier(2, 1f, 2f, 3f, 4f);
            Assert.That(t.WeightOf(SkillRarity.Common), Is.EqualTo(1f));
            Assert.That(t.WeightOf(SkillRarity.Rare), Is.EqualTo(2f));
            Assert.That(t.WeightOf(SkillRarity.Epic), Is.EqualTo(3f));
            Assert.That(t.WeightOf(SkillRarity.Legendary), Is.EqualTo(4f));
            Assert.That(new SkillProgression.Tier().legendary, Is.Zero, "a tier offers Legendary only when set");
        }

        [Test]
        public void Roll_LegendaryWithZeroWeight_NeverOfferedWhileOthersRemain()
        {
            var legend = Skill("legend", SkillRarity.Legendary);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 0f, Many("c", SkillRarity.Common, 5).Append(legend).ToArray()) });
            var offer = new List<SkillDefinition>();
            for (int n = 0; n < 200; n++)
            {
                _manager.Roll(3, 2, offer);
                Assert.That(offer, Has.No.Member(legend));
            }
        }

        [Test]
        public void SimulateOffers_SharesFollowTheTable_AndKeepThePityCounter()
        {
            // 10 skills per tier, equal skill weights, one card per offer, no pity: share per tier = table weight.
            var pool = Many("c", SkillRarity.Common, 10).Concat(Many("r", SkillRarity.Rare, 10))
                .Concat(Many("e", SkillRarity.Epic, 10)).Concat(Many("l", SkillRarity.Legendary, 10)).ToArray();
            _table.Configure(new[] { Tier(2, 55f, 30f, 12f, 3f, pool) }, pity: 0);
            var stats = _manager.SimulateOffers(4000, 2, count: 1);
            Assert.That(stats.Offers, Is.EqualTo(4000));
            Assert.That(stats.Share(SkillRarity.Common), Is.EqualTo(0.55f).Within(0.03f));
            Assert.That(stats.Share(SkillRarity.Rare), Is.EqualTo(0.30f).Within(0.03f));
            Assert.That(stats.Share(SkillRarity.Epic), Is.EqualTo(0.12f).Within(0.02f));
            Assert.That(stats.Share(SkillRarity.Legendary), Is.EqualTo(0.03f).Within(0.012f));
            Assert.That(stats.PityRescues, Is.Zero);
            StringAssert.Contains("Legendary", stats.ToString());
            Assert.That(_manager.OffersWithoutRarePlus, Is.Zero, "simulation puts the live pity counter back");
        }

        // ------------------------------------------------------------ Pity

        [Test]
        public void Pity_RarePlusAtLeastEveryThirdOffer_OffWhenZero()
        {
            var commons = Many("c", SkillRarity.Common, 8);
            var rare = Skill("rare", SkillRarity.Rare);
            var pool = commons.Append(rare).ToArray();

            _table.Configure(new[] { Tier(2, 1000f, 0.01f, 0f, 0f, pool) }, pity: 3);
            var stats = _manager.SimulateOffers(300, 2);
            Assert.That(stats.LongestDryRun, Is.LessThanOrEqualTo(2), "never 3 offers in a row without Rare+");
            Assert.That(stats.PityRescues, Is.GreaterThan(80), "the tiny rare weight alone would almost never show");
            Assert.That(stats.OffersWithRarePlus, Is.GreaterThanOrEqualTo(100));

            _table.Configure(new[] { Tier(2, 1000f, 0.01f, 0f, 0f, pool) }, pity: 0);
            stats = _manager.SimulateOffers(300, 2);
            Assert.That(stats.PityRescues, Is.Zero);
            Assert.That(stats.LongestDryRun, Is.GreaterThan(10), "pity off: long runs of commons");
        }

        [Test]
        public void Pity_SwapsTheLastCommonCard_CountsOffers_AndResetsOnRarePlus()
        {
            var commons = Many("c", SkillRarity.Common, 8);
            var rare = Skill("rare", SkillRarity.Rare);
            _table.Configure(new[] { Tier(2, 1000f, 0.0001f, 0f, 0f, commons.Append(rare).ToArray()) }, pity: 3);
            var offer = new List<SkillDefinition>();

            _manager.Roll(3, 2, offer);
            Assert.That(_manager.OffersWithoutRarePlus, Is.EqualTo(1));
            _manager.Roll(3, 2, offer);
            Assert.That(_manager.OffersWithoutRarePlus, Is.EqualTo(2));
            Assert.That(_manager.LastRollUsedPity, Is.False);

            _manager.Roll(3, 2, offer);
            Assert.That(_manager.LastRollUsedPity, Is.True);
            Assert.That(offer[2], Is.EqualTo(rare), "the last card is the one swapped");
            Assert.That(offer.Distinct().Count(), Is.EqualTo(3), "no duplicate after the swap");
            Assert.That(_manager.OffersWithoutRarePlus, Is.Zero);

            _manager.ResetRun();
            Assert.That(_manager.OffersWithoutRarePlus, Is.Zero);
        }

        [Test]
        public void Pity_RestoredCounter_ForcesRarePlusOnTheFirstOfferOfTheNextChamber()
        {
            var commons = Many("c", SkillRarity.Common, 8);
            var rare = Skill("rare", SkillRarity.Rare);
            _table.Configure(new[] { Tier(2, 1000f, 0.0001f, 0f, 0f, commons.Append(rare).ToArray()) }, pity: 3);
            var offer = new List<SkillDefinition>();

            _manager.RestorePity(2); // two dry offers in the Chamber before (KAN-54)
            _manager.Roll(3, 2, offer);
            Assert.That(_manager.LastRollUsedPity, Is.True, "the carried count reaches the pity limit");
            Assert.That(offer, Has.Member(rare));
            Assert.That(_manager.OffersWithoutRarePlus, Is.Zero);
        }

        [Test]
        public void Pity_NeverReplacesTheGuarantee_AndSkipsZeroWeightTiers()
        {
            var commons = Many("c", SkillRarity.Common, 4);
            var star = Skill("star", SkillRarity.Common, 9);
            var epic = Skill("epic", SkillRarity.Epic);
            _table.Configure(new[] { Tier(2, 1f, 0f, 0f, 0f, commons.Append(star).Append(epic).ToArray()) },
                new[] { new SkillProgression.Guarantee { level = 2, skill = star } }, pity: 1);
            var offer = new List<SkillDefinition>();
            for (int n = 0; n < 20; n++)
            {
                _manager.Roll(3, 2, offer);
                Assert.That(offer[0], Is.EqualTo(star));
                Assert.That(offer, Has.No.Member(epic), "epic weight 0 in this tier: pity does not force it");
                Assert.That(_manager.LastRollUsedPity, Is.False);
            }

            _table.Configure(new[] { Tier(2, 1f, 0f, 1e-6f, 0f, commons.Append(star).Append(epic).ToArray()) },
                new[] { new SkillProgression.Guarantee { level = 2, skill = star } }, pity: 1);
            _manager.Roll(1, 2, offer);
            Assert.That(offer, Is.EqualTo(new[] { star }), "only the guarantee: nothing to swap");
            _manager.Roll(3, 2, offer);
            Assert.That(offer[0], Is.EqualTo(star));
            Assert.That(offer, Has.Member(epic), "pity 1: every offer has a Rare+ card");
        }

        [Test]
        public void ForceNextOffer_UsedOnce_SkipsMaxedSkills()
        {
            var a = Skill("a", SkillRarity.Legendary, 1);
            var b = Skill("b", SkillRarity.Epic);
            var c = Skill("c", SkillRarity.Common);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("x", SkillRarity.Common, 6)) });
            _manager.Grant(a);
            _manager.ForceNextOffer(a, b, c);
            var offer = new List<SkillDefinition>();
            Assert.That(_manager.Roll(3, 2, offer), Is.EqualTo(2));
            Assert.That(offer, Is.EqualTo(new[] { b, c }), "maxed a left out, order kept");
            _manager.Roll(3, 2, offer);
            Assert.That(offer, Has.No.Member(b), "one use only");
        }

        // ------------------------------------------------------------ Card screen

#if UNITY_EDITOR
        private SkillChoiceScreen OpenScreen(SkillRarityStyles styles)
        {
            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var screen = go.AddComponent<SkillChoiceScreen>();
            screen.Setup(_manager, 0.4f, styles);
            go.SetActive(true);
            return screen;
        }

        [UnityTest]
        public IEnumerator CardScreen_ShowsTierClassNameAndGems_RevealsOnce_LegendaryBursts()
        {
            var styles = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillRarityStyles>("Assets/_Project/Data/Skills/SkillRarityStyles.asset");
            Assert.That(styles, Is.Not.Null);
            var legend = Skill("legend", SkillRarity.Legendary);
            var epic = Skill("epic", SkillRarity.Epic);
            var common = Skill("common", SkillRarity.Common);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, legend, epic, common) });
            var screen = OpenScreen(styles);
            yield return null;

            var reveals = new List<int>();
            screen.CardRevealed += (i, s) => reveals.Add(i);
            _manager.ForceNextOffer(legend, epic, common);
            screen.Present(2, () => { });
            Assert.That(screen.Options, Is.EqualTo(new[] { legend, epic, common }));

            var root = screen.GetComponent<UIDocument>().rootVisualElement;
            var cards = Enumerable.Range(0, 3).Select(i => root.Q<Button>("skill-card-" + i)).ToArray();
            Assert.That(cards[0].ClassListContains("skill-card--legendary"), Is.True);
            Assert.That(cards[1].ClassListContains("skill-card--epic"), Is.True);
            Assert.That(cards[2].ClassListContains("skill-card--common"), Is.True);
            Assert.That(cards[0].ClassListContains("skill-card--common"), Is.False, "one tier class per card");
            Assert.That(cards[0].Q<Label>(className: "skill-card__rarity").text, Is.EqualTo("LEGENDARY"));
            Assert.That(cards[1].Q<Label>(className: "skill-card__rarity").text, Is.EqualTo("EPIC"));
            int Gems(Button card) => card.Q(className: "skill-card__marks").Children().Count(m => m.style.display != DisplayStyle.None);
            Assert.That(Gems(cards[0]), Is.EqualTo(4));
            Assert.That(Gems(cards[1]), Is.EqualTo(3));
            Assert.That(Gems(cards[2]), Is.EqualTo(1));

            Assert.That(reveals, Is.Empty, "nothing has landed at open");
            var burst = cards[0].Q(className: "skill-card__burst");
            float peakBurst = 0f;
            for (int f = 0; f < 30; f++)
            {
                screen.Tick(1f / 60f);
                peakBurst = Mathf.Max(peakBurst, burst.style.opacity.value);
            }
            Assert.That(reveals, Is.EqualTo(new[] { 0, 1, 2 }), "each card once, in landing order");
            Assert.That(peakBurst, Is.GreaterThan(0.3f), "Legendary ring burst shows when the card lands");
            Assert.That(cards[1].Q(className: "skill-card__burst").style.opacity.value, Is.Zero, "Epic has no burst");
            var shine = cards[0].Q(className: "skill-card__shine");
            float x0 = shine.style.translate.value.x.value;
            screen.Tick(0.2f);
            Assert.That(shine.style.translate.value.x.value, Is.Not.EqualTo(x0), "Legendary shine sweeps");
            for (int f = 0; f < 60; f++) screen.Tick(1f / 60f);
            Assert.That(reveals.Count, Is.EqualTo(3), "no second reveal while open");
            Assert.That(burst.style.opacity.value, Is.Zero, "burst is over");
        }

        [Test]
        public void PickPulse_GrowsWithTier_EpicAndLegendaryBurst()
        {
            Assert.That(SkillChoiceScreen.PickPulse(SkillRarity.Common), Is.EqualTo(SkillChoiceScreen.PickPulse(SkillRarity.Rare)));
            Assert.That(SkillChoiceScreen.PickPulse(SkillRarity.Epic), Is.GreaterThan(SkillChoiceScreen.PickPulse(SkillRarity.Rare)));
            Assert.That(SkillChoiceScreen.PickPulse(SkillRarity.Legendary), Is.GreaterThan(SkillChoiceScreen.PickPulse(SkillRarity.Epic)));
            Assert.That(SkillChoiceScreen.PickBursts(SkillRarity.Common), Is.False);
            Assert.That(SkillChoiceScreen.PickBursts(SkillRarity.Rare), Is.False);
            Assert.That(SkillChoiceScreen.PickBursts(SkillRarity.Epic), Is.True);
            Assert.That(SkillChoiceScreen.PickBursts(SkillRarity.Legendary), Is.True);
        }

        [UnityTest]
        public IEnumerator CardScreen_EpicPick_PulsesHarderAndBurstsAgain_CommonPickDoesNot()
        {
            var styles = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillRarityStyles>("Assets/_Project/Data/Skills/SkillRarityStyles.asset");
            var epic = Skill("epic", SkillRarity.Epic);
            var common = Skill("common", SkillRarity.Common);
            var rare = Skill("rare", SkillRarity.Rare);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, epic, common, rare) });
            var screen = OpenScreen(styles);
            yield return null;
            var root = screen.GetComponent<UIDocument>().rootVisualElement;

            // Picks card index and returns (peak card scale, peak burst opacity, peak spark opacity) over the pick-out.
            (float scale, float burst, float spark) PickAndWatch(int index)
            {
                var card = root.Q<Button>("skill-card-" + index);
                var burst = card.Q(className: "skill-card__burst");
                var sparks = card.Query(className: "skill-spark").ToList();
                for (int f = 0; f < 90 && !screen.IsArmed; f++) screen.Tick(1f / 60f);
                Assert.That(screen.IsArmed, Is.True, "test setup: picks armed");
                for (int f = 0; f < 20; f++) screen.Tick(1f / 60f); // let landing bursts finish
                Assert.That(screen.Choose(index), Is.True);
                float s = 0f, b = 0f, k = 0f;
                for (int f = 0; f < 22 && screen.IsShowing; f++)
                {
                    screen.Tick(1f / 60f);
                    s = Mathf.Max(s, card.style.scale.value.value.y);
                    b = Mathf.Max(b, burst.style.opacity.value);
                    foreach (var sp in sparks) k = Mathf.Max(k, sp.style.opacity.value);
                }
                return (s, b, k);
            }

            _manager.ForceNextOffer(epic, common, rare);
            screen.Present(2, () => { });
            var epicPick = PickAndWatch(0);
            for (int f = 0; f < 60 && screen.IsShowing; f++) screen.Tick(1f / 60f);

            _manager.ForceNextOffer(epic, common, rare);
            screen.Present(3, () => { });
            var commonPick = PickAndWatch(1);

            Assert.That(epicPick.burst, Is.GreaterThan(0.5f), "Epic pick fires the ring burst");
            Assert.That(epicPick.spark, Is.GreaterThan(0.5f), "Epic pick throws sparks");
            Assert.That(commonPick.burst, Is.Zero, "Common pick: no ring");
            Assert.That(commonPick.spark, Is.Zero, "Common pick: no sparks");
            Assert.That(epicPick.scale, Is.GreaterThan(commonPick.scale + 0.03f), "Epic pulse is stronger than Common");
        }

        // ------------------------------------------------------------ Shipped data

        [Test]
        public void ShippedData_TiersOddsAndStyles()
        {
            SkillDefinition Load(string file) =>
                UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>($"Assets/_Project/Data/Skills/Skill_{file}.asset");
            Assert.That(Load("SharpenedBolts").Rarity, Is.EqualTo(SkillRarity.Common));
            Assert.That(Load("QuickDraw").Rarity, Is.EqualTo(SkillRarity.Common));
            Assert.That(Load("Vigor").Rarity, Is.EqualTo(SkillRarity.Common));
            Assert.That(Load("KeenEye").Rarity, Is.EqualTo(SkillRarity.Rare));
            Assert.That(Load("PiercingBolts").Rarity, Is.EqualTo(SkillRarity.Rare));
            Assert.That(Load("BloodCovenant").Rarity, Is.EqualTo(SkillRarity.Epic));
            Assert.That(Load("Multishot").Rarity, Is.EqualTo(SkillRarity.Rare), "Spread Shot (KAN-83)");
            Assert.That(Load("DoubleShot").Rarity, Is.EqualTo(SkillRarity.Epic));
            Assert.That(Load("TemporaryShield").Rarity, Is.EqualTo(SkillRarity.Epic), "Hallowed Shield (KAN-93)");
            Assert.That(Load("EmberBolts").Rarity, Is.EqualTo(SkillRarity.Legendary));
            Assert.That(Load("SoulBurst").Rarity, Is.EqualTo(SkillRarity.Legendary));

            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>("Assets/_Project/Data/Skills/SkillProgression_Run.asset");
            Assert.That(table.PityOffers, Is.EqualTo(3));
            Assert.That(table.RarityWeight(2, SkillRarity.Legendary), Is.Zero, "no Legendary roll at level 2");
            Assert.That(table.RarityWeight(4, SkillRarity.Legendary), Is.GreaterThan(0f));
            float Share(int level, SkillRarity r) =>
                table.RarityWeight(level, r) / System.Enum.GetValues(typeof(SkillRarity)).Cast<SkillRarity>().Sum(x => table.RarityWeight(level, x));
            Assert.That(Share(10, SkillRarity.Legendary), Is.GreaterThan(Share(4, SkillRarity.Legendary)), "higher levels raise Legendary odds");
            Assert.That(Share(10, SkillRarity.Epic), Is.GreaterThan(Share(4, SkillRarity.Epic)));

            var styles = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillRarityStyles>("Assets/_Project/Data/Skills/SkillRarityStyles.asset");
            Assert.That(styles, Is.Not.Null);
            var all = System.Enum.GetValues(typeof(SkillRarity)).Cast<SkillRarity>().Select(styles.Of).ToArray();
            Assert.That(all.Select(s => s.marks), Is.EqualTo(new[] { 1, 2, 3, 4 }), "gem count per tier");
            Assert.That(all.Select(s => s.label).Distinct().Count(), Is.EqualTo(4));
            Assert.That(all.All(s => s.reveal != null), Is.True, "reveal sound per tier");
            Assert.That(all.Select(s => s.reveal).Distinct().Count(), Is.EqualTo(4), "a different sound per tier");
            Assert.That(all[3].shine && all[3].burst, Is.True, "Legendary shines and bursts");
            Assert.That(all.Take(3).Any(s => s.shine || s.burst), Is.False);
        }
#endif
    }
}
