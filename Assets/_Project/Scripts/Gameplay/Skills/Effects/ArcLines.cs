using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// Pooled jagged lightning arcs (KAN-246: Storm Touch, Stormshock, Storm Satellites). One <see cref="template"/>
    /// LineRenderer child is copied to <see cref="poolSize"/> lines in Awake (no allocation after that). An arc is a thin
    /// zig-zag of <see cref="Points"/> points between two world points that re-jitters every <see cref="jitterInterval"/> s
    /// (crackle) and fades out over its life. World space, unlit additive material from the builder.
    /// </summary>
    public sealed class ArcLines : MonoBehaviour
    {
        public const int Points = 9;

        [SerializeField] private LineRenderer template;
        [SerializeField, Min(1)] private int poolSize = 16;
        [Tooltip("Sideways zig-zag size as a share of the arc length (capped at maxJag m).")]
        [SerializeField, Range(0f, 0.5f)] private float jag = 0.16f;
        [SerializeField, Min(0f)] private float maxJag = 0.45f;
        [SerializeField, Min(0.01f)] private float jitterInterval = 0.035f;

        private LineRenderer[] _lines;
        private Vector3[][] _points;
        private Vector3[] _from, _to;
        private float[] _life, _left, _jitter;
        private Color[] _color;
        private int _next;

        /// <summary>Arcs drawn since the effect spawned (tests).</summary>
        public int ArcCount { get; private set; }
        /// <summary>Arcs on screen now.</summary>
        public int Live
        {
            get
            {
                int n = 0;
                if (_left != null)
                    for (int i = 0; i < _left.Length; i++)
                        if (_left[i] > 0f) n++;
                return n;
            }
        }

        private void Awake() => EnsurePool();

        private void EnsurePool()
        {
            if (_lines != null) return;
            int n = Mathf.Max(1, poolSize);
            _lines = new LineRenderer[n];
            _points = new Vector3[n][];
            _from = new Vector3[n];
            _to = new Vector3[n];
            _life = new float[n];
            _left = new float[n];
            _jitter = new float[n];
            _color = new Color[n];
            for (int i = 0; i < n; i++)
            {
                LineRenderer line;
                if (template == null)
                {
                    line = new GameObject("Arc").AddComponent<LineRenderer>();
                    line.transform.SetParent(transform, false);
                    line.widthMultiplier = 0.08f;
                }
                else line = i == 0 ? template : Instantiate(template, transform);
                line.useWorldSpace = true;
                line.positionCount = Points;
                line.enabled = false;
                _lines[i] = line;
                _points[i] = new Vector3[Points];
            }
        }

        /// <summary>Draws an arc from <paramref name="from"/> to <paramref name="to"/> for <paramref name="life"/> s.</summary>
        public void Arc(Vector3 from, Vector3 to, Color color, float life = 0.18f, float width = 1f)
        {
            EnsurePool();
            int i = _next;
            _next = (_next + 1) % _lines.Length;
            _from[i] = from;
            _to[i] = to;
            _life[i] = _left[i] = Mathf.Max(0.02f, life);
            _jitter[i] = 0f;
            _color[i] = color;
            var line = _lines[i];
            line.widthMultiplier = (template != null ? template.widthMultiplier : 0.08f) * width;
            line.enabled = true;
            Jag(i);
            Paint(i);
            ArcCount++;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Crackle and fade, <paramref name="dt"/> scaled seconds. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (_lines == null) return;
            for (int i = 0; i < _lines.Length; i++)
            {
                if (_left[i] <= 0f) continue;
                _left[i] -= dt;
                if (_left[i] <= 0f)
                {
                    _lines[i].enabled = false;
                    continue;
                }
                _jitter[i] += dt;
                if (_jitter[i] >= jitterInterval)
                {
                    _jitter[i] = 0f;
                    Jag(i);
                }
                Paint(i);
            }
        }

        private void Jag(int i)
        {
            Vector3 a = _from[i], b = _to[i];
            Vector3 d = b - a;
            float len = d.magnitude;
            Vector3 side = len > 1e-4f ? Vector3.Cross(d / len, Vector3.up) : Vector3.right;
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            float amp = Mathf.Min(maxJag, len * jag);
            var pts = _points[i];
            for (int k = 0; k < Points; k++)
            {
                float t = k / (float)(Points - 1);
                float edge = Mathf.Sin(t * Mathf.PI); // ends sit on the targets
                pts[k] = Vector3.Lerp(a, b, t) + side * (Random.Range(-amp, amp) * edge) + Vector3.up * (Random.Range(-amp, amp) * 0.5f * edge);
            }
            _lines[i].SetPositions(pts);
        }

        private void Paint(int i)
        {
            float k = Mathf.Clamp01(_left[i] / _life[i]);
            var c = _color[i];
            c.a *= k;
            _lines[i].startColor = c;
            _lines[i].endColor = c;
        }
    }
}
