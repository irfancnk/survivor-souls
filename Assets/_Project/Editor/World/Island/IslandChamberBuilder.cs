using System;
using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.World.Island
{
    /// <summary>
    /// Island Chambers of the Game scene (KAN-245 / KAN-247): one inactive <see cref="IslandChamber"/> object per theme, far
    /// south of the streamed world, made from the concept generator (Tools/Blender/concepts/chamber_v3.py, renders in
    /// Docs/screenshots/kan245-concepts/).
    /// <list type="bullet">
    /// <item>Room: 36 x 60 m, NE and SW corners chamfered 7.5 m. Walls on the polygon (low on the camera side), an island
    /// foundation of big masonry down to the sea, rocks at the waterline, sea on all four sides (<c>IslandSea</c> shader).</item>
    /// <item>SW: open 5.4 m gap with flanking piers; a bridge arrives from the SW over the sea (no door; a blocker holds the
    /// Knight inside). NE: a gate in the chamfer turned to face NE, with the exit portal (<see cref="PortalBuilder"/>), warm
    /// light pouring in (glow card behind the arch, light shafts and a light pool on the floor: additive quads, no realtime
    /// volumetrics) and a bridge leaving NE into the haze.</item>
    /// <item>Theme content (ponds and pools, wall runs, cover, set dressing, light) from <see cref="IslandThemeSpec"/>.</item>
    /// </list>
    /// Geometry is split in 8 chunks (2 columns x 4 rows), one mesh per material each, so no chunk meets more than 4 point
    /// lights (URP forward). Meshes are assets under Art/World/Island/; the objects are rebuilt with the scene.
    /// Local coordinates: X = east, Z = north, room centre at the island origin.
    /// </summary>
    public static class IslandChamberBuilder
    {
        public static readonly Vector3 Centre = new Vector3(0f, 0f, -400f);
        public const float RX = 18f, RZ = 30f;   // interior half size
        public const float Chamfer = 7.5f;
        public const float WallT = 1f;
        public const float SeaLevel = -2.6f;
        private const float GateOpening = 3.9f;  // portal (2.2 x 3.5 m) scaled by PortalScale fills it
        private const float PortalScale = 1.75f;
        private const float EntryGap = 5.4f;
        private const string MeshDir = "Assets/_Project/Art/World/Island";
        private const string MatDir = "Assets/_Project/Art/Materials/Game";
        private static readonly float S2 = Mathf.Sqrt(0.5f);

        /// <summary>Chunks: 2 columns (x below / above 0) x 4 rows (z cut at -15, 0, 15).</summary>
        internal sealed class Chunk
        {
            public readonly MeshBuilder Floor = new MeshBuilder(), Stone = new MeshBuilder(), Props = new MeshBuilder(), Dark = new MeshBuilder();
        }

        internal sealed class Build
        {
            public IslandThemeSpec Theme;
            public System.Random Rng;
            public readonly Chunk[] Chunks = new Chunk[8];
            public Transform Root, Colliders;
            public readonly List<IslandChamber.Pond> Ponds = new List<IslandChamber.Pond>();
            public readonly List<IslandChamber.WallRun> Walls = new List<IslandChamber.WallRun>();
            public readonly List<IslandChamber.Round> Rounds = new List<IslandChamber.Round>();
            public readonly List<(Vector3 pos, float scale, float intensity, float range)> Fires = new List<(Vector3, float, float, float)>();
            public GameArtKit Kit;
            public RealmArt Art;
            public Material Water, Glow;

            public Chunk C(Vector3 p) => Chunks[(p.x < 0f ? 0 : 1) + 2 * (p.z < -15f ? 0 : p.z < 0f ? 1 : p.z < 15f ? 2 : 3)];
        }

        /// <summary>Builds every island theme in the open scene, wired to the world, spawner, Knight, camera and arrow. All inactive.</summary>
        public static List<IslandChamber> CreateAll(GameArtKit kit, RealmArt art, AudioClip portalHum, WorldStreamer world, EnemySpawner spawner,
            Transform player, TopDownFollowCamera camera, ObjectiveArrow arrow, Light sun)
        {
            var list = new List<IslandChamber>();
            for (int i = 0; i < IslandThemes.All.Length; i++)
                list.Add(Create(IslandThemes.All[i], Centre + new Vector3(i * 160f, 0f, 0f), kit, art, portalHum, world, spawner, player, camera, arrow, sun));
            return list;
        }

        public static IslandChamber Create(IslandThemeSpec theme, Vector3 centre, GameArtKit kit, RealmArt art, AudioClip portalHum, WorldStreamer world,
            EnemySpawner spawner, Transform player, TopDownFollowCamera camera, ObjectiveArrow arrow, Light sun)
        {
            var b = new Build { Theme = theme, Rng = new System.Random(theme.Seed), Kit = kit, Art = art };
            for (int i = 0; i < b.Chunks.Length; i++)
            {
                b.Chunks[i] = new Chunk();
                b.Chunks[i].Floor.Tint = new Color(theme.FloorTint.r, theme.FloorTint.g, theme.FloorTint.b, 0f);
                b.Chunks[i].Stone.Tint = new Color(theme.StoneTint.r, theme.StoneTint.g, theme.StoneTint.b, theme.Snow);
                b.Chunks[i].Props.Tint = new Color(1f, 1f, 1f, theme.Snow);
                b.Chunks[i].Dark.Tint = new Color(theme.FoundationTint.r, theme.FoundationTint.g, theme.FoundationTint.b, 0f);
            }
            var root = new GameObject($"Island {theme.Name}");
            root.transform.SetPositionAndRotation(centre, Quaternion.identity);
            b.Root = root.transform;
            b.Colliders = new GameObject("Colliders").transform;
            b.Colliders.SetParent(root.transform, false);
            b.Water = WaterMaterial(art, theme);
            b.Glow = GlowMaterial(kit, theme);

            BuildFloor(b);
            BuildFoundation(b);
            BuildWalls(b);
            var portal = BuildGate(b, kit, art, portalHum);
            BuildBridge(b, GatePoint(1.0f), new Vector3(S2, 0f, S2), 46f);
            BuildBridge(b, EntryPoint(-1.0f), new Vector3(-S2, 0f, -S2), 34f);
            BuildSea(b, centre);
            theme.Dress?.Invoke(new IslandDresser(b));

            for (int i = 0; i < b.Chunks.Length; i++)
            {
                Part(b.Root, theme.Id, i, "Floor", b.Chunks[i].Floor, art.SnowFloor);
                Part(b.Root, theme.Id, i, "Stone", b.Chunks[i].Stone, art.SnowStone);
                Part(b.Root, theme.Id, i, "Props", b.Chunks[i].Props, art.SnowProps);
                Part(b.Root, theme.Id, i, "Foundation", b.Chunks[i].Dark, art.SnowStone);
            }
            foreach (var (pos, scale, intensity, range) in b.Fires) PropKit.Fire(b.Root, pos, kit, scale, intensity, range);

            // Floor collider (the world's ground collider is far away) and the room edge.
            Box(b.Colliders, new Vector3(0f, -0.5f, 0f), new Vector3(2f * RX + 6f, 1f, 2f * RZ + 6f), 0f);

            var island = root.AddComponent<IslandChamber>();
            var so = new SerializedObject(island);
            so.FindProperty("themeId").stringValue = theme.Id;
            so.FindProperty("displayName").stringValue = theme.Name;
            so.FindProperty("halfSize").vector2Value = new Vector2(RX, RZ);
            so.FindProperty("chamfer").floatValue = Chamfer;
            WriteArray(so.FindProperty("ponds"), b.Ponds, (el, p) =>
            {
                el.FindPropertyRelative("centre").vector2Value = p.centre;
                el.FindPropertyRelative("radii").vector2Value = p.radii;
                el.FindPropertyRelative("yaw").floatValue = p.yaw;
                el.FindPropertyRelative("rect").boolValue = p.rect;
            });
            WriteArray(so.FindProperty("walls"), b.Walls, (el, w) =>
            {
                el.FindPropertyRelative("a").vector2Value = w.a;
                el.FindPropertyRelative("b").vector2Value = w.b;
                el.FindPropertyRelative("radius").floatValue = w.radius;
            });
            WriteArray(so.FindProperty("rounds"), b.Rounds, (el, r) =>
            {
                el.FindPropertyRelative("centre").vector2Value = r.centre;
                el.FindPropertyRelative("radius").floatValue = r.radius;
            });
            so.FindProperty("playerStart").vector3Value = theme.PlayerStart;
            so.FindProperty("portal").objectReferenceValue = portal;
            so.FindProperty("ambientSky").colorValue = theme.AmbientSky;
            so.FindProperty("ambientEquator").colorValue = theme.AmbientEquator;
            so.FindProperty("ambientGround").colorValue = theme.AmbientGround;
            so.FindProperty("fogColor").colorValue = theme.FogColor;
            so.FindProperty("fogStart").floatValue = theme.FogStart;
            so.FindProperty("fogEnd").floatValue = theme.FogEnd;
            so.FindProperty("sunColor").colorValue = theme.SunColor;
            so.FindProperty("sunIntensity").floatValue = theme.SunIntensity;
            so.FindProperty("sunEuler").vector3Value = theme.SunEuler;
            so.FindProperty("world").objectReferenceValue = world;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("followCamera").objectReferenceValue = camera;
            so.FindProperty("arrow").objectReferenceValue = arrow;
            so.FindProperty("sun").objectReferenceValue = sun;
            var hide = new List<GameObject>();
            if (world != null)
                foreach (var name in new[] { "Ground", "Sea", "Landmarks", "Snowfall", "Sea Mist", "Footprints" })
                {
                    var t = world.transform.Find(name);
                    if (t != null) hide.Add(t.gameObject);
                }
            WriteArray(so.FindProperty("hideOnEnter"), hide, (el, go) => el.objectReferenceValue = go);
            so.ApplyModifiedPropertiesWithoutUndo();

            int tris = 0, renderers = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                renderers++;
                if (mf.name != "Sea") tris += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
            }
            Debug.Log($"[IslandChamberBuilder] {theme.Id}: {tris} triangles (sea apart), {renderers} mesh renderers, {b.Fires.Count} fires, " +
                      $"{b.Ponds.Count} pools, {b.Walls.Count} wall runs, {b.Rounds.Count} round blockers");

            root.SetActive(false); // ChamberSetup turns it on when the Chamber's theme names it
            return island;
        }

        // ------------------------------------------------------------ Room geometry

        /// <summary>Room polygon test (local), with a margin: rectangle minus the NE / SW chamfer triangles.</summary>
        public static bool Inside(float x, float z, float margin = 0f)
        {
            if (Mathf.Abs(x) > RX - margin || Mathf.Abs(z) > RZ - margin) return false;
            float diag = RX + RZ - Chamfer, m = margin * 1.41421356f;
            return x + z <= diag - m && x + z >= -diag + m;
        }

        /// <summary>Middle of the NE chamfer, pushed <paramref name="outward"/> metres along the NE normal.</summary>
        public static Vector3 GatePoint(float outward) => new Vector3(RX - Chamfer * 0.5f, 0f, RZ - Chamfer * 0.5f) + new Vector3(S2, 0f, S2) * outward;

        /// <summary>Middle of the SW chamfer, pushed <paramref name="outward"/> metres along the NE normal (negative = out to the SW).</summary>
        public static Vector3 EntryPoint(float outward) => new Vector3(-RX + Chamfer * 0.5f, 0f, -RZ + Chamfer * 0.5f) + new Vector3(S2, 0f, S2) * outward;

        /// <summary>The six room edges, centre line offset outward by half the wall: (a, b, kind, outward normal).</summary>
        private static List<(Vector3 a, Vector3 b, string kind, Vector3 n)> Edges()
        {
            var p = new[]
            {
                new Vector3(-RX + Chamfer, 0f, -RZ), new Vector3(RX, 0f, -RZ), new Vector3(RX, 0f, RZ - Chamfer),
                new Vector3(RX - Chamfer, 0f, RZ), new Vector3(-RX, 0f, RZ), new Vector3(-RX, 0f, -RZ + Chamfer),
            };
            string[] kinds = { "S", "E", "NE", "N", "W", "SW" };
            var list = new List<(Vector3, Vector3, string, Vector3)>();
            for (int i = 0; i < 6; i++)
            {
                Vector3 a = p[i], b = p[(i + 1) % 6];
                Vector3 d = (b - a).normalized, n = new Vector3(d.z, 0f, -d.x);
                list.Add((a + n * (WallT * 0.5f) - d * (WallT * 0.5f), b + n * (WallT * 0.5f) + d * (WallT * 0.5f), kinds[i], n));
            }
            return list;
        }

        private static void BuildFloor(Build b)
        {
            var uv = PropKit.StoneUv(3f);
            const float cell = 2f;
            for (float z = -RZ - 1f; z < RZ + 1f; z += cell)
            for (float x = -RX - 1f; x < RX + 1f; x += cell)
            {
                float cx = x + cell * 0.5f, cz = z + cell * 0.5f;
                if (!Inside(cx, cz, -1.4f)) continue;
                var floor = b.C(new Vector3(cx, 0f, cz)).Floor;
                var t = b.Theme.FloorTint; // one tint: a per-cell tint shows as a checkerboard at the gameplay camera
                floor.Tint = new Color(t.r, t.g, t.b, 0f);
                floor.Quad(new Vector3(x, 0f, z), new Vector3(x, 0f, z + cell), new Vector3(x + cell, 0f, z + cell), new Vector3(x + cell, 0f, z), uv, 0);
            }
        }

        /// <summary>Big masonry from the sea up to the floor, a cap course and rocks at the waterline.</summary>
        private static void BuildFoundation(Build b)
        {
            var uv = PropKit.StoneUv(2.5f);
            foreach (var (a0, b0, _, n) in Edges())
            {
                Vector3 d = (b0 - a0).normalized;
                Vector3 a = a0 + n * (WallT * 0.5f + 0.7f) - d * 0.9f, e = b0 + n * (WallT * 0.5f + 0.7f) + d * 0.9f;
                float len = (e - a).magnitude;
                var rot = Quaternion.LookRotation(d, Vector3.up);
                float y = SeaLevel - 1.2f;
                int row = 0;
                while (y < -0.1f)
                {
                    float h = Mathf.Min(PropKit.Range(b.Rng, 0.95f, 1.25f), -0.05f - y);
                    float t = row % 2 == 1 ? -PropKit.Range(b.Rng, 0.5f, 1.4f) : 0f;
                    while (t < len)
                    {
                        float l = PropKit.Range(b.Rng, 2.2f, 3.6f);
                        float t0 = Mathf.Max(t, 0f), t1 = Mathf.Min(t + l, len);
                        if (t1 - t0 > 0.3f)
                        {
                            var c = a + d * ((t0 + t1) * 0.5f) + n * PropKit.Range(b.Rng, -0.06f, 0.06f) + Vector3.up * (y + h * 0.5f);
                            var dark = b.C(c).Dark;
                            float wet = Mathf.InverseLerp(SeaLevel + 1.8f, SeaLevel - 0.6f, y);
                            var tint = b.Theme.FoundationTint * Mathf.Lerp(1f, 0.55f, wet);
                            dark.Tint = new Color(tint.r, tint.g * (1f + wet * 0.08f), tint.b, 0f);
                            dark.Box(c, new Vector3(1.9f, h - 0.02f, t1 - t0 - 0.03f), rot * Quaternion.Euler(0f, PropKit.Range(b.Rng, -1f, 1f), 0f), uv, 0);
                        }
                        t += l;
                    }
                    y += h;
                    row++;
                }
                // Waterline rocks
                for (float t = 0f; t < len; t += PropKit.Range(b.Rng, 1.6f, 3f))
                {
                    if (b.Rng.NextDouble() < 0.4) continue;
                    var p = a + d * t + n * PropKit.Range(b.Rng, 1.3f, 2.8f) + Vector3.up * (SeaLevel - 0.35f);
                    var props = b.C(p).Props;
                    props.Tint = new Color(0.55f, 0.55f, 0.56f, 0f);
                    ShoreKit.Boulder(props, p, b.Rng, PropKit.Range(b.Rng, 0.7f, 1.5f));
                    props.Tint = new Color(1f, 1f, 1f, b.Theme.Snow);
                }
            }
        }

        private static void BuildWalls(Build b)
        {
            foreach (var (a, e, kind, n) in Edges())
            {
                Vector3 d = (e - a).normalized;
                float len = (e - a).magnitude;
                if (kind == "NE")
                {
                    // Chamfer walls either side of the gate towers; the gate builds the middle.
                    float half = GateOpening * 0.5f + 2.5f;
                    var mid = (a + e) * 0.5f;
                    Ashlar(b, a, mid - d * half, b.Theme.WallHeight + 0.8f, WallT * 1.3f, b.Theme.CapTint);
                    Ashlar(b, mid + d * half, e, b.Theme.WallHeight + 0.8f, WallT * 1.3f, b.Theme.CapTint);
                    Collide(b, a, mid - d * (GateOpening * 0.5f + 0.2f), 4f, WallT + 0.3f);
                    Collide(b, mid + d * (GateOpening * 0.5f + 0.2f), e, 4f, WallT + 0.3f);
                    continue;
                }
                if (kind == "SW")
                {
                    var mid = (a + e) * 0.5f;
                    float half = EntryGap * 0.5f;
                    float h = b.Theme.SouthHeight;
                    Ashlar(b, a, mid - d * half, h, WallT, b.Theme.CapTint, broken: 0.3f);
                    Ashlar(b, mid + d * half, e, h, WallT, b.Theme.CapTint, broken: 0.3f);
                    foreach (float s in new[] { -1f, 1f })
                        PropKit.Pillar(b.C(mid).Stone, mid + d * (s * (half + 0.55f)), b.Rng, Mathf.Max(1.6f, h + 1.2f));
                    Collide(b, a, e, 4f, WallT + 0.2f); // the way back is closed: the Knight came over the bridge
                    continue;
                }
                bool south = kind == "S";
                if (south)
                {
                    Ashlar(b, a, e, b.Theme.SouthHeight, WallT, b.Theme.CapTint, broken: 0.35f);
                }
                else if (b.Theme.Walls == IslandWallStyle.Arcade)
                {
                    Arcade(b, a, e, kind == "N" ? b.Theme.NorthHeight : b.Theme.WallHeight);
                }
                else
                {
                    Ashlar(b, a, e, kind == "N" ? b.Theme.NorthHeight : b.Theme.WallHeight, WallT, b.Theme.CapTint, broken: b.Theme.Walls == IslandWallStyle.Ruin ? 0.3f : 0f);
                }
                Collide(b, a, e, 4f, WallT + 0.2f);
            }
        }

        /// <summary>Dry-laid block wall from <paramref name="a"/> to <paramref name="e"/>, courses of 0.4-0.55 m, an optional broken top, a cap course.</summary>
        private static void Ashlar(Build b, Vector3 a, Vector3 e, float h, float thick, Color capTint, float broken = 0f)
        {
            var uv = PropKit.StoneUv(2f);
            Vector3 d = (e - a).normalized;
            float len = (e - a).magnitude;
            if (len < 0.3f || h <= 0.05f) return;
            var rot = Quaternion.LookRotation(d, Vector3.up);
            float y = 0f;
            int row = 0;
            while (y < h - 0.05f)
            {
                float ch = Mathf.Min(PropKit.Range(b.Rng, 0.5f, 0.7f), h - y);
                float t = row % 2 == 1 ? -PropKit.Range(b.Rng, 0.3f, 0.9f) : 0f;
                while (t < len)
                {
                    float l = PropKit.Range(b.Rng, 1.1f, 2.1f);
                    float t0 = Mathf.Max(t, 0f), t1 = Mathf.Min(t + l, len);
                    t += l + 0.02f;
                    if (t1 - t0 < 0.2f) continue;
                    if (broken > 0f && y > h * 0.45f && b.Rng.NextDouble() < broken * (y / h)) continue;
                    var c = a + d * ((t0 + t1) * 0.5f) + Vector3.up * (y + ch * 0.5f);
                    var stone = b.C(c).Stone;
                    float k = PropKit.Range(b.Rng, 0.86f, 1.08f) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(y / 1.3f)); // damp, darker base
                    var st = b.Theme.StoneTint;
                    stone.Tint = new Color(st.r * k, st.g * k, st.b * k, b.Theme.Snow);
                    stone.Box(c, new Vector3(thick + PropKit.Range(b.Rng, -0.04f, 0.04f), ch - 0.015f, t1 - t0),
                        rot * Quaternion.Euler(PropKit.Range(b.Rng, -0.6f, 0.6f), PropKit.Range(b.Rng, -1f, 1f), 0f), uv, 0);
                }
                y += ch;
                row++;
            }
            if (broken < 0.25f)
            {
                for (float t = 0f; t < len; t += 1.3f)
                {
                    float l = Mathf.Min(1.25f, len - t);
                    var c = a + d * (t + l * 0.5f) + Vector3.up * (y + 0.08f);
                    var stone = b.C(c).Stone;
                    stone.Tint = new Color(capTint.r, capTint.g, capTint.b, b.Theme.Snow);
                    stone.Box(c, new Vector3(thick + 0.14f, 0.16f, l - 0.03f), rot, uv, 0);
                }
            }
        }

        /// <summary>Gothic arcade: low parapet, piers every ~5 m, pointed arches between them; some piers snapped and some arches half fallen.</summary>
        private static void Arcade(Build b, Vector3 a, Vector3 e, float h)
        {
            Ashlar(b, a, e, 0.7f, WallT, b.Theme.CapTint);
            Vector3 d = (e - a).normalized;
            float len = (e - a).magnitude;
            int bays = Mathf.Max(1, Mathf.RoundToInt(len / 5f));
            float spring = h * 0.62f;
            var snapped = new bool[bays + 1];
            for (int i = 0; i <= bays; i++) snapped[i] = i > 0 && i < bays && b.Rng.NextDouble() < 0.18;
            for (int i = 0; i <= bays; i++)
            {
                var p = a + d * (len * i / bays);
                float ph = snapped[i] ? PropKit.Range(b.Rng, 1.2f, 2.4f) : spring + 0.1f;
                Pier(b, p, ph);
                if (i < bays && !snapped[i] && !snapped[i + 1])
                {
                    var q = a + d * (len * (i + 1) / bays);
                    float span = (q - p).magnitude - 0.95f;
                    int missing = b.Rng.NextDouble() < 0.3 ? (b.Rng.NextDouble() < 0.5 ? -1 : 1) : 0;
                    PointedArch(b, (p + q) * 0.5f, d, span, spring + 0.1f, 0.9f, missing);
                }
                else if (i < bays && b.Rng.NextDouble() < 0.7)
                {
                    var q = a + d * (len * (i + 0.5f) / bays) - new Vector3(d.z, 0f, -d.x) * 1.4f;
                    for (int k = 0; k < 5; k++) PropKit.Rock(b.C(q).Stone, q + new Vector3(PropKit.Range(b.Rng, -1f, 1f), 0f, PropKit.Range(b.Rng, -1f, 1f)), b.Rng, PropKit.Range(b.Rng, 0.25f, 0.5f));
                }
            }
        }

        private static void Pier(Build b, Vector3 p, float h)
        {
            var uv = PropKit.StoneUv(2f);
            var stone = b.C(p).Stone;
            var st = b.Theme.StoneTint;
            stone.Tint = new Color(st.r * 0.82f, st.g * 0.82f, st.b * 0.82f, b.Theme.Snow);
            stone.Box(p + Vector3.up * 0.18f, new Vector3(1.25f, 0.36f, 1.25f), Quaternion.identity, uv, 0);
            float y = 0.36f;
            while (y < h - 0.1f)
            {
                float ch = Mathf.Min(PropKit.Range(b.Rng, 0.45f, 0.6f), h - y);
                stone.Tint = new Color(st.r * PropKit.Range(b.Rng, 0.88f, 1.05f), st.g * PropKit.Range(b.Rng, 0.88f, 1.02f), st.b * 0.95f, b.Theme.Snow);
                stone.Box(p + Vector3.up * (y + ch * 0.5f), new Vector3(0.95f, ch - 0.02f, 0.95f), Quaternion.Euler(0f, PropKit.Range(b.Rng, -2f, 2f), 0f), uv, 0);
                y += ch;
            }
            var ct = b.Theme.CapTint;
            stone.Tint = new Color(ct.r, ct.g, ct.b, b.Theme.Snow);
            stone.Box(p + Vector3.up * (y + 0.11f), new Vector3(1.15f, 0.22f, 1.15f), Quaternion.identity, uv, 0);
        }

        /// <summary>Pointed (two-centred) arch of voussoir blocks over <paramref name="span"/>, springing at <paramref name="spring"/>.
        /// <paramref name="missing"/>: -1 = the half towards -<paramref name="along"/> has fallen, 1 = the other half, 0 = whole.</summary>
        private static void PointedArch(Build b, Vector3 centre, Vector3 along, float span, float spring, float depth, int missing = 0)
        {
            var uv = PropKit.StoneUv(2f);
            float r = span * 0.5f, k = 0.35f * r, rr = r + k;
            const int n = 9;
            var look = Quaternion.LookRotation(new Vector3(-along.z, 0f, along.x), Vector3.up); // local x = along
            var stone = b.C(centre).Stone;
            var st = b.Theme.StoneTint;
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * i / n, a1 = Mathf.PI * (i + 1) / n, am = (a0 + a1) * 0.5f;
                bool leftHalf = am > Mathf.PI * 0.5f; // towards -along
                if (missing == -1 && leftHalf || missing == 1 && !leftHalf) continue;
                float px, pz;
                if (!leftHalf) { px = -k + rr * Mathf.Cos(am); pz = rr * Mathf.Sin(am) * 0.95f; }
                else { px = k + rr * Mathf.Cos(am); pz = rr * Mathf.Sin(am) * 0.95f; }
                float seg = rr * (a1 - a0) * 1.03f;
                var pos = centre + along * px + Vector3.up * (spring + pz);
                stone.Tint = new Color(st.r * PropKit.Range(b.Rng, 0.86f, 1.04f), st.g * 0.95f, st.b * 0.92f, b.Theme.Snow);
                // block tangent to the curve: turn about the wall normal
                var rot = look * Quaternion.Euler(0f, 0f, (am - Mathf.PI * 0.5f) * Mathf.Rad2Deg);
                stone.Box(pos, new Vector3(seg, 0.42f, depth), rot, uv, 0);
            }
        }

        // ------------------------------------------------------------ NE gate, bridges, sea

        /// <summary>Gothic gatehouse in the NE chamfer, facing NE (out of the room), with the exit portal and the warm light.</summary>
        private static PortalGate BuildGate(Build b, GameArtKit kit, RealmArt art, AudioClip portalHum)
        {
            var g = GatePoint(0f);
            var face = Quaternion.Euler(0f, 45f, 0f); // local +Z = NE (out), local -Z = into the room
            Vector3 L(float u, float y, float v) => g + face * new Vector3(u, y, v);
            var uv = PropKit.StoneUv(2f);
            var stone = b.C(g).Stone;
            var st = b.Theme.GateTint;
            float towerH = 8.2f;
            foreach (float s in new[] { -1f, 1f })
            {
                float u = s * (GateOpening * 0.5f + 1.2f);
                float y = 0f;
                while (y < towerH)
                {
                    float ch = Mathf.Min(PropKit.Range(b.Rng, 0.5f, 0.65f), towerH - y);
                    stone.Tint = new Color(st.r * PropKit.Range(b.Rng, 0.85f, 1.05f), st.g * 0.97f, st.b * 0.95f, b.Theme.Snow);
                    stone.Box(L(u, y + ch * 0.5f, 0f), new Vector3(2.4f, ch - 0.02f, 2.4f), face * Quaternion.Euler(0f, PropKit.Range(b.Rng, -1f, 1f), 0f), uv, 0);
                    y += ch;
                }
                stone.Tint = new Color(st.r * 0.8f, st.g * 0.8f, st.b * 0.8f, b.Theme.Snow);
                stone.Box(L(u, y + 0.15f, 0f), new Vector3(2.7f, 0.3f, 2.7f), face, uv, 0);
                for (int k = 0; k < 3; k++) // spire: shrinking blocks
                    stone.Box(L(u, y + 0.6f + k * 0.65f, 0f), new Vector3(1.5f - k * 0.45f, 0.65f, 1.5f - k * 0.45f), face * Quaternion.Euler(0f, 45f, 0f), uv, 0);
                PropKit.Brazier(b.C(L(u, 0f, -2.6f)).Props, L(u, 0f, -2.6f));
                b.Fires.Add((L(u, 1.05f, -2.6f), 1f, 3f, 7f));
                b.Rounds.Add(new IslandChamber.Round { centre = new Vector2(L(u, 0f, -2.6f).x, L(u, 0f, -2.6f).z), radius = 0.6f });
                Box(b.Colliders, L(u, 2f, 0f), new Vector3(2.4f, 4f, 2.4f), 45f);
            }
            // pointed arch + lintel over the opening (local x = along the chamfer)
            var along = face * Vector3.right;
            PointedArch(b, g, along, GateOpening + 0.2f, 5.6f, 1.6f);
            stone.Tint = new Color(st.r * 0.8f, st.g * 0.8f, st.b * 0.8f, b.Theme.Snow);
            stone.Box(L(0f, 8.3f, 0f), new Vector3(GateOpening + 1.2f, 0.7f, 1.7f), face, uv, 0);
            stone.Box(L(0f, 0.06f, -1.1f), new Vector3(GateOpening + 0.6f, 0.12f, 0.9f), face, uv, 0); // threshold step
            Box(b.Colliders, L(0f, 1.5f, 1.3f), new Vector3(GateOpening + 0.4f, 3f, 0.6f), 45f); // backstop behind the open portal

            var portal = PortalBuilder.Build(b.Root, L(0f, 0f, 0.15f), kit, art.Glow, portalHum);
            portal.transform.localRotation = face;
            portal.transform.localScale = Vector3.one * PortalScale;

            // Warm light pouring in: a soft glow card behind the arch, three shafts sloping into the room, a light pool on the floor.
            var glow = new GameObject("Gate Light").transform;
            glow.SetParent(b.Root, false);
            GlowQuad(glow, b.Glow, kit, L(0f, 4.6f, 2.6f), face, new Vector3(9f, 11f, 1f), 1f);
            for (int i = -1; i <= 1; i++)
            {
                var start = L(i * 1.1f, 5.4f, 0.2f);
                var end = L(i * 2.4f, 0.2f, -12f);
                var mid = (start + end) * 0.5f;
                var dir = (end - start).normalized;
                var rot = Quaternion.LookRotation(dir, face * Vector3.right) * Quaternion.Euler(90f, 0f, 0f);
                GlowQuad(glow, b.Glow, kit, mid, rot, new Vector3(1.6f + Mathf.Abs(i) * 0.4f, (end - start).magnitude, 1f), 0.45f);
                GlowQuad(glow, b.Glow, kit, mid, rot * Quaternion.Euler(0f, 90f, 0f), new Vector3(1.6f + Mathf.Abs(i) * 0.4f, (end - start).magnitude, 1f), 0.3f);
            }
            GlowQuad(glow, b.Glow, kit, L(0f, 0.04f, -7.5f), face * Quaternion.Euler(90f, 0f, 0f), new Vector3(8f, 15f, 1f), 0.55f);
            return portal;
        }

        private static void GlowQuad(Transform parent, Material mat, GameArtKit kit, Vector3 pos, Quaternion rot, Vector3 scale, float alpha)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            var r = go.AddComponent<MeshRenderer>();
            PropKit.SetupRenderer(r, mat);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Stone arch bridge from <paramref name="start"/> along <paramref name="dir"/>: deck, parapets, piers into the sea, lamps.</summary>
        private static void BuildBridge(Build b, Vector3 start, Vector3 dir, float length)
        {
            var uv = PropKit.StoneUv(2f);
            var mb = new MeshBuilder();
            var props = new MeshBuilder();
            var st = b.Theme.StoneTint;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            var side = new Vector3(dir.z, 0f, -dir.x);
            const float span = 7f, width = 4.6f;
            for (float t = 0f; t < length; t += span)
            {
                var c = start + dir * (t + span * 0.5f);
                mb.Tint = new Color(st.r * 0.8f, st.g * 0.8f, st.b * 0.8f, b.Theme.Snow);
                mb.Box(c + Vector3.down * 0.35f, new Vector3(width + 0.6f, 0.7f, span + 0.02f), rot, uv, 0);
                foreach (float s in new[] { -1f, 1f })
                {
                    for (float u = -span * 0.5f; u < span * 0.5f; u += 1.15f)
                    {
                        mb.Tint = new Color(st.r * PropKit.Range(b.Rng, 0.8f, 1f), st.g * 0.9f, st.b * 0.88f, b.Theme.Snow);
                        mb.Box(c + side * (s * (width * 0.5f + 0.1f)) + dir * (u + 0.55f) + Vector3.up * 0.4f, new Vector3(0.45f, 0.8f, 1.1f), rot, uv, 0);
                    }
                }
                var pier = start + dir * (t + span);
                mb.Tint = new Color(st.r * 0.6f, st.g * 0.62f, st.b * 0.62f, 0f);
                mb.Box(pier + Vector3.up * (SeaLevel * 0.5f - 0.6f), new Vector3(width + 0.8f, -SeaLevel + 2f, 1.6f), rot, uv, 0);
                mb.Box(pier + Vector3.up * (SeaLevel * 0.5f - 0.9f), new Vector3(1.6f, -SeaLevel + 1.4f, 1.6f), rot * Quaternion.Euler(0f, 45f, 0f), uv, 0);
                if (Mathf.RoundToInt(t / span) % 2 == 1)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var post = c + side * (s * (width * 0.5f + 0.1f)) + Vector3.up * 0.8f;
                        props.Box(post + Vector3.up * 0.7f, new Vector3(0.1f, 1.4f, 0.1f), rot, PropKit.Pal(Swatch.Gunmetal), 0);
                        PropKit.Sconce(props, post + Vector3.up * 1.4f, side * s);
                    }
            }
            var go = new GameObject("Bridge");
            go.transform.SetParent(b.Root, false);
            Write(go.transform, $"{MeshDir}/{b.Theme.Id}_Bridge_{(dir.x > 0 ? "NE" : "SW")}_Stone.asset", mb, b.Art.SnowStone);
            Write(go.transform, $"{MeshDir}/{b.Theme.Id}_Bridge_{(dir.x > 0 ? "NE" : "SW")}_Props.asset", props, b.Art.SnowProps);
        }

        /// <summary>One 240 x 240 m sea grid around the island (3 m cells, vertex waves), IslandSea shader.</summary>
        private static void BuildSea(Build b, Vector3 centre)
        {
            const float size = 240f, cell = 4f;
            int n = Mathf.RoundToInt(size / cell);
            var mb = new MeshBuilder();
            mb.Surface(n, n, (i, j) => new Vector3(-size * 0.5f + i * cell, SeaLevel, -size * 0.5f + j * cell), (p, nn) => Vector2.zero);
            var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/{b.Theme.Id}_Sea.asset");
            mb.WriteTo(mesh);
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.up * 2f);
            EditorUtility.SetDirty(mesh);

            var mat = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_IslandSea_{b.Theme.Id}.mat", "SurvivorSouls/IslandSea");
            mat.SetTexture("_NormalMap", b.Art.Sea.GetTexture("_NormalMap"));
            mat.SetTexture("_FoamMap", b.Art.Sea.GetTexture("_FoamMap"));
            mat.SetColor("_ShallowColor", b.Theme.SeaShallow);
            mat.SetColor("_DeepColor", b.Theme.SeaDeep);
            mat.SetColor("_CrestColor", b.Theme.SeaCrest);
            mat.SetColor("_SkyColor", b.Theme.SeaSky);
            mat.SetVector("_IslandCentre", new Vector4(centre.x, centre.z, 0f, 0f));
            mat.SetVector("_IslandHalf", new Vector4(RX + WallT + 2.2f, RZ + WallT + 2.2f, 0f, 0f));
            EditorUtility.SetDirty(mat);

            var go = new GameObject("Sea");
            go.transform.SetParent(b.Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
        }

        // ------------------------------------------------------------ Materials

        /// <summary>Still pool water: the Sea shader with no shore band and tiny waves.</summary>
        private static Material WaterMaterial(RealmArt art, IslandThemeSpec theme)
        {
            var mat = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_IslandPool_{theme.Id}.mat", "SurvivorSouls/Sea");
            mat.SetTexture("_NormalMap", art.Sea.GetTexture("_NormalMap"));
            mat.SetTexture("_FoamMap", art.Sea.GetTexture("_FoamMap"));
            mat.SetColor("_ShallowColor", theme.PoolColor);
            mat.SetColor("_DeepColor", theme.PoolColor * 0.7f);
            mat.SetColor("_SkyColor", theme.SeaSky);
            mat.SetColor("_FoamColor", new Color(0.8f, 0.82f, 0.84f));
            mat.SetFloat("_ShoreZ", -100000f); // no shore: deep everywhere, no foam band
            mat.SetFloat("_DepthRange", 1f);
            mat.SetFloat("_WaveHeight", 0.008f);
            mat.SetFloat("_RippleScale", 2.5f);
            mat.SetFloat("_RippleStrength", 0.35f);
            mat.SetFloat("_Glint", 0.9f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material GlowMaterial(GameArtKit kit, IslandThemeSpec theme)
        {
            var mat = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_IslandGateGlow_{theme.Id}.mat", kit.SoftDot, additive: true);
            mat.SetColor("_BaseColor", theme.GateLight);
            mat.SetFloat("_Cull", 0f); // shafts are seen from both sides
            EditorAssetUtil.ValidateLikeInspector(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------ Helpers

        internal static void Box(Transform parent, Vector3 centre, Vector3 size, float yaw)
        {
            var go = new GameObject("Block");
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>Box collider along a segment (local), <paramref name="height"/> tall, <paramref name="thick"/> wide.</summary>
        private static void Collide(Build b, Vector3 a, Vector3 e, float height, float thick)
        {
            Vector3 d = e - a;
            if (d.sqrMagnitude < 0.01f) return;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Box(b.Colliders, (a + e) * 0.5f + Vector3.up * (height * 0.5f), new Vector3(thick, height, d.magnitude), yaw);
        }

        private static void Part(Transform root, string theme, int chunk, string part, MeshBuilder mb, Material mat)
        {
            if (mb.VertexCount == 0) return;
            Write(root, $"{MeshDir}/{theme}_C{chunk}_{part}.asset", mb, mat, $"C{chunk} {part}");
        }

        private static void Write(Transform parent, string path, MeshBuilder mb, Material mat, string name = null)
        {
            if (mb.VertexCount == 0) return;
            var mesh = EditorAssetUtil.LoadOrCreateMesh(path);
            mb.WriteTo(mesh);
            EditorUtility.SetDirty(mesh);
            var go = new GameObject(name ?? System.IO.Path.GetFileNameWithoutExtension(path));
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
        }

        private static void WriteArray<T>(SerializedProperty array, IReadOnlyList<T> values, Action<SerializedProperty, T> write)
        {
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) write(array.GetArrayElementAtIndex(i), values[i]);
        }

        // ------------------------------------------------------------ Theme dressing API

        /// <summary>What a theme's <see cref="IslandThemeSpec.Dress"/> can place: obstacles (with colliders + walkability) and props.</summary>
        public sealed class IslandDresser
        {
            private readonly Build _b;
            internal IslandDresser(Build b) => _b = b;

            public System.Random Rng => _b.Rng;

            /// <summary>Rectangular pool with a raised stone curb and still water (local centre, half size, Unity yaw).</summary>
            public void RectPool(Vector2 centre, Vector2 half, float yaw, float curbHeight = 0.35f)
            {
                var c = new Vector3(centre.x, 0f, centre.y);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var uv = PropKit.StoneUv(2f);
                var stone = _b.C(c).Stone;
                var ct = _b.Theme.CapTint;
                stone.Tint = new Color(ct.r, ct.g, ct.b, _b.Theme.Snow);
                const float w = 0.45f;
                stone.Box(c + rot * new Vector3(half.x + w * 0.5f, curbHeight * 0.5f, 0f), new Vector3(w, curbHeight, half.y * 2f + w * 2f), rot, uv, 0);
                stone.Box(c + rot * new Vector3(-half.x - w * 0.5f, curbHeight * 0.5f, 0f), new Vector3(w, curbHeight, half.y * 2f + w * 2f), rot, uv, 0);
                stone.Box(c + rot * new Vector3(0f, curbHeight * 0.5f, half.y + w * 0.5f), new Vector3(half.x * 2f, curbHeight, w), rot, uv, 0);
                stone.Box(c + rot * new Vector3(0f, curbHeight * 0.5f, -half.y - w * 0.5f), new Vector3(half.x * 2f, curbHeight, w), rot, uv, 0);
                // dark basin floor just under the water, then the water surface
                var basin = _b.C(c).Dark;
                basin.Tint = new Color(0.25f, 0.26f, 0.24f, 0f);
                basin.Box(c + Vector3.up * 0.02f, new Vector3(half.x * 2f, 0.04f, half.y * 2f), rot, uv, 0);
                var water = new GameObject("Pool Water");
                water.transform.SetParent(_b.Root, false);
                water.transform.SetLocalPositionAndRotation(c + Vector3.up * (curbHeight - 0.12f), rot);
                water.transform.localScale = new Vector3(half.x * 2f, 1f, half.y * 2f);
                water.AddComponent<MeshFilter>().sharedMesh = SubdividedQuad();
                PropKit.SetupRenderer(water.AddComponent<MeshRenderer>(), _b.Water);
                _b.Ponds.Add(new IslandChamber.Pond { centre = centre, radii = half + new Vector2(w, w), yaw = yaw, rect = true });
                Box(_b.Colliders, c + Vector3.up * 0.5f, new Vector3(half.x * 2f + w * 2f, 1f, half.y * 2f + w * 2f), yaw);
            }

            /// <summary>Low ruined dry-stone wall from a to b (local XZ), with rubble at the ends. Blocks walking.</summary>
            public void RuinWall(Vector2 a, Vector2 b, float height)
            {
                var pa = new Vector3(a.x, 0f, a.y);
                var pb = new Vector3(b.x, 0f, b.y);
                Ashlar(_b, pa, pb, height, 0.85f, _b.Theme.CapTint, broken: 0.3f);
                foreach (var e in new[] { pa, pb })
                    for (int k = 0; k < 5; k++)
                        PropKit.Rock(_b.C(e).Stone, e + new Vector3(PropKit.Range(_b.Rng, -0.8f, 0.8f), 0f, PropKit.Range(_b.Rng, -0.8f, 0.8f)), _b.Rng, PropKit.Range(_b.Rng, 0.2f, 0.45f));
                _b.Walls.Add(new IslandChamber.WallRun { a = a, b = b, radius = 0.5f });
                Collide(_b, pa, pb, 3f, 1f);
            }

            /// <summary>A block of pews from a to b (local XZ): rows across the line, a few toppled. Blocks walking as one capsule.</summary>
            public void Pews(Vector2 a, Vector2 b, float rowWidth = 2.6f)
            {
                var pa = new Vector3(a.x, 0f, a.y);
                var pb = new Vector3(b.x, 0f, b.y);
                var d = (pb - pa).normalized;
                float len = (pb - pa).magnitude;
                var across = Quaternion.LookRotation(new Vector3(d.z, 0f, -d.x), Vector3.up); // local z across the aisle
                var wood = PropKit.Pal(Swatch.Wood);
                for (float t = 0.3f; t < len; t += 1.15f)
                {
                    var p = pa + d * t;
                    var props = _b.C(p).Props;
                    var jitter = Quaternion.Euler(0f, PropKit.Range(_b.Rng, -5f, 5f), 0f);
                    if (_b.Rng.NextDouble() < 0.15)
                    {
                        props.Box(p + Vector3.up * 0.16f, new Vector3(0.5f, 0.3f, rowWidth), across * Quaternion.Euler(PropKit.Range(_b.Rng, -25f, 25f), PropKit.Range(_b.Rng, -20f, 20f), 80f), wood, 0);
                        continue;
                    }
                    props.Box(p + Vector3.up * 0.45f, new Vector3(0.5f, 0.08f, rowWidth), across * jitter, wood, 0);
                    props.Box(p - d * 0.24f + Vector3.up * 0.78f, new Vector3(0.08f, 0.66f, rowWidth), across * jitter, wood, 0);
                    foreach (float s in new[] { -1f, 1f })
                        props.Box(p + across * jitter * new Vector3(0f, 0f, s * rowWidth * 0.5f) + Vector3.up * 0.45f, new Vector3(0.56f, 0.9f, 0.08f), across * jitter, PropKit.Pal(Swatch.Bark), 0);
                }
                _b.Walls.Add(new IslandChamber.WallRun { a = a, b = b, radius = rowWidth * 0.5f + 0.1f });
                Collide(_b, pa, pb, 1.2f, rowWidth + 0.2f);
            }

            /// <summary>Broken column (cover): plinth + drums, a fallen drum beside it. Blocks walking.</summary>
            public void BrokenColumn(Vector2 at, float height)
            {
                var p = new Vector3(at.x, 0f, at.y);
                var stone = _b.C(p).Stone;
                var ct = _b.Theme.StoneTint;
                stone.Tint = new Color(ct.r, ct.g, ct.b, _b.Theme.Snow);
                PropKit.Pillar(stone, p, _b.Rng, height);
                float yaw = PropKit.Range(_b.Rng, 0f, 360f);
                PropKit.FallenColumn(_b.C(p).Props, p + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 1.3f, yaw + 90f, _b.Rng.Next(1, 3), _b.Rng);
                _b.Rounds.Add(new IslandChamber.Round { centre = at, radius = 0.8f });
                Box(_b.Colliders, p + Vector3.up * 1.5f, new Vector3(1.2f, 3f, 1.2f), 0f);
            }

            /// <summary>Brazier with a fire and a flickering point light. Blocks walking.</summary>
            public void Brazier(Vector2 at)
            {
                var p = new Vector3(at.x, 0f, at.y);
                PropKit.Brazier(_b.C(p).Props, p);
                _b.Fires.Add((p + Vector3.up * 1.05f, 1f, 3.2f, 7.5f));
                _b.Rounds.Add(new IslandChamber.Round { centre = at, radius = 0.6f });
                Box(_b.Colliders, p + Vector3.up * 0.6f, new Vector3(0.9f, 1.2f, 0.9f), 0f);
            }

            /// <summary>Altar block with candles and one warm candle light. Blocks walking.</summary>
            public void Altar(Vector2 at, float yaw)
            {
                var p = new Vector3(at.x, 0f, at.y);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var uv = PropKit.StoneUv(2f);
                var stone = _b.C(p).Stone;
                stone.Tint = new Color(0.5f, 0.47f, 0.45f, 0f);
                stone.Box(p + Vector3.up * 0.55f, new Vector3(3.2f, 1.1f, 1.3f), rot, uv, 0);
                stone.Tint = new Color(0.8f, 0.76f, 0.7f, 0f);
                stone.Box(p + Vector3.up * 1.16f, new Vector3(3.5f, 0.12f, 1.5f), rot, uv, 0);
                var props = _b.C(p).Props;
                for (int i = 0; i < 14; i++)
                {
                    float h = PropKit.Range(_b.Rng, 0.12f, 0.38f);
                    var c = p + rot * new Vector3(PropKit.Range(_b.Rng, -1.5f, 1.5f), 1.22f + h * 0.5f, PropKit.Range(_b.Rng, -0.55f, 0.55f));
                    props.Box(c, new Vector3(0.07f, h, 0.07f), Quaternion.identity, PropKit.Pal(Swatch.Bleached), 0);
                }
                _b.Fires.Add((p + Vector3.up * 1.55f, 0.35f, 1.6f, 5f));
                _b.Rounds.Add(new IslandChamber.Round { centre = at, radius = 1.7f });
                Box(_b.Colliders, p + Vector3.up * 0.7f, new Vector3(3.5f, 1.4f, 1.5f), yaw);
            }

            /// <summary>Worn carpet runner from a to b (local XZ): strips with gaps, slightly twisted.</summary>
            public void Carpet(Vector2 a, Vector2 b, float width)
            {
                var pa = new Vector3(a.x, 0f, a.y);
                var pb = new Vector3(b.x, 0f, b.y);
                var d = (pb - pa).normalized;
                float len = (pb - pa).magnitude;
                var rot = Quaternion.LookRotation(d, Vector3.up);
                float t = 0f;
                while (t < len)
                {
                    float l = Mathf.Min(PropKit.Range(_b.Rng, 2.2f, 4.2f), len - t);
                    var c = pa + d * (t + l * 0.5f) + Vector3.up * 0.012f;
                    var props = _b.C(c).Props;
                    float k = PropKit.Range(_b.Rng, 0.36f, 0.48f); // worn, dark wine red: the palette crimson is too loud on the floor
                    props.Tint = new Color(k, k * 0.8f, k * 0.8f, 0f);
                    props.Box(c, new Vector3(width + PropKit.Range(_b.Rng, -0.15f, 0.1f), 0.02f, l), rot * Quaternion.Euler(0f, PropKit.Range(_b.Rng, -1.5f, 1.5f), 0f), PropKit.Pal(Swatch.Crimson), 0);
                    props.Tint = new Color(1f, 1f, 1f, _b.Theme.Snow);
                    t += l + PropKit.Range(_b.Rng, 0.25f, 0.9f);
                }
            }

            /// <summary>Banner on a side-wall pier at local (x, z), facing into the room.</summary>
            public void Banner(Vector2 at, Vector2 facing, float height)
            {
                var p = new Vector3(at.x, 0f, at.y);
                PropKit.Banner(_b.C(p).Props, p, new Vector3(facing.x, 0f, facing.y).normalized, height);
            }

            /// <summary>Loose stones scattered near the walls.</summary>
            public void Rubble(int count)
            {
                for (int i = 0; i < count * 6 && count > 0; i++)
                {
                    float x = PropKit.Range(_b.Rng, -RX + 0.6f, RX - 0.6f), z = PropKit.Range(_b.Rng, -RZ + 0.6f, RZ - 0.6f);
                    if (!Inside(x, z, 0.6f)) continue;
                    float edge = Mathf.Min(RX - Mathf.Abs(x), RZ - Mathf.Abs(z));
                    if (edge > 2.5f && _b.Rng.NextDouble() > 0.15) continue;
                    var p = new Vector3(x, 0f, z);
                    PropKit.Rock(_b.C(p).Stone, p, _b.Rng, PropKit.Range(_b.Rng, 0.12f, 0.3f));
                    count--;
                }
            }

            /// <summary>True when (x, z) is clear of obstacles placed so far by <paramref name="margin"/> metres.</summary>
            public bool Clear(Vector2 at, float margin)
            {
                foreach (var p in _b.Ponds)
                    if (IslandChamber.InPond(p, at.x, at.y, margin)) return false;
                foreach (var w in _b.Walls)
                {
                    Vector2 d = w.b - w.a;
                    float t = Mathf.Clamp01(Vector2.Dot(at - w.a, d) / Mathf.Max(d.sqrMagnitude, 1e-6f));
                    if ((w.a + d * t - at).magnitude < w.radius + margin) return false;
                }
                foreach (var r in _b.Rounds)
                    if ((r.centre - at).magnitude < r.radius + margin) return false;
                return Inside(at.x, at.y, margin);
            }
        }

        private static Mesh _quad16;

        /// <summary>A 1 x 1 quad on the XZ plane (facing up) split 16 x 16, so the pool water's vertex waves and fog work.</summary>
        private static Mesh SubdividedQuad()
        {
            if (_quad16 != null) return _quad16;
            var mb = new MeshBuilder();
            mb.Surface(16, 16, (i, j) => new Vector3(-0.5f + i / 16f, 0f, -0.5f + j / 16f), (p, n) => new Vector2(p.x + 0.5f, p.z + 0.5f));
            _quad16 = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/PoolQuad16.asset");
            mb.WriteTo(_quad16);
            EditorUtility.SetDirty(_quad16);
            return _quad16;
        }
    }
}
