using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-101 pending souls in play: the app going to the background mid-run (pause or focus loss) saves the run's
    /// unbanked souls as the pending amount, a later background save replaces it, a normal run end banks the run once and
    /// deletes the pending amount (no double count), a background save after the bank saves nothing, and the main menu on
    /// the real MainMenu.uxml banks a pending amount once at start with a "souls recovered" toast (a second start adds
    /// nothing). Real keys are kept by <see cref="WalletPrefsGuard"/>.
    /// </summary>
    public sealed class PendingSoulsTests
    {
        private const int TestRealm = 94;
        private readonly List<Object> _objects = new List<Object>();

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            RunState.NewRun(TestRealm);
            SoulWallet.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            SoulWallet.Clear();
            RunState.NewRun();
        }

        private RunCounters Counters(int souls)
        {
            var counters = Track(new GameObject("Chamber")).AddComponent<RunCounters>();
            counters.Restore(5, souls);
            return counters;
        }

        [Test]
        public void Pause_SavesUnbankedSouls_LaterSaveReplaces()
        {
            var counters = Counters(320);
            counters.gameObject.SendMessage("OnApplicationPause", true);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(320));
            Assert.That(SoulWallet.Balance, Is.EqualTo(0), "a background save is not a bank");

            counters.gameObject.SendMessage("OnApplicationPause", false);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(320), "resume keeps the save (a later crash still keeps the souls)");

            counters.AddSouls(80);
            counters.gameObject.SendMessage("OnApplicationFocus", false);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(400), "focus loss saves the newer total");
            counters.gameObject.SendMessage("OnApplicationFocus", true);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(400));
        }

        [Test]
        public void RunEnd_BanksOnce_DeletesPending_NoDoubleCount()
        {
            var counters = Counters(500);
            counters.SavePendingSouls();
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(500));

            counters.AddSouls(100);
            Assert.That(counters.BankToWallet(), Is.EqualTo(600));
            Assert.That(SoulWallet.Balance, Is.EqualTo(600));
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0), "the bank deletes the background save");
            Assert.That(SoulWallet.RestorePending(), Is.EqualTo(0), "next app start adds nothing");
            Assert.That(SoulWallet.Balance, Is.EqualTo(600));

            counters.gameObject.SendMessage("OnApplicationPause", true);
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0), "a banked run saves nothing (death screen in the background)");
        }

        [Test]
        public void KilledInBackground_NextStartBanksPending()
        {
            var counters = Counters(260);
            counters.gameObject.SendMessage("OnApplicationPause", true);
            // The OS kills the app: the run never ends, RunState and the scene are gone.
            Object.DestroyImmediate(counters.gameObject);
            RunState.NewRun();
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[SoulWallet\] Restored 260 pending souls"));
            Assert.That(SoulWallet.RestorePending(), Is.EqualTo(260));
            Assert.That(SoulWallet.Balance, Is.EqualTo(260));
        }

        [Test]
        public void ZeroSouls_SavesNothing()
        {
            var counters = Counters(0);
            counters.SavePendingSouls();
            Assert.That(PlayerPrefs.HasKey(SoulWallet.KeyPending), Is.False);
        }

#if UNITY_EDITOR
        private MainMenuController Menu(out VisualElement root)
        {
            var go = Track(new GameObject("Menu"));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            go.SetActive(false);
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(null);
            go.SetActive(true);
            root = doc.rootVisualElement;
            return menu;
        }

        [UnityTest]
        public IEnumerator MenuStart_BanksPendingOnce_ShowsToast()
        {
            SoulWallet.Earn(1000);
            SoulWallet.SavePending(240);
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[SoulWallet\] Restored 240 pending souls"));
            Menu(out var root);
            yield return null;
            Assert.That(SoulWallet.Balance, Is.EqualTo(1240));
            Assert.That(SoulWallet.PendingSouls, Is.EqualTo(0));
            Assert.That(root.Q<Label>("lbl-wallet-souls").text, Is.EqualTo("1,240"), "the pill shows the restored balance");
            var toast = root.Q<Label>("lbl-toast");
            Assert.That(toast.text, Is.EqualTo(MainMenuController.RecoveredText(240)));
            Assert.That(toast.ClassListContains("ss-hidden"), Is.False, "toast visible");
            Assert.That(MainMenuController.RecoveredText(1240), Is.EqualTo("1,240 souls recovered from your unfinished journey."));

            var first = _objects[_objects.Count - 1];
            _objects.Remove(first);
            Object.DestroyImmediate(first);
            Menu(out var again);
            yield return null;
            Assert.That(SoulWallet.Balance, Is.EqualTo(1240), "a second start adds nothing");
            Assert.That(again.Q<Label>("lbl-toast").text, Is.Not.EqualTo(MainMenuController.RecoveredText(240)));
        }
#endif
    }
}
