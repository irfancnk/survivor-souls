using UnityEngine;

namespace SurvivorSouls.Gameplay.CameraRig
{
    /// <summary>
    /// Archero-style camera: fixed pitch, yaw 0, follows the target along Z.
    /// Distance is computed from the screen aspect so <see cref="viewWidth"/> metres of arena always fit
    /// the screen width (tall and short phones show the same width; taller phones see more depth).
    /// <see cref="Shake"/> adds a short decaying wobble on top (unscaled time, so it settles during a pause too).
    /// <see cref="SetBounds"/> (boss arena, KAN-59) keeps the look point inside a circle with a soft edge: it follows freely
    /// in the middle, slows down in the last <c>softBand</c> metres and never passes the limit.
    /// <see cref="Focus"/> (boss intro and victory beat, KAN-62) eases the look point to a fixed spot and the distance by a zoom
    /// factor (above 1 = pulled out); <see cref="ReleaseFocus"/> eases back to the follow view. Unscaled time, so slow motion
    /// does not slow the move.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TopDownFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Range(30f, 89f)] private float pitch = 55f;
        [Tooltip("World width (m) visible across the screen at the target.")]
        [SerializeField, Min(1f)] private float viewWidth = 10.5f;
        [Tooltip("Shifts the look point ahead (+Z) of the target, so the player sits below screen centre.")]
        [SerializeField] private float lookAhead = 2.2f;
        [SerializeField, Min(0f)] private float followSharpness = 8f;
        [Tooltip("0 = camera X stays at arenaCentreX; 1 = follows target X fully.")]
        [SerializeField, Range(0f, 1f)] private float followX = 0.15f;
        [SerializeField] private float arenaCentreX;
        [Tooltip("Clamp of the look point Z, so the camera does not show too much outside the arena.")]
        [SerializeField] private Vector2 lookZRange = new Vector2(-8f, 8f);
        [Tooltip("How fast Focus / ReleaseFocus blend in and out (1/s, exponential).")]
        [SerializeField, Min(0.1f)] private float focusSharpness = 3.5f;

        private Camera _camera;
        private Vector3 _look;
        private bool _bounded;
        private Vector3 _boundsCentre;
        private float _boundsRadius, _boundsSoftBand;
        private float _shakeAmplitude, _shakeTime, _shakeLength;
        private bool _focused;
        private Vector3 _focusPoint;
        private float _focusZoom = 1f, _focusWeight;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        private void Awake() => _camera = GetComponent<Camera>();

        private void Start()
        {
            if (target == null) return;
            _look = DesiredLook();
            Place();
        }

        private void LateUpdate()
        {
            if (target == null) return;
            StepFocus(Time.unscaledDeltaTime);
            _look = Vector3.Lerp(_look, DesiredLook(), 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            if (_shakeTime > 0f) _shakeTime = Mathf.Max(0f, _shakeTime - Time.unscaledDeltaTime);
            Place();
        }

        /// <summary>Whether the look point is held inside a circle (<see cref="SetBounds"/>).</summary>
        public bool Bounded => _bounded;

        /// <summary>
        /// Holds the look point inside a circle of <paramref name="radius"/> m around <paramref name="centre"/> (XZ), with a
        /// soft edge <paramref name="softBand"/> m wide. Jumps the camera to the new look point (call it before the first frame).
        /// </summary>
        public void SetBounds(Vector3 centre, float radius, float softBand)
        {
            _bounded = true;
            _boundsCentre = centre;
            _boundsRadius = Mathf.Max(0.01f, radius);
            _boundsSoftBand = Mathf.Clamp(softBand, 0.01f, _boundsRadius);
            if (target != null && _camera != null)
            {
                _look = DesiredLook();
                Place();
            }
        }

        public void ClearBounds() => _bounded = false;

        /// <summary>A <see cref="Focus"/> is on (it may still be blending in).</summary>
        public bool Focused => _focused;
        /// <summary>Blend 0 (follow view) .. 1 (fully on the focus point and zoom).</summary>
        public float FocusWeight => _focusWeight;
        /// <summary>Distance factor now: 1 in the follow view, the focus zoom when fully focused.</summary>
        public float CurrentZoom => Mathf.Lerp(1f, _focusZoom, _focusWeight);

        /// <summary>
        /// Eases the look point to <paramref name="point"/> (XZ) and the camera distance x <paramref name="zoom"/> (boss intro:
        /// both fighters in view). A new call moves the target; the blend keeps going from where it is.
        /// </summary>
        public void Focus(Vector3 point, float zoom)
        {
            _focused = true;
            _focusPoint = new Vector3(point.x, 0f, point.z);
            _focusZoom = Mathf.Max(0.1f, zoom);
        }

        /// <summary>Eases back to the follow view.</summary>
        public void ReleaseFocus() => _focused = false;

        /// <summary>Moves the focus blend by <paramref name="dt"/> real seconds. Public for tests (LateUpdate calls it).</summary>
        public void StepFocus(float dt)
        {
            float goal = _focused ? 1f : 0f;
            if (Mathf.Approximately(_focusWeight, goal)) return;
            _focusWeight = Mathf.Lerp(_focusWeight, goal, 1f - Mathf.Exp(-focusSharpness * Mathf.Max(0f, dt)));
            if (Mathf.Abs(_focusWeight - goal) < 0.002f) _focusWeight = goal;
        }

        /// <summary>
        /// Soft circular clamp of an offset from the centre: unchanged up to <c>radius - band</c>, then eased so it reaches
        /// <paramref name="radius"/> only far outside. Continuous and monotonic (no jump, no turn-back). Public for tests.
        /// </summary>
        public static Vector2 SoftClamp(Vector2 offset, float radius, float band)
        {
            float d = offset.magnitude;
            float inner = radius - band;
            if (d <= inner || d <= 0f) return offset;
            float eased = inner + band * (1f - Mathf.Exp(-(d - inner) / band));
            return offset * (eased / d);
        }

        /// <summary>Small camera shake: <paramref name="amplitude"/> metres, fading out over <paramref name="duration"/> s.
        /// A stronger shake replaces a weaker running one.</summary>
        public void Shake(float amplitude, float duration)
        {
            float current = _shakeLength > 0f ? _shakeAmplitude * _shakeTime / _shakeLength : 0f;
            if (amplitude < current || duration <= 0f) return;
            _shakeAmplitude = amplitude;
            _shakeLength = _shakeTime = duration;
        }

        /// <summary>Current shake offset (metres). Zero when calm.</summary>
        public Vector3 ShakeOffset
        {
            get
            {
                if (_shakeTime <= 0f || _shakeLength <= 0f) return Vector3.zero;
                float k = _shakeTime / _shakeLength;
                float t = Time.unscaledTime * 27f;
                return new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 0.5f, Mathf.PerlinNoise(t, 5.1f) - 0.5f)
                       * (2f * _shakeAmplitude * k * k);
            }
        }

        private Vector3 DesiredLook()
        {
            Vector3 p = target.position;
            float x = Mathf.Lerp(arenaCentreX, p.x, followX);
            float z = Mathf.Clamp(p.z + lookAhead, lookZRange.x, lookZRange.y);
            if (_bounded)
            {
                var o = SoftClamp(new Vector2(x - _boundsCentre.x, z - _boundsCentre.z), _boundsRadius, _boundsSoftBand);
                x = _boundsCentre.x + o.x;
                z = _boundsCentre.z + o.y;
            }
            var follow = new Vector3(x, 0f, z);
            return _focusWeight > 0f ? Vector3.Lerp(follow, _focusPoint, _focusWeight) : follow;
        }

        private void Place()
        {
            float halfV = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * _camera.aspect);
            float distance = viewWidth * CurrentZoom * 0.5f / Mathf.Tan(halfH);

            var rotation = Quaternion.Euler(pitch, 0f, 0f);
            transform.SetPositionAndRotation(_look - rotation * Vector3.forward * distance + ShakeOffset, rotation);
        }
    }
}
