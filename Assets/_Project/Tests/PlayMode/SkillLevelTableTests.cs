#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Level tables of the 12 shipped skills (KAN-50.2 = KAN-71): the stat sheet at every skill and level matches the
    /// balance sheet (Confluence SS > 01 Product &amp; Game Design), the table rules hold (levels per tier, text per level,
    /// row 1 = old first pick, power never drops), and the two new secondary stats reach their consumers (Ember Bolts
    /// burn length, Soul Burst radius). Changing a number in a skill asset means changing it here and in the sheet.
    /// </summary>
    public sealed class SkillLevelTableTests
    {
        private const string Dir = "Assets/_Project/Data/Skills/";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private SkillManager _manager;

        [SetUp]
        public void SetUp()
        {
            _player = Track(new GameObject("Player"));
            _stats = _player.AddComponent<PlayerStats>();
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, null, null, _player.transform, rngSeed: 71);
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

        private static SkillDefinition Load(string file)
        {
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>($"{Dir}Skill_{file}.asset");
            Assert.That(skill, Is.Not.Null, file);
            return skill;
        }

        private static IEnumerable<SkillDefinition> Shipped() =>
            UnityEditor.AssetDatabase.FindAssets("t:SkillDefinition", new[] { Dir.TrimEnd('/') })
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)));

        // ------------------------------------------------------------ Balance sheet: every skill, every level

        /// <summary>
        /// Skill file, level, then the whole expected stat sheet: "Stat%=v" = percent total, "Stat+=v" = flat total. Every
        /// stat not named must be 0.
        /// </summary>
        private static readonly object[] Sheet =
        {
            new object[] { "SharpenedBolts", 1, new[] { "Damage%=0.25" } },
            new object[] { "SharpenedBolts", 2, new[] { "Damage%=0.45" } },
            new object[] { "SharpenedBolts", 3, new[] { "Damage%=0.65" } },
            new object[] { "SharpenedBolts", 4, new[] { "Damage%=0.85" } },
            new object[] { "SharpenedBolts", 5, new[] { "Damage%=1.05" } },
            new object[] { "SharpenedBolts", 6, new[] { "Damage%=1.25" } },
            new object[] { "QuickDraw", 1, new[] { "AttackSpeed%=0.18" } },
            new object[] { "QuickDraw", 2, new[] { "AttackSpeed%=0.34" } },
            new object[] { "QuickDraw", 3, new[] { "AttackSpeed%=0.48" } },
            new object[] { "QuickDraw", 4, new[] { "AttackSpeed%=0.62" } },
            new object[] { "QuickDraw", 5, new[] { "AttackSpeed%=0.76" } },
            new object[] { "QuickDraw", 6, new[] { "AttackSpeed%=0.9" } },
            new object[] { "FleetFoot", 1, new[] { "MoveSpeed%=0.1" } },
            new object[] { "FleetFoot", 2, new[] { "MoveSpeed%=0.18" } },
            new object[] { "FleetFoot", 3, new[] { "MoveSpeed%=0.24" } },
            new object[] { "FleetFoot", 4, new[] { "MoveSpeed%=0.29" } },
            new object[] { "FleetFoot", 5, new[] { "MoveSpeed%=0.33" } },
            new object[] { "Vigor", 1, new[] { "MaxHealth+=80" } },
            new object[] { "Vigor", 2, new[] { "MaxHealth+=150" } },
            new object[] { "Vigor", 3, new[] { "MaxHealth+=215" } },
            new object[] { "Vigor", 4, new[] { "MaxHealth+=275" } },
            new object[] { "Vigor", 5, new[] { "MaxHealth+=330" } },
            new object[] { "Vigor", 6, new[] { "MaxHealth+=380" } },
            new object[] { "SoulSiphon", 1, new[] { "PickupRadius%=0.4" } },
            new object[] { "SoulSiphon", 2, new[] { "PickupRadius%=0.7" } },
            new object[] { "SoulSiphon", 3, new[] { "PickupRadius%=0.95" } },
            new object[] { "SoulSiphon", 4, new[] { "PickupRadius%=1.15" } },
            new object[] { "SoulSiphon", 5, new[] { "PickupRadius%=1.3" } },
            new object[] { "KeenEye", 1, new[] { "CritChance+=0.1", "CritDamage+=0.3" } },
            new object[] { "KeenEye", 2, new[] { "CritChance+=0.15", "CritDamage+=0.45" } },
            new object[] { "KeenEye", 3, new[] { "CritChance+=0.2", "CritDamage+=0.6" } },
            new object[] { "KeenEye", 4, new[] { "CritChance+=0.25", "CritDamage+=0.8" } },
            new object[] { "KeenEye", 5, new[] { "CritChance+=0.3", "CritDamage+=1" } },
            new object[] { "PiercingBolts", 1, new[] { "Pierce+=1" } },
            new object[] { "PiercingBolts", 2, new[] { "Pierce+=2" } },
            new object[] { "PiercingBolts", 3, new[] { "Pierce+=2", "Damage%=0.1" } },
            new object[] { "PiercingBolts", 4, new[] { "Pierce+=3", "Damage%=0.15" } },
            new object[] { "Ricochet", 1, new[] { "Ricochet+=1" } },
            new object[] { "Ricochet", 2, new[] { "Ricochet+=2" } },
            new object[] { "Ricochet", 3, new[] { "Ricochet+=3" } },
            new object[] { "Ricochet", 4, new[] { "Ricochet+=3", "Damage%=0.15" } },
            new object[] { "EmberBolts", 1, new[] { "BurnDps+=25" } },
            new object[] { "EmberBolts", 2, new[] { "BurnDps+=40" } },
            new object[] { "EmberBolts", 3, new[] { "BurnDps+=55", "BurnDuration+=1" } },
            new object[] { "EmberBolts", 4, new[] { "BurnDps+=75", "BurnDuration+=2" } },
            new object[] { "BloodCovenant", 1, new[] { "LifeSteal+=0.05" } },
            new object[] { "BloodCovenant", 2, new[] { "LifeSteal+=0.07" } },
            new object[] { "BloodCovenant", 3, new[] { "LifeSteal+=0.09" } },
            new object[] { "BloodCovenant", 4, new[] { "LifeSteal+=0.12", "MaxHealth+=60" } },
            new object[] { "Multishot", 1, new[] { "Multishot+=2" } }, // Spread Shot (KAN-83)
            new object[] { "Multishot", 2, new[] { "Multishot+=4", "SpreadDamage+=0.05" } },
            new object[] { "Multishot", 3, new[] { "Multishot+=6", "SpreadDamage+=0.1" } },
            new object[] { "DoubleShot", 1, new[] { "DoubleShot+=1" } },
            new object[] { "DoubleShot", 2, new[] { "DoubleShot+=2" } },
            new object[] { "DoubleShot", 3, new[] { "DoubleShot+=3", "ChainDamage%=-0.1" } },
            new object[] { "SoulBurst", 1, new[] { "KillBurst+=0.5" } },
            new object[] { "SoulBurst", 2, new[] { "KillBurst+=0.7" } },
            new object[] { "SoulBurst", 3, new[] { "KillBurst+=0.9", "BurstRadius+=0.6" } },
            new object[] { "SoulBurst", 4, new[] { "KillBurst+=1.2", "BurstRadius+=1" } },
            new object[] { "ReliquaryHunter", 1, new[] { "ChestChance+=0.01", "ChestEliteChance+=0.08" } },
            new object[] { "ReliquaryHunter", 2, new[] { "ChestChance+=0.015", "ChestEliteChance+=0.1" } },
            new object[] { "ReliquaryHunter", 3, new[] { "ChestChance+=0.02", "ChestEliteChance+=0.12", "ChestChoices+=1" } },
            new object[] { "ReliquaryHunter", 4, new[] { "ChestChance+=0.025", "ChestEliteChance+=0.14", "ChestChoices+=1" } },
            new object[] { "ReliquaryHunter", 5, new[] { "ChestChance+=0.03", "ChestEliteChance+=0.16", "ChestChoices+=1", "RareChestEvery+=5" } },
        };

        [TestCaseSource(nameof(Sheet))]
        public void ShippedSkill_StatSheetAtLevel_MatchesBalanceSheet(string file, int level, string[] expected)
        {
            var skill = Load(file);
            Assert.That(_manager.Grant(skill), Is.True);
            if (level > 1) Assert.That(_manager.Upgrade(skill, level - 1), Is.True);
            Assert.That(_manager.GetLevel(skill), Is.EqualTo(level));

            var flat = new float[PlayerStats.StatCount];
            var percent = new float[PlayerStats.StatCount];
            foreach (string e in expected)
            {
                bool isPercent = e.Contains("%=");
                string[] parts = e.Split(new[] { isPercent ? "%=" : "+=" }, System.StringSplitOptions.None);
                int stat = (int)System.Enum.Parse(typeof(StatId), parts[0]);
                float value = float.Parse(parts[1], CultureInfo.InvariantCulture);
                if (isPercent) percent[stat] = value;
                else flat[stat] = value;
            }
            for (int i = 0; i < PlayerStats.StatCount; i++)
            {
                var id = (StatId)i;
                Assert.That(_stats.Flat(id), Is.EqualTo(flat[i]).Within(1e-4f), $"{file} L{level} {id} flat");
                Assert.That(_stats.Percent(id), Is.EqualTo(percent[i]).Within(1e-4f), $"{file} L{level} {id} percent");
            }
        }

        [Test]
        public void BalanceSheet_CoversEveryShippedSkillToItsMaxLevel()
        {
            var rows = Sheet.Cast<object[]>().Select(r => ((string)r[0], (int)r[1])).ToList();
            var skills = Shipped().ToList();
            Assert.That(skills.Count, Is.EqualTo(14)); // 12 KAN-19 skills + Reliquary Hunter (KAN-72) + Double Shot (KAN-83)
            foreach (var s in skills)
            {
                string file = s.name.Substring("Skill_".Length);
                var levels = rows.Where(r => r.Item1 == file).Select(r => r.Item2).OrderBy(l => l).ToArray();
                Assert.That(levels, Is.EqualTo(Enumerable.Range(1, s.MaxLevel).ToArray()), $"{s.Id}: one sheet row per level");
            }
        }

        // ------------------------------------------------------------ Table rules

        [Test]
        public void ShippedTables_FollowTheTierRules_TextPerLevel_RowOneIsTheFirstPick()
        {
            foreach (var s in Shipped())
            {
                Assert.That(s.HasLevelTable, Is.True, s.Id + " has a level table");
                // Utility Epic: KAN-50 asks for 5 levels. Spread Shot (Rare): KAN-83 asks for 3 (3 / 5 / 7 bolts).
                var (min, max) = s.Id == "reliquary_hunter" ? (5, 5) : s.Id == "multishot" ? (3, 3) : s.Rarity switch
                {
                    SkillRarity.Common => (5, 6),
                    SkillRarity.Rare => (4, 5),
                    SkillRarity.Epic => (3, 4),
                    _ => (3, 4),
                };
                Assert.That(s.MaxLevel, Is.InRange(min, max), $"{s.Id} ({s.Rarity}) max level");

                var texts = Enumerable.Range(1, s.MaxLevel).Select(s.DescriptionAt).ToArray();
                Assert.That(texts.All(t => !string.IsNullOrWhiteSpace(t)), Is.True, s.Id + " text on every level");
                Assert.That(texts.Distinct().Count(), Is.EqualTo(texts.Length), s.Id + " each level reads differently");
                Assert.That(texts.Max(t => t.Length), Is.LessThanOrEqualTo(35), s.Id + " short card text (layout for long text: KAN-76)");

                Assert.That(s.Description, Is.EqualTo(texts[0]), s.Id + " description = level 1 text");
                Assert.That(s.Modifiers, Is.EqualTo(s.Levels[0].modifiers), s.Id + " fallback modifiers = level 1 row");
            }
        }

        [Test]
        public void ShippedTables_PrimaryStatNeverDrops_AndEveryLevelChangesTheSheet()
        {
            foreach (var s in Shipped())
            {
                StatId primary = s.Levels[0].modifiers[0].stat;
                float Total(int level, StatId id) => s.Levels[level - 1].modifiers.Where(m => m.stat == id).Sum(m => m.value);
                for (int level = 2; level <= s.MaxLevel; level++)
                {
                    Assert.That(Total(level, primary), Is.GreaterThanOrEqualTo(Total(level - 1, primary)), $"{s.Id} L{level} {primary}");
                    var before = s.Levels[level - 2].modifiers;
                    var now = s.Levels[level - 1].modifiers;
                    Assert.That(now.SequenceEqual(before), Is.False, $"{s.Id} L{level} must change something");
                }
            }
        }

        [Test]
        public void ShippedSkill_UpgradesToMax_ThenStops_CardShowsTheNextLevelText()
        {
            var skill = Load("SharpenedBolts");
            _manager.Grant(skill);
            Assert.That(skill.DescriptionAt(_manager.GetLevel(skill) + 1), Is.EqualTo("+36% bolt damage"), "card for 1 -> 2");
            Assert.That(_manager.Upgrade(skill, 99), Is.True);
            Assert.That(_manager.GetLevel(skill), Is.EqualTo(6));
            Assert.That(_manager.CanUpgrade(skill), Is.False);
            Assert.That(_manager.CanTake(skill), Is.False, "maxed: never offered again");
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(195f).Within(1e-3f));
        }

        // ------------------------------------------------------------ New secondary stats reach their consumers

        [Test]
        public void EmberBolts_HigherLevelsBurnLonger()
        {
            var skill = Load("EmberBolts");
            _manager.Grant(skill);
            var burn = (BurnOnHitEffect)_manager.OwnedSkills.Single().Effect;
            Assert.That(burn, Is.Not.Null);
            float[] expected = { 3f, 3f, 4f, 5f };
            for (int level = 1; level <= 4; level++)
            {
                if (level > 1) _manager.Upgrade(skill);
                Assert.That(burn.Duration, Is.EqualTo(expected[level - 1]).Within(1e-4f), $"L{level} burn seconds");
                Assert.That(_stats.Modify(StatId.BurnDps, 0f), Is.EqualTo(new[] { 25f, 40f, 55f, 75f }[level - 1]).Within(1e-4f));
            }
        }

        [Test]
        public void SoulBurst_LevelThreeAndFourReachFurther()
        {
            var skill = Load("SoulBurst");
            _manager.Grant(skill);
            var burst = (KillBurstEffect)_manager.OwnedSkills.Single().Effect;
            Assert.That(burst, Is.Not.Null);
            float[] expected = { 2.6f, 2.6f, 3.2f, 3.6f };
            for (int level = 1; level <= 4; level++)
            {
                if (level > 1) _manager.Upgrade(skill);
                Assert.That(burst.Radius, Is.EqualTo(expected[level - 1]).Within(1e-4f), $"L{level} burst radius");
            }
            _manager.ResetRun();
            Assert.That(_stats.Flat(StatId.BurstRadius), Is.Zero, "run reset drops the reach bonus");
        }
    }
}
#endif
