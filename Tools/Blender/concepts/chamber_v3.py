# Survivor Souls - Chamber concepts v3: v2 island chamber + impassable ponds / pools and Archero-style natural wall runs,
# more set dressing, and a second shot from the in-game camera distance.
# Run: blender -b --factory-startup -P chamber_v3.py -- <theme> <out.png> [knight.glb|-] [zombie.fbx|-] [w h]
import os, sys, math
NO_MAIN = True
OBST = []

def _local(o, x, y):
    cx, cy = o["c"]; a = math.radians(o.get("rot", 0.0))
    dx, dy = x - cx, y - cy
    return dx * math.cos(a) + dy * math.sin(a), -dx * math.sin(a) + dy * math.cos(a)

def pond_inside(o, x, y, m=0.0):
    u, v = _local(o, x, y); rx, ry = o["r"]
    if o.get("shape", "ellipse") == "rect":
        return abs(u) <= rx + m and abs(v) <= ry + m
    return (u / (rx + m)) ** 2 + (v / (ry + m)) ** 2 <= 1.0

def FLOOR_SKIP(x, y):
    return any(o["t"] == "pond" and pond_inside(o, x, y, 0.35) for o in OBST)

def GROUND_DIP(x, y):
    d = 0.0
    for o in OBST:
        if o["t"] != "pond": continue
        if pond_inside(o, x, y, 0.0): d = max(d, 0.75)
        elif pond_inside(o, x, y, 0.8): d = max(d, 0.35)
    return d

def EXTRA_BUILD(T, mats):
    build_obstacles(T, mats)
    for f in T.get("details", []):
        f(T, mats)

_here = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(_here, "chamber_v2.py"), encoding="utf-8").read())

# ---------------------------------------------------------------- pond materials
def mat_pond(kind):
    m = bpy.data.materials.new("Pond_" + kind); nt, out = nodes_of(m)
    p = principled(nt); tc = nt.nodes.new("ShaderNodeTexCoord")
    rip = nt.nodes.new("ShaderNodeTexNoise"); rip.inputs["Scale"].default_value = 3.5; rip.inputs["Detail"].default_value = 6
    lin(nt, tc.outputs["Object"], rip.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.08
    lin(nt, rip.outputs["Fac"], bump.inputs["Height"]); lin(nt, bump.outputs["Normal"], p.inputs["Normal"])
    p.inputs["Specular IOR Level"].default_value = 0.7
    if kind == "water":
        p.inputs["Base Color"].default_value = (0.035, 0.075, 0.08, 1); p.inputs["Roughness"].default_value = 0.02
    elif kind == "brine":
        p.inputs["Base Color"].default_value = (0.05, 0.3, 0.28, 1); p.inputs["Roughness"].default_value = 0.04
    elif kind == "tar":
        p.inputs["Base Color"].default_value = (0.006, 0.006, 0.007, 1); p.inputs["Roughness"].default_value = 0.08
    elif kind == "blood":
        p.inputs["Base Color"].default_value = (0.08, 0.004, 0.004, 1); p.inputs["Roughness"].default_value = 0.06
    elif kind == "bog":
        scum = nt.nodes.new("ShaderNodeTexNoise"); scum.inputs["Scale"].default_value = 0.9; scum.inputs["Detail"].default_value = 8
        lin(nt, tc.outputs["Object"], scum.inputs["Vector"])
        mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 0.5; mr.inputs["From Max"].default_value = 0.6
        lin(nt, scum.outputs["Fac"], mr.inputs["Value"])
        mx = nt.nodes.new("ShaderNodeMix"); mx.data_type = "RGBA"
        lin(nt, mr.outputs[0], mx.inputs["Factor"]); mx.inputs[6].default_value = (0.02, 0.025, 0.012, 1); mx.inputs[7].default_value = (0.09, 0.12, 0.03, 1)
        lin(nt, mx.outputs[2], p.inputs["Base Color"])
        rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.05; rr.inputs["To Max"].default_value = 0.7
        lin(nt, mr.outputs[0], rr.inputs["Value"]); lin(nt, rr.outputs[0], p.inputs["Roughness"])
    elif kind == "ice":
        vor = nt.nodes.new("ShaderNodeTexVoronoi"); vor.feature = "DISTANCE_TO_EDGE"; vor.inputs["Scale"].default_value = 0.9
        lin(nt, tc.outputs["Object"], vor.inputs["Vector"])
        cr = nt.nodes.new("ShaderNodeMapRange"); cr.inputs["From Min"].default_value = 0.03; cr.inputs["From Max"].default_value = 0.0
        lin(nt, vor.outputs["Distance"], cr.inputs["Value"])
        sn = nt.nodes.new("ShaderNodeTexNoise"); sn.inputs["Scale"].default_value = 0.8; sn.inputs["Detail"].default_value = 6
        lin(nt, tc.outputs["Object"], sn.inputs["Vector"])
        sm = nt.nodes.new("ShaderNodeMapRange"); sm.inputs["From Min"].default_value = 0.55; sm.inputs["From Max"].default_value = 0.62
        lin(nt, sn.outputs["Fac"], sm.inputs["Value"])
        mx = nt.nodes.new("ShaderNodeMix"); mx.data_type = "RGBA"
        add = nt.nodes.new("ShaderNodeMath"); add.operation = "MAXIMUM"
        lin(nt, cr.outputs[0], add.inputs[0]); lin(nt, sm.outputs[0], add.inputs[1])
        lin(nt, add.outputs[0], mx.inputs["Factor"]); mx.inputs[6].default_value = (0.2, 0.32, 0.4, 1); mx.inputs[7].default_value = (0.78, 0.84, 0.9, 1)
        lin(nt, mx.outputs[2], p.inputs["Base Color"])
        rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.06; rr.inputs["To Max"].default_value = 0.7
        lin(nt, add.outputs[0], rr.inputs["Value"]); lin(nt, rr.outputs[0], p.inputs["Roughness"])
    elif kind == "lava":
        vor = nt.nodes.new("ShaderNodeTexVoronoi"); vor.feature = "DISTANCE_TO_EDGE"; vor.inputs["Scale"].default_value = 1.4
        lin(nt, tc.outputs["Object"], vor.inputs["Vector"])
        cr = nt.nodes.new("ShaderNodeMapRange"); cr.inputs["From Min"].default_value = 0.12; cr.inputs["From Max"].default_value = 0.0
        lin(nt, vor.outputs["Distance"], cr.inputs["Value"])
        p.inputs["Base Color"].default_value = (0.04, 0.02, 0.015, 1); p.inputs["Roughness"].default_value = 0.6
        p.inputs["Emission Color"].default_value = (1.0, 0.33, 0.05, 1)
        em = nt.nodes.new("ShaderNodeMath"); em.operation = "MULTIPLY"; em.inputs[1].default_value = 22
        lin(nt, cr.outputs[0], em.inputs[0]); lin(nt, em.outputs[0], p.inputs["Emission Strength"])
    lin(nt, p.outputs[0], out.inputs[0])
    return m

def shape_points(o, n=56, jitter=0.0, m=0.0):
    cx, cy = o["c"]; rx, ry = o["r"]; a = math.radians(o.get("rot", 0.0)); pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        if o.get("shape", "ellipse") == "rect":
            # walk the rectangle perimeter evenly
            per = 4 * (rx + ry); s = per * k / n
            if s < 2 * rx: u, v = -rx + s, -ry
            elif s < 2 * rx + 2 * ry: u, v = rx, -ry + (s - 2 * rx)
            elif s < 4 * rx + 2 * ry: u, v = rx - (s - 2 * rx - 2 * ry), ry
            else: u, v = -rx, ry - (s - 4 * rx - 2 * ry)
            u += math.copysign(m, u) if abs(u) >= rx - 1e-6 else 0; v += math.copysign(m, v) if abs(v) >= ry - 1e-6 else 0
        else:
            j = 1.0 + jitter * fbm(math.cos(t) * 2, math.sin(t) * 2, 1.0, cx + cy)
            u, v = math.cos(t) * (rx + m) * j, math.sin(t) * (ry + m) * j
        pts.append(Vector((cx + u * math.cos(a) - v * math.sin(a), cy + u * math.sin(a) + v * math.cos(a))))
    return pts

def disc(name, pts, z, mat, coll=None):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    vs = [bm.verts.new((p.x, p.y, z)) for p in pts]
    bm.faces.new(vs)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); me.materials.append(mat)
    return link(ob, coll or ENV)

def pond(o, T, mats):
    kind, rim = o["kind"], o.get("rim", "rocks")
    natural = rim in ("rocks", "reeds", "snow")
    z = -0.22 if natural else -0.14
    pts = shape_points(o, jitter=0.12 if natural and o.get("shape", "ellipse") != "rect" else 0.0)
    disc("pond_basin", shape_points(o, m=0.6), -0.9, mats["gap"])
    if T["floor"] in ("flag", "slab", "marble", "snowflag", "water", "planks"):
        # paved apron just under the tile tops: fills the gap where whole tiles around the pool were skipped
        outer, inner = shape_points(o, n=64, m=1.6), shape_points(o, n=64, m=0.0)
        me = bpy.data.meshes.new("apron"); bm = bmesh.new()
        vo = [bm.verts.new((p_.x, p_.y, -0.03)) for p_ in outer]; vi = [bm.verts.new((p_.x, p_.y, -0.03)) for p_ in inner]
        for k in range(64):
            k2 = (k + 1) % 64
            bm.faces.new((vo[k], vo[k2], vi[k2], vi[k]))
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new("apron", me); me.materials.append(mats["cap"]); link(ob, ENV)
    disc("pond", pts, z, mat_pond(kind), coll=FX)
    if kind == "lava":
        cx, cy = o["c"]; torch_light((cx, cy, 1.2), color=(1.0, 0.35, 0.08), power=900 * max(o["r"]), radius=1.0, flame=False)
    if rim == "curb":
        rp = shape_points(o, n=max(16, int(sum(o["r"]) * 4)), m=0.22)
        for i in range(len(rp)):
            a, b = rp[i], rp[(i + 1) % len(rp)]
            mid = (a + b) / 2; d = b - a
            box("curb", (mid.x, mid.y, 0.1), (d.length + 0.05, 0.45, 0.32), (0, 0, math.atan2(d.y, d.x)), mats["cap"], bevel=0.04)
    else:
        rp = shape_points(o, n=max(12, int(sum(o["r"]) * 3.2)), jitter=0.1, m=0.25)
        rm = mats["rock"] if kind != "ice" else mats["cliff"]
        for i, p_ in enumerate(rp):
            if rng.random() < 0.85:
                s = R(0.35, 0.8)
                cliff("rim", (p_.x + R(-0.2, 0.2), p_.y + R(-0.2, 0.2), -0.3), (s * 1.2, s, s * 0.9), rm, sub=2, seed=int(p_.x * 13 + p_.y * 7) + i, step=0.25)
        if rim == "reeds" or (kind in ("water", "bog") and T.get("reeds", True)):
            reeds(shape_points(o, n=30, jitter=0.1, m=0.1), mats)
        if kind == "water" and o.get("lilies"):
            lily = mat_flat("Lily", (0.08, 0.16, 0.05), rough=0.5)
            for i in range(int(sum(o["r"]) * 4)):
                u, v = R(-0.8, 0.8), R(-0.8, 0.8)
                x, y = o["c"][0] + u * o["r"][0], o["c"][1] + v * o["r"][1]
                if pond_inside(o, x, y, -0.3):
                    cylinder("lily", (x, y, z + 0.015), R(0.18, 0.35), 0.01, lily, verts=10)
                    if rng.random() < 0.15:
                        rock("lotus", (x, y, z + 0.06), (0.08, 0.08, 0.05), mat_flat("Lotus", (0.9, 0.75, 0.8), rough=0.5), sub=1, rough=0.2, seed=i)

def reeds(pts, mats):
    mat = mat_flat("Reed", (0.2, 0.2, 0.09), rough=0.85)
    me = bpy.data.meshes.new("Reeds"); bm = bmesh.new()
    for p_ in pts:
        if rng.random() < 0.45: continue
        for k in range(12):
            a = R(0, 6.28); r = R(0.0, 0.35); hh = R(0.5, 1.2)
            base = Vector((p_.x + math.cos(a) * r, p_.y + math.sin(a) * r, -0.2))
            tip = base + Vector((R(-0.15, 0.15), R(-0.15, 0.15), hh))
            w = Vector((-math.sin(a), math.cos(a), 0)) * 0.02
            v1 = bm.verts.new(base - w); v2 = bm.verts.new(base + w); v3 = bm.verts.new(tip)
            bm.faces.new((v1, v2, v3))
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("Reeds", me); me.materials.append(mat); link(ob, ENV)

# ---------------------------------------------------------------- maze walls (natural styles)
def maze_wall(o, T, mats):
    a, b = Vector(o["a"]), Vector(o["b"]); style = o["style"]; h = o.get("h", 1.8)
    d = b - a; Ld = d.length; dn = d.normalized(); n = Vector((-dn.y, dn.x)); yaw = math.atan2(d.y, d.x)
    if style == "ruin":
        ashlar(a, b, h, mats["stone"], thick=0.85, top_mat=mats["cap"] if not T.get("snow") else mats["cap"], broken=0.3,
               course=T.get("course", (0.38, 0.55)))
        for e in (a, b): rubble(e.x, e.y, 6, mats["stone"], spread=0.7, seed=int(e.x * 9 + e.y))
        if T.get("snow"):
            t = 0.0
            while t < Ld:
                p_ = a + dn * t + n * R(-0.7, 0.7) * 1.4
                rock("snowpile", (p_.x, p_.y, 0.0), (R(0.6, 1.1), R(0.5, 0.9), R(0.25, 0.45)), mats["snowpile"], sub=2, rough=0.2, seed=int(t * 10))
                t += R(1.2, 2.2)
    elif style == "hedge":
        hedge_wall(a, b, h, [], mats)
    elif style == "palisade":
        palisade(a, b, h, [], mats)
    elif style == "fence":
        t = 0.0
        while t <= Ld:
            p_ = a + dn * t
            box("fpost", (p_.x, p_.y, 0.6), (0.14, 0.14, 1.2), (R(-0.05, 0.05), R(-0.05, 0.05), yaw), mats["wood"], bevel=0.01)
            t += 1.6
        for zz in (0.45, 0.95):
            m_ = (a + b) / 2
            box("frail", (m_.x, m_.y, zz), (Ld, 0.08, 0.12), (0, 0, yaw + R(-0.01, 0.01)), mats["wood"], bevel=0.01)
        for k in range(int(Ld / 3)):
            p_ = a + dn * R(0.5, Ld - 0.5) + n * R(-1.0, 1.0)
            cylinder("hay", (p_.x, p_.y, 0.45), 0.55, 0.9, mats["hay"], verts=14, rot=(math.radians(90), 0, R(0, 3.14)))
    elif style == "rock":
        t = 0.0
        while t < Ld:
            p_ = a + dn * t + n * R(-0.3, 0.3)
            cliff("ridge", (p_.x, p_.y, 0.0), (R(1.0, 1.5), R(1.1, 1.5), h * R(0.7, 1.15)), mats["cliff"], seed=int(t * 17 + a.x * 5), step=R(0.3, 0.45), rough=0.42)
            t += R(1.2, 1.7)
    elif style == "stones":
        t = 0.0
        while t <= Ld:
            p_ = a + dn * t
            hh = h * R(0.75, 1.25)
            box("menhir", (p_.x, p_.y, hh / 2 - 0.1), (R(0.6, 0.9), R(0.45, 0.6), hh), (R(-0.08, 0.08), R(-0.08, 0.08), yaw + R(-0.3, 0.3)), mats["stone"], bevel=0.12)
            t += R(1.4, 1.9)
    elif style == "crates":
        t = 0.0
        while t < Ld:
            p_ = a + dn * t
            if rng.random() < 0.3:
                cylinder("barrel", (p_.x, p_.y, 0.45), 0.38, 0.9, mats["wood"], verts=12, bevel=0.02)
                cylinder("barrel_hoop", (p_.x, p_.y, 0.72), 0.39, 0.06, mats["iron"], verts=12)
            else:
                for k in range(int(R(1, 3))):
                    box("crate", (p_.x + R(-0.1, 0.1), p_.y + R(-0.1, 0.1), 0.38 + k * 0.76), (0.76, 0.76, 0.76), (0, 0, yaw + R(-0.2, 0.2)), mats["crate"], bevel=0.03)
            t += 0.85
    elif style == "hull":
        m_ = (a + b) / 2
        hull((m_.x, m_.y, -0.2), Ld, R(2.4, 3.2), 1.6, mats["planks"], mats["wood"], tilt=R(-30, 30), yaw=math.degrees(yaw) - 90, seed=int(Ld * 10), broken=0.2)
    elif style == "stalls":
        t = 1.2
        while t < Ld - 1.0:
            p_ = a + dn * t
            box("stall_table", (p_.x, p_.y, 0.45), (2.0, 1.0, 0.9), (0, 0, yaw), mats["wood"], bevel=0.02)
            for s_ in (-1, 1):
                q = p_ + dn * s_ * 0.9
                box("stall_post", (q.x, q.y, 1.2), (0.1, 0.1, 2.4), (0, 0, yaw), mats["wood"], bevel=0)
            ob = box("awning", (p_.x + n.x * 0.2, p_.y + n.y * 0.2, 2.3), (2.3, 1.6, 0.04), (0, 0, yaw), rng.choice(mats["cloths"]), bevel=0)
            ob.rotation_euler = Euler((math.radians(14), 0, 0)); ob.rotation_euler.rotate(Euler((0, 0, yaw)))
            for k in range(int(R(2, 5))):
                q = p_ + dn * R(-0.8, 0.8) + n * R(-0.3, 0.3)
                box("goods", (q.x, q.y, 1.0), (R(0.2, 0.4), R(0.2, 0.4), R(0.15, 0.3)), (0, 0, R(0, 1)), rng.choice([mats["crate"], mats["hay"]]), bevel=0.01)
            t += 2.6
    elif style == "basalt":
        basalt_wall(a, b, h, [], mats, n)
    elif style == "mound":
        m_ = (a + b) / 2
        cliff("barrow", (m_.x, m_.y, -0.4), (Ld / 2, 2.4, h), mats["mound"], seed=int(Ld * 7), sub=3, step=5.0, rough=0.12, yaw=yaw)
        box("barrow_door", (m_.x + n.x * 2.0, m_.y + n.y * 2.0, 0.6), (1.4, 0.4, 1.2), (0, 0, yaw), mats["stone"], bevel=0.05)
    elif style == "pews":
        t = 0.0
        while t < Ld:
            p_ = a + dn * t
            if rng.random() > 0.15:
                ob = box("pew_seat", (p_.x, p_.y, 0.45), (0.5, 2.6, 0.08), (0, 0, yaw + R(-0.08, 0.08)), mats["wood"], bevel=0.01)
                box("pew_back", (p_.x - dn.x * 0.25, p_.y - dn.y * 0.25, 0.75), (0.08, 2.6, 0.7), (0, 0, yaw + R(-0.08, 0.08)), mats["wood"], bevel=0.01)
                for s_ in (-1, 1):
                    q = p_ + n * s_ * 1.25
                    box("pew_end", (q.x, q.y, 0.45), (0.55, 0.08, 0.9), (0, 0, yaw), mats["wood"], bevel=0.01)
            else:
                box("pew_fallen", (p_.x, p_.y, 0.15), (0.5, 2.6, 0.3), (R(-0.5, 0.5), 0, yaw + R(-0.5, 0.5)), mats["wood"], bevel=0.01)
            t += 1.15

def build_obstacles(T, mats):
    for o in OBST:
        if o["t"] == "pond": pond(o, T, mats)
        elif o["t"] == "wall": maze_wall(o, T, mats)

def obstacle_near(x, y, m):
    for o in OBST:
        if o["t"] == "pond" and pond_inside(o, x, y, m): return True
        if o["t"] == "wall":
            a, b = Vector(o["a"]), Vector(o["b"]); p_ = Vector((x, y)); d = b - a
            t = max(0.0, min(1.0, (p_ - a).dot(d) / max(d.length_squared, 1e-6)))
            if (a + d * t - p_).length < m + 0.8: return True
    return False

# ---------------------------------------------------------------- extra set dressing
def mat_flame():
    """Flame: emission rising from the base, alpha fading to the tip (object Z), flickering noise."""
    m = bpy.data.materials.new("Flame"); nt, out = nodes_of(m)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); lin(nt, tc.outputs["Generated"], sep.inputs[0])
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 4; nz.inputs["Detail"].default_value = 4
    lin(nt, tc.outputs["Object"], nz.inputs["Vector"])
    a = nt.nodes.new("ShaderNodeMath"); a.operation = "MULTIPLY_ADD"; a.inputs[1].default_value = 0.6
    lin(nt, nz.outputs["Fac"], a.inputs[0]); lin(nt, sep.outputs["Z"], a.inputs[2])
    al = nt.nodes.new("ShaderNodeMapRange"); al.inputs["From Min"].default_value = 1.25; al.inputs["From Max"].default_value = 0.55
    lin(nt, a.outputs[0], al.inputs["Value"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (1.0, 0.85, 0.45, 1); ramp.color_ramp.elements[1].color = (1.0, 0.22, 0.03, 1)
    lin(nt, sep.outputs["Z"], ramp.inputs["Fac"])
    em = nt.nodes.new("ShaderNodeEmission"); em.inputs["Strength"].default_value = 7
    lin(nt, ramp.outputs["Color"], em.inputs["Color"])
    tr = nt.nodes.new("ShaderNodeBsdfTransparent"); mx = nt.nodes.new("ShaderNodeMixShader")
    lin(nt, al.outputs[0], mx.inputs[0]); lin(nt, tr.outputs[0], mx.inputs[1]); lin(nt, em.outputs[0], mx.inputs[2])
    lin(nt, mx.outputs[0], out.inputs[0])
    return m

_FLAME = None; _SMOKE = None
def fire(x, y, z=0.0, s=1.0, smoke=False):
    global _FLAME, _SMOKE
    if _FLAME is None:
        _FLAME = mat_flame()
        _SMOKE = mat_flat("Smoke", (0.05, 0.05, 0.055), rough=1.0, alpha=0.32)
    for k in range(int(5 * s) + 3):
        h = R(0.6, 1.6) * s
        cylinder("flame", (x + R(-0.4, 0.4) * s, y + R(-0.4, 0.4) * s, z + h / 2), R(0.12, 0.3) * s, h, _FLAME, verts=8, r2=0.01,
                 rot=(R(-0.15, 0.15), R(-0.15, 0.15), 0), coll=FX)
    emb = mat_flat("Embers2", (0.1, 0.03, 0.01), emit=(1.0, 0.35, 0.06), emit_str=20)
    rock("embers", (x, y, z + 0.04), (0.7 * s, 0.6 * s, 0.12), emb, sub=2, rough=0.5, seed=int(x * 3 + y))
    for k in range(int(10 * s)):
        rock("spark", (x + R(-1.2, 1.2) * s, y + R(-1.2, 1.2) * s, z + R(1.0, 4.0) * s), (0.03, 0.03, 0.03), mat_flat("Spark", (1, 0.5, 0.1), emit=(1.0, 0.5, 0.1), emit_str=60), sub=1, rough=0.0, coll=FX, seed=k)
    if smoke:
        for k in range(5):
            r = R(0.6, 1.2) * s * (1 + k * 0.35)
            rock("smoke", (x + k * 0.5 * s + R(-0.3, 0.3), y - k * 0.35 * s, z + 2.0 * s + k * 1.3 * s), (r, r, r * 0.8), _SMOKE, sub=2, rough=0.25, coll=FX, seed=k)
    torch_light((x, y, z + 1.0 * s), power=650 * s, radius=0.6, flame=False)

def snowfall(n=1600):
    mat = mat_flat("Flake", (0.95, 0.97, 1.0), rough=0.9, emit=(0.9, 0.93, 1.0), emit_str=0.6)
    me = bpy.data.meshes.new("Snowfall"); bm = bmesh.new()
    for i in range(n):
        x, y, z = R(-RX - 6, RX + 6), R(-RY - 6, RY + 6), R(0.5, 14)
        res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=R(0.025, 0.05))
        bmesh.ops.translate(bm, verts=res["verts"], vec=(x, y, z))
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("Snowfall", me); me.materials.append(mat); link(ob, FX)

def fireflies(n=160, col=(0.6, 1.0, 0.35)):
    mat = mat_flat("Firefly", col, emit=col, emit_str=40)
    me = bpy.data.meshes.new("Fireflies"); bm = bmesh.new()
    for i in range(n):
        x, y = R(-RX, RX), R(-RY, RY)
        if not inside(x, y, 0.5): continue
        res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.04)
        bmesh.ops.translate(bm, verts=res["verts"], vec=(x, y, R(0.3, 2.0)))
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("Fireflies", me); me.materials.append(mat); link(ob, FX)
    for i in range(6):
        x, y = R(-RX + 3, RX - 3), R(-RY + 3, RY - 3)
        torch_light((x, y, 1.0), color=col, power=40, radius=0.2, flame=False)

def free_spots(n, margin=2.0, edge=1.5, tries=400):
    out = []
    for _ in range(tries):
        if len(out) >= n: break
        x, y = R(-RX + edge, RX - edge), R(-RY + edge, RY - edge)
        if not inside(x, y, edge) or obstacle_near(x, y, margin): continue
        if (Vector((x, y)) - G_SW).length < 6 or (Vector((x, y)) - G_NE).length < 7: continue
        if any((Vector((x, y)) - Vector(p)).length < margin * 1.5 for p in out): continue
        out.append((x, y))
    return out

def scatter_props(T, mats, kinds, n=14):
    for (x, y) in free_spots(n, margin=1.2, edge=1.2):
        k = rng.choice(kinds)
        if k == "barrels":
            for j in range(int(R(1, 4))):
                cylinder("barrel", (x + R(-0.6, 0.6), y + R(-0.6, 0.6), 0.45), 0.36, 0.9, mats["wood"], verts=12, bevel=0.02)
        elif k == "crates":
            for j in range(int(R(1, 4))):
                box("crate", (x + R(-0.6, 0.6), y + R(-0.6, 0.6), 0.36 + (0.72 if j == 3 else 0)), (0.72, 0.72, 0.72), (0, 0, R(0, 1)), mats["crate"], bevel=0.03)
        elif k == "swords":
            for j in range(int(R(1, 4))):
                box("sword", (x + R(-0.8, 0.8), y + R(-0.8, 0.8), 0.5), (0.05, 0.1, 1.1), (R(-0.3, 0.3), R(-0.3, 0.3), R(0, 3)), mats["iron"], bevel=0)
            rock("helm", (x, y, 0.12), (0.18, 0.2, 0.16), mats["iron"], sub=2, rough=0.1, seed=int(x))
        elif k == "bones":
            for j in range(10):
                cylinder("bone", (x + R(-0.7, 0.7), y + R(-0.7, 0.7), 0.05), 0.035, R(0.3, 0.55), mats["bone"], verts=5, rot=(math.radians(90), 0, R(0, 3.14)))
            rock("skull", (x, y, 0.12), (0.13, 0.12, 0.14), mats["bone"], sub=1, rough=0.1, seed=int(y))
        elif k == "cart":
            yaw = R(0, 3.14)
            box("cart_bed", (x, y, 0.75), (1.4, 2.6, 0.12), (R(-0.2, 0.2), 0, yaw), mats["wood"], bevel=0.01)
            for s_ in (-1, 1):
                q = Vector((x, y)) + Vector((math.cos(yaw), math.sin(yaw))) * s_ * 0.8
                cylinder("wheel", (q.x, q.y, 0.5), 0.5, 0.1, mats["wood"], verts=14, rot=(0, math.radians(90), yaw))
        elif k == "rope":
            for j in range(3):
                cylinder("coil", (x + R(-0.3, 0.3), y + R(-0.3, 0.3), 0.06 + j * 0.08), 0.35 - j * 0.05, 0.08, mats["rope"], verts=14)
        elif k == "nets":
            box("net", (x, y, 0.02), (R(1.5, 2.5), R(1.0, 2.0), 0.03), (0, 0, R(0, 3)), mats["net"], bevel=0)
        elif k == "rubble":
            rubble(x, y, 9, mats["stone"], spread=0.9, size=(0.15, 0.45), seed=int(x * 5 + y))
        elif k == "candles":
            wax = mats["wax"]
            for j in range(int(R(4, 9))):
                cx_, cy_ = x + R(-0.5, 0.5), y + R(-0.5, 0.5); h = R(0.1, 0.4)
                cylinder("candle", (cx_, cy_, h / 2), 0.035, h, wax, verts=6)
            torch_light((x, y, 0.6), power=40, radius=0.2, flame=False)
        elif k == "lanterns":
            cylinder("lantern_post", (x, y, 1.0), 0.05, 2.0, mats["iron"], verts=6)
            torch_light((x, y, 2.05), color=T.get("lamp", (1.0, 0.6, 0.3)), power=90, radius=0.05, flame=True)
        elif k == "pine":
            pine(x, y, R(3.5, 5.5), mats["pine"], mats["wood"], mats["snowpile"] if T.get("snow") else None)
        elif k == "tree":
            leafy_tree(x, y, R(4.5, 6.5), mats["trunk"], mats["leaf"], mats["leaf2"])
        elif k == "deadtree":
            dead_tree(x, y, R(3.5, 5.5), mats["trunk"])
        elif k == "coral":
            for j in range(int(R(3, 7))):
                cylinder("coral", (x + R(-0.6, 0.6), y + R(-0.6, 0.6), 0.3), R(0.08, 0.2), R(0.4, 1.0), rng.choice([mats["coral"], mats["coral2"]]), verts=6, r2=0.03,
                         rot=(R(-0.4, 0.4), R(-0.4, 0.4), 0))
        elif k == "anvil":
            box("anvil_base", (x, y, 0.35), (0.5, 0.4, 0.7), (0, 0, R(0, 1)), mats["dark"], bevel=0.03)
            box("anvil", (x, y, 0.8), (0.9, 0.35, 0.25), (0, 0, R(0, 1)), mats["iron"], bevel=0.03)
        elif k == "tent":
            cylinder("tent", (x, y, 1.0), 1.6, 2.0, rng.choice(mats["cloths"]), verts=8, r2=0.05)
        elif k == "racks":
            yaw = R(0, 3.14)
            for s_ in (-1, 1):
                q = Vector((x, y)) + Vector((math.cos(yaw), math.sin(yaw))) * s_ * 1.2
                box("rack_post", (q.x, q.y, 0.9), (0.1, 0.1, 1.8), (0, 0, yaw), mats["wood"], bevel=0)
            box("rack_bar", (x, y, 1.75), (2.6, 0.08, 0.08), (0, 0, yaw), mats["wood"], bevel=0)
            for j in range(6):
                q = Vector((x, y)) + Vector((math.cos(yaw), math.sin(yaw))) * R(-1.0, 1.0)
                box("fish", (q.x, q.y, 1.45), (0.06, 0.12, 0.5), (0, 0, yaw), mats["fish"], bevel=0)

def pews_rows(T, mats):
    mid = (G_SW + G_NE) / 2; d = (G_NE - G_SW).normalized(); n = Vector((-d.y, d.x))
    # two blocks of pews either side of the carpet aisle, in the southern half of the nave
    for s_ in (-1, 1):
        a = mid - d * 17.0 + n * s_ * 3.0; b = mid - d * 5.0 + n * s_ * 3.0
        maze_wall({"a": a[:], "b": b[:], "style": "pews", "h": 1.0}, T, mats)

def chains_banners(T, mats):
    banners(mats, T.get("banner", (0.4, 0.06, 0.05)))

# ---------------------------------------------------------------- camera for the in-game shot
def game_camera(look, view_width=15.0, pitch=55.0, fov_v=40.0):
    cd = bpy.data.cameras.new("GameCam"); cd.sensor_fit = "VERTICAL"; cd.angle_y = math.radians(fov_v); cd.clip_end = 600
    co = bpy.data.objects.new("GameCam", cd); scene.collection.objects.link(co)
    aspect = RES[0] / RES[1]
    half_h = math.atan(math.tan(math.radians(fov_v) / 2) * aspect)
    dist = view_width * 0.5 / math.tan(half_h)
    pr = math.radians(pitch)
    co.location = (look[0], look[1] - dist * math.cos(pr), dist * math.sin(pr))
    co.rotation_euler = (math.radians(90 - pitch), 0, 0)
    return co

def chars3():
    if KNIGHT:
        _place(_import(KNIGHT), -11.0, -21.5, 225, action="AimIdle")
    if ZOMBIE:
        spots = [(-4, -8), (4, -12), (-8, 4), (6, 2), (0, 10), (10, 10), (-6, 16), (3, 20), (12, -6), (-13, -4), (8, 22), (-2, -16)]
        acts = None
        placed = 0
        for (x, y) in spots:
            if obstacle_near(x, y, 0.8) or placed >= 10: continue
            new = _import(ZOMBIE)
            acts = [a.name for a in bpy.data.actions if "running" in a.name or "clip0" in a.name]
            _place(new, x, y, math.degrees(math.atan2(-21.5 - y, -11.0 - x)) - 90, action=acts[-1] if acts else None)
            placed += 1
    scene.frame_set(12)

# ---------------------------------------------------------------- layouts (Archero-like, natural) -- SW start (-11,-22), NE gate (14,26)
def P(kind, rim, c, r, rot=0.0, shape="ellipse", **kw):
    d = {"t": "pond", "kind": kind, "rim": rim, "c": c, "r": r, "rot": rot, "shape": shape}; d.update(kw); return d
def W(style, a, b, h=1.8):
    return {"t": "wall", "style": style, "a": a, "b": b, "h": h}

# ---------------------------------------------------------------- themes
EXTRA_MATS = {}
def more_mats(T, mats):
    mats["snowpile"] = mat_flat("SnowPile", (0.82, 0.86, 0.92), rough=0.8)
    mats["hay"] = mat_stone("Hay", (0.35, 0.28, 0.12), (0.6, 0.5, 0.25), rough=0.9, scale=8)
    mats["crate"] = mat_stone("CrateWood", (0.16, 0.11, 0.06), (0.32, 0.23, 0.13), rough=0.8, scale=5)
    mats["rope"] = mat_flat("Rope", (0.4, 0.32, 0.2), rough=0.9)
    mats["net"] = mat_flat("Net", (0.25, 0.22, 0.15), rough=0.9)
    mats["wax"] = mat_flat("Wax", (0.85, 0.8, 0.65), rough=0.6)
    mats["pine"] = mat_flat("PineN", (0.04, 0.09, 0.07), rough=0.9)
    mats["trunk"] = mat_flat("Trunk", (0.1, 0.075, 0.05), rough=0.9)
    mats["leaf"] = mat_stone("Leaf", (0.06, 0.1, 0.03), (0.18, 0.24, 0.07), rough=0.85, scale=4)
    mats["leaf2"] = mat_stone("Leaf2", (0.12, 0.1, 0.03), (0.35, 0.25, 0.06), rough=0.85, scale=4)
    mats["coral"] = mat_flat("Coral", (0.7, 0.3, 0.28), rough=0.7)
    mats["coral2"] = mat_flat("Coral2", (0.85, 0.6, 0.35), rough=0.7)
    mats["fish"] = mat_flat("Fish", (0.45, 0.45, 0.42), rough=0.4, metal=0.3)
    mats["mound"] = mat_stone("Mound", (0.08, 0.1, 0.04), (0.2, 0.22, 0.1), rough=0.95, scale=2)
    mats["cloths"] = [mat_torn_cloth("Cloth" + str(i), tuple(v * 0.5 for v in c), c) for i, c in enumerate(((0.45, 0.08, 0.06), (0.15, 0.22, 0.35), (0.5, 0.38, 0.15), (0.2, 0.3, 0.15)))]

_orig_build = build
def build(T):
    # wrap: create the extra materials inside the same mats dict the v2 build makes
    global EXTRA_BUILD
    def eb(T_, mats):
        more_mats(T_, mats)
        build_obstacles(T_, mats)
        for f in T_.get("details", []):
            f(T_, mats)
    EXTRA_BUILD = eb
    _orig_build(T)

def d_scatter(kinds, n=14):
    return lambda T, mats: scatter_props(T, mats, kinds, n)

def d_fires(n=6, s=1.0):
    def f(T, mats):
        for (x, y) in free_spots(n, margin=2.5, edge=2.5):
            fire(x, y, 0.0, s * R(0.8, 1.3))
    return f

def d_roof_fires(T, mats):
    for k in range(4):
        side = rng.choice((-1, 1)); y = R(-RY + 6, RY - 6)
        fire(side * (RX + 2.0), y, 4.4, R(1.0, 1.5))

def d_well(T, mats):
    cylinder("well", (3, 2, 0.45), 1.2, 0.9, mats["stone"], verts=16, bevel=0.03)
    for s_ in (-1, 1): box("well_post", (3 + s_ * 1.0, 2, 1.4), (0.15, 0.15, 1.9), (0, 0, 0), mats["wood"])
    box("well_roof", (3, 2, 2.5), (2.6, 1.6, 0.12), (0, 0, 0), mats["roof"])
def d_snow(T, mats): snowfall(1800)
def d_fireflies(T, mats): fireflies(180, T.get("firefly", (0.6, 1.0, 0.35)))
def d_pews(T, mats): pews_rows(T, mats)
def d_banners(T, mats): chains_banners(T, mats)
def d_braziers(T, mats): pos_braziers(mats)
def d_altar(T, mats): altar_north(mats)
def d_lighthouse(T, mats): lighthouse(-RX - 9, RY + 12, mats, (1.0, 0.85, 0.6))
def d_floes(T, mats): ice_floes(mat_cliff("Ice", (0.55, 0.65, 0.72), (0.82, 0.88, 0.92)), 60)
def d_wreck(T, mats): wreck_extras(mats)
def d_boats(T, mats): boats(mats, 5)
def d_village(T, mats): village_extras(mats)
def d_graves(n):
    return lambda T, mats: graves(mats, n)
def d_statues(pts):
    def f(T, mats):
        for (x, y) in pts:
            if not obstacle_near(x, y, 0.5): statue(x, y, mats, scale=1.2, seed=int(x * y), yaw=math.atan2(-y, -x))
    return f
def d_bonfire(x, y):
    return lambda T, mats: bonfire(x, y, mats)
def d_fountain(x, y):
    return lambda T, mats: fountain(x, y, mats)
def d_lanterns_string(T, mats):
    lamp = mat_flat("Paper", (1.0, 0.7, 0.4), emit=(1.0, 0.6, 0.3), emit_str=12)
    for k in range(5):
        y = -18 + k * 9
        for i in range(12):
            x = -RX + 1.5 + i * (2 * RX - 3) / 11
            z = 3.6 - 0.6 * math.sin(math.pi * i / 11)
            rock("paper_lamp", (x, y, z), (0.16, 0.16, 0.2), lamp, sub=1, rough=0.1, coll=FX, seed=i + k * 20)
        torch_light((0, y, 3.2), color=(1.0, 0.62, 0.32), power=260, radius=1.5, flame=False)
def d_trees(n, kind="tree"):
    return d_scatter([kind], n)

COMMON = dict(debris=60)
B = THEMES  # v2 theme dicts

THEMES3 = {
    # ---- the four picks, upgraded --------------------------------------------------------------------------------------
    "01_drowned_cathedral": dict(B["01_drowned_cathedral"], **COMMON, cover_n=5,
        obst=[P("water", "curb", (-5.4, 1.4), (1.7, 4.6), rot=-28.5, shape="rect"), P("water", "curb", (5.4, -1.4), (1.7, 4.6), rot=-28.5, shape="rect"),
              W("ruin", (-14, 14), (-7, 14), 1.6), W("ruin", (7, -14), (14, -14), 1.6)],
        details=[d_pews, d_banners, d_altar, d_braziers, d_scatter(["candles", "rubble", "swords", "candles"], 14)],
        game_look=(2, 6)),
    "02_frost_bastion": dict(B["02_frost_bastion"], **COMMON, cover_n=5,
        obst=[P("ice", "snow", (-7, 9), (4.2, 3.2), rot=20), P("ice", "snow", (8, -10), (3.4, 2.6), rot=-15),
              W("ruin", (3, 3), (11, 3), 1.6), W("ruin", (3, 3), (3, 8), 1.6), W("ruin", (-11, -6), (-3, -6), 1.6), W("ruin", (-3, -6), (-3, -11), 1.6)],
        details=[d_snow, d_banners, d_braziers, d_floes, d_scatter(["pine", "crates", "barrels", "swords", "pine"], 16)],
        game_look=(0, 2)),
    "03_wreck_cove": dict(B["03_wreck_cove"], **COMMON, cover_n=3,
        obst=[P("water", "rocks", (8, 14), (3.2, 2.4), rot=30), P("water", "rocks", (-8, -12), (2.8, 2.2), rot=-20), P("water", "rocks", (9, -14), (2.2, 1.8)),
              W("rock", (-14, 0), (-6, 3), 1.8), W("rock", (4, -2), (12, 2), 1.6)],
        details=[d_wreck, d_scatter(["barrels", "rope", "nets", "crates", "rope", "racks"], 18)],
        game_look=(-2, 6)),
    "05_burning_hamlet": dict(B["05_burning_hamlet"], **COMMON, cover_n=4, exposure=0.1,
        obst=[P("water", "reeds", (7, 13), (3.6, 2.6), rot=10), W("fence", (-13, 6), (-4, 6), 1.2), W("fence", (-4, 6), (-4, 12), 1.2),
              W("fence", (3, -6), (12, -6), 1.2), W("crates", (-8, -12), (-2, -12))],
        details=[d_fires(4, 1.1), d_roof_fires, d_well, d_scatter(["cart", "barrels", "crates", "cart", "swords"], 14)],
        game_look=(0, 4)),
    # ---- fifteen new chambers (16 - 30) ------------------------------------------------------------------------------
    "16_sunken_bathhouse": dict(DUSK, **COMMON, title="Sunken Bathhouse", walls="arcade", wall_h=6.0, north_h=6.0, south_h=0.8, south_style="ruin",
        floor="marble", floorc=((0.42, 0.38, 0.33), (0.62, 0.57, 0.5)), gloss=0.25, wet=0.5, stone=((0.42, 0.38, 0.32), (0.66, 0.6, 0.52)),
        capc=((0.3, 0.27, 0.23), (0.5, 0.45, 0.38)), moss=(0.12, 0.16, 0.08), gate="marble", bridge="stone", cover="pillars", cover_n=3,
        obst=[P("water", "curb", (-7, -5), (3.2, 7.0), shape="rect"), P("water", "curb", (7, 9), (3.2, 7.0), shape="rect"), P("water", "curb", (0, 22), (2.6, 2.6))],
        details=[d_statues([(-7, 4.5), (7, -0.5), (-3, 22), (3, 22)]), d_braziers, d_scatter(["rubble", "candles", "rubble"], 10)],
        sea=((0.02, 0.05, 0.09), (0.1, 0.19, 0.23)), game_look=(0, 4)),
    "17_fishing_village": dict(**COMMON, title="Fishing Village Ruins", walls="timber", wall_h=3.4, north_h=3.4, south_h=0.9, south_style="palisade",
        floor="dirt", floorc=((0.13, 0.11, 0.09), (0.28, 0.24, 0.19)), wet=0.5, stone=((0.16, 0.15, 0.13), (0.34, 0.32, 0.28)),
        gate="timber", bridge="wood", cover="crates", cover_n=3,
        obst=[P("water", "reeds", (-7, 6), (3.0, 2.2), rot=15), P("water", "rocks", (6, -10), (2.6, 2.0)),
              W("fence", (2, 2), (12, 2), 1.2), W("fence", (-12, -6), (-3, -6), 1.2), W("crates", (-4, 16), (4, 16))],
        details=[d_boats, d_scatter(["racks", "nets", "barrels", "rope", "racks", "lanterns"], 18)],
        sun=(16, 155), sun_col=(1.0, 0.82, 0.62), sun_str=1.8, sky=((0.35, 0.4, 0.45), (0.75, 0.68, 0.6)), sky_str=0.55,
        haze_col=(0.62, 0.62, 0.62), haze=0.6, gate_light=(1.0, 0.8, 0.55), sea=((0.03, 0.07, 0.08), (0.1, 0.22, 0.22)), game_look=(0, 4)),
    "18_frozen_lake_fort": dict(**COMMON, title="Frozen Lake Fort", walls="palisade", wall_h=3.0, north_h=2.4, south_h=1.0, snow=1.0,
        floor="dirt", floorc=((0.2, 0.19, 0.18), (0.36, 0.34, 0.32)), stone=((0.16, 0.16, 0.17), (0.36, 0.35, 0.34)),
        gate="timber", bridge="wood", cover="crates", cover_n=2,
        obst=[P("ice", "snow", (-1, 4), (6.5, 4.2), rot=25), P("ice", "snow", (-10, -14), (3.0, 2.2)), W("palisade", (6, -6), (13, -6), 2.0),
              W("palisade", (-14, 16), (-6, 16), 2.0)],
        details=[d_snow, d_scatter(["pine", "tent", "pine", "crates", "lanterns", "pine"], 18)],
        sun=(10, 155), sun_col=(1.0, 0.55, 0.35), sun_str=1.8, sky=((0.12, 0.16, 0.3), (0.65, 0.48, 0.48)), sky_str=0.5,
        haze_col=(0.42, 0.45, 0.58), haze=0.45, gate_light=(1.0, 0.7, 0.42), sea=((0.02, 0.05, 0.08), (0.06, 0.16, 0.2)), game_look=(0, 4)),
    "19_cursed_swamp": dict(**COMMON, title="Cursed Swamp Isle", walls="cliff", wall_h=3.0, north_h=1.8, south_h=1.0, found_style="cliff",
        floor="grass", floorc=((0.03, 0.04, 0.02), (0.1, 0.11, 0.05)), wet=0.6, stone=((0.12, 0.13, 0.1), (0.26, 0.27, 0.22)), moss=(0.08, 0.12, 0.05),
        cliffc=((0.04, 0.05, 0.04), (0.16, 0.18, 0.13)), gate="round", bridge="wood", cover="rocks", cover_n=2, grass=(0.08, 0.1, 0.04), grass_n=500,
        obst=[P("bog", "reeds", (-8, -4), (3.6, 2.8), rot=30), P("bog", "reeds", (7, 6), (4.2, 3.0), rot=-20), P("bog", "reeds", (-5, 16), (3.0, 2.4)),
              P("bog", "reeds", (9, -16), (2.6, 2.2)), W("rock", (-2, -12), (5, -10), 1.6)],
        details=[d_fireflies, d_scatter(["deadtree", "deadtree", "bones", "deadtree", "lanterns"], 16)],
        sun=(30, 155), sun_col=(0.55, 0.75, 0.9), sun_str=1.2, sky=((0.02, 0.04, 0.05), (0.14, 0.2, 0.2)), sky_str=0.4,
        haze_col=(0.12, 0.2, 0.18), haze=0.6, exposure=1.4, gate_light=(1.0, 0.75, 0.45), sea=((0.01, 0.03, 0.035), (0.04, 0.09, 0.08)), game_look=(0, 4)),
    "20_crumbling_keep": dict(DUSK, **COMMON, title="Crumbling Keep Maze", sea=((0.02, 0.05, 0.09), (0.1, 0.19, 0.23)), walls="crenel", wall_h=4.0, north_h=4.0, south_h=0.9, buttress=True,
        floor="flag", floorc=((0.22, 0.2, 0.18), (0.42, 0.38, 0.34)), stone=((0.18, 0.17, 0.16), (0.4, 0.37, 0.34)), moss=(0.12, 0.15, 0.07),
        gate="portcullis", bridge="stone", cover="columns", cover_n=0,
        obst=[W("ruin", (-12, -14), (-4, -14), 2.2), W("ruin", (-4, -14), (-4, -8), 2.2), W("ruin", (4, -4), (12, -4), 2.2), W("ruin", (4, -4), (4, 2), 2.2),
              W("ruin", (-12, 6), (-4, 6), 2.2), W("ruin", (-4, 6), (-4, 12), 2.2), W("ruin", (3, 16), (11, 16), 2.2), P("water", "curb", (0, 0), (1.4, 1.4))],
        details=[d_banners, d_braziers, d_scatter(["rubble", "swords", "barrels", "rubble", "crates"], 16)],
        game_look=(0, 0)),
    "21_cliffside_hermitage": dict(**COMMON, title="Cliffside Hermitage", walls="cliff", wall_h=4.2, north_h=2.0, south_h=1.2, found_style="cliff",
        floor="gravel", floorc=((0.2, 0.19, 0.18), (0.38, 0.36, 0.33)), wet=0.4, stone=((0.24, 0.23, 0.21), (0.46, 0.44, 0.4)), moss=(0.1, 0.13, 0.06),
        gate="cave", bridge="wood", cover="rocks", cover_n=2,
        obst=[P("water", "rocks", (-8, 10), (3.0, 2.4)), P("water", "rocks", (6, -6), (2.6, 2.0)), P("water", "rocks", (8, 18), (2.4, 1.8)),
              W("rock", (-14, -4), (-5, -2), 2.0), W("rock", (2, 6), (11, 8), 1.8), W("rock", (-6, 22), (1, 21), 1.6)],
        details=[d_scatter(["lanterns", "candles", "rope", "tree", "lanterns"], 12), d_bonfire(-2, -12)],
        sun=(22, 155), sun_col=(1.0, 0.86, 0.7), sun_str=1.6, sky=((0.36, 0.4, 0.46), (0.7, 0.68, 0.66)), sky_str=0.6,
        haze_col=(0.58, 0.6, 0.62), haze=0.5, gate_light=(1.0, 0.8, 0.55), sea=((0.03, 0.07, 0.09), (0.1, 0.22, 0.24)), game_look=(0, 4)),
    "22_mossy_cloister_garden": dict(**COMMON, title="Mossy Cloister Garden", walls="arcade", wall_h=5.0, north_h=5.0, south_h=0.8, south_style="ruin",
        floor="grass", floorc=((0.05, 0.08, 0.03), (0.17, 0.21, 0.08)), stone=((0.3, 0.28, 0.24), (0.55, 0.52, 0.45)), moss=(0.12, 0.17, 0.06),
        gate="round", bridge="stone", cover="hedges", cover_n=0, path=((0.22, 0.2, 0.17), (0.4, 0.37, 0.32)), grass=(0.14, 0.18, 0.06), grass_n=700, grass_walls=False,
        obst=[P("water", "curb", (0.5, 1.0), (4.0, 4.0), lilies=True), W("hedge", (-12, -12), (-5, -12), 1.6), W("hedge", (-12, -12), (-12, -6), 1.6),
              W("hedge", (5, 14), (12, 14), 1.6), W("hedge", (12, 14), (12, 8), 1.6), W("hedge", (5, -14), (11, -14), 1.6), W("hedge", (-11, 15), (-5, 15), 1.6)],
        details=[d_scatter(["tree", "tree", "lanterns", "tree"], 10), d_statues([(-6, 0), (7, 2)])],
        sun=(14, 155), sun_col=(1.0, 0.72, 0.42), sun_str=2.3, sky=((0.2, 0.28, 0.4), (0.9, 0.62, 0.38)), sky_str=0.55,
        haze_col=(0.6, 0.48, 0.34), haze=0.4, gate_light=(1.0, 0.78, 0.5), sea=((0.02, 0.06, 0.09), (0.08, 0.22, 0.24)), game_look=(0, 2)),
    "23_bloodmoon_circle": dict(**COMMON, title="Bloodmoon Stone Circle", walls="ruin", wall_h=2.2, north_h=1.6, south_h=0.9,
        floor="dirt", floorc=((0.07, 0.05, 0.045), (0.18, 0.13, 0.11)), stone=((0.14, 0.12, 0.12), (0.3, 0.27, 0.26)),
        gate="basalt", bridge="stone", cover="tombs", cover_n=0, grass=(0.12, 0.08, 0.06), grass_n=300,
        obst=[P("blood", "rocks", (0.5, 2.0), (3.2, 3.2))] + [W("stones", (math.cos(a) * 9 + 0.5, math.sin(a) * 9 + 2), (math.cos(a + 0.5) * 9 + 0.5, math.sin(a + 0.5) * 9 + 2), 2.6)
              for a in [k * 2 * math.pi / 7 for k in range(7)]],
        details=[d_scatter(["candles", "bones", "candles", "deadtree", "bones"], 16)],
        sun=(30, 155), sun_col=(1.0, 0.25, 0.18), sun_str=1.4, sky=((0.06, 0.01, 0.015), (0.35, 0.06, 0.05)), sky_str=0.45,
        haze_col=(0.3, 0.05, 0.05), haze=0.55, exposure=0.8, gate_light=(1.0, 0.65, 0.4), sea=((0.02, 0.008, 0.01), (0.08, 0.03, 0.03)), game_look=(0, 2)),
    "24_salt_flat_shrine": dict(**COMMON, title="Salt Flat Shrine", walls="ruin", wall_h=2.0, north_h=1.4, south_h=0.8,
        floor="sand", floorc=((0.62, 0.6, 0.56), (0.82, 0.8, 0.76)), stone=((0.5, 0.47, 0.42), (0.75, 0.72, 0.66)), capc=((0.45, 0.42, 0.38), (0.7, 0.67, 0.6)),
        gate="round", bridge="stone", cover="pillars", cover_n=2,
        obst=[P("brine", "rocks", (-8, 8), (3.4, 2.4), rot=20), P("brine", "rocks", (7, -4), (3.0, 2.6)), P("brine", "rocks", (-4, -14), (2.6, 2.0), rot=-30),
              P("brine", "rocks", (8, 18), (2.4, 2.0)), W("stones", (-2, 18), (4, 22), 2.2)],
        details=[d_scatter(["bones", "bones", "rubble", "candles"], 12), d_statues([(0, 6)])],
        sun=(38, 155), sun_col=(1.0, 0.95, 0.85), sun_str=2.6, sky=((0.4, 0.55, 0.7), (0.85, 0.85, 0.82)), sky_str=0.7,
        haze_col=(0.85, 0.82, 0.78), haze=0.35, exposure=-0.9, gate_light=(1.0, 0.85, 0.6), gate_power=200000, sea=((0.03, 0.12, 0.16), (0.1, 0.38, 0.38)), game_look=(0, 4)),
    "25_shipbreakers_yard": dict(**COMMON, title="Shipbreaker's Yard", walls="palisade", wall_h=3.2, north_h=2.4, south_h=1.0,
        floor="dirt", floorc=((0.1, 0.085, 0.07), (0.24, 0.2, 0.15)), wet=0.4, stone=((0.15, 0.14, 0.12), (0.32, 0.3, 0.26)),
        gate="timber", bridge="wood", cover="crates", cover_n=2,
        obst=[W("hull", (-12, -10), (-3, -6)), W("hull", (3, 2), (12, 6)), W("hull", (-10, 14), (-2, 18)), P("tar", "rocks", (7, -12), (2.8, 2.0)), P("tar", "rocks", (4, 18), (2.2, 1.8))],
        details=[d_fires(3, 0.9), d_scatter(["barrels", "rope", "crates", "barrels", "lanterns"], 18)],
        sun=(8, 155), sun_col=(1.0, 0.55, 0.3), sun_str=2.0, sky=((0.12, 0.12, 0.2), (0.75, 0.42, 0.28)), sky_str=0.5,
        haze_col=(0.5, 0.36, 0.3), haze=0.45, gate_light=(1.0, 0.7, 0.42), sea=((0.02, 0.05, 0.08), (0.08, 0.17, 0.2)), game_look=(0, 4)),
    "26_coral_ruins": dict(**COMMON, title="Coral-Crusted Ruins", walls="ruin", wall_h=2.6, north_h=1.8, south_h=0.9,
        floor="sand", floorc=((0.45, 0.4, 0.33), (0.65, 0.58, 0.48)), wet=0.6, stone=((0.4, 0.33, 0.3), (0.62, 0.52, 0.46)), moss=(0.15, 0.25, 0.2),
        gate="round", bridge="stone", cover="columns", cover_n=3,
        obst=[P("water", "rocks", (-7, -6), (3.4, 2.6), rot=10), P("water", "rocks", (8, 8), (3.0, 2.4), rot=-25), P("water", "rocks", (-6, 18), (2.4, 2.0)),
              W("rock", (2, -14), (10, -11), 1.6), W("rock", (-14, 6), (-6, 8), 1.6)],
        details=[d_scatter(["coral", "coral", "rope", "coral", "rubble"], 20)],
        sun=(32, 155), sun_col=(1.0, 0.92, 0.8), sun_str=2.4, sky=((0.25, 0.45, 0.65), (0.75, 0.8, 0.82)), sky_str=0.7,
        haze_col=(0.6, 0.7, 0.75), haze=0.35, exposure=-0.5, gate_light=(1.0, 0.86, 0.62), gate_power=180000, sea=((0.02, 0.12, 0.16), (0.05, 0.38, 0.38)), game_look=(0, 4)),
    "27_lantern_market": dict(DUSK, **COMMON, title="Lantern Market", sea=((0.02, 0.05, 0.09), (0.1, 0.19, 0.23)), walls="timber", wall_h=3.6, north_h=3.6, south_h=0.9, south_style="ashlar",
        floor="flag", floorc=((0.2, 0.18, 0.16), (0.38, 0.34, 0.3)), wet=0.4, stone=((0.18, 0.16, 0.14), (0.36, 0.33, 0.3)),
        gate="timber", bridge="stone", cover="crates", cover_n=0,
        obst=[P("water", "curb", (0.5, 2.0), (3.2, 3.2)), W("stalls", (-13, -10), (-3, -10)), W("stalls", (4, -2), (13, -2)), W("stalls", (-13, 10), (-4, 10)),
              W("stalls", (3, 16), (12, 16))],
        details=[d_lanterns_string, d_fountain(0.5, 2.0), d_scatter(["barrels", "crates", "cart", "lanterns"], 10)],
        game_look=(0, 2)),
    "28_frostfire_forge": dict(**COMMON, title="Frostfire Forge", walls="crenel", wall_h=3.4, north_h=2.4, south_h=0.9, snow=0.8,
        floor="snowflag", floorc=((0.16, 0.15, 0.15), (0.32, 0.3, 0.29)), stone=((0.12, 0.12, 0.13), (0.3, 0.29, 0.29)),
        gate="portcullis", bridge="stone", cover="columns", cover_n=0,
        obst=[P("lava", "curb", (-7, -2), (1.4, 8.0), shape="rect"), P("lava", "curb", (7, 8), (1.4, 8.0), shape="rect"), W("ruin", (-2, 16), (6, 16), 1.6),
              W("ruin", (-6, -16), (2, -16), 1.6)],
        details=[d_snow, d_banners, d_braziers, d_scatter(["anvil", "anvil", "crates", "swords", "barrels"], 14)],
        sun=(8, 155), sun_col=(0.7, 0.75, 1.0), sun_str=1.2, sky=((0.04, 0.05, 0.1), (0.25, 0.22, 0.3)), sky_str=0.45,
        haze_col=(0.25, 0.2, 0.25), haze=0.5, exposure=0.6, gate_light=(1.0, 0.65, 0.35), sea=((0.015, 0.03, 0.05), (0.05, 0.1, 0.14)), game_look=(0, 4)),
    "29_windswept_barrow": dict(**COMMON, title="Windswept Barrow", walls="ruin", wall_h=1.8, north_h=1.4, south_h=0.8,
        floor="grass", floorc=((0.08, 0.09, 0.05), (0.22, 0.22, 0.12)), stone=((0.2, 0.2, 0.19), (0.4, 0.4, 0.38)), moss=(0.12, 0.15, 0.07),
        gate="round", bridge="stone", cover="rocks", cover_n=0, grass=(0.2, 0.2, 0.1), grass_n=900, grass_walls=False,
        obst=[W("mound", (-12, -8), (-4, -8), 2.2), W("mound", (4, 4), (12, 4), 2.2), W("mound", (-10, 16), (-2, 16), 2.2),
              W("stones", (-2, -2), (-2, 8), 2.4), W("stones", (6, -16), (12, -12), 2.4), P("bog", "reeds", (8, 16), (2.8, 2.2)), P("bog", "reeds", (-8, 4), (2.4, 2.0))],
        details=[d_scatter(["deadtree", "bones", "lanterns", "deadtree"], 10)],
        sun=(12, 155), sun_col=(1.0, 0.75, 0.55), sun_str=1.6, sky=((0.25, 0.28, 0.34), (0.6, 0.55, 0.5)), sky_str=0.5,
        haze_col=(0.48, 0.48, 0.46), haze=0.55, gate_light=(1.0, 0.78, 0.5), sea=((0.03, 0.06, 0.08), (0.08, 0.18, 0.2)), game_look=(0, 4)),
    "30_abyssal_watergate": dict(**COMMON, title="Abyssal Watergate", walls="ashlar", wall_h=4.5, north_h=3.0, south_h=0.9, buttress=True,
        floor="slab", floorc=((0.14, 0.14, 0.15), (0.3, 0.3, 0.31)), wet=0.6, stone=((0.12, 0.12, 0.13), (0.28, 0.28, 0.29)),
        gate="iron", bridge="stone", cover="pillars", cover_n=2,
        obst=[P("water", "curb", (-6, 2), (1.3, 12.0), shape="rect"), P("water", "curb", (6, 2), (12.0, 1.3), shape="rect", rot=0),
              W("ruin", (-14, -16), (-8, -16), 1.8), W("ruin", (8, 18), (14, 18), 1.8)],
        details=[d_banners, d_braziers, d_scatter(["barrels", "rope", "crates", "lanterns", "swords"], 12)],
        sun=(18, 155), sun_col=(0.75, 0.82, 1.0), sun_str=1.4, sky=((0.06, 0.07, 0.1), (0.3, 0.32, 0.36)), sky_str=0.4,
        haze_col=(0.25, 0.27, 0.32), haze=0.5, exposure=0.6, gate_light=(1.0, 0.75, 0.45), sea=((0.015, 0.03, 0.05), (0.06, 0.1, 0.13)), waves=1.3, game_look=(0, 4)),
}

T = THEMES3[THEME]
OBST[:] = T.get("obst", [])
COVER[:] = [p for p in COVER if not obstacle_near(p[0], p[1], 2.0)]
build(T)
chars3()
auto_camera()
render2(T)
compositor(T)
stats()
bpy.ops.render.render(write_still=True)
print("RENDERED", OUT)
# second shot: the in-game camera distance, looking at the fight area
gc = game_camera(T.get("game_look", (0, 4)), view_width=T.get("game_width", 16.0))
scene.camera = gc
dcam = gc.location.length
scene.world.mist_settings.start = dcam * 1.6; scene.world.mist_settings.depth = dcam * 3.0
scene.render.filepath = OUT.replace(".png", "_game.png")
bpy.ops.render.render(write_still=True)
print("RENDERED", scene.render.filepath)
