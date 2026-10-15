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
        private int _hitHash;
        private int _deathHash;
        private bool _hasHit;
        private bool _hasDeath;
        private float _hitLeft;      // seconds left in the current Hit flinch
        private float _hitCooldown;  // seconds until the next flinch may start
        private bool _holding;       // ranged: standing inside the hold band

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
        /// <summary>This spawn's move speed (type speed x random spread x <see cref="SpeedBoost"/>).</summary>
        public float MoveSpeed => moveSpeed * _speedMul * SpeedBoost;
        /// <summary>Extra walk speed factor set by an attack pattern (KAN-61 boss phase 2). Reset to 1 on spawn.</summary>
        public float SpeedBoost { get; set; } = 1f;
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
        public float BurnDps => IsBurning ? _burnDps : 0f;
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
            _burnPhase = UnityEngine.Random.value * Mathf.PI * 2f;
            _detourSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            _detouring = false;
            _steerAngle = 0f;
            _steerTimer = UnityEngine.Random.value * SteerInterval; // staggered: probes spread over the frames
            _speedMul = 1f + UnityEngine.Random.Range(-speedJitter, speedJitter);
            SpeedBoost = 1f;
            _scale = bodyScale * (1f + UnityEngine.Random.Range(-scaleJitter, scaleJitter));
            _groundSpeed = MoveSpeed;
            _attacking = false;
            _hitLeft = 0f;
            _hitCooldown = 0f;
            Halted = false;
            NeedsClearShot = false;
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
            _attackHash = Animator.StringToHash(attackState);
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
        /// A new burn refreshes the time and keeps the stronger damage; it does not stack.
        /// </summary>
        public void ApplyBurn(float dps, float duration)
        {
            if (!IsAlive || dps <= 0f || duration <= 0f) return;
            if (_burnLeft <= 0f) _burnTick = BurnTickInterval; // first tick after one interval
            _burnDps = _burnLeft > 0f ? Mathf.Max(_burnDps, dps) : dps;
            _burnLeft = Mathf.Max(_burnLeft, duration);
        }

        /// <summary>Short Hit state on a direct hit, unless attacking, already flinching or flinched within <see cref="HitCooldown"/>.</summary>
        private void Flinch()
        {
            if (!_hasHit || _attacking || _hitLeft > 0f || _hitCooldown > 0f || !animator.isActiveAndEnabled) return;
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
                if (dist > 0.01f)
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
        /// Ranged movement (KAN-105). Holding: stand and face the Knight while it stays inside the band and the shot is clear.
        /// Moving: walk in (farther than the band middle, or no clear shot) or back off (closer), steered round walls, until
        /// the distance settles near the band middle. Returns the planar velocity.
        /// </summary>
        private Vector3 KeepDistance(Vector3 pos, Vector3 toTarget, float dist, float dt)
        {
            float mid = (holdMin + holdMax) * 0.5f;
            float settle = Mathf.Max(0.3f, (holdMax - holdMin) * SettleShare);
            if (_holding) _holding = !NeedsClearShot && dist >= holdMin && dist <= holdMax;
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

        /// <summary>Burn ticks and body tint (hit flash, ember glow). Public for tests (Update calls it).</summary>
        public void TickStatus(float dt)
        {
            UpdateBurn(dt);
            UpdateTint(dt);
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

        /// <summary>White hit flash over an ember glow while burning (pulsing), else the base tint.</summary>
        private void UpdateTint(float dt)
        {
            Color c = _calmTint;
            if (_burnLeft > 0f)
            {
                float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 11f + _burnPhase);
                c = Color.Lerp(_calmTint, BurnTint, pulse * Mathf.Clamp01(_burnLeft / 0.3f));
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
