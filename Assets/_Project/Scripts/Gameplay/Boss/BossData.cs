using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// One boss (KAN-22 / KAN-58 / KAN-61): name, body, health, its three attacks (slam, projectile fan, charge dash), the
    /// second phase, the reward and how the horde behaves during the fight. All numbers live here, so balance needs no
    /// code. Every attack warns at least <see cref="MinTelegraph"/> s and no single hit takes more than
    /// <see cref="maxHitShare"/> of the Knight's max health. Phase 1: slam + fan. Phase 2 (at <see cref="Phase2At"/>
    /// health): shorter cooldowns and wind-ups, a faster walk and the charge dash. Edit the asset; the builder never
    /// overwrites it (it only fills an empty prefab slot). Hearts have their own asset (<see cref="HeartRules"/>).
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

        [Header("Projectile fan (KAN-61)")]
        [Tooltip("Bolts per volley. 0 = no fan.")]
        [SerializeField, Range(0, LineTelegraph.MaxLanes)] private int fanBolts = 5;
        [Tooltip("Full spread of the volley (degrees), aimed at the Knight.")]
        [SerializeField, Range(0f, 120f)] private float fanSpread = 56f;
        [Tooltip("The fan is used when the Knight is out of slam range but within this distance (m).")]
        [SerializeField, Min(1f)] private float fanRange = 13f;
        [Tooltip("Seconds the red lanes fill before the volley flies. Never below 0.6 s.")]
        [SerializeField, Min(MinTelegraph)] private float fanTelegraph = 0.9f;
        [SerializeField, Min(1f)] private float fanSpeed = 9f;
        [Tooltip("How far a bolt flies (m) unless a pillar or the ring wall stops it.")]
        [SerializeField, Min(1f)] private float fanDistance = 15f;
        [Tooltip("Width of a telegraph lane (m).")]
        [SerializeField, Min(0.1f)] private float fanLaneWidth = 0.7f;
        [Tooltip("A bolt hits the Knight inside this radius around its centre (m).")]
        [SerializeField, Min(0.05f)] private float fanHitRadius = 0.7f;
        [Tooltip("Damage of a bolt as a share of max health. One volley hits the Knight at most once.")]
        [SerializeField, Range(0f, 1f)] private float fanDamage = 0.12f;
        [SerializeField, Min(0f)] private float fanRecover = 0.5f;
        [SerializeField, Min(0f)] private float fanCooldown = 3.4f;

        [Header("Charge dash (KAN-61, phase 2 only)")]
        [SerializeField] private bool chargeEnabled = true;
        [Tooltip("The charge is used when the Knight is between these distances (m).")]
        [SerializeField, Min(0f)] private float chargeMinRange = 4.5f;
        [SerializeField, Min(1f)] private float chargeMaxRange = 13f;
        [Tooltip("Seconds the red line fills before the dash. Never below 0.6 s.")]
        [SerializeField, Min(MinTelegraph)] private float chargeTelegraph = 0.9f;
        [SerializeField, Min(1f)] private float chargeSpeed = 15f;
        [Tooltip("Longest dash (m). It stops early at a pillar or the ring wall.")]
        [SerializeField, Min(1f)] private float chargeDistance = 12f;
        [Tooltip("Width of the danger lane (m): the Knight inside it when the boss passes is hit.")]
        [SerializeField, Min(0.5f)] private float chargeWidth = 2.4f;
        [Tooltip("Damage as a share of max health (once per dash).")]
        [SerializeField, Range(0f, 1f)] private float chargeDamage = 0.2f;
        [Tooltip("Seconds the boss stands still after the dash (the punish window).")]
        [SerializeField, Min(0f)] private float chargeRecover = 1f;
        [SerializeField, Min(0f)] private float chargeCooldown = 3.8f;

        [Header("Phase 2 (KAN-61)")]
        [Tooltip("Phase 2 starts when boss health falls to this share. 0 = no phase 2.")]
        [SerializeField, Range(0f, 0.95f)] private float phase2At = 0.5f;
        [Tooltip("Attack cooldowns x this in phase 2 (below 1 = more often).")]
        [SerializeField, Range(0.2f, 1f)] private float phase2Cooldown = 0.7f;
        [Tooltip("Wind-ups x this in phase 2. The 0.6 s floor still holds.")]
        [SerializeField, Range(0.3f, 1f)] private float phase2Telegraph = 0.85f;
        [Tooltip("Boss walk speed x this in phase 2.")]
        [SerializeField, Range(1f, 2f)] private float phase2Speed = 1.25f;

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
        public int FanBolts => fanBolts;
        public float FanSpread => fanSpread;
        public float FanRange => fanRange;
        public float FanTelegraph => Mathf.Max(MinTelegraph, fanTelegraph);
        public float FanSpeed => fanSpeed;
        public float FanDistance => fanDistance;
        public float FanLaneWidth => fanLaneWidth;
        public float FanHitRadius => fanHitRadius;
        public float FanRecover => fanRecover;
        public float FanCooldown => fanCooldown;
        public bool ChargeEnabled => chargeEnabled;
        public float ChargeMinRange => chargeMinRange;
        public float ChargeMaxRange => chargeMaxRange;
        public float ChargeTelegraph => Mathf.Max(MinTelegraph, chargeTelegraph);
        public float ChargeSpeed => chargeSpeed;
        public float ChargeDistance => chargeDistance;
        public float ChargeWidth => chargeWidth;
        public float ChargeRecover => chargeRecover;
        public float ChargeCooldown => chargeCooldown;
        public float Phase2At => phase2At;
        public float Phase2Cooldown => phase2Cooldown;
        public float Phase2Telegraph => phase2Telegraph;
        public float Phase2Speed => phase2Speed;
        public int RewardXp => rewardXp;
        public int RewardWisps => rewardWisps;
        public float HordeCap => hordeCap;
        public float HordeInterval => hordeInterval;

        /// <summary>Slam damage for a Knight with <paramref name="playerMax"/> health, capped at <see cref="maxHitShare"/>.</summary>
        public float SlamDamageFor(float playerMax) => HitFor(slamDamage, playerMax);

        /// <summary>Damage of one fan bolt, capped like every boss hit.</summary>
        public float FanDamageFor(float playerMax) => HitFor(fanDamage, playerMax);

        /// <summary>Damage of the charge dash, capped like every boss hit.</summary>
        public float ChargeDamageFor(float playerMax) => HitFor(chargeDamage, playerMax);

        /// <summary>Wind-up of <paramref name="baseTelegraph"/> s in <paramref name="phase"/>: phase 2 is quicker, never under 0.6 s.</summary>
        public float TelegraphFor(float baseTelegraph, int phase) =>
            Mathf.Max(MinTelegraph, baseTelegraph * (phase >= 2 ? phase2Telegraph : 1f));

        /// <summary>Cooldown of <paramref name="baseCooldown"/> s in <paramref name="phase"/>: phase 2 attacks more often.</summary>
        public float CooldownFor(float baseCooldown, int phase) => baseCooldown * (phase >= 2 ? phase2Cooldown : 1f);

        private float HitFor(float share, float playerMax) => Mathf.Min(share, maxHitShare) * Mathf.Max(0f, playerMax);

        /// <summary>
        /// Tests and tools: a boss without an asset. Slam only unless asked: <paramref name="fanBolts"/> 0 = no fan,
        /// <paramref name="charge"/> false = no dash (the asset defaults have both). Phase 2 at <paramref name="phase2At"/>.
        /// </summary>
        public static BossData Create(Enemy body, float health = 3200f, float introDelay = 2f, float spawnWarning = 1.2f,
            float slamTelegraph = 1f, float slamDamage = 0.22f, float maxHitShare = 0.35f, int rewardXp = 600, int rewardWisps = 6,
            float slamRange = 4f, float slamRadius = 3f, float slamReach = 1.6f, float firstSlamDelay = 1.5f, float slamCooldown = 3.2f,
            float slamRecover = 0.7f, int fanBolts = 0, float fanTelegraph = 0.9f, float fanDamage = 0.12f, bool charge = false,
            float chargeTelegraph = 0.9f, float chargeDamage = 0.2f, float phase2At = 0.5f)
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
            data.fanBolts = fanBolts;
            data.fanTelegraph = fanTelegraph;
            data.fanDamage = fanDamage;
            data.chargeEnabled = charge;
            data.chargeTelegraph = chargeTelegraph;
            data.chargeDamage = chargeDamage;
            data.phase2At = phase2At;
            return data;
        }
    }
}
