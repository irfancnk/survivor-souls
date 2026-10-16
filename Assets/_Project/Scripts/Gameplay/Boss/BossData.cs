using System;
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
    /// KAN-62: intro (Knight held, camera pulled out), victory beat (slow motion, then reward + portal) and the guaranteed
    /// reward pick. <see cref="Create"/> has no intro and no beat (tests of the fight itself).
    /// KAN-163: boss hits hurt a lot (3 clean hits of the biggest attack kill a fresh Knight): max-hit cap 0.5, and phase 2
    /// hits x <see cref="Phase2Damage"/> on top (still capped). <see cref="DamageFor(float, float, int)"/> is the one formula.
    /// KAN-108: the fight runs a list of attacks (<see cref="Attacks"/>, <see cref="BossAttack"/> entries per phase). A boss
    /// with an empty list (the Drowned Warden asset) gets the list built from its slam / fan / charge fields above, so its
    /// fight plays as before. New bosses (The Butcher of Ashgrave) fill the list; the slam / fan / charge fields are unused.
    /// KAN-164.2 aggression model: shorter cooldowns with no summon alive (<see cref="AloneCooldownScale"/>), back-to-back
    /// <see cref="Combos"/> from phase 2, and the enrage (phase 3) under <see cref="Phase3At"/>. The defaults switch all of
    /// it off, so an asset without these fields fights as before.
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
        [SerializeField, Min(1f)] private float maxHealth = 7200f; // KAN-62 balance: 10 picks win Chamber 5 in ~73 s

        [Header("Arrival")]
        [Tooltip("Scaled seconds after the Chamber starts before the spawn warning shows.")]
        [SerializeField, Min(0f)] private float introDelay = 2f;
        [Tooltip("Seconds the red ground warning shows where the boss will rise.")]
        [SerializeField, Min(0.1f)] private float spawnWarning = 1.2f;
        [Tooltip("Distance from the Knight where the boss rises.")]
        [SerializeField, Min(2f)] private float spawnDistance = 8f;

        [Header("Intro and victory (KAN-62)")]
        [Tooltip("Seconds after the boss rises: it roars in place, the camera pulls out, the name slides in. The Knight cannot move "
            + "and takes no damage. 0 = no intro.")]
        [SerializeField, Min(0f)] private float introTime = 2.4f;
        [Tooltip("Camera view width x this during the intro (above 1 = pulled out).")]
        [SerializeField, Range(1f, 2f)] private float introZoom = 1.35f;
        [Tooltip("Scaled seconds from the kill to the reward and the open portal (the victory beat). 0 = at once.")]
        [SerializeField, Min(0f)] private float victoryBeat = 1.8f;
        [Tooltip("Time scale right after the kill (slow motion), easing back to 1.")]
        [SerializeField, Range(0.05f, 1f)] private float victorySlowMo = 0.3f;
        [Tooltip("Real seconds of the slow motion (half held, half easing back).")]
        [SerializeField, Min(0f)] private float victorySlowMoTime = 0.8f;
        [Tooltip("Guaranteed reward pick after the beat: a Rare Reliquary chest with this many cards (or a skill pick when no "
            + "owned skill can level up).")]
        [SerializeField, Range(1, 4)] private int rewardChoices = 3;
        [Tooltip("Levels the reward chest's pick adds to the chosen skill.")]
        [SerializeField, Range(1, 3)] private int rewardUpgrades = 2;

        [Header("Attack list (KAN-108)")]
        [Tooltip("Attacks of this boss. Empty = built from the slam / fan / charge fields below (the Warden).")]
        [SerializeField] private BossAttack[] attacks = new BossAttack[0];
        [Tooltip("Animator state of the body during the intro and the phase 2 roar. Empty or missing = its Attack state.")]
        [SerializeField] private string roarClip = "";
        [Tooltip("Seconds the boss stands and roars when phase 2 starts (when it is not mid-attack). 0 = no roar.")]
        [SerializeField, Min(0f)] private float phaseRoar;

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
        [SerializeField, Range(0.05f, 1f)] private float maxHitShare = 0.5f; // KAN-163: was 0.35

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
        [Tooltip("KAN-163: boss hit damage x this in phase 2 (the max-hit cap still holds). 1 = same as phase 1.")]
        [SerializeField, Range(1f, 2f)] private float phase2Damage = 1.15f;

        [Header("Reward")]
        [Tooltip("XP (= souls) dropped when the boss dies, split over the wisps below.")]
        [SerializeField, Min(0)] private int rewardXp = 600;
        [SerializeField, Range(1, 24)] private int rewardWisps = 6;

        [Header("Horde during the fight")]
        [Tooltip("Max alive / burst size x this while the boss Chamber runs.")]
        [SerializeField, Range(0f, 1f)] private float hordeCap = 0.45f;
        [Tooltip("Spawn intervals x this while the boss Chamber runs (above 1 = slower).")]
        [SerializeField, Range(1f, 4f)] private float hordeInterval = 1.6f;

        [Header("Aggression (KAN-164.2)")]
        [Tooltip("Attack cooldowns x this while none of the boss's summons is alive (the duel). 1 = off.")]
        [SerializeField, Range(0.2f, 1f)] private float aloneCooldownScale = 1f;
        [Tooltip("Back-to-back attack pairs (indices into the attack list): when the first ends, the second may start at once.")]
        [SerializeField] private BossCombo[] combos = new BossCombo[0];
        [Tooltip("Chance a finished combo opener chains into its partner (phase 2, no summon alive). 0 = no combos.")]
        [SerializeField, Range(0f, 1f)] private float comboChance;
        [Tooltip("Seconds of chase between the opener's end and the partner's warning.")]
        [SerializeField, Min(0f)] private float comboGap = 0.25f;
        [Tooltip("Enrage (phase 3) starts when boss health falls to this share, never before phase 2. 0 = no enrage.")]
        [SerializeField, Range(0f, 0.95f)] private float phase3At;
        [Tooltip("Attack cooldowns x this while enraged (on top of phase 2 and alone).")]
        [SerializeField, Range(0.2f, 1f)] private float enrageCooldownScale = 0.8f;
        [Tooltip("Bolt / orb / shockwave speed x this while enraged.")]
        [SerializeField, Range(1f, 1.5f)] private float enrageBoltSpeed = 1.15f;
        [Tooltip("Boss walk speed x this while enraged (on top of the phase 2 speed).")]
        [SerializeField, Range(1f, 1.5f)] private float enrageWalk = 1.1f;
        [Tooltip("Combo chance while enraged (the higher of this and the phase 2 chance).")]
        [SerializeField, Range(0f, 1f)] private float enrageComboChance;
        [Tooltip("Seconds the boss stands and roars when the enrage starts (when it is not mid-attack). 0 = no roar.")]
        [SerializeField, Min(0f)] private float enrageRoar;

        [Header("Bolt look (KAN-164.3)")]
        [Tooltip("Look of this boss's bolts: Ember (default), Cleaver (Butcher), Fireball (Pyre Lord), Boulder (Colossus), " +
                 "Skull (Matriarch).")]
        [SerializeField] private BossProjectiles.BoltLook boltLook;

        /// <summary>KAN-239: look of this boss's bolts.</summary>
        public BossProjectiles.BoltLook BoltLook => boltLook;

        /// <summary>KAN-239 tests and tools: sets the bolt look.</summary>
        public BossData WithBoltLook(BossProjectiles.BoltLook look)
        {
            boltLook = look;
            return this;
        }

        public string DisplayName => displayName;
        public string Title => title;
        public Enemy Prefab => prefab;
        public float MaxHealth => maxHealth;
        public float IntroDelay => introDelay;
        public float SpawnWarning => spawnWarning;
        public float SpawnDistance => spawnDistance;
        public float IntroTime => introTime;
        public float IntroZoom => introZoom;
        public float VictoryBeat => victoryBeat;
        public float VictorySlowMo => victorySlowMo;
        public float VictorySlowMoTime => victorySlowMoTime;
        public int RewardChoices => rewardChoices;
        public int RewardUpgrades => rewardUpgrades;
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
        public float Phase2Damage => phase2Damage;
        public string RoarClip => roarClip;
        public float PhaseRoar => phaseRoar;
        public float MaxHitShare => maxHitShare;
        public int RewardXp => rewardXp;
        public int RewardWisps => rewardWisps;
        public float HordeCap => hordeCap;
        public float HordeInterval => hordeInterval;
        public float AloneCooldownScale => aloneCooldownScale;
        public BossCombo[] Combos => combos ?? Array.Empty<BossCombo>();
        public float ComboChance => comboChance;
        public float ComboGap => comboGap;
        public float Phase3At => phase3At;
        public float EnrageCooldownScale => enrageCooldownScale;
        public float EnrageBoltSpeed => enrageBoltSpeed;
        public float EnrageWalk => enrageWalk;
        public float EnrageComboChance => enrageComboChance;
        public float EnrageRoar => enrageRoar;

        /// <summary>KAN-164.2: cooldown factor of the aggression model: x alone share with no summon alive, x enrage share.</summary>
        public float AggressionScale(bool alone, bool enraged) =>
            (alone ? aloneCooldownScale : 1f) * (enraged ? enrageCooldownScale : 1f);

        /// <summary>KAN-164.2: chance a combo opener chains in <paramref name="phase"/> (none in phase 1).</summary>
        public float ComboChanceFor(int phase, bool enraged) =>
            phase < 2 ? 0f : enraged ? Mathf.Max(comboChance, enrageComboChance) : comboChance;

        /// <summary>KAN-164.2: speed of a boss bolt worth <paramref name="speed"/> m/s (faster while enraged).</summary>
        public float BoltSpeedFor(float speed, bool enraged) => enraged ? speed * enrageBoltSpeed : speed;

        /// <summary>
        /// Tests and tools (KAN-164.2): sets the aggression model on this (runtime) instance and returns it. Never call it
        /// on an asset in the editor (it would be saved).
        /// </summary>
        public BossData WithAggression(float aloneScale = 1f, BossCombo[] comboList = null, float chance = 0f, float gap = 0.25f,
            float enrageAt = 0f, float enrageCooldown = 0.8f, float enrageSpeed = 1.15f, float walk = 1.1f, float enrageChance = 0f,
            float roar = 0f)
        {
            aloneCooldownScale = Mathf.Clamp(aloneScale, 0.2f, 1f);
            combos = comboList ?? new BossCombo[0];
            comboChance = Mathf.Clamp01(chance);
            comboGap = Mathf.Max(0f, gap);
            phase3At = Mathf.Clamp(enrageAt, 0f, 0.95f);
            enrageCooldownScale = Mathf.Clamp(enrageCooldown, 0.2f, 1f);
            enrageBoltSpeed = Mathf.Clamp(enrageSpeed, 1f, 1.5f);
            enrageWalk = Mathf.Clamp(walk, 1f, 1.5f);
            enrageComboChance = Mathf.Clamp01(enrageChance);
            enrageRoar = Mathf.Max(0f, roar);
            return this;
        }

        [NonSerialized] private BossAttack[] _legacy;

        /// <summary>
        /// The attack list the fight runs: <see cref="attacks"/>, or (when empty) the Warden list from the slam / fan /
        /// charge fields: slam up close (priority 1), fan farther, charge in phase 2 (charge weight 2: it goes first, then
        /// fan and charge take turns, as in KAN-61).
        /// </summary>
        public BossAttack[] Attacks => attacks != null && attacks.Length > 0 ? attacks : _legacy ??= LegacyAttacks();

        /// <summary>True when the list comes from the asset (not built from the Warden fields).</summary>
        public bool HasAttackList => attacks != null && attacks.Length > 0;

        private BossAttack[] LegacyAttacks()
        {
            var list = new System.Collections.Generic.List<BossAttack>(3)
            {
                BossAttack.Slam("Slam", BossPhases.Both, slamRange, slamRadius, slamReach, slamTelegraph, slamDamage,
                    slamRecover, slamCooldown),
            };
            if (fanBolts > 0)
                list.Add(BossAttack.Fan("Fan", BossPhases.Both, fanRange, fanBolts, fanSpread, fanTelegraph, fanSpeed, fanDistance,
                    fanLaneWidth, fanHitRadius, fanDamage, fanRecover, fanCooldown));
            if (chargeEnabled)
                list.Add(BossAttack.Charge("Charge", BossPhases.Phase2, chargeMinRange, chargeMaxRange, chargeTelegraph, chargeSpeed,
                    chargeDistance, chargeWidth, chargeDamage, chargeRecover, chargeCooldown, weight: 2f));
            return list.ToArray();
        }

        /// <summary>Damage of a hit worth <paramref name="share"/> of max health, capped at the max-hit share.</summary>
        public float DamageFor(float share, float playerMax) => HitFor(share, playerMax);

        /// <summary>
        /// KAN-163: share of max health a hit worth <paramref name="share"/> takes in <paramref name="phase"/>: phase 2 x
        /// <see cref="Phase2Damage"/>, then the max-hit cap.
        /// </summary>
        public float ShareFor(float share, int phase) =>
            Mathf.Min(Mathf.Max(0f, share) * (phase >= 2 ? phase2Damage : 1f), maxHitShare);

        /// <summary>KAN-163: damage of a hit worth <paramref name="share"/> in <paramref name="phase"/> (<see cref="ShareFor"/>).</summary>
        public float DamageFor(float share, float playerMax, int phase) => ShareFor(share, phase) * Mathf.Max(0f, playerMax);

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
        /// Phase 2 hits x <paramref name="phase2Damage"/> (default 1: plain numbers; the asset default is 1.15).
        /// </summary>
        public static BossData Create(Enemy body, float health = 3200f, float introDelay = 2f, float spawnWarning = 1.2f,
            float slamTelegraph = 1f, float slamDamage = 0.22f, float maxHitShare = 0.35f, int rewardXp = 600, int rewardWisps = 6,
            float slamRange = 4f, float slamRadius = 3f, float slamReach = 1.6f, float firstSlamDelay = 1.5f, float slamCooldown = 3.2f,
            float slamRecover = 0.7f, int fanBolts = 0, float fanTelegraph = 0.9f, float fanDamage = 0.12f, bool charge = false,
            float chargeTelegraph = 0.9f, float chargeDamage = 0.2f, float phase2At = 0.5f, float introTime = 0f,
            float victoryBeat = 0f, float phase2Damage = 1f)
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
            data.introTime = introTime;
            data.victoryBeat = victoryBeat;
            data.phase2Damage = phase2Damage;
            return data;
        }

        /// <summary>
        /// Tests and the builder (KAN-108): a boss that runs <paramref name="attackList"/>. No intro and no beat unless asked.
        /// </summary>
        public static BossData CreateWithAttacks(Enemy body, BossAttack[] attackList, float health = 3200f, float introDelay = 2f,
            float spawnWarning = 1.2f, float firstAttackDelay = 1.5f, float maxHitShare = 0.35f, float phase2At = 0.5f,
            float introTime = 0f, float victoryBeat = 0f, string roarClip = "", float phaseRoar = 0f, int rewardXp = 600,
            int rewardWisps = 6, string displayName = null, string title = null, float phase2Damage = 1f)
        {
            var data = Create(body, health, introDelay, spawnWarning, maxHitShare: maxHitShare, rewardXp: rewardXp,
                rewardWisps: rewardWisps, firstSlamDelay: firstAttackDelay, phase2At: phase2At, introTime: introTime,
                victoryBeat: victoryBeat, phase2Damage: phase2Damage);
            data.attacks = attackList ?? new BossAttack[0];
            data.roarClip = roarClip ?? "";
            data.phaseRoar = phaseRoar;
            if (displayName != null) data.displayName = displayName;
            if (title != null) data.title = title;
            return data;
        }
    }
}
