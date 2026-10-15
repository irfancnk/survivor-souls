"""XP soul crystal (KAN-177): soft, rounded gold gem dropped by every kill; game mesh + preview renders.

Run (headless, ~30 s):
    blender -b --python Tools/Blender/build_xp_crystal.py -- [--fbx FBX] [--blend BLEND] [--preview PNG] [--size PX]

Defaults:
    --fbx     Assets/_Project/Art/Progression/SM_XpCrystal.fbx   (game mesh, Y up, height 1, no materials)
    --blend   ArtSource/Progression/XpCrystal/XpCrystal.blend     (source scene: mesh, preview material, lights, camera)
    --preview Docs/screenshots/kan177-xp-crystal-blender.png      (hero render + 6-angle turntable strip)
    --size    512

Model: a six-sided soul crystal. Tall upper point, short girdle band, shorter lower point (reads as a gem from the
55 deg gameplay camera, not as a diamond or a ball). Every edge is bevelled with 3 segments and shaded by angle, so
the facets stay flat (sharp glints when it spins) and the edges are soft and rounded (the "soft XP" look). Under
400 triangles. Height 1 (the particle size in the game is the crystal height), widest part about 0.5.

UV0 carries data for the SurvivorSouls/XpCrystal shader, not a texture layout:
    u = height 0 (bottom tip) .. 1 (top tip)   -> inner core glow, brighter in the girdle, fades to the tips
    v = facet id 0..1 (same value on one flat facet, from its normal)   -> small per-facet brightness change
"""

import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

SIDES = 6
RADIUS = 0.25           # girdle radius before the height is scaled to 1
TOP = 0.58              # top tip above the girdle top
GIRDLE = 0.13           # girdle band height
BOTTOM = 0.38           # bottom tip below the girdle bottom
TWIST = math.radians(30)  # lower ring of the girdle turned by half a facet: diamond facets, not plain rectangles
BEVEL = 0.018
BEVEL_SEGMENTS = 2
SMOOTH_ANGLE = math.radians(32)
MAX_TRIS = 400


def arg(name, default):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return argv[argv.index(name) + 1] if name in argv else default


def ring(z, twist):
    return [(RADIUS * math.cos(twist + i * 2 * math.pi / SIDES), RADIUS * math.sin(twist + i * 2 * math.pi / SIDES), z)
            for i in range(SIDES)]


def build_mesh():
    """Crystal centred on its height: z from -0.5 to +0.5 after the shift."""
    total = TOP + GIRDLE + BOTTOM
    z0 = -total / 2
    bottom_tip = (0.0, 0.0, z0)
    low = ring(z0 + BOTTOM, TWIST)
    high = ring(z0 + BOTTOM + GIRDLE, 0.0)
    top_tip = (0.0, 0.0, z0 + total)

    bm = bmesh.new()
    vb = bm.verts.new(bottom_tip)
    vl = [bm.verts.new(p) for p in low]
    vh = [bm.verts.new(p) for p in high]
    vt = bm.verts.new(top_tip)
    for i in range(SIDES):
        j = (i + 1) % SIDES
        bm.faces.new((vb, vl[j], vl[i]))          # lower point
        bm.faces.new((vl[i], vl[j], vh[j]))       # girdle: two triangles per side, twisted rings
        bm.faces.new((vl[i], vh[j], vh[i]))
        bm.faces.new((vh[i], vh[j], vt))          # upper point
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new("SM_XpCrystal")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("SM_XpCrystal", mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = BEVEL
    mod.segments = BEVEL_SEGMENTS
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(20)
    mod.profile = 0.5
    bpy.ops.object.modifier_apply(modifier="Bevel")
    tri = obj.modifiers.new("Tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier="Tri")

    mesh = obj.data
    zs = [v.co.z for v in mesh.vertices]
    scale = 1.0 / (max(zs) - min(zs))  # the bevel rounds the tips off: height back to exactly 1
    mid = (max(zs) + min(zs)) / 2
    for v in mesh.vertices:
        v.co = Vector((v.co.x * scale, v.co.y * scale, (v.co.z - mid) * scale))
    z0, total = -0.5, 1.0
    for p in mesh.polygons:
        p.use_smooth = True
    mesh.set_sharp_from_angle(angle=SMOOTH_ANGLE)  # flat facets, rounded bevels
    write_data_uv(mesh, z0, total)
    return obj


def facet_id(normal):
    """Same value for every triangle of one flat facet: hash of the rounded normal."""
    q = tuple(int(round(c * 6)) for c in normal)
    h = (q[0] * 73856093) ^ (q[1] * 19349663) ^ (q[2] * 83492791)
    return (h & 0xFFFF) / 65535.0


def write_data_uv(mesh, z0, total):
    uv = mesh.uv_layers.new(name="Data")
    for poly in mesh.polygons:
        fid = facet_id(poly.normal)
        for li in poly.loop_indices:
            z = mesh.vertices[mesh.loops[li].vertex_index].co.z
            uv.data[li].uv = ((z - z0) / total, fid)


def preview_material():
    """Blender preview only (the game uses the XpCrystal shader): warm gold gem, glassy, glowing core."""
    mat = bpy.data.materials.new("M_XpCrystal_Preview")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (1.0, 0.52, 0.08, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.55
    bsdf.inputs["Roughness"].default_value = 0.16
    bsdf.inputs["Coat Weight"].default_value = 0.35
    bsdf.inputs["Coat Roughness"].default_value = 0.04
    bsdf.inputs["Emission Color"].default_value = (1.0, 0.55, 0.10, 1.0)
    # Core glow: brighter where the surface faces the camera (the gem looks lit from inside), via layer weight.
    lw = nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.45
    ramp = nodes.new("ShaderNodeMapRange")
    ramp.inputs["From Min"].default_value = 0.0
    ramp.inputs["From Max"].default_value = 1.0
    ramp.inputs["To Min"].default_value = 0.8
    ramp.inputs["To Max"].default_value = 0.04
    links.new(lw.outputs["Facing"], ramp.inputs["Value"])
    links.new(ramp.outputs["Result"], bsdf.inputs["Emission Strength"])
    return mat


def build_scene(size):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    crystal = build_mesh()
    crystal.data.materials.append(preview_material())

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.16, 0.15, 0.15, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    scene.world = world

    def area(name, energy, size_, colour, loc):
        light = bpy.data.lights.new(name, "AREA")
        light.energy = energy
        light.size = size_
        light.color = colour
        obj = bpy.data.objects.new(name, light)
        obj.location = loc
        direction = Vector((0, 0, 0)) - Vector(loc)
        obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(obj)

    area("Key", 120, 1.0, (1.0, 0.88, 0.70), (-1.4, -1.6, 2.0))
    area("Rim", 160, 0.8, (0.70, 0.80, 1.0), (1.2, 1.8, 0.9))
    area("Fill", 30, 2.0, (1.0, 0.95, 0.9), (2.0, -1.2, -0.4))

    cam = bpy.data.cameras.new("Cam")
    cam.type = "ORTHO"
    cam.ortho_scale = 1.45
    cam_obj = bpy.data.objects.new("Cam", cam)
    cam_obj.location = (0, -3.0, 1.9)  # ~32 deg down: a bit above, like the gameplay camera sees it
    cam_obj.rotation_euler = (Vector((0, 0, 0)) - cam_obj.location).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.render.film_transparent = False
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.view_settings.view_transform = "Standard"
    return scene, crystal


def render_preview(scene, crystal, path, size, tmp_dir):
    """Hero frame (big) on top, 6 turntable frames (60 deg apart, tilted like the game) in a strip under it."""
    frames = []
    for i, yaw in enumerate([0] + [k * 60 for k in range(6)]):
        crystal.rotation_euler = (math.radians(12), 0.0, math.radians(yaw + 15))
        scene.render.resolution_x = scene.render.resolution_y = size if i == 0 else size // 3
        f = os.path.join(tmp_dir, f"xp_crystal_{i}.png")
        scene.render.filepath = f
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(f)
        w, h = img.size
        frames.append(np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4))
        bpy.data.images.remove(img)
    crystal.rotation_euler = (0, 0, 0)

    hero = frames[0]
    small = frames[1:]
    sw = small[0].shape[1]
    strip_w = sw * 6
    width = max(hero.shape[1], strip_w)
    sheet = np.zeros((hero.shape[0] + small[0].shape[0], width, 4), dtype=np.float32)
    sheet[..., 3] = 1.0
    sheet[..., :3] = hero[2, 2, :3]  # same backdrop colour as the renders
    # Blender pixels are bottom-up: the strip goes at the bottom rows, the hero above it.
    for k, f in enumerate(small):
        sheet[0:f.shape[0], k * sw:(k + 1) * sw] = f
    x0 = (width - hero.shape[1]) // 2
    sheet[small[0].shape[0]:, x0:x0 + hero.shape[1]] = hero

    out = bpy.data.images.new("preview", width, sheet.shape[0], alpha=False)
    out.pixels[:] = sheet.ravel()
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()


def export_fbx(crystal, path):
    bpy.ops.object.select_all(action="DESELECT")
    crystal.select_set(True)
    bpy.context.view_layer.objects.active = crystal
    saved = list(crystal.data.materials)
    crystal.data.materials.clear()  # the game binds its own material
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
        path_mode="STRIP", embed_textures=False)
    for m in saved:
        crystal.data.materials.append(m)


def main():
    fbx = os.path.join(ROOT, arg("--fbx", "Assets/_Project/Art/Progression/SM_XpCrystal.fbx"))
    blend = os.path.join(ROOT, arg("--blend", "ArtSource/Progression/XpCrystal/XpCrystal.blend"))
    preview = os.path.join(ROOT, arg("--preview", "Docs/screenshots/kan177-xp-crystal-blender.png"))
    size = int(arg("--size", "512"))
    scene, crystal = build_scene(size)
    tris = len(crystal.data.polygons)
    dims = crystal.dimensions
    print(f"[xp_crystal] tris={tris} size={dims.x:.3f} x {dims.y:.3f} x {dims.z:.3f}")
    if tris >= MAX_TRIS:
        raise SystemExit(f"too many tris: {tris}")
    for p in (fbx, blend, preview):
        os.makedirs(os.path.dirname(p), exist_ok=True)
    export_fbx(crystal, fbx)
    render_preview(scene, crystal, preview, size, os.path.dirname(blend))
    for f in os.listdir(os.path.dirname(blend)):
        if f.startswith("xp_crystal_") and f.endswith(".png"):
            os.remove(os.path.join(os.path.dirname(blend), f))
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backup
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    print(f"[xp_crystal] wrote {fbx} ({os.path.getsize(fbx)} bytes), {preview}, {blend}")


main()
