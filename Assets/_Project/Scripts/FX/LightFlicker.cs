using UnityEngine;

namespace SurvivorSouls.FX
{
    /// <summary>Cheap fire flicker: Perlin noise on light intensity and range. No allocations.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class LightFlicker : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float baseIntensity = 3f;
        [SerializeField, Min(0f)] private float intensityAmplitude = 0.8f;
        [SerializeField, Min(0f)] private float baseRange = 8f;
        [SerializeField, Min(0f)] private float rangeAmplitude = 0.6f;
        [SerializeField, Min(0.01f)] private float speed = 3.5f;

        private Light _light;
        private float _seed;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _seed = Random.value * 100f;
        }

        private void Update()
        {
            float n = Mathf.PerlinNoise(_seed, Time.time * speed) * 2f - 1f;
            _light.intensity = baseIntensity + n * intensityAmplitude;
            _light.range = baseRange + n * rangeAmplitude;
        }

        public void Configure(float intensity, float intensityAmp, float range, float rangeAmp)
        {
            baseIntensity = intensity;
            intensityAmplitude = intensityAmp;
            baseRange = range;
            rangeAmplitude = rangeAmp;
        }
    }
}
