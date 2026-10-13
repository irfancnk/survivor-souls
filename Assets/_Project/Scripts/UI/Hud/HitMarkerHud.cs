using SurvivorSouls.Gameplay.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Call of Duty style hit confirmation on the HUD document: four diagonal ticks around a gap, popped at the screen
    /// point of the enemy that was hit. White on a hit, larger + red + longer on a kill (KAN-13).
    /// Event driven (<see cref="Enemy.Damaged"/>). A fixed pool of <see cref="capacity"/> markers is built once; a new hit
    /// on an enemy that already shows a marker restarts that marker (no stacking), and when every marker is busy the oldest
    /// one is reused. Target off screen: the marker shows at screen centre. Never blocks touches (picking ignored).
    /// Looks live in GameHud.uss (hit-marker*). Animation numbers are the fields below.
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate
    [RequireComponent(typeof(UIDocument))]
    public sealed class HitMarkerHud : MonoBehaviour
    {
        private const string LayerClass = "hit-marker-layer";
        private const string MarkerClass = "hit-marker";
        private const string KillClass = "hit-marker--kill";
        private const string ArmClass = "hit-marker__arm";
        private const string TickClass = "hit-marker__tick";
        private const string JoystickName = "joystick"; // GameHud.uxml
        private static readonly float[] ArmAngles = { 45f, 135f, 225f, 315f };

        [SerializeField] private Camera worldCamera;
        [Tooltip("Markers on screen at once. A hit beyond this reuses the oldest marker.")]
        [SerializeField, Range(1, 16)] private int capacity = 6;

        [Header("Hit")]
        [SerializeField, Min(0.05f)] private float hitLifetime = 0.2f;
        [SerializeField, Min(1f)] private float hitPopScale = 1.2f;

        [Header("Kill")]
        [SerializeField, Min(0.05f)] private float killLifetime = 0.38f;
        [SerializeField, Min(1f)] private float killPopScale = 1.45f;

        [Header("Shared")]
        [Tooltip("Seconds of the pop-in (scale from pop scale down to 1).")]
        [SerializeField, Min(0.01f)] private float popTime = 0.07f;
        [Tooltip("Fraction of the lifetime at full opacity before the fade-out starts.")]
        [SerializeField, Range(0f, 0.95f)] private float holdFraction = 0.35f;

        private struct Marker
        {
            public VisualElement Root;
            public Enemy Target;
            public Vector3 World;
            public float Age, Life, Pop;
            public bool Active, Kill;
        }

        private UIDocument _document;
        private VisualElement _layer;
        private Marker[] _markers;

        /// <summary>Markers currently on screen.</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                if (_markers != null)
                    foreach (var m in _markers) if (m.Active) n++;
                return n;
            }
        }

        public int Capacity => capacity;

        /// <summary>True when <paramref name="enemy"/> has a marker on screen; <paramref name="kill"/> says which kind.</summary>
        public bool TryGetMarker(Enemy enemy, out bool kill)
        {
            kill = false;
            int i = IndexOf(enemy);
            if (i < 0) return false;
            kill = _markers[i].Kill;
            return true;
        }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            _layer ??= BuildLayer();
            // Above the world-anchored bars and numbers (CombatHud), below the joystick, buttons and death screen.
            var root = _document.rootVisualElement;
            if (root != null)
            {
                int below = root.IndexOf(root.Q(JoystickName));
                root.Insert(below >= 0 ? below : root.childCount, _layer);
            }
            Enemy.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            Enemy.Damaged -= OnDamaged;
            for (int i = 0; i < _markers.Length; i++) Hide(i);
            _layer.RemoveFromHierarchy();
        }

        private void OnDamaged(Enemy enemy, float deducted, bool killed) => Show(enemy, killed);

        /// <summary>Shows a hit (or kill) marker on <paramref name="enemy"/>. Restarts its marker if it already has one.</summary>
        public void Show(Enemy enemy, bool kill)
        {
            if (enemy == null || _markers == null) return;
            int i = IndexOf(enemy);
            if (i < 0) i = FreeOrOldest();
            ref var m = ref _markers[i];
            // A late hit never downgrades a kill marker that is still showing.
            bool asKill = kill || (m.Active && m.Target == enemy && m.Kill);
            m.Target = enemy;
            m.World = enemy.AimPoint;
            m.Age = 0f;
            m.Kill = asKill;
            m.Life = asKill ? killLifetime : hitLifetime;
            m.Pop = asKill ? killPopScale : hitPopScale;
            m.Active = true;
            m.Root.EnableInClassList(KillClass, asKill);
            m.Root.style.display = DisplayStyle.Flex;
            Animate(ref m);
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Advances every marker by <paramref name="dt"/> seconds and places it on screen. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (_markers == null) return;
            var cam = worldCamera != null ? worldCamera : Camera.main;
            bool canPlace = cam != null && _layer.panel != null;
            for (int i = 0; i < _markers.Length; i++)
            {
                ref var m = ref _markers[i];
                if (!m.Active) continue;
                m.Age += dt;
                if (m.Age >= m.Life) { Hide(i); continue; }
                // Follow the body while it is around (knockback, fall), else stay where it was last seen.
                if (m.Target != null && m.Target.isActiveAndEnabled) m.World = m.Target.AimPoint;
                Animate(ref m);
                if (canPlace) Place(m.Root, cam, m.World);
            }
        }

        private void Animate(ref Marker m)
        {
            float pop = Mathf.Clamp01(m.Age / popTime);
            float s = Mathf.Lerp(m.Pop, 1f, 1f - (1f - pop) * (1f - pop)); // ease out
            m.Root.style.scale = new Scale(new Vector3(s, s, 1f));
            float t = m.Age / m.Life;
            float fade = t <= holdFraction ? 0f : (t - holdFraction) / (1f - holdFraction);
            m.Root.style.opacity = 1f - fade * fade;
        }

        /// <summary>Centres <paramref name="e"/> on the screen point of <paramref name="world"/>; screen centre when off screen.</summary>
        private void Place(VisualElement e, Camera cam, Vector3 world)
        {
            Vector3 view = cam.WorldToViewportPoint(world);
            bool onScreen = view.z > 0f && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f;
            Vector2 p = onScreen
                ? RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, world, cam)
                : _layer.layout.center;
            e.style.translate = new Translate(p.x, p.y);
        }

        private void Hide(int i)
        {
            ref var m = ref _markers[i];
            m.Active = false;
            m.Target = null;
            m.Root.style.display = DisplayStyle.None;
        }

        private int IndexOf(Enemy enemy)
        {
            if (_markers == null) return -1;
            for (int i = 0; i < _markers.Length; i++)
                if (_markers[i].Active && _markers[i].Target == enemy) return i;
            return -1;
        }

        private int FreeOrOldest()
        {
            int oldest = 0;
            for (int i = 0; i < _markers.Length; i++)
            {
                if (!_markers[i].Active) return i;
                if (_markers[i].Age > _markers[oldest].Age) oldest = i;
            }
            return oldest;
        }

        /// <summary>Builds the full-screen layer and every marker up front: hits never create elements.</summary>
        private VisualElement BuildLayer()
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList(LayerClass);
            _markers = new Marker[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var root = new VisualElement { pickingMode = PickingMode.Ignore };
                root.AddToClassList(MarkerClass);
                foreach (float angle in ArmAngles)
                {
                    // Zero-size arm rotated about its own point; the tick sits above it, past the centre gap.
                    var arm = new VisualElement { pickingMode = PickingMode.Ignore };
                    arm.AddToClassList(ArmClass);
                    arm.style.rotate = new Rotate(Angle.Degrees(angle));
                    var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                    tick.AddToClassList(TickClass);
                    arm.Add(tick);
                    root.Add(arm);
                }
                root.style.display = DisplayStyle.None;
                layer.Add(root);
                _markers[i].Root = root;
            }
            return layer;
        }
    }
}
