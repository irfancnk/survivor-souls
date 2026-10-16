using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Enemies;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-138 Hexbound Shaman feel, Game scene side:
    /// <list type="bullet">
    /// <item>"Hex Motes" under "Hex FX": an Emit-only particle system for the embers that rise off hexed zombies
    /// (<see cref="HexFx.EmitMotes"/>). Small hot-pink core with a dark plum rim (<see cref="MotePixel"/>), alpha blend so it
    /// reads on snow and on dark bodies. One draw call, only while a zombie is hexed.</item>
    /// <item><see cref="ShamanSounds"/> on the audio object with the procedural clips of SfxBuilder (hum, whoosh, chime,
    /// crack, pop).</item>
    /// </list>
    /// </summary>
    public sealed class ShamanFeelStep : IGameSceneStep
    {
        public const string MoteTexture = "T_HexMote";
        public const int MaxMotes = 240;

        public int Order => 138;

        public void Build(GameSceneContext ctx)
        {
            var fx = ctx.EnemyShots != null ? ctx.EnemyShots.GetComponentInChildren<HexFx>(true) : null;
            if (fx == null) throw new System.InvalidOperationException("[ShamanFeelStep] Hex FX missing under the enemy shots (ShamanBuilder.CreateScene)");
            var motes = CreateMotes(fx.transform);
            GameSceneContext.SetRef(fx, "motes", motes);

            var sounds = ctx.Audio.gameObject.AddComponent<ShamanSounds>();
            var so = new SerializedObject(sounds);
            SetClips(so.FindProperty("hum.clips"), ctx.Sfx.ShamanHums);
            SetClips(so.FindProperty("whoosh.clips"), ctx.Sfx.ShamanWhooshes);
            SetClips(so.FindProperty("chime.clips"), ctx.Sfx.ShamanChimes);
            SetClips(so.FindProperty("crack.clips"), ctx.Sfx.ShamanCracks);
            SetClips(so.FindProperty("pop.clips"), ctx.Sfx.ShamanPops);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>World-space embers: born where <see cref="HexFx"/> puts them, velocity from the emit, shrink and fade.</summary>
        public static ParticleSystem CreateMotes(Transform parent)
        {
            string dir = RangedEnemyBuilder.FxDir;
            var tex = TextureFactory.Write($"{dir}/{MoteTexture}.png", RangedEnemyBuilder.FxSize, RangedEnemyBuilder.FxSize, MotePixel,
                TextureFactory.Kind.Color, wrap: TextureWrapMode.Clamp, maxSize: RangedEnemyBuilder.FxSize);
            var mat = EditorAssetUtil.ParticleMaterial($"{dir}/M_FX_HexMote.mat", tex, additive: false);
            var ps = PropKit.Particles("Hex Motes", parent, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.26f);
            main.gravityModifier = -0.05f;
            main.maxParticles = MaxMotes;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.35f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.12f), (0.9f, 0.6f), (0f, 1f) });
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 1.2f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = 1; // over the warnings, under the Shaman markers
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        /// <summary>Ember: near-white hot core, pink-magenta glow, dark plum rim (reads on snow). The particle colour tints it.</summary>
        public static Color MotePixel(int x, int y)
        {
            int n = RangedEnemyBuilder.FxSize;
            float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            var core = new Color(1f, 0.95f, 1f);
            var glow = new Color(1f, 0.55f, 0.95f);
            var rim = new Color(0.2f, 0.03f, 0.2f);
            Color c = r < 0.25f ? core : r < 0.58f ? Color.Lerp(core, glow, (r - 0.25f) / 0.33f) : Color.Lerp(glow, rim, Mathf.Clamp01((r - 0.58f) / 0.2f));
            c.a = 1f - TextureFactory.SmoothStep(0.72f, 0.9f, r);
            return c;
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }
}
