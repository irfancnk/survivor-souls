using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Sovereign's Fall" (KAN-246, Legendary): every <see cref="critsNeeded"/> direct weapon critical hits (<see cref="BoltHit.Crit"/>
    /// on <see cref="AutoShooter.EnemyHit"/>), at most once per <see cref="minInterval"/> s, a colossal spectral sword falls
    /// on the struck foe (or, if it died, on the nearest foe to it) and hits every foe within <see cref="Radius"/> m of the
    /// impact for <see cref="StatId.SwordDamage"/> x the bolt damage. Damage, not an instant kill: bosses take it like anyone.
    /// The sword tracks its target while it falls (<see cref="fallTime"/> s), stands in the ground for <see cref="holdTime"/>
    /// s and fades. Look: blade mesh (<see cref="sword"/>), white-gold flash, impact ring, small camera shake.
    /// </summary>
    public sealed class SovereignsFallEffect : SkillEffect
    {
        [SerializeField] private Transform sword;
        [SerializeField, Min(1)] private int critsNeeded = 6;
        [SerializeField, Min(0f)] private float minInterval = 4f;
        [SerializeField, Min(0.5f)] private float baseRadius = 2.5f;
        [SerializeField, Min(0.05f)] private float fallTime = 0.35f;
        [SerializeField, Min(0f)] private float holdTime = 0.6f;
        [SerializeField, Min(1f)] private float dropHeight = 9f;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Flash = new Color(1f, 0.95f, 0.8f, 1f);
        private static readonly Color Ring = new Color(1f, 0.8f, 0.4f, 0.9f);

        private System.Action<BoltHit> _onHit;
        private int _crits;
        private float _sinceLast = 999f;
        private float _phase = -1f; // < 0 idle, 0..fall falling, then holding
        private Enemy _target;
        private Vector3 _impact;
        private TopDownFollowCamera _camera;
        private bool _cameraLooked;

        public float Radius => Mathf.Max(0.5f, PlayerStats.Eval(Context?.Stats, StatId.SwordRadius, baseRadius));
        public float DamageShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.SwordDamage, 0f));
        public int Crits => _crits;
        public int CritsNeeded => critsNeeded;
        public bool Falling => _phase >= 0f && _phase < fallTime;
        /// <summary>Swords that landed this run (tests).</summary>
        public int Strikes { get; private set; }
        /// <summary>Foes hit by the last sword (tests).</summary>
        public int LastStrikeHits { get; private set; }

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
            _phase = -1f;
            if (sword != null) sword.gameObject.SetActive(false);
        }

        private void OnHit(BoltHit hit)
        {
            if (!hit.Crit || hit.Enemy == null || DamageShare <= 0f) return;
            if (_crits < critsNeeded) _crits++;
            if (_crits < critsNeeded || _sinceLast < minInterval || _phase >= 0f) return;
            var target = hit.Enemy.IsAlive ? hit.Enemy : SkillFx.Nearest(hit.Enemy.transform.position, 6f, hit.Enemy);
            Drop(target, hit.Enemy.transform.position);
        }

        /// <summary>Drops the sword on <paramref name="target"/> (or at <paramref name="fallback"/>). Public for tests.</summary>
        public void Drop(Enemy target, Vector3 fallback)
        {
            _crits = 0;
            _sinceLast = 0f;
            _target = target;
            _impact = target != null ? target.transform.position : fallback;
            _phase = 0f;
            if (sword != null)
            {
                sword.gameObject.SetActive(true);
                sword.localScale = Vector3.one;
                sword.SetPositionAndRotation(_impact + Vector3.up * dropHeight, Quaternion.Euler(0f, 30f, 0f));
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Fall, impact, hold and fade for <paramref name="dt"/> scaled s. Public for tests.</summary>
        public void Tick(float dt)
        {
            _sinceLast += dt;
            if (_phase < 0f) return;
            float before = _phase;
            _phase += dt;
            if (before < fallTime)
            {
                if (_target != null && _target.IsAlive) _impact = _target.transform.position;
                float t = Mathf.Clamp01(_phase / fallTime);
                if (sword != null) sword.position = _impact + Vector3.up * (Mathf.Lerp(dropHeight, 0f, t * t) + 0.2f);
                if (_phase >= fallTime) Impact();
                return;
            }
            float fade = Mathf.Clamp01((_phase - fallTime - holdTime) / 0.3f);
            if (sword != null) sword.localScale = Vector3.one * (1f - fade);
            if (fade >= 1f)
            {
                _phase = -1f;
                if (sword != null) sword.gameObject.SetActive(false);
            }
        }

        private void Impact()
        {
            Strikes++;
            float r = Radius;
            float damage = SkillFx.BoltShare(Context, DamageShare);
            int hits = 0;
            var enemies = Enemy.Active;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (i >= enemies.Count) continue;
                var e = enemies[i];
                if (!e.IsAlive) continue;
                Vector3 d = e.transform.position - _impact;
                d.y = 0f;
                float reach = r + e.HitRadius;
                if (d.sqrMagnitude > reach * reach) continue;
                hits++;
                if (damage > 0f) e.TakeDamage(damage, d, DamageKind.Burst);
            }
            LastStrikeHits = hits;
            SkillFx.Flash(fx, _impact + Vector3.up * 0.8f, 3f, Flash, 0.18f);
            SkillFx.Ring(fx, _impact, r, 40, Ring, 0.45f, 0.35f);
            SkillFx.Sparks(fx, _impact + Vector3.up * 0.3f, 20, Flash, 6f, 0.12f, 0.4f);
            Haptics.Light();
            if (!_cameraLooked)
            {
                _cameraLooked = true;
                _camera = FindAnyObjectByType<TopDownFollowCamera>();
            }
            if (_camera != null) _camera.Shake(0.09f, 0.22f);
        }
    }
}
