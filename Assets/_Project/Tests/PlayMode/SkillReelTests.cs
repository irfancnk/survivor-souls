using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
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
    /// Skill pick slot-machine roll (KAN-159): reel math (stop item, position, speed, pool order), and the screen: cards stop
    /// in order at 0.6 / 0.8 / 1.0 s on the offer rolled before the roll (result unchanged), tier reveal on the stop, reel
    /// pool = offerable skills only (no maxed one; chest: owned upgradable), tap skips, picks arm after the last stop +
    /// arm delay, the re-roll path rolls again, ticks are rate limited, the Fast Skill Reveal setting turns it off, no GC
    /// allocation per frame while rolling, and the shipped scene's screen has the roll on.
    /// </summary>
    public sealed class SkillReelTests
    {
        private const float Dt = 1f / 60f;
        private readonly List<Object> _objects = new List<Object>();
        private SkillManager _manager;
        private SkillProgression _table;
        private bool _fastReveal;

        [SetUp]
        public void SetUp()
        {
            _fastReveal = GameSettings.FastSkillReveal;
            GameSettings.FastSkillReveal = false;
            RunState.NewRun(1, 1591);
            _table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(_table, null, null, null, _manager.transform, rngSeed: 159);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GameSettings.FastSkillReveal = _fastReveal;
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

        // ------------------------------------------------------------ Reel math

        [Test]
        public void Reel_PositionRunsFromZeroToTheStopItem_SlowsDownAndStays()
        {
            int stop = SkillReel.StopItem(0.6f, 0.3f, 16f);
            Assert.That(stop, Is.EqualTo(Mathf.RoundToInt(16f * 0.45f)));
            Assert.That(SkillReel.StopItem(0.05f, 0.3f, 1f), Is.EqualTo(2), "every reel passes other skills");
            Assert.That(SkillReel.Position(0f, 0.6f, stop, 0.3f), Is.Zero);
            Assert.That(SkillReel.Position(0.6f, 0.6f, stop, 0.3f), Is.EqualTo(stop));
            Assert.That(SkillReel.Position(5f, 0.6f, stop, 0.3f), Is.EqualTo(stop), "stays on the stop item");
            float last = 0f;
            for (float t = 0f; t <= 0.6f; t += 0.01f)
            {
                float p = SkillReel.Position(t, 0.6f, stop, 0.3f);
                Assert.That(p, Is.GreaterThanOrEqualTo(last - 1e-4f), "never runs backwards");
                Assert.That(p, Is.LessThanOrEqualTo(stop));
                last = p;
            }
            float early = SkillReel.Position(0.1f, 0.6f, stop, 0.3f) - SkillReel.Position(0.05f, 0.6f, stop, 0.3f);
            float late = SkillReel.Position(0.59f, 0.6f, stop, 0.3f) - SkillReel.Position(0.54f, 0.6f, stop, 0.3f);
            Assert.That(late, Is.LessThan(early * 0.5f), "slows down before the stop");
            Assert.That(SkillReel.Speed01(0.1f, 0.6f, 0.3f), Is.EqualTo(1f));
            Assert.That(SkillReel.Speed01(0.45f, 0.6f, 0.3f), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(SkillReel.Speed01(0.6f, 0.6f, 0.3f), Is.Zero);
        }

        [Test]
        public void Reel_PoolIndex_TwoInARowDiffer_AndNoTwinBeforeTheStop()
        {
            for (int seed = 0; seed < 5; seed++)
                for (int item = 0; item < 20; item++)
                    Assert.That(SkillReel.PoolIndex(item, 30, seed, 4), Is.Not.EqualTo(SkillReel.PoolIndex(item + 1, 30, seed, 4)));
            for (int seed = 0; seed < 4; seed++)
                Assert.That(SkillReel.PoolIndex(9, 10, seed, 4, 2), Is.Not.EqualTo(2), "the item before the stop is not the rolled skill");
            Assert.That(SkillReel.PoolIndex(-3, 10, 0, 4, -1), Is.InRange(0, 3));
            Assert.That(SkillReel.PoolIndex(1, 10, 0, 0, -1), Is.EqualTo(-1));
            Assert.That(SkillReel.Bounce(0f), Is.Zero);
            Assert.That(SkillReel.Bounce(1f), Is.Zero);
            Assert.That(SkillReel.Bounce(0.4f), Is.GreaterThan(0f));
        }

        // ------------------------------------------------------------ Screen

#if UNITY_EDITOR
        private SkillChoiceScreen OpenScreen(bool roll = true)
        {
            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var screen = go.AddComponent<SkillChoiceScreen>();
            screen.Setup(_manager, 0.4f, null, roll);
            go.SetActive(true);
            return screen;
        }

        private static float Run(SkillChoiceScreen screen, float seconds, float from = 0f, Action<float> each = null)
        {
            float t = from;
            for (float k = 0f; k < seconds - 1e-4f; k += Dt)
            {
                t += Dt;
                screen.Tick(Dt);
                each?.Invoke(t);
            }
            return t;
        }

        [UnityTest]
        public IEnumerator Roll_StopsInOrder_OnTheRolledOffer_RevealOnStop_RateLimitedTicks()
        {
            _table.Configure(new[]
            {
                Tier(2, 1f, 1f, 1f, 1f, Many("c", SkillRarity.Common, 6).Concat(Many("r", SkillRarity.Rare, 3))
                    .Concat(Many("e", SkillRarity.Epic, 2)).Concat(Many("l", SkillRarity.Legendary, 1)).ToArray()),
            }, pity: 0);
            var screen = OpenScreen();
            yield return null;
            for (int round = 0; round < 6; round++)
            {
                var stops = new List<(int index, float at, SkillDefinition skill)>();
                var reveals = new List<(int index, float at)>();
                var ticks = new List<float>();
                float now = 0f;
                screen.ReelStopped += OnStop;
                screen.CardRevealed += OnReveal;
                screen.ReelTicked += OnTick;
                void OnStop(int i, SkillDefinition s) => stops.Add((i, now, s));
                void OnReveal(int i, SkillDefinition s) => reveals.Add((i, now));
                void OnTick() => ticks.Add(now);

                screen.Present(2, () => { });
                var offer = screen.Options.ToArray();
                Assert.That(offer.Length, Is.EqualTo(3));
                Assert.That(screen.IsRolling, Is.True, "the pick opens rolling");
                var seen = new HashSet<SkillDefinition>();
                Run(screen, 1.45f, 0f, t =>
                {
                    now = t;
                    for (int i = 0; i < 3; i++)
                        if (!screen.IsStopped(i)) seen.Add(screen.ShownSkill(i));
                });
                screen.ReelStopped -= OnStop;
                screen.CardRevealed -= OnReveal;
                screen.ReelTicked -= OnTick;

                Assert.That(stops.Select(s => s.index), Is.EqualTo(new[] { 0, 1, 2 }), "left to right");
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(stops[i].at, Is.EqualTo(screen.ReelStopTime(i)).Within(Dt * 1.5f), $"card {i} stop time");
                    Assert.That(stops[i].skill, Is.SameAs(offer[i]), "lands on the rolled skill");
                    Assert.That(screen.ShownSkill(i), Is.SameAs(offer[i]));
                    Assert.That(reveals[i].index, Is.EqualTo(i));
                    Assert.That(reveals[i].at, Is.EqualTo(stops[i].at).Within(Dt * 0.5f), "tier reveal on the stop");
                }
                Assert.That(screen.ReelStopTime(0), Is.EqualTo(0.6f).Within(1e-4f));
                Assert.That(screen.ReelStopTime(1), Is.EqualTo(0.8f).Within(1e-4f));
                Assert.That(screen.ReelStopTime(2), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(screen.Options, Is.EqualTo(offer), "the roll never changes the offer");
                Assert.That(seen.Count, Is.GreaterThan(3), "reels pass other skills");
                Assert.That(seen.All(s => screen.ReelPool.Contains(s)), Is.True);
                Assert.That(ticks.Count, Is.GreaterThan(5));
                for (int i = 1; i < ticks.Count; i++)
                    Assert.That(ticks[i] - ticks[i - 1], Is.GreaterThanOrEqualTo(0.05f - 1e-4f), "tick rate limit");
                Assert.That(screen.Choose(0), Is.True);
                Run(screen, 0.5f);
            }
        }

        [UnityTest]
        public IEnumerator Roll_PoolHasOnlyOfferableSkills_ChestReelHasOnlyOwnedUpgradable()
        {
            var maxed = Skill("maxed", SkillRarity.Common, 1);
            var owned = Skill("owned", SkillRarity.Rare);
            var full = Skill("full", SkillRarity.Epic, 2);
            var others = Many("o", SkillRarity.Common, 5);
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, others.Concat(new[] { maxed, owned, full }).ToArray()) }, pity: 0);
            _manager.Grant(maxed);
            _manager.Grant(owned);
            _manager.SetLevel(full, 2);
            var screen = OpenScreen();
            yield return null;

            screen.Present(3, () => { });
            Assert.That(screen.ReelPool, Has.No.Member(maxed));
            Assert.That(screen.ReelPool, Has.No.Member(full));
            Assert.That(screen.ReelPool.All(_manager.CanOffer), Is.True);
            Assert.That(screen.ReelPool.Count, Is.EqualTo(_manager.OfferableCount(3)));
            Run(screen, 1.5f);
            Assert.That(screen.Choose(0), Is.True);
            Run(screen, 0.5f);

            Assert.That(screen.PresentUpgrade(new UpgradeOffer(3, 1, false), () => { }), Is.True);
            Assert.That(screen.IsRolling, Is.True, "the chest screen rolls too");
            Assert.That(screen.ReelPool.All(_manager.CanUpgrade), Is.True, "chest reel: owned skills that can level up");
            Assert.That(screen.ReelPool, Has.No.Member(maxed));
            Run(screen, 1.1f);
            for (int i = 0; i < screen.Options.Count; i++) Assert.That(screen.ShownSkill(i), Is.SameAs(screen.Options[i]));
        }

        [UnityTest]
        public IEnumerator Roll_TapSkips_PickArmsAfterLastStopPlusArmDelay()
        {
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("s", SkillRarity.Common, 8)) }, pity: 0);
            var screen = OpenScreen();
            yield return null;

            // No skip: arms 0.4 s after the last stop (1.0 s).
            screen.Present(2, () => { });
            Run(screen, 1.05f);
            Assert.That(screen.IsRolling, Is.False);
            Assert.That(screen.IsArmed, Is.False);
            Assert.That(screen.Choose(1), Is.False, "not armed yet: last stop + arm delay");
            Run(screen, 0.3f);
            Assert.That(screen.Choose(1), Is.False);
            Run(screen, 0.1f);
            Assert.That(screen.IsArmed, Is.True);
            var pick = screen.Options[1];
            Assert.That(screen.Choose(1), Is.True);
            Assert.That(_manager.GetLevel(pick), Is.EqualTo(1));
            Run(screen, 0.5f);

            // Skip: a tap at 0.25 s lands every reel at once and picks nothing.
            var picked = new List<SkillDefinition>();
            screen.Picked += picked.Add;
            screen.Present(3, () => { });
            var offer = screen.Options.ToArray();
            Run(screen, 0.25f);
            Assert.That(screen.IsRolling, Is.True);
            Assert.That(screen.Choose(0), Is.False, "the tap only stops the reels");
            Assert.That(screen.IsRolling, Is.False);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(screen.IsStopped(i), Is.True);
                Assert.That(screen.ShownSkill(i), Is.SameAs(offer[i]));
            }
            Assert.That(picked, Is.Empty);
            Assert.That(screen.SkipRoll(), Is.False, "nothing left to skip");
            Run(screen, 0.35f);
            Assert.That(screen.Choose(0), Is.False, "arm delay after the skip");
            Run(screen, 0.1f);
            Assert.That(screen.Choose(0), Is.True);
            Assert.That(picked, Is.EqualTo(new[] { offer[0] }));
        }

        [UnityTest]
        public IEnumerator Roll_PointerTapOnTheScreen_Skips()
        {
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("s", SkillRarity.Common, 8)) }, pity: 0);
            var screen = OpenScreen();
            yield return null;
            screen.Present(2, () => { });
            Run(screen, 0.2f);
            var panel = screen.GetComponent<UIDocument>().rootVisualElement.Q("skill-choice");
            using (var down = PointerDownEvent.GetPooled())
            {
                down.target = panel;
                panel.SendEvent(down);
            }
            Assert.That(screen.IsRolling, Is.False, "a tap anywhere on the pick stops the reels");
        }

        [UnityTest]
        public IEnumerator Roll_RerollPathRollsAgain_RerollWaitsForTheStop()
        {
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("s", SkillRarity.Common, 9)) }, pity: 0, rerolls: 2);
            var screen = OpenScreen();
            yield return null;
            screen.Present(2, () => { });
            Run(screen, 0.5f);
            Assert.That(screen.CanReroll, Is.False, "no re-roll while the reels roll");
            Assert.That(screen.Reroll(), Is.False);
            Run(screen, 0.95f);
            Assert.That(screen.CanReroll, Is.True);
            var first = screen.Options.ToArray();
            Assert.That(screen.Reroll(), Is.True);
            var stops = new List<int>();
            screen.ReelStopped += (i, s) => stops.Add(i);
            Run(screen, 0.2f);
            Assert.That(screen.IsFlipping, Is.False);
            Assert.That(screen.IsRolling, Is.True, "the new cards roll");
            var second = screen.Options.ToArray();
            Assert.That(second.Intersect(first), Is.Empty);
            Run(screen, 1.1f);
            Assert.That(stops, Is.EqualTo(new[] { 0, 1, 2 }));
            for (int i = 0; i < 3; i++) Assert.That(screen.ShownSkill(i), Is.SameAs(second[i]));
            Assert.That(screen.Options, Is.EqualTo(second));
        }

        [UnityTest]
        public IEnumerator FastSkillReveal_TurnsTheRollOff()
        {
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("s", SkillRarity.Common, 6)) }, pity: 0);
            var screen = OpenScreen();
            yield return null;
            GameSettings.FastSkillReveal = true;
            Assert.That(screen.ReelOn, Is.False);
            var reveals = new List<int>();
            int stops = 0;
            screen.CardRevealed += (i, s) => reveals.Add(i);
            screen.ReelStopped += (i, s) => stops++;
            screen.Present(2, () => { });
            Assert.That(screen.IsRolling, Is.False);
            Run(screen, 0.6f);
            Assert.That(reveals, Is.EqualTo(new[] { 0, 1, 2 }), "cards land and reveal as before");
            Assert.That(stops, Is.Zero);
            Assert.That(screen.Choose(2), Is.True, "armed after the usual 0.4 s");
            Run(screen, 0.5f);

            GameSettings.FastSkillReveal = false;
            screen.Present(3, () => { });
            Assert.That(screen.IsRolling, Is.True, "setting off: the next pick rolls");
        }

        [UnityTest]
        public IEnumerator Roll_NoGcAllocationPerFrame()
        {
            _table.Configure(new[] { Tier(2, 1f, 1f, 1f, 1f, Many("s", SkillRarity.Common, 8)) }, pity: 0);
            var screen = OpenScreen();
            yield return null;
            screen.Present(2, () => { }); // warm up: name cache, inline styles
            Run(screen, 1.5f);
            screen.Choose(0);
            Run(screen, 0.5f);
            screen.Present(3, () => { });
            Run(screen, 0.1f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Run(screen, 0.4f);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(screen.IsRolling, Is.True);
            Assert.That(bytes, Is.Zero, "rolling reels allocate nothing per frame");
        }

        [Test]
        public void ShippedScene_PickHasTheRollOn()
        {
            var scene = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/_Project/Scenes/Game.unity");
            Assert.That(scene, Is.Not.Null);
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Scenes/Game.unity");
            Assert.That(yaml, Does.Contain("reelStops:"), "SkillChoiceScreen in the generated scene carries the roll timings");
            Assert.That(yaml, Does.Contain("reel: 1"));
            Assert.That(yaml, Does.Contain("reelTicks:"), "GameAudio has the tick clips");
        }
#endif
    }
}
