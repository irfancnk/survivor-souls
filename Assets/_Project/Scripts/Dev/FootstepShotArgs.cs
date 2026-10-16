using System.Globalization;
using SurvivorSouls.Audio;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build footstep log (KAN-235), next to <see cref="DevScreenshot"/>: <c>-ss-footsteps &lt;seconds&gt;</c> logs
    /// <c>[FootstepShotArgs] ...</c> every that many seconds and once before the capture: steps (left / right), steps per
    /// second, speed, surface, last level, duck and fight rate, foot source (rig bones or stride timer). Pair it with
    /// <c>-ss-move x,y</c> (the Knight runs). Audio cannot be screenshotted: the numbers show the timing and the mix.
    /// Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class FootstepShotArgs : MonoBehaviour
    {
        private float _every = 1f;
        private float _next;
        private int _stepsAtLast;
        private float _timeAtLast;
        private FootstepSounds _sounds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-footsteps");
            if (string.IsNullOrEmpty(spec)) return;
            var go = new GameObject(nameof(FootstepShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<FootstepShotArgs>();
            args._every = float.TryParse(spec, NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? Mathf.Max(0.2f, s) : 1f;
            DevScreenshot.BeforeCapture += args.Log;
        }

        private void OnDestroy() => DevScreenshot.BeforeCapture -= Log;

        private void Update()
        {
            if (_sounds == null)
            {
                _sounds = FindAnyObjectByType<FootstepSounds>();
                if (_sounds == null) return;
                _next = Time.time + _every;
                _timeAtLast = Time.time;
            }
            if (Time.time >= _next)
            {
                _next = Time.time + _every;
                Log();
            }
        }

        private void Log()
        {
            if (_sounds == null) return;
            float span = Mathf.Max(0.001f, Time.time - _timeAtLast);
            float rate = (_sounds.Steps - _stepsAtLast) / span;
            _stepsAtLast = _sounds.Steps;
            _timeAtLast = Time.time;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[FootstepShotArgs] steps {0} (L {1} / R {2}), {3:0.00} steps/s, speed {4:0.00} m/s, surface {5}, level {6:0.000}, duck {7:0.00}, fight {8:0.0}/s, feet {9}",
                _sounds.Steps, _sounds.LeftSteps, _sounds.RightSteps, rate, _sounds.Speed, _sounds.LastSurface, _sounds.LastLevel,
                _sounds.Duck, _sounds.FightRate, _sounds.UsingStrideTimer ? "stride timer" : "rig bones"));
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
