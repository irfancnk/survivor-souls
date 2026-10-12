using System.Collections.Generic;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Archero rule: while the player stands still, turn to the nearest enemy in range and fire one bolt
    /// every <see cref="fireInterval"/> seconds. Moving = no shooting (unless <see cref="fireWhileMoving"/>).
    /// Default numbers: enemy 150 HP, bolt 100 damage, so one bolt leaves it at a third and the second kills.
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

        private const float MovingSpeed = 0.2f;

        private readonly Stack<Bolt> _pool = new Stack<Bolt>();
        private PlayerMover _mover;
        private float _cooldown;

        private void Awake() => _mover = GetComponent<PlayerMover>();

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
            _cooldown = fireInterval;
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

        private void Fire(Enemy target)
        {
            if (boltPrefab == null) return;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.1f;
            Vector3 dir = (target.AimPoint - from).normalized;
            var bolt = _pool.Count > 0 ? _pool.Pop() : Instantiate(boltPrefab);
            bolt.Launch(from, dir, boltSpeed, damage, range * 1.5f / boltSpeed, b => _pool.Push(b));
        }
    }
}
