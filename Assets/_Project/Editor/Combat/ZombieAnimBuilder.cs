using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Gameplay.Characters;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Combat
{
    /// <summary>
    /// Bakes the zombie's procedural clips on the Meshy rig (KAN-30). The Meshy export only has a sprint; a horde needs
    /// a shamble. Each frame poses a copy of the model from its rest pose with character-space offsets, solves the legs
    /// with two-bone IK onto planned foot paths, and keys every bone (local rotation, plus the hips position).
    /// <list type="bullet">
    /// <item><b>Shamble</b>: hunched, lurching walk. Stiff arms reach forward (one higher), head lolls, the left leg drags
    /// (low lift, toe scraping) and the body dips on it (limp). Stance feet are planted exactly, so the clip's ground
    /// speed is stride / cycle (<see cref="ShambleGroundSpeed"/>) and Enemy can match playback to real speed.</item>
    /// <item><b>Attack</b>: feet planted, alternating overhead claw swipes with a torso lunge (in contact range).</item>
    /// </list>
    /// No external clip, no licence question. Re-running rebuilds the clips in place (same asset GUIDs).
    /// </summary>
    public static class ZombieAnimBuilder
    {
        private const int FrameRate = 30;

        // ---- Shamble gait (metres, degrees, seconds). Tuned on the 1.95 m Meshy zombie (leg 0.77 m).
        private const float ShambleCycle = 0.95f;   // one full cycle = two steps
        private const float Duty = 0.58f;           // share of the cycle a foot is planted
        private const float Sweep = 0.84f;          // distance a planted foot travels under the body per stance
        private const float Crouch = 0.16f;         // hips lower than rest: bent knees, longer reach
        private const float Bob = 0.025f;           // hips dip at each footfall
        private const float LimpDip = 0.035f;       // extra dip while standing on the dragging leg
        private const float Sway = 0.045f;          // hips shift over the stance foot
        private const float HipYaw = 7f, HipRoll = 5f, HipTilt = 10f, Crook = 6f;
        private const float Hunch = 34f;            // forward bend spread over the spine
        private const float HeadLoll = 16f;
        private const float GoodLift = 0.09f, DragLift = 0.022f, DragToe = 22f, StepToe = 16f;
        private const float StanceWidth = 1.12f;    // x rest hip width: a wide, unsteady base
        private const float FootCentre = 0.06f;     // stance centre ahead of the hips (the hunched torso leans forward)
        private const float KneeOut = 0.3f;         // bow-legged knees
        private static readonly float[] ArmElevation = { 12f, 34f }; // left reaches higher, right hangs lower (below horizontal)
        private const float ArmSwing = 7f, ArmOut = 0.28f, ElbowBend = 14f, WristDroop = 38f;

        // ---- Attack
        private const float AttackCycle = 1.2f;     // two swipes
        private const float AttackStagger = 0.13f;  // right foot forward, left back
        private const float AttackCrouch = 0.13f;

        /// <summary>Ground speed of the shamble clip at rate 1 and scale 1 (m/s): planted feet move back at exactly this.</summary>
        public const float ShambleGroundSpeed = Sweep / Duty / ShambleCycle;

        /// <summary>Builds (or rebuilds in place) both clips from <paramref name="modelPrefab"/>. Logs the IK fit.</summary>
        public static (AnimationClip shamble, AnimationClip attack) Build(GameObject modelPrefab, string shamblePath, string attackPath)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                var rig = new Rig(instance.transform);
                var shamble = Bake(rig, ShambleCycle, rig.PoseShamble, "Zombie_Shamble");
                Debug.Log($"[ZombieAnimBuilder] Shamble: cycle {ShambleCycle}s, stride {Sweep / Duty:F2} m, ground speed {ShambleGroundSpeed:F3} m/s, " +
                          $"max IK miss {rig.MaxMiss * 100f:F1} cm");
                rig.MaxMiss = 0f;
                var attack = Bake(rig, AttackCycle, rig.PoseAttack, "Zombie_Attack");
                Debug.Log($"[ZombieAnimBuilder] Attack: cycle {AttackCycle}s, max IK miss {rig.MaxMiss * 100f:F1} cm");
                return (Save(shamble, shamblePath), Save(attack, attackPath));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Measured ground speed of any in-place clip on the zombie model (used for the run clip).</summary>
        public static GaitMeasure.Result Measure(GameObject modelPrefab, AnimationClip clip)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var rig = new Rig(instance.transform);
                return GaitMeasure.GroundSpeed(instance, clip, rig.Frame, new[] { rig.Foot[0], rig.Foot[1], rig.Toe[0], rig.Toe[1] });
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static AnimationClip Save(AnimationClip clip, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, path);
                return clip;
            }
            EditorUtility.CopySerialized(clip, existing);
            Object.DestroyImmediate(clip);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ------------------------------------------------------------ Baking

        private static AnimationClip Bake(Rig rig, float cycle, System.Action<float> pose, string name)
        {
            int frames = Mathf.RoundToInt(cycle * FrameRate);
            var bones = rig.All;
            var rot = new float[bones.Length, 4, frames + 1];
            var hips = new float[3, frames + 1];
            var prev = new Quaternion[bones.Length];
            for (int f = 0; f <= frames; f++)
            {
                rig.Reset();
                pose(f == frames ? 0f : (float)f / frames); // last key = first pose: seamless loop
                for (int b = 0; b < bones.Length; b++)
                {
                    var q = bones[b].localRotation;
                    if (f > 0 && Quaternion.Dot(q, prev[b]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // no flip between keys
                    prev[b] = q;
                    rot[b, 0, f] = q.x; rot[b, 1, f] = q.y; rot[b, 2, f] = q.z; rot[b, 3, f] = q.w;
                }
                var hp = rig.Hips.localPosition;
                hips[0, f] = hp.x; hips[1, f] = hp.y; hips[2, f] = hp.z;
            }

            var clip = new AnimationClip { name = name, frameRate = FrameRate };
            string[] comps = { "x", "y", "z", "w" };
            var v = new float[frames + 1];
            for (int b = 0; b < bones.Length; b++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[b], rig.Root);
                // The last key is the first pose, possibly with the opposite quaternion sign (same rotation).
                float dot = 0f;
                for (int c = 0; c < 4; c++) dot += rot[b, c, 0] * rot[b, c, frames];
                float sign = dot < 0f ? -1f : 1f;
                for (int c = 0; c < 4; c++)
                {
                    for (int f = 0; f <= frames; f++) v[f] = rot[b, c, f];
                    clip.SetCurve(path, typeof(Transform), "localRotation." + comps[c], Looped(v, frames, cycle, sign));
                }
            }
            string hipsPath = AnimationUtility.CalculateTransformPath(rig.Hips, rig.Root);
            for (int c = 0; c < 3; c++)
            {
                for (int f = 0; f <= frames; f++) v[f] = hips[c, f];
                clip.SetCurve(hipsPath, typeof(Transform), "localPosition." + comps[c], Looped(v, frames, cycle, 1f));
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        /// <summary>
        /// One key per frame; tangents by central difference, across the loop seam too (no hitch at the wrap).
        /// <paramref name="sign"/>: v[frames] == sign * v[0] (quaternion components may close the loop flipped).
        /// </summary>
        private static AnimationCurve Looped(float[] v, int frames, float cycle, float sign)
        {
            float step = cycle / frames;
            var keys = new Keyframe[frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                float before = f == 0 ? sign * v[frames - 1] : v[f - 1];
                float after = f == frames ? sign * v[1] : v[f + 1];
                float slope = (after - before) / (2f * step);
                keys[f] = new Keyframe(f * step, v[f], slope, slope);
            }
            return new AnimationCurve(keys);
        }

        // ------------------------------------------------------------ Rig + poses

        private sealed class Rig
        {
            public readonly Transform Root, Frame, Hips, Spine02, Spine01, Spine, Neck, Head;
            public readonly Transform[] Shoulder = new Transform[2], Arm = new Transform[2], ForeArm = new Transform[2], Hand = new Transform[2];
            public readonly Transform[] UpLeg = new Transform[2], Leg = new Transform[2], Foot = new Transform[2], Toe = new Transform[2];
            public readonly Transform[] All;
            public float MaxMiss;

            private readonly Quaternion[] _restLocalRot;
            private readonly Vector3[] _restLocalPos;
            private readonly Quaternion[] _restFootWorld = new Quaternion[2];
            private readonly Vector3 _hipsRest;
            private readonly float[] _side = new float[2];      // signed lateral offset of each leg (m, along Right)
            private readonly float[] _ankleHeight = new float[2];
            private readonly float[] _armSide = new float[2];   // sign of each arm along Right
            private readonly Vector3 _fwd, _right, _up = Vector3.up;

            public Rig(Transform root)
            {
                Root = root;
                Frame = root;
                Transform Find(string n) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n)
                                            ?? throw new System.InvalidOperationException($"Zombie rig: bone '{n}' missing");
                Hips = Find("Hips"); Spine02 = Find("Spine02"); Spine01 = Find("Spine01"); Spine = Find("Spine");
                Neck = Find("neck"); Head = Find("Head");
                string[] sides = { "Left", "Right" };
                for (int i = 0; i < 2; i++)
                {
                    Shoulder[i] = Find(sides[i] + "Shoulder"); Arm[i] = Find(sides[i] + "Arm");
                    ForeArm[i] = Find(sides[i] + "ForeArm"); Hand[i] = Find(sides[i] + "Hand");
                    UpLeg[i] = Find(sides[i] + "UpLeg"); Leg[i] = Find(sides[i] + "Leg");
                    Foot[i] = Find(sides[i] + "Foot"); Toe[i] = Find(sides[i] + "ToeBase");
                }
                All = Hips.GetComponentsInChildren<Transform>(true);
                _restLocalRot = All.Select(t => t.localRotation).ToArray();
                _restLocalPos = All.Select(t => t.localPosition).ToArray();

                // Character frame from the rig itself: forward = where the face points.
                Vector3 face = Find("headfront").position - Head.position;
                face.y = 0f;
                _fwd = face.sqrMagnitude > 1e-6f ? face.normalized : Vector3.forward;
                _right = Vector3.Cross(_up, _fwd);
                _hipsRest = Hips.position;
                for (int i = 0; i < 2; i++)
                {
                    _side[i] = Vector3.Dot(UpLeg[i].position - _hipsRest, _right);
                    _ankleHeight[i] = Foot[i].position.y;
                    _restFootWorld[i] = Foot[i].rotation;
                    _armSide[i] = Mathf.Sign(Vector3.Dot(Arm[i].position - _hipsRest, _right));
                }
            }

            public void Reset()
            {
                for (int i = 0; i < All.Length; i++)
                {
                    All[i].localRotation = _restLocalRot[i];
                    All[i].localPosition = _restLocalPos[i];
                }
            }

            // ---------------- Shamble

            /// <summary>Right leg (1) steps at phase 0, the dragging left leg (0) half a cycle later.</summary>
            public void PoseShamble(float p)
            {
                float w = 2f * Mathf.PI * p;
                float rightSide = Mathf.Sign(_side[1]);
                float bob = -Bob * Mathf.Cos(2f * w);                    // low at footfalls, high mid-stance
                float limp = -LimpDip * Mathf.Max(0f, Mathf.Sin(w - Mathf.PI)); // sags on the bad leg
                float sway = Sway * Mathf.Sin(w) * rightSide;             // over the stance foot
                float yaw = HipYaw * Mathf.Sin(w);
                float roll = HipRoll * Mathf.Sin(w) + Crook * 0.5f;

                Hips.position = _hipsRest + _up * (bob + limp - Crouch) + _right * sway + _fwd * (0.015f * Mathf.Sin(2f * w));
                Turn(Hips, Q(yaw, _up) * Q(roll, _fwd) * Q(HipTilt, _right));

                Turn(Spine02, Q(Hunch * 0.35f, _right) * Q(-yaw * 0.6f, _up) * Q(-roll * 0.7f + Crook, _fwd));
                Turn(Spine01, Q(Hunch * 0.35f + 2f * Mathf.Cos(2f * w), _right) * Q(-yaw * 0.4f, _up));
                Turn(Spine, Q(Hunch * 0.3f, _right) * Q(3f * Mathf.Sin(w + 0.4f), _fwd));
                Turn(Neck, Q(-Hunch * 0.45f, _right));
                Turn(Head, Q(HeadLoll + 5f * Mathf.Sin(w + 0.8f), _fwd) * Q(-Hunch * 0.25f + 6f * Mathf.Cos(2f * w + 0.7f), _right)
                           * Q(7f * Mathf.Sin(w + 1.3f), _up));

                for (int i = 0; i < 2; i++)
                {
                    float phase = i == 1 ? 0f : Mathf.PI;
                    float elev = ArmElevation[i] + ArmSwing * Mathf.Sin(w + phase + 0.6f);
                    ReachArm(i, elev, ArmOut, ElbowBend, WristDroop + 6f * Mathf.Sin(2f * w + i));
                }

                for (int i = 0; i < 2; i++)
                {
                    bool drag = i == 0;
                    float u = Frac(p - (drag ? 0.5f : 0f));
                    float z, h, toe;
                    if (u < Duty)
                    {
                        z = Sweep * (0.5f - u / Duty); // planted: moves back at exactly the ground speed
                        h = 0f;
                        toe = 0f;
                    }
                    else
                    {
                        float s = (u - Duty) / (1f - Duty);
                        float e = Smooth(s);
                        if (drag) e = Mathf.Pow(e, 1.35f); // hangs back, then hauled through
                        z = Sweep * (-0.5f + e);
                        float arc = Mathf.Sin(Mathf.PI * s);
                        h = (drag ? DragLift : GoodLift) * arc;
                        toe = drag ? DragToe * arc : StepToe * Mathf.Sin(2f * Mathf.PI * s); // scrape / toe-off then heel-first
                    }
                    PlaceFoot(i, _side[i] * StanceWidth, Vector3.Dot(_hipsRest, _fwd) + FootCentre + z, h, toe);
                }
            }

            // ---------------- Attack

            /// <summary>Right arm swipes in the first half, left in the second; feet stay planted.</summary>
            public void PoseAttack(float p)
            {
                float w = 2f * Mathf.PI * p;
                float strikeR = Strike(Frac(p) * 2f);           // 0..1 bell during the right swipe
                float strikeL = Strike(Frac(p - 0.5f) * 2f);
                float lunge = Mathf.Max(strikeR, strikeL);
                float twist = 16f * (strikeR - strikeL) * Mathf.Sign(_side[1]);

                Hips.position = _hipsRest + _up * (-AttackCrouch - 0.03f * lunge + 0.01f * Mathf.Cos(2f * w)) + _fwd * (0.07f * lunge);
                Turn(Hips, Q(twist * 0.4f, _up) * Q(HipTilt + 6f * lunge, _right));
                Turn(Spine02, Q(Hunch * 0.3f + 6f * lunge, _right) * Q(twist * 0.3f, _up));
                Turn(Spine01, Q(Hunch * 0.3f + 6f * lunge, _right) * Q(twist * 0.3f, _up));
                Turn(Spine, Q(Hunch * 0.25f, _right));
                Turn(Neck, Q(-Hunch * 0.4f, _right));
                Turn(Head, Q(HeadLoll * 0.6f, _fwd) * Q(-Hunch * 0.2f + 18f * lunge, _right)); // snaps forward: a bite

                for (int i = 0; i < 2; i++)
                {
                    float u = Frac(p - (i == 1 ? 0f : 0.5f)) * 2f; // this arm swipes while u < 1
                    float elev, outward;
                    if (u < 1f) Swipe(u, out elev, out outward);
                    else
                    {
                        elev = 18f + 5f * Mathf.Sin(w * 2f + i);
                        outward = 0.2f;
                    }
                    ReachArm(i, elev, outward, ElbowBend + (u < 1f ? 10f : 0f), WristDroop * 0.6f);
                }

                for (int i = 0; i < 2; i++)
                {
                    float z = (i == 1 ? AttackStagger : -AttackStagger);
                    PlaceFoot(i, _side[i] * StanceWidth, Vector3.Dot(_hipsRest, _fwd) + FootCentre + z, 0f, 0f);
                }
            }

            /// <summary>Wind-up high, fast diagonal rake down and across the body, slow recovery.</summary>
            private static void Swipe(float u, out float elev, out float outward)
            {
                if (u < 0.38f)
                {
                    float t = Smooth(u / 0.38f);
                    elev = Mathf.Lerp(18f, -55f, t);
                    outward = Mathf.Lerp(0.2f, 0.45f, t);
                }
                else if (u < 0.56f)
                {
                    float t = Smooth((u - 0.38f) / 0.18f);
                    elev = Mathf.Lerp(-55f, 62f, t);
                    outward = Mathf.Lerp(0.45f, -0.35f, t);
                }
                else
                {
                    float t = Smooth((u - 0.56f) / 0.44f);
                    elev = Mathf.Lerp(62f, 18f, t);
                    outward = Mathf.Lerp(-0.35f, 0.2f, t);
                }
            }

            private static float Strike(float u) => u >= 1f ? 0f : Mathf.Exp(-Mathf.Pow((u - 0.47f) / 0.12f, 2f));

            // ---------------- Limbs

            /// <summary>
            /// Points the upper arm along the forward axis tipped down by <paramref name="elev"/> degrees (negative = up),
            /// spread sideways by <paramref name="outward"/>; the forearm a little straighter; the hand hangs limp.
            /// </summary>
            private void ReachArm(int i, float elev, float outward, float elbow, float droop)
            {
                float side = _armSide[i];
                Vector3 upper = (Q(elev, _right) * _fwd + _right * (side * outward)).normalized;
                Aim(Arm[i], ForeArm[i], upper);
                Vector3 lower = (Q(elev - elbow, _right) * _fwd + _right * (side * outward * 0.25f)).normalized;
                Aim(ForeArm[i], Hand[i], lower);
                Turn(Hand[i], Q(droop, _right));
            }

            /// <summary>Two-bone IK: ankle to the target (character space), knee towards forward + out, foot pitched by <paramref name="toe"/>.</summary>
            private void PlaceFoot(int i, float lateral, float forward, float lift, float toe)
            {
                Vector3 target = _right * lateral + _fwd * forward + _up * (_ankleHeight[i] + lift);
                Vector3 pole = _fwd + _right * (Mathf.Sign(_side[i]) * KneeOut);
                SolveLeg(UpLeg[i], Leg[i], Foot[i], target, pole);
                MaxMiss = Mathf.Max(MaxMiss, Vector3.Distance(Foot[i].position, target));
                Foot[i].rotation = Q(toe, _right) * _restFootWorld[i]; // flat on the ground unless pitched
            }

            private static void SolveLeg(Transform hip, Transform knee, Transform ankle, Vector3 target, Vector3 pole)
            {
                Vector3 h = hip.position, k = knee.position, a = ankle.position;
                float la = (k - h).magnitude, lb = (a - k).magnitude;
                float d = Mathf.Clamp((target - h).magnitude, Mathf.Abs(la - lb) + 1e-4f, la + lb - 1e-4f);
                float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
                float cur = Vector3.Angle(h - k, a - k);
                Vector3 axis = Vector3.Cross(h - k, a - k);
                if (axis.sqrMagnitude > 1e-10f)
                {
                    axis.Normalize();
                    var bend = Q(want - cur, axis);
                    if (Mathf.Abs(Vector3.Angle(h - k, bend * (a - k)) - want) > 0.5f) bend = Q(cur - want, axis); // handedness check
                    knee.rotation = bend * knee.rotation;
                }
                hip.rotation = Quaternion.FromToRotation(ankle.position - h, target - h) * hip.rotation;
                Vector3 along = (target - h).normalized;
                Vector3 kneeDir = Vector3.ProjectOnPlane(knee.position - h, along);
                Vector3 poleDir = Vector3.ProjectOnPlane(pole, along);
                if (kneeDir.sqrMagnitude > 1e-8f && poleDir.sqrMagnitude > 1e-8f)
                    hip.rotation = Q(Vector3.SignedAngle(kneeDir, poleDir, along), along) * hip.rotation;
            }

            private static void Aim(Transform bone, Transform child, Vector3 dir) =>
                bone.rotation = Quaternion.FromToRotation(child.position - bone.position, dir) * bone.rotation;

            private static void Turn(Transform t, Quaternion worldDelta) => t.rotation = worldDelta * t.rotation;
            private static Quaternion Q(float degrees, Vector3 axis) => Quaternion.AngleAxis(degrees, axis);
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
