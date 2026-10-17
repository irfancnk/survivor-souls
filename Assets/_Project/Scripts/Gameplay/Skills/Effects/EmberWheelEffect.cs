using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Ember Wheel" (KAN-246, Epic): <see cref="StatId.WheelCount"/> firebrands circle close to the Knight
    /// (<see cref="radius"/> m, one turn per <see cref="StatId.WheelSpin"/> s). A touched foe takes <see cref="contactShare"/>
    /// x the bolt damage and catches fire: <see cref="StatId.WheelBurn"/> damage per second for <see cref="burnTime"/> s
    /// through <see cref="Enemy.ApplyBurn"/>, the same burn Ember Bolts uses (one burn per foe: the stronger one wins and the
    /// time refreshes; nothing stacks). Look: glowing brand bodies + ember trail.
    /// </summary>
    public sealed class EmberWheelEffect : OrbitRing
    {
        [SerializeField, Min(0.5f)] private float radius = 1.7f;
        [SerializeField, Min(0.1f)] private float burnTime = 3f;
        [SerializeField, Range(0f, 2f)] private float contactShare = 0.2f;

        private static readonly Color Spark = new Color(1f, 0.6f, 0.15f, 1f);

        protected override int BodyCount => Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.WheelCount, 0f));
        public override float Radius => radius;
        public override float SecondsPerTurn => Mathf.Max(0.3f, PlayerStats.Eval(Context?.Stats, StatId.WheelSpin, 0f));
        protected override int FormationSlot => 1;
        public float BurnDps => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.WheelBurn, 0f));
        public float BurnTime => burnTime;

        protected override void OnContact(Enemy enemy, int body, Vector3 point)
        {
            float dps = BurnDps;
            if (dps > 0f) enemy.ApplyBurn(dps, burnTime);
            float damage = SkillFx.BoltShare(Context, contactShare);
            Vector3 away = enemy.transform.position - (Context.Player != null ? Context.Player.position : transform.position);
            if (damage > 0f) enemy.TakeDamage(damage, away, DamageKind.Burst);
            SkillFx.Sparks(fx, point, 6, Spark, 2.8f, 0.1f, 0.3f);
        }

        protected override Quaternion BodyRotation(int index, float angle, Vector3 tangent) =>
            Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(Clock * 540f, 0f, 0f); // tumbling brand
    }
}
