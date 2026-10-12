using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Combat
{
    /// <summary>
    /// Builds the combat prefabs from code: the crossbow bolt (palette mesh + short glowing trail) and the
    /// "Hollow" enemy (the procedural figure without bow and quiver, pale sickly tint, procedural walk).
    /// Called by GameSceneBuilder; re-run is safe (assets are updated in place).
    /// </summary>
    public static class CombatBuilder
    {
        private const string ArtDir = "Assets/_Project/Art/Combat";
        private const string PrefabDir = "Assets/_Project/Prefabs/Combat";
        private const string BoltMeshPath = ArtDir + "/Bolt_Mesh.asset";
        private const string HollowMeshPath = ArtDir + "/Hollow_Mesh.asset";
        public const string BoltPrefabPath = PrefabDir + "/Bolt.prefab";
        public const string EnemyPrefabPath = PrefabDir + "/Enemy_Hollow.prefab";
        private static readonly Color HollowTint = new Color(0.78f, 0.9f, 0.7f);

        public static void Build(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(ArtDir);
            EditorAssetUtil.EnsureFolder(PrefabDir);
            BuildBolt(kit);
            BuildEnemy(kit);
        }

        /// <summary>Loads the saved prefabs fresh (objects returned at save time can go stale after later imports).</summary>
        public static (Bolt bolt, Enemy enemy) Load() =>
            (AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefabPath).GetComponent<Bolt>(),
             AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath).GetComponent<Enemy>());

        private static void BuildBolt(GameArtKit kit)
        {
            // Points along +Z, origin at the tip's base. 0.45 m long: readable from the top-down camera.
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, 0f, -0.18f), new Vector3(0.028f, 0.028f, 0.42f), Quaternion.identity, PaletteAtlas.For(Swatch.Wood), 0);
            mb.Box(new Vector3(0f, 0f, 0.05f), new Vector3(0.045f, 0.045f, 0.08f), Quaternion.Euler(0f, 0f, 45f), PaletteAtlas.For(Swatch.BrightMetal), 0);
            mb.Box(new Vector3(0f, 0.022f, -0.34f), new Vector3(0.006f, 0.04f, 0.09f), Quaternion.identity, PaletteAtlas.For(Swatch.Fletching), 0);
            mb.Box(new Vector3(0f, -0.022f, -0.34f), new Vector3(0.006f, 0.04f, 0.09f), Quaternion.identity, PaletteAtlas.For(Swatch.Fletching), 0);
            var mesh = EditorAssetUtil.LoadOrCreateMesh(BoltMeshPath);
            mb.WriteTo(mesh);
            EditorUtility.SetDirty(mesh);

            var root = new GameObject("Bolt");
            try
            {
                var model = new GameObject("Mesh");
                model.transform.SetParent(root.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = model.AddComponent<MeshRenderer>();
                r.sharedMaterial = kit.Palette;
                Quiet(r);

                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_BoltTrail.mat", kit.SoftDot, additive: true);
                trail.time = 0.12f;
                trail.minVertexDistance = 0.15f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.09f), new Keyframe(1f, 0f));
                trail.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(1f, 0.85f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.25f), 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) },
                };
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;

                var bolt = root.AddComponent<Bolt>();
                SetRef(bolt, "trail", trail);
                PrefabUtility.SaveAsPrefabAsset(root, BoltPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildEnemy(GameArtKit kit)
        {
            var mat = EditorAssetUtil.SimpleLitMaterial(ArtDir + "/M_Hollow.mat", (Texture2D)kit.Palette.GetTexture("_BaseMap"), HollowTint,
                specMap: (Texture2D)kit.Palette.GetTexture("_SpecGlossMap"));

            var root = new GameObject("Enemy_Hollow");
            try
            {
                var rig = ArcherModelBuilder.BuildModel(root.transform, mat, HollowMeshPath, withGear: false, out _);
                ArcherModelBuilder.AddBlobShadow(root.transform, kit.BlobShadow, kit.Quad);

                var loco = new SerializedObject(root.AddComponent<ProceduralLocomotion>());
                loco.FindProperty("rig").objectReferenceValue = rig;
                loco.FindProperty("runSpeed").floatValue = 3.2f;  // 2.2 m/s chase reads as a heavy jog
                loco.FindProperty("forwardLean").floatValue = 18f; // hunched
                loco.ApplyModifiedPropertiesWithoutUndo();

                var enemy = root.AddComponent<Enemy>();
                var so = new SerializedObject(enemy);
                so.FindProperty("body").objectReferenceValue = root.GetComponentInChildren<SkinnedMeshRenderer>();
                so.FindProperty("baseTint").colorValue = HollowTint;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void SetRef(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
