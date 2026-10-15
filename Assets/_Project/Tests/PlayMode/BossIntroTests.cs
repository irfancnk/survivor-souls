using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.World;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UIElements;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-62 (KAN-22.5) acceptance, logic side: the intro holds the Knight (no move, no damage) and the boss (no attack)
    /// for the intro time only, then the fight starts; a kill during the intro frees the Knight; the victory beat delays
    /// the portal and the reward, pauses with the game and runs once; the reward is a Rare chest where the boss fell (never
    /// fades, ignores the ground cap) or a bonus skill pick when nothing can level up; the camera pulls out and back; the
    /// kill slow motion holds, eases back and never fights a pause; the banner shows the name, then "GREAT FOE FELLED";
    /// the next boss Chamber wraps; the built Game scene plays intro -> fight -> beat -> chest; and the balance estimate of
    /// a 10-pick build lands in the 60-90 s target. Feel and the real dodge rate: phone.
    /// </summary>
    public sealed class BossIntroTests
    {
        private const float Frame = 1f / 60f;
        private const int TestRealm = 97; // never touches the saved progress of a real Realm
        private const string WardenPath = "Assets/_Project/Data/Boss/Boss_Realm1_Warden.asset";
        private const string ButcherPath = "Assets/_Project/Data/Boss/Boss_Realm1_Butcher.asset"; // the shipped Realm 1 boss (KAN-108)
        private const string PyreLordPath = "Assets/_Project/Data/Boss/Boss_PyreLord.asset"; // Realm 2 candidate (KAN-109)
        private const string DifficultyPath = "Assets/_Project/Data/Chamber/ChamberDifficulty_Realm1.asset";

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private PlayerMover _mover;
        private SurvivalTimer _timer;
        private EnemySpawner _spawner;
        private SoulWispField _souls;
        private GroundTelegraph _telegraph;
        private Enemy _body;
        private BossData _data;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private int _introStarted, _introEnded, _finished, _victories, _slamsWarned;
        private Vector3 _victoryAt;

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(6262);

            var playerGo = Track(new GameObject("Player"));
            _player = playerGo.AddComponent<PlayerHealth>();
            _mover = playerGo.AddComponent<PlayerMover>();
            var collector = playerGo.AddComponent<SoulCollector>();
            collector.Setup(0.01f, 1f);

            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false);
            var xp = xpGo.AddComponent<XpManager>();
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100, 1f, 1);
            xp.Setup(curve);
            xpGo.SetActive(true);
            _souls = Track(new GameObject("Souls")).AddComponent<SoulWispField>();
            _souls.Setup(xp, collector, 64);
            _souls.DriftEnabled = false;

            _timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            _timer.Setup(Track(ChamberRules.Create(600f)), _player.transform, _player);
            _timer.Finished += () => _finished++;

            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false);
            _spawner = spawnerGo.AddComponent<EnemySpawner>();

            _telegraph = Track(new GameObject("Telegraph")).AddComponent<GroundTelegraph>();
            var ring = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            var fill = Track(GameObject.CreatePrimitive(PrimitiveType.Quad)).GetComponent<Renderer>();
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            Object.DestroyImmediate(fill.GetComponent<Collider>());
            _telegraph.Setup(ring, fill);

            var bodyGo = Track(new GameObject("BossBody"));
            bodyGo.SetActive(false);
            _body = bodyGo.AddComponent<Enemy>();

            _data = Track(BossData.Create(_body, health: 3000f, introDelay: 2f, spawnWarning: 1f, slamTelegraph: 1f,
                slamDamage: 0.2f, slamRange: 4f, slamRadius: 3f, slamReach: 1.5f, firstSlamDelay: 0.5f, slamCooldown: 3f,
                slamRecover: 0.5f, introTime: 2.4f, victoryBeat: 1.8f));
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(_data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner, _player,
                _souls, _telegraph);
            _fight.IntroStarted += e => _introStarted++;
            _fight.IntroEnded += () => _introEnded++;
            _fight.SlamWarned += (c, r) => _slamsWarned++;
            _fight.VictoryFinished += at =>
            {
                _victories++;
                _victoryAt = at;
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_fight != null && _fight.Boss != null) Object.DestroyImmediate(_fight.Boss.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _introStarted = _introEnded = _finished = _victories = _slamsWarned = 0;
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
            GamePause.Clear();
            Time.timeScale = 1f;
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) _fight.Tick(Frame);
        }

        /// <summary>Begin + wait + warning: the boss just rose and the intro started.</summary>
        private void Rise()
        {
            // A yielding test lets BossFight.Start begin with the last scene's plan (a boss Chamber after a scene test).
            if (_fight.Current == BossFight.Stage.Inactive)
                Assert.That(_fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77)), Is.True);
            Run(_data.IntroDelay + _data.SpawnWarning + 2f * Frame);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Intro));
        }

        private void Kill() => _fight.Boss.TakeDamage(_fight.Boss.Health + 1f, Vector3.zero);

        // ------------------------------------------------------------ Intro

        [Test]
        public void Intro_HoldsKnightAndBoss_ThenTheFightStarts()
        {
            Rise();
            Assert.That(_introStarted, Is.EqualTo(1));
            Assert.That(_fight.KnightHeld, Is.True);
            Assert.That(_mover.InputLocked, Is.True, "no stick input during the intro");
            Assert.That(_player.Invulnerable, Is.True);
            Assert.That(_fight.Boss.Halted, Is.True, "the boss stands for its entrance");
            Assert.That(_fight.HealthFraction, Is.EqualTo(1f));

            // Knight right in front of the boss: still no slam while the intro runs.
            var b = _fight.Boss.transform;
            _player.transform.position = b.position + b.forward * 2f;
            Run(_data.IntroTime - 0.1f);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Intro));
            Assert.That(_slamsWarned, Is.EqualTo(0));
            Assert.That(_introEnded, Is.EqualTo(0));

            Run(0.15f);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            Assert.That(_introEnded, Is.EqualTo(1));
            Assert.That(_fight.KnightHeld, Is.False);
            Assert.That(_mover.InputLocked, Is.False);
            Assert.That(_player.Invulnerable, Is.False);
            Assert.That(_fight.Boss.Halted, Is.False);

            Run(_data.FirstSlamDelay + 0.1f);
            Assert.That(_slamsWarned, Is.EqualTo(1), "the first slam follows its delay after the intro");
            Assert.That(_introStarted, Is.EqualTo(1));
        }

        [Test]
        public void Intro_KnightTakesNoDamage_UntilTheFightStarts()
        {
            Rise();
            Assert.That(_player.TakeDamage(30f), Is.Zero);
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
            Run(_data.IntroTime + Frame);
            Assert.That(_player.TakeDamage(30f), Is.EqualTo(30f));
        }

        [Test]
        public void Intro_KillDuringIntro_FreesTheKnight()
        {
            Rise();
            Kill();
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_introEnded, Is.EqualTo(1));
            Assert.That(_fight.KnightHeld, Is.False);
            Assert.That(_mover.InputLocked, Is.False);
            Assert.That(_player.Invulnerable, Is.False);
        }

        [Test]
        public void Intro_DisabledFight_NeverLeavesTheKnightHeld()
        {
            Rise();
            _fight.enabled = false;
            Assert.That(_mover.InputLocked, Is.False);
            Assert.That(_player.Invulnerable, Is.False);
        }

        [Test]
        public void NoIntroTime_FightStartsWhenTheBossRises()
        {
            var data = Track(BossData.Create(_body, introDelay: 0.5f, spawnWarning: 0.5f));
            var fight = Track(new GameObject("Boss 2")).AddComponent<BossFight>();
            fight.Setup(data, null, _timer, null, _player, null, _telegraph);
            int intros = 0;
            fight.IntroStarted += e => intros++;
            fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77));
            for (float t = 0f; t < 1.1f; t += Frame) fight.Tick(Frame);
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            Assert.That(intros, Is.Zero);
            Assert.That(_mover.InputLocked, Is.False);
            Object.DestroyImmediate(fight.Boss.gameObject);
        }

        // ------------------------------------------------------------ Victory beat

        [Test]
        public void VictoryBeat_PortalAndRewardWaitForTheBeat_Once()
        {
            Rise();
            Run(_data.IntroTime + Frame);
            var fell = _fight.Boss.transform.position;
            Kill();
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(_fight.DeathPoint, Is.EqualTo(fell));
            Assert.That(_finished, Is.Zero, "the portal waits for the beat");
            Assert.That(_victories, Is.Zero);
            Assert.That(_fight.VictoryDone, Is.False);

            Run(_data.VictoryBeat - 0.1f);
            Assert.That(_finished, Is.Zero);
            Run(0.15f);
            Assert.That(_finished, Is.EqualTo(1), "portal open");
            Assert.That(_victories, Is.EqualTo(1));
            Assert.That(_victoryAt, Is.EqualTo(fell), "the reward goes where the boss fell");
            Assert.That(_fight.VictoryDone, Is.True);
            Run(5f);
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_victories, Is.EqualTo(1));
        }

        [Test]
        public void VictoryBeat_PausedTimeHoldsIt()
        {
            Rise();
            Run(_data.IntroTime + Frame);
            Kill();
            for (int i = 0; i < 600; i++) _fight.Tick(0f);
            Assert.That(_victories, Is.Zero, "a level-up pause holds the beat");
            Run(_data.VictoryBeat + Frame);
            Assert.That(_victories, Is.EqualTo(1));
        }

        [Test]
        public void VictoryBeat_ZeroBeat_OpensThePortalWithTheKill()
        {
            var data = Track(BossData.Create(_body, introDelay: 0.1f, spawnWarning: 0.1f));
            var fight = Track(new GameObject("Boss 3")).AddComponent<BossFight>();
            fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, null, _player, null, _telegraph);
            int victories = 0;
            fight.VictoryFinished += at => victories++;
            fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77));
            for (float t = 0f; t < 0.3f; t += Frame) fight.Tick(Frame);
            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            Assert.That(victories, Is.EqualTo(1));
            Assert.That(_finished, Is.EqualTo(1));
            Object.DestroyImmediate(fight.Boss.gameObject);
        }

        // ------------------------------------------------------------ Reward pick

        private ReliquaryChest ChestTemplate()
        {
            var root = Track(new GameObject("Chest Template"));
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var beam = new GameObject("Beam").AddComponent<MeshRenderer>();
            beam.transform.SetParent(root.transform, false);
            var glow = new GameObject("Glow").AddComponent<MeshRenderer>();
            glow.transform.SetParent(root.transform, false);
            var chest = root.AddComponent<ReliquaryChest>();
            chest.Setup(body.transform, beam, glow);
            root.SetActive(false);
            return chest;
        }

        [Test]
        public void Reward_RareChestWhereTheBossFell_NeverFades_IgnoresTheCap()
        {
            var config = Track(ChestDropConfig.Create(cap: 2, life: 10f));
            var dropper = Track(new GameObject("Chests")).AddComponent<ChestDropper>();
            dropper.Setup(config, null, ChestTemplate(), rngSeed: 62);
            Assert.That(dropper.Drop(new Vector3(9f, 0f, 9f), ChestTier.Light, 2, 1), Is.Not.Null);
            Assert.That(dropper.Drop(new Vector3(-9f, 0f, 9f), ChestTier.Light, 2, 1), Is.Not.Null);
            Assert.That(dropper.IsFull, Is.True);

            var reward = Track(new GameObject("Reward")).AddComponent<BossReward>();
            reward.Setup(_fight, dropper, null, null, null);
            int dropped = 0;
            reward.ChestDropped += c => dropped++;
            Rise();
            Run(_data.IntroTime + Frame);
            var fell = _fight.Boss.transform.position;
            Kill();
            Assert.That(reward.Given, Is.False, "nothing before the beat ends");
            Run(_data.VictoryBeat + Frame);

            Assert.That(reward.Given, Is.True);
            Assert.That(dropped, Is.EqualTo(1));
            var chest = reward.Chest;
            Assert.That(chest, Is.Not.Null, "lands even with the ground full");
            Assert.That(chest.Tier, Is.EqualTo(ChestTier.Rare));
            Assert.That(chest.Choices, Is.EqualTo(_data.RewardChoices));
            Assert.That(chest.Upgrades, Is.EqualTo(_data.RewardUpgrades));
            var d = chest.transform.position - fell;
            d.y = 0f;
            Assert.That(d.magnitude, Is.LessThanOrEqualTo(config.Scatter + 0.01f));
            Assert.That(dropper.SkillChests, Is.Zero, "not a skill chest (Rare count untouched)");

            dropper.Tick(3600f);
            Assert.That(chest.IsOnGround, Is.True, "a boss reward never fades");
            Assert.That(dropper.Active, Does.Contain(chest));

            reward.Give(Vector3.zero);
            Assert.That(dropped, Is.EqualTo(1), "once per fight");
        }

        [Test]
        public void Reward_NothingToUpgrade_GivesABonusSkillPick()
        {
            var config = Track(ChestDropConfig.Create());
            var dropper = Track(new GameObject("Chests")).AddComponent<ChestDropper>();
            dropper.Setup(config, null, ChestTemplate(), rngSeed: 63);
            var stats = Track(new GameObject("Stats")).AddComponent<PlayerStats>();
            var table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, unlocks = new SkillDefinition[0] } });
            var skills = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            skills.Setup(table, stats, null, null, stats.transform, rngSeed: 4);
            Assert.That(skills.HasUpgradableSkill(), Is.False);

            var flowGo = Track(new GameObject("Flow"));
            flowGo.SetActive(false); // no Update: the queue is only counted
            var flow = flowGo.AddComponent<LevelUpFlow>();
            flow.Setup(null, _player, null);

            var reward = Track(new GameObject("Reward")).AddComponent<BossReward>();
            reward.Setup(_fight, dropper, skills, flow, null);
            int picks = 0;
            reward.PickQueued += level => picks++;
            reward.Give(Vector3.zero);
            Assert.That(reward.Chest, Is.Null, "a chest would have nothing to offer");
            Assert.That(dropper.Active.Count, Is.Zero);
            Assert.That(picks, Is.EqualTo(1));
            Assert.That(flow.PendingCount, Is.EqualTo(1), "one bonus skill pick queued");
        }

        // ------------------------------------------------------------ Camera, slow motion, banner

        [Test]
        public void Camera_FocusPullsOutAndEasesBack()
        {
            var target = Track(new GameObject("Target")).transform;
            var camGo = Track(new GameObject("Camera"));
            camGo.SetActive(false);
            camGo.AddComponent<Camera>();
            var cam = camGo.AddComponent<TopDownFollowCamera>();
            cam.Target = target;
            camGo.SetActive(true);

            Assert.That(cam.CurrentZoom, Is.EqualTo(1f));
            cam.Focus(new Vector3(0f, 0f, 8f), 1.35f);
            Assert.That(cam.Focused, Is.True);
            cam.StepFocus(0.2f);
            Assert.That(cam.FocusWeight, Is.InRange(0.05f, 0.95f), "eases in, no cut");
            for (int i = 0; i < 200; i++) cam.StepFocus(Frame);
            Assert.That(cam.FocusWeight, Is.EqualTo(1f));
            Assert.That(cam.CurrentZoom, Is.EqualTo(1.35f).Within(1e-4f));

            cam.ReleaseFocus();
            for (int i = 0; i < 200; i++) cam.StepFocus(Frame);
            Assert.That(cam.FocusWeight, Is.Zero);
            Assert.That(cam.CurrentZoom, Is.EqualTo(1f));
            Assert.That(() => cam.StepFocus(Frame), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void SlowMotion_HoldsThenEasesBack_AndNeverWritesDuringAPause()
        {
            var game = Track(new GameObject("Game")).AddComponent<GameSceneController>();
            game.StartSlowMotion(0.3f, 0.8f);
            Assert.That(game.SlowMotion, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.3f).Within(1e-4f));
            game.StepSlowMotion(0.35f);
            Assert.That(Time.timeScale, Is.EqualTo(0.3f).Within(1e-4f), "held for the first half");
            game.StepSlowMotion(0.2f);
            Assert.That(Time.timeScale, Is.InRange(0.31f, 0.99f), "eases back");

            GamePause.Acquire(); // a level-up opens mid slow motion
            game.StepSlowMotion(5f);
            Assert.That(Time.timeScale, Is.Zero, "a pause is never overwritten");
            GamePause.Release();
            game.StepSlowMotion(0.5f);
            Assert.That(game.SlowMotion, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator Banner_ShowsTheNameAtTheIntro_ThenFelledAtTheKill()
        {
            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var banner = go.AddComponent<BossBannerHud>();
            banner.Setup(_fight);
            go.SetActive(true);
            yield return null;
            var band = doc.rootVisualElement.Q("boss-banner");
            Assert.That(band, Is.Not.Null, "boss-banner element in GameHud.uxml");
            Assert.That(banner.IsShown, Is.False);

            Rise();
            Assert.That(banner.IsShown, Is.True);
            Assert.That(banner.IsVictory, Is.False);
            Assert.That(banner.Text, Is.EqualTo(_data.DisplayName));
            Assert.That(banner.Sub, Is.EqualTo(_data.Title.ToUpperInvariant()));
            Assert.That(band.ClassListContains("hud-hidden"), Is.False);
            banner.Step(1f);
            Assert.That(banner.Alpha, Is.EqualTo(1f), "faded in, held while the intro runs");
            banner.Step(10f);
            Assert.That(banner.IsShown, Is.True, "stays until the intro ends");

            Run(_data.IntroTime + Frame);
            banner.Step(0.25f);
            Assert.That(banner.Alpha, Is.InRange(0.01f, 0.99f), "fading out with the fight start");
            banner.Step(1f);
            Assert.That(banner.IsShown, Is.False);
            Assert.That(band.ClassListContains("hud-hidden"), Is.True);

            Kill();
            Assert.That(banner.IsShown, Is.True);
            Assert.That(banner.IsVictory, Is.True);
            Assert.That(banner.Text, Is.EqualTo(BossBannerHud.VictoryText));
            Assert.That(banner.Sub, Is.EqualTo($"+{_data.RewardXp} SOULS"));
            Assert.That(band.ClassListContains("boss-banner--victory"), Is.True);
            Assert.That(() => banner.Step(Frame), Is.Not.AllocatingGCMemory());
            banner.Step(5f);
            Assert.That(banner.IsShown, Is.False, "fades after its hold");
        }

        [Test]
        public void NextBossChamber_FindsTheNextBoss_AndWraps()
        {
            var d = Track(ChamberDifficulty.Create(chambers: 10, boss: 5));
            Assert.That(d.NextBossChamber(1), Is.EqualTo(5));
            Assert.That(d.NextBossChamber(4), Is.EqualTo(5));
            Assert.That(d.NextBossChamber(5), Is.EqualTo(10));
            Assert.That(d.NextBossChamber(10), Is.EqualTo(5), "wraps to the first boss");
            var onlyLast = Track(ChamberDifficulty.Create(chambers: 7, boss: 0));
            Assert.That(onlyLast.NextBossChamber(1), Is.EqualTo(7));
            Assert.That(onlyLast.NextBossChamber(7), Is.EqualTo(7));
        }

        [Test]
        public void IntroTick_AllocatesNothing()
        {
            Rise();
            _fight.Tick(Frame);
            Assert.That(() => _fight.Tick(Frame), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Balance (KAN-62)

        /// <summary>
        /// Time to kill the Warden and the shipped Butcher (KAN-108) in their first boss Chamber with a typical 10-pick build. Model: bolt damage x fire
        /// rate x crit, Spread Shot side bolts (at their damage share) hit half the time on the big body, burn adds its dps, and the Knight shoots 55 %
        /// of the fight (it stops to shoot; boss attacks make it move). Target (KAN-22): 60-90 s. The base Knight (no picks)
        /// must still be able to win (under 4 minutes of standing time).
        /// </summary>
        [TestCase(WardenPath)]
        [TestCase(ButcherPath)]
        [TestCase(PyreLordPath)]
        public void Balance_TenPickBuild_KillsTheBossIn60To90Seconds(string path)
        {
            var warden = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>(path);
            var difficulty = UnityEditor.AssetDatabase.LoadAssetAtPath<ChamberDifficulty>(DifficultyPath);
            Assert.That(warden, Is.Not.Null, path);
            Assert.That(difficulty, Is.Not.Null, DifficultyPath);
            int chamber = difficulty.NextBossChamber(0);
            float hp = warden.MaxHealth * difficulty.HealthMultiplier(chamber);

            var stats = Track(new GameObject("Knight")).AddComponent<PlayerStats>();
            float baseTtk = hp / (Dps(stats) * 0.55f);
            var build = new (string id, int level)[]
            {
                ("SharpenedBolts", 2), ("QuickDraw", 2), ("KeenEye", 1), ("Multishot", 1), ("EmberBolts", 1),
                ("Vigor", 1), ("FleetFoot", 1), ("SoulSiphon", 1),
            };
            int picks = 0;
            foreach (var (id, level) in build)
            {
                var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>($"Assets/_Project/Data/Skills/Skill_{id}.asset");
                Assert.That(skill, Is.Not.Null, id);
                skill.ApplyLevel(stats, level);
                picks += level;
            }
            Assert.That(picks, Is.EqualTo(10));
            float ttk = hp / (Dps(stats) * 0.55f);
            Debug.Log($"[Balance] {warden.DisplayName} Chamber {chamber}: {hp:F0} HP; 10 picks {Dps(stats):F0} dps -> {ttk:F0} s; " +
                      $"no picks {Dps(Track(new GameObject("Bare")).AddComponent<PlayerStats>()):F0} dps -> {baseTtk:F0} s");
            Assert.That(ttk, Is.InRange(60f, 90f), "10 picks win in 60-90 s");
            Assert.That(baseTtk, Is.LessThan(240f), "a weak Knight can still win");
        }

        private static float Dps(PlayerStats stats)
        {
            float damage = PlayerStats.Eval(stats, StatId.Damage, 100f); // AutoShooter base in the Game scene
            float rate = PlayerStats.Eval(stats, StatId.AttackSpeed, 1f);
            float crit = Mathf.Clamp01(PlayerStats.Eval(stats, StatId.CritChance, 0f));
            float critMul = Mathf.Max(1f, PlayerStats.Eval(stats, StatId.CritDamage, 2f));
            int extra = stats.ModifyInt(StatId.Multishot, 0);
            float side = Mathf.Clamp01(PlayerStats.Eval(stats, StatId.SpreadDamage, 0.5f)); // AutoShooter spread base (KAN-83)
            float burn = PlayerStats.Eval(stats, StatId.BurnDps, 0f);
            return damage * rate * (1f + crit * (critMul - 1f)) * (1f + 0.5f * extra * side) + burn;
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>The built Game scene, Chamber 5: intro (camera out, Knight held, name) -> fight -> kill -> beat -> chest.</summary>
        [UnityTest]
        public IEnumerator GameScene_IntroFightVictoryBeat_AndRewardChest()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 9001);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            var reward = fight.GetComponent<BossReward>();
            Assert.That(reward, Is.Not.Null, "reward wired on the Boss object");
            var banner = Object.FindAnyObjectByType<BossBannerHud>();
            Assert.That(banner, Is.Not.Null, "banner wired on the HUD");
            var cam = Object.FindAnyObjectByType<TopDownFollowCamera>();
            var mover = Object.FindAnyObjectByType<PlayerMover>();
            var knight = Object.FindAnyObjectByType<PlayerHealth>();
            Assert.That(fight.Data.IntroTime, Is.GreaterThan(1.9f).And.LessThan(3.1f), "2-3 s intro");

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f;
            while (fight.Current != BossFight.Stage.Intro && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Intro));
            Assert.That(mover.InputLocked, Is.True);
            Assert.That(knight.Invulnerable, Is.True);
            Assert.That(cam.Focused, Is.True, "camera pulls out");
            Assert.That(banner.IsShown, Is.True);
            Assert.That(banner.Text, Is.EqualTo(fight.Data.DisplayName));
            yield return new WaitForSeconds(0.6f);
            Assert.That(cam.CurrentZoom, Is.GreaterThan(1.05f));

            wait = fight.Data.IntroTime + 2f;
            while (fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            Assert.That(mover.InputLocked, Is.False);
            Assert.That(knight.Invulnerable, Is.False);
            Assert.That(cam.Focused, Is.False, "camera back to the follow view");

            var skills = Object.FindAnyObjectByType<SkillManager>();
            Assert.That(skills.Grant("sharpened_bolts"), Is.True, "an owned skill that can level up: the reward is a chest");
            var boss = fight.Boss;
            boss.TakeDamage(boss.Health + 1f, Vector3.zero);
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(banner.IsVictory, Is.True);
            Assert.That(Time.timeScale, Is.LessThan(1f), "slow motion");
            Assert.That(SurvivalTimer.Active.IsFinished, Is.False, "portal waits for the beat");

            wait = fight.Data.VictoryBeat / fight.Data.VictorySlowMo + 3f;
            while (!fight.VictoryDone && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.VictoryDone, Is.True);
            Assert.That(SurvivalTimer.Active.IsFinished, Is.True, "portal open");
            Assert.That(reward.Given, Is.True);
            Assert.That(reward.Chest, Is.Not.Null, "Rare chest where the boss fell");
            Assert.That(reward.Chest.Tier, Is.EqualTo(ChestTier.Rare));
            yield return new WaitForSecondsRealtime(fight.Data.VictorySlowMoTime);
            Assert.That(Time.timeScale, Is.EqualTo(1f), "slow motion over");

            var empty = SceneManager.CreateScene("BossIntroEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
