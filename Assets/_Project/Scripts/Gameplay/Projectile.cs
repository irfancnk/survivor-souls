using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Thrown ember sword. Flies straight, hits the first zombie it overlaps (distance check on the
    /// floor plane; no colliders needed), then disappears.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float speed = 16f;
        [SerializeField, Min(0.1f)] private float lifetime = 1.2f;
        [SerializeField, Min(0f)] private float radius = 0.25f;
        [SerializeField] private Transform spinner;
        [SerializeField] private float spinDegreesPerSecond = 1080f;

        private int _damage;
        private float _age;

        public void Launch(int damage) => _damage = damage;

        private void Update()
        {
            float step = speed * Time.deltaTime;
            transform.position += transform.forward * step;
            if (spinner != null) spinner.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);

            Vector3 p = transform.position;
            var zombies = ZombieEnemy.All;
            for (int i = 0; i < zombies.Count; i++)
            {
                var z = zombies[i];
                Vector3 d = z.transform.position - p;
                d.y = 0f;
                float r = z.HitRadius + radius;
                if (d.sqrMagnitude <= r * r)
                {
                    z.TakeHit(_damage, transform.forward);
                    Destroy(gameObject);
                    return;
                }
            }

            _age += Time.deltaTime;
            if (_age >= lifetime) Destroy(gameObject);
        }
    }
}
