using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Hollow zombie: rises from the ground, runs straight at the player, claws on contact.
    /// Steering is direct (flat arena, no obstacles) plus a light push away from other zombies
    /// so a crowd spreads into a ring instead of one stack. The run clip is in place (no root motion).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class ZombieEnemy : MonoBehaviour
    {
        private static readonly List<ZombieEnemy> Alive = new List<ZombieEnemy>();

        /// <summary>Zombies that can still be hit. Read-only view; do not cache across frames.</summary>
        public static IReadOnlyList<ZombieEnemy> All => Alive;
        public static event Action<ZombieEnemy> AnyDied;

        [SerializeField, Min(0f)] private float moveSpeed = 3.6f;
        [SerializeField, Min(0f)] private float turnSpeed = 540f;
        [SerializeField, Min(0.1f)] private float attackRange = 1.1f;
        [SerializeField, Min(0)] private int attackDamage = 10;
        [SerializeField, Min(0.1f)] private float attackCooldown = 1f;
        [SerializeField, Min(0f)] private float separationRadius = 1f;
        [Tooltip("Body radius for projectile hits.")]
        [SerializeField, Min(0.1f)] private float hitRadius = 0.55f;
        [SerializeField, Min(0f)] private float riseSeconds = 0.7f;
        [SerializeField, Min(0f)] private float sinkSeconds = 0.9f;
        [SerializeField] private Animator animator;

        private Health _health;
        private PlayerController _target;
        private float _attackTimer;
        private float _riseTimer;
        private Vector3 _knockback;

        public float HitRadius => hitRadius;
        public bool IsDead => _health.IsDead;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.Died += OnDied;
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable() => Alive.Add(this);
        private void OnDisable() => Alive.Remove(this);

        public void Init(PlayerController target)
        {
            _target = target;
            _riseTimer = 0f;
            _attackTimer = attackCooldown * 0.5f;
            if (animator != null)
            {
                // Desync a crowd: random phase and slight speed spread.
                animator.Play(0, 0, UnityEngine.Random.value);
                animator.speed = UnityEngine.Random.Range(0.9f, 1.1f);
            }
        }

        public void TakeHit(int damage, Vector3 fromDirection)
        {
            if (IsDead) return;
            fromDirection.y = 0f;
            _knockback += fromDirection.normalized * 3f;
            _health.Damage(damage);
        }

        private void Update()
        {
            if (IsDead || _target == null) return;

            if (_riseTimer < riseSeconds)
            {
                _riseTimer += Time.deltaTime;
                Vector3 p = transform.position;
                p.y = Mathf.Lerp(-1.8f, 0f, Mathf.SmoothStep(0f, 1f, _riseTimer / riseSeconds));
                transform.position = p;
                return;
            }

            Vector3 to = _target.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.001f ? to / dist : transform.forward;
            bool targetAlive = !_target.Health.IsDead;

            Vector3 velocity = Vector3.zero;
            if (targetAlive && dist > attackRange * 0.8f) velocity = dir * moveSpeed;
            velocity += Separation() * (moveSpeed * 0.6f);
            velocity += _knockback;
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 12f * Time.deltaTime);

            Vector3 pos = transform.position + velocity * Time.deltaTime;
            pos.y = 0f;
            transform.position = pos;

            if (targetAlive)
            {
                var look = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
            }

            _attackTimer -= Time.deltaTime;
            if (targetAlive && dist <= attackRange && _attackTimer <= 0f)
            {
                _attackTimer = attackCooldown;
                _target.Health.Damage(attackDamage);
            }
        }

        private Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            Vector3 self = transform.position;
            for (int i = 0; i < Alive.Count; i++)
            {
                var other = Alive[i];
                if (other == this) continue;
                Vector3 away = self - other.transform.position;
                away.y = 0f;
                float d = away.magnitude;
                if (d < separationRadius && d > 0.0001f) push += away / d * (1f - d / separationRadius);
            }
            return push;
        }

        private void OnDied()
        {
            Alive.Remove(this);
            AnyDied?.Invoke(this);
            StartCoroutine(SinkAndDestroy());
        }

        private IEnumerator SinkAndDestroy()
        {
            if (animator != null) animator.speed = 0.25f;
            Vector3 start = transform.position;
            for (float t = 0f; t < sinkSeconds; t += Time.deltaTime)
            {
                transform.position = start + Vector3.down * (1.9f * (t / sinkSeconds) * (t / sinkSeconds));
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
