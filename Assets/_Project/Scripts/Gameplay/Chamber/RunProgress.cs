using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>Lock state of a Realm on the Realm selection page (KAN-20).</summary>
    public enum RealmState
    {
        /// <summary>The previous Realm is not completed yet.</summary>
        Locked = 0,
        /// <summary>Can be started; not completed yet.</summary>
        Unlocked = 1,
        /// <summary>Its last Chamber was cleared (KAN-47 calls <see cref="RunProgress.MarkCompleted"/>).</summary>
        Completed = 2,
    }

    /// <summary>
    /// Saved Realm progress (KAN-40), for the Realm selection page (KAN-20): last Realm played, the Chamber the last run
    /// reached in each Realm, the best Chamber ever reached there and whether the Realm is completed. Written at the start
    /// of every Chamber (<see cref="ChamberTransition"/>). PlayerPrefs, like <see cref="Core.GameSettings"/>. A run cannot
    /// be resumed after an app kill (out of scope): only the numbers are kept.
    /// Lock rule: Realm 1 is always open; Realm N opens when Realm N-1 is completed. Realm ids run 1, 2, 3, ...
    /// Test Mode (KAN-144, <see cref="TestMode.IsOn"/>) opens every Realm without writing anything here.
    /// </summary>
    public static class RunProgress
    {
        private const string KeyLastRealm = "progress.lastRealm";

        private static string ChamberKey(int realm) => $"progress.realm{realm}.chamber";
        private static string BestKey(int realm) => $"progress.realm{realm}.best";
        private static string CompletedKey(int realm) => $"progress.realm{realm}.completed";

        /// <summary>Last Realm played (1 when nothing is saved).</summary>
        public static int LastRealm => PlayerPrefs.GetInt(KeyLastRealm, 1);

        /// <summary>Chamber the last run reached in <paramref name="realm"/> (0 = never played).</summary>
        public static int LastChamber(int realm) => PlayerPrefs.GetInt(ChamberKey(realm), 0);

        /// <summary>Best Chamber ever reached in <paramref name="realm"/> (0 = never played).</summary>
        public static int BestChamber(int realm) => PlayerPrefs.GetInt(BestKey(realm), 0);

        /// <summary>The last Chamber of <paramref name="realm"/> was cleared at least once.</summary>
        public static bool IsCompleted(int realm) => PlayerPrefs.GetInt(CompletedKey(realm), 0) != 0;

        /// <summary>Realm 1 always; any other Realm once the one before it is completed; every Realm in Test Mode (KAN-144).</summary>
        public static bool IsUnlocked(int realm) => TestMode.IsOn || realm <= 1 || IsCompleted(realm - 1);

        /// <summary>Runs write progress (best Chamber, completed). False in Test Mode: the real save stays as it was (KAN-144).</summary>
        public static bool Recording => !TestMode.IsOn;

        /// <summary>Locked, unlocked or completed (a completed Realm is always open).</summary>
        public static RealmState StateOf(int realm) =>
            IsCompleted(realm) ? RealmState.Completed : IsUnlocked(realm) ? RealmState.Unlocked : RealmState.Locked;

        /// <summary>End of Realm (KAN-47): marks <paramref name="realm"/> completed, which unlocks the next one.</summary>
        public static void MarkCompleted(int realm)
        {
            PlayerPrefs.SetInt(CompletedKey(realm), 1);
            PlayerPrefs.Save();
        }

        /// <summary>A Chamber started: saves the Realm, the Chamber and a new best.</summary>
        public static void RecordChamberStart(int realm, int chamber)
        {
            PlayerPrefs.SetInt(KeyLastRealm, realm);
            PlayerPrefs.SetInt(ChamberKey(realm), chamber);
            if (chamber > BestChamber(realm)) PlayerPrefs.SetInt(BestKey(realm), chamber);
            PlayerPrefs.Save(); // a Chamber lasts minutes: one write per Chamber is cheap, and a kill keeps the best
        }

        /// <summary>Removes the saved numbers of <paramref name="realm"/> (tests, debug).</summary>
        public static void Clear(int realm)
        {
            PlayerPrefs.DeleteKey(ChamberKey(realm));
            PlayerPrefs.DeleteKey(BestKey(realm));
            PlayerPrefs.DeleteKey(CompletedKey(realm));
        }
    }
}
