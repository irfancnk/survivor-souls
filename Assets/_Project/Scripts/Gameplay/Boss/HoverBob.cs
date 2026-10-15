using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// KAN-111: a floating body (the Bone Matriarch). Lifts the model it sits on <see cref="lift"/> m off the ground and bobs
    /// it <see cref="amplitude"/> m up and down every <see cref="period"/> s. Sits on the model child, not the enemy root: the
    /// root (movement, hit tests, blob shadow) stays on the ground, so the shadow reads as a gap under the gown. Scaled
    /// time: it holds still while the game is paused. No allocations.
    /// </summary>
    public sealed class HoverBob : MonoBehaviour
    {
        [Tooltip("Metres the model floats above the ground at the bottom of the bob.")]
        [SerializeField, Min(0f)] private float lift = 0.3f;
        [Tooltip("Metres of the up-down bob.")]
        [SerializeField, Min(0f)] private float amplitude = 0.12f;
        [Tooltip("Seconds of one full bob.")]
        [SerializeField, Min(0.1f)] private float period = 2.6f;

        private Vector3 _base;
        private float _phase;

        public float Lift => lift;
        public float Amplitude => amplitude;
        public float Period => period;

        /// <summary>Height of the model above its rest point now (m).</summary>
        public float Offset => transform.localPosition.y - _base.y;

        /// <summary>Builder and tests: the hover numbers.</summary>
        public void Configure(float liftMetres, float bobMetres, float bobSeconds)
        {
            lift = Mathf.Max(0f, liftMetres);
            amplitude = Mathf.Max(0f, bobMetres);
            period = Mathf.Max(0.1f, bobSeconds);
        }

        private void Awake() => _base = transform.localPosition;

        private void OnEnable() => Tick(0f);

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step of <paramref name="dt"/> scaled seconds. Public for tests.</summary>
        public void Tick(float dt)
        {
            _phase = Mathf.Repeat(_phase + dt / period, 1f);
            float bob = (1f - Mathf.Cos(_phase * 2f * Mathf.PI)) * 0.5f; // 0 at the bottom, 1 at the top
            transform.localPosition = _base + Vector3.up * (lift + amplitude * bob);
        }
    }
}
