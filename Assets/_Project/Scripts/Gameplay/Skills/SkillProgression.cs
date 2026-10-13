using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// Level-by-level skill table (KAN-19). Tiers unlock skills from a level on (a level's pool = every tier at or below
    /// it, so later levels add stronger skills on top of the early ones) and set the rarity odds (the highest tier at or
    /// below the level wins). Guarantees force one skill into the offer at an exact level (if it can still be taken).
    /// Tuning data only: no numbers live in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Skill Progression", fileName = "SkillProgression")]
    public sealed class SkillProgression : ScriptableObject
    {
        [Serializable]
        public sealed class Tier
        {
            [Min(1)] public int fromLevel = 2;
            public SkillDefinition[] unlocks = new SkillDefinition[0];
            [Tooltip("Roll weight per rarity while this tier is the active one. 0 = never offered (unless nothing else is left).")]
            [Min(0f)] public float common = 70f;
            [Min(0f)] public float rare = 25f;
            [Min(0f)] public float epic = 5f;
            [Tooltip("0 by default: a tier offers Legendary skills only when this is set.")]
            [Min(0f)] public float legendary;

            public float WeightOf(SkillRarity r)
            {
                switch (r)
                {
                    case SkillRarity.Common: return common;
                    case SkillRarity.Rare: return rare;
                    case SkillRarity.Epic: return epic;
                    default: return legendary;
                }
            }
        }

        [Serializable]
        public sealed class Guarantee
        {
            [Min(1)] public int level = 2;
            public SkillDefinition skill;
        }

        [SerializeField] private Tier[] tiers = new Tier[0];
        [SerializeField] private Guarantee[] guarantees = new Guarantee[0];
        [Tooltip("Pity (KAN-37): at least one Rare or better card in every N offers in a row. 0 = off.")]
        [SerializeField, Min(0)] private int pityOffers = 3;

        public IReadOnlyList<Tier> Tiers => tiers;
        public int PityOffers => pityOffers;

        /// <summary>The tier whose odds apply at <paramref name="level"/>: highest fromLevel at or below it, else the lowest tier.</summary>
        public Tier ActiveTier(int level)
        {
            Tier best = null, lowest = null;
            foreach (var t in tiers)
            {
                if (t == null) continue;
                if (lowest == null || t.fromLevel < lowest.fromLevel) lowest = t;
                if (t.fromLevel <= level && (best == null || t.fromLevel > best.fromLevel)) best = t;
            }
            return best ?? lowest;
        }

        /// <summary>Adds every skill unlocked at or below <paramref name="level"/> to <paramref name="into"/> (no duplicates).</summary>
        public void CollectPool(int level, List<SkillDefinition> into)
        {
            var lowest = ActiveTier(int.MinValue); // used when the level is below every tier
            foreach (var t in tiers)
            {
                if (t == null || (t.fromLevel > level && t != lowest)) continue;
                foreach (var s in t.unlocks)
                    if (s != null && !into.Contains(s)) into.Add(s);
            }
        }

        public float RarityWeight(int level, SkillRarity rarity) => ActiveTier(level)?.WeightOf(rarity) ?? 1f;

        /// <summary>The skill forced into the offer at exactly <paramref name="level"/>, or null.</summary>
        public SkillDefinition GuaranteedAt(int level)
        {
            foreach (var g in guarantees)
                if (g != null && g.level == level && g.skill != null) return g.skill;
            return null;
        }

        /// <summary>Any skill in the table (tier unlocks or guarantees) with this id; null when none.</summary>
        public SkillDefinition FindSkill(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            foreach (var t in tiers)
                if (t?.unlocks != null)
                    foreach (var s in t.unlocks)
                        if (s != null && s.Id == skillId) return s;
            foreach (var g in guarantees)
                if (g?.skill != null && g.skill.Id == skillId) return g.skill;
            return null;
        }

        /// <summary>Code-side setup (asset builder, tests).</summary>
        public void Configure(Tier[] tierTable, Guarantee[] guaranteeTable = null, int pity = 3)
        {
            tiers = tierTable ?? new Tier[0];
            guarantees = guaranteeTable ?? new Guarantee[0];
            pityOffers = Mathf.Max(0, pity);
        }
    }
}
