"""KAN-146: Plague Bloater Burst clip - arms held wider so the hands stay out of the belly and the apron skirt.
blender -b --python fix_bloater_burst.py -- <meshy_job_dir> [<out_fbx> [<render_dir>]]
  no <out_fbx>: prints the clip report only (arm vertices inside the body, per frame, left / right).
  <out_fbx>:    turns both upper arms SWING deg outward (eased in over RAMP frames, then held), re-measures and writes
                the clip with the same FBX settings as to_unity.py (armature only, one take named Burst).
  <render_dir>: also writes Blender before / after close-ups (front-high and side) of RENDER_FRAMES.
Check the result in Unity: SurvivorSouls.Editor.Enemies.BloaterSwellShots (2-bone skin, swell scale, gameplay angle).
Example (repo root):
  blender -b --python Tools/Meshy/fix_bloater_burst.py -- ~/survivor-souls-assets/meshy_jobs/20261015_031720_enemy-plague-bloater_01a13cb4 \
    Assets/_Project/Art/Characters/Enemies/PlagueBloater/A_PlagueBloater_Burst.fbx

Why it clips: Meshy auto-rig weights the big belly and the apron to the thigh bones (LeftUpLeg / RightUpLeg), so they
swing forward with the lunge of the Meshy Burst action (386) while the hands hang close to the body: the hands slide
under the apron skirt (from ~0.75 s) and the inner upper arm sinks into the belly side. The Mobile quality skins with 2
bones per vertex; the measure uses the same 2-bone skin.

Measure: generalised winding number of each arm vertex against every non-arm triangle (> W_IN = inside). The Meshy mesh
is a triangle soup (every edge open, flat shaded), so nearest-face and ray tests lie; the winding number does not need a
closed mesh. Vertices already inside in the rest pose (the armpit seam, where the upper arm meets the chest) are ignored:
no arm swing moves them and they are never seen. The apron is a thin open skirt, so a hand under it is not "inside" by
this measure: the report counts the belly / chest contact, the Unity close-ups show the skirt. A per-frame automatic
swing (smallest angle that clears the measure) needed up to 30 deg for that small contact and bent the clip too much,
so one held swing was chosen instead."""
import bpy, sys, os, math
import numpy as np
from mathutils import Matrix, Vector, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
job = argv[0]
out = argv[1] if len(argv) > 1 else None
render_dir = argv[2] if len(argv) > 2 else None

FBX = dict(use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
           axis_forward='-Z', axis_up='Y', add_leaf_bones=False, primary_bone_axis='Y',
           secondary_bone_axis='X', armature_nodetype='NULL', path_mode='STRIP',
           embed_textures=False, mesh_smooth_type='FACE', use_armature_deform_only=True)  # = to_unity.py

ARM_BONES = {"L": ("LeftArm", "LeftForeArm", "LeftHand"), "R": ("RightArm", "RightForeArm", "RightHand")}
ARM_ONLY = set(ARM_BONES["L"] + ARM_BONES["R"])
W_IN = 0.35           # winding number above this = inside (0.5 is the surface; a bit lower = a small skin margin)
SKIN_BONES = 2        # Mobile quality skin weights (ProjectSettings/QualitySettings.asset, PC = 4)
SWING = 15.0          # deg outward per upper arm: clears the apron skirt in the Unity close-ups (BloaterSwellShots)
RAMP = 6              # frames (0.25 s) to ease the swing in
RENDER_FRAMES = (6, 12, 18, 24, 34, 44)  # 24 fps: the 1 s swell shows frames 1-24; the rest only on a long swell

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(job, "anim_Burst.glb"))
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm][0]
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.parent != arm]:
    bpy.data.objects.remove(o, do_unlink=True)  # stray Meshy Icosphere
act = arm.animation_data.action
f0, f1 = (int(round(x)) for x in act.frame_range)
sc = bpy.context.scene
mw = body.matrix_world

# same skin as the phone: keep the 2 strongest weights per vertex
bpy.context.view_layer.objects.active = body
body.select_set(True)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=SKIN_BONES)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)

gn = {g.index: g.name for g in body.vertex_groups}
def dom(v): return gn[max(v.groups, key=lambda g: g.weight).group] if len(v.groups) else None
body_t = np.array([tuple(p.vertices) for p in body.data.polygons
                   if not any(dom(body.data.vertices[i]) in ARM_ONLY for i in p.vertices)])
side_v = {s: np.array([v.index for v in body.data.vertices if dom(v) in b]) for s, b in ARM_BONES.items()}


def evaluated_points():
    dg = bpy.context.evaluated_depsgraph_get()
    eb = body.evaluated_get(dg)
    m = eb.to_mesh()
    co = np.empty(len(m.vertices) * 3)
    m.vertices.foreach_get("co", co)
    eb.to_mesh_clear()
    co = co.reshape(-1, 3)
    M = np.array(mw)
    return co @ M[:3, :3].T + M[:3, 3]


def winding(V, ids):
    A, B, C = V[body_t[:, 0]], V[body_t[:, 1]], V[body_t[:, 2]]
    w = np.empty(len(ids))
    for k, i in enumerate(ids):
        a, b, c = A - V[i], B - V[i], C - V[i]
        la, lb, lc = np.linalg.norm(a, axis=1), np.linalg.norm(b, axis=1), np.linalg.norm(c, axis=1)
        num = np.einsum('ij,ij->i', a, np.cross(b, c))
        den = (la * lb * lc + np.einsum('ij,ij->i', a, b) * lc + np.einsum('ij,ij->i', b, c) * la
               + np.einsum('ij,ij->i', c, a) * lb)
        w[k] = np.arctan2(num, den).sum() / (2 * np.pi)
    return w


# armpit seam: arm vertices inside in the rest pose
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis = Matrix()
bpy.context.view_layer.update()
V0 = evaluated_points()
for s in ("L", "R"):
    seam = winding(V0, side_v[s]) > W_IN
    side_v[s] = side_v[s][~seam]
    print(f"BLOATER {s} arm verts {len(side_v[s])} (armpit seam {int(seam.sum())} ignored)")
arm.animation_data.action = act
print(f"BLOATER body tris {len(body_t)} frames {f0}-{f1} skin {SKIN_BONES} bones")


def inside(side):
    return int((winding(evaluated_points(), side_v[side]) > W_IN).sum())


# Outward swing: the upper arm turns round its head about the body's forward axis, away from the centre line.
fwd = (arm.matrix_world.inverted().to_3x3() @ Vector((0, -1, 0))).normalized()  # glTF front = -Y in Blender


def swing(side, deg):
    """Turns <side>Arm outward by deg on top of the current pose."""
    pb = arm.pose.bones[ARM_BONES[side][0]]
    sign = 1.0 if side == "L" else -1.0
    head = pb.head.copy()
    R = Matrix.Translation(head) @ Quaternion(fwd, math.radians(deg * sign)).to_matrix().to_4x4() @ Matrix.Translation(-head)
    m = arm.convert_space(pose_bone=pb, matrix=R @ pb.matrix, from_space='POSE', to_space='LOCAL')
    pb.rotation_quaternion = m.to_quaternion()


def pose(f, angles=None):
    sc.frame_set(f)
    if angles:
        for s in ("L", "R"):
            if angles[s][f] > 0:
                swing(s, angles[s][f])
        bpy.context.view_layer.update()


def check_sign():
    """Outward must move each hand away from the centre line."""
    ok = True
    for s, hand in (("L", "LeftHand"), ("R", "RightHand")):
        pose(f0)
        x0 = abs((arm.matrix_world @ arm.pose.bones[hand].head).x)
        swing(s, 20); bpy.context.view_layer.update()
        ok &= abs((arm.matrix_world @ arm.pose.bones[hand].head).x) > x0
    pose(f0)
    return ok


def report(tag, angles=None):
    worst = 0; total = 0
    for f in range(f0, f1 + 1):
        pose(f, angles)
        nl, nr = inside("L"), inside("R")
        worst = max(worst, nl, nr); total += nl + nr
        a = f"  swing L {angles['L'][f]:4.1f} R {angles['R'][f]:4.1f}" if angles else ""
        print(f"CLIP {tag} f{f:02d} L {nl:3d} R {nr:3d}{a}")
    print(f"CLIP {tag} worst {worst} total {total}")
    return worst


def render(tag, angles=None):
    """Close-ups of the swell frames: front-high (gameplay camera) and side, workbench texture colour."""
    if not render_dir:
        return
    os.makedirs(render_dir, exist_ok=True)
    for m in bpy.data.materials:
        if m.use_nodes:
            b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if b and b.inputs['Base Color'].links:
                m.node_tree.nodes.active = b.inputs['Base Color'].links[0].from_node
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x, sc.render.resolution_y = 360, 420
    if sc.world is None:
        sc.world = bpy.data.worlds.new("w")
    cam = bpy.data.objects.get("cam") or bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.2
    c = Vector((0, 0, 0.85))
    for view, d in (("front", Vector((0.25, -1, 1.0))), ("side", Vector((-1, -0.35, 0.3)))):
        d.normalize()
        cam.location = c + d * 6
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        for f in RENDER_FRAMES:
            pose(f, angles)
            sc.render.filepath = os.path.join(render_dir, f"{tag}_{view}_f{f:02d}.png")
            bpy.ops.render.render(write_still=True)


if not check_sign():
    raise SystemExit("BLOATER outward swing sign is wrong for this rig")

report("before")
render("before")
if out is None:
    raise SystemExit(0)

# 1) one outward swing for the whole clip, eased in over the first RAMP frames (the game cross-fades in from the walk)
def ease(f):
    t = min(1.0, max(0.0, (f - f0) / RAMP))
    return t * t * (3 - 2 * t)
angles = {s: {f: SWING * ease(f) for f in range(f0, f1 + 1)} for s in ("L", "R")}

worst = report("after", angles)
render("after", angles)

# 3) bake: key the upper-arm rotation on every frame (the glTF action is sampled per frame already)
for f in range(f0, f1 + 1):
    pose(f, angles)
    for s in ("L", "R"):
        arm.pose.bones[ARM_BONES[s][0]].keyframe_insert("rotation_quaternion", frame=f)
pose(f0)
for f in (f0, (f0 + f1) // 2, f1):  # the baked keys play back the swing
    pose(f)
    print(f"BLOATER baked f{f:02d} L {inside('L')} R {inside('R')}")

# 4) export like to_unity.py
for o in [o for o in bpy.data.objects if o.type != 'ARMATURE']:
    bpy.data.objects.remove(o, do_unlink=True)
act.name = "Burst"
sc.name = "Burst"  # FBX take name comes from the scene
arm.name = "PlagueBloater_Rig"
sc.frame_start, sc.frame_end = f0, f1
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=out, object_types={'ARMATURE'}, bake_anim=True, bake_anim_use_all_actions=False,
                         bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.5, **FBX)
print(f"BLOATER wrote {out} (worst after {worst})")
