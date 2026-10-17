# Survivor Souls - Chamber concepts v2: 36 x 60 m island chamber on the sea.
# Entry: open gap in the SW corner where a bridge arrives (no door). Exit: gate in the chamfered NE corner, facing NE,
# light pouring in through it; a bridge leaves NE over the sea into the haze.
# Run: blender -b --factory-startup -P chamber_v2.py -- <theme> <out.png> [knight.glb|-] [zombie.fbx|-] [w h]
import os, sys
_here = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(_here, "kit.py"), encoding="utf-8").read())

from mathutils import Matrix
THEME = CONCEPT
FLOOR_SKIP = globals().get("FLOOR_SKIP", lambda x, y: False)
GROUND_DIP = globals().get("GROUND_DIP", lambda x, y: 0.0)
EXTRA_BUILD = globals().get("EXTRA_BUILD", lambda T, mats: None)
RX, RY = 18.0, 30.0           # interior half size (36 x 60 m)
C = 7.5                        # corner chamfer (NE gate, SW entry)
WT = 1.0                       # wall thickness
GW = 4.6                       # gate opening width
SEA_Z = -2.6
S2 = math.sqrt(0.5)
N_OUT = Vector((S2, S2))       # NE outward normal
T_NE = Vector((-S2, S2))       # NE chamfer direction (from the east end to the north end)
G_NE = Vector((RX - C / 2, RY - C / 2))
G_SW = Vector((-RX + C / 2, -RY + C / 2))
YAW_NE = math.atan2(T_NE.y, T_NE.x)

def poly():
    return [Vector(p) for p in ((-RX + C, -RY), (RX, -RY), (RX, RY - C), (RX - C, RY), (-RX, RY), (-RX, -RY + C))]

def inside(x, y, m=0.0):
    if abs(x) > RX - m or abs(y) > RY - m: return False
    if x + y > RX + RY - C - m * 1.41: return False
    if x + y < -(RX + RY - C) + m * 1.41: return False
    return True

def edge_dist(x, y):
    return min(RX - abs(x), RY - abs(y), ((RX + RY - C) - (x + y)) * S2, ((RX + RY - C) + (x + y)) * S2)

def L(u, v, z=0.0, g=G_NE, t=T_NE, n=N_OUT):
    p = g + t * u + n * v
    return (p.x, p.y, z)

# ---------------------------------------------------------------- material helpers
def add_grime(m, h=1.3, dark=0.5, moss=None):
    """Darken (and optionally moss) the bottom of walls by world height: damp, dirty bases read as real masonry."""
    nt = m.node_tree
    p = [n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"][0]
    src = p.inputs["Base Color"].links[0].from_socket if p.inputs["Base Color"].links else None
    if src is None: return m
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, geo.outputs["Position"], sep.inputs[0])
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 1.5
    lin(nt, geo.outputs["Position"], nz.inputs["Vector"])
    add = nt.nodes.new("ShaderNodeMath"); add.operation = "MULTIPLY_ADD"; add.inputs[1].default_value = 0.6
    lin(nt, nz.outputs["Fac"], add.inputs[0]); lin(nt, sep.outputs["Z"], add.inputs[2])
    mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 0.2; mr.inputs["From Max"].default_value = h
    mr.inputs["To Min"].default_value = dark; mr.inputs["To Max"].default_value = 1.0
    lin(nt, add.outputs[0], mr.inputs["Value"])
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"; mul.inputs["Factor"].default_value = 1.0
    comb = nt.nodes.new("ShaderNodeCombineColor")
    for i in range(3): lin(nt, mr.outputs[0], comb.inputs[i])
    lin(nt, src, mul.inputs[6]); lin(nt, comb.outputs[0], mul.inputs[7])
    col = mul.outputs[2]
    if moss:
        mm = nt.nodes.new("ShaderNodeMix"); mm.data_type = "RGBA"
        inv = nt.nodes.new("ShaderNodeMapRange"); inv.inputs["From Min"].default_value = 0.25; inv.inputs["From Max"].default_value = 0.75
        inv.inputs["To Min"].default_value = 0.85; inv.inputs["To Max"].default_value = 0.0
        lin(nt, add.outputs[0], inv.inputs["Value"]); lin(nt, inv.outputs[0], mm.inputs["Factor"])
        lin(nt, col, mm.inputs[6]); mm.inputs[7].default_value = (*moss, 1)
        col = mm.outputs[2]
    lin(nt, col, p.inputs["Base Color"])
    return m

def mat_tiles(name, c1, c2, gloss=0.7, snow=False, moss_gap=None):
    """Tile mesh material: per-tile tint from face attribute 'trand', large stains, fine grain, mask R = snow, G = wet."""
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    tr = nt.nodes.new("ShaderNodeAttribute"); tr.attribute_name = "trand"
    st = nt.nodes.new("ShaderNodeTexNoise"); st.inputs["Scale"].default_value = 0.12; st.inputs["Detail"].default_value = 5
    lin(nt, tc.outputs["Object"], st.inputs["Vector"])
    fine = nt.nodes.new("ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 9; fine.inputs["Detail"].default_value = 9
    lin(nt, tc.outputs["Object"], fine.inputs["Vector"])
    f = nt.nodes.new("ShaderNodeMath"); f.operation = "MULTIPLY_ADD"; f.inputs[1].default_value = 0.6
    lin(nt, tr.outputs["Fac"], f.inputs[0]); lin(nt, fine.outputs["Fac"], f.inputs[2])
    f2 = nt.nodes.new("ShaderNodeMath"); f2.operation = "MULTIPLY_ADD"; f2.inputs[1].default_value = 0.5
    lin(nt, st.outputs["Fac"], f2.inputs[0]); lin(nt, f.outputs[0], f2.inputs[2])
    sc = nt.nodes.new("ShaderNodeMapRange"); sc.inputs["From Min"].default_value = 0.55; sc.inputs["From Max"].default_value = 1.35
    lin(nt, f2.outputs[0], sc.inputs["Value"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*c1, 1); ramp.color_ramp.elements[1].color = (*c2, 1)
    lin(nt, sc.outputs[0], ramp.inputs["Fac"])
    col = ramp.outputs["Color"]
    vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "mask"
    sp = nt.nodes.new("ShaderNodeSeparateColor"); lin(nt, vc.outputs["Color"], sp.inputs[0])
    if snow:
        en = nt.nodes.new("ShaderNodeTexNoise"); en.inputs["Scale"].default_value = 1.4; en.inputs["Detail"].default_value = 8
        lin(nt, tc.outputs["Object"], en.inputs["Vector"])
        ad = nt.nodes.new("ShaderNodeMath"); ad.operation = "MULTIPLY_ADD"; ad.inputs[1].default_value = 0.45
        lin(nt, en.outputs["Fac"], ad.inputs[0]); lin(nt, sp.outputs["Red"], ad.inputs[2])
        stp = nt.nodes.new("ShaderNodeMapRange"); stp.inputs["From Min"].default_value = 0.68; stp.inputs["From Max"].default_value = 0.74
        lin(nt, ad.outputs[0], stp.inputs["Value"])
        sm = nt.nodes.new("ShaderNodeMix"); sm.data_type = "RGBA"
        lin(nt, stp.outputs[0], sm.inputs["Factor"]); lin(nt, col, sm.inputs[6]); sm.inputs[7].default_value = (0.8, 0.84, 0.9, 1)
        col = sm.outputs[2]
    # wet darkening (G)
    wm = nt.nodes.new("ShaderNodeMix"); wm.data_type = "RGBA"; wm.blend_type = "MULTIPLY"
    lin(nt, sp.outputs["Green"], wm.inputs["Factor"]); lin(nt, col, wm.inputs[6]); wm.inputs[7].default_value = (0.55, 0.57, 0.62, 1)
    lin(nt, wm.outputs[2], p.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = gloss; rr.inputs["To Max"].default_value = 0.12
    lin(nt, sp.outputs["Green"], rr.inputs["Value"]); lin(nt, rr.outputs[0], p.inputs["Roughness"])
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.25
    lin(nt, fine.outputs["Fac"], bump.inputs["Height"]); lin(nt, bump.outputs["Normal"], p.inputs["Normal"])
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_ground(name, kind, c1, c2, extra=None):
    """Natural ground: grass / sand / dirt / ash / gravel / snow; mask R = snow (crisp), G = wet."""
    m = bpy.data.materials.new(name); nt, out = nodes_of(m)
    p = principled(nt)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    big = nt.nodes.new("ShaderNodeTexNoise"); big.inputs["Scale"].default_value = 0.18; big.inputs["Detail"].default_value = 6
    lin(nt, tc.outputs["Object"], big.inputs["Vector"])
    fine = nt.nodes.new("ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 6 if kind != "grass" else 14; fine.inputs["Detail"].default_value = 10
    lin(nt, tc.outputs["Object"], fine.inputs["Vector"])
    f = nt.nodes.new("ShaderNodeMath"); f.operation = "MULTIPLY_ADD"; f.inputs[1].default_value = 0.5
    lin(nt, fine.outputs["Fac"], f.inputs[0]); lin(nt, big.outputs["Fac"], f.inputs[2])
    sc = nt.nodes.new("ShaderNodeMapRange"); sc.inputs["From Min"].default_value = 0.55; sc.inputs["From Max"].default_value = 1.05
    lin(nt, f.outputs[0], sc.inputs["Value"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*c1, 1); ramp.color_ramp.elements[1].color = (*c2, 1)
    lin(nt, sc.outputs[0], ramp.inputs["Fac"])
    col = ramp.outputs["Color"]
    hgt = fine.outputs["Fac"]
    if kind in ("gravel", "sand"):
        vor = nt.nodes.new("ShaderNodeTexVoronoi"); vor.inputs["Scale"].default_value = 7 if kind == "gravel" else 3
        lin(nt, tc.outputs["Object"], vor.inputs["Vector"])
        peb = nt.nodes.new("ShaderNodeMapRange"); peb.inputs["From Min"].default_value = 0.0; peb.inputs["From Max"].default_value = 0.35
        peb.inputs["To Min"].default_value = 1.0; peb.inputs["To Max"].default_value = 0.0
        lin(nt, vor.outputs["Distance"], peb.inputs["Value"])
        pm = nt.nodes.new("ShaderNodeMix"); pm.data_type = "RGBA"; pm.blend_type = "MULTIPLY"
        pmf = nt.nodes.new("ShaderNodeMath"); pmf.operation = "MULTIPLY"; pmf.inputs[1].default_value = 0.35 if kind == "gravel" else 0.12
        lin(nt, peb.outputs[0], pmf.inputs[0]); lin(nt, pmf.outputs[0], pm.inputs["Factor"])
        lin(nt, col, pm.inputs[6]); lin(nt, vor.outputs["Color"], pm.inputs[7])
        col = pm.outputs[2]; hgt = peb.outputs[0]
    if kind == "ash" and extra:
        # glowing cracks: voronoi edges masked by large noise
        vor = nt.nodes.new("ShaderNodeTexVoronoi"); vor.feature = "DISTANCE_TO_EDGE"; vor.inputs["Scale"].default_value = 0.35
        vd = nt.nodes.new("ShaderNodeTexNoise"); vd.inputs["Scale"].default_value = 0.6; vd.inputs["Detail"].default_value = 4
        lin(nt, tc.outputs["Object"], vd.inputs["Vector"])
        vadd = nt.nodes.new("ShaderNodeVectorMath"); vadd.operation = "ADD"
        lin(nt, tc.outputs["Object"], vadd.inputs[0]); lin(nt, vd.outputs["Color"], vadd.inputs[1])
        lin(nt, vadd.outputs[0], vor.inputs["Vector"])
        cr = nt.nodes.new("ShaderNodeMapRange"); cr.inputs["From Min"].default_value = 0.035; cr.inputs["From Max"].default_value = 0.0
        lin(nt, vor.outputs["Distance"], cr.inputs["Value"])
        msk = nt.nodes.new("ShaderNodeMapRange"); msk.inputs["From Min"].default_value = 0.56; msk.inputs["From Max"].default_value = 0.66
        lin(nt, big.outputs["Fac"], msk.inputs["Value"])
        em = nt.nodes.new("ShaderNodeMath"); em.operation = "MULTIPLY"
        lin(nt, cr.outputs[0], em.inputs[0]); lin(nt, msk.outputs[0], em.inputs[1])
        ems = nt.nodes.new("ShaderNodeMath"); ems.operation = "MULTIPLY"; ems.inputs[1].default_value = extra
        lin(nt, em.outputs[0], ems.inputs[0])
        p.inputs["Emission Color"].default_value = (1.0, 0.32, 0.06, 1)
        lin(nt, ems.outputs[0], p.inputs["Emission Strength"])
    vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "mask"
    sp = nt.nodes.new("ShaderNodeSeparateColor"); lin(nt, vc.outputs["Color"], sp.inputs[0])
    en = nt.nodes.new("ShaderNodeTexNoise"); en.inputs["Scale"].default_value = 1.4; en.inputs["Detail"].default_value = 8
    lin(nt, tc.outputs["Object"], en.inputs["Vector"])
    ad = nt.nodes.new("ShaderNodeMath"); ad.operation = "MULTIPLY_ADD"; ad.inputs[1].default_value = 0.45
    lin(nt, en.outputs["Fac"], ad.inputs[0]); lin(nt, sp.outputs["Red"], ad.inputs[2])
    stp = nt.nodes.new("ShaderNodeMapRange"); stp.inputs["From Min"].default_value = 0.68; stp.inputs["From Max"].default_value = 0.74
    lin(nt, ad.outputs[0], stp.inputs["Value"])
    sm = nt.nodes.new("ShaderNodeMix"); sm.data_type = "RGBA"
    lin(nt, stp.outputs[0], sm.inputs["Factor"]); lin(nt, col, sm.inputs[6]); sm.inputs[7].default_value = (0.8, 0.84, 0.9, 1)
    wm = nt.nodes.new("ShaderNodeMix"); wm.data_type = "RGBA"; wm.blend_type = "MULTIPLY"
    lin(nt, sp.outputs["Green"], wm.inputs["Factor"]); lin(nt, sm.outputs[2], wm.inputs[6]); wm.inputs[7].default_value = (0.5, 0.52, 0.55, 1)
    lin(nt, wm.outputs[2], p.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.9; rr.inputs["To Max"].default_value = 0.2
    lin(nt, sp.outputs["Green"], rr.inputs["Value"]); lin(nt, rr.outputs[0], p.inputs["Roughness"])
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.35
    lin(nt, hgt, bump.inputs["Height"]); lin(nt, bump.outputs["Normal"], p.inputs["Normal"])
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def mat_sea2(name, deep, shallow, hx, hy, foam_amount=1.0, swell=1.0):
    """Mobile-style sea around a rectangular island: depth + shore wash from the box distance to the island."""
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
    dist = math_("MAXIMUM", math_("SUBTRACT", math_("ABSOLUTE", sep.outputs["X"]), hx), math_("SUBTRACT", math_("ABSOLUTE", sep.outputs["Y"]), hy))
    s1 = wave(0.022 * swell, 35, 6.0, 0.0)
    s2 = wave(0.045 * swell, -20, 5.0, 1.3)
    ch = node("ShaderNodeTexNoise", Scale=0.28, Detail=6.0, Roughness=0.62); lin(nt, tc.outputs["Object"], ch.inputs["Vector"])
    h = math_("MULTIPLY_ADD", s1, 0.55, math_("MULTIPLY_ADD", s2, 0.3, math_("MULTIPLY", ch.outputs["Fac"], 0.35)))
    rip = node("ShaderNodeTexNoise", Scale=4.0, Detail=8.0, Roughness=0.55); lin(nt, tc.outputs["Object"], rip.inputs["Vector"])
    b1 = node("ShaderNodeBump", Strength=0.55, Distance=0.6); lin(nt, h, b1.inputs["Height"])
    b2 = node("ShaderNodeBump", Strength=0.18, Distance=0.1); lin(nt, rip.outputs["Fac"], b2.inputs["Height"]); lin(nt, b1.outputs["Normal"], b2.inputs["Normal"])
    lin(nt, b2.outputs["Normal"], p.inputs["Normal"])
    depth = maprange(dist, 0.0, 22.0)
    base = mix(depth, shallow, deep)
    # thin crest lines along each swell (read from far away), light only a little; no big dark trough blotches
    crest1 = maprange(s1, 0.82, 0.97); crest2 = maprange(s2, 0.88, 0.98)
    crest = math_("MAXIMUM", crest1, math_("MULTIPLY", crest2, 0.6))
    lit = tuple(min(1.0, c * 1.7 + 0.03) for c in shallow)
    col = mix(math_("MULTIPLY", crest, 0.55), base, lit)
    fn = node("ShaderNodeTexNoise", Scale=1.8, Detail=10.0, Roughness=0.7); lin(nt, tc.outputs["Object"], fn.inputs["Vector"])
    caps = maprange(math_("MULTIPLY", crest1, fn.outputs["Fac"]), 0.5, 0.62)
    near = maprange(dist, 6.0, 0.0)
    wv = math_("FRACT", math_("MULTIPLY_ADD", fn.outputs["Fac"], 1.6, math_("MULTIPLY", dist, 0.42)))
    wash_line = maprange(wv, 0.82, 0.98)
    shore_f = math_("MULTIPLY", math_("MAXIMUM", math_("MULTIPLY", wash_line, near), math_("POWER", near, 4.0)), 1.0)
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

def sea2(deep, shallow, wave_scale=0.7, choppy=1.1, foam=0.35):
    me = bpy.data.meshes.new("Sea"); bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=320, y_segments=320, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * 150, v.co.y * 150 + 20, 0))
    bm.to_mesh(me); bm.free()
    for p_ in me.polygons: p_.use_smooth = True
    ob = bpy.data.objects.new("Sea", me); ob.location.z = SEA_Z
    me.materials.append(mat_sea2("Sea", deep, shallow, RX + WT + 1.6, RY + WT + 1.6))
    m = ob.modifiers.new("ocean", "OCEAN")
    m.geometry_mode = "DISPLACE"; m.resolution = 14; m.viewport_resolution = 6
    m.spatial_size = 75; m.wave_scale = wave_scale; m.choppiness = choppy; m.wind_velocity = 14
    m.wave_alignment = 0.35; m.wave_direction = math.radians(-135)
    m.use_foam = True; m.foam_layer_name = "foam"; m.foam_coverage = foam; m.time = 3.0; m.random_seed = 7
    return link(ob, FX)

# ---------------------------------------------------------------- tiled floor (one mesh, per-tile tint, bevelled)
def tiled_floor(mat, tw, td, bond=0.5, gap=0.035, thick=0.12, tilt=0.6, missing=0.0, mask_fn=None, irregular=True):
    """Paving as one mesh: rows of random height, stones of random width (irregular) or a strict grid (formal floors)."""
    bm = bmesh.new()
    tr = bm.faces.layers.float.new("trand")
    y0 = -RY - 0.4
    while y0 < RY + 0.4:
        rh = td * (R(0.7, 1.25) if irregular else 1.0)
        y = y0 + rh / 2
        x0 = -RX - 0.4 - (R(0, tw) if irregular else 0.0)
        while x0 < RX + 0.4:
            w = tw * (R(0.55, 1.45) if irregular else 1.0)
            xa, xb = max(x0, -RX - 0.4), min(x0 + w, RX + 0.4)
            x0 += w
            if xb - xa < 0.2: continue
            cx = (xa + xb) / 2
            if not inside(cx, y, -0.2) or rng.random() < missing or FLOOR_SKIP(cx, y): continue
            if not all(inside(px, py, -0.85) for px in (xa, xb) for py in (y - rh / 2, y + rh / 2)): continue
            sink = R(-0.012, 0.012) - (0.03 if rng.random() < 0.04 else 0.0)
            mat_ = Matrix.Translation((cx, y, -thick / 2 + sink)) @ Euler((math.radians(R(-tilt, tilt)), math.radians(R(-tilt, tilt)), math.radians(R(-0.8, 0.8)) if irregular else 0)).to_matrix().to_4x4() \
                   @ Matrix.Diagonal(((xb - xa) - gap, rh - gap, thick, 1))
            res = bmesh.ops.create_cube(bm, size=1.0, matrix=mat_)
            r = rng.random()
            for f_ in {f for v in res["verts"] for f in v.link_faces}: f_[tr] = r
        y0 += rh
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.03, segments=1, affect="EDGES", profile=0.5)
    if mask_fn:
        lay = bm.loops.layers.color.new("mask")
        for f_ in bm.faces:
            for lp in f_.loops:
                rr_, gg = mask_fn(lp.vert.co.x, lp.vert.co.y)
                lp[lay] = (rr_, gg, 0, 1)
    me = bpy.data.meshes.new("Tiles"); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("Tiles", me); me.materials.append(mat)
    return link(ob, ENV)

def plank_floor(mat, bw=0.28, gap=0.02, mask_fn=None):
    bm = bmesh.new(); tr = bm.faces.layers.float.new("trand")
    y = -RY
    while y < RY:
        x = -RX + R(-1.5, 0)
        while x < RX:
            l = R(2.0, 4.0); x0, x1 = max(x, -RX), min(x + l, RX)
            cx = (x0 + x1) / 2
            if x1 - x0 > 0.4 and all(inside(px, py, -0.05) for px in (x0, x1) for py in (y, y + bw)) and rng.random() > 0.015 and not FLOOR_SKIP(cx, y + bw / 2):
                mat_ = Matrix.Translation((cx, y + bw / 2, -0.05 + R(-0.01, 0.01))) @ Matrix.Diagonal((x1 - x0 - gap, bw - gap, 0.1, 1))
                res = bmesh.ops.create_cube(bm, size=1.0, matrix=mat_)
                r = rng.random()
                for f_ in {f for v in res["verts"] for f in v.link_faces}: f_[tr] = r
            x += l
        y += bw
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.012, segments=1, affect="EDGES")
    if mask_fn:
        lay = bm.loops.layers.color.new("mask")
        for f_ in bm.faces:
            for lp in f_.loops:
                rr_, gg = mask_fn(lp.vert.co.x, lp.vert.co.y); lp[lay] = (rr_, gg, 0, 1)
    me = bpy.data.meshes.new("Planks"); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("Planks", me); me.materials.append(mat)
    return link(ob, ENV)

def ground_poly(mat, step=0.5, mask_fn=None, z_fn=None, z=0.0, margin=-0.5):
    def zf(x, y):
        return (z_fn(x, y) if z_fn else 0.0) + z - GROUND_DIP(x, y)
    g = ground_grid("Ground", -RX + margin, RX - margin, -RY + margin, RY - margin, step, mat, mask_fn or (lambda x, y: (0, 0)), z_fn=zf)
    return g

# ---------------------------------------------------------------- walls
def wall_edges():
    """(a, b, kind) along the room polygon, centreline offset outward by WT / 2. kind: S, SE?, E, NE, N, W, SW."""
    P = poly()
    kinds = ["S", "E", "NE", "N", "W", "SW"]
    out = []
    for i in range(6):
        a, b = P[i], P[(i + 1) % 6]
        d = (b - a).normalized(); n = Vector((d.y, -d.x))
        a2 = a + n * (WT / 2) - d * (WT / 2); b2 = b + n * (WT / 2) + d * (WT / 2)
        out.append((a2, b2, kinds[i], n))
    return out

def opening_for(kind, a, b):
    L_ = (b - a).length
    w = GW if kind == "NE" else 5.4
    if kind in ("NE", "SW"):
        return [(L_ / 2 - w / 2, L_ / 2 + w / 2)]
    return []

def build_walls(T, mats):
    st = T["walls"]; stone, cap = mats["stone"], mats["cap"]
    for a, b, kind, n in wall_edges():
        if kind == "NE": continue                                   # the gate builds its own wall
        h = T["wall_h"] if kind in ("E", "W") else (T["north_h"] if kind == "N" else T["south_h"])
        style = st if kind != "S" and kind != "SW" else T.get("south_style", st if st in ("cliff", "palisade", "hedge", "basalt") else "ashlar")
        ops = opening_for(kind, a, b)
        if style in ("ashlar", "crenel", "ruin", "niche"):
            top = ashlar(a, b, h, stone, thick=WT, openings=ops, top_mat=cap if style != "ruin" else None,
                         crenel=(0.75, 0.6, 0.6) if style == "crenel" and kind != "S" else None, broken=0.35 if style == "ruin" else 0.0,
                         course=T.get("course", (0.38, 0.55)))
            if style == "niche" and kind in ("E", "W", "N"):
                niches(a, b, h, n, mats)
            if kind in ("E", "W", "N") and T.get("buttress"):
                d = (b - a); Ld = d.length; d.normalize()
                for k in range(1, int(Ld / 8)):
                    p_ = a + d * (k * Ld / int(Ld / 8))
                    if any(o0 <= k * Ld / int(Ld / 8) <= o1 for o0, o1 in ops): continue
                    box("buttress", (p_.x + n.x * 0.7, p_.y + n.y * 0.7, h * 0.45), (0.8 if abs(n.x) > 0.5 else 1.2, 1.2 if abs(n.x) > 0.5 else 0.8, h * 0.9),
                        (0, 0, 0), stone)
        elif style == "arcade":
            arcade(a, b, h, ops, mats, kind)
        elif style == "palisade":
            palisade(a, b, h, ops, mats)
        elif style == "cliff":
            cliff_wall(a, b, h, ops, mats, n)
        elif style == "basalt":
            basalt_wall(a, b, h, ops, mats, n)
        elif style == "hedge":
            hedge_wall(a, b, h, ops, mats)
        elif style == "fence":
            ashlar(a, b, 0.6, stone, thick=WT * 0.8, openings=ops, top_mat=cap)
            iron_fence(a, b, h, ops, mats)
        elif style == "tiers":
            tiers_wall(a, b, h, ops, mats, n)
        elif style == "timber":
            timber_wall(a, b, h, ops, mats, n)
        # pillars at the SW entry gap: open, no door, a pair of plinths with braziers marks the arrival point
        if kind == "SW":
            Ld = (b - a).length; d = (b - a).normalized()
            for s_ in (-1, 1):
                p_ = a + d * (Ld / 2 + s_ * (5.4 / 2 + 0.6))
                if style not in ("cliff", "basalt", "hedge", "palisade"):
                    pillar((p_.x, p_.y), max(1.2, h + 0.6), 1.1, stone, cap)

def arcade(a, b, h, ops, mats, kind):
    stone, cap = mats["stone"], mats["cap"]
    d = (b - a); Ld = d.length; d.normalize()
    yaw = math.atan2(d.y, d.x)
    ashlar(a, b, 0.7, stone, thick=WT, openings=ops, top_mat=cap)
    bay = 5.0
    nb = max(1, int(Ld / bay))
    spring = h * 0.62
    xs = [k * Ld / nb for k in range(nb + 1)]
    for k, t in enumerate(xs):
        if any(o0 - 0.6 < t < o1 + 0.6 for o0, o1 in ops): continue
        p_ = a + d * t
        snapped = rng.random() < 0.18
        pillar((p_.x, p_.y), spring + 0.1 if not snapped else R(1.2, 2.4), 0.95, stone, cap)
        if k < nb and not snapped:
            t2 = xs[k + 1]
            if any(o0 - 0.6 < t2 < o1 + 0.6 for o0, o1 in ops): continue
            c_ = a + d * ((t + t2) / 2)
            miss = () if rng.random() > 0.3 else tuple(range(0, 4)) if rng.random() < 0.5 else tuple(range(5, 9))
            arch((c_.x, c_.y), (t2 - t) - 0.95, spring, 0.9, stone, pointed=True, n=9, yaw=yaw, missing=miss)
            if not miss and rng.random() < 0.6:
                box("arc_top", (c_.x, c_.y, spring + (t2 - t) * 0.55 + 0.35), ((t2 - t) + 0.6, 0.9, 0.5), (0, 0, yaw), cap)

def palisade(a, b, h, ops, mats):
    wood = mats["wood"]
    d = (b - a); Ld = d.length; d.normalize(); t = 0.0
    while t < Ld:
        if not any(o0 <= t <= o1 for o0, o1 in ops):
            p_ = a + d * t; hh = h * R(0.85, 1.1)
            cylinder("stake", (p_.x, p_.y, hh / 2), 0.17, hh, wood, verts=7, rot=(R(-0.04, 0.04), R(-0.04, 0.04), 0))
            cylinder("stake_tip", (p_.x, p_.y, hh + 0.2), 0.17, 0.4, wood, verts=7, r2=0.0)
        t += 0.34
    for zz in (0.6, h * 0.75):
        for seg0, seg1 in segs_without(Ld, ops):
            m_ = a + d * ((seg0 + seg1) / 2)
            box("rail", (m_.x, m_.y, zz), (seg1 - seg0, 0.14, 0.18), (0, 0, math.atan2(d.y, d.x)), wood, bevel=0.01)

def segs_without(Ld, ops):
    cuts = sorted(ops); segs = []; s = 0.0
    for o0, o1 in cuts:
        if o0 > s: segs.append((s, o0))
        s = o1
    if s < Ld: segs.append((s, Ld))
    return segs

def cliff_wall(a, b, h, ops, mats, n):
    d = (b - a); Ld = d.length; d.normalize(); t = 0.0
    while t < Ld:
        if not any(o0 - 0.5 <= t <= o1 + 0.5 for o0, o1 in ops):
            p_ = a + d * t + n * R(0.3, 0.9)
            cliff("cliffw", (p_.x, p_.y, 0.0), (R(1.6, 2.2), R(1.4, 1.9), h * R(0.8, 1.2)), mats["cliff"], seed=int(t * 13 + p_.x * 7), step=R(0.4, 0.6), rough=0.42)
        t += R(1.6, 2.1)

def basalt_wall(a, b, h, ops, mats, n):
    d = (b - a); Ld = d.length; d.normalize(); t = 0.0
    while t < Ld:
        if not any(o0 - 0.3 <= t <= o1 + 0.3 for o0, o1 in ops):
            for k in range(2):
                p_ = a + d * (t + R(-0.2, 0.2)) + n * (k * 0.75 + R(-0.1, 0.1))
                hh = h * R(0.55, 1.15) * (1.15 if k else 1.0)
                cylinder("basalt", (p_.x, p_.y, hh / 2), 0.42, hh, mats["cliff"], verts=6, rot=(0, 0, R(0, 1)), bevel=0.02)
        t += 0.72

def hedge_wall(a, b, h, ops, mats):
    d = (b - a); Ld = d.length; d.normalize()
    for s0, s1 in segs_without(Ld, ops):
        t = s0
        while t < s1:
            l = min(R(1.4, 2.2), s1 - t); m_ = a + d * (t + l / 2)
            rock("hedge", (m_.x, m_.y, h * 0.5), (l * 0.6, 0.75, h * 0.55), mats["hedge"], sub=2, rough=0.18, seed=int(t * 31 + m_.y))
            t += l * 0.85

def iron_fence(a, b, h, ops, mats):
    iron = mats["iron"]
    d = (b - a); Ld = d.length; d.normalize(); yaw = math.atan2(d.y, d.x)
    for s0, s1 in segs_without(Ld, ops):
        t = s0 + 0.15
        while t < s1:
            p_ = a + d * t
            box("bar", (p_.x, p_.y, 0.6 + h / 2), (0.05, 0.05, h), (R(-0.03, 0.03), R(-0.03, 0.03), 0), iron, bevel=0)
            cylinder("spike", (p_.x, p_.y, 0.6 + h + 0.1), 0.05, 0.2, iron, verts=4, r2=0.0)
            t += 0.22
        m_ = a + d * ((s0 + s1) / 2)
        for zz in (0.75, 0.6 + h - 0.15):
            box("frail", (m_.x, m_.y, zz), (s1 - s0, 0.05, 0.06), (0, 0, yaw), iron, bevel=0)

def tiers_wall(a, b, h, ops, mats, n):
    stone, cap = mats["stone"], mats["cap"]
    d = (b - a); Ld = d.length; d.normalize(); yaw = math.atan2(d.y, d.x)
    for k in range(3):
        for s0, s1 in segs_without(Ld, ops):
            m_ = a + d * ((s0 + s1) / 2) + n * (k * 0.9)
            box("tier", (m_.x, m_.y, (k + 1) * h / 3 / 2), (s1 - s0, 0.9, (k + 1) * h / 3), (0, 0, yaw), stone if k % 2 == 0 else cap, bevel=0.04)

def timber_wall(a, b, h, ops, mats, n):
    """Ruined timber-framed houses lining the wall: stone plinth, posts, beams, broken roofs."""
    stone, wood, roof = mats["stone"], mats["wood"], mats.get("roof", mats["wood"])
    d = (b - a); Ld = d.length; d.normalize(); yaw = math.atan2(d.y, d.x)
    for s0, s1 in segs_without(Ld, ops):
        t = s0
        while t < s1 - 2:
            l = min(R(4.5, 6.5), s1 - t)
            m_ = a + d * (t + l / 2) + n * 1.6
            depth = 3.6
            box("plinth", (m_.x, m_.y, 0.4), (l, depth, 0.8), (0, 0, yaw), stone)
            for u in (-l / 2 + 0.15, 0, l / 2 - 0.15):
                for v in (-depth / 2 + 0.15, depth / 2 - 0.15):
                    pp = Vector((m_.x, m_.y)) + d * u + n * v
                    box("post", (pp.x, pp.y, 0.8 + h * 0.4), (0.22, 0.22, h * 0.8), (0, 0, yaw), wood, bevel=0.01)
            for v in (-depth / 2 + 0.15, depth / 2 - 0.15):
                pp = Vector((m_.x, m_.y)) + n * v
                box("beam", (pp.x, pp.y, 0.8 + h * 0.8), (l, 0.22, 0.22), (0, 0, yaw), wood, bevel=0.01)
            # wattle infill panels (lighter plaster) on the back side
            pp = Vector((m_.x, m_.y)) + n * (depth / 2 - 0.15)
            box("plaster", (pp.x, pp.y, 0.8 + h * 0.4), (l - 0.3, 0.12, h * 0.75), (0, 0, yaw), mats.get("plaster", stone), bevel=0)
            if rng.random() < 0.7:
                for side in (-1, 1):
                    rp = Vector((m_.x, m_.y)) + n * (side * depth * 0.25)
                    ob = box("roof", (rp.x, rp.y, 0.8 + h * 0.8 + 0.9), (l * R(0.6, 1.0), depth * 0.62, 0.14), (0, 0, yaw), roof, bevel=0.01)
                    ob.rotation_euler = Euler((side * math.radians(38), 0, 0)); ob.rotation_euler.rotate(Euler((0, 0, yaw)))
            t += l + R(0.6, 1.4)

def niches(a, b, h, n, mats):
    bone = mats.get("bone")
    if not bone: return
    d = (b - a); Ld = d.length; d.normalize()
    t = 1.0
    while t < Ld - 1:
        for zz in (0.9, 1.6, 2.3):
            if zz > h - 0.4: continue
            p_ = a - n * (WT / 2 + 0.02) + d * t
            rock("skull", (p_.x, p_.y, zz), (0.14, 0.13, 0.15), bone, sub=1, rough=0.12, seed=int(t * 10 + zz * 3))
        t += R(0.5, 0.9)

# ---------------------------------------------------------------- NE gate (faces NE), SW entry, bridges, island
def gate(T, mats):
    style = T["gate"]; stone, cap, dark = mats["stone"], mats["cap"], mats.get("dark", mats["cap"])
    a, b = Vector((RX + WT / 2 * S2, RY - C - WT / 2)), Vector((RX - C - WT / 2, RY + WT / 2 * S2))
    # chamfer wall either side of the opening
    for a, b, kind, n in wall_edges():
        if kind != "NE": continue
        ops = opening_for(kind, a, b)
        if style in ("cave",):
            cliff_wall(a, b, T["wall_h"] * 1.2, [(o0 - 0.4, o1 + 0.4) for o0, o1 in ops], mats, n)
        elif style == "palisade":
            palisade(a, b, T["wall_h"], ops, mats)
        else:
            ashlar(a, b, T["wall_h"] + 1.0, stone, thick=WT * 1.4, openings=[(o0 - 1.0, o1 + 1.0) for o0, o1 in ops], top_mat=cap,
                   crenel=(0.7, 0.6, 0.5) if style in ("portcullis", "crenel") else None, course=T.get("course", (0.38, 0.55)))
    H = T.get("gate_h", 6.4)
    if style in ("gothic", "portcullis", "round", "marble", "basalt", "iron", "great"):
        tm = dark if style in ("gothic", "basalt") else stone
        for s_ in (-1, 1):
            u = s_ * (GW / 2 + 1.2)
            tower_h = H + (3.2 if style in ("portcullis", "great") else 2.0)
            z = 0.0
            while z < tower_h:
                ch = min(R(0.5, 0.65), tower_h - z)
                box("gpier", L(u, 0, z + ch / 2), (2.4, 2.4, ch - 0.02), (0, 0, YAW_NE + R(-0.02, 0.02)), tm, bevel=0.04)
                z += ch
            box("gpier_cap", L(u, 0, z + 0.15), (2.7, 2.7, 0.3), (0, 0, YAW_NE), cap, bevel=0.05)
            if style in ("gothic", "basalt", "iron", "marble"):
                cylinder("spire", L(u, 0, z + 1.6), 0.9, 2.6, tm, verts=4, r2=0.0, rot=(0, 0, YAW_NE + math.pi / 4))
            brazier(*L(u, -2.4)[:2], mats["iron"], h=1.1)
            if style in ("portcullis", "great"):
                for k in range(3):
                    box("merlon", L(u + (k - 1) * 0.7, 0, z + 0.6), (0.45, 1.9, 0.6), (0, 0, YAW_NE), tm, bevel=0.03)
        arch((L(0, 0)[0], L(0, 0)[1]), GW, H - GW * 0.15, 1.6, tm, pointed=style in ("gothic", "basalt", "iron"), n=11, yaw=YAW_NE)
        box("glintel", L(0, 0, H + GW * 0.62), (GW + 1.0, 1.6, 0.7), (0, 0, YAW_NE), cap)
        if style == "portcullis":
            for i in range(8):
                u = -GW / 2 + 0.3 + i * (GW - 0.6) / 7
                box("pbar", L(u, -0.2, H + 0.4), (0.07, 0.07, 1.6), (0, 0, YAW_NE), mats["iron"], bevel=0)
        if style in ("gothic", "great", "marble", "iron", "round", "basalt"):
            for s_ in (-1, 1):
                ang = YAW_NE + s_ * math.radians(75)
                ob = box("gdoor", (0, 0, 0), (GW / 2, 0.14, H - 0.2), (0, 0, ang), mats["wood"] if style != "iron" else mats["iron"])
                hinge = Vector(L(s_ * GW / 2, -0.4)[:2])
                dvec = Vector((math.cos(ang), math.sin(ang))) * (-s_ * GW / 4)
                ob.location = (hinge.x + dvec.x, hinge.y + dvec.y, (H - 0.2) / 2)
    elif style == "cave":
        for s_ in (-1, 1):
            cliff("cave_side", L(s_ * (GW / 2 + 1.4), 0.4, 0.0), (1.9, 2.0, H + 1.4), mats["cliff"], seed=900 + s_, step=0.6)
        cliff("cave_top", L(0, 0.8, H - 0.3), (GW / 2 + 1.3, 2.2, 1.8), mats["cliff"], seed=950, step=0.5)
        for s_ in (-1, 1):
            box("post", L(s_ * (GW / 2 - 0.1), -0.3, H * 0.4), (0.26, 0.26, H * 0.8), (0, 0, YAW_NE), mats["wood"])
        box("beam", L(0, -0.3, H * 0.8 + 0.1), (GW + 0.5, 0.28, 0.28), (0, 0, YAW_NE), mats["wood"])
    elif style in ("timber", "palisade"):
        for s_ in (-1, 1):
            for v in (-0.6, 0.6):
                cylinder("gpost", L(s_ * (GW / 2 + 0.3), v, (H + 1.5) / 2), 0.25, H + 1.5, mats["wood"], verts=8)
            box("gplatform", L(s_ * (GW / 2 + 0.3), 0, H + 0.9), (1.6, 1.8, 0.16), (0, 0, YAW_NE), mats["wood"])
        box("gbeam", L(0, 0, H + 0.3), (GW + 1.6, 0.35, 0.4), (0, 0, YAW_NE), mats["wood"])
        rp = L(0, 0, H + 1.6)
        for s_ in (-1, 1):
            ob = box("groof", (0, 0, 0), (GW + 2.4, 1.4, 0.12), (0, 0, 0), mats.get("roof", mats["wood"]))
            off = Vector(L(0, s_ * 0.5)[:2]) - Vector(L(0, 0)[:2])
            ob.location = (rp[0] + off.x, rp[1] + off.y, rp[2]); ob.rotation_euler = Euler((s_ * math.radians(-35), 0, 0)); ob.rotation_euler.rotate(Euler((0, 0, YAW_NE)))
    # the light: blinding glow just outside, a strong spot through the opening, a soft volume to show the shafts
    gc = T["gate_light"]
    glow = mat_flat("GateGlow", (0.0, 0.0, 0.0), emit=gc, emit_str=T.get("gate_glow", 30.0))
    box("gate_glow", L(0, 2.2, H / 2 + 0.6), (GW + 0.6, 0.05, H + 1.6), (0, 0, YAW_NE), glow, bevel=0, coll=FX)
    ld = bpy.data.lights.new("gate_spot", "SPOT"); ld.energy = T.get("gate_power", 140000); ld.color = gc
    ld.spot_size = math.radians(48); ld.spot_blend = 0.5; ld.shadow_soft_size = 0.4
    lo = bpy.data.objects.new("gate_spot", ld); lo.location = L(0, 7.0, 5.5); FX.objects.link(lo)
    tgt = Vector(L(0, -14, 0)); dvec = tgt - lo.location
    lo.rotation_euler = dvec.to_track_quat("-Z", "Y").to_euler()
    vol = box("gate_volume", L(0, -8, 3.0), (GW + 14, 26, 6.5), (0, 0, YAW_NE), mat_volume(T.get("shaft", 0.09), gc), bevel=0, coll=FX)

def mat_volume(density, color):
    """Light beam volume in the gate's local box (Generated: X across, Y = 1 at the gate, Z up): a wedge that widens and
    slopes down into the room, fades with distance, broken into rays by noise stretched along the beam. The shape lives
    in the density, so the beam reads as a beam even though the sun lights the volume too."""
    m = bpy.data.materials.new("Shaft"); nt, out = nodes_of(m)
    pv = nt.nodes.new("ShaderNodeVolumePrincipled")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, tc.outputs["Generated"], sep.inputs[0])
    def M(op, a, b=None):
        n = nt.nodes.new("ShaderNodeMath"); n.operation = op
        for i, v in enumerate((a, b)):
            if v is None: continue
            if isinstance(v, (int, float)): n.inputs[i].default_value = v
            else: lin(nt, v, n.inputs[i])
        return n.outputs[0]
    def MR(v, f0, f1, t0=0.0, t1=1.0):
        n = nt.nodes.new("ShaderNodeMapRange"); n.clamp = True
        n.inputs["From Min"].default_value = f0; n.inputs["From Max"].default_value = f1
        n.inputs["To Min"].default_value = t0; n.inputs["To Max"].default_value = t1
        lin(nt, v, n.inputs["Value"]); return n.outputs[0]
    X, Z = sep.outputs["X"], sep.outputs["Z"]
    Y = M("SUBTRACT", 1.0, sep.outputs["Y"])                   # box local +Y points into the room: flip so 1 = gate side
    depth = M("SUBTRACT", 1.0, Y)                               # 0 at the gate, 1 deep in the room
    half = M("MULTIPLY_ADD", depth, 0.22) if False else M("ADD", M("MULTIPLY", depth, 0.24), 0.11)
    ax = M("ABSOLUTE", M("SUBTRACT", X, 0.5))
    mx = MR(M("SUBTRACT", half, ax), 0.0, 0.05)
    zc = M("ADD", M("MULTIPLY", Y, 0.7), 0.04)                  # beam centre height: high at the gate, floor deep inside
    az = M("ABSOLUTE", M("SUBTRACT", Z, zc))
    mz = MR(az, 0.32, 0.08)
    fade = M("MULTIPLY", MR(Y, 0.0, 0.55), MR(Y, 1.0, 0.92))
    mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (14.0, 0.6, 3.0)
    lin(nt, tc.outputs["Generated"], mp.inputs["Vector"])
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 1.0; nz.inputs["Detail"].default_value = 2
    lin(nt, mp.outputs[0], nz.inputs["Vector"])
    rays = MR(nz.outputs["Fac"], 0.35, 0.7, 0.25, 1.0)
    d = M("MULTIPLY", M("MULTIPLY", M("MULTIPLY", mx, mz), fade), rays)
    lin(nt, M("MULTIPLY", d, density * 2.5), pv.inputs["Density"])
    pv.inputs["Color"].default_value = (*color, 1)
    lin(nt, pv.outputs[0], out.inputs["Volume"])
    return m

def bridge(start, direction, length, T, mats, width=4.6):
    """Deck at floor level from start along direction; piers + arches into the sea (stone) or posts (wood)."""
    d = Vector(direction).normalized(); n = Vector((-d.y, d.x)); yaw = math.atan2(d.y, d.x)
    s = Vector(start)
    stone, cap = mats["stone"], mats["cap"]
    if T["bridge"] == "stone":
        span = 7.0
        k = 0
        while k * span < length:
            p_ = s + d * (k * span + span / 2)
            box("deck", (p_.x, p_.y, -0.35), (span + 0.02, width + 0.6, 0.7), (0, 0, yaw), mats["bridge"], bevel=0.04)
            for sd in (-1, 1):
                q = p_ + n * sd * (width / 2 + 0.1)
                t = -span / 2
                while t < span / 2:
                    l = min(R(0.8, 1.4), span / 2 - t); qq = q + d * (t + l / 2)
                    box("parapet", (qq.x, qq.y, 0.4), (l - 0.03, 0.45, 0.8), (0, 0, yaw + R(-0.01, 0.01)), stone, bevel=0.03)
                    t += l
            pier = s + d * (k * span + span)
            box("pier", (pier.x, pier.y, -2.2), (1.6, width + 0.8, 3.6), (0, 0, yaw), mats["bridge"], bevel=0.05)
            box("cutwater", (pier.x, pier.y, -2.6), (1.6, 1.6, 3.0), (0, 0, yaw + math.radians(45)), mats["bridge"], bevel=0.05)
            if k % 2 == 1 and T.get("bridge_lamps", True):
                for sd in (-1, 1):
                    q = p_ + n * sd * (width / 2 + 0.1)
                    cylinder("lamp_post", (q.x, q.y, 1.4), 0.07, 1.6, mats["iron"], verts=6)
                    torch_light((q.x, q.y, 2.3), color=T.get("lamp", (1.0, 0.6, 0.3)), power=90, flame=True, radius=0.08)
            k += 1
    else:
        span = 3.0; k = 0
        while k * span < length:
            p_ = s + d * (k * span + span / 2)
            for t in range(int(span / 0.32)):
                q = s + d * (k * span + t * 0.32 + 0.16)
                if rng.random() < 0.05: continue
                box("board", (q.x, q.y, -0.05 + R(-0.02, 0.02)), (0.28, width + R(-0.2, 0.2), 0.08), (0, 0, yaw + R(-0.02, 0.02)), mats["wood"], bevel=0.01)
            for sd in (-1, 1):
                q = s + d * (k * span) + n * sd * (width / 2)
                cylinder("bpost", (q.x, q.y, -1.0), 0.18, 3.6, mats["wood"], verts=8)
                m_ = p_ + n * sd * (width / 2)
                box("brail", (m_.x, m_.y, 0.85), (span, 0.12, 0.12), (0, 0, yaw), mats["wood"], bevel=0.01)
                if k % 3 == 1:
                    cylinder("lantern_post", (q.x, q.y, 1.4), 0.07, 1.4, mats["wood"], verts=6)
                    torch_light((q.x, q.y, 2.2), color=T.get("lamp", (1.0, 0.6, 0.3)), power=70, flame=True, radius=0.08)
            box("stringer", (p_.x, p_.y, -0.18), (span, 0.25, 0.25), (0, 0, yaw), mats["wood"], bevel=0.01)
            k += 1

def island(T, mats):
    """Foundation under the room: big masonry (or cliff) from the waterline to the floor, rocks + kelp at the waterline."""
    found = mats["found"]
    P = poly()
    for i in range(6):
        a, b = P[i], P[(i + 1) % 6]
        d = (b - a).normalized(); n = Vector((d.y, -d.x))
        a2 = a + n * (WT + 0.9) - d * (WT + 0.9) * 0.6; b2 = b + n * (WT + 0.9) + d * (WT + 0.9) * 0.6
        if T.get("found_style", "masonry") == "masonry":
            ashlar(a2, b2, -0.05, found, thick=1.8, course=(0.6, 0.85), blen=(1.0, 2.0), base_z=SEA_Z - 1.2, inset=0.08)
            # cap course flush with the floor
            ashlar(a2 - n * 0.9, b2 - n * 0.9, 0.0, found, thick=1.2, course=(0.5, 0.5), blen=(1.2, 2.0), base_z=-0.5, inset=0.0)
        L_ = (b2 - a2).length; t = 0.0
        while t < L_:
            p_ = a2 + d * t + n * R(0.6, 2.2)
            if T.get("found_style", "masonry") == "cliff":
                cliff("fcliff", (p_.x, p_.y, SEA_Z - 1.5), (R(1.8, 2.6), R(1.6, 2.4), R(2.4, 3.2)), mats["cliff"], seed=int(t * 17 + p_.y), step=0.5, rough=0.42)
            elif rng.random() < 0.55:
                cliff("frock", (p_.x, p_.y, SEA_Z - 0.9), (R(0.7, 1.6), R(0.7, 1.4), R(0.9, 1.6)), mats["rock"], sub=2, seed=int(t * 11 + p_.x), step=0.35)
            t += R(1.4, 2.6) if T.get("found_style", "masonry") != "cliff" else R(2.0, 2.8)
    # fill under the floor so nothing shows through gaps
    box("core", (0, 0, -2.45), (2 * RX + 2 * WT, 2 * RY + 2 * WT, 3.0), (0, 0, 0), mats["gap"], bevel=0)   # top at -0.95: below pond water

def distant_isles(T, mats):
    """Silhouettes of the next chambers in the haze: the bridge leads somewhere."""
    for (x, y, s, tower) in ((40, 84, 1.0, True), (-34, 80, 0.8, False), (-52, 40, 0.6, True)):
        cliff("isle", (x, y, SEA_Z - 2), (14 * s, 11 * s, 9 * s), mats["rock"], seed=int(x + y), step=1.2, rough=0.4)
        if tower:
            for k in range(6):
                box("tw", (x, y, 6 * s + k * 2.2 * s), (4 * s, 4 * s, 2.2 * s), (0, 0, 0.3), mats["stone"], bevel=0.1)
            torch_light((x, y, 6 * s + 14 * s), color=T["gate_light"], power=4000, flame=False, radius=1.0)

# ---------------------------------------------------------------- props
COVER = [(-9.5, -12.0), (9.0, -16.5), (-11.0, 3.0), (11.0, -2.0), (-3.5, 13.5), (8.5, 15.0), (-12.0, 21.0), (2.0, -4.5), (-1.5, 24.0), (13.0, 6.5)]

def cover(T, mats):
    kind = T["cover"]; stone, cap = mats["stone"], mats["cap"]
    for i, (x, y) in enumerate(COVER[:T.get("cover_n", 8)]):
        x += R(-1, 1); y += R(-1, 1)
        if kind == "columns":
            if i % 3 == 2:
                for k in range(4):
                    cylinder("fallen", (x - 1.5 + k * 1.02, y + k * 0.26, 0.45), 0.46, 0.98, stone, verts=14, rot=(0, math.radians(90), math.radians(14)), bevel=0.03)
                rubble(x - 2.2, y, 6, stone, spread=0.6, seed=i)
            else:
                broken_column(x, y, R(1.0, 2.6), stone, cap, r=0.55, seed=i * 7)
        elif kind == "tombs":
            box("sarc", (x, y, 0.5), (2.4, 1.1, 1.0), (0, 0, R(-0.2, 0.2)), stone)
            box("sarc_lid", (x + R(-0.2, 0.2), y, 1.07), (2.5, 1.2, 0.16), (0, 0, R(-0.3, 0.3)), cap)
        elif kind == "crates":
            for k in range(int(R(3, 6))):
                box("crate", (x + R(-0.9, 0.9), y + R(-0.9, 0.9), 0.36 + (0.72 if k == 4 else 0)), (0.72, 0.72, 0.72), (0, 0, R(0, 1)), mats["wood"])
            cylinder("barrel", (x + 1.2, y - 0.8, 0.45), 0.36, 0.9, mats["wood"], verts=12, bevel=0.02)
        elif kind == "rocks":
            cliff("crock", (x, y, 0.0), (R(1.1, 1.8), R(1.0, 1.6), R(1.0, 1.8)), mats["rock"], seed=i * 13, step=0.4)
            cliff("crock2", (x + 1.4, y - 0.6, 0.0), (0.7, 0.6, 0.7), mats["rock"], seed=i * 13 + 1, sub=2, step=0.3)
        elif kind == "statues":
            statue(x, y, mats, scale=R(1.0, 1.3), seed=i)
        elif kind == "barricades":
            yaw = R(0, 3.14)
            for k in range(5):
                q = Vector((x, y)) + Vector((math.cos(yaw), math.sin(yaw))) * (k - 2) * 0.7
                cylinder("spike", (q.x, q.y, 0.7), 0.1, 2.0, mats["wood"], verts=6, r2=0.02, rot=(math.radians(50), 0, yaw + math.radians(90)))
            cylinder("xbar", (x, y, 0.55), 0.12, 3.4, mats["wood"], verts=6, rot=(math.radians(90), 0, yaw - math.radians(90)))
        elif kind == "hedges":
            rock("topiary", (x, y, 0.9), (1.2, 1.2, 0.9), mats["hedge"], sub=2, rough=0.12, seed=i)
            cylinder("pot", (x, y, 0.3), 1.0, 0.6, stone, verts=16, bevel=0.03)
        elif kind == "basalt":
            for k in range(int(R(4, 8))):
                hh = R(0.8, 2.6)
                cylinder("bcol", (x + R(-0.9, 0.9), y + R(-0.9, 0.9), hh / 2), 0.42, hh, mats["cliff"], verts=6, rot=(0, 0, R(0, 1)), bevel=0.02)
        elif kind == "pillars":
            pillar((x, y), R(3.0, 4.5), 1.1, stone, cap)
        elif kind == "graves":
            pass

def statue(x, y, mats, scale=1.0, seed=0, yaw=None):
    """Kneeling / standing knight statue blocked out in primitives: plinth, robe, torso, head, sword."""
    st = mats.get("statue", mats["stone"])
    yaw = R(0, 6.28) if yaw is None else yaw
    s = scale
    box("plinth", (x, y, 0.35 * s), (1.6 * s, 1.6 * s, 0.7 * s), (0, 0, yaw), mats["cap"], bevel=0.05)
    cylinder("robe", (x, y, 0.7 * s + 0.9 * s), 0.55 * s, 1.8 * s, st, verts=12, r2=0.35 * s, bevel=0.02)
    box("chest", (x, y, 0.7 * s + 2.0 * s), (0.9 * s, 0.5 * s, 0.7 * s), (0, 0, yaw), st, bevel=0.06)
    rock("head", (x, y, 0.7 * s + 2.55 * s), (0.2 * s, 0.22 * s, 0.26 * s), st, sub=2, rough=0.05, seed=seed)
    box("sword", (x + math.cos(yaw) * 0.35 * s, y + math.sin(yaw) * 0.35 * s, 0.7 * s + 1.2 * s), (0.08 * s, 0.14 * s, 2.0 * s), (0, 0, yaw), st, bevel=0.01)
    box("guard", (x + math.cos(yaw) * 0.35 * s, y + math.sin(yaw) * 0.35 * s, 0.7 * s + 2.1 * s), (0.08 * s, 0.6 * s, 0.08 * s), (0, 0, yaw), st, bevel=0.01)

def bonfire(x, y, mats):
    rubble(x, y, 10, mats["stone"], spread=0.5, size=(0.15, 0.25), seed=3)
    for k in range(8):
        a = k * math.pi / 4
        cylinder("log", (x + math.cos(a) * 0.25, y + math.sin(a) * 0.25, 0.25), 0.07, 0.8, mats["wood"], verts=6,
                 rot=(math.radians(60) * math.cos(a + 1.57), math.radians(60) * math.sin(a + 1.57), 0))
    box("bf_sword", (x, y, 1.0), (0.06, 0.12, 1.4), (math.radians(8), 0, 0.4), mats["iron"], bevel=0.005)
    emb = mat_flat("Embers", (0.1, 0.03, 0.01), emit=(1.0, 0.35, 0.06), emit_str=25)
    rock("bf_core", (x, y, 0.2), (0.35, 0.35, 0.25), emb, sub=2, rough=0.4, seed=8)
    for k in range(5):
        rock("flame", (x + R(-0.15, 0.15), y + R(-0.15, 0.15), 0.5 + k * 0.15), (0.12, 0.12, 0.3), mat_flat("fl", (1, 0.5, 0.1), emit=(1.0, 0.45, 0.1), emit_str=30), sub=1, rough=0.3, coll=FX, seed=k)
    torch_light((x, y, 1.2), power=1800, radius=0.4, flame=False)

def fountain(x, y, mats):
    stone, cap = mats["stone"], mats["cap"]
    cylinder("basin", (x, y, 0.35), 3.2, 0.7, stone, verts=32, bevel=0.04)
    water = mat_flat("FountainWater", (0.05, 0.1, 0.11), rough=0.05)
    cylinder("water", (x, y, 0.62), 2.95, 0.04, water, verts=32)
    cylinder("stem", (x, y, 1.2), 0.45, 1.6, cap, verts=12, bevel=0.03)
    cylinder("bowl", (x, y, 2.05), 1.2, 0.3, stone, verts=20, r2=0.7, rot=(math.pi, 0, 0), bevel=0.02)
    statue(x, y, mats, scale=0.55, seed=5, yaw=0.8)

def graves(mats, n=40):
    stone, dark = mats["stone"], mats.get("dark", mats["cap"])
    placed = 0
    while placed < n:
        x, y = R(-RX + 2, RX - 2), R(-RY + 6, RY - 4)
        if not inside(x, y, 2) or (abs(x - y * 0.6) < 3.5): continue    # keep the SW -> NE lane open
        if rng.random() < 0.2:
            box("cross_v", (x, y, 0.65), (0.15, 0.15, 1.3), (R(-0.15, 0.15), R(-0.15, 0.15), R(-0.3, 0.3)), dark, bevel=0.01)
            box("cross_h", (x, y, 0.95), (0.65, 0.13, 0.13), (R(-0.1, 0.1), 0, R(-0.3, 0.3)), dark, bevel=0.01)
        else:
            box("headstone", (x, y, 0.42), (R(0.55, 0.85), 0.18, R(0.65, 1.0)), (R(-0.22, 0.22), R(-0.18, 0.18), R(-0.2, 0.2)), stone, bevel=0.05)
            box("mound", (x, y - 0.9, 0.05), (0.8, 1.6, 0.18), (0, 0, R(-0.1, 0.1)), mats["gap"], bevel=0.08)
        placed += 1

def dead_tree(x, y, h, mat):
    cylinder("dt", (x, y, h / 2), 0.2, h, mat, verts=7, r2=0.07, rot=(R(-0.1, 0.1), R(-0.1, 0.1), 0))
    for k in range(6):
        a = R(0, 6.28); z = R(h * 0.45, h * 0.9); l = R(0.9, 1.8)
        cylinder("db", (x + math.cos(a) * l * 0.35, y + math.sin(a) * l * 0.35, z + l * 0.25), 0.06, l, mat, verts=5, r2=0.01,
                 rot=(math.radians(55) * math.cos(a + 1.57), math.radians(55) * math.sin(a + 1.57), 0))

def leafy_tree(x, y, h, trunk, leaf, leaf2):
    cylinder("lt", (x, y, h * 0.3), 0.22, h * 0.6, trunk, verts=8, r2=0.14)
    for k in range(5):
        rock("crown", (x + R(-1.2, 1.2), y + R(-1.2, 1.2), h * R(0.6, 0.95)), (R(1.2, 1.9), R(1.2, 1.9), R(0.9, 1.3)), leaf if k % 2 else leaf2,
             sub=2, rough=0.3, seed=int(x * 10 + y + k))

def grass_tufts(mat, n, near_walls=True, mat2=None):
    me = bpy.data.meshes.new("Grass"); bm = bmesh.new()
    placed = 0; tries = 0
    while placed < n and tries < n * 20:
        tries += 1
        x, y = R(-RX, RX), R(-RY, RY)
        if not inside(x, y, 0.2): continue
        if near_walls and edge_dist(x, y) > 2.5 and rng.random() > 0.12: continue
        for k in range(7):
            a = R(0, 6.28); r = R(0.02, 0.18); hh = R(0.15, 0.45)
            base = Vector((x + math.cos(a) * r, y + math.sin(a) * r, 0.0))
            tip = base + Vector((math.cos(a) * hh * 0.4, math.sin(a) * hh * 0.4, hh))
            w = Vector((-math.sin(a), math.cos(a), 0)) * 0.025
            v1 = bm.verts.new(base - w); v2 = bm.verts.new(base + w); v3 = bm.verts.new(tip)
            bm.faces.new((v1, v2, v3))
        placed += 1
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("Grass", me); me.materials.append(mat)
    return link(ob, ENV)

def banners(mats, color):
    cloth = mat_torn_cloth("Banner", tuple(c * 0.5 for c in color), color)
    for y in (-18, -6, 6, 18):
        for sx in (-1, 1):
            box("banner", (sx * (RX - 0.04), y, 2.3), (0.03, 1.0, 2.2), (0, 0, 0), cloth, bevel=0)
            box("banner_rod", (sx * (RX - 0.12), y, 3.45), (0.06, 1.3, 0.06), (0, 0, 0), mats["iron"], bevel=0)

def wall_lights(T, mats, every=10.0):
    col = T.get("lamp", (1.0, 0.55, 0.22))
    for y in [-RY + 6 + k * every for k in range(int((2 * RY - 10) / every) + 1)]:
        for sx in (-1, 1):
            if not inside(sx * (RX - 0.6), y, 0): continue
            box("sconce", (sx * (RX - 0.1), y, 2.25), (0.12, 0.12, 0.4), (0, 0, 0), mats["iron"], bevel=0)
            torch_light((sx * (RX - 0.35), y, 2.6), color=col, power=T.get("torch_power", 260), radius=0.08)
    for x in [-RX + 5 + k * every for k in range(int((2 * RX - 8) / every) + 1)]:
        if inside(x, RY - 0.6, 0) and T["walls"] not in ("arcade",):
            box("sconce", (x, RY - 0.1, 2.25), (0.12, 0.12, 0.4), (0, 0, 0), mats["iron"], bevel=0)
            torch_light((x, RY - 0.35, 2.6), color=col, power=T.get("torch_power", 260), radius=0.08)

def ice_floes(mat, n=40):
    for i in range(n):
        x, y = R(-70, 80), R(-50, 90)
        if abs(x) < RX + 8 and abs(y) < RY + 8: continue
        cliff("floe", (x, y, SEA_Z - 0.15), (R(1.0, 4.0), R(1.0, 3.5), 0.35), mat, sub=2, seed=i * 3, step=0.5, rough=0.35)

def lighthouse(x, y, mats, beam_col):
    stone = mats["stone"]
    cliff("lh_rock", (x, y, SEA_Z - 1.5), (6, 5.5, 4.0), mats["rock"], seed=77, step=0.6)
    z = 1.0
    for k in range(10):
        r = 2.0 - k * 0.08
        cylinder("lh", (x, y, z + 0.9), r, 1.8, stone if k % 3 else mats["cap"], verts=20, r2=r - 0.08, bevel=0.03)
        z += 1.8
    lamp = mat_flat("LHLamp", (1, 0.9, 0.7), emit=beam_col, emit_str=40)
    cylinder("lh_lamp", (x, y, z + 0.8), 1.1, 1.6, lamp, verts=16)
    cylinder("lh_roof", (x, y, z + 2.2), 1.6, 1.2, mats["iron"], verts=16, r2=0.1)
    torch_light((x, y, z + 0.8), color=beam_col, power=6000, flame=False, radius=0.8)

def boats(mats, n=4):
    for i in range(n):
        x, y = rng.choice([(-RX - 9, R(-10, 20)), (RX + 9, R(-20, 10)), (R(-10, 5), RY + 11)])
        hull((x, y, SEA_Z - 0.2), R(5, 7), R(1.8, 2.4), 1.0, mats["planks"], mats["wood"], tilt=R(-8, 8), yaw=R(0, 180), seed=i, broken=-1.0)

# ---------------------------------------------------------------- characters, camera, compositor
def chars2():
    if KNIGHT:
        _place(_import(KNIGHT), -11.5, -22.0, 225, action="AimIdle")
    if ZOMBIE:
        spots = [(-4, -8, 210), (4, -12, 250), (-8, 4, 200), (6, 2, 220), (0, 10, 230), (10, 10, 240), (-6, 16, 215), (3, 20, 225), (12, -6, 260), (-13, -4, 190)]
        for (x, y, yaw) in spots:
            new = _import(ZOMBIE)
            acts = [a.name for a in bpy.data.actions if "running" in a.name or "clip0" in a.name]
            _place(new, x, y, yaw, action=acts[-1] if acts else None)
    scene.frame_set(12)

def auto_camera(pitch=55.0, fov_v=40.0, margin=0.03):
    from bpy_extras.object_utils import world_to_camera_view
    cd = bpy.data.cameras.new("Cam"); cd.sensor_fit = "VERTICAL"; cd.angle_y = math.radians(fov_v); cd.clip_end = 600
    co = bpy.data.objects.new("Cam", cd); scene.collection.objects.link(co); scene.camera = co
    scene.render.resolution_x, scene.render.resolution_y = RES
    pts = [Vector((p.x * 1.0 + (1.6 if p.x > 0 else -1.6), p.y + (1.6 if p.y > 0 else -1.6), 0)) for p in poly()]
    pts += [Vector((p.x, p.y, 4.5)) for p in poly()]
    pr = math.radians(pitch); co.rotation_euler = (math.radians(90 - pitch), 0, 0)
    look = Vector((0, 0, 0))
    def place(dist):
        co.location = (look.x, look.y - dist * math.cos(pr), dist * math.sin(pr)); bpy.context.view_layer.update()
    for _ in range(3):
        lo_, hi_ = 20.0, 400.0
        for _ in range(30):
            mid = (lo_ + hi_) / 2; place(mid)
            ok = all(margin <= world_to_camera_view(scene, co, p).x <= 1 - margin and margin <= world_to_camera_view(scene, co, p).y <= 1 - margin for p in pts)
            if ok: hi_ = mid
            else: lo_ = mid
        place(hi_)
        ys = [world_to_camera_view(scene, co, p).y for p in pts]
        shift = (min(ys) + max(ys)) / 2 - 0.5
        look.y += shift * 40
    return co

def compositor(T):
    try:
        vl = scene.view_layers[0]; vl.use_pass_mist = True
        w = scene.world; dcam = (scene.camera.location - Vector((0, 0, 0))).length
        w.mist_settings.start = dcam * T.get("mist_start_k", 0.95); w.mist_settings.depth = dcam * T.get("mist_depth_k", 1.1); w.mist_settings.falloff = "LINEAR"
        ng = bpy.data.node_groups.new("Comp", "CompositorNodeTree")
        ng.interface.new_socket("Image", in_out="OUTPUT", socket_type="NodeSocketColor")
        rl = ng.nodes.new("CompositorNodeRLayers"); go = ng.nodes.new("NodeGroupOutput")
        # haze: mix towards the haze colour by mist
        mf = ng.nodes.new("ShaderNodeMath"); mf.operation = "MULTIPLY"; mf.inputs[1].default_value = T.get("haze", 0.45) * 0.8
        ng.links.new(rl.outputs["Mist"], mf.inputs[0])
        hz = ng.nodes.new("ShaderNodeMix"); hz.data_type = "RGBA"
        ng.links.new(mf.outputs[0], hz.inputs["Factor"]); ng.links.new(rl.outputs["Image"], hz.inputs[6])
        hz.inputs[7].default_value = (*T.get("haze_col", (0.5, 0.45, 0.45)), 1)
        gl = ng.nodes.new("CompositorNodeGlare")
        gl.inputs["Type"].default_value = "Bloom"; gl.inputs["Threshold"].default_value = T.get("bloom_thr", 1.0)
        gl.inputs["Strength"].default_value = T.get("bloom", 0.45); gl.inputs["Size"].default_value = 0.6 if gl.inputs["Size"].type == "VALUE" else gl.inputs["Size"].default_value
        ng.links.new(hz.outputs[2], gl.inputs["Image"])
        # vignette
        el = ng.nodes.new("CompositorNodeEllipseMask")
        for k, v in (("Size", (0.95, 0.85)),):
            try: el.inputs[k].default_value = v
            except Exception as e: print("ellipse", e)
        bl = ng.nodes.new("CompositorNodeBlur")
        try:
            bl.inputs["Size"].default_value = (300, 300)
        except Exception as e: print("blur", e)
        ng.links.new(el.outputs[0], bl.inputs["Image"])
        vg = ng.nodes.new("ShaderNodeMapRange"); vg.inputs["To Min"].default_value = T.get("vignette", 0.35); vg.inputs["To Max"].default_value = 0.0
        ng.links.new(bl.outputs[0], vg.inputs["Value"])
        vm = ng.nodes.new("ShaderNodeMix"); vm.data_type = "RGBA"
        ng.links.new(vg.outputs[0], vm.inputs["Factor"])
        ng.links.new(gl.outputs["Image"], vm.inputs[6]); vm.inputs[7].default_value = (0.0, 0.0, 0.0, 1)
        ng.links.new(vm.outputs[2], go.inputs[0])
        scene.compositing_node_group = ng
    except Exception as e:
        import traceback; traceback.print_exc()

def render2(T):
    scene.render.resolution_x, scene.render.resolution_y = RES
    scene.render.engine = "BLENDER_EEVEE"
    ee = scene.eevee
    for k, v in (("taa_render_samples", 64), ("use_raytracing", True), ("use_shadows", True), ("shadow_ray_count", 2),
                 ("use_fast_gi", True), ("use_volumetric_shadows", True), ("volumetric_tile_size", "4"),
                 ("use_volume_custom_range", True), ("volumetric_start", 1.0), ("volumetric_end", 200.0), ("shadow_pool_size", "4096")):
        try: setattr(ee, k, v)
        except Exception as e: print("eevee", k, e)
    scene.view_settings.view_transform = "AgX"
    try: scene.view_settings.look = T.get("look", "AgX - Medium High Contrast")
    except Exception as e: print("look", e)
    scene.view_settings.exposure = T.get("exposure", 0.0)
    scene.render.image_settings.file_format = "PNG"; scene.render.filepath = OUT

def world2(top, horizon, strength, sun_el, sun_az, glow, clouds=0.25):
    """Sky for reflections: Z gradient, a warm glow around the sun direction, soft cloud deck (low contrast)."""
    w = bpy.data.worlds.new("W2"); scene.world = w; w.use_nodes = True
    nt = w.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground"); bg.inputs["Strength"].default_value = strength
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, tc.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (*horizon, 1)
    ramp.color_ramp.elements[1].position = 0.55; ramp.color_ramp.elements[1].color = (*top, 1)
    lin(nt, sep.outputs["Z"], ramp.inputs["Fac"])
    # direction towards the sun (light comes FROM az; sun object az rotates the beam, so the sun sits opposite the beam)
    el = math.radians(sun_el); az = math.radians(sun_az)
    beam = Vector((-math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), -math.sin(el)))
    sdir = -beam
    dot = nt.nodes.new("ShaderNodeVectorMath"); dot.operation = "DOT_PRODUCT"
    lin(nt, tc.outputs["Generated"], dot.inputs[0]); dot.inputs[1].default_value = tuple(sdir)
    pw = nt.nodes.new("ShaderNodeMath"); pw.operation = "POWER"; pw.inputs[1].default_value = 6.0
    cl = nt.nodes.new("ShaderNodeMath"); cl.operation = "MAXIMUM"; cl.inputs[1].default_value = 0.0
    lin(nt, dot.outputs["Value"], cl.inputs[0]); lin(nt, cl.outputs[0], pw.inputs[0])
    gm = nt.nodes.new("ShaderNodeMix"); gm.data_type = "RGBA"; gm.blend_type = "ADD"
    lin(nt, pw.outputs[0], gm.inputs["Factor"]); lin(nt, ramp.outputs["Color"], gm.inputs[6]); gm.inputs[7].default_value = (*glow, 1)
    # soft clouds
    zc = nt.nodes.new("ShaderNodeMath"); zc.operation = "ADD"; zc.inputs[1].default_value = 0.08
    lin(nt, sep.outputs["Z"], zc.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    for i in range(3): lin(nt, zc.outputs[0], comb.inputs[i])
    dv = nt.nodes.new("ShaderNodeVectorMath"); dv.operation = "DIVIDE"
    lin(nt, tc.outputs["Generated"], dv.inputs[0]); lin(nt, comb.outputs[0], dv.inputs[1])
    cn = nt.nodes.new("ShaderNodeTexNoise"); cn.inputs["Scale"].default_value = 0.9; cn.inputs["Detail"].default_value = 6
    lin(nt, dv.outputs[0], cn.inputs["Vector"])
    cr = nt.nodes.new("ShaderNodeMapRange"); cr.inputs["From Min"].default_value = 0.4; cr.inputs["From Max"].default_value = 0.75
    cr.inputs["To Max"].default_value = clouds
    lin(nt, cn.outputs["Fac"], cr.inputs["Value"])
    cm = nt.nodes.new("ShaderNodeMix"); cm.data_type = "RGBA"
    lin(nt, cr.outputs[0], cm.inputs["Factor"]); lin(nt, gm.outputs[2], cm.inputs[6])
    cm.inputs[7].default_value = (*[min(1.0, c * 1.4 + 0.05) for c in horizon], 1)
    lin(nt, cm.outputs[2], bg.inputs["Color"]); lin(nt, bg.outputs[0], out.inputs["Surface"])

# ---------------------------------------------------------------- build
def build(T):
    mats = {}
    mats["stone"] = add_grime(mat_stone("Stone", *T["stone"], snow=T.get("snow", 0.0), scale=2.2, moss=T.get("moss")), moss=T.get("moss"))
    mats["cap"] = mat_stone("Cap", *T.get("capc", ((0.18, 0.17, 0.17), (0.34, 0.32, 0.3))), snow=T.get("snow", 0.0) * 1.2)
    mats["dark"] = add_grime(mat_stone("DarkStone", (0.08, 0.075, 0.075), (0.22, 0.2, 0.19), scale=2.0, snow=T.get("snow", 0.0)))
    mats["found"] = add_grime(mat_stone("Found", *T.get("foundc", ((0.1, 0.1, 0.1), (0.28, 0.27, 0.26))), scale=1.4, moss=(0.08, 0.11, 0.05)), h=SEA_Z + 2.4, dark=0.35, moss=(0.06, 0.09, 0.04))
    mats["bridge"] = add_grime(mat_stone("BridgeStone", *T["stone"], scale=2.0, moss=T.get("moss")), h=-0.5, dark=0.45)
    mats["iron"] = mat_flat("Iron", (0.05, 0.05, 0.055), rough=0.45, metal=0.9)
    mats["wood"] = mat_stone("Wood", (0.07, 0.045, 0.03), (0.2, 0.13, 0.08), rough=0.8, scale=7)
    mats["planks"] = mat_planks("Planks", (0.08, 0.06, 0.04), (0.22, 0.15, 0.1))
    mats["rock"] = mat_cliff("Rock", (0.06, 0.065, 0.07), (0.24, 0.235, 0.23), snow=T.get("snow", 0.0) * 0.6, moss=(0.07, 0.1, 0.05) if not T.get("snow") else None)
    mats["cliff"] = mat_cliff("Cliff", *T.get("cliffc", ((0.06, 0.065, 0.075), (0.26, 0.26, 0.27))), snow=T.get("snow", 0.0))
    mats["gap"] = mat_flat("Gap", T.get("gapc", (0.07, 0.08, 0.05)), rough=0.95)
    mats["hedge"] = mat_stone("Hedge", (0.03, 0.06, 0.025), (0.09, 0.15, 0.06), rough=0.9, scale=5)
    mats["bone"] = mat_stone("Bone", (0.5, 0.46, 0.38), (0.75, 0.7, 0.6), rough=0.7, scale=6)
    mats["roof"] = mat_stone("Roof", (0.12, 0.06, 0.05), (0.3, 0.15, 0.1), rough=0.8, scale=4)
    mats["plaster"] = mat_stone("Plaster", (0.4, 0.37, 0.32), (0.6, 0.56, 0.5), rough=0.9, scale=3)
    mats["statue"] = mat_stone("StatueStone", *T.get("statuec", ((0.3, 0.3, 0.29), (0.5, 0.49, 0.47))), scale=3, moss=(0.15, 0.18, 0.1))

    def mask(x, y):
        wd = edge_dist(x, y)
        snow = 0.0
        if T.get("snow"):
            snow = T["snow"] * (max(0.0, 1.0 - wd / 3.0) + 0.55 * max(0.0, fbm(x, y, 0.22, 9) + 0.05) + 0.7 * max(0.0, fbm(x, y, 0.7, 4) - 0.05))
        wet = 0.0
        if T.get("wet"):
            wet = max(0.0, fbm(x, y, 0.15, 21) - 0.22) * 2.4 * T["wet"]
        return (max(0.0, min(1.0, snow)), max(0.0, min(1.0, wet)))

    fl = T["floor"]
    if fl in ("flag", "slab", "marble", "snowflag", "water"):
        tw, td, bond = {"flag": (1.3, 0.85, 0.5), "slab": (1.5, 1.5, 0.0), "marble": (1.8, 1.8, 0.0), "snowflag": (1.3, 0.85, 0.5),
                        "water": (1.5, 1.5, 0.0)}[fl]
        tm = mat_tiles("Tiles", *T["floorc"], gloss=T.get("gloss", 0.7), snow=bool(T.get("snow")))
        tiled_floor(tm, tw, td, bond=bond, mask_fn=mask, tilt=0.3 if fl == "marble" else 0.7, irregular=fl != "marble")
        if fl == "water":
            wm = mat_flat("Shallows", (0.04, 0.12, 0.11), rough=0.03, alpha=0.78)
            wm.surface_render_method = "BLENDED" if hasattr(wm, "surface_render_method") else None
            ground_poly(wm, step=2.0, z=0.07, margin=0.0)
    elif fl == "planks":
        plank_floor(mat_tiles("PlankTiles", *T["floorc"], gloss=0.8), mask_fn=mask)
    else:
        gm = mat_ground("Ground", fl, *T["floorc"], extra=T.get("lava"))
        ground_poly(gm, step=0.5, mask_fn=mask, z_fn=lambda x, y: 0.08 * fbm(x, y, 0.4, 2) - 0.02, margin=-0.6)
    if T.get("carpet"):
        cloth = mat_torn_cloth("Carpet", tuple(c * 0.45 for c in T["carpet"]), T["carpet"])
        mid = (G_SW + G_NE) / 2; d = (G_NE - G_SW)
        box("carpet", (mid.x, mid.y, 0.02), (2.4, d.length - 6, 0.02), (0, 0, math.atan2(d.y, d.x) - math.pi / 2), cloth, bevel=0)
    if T.get("path"):
        pm = mat_ground("Path", "gravel", *T["path"])
        mid = (G_SW + G_NE) / 2; d = (G_NE - G_SW)
        box("path", (mid.x, mid.y, 0.01), (3.0, d.length - 4, 0.04), (0, 0, math.atan2(d.y, d.x) - math.pi / 2), pm, bevel=0)

    if fl in ("flag", "slab", "marble", "snowflag", "water", "planks") and not T.get("grass"):
        grass_tufts(mat_flat("WeedMat", T.get("weed", (0.1, 0.11, 0.06)), rough=0.9), 260, near_walls=True)
    for i in range(T.get("debris", 45)):
        x, y = R(-RX, RX), R(-RY, RY)
        if inside(x, y, 0.3) and (edge_dist(x, y) < 3.0 or rng.random() < 0.25):
            s = R(0.08, 0.3)
            rock("debris", (x, y, s * 0.3), (s * R(0.8, 1.5), s, s * 0.6), mats["stone"], sub=1, rough=0.3, seed=i)
    build_walls(T, mats)
    gate(T, mats)
    island(T, mats)
    bridge(Vector(L(0, 1.0)[:2]), N_OUT, 60, T, mats)
    bridge(Vector(L(0, -1.0, g=G_SW, n=-N_OUT)[:2]), -N_OUT, 40, T, mats)
    # distant_isles(T, mats)  # silhouettes read as lit blobs at this zoom
    cover(T, mats)
    EXTRA_BUILD(T, mats)
    if T.get("grass"):
        grass_tufts(mat_flat("GrassMat", T["grass"], rough=0.9), T.get("grass_n", 400), near_walls=T.get("grass_walls", True))
    if T.get("wall_lights", True): wall_lights(T, mats)
    for f in T.get("extras", []):
        f(mats)
    sea2(T["sea"][0], T["sea"][1], wave_scale=T.get("waves", 0.8), choppy=1.15, foam=T.get("foam", 0.35))
    world2(T["sky"][0], T["sky"][1], T.get("sky_str", 0.5), T["sun"][0], T["sun"][1], T.get("glow", tuple(c * 0.6 for c in T["sun_col"])), T.get("clouds", 0.25))
    el, az = T["sun"]
    sun((90 - el, 0, az), T.get("sun_str", 2.2), T["sun_col"], angle=T.get("sun_angle", 2.0))

# ---------------------------------------------------------------- themes
def pos_braziers(mats, pts=((-14, -26.5 + 3.5), (-8.5, -22), (14, 20), (8, 25), (-14, 26), (14, -26))):
    for x, y in pts:
        if inside(x, y, 1.0): brazier(x, y, mats["iron"])

def altar_north(mats):
    box("altar", (-4, RY - 2.6, 0.55), (3.2, 1.3, 1.1), (0, 0, 0), mats["dark"])
    box("altar_top", (-4, RY - 2.6, 1.15), (3.5, 1.5, 0.12), (0, 0, 0), mats["cap"])
    wax = mat_flat("Wax", (0.85, 0.8, 0.65), rough=0.6)
    for i in range(14):
        cx, cy = -4 + R(-1.5, 1.5), RY - 2.6 + R(-0.55, 0.55); h = R(0.12, 0.4)
        cylinder("candle", (cx, cy, 1.21 + h / 2), 0.04, h, wax, verts=6)
        torch_light((cx, cy, 1.21 + h + 0.05), power=6, radius=0.02, flame=True)
    torch_light((-4, RY - 4, 2.0), power=200, radius=0.5, flame=False)

def throne_north(mats):
    x, y = -4, RY - 3
    for k in range(3):
        box("dais", (x, y + 0.4 - k * 0.0, 0.2 + k * 0.4), (6 - k * 1.4, 4 - k * 1.0, 0.4), (0, 0, 0), mats["cap"], bevel=0.04)
    box("throne_seat", (x, y + 0.3, 1.6), (1.4, 1.2, 0.5), (0, 0, 0), mats["dark"])
    box("throne_back", (x, y + 0.85, 2.9), (1.4, 0.3, 2.6), (0, 0, 0), mats["dark"])
    gold = mat_flat("Gold", (0.6, 0.42, 0.15), rough=0.3, metal=1.0)
    for s_ in (-1, 1):
        box("throne_arm", (x + s_ * 0.75, y + 0.3, 2.0), (0.2, 1.2, 0.3), (0, 0, 0), gold)
        statue(x + s_ * 6.5, y - 0.5, mats, scale=2.0, seed=40 + s_, yaw=math.radians(-90))
    box("crown_spike", (x, y + 0.85, 4.4), (0.4, 0.3, 0.6), (0, 0, 0), gold)

def ruined_chapel_extras(mats):
    altar_north(mats); pos_braziers(mats)

def harbor_extras(mats):
    boats(mats, 5)
    for i in range(6):
        x, y = R(-RX + 2, RX - 2), R(-RY + 3, RY - 3)
        if not inside(x, y, 2): continue
        box("net", (x, y, 0.02), (R(1.5, 2.5), R(1.0, 2.0), 0.03), (0, 0, R(0, 3)), mat_flat("Net", (0.25, 0.22, 0.15), rough=0.9), bevel=0)
    # crane by the north edge
    x, y = 6, RY - 2
    box("crane_base", (x, y, 0.6), (2, 2, 1.2), (0, 0, 0), mats["wood"])
    cylinder("crane_mast", (x, y, 4.2), 0.2, 6.0, mats["wood"], verts=8)
    box("crane_jib", (x, y + 3.0, 6.8), (0.25, 7.0, 0.25), (math.radians(-12), 0, 0), mats["wood"])
    pos_braziers(mats)

def lighthouse_extras(mats):
    lighthouse(-RX - 9, RY + 12, mats, (1.0, 0.85, 0.6))
    ice_floes(mat_cliff("Ice", (0.55, 0.65, 0.72), (0.82, 0.88, 0.92), snow=0.0), 60)
    pos_braziers(mats)

def graveyard_extras(mats):
    graves(mats, 46)
    bark = mat_flat("Bark", (0.05, 0.045, 0.04), rough=0.9)
    for (x, y) in ((-14, 8), (12, -20), (13, 12), (-6, -18), (-14, -12)):
        dead_tree(x, y, R(4, 6), bark)
    # mausoleum at the north
    x, y = -5, RY - 4
    box("maus", (x, y, 1.8), (6, 4.5, 3.6), (0, 0, 0), mats["stone"])
    box("maus_roof", (x, y, 3.9), (6.6, 5.0, 0.6), (0, 0, 0), mats["cap"])
    for s_ in (-1, 1):
        cylinder("maus_col", (x + s_ * 2.0, y - 2.6, 1.6), 0.25, 3.2, mats["cap"], verts=10)
    torch_light((x, y - 3.2, 1.6), power=150, radius=0.3, flame=False, color=(0.6, 0.8, 1.0))
    for i in range(10):
        x, y = R(-RX + 2, RX - 2), R(-RY + 4, RY - 4)
        if inside(x, y, 2):
            cylinder("lantern_post", (x, y, 0.8), 0.05, 1.6, mats["iron"], verts=6)
            torch_light((x, y, 1.7), color=(1.0, 0.6, 0.3), power=60, radius=0.05, flame=True)

def village_extras(mats):
    for i in range(8):
        x, y = R(-RX + 4, RX - 4), R(-RY + 6, RY - 6)
        if inside(x, y, 3):
            bonfire_small(x, y, mats)
    cylinder("well", (3, 2, 0.45), 1.2, 0.9, mats["stone"], verts=16, bevel=0.03)
    for s_ in (-1, 1): box("well_post", (3 + s_ * 1.0, 2, 1.4), (0.15, 0.15, 1.9), (0, 0, 0), mats["wood"])
    box("well_roof", (3, 2, 2.5), (2.6, 1.6, 0.12), (0, 0, 0), mats["roof"])

def bonfire_small(x, y, mats):
    emb = mat_flat("Embers", (0.1, 0.03, 0.01), emit=(1.0, 0.35, 0.06), emit_str=20)
    rock("embers", (x, y, 0.05), (R(0.6, 1.4), R(0.6, 1.2), 0.15), emb, sub=2, rough=0.5, seed=int(x * 3 + y))
    for k in range(4):
        rock("flame", (x + R(-0.4, 0.4), y + R(-0.4, 0.4), 0.3 + k * 0.2), (0.15, 0.15, 0.4), mat_flat("fl", (1, 0.5, 0.1), emit=(1.0, 0.42, 0.08), emit_str=25), sub=1, rough=0.3, coll=FX, seed=k)
    torch_light((x, y, 1.0), power=900, radius=0.5, flame=False)

def shrine_extras(mats):
    bonfire(0.5, 1.0, mats)
    trunk = mat_flat("Trunk", (0.12, 0.09, 0.06), rough=0.9)
    leaf = mat_stone("Leaf", (0.06, 0.1, 0.03), (0.18, 0.24, 0.07), rough=0.85, scale=4)
    leaf2 = mat_stone("Leaf2", (0.12, 0.1, 0.03), (0.35, 0.25, 0.06), rough=0.85, scale=4)
    for (x, y) in ((-13, 10), (12, -12), (-12, -6), (12.5, 18), (-6, 25)):
        leafy_tree(x, y, R(5, 7), trunk, leaf, leaf2)
    flowers = mat_flat("Flowers", (0.7, 0.55, 0.2), rough=0.8)
    grass_tufts(flowers, 80, near_walls=False)

def garden_extras(mats):
    fountain(0.5, 0.0, mats)
    for (x, y) in ((-10, -16), (10, -14), (-11, 14), (11, 16)):
        statue(x, y, mats, scale=1.3, seed=int(x), yaw=math.atan2(-y, -x))
    for (x, y) in ((-4, -20), (6, 20), (-14, 2), (14, 0)):
        rock("topiary", (x, y, 1.0), (1.0, 1.0, 1.0), mats["hedge"], sub=2, rough=0.1, seed=int(x * y))
        cylinder("pot", (x, y, 0.3), 0.9, 0.6, mats["stone"], verts=16, bevel=0.03)

def ossuary_extras(mats):
    for i in range(14):
        x, y = R(-RX + 2, RX - 2), R(-RY + 3, RY - 3)
        if inside(x, y, 1.5) and edge_dist(x, y) < 4:
            for k in range(int(R(10, 20))):
                if rng.random() < 0.3:
                    rock("skull", (x + R(-0.6, 0.6), y + R(-0.6, 0.6), R(0.1, 0.4)), (0.13, 0.12, 0.14), mats["bone"], sub=1, rough=0.1, seed=k + i * 30)
                else:
                    cylinder("bone", (x + R(-0.7, 0.7), y + R(-0.7, 0.7), R(0.05, 0.35)), 0.035, R(0.35, 0.6), mats["bone"], verts=5,
                             rot=(math.radians(90), 0, R(0, 3.14)))
    for i in range(8):
        x, y = R(-RX + 3, RX - 3), R(-RY + 4, RY - 4)
        if inside(x, y, 2):
            cylinder("candle_cluster", (x, y, 0.15), 0.3, 0.3, mat_flat("Wax", (0.8, 0.75, 0.6), rough=0.6), verts=8)
            torch_light((x, y, 0.6), color=(0.55, 1.0, 0.6), power=120, radius=0.1, flame=False)

def volcano_extras(mats):
    emb = mat_flat("Lava", (0.1, 0.02, 0.0), emit=(1.0, 0.3, 0.04), emit_str=18)
    for i in range(5):
        x, y = R(-RX + 4, RX - 4), R(-RY + 6, RY - 6)
        if inside(x, y, 3):
            rock("lava_pool", (x, y, -0.02), (R(1.2, 2.4), R(1.0, 2.0), 0.06), emb, sub=2, rough=0.4, seed=i)
            torch_light((x, y, 1.0), color=(1.0, 0.35, 0.08), power=700, radius=1.0, flame=False)
    pos_braziers(mats)

def arena_extras(mats):
    banners(mats, (0.45, 0.08, 0.05))
    for (x, y) in ((-14, -2), (14, 4), (-14, 14)):
        box("rack", (x, y, 0.9), (0.2, 2.4, 1.8), (0, 0, 0), mats["wood"])
        for k in range(4):
            box("rack_sword", (x + 0.15, y - 0.9 + k * 0.6, 1.0), (0.05, 0.08, 1.6), (0, math.radians(8), 0), mats["iron"], bevel=0)
    pos_braziers(mats)

def monastery_extras(mats):
    for (x, y) in ((-6, RY - 3), (4, RY - 3)):
        box("bell_frame", (x, y, 2.0), (3.0, 0.3, 4.0), (0, 0, 0), mats["wood"])
        cylinder("bell", (x, y, 2.6), 0.7, 1.2, mat_flat("Bronze", (0.35, 0.25, 0.12), rough=0.35, metal=1.0), verts=16, r2=0.35)
    for i in range(12):
        x, y = R(-RX + 2, RX - 2), R(-RY + 3, RY - 3)
        if inside(x, y, 1.5) and edge_dist(x, y) < 3:
            box("stone_lantern", (x, y, 0.6), (0.4, 0.4, 1.2), (0, 0, 0), mats["cap"])
            box("sl_roof", (x, y, 1.3), (0.7, 0.7, 0.15), (0, 0, 0), mats["dark"])
            torch_light((x, y, 1.05), color=(1.0, 0.65, 0.35), power=60, radius=0.05, flame=False)
    pos_braziers(mats, pts=((-8, -22), (14, 20)))

def throne_extras(mats):
    throne_north(mats); pos_braziers(mats)

def temple_extras(mats):
    altar_north(mats)
    for (x, y) in ((-6, -10), (6, -10), (-6, 0), (6, 0), (-6, 10), (6, 10)):
        pillar((x, y), 5.5, 1.2, mats["stone"], mats["cap"])

def wreck_extras(mats):
    hull((-6, 6, -0.3), 9.0, 3.4, 1.8, mats["planks"], mats["wood"], tilt=26.0, yaw=20.0, seed=3)
    cylinder("mast", (5, -4, 0.3), 0.26, 9.0, mats["wood"], verts=8, rot=(math.radians(90), 0, math.radians(35)))
    kelp = mat_flat("Kelp", (0.07, 0.09, 0.04), rough=0.6)
    for i in range(24):
        x, y = R(-RX + 1, RX - 1), R(-RY + 1, RY - 1)
        if inside(x, y, 0.5):
            box("kelp", (x, y, 0.02), (R(0.4, 1.2), R(0.08, 0.2), 0.02), (0, 0, R(0, 3.14)), kelp, bevel=0)
    for (x, y) in ((-14, -14), (14, 14), (-13, 12)):
        cylinder("pole", (x, y, 1.3), 0.08, 2.6, mats["wood"], verts=6)
        torch_light((x, y, 2.6), color=(1.0, 0.6, 0.3), power=240, radius=0.1, flame=True)

DUSK = dict(sun=(13, 155), sun_col=(1.0, 0.6, 0.36), sun_str=2.2, sky=((0.1, 0.11, 0.22), (0.75, 0.48, 0.38)), sky_str=0.45,
            haze_col=(0.55, 0.38, 0.32), haze=0.4, gate_light=(1.0, 0.72, 0.45), mist_start=75, mist_depth=120)

THEMES = {
    "01_drowned_cathedral": dict(DUSK, title="Drowned Cathedral", walls="arcade", wall_h=6.5, north_h=6.5, south_h=0.8, south_style="ruin",
        floor="slab", floorc=((0.17, 0.155, 0.14), (0.38, 0.35, 0.31)), wet=0.7, stone=((0.3, 0.27, 0.24), (0.55, 0.5, 0.44)), moss=(0.14, 0.17, 0.08),
        gate="gothic", bridge="stone", cover="columns", carpet=(0.33, 0.06, 0.05), extras=[ruined_chapel_extras],
        sea=((0.02, 0.05, 0.09), (0.1, 0.19, 0.23))),
    "02_frost_bastion": dict(title="Frost Bastion", walls="crenel", wall_h=3.4, north_h=1.6, south_h=0.9, buttress=True,
        floor="snowflag", floorc=((0.27, 0.27, 0.29), (0.47, 0.45, 0.43)), snow=0.9, stone=((0.14, 0.14, 0.16), (0.36, 0.35, 0.34)),
        gate="portcullis", bridge="stone", cover="columns", extras=[lambda m: (banners(m, (0.4, 0.06, 0.05)), pos_braziers(m),
        ice_floes(mat_cliff("Ice", (0.55, 0.65, 0.72), (0.82, 0.88, 0.92)), 50))],
        sun=(12, 155), sun_col=(1.0, 0.55, 0.32), sun_str=2.0, sky=((0.16, 0.2, 0.32), (0.7, 0.5, 0.45)), sky_str=0.5,
        haze_col=(0.5, 0.52, 0.6), haze=0.4, gate_light=(1.0, 0.68, 0.4), sea=((0.02, 0.06, 0.09), (0.07, 0.2, 0.23))),
    "03_wreck_cove": dict(title="Wreck Cove", walls="cliff", wall_h=4.5, north_h=2.0, south_h=1.2, found_style="cliff",
        floor="sand", floorc=((0.24, 0.225, 0.2), (0.44, 0.41, 0.36)), snow=0.5, wet=0.4, stone=((0.14, 0.13, 0.12), (0.32, 0.3, 0.28)),
        gate="cave", bridge="wood", cover="rocks", cover_n=6, extras=[wreck_extras],
        sun=(10, 155), sun_col=(1.0, 0.66, 0.42), sun_str=1.9, sky=((0.2, 0.23, 0.3), (0.65, 0.55, 0.5)), sky_str=0.5,
        haze_col=(0.5, 0.48, 0.48), haze=0.45, gate_light=(1.0, 0.7, 0.42), sea=((0.02, 0.055, 0.075), (0.09, 0.22, 0.24)), waves=1.1),
    "04_moonlit_graveyard": dict(title="Moonlit Graveyard", walls="fence", wall_h=1.6, north_h=1.6, south_h=1.0, south_style="fence",
        floor="grass", floorc=((0.05, 0.06, 0.04), (0.14, 0.15, 0.09)), stone=((0.2, 0.2, 0.21), (0.4, 0.4, 0.4)), moss=(0.1, 0.13, 0.07),
        gate="iron", bridge="stone", cover="tombs", cover_n=6, path=((0.08, 0.075, 0.07), (0.17, 0.16, 0.14)), grass=(0.12, 0.14, 0.07), grass_n=600,
        extras=[graveyard_extras], wall_lights=False,
        sun=(28, 155), sun_col=(0.6, 0.72, 1.0), sun_str=1.6, sky=((0.02, 0.03, 0.07), (0.12, 0.15, 0.25)), sky_str=0.35,
        haze_col=(0.12, 0.16, 0.24), haze=0.55, gate_light=(1.0, 0.7, 0.4), gate_power=140000, exposure=1.0,
        sea=((0.01, 0.025, 0.05), (0.04, 0.08, 0.12))),
    "05_burning_hamlet": dict(title="Burning Hamlet", walls="timber", wall_h=3.6, north_h=3.6, south_h=0.9, south_style="palisade",
        floor="gravel", floorc=((0.12, 0.11, 0.1), (0.26, 0.23, 0.2)), stone=((0.14, 0.13, 0.12), (0.3, 0.28, 0.26)),
        gate="timber", bridge="wood", cover="barricades", extras=[village_extras], wall_lights=False,
        sun=(6, 155), sun_col=(1.0, 0.4, 0.2), sun_str=1.6, sky=((0.05, 0.04, 0.06), (0.4, 0.15, 0.08)), sky_str=0.5,
        haze_col=(0.35, 0.16, 0.1), haze=0.55, gate_light=(1.0, 0.65, 0.35), sea=((0.015, 0.025, 0.04), (0.06, 0.08, 0.1)), exposure=0.9),
    "06_lighthouse_isle": dict(title="Lighthouse Isle", walls="ashlar", wall_h=2.6, north_h=1.4, south_h=0.8,
        floor="snowflag", floorc=((0.24, 0.24, 0.26), (0.42, 0.41, 0.4)), snow=0.7, stone=((0.16, 0.16, 0.18), (0.38, 0.37, 0.36)),
        gate="round", bridge="stone", cover="crates", extras=[lighthouse_extras],
        sun=(7, 155), sun_col=(1.0, 0.5, 0.35), sun_str=1.6, sky=((0.08, 0.12, 0.24), (0.55, 0.42, 0.45)), sky_str=0.45,
        haze_col=(0.32, 0.36, 0.48), haze=0.45, gate_light=(1.0, 0.7, 0.42), sea=((0.015, 0.04, 0.07), (0.05, 0.14, 0.18))),
    "07_sunken_harbor": dict(title="Sunken Harbor", walls="ashlar", wall_h=1.2, north_h=0.9, south_h=0.7,
        floor="planks", floorc=((0.11, 0.08, 0.055), (0.27, 0.19, 0.12)), wet=0.5, stone=((0.16, 0.15, 0.14), (0.36, 0.34, 0.31)),
        gate="timber", bridge="wood", cover="crates", extras=[harbor_extras],
        sun=(16, 155), sun_col=(1.0, 0.82, 0.62), sun_str=1.8, sky=((0.35, 0.4, 0.45), (0.75, 0.68, 0.6)), sky_str=0.55,
        haze_col=(0.62, 0.62, 0.62), haze=0.6, mist_start=60, gate_light=(1.0, 0.8, 0.55), sea=((0.03, 0.07, 0.08), (0.1, 0.22, 0.22))),
    "08_overgrown_shrine": dict(title="Overgrown Bonfire Shrine", walls="ruin", wall_h=3.0, north_h=2.0, south_h=0.9,
        floor="grass", floorc=((0.06, 0.08, 0.03), (0.2, 0.22, 0.09)), stone=((0.26, 0.25, 0.22), (0.5, 0.48, 0.42)), moss=(0.12, 0.17, 0.06),
        gate="round", bridge="stone", cover="columns", path=((0.22, 0.2, 0.17), (0.4, 0.37, 0.32)), grass=(0.16, 0.2, 0.07), grass_n=900,
        grass_walls=False, extras=[shrine_extras],
        sun=(14, 155), sun_col=(1.0, 0.7, 0.4), sun_str=2.4, sky=((0.2, 0.28, 0.4), (0.9, 0.62, 0.38)), sky_str=0.55,
        haze_col=(0.6, 0.48, 0.34), haze=0.4, gate_light=(1.0, 0.78, 0.5), sea=((0.02, 0.06, 0.09), (0.08, 0.22, 0.24))),
    "09_royal_garden": dict(title="Forsaken Royal Garden", walls="hedge", wall_h=2.6, north_h=1.8, south_h=1.0,
        floor="gravel", floorc=((0.17, 0.16, 0.14), (0.32, 0.3, 0.27)), stone=((0.4, 0.38, 0.35), (0.65, 0.62, 0.58)), statuec=((0.42, 0.42, 0.4), (0.65, 0.64, 0.6)),
        gate="marble", bridge="stone", cover="hedges", cover_n=6, grass=(0.12, 0.16, 0.06), grass_n=500, extras=[garden_extras], wall_lights=False,
        sun=(20, 155), sun_col=(1.0, 0.85, 0.7), sun_str=1.6, sky=((0.4, 0.44, 0.5), (0.7, 0.68, 0.66)), sky_str=0.65,
        haze_col=(0.6, 0.6, 0.62), haze=0.45, gate_light=(1.0, 0.82, 0.6), sea=((0.03, 0.07, 0.09), (0.1, 0.22, 0.24))),
    "10_storm_throne": dict(title="Storm-Torn Throne Hall", walls="ruin", wall_h=5.5, north_h=5.5, south_h=1.0,
        floor="marble", floorc=((0.04, 0.04, 0.045), (0.16, 0.15, 0.15)), gloss=0.12, wet=0.5, stone=((0.08, 0.08, 0.09), (0.22, 0.21, 0.22)),
        capc=((0.25, 0.22, 0.15), (0.45, 0.38, 0.22)), gate="gothic", bridge="stone", cover="statues", cover_n=6,
        statuec=((0.15, 0.15, 0.16), (0.3, 0.3, 0.31)), carpet=(0.12, 0.1, 0.25), extras=[throne_extras],
        sun=(18, 155), sun_col=(0.75, 0.82, 1.0), sun_str=1.4, sky=((0.06, 0.07, 0.1), (0.3, 0.32, 0.36)), sky_str=0.4,
        haze_col=(0.25, 0.27, 0.32), haze=0.5, exposure=0.6, gate_light=(1.0, 0.75, 0.45), sea=((0.015, 0.03, 0.05), (0.06, 0.1, 0.13)), waves=1.3),
    "11_misty_monastery": dict(title="Misty Monastery", walls="ashlar", wall_h=3.2, north_h=1.8, south_h=0.9, buttress=True,
        floor="flag", floorc=((0.36, 0.35, 0.32), (0.58, 0.56, 0.52)), wet=0.3, stone=((0.32, 0.31, 0.29), (0.58, 0.56, 0.52)), moss=(0.13, 0.16, 0.08),
        capc=((0.12, 0.1, 0.09), (0.25, 0.2, 0.17)), gate="timber", bridge="stone", cover="pillars", cover_n=6, grass=(0.13, 0.16, 0.08), grass_n=300,
        extras=[monastery_extras],
        sun=(11, 155), sun_col=(1.0, 0.8, 0.62), sun_str=1.8, sky=((0.45, 0.48, 0.55), (0.85, 0.75, 0.68)), sky_str=0.6,
        haze_col=(0.75, 0.72, 0.7), haze=0.7, mist_start=55, mist_depth=110, gate_light=(1.0, 0.82, 0.6), sea=((0.04, 0.08, 0.1), (0.12, 0.22, 0.24))),
    "12_ossuary_court": dict(title="Ossuary Court", walls="niche", wall_h=3.6, north_h=3.0, south_h=0.9,
        floor="flag", floorc=((0.16, 0.15, 0.13), (0.32, 0.3, 0.27)), wet=0.4, stone=((0.16, 0.15, 0.13), (0.34, 0.32, 0.29)), moss=(0.08, 0.12, 0.06),
        gate="gothic", bridge="stone", cover="tombs", extras=[ossuary_extras], lamp=(0.6, 1.0, 0.65), torch_power=220,
        sun=(8, 155), sun_col=(0.85, 0.75, 0.6), sun_str=1.0, sky=((0.04, 0.05, 0.06), (0.25, 0.25, 0.22)), sky_str=0.35,
        haze_col=(0.2, 0.26, 0.22), haze=0.55, gate_light=(1.0, 0.72, 0.42), sea=((0.01, 0.03, 0.035), (0.04, 0.09, 0.09)), exposure=0.9),
    "13_ashen_basalt": dict(title="Ashen Basalt Isle", walls="basalt", wall_h=4.0, north_h=2.4, south_h=1.2, found_style="cliff",
        floor="ash", floorc=((0.07, 0.065, 0.06), (0.2, 0.19, 0.18)), lava=3.0, stone=((0.06, 0.06, 0.065), (0.18, 0.17, 0.17)),
        cliffc=((0.03, 0.03, 0.035), (0.14, 0.13, 0.13)), gate="basalt", bridge="stone", cover="basalt", extras=[volcano_extras],
        sun=(8, 155), sun_col=(1.0, 0.38, 0.15), sun_str=1.6, sky=((0.08, 0.04, 0.04), (0.6, 0.2, 0.08)), sky_str=0.45,
        haze_col=(0.35, 0.14, 0.08), haze=0.5, gate_light=(1.0, 0.55, 0.25), sea=((0.015, 0.02, 0.03), (0.05, 0.07, 0.08))),
    "14_gladiator_pit": dict(title="Gladiator Pit", walls="tiers", wall_h=3.0, north_h=2.0, south_h=0.9, south_style="ashlar",
        floor="sand", floorc=((0.36, 0.3, 0.22), (0.58, 0.5, 0.38)), stone=((0.3, 0.26, 0.21), (0.55, 0.48, 0.4)),
        gate="portcullis", bridge="stone", cover="barricades", extras=[arena_extras],
        sun=(10, 155), sun_col=(1.0, 0.6, 0.34), sun_str=2.4, sky=((0.18, 0.18, 0.3), (0.9, 0.55, 0.35)), sky_str=0.5,
        haze_col=(0.6, 0.42, 0.32), haze=0.4, gate_light=(1.0, 0.72, 0.42), sea=((0.02, 0.06, 0.09), (0.09, 0.22, 0.24))),
    "15_tidal_temple": dict(title="Tidal Temple", walls="arcade", wall_h=6.0, north_h=6.0, south_h=0.8, south_style="ruin",
        floor="water", floorc=((0.2, 0.22, 0.2), (0.4, 0.42, 0.38)), stone=((0.24, 0.26, 0.24), (0.46, 0.48, 0.44)), moss=(0.08, 0.16, 0.1),
        gate="gothic", bridge="stone", cover="columns", cover_n=4, extras=[temple_extras],
        sun=(20, 155), sun_col=(1.0, 0.86, 0.66), sun_str=2.0, sky=((0.25, 0.35, 0.45), (0.7, 0.72, 0.68)), sky_str=0.55,
        haze_col=(0.42, 0.55, 0.55), haze=0.45, gate_light=(1.0, 0.85, 0.6), sea=((0.02, 0.07, 0.08), (0.06, 0.24, 0.24))),
}

if not globals().get("NO_MAIN"):
  T = THEMES[THEME]
  build(T)
  chars2()
  auto_camera()
  render2(T)
  compositor(T)
  stats()
  bpy.ops.render.render(write_still=True)
  print("RENDERED", OUT)
