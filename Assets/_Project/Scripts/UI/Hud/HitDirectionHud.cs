using SurvivorSouls.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Hit direction hint (KAN-187): a short crimson arc at the screen edge on the side a discrete hit came from
    /// (<see cref="PlayerHealth.OnHit"/>: ranged shots, boss attacks, telegraphed specials; never horde contact). The arc sits
    /// on an ellipse just inside the screen edge, turned so its bright outer rim faces the edge, pops in and fades out on
    /// scaled time, like the damage vignette (it holds through a hit-stop or a pause and finishes after). Pooled: <see cref="PoolSize"/> arcs built once next to the
    /// damage vignette; a new hit reuses a running arc on the same side, else the oldest. The arc texture is procedural
    /// (built once). Shown also with "Reduce screen effects": it is information, not shake.
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate, like PlayerHealthHud
    [RequireComponent(typeof(UIDocument))]
    public sealed class HitDirectionHud : MonoBehaviour
    {
        public const int PoolSize = 3;
        public const string ArcClass = "hud-hit-arc";
        private const string VignetteName = "damage-vignette"; // GameHud.uxml: arcs go right above it

        /// <summary>Screen side of a hit, for tests and logs.</summary>
        public enum Side { Top, Right, Bottom, Left }

        [SerializeField] private PlayerHealth health;
        [SerializeField] private Camera worldCamera;
        [Tooltip("Arc size (panel px).")]
        [SerializeField] private Vector2 arcSize = new Vector2(360f, 104f);
        [Tooltip("Gap between the arc's outer rim and the screen edge (panel px).")]
        [SerializeField, Min(0f)] private float edgeMargin = 22f;
        [Tooltip("Seconds the arc shows (scaled time).")]
        [SerializeField, Min(0.1f)] private float showTime = 0.75f;
        [Tooltip("Seconds of full opacity before the fade.")]
        [SerializeField, Min(0f)] private float holdTime = 0.18f;
        [Tooltip("Same-side hits closer than this (degrees) reuse the running arc.")]
        [SerializeField, Range(0f, 90f)] private float mergeAngle = 35f;
        [SerializeField] private Color arcColor = new Color(0.78f, 0.03f, 0.02f, 1f);

        private const int TexWidth = 128, TexHeight = 40;

        private UIDocument _document;
        private Texture2D _texture;
        private readonly VisualElement[] _arcs = new VisualElement[PoolSize];
        private readonly float[] _age = new float[PoolSize];
        private readonly float[] _angle = new float[PoolSize];
        private readonly float[] _drawnOpacity = new float[PoolSize];

        /// <summary>Arcs shown so far (tests).</summary>
        public int Shown { get; private set; }
        /// <summary>Screen angle (degrees, 0 = up, 90 = right) of the last arc (tests).</summary>
        public float LastAngle { get; private set; }
        /// <summary>Arcs visible now (tests).</summary>
        public int Visible
        {
            get
            {
                int n = 0;
                for (int i = 0; i < PoolSize; i++)
                    if (_age[i] < showTime) n++;
                return n;
            }
        }

        /// <summary>Wires the Knight and the camera (tests; the scene builder sets the serialized fields).</summary>
        public void Setup(PlayerHealth knight, Camera cam)
        {
            if (isActiveAndEnabled && health != null) health.OnHit -= OnHit;
            health = knight;
            worldCamera = cam;
            if (isActiveAndEnabled && health != null) health.OnHit += OnHit;
        }

        /// <summary>
        /// Screen angle (degrees, 0 = up, 90 = right, 180 = down, 270 = left) of a hit from <paramref name="source"/> on the
        /// Knight at <paramref name="knight"/>. With a camera it projects both points; without one it uses the top-down mapping
        /// (world +X = right, +Z = up), which is what the yaw-0 follow camera shows.
        /// </summary>
        public static float ScreenAngle(Vector3 knight, Vector3 source, Camera cam)
        {
            float dx = source.x - knight.x, dy = source.z - knight.z;
            if (cam != null)
            {
                Vector3 a = cam.WorldToScreenPoint(knight), b = cam.WorldToScreenPoint(source);
                if (a.z > 0f && b.z > 0f)
                {
                    dx = b.x - a.x;
                    dy = b.y - a.y;
                }
            }
            if (dx * dx + dy * dy < 1e-8f) return 0f;
            float deg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>Screen side for an angle from <see cref="ScreenAngle"/> (45-degree sectors).</summary>
        public static Side SideOf(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            if (angle < 45f || angle >= 315f) return Side.Top;
            if (angle < 135f) return Side.Right;
            return angle < 225f ? Side.Bottom : Side.Left;
        }

        /// <summary>
        /// Arc centre on the panel for <paramref name="angle"/>: on an ellipse inside a <paramref name="panel"/>-sized screen,
        /// <paramref name="inset"/> px from the edge. Panel y grows downward.
        /// </summary>
        public static Vector2 ArcCentre(float angle, Vector2 panel, float inset)
        {
            float rad = angle * Mathf.Deg2Rad;
            float rx = Mathf.Max(0f, panel.x * 0.5f - inset), ry = Mathf.Max(0f, panel.y * 0.5f - inset);
            return new Vector2(panel.x * 0.5f + Mathf.Sin(rad) * rx, panel.y * 0.5f - Mathf.Cos(rad) * ry);
        }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            BuildArcs();
            if (health != null) health.OnHit += OnHit;
        }

        private void OnDisable()
        {
            if (health != null) health.OnHit -= OnHit;
        }

        private void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
        }

        private void BuildArcs()
        {
            var root = _document.rootVisualElement;
            if (root == null) return;
            _texture ??= BuildTexture(arcColor);
            var vignette = root.Q(VignetteName);
            var parent = vignette?.parent ?? root;
            int index = vignette != null ? parent.IndexOf(vignette) + 1 : parent.childCount;
            for (int i = 0; i < PoolSize; i++)
            {
                _age[i] = float.PositiveInfinity;
                _drawnOpacity[i] = -1f;
                if (_arcs[i] != null && _arcs[i].parent != null) continue; // re-enabled: the pooled arc is still there
                var arc = new VisualElement { name = "hit-arc-" + i, pickingMode = PickingMode.Ignore };
                arc.AddToClassList(ArcClass);
                arc.style.position = Position.Absolute;
                arc.style.left = 0f;
                arc.style.top = 0f;
                arc.style.width = arcSize.x;
                arc.style.height = arcSize.y;
                arc.style.opacity = 0f;
                arc.style.backgroundImage = new StyleBackground(_texture);
                arc.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
                arc.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f));
                parent.Insert(Mathf.Min(index + i, parent.childCount), arc);
                _arcs[i] = arc;
            }
        }

        private void OnHit(PlayerHit hit)
        {
            if (health == null) return;
            float angle = ScreenAngle(health.transform.position, hit.Source, worldCamera != null ? worldCamera : Camera.main);
            int slot = -1;
            float oldest = -1f;
            for (int i = 0; i < PoolSize; i++)
            {
                if (_age[i] < showTime && Mathf.Abs(Mathf.DeltaAngle(_angle[i], angle)) <= mergeAngle)
                {
                    slot = i; // same side again: refresh it instead of stacking
                    break;
                }
                if (_age[i] > oldest)
                {
                    oldest = _age[i];
                    slot = i;
                }
            }
            if (slot < 0 || _arcs[slot] == null) return;
            _age[slot] = 0f;
            _angle[slot] = angle;
            _drawnOpacity[slot] = -1f;
            LastAngle = angle;
            Shown++;
            Place(slot);
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Ages and fades the arcs by <paramref name="dt"/> seconds. Public for tests (LateUpdate calls it).</summary>
        public void Tick(float dt)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                if (_arcs[i] == null || float.IsPositiveInfinity(_age[i])) continue;
                _age[i] += dt;
                float t = _age[i];
                float opacity = t >= showTime ? 0f : t <= holdTime ? 1f : 1f - Smooth(0f, 1f, (t - holdTime) / (showTime - holdTime));
                if (t >= showTime) _age[i] = float.PositiveInfinity;
                // Pop: 1.25 -> 1 over the first 0.12 s.
                float scale = 1f + 0.25f * Mathf.Clamp01(1f - t / 0.12f);
                _arcs[i].style.scale = new Scale(new Vector3(scale, scale, 1f));
                if (opacity == _drawnOpacity[i]) continue;
                _drawnOpacity[i] = opacity;
                _arcs[i].style.opacity = opacity;
            }
        }

        private void Place(int i)
        {
            var arc = _arcs[i];
            var root = _document.rootVisualElement;
            Vector2 panel = new Vector2(root.layout.width, root.layout.height);
            if (float.IsNaN(panel.x) || panel.x <= 0f) panel = new Vector2(1080f, 2340f); // before the first layout pass
            Vector2 c = ArcCentre(_angle[i], panel, edgeMargin + arcSize.y * 0.5f);
            arc.style.translate = new Translate(c.x - arcSize.x * 0.5f, c.y - arcSize.y * 0.5f);
            arc.style.rotate = new Rotate(_angle[i]); // the bright rim (texture top) faces the edge
        }

        /// <summary>
        /// Crescent: a band of a big circle below the texture, bright and opaque at the outer (top) rim, soft toward the
        /// inside, fading out at both ends.
        /// </summary>
        private static Texture2D BuildTexture(Color color)
        {
            var tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false)
            {
                name = "T_HitArc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[TexWidth * TexHeight];
            float radius = TexWidth * 0.9f; // circle centre sits below the texture: a gentle curve
            float cx = TexWidth * 0.5f, cy = -radius + TexHeight * 0.88f; // texture y up: top row = outer rim
            for (int y = 0; y < TexHeight; y++)
            for (int x = 0; x < TexWidth; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float outer = radius, inner = radius - TexHeight * 0.75f;
                float band = Mathf.Clamp01((d - inner) / (outer - inner)); // 0 inner .. 1 outer rim
                float edge = Mathf.Clamp01((outer + 1.5f - d) / 3f);        // anti-aliased outer rim
                float a = Smooth(0f, 0.55f, band) * edge;         // solid band, soft inner glow
                float ends = Mathf.Abs(dx) / (TexWidth * 0.5f);             // fade at the tips
                a *= 1f - Smooth(0.5f, 1f, ends);
                float hot = Smooth(0.6f, 0.95f, band) * edge;    // bright core near the rim
                var c = Color.Lerp(color, new Color(1f, 0.42f, 0.28f), hot * 0.75f);
                pixels[y * TexWidth + x] = new Color(c.r, c.g, c.b, Mathf.Clamp01(a * 0.95f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>GLSL smoothstep: 0 at <paramref name="e0"/>, 1 at <paramref name="e1"/>, eased between (Mathf.SmoothStep
        /// eases between two values instead).</summary>
        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
    }
}
