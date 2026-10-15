using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Sealed-portal bubble (KAN-49): when the Knight walks up to a locked portal (<see cref="PortalGate.SealTouched"/>), a
    /// dark parchment plate with a gold border, a soft ember glow and a tail appears over the arch: an archaic line on top
    /// ("The gate is sealed.") and the live time left below ("Endure 07:32 more."), the same clock as the HUD timer
    /// (<see cref="SurvivalTimer.Remaining"/>, <see cref="TimerHud.Format"/>). Boss Chambers show a "slay the guardian"
    /// line instead (no clock). Lines and timing come from <see cref="PortalSealLines"/> (one asset, random line, never the
    /// same twice in a row). Fades in, holds ~2.5 s, floats up a little and fades out; hides at once when the portal starts
    /// to open. A light haptic tap on each touch.
    /// One pooled element tree built in code (no UXML / USS edits), projected from the world anchor over the arch to the
    /// panel each frame (UI Toolkit, no world-space canvas), clamped inside the safe area and below the XP / timer / boss
    /// bars. Picking ignored. Animates on unscaled time. No allocation per frame (texts change only once per second).
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate (the bubble follows the final camera)
    [RequireComponent(typeof(UIDocument))]
    public sealed class PortalSealBubbleHud : MonoBehaviour
    {
        public const string RootName = "portal-seal-bubble";
        /// <summary>HUD bars the bubble stays below.</summary>
        private static readonly string[] TopBarNames = { "xp", "timer", "boss" };

        private static readonly Color TextColor = new Color(0.95f, 0.87f, 0.64f, 1f);
        private static readonly Color TimeColor = new Color(1f, 0.92f, 0.62f, 1f);
        private static readonly Color OutlineColor = new Color(0.04f, 0.02f, 0.01f, 0.9f);
        private static readonly Color PlateColor = new Color(0.1f, 0.07f, 0.05f, 0.92f);
        private static readonly Color BorderColor = new Color(0.8f, 0.61f, 0.3f, 1f);
        private static readonly Color GlowColor = new Color(1f, 0.55f, 0.2f, 0.26f);

        [SerializeField] private SurvivalTimer timer;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private PortalSealLines lines;

        [Header("Layout (panel units, 1080 x 1920 reference)")]
        [SerializeField, Min(20f)] private float headerSize = 34f;
        [SerializeField, Min(20f)] private float timeSize = 44f;
        [SerializeField, Min(200f)] private float maxWidth = 820f;
        [Tooltip("Gap kept to the safe-area sides and below the top bars.")]
        [SerializeField, Min(0f)] private float margin = 24f;
        [Tooltip("How far the bubble floats up over its life.")]
        [SerializeField, Min(0f)] private float rise = 26f;

        private UIDocument _document;
        private VisualElement _root, _bubble, _timeRow;
        private Label _header, _prefix, _time, _suffix;
        private readonly VisualElement[] _topBars = new VisualElement[3];
        private PortalSealLines _fallback;
        private PortalGate _gate;
        private float _age = -1f, _life, _hideAt;
        private int _drawnSeconds = -1, _lastTimed = -1, _lastBoss = -1;
        private bool _timed;

        /// <summary>Tests and dev tools: the bubble is on screen (fading in, holding or fading out).</summary>
        public bool Visible => _age >= 0f;
        /// <summary>Tests: current opacity.</summary>
        public float Alpha { get; private set; }
        /// <summary>Tests: the line on top / the time text / the full lower row.</summary>
        public string Header => _header?.text;
        public string TimeText => _time?.text;
        public string LowerLine => _prefix == null ? null : _timed ? _prefix.text + _time.text + _suffix.text : _prefix.text;
        /// <summary>Tests: how many times the bubble was shown.</summary>
        public int ShowCount { get; private set; }
        /// <summary>Tests: the bubble's top-left in panel units this frame.</summary>
        public Vector2 PanelPosition { get; private set; }
        public VisualElement Bubble => _bubble;

        private PortalSealLines Lines => lines != null ? lines : _fallback ??= PortalSealLines.CreateDefault();

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            _root = _document.rootVisualElement;
            if (_root != null)
            {
                _bubble = _root.Q(RootName) ?? Build(_root);
                for (int i = 0; i < TopBarNames.Length; i++) _topBars[i] = _root.Q(TopBarNames[i]);
            }
            HideNow();
            PortalGate.SealTouched += OnSealTouched;
        }

        private void OnDisable()
        {
            PortalGate.SealTouched -= OnSealTouched;
            HideNow();
        }

        private void OnDestroy()
        {
            if (_fallback != null) Destroy(_fallback);
        }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(SurvivalTimer survivalTimer, Camera cam, PortalSealLines table)
        {
            timer = survivalTimer;
            worldCamera = cam;
            lines = table;
        }

        /// <summary>Plate (glow ring, gold border, a small gem on top), header, time row, tail. All looks set in code.</summary>
        private VisualElement Build(VisualElement root)
        {
            var bubble = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            bubble.style.position = Position.Absolute;
            bubble.style.left = 0f;
            bubble.style.top = 0f;
            bubble.style.alignItems = Align.Center;
            bubble.style.maxWidth = maxWidth;

            var glow = new VisualElement { name = RootName + "-glow", pickingMode = PickingMode.Ignore };
            SetBorder(glow, 8f, GlowColor, 34f);

            var plate = new VisualElement { name = RootName + "-plate", pickingMode = PickingMode.Ignore };
            plate.style.backgroundColor = PlateColor;
            SetBorder(plate, 3f, BorderColor, 24f);
            plate.style.paddingTop = 22f;
            plate.style.paddingBottom = 20f;
            plate.style.paddingLeft = 36f;
            plate.style.paddingRight = 36f;
            plate.style.alignItems = Align.Center;

            var gem = new VisualElement { name = RootName + "-gem", pickingMode = PickingMode.Ignore };
            gem.style.position = Position.Absolute;
            gem.style.width = 16f;
            gem.style.height = 16f;
            gem.style.top = -10f;
            gem.style.left = Length.Percent(50f);
            gem.style.marginLeft = -8f;
            gem.style.backgroundColor = BorderColor;
            gem.style.rotate = new Rotate(45f);
            plate.Add(gem);

            _header = NewLabel("header", headerSize, TextColor, FontStyle.Italic);
            _header.style.marginBottom = 4f;
            plate.Add(_header);

            _timeRow = new VisualElement { name = RootName + "-row", pickingMode = PickingMode.Ignore };
            _timeRow.style.flexDirection = FlexDirection.Row;
            _timeRow.style.alignItems = Align.FlexEnd;
            _timeRow.style.justifyContent = Justify.Center;
            _prefix = NewLabel("prefix", headerSize, TextColor, FontStyle.Normal);
            _time = NewLabel("time", timeSize, TimeColor, FontStyle.Bold);
            _suffix = NewLabel("suffix", headerSize, TextColor, FontStyle.Normal);
            _prefix.style.marginBottom = 3f;
            _suffix.style.marginBottom = 3f;
            _timeRow.Add(_prefix);
            _timeRow.Add(_time);
            _timeRow.Add(_suffix);
            plate.Add(_timeRow);

            glow.Add(plate);
            bubble.Add(glow);

            var tail = new VisualElement { name = RootName + "-tail", pickingMode = PickingMode.Ignore };
            tail.style.width = 28f;
            tail.style.height = 28f;
            tail.style.marginTop = -27f; // centre on the plate's bottom border: the upper half hides it, the tip points down
            tail.style.backgroundColor = PlateColor;
            tail.style.borderRightWidth = 3f;
            tail.style.borderBottomWidth = 3f;
            tail.style.borderRightColor = BorderColor;
            tail.style.borderBottomColor = BorderColor;
            tail.style.borderBottomRightRadius = 4f;
            tail.style.rotate = new Rotate(45f);
            bubble.Add(tail);

            root.Add(bubble);
            return bubble;
        }

        private static void SetBorder(VisualElement e, float width, Color color, float radius)
        {
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
            e.style.borderTopLeftRadius = radius;
            e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius;
            e.style.borderBottomRightRadius = radius;
        }

        private static Label NewLabel(string part, float size, Color color, FontStyle fontStyle)
        {
            var label = new Label { name = RootName + "-" + part, pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.unityTextAlign = TextAnchor.LowerCenter;
            label.style.unityTextOutlineColor = OutlineColor;
            label.style.unityTextOutlineWidth = 1.5f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            return label;
        }

        private void OnSealTouched(PortalGate gate)
        {
            if (_bubble == null || gate == null) return;
            var table = Lines;
            bool fading = _age >= 0f && _age > _hideAt;
            if (_age >= 0f && gate == _gate && !fading)
            {
                _age = Mathf.Min(_age, table.fadeIn); // touched again while shown: hold again, no new fade-in
            }
            else
            {
                _gate = gate;
                _timed = timer == null || timer.UsesTimer;
                ApplyLine(table);
                _age = _age >= 0f ? Mathf.Min(_age, table.fadeIn) : 0f;
            }
            _hideAt = table.visibleSeconds;
            _life = table.visibleSeconds + table.fadeOut;
            _bubble.style.display = DisplayStyle.Flex;
            ShowCount++;
            Haptics.Light();
        }

        private void ApplyLine(PortalSealLines table)
        {
            var set = _timed ? table.timed : table.boss;
            int count = set != null ? set.Length : 0;
            int last = _timed ? _lastTimed : _lastBoss;
            int i = PortalSealLines.Pick(count, last, Random.value);
            if (_timed) _lastTimed = i;
            else _lastBoss = i;
            var line = i >= 0 ? set[i] : new PortalSealLines.Line("The gate is sealed.", "", "");
            _header.text = line.header;
            _prefix.text = line.prefix;
            _suffix.text = _timed ? line.suffix : "";
            _prefix.style.display = string.IsNullOrEmpty(line.prefix) ? DisplayStyle.None : DisplayStyle.Flex;
            _suffix.style.display = !_timed || string.IsNullOrEmpty(line.suffix) ? DisplayStyle.None : DisplayStyle.Flex;
            _time.style.display = _timed ? DisplayStyle.Flex : DisplayStyle.None;
            _drawnSeconds = -1;
            UpdateTime();
        }

        private void LateUpdate() => Tick(Time.unscaledDeltaTime);

        /// <summary>One frame of the countdown text, fade and placement (tests call it directly).</summary>
        public void Tick(float dt)
        {
            if (_age < 0f || _bubble == null) return;
            var table = Lines;
            // The seal broke (portal opening) or the tile was pooled away: fade out now.
            if (_gate == null || !_gate.isActiveAndEnabled || _gate.State != PortalState.Locked)
                if (_age < _hideAt) _age = _hideAt;
            _age += dt;
            if (_age >= _life)
            {
                HideNow();
                return;
            }
            UpdateTime();

            float fadeIn = table.fadeIn > 0f ? Mathf.Clamp01(_age / table.fadeIn) : 1f;
            float fadeOut = _age > _hideAt && table.fadeOut > 0f ? 1f - Mathf.Clamp01((_age - _hideAt) / table.fadeOut) : 1f;
            float alpha = fadeIn * fadeOut;
            float t = Mathf.Clamp01(_age / _life);
            float lift = rise * (1f - (1f - t) * (1f - t)); // ease-out float-up
            float pop = Mathf.Lerp(0.92f, 1f, fadeIn);

            if (!Place(lift)) alpha = 0f; // no layout yet (first frame) or the anchor is behind the camera
            Alpha = alpha;
            _bubble.style.opacity = alpha;
            _bubble.style.scale = new Scale(new Vector2(pop, pop));
        }

        private void UpdateTime()
        {
            if (!_timed || timer == null) return;
            int seconds = Mathf.CeilToInt(timer.Remaining);
            if (seconds == _drawnSeconds) return;
            _drawnSeconds = seconds;
            _time.text = TimerHud.Format(seconds);
        }

        /// <summary>Tail tip on the anchor over the arch, clamped inside the safe area and below the top bars.</summary>
        private bool Place(float lift)
        {
            var panel = _bubble.panel;
            if (panel == null) return false;
            float w = _bubble.resolvedStyle.width, h = _bubble.resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w < 1f || h < 1f) return false;
            Vector2 panelSize = panel.visualTree.layout.size;
            if (panelSize.x <= 0f || panelSize.y <= 0f || Screen.width <= 0 || Screen.height <= 0) return false;
            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return false;
            Vector3 anchor = _gate.BubbleAnchor;
            if (cam.WorldToViewportPoint(anchor).z <= 0f) return false;
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, anchor, cam);

            Rect safe = Screen.safeArea; // pixels, origin bottom-left
            float sx = panelSize.x / Screen.width, sy = panelSize.y / Screen.height;
            float xMin = safe.xMin * sx + margin, xMax = safe.xMax * sx - margin;
            float yMin = (Screen.height - safe.yMax) * sy + margin, yMax = (Screen.height - safe.yMin) * sy - margin;
            for (int i = 0; i < _topBars.Length; i++)
            {
                var bar = _topBars[i];
                if (bar == null || bar.resolvedStyle.display == DisplayStyle.None) continue;
                Rect r = bar.worldBound;
                if (float.IsNaN(r.yMax) || r.height < 1f || r.yMax > panelSize.y * 0.5f) continue;
                yMin = Mathf.Max(yMin, r.yMax + margin);
            }

            float x = Mathf.Clamp(p.x - w * 0.5f, xMin, Mathf.Max(xMin, xMax - w));
            float y = Mathf.Clamp(p.y - h - lift, yMin, Mathf.Max(yMin, yMax - h));
            PanelPosition = new Vector2(x, y);
            _bubble.style.translate = new Translate(x, y);
            return true;
        }

        private void HideNow()
        {
            _age = -1f;
            Alpha = 0f;
            if (_bubble == null) return;
            _bubble.style.display = DisplayStyle.None;
            _bubble.style.opacity = 0f;
        }
    }
}
