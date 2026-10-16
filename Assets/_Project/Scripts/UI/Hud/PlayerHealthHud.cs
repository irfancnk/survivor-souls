using System.Globalization;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Player health on the HUD document: the medieval top bar (<see cref="HealthBarView"/>, KAN-82: fill, ghost trail,
    /// heal glow, shield, shards, "-24" / "+12" pops, heart medallion, low-health heartbeat), a small bar over the Knight
    /// (shown ~2 s after a change and at low health), a red screen-edge vignette on every hurt pulse (plus a heartbeat at
    /// low health, faster when critical; softer with "Reduce screen effects"), and the "YOU DIED" screen with the run counters (souls, kills, Chamber; KAN-51), the
    /// banked souls + wallet line (KAN-96) and Retry / Menu once <see cref="PlayerDeath.Finished"/>.
    /// Nothing here blocks touches except the death screen buttons. Element names are constants below; keep them in
    /// sync with GameHud.uxml. Looks live in GameHud.uss (hud-hp*, hud-overhead*, hud-vignette, death*).
    /// Dev shots of the bar effects: <c>HealthBarShotArgs</c> (<c>-ss-hp-fx</c>).
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate (overhead bar follows the final camera)
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerHealthHud : MonoBehaviour
    {
        private const string OverheadName = "player-overhead";
        private const string OverheadFillName = "player-overhead-fill";
        private const string VignetteName = "damage-vignette";
        private const string DeathName = "death-screen";
        private const string RetryName = "btn-retry";
        private const string MenuName = "btn-menu";
        private const string HintName = "lbl-hint";
        private const string SoulsName = "death-souls";
        private const string KillsName = "death-kills";
        private const string ChamberName = "death-chamber";
        private const string WalletName = "death-wallet";
        private const string HintGoneClass = "hud-hint--gone"; // GameHud.uss, shared with GameHudController
        private const string HiddenClass = "hud-hidden";
        private const string DeathShownClass = "death--shown";

        [SerializeField] private PlayerHealth health;
        [SerializeField] private PlayerDeath death;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private RunCounters counters;

        [Header("Bar (KAN-82)")]
        [SerializeField] private HealthBarFeel bar = new HealthBarFeel();
        [Tooltip("How fast the overhead bar eases to the real value (1/s, exponential).")]
        [SerializeField, Min(1f)] private float fillSharpness = 14f;
        [Tooltip("Overhead bar height above the player's feet (m).")]
        [SerializeField, Min(0f)] private float overheadHeight = 2.2f;
        [Tooltip("Seconds the overhead bar stays after a health change (always shown at low health), then fade time.")]
        [SerializeField, Min(0f)] private float overheadHold = 2f;
        [SerializeField, Min(0.05f)] private float overheadFade = 0.4f;

        [Header("Vignette")]
        [SerializeField] private Color vignetteColor = new Color(0.62f, 0.02f, 0.02f, 1f);
        [SerializeField, Range(0f, 1f)] private float hurtOpacity = 0.85f;
        [Tooltip("Hurt flash fade speed (opacity per second).")]
        [SerializeField, Min(0.1f)] private float hurtFade = 2.6f;
        [Tooltip("Below this health fraction a steady heartbeat vignette shows.")]
        [SerializeField, Range(0f, 1f)] private float lowHealth = 0.3f;
        [SerializeField, Range(0f, 1f)] private float lowHealthOpacity = 0.55f;
        [SerializeField, Min(0.1f)] private float heartbeatHz = 1.2f;
        [Tooltip("Below this health fraction the heartbeat is faster and the vignette stronger (KAN-82).")]
        [SerializeField, Range(0f, 1f)] private float criticalHealth = 0.15f;
        [SerializeField, Min(0.1f)] private float criticalHeartbeatHz = 2f;
        [Tooltip("Hurt flash strength with \"Reduce screen effects\" on (share of the normal flash).")]
        [SerializeField, Range(0f, 1f)] private float reducedHurt = 0.4f;
        [Tooltip("Vignette texture: inner radius where the tint starts, 0 = centre, 1 = edge midpoints.")]
        [SerializeField, Range(0f, 1f)] private float vignetteInner = 0.55f;

        private const int VignetteWidth = 64, VignetteHeight = 128; // portrait; stretched to the screen

        private UIDocument _document;
        private VisualElement _overhead, _overheadFill, _vignette, _deathScreen;
        private Texture2D _vignetteTexture;
        private HealthBarView _bar;
        private float _shown = 1f, _drawnShown = -1f;
        private float _lastChange = float.NegativeInfinity, _drawnOverhead = -1f;
        private float _hurt, _beat;
        private bool _reduceEffects;
        private bool _leaving;

        /// <summary>The top bar (tests, dev shots).</summary>
        public HealthBarView Bar => _bar;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _bar = new HealthBarView(root, bar);
            _overhead = root.Q(OverheadName);
            _overheadFill = root.Q(OverheadFillName);
            _vignette = root.Q(VignetteName);
            _deathScreen = root.Q(DeathName);
            root.Q<Button>(RetryName).clicked += Retry;
            root.Q<Button>(MenuName).clicked += ToMenu;

            _vignetteTexture ??= BuildVignette(vignetteColor, vignetteInner);
            _vignette.style.backgroundImage = new StyleBackground(_vignetteTexture);

            if (health != null)
            {
                health.OnDamaged += OnDamaged;
                health.OnHurt += OnHurt;
                health.OnDeath += OnDeath;
                health.OnHealthChanged += OnHealthChanged;
                health.OnShieldChanged += OnShieldChanged;
                _shown = health.Normalized;
                _bar.Reset(health.Current, health.Max, health.BaseMax, health.Shield);
            }
            if (death != null) death.Finished += ShowDeathScreen;
            GameSettings.Changed += ReadSettings;
            ReadSettings();
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDamaged -= OnDamaged;
                health.OnHurt -= OnHurt;
                health.OnDeath -= OnDeath;
                health.OnHealthChanged -= OnHealthChanged;
                health.OnShieldChanged -= OnShieldChanged;
            }
            if (death != null) death.Finished -= ShowDeathScreen;
            GameSettings.Changed -= ReadSettings;
        }

        private void OnDestroy()
        {
            if (_vignetteTexture != null) Destroy(_vignetteTexture);
        }

        private void ReadSettings()
        {
            _reduceEffects = GameSettings.ReduceScreenEffects;
            if (_bar != null) _bar.ReduceEffects = _reduceEffects;
        }

        private void OnDamaged(float amount) => _bar?.Damaged(amount);
        private void OnShieldChanged(float shield) => _bar?.ShieldChanged(shield);
        private void OnHurt() => _hurt = _reduceEffects ? reducedHurt : 1f;

        private void OnHealthChanged(float current, float max)
        {
            _lastChange = Time.time;
            _bar?.Changed(current, max);
        }

        private void OnDeath()
        {
            _overhead.AddToClassList(HiddenClass);
            _bar?.Died();
        }

        private void LateUpdate()
        {
            if (health == null || _overhead?.panel == null) return;
            float dt = Time.deltaTime;
            float target = health.Normalized;
            _bar.Tick(dt);

            _shown += (target - _shown) * (1f - Mathf.Exp(-fillSharpness * dt));
            if (Mathf.Abs(target - _shown) < 0.0005f) _shown = target;
            if (_shown != _drawnShown)
            {
                // Only touch styles when a value moved: an idle bar costs no layout work.
                _drawnShown = _shown;
                _overheadFill.style.width = Length.Percent(_shown * 100f);
            }
            UpdateOverheadOpacity(target);
            UpdateVignette(dt, target);
            PlaceOverhead();
        }

        /// <summary>KAN-82: the overhead bar shows for <see cref="overheadHold"/> s after a change (always at low health),
        /// then fades out.</summary>
        private void UpdateOverheadOpacity(float healthFraction)
        {
            float since = Time.time - _lastChange;
            float opacity = healthFraction < lowHealth ? 1f : Mathf.Clamp01(1f - (since - overheadHold) / overheadFade);
            if (opacity == _drawnOverhead) return;
            _drawnOverhead = opacity;
            _overhead.style.opacity = opacity;
        }

        private void UpdateVignette(float dt, float healthFraction)
        {
            _hurt = Mathf.Max(0f, _hurt - hurtFade * dt);
            float low = 0f;
            if (!health.IsDead && healthFraction < lowHealth && lowHealth > 0f)
            {
                // KAN-82: faster below the critical share; steady (no beat) with "Reduce screen effects".
                _beat += dt * (healthFraction < criticalHealth ? criticalHeartbeatHz : heartbeatHz);
                float beat = _reduceEffects ? 0.5f : 0.5f + 0.5f * Mathf.Sin(_beat * Mathf.PI * 2f);
                // sqrt: clearly visible soon after crossing the threshold, strongest near 0.
                low = Mathf.Sqrt(1f - healthFraction / lowHealth) * lowHealthOpacity * Mathf.Lerp(0.6f, 1f, beat);
            }
            _vignette.style.opacity = Mathf.Max(_hurt * _hurt * hurtOpacity, low);
        }

        private void PlaceOverhead()
        {
            if (health.IsDead) return;
            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return;
            Vector3 world = health.transform.position + Vector3.up * overheadHeight;
            if (cam.WorldToViewportPoint(world).z <= 0f) return;
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(_overhead.panel, world, cam);
            _overhead.style.translate = new Translate(p.x, p.y);
        }

        private void ShowDeathScreen()
        {
            var root = _document.rootVisualElement;
            counters?.BankToWallet(); // already banked at the moment of death; this only covers a scene without that hook
            SetText(root, WalletName, WalletLine(counters != null ? counters.Souls : 0, SoulWallet.Balance,
                counters != null ? counters.BonusGems : 0));
            SetText(root, SoulsName, FormatCount(counters != null ? counters.Souls : 0));
            SetText(root, KillsName, FormatCount(counters != null ? counters.Kills : 0));
            SetText(root, ChamberName, ChamberTransitionHud.Roman(RunState.Chamber));
            root.Q(HintName)?.AddToClassList(HintGoneClass);
            _deathScreen.RemoveFromClassList(HiddenClass);
            // Next frame, so the opacity transition starts from 0 instead of snapping.
            _deathScreen.schedule.Execute(() => _deathScreen.AddToClassList(DeathShownClass)).ExecuteLater(30);
        }

        private static void SetText(VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = text;
        }

        /// <summary>Death / victory screen wallet line (KAN-96): "+1,240 BANKED · 12,480 SOULS HELD".</summary>
        public static string WalletLine(int banked, int balance) => $"+{FormatCount(banked)} BANKED · {FormatCount(balance)} SOULS HELD";

        /// <summary>
        /// Wallet line plus, when the run gave the first-run bonus (KAN-97), a second line "+25 EMBER SHARDS · FIRST RUN BONUS".
        /// </summary>
        public static string WalletLine(int banked, int balance, int bonusGems) => bonusGems > 0
            ? WalletLine(banked, balance) + $"\n+{FormatCount(bonusGems)} EMBER SHARDS · FIRST RUN BONUS"
            : WalletLine(banked, balance);

        /// <summary>Death / victory screen number: digits with thousands separators ("12,480"), never negative.</summary>
        public static string FormatCount(int n) => Mathf.Max(0, n).ToString("N0", CultureInfo.InvariantCulture);

        private void Retry()
        {
            if (_leaving) return;
            RunState.NewRun(RunState.Realm); // a retry is a new run: Chamber 1, nothing carried (KAN-40)
            Load(SceneNames.Game);
        }
        private void ToMenu() => Load(SceneNames.MainMenu);

        private void Load(string scene)
        {
            if (_leaving || !SceneLoader.IsInBuild(scene)) return;
            _leaving = true;
            SceneLoader.LoadAsync(scene);
        }

        /// <summary>Transparent centre, tint toward the edges (smooth falloff, strongest in the corners).</summary>
        private static Texture2D BuildVignette(Color color, float inner)
        {
            var tex = new Texture2D(VignetteWidth, VignetteHeight, TextureFormat.RGBA32, false)
            {
                name = "T_DamageVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[VignetteWidth * VignetteHeight];
            for (int y = 0; y < VignetteHeight; y++)
            for (int x = 0; x < VignetteWidth; x++)
            {
                float u = (x + 0.5f) / VignetteWidth * 2f - 1f;
                float v = (y + 0.5f) / VignetteHeight * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float t = Mathf.Clamp01((d - inner) / (1.25f - inner));
                float a = t * t * (3f - 2f * t); // smoothstep
                pixels[y * VignetteWidth + x] = new Color(color.r, color.g, color.b, a * color.a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
