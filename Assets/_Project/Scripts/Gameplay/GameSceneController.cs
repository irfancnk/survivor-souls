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
    /// Boss intro and victory beat (KAN-62): the camera pulls out to frame the Knight and the boss during the intro and eases
    /// back when the fight starts. The kill starts a short slow motion (<see cref="BossData.VictorySlowMo"/>, real seconds,
    /// held then eased back to 1; it never writes the time scale while a <see cref="GamePause"/> hold is open) and frames the
    /// fall until the reward lands.
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
        [Header("Boss victory beat (KAN-62)")]
        [Tooltip("Camera distance x this while the fall is framed (between the Knight and the body).")]
        [SerializeField, Min(0.5f)] private float victoryZoom = 1.1f;
        [SerializeField, Min(0f)] private float victoryShake = 0.25f;

        private float _slowMoScale = 1f, _slowMoLength, _slowMoAge = -1f;

        /// <summary>The kill slow motion is running.</summary>
        public bool SlowMotion => _slowMoAge >= 0f;

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
                boss.IntroStarted += OnBossIntro;
                boss.IntroEnded += OnBossIntroEnded;
                boss.Defeated += OnBossDefeated;
                boss.VictoryFinished += OnBossVictoryFinished;
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
                boss.IntroStarted -= OnBossIntro;
                boss.IntroEnded -= OnBossIntroEnded;
                boss.Defeated -= OnBossDefeated;
                boss.VictoryFinished -= OnBossVictoryFinished;
                if (boss.Projectiles != null) boss.Projectiles.HitPlayer -= OnBoltHit;
            }
            if (hearts != null) hearts.PickedUp -= OnHeartPickedUp;
        }

        // Leaving the scene (retry / menu) must never keep a level-up pause or the kill slow motion alive.
        private void OnDestroy()
        {
            GamePause.Clear();
            if (SlowMotion) Time.timeScale = 1f;
        }

        private void Update() => StepSlowMotion(Time.unscaledDeltaTime);

        /// <summary>Starts the kill slow motion: <paramref name="scale"/> held for half of <paramref name="length"/> real seconds,
        /// then eased back to 1. Public for tests.</summary>
        public void StartSlowMotion(float scale, float length)
        {
            if (length <= 0f || scale >= 1f) return;
            _slowMoScale = Mathf.Clamp(scale, 0.05f, 1f);
            _slowMoLength = length;
            _slowMoAge = 0f;
            if (!GamePause.IsPaused) Time.timeScale = _slowMoScale;
        }

        /// <summary>Advances the slow motion by <paramref name="dt"/> real seconds. Waits while the game is paused. Public for tests.</summary>
        public void StepSlowMotion(float dt)
        {
            if (_slowMoAge < 0f || GamePause.IsPaused) return;
            _slowMoAge += dt;
            float k = _slowMoAge / _slowMoLength;
            if (k >= 1f)
            {
                _slowMoAge = -1f;
                Time.timeScale = 1f;
                return;
            }
            float ease = k < 0.5f ? 0f : (k - 0.5f) * 2f;
            Time.timeScale = Mathf.Lerp(_slowMoScale, 1f, ease * ease * (3f - 2f * ease));
        }

        private void OnBossIntro(Enemy body)
        {
            if (followCamera == null || player == null) return;
            var data = boss.Data;
            followCamera.Focus(Vector3.Lerp(player.transform.position, body.transform.position, 0.5f),
                data != null ? data.IntroZoom : 1.3f);
        }

        private void OnBossIntroEnded()
        {
            if (followCamera != null) followCamera.ReleaseFocus();
        }

        private void OnBossDefeated()
        {
            var data = boss.Data;
            if (data != null) StartSlowMotion(data.VictorySlowMo, data.VictorySlowMoTime);
            Haptics.Light();
            if (followCamera == null) return;
            followCamera.Shake(victoryShake, slamShakeTime);
            if (player != null && data != null && data.VictoryBeat > 0f)
                followCamera.Focus(Vector3.Lerp(player.transform.position, boss.DeathPoint, 0.5f), victoryZoom);
        }

        private void OnBossVictoryFinished(Vector3 at)
        {
            if (followCamera != null) followCamera.ReleaseFocus();
        }

        private void OnPlayerDeath()
        {
            if (spawner != null) spawner.enabled = false;
        }

        private void OnSlam(Vector3 centre, float dealt)
        {
            if (dealt > 0f) Haptics.Light();
            if (followCamera != null) followCamera.Shake(dealt > 0f ? slamHitShake : slamShake, slamShakeTime);
        }

        private void OnBossPhase(int phase)
        {
            Haptics.Light();
            if (followCamera != null) followCamera.Shake(phaseShake, phaseShakeTime);
        }

        private void OnBoltHit(Vector3 at, float dealt)
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
