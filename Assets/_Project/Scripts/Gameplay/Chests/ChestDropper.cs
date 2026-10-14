using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chests
{
    /// <summary>
    /// Reliquary chest drops (KAN-72, part 2 of KAN-50). Listens to every kill (<see cref="Enemy.Damaged"/> with the kill
    /// flag) and rolls a chest with the player's <see cref="StatId.ChestChance"/> (normal kill) or
    /// <see cref="StatId.ChestEliteChance"/> (elite, <see cref="ChestDropConfig.IsElite"/>). Both are 0 until the Reliquary
    /// Hunter skill is owned, so without the skill nothing happens (the skill is pure data: its level table sets the stats,
    /// no effect code). Rules from <see cref="ChestDropConfig"/>: pity (after <see cref="ChestDropConfig.PitySeconds"/> of
    /// game time without a chest, the next kill always drops one), at most <see cref="ChestDropConfig.MaxOnGround"/> chests
    /// waiting, each one fades after its lifetime with a warning blink. Every Nth skill chest is Rare when the skill level
    /// sets <see cref="StatId.RareChestEvery"/>; <see cref="StatId.ChestChoices"/> adds upgrade-screen cards.
    /// Run-only and per Chamber: chests left on the ground and the pity clock are lost with the scene (KAN-50 decision: a
    /// chest left behind is lost). Chests are pooled children of this object; one Update ticks them all.
    /// Opening (KAN-73): the living Knight walks within <see cref="ChestDropConfig.PickupRadius"/> of a landed chest, it
    /// leaves the ground (<see cref="OpenStarted"/>: sound, haptic), plays its open animation for
    /// <see cref="ChestDropConfig.OpenTime"/> and raises <see cref="Opened"/> (<see cref="ChestRewards"/> queues the
    /// upgrade screen) before going back to the pool. While no owned skill can level up, kills roll no chest at all.
    /// </summary>
    public sealed class ChestDropper : MonoBehaviour
    {
        /// <summary>Result of <see cref="Simulate"/> (statistics check and debug, KAN-75).</summary>
        public struct DropStats
        {
            public int Kills, EliteKills, Drops, PityDrops, RareChests;
            public float Seconds;

            /// <summary>Chests per minute of game time.</summary>
            public float PerMinute => Seconds > 0f ? Drops / (Seconds / 60f) : 0f;
            /// <summary>Chests per kill.</summary>
            public float PerKill => Kills > 0 ? Drops / (float)Kills : 0f;

            public override string ToString() =>
                $"{Kills} kills ({EliteKills} elite) in {Seconds / 60f:0.#} min: {Drops} chests ({PityDrops} pity, {RareChests} rare), " +
                $"{PerMinute:0.00}/min, {PerKill:P2} per kill";
        }

        [SerializeField] private ChestDropConfig config;
        [SerializeField] private PlayerStats stats;
        [SerializeField] private ReliquaryChest chestPrefab;
        [Tooltip("The Knight: walks into chests to open them. Dead = no pickup.")]
        [SerializeField] private PlayerHealth player;
        [Tooltip("Owned skills: no chest drops while none can level up (a chest would have nothing to upgrade).")]
        [SerializeField] private SkillManager skills;
        [Tooltip("0 = random every run. Fixed seeds make drops repeatable (tests, bug repro).")]
        [SerializeField] private int seed;

        /// <summary>A chest landed (any source). Pickup sound, telemetry (ChestDropped).</summary>
        public event Action<ReliquaryChest> Dropped;
        /// <summary>A chest faded away unopened (telemetry ChestExpired).</summary>
        public event Action<ReliquaryChest> Expired;
        /// <summary>The Knight walked into a chest: open animation starts (sound, haptic).</summary>
        public event Action<ReliquaryChest> OpenStarted;
        /// <summary>
        /// The open animation ended (telemetry ChestOpened): contents (<see cref="ReliquaryChest.Tier"/>, Choices, Upgrades)
        /// and position are still valid during the call; the chest goes back to the pool right after.
        /// </summary>
        public event Action<ReliquaryChest> Opened;

        private readonly List<ReliquaryChest> _active = new List<ReliquaryChest>(4);
        private readonly List<ReliquaryChest> _free = new List<ReliquaryChest>(4);
        private readonly List<ReliquaryChest> _opening = new List<ReliquaryChest>(4);
        private System.Random _rng;
        private Action<Enemy, float, bool> _onDamaged;
        private float _sinceDrop;
        private int _skillChests;

        public IReadOnlyList<ReliquaryChest> Active => _active;
        /// <summary>Chests playing their open animation.</summary>
        public IReadOnlyList<ReliquaryChest> Opening => _opening;
        public int OpenedCount { get; private set; }
        public ChestDropConfig Config => config;
        /// <summary>Game seconds since the last chest while the skill is owned (pity clock).</summary>
        public float SinceLastDrop => _sinceDrop;
        /// <summary>Chests dropped by the skill this Chamber (Rare chest count).</summary>
        public int SkillChests => _skillChests;
        public int PityDrops { get; private set; }
        public int ExpiredCount { get; private set; }

        /// <summary>Chest chance per normal kill from the skill (0 = skill not owned).</summary>
        public float Chance => Mathf.Clamp01(PlayerStats.Eval(stats, StatId.ChestChance, 0f));
        /// <summary>Chest chance per elite kill from the skill.</summary>
        public float EliteChance => Mathf.Clamp01(PlayerStats.Eval(stats, StatId.ChestEliteChance, 0f));
        /// <summary>The skill is owned: kills roll and the pity clock runs.</summary>
        public bool IsHunting => Chance > 0f || EliteChance > 0f;
        /// <summary>Cards a skill chest offers now (config base + skill levels).</summary>
        public int Choices =>
            Mathf.Max(1, Mathf.RoundToInt(PlayerStats.Eval(stats, StatId.ChestChoices, config != null ? config.BaseChoices : 2)));
        /// <summary>Every Nth skill chest is Rare (0 = never).</summary>
        public int RareEvery => Mathf.Max(0, Mathf.RoundToInt(PlayerStats.Eval(stats, StatId.RareChestEvery, 0f)));
        public bool IsFull => config != null && _active.Count >= config.MaxOnGround;

        private void Awake()
        {
            _rng = seed != 0 ? new System.Random(seed) : new System.Random(Environment.TickCount);
            Prewarm();
        }

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(ChestDropConfig dropConfig, PlayerStats playerStats, ReliquaryChest prefab, int rngSeed = 0,
            PlayerHealth knight = null, SkillManager skillManager = null)
        {
            config = dropConfig;
            stats = playerStats;
            chestPrefab = prefab;
            player = knight;
            skills = skillManager;
            seed = rngSeed;
            _rng = seed != 0 ? new System.Random(seed) : new System.Random(Environment.TickCount);
            Prewarm();
        }

        private void OnEnable()
        {
            _onDamaged ??= OnDamaged;
            Enemy.Damaged += _onDamaged;
        }

        private void OnDisable() => Enemy.Damaged -= _onDamaged;

        private void OnDamaged(Enemy enemy, float amount, bool killed)
        {
            if (killed && config != null) OnKill(enemy.transform.position, config.IsElite(enemy));
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Runs the pity clock (only while hunting) and the chest timers by <paramref name="dt"/> game seconds.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (IsHunting) _sinceDrop += dt;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var chest = _active[i];
                if (!chest.Tick(dt)) continue;
                ExpiredCount++;
                Expired?.Invoke(chest);
                Release(i);
            }
            if (player != null && !player.IsDead && config != null) PickUp(player.transform.position);
            for (int i = _opening.Count - 1; i >= 0; i--)
            {
                var chest = _opening[i];
                if (!chest.TickOpen(dt, config != null ? config.OpenTime : 0.4f)) continue;
                _opening.RemoveAt(i);
                OpenedCount++;
                Opened?.Invoke(chest);
                chest.Despawn();
                _free.Add(chest);
            }
        }

        /// <summary>Opens every landed chest within the pickup radius of <paramref name="at"/> (ground plane).</summary>
        private void PickUp(Vector3 at)
        {
            float r2 = config.PickupRadius * config.PickupRadius;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var chest = _active[i];
                if (chest.Age < config.PickupDelay) continue;
                var d = chest.transform.position - at;
                if (d.x * d.x + d.z * d.z <= r2) Open(chest);
            }
        }

        /// <summary>
        /// Starts opening <paramref name="chest"/> (walk-in pickup, tests, debug): it leaves the ground (frees a cap slot,
        /// no expiry) and plays its open animation; <see cref="Opened"/> follows after <see cref="ChestDropConfig.OpenTime"/>.
        /// False when it is not a landed chest of this dropper.
        /// </summary>
        public bool Open(ReliquaryChest chest)
        {
            int i = _active.IndexOf(chest);
            if (i < 0 || !chest.BeginOpen()) return false;
            _active.RemoveAt(i);
            _opening.Add(chest);
            OpenStarted?.Invoke(chest);
            return true;
        }

        /// <summary>
        /// A kill at <paramref name="position"/>: rolls a skill chest. Nothing when the skill is not owned or the ground is
        /// full. The pity rule turns a failed roll into a drop. Returns the chest that landed, or null.
        /// </summary>
        public ReliquaryChest OnKill(Vector3 position, bool elite)
        {
            if (config == null || IsFull) return null;
            if (!Decide(elite, _rng, _sinceDrop, out bool pity)) return null;
            var tier = NextSkillChestTier(_skillChests, RareEvery);
            var chest = Drop(position, tier, Choices, tier == ChestTier.Rare ? config.RareUpgrades : 1);
            if (chest == null) return null;
            _skillChests++;
            if (pity) PityDrops++;
            return chest;
        }

        /// <summary>
        /// Puts a chest on free ground near <paramref name="position"/> (skill drops, later mini boss rewards and debug).
        /// Respects the ground cap (null when full). Resets the pity clock.
        /// </summary>
        public ReliquaryChest Drop(Vector3 position, ChestTier tier, int choices, int upgrades)
        {
            if (config == null || chestPrefab == null || IsFull) return null;
            var chest = Take();
            chest.Spawn(LandingPoint(position), tier, choices, upgrades, config.Lifetime, config.WarningTime);
            _active.Add(chest);
            _sinceDrop = 0f;
            Dropped?.Invoke(chest);
            return chest;
        }

        /// <summary>
        /// Guaranteed reward chest (boss kill, KAN-62): lands near <paramref name="position"/> even when the ground is full and
        /// never fades. Does not touch the pity clock or the skill chest count. Null without a config or prefab.
        /// </summary>
        public ReliquaryChest DropReward(Vector3 position, ChestTier tier, int choices, int upgrades)
        {
            if (config == null || chestPrefab == null) return null;
            var chest = Take();
            chest.Spawn(LandingPoint(position), tier, choices, upgrades, float.PositiveInfinity, 0f);
            _active.Add(chest);
            Dropped?.Invoke(chest);
            return chest;
        }

        /// <summary>Debug / dev (DevScreenshot -ss-chests, KAN-75 menu): a skill chest now, as if rolled.</summary>
        public ReliquaryChest DropNow(Vector3 position, ChestTier? tier = null)
        {
            var t = tier ?? NextSkillChestTier(_skillChests, RareEvery);
            var chest = Drop(position, t, Choices, t == ChestTier.Rare ? config.RareUpgrades : 1);
            if (chest != null) _skillChests++;
            return chest;
        }

        /// <summary>Takes a chest off the ground without opening it (tools, tests). Not counted as expired or opened.</summary>
        public bool Remove(ReliquaryChest chest)
        {
            int i = _active.IndexOf(chest);
            if (i < 0) return false;
            Release(i);
            return true;
        }

        /// <summary>
        /// Statistics without spawning anything (KAN-72 check, KAN-75 debug): <paramref name="kills"/> kills,
        /// <paramref name="eliteShare"/> of them elite, one every <paramref name="secondsPerKill"/> game seconds (0 = pity
        /// off), at the player's current chances. Chests are assumed collected at once (the cap never blocks). Uses its own
        /// random stream: live drops and the pity clock are untouched.
        /// </summary>
        public DropStats Simulate(int kills, float eliteShare, float secondsPerKill, int rngSeed)
        {
            var rng = new System.Random(rngSeed);
            var result = new DropStats();
            float since = 0f;
            int skillChests = 0;
            int rareEvery = RareEvery;
            for (int k = 0; k < kills; k++)
            {
                since += secondsPerKill;
                result.Seconds += secondsPerKill;
                bool elite = rng.NextDouble() < eliteShare;
                result.Kills++;
                if (elite) result.EliteKills++;
                if (!Decide(elite, rng, secondsPerKill > 0f ? since : 0f, out bool pity)) continue;
                result.Drops++;
                if (pity) result.PityDrops++;
                if (NextSkillChestTier(skillChests, rareEvery) == ChestTier.Rare) result.RareChests++;
                skillChests++;
                since = 0f;
            }
            return result;
        }

        /// <summary>
        /// Drop statistics per skill level (KAN-75 debug and balance check): <see cref="Simulate"/> on a throw-away stat sheet
        /// with <paramref name="hunter"/> (Reliquary Hunter) at each level 1..max, so no scene or live run is needed. Index
        /// L - 1 = level L. Debug only: allocates.
        /// </summary>
        public static DropStats[] SimulatePerLevel(ChestDropConfig dropConfig, SkillDefinition hunter, int kills, float eliteShare,
            float secondsPerKill, int rngSeed)
        {
            if (dropConfig == null || hunter == null) return new DropStats[0];
            var go = new GameObject("Chest Statistics (temp)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sheet = go.AddComponent<PlayerStats>();
                var dropper = go.AddComponent<ChestDropper>();
                dropper.Setup(dropConfig, sheet, null, rngSeed);
                dropper.enabled = false; // no live kill events
                var result = new DropStats[hunter.MaxLevel];
                for (int level = 1; level <= hunter.MaxLevel; level++)
                {
                    hunter.ApplyLevel(sheet, level);
                    result[level - 1] = dropper.Simulate(kills, eliteShare, secondsPerKill, rngSeed + level);
                }
                return result;
            }
            finally
            {
                DestroyImmediate(go);
            }
        }

        /// <summary>Roll for one kill: the skill's chance, else the pity rule once the clock passed the pity time.</summary>
        private bool Decide(bool elite, System.Random rng, float sinceDrop, out bool pity)
        {
            pity = false;
            if (!IsHunting || (skills != null && !skills.HasUpgradableSkill())) return false;
            if (rng.NextDouble() < (elite ? EliteChance : Chance)) return true;
            pity = sinceDrop >= config.PitySeconds;
            return pity;
        }

        /// <summary>Tier of the next skill chest: Rare when it is every <paramref name="rareEvery"/>th one.</summary>
        public static ChestTier NextSkillChestTier(int droppedSoFar, int rareEvery) =>
            rareEvery > 0 && (droppedSoFar + 1) % rareEvery == 0 ? ChestTier.Rare : ChestTier.Light;

        private Vector3 LandingPoint(Vector3 around)
        {
            float r = config.Scatter;
            var walk = Enemy.Walkability;
            for (int i = 0; i < 4 && r > 0f; i++)
            {
                float a = (float)_rng.NextDouble() * Mathf.PI * 2f;
                float d = r * (0.4f + 0.6f * (float)_rng.NextDouble());
                var p = around + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (walk == null || !walk.IsBlocked(p, 0.45f)) return p;
            }
            return around;
        }

        private void Prewarm()
        {
            if (chestPrefab == null || config == null) return;
            while (_free.Count + _active.Count + _opening.Count < config.MaxOnGround) _free.Add(Create());
        }

        private ReliquaryChest Take()
        {
            if (_free.Count == 0) return Create();
            int last = _free.Count - 1;
            var chest = _free[last];
            _free.RemoveAt(last);
            return chest;
        }

        private ReliquaryChest Create()
        {
            var chest = Instantiate(chestPrefab, transform);
            chest.name = chestPrefab.name;
            chest.gameObject.SetActive(false);
            return chest;
        }

        private void Release(int index)
        {
            var chest = _active[index];
            _active.RemoveAt(index);
            chest.Despawn();
            _free.Add(chest);
        }
    }
}
