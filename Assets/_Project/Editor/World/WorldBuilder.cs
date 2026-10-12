using System;
using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Builds the open world: tile prefabs (variants) + the scene objects that stream them.
    /// Tiles are <see cref="TileSize"/> m squares, content in local [-6, 6]. At runtime WorldStreamer places
    /// and recycles them around the player with a new seed each run. One big ground quad follows the player.
    /// Add a variant: write a Fill method, add it to <see cref="Variants"/> with a weight.
    /// Mobile rules: per tile one mesh per material (palette props, stone masonry, flagstone floor), few lights
    /// (only shrine, camp, spawn and exit tiles have fire), no realtime shadows.
    /// </summary>
    public static class WorldBuilder
    {
        public const float TileSize = 12f;
        public const int HalfTiles = 5; // playable world: 11 x 11 tiles = 132 m square
        private const string MeshDir = "Assets/_Project/Art/World";
        private const string PrefabDir = "Assets/_Project/Prefabs/World";

        /// <summary>Content of one tile before it becomes a prefab.</summary>
        private sealed class Draft
        {
            public readonly MeshBuilder Props = new MeshBuilder(), Stone = new MeshBuilder(), Floor = new MeshBuilder();
            public readonly List<(Vector3 centre, Vector3 size, float yaw)> Blocks = new List<(Vector3, Vector3, float)>();
            public readonly List<(Vector3 pos, float scale, float intensity, float range)> Fires = new List<(Vector3, float, float, float)>();
            public bool Rune;
            public Vector3? Beacon;
            public System.Random Rng;
        }

        private static readonly (string name, float weight, bool rotate, Action<Draft> fill)[] Variants =
        {
            ("Meadow", 4f, true, FillMeadow),
            ("Grove", 3f, true, FillGrove),
            ("Ruins", 2.2f, true, FillRuins),
            ("Road", 1.6f, true, FillRoad),
            ("Rocks", 1.6f, true, FillRocks),
            ("Camp", 0.9f, true, FillCamp),
            ("Shrine", 0.8f, false, FillShrine),
        };

        /// <summary>Builds tile prefabs and the world objects in the open scene. Returns the streamer.</summary>
        public static WorldStreamer Build(GameArtKit kit, Transform player, PlayerMover mover)
        {
            var root = new GameObject("World");
            var streamer = root.AddComponent<WorldStreamer>();
            var so = new SerializedObject(streamer);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("tileSize").floatValue = TileSize;
            so.FindProperty("halfTiles").intValue = HalfTiles;

            var variants = so.FindProperty("variants");
            variants.arraySize = Variants.Length;
            for (int i = 0; i < Variants.Length; i++)
            {
                var (name, weight, rotate, fill) = Variants[i];
                var el = variants.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("prefab").objectReferenceValue = MakeTile(name, fill, kit, 100 + i);
                el.FindPropertyRelative("weight").floatValue = weight;
                el.FindPropertyRelative("rotate").boolValue = rotate;
            }
            so.FindProperty("spawnTile").objectReferenceValue = MakeTile("Spawn", FillSpawn, kit, 1);
            so.FindProperty("exitTile").objectReferenceValue = MakeTile("Exit", FillExit, kit, 2);
            so.FindProperty("edgeTile").objectReferenceValue = MakeTile("Edge", FillEdge, kit, 3);
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildGround(root.transform, kit, player);
            BuildBounds(root.transform);
            BuildPlayerAids(kit, player, mover, streamer);
            BuildMist(root.transform, kit, player);
            return streamer;
        }

        // ------------------------------------------------------------ Variants

        private static void Scatter(Draft d, int tufts, float margin = 0.3f)
        {
            for (int i = 0; i < tufts; i++)
                PropKit.Tuft(d.Props, RandomPoint(d, margin), d.Rng, PropKit.Range(d.Rng, 0.18f, 0.4f));
        }

        private static Vector3 RandomPoint(Draft d, float margin) =>
            new Vector3(PropKit.Range(d.Rng, -6f + margin, 6f - margin), 0f, PropKit.Range(d.Rng, -6f + margin, 6f - margin));

        private static void FillMeadow(Draft d)
        {
            Scatter(d, 28);
            for (int i = 0; i < 3; i++) PropKit.Flowers(d.Props, RandomPoint(d, 1f), d.Rng);
            for (int i = 0; i < 2; i++) PropKit.Rock(d.Props, RandomPoint(d, 1f), d.Rng, PropKit.Range(d.Rng, 0.2f, 0.4f));
        }

        private static void FillGrove(Draft d)
        {
            var spots = new List<Vector3>();
            for (int tries = 0; tries < 30 && spots.Count < 4; tries++)
            {
                var p = RandomPoint(d, 1.2f);
                if (spots.TrueForAll(s => (s - p).magnitude > 3.2f)) spots.Add(p);
            }
            foreach (var p in spots)
            {
                PropKit.Tree(d.Props, p, d.Rng);
                d.Blocks.Add((p + Vector3.up, new Vector3(0.7f, 2f, 0.7f), 0f));
            }
            for (int i = 0; i < 3; i++) PropKit.Bush(d.Props, RandomPoint(d, 0.8f), d.Rng);
            Scatter(d, 16);
        }

        private static void FillRuins(Draft d)
        {
            var a = new Vector3(PropKit.Range(d.Rng, -4.5f, -1f), 0f, PropKit.Range(d.Rng, -2f, 2f));
            var b = a + Quaternion.Euler(0f, PropKit.Range(d.Rng, 60f, 120f), 0f) * Vector3.forward * PropKit.Range(d.Rng, 4.5f, 7f);
            PropKit.WallRun(d.Stone, a, b, d.Rng, 0.7f, 1.8f);
            AddWallBlock(d, a, b);
            var pillar = b + (b - a).normalized * 0.9f;
            if (Mathf.Abs(pillar.x) < 5.3f && Mathf.Abs(pillar.z) < 5.3f)
            {
                float h = PropKit.Range(d.Rng, 1.6f, 3.4f);
                PropKit.Pillar(d.Stone, pillar, d.Rng, h);
                d.Blocks.Add((pillar + Vector3.up * 1.5f, new Vector3(1.1f, 3f, 1.1f), 0f));
            }
            for (int i = 0; i < 10; i++)
            {
                var p = Vector3.Lerp(a, b, (float)d.Rng.NextDouble()) + new Vector3(PropKit.Range(d.Rng, -1.2f, 1.2f), 0f, PropKit.Range(d.Rng, -1.2f, 1.2f));
                float s = PropKit.Range(d.Rng, 0.12f, 0.35f);
                d.Stone.Box(p + Vector3.up * s * 0.3f, new Vector3(s * 1.4f, s, s), Quaternion.Euler(PropKit.Range(d.Rng, -15f, 15f), PropKit.Range(d.Rng, 0f, 360f), 0f), PropKit.StoneUv(1.5f), 0);
            }
            Scatter(d, 18);
        }

        private static void FillRoad(Draft d)
        {
            float z = PropKit.Range(d.Rng, -2f, 2f);
            PropKit.Path(d.Floor, new Vector3(-6f, 0f, z), new Vector3(6f, 0f, z), 3f);
            for (int i = 0; i < 12; i++)
            {
                float x = PropKit.Range(d.Rng, -5.8f, 5.8f);
                float side = d.Rng.NextDouble() < 0.5 ? -1f : 1f;
                PropKit.Tuft(d.Props, new Vector3(x, 0f, z + side * PropKit.Range(d.Rng, 1.4f, 1.9f)), d.Rng, PropKit.Range(d.Rng, 0.2f, 0.4f));
            }
            Scatter(d, 10);
            PropKit.Rock(d.Props, new Vector3(PropKit.Range(d.Rng, -5f, 5f), 0f, z + 3.5f), d.Rng, 0.45f);
        }

        private static void FillRocks(Draft d)
        {
            int n = 4 + d.Rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                var p = RandomPoint(d, 1f);
                float s = PropKit.Range(d.Rng, 0.4f, 1.2f);
                PropKit.Rock(d.Props, p, d.Rng, s);
                if (s > 0.6f) d.Blocks.Add((p + Vector3.up * 0.5f, new Vector3(s * 1.6f, 1f, s * 1.6f), 0f));
            }
            for (int i = 0; i < 2; i++) PropKit.Bush(d.Props, RandomPoint(d, 1f), d.Rng);
            Scatter(d, 16);
        }

        private static void FillCamp(Draft d)
        {
            PropKit.Plaza(d.Floor, Vector3.zero, 3f, 7, PropKit.Range(d.Rng, 0f, 50f));
            var brazier = new Vector3(0f, 0f, 0f);
            PropKit.Brazier(d.Props, brazier);
            d.Blocks.Add((brazier + Vector3.up, new Vector3(0.8f, 2f, 0.8f), 0f));
            d.Fires.Add((brazier + Vector3.up * 1.05f, 1f, 6f, 8f));
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(-3.6f + i * 0.75f, 0f, 3.6f - i * 0.2f);
                PropKit.Barrel(d.Props, p, PropKit.Range(d.Rng, 0f, 360f));
                d.Blocks.Add((p + Vector3.up * 0.5f, new Vector3(0.75f, 1f, 0.75f), 0f));
            }
            PropKit.Crate(d.Props, new Vector3(3.4f, 0f, 3.2f), 0.8f, 15f);
            PropKit.Crate(d.Props, new Vector3(3.4f, 0.8f, 3.2f), 0.55f, 40f);
            PropKit.Crate(d.Props, new Vector3(4.3f, 0f, 2.6f), 0.7f, -10f);
            d.Blocks.Add((new Vector3(3.8f, 0.5f, 2.9f), new Vector3(1.9f, 1f, 1.4f), 0f));
            PropKit.FallenColumn(d.Props, new Vector3(-4f, 0f, -3.5f), 70f, 3, d.Rng);
            d.Blocks.Add((new Vector3(-2.75f, 0.45f, -3.05f), new Vector3(2.8f, 0.9f, 0.9f), 70f));
            Scatter(d, 14, 0.5f);
        }

        private static void FillShrine(Draft d)
        {
            PropKit.Plaza(d.Floor, Vector3.zero, 4.2f, 8, 22.5f);
            d.Rune = true;
            foreach (var p in new[] { new Vector3(-3f, 0f, 3f), new Vector3(3f, 0f, 3f), new Vector3(-3f, 0f, -3f), new Vector3(3f, 0f, -3f) })
            {
                float h = PropKit.Range(d.Rng, 2.4f, 3.6f);
                PropKit.Pillar(d.Stone, p, d.Rng, h);
                d.Blocks.Add((p + Vector3.up * 1.5f, new Vector3(1.1f, 3f, 1.1f), 0f));
                if (p.z > 0f) PropKit.Banner(d.Props, p, Vector3.back, h);
            }
            PropKit.Brazier(d.Props, new Vector3(0f, 0f, 2f));
            d.Blocks.Add((new Vector3(0f, 1f, 2f), new Vector3(0.8f, 2f, 0.8f), 0f));
            d.Fires.Add((new Vector3(0f, 1.05f, 2f), 1f, 6.5f, 8.5f));
            Scatter(d, 10, 0.4f);
        }

        private static void FillSpawn(Draft d)
        {
            PropKit.Plaza(d.Floor, Vector3.zero, 4.8f, 8, 22.5f);
            d.Rune = true;
            foreach (float side in new[] { -1f, 1f })
            {
                var p = new Vector3(side * 3.9f, 0f, 1.8f);
                PropKit.Brazier(d.Props, p);
                d.Blocks.Add((p + Vector3.up, new Vector3(0.8f, 2f, 0.8f), 0f));
                d.Fires.Add((p + Vector3.up * 1.05f, 1f, 6.5f, 8.5f));
            }
            Scatter(d, 8, 0.2f);
        }

        private static void FillExit(Draft d)
        {
            PropKit.Plaza(d.Floor, Vector3.zero, 5.2f, 8, 22.5f);
            var gate = new Vector3(0f, 0f, 2.6f);
            foreach (var flame in PropKit.DoorGate(d.Stone, d.Props, gate, d.Rng)) d.Fires.Add((flame, 0.6f, 4.5f, 6.5f));
            d.Blocks.Add((gate + new Vector3(0f, 1.5f, 0.05f), new Vector3(8f, 3f, 1.3f), 0f));
            d.Beacon = gate + new Vector3(0f, 5.4f, -0.4f);
            Scatter(d, 8, 0.2f);
        }

        private static void FillEdge(Draft d)
        {
            // Dense forest just outside the bounds: reads as "the world ends here".
            for (int i = 0; i < 9; i++) PropKit.Tree(d.Props, RandomPoint(d, 0.8f), d.Rng, PropKit.Range(d.Rng, 1f, 1.35f));
            for (int i = 0; i < 6; i++) PropKit.Bush(d.Props, RandomPoint(d, 0.5f), d.Rng);
            for (int i = 0; i < 3; i++) PropKit.Rock(d.Props, RandomPoint(d, 0.8f), d.Rng, PropKit.Range(d.Rng, 0.6f, 1.2f));
            Scatter(d, 12);
        }

        private static void AddWallBlock(Draft d, Vector3 a, Vector3 b)
        {
            var dir = b - a;
            float yaw = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
            d.Blocks.Add(((a + b) * 0.5f + Vector3.up, new Vector3(0.9f, 2f, dir.magnitude + 0.2f), yaw));
        }

        // ------------------------------------------------------------ Tile prefab

        private static GameObject MakeTile(string name, Action<Draft> fill, GameArtKit kit, int seed)
        {
            var d = new Draft { Rng = new System.Random(seed * 7919) };
            fill(d);

            var root = new GameObject($"Tile_{name}");
            try
            {
                Part(root.transform, name, "Floor", d.Floor, kit.Floor);
                Part(root.transform, name, "Stone", d.Stone, kit.Wall);
                Part(root.transform, name, "Props", d.Props, kit.Palette);
                if (d.Rune)
                {
                    var rune = new GameObject("RuneCircle");
                    rune.transform.SetParent(root.transform, false);
                    rune.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.03f, 0f), Quaternion.Euler(90f, 0f, 0f));
                    rune.transform.localScale = new Vector3(5.5f, 5.5f, 1f);
                    rune.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
                    PropKit.SetupRenderer(rune.AddComponent<MeshRenderer>(), kit.RuneDecal);
                }
                foreach (var (pos, scale, intensity, range) in d.Fires) PropKit.Fire(root.transform, pos, kit, scale, intensity, range);
                if (d.Beacon.HasValue) AddBeacon(root.transform, d.Beacon.Value, kit);

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

        private static GameObject ArrowObject(string name, Transform parent, GameArtKit kit, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), kit.Gold);
            return go;
        }

        private static void AddBeacon(Transform tile, Vector3 pos, GameArtKit kit)
        {
            var holder = new GameObject("Exit Beacon").transform;
            holder.SetParent(tile, false);
            holder.localPosition = pos;
            holder.gameObject.AddComponent<FloatBob>();
            var arrow = ArrowObject("Arrow", holder, kit, 2.2f);
            arrow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // tip down at the door
        }

        // ------------------------------------------------------------ Scene objects

        private static void BuildGround(Transform root, GameArtKit kit, Transform player)
        {
            const float size = 144f, texTile = 12f; // big texture tile: no visible repeat at game zoom
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), kit.OuterGround);
            kit.OuterGround.SetTextureScale("_BaseMap", Vector2.one * (size / texTile));
            EditorUtility.SetDirty(kit.OuterGround);
            var snap = new SerializedObject(go.AddComponent<SnapFollow>());
            snap.FindProperty("target").objectReferenceValue = player;
            snap.FindProperty("grid").floatValue = texTile;
            snap.ApplyModifiedPropertiesWithoutUndo();

            var floor = new GameObject("Ground Collider");
            floor.transform.SetParent(root, false);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(400f, 1f, 400f);
        }

        private static void BuildBounds(Transform root)
        {
            float half = (HalfTiles + 0.5f) * TileSize;
            var parent = new GameObject("Bounds").transform;
            parent.SetParent(root, false);
            (Vector3 c, Vector3 s)[] walls =
            {
                (new Vector3(0f, 2f, half + 1f), new Vector3(half * 2f + 4f, 4f, 2f)),
                (new Vector3(0f, 2f, -half - 1f), new Vector3(half * 2f + 4f, 4f, 2f)),
                (new Vector3(half + 1f, 2f, 0f), new Vector3(2f, 4f, half * 2f + 4f)),
                (new Vector3(-half - 1f, 2f, 0f), new Vector3(2f, 4f, half * 2f + 4f)),
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
        private static void BuildPlayerAids(GameArtKit kit, Transform player, PlayerMover mover, WorldStreamer world)
        {
            var ring = Decal("Hero Ring", player, kit, kit.HeroRing, 1.25f);
            ring.transform.localPosition = new Vector3(0f, 0.035f, 0f);

            var markerGo = Decal("Direction Marker", player, kit, kit.DirectionMarker, 0.6f);
            var marker = new SerializedObject(player.gameObject.AddComponent<DirectionMarker>());
            marker.FindProperty("mover").objectReferenceValue = mover;
            marker.FindProperty("marker").objectReferenceValue = markerGo.GetComponent<MeshRenderer>();
            marker.ApplyModifiedPropertiesWithoutUndo();

            var arrow = ArrowObject("Objective Arrow", player, kit, 1.3f);
            var obj = new SerializedObject(player.gameObject.AddComponent<ObjectiveArrow>());
            obj.FindProperty("world").objectReferenceValue = world;
            obj.FindProperty("arrow").objectReferenceValue = arrow.transform;
            obj.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Decal(string name, Transform parent, GameArtKit kit, Material mat, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
            return go;
        }

        /// <summary>Morning ground mist around the player (world-space particles, emitter rides on the player).</summary>
        private static void BuildMist(Transform root, GameArtKit kit, Transform player)
        {
            var holder = new GameObject("Ground Mist");
            holder.transform.SetParent(root, false);
            var snap = new SerializedObject(holder.AddComponent<SnapFollow>());
            snap.FindProperty("target").objectReferenceValue = player;
            snap.FindProperty("grid").floatValue = 1f;
            snap.FindProperty("y").floatValue = 0.4f;
            snap.ApplyModifiedPropertiesWithoutUndo();
            var ps = PropKit.Particles("Emitter", holder.transform, kit.Mist);
            ps.transform.localPosition = new Vector3(0f, 0f, 6f); // camera sees more ahead (+Z) than behind
            ps.transform.localRotation = Quaternion.identity;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.96f, 0.98f, 0.1f), new Color(1f, 0.96f, 0.9f, 0.18f));
            main.maxParticles = 36;
            var emission = ps.emission;
            emission.rateOverTime = 3.5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 0.4f, 30f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.3f), (1f, 0.7f), (0f, 1f) });
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }
    }
}
