"""Gate arrow (KAN-63): low-poly forged-gold arrow with a dark iron edge, baked to one HUD sprite.

Run (headless, ~20 s):
    blender -b --python Tools/Blender/build_gate_arrow.py -- [--out PNG] [--blend BLEND] [--size PX]

Defaults:
    --out   Assets/_Project/Art/UI/T_UI_GateArrow.png  (sprite, tip up, transparent, ember glow baked in)
    --blend ArtSource/UI/GateArrow/GateArrow.blend     (source scene: mesh, materials, light, camera)
    --size  256

Model: one arrow outline (bold head, tapered shaft, swallow tail), extruded and bevelled into a pillow-forged gold
plate; under it the same outline grown by IRON_EDGE as a dark iron backing plate. Under 500 tris in total.
Render: Cycles CPU (no GPU needed headless), orthographic top-down camera, transparent film, one warm key light +
cool fill. The ember glow is added after the render: the alpha is blurred, tinted ember orange and put under the arrow.
"""

import math
import os
import sys

import bmesh
import bpy
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# Arrow outline, tip at +Y, counter-clockwise. Units: 1 = half the sprite height.
OUTLINE = [
    (0.00, 0.92),    # tip
    (-0.56, 0.20),   # head left corner
    (-0.21, 0.27),   # head left inner
    (-0.15, -0.58),  # shaft left end
    (-0.33, -0.82),  # tail left fin
    (0.00, -0.66),   # tail notch
    (0.33, -0.82),   # tail right fin
    (0.15, -0.58),   # shaft right end
    (0.21, 0.27),    # head right inner
    (0.56, 0.20),    # head right corner
]
IRON_EDGE = 0.055    # dark iron backing plate, grown by this much
GOLD_HEIGHT = 0.10
IRON_HEIGHT = 0.05
GLOW_COLOUR = np.array([1.0, 0.42, 0.08])
GLOW_STRENGTH = 0.85


def arg(name, default):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return argv[argv.index(name) + 1] if name in argv else default


def offset_outline(points, d):
    """Miter offset of a closed CCW polygon by d (outwards)."""
    n = len(points)
    out = []
    for i in range(n):
        p0, p1, p2 = np.array(points[i - 1]), np.array(points[i]), np.array(points[(i + 1) % n])
        e0, e1 = p1 - p0, p2 - p1
        n0 = np.array([e0[1], -e0[0]]) / np.linalg.norm(e0)   # right normal = outside for CCW
        n1 = np.array([e1[1], -e1[0]]) / np.linalg.norm(e1)
        bis = n0 + n1
        bis /= np.linalg.norm(bis)
        miter = d / max(0.35, float(np.dot(bis, n1)))       # clamp sharp spikes
        out.append(tuple(p1 + bis * miter))
    return out


def plate(name, outline, height, z, bevel, segments, material):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    verts = [bm.verts.new((x, y, z)) for x, y in outline]
    face = bm.faces.new(verts)
    if face.normal.z < 0:
        face.normal_flip()
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    top = [v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, height), verts=top)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = bevel
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(40)
    mod.profile = 0.7
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Bevel")
    tri = obj.modifiers.new("Tri", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier="Tri")
    obj.select_set(False)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def material(name, colour, metallic, roughness, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def build_scene(size):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    gold = material("M_GateArrow_Gold", (1.0, 0.70, 0.30), 1.0, 0.32, (1.0, 0.55, 0.18), 0.25)
    iron = material("M_GateArrow_Iron", (0.055, 0.045, 0.04), 0.85, 0.48)
    plate("GateArrow_Iron", offset_outline(OUTLINE, IRON_EDGE), IRON_HEIGHT, 0.0, 0.02, 1, iron)
    plate("GateArrow_Gold", OUTLINE, GOLD_HEIGHT, IRON_HEIGHT * 0.6, 0.05, 2, gold)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.20, 0.17, 0.15, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    scene.world = world

    key = bpy.data.lights.new("Key", "AREA")
    key.energy = 260
    key.size = 1.2
    key.color = (1.0, 0.86, 0.66)
    key_obj = bpy.data.objects.new("Key", key)
    key_obj.location = (-1.6, 1.8, 2.6)
    key_obj.rotation_euler = (math.radians(-38), math.radians(-30), 0)
    scene.collection.objects.link(key_obj)
    fill = bpy.data.lights.new("Fill", "AREA")
    fill.energy = 70
    fill.size = 2.0
    fill.color = (0.62, 0.70, 1.0)
    fill_obj = bpy.data.objects.new("Fill", fill)
    fill_obj.location = (1.8, -1.4, 2.2)
    fill_obj.rotation_euler = (math.radians(32), math.radians(36), 0)
    scene.collection.objects.link(fill_obj)

    cam = bpy.data.cameras.new("Cam")
    cam.type = "ORTHO"
    cam.ortho_scale = 2.3  # margin for the glow
    cam_obj = bpy.data.objects.new("Cam", cam)
    cam_obj.location = (0, 0, 5)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    return scene


def blur(a, radius, passes=3):
    """Separable box blur, repeated (close to a gaussian)."""
    k = 2 * radius + 1
    for _ in range(passes):
        p = np.pad(a, radius, mode="constant")
        c = np.cumsum(np.cumsum(p, axis=0), axis=1)
        c = np.pad(c, ((1, 0), (1, 0)))
        a = (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)
    return a


def add_glow(path, size):
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(size, size, 4)
    alpha = px[..., 3]
    glow = np.clip(blur(alpha, max(2, size // 40)) * 1.6, 0.0, 1.0) * GLOW_STRENGTH
    out_a = alpha + glow * (1.0 - alpha)
    rgb = (px[..., :3] * alpha[..., None] + GLOW_COLOUR * (glow * (1.0 - alpha))[..., None]) / np.maximum(out_a, 1e-5)[..., None]
    px[..., :3] = np.clip(rgb, 0.0, 1.0)
    px[..., 3] = out_a
    img.pixels[:] = px.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


def main():
    out = os.path.join(ROOT, arg("--out", "Assets/_Project/Art/UI/T_UI_GateArrow.png"))
    blend = os.path.join(ROOT, arg("--blend", "ArtSource/UI/GateArrow/GateArrow.blend"))
    size = int(arg("--size", "256"))
    scene = build_scene(size)
    tris = sum(len(o.data.polygons) for o in scene.objects if o.type == "MESH")
    print(f"[gate_arrow] tris={tris}")
    if tris >= 500:
        raise SystemExit(f"too many tris: {tris}")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    os.makedirs(os.path.dirname(blend), exist_ok=True)
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    add_glow(out, size)
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backup
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    print(f"[gate_arrow] wrote {out} ({os.path.getsize(out)} bytes), {blend}")


main()
