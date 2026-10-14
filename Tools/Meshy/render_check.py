"""blender -b --python render_check.py -- <project_dir> <out_png_prefix>
Renders rest pose + a mid frame of every anim_*.glb (mesh + rig) with workbench texture colour."""
import bpy, sys, os, glob, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
proj, prefix = argv
os.makedirs(os.path.dirname(prefix), exist_ok=True)

def setup(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.parent != arm]:
        bpy.data.objects.remove(o, do_unlink=True)
    for m in bpy.data.materials:  # workbench shows the active image node: make it the base colour
        if m.use_nodes:
            b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if b and b.inputs['Base Color'].links:
                m.node_tree.nodes.active = b.inputs['Base Color'].links[0].from_node
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x, sc.render.resolution_y = 360, 480
    sc.render.film_transparent = False
    w = bpy.data.worlds.new("w"); sc.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam); sc.camera = cam
    return arm, sc, cam

def frame_cam(sc, cam):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in bpy.data.objects:
        if o.type == 'MESH':
            m = o.evaluated_get(dg).to_mesh()
            pts += [o.matrix_world @ v.co for v in list(m.vertices)[::7]]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (lo + hi) / 2; size = max(hi - lo) * 1.15
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = size
    d = Vector((0.55, -1, 0.35)).normalized()  # glTF front faces -Y in Blender
    cam.location = c + d * size * 3
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()

shots = []
for path in [os.path.join(proj, "rigged.glb")] + sorted(glob.glob(os.path.join(proj, "anim_*.glb"))):
    clip = "Rest" if path.endswith("rigged.glb") else os.path.basename(path)[5:-4]
    arm, sc, cam = setup(path)
    if clip != "Rest" and arm.animation_data and arm.animation_data.action:
        f0, f1 = arm.animation_data.action.frame_range
        sc.frame_set(int(f0 + (f1 - f0) * 0.45))
    frame_cam(sc, cam)
    out = f"{prefix}_{clip}.png"
    sc.render.filepath = out
    bpy.ops.render.render(write_still=True)
    shots.append(out)
print("SHOTS", shots)
