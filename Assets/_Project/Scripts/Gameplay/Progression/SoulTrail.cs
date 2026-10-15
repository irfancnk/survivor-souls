using UnityEngine;

namespace SurvivorSouls.Gameplay.Progression
{
    /// <summary>
    /// Soft see-through trails behind flying soul crystals (KAN-177). <see cref="SoulWispField"/> takes a slot for a
    /// crystal while it pops out of a body or flies to the Knight, pushes its position every frame, and lets the slot
    /// go when the crystal lands or is absorbed. A released trail keeps its points and shrinks into its last point as they
    /// age, so it never cuts off.
    /// <para>
    /// Drawing: ONE camera-facing ribbon mesh for every trail (one draw call, alpha blend, soft texture across the width).
    /// Each trail is a ring of the last <see cref="PointsPerTrail"/> positions; only points younger than
    /// <see cref="Life"/> are drawn. Width tapers from the head to a point at the tail, alpha fades with age. All arrays
    /// are made in Awake: no allocation per frame.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(110)] // after SoulWispField pushed this frame's positions
    public sealed class SoulTrail : MonoBehaviour
    {
        public const int PointsPerTrail = 14;

        private struct Trail
        {
            public bool Used;
            public bool Released;
            public int Head;   // ring index of the newest point
            public int Count;  // points stored (max PointsPerTrail)
            public float Width;
            public Color32 Colour;
        }

        [Tooltip("Trails alive at once. A crystal that finds no free slot flies without a trail.")]
        [SerializeField, Min(1)] private int capacity = 40;
        [Tooltip("Seconds a point stays in the trail (trail length = flight speed x life).")]
        [SerializeField, Min(0.02f)] private float life = 0.2f;
        [Tooltip("Alpha at the head of the trail (slight: the crystal must stay the hero).")]
        [SerializeField, Range(0f, 1f)] private float headAlpha = 0.72f;

        private Trail[] _trails;
        private Vector3[] _points;   // capacity * PointsPerTrail
        private float[] _times;
        private Vector3[] _vertices;
        private Color32[] _colours;
        private Vector2[] _uvs;
        private int[] _indices;
        private Mesh _mesh;
        private Transform _camera;
        private float _now;

        public float Life => life;
        public int Capacity => capacity;
        /// <summary>Ribbon vertices built last frame (2 per drawn point). For tests.</summary>
        public int VertexCount { get; private set; }
        /// <summary>Trails in use, released ones still fading included.</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                if (_trails != null)
                    for (int i = 0; i < _trails.Length; i++)
                        if (_trails[i].Used) n++;
                return n;
            }
        }

        private void Awake() => Allocate();

        /// <summary>Wiring for tests and code-built objects.</summary>
        public void Setup(int trailCapacity, float trailLife)
        {
            capacity = Mathf.Max(1, trailCapacity);
            life = Mathf.Max(0.02f, trailLife);
            _trails = null;
            Allocate();
        }

        private void Allocate()
        {
            if (_trails != null && _trails.Length == capacity) return;
            _trails = new Trail[capacity];
            int points = capacity * PointsPerTrail;
            _points = new Vector3[points];
            _times = new float[points];
            _vertices = new Vector3[points * 2];
            _colours = new Color32[points * 2];
            _uvs = new Vector2[points * 2];
            _indices = new int[capacity * (PointsPerTrail - 1) * 6];
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Soul Trails" };
                _mesh.MarkDynamic();
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }
        }

        /// <summary>Takes a free trail slot (-1 when all are busy). <paramref name="width"/> = ribbon width at the head (m).</summary>
        public int Acquire(Vector3 position, float width, Color colour)
        {
            Allocate();
            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i].Used) continue;
                _trails[i] = new Trail { Used = true, Width = width, Colour = colour, Head = 0, Count = 1 };
                _points[i * PointsPerTrail] = position;
                _times[i * PointsPerTrail] = _now;
                return i;
            }
            return -1;
        }

        /// <summary>Adds the crystal's current position to its trail.</summary>
        public void Push(int slot, Vector3 position)
        {
            if (slot < 0 || _trails == null || slot >= _trails.Length) return;
            ref var t = ref _trails[slot];
            if (!t.Used || t.Released) return;
            int baseIndex = slot * PointsPerTrail;
            // Two pushes in one frame (or a tiny move): move the head instead of adding a point.
            if ((_points[baseIndex + t.Head] - position).sqrMagnitude < 0.0004f || _times[baseIndex + t.Head] >= _now)
            {
                _points[baseIndex + t.Head] = position;
                return;
            }
            t.Head = (t.Head + 1) % PointsPerTrail;
            if (t.Count < PointsPerTrail) t.Count++;
            _points[baseIndex + t.Head] = position;
            _times[baseIndex + t.Head] = _now;
        }

        /// <summary>The crystal landed or was absorbed: the trail stops growing and fades out on its own.</summary>
        public void Release(int slot)
        {
            if (slot < 0 || _trails == null || slot >= _trails.Length) return;
            _trails[slot].Released = true;
        }

        /// <summary>Clears every trail at once (scene reset, tests).</summary>
        public void Clear()
        {
            if (_trails == null) return;
            for (int i = 0; i < _trails.Length; i++) _trails[i] = default;
            Tick(0f);
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Ages the points and rebuilds the ribbon mesh. Public for tests (LateUpdate calls it).</summary>
        public void Tick(float dt)
        {
            Allocate();
            _now += dt;
            if (_camera == null)
            {
                var cam = Camera.main;
                if (cam != null) _camera = cam.transform;
            }
            Vector3 camPos = _camera != null ? _camera.position : Vector3.zero;
            bool hasCamera = _camera != null;

            int v = 0, idx = 0;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int s = 0; s < _trails.Length; s++)
            {
                ref var t = ref _trails[s];
                if (!t.Used) continue;
                int baseIndex = s * PointsPerTrail;

                // Drop points older than life (oldest first); a released trail with no points left frees its slot.
                while (t.Count > 0)
                {
                    int tail = (t.Head - t.Count + 1 + PointsPerTrail) % PointsPerTrail;
                    if (_now - _times[baseIndex + tail] <= life) break;
                    t.Count--;
                }
                if (t.Count == 0 || (t.Released && t.Count == 1 && _now - _times[baseIndex + t.Head] > life * 0.5f))
                {
                    t = default;
                    continue;
                }
                if (t.Count < 2) continue;

                int first = v;
                for (int k = 0; k < t.Count; k++) // k = 0 head (newest) .. Count-1 tail (oldest)
                {
                    int ring = (t.Head - k + PointsPerTrail) % PointsPerTrail;
                    Vector3 p = _points[baseIndex + ring];
                    Vector3 prev = _points[baseIndex + (k == 0 ? ring : (ring + 1) % PointsPerTrail)];
                    Vector3 next = _points[baseIndex + (k == t.Count - 1 ? ring : (ring - 1 + PointsPerTrail) % PointsPerTrail)];
                    Vector3 tangent = prev - next;
                    if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.forward;
                    Vector3 toCam = hasCamera ? camPos - p : Vector3.up;
                    Vector3 side = Vector3.Cross(tangent, toCam);
                    float sideLen = side.magnitude;
                    side = sideLen > 1e-6f ? side / sideLen : Vector3.right;

                    float along = k / (float)(t.Count - 1);                 // 0 head .. 1 tail
                    float age = Mathf.Clamp01((_now - _times[baseIndex + ring]) / life);
                    float fade = (1f - age) * Mathf.Sqrt(1f - along);
                    float half = t.Width * 0.5f * (1f - along * along);    // rounded taper to a point
                    var c = t.Colour;
                    c.a = (byte)(c.a * headAlpha * fade);

                    _vertices[v] = p + side * half;
                    _vertices[v + 1] = p - side * half;
                    _colours[v] = c;
                    _colours[v + 1] = c;
                    _uvs[v] = new Vector2(0.5f, 0f);
                    _uvs[v + 1] = new Vector2(0.5f, 1f);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    v += 2;
                }
                for (int k = 0; k < t.Count - 1; k++)
                {
                    int a = first + k * 2; // wound to face the camera (particle materials cull back faces)
                    _indices[idx++] = a;
                    _indices[idx++] = a + 1;
                    _indices[idx++] = a + 2;
                    _indices[idx++] = a + 1;
                    _indices[idx++] = a + 3;
                    _indices[idx++] = a + 2;
                }
            }

            VertexCount = v;
            _mesh.SetIndices(_indices, 0, 0, MeshTopology.Triangles, 0, false);
            _mesh.SetVertices(_vertices, 0, v);
            _mesh.SetColors(_colours, 0, v);
            _mesh.SetUVs(0, _uvs, 0, v);
            _mesh.SetIndices(_indices, 0, idx, MeshTopology.Triangles, 0, false);
            _mesh.bounds = v > 0 ? new Bounds((min + max) * 0.5f, max - min + Vector3.one) : new Bounds(Vector3.zero, Vector3.one);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
