using SurvivorSouls.Core;
using UnityEditor;

namespace SurvivorSouls.Editor.Economy
{
    /// <summary>
    /// Economy data assets (KAN-97): the free Ember Shard sources (<see cref="GemRewardConfig"/>, welcome / first-run bonus
    /// and Rare chest shards). Made once with the first guesses, never overwritten, so Inspector tuning stays. Used by
    /// GameSceneBuilder (run end bonus, chest shards) and MainMenuSceneBuilder (welcome bonus).
    /// </summary>
    public static class EconomyBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Economy";
        public const string GemRewardsPath = DataDir + "/GemRewards.asset";

        public static GemRewardConfig LoadOrCreateGemRewards()
        {
            var config = AssetDatabase.LoadAssetAtPath<GemRewardConfig>(GemRewardsPath);
            if (config != null) return config;
            EditorAssetUtil.EnsureFolder(DataDir);
            config = GemRewardConfig.Create(); // welcome 50, first run 25, Rare chest 35 % for 3-8
            AssetDatabase.CreateAsset(config, GemRewardsPath);
            AssetDatabase.SaveAssets();
            return config;
        }
    }
}
