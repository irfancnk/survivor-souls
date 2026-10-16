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
    /// Imports a rigged Meshy GLB (one skinned body mesh + PBR material) without extra packages:
    /// parses the binary glTF, converts to Unity space, writes a Mesh asset, mobile textures and a URP Lit
    /// material, then builds the player prefab with the same components as the procedural archer.
    /// Non-skinned mesh nodes parented to a bone (the crossbow) become MeshRenderers under that bone, with
    /// their child nodes (Crossbow_Muzzle socket) kept as empty transforms.
    ///
    /// glTF -> Unity: mirror X (x = -x), quaternion (x, -y, -z, w), flip triangle winding, uv.y = 1 - uv.y.
    /// The rig uses Mixamo bone names (mixamorig:*). glTF animations become Generic AnimationClips that play on the
    /// same bones (no retargeting), driven by an Animator blend tree on Speed (Idle / Walking / Running) + a Hit state.
    /// To use a new export: replace the GLB in ArtSource, check BoneMap and clip names, then "Survivor Souls/Rebuild Game Scene".
    /// </summary>
    public static class MeshyCharacterImporter
    {
        public const string SourcePath = "ArtSource/Characters/Archer/Knight_Crossbow.glb"; // outside Assets: Unity does not import it
        private const string Dir = "Assets/_Project/Art/Characters/ArcherMeshy";
        private const string MeshPath = Dir + "/ArcherMeshy_Mesh.asset";
        private const string MaterialPath = Dir + "/M_ArcherMeshy.mat";
        public const string PrefabPath = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";
        private const string ControllerPath = Dir + "/AC_ArcherMeshy.controller";
        private const string UpperBodyMaskPath = Dir + "/AM_ArcherMeshy_UpperBody.mask";
        private const string HipsBone = "mixamorig:Hips";
        private const int TextureSize = 1024; // character is ~100 px tall on screen; 1k is plenty on phones
        private const int AttachmentTextureSize = 512; // weapon covers a few dozen pixels

        /// <summary>HumanoidRig field -> glTF node name (Mixamo naming).</summary>
        private static readonly (string field, string bone)[] BoneMap =
        {
            ("hips", "mixamorig:Hips"), ("spine", "mixamorig:Spine"), ("chest", "mixamorig:Spine2"), ("neck", "mixamorig:Neck"), ("head", "mixamorig:Head"),
            ("upperArmL", "mixamorig:LeftArm"), ("foreArmL", "mixamorig:LeftForeArm"), ("handL", "mixamorig:LeftHand"),
            ("upperArmR", "mixamorig:RightArm"), ("foreArmR", "mixamorig:RightForeArm"), ("handR", "mixamorig:RightHand"),
            ("thighL", "mixamorig:LeftUpLeg"), ("shinL", "mixamorig:LeftLeg"), ("footL", "mixamorig:LeftFoot"),
            ("thighR", "mixamorig:RightUpLeg"), ("shinR", "mixamorig:RightLeg"), ("footR", "mixamorig:RightFoot"),
        };

        // glTF animation names in the Meshy export, and blend thresholds (planar speed, m/s).
        private const string WalkClip = "Walking", RunClip = "Running", HitClip = "Hit_Reaction";
        private const float WalkSpeed = 1.6f, RunSpeed = 5f;
        // Shoulder aim clips (KAN-216, from build_knight_crossbow.py): upper-body layer (AimIdle loop, Lower one shot) + turn step.
        private const string AimClip = "AimIdle", LowerClip = "Lower", TurnStepClip = "TurnStep";
        private const string UpperBodyRoot = "mixamorig:Spine1"; // this bone and everything under it is in the upper-body mask

        public static bool SourceExists => File.Exists(SourcePath);

        public static GameObject BuildPrefab(Material blobShadow, Mesh quad)
        {
            var glb = Glb.Load(SourcePath);
            EditorAssetUtil.EnsureFolder(Dir);
            int bodyNode = FindSkinnedNode(glb);
            int bodyMesh = glb.Json["nodes"][bodyNode]["mesh"].AsInt();
            var material = CreateMaterial(glb, glb.Json["meshes"][bodyMesh]["primitives"][0]["material"].AsInt(),
                "T_ArcherMeshy", MaterialPath, TextureSize);

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
                Vector3 hips = FindBone(bones, HipsBone).position;
                model.localPosition = new Vector3(-hips.x, 0f, -hips.z);

                var mesh = BuildMesh(glb, bodyMesh, bones, model);
                var body = new GameObject("Body");
                body.transform.SetParent(model, false);
                var smr = body.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = FindBone(bones, HipsBone);
                smr.sharedMaterial = material;
                smr.quality = SkinQuality.Bone4;
                smr.shadowCastingMode = ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.lightProbeUsage = LightProbeUsage.Off;
                smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                smr.localBounds = new Bounds(Vector3.zero, new Vector3(2f, 2.4f, 2f));

                BuildAttachments(glb, nodes, armatureNode, armature);

                var rig = model.gameObject.AddComponent<HumanoidRig>();
                var rigSo = new SerializedObject(rig);
                foreach (var (field, bone) in BoneMap) rigSo.FindProperty(field).objectReferenceValue = FindBone(bones, bone);
                rigSo.ApplyModifiedPropertiesWithoutUndo();

                ArcherModelBuilder.AddBlobShadow(root.transform, blobShadow, quad);
                AddHeroLight(root.transform);
                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.75f;
                cc.radius = 0.32f;
                cc.center = new Vector3(0f, 0.875f, 0f);
                cc.skinWidth = 0.03f;
                cc.stepOffset = 0.3f;
                root.AddComponent<PlayerMover>();

                // Authored animations: clips from the GLB, Animator on the Model (curve paths are relative to it).
                var clips = BuildClips(glb, nodes, model);
                var animator = model.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = BuildController(clips, BuildUpperBodyMask(nodes, model));
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var driver = new SerializedObject(root.AddComponent<LocomotionAnimatorDriver>());
                driver.FindProperty("animator").objectReferenceValue = animator;
                driver.ApplyModifiedPropertiesWithoutUndo();
                if (clips.ContainsKey(AimClip))
                {
                    // KAN-216: upper-body aim layer + turn step, driven from the shooter's target.
                    var stance = new SerializedObject(root.AddComponent<SurvivorSouls.Gameplay.Combat.CrossbowStance>());
                    stance.FindProperty("animator").objectReferenceValue = animator;
                    stance.ApplyModifiedPropertiesWithoutUndo();
                }

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

        /// <summary>Soft key light that follows the player so dark armour reads against the stone floor.</summary>
        private static void AddHeroLight(Transform root)
        {
            var go = new GameObject("Hero Light");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0.6f, 2.6f, -1.4f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.85f, 0.88f, 1f);
            light.intensity = 2.2f;
            light.range = 4.5f;
            light.shadows = LightShadows.None;
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

        private static int FindSkinnedNode(Glb glb)
        {
            var nodes = glb.Json["nodes"];
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i]["skin"] != null && nodes[i]["mesh"] != null) return i;
            throw new InvalidOperationException("GLB has no skinned mesh node.");
        }

        /// <summary>
        /// Rigid props held by the rig: mesh nodes without a skin whose parent is a bone. Each gets a static
        /// Mesh asset + material; its child nodes (sockets such as Crossbow_Muzzle) are created as empties.
        /// </summary>
        private static void BuildAttachments(Glb glb, Dictionary<int, Transform> nodes, int armatureNode, Transform armature)
        {
            var json = glb.Json["nodes"];
            for (int i = 0; i < json.Count; i++)
            {
                var node = json[i];
                if (node["mesh"] == null || node["skin"] != null) continue;
                if (!nodes.ContainsKey(glb.ParentOf(i))) continue; // only meshes attached to the skeleton
                var t = EnsureNode(glb, i, nodes, armatureNode, armature);
                int meshIndex = node["mesh"].AsInt();
                var prim = glb.Json["meshes"][meshIndex]["primitives"][0];
                string name = t.name;
                var mat = CreateMaterial(glb, prim["material"].AsInt(), $"T_{name}", $"{Dir}/M_{name}.mat", AttachmentTextureSize);
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = BuildStaticMesh(glb, meshIndex, $"{Dir}/{name}_Mesh.asset");
                var r = t.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                for (int c = 0; c < json.Count; c++)
                    if (glb.ParentOf(c) == i) EnsureNode(glb, c, nodes, armatureNode, armature);
            }
        }

        private static Transform FindBone(Transform[] bones, string name) =>
            Array.Find(bones, b => b.name == name) ?? throw new InvalidOperationException($"Bone {name} not in GLB. Update BoneMap.");

        // ------------------------------------------------------------ Mesh

        private static Mesh BuildMesh(Glb glb, int meshIndex, Transform[] bones, Transform model)
        {
            var prim = glb.Json["meshes"][meshIndex]["primitives"][0];
            var attrs = prim["attributes"];
            var pos = glb.ReadFloats(attrs["POSITION"].AsInt(), 3);
            var nrm = glb.ReadFloats(attrs["NORMAL"].AsInt(), 3);
            var uv = glb.ReadFloats(attrs["TEXCOORD_0"].AsInt(), 2);
            // Meshy can export up to 16 influences (JOINTS_0..3). Mobile skinning uses the strongest 4.
            var jointSets = new List<int[]>();
            var weightSets = new List<float[]>();
            for (int k = 0; attrs["JOINTS_" + k] != null; k++)
            {
                jointSets.Add(glb.ReadInts(attrs["JOINTS_" + k].AsInt(), 4));
                weightSets.Add(glb.ReadFloats(attrs["WEIGHTS_" + k].AsInt(), 4));
            }
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
                bw[i] = TopFourWeights(jointSets, weightSets, i);
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

        /// <summary>Unskinned primitive in its node's local space (same axis conversion as the body).</summary>
        private static Mesh BuildStaticMesh(Glb glb, int meshIndex, string path)
        {
            var prim = glb.Json["meshes"][meshIndex]["primitives"][0];
            var attrs = prim["attributes"];
            var pos = glb.ReadFloats(attrs["POSITION"].AsInt(), 3);
            var nrm = glb.ReadFloats(attrs["NORMAL"].AsInt(), 3);
            var uv = glb.ReadFloats(attrs["TEXCOORD_0"].AsInt(), 2);
            var idx = glb.ReadInts(prim["indices"].AsInt(), 1);
            int n = pos.Length / 3;
            var v = new Vector3[n];
            var vn = new Vector3[n];
            var vt = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(-pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                vn[i] = new Vector3(-nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                vt[i] = new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]);
            }
            var tris = new int[idx.Length];
            for (int i = 0; i < idx.Length; i += 3) { tris[i] = idx[i]; tris[i + 1] = idx[i + 2]; tris[i + 2] = idx[i + 1]; }

            var mesh = EditorAssetUtil.LoadOrCreateMesh(path);
            mesh.Clear();
            mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = v;
            mesh.normals = vn;
            mesh.uv = vt;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static BoneWeight TopFourWeights(List<int[]> jointSets, List<float[]> weightSets, int vertex)
        {
            var influences = new List<(int bone, float weight)>();
            for (int set = 0; set < jointSets.Count; set++)
            for (int c = 0; c < 4; c++)
            {
                float w = weightSets[set][vertex * 4 + c];
                if (w > 0f) influences.Add((jointSets[set][vertex * 4 + c], w));
            }
            influences.Sort((a, b) => b.weight.CompareTo(a.weight));
            while (influences.Count < 4) influences.Add((0, 0f));
            float sum = influences[0].weight + influences[1].weight + influences[2].weight + influences[3].weight;
            if (sum <= 0f) sum = 1f;
            return new BoneWeight
            {
                boneIndex0 = influences[0].bone, weight0 = influences[0].weight / sum,
                boneIndex1 = influences[1].bone, weight1 = influences[1].weight / sum,
                boneIndex2 = influences[2].bone, weight2 = influences[2].weight / sum,
                boneIndex3 = influences[3].bone, weight3 = influences[3].weight / sum,
            };
        }

        // ------------------------------------------------------------ Animation

        /// <summary>
        /// glTF animations -> Generic AnimationClip assets. Hips X/Z translation is held at frame 0 (in-place clips;
        /// PlayerMover moves the character). Generates an "Idle" clip when the export has none.
        /// </summary>
        private static Dictionary<string, AnimationClip> BuildClips(Glb glb, Dictionary<int, Transform> nodes, Transform model)
        {
            var result = new Dictionary<string, AnimationClip>();
            var anims = glb.Json["animations"];
            for (int a = 0; a < (anims?.Count ?? 0); a++)
            {
                var anim = anims[a];
                string name = anim["name"]?.AsString() ?? $"Anim_{a}";
                var clip = new AnimationClip { name = name, frameRate = 30f };
                var channels = anim["channels"];
                var samplers = anim["samplers"];
                for (int c = 0; c < channels.Count; c++)
                {
                    var target = channels[c]["target"];
                    if (!nodes.TryGetValue(target["node"].AsInt(), out var bone)) continue;
                    var sampler = samplers[channels[c]["sampler"].AsInt()];
                    var times = glb.ReadFloats(sampler["input"].AsInt(), 1);
                    string path = AnimationUtility.CalculateTransformPath(bone, model);
                    string channel = target["path"].AsString();
                    bool hips = bone.name == HipsBone;
                    if (channel == "translation") AddVectorCurves(clip, path, times, glb.ReadFloats(sampler["output"].AsInt(), 3), hips);
                    else if (channel == "rotation") AddRotationCurves(clip, path, times, glb.ReadFloats(sampler["output"].AsInt(), 4));
                }
                clip.EnsureQuaternionContinuity();
                SetLoop(clip, name != HitClip && name != LowerClip && name != TurnStepClip);
                result[name] = SaveClip(clip);
            }
            if (!result.ContainsKey("Idle")) result["Idle"] = SaveClip(MakeIdle(nodes, model));
            return result;
        }

        private static void AddVectorCurves(AnimationClip clip, string path, float[] t, float[] v, bool inPlace)
        {
            var x = new AnimationCurve(); var y = new AnimationCurve(); var z = new AnimationCurve();
            for (int i = 0; i < t.Length; i++)
            {
                x.AddKey(new Keyframe(t[i], inPlace ? -v[0] : -v[i * 3]));
                y.AddKey(new Keyframe(t[i], v[i * 3 + 1]));
                z.AddKey(new Keyframe(t[i], inPlace ? v[2] : v[i * 3 + 2]));
            }
            SetLinear(x); SetLinear(y); SetLinear(z);
            clip.SetCurve(path, typeof(Transform), "localPosition.x", x);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", y);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", z);
        }

        private static void AddRotationCurves(AnimationClip clip, string path, float[] t, float[] q)
        {
            var c = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var prev = Quaternion.identity;
            for (int i = 0; i < t.Length; i++)
            {
                var r = new Quaternion(q[i * 4], -q[i * 4 + 1], -q[i * 4 + 2], q[i * 4 + 3]);
                if (i > 0 && Quaternion.Dot(prev, r) < 0f) r = new Quaternion(-r.x, -r.y, -r.z, -r.w); // shortest path
                prev = r;
                c[0].AddKey(t[i], r.x); c[1].AddKey(t[i], r.y); c[2].AddKey(t[i], r.z); c[3].AddKey(t[i], r.w);
            }
            string[] props = { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
            for (int k = 0; k < 4; k++) { SetLinear(c[k]); clip.SetCurve(path, typeof(Transform), props[k], c[k]); }
        }

        /// <summary>
        /// Idle from the bind pose (A-pose): arms lowered toward the body, elbows a little bent, slow breathing
        /// bob on the hips. Arm offsets are in character space (see HumanoidRig.SetRotation for the math).
        /// </summary>
        private static AnimationClip MakeIdle(Dictionary<int, Transform> nodes, Transform model)
        {
            var idle = new AnimationClip { name = "Idle", frameRate = 30f };
            const float length = 3f;
            var offsets = new Dictionary<string, Quaternion>
            {
                ["mixamorig:LeftArm"] = Quaternion.Euler(0f, 0f, 38f),
                ["mixamorig:RightArm"] = Quaternion.Euler(0f, 0f, -38f),
                ["mixamorig:LeftForeArm"] = Quaternion.Euler(-14f, 0f, 0f),
                ["mixamorig:RightForeArm"] = Quaternion.Euler(-14f, 0f, 0f),
            };
            var inv = Quaternion.Inverse(model.rotation);
            foreach (var bone in nodes.Values)
            {
                if (bone == model) continue;
                string path = AnimationUtility.CalculateTransformPath(bone, model);
                var rot = bone.localRotation;
                if (offsets.TryGetValue(bone.name, out var d))
                {
                    var restChar = inv * bone.rotation;
                    rot = rot * (Quaternion.Inverse(restChar) * d * restChar);
                }
                SetConstant(idle, path, "localRotation.x", rot.x, length);
                SetConstant(idle, path, "localRotation.y", rot.y, length);
                SetConstant(idle, path, "localRotation.z", rot.z, length);
                SetConstant(idle, path, "localRotation.w", rot.w, length);
                if (bone.name == HipsBone)
                {
                    var p = bone.localPosition;
                    SetConstant(idle, path, "localPosition.x", p.x, length);
                    SetConstant(idle, path, "localPosition.z", p.z, length);
                    idle.SetCurve(path, typeof(Transform), "localPosition.y",
                        new AnimationCurve(new Keyframe(0f, p.y), new Keyframe(length * 0.5f, p.y - 0.012f), new Keyframe(length, p.y)));
                }
            }
            idle.EnsureQuaternionContinuity();
            SetLoop(idle, true);
            return idle;
        }

        private static void SetConstant(AnimationClip clip, string path, string property, float value, float length) =>
            clip.SetCurve(path, typeof(Transform), property, new AnimationCurve(new Keyframe(0f, value), new Keyframe(length, value)));

        private static void SetLinear(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        }

        private static void SetLoop(AnimationClip clip, bool loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        private static AnimationClip SaveClip(AnimationClip clip)
        {
            string path = $"{Dir}/Anim_{clip.name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) { AssetDatabase.CreateAsset(clip, path); return clip; }
            EditorUtility.CopySerialized(clip, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>
        /// Generic transform mask for the upper-body layer (KAN-216): <see cref="UpperBodyRoot"/> and every node under it
        /// (chest, neck, head, arms, hands, fingers, crossbow); hips, lower spine and legs stay with the base layer.
        /// </summary>
        private static AvatarMask BuildUpperBodyMask(Dictionary<int, Transform> nodes, Transform model)
        {
            var paths = new List<(string path, bool on)>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t == model) continue;
                bool on = false;
                for (var p = t; p != null && p != model; p = p.parent)
                    if (p.name == UpperBodyRoot) { on = true; break; }
                paths.Add((AnimationUtility.CalculateTransformPath(t, model), on));
            }
            var mask = new AvatarMask { name = "AM_ArcherMeshy_UpperBody", transformCount = paths.Count };
            for (int i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i].path);
                mask.SetTransformActive(i, paths[i].on);
            }
            var existing = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (existing == null) { AssetDatabase.CreateAsset(mask, UpperBodyMaskPath); return mask; }
            EditorUtility.CopySerialized(mask, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>
        /// Locomotion blend tree on Speed (Idle 0, Walking, Running) + Hit reaction on trigger. With the KAN-216 clips: a
        /// TurnStep state (trigger) on the base layer and an "UpperBody" override layer (mask, weight set by
        /// <see cref="SurvivorSouls.Gameplay.Combat.CrossbowStance"/>) with Aim (AimIdle loop) and Lower (one shot) on bool "Aim".
        /// </summary>
        private static UnityEditor.Animations.AnimatorController BuildController(Dictionary<string, AnimationClip> clips, AvatarMask upperBody)
        {
            StableAssetWriter.Remember(ControllerPath); // new fileIDs every rebuild: keep the old file when nothing changed (KAN-157)
            AssetDatabase.DeleteAsset(ControllerPath); // rebuilt from scratch each import
            var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            var locoState = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = UnityEditor.Animations.BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips[WalkClip], WalkSpeed);
            tree.AddChild(clips[RunClip], RunSpeed);

            var sm = ctrl.layers[0].stateMachine;
            if (clips.TryGetValue(HitClip, out var hitClip))
            {
                var hit = sm.AddState("Hit");
                hit.motion = hitClip;
                var toHit = sm.AddAnyStateTransition(hit);
                toHit.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Hit");
                toHit.duration = 0.05f;
                var back = hit.AddTransition(locoState);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.15f;
            }
            sm.defaultState = locoState;
            if (clips.TryGetValue(TurnStepClip, out var stepClip) && clips.TryGetValue(AimClip, out var aimClip) && clips.TryGetValue(LowerClip, out var lowerClip))
                AddAimLayers(ctrl, sm, locoState, stepClip, aimClip, lowerClip, upperBody);
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>KAN-216: turn step on the base layer + the upper-body aim layer (Aim / Lower on bool "Aim").</summary>
        private static void AddAimLayers(UnityEditor.Animations.AnimatorController ctrl, UnityEditor.Animations.AnimatorStateMachine sm,
            UnityEditor.Animations.AnimatorState locoState, AnimationClip stepClip, AnimationClip aimClip, AnimationClip lowerClip, AvatarMask upperBody)
        {
            ctrl.AddParameter(SurvivorSouls.Gameplay.Combat.CrossbowStance.AimParam, AnimatorControllerParameterType.Bool);
            ctrl.AddParameter(SurvivorSouls.Gameplay.Combat.CrossbowStance.TurnStepParam, AnimatorControllerParameterType.Trigger);

            var step = sm.AddState(SurvivorSouls.Gameplay.Combat.CrossbowStance.TurnStepState);
            step.motion = stepClip;
            var toStep = locoState.AddTransition(step);
            toStep.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, SurvivorSouls.Gameplay.Combat.CrossbowStance.TurnStepParam);
            toStep.hasExitTime = false;
            toStep.duration = 0.06f;
            var stepBack = step.AddTransition(locoState);
            stepBack.hasExitTime = true;
            stepBack.exitTime = 0.85f;
            stepBack.duration = 0.1f;
            // Leave the step as soon as the Knight runs (the step is an in-place shuffle).
            var stepRun = step.AddTransition(locoState);
            stepRun.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.5f, "Speed");
            stepRun.hasExitTime = false;
            stepRun.duration = 0.1f;

            ctrl.AddLayer(SurvivorSouls.Gameplay.Combat.CrossbowStance.UpperBodyLayer);
            var layers = ctrl.layers;
            var layer = layers[layers.Length - 1];
            layer.avatarMask = upperBody;
            layer.blendingMode = UnityEditor.Animations.AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 0f; // CrossbowStance raises it while aiming / lowering
            ctrl.layers = layers;

            var usm = layer.stateMachine;
            var aim = usm.AddState(SurvivorSouls.Gameplay.Combat.CrossbowStance.AimState);
            aim.motion = aimClip;
            var lower = usm.AddState(SurvivorSouls.Gameplay.Combat.CrossbowStance.LowerState);
            lower.motion = lowerClip;
            usm.defaultState = lower; // at rest the layer sits on the last Lower frame (= hip carry), weight 0
            var toLower = aim.AddTransition(lower);
            toLower.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0f, SurvivorSouls.Gameplay.Combat.CrossbowStance.AimParam);
            toLower.hasExitTime = false;
            toLower.duration = 0.05f;
            var toAim = lower.AddTransition(aim);
            toAim.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, SurvivorSouls.Gameplay.Combat.CrossbowStance.AimParam);
            toAim.hasExitTime = false;
            toAim.duration = 0.12f;
        }

        // ------------------------------------------------------------ Material + textures

        private static Material CreateMaterial(Glb glb, int materialIndex, string texturePrefix, string materialPath, int maxSize)
        {
            var mat = glb.Json["materials"][materialIndex];
            var pbr = mat["pbrMetallicRoughness"];
            var albedo = WriteTexture(glb, pbr["baseColorTexture"]["index"].AsInt(), texturePrefix + "_Albedo", TextureImporterType.Default, true, maxSize);
            var normal = WriteTexture(glb, mat["normalTexture"]["index"].AsInt(), texturePrefix + "_Normal", TextureImporterType.NormalMap, false, maxSize);
            var metal = WriteMetallicSmoothness(glb, pbr["metallicRoughnessTexture"]["index"].AsInt(), texturePrefix + "_MetalSmooth", maxSize);

            var m = EditorAssetUtil.LoadOrCreateMaterial(materialPath, "Universal Render Pipeline/Lit");
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
            EditorAssetUtil.ValidateLikeInspector(m); // URP Lit inspector pass, so the .mat never flips in git (KAN-178)
            return m;
        }

        private static byte[] ImageBytes(Glb glb, int textureIndex)
        {
            int image = glb.Json["textures"][textureIndex]["source"].AsInt();
            return glb.BufferView(glb.Json["images"][image]["bufferView"].AsInt());
        }

        private static Texture2D WriteTexture(Glb glb, int textureIndex, string name, TextureImporterType type, bool srgb, int maxSize)
        {
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, ImageBytes(glb, textureIndex));
            return ImportTexture(path, type, srgb, maxSize);
        }

        /// <summary>glTF metallicRoughness (G = roughness, B = metallic) -> URP (R = metallic, A = smoothness), half size.</summary>
        private static Texture2D WriteMetallicSmoothness(Glb glb, int textureIndex, string name, int maxSize)
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
            return ImportTexture(path, TextureImporterType.Default, false, maxSize);
        }

        private static Texture2D ImportTexture(string path, TextureImporterType type, bool srgb, int maxSize)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
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
