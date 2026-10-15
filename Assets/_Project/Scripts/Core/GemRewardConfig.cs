using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Free Ember Shard sources (KAN-97): the one-time welcome and first-run bonuses and the gem reward of a Rare
    /// Reliquary chest (chance + amount range). Asset <c>Data/Economy/GemRewards.asset</c>, made once by the builders and
    /// never overwritten. First guesses, sized against the KAN-99 store packs (smallest pack ~100 shards); tune with
    /// playtests.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Gem Reward Config", fileName = "GemRewards")]
    public sealed class GemRewardConfig : ScriptableObject
    {
        [Tooltip("Shards given once, the first time the main menu opens. 0 = no welcome bonus.")]
        [SerializeField, Min(0)] private int welcomeBonus = 50;
        [Tooltip("Shards given once, when the first run ends (death, Realm cleared or leave to the menu). 0 = none.")]
        [SerializeField, Min(0)] private int firstRunBonus = 25;
        [Tooltip("Chance that an opened Rare Reliquary chest also gives shards (0..1). Light chests give none.")]
        [SerializeField, Range(0f, 1f)] private float rareChestChance = 0.35f;
        [Tooltip("Least shards a Rare chest gives when the chance hits.")]
        [SerializeField, Min(1)] private int rareChestMin = 3;
        [Tooltip("Most shards a Rare chest gives when the chance hits (inclusive).")]
        [SerializeField, Min(1)] private int rareChestMax = 8;

        public int WelcomeBonus => welcomeBonus;
        public int FirstRunBonus => firstRunBonus;
        public float RareChestChance => rareChestChance;
        public int RareChestMin => rareChestMin;
        public int RareChestMax => Mathf.Max(rareChestMin, rareChestMax);

        /// <summary>
        /// Shards from one Rare chest for two uniform rolls in [0, 1): 0 when <paramref name="chanceRoll"/> misses
        /// (roll >= chance), else min..max picked by <paramref name="amountRoll"/>. Pure, so tests pick the rolls.
        /// </summary>
        public int RollRareChest(float chanceRoll, float amountRoll)
        {
            if (rareChestChance <= 0f || chanceRoll >= rareChestChance) return 0;
            int span = RareChestMax - rareChestMin + 1;
            int step = Mathf.Min(span - 1, Mathf.FloorToInt(Mathf.Clamp01(amountRoll) * span));
            return rareChestMin + step;
        }

        /// <summary>Code-side setup (asset builder, tests). The defaults are the shipped first guesses.</summary>
        public static GemRewardConfig Create(int welcome = 50, int firstRun = 25, float chestChance = 0.35f, int chestMin = 3,
            int chestMax = 8)
        {
            var c = CreateInstance<GemRewardConfig>();
            c.welcomeBonus = Mathf.Max(0, welcome);
            c.firstRunBonus = Mathf.Max(0, firstRun);
            c.rareChestChance = Mathf.Clamp01(chestChance);
            c.rareChestMin = Mathf.Max(1, chestMin);
            c.rareChestMax = Mathf.Max(c.rareChestMin, chestMax);
            return c;
        }
    }
}
