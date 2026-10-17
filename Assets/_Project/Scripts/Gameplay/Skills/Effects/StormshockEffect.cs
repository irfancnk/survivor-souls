using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Stormshock" (KAN-246, Epic): every <see cref="hitsPerChain"/>-th weapon hit releases yellow chain lightning into the
    /// struck zombie and on to the nearest zombie not hit yet within <see cref="jumpRange"/> m of the last one, up to
    /// <see cref="StatId.ShockTargets"/> zombies in all. Each takes <see cref="StatId.ShockDamage"/> x the bolt damage and a
    /// <see cref="stun"/> s mini-stun (<see cref="Enemy.Stun"/>: no stacking, <see cref="restun"/> s before the same zombie
    /// can be stunned again, never bosses: a boss takes the damage and a short reduced slow). A chain never revisits a zombie
    /// and stops when no unhit zombie is in reach. At least <see cref="chainCooldown"/> s between two chains: a ready charge
    /// waits for the cooldown. Chain hits are secondary damage (<see cref="ProcGuard"/>): they trigger no Storm Touch,
    /// Ricochet, Stormshock or weapon-hit counter. Look: thin jagged yellow arcs (<see cref="arcs"/>), a yellow flicker on
    /// each zombie (<see cref="Enemy.Shock"/>), small sparks. No full-screen flash.
    /// </summary>
    public sealed class StormshockEffect : SkillEffect
    {
        private const int MaxTargets = 8;

        [SerializeField, Min(1)] private int hitsPerChain = 4;
        [SerializeField, Min(0.5f)] private float jumpRange = 3f;
        [SerializeField, Min(0f)] private float stun = 0.25f;
        [SerializeField, Min(0f)] private float restun = 1f;
        [SerializeField, Min(0f)] private float chainCooldown = 2f;
        [Tooltip("Slow share (bosses take half) and length for a boss in the chain.")]
        [SerializeField, Range(0f, 0.8f)] private float bossSlow = 0.3f;
        [SerializeField, Min(0f)] private float bossSlowTime = 0.6f;
        [SerializeField] private ArcLines arcs;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color ArcColour = new Color(1f, 0.88f, 0.25f, 1f);
        private static readonly Color SparkColour = new Color(1f, 0.93f, 0.5f, 1f);

        private readonly Enemy[] _chain = new Enemy[MaxTargets];
        private System.Action<BoltHit> _onHit;
        private int _charge;
        private float _cooldown;

        public int Targets => Mathf.Clamp(Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.ShockTargets, 0f)), 0, MaxTargets);
        public float DamageShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.ShockDamage, 0f));
        public int HitsPerChain => hitsPerChain;
        public float JumpRange => jumpRange;
        public float ChainCooldown => chainCooldown;
        public int Charge => _charge;
        /// <summary>Chains released this run (tests).</summary>
        public int ChainCount { get; private set; }
        /// <summary>Zombies struck by chains this run (tests).</summary>
        public int ChainHits { get; private set; }
        /// <summary>Zombies of the last chain, in order (tests). Valid until the next chain.</summary>
        public int LastChainLength { get; private set; }

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Counts the chain cooldown down (scaled time). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (_cooldown > 0f) _cooldown -= dt;
        }

        private void OnHit(BoltHit hit)
        {
            int targets = Targets;
            if (targets <= 0 || hit.Enemy == null) return;
            if (_charge < hitsPerChain) _charge++;
            if (_charge < hitsPerChain || _cooldown > 0f) return;
            _charge = 0;
            _cooldown = chainCooldown;
            Release(hit.Enemy, hit.Point, targets);
        }

        /// <summary>One chain from <paramref name="first"/> (alive or just killed by the bolt). Public for tests.</summary>
        public void Release(Enemy first, Vector3 start, int targets)
        {
            ChainCount++;
            float damage = SkillFx.BoltShare(Context, DamageShare);
            int n = 0;
            _chain[n++] = first;
            Vector3 at = first.transform.position;
            Vector3 from = start;
            Strike(first, from, from, damage);
            while (n < targets)
            {
                var next = Next(at, n);
                if (next == null) break; // nobody unhit within the jump range: the chain stops
                _chain[n++] = next;
                Vector3 to = next.AimPoint;
                if (arcs != null) arcs.Arc(from, to, ArcColour, 0.22f, 1.2f);
                Strike(next, from, to, damage);
                at = next.transform.position;
                from = to;
            }
            LastChainLength = n;
            for (int i = 0; i < n; i++) _chain[i] = null;
        }

        private void Strike(Enemy e, Vector3 from, Vector3 at, float damage)
        {
            if (e == null) return;
            SkillFx.Sparks(fx, at, 6, SparkColour, 3f, 0.08f, 0.22f);
            if (!e.IsAlive) return;
            e.Shock(0.3f);
            if (e.IsBoss) e.ApplySlow(bossSlow, bossSlowTime);
            else e.Stun(stun, restun);
            e.TakeDamage(damage, at - from, DamageKind.Burst);
            ChainHits++;
        }

        private Enemy Next(Vector3 from, int count)
        {
            Enemy best = null;
            float bestSq = jumpRange * jumpRange;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.IsAlive || InChain(e, count)) continue;
                Vector3 d = e.transform.position - from;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        private bool InChain(Enemy e, int count)
        {
            for (int i = 0; i < count; i++)
                if (_chain[i] == e) return true;
            return false;
        }
    }
}
