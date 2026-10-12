using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Raises zombies around the player in a ring just inside the arena. Pressure ramps over
    /// <see cref="rampSeconds"/>: shorter interval, bigger crowd cap.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private ZombieEnemy zombiePrefab;
        [SerializeField] private PlayerController player;
        [SerializeField, Min(1f)] private float arenaRadius = 14f;
        [SerializeField] private Vector2 spawnDistance = new Vector2(6.5f, 10f);
        [SerializeField, Min(0f)] private float firstDelay = 1f;
        [SerializeField] private Vector2 intervalStartEnd = new Vector2(1.5f, 0.55f);
        [SerializeField] private Vector2Int maxAliveStartEnd = new Vector2Int(5, 18);
        [SerializeField, Min(1f)] private float rampSeconds = 90f;

        private float _elapsed;
        private float _timer;

        public void Configure(ZombieEnemy prefab, PlayerController target, float radius)
        {
            zombiePrefab = prefab;
            player = target;
            arenaRadius = radius;
        }

        private void Start() => _timer = firstDelay;

        private void Update()
        {
            if (zombiePrefab == null || player == null || player.Health.IsDead) return;

            _elapsed += Time.deltaTime;
            float ramp = Mathf.Clamp01(_elapsed / rampSeconds);
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = Mathf.Lerp(intervalStartEnd.x, intervalStartEnd.y, ramp);

            int cap = Mathf.RoundToInt(Mathf.Lerp(maxAliveStartEnd.x, maxAliveStartEnd.y, ramp));
            if (ZombieEnemy.All.Count >= cap) return;

            Spawn();
        }

        private void Spawn()
        {
            Vector3 center = player.transform.position;
            Vector3 pos = center;
            // A few tries for a point inside the arena; the clamp below catches the rest.
            for (int i = 0; i < 6; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(spawnDistance.x, spawnDistance.y);
                pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                if (pos.magnitude <= arenaRadius - 0.5f) break;
            }
            pos = Vector3.ClampMagnitude(new Vector3(pos.x, 0f, pos.z), arenaRadius - 0.5f);
            pos.y = -1.8f; // starts buried; ZombieEnemy raises it

            Vector3 look = center - pos;
            look.y = 0f;
            var zombie = Instantiate(zombiePrefab, pos, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward), transform);
            zombie.Init(player);
        }
    }
}
