using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-107: the Hexbound Shaman. Prefab side (<see cref="ConfigurePrefab"/>, called by <see cref="EnemyRosterBuilder"/>
    /// before it saves a prefab): hold band 8-11 m on <see cref="Enemy"/> and <see cref="HexShaman"/> with the numbers of
    /// Docs/ENEMY_ROSTER.md 3.11; it raises Graveyard Laborers. Scene side (<see cref="CreateScene"/>, called by
    /// GameSceneBuilder): "Hex FX" under "Enemy Shots" (<see cref="HexFx"/>: violet puff + priority markers). The soul orb
    /// look is the fourth shot kind of <see cref="RangedEnemyBuilder"/> (<see cref="SoulOrbPixel"/>). Textures are procedural.
    /// Not in the waves yet (spawn tables: KAN-112); the spawner has it as an extra type.
    /// Debug menu (Play mode): "Survivor Souls/Debug/Caster: Spawn Hexbound Shaman".
    /// </summary>
    public static class ShamanBuilder
    {
        public const string Id = "HexboundShaman";
        public const string RaiseId = "GraveyardLaborer";
        public const float HoldMin = 8f, HoldMax = 11f;

        /// <summary>
        /// Doc 3.11 numbers (first guesses, Chamber 1 terms): ring of 8 orbs at 5 m/s every 5 s (0.7 s warning, 10 % on a hit,
        /// gap ~2.4 m at 4 m out); cast B every 12 s: hex zombies in 6 m (+30 % speed, 5 s) when 2+ are there, else raise 2
        /// Laborers (max 4 alive); blink 6 m away when the Knight is within 3 m (cooldown 6 s).
        /// </summary>
        public static readonly HexShaman.Settings Spec = new HexShaman.Settings
        {
            orbWindUp = 0.7f, orbCount = 8, orbSpeed = 5f, orbRange = 13f, orbDamageShare = 0.1f, orbHitRadius = 0.4f,
            orbWarnRadius = 1.6f, orbCooldown = 5f,
            castWindUp = 0.8f, hexRadius = 6f, hexBoost = 1.3f, hexDuration = 5f, hexMinTargets = 2,
            raiseCount = 2, raiseMax = 4, raiseRing = 2.2f, castCooldown = 12f,
            blinkTrigger = 3f, blinkDistance = 6f, blinkCooldown = 6f, blinkTime = 0.25f,
            recover = 0.4f,
        };

        /// <summary>Hold band + <see cref="HexShaman"/> on the Shaman prefab root before it is saved. No-op for other types.</summary>
        public static void ConfigurePrefab(GameObject root, Enemy enemy, string id)
        {
            if (id != Id) return;
            var so = new SerializedObject(enemy);
            so.FindProperty("holdMin").floatValue = HoldMin;
            so.FindProperty("holdMax").floatValue = HoldMax;
            so.ApplyModifiedPropertiesWithoutUndo();
            var raise = EnemyRosterBuilder.LoadPrefab(RaiseId);
            if (raise == null) Debug.LogWarning("[ShamanBuilder] Graveyard Laborer prefab missing: the Shaman cannot raise the dead");
            root.AddComponent<HexShaman>().Configure(Spec, raise);
            TimeCastStates(root);
        }

        /// <summary>Slowest / fastest Cast and Summon state speed: a clip far off its wind-up keeps a readable motion.</summary>
        public const float MinCastSpeed = 0.4f, MaxCastSpeed = 3f;

        /// <summary>
        /// KAN-138: Cast (orb ring, <see cref="HexShaman.Settings.orbWindUp"/>) and Summon (hex / raise,
        /// <see cref="HexShaman.Settings.castWindUp"/>) state speeds, so the hand peak of the clip (hands moving fastest,
        /// <see cref="EnemyRosterBuilder.StrikeTime"/>) lands on the release: speed = peak time / wind-up. HexShaman starts the
        /// state at the wind-up start (cross-fade from time 0).
        /// </summary>
        private static void TimeCastStates(GameObject root)
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            if (controller == null)
            {
                Debug.LogWarning("[ShamanBuilder] no animator controller: Cast / Summon not timed to the release");
                return;
            }
            foreach (var child in controller.layers[0].stateMachine.states)
            {
                var state = child.state;
                float windUp = WindUpOf(state.name);
                if (windUp <= 0f || !(state.motion is AnimationClip clip)) continue;
                float peak = EnemyRosterBuilder.StrikeTime(AssetDatabase.GetAssetPath(clip), handsOnly: true);
                state.speed = CastSpeed(peak, windUp);
                Debug.Log($"[ShamanBuilder] {state.name}: hand peak {peak:F2} s of {clip.length:F2} s, wind-up {windUp:F2} s -> speed {state.speed:F2}");
            }
            EditorUtility.SetDirty(controller);
        }

        /// <summary>Wind-up (s) that plays the state <paramref name="state"/>; 0 = not a cast state.</summary>
        public static float WindUpOf(string state) => state == "Cast" ? Spec.orbWindUp : state == "Summon" ? Spec.castWindUp : 0f;

        /// <summary>State speed that puts a clip time of <paramref name="peak"/> s at <paramref name="windUp"/> s of play.</summary>
        public static float CastSpeed(float peak, float windUp) => Mathf.Clamp(peak / Mathf.Max(0.05f, windUp), MinCastSpeed, MaxCastSpeed);

        // ------------------------------------------------------------ Scene

        /// <summary>"Hex FX": the puff burst system and the marker billboards.</summary>
        public static HexFx CreateScene(Transform parent)
        {
            var go = new GameObject("Hex FX");
            go.transform.SetParent(parent, false);
            var markers = RangedEnemyBuilder.ScriptSprites(go.transform, "Shaman Markers", "T_HexMarker", MarkerPixel, HexFx.MaxMarkers);
            markers.GetComponent<ParticleSystemRenderer>().sortingOrder = 2; // over the warnings
            var puff = CreatePuff(go.transform);
            var fx = go.AddComponent<HexFx>();
            fx.Setup(puff, markers);
            EditorUtility.SetDirty(fx);
            return fx;
        }

        /// <summary>Violet smoke + sparks rising round a point (alpha blend so it reads on snow). Emitted by script only.</summary>
        private static ParticleSystem CreatePuff(Transform parent)
        {
            string dir = RangedEnemyBuilder.FxDir;
            var tex = TextureFactory.Write($"{dir}/T_HexPuff.png", RangedEnemyBuilder.FxSize, RangedEnemyBuilder.FxSize, PuffPixel,
                TextureFactory.Kind.Color, wrap: TextureWrapMode.Clamp, maxSize: RangedEnemyBuilder.FxSize);
            var mat = EditorAssetUtil.ParticleMaterial($"{dir}/M_FX_HexPuff.mat", tex, additive: false);
            var ps = PropKit.Particles("Hex Puff", parent, mat);
            var main = ps.main;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.45f, 1f, 0.95f), new Color(0.35f, 0.12f, 0.55f, 0.85f));
            main.gravityModifier = -0.25f; // drifts up
            main.maxParticles = 96;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = 0.45f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0.8f, 0.5f), (0f, 1f) });
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        // ------------------------------------------------------------ Textures

        private static float R(int x, int y, out float u, out float v)
        {
            int n = RangedEnemyBuilder.FxSize;
            u = (x + 0.5f) / n * 2f - 1f;
            v = (y + 0.5f) / n * 2f - 1f;
            return Mathf.Sqrt(u * u + v * v);
        }

        /// <summary>Soul orb: pale violet-white core, violet glow, dark plum rim (reads on snow), faint swirl.</summary>
        public static Color SoulOrbPixel(int x, int y)
        {
            float r = R(x, y, out float u, out float v);
            float swirl = 0.5f + 0.5f * Mathf.Sin(Mathf.Atan2(v, u) * 3f + r * 9f);
            var core = new Color(0.97f, 0.9f, 1f);
            var glow = Color.Lerp(new Color(0.72f, 0.35f, 1f), new Color(0.55f, 0.2f, 0.9f), swirl);
            var rim = new Color(0.16f, 0.04f, 0.24f);
            Color c = r < 0.22f ? core : r < 0.6f ? Color.Lerp(core, glow, (r - 0.22f) / 0.38f) : Color.Lerp(glow, rim, Mathf.Clamp01((r - 0.6f) / 0.18f));
            c.a = 1f - TextureFactory.SmoothStep(0.76f, 0.92f, r);
            return c;
        }

        /// <summary>Priority marker: a violet diamond rune (bright inner diamond, dark outline), pointing down at the Shaman.</summary>
        private static Color MarkerPixel(int x, int y)
        {
            R(x, y, out float u, out float v);
            float d = Mathf.Abs(u) * 1.35f + Mathf.Abs(v); // diamond, a bit taller than wide
            var c = d < 0.42f ? new Color(0.98f, 0.85f, 1f) : d < 0.72f ? new Color(0.68f, 0.3f, 1f) : new Color(0.12f, 0.02f, 0.2f);
            c.a = 1f - TextureFactory.SmoothStep(0.86f, 0.96f, d);
            return c;
        }

        /// <summary>Puff: soft violet blob with a darker noisy edge.</summary>
        private static Color PuffPixel(int x, int y)
        {
            float r = R(x, y, out float u, out float v);
            float n = TextureFactory.Fbm((u + 1f) * 0.5f, (v + 1f) * 0.5f, 4, 3, 307);
            float k = r + (n - 0.5f) * 0.3f;
            var c = Color.Lerp(Color.white, new Color(0.7f, 0.6f, 0.8f), TextureFactory.SmoothStep(0.2f, 0.7f, k));
            c.a = (1f - TextureFactory.SmoothStep(0.45f, 0.9f, k)) * 0.9f;
            return c;
        }

        // ------------------------------------------------------------ Debug

        [MenuItem("Survivor Souls/Debug/Caster: Spawn Hexbound Shaman")]
        private static void SpawnShaman()
        {
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            var prefab = spawner != null ? spawner.FindType("Enemy_" + Id) : null;
            var player = EnemyProjectiles.Current != null ? EnemyProjectiles.Current.Player : null;
            if (prefab == null || player == null)
            {
                Debug.LogWarning("[ShamanBuilder] cannot spawn the Shaman: no spawner type or Knight");
                return;
            }
            spawner.SpawnAt(prefab, player.transform.position + new Vector3(0f, 0f, 9f));
        }

        [MenuItem("Survivor Souls/Debug/Caster: Shaman Orb Ring")]
        private static void OrbRing() => ForceAll(HexShaman.Cast.Orbs);

        [MenuItem("Survivor Souls/Debug/Caster: Shaman Hex / Raise")]
        private static void HexOrRaise() => ForceAll(HexShaman.Cast.Hex);

        [MenuItem("Survivor Souls/Debug/Caster: Spawn Hexbound Shaman", true)]
        [MenuItem("Survivor Souls/Debug/Caster: Shaman Orb Ring", true)]
        [MenuItem("Survivor Souls/Debug/Caster: Shaman Hex / Raise", true)]
        private static bool InPlay() => Application.isPlaying && Object.FindAnyObjectByType<EnemySpawner>() != null;

        private static void ForceAll(HexShaman.Cast cast)
        {
            foreach (var s in HexShaman.Active) s.ForceCast(cast);
        }
    }
}
