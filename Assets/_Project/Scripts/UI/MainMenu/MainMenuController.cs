using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Meta;
using SurvivorSouls.Gameplay.Realms;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>
    /// Binds MainMenu.uxml. Element names are constants below; keep them in sync with the UXML.
    /// No game logic here: Play delegates to SceneLoader, settings delegate to GameSettings.
    /// PLAY opens the Realm selection page (KAN-20, <see cref="RealmSelectScreen"/>); its START begins a run in that
    /// Realm. Without a catalog PLAY starts Realm 1 at once. SOUL TREE opens the soul tree page (KAN-98,
    /// <see cref="SoulTreeScreen"/>); without a tree asset the button is hidden. STORE opens the store page (KAN-99,
    /// <see cref="StoreScreen"/>, Ember Shard packs through <see cref="StoreProvider"/> + shard offers); without a catalog
    /// the button is hidden. Back / Esc closes the top layer (sheet, page, settings).
    /// Test Mode (KAN-144): a settings row (dev builds, or after 7 taps on the settings title: <see cref="TestModeReveal"/>)
    /// switches <see cref="TestMode"/>; a "TEST MODE" tag shows top left while it is on.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string SafeRootName = "safe-root";
        private const string PlayName = "btn-play";
        private const string SettingsName = "btn-settings";
        private const string QuitName = "btn-quit";
        public const string VersionName = "lbl-version";
        public const string SettingsVersionName = "lbl-settings-version";
        private const string ToastName = "lbl-toast";
        private const string OverlayName = "settings-overlay";
        private const string MusicName = "sld-music";
        private const string SfxName = "sld-sfx";
        private const string VibrationName = "tgl-vibration";
        private const string FpsName = "tgl-fps";
        private const string ReloadRingName = "tgl-reload-ring";
        public const string TestModeToggleName = "tgl-test-mode";
        public const string TestModeTagName = "lbl-test-mode";
        public const string SettingsTitleName = "lbl-settings-title";
        private const string CloseName = "btn-settings-close";
        private const string HiddenClass = "ss-hidden";
        private const string WalletName = "lbl-wallet-souls";
        private const string GemsName = "lbl-wallet-gems";
        private const string SoulTreeButtonName = "btn-soul-tree";
        private const string StoreButtonName = "btn-store";

        [SerializeField, Min(0.5f)] private float toastSeconds = 2f;
        [Tooltip("Realms on the selection page (Data/Realms/RealmCatalog.asset). Null = PLAY starts Realm 1.")]
        [SerializeField] private RealmCatalog realms;
        [Tooltip("Welcome bonus amount (KAN-97). Null = no welcome bonus.")]
        [SerializeField] private GemRewardConfig gems;
        [Tooltip("Soul tree page (Data/Economy/SoulTree.asset, KAN-98). Null = no SOUL TREE button.")]
        [SerializeField] private SoulTree soulTree;
        [Tooltip("Store page content (Data/Economy/StoreCatalog.asset, KAN-99). Null = no STORE button.")]
        [SerializeField] private StoreCatalog storeCatalog;

        private UIDocument _document;
        private SafeAreaPadding _safeArea;
        private SafeAreaPadding _realmSafeArea;
        private SafeAreaPadding _treeSafeArea;
        private SafeAreaPadding _storeSafeArea;
        private VisualElement _overlay;
        private Label _toast;
        private Label _wallet;
        private Label _gems;
        private Button _play;
        private IVisualElementScheduledItem _toastHide;
        private Toggle _testToggle;
        private Label _testTag;
        private TestModeReveal _testReveal;
        private RealmSelectScreen _realmSelect;
        private SoulTreeScreen _soulTreeScreen;
        private StoreScreen _storeScreen;
        private bool _loading;

        /// <summary>The Realm selection page (null until enabled or without a catalog).</summary>
        public RealmSelectScreen RealmSelect => _realmSelect;
        /// <summary>The soul tree page (null until enabled or without a tree asset).</summary>
        public SoulTreeScreen SoulTreePage => _soulTreeScreen;
        /// <summary>The store page (null until enabled or without a catalog).</summary>
        public StoreScreen StorePage => _storeScreen;
        /// <summary>
        /// Real-money provider of the store (KAN-99). Sandbox <see cref="MockPurchaseProvider"/> until StoreKit / Play
        /// Billing are wired. Tests set their own before the object is enabled.
        /// </summary>
        public IPurchaseProvider StoreProvider { get; set; }
        /// <summary>The settings overlay is showing.</summary>
        public bool SettingsOpen => _overlay != null && !_overlay.ClassListContains(HiddenClass);
        /// <summary>The Test Mode row shows in the settings (tests, screenshots).</summary>
        public bool TestModeRowVisible => _testToggle != null && !_testToggle.ClassListContains(HiddenClass);
        /// <summary>The "TEST MODE" corner tag shows (tests, screenshots).</summary>
        public bool TestModeTagVisible => _testTag != null && !_testTag.ClassListContains(HiddenClass);
        /// <summary>Build flag for the Test Mode row; tests set false to check the hidden reveal. Default: development build.</summary>
        public bool DevBuildForTestMode
        {
            get => _devBuildForTestMode ?? TestMode.RowVisibleByDefault;
            set
            {
                _devBuildForTestMode = value;
                if (_testReveal == null) return;
                _testReveal = new TestModeReveal(value);
                ShowTestMode();
            }
        }
        private bool? _devBuildForTestMode; // null = the build flag (read on enable, not in a field initializer)

        /// <summary>Wiring for tests and code-built menus (call before the object is enabled).</summary>
        public void Setup(RealmCatalog catalog, GemRewardConfig gemRewards = null, SoulTree tree = null, StoreCatalog store = null)
        {
            realms = catalog;
            gems = gemRewards;
            soulTree = tree;
            storeCatalog = store;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            GameSettings.Apply();
        }

        private void OnEnable()
        {
            var root = _document.rootVisualElement;

            _safeArea = new SafeAreaPadding(root.Q(SafeRootName));
            _overlay = root.Q(OverlayName);
            _toast = root.Q<Label>(ToastName);
            // Version from VERSION.json, stamped at build time (KAN-142); dev builds add the commit.
            string version = BuildInfo.DisplayLabel;
            root.Q<Label>(VersionName).text = version;
            var settingsVersion = root.Q<Label>(SettingsVersionName);
            if (settingsVersion != null) settingsVersion.text = version;
            _wallet = root.Q<Label>(WalletName);
            int recovered = SoulWallet.RestorePending(); // souls of a run the OS killed in the background (KAN-101)
            ShowWallet(SoulWallet.Balance);
            SoulWallet.Changed += ShowWallet;
            _gems = root.Q<Label>(GemsName);
            ShowGems(GemWallet.Balance);
            GemWallet.Changed += ShowGems;

            _play = root.Q<Button>(PlayName);
            _play.clicked += OnPlay;
            root.Q<Button>(SettingsName).clicked += OpenSettings;
            root.Q<Button>(CloseName).clicked += CloseSettings;

            var quit = root.Q<Button>(QuitName);
            // Apple rejects apps that quit themselves; hide the button on iOS.
            if (Application.platform == RuntimePlatform.IPhonePlayer)
                quit.AddToClassList(HiddenClass);
            else
                quit.clicked += OnQuit;

            BindSettings(root);
            BindTestMode(root);
            _realmSelect = realms != null && root.Q(RealmSelectScreen.ScreenName) != null
                ? new RealmSelectScreen(root, realms, StartRealm)
                : null;
            _soulTreeScreen = soulTree != null && root.Q(SoulTreeScreen.ScreenName) != null
                ? new SoulTreeScreen(root, soulTree)
                : null;
            var treeButton = root.Q<Button>(SoulTreeButtonName);
            if (treeButton != null)
            {
                if (_soulTreeScreen != null)
                {
                    treeButton.clicked += OpenSoulTree;
                    _soulTreeScreen.Bought += (_, _) => Haptics.Light();
                }
                else treeButton.AddToClassList(HiddenClass);
            }
            StoreProvider ??= new MockPurchaseProvider();
            _storeScreen = storeCatalog != null && root.Q(StoreScreen.ScreenName) != null
                ? new StoreScreen(root, new ShardStore(storeCatalog, StoreProvider))
                : null;
            var storeButton = root.Q<Button>(StoreButtonName);
            if (storeButton != null)
            {
                if (_storeScreen != null)
                {
                    storeButton.clicked += OpenStore;
                    _storeScreen.Bought += () => Haptics.Light();
                }
                else storeButton.AddToClassList(HiddenClass);
            }
            _play.Focus();
            if (recovered > 0) ShowToast(RecoveredText(recovered), 4f); // as long as the welcome toast: news, not a hint
        }

        /// <summary>Toast after the pending souls of an unfinished run were banked (KAN-101).</summary>
        public static string RecoveredText(int souls) => $"{PlayerHealthHud.FormatCount(souls)} souls recovered from your unfinished journey.";

        private void OnDisable()
        {
            SoulWallet.Changed -= ShowWallet;
            GemWallet.Changed -= ShowGems;
        }

        /// <summary>Souls held, top right (KAN-96).</summary>
        private void ShowWallet(int balance)
        {
            if (_wallet != null) _wallet.text = PlayerHealthHud.FormatCount(balance);
        }

        /// <summary>Ember Shards held, under the Souls pill (KAN-97).</summary>
        private void ShowGems(int balance)
        {
            if (_gems != null) _gems.text = PlayerHealthHud.FormatCount(balance);
        }

        private void Start()
        {
            // First menu ever (KAN-97): one-time welcome shards, said in the toast. The pill updates through Changed.
            if (gems != null && GemWallet.TryGrantBonus(GemBonus.Welcome, gems.WelcomeBonus))
                ShowToast($"Welcome, Ashen One. +{gems.WelcomeBonus} Ember Shards.", 4f);
            // Back from a cleared Realm (KAN-47): open on the Realm page, where the new unlock shows.
            if (RunState.TakeOpenRealms()) OpenRealms();
        }

        private void Update()
        {
            _safeArea?.Refresh();
            _realmSafeArea?.Refresh();
            _treeSafeArea?.Refresh();
            _storeSafeArea?.Refresh();
            // Esc on desktop; Android's back button arrives as Escape too.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Back();
        }

        /// <summary>Closes the top layer: Realm sheet / page, soul tree sheet / page, store sheet / page, then settings. False when nothing was open.</summary>
        public bool Back()
        {
            if (_realmSelect != null && _realmSelect.Back()) return true;
            if (_soulTreeScreen != null && _soulTreeScreen.Back()) return true;
            if (_storeScreen != null && _storeScreen.Back()) return true;
            if (_overlay != null && !_overlay.ClassListContains(HiddenClass))
            {
                CloseSettings();
                return true;
            }
            return false;
        }

        /// <summary>Opens the Realm selection page (PLAY). Dev screenshots call it too.</summary>
        public void OpenRealms()
        {
            if (_realmSelect == null) return;
            _realmSafeArea ??= new SafeAreaPadding(_realmSelect.Page);
            _realmSelect.Open();
            _realmSafeArea.Refresh(force: true);
        }

        /// <summary>Opens the soul tree page (SOUL TREE). Dev screenshots call it too.</summary>
        public void OpenSoulTree()
        {
            if (_soulTreeScreen == null) return;
            _treeSafeArea ??= new SafeAreaPadding(_soulTreeScreen.Page);
            _soulTreeScreen.Open();
            _treeSafeArea.Refresh(force: true);
        }

        /// <summary>Opens the store page (STORE). Dev screenshots call it too.</summary>
        public void OpenStore()
        {
            if (_storeScreen == null) return;
            _storeSafeArea ??= new SafeAreaPadding(_storeScreen.Page);
            _storeScreen.Open();
            _storeSafeArea.Refresh(force: true);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) GameSettings.Save();
        }

        private void OnPlay()
        {
            if (_loading) return;
            if (_realmSelect != null)
            {
                OpenRealms();
                return;
            }
            StartRun(1);
        }

        private void StartRealm(RealmDefinition realm) => StartRun(realm.Id, _realmSelect.StartChamber);

        /// <summary>New run in <paramref name="realm"/> at <paramref name="chamber"/> (above 1 only from the Test Mode stepper).</summary>
        private void StartRun(int realm, int chamber = RunState.FirstChamber)
        {
            if (_loading) return;
            if (!SceneLoader.IsInBuild(SceneNames.Game))
            {
                _realmSelect?.Close();
                ShowToast("The path ahead is not yet forged.");
                return;
            }

            _loading = true;
            _play.SetEnabled(false);
            RunState.NewRun(realm); // Chamber 1 of the chosen Realm, nothing carried (KAN-40)
            if (chamber > RunState.FirstChamber) RunState.JumpToChamber(chamber); // Test Mode start Chamber (KAN-144)
            SceneLoader.LoadAsync(SceneNames.Game);
        }

        private void OnQuit()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Opens the settings overlay (SETTINGS). Dev screenshots call it too.</summary>
        public void OpenSettings()
        {
            _overlay.RemoveFromClassList(HiddenClass);
            ShowTestMode();
        }

        /// <summary>One tap on the settings title: the 7th shows the Test Mode row in a non-development build.</summary>
        public void TapSettingsTitle()
        {
            if (_testReveal == null || !_testReveal.Tap()) return;
            ShowTestMode();
            ShowToast("Test Mode row revealed.");
        }

        /// <summary>Switches Test Mode (the settings toggle does the same). Switch-on tells the one-time Souls grant.</summary>
        public void SetTestMode(bool on)
        {
            if (TestMode.Locked) return; // store build (KAN-151)
            int added = TestMode.SetOn(on);
            _testToggle?.SetValueWithoutNotify(on);
            ShowTestMode();
            _realmSelect?.Refresh();
            if (on) ShowToast(added > 0 ? $"Test Mode on. +{PlayerHealthHud.FormatCount(added)} Souls." : "Test Mode on. All Realms open.", 3f);
            else ShowToast("Test Mode off. Real progress restored.", 3f);
        }

        private void BindTestMode(VisualElement root)
        {
            _testReveal = new TestModeReveal(DevBuildForTestMode);
            _testTag = root.Q<Label>(TestModeTagName);
            _testToggle = root.Q<Toggle>(TestModeToggleName);
            if (TestMode.Locked)
            {
                // Store build (KAN-151): the row stays hidden and neither the toggle nor the title taps are bound.
                ShowTestMode();
                return;
            }
            if (_testToggle != null)
            {
                _testToggle.SetValueWithoutNotify(TestMode.IsOn);
                _testToggle.RegisterValueChangedCallback(e => SetTestMode(e.newValue));
            }
            root.Q<Label>(SettingsTitleName)?.RegisterCallback<PointerDownEvent>(_ => TapSettingsTitle());
            ShowTestMode();
        }

        /// <summary>Row visibility and the corner tag from the current Test Mode state.</summary>
        private void ShowTestMode()
        {
            _testToggle?.EnableInClassList(HiddenClass, _testReveal == null || !_testReveal.Visible);
            _testTag?.EnableInClassList(HiddenClass, !TestMode.IsOn);
        }

        private void CloseSettings()
        {
            _overlay.AddToClassList(HiddenClass);
            GameSettings.Save();
        }

        private static void BindSettings(VisualElement root)
        {
            var music = root.Q<Slider>(MusicName);
            music.SetValueWithoutNotify(GameSettings.MusicVolume);
            music.RegisterValueChangedCallback(e => GameSettings.MusicVolume = e.newValue);

            var sfx = root.Q<Slider>(SfxName);
            sfx.SetValueWithoutNotify(GameSettings.SfxVolume);
            sfx.RegisterValueChangedCallback(e => GameSettings.SfxVolume = e.newValue);

            var vibration = root.Q<Toggle>(VibrationName);
            vibration.SetValueWithoutNotify(GameSettings.Vibration);
            vibration.RegisterValueChangedCallback(e =>
            {
                GameSettings.Vibration = e.newValue;
                if (e.newValue) Haptics.Light(); // preview tap: the player feels what they turned on
            });

            var fps = root.Q<Toggle>(FpsName);
            fps.SetValueWithoutNotify(GameSettings.HighFrameRate);
            fps.RegisterValueChangedCallback(e => GameSettings.HighFrameRate = e.newValue);

            var reloadRing = root.Q<Toggle>(ReloadRingName);
            reloadRing.SetValueWithoutNotify(GameSettings.ReloadRing);
            reloadRing.RegisterValueChangedCallback(e => GameSettings.ReloadRing = e.newValue);
        }

        private void ShowToast(string message, float seconds = 0f)
        {
            _toast.text = message;
            _toast.RemoveFromClassList(HiddenClass);
            _toastHide?.Pause();
            _toastHide = _toast.schedule
                .Execute(() => _toast.AddToClassList(HiddenClass))
                .StartingIn((long)((seconds > 0f ? seconds : toastSeconds) * 1000f));
        }
    }
}
