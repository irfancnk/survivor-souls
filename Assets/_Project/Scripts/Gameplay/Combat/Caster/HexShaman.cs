using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Hexbound Shaman (KAN-107, Docs/ENEMY_ROSTER.md 3.11): a support caster. Sits next to <see cref="Enemy"/> (which keeps
    /// the distance: hold band 8-11 m, capped to the screen like the ranged zombies) and uses the scene's shared pools: shots
    /// (<see cref="EnemyProjectiles"/>), warnings (<see cref="EnemyTelegraphs"/>), spawner (<see cref="EnemySpawner.Current"/>)
    /// and puffs (<see cref="HexFx"/>).
    /// <list type="bullet">
    /// <item><b>Orb ring</b> (cast A, every <see cref="Settings.orbCooldown"/> s): a violet circle round the body warns
    /// <see cref="Settings.orbWindUp"/> s, then <see cref="Settings.orbCount"/> slow soul orbs fly outwards, evenly spaced
    /// with one aimed at the Knight (the gaps sit beside it: a side step dodges). One ring hurts the Knight once at most.</item>
    /// <item><b>Hex or raise</b> (cast B, every <see cref="Settings.castCooldown"/> s): with at least
    /// <see cref="Settings.hexMinTargets"/> other zombies within <see cref="Settings.hexRadius"/> m a violet circle of that
    /// size warns, then every zombie inside gets <see cref="Enemy.ApplyHex"/> (+30 % speed, violet tint, 5 s). Else it
    /// raises <see cref="Settings.raiseCount"/> Graveyard Laborers on a ring round itself (a circle warns at each point), never
    /// more than <see cref="Settings.raiseMax"/> of its own alive. No target and nothing to raise = try again in 1 s.</item>
    /// <item><b>Blink</b>: when the Knight comes within <see cref="Settings.blinkTrigger"/> m (cooldown
    /// <see cref="Settings.blinkCooldown"/> s) it drops any cast, shrinks away in a violet puff and reappears
    /// <see cref="Settings.blinkDistance"/> m from the Knight (away from it first, on screen first, walkable), then grows back.
    /// A blink never hurts, so it has no warning.</item>
    /// </list>
    /// A kill or a dead Knight cancels a cast and hides its warnings; orbs in the air fly on. <see cref="HexFx"/> draws a
    /// small violet marker over every living Shaman (priority target). No allocation per frame.
    /// </summary>
    [RequireComponent(typeof(Enemy))]
    public sealed class HexShaman : MonoBehaviour
    {
        public enum Cast : byte { None, Orbs, Hex, Raise }
        public enum Phase : byte { Idle, WindUp, Recover, Vanish, Appear }

        /// <summary>Every warning shows at least this long before damage can land (boss rule, BossData.MinTelegraph).</summary>
        public const float MinTelegraph = 0.6f;
        public const int MaxOrbs = 16;
        public const int MaxRaised = 8;
        /// <summary>Seconds between view checks of one Shaman (staggered, like <see cref="RangedAttack.SightInterval"/>).</summary>
        public const float SightInterval = 0.25f;
        /// <summary>Seconds before the next cast-B try when nothing was in reach.</summary>
        public const float RetryDelay = 1f;
        private const float TurnSpeed = 540f;
        private const float RaiseCircleRadius = 0.9f;
        private const float RaiseClearRadius = 0.5f;
        private const float BlinkClearRadius = 0.5f;
        private const float MinVisible = 0.05f;

        /// <summary>Warning colours (violet), shared by every cast.</summary>
        public static readonly Color RingTint = new Color(0.62f, 0.25f, 0.95f, 1f);
        public static readonly Color FillTint = new Color(0.45f, 0.15f, 0.75f, 0.6f);
        public static readonly Color FlashTint = new Color(0.85f, 0.6f, 1f, 0.85f);

        [Serializable]
        public struct Settings
        {
            [Header("Cast A: orb ring")]
            [Tooltip("Seconds the circle round the body warns before the ring leaves.")]
            [Min(MinTelegraph)] public float orbWindUp;
            [Tooltip("Orbs per ring (evenly spaced, one aimed at the Knight).")]
            [Range(3, MaxOrbs)] public int orbCount;
            [Min(0.5f)] public float orbSpeed;
            [Tooltip("Metres an orb flies.")]
            [Min(1f)] public float orbRange;
            [Tooltip("Damage of a ring that hits, share of the Knight's max health (one hit per ring).")]
            [Range(0f, 1f)] public float orbDamageShare;
            [Tooltip("Hit radius (m) of an orb round the Knight.")]
            [Min(0.05f)] public float orbHitRadius;
            [Tooltip("Radius (m) of the warning circle round the body.")]
            [Min(0.3f)] public float orbWarnRadius;
            [Tooltip("Seconds from one ring to the next wind-up.")]
            [Min(0.5f)] public float orbCooldown;

            [Header("Cast B: hex or raise")]
            [Tooltip("Seconds the hex circle / raise circles warn.")]
            [Min(MinTelegraph)] public float castWindUp;
            [Tooltip("Hex reach (m) round the Shaman = the warning circle.")]
            [Min(0.5f)] public float hexRadius;
            [Tooltip("Speed factor of hexed zombies.")]
            [Min(1f)] public float hexBoost;
            [Tooltip("Seconds a hex lasts.")]
            [Min(0.1f)] public float hexDuration;
            [Tooltip("Hex when at least this many zombies are in reach, else raise.")]
            [Min(1)] public int hexMinTargets;
            [Tooltip("Laborers raised per cast.")]
            [Range(0, 4)] public int raiseCount;
            [Tooltip("Most raised zombies of one Shaman alive at once.")]
            [Range(0, MaxRaised)] public int raiseMax;
            [Tooltip("Radius (m) of the ring round the Shaman the dead rise on.")]
            [Min(0.5f)] public float raiseRing;
            [Tooltip("Seconds from one cast B to the next.")]
            [Min(0.5f)] public float castCooldown;

            [Header("Blink")]
            [Tooltip("The Knight within this many metres = blink.")]
            [Min(0f)] public float blinkTrigger;
            [Tooltip("Metres from the Knight the Shaman reappears.")]
            [Min(1f)] public float blinkDistance;
            [Min(0.1f)] public float blinkCooldown;
            [Tooltip("Seconds to shrink away (and again to grow back).")]
            [Min(0.05f)] public float blinkTime;

            [Tooltip("Seconds the Shaman stays still after a cast.")]
            [Min(0f)] public float recover;
        }

        /// <summary>Living Shamans (marker drawing, tests). Kept by OnEnable / OnDisable.</summary>
        public static readonly List<HexShaman> Active = new List<HexShaman>();

        /// <summary>A ring left: caster, orbs fired.</summary>
        public static event Action<HexShaman, int> OrbsFired;
        /// <summary>A hex landed: caster, zombies hexed.</summary>
        public static event Action<HexShaman, int> Hexed;
        /// <summary>The dead rose: caster, zombies raised.</summary>
        public static event Action<HexShaman, int> Raised;
        /// <summary>A blink ended: caster, from, to.</summary>
        public static event Action<HexShaman, Vector3, Vector3> Blinked;

        [SerializeField] private Settings settings;
        [Tooltip("Spawner type raised by cast B (Graveyard Laborer).")]
        [SerializeField] private Enemy raiseType;

        private readonly GroundTelegraph[] _circles = new GroundTelegraph[4];
        private readonly Vector3[] _raiseAt = new Vector3[4];
        private readonly Enemy[] _raised = new Enemy[MaxRaised];
        private Enemy _enemy;
        private Phase _phase;
        private Cast _cast;
        private float _time;
        private float _orbCooldown;
        private float _castCooldown;
        private float _blinkCooldown;
        private float _sight;
        private int _planned;
        private Vector3 _blinkFrom, _blinkTo;
        private Vector3 _scale = Vector3.one;
        private bool _shrunk;
        private float _visible = 1f;

        public Settings Config => settings;
        public Enemy RaiseType => raiseType;
        public Enemy Enemy => _enemy != null ? _enemy : _enemy = GetComponent<Enemy>();
        public Phase CurrentPhase => _phase;
        /// <summary>The cast winding up / recovering (None while idle or blinking).</summary>
        public Cast CurrentCast => _cast;
        /// <summary>Seconds into the current phase.</summary>
        public float PhaseTime => _time;
        public float OrbCooldown => _orbCooldown;
        public float CastCooldown => _castCooldown;
        public float BlinkCooldown => _blinkCooldown;
        /// <summary>Warning circle <paramref name="i"/> of the current wind-up (0 = orb / hex circle; raise: one per point).</summary>
        public GroundTelegraph CircleOf(int i) => _circles[Mathf.Clamp(i, 0, _circles.Length - 1)];
        /// <summary>Raise points planned in the current wind-up.</summary>
        public int PlannedRaises => _cast == Cast.Raise ? _planned : 0;
        public Vector3 RaisePoint(int i) => _raiseAt[Mathf.Clamp(i, 0, _raiseAt.Length - 1)];
        /// <summary>Where the current / last blink goes.</summary>
        public Vector3 BlinkTarget => _blinkTo;
        /// <summary>1 = full size; less while blinking (marker height).</summary>
        public float VisibleShare => _visible;
        /// <summary>Over the head: where the priority marker sits.</summary>
        public Vector3 MarkerPoint => transform.position + Vector3.up * (Enemy.Height * _visible + 0.45f);

        /// <summary>Tests and tools: sets the numbers without the builder.</summary>
        public void Configure(in Settings value, Enemy raise = null)
        {
            settings = value;
            if (raise != null) raiseType = raise;
        }

        private void Awake() => _enemy = GetComponent<Enemy>();

        private void OnEnable()
        {
            _phase = Phase.Idle;
            _cast = Cast.None;
            _time = 0f;
            _orbCooldown = settings.orbCooldown * UnityEngine.Random.Range(0.3f, 0.7f); // a group never casts in sync
            _castCooldown = settings.castCooldown * UnityEngine.Random.Range(0.4f, 0.8f);
            _blinkCooldown = 0f;
            _sight = UnityEngine.Random.value * SightInterval;
            _shrunk = false;
            _visible = 1f;
            Array.Clear(_raised, 0, _raised.Length);
            Enemy.Damaged += OnEnemyDamaged;
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Cancel();
            Enemy.Damaged -= OnEnemyDamaged;
            Active.Remove(this);
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
            if (enemy.IsFrozenSolid) return; // KAN-91: a frozen Shaman's casts pause
            if (_orbCooldown > 0f) _orbCooldown -= dt;
            if (_castCooldown > 0f) _castCooldown -= dt;
            if (_blinkCooldown > 0f) _blinkCooldown -= dt;
            CheckSight(knight, dt);

            if (_phase <= Phase.Recover && _blinkCooldown <= 0f
                && SpecialAttack.Planar(knight.transform.position, transform.position) <= settings.blinkTrigger)
                TryBlink(knight);

            switch (_phase)
            {
                case Phase.Idle:
                    if (!enemy.IsHolding) break;
                    if (_castCooldown <= 0f) BeginCast(knight, ChooseCast());
                    else if (_orbCooldown <= 0f) BeginCast(knight, Cast.Orbs);
                    break;
                case Phase.WindUp:
                    _time += dt;
                    WindUp(knight, dt);
                    if (_time >= WindUpTime) Release(knight);
                    break;
                case Phase.Recover:
                    _time += dt;
                    if (_time >= settings.recover) Finish();
                    break;
                case Phase.Vanish:
                    _time += dt;
                    SetVisible(1f - _time / settings.blinkTime);
                    if (_time >= settings.blinkTime) Reappear(knight);
                    break;
                case Phase.Appear:
                    _time += dt;
                    SetVisible(_time / settings.blinkTime);
                    if (_time >= settings.blinkTime) Finish();
                    break;
            }
        }

        private float WindUpTime => _cast == Cast.Orbs ? settings.orbWindUp : settings.castWindUp;

        private static PlayerHealth Knight()
        {
            var shots = EnemyProjectiles.Current;
            return shots != null ? shots.Player : null;
        }

        /// <summary>Time sliced: caps the hold band to the screen in this Shaman's direction (casts from on screen).</summary>
        private void CheckSight(PlayerHealth knight, float dt)
        {
            _sight -= dt;
            if (_sight > 0f) return;
            _sight += SightInterval;
            Vector3 k = knight.transform.position;
            Enemy.ViewReach = RangedAttack.ViewReach(k, transform.position - k);
        }

        // ------------------------------------------------------------ Casts

        /// <summary>Starts <paramref name="cast"/> now (debug, dev screenshots, tests), skipping its cooldown. False while busy or when it cannot start.</summary>
        public bool ForceCast(Cast cast)
        {
            if (_phase != Phase.Idle || !Enemy.IsAlive) return false;
            var knight = Knight();
            return knight != null && !knight.IsDead && BeginCast(knight, cast == Cast.Hex && HexTargets() == 0 ? ChooseCast() : cast);
        }

        /// <summary>Cast B: hex with enough zombies in reach, else raise (room and a type), else hex what is there, else none.</summary>
        public Cast ChooseCast()
        {
            int targets = HexTargets();
            if (targets >= settings.hexMinTargets) return Cast.Hex;
            if (CanRaise) return Cast.Raise;
            return targets > 0 ? Cast.Hex : Cast.None;
        }

        private bool CanRaise => raiseType != null && EnemySpawner.Current != null && settings.raiseCount > 0 && RaisedAlive < settings.raiseMax;

        /// <summary>Living zombies (not bosses, not this Shaman) within <see cref="Settings.hexRadius"/> m.</summary>
        public int HexTargets()
        {
            int n = 0;
            Vector3 p = transform.position;
            float r2 = settings.hexRadius * settings.hexRadius;
            var list = Enemy.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e == _enemy || !e.IsAlive || e.IsBoss) continue;
                float dx = e.transform.position.x - p.x, dz = e.transform.position.z - p.z;
                if (dx * dx + dz * dz <= r2) n++;
            }
            return n;
        }

        /// <summary>Zombies raised by this Shaman that are still alive.</summary>
        public int RaisedAlive
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxRaised; i++)
                    if (_raised[i] != null && _raised[i].IsAlive) n++;
                return n;
            }
        }

        private bool BeginCast(PlayerHealth knight, Cast cast)
        {
            var warnings = EnemyTelegraphs.Current;
            if (cast == Cast.None || warnings == null)
            {
                if (cast == Cast.None) _castCooldown = RetryDelay;
                return false;
            }
            Vector3 origin = transform.position;
            Vector3 to = knight.transform.position - origin;
            to.y = 0f;
            var dir = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;
            switch (cast)
            {
                case Cast.Orbs:
                case Cast.Hex:
                    var circle = warnings.RentCircle();
                    if (circle == null)
                    {
                        Retry(cast);
                        return false;
                    }
                    circle.Show(origin, cast == Cast.Orbs ? settings.orbWarnRadius : settings.hexRadius);
                    circle.Tint(RingTint, FillTint, FlashTint);
                    _circles[0] = circle;
                    Enemy.PlayAction(cast == Cast.Orbs ? "Cast" : "Summon");
                    break;
                case Cast.Raise:
                    if (!PlanRaise(warnings, origin, dir))
                    {
                        _castCooldown = RetryDelay;
                        return false;
                    }
                    Enemy.PlayAction("Summon");
                    break;
            }
            _cast = cast;
            _phase = Phase.WindUp;
            _time = 0f;
            Enemy.Halted = true;
            return true;
        }

        private void Retry(Cast cast)
        {
            if (cast == Cast.Orbs) _orbCooldown = 0.2f; // all warnings busy: try again soon (never cast without the warning)
            else _castCooldown = 0.2f;
        }

        /// <summary>Raise points on a ring round the Shaman, beside the Knight's line (pillars / the wall: closer in). False = none.</summary>
        private bool PlanRaise(EnemyTelegraphs warnings, Vector3 origin, Vector3 dir)
        {
            int want = Mathf.Min(settings.raiseCount, settings.raiseMax - RaisedAlive, _circles.Length);
            want = Mathf.Min(want, warnings.FreeCircles());
            _planned = 0;
            if (want <= 0) return false;
            float baseAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var walk = Enemy.Walkability;
            for (int k = 0; k < want; k++)
            {
                float rad = (baseAngle + 90f + 360f * k / want) * Mathf.Deg2Rad; // 2 points: left and right of the Knight's line
                var d = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 p = origin + d * settings.raiseRing;
                if (!Free(p, walk, RaiseClearRadius))
                {
                    p = origin + d * (settings.raiseRing * 0.5f);
                    if (!Free(p, walk, RaiseClearRadius)) continue;
                }
                var circle = warnings.RentCircle();
                if (circle == null) break;
                circle.Show(p, RaiseCircleRadius);
                circle.Tint(RingTint, FillTint, FlashTint);
                _raiseAt[_planned] = p;
                _circles[_planned] = circle;
                _planned++;
            }
            return _planned > 0;
        }

        private static bool Free(Vector3 p, IWalkability walk, float radius) =>
            (walk == null || !walk.IsBlocked(p, radius)) && (float.IsInfinity(Enemy.LandMaxZ) || p.z <= Enemy.LandMaxZ - radius);

        private void WindUp(PlayerHealth knight, float dt)
        {
            float p = WindUpTime > 0f ? _time / WindUpTime : 1f;
            for (int i = 0; i < _circles.Length; i++)
                if (_circles[i] != null) _circles[i].SetProgress(p);
            Vector3 to = knight.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), TurnSpeed * dt);
        }

        private void Release(PlayerHealth knight)
        {
            switch (_cast)
            {
                case Cast.Orbs: FireRing(knight); break;
                case Cast.Hex: HexAround(); break;
                case Cast.Raise: RaiseDead(); break;
            }
            for (int i = 0; i < _circles.Length; i++) ReleaseCircle(i, hide: false); // the flash fades on its own
            _phase = Phase.Recover;
            _time = 0f;
        }

        /// <summary>One ring: <see cref="Settings.orbCount"/> orbs evenly spaced, the first straight at the Knight; one volley id.</summary>
        private void FireRing(PlayerHealth knight)
        {
            _orbCooldown = settings.orbCooldown;
            var shots = EnemyProjectiles.Current;
            int fired = 0;
            if (shots != null)
            {
                Vector3 origin = transform.position;
                Vector3 to = knight.transform.position - origin;
                to.y = 0f;
                float baseAngle = to.sqrMagnitude > 0.0001f ? Mathf.Atan2(to.x, to.z) : transform.eulerAngles.y * Mathf.Deg2Rad;
                int volley = shots.NextVolley();
                float damage = settings.orbDamageShare * knight.Max;
                int count = Mathf.Clamp(settings.orbCount, 1, MaxOrbs);
                for (int i = 0; i < count; i++)
                {
                    float a = baseAngle + Mathf.PI * 2f * i / count;
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    if (shots.FireStraight(volley, ShotKind.SoulOrb, origin, d, settings.orbSpeed, settings.orbRange, damage, settings.orbHitRadius))
                        fired++;
                }
            }
            if (_circles[0] != null) _circles[0].Flash();
            OrbsFired?.Invoke(this, fired);
        }

        private void HexAround()
        {
            _castCooldown = settings.castCooldown;
            int n = 0;
            Vector3 p = transform.position;
            float r2 = settings.hexRadius * settings.hexRadius;
            var list = Enemy.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e == _enemy || !e.IsAlive || e.IsBoss) continue;
                float dx = e.transform.position.x - p.x, dz = e.transform.position.z - p.z;
                if (dx * dx + dz * dz <= r2 && e.ApplyHex(settings.hexBoost, settings.hexDuration)) n++;
            }
            if (_circles[0] != null) _circles[0].Flash();
            var fx = HexFx.Current;
            if (fx != null) fx.Puff(p);
            Hexed?.Invoke(this, n);
        }

        private void RaiseDead()
        {
            _castCooldown = settings.castCooldown;
            var spawner = EnemySpawner.Current;
            var fx = HexFx.Current;
            int n = 0;
            for (int i = 0; i < _planned; i++)
            {
                if (_circles[i] != null) _circles[i].Flash();
                if (spawner == null || raiseType == null) continue;
                int slot = FreeRaiseSlot();
                if (slot < 0) break;
                _raised[slot] = spawner.SpawnAt(raiseType, _raiseAt[i], onScreen: true); // they rise in sight, by design
                if (fx != null) fx.Puff(_raiseAt[i]);
                n++;
            }
            _planned = 0;
            Raised?.Invoke(this, n);
        }

        private int FreeRaiseSlot()
        {
            if (RaisedAlive >= settings.raiseMax) return -1;
            for (int i = 0; i < MaxRaised; i++)
                if (_raised[i] == null || !_raised[i].IsAlive) return i;
            return -1;
        }

        /// <summary>A raised zombie died: its pooled body may come back as a plain horde zombie, so it no longer counts.</summary>
        private void OnEnemyDamaged(Enemy enemy, float amount, bool killed)
        {
            if (!killed) return;
            for (int i = 0; i < MaxRaised; i++)
                if (_raised[i] == enemy) _raised[i] = null;
        }

        // ------------------------------------------------------------ Blink

        /// <summary>Drops any cast and starts the blink when a point is free; else tries again in a moment.</summary>
        private void TryBlink(PlayerHealth knight)
        {
            if (!FindBlinkPoint(knight.transform.position, out var to))
            {
                _blinkCooldown = 0.3f;
                return;
            }
            Cancel();
            _blinkFrom = transform.position;
            _blinkTo = to;
            _blinkCooldown = settings.blinkCooldown;
            _scale = transform.localScale;
            _shrunk = true;
            _phase = Phase.Vanish;
            _time = 0f;
            Enemy.Halted = true;
            var fx = HexFx.Current;
            if (fx != null) fx.Puff(_blinkFrom);
        }

        /// <summary>
        /// A ground point <see cref="Settings.blinkDistance"/> m from the Knight: straight away from it first, then turning
        /// out in 30 deg steps both ways. Walkable and on land; points inside the screen come first. False = none.
        /// </summary>
        public bool FindBlinkPoint(Vector3 knight, out Vector3 point)
        {
            Vector3 away = transform.position - knight;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
            float baseAngle = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
            var walk = Enemy.Walkability;
            float dist = settings.blinkDistance;
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < 12; k++)
                {
                    float off = (k + 1) / 2 * 30f * (k % 2 == 0 ? 1f : -1f); // 0, -30, 30, -60, 60 ... 180
                    float rad = (baseAngle + off) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                    Vector3 p = knight + d * dist;
                    p.y = transform.position.y;
                    if (!Free(p, walk, BlinkClearRadius)) continue;
                    if (pass == 0 && RangedAttack.ViewReach(knight, d) + RangedAttack.ViewMargin * 0.5f < dist) continue; // off screen
                    point = p;
                    return true;
                }
            point = transform.position;
            return false;
        }

        private void Reappear(PlayerHealth knight)
        {
            transform.position = _blinkTo;
            Vector3 to = knight.transform.position - _blinkTo;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(to);
            var fx = HexFx.Current;
            if (fx != null) fx.Puff(_blinkTo);
            _phase = Phase.Appear;
            _time = 0f;
            Blinked?.Invoke(this, _blinkFrom, _blinkTo);
        }

        private void SetVisible(float share)
        {
            _visible = Mathf.Clamp(share, MinVisible, 1f);
            transform.localScale = _scale * _visible;
        }

        private void RestoreScale()
        {
            if (_shrunk) transform.localScale = _scale;
            _shrunk = false;
            _visible = 1f;
        }

        // ------------------------------------------------------------ End

        private void Finish()
        {
            RestoreScale();
            _phase = Phase.Idle;
            _cast = Cast.None;
            _time = 0f;
            if (Enemy.IsAlive)
            {
                Enemy.Halted = false;
                Enemy.ResetAction();
            }
        }

        /// <summary>Ends a cast or blink: hides its warnings, restores the size, frees the enemy. Orbs in the air fly on.</summary>
        public void Cancel()
        {
            for (int i = 0; i < _circles.Length; i++) ReleaseCircle(i, hide: true);
            _planned = 0;
            RestoreScale();
            if (_phase != Phase.Idle && _enemy != null && _enemy.IsAlive)
            {
                _enemy.Halted = false;
                _enemy.ResetAction();
            }
            _phase = Phase.Idle;
            _cast = Cast.None;
            _time = 0f;
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
