using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using System.Linq;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Combat
{
    /// <summary>
    /// Builds the combat prefabs from code: the crossbow bolt (palette mesh + short glowing trail) and the
    /// zombie horde (KAN-30): one Meshy model in three types. Shambler (most of the horde: slow, one bolt), Runner (the old
    /// sprint clip, fast and frail, rare) and Brute (big, slow, tough, drops an elite wisp). Shambler and Brute use the
    /// baked shamble + attack clips (<see cref="ZombieAnimBuilder"/>); every type carries its clip's measured ground speed
    /// so Enemy plays the legs at the real speed.
    /// KAN-58: a fourth type, the boss body (Warden): the same model at twice the size, dark crimson, hard to push, no
    /// small health bar and no XP of its own (BossData holds its health and reward).
    /// Called by GameSceneBuilder; re-run is safe (assets are updated in place).
    /// </summary>
    public static class CombatBuilder
    {
        private const string ArtDir = "Assets/_Project/Art/Combat";
        private const string PrefabDir = "Assets/_Project/Prefabs/Combat";
        private const string BoltMeshPath = ArtDir + "/Bolt_Mesh.asset";
        public const string BoltPrefabPath = PrefabDir + "/Bolt.prefab";
        public const string EnemyPrefabPath = PrefabDir + "/Enemy_Zombie.prefab";
        public const string RunnerPrefabPath = PrefabDir + "/Enemy_ZombieRunner.prefab";
        public const string BrutePrefabPath = PrefabDir + "/Enemy_ZombieBrute.prefab";
        public const string BossPrefabPath = PrefabDir + "/Enemy_ZombieBoss.prefab";
        private const string ZombieDir = "Assets/_Project/Art/Characters/Zombie";
        private const string ZombieModelPath = ZombieDir + "/SK_Zombie.fbx";
        private const string ZombieRunPath = ZombieDir + "/A_Zombie_Run.fbx";
        private const string ZombieTexturePath = ZombieDir + "/T_Zombie_BaseColor.jpg"; // extracted from the Meshy FBX
        private const string ZombieShamblePath = ZombieDir + "/A_Zombie_Shamble.anim";
        private const string ZombieAttackPath = ZombieDir + "/A_Zombie_Attack.anim";
        private const string ZombieControllerPath = ZombieDir + "/AC_Zombie.controller";
        private const string RunnerControllerPath = ZombieDir + "/AC_ZombieRunner.controller";
        private const string ZombieMaterialPath = ZombieDir + "/M_Zombie.mat";
        private const string RunnerMaterialPath = ZombieDir + "/M_ZombieRunner.mat";
        private const string BruteMaterialPath = ZombieDir + "/M_ZombieBrute.mat";
        private const string BossMaterialPath = ZombieDir + "/M_ZombieBoss.mat";
        private const int ZombieTextureSize = 1024; // 2k source; ~100 px tall on screen

        /// <summary>
        /// One zombie type. Pre-KAN-30 baseline (single type): 150 HP, 3 m/s sprint, 20 dps, 10 XP, max 6-10 alive.
        /// Horde balance: three times the bodies, so the common one dies to one base bolt (100), walks at 58% of the old
        /// speed (Knight 5 m/s can kite) and bites softer; XP per kill drops so levels come at about the old pace.
        /// </summary>
        private struct ZombieType
        {
            public string Name, Prefab, Material, Controller;
            public Color Tint;
            public float Health, Speed, SpeedJitter, Scale, ScaleJitter, ContactRange, StopDistance, Dps;
            public int Xp;
            public bool Runs; // sprint clip instead of the shamble
            public bool Boss; // KAN-58: no small bar, low knockback
        }

        private static readonly ZombieType Shambler = new ZombieType
        {
            Name = "Enemy_Zombie", Prefab = EnemyPrefabPath, Material = ZombieMaterialPath, Controller = ZombieControllerPath,
            Tint = Color.white, Health = 100f, Speed = 1.75f, SpeedJitter = 0.1f, Scale = 1f, ScaleJitter = 0.05f,
            ContactRange = 1.4f, StopDistance = 1.1f, Dps = 12f, Xp = 5,
        };

        private static readonly ZombieType Runner = new ZombieType
        {
            Name = "Enemy_ZombieRunner", Prefab = RunnerPrefabPath, Material = RunnerMaterialPath, Controller = RunnerControllerPath,
            Tint = new Color(1.08f, 0.84f, 0.76f), Health = 70f, Speed = 2.6f, SpeedJitter = 0.08f, Scale = 0.93f, ScaleJitter = 0.04f,
            ContactRange = 1.35f, StopDistance = 1.05f, Dps = 10f, Xp = 6, Runs = true,
        };

        private static readonly ZombieType Brute = new ZombieType
        {
            Name = "Enemy_ZombieBrute", Prefab = BrutePrefabPath, Material = BruteMaterialPath, Controller = ZombieControllerPath,
            Tint = new Color(0.7f, 0.78f, 0.66f), Health = 340f, Speed = 1.3f, SpeedJitter = 0.06f, Scale = 1.25f, ScaleJitter = 0.04f,
            ContactRange = 1.65f, StopDistance = 1.3f, Dps = 26f, Xp = 25, // 25 = elite wisp tier
        };

        /// <summary>
        /// Boss body (KAN-58). Health here is only the type base: BossFight spawns it with BossData health. XP 0: the boss
        /// drops its reward as several gold wisps. Contact range and stop distance grow with the 2.1x body.
        /// </summary>
        private static readonly ZombieType Warden = new ZombieType
        {
            Name = "Enemy_ZombieBoss", Prefab = BossPrefabPath, Material = BossMaterialPath, Controller = ZombieControllerPath,
            Tint = new Color(0.66f, 0.42f, 0.4f), Health = 1000f, Speed = 1.55f, SpeedJitter = 0f, Scale = 2.1f, ScaleJitter = 0f,
            ContactRange = 2.5f, StopDistance = 2.1f, Dps = 30f, Xp = 0, Boss = true,
        };

        public static void Build(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(ArtDir);
            EditorAssetUtil.EnsureFolder(PrefabDir);
            BuildBolt(kit);
            BuildEnemy(kit);
        }

        public const string WavesPath = "Assets/_Project/Data/Combat/EnemyWaves_Realm1.asset";

        /// <summary>
        /// Spawn pacing for Realm 1. KAN-29 baseline: 6 / 8 / 10 alive at 0 / 45 / 120 s, groups of 1-2, bursts of 3-4.
        /// KAN-30 horde: 18 / 24 / 30 alive (x3) plus 36 from 240 s; groups of 1-4 every 0.8-1.1 s and bursts of 5-12 every
        /// 20-26 s so the crowd swells and drains; Runners and Brutes mixed in from 45 s.
        /// An existing asset is never overwritten (designer edits stay); only empty enemy slots get the shambler.
        /// </summary>
        public static EnemyWaveTable LoadOrCreateWaves(Enemy zombie)
        {
            var runner = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerPrefabPath)?.GetComponent<Enemy>();
            var brute = AssetDatabase.LoadAssetAtPath<GameObject>(BrutePrefabPath)?.GetComponent<Enemy>();
            var table = AssetDatabase.LoadAssetAtPath<EnemyWaveTable>(WavesPath);
            if (table != null)
            {
                bool dirty = false;
                for (int i = 0; i < table.PhaseCount; i++)
                {
                    var entries = table.GetPhase(i).enemies;
                    if (entries == null) continue;
                    for (int k = 0; k < entries.Length; k++)
                        if (entries[k].prefab == null) { entries[k].prefab = zombie; dirty = true; }
                }
                if (dirty) EditorUtility.SetDirty(table);
                return table;
            }

            EnemyWaveTable.EnemyEntry[] Mix(float shamblers, float runners, float brutes)
            {
                var list = new System.Collections.Generic.List<EnemyWaveTable.EnemyEntry> { new EnemyWaveTable.EnemyEntry { prefab = zombie, weight = shamblers } };
                if (runner != null && runners > 0f) list.Add(new EnemyWaveTable.EnemyEntry { prefab = runner, weight = runners });
                if (brute != null && brutes > 0f) list.Add(new EnemyWaveTable.EnemyEntry { prefab = brute, weight = brutes });
                return list.ToArray();
            }
            table = ScriptableObject.CreateInstance<EnemyWaveTable>();
            table.Configure(firstDelay: 1f, prewarm: 12, new[]
            {
                new EnemyWaveTable.Phase { startTime = 0f, maxAlive = 18, spawnInterval = 1.1f, groupSize = new Vector2Int(1, 2), burstInterval = 26f, burstSize = 5, enemies = Mix(1f, 0f, 0f) },
                new EnemyWaveTable.Phase { startTime = 45f, maxAlive = 24, spawnInterval = 1f, groupSize = new Vector2Int(2, 3), burstInterval = 24f, burstSize = 7, enemies = Mix(0.82f, 0.14f, 0.04f) },
                new EnemyWaveTable.Phase { startTime = 120f, maxAlive = 30, spawnInterval = 0.9f, groupSize = new Vector2Int(2, 3), burstInterval = 22f, burstSize = 9, enemies = Mix(0.72f, 0.18f, 0.1f) },
                new EnemyWaveTable.Phase { startTime = 240f, maxAlive = 36, spawnInterval = 0.8f, groupSize = new Vector2Int(2, 4), burstInterval = 20f, burstSize = 12, enemies = Mix(0.66f, 0.2f, 0.14f) },
            });
            EditorAssetUtil.EnsureFolder(System.IO.Path.GetDirectoryName(WavesPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(table, WavesPath);
            return table;
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

        /// <summary>
        /// Meshy zombie: rigged FBX imported by Unity (Generic rig, Mesh LODs generated on import). The FBX's embedded
        /// material does not pick up its packed texture in URP, so the extracted base colour is bound to our own materials
        /// (one per type, tint baked in: no per-renderer property block, SRP Batcher friendly).
        /// </summary>
        private static void BuildEnemy(GameArtKit kit)
        {
            ConfigureModelImport(ZombieModelPath, importAnimation: false, meshLods: true);
            ConfigureModelImport(ZombieRunPath, importAnimation: true);
            ConfigureTextureImport(ZombieTexturePath, ZombieTextureSize);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ZombieModelPath);
            var run = AssetDatabase.LoadAllAssetsAtPath(ZombieRunPath).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__"));
            var (shamble, attack) = ZombieAnimBuilder.Build(model, ZombieShamblePath, ZombieAttackPath);
            float shambleSpeed = ZombieAnimBuilder.Measure(model, shamble).Speed;
            var runGait = ZombieAnimBuilder.Measure(model, run);
            Debug.Log($"[CombatBuilder] Zombie clip ground speeds: shamble {shambleSpeed:F3} m/s (planned {ZombieAnimBuilder.ShambleGroundSpeed:F3}), run {runGait}");

            BuildController(ZombieControllerPath, shamble, attack);
            BuildController(RunnerControllerPath, run, attack);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ZombieTexturePath);
            foreach (var type in new[] { Shambler, Runner, Brute, Warden })
                BuildZombie(kit, model, type, EditorAssetUtil.SimpleLitMaterial(type.Material, texture, type.Tint, smoothness: 0.15f),
                    type.Runs ? runGait.Speed : shambleSpeed);
        }

        /// <summary>Walk (default) + Attack + Stand (walk frozen, KAN-162). Enemy cross-fades between them from code: no transitions, no parameters.</summary>
        private static void BuildController(string path, AnimationClip walk, AnimationClip attack)
        {
            StableAssetWriter.Remember(path); // new fileIDs every rebuild: keep the old file when nothing changed (KAN-157)
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            var walkState = machine.AddState("Walk");
            walkState.motion = walk;
            var attackState = machine.AddState("Attack");
            attackState.motion = attack;
            var standState = machine.AddState(Enemy.StandState); // still pose: the Warden recovers standing (KAN-162)
            standState.motion = walk;
            standState.speed = 0f;
            machine.defaultState = walkState;
            EditorUtility.SetDirty(controller);
        }

        private static void BuildZombie(GameArtKit kit, GameObject modelAsset, in ZombieType type, Material mat, float clipSpeed)
        {
            var root = new GameObject(type.Name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.transform.SetParent(root.transform, false);
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(type.Controller);
                animator.applyRootMotion = false; // the clips run in place; Enemy moves the root
                // Off screen the state machine keeps running (walk / attack cross-fades are never lost, KAN-43); bone
                // transforms and skinning are still skipped, which is the expensive part.
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
                body.sharedMaterials = Enumerable.Repeat(mat, body.sharedMaterials.Length).ToArray();
                body.quality = SkinQuality.Bone2;      // ~100 px tall on screen: two weights are plenty, half the skinning
                body.skinnedMotionVectors = false;
                body.updateWhenOffscreen = false;
                Quiet(body);
                ArcherModelBuilder.AddBlobShadow(root.transform, kit.BlobShadow, kit.Quad);

                var enemy = root.AddComponent<Enemy>();
                var so = new SerializedObject(enemy);
                so.FindProperty("body").objectReferenceValue = body;
                so.FindProperty("baseTint").colorValue = type.Tint; // same as the material: no property block when calm
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("maxHealth").floatValue = type.Health;
                so.FindProperty("moveSpeed").floatValue = type.Speed;
                so.FindProperty("stopDistance").floatValue = type.StopDistance;
                so.FindProperty("contactRange").floatValue = type.ContactRange;
                so.FindProperty("damagePerSecond").floatValue = type.Dps;
                so.FindProperty("xpValue").intValue = type.Xp;
                so.FindProperty("bodyScale").floatValue = type.Scale;
                so.FindProperty("speedJitter").floatValue = type.SpeedJitter;
                so.FindProperty("scaleJitter").floatValue = type.ScaleJitter;
                so.FindProperty("animRefSpeed").floatValue = clipSpeed;
                so.FindProperty("isBoss").boolValue = type.Boss;
                so.FindProperty("knockbackScale").floatValue = type.Boss ? 0.1f : 1f;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, type.Prefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureModelImport(string path, bool importAnimation, bool meshLods = false)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Model missing: {path}");
            bool dirty = importer.animationType != ModelImporterAnimationType.Generic
                || importer.importAnimation != importAnimation
                || importer.materialImportMode != ModelImporterMaterialImportMode.None
                || importer.generateMeshLods != meshLods;
            importer.generateMeshLods = meshLods; // 10.4k tris at full detail; the top-down camera picks a ~2-3k level
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = importAnimation;
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // material is ours, see BuildEnemy
            if (importAnimation && (importer.clipAnimations.Length == 0 || !importer.clipAnimations.All(c => c.loopTime)))
            {
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips) clip.loopTime = true;
                importer.clipAnimations = clips;
                dirty = true;
            }
            if (dirty) importer.SaveAndReimport();
        }

        private static void ConfigureTextureImport(string path, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Texture missing: {path}");
            if (importer.maxTextureSize == maxSize) return;
            importer.maxTextureSize = maxSize;
            importer.SaveAndReimport();
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
