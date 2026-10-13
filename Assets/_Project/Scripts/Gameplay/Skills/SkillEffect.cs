using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>What a behaviour skill may touch. Built once by <see cref="SkillManager"/>.</summary>
    public sealed class SkillContext
    {
        public PlayerStats Stats;
        public AutoShooter Shooter;
        public PlayerHealth Health;
        public Transform Player;
    }

    /// <summary>
    /// Runtime part of a behaviour skill (KAN-19): lives on the skill's effect prefab, spawned under the player on the
    /// first stack and destroyed on run reset. Strength comes from <see cref="PlayerStats"/> (the skill's modifiers), so an
    /// effect never counts stacks itself. Subclasses hook game events in <see cref="Subscribe"/>; the base class makes sure
    /// that happens only while bound and enabled, exactly once.
    /// </summary>
    public abstract class SkillEffect : MonoBehaviour
    {
        private bool _subscribed;

        protected SkillContext Context { get; private set; }
        public int Stacks { get; private set; }

        public void Bind(SkillContext context)
        {
            Context = context;
            if (isActiveAndEnabled) Hook();
        }

        public void SetStacks(int stacks)
        {
            Stacks = stacks;
            OnStacksChanged(stacks);
        }

        protected virtual void OnEnable()
        {
            if (Context != null) Hook();
        }

        protected virtual void OnDisable() => Unhook();

        protected abstract void Subscribe();
        protected abstract void Unsubscribe();
        protected virtual void OnStacksChanged(int stacks) { }

        private void Hook()
        {
            if (_subscribed) return;
            _subscribed = true;
            Subscribe();
        }

        private void Unhook()
        {
            if (!_subscribed) return;
            _subscribed = false;
            Unsubscribe();
        }
    }
}
