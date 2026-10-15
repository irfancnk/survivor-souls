using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Playtest Test Mode (KAN-144), switched in the main menu settings. The one read point for every unlock rule: when it
    /// is on, every Realm is unlocked and startable (<c>RunProgress.IsUnlocked</c>, <c>RealmSelectScreen</c>), the Realm
    /// sheet has a Start Chamber stepper, and runs do not write Realm progress (best Chamber, completed flags), so the real
    /// save is the same when it goes off again. The first switch-on tops the Souls balance up to <see cref="SoulGrant"/>;
    /// later switch-ons add nothing (a saved flag remembers the grant). Granted Souls stay when it goes off.
    /// Future unlock systems (hero roster KAN-100, gear KAN-139, Grand Reliquaries KAN-137) read <see cref="IsOn"/> too.
    /// The settings row shows in development builds; other builds show it after <see cref="RevealTaps"/> taps on the
    /// settings title (<see cref="TestModeReveal"/>), or while Test Mode is on.
    /// Store release builds (KAN-151, <see cref="StoreBuild"/>) lock it off: no row, no reveal, no switch-on.
    /// </summary>
    public static class TestMode
    {
        /// <summary>Souls balance after the first switch-on (at least; a bigger balance is kept).</summary>
        public const int SoulGrant = 999_999;
        /// <summary>Taps on the settings title that show the row in a non-development build.</summary>
        public const int RevealTaps = 7;
        public const string KeyGranted = "settings.testMode.soulsGranted";
        /// <summary>Scripting define of store release builds (<c>DevBuildTools -ss-store</c>).</summary>
        public const string StoreDefine = "SS_STORE";

        /// <summary>
        /// Store release build (KAN-151): compiled with <see cref="StoreDefine"/>. Test Mode can never turn on there:
        /// <see cref="IsOn"/> is false even with a saved flag from an older install, <see cref="SetOn"/> writes nothing,
        /// and the settings row never shows. Dev and phone playtest builds have no define and keep KAN-144 as is.
        /// </summary>
        public const bool StoreBuild =
#if SS_STORE
            true;
#else
            false;
#endif

        /// <summary>Tests only: a non-store build acts as a store build. It cannot unlock a real store build.</summary>
        public static bool SimulateStoreBuild { get; set; }

        /// <summary>Test Mode is locked off: a store build, or a test that simulates one.</summary>
        public static bool Locked => StoreBuild || SimulateStoreBuild;

        /// <summary>Test Mode is on (saved setting; always false in a store build).</summary>
        public static bool IsOn => !Locked && GameSettings.TestMode;

        /// <summary>The one-time Souls grant was done. Plain flag: Test Mode is a playtest tool, not an economy source.</summary>
        public static bool SoulsGranted => PlayerPrefs.GetInt(KeyGranted, 0) != 0;

        /// <summary>Settings row visible without the hidden taps.</summary>
        public static bool RowVisibleByDefault => !Locked && Debug.isDebugBuild;

        /// <summary>
        /// Turns Test Mode on or off. Switching on grants Souls up to <see cref="SoulGrant"/> the first time only.
        /// Returns the Souls added now (0 when off, already granted, or the balance was already big enough).
        /// A store build (<see cref="Locked"/>) writes nothing and returns 0.
        /// </summary>
        public static int SetOn(bool on)
        {
            if (Locked) return 0;
            GameSettings.TestMode = on;
            if (!on || SoulsGranted) return 0;
            PlayerPrefs.SetInt(KeyGranted, 1);
            int added = Mathf.Max(0, SoulGrant - SoulWallet.Balance);
            if (added > 0) SoulWallet.Earn(added);
            PlayerPrefs.Save();
            return added;
        }

        /// <summary>Tests and debug: Test Mode off and the grant flag removed (the next switch-on grants again).</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(GameSettings.KeyTestMode);
            PlayerPrefs.DeleteKey(KeyGranted);
        }
    }

    /// <summary>
    /// Hidden reveal of the Test Mode row (KAN-144): visible at once in a development build or while Test Mode is on;
    /// otherwise after <see cref="TestMode.RevealTaps"/> taps on the settings title. Never in a store build
    /// (<see cref="TestMode.Locked"/>). Plain class: tests drive it with any build flag. A reveal lasts until the menu is left.
    /// </summary>
    public sealed class TestModeReveal
    {
        private readonly bool _devBuild;
        private int _taps;
        private bool _revealed;

        public TestModeReveal(bool devBuild) => _devBuild = devBuild;

        /// <summary>The row should show now.</summary>
        public bool Visible => !TestMode.Locked && (_devBuild || _revealed || TestMode.IsOn);

        /// <summary>One tap on the settings title. True when this tap revealed the row.</summary>
        public bool Tap()
        {
            if (TestMode.Locked || Visible) return false;
            _taps++;
            if (_taps < TestMode.RevealTaps) return false;
            _revealed = true;
            return true;
        }
    }
}
