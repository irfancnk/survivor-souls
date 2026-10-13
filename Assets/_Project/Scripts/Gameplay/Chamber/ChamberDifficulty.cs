using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>What a Chamber holds besides the regular horde (KAN-46 rule hook).</summary>
    public enum ChamberType
    {
        Normal = 0,
        /// <summary>Mini boss Chamber (KAN-23). Until mini bosses exist it plays as <see cref="Normal"/>.</summary>
        MiniBoss = 1,
        /// <summary>Boss Chamber (KAN-22): every 5th and the last of the Realm. Until bosses exist it plays as <see cref="Normal"/>.</summary>
        Boss = 2,
    }

    /// <summary>
    /// Everything that changes from one Chamber to the next (KAN-46), worked out once when the Chamber starts by
    /// <see cref="ChamberSetup"/>: Chamber type, the multipliers for the wave table and enemy health, and the world seed.
    /// </summary>
    public readonly struct ChamberPlan
    {
        public readonly int Realm;
        public readonly int Chamber;
        public readonly ChamberType Type;
        /// <summary>Last Chamber of the Realm (KAN-47: victory after it).</summary>
        public readonly bool IsLast;
        /// <summary>Enemy max health x this at spawn.</summary>
        public readonly float HealthMultiplier;
        /// <summary>Wave caps (max alive, burst size) x this.</summary>
        public readonly float CapMultiplier;
        /// <summary>Seconds between groups and bursts x this (below 1 = faster spawns).</summary>
        public readonly float IntervalMultiplier;
        /// <summary>Seed of the world layout (tile variants, turns, exit cell).</summary>
        public readonly int LayoutSeed;

        public ChamberPlan(int realm, int chamber, ChamberType type, bool isLast, float health, float cap, float interval, int seed)
        {
            Realm = realm;
            Chamber = chamber;
            Type = type;
            IsLast = isLast;
            HealthMultiplier = health;
            CapMultiplier = cap;
            IntervalMultiplier = interval;
            LayoutSeed = seed;
        }

        /// <summary>Chamber with no scaling (no difficulty asset): the wave table as written.</summary>
        public static ChamberPlan Plain(int realm, int chamber, int seed) =>
            new ChamberPlan(realm, chamber, ChamberType.Normal, false, 1f, 1f, 1f, seed);

        public override string ToString() =>
            $"Realm {Realm} Chamber {Chamber} {Type}{(IsLast ? " (last)" : "")} hp x{HealthMultiplier:F2} cap x{CapMultiplier:F2} " +
            $"interval x{IntervalMultiplier:F2} seed {LayoutSeed}";
    }

    /// <summary>
    /// Per-Chamber difficulty and layout rules of one Realm (KAN-46). Chamber 1 plays the wave table as written; every
    /// later Chamber adds <see cref="healthGrowth"/> to the enemy health multiplier and <see cref="capGrowth"/> to the
    /// wave caps, and multiplies the spawn intervals by <see cref="intervalFactor"/>, each clamped. The world layout comes
    /// from a seed made of the run seed, the Realm and the Chamber: a new shore every Chamber, the same one if the same
    /// Chamber of the same run loads again. A non-zero entry in <see cref="layoutSeeds"/> pins a hand-picked layout.
    /// Type rule: the last Chamber and every <see cref="bossEvery"/>th is a boss Chamber (KAN-22), every
    /// <see cref="miniBossEvery"/>th a mini boss Chamber (KAN-23, 0 = off). Edit the asset; the builder never overwrites it.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Chamber Difficulty", fileName = "ChamberDifficulty")]
    public sealed class ChamberDifficulty : ScriptableObject
    {
        [Tooltip("Chambers in this Realm. The last one is a boss Chamber and ends the Realm (KAN-47).")]
        [SerializeField, Min(1)] private int chambersInRealm = 10;
        [Tooltip("Every Nth Chamber is a boss Chamber (KAN-22). 0 = only the last one.")]
        [SerializeField, Min(0)] private int bossEvery = 5;
        [Tooltip("Every Nth Chamber is a mini boss Chamber (KAN-23); a boss Chamber wins. 0 = off.")]
        [SerializeField, Min(0)] private int miniBossEvery;

        [Header("Enemy health")]
        [Tooltip("Added to the enemy health multiplier per Chamber after the first (0.15 = +15 % each).")]
        [SerializeField, Min(0f)] private float healthGrowth = 0.15f;
        [SerializeField, Min(1f)] private float maxHealthMultiplier = 4f;

        [Header("Wave size")]
        [Tooltip("Added to the max-alive / burst-size multiplier per Chamber after the first.")]
        [SerializeField, Min(0f)] private float capGrowth = 0.05f;
        [Tooltip("Cap of the max-alive multiplier. Keep the biggest phase x this near the phone budget (~50 alive, KAN-44).")]
        [SerializeField, Min(1f)] private float maxCapMultiplier = 1.35f;

        [Header("Spawn pace")]
        [Tooltip("Spawn intervals x this per Chamber after the first (0.96 = 4 % faster each).")]
        [SerializeField, Range(0.5f, 1f)] private float intervalFactor = 0.96f;
        [SerializeField, Range(0.2f, 1f)] private float minIntervalMultiplier = 0.7f;

        [Header("Layout")]
        [Tooltip("Fixed world seed per Chamber (index 0 = Chamber 1). 0 or missing = new layout every run.")]
        [SerializeField] private int[] layoutSeeds = new int[0];

        public int ChambersInRealm => chambersInRealm;

        public ChamberType TypeOf(int chamber)
        {
            if (chamber >= chambersInRealm || (bossEvery > 0 && chamber % bossEvery == 0)) return ChamberType.Boss;
            if (miniBossEvery > 0 && chamber % miniBossEvery == 0) return ChamberType.MiniBoss;
            return ChamberType.Normal;
        }

        public float HealthMultiplier(int chamber) => Mathf.Min(1f + healthGrowth * Steps(chamber), maxHealthMultiplier);

        public float CapMultiplier(int chamber) => Mathf.Min(1f + capGrowth * Steps(chamber), maxCapMultiplier);

        public float IntervalMultiplier(int chamber) => Mathf.Max(Mathf.Pow(intervalFactor, Steps(chamber)), minIntervalMultiplier);

        /// <summary>World seed of <paramref name="chamber"/>: the pinned one, else a mix of run, Realm and Chamber. Never 0.</summary>
        public int LayoutSeed(int runSeed, int realm, int chamber)
        {
            int index = chamber - 1;
            if (layoutSeeds != null && index >= 0 && index < layoutSeeds.Length && layoutSeeds[index] != 0) return layoutSeeds[index];
            return MixSeed(runSeed, realm, chamber);
        }

        public ChamberPlan Plan(int realm, int chamber, int runSeed) =>
            new ChamberPlan(realm, chamber, TypeOf(chamber), chamber >= chambersInRealm, HealthMultiplier(chamber),
                CapMultiplier(chamber), IntervalMultiplier(chamber), LayoutSeed(runSeed, realm, chamber));

        private static int Steps(int chamber) => Mathf.Max(0, chamber - 1);

        /// <summary>Stable integer hash (no string or platform hash): same inputs, same seed on every device. Never 0.</summary>
        public static int MixSeed(int runSeed, int realm, int chamber)
        {
            unchecked
            {
                ulong h = (ulong)(uint)runSeed;
                h = (h ^ ((ulong)(uint)realm << 32)) * 0x9E3779B97F4A7C15UL;
                h ^= (ulong)(uint)chamber * 0xBF58476D1CE4E5B9UL;
                h ^= h >> 31;
                h *= 0x94D049BB133111EBUL;
                h ^= h >> 29;
                int seed = (int)(h & 0x7fffffff);
                return seed != 0 ? seed : 1;
            }
        }

        /// <summary>Tests and tools: rules without an asset.</summary>
        public static ChamberDifficulty Create(int chambers = 10, int boss = 5, int miniBoss = 0, float health = 0.15f,
            float maxHealth = 4f, float cap = 0.05f, float maxCap = 1.35f, float interval = 0.96f, float minInterval = 0.7f,
            int[] seeds = null)
        {
            var d = CreateInstance<ChamberDifficulty>();
            d.chambersInRealm = chambers;
            d.bossEvery = boss;
            d.miniBossEvery = miniBoss;
            d.healthGrowth = health;
            d.maxHealthMultiplier = maxHealth;
            d.capGrowth = cap;
            d.maxCapMultiplier = maxCap;
            d.intervalFactor = interval;
            d.minIntervalMultiplier = minInterval;
            d.layoutSeeds = seeds ?? new int[0];
            return d;
        }
    }
}
