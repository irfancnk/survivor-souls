using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Builds one grass mesh per world tile: clumps of single-triangle blades on the open ground, kept off the
    /// stone floor (floor triangles) and out of wall footprints (collider blocks). Drawn by "SurvivorSouls/Grass".
    /// Vertex colour RGB = blade tint, A = bend weight (0 root, 1 tip) for wind and player push.
    /// Budget: about <see cref="ClumpsPerM2"/> x 4.5 blades per m2, one triangle each (a full open tile is ~7k tris).
    /// </summary>
    public static class GrassBuilder
    {
        private const float ClumpsPerM2 = 11f;
        private const float Cell = 0.1f;          // occupancy grid cell (m)
        private const float FloorMargin = 0.06f;  // keep blades this far from the floor edge
        private const float FlowerChance = 0.018f;

        // Realistic temperate meadow, a little warm (morning sun). Not neon.
        private static readonly Color RootColor = new Color(0.1f, 0.14f, 0.05f);
        private static readonly Color LushTip = new Color(0.24f, 0.36f, 0.1f);
        private static readonly Color PaleTip = new Color(0.36f, 0.44f, 0.15f);
        private static readonly Color DryTip = new Color(0.5f, 0.46f, 0.26f);
        private static readonly Color[] FlowerColors =
        {
            new Color(0.95f, 0.93f, 0.85f), new Color(0.98f, 0.82f, 0.3f), new Color(0.7f, 0.55f, 0.85f),
        };

        /// <summary>
        /// Writes the grass mesh for one tile to <paramref name="path"/>. Tile content lives in local [-half, half].
        /// Returns null when the tile has no open ground.
        /// </summary>
        public static Mesh Build(string path, float tileSize, MeshBuilder floor,
            IEnumerable<(Vector3 centre, Vector3 size, float yaw)> blocks, int seed)
        {
            float half = tileSize * 0.5f;
            int n = Mathf.CeilToInt(tileSize / Cell);
            var blocked = new bool[n, n];
            MarkFloor(blocked, floor, half, n);
            MarkBlocks(blocked, blocks, half, n);

            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var positions = new List<Vector3>();
            var colors = new List<Color32>();
            var tris = new List<int>();

            int clumps = Mathf.RoundToInt(tileSize * tileSize * ClumpsPerM2);
            for (int c = 0; c < clumps; c++)
            {
                var centre = new Vector3(R() * tileSize - half, 0f, R() * tileSize - half);
                if (IsBlocked(blocked, centre, half, n)) continue;

                // Low-frequency field: lush hollows vs pale, drier rises.
                float u = (centre.x + half) / tileSize, v = (centre.z + half) / tileSize;
                float lush = TextureFactory.Fbm(u, v, 3, 3, seed & 1023);
                float clumpHeight = Mathf.Lerp(0.22f, 0.42f, lush) * Mathf.Lerp(0.8f, 1.25f, R());
                bool dryClump = R() < 0.1f;
                int blades = 3 + rng.Next(4);
                for (int b = 0; b < blades; b++)
                {
                    float a = R() * Mathf.PI * 2f, r = Mathf.Sqrt(R()) * 0.11f;
                    var root = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (IsBlocked(blocked, root, half, n)) continue;
                    var tip = Color.Lerp(Color.Lerp(PaleTip, LushTip, lush), DryTip, dryClump ? 0.75f : R() * 0.2f);
                    float height = clumpHeight * Mathf.Lerp(0.6f, 1.1f, R());
                    AddBlade(positions, colors, tris, root, height, Mathf.Lerp(0.045f, 0.075f, R()), R() * Mathf.PI * 2f,
                        Mathf.Lerp(0.15f, 0.55f, R()), R() * Mathf.PI * 2f, tip, Mathf.Lerp(0.8f, 1.05f, R()));
                }
                if (R() < FlowerChance) AddFlower(positions, colors, tris, centre, clumpHeight, FlowerColors[rng.Next(FlowerColors.Length)]);
            }
            if (positions.Count == 0) return null;

            var mesh = EditorAssetUtil.LoadOrCreateMesh(path);
            mesh.Clear();
            mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            // Bounds include the wind sway and push so culling never clips moving tips.
            var bounds = new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(tileSize + 1f, 0.9f, tileSize + 1f));
            mesh.bounds = bounds;
            mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>One blade: a thin triangle whose tip leans out (lean) toward leanDir.</summary>
        private static void AddBlade(List<Vector3> p, List<Color32> c, List<int> t, Vector3 root, float height, float width,
            float facing, float lean, float leanDir, Color tip, float shade)
        {
            var side = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing)) * (width * 0.5f);
            var tipPos = root + new Vector3(Mathf.Cos(leanDir) * lean * height, height * (1f - lean * 0.35f), Mathf.Sin(leanDir) * lean * height);
            int i = p.Count;
            p.Add(root - side);
            p.Add(root + side);
            p.Add(tipPos);
            var rootColor = RootColor * shade;
            rootColor.a = 0f;
            var tipColor = tip * shade;
            tipColor.a = 1f;
            c.Add(rootColor);
            c.Add(rootColor);
            c.Add(tipColor);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <summary>A small flower: a stem blade and a flat coloured head (two triangles) at the top.</summary>
        private static void AddFlower(List<Vector3> p, List<Color32> c, List<int> t, Vector3 at, float height, Color head)
        {
            float h = height * 1.2f;
            AddBlade(p, c, t, at, h, 0.02f, 0f, 0f, 0f, LushTip, 0.9f);
            var top = at + Vector3.up * h;
            const float s = 0.05f;
            int i = p.Count;
            p.Add(top + new Vector3(-s, 0f, 0f));
            p.Add(top + new Vector3(0f, 0f, s));
            p.Add(top + new Vector3(s, 0f, 0f));
            p.Add(top + new Vector3(0f, 0f, -s));
            head.a = 1f;
            for (int k = 0; k < 4; k++) c.Add(head);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        // ------------------------------------------------------------ Occupancy

        private static bool IsBlocked(bool[,] grid, Vector3 p, float half, int n)
        {
            int x = Mathf.FloorToInt((p.x + half) / Cell), z = Mathf.FloorToInt((p.z + half) / Cell);
            if (x < 0 || z < 0 || x >= n || z >= n) return true;
            return grid[x, z];
        }

        private static void MarkFloor(bool[,] grid, MeshBuilder floor, float half, int n)
        {
            foreach (var (a, b, c) in floor.Triangles())
            {
                // Only near-flat walkable triangles (skip steps' vertical sides).
                if (Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) < 0.5f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - FloorMargin + half) / Cell));
                int x1 = Mathf.Min(n - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + FloorMargin + half) / Cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - FloorMargin + half) / Cell));
                int z1 = Mathf.Min(n - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + FloorMargin + half) / Cell));
                for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (grid[x, z]) continue;
                    var q = new Vector2((x + 0.5f) * Cell - half, (z + 0.5f) * Cell - half);
                    if (NearTriangle(q, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), FloorMargin)) grid[x, z] = true;
                }
            }
        }

        private static void MarkBlocks(bool[,] grid, IEnumerable<(Vector3 centre, Vector3 size, float yaw)> blocks, float half, int n)
        {
            foreach (var (centre, size, yaw) in blocks)
            {
                var inv = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
                float reach = Mathf.Max(size.x, size.z) * 0.75f;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    var q = new Vector3((x + 0.5f) * Cell - half, 0f, (z + 0.5f) * Cell - half);
                    if (Mathf.Abs(q.x - centre.x) > reach || Mathf.Abs(q.z - centre.z) > reach) continue;
                    var l = inv * (q - centre);
                    if (Mathf.Abs(l.x) <= size.x * 0.5f && Mathf.Abs(l.z) <= size.z * 0.5f) grid[x, z] = true;
                }
            }
        }

        private static bool NearTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float margin)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos)) return true;
            return SegmentDistance(p, a, b) < margin || SegmentDistance(p, b, c) < margin || SegmentDistance(p, c, a) < margin;
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return (a + ab * t - p).magnitude;
        }
    }
}
