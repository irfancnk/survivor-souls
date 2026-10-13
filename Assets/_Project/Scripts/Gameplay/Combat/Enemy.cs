using System;
using System.Collections.Generic;
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
        [SerializeField] private Color baseTint = Color.white;
        [SerializeField] private Animator animator;

        private const float KnockbackSpeed = 4f;
        private const float FlashTime = 0.12f;
        private const float FallTime = 0.35f;
        private const float SinkTime = 0.7f;
        private const float BurnTickInterval = 0.5f;
        private static readonly Color BurnTint = new Color(1.9f, 0.85f, 0.35f);

        private Transform _target;
        private Action<Enemy> _release;
        private MaterialPropertyBlock _block;
        private float _health;
        private float _flash;
        private float _deadTime = -1f;
        private Vector3 _knock;
        private Quaternion _deathStart;
        private float _burnDps;
        private float _burnLeft;
        private float _burnTick;
        private float _burnPhase; // per enemy, so a burning crowd does not pulse in sync
        private Color _tint = Color.clear;

        /// <summary>Northmost Z an enemy may walk to (the sea edge). Set by the spawner from the world; +inf = no limit.</summary>
        public static float LandMaxZ = float.PositiveInfinity;

        public float Health => _health;
        public float MaxHealth => maxHealth;
        public bool IsAlive => _health > 0f;
        public float HitRadius => hitRadius;
        public float Height => height;
        public float ContactRange => contactRange;
        public float DamagePerSecond => damagePerSecond;
        public int XpValue => xpValue;
        public bool IsBurning => _burnLeft > 0f && IsAlive;
        public float BurnDps => IsBurning ? _burnDps : 0f;
        public DamageKind LastDamageKind { get; private set; }
        public bool LastHitCrit { get; private set; }
        /// <summary>Chest height: where bolts aim.</summary>
        public Vector3 AimPoint => transform.position + Vector3.up * (height * 0.6f);
        /// <summary>Above the head: where the health bar sits.</summary>
        public Vector3 BarAnchor => transform.position + Vector3.up * (height + 0.3f);

        public void Spawn(Vector3 position, Transform target, Action<Enemy> release)
        {
            _target = target;
            _release = release;
            _health = maxHealth;
            _deadTime = -1f;
            _knock = Vector3.zero;
            _flash = 0f;
            _burnDps = 0f;
            _burnLeft = 0f;
            _burnPhase = UnityEngine.Random.value * Mathf.PI * 2f;
            Vector3 toTarget = target != null ? target.position - position : Vector3.forward;
            toTarget.y = 0f;
            transform.SetPositionAndRotation(position, toTarget.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toTarget) : Quaternion.identity);
            gameObject.SetActive(true);
            SetTint(baseTint);
            if (animator != null)
            {
                // Random phase and a small speed spread, so a crowd does not run in lockstep.
                animator.speed = UnityEngine.Random.Range(0.9f, 1.1f);
                animator.Play(0, 0, UnityEngine.Random.value);
            }
            Active.Add(this);
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
                _knock = direction.normalized * KnockbackSpeed;
                _flash = FlashTime;
            }
            bool killed = _health <= 0f;
            if (killed) BeginDeath();
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

        private void BeginDeath()
        {
            Active.Remove(this);
            _deadTime = 0f;
            _deathStart = transform.rotation;
            if (animator != null) animator.speed = 0f; // hold the pose while the body falls
        }

        private void OnDisable() => Active.Remove(this);

        private void Update()
        {
            float dt = Time.deltaTime;
            TickStatus(dt);
            if (_deadTime >= 0f)
            {
                UpdateDeath(dt);
                return;
            }

            Vector3 pos = transform.position;
            Vector3 move = Vector3.zero;
            if (_target != null)
            {
                Vector3 to = _target.position - pos;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > 0.01f)
                {
                    var look = Quaternion.LookRotation(to / dist);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
                    if (dist > stopDistance) move = to / dist * moveSpeed;
                }
            }
            move += Separation(pos);
            _knock = Vector3.MoveTowards(_knock, Vector3.zero, KnockbackSpeed * 6f * dt);
            pos += (move + _knock) * dt;
            pos.z = Mathf.Min(pos.z, LandMaxZ); // knockback and crowding never push them into the sea
            transform.position = pos;
        }

        /// <summary>Pushes away from close neighbours so a crowd does not stack into one body.</summary>
        private Vector3 Separation(Vector3 pos)
        {
            Vector3 push = Vector3.zero;
            foreach (var other in Active)
            {
                if (other == this) continue;
                Vector3 d = pos - other.transform.position;
                d.y = 0f;
                float m = d.magnitude;
                if (m < separation && m > 0.001f) push += d / m * ((separation - m) / separation * moveSpeed * 1.5f);
            }
            return push;
        }

        private void UpdateDeath(float dt)
        {
            _deadTime += dt;
            float fall = Mathf.Clamp01(_deadTime / FallTime);
            transform.rotation = _deathStart * Quaternion.Euler(-85f * (1f - (1f - fall) * (1f - fall)), 0f, 0f); // backwards, ease out
            transform.position += _knock * dt;
            _knock = Vector3.MoveTowards(_knock, Vector3.zero, KnockbackSpeed * 4f * dt);
            if (_deadTime > FallTime) transform.position += Vector3.down * (0.9f / SinkTime * dt);
            if (_deadTime >= FallTime + SinkTime)
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
            Color c = baseTint;
            if (_burnLeft > 0f)
            {
                float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 11f + _burnPhase);
                c = Color.Lerp(baseTint, BurnTint, pulse * Mathf.Clamp01(_burnLeft / 0.3f));
            }
            if (_flash > 0f)
            {
                _flash -= dt;
                c = Color.Lerp(c, new Color(2.2f, 1.6f, 1.5f), Mathf.Clamp01(_flash / FlashTime));
            }
            if (c != _tint) SetTint(c);
        }

        private void SetTint(Color c)
        {
            _tint = c;
            if (body == null) return;
            _block ??= new MaterialPropertyBlock();
            body.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            body.SetPropertyBlock(_block);
        }
    }
}
