using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Boss;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-164.3 (KAN-239) boss movers, Game scene side:
    /// <list type="bullet">
    /// <item>Per-boss bolt looks on the boss bolt pool (<see cref="BossProjectiles.SetLook"/>, picked by
    /// <see cref="BossData.BoltLook"/>): Ember (the old bolt, the Warden), Cleaver (Butcher: spinning steel blade), Fireball
    /// (Pyre Lord: bigger flame head, long trail), Boulder (Colossus: rolling rock, no trail), Skull (Matriarch: bone skull,
    /// teal eyes, teal trail). One material per look (alpha blend, dark rim so it reads on snow); one look per fight, so
    /// the bolts stay one draw call.</item>
    /// <item><see cref="BossSpikeFx"/> with a "Spike Shards" particle system (stone spike clusters, vertical billboards,
    /// world space, one draw call) for the Spikes move.</item>
    /// </list>
    /// Debug menu: Survivor Souls/Debug/Boss: Boomerang, Sine, Bounce, Wall, Spikes (a boss without the move gets
    /// <see cref="BossAttack.Sample"/> first). Dev screenshots: <c>-ss-boss-attack boomerang|sine|bounce|wall|spikes</c>.
    /// </summary>
    public sealed class BossMoversStep : IGameSceneStep
    {
        private const string ArtDir = "Assets/_Project/Art/Boss";
        private const int Size = 64;
        private const int MaxShards = 160;

        public int Order => 239;

        public void Build(GameSceneContext ctx)
        {
            if (ctx.Boss == null) throw new System.InvalidOperationException("[BossMoversStep] no BossFight in the scene");
            var bolts = ctx.Boss.Projectiles;
            if (bolts == null) throw new System.InvalidOperationException("[BossMoversStep] the boss has no bolt pool");
            WriteLooks(bolts);

            var shards = CreateShards(ctx.Boss.transform);
            var fx = ctx.Boss.gameObject.AddComponent<BossSpikeFx>();
            GameSceneContext.SetRef(fx, "fight", ctx.Boss);
            GameSceneContext.SetRef(fx, "shards", shards);
            GameSceneContext.SetRef(fx, "followCamera", ctx.FollowCamera);
        }

        /// <summary>Looks by <see cref="BossProjectiles.BoltLook"/> index; Ember keeps the pool's current material.</summary>
        private static void WriteLooks(BossProjectiles bolts)
        {
            var bodies = new SerializedObject(bolts).FindProperty("bodies").objectReferenceValue as ParticleSystem;
            var ember = bodies != null ? bodies.GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
            var looks = new[]
            {
                Look(ember, Color.white, 0.75f, 0.38f, 0.55f, 0f, false),
                Look(BoltMaterial("Cleaver", CleaverPixel), Color.white, 0.95f, 0.3f, 0.35f, -900f, false),
                Look(BoltMaterial("Fireball", FireballPixel), Color.white, 0.95f, 0.46f, 0.7f, 140f, false),
                Look(BoltMaterial("Boulder", BoulderPixel), Color.white, 1.05f, 0f, 0f, 0f, true),
                Look(BoltMaterial("Skull", SkullPixel), Color.white, 0.85f, 0.36f, 0.4f, 0f, false),
            };
            var so = new SerializedObject(bolts);
            var list = so.FindProperty("looks");
            list.arraySize = looks.Length;
            for (int i = 0; i < looks.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("material").objectReferenceValue = looks[i].material;
                e.FindPropertyRelative("color").colorValue = looks[i].color;
                e.FindPropertyRelative("size").floatValue = looks[i].size;
                e.FindPropertyRelative("ghostSpacing").floatValue = looks[i].ghostSpacing;
                e.FindPropertyRelative("ghostAlpha").floatValue = looks[i].ghostAlpha;
                e.FindPropertyRelative("spin").floatValue = looks[i].spin;
                e.FindPropertyRelative("roll").boolValue = looks[i].roll;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static BossProjectiles.LookStyle Look(Material mat, Color color, float size, float ghostSpacing, float ghostAlpha,
            float spin, bool roll) =>
            new BossProjectiles.LookStyle
            {
                material = mat, color = color, size = size, ghostSpacing = ghostSpacing, ghostAlpha = ghostAlpha, spin = spin,
                roll = roll,
            };

        private static Material BoltMaterial(string name, System.Func<int, int, Color> pixel)
        {
            var tex = TextureFactory.Write($"{ArtDir}/T_BossBolt{name}.png", Size, Size, pixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: Size);
            return EditorAssetUtil.ParticleMaterial($"{ArtDir}/M_FX_BossBolt{name}.mat", tex, additive: false);
        }

        /// <summary>Spike clusters: one per emit (BossSpikeFx sets place, size, life), pop up fast, sink and fade.</summary>
        private static ParticleSystem CreateShards(Transform parent)
        {
            var tex = TextureFactory.Write($"{ArtDir}/T_BossSpikes.png", Size, Size, SpikePixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: Size);
            var mat = EditorAssetUtil.ParticleMaterial($"{ArtDir}/M_FX_BossSpikes.mat", tex, additive: false);
            var ps = PropKit.Particles("Spike Shards", parent, mat);
            var main = ps.main;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.75f;
            main.startSpeed = 0f;
            main.maxParticles = MaxShards;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.08f, 1.05f),
                new Keyframe(0.16f, 1f), new Keyframe(0.7f, 0.95f), new Keyframe(1f, 0.55f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (1f, 0.65f), (0f, 1f) });
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard; // upright spikes from the top-down camera
            r.sortMode = ParticleSystemSortMode.Distance;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        // ------------------------------------------------------------------ Sprites (u, v in -1..1, v up)

        private static void UV(int x, int y, out float u, out float v)
        {
            u = (x + 0.5f) / Size * 2f - 1f;
            v = (y + 0.5f) / Size * 2f - 1f;
        }

        /// <summary>Dark outline around a shape mask (inside = 1): the last <paramref name="rim"/> of the edge distance.</summary>
        private static Color Rimmed(Color fill, float edge, float rim, Color rimColor)
        {
            if (edge <= 0f) return new Color(0f, 0f, 0f, 0f);
            var c = edge < rim ? rimColor : fill;
            c.a = Mathf.Clamp01(edge / 0.02f);
            return c;
        }

        /// <summary>Butcher cleaver: steel blade with a bright edge and a dark hole, wooden grip, dark outline.</summary>
        public static Color CleaverPixel(int x, int y)
        {
            UV(x, y, out float u, out float v);
            var rim = new Color(0.08f, 0.05f, 0.05f);
            // blade: u -0.5..0.62, v -0.42..0.42, front-bottom corner rounded
            float blade = Mathf.Min(Mathf.Min(u + 0.5f, 0.62f - u), Mathf.Min(v + 0.42f, 0.42f - v));
            float corner = 0.3f - Mathf.Sqrt(Mathf.Pow(Mathf.Max(0f, u - 0.32f), 2f) + Mathf.Pow(Mathf.Max(0f, -0.12f - v), 2f));
            blade = Mathf.Min(blade, corner);
            float hole = Mathf.Sqrt((u + 0.3f) * (u + 0.3f) + (v - 0.22f) * (v - 0.22f)) - 0.09f;
            // grip: u -0.95..-0.5, v -0.1..0.1
            float grip = Mathf.Min(Mathf.Min(u + 0.95f, -0.46f - u), Mathf.Min(v + 0.11f, 0.11f - v));
            if (blade > 0f && hole > 0f)
            {
                float shade = Mathf.Lerp(0.55f, 0.82f, (v + 0.42f) / 0.84f) + 0.05f * Mathf.Sin(u * 9f);
                var steel = new Color(shade, shade, shade * 1.04f);
                if (v < -0.3f) steel = Color.Lerp(steel, new Color(0.97f, 0.97f, 1f), 0.8f); // honed edge
                if (u > 0.25f && v < -0.05f) steel = Color.Lerp(steel, new Color(0.55f, 0.06f, 0.05f), 0.45f); // old blood
                return Rimmed(steel, Mathf.Min(blade, hole), 0.06f, rim);
            }
            if (grip > 0f)
            {
                float grain = 0.5f + 0.5f * Mathf.Sin(u * 40f);
                return Rimmed(Color.Lerp(new Color(0.28f, 0.15f, 0.07f), new Color(0.4f, 0.23f, 0.1f), grain), grip, 0.05f, rim);
            }
            return new Color(0f, 0f, 0f, 0f);
        }

        /// <summary>Pyre Lord fireball: white-gold core, orange flame with tongues, red shell, thin dark rim, soft glow.</summary>
        public static Color FireballPixel(int x, int y)
        {
            UV(x, y, out float u, out float v);
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Atan2(v, u);
            float edge = 0.66f + 0.09f * Mathf.Sin(a * 5f) + 0.05f * Mathf.Sin(a * 9f + 1.3f);
            if (r > edge)
            {
                float glow = 0.4f * (1f - TextureFactory.SmoothStep(edge, edge + 0.25f, r));
                return new Color(1f, 0.45f, 0.1f, glow);
            }
            float k = r / edge;
            Color c = k < 0.3f ? new Color(1f, 0.97f, 0.78f)
                : k < 0.6f ? Color.Lerp(new Color(1f, 0.97f, 0.78f), new Color(1f, 0.62f, 0.12f), (k - 0.3f) / 0.3f)
                : k < 0.88f ? Color.Lerp(new Color(1f, 0.62f, 0.12f), new Color(0.8f, 0.14f, 0.04f), (k - 0.6f) / 0.28f)
                : new Color(0.25f, 0.04f, 0.02f);
            c.a = 1f;
            return c;
        }

        /// <summary>Colossus boulder: lumpy grey-brown rock, lit from the top left, dark cracks, dark outline.</summary>
        public static Color BoulderPixel(int x, int y)
        {
            UV(x, y, out float u, out float v);
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Atan2(v, u);
            float edge = 0.8f + 0.06f * Mathf.Sin(a * 3f + 1f) + 0.04f * Mathf.Sin(a * 7f + 2.1f);
            if (r > edge) return new Color(0f, 0f, 0f, 0f);
            float light = Mathf.Clamp01(0.6f + 0.45f * (-u * 0.6f + v * 0.8f) / Mathf.Max(0.3f, edge));
            float grain = TextureFactory.ValueNoise((u + 1f) * 6f, (v + 1f) * 6f, 12, 239);
            var rock = Color.Lerp(new Color(0.3f, 0.26f, 0.22f), new Color(0.62f, 0.57f, 0.5f), light * 0.8f + grain * 0.2f);
            float crack = Mathf.Abs(Mathf.Sin(u * 5.5f + v * 2.3f + Mathf.Sin(v * 6f) * 0.6f));
            if (crack < 0.07f && r < edge - 0.12f) rock *= 0.45f;
            rock.a = 1f;
            return Rimmed(rock, edge - r, 0.07f, new Color(0.1f, 0.08f, 0.07f));
        }

        /// <summary>Matriarch skull: bone-white cranium and jaw, teal glowing eyes, dark nose and teeth, teal halo.</summary>
        public static Color SkullPixel(int x, int y)
        {
            UV(x, y, out float u, out float v);
            var rim = new Color(0.06f, 0.1f, 0.1f);
            float cranium = 0.62f - Mathf.Sqrt(u * u + (v - 0.12f) * (v - 0.12f));
            float jaw = Mathf.Min(Mathf.Min(u + 0.34f, 0.34f - u), Mathf.Min(v + 0.66f, -0.2f - v));
            float shape = Mathf.Max(cranium, jaw);
            if (shape <= 0f)
            {
                float d = -shape;
                return new Color(0.2f, 0.95f, 0.85f, 0.38f * (1f - TextureFactory.SmoothStep(0f, 0.22f, d)));
            }
            var bone = Color.Lerp(new Color(0.72f, 0.69f, 0.6f), new Color(0.95f, 0.93f, 0.85f), Mathf.Clamp01(0.5f + v * 0.6f));
            float eye = Mathf.Min(Mathf.Sqrt((u - 0.24f) * (u - 0.24f) + v * v), Mathf.Sqrt((u + 0.24f) * (u + 0.24f) + v * v));
            if (eye < 0.16f) bone = Color.Lerp(new Color(0.75f, 1f, 0.95f), new Color(0.1f, 0.7f, 0.65f), eye / 0.16f);
            else if (eye < 0.19f) bone = rim;
            bool nose = v < -0.12f && v > -0.28f && Mathf.Abs(u) < (v + 0.28f) * 0.5f;
            bool teeth = v < -0.36f && v > -0.5f && Mathf.Repeat(u + 0.34f, 0.17f) < 0.04f;
            if (nose || teeth) bone = rim;
            bone.a = 1f;
            return Rimmed(bone, shape, 0.06f, rim);
        }

        /// <summary>Stone spike cluster: a tall middle spike and two short ones, lit edge, dark outline, dust at the base.</summary>
        public static Color SpikePixel(int x, int y)
        {
            UV(x, y, out float u, out float v);
            var rim = new Color(0.08f, 0.07f, 0.07f);
            float best = -1f, lit = 0f;
            Spike(u, v, 0f, 0.95f, 0.36f, ref best, ref lit);
            Spike(u, v, -0.45f, 0.25f, 0.26f, ref best, ref lit);
            Spike(u, v, 0.48f, 0.4f, 0.24f, ref best, ref lit);
            if (best <= 0f)
            {
                float dust = v < -0.78f ? 0.4f * Mathf.Clamp01(1f - Mathf.Abs(u) * 1.2f) * (1f - TextureFactory.SmoothStep(-0.9f, -0.78f, v)) : 0f;
                return new Color(0.82f, 0.82f, 0.85f, dust); // snow dust kicked up at the base
            }
            var stone = Color.Lerp(new Color(0.24f, 0.22f, 0.21f), new Color(0.58f, 0.55f, 0.52f), lit);
            stone.a = 1f;
            return Rimmed(stone, best, 0.05f, rim);
        }

        /// <summary>One spike: base at v -0.9, centre <paramref name="cx"/>, tip at <paramref name="tip"/>, half width at the base.</summary>
        private static void Spike(float u, float v, float cx, float tip, float half, ref float best, ref float lit)
        {
            float t = Mathf.InverseLerp(-0.9f, tip, v);
            if (v < -0.9f || v > tip) return;
            float w = half * (1f - t);
            float inside = Mathf.Min(w - Mathf.Abs(u - cx), (v + 0.9f) * 0.5f);
            if (inside <= best) return;
            best = inside;
            lit = u < cx ? 0.85f : 0.3f; // left face catches the light
        }

        // ------------------------------------------------------------------ Debug menu (play mode)

        [MenuItem("Survivor Souls/Debug/Boss: Boomerang")]
        private static void Boomerang() => ForceOrSample(BossFight.Move.Boomerang);

        [MenuItem("Survivor Souls/Debug/Boss: Sine")]
        private static void Sine() => ForceOrSample(BossFight.Move.Sine);

        [MenuItem("Survivor Souls/Debug/Boss: Bounce")]
        private static void Bounce() => ForceOrSample(BossFight.Move.Bounce);

        [MenuItem("Survivor Souls/Debug/Boss: Wall")]
        private static void Wall() => ForceOrSample(BossFight.Move.Wall);

        [MenuItem("Survivor Souls/Debug/Boss: Spikes")]
        private static void Spikes() => ForceOrSample(BossFight.Move.Spikes);

        [MenuItem("Survivor Souls/Debug/Boss: Boomerang", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Sine", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Bounce", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Wall", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Spikes", true)]
        private static bool Fighting() => Application.isPlaying && BossFight.Active != null;

        /// <summary>Starts the boss's <paramref name="move"/> attack; a boss without one gets the sample entry first.</summary>
        private static void ForceOrSample(BossFight.Move move)
        {
            var fight = BossFight.Active;
            if (fight.ForceAttack(move)) return;
            foreach (var a in fight.Attacks)
                if (a.type == move) return; // it has one: busy right now, try again
            fight.ForceAttack(fight.AddAttack(BossAttack.Sample(move)));
        }
    }
}
