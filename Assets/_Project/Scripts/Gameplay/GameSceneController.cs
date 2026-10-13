using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Entry point of the Game scene. Applies player settings (frame-rate cap) so the scene also works
    /// when started directly in the editor. Run state: when the player dies, enemy spawning stops.
    /// Later: wave / chamber state lives here.
    /// </summary>
    public sealed class GameSceneController : MonoBehaviour
    {
        [SerializeField] private PlayerHealth player;
        [SerializeField] private EnemySpawner spawner;

        private void Awake() => GameSettings.Apply();

        private void OnEnable()
        {
            if (player != null) player.OnDeath += OnPlayerDeath;
        }

        private void OnDisable()
        {
            if (player != null) player.OnDeath -= OnPlayerDeath;
        }

        private void OnPlayerDeath()
        {
            if (spawner != null) spawner.enabled = false;
        }
    }
}
