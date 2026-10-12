using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Entry point of the Game scene. Applies player settings (frame-rate cap) so the scene also works
    /// when started directly in the editor. Later: run/wave state lives here.
    /// </summary>
    public sealed class GameSceneController : MonoBehaviour
    {
        private void Awake() => GameSettings.Apply();
    }
}
