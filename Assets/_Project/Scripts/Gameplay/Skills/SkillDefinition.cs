using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// Tier of a skill (KAN-37): card frame colour, tier marks and reveal sound (<see cref="SkillRarityStyles"/>) and the
    /// roll odds bucket (odds per level live in <see cref="SkillProgression"/>). Stored as numbers: new tiers go at the END.
    /// A chest upgrade (KAN-23) never changes the tier, only the stack.
    /// </summary>
    public enum SkillRarity { Common, Rare, Epic, Legendary }

    /// <summary>
    /// One row of a skill's level table (KAN-50): the TOTAL effect at that level (not the step from the level before),
    /// so a level can change any number, add a new stat or drop one. Card text for that level.
    /// </summary>
    [Serializable]
    public struct SkillLevelData
    {
        [Tooltip("Card text for this level, e.g. '3 arrows per shot'. Empty = the skill's own description.")]
        [TextArea(1, 3)] public string description;
        [Tooltip("Total stat changes at this level (replace the lower levels, never add to them).")]
        public StatModifier[] modifiers;

        public SkillLevelData(string text, params StatModifier[] mods)
        {
            description = text;
            modifiers = mods ?? new StatModifier[0];
        }
    }

    /// <summary>
    /// One skill ("Ember Boon" in game, KAN-19). Data only: a new stat skill is a new asset with stat modifiers; a
    /// behaviour skill also points at an effect prefab whose <see cref="SkillEffect"/> reads its strength from
    /// <see cref="PlayerStats"/> (so levels stay in the data, never in effect code).
    /// Levels (KAN-50): with a <see cref="Levels"/> table, level N applies row N and <see cref="MaxLevel"/> is the table
    /// length. Without one (the KAN-19 skills until KAN-71 fills their tables), level N applies <see cref="Modifiers"/>
    /// N times, up to the old max stack.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Skill", fileName = "Skill_")]
    public sealed class SkillDefinition : ScriptableObject
    {
        [Tooltip("Stable id for saves, analytics and debug commands (snake_case). Never rename after ship.")]
        [SerializeField] private string id = "";
        [SerializeField] private string displayName = "";
        [Tooltip("Card text for one stack, e.g. '+20% bolt damage'.")]
        [SerializeField, TextArea(2, 4)] private string description = "";
        [Tooltip("White glyph on transparent; the card and HUD tint it with the rarity colour.")]
        [SerializeField] private Texture2D icon;
        [SerializeField] private SkillRarity rarity = SkillRarity.Common;
        [Tooltip("Max level when there is no level table (each level = the modifiers once more).")]
        [SerializeField, Min(1)] private int maxStack = 1;
        [Tooltip("Relative odds inside its rarity (1 = normal, 0.5 = half as likely).")]
        [SerializeField, Min(0f)] private float weight = 1f;
        [Tooltip("No level table: applied once per level.")]
        [SerializeField] private StatModifier[] modifiers = new StatModifier[0];
        [Tooltip("Level table (KAN-50): row N = total effect at level N. Empty = old stacking (modifiers x level).")]
        [SerializeField] private SkillLevelData[] levels = new SkillLevelData[0];
        [Tooltip("Behaviour skills only: spawned under the player at level 1, destroyed on run reset.")]
        [SerializeField] private SkillEffect effectPrefab;
        [Tooltip("Offered as a new skill only while the player owns another skill that can still level up (chest skills, KAN-72).")]
        [SerializeField] private bool requiresUpgradableSkill;
        [Tooltip("Repeat offers (KAN-83): level-up weight multiplier while owned. Element N-1 applies at owned level N (the " +
                 "last element covers higher levels). Empty = 1 (no change). Owned skills below 1 are never pushed by pity or guarantees.")]
        [SerializeField] private float[] repeatOfferWeights = new float[0];
        [Tooltip("Chest upgrade screens (KAN-83): weight multiplier of this skill's card (1 = normal, 0.35 = rarely offered).")]
        [SerializeField, Range(0.01f, 1f)] private float upgradeOfferWeight = 1f;
        [Tooltip("KAN-246 offer gate: offered as new only while the player owns at least one of these (Eclipse Crown needs an orbital). Empty = no gate.")]
        [SerializeField] private SkillDefinition[] requiresAnyOf = new SkillDefinition[0];

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Texture2D Icon => icon;
        public SkillRarity Rarity => rarity;
        public float Weight => weight;
        public StatModifier[] Modifiers => modifiers;
        public SkillEffect EffectPrefab => effectPrefab;
        /// <summary>Offer gate (KAN-72): not offered as new until another owned skill can still level up.</summary>
        public bool RequiresUpgradableSkill => requiresUpgradableSkill;
        public SkillLevelData[] Levels => levels;
        /// <summary>Chest upgrade card weight multiplier (KAN-83).</summary>
        public float UpgradeOfferWeight => upgradeOfferWeight;
        /// <summary>KAN-246 offer gate: owning one of these unlocks the offer (empty = always). See <see cref="SkillManager.CanOffer"/>.</summary>
        public SkillDefinition[] RequiresAnyOf => requiresAnyOf;
        /// <summary>
        /// KAN-246 Ember Boons art: the icon is a full-colour 3:4 portrait (T_Boon_*), not a white glyph. The card shows it
        /// whole and untinted; small slots show a centre crop.
        /// </summary>
        public bool HasPortraitArt => icon != null && icon.height > icon.width;

        /// <summary>
        /// Level-up weight multiplier at owned level <paramref name="ownedLevel"/> (KAN-83): 1 when not owned or without a
        /// table, else the entry for that level (the last one covers higher levels).
        /// </summary>
        public float RepeatOfferWeight(int ownedLevel)
        {
            if (ownedLevel <= 0 || repeatOfferWeights == null || repeatOfferWeights.Length == 0) return 1f;
            return Mathf.Max(0f, repeatOfferWeights[Mathf.Min(ownedLevel, repeatOfferWeights.Length) - 1]);
        }

        /// <summary>True when owning it lowers its repeat odds: pity and guarantees must not push it again.</summary>
        public bool DampsRepeats(int ownedLevel) => RepeatOfferWeight(ownedLevel) < 1f;
        public bool HasLevelTable => levels != null && levels.Length > 0;
        /// <summary>Highest level: the table length, or the old max stack without a table.</summary>
        public int MaxLevel => HasLevelTable ? levels.Length : maxStack;

        /// <summary>Card / guide text for <paramref name="level"/> (clamped to 1..max): the row's text, else the description.</summary>
        public string DescriptionAt(int level)
        {
            if (!HasLevelTable) return description;
            string text = levels[Mathf.Clamp(level, 1, levels.Length) - 1].description;
            return string.IsNullOrEmpty(text) ? description : text;
        }

        /// <summary>
        /// Writes this skill's effect at <paramref name="level"/> to <paramref name="stats"/> (0 removes it). Always the
        /// whole level from the data, so a level change can never drift from the table.
        /// </summary>
        public void ApplyLevel(PlayerStats stats, int level)
        {
            if (stats == null) return;
            level = Mathf.Min(level, MaxLevel);
            if (level <= 0) stats.RemoveSource(this);
            else if (HasLevelTable) stats.SetSource(this, levels[level - 1].modifiers, 1);
            else stats.SetSource(this, modifiers, level);
        }

        /// <summary>Code-side setup (asset builder, tests). Inspector edits are the normal way to tune.</summary>
        public SkillDefinition Configure(string skillId, string name, string text, SkillRarity skillRarity, int stacks,
            params StatModifier[] mods)
        {
            id = skillId;
            displayName = name;
            description = text;
            rarity = skillRarity;
            maxStack = Mathf.Max(1, stacks);
            modifiers = mods ?? new StatModifier[0];
            return this;
        }

        /// <summary>Code-side level table (asset builder, tests). Empty = old stacking.</summary>
        public SkillDefinition SetLevels(params SkillLevelData[] table)
        {
            levels = table ?? new SkillLevelData[0];
            return this;
        }

        /// <summary>Code-side offer gate (asset builder, tests): see <see cref="RequiresUpgradableSkill"/>.</summary>
        public SkillDefinition SetRequiresUpgradableSkill(bool required)
        {
            requiresUpgradableSkill = required;
            return this;
        }

        /// <summary>Code-side offer odds (asset builder, tests): see <see cref="RepeatOfferWeight"/> and <see cref="UpgradeOfferWeight"/>.</summary>
        public SkillDefinition SetOfferWeights(float[] repeat, float upgrade = 1f)
        {
            repeatOfferWeights = repeat ?? new float[0];
            upgradeOfferWeight = Mathf.Clamp(upgrade, 0.01f, 1f);
            return this;
        }

        /// <summary>Code-side offer gate (asset builder, tests): see <see cref="RequiresAnyOf"/>.</summary>
        public SkillDefinition SetRequiresAnyOf(params SkillDefinition[] skills)
        {
            requiresAnyOf = skills ?? new SkillDefinition[0];
            return this;
        }

        public void SetPresentation(Texture2D iconTexture, SkillEffect effect, float rollWeight = 1f)
        {
            icon = iconTexture;
            effectPrefab = effect;
            weight = Mathf.Max(0f, rollWeight);
        }
    }
}
