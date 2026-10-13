using System;
using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.FX;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Builds the Realm 1 open world (snowy seaside shore): tile prefabs + the scene objects that stream them.
    /// Tiles are <see cref="TileSize"/> m squares, content in local [-6, 6]. At runtime WorldStreamer places
    /// and recycles them around the player with a new seed each run.
    /// Layout: playable rows -<see cref="SouthRows"/>..<see cref="ShoreRow"/>; the shore row holds beach edge tiles;
    /// the sea lies north (up the screen), cliffs close the other sides. One snow ground quad covers the land.
    /// Readability first: the play floor is plain, bright, low-contrast snow, so the dark Knight, enemies and
    /// pickups stand out; props stay sparse in the middle of tiles and dense only at the world edge.
    /// Add a variant: write a Fill method, add it to <see cref="Variants"/> (inland) or <see cref="ShoreVariants"/>.
    /// Mobile rules: per tile one mesh per material (snow ground, flagstone, snow stone, snow props, dirt decals,
    /// foam), few lights (only bonfire, camp and exit tiles have fire), no realtime shadows.
    /// </summary>
    public static class WorldBuilder
    {
        public const float TileSize = 12f;
        public const int HalfTiles = 5;   // playable width: 11 tiles = 132 m
        public const int ShoreRow = 1;    // tile row of the beach; north of it is sea
        public const int SouthRows = 4;   // playable rows south of the spawn row
        public const float ShoreLineLocalZ = 1f; // average water edge inside a shore tile (local z)
        public static float ShoreLineZ => ShoreRow * TileSize + ShoreLineLocalZ;
        private const float WaterlineOffset = 1.1f; // the beach slope meets the sea this far past the land edge
        private const string MeshDir = "Assets/_Project/Art/World";
        private const string PrefabDir = "Assets/_Project/Prefabs/World";
        private static readonly Vector3 Wind = new Vector3(0.8f, 0f, -0.6f).normalized; // sea wind: pines lean inland

        /// <summary>Content of one tile before it becomes a prefab.</summary>
        private sealed class Draft
        {
            public readonly MeshBuilder Props = new MeshBuilder(), Stone = new MeshBuilder(), Floor = new MeshBuilder(),
                Snow = new MeshBuilder(), Decals = new MeshBuilder(), Foam = new MeshBuilder();
            public readonly List<(Vector3 centre, Vector3 size, float yaw)> Blocks = new List<(Vector3, Vector3, float)>();
            public readonly List<(Vector3 pos, float scale, float intensity, float range)> Fires = new List<(Vector3, float, float, float)>();
            public readonly List<(Vector3 pos, float size)> Glows = new List<(Vector3, float)>();
            public readonly List<(Vector3 pos, float yaw)> Gates = new List<(Vector3, float)>(); // enemy entry points (KAN-29)
            public Vector3? Beacon;
            public System.Random Rng;
            public float Density = 1f;

            public int Count(int n) => Mathf.Max(0, Mathf.RoundToInt(n * Density));
        }

        private static readonly (string name, float weight, bool rotate, Action<Draft> fill)[] Variants =
        {
            ("Snowfield", 4f, true, FillSnowfield),
            ("Drifts", 2.4f, true, FillDrifts),
            ("Pines", 2f, true, FillPines),
            ("Ruins", 1.8f, true, FillRuins),
            ("Boulders", 1.5f, true, FillBoulders),
            ("Graveyard", 1f, true, FillGraveyard),
            ("Pools", 1f, true, FillPools),
            ("Bones", 0.8f, true, FillBones),
            ("Camp", 0.8f, true, FillCamp),
        };

        private static readonly (string name, float weight, Action<Draft> fill)[] ShoreVariants =
        {
            ("ShoreBeach", 3f, FillBeach),
            ("ShoreStacks", 1.5f, FillStacks),
            ("ShorePier", 1.1f, FillPier),
            ("ShoreWreck", 1f, FillWreck),
            ("ShoreIce", 1f, FillIceShelf),
        };

        private static RealmArt _art;
        private static GameArtKit _kit;

        /// <summary>Builds tile prefabs and the world objects in the open scene. Returns the streamer.</summary>
        public static WorldStreamer Build(GameArtKit kit, RealmArt art, Transform player, PlayerMover mover)
        {
            _kit = kit;
            _art = art;
            DeleteStaleTiles();

            var root = new GameObject("World");
            var streamer = root.AddComponent<WorldStreamer>();
            var so = new SerializedObject(streamer);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("tileSize").floatValue = TileSize;
            so.FindProperty("halfTiles").intValue = HalfTiles;
            so.FindProperty("shoreRow").intValue = ShoreRow;
            so.FindProperty("southRows").intValue = SouthRows;
            so.FindProperty("shoreLineLocalZ").floatValue = ShoreLineLocalZ;

            var variants = so.FindProperty("variants");
            variants.arraySize = Variants.Length;
            for (int i = 0; i < Variants.Length; i++)
            {
                var (name, weight, rotate, fill) = Variants[i];
                SetVariant(variants.GetArrayElementAtIndex(i), MakeTile(name, fill, 100 + i), weight, rotate);
            }
            var shore = so.FindProperty("shoreVariants");
            shore.arraySize = ShoreVariants.Length;
            for (int i = 0; i < ShoreVariants.Length; i++)
            {
                var (name, weight, fill) = ShoreVariants[i];
                SetVariant(shore.GetArrayElementAtIndex(i), MakeTile(name, fill, 200 + i), weight, false);
            }
            so.FindProperty("spawnTile").objectReferenceValue = MakeTile("Spawn", FillSpawn, 1);
            so.FindProperty("exitTile").objectReferenceValue = MakeTile("Exit", FillExit, 2);
            so.FindProperty("edgeTile").objectReferenceValue = MakeTile("Edge", FillEdge, 3);
            so.FindProperty("edgeShoreTile").objectReferenceValue = MakeTile("EdgeShore", FillHeadland, 4);
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildGround(root.transform, player);
            BuildSea(root.transform, player);
            BuildLandmarks(root.transform);
            BuildBounds(root.transform);
            BuildPlayerAids(player, mover, streamer);
            BuildWeather(root.transform, player);
            BuildFootprints(root.transform, player, mover);
            return streamer;
        }

        private static void SetVariant(SerializedProperty el, GameObject prefab, float weight, bool rotate)
        {
            el.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("weight").floatValue = weight;
            el.FindPropertyRelative("rotate").boolValue = rotate;
        }

        /// <summary>Removes tile prefabs and meshes of variants that no longer exist (old grass realm).</summary>
        private static void DeleteStaleTiles()
        {
            var names = new HashSet<string>(Variants.Select(v => v.name).Concat(ShoreVariants.Select(v => v.name))
                .Concat(new[] { "Spawn", "Exit", "Edge", "EdgeShore" }));
            foreach (var dir in new[] { PrefabDir, MeshDir })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("Tile_", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = System.IO.Path.GetFileNameWithoutExtension(path);
                    string tile = file.Substring(5).Split('_')[0];
                    bool staleGrass = file.EndsWith("_Grass");
                    if (staleGrass || !names.Contains(tile)) AssetDatabase.DeleteAsset(path);
                }
            }
        }

        // ------------------------------------------------------------ Helpers

        private static Vector3 RandomPoint(Draft d, float margin) =>
            new Vector3(PropKit.Range(d.Rng, -6f + margin, 6f - margin), 0f, PropKit.Range(d.Rng, -6f + margin, 6f - margin));

        /// <summary>Point on a ring around the tile centre: keeps the middle of the tile open for fights.</summary>
        private static Vector3 OuterPoint(Draft d, float minRadius = 3.2f, float margin = 0.6f)
        {
            for (int i = 0; i < 20; i++)
            {
                var p = RandomPoint(d, margin);
                if (new Vector2(p.x, p.z).magnitude >= minRadius) return p;
            }
            return new Vector3(4.5f, 0f, 4.5f);
        }

        /// <summary>Soft dark patch (dirty, trampled snow) on the ground.</summary>
        private static void Decal(Draft d, Vector3 c, Vector2 size, float yaw = 0f)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var inv = Quaternion.Inverse(rot);
            var hx = rot * new Vector3(size.x * 0.5f, 0f, 0f);
            var hz = rot * new Vector3(0f, 0f, size.y * 0.5f);
            var y = Vector3.up * 0.012f;
            var centre = new Vector3(c.x, 0f, c.z);
            d.Decals.Quad(centre - hx - hz + y, centre - hx + hz + y, centre + hx + hz + y, centre + hx - hz + y, (p, n) =>
            {
                var l = inv * (p - centre);
                return new Vector2(l.x / size.x + 0.5f, l.z / size.y + 0.5f);
            }, 0);
        }

        private static void Block(Draft d, Vector3 p, Vector3 size, float yaw = 0f) => d.Blocks.Add((p + Vector3.up * size.y * 0.5f, size, yaw));

        private static void Boulder(Draft d, Vector3 p, float size)
        {
            ShoreKit.Boulder(d.Props, p, d.Rng, size);
            Decal(d, p, Vector2.one * size * 3.2f, PropKit.Range(d.Rng, 0f, 180f));
            if (size > 0.55f) Block(d, p, new Vector3(size * 1.5f, 1.2f, size * 1.5f));
        }

        private static void Pine(Draft d, Vector3 p, float scale = 1f, bool? dead = null)
        {
            ShoreKit.Pine(d.Props, p, d.Rng, scale, dead ?? d.Rng.NextDouble() < 0.35, Wind);
            Block(d, p, new Vector3(0.6f, 2f, 0.6f));
        }

        private static void Tufts(Draft d, int n, float margin = 0.5f)
        {
            for (int i = 0; i < d.Count(n); i++) ShoreKit.DryTuft(d.Props, RandomPoint(d, margin), d.Rng, PropKit.Range(d.Rng, 0.18f, 0.35f));
        }

        private static void Pebbles(Draft d, int n)
        {
            for (int i = 0; i < d.Count(n); i++) ShoreKit.Pebbles(d.Props, RandomPoint(d, 0.6f), d.Rng);
        }

        // ------------------------------------------------------------ Inland variants

        private static void FillSnowfield(Draft d)
        {
            // Almost empty on purpose: the cleanest play floor.
            Tufts(d, 5);
            Pebbles(d, 2);
            if (d.Rng.NextDouble() < 0.5) Boulder(d, OuterPoint(d, 4f), PropKit.Range(d.Rng, 0.25f, 0.4f));
        }

        private static void FillDrifts(Draft d)
        {
            int n = 2 + d.Rng.Next(2);
            for (int i = 0; i < n; i++)
            {
                var p = OuterPoint(d, 3.5f, 1.8f);
                ShoreKit.Mound(d.Snow, p, new Vector2(PropKit.Range(d.Rng, 1.6f, 2.6f), PropKit.Range(d.Rng, 1.1f, 1.8f)), PropKit.Range(d.Rng, 0.35f, 0.7f), PropKit.Range(d.Rng, 0f, 180f));
            }
            Pine(d, OuterPoint(d, 4.2f), PropKit.Range(d.Rng, 0.8f, 1f), dead: true);
            ShoreKit.Stump(d.Props, OuterPoint(d, 3.5f), d.Rng);
            Tufts(d, 6);
        }

        private static void FillPines(Draft d)
        {
            var spots = new List<Vector3>();
            for (int tries = 0; tries < 40 && spots.Count < 4; tries++)
            {
                var p = OuterPoint(d, 2.8f, 1.1f);
                if (spots.TrueForAll(s => (s - p).magnitude > 3f)) spots.Add(p);
            }
            foreach (var p in spots) Pine(d, p, PropKit.Range(d.Rng, 0.7f, 0.95f)); // inside the play area: short, so they hide less of the fight
            for (int i = 0; i < d.Count(3); i++) ShoreKit.Shrub(d.Props, OuterPoint(d, 2.5f), d.Rng);
            ShoreKit.Stump(d.Props, OuterPoint(d, 3f), d.Rng);
            Tufts(d, 6);
        }

        private static void FillRuins(Draft d)
        {
            var a = new Vector3(PropKit.Range(d.Rng, -5f, -3.5f), 0f, PropKit.Range(d.Rng, -4.5f, -1.5f));
            var b = a + new Vector3(0f, 0f, PropKit.Range(d.Rng, 4.5f, 6.5f));
            PropKit.WallRun(d.Stone, a, b, d.Rng, 0.7f, 1.9f);
            AddWallBlock(d, a, b);
            var c = b + new Vector3(PropKit.Range(d.Rng, 3f, 5f), 0f, 0f);
            PropKit.WallRun(d.Stone, b + Vector3.right * 0.5f, c, d.Rng, 0.6f, 1.5f, 0.3f);
            AddWallBlock(d, b + Vector3.right * 0.5f, c);
            var arch = new Vector3(PropKit.Range(d.Rng, 2.5f, 3.5f), 0f, PropKit.Range(d.Rng, -4.5f, -3.5f));
            ShoreKit.Archway(d.Stone, arch, 0f, d.Rng);
            foreach (float s in new[] { -1f, 1f }) Block(d, arch + Vector3.right * (s * 1.3f), new Vector3(0.8f, 3f, 0.9f));
            if (d.Rng.NextDouble() < 0.6)
            {
                var statue = new Vector3(4.2f, 0f, 1.5f);
                ShoreKit.BrokenStatue(d.Stone, statue, PropKit.Range(d.Rng, -30f, 30f), d.Rng);
                Block(d, statue, new Vector3(1.4f, 2f, 1.4f));
            }
            for (int i = 0; i < 8; i++)
            {
                var p = Vector3.Lerp(a, b, (float)d.Rng.NextDouble()) + new Vector3(PropKit.Range(d.Rng, 0.6f, 1.6f), 0f, PropKit.Range(d.Rng, -0.6f, 0.6f));
                float s = PropKit.Range(d.Rng, 0.15f, 0.35f);
                d.Stone.Box(p + Vector3.up * s * 0.3f, new Vector3(s * 1.4f, s, s), Quaternion.Euler(PropKit.Range(d.Rng, -15f, 15f), PropKit.Range(d.Rng, 0f, 360f), 0f), PropKit.StoneUv(1.5f), 0);
            }
            PropKit.Banner(d.Props, arch + Vector3.right * 1.3f, Vector3.back, 3f);
            Decal(d, (a + b) * 0.5f + Vector3.right * 0.9f, new Vector2(2.4f, 6f));
            Tufts(d, 5);
        }

        private static void FillBoulders(Draft d)
        {
            int n = 3 + d.Rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float s = i == 0 ? PropKit.Range(d.Rng, 1.1f, 1.5f) : PropKit.Range(d.Rng, 0.3f, 0.9f);
                Boulder(d, i == 0 ? OuterPoint(d, 3.8f, 1.6f) : OuterPoint(d, 2.6f, 0.8f), s);
            }
            ShoreKit.Shrub(d.Props, OuterPoint(d, 3f), d.Rng);
            Pebbles(d, 3);
            Tufts(d, 4);
        }

        private static void FillGraveyard(Draft d)
        {
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < 4; i++)
            {
                if (d.Rng.NextDouble() < 0.2) continue;
                var p = new Vector3(-4.6f + i * 1.25f + PropKit.Range(d.Rng, -0.2f, 0.2f), 0f, 3.2f + row * 1.7f + PropKit.Range(d.Rng, -0.2f, 0.2f));
                ShoreKit.Grave(d.Props, p, PropKit.Range(d.Rng, -12f, 12f), d.Rng);
                Block(d, p, new Vector3(0.6f, 1f, 0.3f));
            }
            var rack = new Vector3(3.8f, 0f, -3.6f);
            ShoreKit.WeaponRack(d.Props, rack, PropKit.Range(d.Rng, -20f, 20f), d.Rng);
            Block(d, rack, new Vector3(1.8f, 1.2f, 0.5f));
            var lantern = new Vector3(-4.8f, 0f, -3.8f);
            d.Glows.Add((ShoreKit.LanternPost(d.Props, lantern, 90f), 0.9f));
            Block(d, lantern, new Vector3(0.3f, 2f, 0.3f));
            Pine(d, new Vector3(4.6f, 0f, 4.4f), 0.9f, dead: true);
            Tufts(d, 4);
        }

        private static void FillPools(Draft d)
        {
            int n = 2 + d.Rng.Next(2);
            for (int i = 0; i < n; i++)
            {
                var p = OuterPoint(d, 2.5f, 2f);
                ShoreKit.IcePlate(d.Props, p, PropKit.Range(d.Rng, 1.2f, 2f), d.Rng);
                for (int k = 0; k < 4; k++)
                {
                    float a = PropKit.Range(d.Rng, 0f, Mathf.PI * 2f);
                    ShoreKit.DryTuft(d.Props, p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PropKit.Range(d.Rng, 1.3f, 2f), d.Rng, PropKit.Range(d.Rng, 0.45f, 0.75f), reeds: true);
                }
            }
            Pebbles(d, 3);
            Tufts(d, 3);
        }

        private static void FillBones(Draft d)
        {
            var p = new Vector3(PropKit.Range(d.Rng, -3.5f, -2.5f), 0f, PropKit.Range(d.Rng, -1f, 1f));
            float yaw = PropKit.Range(d.Rng, -30f, 30f);
            ShoreKit.WhaleRibs(d.Props, p, yaw, d.Rng);
            Decal(d, p, new Vector2(3.6f, 6.5f), yaw);
            foreach (float s in new[] { -1f, 1f }) Block(d, p + Quaternion.Euler(0f, yaw, 0f) * new Vector3(s * 1.3f, 0f, 0f), new Vector3(0.4f, 1.5f, 4f), yaw);
            var boat = new Vector3(3.6f, 0f, PropKit.Range(d.Rng, 2.5f, 4f));
            ShoreKit.Rowboat(d.Props, boat, PropKit.Range(d.Rng, 0f, 360f), d.Rng);
            Block(d, boat, new Vector3(1.4f, 0.8f, 1.4f));
            ShoreKit.Driftwood(d.Props, OuterPoint(d, 3f), PropKit.Range(d.Rng, 0f, 180f), PropKit.Range(d.Rng, 1.6f, 2.6f), d.Rng);
            Pebbles(d, 3);
        }

        private static void FillCamp(Draft d)
        {
            var fire = new Vector3(0f, 0f, 2.6f);
            ShoreKit.Bonfire(d.Props, fire, d.Rng);
            Block(d, fire, new Vector3(1.4f, 1f, 1.4f));
            d.Fires.Add((fire + Vector3.up * 0.25f, 0.9f, 5.5f, 8f));
            Decal(d, fire, new Vector2(4.2f, 4.2f));
            for (int i = 0; i < 2; i++)
            {
                var b = new Vector3(-4.4f + i * 0.75f, 0f, 4.2f - i * 0.2f);
                PropKit.Barrel(d.Props, b, PropKit.Range(d.Rng, 0f, 360f));
                Block(d, b, new Vector3(0.7f, 1f, 0.7f));
            }
            PropKit.Crate(d.Props, new Vector3(4.2f, 0f, 3.6f), 0.75f, 18f);
            PropKit.Crate(d.Props, new Vector3(4.2f, 0.75f, 3.6f), 0.5f, 40f);
            Block(d, new Vector3(4.2f, 0f, 3.6f), new Vector3(1f, 1.2f, 1f));
            var net = new Vector3(-3.8f, 0f, -3.8f);
            ShoreKit.NetFrame(d.Props, net, 15f, d.Rng);
            Block(d, net, new Vector3(2.4f, 1.8f, 0.4f), 15f);
            ShoreKit.Anchor(d.Props, new Vector3(4.3f, 0f, -3.4f), PropKit.Range(d.Rng, 0f, 360f));
            var gibbet = new Vector3(-4.8f, 0f, 1.2f);
            ShoreKit.Gibbet(d.Props, gibbet, 90f);
            Block(d, gibbet, new Vector3(0.4f, 3f, 0.4f));
            ShoreKit.Driftwood(d.Props, new Vector3(1.8f, 0f, 4.6f), 80f, 2f, d.Rng);
            Tufts(d, 3);
        }

        private static void FillSpawn(Draft d)
        {
            // The run starts at the bonfire (menu art): trampled snow, lanterns, a few supplies.
            var fire = new Vector3(-2.3f, 0f, 1.6f); // beside the start spot: the way north to the sea stays open
            ShoreKit.Bonfire(d.Props, fire, d.Rng);
            Block(d, fire, new Vector3(1.4f, 1f, 1.4f));
            d.Fires.Add((fire + Vector3.up * 0.25f, 1f, 6f, 9f));
            Decal(d, fire, new Vector2(4.5f, 4.5f));
            foreach (float s in new[] { -1f, 1f })
            {
                var lp = new Vector3(s * 4.2f, 0f, 3.8f);
                d.Glows.Add((ShoreKit.LanternPost(d.Props, lp, s > 0f ? 270f : 90f), 1f));
                Block(d, lp, new Vector3(0.3f, 2f, 0.3f));
            }
            PropKit.Crate(d.Props, new Vector3(-4.3f, 0f, -3.6f), 0.7f, 10f);
            PropKit.Barrel(d.Props, new Vector3(-3.4f, 0f, -4.2f), 40f);
            Block(d, new Vector3(-3.9f, 0f, -3.9f), new Vector3(1.6f, 1f, 1.2f));
            ShoreKit.Driftwood(d.Props, new Vector3(3.8f, 0f, -3.8f), 30f, 2.2f, d.Rng);
            Tufts(d, 3, 0.6f);
        }

        private static void FillExit(Draft d)
        {
            d.Floor.Tint = new Color(1f, 1f, 1f, 0.45f); // drifted snow over the flagstones
            PropKit.Plaza(d.Floor, Vector3.zero, 4.6f, 8, 22.5f);
            var gate = new Vector3(0f, 0f, 2.6f);
            foreach (var flame in PropKit.DoorGate(d.Stone, d.Props, gate, d.Rng)) d.Fires.Add((flame, 0.6f, 4.5f, 6.5f));
            d.Blocks.Add((gate + new Vector3(0f, 1.5f, 0.05f), new Vector3(8f, 3f, 1.3f), 0f));
            d.Beacon = gate + new Vector3(0f, 5.4f, -0.4f);
            foreach (float s in new[] { -1f, 1f }) Boulder(d, new Vector3(s * 5f, 0f, 3.6f), PropKit.Range(d.Rng, 0.6f, 0.9f));
            Tufts(d, 3);
        }

        private static void FillEdge(Draft d)
        {
            // Rock outcrops, high drifts and pines just outside the bounds: reads as "the world ends here".
            ShoreKit.Cliff(d.Props, new Vector3(PropKit.Range(d.Rng, -1.5f, 1.5f), 0f, PropKit.Range(d.Rng, -1f, 1f)), PropKit.Range(d.Rng, 0f, 180f), PropKit.Range(d.Rng, 5f, 7f), PropKit.Range(d.Rng, 2.4f, 3.4f), d.Rng);
            for (int i = 0; i < 2; i++)
                ShoreKit.Mound(d.Snow, RandomPoint(d, 2.5f), new Vector2(PropKit.Range(d.Rng, 2.5f, 3.5f), PropKit.Range(d.Rng, 2f, 3f)), PropKit.Range(d.Rng, 0.9f, 1.6f), PropKit.Range(d.Rng, 0f, 180f));
            for (int i = 0; i < 5; i++) ShoreKit.Boulder(d.Props, RandomPoint(d, 1f), d.Rng, PropKit.Range(d.Rng, 1f, 2f));
            for (int i = 0; i < 4; i++) ShoreKit.Pine(d.Props, RandomPoint(d, 0.8f), d.Rng, PropKit.Range(d.Rng, 1f, 1.4f), d.Rng.NextDouble() < 0.3, Wind);
        }

        // ------------------------------------------------------------ Shore variants (never rotated)

        /// <summary>
        /// Water edge in local z as a function of local x. Equals <see cref="ShoreLineLocalZ"/> at x = +-6, so
        /// neighbouring shore tiles always join; wiggles only seaward-ish in between.
        /// </summary>
        private static Func<float, float> EdgeShape(Draft d, float amplitude, float bulge = 0f)
        {
            float f1 = PropKit.Range(d.Rng, 0.35f, 0.6f), f2 = PropKit.Range(d.Rng, 0.9f, 1.4f);
            float p1 = PropKit.Range(d.Rng, 0f, 6.28f), p2 = PropKit.Range(d.Rng, 0f, 6.28f);
            return x =>
            {
                float window = Mathf.Sin(Mathf.PI * Mathf.Clamp01((x + 6f) / 12f));
                float wiggle = Mathf.Sin(x * f1 + p1) * 0.65f + Mathf.Sin(x * f2 + p2) * 0.35f;
                return ShoreLineLocalZ + window * (wiggle * amplitude + 0.3f * amplitude) + window * window * bulge;
            };
        }

        /// <summary>
        /// Snow land up to the edge, a slope down under the sea, a foam ribbon at the waterline, tide-line debris
        /// and a collider wall so the player cannot enter the water.
        /// </summary>
        private static void Shore(Draft d, Func<float, float> edge, bool colliders = true)
        {
            const int nx = 24, landRows = 2, slopeRows = 6;
            d.Snow.Surface(nx, landRows + slopeRows, (i, j) =>
            {
                float x = -6f + 12f * i / nx;
                float e = edge(x);
                if (j <= landRows) return new Vector3(x, 0f, Mathf.Lerp(-6f, e, j / (float)landRows));
                float t = (j - landRows) / (float)slopeRows;
                return new Vector3(x, -1.15f * Mathf.Pow(t, 1.25f), e + t * 2.4f);
            }, (p, n) => Vector2.zero);

            d.Foam.Surface(nx, 2, (i, j) =>
            {
                float x = -6f + 12f * i / nx;
                return new Vector3(x, ShoreKit.SeaLevel + 0.06f, edge(x) + 0.75f + j * 1.25f);
            }, (p, n) => new Vector2(0f, Mathf.Clamp01((p.z - edge(p.x) - 0.75f) / 2.5f)));

            // Tide line: thin dark streaks of washed-up weed just above the water edge.
            for (float x = -5.5f; x < 5.6f; x += PropKit.Range(d.Rng, 1.2f, 2.4f))
                Decal(d, new Vector3(x, 0f, edge(x) - 0.25f), new Vector2(PropKit.Range(d.Rng, 1.2f, 2.2f), 0.35f), PropKit.Range(d.Rng, -8f, 8f));

            if (!colliders) return;
            const float step = 1.5f;
            for (float x = -6f; x < 5.99f; x += step)
            {
                float e0 = edge(x), e1 = edge(x + step);
                float len = Mathf.Sqrt(step * step + (e1 - e0) * (e1 - e0));
                var normal = new Vector3(-(e1 - e0) / len, 0f, step / len);
                var mid = new Vector3(x + step * 0.5f, 0f, (e0 + e1) * 0.5f) + normal * 0.5f;
                d.Blocks.Add((mid + Vector3.up, new Vector3(len + 0.2f, 2f, 1f), -Mathf.Atan2(e1 - e0, step) * Mathf.Rad2Deg));
            }
        }

        private static void ShoreRocks(Draft d, Func<float, float> edge, int count, float minSize, float maxSize)
        {
            for (int i = 0; i < count; i++)
            {
                float x = PropKit.Range(d.Rng, -5.5f, 5.5f);
                ShoreKit.Boulder(d.Props, new Vector3(x, -0.25f, edge(x) + PropKit.Range(d.Rng, 0.2f, 1.6f)), d.Rng, PropKit.Range(d.Rng, minSize, maxSize));
            }
        }

        private static void FloatingIce(Draft d, Func<float, float> edge, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float x = PropKit.Range(d.Rng, -5.5f, 5.5f);
                ShoreKit.IceChunk(d.Props, new Vector3(x, ShoreKit.SeaLevel - 0.05f, edge(x) + PropKit.Range(d.Rng, 2.5f, 7f)), d.Rng, PropKit.Range(d.Rng, 0.4f, 1f));
            }
        }

        private static void FillBeach(Draft d)
        {
            var edge = EdgeShape(d, 0.6f);
            Shore(d, edge);
            for (int i = 0; i < 2; i++)
            {
                float x = PropKit.Range(d.Rng, -4.5f, 4.5f);
                ShoreKit.Driftwood(d.Props, new Vector3(x, 0f, edge(x) - PropKit.Range(d.Rng, 0.8f, 2f)), PropKit.Range(d.Rng, 60f, 120f), PropKit.Range(d.Rng, 1.8f, 3f), d.Rng);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = PropKit.Range(d.Rng, -5.5f, 5.5f);
                ShoreKit.DryTuft(d.Props, new Vector3(x, 0f, edge(x) - PropKit.Range(d.Rng, 0.2f, 0.6f)), d.Rng, PropKit.Range(d.Rng, 0.5f, 0.8f), reeds: true);
            }
            ShoreRocks(d, edge, 2, 0.3f, 0.6f);
            FloatingIce(d, edge, 2);
            Pebbles(d, 3);
            Tufts(d, 3, 1f);
        }

        private static void FillStacks(Draft d)
        {
            var edge = EdgeShape(d, 1.1f);
            Shore(d, edge);
            ShoreRocks(d, edge, 6, 0.5f, 1.1f);
            ShoreKit.SeaStack(d.Props, new Vector3(PropKit.Range(d.Rng, -3.5f, -1.5f), ShoreKit.SeaLevel, edge(-2f) + PropKit.Range(d.Rng, 5f, 7f)), d.Rng, PropKit.Range(d.Rng, 5f, 7f));
            ShoreKit.SeaStack(d.Props, new Vector3(PropKit.Range(d.Rng, 2f, 4f), ShoreKit.SeaLevel, edge(3f) + PropKit.Range(d.Rng, 3.5f, 5f)), d.Rng, PropKit.Range(d.Rng, 3f, 4.5f));
            var rock = new Vector3(PropKit.Range(d.Rng, -4f, 4f), 0f, PropKit.Range(d.Rng, -4.5f, -3f));
            Boulder(d, rock, PropKit.Range(d.Rng, 0.9f, 1.3f));
            Tufts(d, 3, 1f);
        }

        private static void FillPier(Draft d)
        {
            var edge = EdgeShape(d, 0.4f);
            Shore(d, edge);
            float px = PropKit.Range(d.Rng, -1.5f, 1.5f);
            var start = new Vector3(px, 0f, edge(px) - 1.6f);
            d.Glows.Add((ShoreKit.Pier(d.Props, start, 9f, d.Rng), 1f));
            Block(d, start + new Vector3(0f, 0f, 1f), new Vector3(2.2f, 1.2f, 2f)); // the pier is not walkable: stop at its foot
            d.Gates.Add((start + new Vector3(0f, 0f, -1.8f), 180f)); // the drowned come off the pier and walk inland
            ShoreKit.Rowboat(d.Props, new Vector3(px + 2.4f, ShoreKit.SeaLevel + 0.1f, edge(px + 2.4f) + 3.5f), 10f, d.Rng);
            foreach (float s in new[] { -1f, 1f })
            {
                var post = new Vector3(px + s * 2.2f, 0f, edge(px + s * 2.2f) - 0.6f);
                d.Props.Lathe(post, Quaternion.identity, new[] { new Vector2(0f, 0.13f), new Vector2(0.8f, 0.11f) }, PropKit.Pal(Swatch.Bark), 0, 7, capTop: true); // mooring post
            }
            PropKit.Crate(d.Props, new Vector3(px - 2.8f, 0f, edge(px - 2.8f) - 2.4f), 0.7f, 25f);
            Block(d, new Vector3(px - 2.8f, 0f, edge(px - 2.8f) - 2.4f), new Vector3(0.9f, 1f, 0.9f));
            FloatingIce(d, edge, 1);
            Tufts(d, 3, 1f);
        }

        private static void FillWreck(Draft d)
        {
            var edge = EdgeShape(d, 0.5f);
            Shore(d, edge);
            float wx = PropKit.Range(d.Rng, -1f, 1f);
            var hull = new Vector3(wx, 0f, edge(wx) - 0.8f);
            ShoreKit.Shipwreck(d.Props, hull, PropKit.Range(d.Rng, 15f, 35f), d.Rng);
            Decal(d, hull, new Vector2(4.5f, 7f), 25f);
            Block(d, hull + new Vector3(0f, 0f, -0.5f), new Vector3(3.6f, 2f, 3.4f), 25f);
            d.Gates.Add((hull + new Vector3(wx < 0f ? 3.2f : -3.2f, 0f, -2.6f), 180f)); // crew climbing out of the wreck
            ShoreKit.Driftwood(d.Props, new Vector3(wx - 3.6f, 0f, edge(wx - 3.6f) - 1.4f), 100f, 2.2f, d.Rng);
            ShoreRocks(d, edge, 2, 0.3f, 0.6f);
            Tufts(d, 2, 1f);
        }

        private static void FillIceShelf(Draft d)
        {
            var edge = EdgeShape(d, 0.8f);
            Shore(d, edge);
            for (float x = -5.5f; x < 5.6f; x += PropKit.Range(d.Rng, 1.4f, 2.4f))
                ShoreKit.IceChunk(d.Props, new Vector3(x, -0.2f, edge(x) + PropKit.Range(d.Rng, 0.6f, 1.6f)), d.Rng, PropKit.Range(d.Rng, 0.8f, 1.4f));
            FloatingIce(d, edge, 5);
            for (int i = 0; i < 2; i++) ShoreKit.IcePlate(d.Props, new Vector3(PropKit.Range(d.Rng, -4f, 4f), 0f, PropKit.Range(d.Rng, -4f, -1.5f)), PropKit.Range(d.Rng, 0.9f, 1.4f), d.Rng);
            Tufts(d, 2, 1f);
        }

        private static void FillHeadland(Draft d)
        {
            // Shore row, outside the bounds: cliffs run out into the sea and close the beach.
            var edge = EdgeShape(d, 0.5f, bulge: 3.5f);
            Shore(d, edge, colliders: false);
            ShoreKit.Cliff(d.Props, new Vector3(0f, 0f, edge(0f) - 0.5f), 75f, 7f, 3.6f, d.Rng);
            ShoreKit.Cliff(d.Props, new Vector3(PropKit.Range(d.Rng, -2f, 2f), 0f, -3f), 10f, 8f, 3f, d.Rng);
            ShoreRocks(d, edge, 5, 0.7f, 1.4f);
            ShoreKit.SeaStack(d.Props, new Vector3(1.5f, ShoreKit.SeaLevel, edge(1.5f) + 4f), d.Rng, 4.5f);
            for (int i = 0; i < 3; i++) ShoreKit.Pine(d.Props, RandomPoint(d, 1f) + Vector3.back * 1.5f, d.Rng, 1.2f, false, Wind);
        }

        private static void AddWallBlock(Draft d, Vector3 a, Vector3 b)
        {
            var dir = b - a;
            float yaw = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
            d.Blocks.Add(((a + b) * 0.5f + Vector3.up, new Vector3(0.9f, 2f, dir.magnitude + 0.2f), yaw));
        }

        // ------------------------------------------------------------ Tile prefab

        private static GameObject MakeTile(string name, Action<Draft> fill, int seed)
        {
            var d = new Draft { Rng = new System.Random(seed * 7919), Density = _art.Look.propDensity };
            d.Stone.Tint = new Color(1f, 1f, 1f, 0.75f); // masonry: snow on most ledges, some bare stone
            fill(d);

            var root = new GameObject($"Tile_{name}");
            try
            {
                Part(root.transform, name, "Snow", d.Snow, _art.Snow);
                Part(root.transform, name, "Floor", d.Floor, _art.SnowFloor);
                Part(root.transform, name, "Stone", d.Stone, _art.SnowStone);
                Part(root.transform, name, "Props", d.Props, _art.SnowProps);
                Part(root.transform, name, "Decals", d.Decals, _art.Decal);
                Part(root.transform, name, "Foam", d.Foam, _art.Foam);
                foreach (var (pos, scale, intensity, range) in d.Fires) PropKit.Fire(root.transform, pos, _kit, scale, intensity, range);
                foreach (var (pos, size) in d.Glows) Glow(root.transform, pos, size, new Color(1f, 0.72f, 0.38f, 0.75f));
                if (d.Beacon.HasValue) AddBeacon(root.transform, d.Beacon.Value);
                foreach (var (pos, yaw) in d.Gates)
                {
                    var gate = new GameObject("SpawnGate");
                    gate.transform.SetParent(root.transform, false);
                    gate.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                    gate.AddComponent<SpawnGate>();
                }

                if (d.Blocks.Count > 0)
                {
                    var cols = new GameObject("Colliders").transform;
                    cols.SetParent(root.transform, false);
                    foreach (var (centre, size, yaw) in d.Blocks)
                    {
                        var go = new GameObject("Block");
                        go.transform.SetParent(cols, false);
                        go.transform.SetLocalPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
                        go.AddComponent<BoxCollider>().size = size;
                    }
                }

                EditorAssetUtil.EnsureFolder(PrefabDir);
                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Tile_{name}.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Part(Transform root, string tile, string part, MeshBuilder mb, Material mat)
        {
            if (mb.VertexCount == 0) return;
            var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/Tile_{tile}_{part}.asset");
            mb.WriteTo(mesh);
            EditorUtility.SetDirty(mesh);
            var go = new GameObject(part);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
        }

        /// <summary>
        /// Warm lantern glow: one long-lived additive billboard particle (faces the camera from any tile rotation).
        /// Cheaper than a point light, and it does not use one of the 4 per-object light slots.
        /// </summary>
        private static void Glow(Transform parent, Vector3 localPos, float size, Color color)
        {
            var ps = PropKit.Particles("Glow", parent, _art.Glow);
            ps.transform.localPosition = localPos;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 600f; // re-burst each loop is capped by maxParticles = 1
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = 1;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var shape = ps.shape;
            shape.enabled = false;
        }

        // ------------------------------------------------------------ Gold arrow (objective + beacon)

        private static Mesh ArrowMesh()
        {
            var mb = new MeshBuilder();
            MeshBuilder.UvMapper uv = (p, n) => Vector2.zero;
            mb.Box(new Vector3(0f, 0.02f, -0.18f), new Vector3(0.2f, 0.04f, 0.62f), Quaternion.identity, uv, 0);
            var y = Vector3.up * 0.04f;
            mb.Quad(new Vector3(-0.36f, 0f, 0.1f) + y, new Vector3(0f, 0f, 0.62f) + y, new Vector3(0.36f, 0f, 0.1f) + y, new Vector3(0.36f, 0f, 0.1f) + y, uv, 0);
            var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/GoldArrow.asset");
            mb.WriteTo(mesh);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>Gold arrow with a dark rim under it, so it reads on white snow as well as on dark stone.</summary>
        private static GameObject ArrowObject(string name, Transform parent, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var mesh = ArrowMesh();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), _kit.Gold);
            var rim = new GameObject("Rim");
            rim.transform.SetParent(go.transform, false);
            rim.transform.SetLocalPositionAndRotation(new Vector3(0f, -0.012f, -0.03f), Quaternion.identity);
            rim.transform.localScale = new Vector3(1.32f, 1f, 1.22f);
            rim.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(rim.AddComponent<MeshRenderer>(), _kit.GoldRim);
            return go;
        }

        private static void AddBeacon(Transform tile, Vector3 pos)
        {
            var holder = new GameObject("Exit Beacon").transform;
            holder.SetParent(tile, false);
            holder.localPosition = pos;
            holder.gameObject.AddComponent<FloatBob>();
            var arrow = ArrowObject("Arrow", holder, 2.2f);
            arrow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // tip down at the door
        }

        // ------------------------------------------------------------ Scene objects

        /// <summary>
        /// One snow quad for all land: follows the player in X only; its north edge stops where the shore tiles
        /// start, so it never covers the sea. World-space snow shader: no swimming, no seams with tile meshes.
        /// </summary>
        private static void BuildGround(Transform root, Transform player)
        {
            const float width = 144f, depth = 120f;
            float north = ShoreRow * TileSize - TileSize * 0.5f;
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0f, north - depth * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(width, depth, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = _kit.Quad;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), _art.Snow);
            Follow(go, player, 12f, 0f, north - depth * 0.5f);

            var floor = new GameObject("Ground Collider");
            floor.transform.SetParent(root, false);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(400f, 1f, 400f);
        }

        private static void Follow(GameObject go, Transform target, float grid, float y, float? fixedZ)
        {
            var snap = new SerializedObject(go.AddComponent<SnapFollow>());
            snap.FindProperty("target").objectReferenceValue = target;
            snap.FindProperty("grid").floatValue = grid;
            snap.FindProperty("y").floatValue = y;
            snap.FindProperty("followZ").boolValue = !fixedZ.HasValue;
            snap.FindProperty("fixedZ").floatValue = fixedZ ?? 0f;
            snap.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Sea: 2 m grid (vertex waves), 72 m wide, follows the player in X on its grid (waves are world-space).</summary>
        private static void BuildSea(Transform root, Transform player)
        {
            const float width = 72f, depth = 76f, cell = 2f;
            float z0 = ShoreRow * TileSize - TileSize * 0.5f;
            int nx = Mathf.RoundToInt(width / cell), nz = Mathf.RoundToInt(depth / cell);
            var mb = new MeshBuilder();
            mb.Surface(nx, nz, (i, j) => new Vector3(-width * 0.5f + i * cell, 0f, z0 + j * cell), (p, n) => Vector2.zero);
            var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/Sea.asset");
            mb.WriteTo(mesh);
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.up * 1f); // room for the waves
            EditorUtility.SetDirty(mesh);

            _art.Sea.SetFloat("_ShoreZ", ShoreLineZ + WaterlineOffset);
            _art.Sea.SetFloat("_SeaLevel", ShoreKit.SeaLevel);
            EditorUtility.SetDirty(_art.Sea);

            var go = new GameObject("Sea");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), _art.Sea);
            Follow(go, player, cell, ShoreKit.SeaLevel, 0f);
        }

        /// <summary>Ruined lighthouse on an islet off the spawn beach (hero landmark) and a ghost ship far out.</summary>
        private static void BuildLandmarks(Transform root)
        {
            var rng = new System.Random(4242);
            var props = new MeshBuilder();
            var stone = new MeshBuilder();
            var lighthousePos = new Vector3(11f, ShoreKit.SeaLevel, ShoreLineZ + 15f);
            var lamp = ShoreKit.Lighthouse(props, stone, lighthousePos, rng);
            ShoreKit.SeaStack(props, lighthousePos + new Vector3(-5.5f, 0f, 3f), rng, 3.5f);
            ShoreKit.GhostShip(props, new Vector3(-16f, ShoreKit.SeaLevel, ShoreLineZ + 34f));

            var go = new GameObject("Landmarks");
            go.transform.SetParent(root, false);
            Part(go.transform, "Landmarks", "Props", props, _art.SnowProps);
            Part(go.transform, "Landmarks", "Stone", stone, _art.SnowStone);
            Glow(go.transform, lamp, 2.6f, new Color(1f, 0.78f, 0.45f, 0.85f));
        }

        private static void BuildBounds(Transform root)
        {
            float half = (HalfTiles + 0.5f) * TileSize;
            float south = -(SouthRows + 0.5f) * TileSize;
            float north = ShoreLineZ + 3f; // backup: shore tile colliders already hold the water edge
            float depth = north - south, midZ = (north + south) * 0.5f;
            var parent = new GameObject("Bounds").transform;
            parent.SetParent(root, false);
            (Vector3 c, Vector3 s)[] walls =
            {
                (new Vector3(0f, 2f, north + 1f), new Vector3(half * 2f + 4f, 4f, 2f)),
                (new Vector3(0f, 2f, south - 1f), new Vector3(half * 2f + 4f, 4f, 2f)),
                (new Vector3(half + 1f, 2f, midZ), new Vector3(2f, 4f, depth + 4f)),
                (new Vector3(-half - 1f, 2f, midZ), new Vector3(2f, 4f, depth + 4f)),
            };
            foreach (var (c, s) in walls)
            {
                var go = new GameObject("Bound");
                go.transform.SetParent(parent, false);
                go.transform.position = c;
                go.AddComponent<BoxCollider>().size = s;
            }
        }

        /// <summary>Under-foot ring, move-direction ring and the gold arrow to the exit, all children of the player.</summary>
        private static void BuildPlayerAids(Transform player, PlayerMover mover, WorldStreamer world)
        {
            var ring = Decal("Hero Ring", player, _kit.HeroRing, 1.25f);
            ring.transform.localPosition = new Vector3(0f, 0.035f, 0f);

            var markerGo = Decal("Direction Marker", player, _kit.DirectionMarker, 0.6f);
            var marker = new SerializedObject(player.gameObject.AddComponent<DirectionMarker>());
            marker.FindProperty("mover").objectReferenceValue = mover;
            marker.FindProperty("marker").objectReferenceValue = markerGo.GetComponent<MeshRenderer>();
            marker.ApplyModifiedPropertiesWithoutUndo();

            var arrow = ArrowObject("Objective Arrow", player, 1.3f);
            var obj = new SerializedObject(player.gameObject.AddComponent<ObjectiveArrow>());
            obj.FindProperty("world").objectReferenceValue = world;
            obj.FindProperty("arrow").objectReferenceValue = arrow.transform;
            obj.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Decal(string name, Transform parent, Material mat, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = _kit.Quad;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
            return go;
        }

        /// <summary>Light snowfall around the player and low sea mist along the shore. Both faint (hero stays clear).</summary>
        private static void BuildWeather(Transform root, Transform player)
        {
            var look = _art.Look;
            var snow = new GameObject("Snowfall");
            snow.transform.SetParent(root, false);
            Follow(snow, player, 1f, 7f, null);
            var ps = PropKit.Particles("Emitter", snow.transform, _art.Snowflake);
            ps.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, 6f), Quaternion.Euler(90f, 0f, 0f)); // emits down
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.55f), new Color(0.9f, 0.94f, 1f, 0.8f));
            main.maxParticles = look.snowfallMax;
            var emission = ps.emission;
            emission.rateOverTime = look.snowfallRate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(20f, 34f, 0.5f); // local XY = world XZ (emitter faces down)
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, -0.15f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var mist = new GameObject("Sea Mist");
            mist.transform.SetParent(root, false);
            Follow(mist, player, 2f, 0.3f, ShoreLineZ + 3f);
            var mp = PropKit.Particles("Emitter", mist.transform, _art.SeaMist);
            mp.transform.localRotation = Quaternion.identity;
            main = mp.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 13f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.95f, 0.98f, look.seaMistAlpha * 0.6f), new Color(0.95f, 0.97f, 1f, look.seaMistAlpha));
            main.maxParticles = 24;
            emission = mp.emission;
            emission.rateOverTime = 2.2f;
            shape = mp.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 0.3f, 5f);
            var col = mp.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.3f), (1f, 0.7f), (0f, 1f) });
            mp.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        /// <summary>Boot prints behind the Knight: one world-space particle system, prints fade out in a few seconds.</summary>
        private static void BuildFootprints(Transform root, Transform player, PlayerMover mover)
        {
            var holder = new GameObject("Footprints");
            holder.transform.SetParent(root, false);
            var ps = PropKit.Particles("Prints", holder.transform, _art.Footprint);
            ps.transform.localRotation = Quaternion.identity;
            var main = ps.main;
            main.prewarm = false;
            main.playOnAwake = true;
            main.startLifetime = 4.5f;
            main.startSpeed = 0f;
            main.startSize = 0.3f;
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0.8f, 0.6f), (0f, 1f) });
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sortingFudge = 10f; // under other transparent things (ring, arrow)

            var prints = new SerializedObject(player.gameObject.AddComponent<SnowFootprints>());
            prints.FindProperty("mover").objectReferenceValue = mover;
            prints.FindProperty("prints").objectReferenceValue = ps;
            prints.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
