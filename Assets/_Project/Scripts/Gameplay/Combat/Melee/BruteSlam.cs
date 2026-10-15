using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Grave Brute (KAN-106, Docs/ENEMY_ROSTER.md 3.10): a heavy elite that slams the ground.
    /// <list type="number">
    /// <item>Wind-up: with the Knight within <see cref="Settings.triggerRange"/> m it stops, plays Slam and a red circle of the
    /// whole danger area (<see cref="Settings.radius"/>) fills round it for <see cref="Settings.windUp"/> s.</item>
    /// <item>Shockwave: the circle flashes and a ring grows from the Brute to the circle's edge in
    /// <see cref="Settings.waveTime"/> s. Only the ring's band (<see cref="Settings.width"/>) hurts, once per slam, as it
    /// sweeps over the Knight (<see cref="Settings.damageShare"/> of max health): a Knight already inside the ring after it
    /// passes, or outside the circle, is safe. Dash through it or outrun it.</item>
    /// <item>Recover <see cref="Settings.recover"/> s, then it walks again (cooldown <see cref="Settings.cooldown"/>).</item>
    /// </list>
    /// The ring is one pooled circle warning in its wave mode (<see cref="GroundTelegraph.ShowWave"/>), scaled per frame.
    /// The Brute's knockback immunity is on its <see cref="Enemy"/> (knockback scale 0).
    /// </summary>
    public sealed class BruteSlam : SpecialAttack
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Distance (m) to the Knight at which the slam starts.")]
            [Min(0.5f)] public float triggerRange;
            [Tooltip("Seconds of wind-up (circle warning) before the slam.")]
            [Min(MinTelegraph)] public float windUp;
            [Tooltip("Outer radius (m) the ring reaches = the warning circle.")]
            [Min(0.5f)] public float radius;
            [Tooltip("Ring band width (m).")]
            [Min(0.1f)] public float width;
            [Tooltip("Seconds the ring takes from the Brute to the circle's edge.")]
            [Min(0.05f)] public float waveTime;
            [Tooltip("Damage, share of the Knight's max health.")]
            [Range(0f, 1f)] public float damageShare;
            [Tooltip("Seconds the Brute stays still after the ring ends.")]
            [Min(0f)] public float recover;
            [Tooltip("Seconds from the end of a slam to the next wind-up.")]
            [Min(0.1f)] public float cooldown;
        }

        [SerializeField] private Settings settings;

        private GroundTelegraph _circle;
        private GroundTelegraph _wave;
        private Vector3 _origin;
        private float _front = -1f;
        private bool _hit;

        /// <summary>A Brute's ring ended: centre, hit the Knight.</summary>
        public static event Action<BruteSlam, Vector3, bool> WaveEnded;

        public Settings Config => settings;
        /// <summary>The warning circle of the wind-up (null when none).</summary>
        public GroundTelegraph Circle => _circle;
        /// <summary>The growing ring (null when none).</summary>
        public GroundTelegraph Wave => _wave;
        /// <summary>Radius (m) of the ring's front; -1 when no ring grows.</summary>
        public float Front => _phase == Phase.Strike ? _front : -1f;
        /// <summary>Slam centre.</summary>
        public Vector3 Origin => _origin;
        public bool HitThisSlam => _hit;
        protected override float SpawnCooldown => settings.cooldown;

        /// <summary>Tests and tools: sets the attack without the builder.</summary>
        public void Configure(in Settings value) => settings = value;

        /// <summary>Radius where the ring's front stops: its band's outer edge on the warning circle.</summary>
        public float WaveEnd => Mathf.Max(settings.width * 0.5f, settings.radius - settings.width * 0.5f);

        protected override bool WantsToStart(PlayerHealth knight)
        {
            if (Planar(knight.transform.position, transform.position) > settings.triggerRange) return false;
            var pool = EnemyTelegraphs.Current;
            return pool != null && pool.FreeCircles() >= 2; // warning + ring: no invisible wave
        }

        protected override bool Begin(PlayerHealth knight)
        {
            var pool = EnemyTelegraphs.Current;
            if (pool == null || pool.FreeCircles() < 2) return false;
            _circle = pool.RentCircle();
            _origin = transform.position;
            _circle.Show(_origin, settings.radius);
            _hit = false;
            _front = -1f;
            Vector3 to = knight.transform.position - _origin;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to);
            Enemy.PlayAction("Slam");
            return true;
        }

        protected override void Step(PlayerHealth knight, float dt)
        {
            switch (_phase)
            {
                case Phase.WindUp:
                    if (_circle != null) _circle.SetProgress(settings.windUp > 0f ? _time / settings.windUp : 1f);
                    if (_time >= settings.windUp) Slam();
                    break;
                case Phase.Strike:
                    Grow(knight, dt);
                    break;
                case Phase.Recover:
                    if (_time >= settings.recover) EndAttack(settings.cooldown);
                    break;
            }
        }

        private void Slam()
        {
            if (_circle != null) _circle.Flash();
            _circle = ReleaseCircle(_circle, hide: false); // the flash fades on its own
            var pool = EnemyTelegraphs.Current;
            _wave = pool != null ? pool.RentCircle() : null;
            _front = 0f;
            Enter(Phase.Strike);
            RaiseStruck();
        }

        /// <summary>The front moves out at constant speed; the band [front - half, front + half] sweeping over the Knight hits once.</summary>
        private void Grow(PlayerHealth knight, float dt)
        {
            float half = settings.width * 0.5f;
            float end = WaveEnd;
            float speed = end / Mathf.Max(0.05f, settings.waveTime);
            float prev = _front;
            float next = Mathf.Min(end, prev + speed * dt);
            _front = next;
            if (!_hit && knight != null && !knight.IsDead)
            {
                float d = Planar(knight.transform.position, _origin);
                if (d >= prev - half && d <= next + half)
                {
                    _hit = true;
                    Hurt(knight, settings.damageShare);
                }
            }
            if (_wave != null) _wave.ShowWave(_origin, next, next / end);
            if (next < end) return;
            _wave = ReleaseCircle(_wave, hide: true);
            Enter(Phase.Recover);
            WaveEnded?.Invoke(this, _origin, _hit);
        }

        public override void Cancel()
        {
            _circle = ReleaseCircle(_circle, hide: true);
            _wave = ReleaseCircle(_wave, hide: true);
            _front = -1f;
            base.Cancel();
        }
    }
}
