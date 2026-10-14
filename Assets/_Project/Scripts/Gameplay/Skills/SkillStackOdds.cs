using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// How often a run stacks one skill (KAN-83 statistics check, made for Double Shot): <see cref="Runs"/> simulated runs
    /// roll every level-up offer from level 2 to <see cref="MaxLevel"/> with the real <see cref="SkillManager.Roll"/>
    /// (tiers, guarantees, pity, repeat-offer weights). Two pick rules: a random card (the same player model as
    /// <see cref="SkillPowerCurve"/>) and a greedy player who always takes the skill when it is offered (worst case).
    /// Chest upgrades are not in it (they need the drop rate). Debug and tests only: allocates.
    /// </summary>
    public sealed class SkillStackOdds
    {
        public string SkillId { get; private set; }
        public int Runs { get; private set; }
        public int MaxLevel { get; private set; }
        /// <summary>Index N = runs that ended with the skill at level N or higher (random card).</summary>
        public int[] Reached { get; private set; }
        /// <summary>Index N = runs that ended with the skill at level N or higher (always take it).</summary>
        public int[] ReachedGreedy { get; private set; }

        /// <summary>Share of random-card runs at level <paramref name="level"/> or higher.</summary>
        public float Share(int level) => level >= 0 && level < Reached.Length ? Reached[level] / (float)Runs : 0f;
        /// <summary>Share of greedy runs at level <paramref name="level"/> or higher.</summary>
        public float GreedyShare(int level) => level >= 0 && level < ReachedGreedy.Length ? ReachedGreedy[level] / (float)Runs : 0f;

        public static SkillStackOdds Simulate(SkillProgression table, string skillId, int runs = 1000, int maxLevel = 20, int seed = 83)
        {
            runs = Mathf.Max(1, runs);
            maxLevel = Mathf.Max(2, maxLevel);
            var skill = table != null ? table.FindSkill(skillId) : null;
            int max = skill != null ? skill.MaxLevel : 0;
            var odds = new SkillStackOdds
            {
                SkillId = skillId, Runs = runs, MaxLevel = maxLevel, Reached = new int[max + 1], ReachedGreedy = new int[max + 1],
            };
            if (skill == null) return odds;

            var go = new GameObject("Stack Odds (temp)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var stats = go.AddComponent<PlayerStats>();
                var manager = go.AddComponent<SkillManager>();
                var offer = new List<SkillDefinition>(3);
                var pick = new System.Random(seed);
                for (int greedy = 0; greedy < 2; greedy++)
                {
                    var reached = greedy == 1 ? odds.ReachedGreedy : odds.Reached;
                    for (int r = 0; r < runs; r++)
                    {
                        manager.Setup(table, stats, null, null, go.transform, seed + 7919 * (r + 1));
                        manager.SpawnEffects = false;
                        stats.Clear();
                        for (int level = 2; level <= maxLevel; level++)
                        {
                            int n = manager.Roll(3, level, offer);
                            if (n == 0) continue;
                            int chosen = greedy == 1 ? offer.IndexOf(skill) : -1;
                            manager.Grant(offer[chosen >= 0 ? chosen : pick.Next(n)]);
                        }
                        int got = Mathf.Min(max, manager.GetLevel(skill));
                        for (int l = 0; l <= got; l++) reached[l]++;
                    }
                }
                manager.ResetRun();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            return odds;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"Stack odds of {SkillId}: {Runs} runs, level-up picks from level 2 to {MaxLevel} (no chest upgrades).");
            for (int l = 1; l < Reached.Length; l++)
                sb.Append($"\n  level {l}+: {Share(l),6:P1} random card, {GreedyShare(l),6:P1} always taken");
            return sb.ToString();
        }
    }
}
