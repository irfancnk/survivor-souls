using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Procedural art of the Ember Boons effects (KAN-246): small low-poly meshes (under 200 triangles each) and one
    /// material per look, all rewritten on every build at the same paths (prefab links survive):
    /// firebrand shard (Ember Wheel), ice blade (Rime Blades), storm orb (Storm Satellites, built-in sphere), crescent scythe
    /// (Reaper Scythes), shield plate (Sentinel Halo), colossal sword (Sovereign's Fall), ghost figure (Grave Choir archer,
    /// Stolen Moment echo), plus an alpha-blended glow particle material and an arc line material. Simple Lit with emission
    /// for the solid bodies (SRP batched), Unlit transparent for the ghosts. Readable first version: a VFX pass is a follow-up.
    /// </summary>
    public static class EmberBoonsArt
    {
        public sealed class Result
        {
            public Mesh Shard, Blade, Orb, Scythe, Plate, Sword, Ghost, Head;
            public Material Fire, Ice, Storm, Reaper, Halo, SwordMat, GhostArcher, GhostEcho;
            public Material Glow, Arc;
        }

        public static Result Build(string fxDir, Texture2D softDot)
        {
            string dir = fxDir + "/Boons";
            EditorAssetUtil.EnsureFolder(dir);
            var r = new Result
            {
                Shard = Mesh(dir + "/SM_Boon_Firebrand.asset", m => Diamond(m, 0.11f, 0.11f, 0.42f)),
                Blade = Mesh(dir + "/SM_Boon_IceBlade.asset", m => Diamond(m, 0.16f, 0.035f, 0.62f)),
                Scythe = Mesh(dir + "/SM_Boon_Scythe.asset", Crescent),
                Plate = Mesh(dir + "/SM_Boon_ShieldPlate.asset", Plate),
                Sword = Mesh(dir + "/SM_Boon_Sword.asset", Sword),
                Orb = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
                Ghost = Resources.GetBuiltinResource<Mesh>("Capsule.fbx"),
                Head = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
                Fire = Emissive(dir + "/M_Boon_Firebrand.mat", new Color(0.55f, 0.18f, 0.05f), new Color(3f, 1.1f, 0.25f)),
                Ice = Emissive(dir + "/M_Boon_IceBlade.mat", new Color(0.62f, 0.82f, 1f), new Color(0.45f, 0.8f, 1.4f)),
                Storm = Emissive(dir + "/M_Boon_StormOrb.mat", new Color(0.8f, 0.9f, 1f), new Color(1.6f, 2f, 2.6f)),
                Reaper = Emissive(dir + "/M_Boon_Scythe.mat", new Color(0.45f, 0.35f, 0.6f), new Color(0.9f, 0.6f, 1.6f)),
                Halo = Emissive(dir + "/M_Boon_ShieldPlate.mat", new Color(1f, 0.85f, 0.5f), new Color(1.2f, 0.95f, 0.45f)),
                SwordMat = Emissive(dir + "/M_Boon_Sword.mat", new Color(0.9f, 0.88f, 0.8f), new Color(1.8f, 1.6f, 1.1f)),
                GhostArcher = EditorAssetUtil.TransparentMaterial(dir + "/M_Boon_GhostArcher.mat", EditorAssetUtil.Unlit, null,
                    new Color(0.55f, 0.85f, 1f, 0.55f)),
                GhostEcho = EditorAssetUtil.TransparentMaterial(dir + "/M_Boon_GhostEcho.mat", EditorAssetUtil.Unlit, null,
                    new Color(0.7f, 0.8f, 1f, 0.4f)),
                // Alpha blend, not additive: additive light vanishes on the Realm 1 snow (KAN-134 rule).
                Glow = EditorAssetUtil.ParticleMaterial(dir + "/M_FX_BoonGlow.mat", softDot, additive: false),
                Arc = EditorAssetUtil.ParticleMaterial(dir + "/M_FX_BoonArc.mat", ArcTexture(dir), additive: false),
            };
            return r;
        }

        private static Material Emissive(string path, Color albedo, Color emission)
        {
            var mat = EditorAssetUtil.SimpleLitMaterial(path, null, albedo, smoothness: 0.4f, specColor: new Color(0.4f, 0.4f, 0.4f));
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.EnableKeyword("_EMISSION");
            mat.enableInstancing = true;
            EditorAssetUtil.ValidateSimpleLit(mat);
            return mat;
        }

        /// <summary>Arc line texture: white, soft across the line (V), solid along it (U).</summary>
        private static Texture2D ArcTexture(string dir) =>
            TextureFactory.Write(dir + "/T_Boon_Arc.png", 8, 32, (x, y) =>
            {
                float v = Mathf.Abs(y / 31f - 0.5f) * 2f;
                float a = Mathf.Clamp01(1f - v * v * 1.15f);
                return new Color(1f, 1f, 1f, a);
            }, TextureFactory.Kind.Color, mipmaps: false, wrap: TextureWrapMode.Clamp, maxSize: 32);

        private static Mesh Mesh(string path, System.Action<Mesh> build)
        {
            var mesh = EditorAssetUtil.LoadOrCreateMesh(path);
            build(mesh);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------ Shapes

        /// <summary>Stretched octahedron along Z (shard, blade): half widths <paramref name="hx"/>, <paramref name="hy"/>, half length <paramref name="hz"/>.</summary>
        private static void Diamond(Mesh mesh, float hx, float hy, float hz)
        {
            var p = new[]
            {
                new Vector3(0f, 0f, hz), new Vector3(0f, 0f, -hz * 0.55f),
                new Vector3(hx, 0f, 0f), new Vector3(-hx, 0f, 0f), new Vector3(0f, hy, 0f), new Vector3(0f, -hy, 0f),
            };
            int[] faces = { 0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4, 1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3 };
            Flat(mesh, p, faces);
        }

        /// <summary>Flat crescent blade (scythe) in the XZ plane, edge leading along +Z, with a short shaft; both faces.</summary>
        private static void Crescent(Mesh mesh)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            const int steps = 10;
            const float outer = 0.75f, inner = 0.48f, thick = 0.03f;
            for (int i = 0; i <= steps; i++)
            {
                float k = i / (float)steps;
                float a = Mathf.Lerp(-1.25f, 1.25f, k);
                float taper = Mathf.Sin(k * Mathf.PI); // pointed ends
                float rIn = Mathf.Lerp(outer, inner, taper);
                v.Add(new Vector3(Mathf.Sin(a) * outer, thick, Mathf.Cos(a) * outer - 0.45f));
                v.Add(new Vector3(Mathf.Sin(a) * rIn, thick, Mathf.Cos(a) * rIn - 0.45f));
            }
            int top = v.Count;
            for (int i = 0; i < top; i++) v.Add(new Vector3(v[i].x, -thick, v[i].z));
            for (int i = 0; i < steps; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                t.AddRange(new[] { a, c, b, b, c, d });
                t.AddRange(new[] { top + a, top + b, top + c, top + b, top + d, top + c });
            }
            // Shaft (a thin box from the blade middle back toward the Knight).
            AddBox(v, t, new Vector3(-0.03f, -0.03f, -0.95f), new Vector3(0.03f, 0.03f, 0.05f));
            Finish(mesh, v, t);
        }

        /// <summary>Kite-shaped shield plate (Sentinel Halo) facing +Z.</summary>
        private static void Plate(Mesh mesh)
        {
            var p = new[]
            {
                new Vector3(0f, 0.32f, 0.04f), new Vector3(0.24f, 0.12f, 0.04f), new Vector3(0.18f, -0.18f, 0.04f),
                new Vector3(0f, -0.36f, 0.04f), new Vector3(-0.18f, -0.18f, 0.04f), new Vector3(-0.24f, 0.12f, 0.04f),
                new Vector3(0f, 0f, 0.09f), // boss of the shield
                new Vector3(0f, 0f, -0.03f),
            };
            var faces = new List<int>();
            for (int i = 0; i < 6; i++) { faces.Add(6); faces.Add(i); faces.Add((i + 1) % 6); }
            for (int i = 0; i < 6; i++) { faces.Add(7); faces.Add((i + 1) % 6); faces.Add(i); }
            Flat(mesh, p, faces.ToArray());
        }

        /// <summary>Colossal sword pointing down (-Y): blade, cross-guard, grip and pommel. About 3.4 m tall.</summary>
        private static void Sword(Mesh mesh)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            AddBox(v, t, new Vector3(-0.16f, 0.3f, -0.035f), new Vector3(0.16f, 2.6f, 0.035f));   // blade
            AddTip(v, t, 0.16f, 0.035f, 0.3f, 0f);                                                // point
            AddBox(v, t, new Vector3(-0.62f, 2.6f, -0.1f), new Vector3(0.62f, 2.75f, 0.1f));      // guard
            AddBox(v, t, new Vector3(-0.06f, 2.75f, -0.06f), new Vector3(0.06f, 3.3f, 0.06f));    // grip
            AddBox(v, t, new Vector3(-0.12f, 3.3f, -0.12f), new Vector3(0.12f, 3.48f, 0.12f));    // pommel
            Finish(mesh, v, t);
        }

        private static void AddTip(List<Vector3> v, List<int> t, float hw, float ht, float top, float tipY)
        {
            int i = v.Count;
            v.Add(new Vector3(-hw, top, -ht)); v.Add(new Vector3(hw, top, -ht));
            v.Add(new Vector3(hw, top, ht)); v.Add(new Vector3(-hw, top, ht));
            v.Add(new Vector3(0f, tipY, 0f));
            t.AddRange(new[] { i, i + 1, i + 4, i + 1, i + 2, i + 4, i + 2, i + 3, i + 4, i + 3, i, i + 4 });
        }

        private static void AddBox(List<Vector3> v, List<int> t, Vector3 min, Vector3 max)
        {
            int i = v.Count;
            for (int k = 0; k < 8; k++)
                v.Add(new Vector3((k & 1) == 0 ? min.x : max.x, (k & 2) == 0 ? min.y : max.y, (k & 4) == 0 ? min.z : max.z));
            int[] q =
            {
                0, 2, 3, 1, // -Z
                4, 5, 7, 6, // +Z
                0, 4, 6, 2, // -X
                1, 3, 7, 5, // +X
                0, 1, 5, 4, // -Y
                2, 6, 7, 3, // +Y
            };
            for (int f = 0; f < 6; f++)
            {
                int a = i + q[f * 4], b = i + q[f * 4 + 1], c = i + q[f * 4 + 2], d = i + q[f * 4 + 3];
                t.AddRange(new[] { a, b, c, a, c, d });
            }
        }

        /// <summary>
        /// Unwelds every triangle (flat shading) and writes the mesh. Convex shapes only: each triangle is turned so its
        /// normal points away from the shape centre (Unity culls the other side).
        /// </summary>
        private static void Flat(Mesh mesh, Vector3[] points, int[] faces)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            Vector3 centre = Vector3.zero;
            foreach (var p in points) centre += p;
            centre /= points.Length;
            for (int i = 0; i + 2 < faces.Length; i += 3)
            {
                Vector3 a = points[faces[i]], b = points[faces[i + 1]], c = points[faces[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, (a + b + c) / 3f - centre) < 0f) { var s = b; b = c; c = s; }
                int k = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
        }

        private static void Finish(Mesh mesh, List<Vector3> v, List<int> t)
        {
            // Flat shading: unweld so hard edges stay crisp.
            var fv = new List<Vector3>(t.Count);
            var ft = new List<int>(t.Count);
            for (int i = 0; i < t.Count; i++) { fv.Add(v[t[i]]); ft.Add(i); }
            mesh.Clear();
            mesh.SetVertices(fv);
            mesh.SetTriangles(ft, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
        }
    }
}
