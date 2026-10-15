using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Red ground lanes that warn of a straight boss attack (KAN-61): one lane per fan bolt, or one wide lane for the charge
    /// dash. Each lane is an edge quad over its full length (where) and a fill quad that grows from the boss to the far
    /// end while the attack winds up (<see cref="SetProgress"/>, when). The hit flashes the lanes bright, then they fade.
    /// Same colours and look as <see cref="GroundTelegraph"/>; colour through one MaterialPropertyBlock, no allocation per
    /// frame. Lanes are made by the builder (up to <see cref="MaxLanes"/>); unused ones stay disabled.
    /// KAN-121: <see cref="Hold"/> keeps the full lanes bright (live beams) and <see cref="Turn"/> turns them round the
    /// origin, until <see cref="Flash"/> fades them or <see cref="Hide"/>.
    /// KAN-167: the look is the TelegraphLane shader (burnt scorch, cracks that widen and glow with the fill, ember
    /// chevrons that flow from the attacker to the far end, dark torn outline, a sigil at the far end): the block also
    /// carries the quad length, lane width, progress and the "snap" of the last <see cref="snapTime"/> s. While a lane
    /// warns, dark-red smoke wisps rise along its sides through the scene's one shared <see cref="LaneSmoke"/> (wide lanes
    /// smoke more than thin fan / aim lanes).
    /// </summary>
    public sealed class LineTelegraph : MonoBehaviour
    {
        /// <summary>Most lanes one warning can show (largest fan).</summary>
        public const int MaxLanes = 9;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SpanId = Shader.PropertyToID("_Span");
        private static readonly int LaneWidthId = Shader.PropertyToID("_LaneWidth");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int SnapId = Shader.PropertyToID("_Snap");

        [Tooltip("Full-length lane quads (built lying flat: local Y = along the lane).")]
        [SerializeField] private Renderer[] edges = new Renderer[0];
        [Tooltip("Growing fill quads, same count as the edges.")]
        [SerializeField] private Renderer[] fills = new Renderer[0];
        [SerializeField] private Color edgeColor = new Color(0.62f, 0.02f, 0.01f, 1f);
        [SerializeField] private Color fillColor = new Color(0.5f, 0.02f, 0.01f, 0.72f);
        [SerializeField] private Color flashColor = new Color(1f, 0.42f, 0.2f, 0.85f);
        [SerializeField, Min(0.05f)] private float flashTime = 0.3f;
        [Tooltip("Height above the ground (no z-fight with the floor; just over the circle telegraph).")]
        [SerializeField, Min(0f)] private float lift = 0.06f;
        [Tooltip("KAN-167: seconds before the attack goes when the lane flashes and its cracks snap wide.")]
        [SerializeField, Min(0.01f)] private float snapTime = 0.15f;
        [Tooltip("KAN-167: smoke wisps per second per metre of lane width at the end of the wind-up (a third at the start).")]
        [SerializeField, Min(0f)] private float smokePerMetre = 7f;
        [Tooltip("KAN-167: most wisps one lane telegraph emits in one frame.")]
        [SerializeField, Min(0)] private int maxSmokePerFrame = 2;

        private readonly Vector3[] _dirs = new Vector3[MaxLanes];
        private readonly float[] _lengths = new float[MaxLanes];
        private MaterialPropertyBlock _mpb;
        private Vector3 _origin;
        private float _length = 1f, _width = 1f;
        private float _flash = -1f;
        private bool _held;
        private float _snap;
        private float _smokeDebt;

        /// <summary>A warning or its hit flash is on screen.</summary>
        public bool IsShowing { get; private set; }
        /// <summary>KAN-121: the lanes are live beams (full, bright, no fade).</summary>
        public bool IsHeld => _held;
        /// <summary>0 = just shown, 1 = the attack goes.</summary>
        public float Progress { get; private set; }
        /// <summary>KAN-167: 0 until the last <see cref="snapTime"/> s of the warning, then up to 1 (flash + cracks snap).</summary>
        public float Snap => _snap;
        /// <summary>Lanes in the current warning.</summary>
        public int LaneCount { get; private set; }
        /// <summary>Longest lane of the current warning.</summary>
        public float Length => _length;
        public float Width => _width;
        public Vector3 Origin => _origin;
        /// <summary>Lanes the builder made (the most <see cref="ShowFan"/> can show).</summary>
        public int Capacity => Mathf.Min(edges.Length, fills.Length);

        /// <summary>Ground direction of lane <paramref name="i"/> (unit, y 0).</summary>
        public Vector3 LaneDirection(int i) => _dirs[Mathf.Clamp(i, 0, MaxLanes - 1)];

        /// <summary>Length of lane <paramref name="i"/> (m).</summary>
        public float LaneLength(int i) => _lengths[Mathf.Clamp(i, 0, MaxLanes - 1)];

        /// <summary>Shortens lane <paramref name="i"/> to <paramref name="length"/> m (where a wall or pillar stops the attack).</summary>
        public void SetLaneLength(int i, float length)
        {
            if (i < 0 || i >= LaneCount) return;
            _lengths[i] = Mathf.Clamp(length, 0.05f, _length);
            Place(edges[i], _dirs[i], _lengths[i]);
            if (_flash < 0f) Place(fills[i], _dirs[i], Mathf.Max(0.05f, Eased() * _lengths[i]));
        }

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(Renderer[] laneEdges, Renderer[] laneFills)
        {
            edges = laneEdges ?? new Renderer[0];
            fills = laneFills ?? new Renderer[0];
            Hide();
        }

        private void Awake() => Hide();

        /// <summary>One lane from <paramref name="origin"/> along <paramref name="direction"/> (the charge dash).</summary>
        public void ShowLine(Vector3 origin, Vector3 direction, float length, float width) =>
            ShowFan(origin, direction, 1, 0f, length, width);

        /// <summary>
        /// <paramref name="count"/> lanes from <paramref name="origin"/>, spread evenly over <paramref name="spreadDegrees"/>
        /// around <paramref name="direction"/> (the projectile fan). Progress 0.
        /// </summary>
        public void ShowFan(Vector3 origin, Vector3 direction, int count, float spreadDegrees, float length, float width)
        {
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            LaneCount = Mathf.Clamp(count, 0, Mathf.Min(MaxLanes, Capacity));
            _origin = origin;
            _length = Mathf.Max(0.1f, length);
            _width = Mathf.Max(0.05f, width);
            _flash = -1f;
            _held = false;
            _snap = 0f;
            _smokeDebt = 0f;
            IsShowing = true;
            for (int i = 0; i < MaxLanes; i++)
            {
                float angle = count > 1 ? -spreadDegrees * 0.5f + spreadDegrees * i / (count - 1) : 0f;
                _dirs[i] = Quaternion.Euler(0f, angle, 0f) * direction;
                _lengths[i] = _length;
            }
            int capacity = Capacity;
            for (int i = 0; i < capacity; i++)
            {
                bool on = i < LaneCount;
                SetEnabled(edges[i], on);
                SetEnabled(fills[i], on);
                if (on) Place(edges[i], _dirs[i], _length);
            }
            SetProgress(0f);
        }

        /// <summary>
        /// Wind-up 0..1: the fills grow from the boss to the lane ends; the edges pulse faster near the end. Without the
        /// seconds left, the snap starts at 88 % of the warning.
        /// </summary>
        public void SetProgress(float progress) => SetProgress(progress, -1f);

        /// <summary>
        /// Wind-up 0..1 with the <paramref name="secondsLeft"/> of the warning (KAN-167: the lane snaps in the last
        /// <see cref="snapTime"/> s; negative = unknown, then by progress).
        /// </summary>
        public void SetProgress(float progress, float secondsLeft)
        {
            if (!IsShowing || _flash >= 0f || _held) return;
            Progress = Mathf.Clamp01(progress);
            _snap = secondsLeft >= 0f ? 1f - Mathf.Clamp01(secondsLeft / snapTime) : Mathf.InverseLerp(0.88f, 1f, Progress);
            float eased = Eased();
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, Progress));
            var e = edgeColor;
            e.a *= pulse;
            var f = fillColor;
            f.a *= Mathf.Lerp(0.55f, 1f, Progress);
            for (int i = 0; i < LaneCount; i++)
            {
                Place(fills[i], _dirs[i], Mathf.Max(0.05f, eased * _lengths[i]));
                Paint(edges[i], e);
                Paint(fills[i], f);
            }
            Smoke(eased);
        }

        /// <summary>KAN-167: wisps along the lane sides, over the filled part; more as the warning fills.</summary>
        private void Smoke(float eased)
        {
            var smoke = LaneSmoke.Active;
            if (smoke == null || LaneCount == 0) return;
            _smokeDebt += Time.deltaTime * smokePerMetre * _width * Mathf.Lerp(0.33f, 1f, Progress);
            int count = Mathf.Min((int)_smokeDebt, maxSmokePerFrame);
            _smokeDebt -= count;
            if (_smokeDebt > maxSmokePerFrame) _smokeDebt = maxSmokePerFrame; // a long frame never bursts later
            for (int n = 0; n < count; n++)
            {
                int i = Random.Range(0, LaneCount);
                Vector3 dir = _dirs[i];
                Vector3 side = new Vector3(dir.z, 0f, -dir.x) * (_width * 0.45f * (Random.value < 0.5f ? -1f : 1f));
                Vector3 at = _origin + dir * (Random.Range(0.1f, 1f) * Mathf.Max(0.05f, eased * _lengths[i])) + side;
                smoke.Emit(at, Progress);
            }
        }

        /// <summary>KAN-121: the lanes go live: full length, hit colour, no fade until <see cref="Flash"/> or <see cref="Hide"/>.</summary>
        public void Hold()
        {
            if (!IsShowing) return;
            Progress = 1f;
            _flash = -1f;
            _held = true;
            for (int i = 0; i < LaneCount; i++)
            {
                Place(fills[i], _dirs[i], _lengths[i]);
                Paint(fills[i], flashColor);
                Paint(edges[i], edgeColor);
            }
        }

        /// <summary>KAN-121: turns every lane <paramref name="degrees"/> round the origin (clockwise seen from above when positive).</summary>
        public void Turn(float degrees)
        {
            if (!IsShowing || LaneCount == 0) return;
            var q = Quaternion.Euler(0f, degrees, 0f);
            float length = _flash >= 0f || _held ? 1f : Eased();
            for (int i = 0; i < LaneCount; i++)
            {
                _dirs[i] = q * _dirs[i];
                Place(edges[i], _dirs[i], _lengths[i]);
                Place(fills[i], _dirs[i], Mathf.Max(0.05f, length * _lengths[i]));
            }
        }

        /// <summary>The attack goes: full lanes flash bright, then fade out and hide.</summary>
        public void Flash()
        {
            if (!IsShowing) return;
            Progress = 1f;
            _flash = 0f;
            _held = false;
            for (int i = 0; i < LaneCount; i++) Place(fills[i], _dirs[i], _lengths[i]);
        }

        /// <summary>Hides at once (attack cancelled, boss died).</summary>
        public void Hide()
        {
            IsShowing = false;
            _flash = -1f;
            _held = false;
            Progress = 0f;
            _snap = 0f;
            LaneCount = 0;
            for (int i = 0; i < edges.Length; i++) SetEnabled(edges[i], false);
            for (int i = 0; i < fills.Length; i++) SetEnabled(fills[i], false);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Fades the hit flash (public for tests).</summary>
        public void Tick(float dt)
        {
            if (_flash < 0f) return;
            _flash += dt;
            float k = Mathf.Clamp01(_flash / flashTime);
            var f = flashColor;
            f.a *= 1f - k;
            var e = edgeColor;
            e.a *= 1f - k;
            for (int i = 0; i < LaneCount; i++)
            {
                Paint(fills[i], f);
                Paint(edges[i], e);
            }
            if (k >= 1f) Hide();
        }

        private float Eased() => 1f - (1f - Progress) * (1f - Progress);

        /// <summary>Lays a lane quad from the origin along <paramref name="dir"/> for <paramref name="length"/> m.</summary>
        private void Place(Renderer r, Vector3 dir, float length)
        {
            if (r == null) return;
            var t = r.transform;
            t.SetPositionAndRotation(new Vector3(_origin.x, _origin.y + lift, _origin.z) + dir * (length * 0.5f),
                Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f));
            t.localScale = new Vector3(_width, length, 1f);
        }

        private static void SetEnabled(Renderer r, bool on)
        {
            if (r != null && r.enabled != on) r.enabled = on;
        }

        /// <summary>Colour + the shader's lane values (KAN-167); the quad's length is its Y scale (<see cref="Place"/>).</summary>
        private void Paint(Renderer r, Color c)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetFloat(SpanId, r.transform.localScale.y);
            _mpb.SetFloat(LaneWidthId, _width);
            _mpb.SetFloat(ProgressId, Progress);
            _mpb.SetFloat(SnapId, _snap);
            r.SetPropertyBlock(_mpb);
        }
    }
}
