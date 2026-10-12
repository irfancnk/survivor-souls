using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Characters
{
    /// <summary>
    /// Builds the Archer hero from code: a rigid-skinned mesh (one SkinnedMeshRenderer, one material,
    /// one draw call), its bone hierarchy, and the player prefab.
    /// Look follows the Archer concept sheet: dark hood, short beard, layered iron pauldrons, crimson
    /// scarf and belt cloth, criss-cross straps, ragged tunic, bracers, knee pads, back quiver, recurve bow.
    /// Units: metres. Character faces +Z, its left side is -X. Rest pose: arms hang down.
    /// Placeholder until a Meshy model replaces the mesh; the rig fields (HumanoidRig) stay the same.
    /// </summary>
    public static class ArcherModelBuilder
    {
        public const string Dir = "Assets/_Project/Art/Characters/Archer";
        public const string MeshPath = Dir + "/Archer_Mesh.asset";
        public const string PrefabPath = "Assets/_Project/Prefabs/Player_Archer.prefab";

        // Bone indices = order in Bones. Parent index -1 = child of the model root.
        private enum B { Hips, Spine, Chest, Neck, Head, UpperArmL, ForeArmL, HandL, UpperArmR, ForeArmR, HandR, ThighL, ShinL, FootL, ThighR, ShinR, FootR }

        private static readonly (B bone, B? parent, Vector3 pos)[] Bones =
        {
            (B.Hips, null, new Vector3(0f, 0.95f, 0f)),
            (B.Spine, B.Hips, new Vector3(0f, 1.08f, 0f)),
            (B.Chest, B.Spine, new Vector3(0f, 1.28f, 0f)),
            (B.Neck, B.Chest, new Vector3(0f, 1.5f, 0f)),
            (B.Head, B.Neck, new Vector3(0f, 1.58f, 0f)),
            (B.UpperArmL, B.Chest, new Vector3(-0.215f, 1.44f, 0f)),
            (B.ForeArmL, B.UpperArmL, new Vector3(-0.215f, 1.16f, 0f)),
            (B.HandL, B.ForeArmL, new Vector3(-0.215f, 0.93f, 0f)),
            (B.UpperArmR, B.Chest, new Vector3(0.215f, 1.44f, 0f)),
            (B.ForeArmR, B.UpperArmR, new Vector3(0.215f, 1.16f, 0f)),
            (B.HandR, B.ForeArmR, new Vector3(0.215f, 0.93f, 0f)),
            (B.ThighL, B.Hips, new Vector3(-0.1f, 0.93f, 0f)),
            (B.ShinL, B.ThighL, new Vector3(-0.1f, 0.52f, 0f)),
            (B.FootL, B.ShinL, new Vector3(-0.1f, 0.1f, 0f)),
            (B.ThighR, B.Hips, new Vector3(0.1f, 0.93f, 0f)),
            (B.ShinR, B.ThighR, new Vector3(0.1f, 0.52f, 0f)),
            (B.FootR, B.ShinR, new Vector3(0.1f, 0.1f, 0f)),
        };

        private static MeshBuilder _mb;

        /// <summary>Builds mesh + prefab. Needs the shared palette material (see PropKit).</summary>
        public static GameObject BuildPrefab(Material palette, Material blobShadow, Mesh quad)
        {
            var root = new GameObject("Player_Archer");
            try
            {
                var model = new GameObject("Model").transform;
                model.SetParent(root.transform, false);
                var bones = CreateBones(model);

                var bindposes = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix * model.localToWorldMatrix;

                _mb = new MeshBuilder();
                BuildBody();
                BuildHead();
                BuildArms();
                BuildLegs();
                BuildQuiver();
                BuildBow();
                var mesh = EditorAssetUtil.LoadOrCreateMesh(MeshPath);
                _mb.WriteTo(mesh, bindposes);
                EditorUtility.SetDirty(mesh);

                var body = new GameObject("Body");
                body.transform.SetParent(model, false);
                var smr = body.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = bones[(int)B.Hips];
                smr.sharedMaterial = palette;
                smr.quality = SkinQuality.Bone1;
                smr.shadowCastingMode = ShadowCastingMode.Off; // blob shadow instead
                smr.receiveShadows = false;
                smr.lightProbeUsage = LightProbeUsage.Off;
                smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                smr.localBounds = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(1.8f, 2.2f, 1.8f)); // relative to hips, covers bow swing

                var rig = model.gameObject.AddComponent<HumanoidRig>();
                AssignRig(rig, bones);

                AddBlobShadow(root.transform, blobShadow, quad);

                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.32f;
                cc.center = new Vector3(0f, 0.9f, 0f);
                cc.skinWidth = 0.03f;
                cc.stepOffset = 0.3f;
                root.AddComponent<PlayerMover>();
                var locomotion = root.AddComponent<ProceduralLocomotion>();
                var so = new SerializedObject(locomotion);
                so.FindProperty("rig").objectReferenceValue = rig;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorAssetUtil.EnsureFolder("Assets/_Project/Prefabs");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[ArcherModelBuilder] {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris -> {PrefabPath}");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
                _mb = null;
            }
        }

        private static Transform[] CreateBones(Transform model)
        {
            var result = new Transform[Bones.Length];
            foreach (var (bone, parent, pos) in Bones)
            {
                var t = new GameObject(bone.ToString()).transform;
                t.SetParent(parent.HasValue ? result[(int)parent.Value] : model, false);
                t.position = model.TransformPoint(pos);
                result[(int)bone] = t;
            }
            return result;
        }

        private static void AssignRig(HumanoidRig rig, Transform[] b)
        {
            rig.hips = b[(int)B.Hips]; rig.spine = b[(int)B.Spine]; rig.chest = b[(int)B.Chest];
            rig.neck = b[(int)B.Neck]; rig.head = b[(int)B.Head];
            rig.upperArmL = b[(int)B.UpperArmL]; rig.foreArmL = b[(int)B.ForeArmL]; rig.handL = b[(int)B.HandL];
            rig.upperArmR = b[(int)B.UpperArmR]; rig.foreArmR = b[(int)B.ForeArmR]; rig.handR = b[(int)B.HandR];
            rig.thighL = b[(int)B.ThighL]; rig.shinL = b[(int)B.ShinL]; rig.footL = b[(int)B.FootL];
            rig.thighR = b[(int)B.ThighR]; rig.shinR = b[(int)B.ShinR]; rig.footR = b[(int)B.FootR];
        }

        private static void AddBlobShadow(Transform root, Material mat, Mesh quad)
        {
            var go = new GameObject("BlobShadow");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(1.25f, 1.25f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ------------------------------------------------------------ Parts

        private static void BuildBody()
        {
            // Pants / pelvis.
            Lathe(Vector3.zero, Swatch.Pants, B.Hips, new Vector2(1f, 0.74f), P(0.84f, 0.13f, 0.92f, 0.152f, 1.0f, 0.158f, 1.06f, 0.152f), capBottom: true);

            // Ragged tunic skirt: jagged hem, raised at the sides so the legs can swing.
            Lathe(Vector3.zero, Swatch.TunicCloth, B.Hips, new Vector2(1f, 0.8f), P(0.72f, 0.215f, 0.86f, 0.192f, 1.0f, 0.165f),
                segments: 18, rowShape: (row, theta) =>
                {
                    if (row != 0) return new Vector2(1f, 0f);
                    int s = Mathf.RoundToInt(theta / (Mathf.PI * 2f) * 18f);
                    float side = Mathf.Pow(Mathf.Abs(Mathf.Sin(theta)), 4f) * 0.1f; // slits at +-X
                    float jag = (s % 2) * 0.045f + TextureFactory.Hash(s % 18, 3, 9) * 0.04f;
                    return new Vector2(1f, side + jag);
                });

            // Belt, buckle, pouches.
            Lathe(Vector3.zero, Swatch.Leather, B.Hips, new Vector2(1f, 0.8f), P(0.985f, 0.17f, 1.06f, 0.17f), segments: 16);
            Box(new Vector3(0f, 1.022f, 0.14f), new Vector3(0.05f, 0.045f, 0.018f), Quaternion.identity, Swatch.BrightMetal, B.Hips);
            Box(new Vector3(0.135f, 0.97f, 0.1f), new Vector3(0.075f, 0.085f, 0.05f), Quaternion.Euler(0f, 38f, 0f), Swatch.Leather, B.Hips);
            Box(new Vector3(-0.165f, 0.975f, 0.03f), new Vector3(0.05f, 0.085f, 0.075f), Quaternion.Euler(0f, 10f, 0f), Swatch.DarkLeather, B.Hips);
            Box(new Vector3(0.1f, 0.975f, -0.13f), new Vector3(0.085f, 0.075f, 0.05f), Quaternion.Euler(0f, -25f, 0f), Swatch.Leather, B.Hips);

            // Crimson cloth hanging from the belt (front left).
            Box(new Vector3(-0.075f, 0.88f, 0.178f), new Vector3(0.08f, 0.23f, 0.012f), Quaternion.Euler(-6f, 0f, 5f), Swatch.Crimson, B.Hips);
            Box(new Vector3(-0.04f, 0.86f, 0.182f), new Vector3(0.04f, 0.17f, 0.01f), Quaternion.Euler(-5f, 0f, -7f), Swatch.Crimson, B.Hips);

            // Torso (tunic).
            Lathe(Vector3.zero, Swatch.TunicCloth, B.Spine, new Vector2(1f, 0.68f), P(1.0f, 0.155f, 1.12f, 0.16f, 1.25f, 0.174f));
            Lathe(Vector3.zero, Swatch.TunicCloth, B.Chest, new Vector2(1f, 0.7f), P(1.23f, 0.174f, 1.34f, 0.192f, 1.43f, 0.192f, 1.5f, 0.12f), capTop: true);

            // Criss-cross leather straps, front and back.
            foreach (float z in new[] { 0.128f, -0.128f })
            {
                Box(new Vector3(0f, 1.27f, z), new Vector3(0.048f, 0.5f, 0.02f), Quaternion.Euler(0f, 0f, 33f), Swatch.Leather, B.Chest);
                Box(new Vector3(0f, 1.27f, z * 1.05f), new Vector3(0.042f, 0.5f, 0.02f), Quaternion.Euler(0f, 0f, -33f), Swatch.DarkLeather, B.Chest);
            }
            Box(new Vector3(0f, 1.27f, 0.142f), new Vector3(0.045f, 0.045f, 0.015f), Quaternion.Euler(0f, 0f, 45f), Swatch.Gunmetal, B.Chest); // strap ring

            // Cowl draped over the shoulders, then the crimson scarf over it.
            Lathe(Vector3.zero, Swatch.HoodCloth, B.Chest, new Vector2(1f, 0.78f), P(1.35f, 0.225f, 1.42f, 0.205f, 1.48f, 0.16f), segments: 16,
                rowShape: (row, theta) => row == 0 ? new Vector2(1f, Mathf.Abs(Mathf.Sin(theta * 3f)) * 0.03f) : new Vector2(1f, 0f));
            Lathe(Vector3.zero, Swatch.Crimson, B.Chest, new Vector2(1f, 0.9f), P(1.44f, 0.16f, 1.47f, 0.168f, 1.52f, 0.15f, 1.56f, 0.105f), segments: 16,
                rowShape: (row, theta) => new Vector2(1f + 0.07f * Mathf.Sin(theta * 5f + row * 1.3f), 0f));
            Box(new Vector3(0.06f, 1.37f, 0.148f), new Vector3(0.07f, 0.17f, 0.018f), Quaternion.Euler(-14f, 0f, -10f), Swatch.Crimson, B.Chest);
            Box(new Vector3(0.085f, 1.3f, 0.158f), new Vector3(0.045f, 0.1f, 0.014f), Quaternion.Euler(-12f, 0f, -18f), Swatch.Crimson, B.Chest);

            // Layered iron pauldrons: three overlapping dome plates per side, outer edge drooping.
            var dome = P(0f, 0.118f, 0.012f, 0.118f, 0.03f, 0.104f, 0.05f, 0.075f, 0.064f, 0.04f, 0.07f, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                Lathe(new Vector3(side * 0.225f, 1.455f, 0f), Swatch.Gunmetal, B.Chest, new Vector2(1f, 1.1f), dome, segments: 14, capBottom: true, rotation: Quaternion.Euler(0f, 0f, -side * 18f));
                Lathe(new Vector3(side * 0.262f, 1.395f, 0f), Swatch.Rust, B.Chest, new Vector2(0.95f, 1.05f), dome, segments: 14, capBottom: true, rotation: Quaternion.Euler(0f, 0f, -side * 34f));
                Lathe(new Vector3(side * 0.29f, 1.335f, 0f), Swatch.Gunmetal, B.Chest, new Vector2(0.85f, 0.95f), dome, segments: 14, capBottom: true, rotation: Quaternion.Euler(0f, 0f, -side * 48f));
                Ellipsoid(new Vector3(side * 0.228f, 1.528f, 0.0f), new Vector3(0.018f, 0.012f, 0.018f), Quaternion.identity, Swatch.BrightMetal, B.Chest, 6, 4); // rivet
            }
        }

        private static void BuildHead()
        {
            Lathe(Vector3.zero, Swatch.Skin, B.Neck, Vector2.one, P(1.48f, 0.05f, 1.6f, 0.046f), segments: 8);

            Ellipsoid(new Vector3(0f, 1.665f, 0.005f), new Vector3(0.074f, 0.1f, 0.088f), Quaternion.identity, Swatch.Skin, B.Head);
            Ellipsoid(new Vector3(0f, 1.602f, 0.035f), new Vector3(0.07f, 0.052f, 0.062f), Quaternion.Euler(10f, 0f, 0f), Swatch.Beard, B.Head);
            Box(new Vector3(0f, 1.625f, 0.094f), new Vector3(0.06f, 0.013f, 0.016f), Quaternion.identity, Swatch.Beard, B.Head); // moustache
            Box(new Vector3(0f, 1.656f, 0.1f), new Vector3(0.022f, 0.042f, 0.03f), Quaternion.Euler(-12f, 0f, 0f), Swatch.Skin, B.Head); // nose
            foreach (float side in new[] { -1f, 1f })
            {
                Ellipsoid(new Vector3(side * 0.033f, 1.676f, 0.088f), new Vector3(0.014f, 0.008f, 0.007f), Quaternion.identity, Swatch.HoodInner, B.Head, 6, 4);
                Box(new Vector3(side * 0.034f, 1.69f, 0.092f), new Vector3(0.036f, 0.01f, 0.014f), Quaternion.Euler(0f, 0f, side * -8f), Swatch.Beard, B.Head); // brow
            }

            // Hood: outer cloth shell + dark inner shell, front opening around the face.
            // Narrow face opening: the hood shades the brow and cheeks.
            bool Open(Vector3 n) => !(n.z > 0.55f && n.y < 0.3f && n.y > -0.8f);
            var hoodCentre = new Vector3(0f, 1.69f, 0.0f);
            var hoodRadii = new Vector3(0.118f, 0.145f, 0.13f);
            var hoodTilt = Quaternion.Euler(16f, 0f, 0f);
            _mb.Ellipsoid(hoodCentre, hoodRadii, hoodTilt, PaletteAtlas.For(Swatch.HoodCloth), (int)B.Head, 14, 10, Open);
            _mb.Ellipsoid(hoodCentre, hoodRadii * 0.94f, hoodTilt, PaletteAtlas.For(Swatch.HoodInner), (int)B.Head, 14, 10, Open, inward: true);
        }

        private static void BuildArms()
        {
            foreach (float side in new[] { -1f, 1f })
            {
                bool left = side < 0f;
                B upper = left ? B.UpperArmL : B.UpperArmR;
                B fore = left ? B.ForeArmL : B.ForeArmR;
                B hand = left ? B.HandL : B.HandR;
                float x = side * 0.215f;

                Lathe(new Vector3(x, 1.17f, 0f), Swatch.HoodCloth, upper, Vector2.one, P(0f, 0.054f, 0.14f, 0.06f, 0.27f, 0.066f), segments: 10);
                Ellipsoid(new Vector3(x, 1.16f, 0f), new Vector3(0.056f, 0.056f, 0.056f), Quaternion.identity, Swatch.HoodCloth, upper, 8, 6);

                Lathe(new Vector3(x, 1.08f, 0f), Swatch.DarkLeather, fore, Vector2.one, P(0f, 0.048f, 0.07f, 0.049f), segments: 10);
                Lathe(new Vector3(x, 0.955f, 0f), Swatch.Gunmetal, fore, Vector2.one, P(0f, 0.05f, 0.04f, 0.058f, 0.15f, 0.064f, 0.18f, 0.062f), segments: 10, capBottom: true);
                Lathe(new Vector3(x, 1.12f, 0f), Swatch.Rust, fore, Vector2.one, P(0f, 0.065f, 0.025f, 0.065f), segments: 10);

                Ellipsoid(new Vector3(x, 0.892f, 0.006f), new Vector3(0.036f, 0.05f, 0.046f), Quaternion.identity, Swatch.DarkLeather, hand, 8, 6);
                Ellipsoid(new Vector3(x, 0.848f, 0.018f), new Vector3(0.03f, 0.026f, 0.038f), Quaternion.Euler(20f, 0f, 0f), Swatch.Skin, hand, 8, 6);
            }
        }

        private static void BuildLegs()
        {
            foreach (float side in new[] { -1f, 1f })
            {
                bool left = side < 0f;
                B thigh = left ? B.ThighL : B.ThighR;
                B shin = left ? B.ShinL : B.ShinR;
                B foot = left ? B.FootL : B.FootR;
                float x = side * 0.1f;

                Lathe(new Vector3(x, 0.52f, 0f), Swatch.Pants, thigh, Vector2.one, P(0f, 0.066f, 0.2f, 0.078f, 0.41f, 0.088f), segments: 10);

                Ellipsoid(new Vector3(x, 0.525f, 0.045f), new Vector3(0.056f, 0.07f, 0.042f), Quaternion.Euler(-10f, 0f, 0f), Swatch.Gunmetal, shin, 8, 6);
                Lathe(new Vector3(x, 0.38f, 0f), Swatch.Pants, shin, Vector2.one, P(0f, 0.056f, 0.15f, 0.064f), segments: 10);
                Lathe(new Vector3(x, 0.06f, 0f), Swatch.Leather, shin, new Vector2(1f, 1.05f), P(0f, 0.058f, 0.2f, 0.057f, 0.33f, 0.064f, 0.36f, 0.068f), segments: 10);
                Lathe(new Vector3(x, 0.4f, 0f), Swatch.DarkLeather, shin, Vector2.one, P(0f, 0.07f, 0.045f, 0.072f), segments: 10);

                Ellipsoid(new Vector3(x, 0.058f, 0.045f), new Vector3(0.058f, 0.055f, 0.13f), Quaternion.identity, Swatch.Leather, foot, 10, 6);
                Box(new Vector3(x, 0.013f, 0.04f), new Vector3(0.11f, 0.026f, 0.26f), Quaternion.identity, Swatch.DarkLeather, foot);
            }
        }

        private static void BuildQuiver()
        {
            // Hangs on the back, top over the right shoulder.
            var bottom = new Vector3(-0.09f, 1.03f, -0.2f);
            var top = new Vector3(0.13f, 1.56f, -0.21f);
            var axis = (top - bottom).normalized;
            float length = (top - bottom).magnitude;
            var rot = Quaternion.FromToRotation(Vector3.up, axis);

            Lathe(bottom, Swatch.Leather, B.Chest, Vector2.one, P(0f, 0.056f, 0.03f, 0.062f, length - 0.02f, 0.068f, length, 0.07f), segments: 12, capBottom: true, rotation: rot);
            Lathe(bottom, Swatch.Gunmetal, B.Chest, Vector2.one, P(0f, 0.064f, 0.045f, 0.064f), segments: 12, rotation: rot);
            Lathe(bottom + axis * (length - 0.05f), Swatch.Gunmetal, B.Chest, Vector2.one, P(0f, 0.073f, 0.045f, 0.073f), segments: 12, rotation: rot);

            // Arrows: shafts fan out of the top, dark-red fletching.
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var offset = rot * new Vector3(Mathf.Cos(a) * 0.03f, 0f, Mathf.Sin(a) * 0.03f);
                var dir = (axis + offset * 1.5f).normalized;
                var start = top - axis * 0.12f + offset;
                var end = start + dir * (0.3f + (i % 3) * 0.025f);
                _mb.Tube(new[] { start, end }, _ => 0.007f, PaletteAtlas.For(Swatch.Wood), (int)B.Chest, 4);

                var fletchRot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, i * 40f, 0f);
                var fletchPos = end - dir * 0.055f;
                Box(fletchPos, new Vector3(0.036f, 0.09f, 0.004f), fletchRot, Swatch.Fletching, B.Chest);
                Box(fletchPos, new Vector3(0.004f, 0.09f, 0.036f), fletchRot, Swatch.Fletching, B.Chest);
            }
        }

        private static void BuildBow()
        {
            // Recurve bow in the left hand. Bow plane = YZ; limbs bend back toward the string, tips recurve forward.
            var grip = new Vector3(-0.215f, 0.875f, 0.02f);
            var rot = Quaternion.Euler(8f, 0f, 0f);
            Vector3 At(float t)
            {
                float a = Mathf.Abs(t);
                float z = -0.1f * t * t + 0.09f * Mathf.Pow(Mathf.Max(0f, a - 0.72f), 2f) / 0.078f;
                return grip + rot * new Vector3(0f, t * 0.62f, z + 0.02f);
            }

            const int n = 18;
            var limb = new List<Vector3>();
            for (int i = 0; i <= n; i++) limb.Add(At(-1f + 2f * i / n));
            _mb.Tube(limb, u => Mathf.Lerp(0.019f, 0.008f, Mathf.Abs(u * 2f - 1f)), PaletteAtlas.For(Swatch.Wood), (int)B.HandL, 6);

            _mb.Tube(new[] { At(-0.12f), At(0f), At(0.12f) }, _ => 0.024f, PaletteAtlas.For(Swatch.DarkLeather), (int)B.HandL, 6);
            foreach (float t in new[] { -0.17f, 0.17f, -0.55f, 0.55f })
                _mb.Tube(new[] { At(t - 0.02f), At(t + 0.02f) }, _ => 0.021f, PaletteAtlas.For(Swatch.Gunmetal), (int)B.HandL, 6);
            foreach (float t in new[] { -1f, 1f })
                Ellipsoid(At(t), Vector3.one * 0.014f, Quaternion.identity, Swatch.BrightMetal, B.HandL, 6, 4);

            var stringBack = rot * new Vector3(0f, 0f, -0.012f);
            _mb.Tube(new[] { At(-0.97f) + stringBack, At(0.97f) + stringBack }, _ => 0.0028f, PaletteAtlas.For(Swatch.TunicCloth), (int)B.HandL, 4, caps: false);
        }

        // ------------------------------------------------------------ Shorthands

        /// <summary>Profile from flat (height, radius) pairs, bottom to top.</summary>
        private static Vector2[] P(params float[] hr)
        {
            var result = new Vector2[hr.Length / 2];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector2(hr[i * 2], hr[i * 2 + 1]);
            return result;
        }

        private static void Lathe(Vector3 origin, Swatch swatch, B bone, Vector2 scaleXZ, Vector2[] profile, int segments = 14,
            bool capBottom = false, bool capTop = false, System.Func<int, float, Vector2> rowShape = null, Quaternion? rotation = null)
        {
            _mb.Lathe(origin, rotation ?? Quaternion.identity, profile, PaletteAtlas.For(swatch), (int)bone, segments, scaleXZ, capBottom, capTop, rowShape);
        }

        private static void Ellipsoid(Vector3 c, Vector3 r, Quaternion rot, Swatch swatch, B bone, int segments = 12, int rings = 8)
        {
            _mb.Ellipsoid(c, r, rot, PaletteAtlas.For(swatch), (int)bone, segments, rings);
        }

        private static void Box(Vector3 c, Vector3 size, Quaternion rot, Swatch swatch, B bone)
        {
            _mb.Box(c, size, rot, PaletteAtlas.For(swatch), (int)bone);
        }
    }
}
