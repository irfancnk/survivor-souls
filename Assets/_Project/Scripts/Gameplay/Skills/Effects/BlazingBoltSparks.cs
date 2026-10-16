using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// Sparks shed by blazing bolts (KAN-200, Ember Bolts): every bolt in <see cref="Bolt.Blazing"/> throws a few hot sparks
    /// and fire flecks behind its head into ONE shared world-space particle system (<see cref="sparks"/>, Emit only): one
    /// draw call for any number of bolts. Hotter bolts (<see cref="Bolt.Heat"/>, Ember Bolts level) shed more and brighter
    /// sparks. Lives on the Ember Bolts effect prefab, so it exists only while the skill is owned. No allocation per frame.
    /// </summary>
    public sealed class BlazingBoltSparks : MonoBehaviour
    {
        [SerializeField] private ParticleSystem sparks;
        [Tooltip("Sparks per second per blazing bolt at the coolest (x) and hottest (y) heat.")]
        [SerializeField] private Vector2 sparkRate = new Vector2(18f, 40f);
        [Tooltip("Most sparks one bolt sheds in one frame (a hitch never dumps a burst).")]
        [SerializeField, Min(1)] private int maxPerBoltPerFrame = 3;

        private float _debt;

        /// <summary>Sparks emitted since this component woke (tests).</summary>
        public int Emitted { get; private set; }

        private void Update() => Emit(Time.deltaTime);

        /// <summary>Emits this frame's sparks for every blazing bolt in flight. Public for tests (Update calls it).</summary>
        public void Emit(float dt)
        {
            var bolts = Bolt.Blazing;
            if (sparks == null || bolts.Count == 0 || dt <= 0f)
            {
                _debt = 0f;
                return;
            }
            // One debt for all bolts, counted at the hottest rate; cooler bolts drop part of it (fewer sparks).
            _debt += sparkRate.y * dt;
            int budget = (int)_debt;
            if (budget <= 0) return;
            _debt -= budget;
            if (budget > maxPerBoltPerFrame) budget = maxPerBoltPerFrame;
            float coolShare = sparkRate.y > 0f ? sparkRate.x / sparkRate.y : 1f;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < bolts.Count; i++)
            {
                var bolt = bolts[i];
                float heat = bolt.Heat;
                int n = Mathf.Max(1, Mathf.RoundToInt(budget * Mathf.Lerp(coolShare, 1f, heat)));
                Vector3 head = bolt.transform.position;
                Vector3 dir = bolt.Direction;
                for (int k = 0; k < n; k++)
                {
                    // Just behind the head, thrown back and up a little, drifting off the line (reads as a fire trail).
                    Vector3 side = Random.insideUnitSphere * 0.08f;
                    p.position = head - dir * Random.Range(0.05f, 0.35f) + side;
                    p.velocity = -dir * Random.Range(1.5f, 3.5f) + side * 12f + Vector3.up * Random.Range(0.4f, 1.6f);
                    p.startSize = Random.Range(0.05f, 0.1f) * Mathf.Lerp(0.9f, 1.4f, heat);
                    p.startLifetime = Random.Range(0.16f, 0.3f);
                    p.startColor = new Color(1f, Mathf.Lerp(0.55f, 0.85f, heat * Random.value), Mathf.Lerp(0.12f, 0.4f, heat));
                    sparks.Emit(p, 1);
                }
                Emitted += n;
            }
        }
    }
}
