using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// All look knobs of one Realm in one asset: snow, sea, sky, light, weather and prop density.
    /// The editor WorldBuilder / GameSceneBuilder read it and write materials, lights and particles.
    /// Edit the asset, then run "Survivor Souls/Rebuild Game Scene". Realm 1 = snowy seaside shore.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Realm Look", fileName = "RealmLook")]
    public sealed class RealmLook : ScriptableObject
    {
        [Header("Snow ground")]
        public Color freshSnow = new Color(0.86f, 0.89f, 0.93f);
        public Color packedSnow = new Color(0.76f, 0.8f, 0.86f);
        public Color scouredGround = new Color(0.68f, 0.68f, 0.68f);
        [Range(0f, 1f)] public float scouredAmount = 0.2f;
        [Range(0f, 2f)] public float sparkle = 0.55f;
        [Range(0f, 2f)] public float snowNormalStrength = 0.6f;

        [Header("Snow on props")]
        public Color snowCap = new Color(0.86f, 0.89f, 0.93f);
        [Range(0f, 1f)] public float snowCapLevel = 0.7f;

        [Header("Sea")]
        public Color shallowWater = new Color(0.22f, 0.4f, 0.42f);
        public Color deepWater = new Color(0.09f, 0.15f, 0.19f);
        public Color seaSky = new Color(0.66f, 0.72f, 0.79f);
        public Color foam = new Color(0.9f, 0.93f, 0.95f);
        [Range(0f, 0.5f)] public float waveHeight = 0.12f;
        [Min(1f)] public float waveLength = 9f;
        [Min(0f)] public float waveSpeed = 1.1f;
        [Min(1f)] public float depthRange = 18f;

        [Header("Sky, light, fog")]
        public Color skyZenith = new Color(0.5f, 0.56f, 0.64f);
        public Color skyHorizon = new Color(0.78f, 0.81f, 0.85f);
        public Color sunGlow = new Color(1f, 0.8f, 0.6f);
        public Color sunColor = new Color(0.94f, 0.95f, 1f);
        [Min(0f)] public float sunIntensity = 0.95f;
        public Color ambientSky = new Color(0.56f, 0.62f, 0.72f);
        public Color ambientEquator = new Color(0.5f, 0.53f, 0.58f);
        public Color ambientGround = new Color(0.4f, 0.42f, 0.46f);
        public Color fogColor = new Color(0.7f, 0.75f, 0.8f);
        [Min(0f)] public float fogStart = 30f;
        [Min(0f)] public float fogEnd = 85f;

        [Header("Weather")]
        [Min(0f)] public float snowfallRate = 22f;
        [Min(0)] public int snowfallMax = 110;
        [Range(0f, 1f)] public float seaMistAlpha = 0.16f;

        [Header("Props")]
        [Range(0.25f, 2f)] public float propDensity = 1f;
    }
}
