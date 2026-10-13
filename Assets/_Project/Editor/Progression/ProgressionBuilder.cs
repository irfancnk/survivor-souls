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
    /// Builds the XP system of the Game scene (KAN-21): the XP curve asset, the soul wisp texture and materials,
    /// the "Progression" object (XpManager + LevelUpFlow), the "Souls" object (SoulWispField with its two particle
    /// systems: bodies driven by SetParticles, additive motes) and, on the player, the SoulCollector and the level-up burst
    /// (ring, flash, sparks on unscaled time). Called by GameSceneBuilder; re-run is safe (assets are updated in place).
    /// </summary>
    public static class ProgressionBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Progression";
        public const string CurvePath = DataDir + "/XpCurve_Run.asset";
        private const string FxDir = "Assets/_Project/Art/Progression";
        private const string WispTexturePath = FxDir + "/T_SoulWisp.png";
        private const string RingTexturePath = "Assets/_Project/Art/Textures/Game/T_Ring.png"; // made by GameArtKit
        public const int WispCapacity = 128;
        private const int MoteBudget = 320;

        // Run pacing: first levels come quickly (zombie = 10 XP), then the formula takes over.
        private const int CurveBase = 50;
        private const float CurveGrowth = 1.22f;
        private const int CurveRound = 5;
        private static readonly int[] CurveTable = { 30, 50, 70 };

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

            var wispTex = TextureFactory.Write(WispTexturePath, 64, 64, WispPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: 64);
            var wispMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_SoulWisp.mat", wispTex, additive: false);
            var moteMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_SoulMote.mat", kit.SoftDot, additive: true);
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
            var bodies = Bodies(soulsGo.transform, wispMat);
            var motes = Motes(soulsGo.transform, moteMat);
            result.Souls = soulsGo.AddComponent<SoulWispField>();
            var so = new SerializedObject(result.Souls);
            so.FindProperty("xp").objectReferenceValue = result.Xp;
            so.FindProperty("collector").objectReferenceValue = result.Collector;
            so.FindProperty("bodies").objectReferenceValue = bodies;
            so.FindProperty("motes").objectReferenceValue = motes;
            so.FindProperty("capacity").intValue = WispCapacity;
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

        /// <summary>Wisp bodies: script-owned billboards (SetParticles), no emission, never expire on their own.</summary>
        private static ParticleSystem Bodies(Transform parent, Material mat)
        {
            var ps = PropKit.Particles("Wisp Bodies", parent, mat);
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
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.Distance; // alpha blend: far wisps first
            Quiet(r);
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

        /// <summary>
        /// Soul wisp sprite, white + alpha (the tier colour tints it): solid glowing core, translucent shell, a slightly
        /// darker rim (reads as a glass sphere edge and keeps the wisp visible on pale stone), faint outer glow.
        /// </summary>
        private static Color WispPixel(int x, int y)
        {
            float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 64f * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float core = 1f - TextureFactory.SmoothStep(0f, 0.3f, r);
            float shell = 0.4f * (1f - TextureFactory.SmoothStep(0.6f, 0.76f, r));
            float rim = 0.6f * Mathf.Exp(-Mathf.Pow((r - 0.7f) / 0.065f, 2f));
            float glow = 0.2f * (1f - TextureFactory.SmoothStep(0.7f, 1f, r));
            float a = Mathf.Clamp01(core + shell + rim + glow);
            float rimShare = a > 0.001f ? rim / a : 0f;
            float grey = Mathf.Lerp(Mathf.Lerp(0.86f, 0.55f, rimShare), 1f, core);
            return new Color(grey, grey, grey, a);
        }

        private static void SetRef(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
