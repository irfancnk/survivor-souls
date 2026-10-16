using SurvivorSouls.Editor.Enemies;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Boss;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-164.2 boss enrage, Game scene side: <see cref="BossEnrageFx"/> on the "Boss" object with an "Enrage Aura" child
    /// (red-orange embers rising from a ring at the boss's feet, additive, world space, one draw call, stopped until the
    /// enrage), the follow camera for the shake and the boss groan clips (<c>Deaths</c>) for the low roar.
    /// </summary>
    public sealed class BossEnrageStep : IGameSceneStep
    {
        public const string EmberTexture = "T_EnrageEmber";
        public const int MaxEmbers = 160;

        public int Order => 237;

        public void Build(GameSceneContext ctx)
        {
            if (ctx.Boss == null) throw new System.InvalidOperationException("[BossEnrageStep] no BossFight in the scene");
            var aura = CreateAura(ctx.Boss.transform);
            var fx = ctx.Boss.gameObject.AddComponent<BossEnrageFx>();
            GameSceneContext.SetRef(fx, "fight", ctx.Boss);
            GameSceneContext.SetRef(fx, "aura", aura);
            GameSceneContext.SetRef(fx, "followCamera", ctx.FollowCamera);
            var so = new SerializedObject(fx);
            var clips = so.FindProperty("roars");
            var deaths = ctx.Sfx.Deaths ?? new AudioClip[0];
            clips.arraySize = deaths.Length;
            for (int i = 0; i < deaths.Length; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = deaths[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Embers born on a 1.1 m ring round the feet, rising 1.5-2.6 m/s with a little noise; shrink and fade.</summary>
        public static ParticleSystem CreateAura(Transform parent)
        {
            string dir = RangedEnemyBuilder.FxDir;
            var tex = TextureFactory.Write($"{dir}/{EmberTexture}.png", RangedEnemyBuilder.FxSize, RangedEnemyBuilder.FxSize, EmberPixel,
                TextureFactory.Kind.Color, wrap: TextureWrapMode.Clamp, maxSize: RangedEnemyBuilder.FxSize);
            var mat = EditorAssetUtil.ParticleMaterial($"{dir}/M_FX_EnrageEmber.mat", tex, additive: true);
            var ps = PropKit.Particles("Enrage Aura", parent, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.32f, 0.12f), new Color(1f, 0.62f, 0.2f));
            main.gravityModifier = -0.1f;
            main.maxParticles = MaxEmbers;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 70f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle; // the system points up (PropKit): the ring lies on the ground
            shape.radius = 1.1f;
            shape.radiusThickness = 0.35f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.2f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.1f), (0.8f, 0.6f), (0f, 1f) });
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.9f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = 1;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        /// <summary>Ember: white-hot core fading to a soft orange glow (the particle colour tints it red).</summary>
        public static Color EmberPixel(int x, int y)
        {
            int n = RangedEnemyBuilder.FxSize;
            float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            var core = new Color(1f, 0.97f, 0.9f);
            var glow = new Color(1f, 0.6f, 0.35f);
            Color c = r < 0.2f ? core : Color.Lerp(core, glow, Mathf.Clamp01((r - 0.2f) / 0.35f));
            c.a = 1f - TextureFactory.SmoothStep(0.3f, 0.95f, r);
            return c;
        }
    }
}
