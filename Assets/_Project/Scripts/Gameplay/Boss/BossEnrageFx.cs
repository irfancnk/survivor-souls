using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// KAN-164.2 enrage look and sound. On <see cref="BossFight.Enraged"/>: the aura (red embers rising off the body, world
    /// space, one draw call) follows the boss and plays until the boss dies or the fight object goes; a deep roar (the boss
    /// groan clips at a low pitch, <see cref="GameSettings.SfxVolume"/>), a camera shake and a light haptic. Built by
    /// BossEnrageStep on the "Boss" object. No GC per frame (the aura is moved, not re-parented, so a pooled or scaled body
    /// never carries it).
    /// </summary>
    public sealed class BossEnrageFx : MonoBehaviour
    {
        [SerializeField] private BossFight fight;
        [Tooltip("Looping ember aura (world space); stopped until the enrage.")]
        [SerializeField] private ParticleSystem aura;
        [SerializeField] private TopDownFollowCamera followCamera;
        [SerializeField] private AudioClip[] roars = new AudioClip[0];
        [SerializeField, Range(0f, 1f)] private float roarVolume = 0.95f;
        [SerializeField, Range(0.2f, 1.5f)] private float roarPitch = 0.5f;
        [SerializeField, Min(0f)] private float shake = 0.35f;
        [SerializeField, Min(0f)] private float shakeTime = 0.6f;

        private AudioSource _voice;
        private bool _live;
        private int _roared;

        /// <summary>The aura is on (enraged boss alive).</summary>
        public bool AuraOn => _live;
        /// <summary>Roars played (tests).</summary>
        public int Roared => _roared;
        public ParticleSystem Aura => aura;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossFight bossFight, ParticleSystem auraSystem, AudioClip[] roarClips = null)
        {
            if (isActiveAndEnabled) Unhook();
            fight = bossFight;
            aura = auraSystem;
            roars = roarClips ?? new AudioClip[0];
            if (isActiveAndEnabled) Hook();
        }

        private void OnEnable() => Hook();

        private void OnDisable()
        {
            Unhook();
            StopAura();
        }

        private void Hook()
        {
            if (fight == null) return;
            fight.Enraged += OnEnraged;
            fight.Defeated += StopAura;
        }

        private void Unhook()
        {
            if (fight == null) return;
            fight.Enraged -= OnEnraged;
            fight.Defeated -= StopAura;
        }

        private void OnEnraged()
        {
            _live = true;
            Follow();
            if (aura != null) aura.Play(true);
            if (followCamera != null) followCamera.Shake(shake, shakeTime);
            Haptics.Light();
            PlayRoar();
        }

        private void LateUpdate()
        {
            if (!_live) return;
            var body = fight != null ? fight.Boss : null;
            if (body == null || !body.IsAlive)
            {
                StopAura();
                return;
            }
            Follow();
        }

        private void Follow()
        {
            var body = fight != null ? fight.Boss : null;
            if (aura != null && body != null) aura.transform.position = body.transform.position;
        }

        private void StopAura()
        {
            _live = false;
            if (aura != null) aura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void PlayRoar()
        {
            if (roars == null || roars.Length == 0) return;
            var clip = roars[Random.Range(0, roars.Length)];
            if (clip == null) return;
            if (_voice == null)
            {
                _voice = gameObject.AddComponent<AudioSource>();
                _voice.playOnAwake = false;
                _voice.spatialBlend = 0f;
            }
            _voice.pitch = roarPitch * Random.Range(0.97f, 1.03f);
            _voice.PlayOneShot(clip, roarVolume * GameSettings.SfxVolume);
            _roared++;
        }
    }
}
