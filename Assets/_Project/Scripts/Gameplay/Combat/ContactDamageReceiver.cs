using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Enemy contact damage on the player. Each frame, every living enemy whose planar distance to the player is
    /// within its <see cref="Enemy.ContactRange"/> adds its <see cref="Enemy.DamagePerSecond"/>; the sum times
    /// deltaTime is applied once. So three enemies in range drain three times as fast as one.
    /// Squared-distance checks over <see cref="Enemy.Active"/>: no physics, no allocations.
    /// </summary>
    [DefaultExecutionOrder(50)] // after enemies moved this frame
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class ContactDamageReceiver : MonoBehaviour
    {
        private PlayerHealth _health;

        /// <summary>Enemies in contact range on the last frame (debug / HUD).</summary>
        public int EnemiesInRange { get; private set; }
        /// <summary>Summed damage per second on the last frame.</summary>
        public float IncomingDps { get; private set; }

        private void Awake() => _health = GetComponent<PlayerHealth>();

        private void OnDisable()
        {
            EnemiesInRange = 0;
            IncomingDps = 0f;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One damage step over <paramref name="deltaTime"/> seconds. Public for tests.</summary>
        public void Tick(float deltaTime)
        {
            if (_health.IsDead) return;
            Vector3 p = transform.position;
            float dps = 0f;
            int count = 0;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                Vector3 d = e.transform.position - p;
                float sq = d.x * d.x + d.z * d.z;
                float r = e.ContactRange;
                if (sq > r * r) continue;
                dps += e.DamagePerSecond;
                count++;
            }
            EnemiesInRange = count;
            IncomingDps = dps;
            if (dps > 0f) _health.TakeDamage(dps * deltaTime);
        }
    }
}
