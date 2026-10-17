using System;
using UnityEngine;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>What a hero on the stage does now (KAN-244).</summary>
    public enum HeroStageMotion
    {
        /// <summary>The idle clip alone (breathing, weight shift).</summary>
        Idle,
        /// <summary>Idle variant: head and chest turn to look left, then right, then back.</summary>
        LookAround,
        /// <summary>Idle variant: the hero turns aside and works the weapon with its own clips (Knight: shoulder aim then
        /// Lower; Ranger: a full dry draw). Heroes without such clips never pick it.</summary>
        Drill,
        /// <summary>Pick taunt: weapon high overhead with two pumps, chest out, chin up.</summary>
        Taunt,
    }

    /// <summary>
    /// Makes a hero on the menu stage feel alive (KAN-244): on top of the idle clip it plays a random idle variant every
    /// <see cref="gapMin"/>..<see cref="gapMax"/> s (<see cref="HeroStageMotion.LookAround"/>, <see cref="HeroStageMotion.Drill"/>)
    /// and the pick <see cref="Taunt"/>. Look-around and taunt are procedural bone offsets written in LateUpdate after the
    /// Animator, like the KAN-215 crossbow aim (no Meshy library clips on this machine: the raw rigs live on the Mac; real
    /// clips can replace them later). The drill drives the hero's own controller: the Knight's UpperBody layer + Aim bool
    /// (AimIdle, Lower) or the Ranger's Shooting + ShootTime (Shoot clip). Every motion eases in and out, so it never pops.
    /// Bones are found by name (Mixamo "mixamorig:" prefix or the Meshy names), the weapon arm is the hand that holds the
    /// muzzle socket. No allocation per frame.
    /// </summary>
    public sealed class HeroStageLife : MonoBehaviour
    {
        [Tooltip("Seconds between idle variants (random in this range, after the last motion ended).")]
        [Min(0.5f)] public float gapMin = 5f;
        [Min(0.5f)] public float gapMax = 9f;
        [Tooltip("Seconds of the look-around.")]
        [Min(1f)] public float lookTime = 3.8f;
        [Tooltip("Seconds of the pick taunt.")]
        [Min(0.5f)] public float tauntTime = 1.8f;
        [Tooltip("Seconds to fade a motion out when another one cuts in.")]
        [Min(0.05f)] public float cutFade = 0.25f;
        [Tooltip("Body turn of the drill (deg, + = the hero's left): he works the weapon aside, not at the viewer.")]
        public float drillTurn = 38f;

        private static readonly int AimId = Animator.StringToHash("Aim");
        private static readonly int ShootingId = Animator.StringToHash("Shooting");
        private static readonly int ShootTimeId = Animator.StringToHash("ShootTime");
        private const string UpperBodyLayer = "UpperBody";

        private enum DrillKind { None, ShoulderAim, DryDraw }

        // LookAround curves (deg): yaw + = the hero's left, pitch + = chin down.
        private static readonly AnimationCurve LookYaw = Curve((0f, 0f), (0.9f, 40f), (1.7f, 38f), (2.6f, -30f), (3.15f, -28f), (3.8f, 0f));
        private static readonly AnimationCurve LookPitch = Curve((0f, 0f), (1.2f, -3f), (2.6f, 6f), (3.8f, 0f));

        private Animator _animator;
        private Transform _root, _chest, _neck, _head, _arm, _foreArm, _hand;
        private float _armSide = 1f;
        private readonly Transform[] _bones = new Transform[5];
        private readonly Quaternion[] _before = new Quaternion[5];
        private readonly Quaternion[] _after = new Quaternion[5];
        private DrillKind _drill;
        private bool _animatorRead;
        private int _upperLayer = -1;
        private float _shootLength = 1f, _layerWeight;
        private System.Random _random;

        private HeroStageMotion _motion = HeroStageMotion.Idle;
        private float _t, _wait, _cut = 1f; // _cut < 1: the motion fades out over cutFade, then goes Idle
        private HeroStageMotion _queued = HeroStageMotion.Idle;

        /// <summary>Motion playing now.</summary>
        public HeroStageMotion Motion => _motion;
        /// <summary>Seconds into the motion now.</summary>
        public float MotionTime => _t;
        /// <summary>Seconds until the next idle variant (counts only while idle).</summary>
        public float NextVariantIn => _wait;
        /// <summary>Extra body turn (deg) the stage adds to the hero yaw (the drill turns aside).</summary>
        public float BodyYaw { get; private set; }
        /// <summary>Taunt glow 0..1 (peaks when the weapon is up): the stage flare light follows it.</summary>
        public float Flare { get; private set; }
        /// <summary>True when the hero has its own weapon clips for <see cref="HeroStageMotion.Drill"/>.</summary>
        public bool CanDrill
        {
            get
            {
                ReadAnimator();
                return _drill != DrillKind.None;
            }
        }
        /// <summary>Total seconds of <paramref name="motion"/>.</summary>
        public float LengthOf(HeroStageMotion motion) => motion switch
        {
            HeroStageMotion.LookAround => lookTime,
            HeroStageMotion.Taunt => tauntTime,
            HeroStageMotion.Drill => _drill == DrillKind.ShoulderAim ? 3.1f : 0.5f + _shootLength + 0.25f + 0.55f,
            _ => 0f,
        };
        /// <summary>Raised when a taunt starts (the stage plays the hero's sound).</summary>
        public event Action TauntStarted;

        /// <summary>Wiring (the stage calls it right after spawning the model). <paramref name="seed"/> fixes the variant order.</summary>
        public void Setup(Animator animator, int seed)
        {
            _animator = animator;
            _root = transform;
            _random = new System.Random(seed);
            _chest = Find("Spine2", "Spine02", "Spine1", "Spine01", "Chest", "Spine");
            _neck = Find("Neck");
            _head = Find("Head");
            var weaponHand = WeaponHand();
            string side = weaponHand != null && weaponHand.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0 ? "Left" : "Right";
            _hand = weaponHand != null ? weaponHand : Find(side + "Hand");
            _arm = Find(side + "Arm");
            _foreArm = Find(side + "ForeArm");
            _bones[0] = _chest;
            _bones[1] = _neck;
            _bones[2] = _head;
            _bones[3] = _arm;
            _bones[4] = _foreArm;

            _drill = DrillKind.None;
            _animatorRead = false;
            ReadAnimator();
            _motion = HeroStageMotion.Idle;
            _t = 0f;
            _cut = 1f;
            _wait = NextGap();
        }

        /// <summary>
        /// Which drill the controller allows. An Animator under an inactive parent has no layers or parameters yet (the
        /// stage spawns heroes hidden), so this runs again until the model is active.
        /// </summary>
        private void ReadAnimator()
        {
            if (_animatorRead || _animator == null || _animator.runtimeAnimatorController == null || !_animator.isActiveAndEnabled) return;
            _animatorRead = true;
            _upperLayer = _animator.GetLayerIndex(UpperBodyLayer);
            if (_upperLayer >= 0 && HasParam(AimId)) _drill = DrillKind.ShoulderAim;
            else if (HasParam(ShootingId) && HasParam(ShootTimeId))
            {
                _drill = DrillKind.DryDraw;
                foreach (var clip in _animator.runtimeAnimatorController.animationClips)
                    if (clip != null && clip.name.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) >= 0) _shootLength = Mathf.Max(0.3f, clip.length);
            }
        }

        /// <summary>Starts the pick taunt now (cuts any variant with a short fade).</summary>
        public void Taunt() => Play(HeroStageMotion.Taunt);

        /// <summary>Starts <paramref name="motion"/> (dev screenshots, tests). A running motion fades out first.</summary>
        public void Play(HeroStageMotion motion)
        {
            if (motion == HeroStageMotion.Drill && !CanDrill) motion = HeroStageMotion.LookAround;
            if (_motion == HeroStageMotion.Idle || motion == HeroStageMotion.Idle)
            {
                Begin(motion);
                return;
            }
            _queued = motion;
            if (_cut >= 1f) _cut = 0.999f;
        }

        /// <summary>Back to the plain idle at once (the stage swaps heroes).</summary>
        public void ResetToIdle()
        {
            Begin(HeroStageMotion.Idle);
            ApplyAnimator(0f, false, 0f, false);
            BodyYaw = 0f;
            Flare = 0f;
        }

        private void Begin(HeroStageMotion motion)
        {
            if (_motion == HeroStageMotion.Drill && motion != HeroStageMotion.Drill) ApplyAnimator(_layerWeight, false, 0f, false);
            _motion = motion;
            _queued = HeroStageMotion.Idle;
            _t = 0f;
            _cut = 1f;
            if (motion == HeroStageMotion.Idle) _wait = NextGap();
            if (motion == HeroStageMotion.Taunt) TauntStarted?.Invoke();
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Advances the motion and writes the bone offsets (LateUpdate; tests call it with their own step).</summary>
        public void Tick(float dt)
        {
            ReadAnimator();
            RestoreUntouchedBones();
            if (dt < 0f) dt = 0f;
            float cutWeight = 1f;
            if (_motion == HeroStageMotion.Idle)
            {
                _wait -= dt;
                if (_wait <= 0f) Begin(PickVariant());
            }
            else
            {
                _t += dt;
                if (_cut < 1f)
                {
                    _cut += dt / cutFade;
                    cutWeight = Mathf.Clamp01(1f - _cut);
                    if (_cut >= 1f)
                    {
                        var next = _queued;
                        Begin(HeroStageMotion.Idle);
                        if (next != HeroStageMotion.Idle) Begin(next);
                    }
                }
                else if (_t >= LengthOf(_motion)) Begin(HeroStageMotion.Idle);
            }

            BodyYaw = 0f;
            Flare = 0f;
            switch (_motion)
            {
                case HeroStageMotion.LookAround:
                    ApplyLook(_t, cutWeight);
                    break;
                case HeroStageMotion.Drill:
                    ApplyDrill(_t, cutWeight, dt);
                    break;
                case HeroStageMotion.Taunt:
                    ApplyTaunt(_t, cutWeight);
                    break;
                default:
                    FadeLayer(dt);
                    break;
            }
            StoreWrittenBones();
        }

        private HeroStageMotion PickVariant()
        {
            if (!CanDrill) return HeroStageMotion.LookAround;
            return _random.NextDouble() < 0.5 ? HeroStageMotion.LookAround : HeroStageMotion.Drill;
        }

        private float NextGap()
        {
            float lo = Mathf.Min(gapMin, gapMax), hi = Mathf.Max(gapMin, gapMax);
            return lo + (float)(_random?.NextDouble() ?? 0.5) * (hi - lo);
        }

        // ------------------------------------------------------------ Motions

        private void ApplyLook(float t, float w)
        {
            float edge = Mathf.Clamp01(Mathf.Min(t / 0.35f, (lookTime - t) / 0.35f));
            w *= edge * edge * (3f - 2f * edge);
            float yaw = LookYaw.Evaluate(t) * w, pitch = LookPitch.Evaluate(t) * w;
            Turn(_chest, yaw * 0.25f, pitch * 0.2f);
            Turn(_neck, yaw * 0.3f, pitch * 0.3f);
            Turn(_head, yaw * 0.45f, pitch * 0.5f);
        }

        private void ApplyDrill(float t, float w, float dt)
        {
            float length = LengthOf(HeroStageMotion.Drill);
            float turnIn = Ease(t / 0.5f), turnOut = Ease((length - t) / 0.55f);
            BodyYaw = drillTurn * Mathf.Min(turnIn, turnOut) * w;
            if (_drill == DrillKind.ShoulderAim)
            {
                bool aim = t >= 0.25f && t < 2.0f && w >= 1f;
                float goal = t < 2.35f && w >= 1f ? 1f : 0f; // hold the layer until the Lower clip is done (~0.3 s)
                _layerWeight = Mathf.MoveTowards(_layerWeight, goal, dt / (goal > _layerWeight ? 0.15f : 0.22f));
                ApplyAnimator(_layerWeight, aim, 0f, false);
            }
            else
            {
                float draw = (t - 0.5f) / _shootLength;
                bool shooting = draw >= 0f && draw <= 1f && w >= 1f;
                ApplyAnimator(0f, false, Mathf.Clamp01(draw), shooting);
            }
        }

        private void ApplyTaunt(float t, float w)
        {
            float up = Ease(t / 0.35f), down = Ease((tauntTime - t) / 0.45f);
            float env = Mathf.Min(up, down) * w;
            float pump = Bump(t, 0.75f, 0.12f) + Bump(t, 1.12f, 0.12f);
            Flare = Mathf.Clamp01(env * (0.55f + 0.45f * Bump(t, 0.4f, 0.25f)));
            if (env <= 0f) return;
            // Chest out, chin up, then the weapon arm high over the head (upper arm up and out, forearm up, pumps bend it).
            Turn(_chest, 0f, -8f * env);
            Turn(_head, 0f, -10f * env);
            if (_arm == null || _foreArm == null || _hand == null) return;
            Vector3 up3 = Vector3.up, fwd = _root.forward, side = _root.right * _armSide;
            Aim(_arm, _foreArm.position, (up3 * 0.92f + side * 0.34f + fwd * 0.12f).normalized, env);
            Aim(_foreArm, _hand.position, (up3 + fwd * (0.06f + 0.6f * pump) + side * 0.02f).normalized, env);
        }

        private void FadeLayer(float dt)
        {
            if (_layerWeight <= 0f) return;
            _layerWeight = Mathf.MoveTowards(_layerWeight, 0f, dt / 0.2f);
            ApplyAnimator(_layerWeight, false, 0f, false);
        }

        private void ApplyAnimator(float layerWeight, bool aim, float shootTime, bool shooting)
        {
            if (_animator == null) return;
            if (_drill == DrillKind.ShoulderAim)
            {
                _animator.SetBool(AimId, aim);
                if (_upperLayer >= 0) _animator.SetLayerWeight(_upperLayer, layerWeight);
            }
            else if (_drill == DrillKind.DryDraw)
            {
                _animator.SetBool(ShootingId, shooting);
                _animator.SetFloat(ShootTimeId, shootTime);
            }
        }

        // ------------------------------------------------------------ Bone helpers

        /// <summary>Yaw about world up and pitch about the hero's right axis, on top of the animated pose.</summary>
        private void Turn(Transform bone, float yaw, float pitch)
        {
            if (bone == null || (yaw == 0f && pitch == 0f)) return;
            bone.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, _root.right) * bone.rotation;
        }

        /// <summary>Turns <paramref name="bone"/> so the line to <paramref name="childPos"/> points along <paramref name="dir"/>, by <paramref name="weight"/>.</summary>
        private static void Aim(Transform bone, Vector3 childPos, Vector3 dir, float weight)
        {
            Vector3 now = childPos - bone.position;
            if (now.sqrMagnitude < 1e-8f) return;
            var full = Quaternion.FromToRotation(now.normalized, dir);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, full, weight) * bone.rotation;
        }

        // A bone no clip animates keeps last frame's offset: put it back before writing a new one (no drift).
        private void RestoreUntouchedBones()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                var b = _bones[i];
                if (b == null) continue;
                if (b.localRotation == _after[i] && _before[i] != _after[i]) b.localRotation = _before[i];
                _before[i] = b.localRotation;
            }
        }

        private void StoreWrittenBones()
        {
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _after[i] = _bones[i].localRotation;
        }

        private Transform Find(params string[] names)
        {
            foreach (var n in names)
            {
                var hit = FindDeep(transform, n);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Exact bone name match without the "mixamorig:" prefix, case-insensitive.</summary>
        private static Transform FindDeep(Transform t, string name)
        {
            string n = t.name;
            int colon = n.IndexOf(':');
            if (string.Equals(colon >= 0 ? n.Substring(colon + 1) : n, name, StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindDeep(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>The hand bone above a "*Muzzle" socket (Knight: crossbow in the right hand, Ranger: bow hand), or null.</summary>
        private Transform WeaponHand()
        {
            var muzzle = FindMuzzle(transform);
            for (var t = muzzle; t != null && t != transform; t = t.parent)
            {
                string n = t.name;
                int colon = n.IndexOf(':');
                string bare = colon >= 0 ? n.Substring(colon + 1) : n;
                if (bare.Equals("LeftHand", StringComparison.OrdinalIgnoreCase) || bare.Equals("RightHand", StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        private static Transform FindMuzzle(Transform t)
        {
            if (t.name.EndsWith("Muzzle", StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindMuzzle(t.GetChild(i));
                if (hit != null) return hit;
            }
            return null;
        }

        private void Start() => UpdateArmSide();

        /// <summary>Which side of the body the weapon arm is on (+1 = the hero's right).</summary>
        public void UpdateArmSide()
        {
            if (_arm == null || _chest == null) return;
            _armSide = Vector3.Dot(_arm.position - _chest.position, _root.right) >= 0f ? 1f : -1f;
        }

        /// <summary>Weapon arm bones found (tests): upper arm, forearm, hand.</summary>
        public bool HasWeaponArm => _arm != null && _foreArm != null && _hand != null;
        /// <summary>Head bone found (tests).</summary>
        public Transform Head => _head;
        /// <summary>Weapon hand (tests).</summary>
        public Transform WeaponHandBone => _hand;

        private bool HasParam(int id)
        {
            foreach (var p in _animator.parameters)
                if (p.nameHash == id) return true;
            return false;
        }

        private static float Ease(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float Bump(float t, float at, float width)
        {
            float k = (t - at) / width;
            return Mathf.Exp(-k * k);
        }

        private static AnimationCurve Curve(params (float t, float v)[] keys)
        {
            var c = new AnimationCurve();
            foreach (var (t, v) in keys) c.AddKey(new Keyframe(t, v));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return c;
        }
    }
}
