using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Peak limiter on the final mix (KAN-236, KAN-81.3): sits on the <see cref="AudioListener"/>, so it sees EVERY sound
    /// (mixer groups and sources that are not routed to the mixer). Instant attack (no sample over <see cref="ceiling"/>),
    /// smooth release. A heavy fight (10 shots + 20 hits per second, a mass death, a boss slam) never clips the output.
    /// Runs on the audio thread: no GC, no Unity API inside <see cref="OnAudioFilterRead"/>.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class MasterLimiter : MonoBehaviour
    {
        [Tooltip("Highest output sample (0.89 = -1 dBFS).")]
        [SerializeField, Range(0.5f, 1f)] private float ceiling = 0.89f;
        [Tooltip("Seconds for the gain to come most of the way back after a peak.")]
        [SerializeField, Min(0.005f)] private float release = 0.12f;

        private float _gain = 1f;
        private float _releaseCoef;
        private volatile float _lowest = 1f; // lowest gain since the last read (sound board)
        private float _sessionLowest = 1f;

        public float Ceiling => ceiling;

        /// <summary>Lowest gain since the last call, in dB (0 = untouched). Main thread (sound board, dev log).</summary>
        public float TakeGainReductionDb()
        {
            float g = _lowest;
            _lowest = 1f;
            if (g < _sessionLowest) _sessionLowest = g;
            return 20f * Mathf.Log10(Mathf.Max(1e-4f, g));
        }

        private void Awake() => _releaseCoef = ReleaseCoefficient(release, AudioSettings.outputSampleRate);

        /// <summary>Per-sample release factor for <paramref name="seconds"/> at <paramref name="rate"/> Hz.</summary>
        public static float ReleaseCoefficient(float seconds, int rate) =>
            Mathf.Exp(-1f / (Mathf.Max(0.001f, seconds) * Mathf.Max(8000, rate)));

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float low = Process(data, channels, ceiling, _releaseCoef, ref _gain);
            if (low < _lowest) _lowest = low;
        }

        // Dev builds: the deepest gain reduction of the session at quit (KAN-236 check on a Mac / phone dev run).
        private void OnDestroy()
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            TakeGainReductionDb();
            Debug.Log($"[MasterLimiter] deepest gain reduction {20f * Mathf.Log10(Mathf.Max(1e-4f, _sessionLowest)):0.0} dB (ceiling {ceiling:0.00})");
        }

        /// <summary>
        /// Limits interleaved <paramref name="data"/> in place so no sample is above <paramref name="ceiling"/>. One gain for all
        /// channels of a frame (no image shift). Returns the lowest gain used. Pure (tests run it on a mixed buffer).
        /// </summary>
        public static float Process(float[] data, int channels, float ceiling, float releaseCoef, ref float gain)
        {
            if (channels < 1) channels = 1;
            float lowest = gain;
            for (int i = 0; i + channels <= data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float a = data[i + c] < 0f ? -data[i + c] : data[i + c];
                    if (a > peak) peak = a;
                }
                float target = peak > ceiling ? ceiling / peak : 1f;
                gain = target < gain ? target : target + (gain - target) * releaseCoef;
                if (gain < lowest) lowest = gain;
                for (int c = 0; c < channels; c++) data[i + c] *= gain;
            }
            return lowest;
        }
    }
}
