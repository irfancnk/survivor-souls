using SurvivorSouls.Gameplay.CameraRig;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Strike effects of the special melee zombies (KAN-132), one scene object with shared Emit-only particle systems (no
    /// GameObject per attack, no GC per frame):
    /// <list type="bullet">
    /// <item>Plague Bloater burst (<see cref="BloaterBurst.Burst"/>): a splash of toxic green spores flung out and falling, a
    /// green pop puff over the body (the body vanishes in it, <see cref="Enemy.Vanish"/>), a small shake when the Knight is
    /// inside the circle.</item>
    /// <item>Frenzied Ghoul: a snow kick at the take-off (<see cref="SpecialAttack.Struck"/>), a snow dust puff at the
    /// landing (<see cref="GhoulLeap.Landed"/>).</item>
    /// <item>Grave Brute slam (<see cref="SpecialAttack.Struck"/>): a ground crack decal (fades over <see cref="crackLife"/> s),
    /// a dust burst at the fists, a dust ring that follows the wave front (<see cref="BruteSlam.Front"/>), and a camera shake
    /// while the Knight is within <see cref="shakeRange"/> m (stronger closer).</item>
    /// </list>
    /// Alpha-blend materials (additive dust vanishes on the Realm 1 snow, KAN-199).
    /// </summary>
    public sealed class SpecialMeleeFx : MonoBehaviour
    {
        [SerializeField] private ParticleSystem spores;
        [SerializeField] private ParticleSystem puffs;
        [SerializeField] private ParticleSystem dust;
        [SerializeField] private ParticleSystem cracks;
        [SerializeField] private TopDownFollowCamera followCamera;

        [Header("Bloater burst")]
        [SerializeField, Min(0)] private int sporeCount = 22;
        [SerializeField] private Vector2 sporeSpeed = new Vector2(2.2f, 5.5f);
        [SerializeField] private Color sporeColor = new Color(0.55f, 0.82f, 0.12f, 0.95f);
        [SerializeField] private Color sporeDark = new Color(0.25f, 0.42f, 0.04f, 0.95f);
        [SerializeField, Min(0)] private int puffCount = 5;
        [SerializeField] private Color puffColor = new Color(0.6f, 0.8f, 0.25f, 0.7f);
        [SerializeField, Min(0f)] private float burstShake = 0.08f;

        [Header("Ghoul leap")]
        [SerializeField, Min(0)] private int kickCount = 6;
        [SerializeField, Min(0)] private int landCount = 12;
        [SerializeField] private Color snowDust = new Color(0.86f, 0.88f, 0.92f, 0.8f);
        [SerializeField] private Color earthDust = new Color(0.42f, 0.38f, 0.34f, 0.8f);

        [Header("Brute slam")]
        [SerializeField, Min(0.1f)] private float crackSize = 3.4f;
        [SerializeField, Min(0.1f)] private float crackLife = 1.5f;
        [SerializeField] private Color crackColor = new Color(0.16f, 0.1f, 0.07f, 0.92f);
        [SerializeField, Min(0)] private int slamDustCount = 14;
        [Tooltip("Dust particles per metre of wave front per second while the ring grows.")]
        [SerializeField, Min(0f)] private float ringDustDensity = 9f;
        [SerializeField, Min(1)] private int ringDustMaxPerFrame = 14;
        [SerializeField, Min(0f)] private float shakeRange = 6f;
        [SerializeField, Min(0f)] private float slamShake = 0.14f;
        [SerializeField, Min(0f)] private float slamShakeTime = 0.3f;

        private const int MaxSlams = 6;
        private readonly BruteSlam[] _slams = new BruteSlam[MaxSlams];
        private float _ringDustCarry;

        /// <summary>Spore splashes (tests).</summary>
        public int Bursts { get; private set; }
        /// <summary>Ghoul landings with a dust puff (tests).</summary>
        public int Landings { get; private set; }
        /// <summary>Brute slams with a crack decal (tests).</summary>
        public int Slams { get; private set; }
        /// <summary>Camera shakes started (tests).</summary>
        public int Shakes { get; private set; }
        /// <summary>Strength of the last shake (tests).</summary>
        public float LastShake { get; private set; }
        /// <summary>Brute rings the dust follows now (tests).</summary>
        public int TrackedSlams
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxSlams; i++) if (_slams[i] != null) n++;
                return n;
            }
        }
        /// <summary>Dust particles emitted along wave fronts (tests).</summary>
        public int RingDust { get; private set; }

        /// <summary>Wires the systems and the camera (tests; the scene builder sets the serialized fields).</summary>
        public void Setup(ParticleSystem sporeSystem, ParticleSystem puffSystem, ParticleSystem dustSystem, ParticleSystem crackSystem,
            TopDownFollowCamera camera)
        {
            spores = sporeSystem;
            puffs = puffSystem;
            dust = dustSystem;
            cracks = crackSystem;
            followCamera = camera;
        }

        private void OnEnable()
        {
            BloaterBurst.Burst += OnBurst;
            GhoulLeap.Landed += OnLanded;
            SpecialAttack.Struck += OnStruck;
        }

        private void OnDisable()
        {
            BloaterBurst.Burst -= OnBurst;
            GhoulLeap.Landed -= OnLanded;
            SpecialAttack.Struck -= OnStruck;
            for (int i = 0; i < MaxSlams; i++) _slams[i] = null;
        }

        private void OnBurst(BloaterBurst bloater, Vector3 centre, float dealt, bool byKill)
        {
            Bursts++;
            float height = bloater != null ? Mathf.Max(0.8f, bloater.Enemy.Height * 0.55f) : 1f;
            if (spores != null)
            {
                for (int i = 0; i < sporeCount; i++)
                {
                    Vector2 d = Random.insideUnitCircle.normalized;
                    float speed = Random.Range(sporeSpeed.x, sporeSpeed.y);
                    var e = new ParticleSystem.EmitParams
                    {
                        position = centre + new Vector3(d.x * 0.3f, height + Random.Range(-0.25f, 0.25f), d.y * 0.3f),
                        velocity = new Vector3(d.x * speed, Random.Range(1.5f, 4.5f), d.y * speed),
                        startColor = Color.Lerp(sporeDark, sporeColor, Random.value),
                        startSize = Random.Range(0.12f, 0.3f),
                        startLifetime = Random.Range(0.55f, 0.95f),
                    };
                    spores.Emit(e, 1);
                }
            }
            if (puffs != null)
            {
                for (int i = 0; i < puffCount; i++)
                {
                    Vector2 d = Random.insideUnitCircle * 0.5f;
                    var e = new ParticleSystem.EmitParams
                    {
                        position = centre + new Vector3(d.x, height * Random.Range(0.6f, 1.1f), d.y),
                        velocity = new Vector3(d.x * 2f, Random.Range(0.4f, 1.2f), d.y * 2f),
                        startColor = puffColor,
                        startSize = Random.Range(1.3f, 2.1f),
                        startLifetime = Random.Range(0.45f, 0.7f),
                    };
                    puffs.Emit(e, 1);
                }
            }
            if (bloater != null && Planar(KnightPosition(out bool has), centre) <= bloater.Config.radius && has) Shake(burstShake, 0.25f);
        }

        private void OnLanded(GhoulLeap ghoul, Vector3 at, bool hit)
        {
            Landings++;
            DustRing(at, landCount, 0.35f, new Vector2(1.6f, 3f), new Vector2(0.5f, 0.9f));
        }

        private void OnStruck(SpecialAttack attack)
        {
            if (attack is GhoulLeap ghoul)
            {
                Vector3 back = -ghoul.LeapDirection;
                for (int i = 0; i < kickCount && dust != null; i++)
                {
                    Vector2 j = Random.insideUnitCircle * 0.6f;
                    var e = new ParticleSystem.EmitParams
                    {
                        position = attack.transform.position + new Vector3(j.x * 0.4f, 0.1f, j.y * 0.4f),
                        velocity = new Vector3(back.x * 2.2f + j.x, Random.Range(0.8f, 1.8f), back.z * 2.2f + j.y),
                        startColor = Color.Lerp(snowDust, earthDust, Random.value * 0.35f),
                        startSize = Random.Range(0.35f, 0.6f),
                        startLifetime = Random.Range(0.4f, 0.65f),
                    };
                    dust.Emit(e, 1);
                }
                return;
            }
            if (!(attack is BruteSlam slam)) return;
            Slams++;
            Vector3 o = slam.Origin;
            if (cracks != null)
            {
                var e = new ParticleSystem.EmitParams
                {
                    position = new Vector3(o.x, o.y + 0.04f, o.z),
                    velocity = Vector3.zero,
                    startColor = crackColor,
                    startSize = crackSize,
                    startLifetime = crackLife,
                    rotation = Random.Range(0f, 360f),
                };
                cracks.Emit(e, 1);
            }
            Vector3 fwd = slam.transform.forward;
            DustRing(o + new Vector3(fwd.x, 0f, fwd.z) * 0.9f, slamDustCount, 0.5f, new Vector2(1.2f, 3.2f), new Vector2(0.6f, 1.1f));
            Track(slam);
            Vector3 knight = KnightPosition(out bool has);
            if (has)
            {
                float d = Planar(knight, o);
                if (d <= shakeRange) Shake(slamShake * Mathf.Lerp(1f, 0.45f, d / Mathf.Max(0.01f, shakeRange)), slamShakeTime);
            }
        }

        private void LateUpdate() => FollowFronts(Time.deltaTime);

        /// <summary>Dust along each growing Brute ring, at its front. Public for tests (LateUpdate calls it).</summary>
        public void FollowFronts(float dt)
        {
            if (dt <= 0f) return;
            int budget = ringDustMaxPerFrame;
            for (int i = 0; i < MaxSlams; i++)
            {
                var slam = _slams[i];
                if (slam == null) continue;
                float front = slam.isActiveAndEnabled ? slam.Front : -1f;
                if (front < 0f)
                {
                    _slams[i] = null; // ring over (or the Brute died): stop following it
                    continue;
                }
                if (dust == null || front < 0.3f) continue;
                _ringDustCarry += 2f * Mathf.PI * front * ringDustDensity * dt;
                int n = Mathf.Min(budget, (int)_ringDustCarry);
                _ringDustCarry -= n;
                budget -= n;
                Vector3 o = slam.Origin;
                for (int k = 0; k < n; k++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    float cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                    var e = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(o.x + cx * front, o.y + 0.15f, o.z + sz * front),
                        velocity = new Vector3(cx * 1.4f, Random.Range(0.6f, 1.5f), sz * 1.4f),
                        startColor = Color.Lerp(snowDust, earthDust, Random.Range(0.2f, 0.6f)),
                        startSize = Random.Range(0.45f, 0.8f),
                        startLifetime = Random.Range(0.35f, 0.6f),
                    };
                    dust.Emit(e, 1);
                }
                RingDust += n;
            }
            if (budget == ringDustMaxPerFrame) _ringDustCarry = 0f;
        }

        private void Track(BruteSlam slam)
        {
            int free = -1;
            for (int i = 0; i < MaxSlams; i++)
            {
                if (_slams[i] == slam) return;
                if (free < 0 && _slams[i] == null) free = i;
            }
            if (free >= 0) _slams[free] = slam; // more than MaxSlams at once: the extra ring has no dust (rare)
        }

        private void DustRing(Vector3 at, int count, float radius, Vector2 speed, Vector2 size)
        {
            if (dust == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = (i + Random.value * 0.6f) / Mathf.Max(1, count) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), sz = Mathf.Sin(a), v = Random.Range(speed.x, speed.y);
                var e = new ParticleSystem.EmitParams
                {
                    position = new Vector3(at.x + cx * radius, at.y + 0.12f, at.z + sz * radius),
                    velocity = new Vector3(cx * v, Random.Range(0.5f, 1.6f), sz * v),
                    startColor = Color.Lerp(snowDust, earthDust, Random.value * 0.4f),
                    startSize = Random.Range(size.x, size.y),
                    startLifetime = Random.Range(0.45f, 0.75f),
                };
                dust.Emit(e, 1);
            }
        }

        private void Shake(float amplitude, float time)
        {
            Shakes++;
            LastShake = amplitude;
            if (followCamera != null) followCamera.Shake(amplitude, time);
        }

        private static Vector3 KnightPosition(out bool has)
        {
            var shots = EnemyProjectiles.Current;
            var knight = shots != null ? shots.Player : null;
            has = knight != null && !knight.IsDead;
            return has ? knight.transform.position : Vector3.zero;
        }

        private static float Planar(Vector3 a, Vector3 b) => SpecialAttack.Planar(a, b);
    }
}
