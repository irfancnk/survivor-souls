using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor
{
    /// <summary>Shared asset helpers for the scene/asset builders (folders, materials, mesh assets).</summary>
    public static class EditorAssetUtil
    {
        public const string SimpleLit = "Universal Render Pipeline/Simple Lit";
        public const string Unlit = "Universal Render Pipeline/Unlit";
        public const string ParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Loads the material at <paramref name="path"/> or creates it. Always (re)assigns the shader.</summary>
        public static Material LoadOrCreateMaterial(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader not found: {shaderName}");
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            return mat;
        }

        /// <summary>
        /// Simple Lit (Blinn-Phong): cheaper than Lit on mobile. Optional normal map and specular map
        /// (RGB = specular colour, A = smoothness).
        /// </summary>
        public static Material SimpleLitMaterial(string path, Texture2D baseMap, Color tint,
            Texture2D normalMap = null, Texture2D specMap = null, float smoothness = 0.2f, Color? specColor = null)
        {
            var mat = LoadOrCreateMaterial(path, SimpleLit);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", tint);

            mat.SetTexture("_BumpMap", normalMap);
            SetKeyword(mat, "_NORMALMAP", normalMap != null);

            mat.SetTexture("_SpecGlossMap", specMap);
            mat.SetFloat("_SmoothnessSource", 0f); // smoothness from specular alpha
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.SetColor("_SpecColor", specColor ?? Color.white);
            mat.SetFloat("_Smoothness", smoothness);
            SetKeyword(mat, "_SPECGLOSSMAP", specMap != null);
            SetKeyword(mat, "_SPECULAR_COLOR", specMap == null);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Alpha-blended material (shadow blobs, floor decals). Unlit or Simple Lit.</summary>
        public static Material TransparentMaterial(string path, string shaderName, Texture2D baseMap, Color color)
        {
            var mat = LoadOrCreateMaterial(path, shaderName);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Particle material. Additive for fire, alpha blend for mist.</summary>
        public static Material ParticleMaterial(string path, Texture2D texture, bool additive)
        {
            var mat = LoadOrCreateMaterial(path, ParticlesUnlit);
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Returns the mesh asset at <paramref name="path"/>, creating it if needed. Builders write into it,
        /// so scene/prefab references survive rebuilds.
        /// </summary>
        public static Mesh LoadOrCreateMesh(string path)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void SetKeyword(Material mat, string keyword, bool on)
        {
            if (on) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }
    }
}
