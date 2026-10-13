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

        /// <summary>Raised once, after the fall settles.</summary>
        public event Action Finished;

        private PlayerHealth _health;
        private Vector3 _modelPosition;
        private Quaternion _modelRotation;
        private float _time = -1f; // < 0 = alive
        private bool _finished;

        public bool IsDying => _time >= 0f;

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
                animator.SetTrigger(LocomotionAnimatorDriver.HitId);
            }
        }

        private void LateUpdate()
        {
            if (_time < 0f || _finished) return;
            _time += Time.deltaTime;
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
