using System.Globalization;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build screenshot helper for the upper-body crossbow aim (KAN-215), next to <see cref="DevScreenshot"/>:
    /// <c>-ss-aim on|off[,&lt;shot&gt;]</c> keeps (<c>on</c>) or turns off (<c>off</c>, the old pose for before / after shots)
    /// <see cref="CrossbowAim"/>, logs the crossbow-line error of each volley, draws a thin red line along the crossbow line
    /// (6 m) and stops time 0.03 s after volley number <c>shot</c> (default 2), so the capture (<c>-ss-delay</c>, realtime)
    /// shows the pose at the shot. Use with <c>-ss-horde</c> and <c>-ss-closeup</c>. Dev builds only; no effect without the arg.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after CrossbowAim / CrossbowRecoil: the line follows the final pose
    public sealed class AimShotArgs : MonoBehaviour
    {
        private const float LineLength = 6f;

        private bool _on;
        private int _freezeShot;
        private AutoShooter _shooter;
        private CrossbowAim _aim;
        private LineRenderer _line;
        private int _shots;
        private float _left = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-aim");
            if (string.IsNullOrEmpty(spec)) return;
            string[] parts = spec.Split(',');
            var go = new GameObject(nameof(AimShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<AimShotArgs>();
            args._on = parts[0] != "off";
            args._freezeShot = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 2;
        }

        private void Update()
        {
            if (_shooter == null)
            {
                _shooter = FindAnyObjectByType<AutoShooter>();
                if (_shooter == null) return;
                _aim = _shooter.GetComponent<CrossbowAim>();
                if (_aim != null) _aim.enabled = _on;
                _shooter.Fired += OnFired;
                CreateLine();
                Debug.Log($"[AimShotArgs] aim {(_on ? "on" : "off")}, freeze after volley {_freezeShot}, aim component {(_aim != null)}");
            }
            if (_left < 0f) return;
            _left -= Time.deltaTime;
            if (_left > 0f) return;
            _left = -1f;
            Time.timeScale = 0f;
            Debug.Log($"[AimShotArgs] frozen after volley {_shots}");
        }

        private void OnFired(Vector3 from, Vector3 dir)
        {
            _shots++;
            float error = _aim != null ? Vector3.Angle(_aim.MuzzleLine, dir) : -1f;
            Debug.Log($"[AimShotArgs] volley {_shots}: crossbow line vs bolt line {error:F2} deg, aim weight {(_aim != null ? _aim.Weight : 0f):F2}");
            if (_shots == _freezeShot) _left = 0.03f;
        }

        private void CreateLine()
        {
            var shader = Shader.Find("Sprites/Default");
            if (_aim == null || _aim.Muzzle == null || shader == null) return;
            _line = new GameObject("Aim Line").AddComponent<LineRenderer>();
            _line.sharedMaterial = new Material(shader);
            _line.startColor = _line.endColor = new Color(1f, 0.15f, 0.1f, 0.9f);
            _line.startWidth = _line.endWidth = 0.025f;
            _line.positionCount = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (_line == null) return;
            Vector3 from = _aim.Muzzle.position;
            _line.SetPosition(0, from);
            _line.SetPosition(1, from + _aim.MuzzleLine * LineLength);
        }

        private void OnDestroy()
        {
            if (_shooter != null) _shooter.Fired -= OnFired;
        }

        private static string GetArg(string name)
        {
            string[] args = DevArgs.All;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
