using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>Bobs and spins a marker (e.g. the gold arrow above the exit door).</summary>
    public sealed class FloatBob : MonoBehaviour
    {
        [SerializeField] private float height = 0.25f;
        [SerializeField] private float speed = 2.5f;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private Vector3 _base;

        private void OnEnable() => _base = transform.localPosition;

        private void Update()
        {
            transform.localPosition = _base + Vector3.up * (Mathf.Sin(Time.time * speed) * height);
            transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
        }
    }
}
