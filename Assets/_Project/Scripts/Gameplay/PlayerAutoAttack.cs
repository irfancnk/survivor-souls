using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Archero rule: while the hero stands still, face the nearest zombie in range and throw at it.
    /// The cooldown keeps running while moving, so a quick stop fires at once.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerAutoAttack : MonoBehaviour
    {
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(0f)] private float range = 7f;
        [SerializeField, Min(0.05f)] private float interval = 0.6f;
        [SerializeField, Min(1)] private int damage = 25;

        private PlayerController _player;
        private float _cooldown;

        public void Configure(Projectile prefab, Transform muzzlePoint)
        {
            projectilePrefab = prefab;
            muzzle = muzzlePoint;
        }

        private void Awake() => _player = GetComponent<PlayerController>();

        private void Update()
        {
            _cooldown -= Time.deltaTime;
            if (_player.IsMoving || _player.Health.IsDead) return;

            var target = Nearest();
            if (target == null) return;
            _player.FaceTowards(target.transform.position);

            if (_cooldown > 0f) return;
            _cooldown = interval;
            Fire(target);
        }

        private ZombieEnemy Nearest()
        {
            ZombieEnemy best = null;
            float bestSq = range * range;
            Vector3 p = transform.position;
            var zombies = ZombieEnemy.All;
            for (int i = 0; i < zombies.Count; i++)
            {
                Vector3 d = zombies[i].transform.position - p;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = zombies[i];
                }
            }
            return best;
        }

        private void Fire(ZombieEnemy target)
        {
            if (projectilePrefab == null) return;
            Vector3 from = muzzle != null ? muzzle.position : transform.position + Vector3.up;
            Vector3 aim = target.transform.position - from;
            aim.y = 0f; // fly level at chest height
            if (aim.sqrMagnitude < 0.0001f) aim = transform.forward;
            var shot = Instantiate(projectilePrefab, from, Quaternion.LookRotation(aim.normalized, Vector3.up));
            shot.Launch(damage);
        }
    }
}
