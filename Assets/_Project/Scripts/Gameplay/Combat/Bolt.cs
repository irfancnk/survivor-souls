using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Ember Bolts look (KAN-200): 0 = a normal bolt, above 0 = a blazing bolt (hot head, fire trail, sparks), 1 = the
        /// hottest (Ember Bolts level 4). Look only: damage and burn come from the skill effects.
        /// </summary>
        public float Heat;
        /// <summary>
        /// Point-blank guard (KAN-226): the first metres (flat, along the launch direction) on which the bolt ignores enemies,
        /// = the part of the path behind the shooter's root. Cleared by the first hit. 0 = none (the struct default).
        /// </summary>
        public float Guard;
        /// <summary>Bouncy Wall (KAN-246): rebounds off walls left. 0 = the bolt flies through walls as before.</summary>
        public int WallBounces;
        /// <summary>Bouncy Wall: damage share the bolt keeps after each rebound (0.6 = 60 %).</summary>
        public float WallKeep;

        /// <summary>Shipped falloffs (KAN-86): AutoShooter defaults and the power curve base.</summary>
        public const float DefaultPierceFalloff = 0.33f;
        public const float DefaultBounceFalloff = 0.3f;
        /// <summary>
        /// Shared hit budget (KAN-246): pierces + ricochet bounces of one bolt never exceed this, so the two skills together
        /// cannot make a bolt hit a whole horde.
        /// </summary>
        public const int MaxExtraHits = 8;
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
    /// Hit budget (KAN-246): Ricochet goes first when a living foe not hit yet is within reach, else the bolt pierces on;
    /// pierces + bounces share one budget (<see cref="BoltShot.MaxExtraHits"/>, capped by the shooter). Bouncy Wall
    /// (<see cref="BoltShot.WallBounces"/>): a bolt that meets a wall (<see cref="Enemy.Walkability"/>) rebounds with
    /// <see cref="BoltShot.WallKeep"/> of its damage and may hit the foes it hit before again.
    /// Blazing look (KAN-200): a shot with <see cref="BoltShot.Heat"/> above 0 shows the hot head child, swaps the trail to
    /// the fire material, a longer, wider fire gradient (hotter = whiter core, longer, wider) and lists the bolt in
    /// <see cref="Blazing"/> while it flies (the shared spark system reads it). A pooled bolt changes its look only when the
    /// heat of its new shot differs from the last one: no allocation per shot.
    /// </summary>
    public sealed class Bolt : MonoBehaviour
    {
        /// <summary>Raised when a bolt hits an enemy: hit point, flight direction. Impact FX and audio listen.</summary>
        public static event Action<Vector3, Vector3> Hit;
        /// <summary>Bouncy Wall (KAN-246): a bolt rebounded off a wall: point, new flight direction.</summary>
        public static event Action<Vector3, Vector3> Rebounded;

        /// <summary>Wall test radius of a rebounding bolt (m).</summary>
        private const float WallProbe = 0.12f;

        private const int MaxTracked = 16;

        [SerializeField] private TrailRenderer trail;
        [SerializeField] private Transform model;
        [SerializeField] private float spinDegreesPerSecond = 900f;
        [Header("Blazing look (KAN-200)")]
        [Tooltip("Hot head child (glow quads at the bolt head), shown only on blazing bolts.")]
        [SerializeField] private Transform blazeHead;
        [Tooltip("Trail material of blazing bolts (alpha blend: additive fire vanishes on the Realm 1 snow).")]
        [SerializeField] private Material blazeTrailMaterial;

        /// <summary>Blazing bolts in flight (KAN-200): the shared bolt spark system emits along them. Never edit.</summary>
        public static readonly List<Bolt> Blazing = new List<Bolt>(64);

        private const float NoLook = -1f;

        private readonly Enemy[] _hitEnemies = new Enemy[MaxTracked];
        private int _hitCount;
        private BoltShot _shot;
        private Vector3 _velocity;
        private float _life;
        private Vector3 _origin;     // launch point (KAN-226 guard)
        private Vector3 _guardDir;   // flat unit launch direction
        private float _guard;        // ignore enemies whose centre is less than this far along _guardDir from _origin
        private Action<Bolt> _release;
        private Action<BoltHit> _onHit;
        private float _lookHeat = NoLook;
        private int _blazingIndex = -1;
        private Material _trailMaterial;
        private Gradient _trailGradient;
        private float _trailTime;
        private float _trailWidth = 1f;
        private Gradient _blazeGradient;
        private GradientColorKey[] _blazeColors;
        private GradientAlphaKey[] _blazeAlphas;

        public bool InFlight => gameObject.activeSelf;
        /// <summary>Heat of the look on show (KAN-200): 0 = normal bolt, above 0 = blazing.</summary>
        public float Heat => Mathf.Max(0f, _lookHeat);
        public bool IsBlazing => _lookHeat > 0f;
        /// <summary>Flight direction (unit length; bounces turn it).</summary>
        public Vector3 Direction => _shot.Speed > 0f ? _velocity / _shot.Speed : transform.forward;

        public void Launch(Vector3 position, Vector3 direction, in BoltShot shot, Action<Bolt> release, Action<BoltHit> onHit = null)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            float heat = Mathf.Clamp01(shot.Heat);
            if (heat != _lookHeat) ApplyLook(heat);
            _shot = shot;
            _velocity = direction * shot.Speed;
            _life = shot.Lifetime;
            _origin = position;
            _guardDir = new Vector3(direction.x, 0f, direction.z);
            _guard = shot.Guard > 0f && _guardDir.sqrMagnitude > 1e-8f ? shot.Guard : 0f;
            if (_guard > 0f) _guardDir.Normalize();
            _release = release;
            _onHit = onHit;
            _hitCount = 0;
            Array.Clear(_hitEnemies, 0, MaxTracked);
            gameObject.SetActive(true);
            if (trail != null) trail.Clear();
            if (_lookHeat > 0f) AddBlazing();
        }

        /// <summary>
        /// Switches between the normal and the blazing look (KAN-200). Runs only when the heat of a new shot differs from
        /// the last one, so a pooled bolt firing the same look again touches nothing.
        /// </summary>
        private void ApplyLook(float heat)
        {
            bool blaze = heat > 0f;
            if (trail != null)
            {
                if (_trailMaterial == null) // first look change: remember the normal trail
                {
                    _trailMaterial = trail.sharedMaterial;
                    _trailGradient = trail.colorGradient;
                    _trailTime = trail.time;
                    _trailWidth = trail.widthMultiplier;
                }
                if (blaze)
                {
                    if (blazeTrailMaterial != null) trail.sharedMaterial = blazeTrailMaterial;
                    trail.colorGradient = BlazeGradient(heat);
                    trail.time = _trailTime * Mathf.Lerp(1.05f, 1.3f, heat);
                    trail.widthMultiplier = _trailWidth * Mathf.Lerp(3f, 4.2f, heat);
                }
                else
                {
                    trail.sharedMaterial = _trailMaterial;
                    trail.colorGradient = _trailGradient;
                    trail.time = _trailTime;
                    trail.widthMultiplier = _trailWidth;
                }
            }
            if (blazeHead != null)
            {
                if (blazeHead.gameObject.activeSelf != blaze) blazeHead.gameObject.SetActive(blaze);
                if (blaze) blazeHead.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.3f, heat);
            }
            _lookHeat = heat;
        }

        /// <summary>
        /// Fire trail (KAN-200): a yellow-white head (whiter when hotter), orange, then a dark red tail that still reads on
        /// snow (alpha blend). Keys are reused, one Gradient per bolt.
        /// </summary>
        private Gradient BlazeGradient(float heat)
        {
            if (_blazeGradient == null)
            {
                _blazeGradient = new Gradient();
                _blazeColors = new GradientColorKey[3];
                _blazeAlphas = new GradientAlphaKey[3];
            }
            _blazeColors[0] = new GradientColorKey(Color.Lerp(new Color(1f, 0.82f, 0.4f), new Color(1f, 0.96f, 0.78f), heat), 0f);
            _blazeColors[1] = new GradientColorKey(new Color(1f, Mathf.Lerp(0.45f, 0.58f, heat), 0.1f), 0.3f);
            _blazeColors[2] = new GradientColorKey(new Color(0.6f, 0.12f, 0.04f), 1f);
            _blazeAlphas[0] = new GradientAlphaKey(1f, 0f);
            _blazeAlphas[1] = new GradientAlphaKey(Mathf.Lerp(0.7f, 0.85f, heat), 0.35f);
            _blazeAlphas[2] = new GradientAlphaKey(0f, 1f);
            _blazeGradient.SetKeys(_blazeColors, _blazeAlphas);
            return _blazeGradient;
        }

        private void AddBlazing()
        {
            if (_blazingIndex >= 0) return;
            _blazingIndex = Blazing.Count;
            Blazing.Add(this);
        }

        /// <summary>Swap-remove from <see cref="Blazing"/> (order does not matter).</summary>
        private void RemoveBlazing()
        {
            int i = _blazingIndex;
            if (i < 0) return;
            _blazingIndex = -1;
            int last = Blazing.Count - 1;
            if (i > last || Blazing[i] != this) { Blazing.Remove(this); return; } // list was cleared under us
            if (i != last)
            {
                var moved = Blazing[last];
                Blazing[i] = moved;
                moved._blazingIndex = i;
            }
            Blazing.RemoveAt(last);
        }

        private void OnDisable() => RemoveBlazing();

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
                if (WasHit(e) || (_guard > 0f && Guarded(e)) || !Crosses(e, from, to, out float t) || t >= bestT) continue;
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
            else if (_shot.WallBounces > 0 && Enemy.Walkability != null && Enemy.Walkability.IsBlocked(to, WallProbe))
            {
                Rebound(from, to); // KAN-246 Bouncy Wall: stays at the wall face this step, flies back next step
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
            _guard = 0f; // the guard covers only the first leg up to the first hit
            if (_hitCount < MaxTracked) _hitEnemies[_hitCount++] = enemy;
            float deducted = enemy.TakeDamage(damage, _velocity, DamageKind.Direct, crit);
            Hit?.Invoke(point, dir);
            _onHit?.Invoke(new BoltHit
            {
                Enemy = enemy, Point = point, Direction = dir, Damage = deducted, Crit = crit, Killed = !enemy.IsAlive,
            });

            // KAN-246 shared hit budget: Ricochet first when a valid new target is near, else pierce on.
            if (_shot.Ricochet > 0 && Bounce(point))
            {
                _shot.Ricochet--;
                _shot.Damage *= 1f - Mathf.Clamp01(_shot.BounceFalloff);
                return;
            }
            if (_shot.Pierce > 0)
            {
                _shot.Pierce--;
                _shot.Damage *= 1f - Mathf.Clamp01(_shot.PierceFalloff);
                return;
            }
            Finish();
        }

        /// <summary>
        /// Bouncy Wall (KAN-246): mirrors the flight off the wall between <paramref name="from"/> and <paramref name="to"/>
        /// (the blocked axis flips; a corner flips both), keeps <see cref="BoltShot.WallKeep"/> of the damage and forgets the
        /// foes hit so far (the returning bolt may hit them again). A bolt that starts inside a wall just flies on.
        /// </summary>
        private void Rebound(Vector3 from, Vector3 to)
        {
            var walk = Enemy.Walkability;
            if (walk.IsBlocked(from, WallProbe))
            {
                transform.position = to;
                return;
            }
            bool blockX = walk.IsBlocked(new Vector3(to.x, to.y, from.z), WallProbe);
            bool blockZ = walk.IsBlocked(new Vector3(from.x, to.y, to.z), WallProbe);
            if (!blockX && !blockZ) blockX = blockZ = true; // corner: straight back
            if (blockX) _velocity.x = -_velocity.x;
            if (blockZ) _velocity.z = -_velocity.z;
            _shot.WallBounces--;
            _shot.Damage *= Mathf.Clamp01(_shot.WallKeep);
            _guard = 0f;
            Array.Clear(_hitEnemies, 0, _hitCount);
            _hitCount = 0;
            _life = Mathf.Max(_life, 0.6f);
            Vector3 dir = _velocity.normalized;
            transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            Rebounded?.Invoke(from, dir);
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

        /// <summary>True when the enemy centre lies on the guarded first metres of the path (behind the shooter, KAN-226).</summary>
        private bool Guarded(Enemy e)
        {
            Vector3 d = e.transform.position - _origin;
            return d.x * _guardDir.x + d.z * _guardDir.z < _guard;
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
            RemoveBlazing();
            gameObject.SetActive(false);
            Array.Clear(_hitEnemies, 0, _hitCount);
            _hitCount = 0;
            _onHit = null;
            _release?.Invoke(this);
        }
    }
}
