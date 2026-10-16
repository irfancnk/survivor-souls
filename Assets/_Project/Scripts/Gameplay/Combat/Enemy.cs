using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.World;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>Where a hit came from. Feedback filters on it (burn ticks get no hit marker, crits a bigger number).</summary>
    public enum DamageKind { Direct, Burn, Burst }

    /// <summary>
    /// Melee chaser: walks to the player and stops at arm's length. Has health; <see cref="TakeDamage"/>
    /// knocks it back and flashes the body. At 0 health it falls, sinks into the ground and returns to its pool.
    /// Optional <see cref="animator"/> plays an in-place locomotion clip; it freezes when the enemy dies.
    /// Living enemies are listed in <see cref="Active"/> (targeting, health bars). Pooled by <see cref="EnemySpawner"/>.
    /// Status: <see cref="ApplyBurn"/> (KAN-19 skill) deals fire damage in ticks and glows the body ember orange.
    /// Steering (KAN-29): with a <see cref="Walkability"/> set, a chaser whose way is blocked (rock, wall, pier) turns to
    /// the first free heading on a remembered side, and a step into an obstacle slides along it. No physics queries.
    /// Horde (KAN-30): the steering probe runs in time slices (every <see cref="SteerInterval"/> s, staggered per enemy;
    /// the wall slide still runs every frame), separation uses the per-frame <see cref="EnemyCrowd"/> grid, and each spawn
    /// rolls its own speed and size so a crowd never moves in lockstep. With <see cref="animRefSpeed"/> set, the walk
    /// clip's playback rate follows the real ground speed (no foot sliding) and an "Attack" state plays at the player.
    /// Walk / attack switching (KAN-43): enter and leave reach differ (hysteresis) and each state holds a minimum time, so
    /// crowd jostling at the edge of reach cannot keep the animator in half-finished cross-fades; a staggered check
    /// re-requests the wanted state when the animator rests in the other one, and the playback rate is always finite.
    /// Boss (KAN-58): <see cref="isBoss"/> hides the small health bar (the HUD has a boss bar), <see cref="knockbackScale"/>
    /// makes it hard to push, and <see cref="Halted"/> lets an attack pattern stop the chase (it plays the attack state).
    /// Hit / Death states (KAN-104 roster): when the controller has them, a direct hit plays a short "Hit" flinch (not while
    /// attacking, not on bosses, at most every <see cref="HitCooldown"/> s) and a kill plays "Death" for
    /// <see cref="deathHold"/> s before the body sinks. Without them (old zombies) the body tips over as before.
    /// Keep distance (KAN-105 ranged): with a hold band (<see cref="holdMin"/>..<see cref="holdMax"/> &gt; 0) the enemy walks in
    /// until it is inside the band, backs off when the Knight comes closer than the band, and stands facing the Knight
    /// inside it. <see cref="NeedsClearShot"/> (set by <see cref="RangedAttack"/> when a pillar blocks the line of fire)
    /// makes it walk in again. 0 / 0 = melee chaser.
    /// Burn control (KAN-134): <see cref="ApplySlow"/> lowers the move speed for a while (highest slow wins, no stacking,
    /// bosses take half) and <see cref="Stun"/> stops the chase for a moment and plays the Hit flinch (not on bosses, with
    /// its own per-enemy cooldown). Ember Bolts uses both when a bolt sets a foe on fire.
    /// Freeze (KAN-91 Freeze Arrow): <see cref="Freeze"/> roots the foe for a while with an ice tint: no chase, no turning,
    /// no contact damage (<see cref="ContactDamageReceiver"/>), its special / ranged / hex attacks pause, and the body holds
    /// its pose (animator stopped, no Hit flinch). Bosses are only rooted and tinted (their attack patterns and clips keep
    /// their timing). A per-enemy cooldown (from the freeze start) stops a fast fire rate from locking a foe.
    /// </summary>
    public sealed class Enemy : MonoBehaviour
    {
        /// <summary>Alive enemies (not dying). Read-only for other systems.</summary>
        public static readonly List<Enemy> Active = new List<Enemy>();

        /// <summary>
        /// Raised on every hit: enemy, health deducted, killing blow. During the call <see cref="LastDamageKind"/> and
        /// <see cref="LastHitCrit"/> describe the hit.
        /// </summary>
        public static event Action<Enemy, float, bool> Damaged;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Min(1f)] private float maxHealth = 150f;
        [SerializeField, Min(0f)] private float moveSpeed = 2.2f;
        [SerializeField, Min(0f)] private float turnSpeed = 540f; // degrees per second
        [SerializeField, Min(0f)] private float stopDistance = 1.1f;
        [SerializeField, Min(0.05f)] private float hitRadius = 0.45f;
        [SerializeField, Min(0.5f)] private float height = 1.8f;
        [SerializeField, Min(0f)] private float separation = 0.9f;
        [Tooltip("Centre-to-centre distance (m) at which this enemy hurts the player. Keep above stopDistance.")]
        [SerializeField, Min(0f)] private float contactRange = 1.4f;
        [Tooltip("Damage per second dealt to the player while within contactRange (ContactDamageReceiver).")]
        [SerializeField, Min(0f)] private float damagePerSecond = 20f;
        [Tooltip("XP in the soul wisp this enemy drops on death (KAN-21). Wisp size and colour follow the value.")]
        [SerializeField, Min(0)] private int xpValue = 10;
        [SerializeField] private Renderer body;
        [Tooltip("Body colour. Equal to the material colour: then no property block is set and the SRP Batcher keeps the body.")]
        [SerializeField] private Color baseTint = Color.white;
        [SerializeField] private Animator animator;
        [Tooltip("Share of the bolt knockback this type takes (bosses: low).")]
        [SerializeField, Range(0f, 1f)] private float knockbackScale = 1f;
        [Tooltip("Boss (KAN-58): no small health bar over the head; the HUD boss bar shows it.")]
        [SerializeField] private bool isBoss;

        [Header("Horde variety (KAN-30)")]
        [Tooltip("Root scale of this type (brutes are bigger). Height, hit radius and separation scale with it.")]
        [SerializeField, Min(0.1f)] private float bodyScale = 1f;
        [Tooltip("Per-spawn random spread of move speed (0.1 = +-10%).")]
        [SerializeField, Range(0f, 0.3f)] private float speedJitter;
        [Tooltip("Per-spawn random spread of body size (0.05 = +-5%).")]
        [SerializeField, Range(0f, 0.2f)] private float scaleJitter;

        [Header("Animation sync (KAN-30)")]
        [Tooltip("Ground speed (m/s) of the walk clip at animator speed 1 and scale 1 (measured by the builder). " +
                 "Animator speed = real ground speed / (this x scale), so the feet stay planted. 0 = free-running clip.")]
        [SerializeField, Min(0f)] private float animRefSpeed;
        [SerializeField] private string walkState = "Walk";
        [Tooltip("Played while the player is within contact range. Skipped when the controller has no such state.")]
        [SerializeField] private string attackState = "Attack";
        [Tooltip("Short flinch on a direct hit (KAN-104). Skipped when the controller has no such state.")]
        [SerializeField] private string hitState = "Hit";
        [Tooltip("Played on death (KAN-104). Without it the body tips over backwards.")]
        [SerializeField] private string deathState = "Death";
        [Tooltip("Seconds of the Hit state before the walk / attack resumes.")]
        [SerializeField, Min(0f)] private float hitHold = 0.4f;
        [Tooltip("Seconds the Death state plays before the body sinks (the builder sets it to the state's play time).")]
        [SerializeField, Min(0f)] private float deathHold = 0.9f;

        [Header("Keep distance (KAN-105)")]
        [Tooltip("Ranged types: nearest distance (m) to the Knight. Closer = back off. 0 with holdMax 0 = melee chaser.")]
        [SerializeField, Min(0f)] private float holdMin;
        [Tooltip("Ranged types: farthest distance (m) to the Knight. Farther = walk in. 0 = melee chaser.")]
        [SerializeField, Min(0f)] private float holdMax;

        private const float KnockbackSpeed = 4f;
        private const float FlashTime = 0.12f;
        private const float FallTime = 0.35f;
        private const float SinkTime = 0.7f;
        private const float BurnTickInterval = 0.5f;
        private static readonly Color BurnTint = new Color(1.9f, 0.85f, 0.35f);
        private static readonly Color HexTint = new Color(1.35f, 0.62f, 1.9f); // KAN-107 Hexbound Shaman buff (violet)
        private const float BodyRadius = 0.35f;     // ground footprint for wall tests
        private const float ProbeDistance = 0.9f;   // look-ahead for steering
        private const float DetourStep = 35f;       // degrees per detour try
        private const int DetourTries = 4;          // up to 140 degrees off the direct heading
        /// <summary>Seconds between steering probes of one enemy (staggered). The wall slide still runs every frame.</summary>
        public const float SteerInterval = 0.12f;
        private const float MinWalkRate = 0.2f;     // animator speed floor while shuffling (crowded, nearly still)
        private const float MaxWalkRate = 2.2f;
        private const float AnimBlend = 0.18f;      // walk <-> attack cross-fade (s)
        private const float AttackReach = 0.15f;    // attack starts this far outside contact range
        /// <summary>Attack ends only beyond contact range + this (m). Wider than the start reach: no dithering at one line.</summary>
        public const float AttackRelease = 0.45f;
        /// <summary>Minimum seconds in walk or attack before the next switch.</summary>
        public const float MinStateHold = 0.3f;
        /// <summary>Seconds between checks that the animator plays the wanted state (staggered per enemy).</summary>
        public const float AnimCheckInterval = 0.25f;
        /// <summary>Minimum seconds between two Hit flinches of one enemy, so a horde under fire still walks.</summary>
        public const float HitCooldown = 1.2f;
        private const float HitBlend = 0.06f;
        /// <summary>Back-off speed as a share of the move speed (ranged types walking away from the Knight).</summary>
        public const float BackOffSpeed = 0.8f;
        /// <summary>A holding ranged enemy settles this far (share of the band width) round the band middle before it stops.</summary>
        private const float SettleShare = 0.25f;
        /// <summary>A view-capped hold band never comes closer than this (m) or gets narrower than <see cref="MinBandWidth"/>.</summary>
        public const float MinHoldDistance = 3.5f, MinBandWidth = 1.5f;

        private Transform _target;
        private Action<Enemy> _release;
        private MaterialPropertyBlock _block;
        private float _health;
        private float _maxHealth; // this spawn: type max x Chamber multiplier (KAN-46)
        private float _flash;
        private float _deadTime = -1f;
        private Vector3 _knock;
        private Quaternion _deathStart;
        private float _burnDps;
        private float _burnLeft;
        private float _burnTick;
        private float _burnPhase; // per enemy, so a burning crowd does not pulse in sync
        private bool _burnSpread; // KAN-77: this fire was caught from a burning neighbour, not lit by a bolt
        private float _hexLeft;
        private float _hexBoost = 1f;
        private Color _tint = Color.clear;
        private Color _calmTint = Color.white; // base tint x danger tier colour (KAN-92)
        private Material[] _sourceMaterials;   // the body's own materials (tier step 0)
        private int _tierStep;
        private float _detourSide = 1f;
        private bool _detouring;
        private float _steerTimer;
        private float _steerAngle;   // current detour, degrees off the direct heading
        private float _speedMul = 1f;
        private float _scale = 1f;
        private float _groundSpeed;  // smoothed real planar speed (m/s) for the walk rate
        private float _attackRate = 1f;
        private bool _attacking;
        private bool _hasAttack;
        private bool _synced;        // walk rate follows ground speed; walk / attack switching from code
        private float _sinceSwitch;  // seconds since the last walk <-> attack cross-fade
        private float _animCheck;    // seconds to the next animator state check
        private int _walkHash;
        private int _attackHash;
        private int _defaultAttackHash; // the prefab's Attack state; boss patterns swap _attackHash (KAN-108)
        private int _hitHash;
        private int _deathHash;
        private bool _hasHit;
        private bool _hasDeath;
        private float _hitLeft;      // seconds left in the current Hit flinch
        private float _hitCooldown;  // seconds until the next flinch may start
        private bool _holding;       // ranged: standing inside the hold band
        private float _slow;         // KAN-134: move speed share lost while _slowLeft > 0
        private float _slowLeft;
        private float _stunLeft;     // KAN-134: seconds the chase stays stopped (ignite flinch)
        private float _stunCooldown; // seconds until the next stun may start
        private float _freezeLeft;     // KAN-91: seconds the foe stays frozen
        private float _freezeCooldown; // seconds until the next freeze may start
        private float _freezeTotal;    // length of the running freeze (tint fade)
        private static readonly Color IceTint = new Color(1.45f, 1.9f, 2.7f); // KAN-91 frozen body: pale frosted blue (HDR lifts the dark albedo)

        /// <summary>Northmost Z an enemy may walk to (the sea edge). Set by the spawner from the world; +inf = no limit.</summary>
        public static float LandMaxZ = float.PositiveInfinity;

        /// <summary>Walls, props and play-area bounds for steering. Set by the spawner from the world; null = walk straight.</summary>
        public static IWalkability Walkability;

        /// <summary>
        /// Times any enemy's animator was found resting in the wrong state and the cross-fade was requested again (KAN-43).
        /// Debug and tests: a steady climb means cross-fades are being lost.
        /// </summary>
        public static int AnimatorResyncs;

        public float Health => _health;
        /// <summary>This spawn's max health (type max x the Chamber multiplier, KAN-46). The type max before the first spawn.</summary>
        public float MaxHealth => _maxHealth > 0f ? _maxHealth : maxHealth;
        /// <summary>Max health of the type, before Chamber scaling.</summary>
        public float BaseMaxHealth => maxHealth;
        public bool IsAlive => _health > 0f;
        /// <summary>Took damage since spawn (health bars only show for these).</summary>
        public bool IsHurt => _health < MaxHealth;
        public float HitRadius => hitRadius * _scale;
        public float Height => height * _scale;
        /// <summary>This spawn's move speed (type speed x random spread x <see cref="SpeedBoost"/> x what <see cref="Slow"/> leaves).</summary>
        public float MoveSpeed => moveSpeed * _speedMul * SpeedBoost * (1f - Slow);
        /// <summary>Extra walk speed factor set by an attack pattern (KAN-61 boss phase 2). Reset to 1 on spawn.</summary>
        public float SpeedBoost { get; set; } = 1f;
        /// <summary>True while a Hexbound Shaman buff runs (KAN-107): faster, violet tint.</summary>
        public bool IsHexed => _hexLeft > 0f && IsAlive;
        /// <summary>Seconds of hex buff left (0 = none).</summary>
        public float HexLeft => IsHexed ? _hexLeft : 0f;
        /// <summary>This spawn's root scale (type scale x random spread).</summary>
        public float Scale => _scale;
        public float AnimRefSpeed => animRefSpeed;
        /// <summary>Playing the attack state (player within reach).</summary>
        public bool IsAttacking => _attacking;
        /// <summary>Playing the Hit flinch (KAN-104).</summary>
        public bool IsFlinching => _hitLeft > 0f;
        /// <summary>The controller has a Death state: the kill plays it instead of the tip-over (KAN-104).</summary>
        public bool HasDeathState => _hasDeath;
        /// <summary>Seconds from the killing blow until the body is back in its pool.</summary>
        public float DeathDuration => (_hasDeath ? deathHold : FallTime) + SinkTime;
        public Animator Animator => animator;
        public float ContactRange => contactRange;
        public float DamagePerSecond => damagePerSecond;
        public int XpValue => xpValue;
        public bool IsBoss => isBoss;
        /// <summary>Ranged type: has a hold band (KAN-105).</summary>
        public bool KeepsDistance => holdMax > 0f;
        public float HoldMin => holdMin;
        public float HoldMax => holdMax;
        /// <summary>Ranged: standing inside the hold band, facing the Knight (ready to shoot).</summary>
        public bool IsHolding => _holding;
        /// <summary>Ranged: the line of fire is blocked (pillar, wall); walk in until it is clear. Cleared on spawn.</summary>
        public bool NeedsClearShot { get; set; }
        /// <summary>
        /// Ranged: ground distance (m) from the Knight to the screen edge in this enemy's direction, minus a margin (set by
        /// <see cref="RangedAttack"/>). Caps the hold band, so a shooter never holds or fires from off screen (the portrait
        /// view is only ~11 m wide). +inf = no camera (tests). Reset on spawn.
        /// </summary>
        public float ViewReach { get; set; } = float.PositiveInfinity;
        /// <summary>Hold band after the <see cref="ViewReach"/> cap: (min, max) in m.</summary>
        public Vector2 EffectiveBand
        {
            get
            {
                float max = Mathf.Max(MinHoldDistance + MinBandWidth, Mathf.Min(holdMax, ViewReach));
                float min = Mathf.Clamp(Mathf.Min(holdMin, max - MinBandWidth), MinHoldDistance, max);
                return new Vector2(min, max);
            }
        }
        /// <summary>Tests and tools: sets the hold band (0 / 0 = melee chaser).</summary>
        public void SetHoldBand(float min, float max)
        {
            holdMin = Mathf.Max(0f, min);
            holdMax = Mathf.Max(holdMin, max);
        }
        /// <summary>
        /// Stops the chase (no walking, no turning) while an attack pattern runs (KAN-58 boss slam). Crowd push and
        /// knockback still move it. A halted enemy with an attack state plays it (wind-up). Cleared on spawn.
        /// </summary>
        public bool Halted { get; set; }
        public bool IsBurning => _burnLeft > 0f && IsAlive;
        /// <summary>Move speed share lost to a slow right now (KAN-134 burn slow), 0..<see cref="MaxSlow"/>.</summary>
        public float Slow => _slowLeft > 0f && IsAlive ? _slow : 0f;
        /// <summary>Seconds of slow left (0 = none).</summary>
        public float SlowLeft => IsAlive ? Mathf.Max(0f, _slowLeft) : 0f;
        /// <summary>The chase is stopped by a <see cref="Stun"/> (KAN-134 ignite flinch).</summary>
        public bool IsStunned => _stunLeft > 0f && IsAlive;
        /// <summary>Seconds until this enemy can be stunned again (0 = ready).</summary>
        public float StunCooldown => Mathf.Max(0f, _stunCooldown);
        /// <summary>Frozen by <see cref="Freeze"/> (KAN-91): rooted, no contact damage, attacks paused, ice tint.</summary>
        public bool IsFrozen => _freezeLeft > 0f && IsAlive;
        /// <summary>Seconds of freeze left (0 when not frozen).</summary>
        public float FreezeLeft => IsFrozen ? _freezeLeft : 0f;
        /// <summary>Seconds until a new <see cref="Freeze"/> may start on this foe.</summary>
        public float FreezeCooldown => Mathf.Max(0f, _freezeCooldown);
        /// <summary>Frozen and not a boss: the body holds its pose and plays no attack.</summary>
        public bool IsFrozenSolid => !isBoss && IsFrozen;
        /// <summary>A slow never takes more than this share of the move speed.</summary>
        public const float MaxSlow = 0.8f;
        /// <summary>Share of a slow a boss takes (KAN-134: bosses half).</summary>
        public const float BossSlowShare = 0.5f;
        public float BurnDps => IsBurning ? _burnDps : 0f;
        /// <summary>Burning from a fire caught from a neighbour (KAN-77 Ember Bolts spread), not from a bolt hit. Spread fires do not spread on.</summary>
        public bool BurnFromSpread => IsBurning && _burnSpread;
        /// <summary>Burn seconds left, also during the <see cref="Damaged"/> call of the killing blow (KAN-77: a burning death blasts).</summary>
        public float BurnTimeLeft => _burnLeft;
        public DamageKind LastDamageKind { get; private set; }
        public bool LastHitCrit { get; private set; }
        /// <summary>Chest height: where bolts aim.</summary>
        public Vector3 AimPoint => transform.position + Vector3.up * (Height * 0.6f);
        /// <summary>Above the head: where the health bar sits.</summary>
        public Vector3 BarAnchor => transform.position + Vector3.up * (Height + 0.3f);

        /// <summary>
        /// Starts this pooled enemy at <paramref name="position"/>. <paramref name="healthMultiplier"/> scales the max
        /// health of this spawn (Chamber difficulty, KAN-46).
        /// </summary>
        public void Spawn(Vector3 position, Transform target, Action<Enemy> release, float healthMultiplier = 1f)
        {
            _target = target;
            _release = release;
            _maxHealth = maxHealth * Mathf.Max(0.01f, healthMultiplier);
            _health = _maxHealth;
            _deadTime = -1f;
            _knock = Vector3.zero;
            _flash = 0f;
            _burnDps = 0f;
            _burnLeft = 0f;
            _burnSpread = false;
            _slow = 0f;
            _slowLeft = 0f;
            _stunLeft = 0f;
            _stunCooldown = 0f;
            _freezeLeft = 0f;
            _freezeCooldown = 0f;
            _burnPhase = UnityEngine.Random.value * Mathf.PI * 2f;
            _detourSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            _detouring = false;
            _steerAngle = 0f;
            _steerTimer = UnityEngine.Random.value * SteerInterval; // staggered: probes spread over the frames
            _speedMul = 1f + UnityEngine.Random.Range(-speedJitter, speedJitter);
            SpeedBoost = 1f;
            _hexLeft = 0f;
            _hexBoost = 1f;
            _scale = bodyScale * (1f + UnityEngine.Random.Range(-scaleJitter, scaleJitter));
            _groundSpeed = MoveSpeed;
            _attacking = false;
            _hitLeft = 0f;
            _hitCooldown = 0f;
            Halted = false;
            NeedsClearShot = false;
            ViewReach = float.PositiveInfinity;
            _holding = false;
            _sinceSwitch = MinStateHold;
            _animCheck = UnityEngine.Random.value * AnimCheckInterval;
            Vector3 toTarget = target != null ? target.position - position : Vector3.forward;
            toTarget.y = 0f;
            transform.SetPositionAndRotation(position, toTarget.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toTarget) : Quaternion.identity);
            transform.localScale = new Vector3(_scale, _scale, _scale);
            gameObject.SetActive(true);
            if (_tierStep != 0) SetDangerStep(null, 0);
            _calmTint = baseTint;
            SetTint(baseTint);
            StartAnimation();
            Active.Add(this);
        }

        /// <summary>Random phase (and, for a free-running clip, a small rate spread), so a crowd does not walk in lockstep.</summary>
        private void StartAnimation()
        {
            if (animator == null) return;
            _walkHash = Animator.StringToHash(walkState);
            _attackHash = _defaultAttackHash = Animator.StringToHash(attackState);
            _hitHash = Animator.StringToHash(hitState);
            _deathHash = Animator.StringToHash(deathState);
            _synced = animRefSpeed > 0f && animator.HasState(0, _walkHash);
            _hasAttack = _synced && animator.HasState(0, _attackHash);
            _hasHit = _synced && !isBoss && hitHold > 0f && animator.HasState(0, _hitHash);
            _hasDeath = animator.HasState(0, _deathHash);
            _attackRate = UnityEngine.Random.Range(0.9f, 1.1f);
            if (_synced)
            {
                animator.speed = WalkRate(_groundSpeed);
                animator.Play(_walkHash, 0, UnityEngine.Random.value);
                return;
            }
            animator.speed = UnityEngine.Random.Range(0.9f, 1.1f);
            animator.Play(0, 0, UnityEngine.Random.value);
        }

        /// <summary>
        /// Boss patterns (KAN-108): the attack state becomes <paramref name="state"/> (e.g. "Chop", "Spin", "Roar") and plays
        /// from its start now; the enemy keeps it while <see cref="Halted"/>. Empty or a state the controller lacks = the
        /// prefab's Attack state. Returns true when the wanted state plays. <see cref="ResetAction"/> goes back.
        /// </summary>
        public bool PlayAction(string state)
        {
            if (animator == null || !_synced) return false;
            int hash = string.IsNullOrEmpty(state) ? _defaultAttackHash : Animator.StringToHash(state);
            bool found = animator.HasState(0, hash);
            if (!found)
            {
                hash = _defaultAttackHash;
                if (!animator.HasState(0, hash)) return false;
            }
            PlayActionState(hash);
            return found;
        }

        /// <summary>
        /// KAN-162: like <see cref="PlayAction"/>, but only when the controller has <paramref name="state"/> (no fallback to
        /// the Attack state). A boss stands (<see cref="StandState"/>) or reels (Roar) while it recovers from an attack.
        /// Returns false and changes nothing when the state is missing.
        /// </summary>
        public bool PlayPose(string state)
        {
            if (animator == null || !_synced || string.IsNullOrEmpty(state)) return false;
            int hash = Animator.StringToHash(state);
            if (!animator.HasState(0, hash)) return false;
            PlayActionState(hash);
            return true;
        }

        /// <summary>Still pose state (the walk clip at speed 0) that boss controllers carry for their recover (KAN-162).</summary>
        public const string StandState = "Stand";

        private void PlayActionState(int hash)
        {
            _attackHash = hash;
            _hasAttack = true;
            _hitLeft = 0f;
            _attacking = true;
            _sinceSwitch = 0f;
            if (animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(hash, AnimBlend, 0, 0f);
        }

        /// <summary>The attack state is the prefab's Attack state again (KAN-108).</summary>
        public void ResetAction() => _attackHash = _defaultAttackHash;

        /// <summary>The animator state the enemy plays when it attacks (KAN-108 tests).</summary>
        public int AttackStateHash => _attackHash;

        /// <summary>Walk clip playback rate that makes the feet cover <paramref name="groundSpeed"/> m/s.</summary>
        public float WalkRate(float groundSpeed) =>
            animRefSpeed > 0f ? Mathf.Clamp(groundSpeed / (animRefSpeed * _scale), MinWalkRate, MaxWalkRate) : 1f;

        /// <summary>
        /// Moves a living enemy to <paramref name="position"/> facing its target, keeping health and status. Used by the
        /// spawner to bring back enemies the player outran, while they are out of view.
        /// </summary>
        public void Relocate(Vector3 position)
        {
            if (!IsAlive) return;
            _knock = Vector3.zero;
            Vector3 toTarget = _target != null ? _target.position - position : transform.forward;
            toTarget.y = 0f;
            transform.SetPositionAndRotation(position, toTarget.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toTarget) : transform.rotation);
        }

        /// <summary>
        /// Applies damage. Returns the health actually deducted. Only <see cref="DamageKind.Direct"/> hits knock back and
        /// flash white; burn and burst damage just take health.
        /// </summary>
        public float TakeDamage(float amount, Vector3 direction, DamageKind kind = DamageKind.Direct, bool crit = false)
        {
            if (!IsAlive || amount <= 0f) return 0f;
            float deducted = Mathf.Min(amount, _health);
            _health -= deducted;
            if (kind == DamageKind.Direct)
            {
                direction.y = 0f;
                _knock = direction.normalized * (KnockbackSpeed * knockbackScale);
                _flash = FlashTime;
            }
            bool killed = _health <= 0f;
            if (killed) BeginDeath();
            else if (kind == DamageKind.Direct) Flinch();
            LastDamageKind = kind;
            LastHitCrit = crit;
            Damaged?.Invoke(this, deducted, killed);
            return deducted;
        }

        /// <summary>
        /// Sets the enemy on fire for <paramref name="duration"/> seconds: <paramref name="dps"/> fire damage in 0.5 s ticks.
        /// A new burn refreshes the time and keeps the stronger damage; it does not stack. <paramref name="spread"/> marks a
        /// fire caught from a neighbour (KAN-77); a bolt's fire on a spread-burning enemy makes it a bolt fire again.
        /// </summary>
        public void ApplyBurn(float dps, float duration, bool spread = false)
        {
            if (!IsAlive || dps <= 0f || duration <= 0f) return;
            if (_burnLeft <= 0f) { _burnTick = BurnTickInterval; _burnSpread = spread; } // first tick after one interval
            else if (!spread) _burnSpread = false;
            _burnDps = _burnLeft > 0f ? Mathf.Max(_burnDps, dps) : dps;
            _burnLeft = Mathf.Max(_burnLeft, duration);
        }

        /// <summary>
        /// Slows the walk by <paramref name="share"/> of the move speed for <paramref name="duration"/> s (KAN-134 burn slow).
        /// No stacking: while a slow runs, the higher share wins and the longer time wins. Bosses take
        /// <see cref="BossSlowShare"/> of the share. Capped at <see cref="MaxSlow"/>. Returns the share now in effect.
        /// </summary>
        public float ApplySlow(float share, float duration)
        {
            if (!IsAlive || share <= 0f || duration <= 0f) return Slow;
            if (isBoss) share *= BossSlowShare;
            share = Mathf.Min(share, MaxSlow);
            _slow = _slowLeft > 0f ? Mathf.Max(_slow, share) : share;
            _slowLeft = Mathf.Max(_slowLeft, duration);
            return _slow;
        }

        /// <summary>
        /// Stops the chase (no walking, no turning; crowd push and knockback still move it) for <paramref name="seconds"/> and
        /// plays the Hit flinch when the controller has one (KAN-134 ignite flinch). Refused for bosses, dead enemies and
        /// while the last stun's <paramref name="cooldown"/> (s, counted from its start) runs, so a fast fire rate cannot
        /// lock a foe. Returns true when the stun took.
        /// </summary>
        public bool Stun(float seconds, float cooldown)
        {
            if (!IsAlive || isBoss || seconds <= 0f || _stunCooldown > 0f) return false;
            _stunLeft = seconds;
            _stunCooldown = Mathf.Max(seconds, cooldown);
            if (_hasHit && !_attacking && _hitLeft <= 0f && !IsFrozenSolid && animator != null && animator.isActiveAndEnabled)
            {
                _hitLeft = hitHold;
                _hitCooldown = HitCooldown;
                animator.speed = 1f;
                animator.CrossFadeInFixedTime(_hitHash, HitBlend, 0, 0f);
            }
            return true;
        }

        /// <summary>
        /// Freezes the foe for <paramref name="seconds"/> (KAN-91 Freeze Arrow): see the class summary for what a frozen foe
        /// does. Refused for dead foes and while the last freeze's <paramref name="cooldown"/> (s, counted from its start)
        /// runs. The caller picks the length (Freeze Arrow gives bosses and elites a shorter one). Returns true when it took.
        /// </summary>
        public bool Freeze(float seconds, float cooldown)
        {
            if (!IsAlive || seconds <= 0f || _freezeCooldown > 0f) return false;
            _freezeLeft = seconds;
            _freezeTotal = seconds;
            _freezeCooldown = Mathf.Max(seconds, cooldown);
            _hitLeft = 0f; // a running flinch ends: the body holds the pose it has now
            return true;
        }

        /// <summary>
        /// Hexbound Shaman buff (KAN-107): <see cref="SpeedBoost"/> = <paramref name="boost"/> and a violet tint for
        /// <paramref name="duration"/> s. A new hex refreshes the time and keeps the stronger boost (no stacking). Bosses
        /// (their own phase speed) and dead enemies ignore it. Returns true when the hex took.
        /// </summary>
        public bool ApplyHex(float boost, float duration)
        {
            if (!IsAlive || isBoss || boost <= 0f || duration <= 0f) return false;
            _hexBoost = _hexLeft > 0f ? Mathf.Max(_hexBoost, boost) : boost;
            _hexLeft = Mathf.Max(_hexLeft, duration);
            SpeedBoost = _hexBoost;
            return true;
        }

        /// <summary>
        /// Dies by its own attack (KAN-106 Plague Bloater burst): plays the death and sinks like a kill, but raises no
        /// <see cref="Damaged"/> event, so it drops no soul wisp and counts as no kill (the Knight did not kill it).
        /// </summary>
        public void Expire()
        {
            if (!IsAlive) return;
            _health = 0f;
            _burnLeft = 0f;
            BeginDeath();
        }

        /// <summary>
        /// KAN-132: a dying body leaves at once (hidden and back to its pool, no Death clip, no sink). The Plague Bloater bursts
        /// into spores instead of standing up to play its Death clip. No-op while alive or already gone.
        /// </summary>
        public void Vanish()
        {
            if (IsAlive || _deadTime < 0f || !gameObject.activeSelf) return;
            gameObject.SetActive(false);
            _release?.Invoke(this);
        }

        /// <summary>Short Hit state on a direct hit, unless attacking, already flinching or flinched within <see cref="HitCooldown"/>.</summary>
        private void Flinch()
        {
            if (!_hasHit || _attacking || _hitLeft > 0f || _hitCooldown > 0f || IsFrozenSolid || !animator.isActiveAndEnabled) return;
            _hitLeft = hitHold;
            _hitCooldown = HitCooldown;
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(_hitHash, HitBlend, 0, 0f);
        }

        private void BeginDeath()
        {
            Active.Remove(this);
            _deadTime = 0f;
            _deathStart = transform.rotation;
            _hitLeft = 0f;
            _freezeLeft = 0f; // KAN-91: a frozen foe thaws as it dies (the death clip plays)
            if (animator == null) return;
            if (_hasDeath && animator.isActiveAndEnabled)
            {
                animator.speed = 1f; // the state's own speed sets the play time
                animator.CrossFadeInFixedTime(_deathHash, HitBlend, 0, 0f);
            }
            else animator.speed = 0f; // hold the pose while the body falls
        }

        private void OnDisable() => Active.Remove(this);

        private void Update()
        {
            float dt = Time.deltaTime;
            TickStatus(dt);
            if (_hitCooldown > 0f) _hitCooldown -= dt;
            if (_deadTime >= 0f)
            {
                UpdateDeath(dt);
                return;
            }

            Vector3 pos = transform.position;
            Vector3 move = Vector3.zero;
            float dist = float.PositiveInfinity;
            if (_target != null && !Halted)
            {
                Vector3 to = _target.position - pos;
                to.y = 0f;
                dist = to.magnitude;
                if (dist > 0.01f && _stunLeft <= 0f && _freezeLeft <= 0f) // KAN-134 / KAN-91: a stunned foe stands (crowd push and knockback still move it)
                {
                    Vector3 heading = to / dist;
                    if (holdMax > 0f) move = KeepDistance(pos, heading, dist, dt);
                    else
                    {
                        if (dist > stopDistance) heading = SlicedSteer(pos, heading, dt);
                        var look = Quaternion.LookRotation(heading);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
                        if (dist > stopDistance) move = heading * MoveSpeed;
                    }
                }
            }
            move += EnemyCrowd.Separation(this, pos, separation * _scale, MoveSpeed * 1.5f);
            _knock = Vector3.MoveTowards(_knock, Vector3.zero, KnockbackSpeed * 6f * dt);
            Vector3 next = pos + (move + _knock) * dt;
            next.z = Mathf.Min(next.z, LandMaxZ); // knockback and crowding never push them into the sea
            next = Slide(pos, next);
            transform.position = next;
            UpdateAnimation(pos, next, dist, dt);
        }

        /// <summary>
        /// Ranged movement (KAN-105). Holding: stand and face the Knight while it stays inside the band (capped to the view,
        /// <see cref="EffectiveBand"/>) and the shot is clear.
        /// Moving: walk in (farther than the band middle, or no clear shot) or back off (closer), steered round walls, until
        /// the distance settles near the band middle. Returns the planar velocity.
        /// </summary>
        private Vector3 KeepDistance(Vector3 pos, Vector3 toTarget, float dist, float dt)
        {
            Vector2 band = EffectiveBand;
            float mid = (band.x + band.y) * 0.5f;
            float settle = Mathf.Max(0.3f, (band.y - band.x) * SettleShare);
            if (_holding) _holding = !NeedsClearShot && dist >= band.x && dist <= band.y;
            else _holding = !NeedsClearShot && Mathf.Abs(dist - mid) <= settle;

            Vector3 face = toTarget;
            Vector3 move = Vector3.zero;
            if (!_holding)
            {
                bool walkIn = NeedsClearShot ? dist > stopDistance : dist > mid;
                if (walkIn || dist < mid)
                {
                    Vector3 heading = SlicedSteer(pos, walkIn ? toTarget : -toTarget, dt);
                    move = heading * (MoveSpeed * (walkIn ? 1f : BackOffSpeed));
                    face = heading;
                }
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), turnSpeed * dt);
            return move;
        }

        /// <summary>
        /// Walk rate from the real ground speed (steering, crowd push and knockback included), smoothed so a jostle
        /// does not flicker the legs. Within reach of the player: cross-fade to the attack state, back to the walk after.
        /// </summary>
        private void UpdateAnimation(Vector3 from, Vector3 to, float distToTarget, float dt)
        {
            if (animator == null || !_synced) return;
            if (dt <= 0f) return; // paused (timeScale 0): speed = 0 / 0 would poison _groundSpeed and freeze the walk for good
            if (IsFrozenSolid) // KAN-91: hold the pose; the walk / attack logic resumes where it was after the thaw
            {
                animator.speed = 0f;
                _groundSpeed = 0f;
                return;
            }
            float dx = to.x - from.x, dz = to.z - from.z;
            float speed = Mathf.Sqrt(dx * dx + dz * dz) / dt;
            _groundSpeed += (speed - _groundSpeed) * (1f - Mathf.Exp(-dt * 10f));
            if (!float.IsFinite(_groundSpeed)) _groundSpeed = MoveSpeed;

            _sinceSwitch += dt;
            if (_hitLeft > 0f)
            {
                _hitLeft -= dt;
                if (_hitLeft > 0f) return; // the flinch plays at its own speed
                _sinceSwitch = 0f;
                animator.CrossFadeInFixedTime(_walkHash, AnimBlend, 0, UnityEngine.Random.value * 0.2f);
            }
            bool attack = _attacking
                ? Halted || distToTarget <= contactRange + AttackRelease
                : _hasAttack && (Halted || distToTarget <= contactRange + AttackReach);
            if (attack != _attacking && _sinceSwitch >= MinStateHold)
            {
                _attacking = attack;
                _sinceSwitch = 0f;
                animator.CrossFadeInFixedTime(attack ? _attackHash : _walkHash, AnimBlend, 0, attack ? 0f : UnityEngine.Random.value * 0.2f);
            }
            float rate = _attacking ? _attackRate : WalkRate(_groundSpeed);
            animator.speed = float.IsFinite(rate) ? rate : 1f;

            _animCheck -= dt;
            if (_animCheck > 0f) return;
            _animCheck += AnimCheckInterval;
            ResyncAnimator();
        }

        /// <summary>
        /// Self-heal: <see cref="_attacking"/> is the truth. When the animator rests (no transition) in the other state, for
        /// example a cross-fade cut short or dropped, request the wanted state again. Skipped while a switch is still blending.
        /// </summary>
        private void ResyncAnimator()
        {
            if (_hitLeft > 0f || _sinceSwitch < AnimBlend + 0.05f || !animator.isActiveAndEnabled || animator.IsInTransition(0)) return;
            int want = _attacking ? _attackHash : _walkHash;
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == want) return;
            animator.CrossFadeInFixedTime(want, AnimBlend, 0, _attacking ? 0f : UnityEngine.Random.value * 0.2f);
            _sinceSwitch = 0f;
            AnimatorResyncs++;
        }

        /// <summary>
        /// Time-sliced <see cref="Steer"/>: the probe runs every <see cref="SteerInterval"/> s; in between, the last
        /// detour angle is applied to the fresh direct heading (so the chase still tracks a moving player every frame).
        /// </summary>
        private Vector3 SlicedSteer(Vector3 pos, Vector3 heading, float dt)
        {
            _steerTimer -= dt;
            if (_steerTimer <= 0f)
            {
                _steerTimer = Mathf.Max(_steerTimer + SteerInterval, 0f);
                Vector3 steered = Steer(pos, heading);
                _steerAngle = _detouring ? Vector3.SignedAngle(heading, steered, Vector3.up) : 0f;
                return steered;
            }
            return _steerAngle == 0f ? heading : Quaternion.AngleAxis(_steerAngle, Vector3.up) * heading;
        }

        /// <summary>
        /// Direct heading when the way ahead is free. Blocked: a fresh detour takes the smallest free turn on either side;
        /// while the detour lasts it keeps that side (else the target angle drifting makes it dither left-right at a wall).
        /// </summary>
        private Vector3 Steer(Vector3 pos, Vector3 heading)
        {
            var walk = Walkability;
            if (walk == null || !walk.IsBlocked(pos + heading * ProbeDistance, BodyRadius))
            {
                _detouring = false;
                return heading;
            }
            if (!_detouring)
            {
                for (int i = 1; i <= DetourTries; i++)
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? _detourSide : -_detourSide;
                    if (!TryTurn(walk, pos, heading, side * DetourStep * i, out var d)) continue;
                    _detourSide = side;
                    _detouring = true;
                    return d;
                }
                return heading;
            }
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? _detourSide : -_detourSide; // other side only when this one is closed off
                for (int i = 1; i <= DetourTries; i++)
                {
                    if (!TryTurn(walk, pos, heading, side * DetourStep * i, out var d)) continue;
                    _detourSide = side;
                    return d;
                }
            }
            return heading;
        }

        private static bool TryTurn(IWalkability walk, Vector3 pos, Vector3 heading, float degrees, out Vector3 dir)
        {
            dir = Quaternion.Euler(0f, degrees, 0f) * heading;
            return !walk.IsBlocked(pos + dir * ProbeDistance, BodyRadius);
        }

        /// <summary>Step into an obstacle: keep the free axis (wall slide). Already inside one (spawned or shoved in): let it out.</summary>
        private static Vector3 Slide(Vector3 from, Vector3 to)
        {
            var walk = Walkability;
            if (walk == null || !walk.IsBlocked(to, BodyRadius) || walk.IsBlocked(from, BodyRadius)) return to;
            var alongX = new Vector3(to.x, to.y, from.z);
            if (!walk.IsBlocked(alongX, BodyRadius)) return alongX;
            var alongZ = new Vector3(from.x, to.y, to.z);
            if (!walk.IsBlocked(alongZ, BodyRadius)) return alongZ;
            return from;
        }

        private void UpdateDeath(float dt)
        {
            _deadTime += dt;
            float fallTime = _hasDeath ? deathHold : FallTime;
            if (!_hasDeath)
            {
                float fall = Mathf.Clamp01(_deadTime / FallTime);
                transform.rotation = _deathStart * Quaternion.Euler(-85f * (1f - (1f - fall) * (1f - fall)), 0f, 0f); // backwards, ease out
            }
            transform.position += _knock * dt;
            _knock = Vector3.MoveTowards(_knock, Vector3.zero, KnockbackSpeed * 4f * dt);
            if (_deadTime > fallTime) transform.position += Vector3.down * (0.9f * Mathf.Max(1f, Height / 1.8f) / SinkTime * dt);
            if (_deadTime >= fallTime + SinkTime)
            {
                gameObject.SetActive(false);
                _release?.Invoke(this);
            }
        }

        /// <summary>Burn ticks, slow / stun timers and body tint (hit flash, ember glow). Public for tests (Update calls it).</summary>
        public void TickStatus(float dt)
        {
            UpdateBurn(dt);
            UpdateHex(dt);
            UpdateControl(dt);
            UpdateTint(dt);
        }

        /// <summary>Slow and stun timers (KAN-134).</summary>
        private void UpdateControl(float dt)
        {
            if (_slowLeft > 0f && (_slowLeft -= dt) <= 0f) { _slowLeft = 0f; _slow = 0f; }
            if (_stunLeft > 0f) _stunLeft -= dt;
            if (_stunCooldown > 0f) _stunCooldown -= dt;
            if (_freezeLeft > 0f && (_freezeLeft -= dt) <= 0f) _freezeLeft = 0f;
            if (_freezeCooldown > 0f) _freezeCooldown -= dt;
        }

        private void UpdateBurn(float dt)
        {
            if (_burnLeft <= 0f) return;
            if (!IsAlive) { _burnLeft = 0f; return; }
            _burnLeft -= dt;
            _burnTick -= dt;
            if (_burnTick > 0f) return;
            _burnTick += BurnTickInterval;
            TakeDamage(_burnDps * BurnTickInterval, Vector3.zero, DamageKind.Burn);
        }

        private void UpdateHex(float dt)
        {
            if (_hexLeft <= 0f) return;
            _hexLeft -= dt;
            if (_hexLeft > 0f && IsAlive) return;
            _hexLeft = 0f;
            _hexBoost = 1f;
            SpeedBoost = 1f;
        }

        /// <summary>White hit flash over an ember glow while burning (pulsing), a violet glow while hexed, else the base tint.</summary>
        private void UpdateTint(float dt)
        {
            Color c = _calmTint;
            if (_hexLeft > 0f)
            {
                float pulse = 0.6f + 0.2f * Mathf.Sin(Time.time * 7f + _burnPhase);
                c = Color.Lerp(_calmTint, HexTint, pulse * Mathf.Clamp01(_hexLeft / 0.3f));
            }
            if (_burnLeft > 0f)
            {
                float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 11f + _burnPhase);
                c = Color.Lerp(c, BurnTint, pulse * Mathf.Clamp01(_burnLeft / 0.3f));
            }
            if (_freezeLeft > 0f) // KAN-91: ice over fire and hex; fades in fast, thaws over the last 0.3 s
            {
                float k = Mathf.Clamp01(_freezeLeft / 0.3f) * Mathf.Clamp01((_freezeTotal - _freezeLeft) / 0.08f + 0.35f);
                c = Color.Lerp(c, IceTint, 0.9f * k);
            }
            if (_flash > 0f)
            {
                _flash -= dt;
                c = Color.Lerp(c, new Color(2.2f, 1.6f, 1.5f), Mathf.Clamp01(_flash / FlashTime));
            }
            if (c != _tint) SetTint(c);
        }

        /// <summary>
        /// Danger tier colour step of this spawn (KAN-92): the body gets the shared tier materials of
        /// <paramref name="step"/> (<see cref="HordeTierMaterials"/>), so a calm tinted zombie still has no property block.
        /// Step 0 or no <paramref name="scaling"/> = the type's own materials. Call after <see cref="Spawn"/>; the next spawn resets it.
        /// </summary>
        public void SetDangerStep(HordeScaling scaling, int step)
        {
            if (body == null) return;
            CacheBodyMaterials();
            step = scaling != null ? Mathf.Clamp(step, 0, scaling.StepCount - 1) : 0;
            var mats = HordeTierMaterials.Get(_sourceMaterials, scaling, step);
            if (mats != null && (step != 0 || _tierStep != 0)) body.sharedMaterials = mats; // calm -> calm: nothing to swap
            _tierStep = mats == _sourceMaterials ? 0 : step;
            _calmTint = HordeTierMaterials.CalmTint(baseTint, scaling, _tierStep);
            SetTint(_calmTint);
        }

        /// <summary>Remembers the body's own materials (tier step 0). The spawner calls it when it makes a pooled enemy.</summary>
        public void CacheBodyMaterials()
        {
            if (_sourceMaterials == null && body != null) _sourceMaterials = body.sharedMaterials;
        }

        /// <summary>Danger tier colour step of this spawn (0 = calm, the type's own colour).</summary>
        public int DangerStep => _tierStep;
        /// <summary>Body colour when not flashing or burning: the type tint x the danger tier colour.</summary>
        public Color CalmTint => _calmTint;

        /// <summary>
        /// Base tint = no property block (the material already has that colour), so a calm horde stays SRP-Batcher
        /// friendly; only flashing or burning bodies carry a block.
        /// </summary>
        private void SetTint(Color c)
        {
            _tint = c;
            if (body == null) return;
            if (c == _calmTint)
            {
                body.SetPropertyBlock(null);
                return;
            }
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, c);
            body.SetPropertyBlock(_block);
        }
    }
}
