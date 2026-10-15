using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-106: special melee zombies. Prefab side (<see cref="ConfigurePrefab"/>, called by <see cref="EnemyRosterBuilder"/>
    /// before it saves a prefab): the attack component with the numbers of Docs/ENEMY_ROSTER.md 3.5 / 3.9 / 3.10
    /// (<see cref="BloaterBurst"/>, <see cref="GhoulLeap"/>, <see cref="BruteSlam"/>). The toxic cloud look is in the hazard
    /// pool of <see cref="RangedEnemyBuilder"/>. Waves (<see cref="AddToWaves"/>, one time): the Grave Brute takes the old
    /// scaled Brute's slots, the Bloater and the Ghoul join the phases from 45 s.
    /// Debug menus (Play mode): "Survivor Souls/Debug/Melee: Spawn Plague Bloater / Frenzied Ghoul / Grave Brute".
    /// </summary>
    public static class SpecialMeleeBuilder
    {
        public const string Bloater = "PlagueBloater", Ghoul = "FrenziedGhoul", Brute = "GraveBrute";
        public static readonly string[] Ids = { Bloater, Ghoul, Brute };

        /// <summary>Plague Bloater (3.5): swell 1 s in 2 m, burst r 2.8 m 25 %, other zombies 50 % of their max, cloud r 2.2 m 6 s 15 dps.</summary>
        public static readonly BloaterBurst.Settings BloaterSpec = new BloaterBurst.Settings
        {
            triggerRange = 2f, swell = 1f, radius = 2.8f, damageShare = 0.25f, enemyShare = 0.5f, deathFuse = 0.6f,
            swellScale = 1.22f, spawnCooldown = 1f,
            cloud = new HazardSpec { kind = HazardKind.Toxic, radius = 2.2f, life = 6f, dps = 15f },
        };

        /// <summary>Frenzied Ghoul (3.9): at 4-6 m crouch 0.6 s + lane, leap 5 m in 0.35 s, 10 %, stun 0.8 s.</summary>
        public static readonly GhoulLeap.Settings GhoulSpec = new GhoulLeap.Settings
        {
            minRange = 4f, maxRange = 6f, crouch = 0.6f, leapDistance = 5f, minLeap = 3f, leapTime = 0.35f, arcHeight = 0.8f,
            laneWidth = 1f, hitPadding = 0.25f, damageShare = 0.1f, stun = 0.8f, cooldown = 3f,
        };

        /// <summary>Grave Brute (3.10): in 3.4 m wind-up 0.9 s, ring 0 -> 4.5 m in 0.6 s, band 0.8 m, 18 %.</summary>
        public static readonly BruteSlam.Settings BruteSpec = new BruteSlam.Settings
        {
            triggerRange = 3.4f, windUp = 0.9f, radius = 4.5f, width = 0.8f, waveTime = 0.6f, damageShare = 0.18f,
            recover = 0.5f, cooldown = 4f,
        };

        public static bool IsSpecial(string id) => Ids.Contains(id);

        /// <summary>The attack component on a roster prefab root before it is saved. No-op for other types.</summary>
        public static void ConfigurePrefab(GameObject root, string id)
        {
            switch (id)
            {
                case Bloater: root.AddComponent<BloaterBurst>().Configure(BloaterSpec); break;
                case Ghoul: root.AddComponent<GhoulLeap>().Configure(GhoulSpec); break;
                case Brute: root.AddComponent<BruteSlam>().Configure(BruteSpec); break;
            }
        }

        // ------------------------------------------------------------ Waves

        /// <summary>Wave weights per phase index (Bloater, Ghoul); phases before 45 s get none, later phases use the last row.</summary>
        public static readonly float[][] Shares =
        {
            new[] { 0f, 0f },
            new[] { 0.04f, 0.05f },
            new[] { 0.06f, 0.07f },
            new[] { 0.07f, 0.08f },
        };

        public const float JoinTime = 45f;

        /// <summary>
        /// One-time change of the wave table (KAN-106): every old Brute entry (<see cref="CombatBuilder.BrutePrefabPath"/>) becomes
        /// the Grave Brute with the same weight; phases from <see cref="JoinTime"/> s get the Bloater and the Ghoul
        /// (<see cref="Shares"/>). Runs only while none of the three is in the table, so designer edits are kept.
        /// Returns true when it changed the table.
        /// </summary>
        public static bool AddToWaves(EnemyWaveTable table)
        {
            var bloater = EnemyRosterBuilder.LoadPrefab(Bloater);
            var ghoul = EnemyRosterBuilder.LoadPrefab(Ghoul);
            var brute = EnemyRosterBuilder.LoadPrefab(Brute);
            var oldBrute = AssetDatabase.LoadAssetAtPath<Enemy>(CombatBuilder.BrutePrefabPath);
            if (table == null || bloater == null || ghoul == null || brute == null) return false;
            var phases = Enumerable.Range(0, table.PhaseCount).Select(table.GetPhase).ToArray();
            var specials = new[] { bloater, ghoul, brute };
            if (phases.Any(p => p.enemies != null && p.enemies.Any(e => specials.Contains(e.prefab)))) return false;
            for (int i = 0; i < phases.Length; i++)
            {
                var list = new List<EnemyWaveTable.EnemyEntry>();
                foreach (var e in phases[i].enemies ?? new EnemyWaveTable.EnemyEntry[0])
                    list.Add(oldBrute != null && e.prefab == oldBrute ? new EnemyWaveTable.EnemyEntry { prefab = brute, weight = e.weight } : e);
                if (phases[i].startTime >= JoinTime - 0.01f)
                {
                    var shares = Shares[Mathf.Min(i, Shares.Length - 1)];
                    if (shares[0] > 0f) list.Add(new EnemyWaveTable.EnemyEntry { prefab = bloater, weight = shares[0] });
                    if (shares[1] > 0f) list.Add(new EnemyWaveTable.EnemyEntry { prefab = ghoul, weight = shares[1] });
                }
                phases[i].enemies = list.ToArray();
            }
            table.Configure(table.FirstSpawnDelay, table.PrewarmPerType, phases);
            EditorUtility.SetDirty(table);
            Debug.Log("[SpecialMeleeBuilder] wave table: Grave Brute replaces the old Brute; Plague Bloater + Frenzied Ghoul from 45 s");
            return true;
        }

        // ------------------------------------------------------------ Debug

        [MenuItem("Survivor Souls/Debug/Melee: Spawn Plague Bloater")]
        private static void SpawnBloater() => SpawnNearKnight(Bloater, 6f);

        [MenuItem("Survivor Souls/Debug/Melee: Spawn Frenzied Ghoul")]
        private static void SpawnGhoul() => SpawnNearKnight(Ghoul, 9f);

        [MenuItem("Survivor Souls/Debug/Melee: Spawn Grave Brute")]
        private static void SpawnBrute() => SpawnNearKnight(Brute, 6f);

        [MenuItem("Survivor Souls/Debug/Melee: Spawn Plague Bloater", true)]
        [MenuItem("Survivor Souls/Debug/Melee: Spawn Frenzied Ghoul", true)]
        [MenuItem("Survivor Souls/Debug/Melee: Spawn Grave Brute", true)]
        private static bool InPlay() => Application.isPlaying && Object.FindAnyObjectByType<EnemySpawner>() != null;

        private static void SpawnNearKnight(string id, float distance)
        {
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            var prefab = spawner != null ? spawner.FindType("Enemy_" + id) : null;
            var player = EnemyProjectiles.Current != null ? EnemyProjectiles.Current.Player : null;
            if (prefab == null || player == null)
            {
                Debug.LogWarning($"[SpecialMeleeBuilder] cannot spawn {id}: no spawner type or Knight");
                return;
            }
            spawner.SpawnAt(prefab, player.transform.position + new Vector3(0f, 0f, distance));
        }
    }
}
