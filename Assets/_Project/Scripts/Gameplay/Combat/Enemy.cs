using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Melee chaser: walks to the player and stops at arm's length. Has health; <see cref="TakeDamage"/>
    /// knocks it back and flashes the body. At 0 health it falls, sinks into the ground and returns to its pool.
    /// Optional <see cref="animator"/> plays an in-place locomotion clip; it freezes when the enemy dies.
    /// Living enemies are listed in <see cref="Active"/> (targeting, health bars). Pooled by <see cref="EnemySpawner"/>.
    /// </summary>
    public sealed class Enemy : MonoBehaviour
    {
        /// <summary>Alive enemies (not dying). Read-only for other systems.</summary>
        public static readonly List<Enemy> Active = new List<Enemy>();

        /// <summary>Raised on every hit: enemy, health deducted, killing blow.</summary>
        public static event Action<Enemy, float, bool> Damaged;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Min(1f)] private float maxHealth = 150f;
        [SerializeField, Min(0f)] private float moveSpeed = 2.2f;
        [SerializeField, Min(0f)] private float turnSpeed = 540f; // degrees per second
        [SerializeField, Min(0f)] private float stopDistance = 1.1f;
        [SerializeField, Min(0.05f)] private float hitRadius = 0.45f;
        [SerializeField, Min(0.5f)] private float height = 1.8f;
        [SerializeField, Min(0f)] private float separation = 0.9f;
        [SerializeField] private Renderer body;
        [SerializeField] private Color baseTint = Color.white;
        [SerializeField] private Animator animator;

        private const float KnockbackSpeed = 4f;
        private const float FlashTime = 0.12f;
        private const float FallTime = 0.35f;
        private const float SinkTime = 0.7f;

        private Transform _target;
        private Action<Enemy> _release;
        private MaterialPropertyBlock _block;
        private float _health;
        private float _flash;
        private float _deadTime = -1f;
        private Vector3 _knock;
        private Quaternion _deathStart;

        /// <summary>Northmost Z an enemy may walk to (the sea edge). Set by the spawner from the world; +inf = no limit.</summary>
        public static float LandMaxZ = float.PositiveInfinity;

        public float Health => _health;
        public float MaxHealth => maxHealth;
        public bool IsAlive => _health > 0f;
        public float HitRadius => hitRadius;
        public float Height => height;
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

        /// <summary>Applies damage. Returns the health actually deducted.</summary>
        public float TakeDamage(float amount, Vector3 direction)
        {
            if (!IsAlive || amount <= 0f) return 0f;
            float deducted = Mathf.Min(amount, _health);
            _health -= deducted;
            direction.y = 0f;
            _knock = direction.normalized * KnockbackSpeed;
            _flash = FlashTime;
            bool killed = _health <= 0f;
            if (killed) BeginDeath();
            Damaged?.Invoke(this, deducted, killed);
            return deducted;
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
            UpdateFlash(dt);
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

        private void UpdateFlash(float dt)
        {
            if (_flash <= 0f) return;
            _flash -= dt;
            float k = Mathf.Clamp01(_flash / FlashTime);
            SetTint(Color.Lerp(baseTint, new Color(2.2f, 1.6f, 1.5f), k));
        }

        private void SetTint(Color c)
        {
            if (body == null) return;
            _block ??= new MaterialPropertyBlock();
            body.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            body.SetPropertyBlock(_block);
        }
    }
}
