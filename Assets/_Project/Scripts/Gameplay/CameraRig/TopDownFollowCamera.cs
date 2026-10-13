using UnityEngine;

namespace SurvivorSouls.Gameplay.CameraRig
{
    /// <summary>
    /// Archero-style camera: fixed pitch, yaw 0, follows the target along Z.
    /// Distance is computed from the screen aspect so <see cref="viewWidth"/> metres of arena always fit
    /// the screen width (tall and short phones show the same width; taller phones see more depth).
    /// <see cref="Shake"/> adds a short decaying wobble on top (unscaled time, so it settles during a pause too).
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

        private Camera _camera;
        private Vector3 _look;
        private float _shakeAmplitude, _shakeTime, _shakeLength;

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
            _look = Vector3.Lerp(_look, DesiredLook(), 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            if (_shakeTime > 0f) _shakeTime = Mathf.Max(0f, _shakeTime - Time.unscaledDeltaTime);
            Place();
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
            return new Vector3(x, 0f, z);
        }

        private void Place()
        {
            float halfV = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * _camera.aspect);
            float distance = viewWidth * 0.5f / Mathf.Tan(halfH);

            var rotation = Quaternion.Euler(pitch, 0f, 0f);
            transform.SetPositionAndRotation(_look - rotation * Vector3.forward * distance + ShakeOffset, rotation);
        }
    }
}
