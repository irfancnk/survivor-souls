"""Blender headless: one web GLB per character (mesh + every clip as a named animation, small JPEG textures).
blender -b --python to_web.py -- <project_dir> <out.glb>"""
import bpy, sys, os, glob

argv = sys.argv[sys.argv.index("--") + 1:]
proj, out = argv
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(proj, "rigged.glb"))
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.parent != arm]:
    bpy.data.objects.remove(o, do_unlink=True)  # stray Icosphere in Meshy rig GLBs
keep_objs = set(bpy.data.objects)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
if arm.animation_data:
    arm.animation_data_clear()
arm.animation_data_create()

for path in sorted(glob.glob(os.path.join(proj, "anim_*.glb"))):
    clip = os.path.basename(path)[5:-4]
    before = set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=path)
    for o in [o for o in bpy.data.objects if o not in keep_objs]:
        bpy.data.objects.remove(o, do_unlink=True)
    new = [a for a in bpy.data.actions if a not in before]
    act = new[0]
    for a in new[1:]:
        bpy.data.actions.remove(a)
    act.name = clip
    act.use_fake_user = True
    track = arm.animation_data.nla_tracks.new()
    track.name = clip
    strip = track.strips.new(clip, int(act.frame_range[0]), act)
    if hasattr(strip, "action_slot") and act.slots:
        strip.action_slot = act.slots[0]
    track.mute = True

for img in bpy.data.images:
    if img.size[0] > 0:
        lim = 1024 if "normal" not in img.name.lower() and "metallic" not in img.name.lower() else 512
        if max(img.size) > lim:
            img.scale(lim, lim)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=82,
                          export_animations=True, export_animation_mode='NLA_TRACKS', export_skins=True,
                          export_morph=False, export_lights=False, export_cameras=False)
print("WEB_OK", out, os.path.getsize(out), [t.name for t in arm.animation_data.nla_tracks])
