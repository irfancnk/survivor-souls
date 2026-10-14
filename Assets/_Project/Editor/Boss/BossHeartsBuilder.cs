using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Boss
{
    /// <summary>
    /// Healing hearts of the boss fight (KAN-60): the rules asset (<see cref="HeartRules"/>, made once, never overwritten),
    /// the ember heart prefab and the <see cref="BossHearts"/> component on the "Boss" object. Called by <see cref="BossBuilder"/>.
    /// Look: a puffy heart (pillow mesh from the classic heart curve, ~0.7 m) on Simple Lit with a radial gradient (bright
    /// amber core, crimson body, gold rim) used as base and emission map, so it glows red-gold on snow and on stone in one
    /// draw call; it leans back towards the camera. Under it an alpha-blended red ground glow (additive would vanish on snow).
    /// Debug menu (Play mode): "Survivor Souls/Debug/Boss: Drop Heart".
    /// </summary>
    public static class BossHeartsBuilder
    {
        public const string RulesPath = "Assets/_Project/Data/Boss/HeartRules_Realm1.asset";
        private const string ArtDir = "Assets/_Project/Art/Boss";
        private const string GradientPath = ArtDir + "/T_HeartEmber.png";
        private const string PrefabDir = "Assets/_Project/Prefabs/Boss";
        public const string PrefabPath = PrefabDir + "/Heart_Ember.prefab";
        private const float HeartHeight = 0.72f;  // m, notch to tip
        private const float HalfDepth = 0.15f;    // m, pillow thickness each side
        private const float LeanBack = 40f;       // deg towards the camera (pitch 55)
        private const int OutlinePoints = 56;
        private const int Rings = 5;

        public static BossHearts Create(GameArtKit kit, GameObject bossRoot, BossFight fight, PlayerHealth player)
        {
            var rules = LoadOrCreateRules();
            var prefab = BuildPrefab(kit);
            var hearts = bossRoot.AddComponent<BossHearts>();
            var so = new SerializedObject(hearts);
            so.FindProperty("rules").objectReferenceValue = rules;
            so.FindProperty("heartPrefab").objectReferenceValue = prefab;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("fight").objectReferenceValue = fight;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hearts;
        }

        private static HeartRules LoadOrCreateRules()
        {
            var rules = AssetDatabase.LoadAssetAtPath<HeartRules>(RulesPath);
            if (rules != null) return rules;
            EditorAssetUtil.EnsureFolder(System.IO.Path.GetDirectoryName(RulesPath).Replace('\\', '/'));
            // First guesses (KAN-22 rules; KAN-62 balances): 20-25 s, x2 under 30 % HP, cap 2, 15 s, heal 25 %.
            rules = HeartRules.Create();
            AssetDatabase.CreateAsset(rules, RulesPath);
            AssetDatabase.SaveAssets();
            return rules;
        }

        public static HealingHeart BuildPrefab(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(PrefabDir);
            var gradient = TextureFactory.Write(GradientPath, 64, 4, GradientPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: 64);
            var heartMat = EditorAssetUtil.SimpleLitMaterial(ArtDir + "/M_HeartEmber.mat", gradient, Color.white, smoothness: 0.7f,
                specColor: new Color(1f, 0.85f, 0.6f));
            heartMat.SetTexture("_EmissionMap", gradient);
            heartMat.SetColor("_EmissionColor", new Color(1f, 0.36f, 0.1f));
            heartMat.EnableKeyword("_EMISSION");
            heartMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(heartMat);
            var glowMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_HeartGlow.mat", kit.SoftDot, additive: false);
            var mesh = EditorAssetUtil.LoadOrCreateMesh(ArtDir + "/Mesh_Heart.asset");
            WriteHeart(mesh);

            var root = new GameObject("Heart_Ember");
            try
            {
                var pivot = new GameObject("Pivot").transform;
                pivot.SetParent(root.transform, false);
                var tilt = new GameObject("Tilt").transform;
                tilt.SetParent(pivot, false);
                tilt.localRotation = Quaternion.Euler(LeanBack, 0f, 0f);
                var heart = Part("Heart", tilt, mesh, heartMat);
                var glow = Part("Glow", root.transform, kit.Quad, glowMat);
                glow.transform.localPosition = Vector3.up * 0.04f;
                glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var component = root.AddComponent<HealingHeart>();
                component.Setup(pivot, heart, glow);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return prefab.GetComponent<HealingHeart>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static MeshRenderer Part(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>
        /// Pillow heart: the outline of the classic heart curve (x = 16 sin^3 t, y = 13 cos t - 5 cos 2t - 2 cos 3t - cos 4t),
        /// shrunk in rings towards an inner point on an elliptic profile, front (-Z, faces the camera) and back. Outline shared,
        /// smooth normals. UV.x = ring share (0 core .. 1 rim) samples the radial gradient.
        /// </summary>
        private static void WriteHeart(Mesh mesh)
        {
            float scale = HeartHeight / 29f; // curve spans y -17..12
            var centre = new Vector2(0f, -1.5f);
            var outline = new Vector2[OutlinePoints];
            for (int i = 0; i < OutlinePoints; i++)
            {
                float t = i * Mathf.PI * 2f / OutlinePoints;
                float s = Mathf.Sin(t);
                outline[i] = new Vector2(16f * s * s * s,
                    13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t));
            }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 Point(Vector2 p, float share, float z) =>
                new Vector3((centre.x + (p.x - centre.x) * share) * scale, (centre.y + (p.y - centre.y) * share + 2.5f) * scale, z);

            for (int i = 0; i < OutlinePoints; i++) // ring 0: the shared outline
            {
                verts.Add(Point(outline[i], 1f, 0f));
                uvs.Add(new Vector2(1f, 0.5f));
            }
            foreach (float side in new[] { -1f, 1f }) // front (-Z), back (+Z)
            {
                int previous = 0; // first vertex of the ring outside this one
                for (int k = 1; k <= Rings; k++)
                {
                    float a = k / (float)Rings * Mathf.PI * 0.5f;
                    float share = Mathf.Cos(a), z = side * HalfDepth * Mathf.Sin(a);
                    int start = verts.Count;
                    if (k == Rings)
                    {
                        verts.Add(Point(Vector2.zero, 0f, z));
                        uvs.Add(new Vector2(0f, 0.5f));
                        for (int i = 0; i < OutlinePoints; i++)
                            Tri(tris, side, start, previous + i, previous + (i + 1) % OutlinePoints);
                        break;
                    }
                    for (int i = 0; i < OutlinePoints; i++)
                    {
                        verts.Add(Point(outline[i], share, z));
                        uvs.Add(new Vector2(share, 0.5f));
                    }
                    for (int i = 0; i < OutlinePoints; i++)
                    {
                        int j = (i + 1) % OutlinePoints;
                        Tri(tris, side, start + i, previous + i, previous + j);
                        Tri(tris, side, start + i, previous + j, start + j);
                    }
                    previous = start;
                }
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);
        }

        /// <summary>Front (side -1) keeps the order (clockwise seen from -Z); the back flips it.</summary>
        private static void Tri(List<int> tris, float side, int a, int b, int c)
        {
            tris.Add(a);
            if (side < 0f)
            {
                tris.Add(b);
                tris.Add(c);
            }
            else
            {
                tris.Add(c);
                tris.Add(b);
            }
        }

        /// <summary>u = 0 core .. 1 rim: bright amber core, crimson body, deep crimson, gold rim.</summary>
        private static Color GradientPixel(int x, int y)
        {
            float u = (x + 0.5f) / 64f;
            var core = new Color(1f, 0.66f, 0.3f);
            var body = new Color(0.8f, 0.1f, 0.08f);
            var deep = new Color(0.48f, 0.04f, 0.05f);
            var rim = new Color(1f, 0.74f, 0.28f);
            if (u < 0.45f) return Color.Lerp(core, body, TextureFactory.SmoothStep(0.05f, 0.45f, u));
            if (u < 0.85f) return Color.Lerp(body, deep, TextureFactory.SmoothStep(0.45f, 0.85f, u));
            return Color.Lerp(deep, rim, TextureFactory.SmoothStep(0.86f, 0.97f, u));
        }

        [MenuItem("Survivor Souls/Debug/Boss: Drop Heart")]
        private static void DropHeart()
        {
            var hearts = Object.FindAnyObjectByType<BossHearts>();
            if (hearts != null && hearts.DropNow() == null) Debug.Log("[Hearts] No drop: cap reached or no free ground.");
        }

        [MenuItem("Survivor Souls/Debug/Boss: Drop Heart", true)]
        private static bool InPlayMode() => Application.isPlaying;
    }
}
