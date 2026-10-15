using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Shared Hexbound Shaman effects (KAN-107): a violet puff (<see cref="Puff"/>: blink both ends, hex, raise) and the small
    /// violet priority marker over every living Shaman (<see cref="HexShaman.Active"/>). The puff is one world-space particle
    /// system burst with <see cref="ParticleSystem.Emit(int)"/>; the markers are billboards of ONE particle system filled with
    /// <see cref="ParticleSystem.SetParticles(ParticleSystem.Particle[], int)"/> each frame (one draw call for any count, no
    /// lights, no allocation). On the "Enemy Shots" object (ShamanBuilder).
    /// </summary>
    public sealed class HexFx : MonoBehaviour
    {
        public const int MaxMarkers = 16;

        /// <summary>The scene's Shaman effects. Set while enabled.</summary>
        public static HexFx Current { get; private set; }

        [SerializeField] private ParticleSystem puff;
        [SerializeField, Min(0)] private int puffCount = 22;
        [SerializeField] private ParticleSystem markers;
        [SerializeField, Min(0.05f)] private float markerSize = 0.5f;
        [SerializeField] private Color markerColor = new Color(0.8f, 0.5f, 1f, 1f);

        private readonly ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[MaxMarkers];
        private int _drawn = -1;

        /// <summary>Puffs since the scene started (tests).</summary>
        public int Puffs { get; private set; }
        /// <summary>Markers drawn in the last frame.</summary>
        public int MarkerCount => Mathf.Max(0, _drawn);

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(ParticleSystem puffSystem, ParticleSystem markerSystem)
        {
            puff = puffSystem;
            markers = markerSystem;
        }

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        /// <summary>A violet puff of smoke and sparks rising from <paramref name="ground"/>.</summary>
        public void Puff(Vector3 ground)
        {
            Puffs++;
            if (puff == null || puffCount <= 0) return;
            puff.transform.position = ground + Vector3.up * 0.4f;
            puff.Emit(puffCount);
        }

        private void LateUpdate() => DrawMarkers(Time.time);

        /// <summary>One pulsing marker over each living Shaman. Public for tests.</summary>
        public void DrawMarkers(float time)
        {
            int n = 0;
            var list = HexShaman.Active;
            for (int i = 0; i < list.Count && n < MaxMarkers; i++)
            {
                var s = list[i];
                if (s == null || !s.Enemy.IsAlive) continue;
                ref var p = ref _buffer[n++];
                float pulse = 1f + 0.12f * Mathf.Sin(time * 5f + i * 1.3f);
                p.position = s.MarkerPoint + Vector3.up * (0.06f * Mathf.Sin(time * 2.5f + i));
                p.velocity = Vector3.zero;
                p.rotation = 0f;
                p.startSize = markerSize * pulse * s.VisibleShare;
                p.startColor = markerColor;
                p.startLifetime = 1000f;
                p.remainingLifetime = 1000f;
            }
            if (n == 0 && _drawn == 0) return;
            _drawn = n;
            if (markers != null) markers.SetParticles(_buffer, n);
        }
    }
}
