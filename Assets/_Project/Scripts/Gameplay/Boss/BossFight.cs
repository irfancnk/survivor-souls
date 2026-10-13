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
    /// Knight is within <see cref="BossData.SlamRange"/> and the cooldown is over, it stops (<see cref="Enemy.Halted"/>), the
    /// ground telegraph fills for <see cref="BossData.SlamTelegraph"/> s (never under 0.6 s), then the slam hits whatever is
    /// in the circle (<see cref="BossData.SlamDamageFor"/>: a share of max health, capped). Short recovery, then the chase
    /// resumes. Nothing attacks a dead Knight.</item>
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
        }

        private const float SpawnClearRadius = 1.4f; // free ground the boss body needs
        private const int SpawnTries = 12;

        [SerializeField] private BossData data;
        [Tooltip("Rules for boss Chambers: OpenCondition KillBoss (no clock).")]
        [SerializeField] private ChamberRules bossRules;
        [SerializeField] private SurvivalTimer timer;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private SoulWispField souls;
        [SerializeField] private GroundTelegraph telegraph;

        private Enemy _boss;
        private float _bossHealthMultiplier = 1f; // BossData health over the body's type health, x the Chamber multiplier
        private float _time;
        private float _cooldown;
        private Vector3 _spawnPoint;
        private Vector3 _slamCentre;

        public static BossFight Active { get; private set; }

        /// <summary>The boss stands up (after the spawn warning).</summary>
        public event Action<Enemy> Spawned;
        /// <summary>A slam starts winding up: centre on the ground, radius.</summary>
        public event Action<Vector3, float> SlamWarned;
        /// <summary>A slam landed: centre, damage dealt to the Knight (0 = dodged).</summary>
        public event Action<Vector3, float> Slammed;
        /// <summary>The boss died (reward dropped, portal opening).</summary>
        public event Action Defeated;

        public BossData Data => data;
        public Stage Current { get; private set; } = Stage.Inactive;
        public Attack CurrentAttack { get; private set; } = Attack.Chase;
        /// <summary>The boss body (null until it spawns).</summary>
        public Enemy Boss => _boss;
        /// <summary>Boss health 0..1 (1 before it spawns, 0 once defeated).</summary>
        public float HealthFraction => Current == Stage.Defeated ? 0f : _boss != null && _boss.IsAlive ? _boss.Health / _boss.MaxHealth : 1f;
        public GroundTelegraph Telegraph => telegraph;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossData bossData, ChamberRules rules, SurvivalTimer survivalTimer, EnemySpawner enemySpawner,
            PlayerHealth playerHealth, SoulWispField soulField, GroundTelegraph groundTelegraph)
        {
            data = bossData;
            bossRules = rules;
            timer = survivalTimer;
            spawner = enemySpawner;
            player = playerHealth;
            souls = soulField;
            telegraph = groundTelegraph;
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
                    if (PlanarDistance(_boss.transform.position, player.transform.position) <= data.SlamRange) BeginSlam();
                    break;
                case Attack.WindUp:
                    if (playerDead)
                    {
                        if (telegraph != null) telegraph.Hide();
                        EndAttack();
                        break;
                    }
                    _time += dt;
                    if (telegraph != null) telegraph.SetProgress(_time / data.SlamTelegraph);
                    if (_time >= data.SlamTelegraph) LandSlam();
                    break;
                case Attack.Recover:
                    _time += dt;
                    if (_time >= data.SlamRecover) EndAttack();
                    break;
            }
        }

        private void BeginSlam()
        {
            Vector3 from = _boss.transform.position;
            Vector3 to = player.transform.position - from;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : _boss.transform.forward;
            _boss.transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // commits to the swing: dodge sideways or back
            _boss.Halted = true;
            _slamCentre = new Vector3(from.x, player.transform.position.y, from.z) + dir * data.SlamReach;
            if (telegraph != null) telegraph.Show(_slamCentre, data.SlamRadius);
            CurrentAttack = Attack.WindUp;
            _time = 0f;
            SlamWarned?.Invoke(_slamCentre, data.SlamRadius);
        }

        private void LandSlam()
        {
            float dealt = 0f;
            if (player != null && !player.IsDead && PlanarDistance(player.transform.position, _slamCentre) <= data.SlamRadius)
                dealt = player.TakeDamage(data.SlamDamageFor(player.Max));
            if (telegraph != null) telegraph.Flash();
            CurrentAttack = Attack.Recover;
            _time = 0f;
            Slammed?.Invoke(_slamCentre, dealt);
        }

        private void EndAttack()
        {
            if (_boss != null) _boss.Halted = false;
            CurrentAttack = Attack.Chase;
            _cooldown = data.SlamCooldown;
            _time = 0f;
        }

        private void OnEnemyDamaged(Enemy enemy, float amount, bool killed)
        {
            if (!killed || enemy != _boss || Current != Stage.Fighting) return;
            if (telegraph != null && CurrentAttack == Attack.WindUp) telegraph.Hide();
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
