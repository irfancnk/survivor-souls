# Survivor Souls - Chamber concept renders (Archero-size room, door NE, sea beyond the north wall).
# Run: blender -b --factory-startup -P chamber_concepts.py -- <concept 1|2|3> <out.png> [knight.glb] [zombie.fbx] [w h]
# Blender axes: X = east, Y = north, Z = up. Game camera: pitch 55, vertical FOV 40, portrait.
import bpy, bmesh, math, random, sys
from mathutils import Vector, Euler

argv = sys.argv[sys.argv.index("--") + 1:]
CONCEPT = argv[0]
OUT = argv[1]
KNIGHT = argv[2] if len(argv) > 2 and argv[2] != "-" else None
ZOMBIE = argv[3] if len(argv) > 3 and argv[3] != "-" else None
NOFOG = True  # EEVEE world volume blocks the sun here: no volume fog
RES = (int(argv[4]), int(argv[5])) if len(argv) > 5 else (720, 1560)

# Room interior (metres): 18 wide x 30 deep. Door in the north wall, east end (NE).
RX, RY = 9.0, 15.0
DOOR_X0, DOOR_X1 = 4.4, 7.6
WALL_T = 0.8

rng = random.Random(sum(map(ord, CONCEPT)) * 7919)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
ENV = bpy.data.collections.new("Env"); scene.collection.children.link(ENV)
FX = bpy.data.collections.new("FX"); scene.collection.children.link(FX)       # sea, foam, fog gate: shader-side in game
CHARS = bpy.data.collections.new("Chars"); scene.collection.children.link(CHARS)

def R(a, b): return rng.uniform(a, b)

# ---------------------------------------------------------------- materials
def nodes_of(m):
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    return nt, out

def principled(nt):
    return nt.nodes.new("ShaderNodeBsdfPrincipled")

def lin(nt, a, b): nt.links.new(a, b)

def mat_stone(name, c1, c2, rough=0.85, snow=0.0, wet=0.0, scale=3.0, moss=None):
    """Per-object tinted stone: colour from Object Info random + noise grime; optional snow on top faces."""
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    oi = nt.nodes.new("ShaderNodeObjectInfo")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = scale; nz.inputs["Detail"].default_value = 6
    lin(nt, tc.outputs["Object"], nz.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*c1, 1); ramp.color_ramp.elements[1].color = (*c2, 1)
    mix = nt.nodes.new("ShaderNodeMath"); mix.operation = "MULTIPLY_ADD"
    mix.inputs[1].default_value = 0.55
    lin(nt, oi.outputs["Random"], mix.inputs[0]); lin(nt, nz.outputs["Fac"], mix.inputs[2])
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = 0.75
    lin(nt, mix.outputs[0], mul.inputs[0]); lin(nt, mul.outputs[0], ramp.inputs["Fac"])
    col = ramp.outputs["Color"]
    if moss:
        mz = nt.nodes.new("ShaderNodeTexNoise"); mz.inputs["Scale"].default_value = 1.2
        lin(nt, tc.outputs["Object"], mz.inputs["Vector"])
        mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 0.55; mr.inputs["From Max"].default_value = 0.7
        lin(nt, mz.outputs["Fac"], mr.inputs["Value"])
        mm = nt.nodes.new("ShaderNodeMix"); mm.data_type = "RGBA"
        lin(nt, mr.outputs[0], mm.inputs["Factor"]); lin(nt, col, mm.inputs[6]); mm.inputs[7].default_value = (*moss, 1)
        col = mm.outputs[2]
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.35
    nz2 = nt.nodes.new("ShaderNodeTexNoise"); nz2.inputs["Scale"].default_value = scale * 6; nz2.inputs["Detail"].default_value = 8
    lin(nt, tc.outputs["Object"], nz2.inputs["Vector"]); lin(nt, nz2.outputs["Fac"], bump.inputs["Height"])
    lin(nt, bump.outputs["Normal"], p.inputs["Normal"])
    rough_in = rough
    if snow > 0:
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, geo.outputs["Normal"], sep.inputs[0])
        sn = nt.nodes.new("ShaderNodeTexNoise"); sn.inputs["Scale"].default_value = 4
        lin(nt, tc.outputs["Object"], sn.inputs["Vector"])
        add = nt.nodes.new("ShaderNodeMath"); add.operation = "MULTIPLY_ADD"; add.inputs[1].default_value = 0.35
        lin(nt, sn.outputs["Fac"], add.inputs[0]); lin(nt, sep.outputs["Z"], add.inputs[2])
        mr = nt.nodes.new("ShaderNodeMapRange")
        mr.inputs["From Min"].default_value = 1.35 - 0.4 * snow; mr.inputs["From Max"].default_value = 1.43 - 0.4 * snow
        lin(nt, add.outputs[0], mr.inputs["Value"])
        mm = nt.nodes.new("ShaderNodeMix"); mm.data_type = "RGBA"
        lin(nt, mr.outputs[0], mm.inputs["Factor"]); lin(nt, col, mm.inputs[6]); mm.inputs[7].default_value = (0.86, 0.9, 0.95, 1)
        col = mm.outputs[2]
    lin(nt, col, p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = rough_in if wet == 0 else rough_in * (1 - wet)
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_flat(name, color, rough=0.7, metal=0.0, emit=None, emit_str=0.0, alpha=None):
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = rough; p.inputs["Metallic"].default_value = metal
    if emit:
        p.inputs["Emission Color"].default_value = (*emit, 1); p.inputs["Emission Strength"].default_value = emit_str
    if alpha is not None:
        p.inputs["Alpha"].default_value = alpha
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_floor(name, kind):
    """Floor: brick-node flagstones (kind 'flag'), cathedral slabs ('slab') or sand ('sand'); snow / puddles from vertex colour 'mask'."""
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "mask"
    sepm = nt.nodes.new("ShaderNodeSeparateColor"); lin(nt, attr.outputs["Color"], sepm.inputs[0])
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 0.6; nz.inputs["Detail"].default_value = 5
    lin(nt, tc.outputs["Object"], nz.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    if kind in ("flag", "slab"):
        br = nt.nodes.new("ShaderNodeTexBrick")
        lin(nt, tc.outputs["Object"], br.inputs["Vector"])
        if kind == "flag":
            br.inputs["Scale"].default_value = 0.85; br.inputs["Mortar Size"].default_value = 0.025
            br.inputs["Brick Width"].default_value = 1.4; br.inputs["Row Height"].default_value = 0.9
            br.inputs["Color1"].default_value = (0.5, 0.47, 0.43, 1); br.inputs["Color2"].default_value = (0.26, 0.28, 0.31, 1)
            br.inputs["Mortar"].default_value = (0.12, 0.12, 0.13, 1)
            br.inputs["Bias"].default_value = 0.0
        else:
            br.inputs["Scale"].default_value = 0.55; br.inputs["Mortar Size"].default_value = 0.02
            br.inputs["Brick Width"].default_value = 1.0; br.inputs["Row Height"].default_value = 1.0; br.offset = 0.0
            br.inputs["Color1"].default_value = (0.47, 0.43, 0.39, 1); br.inputs["Color2"].default_value = (0.3, 0.29, 0.28, 1)
            br.inputs["Mortar"].default_value = (0.1, 0.1, 0.1, 1)
        grime = nt.nodes.new("ShaderNodeMix"); grime.data_type = "RGBA"; grime.blend_type = "MULTIPLY"
        grime.inputs["Factor"].default_value = 0.6
        lin(nt, br.outputs["Color"], grime.inputs[6]); lin(nt, nz.outputs["Fac"], grime.inputs[7])
        col = grime.outputs[2]
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
        lin(nt, br.outputs["Fac"], inv.inputs[1]); lin(nt, inv.outputs[0], bump.inputs["Height"])
        bump.inputs["Strength"].default_value = 0.4
    else:
        sand = nt.nodes.new("ShaderNodeValToRGB")
        sand.color_ramp.elements[0].color = (0.25, 0.235, 0.215, 1); sand.color_ramp.elements[1].color = (0.44, 0.41, 0.37, 1)
        lin(nt, nz.outputs["Fac"], sand.inputs["Fac"])
        vor = nt.nodes.new("ShaderNodeTexVoronoi"); vor.inputs["Scale"].default_value = 9
        lin(nt, tc.outputs["Object"], vor.inputs["Vector"])
        peb = nt.nodes.new("ShaderNodeMapRange"); peb.inputs["From Min"].default_value = 0.0; peb.inputs["From Max"].default_value = 0.25
        peb.inputs["To Min"].default_value = 1.0; peb.inputs["To Max"].default_value = 0.0
        lin(nt, vor.outputs["Distance"], peb.inputs["Value"])
        pm = nt.nodes.new("ShaderNodeMix"); pm.data_type = "RGBA"; pm.blend_type = "MULTIPLY"
        pmf = nt.nodes.new("ShaderNodeMath"); pmf.operation = "MULTIPLY"; pmf.inputs[1].default_value = 0.35
        lin(nt, peb.outputs[0], pmf.inputs[0]); lin(nt, pmf.outputs[0], pm.inputs["Factor"])
        lin(nt, sand.outputs["Color"], pm.inputs[6]); pm.inputs[7].default_value = (0.45, 0.45, 0.47, 1)
        col = pm.outputs[2]
        lin(nt, peb.outputs[0], bump.inputs["Height"]); bump.inputs["Strength"].default_value = 0.25
    # R channel of mask = snow / foam-wet (white), G = puddle (dark glossy)
    # crisp edges: mask + fine noise, then a narrow step (snow drifts / puddles with clean borders, not haze)
    def crisp(channel, sc, lo, hi):
        en = nt.nodes.new("ShaderNodeTexNoise"); en.inputs["Scale"].default_value = sc; en.inputs["Detail"].default_value = 8
        lin(nt, tc.outputs["Object"], en.inputs["Vector"])
        ad = nt.nodes.new("ShaderNodeMath"); ad.operation = "MULTIPLY_ADD"; ad.inputs[1].default_value = 0.45
        lin(nt, en.outputs["Fac"], ad.inputs[0]); lin(nt, channel, ad.inputs[2])
        st = nt.nodes.new("ShaderNodeMapRange"); st.inputs["From Min"].default_value = lo; st.inputs["From Max"].default_value = hi
        lin(nt, ad.outputs[0], st.inputs["Value"])
        return st.outputs[0]
    snow_f = crisp(sepm.outputs["Red"], 1.6, 0.68, 0.76)
    pud_f = crisp(sepm.outputs["Green"], 0.9, 0.62, 0.66)
    snow_mix = nt.nodes.new("ShaderNodeMix"); snow_mix.data_type = "RGBA"
    lin(nt, snow_f, snow_mix.inputs["Factor"]); lin(nt, col, snow_mix.inputs[6])
    snow_mix.inputs[7].default_value = (0.8, 0.84, 0.9, 1)
    pud = nt.nodes.new("ShaderNodeMix"); pud.data_type = "RGBA"; pud.blend_type = "MULTIPLY"
    lin(nt, pud_f, pud.inputs["Factor"]); lin(nt, snow_mix.outputs[2], pud.inputs[6])
    pud.inputs[7].default_value = (0.6, 0.62, 0.68, 1)
    lin(nt, pud.outputs[2], p.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.8 if kind != "slab" else 0.55; rr.inputs["To Max"].default_value = 0.12
    lin(nt, pud_f, rr.inputs["Value"]); lin(nt, rr.outputs[0], p.inputs["Roughness"])
    lin(nt, bump.outputs["Normal"], p.inputs["Normal"])
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_sea(name, deep, shallow, shore_y, foam_amount=1.0, swell=1.0):
    """Mobile-style sea: two swell bands (Gerstner stand-in) + noise chop -> height h; h drives bump, trough / crest colour
    and crest foam; shore wash lines near the shore; ocean-sim foam attribute on top. Depth colour from distance to shore."""
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    def node(t, **inp):
        n = nt.nodes.new(t)
        for k, v in inp.items(): n.inputs[k].default_value = v
        return n
    def math_(op, a, b=None, c=None):
        n = nt.nodes.new("ShaderNodeMath"); n.operation = op
        for i, v in enumerate((a, b, c)):
            if v is None: continue
            if isinstance(v, (int, float)): n.inputs[i].default_value = v
            else: lin(nt, v, n.inputs[i])
        return n.outputs[0]
    def maprange(v, f0, f1, t0=0.0, t1=1.0):
        n = node("ShaderNodeMapRange", **{"From Min": f0, "From Max": f1, "To Min": t0, "To Max": t1}); lin(nt, v, n.inputs["Value"]); return n.outputs[0]
    def mix(f, a, b):
        n = nt.nodes.new("ShaderNodeMix"); n.data_type = "RGBA"
        if isinstance(f, (int, float)): n.inputs["Factor"].default_value = f
        else: lin(nt, f, n.inputs["Factor"])
        for idx, v in ((6, a), (7, b)):
            if isinstance(v, tuple): n.inputs[idx].default_value = (*v, 1)
            else: lin(nt, v, n.inputs[idx])
        return n.outputs[2]
    def wave(scale, rot_deg, distortion, phase):
        mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Rotation"].default_value = (0, 0, math.radians(rot_deg))
        lin(nt, tc.outputs["Object"], mp.inputs["Vector"])
        w = nt.nodes.new("ShaderNodeTexWave"); w.wave_type = "BANDS"; w.bands_direction = "Y"; w.wave_profile = "SIN"
        w.inputs["Scale"].default_value = scale; w.inputs["Distortion"].default_value = distortion
        w.inputs["Detail"].default_value = 3; w.inputs["Detail Scale"].default_value = 1.5; w.inputs["Phase Offset"].default_value = phase
        lin(nt, mp.outputs[0], w.inputs["Vector"])
        return w.outputs["Fac"]
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, tc.outputs["Object"], sep.inputs[0])
    s1 = wave(0.055 * swell, 8, 5.0, 0.0)       # long swell rolling to the shore
    s2 = wave(0.11 * swell, -24, 4.0, 1.3)      # cross swell
    ch = node("ShaderNodeTexNoise", Scale=0.6, Detail=6.0, Roughness=0.62); lin(nt, tc.outputs["Object"], ch.inputs["Vector"])
    h = math_("MULTIPLY_ADD", s1, 0.55, math_("MULTIPLY_ADD", s2, 0.3, math_("MULTIPLY", ch.outputs["Fac"], 0.35)))
    # bump: big shapes + fine ripples
    rip = node("ShaderNodeTexNoise", Scale=4.0, Detail=8.0, Roughness=0.55); lin(nt, tc.outputs["Object"], rip.inputs["Vector"])
    b1 = node("ShaderNodeBump", Strength=0.55, Distance=0.6); lin(nt, h, b1.inputs["Height"])
    b2 = node("ShaderNodeBump", Strength=0.18, Distance=0.1); lin(nt, rip.outputs["Fac"], b2.inputs["Height"]); lin(nt, b1.outputs["Normal"], b2.inputs["Normal"])
    lin(nt, b2.outputs["Normal"], p.inputs["Normal"])
    # colour: depth from shore distance, then trough darker / crest lighter teal (light through the wave)
    depth = maprange(sep.outputs["Y"], shore_y, shore_y + 18)
    base = mix(depth, shallow, deep)
    crest = maprange(h, 0.45, 0.85)
    trough = maprange(h, 0.15, 0.4, 1.0, 0.0)
    lit = tuple(min(1.0, c * 1.8 + 0.03) for c in shallow)
    col = mix(math_("MULTIPLY", crest, 0.7), base, lit)
    col = mix(math_("MULTIPLY", trough, 0.5), col, tuple(c * 0.45 for c in deep))
    # foam: crest caps (broken by noise), shore wash lines, ocean-sim foam attribute
    fn = node("ShaderNodeTexNoise", Scale=1.8, Detail=10.0, Roughness=0.7); lin(nt, tc.outputs["Object"], fn.inputs["Vector"])
    caps = maprange(math_("MULTIPLY_ADD", fn.outputs["Fac"], 0.5, h), 1.2, 1.3)
    near = maprange(sep.outputs["Y"], shore_y + 7, shore_y)                       # 0 far, 1 at the shore
    wash_mp = nt.nodes.new("ShaderNodeMapping"); lin(nt, tc.outputs["Object"], wash_mp.inputs["Vector"])
    wash = nt.nodes.new("ShaderNodeTexWave"); wash.wave_type = "BANDS"; wash.bands_direction = "Y"; wash.wave_profile = "SAW"
    wash.inputs["Scale"].default_value = 0.16; wash.inputs["Distortion"].default_value = 7; wash.inputs["Detail"].default_value = 4
    lin(nt, wash_mp.outputs[0], wash.inputs["Vector"])
    wash_line = maprange(math_("MULTIPLY_ADD", fn.outputs["Fac"], 0.35, wash.outputs["Fac"]), 0.88, 1.02)
    shore_f = math_("MULTIPLY", math_("MAXIMUM", wash_line, math_("POWER", near, 3.0)), near)
    fa = nt.nodes.new("ShaderNodeAttribute"); fa.attribute_name = "foam"
    sim = maprange(math_("MULTIPLY", fa.outputs["Fac"], fn.outputs["Fac"]), 0.18, 0.4)
    foam = math_("MINIMUM", math_("MAXIMUM", math_("MAXIMUM", caps, shore_f), math_("MULTIPLY", sim, foam_amount)), 1.0)
    foam_soft = maprange(math_("MULTIPLY_ADD", fn.outputs["Fac"], 0.6, foam), 0.75, 1.15)
    col = mix(foam_soft, col, (0.86, 0.9, 0.93))
    lin(nt, col, p.inputs["Base Color"])
    lin(nt, maprange(foam_soft, 0.0, 1.0, 0.04, 0.55), p.inputs["Roughness"])
    p.inputs["Specular IOR Level"].default_value = 0.7
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_foam_strip(name):
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt); p.inputs["Base Color"].default_value = (0.9, 0.93, 0.96, 1); p.inputs["Roughness"].default_value = 0.5
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 3.0; nz.inputs["Detail"].default_value = 10
    lin(nt, tc.outputs["Object"], nz.inputs["Vector"])
    uvsep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, tc.outputs["Generated"], uvsep.inputs[0])
    edge = nt.nodes.new("ShaderNodeMapRange"); edge.inputs["From Min"].default_value = 1.0; edge.inputs["From Max"].default_value = 0.0
    lin(nt, uvsep.outputs["Y"], edge.inputs["Value"])
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"
    lin(nt, nz.outputs["Fac"], mul.inputs[0]); lin(nt, edge.outputs[0], mul.inputs[1])
    cut = nt.nodes.new("ShaderNodeMapRange"); cut.inputs["From Min"].default_value = 0.22; cut.inputs["From Max"].default_value = 0.42
    lin(nt, mul.outputs[0], cut.inputs["Value"]); lin(nt, cut.outputs[0], p.inputs["Alpha"])
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_fog_gate(name, color):
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    em = nt.nodes.new("ShaderNodeEmission"); em.inputs["Color"].default_value = (*color, 1); em.inputs["Strength"].default_value = 0.7
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mx = nt.nodes.new("ShaderNodeMixShader")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 2.5; nz.inputs["Detail"].default_value = 6
    nz.inputs["Distortion"].default_value = 1.5
    lin(nt, tc.outputs["Object"], nz.inputs["Vector"])
    mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["To Min"].default_value = 0.0; mr.inputs["To Max"].default_value = 0.6; mr.inputs["From Min"].default_value = 0.35; mr.inputs["From Max"].default_value = 0.7
    lin(nt, nz.outputs["Fac"], mr.inputs["Value"])
    lin(nt, mr.outputs[0], mx.inputs[0]); lin(nt, tr.outputs[0], mx.inputs[1]); lin(nt, em.outputs[0], mx.inputs[2])
    lin(nt, mx.outputs[0], out.inputs[0])
    return m

# ---------------------------------------------------------------- geometry helpers
def link(ob, coll): coll.objects.link(ob); return ob

def box(name, loc, size, rot=(0, 0, 0), mat=None, bevel=0.03, coll=None):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    ob.location = loc; ob.rotation_euler = rot
    if mat: me.materials.append(mat)
    if bevel > 0:
        b = ob.modifiers.new("bev", "BEVEL"); b.width = bevel; b.segments = 1; b.limit_method = "NONE"
    return link(ob, coll or ENV)

def cylinder(name, loc, r, h, mat, verts=10, r2=None, rot=(0, 0, 0), coll=None, bevel=0.0):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r if r2 is None else r2, depth=h)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); ob.location = loc; ob.rotation_euler = rot
    me.materials.append(mat)
    if bevel > 0:
        b = ob.modifiers.new("bev", "BEVEL"); b.width = bevel; b.segments = 1
    return link(ob, coll or ENV)

def rock(name, loc, size, mat, sub=2, rough=0.35, coll=None, seed=0):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    r2 = random.Random(seed)
    off = Vector((r2.uniform(0, 100), r2.uniform(0, 100), r2.uniform(0, 100)))
    from mathutils import noise
    for v in bm.verts:
        n = noise.noise(v.co * 1.3 + off)
        v.co *= 1.0 + rough * n
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = False
    ob = bpy.data.objects.new(name, me); ob.location = loc
    ob.rotation_euler = (r2.uniform(-0.2, 0.2), r2.uniform(-0.2, 0.2), r2.uniform(0, 6.28))
    me.materials.append(mat)
    return link(ob, coll or ENV)

def ashlar(a, b, h, mat, thick=WALL_T, course=(0.38, 0.55), blen=(0.55, 1.25), openings=(), top_mat=None,
           crenel=None, broken=0.0, inset=0.035, base_z=0.0):
    """Dry-laid block wall along a->b (XY). openings: list of (t0, t1) metres along the wall that stay empty."""
    a, b = Vector(a), Vector(b)
    d = (b - a); L = d.length; d.normalize()
    yaw = math.atan2(d.y, d.x)
    nrm = Vector((-d.y, d.x))
    z = base_z
    row = 0
    while z < h - 0.05:
        ch = min(R(*course), h - z)
        t = R(-0.4, 0.0) if row % 2 else 0.0
        while t < L:
            bl = R(*blen)
            t0, t1 = max(t, 0), min(t + bl, L)
            t = t + bl + 0.02
            if t1 - t0 < 0.15: continue
            if any(t1 > o0 and t0 < o1 for o0, o1 in openings): continue
            # broken top: drop blocks near the top in a noisy way
            if broken > 0 and z > h * 0.45 and rng.random() < broken * (z / h): continue
            mid = a + d * ((t0 + t1) / 2) + nrm * R(-inset, inset)
            box("blk", (mid.x, mid.y, z + ch / 2), (t1 - t0, thick + R(-0.04, 0.04), ch - 0.015),
                (R(-0.01, 0.01), R(-0.01, 0.01), yaw + R(-0.012, 0.012)), mat, bevel=0.035)
        z += ch; row += 1
    if top_mat:
        t = 0.0
        while t < L:
            bl = R(0.9, 1.6); t0, t1 = t, min(t + bl, L); t += bl + 0.02
            if any(t1 > o0 and t0 < o1 for o0, o1 in openings): continue
            mid = a + d * ((t0 + t1) / 2)
            box("cap", (mid.x, mid.y, z + 0.08), (t1 - t0, thick + 0.12, 0.16), (0, 0, yaw), top_mat, bevel=0.04)
        z += 0.16
    if crenel:
        cw, ch, gap = crenel
        t = 0.2
        while t < L - cw:
            if not any(t + cw > o0 and t < o1 for o0, o1 in openings):
                mid = a + d * (t + cw / 2)
                box("merlon", (mid.x, mid.y, z + ch / 2), (cw, thick * 0.9, ch), (0, 0, yaw), mat, bevel=0.04)
            t += cw + gap
    return z

def pillar(loc, h, w, mat, cap_mat=None):
    x, y = loc
    box("plinth", (x, y, 0.18), (w + 0.25, w + 0.25, 0.36), (0, 0, 0), cap_mat or mat, bevel=0.05)
    z = 0.36
    while z < h - 0.1:
        ch = min(R(0.45, 0.6), h - z)
        box("drum", (x, y, z + ch / 2), (w, w, ch - 0.02), (0, 0, R(-0.03, 0.03)), mat, bevel=0.04)
        z += ch
    box("pcap", (x, y, z + 0.11), (w + 0.2, w + 0.2, 0.22), (0, 0, 0), cap_mat or mat, bevel=0.05)
    return z + 0.22

def arch(center, span, spring, depth, mat, pointed=False, n=9, yaw=0.0, missing=()):
    """Voussoir arch over an opening of width span, springing at height spring, facing along Y (yaw rotates)."""
    cx, cy = center
    r = span / 2
    rot = Euler((0, 0, yaw))
    for i in range(n):
        if i in missing: continue
        a0 = math.pi * i / n; a1 = math.pi * (i + 1) / n; am = (a0 + a1) / 2
        if pointed:
            # two arcs centred at +-r*0.35 give a gothic point
            k = 0.35 * r
            if am < math.pi / 2:
                px, pz = -k + (r + k) * math.cos(am), (r + k) * math.sin(am) * 0.95
            else:
                px, pz = k + (r + k) * math.cos(am), (r + k) * math.sin(am) * 0.95
            rr = r + k
        else:
            px, pz = r * math.cos(am), r * math.sin(am); rr = r
        seg = rr * (a1 - a0) * 1.02
        off = Vector((px, 0, 0)); off.rotate(rot)
        ob = box("vous", (cx + off.x, cy + off.y, spring + pz), (seg, depth, 0.42), (0, 0, 0), mat, bevel=0.03)
        # long side tangent to the curve: rotation about local Y, then the wall yaw
        ob.rotation_euler = Euler((0, math.pi / 2 - am, 0)); ob.rotation_euler.rotate(rot)

def ground_grid(name, x0, x1, y0, y1, step, mat, mask_fn, z_fn=None, coll=None):
    nx, ny = int((x1 - x0) / step), int((y1 - y0) / step)
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    verts = {}
    for j in range(ny + 1):
        for i in range(nx + 1):
            x, y = x0 + i * step, y0 + j * step
            verts[i, j] = bm.verts.new((x, y, z_fn(x, y) if z_fn else 0.0))
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((verts[i, j], verts[i + 1, j], verts[i + 1, j + 1], verts[i, j + 1]))
    lay = bm.loops.layers.color.new("mask")
    for f in bm.faces:
        for lp in f.loops:
            r, g = mask_fn(lp.vert.co.x, lp.vert.co.y)
            lp[lay] = (r, g, 0, 1)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); me.materials.append(mat)
    return link(ob, coll or ENV)

from mathutils import noise as N
def fbm(x, y, s=1.0, o=0.0):
    return N.noise(Vector((x * s + o, y * s - o, o * 0.37)))

def wall_dist(x, y):
    return min(RX - abs(x), RY - abs(y))

def sea(level, shore_y, deep, shallow, wave_scale=0.7, choppy=1.1, foam=0.35):
    me = bpy.data.meshes.new("Sea"); bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=300, y_segments=160, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * 120, v.co.y * 50 + shore_y + 48, 0))
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    ob = bpy.data.objects.new("Sea", me); ob.location.z = level
    me.materials.append(mat_sea("Sea", deep, shallow, shore_y))
    m = ob.modifiers.new("ocean", "OCEAN")
    m.geometry_mode = "DISPLACE"; m.resolution = 14; m.viewport_resolution = 6
    m.spatial_size = 40; m.wave_scale = wave_scale; m.choppiness = choppy; m.wind_velocity = 14
    m.wave_alignment = 0.35; m.wave_direction = math.radians(-90 + 15)
    m.use_foam = True; m.foam_layer_name = "foam"; m.foam_coverage = foam; m.time = 3.0
    m.random_seed = 7
    return link(ob, FX)

def foam_strip(y0, x0, x1, level, width=3.0):
    me = bpy.data.meshes.new("Foam"); bm = bmesh.new()
    nx = 120
    vs = []
    for j in range(2):
        row = []
        for i in range(nx + 1):
            x = x0 + (x1 - x0) * i / nx
            y = y0 + j * width + fbm(x, 0, 0.4, 3.0) * 0.8
            row.append(bm.verts.new((x, y, level + 0.08)))
        vs.append(row)
    for i in range(nx):
        bm.faces.new((vs[0][i], vs[0][i + 1], vs[1][i + 1], vs[1][i]))
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("Foam", me); me.materials.append(mat_foam_strip("Foam"))
    return link(ob, FX)

def torch_light(loc, color=(1.0, 0.55, 0.22), power=180, flame=True, radius=0.1):
    ld = bpy.data.lights.new("torch", "POINT"); ld.energy = power; ld.color = color; ld.shadow_soft_size = radius
    lo = bpy.data.objects.new("torch", ld); lo.location = loc; FX.objects.link(lo)
    if flame:
        fm = mat_flat("flame", (1, 0.5, 0.15), emit=(1.0, 0.5, 0.12), emit_str=40)
        rock("flame", (loc[0], loc[1], loc[2] - 0.05), (0.09, 0.09, 0.18), fm, sub=1, rough=0.2, coll=FX)

def brazier(x, y, mat_iron, h=0.9):
    cylinder("brz_leg", (x, y, h / 2), 0.07, h, mat_iron, verts=6)
    cylinder("brz_bowl", (x, y, h + 0.12), 0.38, 0.26, mat_iron, verts=10, r2=0.22, rot=(math.pi, 0, 0))
    coal = mat_flat("coal", (0.1, 0.05, 0.02), emit=(1.0, 0.35, 0.08), emit_str=12)
    cylinder("brz_coal", (x, y, h + 0.24), 0.33, 0.04, coal, verts=10)
    torch_light((x, y, h + 0.7), power=320, radius=0.25)

def wall_torch(x, y, z, face, iron):
    box("sconce", (x, y, z - 0.15), (0.1, 0.1, 0.35), (0, 0, 0), iron, bevel=0.0)
    torch_light((x + face[0] * 0.15, y + face[1] * 0.15, z + 0.25), power=220)

def pine(x, y, h, mat_n, mat_t, snow=None):
    cylinder("trunk", (x, y, h * 0.15), 0.12, h * 0.3, mat_t, verts=6)
    tiers = 5
    for k in range(tiers):
        f = k / (tiers - 1)
        r = h * (0.3 - f * 0.2) * R(0.9, 1.1); zz = h * (0.18 + f * 0.62)
        t = cylinder("tier", (x + R(-0.05, 0.05), y + R(-0.05, 0.05), zz + h * 0.09), r, h * 0.2, mat_n, verts=9, r2=r * 0.25,
                     rot=(R(-0.06, 0.06), R(-0.06, 0.06), R(0, 1)))
        if snow and k % 2 == 0:
            cylinder("tsnow", (x, y, zz + h * 0.15), r * 0.55, h * 0.06, snow, verts=9, r2=r * 0.2, rot=(0, 0, R(0, 1)))
    cylinder("tip", (x, y, h * 0.95), h * 0.06, h * 0.14, mat_n, verts=7, r2=0.0)

def rubble(x, y, n, mat, spread=1.0, size=(0.12, 0.35), seed=0):
    for i in range(n):
        s = R(*size)
        rock("rubble", (x + R(-spread, spread), y + R(-spread, spread), s * 0.35), (s * R(0.8, 1.4), s, s * R(0.5, 0.8)), mat, sub=1,
             rough=0.25, seed=seed + i)

def broken_column(x, y, h, mat, base_mat, r=0.5, lean=0.0, seed=0):
    """Plinth + fluted-ish drums up to h, the top drum tilted, a fallen drum and rubble beside it."""
    box("col_base", (x, y, 0.2), (r * 2.6, r * 2.6, 0.4), (0, 0, R(-0.1, 0.1)), base_mat, bevel=0.05)
    z = 0.4; i = 0
    while z < h:
        dh = min(R(0.45, 0.6), h - z + 0.05)
        top = z + dh >= h
        cylinder("drum", (x, y, z + dh / 2), r, dh - 0.03, mat, verts=12,
                 rot=(R(-0.08, 0.08) if top else 0, R(-0.08, 0.08) if top else 0, R(0, 1)), bevel=0.03)
        z += dh; i += 1
    a = R(0, 6.28)
    cylinder("drum_fallen", (x + math.cos(a) * 1.3, y + math.sin(a) * 1.3, r * 0.95), r, 0.55, mat, verts=12,
             rot=(math.radians(90), 0, a + math.radians(90)), bevel=0.03)
    rubble(x + math.cos(a) * 0.8, y + math.sin(a) * 0.8, 6, mat, spread=0.7, seed=seed)

def floor_ring(x, y, r_out, r_in, mat, segs=32):
    """Inlaid stone ring (seal / compass) flush with the floor: a thin annulus a few mm above it."""
    me = bpy.data.meshes.new("ring"); bm = bmesh.new()
    outer = [bm.verts.new((x + math.cos(2 * math.pi * k / segs) * r_out, y + math.sin(2 * math.pi * k / segs) * r_out, 0.006)) for k in range(segs)]
    inner = [bm.verts.new((x + math.cos(2 * math.pi * k / segs) * r_in, y + math.sin(2 * math.pi * k / segs) * r_in, 0.006)) for k in range(segs)]
    for k in range(segs):
        k2 = (k + 1) % segs
        bm.faces.new((outer[k], outer[k2], inner[k2], inner[k]))
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("ring", me); me.materials.append(mat)
    return link(ob, ENV)

def cliff(name, loc, size, mat, seed=0, sub=3, step=0.55, rough=0.32, yaw=None, coll=None):
    """Stratified rock: icosphere + two noise octaves, then terraced in Z (flat ledges, steep risers), flat bottom. Faceted."""
    from mathutils import noise
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    r2 = random.Random(seed)
    off = Vector((r2.uniform(0, 100), r2.uniform(0, 100), r2.uniform(0, 100)))
    sx, sy, sz = size
    for v in bm.verts:
        d = v.co.copy()
        n = noise.noise(d * 1.1 + off) * rough + noise.noise(d * 3.1 + off * 1.7) * rough * 0.35
        d *= 1.0 + n
        x, y, z = d.x * sx, d.y * sy, d.z * sz
        if z > 0:
            t = z / step; f = t - math.floor(t)
            k = max(0.0, min(1.0, (f - 0.55) / 0.45)); k = k * k * (3 - 2 * k)
            z = (math.floor(t) + k) * step
            shrink = 1.0 - 0.08 * (z / max(sz, 0.01))   # slight batter: cliffs lean back with height
            x *= shrink; y *= shrink
        else:
            z *= 0.15
        v.co = Vector((x, y, z))
    bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = False
    ob = bpy.data.objects.new(name, me); ob.location = loc
    ob.rotation_euler = (0, 0, r2.uniform(0, 6.28) if yaw is None else yaw)
    me.materials.append(mat)
    return link(ob, coll or ENV)

def mat_cliff(name, c_dark, c_light, snow=0.0, moss=None):
    """Rock with horizontal strata bands (wave along Z) + noise; snow on ledges (normal Z) when snow > 0."""
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wv = nt.nodes.new("ShaderNodeTexWave"); wv.wave_type = "BANDS"; wv.bands_direction = "Z"
    wv.inputs["Scale"].default_value = 1.1; wv.inputs["Distortion"].default_value = 4; wv.inputs["Detail"].default_value = 4
    lin(nt, tc.outputs["Generated"], wv.inputs["Vector"])
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 3; nz.inputs["Detail"].default_value = 8
    lin(nt, tc.outputs["Object"], nz.inputs["Vector"])
    mixf = nt.nodes.new("ShaderNodeMath"); mixf.operation = "MULTIPLY_ADD"; mixf.inputs[1].default_value = 0.45
    lin(nt, wv.outputs["Fac"], mixf.inputs[0]); lin(nt, nz.outputs["Fac"], mixf.inputs[2])
    oi = nt.nodes.new("ShaderNodeObjectInfo")
    mixr = nt.nodes.new("ShaderNodeMath"); mixr.operation = "MULTIPLY_ADD"; mixr.inputs[1].default_value = 0.25
    lin(nt, oi.outputs["Random"], mixr.inputs[0]); lin(nt, mixf.outputs[0], mixr.inputs[2])
    sc = nt.nodes.new("ShaderNodeMath"); sc.operation = "MULTIPLY"; sc.inputs[1].default_value = 0.6
    lin(nt, mixr.outputs[0], sc.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*c_dark, 1); ramp.color_ramp.elements[1].color = (*c_light, 1)
    lin(nt, sc.outputs[0], ramp.inputs["Fac"])
    col = ramp.outputs["Color"]
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, geo.outputs["Normal"], sep.inputs[0])
    if moss:
        mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 0.5; mr.inputs["From Max"].default_value = 0.85
        lin(nt, sep.outputs["Z"], mr.inputs["Value"])
        mm = nt.nodes.new("ShaderNodeMix"); mm.data_type = "RGBA"
        lin(nt, mr.outputs[0], mm.inputs["Factor"]); lin(nt, col, mm.inputs[6]); mm.inputs[7].default_value = (*moss, 1)
        col = mm.outputs[2]
    if snow > 0:
        sn = nt.nodes.new("ShaderNodeTexNoise"); sn.inputs["Scale"].default_value = 5; sn.inputs["Detail"].default_value = 6
        lin(nt, tc.outputs["Object"], sn.inputs["Vector"])
        add = nt.nodes.new("ShaderNodeMath"); add.operation = "MULTIPLY_ADD"; add.inputs[1].default_value = 0.4
        lin(nt, sn.outputs["Fac"], add.inputs[0]); lin(nt, sep.outputs["Z"], add.inputs[2])
        mr = nt.nodes.new("ShaderNodeMapRange")
        mr.inputs["From Min"].default_value = 1.2 - 0.35 * snow; mr.inputs["From Max"].default_value = 1.26 - 0.35 * snow
        lin(nt, add.outputs[0], mr.inputs["Value"])
        mm = nt.nodes.new("ShaderNodeMix"); mm.data_type = "RGBA"
        lin(nt, mr.outputs[0], mm.inputs["Factor"]); lin(nt, col, mm.inputs[6]); mm.inputs[7].default_value = (0.78, 0.82, 0.88, 1)
        col = mm.outputs[2]
    lin(nt, col, p.inputs["Base Color"]); p.inputs["Roughness"].default_value = 0.85
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.4
    lin(nt, nz.outputs["Fac"], bump.inputs["Height"]); lin(nt, bump.outputs["Normal"], p.inputs["Normal"])
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_torn_cloth(name, c1, c2):
    m = mat_stone(name, c1, c2, rough=0.95, scale=5)
    nt = m.node_tree; p = [n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"][0]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 1.6; nz.inputs["Detail"].default_value = 10
    lin(nt, tc.outputs["Object"], nz.inputs["Vector"])
    mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 0.36; mr.inputs["From Max"].default_value = 0.4
    lin(nt, nz.outputs["Fac"], mr.inputs["Value"]); lin(nt, mr.outputs[0], p.inputs["Alpha"])
    return m

def mat_planks(name, c1, c2):
    """Hull planking: bands along the section (Generated Z) + per-band tint, tar-dark seams."""
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wv = nt.nodes.new("ShaderNodeTexWave"); wv.wave_type = "BANDS"; wv.bands_direction = "Y"; wv.wave_profile = "SAW"
    wv.inputs["Scale"].default_value = 3.2; wv.inputs["Distortion"].default_value = 0.4
    lin(nt, tc.outputs["UV"], wv.inputs["Vector"])
    seam = nt.nodes.new("ShaderNodeMapRange"); seam.inputs["From Min"].default_value = 0.0; seam.inputs["From Max"].default_value = 0.08
    lin(nt, wv.outputs["Fac"], seam.inputs["Value"])
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 6; nz.inputs["Detail"].default_value = 6
    nz_v = nt.nodes.new("ShaderNodeMapping"); nz_v.inputs["Scale"].default_value = (0.4, 6.0, 1.0)
    lin(nt, tc.outputs["UV"], nz_v.inputs["Vector"]); lin(nt, nz_v.outputs[0], nz.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*c1, 1); ramp.color_ramp.elements[1].color = (*c2, 1)
    lin(nt, nz.outputs["Fac"], ramp.inputs["Fac"])
    mm = nt.nodes.new("ShaderNodeMix"); mm.data_type = "RGBA"; mm.blend_type = "MULTIPLY"
    lin(nt, seam.outputs[0], mm.inputs["Factor"]); mm.inputs["Factor"].default_value = 1.0
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    lin(nt, seam.outputs[0], inv.inputs[1]); lin(nt, inv.outputs[0], mm.inputs["Factor"])
    lin(nt, ramp.outputs["Color"], mm.inputs[6]); mm.inputs[7].default_value = (0.15, 0.12, 0.1, 1)
    lin(nt, mm.outputs[2], p.inputs["Base Color"]); p.inputs["Roughness"].default_value = 0.85
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.5
    lin(nt, seam.outputs[0], bump.inputs["Height"]); lin(nt, bump.outputs["Normal"], p.inputs["Normal"])
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def hull(loc, length, beam, depth, mat, rib_mat, tilt=25.0, yaw=0.0, seed=0, broken=0.35):
    """Lofted U-section boat hull (pointed bow, round stern), lying tilted and half sunk; a broken patch of planks
    on the high side shows bare ribs. One mesh + a ribs mesh."""
    from mathutils import noise
    nsec, nring = 22, 12
    me = bpy.data.meshes.new("hull"); bm = bmesh.new()
    uv_l = bm.loops.layers.uv.new("UVMap")
    grid = []
    for i in range(nsec + 1):
        t = i / nsec                                   # 0 stern .. 1 bow
        y = (t - 0.5) * length
        w = beam * 0.5 * (math.sin(math.pi * min(1.0, t * 1.25)) ** 0.6 if t < 0.8 else math.sin(math.pi * min(1.0, t * 1.25)) ** 0.6)
        w = max(w, 0.04)
        sheer = depth * (1.0 + 0.25 * (2 * t - 1) ** 2)
        row = []
        for j in range(nring + 1):
            a = -math.pi / 2 + math.pi * j / nring       # -90 (port rail) .. +90 (starboard rail)
            x = w * math.sin(a)
            z = sheer - depth * math.cos(a) ** 0.8
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    r2 = random.Random(seed)
    off = r2.uniform(0, 50)
    for i in range(nsec):
        for j in range(nring):
            # broken planks: high (starboard) side, middle of the hull
            if j > nring * 0.55 and 0.25 < i / nsec < 0.75 and noise.noise(Vector((i * 0.35 + off, j * 0.5, 0.0))) > -broken + 0.3:
                continue
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            for lp, (u, v) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                lp[uv_l].uv = (u / nsec, v / nring)
    bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = True
    ob = bpy.data.objects.new("hull", me); me.materials.append(mat)
    ob.location = loc; ob.rotation_euler = (0, math.radians(tilt), math.radians(yaw))
    sol = ob.modifiers.new("thick", "SOLIDIFY"); sol.thickness = 0.08
    link(ob, ENV)
    # ribs: a frame every ~0.6 m following the section, visible where the planks are gone
    for i in range(2, nsec - 1, 2):
        t = i / nsec; y = (t - 0.5) * length
        w = max(beam * 0.5 * math.sin(math.pi * min(1.0, t * 1.25)) ** 0.6, 0.04)
        sheer = depth * (1.0 + 0.25 * (2 * t - 1) ** 2)
        for j in range(nring):
            a0 = -math.pi / 2 + math.pi * j / nring; a1 = -math.pi / 2 + math.pi * (j + 1) / nring
            p0 = Vector((w * math.sin(a0) * 0.97, y, sheer - depth * math.cos(a0) ** 0.8))
            p1 = Vector((w * math.sin(a1) * 0.97, y, sheer - depth * math.cos(a1) ** 0.8))
            mid = (p0 + p1) / 2; d = p1 - p0
            rb = box("rib", (0, 0, 0), (0.12, 0.12, d.length + 0.04), (0, 0, 0), rib_mat, bevel=0.0)
            ang = math.atan2(d.x, d.z)
            rb.parent = ob; rb.location = mid; rb.rotation_euler = (0, ang, 0)
    return ob

# ---------------------------------------------------------------- characters (scale + readability)
def _import(path):
    before = set(bpy.data.objects)
    try:
        if path.lower().endswith(".glb"): bpy.ops.import_scene.gltf(filepath=path)
        else: bpy.ops.import_scene.fbx(filepath=path)
    except Exception as e:
        print("import failed", path, e); return []
    new = [o for o in bpy.data.objects if o not in before]
    for o in list(new):
        if o.name.startswith("Icosphere"):          # stray helper sphere inside the Knight GLB
            bpy.data.objects.remove(o); new.remove(o); continue
        for c in o.users_collection: c.objects.unlink(o)
        CHARS.objects.link(o)
    return new

def _place(new, x, y, yaw, action=None, frame=0):
    """Parent the imported roots to an empty at (x, y): the clips key the root transform, so a plain location would be overwritten."""
    holder = bpy.data.objects.new("holder", None); holder.location = (x, y, 0); holder.rotation_euler.z = math.radians(yaw)
    CHARS.objects.link(holder)
    for o in new:
        if o.parent is None: o.parent = holder
        if o.type == "ARMATURE" and action and action in bpy.data.actions:
            o.animation_data_create()
            act = bpy.data.actions[action]
            o.animation_data.action = act
            try:
                if act.slots: o.animation_data.action_slot = act.slots[0]
            except Exception as e: print("slot", e)

def import_chars():
    if KNIGHT:
        new = _import(KNIGHT)
        _place(new, 0.6, -5.5, 200, action="AimIdle")
    if ZOMBIE:
        zs = [(-3.5, 3.0, 150), (3.8, 5.5, 210), (-1.2, 8.5, 185), (5.0, -0.5, 235), (-5.8, -2.8, 125), (1.8, 11.0, 175), (-0.8, 0.8, 190)]
        for (x, y, yaw) in zs:
            new = _import(ZOMBIE)
            acts = [a.name for a in bpy.data.actions if "running" in a.name or "clip0" in a.name]
            _place(new, x, y, yaw, action=acts[-1] if acts else None)
    scene.frame_set(12)

# ---------------------------------------------------------------- world, camera, render
def world(top, horizon, strength=1.0, fog=0.0, fog_col=(0.8, 0.85, 0.9)):
    w = bpy.data.worlds.new("W"); scene.world = w; w.use_nodes = True
    nt = w.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground"); bg.inputs["Strength"].default_value = strength
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, tc.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (*horizon, 1)
    ramp.color_ramp.elements[1].position = 0.6; ramp.color_ramp.elements[1].color = (*top, 1)
    lin(nt, sep.outputs["Z"], ramp.inputs["Fac"])
    # cloud deck projected on a plane above: gives the sea something to reflect (broken bright / dark patches)
    zc = nt.nodes.new("ShaderNodeMath"); zc.operation = "ADD"; zc.inputs[1].default_value = 0.08
    lin(nt, sep.outputs["Z"], zc.inputs[0])
    dv = nt.nodes.new("ShaderNodeVectorMath"); dv.operation = "DIVIDE"
    comb = nt.nodes.new("ShaderNodeCombineXYZ"); lin(nt, zc.outputs[0], comb.inputs[0]); lin(nt, zc.outputs[0], comb.inputs[1]); lin(nt, zc.outputs[0], comb.inputs[2])
    lin(nt, tc.outputs["Generated"], dv.inputs[0]); lin(nt, comb.outputs[0], dv.inputs[1])
    cn = nt.nodes.new("ShaderNodeTexNoise"); cn.inputs["Scale"].default_value = 1.4; cn.inputs["Detail"].default_value = 7
    cn.inputs["Roughness"].default_value = 0.6
    lin(nt, dv.outputs[0], cn.inputs["Vector"])
    cr = nt.nodes.new("ShaderNodeMapRange"); cr.inputs["From Min"].default_value = 0.42; cr.inputs["From Max"].default_value = 0.68
    lin(nt, cn.outputs["Fac"], cr.inputs["Value"])
    cm = nt.nodes.new("ShaderNodeMix"); cm.data_type = "RGBA"
    lin(nt, cr.outputs[0], cm.inputs["Factor"]); lin(nt, ramp.outputs["Color"], cm.inputs[6])
    cm.inputs[7].default_value = (*[min(1.0, c * 1.7 + 0.08) for c in horizon], 1)
    lin(nt, cm.outputs[2], bg.inputs["Color"])
    lin(nt, bg.outputs[0], out.inputs["Surface"])
    if fog > 0 and not NOFOG:
        vs = nt.nodes.new("ShaderNodeVolumeScatter"); vs.inputs["Density"].default_value = fog
        vs.inputs["Color"].default_value = (*fog_col, 1); vs.inputs["Anisotropy"].default_value = 0.2
        lin(nt, vs.outputs[0], out.inputs["Volume"])

def sun(rot_deg, strength, color, angle=3.0):
    ld = bpy.data.lights.new("sun", "SUN"); ld.energy = strength; ld.color = color; ld.angle = math.radians(angle)
    lo = bpy.data.objects.new("sun", ld); lo.rotation_euler = [math.radians(a) for a in rot_deg]
    FX.objects.link(lo)

def camera(look=(0, 1.0), view_width=20.0, pitch=55.0, fov_v=40.0):
    cd = bpy.data.cameras.new("Cam"); cd.sensor_fit = "VERTICAL"; cd.angle_y = math.radians(fov_v)
    cd.clip_end = 400
    co = bpy.data.objects.new("Cam", cd); scene.collection.objects.link(co); scene.camera = co
    aspect = RES[0] / RES[1]
    half_h = math.atan(math.tan(math.radians(fov_v) / 2) * aspect)
    dist = view_width * 0.5 / math.tan(half_h)
    pr = math.radians(pitch)
    co.location = (look[0], look[1] - dist * math.cos(pr), dist * math.sin(pr))
    co.rotation_euler = (math.radians(90 - pitch), 0, 0)

def render_setup():
    scene.render.resolution_x, scene.render.resolution_y = RES
    scene.render.resolution_percentage = 100
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = eng; break
        except TypeError:
            pass
    ee = scene.eevee
    for k, v in (("taa_render_samples", 64), ("use_raytracing", True), ("volumetric_end", 160.0), ("volumetric_tile_size", "4"),
                 ("use_shadows", True), ("shadow_ray_count", 2), ("fast_gi_method", "GLOBAL_ILLUMINATION"), ("use_fast_gi", True)):
        try: setattr(ee, k, v)
        except Exception as e: print("eevee", k, e)
    try:
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Medium High Contrast"
    except Exception as e: print("cm", e)
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = OUT

def stats():
    dg = bpy.context.evaluated_depsgraph_get()
    tris = {"Env": 0, "FX": 0, "Chars": 0}; objs = {"Env": 0, "FX": 0, "Chars": 0}; mats = {"Env": set(), "FX": set()}
    for coll in (ENV, FX, CHARS):
        for o in coll.objects:
            if o.type != "MESH": continue
            e = o.evaluated_get(dg); me = e.to_mesh()
            me.calc_loop_triangles(); tris[coll.name] += len(me.loop_triangles); objs[coll.name] += 1
            if coll.name in mats: mats[coll.name].update(m.name.split(".")[0] for m in me.materials if m)
            e.to_mesh_clear()
    print(f"STATS concept={CONCEPT} env_tris={tris['Env']} env_objs={objs['Env']} env_mats={len(mats['Env'])} "
          f"fx_tris={tris['FX']} char_tris={tris['Chars']}")

