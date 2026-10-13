using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Saved Realm progress (KAN-40), for the Realm selection page (KAN-20): last Realm played, the Chamber the last run
    /// reached in each Realm and the best Chamber ever reached there. Written at the start of every Chamber
    /// (<see cref="ChamberTransition"/>). PlayerPrefs, like <see cref="Core.GameSettings"/>. A run cannot be resumed
    /// after an app kill (out of scope): only the numbers are kept.
    /// </summary>
    public static class RunProgress
    {
        private const string KeyLastRealm = "progress.lastRealm";

        private static string ChamberKey(int realm) => $"progress.realm{realm}.chamber";
        private static string BestKey(int realm) => $"progress.realm{realm}.best";

        /// <summary>Last Realm played (1 when nothing is saved).</summary>
        public static int LastRealm => PlayerPrefs.GetInt(KeyLastRealm, 1);

        /// <summary>Chamber the last run reached in <paramref name="realm"/> (0 = never played).</summary>
        public static int LastChamber(int realm) => PlayerPrefs.GetInt(ChamberKey(realm), 0);

        /// <summary>Best Chamber ever reached in <paramref name="realm"/> (0 = never played).</summary>
        public static int BestChamber(int realm) => PlayerPrefs.GetInt(BestKey(realm), 0);

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
        }
    }
}
