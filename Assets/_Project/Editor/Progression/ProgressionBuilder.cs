using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.FX;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Progression
{
    /// <summary>
    /// Builds the XP system of the Game scene (KAN-21): the XP curve asset, the soul crystal materials (KAN-177: Blender
    /// mesh <c>SM_XpCrystal.fbx</c> from <c>Tools/Blender/build_xp_crystal.py</c>, opaque gold gem shader), the
    /// "Progression" object (XpManager + LevelUpFlow), the "Souls" object (SoulWispField with its three particle systems:
    /// opaque mesh crystal bodies and additive halos driven by SetParticles, additive motes; plus the SoulTrail ribbon
    /// mesh) and, on the player, the SoulCollector and the level-up burst (ring, flash, sparks on unscaled time). Called
    /// by GameSceneBuilder; re-run is safe (assets are updated in place).
    /// </summary>
    public static class ProgressionBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Progression";
        public const string CurvePath = DataDir + "/XpCurve_Run.asset";
        private const string FxDir = "Assets/_Project/Art/Progression";
        public const string CrystalModelPath = FxDir + "/SM_XpCrystal.fbx"; // Tools/Blender/build_xp_crystal.py
        private const string CrystalShader = "SurvivorSouls/XpCrystal";
        private const int TrailCapacity = 40;
        private const float TrailLife = 0.2f;
        private const float TrailHeadAlpha = 0.72f;
        private const string RingTexturePath = "Assets/_Project/Art/Textures/Game/T_Ring.png"; // made by GameArtKit
        public const int WispCapacity = 128;
        private const int WispMergeAbove = 24;    // KAN-30 horde: past this many wisps out, close drops pile into one
        private const float WispMergeRadius = 1.6f;
        private const int MoteBudget = 320;

        // Run pacing (KAN-113 pacing model, ChamberPacing; KAN-232 45 s Chambers, 20 per Realm): first levels come quickly
        // (3 picks in Chamber 1), then a slow +3 % per level keeps 2-3 picks per Chamber; level ~40 at the Realm end.
        private const int CurveBase = 70;
        private const float CurveGrowth = 1.03f;
        private const int CurveRound = 5;
        private static readonly int[] CurveTable = { 20, 35, 50, 70 };

        public sealed class Result
        {
            public XpManager Xp;
            public LevelUpFlow Flow;
            public SoulWispField Souls;
            public SoulCollector Collector;
        }

        public static Result Create(GameArtKit kit, GameObject player)
        {
            EditorAssetUtil.EnsureFolder(DataDir);
            EditorAssetUtil.EnsureFolder(FxDir);
            var curve = BuildCurve();

            var crystalMesh = LoadCrystalMesh();
            var crystalMat = EditorAssetUtil.LoadOrCreateMaterial(FxDir + "/M_XpCrystal.mat", CrystalShader);
            EditorUtility.SetDirty(crystalMat);
            var moteMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_SoulMote.mat", kit.SoftDot, additive: true);
            var trailMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_SoulTrail.mat", kit.SoftDot, additive: false);
            var ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath);
            if (ringTex == null) throw new System.InvalidOperationException($"Missing {RingTexturePath} (GameArtKit builds it).");
            var ringMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_LevelUpRing.mat", ringTex, additive: true);

            var result = new Result();
            var progression = new GameObject("Progression");
            result.Xp = progression.AddComponent<XpManager>();
            SetRef(result.Xp, "curve", curve);
            result.Flow = progression.AddComponent<LevelUpFlow>();
            SetRef(result.Flow, "xp", result.Xp);

            result.Collector = player.AddComponent<SoulCollector>();

            var soulsGo = new GameObject("Souls");
            var bodies = Bodies(soulsGo.transform, crystalMat, crystalMesh);
            var halos = Halos(soulsGo.transform, moteMat);
            var motes = Motes(soulsGo.transform, moteMat);
            var trail = Trail(soulsGo.transform, trailMat);
            result.Souls = soulsGo.AddComponent<SoulWispField>();
            var so = new SerializedObject(result.Souls);
            so.FindProperty("xp").objectReferenceValue = result.Xp;
            so.FindProperty("collector").objectReferenceValue = result.Collector;
            so.FindProperty("bodies").objectReferenceValue = bodies;
            so.FindProperty("halos").objectReferenceValue = halos;
            so.FindProperty("motes").objectReferenceValue = motes;
            so.FindProperty("trail").objectReferenceValue = trail;
            so.FindProperty("capacity").intValue = WispCapacity;
            so.FindProperty("mergeAbove").intValue = WispMergeAbove;
            so.FindProperty("mergeRadius").floatValue = WispMergeRadius;
            so.ApplyModifiedPropertiesWithoutUndo();

            CreateLevelUpFx(player, result.Flow, ringMat, moteMat);
            return result;
        }

        private static XpCurve BuildCurve()
        {
            var curve = AssetDatabase.LoadAssetAtPath<XpCurve>(CurvePath);
            if (curve == null)
            {
                curve = ScriptableObject.CreateInstance<XpCurve>();
                curve.Configure(CurveBase, CurveGrowth, CurveRound, CurveTable);
                AssetDatabase.CreateAsset(curve, CurvePath);
            }
            // Existing asset: keep designer edits, do not overwrite.
            return curve;
        }

        /// <summary>
        /// The Blender soul crystal mesh. Import settings: readable (CPU mesh particles), file normals (flat facets, soft
        /// bevels), no materials, no animation, no rig. Run <c>Tools/Blender/build_xp_crystal.py</c> to rebuild the FBX.
        /// </summary>
        public static Mesh LoadCrystalMesh()
        {
            var importer = AssetImporter.GetAtPath(CrystalModelPath) as ModelImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"Missing {CrystalModelPath} (run Tools/Blender/build_xp_crystal.py).");
            bool dirty = !importer.isReadable || importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                         importer.importAnimation || importer.animationType != ModelImporterAnimationType.None ||
                         importer.importNormals != ModelImporterNormals.Import || importer.importTangents != ModelImporterTangents.None ||
                         importer.meshCompression != ModelImporterMeshCompression.Off || importer.importCameras || importer.importLights;
            if (dirty)
            {
                importer.isReadable = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.None;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importCameras = false;
                importer.importLights = false;
                importer.SaveAndReimport();
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(CrystalModelPath);
            if (mesh == null) throw new System.InvalidOperationException($"No mesh in {CrystalModelPath}.");
            return mesh;
        }

        /// <summary>
        /// Crystal bodies: script-owned opaque mesh particles (SetParticles), no emission, never expire on their own. 3D
        /// rotation (spin + tilt) and 3D size (squash and stretch) per particle. CPU-built mesh particles (no GPU
        /// instancing): the XpCrystal shader reads world-space vertices, normals, the vertex colour and the mesh UV0 data.
        /// </summary>
        private static ParticleSystem Bodies(Transform parent, Material mat, Mesh crystal)
        {
            var ps = ScriptOwned("Wisp Bodies", parent, mat);
            var main = ps.main;
            main.startRotation3D = true;
            main.startSize3D = true;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = crystal;
            r.alignment = ParticleSystemRenderSpace.World;
            r.enableGPUInstancing = false;
            r.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
            });
            r.sortMode = ParticleSystemSortMode.None; // opaque: depth sorts
            return ps;
        }

        /// <summary>Halos behind the orbs: script-owned additive billboards (SetParticles); the opaque orb hides the centre.</summary>
        private static ParticleSystem Halos(Transform parent, Material mat)
        {
            var ps = ScriptOwned("Wisp Halos", parent, mat);
            ps.GetComponent<ParticleSystemRenderer>().sortMode = ParticleSystemSortMode.None; // additive: order free
            return ps;
        }

        /// <summary>See-through ribbons behind popping and flying crystals: one world-space mesh, one draw call.</summary>
        private static SoulTrail Trail(Transform parent, Material mat)
        {
            var go = new GameObject("Wisp Trails", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            var trail = go.AddComponent<SoulTrail>();
            var so = new SerializedObject(trail);
            so.FindProperty("capacity").intValue = TrailCapacity;
            so.FindProperty("life").floatValue = TrailLife;
            so.FindProperty("headAlpha").floatValue = TrailHeadAlpha;
            so.ApplyModifiedPropertiesWithoutUndo();
            return trail;
        }

        private static ParticleSystem ScriptOwned(string name, Transform parent, Material mat)
        {
            var ps = PropKit.Particles(name, parent, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.maxParticles = WispCapacity;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            Quiet(ps.GetComponent<ParticleSystemRenderer>());
            return ps;
        }

        /// <summary>Trail motes + absorb flashes: additive soft dots fed by Emit, fade and shrink, a little air drag.</summary>
        private static ParticleSystem Motes(Transform parent, Material mat)
        {
            var ps = PropKit.Particles("Wisp Motes", parent, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.maxParticles = MoteBudget;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;
            main.startSize = 0.06f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = 2f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.12f), (0.6f, 0.5f), (0f, 1f) });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));
            Quiet(ps.GetComponent<ParticleSystemRenderer>());
            return ps;
        }

        /// <summary>Level-up burst under the player: ground ring, chest flash, rising sparks. Unscaled time (game is paused).</summary>
        private static void CreateLevelUpFx(GameObject player, LevelUpFlow flow, Material ringMat, Material glowMat)
        {
            var root = new GameObject("Level Up FX");
            root.transform.SetParent(player.transform, false);

            var ring = Burst(root.transform, "Ring", ringMat, 0.7f, 5.5f, new Color(0.78f, 0.92f, 1f, 0.95f), 4);
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeCurve(ring, new AnimationCurve(new Keyframe(0f, 0.12f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f)));

            var flash = Burst(root.transform, "Flash", glowMat, 0.35f, 2.8f, new Color(0.86f, 0.95f, 1f, 0.9f), 2);
            SizeCurve(flash, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.2f)));

            var sparks = Burst(root.transform, "Sparks", glowMat, 0.8f, 0.1f, new Color(0.8f, 0.94f, 1f, 1f), 40);
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.94f, 1f), new Color(1f, 0.88f, 0.55f));
            main.gravityModifier = 0.35f;
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.05f;
            sr.lengthScale = 1.6f;
            SizeCurve(sparks, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

            var fx = player.AddComponent<LevelUpFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("flow").objectReferenceValue = flow;
            so.FindProperty("ring").objectReferenceValue = ring;
            so.FindProperty("flash").objectReferenceValue = flash;
            so.FindProperty("sparks").objectReferenceValue = sparks;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ParticleSystem Burst(Transform parent, string name, Material mat, float life, float size, Color color, int max)
        {
            var ps = PropKit.Particles(name, parent, mat);
            var main = ps.main;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true; // plays while the level-up pause holds time scale at 0
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = max;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0.85f, 0.35f), (0f, 1f) });
            Quiet(ps.GetComponent<ParticleSystemRenderer>());
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private static void SizeCurve(ParticleSystem ps, AnimationCurve curve)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void Quiet(ParticleSystemRenderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static void SetRef(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
