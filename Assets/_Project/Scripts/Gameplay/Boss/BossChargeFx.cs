using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Boss charge dash visuals (KAN-80, follow-up of KAN-61). Body language and dust for <see cref="BossFight.Move.Charge"/>:
    /// <list type="bullet">
    /// <item>Wind-up: the upper body leans in (spine bones, after the Animator) more and more with the lane fill, with a
    /// small shudder, and the back foot kicks up scrape dust.</item>
    /// <item>Dash: a full lean and a dust / snow trail behind the boss (one puff per <see cref="trailSpacing"/> m of travel,
    /// only while <see cref="BossFight.Attack.Dashing"/>), plus a skid cloud where it stops (bigger after a crash).</item>
    /// <item>Punish window (the recover after the last dash, <see cref="BossData.ChargeRecover"/> + stun): the boss slumps
    /// forward and sways, and small embers circle over its head, so the Knight sees it is open to attack.</item>
    /// </list>
    /// One shared particle system (one material, no emission module, <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>
    /// with one reused <see cref="ParticleSystem.EmitParams"/>): one draw call while puffs live, nothing otherwise. No lights,
    /// no allocation per frame. The lean is additive on top of the clip and is undone when the clip did not rewrite a bone
    /// (no drift on bones a clip leaves alone).
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class BossChargeFx : MonoBehaviour
    {
        /// <summary>What the boss shows now.</summary>
        public enum Phase
        {
            None = 0,
            WindUp = 1,
            Dash = 2,
            Punish = 3,
        }

        [SerializeField] private BossFight fight;

        [Header("Pose (degrees)")]
        [SerializeField, Range(0f, 40f)] private float windUpLean = 16f;
        [SerializeField, Range(0f, 40f)] private float dashLean = 22f;
        [SerializeField, Range(0f, 40f)] private float slumpLean = 14f;
        [SerializeField, Range(0f, 30f)] private float slumpSway = 7f;
        [SerializeField, Range(0f, 30f)] private float headDroop = 10f;
        [SerializeField, Range(0f, 10f)] private float shudder = 1.6f;
        [Tooltip("How fast the lean follows its target (1/s).")]
        [SerializeField, Min(0.1f)] private float poseRate = 12f;

        [Header("Dust")]
        [SerializeField] private Color dustColor = new Color(0.86f, 0.84f, 0.82f, 0.9f); // snow powder: reads on the dark arena stone
        [SerializeField, Min(0.05f)] private float trailSpacing = 0.3f;
        [SerializeField, Min(0.01f)] private float scrapeEvery = 0.09f;
        [SerializeField, Min(0f)] private float skidPuffs = 9f;

        [Header("Dizzy embers")]
        [SerializeField] private Color emberColor = new Color(1f, 0.78f, 0.3f, 1f);
        [SerializeField, Min(0.01f)] private float emberEvery = 0.035f;
        [Tooltip("Turns per second of the ember ring over the head.")]
        [SerializeField, Min(0f)] private float emberSpin = 1.1f;

        private static readonly string SpineName = "Spine";
        private static readonly string Spine1Name = "Spine01";
        private static readonly string Spine2Name = "Spine02";
        private static readonly string HeadName = "Head";
        private const int BoneCount = 4; // spine, spine 1, spine 2, head
        private static readonly float[] LeanShare = { 0.4f, 0.3f, 0.3f, 0f };

        private readonly Transform[] _bones = new Transform[BoneCount];
        private readonly Quaternion[] _written = new Quaternion[BoneCount];
        private readonly Quaternion[] _base = new Quaternion[BoneCount];
        private readonly bool[] _hasWritten = new bool[BoneCount];
        private ParticleSystem _ps;
        private ParticleSystem.EmitParams _params;
        private Enemy _body;
        private Vector3 _lastPos;
        private float _trailLeft, _scrapeLeft, _emberLeft, _clock, _punishTime;
        private float _lean, _sway, _droop;
        private Phase _phase;

        /// <summary>The scene's charge visuals (null when the scene has none).</summary>
        public static BossChargeFx Active { get; private set; }
        public Phase Current => _phase;
        /// <summary>Forward lean of the upper body now, in degrees (tests).</summary>
        public float Lean => _lean;
        /// <summary>Dust puffs of the dash trail emitted so far (tests).</summary>
        public int TrailEmitted { get; private set; }
        /// <summary>Scrape puffs of the wind-up emitted so far (tests).</summary>
        public int ScrapeEmitted { get; private set; }
        /// <summary>Skid cloud puffs at the dash ends so far (tests).</summary>
        public int SkidEmitted { get; private set; }
        /// <summary>Dizzy embers over the head so far (tests).</summary>
        public int EmbersEmitted { get; private set; }
        /// <summary>True when the boss body has the spine bones the lean drives.</summary>
        public bool HasBones => _bones[0] != null;
        public ParticleSystem System => _ps != null ? _ps : _ps = GetComponent<ParticleSystem>();

        /// <summary>Wires the fight (tests; the scene builder sets the serialized field).</summary>
        public void Setup(BossFight boss)
        {
            Unbind();
            fight = boss;
            if (isActiveAndEnabled) Bind();
        }

        private void OnEnable()
        {
            Active = this;
            Bind();
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            Unbind();
            ReleaseBones();
        }

        private void Bind()
        {
            if (fight != null) fight.Charged += OnCharged;
        }

        private void Unbind()
        {
            if (fight != null) fight.Charged -= OnCharged;
        }

        // After the Animator wrote this frame's pose (the lean is added on top of the clip).
        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>One step: phase from the fight, lean, dust and embers. Tests call it after <see cref="BossFight.Tick"/>.</summary>
        public void Tick(float dt)
        {
            var boss = fight != null ? fight.Boss : null;
            if (boss != _body) Attach(boss);
            if (boss == null || !boss.IsAlive || fight.Current != BossFight.Stage.Fighting)
            {
                _phase = Phase.None;
                ReleaseBones();
                _lean = _sway = _droop = 0f;
                if (boss != null) _lastPos = boss.transform.position;
                return;
            }
            Transform t = boss.transform;
            Vector3 pos = t.position;
            Vector3 fwd = t.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            if (dt <= 0f) // paused: hold the pose (the Animator may still rewrite the bones), emit nothing
            {
                if (_hasWritten[0] || _hasWritten[BoneCount - 1]) Pose(right, fwd);
                return;
            }
            _clock += dt;
            _phase = PhaseOf(fight);
            if (_phase != Phase.Punish) _punishTime = 0f;
            float r = Mathf.Max(0.4f, boss.HitRadius);

            float lean = 0f, sway = 0f, droop = 0f;
            switch (_phase)
            {
                case Phase.WindUp:
                    float fill = fight.WindUpProgress;
                    lean = windUpLean * (1f - (1f - fill) * (1f - fill)) + Mathf.Sin(_clock * 31f) * shudder * fill;
                    _scrapeLeft -= dt;
                    if (_scrapeLeft <= 0f)
                    {
                        _scrapeLeft = scrapeEvery;
                        Scrape(pos, fwd, right, r, fill);
                    }
                    break;
                case Phase.Dash:
                    lean = dashLean;
                    Trail(pos, fwd, right, r);
                    break;
                case Phase.Punish:
                    _punishTime += dt;
                    lean = slumpLean;
                    sway = Mathf.Sin(_punishTime * 4.6f) * slumpSway;
                    droop = headDroop;
                    _emberLeft -= dt;
                    while (_emberLeft <= 0f)
                    {
                        _emberLeft += emberEvery;
                        Ember(boss, pos, r);
                    }
                    break;
            }
            if (_phase != Phase.Dash) _trailLeft = 0f;
            _lastPos = pos;

            float k = 1f - Mathf.Exp(-poseRate * dt);
            _lean = Mathf.Lerp(_lean, lean, k);
            _sway = Mathf.Lerp(_sway, sway, k);
            _droop = Mathf.Lerp(_droop, droop, k);
            if (Mathf.Abs(_lean) < 0.01f && Mathf.Abs(_sway) < 0.01f && Mathf.Abs(_droop) < 0.01f) ReleaseBones();
            else Pose(right, fwd);
        }

        /// <summary>The charge visual the fight calls for now.</summary>
        public static Phase PhaseOf(BossFight f)
        {
            if (f == null || f.CurrentMove != BossFight.Move.Charge) return Phase.None;
            switch (f.CurrentAttack)
            {
                case BossFight.Attack.WindUp: return Phase.WindUp;
                case BossFight.Attack.Dashing: return Phase.Dash;
                case BossFight.Attack.Recover: return f.DashesLeft > 0 ? Phase.None : Phase.Punish; // a re-dash pause is no opening
                default: return Phase.None;
            }
        }

        private void Attach(Enemy boss)
        {
            ReleaseBones();
            _body = boss;
            for (int i = 0; i < BoneCount; i++)
            {
                _bones[i] = null;
                _hasWritten[i] = false;
            }
            _lean = _sway = _droop = 0f;
            if (boss == null) return;
            _lastPos = boss.transform.position;
            Transform root = boss.Animator != null ? boss.Animator.transform : boss.transform;
            _bones[0] = Find(root, SpineName);
            _bones[1] = Find(root, Spine1Name);
            _bones[2] = Find(root, Spine2Name);
            _bones[3] = Find(root, HeadName);
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = Find(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Lean on top of the clip. A bone the clip did not rewrite still holds our last value: undo it first.</summary>
        private void Pose(Vector3 right, Vector3 fwd)
        {
            for (int i = 0; i < BoneCount; i++)
            {
                var b = _bones[i];
                if (b == null) continue;
                Undo(i);
                float pitch = _lean * LeanShare[i] + (i == BoneCount - 1 ? _droop : 0f);
                float roll = _sway * (i == BoneCount - 1 ? 0.5f : LeanShare[i]);
                b.rotation = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(roll, fwd) * b.rotation;
                _written[i] = b.localRotation;
                _hasWritten[i] = true;
            }
        }

        private void Undo(int i)
        {
            var b = _bones[i];
            if (_hasWritten[i] && Quaternion.Dot(b.localRotation, _written[i]) > 0.999999f) b.localRotation = _base[i];
            _base[i] = b.localRotation;
        }

        private void ReleaseBones()
        {
            for (int i = 0; i < BoneCount; i++)
            {
                if (!_hasWritten[i]) continue;
                var b = _bones[i];
                if (b != null && Quaternion.Dot(b.localRotation, _written[i]) > 0.999999f) b.localRotation = _base[i];
                _hasWritten[i] = false;
            }
        }

        private void Scrape(Vector3 pos, Vector3 fwd, Vector3 right, float r, float fill)
        {
            Vector3 foot = pos - fwd * (r * 0.55f) + right * (r * 0.35f);
            Vector3 vel = -fwd * Random.Range(1f, 2.2f) + right * Random.Range(-0.4f, 0.4f) + Vector3.up * Random.Range(0.4f, 0.9f);
            Dust(foot, vel, r * Random.Range(0.35f, 0.55f) * (0.7f + 0.5f * fill), Random.Range(0.5f, 0.8f));
            ScrapeEmitted++;
        }

        private void Trail(Vector3 pos, Vector3 fwd, Vector3 right, float r)
        {
            Vector3 moved = pos - _lastPos;
            moved.y = 0f;
            _trailLeft += moved.magnitude;
            int guard = 0;
            while (_trailLeft >= trailSpacing && guard++ < 8)
            {
                _trailLeft -= trailSpacing;
                Vector3 at = pos - fwd * (r * 0.6f) + right * (r * Random.Range(-0.8f, 0.8f));
                Vector3 vel = -fwd * Random.Range(0.4f, 1.4f) + right * Random.Range(-0.6f, 0.6f) + Vector3.up * Random.Range(0.5f, 1.2f);
                Dust(at, vel, r * Random.Range(0.7f, 1.1f), Random.Range(0.8f, 1.2f));
                TrailEmitted++;
            }
        }

        private void OnCharged(Vector3 end, float dealt)
        {
            if (fight == null || fight.Boss == null) return;
            var t = fight.Boss.transform;
            Vector3 fwd = t.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            float r = Mathf.Max(0.4f, fight.Boss.HitRadius);
            float big = fight.DashBlocked ? 1.6f : 1f;
            int n = Mathf.RoundToInt(skidPuffs * big);
            for (int i = 0; i < n; i++)
            {
                float side = Random.Range(-1f, 1f);
                Vector3 at = t.position + fwd * (r * 0.4f) + right * (side * r);
                Vector3 vel = fwd * Random.Range(0.3f, 1.6f) * big + right * (side * Random.Range(0.8f, 2f)) + Vector3.up * Random.Range(0.5f, 1.4f);
                Dust(at, vel, r * Random.Range(0.6f, 1f) * big, Random.Range(0.8f, 1.3f));
                SkidEmitted++;
            }
        }

        private void Ember(Enemy boss, Vector3 pos, float r)
        {
            var head = _bones[BoneCount - 1];
            Vector3 top = head != null ? head.position + Vector3.up * (r * 0.45f) : pos + Vector3.up * (r * 2.6f);
            float ring = Mathf.Max(0.45f, r * 0.55f);
            float a = (_clock * emberSpin + (EmbersEmitted % 3) / 3f) * Mathf.PI * 2f;
            Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 tangent = new Vector3(-off.z, 0f, off.x);
            Puff(top + off * ring, tangent * (ring * 1.2f) + Vector3.up * 0.15f, Mathf.Max(0.25f, r * 0.22f) * Random.Range(0.8f, 1.2f),
                emberColor, 0.45f);
            EmbersEmitted++;
        }

        /// <summary>A dust puff sits on the ground: lifted by part of its size so the ground does not cut its lower half.</summary>
        private void Dust(Vector3 at, Vector3 velocity, float size, float life)
        {
            at.y += size * 0.4f;
            Puff(at, velocity, size, dustColor, life);
        }

        private void Puff(Vector3 at, Vector3 velocity, float size, Color color, float life)
        {
            var ps = System;
            if (ps == null) return;
            _params.position = at;
            _params.velocity = velocity;
            _params.startSize = size;
            _params.startColor = color;
            _params.startLifetime = life;
            _params.rotation = Random.Range(0f, 360f);
            ps.Emit(_params, 1);
        }
    }
}
