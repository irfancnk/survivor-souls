using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-225 Knight hit reaction, Game scene side: puts <see cref="PlayerHitReaction"/> on the Knight, wired to its
    /// <see cref="LocomotionAnimatorDriver"/> (the driver follows a hero swap, so the reaction does too). The upper-body
    /// dip during the clip is in <see cref="SurvivorSouls.Gameplay.Combat.CrossbowStance"/> on the Knight prefab.
    /// </summary>
    public sealed class PlayerHitReactionStep : IGameSceneStep
    {
        public int Order => 225;

        public void Build(GameSceneContext ctx)
        {
            if (ctx.Player == null || ctx.Health == null) return;
            var driver = ctx.Player.GetComponent<LocomotionAnimatorDriver>();
            if (driver == null)
            {
                Debug.LogWarning("[PlayerHitReactionStep] Knight has no LocomotionAnimatorDriver: no hit reaction");
                return;
            }
            var reaction = ctx.Player.AddComponent<PlayerHitReaction>();
            GameSceneContext.SetRef(reaction, "driver", driver);
        }
    }
}
