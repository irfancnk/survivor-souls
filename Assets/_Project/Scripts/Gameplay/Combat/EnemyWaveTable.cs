using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Spawn pacing for one run or Chamber (KAN-29). Tuning data only: <see cref="EnemySpawner"/> holds no numbers.
    /// The run clock picks the active <see cref="Phase"/> (the last one whose <see cref="Phase.startTime"/> has passed).
    /// A phase spawns small groups every <see cref="Phase.spawnInterval"/> seconds while fewer than
    /// <see cref="Phase.maxAlive"/> enemies are up, plus an optional bigger burst every <see cref="Phase.burstInterval"/>
    /// seconds (KAN-30 sets the horde numbers). The enemy type of each spawn is a weighted pick from the phase's list.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Enemy Wave Table", fileName = "EnemyWaves")]
    public sealed class EnemyWaveTable : ScriptableObject
    {
        [Serializable]
        public struct EnemyEntry
        {
            public Enemy prefab;
            [Min(0f)] public float weight;
        }

        [Serializable]
        public struct Phase
        {
            [Tooltip("Run time (s) at which this phase takes over.")]
            [Min(0f)] public float startTime;
            [Tooltip("Cap of enemies alive or dying at once.")]
            [Min(0)] public int maxAlive;
            [Tooltip("Seconds between regular groups.")]
            [Min(0.05f)] public float spawnInterval;
            [Tooltip("Enemies per regular group (min, max inclusive). Each one gets its own spawn point.")]
            public Vector2Int groupSize;
            [Tooltip("Seconds between bursts. 0 = no bursts.")]
            [Min(0f)] public float burstInterval;
            [Tooltip("Enemies per burst (still capped by maxAlive).")]
            [Min(0)] public int burstSize;
            public EnemyEntry[] enemies;
        }

        [Tooltip("Seconds from scene start to the first spawn.")]
        [SerializeField, Min(0f)] private float firstSpawnDelay = 1f;
        [Tooltip("Enemies made per type before play starts, so the run never instantiates mid-fight.")]
        [SerializeField, Min(0)] private int prewarmPerType = 8;
        [SerializeField] private Phase[] phases = new Phase[0];

        public float FirstSpawnDelay => firstSpawnDelay;
        public int PrewarmPerType => prewarmPerType;
        public int PhaseCount => phases?.Length ?? 0;
        public Phase GetPhase(int index) => phases[index];

        /// <summary>Index of the phase active at <paramref name="time"/> seconds into the run; -1 when there is none.</summary>
        public int PhaseAt(float time)
        {
            int found = -1;
            for (int i = 0; i < PhaseCount; i++)
                if (phases[i].startTime <= time && (found < 0 || phases[i].startTime >= phases[found].startTime)) found = i;
            return found;
        }

        /// <summary>Weighted enemy pick from a phase; <paramref name="roll"/> in [0, 1). Null when the phase has no usable entry.</summary>
        public static Enemy PickEnemy(in Phase phase, float roll)
        {
            if (phase.enemies == null) return null;
            float total = 0f;
            foreach (var e in phase.enemies)
                if (e.prefab != null) total += e.weight;
            if (total <= 0f) return null;
            float r = roll * total;
            Enemy last = null;
            foreach (var e in phase.enemies)
            {
                if (e.prefab == null || e.weight <= 0f) continue;
                last = e.prefab;
                if (r < e.weight) return e.prefab;
                r -= e.weight;
            }
            return last;
        }

        /// <summary>Editor builder / tests: sets the whole table.</summary>
        public void Configure(float firstDelay, int prewarm, Phase[] newPhases)
        {
            firstSpawnDelay = firstDelay;
            prewarmPerType = prewarm;
            phases = newPhases;
        }
    }
}
