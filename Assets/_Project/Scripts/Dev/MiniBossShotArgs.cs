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
    /// KAN-212: <c>-ss-mb-attack slam|charge[,&lt;wind-up share&gt;]</c> sets the special attack kind, starts the first attack
    /// at the rise and freezes time (<c>Time.timeScale</c> 0) when its warning has filled to that share (no share = no freeze).
    /// </summary>
    public sealed class MiniBossShotArgs : MonoBehaviour
    {
        private float _after;
        private float _share = 1f;
        private bool _halt;
        private MiniBossAttackKind _attack = MiniBossAttackKind.None;
        private float _freezeAt = -1f;
        private MiniBossAttack _loop;

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
            string attack = GetArg("-ss-mb-attack");
            if (!string.IsNullOrEmpty(attack))
            {
                string[] ap = attack.Split(',');
                args._attack = ap[0] == "slam" ? MiniBossAttackKind.Slam : ap[0] == "charge" ? MiniBossAttackKind.Charge : MiniBossAttackKind.None;
                if (ap.Length > 1 && float.TryParse(ap[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) args._freezeAt = Mathf.Clamp01(f);
            }
            SceneManager.sceneLoaded += args.OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var director = FindAnyObjectByType<MiniBossDirector>();
            if (director == null) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            director.Spawned += OnSpawned;
            if (_attack != MiniBossAttackKind.None && director.Data != null)
            {
                var d = director.Data; // runtime copy of the asset in a player: never saved
                d.WithAttack(_attack, 99f, d.AttackWindUp, d.AttackCooldown, 0f, d.AttackDamage, d.AttackRecover, d.SlamRadius,
                    d.ChargeLength, d.ChargeWidth, d.ChargeTime);
                _loop = director.Attack;
            }
            bool armed = director.Arm(_after);
            Debug.Log($"[MiniBossShotArgs] armed={armed} after={_after} share={_share} halt={_halt}");
        }

        private void OnSpawned(Enemy boss)
        {
            if (_share < 1f) boss.TakeDamage(boss.MaxHealth * (1f - _share), Vector3.forward, DamageKind.Burn);
            if (_halt) boss.Halted = true;
        }

        private void Update()
        {
            if (_loop == null || _freezeAt < 0f) return;
            if (_loop.State == MiniBossAttack.Phase.WindUp && _loop.Progress >= _freezeAt)
            {
                Time.timeScale = 0f;
                Debug.Log($"[MiniBossShotArgs] froze {_loop.Kind} wind-up at {_loop.Progress:0.00}");
                _freezeAt = -1f;
            }
        }

        private static string GetArg(string name)
        {
            var args = DevArgs.All; // KAN-179: command line or SS_ARGS (iOS)
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
