using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Archero rule: while the player stands still, turn to the nearest enemy in range and fire one bolt
    /// every <see cref="fireInterval"/> seconds. Moving = no shooting (unless <see cref="fireWhileMoving"/>).
    /// Default numbers: enemy 150 HP, bolt 100 damage, so one bolt leaves it at a third and the second kills.
    /// The serialized numbers are base values: damage, fire rate, crit and the extra-bolt counts go through the player's
    /// <see cref="PlayerStats"/> (KAN-19 skills) when it has one. Multishot fans extra bolts around the aim line.
    /// Every bolt hit is reported by <see cref="EnemyHit"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerMover))]
    public sealed class AutoShooter : MonoBehaviour
    {
        [SerializeField] private Bolt boltPrefab;
        [Tooltip("Bolt spawn point (Crossbow_Muzzle). Falls back to chest height when empty.")]
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(0.05f)] private float fireInterval = 1f;
        [SerializeField, Min(0f)] private float damage = 100f;
        [SerializeField, Min(1f)] private float range = 10f;
        [SerializeField, Min(1f)] private float boltSpeed = 26f;
        [SerializeField] private bool fireWhileMoving;
        [SerializeField, Min(0f)] private float aimTurnSpeed = 720f; // degrees per second
        [SerializeField, Range(1f, 90f)] private float aimTolerance = 15f;
        [Header("Skill bases (PlayerStats modifies these)")]
        [SerializeField, Range(0f, 1f)] private float critChance;
        [SerializeField, Min(1f)] private float critMultiplier = 2f;
        [Tooltip("Degrees between two neighbouring bolts of a multishot fan.")]
        [SerializeField, Range(0f, 45f)] private float multishotSpread = 11f;
        [Tooltip("How far (m) a ricochet bolt looks for its next enemy.")]
        [SerializeField, Min(0.5f)] private float ricochetRange = 6.5f;

        private const float MovingSpeed = 0.2f;

        /// <summary>Raised on every shot: muzzle position, flight direction. Recoil, muzzle FX and audio listen.</summary>
        public event Action<Vector3, Vector3> Fired;
        /// <summary>Raised for every enemy any of this shooter's bolts hits (on-hit skills listen).</summary>
        public event Action<BoltHit> EnemyHit;

        private readonly Stack<Bolt> _pool = new Stack<Bolt>();
        private PlayerMover _mover;
        private PlayerStats _stats;
        private Action<Bolt> _release;
        private Action<BoltHit> _onHit;
        private float _cooldown;

        /// <summary>Bolt damage after skills (burst and similar skills scale from it).</summary>
        public float CurrentDamage => PlayerStats.Eval(_stats, StatId.Damage, damage);
        /// <summary>Seconds between shots after attack speed skills.</summary>
        public float CurrentInterval => 1f / Mathf.Max(0.05f, PlayerStats.Eval(_stats, StatId.AttackSpeed, 1f / fireInterval));

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void SetBoltPrefab(Bolt prefab) => boltPrefab = prefab;

        private void Awake()
        {
            _mover = GetComponent<PlayerMover>();
            _stats = GetComponent<PlayerStats>();
            _release = b => _pool.Push(b);
            _onHit = hit => EnemyHit?.Invoke(hit);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _cooldown -= dt;

            bool moving = _mover.Velocity.sqrMagnitude > MovingSpeed * MovingSpeed;
            if (moving && !fireWhileMoving) return;
            var target = Nearest();
            if (target == null) return;

            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            var look = Quaternion.LookRotation(to);
            if (!moving) transform.rotation = Quaternion.RotateTowards(transform.rotation, look, aimTurnSpeed * dt);

            if (_cooldown > 0f) return;
            if (!moving && Quaternion.Angle(transform.rotation, look) > aimTolerance) return;
            Fire(target);
            _cooldown = CurrentInterval;
        }

        private Enemy Nearest()
        {
            Enemy best = null;
            float bestSq = range * range;
            Vector3 p = transform.position;
            foreach (var e in Enemy.Active)
            {
                Vector3 d = e.transform.position - p;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        /// <summary>Shoots one volley at <paramref name="target"/> now (no cooldown check). Public for tests and debug.</summary>
        public void Fire(Enemy target)
        {
            if (boltPrefab == null || target == null) return;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.1f;
            Vector3 dir = (target.AimPoint - from).normalized;
            var shot = new BoltShot
            {
                Damage = CurrentDamage,
                Speed = boltSpeed,
                Lifetime = range * 1.5f / boltSpeed,
                CritChance = Mathf.Clamp01(PlayerStats.Eval(_stats, StatId.CritChance, critChance)),
                CritMultiplier = Mathf.Max(1f, PlayerStats.Eval(_stats, StatId.CritDamage, critMultiplier)),
                Pierce = _stats != null ? _stats.ModifyInt(StatId.Pierce, 0) : 0,
                Ricochet = _stats != null ? _stats.ModifyInt(StatId.Ricochet, 0) : 0,
                RicochetRange = ricochetRange,
            };
            int count = 1 + (_stats != null ? _stats.ModifyInt(StatId.Multishot, 0) : 0);
            for (int i = 0; i < count; i++)
            {
                float yaw = (i - (count - 1) * 0.5f) * multishotSpread; // symmetric fan around the aim line
                Vector3 d = yaw == 0f ? dir : Quaternion.AngleAxis(yaw, Vector3.up) * dir;
                var bolt = _pool.Count > 0 ? _pool.Pop() : Instantiate(boltPrefab);
                bolt.Launch(from, d, shot, _release, _onHit);
            }
            Fired?.Invoke(from, dir);
        }
    }
}
