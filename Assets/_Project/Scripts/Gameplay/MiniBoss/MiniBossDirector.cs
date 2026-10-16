using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.MiniBosses
{
    /// <summary>
    /// The mini boss of a mini boss Chamber (KAN-23, <see cref="ChamberType.MiniBoss"/> from <see cref="ChamberSetup.Current"/>).
    /// <see cref="MiniBossData.SpawnDelay"/> game seconds into the Chamber a red ground warning (<see cref="GroundTelegraph"/>)
    /// grows on free ground <see cref="MiniBossData.SpawnDistance"/> m ahead of the living Knight; when it is full, the roster
    /// enemy <see cref="MiniBossData.EnemyType"/> rises there from the spawner's pool and is promoted
    /// (<see cref="Enemy.PromoteToMiniBoss"/>: health, size, tint) with a ground aura (<see cref="MiniBossAura"/>). It keeps
    /// its type's own attacks. The kill gives <see cref="MiniBossData.BonusXp"/> in several wisps and exactly one chest
    /// (<see cref="ChestDropper.DropReward"/>: never fades, ignores the ground cap) of the data tier, +1 upgrade with
    /// Reliquary Hunter (KAN-50); with no upgradable skill a chest would be empty, so a bonus skill pick is queued instead
    /// (same rule as <see cref="BossReward"/>). At most one mini boss per Chamber; the scene reloads every Chamber, so the
    /// state is per Chamber and per run. KAN-212: from the rise its own telegraphed attack runs (<see cref="MiniBossAttack"/>,
    /// <see cref="MiniBossData.Attack"/>). On the "MiniBoss" object (MiniBossBuilder).
    /// </summary>
    public sealed class MiniBossDirector : MonoBehaviour
    {
        public enum Phase { Off, Waiting, Warning, Alive, Done }

        private const int SpotTries = 8;

        [SerializeField] private MiniBossData data;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private ChestDropper chests;
        [SerializeField] private SkillManager skills;
        [SerializeField] private LevelUpFlow flow;
        [SerializeField] private XpManager xp;
        [SerializeField] private SoulWispField souls;
        [Tooltip("Ground warning before the rise.")]
        [SerializeField] private GroundTelegraph warning;
        [SerializeField] private MiniBossAura aura;
        [Tooltip("KAN-212: the mini boss's own telegraphed attack.")]
        [SerializeField] private MiniBossAttack attack;
        [Tooltip("Warning colours (ring, disc, flash): soul crimson, apart from the attack red.")]
        [SerializeField] private Color warningRing = new Color(0.55f, 0.02f, 0.08f, 1f);
        [SerializeField] private Color warningFill = new Color(0.32f, 0.0f, 0.05f, 0.72f);
        [SerializeField] private Color warningFlash = new Color(1f, 0.45f, 0.25f, 0.85f);

        /// <summary>The warning ring appeared (centre).</summary>
        public event Action<Vector3> WarningShown;
        /// <summary>The mini boss rose.</summary>
        public event Action<Enemy> Spawned;
        /// <summary>The mini boss died (death point).</summary>
        public event Action<Vector3> Killed;
        /// <summary>Its reward chest landed.</summary>
        public event Action<ReliquaryChest> ChestDropped;
        /// <summary>No chest possible: a bonus skill pick was queued (level).</summary>
        public event Action<int> PickQueued;

        private Action<Enemy, float, bool> _onDamaged;
        private float _clock;
        private float _delay;
        private Vector3 _spot;

        public Phase State { get; private set; } = Phase.Off;
        /// <summary>The living mini boss (null before the rise and after the kill).</summary>
        public Enemy Boss { get; private set; }
        public MiniBossData Data => data;
        /// <summary>KAN-212: the special attack loop (null when the scene has none).</summary>
        public MiniBossAttack Attack => attack;
        public Vector3 Spot => _spot;
        /// <summary>The reward chest (null before the kill or when a pick was given).</summary>
        public ReliquaryChest Chest { get; private set; }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(MiniBossData miniBoss, EnemySpawner enemySpawner, PlayerHealth knight, ChestDropper chestDropper,
            SkillManager skillManager, LevelUpFlow levelUpFlow, XpManager xpManager, SoulWispField wisps,
            GroundTelegraph warningTelegraph = null, MiniBossAura groundAura = null, MiniBossAttack specialAttack = null)
        {
            data = miniBoss;
            spawner = enemySpawner;
            player = knight;
            chests = chestDropper;
            skills = skillManager;
            flow = levelUpFlow;
            xp = xpManager;
            souls = wisps;
            warning = warningTelegraph;
            aura = groundAura;
            attack = specialAttack;
        }

        private void OnEnable()
        {
            _onDamaged ??= OnDamaged;
            Enemy.Damaged += _onDamaged;
        }

        private void OnDisable() => Enemy.Damaged -= _onDamaged;

        private void Start()
        {
            if (State == Phase.Off && ChamberSetup.Current.Type == ChamberType.MiniBoss) Arm();
        }

        /// <summary>
        /// Starts the countdown to the mini boss (<paramref name="delay"/> game seconds; negative = the data delay). Start
        /// calls it in a mini boss Chamber; tests and dev tools call it anywhere. Once per Chamber: false after a spawn.
        /// </summary>
        public bool Arm(float delay = -1f)
        {
            if (data == null || State == Phase.Alive || State == Phase.Done || State == Phase.Warning) return false;
            _delay = delay >= 0f ? delay : data.SpawnDelay;
            _clock = 0f;
            State = Phase.Waiting;
            return true;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the countdown and the warning by <paramref name="dt"/> game seconds. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            switch (State)
            {
                case Phase.Waiting:
                    if (player == null || player.IsDead) return;
                    _clock += dt;
                    if (_clock >= _delay) BeginWarning();
                    break;
                case Phase.Warning:
                    _clock += dt;
                    float t = _clock / data.WarningTime;
                    if (warning != null) warning.SetProgress(t);
                    if (t >= 1f) Rise();
                    break;
                case Phase.Alive:
                    if (Boss != null && !Boss.gameObject.activeSelf) Finish(); // pooled away without a kill (tools)
                    break;
            }
        }

        private void BeginWarning()
        {
            _spot = FindSpot(player.transform);
            _clock = 0f;
            State = Phase.Warning;
            if (warning != null)
            {
                warning.Show(_spot, data.WarningRadius);
                warning.Tint(warningRing, warningFill, warningFlash);
            }
            WarningShown?.Invoke(_spot);
        }

        /// <summary>
        /// Free ground <see cref="MiniBossData.SpawnDistance"/> m from the Knight: ahead of where it faces first, then turning
        /// 45 degrees per try; the land edge (<see cref="Enemy.LandMaxZ"/>) is kept clear. No allocation.
        /// </summary>
        private Vector3 FindSpot(Transform knight)
        {
            Vector3 at = knight.position;
            Vector3 ahead = knight.forward;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 0.01f) ahead = Vector3.forward;
            ahead.Normalize();
            float dist = data.SpawnDistance;
            for (int i = 0; i < SpotTries; i++)
            {
                float turn = (i + 1) / 2 * 45f * (i % 2 == 0 ? 1f : -1f);
                var p = at + Quaternion.Euler(0f, turn, 0f) * ahead * dist;
                p.y = at.y;
                if (p.z > Enemy.LandMaxZ - 1f) continue;
                if (Enemy.Walkability != null && Enemy.Walkability.IsBlocked(p, 1f)) continue;
                return p;
            }
            return at + ahead * (dist * 0.5f);
        }

        private void Rise()
        {
            if (warning != null) warning.Flash();
            State = Phase.Done;
            var prefab = spawner != null ? spawner.FindType(data.EnemyType) : null;
            if (prefab == null)
            {
                Debug.LogWarning($"[MiniBoss] no enemy type {data.EnemyType} in this scene");
                return;
            }
            var enemy = spawner.SpawnAt(prefab, _spot, onScreen: true);
            enemy.PromoteToMiniBoss(data.HealthMultiplier, data.SizeMultiplier, data.Tint);
            Boss = enemy;
            State = Phase.Alive;
            if (aura != null) aura.Follow(enemy, data.AuraRadius, data.AuraColor);
            if (attack != null) attack.Begin(enemy, data, player);
            if (Debug.isDebugBuild) Debug.Log($"[MiniBoss] {data.DisplayName} rose at {_spot}, health {enemy.MaxHealth:0}");
            Spawned?.Invoke(enemy);
        }

        private void OnDamaged(Enemy enemy, float amount, bool killed)
        {
            if (!killed || State != Phase.Alive || enemy != Boss) return;
            var at = enemy.transform.position;
            Finish();
            GiveReward(at);
            Killed?.Invoke(at);
        }

        private void Finish()
        {
            Boss = null;
            State = Phase.Done;
            if (aura != null) aura.Stop();
            if (attack != null) attack.Stop();
        }

        /// <summary>Bonus wisps and the chest (or the bonus pick) at <paramref name="at"/>.</summary>
        private void GiveReward(Vector3 at)
        {
            if (souls != null && data.BonusXp > 0)
            {
                int count = Mathf.Min(data.BonusWisps, data.BonusXp);
                int each = data.BonusXp / count, rest = data.BonusXp - each * count;
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2f / count;
                    souls.Spawn(at + new Vector3(Mathf.Cos(a) * 0.8f, 1.2f, Mathf.Sin(a) * 0.8f), each + (i == 0 ? rest : 0));
                }
            }
            bool upgradable = skills == null || skills.HasUpgradableSkill();
            if (chests != null && upgradable)
            {
                Chest = chests.DropReward(at, data.ChestTier, data.ChestChoices, Upgrades);
                if (Chest != null)
                {
                    ChestDropped?.Invoke(Chest);
                    return;
                }
            }
            int level = xp != null ? xp.Level : 1;
            if (flow != null && flow.EnqueueBonusPick(level)) PickQueued?.Invoke(level);
        }

        /// <summary>Skill levels the reward chest gives now: the data value, + the Reliquary Hunter bonus when it is owned.</summary>
        public int Upgrades
        {
            get
            {
                if (data == null) return 1;
                bool hunter = skills != null && !string.IsNullOrEmpty(data.HunterSkillId) && skills.GetLevel(data.HunterSkillId) > 0;
                return data.ChestUpgrades + (hunter ? data.HunterBonusUpgrades : 0);
            }
        }
    }
}
