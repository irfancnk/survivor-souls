using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Frenzied Ghoul (KAN-106, Docs/ENEMY_ROSTER.md 3.9): a fast flanker that leaps.
    /// <list type="number">
    /// <item>Crouch: with the Knight <see cref="Settings.minRange"/>..<see cref="Settings.maxRange"/> m away it stops and
    /// crouches for <see cref="Settings.crouch"/> s; a red lane shows the leap (direction locked at the start: side-step it).
    /// The lane stops at pillars and walls; with less than <see cref="Settings.minLeap"/> m free the Ghoul runs on.</item>
    /// <item>Leap: it flies along the lane (<see cref="Settings.leapTime"/> s, arc <see cref="Settings.arcHeight"/> m). The
    /// Knight inside the lane (half width + <see cref="Settings.hitPadding"/>) when the Ghoul passes loses
    /// <see cref="Settings.damageShare"/> of its max health, once per leap.</item>
    /// <item>Stun: it lands and staggers for <see cref="Settings.stun"/> s (the punish window), then sprints again.</item>
    /// </list>
    /// Crouch is 0.6 s, not the 0.5 s of the roster doc: every warning shows at least <see cref="SpecialAttack.MinTelegraph"/> s.
    /// </summary>
    public sealed class GhoulLeap : SpecialAttack
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Nearest distance (m) to the Knight at which a leap starts.")]
            [Min(0f)] public float minRange;
            [Tooltip("Farthest distance (m) to the Knight at which a leap starts.")]
            [Min(0.5f)] public float maxRange;
            [Tooltip("Seconds of crouch (lane warning) before the leap.")]
            [Min(MinTelegraph)] public float crouch;
            [Tooltip("Leap length (m) along the lane.")]
            [Min(0.5f)] public float leapDistance;
            [Tooltip("Shortest free leap (m): a pillar closer than this = no leap.")]
            [Min(0.5f)] public float minLeap;
            [Tooltip("Seconds in the air.")]
            [Min(0.05f)] public float leapTime;
            [Tooltip("Apex height (m) of the leap.")]
            [Min(0f)] public float arcHeight;
            [Tooltip("Lane width (m).")]
            [Min(0.1f)] public float laneWidth;
            [Tooltip("The Knight is hit while its centre is within half the lane width + this (m) of the Ghoul's path.")]
            [Min(0f)] public float hitPadding;
            [Tooltip("Damage, share of the Knight's max health.")]
            [Range(0f, 1f)] public float damageShare;
            [Tooltip("Seconds of stagger after the landing.")]
            [Min(0f)] public float stun;
            [Tooltip("Seconds from a landing to the next crouch (stun included).")]
            [Min(0.1f)] public float cooldown;
        }

        private const float BodyRadius = 0.35f;
        private const float LaneStep = 0.25f; // 5 m = 20 probes: a free lane is the full leap
        private const float TurnSpeed = 900f;

        [SerializeField] private Settings settings;

        private LineTelegraph _line;
        private Vector3 _dir = Vector3.forward;
        private Vector3 _from;
        private float _length;
        private float _groundY;
        private bool _hit;

        /// <summary>A Ghoul landed: where, hit the Knight.</summary>
        public static event Action<GhoulLeap, Vector3, bool> Landed;

        public Settings Config => settings;
        /// <summary>The lane of the current crouch (null when none).</summary>
        public LineTelegraph Lane => _line;
        /// <summary>Leap direction (locked at the crouch start).</summary>
        public Vector3 LeapDirection => _dir;
        /// <summary>Leap length (m) after the pillar cut.</summary>
        public float LeapLength => _length;
        /// <summary>Hit the Knight in the current / last leap.</summary>
        public bool HitThisLeap => _hit;
        /// <summary>In the air.</summary>
        public bool IsLeaping => _phase == Phase.Strike;
        /// <summary>Staggered after a landing (punish window).</summary>
        public bool IsStunned => _phase == Phase.Recover;
        protected override float SpawnCooldown => settings.cooldown;

        /// <summary>Tests and tools: sets the attack without the builder.</summary>
        public void Configure(in Settings value) => settings = value;

        protected override bool WantsToStart(PlayerHealth knight)
        {
            float d = Planar(knight.transform.position, transform.position);
            return d >= settings.minRange && d <= settings.maxRange;
        }

        protected override bool Begin(PlayerHealth knight)
        {
            Vector3 origin = transform.position;
            Vector3 to = knight.transform.position - origin;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return false;
            _dir = to.normalized;
            _length = BossFight.FreeRun(origin, _dir, settings.leapDistance, BodyRadius, LaneStep);
            if (_length < settings.minLeap) return false;
            var pool = EnemyTelegraphs.Current;
            _line = pool != null ? pool.RentLine() : null;
            if (_line == null) return false;
            _line.ShowLine(origin, _dir, _length, settings.laneWidth);
            _hit = false;
            Enemy.PlayAction("Leap");
            return true;
        }

        protected override void Step(PlayerHealth knight, float dt)
        {
            switch (_phase)
            {
                case Phase.WindUp:
                    if (_line != null) _line.SetProgress(settings.crouch > 0f ? _time / settings.crouch : 1f);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(_dir), TurnSpeed * dt);
                    if (_time >= settings.crouch) TakeOff();
                    break;
                case Phase.Strike:
                    Fly(knight);
                    break;
                case Phase.Recover:
                    if (_time >= settings.stun) EndAttack(Mathf.Max(0f, settings.cooldown - settings.stun));
                    break;
            }
        }

        private void TakeOff()
        {
            if (_line != null) _line.Flash();
            _line = ReleaseLine(_line, hide: false); // its flash fades on its own
            _from = transform.position;
            _groundY = _from.y;
            transform.rotation = Quaternion.LookRotation(_dir);
            Enter(Phase.Strike);
            RaiseStruck();
        }

        /// <summary>Along the lane at constant speed with an arc; the swept part of the path hits the Knight once.</summary>
        private void Fly(PlayerHealth knight)
        {
            float t = Mathf.Clamp01(_time / settings.leapTime);
            Vector3 prev = transform.position;
            prev.y = _groundY;
            Vector3 next = _from + _dir * (_length * t);
            next.y = _groundY;
            if (!_hit && knight != null && !knight.IsDead)
            {
                float reach = settings.laneWidth * 0.5f + settings.hitPadding;
                if (SegmentDistance(knight.transform.position, prev, next) <= reach)
                {
                    _hit = true;
                    Hurt(knight, settings.damageShare);
                }
            }
            transform.position = new Vector3(next.x, _groundY + settings.arcHeight * Mathf.Sin(t * Mathf.PI), next.z);
            if (t < 1f) return;
            Enter(Phase.Recover);
            Enemy.PlayAction("Hit"); // stagger pose for the punish window
            Landed?.Invoke(this, next, _hit);
        }

        /// <summary>Planar distance from <paramref name="p"/> to the segment <paramref name="a"/>-<paramref name="b"/>.</summary>
        public static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float apx = p.x - a.x, apz = p.z - a.z;
            float len = abx * abx + abz * abz;
            float u = len > 1e-6f ? Mathf.Clamp01((apx * abx + apz * abz) / len) : 0f;
            float dx = apx - abx * u, dz = apz - abz * u;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public override void Cancel()
        {
            _line = ReleaseLine(_line, hide: true);
            if (_phase == Phase.Strike)
            {
                var p = transform.position;
                transform.position = new Vector3(p.x, _groundY, p.z); // never left hanging in the air
            }
            base.Cancel();
        }
    }
}
