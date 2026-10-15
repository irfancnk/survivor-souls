"""
Export the black fireball of the Butcher of Ashgrave (KAN-174) as a mobile mesh.

Source: ~/survivor-souls-assets/Fantasy Black Fireball.blend (comet mesh "Fireball" with Displace x2 + Wave + Solidify,
procedural Eevee shaders, a lightning curve, two particle systems: sparks + speed lines). Unity cannot use the procedural
shaders, the curve or the Blender particles, so only the comet mesh ships; the look is rebuilt in Unity
(BlackFireball.shader on mesh particles + a spark trail).

Run headless from the repo root:
    blender -b "<assets>/Fantasy Black Fireball.blend" -P Tools/Blender/export_black_fireball.py -- --out Assets/_Project/Art/Boss/SM_BlackFireball.fbx

Steps:
  1. Evaluate "Fireball" at frame 120 with the two Displace modifiers and the Wave (Solidify off: the inner shell is a
     second transparent material; one shell is enough on a phone).
  2. Decimate to about --tris triangles, triangulate, smooth normals.
  3. Pivot = centre of the round head; the head points along Blender -Y (= Unity +Z, the flight direction); the head is
     1 m wide (the game scales it per shot).
  4. UV0 (the shader's gradient + flame noise input): u = angle round the long axis (0..1), v = 0 at the head tip ..
     1 at the tail tip.
  5. Export FBX (Y up, -Z forward, transforms applied, no materials, no animation).
"""
import argparse
import math
import sys

import bmesh
import bpy
from mathutils import Vector

FRAME = 120


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--tris", type=int, default=640)
    return p.parse_args(argv)


def build(tris_budget):
    scene = bpy.context.scene
    scene.frame_set(FRAME)
    src = bpy.data.objects["Fireball"]
    for m in src.modifiers:
        if m.type == "SOLIDIFY":
            m.show_viewport = False
            m.show_render = False
    deps = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(src.evaluated_get(deps), depsgraph=deps)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.transform(bm, matrix=src.matrix_world, verts=bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new("SM_BlackFireball", mesh)
    scene.collection.objects.link(obj)
    tris = len(mesh.polygons)
    if tris > tris_budget:
        dec = obj.modifiers.new("Decimate", "DECIMATE")
        dec.ratio = tris_budget / tris
        dec.use_collapse_triangulate = True
        deps = bpy.context.evaluated_depsgraph_get()
        low = bpy.data.meshes.new_from_object(obj.evaluated_get(deps), depsgraph=deps)
        obj.modifiers.clear()
        obj.data = low
        mesh = low

    # Long axis = Blender Z (head at the bottom). Head tip, head centre, width.
    zs = [v.co.z for v in mesh.vertices]
    zmin, zmax = min(zs), max(zs)
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    cx, cy = (min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5
    width = max(max(xs) - min(xs), max(ys) - min(ys))
    head = Vector((cx, cy, zmin + width * 0.5))
    scale = 1.0 / width

    bm = bmesh.new()
    bm.from_mesh(mesh)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for loop in f.loops:
            p = loop.vert.co
            ang = math.atan2(p.y - cy, p.x - cx) / (2.0 * math.pi) + 0.5
            loop[uv].uv = (ang, (p.z - zmin) / (zmax - zmin))
    for v in bm.verts:
        p = (v.co - head) * scale
        v.co = Vector((p.x, p.z, -p.y))  # head (-Z) -> Blender -Y = Unity +Z
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    for slot_mesh in [mesh]:
        slot_mesh.materials.clear()
    obj.location = (0.0, 0.0, 0.0)
    return obj, len(mesh.polygons), (zmax - zmin) * scale


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE",
        use_tspace=False, add_leaf_bones=False, path_mode="STRIP", embed_textures=False,
        bake_anim=False)


def main(out, tris):
    obj, count, length = build(tris)
    export(obj, out)
    return {"out": out, "tris": count, "length_m": round(length, 3)}


if __name__ == "__main__":
    a = args()
    print("[export_black_fireball]", main(a.out, a.tris))
