using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Stolen Moment" (KAN-246, Legendary): every <see cref="RequiredVolleys"/> volleys of the main weapon
    /// (<see cref="AutoShooter.Fired"/>: original volleys only), a stationary time echo appears where the Knight stands and
    /// repeats the firing pattern <see cref="repeats"/> times, one every <see cref="repeatInterval"/> s, at
    /// <see cref="StatId.EchoDamage"/> of the damage (<see cref="AutoShooter.FireEcho"/>: spread, forward lanes, rear bolts;
    /// no Multishot repeat). The Knight is free to move away meanwhile. Each echo volley aims at the nearest foe to the echo
    /// (else the last aim line). Echo bolts are secondary: they make no echoes and trigger no weapon-hit skill. One echo at a
    /// time; volleys keep counting while it plays. Look: a pale blue ghost of the Knight's stance and a time ripple.
    /// </summary>
    public sealed class StolenMomentEffect : SkillEffect
    {
        [SerializeField] private Transform echoBody;
        [SerializeField, Min(1)] private int baseVolleys = 8;
        [SerializeField, Min(1)] private int repeats = 3;
        [SerializeField, Min(0.05f)] private float repeatInterval = 0.35f;
        [SerializeField, Min(1f)] private float range = 10f;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Ripple = new Color(0.6f, 0.85f, 1f, 0.8f);
        private static readonly Color Spark = new Color(0.8f, 0.95f, 1f, 1f);

        private System.Action<Vector3, Vector3> _onFired;
        private int _count;
        private int _left;
        private float _timer;
        private Vector3 _at;

        public int RequiredVolleys => Mathf.Max(2, Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.EchoVolleys, baseVolleys)));
        public float DamageShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.EchoDamage, 0f));
        public int Counted => _count;
        public bool Playing => _left > 0;
        public Vector3 EchoPosition => _at;
        /// <summary>Echoes started this run (tests).</summary>
        public int Echoes { get; private set; }
        /// <summary>Echo volleys fired this run (tests).</summary>
        public int EchoVolleys { get; private set; }

        protected override void Subscribe()
        {
            _onFired ??= OnFired;
            if (Context.Shooter != null) Context.Shooter.Fired += _onFired;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.Fired -= _onFired;
            _left = 0;
            if (echoBody != null) echoBody.gameObject.SetActive(false);
        }

        private void OnFired(Vector3 muzzle, Vector3 dir)
        {
            if (DamageShare <= 0f) return;
            _count++;
            if (_count < RequiredVolleys || _left > 0) return;
            _count = 0;
            Begin(Context.Player != null ? Context.Player.position : transform.position, Context.Player != null ? Context.Player.rotation : Quaternion.identity);
        }

        /// <summary>Starts an echo at <paramref name="at"/> now (tests, debug).</summary>
        public void Begin(Vector3 at, Quaternion facing)
        {
            Echoes++;
            _at = at;
            _left = repeats;
            _timer = 0.12f;
            if (echoBody != null)
            {
                echoBody.SetPositionAndRotation(at, facing);
                echoBody.gameObject.SetActive(true);
            }
            SkillFx.Ring(fx, at, 1.4f, 28, Ripple, 0.35f, 0.4f);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Plays the echo volleys for <paramref name="dt"/> scaled s. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (_left <= 0 || Context?.Shooter == null) return;
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = repeatInterval;
            _left--;
            Vector3 from = _at + Vector3.up * 1.1f;
            var target = SkillFx.Nearest(_at, range);
            Vector3 dir = target != null ? target.AimPoint - from : Context.Shooter.LastAimDirection;
            dir.y = 0f;
            if (echoBody != null && dir.sqrMagnitude > 1e-4f) echoBody.rotation = Quaternion.LookRotation(dir);
            Context.Shooter.FireEcho(from, dir, DamageShare);
            EchoVolleys++;
            SkillFx.Sparks(fx, from, 6, Spark, 2.5f, 0.08f, 0.25f);
            if (_left > 0) return;
            if (echoBody != null) echoBody.gameObject.SetActive(false);
            SkillFx.Ring(fx, _at, 1f, 20, Ripple, 0.3f, 0.35f);
        }
    }
}
