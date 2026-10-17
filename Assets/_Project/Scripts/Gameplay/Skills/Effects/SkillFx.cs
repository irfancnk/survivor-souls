using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// Shared helpers of the Ember Boons effects (KAN-246): Emit-only particle bursts on one shared world-space system per
    /// effect (no allocation, one draw call per effect), the "hide while the Knight cannot fight" rule of the orbitals, and
    /// small target searches over <see cref="Enemy.Active"/>.
    /// </summary>
    public static class SkillFx
    {
        /// <summary>One soft flash particle at <paramref name="at"/>.</summary>
        public static void Flash(ParticleSystem ps, Vector3 at, float size, Color color, float life = 0.14f)
        {
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            p.position = at;
            p.velocity = Vector3.zero;
            p.startSize = size;
            p.startLifetime = life;
            p.startColor = color;
            ps.Emit(p, 1);
        }

        /// <summary><paramref name="count"/> sparks thrown out of <paramref name="at"/> (upward hemisphere).</summary>
        public static void Sparks(ParticleSystem ps, Vector3 at, int count, Color color, float speed = 3f, float size = 0.1f,
            float life = 0.3f)
        {
            if (ps == null || count <= 0) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.7f + 0.2f;
                p.position = at;
                p.velocity = dir.normalized * speed * Random.Range(0.6f, 1.2f);
                p.startSize = size * Random.Range(0.75f, 1.3f);
                p.startLifetime = life * Random.Range(0.75f, 1.2f);
                p.startColor = color;
                ps.Emit(p, 1);
            }
        }

        /// <summary>A flat ring of <paramref name="count"/> particles of radius <paramref name="radius"/> flying outwards (shockwave).</summary>
        public static void Ring(ParticleSystem ps, Vector3 centre, float radius, int count, Color color, float size = 0.3f,
            float life = 0.35f)
        {
            if (ps == null || count <= 0) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                p.position = centre + dir * (radius * 0.25f) + Vector3.up * 0.25f;
                p.velocity = dir * (radius * 0.75f / Mathf.Max(0.05f, life)) + Vector3.up * Random.Range(0.1f, 0.6f);
                p.startSize = size * Random.Range(0.8f, 1.2f);
                p.startLifetime = life;
                p.startColor = color;
                ps.Emit(p, 1);
            }
        }

        /// <summary>Rising motes round a body (status look: poison bubbles, oath embers).</summary>
        public static void Rise(ParticleSystem ps, Vector3 root, float radius, float height, int count, Color color,
            float size = 0.12f, float life = 0.6f)
        {
            if (ps == null || count <= 0) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector2 ring = Random.insideUnitCircle * radius;
                p.position = root + new Vector3(ring.x, Random.Range(0.2f, height), ring.y);
                p.velocity = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.5f, 1.2f), Random.Range(-0.2f, 0.2f));
                p.startSize = size * Random.Range(0.7f, 1.3f);
                p.startLifetime = life * Random.Range(0.8f, 1.2f);
                p.startColor = color;
                ps.Emit(p, 1);
            }
        }

        /// <summary>
        /// True while the Knight cannot fight: dead, a Chamber transition runs or the boss intro holds him. Orbitals hide and
        /// stop hitting, timers wait (same rule as Holy Tomes, KAN-123).
        /// </summary>
        public static bool ShouldHide(SkillContext context)
        {
            if (context == null) return true;
            var health = context.Health;
            if (health != null && health.IsDead) return true;
            var transition = ChamberTransition.Active;
            if (transition != null && transition.Current != ChamberTransition.Phase.Idle) return true;
            var boss = BossFight.Active;
            return boss != null && boss.KnightHeld;
        }

        /// <summary>Nearest living foe within <paramref name="range"/> m of <paramref name="from"/> on the ground plane, not <paramref name="except"/>.</summary>
        public static Enemy Nearest(Vector3 from, float range, Enemy except = null)
        {
            Enemy best = null;
            float bestSq = range * range;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == except || !e.IsAlive) continue;
                Vector3 d = e.transform.position - from;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        /// <summary>Damage of a skill as <paramref name="share"/> of the shooter's current bolt damage (100 without a shooter).</summary>
        public static float BoltShare(SkillContext context, float share) =>
            (context?.Shooter != null ? context.Shooter.CurrentDamage : 100f) * Mathf.Max(0f, share);
    }
}
