using System.Globalization;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.MiniBosses;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build screenshot helper for the mini boss (KAN-23), next to <see cref="DevScreenshot"/>:
    /// <c>-ss-miniboss &lt;after&gt;[,&lt;health share&gt;[,halt]]</c> arms the Game scene's <see cref="MiniBossDirector"/> in any
    /// Chamber: the warning shows <c>after</c> game seconds after load, the mini boss rises <see cref="MiniBossData.WarningTime"/>
    /// later; with a share below 1 its health drops to that share at the rise (the bar shows a wound); <c>halt</c> keeps
    /// it standing where it rose. Example: <c>-ss-chamber 2 -ss-miniboss 1,0.6</c>. Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class MiniBossShotArgs : MonoBehaviour
    {
        private float _after;
        private float _share = 1f;
        private bool _halt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-miniboss");
            if (string.IsNullOrEmpty(spec)) return;
            var go = new GameObject(nameof(MiniBossShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<MiniBossShotArgs>();
            string[] parts = spec.Split(',');
            args._after = float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float a) ? a : 1f;
            if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                args._share = Mathf.Clamp(s, 0.01f, 1f);
            args._halt = parts.Length > 2 && parts[2] == "halt";
            SceneManager.sceneLoaded += args.OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var director = FindAnyObjectByType<MiniBossDirector>();
            if (director == null) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            director.Spawned += OnSpawned;
            bool armed = director.Arm(_after);
            Debug.Log($"[MiniBossShotArgs] armed={armed} after={_after} share={_share} halt={_halt}");
        }

        private void OnSpawned(Enemy boss)
        {
            if (_share < 1f) boss.TakeDamage(boss.MaxHealth * (1f - _share), Vector3.forward, DamageKind.Burn);
            if (_halt) boss.Halted = true;
        }

        private static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
