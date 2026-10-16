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
    /// (KAN-59) instead of the streamed shore; every other Chamber keeps the arena hidden. On the "Chamber" object (ChamberBuilder).
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class ChamberSetup : MonoBehaviour
    {
        [SerializeField] private ChamberDifficulty difficulty;
        [SerializeField] private WorldStreamer world;
        [SerializeField] private EnemySpawner spawner;
        [Tooltip("Boss Chambers play here (KAN-59). Optional.")]
        [SerializeField] private BossArena arena;

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
            Current = plan;
            if (Debug.isDebugBuild) Debug.Log($"[ChamberSetup] {plan}");
            Planned?.Invoke(plan);
            return plan;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = ChamberPlan.Plain(1, RunState.FirstChamber, 1);
            Planned = null;
        }
    }
}
