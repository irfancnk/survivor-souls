using System;
using SurvivorSouls.Gameplay.Characters;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Runs when <see cref="PlayerHealth.OnDeath"/> fires: turns off input, movement and attacks
    /// (<see cref="disableOnDeath"/>), plays the hit reaction and tips the model backwards about the feet
    /// (placeholder until a real death clip exists), then freezes the pose and raises <see cref="Finished"/>
    /// (the HUD shows the death screen).
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerDeath : MonoBehaviour
    {
        [Tooltip("Turned off on death: input, mover, shooter, contact damage, locomotion driver.")]
        [SerializeField] private Behaviour[] disableOnDeath;
        [Tooltip("Hidden on death: ground aids such as the objective arrow.")]
        [SerializeField] private GameObject[] hideOnDeath;
        [Tooltip("Visual root that falls (child of this object; pivot = feet).")]
        [SerializeField] private Transform model;
        [SerializeField] private Animator animator;
        [SerializeField, Min(0.05f)] private float fallTime = 0.65f;
        [SerializeField, Range(0f, 90f)] private float fallAngle = 78f;
        [Tooltip("Seconds after the body lands before Finished is raised.")]
        [SerializeField, Min(0f)] private float settleTime = 0.35f;
        [Tooltip("KAN-153: the model has a death clip (trigger \"Die\"): play it instead of the procedural tip-over.")]
        [SerializeField] private bool deathClip;
        [Tooltip("KAN-153: seconds of the death clip (used instead of fallTime when deathClip is on).")]
        [SerializeField, Min(0.05f)] private float deathClipTime = 1.6f;

        public static readonly int DieId = Animator.StringToHash("Die");

        /// <summary>Raised once, after the fall settles.</summary>
        public event Action Finished;

        private PlayerHealth _health;
        private Vector3 _modelPosition;
        private Quaternion _modelRotation;
        private float _time = -1f; // < 0 = alive
        private bool _finished;

        public bool IsDying => _time >= 0f;
        public Transform Model => model;
        public bool UsesDeathClip => deathClip;

        /// <summary>
        /// KAN-153: another hero model took the Knight's place (<c>HeroLoadout</c>). <paramref name="clipSeconds"/> &gt; 0 =
        /// the model plays its own death clip (trigger "Die") for that long instead of the tip-over.
        /// </summary>
        public void SetModel(Transform body, Animator anim, float clipSeconds)
        {
            model = body;
            animator = anim;
            deathClip = clipSeconds > 0f;
            if (deathClip) deathClipTime = clipSeconds;
        }

        private void Awake() => _health = GetComponent<PlayerHealth>();
        private void OnEnable() => _health.OnDeath += OnDeath;
        private void OnDisable() => _health.OnDeath -= OnDeath;

        private void OnDeath()
        {
            if (_time >= 0f) return;
            _time = 0f;
            foreach (var b in disableOnDeath)
                if (b != null) b.enabled = false;
            foreach (var go in hideOnDeath)
                if (go != null) go.SetActive(false);
            if (model != null)
            {
                _modelPosition = model.localPosition;
                _modelRotation = model.localRotation;
            }
            if (animator != null)
            {
                animator.SetFloat(LocomotionAnimatorDriver.SpeedId, 0f); // driver is off: no stale run pose after the hit clip
                animator.SetTrigger(deathClip ? DieId : LocomotionAnimatorDriver.HitId);
            }
        }

        private void LateUpdate()
        {
            if (_time < 0f || _finished) return;
            _time += Time.deltaTime;
            if (deathClip)
            {
                // The clip lies the body down and holds its last frame (plays once); no procedural tip.
                if (_time < deathClipTime + settleTime) return;
                _finished = true;
                Finished?.Invoke();
                return;
            }
            float t = Mathf.Clamp01(_time / fallTime);
            float ease = t * t; // accelerates like a toppling body
            if (model != null)
            {
                var tip = Quaternion.AngleAxis(-fallAngle * ease, Vector3.right); // backwards, about the feet
                model.localRotation = tip * _modelRotation;
                model.localPosition = tip * _modelPosition;
            }
            if (t < 1f) return;
            if (animator != null) animator.speed = 0f; // hold the pose on the ground
            if (_time < fallTime + settleTime) return;
            _finished = true;
            Finished?.Invoke();
        }
    }
}
