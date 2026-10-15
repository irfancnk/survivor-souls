using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Ranged zombie attack (KAN-105): Bone Archer arrows, Pyre Thrower fire pots, Bile Spitter acid globs. Sits next to
    /// <see cref="Enemy"/> (which keeps the distance: hold band) and uses the scene's shared pools: shots
    /// (<see cref="EnemyProjectiles"/>), warnings (<see cref="EnemyTelegraphs"/>) and hazards (<see cref="GroundHazards"/>).
    /// <list type="number">
    /// <item>Idle: when the cooldown is over, the enemy holds inside its band and (straight shots only) the line of fire is
    /// clear, the wind-up starts. No free warning = wait (an attack never fires without its warning).</item>
    /// <item>Wind-up: the enemy stops (<see cref="Enemy.Halted"/>: plays its Attack clip) and turns to the aim. Straight: an aim
    /// lane per arrow follows the Knight, then locks for the last <see cref="Settings.lockShare"/> of the wind-up. Lobbed: the
    /// landing circles appear at once at the lead-aimed points and stay there.</item>
    /// <item>Release: the shots leave along the lanes / to the circles; the lanes flash. A short recover, then the cooldown.</item>
    /// </list>
    /// The volley pattern cycles (<see cref="Settings.pattern"/>: 1, 1, 3 = every third volley is a fan / spread). A kill or
    /// a dead Knight cancels the wind-up and hides its warnings; shots already in the air fly on.
    /// Line-of-fire check (time sliced): a pillar between shooter and Knight sets <see cref="Enemy.NeedsClearShot"/>.
    /// </summary>
    [RequireComponent(typeof(Enemy))]
    public sealed class RangedAttack : MonoBehaviour
    {
        public enum Phase : byte { Idle, WindUp, Recover }

        /// <summary>Every ranged attack shows its warning at least this long before anything can hurt (boss rule).</summary>
        public const float MinTelegraph = 0.6f;
        /// <summary>Seconds the enemy stays halted after a release (the throw / shot clip finishes).</summary>
        public const float RecoverTime = 0.35f;
        /// <summary>Seconds between line-of-fire checks of one enemy (staggered).</summary>
        public const float SightInterval = 0.25f;
        /// <summary>Seconds between aim updates of a tracking aim lane.</summary>
        public const float AimInterval = 0.1f;
        public const int MaxShotsPerVolley = 5;
        private const float SightStep = 0.6f;
        private const float SightRadius = 0.15f;
        private const float LaneStep = 0.4f;
        private const float TurnSpeed = 720f;
        private const float MuzzleForward = 0.4f;

        [Serializable]
        public struct Settings
        {
            public ShotKind kind;
            [Tooltip("Lobbed (arc to a ground point + landing circle) or straight (aim lane, flies at chest height).")]
            public bool lobbed;
            [Tooltip("Seconds of wind-up (aim / throw) before the release.")]
            [Min(0f)] public float windUp;
            [Tooltip("Straight shots: share of the wind-up at its end in which the aim no longer follows the Knight.")]
            [Range(0f, 1f)] public float lockShare;
            [Tooltip("Seconds from one release to the next wind-up.")]
            [Min(0.1f)] public float cooldown;
            [Tooltip("Shots per volley, cycling (1, 1, 3 = a fan / spread every third volley). Empty = 1.")]
            public int[] pattern;
            [Tooltip("Straight: fan angle (deg) of a multi-shot volley. Lobbed: spacing (m) of the landing points.")]
            [Min(0f)] public float spread;
            [Tooltip("Shot speed (m/s). Lobbed with flightTime 0: the flight time is the distance / this speed.")]
            [Min(0f)] public float speed;
            [Tooltip("Straight: flight distance (m). Lobbed: longest throw (m).")]
            [Min(0f)] public float range;
            [Tooltip("Lobbed: fixed flight time (s); 0 = from the speed.")]
            [Min(0f)] public float flightTime;
            [Tooltip("Lobbed: apex height (m) over the straight line.")]
            [Min(0f)] public float arcHeight;
            [Tooltip("Lobbed: a low arc that walls and pillars stop.")]
            public bool blockable;
            [Tooltip("Lobbed: seconds of the Knight's velocity added to the aim point (lead aim). 0 = aim at the Knight.")]
            [Min(0f)] public float leadTime;
            [Tooltip("Lobbed: the lead never moves the aim point more than this (m).")]
            [Min(0f)] public float maxLead;
            [Tooltip("Damage as a share of the Knight's max health (arrow hit / impact inside the circle).")]
            [Range(0f, 1f)] public float damageShare;
            [Tooltip("Straight: hit radius (m) round the Knight. Lobbed: impact (= landing circle) radius.")]
            [Min(0.05f)] public float hitRadius;
            [Tooltip("Straight: aim lane width (m).")]
            [Min(0.05f)] public float laneWidth;
            [Tooltip("Hazard left where a lobbed shot lands (radius 0 = none).")]
            public HazardSpec hazard;
            [Tooltip("Most hazards one enemy keeps on the ground (0 = no limit).")]
            [Min(0)] public int maxHazards;
            [Tooltip("Straight shots: walk in when a pillar or wall blocks the line of fire.")]
            public bool needsLineOfFire;

            /// <summary>Shots in volley <paramref name="index"/> (0-based) of the pattern.</summary>
            public int ShotsIn(int index) =>
                pattern == null || pattern.Length == 0 ? 1 : Mathf.Clamp(pattern[Mathf.Abs(index) % pattern.Length], 1, MaxShotsPerVolley);

            /// <summary>Seconds a warning shows before the first damage can land (wind-up + flight for lobbed shots at <paramref name="distance"/> m).</summary>
            public float WarningTime(float distance) => windUp + (lobbed ? FlightTimeFor(distance) : 0f);

            /// <summary>Flight time of a lob over <paramref name="distance"/> m.</summary>
            public float FlightTimeFor(float distance) =>
                flightTime > 0f ? flightTime : Mathf.Max(0.2f, distance / Mathf.Max(0.1f, speed));
        }

        [SerializeField] private Settings settings;

        private readonly Vector3[] _targets = new Vector3[MaxShotsPerVolley];
        private readonly GroundTelegraph[] _circles = new GroundTelegraph[MaxShotsPerVolley];
        private Enemy _enemy;
        private LineTelegraph _line;
        private Phase _phase;
        private float _time;
        private float _cooldown;
        private float _sight;
        private float _aimTimer;
        private int _volleyIndex;
        private int _shots;
        private Vector3 _aimDir = Vector3.forward;
        private float _flight;
        private int _ownerId; // hazard owner (max puddles per enemy)
        private static int _nextOwnerId;

        /// <summary>A volley left: shooter, shot count.</summary>
        public static event Action<RangedAttack, int> Fired;

        public Settings Config => settings;
        public Phase CurrentPhase => _phase;
        public Enemy Enemy => _enemy != null ? _enemy : _enemy = GetComponent<Enemy>();
        /// <summary>Volleys released since spawn.</summary>
        public int Volleys => _volleyIndex;
        /// <summary>Shots in the current / next volley.</summary>
        public int ShotsThisVolley => settings.ShotsIn(_volleyIndex);
        /// <summary>Seconds into the current phase.</summary>
        public float PhaseTime => _time;
        public float Cooldown => _cooldown;
        /// <summary>The aim lane of the current wind-up (straight shots), else null.</summary>
        public LineTelegraph AimLane => _line;
        /// <summary>Landing point <paramref name="i"/> of the current lobbed wind-up.</summary>
        public Vector3 TargetOf(int i) => _targets[Mathf.Clamp(i, 0, MaxShotsPerVolley - 1)];
        /// <summary>Landing circle <paramref name="i"/> of the current lobbed wind-up (null when none).</summary>
        public GroundTelegraph CircleOf(int i) => _circles[Mathf.Clamp(i, 0, MaxShotsPerVolley - 1)];

        /// <summary>Tests and tools: sets the attack without the builder.</summary>
        public void Configure(in Settings value) => settings = value;

        private void Awake()
        {
            _enemy = GetComponent<Enemy>();
            _ownerId = ++_nextOwnerId;
        }

        private void OnEnable()
        {
            _phase = Phase.Idle;
            _time = 0f;
            _volleyIndex = 0;
            _cooldown = settings.cooldown * UnityEngine.Random.Range(0.3f, 0.7f); // a group never fires in sync
            _sight = UnityEngine.Random.value * SightInterval;
        }

        private void OnDisable() => Cancel();

        /// <summary>Starts the next volley now (debug, tests). False while busy or when no warning is free.</summary>
        public bool ForceAttack()
        {
            if (_phase != Phase.Idle || !Enemy.IsAlive) return false;
            var knight = Knight();
            return knight != null && BeginWindUp(knight);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            var enemy = Enemy;
            var knight = Knight();
            if (!enemy.IsAlive || knight == null || knight.IsDead)
            {
                Cancel();
                return;
            }
            if (_cooldown > 0f) _cooldown -= dt;
            CheckSight(knight, dt);

            switch (_phase)
            {
                case Phase.Idle:
                    if (_cooldown <= 0f && enemy.IsHolding && !enemy.NeedsClearShot) BeginWindUp(knight);
                    break;
                case Phase.WindUp:
                    _time += dt;
                    WindUp(knight, dt);
                    if (_time >= settings.windUp) Release();
                    break;
                case Phase.Recover:
                    _time += dt;
                    if (_time >= RecoverTime)
                    {
                        _phase = Phase.Idle;
                        enemy.Halted = false;
                    }
                    break;
            }
        }

        private static PlayerHealth Knight()
        {
            var shots = EnemyProjectiles.Current;
            return shots != null ? shots.Player : null;
        }

        /// <summary>Straight shooters: is the way from the chest to the Knight free of pillars and walls? Time sliced.</summary>
        private void CheckSight(PlayerHealth knight, float dt)
        {
            if (!settings.needsLineOfFire) return;
            _sight -= dt;
            if (_sight > 0f) return;
            _sight += SightInterval;
            Enemy.NeedsClearShot = !ClearShot(transform.position, knight.transform.position);
        }

        /// <summary>True when nothing blocks a shot from <paramref name="from"/> to <paramref name="to"/> (planar, <see cref="Enemy.Walkability"/>).</summary>
        public static bool ClearShot(Vector3 from, Vector3 to)
        {
            if (Enemy.Walkability == null) return true;
            Vector3 d = to - from;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < SightStep * 2f) return true;
            return BossFight.FreeRun(from, d / dist, dist - SightStep, SightRadius, SightStep) >= dist - SightStep * 2f;
        }

        private bool BeginWindUp(PlayerHealth knight)
        {
            var warnings = EnemyTelegraphs.Current;
            if (warnings == null) return false;
            _shots = settings.ShotsIn(_volleyIndex);
            Vector3 origin = transform.position;
            Vector3 to = knight.transform.position - origin;
            to.y = 0f;
            _aimDir = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;

            if (settings.lobbed)
            {
                if (warnings.FreeCircles() < _shots)
                {
                    _cooldown = 0.2f; // all warnings busy: try again soon
                    return false;
                }
                PlaceTargets(knight, origin);
                for (int i = 0; i < _shots; i++)
                {
                    _circles[i] = warnings.RentCircle();
                    _circles[i].Show(_targets[i], settings.hitRadius);
                }
            }
            else
            {
                _line = warnings.RentLine();
                if (_line == null)
                {
                    _cooldown = 0.2f;
                    return false;
                }
                ShowLanes(origin);
            }
            _phase = Phase.WindUp;
            _time = 0f;
            _aimTimer = AimInterval;
            Enemy.Halted = true;
            return true;
        }

        /// <summary>
        /// Lobbed landing points: the Knight plus its velocity x <see cref="Settings.leadTime"/> (capped at
        /// <see cref="Settings.maxLead"/>), clamped to the throw range; a spread volley lines up extra points across the throw.
        /// </summary>
        private void PlaceTargets(PlayerHealth knight, Vector3 origin)
        {
            Vector3 aim = knight.transform.position;
            if (settings.leadTime > 0f && knight.TryGetComponent<PlayerMover>(out var mover))
            {
                Vector3 lead = mover.Velocity * settings.leadTime;
                lead.y = 0f;
                if (lead.magnitude > settings.maxLead) lead = lead.normalized * settings.maxLead;
                aim += lead;
            }
            Vector3 d = aim - origin;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > settings.range && dist > 0.01f) aim = origin + d * (settings.range / dist) + Vector3.up * (aim.y - origin.y);
            Vector3 dir = dist > 0.01f ? d / dist : _aimDir;
            _aimDir = dir;
            Vector3 across = new Vector3(dir.z, 0f, -dir.x);
            for (int i = 0; i < _shots; i++)
                _targets[i] = aim + across * (settings.spread * (i - (_shots - 1) * 0.5f));
            float far = 0f;
            for (int i = 0; i < _shots; i++)
            {
                Vector3 o = _targets[i] - origin;
                o.y = 0f;
                far = Mathf.Max(far, o.magnitude);
            }
            _flight = settings.FlightTimeFor(far);
        }

        /// <summary>Aim lanes along <see cref="_aimDir"/>, each cut where a pillar or wall stops the arrow.</summary>
        private void ShowLanes(Vector3 origin)
        {
            _line.ShowFan(origin, _aimDir, _shots, _shots > 1 ? settings.spread : 0f, settings.range, settings.laneWidth);
            for (int i = 0; i < _line.LaneCount; i++)
                _line.SetLaneLength(i, BossFight.FreeRun(origin, _line.LaneDirection(i), settings.range, SightRadius, LaneStep));
        }

        private void WindUp(PlayerHealth knight, float dt)
        {
            Vector3 origin = transform.position;
            if (settings.lobbed)
            {
                float total = settings.windUp + _flight;
                float p = total > 0f ? _time / total : 1f;
                for (int i = 0; i < _shots; i++)
                    if (_circles[i] != null) _circles[i].SetProgress(p);
            }
            else if (_line != null)
            {
                bool tracking = _time < settings.windUp * (1f - settings.lockShare);
                _aimTimer -= dt;
                if (tracking && _aimTimer <= 0f)
                {
                    _aimTimer += AimInterval;
                    Vector3 to = knight.transform.position - origin;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.0001f) _aimDir = to.normalized;
                    ShowLanes(origin);
                }
                _line.SetProgress(settings.windUp > 0f ? _time / settings.windUp : 1f);
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(_aimDir), TurnSpeed * dt);
        }

        private void Release()
        {
            var shots = EnemyProjectiles.Current;
            var knight = shots != null ? shots.Player : null;
            Vector3 origin = transform.position;
            float damage = knight != null ? settings.damageShare * knight.Max : 0f;
            if (shots != null)
            {
                int volley = shots.NextVolley();
                if (settings.lobbed)
                {
                    float total = settings.windUp + _flight;
                    float start = total > 0f ? settings.windUp / total : 0f;
                    for (int i = 0; i < _shots; i++)
                    {
                        bool fired = shots.FireLobbed(volley, settings.kind, origin + _aimDir * MuzzleForward, _targets[i], _flight,
                            settings.arcHeight, settings.hitRadius, damage, settings.hazard, _ownerId, settings.maxHazards,
                            settings.blockable, _circles[i], start);
                        if (!fired) ReleaseCircle(i, hide: true);
                        _circles[i] = null; // the shot owns it now
                    }
                }
                else if (_line != null)
                {
                    for (int i = 0; i < _line.LaneCount; i++)
                        shots.FireStraight(volley, settings.kind, origin + _aimDir * MuzzleForward, _line.LaneDirection(i), settings.speed,
                            settings.range, damage, settings.hitRadius);
                    _line.Flash();
                }
            }
            ReleaseLine(hide: false);
            Fired?.Invoke(this, _shots);
            _volleyIndex++;
            _cooldown = settings.cooldown;
            _phase = Phase.Recover;
            _time = 0f;
        }

        /// <summary>Ends a wind-up: hides its warnings, frees the enemy. Shots in the air fly on.</summary>
        public void Cancel()
        {
            ReleaseLine(hide: true);
            for (int i = 0; i < MaxShotsPerVolley; i++) ReleaseCircle(i, hide: true);
            if (_phase != Phase.Idle && _enemy != null && _enemy.IsAlive) _enemy.Halted = false;
            _phase = Phase.Idle;
            _time = 0f;
        }

        private void ReleaseLine(bool hide)
        {
            if (_line == null) return;
            if (hide) _line.Hide();
            var warnings = EnemyTelegraphs.Current;
            if (warnings != null) warnings.Release(_line);
            _line = null;
        }

        private void ReleaseCircle(int i, bool hide)
        {
            var c = _circles[i];
            if (c == null) return;
            if (hide) c.Hide();
            var warnings = EnemyTelegraphs.Current;
            if (warnings != null) warnings.Release(c);
            _circles[i] = null;
        }
    }
}
