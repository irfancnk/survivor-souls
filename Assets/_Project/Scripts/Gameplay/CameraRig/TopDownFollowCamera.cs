using UnityEngine;

namespace SurvivorSouls.Gameplay.CameraRig
{
    /// <summary>
    /// Archero-style camera: fixed pitch, yaw 0, follows the target along Z.
    /// Distance is computed from the screen aspect so <see cref="viewWidth"/> metres of arena always fit
    /// the screen width (tall and short phones show the same width; taller phones see more depth).
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
            Place();
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
            transform.SetPositionAndRotation(_look - rotation * Vector3.forward * distance, rotation);
        }
    }
}
