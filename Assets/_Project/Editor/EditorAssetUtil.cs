using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
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
        /// (RGB = specular colour, A = smoothness). <paramref name="specular"/> false = URP "NoSpecular" (no highlight, cheaper
        /// pixel). Look per family (KAN-158): stone + props on, open ground + characters off.
        /// The result is already in the URP-validated state (<see cref="ValidateSimpleLit"/>), so opening or testing it changes nothing.
        /// </summary>
        public static Material SimpleLitMaterial(string path, Texture2D baseMap, Color tint,
            Texture2D normalMap = null, Texture2D specMap = null, float smoothness = 0.2f, Color? specColor = null,
            bool specular = true)
        {
            var mat = LoadOrCreateMaterial(path, SimpleLit);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", tint);

            mat.SetTexture("_BumpMap", normalMap);
            SetKeyword(mat, "_NORMALMAP", normalMap != null);

            mat.SetTexture("_SpecGlossMap", specMap);
            mat.SetFloat("_SmoothnessSource", 0f); // smoothness from specular alpha
            // URP SimpleLitGUI.SpecularSource: 0 = SpecularTextureAndColor, 1 = NoSpecular. Was always 1 with _SPECULAR_COLOR on,
            // so validation removed the highlight and the .mat changed in git (KAN-158).
            mat.SetFloat("_SpecularHighlights", specular ? 0f : 1f);
            mat.SetColor("_SpecColor", specColor ?? Color.white);
            mat.SetFloat("_Smoothness", smoothness);
            ValidateSimpleLit(mat);
            return mat;
        }

        /// <summary>
        /// Runs the same keyword / blend / queue pass as the URP Simple Lit inspector (<c>ShaderGUI.ValidateMaterial</c>), which the
        /// editor and test sessions run on Simple Lit materials. Call it after the last property change in a builder, so the builder
        /// output equals the validated state and a rebuild or test run leaves the .mat unchanged (KAN-157, KAN-158).
        /// </summary>
        public static void ValidateSimpleLit(Material mat)
        {
            BaseShaderGUI.SetMaterialKeywords(mat, SimpleLitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(mat);
        }

        /// <summary>
        /// Same pass as the URP inspector of the material's shader (Simple Lit, Unlit, Particles/Unlit). Other shaders: no change.
        /// A new material is written unvalidated; the next editor / test session validated it and the .mat changed in git (KAN-158).
        /// </summary>
        public static void ValidateLikeInspector(Material mat)
        {
            switch (mat.shader.name)
            {
                case SimpleLit: BaseShaderGUI.SetMaterialKeywords(mat, SimpleLitGUI.SetMaterialKeywords); break;
                case Unlit: BaseShaderGUI.SetMaterialKeywords(mat); break;
                case ParticlesUnlit: BaseShaderGUI.SetMaterialKeywords(mat, null, ParticleGUI.SetMaterialKeywords); break;
                default: return;
            }
            EditorUtility.SetDirty(mat);
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
            ValidateLikeInspector(mat); // KAN-158
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
            ValidateLikeInspector(mat); // KAN-158
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
