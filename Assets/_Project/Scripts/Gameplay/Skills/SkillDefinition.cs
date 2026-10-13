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
    /// One skill ("Ember Boon" in game, KAN-19). Data only: a new stat skill is a new asset with stat modifiers; a
    /// behaviour skill also points at an effect prefab whose <see cref="SkillEffect"/> reads its strength from
    /// <see cref="PlayerStats"/> (so stacking stays in the modifiers, never in effect code).
    /// Each stack applies <see cref="Modifiers"/> once more, up to <see cref="MaxStack"/>.
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
        [SerializeField, Min(1)] private int maxStack = 1;
        [Tooltip("Relative odds inside its rarity (1 = normal, 0.5 = half as likely).")]
        [SerializeField, Min(0f)] private float weight = 1f;
        [Tooltip("Applied once per stack.")]
        [SerializeField] private StatModifier[] modifiers = new StatModifier[0];
        [Tooltip("Behaviour skills only: spawned under the player on the first stack, destroyed on run reset.")]
        [SerializeField] private SkillEffect effectPrefab;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Texture2D Icon => icon;
        public SkillRarity Rarity => rarity;
        public int MaxStack => maxStack;
        public float Weight => weight;
        public StatModifier[] Modifiers => modifiers;
        public SkillEffect EffectPrefab => effectPrefab;

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

        public void SetPresentation(Texture2D iconTexture, SkillEffect effect, float rollWeight = 1f)
        {
            icon = iconTexture;
            effectPrefab = effect;
            weight = Mathf.Max(0f, rollWeight);
        }
    }
}
