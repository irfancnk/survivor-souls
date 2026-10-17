using System;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.World;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Sets up this scene's Chamber (KAN-46) before anything else wakes: reads <see cref="RunState"/> (Realm, Chamber,
    /// run seed), asks the <see cref="ChamberDifficulty"/> asset for the <see cref="ChamberPlan"/>, gives the world its
    /// layout seed and the spawner its multipliers. <see cref="Current"/> holds the plan for the rest of the scene:
    /// KAN-22 / KAN-23 read <see cref="ChamberPlan.Type"/>, KAN-47 reads <see cref="ChamberPlan.IsLast"/>. A MiniBoss
    /// Chamber adds one mini boss to the horde (MiniBossDirector, KAN-23). A Boss Chamber enters the round <see cref="BossArena"/>
    /// (KAN-59) instead of the streamed shore; every other Chamber keeps the arena hidden. A non-boss Chamber whose theme
    /// (<see cref="ChamberThemeSchedule"/>, or <see cref="ForcedTheme"/>) names an island plays on that <see cref="IslandChamber"/>
    /// (KAN-245). On the "Chamber" object (ChamberBuilder).
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class ChamberSetup : MonoBehaviour
    {
        [SerializeField] private ChamberDifficulty difficulty;
        [SerializeField] private WorldStreamer world;
        [SerializeField] private EnemySpawner spawner;
        [Tooltip("Boss Chambers play here (KAN-59). Optional.")]
        [SerializeField] private BossArena arena;
        [Tooltip("Island Chambers, one per theme (KAN-245). The schedule picks one per Chamber; the others stay hidden.")]
        [SerializeField] private IslandChamber[] islands = Array.Empty<IslandChamber>();
        [Tooltip("Theme per Chamber of a Realm (KAN-245). Empty = every non-boss Chamber streams the shore.")]
        [SerializeField] private ChamberThemeSchedule themes;

        /// <summary>Dev / screenshots (-ss-theme): play every non-boss Chamber in this theme. Null = the schedule decides.</summary>
        public static string ForcedTheme;

        /// <summary>Theme id of the Chamber being played (null = streamed shore or boss arena).</summary>
        public static string CurrentTheme { get; private set; }

        /// <summary>Plan of the Chamber being played. Chamber 1, no scaling, before any Chamber was set up.</summary>
        public static ChamberPlan Current { get; private set; } = ChamberPlan.Plain(1, RunState.FirstChamber, 1);

        /// <summary>A Chamber was set up (its plan). Raised in Awake.</summary>
        public static event Action<ChamberPlan> Planned;

        public ChamberDifficulty Difficulty => difficulty;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(ChamberDifficulty rules, WorldStreamer worldStreamer, EnemySpawner enemySpawner, BossArena bossArena = null)
        {
            difficulty = rules;
            world = worldStreamer;
            spawner = enemySpawner;
            arena = bossArena;
        }

        public BossArena Arena => arena;
        public ChamberThemeSchedule Themes => themes;
        public System.Collections.Generic.IReadOnlyList<IslandChamber> Islands => islands;

        /// <summary>Tests: island Chambers and their schedule without the scene builder.</summary>
        public void SetupIslands(ChamberThemeSchedule schedule, params IslandChamber[] islandChambers)
        {
            themes = schedule;
            islands = islandChambers ?? Array.Empty<IslandChamber>();
        }

        private void Awake() => Apply(RunState.Realm, RunState.Chamber, RunState.RunSeed);

        /// <summary>Works out and applies the plan of <paramref name="chamber"/>. Public for tests (Awake calls it from <see cref="RunState"/>).</summary>
        public ChamberPlan Apply(int realm, int chamber, int runSeed)
        {
            var plan = difficulty != null
                ? difficulty.Plan(realm, chamber, runSeed)
                : ChamberPlan.Plain(realm, chamber, ChamberDifficulty.MixSeed(runSeed, realm, chamber));
            if (world != null) world.UseSeed(plan.LayoutSeed);
            if (spawner != null)
            {
                spawner.SetDifficulty(plan.CapMultiplier, plan.IntervalMultiplier, plan.HealthMultiplier);
                spawner.SetChamber(plan.Chamber); // KAN-112: wave entries open by Chamber
            }
            if (arena != null)
            {
                if (plan.Type == ChamberType.Boss) arena.Enter();
                else arena.Leave();
            }
            ApplyTheme(plan, realm);
            Current = plan;
            if (Debug.isDebugBuild) Debug.Log($"[ChamberSetup] {plan}");
            Planned?.Invoke(plan);
            return plan;
        }

        /// <summary>KAN-245: a non-boss Chamber with a theme plays on its island; every other island stays hidden.</summary>
        private void ApplyTheme(ChamberPlan plan, int realm)
        {
            string theme = null;
            if (plan.Type != ChamberType.Boss)
                theme = !string.IsNullOrEmpty(ForcedTheme) ? ForcedTheme : themes != null ? themes.ThemeFor(realm, plan.Chamber) : null;
            IslandChamber chosen = null;
            if (theme != null)
                foreach (var island in islands)
                    if (island != null && island.ThemeId == theme)
                    {
                        chosen = island;
                        break;
                    }
            foreach (var island in islands)
                if (island != null && island != chosen) island.Leave();
            if (chosen != null) chosen.Enter();
            CurrentTheme = chosen != null ? chosen.ThemeId : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = ChamberPlan.Plain(1, RunState.FirstChamber, 1);
            Planned = null;
            CurrentTheme = null;
            // ForcedTheme is set by the dev args before the scene loads; keep it.
        }
    }
}
