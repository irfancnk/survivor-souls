"""Blender headless: count bow / string vertices inside the body for every frame of a clip.
blender -b ShadowRanger.blend --python check_ranger_bow.py -- <Action>"""
import bpy, sys
from mathutils.bvhtree import BVHTree
act = sys.argv[sys.argv.index("--") + 1]
arm = bpy.data.objects['ShadowRanger_Rig']; body = bpy.data.objects['ShadowRanger_Body']
bow = bpy.data.objects['ShadowRanger_Bow']
ad = arm.animation_data; ad.action = bpy.data.actions[act]
if hasattr(ad, "action_slot") and ad.action.slots: ad.action_slot = ad.action.slots[0]
f0, f1 = (int(x) for x in ad.action.frame_range)
worst = 0
gname = {g.index: g.name for g in body.vertex_groups}
def part(face_idx, m):
    from collections import Counter
    c = Counter()
    for vi in m.polygons[face_idx].vertices:
        for g in body.data.vertices[vi].groups:
            c[gname[g.group]] += g.weight
    return c.most_common(1)[0][0] if c else "?"
for f in range(f0, f1 + 1):
    bpy.context.scene.frame_set(f); dg = bpy.context.evaluated_depsgraph_get()
    eb = body.evaluated_get(dg); m = eb.to_mesh()
    P = [body.matrix_world @ v.co for v in m.vertices]
    bvh = BVHTree.FromPolygons(P, [tuple(p.vertices) for p in m.polygons])
    ob = bow.evaluated_get(dg); bm = ob.to_mesh()
    n = 0; deep = 0.0; where = None
    hand = arm.matrix_world @ arm.pose.bones["LeftHand"].head
    for v in bm.vertices:
        p = bow.matrix_world @ v.co
        if (p - hand).length < 0.10:
            continue                                # the grip sits inside the closed fist on purpose
        loc, nrm, idx, d = bvh.find_nearest(p, 0.05)
        if loc is not None and (p - loc).dot(nrm) < -0.004:
            n += 1
            if d > deep:
                deep = d; where = (p - hand).length; hitpart = part(idx, m)
    eb.to_mesh_clear(); ob.to_mesh_clear()
    worst = max(worst, n)
    if n: print(f"BOW {act} f{f:02d} {n:3d} verts in body ({deep*100:.1f} cm deep, {where*100:.0f} cm from the grip, into {hitpart})")
print(f"BOW {act} worst {worst}")
