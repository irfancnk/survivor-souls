using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Freeze Arrow" (KAN-91, Rare): every <see cref="StatId.FreezeEvery"/>-th bolt hit is a frost hit that freezes the foe
    /// for <see cref="StatId.FreezeDuration"/> s (<see cref="Enemy.Freeze"/>: rooted, no bite, attacks paused, ice tint).
    /// Only hits that leave the foe alive count, so a frost hit is never spent on a kill. A frost hit on a foe that cannot
    /// freeze now (its <see cref="refreezeDelay"/> after the last freeze still runs) keeps the charge: the next hit tries
    /// again. Bosses freeze for <see cref="bossShare"/> of the time (rooted only, their patterns keep going), elites
    /// (<see cref="Enemy.XpValue"/> &gt;= <see cref="eliteMinXp"/>, the chest elite rule) for <see cref="eliteShare"/>.
    /// Look: ONE shared world-space system (<see cref="frost"/>, Emit only, alpha blend so it reads on the Realm 1 snow) for
    /// the freeze burst (a pale flash, ice shards thrown out, frost mist) and the rime crystals that drift off frozen foes,
    /// plus <see cref="Frozen"/> for the sound (FrostSounds). +1 draw call for any number of frozen foes. No GC per hit.
    /// </summary>
    public sealed class FreezeArrowEffect : SkillEffect
    {
        [Tooltip("Boss freeze time share (bosses are only rooted; their attack patterns keep their timing).")]
        [SerializeField, Range(0f, 1f)] private float bossShare = 0.35f;
        [Tooltip("Elite freeze time share (Grave Brute, Gravebound Knight, ...).")]
        [SerializeField, Range(0f, 1f)] private float eliteShare = 0.6f;
        [Tooltip("XP value from which a foe counts as an elite (same rule as ChestDropConfig.eliteMinXp).")]
        [SerializeField, Min(1)] private int eliteMinXp = 25;
        [Tooltip("Seconds after a thaw before the same foe can freeze again (bosses: x bossRefreezeScale).")]
        [SerializeField, Min(0f)] private float refreezeDelay = 1f;
        [SerializeField, Min(1f)] private float bossRefreezeScale = 4f;

        [Header("Look")]
        [Tooltip("Shared frost system: freeze burst + rime crystals (world space, Emit only).")]
        [SerializeField] private ParticleSystem frost;
        [SerializeField, Min(0)] private int burstShards = 12;
        [SerializeField, Min(0)] private int burstMist = 6;
        [Tooltip("Rime crystals per second per frozen foe.")]
        [SerializeField, Min(0f)] private float rimeRate = 7f;

        /// <summary>A frost hit froze a foe (KAN-91). For sounds; raised after the freeze took.</summary>
        public static event System.Action<Enemy> Frozen;

        private static readonly Color FlashColour = new Color(0.86f, 0.96f, 1f, 0.9f);
        private static readonly Color ShardColour = new Color(0.62f, 0.86f, 1f);
        private static readonly Color MistColour = new Color(0.78f, 0.9f, 1f, 0.55f);
        private static readonly Color RimeColour = new Color(0.7f, 0.9f, 1f);

        private System.Action<BoltHit> _onHit;
        private int _charge;
        private float _rimeDebt;

        /// <summary>Bolt hits per frost hit (0 = no frost).</summary>
        public int Every => Mathf.Max(0, Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.FreezeEvery, 0f)));
        /// <summary>Freeze length in seconds on a normal foe.</summary>
        public float Duration => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.FreezeDuration, 0f));
        /// <summary>Counted hits since the last frost hit (tests). At <see cref="Every"/> the next hit is a frost hit.</summary>
        public int Charge => _charge;
        /// <summary>Foes frozen this run (tests, dev log).</summary>
        public int FreezeCount { get; private set; }
        /// <summary>Frost hits that met a foe on its refreeze delay (the charge waited) (tests).</summary>
        public int RefusedCount { get; private set; }
        public float BossShare => bossShare;
        public float EliteShare => eliteShare;
        public float RefreezeDelay => refreezeDelay;

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
        }

        /// <summary>Freeze time for <paramref name="enemy"/>: <see cref="Duration"/> x the boss / elite share.</summary>
        public float DurationFor(Enemy enemy)
        {
            float d = Duration;
            if (enemy == null) return d;
            if (enemy.IsBoss) return d * bossShare;
            return enemy.XpValue >= eliteMinXp ? d * eliteShare : d;
        }

        private void OnHit(BoltHit hit)
        {
            int every = Every;
            if (every <= 0 || hit.Killed || hit.Enemy == null || !hit.Enemy.IsAlive) return;
            if (_charge < every) _charge++;
            if (_charge < every) return;
            var enemy = hit.Enemy;
            float seconds = DurationFor(enemy);
            float delay = refreezeDelay * (enemy.IsBoss ? bossRefreezeScale : 1f);
            if (!enemy.Freeze(seconds, seconds + delay))
            {
                RefusedCount++; // the charge waits for a foe that can freeze
                return;
            }
            _charge = 0;
            FreezeCount++;
            EmitBurst(enemy, hit.Point);
            Frozen?.Invoke(enemy);
        }

        private void Update() => EmitRime(Time.deltaTime);

        /// <summary>Rime crystals drifting off every frozen foe. Public for tests (Update calls it).</summary>
        public void EmitRime(float dt)
        {
            if (frost == null || rimeRate <= 0f) return;
            _rimeDebt += rimeRate * dt;
            int perEnemy = (int)_rimeDebt;
            if (perEnemy <= 0) return;
            _rimeDebt -= perEnemy;
            if (perEnemy > 3) perEnemy = 3; // a hitch never dumps a burst
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.IsFrozen) continue;
                float scale = e.Scale;
                Vector3 root = e.transform.position;
                for (int k = 0; k < perEnemy; k++)
                {
                    // On the skin (inside the body they would be hidden), slow drift up and out.
                    Vector2 ring = Random.insideUnitCircle.normalized * (e.HitRadius * Random.Range(0.85f, 1.1f));
                    p.position = root + new Vector3(ring.x, Random.Range(0.15f, e.Height * 0.95f), ring.y);
                    p.velocity = new Vector3(ring.x * 0.3f, Random.Range(0.25f, 0.6f), ring.y * 0.3f);
                    p.startSize = Random.Range(0.1f, 0.18f) * scale;
                    p.startLifetime = Random.Range(0.5f, 0.8f);
                    p.startColor = RimeColour;
                    frost.Emit(p, 1);
                }
            }
        }

        /// <summary>Freeze burst at the hit: a pale flash on the chest, ice shards thrown out, a puff of frost mist round the feet.</summary>
        private void EmitBurst(Enemy e, Vector3 point)
        {
            if (frost == null) return;
            float scale = e.Scale;
            Vector3 chest = e.AimPoint;
            Vector3 root = e.transform.position;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            p.position = chest;
            p.velocity = Vector3.zero;
            p.startSize = 1.6f * scale;
            p.startLifetime = 0.16f;
            p.startColor = FlashColour;
            frost.Emit(p, 1);
            for (int i = 0; i < burstShards; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.25f;
                p.position = Vector3.Lerp(chest, point, 0.5f) + dir * (0.1f * scale);
                p.velocity = dir.normalized * Random.Range(2.8f, 5f);
                p.startSize = Random.Range(0.1f, 0.2f) * scale;
                p.startLifetime = Random.Range(0.3f, 0.5f);
                p.startColor = ShardColour;
                frost.Emit(p, 1);
            }
            for (int i = 0; i < burstMist; i++)
            {
                float a = (i + Random.value * 0.5f) / Mathf.Max(1, burstMist) * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                p.position = root + dir * (e.HitRadius * 0.9f) + Vector3.up * 0.25f;
                p.velocity = dir * Random.Range(0.6f, 1.1f) + Vector3.up * 0.2f;
                p.startSize = Random.Range(0.8f, 1.2f) * scale;
                p.startLifetime = Random.Range(0.6f, 0.85f);
                p.startColor = MistColour;
                frost.Emit(p, 1);
            }
        }
    }
}
