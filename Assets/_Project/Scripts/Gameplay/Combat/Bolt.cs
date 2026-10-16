using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>Everything a bolt carries from the shooter's stats at the moment it is fired (KAN-19 skills).</summary>
    public struct BoltShot
    {
        public float Damage;
        public float Speed;
        public float Lifetime;
        /// <summary>0..1 chance per hit to deal <see cref="CritMultiplier"/> times damage.</summary>
        public float CritChance;
        public float CritMultiplier;
        /// <summary>Enemies the bolt flies through before it can stop.</summary>
        public int Pierce;
        /// <summary>Bounces to the nearest enemy not hit yet, after the pierces are used.</summary>
        public int Ricochet;
        public float RicochetRange;
        /// <summary>
        /// Share of damage lost each time the bolt pierces on (KAN-86, Archero rule): 0.33 = the next enemy takes 67 %,
        /// the one after 45 %. 0 = full damage (the struct default).
        /// </summary>
        public float PierceFalloff;
        /// <summary>Share of damage lost on each ricochet bounce (KAN-86): 0.3 = 70 % after the first bounce. 0 = full damage.</summary>
        public float BounceFalloff;

        /// <summary>Shipped falloffs (KAN-86): AutoShooter defaults and the power curve base.</summary>
        public const float DefaultPierceFalloff = 0.33f;
        public const float DefaultBounceFalloff = 0.3f;
    }

    /// <summary>One enemy hit by a bolt, reported to the shooter (life steal, burn and other on-hit skills listen).</summary>
    public struct BoltHit
    {
        public Enemy Enemy;
        public Vector3 Point;
        public Vector3 Direction;
        /// <summary>Health actually deducted.</summary>
        public float Damage;
        public bool Crit;
        public bool Killed;
    }

    /// <summary>
    /// Crossbow bolt: flies straight and hits the nearest living <see cref="Enemy"/> its path crosses (vertical cylinder
    /// test, no physics). After a hit it pierces on (<see cref="BoltShot.Pierce"/>), then bounces to the nearest enemy it
    /// has not hit yet (<see cref="BoltShot.Ricochet"/>), else returns to its pool. Never hits the same enemy twice.
    /// Each pierce and bounce lowers the damage for the next enemy (<see cref="BoltShot.PierceFalloff"/>, KAN-86).
    /// Pooled by <see cref="AutoShooter"/>. The model child spins around the flight axis (rifled bolt look).
    /// </summary>
    public sealed class Bolt : MonoBehaviour
    {
        /// <summary>Raised when a bolt hits an enemy: hit point, flight direction. Impact FX and audio listen.</summary>
        public static event Action<Vector3, Vector3> Hit;

        private const int MaxTracked = 16;

        [SerializeField] private TrailRenderer trail;
        [SerializeField] private Transform model;
        [SerializeField] private float spinDegreesPerSecond = 900f;

        private readonly Enemy[] _hitEnemies = new Enemy[MaxTracked];
        private int _hitCount;
        private BoltShot _shot;
        private Vector3 _velocity;
        private float _life;
        private Action<Bolt> _release;
        private Action<BoltHit> _onHit;

        public bool InFlight => gameObject.activeSelf;

        public void Launch(Vector3 position, Vector3 direction, in BoltShot shot, Action<Bolt> release, Action<BoltHit> onHit = null)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            _shot = shot;
            _velocity = direction * shot.Speed;
            _life = shot.Lifetime;
            _release = release;
            _onHit = onHit;
            _hitCount = 0;
            Array.Clear(_hitEnemies, 0, MaxTracked);
            gameObject.SetActive(true);
            if (trail != null) trail.Clear();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Moves the bolt one step and resolves at most one hit. Public for tests (Update calls it).</summary>
        public void Tick(float dt)
        {
            if (!gameObject.activeSelf) return;
            Vector3 from = transform.position;
            Vector3 to = from + _velocity * dt;

            // Nearest crossing along the path. Found first, damaged after: a kill edits Enemy.Active.
            Enemy target = null;
            float bestT = float.MaxValue;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (WasHit(e) || !Crosses(e, from, to, out float t) || t >= bestT) continue;
                bestT = t;
                target = e;
            }

            if (target != null)
            {
                Vector3 point = ClosestPointTo(target, from, to);
                Strike(target, point);
                if (!gameObject.activeSelf) return; // bolt spent
                transform.position = point; // carry on from the body, so an enemy right behind is not skipped
            }
            else
            {
                transform.position = to;
            }

            if (model != null) model.Rotate(0f, 0f, spinDegreesPerSecond * dt, Space.Self);
            _life -= dt;
            if (_life <= 0f) Finish();
        }

        private void Strike(Enemy enemy, Vector3 point)
        {
            bool crit = _shot.CritChance >= 1f || (_shot.CritChance > 0f && UnityEngine.Random.value < _shot.CritChance);
            float damage = crit ? _shot.Damage * Mathf.Max(1f, _shot.CritMultiplier) : _shot.Damage;
            Vector3 dir = _velocity.normalized;
            if (_hitCount < MaxTracked) _hitEnemies[_hitCount++] = enemy;
            float deducted = enemy.TakeDamage(damage, _velocity, DamageKind.Direct, crit);
            Hit?.Invoke(point, dir);
            _onHit?.Invoke(new BoltHit
            {
                Enemy = enemy, Point = point, Direction = dir, Damage = deducted, Crit = crit, Killed = !enemy.IsAlive,
            });

            if (_shot.Pierce > 0)
            {
                _shot.Pierce--;
                _shot.Damage *= 1f - Mathf.Clamp01(_shot.PierceFalloff);
                return;
            }
            if (_shot.Ricochet > 0 && Bounce(point))
            {
                _shot.Ricochet--;
                _shot.Damage *= 1f - Mathf.Clamp01(_shot.BounceFalloff);
                return;
            }
            Finish();
        }

        /// <summary>Turns toward the nearest living enemy not hit yet within ricochet range. False when none.</summary>
        private bool Bounce(Vector3 from)
        {
            Enemy best = null;
            float bestSq = _shot.RicochetRange * _shot.RicochetRange;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (WasHit(e)) continue;
                Vector3 d = e.transform.position - from;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            if (best == null) return false;
            Vector3 dir = best.AimPoint - from;
            if (dir.sqrMagnitude < 1e-6f) return false;
            dir.Normalize();
            _velocity = dir * _shot.Speed;
            transform.rotation = Quaternion.LookRotation(dir);
            _life = Mathf.Max(_life, _shot.RicochetRange * 1.5f / Mathf.Max(1f, _shot.Speed));
            return true;
        }

        private bool WasHit(Enemy e)
        {
            for (int i = 0; i < _hitCount; i++)
                if (_hitEnemies[i] == e) return true;
            return false;
        }

        /// <summary>
        /// Does the segment pass within the enemy's hit radius (XZ) at body height? <paramref name="t"/> = 0..1 along the
        /// segment where it passes closest (orders several enemies in one step).
        /// </summary>
        private static bool Crosses(Enemy enemy, Vector3 from, Vector3 to, out float t)
        {
            Vector3 c = enemy.transform.position;
            Vector2 a = new Vector2(from.x - c.x, from.z - c.z);
            Vector2 ab = new Vector2(to.x - from.x, to.z - from.z);
            float len2 = ab.sqrMagnitude;
            t = len2 > 0f ? Mathf.Clamp01(-Vector2.Dot(a, ab) / len2) : 0f;
            if ((a + ab * t).sqrMagnitude > enemy.HitRadius * enemy.HitRadius) return false;
            float y = Mathf.Lerp(from.y, to.y, t) - c.y;
            return y >= 0f && y <= enemy.Height;
        }

        /// <summary>Point on the segment nearest the enemy axis, pulled back to its hit radius (the body surface).</summary>
        private static Vector3 ClosestPointTo(Enemy enemy, Vector3 from, Vector3 to)
        {
            Vector3 c = enemy.transform.position;
            Vector3 ab = to - from;
            Vector2 abXZ = new Vector2(ab.x, ab.z);
            float t = abXZ.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(new Vector2(c.x - from.x, c.z - from.z), abXZ) / abXZ.sqrMagnitude) : 0f;
            Vector3 p = from + ab * t;
            Vector3 back = ab.normalized * enemy.HitRadius;
            return p - back;
        }

        private void Finish()
        {
            gameObject.SetActive(false);
            Array.Clear(_hitEnemies, 0, _hitCount);
            _hitCount = 0;
            _onHit = null;
            _release?.Invoke(this);
        }
    }
}
