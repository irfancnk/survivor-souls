using System.IO;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-109: bakes an emission map from a Meshy base colour texture whose lava cracks are painted in (The Pyre Lord).
    /// Only the hot pixels glow: bright red with some green (orange / yellow cracks); dark red cloth and black armour stay
    /// unlit. The map is written next to the base colour as <c>T_&lt;Id&gt;_Emission.jpg</c>, which
    /// <see cref="EnemyRosterBuilder"/> already wires into the material (white emission colour). Written once: delete the
    /// file to bake it again.
    /// </summary>
    public static class LavaEmissionBaker
    {
        private const int Quality = 90;

        /// <summary>Writes <paramref name="emissionPath"/> from <paramref name="baseColorPath"/> when it is missing. True = written.</summary>
        public static bool Ensure(string baseColorPath, string emissionPath)
        {
            if (File.Exists(emissionPath) || !File.Exists(baseColorPath)) return false;
            var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
            source.LoadImage(File.ReadAllBytes(baseColorPath)); // the file, not the imported asset: no read/write flag needed
            var pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Glow(pixels[i]);
            var output = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            output.SetPixels32(pixels);
            output.Apply();
            File.WriteAllBytes(emissionPath, output.EncodeToJPG(Quality));
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(emissionPath);
            Debug.Log($"[LavaEmissionBaker] {emissionPath} baked from {baseColorPath}");
            return true;
        }

        /// <summary>Heat of one pixel (0..1) times its own colour, pushed towards orange-white.</summary>
        public static Color32 Glow(Color32 c)
        {
            float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
            float heat = Heat(r, g, b);
            if (heat <= 0f) return new Color32(0, 0, 0, 255);
            var hot = new Color(Mathf.Min(1f, r * 1.15f), Mathf.Min(1f, g * 1.1f), b * 0.6f) * heat;
            return new Color32((byte)(hot.r * 255f), (byte)(hot.g * 255f), (byte)(hot.b * 255f), 255);
        }

        /// <summary>
        /// Lava test: bright red channel, some green (orange or yellow, not the dark red cloth), little blue.
        /// </summary>
        public static float Heat(float r, float g, float b)
        {
            if (r < b * 1.8f) return 0f;
            return Smooth(0.5f, 0.8f, r) * Smooth(0.14f, 0.4f, g);
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
