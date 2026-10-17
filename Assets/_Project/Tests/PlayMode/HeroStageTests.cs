using System.Collections;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Heroes;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using TestMode = SurvivorSouls.Core.TestMode;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-244 in play mode on the built MainMenu scene: the 3D hero stage is off until the hero page opens; opening spawns
    /// every roster hero once (clean copies: no gameplay scripts), shows the picked one alone, renders into the page's
    /// stage element and turns the bonfire camera off; the picked hero taunts on open (weapon hand over the head, flare
    /// light on) and on a pick, not on a swipe; a swipe slides the heroes; locked heroes stand in dimmed light; idle
    /// variants come every 5-9 s and move the head / turn the body; a drag turns the hero; closing turns the stage off
    /// and the bonfire camera back on.
    /// </summary>
    public sealed class HeroStageTests
    {
        private const string RosterPath = "Assets/_Project/Data/Heroes/HeroRoster.asset";
        private HeroRoster _roster;

        [SetUp]
        public void SetUp()
        {
            _roster = UnityEditor.AssetDatabase.LoadAssetAtPath<HeroRoster>(RosterPath);
            TestMode.ResetAll();
            SoulWallet.Clear();
            GemWallet.Clear();
            HeroProgress.ResetAll(_roster);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            TestMode.ResetAll();
            SoulWallet.Clear();
            GemWallet.Clear();
            HeroProgress.ResetAll(_roster);
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        private static IEnumerator LoadMenu()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;
        }

        private static IEnumerator UnloadMenu()
        {
            var empty = SceneManager.CreateScene("StageEmpty" + Random.Range(0, 100000));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("MainMenu");
        }

        private static MainMenuController Menu() => Object.FindAnyObjectByType<MainMenuController>();

        [UnityTest]
        public IEnumerator Stage_OffUntilOpen_SpawnsEveryHero_RendersToThePage_ClosedIsOff()
        {
            yield return LoadMenu();
            var menu = Menu();
            var page = menu.HeroPage;
            Assert.That(page, Is.Not.Null);
            var stage = page.Stage;
            Assert.That(stage, Is.Not.Null, "MainMenu scene wires the stage");
            Assert.That(stage.gameObject.activeSelf, Is.False, "off while the page is closed");
            Assert.That(stage.StageCamera.enabled, Is.False);
            Assert.That(stage.MenuCamera.enabled, Is.True);

            menu.OpenHeroes();
            yield return null;
            Assert.That(stage.IsOpen && stage.gameObject.activeSelf, Is.True);
            Assert.That(stage.StageCamera.enabled, Is.True);
            Assert.That(stage.Target, Is.Not.Null);
            Assert.That(stage.StageCamera.targetTexture, Is.SameAs(stage.Target));
            var view = page.Page.Q(HeroSelectScreen.StageName);
            Assert.That(view.style.backgroundImage.value.renderTexture, Is.SameAs(stage.Target), "page shows the render");
            Assert.That(stage.SpawnedCount, Is.EqualTo(_roster.Heroes.Count), "every roster hero spawned once");
            Assert.That(stage.ActiveModels, Is.EqualTo(1), "only the shown hero is on");
            Assert.That(stage.Shown.Id, Is.EqualTo("knight"));
            Assert.That(stage.ShownPivot.GetComponentsInChildren<MonoBehaviour>(true), Has.All.InstanceOf<HeroStageLife>(),
                "clean copy: no gameplay scripts");
            Assert.That(stage.ShownPivot.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.GreaterThan(0));
            Assert.That(stage.ShownAnimator, Is.Not.Null);
            Vector3 view3 = stage.StageCamera.WorldToViewportPoint(stage.ShownPivot.position + Vector3.up * 0.9f);
            Assert.That(view3.x, Is.EqualTo(0.5f).Within(0.05f), "hero in the middle");
            Assert.That(view3.y, Is.InRange(0.45f, 0.75f), "hero above the text block");
            Assert.That(stage.StageCamera.WorldToViewportPoint(stage.ShownPivot.position).y, Is.GreaterThan(0.3f), "feet above the price row");
            yield return Wait(0.4f);
            Assert.That(stage.MenuCamera.enabled, Is.False, "bonfire camera off under the open page");

            Assert.That(menu.Back(), Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(stage.gameObject.activeSelf, Is.False, "closed: stage off");
            Assert.That(stage.StageCamera.enabled, Is.False);
            Assert.That(stage.Target, Is.Null, "render texture freed");
            Assert.That(stage.MenuCamera.enabled, Is.True, "bonfire camera back");
            yield return UnloadMenu();
        }

        [UnityTest]
        public IEnumerator Taunt_OnOpenAndOnPick_WeaponOverHead_NotOnSwipe()
        {
            yield return LoadMenu();
            var menu = Menu();
            var page = menu.HeroPage;
            var stage = page.Stage;
            menu.OpenHeroes();
            yield return Wait(HeroSelectScreen.OpenTauntDelay + 0.1f);
            var life = stage.ShownLife;
            Assert.That(life.Motion, Is.EqualTo(HeroStageMotion.Taunt), "the picked hero taunts when the page opens");
            Assert.That(life.HasWeaponArm, Is.True);
            float flare = 0f, handOverHead = -1f;
            while (life.Motion == HeroStageMotion.Taunt)
            {
                flare = Mathf.Max(flare, stage.FlareLight.intensity);
                handOverHead = Mathf.Max(handOverHead, life.WeaponHandBone.position.y - life.Head.position.y);
                yield return null;
            }
            Assert.That(flare, Is.GreaterThan(1f), "flare light at the peak");
            Assert.That(handOverHead, Is.GreaterThan(0.15f), "Knight: crossbow hand high over the head");
            Assert.That(stage.FlareLight.enabled, Is.False, "flare off after the taunt");

            // A swipe to the Ranger slides him in and does not taunt.
            page.Step(1);
            Assert.That(stage.Swapping, Is.True);
            Assert.That(stage.ActiveModels, Is.EqualTo(2), "old hero slides out while the new one slides in");
            yield return Wait(0.5f);
            Assert.That(stage.Swapping, Is.False);
            Assert.That(stage.ActiveModels, Is.EqualTo(1));
            Assert.That(stage.Shown.Id, Is.EqualTo("shadow_ranger"));
            Assert.That(stage.ShownPivot.localPosition.x, Is.EqualTo(0f).Within(1e-3f), "on the floor centre");
            Assert.That(stage.ShownLife.Motion, Is.Not.EqualTo(HeroStageMotion.Taunt), "no taunt on a swipe");
            Assert.That(stage.KeyLight.intensity, Is.LessThan(2f), "locked hero: dimmed key light");

            // Test Mode: the Ranger is free; picking him taunts and lights him fully.
            menu.SetTestMode(true);
            Assert.That(page.Press("shadow_ranger"), Is.EqualTo(HeroCardState.Select));
            yield return null;
            life = stage.ShownLife;
            Assert.That(life.Motion, Is.EqualTo(HeroStageMotion.Taunt), "a pick taunts");
            handOverHead = -1f;
            while (life.Motion == HeroStageMotion.Taunt)
            {
                handOverHead = Mathf.Max(handOverHead, life.WeaponHandBone.position.y - life.Head.position.y);
                yield return null;
            }
            Assert.That(handOverHead, Is.GreaterThan(0.15f), "Ranger: bow hand high over the head");
            Assert.That(stage.KeyLight.intensity, Is.GreaterThan(3f), "unlocked: full light");
            menu.Back();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return UnloadMenu();
        }

        [UnityTest]
        public IEnumerator IdleVariants_EveryFiveToNineSeconds_MoveTheHead_DrillTurnsTheBody()
        {
            yield return LoadMenu();
            var menu = Menu();
            var stage = menu.HeroPage.Stage;
            menu.OpenHeroes();
            yield return Wait(HeroSelectScreen.OpenTauntDelay + 0.1f); // the open taunt has started (it would cut a variant)
            foreach (var id in new[] { "knight", "shadow_ranger" })
            {
                menu.HeroPage.View(id);
                yield return null;
                var life = stage.ShownLife;
                life.ResetToIdle();
                Assert.That(life.NextVariantIn, Is.InRange(5f, 9f), $"{id}: next variant in 5-9 s");
                Assert.That(life.CanDrill, Is.True, $"{id}: has its own weapon clips for the drill");

                // Look-around: the head turns well away from the idle pose.
                yield return Wait(0.2f);
                var idleHead = life.Head.forward;
                life.Play(HeroStageMotion.LookAround);
                float headTurn = 0f;
                for (float t = 0f; t < 2.6f; t += Time.deltaTime)
                {
                    headTurn = Mathf.Max(headTurn, Vector3.Angle(Flat(idleHead), Flat(life.Head.forward)));
                    yield return null;
                }
                Assert.That(headTurn, Is.GreaterThan(20f), $"{id}: head looks around");

                // Drill: the hero turns aside while he works the weapon, then back to idle.
                life.Play(HeroStageMotion.Drill);
                float bodyYaw = 0f;
                for (float t = 0f; t < 6f && (life.Motion != HeroStageMotion.Idle || t < 0.5f); t += Time.deltaTime)
                {
                    bodyYaw = Mathf.Max(bodyYaw, life.BodyYaw);
                    yield return null;
                }
                Assert.That(bodyYaw, Is.GreaterThan(25f), $"{id}: turns aside for the drill");
                Assert.That(life.Motion, Is.EqualTo(HeroStageMotion.Idle), $"{id}: back to idle after the drill");
                Assert.That(life.BodyYaw, Is.EqualTo(0f));

                // The variant timer starts one by itself.
                life.Tick(9.1f);
                Assert.That(life.Motion == HeroStageMotion.LookAround || life.Motion == HeroStageMotion.Drill, Is.True, $"{id}: a variant after the gap");
                life.ResetToIdle();
            }
            menu.Back();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return UnloadMenu();
        }

        [UnityTest]
        public IEnumerator Drag_TurnsTheHero_ThenHeEasesBackToTheFront()
        {
            yield return LoadMenu();
            var menu = Menu();
            var stage = menu.HeroPage.Stage;
            menu.OpenHeroes();
            yield return Wait(0.1f);
            stage.ShownLife.ResetToIdle();
            stage.BeginDrag();
            stage.Drag(150f);
            for (int i = 0; i < 6; i++) yield return null; // the finger rests before it lifts: no flick
            stage.EndDrag();
            Assert.That(stage.Spin.Velocity, Is.EqualTo(0f));
            Assert.That(stage.Spin.Yaw, Is.EqualTo(-60f).Within(0.5f));
            yield return null;
            Assert.That(Mathf.DeltaAngle(stage.ShownPivot.localEulerAngles.y, 120f), Is.EqualTo(0f).Within(2f), "model faces the camera (180) turned by -60");
            yield return Wait(2.8f);
            Assert.That(stage.Spin.Yaw, Is.EqualTo(0f).Within(0.5f), "back to the front");
            menu.Back();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return UnloadMenu();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
