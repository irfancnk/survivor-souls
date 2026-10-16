using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-132: strike effects and sounds of the special melee zombies in the Game scene (called by GameSceneBuilder).
    /// <list type="bullet">
    /// <item>"Special Melee FX" (<see cref="SpecialMeleeFx"/>) under the enemy shot pools: four shared Emit-only particle
    /// systems (spores, pop puffs, dust, ground cracks), all alpha blend (additive dust vanishes on snow), +1 draw call each
    /// only while they have particles.</item>
    /// <item><see cref="SpecialMeleeSounds"/> on the audio object with the procedural clips of <see cref="SfxBuilder"/>.</item>
    /// <item>Art: <c>M_FX_MeleeDust</c> (soft dot), <c>M_FX_SlamCrack</c> + <c>T_SlamCrack</c> (radial ground cracks round a
    /// crater, alpha mask, tinted per particle) in <see cref="RangedEnemyBuilder.FxDir"/>.</item>
    /// </list>
    /// </summary>
    public static class SpecialMeleeFxBuilder
    {
        public const string CrackTexturePath = RangedEnemyBuilder.FxDir + "/T_SlamCrack.png";
        private const int CrackSize = 256;
        private const int CrackRays = 11;
        private const int Seed = 132;

        public static SpecialMeleeFx Create(GameArtKit kit, Transform parent, TopDownFollowCamera camera, GameObject audioHost, SfxBuilder.Set sfx)
        {
            var dustMat = EditorAssetUtil.ParticleMaterial(RangedEnemyBuilder.FxDir + "/M_FX_MeleeDust.mat", kit.SoftDot, additive: false);
            var crackTex = TextureFactory.Write(CrackTexturePath, CrackSize, CrackSize, CrackPixel, TextureFactory.Kind.Color,
                mipmaps: true, wrap: TextureWrapMode.Clamp, maxSize: CrackSize);
            var crackMat = EditorAssetUtil.ParticleMaterial(RangedEnemyBuilder.FxDir + "/M_FX_SlamCrack.mat", crackTex, additive: false);

            var root = new GameObject("Special Melee FX");
            root.transform.SetParent(parent, false);

            var spores = Particles(root, "Bloater Spores", dustMat, 160);
            var sporeMain = spores.main;
            sporeMain.gravityModifier = 1.6f;
            Fade(spores, 0.05f, 0.75f, 1f, 0.55f);
            Drag(spores, 1.2f);

            var puffs = Particles(root, "Bloater Pop Puffs", dustMat, 60);
            Fade(puffs, 0.04f, 0.4f, 0.7f, 1.6f);
            Drag(puffs, 4f);

            var dust = Particles(root, "Melee Dust", dustMat, 420);
            var dustMain = dust.main;
            dustMain.gravityModifier = -0.04f;
            Fade(dust, 0.08f, 0.45f, 0.6f, 1.7f);
            Drag(dust, 3f);

            var cracks = Particles(root, "Slam Cracks", crackMat, 12);
            cracks.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var crackCol = cracks.colorOverLifetime;
            crackCol.enabled = true;
            crackCol.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.03f), (1f, 0.55f), (0f, 1f) });
            var crackSize = cracks.sizeOverLifetime;
            crackSize.enabled = true;
            crackSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.75f, 0f, 6f), new Keyframe(0.12f, 1f), new Keyframe(1f, 1f)));

            var fx = root.AddComponent<SpecialMeleeFx>();
            fx.Setup(spores, puffs, dust, cracks, camera);
            EditorUtility.SetDirty(fx);

            var sounds = audioHost.AddComponent<SpecialMeleeSounds>();
            var so = new SerializedObject(sounds);
            SetClips(so.FindProperty("bloaterSwell.clips"), sfx.MeleeBloaterSwells);
            SetClips(so.FindProperty("bloaterBurst.clips"), sfx.MeleeBloaterBursts);
            SetClips(so.FindProperty("ghoulScreech.clips"), sfx.MeleeGhoulScreeches);
            SetClips(so.FindProperty("ghoulWhoosh.clips"), sfx.MeleeGhoulWhooshes);
            SetClips(so.FindProperty("ghoulThud.clips"), sfx.MeleeGhoulThuds);
            SetClips(so.FindProperty("bruteGrunt.clips"), sfx.MeleeBruteGrunts);
            SetClips(so.FindProperty("bruteSlam.clips"), sfx.MeleeBruteSlams);
            so.FindProperty("cloudHiss").objectReferenceValue = sfx.MeleeCloudHiss;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fx;
        }

        /// <summary>
        /// Ground crack mask: <see cref="CrackRays"/> jagged cracks out of a dark crater, thinner and fainter towards their
        /// ends, inside a soft disc edge. White rgb (the particle colour tints it), alpha = crack.
        /// </summary>
        public static Color CrackPixel(int x, int y)
        {
            float px = (x + 0.5f) / CrackSize * 2f - 1f, py = (y + 0.5f) / CrackSize * 2f - 1f;
            float r = Mathf.Sqrt(px * px + py * py);
            if (r >= 1f) return new Color(1f, 1f, 1f, 0f);
            float ang = Mathf.Atan2(py, px);
            float crack = 0f;
            for (int i = 0; i < CrackRays; i++)
            {
                float a0 = (i + TextureFactory.Hash(i, 1, Seed) * 0.6f) / CrackRays * Mathf.PI * 2f;
                float len = 0.55f + 0.4f * TextureFactory.Hash(i, 2, Seed);
                if (r > len) continue;
                float wiggle = (TextureFactory.ValueNoise(r * 7f, i * 3.7f, 64, Seed + i) - 0.5f) * 0.5f;
                float d = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, (a0 + wiggle) * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                float w = Mathf.Lerp(0.06f, 0.014f, r / len);
                float c = 1f - TextureFactory.SmoothStep(w * 0.5f, w, d);
                crack = Mathf.Max(crack, c * Mathf.Lerp(1f, 0.55f, r / len));
                // One side branch per crack, from half its length.
                float b0 = a0 + wiggle + (TextureFactory.Hash(i, 3, Seed) < 0.5f ? 0.35f : -0.35f);
                if (r > len * 0.45f && r < len * 0.85f)
                {
                    float db = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, (b0 + (r - len * 0.45f) * 0.8f) * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                    crack = Mathf.Max(crack, (1f - TextureFactory.SmoothStep(w * 0.35f, w * 0.7f, db)) * 0.6f);
                }
            }
            float crater = (1f - TextureFactory.SmoothStep(0.08f, 0.2f, r)) * (0.65f + 0.25f * TextureFactory.ValueNoise(px * 6f + 9f, py * 6f + 9f, 64, Seed));
            float ring = (1f - TextureFactory.SmoothStep(0.012f, 0.03f, Mathf.Abs(r - 0.17f))) * 0.7f; // crater lip
            float edge = 1f - TextureFactory.SmoothStep(0.82f, 1f, r);
            float a = Mathf.Clamp01(Mathf.Max(crack, Mathf.Max(crater, ring))) * edge;
            return new Color(1f, 1f, 1f, a);
        }

        private static ParticleSystem Particles(GameObject parent, string name, Material mat, int max)
        {
            var ps = PropKit.Particles(name, parent.transform, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.maxParticles = max;
            main.startSpeed = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        /// <summary>Alpha in fast, hold, fade out; size from 1 to <paramref name="grow"/> over the life.</summary>
        private static void Fade(ParticleSystem ps, float inEnd, float holdEnd, float holdAlpha, float grow)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0f, 0f), (1f, inEnd), (holdAlpha, holdEnd), (0f, 1f) });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            float max = Mathf.Max(1f, grow);
            size.size = new ParticleSystem.MinMaxCurve(max, new AnimationCurve(new Keyframe(0f, 1f / max), new Keyframe(1f, grow / max)));
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }
}
