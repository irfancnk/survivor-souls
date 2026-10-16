using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Boss
{
    /// <summary>
    /// KAN-204: renders lane telegraphs (the shipped <c>M_FX_TelegraphLane(Fill).mat</c>, TelegraphLane shader) straight
    /// to a texture, top-down, v (attacker 0 .. far end 1) = image bottom .. top. Used by <c>TelegraphLaneLookTests</c>
    /// (rune shape, tint palette) and by a close-up sheet: red / green / violet tints and a thin aim lane, at the wind-up,
    /// near the end and in the snap, on a snow-white ground.
    /// <c>Unity -batchmode -projectPath . -executeMethod SurvivorSouls.Editor.Boss.TelegraphLanePreview.RenderFromCommandLine -ss-out Docs/screenshots</c>
    /// (no -nographics) writes <c>kan204-lane-tints.png</c>.
    /// </summary>
    public static class TelegraphLanePreview
    {
        public const string FillMaterialPath = "Assets/_Project/Art/Boss/M_FX_TelegraphLaneFill.mat";
        public const string EdgeMaterialPath = "Assets/_Project/Art/Boss/M_FX_TelegraphLane.mat";

        /// <summary>Default red fill / edge (LineTelegraph serialized colours) and two test tints.</summary>
        public static readonly Color RedFill = new Color(0.5f, 0.02f, 0.01f, 0.72f);
        public static readonly Color RedEdge = new Color(0.62f, 0.02f, 0.01f, 1f);
        public static readonly Color VioletFill = new Color(0.36f, 0.08f, 0.62f, 0.72f);
        public static readonly Color VioletEdge = new Color(0.46f, 0.1f, 0.8f, 1f);
        public static readonly Color GreenFill = new Color(0.16f, 0.5f, 0.06f, 0.72f);
        public static readonly Color GreenEdge = new Color(0.22f, 0.66f, 0.08f, 1f);

        private static readonly Color Snow = new Color(0.86f, 0.88f, 0.9f, 1f);

        /// <summary>
        /// Renders one lane quad with <paramref name="material"/> into a premultiplied RGBA texture (cleared to 0),
        /// <paramref name="ppm"/> pixels per metre. Caller destroys the texture.
        /// </summary>
        public static Texture2D Render(Material material, Color baseColor, float span, float width, float progress,
            float snap, int ppm = 64)
        {
            int w = Mathf.Max(8, Mathf.RoundToInt(width * ppm)), h = Mathf.Max(8, Mathf.RoundToInt(span * ppm));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var mesh = FullScreenQuad();
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", baseColor);
            block.SetFloat("_Span", span);
            block.SetFloat("_LaneWidth", width);
            block.SetFloat("_Progress", progress);
            block.SetFloat("_Snap", snap);
            var cmd = new CommandBuffer { name = "Lane Preview" };
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(Matrix4x4.identity, false));
            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, block);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(mesh);
            return tex;
        }

        /// <summary>Clip-space quad: uv (0,0) bottom left .. (1,1) top right.</summary>
        private static Mesh FullScreenQuad()
        {
            var mesh = new Mesh { name = "Lane Preview Quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-1, -1, 0.5f), new Vector3(1, -1, 0.5f), new Vector3(-1, 1, 0.5f), new Vector3(1, 1, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            return mesh;
        }

        [MenuItem("Survivor Souls/Dev/Render Lane Telegraph Preview")]
        public static void RenderMenu() => RenderSheet("Docs/screenshots");

        public static void RenderFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-ss-out");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Docs/screenshots";
            RenderSheet(dir);
        }

        /// <summary>
        /// Columns: red 2 m charge lane, green 1.6 m, violet 1.6 m, red 0.5 m aim lane. Rows (bottom to top in the
        /// image, left to right in time): 45 % wind-up, 90 %, snap. Each cell = fill + edge over snow.
        /// </summary>
        public static string RenderSheet(string dir)
        {
            var fill = AssetDatabase.LoadAssetAtPath<Material>(FillMaterialPath);
            var edge = AssetDatabase.LoadAssetAtPath<Material>(EdgeMaterialPath);
            const int ppm = 48;
            const float span = 7f, gap = 0.6f;
            var lanes = new (Color fill, Color edge, float width)[]
            {
                (RedFill, RedEdge, 2f), (GreenFill, GreenEdge, 1.6f), (VioletFill, VioletEdge, 1.6f), (RedFill, RedEdge, 0.5f),
            };
            var stages = new (float progress, float snap)[] { (0.45f, 0f), (0.9f, 0f), (1f, 0.8f) };
            float sheetW = gap;
            foreach (var l in lanes) sheetW += l.width + gap;
            sheetW *= stages.Length;
            int W = Mathf.RoundToInt(sheetW * ppm), H = Mathf.RoundToInt((span + gap * 2f) * ppm);
            var sheet = new Color[W * H];
            for (int k = 0; k < sheet.Length; k++) sheet[k] = Snow.linear; // composite in linear (the shader's space)

            float x0 = gap;
            foreach (var s in stages)
            {
                foreach (var l in lanes)
                {
                    int ox = Mathf.RoundToInt(x0 * ppm), oy = Mathf.RoundToInt(gap * ppm);
                    // The fill quad covers the eased share of the lane (LineTelegraph), the edge the whole lane.
                    float eased = 1f - (1f - s.progress) * (1f - s.progress);
                    var f = Render(fill, l.fill, span * eased, l.width, s.progress, s.snap, ppm);
                    var e = Render(edge, l.edge, span, l.width, s.progress, s.snap, ppm);
                    Over(sheet, W, f, ox, oy);
                    Over(sheet, W, e, ox, oy);
                    Object.DestroyImmediate(f);
                    Object.DestroyImmediate(e);
                    x0 += l.width + gap;
                }
                x0 += gap;
            }

            for (int k = 0; k < sheet.Length; k++) sheet[k] = sheet[k].gamma; // PNG in sRGB like the screen
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false, true);
            tex.SetPixels(sheet);
            tex.Apply();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "kan204-lane-tints.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[KAN-204] lane preview: " + path);
            return path;
        }

        /// <summary>Premultiplied "over" of <paramref name="src"/> onto <paramref name="dst"/> at (ox, oy).</summary>
        private static void Over(Color[] dst, int dstW, Texture2D src, int ox, int oy)
        {
            var px = src.GetPixels();
            int dstH = dst.Length / dstW;
            for (int y = 0; y < src.height; y++)
            {
                int dy = oy + y;
                if (dy < 0 || dy >= dstH) continue;
                for (int x = 0; x < src.width; x++)
                {
                    int dx = ox + x;
                    if (dx < 0 || dx >= dstW) continue;
                    var s = px[y * src.width + x];
                    ref var d = ref dst[dy * dstW + dx];
                    d = new Color(s.r + d.r * (1f - s.a), s.g + d.g * (1f - s.a), s.b + d.b * (1f - s.a), 1f);
                }
            }
        }
    }
}
