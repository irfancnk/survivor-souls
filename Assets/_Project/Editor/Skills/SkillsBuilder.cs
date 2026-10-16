using System;
using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Builds the skill system (KAN-19): rune icons, the 12 skill assets, the level table, the three behaviour-skill effect
    /// prefabs (burn embers, life steal motes, soul burst dark-magic explosion (KAN-66)) and the scene <see cref="SkillManager"/>.
    /// Skill and table assets are created only when missing: designer edits are never overwritten (missing icon / effect
    /// links are filled in). Icons and effect prefabs are rebuilt every run. Called by GameSceneBuilder.
    /// Tiers (KAN-37): Common = plain stat boosts, Rare = crit / pierce / ricochet / Spread Shot, Epic = life steal /
    /// Double Shot, Legendary = burning bolts / soul burst. Reliquary Hunter (KAN-72, Epic) drops upgrade chests through stats only.
    /// Hallowed Shield (KAN-93, Epic): a ward bubble that blocks all damage for a few seconds on a cooldown.
    /// <see cref="BuildStyles"/> makes the per-tier style asset (labels, gems, sounds).
    /// </summary>
    public static class SkillsBuilder
    {
        public const string DataDir = "Assets/_Project/Data/Skills";
        public const string ProgressionPath = DataDir + "/SkillProgression_Run.asset";
        public const string StylesPath = DataDir + "/SkillRarityStyles.asset";
        private const string IconDir = "Assets/_Project/Art/Skills/Icons";
        private const string FxDir = "Assets/_Project/Art/Skills";
        private const string PrefabDir = "Assets/_Project/Prefabs/Skills";
        private const string RingTexturePath = "Assets/_Project/Art/Textures/Game/T_Ring.png"; // made by GameArtKit

        private enum Fx { None, Burn, LifeSteal, KillBurst, Ward }

        private sealed class Spec
        {
            public string Id, Name, File;
            public SkillRarity Rarity;
            public float Weight = 1f;
            /// <summary>Level table (KAN-71): row N = total effect at level N. Row 1 is also the card text and stacking fallback.</summary>
            public SkillLevelData[] Levels;
            public Func<Vector2, float> Glyph;
            public Fx Effect;
            /// <summary>Offer gate (KAN-72): only offered as new while another owned skill can still level up.</summary>
            public bool NeedsUpgradable;
            /// <summary>Repeat offer weights while owned (KAN-83), null = 1.</summary>
            public float[] RepeatWeights;
            /// <summary>Chest upgrade card weight (KAN-83).</summary>
            public float UpgradeWeight = 1f;
            /// <summary>
            /// Rework migration (KAN-83): an existing asset still named this is rewritten once from the spec (name, text,
            /// tier, levels, offer weights). After that its display name differs and designer edits stay.
            /// </summary>
            public string ReworkFrom;

            public string Text => Levels[0].description;
            public StatModifier[] Mods => Levels[0].modifiers;
            public int Max => Levels.Length;
        }

        private static StatModifier Pct(StatId s, float v) => new StatModifier(s, ModifierKind.Percent, v);
        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);
        private static SkillLevelData L(string text, params StatModifier[] mods) => new SkillLevelData(text, mods);

        // Base numbers for reference: bolt 100 dmg / 1 shot per s, no crit / x2 crit damage, zombie 150 HP (Shambler 100),
        // player 400 HP at 5 m/s, pickup 2.6 m, burn 3 s, soul burst 2.6 m.
        // Level tables (KAN-71, balance sheet in Confluence SS > 01 Product & Game Design): each row is the TOTAL at that
        // level. Common = one stat, shrinking steps, up to 6 levels (5 where the stat saturates: move, pickup). Rare / Epic
        // = 3-5 levels, the last level adds a second stat. Legendary = 4 levels, level 3 adds reach (burn length, burst
        // radius). Level 1 keeps the KAN-19 numbers, so the first pick feels the same as before.
        // KAN-86 single-target pass: Sharpened Bolts / Quick Draw / Keen Eye hit harder from level 2 (level 1 kept, later
        // levels grow most: boss DPS at level 20 up from 3.8x), pierce -33 % and bounce -30 % damage per enemy (BoltShot falloff), Spread Shot side bolts 50 / 55 / 60 %.
        // KAN-191: level 10 single-target back to 2.5x (true mean at 2000 runs was 2.44x): Sharpened Bolts 42-135 % from level 2,
        // Quick Draw 50-94 % from level 3 (levels 1-2 kept: the 10-pick boss fight stays >= 60 s), Fleet Foot weight 0.5 (no
        // damage, it crowded the early Common offers).
        // NOTE: shipped assets keep designer numbers; these rows only seed a fresh asset. Edit both.
        private static readonly Spec[] Specs =
        {
            new Spec { Id = "sharpened_bolts", File = "SharpenedBolts", Name = "Sharpened Bolts", Rarity = SkillRarity.Common,
                Glyph = SkillIconPainter.Damage, Levels = new[]
                {
                    L("+20% bolt damage", Pct(StatId.Damage, 0.2f)),
                    L("+42% bolt damage", Pct(StatId.Damage, 0.42f)),
                    L("+66% bolt damage", Pct(StatId.Damage, 0.66f)),
                    L("+90% bolt damage", Pct(StatId.Damage, 0.9f)),
                    L("+112% bolt damage", Pct(StatId.Damage, 1.12f)),
                    L("+135% bolt damage", Pct(StatId.Damage, 1.35f)),
                } },
            new Spec { Id = "quick_draw", File = "QuickDraw", Name = "Quick Draw", Rarity = SkillRarity.Common,
                Glyph = SkillIconPainter.AttackSpeed, Levels = new[]
                {
                    L("+15% attack speed", Pct(StatId.AttackSpeed, 0.15f)),
                    L("+30% attack speed", Pct(StatId.AttackSpeed, 0.3f)),
                    L("+50% attack speed", Pct(StatId.AttackSpeed, 0.5f)),
                    L("+66% attack speed", Pct(StatId.AttackSpeed, 0.66f)),
                    L("+80% attack speed", Pct(StatId.AttackSpeed, 0.8f)),
                    L("+94% attack speed", Pct(StatId.AttackSpeed, 0.94f)),
                } },
            new Spec { Id = "fleet_foot", File = "FleetFoot", Name = "Fleet Foot", Rarity = SkillRarity.Common, Weight = 0.5f,
                Glyph = SkillIconPainter.MoveSpeed, Levels = new[]
                {
                    L("+10% move speed", Pct(StatId.MoveSpeed, 0.1f)),
                    L("+18% move speed", Pct(StatId.MoveSpeed, 0.18f)),
                    L("+24% move speed", Pct(StatId.MoveSpeed, 0.24f)),
                    L("+29% move speed", Pct(StatId.MoveSpeed, 0.29f)),
                    L("+33% move speed", Pct(StatId.MoveSpeed, 0.33f)),
                } },
            new Spec { Id = "vigor", File = "Vigor", Name = "Vigor", Rarity = SkillRarity.Common,
                Glyph = SkillIconPainter.MaxHealth, Levels = new[]
                {
                    L("+80 max health, healed at once", Add(StatId.MaxHealth, 80f)),
                    L("+150 max health, healed at once", Add(StatId.MaxHealth, 150f)),
                    L("+215 max health, healed at once", Add(StatId.MaxHealth, 215f)),
                    L("+275 max health, healed at once", Add(StatId.MaxHealth, 275f)),
                    L("+330 max health, healed at once", Add(StatId.MaxHealth, 330f)),
                    L("+380 max health, healed at once", Add(StatId.MaxHealth, 380f)),
                } },
            new Spec { Id = "soul_siphon", File = "SoulSiphon", Name = "Soul Siphon", Rarity = SkillRarity.Common, Weight = 0.7f,
                Glyph = SkillIconPainter.PickupRadius, Levels = new[]
                {
                    L("+40% soul pickup range", Pct(StatId.PickupRadius, 0.4f)),
                    L("+70% soul pickup range", Pct(StatId.PickupRadius, 0.7f)),
                    L("+95% soul pickup range", Pct(StatId.PickupRadius, 0.95f)),
                    L("+115% soul pickup range", Pct(StatId.PickupRadius, 1.15f)),
                    L("+130% soul pickup range", Pct(StatId.PickupRadius, 1.3f)),
                } },
            new Spec { Id = "keen_eye", File = "KeenEye", Name = "Keen Eye", Rarity = SkillRarity.Rare,
                Glyph = SkillIconPainter.Crit, Levels = new[]
                {
                    L("+10% crit chance, +25% crit damage", Add(StatId.CritChance, 0.1f), Add(StatId.CritDamage, 0.25f)),
                    L("+15% crit chance, +45% crit damage", Add(StatId.CritChance, 0.15f), Add(StatId.CritDamage, 0.45f)),
                    L("+20% crit chance, +60% crit damage", Add(StatId.CritChance, 0.2f), Add(StatId.CritDamage, 0.6f)),
                    L("+25% crit chance, +80% crit damage", Add(StatId.CritChance, 0.25f), Add(StatId.CritDamage, 0.8f)),
                    L("+30% crit chance, +100% crit damage", Add(StatId.CritChance, 0.3f), Add(StatId.CritDamage, 1f)),
                } },
            new Spec { Id = "piercing_bolts", File = "PiercingBolts", Name = "Piercing Bolts", Rarity = SkillRarity.Rare,
                Glyph = SkillIconPainter.Pierce, Levels = new[]
                {
                    L("Pierce +1 enemy, -33% per enemy", Add(StatId.Pierce, 1f)),
                    L("Pierce +2 enemies, -33% per enemy", Add(StatId.Pierce, 2f)),
                    L("Pierce +2 enemies, +10% damage", Add(StatId.Pierce, 2f), Pct(StatId.Damage, 0.1f)),
                    L("Pierce +3 enemies, +15% damage", Add(StatId.Pierce, 3f), Pct(StatId.Damage, 0.15f)),
                } },
            new Spec { Id = "ricochet", File = "Ricochet", Name = "Ricochet", Rarity = SkillRarity.Rare,
                Glyph = SkillIconPainter.Ricochet, Levels = new[]
                {
                    L("Bounce +1 enemy, -30% per bounce", Add(StatId.Ricochet, 1f)),
                    L("Bounce +2 enemies, -30% per bounce", Add(StatId.Ricochet, 2f)),
                    L("Bounce +3 enemies, -30% per bounce", Add(StatId.Ricochet, 3f)),
                    L("Bounce to +3 enemies, +15% damage", Add(StatId.Ricochet, 3f), Pct(StatId.Damage, 0.15f)),
                } },
            new Spec { Id = "ember_bolts", File = "EmberBolts", Name = "Ember Bolts", Rarity = SkillRarity.Legendary,
                Glyph = SkillIconPainter.Burn, Effect = Fx.Burn, Levels = new[]
                {
                    // KAN-134: burning foes move 15-25 % slower (bosses half), an ignite stops a foe 0.2 s (not bosses).
                    L("Bolts set foes ablaze, slow 15%", Add(StatId.BurnDps, 25f), Add(StatId.BurnSlow, 0.15f),
                        Add(StatId.IgniteStun, 0.2f)),
                    L("Fire 40/s for 3 s, slows 18%", Add(StatId.BurnDps, 40f), Add(StatId.BurnSlow, 0.18f),
                        Add(StatId.IgniteStun, 0.2f)),
                    // KAN-77 perks: L3 fire spreads to the nearest foe (one step), L4 burning deaths blast 60 % bolt damage.
                    L("Fire 55/s 4 s, spreads, slow 21%", Add(StatId.BurnDps, 55f), Add(StatId.BurnDuration, 1f),
                        Add(StatId.BurnSpread, 1f), Add(StatId.BurnSlow, 0.21f), Add(StatId.IgniteStun, 0.2f)),
                    L("Fire 75/s 5 s, 60% blast, slow 25%", Add(StatId.BurnDps, 75f), Add(StatId.BurnDuration, 2f),
                        Add(StatId.BurnSpread, 1f), Add(StatId.BurnDeathBlast, 0.6f), Add(StatId.BurnSlow, 0.25f),
                        Add(StatId.IgniteStun, 0.2f)),
                } },
            new Spec { Id = "blood_covenant", File = "BloodCovenant", Name = "Blood Covenant", Rarity = SkillRarity.Epic,
                Glyph = SkillIconPainter.LifeSteal, Effect = Fx.LifeSteal, Levels = new[]
                {
                    L("Heal 5% of bolt damage dealt", Add(StatId.LifeSteal, 0.05f)),
                    L("Heal 7% of bolt damage dealt", Add(StatId.LifeSteal, 0.07f)),
                    L("Heal 9% of bolt damage dealt", Add(StatId.LifeSteal, 0.09f)),
                    L("Heal 12% of damage, +60 max HP", Add(StatId.LifeSteal, 0.12f), Add(StatId.MaxHealth, 60f)),
                } },
            // KAN-83: the old Multishot is now Spread Shot (same asset and id "multishot", so saves and debug configs load).
            // The main bolt always flies straight; angled bolts at 15 deg steps on both sides deal a damage share (AutoShooter
            // base 50 %, levels add to it). Rare: a fan is a crowd tool, not a boss killer.
            new Spec { Id = "multishot", File = "Multishot", Name = "Spread Shot", Rarity = SkillRarity.Rare, ReworkFrom = "Multishot",
                Glyph = SkillIconPainter.Multishot, Levels = new[]
                {
                    L("2 side bolts at 15°, 50% damage", Add(StatId.Multishot, 2f)),
                    L("4 side bolts to 30°, 55% damage", Add(StatId.Multishot, 4f), Add(StatId.SpreadDamage, 0.05f)),
                    L("6 side bolts to 45°, 60% damage", Add(StatId.Multishot, 6f), Add(StatId.SpreadDamage, 0.1f)),
                } },
            // KAN-83 (KAN-36): Double Shot. The volley fires again down the same line 0.09 s later. Repeats are rare on
            // purpose: owned, its level-up weight drops to 25 % (2nd stack) and 6 % (3rd), pity and guarantees never push
            // it again, and chest screens offer it at 35 % weight.
            new Spec { Id = "double_shot", File = "DoubleShot", Name = "Double Shot", Rarity = SkillRarity.Epic,
                Glyph = SkillIconPainter.DoubleShot, RepeatWeights = new[] { 0.25f, 0.06f }, UpgradeWeight = 0.35f, Levels = new[]
                {
                    L("1 extra bolt down the same line", Add(StatId.DoubleShot, 1f)),
                    L("2 extra bolts down the same line", Add(StatId.DoubleShot, 2f)),
                    L("3 extra bolts, 90% damage each", Add(StatId.DoubleShot, 3f), Pct(StatId.ChainDamage, -0.1f)),
                } },
            new Spec { Id = "soul_burst", File = "SoulBurst", Name = "Soul Burst", Rarity = SkillRarity.Legendary,
                Glyph = SkillIconPainter.KillBurst, Effect = Fx.KillBurst, Levels = new[]
                {
                    L("Kills burst: 50% damage in 2.6 m", Add(StatId.KillBurst, 0.5f)),
                    L("Kills burst: 70% damage in 2.6 m", Add(StatId.KillBurst, 0.7f)),
                    L("Kills burst: 90% damage in 3.2 m", Add(StatId.KillBurst, 0.9f), Add(StatId.BurstRadius, 0.6f)),
                    L("Kills burst: 120% damage in 3.6 m", Add(StatId.KillBurst, 1.2f), Add(StatId.BurstRadius, 1f)),
                } },
            // KAN-72 (KAN-50 part 2): chest drops. Pure data: ChestDropper reads these stats, no effect prefab. Epic with
            // 5 levels (KAN-50 asks for 5; a utility skill, so outside the 3-4 Epic rule). L3 adds a third upgrade card,
            // L5 makes every 5th chest Rare (2 levels). Pity 90 s, cap 2, lifetime 40 s: Data/Chests/ChestDropConfig.
            new Spec { Id = "reliquary_hunter", File = "ReliquaryHunter", Name = "Reliquary Hunter", Rarity = SkillRarity.Epic,
                Glyph = SkillIconPainter.Chest, NeedsUpgradable = true, Levels = new[]
                {
                    L("Kills drop chests: 1%, elites 8%", Add(StatId.ChestChance, 0.01f), Add(StatId.ChestEliteChance, 0.08f)),
                    L("Kills drop chests: 1.5%, elites 10%", Add(StatId.ChestChance, 0.015f), Add(StatId.ChestEliteChance, 0.1f)),
                    L("Chests 2%, elites 12%, 3 choices", Add(StatId.ChestChance, 0.02f), Add(StatId.ChestEliteChance, 0.12f),
                        Add(StatId.ChestChoices, 1f)),
                    L("Chests 2.5%, elites 14%, 3 choices", Add(StatId.ChestChance, 0.025f), Add(StatId.ChestEliteChance, 0.14f),
                        Add(StatId.ChestChoices, 1f)),
                    L("Chests 3%, elites 16%, 5th is rare", Add(StatId.ChestChance, 0.03f), Add(StatId.ChestEliteChance, 0.16f),
                        Add(StatId.ChestChoices, 1f), Add(StatId.RareChestEvery, 5f)),
                } },
            // KAN-93: Temporary Shield. Every Cooldown seconds a ward blocks ALL damage for WardDuration seconds (the shield
            // pool is not touched). The cooldown counts from the end of the last ward; the effect's base is 20 s, levels
            // subtract. Epic, from the level 6 tier, weight 0.6 (like Soul Siphon), repeat offers at 60 %: a defensive card
            // must not crowd out the early damage picks (power curve, 2000 runs: level 10 single-target 2.44x without it,
            // 2.43-2.44x with it; SingleTargetBalanceTests).
            new Spec { Id = "temporary_shield", File = "TemporaryShield", Name = "Hallowed Shield", Rarity = SkillRarity.Epic,
                Weight = 0.6f, RepeatWeights = new[] { 0.6f, 0.6f }, Glyph = SkillIconPainter.Ward, Effect = Fx.Ward, Levels = new[]
                {
                    L("Ward: immune 3 s, every 20 s", Add(StatId.WardDuration, 3f)),
                    L("Ward: immune 4 s, every 17 s", Add(StatId.WardDuration, 4f), Add(StatId.WardCooldown, -3f)),
                    L("Ward: immune 5 s, every 14 s", Add(StatId.WardDuration, 5f), Add(StatId.WardCooldown, -6f)),
                } },
        };

        /// <summary>Skills added after the progression asset was first made: id -> tier fromLevel they join (once).</summary>
        private static readonly (string id, int fromLevel)[] LateUnlocks = { ("reliquary_hunter", 4), ("double_shot", 2), ("temporary_shield", 6) };

        /// <summary>Guarantees added after the progression asset was first made: id -> level (once, if that level is free).</summary>
        private static readonly (string id, int level)[] LateGuarantees = { ("double_shot", 3), ("sharpened_bolts", 2), ("quick_draw", 8) };

        /// <summary>Builds the assets and adds the SkillManager (plus PlayerStats on the player when missing).</summary>
        public static SkillManager Create(GameArtKit kit, GameObject host, GameObject player, AutoShooter shooter, PlayerHealth health)
        {
            var progression = BuildAssets(kit);
            if (!player.TryGetComponent<PlayerStats>(out var stats)) stats = player.AddComponent<PlayerStats>();
            var manager = host.AddComponent<SkillManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("progression").objectReferenceValue = progression;
            so.FindProperty("stats").objectReferenceValue = stats;
            so.FindProperty("shooter").objectReferenceValue = shooter;
            so.FindProperty("health").objectReferenceValue = health;
            so.FindProperty("effectRoot").objectReferenceValue = player.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        [MenuItem("Survivor Souls/Rebuild Skill Assets")]
        private static void BuildAssetsMenu()
        {
            BuildAssets(GameArtKit.Build());
            BuildStyles(Audio.SfxBuilder.Build().RarityReveals);
        }

        /// <summary>
        /// Per-tier style asset (KAN-37). Created with the built-in table when missing; an existing asset keeps its labels,
        /// gems, volumes and flags, only empty reveal sound slots are filled with <paramref name="reveals"/>.
        /// </summary>
        public static SkillRarityStyles BuildStyles(AudioClip[] reveals)
        {
            EditorAssetUtil.EnsureFolder(DataDir);
            var styles = AssetDatabase.LoadAssetAtPath<SkillRarityStyles>(StylesPath);
            if (styles == null)
            {
                styles = ScriptableObject.CreateInstance<SkillRarityStyles>();
                styles.SetReveals(reveals);
                AssetDatabase.CreateAsset(styles, StylesPath);
            }
            else
            {
                var so = new SerializedObject(styles);
                var list = so.FindProperty("styles");
                for (int i = 0; i < list.arraySize && reveals != null && i < reveals.Length; i++)
                {
                    var clip = list.GetArrayElementAtIndex(i).FindPropertyRelative("reveal");
                    if (clip.objectReferenceValue == null) clip.objectReferenceValue = reveals[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            return styles;
        }

        public static SkillProgression BuildAssets(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(DataDir);
            EditorAssetUtil.EnsureFolder(IconDir);
            EditorAssetUtil.EnsureFolder(PrefabDir);
            var effects = BuildEffects(kit);
            var byId = new Dictionary<string, SkillDefinition>();
            foreach (var spec in Specs)
            {
                var icon = SkillIconPainter.Write($"{IconDir}/T_Skill_{spec.File}.png", spec.Glyph);
                effects.TryGetValue(spec.Effect, out var effect);
                byId[spec.Id] = LoadOrCreate(spec, icon, effect);
            }
            var table = BuildProgression(byId);
            AssetDatabase.SaveAssets();
            return table;
        }

        private static SkillDefinition LoadOrCreate(Spec spec, Texture2D icon, SkillEffect effect)
        {
            string path = $"{DataDir}/Skill_{spec.File}.asset";
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);
            if (skill == null)
            {
                skill = ScriptableObject.CreateInstance<SkillDefinition>();
                skill.Configure(spec.Id, spec.Name, spec.Text, spec.Rarity, spec.Max, spec.Mods);
                skill.SetLevels(spec.Levels);
                skill.SetRequiresUpgradableSkill(spec.NeedsUpgradable);
                skill.SetOfferWeights(spec.RepeatWeights, spec.UpgradeWeight);
                skill.SetPresentation(icon, effect, spec.Weight);
                AssetDatabase.CreateAsset(skill, path);
                return skill;
            }
            // Existing asset: keep designer numbers, only repair missing links and give a table-less skill its table.
            if (spec.ReworkFrom != null && skill.DisplayName == spec.ReworkFrom) Rework(skill, spec);
            else if (!skill.HasLevelTable) FillLevels(skill, spec);
            var so = new SerializedObject(skill);
            var iconProp = so.FindProperty("icon");
            if (iconProp.objectReferenceValue == null) iconProp.objectReferenceValue = icon;
            var fxProp = so.FindProperty("effectPrefab");
            if (fxProp.objectReferenceValue == null && effect != null) fxProp.objectReferenceValue = effect;
            so.ApplyModifiedPropertiesWithoutUndo();
            return skill;
        }

        /// <summary>
        /// One-time move of a KAN-19 asset (old stacking, empty table) to its level table (KAN-71). The fallback fields
        /// (card text, modifiers, max stack) are set to row 1 / the table length so every view of the skill agrees. An
        /// asset that already has a table is never touched: tune it in the Inspector.
        /// </summary>
        private static void FillLevels(SkillDefinition skill, Spec spec)
        {
            skill.Configure(skill.Id, skill.DisplayName, spec.Text, skill.Rarity, spec.Max, spec.Mods);
            skill.SetLevels(spec.Levels);
            EditorUtility.SetDirty(skill);
        }

        /// <summary>
        /// One-time rewrite of a reworked skill (<see cref="Spec.ReworkFrom"/>, KAN-83): same asset, guid and id (saves, debug
        /// configs and level tables keep their link), new name, card text, tier, level table and offer weights.
        /// </summary>
        private static void Rework(SkillDefinition skill, Spec spec)
        {
            skill.Configure(spec.Id, spec.Name, spec.Text, spec.Rarity, spec.Max, spec.Mods);
            skill.SetLevels(spec.Levels);
            skill.SetOfferWeights(spec.RepeatWeights, spec.UpgradeWeight);
            EditorUtility.SetDirty(skill);
            Debug.Log($"[SkillsBuilder] Reworked {spec.ReworkFrom} -> {spec.Name} ({skill.name})");
        }

        /// <summary>
        /// Run pacing (XP curve: levels 2-4 come every few kills). Stat skills first, behaviour skills from level 4, epic
        /// game-changers from level 6. Level 3 always offers Double Shot (KAN-83, from level 2), level 4 Ember Bolts (the
        /// first Legendary), level 6 Spread Shot. KAN-86: level 2 Sharpened Bolts and level 8 Quick Draw (single-target damage
        /// for the bosses).
        /// Tier odds (KAN-37) grow with the level: 70/27/3/0 at L2, 55/30/12/3 at L4, 45/32/16/7 at L6, 38/32/20/10 from L10.
        /// Pity: at least one Rare+ card in every 3 offers.
        /// </summary>
        private static SkillProgression BuildProgression(Dictionary<string, SkillDefinition> s)
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            if (table != null)
            {
                AddLateUnlocks(table, s);
                AddLateGuarantees(table, s);
                return table;
            }
            table = ScriptableObject.CreateInstance<SkillProgression>();
            table.Configure(
                new[]
                {
                    new SkillProgression.Tier
                    {
                        fromLevel = 2, common = 70f, rare = 27f, epic = 3f, legendary = 0f,
                        unlocks = new[] { s["sharpened_bolts"], s["quick_draw"], s["fleet_foot"], s["vigor"], s["soul_siphon"], s["keen_eye"], s["piercing_bolts"], s["double_shot"] },
                    },
                    new SkillProgression.Tier
                    {
                        fromLevel = 4, common = 55f, rare = 30f, epic = 12f, legendary = 3f,
                        unlocks = new[] { s["ricochet"], s["ember_bolts"], s["blood_covenant"], s["reliquary_hunter"] },
                    },
                    new SkillProgression.Tier
                    {
                        fromLevel = 6, common = 45f, rare = 32f, epic = 16f, legendary = 7f,
                        unlocks = new[] { s["multishot"], s["soul_burst"], s["temporary_shield"] },
                    },
                    new SkillProgression.Tier
                    {
                        fromLevel = 10, common = 38f, rare = 32f, epic = 20f, legendary = 10f,
                        unlocks = new SkillDefinition[0],
                    },
                },
                new[]
                {
                    new SkillProgression.Guarantee { level = 3, skill = s["double_shot"] },
                    new SkillProgression.Guarantee { level = 4, skill = s["ember_bolts"] },
                    new SkillProgression.Guarantee { level = 6, skill = s["multishot"] },
                    new SkillProgression.Guarantee { level = 2, skill = s["sharpened_bolts"] }, // KAN-86
                    new SkillProgression.Guarantee { level = 8, skill = s["quick_draw"] },
                });
            AssetDatabase.CreateAsset(table, ProgressionPath);
            return table;
        }

        /// <summary>
        /// Puts a skill added after the table was made (<see cref="LateUnlocks"/>) into the tier of its level, once: only
        /// when the table does not list it anywhere yet. To take it out of runs, set its weight to 0 (removing it from
        /// the table brings it back on the next build).
        /// </summary>
        private static void AddLateUnlocks(SkillProgression table, Dictionary<string, SkillDefinition> s)
        {
            foreach (var (id, fromLevel) in LateUnlocks)
            {
                if (!s.TryGetValue(id, out var skill) || table.FindSkill(id) != null) continue;
                SkillProgression.Tier tier = null;
                foreach (var t in table.Tiers)
                    if (t != null && t.fromLevel == fromLevel) tier = t;
                if (tier == null) continue;
                var list = new List<SkillDefinition>(tier.unlocks ?? new SkillDefinition[0]) { skill };
                tier.unlocks = list.ToArray();
                EditorUtility.SetDirty(table);
                Debug.Log($"[SkillsBuilder] Added {id} to the level {fromLevel} tier of {ProgressionPath}");
            }
        }

        /// <summary>
        /// Adds a guarantee made after the table (<see cref="LateGuarantees"/>), once: only when the skill has no guarantee
        /// yet and its level is free. Remove it in the Inspector and it comes back; set the level to an unused one instead.
        /// </summary>
        private static void AddLateGuarantees(SkillProgression table, Dictionary<string, SkillDefinition> s)
        {
            foreach (var (id, level) in LateGuarantees)
            {
                if (!s.TryGetValue(id, out var skill) || table.GuaranteedAt(level) != null || table.HasGuarantee(skill)) continue;
                table.AddGuarantee(level, skill);
                EditorUtility.SetDirty(table);
                Debug.Log($"[SkillsBuilder] Added a level {level} guarantee for {id} to {ProgressionPath}");
            }
        }

        // ------------------------------------------------------------ Effect prefabs

        private static Dictionary<Fx, SkillEffect> BuildEffects(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(FxDir);
            var ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath);
            if (ringTex == null) throw new InvalidOperationException($"Missing {RingTexturePath} (GameArtKit builds it).");
            var burstMats = SoulBurstArt.Build(FxDir, ringTex); // KAN-66
            var glowMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_SkillGlow.mat", kit.SoftDot, additive: true);
            var wardMat = EditorAssetUtil.LoadOrCreateMaterial(FxDir + "/M_FX_WardBubble.mat", "SurvivorSouls/WardBubble"); // KAN-93
            // KAN-134: alpha blend, not additive: additive fire vanishes on the white snow of Realm 1.
            var fireMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_BurnFlame.mat", kit.SoftDot, additive: false);

            return new Dictionary<Fx, SkillEffect>
            {
                [Fx.Burn] = SavePrefab<BurnOnHitEffect>("FX_Skill_EmberBolts", (root, fx) =>
                {
                    // KAN-77: 360 (was 260) for the spread streams and death blasts on the same system.
                    var embers = Particles(root, "Embers", glowMat, 360, new Vector2(0.45f, 0.85f), new Vector2(0.06f, 0.13f),
                        new Color(1f, 0.62f, 0.2f), new Color(1f, 0.32f, 0.08f));
                    Drag(embers, 1.5f);
                    Set(fx, "embers", embers);
                    // KAN-134: flame tongues on burning foes + the ignite flare: two more shared systems (+2 draw calls).
                    var flames = Particles(root, "Flames", fireMat, 800, new Vector2(0.32f, 0.52f), new Vector2(0.42f, 0.72f),
                        new Color(1f, 0.78f, 0.3f), new Color(1f, 0.45f, 0.1f));
                    FlameColour(flames);
                    var fr = flames.GetComponent<ParticleSystemRenderer>();
                    fr.renderMode = ParticleSystemRenderMode.Stretch;
                    fr.velocityScale = 0.12f;
                    fr.lengthScale = 1.5f;
                    Drag(flames, 2f);
                    var flare = Particles(root, "Ignite Flare", fireMat, 240, new Vector2(0.18f, 0.28f), new Vector2(0.4f, 0.65f),
                        new Color(1f, 0.7f, 0.25f), new Color(1f, 0.5f, 0.12f));
                    FlameColour(flare);
                    Drag(flare, 6f);
                    Set(fx, "flames", flames);
                    Set(fx, "flare", flare);
                }),
                [Fx.LifeSteal] = SavePrefab<LifeStealEffect>("FX_Skill_BloodCovenant", (root, fx) =>
                {
                    var motes = Particles(root, "Blood Motes", glowMat, 40, new Vector2(0.45f, 0.7f), new Vector2(0.1f, 0.18f),
                        new Color(1f, 0.25f, 0.3f), new Color(0.85f, 0.08f, 0.15f));
                    Drag(motes, 3f);
                    Set(fx, "motes", motes);
                }),
                // KAN-66: dark-magic mini explosion, six Emit-only layers (KillBurstEffect sets size / colour per burst).
                [Fx.KillBurst] = SavePrefab<KillBurstEffect>("FX_Skill_SoulBurst", (root, fx) =>
                {
                    var flash = Particles(root, "Burst Flash", glowMat, 24, new Vector2(0.1f, 0.1f), new Vector2(1.6f, 1.6f),
                        Color.white, Color.white);
                    Curve(flash, 0.6f, 1.25f, 1.1f);
                    var ring = Particles(root, "Burst Ring", burstMats.GroundRing, 32, new Vector2(0.42f, 0.42f), new Vector2(1f, 1f),
                        Color.white, Color.white);
                    ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    Curve(ring, 0.2f, 1f, 1f); // ends exactly on the damage radius (KillBurstEffect.RingSize)
                    var shell = Particles(root, "Burst Shell", burstMats.Ring, 32, new Vector2(0.34f, 0.34f), new Vector2(1f, 1f),
                        Color.white, Color.white);
                    Curve(shell, 0.3f, 1f, 1f);
                    var fire = Particles(root, "Burst Soul Fire", burstMats.Wisp, 220, new Vector2(0.45f, 0.6f), new Vector2(0.6f, 1f),
                        Color.white, Color.white);
                    Curve(fire, 0.55f, 1.35f, 1.5f);
                    Drag(fire, 4.5f);
                    var fireMain = fire.main;
                    fireMain.gravityModifier = -0.35f; // wisps curl up
                    var sparks = Particles(root, "Burst Shards", glowMat, 240, new Vector2(0.45f, 0.65f), new Vector2(0.07f, 0.12f),
                        Color.white, Color.white);
                    var sr = sparks.GetComponent<ParticleSystemRenderer>();
                    sr.renderMode = ParticleSystemRenderMode.Stretch;
                    sr.velocityScale = 0.05f;
                    sr.lengthScale = 1.6f;
                    Drag(sparks, 2.5f);
                    var main = sparks.main;
                    main.gravityModifier = 1.1f; // shards arc out and fall
                    var mark = Particles(root, "Burst Rune Mark", burstMats.Mark, 32, new Vector2(0.45f, 0.45f), new Vector2(1f, 1f),
                        Color.white, Color.white);
                    mark.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    Curve(mark, 0.92f, 1f, 1f);
                    Set(fx, "flash", flash);
                    Set(fx, "ring", ring);
                    Set(fx, "shell", shell);
                    Set(fx, "fire", fire);
                    Set(fx, "sparks", sparks);
                    Set(fx, "mark", mark);
                }),
                // KAN-93: ward bubble (built-in sphere, WardBubble shader, off until the first ward) + rise / break motes.
                [Fx.Ward] = SavePrefab<ShieldWardEffect>("FX_Skill_HallowedShield", (root, fx) =>
                {
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.name = "Ward Bubble";
                    UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
                    sphere.transform.SetParent(root.transform, false);
                    sphere.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                    sphere.transform.localScale = Vector3.one * 2.2f;
                    var mr = sphere.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = wardMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    mr.enabled = false;
                    var motes = Particles(root, "Ward Motes", glowMat, 80, new Vector2(0.5f, 0.8f), new Vector2(0.08f, 0.15f),
                        new Color(1f, 0.9f, 0.6f), new Color(0.75f, 0.85f, 1f));
                    Drag(motes, 2.5f);
                    Set(fx, "bubble", mr);
                    Set(fx, "motes", motes);
                }),
            };
        }

        private static SkillEffect SavePrefab<T>(string name, Action<GameObject, T> build) where T : SkillEffect
        {
            var root = new GameObject(name);
            try
            {
                var fx = root.AddComponent<T>();
                build(root, fx);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
                return prefab.GetComponent<T>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Script-fed world-space particles (Emit only): fade out, shrink, no lights, shadows or probes.</summary>
        private static ParticleSystem Particles(GameObject parent, string name, Material mat, int max, Vector2 life, Vector2 size,
            Color a, Color b)
        {
            var ps = PropKit.Particles(name, parent.transform, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.1f), (0.7f, 0.55f), (0f, 1f) });
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        /// <summary>Size over life: <paramref name="from"/> at birth, eased out to <paramref name="to"/> (KAN-66 burst layers).</summary>
        private static void Curve(ParticleSystem ps, float from, float to, float max)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(max, new AnimationCurve(
                new Keyframe(0f, from / max, 0f, 3f * (to - from) / max), new Keyframe(1f, to / max, 0f, 0f)));
        }

        /// <summary>Fire over its life (KAN-134): yellow at birth, orange, then a dark red fade (alpha blend reads on snow).</summary>
        private static void FlameColour(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(
                new[] { (new Color(1f, 0.95f, 0.65f), 0f), (new Color(1f, 0.55f, 0.18f), 0.45f), (new Color(0.5f, 0.12f, 0.05f), 1f) },
                new[] { (0f, 0f), (1f, 0.08f), (0.75f, 0.5f), (0f, 1f) });
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
        }

        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
