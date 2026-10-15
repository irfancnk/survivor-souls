"""Blender headless: Shadow Ranger clean-up after the Meshy auto-rig.
1. Bow: Meshy fused a bow into the left hand. After the 20k remesh it is a strip of long thin triangles; the rig
   spreads it over LeftArm / LeftForeArm, so it bends and tears. Cut those triangles out and build a new black recurve
   bow (+ string) on the same arc, skinned 100 % to LeftHand, so it stays rigid in every clip.
2. Quiver: the arrows on the back follow the right shoulder / arm and swing out. Quiver vertices -> 100 % Spine.
blender -b --python fix_ranger_weights.py -- <rigged.glb in> <rigged.glb out>
Positions are world space of the Meshy rig rest pose (glTF import, front = -Y), measured from vertex plots."""
import bpy, bmesh, sys
from mathutils import Vector
from mathutils.geometry import intersect_point_line

src, dst = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.parent != arm]:
    bpy.data.objects.remove(o, do_unlink=True)
me = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm][0]
mw = me.matrix_world

# bow centre line, top tip -> grip (left hand) -> bottom tip
BOW = [Vector(p) for p in [(0.10, -0.20, 1.79), (0.27, -0.33, 1.60), (0.43, -0.42, 1.40),
                           (0.40, -0.40, 1.22), (0.37, -0.33, 1.05), (0.25, -0.21, 0.86)]]
GRIP = BOW[2]
CUT_R, HAND_R = 0.055, 0.09


def dist_to_polyline(p, pts):
    best = 9.0
    for a, b in zip(pts, pts[1:]):
        _, t = intersect_point_line(p, a, b)
        t = max(0.0, min(1.0, t))
        best = min(best, (p - (a + (b - a) * t)).length)
    return best


def on_bow(p):
    return dist_to_polyline(p, BOW) < CUT_R and (p - GRIP).length > HAND_R


def in_quiver(p):
    return -0.30 < p.x < -0.08 and 0.055 < p.y < 0.13 and 1.36 < p.z < 1.60  # thin bar on the back, above the cloak


# 1. cut the fused bow (faces with 2+ corners on the bow arc, outside the hand)
bm = bmesh.new(); bm.from_mesh(me.data)
dead = [f for f in bm.faces if sum(on_bow(mw @ v.co) for v in f.verts) >= 2]  # slivers: 2 of 3 corners on the arc
bmesh.ops.delete(bm, geom=dead, context='FACES')
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context='VERTS')
bm.to_mesh(me.data); bm.free()

# 2. quiver rigid on the upper spine
spine = me.vertex_groups["Spine"]
quiver_n = 0
for v in me.data.vertices:
    if in_quiver(mw @ v.co):
        for g in list(v.groups):
            me.vertex_groups[g.group].remove([v.index])
        spine.add([v.index], 1.0, 'REPLACE'); quiver_n += 1

# 3. new recurve bow on the same arc (world space), then skinned to LeftHand
cd = bpy.data.curves.new("BowLimbs", 'CURVE'); cd.dimensions = '3D'; cd.bevel_depth = 1.0; cd.bevel_resolution = 2
cd.resolution_u = 8
sp = cd.splines.new('BEZIER'); sp.bezier_points.add(len(BOW) - 1)
radii = [0.004, 0.009, 0.016, 0.010, 0.008, 0.004]  # thick grip, thin tips
for bp, p, r in zip(sp.bezier_points, BOW, radii):
    bp.co = p; bp.handle_left_type = bp.handle_right_type = 'AUTO'; bp.radius = r
limbs = bpy.data.objects.new("Bow", cd); bpy.context.scene.collection.objects.link(limbs)
# recurve tips: short hooks bent away from the archer (towards -Y, the shooting side)
for tip, nb in ((BOW[0], BOW[1]), (BOW[-1], BOW[-2])):
    d = (tip - nb).normalized()
    hook = bpy.data.curves.new("Tip", 'CURVE'); hook.dimensions = '3D'; hook.bevel_depth = 1.0; hook.bevel_resolution = 1
    hs = hook.splines.new('BEZIER'); hs.bezier_points.add(1)
    hs.bezier_points[0].co = tip; hs.bezier_points[1].co = tip + d * 0.05 + Vector((0, -0.04, 0))
    for bp in hs.bezier_points:
        bp.handle_left_type = bp.handle_right_type = 'AUTO'; bp.radius = 0.004
    o = bpy.data.objects.new("BowTip", hook); bpy.context.scene.collection.objects.link(o)
string = bpy.data.curves.new("BowString", 'CURVE'); string.dimensions = '3D'; string.bevel_depth = 0.0015
ss = string.splines.new('POLY'); ss.points.add(1)
ss.points[0].co = (*BOW[0], 1); ss.points[1].co = (*BOW[-1], 1)
so = bpy.data.objects.new("BowString", string); bpy.context.scene.collection.objects.link(so)

bpy.ops.object.select_all(action='DESELECT')
parts = [o for o in bpy.data.objects if o.name.startswith(("Bow", "BowTip", "BowString"))]
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = limbs
bpy.ops.object.convert(target='MESH')
bpy.ops.object.join()
bow = bpy.context.view_layer.objects.active
bow.name = "Bow"
mat = bpy.data.materials.new("M_Bow"); mat.use_nodes = True
b = mat.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.035, 0.03, 0.028, 1); b.inputs["Roughness"].default_value = 0.45
b.inputs["Metallic"].default_value = 0.2
bow.data.materials.clear(); bow.data.materials.append(mat)
# skin: parent to the armature with one group, LeftHand = 1.0
bow.parent = arm; bow.matrix_parent_inverse = arm.matrix_world.inverted()
g = bow.vertex_groups.new(name="LeftHand"); g.add(list(range(len(bow.data.vertices))), 1.0, 'REPLACE')
mod = bow.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
print("FIXED cut", len(dead), "faces; quiver", quiver_n, "verts; bow", len(bow.data.polygons), "faces")

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', export_animations=False, export_skins=True)
