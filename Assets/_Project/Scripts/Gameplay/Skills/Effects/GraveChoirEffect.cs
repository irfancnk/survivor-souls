using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Grave Choir" (KAN-246, Legendary): a weapon kill (<see cref="AutoShooter.EnemyHit"/> with a kill) raises a spectral
    /// archer where the foe fell with <see cref="StatId.ChoirChance"/> chance, while fewer than <see cref="maxArchers"/> stand.
    /// An archer lives <see cref="baseLife"/> + <see cref="StatId.ChoirDuration"/> s and every <see cref="fireInterval"/> s
    /// shoots the nearest foe within <see cref="range"/> m with a plain bolt (no pierce, bounce or wall rebound) of
    /// <see cref="StatId.ChoirDamage"/> x the bolt damage. Archer bolts are secondary (<see cref="AutoShooter.LaunchSecondary"/>):
    /// their kills raise no archers and count for no weapon-hit skill. Simple target choice, pooled bodies (copies of
    /// <see cref="archerTemplate"/> made in Awake). Look: pale ghost figures that rise out of the ground and sink away.
    /// </summary>
    public sealed class GraveChoirEffect : SkillEffect
    {
        private const int MaxPool = 4;

        [SerializeField] private Transform archerTemplate;
        [SerializeField, Range(1, MaxPool)] private int maxArchers = 3;
        [SerializeField, Min(1f)] private float baseLife = 6f;
        [SerializeField, Min(0.1f)] private float fireInterval = 0.8f;
        [SerializeField, Min(1f)] private float range = 9f;
        [SerializeField, Min(0.05f)] private float riseTime = 0.35f;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Soul = new Color(0.55f, 0.85f, 1f, 0.85f);

        private readonly Transform[] _archer = new Transform[MaxPool];
        private readonly float[] _life = new float[MaxPool];
        private readonly float[] _age = new float[MaxPool];
        private readonly float[] _fire = new float[MaxPool];
        private System.Action<BoltHit> _onHit;
        private System.Random _rng = new System.Random(246);

        public float Chance => Mathf.Clamp01(PlayerStats.Eval(Context?.Stats, StatId.ChoirChance, 0f));
        public float Life => baseLife + Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.ChoirDuration, 0f));
        public float DamageShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.ChoirDamage, 0f));
        public int MaxArchers => maxArchers;
        /// <summary>Archers standing now.</summary>
        public int Standing
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxPool; i++) if (_life[i] > 0f) n++;
                return n;
            }
        }
        /// <summary>Archers raised this run (tests).</summary>
        public int Raised { get; private set; }
        /// <summary>Archer shots this run (tests).</summary>
        public int Shots { get; private set; }

        private void Awake()
        {
            if (archerTemplate == null) return;
            archerTemplate.gameObject.SetActive(false);
            for (int i = 0; i < MaxPool; i++)
            {
                _archer[i] = i == 0 ? archerTemplate : Instantiate(archerTemplate, transform);
                _archer[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Fixes the chance roll (tests).</summary>
        public void SetSeed(int seed) => _rng = new System.Random(seed);

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
            for (int i = 0; i < MaxPool; i++)
            {
                _life[i] = 0f;
                if (_archer[i] != null) _archer[i].gameObject.SetActive(false);
            }
        }

        private void OnHit(BoltHit hit)
        {
            if (!hit.Killed || hit.Enemy == null) return;
            float chance = Chance;
            if (chance <= 0f || _rng.NextDouble() >= chance) return;
            Raise(hit.Enemy.transform.position);
        }

        /// <summary>Raises an archer at <paramref name="at"/> when the cap has room. Public for tests and debug.</summary>
        public bool Raise(Vector3 at)
        {
            if (Standing >= maxArchers) return false;
            for (int i = 0; i < MaxPool; i++)
            {
                if (_life[i] > 0f || _archer[i] == null) continue;
                _life[i] = Life;
                _age[i] = 0f;
                _fire[i] = fireInterval * 0.5f;
                at.y = Context?.Player != null ? Context.Player.position.y : at.y;
                _archer[i].position = at;
                _archer[i].gameObject.SetActive(true);
                Raised++;
                SkillFx.Rise(fx, at, 0.5f, 1.8f, 16, Soul, 0.16f, 0.8f);
                return true;
            }
            return false;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Archer life, rise / sink and shots for <paramref name="dt"/> scaled s. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (Context == null || dt <= 0f) return;
            for (int i = 0; i < MaxPool; i++)
            {
                if (_life[i] <= 0f) continue;
                var a = _archer[i];
                _life[i] -= dt;
                _age[i] += dt;
                if (_life[i] <= 0f)
                {
                    a.gameObject.SetActive(false);
                    SkillFx.Rise(fx, a.position, 0.4f, 1.6f, 10, Soul, 0.14f, 0.6f);
                    continue;
                }
                float rise = Mathf.Clamp01(_age[i] / riseTime) * Mathf.Clamp01(_life[i] / riseTime);
                a.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, rise);
                var target = SkillFx.Nearest(a.position, range);
                if (target == null) continue;
                Vector3 to = target.transform.position - a.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f) a.rotation = Quaternion.LookRotation(to);
                _fire[i] -= dt;
                if (_fire[i] > 0f) continue;
                _fire[i] = fireInterval;
                Shoot(a.position + Vector3.up * 1.2f, target);
            }
        }

        private void Shoot(Vector3 from, Enemy target)
        {
            var shooter = Context.Shooter;
            if (shooter == null) return;
            var shot = shooter.MakeShot();
            shot.Damage *= DamageShare;
            shot.Pierce = 0;
            shot.Ricochet = 0;
            shot.WallBounces = 0;
            shot.Heat = 0f;
            if (shooter.LaunchSecondary(from, target.AimPoint - from, shot)) Shots++;
            SkillFx.Flash(fx, from, 0.5f, Soul, 0.1f);
        }
    }
}
