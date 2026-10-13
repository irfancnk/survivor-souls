using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Procedural
{
    /// <summary>
    /// Writes generated textures as PNG assets and sets import options.
    /// Also holds small tileable noise helpers used by the generators.
    /// </summary>
    public static class TextureFactory
    {
        public enum Kind { Color, Linear, NormalMap }

        /// <summary>Generates a PNG at <paramref name="path"/> (always overwrites) and returns the imported texture.</summary>
        public static Texture2D Write(string path, int width, int height, Func<int, int, Color> pixel, Kind kind,
            bool mipmaps = true, TextureWrapMode wrap = TextureWrapMode.Repeat, FilterMode filter = FilterMode.Bilinear,
            int maxSize = 1024, bool compress = true)
        {
            EditorAssetUtil.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, kind != Kind.Color);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = pixel(x, y);
            tex.SetPixels(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = kind == Kind.NormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = kind == Kind.Color;
            importer.mipmapEnabled = mipmaps;
            importer.wrapMode = wrap;
            importer.filterMode = filter;
            importer.anisoLevel = 1;
            importer.maxTextureSize = maxSize;
            // ASTC on mobile. Uncompressed only for small masks whose edges get sharpened in a shader (blocks would show).
            importer.textureCompression = compress ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Height field to tangent-space normal colour. <paramref name="height"/> is sampled with wrap.</summary>
        public static Color HeightToNormal(float[,] height, int x, int y, float strength)
        {
            int w = height.GetLength(0), h = height.GetLength(1);
            float l = height[(x - 1 + w) % w, y], r = height[(x + 1) % w, y];
            float d = height[x, (y - 1 + h) % h], u = height[x, (y + 1) % h];
            var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
            return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }

        // ------------------------------------------------------------ Noise

        /// <summary>GLSL-style smoothstep: 0 below edge0, 1 above edge1. (Mathf.SmoothStep is an interpolator, not this.)</summary>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Integer hash to [0,1).</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Tileable value noise. <paramref name="period"/> = cells per tile.</summary>
        public static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable fractal noise in [0,1]. u, v in [0,1) over the tile.</summary>
        public static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * period, v * period, period, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }
    }
}
