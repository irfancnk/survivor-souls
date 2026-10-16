using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Holy Tomes book art (KAN-123): a small closed tome built from 11 boxes (132 triangles): oxblood leather covers and
    /// spine, a glowing gold-white page block, gold corner caps, a gold clasp and a glowing cross on the front cover. One
    /// 64 x 64 atlas (leather / gold / pages / glow quarters) and its emission mask; Simple Lit with emission, so all books
    /// share one material (SRP batched). Local axes: Z = length (orbit direction), X = width (spine at -X, toward the
    /// Knight), Y = thickness (front cover up). Mesh, textures and material are rewritten on every build (same paths, so
    /// prefab links survive).
    /// </summary>
    public static class HolyTomeArt
    {
        public struct Result
        {
            public Mesh Mesh;
            public Material Material;
        }

        private const int Size = 64;
        private enum Region { Leather, Gold, Pages, Glow }

        public static Result Build(string fxDir)
        {
            string dir = fxDir + "/Tome";
            var albedo = TextureFactory.Write(dir + "/T_HolyTome.png", Size, Size, Albedo, TextureFactory.Kind.Color,
                mipmaps: true, wrap: TextureWrapMode.Clamp, maxSize: 64);
            var glow = TextureFactory.Write(dir + "/T_HolyTome_Emission.png", Size, Size, Emission, TextureFactory.Kind.Color,
                mipmaps: true, wrap: TextureWrapMode.Clamp, maxSize: 64);
            var mat = EditorAssetUtil.SimpleLitMaterial(dir + "/M_HolyTome.mat", albedo, Color.white, smoothness: 0.35f,
                specColor: new Color(0.45f, 0.38f, 0.25f));
            mat.SetTexture("_EmissionMap", glow);
            mat.SetColor("_EmissionColor", new Color(1.6f, 1.25f, 0.65f)); // HDR: pages and cross glow warm gold
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.EnableKeyword("_EMISSION");
            mat.enableInstancing = true;
            EditorAssetUtil.ValidateSimpleLit(mat);

            var mesh = EditorAssetUtil.LoadOrCreateMesh(dir + "/SM_HolyTome.asset");
            BuildMesh(mesh);
            EditorUtility.SetDirty(mesh);
            return new Result { Mesh = mesh, Material = mat };
        }

        // ------------------------------------------------------------ Mesh

        private static void BuildMesh(Mesh mesh)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            const float hw = 0.19f, hl = 0.25f, ht = 0.06f, board = 0.025f;
            // Covers and spine (leather).
            Box(v, n, uv, t, new Vector3(-hw, -ht, -hl), new Vector3(hw, -ht + board, hl), Region.Leather);
            Box(v, n, uv, t, new Vector3(-hw, ht - board, -hl), new Vector3(hw, ht, hl), Region.Leather);
            Box(v, n, uv, t, new Vector3(-hw, -ht, -hl), new Vector3(-hw + board, ht, hl), Region.Leather);
            // Page block, inset from the cover edges (glows).
            Box(v, n, uv, t, new Vector3(-hw + board, -ht + board, -hl + 0.015f), new Vector3(hw - 0.012f, ht - board, hl - 0.015f), Region.Pages);
            // Gold corner caps on the front cover.
            const float c = 0.075f, cap = 0.012f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float x0 = sx < 0 ? -hw - 0.004f : hw - c, z0 = sz < 0 ? -hl - 0.004f : hl - c;
                Box(v, n, uv, t, new Vector3(x0, ht - 0.004f, z0), new Vector3(x0 + c + 0.004f, ht + cap, z0 + c + 0.004f), Region.Gold);
            }
            // Glowing cross on the front cover (long bar along the length, short bar toward the head).
            Box(v, n, uv, t, new Vector3(-0.02f, ht, -0.15f), new Vector3(0.02f, ht + 0.01f, 0.15f), Region.Glow);
            Box(v, n, uv, t, new Vector3(-0.09f, ht, 0.04f), new Vector3(0.09f, ht + 0.01f, 0.08f), Region.Glow);
            // Gold clasp over the fore edge.
            Box(v, n, uv, t, new Vector3(hw - 0.01f, -ht - 0.004f, -0.035f), new Vector3(hw + 0.016f, ht + 0.008f, 0.035f), Region.Gold);

            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
        }

        /// <summary>Axis-aligned box with flat normals; every face maps the whole atlas quarter of <paramref name="r"/>.</summary>
        private static void Box(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 min, Vector3 max, Region r)
        {
            Vector2 o = Origin(r);
            const float inset = 2f / Size, span = 0.5f - 2f * inset;
            void Face(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 normal)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(o + new Vector2(inset, inset));
                uv.Add(o + new Vector2(inset, inset + span));
                uv.Add(o + new Vector2(inset + span, inset + span));
                uv.Add(o + new Vector2(inset + span, inset));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            Face(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up);
            Face(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), Vector3.down);
            Face(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right);
            Face(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), Vector3.left);
            Face(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.forward);
            Face(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back);
        }

        private static Vector2 Origin(Region r) => r switch
        {
            Region.Leather => new Vector2(0f, 0f),
            Region.Gold => new Vector2(0.5f, 0f),
            Region.Pages => new Vector2(0f, 0.5f),
            _ => new Vector2(0.5f, 0.5f),
        };

        // ------------------------------------------------------------ Textures

        private static Region At(int x, int y) => x < Size / 2 ? (y < Size / 2 ? Region.Leather : Region.Pages) : (y < Size / 2 ? Region.Gold : Region.Glow);

        private static Color Albedo(int x, int y)
        {
            int lx = x % (Size / 2), ly = y % (Size / 2);
            float grain = TextureFactory.Hash(x, y, 123);
            switch (At(x, y))
            {
                case Region.Leather:
                {
                    // Oxblood leather: fine grain and a soft darker edge.
                    float edge = Mathf.Min(Mathf.Min(lx, ly), Mathf.Min(31 - lx, 31 - ly)) / 6f;
                    float k = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(edge)) * Mathf.Lerp(0.85f, 1.08f, grain);
                    return new Color(0.36f * k, 0.12f * k, 0.09f * k, 1f);
                }
                case Region.Gold:
                {
                    float shine = 0.75f + 0.25f * Mathf.Sin(ly / 31f * Mathf.PI);
                    float k = shine * Mathf.Lerp(0.92f, 1.05f, grain);
                    return new Color(0.95f * k, 0.72f * k, 0.3f * k, 1f);
                }
                case Region.Pages:
                {
                    // Cream pages with thin page lines along the length.
                    float line = (lx % 3 == 0) ? 0.86f : 1f;
                    return new Color(0.96f * line, 0.9f * line, 0.74f * line, 1f);
                }
                default:
                    return new Color(1f, 0.93f, 0.7f, 1f);
            }
        }

        private static Color Emission(int x, int y)
        {
            int lx = x % (Size / 2);
            switch (At(x, y))
            {
                case Region.Leather: return Color.black;
                case Region.Gold: return new Color(0.12f, 0.09f, 0.04f, 1f);
                case Region.Pages:
                {
                    float line = (lx % 3 == 0) ? 0.7f : 1f;
                    return new Color(0.75f * line, 0.68f * line, 0.5f * line, 1f);
                }
                default: return Color.white;
            }
        }
    }
}
