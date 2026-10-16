using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Reliquary Hunter chest drops (KAN-72 = KAN-50.3): the shipped skill levels (chances, cards, Rare every 5th), no
    /// drops without the skill, drop statistics (40 x 1000 simulated kills per level within 10 % of the target), the 90 s
    /// pity, the ground cap, lifetime with the warning blink and expiry, Rare chest counting, real kill events (normal vs
    /// elite chance), the offer gate (not offered before an upgradable skill is owned), shipped assets and the Game scene.
    /// </summary>
    public sealed class ChestDropTests
    {
        private const string SkillPath = "Assets/_Project/Data/Skills/Skill_ReliquaryHunter.asset";
        private const string ConfigPath = "Assets/_Project/Data/Chests/ChestDropConfig.asset";
        private const string PrefabPath = "Assets/_Project/Prefabs/Chests/Chest_Reliquary.prefab";
        private const string ProgressionPath = "Assets/_Project/Data/Skills/SkillProgression_Run.asset";
        private const int TestRealm = 94; // the Game scene test never touches real Realm progress
        private const string LastRealmKey = "progress.lastRealm";

        private readonly List<Object> _objects = new List<Object>();
        private PlayerStats _stats;
        private ChestDropper _dropper;
        private ChestDropConfig _config;
        private ReliquaryChest _prefab;
        private int _savedLastRealm;
        private bool _hadLastRealm;

        [SetUp]
        public void SetUp()
        {
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            _stats = Track(new GameObject("Player")).AddComponent<PlayerStats>();
            _config = Track(ChestDropConfig.Create());
            _prefab = MakeChestPrefab();
            _dropper = Track(new GameObject("Chests")).AddComponent<ChestDropper>();
            _dropper.Setup(_config, _stats, _prefab, rngSeed: 72);
            Enemy.Walkability = null;
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

        /// <summary>A chest template like the builder's: body, beam and glow renderers (no materials needed).</summary>
        private ReliquaryChest MakeChestPrefab()
        {
            var root = Track(new GameObject("Chest Template"));
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var beam = new GameObject("Beam").AddComponent<MeshRenderer>();
            beam.transform.SetParent(root.transform, false);
            var glow = new GameObject("Glow").AddComponent<MeshRenderer>();
            glow.transform.SetParent(root.transform, false);
            var chest = root.AddComponent<ReliquaryChest>();
            chest.Setup(body.transform, beam, glow);
            root.SetActive(false);
            return chest;
        }

        private static SkillDefinition Shipped()
        {
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(SkillPath);
            Assert.That(skill, Is.Not.Null, SkillPath);
            return skill;
        }

        /// <summary>Puts the skill's level on the stat sheet, the way SkillManager does.</summary>
        private void Hunt(int level) => Shipped().ApplyLevel(_stats, level);

        /// <summary>Any chance values (owner = the config asset, a stand-in source).</summary>
        private void Chances(float normal, float elite, params StatModifier[] more)
        {
            var mods = new List<StatModifier>
            {
                new StatModifier(StatId.ChestChance, ModifierKind.Flat, normal),
                new StatModifier(StatId.ChestEliteChance, ModifierKind.Flat, elite),
            };
            mods.AddRange(more);
            _stats.SetSource(_config, mods.ToArray(), 1);
        }

        private void Collect(ReliquaryChest chest) => Assert.That(_dropper.Remove(chest), Is.True);

        // ------------------------------------------------------------ Skill data

        [Test]
        public void ShippedSkill_LevelsSetChances_Cards_AndRareEveryFifth()
        {
            var skill = Shipped();
            Assert.That(skill.Rarity, Is.EqualTo(SkillRarity.Epic));
            Assert.That(skill.MaxLevel, Is.EqualTo(5));
            Assert.That(skill.RequiresUpgradableSkill, Is.True);
            Assert.That(skill.EffectPrefab, Is.Null, "pure data: the dropper reads the stats");

            var expect = new[] { (0.01f, 0.08f, 2, 0), (0.015f, 0.10f, 2, 0), (0.02f, 0.12f, 3, 0), (0.025f, 0.14f, 3, 0), (0.03f, 0.16f, 3, 5) };
            Assert.That(_dropper.IsHunting, Is.False);
            for (int level = 1; level <= 5; level++)
            {
                Hunt(level);
                var (normal, elite, cards, rare) = expect[level - 1];
                Assert.That(_dropper.Chance, Is.EqualTo(normal).Within(1e-5f), $"L{level} chance");
                Assert.That(_dropper.EliteChance, Is.EqualTo(elite).Within(1e-5f), $"L{level} elite chance");
                Assert.That(_dropper.Choices, Is.EqualTo(cards), $"L{level} cards");
                Assert.That(_dropper.RareEvery, Is.EqualTo(rare), $"L{level} rare every");
            }
            Hunt(0);
            Assert.That(_dropper.IsHunting, Is.False, "level 0 removes the skill");
        }

        [Test]
        public void WithoutTheSkill_KillsDropNothing_AndThePityClockStands()
        {
            for (int i = 0; i < 200; i++) Assert.That(_dropper.OnKill(Vector3.zero, i % 3 == 0), Is.Null);
            _dropper.Tick(500f);
            Assert.That(_dropper.SinceLastDrop, Is.EqualTo(0f));
            Assert.That(_dropper.OnKill(Vector3.zero, true), Is.Null, "no pity without the skill");
            Assert.That(_dropper.Active, Is.Empty);
            Assert.That(_dropper.Simulate(1000, 0.2f, 1f, 1).Drops, Is.EqualTo(0));
        }

        // ------------------------------------------------------------ Statistics

        [Test]
        public void Statistics_ThousandKillRuns_HitTheTargetRateWithinTenPercent([Values(1, 2, 3, 4, 5)] int level)
        {
            Hunt(level);
            const float eliteShare = 0.1f;
            const int runs = 40;
            float target = (1f - eliteShare) * _dropper.Chance + eliteShare * _dropper.EliteChance;
            int drops = 0, kills = 0;
            for (int seed = 1; seed <= runs; seed++)
            {
                var run = _dropper.Simulate(1000, eliteShare, 0f, seed * 7919); // pity off: chance only
                Assert.That(run.Kills, Is.EqualTo(1000));
                Assert.That(run.PityDrops, Is.EqualTo(0));
                drops += run.Drops;
                kills += run.Kills;
            }
            float rate = drops / (float)kills;
            Debug.Log($"[ChestDropTests] L{level}: {rate:P2} per kill over {kills} kills, target {target:P2}");
            Assert.That(rate, Is.EqualTo(target).Within(target * 0.1f), $"L{level} per-kill rate");
            Assert.That(_dropper.Active, Is.Empty, "simulation spawns nothing");
        }

        [Test]
        public void Statistics_PacedRun_PityKeepsAtLeastOneChestPerNinetySeconds()
        {
            Hunt(1);
            // A slow Chamber: one kill every 2 s, no elites, 1000 kills = 33 min.
            var run = _dropper.Simulate(1000, 0f, 2f, 99);
            Debug.Log($"[ChestDropTests] {run}");
            Assert.That(run.PerMinute, Is.GreaterThanOrEqualTo(60f / _config.PitySeconds - 0.01f));
            Assert.That(run.PityDrops, Is.GreaterThan(0));
            Assert.That(run.Seconds, Is.EqualTo(2000f).Within(0.01f));
        }

        // ------------------------------------------------------------ Pity, cap, lifetime

        [Test]
        public void Pity_AfterNinetySecondsTheNextKillAlwaysDrops_ThenTheClockRestarts()
        {
            Chances(1e-9f, 1e-9f);
            _dropper.Tick(89f);
            Assert.That(_dropper.OnKill(Vector3.zero, false), Is.Null, "89 s: no pity yet");
            _dropper.Tick(1.5f);
            var chest = _dropper.OnKill(new Vector3(3f, 0f, 4f), false);
            Assert.That(chest, Is.Not.Null, "90 s: pity");
            Assert.That(_dropper.PityDrops, Is.EqualTo(1));
            Assert.That(_dropper.SinceLastDrop, Is.EqualTo(0f));
            Assert.That(_dropper.OnKill(Vector3.zero, false), Is.Null, "clock restarted");

            // Paced: nothing but pity at a near-zero chance = one chest every 90 s of kills.
            var run = _dropper.Simulate(900, 0f, 1f, 5);
            Assert.That(run.Drops, Is.EqualTo(10));
            Assert.That(run.PityDrops, Is.EqualTo(10));
        }

        [Test]
        public void Cap_TwoChestsOnTheGround_KillsWhileFullDropNothing_PityWaits()
        {
            _config = Track(ChestDropConfig.Create(life: 300f)); // chests outlive the 90 s pity wait below
            _dropper.Setup(_config, _stats, _prefab, rngSeed: 73);
            Chances(1f, 1f);
            var a = _dropper.OnKill(Vector3.zero, false);
            var b = _dropper.OnKill(Vector3.right * 4f, false);
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(_dropper.IsFull, Is.True);
            Assert.That(_dropper.OnKill(Vector3.forward * 4f, false), Is.Null);
            Assert.That(_dropper.DropNow(Vector3.zero), Is.Null, "debug drop respects the cap too");
            Assert.That(_dropper.Active.Count, Is.EqualTo(2));

            Chances(1e-9f, 1e-9f);
            _dropper.Tick(120f);
            Assert.That(_dropper.OnKill(Vector3.zero, false), Is.Null, "full: even pity waits");
            Collect(a);
            Assert.That(_dropper.OnKill(Vector3.zero, false), Is.Not.Null, "a slot opened: the waiting pity drops");
        }

        [Test]
        public void Lifetime_WarningBlinkInTheLastFiveSeconds_ThenExpires_AndThePoolReusesIt()
        {
            Chances(1f, 1f);
            var expired = new List<ReliquaryChest>();
            _dropper.Expired += expired.Add;
            var chest = _dropper.OnKill(Vector3.zero, false);
            Assert.That(chest.gameObject.activeSelf, Is.True);
            Assert.That(chest.Lifetime, Is.EqualTo(40f));

            _dropper.Tick(34.9f);
            Assert.That(chest.IsWarning, Is.False);
            Assert.That(chest.Brightness, Is.GreaterThanOrEqualTo(0.6f), "calm glow");
            _dropper.Tick(0.2f);
            Assert.That(chest.IsWarning, Is.True);
            float lo = 1f, hi = 0f;
            for (int i = 0; i < 40; i++)
            {
                _dropper.Tick(0.1f);
                lo = Mathf.Min(lo, chest.Brightness);
                hi = Mathf.Max(hi, chest.Brightness);
            }
            Assert.That(hi - lo, Is.GreaterThan(0.5f), "blinks");
            Assert.That(expired, Is.Empty);

            _dropper.Tick(1f);
            Assert.That(expired, Is.EqualTo(new[] { chest }));
            Assert.That(_dropper.ExpiredCount, Is.EqualTo(1));
            Assert.That(_dropper.Active, Is.Empty);
            Assert.That(chest.gameObject.activeSelf, Is.False);
            Assert.That(chest.IsOnGround, Is.False);

            var again = _dropper.OnKill(Vector3.zero, false);
            Assert.That(again, Is.SameAs(chest), "pooled");
            Assert.That(again.Age, Is.EqualTo(0f));
        }

        [Test]
        public void ScaledTime_ZeroDeltaFreezesChestsAndPity()
        {
            Chances(0.5f, 0.5f);
            _dropper.Tick(10f);
            var chest = _dropper.DropNow(Vector3.zero);
            for (int i = 0; i < 10; i++) _dropper.Tick(0f); // paused (level-up cards)
            Assert.That(chest.Age, Is.EqualTo(0f));
            Assert.That(_dropper.SinceLastDrop, Is.EqualTo(0f));
        }

        [Test]
        public void LevelFive_EveryFifthSkillChestIsRare_WithTwoUpgrades_AndThreeCards()
        {
            Hunt(5);
            Chances(1f, 1f); // on top of L5: guaranteed rolls
            var tiers = new List<ChestTier>();
            for (int i = 0; i < 10; i++)
            {
                var chest = _dropper.OnKill(Vector3.zero, false);
                tiers.Add(chest.Tier);
                Assert.That(chest.Choices, Is.EqualTo(3));
                Assert.That(chest.Upgrades, Is.EqualTo(chest.Tier == ChestTier.Rare ? 2 : 1));
                Collect(chest);
            }
            Assert.That(tiers.Select((t, i) => (t, i)).Where(x => x.t == ChestTier.Rare).Select(x => x.i + 1), Is.EqualTo(new[] { 5, 10 }));
            Assert.That(ChestDropper.NextSkillChestTier(3, 0), Is.EqualTo(ChestTier.Light), "0 = never rare");
        }

        // ------------------------------------------------------------ Real kills

        [Test]
        public void RealKills_NormalAndEliteChance_ChestLandsNearTheBody()
        {
            Chances(0f, 1f); // elites only
            var dropped = new List<ReliquaryChest>();
            _dropper.Dropped += dropped.Add;

            var zombie = Track(new GameObject("Zombie")).AddComponent<Enemy>(); // 10 XP: normal
            zombie.Spawn(new Vector3(6f, 0f, 2f), null, null);
            zombie.TakeDamage(1e6f, Vector3.forward);
            Assert.That(dropped, Is.Empty, "normal kill rolls the normal chance (0)");

            var brute = Track(new GameObject("Brute")).AddComponent<Enemy>();
            var so = new UnityEditor.SerializedObject(brute);
            so.FindProperty("xpValue").intValue = 25;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_config.IsElite(brute), Is.True);
            brute.Spawn(new Vector3(-5f, 0f, 8f), null, null);
            brute.TakeDamage(1e6f, Vector3.forward);
            Assert.That(dropped.Count, Is.EqualTo(1), "elite kill rolls the elite chance (1)");
            var p = dropped[0].transform.position;
            Assert.That(Vector3.Distance(p, new Vector3(-5f, 0f, 8f)), Is.LessThanOrEqualTo(_config.Scatter + 0.01f));
            Assert.That(dropped[0].Tier, Is.EqualTo(ChestTier.Light));

            _dropper.enabled = false;
            var other = Track(new GameObject("Brute 2")).AddComponent<Enemy>();
            var so2 = new UnityEditor.SerializedObject(other);
            so2.FindProperty("xpValue").intValue = 25;
            so2.ApplyModifiedPropertiesWithoutUndo();
            other.Spawn(Vector3.zero, null, null);
            other.TakeDamage(1e6f, Vector3.forward);
            Assert.That(dropped.Count, Is.EqualTo(1), "disabled dropper hears nothing");
        }

        // ------------------------------------------------------------ Offer gate

        [Test]
        public void OfferGate_NotOfferedUntilAnotherOwnedSkillCanLevelUp()
        {
            var hunter = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("hunter", "Hunter", "chests", SkillRarity.Common, 5, new StatModifier(StatId.ChestChance, ModifierKind.Flat, 0.01f))
                .SetRequiresUpgradableSkill(true);
            var small = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("small", "Small", "one level", SkillRarity.Common, 1, new StatModifier(StatId.Damage, ModifierKind.Flat, 1f));
            var ladder = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("ladder", "Ladder", "three levels", SkillRarity.Common, 3, new StatModifier(StatId.Damage, ModifierKind.Flat, 1f));
            var table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, common = 1f, rare = 0f, epic = 0f, unlocks = new[] { hunter, small, ladder } } },
                pity: 0);
            var manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            manager.Setup(table, _stats, null, null, _stats.transform, rngSeed: 3);
            var offer = new List<SkillDefinition>();

            Assert.That(manager.CanOffer(hunter), Is.False, "owns nothing");
            Assert.That(manager.CanTake(hunter), Is.True, "a direct grant (dev) still works");
            manager.Roll(3, 2, offer);
            Assert.That(offer, Has.No.Member(hunter));

            manager.Grant(small); // level 1 of 1: maxed, nothing a chest could raise
            Assert.That(manager.HasUpgradableSkill(), Is.False);
            for (int i = 0; i < 20; i++)
            {
                manager.Roll(3, 2, offer);
                Assert.That(offer, Has.No.Member(hunter), "only a maxed skill owned");
            }

            manager.Grant(ladder); // 1 of 3: upgradable
            Assert.That(manager.CanOffer(hunter), Is.True);
            manager.Roll(3, 2, offer);
            Assert.That(offer, Has.Member(hunter));

            manager.Grant(hunter);
            manager.Grant(ladder);
            manager.Grant(ladder); // ladder maxed: the hunter itself is owned, so its own upgrades stay on offer
            Assert.That(manager.HasUpgradableSkill(hunter), Is.False);
            Assert.That(manager.CanOffer(hunter), Is.True);

            manager.ResetRun();
            manager.ForceNextOffer(hunter); // dev offers skip the gate
            manager.Roll(3, 2, offer);
            Assert.That(offer, Is.EqualTo(new[] { hunter }));
        }

        // ------------------------------------------------------------ Shipped data and scene

        [Test]
        public void ShippedAssets_ConfigDefaults_PrefabWiring_SkillInTheLevelFourTier()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<ChestDropConfig>(ConfigPath);
            Assert.That(config, Is.Not.Null, ConfigPath);
            Assert.That(config.PitySeconds, Is.EqualTo(60f), "KAN-113: retuned for 2 minute Chambers");
            Assert.That(config.MaxOnGround, Is.EqualTo(2));
            Assert.That(config.Lifetime, Is.EqualTo(40f));
            Assert.That(config.WarningTime, Is.EqualTo(5f));
            Assert.That(config.BaseChoices, Is.EqualTo(2));
            Assert.That(config.EliteMinXp, Is.EqualTo(25));

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<ReliquaryChest>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var so = new UnityEditor.SerializedObject(prefab);
            foreach (var field in new[] { "body", "beam", "glow" })
                Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                Assert.That(r.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off), r.name);

            var progression = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            var tier = progression.Tiers.Single(t => t.fromLevel == 4);
            Assert.That(tier.unlocks, Has.Member(Shipped()));
            Assert.That(progression.FindSkill("reliquary_hunter"), Is.SameAs(Shipped()));
        }

        [UnityTest]
        public IEnumerator GameScene_HasTheDropper_GrantedSkillMakesKillsDropChests()
        {
            RunState.NewRun(TestRealm, 7272);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var dropper = Object.FindAnyObjectByType<ChestDropper>();
            var skills = Object.FindAnyObjectByType<SkillManager>();
            Assert.That(dropper, Is.Not.Null);
            Assert.That(dropper.Config, Is.Not.Null);
            Assert.That(dropper.IsHunting, Is.False, "fresh run: no skill");
            Assert.That(dropper.transform.childCount, Is.EqualTo(dropper.Config.MaxOnGround), "pool prewarmed");

            Assert.That(skills.Grant("reliquary_hunter"), Is.True);
            Assert.That(dropper.Chance, Is.EqualTo(0.01f).Within(1e-5f));
            var chest = dropper.DropNow(skills.Stats.transform.position + Vector3.forward * 2f);
            Assert.That(chest, Is.Not.Null);
            yield return null;
            Assert.That(chest.IsOnGround, Is.True);
            Assert.That(chest.GetComponentsInChildren<Renderer>().All(r => r.enabled), Is.True);

            var empty = SceneManager.CreateScene("ChestDropEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
