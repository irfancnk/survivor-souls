using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Entry point of the Game scene. Applies player settings (frame-rate cap) so the scene also works
    /// when started directly in the editor. Run state: when the player dies, enemy spawning stops. XP and level are
    /// run-only (XpManager starts fresh with the scene), and no pause outlives the scene.
    /// Chamber (KAN-39): when the survival timer ends, a light haptic tap and a small camera shake mark the portal opening
    /// (HUD message, sound and the portal itself listen to the same event).
    /// </summary>
    public sealed class GameSceneController : MonoBehaviour
    {
        [SerializeField] private PlayerHealth player;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private SurvivalTimer timer;
        [SerializeField] private TopDownFollowCamera followCamera;
        [Header("Portal opens")]
        [SerializeField, Min(0f)] private float shakeAmplitude = 0.16f;
        [SerializeField, Min(0f)] private float shakeTime = 0.5f;

        private void Awake() => GameSettings.Apply();

        private void OnEnable()
        {
            if (player != null) player.OnDeath += OnPlayerDeath;
            if (timer != null) timer.Finished += OnPortalOpens;
        }

        private void OnDisable()
        {
            if (player != null) player.OnDeath -= OnPlayerDeath;
            if (timer != null) timer.Finished -= OnPortalOpens;
        }

        // Leaving the scene (retry / menu) must never keep a level-up pause alive.
        private void OnDestroy() => GamePause.Clear();

        private void OnPlayerDeath()
        {
            if (spawner != null) spawner.enabled = false;
        }

        private void OnPortalOpens()
        {
            Haptics.Light();
            if (followCamera != null) followCamera.Shake(shakeAmplitude, shakeTime);
        }
    }
}
