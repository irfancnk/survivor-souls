using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
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
    /// Boss (KAN-58): a landed slam shakes the camera (harder when it hit the Knight, plus a haptic tap). A healing heart
    /// taken in the fight (KAN-60) gives a haptic tap.
    /// </summary>
    public sealed class GameSceneController : MonoBehaviour
    {
        [SerializeField] private PlayerHealth player;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private SurvivalTimer timer;
        [SerializeField] private TopDownFollowCamera followCamera;
        [SerializeField] private BossFight boss;
        [SerializeField] private BossHearts hearts;
        [Header("Portal opens")]
        [SerializeField, Min(0f)] private float shakeAmplitude = 0.16f;
        [SerializeField, Min(0f)] private float shakeTime = 0.5f;
        [Header("Boss slam")]
        [SerializeField, Min(0f)] private float slamShake = 0.14f;
        [SerializeField, Min(0f)] private float slamHitShake = 0.3f;
        [SerializeField, Min(0f)] private float slamShakeTime = 0.35f;
        [Header("Boss phase 2, charge, fan bolts (KAN-61)")]
        [SerializeField, Min(0f)] private float phaseShake = 0.22f;
        [SerializeField, Min(0f)] private float phaseShakeTime = 0.6f;
        [SerializeField, Min(0f)] private float boltHitShake = 0.12f;

        private void Awake() => GameSettings.Apply();

        private void OnEnable()
        {
            if (player != null) player.OnDeath += OnPlayerDeath;
            if (timer != null) timer.Finished += OnPortalOpens;
            if (boss != null)
            {
                boss.Slammed += OnSlam;
                boss.Charged += OnSlam; // the dash ends like a slam: shake, + haptic when it hit
                boss.PhaseChanged += OnBossPhase;
                if (boss.Projectiles != null) boss.Projectiles.HitPlayer += OnBoltHit;
            }
            if (hearts != null) hearts.PickedUp += OnHeartPickedUp;
        }

        private void OnDisable()
        {
            if (player != null) player.OnDeath -= OnPlayerDeath;
            if (timer != null) timer.Finished -= OnPortalOpens;
            if (boss != null)
            {
                boss.Slammed -= OnSlam;
                boss.Charged -= OnSlam;
                boss.PhaseChanged -= OnBossPhase;
                if (boss.Projectiles != null) boss.Projectiles.HitPlayer -= OnBoltHit;
            }
            if (hearts != null) hearts.PickedUp -= OnHeartPickedUp;
        }

        // Leaving the scene (retry / menu) must never keep a level-up pause alive.
        private void OnDestroy() => GamePause.Clear();

        private void OnPlayerDeath()
        {
            if (spawner != null) spawner.enabled = false;
        }

        private void OnSlam(UnityEngine.Vector3 centre, float dealt)
        {
            if (dealt > 0f) Haptics.Light();
            if (followCamera != null) followCamera.Shake(dealt > 0f ? slamHitShake : slamShake, slamShakeTime);
        }

        private void OnBossPhase(int phase)
        {
            Haptics.Light();
            if (followCamera != null) followCamera.Shake(phaseShake, phaseShakeTime);
        }

        private void OnBoltHit(UnityEngine.Vector3 at, float dealt)
        {
            if (dealt <= 0f) return;
            Haptics.Light();
            if (followCamera != null) followCamera.Shake(boltHitShake, slamShakeTime);
        }

        private void OnHeartPickedUp(HealingHeart heart, float healed) => Haptics.Light();

        private void OnPortalOpens()
        {
            Haptics.Light();
            if (followCamera != null) followCamera.Shake(shakeAmplitude, shakeTime);
        }
    }
}
