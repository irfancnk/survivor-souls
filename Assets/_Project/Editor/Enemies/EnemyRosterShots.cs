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
    /// KAN-104 size check: renders the built roster prefabs in a row next to the Knight (left) in a throwaway scene, each
    /// frozen in its Attack clip. Three PNGs: melee tiers, special zombies, bosses (bosses should be 2.3-2.9x the Knight).
    /// Needs a graphics device: run batch mode without -nographics.
    /// <c>Unity -batchmode -projectPath . -executeMethod SurvivorSouls.Editor.Enemies.EnemyRosterShots.RenderFromCommandLine -ss-out Docs/screenshots</c>
    /// </summary>
    public static class EnemyRosterShots
    {
        private const string KnightPrefab = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";

        [MenuItem("Survivor Souls/Enemies/Render Roster Lineups")]
        public static void RenderFromMenu() => Render("Docs/screenshots");

        public static void RenderFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-ss-out");
            Render(i >= 0 && i + 1 < args.Length ? args[i + 1] : "Docs/screenshots");
        }

        public static void Render(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var roster = EnemyRosterBuilder.Roster;
            Lineup($"{dir}/kan104_roster_melee_tiers.png", roster.Take(4).ToArray(), 1.4f);
            Lineup($"{dir}/kan104_roster_specials.png", roster.Skip(4).Where(s => !s.Boss).ToArray(), 1.4f);
            Lineup($"{dir}/kan104_roster_bosses.png", roster.Where(s => s.Boss).ToArray(), 0.6f);
        }

        private static void Lineup(string path, EnemyRosterBuilder.Spec[] specs, float gap)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.47f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")) { color = new Color(0.25f, 0.27f, 0.22f) };
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            var knight = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KnightPrefab));
            float x = 0f, top = 1.8f;
            var log = new System.Text.StringBuilder($"[EnemyRosterShots] {System.IO.Path.GetFileName(path)}: Knight 1.75 m");
            foreach (var spec in specs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<Enemy>(spec.PrefabPath);
                if (prefab == null) throw new System.InvalidOperationException($"{spec.PrefabPath} missing: run the roster builder first");
                float radius = Mathf.Max(0.45f, prefab.HitRadius);
                x += (x == 0f ? 0.6f : 0f) + radius + gap * 0.5f;
                var enemy = (Enemy)PrefabUtility.InstantiatePrefab(prefab);
                enemy.transform.SetPositionAndRotation(new Vector3(x, 0f, 0f), Quaternion.Euler(0f, 200f, 0f));
                var attack = AssetDatabase.LoadAllAssetsAtPath(spec.ClipPath(spec.Attack)).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                attack.SampleAnimation(enemy.Animator.gameObject, attack.length * 0.4f);
                x += radius + gap * 0.5f;
                top = Mathf.Max(top, prefab.Height);
                log.Append($", {spec.Id} {prefab.Height:F2} m ({prefab.Height / EnemyRosterBuilder.KnightHeight:F2}x)");
            }
            knight.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.fieldOfView = 28f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            float width = x + 1f, centre = (x - 0.5f) * 0.5f;
            const int w = 1800, h = 900;
            float aspect = (float)w / h;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = Mathf.Max(top * 0.62f / Mathf.Tan(halfFov), width * 0.56f / (Mathf.Tan(halfFov) * aspect));
            cam.transform.position = new Vector3(centre, top * 0.55f + dist * 0.16f, -dist);
            cam.transform.LookAt(new Vector3(centre, top * 0.45f, 0f));
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = dist * 3f;

            var rt = new RenderTexture(w, h, 24, GraphicsFormat.R8G8B8A8_SRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render(); // warm-up: textures loaded this frame are not all on the GPU yet (first frame shows wrong UV islands)
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(groundMat);
            Debug.Log(log.ToString());
        }
    }
}
