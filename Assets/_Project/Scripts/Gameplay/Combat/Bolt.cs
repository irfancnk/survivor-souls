using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Crossbow bolt: flies straight, hits the first living <see cref="Enemy"/> its path crosses
    /// (vertical cylinder test, no physics), then returns to its pool. Pooled by <see cref="AutoShooter"/>.
    /// </summary>
    public sealed class Bolt : MonoBehaviour
    {
        [SerializeField] private TrailRenderer trail;

        private Vector3 _velocity;
        private float _damage;
        private float _life;
        private Action<Bolt> _release;

        public void Launch(Vector3 position, Vector3 direction, float speed, float damage, float lifetime, Action<Bolt> release)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            _velocity = direction * speed;
            _damage = damage;
            _life = lifetime;
            _release = release;
            gameObject.SetActive(true);
            if (trail != null) trail.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 to = from + _velocity * dt;

            foreach (var enemy in Enemy.Active)
            {
                if (!Crosses(enemy, from, to)) continue;
                enemy.TakeDamage(_damage, _velocity);
                Finish();
                return;
            }

            transform.position = to;
            _life -= dt;
            if (_life <= 0f) Finish();
        }

        /// <summary>Does the segment pass within the enemy's hit radius (XZ) at body height?</summary>
        private static bool Crosses(Enemy enemy, Vector3 from, Vector3 to)
        {
            Vector3 c = enemy.transform.position;
            Vector2 a = new Vector2(from.x - c.x, from.z - c.z);
            Vector2 ab = new Vector2(to.x - from.x, to.z - from.z);
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(-Vector2.Dot(a, ab) / len2) : 0f;
            if ((a + ab * t).sqrMagnitude > enemy.HitRadius * enemy.HitRadius) return false;
            float y = Mathf.Lerp(from.y, to.y, t) - c.y;
            return y >= 0f && y <= enemy.Height;
        }

        private void Finish()
        {
            gameObject.SetActive(false);
            _release?.Invoke(this);
        }
    }
}
