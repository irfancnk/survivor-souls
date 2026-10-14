using System;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Boss Chamber flow (KAN-22, first slice KAN-58). Runs only when <see cref="ChamberSetup.Current"/> is a
    /// <see cref="ChamberType.Boss"/> Chamber (every 5th and the last of the Realm, from the difficulty asset):
    /// <list type="number">
    /// <item><b>Start</b>: the survival timer switches to <see cref="OpenCondition.KillBoss"/> rules (no clock; the HUD hides
    /// it) and the horde thins out (<see cref="BossData.HordeCap"/>, <see cref="BossData.HordeInterval"/>).</item>
    /// <item><b>Waiting</b> (<see cref="BossData.IntroDelay"/>), then <b>Rising</b>: a red ground warning where the boss will
    /// stand (<see cref="BossData.SpawnWarning"/>, walkable ground ahead of the Knight), then the boss spawns.</item>
    /// <item><b>Fighting</b>: the boss chases like any enemy (contact damage, hit markers, haptics come for free). When the
    /// cooldown is over it picks an attack by distance, stops (<see cref="Enemy.Halted"/>) and warns on the ground (never
    /// under 0.6 s), then strikes; short recovery, then the chase resumes. Nothing attacks a dead Knight.
    /// <list type="bullet">
    /// <item><b>Slam</b> (Knight within <see cref="BossData.SlamRange"/>): the circle telegraph fills, then the slam hits
    /// whatever is in the circle.</item>
    /// <item><b>Fan</b> (farther, within <see cref="BossData.FanRange"/>): red lanes fill towards the Knight, then a volley of
    /// <see cref="BossProjectiles"/> flies down them; pillars and the ring wall stop the bolts; one volley hits once.</item>
    /// <item><b>Charge</b> (phase 2 only, between <see cref="BossData.ChargeMinRange"/> and <see cref="BossData.ChargeMaxRange"/>):
    /// a wide lane fills, then the boss dashes down it (it stops early at a pillar or the wall) and stands still for a
    /// punish window. In phase 2 the fan and the charge take turns.</item>
    /// </list>
    /// Every hit is a share of max health, capped by the max-hit share (no instant kill).</item>
    /// <item><b>Phase 2</b> (<see cref="BossData.Phase2At"/> health): <see cref="PhaseChanged"/> (a heart drops, the bar's
    /// phase mark lights, a roar), shorter cooldowns and wind-ups, a faster walk, and the charge joins.</item>
    /// <item><b>Defeated</b>: gold soul wisps (<see cref="BossData.RewardXp"/>) burst from the body and the portal opens
    /// (<see cref="SurvivalTimer.Complete"/>): the usual toast, sound and camera shake follow.</item>
    /// </list>
    /// Everything runs on scaled time, so level-up cards and the Chamber fade-in pause it. On the "Boss" object (BossBuilder).
    /// </summary>
    public sealed class BossFight : MonoBehaviour
    {
        public enum Stage
        {
            Inactive = 0,
            Waiting = 1,
            Rising = 2,
            Fighting = 3,
            Defeated = 4,
        }

        public enum Attack
        {
            Chase = 0,
            WindUp = 1,
            Recover = 2,
            /// <summary>The charge dash is moving the boss.</summary>
            Dashing = 3,
        }

        /// <summary>The boss's attack patterns (KAN-22).</summary>
        public enum Move
        {
            Slam = 0,
            Fan = 1,
            Charge = 2,
        }

        private const float SpawnClearRadius = 1.4f; // free ground the boss body needs
        private const int SpawnTries = 12;
        private const float ChargeBodyRadius = 1.1f; // ground the boss body needs along the dash
        private const float ChargeProbeStep = 0.5f;
        private const float MinChargeLength = 2f;

        [SerializeField] private BossData data;
        [Tooltip("Rules for boss Chambers: OpenCondition KillBoss (no clock).")]
        [SerializeField] private ChamberRules bossRules;
        [SerializeField] private SurvivalTimer timer;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private SoulWispField souls;
        [SerializeField] private GroundTelegraph telegraph;
        [Tooltip("Lane warnings of the fan and the charge (KAN-61).")]
        [SerializeField] private LineTelegraph lines;
        [Tooltip("Fan bolts (KAN-61).")]
        [SerializeField] private BossProjectiles projectiles;

        private Enemy _boss;
        private float _bossHealthMultiplier = 1f; // BossData health over the body's type health, x the Chamber multiplier
        private float _time;
        private float _cooldown;
        private Vector3 _spawnPoint;
        private Vector3 _slamCentre;
        private float _windUp;   // seconds of the current warning (phase 2 shortens it)
        private float _recover;  // seconds of the current recovery
        private Vector3 _attackOrigin, _attackDir;
        private float _dashLeft, _dashDealt;
        private bool _dashHit;
        private Move _lastRanged = Move.Slam;
        private readonly Vector3[] _fanDirs = new Vector3[LineTelegraph.MaxLanes];

        public static BossFight Active { get; private set; }

        /// <summary>The boss stands up (after the spawn warning).</summary>
        public event Action<Enemy> Spawned;
        /// <summary>A slam starts winding up: centre on the ground, radius.</summary>
        public event Action<Vector3, float> SlamWarned;
        /// <summary>A slam landed: centre, damage dealt to the Knight (0 = dodged).</summary>
        public event Action<Vector3, float> Slammed;
        /// <summary>The boss died (reward dropped, portal opening).</summary>
        public event Action Defeated;
        /// <summary>The boss entered a new phase (2). Hearts drop, the HUD lights the phase mark.</summary>
        public event Action<int> PhaseChanged;
        /// <summary>A fan winds up: boss ground point, aim direction.</summary>
        public event Action<Vector3, Vector3> FanWarned;
        /// <summary>A fan volley flew: boss ground point, bolts fired.</summary>
        public event Action<Vector3, int> FanFired;
        /// <summary>A charge winds up: start, direction, lane length.</summary>
        public event Action<Vector3, Vector3, float> ChargeWarned;
        /// <summary>A charge dash ended: where the boss stopped, damage dealt to the Knight (0 = dodged).</summary>
        public event Action<Vector3, float> Charged;

        public BossData Data => data;
        public Stage Current { get; private set; } = Stage.Inactive;
        public Attack CurrentAttack { get; private set; } = Attack.Chase;
        /// <summary>The attack being warned / struck / recovered from (meaningless while chasing).</summary>
        public Move CurrentMove { get; private set; } = Move.Slam;
        /// <summary>1 until the boss falls to <see cref="BossData.Phase2At"/> health, then 2.</summary>
        public int Phase { get; private set; } = 1;
        /// <summary>Seconds of the current (or last) warning, after the phase 2 factor and the 0.6 s floor.</summary>
        public float WindUpTime => _windUp;
        /// <summary>The boss body (null until it spawns).</summary>
        public Enemy Boss => _boss;
        /// <summary>Boss health 0..1 (1 before it spawns, 0 once defeated).</summary>
        public float HealthFraction => Current == Stage.Defeated ? 0f : _boss != null && _boss.IsAlive ? _boss.Health / _boss.MaxHealth : 1f;
        public GroundTelegraph Telegraph => telegraph;
        public LineTelegraph Lines => lines;
        public BossProjectiles Projectiles => projectiles;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossData bossData, ChamberRules rules, SurvivalTimer survivalTimer, EnemySpawner enemySpawner,
            PlayerHealth playerHealth, SoulWispField soulField, GroundTelegraph groundTelegraph,
            LineTelegraph laneTelegraph = null, BossProjectiles bolts = null)
        {
            data = bossData;
            bossRules = rules;
            timer = survivalTimer;
            spawner = enemySpawner;
            player = playerHealth;
            souls = soulField;
            telegraph = groundTelegraph;
            lines = laneTelegraph;
            projectiles = bolts;
        }

        private void OnEnable()
        {
            Active = this;
            Enemy.Damaged += OnEnemyDamaged;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            Enemy.Damaged -= OnEnemyDamaged;
        }

        private void Start() => Begin(ChamberSetup.Current);

        /// <summary>Starts the fight when <paramref name="plan"/> is a boss Chamber; otherwise stays inactive. Public for tests.</summary>
        public bool Begin(in ChamberPlan plan)
        {
            if (plan.Type != ChamberType.Boss || data == null || data.Prefab == null || Current != Stage.Inactive) return false;
            if (timer != null && bossRules != null) timer.UseRules(bossRules);
            if (spawner != null)
                spawner.SetDifficulty(plan.CapMultiplier * data.HordeCap, plan.IntervalMultiplier * data.HordeInterval, plan.HealthMultiplier);

            _boss = Instantiate(data.Prefab, transform);
            _boss.name = data.Prefab.name;
            _boss.gameObject.SetActive(false);
            _bossHealthMultiplier = data.MaxHealth / Mathf.Max(1f, _boss.BaseMaxHealth) * plan.HealthMultiplier;
            SetStage(Stage.Waiting);
            return true;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            switch (Current)
            {
                case Stage.Waiting:
                    _time += dt;
                    if (_time >= data.IntroDelay) BeginRising();
                    break;
                case Stage.Rising:
                    _time += dt;
                    if (telegraph != null) telegraph.SetProgress(_time / data.SpawnWarning);
                    if (_time >= data.SpawnWarning) SpawnBoss();
                    break;
                case Stage.Fighting:
                    TickAttack(dt);
                    break;
            }
        }

        private void BeginRising()
        {
            var p = player != null ? player.transform : null;
            _spawnPoint = p != null ? FindSpawnPoint(p.position, p.forward, data.SpawnDistance) : transform.position;
            if (telegraph != null) telegraph.Show(_spawnPoint, 1.6f);
            SetStage(Stage.Rising);
        }

        private void SpawnBoss()
        {
            if (telegraph != null) telegraph.Flash();
            _boss.Spawn(_spawnPoint, player != null ? player.transform : null, null, _bossHealthMultiplier);
            _cooldown = data.FirstSlamDelay;
            CurrentAttack = Attack.Chase;
            Phase = 1;
            SetStage(Stage.Fighting);
            Spawned?.Invoke(_boss);
        }

        private void TickAttack(float dt)
        {
            if (_boss == null || !_boss.IsAlive) return;
            bool playerDead = player == null || player.IsDead;
            switch (CurrentAttack)
            {
                case Attack.Chase:
                    _cooldown -= dt;
                    if (playerDead || _cooldown > 0f) break;
                    ChooseAttack(PlanarDistance(_boss.transform.position, player.transform.position));
                    break;
                case Attack.WindUp:
                    if (playerDead)
                    {
                        HideWarnings();
                        EndAttack(0f);
                        break;
                    }
                    _time += dt;
                    float progress = _time / _windUp;
                    if (CurrentMove == Move.Slam)
                    {
                        if (telegraph != null) telegraph.SetProgress(progress);
                    }
                    else if (lines != null) lines.SetProgress(progress);
                    if (_time < _windUp) break;
                    if (CurrentMove == Move.Slam) LandSlam();
                    else if (CurrentMove == Move.Fan) ReleaseFan();
                    else BeginDash();
                    break;
                case Attack.Dashing:
                    TickDash(dt);
                    break;
                case Attack.Recover:
                    _time += dt;
                    if (_time >= _recover) EndAttack(CurrentMove == Move.Slam ? data.SlamCooldown
                        : CurrentMove == Move.Fan ? data.FanCooldown : data.ChargeCooldown);
                    break;
            }
        }

        /// <summary>Slam up close; farther away the fan, and in phase 2 the charge (fan and charge take turns).</summary>
        private void ChooseAttack(float distance)
        {
            if (distance <= data.SlamRange)
            {
                BeginSlam();
                return;
            }
            bool fan = CanFan(distance), charge = CanCharge(distance);
            if (charge && (!fan || _lastRanged != Move.Charge) && TryBeginCharge()) return;
            if (fan) BeginFan();
        }

        private bool CanFan(float distance) =>
            data.FanBolts > 0 && lines != null && projectiles != null && distance <= data.FanRange;

        private bool CanCharge(float distance) =>
            Phase >= 2 && data.ChargeEnabled && lines != null
            && distance >= data.ChargeMinRange && distance <= data.ChargeMaxRange;

        /// <summary>
        /// Debug menu, dev screenshots and tests: starts <paramref name="move"/> now, whatever the distance and cooldown
        /// (the charge still needs room). Only while fighting and chasing. Returns true when the attack began.
        /// </summary>
        public bool ForceAttack(Move move)
        {
            if (Current != Stage.Fighting || CurrentAttack != Attack.Chase || player == null || player.IsDead) return false;
            if (_boss == null || !_boss.IsAlive) return false;
            switch (move)
            {
                case Move.Slam:
                    BeginSlam();
                    return true;
                case Move.Fan:
                    if (data.FanBolts <= 0 || lines == null || projectiles == null) return false;
                    BeginFan();
                    return true;
                default:
                    return lines != null && TryBeginCharge();
            }
        }

        /// <summary>Turns the boss to the Knight and stops its chase. Returns the ground direction.</summary>
        private Vector3 FaceKnight()
        {
            Vector3 from = _boss.transform.position;
            Vector3 to = player.transform.position - from;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : _boss.transform.forward;
            _boss.transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // commits to the attack: dodge sideways
            _boss.Halted = true;
            return dir;
        }

        private void BeginWindUp(Move move, float baseTelegraph)
        {
            CurrentMove = move;
            CurrentAttack = Attack.WindUp;
            _windUp = data.TelegraphFor(baseTelegraph, Phase);
            _time = 0f;
        }

        private void BeginSlam()
        {
            Vector3 from = _boss.transform.position;
            Vector3 dir = FaceKnight();
            _slamCentre = new Vector3(from.x, player.transform.position.y, from.z) + dir * data.SlamReach;
            if (telegraph != null) telegraph.Show(_slamCentre, data.SlamRadius);
            BeginWindUp(Move.Slam, data.SlamTelegraph);
            SlamWarned?.Invoke(_slamCentre, data.SlamRadius);
        }

        private void LandSlam()
        {
            float dealt = 0f;
            if (player != null && !player.IsDead && PlanarDistance(player.transform.position, _slamCentre) <= data.SlamRadius)
                dealt = player.TakeDamage(data.SlamDamageFor(player.Max));
            if (telegraph != null) telegraph.Flash();
            BeginRecover(data.SlamRecover);
            Slammed?.Invoke(_slamCentre, dealt);
        }

        private void BeginFan()
        {
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            lines.ShowFan(_attackOrigin, _attackDir, data.FanBolts, data.FanSpread, data.FanDistance, data.FanLaneWidth);
            BeginWindUp(Move.Fan, data.FanTelegraph);
            _lastRanged = Move.Fan;
            FanWarned?.Invoke(_attackOrigin, _attackDir);
        }

        private void ReleaseFan()
        {
            int count = Mathf.Min(lines.LaneCount, _fanDirs.Length);
            for (int i = 0; i < count; i++) _fanDirs[i] = lines.LaneDirection(i);
            int fired = projectiles.FireVolley(_attackOrigin, _fanDirs, count, data.FanSpeed, data.FanDistance,
                data.FanDamageFor(player != null ? player.Max : 0f), data.FanHitRadius);
            lines.Flash();
            BeginRecover(data.FanRecover);
            FanFired?.Invoke(_attackOrigin, fired);
        }

        /// <summary>Charge towards the Knight if the lane has room (it stops at pillars and the wall).</summary>
        private bool TryBeginCharge()
        {
            Vector3 from = _boss.transform.position;
            Vector3 to = player.transform.position - from;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : _boss.transform.forward;
            float length = ChargeLength(from, dir, data.ChargeDistance);
            if (length < MinChargeLength) return false;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            _dashLeft = length;
            lines.ShowLine(_attackOrigin, _attackDir, length, data.ChargeWidth);
            BeginWindUp(Move.Charge, data.ChargeTelegraph);
            _lastRanged = Move.Charge;
            ChargeWarned?.Invoke(_attackOrigin, _attackDir, length);
            return true;
        }

        /// <summary>Free run from <paramref name="from"/> along <paramref name="dir"/>, up to <paramref name="max"/> m.</summary>
        public static float ChargeLength(Vector3 from, Vector3 dir, float max)
        {
            var walk = Enemy.Walkability;
            float length = 0f;
            while (length + ChargeProbeStep <= max)
            {
                Vector3 p = from + dir * (length + ChargeProbeStep);
                if (!float.IsInfinity(Enemy.LandMaxZ) && p.z > Enemy.LandMaxZ - ChargeBodyRadius) break;
                if (walk != null && walk.IsBlocked(p, ChargeBodyRadius)) break;
                length += ChargeProbeStep;
            }
            return length;
        }

        private void BeginDash()
        {
            if (lines != null) lines.Flash();
            CurrentAttack = Attack.Dashing;
            _dashHit = false;
            _dashDealt = 0f;
            _time = 0f;
        }

        private void TickDash(float dt)
        {
            Vector3 from = _boss.transform.position;
            float step = Mathf.Min(data.ChargeSpeed * dt, _dashLeft);
            Vector3 next = from + _attackDir * step;
            _boss.transform.position = next;
            _dashLeft -= step;
            if (!_dashHit && player != null && !player.IsDead
                && BossProjectiles.SegmentDistance(from, next, player.transform.position) <= data.ChargeWidth * 0.5f)
            {
                _dashHit = true;
                _dashDealt = player.TakeDamage(data.ChargeDamageFor(player.Max));
            }
            if (_dashLeft > 0.0001f) return;
            BeginRecover(data.ChargeRecover);
            Charged?.Invoke(next, _dashDealt);
        }

        private void BeginRecover(float seconds)
        {
            CurrentAttack = Attack.Recover;
            _recover = seconds;
            _time = 0f;
        }

        private void EndAttack(float cooldown)
        {
            if (_boss != null) _boss.Halted = false;
            CurrentAttack = Attack.Chase;
            _cooldown = data.CooldownFor(cooldown, Phase);
            _time = 0f;
        }

        private void HideWarnings()
        {
            if (telegraph != null) telegraph.Hide();
            if (lines != null) lines.Hide();
        }

        private void EnterPhase2()
        {
            Phase = 2;
            if (_boss != null) _boss.SpeedBoost = data.Phase2Speed;
            PhaseChanged?.Invoke(2);
        }

        private void OnEnemyDamaged(Enemy enemy, float amount, bool killed)
        {
            if (enemy != _boss || Current != Stage.Fighting) return;
            if (!killed)
            {
                if (Phase == 1 && data.Phase2At > 0f && enemy.Health <= enemy.MaxHealth * data.Phase2At) EnterPhase2();
                return;
            }
            if (CurrentAttack == Attack.WindUp) HideWarnings();
            if (projectiles != null) projectiles.Clear(); // the fight is over: no bolt lands after the kill
            CurrentAttack = Attack.Chase;
            SetStage(Stage.Defeated);
            DropReward(enemy.transform.position);
            if (timer != null) timer.Complete(); // the portal opens
            Defeated?.Invoke();
        }

        private void DropReward(Vector3 at)
        {
            if (souls == null || data.RewardXp <= 0) return;
            int count = Mathf.Max(1, data.RewardWisps);
            int each = data.RewardXp / count;
            int rest = data.RewardXp - each * count;
            for (int i = 0; i < count; i++)
            {
                float a = i * (Mathf.PI * 2f / count);
                souls.Spawn(at + new Vector3(Mathf.Cos(a) * 0.7f, 1.2f, Mathf.Sin(a) * 0.7f), each + (i == 0 ? rest : 0));
            }
        }

        /// <summary>
        /// Walkable point <paramref name="distance"/> m from <paramref name="from"/>: straight ahead first, then fanning out
        /// left and right in 30 degree steps; never past the sea edge. Falls back to straight ahead.
        /// </summary>
        public static Vector3 FindSpawnPoint(Vector3 from, Vector3 forward, float distance)
        {
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 fallback = Clamp(from + forward * distance);
            var walk = Enemy.Walkability;
            if (walk == null) return fallback;
            for (int i = 0; i < SpawnTries; i++)
            {
                float angle = ((i + 1) / 2) * 30f * (i % 2 == 0 ? 1f : -1f);
                Vector3 p = Clamp(from + Quaternion.Euler(0f, angle, 0f) * forward * distance);
                if (!walk.IsBlocked(p, SpawnClearRadius)) return p;
            }
            return fallback;
        }

        private static Vector3 Clamp(Vector3 p)
        {
            if (!float.IsInfinity(Enemy.LandMaxZ)) p.z = Mathf.Min(p.z, Enemy.LandMaxZ - SpawnClearRadius);
            return p;
        }

        private void SetStage(Stage stage)
        {
            Current = stage;
            _time = 0f;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;
    }
}
