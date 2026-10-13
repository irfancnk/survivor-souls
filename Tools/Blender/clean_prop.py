"""
Clean a generated prop (TRELLIS.2 / TRELLIS / Meshy GLB) for Survivor Souls (KAN-27).

Run headless from the repo root:
    One asset:
        blender -b -P Tools/Blender/clean_prop.py -- --in <prop.glb> --out <dir> --name SM_R1_Rock_Boulder_01
    A whole Drive inbox (each <Incoming>/<Name>/ holds <Name>.glb + meta.json [+ preview.png]):
        blender -b -P Tools/Blender/clean_prop.py -- --batch <Drive>/Incoming --out <Drive>/Approved

Options (meta.json "cleanup" block overrides the defaults, command line overrides both):
    --tris N        LOD0 triangle budget (default 1200, prop rule 200-1500)
    --height M      scale so the prop is M metres tall (default 0 = keep generator scale)
    --tex N         texture size (default 512)
    --lods A,B      LOD ratios of LOD0 (default 0.5,0.25)
    --bake          new UV unwrap + bake base colour and normal from the high mesh
                    (use when the input has no UVs / textures, or the UVs are bad)
    --force         process again even when <out>/<Name>/<Name>.fbx exists
    --preview       also render <Name>_preview.png (Workbench)

Steps:
  1. Import GLB, apply transforms, join all mesh parts into one object.
  2. Merge close vertices, delete small loose parts (floaters), delete loose verts/edges.
  3. Scale (optional), pivot = bottom centre at the origin.
  4. Decimate to the budget, triangulate, recalc normals, smooth by angle.
  5. Textures: reuse the generator UVs and resize its PBR maps, or (--bake) unwrap + bake.
     Output (KAN-26 names): T_<Body>_BC.png, T_<Body>_N.png, T_<Body>_MASK.png (R metal, A smooth: URP Lit).
     A source with more than one textured material is always baked (one material per prop).
  6. LOD1.. by more decimation (objects <Name>_LOD0, _LOD1 ...: Unity makes the LODGroup).
  7. Export FBX (Y up, -Z forward, transforms applied: rotation 0 + scale 1 in Unity).
  8. Write <Name>.json: the input meta + cleanup numbers (tris per LOD, texture size, size, time).
"""
import argparse
import datetime
import json
import math
import os
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Vector

DEFAULTS = dict(tris=1200, height=0.0, tex=512, lods=[0.5, 0.25], bake=False)
MERGE_DIST = 0.0005        # m, after scale
MIN_PART = 0.03            # loose part smaller than this x bbox diagonal is removed
SMOOTH_ANGLE_DEG = 40.0
BAKE_SAMPLES = 4
UV_ANGLE_DEG = 80.0        # smart UV project for --bake: higher = bigger islands, more stretch
BAKE_CAGE = 0.02           # ray distance x bbox diagonal (high -> low bake)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--in", dest="src")
    p.add_argument("--batch")
    p.add_argument("--out", required=True)
    p.add_argument("--name")
    p.add_argument("--tris", type=int)
    p.add_argument("--height", type=float)
    p.add_argument("--tex", type=int)
    p.add_argument("--lods")
    p.add_argument("--bake", action="store_true", default=None)
    p.add_argument("--force", action="store_true")
    p.add_argument("--preview", action="store_true")
    a = p.parse_args(argv)
    if bool(a.src) == bool(a.batch):
        p.error("give --in or --batch")
    return a


def settings_for(args, meta):
    s = dict(DEFAULTS)
    s.update({k: v for k, v in meta.get("cleanup", {}).items() if k in DEFAULTS})
    if args.tris: s["tris"] = args.tris
    if args.height is not None: s["height"] = args.height
    if args.tex: s["tex"] = args.tex
    if args.lods: s["lods"] = [float(x) for x in args.lods.split(",") if x]
    if args.bake: s["bake"] = True
    return s


# ------------------------------------------------------------------ scene helpers

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def bbox(obj):
    pts = np.array([v.co[:] for v in obj.data.vertices])
    return pts.min(axis=0), pts.max(axis=0)


def import_glb(path):
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"no mesh in {path}")
    # Unparent (keep world transform), drop empties / armatures / cameras.
    for o in meshes:
        if o.parent:
            m = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = m
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o)
    select_only(meshes)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    for mod in list(obj.modifiers):
        obj.modifiers.remove(mod)
    return obj


def clean_geometry(obj):
    lo, hi = bbox(obj)
    diag = float(np.linalg.norm(hi - lo))
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=diag * 1e-4)
    # Loose parts by face islands; keep big ones.
    bm.faces.ensure_lookup_table()
    seen, islands = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, island = [f], []
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            island.append(cur)
            for e in cur.edges:
                for nb in e.link_faces:
                    if nb.index not in seen:
                        seen.add(nb.index)
                        stack.append(nb)
        islands.append(island)
    removed = 0
    for island in islands:
        pts = np.array([v.co[:] for f in island for v in f.verts])
        if float(np.linalg.norm(pts.max(axis=0) - pts.min(axis=0))) < diag * MIN_PART:
            bmesh.ops.delete(bm, geom=island, context="FACES")
            removed += 1
    loose_verts = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose_verts, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return removed, len(islands)


def scale_and_pivot(obj, height):
    lo, hi = bbox(obj)
    size = hi - lo
    s = height / size[2] if height > 0 and size[2] > 0 else 1.0
    pivot = Vector(((lo[0] + hi[0]) * 0.5, (lo[1] + hi[1]) * 0.5, lo[2]))
    for v in obj.data.vertices:
        v.co = (v.co - pivot) * s
    obj.data.update()
    obj.location = (0, 0, 0)
    return s


def decimate(obj, target_tris):
    tris = tri_count(obj)
    if tris > target_tris:
        mod = obj.modifiers.new("Decimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = target_tris / tris
        mod.use_collapse_triangulate = True
        select_only([obj])
        bpy.ops.object.modifier_apply(modifier=mod.name)
    mod = obj.modifiers.new("Triangulate", "TRIANGULATE")
    select_only([obj])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(SMOOTH_ANGLE_DEG))


# ------------------------------------------------------------------ textures

def tex_path(out_dir, name, suffix):
    """KAN-26 names: SM_R1_Rock_Boulder_01 -> T_R1_Rock_Boulder_01_BC.png (BC, N, MASK)."""
    body = name[3:] if name.startswith("SM_") else name
    return os.path.join(out_dir, f"T_{body}_{suffix}.png")


def source_images(obj):
    """Base colour, normal and glTF metallic-roughness images of the first material that has them."""
    out = {}
    for mat in obj.data.materials:
        if not mat or not mat.node_tree:
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if not bsdf:
            continue

        def image_behind(socket):
            stack = [l.from_node for l in socket.links]
            while stack:
                n = stack.pop()
                if n.type == "TEX_IMAGE" and n.image:
                    return n.image
                for i in n.inputs:
                    stack.extend(l.from_node for l in i.links)
            return None

        out.setdefault("base", image_behind(bsdf.inputs["Base Color"]))
        out.setdefault("normal", image_behind(bsdf.inputs["Normal"]))
        out.setdefault("mr", image_behind(bsdf.inputs["Metallic"]) or image_behind(bsdf.inputs["Roughness"]))
        if all(out.values()):
            break
    return {k: v for k, v in out.items() if v}


def pixels(img, size):
    img = img.copy()
    img.scale(size, size)
    px = np.empty(size * size * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(size, size, 4)


def save_png(px, path, non_color=False):
    size = px.shape[0]
    img = bpy.data.images.new(os.path.basename(path), size, size, alpha=True, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(px.astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def write_metallic_smoothness(mr_px, path):
    """glTF MR (G rough, B metal) -> URP Lit metallic map (R metal, A smooth)."""
    out = np.zeros_like(mr_px)
    if mr_px is None:
        out[..., 3] = 0.25
    else:
        out[..., 0] = mr_px[..., 2]
        out[..., 1] = mr_px[..., 2]
        out[..., 2] = mr_px[..., 2]
        out[..., 3] = 1.0 - mr_px[..., 1]
    save_png(out, path, non_color=True)


def reuse_textures(obj, name, out_dir, size):
    imgs = source_images(obj)
    if "base" not in imgs or not obj.data.uv_layers or len([m for m in obj.data.materials if m]) > 1:
        return False
    for img in imgs.values():
        img.colorspace_settings.name = "Non-Color"  # raw values, no transform on read
    save_png(pixels(imgs["base"], size), tex_path(out_dir, name, "BC"))
    if "normal" in imgs:
        save_png(pixels(imgs["normal"], size), tex_path(out_dir, name, "N"), non_color=True)
    mr = pixels(imgs["mr"], size) if "mr" in imgs else None
    write_metallic_smoothness(mr, tex_path(out_dir, name, "MASK"))
    return True


def bake_textures(high, low, name, out_dir, size):
    """New UVs on low, bake base colour + tangent normal from high (selected to active)."""
    while low.data.uv_layers:
        low.data.uv_layers.remove(low.data.uv_layers[0])
    low.data.uv_layers.new(name="UVMap")
    select_only([low])
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(UV_ANGLE_DEG), island_margin=0.0, area_weight=1.0)
    bpy.ops.uv.pack_islands(rotate=True, margin=2.0 / size, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = BAKE_SAMPLES
    bake = scene.render.bake
    bake.use_selected_to_active = True
    lo, hi = bbox(high)
    bake.cage_extrusion = float(np.linalg.norm(hi - lo)) * BAKE_CAGE
    bake.margin = 4

    mat = bpy.data.materials.new(f"M_{name}_bake")
    mat.use_nodes = True
    low.data.materials.clear()
    low.data.materials.append(mat)
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    mat.node_tree.nodes.active = tex

    def run(kind, filename, non_color):
        img = bpy.data.images.new(filename, size, size, alpha=False)
        if non_color:
            img.colorspace_settings.name = "Non-Color"
        tex.image = img
        select_only([high, low], active=low)
        if kind == "DIFFUSE":
            bake.use_pass_direct = False
            bake.use_pass_indirect = False
            bake.use_pass_color = True
        bpy.ops.object.bake(type=kind)
        px = np.empty(size * size * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        bpy.data.images.remove(img)
        return px.reshape(size, size, 4)

    save_png(run("DIFFUSE", f"{name}_BaseColor", False), tex_path(out_dir, name, "BC"))
    save_png(run("NORMAL", f"{name}_Normal", True), tex_path(out_dir, name, "N"), non_color=True)
    imgs = source_images(high)
    mr = None
    if "mr" in imgs:  # roughness/metal as averages: baking them needs an emission trick, not worth it for props
        avg = pixels(imgs["mr"], 64).reshape(-1, 4).mean(axis=0)
        mr = np.broadcast_to(avg, (size, size, 4)).copy()
    write_metallic_smoothness(mr, tex_path(out_dir, name, "MASK"))
    # Show the baked colour on the low mesh (preview render only; the FBX carries no textures).
    tex.image = bpy.data.images.load(tex_path(out_dir, name, "BC"))
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])


# ------------------------------------------------------------------ pipeline

def render_preview(objs, path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.light = "STUDIO"
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.film_transparent = True
    for o in objs[1:]:
        o.hide_render = True
    lo, hi = bbox(objs[0])
    centre = Vector(((lo + hi) * 0.5).tolist())
    radius = float(np.linalg.norm(hi - lo)) * 0.5
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    direction = Vector((1.0, -1.2, 0.8)).normalized()   # three-quarter view
    cam.location = centre + direction * radius * 3.2
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in objs[1:]:
        o.hide_render = False
    bpy.data.objects.remove(cam)


def export_fbx(objs, path):
    select_only(objs)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE",
        use_tspace=True, add_leaf_bones=False, path_mode="STRIP", embed_textures=False,
        bake_anim=False)


def process(src, name, out_root, s, preview):
    t0 = time.time()
    out_dir = os.path.join(out_root, name)
    os.makedirs(out_dir, exist_ok=True)
    reset_scene()
    obj = import_glb(src)
    tris_in = tri_count(obj)
    removed, parts = clean_geometry(obj)
    scale = scale_and_pivot(obj, s["height"])

    high = None
    if s["bake"]:
        high = obj.copy()
        high.data = obj.data.copy()
        high.name = f"{name}_high"
        bpy.context.scene.collection.objects.link(high)

    obj.name = obj.data.name = f"{name}_LOD0"
    decimate(obj, s["tris"])

    if s["bake"]:
        bake_textures(high, obj, name, out_dir, s["tex"])
        bpy.data.objects.remove(high)
        textures = "baked"
    elif reuse_textures(obj, name, out_dir, s["tex"]):
        textures = "reused"
    else:
        print(f"[clean_prop] {name}: no UV/texture in source, baking instead")
        high = obj.copy()
        high.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(high)
        bake_textures(high, obj, name, out_dir, s["tex"])
        bpy.data.objects.remove(high)
        textures = "baked"

    # Collapse stops early on non-manifold edges: a LOD that saves < 15 % is dropped.
    lods = [obj]
    for ratio in s["lods"]:
        lod = obj.copy()
        lod.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(lod)
        decimate(lod, max(12, int(tri_count(obj) * ratio)))
        if tri_count(lod) > tri_count(lods[-1]) * 0.85:
            bpy.data.objects.remove(lod)
            break
        lod.name = lod.data.name = f"{name}_LOD{len(lods)}"
        lods.append(lod)

    export_fbx(lods, os.path.join(out_dir, f"{name}.fbx"))
    if preview:
        render_preview(lods, os.path.join(out_dir, f"{name}_preview.png"))

    lo, hi = bbox(obj)
    return dict(
        tris_in=tris_in, parts_in=parts, budget_ok=tri_count(obj) <= s["tris"] * 1.1, parts_removed=removed, scale=round(scale, 4),
        tris_lod=[tri_count(o) for o in lods], size_m=[round(float(x), 3) for x in (hi - lo)],
        texture_size=s["tex"], textures=textures, seconds=round(time.time() - t0, 1),
        blender=bpy.app.version_string, date=datetime.date.today().isoformat())


def write_meta(src_meta, cleanup, name, out_root):
    meta = dict(src_meta)
    meta["name"] = name
    meta["cleanup_result"] = cleanup
    with open(os.path.join(out_root, name, f"{name}.json"), "w") as f:
        json.dump(meta, f, indent=2)


def load_meta(folder):
    path = os.path.join(folder, "meta.json")
    if os.path.exists(path):
        with open(path) as f:
            return json.load(f)
    return {}


def main():
    args = parse_args()
    jobs = []
    if args.batch:
        for name in sorted(os.listdir(args.batch)):
            folder = os.path.join(args.batch, name)
            glb = os.path.join(folder, f"{name}.glb")
            if os.path.isfile(glb):
                jobs.append((glb, name, load_meta(folder)))
    else:
        name = args.name or os.path.splitext(os.path.basename(args.src))[0]
        jobs.append((args.src, name, load_meta(os.path.dirname(os.path.abspath(args.src)))))

    failed = 0
    for src, name, meta in jobs:
        if not args.force and os.path.exists(os.path.join(args.out, name, f"{name}.fbx")):
            print(f"[clean_prop] {name}: already done, skip (use --force)")
            continue
        try:
            result = process(src, name, args.out, settings_for(args, meta), args.preview)
            write_meta(meta, result, name, args.out)
            print(f"[clean_prop] {name}: {json.dumps(result)}")
            if not result["budget_ok"]:
                print(f"[clean_prop] {name}: WARNING LOD0 {result['tris_lod'][0]} tris > budget {settings_for(args, meta)['tris']}"
                      " (non-manifold parts stop the decimate: fix by hand or use --bake on a remesh)")
        except Exception as e:  # keep going with the other assets
            failed += 1
            print(f"[clean_prop] {name}: FAILED {e!r}")
    print(f"[clean_prop] done: {len(jobs) - failed}/{len(jobs)} ok")
    sys.exit(1 if failed else 0)


main()
