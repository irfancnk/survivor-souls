"""Blender headless: Meshy rigged GLB + anim GLBs -> Unity-ready FBX set.
blender -b --python to_unity.py -- <project_dir> <out_dir> <Name>
Writes SK_<Name>.fbx (mesh + armature, no anim), A_<Name>_<Clip>.fbx (armature only, one take),
T_<Name>_BaseColor.jpg, T_<Name>_Normal.png, T_<Name>_Emission.jpg (if not black), info.json."""
import bpy, sys, os, glob, json

argv = sys.argv[sys.argv.index("--") + 1:]
proj, out, name = argv
os.makedirs(out, exist_ok=True)

FBX = dict(use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
           axis_forward='-Z', axis_up='Y', add_leaf_bones=False, primary_bone_axis='Y',
           secondary_bone_axis='X', armature_nodetype='NULL', path_mode='STRIP',
           embed_textures=False, mesh_smooth_type='FACE', use_armature_deform_only=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def imp(path):
    bpy.ops.import_scene.gltf(filepath=path)


def select(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


info = {"name": name, "clips": {}}

# --- skinned mesh
reset()
imp(os.path.join(proj, "rigged.glb"))
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm]
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.parent != arm]:
    bpy.data.objects.remove(o, do_unlink=True)  # Meshy rig GLB carries a stray 2 m Icosphere
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
if arm.animation_data:
    arm.animation_data_clear()
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
zs = [(o.matrix_world @ v.co).z for o in meshes for v in o.evaluated_get(dg).to_mesh().vertices]
info["height_m"] = round(max(zs) - min(zs), 3)
info["tris"] = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
info["verts"] = sum(len(o.data.vertices) for o in meshes)
info["bones"] = len(arm.data.bones)
info["root_bone"] = arm.data.bones[0].name
for o in meshes:
    o.name = o.name if o.name.startswith(name + "_") else f"{name}_Mesh"  # keep named parts (Shadow Ranger body / bow / string / arrow / mask)
arm.name = f"{name}_Rig"
select([arm] + meshes)
bpy.ops.export_scene.fbx(filepath=os.path.join(out, f"SK_{name}.fbx"), object_types={'ARMATURE', 'MESH'},
                         bake_anim=False, **FBX)

# --- textures
done = set()
for o in meshes:
    for slot in o.material_slots:
        m = slot.material
        if not m or not m.use_nodes:
            continue
        bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if not bsdf:
            continue

        def src_img(sock):
            for l in sock.links:
                n = l.from_node
                for _ in range(4):
                    if n.type == 'TEX_IMAGE':
                        return n.image
                    ins = [i for i in n.inputs if i.links]
                    if not ins:
                        break
                    n = ins[0].links[0].from_node
            return None

        for sock_name, tag, fmt, size in (("Base Color", "BaseColor", "JPEG", 2048),
                                           ("Normal", "Normal", "PNG", 1024),
                                           ("Emission Color", "Emission", "JPEG", 1024)):
            sock = bsdf.inputs.get(sock_name)
            img = src_img(sock) if sock else None
            if not img or tag in done:
                continue
            if tag == "Emission":
                px = list(img.pixels[::97])
                if max(px[i] for i in range(len(px)) if i % 4 != 3) < 0.04:
                    continue
            im = img.copy()
            if max(im.size) > size:
                im.scale(size, size)
            im.filepath_raw = os.path.join(out, f"T_{name}_{tag}." + ("jpg" if fmt == "JPEG" else "png"))
            im.file_format = fmt
            bpy.context.scene.render.image_settings.quality = 90
            im.save()
            done.add(tag)
info["textures"] = sorted(done)

# --- animation clips (armature only)
for path in sorted(glob.glob(os.path.join(proj, "anim_*.glb"))):
    clip = os.path.basename(path)[5:-4]
    reset()
    imp(path)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for o in [o for o in bpy.data.objects if o.type != 'ARMATURE']:
        bpy.data.objects.remove(o, do_unlink=True)
    act = arm.animation_data.action if arm.animation_data else None
    if act is None and bpy.data.actions:
        act = bpy.data.actions[0]
        arm.animation_data_create().action = act
    for a in list(bpy.data.actions):
        if a != act:
            bpy.data.actions.remove(a)
    act.name = clip
    bpy.context.scene.name = clip  # FBX take name comes from the scene
    arm.name = f"{name}_Rig"
    f0, f1 = act.frame_range
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = int(f0), int(f1)
    fps = bpy.context.scene.render.fps
    info["clips"][clip] = {"frames": int(f1 - f0 + 1), "seconds": round((f1 - f0) / fps, 2)}
    select([arm])
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, f"A_{name}_{clip}.fbx"), object_types={'ARMATURE'},
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.5, **FBX)

json.dump(info, open(os.path.join(proj, "unity_export.json"), "w"), indent=1)
print("EXPORT_INFO", json.dumps(info))
