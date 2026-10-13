using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// One light haptic tap per enemy kill (KAN-12). Listens to <see cref="Enemy.Damaged"/> (same kill flag that drives
    /// the kill marker). Settings toggle and the group-kill rate limit live in <see cref="Haptics"/>.
    /// </summary>
    public sealed class KillHaptics : MonoBehaviour
    {
        private void Awake() => Haptics.Warmup();
        private void OnEnable() => Enemy.Damaged += OnDamaged;
        private void OnDisable() => Enemy.Damaged -= OnDamaged;

        private static void OnDamaged(Enemy enemy, float deducted, bool killed)
        {
            if (killed) Haptics.Light();
        }
    }
}
