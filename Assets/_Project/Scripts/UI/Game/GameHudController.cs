using SurvivorSouls.Core;
using SurvivorSouls.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Game
{
    /// <summary>
    /// Binds GameHud.uxml and runs the run's start/end: health bar, kills, time, joystick ring,
    /// damage flash, and the YOU DIED overlay. Element names are constants below; keep them in sync with the UXML.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameHudController : MonoBehaviour
    {
        private const string SafeRootName = "safe-root";
        private const string FlashName = "damage-flash";
        private const string StickBaseName = "joystick-base";
        private const string StickKnobName = "joystick-knob";
        private const string HpFillName = "hp-fill";
        private const string HpLabelName = "lbl-hp";
        private const string KillsName = "lbl-kills";
        private const string TimeName = "lbl-time";
        private const string HintName = "lbl-hint";
        private const string DeathName = "death-overlay";
        private const string DeathStatsName = "lbl-death-stats";
        private const string RetryName = "btn-retry";
        private const string MenuName = "btn-menu";
        private const string HiddenClass = "ss-hidden";

        [SerializeField] private PlayerController player;
        [SerializeField] private VirtualJoystick joystick;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField, Min(0f)] private float deathOverlayDelay = 1.2f;

        private UIDocument _document;
        private SafeAreaPadding _safeArea;
        private VisualElement _flash, _stickBase, _stickKnob, _hpFill, _death;
        private Label _hp, _kills, _time, _hint, _deathStats;
        private float _flashAmount;
        private float _elapsed;
        private int _killCount;
        private bool _ended;

        public void Configure(PlayerController hero, VirtualJoystick input, EnemySpawner enemySpawner)
        {
            player = hero;
            joystick = input;
            spawner = enemySpawner;
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
            _flash = root.Q(FlashName);
            _stickBase = root.Q(StickBaseName);
            _stickKnob = root.Q(StickKnobName);
            _hpFill = root.Q(HpFillName);
            _hp = root.Q<Label>(HpLabelName);
            _kills = root.Q<Label>(KillsName);
            _time = root.Q<Label>(TimeName);
            _hint = root.Q<Label>(HintName);
            _death = root.Q(DeathName);
            _deathStats = root.Q<Label>(DeathStatsName);
            root.Q<Button>(RetryName).clicked += Retry;
            root.Q<Button>(MenuName).clicked += BackToMenu;

            player.Health.Damaged += OnPlayerDamaged;
            player.Health.Died += OnPlayerDied;
            ZombieEnemy.AnyDied += OnZombieDied;
        }

        // Health fills itself in Awake, which may run after this OnEnable.
        private void Start() => RefreshHealth();

        private void OnDisable()
        {
            if (player != null)
            {
                player.Health.Damaged -= OnPlayerDamaged;
                player.Health.Died -= OnPlayerDied;
            }
            ZombieEnemy.AnyDied -= OnZombieDied;
        }

        private void Update()
        {
            _safeArea?.Refresh();

            if (!_ended)
            {
                _elapsed += Time.deltaTime;
                int s = Mathf.FloorToInt(_elapsed);
                _time.text = $"{s / 60}:{s % 60:00}";
                if (_elapsed > 4f || player.IsMoving) _hint.style.opacity = 0f;
            }

            _flashAmount = Mathf.MoveTowards(_flashAmount, 0f, Time.deltaTime * 1.6f);
            _flash.style.opacity = _flashAmount;

            UpdateJoystick();
        }

        private void UpdateJoystick()
        {
            bool show = joystick.enabled && joystick.IsHeld && _stickBase.panel != null;
            _stickBase.EnableInClassList(HiddenClass, !show);
            if (!show) return;

            var panel = _stickBase.panel;
            // Input System is bottom-left origin; panel conversion expects top-left.
            Vector2 origin = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(joystick.OriginScreen.x, Screen.height - joystick.OriginScreen.y));
            Vector2 knob = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(joystick.KnobScreen.x, Screen.height - joystick.KnobScreen.y));
            float scale = panel.visualTree.layout.width / Mathf.Max(1, Screen.width);
            float size = joystick.RadiusPixels * scale * 2f;

            _stickBase.style.width = size;
            _stickBase.style.height = size;
            _stickBase.style.left = origin.x - size * 0.5f;
            _stickBase.style.top = origin.y - size * 0.5f;
            _stickKnob.style.translate = new Translate(knob.x - origin.x, knob.y - origin.y);
        }

        private void RefreshHealth()
        {
            var health = player.Health;
            _hpFill.style.width = Length.Percent(health.Fraction * 100f);
            _hp.text = health.Current.ToString();
        }

        private void OnPlayerDamaged(int amount)
        {
            RefreshHealth();
            _flashAmount = 0.35f;
        }

        private void OnZombieDied(ZombieEnemy zombie)
        {
            if (_ended) return;
            _killCount++;
            _kills.text = $"{_killCount} slain";
        }

        private void OnPlayerDied()
        {
            _ended = true;
            joystick.enabled = false;
            spawner.enabled = false;
            _flashAmount = 0.6f;
#if UNITY_IOS || UNITY_ANDROID
            if (GameSettings.Vibration) Handheld.Vibrate();
#endif

            int s = Mathf.FloorToInt(_elapsed);
            _deathStats.text = $"Slain {_killCount} hollows\nSurvived {s / 60}:{s % 60:00}";
            _death.schedule.Execute(() => _death.RemoveFromClassList(HiddenClass))
                .StartingIn((long)(deathOverlayDelay * 1000f));
        }

        private void Retry() => SceneManager.LoadScene(SceneNames.Game);

        private void BackToMenu()
        {
            if (SceneLoader.IsInBuild(SceneNames.MainMenu)) SceneLoader.LoadAsync(SceneNames.MainMenu);
        }
    }
}
