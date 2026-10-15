using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Opening Reliquary chests (KAN-73 = KAN-50.4): upgrade card roll (owned + upgradable only, never maxed, one card when
    /// only one can level, rotation so the same skill is not on every chest), walk-in pickup (landing delay, radius, dead
    /// Knight), open animation (off the ground, no expiry, frees a cap slot, pooled after), no drops while nothing can level
    /// up, the upgrade screen through the level-up flow (pause, pick raises by the chest's levels, Rare +2 capped), one
    /// screen at a time with level-up first, queue cap 3 + fallback (heal + wisps), a chest whose turn finds nothing to
    /// upgrade, another pause holder, death, the card look on the real HUD, shipped assets and the Game scene end to end.
    /// </summary>
    public sealed class ChestUpgradeTests
    {
        private const string ConfigPath = "Assets/_Project/Data/Chests/ChestDropConfig.asset";
        private const string PrefabPath = "Assets/_Project/Prefabs/Chests/Chest_Reliquary.prefab";
        private const int TestRealm = 93; // the Game scene test never touches real Realm progress
        private const string LastRealmKey = "progress.lastRealm";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private PlayerHealth _health;
        private SkillManager _manager;
        private SkillProgression _table;
        private ChestDropConfig _config;
        private ChestDropper _dropper;
        private XpManager _xp;
        private LevelUpFlow _flow;
        private SkillChoiceScreen _screen;
        private SoulWispField _souls;
        private ChestRewards _rewards;
        private int _savedLastRealm;
        private bool _hadLastRealm;

        [SetUp]
        public void SetUp()
        {
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            Enemy.Walkability = null;
            _player = Track(new GameObject("Player"));
            _player.transform.position = new Vector3(50f, 0f, 50f); // far from every test chest until a test walks in
            _stats = _player.AddComponent<PlayerStats>();
            _health = _player.AddComponent<PlayerHealth>();
            _table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(_table, _stats, null, _health, _player.transform, rngSeed: 73);

            _config = Track(ChestDropConfig.Create());
            _config.SetOpening(radius: 1.1f, delay: 0.3f, open: 0.4f, heal: 0.25f, xp: 30, wisps: 3);
            _dropper = Track(new GameObject("Chests")).AddComponent<ChestDropper>();
            _dropper.Setup(_config, _stats, MakeChestPrefab(), rngSeed: 73, knight: _health, skillManager: _manager);

            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100, 1f, 1);
            var progression = Track(new GameObject("Progression"));
            progression.SetActive(false);
            _xp = progression.AddComponent<XpManager>();
            _xp.Setup(curve);
            _flow = progression.AddComponent<LevelUpFlow>();
            _screen = Track(new GameObject("HUD")).AddComponent<SkillChoiceScreen>(); // no panel: logic only
            _screen.Setup(_manager, armSeconds: 0.4f);
            _flow.Setup(_xp, _health, _screen);
            progression.SetActive(true);

            _souls = Track(new GameObject("Souls")).AddComponent<SoulWispField>();
            _souls.Setup(_xp, null, 64);
            _rewards = _dropper.gameObject.AddComponent<ChestRewards>();
            _rewards.Setup(_dropper, _flow, _manager, _health, _souls);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
            Time.timeScale = 1f;
            Enemy.Walkability = null;
            Enemy.LandMaxZ = float.PositiveInfinity;
            RunProgress.Clear(TestRealm);
            if (_hadLastRealm) PlayerPrefs.SetInt(LastRealmKey, _savedLastRealm);
            else PlayerPrefs.DeleteKey(LastRealmKey);
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        /// <summary>A chest template like the builder's: body with a hinged lid, beam and glow renderers.</summary>
        private ReliquaryChest MakeChestPrefab()
        {
            var root = Track(new GameObject("Chest Template"));
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var lid = new GameObject("Lid");
            lid.transform.SetParent(body.transform, false);
            var beam = new GameObject("Beam").AddComponent<MeshRenderer>();
            beam.transform.SetParent(root.transform, false);
            var glow = new GameObject("Glow").AddComponent<MeshRenderer>();
            glow.transform.SetParent(root.transform, false);
            var chest = root.AddComponent<ReliquaryChest>();
            chest.Setup(body.transform, beam, glow, lid.transform);
            root.SetActive(false);
            return chest;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        /// <summary>Damage +10 / +25 / +50 (3 levels, text per level).</summary>
        private SkillDefinition Laddered(string id)
        {
            var s = Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure(id, id, id + " text", SkillRarity.Common, 1);
            return s.SetLevels(
                new SkillLevelData(id + " L1", Add(StatId.Damage, 10f)),
                new SkillLevelData(id + " L2", Add(StatId.Damage, 25f)),
                new SkillLevelData(id + " L3", Add(StatId.Damage, 50f)));
        }

        private SkillDefinition[] Own(params string[] ids)
        {
            var skills = ids.Select(Laddered).ToArray();
            foreach (var s in skills) Assert.That(_manager.Grant(s), Is.True);
            return skills;
        }

        /// <summary>Drops a chest at the Knight's feet and walks the clock past landing + opening: the chest is opened.</summary>
        private ReliquaryChest OpenAtFeet(ChestTier tier = ChestTier.Light, int choices = 2, int upgrades = 1)
        {
            _player.transform.position = Vector3.zero;
            var chest = _dropper.Drop(Vector3.zero, tier, choices, upgrades);
            Assert.That(chest, Is.Not.Null);
            _dropper.Tick(0.31f); // landed: walk-in pickup starts the open
            Assert.That(chest.IsOpening, Is.True);
            _dropper.Tick(0.41f); // open animation over: Opened
            return chest;
        }

        // ------------------------------------------------------------ Upgrade card roll

        [Test]
        public void RollUpgrades_OnlyOwnedBelowMax_OneCardWhenOnlyOneCanLevel()
        {
            var (a, maxed) = (Laddered("a"), Laddered("maxed"));
            var notOwned = Laddered("new");
            _table.Configure(new[] { new SkillProgression.Tier { fromLevel = 1, unlocks = new[] { a, maxed, notOwned } } });
            _manager.Grant(a);
            _manager.Grant(maxed);
            _manager.Upgrade(maxed, 5);
            Assert.That(_manager.GetLevel(maxed), Is.EqualTo(3));

            var cards = new List<SkillDefinition>();
            for (int i = 0; i < 20; i++)
            {
                Assert.That(_manager.RollUpgrades(3, cards), Is.EqualTo(1), "one card, no empty slots");
                Assert.That(cards, Is.EqualTo(new[] { a }), "never a maxed or a not-owned skill");
            }
            _manager.Upgrade(a, 2);
            Assert.That(_manager.RollUpgrades(3, cards), Is.Zero, "nothing can level up");
            Assert.That(_manager.HasUpgradableSkill(), Is.False);
        }

        [Test]
        public void RollUpgrades_DistinctCards_RotatesSoTheSameSkillIsNotOnEveryChest()
        {
            var owned = Own("a", "b", "c", "d");
            var cards = new List<SkillDefinition>();
            var seen = owned.ToDictionary(s => s, _ => 0);
            for (int i = 0; i < 200; i++)
            {
                Assert.That(_manager.RollUpgrades(2, cards), Is.EqualTo(2));
                Assert.That(cards.Distinct().Count(), Is.EqualTo(2));
                foreach (var s in cards) seen[s]++;
            }
            foreach (var s in owned)
            {
                Assert.That(seen[s], Is.InRange(80, 120), $"{s.Id} shown {seen[s]} of 400 cards (fair share 100)");
                Assert.That(_manager.UpgradeOffersOf(s), Is.EqualTo(seen[s]));
            }
            _manager.ResetRun();
            Assert.That(_manager.UpgradeOffersOf(owned[0]), Is.Zero, "rotation count is run-only");
        }

        [Test]
        public void RollUpgrades_ForcedOfferIsUsedWhenUpgradable()
        {
            var owned = Own("a", "b", "c");
            var cards = new List<SkillDefinition>();
            _manager.ForceNextOffer(owned[2], owned[0]);
            Assert.That(_manager.RollUpgrades(2, cards), Is.EqualTo(2));
            Assert.That(cards, Is.EqualTo(new[] { owned[2], owned[0] }));
        }

        // ------------------------------------------------------------ Pickup and open

        [Test]
        public void WalkIn_OpensOnlyAfterLanding_InsideTheRadius_ThenOpenedAndPooled()
        {
            Own("a");
            int started = 0, opened = 0;
            _dropper.OpenStarted += _ => started++;
            _dropper.Opened += c => { opened++; Assert.That(c.Tier, Is.EqualTo(ChestTier.Light), "contents valid during Opened"); };
            int pooled = _dropper.transform.childCount;

            var chest = _dropper.Drop(Vector3.zero, ChestTier.Light, 2, 1);
            var at = chest.transform.position;
            _player.transform.position = at + new Vector3(1.2f, 0f, 0f); // just outside 1.1 m
            _dropper.Tick(0.5f);
            Assert.That(chest.IsOnGround, Is.True, "outside the radius");

            _player.transform.position = at + new Vector3(0.7f, 3f, 0.7f); // inside on the ground plane (height ignored)
            var fresh = _dropper.Drop(at + new Vector3(0.3f, 0f, 0f), ChestTier.Light, 2, 1);
            _dropper.Tick(0.1f);
            Assert.That(fresh.IsOnGround, Is.True, "just landed: the pop plays first");
            Assert.That(chest.IsOpening, Is.True);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(_dropper.Active, Has.No.Member(chest), "off the ground");
            Assert.That(_dropper.Opening, Has.Member(chest));

            _dropper.Tick(0.2f);
            Assert.That(chest.LidAngle, Is.GreaterThan(30f), "lid swings open");
            Assert.That(opened, Is.Zero);
            _dropper.Tick(0.25f);
            Assert.That(opened, Is.GreaterThanOrEqualTo(1));
            Assert.That(chest.gameObject.activeSelf, Is.False, "back in the pool");
            Assert.That(chest.LidAngle, Is.Zero, "reset for reuse");
            Assert.That(_dropper.OpenedCount, Is.EqualTo(opened));
            Assert.That(_dropper.transform.childCount, Is.EqualTo(pooled), "no new chest objects");
        }

        [Test]
        public void DeadKnight_DoesNotOpen()
        {
            Own("a");
            _player.transform.position = Vector3.zero;
            var chest = _dropper.Drop(Vector3.zero, ChestTier.Light, 2, 1);
            _health.TakeDamage(10000f);
            Assert.That(_health.IsDead, Is.True);
            _dropper.Tick(0.5f);
            Assert.That(chest.IsOnGround, Is.True);
        }

        [Test]
        public void OpeningChest_NeverExpires_AndFreesItsCapSlot()
        {
            Own("a");
            _player.transform.position = Vector3.zero;
            var first = _dropper.Drop(Vector3.zero, ChestTier.Light, 2, 1);
            Assert.That(_dropper.Drop(new Vector3(20f, 0f, 0f), ChestTier.Light, 2, 1), Is.Not.Null);
            Assert.That(_dropper.IsFull, Is.True);
            _dropper.Tick(0.31f);
            Assert.That(first.IsOpening, Is.True);
            Assert.That(_dropper.IsFull, Is.False, "an opening chest does not count against the cap");
            int expired = 0;
            _dropper.Expired += c => { if (c == first) expired++; };
            first.TickOpen(0f, 0.4f);
            for (int i = 0; i < 10; i++) Assert.That(first.Tick(10f), Is.False, "ground timer is off while opening");
            Assert.That(expired, Is.Zero);
        }

        [Test]
        public void NothingToUpgrade_KillsDropNoChest_UntilASkillCanLevel()
        {
            _stats.SetSource(_config, new[] { Add(StatId.ChestChance, 1f), Add(StatId.ChestEliteChance, 1f) }, 1);
            Assert.That(_dropper.IsHunting, Is.True);
            for (int i = 0; i < 20; i++) Assert.That(_dropper.OnKill(new Vector3(i * 3f, 0f, 0f), false), Is.Null);
            _dropper.Tick(200f);
            Assert.That(_dropper.OnKill(Vector3.zero, false), Is.Null, "not even by pity");
            Own("a");
            Assert.That(_dropper.OnKill(Vector3.zero, false), Is.Not.Null);
        }

        // ------------------------------------------------------------ Upgrade screen through the flow

        [Test]
        public void OpenedChest_PausesAndShowsOwnedSkills_PickRaisesByOne_ThenResumes()
        {
            var owned = Own("a", "b", "c");
            var upgrades = new List<(SkillDefinition, int, int)>();
            _screen.Upgraded += (s, a, b) => upgrades.Add((s, a, b));

            OpenAtFeet(choices: 2);
            Assert.That(_flow.PendingUpgrades, Is.EqualTo(1));
            Assert.That(_rewards.QueuedCount, Is.EqualTo(1));
            _flow.Tick(0f);
            Assert.That(GamePause.IsPaused, Is.True);
            Assert.That(_flow.IsShowingUpgrade, Is.True);
            Assert.That(_screen.IsUpgrade, Is.True);
            Assert.That(_screen.Options.Count, Is.EqualTo(2), "the chest's card count");
            Assert.That(_screen.Options, Is.SubsetOf(owned));

            Assert.That(_screen.Choose(0), Is.False, "armed after 0.4 s");
            _screen.Tick(0.45f);
            var pick = _screen.Options[1];
            Assert.That(_screen.Choose(1), Is.True);
            Assert.That(_manager.GetLevel(pick), Is.EqualTo(2), "exactly one level");
            Assert.That(upgrades, Is.EqualTo(new[] { (pick, 1, 2) }));
            Assert.That(owned.Where(s => s != pick).All(s => _manager.GetLevel(s) == 1), Is.True);
            Assert.That(_manager.OwnedSkills.Count, Is.EqualTo(3), "an upgrade never adds a skill");

            _screen.Tick(1f);
            Assert.That(GamePause.IsPaused, Is.False);
            _flow.Tick(1f);
            Assert.That(_flow.IsBusy, Is.False);
        }

        [Test]
        public void RareChest_GivesTwoLevels_CappedAtMax()
        {
            var (low, high) = (Laddered("low"), Laddered("high"));
            _manager.Grant(low);
            _manager.Grant(high);
            _manager.Upgrade(high);
            Assert.That(OpenAtFeet(ChestTier.Rare, choices: 3, upgrades: 2).Tier, Is.EqualTo(ChestTier.Rare));
            _flow.Tick(0f);
            Assert.That(_screen.Subtitle, Is.Null, "no panel in this test; the text is checked on the real HUD");
            Assert.That(_screen.Options.Count, Is.EqualTo(2));
            _screen.Tick(0.45f);
            int iLow = _screen.Options.ToList().IndexOf(low);
            Assert.That(_screen.Choose(iLow), Is.True);
            Assert.That(_manager.GetLevel(low), Is.EqualTo(3), "1 + 2");
            _screen.Tick(1f);
            _flow.Tick(1f);

            OpenAtFeet(ChestTier.Rare, choices: 3, upgrades: 2);
            _flow.Tick(0f);
            Assert.That(_screen.Options, Is.EqualTo(new[] { high }), "low is maxed now");
            _screen.Tick(0.45f);
            Assert.That(_screen.Choose(0), Is.True);
            Assert.That(_manager.GetLevel(high), Is.EqualTo(3), "2 + 2 capped at max 3");
        }

        [Test]
        public void LevelUpFirst_ThenChest_NeverTwoScreensAtOnce()
        {
            Own("a", "b");
            var order = new List<string>();
            _flow.Started += l => order.Add("level " + l);
            _flow.UpgradeStarted += _ => order.Add("chest");

            var fresh = Laddered("fresh");
            OpenAtFeet();
            _manager.ForceNextOffer(fresh); // the level-up's card (the table has no tiers)
            _xp.AddXp(100); // a level-up arrives in the same frame
            _flow.Tick(0f);
            Assert.That(_flow.IsShowingUpgrade, Is.False, "level-up first");
            _flow.Tick(1f); // celebration over: level cards
            Assert.That(_screen.IsShowing, Is.True);
            Assert.That(_screen.IsUpgrade, Is.False);
            _screen.Tick(0.45f);
            Assert.That(_screen.Choose(0), Is.True);
            _screen.Tick(1f);
            Assert.That(GamePause.IsPaused, Is.True, "pause held: the chest is still waiting");
            Assert.That(_screen.IsShowing, Is.False);
            _flow.Tick(0.25f); // gap
            Assert.That(_screen.IsUpgrade, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "level 2", "chest" }));
        }

        [Test]
        public void PileOfChests_OpensInSequence_QueueCapThree_ExtraGivesFallback()
        {
            Own("a", "b", "c");
            _health.TakeDamage(200f);
            float hurt = _health.Current;
            for (int i = 0; i < 4; i++) OpenAtFeet();
            Assert.That(_flow.PendingUpgrades, Is.EqualTo(LevelUpFlow.UpgradeQueueCap));
            Assert.That(_rewards.FallbackCount, Is.EqualTo(1), "the 4th chest");
            Assert.That(_health.Current, Is.EqualTo(hurt + _health.Max * 0.25f).Within(0.01f), "fallback heals 25 %");
            Assert.That(_souls.Count, Is.EqualTo(3), "fallback XP as 3 wisps");

            int screens = 0;
            _flow.UpgradeStarted += _ => screens++;
            for (int n = 0; n < 3; n++)
            {
                _flow.Tick(0.25f);
                Assert.That(_screen.IsUpgrade, Is.True, $"screen {n + 1}");
                Assert.That(screens, Is.EqualTo(n + 1), "one at a time");
                _screen.Tick(0.45f);
                Assert.That(_screen.Choose(0), Is.True);
                _screen.Tick(1f);
                Assert.That(GamePause.IsPaused, Is.EqualTo(n < 2), "pause held until the last chest");
            }
            _flow.Tick(1f);
            Assert.That(_flow.IsBusy, Is.False);
            Assert.That(_manager.OwnedSkills.Sum(o => o.Level), Is.EqualTo(3 + 3));
        }

        [Test]
        public void NothingUpgradable_OpenedChestGivesFallback_NoScreen()
        {
            var a = Own("a")[0];
            _manager.Upgrade(a, 2);
            _health.TakeDamage(300f);
            float hurt = _health.Current;
            OpenAtFeet(); // Drop skips the roll: as if it was already on the ground
            Assert.That(_flow.PendingUpgrades, Is.Zero);
            Assert.That(_rewards.FallbackCount, Is.EqualTo(1));
            Assert.That(_health.Current, Is.EqualTo(hurt + 100f).Within(0.01f));
            Assert.That(_souls.Count, Is.EqualTo(3));
            _flow.Tick(1f);
            Assert.That(GamePause.IsPaused, Is.False);
        }

        [Test]
        public void QueuedChest_FindsNothingWhenItsTurnComes_FallbackAndResume()
        {
            var a = Own("a")[0];
            OpenAtFeet();
            Assert.That(_flow.PendingUpgrades, Is.EqualTo(1));
            _manager.Upgrade(a, 2); // maxed by something else before the screen opened
            _flow.Tick(0f);
            Assert.That(_screen.IsShowing, Is.False);
            Assert.That(_rewards.FallbackCount, Is.EqualTo(1));
            Assert.That(GamePause.IsPaused, Is.False, "nothing held");
            _flow.Tick(1f);
            Assert.That(_flow.IsBusy, Is.False);
        }

        [Test]
        public void QueuedChest_WaitsWhileSomethingElseHoldsThePause()
        {
            Own("a");
            OpenAtFeet();
            GamePause.Acquire(); // e.g. the Chamber fade or the victory screen
            _flow.Tick(1f);
            Assert.That(_screen.IsShowing, Is.False);
            Assert.That(_flow.PendingUpgrades, Is.EqualTo(1));
            GamePause.Release();
            _flow.Tick(0f);
            Assert.That(_screen.IsUpgrade, Is.True);
        }

        [Test]
        public void Death_DropsQueuedChests()
        {
            Own("a");
            OpenAtFeet();
            _health.TakeDamage(10000f);
            Assert.That(_flow.PendingUpgrades, Is.Zero);
            Assert.That(_flow.EnqueueUpgrade(new UpgradeOffer(2, 1, false)), Is.False, "dead Knight queues nothing");
            _flow.Tick(1f);
            Assert.That(_screen.IsShowing, Is.False);
        }

        [Test]
        public void BadgeAndSubtitleTexts()
        {
            Assert.That(SkillChoiceScreen.BadgeText(1, 2), Is.EqualTo("LV 1 → 3"));
            Assert.That(SkillChoiceScreen.BadgeText(2, 1), Is.EqualTo("LV 2 → 3"));
            Assert.That(SkillChoiceScreen.BadgeText(0, 2), Is.EqualTo("NEW"));
            Assert.That(SkillChoiceScreen.UpgradeSubtitle(1), Is.EqualTo("UPGRADE A SKILL"));
            Assert.That(SkillChoiceScreen.UpgradeSubtitle(2), Is.EqualTo("UPGRADE A SKILL +2 LEVELS"));
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CardScreen_RareChestLook_ThenBackToLevelUpLook()
        {
            var (a, b) = (Laddered("a"), Laddered("b"));
            _table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, unlocks = new[] { a, b } } });
            _manager.Grant(a);
            _manager.Grant(b);
            _manager.Upgrade(b);

            var go = Track(new GameObject("HUD (panel)"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var screen = go.AddComponent<SkillChoiceScreen>();
            screen.Setup(_manager, 0f);
            go.SetActive(true);
            yield return null;

            _manager.ForceNextOffer(a, b);
            Assert.That(screen.PresentUpgrade(new UpgradeOffer(3, 2, true), () => { }), Is.True);
            var root = doc.rootVisualElement;
            var panel = root.Q("skill-choice");
            Assert.That(screen.Title, Is.EqualTo("RARE RELIQUARY"));
            Assert.That(screen.Subtitle, Is.EqualTo("UPGRADE A SKILL +2 LEVELS"));
            Assert.That(panel.ClassListContains("skills--chest"), Is.True);
            Assert.That(panel.ClassListContains("skills--chest-rare"), Is.True);
            Assert.That(panel.ClassListContains("hud-hidden"), Is.False);

            var cardA = root.Q<Button>("skill-card-0");
            var cardB = root.Q<Button>("skill-card-1");
            Assert.That(root.Q<Button>("skill-card-2").style.display.value, Is.EqualTo(DisplayStyle.None), "two skills, two cards");
            Assert.That(cardA.Q<Label>(className: "skill-card__badge").text, Is.EqualTo("LV 1 → 3"));
            Assert.That(cardA.Q<Label>(className: "skill-card__desc").text, Is.EqualTo("a L3"), "text of the level the pick gives");
            Assert.That(cardA.Q(className: "skill-card__pips").Children().Count(p => p.ClassListContains("skill-pip--next")), Is.EqualTo(2));
            Assert.That(cardB.Q<Label>(className: "skill-card__badge").text, Is.EqualTo("LV 2 → 3"), "capped at max");
            Assert.That(cardB.Q(className: "skill-card__pips").Children().Count(p => p.ClassListContains("skill-pip--next")), Is.EqualTo(1));

            screen.Tick(0.05f);
            Assert.That(screen.Choose(0), Is.True);
            Assert.That(_manager.GetLevel(a), Is.EqualTo(3));
            screen.Tick(1f);
            Assert.That(panel.ClassListContains("hud-hidden"), Is.True);

            var c = Laddered("c");
            _manager.ForceNextOffer(c);
            screen.Present(3, () => { });
            Assert.That(screen.Title, Is.EqualTo("LEVEL 3"));
            Assert.That(screen.Subtitle, Is.EqualTo(SkillChoiceScreen.BoonSubtitle));
            Assert.That(panel.ClassListContains("skills--chest"), Is.False, "level-up look again");
            Assert.That(panel.ClassListContains("skills--chest-rare"), Is.False);
            Assert.That(root.Q<Button>("skill-card-0").Q<Label>(className: "skill-card__badge").text, Is.EqualTo("NEW"));
        }

        [Test]
        public void ShippedConfigAndPrefab_PickupRules_HingedLid()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<ChestDropConfig>(ConfigPath);
            Assert.That(config, Is.Not.Null, ConfigPath);
            Assert.That(config.PickupRadius, Is.EqualTo(1.1f).Within(1e-4f));
            Assert.That(config.OpenTime, Is.EqualTo(0.4f).Within(1e-4f), "light chest opens in 0.4 s");
            Assert.That(config.FallbackHeal, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(config.FallbackXp, Is.GreaterThan(0));

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<ReliquaryChest>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var lid = prefab.transform.Find("Body/Lid");
            Assert.That(lid, Is.Not.Null, "lid is its own part under the body");
            Assert.That(lid.localPosition.z, Is.GreaterThan(0.1f), "hinge at the back (lock faces -Z)");
            Assert.That(lid.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
        }
#endif

        [UnityTest]
        public IEnumerator GameScene_WalkIntoChest_UpgradeScreenOpens_PickRaisesTheSkill()
        {
            // The fixture's objects listen to static events: keep them out of the Game scene.
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();

            RunState.NewRun(TestRealm, 7373);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var dropper = Object.FindAnyObjectByType<ChestDropper>();
            var skills = Object.FindAnyObjectByType<SkillManager>();
            var flow = Object.FindAnyObjectByType<LevelUpFlow>();
            var screen = Object.FindAnyObjectByType<SkillChoiceScreen>();
            var rewards = Object.FindAnyObjectByType<ChestRewards>();
            Assert.That(rewards, Is.Not.Null, "ChestRewards on the Chests object");
            Assert.That(rewards.gameObject, Is.SameAs(dropper.gameObject));

            foreach (var c in Object.FindObjectsByType<ContactDamageReceiver>()) c.enabled = false;
            foreach (var s in Object.FindObjectsByType<AutoShooter>()) s.enabled = false; // no kills: no level-up screen first
            Assert.That(skills.Grant("sharpened_bolts"), Is.True);
            var knight = skills.Stats.transform;
            var chest = dropper.Drop(knight.position, ChestTier.Light, 2, 1);
            Assert.That(chest, Is.Not.Null);

            float until = Time.realtimeSinceStartup + 4f;
            while (!screen.IsUpgrade && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(screen.IsUpgrade, Is.True, "walk-in pickup -> open -> upgrade screen");
            Assert.That(GamePause.IsPaused, Is.True);
            Assert.That(flow.IsShowingUpgrade, Is.True);
            Assert.That(screen.Title, Is.EqualTo("RELIQUARY"));
            Assert.That(screen.Options.All(s => skills.GetLevel(s) > 0), Is.True, "owned skills only");

            screen.Tick(1.5f); // the scene's pick rolls first (KAN-159): last reel stops at 1 s, picks arm 0.4 s later
            var pick = screen.Options[0];
            int before = skills.GetLevel(pick);
            Assert.That(screen.Choose(0), Is.True);
            Assert.That(skills.GetLevel(pick), Is.EqualTo(before + 1));
            screen.Tick(1f);
            Assert.That(GamePause.IsPaused, Is.False);

            var empty = SceneManager.CreateScene("ChestUpgradeEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
