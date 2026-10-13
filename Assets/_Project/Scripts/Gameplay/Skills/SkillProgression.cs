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

            public float WeightOf(SkillRarity r) => r == SkillRarity.Common ? common : r == SkillRarity.Rare ? rare : epic;
        }

        [Serializable]
        public sealed class Guarantee
        {
            [Min(1)] public int level = 2;
            public SkillDefinition skill;
        }

        [SerializeField] private Tier[] tiers = new Tier[0];
        [SerializeField] private Guarantee[] guarantees = new Guarantee[0];

        public IReadOnlyList<Tier> Tiers => tiers;

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

        /// <summary>Code-side setup (asset builder, tests).</summary>
        public void Configure(Tier[] tierTable, Guarantee[] guaranteeTable = null)
        {
            tiers = tierTable ?? new Tier[0];
            guarantees = guaranteeTable ?? new Guarantee[0];
        }
    }
}
