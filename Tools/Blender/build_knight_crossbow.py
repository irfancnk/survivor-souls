"""
Build the crossbow knight for Survivor Souls (two-handed hold).

Run headless from the repo root:
    blender -b --python Tools/Blender/build_knight_crossbow.py -- [--preview DIR] [--out GLB]

Steps:
  1. Import the Meshy knight (Mixamo rig, clips Walking / Running / Hit_Reaction).
  2. Append the crossbow from the asset .blend, decimate + scale it, parent it to Spine2 (it follows the torso).
  3. Add a combat stance on the spine (left shoulder forward, small forward lean, head looks ahead).
  4. Both arms: IK (forearm, chain 2) + Copy Rotation (hand) to hand targets on the crossbow. Fingers curled.
  5. Make an Idle clip (bladed ready stance, feet pinned by leg IK, slow breathing).
  6. Bake every clip by hand (pose matrices -> local keys, quaternion continuity), drop the rig helpers.
  7. Export one GLB for Unity (ArtSource/Characters/Archer/Knight_Crossbow.glb) + optional preview renders.

Blender space: character faces -Y, up +Z, character right = -X.
Crossbow local space (asset): muzzle at -X, stock at +X, up +Z. Origin = stock wrist (grip).
All tuning numbers are in the CONFIG block. Change them, run again, check the preview renders.
"""
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

# ------------------------------------------------------------------ CONFIG
HOME = os.path.expanduser("~")
KNIGHT_GLB = f"{HOME}/survivor-souls-assets/models/Meshy_AI_Ironclad_Sentinel_All_Animations.glb"
CROSSBOW_BLEND = f"{HOME}/survivor-souls-assets/models/knight_crossbow/Knight_Crossbow.blend"
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_GLB = os.path.join(REPO, "ArtSource/Characters/Archer/Knight_Crossbow.glb")
OUT_BLEND = f"{HOME}/survivor-souls-assets/models/knight_crossbow/Knight_Crossbow_2H.blend"

CROSSBOW_SCALE = 0.84          # relative to the 0.857 m asset -> about 0.72 m long
CROSSBOW_TARGET_TRIS = 5200    # mobile budget for the weapon
CROSSBOW_TEXTURE_MAX = 1024
# Crossbow pose at rest (after stance), world space. Grip = crossbow origin.
CROSSBOW_GRIP = Vector((-0.115, -0.20, 1.16))
CROSSBOW_YAW_DEG = 14.0        # + turns the muzzle to the character's left (across the body)
CROSSBOW_PITCH_DEG = -5.0      # - points the muzzle down (low ready)
CROSSBOW_ROLL_DEG = 0.0

# Stance: world-axis rotations (degrees) pre-multiplied per bone on every clip.
# (yaw about +Z, pitch about +X: + leans forward, roll about the forward axis)
STANCE = {
    "mixamorig:Spine": (-10.0, 2.0, 0.0),
    "mixamorig:Spine1": (-9.0, 2.0, 0.0),
    "mixamorig:Spine2": (-8.0, 2.0, 0.0),
    "mixamorig:Neck": (12.0, -2.0, 0.0),
    "mixamorig:Head": (12.0, -2.0, 0.0),
}
# Steadier upper body while moving: spine motion is pulled this much toward its cycle average (0 = original).
SPINE_DAMP = {"mixamorig:Spine": 0.5, "mixamorig:Spine1": 0.5, "mixamorig:Spine2": 0.5}

# Hand contacts in crossbow asset space: point, finger direction, thumb direction.
RIGHT_HAND = dict(contact=(0.02, 0.0, -0.045), fingers=(-0.55, -0.55, 0.35), thumb=(-0.35, -0.25, 1.0))
LEFT_HAND = dict(contact=(-0.25, 0.0, -0.075), fingers=(-0.45, 0.85, 0.15), thumb=(-1.0, -0.1, 0.45))
PALM_DEPTH = 0.035     # palm centre -> wrist, along the palm normal (m)
WRIST_TO_PALM = 0.065  # palm centre -> wrist, along the fingers (m)
FINGER_CURL_DEG = {"Index": (38, 55, 40), "Middle": (55, 70, 45), "Ring": (60, 72, 45), "Pinky": (62, 72, 45),
                   "Thumb": (12, 22, 18)}
TRIGGER_INDEX_RIGHT = (20, 30, 25)  # right index finger lies along the trigger

# Elbow poles, world space at rest.
RIGHT_POLE = Vector((-0.55, 0.30, 0.95))
LEFT_POLE = Vector((0.55, 0.05, 0.75))
POLE_ANGLE_DEG = -90.0

# Idle: hips drop (m), hips yaw (deg) for a bladed ready stance, breathing pitch (deg).
IDLE_FRAMES = 48
IDLE_HIPS_DROP = 0.035
IDLE_HIPS_YAW = -14.0
IDLE_BREATH_DEG = 1.6

CLIPS = ["Walking", "Running", "Hit_Reaction"]
B = "mixamorig:"
ARM_BONES = [B + n for n in ("LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
                             "RightShoulder", "RightArm", "RightForeArm", "RightHand")]
LEG_BONES = [B + n for n in ("LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot")]


# ------------------------------------------------------------------ helpers
def log(*a):
    print("[knight]", *a, flush=True)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {"preview": None, "out": OUT_GLB}
    for i, a in enumerate(argv):
        if a in ("--preview", "--out"):
            out[a[2:]] = argv[i + 1]
    return out


def world_rot(yaw, pitch, roll):
    """Yaw about +Z, pitch about +X (+ = lean toward -Y = forward), roll about -Y."""
    return (Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((1, 0, 0), math.radians(pitch))
            @ Quaternion((0, -1, 0), math.radians(roll)))


def to_local(arm, bone_name, q_world):
    """World-axis rotation expressed in the bone's rest frame (pre-multiply onto the pose basis)."""
    r = arm.data.bones[bone_name].matrix_local.to_quaternion()
    return r.inverted() @ q_world @ r


def frames_of(action):
    a, b = action.frame_range
    return list(range(int(round(a)), int(round(b)) + 1))


def set_action(arm, action):
    arm.animation_data.action = action
    if getattr(action, "slots", None):
        arm.animation_data.action_slot = action.slots[0]


def rest_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.matrix_basis = Matrix()
    bpy.context.view_layer.update()


def new_empty(name, world=None, parent=None, bone=None):
    o = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
        if bone:
            o.parent_type, o.parent_bone = "BONE", bone
    if world is not None:
        bpy.context.view_layer.update()
        o.matrix_world = world
    return o


def hand_thumb_local(arm, side):
    """Rest-pose thumb direction in hand-bone space, flattened to the plane across the bone (Y = bone axis)."""
    hand = arm.data.bones[f"{B}{side}Hand"]
    thumb = arm.data.bones[f"{B}{side}HandThumb1"]
    t = hand.matrix_local.to_3x3().inverted() @ (thumb.head_local - hand.head_local)
    t.y = 0.0
    return t.normalized()


def palm_dir(side, fingers, thumb):
    """Direction from the palm toward what the hand holds (right hand: thumb x fingers)."""
    return (thumb.cross(fingers) if side == "Right" else fingers.cross(thumb)).normalized()


# ------------------------------------------------------------------ build
def import_knight():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=KNIGHT_GLB)
    for o in list(bpy.data.objects):
        if o.type == "MESH" and o.parent is None:
            bpy.data.objects.remove(o)  # stray Icosphere in the Meshy export
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    arm.name = "Knight"
    arm.animation_data.action = None
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
    bpy.context.scene.render.fps = 24
    return arm


def append_crossbow():
    with bpy.data.libraries.load(CROSSBOW_BLEND) as (_, dst):
        dst.objects = ["Crossbow", "Crossbow_Muzzle"]
    for o in dst.objects:
        bpy.context.scene.collection.objects.link(o)
    xb = bpy.data.objects["Crossbow"]
    xb.parent = None
    xb.matrix_world = Matrix()
    tris = sum(len(p.vertices) - 2 for p in xb.data.polygons)
    if tris > CROSSBOW_TARGET_TRIS:
        mod = xb.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = CROSSBOW_TARGET_TRIS / tris
        with bpy.context.temp_override(object=xb, active_object=xb):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    log("crossbow tris", sum(len(p.vertices) - 2 for p in xb.data.polygons))
    # Unity keeps the weapon textures at 512 px; 1024 in the GLB keeps the source file small.
    for slot in xb.material_slots:
        for node in (slot.material.node_tree.nodes if slot.material and slot.material.use_nodes else []):
            img = getattr(node, "image", None)
            if img is not None and max(img.size) > CROSSBOW_TEXTURE_MAX:
                img.scale(CROSSBOW_TEXTURE_MAX, CROSSBOW_TEXTURE_MAX)
    return xb


def stance_offsets(arm):
    return {n: to_local(arm, n, world_rot(*v)) for n, v in STANCE.items()}


def pose_rest_with_stance(arm, offs):
    rest_pose(arm)
    for n, q in offs.items():
        arm.pose.bones[n].rotation_quaternion = q
    bpy.context.view_layer.update()


def place_crossbow(arm, xb):
    # Rz(90) maps asset +X (stock) to world +Y (back), so the muzzle (-X) points forward (-Y).
    rot = (Euler((0, 0, math.radians(90 + CROSSBOW_YAW_DEG))).to_matrix()
           @ Euler((0, math.radians(CROSSBOW_PITCH_DEG), 0)).to_matrix()
           @ Euler((math.radians(CROSSBOW_ROLL_DEG), 0, 0)).to_matrix())
    world = Matrix.Translation(CROSSBOW_GRIP) @ rot.to_4x4() @ Matrix.Scale(CROSSBOW_SCALE, 4)
    xb.parent, xb.parent_type, xb.parent_bone = arm, "BONE", B + "Spine2"
    bpy.context.view_layer.update()
    xb.matrix_world = world
    bpy.context.view_layer.update()


def add_arm_rig(arm, xb):
    """Hand target empties (children of the crossbow) + IK and Copy Rotation constraints."""
    helpers = []
    xr = xb.matrix_world.to_3x3().normalized()
    for side, cfg, pole in (("Right", RIGHT_HAND, RIGHT_POLE), ("Left", LEFT_HAND, LEFT_POLE)):
        f = (xr @ Vector(cfg["fingers"])).normalized()
        t = xr @ Vector(cfg["thumb"])
        t = (t - f * t.dot(f)).normalized()
        # Target basis: bone +Y -> fingers, bone thumb axis -> t.
        tl = hand_thumb_local(arm, side)
        local = Matrix((tl, Vector((0, 1, 0)), tl.cross(Vector((0, 1, 0))))).transposed()
        world = Matrix((t, f, t.cross(f))).transposed()
        hand_rot = world @ local.inverted()
        contact = xb.matrix_world @ Vector(cfg["contact"])
        wrist = contact - f * WRIST_TO_PALM - palm_dir(side, f, t) * PALM_DEPTH
        tgt = new_empty(f"{side}HandTarget", parent=xb,
                        world=Matrix.Translation(wrist) @ hand_rot.to_4x4())
        pole_o = new_empty(f"{side}ElbowPole", parent=arm, bone=B + "Spine2", world=Matrix.Translation(pole))
        helpers += [tgt, pole_o]

        ik = arm.pose.bones[f"{B}{side}ForeArm"].constraints.new("IK")
        ik.target, ik.pole_target, ik.chain_count, ik.use_tail = tgt, pole_o, 2, True
        ik.pole_angle = math.radians(POLE_ANGLE_DEG)
        arm.pose.bones[f"{B}{side}Hand"].constraints.new("COPY_ROTATION").target = tgt
        shoulder = arm.pose.bones[f"{B}{side}Arm"].head
        log(f"{side} wrist->shoulder {(wrist - shoulder).length:.3f} m (arm length 0.446)")
    bpy.context.view_layer.update()
    return helpers


def finger_bases(arm):
    """Constant local rotations that curl each finger toward the palm."""
    out = {}
    for side in ("Right", "Left"):
        h3 = arm.data.bones[f"{B}{side}Hand"].matrix_local.to_3x3()
        y = Vector((0, 1, 0))
        palm = palm_dir(side, y, hand_thumb_local(arm, side))   # hand-local
        curl_axis = y.cross(palm).normalized()                   # rotates +Y toward the palm
        thumb_axis = (curl_axis + palm.cross(y).normalized() * 0.5).normalized()
        for finger, angles in FINGER_CURL_DEG.items():
            if side == "Right" and finger == "Index":
                angles = TRIGGER_INDEX_RIGHT
            axis_h = thumb_axis if finger == "Thumb" else curl_axis
            for i, deg in enumerate(angles, start=1):
                name = f"{B}{side}Hand{finger}{i}"
                if name in arm.data.bones:
                    fb = arm.data.bones[name].matrix_local.to_3x3()
                    out[name] = Quaternion((fb.inverted() @ h3 @ axis_h).normalized(), math.radians(deg))
    return out


# ------------------------------------------------------------------ clips
def add_stance_keys(arm, action, offs):
    """Pre-multiply the stance offsets onto the spine keys of one action."""
    set_action(arm, action)
    frames = frames_of(action)
    cache = {}
    for f in frames:
        bpy.context.scene.frame_set(f)
        for n in offs:
            pb = arm.pose.bones[n]
            pb.rotation_mode = "QUATERNION"
            cache[(n, f)] = pb.rotation_quaternion.copy()
    for n, damp in SPINE_DAMP.items():
        avg = Quaternion((0, 0, 0, 0))
        for f in frames:
            q = cache[(n, f)]
            avg = avg + (q if avg.dot(q) >= 0 else -q)
        avg.normalize()
        for f in frames:
            cache[(n, f)] = avg.slerp(cache[(n, f)], 1.0 - damp)
    for f in frames:
        for n, q in offs.items():
            pb = arm.pose.bones[n]
            pb.rotation_quaternion = q @ cache[(n, f)]
            pb.keyframe_insert("rotation_quaternion", frame=f)


def make_idle(arm):
    """Idle: hips a little lower and turned (bladed stance), upper body turned back, slow breathing."""
    idle = bpy.data.actions.new("Idle")
    set_action(arm, idle)
    rest_pose(arm)
    hips, spine = arm.pose.bones[B + "Hips"], arm.pose.bones[B + "Spine1"]
    drop = arm.data.bones[B + "Hips"].matrix_local.to_3x3().inverted() @ Vector((0, 0, -IDLE_HIPS_DROP))
    for f in range(IDLE_FRAMES + 1):
        ph = math.sin(2 * math.pi * f / IDLE_FRAMES)
        hips.location = drop * (1.0 + 0.2 * ph)
        hips.rotation_quaternion = to_local(arm, B + "Hips", world_rot(IDLE_HIPS_YAW, 0, 0))
        spine.rotation_quaternion = to_local(arm, B + "Spine1", world_rot(-IDLE_HIPS_YAW, IDLE_BREATH_DEG * ph, 0))
        hips.keyframe_insert("location", frame=f)
        hips.keyframe_insert("rotation_quaternion", frame=f)
        spine.keyframe_insert("rotation_quaternion", frame=f)
    return idle


def add_leg_ik(arm):
    """Feet pinned a bit wider than rest, right foot back (for the Idle stance)."""
    rest_pose(arm)
    made = []
    for side, dx, dy in (("Left", 0.04, -0.07), ("Right", -0.04, 0.10)):
        foot = arm.pose.bones[f"{B}{side}Foot"]
        t = new_empty(f"{side}FootTarget", world=Matrix.Translation((dx, dy, 0)) @ arm.matrix_world @ foot.matrix)
        knee = arm.matrix_world @ arm.pose.bones[f"{B}{side}Leg"].head
        p = new_empty(f"{side}KneePole", world=Matrix.Translation(knee + Vector((dx * 2, -0.6, 0))))
        ik = arm.pose.bones[f"{B}{side}Leg"].constraints.new("IK")
        ik.target, ik.pole_target, ik.chain_count, ik.use_tail = t, p, 2, True
        ik.pole_angle = math.radians(-90)
        foot.constraints.new("COPY_ROTATION").target = t
        made += [t, p]
    return made


def capture(arm, action, bones):
    """Constrained armature-space matrices per frame, parents first."""
    set_action(arm, action)
    depth = {n: len(arm.data.bones[n].parent_recursive) for n in bones}
    order = sorted(bones, key=lambda n: depth[n])
    frames = frames_of(action)
    data = {}
    for f in frames:
        bpy.context.scene.frame_set(f)
        data[f] = {n: arm.pose.bones[n].matrix.copy() for n in order}
    return frames, order, data


def write_capture(arm, action, frames, order, data, fingers):
    """Turn captured matrices into local rotation keys (constraints already removed)."""
    set_action(arm, action)
    prev = {}
    for f in frames:
        bpy.context.scene.frame_set(f)
        for n in order:
            bone = arm.data.bones[n]
            par = bone.parent
            ppose = data[f][par.name] if par.name in data[f] else arm.pose.bones[par.name].matrix
            rel = par.matrix_local.inverted() @ bone.matrix_local
            rot = (rel.inverted() @ ppose.inverted() @ data[f][n]).to_quaternion()
            if n in prev and prev[n].dot(rot) < 0:
                rot.negate()
            prev[n] = rot
            pb = arm.pose.bones[n]
            pb.rotation_quaternion = rot
            pb.keyframe_insert("rotation_quaternion", frame=f)
        for n, q in fingers.items():
            pb = arm.pose.bones[n]
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=f)


# ------------------------------------------------------------------ preview
def preview(arm, out_dir, actions):
    """Flat-colour close-ups (knight grey, crossbow orange) to check grip and posture."""
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "OBJECT"
    sc.display.shading.show_cavity = True
    for o in bpy.data.objects:
        o.color = (1.0, 0.45, 0.1, 1) if o.name == "Crossbow" else (0.75, 0.75, 0.78, 1)
    sc.render.resolution_x, sc.render.resolution_y = 400, 400
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    views = {"front": ((0, -4, 1.15), (90, 0, 0), 1.1), "right": ((-4, 0, 1.15), (90, 0, -90), 1.1),
             "q34": ((-2.6, -2.6, 2.6), (60, 0, -45), 1.3), "full": ((-2.8, -3.4, 2.2), (72, 0, -40), 2.1),
             "game": ((0, -2.2, 4.4), (28, 0, 0), 2.0)}
    for act in actions:
        set_action(arm, act)
        fr = frames_of(act)
        for f in sorted({fr[0], fr[len(fr) // 2]}):
            sc.frame_set(f)
            grip = bpy.data.objects["Crossbow"].matrix_world.translation
            views["hands"] = (tuple(grip + Vector((0.55, -0.9, 0.35))), (72, 0, 31), 0.55)
            for vn, (loc, rot, scale) in views.items():
                cam.location = loc
                cam.rotation_euler = [math.radians(a) for a in rot]
                cam.data.ortho_scale = scale
                sc.render.filepath = os.path.join(out_dir, f"{act.name}_{f:02d}_{vn}.png")
                bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ------------------------------------------------------------------ main
def main():
    opt = parse_args()
    arm = import_knight()
    actions = [bpy.data.actions[n] for n in CLIPS]
    xb = append_crossbow()
    offs = stance_offsets(arm)
    pose_rest_with_stance(arm, offs)
    place_crossbow(arm, xb)
    helpers = add_arm_rig(arm, xb)
    fingers = finger_bases(arm)

    idle = make_idle(arm)
    actions.append(idle)
    for act in actions:
        add_stance_keys(arm, act, offs)

    captured = {a.name: capture(arm, a, ARM_BONES) for a in actions if a is not idle}
    helpers += add_leg_ik(arm)
    captured[idle.name] = capture(arm, idle, ARM_BONES + LEG_BONES)

    for pb in arm.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    for o in helpers:
        bpy.data.objects.remove(o)
    for act in actions:
        write_capture(arm, act, *captured[act.name], fingers)

    if opt["preview"]:
        preview(arm, opt["preview"], actions)

    # One NLA track per action: the exporter writes each as its own glTF animation.
    for act in actions:
        tr = arm.animation_data.nla_tracks.new()
        tr.name = act.name
        tr.strips.new(act.name, int(act.frame_range[0]), act)
    arm.animation_data.action = None
    rest_pose(arm)
    os.makedirs(os.path.dirname(opt["out"]), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=opt["out"], export_format="GLB", export_animation_mode="NLA_TRACKS",
                              export_yup=True, export_force_sampling=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    log("exported", opt["out"])


if __name__ == "__main__":
    main()
