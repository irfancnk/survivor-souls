using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Dev;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Dev touch debug panel (KAN-87): the bootstrap adds one panel to the Game scene (none elsewhere), it starts closed,
    /// open takes one pause hold and close gives it back (other holds stay), and the acceptance flow through the panel's
    /// own controls: skill to level 3 with the slider, a Rare chest from the button, close, the game runs again.
    /// </summary>
    public sealed class DevDebugPanelTests
    {
        private const int TestRealm = 87; // the Game scene test never touches real Realm progress
        private const string LastRealmKey = "progress.lastRealm";

        private bool _savedLog;
        private int _savedLastRealm;
        private bool _hadLastRealm;

        [SetUp]
        public void SetUp()
        {
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            _savedLog = GameTelemetry.LogToConsole;
            GameTelemetry.LogToConsole = false;
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            GameTelemetry.Clear();
            GameTelemetry.LogToConsole = _savedLog;
            GamePause.Clear();
            Time.timeScale = 1f;
            Enemy.Walkability = null;
            RunProgress.Clear(TestRealm);
            if (_hadLastRealm) PlayerPrefs.SetInt(LastRealmKey, _savedLastRealm);
            else PlayerPrefs.DeleteKey(LastRealmKey);
            RunState.NewRun();
        }

        private static void Click(Button button)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        private static IEnumerator LoadGame()
        {
            RunState.NewRun(TestRealm, 8787);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
        }

        private static IEnumerator UnloadGame()
        {
            var empty = SceneManager.CreateScene("DevPanelEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }

        [Test]
        public void AttachTo_SceneWithoutGameHud_AddsNoPanel()
        {
            var scene = SceneManager.CreateScene("DevPanelNoHud");
            try
            {
                Assert.That(DevDebugPanel.AttachTo(scene), Is.Null);
            }
            finally
            {
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator GameScene_OnePanel_StartsClosed_OpenHoldsOnePause()
        {
            yield return LoadGame();
            var panels = Object.FindObjectsByType<DevDebugPanel>();
            Assert.That(panels.Length, Is.EqualTo(1), "the bootstrap adds exactly one panel to the Game scene");
            var panel = panels[0];
            Assert.That(DevDebugPanel.Instance, Is.SameAs(panel));
            Assert.That(DevDebugPanel.AttachTo(SceneManager.GetSceneByName("Game")), Is.Null, "no second panel");
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(panel.Root.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(GamePause.IsPaused, Is.False);

            panel.Open();
            panel.Open();
            Assert.That(panel.IsOpen && GamePause.IsPaused && Time.timeScale == 0f, Is.True);
            panel.Close();
            Assert.That(GamePause.IsPaused, Is.False, "two opens took one hold");
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            GamePause.Acquire(); // another system (level-up pick) pauses too
            panel.Toggle();
            panel.Toggle();
            Assert.That(GamePause.IsPaused, Is.True, "closing the panel keeps the other hold");
            GamePause.Release();
            Assert.That(GamePause.IsPaused, Is.False);

            yield return UnloadGame();
        }

        [UnityTest]
        public IEnumerator GameScene_SetSkillTo3_DropRareChest_Close_GameRuns()
        {
            yield return LoadGame();
            var panel = DevDebugPanel.Instance;
            var skills = Object.FindAnyObjectByType<SkillManager>();
            var dropper = Object.FindAnyObjectByType<ChestDropper>();
            Assert.That(panel != null && skills != null && dropper != null, Is.True);

            panel.Open();
            yield return null;
            var all = new List<SkillDefinition>();
            DevCheats.CollectSkills(skills, all);
            Assert.That(all.Count, Is.GreaterThan(5), "every skill of the run table has a row");
            var skill = all.Find(s => s.MaxLevel >= 3 && skills.GetLevel(s) == 0);
            Assert.That(skill, Is.Not.Null);
            Assert.That(panel.Root.Q(DevDebugPanel.SkillRowPrefix + skill.Id), Is.Not.Null);

            var slider = panel.SkillSlider(skill.Id);
            Assert.That(slider.highValue, Is.EqualTo(skill.MaxLevel));
            slider.value = 3;
            Assert.That(skills.GetLevel(skill), Is.EqualTo(3));
            var e = GameTelemetry.Recent(0);
            Assert.That((e.Name, e.Subject, e.To, e.Source), Is.EqualTo((GameTelemetry.SkillUpgraded, skill.Id, 3, "debug")));

            Click(panel.Root.Q<Button>(DevDebugPanel.SkillRowPrefix + skill.Id + "-minus"));
            yield return null;
            Assert.That(skills.GetLevel(skill), Is.EqualTo(2), "- button lowers one level");
            Assert.That(slider.value, Is.EqualTo(2), "slider follows the level");
            Click(panel.Root.Q<Button>(DevDebugPanel.SkillRowPrefix + skill.Id + "-plus"));
            yield return null;
            Assert.That(skills.GetLevel(skill), Is.EqualTo(3));

            int before = dropper.Active.Count;
            Click(panel.Root.Q<Button>(DevDebugPanel.DropRareName));
            yield return null;
            Assert.That(dropper.Active.Count, Is.EqualTo(before + 1));
            Assert.That(dropper.Active[dropper.Active.Count - 1].Tier, Is.EqualTo(ChestTier.Rare));
            Assert.That(GameTelemetry.Recent(0).Name, Is.EqualTo(GameTelemetry.ChestDropped));

            Click(panel.Root.Q<Button>(DevDebugPanel.CloseName));
            yield return null;
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(GamePause.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the game continues");
            Assert.That(skills.GetLevel(skill), Is.EqualTo(3), "the change stays after close");

            yield return UnloadGame();
        }
    }
}
