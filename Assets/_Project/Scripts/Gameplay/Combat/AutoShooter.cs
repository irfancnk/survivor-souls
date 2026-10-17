using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Archero rule: while the player stands still, turn to the nearest enemy in range and fire one bolt
    /// every <see cref="fireInterval"/> seconds. Moving = no shooting (unless <see cref="fireWhileMoving"/>).
    /// Default numbers: enemy 150 HP, bolt 100 damage, so one bolt leaves it at a third and the second kills.
    /// The serialized numbers are base values: damage, fire rate, crit and the extra-bolt counts go through the player's
    /// <see cref="PlayerStats"/> (KAN-19 skills) when it has one. The bolt list of a volley comes from
    /// <see cref="VolleyBuilder"/> (KAN-83): Spread Shot adds angled bolts around the straight main bolt, Double Shot fires
    /// the volley again after a short delay (follow-up bolts wait in a fixed-size queue, no allocation per shot). Bolts come
    /// from a pool with an active cap. Every bolt hit is reported by <see cref="EnemyHit"/>.
    /// Reload (KAN-42) is a 0..1 fraction, not a countdown in seconds: each frame it grows by dt / <see cref="CurrentInterval"/>,
    /// so an attack speed change mid-reload keeps the fraction and only changes the rate (the reload ring does not jump), and
    /// the volley fires in the frame the fraction reaches 1. One volley (all spread and Double Shot bolts) = one reload.
    /// </summary>
    [RequireComponent(typeof(PlayerMover))]
    public sealed class AutoShooter : MonoBehaviour
    {
        [SerializeField] private Bolt boltPrefab;
        [Tooltip("Bolt spawn point (Crossbow_Muzzle). Falls back to chest height when empty.")]
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(0.05f)] private float fireInterval = 1f;
        [SerializeField, Min(0f)] private float damage = 100f;
        [SerializeField, Min(1f)] private float range = 10f;
        [SerializeField, Min(1f)] private float boltSpeed = 26f;
        [SerializeField] private bool fireWhileMoving;
        [SerializeField, Min(0f)] private float aimTurnSpeed = 720f; // degrees per second
        [Tooltip("0 = constant-rate turn at aimTurnSpeed (Knight). > 0 = eased turn (smooth damp, this many seconds), " +
                 "capped at aimTurnSpeed; set per hero by HeroLoadout (KAN-229: the Ranger no longer snaps round).")]
        [SerializeField, Min(0f)] private float aimTurnSmoothing;
        [SerializeField, Range(1f, 90f)] private float aimTolerance = 15f;
        [Tooltip("KAN-215: with fireWhileMoving, most degrees between the run direction and the target; beyond it the upper body cannot twist that far, so no shot.")]
        [SerializeField, Range(0f, 180f)] private float maxMovingTwist = 100f;
        [Header("Skill bases (PlayerStats modifies these)")]
        [SerializeField, Range(0f, 1f)] private float critChance;
        [SerializeField, Min(1f)] private float critMultiplier = 2f;
        [Tooltip("Spread Shot (KAN-83): degrees between two neighbouring bolts of the fan.")]
        [SerializeField, Range(0f, 45f)] private float spreadAngle = 15f;
        [Tooltip("Spread Shot: damage share of each angled bolt before skills (levels add to it).")]
        [SerializeField, Range(0f, 1f)] private float spreadDamage = 0.5f;
        [Tooltip("Double Shot (KAN-83): seconds between the bolts fired down the same line.")]
        [SerializeField, Range(0f, 0.3f)] private float doubleShotDelay = 0.09f;
        [Tooltip("Double Shot: sideways step (m) of each follow-up bolt, so the bolts do not overlap.")]
        [SerializeField, Range(0f, 0.5f)] private float doubleShotOffset = 0.18f;
        [Tooltip("Most bolts in one volley (spread x Double Shot), for performance and balance.")]
        [SerializeField, Range(1, 24)] private int maxBoltsPerVolley = VolleyBuilder.DefaultCap;
        [Tooltip("Most bolts in flight at once; a volley over it drops its extra bolts.")]
        [SerializeField, Range(8, 200)] private int maxActiveBolts = 72;
        [Tooltip("How far (m) a ricochet bolt looks for its next enemy.")]
        [SerializeField, Min(0.5f)] private float ricochetRange = 6.5f;
        [Tooltip("KAN-86: share of bolt damage lost each time a bolt pierces on (0.33 = next enemy takes 67 %).")]
        [SerializeField, Range(0f, 1f)] private float pierceFalloff = BoltShot.DefaultPierceFalloff;
        [Tooltip("KAN-86: share of bolt damage lost on each ricochet bounce (0.3 = next enemy takes 70 %).")]
        [SerializeField, Range(0f, 1f)] private float bounceFalloff = BoltShot.DefaultBounceFalloff;
        [Tooltip("KAN-200: burn damage per second of the coolest (Ember Bolts level 1) and hottest (level 4) blazing bolt look.")]
        [SerializeField] private Vector2 blazeDps = new Vector2(25f, 75f);
        [Header("Ember Boons (KAN-246)")]
        [Tooltip("Front Arrow +1: metres between two parallel forward lanes.")]
        [SerializeField, Range(0.1f, 1f)] private float frontLaneStep = 0.42f;
        [Tooltip("Rear Arrow +1: degrees between two rear bolts.")]
        [SerializeField, Range(0f, 45f)] private float rearSpread = 20f;
        [Tooltip("Bouncy Wall: damage share a bolt keeps after a wall rebound, before skills (levels add to it).")]
        [SerializeField, Range(0.1f, 1f)] private float wallKeep = 0.6f;

        /// <summary>Heat of the coolest blazing bolt (KAN-200): any burn at all already reads as fire.</summary>
        public const float MinBlazeHeat = 0.25f;
        /// <summary>KAN-246 cap: crit chance never goes above this, whatever the sources.</summary>
        public const float MaxCritChance = 0.75f;
        /// <summary>KAN-246 cap: the shortest time between two volleys (s), so attack speed keeps readable attack animations.</summary>
        public const float MinFireInterval = 0.22f;

        private const float MovingSpeed = 0.2f;
        private const float MaxCarry = 0.25f; // most reload overshoot one frame may carry into the next reload

        /// <summary>Raised once per volley, with its first bolts: muzzle position, aim direction. Recoil, muzzle FX, audio and the reload ring listen.</summary>
        public event Action<Vector3, Vector3> Fired;
        /// <summary>Raised for each Double Shot follow-up step (KAN-83): a lighter recoil and a quiet extra shot layer.</summary>
        public event Action<Vector3, Vector3> FollowUpFired;
        /// <summary>Raised in the frame the reload fraction reaches 1 (before a volley that fires in the same frame).</summary>
        public event Action ReloadCompleted;
        /// <summary>Raised for every enemy any of this shooter's bolts hits (on-hit skills listen).</summary>
        public event Action<BoltHit> EnemyHit;

        /// <summary>A follow-up bolt waiting for its Double Shot delay.</summary>
        private struct Pending
        {
            public float Time;
            public Vector3 Direction;
            public float Lateral;
            /// <summary>Point-blank pull-back of the bolt start (KAN-224), zero for a normal shot.</summary>
            public Vector3 Back;
            /// <summary>The shot of its own volley, damage share applied (a newer volley does not change it).</summary>
            public BoltShot Shot;
            public bool Lead;
        }

        private const int MaxPending = 48;

        private readonly Stack<Bolt> _pool = new Stack<Bolt>();
        private readonly VolleyArrow[] _volley = new VolleyArrow[24];
        private readonly Pending[] _pending = new Pending[MaxPending];
        private int _pendingCount;
        private int _active;
        private PlayerMover _mover;
        private PlayerStats _stats;
        private CrossbowAim _aim;
        private float _yawVelocity; // eased aim turn (deg/s)
        private Enemy _target;
        private Action<Bolt> _release;
        private Action<BoltHit> _onHit;
        private float _reload = 1f; // ready at spawn, like the old zero cooldown
        private bool _engaged;

        /// <summary>Base numbers before skills (KAN-75 power curve).</summary>
        public float BaseDamage => damage;
        public float BaseShotsPerSecond => 1f / fireInterval;
        public float BaseCritChance => critChance;
        public float BaseCritMultiplier => critMultiplier;
        /// <summary>Spread Shot angled bolt damage share before skills (power curve base, KAN-83).</summary>
        public float BaseSpreadDamage => spreadDamage;
        /// <summary>Damage share lost per pierce / per bounce (power curve base, KAN-86).</summary>
        public float BasePierceFalloff => pierceFalloff;
        public float BaseBounceFalloff => bounceFalloff;
        /// <summary>Damage share a bolt loses per pierce now: the base plus <see cref="StatId.PierceFalloff"/> (KAN-220 Piercing Bolts L7).</summary>
        public float CurrentPierceFalloff => Mathf.Clamp01(PlayerStats.Eval(_stats, StatId.PierceFalloff, pierceFalloff));
        /// <summary>Damage share a bolt loses per bounce now: the base plus <see cref="StatId.BounceFalloff"/> (KAN-220 Ricochet L7).</summary>
        public float CurrentBounceFalloff => Mathf.Clamp01(PlayerStats.Eval(_stats, StatId.BounceFalloff, bounceFalloff));
        /// <summary>Bolt damage after skills (burst and similar skills scale from it).</summary>
        public float CurrentDamage => PlayerStats.Eval(_stats, StatId.Damage, damage);
        /// <summary>Seconds between shots after attack speed skills (never below <see cref="MinFireInterval"/>, KAN-246).</summary>
        public float CurrentInterval =>
            Mathf.Max(MinFireInterval, 1f / Mathf.Max(0.05f, PlayerStats.Eval(_stats, StatId.AttackSpeed, 1f / fireInterval)));
        /// <summary>Crit chance after skills, capped at <see cref="MaxCritChance"/> (KAN-246).</summary>
        public float CurrentCritChance => Mathf.Min(MaxCritChance, Mathf.Clamp01(PlayerStats.Eval(_stats, StatId.CritChance, critChance)));
        /// <summary>Volleys fired this life of the shooter (Stolen Moment counts them, KAN-246).</summary>
        public int VolleysFired { get; private set; }
        /// <summary>Aim direction of the last volley (unit, flat-ish). Forward before the first shot.</summary>
        public Vector3 LastAimDirection { get; private set; } = Vector3.forward;
        /// <summary>
        /// Bolt look heat from the burn damage (KAN-200): 0 without Ember Bolts (<see cref="StatId.BurnDps"/> 0), else
        /// <see cref="MinBlazeHeat"/> at level 1 rising to 1 at level 4 (<see cref="blazeDps"/>).
        /// </summary>
        public float BoltHeat
        {
            get
            {
                float dps = PlayerStats.Eval(_stats, StatId.BurnDps, 0f);
                if (dps <= 0f) return 0f;
                return Mathf.Lerp(MinBlazeHeat, 1f, Mathf.InverseLerp(blazeDps.x, blazeDps.y, dps));
            }
        }
        /// <summary>Reload fraction 0..1 since the last volley; 1 = the next volley can fire.</summary>
        public float ReloadProgress => Mathf.Min(_reload, 1f);
        /// <summary>
        /// True when the last tick had a shot lined up: standing still (or <see cref="fireWhileMoving"/>) with an enemy in range.
        /// False = moving or no target; the ring then shows its calm ready state instead of a sweep that leads nowhere.
        /// </summary>
        public bool Engaged => _engaged;
        /// <summary>The enemy the last tick aimed at (null = none). The upper-body aim (KAN-215) follows it.</summary>
        public Enemy Target => _target;

        /// <summary>Bolts in flight now (active cap check).</summary>
        public int ActiveBolts => _active;
        /// <summary>Double Shot bolts still waiting for their delay.</summary>
        public int PendingBolts => _pendingCount;

        /// <summary>The volley the current stats fire (KAN-83): spread, Double Shot, damage shares, cap.</summary>
        public VolleySpec CurrentVolleySpec => new VolleySpec
        {
            Spread = _stats != null ? _stats.ModifyInt(StatId.Multishot, 0) : 0,
            SpreadStep = spreadAngle,
            SpreadDamage = Mathf.Clamp01(PlayerStats.Eval(_stats, StatId.SpreadDamage, spreadDamage)),
            Chain = _stats != null ? _stats.ModifyInt(StatId.DoubleShot, 0) : 0,
            ChainDelay = doubleShotDelay,
            ChainLateral = doubleShotOffset,
            ChainDamage = Mathf.Max(0f, PlayerStats.Eval(_stats, StatId.ChainDamage, 1f)),
            Cap = Mathf.Min(maxBoltsPerVolley, _volley.Length),
            Front = _stats != null ? _stats.ModifyInt(StatId.FrontLanes, 0) : 0,
            FrontStep = frontLaneStep,
            FrontDamage = Mathf.Clamp(PlayerStats.Eval(_stats, StatId.FrontDamage, 1f), 0f, 1.5f),
            Rear = _stats != null ? _stats.ModifyInt(StatId.RearArrows, 0) : 0,
            RearStep = rearSpread,
            RearDamage = Mathf.Max(0f, PlayerStats.Eval(_stats, StatId.RearDamage, 0f)),
        };

        /// <summary>Debug text of the current volley (Survivor Souls/Debug/Skills: Print Volley).</summary>
        public string DescribeVolley()
        {
            var spec = CurrentVolleySpec;
            return VolleyBuilder.Describe(spec, _volley, VolleyBuilder.Build(spec, _volley));
        }

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void SetBoltPrefab(Bolt prefab) => boltPrefab = prefab;
        /// <summary>The projectile prefab new bolts are made from (the hero's arrow or the Knight bolt, KAN-227).</summary>
        public Bolt BoltPrefab => boltPrefab;

        /// <summary>Wiring for code-built objects and tests: bolts leave from this socket (the scene builder sets it).</summary>
        public void SetMuzzle(Transform socket) => muzzle = socket;

        /// <summary>Aim turn: max rate (deg/s) and smoothing (s, 0 = constant rate). Per hero (KAN-229).</summary>
        public void SetAimTurn(float degreesPerSecond, float smoothing)
        {
            aimTurnSpeed = Mathf.Max(0f, degreesPerSecond);
            aimTurnSmoothing = Mathf.Max(0f, smoothing);
            _yawVelocity = 0f;
        }

        /// <summary>
        /// One frame of the eased aim turn (KAN-229): smooth damp of the yaw towards <paramref name="target"/>, capped at
        /// <paramref name="maxSpeed"/> deg/s, shortest way round. Public for tests.
        /// </summary>
        public static float TurnYaw(float yaw, float target, ref float velocity, float smoothing, float maxSpeed, float dt) =>
            Mathf.SmoothDampAngle(yaw, target, ref velocity, smoothing, maxSpeed, dt);

        public float AimTurnSpeed => aimTurnSpeed;
        public float AimTurnSmoothing => aimTurnSmoothing;

        /// <summary>Volley cap and active bolt budget (tests, perf runs).</summary>
        public void SetBoltLimits(int perVolley, int active)
        {
            maxBoltsPerVolley = Mathf.Clamp(perVolley, 1, _volley.Length);
            maxActiveBolts = Mathf.Max(1, active);
        }

        private void Awake()
        {
            _mover = GetComponent<PlayerMover>();
            _stats = GetComponent<PlayerStats>();
            _aim = GetComponent<CrossbowAim>();
            _release = b =>
            {
                _pool.Push(b);
                _active = Mathf.Max(0, _active - 1);
            };
            _onHit = hit => EnemyHit?.Invoke(hit);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One frame of reload, aim and fire. Public so tests can step exact frame times.</summary>
        public void Tick(float dt)
        {
            TickPending(dt);
            float before = _reload;
            // Capped above 1 only so a long wait cannot grow it without bound; the overshoot of one frame is what matters.
            _reload = Mathf.Min(before + dt / CurrentInterval, 2f);
            if (before < 1f && _reload >= 1f) ReloadCompleted?.Invoke();
            _engaged = false;
            _target = null;

            bool moving = _mover.Velocity.sqrMagnitude > MovingSpeed * MovingSpeed;
            if (moving && !fireWhileMoving) return;
            var target = Nearest();
            if (target == null) return;

            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            _engaged = true;
            _target = target;
            var look = Quaternion.LookRotation(to);
            if (moving) _yawVelocity = 0f; // the mover turns the body
            else if (aimTurnSmoothing > 0f)
                transform.rotation = Quaternion.Euler(0f,
                    TurnYaw(transform.eulerAngles.y, look.eulerAngles.y, ref _yawVelocity, aimTurnSmoothing, aimTurnSpeed, dt), 0f);
            else transform.rotation = Quaternion.RotateTowards(transform.rotation, look, aimTurnSpeed * dt);

            if (_reload < 1f) return;
            if (!moving && Quaternion.Angle(transform.rotation, look) > aimTolerance) return;
            if (moving && Vector3.Angle(transform.forward, to) > maxMovingTwist) return;
            Fire(target);
            // Back-to-back fire keeps this frame's overshoot, so the fire rate does not drift slower with the frame time.
            // A shot that waited (aim, target) starts a clean reload. The carry is capped so a long hitch cannot double-fire.
            _reload = before < 1f ? Mathf.Min(_reload - 1f, MaxCarry) : 0f;
        }

        private Enemy Nearest()
        {
            Enemy best = null;
            float bestSq = range * range;
            Vector3 p = transform.position;
            foreach (var e in Enemy.Active)
            {
                Vector3 d = e.transform.position - p;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        /// <summary>
        /// Shoots one volley at <paramref name="target"/> now (no cooldown check): the first bolts leave at once, Double Shot
        /// follow-ups after their delay (<see cref="Tick"/> launches them, from the muzzle where it is then, down the line
        /// aimed now). Public for tests and debug.
        /// </summary>
        public void Fire(Enemy target)
        {
            if (boltPrefab == null || target == null) return;
            // KAN-215: the upper body takes the last degrees (and this frame's root turn) so the crossbow line is the bolt line.
            // KAN-224: a point-blank target (PointBlankAim): the crossbow aims far down the forward line, the bolt starts further back.
            // KAN-226: blended toward the normal shot between 1.25 and 1.5 m; the bolt skips enemies on its path behind the root.
            Vector3 point = target.AimPoint;
            if (_aim != null && _aim.isActiveAndEnabled) _aim.AlignNow(PointBlankAim.AimPoint(transform.position, point));
            Vector3 muzzleAt = MuzzlePosition;
            Vector3 from = PointBlankAim.Start(transform.position, muzzleAt, point);
            Vector3 back = muzzleAt - from;
            Vector3 dir = (point - from).normalized;
            var shot = MakeShot();
            shot.Guard = PointBlankAim.Guard(transform.position, from, point); // KAN-226: no hit on an enemy behind the Knight
            int count = VolleyBuilder.Build(CurrentVolleySpec, _volley);
            for (int i = 0; i < count; i++)
            {
                var a = _volley[i];
                Vector3 d = a.Yaw == 0f ? dir : Quaternion.AngleAxis(a.Yaw, Vector3.up) * dir;
                var scaled = shot;
                scaled.Damage *= a.DamageScale;
                if (Mathf.Abs(a.Yaw) > 90f) scaled.Guard = 0f; // KAN-246 rear bolt: the point-blank guard is for the forward line
                if (a.Delay <= 0f)
                {
                    Launch(from, d, a.Lateral, scaled);
                }
                else if (_pendingCount < MaxPending)
                {
                    _pending[_pendingCount++] = new Pending
                    {
                        Time = a.Delay, Direction = d, Lateral = a.Lateral, Back = back, Shot = scaled, Lead = a.Lead,
                    };
                }
            }
            VolleysFired++;
            LastAimDirection = dir;
            Fired?.Invoke(muzzleAt, dir);
        }

        /// <summary>
        /// The bolt the current stats fire (KAN-246: one place for main volleys, echoes and summons): damage, crit (capped),
        /// pierce + ricochet inside the shared hit budget, wall rebounds, falloffs, heat. No point-blank guard.
        /// </summary>
        public BoltShot MakeShot()
        {
            int pierce = _stats != null ? _stats.ModifyInt(StatId.Pierce, 0) : 0;
            int ricochet = _stats != null ? _stats.ModifyInt(StatId.Ricochet, 0) : 0;
            ricochet = Mathf.Min(ricochet, BoltShot.MaxExtraHits);
            pierce = Mathf.Min(pierce, BoltShot.MaxExtraHits - ricochet);
            return new BoltShot
            {
                Damage = CurrentDamage,
                Speed = boltSpeed,
                Lifetime = range * 1.5f / boltSpeed,
                CritChance = CurrentCritChance,
                CritMultiplier = Mathf.Max(1f, PlayerStats.Eval(_stats, StatId.CritDamage, critMultiplier)),
                Pierce = pierce,
                Ricochet = ricochet,
                RicochetRange = ricochetRange,
                PierceFalloff = CurrentPierceFalloff,
                BounceFalloff = CurrentBounceFalloff,
                Heat = BoltHeat,
                WallBounces = _stats != null ? _stats.ModifyInt(StatId.WallBounces, 0) : 0,
                WallKeep = Mathf.Clamp01(PlayerStats.Eval(_stats, StatId.WallDamage, wallKeep)),
            };
        }

        /// <summary>
        /// Secondary bolt (KAN-246, <see cref="ProcGuard"/>): a pooled bolt from <paramref name="from"/> along
        /// <paramref name="direction"/> that hits like a normal one but is never reported by <see cref="EnemyHit"/>, so it
        /// sets off no weapon-hit skill (echoes, spectral archers). Counts against the active bolt budget. False when over it.
        /// </summary>
        public bool LaunchSecondary(Vector3 from, Vector3 direction, in BoltShot shot)
        {
            if (boltPrefab == null || _active >= maxActiveBolts || direction.sqrMagnitude < 1e-6f) return false;
            var bolt = _pool.Count > 0 ? _pool.Pop() : Instantiate(boltPrefab);
            _active++;
            bolt.Launch(from, direction.normalized, shot, _release);
            return true;
        }

        /// <summary>
        /// Stolen Moment echo (KAN-246): the current firing pattern once (spread, forward lanes, rear bolts; no Multishot
        /// repeat) from <paramref name="origin"/> along <paramref name="direction"/> at <paramref name="damageShare"/> of the
        /// damage, as secondary bolts. Returns the bolts launched.
        /// </summary>
        public int FireEcho(Vector3 origin, Vector3 direction, float damageShare)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return 0;
            direction.Normalize();
            var spec = CurrentVolleySpec;
            spec.Chain = 0;
            var shot = MakeShot();
            shot.Damage *= Mathf.Max(0f, damageShare);
            int count = VolleyBuilder.Build(spec, _volley);
            int launched = 0;
            for (int i = 0; i < count; i++)
            {
                var a = _volley[i];
                Vector3 d = a.Yaw == 0f ? direction : Quaternion.AngleAxis(a.Yaw, Vector3.up) * direction;
                var scaled = shot;
                scaled.Damage *= a.DamageScale;
                Vector3 from = origin;
                if (a.Lateral != 0f) from += Vector3.Cross(Vector3.up, d).normalized * a.Lateral;
                if (LaunchSecondary(from, d, scaled)) launched++;
            }
            return launched;
        }

        private Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.1f;

        /// <summary>Counts down the Double Shot queue and launches what is due (in fire order; swap-remove keeps it cheap).</summary>
        private void TickPending(float dt)
        {
            if (_pendingCount == 0) return;
            Vector3 from = MuzzlePosition;
            for (int i = 0; i < _pendingCount; i++)
            {
                _pending[i].Time -= dt;
                if (_pending[i].Time > 0f) continue;
                var p = _pending[i];
                Launch(from - p.Back, p.Direction, p.Lateral, p.Shot);
                if (p.Lead) FollowUpFired?.Invoke(from, p.Direction);
                _pending[i] = _pending[--_pendingCount];
                i--;
            }
        }

        /// <summary>Drops queued Double Shot bolts (run reset, death).</summary>
        public void CancelPending() => _pendingCount = 0;

        private void Launch(Vector3 from, Vector3 dir, float lateral, in BoltShot shot)
        {
            if (_active >= maxActiveBolts) return; // over the budget: this bolt is skipped, the volley still counts
            if (lateral != 0f) from += Vector3.Cross(Vector3.up, dir).normalized * lateral;
            var bolt = _pool.Count > 0 ? _pool.Pop() : Instantiate(boltPrefab);
            _active++;
            bolt.Launch(from, dir, shot, _release, _onHit);
        }

        private void OnDisable() => CancelPending();
    }
}
