using UnityEngine;

namespace SurvivorSouls.FX
{
    /// <summary>Slow idle sway for menu cameras. Offsets from the start pose; never accumulates drift.</summary>
    public sealed class CameraDrift : MonoBehaviour
    {
        [SerializeField] private Vector3 positionAmplitude = new Vector3(0.15f, 0.06f, 0f);
        [SerializeField] private Vector2 rotationAmplitude = new Vector2(0.6f, 1.2f); // pitch, yaw degrees
        [SerializeField, Min(0.001f)] private float speed = 0.12f;

        private Vector3 _startPos;
        private Quaternion _startRot;

        private void Awake()
        {
            _startPos = transform.localPosition;
            _startRot = transform.localRotation;
        }

        private void LateUpdate()
        {
            float t = Time.time * speed;
            float a = Mathf.Sin(t * Mathf.PI * 2f);
            float b = Mathf.Sin(t * Mathf.PI * 2f * 0.63f + 1.3f);
            transform.localPosition = _startPos + new Vector3(a * positionAmplitude.x, b * positionAmplitude.y, 0f);
            transform.localRotation = _startRot * Quaternion.Euler(b * rotationAmplitude.x, a * rotationAmplitude.y, 0f);
        }
    }
}
