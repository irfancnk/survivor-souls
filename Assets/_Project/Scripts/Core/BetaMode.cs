namespace SurvivorSouls.Core
{
    /// <summary>
    /// Beta 1 tester build. On only in players built with the <c>SS_BETA1</c> scripting define
    /// (<c>DevBuildTools ... -ss-beta1</c>); the editor, tests and normal builds keep the real rules.
    /// When on: every Realm is unlocked and startable (Realms without own content play the Realm 1 content, KAN-57),
    /// the Realm sheet has a Chamber picker (start at any Chamber, e.g. a boss Chamber), and the menu version shows "beta1".
    /// </summary>
    public static class BetaMode
    {
#if SS_BETA1
        public static readonly bool Enabled = true;
#else
        public static readonly bool Enabled = false;
#endif
        public const string Define = "SS_BETA1";
        public const string Tag = "beta1";
    }
}
