using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// One boss (KAN-22 / KAN-58): name, body, health, the slam attack, the reward and how the horde behaves during the
    /// fight. All numbers live here, so balance needs no code. Phase 2, more attacks (KAN-61) and hearts (KAN-60) add
    /// their own fields later. Edit the asset; the builder never overwrites it (it only fills an empty prefab slot).
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Boss Data", fileName = "Boss")]
    public sealed class BossData : ScriptableObject
    {
        /// <summary>Hard floor of every telegraph (KAN-22: at least 0.6 s of warning before damage).</summary>
        public const float MinTelegraph = 0.6f;

        [Header("Identity")]
        [SerializeField] private string displayName = "THE DROWNED WARDEN";
        [SerializeField] private string title = "Keeper of the Frozen Shore";
        [Tooltip("Body: an Enemy prefab with isBoss on (no small health bar) and XP 0 (the reward below drops instead).")]
        [SerializeField] private Enemy prefab;

        [Header("Health")]
        [Tooltip("Max health in Chamber 1 terms; the Chamber health multiplier (KAN-46) scales it.")]
        [SerializeField, Min(1f)] private float maxHealth = 3200f;

        [Header("Arrival")]
        [Tooltip("Scaled seconds after the Chamber starts before the spawn warning shows.")]
        [SerializeField, Min(0f)] private float introDelay = 2f;
        [Tooltip("Seconds the red ground warning shows where the boss will rise.")]
        [SerializeField, Min(0.1f)] private float spawnWarning = 1.2f;
        [Tooltip("Distance from the Knight where the boss rises.")]
        [SerializeField, Min(2f)] private float spawnDistance = 8f;

        [Header("Slam")]
        [Tooltip("The slam starts when the Knight is this close to the boss (m).")]
        [SerializeField, Min(0.5f)] private float slamRange = 4f;
        [Tooltip("Centre of the slam circle, this far in front of the boss (m).")]
        [SerializeField, Min(0f)] private float slamReach = 1.6f;
        [SerializeField, Min(0.5f)] private float slamRadius = 3f;
        [Tooltip("Seconds the red circle fills before the hit. Never below 0.6 s.")]
        [SerializeField, Min(MinTelegraph)] private float slamTelegraph = 1f;
        [Tooltip("Seconds the boss stays still after the hit.")]
        [SerializeField, Min(0f)] private float slamRecover = 0.7f;
        [Tooltip("Seconds from the end of one slam to the earliest next one.")]
        [SerializeField, Min(0f)] private float slamCooldown = 3.2f;
        [Tooltip("First slam after the boss rises (s).")]
        [SerializeField, Min(0f)] private float firstSlamDelay = 1.5f;
        [Tooltip("Slam damage as a share of the Knight's max health.")]
        [SerializeField, Range(0f, 1f)] private float slamDamage = 0.22f;
        [Tooltip("No single boss hit takes more than this share of max health (no instant kill).")]
        [SerializeField, Range(0.05f, 1f)] private float maxHitShare = 0.35f;

        [Header("Reward")]
        [Tooltip("XP (= souls) dropped when the boss dies, split over the wisps below.")]
        [SerializeField, Min(0)] private int rewardXp = 600;
        [SerializeField, Range(1, 24)] private int rewardWisps = 6;

        [Header("Horde during the fight")]
        [Tooltip("Max alive / burst size x this while the boss Chamber runs.")]
        [SerializeField, Range(0f, 1f)] private float hordeCap = 0.45f;
        [Tooltip("Spawn intervals x this while the boss Chamber runs (above 1 = slower).")]
        [SerializeField, Range(1f, 4f)] private float hordeInterval = 1.6f;

        public string DisplayName => displayName;
        public string Title => title;
        public Enemy Prefab => prefab;
        public float MaxHealth => maxHealth;
        public float IntroDelay => introDelay;
        public float SpawnWarning => spawnWarning;
        public float SpawnDistance => spawnDistance;
        public float SlamRange => slamRange;
        public float SlamReach => slamReach;
        public float SlamRadius => slamRadius;
        public float SlamTelegraph => Mathf.Max(MinTelegraph, slamTelegraph);
        public float SlamRecover => slamRecover;
        public float SlamCooldown => slamCooldown;
        public float FirstSlamDelay => firstSlamDelay;
        public int RewardXp => rewardXp;
        public int RewardWisps => rewardWisps;
        public float HordeCap => hordeCap;
        public float HordeInterval => hordeInterval;

        /// <summary>Slam damage for a Knight with <paramref name="playerMax"/> health, capped at <see cref="maxHitShare"/>.</summary>
        public float SlamDamageFor(float playerMax) => Mathf.Min(slamDamage, maxHitShare) * Mathf.Max(0f, playerMax);

        /// <summary>Tests and tools: a boss without an asset.</summary>
        public static BossData Create(Enemy body, float health = 3200f, float introDelay = 2f, float spawnWarning = 1.2f,
            float slamTelegraph = 1f, float slamDamage = 0.22f, float maxHitShare = 0.35f, int rewardXp = 600, int rewardWisps = 6,
            float slamRange = 4f, float slamRadius = 3f, float slamReach = 1.6f, float firstSlamDelay = 1.5f, float slamCooldown = 3.2f,
            float slamRecover = 0.7f)
        {
            var data = CreateInstance<BossData>();
            data.prefab = body;
            data.maxHealth = health;
            data.introDelay = introDelay;
            data.spawnWarning = spawnWarning;
            data.slamTelegraph = slamTelegraph;
            data.slamDamage = slamDamage;
            data.maxHitShare = maxHitShare;
            data.rewardXp = rewardXp;
            data.rewardWisps = rewardWisps;
            data.slamRange = slamRange;
            data.slamRadius = slamRadius;
            data.slamReach = slamReach;
            data.firstSlamDelay = firstSlamDelay;
            data.slamCooldown = slamCooldown;
            data.slamRecover = slamRecover;
            return data;
        }
    }
}
