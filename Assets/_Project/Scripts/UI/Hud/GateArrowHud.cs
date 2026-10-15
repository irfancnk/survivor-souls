using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Gate arrow on the screen edge (KAN-63, absorbs KAN-33; KAN-176 edge fix): a forged-gold arrow (Blender model baked
    /// to one sprite, <c>Tools/Blender/build_gate_arrow.py</c>) that glides along the real screen edge and points from the
    /// Knight to the gate (<see cref="ObjectiveArrow"/>). Dim while the portal is locked, bright gold with a pulse and two
    /// trailing chevrons once it is open. Hides (fades) while the gate itself is on screen, with hysteresis at the border
    /// (<see cref="EdgeMarker.OnScreen"/>). No distance text (KAN-176).
    /// KAN-176: the arrow's outer edge sits <c>edgeMillimetres</c> (2.5 mm) inside the safe area (notch / home bar) on the
    /// gate's side. Small no-go zones (XP bar, timer / boss bar, pause button, health bar, souls, owned skill icons, Test Mode
    /// tag, idle joystick) push it along the same edge (<see cref="EdgeMarker.AvoidZones"/>); only when a whole edge line is
    /// blocked (the full-width bars at the top) does it step inward, just past them. Zone rects are read on layout changes,
    /// not per frame. Never blocks touches (picking ignored). Animates on unscaled time. No allocation per frame.
    /// Names: keep in sync with GameHud.uxml; looks: gate-arrow* in GameHud.uss.
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate (the arrow follows the final camera)
    [RequireComponent(typeof(UIDocument))]
    public sealed class GateArrowHud : MonoBehaviour
    {
        private const string RootName = "gate-arrow";
        private const string IconName = "gate-arrow-icon";
        private const string TrailName = "gate-arrow-trail";
        private const string HiddenClass = "hud-hidden";
        private const string SkillRowName = "skill-row";
        /// <summary>Screen.dpi is 0 on some devices: use the iPhone 14 Pro Max density (KAN-176).</summary>
        public const float FallbackDpi = 460f;
        /// <summary>HUD elements the arrow keeps clear of (KAN-176). The skill row uses its icons, not its full-width box.</summary>
        private static readonly string[] ZoneNames = { "xp", "timer", "boss", "btn-pause", "player-hp", "run-souls", "lbl-test-mode" };
        private const int MaxZones = 32;

        [SerializeField] private ObjectiveArrow objective;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Texture2D arrowTexture;

        [Header("Layout (panel units, 1080 x 1920 reference)")]
        [SerializeField, Min(16f)] private float size = 110f;
        [Tooltip("Physical gap (mm) between the safe-area edge and the arrow's outer edge (KAN-176).")]
        [SerializeField, Range(0f, 10f)] private float edgeMillimetres = 2.5f;
        [Tooltip("Extra gap (panel units) kept around every HUD no-go zone.")]
        [SerializeField, Min(0f)] private float zoneGap = 12f;
        [Tooltip("The gate counts as on screen once it is this far inside the view (arrow hides).")]
        [SerializeField, Min(0f)] private float enterInset = 110f;
        [Tooltip("An on-screen gate counts as off screen again once it is less than this far inside the view.")]
        [SerializeField, Min(0f)] private float exitInset = 30f;

        [Header("Feel")]
        [Tooltip("How fast position and angle glide to the new edge point (1/s, exponential).")]
        [SerializeField, Min(1f)] private float sharpness = 10f;
        [SerializeField, Min(0.1f)] private float fadeSpeed = 5f;
        [SerializeField, Range(0f, 0.3f)] private float pulse = 0.1f;
        [SerializeField, Min(0.1f)] private float pulseHz = 1f;
        [SerializeField, Range(0f, 1f)] private float lockedOpacity = 0.75f;
        [SerializeField] private Color lockedTint = new Color(0.8f, 0.68f, 0.56f, 1f);
        [Tooltip("Opacity at and beyond farDistance (fades a little when the gate is very far).")]
        [SerializeField, Range(0f, 1f)] private float farOpacity = 0.72f;
        [SerializeField, Min(1f)] private float nearDistance = 40f;
        [SerializeField, Min(1f)] private float farDistance = 140f;

        private UIDocument _document;
        private JoystickView _joystick;
        private VisualElement _root, _icon, _skillRow;
        private readonly VisualElement[] _trail = new VisualElement[2];
        private readonly VisualElement[] _zoneElements = new VisualElement[7];
        private readonly Rect[] _zones = new Rect[MaxZones];
        private EventCallback<GeometryChangedEvent> _onGeometry;
        private int _hudZoneCount, _zoneCount, _skillCount = -1;
        private bool _zonesDirty = true;
        private Vector2 _pos;
        private float _angle, _alpha;
        private bool _onScreen, _placed, _drawnVisible = true, _drawnOpen = true;

        /// <summary>Tests and dev tools: the arrow is drawn this frame (fading in or shown).</summary>
        public bool Visible => _alpha > 0.01f;
        /// <summary>Tests: arrow centre in panel units.</summary>
        public Vector2 Position => _pos;
        /// <summary>Tests: arrow rotation (degrees clockwise from tip-up).</summary>
        public float Angle => _angle;
        /// <summary>Tests: the gate is inside the view, so the arrow hides.</summary>
        public bool GateOnScreen => _onScreen;
        /// <summary>Tests: the rectangle the arrow centre moves on (panel units): the safe area shrunk by the mm gap + half the arrow.</summary>
        public Rect EdgeRect { get; private set; }
        /// <summary>Tests: the mm gap in panel units this frame.</summary>
        public float EdgeInset { get; private set; }
        /// <summary>Tests: arrow width / height in panel units.</summary>
        public float Size => size;
        /// <summary>Tests: the no-go zones used this frame (padded by zoneGap), panel units.</summary>
        public Rect[] Zones => _zones;
        public int ZoneCount => _zoneCount;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _joystick = GetComponent<JoystickView>();
            _onGeometry = _ => _zonesDirty = true;
        }

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _root = root.Q(RootName);
            _icon = root.Q(IconName);
            _skillRow = root.Q(SkillRowName);
            for (int i = 0; i < ZoneNames.Length; i++)
            {
                _zoneElements[i] = root.Q(ZoneNames[i]);
                _zoneElements[i]?.RegisterCallback(_onGeometry);
            }
            _skillRow?.RegisterCallback(_onGeometry);
            root.RegisterCallback(_onGeometry); // panel resize / rotation
            _zonesDirty = true;
            for (int i = 0; i < _trail.Length; i++) _trail[i] = root.Q(TrailName + (i + 1));
            if (arrowTexture != null)
            {
                if (_icon != null) _icon.style.backgroundImage = arrowTexture;
                foreach (var t in _trail)
                    if (t != null) t.style.backgroundImage = arrowTexture;
            }
            if (_root != null)
            {
                _root.style.width = size;
                _root.style.height = size;
            }
            _placed = false;
            _alpha = 0f;
            SetVisible(false);
        }

        private void OnDisable()
        {
            for (int i = 0; i < _zoneElements.Length; i++) _zoneElements[i]?.UnregisterCallback(_onGeometry);
            _skillRow?.UnregisterCallback(_onGeometry);
            _document.rootVisualElement?.UnregisterCallback(_onGeometry);
        }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(ObjectiveArrow target, Camera cam, Texture2D texture)
        {
            objective = target;
            worldCamera = cam;
            arrowTexture = texture;
        }

        private void LateUpdate() => Tick(Time.unscaledDeltaTime);

        /// <summary>One frame of placement and animation (tests call it directly).</summary>
        public void Tick(float dt)
        {
            if (_root == null || _root.panel == null) return;
            var cam = worldCamera != null ? worldCamera : Camera.main;
            bool want = cam != null && objective != null && objective.HasTarget && Place(cam, dt);
            _alpha = Mathf.MoveTowards(_alpha, want ? 1f : 0f, fadeSpeed * dt);
            if (!want && _alpha <= 0f) _placed = false; // next show snaps to its edge point instead of gliding across
            bool visible = _alpha > 0.01f;
            SetVisible(visible);
            if (visible) Draw();
        }

        /// <summary>Edge point + angle for this frame. False when the gate is on screen (no arrow needed).</summary>
        private bool Place(Camera cam, float dt)
        {
            var panel = _root.panel;
            Vector2 panelSize = panel.visualTree.layout.size;
            if (panelSize.x <= 0f || panelSize.y <= 0f || Screen.width <= 0 || Screen.height <= 0) return false;

            Vector3 knight = objective.transform.position;
            Vector3 gate = objective.Target;
            var view = new Rect(Vector2.zero, panelSize);
            bool gateInFront = cam.WorldToViewportPoint(gate).z > 0f;
            Vector2 gatePanel = gateInFront ? RuntimePanelUtils.CameraTransformWorldToPanel(panel, gate, cam) : new Vector2(-1e5f, -1e5f);
            _onScreen = gateInFront && EdgeMarker.OnScreen(_onScreen, gatePanel, view, enterInset, exitInset);
            if (_onScreen) return false;

            // Screen direction of the ground line Knight -> gate: a straight ground line projects to a straight screen
            // line, so a point a few metres along it gives the exact direction, and it is never behind the camera.
            Vector3 flat = gate - knight;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return false;
            Vector2 knightPanel = RuntimePanelUtils.CameraTransformWorldToPanel(panel, knight, cam);
            Vector2 aheadPanel = RuntimePanelUtils.CameraTransformWorldToPanel(panel, knight + flat.normalized * 6f, cam);
            Vector2 dir = aheadPanel - knightPanel;
            if (dir.sqrMagnitude < 1e-6f) return false;
            dir.Normalize();

            EdgeRect = AllowedRect(panelSize);
            RefreshZones();
            Vector2 point = EdgeMarker.ProjectToEdge(knightPanel, dir, EdgeRect);
            point = EdgeMarker.AvoidZones(point, EdgeRect, size * 0.5f, _zones, _zoneCount);
            float angle = EdgeMarker.Angle(dir);
            if (!_placed)
            {
                _pos = point;
                _angle = angle;
                _placed = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-sharpness * dt);
                _pos = Vector2.Lerp(_pos, point, k);
                _angle = Mathf.LerpAngle(_angle, angle, k);
            }
            return true;
        }

        /// <summary>Safe area (notch, home bar) shrunk by the mm gap + half the arrow: the arrow's outer edge hugs the screen edge.</summary>
        private Rect AllowedRect(Vector2 panelSize)
        {
            Rect safe = Screen.safeArea; // pixels, origin bottom-left
            float sx = panelSize.x / Screen.width, sy = panelSize.y / Screen.height;
            float dpi = Screen.dpi > 1f ? Screen.dpi : FallbackDpi;
            EdgeInset = EdgeMarker.MillimetresToPanel(edgeMillimetres, dpi, sx);
            float inset = EdgeInset + size * 0.5f;
            float xMin = safe.xMin * sx + inset;
            float xMax = safe.xMax * sx - inset;
            float yMin = (Screen.height - safe.yMax) * sy + inset;
            float yMax = (Screen.height - safe.yMin) * sy - inset;
            if (xMax < xMin) xMin = xMax = panelSize.x * 0.5f;
            if (yMax < yMin) yMin = yMax = panelSize.y * 0.5f;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// HUD zones are re-read only after a layout change (GeometryChangedEvent, a new skill icon); the idle joystick slot
        /// is a struct copy every frame. No allocation.
        /// </summary>
        private void RefreshZones()
        {
            int skills = _skillRow != null ? _skillRow.childCount : 0;
            if (skills != _skillCount) { _skillCount = skills; _zonesDirty = true; }
            if (_zonesDirty)
            {
                _zonesDirty = false;
                _hudZoneCount = 0;
                for (int i = 0; i < ZoneNames.Length; i++) AddZone(_zoneElements[i]);
                for (int i = 0; i < skills && _hudZoneCount < MaxZones - 1; i++) AddZone(_skillRow[i]);
            }
            _zoneCount = _hudZoneCount;
            if (_joystick != null && _joystick.IdleBounds.width > 0f) _zones[_zoneCount++] = Pad(_joystick.IdleBounds);
        }

        private void AddZone(VisualElement e)
        {
            if (e == null || _hudZoneCount >= MaxZones - 1 || e.resolvedStyle.display == DisplayStyle.None) return;
            Rect r = e.worldBound;
            if (float.IsNaN(r.width) || float.IsNaN(r.height) || r.width < 1f || r.height < 1f) return;
            _zones[_hudZoneCount++] = Pad(r);
        }

        private Rect Pad(Rect r) => Rect.MinMaxRect(r.xMin - zoneGap, r.yMin - zoneGap, r.xMax + zoneGap, r.yMax + zoneGap);

        private void Draw()
        {
            bool open = objective != null && objective.Open;
            float distance = objective != null ? objective.Distance : 0f;
            float far = Mathf.Lerp(1f, farOpacity, Mathf.InverseLerp(nearDistance, farDistance, distance));
            float t = Time.unscaledTime;
            float beat = 0.5f + 0.5f * Mathf.Sin(t * pulseHz * Mathf.PI * 2f);
            float scale = open ? 1f + pulse * beat : 1f;

            float half = size * 0.5f;
            _root.style.translate = new Translate(_pos.x - half, _pos.y - half);
            _root.style.rotate = new Rotate(_angle);
            _root.style.scale = new Scale(new Vector3(scale, scale, 1f));
            _root.style.opacity = _alpha * (open ? 1f : lockedOpacity) * far;
            if (open != _drawnOpen)
            {
                _drawnOpen = open;
                if (_icon != null) _icon.style.unityBackgroundImageTintColor = open ? Color.white : lockedTint;
                foreach (var tr in _trail)
                    if (tr != null) tr.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (open)
            {
                // Chevrons flow toward the tip: the far one lights first, then the near one.
                for (int i = 0; i < _trail.Length; i++)
                {
                    if (_trail[i] == null) continue;
                    float phase = Mathf.Repeat(t * pulseHz * 1.2f - i * 0.35f, 1f);
                    _trail[i].style.opacity = Mathf.Sin(phase * Mathf.PI) * 0.95f;
                }
            }
        }

        private void SetVisible(bool visible)
        {
            if (visible == _drawnVisible) return;
            _drawnVisible = visible;
            _root?.EnableInClassList(HiddenClass, !visible);
        }
    }
}
