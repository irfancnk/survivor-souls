using System;
using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Player-facing settings. Stored in PlayerPrefs. Apply() pushes values to the engine.
    /// Audio volumes are stored only; the audio system reads them when it exists.
    /// </summary>
    public static class GameSettings
    {
        private const string KeyMusic = "settings.musicVolume";
        private const string KeySfx = "settings.sfxVolume";
        private const string KeyVibration = "settings.vibration";
        private const string KeyHighFrameRate = "settings.highFrameRate";
        private const string KeyReloadRing = "settings.reloadRing";
        public const string KeyTestMode = "settings.testMode";
        private const string KeyFastSkillReveal = "settings.fastSkillReveal";
        private const string KeyReduceScreenEffects = "settings.reduceScreenEffects";

        public static event Action Changed;

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(KeyMusic, 0.8f);
            set => SetFloat(KeyMusic, Mathf.Clamp01(value));
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(KeySfx, 1f);
            set => SetFloat(KeySfx, Mathf.Clamp01(value));
        }

        public static bool Vibration
        {
            get => PlayerPrefs.GetInt(KeyVibration, 1) == 1;
            set => SetInt(KeyVibration, value ? 1 : 0);
        }

        /// <summary>60 FPS when true, 30 FPS when false (battery saver).</summary>
        public static bool HighFrameRate
        {
            get => PlayerPrefs.GetInt(KeyHighFrameRate, 1) == 1;
            set => SetInt(KeyHighFrameRate, value ? 1 : 0);
        }

        /// <summary>Reload ring under the Knight (KAN-42). On by default; players who find it busy can turn it off.</summary>
        public static bool ReloadRing
        {
            get => PlayerPrefs.GetInt(KeyReloadRing, 1) == 1;
            set => SetInt(KeyReloadRing, value ? 1 : 0);
        }

        /// <summary>
        /// Fast skill reveal (KAN-159): the skill pick cards land at once, without the slot-machine roll. Off by default.
        /// </summary>
        public static bool FastSkillReveal
        {
            get => PlayerPrefs.GetInt(KeyFastSkillReveal, 0) == 1;
            set => SetInt(KeyFastSkillReveal, value ? 1 : 0);
        }

        /// <summary>
        /// Reduce screen effects (KAN-82, accessibility): no health bar shake, shards or white flash, a softer damage
        /// vignette and no low-health pulse. Off by default.
        /// </summary>
        public static bool ReduceScreenEffects
        {
            get => PlayerPrefs.GetInt(KeyReduceScreenEffects, 0) == 1;
            set => SetInt(KeyReduceScreenEffects, value ? 1 : 0);
        }

        /// <summary>
        /// Playtest Test Mode (KAN-144): every Realm open, any start Chamber, Souls topped up once. Off by default.
        /// Read it through <see cref="Core.TestMode.IsOn"/>; turn it on with <see cref="Core.TestMode.SetOn"/> (that also
        /// does the one-time Souls grant).
        /// </summary>
        public static bool TestMode
        {
            get => PlayerPrefs.GetInt(KeyTestMode, 0) == 1;
            set => SetInt(KeyTestMode, value ? 1 : 0);
        }

        public static void Apply()
        {
            // Mobile ignores vSyncCount; targetFrameRate controls the cap.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = HighFrameRate ? 60 : 30;
        }

        public static void Save() => PlayerPrefs.Save();

        private static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            Changed?.Invoke();
        }

        private static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            Apply();
            Changed?.Invoke();
        }
    }
}
