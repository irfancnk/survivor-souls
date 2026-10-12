using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
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
            // Points along +Z, origin at the head's base. 0.5 m long so it reads from the top-down camera:
            // dark wooden shaft, steel bodkin head (crossed blades), three red vanes, steel nock.
            var mb = new MeshBuilder();
            var wood = PaletteAtlas.For(Swatch.Wood);
            var steel = PaletteAtlas.For(Swatch.BrightMetal);
            var vane = PaletteAtlas.For(Swatch.Fletching);
            mb.Tube(new[] { new Vector3(0f, 0f, -0.42f), new Vector3(0f, 0f, 0.02f) }, t => 0.012f, wood, 0, sides: 6);
            mb.Tube(new[] { new Vector3(0f, 0f, -0.01f), new Vector3(0f, 0f, 0.04f) }, t => 0.017f, steel, 0, sides: 6);
            for (int i = 0; i < 2; i++)
            {
                var r = Quaternion.Euler(0f, 0f, i * 90f);
                DoubleQuad(mb, r * new Vector3(0f, 0.032f, 0.04f), r * new Vector3(0f, 0f, 0.15f), r * new Vector3(0f, -0.032f, 0.04f),
                    r * new Vector3(0f, 0f, 0.03f), steel);
            }
            for (int i = 0; i < 3; i++)
            {
                var r = Quaternion.Euler(0f, 0f, i * 120f);
                DoubleQuad(mb, r * new Vector3(0f, 0.012f, -0.4f), r * new Vector3(0f, 0.012f, -0.3f), r * new Vector3(0f, 0.045f, -0.33f),
                    r * new Vector3(0f, 0.05f, -0.41f), vane);
            }
            mb.Box(new Vector3(0f, 0f, -0.425f), new Vector3(0.03f, 0.03f, 0.02f), Quaternion.identity, steel, 0);
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

                // Thin hot streak: white core fading to amber. Short so it reads as speed, not as a laser.
                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_BoltTrail.mat", kit.SoftDot, additive: true);
                trail.time = 0.16f;
                trail.minVertexDistance = 0.12f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.07f), new Keyframe(0.3f, 0.045f), new Keyframe(1f, 0f));
                trail.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(new Color(1f, 0.97f, 0.88f), 0f), new GradientColorKey(new Color(1f, 0.72f, 0.35f), 0.4f), new GradientColorKey(new Color(0.9f, 0.4f, 0.15f), 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) },
                };
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;

                var bolt = root.AddComponent<Bolt>();
                SetRef(bolt, "trail", trail);
                SetRef(bolt, "model", model.transform);
                PrefabUtility.SaveAsPrefabAsset(root, BoltPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Flat quad visible from both sides (the palette material culls back faces).</summary>
        private static void DoubleQuad(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, MeshBuilder.UvMapper uv)
        {
            mb.Quad(a, b, c, d, uv, 0);
            mb.Quad(d, c, b, a, uv, 0);
        }

        /// <summary>
        /// Scene object with the shared combat particles (muzzle flash + sparks, impact sparks + dust), wired to the
        /// player's shooter. World-space systems that do not emit on their own; CombatFx bursts them.
        /// </summary>
        public static CombatFx CreateFx(GameArtKit kit, AutoShooter shooter)
        {
            var root = new GameObject("Combat FX");
            var additive = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_Spark.mat", kit.SoftDot, additive: true);
            var dustMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_Dust.mat", kit.SoftDot, additive: false);

            var flash = Burst(root.transform, "Muzzle Flash", additive, 0.06f, 0.09f, 0f, 0f, 0.5f, 0.75f,
                new Color(1f, 0.92f, 0.7f, 0.9f), new Color(1f, 0.75f, 0.4f, 0.7f), 0f, 4);
            var muzzleSparks = Burst(root.transform, "Muzzle Sparks", additive, 0.12f, 0.25f, 3f, 7f, 0.04f, 0.08f,
                new Color(1f, 0.85f, 0.5f), new Color(1f, 0.6f, 0.25f), 18f, 40);
            var impactSparks = Burst(root.transform, "Impact Sparks", additive, 0.15f, 0.35f, 2.5f, 6f, 0.04f, 0.09f,
                new Color(1f, 0.9f, 0.6f), new Color(1f, 0.55f, 0.2f), 55f, 80);
            var impactDust = Burst(root.transform, "Impact Dust", dustMat, 0.5f, 0.8f, 0.4f, 1.2f, 0.35f, 0.6f,
                new Color(0.62f, 0.6f, 0.55f, 0.45f), new Color(0.5f, 0.48f, 0.44f, 0.35f), 70f, 20);
            foreach (var ps in new[] { muzzleSparks, impactSparks })
            {
                var main = ps.main;
                main.gravityModifier = 1.2f;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1.5f;
            }
            var dustSize = impactDust.sizeOverLifetime;
            dustSize.enabled = true;
            dustSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));

            var fx = root.AddComponent<CombatFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("shooter").objectReferenceValue = shooter;
            so.FindProperty("muzzleFlash").objectReferenceValue = flash;
            so.FindProperty("muzzleSparks").objectReferenceValue = muzzleSparks;
            so.FindProperty("impactSparks").objectReferenceValue = impactSparks;
            so.FindProperty("impactDust").objectReferenceValue = impactDust;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fx;
        }

        private static ParticleSystem Burst(Transform parent, string name, Material mat, float lifeMin, float lifeMax,
            float speedMin, float speedMax, float sizeMin, float sizeMax, Color c0, Color c1, float coneAngle, int max)
        {
            var ps = PropKit.Particles(name, parent, mat);
            var main = ps.main;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.maxParticles = max;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.02f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0.8f, 0.4f), (0f, 1f) });
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
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
