using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Characters
{
    /// <summary>
    /// Imports a rigged Meshy GLB (single skinned mesh, one PBR material) without extra packages:
    /// parses the binary glTF, converts to Unity space, writes a Mesh asset, mobile textures and a URP Lit
    /// material, then builds the player prefab with the same components as the procedural archer.
    ///
    /// glTF -> Unity: mirror X (x = -x), quaternion (x, -y, -z, w), flip triangle winding, uv.y = 1 - uv.y.
    /// The rig uses generic bone names (Bone_000...), so <see cref="BoneMap"/> names which bone is which.
    /// To use a new export: replace the GLB in ArtSource, check BoneMap, then "Survivor Souls/Rebuild Game Scene".
    /// </summary>
    public static class MeshyCharacterImporter
    {
        public const string SourcePath = "ArtSource/Characters/Archer/Meshy_Archer.glb"; // outside Assets: Unity does not import it
        private const string Dir = "Assets/_Project/Art/Characters/ArcherMeshy";
        private const string MeshPath = Dir + "/ArcherMeshy_Mesh.asset";
        private const string MaterialPath = Dir + "/M_ArcherMeshy.mat";
        public const string PrefabPath = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";
        private const int TextureSize = 1024; // character is ~100 px tall on screen; 1k is plenty on phones

        /// <summary>HumanoidRig field -> glTF node name, for this export (found from bone positions).</summary>
        private static readonly (string field, string bone)[] BoneMap =
        {
            ("hips", "Bone_000"), ("spine", "Bone_005"), ("chest", "Bone_002"), ("neck", "Bone_020"), ("head", "Bone_019"),
            ("upperArmL", "Bone_029"), ("foreArmL", "Bone_028"), ("handL", "Bone_027"),
            ("upperArmR", "Bone_024"), ("foreArmR", "Bone_023"), ("handR", "Bone_022"),
            ("thighL", "Bone_011"), ("shinL", "Bone_010"), ("footL", "Bone_009"),
            ("thighR", "Bone_017"), ("shinR", "Bone_016"), ("footR", "Bone_015"),
        };

        public static bool SourceExists => File.Exists(SourcePath);

        public static GameObject BuildPrefab(Material blobShadow, Mesh quad)
        {
            var glb = Glb.Load(SourcePath);
            EditorAssetUtil.EnsureFolder(Dir);
            var material = CreateMaterial(glb);

            var root = new GameObject("Player_ArcherMeshy");
            try
            {
                var model = new GameObject("Model").transform;
                model.SetParent(root.transform, false);

                // Bone hierarchy from glTF nodes (rest pose = bind pose for Blender exports).
                var skin = glb.Json["skins"][0];
                var jointNodes = skin["joints"].AsInts();
                var nodes = new Dictionary<int, Transform>();
                int armatureNode = glb.ParentOf(jointNodes[0]);
                Transform armature = CreateNode(glb, armatureNode, model, nodes);
                foreach (int j in jointNodes) EnsureNode(glb, j, nodes, armatureNode, armature);
                var bones = new Transform[jointNodes.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = nodes[jointNodes[i]];

                // Centre the hips over the root (the export is off-centre).
                Vector3 hips = FindBone(bones, "Bone_000").position;
                model.localPosition = new Vector3(-hips.x, 0f, -hips.z);

                var mesh = BuildMesh(glb, bones, model);
                var body = new GameObject("Body");
                body.transform.SetParent(model, false);
                var smr = body.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = FindBone(bones, "Bone_000");
                smr.sharedMaterial = material;
                smr.quality = SkinQuality.Bone4;
                smr.shadowCastingMode = ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.lightProbeUsage = LightProbeUsage.Off;
                smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                smr.localBounds = new Bounds(Vector3.zero, new Vector3(2f, 2.4f, 2f));

                var rig = model.gameObject.AddComponent<HumanoidRig>();
                var rigSo = new SerializedObject(rig);
                foreach (var (field, bone) in BoneMap) rigSo.FindProperty(field).objectReferenceValue = FindBone(bones, bone);
                rigSo.ApplyModifiedPropertiesWithoutUndo();

                ArcherModelBuilder.AddBlobShadow(root.transform, blobShadow, quad);
                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.75f;
                cc.radius = 0.32f;
                cc.center = new Vector3(0f, 0.875f, 0f);
                cc.skinWidth = 0.03f;
                cc.stepOffset = 0.3f;
                root.AddComponent<PlayerMover>();
                var loco = new SerializedObject(root.AddComponent<ProceduralLocomotion>());
                loco.FindProperty("rig").objectReferenceValue = rig;
                loco.FindProperty("armWeight").floatValue = 0f; // the model holds its bow in front with both hands
                loco.ApplyModifiedPropertiesWithoutUndo();

                EditorAssetUtil.EnsureFolder("Assets/_Project/Prefabs");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[MeshyCharacterImporter] {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris, {bones.Length} bones -> {PrefabPath}");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------ Nodes

        private static Transform CreateNode(Glb glb, int index, Transform parent, Dictionary<int, Transform> nodes)
        {
            var node = glb.Json["nodes"][index];
            var t = new GameObject(node["name"]?.AsString() ?? $"Node_{index}").transform;
            t.SetParent(parent, false);
            var p = node["translation"]?.AsFloats() ?? new float[] { 0, 0, 0 };
            var r = node["rotation"]?.AsFloats() ?? new float[] { 0, 0, 0, 1 };
            var s = node["scale"]?.AsFloats() ?? new float[] { 1, 1, 1 };
            t.localPosition = new Vector3(-p[0], p[1], p[2]);
            t.localRotation = new Quaternion(r[0], -r[1], -r[2], r[3]);
            t.localScale = new Vector3(s[0], s[1], s[2]);
            nodes[index] = t;
            return t;
        }

        private static Transform EnsureNode(Glb glb, int index, Dictionary<int, Transform> nodes, int armatureNode, Transform armature)
        {
            if (nodes.TryGetValue(index, out var existing)) return existing;
            int parent = glb.ParentOf(index);
            Transform parentT = parent == armatureNode ? armature : EnsureNode(glb, parent, nodes, armatureNode, armature);
            return CreateNode(glb, index, parentT, nodes);
        }

        private static Transform FindBone(Transform[] bones, string name) =>
            Array.Find(bones, b => b.name == name) ?? throw new InvalidOperationException($"Bone {name} not in GLB. Update BoneMap.");

        // ------------------------------------------------------------ Mesh

        private static Mesh BuildMesh(Glb glb, Transform[] bones, Transform model)
        {
            var prim = glb.Json["meshes"][0]["primitives"][0];
            var attrs = prim["attributes"];
            var pos = glb.ReadFloats(attrs["POSITION"].AsInt(), 3);
            var nrm = glb.ReadFloats(attrs["NORMAL"].AsInt(), 3);
            var uv = glb.ReadFloats(attrs["TEXCOORD_0"].AsInt(), 2);
            var joints = glb.ReadInts(attrs["JOINTS_0"].AsInt(), 4);
            var weights = glb.ReadFloats(attrs["WEIGHTS_0"].AsInt(), 4);
            var idx = glb.ReadInts(prim["indices"].AsInt(), 1);
            int n = pos.Length / 3;

            var v = new Vector3[n];
            var vn = new Vector3[n];
            var vt = new Vector2[n];
            var bw = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(-pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                vn[i] = new Vector3(-nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                vt[i] = new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]);
                float sum = weights[i * 4] + weights[i * 4 + 1] + weights[i * 4 + 2] + weights[i * 4 + 3];
                if (sum <= 0f) sum = 1f;
                bw[i] = new BoneWeight
                {
                    boneIndex0 = joints[i * 4], weight0 = weights[i * 4] / sum,
                    boneIndex1 = joints[i * 4 + 1], weight1 = weights[i * 4 + 1] / sum,
                    boneIndex2 = joints[i * 4 + 2], weight2 = weights[i * 4 + 2] / sum,
                    boneIndex3 = joints[i * 4 + 3], weight3 = weights[i * 4 + 3] / sum,
                };
            }
            var tris = new int[idx.Length];
            for (int i = 0; i < idx.Length; i += 3) { tris[i] = idx[i]; tris[i + 1] = idx[i + 2]; tris[i + 2] = idx[i + 1]; }

            // Mesh space = Model space (the Body object sits at the Model origin).
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix * model.localToWorldMatrix;

            var mesh = EditorAssetUtil.LoadOrCreateMesh(MeshPath);
            mesh.Clear();
            mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = v;
            mesh.normals = vn;
            mesh.uv = vt;
            mesh.triangles = tris;
            mesh.boneWeights = bw;
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------ Material + textures

        private static Material CreateMaterial(Glb glb)
        {
            var mat = glb.Json["materials"][0];
            var pbr = mat["pbrMetallicRoughness"];
            var albedo = WriteTexture(glb, pbr["baseColorTexture"]["index"].AsInt(), "T_ArcherMeshy_Albedo", TextureImporterType.Default, true);
            var normal = WriteTexture(glb, mat["normalTexture"]["index"].AsInt(), "T_ArcherMeshy_Normal", TextureImporterType.NormalMap, false);
            var metal = WriteMetallicSmoothness(glb, pbr["metallicRoughnessTexture"]["index"].AsInt(), "T_ArcherMeshy_MetalSmooth");

            var m = EditorAssetUtil.LoadOrCreateMaterial(MaterialPath, "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", metal);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 0.65f); // scales the map; Meshy bakes very glossy metal
            m.SetFloat("_SmoothnessTextureChannel", 0f); // smoothness in metallic alpha
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_EnvironmentReflections", 0f);    // no reflection probes in the arena; saves a sample
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m);
            return m;
        }

        private static byte[] ImageBytes(Glb glb, int textureIndex)
        {
            int image = glb.Json["textures"][textureIndex]["source"].AsInt();
            return glb.BufferView(glb.Json["images"][image]["bufferView"].AsInt());
        }

        private static Texture2D WriteTexture(Glb glb, int textureIndex, string name, TextureImporterType type, bool srgb)
        {
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, ImageBytes(glb, textureIndex));
            return ImportTexture(path, type, srgb);
        }

        /// <summary>glTF metallicRoughness (G = roughness, B = metallic) -> URP (R = metallic, A = smoothness), half size.</summary>
        private static Texture2D WriteMetallicSmoothness(Glb glb, int textureIndex, string name)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            src.LoadImage(ImageBytes(glb, textureIndex));
            int w = Mathf.Max(1, src.width / 2), h = Mathf.Max(1, src.height / 2);
            var s = src.GetPixels32();
            var o = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int metal = 0, rough = 0;
                for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    var c = s[(y * 2 + dy) * src.width + x * 2 + dx];
                    metal += c.b;
                    rough += c.g;
                }
                o[y * w + x] = new Color32((byte)(metal / 4), 0, 0, (byte)(255 - rough / 4));
            }
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            dst.SetPixels32(o);
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, dst.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(src);
            UnityEngine.Object.DestroyImmediate(dst);
            return ImportTexture(path, TextureImporterType.Default, false);
        }

        private static Texture2D ImportTexture(string path, TextureImporterType type, bool srgb)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = TextureSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------ Minimal GLB reader

        /// <summary>Binary glTF 2.0: JSON chunk + one BIN chunk. Only what this importer needs.</summary>
        private sealed class Glb
        {
            public Json Json;
            private byte[] _bin;
            private Dictionary<int, int> _parents;

            public static Glb Load(string path)
            {
                var data = File.ReadAllBytes(path);
                if (BitConverter.ToUInt32(data, 0) != 0x46546C67) throw new InvalidDataException("Not a GLB file: " + path);
                int jsonLength = BitConverter.ToInt32(data, 12);
                var glb = new Glb { Json = Json.Parse(Encoding.UTF8.GetString(data, 20, jsonLength)) };
                int binStart = 20 + jsonLength;
                int binLength = BitConverter.ToInt32(data, binStart);
                glb._bin = new byte[binLength];
                Buffer.BlockCopy(data, binStart + 8, glb._bin, 0, binLength);

                glb._parents = new Dictionary<int, int>();
                var nodes = glb.Json["nodes"];
                for (int i = 0; i < nodes.Count; i++)
                    if (nodes[i]["children"] != null)
                        foreach (int c in nodes[i]["children"].AsInts()) glb._parents[c] = i;
                return glb;
            }

            public int ParentOf(int node) => _parents.TryGetValue(node, out int p) ? p : -1;

            public byte[] BufferView(int index)
            {
                var bv = Json["bufferViews"][index];
                int offset = bv["byteOffset"]?.AsInt() ?? 0;
                var result = new byte[bv["byteLength"].AsInt()];
                Buffer.BlockCopy(_bin, offset, result, 0, result.Length);
                return result;
            }

            public float[] ReadFloats(int accessor, int components)
            {
                var (offset, stride, count, type) = Layout(accessor, components);
                if (type != 5126) throw new NotSupportedException($"Accessor {accessor}: float expected, got {type}");
                var result = new float[count * components];
                for (int i = 0; i < count; i++)
                for (int c = 0; c < components; c++)
                    result[i * components + c] = BitConverter.ToSingle(_bin, offset + i * stride + c * 4);
                return result;
            }

            public int[] ReadInts(int accessor, int components)
            {
                var (offset, stride, count, type) = Layout(accessor, components);
                int size = type == 5121 ? 1 : type == 5123 ? 2 : 4;
                var result = new int[count * components];
                for (int i = 0; i < count; i++)
                for (int c = 0; c < components; c++)
                {
                    int at = offset + i * stride + c * size;
                    result[i * components + c] = size == 1 ? _bin[at] : size == 2 ? BitConverter.ToUInt16(_bin, at) : (int)BitConverter.ToUInt32(_bin, at);
                }
                return result;
            }

            private (int offset, int stride, int count, int type) Layout(int accessor, int components)
            {
                var a = Json["accessors"][accessor];
                var bv = Json["bufferViews"][a["bufferView"].AsInt()];
                int type = a["componentType"].AsInt();
                int size = type == 5121 || type == 5120 ? 1 : type == 5123 || type == 5122 ? 2 : 4;
                int offset = (bv["byteOffset"]?.AsInt() ?? 0) + (a["byteOffset"]?.AsInt() ?? 0);
                int stride = bv["byteStride"]?.AsInt() ?? size * components;
                return (offset, stride, a["count"].AsInt(), type);
            }
        }

        /// <summary>Tiny JSON DOM (objects, arrays, numbers, strings, bools). Enough for glTF headers.</summary>
        private sealed class Json
        {
            private Dictionary<string, Json> _obj;
            private List<Json> _arr;
            private double _num;
            private string _str;

            public Json this[string key] => _obj != null && _obj.TryGetValue(key, out var v) ? v : null;
            public Json this[int i] => _arr[i];
            public int Count => _arr?.Count ?? 0;
            public int AsInt() => (int)_num;
            public string AsString() => _str;
            public int[] AsInts() { var r = new int[_arr.Count]; for (int i = 0; i < r.Length; i++) r[i] = (int)_arr[i]._num; return r; }
            public float[] AsFloats() { var r = new float[_arr.Count]; for (int i = 0; i < r.Length; i++) r[i] = (float)_arr[i]._num; return r; }

            public static Json Parse(string text) { int i = 0; return Value(text, ref i); }

            private static Json Value(string s, ref int i)
            {
                Skip(s, ref i);
                char c = s[i];
                if (c == '{')
                {
                    var j = new Json { _obj = new Dictionary<string, Json>() };
                    i++;
                    while (true)
                    {
                        Skip(s, ref i);
                        if (s[i] == '}') { i++; return j; }
                        string key = Value(s, ref i)._str;
                        Skip(s, ref i); i++; // ':'
                        j._obj[key] = Value(s, ref i);
                        Skip(s, ref i);
                        if (s[i] == ',') i++;
                    }
                }
                if (c == '[')
                {
                    var j = new Json { _arr = new List<Json>() };
                    i++;
                    while (true)
                    {
                        Skip(s, ref i);
                        if (s[i] == ']') { i++; return j; }
                        j._arr.Add(Value(s, ref i));
                        Skip(s, ref i);
                        if (s[i] == ',') i++;
                    }
                }
                if (c == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (s[i] != '"')
                    {
                        if (s[i] == '\\') { i++; sb.Append(s[i] == 'u' ? (char)Convert.ToInt32(s.Substring(i + 1, 4), 16) : s[i]); if (s[i] == 'u') i += 4; }
                        else sb.Append(s[i]);
                        i++;
                    }
                    i++;
                    return new Json { _str = sb.ToString() };
                }
                if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return new Json { _num = 1 }; }
                if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return new Json { _num = 0 }; }
                if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return new Json { _num = double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture) };
            }

            private static void Skip(string s, ref int i) { while (char.IsWhiteSpace(s[i])) i++; }
        }
    }
}
