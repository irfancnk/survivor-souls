using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Rime Blades" (KAN-246, Epic): <see cref="StatId.RimeCount"/> ice blades circle the Knight (<see cref="radius"/> m).
    /// A touched foe takes <see cref="contactShare"/> x the bolt damage and is slowed by <see cref="slowShare"/> for
    /// <see cref="StatId.RimeSlow"/> s (<see cref="Enemy.ApplySlow"/>: slows never stack, the stronger / longer wins; bosses
    /// take half, so a boss is never held for good). Look: pale blue blades with a frost trail.
    /// </summary>
    public sealed class RimeBladesEffect : OrbitRing
    {
        [SerializeField, Min(0.5f)] private float radius = 2.4f;
        [SerializeField, Min(0.1f)] private float spin = 1.8f;
        [SerializeField, Range(0f, 0.8f)] private float slowShare = 0.4f;
        [SerializeField, Range(0f, 2f)] private float contactShare = 0.3f;

        private static readonly Color Shard = new Color(0.6f, 0.85f, 1f, 1f);

        protected override int BodyCount => Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.RimeCount, 0f));
        public override float Radius => radius;
        public override float SecondsPerTurn => -spin; // the other way round from the tomes and the wheel: they cross
        protected override int FormationSlot => 2;
        public float SlowTime => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.RimeSlow, 0f));
        public float SlowShare => slowShare;

        protected override void OnContact(Enemy enemy, int body, Vector3 point)
        {
            float time = SlowTime;
            if (time > 0f) enemy.ApplySlow(slowShare, time);
            float damage = SkillFx.BoltShare(Context, contactShare);
            Vector3 away = enemy.transform.position - (Context.Player != null ? Context.Player.position : transform.position);
            if (damage > 0f) enemy.TakeDamage(damage, away, DamageKind.Burst);
            SkillFx.Sparks(fx, point, 7, Shard, 3f, 0.09f, 0.3f);
        }
    }
}
