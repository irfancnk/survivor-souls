using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Soul Burst": every enemy death bursts a ring of soul fire that deals <see cref="StatId.KillBurst"/> times the
    /// current bolt damage to every enemy within <see cref="radius"/>. Kills from a burst burst again (chain reaction),
    /// one frame later: deaths are queued (fixed ring buffer) and resolved in Update, never inside the damage event, so
    /// <see cref="Enemy.Active"/> is not edited while someone walks it. Ring + sparks via Emit on two shared systems.
    /// </summary>
    public sealed class KillBurstEffect : SkillEffect
    {
        private const int QueueSize = 32;

        [SerializeField, Min(0.5f)] private float radius = 2.6f;
        [SerializeField] private ParticleSystem ring;
        [SerializeField] private ParticleSystem sparks;
        [SerializeField, Min(0)] private int sparkCount = 14;

        private readonly Vector3[] _queue = new Vector3[QueueSize];
        private int _head, _count;
        private System.Action<Enemy, float, bool> _onDamaged;

        public float Radius => radius;
        /// <summary>Bursts resolved this run.</summary>
        public int BurstCount { get; private set; }

        protected override void Subscribe()
        {
            _onDamaged ??= OnDamaged;
            Enemy.Damaged += _onDamaged;
        }

        protected override void Unsubscribe() => Enemy.Damaged -= _onDamaged;

        private void OnDamaged(Enemy enemy, float amount, bool killed)
        {
            if (!killed) return;
            if (_count == QueueSize) { _head = (_head + 1) % QueueSize; _count--; } // drop the oldest under a flood
            _queue[(_head + _count) % QueueSize] = enemy.transform.position;
            _count++;
        }

        private void Update() => Tick();

        /// <summary>Resolves the bursts queued so far (new kills queue for the next call). Public for tests.</summary>
        public void Tick()
        {
            int n = _count;
            if (n == 0) return;
            float share = PlayerStats.Eval(Context.Stats, StatId.KillBurst, 0f);
            float damage = share * (Context.Shooter != null ? Context.Shooter.CurrentDamage : 0f);
            for (int k = 0; k < n; k++)
            {
                Vector3 c = _queue[_head];
                _head = (_head + 1) % QueueSize;
                _count--;
                Explode(c, damage);
            }
        }

        private void Explode(Vector3 centre, float damage)
        {
            BurstCount++;
            PlayFx(centre);
            if (damage <= 0f) return;
            float r2 = radius * radius;
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

        private void PlayFx(Vector3 centre)
        {
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            if (ring != null)
            {
                p.position = centre + Vector3.up * 0.08f;
                p.velocity = Vector3.zero;
                p.startSize = radius * 2.2f;
                ring.Emit(p, 1);
            }
            if (sparks == null) return;
            var s = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < sparkCount; i++)
            {
                float a = i / (float)sparkCount * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                s.position = centre + dir * 0.3f + Vector3.up * 0.4f;
                s.velocity = dir * Random.Range(4.5f, 7f) + Vector3.up * Random.Range(0.8f, 2.2f);
                sparks.Emit(s, 1);
            }
        }
    }
}
