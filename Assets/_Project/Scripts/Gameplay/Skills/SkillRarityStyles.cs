using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// Per-tier presentation of skills (KAN-37), one entry per <see cref="SkillRarity"/>: tier name, tier marks (gem count
    /// on the card, so the tier reads without colour), reveal sound and the Legendary shine / burst switches.
    /// Colours are NOT here: they live in GameHud.uss (skill-card--* / hud-skill--* classes), because UI Toolkit custom
    /// properties cannot be set from code. Common = worn steel, Rare = soul blue, Epic = violet, Legendary = ember gold.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Skill Rarity Styles", fileName = "SkillRarityStyles")]
    public sealed class SkillRarityStyles : ScriptableObject
    {
        [Serializable]
        public sealed class Style
        {
            [Tooltip("Tier name on the card (upper case).")]
            public string label = "COMMON";
            [Tooltip("Gems under the tier name: 1 Common .. 4 Legendary. Second cue next to colour (colour-blind players).")]
            [Range(1, 4)] public int marks = 1;
            [Tooltip("Plays when the card lands on the level-up screen. Soft for Common, big for Legendary.")]
            public AudioClip reveal;
            [Range(0f, 1f)] public float revealVolume = 0.5f;
            [Tooltip("Light band sweeps across the card while it is shown.")]
            public bool shine;
            [Tooltip("Ring + sparks burst out of the icon when the card lands.")]
            public bool burst;
        }

        [SerializeField] private Style[] styles = Defaults();

        /// <summary>Style of <paramref name="rarity"/>; the built-in default when the asset has no entry for it.</summary>
        public Style Of(SkillRarity rarity)
        {
            int i = (int)rarity;
            return styles != null && i >= 0 && i < styles.Length && styles[i] != null ? styles[i] : Fallback[Mathf.Clamp(i, 0, Fallback.Length - 1)];
        }

        /// <summary>Code-side setup (asset builder, tests): reveal clips per tier, Common first. Labels, marks and flags stay.</summary>
        public void SetReveals(AudioClip[] clips)
        {
            if (styles == null || styles.Length < SkillManager.RarityCount) styles = Defaults();
            for (int i = 0; i < styles.Length && clips != null && i < clips.Length; i++) styles[i].reveal = clips[i];
        }

        private static readonly Style[] Fallback = Defaults();

        /// <summary>Built-in tier table: label, marks, volume, Legendary shine + burst.</summary>
        public static Style[] Defaults() => new[]
        {
            new Style { label = "COMMON", marks = 1, revealVolume = 0.32f },
            new Style { label = "RARE", marks = 2, revealVolume = 0.45f },
            new Style { label = "EPIC", marks = 3, revealVolume = 0.6f },
            new Style { label = "LEGENDARY", marks = 4, revealVolume = 0.8f, shine = true, burst = true },
        };
    }
}
