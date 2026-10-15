using System.Linq;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-146 close-up check: the Plague Bloater prefab frozen at <see cref="Times"/> of its Burst clip (the 1 s swell, then
    /// two later frames) with the swell scale of <see cref="BloaterBurst"/>, from the gameplay camera angle (top row) and from
    /// the side (bottom row), skinned with 2 bones per vertex like the Mobile quality. One PNG:
    /// <c>kan146_bloater_swell_&lt;tag&gt;.png</c>. <c>-ss-times 0.2,0.4</c> picks other clip seconds.
    /// Needs a graphics device: run batch mode without -nographics.
    /// <c>Unity -batchmode -projectPath . -executeMethod SurvivorSouls.Editor.Enemies.BloaterSwellShots.RenderFromCommandLine -ss-out Docs/screenshots -ss-tag after</c>
    /// </summary>
    public static class BloaterSwellShots
    {
        /// <summary>Clip seconds of the shots: the swell is the first 1 s (Burst clip at speed 1).</summary>
        public static readonly float[] Times = { 0.25f, 0.5f, 0.75f, 0.95f, 1.4f, 2.2f };

        [MenuItem("Survivor Souls/Enemies/Render Bloater Swell Close-ups")]
        public static void RenderFromMenu() => Render("Docs/screenshots", "editor");

        public static void RenderFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            string times = Arg(args, "-ss-times"); // optional: comma list of clip seconds (default Times)
            float[] at = times?.Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Render(Arg(args, "-ss-out") ?? "Docs/screenshots", Arg(args, "-ss-tag") ?? "now", at);
        }

        private static string Arg(string[] args, string name)
        {
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static void Render(string dir, string tag, float[] times = null)
        {
            times ??= Times;
            System.IO.Directory.CreateDirectory(dir);
            var spec = EnemyRosterBuilder.Find(SpecialMeleeBuilder.Bloater);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // unloads unused assets: load after it
            var prefab = AssetDatabase.LoadAssetAtPath<Enemy>(spec.PrefabPath);
            if (prefab == null) throw new System.InvalidOperationException($"{spec.PrefabPath} missing: run the roster builder first");
            var clip = AssetDatabase.LoadAllAssetsAtPath(spec.ClipPath("Burst")).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.47f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")) { color = new Color(0.62f, 0.64f, 0.68f) };
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            var enemy = (Enemy)PrefabUtility.InstantiatePrefab(prefab);
            enemy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f)); // faces -Z, toward the camera
            float baseScale = enemy.transform.localScale.x;
            enemy.Animator.enabled = false; // the clip is sampled by hand; a live Animator would pose it too
            var swell = SpecialMeleeBuilder.BloaterSpec;

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            cam.nearClipPlane = 0.1f;

            const int cell = 420, cellH = 480;
            int cols = times.Length;
            var sheet = new Texture2D(cell * cols, cellH * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(cell, cellH, 24, GraphicsFormat.R8G8B8A8_SRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            foreach (var skin in enemy.GetComponentsInChildren<SkinnedMeshRenderer>())
                skin.quality = SkinQuality.Bone2; // Mobile quality skins with 2 bones (QualitySettings untouched: no asset churn)
            var tile = new Texture2D(cell, cellH, TextureFormat.RGB24, false);
            var log = new System.Text.StringBuilder($"[BloaterSwellShots] {tag}: clip {clip.length:F2} s, swell scale {swell.swellScale}");
            try
            {
                for (int row = 0; row < 2; row++)
                {
                    // row 0 = gameplay camera (high, from the front, as the Knight sees a Bloater walking at it); row 1 = side
                    Vector3 view = row == 0 ? new Vector3(0.2f, 1.25f, -1f).normalized : new Vector3(-1f, 0.3f, -0.35f).normalized;
                    for (int c = 0; c < cols; c++)
                    {
                        float t = times[c];
                        float p = Mathf.Clamp01(t / swell.swell);
                        float s = baseScale * Mathf.Lerp(1f, swell.swellScale, p * p);
                        enemy.transform.localScale = new Vector3(s, s, s);
                        clip.SampleAnimation(enemy.Animator.gameObject, t);
                        Vector3 look = new Vector3(0f, 0.85f * s / baseScale, 0f);
                        cam.transform.position = look + view * 6.2f;
                        cam.transform.LookAt(look);
                        cam.Render(); // warm-up: textures loaded this frame are not all on the GPU yet
                        cam.Render();
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        tile.ReadPixels(new Rect(0, 0, cell, cellH), 0, 0);
                        tile.Apply();
                        RenderTexture.active = prev;
                        sheet.SetPixels(c * cell, (1 - row) * cellH, cell, cellH, tile.GetPixels());
                        if (row == 0) log.Append($", t {t:F2} scale {s / baseScale:F2}");
                    }
                }
                sheet.Apply();
                string path = $"{dir}/kan146_bloater_swell_{tag}.png";
                System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
                Debug.Log(log.Append($" -> {path}").ToString());
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(groundMat);
            }
        }
    }
}
