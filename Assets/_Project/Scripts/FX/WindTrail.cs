using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.FX
{
    /// <summary>
    /// Wind behind the running Knight (KAN-14): thin pale streaks around the body and a few dust / leaf specks near the
    /// feet, streaming back against the move direction. One world-space particle system that never emits on its own;
    /// this script feeds it with <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>, so each particle
    /// gets its own spawn point and velocity from the current move direction (a quick turn bends the trail instead of
    /// swinging it) and nothing is allocated per frame.
    /// <para>
    /// Intensity follows speed: zero below <see cref="startSpeed"/> (standing, attacking, a slow stick drift), full at
    /// <see cref="fullSpeed"/>. It rises fast and falls slower, and live particles finish their fade, so a stop never
    /// cuts the effect hard. Particle budget is the system's <c>maxParticles</c> (30, set by WindTrailBuilder).
    /// </para>
    /// </summary>
    public sealed class WindTrail : MonoBehaviour
    {
        [SerializeField] private PlayerMover mover;
        [SerializeField] private ParticleSystem system;

        [Header("Intensity")]
        [Tooltip("Planar speed (m/s) where wind starts. Below it nothing emits.")]
        [SerializeField, Min(0f)] private float startSpeed = 2.2f;
        [Tooltip("Planar speed (m/s) with full emission (player max speed).")]
        [SerializeField, Min(0.1f)] private float fullSpeed = 5f;
        [Tooltip("Intensity gain per second while speeding up.")]
        [SerializeField, Min(0.1f)] private float rampUp = 7f;
        [Tooltip("Intensity loss per second after a stop (lower = longer tail-off).")]
        [SerializeField, Min(0.1f)] private float rampDown = 3.5f;
        [Tooltip("Upper bound of particles emitted in one frame (protects against a burst after a frame hitch).")]
        [SerializeField, Min(1)] private int maxEmitPerFrame = 3;

        [Header("Streaks (around the body)")]
        [SerializeField, Min(0f)] private float streakRate = 26f; // per second at full intensity
        [SerializeField] private Vector2 streakLifetime = new Vector2(0.28f, 0.42f);
        [SerializeField] private Vector2 streakWidth = new Vector2(0.06f, 0.1f);
        [SerializeField] private Vector2 streakDrift = new Vector2(2.2f, 3.4f); // m/s backwards, world space
        [SerializeField] private Vector2 streakHeight = new Vector2(0.45f, 1.7f);
        [SerializeField, Min(0f)] private float streakSideSpread = 0.42f;
        [SerializeField] private Vector2 streakBackOffset = new Vector2(0.15f, 0.45f);
        [SerializeField] private Color streakColor = new Color(0.95f, 0.97f, 1f, 0.5f);

        [Header("Specks (dust and leaves at the feet)")]
        [SerializeField, Min(0f)] private float speckRate = 9f;
        [SerializeField] private Vector2 speckLifetime = new Vector2(0.45f, 0.75f);
        [SerializeField] private Vector2 speckSize = new Vector2(0.05f, 0.09f);
        [SerializeField] private Vector2 speckDrift = new Vector2(0.4f, 0.9f);
        [SerializeField] private Vector2 speckLift = new Vector2(0.25f, 0.6f); // m/s up
        [SerializeField] private Vector2 speckHeight = new Vector2(0.05f, 0.35f);
        [SerializeField, Min(0f)] private float speckSideSpread = 0.3f;
        [SerializeField] private Color dustColor = new Color(0.84f, 0.8f, 0.68f, 0.65f);
        [SerializeField] private Color leafColor = new Color(0.62f, 0.68f, 0.38f, 0.7f);
        [SerializeField, Range(0f, 1f)] private float leafChance = 0.3f;

        private ParticleSystem.EmitParams _params;
        private Vector3 _direction = Vector3.forward; // last planar move direction
        private float _intensity;
        private float _streakDebt;
        private float _speckDebt;

        /// <summary>0..1 emission strength after easing. 0 = no new particles.</summary>
        public float Intensity => _intensity;

        /// <summary>Particles emitted since enable (tests and profiling).</summary>
        public int EmittedTotal { get; private set; }

        public ParticleSystem System => system;

        /// <summary>Wiring for code-built objects and tests (the builder sets the same fields in the scene).</summary>
        public void Setup(PlayerMover source, ParticleSystem particles)
        {
            mover = source;
            system = particles;
        }

        private void OnEnable()
        {
            _intensity = 0f;
            _streakDebt = _speckDebt = 0f;
            EmittedTotal = 0;
        }

        private void LateUpdate()
        {
            if (mover == null) return;
            Tick(mover.Velocity, Time.deltaTime);
        }

        /// <summary>
        /// One step: eases intensity toward the speed target, then emits the streaks and specks owed for this step.
        /// Public so tests can drive it with a fixed velocity and time step.
        /// </summary>
        public void Tick(Vector3 velocity, float deltaTime)
        {
            if (system == null || deltaTime <= 0f) return;

            velocity.y = 0f;
            float speed = velocity.magnitude;
            if (speed > 0.1f) _direction = velocity / speed;

            float target = TargetIntensity(speed, startSpeed, fullSpeed);
            float rate = target > _intensity ? rampUp : rampDown;
            _intensity = Mathf.MoveTowards(_intensity, target, rate * deltaTime);
            if (_intensity <= 0f)
            {
                _streakDebt = _speckDebt = 0f;
                return;
            }

            int budget = maxEmitPerFrame;
            _streakDebt += streakRate * _intensity * deltaTime;
            _speckDebt += speckRate * _intensity * deltaTime;
            while (_streakDebt >= 1f && budget-- > 0)
            {
                _streakDebt -= 1f;
                EmitStreak();
            }
            while (_speckDebt >= 1f && budget-- > 0)
            {
                _speckDebt -= 1f;
                EmitSpeck();
            }
            // Never carry a backlog into later frames: a dropped particle is better than a burst.
            _streakDebt = Mathf.Min(_streakDebt, 1f);
            _speckDebt = Mathf.Min(_speckDebt, 1f);
        }

        /// <summary>Speed (m/s) to 0..1 emission target: 0 below <paramref name="start"/>, smooth rise to 1 at <paramref name="full"/>.</summary>
        public static float TargetIntensity(float speed, float start, float full)
        {
            if (speed <= start) return 0f;
            float t = Mathf.Clamp01((speed - start) / Mathf.Max(1e-3f, full - start));
            return t * t * (3f - 2f * t);
        }

        private void EmitStreak()
        {
            Vector3 side = new Vector3(_direction.z, 0f, -_direction.x);
            Vector3 origin = transform.position
                - _direction * Random.Range(streakBackOffset.x, streakBackOffset.y)
                + side * Random.Range(-streakSideSpread, streakSideSpread)
                + Vector3.up * Random.Range(streakHeight.x, streakHeight.y);
            Vector3 drift = -_direction * Random.Range(streakDrift.x, streakDrift.y)
                + side * Random.Range(-0.2f, 0.2f);

            Color c = streakColor;
            c.a *= Mathf.Lerp(0.55f, 1f, _intensity);
            Emit(origin, drift, Random.Range(streakWidth.x, streakWidth.y), Random.Range(streakLifetime.x, streakLifetime.y), c);
        }

        private void EmitSpeck()
        {
            Vector3 side = new Vector3(_direction.z, 0f, -_direction.x);
            Vector3 origin = transform.position
                - _direction * Random.Range(0.1f, 0.3f)
                + side * Random.Range(-speckSideSpread, speckSideSpread)
                + Vector3.up * Random.Range(speckHeight.x, speckHeight.y);
            Vector3 drift = -_direction * Random.Range(speckDrift.x, speckDrift.y)
                + side * Random.Range(-0.3f, 0.3f)
                + Vector3.up * Random.Range(speckLift.x, speckLift.y);

            Color c = Random.value < leafChance ? leafColor : dustColor;
            Emit(origin, drift, Random.Range(speckSize.x, speckSize.y), Random.Range(speckLifetime.x, speckLifetime.y), c);
        }

        private void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            _params.position = position;
            _params.velocity = velocity;
            _params.startSize = size;
            _params.startLifetime = lifetime;
            _params.startColor = color;
            system.Emit(_params, 1);
            EmittedTotal++;
        }
    }
}
