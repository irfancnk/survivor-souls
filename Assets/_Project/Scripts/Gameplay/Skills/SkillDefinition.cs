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

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Texture2D Icon => icon;
        public SkillRarity Rarity => rarity;
        public float Weight => weight;
        public StatModifier[] Modifiers => modifiers;
        public SkillEffect EffectPrefab => effectPrefab;
        public SkillLevelData[] Levels => levels;
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

        public void SetPresentation(Texture2D iconTexture, SkillEffect effect, float rollWeight = 1f)
        {
            icon = iconTexture;
            effectPrefab = effect;
            weight = Mathf.Max(0f, rollWeight);
        }
    }
}
