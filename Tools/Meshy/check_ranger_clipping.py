"""Blender headless: count arm vertices inside the head / hood / cowl for every frame of a clip.
blender -b ShadowRanger.blend --python check_ranger_clipping.py -- <Action> [frames]
A vertex is 'inside' when its nearest head-surface point is < 6 cm away and it lies behind that surface's normal."""
import bpy, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree
args = sys.argv[sys.argv.index("--") + 1:]
act = args[0]
arm = bpy.data.objects['ShadowRanger_Rig']; body = bpy.data.objects['ShadowRanger_Body']; mask = bpy.data.objects['ShadowRanger_Mask']
ad = arm.animation_data; ad.action = bpy.data.actions[act]
if hasattr(ad, "action_slot") and ad.action.slots: ad.action_slot = ad.action.slots[0]
f0, f1 = (int(x) for x in ad.action.frame_range)
frames = [int(x) for x in args[1].split(",")] if len(args) > 1 else list(range(f0, f1 + 1))
gi = {g.name: g.index for g in body.vertex_groups}
def wsum(v, names): return sum(g.weight for g in v.groups if g.group in [gi[n] for n in names if n in gi])
HEAD = ("Head", "neck", "head_end", "headfront")
head_v = {v.index for v in body.data.vertices if wsum(v, HEAD) > 0.5}
head_f = [p.index for p in body.data.polygons if all(i in head_v for i in p.vertices)]
ARMS = {"RH": ("RightHand",), "RF": ("RightForeArm",), "RA": ("RightArm",), "L": ("LeftArm", "LeftForeArm", "LeftHand")}
arm_v = {k: [v.index for v in body.data.vertices if wsum(v, n) > 0.5] for k, n in ARMS.items()}
dg = bpy.context.evaluated_depsgraph_get()
worst = 0
for f in frames:
    bpy.context.scene.frame_set(f); dg = bpy.context.evaluated_depsgraph_get()
    eb = body.evaluated_get(dg); m = eb.to_mesh(); mw = body.matrix_world
    P = [mw @ v.co for v in m.vertices]
    tris = [tuple(m.polygons[i].vertices) for i in head_f]
    em = mask.evaluated_get(dg); mm = em.to_mesh(); mmw = mask.matrix_world
    off = len(P); P2 = P + [mmw @ v.co for v in mm.vertices]
    tris += [tuple(off + i for i in p.vertices) for p in mm.polygons]
    bvh = BVHTree.FromPolygons(P2, tris)
    out = []
    for k, ids in arm_v.items():
        n = 0; deepest = 0.0
        for i in ids:
            loc, nrm, idx, d = bvh.find_nearest(P[i], 0.06)
            if loc is not None and (P[i] - loc).dot(nrm) < -0.003:
                n += 1; deepest = max(deepest, d)
        out.append(f"{k} {n:3d} ({deepest*100:.1f})")
        worst = max(worst, n)
    eb.to_mesh_clear(); em.to_mesh_clear()
    print(f"CLIP {act} f{f:02d}  " + "  ".join(out))
print("CLIP worst", worst)
