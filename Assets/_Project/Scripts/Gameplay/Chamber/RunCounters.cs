using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Progression;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Run counters (KAN-51): enemies killed and souls collected (XP value of every absorbed wisp, merged drops counted
    /// at their full value) since the run started. Lives in the Game scene, so a scene reload starts at 0; the portal
    /// carries the numbers to the next Chamber in the <see cref="RunSnapshot"/> (<see cref="ChamberTransition.Capture"/>
    /// / <see cref="ChamberTransition.Apply"/>). A new run (menu PLAY, RETRY) carries nothing, so it starts at 0 again.
    /// Shown on the death screen (<c>PlayerHealthHud</c>) and later on the KAN-47 victory screen.
    /// </summary>
    public sealed class RunCounters : MonoBehaviour
    {
        [SerializeField] private SoulWispField souls;

        public int Kills { get; private set; }
        public int Souls { get; private set; }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(SoulWispField soulField)
        {
            if (isActiveAndEnabled && souls != null) souls.Collected -= OnCollected;
            souls = soulField;
            if (isActiveAndEnabled && souls != null) souls.Collected += OnCollected;
        }

        private void OnEnable()
        {
            Enemy.Damaged += OnDamaged;
            if (souls != null) souls.Collected += OnCollected;
        }

        private void OnDisable()
        {
            Enemy.Damaged -= OnDamaged;
            if (souls != null) souls.Collected -= OnCollected;
        }

        private void OnDamaged(Enemy enemy, float deducted, bool killed)
        {
            if (killed) AddKill();
        }

        private void OnCollected(Vector3 at, int value) => AddSouls(value);

        public void AddKill() => Kills++;

        public void AddSouls(int value)
        {
            if (value > 0) Souls += value;
        }

        /// <summary>Puts carried numbers back after a portal (negative values clamp to 0).</summary>
        public void Restore(int kills, int soulCount)
        {
            Kills = Mathf.Max(0, kills);
            Souls = Mathf.Max(0, soulCount);
        }
    }
}
