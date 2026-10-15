using System.Globalization;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Player health on the HUD document: top bar (fill eases to the value, a pale trail drains behind it after a
    /// short delay), a small bar over the Knight, a red screen-edge vignette on every hurt pulse (plus a slow
    /// heartbeat at low health), and the "YOU DIED" screen with the run counters (souls, kills, Chamber; KAN-51), the
    /// banked souls + wallet line (KAN-96) and Retry / Menu once <see cref="PlayerDeath.Finished"/>.
    /// Nothing here blocks touches except the death screen buttons. Element names are constants below; keep them in
    /// sync with GameHud.uxml. Looks live in GameHud.uss (hud-hp*, hud-overhead*, hud-vignette, death*).
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate (overhead bar follows the final camera)
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerHealthHud : MonoBehaviour
    {
        private const string FillName = "player-hp-fill";
        private const string TrailName = "player-hp-trail";
        private const string ValueName = "player-hp-value";
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

        [Header("Bar")]
        [Tooltip("How fast the fill eases to the real value (1/s, exponential).")]
        [SerializeField, Min(1f)] private float fillSharpness = 14f;
        [Tooltip("Seconds after the last damage before the trail starts to drain.")]
        [SerializeField, Min(0f)] private float trailDelay = 0.45f;
        [Tooltip("Trail drain speed (fraction of the bar per second).")]
        [SerializeField, Min(0.05f)] private float trailSpeed = 0.7f;
        [Tooltip("Overhead bar height above the player's feet (m).")]
        [SerializeField, Min(0f)] private float overheadHeight = 2.2f;

        [Header("Vignette")]
        [SerializeField] private Color vignetteColor = new Color(0.62f, 0.02f, 0.02f, 1f);
        [SerializeField, Range(0f, 1f)] private float hurtOpacity = 0.85f;
        [Tooltip("Hurt flash fade speed (opacity per second).")]
        [SerializeField, Min(0.1f)] private float hurtFade = 2.6f;
        [Tooltip("Below this health fraction a steady heartbeat vignette shows.")]
        [SerializeField, Range(0f, 1f)] private float lowHealth = 0.3f;
        [SerializeField, Range(0f, 1f)] private float lowHealthOpacity = 0.55f;
        [SerializeField, Min(0.1f)] private float heartbeatHz = 1.2f;
        [Tooltip("Vignette texture: inner radius where the tint starts, 0 = centre, 1 = edge midpoints.")]
        [SerializeField, Range(0f, 1f)] private float vignetteInner = 0.55f;

        private const int VignetteWidth = 64, VignetteHeight = 128; // portrait; stretched to the screen

        private UIDocument _document;
        private VisualElement _fill, _trail, _overhead, _overheadFill, _vignette, _deathScreen;
        private Label _value;
        private Texture2D _vignetteTexture;
        private string[] _numbers; // "0".."max": no string allocations while health drains every frame
        private float _shown = 1f, _trailShown = 1f;
        private float _drawnShown = -1f, _drawnTrail = -1f;
        private float _lastDamage = float.NegativeInfinity;
        private float _hurt;
        private int _lastNumber = -1;
        private bool _leaving;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _fill = root.Q(FillName);
            _trail = root.Q(TrailName);
            _value = root.Q<Label>(ValueName);
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
                _shown = _trailShown = health.Normalized;
            }
            if (death != null) death.Finished += ShowDeathScreen;
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDamaged -= OnDamaged;
                health.OnHurt -= OnHurt;
                health.OnDeath -= OnDeath;
            }
            if (death != null) death.Finished -= ShowDeathScreen;
        }

        private void OnDestroy()
        {
            if (_vignetteTexture != null) Destroy(_vignetteTexture);
        }

        private void OnDamaged(float amount) => _lastDamage = Time.time;
        private void OnHurt() => _hurt = 1f;
        private void OnDeath() => _overhead.AddToClassList(HiddenClass);

        private void LateUpdate()
        {
            if (health == null || _fill?.panel == null) return;
            float dt = Time.deltaTime;
            float target = health.Normalized;

            _shown += (target - _shown) * (1f - Mathf.Exp(-fillSharpness * dt));
            if (Mathf.Abs(target - _shown) < 0.0005f) _shown = target;
            if (_trailShown < _shown) _trailShown = _shown; // healing: trail jumps up with the fill
            else if (Time.time - _lastDamage > trailDelay) _trailShown = Mathf.MoveTowards(_trailShown, _shown, trailSpeed * dt);

            if (_shown != _drawnShown || _trailShown != _drawnTrail)
            {
                // Only touch styles when a value moved: an idle bar costs no layout work.
                _drawnShown = _shown;
                _drawnTrail = _trailShown;
                _fill.style.width = Length.Percent(_shown * 100f);
                _trail.style.width = Length.Percent(_trailShown * 100f);
                _overheadFill.style.width = Length.Percent(_shown * 100f);
            }
            UpdateNumber();
            UpdateVignette(dt, target);
            PlaceOverhead();
        }

        private void UpdateNumber()
        {
            int n = Mathf.CeilToInt(health.Current);
            if (n == _lastNumber) return;
            _lastNumber = n;
            if (_numbers == null || _numbers.Length <= Mathf.CeilToInt(health.Max)) // max health skills grow the cache
            {
                _numbers = new string[Mathf.CeilToInt(health.Max) + 1];
                for (int i = 0; i < _numbers.Length; i++) _numbers[i] = i.ToString();
            }
            _value.text = n >= 0 && n < _numbers.Length ? _numbers[n] : n.ToString();
        }

        private void UpdateVignette(float dt, float healthFraction)
        {
            _hurt = Mathf.Max(0f, _hurt - hurtFade * dt);
            float low = 0f;
            if (!health.IsDead && healthFraction < lowHealth && lowHealth > 0f)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * heartbeatHz * Mathf.PI * 2f);
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
