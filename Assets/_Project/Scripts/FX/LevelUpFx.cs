using SurvivorSouls.Gameplay.Progression;
using UnityEngine;

namespace SurvivorSouls.FX
{
    /// <summary>
    /// Level-up celebration on the Knight (KAN-21): a bright flash at the chest, a light ring that sweeps out over the
    /// ground and a column of rising sparks. Plays on <see cref="LevelUpFlow.Started"/>, while the game is paused, so the
    /// particle systems run on unscaled time (set by the builder). No lights, no shadows; three world-space systems with
    /// emission off, fed by Emit.
    /// </summary>
    public sealed class LevelUpFx : MonoBehaviour
    {
        [SerializeField] private LevelUpFlow flow;
        [Tooltip("Horizontal ring billboard, grows over its lifetime (size curve set by the builder).")]
        [SerializeField] private ParticleSystem ring;
        [SerializeField] private ParticleSystem flash;
        [SerializeField] private ParticleSystem sparks;
        [SerializeField, Min(0)] private int sparkCount = 26;
        [SerializeField, Min(0f)] private float chestHeight = 1f;

        private ParticleSystem.EmitParams _emit;

        /// <summary>Times the burst played (tests).</summary>
        public int Plays { get; private set; }

        public void Setup(LevelUpFlow levelUpFlow)
        {
            if (isActiveAndEnabled && flow != null) flow.Started -= Play;
            flow = levelUpFlow;
            if (isActiveAndEnabled && flow != null) flow.Started += Play;
        }

        private void OnEnable()
        {
            if (flow != null) flow.Started += Play;
        }

        private void OnDisable()
        {
            if (flow != null) flow.Started -= Play;
        }

        private void Play(int level)
        {
            Plays++;
            Vector3 feet = transform.position;
            if (ring != null)
            {
                _emit = default;
                _emit.position = feet + Vector3.up * 0.06f;
                ring.Emit(_emit, 1);
            }
            if (flash != null)
            {
                _emit = default;
                _emit.position = feet + Vector3.up * chestHeight;
                flash.Emit(_emit, 1);
            }
            if (sparks != null)
            {
                for (int i = 0; i < sparkCount; i++)
                {
                    float a = i / (float)Mathf.Max(1, sparkCount) * Mathf.PI * 2f;
                    var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    _emit = default;
                    _emit.position = feet + outward * Random.Range(0.3f, 0.7f) + Vector3.up * Random.Range(0.1f, 0.6f);
                    _emit.velocity = Vector3.up * Random.Range(2.2f, 4.2f) + outward * Random.Range(0.3f, 1f);
                    _emit.applyShapeToPosition = false;
                    sparks.Emit(_emit, 1);
                }
            }
        }
    }
}
