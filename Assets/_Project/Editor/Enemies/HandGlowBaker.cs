using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-145: bakes an emission map that makes the hands of a Meshy enemy glow (Pyre Thrower: both hands, ember; Hexbound
    /// Shaman: the staff hand, violet). The mask comes from the skin weights: every vertex weighted to a hand bone (or a bone
    /// under it) glows by that weight, and the triangles are filled into UV space, so a held staff or torch glows with the
    /// hand. The glow colour is shaded a little by the base colour (detail stays). Written as <c>T_&lt;Id&gt;_Emission.jpg</c>,
    /// which <see cref="EnemyRosterBuilder"/> wires into the material (white emission colour). Written once: delete the file to
    /// bake it again (same rule as <see cref="LavaEmissionBaker"/>).
    /// </summary>
    public static class HandGlowBaker
    {
        public const int Size = 512;    // the glow is a soft blob: 512 is enough, the importer caps it anyway
        private const int Quality = 90;
        private const int Dilate = 4;   // px grown round every UV island, so the island seams do not show a dark line
        public static readonly string[] HandBones = { "LeftHand", "RightHand" };

        /// <summary>Writes <paramref name="emissionPath"/> when it is missing. True = written.</summary>
        public static bool Ensure(string modelPath, string baseColorPath, string emissionPath, Color glow, bool staffHandOnly)
        {
            if (File.Exists(emissionPath)) return false;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var skin = model != null ? model.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (skin == null || skin.sharedMesh == null) throw new System.InvalidOperationException($"HandGlowBaker: no skinned mesh in {modelPath}");

            var weights = VertexGlow(skin, staffHandOnly, out string hands);
            var mask = Rasterize(skin.sharedMesh, weights, Size);

            Color32[] baseColor = null;
            int baseW = 0, baseH = 0;
            if (File.Exists(baseColorPath))
            {
                var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                source.LoadImage(File.ReadAllBytes(baseColorPath)); // the file, not the imported asset: no read/write flag needed
                baseColor = source.GetPixels32();
                baseW = source.width;
                baseH = source.height;
                Object.DestroyImmediate(source);
            }

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float m = mask[y * Size + x];
                if (m <= 0f) { pixels[y * Size + x] = new Color32(0, 0, 0, 255); continue; }
                float shade = 1f;
                if (baseColor != null)
                {
                    var b = baseColor[(y * baseH / Size) * baseW + x * baseW / Size];
                    float lum = (0.299f * b.r + 0.587f * b.g + 0.114f * b.b) / 255f;
                    shade = Mathf.Lerp(0.55f, 1.35f, Mathf.Clamp01(lum * 1.6f));
                }
                var c = glow * (m * shade);
                pixels[y * Size + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
            }

            var output = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            output.SetPixels32(pixels);
            output.Apply();
            File.WriteAllBytes(emissionPath, output.EncodeToJPG(Quality));
            Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(emissionPath);
            Debug.Log($"[HandGlowBaker] {emissionPath} baked from the {hands} weights of {modelPath}");
            return true;
        }

        /// <summary>
        /// Glow (0..1) per vertex: the summed skin weight of the chosen hand bones and every bone under them, eased so the wrist
        /// fades out. <paramref name="staffHandOnly"/>: only the hand with more weighted vertices (it holds the staff).
        /// </summary>
        public static float[] VertexGlow(SkinnedMeshRenderer skin, bool staffHandOnly, out string hands)
        {
            var bones = skin.bones;
            var side = new int[bones.Length]; // 0 = no hand, 1 + index into HandBones
            for (int i = 0; i < bones.Length; i++)
                for (var t = bones[i]; t != null && side[i] == 0; t = t.parent)
                {
                    int h = System.Array.IndexOf(HandBones, t.name);
                    if (h >= 0) side[i] = h + 1;
                }

            var boneWeights = skin.sharedMesh.boneWeights;
            var perSide = new float[HandBones.Length, boneWeights.Length];
            var count = new int[HandBones.Length];
            for (int v = 0; v < boneWeights.Length; v++)
            {
                var w = boneWeights[v];
                Add(perSide, side, v, w.boneIndex0, w.weight0);
                Add(perSide, side, v, w.boneIndex1, w.weight1);
                Add(perSide, side, v, w.boneIndex2, w.weight2);
                Add(perSide, side, v, w.boneIndex3, w.weight3);
                for (int h = 0; h < HandBones.Length; h++) if (perSide[h, v] > 0.5f) count[h]++;
            }

            int only = -1;
            if (staffHandOnly) only = count[1] > count[0] ? 1 : 0;
            hands = only < 0 ? string.Join(" + ", HandBones) : HandBones[only];
            if (count[0] + count[1] == 0) throw new System.InvalidOperationException($"HandGlowBaker: no vertex weighted to {string.Join(" / ", HandBones)}");

            var glow = new float[boneWeights.Length];
            for (int v = 0; v < glow.Length; v++)
            {
                float w = 0f;
                for (int h = 0; h < HandBones.Length; h++) if (only < 0 || only == h) w += perSide[h, v];
                glow[v] = Smooth(0.15f, 0.75f, w);
            }
            return glow;
        }

        private static void Add(float[,] perSide, int[] side, int vertex, int bone, float weight)
        {
            if (weight <= 0f || bone < 0 || bone >= side.Length || side[bone] == 0) return;
            perSide[side[bone] - 1, vertex] += weight;
        }

        /// <summary>Fills every triangle into a <paramref name="size"/>² UV mask (max of the interpolated vertex glow), then grows the islands.</summary>
        public static float[] Rasterize(Mesh mesh, float[] vertexGlow, int size)
        {
            var uv = mesh.uv;
            var tris = mesh.triangles;
            var mask = new float[size * size];
            var covered = new bool[size * size];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                float g0 = vertexGlow[i0], g1 = vertexGlow[i1], g2 = vertexGlow[i2];
                Vector2 a = Clamp(uv[i0]) * size, b = Clamp(uv[i1]) * size, c = Clamp(uv[i2]) * size;
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                float area = Edge(a, b, c);
                if (Mathf.Abs(area) < 1e-6f) continue;
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(b, c, p) / area, w1 = Edge(c, a, p) / area, w2 = 1f - w0 - w1;
                    const float eps = -0.02f;
                    if (w0 < eps || w1 < eps || w2 < eps) continue;
                    int k = y * size + x;
                    covered[k] = true;
                    mask[k] = Mathf.Max(mask[k], g0 * w0 + g1 * w1 + g2 * w2);
                }
            }

            var next = new float[mask.Length];
            var nextCovered = new bool[mask.Length];
            for (int pass = 0; pass < Dilate; pass++)
            {
                System.Array.Copy(mask, next, mask.Length);
                System.Array.Copy(covered, nextCovered, covered.Length);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int k = y * size + x;
                    if (covered[k]) continue;
                    float best = 0f;
                    bool any = false;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                        int n = ny * size + nx;
                        if (!covered[n]) continue;
                        any = true;
                        best = Mathf.Max(best, mask[n]);
                    }
                    if (!any) continue;
                    next[k] = best;
                    nextCovered[k] = true;
                }
                (mask, next) = (next, mask);
                (covered, nextCovered) = (nextCovered, covered);
            }
            return mask;
        }

        private static Vector2 Clamp(Vector2 uv) => new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

        private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
    }
}
