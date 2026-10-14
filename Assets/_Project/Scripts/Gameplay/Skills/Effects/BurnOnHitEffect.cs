using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Ember Bolts": every bolt hit sets the enemy on fire (<see cref="Enemy.ApplyBurn"/>) for <see cref="Duration"/>
    /// seconds (<see cref="duration"/> + <see cref="StatId.BurnDuration"/>) at <see cref="StatId.BurnDps"/> damage per
    /// second. Embers rise from burning enemies through ONE shared world-space particle system (<see cref="embers"/>,
    /// Emit only): one draw call for any number of fires.
    /// </summary>
    public sealed class BurnOnHitEffect : SkillEffect
    {
        [SerializeField, Min(0.1f)] private float duration = 3f;
        [SerializeField] private ParticleSystem embers;
        [Tooltip("Embers per second per burning enemy.")]
        [SerializeField, Min(0f)] private float emberRate = 14f;

        private float _emitDebt;

        /// <summary>Burn length in seconds after skill levels (KAN-71: higher Ember Bolts levels burn longer).</summary>
        public float Duration => Mathf.Max(0.1f, PlayerStats.Eval(Context?.Stats, StatId.BurnDuration, duration));
        private System.Action<BoltHit> _onHit;

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
        }

        private void OnHit(BoltHit hit)
        {
            float dps = PlayerStats.Eval(Context.Stats, StatId.BurnDps, 0f);
            if (dps > 0f && !hit.Killed) hit.Enemy.ApplyBurn(dps, Duration);
        }

        private void Update()
        {
            if (embers == null) return;
            _emitDebt += emberRate * Time.deltaTime;
            int perEnemy = (int)_emitDebt;
            if (perEnemy <= 0) return;
            _emitDebt -= perEnemy;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.IsBurning) continue;
                for (int k = 0; k < perEnemy; k++)
                {
                    Vector2 ring = Random.insideUnitCircle * e.HitRadius * 0.9f;
                    p.position = e.transform.position + new Vector3(ring.x, Random.Range(0.2f, e.Height * 0.85f), ring.y);
                    p.velocity = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.9f, 1.8f), Random.Range(-0.3f, 0.3f));
                    embers.Emit(p, 1);
                }
            }
        }
    }
}
