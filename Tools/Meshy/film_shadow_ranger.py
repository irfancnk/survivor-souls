"""Blender headless: lit preview film of the Shadow Ranger clips (EEVEE, MP4).
blender -b ShadowRanger.blend --python film_shadow_ranger.py -- <out.mp4> [--quick]
Clips back to back, one NLA track each (Shoot x3, Idle, Walking x2, Running x3) with short blends, a label per clip,
three-point light, dark stone ground, slow camera orbit. Walk / run play in place (root motion is frozen in the clips)."""
import bpy, sys, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT = args[0]
QUICK = "--quick" in args
sc = bpy.context.scene
arm = bpy.data.objects["ShadowRanger_Rig"]

# ---------------- clip sequence on one NLA track
ad = arm.animation_data or arm.animation_data_create()
ad.action = None
for tr in list(ad.nla_tracks):
    ad.nla_tracks.remove(tr)
SEQ = [("Shoot", 3, "SHOOT"), ("Idle", 1, "IDLE"), ("Walking", 2, "WALK"), ("Running", 3, "RUN")]
BLEND = 5
cursor = 1
segments = []
for name, reps, label in SEQ:
    act = bpy.data.actions[name]
    f0, f1 = act.frame_range
    length = (f1 - f0) * reps
    track = ad.nla_tracks.new(); track.name = name     # one track per clip: later tracks blend over earlier ones
    st = track.strips.new(name, int(cursor), act)
    if hasattr(st, "action_slot") and act.slots:
        st.action_slot = act.slots[0]
    st.repeat = reps
    st.blend_in = BLEND if segments else 0
    st.blend_out = BLEND
    st.extrapolation = 'NOTHING'
    st.blend_type = 'REPLACE'
    segments.append((label, int(cursor), int(cursor + length)))
    cursor = st.frame_end - BLEND          # overlap for the blend
end = int(segments[-1][2])
sc.frame_start, sc.frame_end = 1, end
sc.render.fps = 30

# ---------------- look
sc.render.engine = 'BLENDER_EEVEE'
sc.eevee.taa_render_samples = 16 if QUICK else 48
sc.view_settings.view_transform = 'AgX'
try:
    sc.view_settings.look = 'AgX - Medium High Contrast'
except Exception:
    pass
for o in [o for o in bpy.data.objects if o.type in ('LIGHT', 'CAMERA')]:
    bpy.data.objects.remove(o)
w = bpy.data.worlds.new("film"); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.045, 0.042, 0.04, 1); bg.inputs[1].default_value = 1.0


def light(name, typ, energy, rot, color, size=None):
    l = bpy.data.lights.new(name, typ); l.energy = energy; l.color = color
    if size is not None and hasattr(l, "angle"):
        l.angle = size
    o = bpy.data.objects.new(name, l); sc.collection.objects.link(o); o.rotation_euler = rot
    return o


light("Key", 'SUN', 3.6, (math.radians(48), 0, math.radians(-40)), (1.0, 0.86, 0.68), math.radians(6))
light("Rim", 'SUN', 4.0, (math.radians(62), 0, math.radians(150)), (0.62, 0.74, 1.0), math.radians(3))
light("Fill", 'SUN', 0.5, (math.radians(75), 0, math.radians(60)), (0.9, 0.9, 1.0), math.radians(20))

# ground: dark flagstone-ish disc that fades into the background
bpy.ops.mesh.primitive_circle_add(vertices=96, radius=6, fill_type='NGON', location=(0, 0, 0))
g = bpy.context.active_object; g.name = "Ground"
gm = bpy.data.materials.new("M_Ground"); gm.use_nodes = True
nt = gm.node_tree; bsdf = nt.nodes["Principled BSDF"]
tc = nt.nodes.new("ShaderNodeTexCoord"); grad = nt.nodes.new("ShaderNodeTexGradient"); grad.gradient_type = 'SPHERICAL'
mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (0.2, 0.2, 0.2)
noise = nt.nodes.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 3.0
mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'
mix.inputs[6].default_value = (0.030, 0.028, 0.026, 1); mix.inputs[7].default_value = (0.11, 0.10, 0.09, 1)
nt.links.new(tc.outputs["Object"], mp.inputs["Vector"]); nt.links.new(mp.outputs["Vector"], grad.inputs["Vector"])
mul = nt.nodes.new("ShaderNodeMath"); mul.operation = 'MULTIPLY'
nt.links.new(grad.outputs["Fac"], mul.inputs[0]); nt.links.new(noise.outputs["Fac"], mul.inputs[1])
nt.links.new(mul.outputs[0], mix.inputs[0]); nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
g.data.materials.append(gm)

# ---------------- camera: slow orbit, starts on the Legolas-like 3/4 view of the shot
cam = bpy.data.objects.new("FilmCam", bpy.data.cameras.new("FilmCam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 50
pivot = bpy.data.objects.new("Pivot", None); sc.collection.objects.link(pivot); pivot.location = (0, -0.15, 0.92)
cam.parent = pivot
start = math.atan2(-0.55, -1.0)              # ref direction (-1, -0.55) seen from above
dist, height = 6.2, 0.75
cam.location = (math.cos(start) * dist, math.sin(start) * dist, height)
con = cam.constraints.new('TRACK_TO'); con.target = pivot; con.track_axis = 'TRACK_NEGATIVE_Z'; con.up_axis = 'UP_Y'
pivot.rotation_euler = (0, 0, 0); pivot.keyframe_insert("rotation_euler", frame=1)
pivot.rotation_euler = (0, 0, math.radians(-50)); pivot.keyframe_insert("rotation_euler", frame=end)

# ---------------- labels, one per clip, parented to the camera
font_mat = bpy.data.materials.new("M_Label"); font_mat.use_nodes = True
fb = font_mat.node_tree.nodes["Principled BSDF"]
fb.inputs["Emission Color"].default_value = (0.85, 0.64, 0.26, 1); fb.inputs["Emission Strength"].default_value = 2.0
fb.inputs["Base Color"].default_value = (0, 0, 0, 1)
for label, a, b in segments:
    cu = bpy.data.curves.new(label, 'FONT'); cu.body = label; cu.size = 0.032; cu.extrude = 0.0
    t = bpy.data.objects.new("Label_" + label, cu); sc.collection.objects.link(t)
    t.data.materials.append(font_mat)
    t.parent = cam; t.location = (-0.34, -0.165, -1.0)
    for fr, hide in ((1, True), (a + 2, False), (b - 2, True)):
        t.hide_render = hide; t.keyframe_insert("hide_render", frame=max(1, fr))
    for fc in (t.animation_data.action.fcurves if hasattr(t.animation_data.action, "fcurves") else []):
        for kp in fc.keyframe_points:
            kp.interpolation = 'CONSTANT'
sub = bpy.data.curves.new("title", 'FONT'); sub.body = "THE SHADOW RANGER"; sub.size = 0.016
ts = bpy.data.objects.new("Title", sub); sc.collection.objects.link(ts); ts.data.materials.append(font_mat)
ts.parent = cam; ts.location = (-0.34, -0.187, -1.0)

# ---------------- output
sc.render.resolution_x, sc.render.resolution_y = (960, 540) if QUICK else (1280, 720)
sc.render.resolution_percentage = 100
if hasattr(sc.render.image_settings, 'media_type'):
    sc.render.image_settings.media_type = 'VIDEO'          # Blender 5: video output is a media type
sc.render.image_settings.file_format = 'FFMPEG'
sc.render.ffmpeg.format = 'MPEG4'
sc.render.ffmpeg.codec = 'H264'
sc.render.ffmpeg.constant_rate_factor = 'HIGH'
sc.render.ffmpeg.audio_codec = 'NONE'
sc.render.filepath = OUT
print("FILM frames", sc.frame_start, sc.frame_end, segments)
bpy.ops.render.render(animation=True)
print("FILM DONE", OUT)
