"""Blender headless build of the Shadow Ranger hero from the raw Meshy rig.
blender -b --python build_shadow_ranger.py -- <job_dir> [--stage A|B] [--renders DIR]

Input  <job_dir>/rigged_meshy.glb (Meshy auto-rig, fused bow / string / arrow from the "drawn bow" preview pose),
       <job_dir>/anim_Walking.glb, anim_Running.glb (rig clips).
Output <job_dir>/rigged.glb (clean mesh + new bow + BowString bone), <job_dir>/anim_Shoot.glb + anim_Idle.glb
       (hand-keyed, IK baked), <job_dir>/ShadowRanger.blend (work file for the user's Blender).

Stage A (mesh): merge the remesh triangle soup, cut the old bow / string / arrow, quiver -> Spine, smooth shading,
                hood shadow painted over the face, dark face cloth (Head).
Stage B (bow + animation): see the second half of this file.
All positions are world space of the Meshy rest pose (glTF import, character faces -Y, up +Z), measured from plots."""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:]
JOB = ARGS[0]
STAGE = ARGS[ARGS.index("--stage") + 1] if "--stage" in ARGS else "B"
RENDERS = ARGS[ARGS.index("--renders") + 1] if "--renders" in ARGS else None

# ---------------------------------------------------------------- measured geometry of the fused parts
BOW = [(0.12, -0.18, 1.775), (0.167, -0.21, 1.699), (0.25, -0.30, 1.64), (0.311, -0.311, 1.595), (0.356, -0.339, 1.548),
       (0.401, -0.372, 1.461), (0.408, -0.372, 1.352), (0.436, -0.38, 1.243), (0.414, -0.358, 1.128), (0.384, -0.331, 1.061),
       (0.326, -0.278, 0.979), (0.27, -0.28, 0.9), (0.24, -0.25, 0.85)]
NOCK = (0.18, -0.18, 1.375)
STRING = [BOW[0], NOCK, BOW[-1]]
ARROW = [NOCK, (0.53, -0.46, 1.39)]
HAND = (0.47, -0.42, 1.38)
FACE = dict(x=(-0.13, 0.11), z=(1.30, 1.55))      # head box for the shadow paint (skin texels only)
MASK_Z = (1.275, 1.432)                             # face cloth / cowl: into the collar to the nose bridge (tip at 1.42)
CHIN_Z = 1.372                                      # below this the cloth drapes straight down from the chin line


def dseg(p, a, b):
    a, b, p = Vector(a), Vector(b), Vector(p)
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


def dpoly(p, pts):
    return min(dseg(p, pts[i], pts[i + 1]) for i in range(len(pts) - 1))


def old_bow_part(p):
    if (Vector(p) - Vector(HAND)).length < 0.075:
        return False
    return dpoly(p, BOW) < 0.045 or dpoly(p, STRING) < 0.03 or dpoly(p, ARROW) < 0.03


def in_quiver(p):
    return -0.30 < p.x < -0.08 and 0.055 < p.y < 0.13 and 1.36 < p.z < 1.60


# ---------------------------------------------------------------- load
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(JOB, "rigged_meshy.glb"))
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.parent != arm]:
    bpy.data.objects.remove(o, do_unlink=True)          # stray Icosphere in Meshy rig GLBs
body = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm][0]
arm.name, body.name = "ShadowRanger_Rig", "ShadowRanger_Body"
mw = body.matrix_world.copy()
mwi = mw.inverted()

# ---------------------------------------------------------------- A1 merge + cut
bm = bmesh.new(); bm.from_mesh(body.data)
n0 = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
dead = [f for f in bm.faces if old_bow_part(mw @ f.calc_center_median())]
bmesh.ops.delete(bm, geom=dead, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bm.to_mesh(body.data); bm.free()
print(f"A1 merge {n0} -> {len(body.data.vertices)} verts, cut {len(dead)} faces")

# ---------------------------------------------------------------- A1b old bow grip flakes inside the bow hand
# The cut above spares the hand. Inside it, faces past the wrist that do not sample skin are the old grip / arrowhead.
_mat0 = body.data.materials[0]
_img0 = next(n for n in _mat0.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].links[0].from_node.image
_W0, _H0 = _img0.size
_px0 = np.array(_img0.pixels[:], dtype=np.float32).reshape(_H0, _W0, 4)
_wl = arm.matrix_world @ arm.data.bones["LeftHand"].head_local
_hd = (Vector(HAND) - _wl).normalized()
_uv0 = body.data.uv_layers.active.data
bm = bmesh.new(); bm.from_mesh(body.data); bm.faces.ensure_lookup_table()
uvl = bm.loops.layers.uv.active
flakes = []
for f in bm.faces:
    c = mw @ f.calc_center_median()
    longest = max(((mw @ f.verts[i].co) - (mw @ f.verts[(i + 1) % len(f.verts)].co)).length for i in range(len(f.verts)))
    behind = (c - _wl).dot(_hd)
    if (c - Vector(HAND)).length > 0.10 or behind < (-0.02 if longest > 0.045 else 0.0):
        continue                                # long slivers just behind the wrist are the old arrow's tail
    u = sum((l[uvl].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
    col = _px0[int(min(max(u.y, 0), 1) * (_H0 - 1)), int(min(max(u.x, 0), 1) * (_W0 - 1))]
    if not (col[0] > col[2] + 0.06 and col[0] > 0.22):
        flakes.append(f)
bmesh.ops.delete(bm, geom=flakes, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bm.to_mesh(body.data); bm.free()
print("A1b grip flakes cut", len(flakes))

# ---------------------------------------------------------------- A2 quiver rigid on Spine
spine = body.vertex_groups["Spine"]
q = 0
for v in body.data.vertices:
    if in_quiver(mw @ v.co):
        for g in list(v.groups):
            body.vertex_groups[g.group].remove([v.index])
        spine.add([v.index], 1.0, 'REPLACE'); q += 1
print("A2 quiver verts", q)

# ---------------------------------------------------------------- A3 smooth shading (keeps hard creases > 40 deg)
bpy.context.view_layer.objects.active = body
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True)
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40), keep_sharp_edges=True)

# ---------------------------------------------------------------- A4 hood shadow painted into the base colour
# Per texel: rasterise every head face in UV space, get the texel's 3D height, darken by a smooth height ramp
# (deep under the hood brow, softer at the chin), weighted by a soft skin mask so the hood cloth is untouched.
mat = body.data.materials[0]
bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
img = bsdf.inputs['Base Color'].links[0].from_node.image
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
uv = body.data.uv_layers.active.data
zmap = np.full((H, W), np.nan, dtype=np.float32)
painted = 0
for poly in body.data.polygons:
    c = mw @ poly.center
    if not (FACE["x"][0] < c.x < FACE["x"][1] and FACE["z"][0] < c.z < FACE["z"][1] and c.y < -0.08):
        continue
    corners = [mw @ body.data.vertices[i].co for i in poly.vertices]
    uvs = [uv[li].uv.copy() for li in poly.loop_indices]
    xs = [u.x * (W - 1) for u in uvs]; ys = [u.y * (H - 1) for u in uvs]
    x0, x1 = int(max(0, math.floor(min(xs)) - 1)), int(min(W - 1, math.ceil(max(xs)) + 1))
    y0, y1 = int(max(0, math.floor(min(ys)) - 1)), int(min(H - 1, math.ceil(max(ys)) + 1))
    (ax, ay), (bx, by), (cx, cy) = (xs[0], ys[0]), (xs[1], ys[1]), (xs[2], ys[2])
    den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(den) < 1e-9:
        continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
    l1 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / den
    l2 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / den
    l3 = 1 - l1 - l2
    inside = (l1 >= -0.15) & (l2 >= -0.15) & (l3 >= -0.15)     # small overdraw closes UV seams
    z = l1 * corners[0].z + l2 * corners[1].z + l3 * corners[2].z
    sub = zmap[y0:y1 + 1, x0:x1 + 1]
    sub[inside & np.isnan(sub)] = z[inside & np.isnan(sub)]
    painted += 1
has = ~np.isnan(zmap)
z = np.where(has, zmap, 0)
# height ramp: brow 1.49 -> 0.86, eyes 1.45 -> 0.80, nose 1.42 -> 0.62, chin 1.37 -> 0.45, neck 1.32 -> 0.55
ramp = np.interp(z, [1.30, 1.34, 1.37, 1.42, 1.45, 1.49, 1.56], [0.55, 0.50, 0.45, 0.62, 0.80, 0.86, 0.90])
r, g, bb = px[..., 0], px[..., 1], px[..., 2]
skin = np.clip((r - bb - 0.02) / 0.10, 0, 1) * np.clip((r - 0.12) / 0.15, 0, 1)
shade = np.where(has, ramp * skin, 0).astype(np.float32)
# soft edge (box blur, two passes)
for _ in range(2):
    k = 4
    pad = np.pad(shade, k, mode='edge'); acc = np.zeros_like(shade)
    for dy in range(-k, k + 1, 2):
        for dx in range(-k, k + 1, 2):
            acc += pad[k + dy:k + dy + H, k + dx:k + dx + W]
    shade = np.maximum(shade * 0.6, acc / ((k + 1) ** 2) * 1.0)
cool = np.array([0.85, 0.9, 1.0], dtype=np.float32)
px[..., :3] = px[..., :3] * (1 - shade[..., None]) + px[..., :3] * cool * 0.15 * shade[..., None]
img.pixels[:] = px.ravel()
img.update()
# save the painted texture to disk, then pack that file (pack() alone re-packs the original GLB bytes)
img.filepath_raw = os.path.join(JOB, "T_ShadowRanger_BaseColor_painted.png"); img.file_format = 'PNG'
img.save(); img.pack()
print("A4 hood shadow faces", painted, "texels", int((shade > 0.05).sum()))

# ---------------------------------------------------------------- A6 right hand fist (rig has no finger bones)
# Meshy modelled the left hand closed round its bow grip, the right hand open (palm forward). Bend every right-hand
# vertex past the knuckle line towards the palm with a constant radius: fingers curl into a fist.
_arm_w = arm.matrix_world
wrist_r = _arm_w @ arm.data.bones["RightHand"].head_local
_hi = body.vertex_groups["RightHand"].index
_rh = [v for v in body.data.vertices if sum(g.weight for g in v.groups if g.group == _hi) > 0.3]
_tip = max((mw @ v.co for v in _rh), key=lambda p: (p - wrist_r).length)
hand_d = (_tip - wrist_r).normalized()
palm_n = Vector((0.27, -0.80, 0.54))                     # thinnest PCA axis of the hand, palm side (faces forward)
palm_n = (palm_n - hand_d * palm_n.dot(hand_d)).normalized()
side_z = hand_d.cross(palm_n)
K0, RAD, TMAX = 0.068, 0.020, 3.6                         # knuckles 6.8 cm from the wrist, curl radius 2 cm
knuckle = wrist_r + hand_d * K0
curled = 0
for v in _rh:
    p = mw @ v.co
    x = (p - knuckle).dot(hand_d)
    if x <= 0:
        continue
    y = (p - knuckle).dot(palm_n); z = (p - knuckle).dot(side_z)
    th = min(x / RAD, TMAX); extra = max(0.0, x - RAD * TMAX)
    q = knuckle + hand_d * ((RAD - y) * math.sin(th)) + palm_n * (RAD - (RAD - y) * math.cos(th)) + side_z * z
    q += (hand_d * math.cos(th) + palm_n * math.sin(th)) * extra
    v.co = mwi @ q; curled += 1
print("A6 right fist verts", curled)

# ---------------------------------------------------------------- A5 face cloth (ranger mask) skinned to Head
# BVH in world space (the rig has a 0.01 root scale, so object-space ray lengths would be 100x too short)
_wv = [mw @ v.co for v in body.data.vertices]
bvh = BVHTree.FromPolygons(_wv, [tuple(p.vertices) for p in body.data.polygons])
cols, rows = 15, 10
grid = []
for r in range(rows):
    zr = MASK_Z[0] + (MASK_Z[1] - MASK_Z[0]) * r / (rows - 1)
    row = []
    for cidx in range(cols):
        a = -0.98 + 1.96 * cidx / (cols - 1)                # wrap round the face, about +-56 deg
        centre = Vector((-0.012, -0.15, zr))
        d = Vector((math.sin(a), -math.cos(a), 0)).normalized()
        start = centre + d * 0.25
        hit = bvh.ray_cast(start, -d, 0.3)
        p = hit[0]
        row.append(p if p is not None else None)
    grid.append(row)
# below the chin the cloth hangs from the chin line (no raycast: the collar would tear it), with a small flare
chin_r = min(range(rows), key=lambda r: abs((MASK_Z[0] + (MASK_Z[1] - MASK_Z[0]) * r / (rows - 1)) - CHIN_Z))
for r in range(rows):
    zr = MASK_Z[0] + (MASK_Z[1] - MASK_Z[0]) * r / (rows - 1)
    if zr < CHIN_Z:
        for cidx in range(cols):
            src = grid[chin_r][cidx]
            if src is None:
                continue
            flare = (CHIN_Z - zr) / (CHIN_Z - MASK_Z[0]) * 0.012
            out = Vector((src.x + 0.012, src.y + 0.15, 0)).normalized()
            grid[r][cidx] = Vector((src.x, src.y, zr)) + out * flare
# fill misses from neighbours, then build the mesh
for r in range(rows):
    for cidx in range(cols):
        if grid[r][cidx] is None:
            nb = [grid[r][j] for j in range(cols) if grid[r][j] is not None]
            grid[r][cidx] = nb[len(nb) // 2] if nb else Vector((0, -0.25, MASK_Z[0]))
# cloth does not follow every bump: keep the outermost hit per column above the nose (covers the nose tip),
# smooth the surface, then push it 11 mm out so no skin pokes through
centre_axis = lambda p: Vector((p.x + 0.012, p.y + 0.15, 0))
for it in range(3):
    new = [[grid[r][c].copy() for c in range(cols)] for r in range(rows)]
    for r in range(rows):
        for c in range(cols):
            nb = [grid[rr][cc] for rr, cc in ((r - 1, c), (r + 1, c), (r, c - 1), (r, c + 1)) if 0 <= rr < rows and 0 <= cc < cols]
            avg = sum(nb, Vector()) / len(nb)
            # keep the radial distance at least the max of self / average (never sink into the face)
            rad_self, rad_avg = centre_axis(grid[r][c]).length, centre_axis(avg).length
            p = avg.copy(); p.z = grid[r][c].z
            dirv = centre_axis(p).normalized()
            p = Vector((-0.012, -0.15, p.z)) + dirv * max(rad_self, rad_avg)
            new[r][c] = p
    grid = new
# cap the radius per row (rays near the hood rim hit the hood and make spikes): at most 15 % over the row's centre
for r in range(rows):
    mid = sorted(centre_axis(grid[r][c]).length for c in range(cols // 2 - 2, cols // 2 + 3))[2]
    for c in range(cols):
        if abs(c - cols // 2) < 3:
            continue                                    # never clip the nose columns
        v = centre_axis(grid[r][c])
        if v.length > mid * 1.10:
            v = v.normalized() * mid * 1.10
            grid[r][c] = Vector((v.x - 0.012, v.y - 0.15, grid[r][c].z))
for r in range(rows):
    for c in range(cols):
        dirv = centre_axis(grid[r][c]).normalized()
        grid[r][c] = grid[r][c] + dirv * 0.011
# top edge: a soft V below the nose bridge
for cidx in range(cols):
    u = abs(cidx - (cols - 1) / 2) / ((cols - 1) / 2)
    grid[rows - 1][cidx] = grid[rows - 1][cidx] + Vector((0, 0, 0.012 * (1 - u) - 0.022 * u * u))
verts = [mwi @ p for row in grid for p in row]
faces = [(r * cols + c, r * cols + c + 1, (r + 1) * cols + c + 1, (r + 1) * cols + c)
         for r in range(rows - 1) for c in range(cols - 1)]
mm = bpy.data.meshes.new("ShadowRanger_Mask")
mm.from_pydata([tuple(v) for v in verts], [], faces)
mm.update()
mask = bpy.data.objects.new("ShadowRanger_Mask", mm)
bpy.context.scene.collection.objects.link(mask)
mask.matrix_world = mw
mask.parent = arm; mask.matrix_parent_inverse = arm.matrix_world.inverted() @ Matrix()  # keep world placement
mask.matrix_world = mw
sol = mask.modifiers.new("Solidify", 'SOLIDIFY'); sol.thickness = 0.003
mcloth = bpy.data.materials.new("M_ShadowRanger_Cloth"); mcloth.use_nodes = True
b = mcloth.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.022, 0.021, 0.022, 1); b.inputs["Roughness"].default_value = 0.85
mask.data.materials.append(mcloth)
bpy.context.view_layer.objects.active = mask
bpy.ops.object.select_all(action='DESELECT'); mask.select_set(True)
bpy.ops.object.modifier_apply(modifier="Solidify")
bpy.ops.object.shade_smooth()
gh = mask.vertex_groups.new(name="Head"); gh.add(list(range(len(mask.data.vertices))), 1.0, 'REPLACE')
mod = mask.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
print("A5 mask verts", len(mask.data.vertices))


if STAGE == "A":
    out = os.path.join(JOB, "ShadowRanger_stageA.blend")
    bpy.ops.wm.save_as_mainfile(filepath=out)
    print("SAVED", out)

# ================================================================= STAGE B: bow, string, arrow, hand-keyed clips
# Rig facts (Meshy, world space): armature root scale 0.01, no rotation; character faces -Y. Joint positions come
# from child bone heads (Meshy bone tails are not at the child joint). Arm reach shoulder->wrist ~0.45 m.
if STAGE != "A":
    sc = bpy.context.scene
    sc.render.fps = 30
    AW = arm.matrix_world.copy(); AWI = AW.inverted()
    S = AW.to_scale()[0]                         # 0.01
    up = Vector((0, 0, 1)); fwd = Vector((0, -1, 0))

    def upd():
        bpy.context.view_layer.update()

    def W(v):                                    # armature space -> world
        return AW @ v

    def A(v):                                    # world -> armature space
        return AWI @ v

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)

    # ---------------- B1 new bones: BowString (child of LeftHand), Arrow (child of Spine, rests in the quiver)
    QUIVER_TOP, QUIVER_DIR = Vector((-0.255, 0.095, 1.60)), Vector((0.13, -0.01, -0.26)).normalized()
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    bs = eb.new("BowString"); bs.parent = eb["LeftHand"]
    ar = eb.new("Arrow"); ar.parent = eb["Spine"]
    ar.head = A(QUIVER_TOP); ar.tail = A(QUIVER_TOP + QUIVER_DIR * 0.10)
    bs.head = A(Vector((0, 0, 1))); bs.tail = A(Vector((0, 0, 1.05)))     # placed properly after the aim pose
    bpy.ops.object.mode_set(mode='POSE')
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'

    PB = arm.pose.bones

    def joint(name):
        return W(PB[name].head)

    def reset_pose():
        for pb in PB:
            pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
        upd()

    def rot_world(name, R):                      # rotate a pose bone about its head by a world rotation R (3x3)
        pb = PB[name]; M = pb.matrix.copy(); h = M.translation.copy()
        pb.matrix = Matrix.Translation(h) @ R.to_4x4() @ Matrix.Translation(-h) @ M
        upd()

    def aim(name, child_now, target):            # rotate bone so (child_now - head) points at target
        h = joint(name)
        v1, v2 = (child_now - h), (target - h)
        if v1.length < 1e-6 or v2.length < 1e-6:
            return
        rot_world(name, v1.rotation_difference(v2).to_matrix())

    def two_bone(upper, lower, hand, target, pole):
        Sh, E0, W0 = joint(upper), joint(lower), joint(hand)
        L1, L2 = (E0 - Sh).length, (W0 - E0).length
        dvec = target - Sh; d = min(dvec.length, (L1 + L2) * 0.999); dirv = dvec.normalized()
        a = (L1 * L1 - L2 * L2 + d * d) / (2 * d); h = math.sqrt(max(L1 * L1 - a * a, 0.0))
        pv = pole - Sh; pv = (pv - dirv * pv.dot(dirv)).normalized()
        E = Sh + dirv * a + pv * h
        aim(upper, E0, E)
        aim(lower, joint(hand), Sh + dirv * d)

    def twist(angles):                           # torso about world Z (left shoulder forward = negative)
        for name, deg in angles:
            rot_world(name, Matrix.Rotation(math.radians(deg), 3, 'Z'))

    def tilt(name, deg, axis='X'):
        rot_world(name, Matrix.Rotation(math.radians(deg), 3, axis))

    _rh = AW.to_3x3() @ arm.data.bones["RightHand"].matrix_local.to_3x3()
    HAND_PALM_AXIS = tuple((_rh.normalized().inverted() @ Vector((0, -1, 0))).normalized())
    REACH_L = (joint("LeftForeArm") - joint("LeftArm")).length + (joint("LeftHand") - joint("LeftForeArm")).length

    # ---------------- B2 aim pose solver (shared by every Shoot frame)
    HIPS = -82                                                        # feet perpendicular to the shot line
    TORSO = [("Spine02", 13), ("Spine01", 12), ("Spine", 11)]         # chest opens back to -46 deg (half to the target)
    HEAD = [("neck", 21), ("Head", 25)]                               # face fully to the target
    CANT = 18                                                         # bow top limb leans to the archer's right

    def base_aim(breath=0.0):
        reset_pose()
        twist([("Hips", HIPS)]); twist(TORSO); twist(HEAD)
        tilt("Spine", -3 + breath)               # chest up a little, breathing
        anchor = joint("Head") + Vector((-0.045, -0.05, 0.025))       # right jaw corner under the hood
        # arrow line: from the anchor straight at the target, slightly up
        aimdir = Vector((0, -1, 0.03)).normalized()
        # bow wrist: on a line parallel to the arrow, 3 cm left / 4 cm low of it, at 97 % arm reach
        Sh = joint("LeftArm"); off = Vector((0.03, 0, -0.04))
        p0 = anchor + off; dp = p0 - Sh
        bq = 2 * dp.dot(aimdir); cq = dp.dot(dp) - (REACH_L * 0.97) ** 2
        t = (-bq + math.sqrt(max(bq * bq - 4 * cq, 0))) / 2
        wrist = p0 + aimdir * t
        return anchor, aimdir, wrist

    def pose_bow_arm(wrist, drop=0.0):
        Sh = joint("LeftArm")
        two_bone("LeftArm", "LeftForeArm", "LeftHand", wrist + Vector((0, 0, -drop)), Sh + Vector((0.35, 0.05, -0.30)))

    def pose_draw_arm(wrist, point_at=None, palm_in=False):   # palm down: the top-down game camera sees the back of the hand
        Sh = joint("RightArm")
        two_bone("RightArm", "RightForeArm", "RightHand", wrist, Sh + Vector((-0.30, 0.45, 0.10)))
        if point_at is not None:
            # fingers (hand bone Y) along the string pull line; then roll the palm to face the archer's cheek (+X)
            pb = PB["RightHand"]; upd()
            h = joint("RightHand")
            ydir = (point_at - h).normalized()
            M = pb.matrix.copy(); cur_y = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
            R = cur_y.rotation_difference(ydir).to_matrix()
            N = R @ M.to_3x3()
            best, bestN = -9, N
            for k in range(24):
                Rk = Matrix.Rotation(k * math.pi / 12, 3, ydir) @ N
                palm = (Rk @ Vector(HAND_PALM_AXIS)).normalized()     # palm normal in bone space (measured below)
                score = palm.dot(Vector((1, 0, -0.3)).normalized()) if palm_in else palm.dot(Vector((0, 0, -1)))
                if score > best:
                    best, bestN = score, Rk
            T = bestN.to_4x4(); T.translation = M.translation
            pb.matrix = T; upd()

    anchor0, aimdir0, wrist0 = base_aim()
    pose_bow_arm(wrist0)
    upd()
    draw_len = (wrist0 - anchor0).length
    print(f"B2 reach {REACH_L:.3f} draw length (wrist->anchor) {draw_len:.3f}")

    # ---------------- B3 bow + string + arrow geometry, built in the aim pose, mapped back to rest
    hand_aim = AW @ PB["LeftHand"].matrix                # world matrix of the hand in the aim pose
    hand_rest = AW @ arm.data.bones["LeftHand"].matrix_local
    to_rest = hand_rest @ hand_aim.inverted()
    palm = joint("LeftHand") + (joint("LeftHand") - joint("LeftForeArm")).normalized() * 0.045
    grip = palm + Vector((0, 0, 0.02))
    side = aimdir0.cross(up).normalized()            # bow plane normal (horizontal, across the arrow)
    back = -aimdir0                                  # towards the archer
    HALF, BRACE = 0.62, 0.155

    def limb_point(u):                               # u in [-1, 1], bottom -> top
        a = abs(u)
        y = BRACE * 0.95 * a ** 1.6 - (0.07 * ((a - 0.82) / 0.18) ** 2 if a > 0.82 else 0)
        return grip + up * (HALF * u) + back * y

    tips = [limb_point(-1.0), limb_point(1.0)]

    def curve_obj(name, pts, radii, bevel=1.0):
        cd = bpy.data.curves.new(name, 'CURVE'); cd.dimensions = '3D'; cd.bevel_depth = bevel; cd.bevel_resolution = 2
        cd.resolution_u = 6; cd.use_fill_caps = True
        sp = cd.splines.new('POLY'); sp.points.add(len(pts) - 1)
        for p, (co, r) in zip(sp.points, zip(pts, radii)):
            p.co = (*co, 1); p.radius = r
        o = bpy.data.objects.new(name, cd); sc.collection.objects.link(o)
        return o

    def to_mesh(o):
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.convert(target='MESH')
        return bpy.context.view_layer.objects.active

    def material(name, rgb, rough, metal=0.0):
        m = bpy.data.materials.new(name); m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*rgb, 1); b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metal
        return m

    M_WOOD = material("M_ShadowRanger_Bow", (0.030, 0.026, 0.024), 0.38, 0.1)
    M_GRIP = material("M_ShadowRanger_Grip", (0.11, 0.065, 0.035), 0.7)
    M_STRING = material("M_ShadowRanger_String", (0.55, 0.52, 0.45), 0.6)
    M_SHAFT = material("M_ShadowRanger_Arrow", (0.06, 0.05, 0.045), 0.5)
    M_STEEL = material("M_ShadowRanger_Steel", (0.35, 0.36, 0.38), 0.3, 0.9)

    us = [i / 24 * 2 - 1 for i in range(25)]
    radius = lambda u: 0.011 if abs(u) < 0.1 else 0.0105 - 0.0055 * (abs(u) - 0.1) / 0.9
    limbs = curve_obj("BowLimbs", [limb_point(u) for u in us], [radius(u) for u in us])
    limbs = to_mesh(limbs); limbs.data.materials.append(M_WOOD)
    # leather grip wrap
    gw = curve_obj("BowGrip", [grip + up * -0.06, grip + up * 0.06], [0.0145, 0.0145]); gw = to_mesh(gw); gw.data.materials.append(M_GRIP)
    # string nocks: small knobs at the tips
    knobs = []
    for t in tips:
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.008, segments=8, ring_count=5, location=t)
        k = bpy.context.active_object; k.data.materials.append(M_WOOD); knobs.append(k)
    bpy.ops.object.select_all(action='DESELECT')
    for o in [limbs, gw] + knobs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = limbs
    bpy.ops.object.join()
    bow = bpy.context.view_layer.objects.active; bow.name = "ShadowRanger_Bow"
    # map aim-pose world -> rest world, then skin 100 % to LeftHand
    for v in bow.data.vertices:
        v.co = to_rest @ (bow.matrix_world @ v.co)
    bow.matrix_world = Matrix()
    bow.parent = arm; bow.matrix_parent_inverse = arm.matrix_world.inverted()
    bow.vertex_groups.new(name="LeftHand").add(list(range(len(bow.data.vertices))), 1.0, 'REPLACE')
    bow.modifiers.new("Armature", 'ARMATURE').object = arm
    bpy.ops.object.shade_smooth()

    # BowString bone rest = brace point between the tips (rest pose)
    tips_rest = [to_rest @ t for t in tips]
    # bow "up" (bottom tip -> top tip) in LeftHand bone space, for posing the hand by the bow
    BOW_UP_BONE = (hand_rest.to_3x3().normalized().inverted() @ (tips_rest[1] - tips_rest[0]).normalized()).normalized()
    brace_rest = to_rest @ (grip + back * BRACE)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    e = arm.data.edit_bones["BowString"]
    e.head = A(brace_rest); e.tail = A(brace_rest + (tips_rest[1] - brace_rest).normalized() * 0.05)
    bpy.ops.object.mode_set(mode='OBJECT')
    # string: two thin tubes tip -> brace point; tip rings on LeftHand, middle ring on BowString
    st = curve_obj("BowStringMesh", [tips_rest[1], brace_rest, tips_rest[0]], [1, 1, 1], bevel=0.0016)
    st.data.bevel_resolution = 1
    st = to_mesh(st); st.name = "ShadowRanger_String"; st.data.materials.append(M_STRING)
    gL = st.vertex_groups.new(name="LeftHand"); gS = st.vertex_groups.new(name="BowString")
    for v in st.data.vertices:
        p = v.co
        dt = min((p - t).length for t in tips_rest); dm = (p - brace_rest).length
        w = max(0.0, min(1.0, dt / (dt + dm)))
        gL.add([v.index], 1 - w, 'REPLACE'); gS.add([v.index], w, 'REPLACE')
    st.parent = arm; st.matrix_parent_inverse = arm.matrix_world.inverted()
    st.modifiers.new("Armature", 'ARMATURE').object = arm

    # arrow: built along the Arrow bone (rest in the quiver), nock at the bone head
    ab = arm.data.bones["Arrow"]
    a0 = AW @ ab.head_local; ad = ((AW @ ab.tail_local) - a0).normalized()
    ALEN = 0.62
    shaft = curve_obj("ArrowShaft", [a0, a0 + ad * ALEN], [0.0042, 0.0042]); shaft = to_mesh(shaft); shaft.data.materials.append(M_SHAFT)
    bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.011, radius2=0.0, depth=0.05, location=a0 + ad * (ALEN + 0.022))
    head = bpy.context.active_object
    head.rotation_euler = Vector((0, 0, 1)).rotation_difference(ad).to_euler(); head.data.materials.append(M_STEEL)
    flet = []
    sideA = ad.orthogonal().normalized()
    for k in range(3):
        R = Matrix.Rotation(k * 2 * math.pi / 3, 3, ad); n = R @ sideA
        vs = [a0 + ad * 0.02, a0 + ad * 0.11, a0 + ad * 0.04 + n * 0.018]
        m = bpy.data.meshes.new("fl"); m.from_pydata([tuple(x) for x in vs], [], [(0, 1, 2)]); m.update()
        fo = bpy.data.objects.new("Fletch", m); sc.collection.objects.link(fo); fo.data.materials.append(M_WOOD); flet.append(fo)
    bpy.ops.object.select_all(action='DESELECT')
    for o in [shaft, head] + flet:
        o.select_set(True)
    bpy.context.view_layer.objects.active = shaft
    bpy.ops.object.join(); arrow = bpy.context.view_layer.objects.active; arrow.name = "ShadowRanger_Arrow"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    sol = arrow.modifiers.new("Solidify", 'SOLIDIFY'); sol.thickness = 0.0015
    bpy.ops.object.modifier_apply(modifier="Solidify")
    arrow.parent = arm; arrow.matrix_parent_inverse = arm.matrix_world.inverted()
    arrow.vertex_groups.new(name="Arrow").add(list(range(len(arrow.data.vertices))), 1.0, 'REPLACE')
    arrow.modifiers.new("Armature", 'ARMATURE').object = arm
    print("B3 bow", len(bow.data.polygons), "string", len(st.data.polygons), "arrow", len(arrow.data.polygons))

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    for pb in PB:
        pb.rotation_mode = 'QUATERNION'

    # ---------------- B4 Shoot clip (30 fps, 30 frames, loops; release on frame 19 = 0.63 s)
    def place_bone_world(name, head_w, ydir_w):     # put a pose bone's head at head_w with its Y axis along ydir_w
        pb = PB[name]; upd()
        M = pb.matrix.copy()
        cur_y = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
        R = cur_y.rotation_difference(ydir_w.normalized()).to_matrix()
        N = (R @ M.to_3x3()).to_4x4(); N.translation = A(head_w)
        pb.matrix = N; upd()

    def key_all(frame, act_bones=None):
        for pb in PB:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("location", frame=frame)

    def smooth(t):
        return t * t * (3 - 2 * t)

    def shoot_pose(f):
        breath = 0.6 * math.sin(f / 30 * 2 * math.pi)
        anchor, aimdir, wrist = base_aim(breath)
        grip_w = None
        brace_pt = None
        # draw amount 0 (brace) .. 1 (anchor); release at 19, follow-through 19..23, back to brace 23..30
        if f <= 4:
            draw = 0.0
        elif f <= 14:
            draw = smooth((f - 4) / 10)
        elif f <= 19:
            draw = 1.0 + 0.03 * smooth((f - 14) / 5)             # last few mm of expansion before the release
        else:
            draw = None
        drop = 0.0
        if 19 < f <= 25:
            drop = 0.035 * math.sin((f - 19) / 6 * math.pi)       # bow arm reacts to the shot
        pose_bow_arm(wrist, drop)
        rot_world("LeftHand", Matrix.Rotation(math.radians(CANT), 3, aimdir))
        upd()
        hand_now = AW @ PB["LeftHand"].matrix
        grip_now = (hand_now @ hand_rest.inverted()) @ (to_rest @ grip)
        brace_now = (hand_now @ hand_rest.inverted()) @ brace_rest
        pinch_brace = brace_now
        pinch_anchor = anchor
        if draw is not None:
            pinch = pinch_brace.lerp(pinch_anchor, min(draw, 1.0)) + aimdir * -0.02 * max(draw - 1.0, 0) / 0.03
            hand_w = pinch + Vector((-0.02, 0.055, -0.01))
            string_w = pinch
            point_at = pinch + aimdir * 0.10
            arrow_on = True
        else:
            k = (f - 19) / 11.0
            # follow-through: hand flies back past the ear, then returns to the string
            back_pt = anchor + Vector((-0.06, 0.12, 0.03))
            if k < 0.35:
                hand_w = (anchor + Vector((-0.02, 0.055, -0.01))).lerp(back_pt, smooth(k / 0.35))
            else:
                hand_w = back_pt.lerp(pinch_brace + Vector((-0.02, 0.055, -0.01)), smooth((k - 0.35) / 0.65))
            string_w = None
            point_at = hand_w + aimdir * 0.10 + Vector((0, 0, -0.03))
            arrow_on = f >= 27                                    # next arrow nocked again at the end of the loop
        pose_draw_arm(hand_w, point_at)
        if string_w is not None:
            place_bone_world("BowString", string_w, (AW.to_3x3() @ (arm.data.bones["BowString"].matrix_local.to_3x3() @ Vector((0, 1, 0)))).normalized())
        if arrow_on:
            nock = string_w if string_w is not None else pinch_brace
            place_bone_world("Arrow", nock, (grip_now - nock).normalized())
        upd()

    act = bpy.data.actions.new("Shoot"); arm.animation_data_create(); arm.animation_data.action = act
    for f in range(0, 31):
        shoot_pose(f % 30 if f == 30 else f)
        key_all(f)
    # string snaps back and the arrow vanishes into the quiver on the release frame: constant keys there
    for fc in (act.fcurves if hasattr(act, "fcurves") else []):
        for kp in fc.keyframe_points:
            if int(kp.co.x) in (19, 20):
                kp.interpolation = 'CONSTANT'
    act.use_fake_user = True
    print("B4 Shoot keyed")

    # ---------------- B5 Idle (60 frames): bow low in the left hand, relaxed, breathing
    act2 = bpy.data.actions.new("Idle"); arm.animation_data.action = act2
    for f in range(0, 61):
        b = math.sin(f / 60 * 2 * math.pi)
        reset_pose()
        twist([("Spine02", -4), ("Spine01", -5), ("Spine", -5)]); twist([("neck", 6), ("Head", 7)])
        tilt("Spine", -1.2 * b); tilt("Spine01", -0.6 * b); tilt("Head", 0.8 * b)
        Sh = joint("LeftArm")
        two_bone("LeftArm", "LeftForeArm", "LeftHand", Sh + Vector((0.12, -0.18, -0.34 + 0.004 * b)), Sh + Vector((0.35, 0.25, -0.1)))
        # carry the bow upright, top limb a little forward
        hw = (AW @ PB["LeftHand"].matrix).to_3x3().normalized()
        cur_up = (hw @ BOW_UP_BONE).normalized()
        rot_world("LeftHand", cur_up.rotation_difference(Vector((0.05, -0.35, 1)).normalized()).to_matrix())
        Sr = joint("RightArm")
        two_bone("RightArm", "RightForeArm", "RightHand", Sr + Vector((-0.10, -0.06, -0.40 + 0.004 * b)), Sr + Vector((-0.35, 0.3, -0.1)))
        key_all(f)
    act2.use_fake_user = True
    print("B5 Idle keyed")

    # ---------------- B6 bring in the Meshy Walking / Running clips for the work file
    for clip in ("Walking", "Running"):
        before = set(bpy.data.actions); keep = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=os.path.join(JOB, f"meshy_anim_{clip}.glb"))
        for o in [o for o in bpy.data.objects if o not in keep]:
            bpy.data.objects.remove(o, do_unlink=True)
        new = [a for a in bpy.data.actions if a not in before]
        if new:
            new[0].name = clip; new[0].use_fake_user = True
            for a in new[1:]:
                bpy.data.actions.remove(a)
            # the Meshy clip has no channels for Arrow / BowString: key them at rest so no player keeps a stale pose
            ad = arm.animation_data; ad.action = new[0]
            if hasattr(ad, "action_slot") and new[0].slots:
                ad.action_slot = new[0].slots[0]
            f0, f1 = (int(x) for x in new[0].frame_range)
            for nm in ("Arrow", "BowString"):
                pb = PB[nm]; pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
                for fr in (f0, f1):
                    pb.keyframe_insert("rotation_quaternion", frame=fr); pb.keyframe_insert("location", frame=fr)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    reset_pose()
    arm.animation_data.action = bpy.data.actions["Shoot"]
    bpy.ops.object.mode_set(mode='OBJECT')
    sc.frame_start, sc.frame_end = 0, 30

    blend = os.path.join(JOB, "ShadowRanger.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    print("SAVED", blend)

    # ---------------- B7 exports for the Unity / web pipeline
    if "--export" in ARGS:
        meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm]
        arm.animation_data.action = None
        reset_pose()
        bpy.ops.object.select_all(action='DESELECT')
        for o in [arm] + meshes:
            o.select_set(True)
        bpy.ops.export_scene.gltf(filepath=os.path.join(JOB, "rigged.glb"), export_format='GLB', use_selection=True,
                                  export_animations=False, export_skins=True)
        for clip in ("Shoot", "Idle", "Walking", "Running"):
            arm.animation_data.action = bpy.data.actions[clip]
            if hasattr(arm.animation_data, "action_slot") and bpy.data.actions[clip].slots:
                arm.animation_data.action_slot = bpy.data.actions[clip].slots[0]
            bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
            bpy.ops.export_scene.gltf(filepath=os.path.join(JOB, f"anim_{clip}.glb"), export_format='GLB', use_selection=True,
                                      export_animations=True, export_animation_mode='ACTIVE_ACTIONS', export_skins=True,
                                      export_force_sampling=True)
        print("EXPORTED")
