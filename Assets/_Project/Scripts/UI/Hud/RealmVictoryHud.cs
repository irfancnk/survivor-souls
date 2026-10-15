using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Realms;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// End of Realm victory screen (KAN-47). Opens on <see cref="ChamberTransition.RealmCleared"/> (the Knight left the
    /// last Chamber through the portal; the game is paused and the Realm is already saved as completed):
    /// "REALM CLEARED" band in gold with the Realm name, the run counters (souls, kills, level) counting up from 0, the
    /// unlock line for the next Realm ("REALM II UNLOCKED", "COMING SOON" or "ALL REALMS CLEARED"), the banked souls +
    /// wallet line (KAN-96), and CONTINUE, which
    /// goes back to the main menu on the Realm page (<see cref="RunState.TakeOpenRealms"/>).
    /// Everything runs on unscaled time (the game is paused). Element names are constants below; keep them in sync with
    /// GameHud.uxml. Looks: victory* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RealmVictoryHud : MonoBehaviour
    {
        public const string ScreenName = "victory-screen";
        private const string RealmName = "victory-realm";
        private const string SoulsName = "victory-souls";
        private const string KillsName = "victory-kills";
        private const string LevelName = "victory-level";
        private const string UnlockName = "victory-unlock";
        private const string WalletName = "victory-wallet";
        private const string ContinueName = "btn-victory-continue";
        private const string HintName = "lbl-hint";
        private const string HintGoneClass = "hud-hint--gone"; // GameHud.uss, shared with GameHudController
        private const string HiddenClass = "hud-hidden";
        private const string ShownClass = "victory--shown";
        private const string UnlockNewClass = "victory__unlock--new";

        [SerializeField] private ChamberTransition transition;
        [Tooltip("Run souls banked into the wallet when the screen opens (KAN-96). Null = no wallet line.")]
        [SerializeField] private RunCounters counters;
        [Tooltip("Realm names and the next Realm (Data/Realms/RealmCatalog.asset). Null = numbers only.")]
        [SerializeField] private RealmCatalog realms;
        [Tooltip("Seconds before the counters start (the screen fades in first).")]
        [SerializeField, Min(0f)] private float countDelay = 0.6f;
        [SerializeField, Min(0.05f)] private float countTime = 1.4f;

        private UIDocument _document;
        private VisualElement _screen;
        private Label _souls, _kills, _level;
        private RunSnapshot _run;
        private float _age = -1f;
        private int _shownSouls = -1, _shownKills = -1, _shownLevel = -1;
        private bool _leaving;

        public bool IsShown => _run != null;
        /// <summary>Texts as last drawn (tests and dev tools).</summary>
        public string RealmText { get; private set; }
        public string UnlockText { get; private set; }
        public string WalletText { get; private set; }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(ChamberTransition chamberTransition, RealmCatalog catalog, RunCounters runCounters = null)
        {
            if (isActiveAndEnabled && transition != null) transition.RealmCleared -= Show;
            transition = chamberTransition;
            realms = catalog;
            counters = runCounters;
            if (isActiveAndEnabled && transition != null) transition.RealmCleared += Show;
        }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _screen = root.Q(ScreenName);
            _souls = root.Q<Label>(SoulsName);
            _kills = root.Q<Label>(KillsName);
            _level = root.Q<Label>(LevelName);
            var button = root.Q<Button>(ContinueName);
            if (button != null) button.clicked += Continue;
            if (transition != null) transition.RealmCleared += Show;
        }

        private void OnDisable()
        {
            if (transition != null) transition.RealmCleared -= Show;
        }

        /// <summary>Opens the screen for <paramref name="run"/> (public for dev screenshots and tests).</summary>
        public void Show(RunSnapshot run)
        {
            if (run == null || _run != null) return;
            _run = run;
            _age = 0f;
            int realm = RunState.Realm;
            RealmText = RealmTitle(realms, realm);
            UnlockText = UnlockLine(realms, realm, out bool unlocked);
            if (counters != null)
            {
                counters.BankToWallet(); // the counters bank on RealmCleared too; whichever handler runs first banks once
                WalletText = PlayerHealthHud.WalletLine(run.Souls, SoulWallet.Balance, counters.BonusGems);
            }
            if (_screen == null) return;
            var root = _document.rootVisualElement;
            SetText(root, RealmName, RealmText);
            var wallet = root.Q<Label>(WalletName);
            if (wallet != null)
            {
                wallet.text = WalletText ?? "";
                wallet.EnableInClassList(HiddenClass, WalletText == null);
            }
            var unlock = root.Q<Label>(UnlockName);
            if (unlock != null)
            {
                unlock.text = UnlockText;
                unlock.EnableInClassList(UnlockNewClass, unlocked);
            }
            DrawCounts(0f);
            root.Q(HintName)?.AddToClassList(HintGoneClass);
            _screen.RemoveFromClassList(HiddenClass);
            // Next frame, so the opacity transition starts from 0 instead of snapping.
            _screen.schedule.Execute(() => _screen.AddToClassList(ShownClass)).ExecuteLater(30);
        }

        private void Update()
        {
            if (_age < 0f || _run == null) return;
            _age += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float t = Mathf.Clamp01((_age - countDelay) / countTime);
            DrawCounts(1f - (1f - t) * (1f - t) * (1f - t)); // ease-out cubic: fast start, settles on the value
            if (t >= 1f) _age = -1f;
        }

        private void DrawCounts(float k)
        {
            // Strings only when a shown number changes: a paused screen, a few dozen strings over 1.4 s.
            Draw(_souls, Mathf.RoundToInt(_run.Souls * k), ref _shownSouls);
            Draw(_kills, Mathf.RoundToInt(_run.Kills * k), ref _shownKills);
            Draw(_level, Mathf.Max(1, Mathf.RoundToInt(_run.Level * k)), ref _shownLevel);
        }

        private static void Draw(Label label, int value, ref int shown)
        {
            if (value == shown) return;
            shown = value;
            if (label != null) label.text = PlayerHealthHud.FormatCount(value);
        }

        private void Continue()
        {
            if (_leaving || !SceneLoader.IsInBuild(SceneNames.MainMenu)) return;
            _leaving = true;
            SceneLoader.LoadAsync(SceneNames.MainMenu); // RunState.EndRealm already asked the menu for the Realm page
        }

        private static void SetText(VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = text;
        }

        /// <summary>"REALM I · FROSTBITTEN SHORE" (or "REALM I" without a catalog entry).</summary>
        public static string RealmTitle(RealmCatalog catalog, int realm)
        {
            var def = catalog != null ? catalog.Get(realm) : null;
            string head = "REALM " + ChamberTransitionHud.Roman(realm);
            return def != null && !string.IsNullOrEmpty(def.DisplayName) ? head + " · " + def.DisplayName.ToUpperInvariant() : head;
        }

        /// <summary>
        /// The line under the counters: the next Realm opened ("REALM II UNLOCKED · UNDEAD BURG", <paramref name="unlocked"/>
        /// true), the next Realm exists but has no content yet ("REALM II · UNDEAD BURG · COMING SOON"), or this was the last
        /// one ("ALL REALMS CLEARED"). Without a catalog: "REALM N UNLOCKED".
        /// </summary>
        public static string UnlockLine(RealmCatalog catalog, int realm, out bool unlocked)
        {
            int next = realm + 1;
            string head = "REALM " + ChamberTransitionHud.Roman(next);
            if (catalog == null)
            {
                unlocked = true;
                return head + " UNLOCKED";
            }
            var def = catalog.Get(next);
            if (def == null)
            {
                unlocked = false;
                return "ALL REALMS CLEARED";
            }
            string name = string.IsNullOrEmpty(def.DisplayName) ? "" : " · " + def.DisplayName.ToUpperInvariant();
            unlocked = def.Playable;
            return def.Playable ? head + " UNLOCKED" + name : head + name + " · COMING SOON";
        }
    }
}
