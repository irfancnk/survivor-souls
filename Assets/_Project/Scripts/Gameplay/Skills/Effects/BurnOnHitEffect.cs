using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Ember Bolts": every bolt hit sets the enemy on fire (<see cref="Enemy.ApplyBurn"/>) for <see cref="Duration"/>
    /// seconds (<see cref="duration"/> + <see cref="StatId.BurnDuration"/>) at <see cref="StatId.BurnDps"/> damage per
    /// second. Embers rise from burning enemies through ONE shared world-space particle system (<see cref="embers"/>,
    /// Emit only): one draw call for any number of fires.
    /// Level perks (KAN-77): with <see cref="StatId.BurnSpread"/>, every burn tick of a bolt-lit enemy sets that many of the
    /// nearest non-burning foes within <see cref="spreadRadius"/> on fire (same damage and length). A caught fire does not
    /// spread on (one step). With <see cref="StatId.BurnDeathBlast"/>, a burning enemy's death blasts that share of the bolt
    /// damage to every foe within <see cref="blastRadius"/>. Deaths are queued (fixed ring buffer) and resolved in Update,
    /// never inside the damage event, like <see cref="KillBurstEffect"/>. Both perks reuse the ember system for their FX.
    /// </summary>
    public sealed class BurnOnHitEffect : SkillEffect
    {
        private const int QueueSize = 32;

        [SerializeField, Min(0.1f)] private float duration = 3f;
        [SerializeField] private ParticleSystem embers;
        [Tooltip("Embers per second per burning enemy.")]
        [SerializeField, Min(0f)] private float emberRate = 14f;
        [Tooltip("KAN-77: a burning enemy sets foes within this range (m) on fire.")]
        [SerializeField, Min(0.5f)] private float spreadRadius = 2.5f;
        [Tooltip("KAN-77: a burning enemy's death blast reaches this far (m).")]
        [SerializeField, Min(0.5f)] private float blastRadius = 2.2f;
        [SerializeField, Min(0)] private int spreadEmbers = 8;
        [SerializeField, Min(0)] private int blastEmbers = 22;

        private float _emitDebt;
        private readonly Vector3[] _queue = new Vector3[QueueSize];
        private int _head, _count;

        /// <summary>Burn length in seconds after skill levels (KAN-71: higher Ember Bolts levels burn longer).</summary>
        public float Duration => Mathf.Max(0.1f, PlayerStats.Eval(Context?.Stats, StatId.BurnDuration, duration));
        /// <summary>Spread reach in metres (KAN-77).</summary>
        public float SpreadRadius => spreadRadius;
        /// <summary>Death blast reach in metres (KAN-77).</summary>
        public float BlastRadius => blastRadius;
        /// <summary>Fires caught from a neighbour this run (KAN-77).</summary>
        public int SpreadCount { get; private set; }
        /// <summary>Death blasts resolved this run (KAN-77).</summary>
        public int BlastCount { get; private set; }

        private System.Action<BoltHit> _onHit;
        private System.Action<Enemy, float, bool> _onDamaged;

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            _onDamaged ??= OnDamaged;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
            Enemy.Damaged += _onDamaged;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
            Enemy.Damaged -= _onDamaged;
        }

        private void OnHit(BoltHit hit)
        {
            float dps = PlayerStats.Eval(Context.Stats, StatId.BurnDps, 0f);
            if (dps > 0f && !hit.Killed) hit.Enemy.ApplyBurn(dps, Duration);
        }

        private void OnDamaged(Enemy enemy, float amount, bool killed)
        {
            if (killed)
            {
                if (enemy.BurnTimeLeft <= 0f) return;
                if (PlayerStats.Eval(Context.Stats, StatId.BurnDeathBlast, 0f) <= 0f) return;
                if (_count == QueueSize) { _head = (_head + 1) % QueueSize; _count--; } // drop the oldest under a flood
                _queue[(_head + _count) % QueueSize] = enemy.transform.position;
                _count++;
                return;
            }
            if (enemy.LastDamageKind != DamageKind.Burn || enemy.BurnFromSpread) return;
            int spread = Mathf.RoundToInt(PlayerStats.Eval(Context.Stats, StatId.BurnSpread, 0f));
            if (spread > 0) Spread(enemy, spread);
        }

        /// <summary>
        /// Sets up to <paramref name="count"/> of the nearest non-burning foes within <see cref="spreadRadius"/> of
        /// <paramref name="source"/> on fire, marked as caught (they do not spread on). Only reads <see cref="Enemy.Active"/>.
        /// </summary>
        private void Spread(Enemy source, int count)
        {
            float dps = source.BurnDps;
            if (dps <= 0f) return;
            float length = Duration;
            Vector3 from = source.transform.position;
            float r2 = spreadRadius * spreadRadius;
            var enemies = Enemy.Active;
            for (int n = 0; n < count; n++)
            {
                Enemy best = null;
                float bestD2 = r2;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (e == source || e.IsBurning || !e.IsAlive) continue;
                    Vector3 d = e.transform.position - from;
                    d.y = 0f;
                    float d2 = d.sqrMagnitude;
                    if (d2 > bestD2) continue;
                    bestD2 = d2;
                    best = e;
                }
                if (best == null) return;
                best.ApplyBurn(dps, length, spread: true);
                SpreadCount++;
                EmitLink(from, best.transform.position);
            }
        }

        private void Update()
        {
            Tick();
            EmitEmbers();
        }

        /// <summary>Resolves the death blasts queued so far (blast kills of burning foes queue for the next call). Public for tests.</summary>
        public void Tick()
        {
            int n = _count;
            if (n == 0) return;
            float share = PlayerStats.Eval(Context.Stats, StatId.BurnDeathBlast, 0f);
            float damage = share * (Context.Shooter != null ? Context.Shooter.CurrentDamage : 0f);
            for (int k = 0; k < n; k++)
            {
                Vector3 c = _queue[_head];
                _head = (_head + 1) % QueueSize;
                _count--;
                Blast(c, damage);
            }
        }

        private void Blast(Vector3 centre, float damage)
        {
            BlastCount++;
            EmitBlast(centre);
            if (damage <= 0f) return;
            float r2 = blastRadius * blastRadius;
            var enemies = Enemy.Active;
            for (int i = enemies.Count - 1; i >= 0; i--) // backwards: a kill removes enemies[i] only
            {
                if (i >= enemies.Count) continue;
                var e = enemies[i];
                Vector3 d = e.transform.position - centre;
                d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                e.TakeDamage(damage, d, DamageKind.Burst);
            }
        }

        private void EmitEmbers()
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

        /// <summary>A short ember stream from the source fire to the foe that caught it.</summary>
        private void EmitLink(Vector3 from, Vector3 to)
        {
            if (embers == null || spreadEmbers <= 0) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            Vector3 step = to - from;
            for (int i = 0; i < spreadEmbers; i++)
            {
                float t = (i + 0.5f) / spreadEmbers;
                p.position = from + step * t + Vector3.up * (0.6f + Mathf.Sin(t * Mathf.PI) * 0.5f);
                p.velocity = step * 0.6f + new Vector3(0f, Random.Range(0.4f, 1.1f), 0f);
                embers.Emit(p, 1);
            }
        }

        /// <summary>A ring of fast, larger embers for a burning death blast.</summary>
        private void EmitBlast(Vector3 centre)
        {
            if (embers == null || blastEmbers <= 0) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < blastEmbers; i++)
            {
                float a = i / (float)blastEmbers * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                p.position = centre + dir * 0.25f + Vector3.up * 0.5f;
                p.velocity = dir * (blastRadius * Random.Range(2.2f, 3f)) + Vector3.up * Random.Range(0.6f, 1.8f);
                p.startSize = Random.Range(0.14f, 0.24f);
                embers.Emit(p, 1);
            }
        }
    }
}
