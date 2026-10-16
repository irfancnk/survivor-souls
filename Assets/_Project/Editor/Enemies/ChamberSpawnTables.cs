using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-112: the roster in the run. One-time change of the Realm 1 wave table (<see cref="Apply"/>): which zombie joins
    /// from which Chamber (<see cref="EnemyWaveTable.EnemyEntry.fromChamber"/>), the shooters in the phases from
    /// <see cref="JoinTime"/> s, and the shooter cap. Realm 1 order (10 Chambers, bosses at 5 and 10):
    /// Chamber 1 Graveyard Laborer, Rusted Watchman, Runner, Grave Brute, Frenzied Ghoul (melee only);
    /// Chamber 2 + Pyre Thrower, Bile Spitter; Chamber 3 + Plague Bloater; Chamber 4 + Hexbound Shaman.
    /// The first <see cref="JoinTime"/> s of every Chamber stay melee only. Called by GameSceneBuilder.
    /// </summary>
    public static class ChamberSpawnTables
    {
        public const string Pyre = "PyreThrower", Spitter = "BileSpitter";
        public const float JoinTime = 12f; // KAN-232: the second phase of the 45 s Chamber (was 45 s of 2 min)

        /// <summary>First Chamber per roster id; ids not listed spawn from Chamber 1.</summary>
        public static readonly (string id, int chamber)[] Gates =
        {
            (Pyre, 2),
            (Spitter, 2),
            (SpecialMeleeBuilder.Bloater, 3),
            (ShamanBuilder.Id, 4),
        };

        /// <summary>Weights per phase index (Pyre Thrower, Bile Spitter, Shaman); phases before 45 s get none, later ones the last row.</summary>
        public static readonly float[][] Shares =
        {
            new[] { 0f, 0f, 0f },
            new[] { 0.05f, 0.05f, 0.03f },
            new[] { 0.06f, 0.06f, 0.04f },
            new[] { 0.06f, 0.06f, 0.05f },
        };

        /// <summary>Shooter cap: 20 % of the phase cap, at most 6 alive (Chamber 1 45 s: 24 alive -> 4 shooters).</summary>
        public const float ShooterShare = 0.2f;
        public const int MostShooters = 6;

        public static int GateOf(string id)
        {
            foreach (var (gateId, chamber) in Gates)
                if (gateId == id) return chamber;
            return 1;
        }

        /// <summary>
        /// Adds the shooters (<see cref="Shares"/>), sets <see cref="Gates"/> on every matching entry and the shooter cap.
        /// Runs only while no shooter is in the table, so designer edits made later are kept. Returns true when it changed the table.
        /// </summary>
        public static bool Apply(EnemyWaveTable table)
        {
            var shooters = new[] { Pyre, Spitter, ShamanBuilder.Id }.Select(EnemyRosterBuilder.LoadPrefab).ToArray();
            if (table == null || shooters.Any(s => s == null)) return false;
            var phases = Enumerable.Range(0, table.PhaseCount).Select(table.GetPhase).ToArray();
            if (phases.Any(p => p.enemies != null && p.enemies.Any(e => shooters.Contains(e.prefab)))) return false;
            for (int i = 0; i < phases.Length; i++)
            {
                var list = new List<EnemyWaveTable.EnemyEntry>(phases[i].enemies ?? new EnemyWaveTable.EnemyEntry[0]);
                if (phases[i].startTime >= JoinTime - 0.01f)
                {
                    var shares = Shares[Mathf.Min(i, Shares.Length - 1)];
                    for (int s = 0; s < shooters.Length; s++)
                        if (shares[s] > 0f) list.Add(new EnemyWaveTable.EnemyEntry { prefab = shooters[s], weight = shares[s] });
                }
                for (int k = 0; k < list.Count; k++)
                {
                    var e = list[k];
                    if (e.prefab == null) continue;
                    e.fromChamber = GateOf(e.prefab.name.Replace("Enemy_", ""));
                    list[k] = e;
                }
                phases[i].enemies = list.ToArray();
            }
            table.Configure(table.FirstSpawnDelay, table.PrewarmPerType, phases);
            table.SetShooterCap(ShooterShare, MostShooters);
            EditorUtility.SetDirty(table);
            Debug.Log("[ChamberSpawnTables] wave table: shooters added, Chamber gates set (KAN-112)");
            return true;
        }
    }
}
