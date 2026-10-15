using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Editor.Boss;
using SurvivorSouls.Editor.Chamber;
using SurvivorSouls.Editor.Chests;
using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Editor.Economy;
using SurvivorSouls.Editor.Enemies;
using SurvivorSouls.Editor.Effects;
using SurvivorSouls.Editor.Progression;
using SurvivorSouls.Editor.Skills;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.FX;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Realms;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Telemetry;
using SurvivorSouls.Gameplay.World;
using SurvivorSouls.UI.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace SurvivorSouls.Editor
{
    /// <summary>
    /// Builds the Game scene from code: art kit (textures, materials), Realm 1 look (snowy shore, sea, sky),
    /// Archer prefab, streamed world, camera,
    /// joystick input, HUD and combat (auto-shooting crossbow, enemy spawner, health bars, player health + death screen),
    /// XP (soul wisps, level bar, level-up flow), skills (stat sheet, 3-card pick, owned-skill row),
    /// Chamber exit (survival timer + HUD countdown; the portal is in the exit tile prefab), boss fight in boss Chambers (KAN-58). Re-run after edits: menu "Survivor Souls/Rebuild Game Scene", or batchmode
    /// <c>-executeMethod SurvivorSouls.Editor.GameSceneBuilder.BuildFromCommandLine</c>.
    /// Hand edits to the generated scene are overwritten on rebuild.
    /// </summary>
    public static class GameSceneBuilder
    {
        private const string Root = "Assets/_Project";
        private const string ScenePath = Root + "/Scenes/" + SceneNames.Game + ".unity";
        private const string HudUxmlPath = Root + "/UI/Hud/GameHud.uxml";
        private const string HudPanelSettingsPath = Root + "/UI/Hud/GameHudPanelSettings.asset";
        private const string ThemePath = Root + "/UI/Theme/SurvivorSoulsTheme.tss";
        private const string RealmCatalogPath = Root + "/Data/Realms/RealmCatalog.asset"; // made by MainMenuSceneBuilder (KAN-55)

        [MenuItem("Survivor Souls/Rebuild Game Scene")]
        public static void Build()
        {
            var kit = GameArtKit.Build();
            var realm = RealmArt.Build(kit); // Realm 1 look: values in Art/World/Realm1_Look.asset
            // Main character: the Meshy model when its source GLB exists, else the procedural placeholder.
            var archerPrefab = ArcherModelBuilder.BuildPrefab(kit.Palette, kit.BlobShadow, kit.Quad);
            if (MeshyCharacterImporter.SourceExists) archerPrefab = MeshyCharacterImporter.BuildPrefab(kit.BlobShadow, kit.Quad);
            CombatBuilder.Build(kit);
            EnemyRosterBuilder.Build(kit); // 15 Meshy enemies (KAN-104): prefabs, controllers, materials
            HeroPetArtImporter.Configure(); // Meshy hero + pet art import settings (KAN-156), before KAN-150 / KAN-153 use it
            var panelSettings = CreatePanelSettings();
            var sfx = SfxBuilder.Build(); // before the world: the exit portal tile needs its hum clip

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment(realm);
            var game = new GameObject("Game").AddComponent<GameSceneController>();
            game.gameObject.AddComponent<KillHaptics>();
            CreateSunLight(realm.Look);

            var joystick = new GameObject("Input").AddComponent<FloatingJoystick>();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(archerPrefab);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // centre of the spawn tile
            var stats = player.AddComponent<PlayerStats>(); // skills write here; mover, shooter, health and pickup read from it
            // Bought soul tree levels as permanent stat sources before anything reads the stats (KAN-98)
            var treeBonuses = player.AddComponent<SurvivorSouls.Gameplay.Meta.SoulTreeBonuses>();
            SetRef(treeBonuses, "tree", Economy.EconomyBuilder.LoadOrCreateSoulTree());
            SetRef(treeBonuses, "stats", stats);
            SetRef(player.GetComponent<PlayerMover>(), "joystick", joystick);
            var world = WorldBuilder.Build(kit, realm, player.transform, player.GetComponent<PlayerMover>(), sfx.PortalHum);
            var combat = CombatBuilder.Load();
            var (shooter, spawner) = CreateCombat(player, combat.bolt, combat.enemy, world);
            CombatBuilder.CreateFx(kit, shooter);
            SetRef(player.GetComponent<ReloadRing>(), "shooter", shooter); // ring under the feet shows the reload
            var progression = ProgressionBuilder.Create(kit, player); // before health: death disables the SoulCollector
            var (health, death) = CreatePlayerHealth(player, joystick, shooter);
            SetRef(progression.Flow, "player", health);
            var skills = SkillsBuilder.Create(kit, progression.Xp.gameObject, player, shooter, health);
            // Reliquary Hunter chest drops (KAN-72), walk-in pickup + upgrade screen queued on the level-up flow (KAN-73)
            var chests = ChestBuilder.Create(kit, player.GetComponent<PlayerStats>(), health, skills, progression.Flow, progression.Souls);
            // Telemetry events only (KAN-75): SkillUpgraded, ChestDropped, ChestOpened, ChestExpired
            var telemetry = chests.gameObject.AddComponent<RunTelemetry>();
            SetRef(telemetry, "skills", skills);
            SetRef(telemetry, "chests", chests);
            SetRef(game, "player", health);
            SetRef(game, "spawner", spawner);
            var timer = ChamberBuilder.Create(player, health); // portal opens when it ends (KAN-39)
            SetRef(game, "timer", timer);
            // Portal -> fade -> same scene reloaded as the next Chamber, run carried over (KAN-40).
            var counters = ChamberBuilder.CreateCounters(timer.gameObject, progression.Souls); // kills + souls this run (KAN-51)
            var transition = ChamberBuilder.CreateTransition(timer.gameObject, player, health, progression.Xp, skills, counters, spawner, joystick);
            SetRef(counters, "health", health); // death banks the run's souls into the wallet (KAN-96)
            SetRef(counters, "transition", transition); // so does a cleared Realm
            var gemRewards = EconomyBuilder.LoadOrCreateGemRewards(); // Ember Shard sources (KAN-97)
            SetRef(counters, "gems", gemRewards); // first run end: one-time shard bonus
            SetRef(chests.GetComponent<SurvivorSouls.Gameplay.Chests.ChestRewards>(), "gems", gemRewards); // Rare chest shards
            var chamberSetup = ChamberBuilder.CreateSetup(timer.gameObject, world, spawner); // per-Chamber difficulty + layout seed (KAN-46)
            ChamberBuilder.CreateHordeScaler(timer.gameObject, spawner, player.GetComponent<PlayerStats>(), timer); // zombie health to power + tiers (KAN-92)
            var boss = BossBuilder.Create(kit, health, timer, spawner, progression.Souls); // boss Chambers: fight, slam, reward (KAN-58)
            var enemyShots = RangedEnemyBuilder.CreateScene(kit, health); // ranged zombie shots, hazards, warnings (KAN-105)
            ShamanBuilder.CreateScene(enemyShots.transform); // Hexbound Shaman puffs + priority markers (KAN-107)
            SetRef(game, "boss", boss); // slam camera shake
            var hearts = boss.GetComponent<BossHearts>();
            SetRef(game, "hearts", hearts); // haptic tap on a heart pickup (KAN-60)
            var audio = CreateAudio(sfx, shooter, player.GetComponent<PlayerMover>(), health, progression, skills, timer, transition);
            SetRef(audio, "chests", chests); // chest land thud + open creak
            var rarityStyles = SkillsBuilder.BuildStyles(sfx.RarityReveals); // tier names, gems, reveal sounds (KAN-37)
            WindTrailBuilder.Create(kit, player);

            var camera = CreateCamera(player.transform, realm.Look);
            SetRef(spawner, "viewCamera", camera); // off-screen test uses the live camera (aspect, zoom)
            SetRef(game, "followCamera", camera.GetComponent<TopDownFollowCamera>()); // shake when the portal opens
            // Round arena for boss Chambers (KAN-59): ChamberSetup enters it instead of the streamed shore.
            var arena = BossArenaBuilder.Create(kit, realm, sfx.PortalHum, world, spawner, player.transform, camera.GetComponent<TopDownFollowCamera>(),
                player.GetComponent<ObjectiveArrow>(), GameObject.Find("Sun Light").GetComponent<Light>());
            SetRef(chamberSetup, "arena", arena);
            var choice = CreateHud(panelSettings, joystick, camera, health, death, progression, skills, rarityStyles, player.transform, timer, transition,
                counters);
            SetRef(audio, "skillChoice", choice); // card reveal sound per tier, chest upgrade pick
            SetRef(choice.GetComponent<CombatHud>(), "chestRewards", chests.GetComponent<SurvivorSouls.Gameplay.Chests.ChestRewards>()); // "+N SHARDS" pop (KAN-97)
            SetRef(audio, "skillHud", choice.GetComponent<SkillHud>()); // chime when a skill slot glows after a level-up (KAN-74)
            SetRef(audio, "boss", boss); // boss groans + slam thud
            SetRef(audio, "hearts", hearts); // heal swell (KAN-60)
            SetRef(choice.gameObject.AddComponent<BossHud>(), "fight", boss); // boss bar in the timer's place
            SetRef(choice.gameObject.AddComponent<BossBannerHud>(), "fight", boss); // name at the intro, "felled" at the kill (KAN-62)
            BossBuilder.CreateReward(boss, chests, skills, progression.Flow, progression.Xp); // guaranteed reward pick (KAN-62)

            StableAssetWriter.Remember(ScenePath);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuild(ScenePath, index: 1);
            AssetDatabase.SaveAssets();
            StableAssetWriter.SettleAll(); // undo pure fileID churn in the scene, controllers and tiles (KAN-157)
            Debug.Log($"[GameSceneBuilder] Built {ScenePath}");
        }

        /// <summary>Batchmode entry point. Exit code 0 on success.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Cold overcast coast: cool trilight ambient, overcast sky, haze that hides the far edge.</summary>
        private static void SetupEnvironment(RealmArt realm)
        {
            var look = realm.Look;
            RenderSettings.skybox = realm.Sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.ambientSky;
            RenderSettings.ambientEquatorColor = look.ambientEquator;
            RenderSettings.ambientGroundColor = look.ambientGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = look.fogStart;
            RenderSettings.fogEndDistance = look.fogEnd;
            RenderSettings.fogColor = look.fogColor;
        }

        private static void CreateSunLight(RealmLook look)
        {
            var go = new GameObject("Sun Light");
            go.transform.rotation = Quaternion.Euler(50f, -35f, 0f); // soft overcast key, slightly from the front-left
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = look.sunColor;
            light.intensity = look.sunIntensity;
            light.shadows = LightShadows.None; // blob shadows instead of shadow maps
        }

        /// <summary>
        /// Auto-shooter on the player (bolts leave the crossbow muzzle), upper-body recoil, off-screen enemy spawner (wave table asset).
        /// </summary>
        private static (AutoShooter, EnemySpawner) CreateCombat(GameObject player, Bolt bolt, Enemy enemy, WorldStreamer world)
        {
            var shooter = player.AddComponent<AutoShooter>();
            SetRef(shooter, "boltPrefab", bolt);
            var muzzle = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Crossbow_Muzzle");
            if (muzzle != null) SetRef(shooter, "muzzle", muzzle);
            SetRef(player.AddComponent<CrossbowRecoil>(), "rig", player.GetComponentInChildren<HumanoidRig>());

            var spawner = new GameObject("Enemies").AddComponent<EnemySpawner>();
            var waves = CombatBuilder.LoadOrCreateWaves(enemy);
            EnemyRosterBuilder.AddMeleeTiers(waves, enemy); // one-time: Shambler slots -> the melee tiers (KAN-104, KAN-128)
            SpecialMeleeBuilder.AddToWaves(waves); // one-time: Grave Brute for the old Brute, Bloater + Ghoul from 45 s (KAN-106)
            ChamberSpawnTables.Apply(waves); // one-time: shooters + Chamber gates + shooter cap (KAN-112)
            SetRef(spawner, "waves", waves);
            SetRef(spawner, "player", player.transform);
            SetRef(spawner, "world", world);
            // Extras: types needed in every Chamber whatever the gates (KAN-112): dev spawns of the ranged zombies (KAN-105) and
            // the Hexbound Shaman (KAN-107), boss summons (KAN-119 Rotting Colossus: Frenzied Ghouls; KAN-111 Bone Matriarch:
            // Graveyard Laborers, a wave type).
            var extras = new SerializedObject(spawner);
            SetArray(extras.FindProperty("extraTypes"), RangedEnemyBuilder.Ids.Append("FrenziedGhoul").Append(ShamanBuilder.Id).Select(EnemyRosterBuilder.LoadPrefab).ToArray());
            extras.ApplyModifiedPropertiesWithoutUndo();
            return (shooter, spawner);
        }

        /// <summary>
        /// Player health pool, enemy contact damage, red hit flash on the body (renderers under "Model" only, not the
        /// blob shadow or ground decals) and the death sequence (input, movement and attacks off; model tips over).
        /// </summary>
        private static (PlayerHealth, PlayerDeath) CreatePlayerHealth(GameObject player, FloatingJoystick joystick, AutoShooter shooter)
        {
            var health = player.AddComponent<PlayerHealth>();
            var contact = player.AddComponent<ContactDamageReceiver>();
            var model = player.transform.Find("Model");
            if (model == null) throw new System.InvalidOperationException($"{player.name} has no 'Model' child");

            var flash = new SerializedObject(player.AddComponent<PlayerHitFlash>());
            SetArray(flash.FindProperty("renderers"), model.GetComponentsInChildren<Renderer>(true));
            flash.ApplyModifiedPropertiesWithoutUndo();

            var death = player.AddComponent<PlayerDeath>();
            var so = new SerializedObject(death);
            var disable = new List<Object> { joystick, player.GetComponent<PlayerMover>(), shooter, contact };
            disable.AddRange(player.GetComponents<LocomotionAnimatorDriver>());
            disable.AddRange(player.GetComponents<ProceduralLocomotion>());
            disable.AddRange(player.GetComponents<SoulCollector>()); // wisps stop flying to a dead Knight
            var arrow = player.GetComponent<ObjectiveArrow>();
            if (arrow != null) disable.Add(arrow); // else its LateUpdate re-shows the arrow
            var hide = new List<Object>();
            if (arrow != null && new SerializedObject(arrow).FindProperty("arrow").objectReferenceValue is Transform a) hide.Add(a.gameObject);
            SetArray(so.FindProperty("disableOnDeath"), disable);
            SetArray(so.FindProperty("hideOnDeath"), hide);
            so.FindProperty("model").objectReferenceValue = model;
            so.FindProperty("animator").objectReferenceValue = model.GetComponent<Animator>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return (health, death);
        }

        private static void SetArray(SerializedProperty array, IReadOnlyList<Object> values)
        {
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>Procedural sound set (shots, impacts, marker ticks, deaths, snow footsteps, soul chimes, level-up, boon pick, portal horn, wind + surf ambience) on one audio object.</summary>
        private static GameAudio CreateAudio(SfxBuilder.Set sfx, AutoShooter shooter, PlayerMover mover, PlayerHealth health,
            ProgressionBuilder.Result progression, SkillManager skills, SurvivalTimer timer, ChamberTransition transition)
        {
            var audio = new GameObject("Audio").AddComponent<GameAudio>();
            var so = new SerializedObject(audio);
            so.FindProperty("shooter").objectReferenceValue = shooter;
            so.FindProperty("player").objectReferenceValue = mover;
            so.FindProperty("playerHealth").objectReferenceValue = health;
            so.FindProperty("souls").objectReferenceValue = progression.Souls;
            so.FindProperty("levelUp").objectReferenceValue = progression.Flow;
            so.FindProperty("skills").objectReferenceValue = skills;
            so.FindProperty("timer").objectReferenceValue = timer;
            so.FindProperty("transition").objectReferenceValue = transition;
            SetClips(so.FindProperty("boonPicks"), sfx.BoonPicks);
            SetClips(so.FindProperty("chestDrops"), sfx.ChestDrops);
            SetClips(so.FindProperty("chestOpens"), sfx.ChestOpens);
            SetClips(so.FindProperty("skillUpgrades"), sfx.SkillUpgrades);
            SetClips(so.FindProperty("heartHeals"), sfx.HeartHeals);
            SetClips(so.FindProperty("portalOpens"), sfx.PortalOpens);
            SetClips(so.FindProperty("portalEnters"), sfx.PortalEnters);
            SetClips(so.FindProperty("chamberArrives"), sfx.ChamberArrives);
            SetClips(so.FindProperty("soulChimes"), sfx.SoulChimes);
            SetClips(so.FindProperty("levelUps"), sfx.LevelUps);
            SetClips(so.FindProperty("shots"), sfx.Shots);
            SetClips(so.FindProperty("impacts"), sfx.Impacts);
            SetClips(so.FindProperty("deaths"), sfx.Deaths);
            SetClips(so.FindProperty("footsteps"), sfx.Footsteps);
            SetClips(so.FindProperty("hitMarkers"), sfx.HitMarkers);
            SetClips(so.FindProperty("killMarkers"), sfx.KillMarkers);
            so.FindProperty("ambience").objectReferenceValue = sfx.Ambience;
            so.ApplyModifiedPropertiesWithoutUndo();
            return audio;
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        private static Camera CreateCamera(Transform target, RealmLook look)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = look.fogColor;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 5f;
            cam.farClipPlane = 90f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            go.AddComponent<AudioListener>();

            var follow = go.AddComponent<TopDownFollowCamera>();
            follow.Target = target;
            var so = new SerializedObject(follow);
            // Open world: follow the player freely in X and Z.
            so.FindProperty("followX").floatValue = 1f;
            so.FindProperty("lookZRange").vector2Value = new Vector2(-10000f, 10000f);
            so.ApplyModifiedPropertiesWithoutUndo();
            go.transform.SetPositionAndRotation(target.position + new Vector3(0f, 34f, -20f), Quaternion.Euler(60f, 0f, 0f));
            return cam;
        }

        private static SkillChoiceScreen CreateHud(PanelSettings panelSettings, FloatingJoystick joystick, Camera camera, PlayerHealth health, PlayerDeath death,
            ProgressionBuilder.Result progression, SkillManager skills, SkillRarityStyles rarityStyles, Transform player, SurvivalTimer timer,
            ChamberTransition transition, RunCounters counters)
        {
            var go = new GameObject("HUD");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            if (doc.visualTreeAsset == null) throw new System.InvalidOperationException($"UXML missing: {HudUxmlPath}");
            var hudController = go.AddComponent<GameHudController>();
            SetRef(hudController, "joystick", joystick);
            SetRef(hudController, "counters", counters); // leaving to the menu banks the run's souls (KAN-96)
            SetRef(go.AddComponent<SoulCounterHud>(), "counters", counters); // souls this run, right of the health bar (KAN-96)
            SetRef(go.AddComponent<JoystickView>(), "joystick", joystick);
            SetRef(go.AddComponent<CombatHud>(), "worldCamera", camera);
            SetRef(go.AddComponent<HitMarkerHud>(), "worldCamera", camera);
            var hp = go.AddComponent<PlayerHealthHud>();
            SetRef(hp, "health", health);
            SetRef(hp, "death", death);
            SetRef(hp, "worldCamera", camera);
            SetRef(hp, "counters", counters); // souls / kills / Chamber on the death screen (KAN-51)

            // XP bar + "LEVEL UP" pop; each level-up opens the 3-card skill pick, owned skills show under the health bar.
            var xpHud = go.AddComponent<XpHud>();
            SetRef(xpHud, "xp", progression.Xp);
            SetRef(xpHud, "flow", progression.Flow);
            SetRef(xpHud, "player", player);
            SetRef(xpHud, "worldCamera", camera);
            var choice = go.AddComponent<SkillChoiceScreen>();
            SetRef(choice, "skills", skills);
            SetRef(choice, "styles", rarityStyles);
            SetRef(progression.Flow, "choice", choice);
            SetRef(go.AddComponent<SkillHud>(), "skills", skills);
            SetRef(go.AddComponent<TimerHud>(), "timer", timer); // countdown under the XP bar + toasts (KAN-39)
            SetRef(go.AddComponent<ChamberTransitionHud>(), "transition", transition); // white fade + "CHAMBER II" card (KAN-40)
            var victory = go.AddComponent<RealmVictoryHud>(); // "REALM CLEARED" after the last Chamber's portal (KAN-47)
            SetRef(victory, "transition", transition);
            SetRef(victory, "realms", AssetDatabase.LoadAssetAtPath<RealmCatalog>(RealmCatalogPath));
            SetRef(victory, "counters", counters); // banked souls + wallet line (KAN-96)
            return choice;
        }

        private static PanelSettings CreatePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(HudPanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, HudPanelSettingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1080, 1920);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0f; // portrait: lock to width
            settings.sortingOrder = 0;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void SetRef(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuild(string scenePath, int index)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Insert(Mathf.Min(index, scenes.Count), new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
