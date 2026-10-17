using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Reaper Scythes" (KAN-246, Epic): <see cref="StatId.ScytheCount"/> spectral scythes sweep a wide, slow outer orbit
    /// (<see cref="radius"/> m, <see cref="spin"/> s per turn). A touched foe takes a heavy <see cref="StatId.ScytheDamage"/>
    /// x the bolt damage, once per pass (the contact cooldown is longer than a pass through a foe). Damage, not an execute:
    /// bosses take it like anyone. <see cref="StatId.ScytheSize"/> grows the blades and their reach. Look: pale violet
    /// crescent blades, cold wisps.
    /// </summary>
    public sealed class ReaperScythesEffect : OrbitRing
    {
        [SerializeField, Min(0.5f)] private float radius = 4.2f;
        [SerializeField, Min(0.3f)] private float spin = 3.6f;

        private static readonly Color Wisp = new Color(0.78f, 0.62f, 1f, 1f);

        protected override int BodyCount => Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.ScytheCount, 0f));
        public override float Radius => radius;
        public override float SecondsPerTurn => spin;
        protected override float BodyScale => 1f + Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.ScytheSize, 0f));
        protected override int FormationSlot => 3;
        public float DamageShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.ScytheDamage, 0f));

        protected override void OnContact(Enemy enemy, int body, Vector3 point)
        {
            float damage = SkillFx.BoltShare(Context, DamageShare);
            Vector3 centre = Context.Player != null ? Context.Player.position : transform.position;
            Vector3 radial = enemy.transform.position - centre;
            Vector3 sweep = new Vector3(-radial.z, 0f, radial.x); // along the orbit
            if (damage > 0f) enemy.TakeDamage(damage, sweep, DamageKind.Burst);
            SkillFx.Flash(fx, point, 1f, Wisp, 0.12f);
            SkillFx.Sparks(fx, point, 8, Wisp, 3.4f, 0.12f, 0.35f);
        }

        protected override Quaternion BodyRotation(int index, float angle, Vector3 tangent)
        {
            // Blade edge leads along the orbit, tilted up toward the camera so the crescent reads from above.
            return Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(-35f, 0f, 0f);
        }
    }
}
