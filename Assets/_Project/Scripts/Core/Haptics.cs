using System.Runtime.CompilerServices;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

[assembly: InternalsVisibleTo("SurvivorSouls.Tests.PlayMode")]

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Short haptic taps (KAN-12). Game code calls only <see cref="Light"/>; the platform backend stays in this file.
    /// <list type="bullet">
    /// <item>iOS: <c>UIImpactFeedbackGenerator</c>, light style (native plugin <c>Plugins/iOS/SSHaptics.mm</c>).</item>
    /// <item>Android: one prebuilt <c>VibrationEffect</c> (14 ms, low amplitude when the motor supports it, else the
    /// system tick on API 29+). Needs <c>android.permission.VIBRATE</c>, added by the editor build step
    /// <c>AndroidVibratePermission</c>.</item>
    /// <item>Editor, desktop, devices without a motor: silent no-op.</item>
    /// </list>
    /// Respects <see cref="GameSettings.Vibration"/>. Pulses closer than <see cref="MinInterval"/> collapse into one,
    /// so a group dying together gives one tap, not a buzz. No managed allocations per pulse.
    /// Call from the main thread.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Seconds (unscaled) a pulse blocks the next one.</summary>
        public const float MinInterval = 0.05f;

        private static float _lastPulse = float.NegativeInfinity;

        /// <summary>Pulses accepted since start (after settings + rate limit). For tests and dev overlays.</summary>
        public static int PulseCount { get; private set; }

        /// <summary>One light tap. Returns true when a pulse was sent (false: vibration off or rate limited).</summary>
        public static bool Light() => Light(Time.unscaledTime);

        /// <summary>
        /// Builds the platform backend now so the first kill does not pay for JNI lookups / generator creation.
        /// Safe to call many times.
        /// </summary>
        public static void Warmup() => Backend.Init();

        internal static bool Light(float now)
        {
            if (!GameSettings.Vibration) return false;
            if (now - _lastPulse < MinInterval) return false;
            _lastPulse = now;
            PulseCount++;
            Backend.Pulse();
            return true;
        }

        internal static void ResetState()
        {
            _lastPulse = float.NegativeInfinity;
            PulseCount = 0;
        }

#if UNITY_IOS && !UNITY_EDITOR
        private static class Backend
        {
            [DllImport("__Internal")] private static extern void SSHaptics_Prepare();
            [DllImport("__Internal")] private static extern void SSHaptics_ImpactLight();

            public static void Init() => SSHaptics_Prepare();
            public static void Pulse() => SSHaptics_ImpactLight();
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private static class Backend
        {
            private const long DurationMs = 14;
            private const int Amplitude = 64; // of 255: a light tap, not a buzz
            private const int EffectTick = 2; // VibrationEffect.EFFECT_TICK (API 29)

            private static bool _initTried;
            private static AndroidJavaObject _vibrator; // keeps the global refs alive
            private static AndroidJavaObject _effect;
            // Raw JNI call path, resolved once: AndroidJavaObject.Call builds a signature string + arg arrays every call.
            private static System.IntPtr _vibrateMethod;
            private static readonly jvalue[] VibrateArgs = new jvalue[1];

            public static void Init()
            {
                if (_initTried) return;
                _initTried = true;
                try
                {
                    int sdk;
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                    var context = UnityEngine.Android.AndroidApplication.currentContext;
                    AndroidJavaObject vibrator;
                    if (sdk >= 31)
                    {
                        using var manager = context.Call<AndroidJavaObject>("getSystemService", "vibrator_manager");
                        vibrator = manager?.Call<AndroidJavaObject>("getDefaultVibrator");
                    }
                    else
                    {
                        vibrator = context.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                    if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                    {
                        vibrator?.Dispose();
                        return; // no motor: stay silent
                    }

                    using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                    {
                        if (vibrator.Call<bool>("hasAmplitudeControl"))
                            _effect = effects.CallStatic<AndroidJavaObject>("createOneShot", DurationMs, Amplitude);
                        else if (sdk >= 29)
                            _effect = effects.CallStatic<AndroidJavaObject>("createPredefined", EffectTick);
                        else
                            _effect = effects.CallStatic<AndroidJavaObject>("createOneShot", DurationMs, -1); // DEFAULT_AMPLITUDE
                    }
                    _vibrateMethod = AndroidJNIHelper.GetMethodID(
                        vibrator.GetRawClass(), "vibrate", "(Landroid/os/VibrationEffect;)V");
                    if (_vibrateMethod == System.IntPtr.Zero)
                    {
                        AndroidJNI.ExceptionClear();
                        vibrator.Dispose();
                        return;
                    }
                    VibrateArgs[0].l = _effect.GetRawObject();
                    _vibrator = vibrator;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Haptics] Android vibrator unavailable, haptics off: {e.Message}");
                    _vibrator = null;
                    _effect = null;
                }
            }

            public static void Pulse()
            {
                Init();
                if (_vibrator == null) return;
                AndroidJNI.CallVoidMethod(_vibrator.GetRawObject(), _vibrateMethod, VibrateArgs);
                System.IntPtr error = AndroidJNI.ExceptionOccurred();
                if (error == System.IntPtr.Zero) return;
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(error);
                Debug.LogWarning("[Haptics] vibrate threw, haptics off.");
                _vibrator = null; // one warning, then silent
            }
        }
#else
        private static class Backend
        {
            public static void Init() { }
            public static void Pulse() { }
        }
#endif
    }
}
