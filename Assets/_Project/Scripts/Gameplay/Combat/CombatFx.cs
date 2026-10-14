using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Shared one-shot particle effects for combat: a muzzle puff + sparks when the crossbow fires and
    /// sparks + dust where a bolt hits. One particle system per effect (world space), moved to the event and
    /// burst with <see cref="ParticleSystem.Emit(int)"/>, so there is no per-shot allocation.
    /// Listens to <see cref="AutoShooter.Fired"/> (on <see cref="shooter"/>) and <see cref="Bolt.Hit"/>.
    /// Built by CombatBuilder.
    /// </summary>
    public sealed class CombatFx : MonoBehaviour
    {
        [SerializeField] private AutoShooter shooter;
        [SerializeField] private ParticleSystem muzzleFlash;
        [SerializeField] private ParticleSystem muzzleSparks;
        [SerializeField] private ParticleSystem impactSparks;
        [SerializeField] private ParticleSystem impactDust;
        [SerializeField, Min(0)] private int muzzleSparkCount = 7;
        [SerializeField, Min(0)] private int impactSparkCount = 12;
        [SerializeField, Min(0)] private int impactDustCount = 4;

        private void OnEnable()
        {
            if (shooter != null)
            {
                shooter.Fired += OnFired;
                shooter.FollowUpFired += OnFired; // Double Shot follow-ups puff too (KAN-83)
            }
            Bolt.Hit += OnHit;
        }

        private void OnDisable()
        {
            if (shooter != null)
            {
                shooter.Fired -= OnFired;
                shooter.FollowUpFired -= OnFired;
            }
            Bolt.Hit -= OnHit;
        }

        private void OnFired(Vector3 muzzle, Vector3 direction)
        {
            Burst(muzzleFlash, muzzle, direction, 1);
            Burst(muzzleSparks, muzzle, direction, muzzleSparkCount);
        }

        private void OnHit(Vector3 point, Vector3 direction)
        {
            // Sparks fly back toward the shooter, dust puffs out around the hit.
            Burst(impactSparks, point, -direction, impactSparkCount);
            Burst(impactDust, point, -direction, impactDustCount);
        }

        private static void Burst(ParticleSystem ps, Vector3 position, Vector3 direction, int count)
        {
            if (ps == null || count <= 0) return;
            ps.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.sqrMagnitude > 1e-6f ? direction : Vector3.forward));
            ps.Emit(count);
        }
    }
}
