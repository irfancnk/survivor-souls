using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-104: turns the Meshy enemies of KAN-102 (Docs/ENEMY_ROSTER.md; 12 since KAN-128 cut three) into game prefabs. Per character:
    /// import settings (Generic rig, no materials, Mesh LODs on the skinned mesh, looping locomotion clips, long action clips
    /// cut to their strike), one URP Simple Lit material (base colour + normal map + emission where the texture glows), one
    /// AnimatorController (Move, Attack, Hit, Death + one state per clip) and one <c>Prefabs/Enemies/Enemy_&lt;Id&gt;.prefab</c>
    /// with the <see cref="Enemy"/> component (height and hit radius measured on the real mesh, walk clip ground speed
    /// measured on its rig). <see cref="AddMeleeTiers"/> puts the melee tiers into the wave table.
    /// The clips bind to paths that start at <c>Hips</c> (the A_ files have no rig node), so the Animator sits on the
    /// <c>&lt;Id&gt;_Rig</c> node and the skinned mesh moves under it (the Animator culls by the renderers below it).
    /// Called by GameSceneBuilder after CombatBuilder; re-run is safe. Trim ranges already set in an importer are kept.
    /// </summary>
    public static class EnemyRosterBuilder
    {
        public const string PrefabDir = "Assets/_Project/Prefabs/Enemies";
        private const string EnemiesArt = "Assets/_Project/Art/Characters/Enemies";
        private const string BossesArt = "Assets/_Project/Art/Characters/Bosses";
        private const int TextureSize = 1024;        // 2k source; zombies ~100 px, bosses ~300 px tall on a phone
        /// <summary>Action clips longer than this (s) are cut to <see cref="StrikeBefore"/> + <see cref="StrikeAfter"/> round the fastest limb.</summary>
        public const float TrimAbove = 3.2f;
        public const float StrikeBefore = 0.8f;
        public const float StrikeAfter = 0.55f;
        /// <summary>The Hit state plays its whole clip in this many seconds (the flinch shows its first part, Enemy.hitHold).</summary>
        public const float HitPlayTime = 0.7f;
        /// <summary>The Death state plays its whole clip in this many seconds (zombies / bosses), then the body sinks.</summary>
        public const float DeathPlayTime = 0.9f, BossDeathPlayTime = 2f;
        public const float KnightHeight = 1.75f;

        public static readonly string[] LoopClips = { "Shamble", "Walk", "Walking", "Running", "Sprint" };

        /// <summary>One character. Numbers are the Chamber 1 first guesses of Docs/ENEMY_ROSTER.md section 3.</summary>
        public sealed class Spec
        {
            public string Id, Move, Attack;
            public bool Boss;
            /// <summary>KAN-109: bake an emission map from the lava cracks of the base colour (<see cref="LavaEmissionBaker"/>).</summary>
            public bool LavaGlow;
            public float Health, Speed, Dps, Knockback = 1f, SpeedJitter = 0.08f, ScaleJitter = 0.04f;
            public int Xp;
            public string ArtDir => $"{(Boss ? BossesArt : EnemiesArt)}/{Id}";
            public string PrefabPath => $"{PrefabDir}/Enemy_{Id}.prefab";
            public string MaterialPath => $"{ArtDir}/M_{Id}.mat";
            public string ControllerPath => $"{ArtDir}/AC_{Id}.controller";
            public string ClipPath(string clip) => $"{ArtDir}/A_{Id}_{clip}.fbx";
        }

        private static Spec Zombie(string id, string move, string attack, float hp, float speed, float dps, int xp, float knock = 1f) =>
            new Spec { Id = id, Move = move, Attack = attack, Health = hp, Speed = speed, Dps = dps, Xp = xp, Knockback = knock };

        /// <summary>
        /// Bosses: health is the type base only (BossData holds the fight's health, KAN-58); no XP of their own; hard to push.
        /// Their contact damage is a placeholder until their attack patterns land (KAN-108..110); the Bone Matriarch has none
        /// (KAN-111, BossBuilder.ConfigurePrefab).
        /// </summary>
        private static Spec Boss(string id, string move, string attack, float hp, float speed) =>
            new Spec { Id = id, Move = move, Attack = attack, Health = hp, Speed = speed, Dps = 30f, Xp = 0, Knockback = 0.1f, Boss = true, SpeedJitter = 0f, ScaleJitter = 0f };

        private static Spec Lava(Spec spec)
        {
            spec.LavaGlow = true;
            return spec;
        }

        /// <summary>
        /// Melee tiers first: they join the waves (KAN-128 cut the Fallen Militiaman, the Gravebound Knight and the Bone
        /// Archer; their art, prefabs and wave slots are gone). Specials melee on contact (8 dps) on top
        /// of their own attacks (ranged KAN-105, Bloater / Ghoul / Brute KAN-106, Shaman KAN-107); their Attack state is their
        /// signature clip.
        /// </summary>
        public static readonly Spec[] Roster =
        {
            Zombie("GraveyardLaborer", "Shamble", "Attack", 100f, 1.75f, 12f, 5),
            Zombie("RustedWatchman", "Shamble", "Attack", 200f, 1.6f, 16f, 8),
            Zombie("PlagueBloater", "Walk", "Burst", 180f, 1.2f, 8f, 8),
            Zombie("PyreThrower", "Walk", "Throw", 110f, 1.6f, 8f, 8),
            Zombie("BileSpitter", "Walk", "Spit", 90f, 1.7f, 8f, 7),
            Zombie("FrenziedGhoul", "Sprint", "Leap", 60f, 4.2f, 8f, 6),
            Zombie("GraveBrute", "Walk", "Slam", 520f, 1.25f, 26f, 25, knock: 0f),
            Zombie("HexboundShaman", "Walking", "Cast", 130f, 1.5f, 8f, 12),
            Boss("AshgraveButcher", "Walk", "Chop", 8000f, 1.55f),
            Lava(Boss("PyreLord", "Walk", "Slam", 9000f, 1.4f)),
            Boss("RottingColossus", "Walk", "Slam", 11000f, 1.2f),
            Boss("BoneMatriarch", "Walking", "CastOrbs", 7000f, 1.3f),
        };

        /// <summary>The melee tiers (wave table order): Graveyard Laborer, Rusted Watchman. The elite melee role is the Grave Brute's.</summary>
        public static readonly string[] MeleeTiers = { "GraveyardLaborer", "RustedWatchman" };

        public static Spec Find(string id) => Roster.First(s => s.Id == id);

        public static Enemy LoadPrefab(string id) => AssetDatabase.LoadAssetAtPath<Enemy>(Find(id).PrefabPath);

        [MenuItem("Survivor Souls/Enemies/Rebuild Enemy Roster")]
        public static void BuildFromMenu()
        {
            Build(GameArtKit.Build());
            AssetDatabase.SaveAssets();
        }

        public static void Build(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(PrefabDir);
            var report = new System.Text.StringBuilder("[EnemyRosterBuilder] roster:\n");
            foreach (var spec in Roster) report.AppendLine(BuildOne(kit, spec));
            Debug.Log(report.ToString());
        }

        private static string BuildOne(GameArtKit kit, Spec spec)
        {
            string skPath = $"{spec.ArtDir}/SK_{spec.Id}.fbx";
            ConfigureModel(skPath, importAnimation: false, meshLods: true);
            var clips = new Dictionary<string, AnimationClip>();
            var trims = new List<string>();
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { spec.ArtDir }).Select(AssetDatabase.GUIDToAssetPath)
                         .Where(p => System.IO.Path.GetFileName(p).StartsWith($"A_{spec.Id}_")).OrderBy(p => p))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path).Substring($"A_{spec.Id}_".Length);
                ConfigureModel(path, importAnimation: true);
                ConfigureClip(path, name, trims);
                clips[name] = LoadClip(path);
            }
            foreach (var need in new[] { spec.Move, spec.Attack, "Hit", "Death" })
                if (need != "Hit" || !spec.Boss)
                    if (!clips.ContainsKey(need)) throw new System.InvalidOperationException($"{spec.Id}: clip '{need}' missing in {spec.ArtDir}");

            var (move, gait) = PickMove(spec, clips);
            var material = BuildMaterial(spec);
            var controller = BuildController(spec, clips, move);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(skPath);
            var (height, radius) = BuildPrefab(kit, spec, model, material, controller, gait.Speed);
            return $"  {spec.Id}: height {height:F2} m ({height / KnightHeight:F2}x Knight), hit radius {radius:F2} m, " +
                   $"Move = {move} {gait} (rate {spec.Speed / Mathf.Max(0.01f, gait.Speed):F2} at {spec.Speed} m/s), clips {string.Join(" ", clips.Select(c => $"{c.Key}={c.Value.length:F2}s"))}" +
                   (trims.Count > 0 ? $", trimmed {string.Join(" ", trims)}" : "");
        }

        // ------------------------------------------------------------ Import

        private static void ConfigureModel(string path, bool importAnimation, bool meshLods = false)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Model missing: {path}");
            if (importer.animationType == ModelImporterAnimationType.Generic && importer.importAnimation == importAnimation
                && importer.materialImportMode == ModelImporterMaterialImportMode.None && importer.generateMeshLods == meshLods) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = importAnimation;
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // material is ours (BuildMaterial)
            importer.generateMeshLods = meshLods; // zombies ~4k tris, bosses ~12.4k: the top-down camera picks a lower level
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Locomotion clips loop. Action clips loop too (the Attack state repeats while the Knight is in reach); Hit and
        /// Death play once. An action clip longer than <see cref="TrimAbove"/> is cut once to its strike window; a range
        /// already set (by this builder or by hand) is kept.
        /// </summary>
        private static void ConfigureClip(string path, string name, List<string> trims)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.defaultClipAnimations;
            if (defaults.Length != 1) throw new System.InvalidOperationException($"{path}: expected one take, found {defaults.Length}");
            bool loop = name != "Hit" && name != "Death";
            float first = defaults[0].firstFrame, last = defaults[0].lastFrame; // take range (frames)
            var current = importer.clipAnimations;
            var clip = current.Length == 1 ? current[0] : defaults[0];
            bool dirty = current.Length != 1 || clip.loopTime != loop || clip.name != name;
            clip.name = name;
            clip.loopTime = loop;

            bool action = loop && !LoopClips.Contains(name);
            float fps = LoadClip(path).frameRate;
            float length = (last - first) / fps;
            bool untouched = Mathf.Approximately(clip.firstFrame, first) && Mathf.Approximately(clip.lastFrame, last);
            if (action && length > TrimAbove && untouched)
            {
                float peak = StrikeTime(path);
                float start = Mathf.Clamp(peak - StrikeBefore, 0f, Mathf.Max(0f, length - StrikeBefore - StrikeAfter));
                float end = Mathf.Min(length, start + StrikeBefore + StrikeAfter);
                clip.firstFrame = first + Mathf.Round(start * fps);
                clip.lastFrame = first + Mathf.Round(end * fps);
                dirty = true;
            }
            if (!Mathf.Approximately(clip.firstFrame, first) || !Mathf.Approximately(clip.lastFrame, last))
                trims.Add($"{name} {(clip.firstFrame - first) / fps:F2}-{(clip.lastFrame - first) / fps:F2}s of {length:F2}s");
            if (!dirty) return;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Time (s) at which the hands or feet move fastest in the untrimmed clip: the strike (chop, throw, leap, shot release).
        /// Sampled at 30 Hz on the clip's own skeleton, speeds smoothed over 3 samples.
        /// </summary>
        public static float StrikeTime(string clipPath)
        {
            var clip = LoadClip(clipPath);
            var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(clipPath));
            try
            {
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var limbs = new[] { "LeftHand", "RightHand", "LeftFoot", "RightFoot" }
                    .Select(n => bones.FirstOrDefault(b => b.name == n)).Where(b => b != null).ToArray();
                if (limbs.Length == 0) return clip.length * 0.5f;
                int n = Mathf.Max(2, Mathf.CeilToInt(clip.length * 30f));
                var prev = new Vector3[limbs.Length];
                var speed = new float[n + 1];
                for (int s = 0; s <= n; s++)
                {
                    clip.SampleAnimation(instance, clip.length * s / n);
                    for (int l = 0; l < limbs.Length; l++)
                    {
                        Vector3 p = instance.transform.InverseTransformPoint(limbs[l].position);
                        if (s > 0) speed[s] = Mathf.Max(speed[s], (p - prev[l]).magnitude);
                        prev[l] = p;
                    }
                }
                int best = 1;
                float bestValue = -1f;
                for (int s = 1; s <= n; s++)
                {
                    float v = speed[s] + 0.5f * (speed[Mathf.Max(1, s - 1)] + speed[Mathf.Min(n, s + 1)]);
                    if (v > bestValue) { bestValue = v; best = s; }
                }
                return clip.length * best / n;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

        private static void ConfigureTexture(string path, bool normalMap)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) return;
            var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.maxTextureSize == TextureSize && importer.textureType == type) return;
            importer.textureType = type;
            importer.maxTextureSize = TextureSize;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------ Material + controller

        /// <summary>Simple Lit like M_Zombie: base colour, normal map, emission map (white) when the character has one.</summary>
        private static Material BuildMaterial(Spec spec)
        {
            string tex = $"{spec.ArtDir}/T_{spec.Id}";
            if (spec.LavaGlow) LavaEmissionBaker.Ensure(tex + "_BaseColor.jpg", tex + "_Emission.jpg"); // KAN-109
            ConfigureTexture(tex + "_BaseColor.jpg", false);
            ConfigureTexture(tex + "_Normal.png", true);
            ConfigureTexture(tex + "_Emission.jpg", false);
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_BaseColor.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_Normal.png");
            var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_Emission.jpg");
            if (baseMap == null) throw new System.InvalidOperationException($"{spec.Id}: base colour texture missing");
            var mat = EditorAssetUtil.SimpleLitMaterial(spec.MaterialPath, baseMap, Color.white, normalMap: normal, smoothness: 0.15f);
            mat.SetTexture("_EmissionMap", emission);
            mat.SetColor("_EmissionColor", emission != null ? Color.white : Color.black);
            if (emission != null) mat.EnableKeyword("_EMISSION");
            else mat.DisableKeyword("_EMISSION");
            // URP keeps _EMISSION only with an emissive GI flag (BaseShaderGUI: AnyEmissive); realtime = no bake cost (KAN-109).
            mat.globalIlluminationFlags = emission != null ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Move (default, the walk clip), Attack (signature clip), Hit and Death (sped up to <see cref="HitPlayTime"/> /
        /// <see cref="DeathPlayTime"/>), plus one state per clip under its own name (later attack patterns play them).
        /// Enemy cross-fades from code: no transitions, no parameters.
        /// </summary>
        private static AnimatorController BuildController(Spec spec, Dictionary<string, AnimationClip> clips, string move)
        {
            StableAssetWriter.Remember(spec.ControllerPath); // new fileIDs every rebuild: keep the old file when nothing changed (KAN-157)
            AssetDatabase.DeleteAsset(spec.ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(spec.ControllerPath);
            var machine = controller.layers[0].stateMachine;
            AnimatorState Add(string state, AnimationClip clip, float playTime = 0f)
            {
                var s = machine.AddState(state);
                s.motion = clip;
                if (playTime > 0f) s.speed = clip.length / playTime;
                return s;
            }
            machine.defaultState = Add("Move", clips[move]);
            Add("Attack", clips[spec.Attack]);
            if (clips.TryGetValue("Hit", out var hit)) Add("Hit", hit, HitPlayTime);
            Add("Death", clips["Death"], spec.Boss ? BossDeathPlayTime : DeathPlayTime);
            foreach (var pair in clips.Where(c => c.Key != "Hit" && c.Key != "Death" && c.Key != "Attack").OrderBy(c => c.Key))
                Add(pair.Key, pair.Value);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>Walk playback rates the Move clip may need (real speed / clip ground speed). Enemy clamps at 0.2-2.2.</summary>
        public const float MinMoveRate = 0.4f, MaxMoveRate = 2.4f; // 0.4: the Bone Matriarch glides (KAN-111: HoverBob)

        /// <summary>
        /// The design walk clip (<see cref="Spec.Move"/>) when the type's speed plays it at <see cref="MinMoveRate"/>..
        /// <see cref="MaxMoveRate"/>; otherwise the locomotion clip whose rate is closest to 1 (a 0.37 m/s lurch cannot carry
        /// a 1.8 m/s zombie without its feet sliding).
        /// </summary>
        private static (string clip, GaitMeasure.Result gait) PickMove(Spec spec, Dictionary<string, AnimationClip> clips)
        {
            var wanted = MeasureGait(spec, spec.Move, clips[spec.Move]);
            float rate = spec.Speed / Mathf.Max(0.01f, wanted.Speed);
            if (rate >= MinMoveRate && rate <= MaxMoveRate) return (spec.Move, wanted);
            var best = (clip: spec.Move, gait: wanted, score: Mathf.Abs(Mathf.Log(Mathf.Max(0.01f, rate))));
            foreach (var name in LoopClips.Where(n => n != spec.Move && clips.ContainsKey(n)))
            {
                var g = MeasureGait(spec, name, clips[name]);
                float score = Mathf.Abs(Mathf.Log(Mathf.Max(0.01f, spec.Speed / Mathf.Max(0.01f, g.Speed))));
                if (score < best.score) best = (name, g, score);
            }
            return (best.clip, best.gait);
        }

        /// <summary>Ground speed of a walk clip on its own skeleton (KAN-30 GaitMeasure), in a +Z-forward frame.</summary>
        private static GaitMeasure.Result MeasureGait(Spec spec, string clipName, AnimationClip move)
        {
            var frame = new GameObject("Frame");
            try
            {
                var skeleton = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.ClipPath(clipName)));
                skeleton.transform.SetParent(frame.transform, false);
                var bones = skeleton.GetComponentsInChildren<Transform>(true);
                var feet = new[] { "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase" }.Select(n => bones.First(b => b.name == n)).ToArray();
                return GaitMeasure.GroundSpeed(skeleton, move, frame.transform, feet);
            }
            finally
            {
                Object.DestroyImmediate(frame);
            }
        }

        // ------------------------------------------------------------ Prefab

        private static (float height, float radius) BuildPrefab(GameArtKit kit, Spec spec, GameObject modelAsset, Material mat,
            RuntimeAnimatorController controller, float clipSpeed)
        {
            var root = new GameObject($"Enemy_{spec.Id}");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.transform.SetParent(root.transform, false);
                var rig = model.transform.Find($"{spec.Id}_Rig") ?? throw new System.InvalidOperationException($"{spec.Id}: rig node missing");
                var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
                body.transform.SetParent(rig, true); // under the Animator: culling sees the renderer
                var oldAnimator = model.GetComponent<Animator>();
                if (oldAnimator != null) Object.DestroyImmediate(oldAnimator);

                var animator = rig.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; // in-place clips; Enemy moves the root
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // state machine runs off screen (KAN-43)

                body.sharedMaterials = Enumerable.Repeat(mat, body.sharedMaterials.Length).ToArray();
                body.quality = spec.Boss ? SkinQuality.Bone4 : SkinQuality.Bone2; // one boss on screen; a horde of ~100 px zombies
                body.skinnedMotionVectors = false;
                body.updateWhenOffscreen = false;
                Quiet(body);

                var (height, radius) = MeasureBody(body);
                var shadowRoot = new GameObject("Shadow").transform;
                shadowRoot.SetParent(root.transform, false);
                shadowRoot.localScale = Vector3.one * Mathf.Max(1f, radius / 0.42f);
                ArcherModelBuilder.AddBlobShadow(shadowRoot, kit.BlobShadow, kit.Quad);

                // Reach grows with the body: a Knight-sized zombie keeps the old 1.4 m contact / 1.1 m stop (CombatBuilder).
                float reach = 1.4f * Mathf.Pow(Mathf.Max(0.5f, height / 1.85f), 0.75f);
                var enemy = root.AddComponent<Enemy>();
                var so = new SerializedObject(enemy);
                so.FindProperty("body").objectReferenceValue = body;
                so.FindProperty("baseTint").colorValue = Color.white;
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("maxHealth").floatValue = spec.Health;
                so.FindProperty("moveSpeed").floatValue = spec.Speed;
                so.FindProperty("height").floatValue = height;
                so.FindProperty("hitRadius").floatValue = radius;
                so.FindProperty("separation").floatValue = Mathf.Max(0.9f, radius * 2.1f);
                so.FindProperty("stopDistance").floatValue = reach - 0.3f;
                so.FindProperty("contactRange").floatValue = reach;
                so.FindProperty("damagePerSecond").floatValue = spec.Dps;
                so.FindProperty("xpValue").intValue = spec.Xp;
                so.FindProperty("bodyScale").floatValue = 1f;
                so.FindProperty("speedJitter").floatValue = spec.SpeedJitter;
                so.FindProperty("scaleJitter").floatValue = spec.ScaleJitter;
                so.FindProperty("animRefSpeed").floatValue = clipSpeed;
                so.FindProperty("walkState").stringValue = "Move";
                so.FindProperty("attackState").stringValue = "Attack";
                so.FindProperty("isBoss").boolValue = spec.Boss;
                so.FindProperty("knockbackScale").floatValue = spec.Knockback;
                so.FindProperty("deathHold").floatValue = spec.Boss ? BossDeathPlayTime : DeathPlayTime;
                so.ApplyModifiedPropertiesWithoutUndo();
                RangedEnemyBuilder.ConfigurePrefab(root, enemy, spec.Id); // hold band + attack (KAN-105)
                SpecialMeleeBuilder.ConfigurePrefab(root, spec.Id); // burst / leap / slam (KAN-106)
                ShamanBuilder.ConfigurePrefab(root, enemy, spec.Id); // hold band + orb ring / hex / raise / blink (KAN-107)
                SurvivorSouls.Editor.Boss.BossBuilder.ConfigurePrefab(root, enemy, spec.Id); // Bone Matriarch: no contact, hold band, hover (KAN-111)
                PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
                return (height, radius);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Height = top of the bind-pose mesh (the rig's rest pose) above the feet. Hit radius = 70th percentile of the
        /// horizontal distance of the chest band (45-75 % of the height) from the body axis (the A-pose arms do not blow it
        /// up), at least a quarter of the height.
        /// </summary>
        private static (float height, float radius) MeasureBody(SkinnedMeshRenderer body)
        {
            var m = body.transform.localToWorldMatrix; // the rig node above carries the FBX 0.01 (cm) scale
            var points = body.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            float minY = points.Min(p => p.y), maxY = points.Max(p => p.y);
            float height = maxY - Mathf.Min(0f, minY);
            var band = points.Where(p => p.y > height * 0.45f && p.y < height * 0.75f)
                .Select(p => new Vector2(p.x, p.z).magnitude).OrderBy(d => d).ToArray();
            float radius = band.Length > 0 ? band[Mathf.Clamp(Mathf.RoundToInt(band.Length * 0.7f), 0, band.Length - 1)] : height * 0.15f;
            // Floor: the old zombie's 0.45 m at 1.8 m, so thin bodies are not harder to hit than the horde they replace.
            return (height, Mathf.Clamp(Mathf.Max(radius, height * 0.25f), 0.3f, 1.6f));
        }

        private static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ------------------------------------------------------------ Waves

        /// <summary>
        /// Share of the old Shambler weight that each melee tier takes, per wave phase (Laborer, Watchman). Later phases are
        /// tougher; phases past the list use the last row. KAN-128: the cut Militiaman share (140 HP) went 0.6 / 0.4 to the
        /// Laborer / Watchman (same mean health), the cut Gravebound Knight share went to the Watchman.
        /// </summary>
        public static readonly float[][] TierShares =
        {
            new[] { 0.94f, 0.06f },
            new[] { 0.792f, 0.208f },
            new[] { 0.668f, 0.332f },
            new[] { 0.582f, 0.418f },
        };

        /// <summary>
        /// One-time move of the wave table to the melee tiers (KAN-104): every Shambler entry (<paramref name="shambler"/>)
        /// is split into the melee tiers by <see cref="TierShares"/>; Runners and Brutes stay. Runs only while no tier is in
        /// the table, so later designer edits are kept. Returns true when it changed the table.
        /// </summary>
        public static bool AddMeleeTiers(EnemyWaveTable table, Enemy shambler)
        {
            var tiers = MeleeTiers.Select(LoadPrefab).ToArray();
            if (table == null || tiers.Any(t => t == null)) return false;
            var phases = Enumerable.Range(0, table.PhaseCount).Select(table.GetPhase).ToArray();
            if (phases.Any(p => p.enemies != null && p.enemies.Any(e => tiers.Contains(e.prefab)))) return false;
            for (int i = 0; i < phases.Length; i++)
            {
                var shares = TierShares[Mathf.Min(i, TierShares.Length - 1)];
                var list = new List<EnemyWaveTable.EnemyEntry>();
                foreach (var e in phases[i].enemies ?? new EnemyWaveTable.EnemyEntry[0])
                {
                    if (e.prefab != shambler)
                    {
                        list.Add(e);
                        continue;
                    }
                    for (int t = 0; t < tiers.Length; t++)
                        if (shares[t] > 0f) list.Add(new EnemyWaveTable.EnemyEntry { prefab = tiers[t], weight = Mathf.Round(e.weight * shares[t] * 1000f) / 1000f });
                }
                phases[i].enemies = list.ToArray();
            }
            table.Configure(table.FirstSpawnDelay, table.PrewarmPerType, phases);
            EditorUtility.SetDirty(table);
            Debug.Log("[EnemyRosterBuilder] wave table: Shambler entries split into the melee tiers");
            return true;
        }
    }
}
