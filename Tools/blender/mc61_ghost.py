# MC61 ghost redesign (from the user's sketch): a floating white ghost with one big eye, a tiny tilted top hat,
# a black bow tie and buttons, a long swaying tail and detached white-gloved hands.
# Exports to Assets/models/creatures/mc61g (the old humanoid pieces in mc61 stay as a fallback).
# Every part is modelled in its own local space (pivot at the origin, +X forward, +Z up).
import math
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

C = "mc61g"
stats = {}


def sculpt(pieces, name, voxel, smooth=3, decimate=None):
    return fuse(pieces, name, voxel=voxel, smooth=smooth, decimate=decimate)


def out(o, part):
    stats[part] = export(o, C, part)


def bevel_obj(o, width, segs=2):
    b = o.modifiers.new("bevel", 'BEVEL'); b.width = width; b.segments = segs; b.limit_method = 'ANGLE'
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier="bevel")
    return o


# ================= BODY (pivot at the middle of the torso) =================
# A smooth surface of revolution (a lathe), so there is no banding: a round dome of a head, a faint neck pinch
# where the bow tie sits, a full sheet-like body, and a bottom that the tail hangs from (z = -25).
import bmesh
DEPTH = 0.95   # front-to-back is a touch shallower than side-to-side
KEYS = [(60, 0.4), (58, 5.0), (55, 11.0), (50, 16.5), (43, 20.2), (36, 22.0), (30, 20.4), (25, 17.2), (21, 16.2), (16, 17.4), (9, 19.8), (1, 20.4), (-8, 18.4), (-17, 15.2), (-25, 10.6)]


def catmull(p0, p1, p2, p3, t):
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)


def sample_profile(per=6):
    pts = [KEYS[0]] + KEYS + [KEYS[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        for k in range(per):
            t = k / per
            out.append((catmull(pts[i - 1][0], pts[i][0], pts[i + 1][0], pts[i + 2][0], t), max(0.05, catmull(pts[i - 1][1], pts[i][1], pts[i + 1][1], pts[i + 2][1], t))))
    out.append(KEYS[-1])
    return out


def prof(z):
    best = min(sample_profile(10), key=lambda p: abs(p[0] - z))
    return best[1]


def bend(z):
    """The lower body sweeps back behind him, like the swoop in the sketch."""
    return -0.012 * max(0.0, 12.0 - z) ** 2


def lathe(name, ring_pts, segs=56):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for z, r in ring_pts:
        rings.append([bm.verts.new((math.cos(2 * math.pi * i / segs) * r * DEPTH + bend(z), math.sin(2 * math.pi * i / segs) * r, z)) for i in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for i in range(segs):
            bm.faces.new((a[i], a[(i + 1) % segs], b[(i + 1) % segs], b[i]))
    top = bm.verts.new((bend(ring_pts[0][0]), 0, ring_pts[0][0] + 0.4)); bot = bm.verts.new((bend(ring_pts[-1][0]), 0, ring_pts[-1][0] - 0.2))
    for i in range(segs):
        bm.faces.new((rings[0][(i + 1) % segs], rings[0][i], top))
        bm.faces.new((rings[-1][i], rings[-1][(i + 1) % segs], bot))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])   # outward normals: the game culls back faces, so inward ones look see-through
    bm.normal_update(); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.shade_smooth()
    return o


clear()
body = lathe("ghost_body", sample_profile(6))
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02); bpy.ops.object.mode_set(mode='OBJECT')
out(body, "body")

# ================= TAIL (4 short segments, each pivoted at its top joint, hanging down -Z) =================
SEG = 10.0
RADII = [10.4, 8.6, 6.2, 3.6, 0.6]
# The tail hangs from (bend(-25), 0, -25) = (-16.4, 0, -25) and its first piece leans back about 42 degrees.
for i in range(4):
    clear()
    r0, r1 = RADII[i], RADII[i + 1]
    p = [ellipsoid("cap", (0, 0, 0), (r0, r0, r0 * 0.9), 20), tube("seg", (0, 0, 0), (0, 0, -SEG), r0, r1, verts=20),
         ellipsoid("end", (0, 0, -SEG), (r1, r1, r1), 16)]
    out(sculpt(p, f"ghost_tail{i}", voxel=0.5, smooth=4, decimate=0.25), f"tail{i}")

# ================= EYE (pivot on the face at the eye centre) =================
# The eye is a shallow dome that follows the head's surface exactly, so its rim stays flush all the way round
# and never sinks into the head. A closed lid ring (heavier on top) outlines it, and the pupil rides on top.
import json
EZ = 38.0                       # eye height on the head
EL, EH = 13.0, 9.6              # half width and half height of the eye
TILT = math.radians(12)         # tilted up toward +Y like the sketch
DOME = 3.4


def hs(y, z):
    """Head surface x at (y, z), following the lathe (and its bend)."""
    r = prof(z)
    q = 1 - (y / r) ** 2
    return r * DEPTH * max(0.0, q) ** 0.5 + bend(z)


EYE_P = (hs(0, EZ), 0.0, EZ)


def hat_pose():
    """Where the hat sits: on the dome at the top-right of the head, laid flat to the surface (brim never sinks in)."""
    from mathutils import Vector
    x, y = -2.0, 11.0
    rr = math.hypot(x / DEPTH, y)
    z = min(KEYS[0][0], max(zk for zk, _ in KEYS if True))
    lo, hi = -25.0, 59.0
    for _ in range(40):            # find the height where the head radius equals rr
        mid = (lo + hi) / 2
        if prof(mid) > rr: lo = mid
        else: hi = mid
    z = hi if prof(hi) > rr * 0.99 else lo
    z = (lo + hi) / 2
    dz = 0.5
    drdz = (prof(z + dz) - prof(z - dz)) / (2 * dz)
    n = Vector((x / DEPTH ** 2, y, rr * -drdz * 1.0))      # gradient of x^2/D^2 + y^2 - r(z)^2
    n.normalize()
    if n.z < 0: n = -n
    p = Vector((x, y, z)) + n * 1.3
    q = n.to_track_quat('Z', 'X')
    e = q.to_euler('XYZ')
    return p, e


HAT_P, HAT_E = hat_pose()
json.dump({"eye": list(EYE_P), "hat_pos": list(HAT_P), "hat_euler_deg": [math.degrees(a) for a in HAT_E],
           "eye_tilt_deg": math.degrees(TILT), "eye_half": [EL, EH]},
          open(r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/mc61g_layout.json", "w"))


def on_eye(u, v):
    """Unit-disc eye coordinates -> (y, z) on the head, relative to the eye centre."""
    y0, z0 = u * EL, v * EH
    return (y0 * math.cos(TILT) - z0 * math.sin(TILT), y0 * math.sin(TILT) + z0 * math.cos(TILT))


def eye_dome(y, z):
    """Height of the eyeball above the head surface at a point (relative y, z)."""
    yy = y * math.cos(TILT) + z * math.sin(TILT); zz = -y * math.sin(TILT) + z * math.cos(TILT)
    rho2 = (yy / EL) ** 2 + (zz / EH) ** 2
    return 0.25 + DOME * max(0.0, 1 - rho2) ** 0.5


def disc_mesh(name, centre_uv, size_uv, height_fn, rings=16, segs=64, planar_uv=False):
    """A dome on the head: a polar grid whose every point sits on the head surface plus height_fn."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    cu, cv = centre_uv
    su, sv = size_uv
    def vert(rho, ang):
        u = cu + su * rho * math.cos(ang); v = cv + sv * rho * math.sin(ang)
        y, z = on_eye(u, v)
        h = height_fn(y, z, rho)
        return bm.verts.new((hs(y, EZ + z) + h - EYE_P[0], y, z))
    ring_list = []
    for k in range(0, rings + 1):
        rho = 0.05 + 0.95 * k / rings      # a tiny first ring instead of a single centre point (no pinch)
        ring_list.append([vert(rho, 2 * math.pi * i / segs) for i in range(segs)])
    bm.faces.new(ring_list[0])
    for a_, b_ in zip(ring_list, ring_list[1:]):
        for i in range(segs):
            bm.faces.new((a_[i], b_[i], b_[(i + 1) % segs], a_[(i + 1) % segs]))
    bm.normal_update()
    # make the normals face out (+X)
    for f in bm.faces:
        if f.normal.x < 0: f.normal_flip()
    if planar_uv:
        # Flat projection onto the eye's own oval, so a 2D picture of an eye maps straight onto the dome.
        uvl = bm.loops.layers.uv.new("eye")
        for f in bm.faces:
            for lp in f.loops:
                y, z = lp.vert.co.y, lp.vert.co.z
                yy = y * math.cos(TILT) + z * math.sin(TILT); zz = -y * math.sin(TILT) + z * math.cos(TILT)
                lp[uvl].uv = (0.5 + yy / (2 * EL), 0.5 + zz / (2 * EH))
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    sub = o.modifiers.new("sub", 'SUBSURF'); sub.levels = 1; sub.render_levels = 1; sub.boundary_smooth = 'PRESERVE_CORNERS'
    bpy.ops.object.modifier_apply(modifier="sub")   # smooths away any shading pinch at the middle
    bpy.ops.object.shade_smooth()
    if not planar_uv:
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02); bpy.ops.object.mode_set(mode='OBJECT')
    return o


clear()
out(disc_mesh("ghost_eyeball", (0, 0), (1, 1), lambda y, z, rho: eye_dome(y, z), planar_uv=True), "eyeball")
clear()
# Pupil: a bold dark oval a little left of centre, sitting on the eyeball.
PU, PV = (-0.22, 0.04), (0.36, 0.62)
def pupil_h(y, z, rho):
    return eye_dome(y, z) + 0.35 + 0.9 * max(0.0, 1 - rho * rho) ** 0.5
out(disc_mesh("ghost_pupil", PU if False else (PU[0], PU[1]), PV, pupil_h, rings=12, segs=48), "pupil")
clear()
# Lid ring: a CLOSED outline all the way round, heavier across the top.
lid = []
N = 72
pts = []
for i in range(N):
    ang = 2 * math.pi * i / N
    y, z = on_eye(math.cos(ang), math.sin(ang))
    pts.append((hs(y, EZ + z) + 0.7 - EYE_P[0], y, z, 0.75 + 0.75 * max(0.0, math.sin(ang + TILT * 0.0)) ** 1.5))
for i in range(N):
    p0, p1 = pts[i], pts[(i + 1) % N]
    lid.append(tube("lid", p0[:3], p1[:3], p0[3], p1[3], verts=10))
    lid.append(ellipsoid("lj", p1[:3], (p1[3],) * 3, 10))
out(sculpt(lid, "ghost_lids", voxel=0.28, smooth=1, decimate=0.4), "lids")

# ================= JACKET (pivot at the body centre) =================
# The tuxedo jacket over the ghost sheet: closed at the back and sides, open in a V at the front over the white
# shirt, with raised lapel edges, ending in a hem just below the waist. The ghost sheet flows out underneath.
def vee(z):
    """Half-angle of the front opening at height z (wide at the collar, narrowing to the waist)."""
    t = max(0.0, min(1.0, (26.0 - z) / 38.0))
    return math.radians(43.0 - 35.0 * t)


def jacket():
    me = bpy.data.meshes.new("ghost_jacket")
    bm = bmesh.new()
    N = 56
    rows = []
    zs = [26 - 38 * k / 24 for k in range(25)]    # collar (26) down to the hem (-12)
    for z in zs:
        r = prof(z) + 0.9
        phi = vee(z)
        row = []
        for i in range(N):
            th = phi + (2 * math.pi - 2 * phi) * i / (N - 1)
            row.append(bm.verts.new((math.cos(th) * r * DEPTH + bend(z), math.sin(th) * r, z)))
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(N - 1):
            bm.faces.new((b_[i], b_[i + 1], a_[i + 1], a_[i]))
    bm.normal_update(); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("ghost_jacket", me)
    bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    sol = o.modifiers.new("solid", 'SOLIDIFY'); sol.thickness = 1.4; sol.offset = -1.0
    bpy.ops.object.modifier_apply(modifier="solid")
    bpy.ops.object.shade_smooth()
    return o


clear()
jk = jacket()
# raised lapel edges along both sides of the V, fatter at the collar
edge = []
for sgn in (-1, 1):
    prev = None
    for k in range(25):
        z = 26 - 38 * k / 24
        r = prof(z) + 1.2
        th = vee(z) * sgn
        p = (math.cos(th) * r * DEPTH + bend(z), math.sin(th) * r, z)
        w = 2.4 - 1.2 * k / 24
        if prev is not None:
            edge.append(tube("lap", prev[0], p, prev[1], w, verts=10))
        edge.append(ellipsoid("lj", p, (w,) * 3, 10))
        prev = (p, w)
bpy.ops.object.select_all(action='DESELECT')
lap = sculpt(edge, "ghost_lapels", voxel=0.4, smooth=2, decimate=0.3)
bpy.ops.object.select_all(action='DESELECT')
jk.select_set(True); lap.select_set(True)
bpy.context.view_layer.objects.active = jk
bpy.ops.object.join()
jk = bpy.context.active_object
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02); bpy.ops.object.mode_set(mode='OBJECT')
out(jk, "jacket")

# ================= FRONT: bow tie + buttons (pivot at the body centre) =================
def fx(z, extra=0.3):
    return prof(z) * DEPTH + bend(z) + extra


clear()
BZ = 22.5
def wing(side, y0=2.2, y1=12.4, h0=3.0, h1=6.4, thick=2.8):
    """A squared-off bow-tie wing: a flat trapezoid (narrow at the knot, tall and square at the tip) that hugs the neck."""
    me = bpy.data.meshes.new("wing")
    bm = bmesh.new()
    def surf(y):
        r = prof(BZ)
        return r * DEPTH * max(0.0, 1 - (y / r) ** 2) ** 0.5 + bend(BZ) + 1.3 + 0.045 * y * y   # stands off the neck, wing tips swing forward a little
    vs = []
    for y, h in ((y0, h0), (y1, h1)):
        x = surf(side * y)
        for dz in (-h, h):
            for dx in (0.0, thick):
                vs.append(bm.verts.new((x + dx, side * y, BZ + dz)))
    # vs order: [y0: (-h,0),(-h,t),(+h,0),(+h,t)], [y1: same]
    q = lambda *i: bm.faces.new([vs[k] for k in i])
    q(0, 2, 3, 1); q(4, 5, 7, 6)              # knot-side and tip-side caps
    q(0, 1, 5, 4); q(2, 6, 7, 3)              # bottom, top
    q(0, 4, 6, 2); q(1, 3, 7, 5)              # back, front
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("wing", me)
    bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    bv = o.modifiers.new("bev", 'BEVEL'); bv.width = 0.45; bv.segments = 2; bv.limit_method = 'ANGLE'
    bpy.ops.object.modifier_apply(modifier="bev")
    return o


def knot_box():
    o = box("knot", (fx(BZ, 2.2), 0, BZ), (4.2, 4.6, 6.6), bevel=0.5)
    return o


bow_parts = [knot_box(), wing(1), wing(-1)]
out(assemble(bow_parts, "ghost_bow", smooth=False), "bow")   # crisp: no remesh, so the corners stay square
clear()
p = []
# a thin choker round the front of the neck, joining the bow
for k in range(24):
    a0, a1 = math.radians(-80 + k * 160 / 24), math.radians(-80 + (k + 1) * 160 / 24)
    pa = (prof(BZ) * DEPTH * math.cos(a0) + bend(BZ) + 0.2, prof(BZ) * math.sin(a0), BZ)
    pb = (prof(BZ) * DEPTH * math.cos(a1) + bend(BZ) + 0.2, prof(BZ) * math.sin(a1), BZ)
    p.append(tube("choker", pa, pb, 0.9, 0.9, verts=8)); p.append(ellipsoid("cj", pb, (0.9,) * 3, 8))
for z in (14, 6, -2):
    p.append(ellipsoid("btn", (fx(z, 0.5), 0, z), (1.3, 1.5, 1.5), 12))
out(sculpt(p, "ghost_front", voxel=0.3, smooth=1, decimate=0.4), "front")

# ================= HAT (pivot at the brim centre; the code tilts it) =================
clear()
crown = bevel_obj(cyl("crown", (0, 0, 6.5), 5.6, 5.1, 11, verts=40), 0.6)
brim = bevel_obj(cyl("brim", (0, 0, 0.7), 8.6, 8.6, 1.4, verts=48), 0.4)
band = bevel_obj(cyl("band", (0, 0, 2.6), 5.7, 5.7, 2.2, verts=40), 0.3)
out(assemble([crown, brim, band], "ghost_hat"), "hat")


# ================= HANDS: smooth, articulated =================
# Each hand is a palm plus separate finger pieces (3 per finger, 2 for the thumb), each pivoted at its own joint
# so the game can curl every finger. Pieces point along +Z from their joint; the palm faces +X, thumb on the side.
FING_Y = (-4.05, -1.35, 1.35, 4.05)          # finger root positions across the knuckles
FING_Z = 9.6                                 # knuckle height on the palm
SEGS = ((3.7, 1.35, 1.28), (3.1, 1.28, 1.2), (2.7, 1.2, 1.1))   # (length, start radius, end radius) prox/mid/dist
T_BASE = (3.6, 1.75, 1.55); T_TIP = (3.0, 1.55, 1.35)


def capsule(name, length, r0, r1, ball_start=True):
    p = [tube("c", (0, 0, 0), (0, 0, length), r0, r1, verts=20), ellipsoid("tip", (0, 0, length), (r1, r1, r1), 20)]
    if ball_start:
        p.append(ellipsoid("joint", (0, 0, 0), (r0 * 1.02, r0 * 1.02, r0 * 1.02), 20))
    return sculpt(p, name, voxel=0.14, smooth=5, decimate=0.35)


def palm(side):
    p = [ellipsoid("palm", (0, 0, 5.2), (3.4, 5.9, 5.3), 40), ellipsoid("heel", (0.2, 0, 2.2), (3.2, 5.1, 3.0), 32),
         cyl("cuff", (0, 0, -1.4), 4.5, 4.9, 3.6, verts=40), ellipsoid("cuffrim", (0, 0, -3.1), (4.9, 4.9, 0.9), 32)]
    for y in FING_Y:                              # soft knuckle bumps
        p.append(ellipsoid("knuckle", (0.4, y, FING_Z - 0.3), (1.9, 1.7, 1.6), 20))
    p.append(ellipsoid("thumbpad", (1.2, side * 4.7, 4.4), (2.3, 2.5, 3.0), 24))     # base of the thumb
    return sculpt(p, "ghost_palm", voxel=0.22, smooth=6, decimate=0.3)


clear(); out(palm(1), "palm_l")
clear(); out(palm(-1), "palm_r")
for i, (ln, r0, r1) in enumerate(SEGS):
    clear(); out(capsule(f"ghost_finger{i}", ln, r0, r1), ("f_prox", "f_mid", "f_dist")[i])
clear(); out(capsule("ghost_thumb0", *T_BASE), "t_base")
clear(); out(capsule("ghost_thumb1", *T_TIP), "t_tip")
json.dump({"eye": list(EYE_P), "hat_pos": list(HAT_P), "hat_euler_deg": [math.degrees(a) for a in HAT_E],
           "eye_tilt_deg": math.degrees(TILT), "eye_half": [EL, EH],
           "finger_y": list(FING_Y), "finger_z": FING_Z, "finger_segs": [list(x) for x in SEGS], "thumb": [list(T_BASE), list(T_TIP)]},
          open(r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/mc61g_layout.json", "w"))

result = stats
