using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Thin dark-red smoke wisps along the edges of attack warning lanes (KAN-167, part of KAN-160). One shared particle
    /// system for every <see cref="LineTelegraph"/> in the scene (boss charge, fan, beams, the ranged zombies' aim lanes):
    /// lanes call <see cref="Emit"/>, nothing else drives it (no emission module), so it costs one draw call while smoke
    /// is alive and nothing when no lane warns. No allocation per emit (one reused <see cref="ParticleSystem.EmitParams"/>).
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class LaneSmoke : MonoBehaviour
    {
        [SerializeField] private Color color = new Color(0.3f, 0.06f, 0.04f, 0.7f);
        [SerializeField, Min(0.05f)] private float sizeMin = 0.45f;
        [SerializeField, Min(0.05f)] private float sizeMax = 1f;
        [SerializeField, Min(0f)] private float rise = 0.55f;

        private ParticleSystem _ps;
        private ParticleSystem.EmitParams _params;

        /// <summary>The scene's smoke (null when the scene has none: lanes then just skip the smoke).</summary>
        public static LaneSmoke Active { get; private set; }
        /// <summary>Wisps emitted since the scene started (tests).</summary>
        public int Emitted { get; private set; }
        public ParticleSystem System => _ps != null ? _ps : _ps = GetComponent<ParticleSystem>();

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }

        /// <summary>One wisp at <paramref name="at"/> (ground point), a little bigger and denser with <paramref name="strength"/> 0..1.</summary>
        public void Emit(Vector3 at, float strength)
        {
            var ps = System;
            if (ps == null) return;
            _params.position = at;
            _params.velocity = new Vector3(Random.Range(-0.12f, 0.12f), rise * Random.Range(0.7f, 1.2f), Random.Range(-0.12f, 0.12f));
            _params.startSize = Mathf.Lerp(sizeMin, sizeMax, Mathf.Clamp01(strength) * Random.Range(0.6f, 1f));
            var c = color;
            c.a *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(strength));
            _params.startColor = c;
            _params.rotation = Random.Range(0f, 360f);
            ps.Emit(_params, 1);
            Emitted++;
        }
    }
}
