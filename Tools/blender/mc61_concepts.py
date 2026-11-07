# MC61 redesign concepts. Builds three full-figure options and renders each to Tools/blender/mc61_concepts/.
# Concept art only: nothing here touches the game assets. +X is forward, +Z up, feet at z=0.
import math, mathutils
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())
from mathutils import Vector, Euler

OUT_DIR = r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/mc61_concepts/"
os.makedirs(OUT_DIR, exist_ok=True)

PAL = {
    "coat": (0.04, 0.035, 0.06), "trouser": (0.14, 0.12, 0.17), "white": (0.94, 0.93, 0.9), "red": (0.85, 0.05, 0.1),
    "skin": (0.55, 0.62, 0.5), "case": (0.35, 0.17, 0.07), "gold": (0.9, 0.72, 0.2), "dark": (0.18, 0.02, 0.06),
    "iris": (1.0, 0.15, 0.06), "black": (0.0, 0.0, 0.0), "plum": (0.28, 0.1, 0.4), "smoke": (0.42, 0.3, 0.75),
    "lilac": (0.62, 0.55, 0.7), "navy": (0.08, 0.1, 0.25),
}
_mats = {}


def mat(name):
    if name not in _mats:
        m = bpy.data.materials.new(name); m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs[0].default_value = PAL[name] + (1,)
        b.inputs["Roughness"].default_value = 0.45
        _mats[name] = m
    return _mats[name]


def paint(o, col):
    o.data.materials.clear(); o.data.materials.append(mat(col))
    return o


def group(parts, name, col, voxel=0.55, smooth=2):
    return paint(fuse(parts, name, voxel=voxel, smooth=smooth), col)


def ball(name, at, r, col):
    o = ellipsoid(name, at, r, segs=24)
    bpy.ops.object.shade_smooth()
    return paint(o, col)


def place(o, loc, rot=(0, 0, 0)):
    o.rotation_euler = Euler(rot, 'XYZ'); o.location = loc
    return o


def teeth(y0, y1, n, x, z, size, direction, col="white"):
    ts = []
    for i in range(n):
        y = y0 + (y1 - y0) * i / (n - 1)
        s = size * (0.75 + 0.5 * ((i * 7) % 3) / 2)
        ts.append(cone("tooth", (x, y, z), (0, 0, direction), s * 0.38, s, verts=8))
    return group(ts, "teeth", col, voxel=0.25, smooth=1)


def eye(at, r, iris=0.62, slit=False):
    """White eyeball, iris, pupil; faces +X."""
    ball("eyeball", at, (r * 0.5, r, r), "white")
    x = at[0] + r * 0.36
    ball("iris", (x, at[1], at[2]), (r * 0.28, r * iris, r * iris), "iris")
    pr = (r * 0.2, r * (0.14 if slit else 0.3), r * (0.5 if slit else 0.3))
    ball("pupil", (x + r * 0.2, at[1], at[2]), pr, "black")


def top_hat(height, brim_r, crown_r, band=True, tilt=(0, 0, 0), at=(0, 0, 0), curl=True, col="coat"):
    """Built around the origin (brim centre), then tilted and placed."""
    crown = cyl("crown", (0, 0, height / 2 + 0.8), crown_r, crown_r * 0.92, height, verts=48)
    brim = cyl("brim", (0, 0, 0.8), brim_r, brim_r, 1.6, verts=64)
    h = group([crown, brim], "hat", col, voxel=0.4, smooth=2)
    objs = [h]
    if band:
        b = cyl("band", (0, 0, 4.2), crown_r * 1.03, crown_r * 1.03, 5, verts=48)
        objs.append(group([b], "band", "red", voxel=0.35, smooth=1))
    for o in objs:
        place(o, at, tilt)
    return objs


def glove(at, spider=False, side=1, scale=1.0):
    """White glove, fingers pointing down (-Z) from the wrist at `at`."""
    ax, ay, az = at
    k = scale
    p = [ellipsoid("palm", (ax + 0.5 * k, ay, az - 3.6 * k), (3.2 * k, 4 * k, 4.2 * k)),
         ellipsoid("wrist", (ax, ay, az - 0.5 * k), (3.4 * k, 3.6 * k, 2 * k))]
    L = (11 if spider else 6.5) * k
    for i, y in enumerate((-2.6, -0.9, 0.9, 2.6)):
        yy = ay + y * k
        a = (ax + 1 * k, yy, az - 6.5 * k); m = (ax + 2.6 * k, yy, az - (6.5 + L * 0.5) * k); b = (ax + 1.4 * k, yy, az - (6.5 + L) * k)
        r = (0.95 if spider else 1.15) * k
        p += [tube("f1", a, m, r, r * 0.9, verts=10), ellipsoid("j", m, (r, r, r), 12), tube("f2", m, b, r * 0.9, r * 0.75, verts=10), ellipsoid("t", b, (r * 0.8,) * 3, 12)]
    p += [tube("th", (ax + 2 * k, ay + side * 3 * k, az - 3 * k), (ax + 3.8 * k, ay + side * 1.8 * k, az - 7.5 * k), 1.3 * k, 1.1 * k, verts=10)]
    return group(p, "glove", "white", voxel=0.3 * k, smooth=1)


def briefcase(at, size=(6, 22, 15), tilt=(0, 0, 0)):
    ax, ay, az = at
    body = box("case", (ax, ay, az - size[2] / 2 - 2.5), size, bevel=1.0)
    handle = tube("handle", (ax, ay - 3, az - 2.4), (ax, ay + 3, az - 2.4), 0.8, 0.8, verts=8)
    h2 = tube("h2", (ax, ay - 3.2, az - 2.4), (ax, ay - 3.2, az - 4.5), 0.8, 0.8, verts=8)
    h3 = tube("h3", (ax, ay + 3.2, az - 2.4), (ax, ay + 3.2, az - 4.5), 0.8, 0.8, verts=8)
    paint(body, "case"); bpy.ops.object.shade_smooth()
    brass = []
    for y in (-size[1] * 0.3, size[1] * 0.3):
        brass.append(box("clasp", (ax + size[0] / 2, ay + y, az - 5.5), (1.2, 2.8, 2.8), bevel=0.3))
    brass += [handle, h2, h3]
    return [body, group(brass, "brass", "gold", voxel=0.25, smooth=1)]


def render(name, target, dist, lens=50, tilt_deg=22, yaw_deg=-35, size=(700, 940)):
    sc = bpy.context.scene
    cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data); sc.collection.objects.link(cam)
    yaw = math.radians(yaw_deg); tilt = math.radians(tilt_deg)
    tgt = Vector(target)
    cam.location = tgt + Vector((math.cos(yaw) * math.cos(tilt), math.sin(yaw) * math.cos(tilt), math.sin(tilt))) * dist
    cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = lens
    sc.camera = cam
    ld = bpy.data.lights.new("sun", 'SUN'); ld.energy = 4.0
    l = bpy.data.objects.new("sun", ld); sc.collection.objects.link(l); l.rotation_euler = (0.7, 0.3, 0.8)
    fl = bpy.data.lights.new("fill", 'SUN'); fl.energy = 1.4
    f = bpy.data.objects.new("fill", fl); sc.collection.objects.link(f); f.rotation_euler = (1.1, -0.4, -2.2)
    w = sc.world or bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.55, 0.66, 1)
    # ground disc
    bpy.ops.mesh.primitive_circle_add(vertices=64, radius=60, fill_type='NGON', location=(0, 0, -0.2))
    g = bpy.context.active_object; g.name = "floor"
    g.data.materials.append(bpy.data.materials.new("floor")); g.data.materials[0].diffuse_color = (0.3, 0.33, 0.3, 1)
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.filepath = OUT_DIR + name + ".png"
    bpy.ops.render.render(write_still=True)


# =====================================================================================
# OPTION A: THE RINGMASTER. Tall, lanky, elegant and creepy. Long limbs, long coat tails,
# spidery gloves, a stovepipe hat and a thin razor grin.
# =====================================================================================
def option_a():
    clear()
    # legs
    legs = [ellipsoid("pelvis", (0, 0, 55), (7, 9.5, 6))]
    shoes = []
    for s in (-1, 1):
        hip, knee, ank = (0, s * 5, 53), (1.5, s * 5.2, 30), (0, s * 5.4, 7)
        legs += [tube("thigh", hip, knee, 3.8, 3.0), ellipsoid("knee", knee, (3.3, 3.3, 3.3), 16), tube("shin", knee, ank, 3.0, 2.3)]
        shoes += [ellipsoid("shoe", (3.5, s * 5.4, 3.2), (8, 3.6, 3.2)), cone("toe", (9, s * 5.4, 3.6), (1, 0, 0.35), 2.8, 7), ellipsoid("heel", (-2, s * 5.4, 3.4), (3, 3.4, 3.4))]
    group(legs, "trousers", "trouser", 0.5, 2)
    group(shoes, "shoes", "coat", 0.4, 2)
    # torso: narrow chest, long tails
    body = [ellipsoid("chest", (0, 0, 78), (10, 11.5, 17)), ellipsoid("waist", (0.5, 0, 63), (8.2, 9, 9))]
    for s in (-1, 1):
        body += [ellipsoid("shoulder", (0, s * 12.5, 90), (6, 6.2, 5.2)),
                 cone("tail", (-6, s * 4.5, 62), (-0.55, s * 0.12, -1), 5.2, 38, verts=24),
                 ellipsoid("lapel", (9.4, s * 5.8, 82), (1.3, 3.4, 11)), ellipsoid("peak", (8.8, s * 7.6, 91), (1.3, 3, 2.4))]
    group(body, "coat", "coat", 0.5, 2)
    group([ellipsoid("bib", (9.2, 0, 77), (3.2, 5.4, 12))] + [ellipsoid("stud", (12, 0, z), (0.8, 0.8, 0.8), 10) for z in (84, 78, 72)], "shirt", "white", 0.3, 1)
    group([ellipsoid("knot", (11.6, 0, 91), (1.6, 1.6, 1.7), 16)] + [ellipsoid("wing", (11.2, s * 3.8, 91), (1.6, 3.8, 3.2), 16) for s in (-1, 1)], "bow", "red", 0.25, 1)
    # arms
    for s in (-1, 1):
        shoulder, elbow, wrist = (0, s * 13, 89), (-1, s * 17.5, 68), (3, s * 17.5, 50)
        group([tube("upper", shoulder, elbow, 3.6, 2.9), ellipsoid("elb", elbow, (3.2, 3.2, 3.2), 16), tube("fore", elbow, wrist, 2.9, 2.5)], "sleeve", "coat", 0.4, 2)
        glove((wrist[0], wrist[1], wrist[2] - 0.5), spider=True, side=-s, scale=1.15)
    briefcase((3, -17.5, 29), size=(5, 18, 14))
    # head
    paint(group([tube("neck", (0, 0, 91), (0, 0, 100), 4, 3.5), ellipsoid("skull", (0, 0, 113), (12.5, 11.5, 16)), ellipsoid("jaw", (4, 0, 102.5), (8.5, 8.8, 7.6))]
                + [cone("ear", (-1, s * 10.5, 115), (-0.5, s * 1, 0.55), 2.8, 15) for s in (-1, 1)]
                + [ellipsoid("brow", (10, 0, 124), (4.5, 10.5, 2.4))], "head", "skin", 0.42, 2), "skin")
    eye((10.2, 0, 114), 8.6, slit=True)
    ball("mouth", (12.3, 0, 102.4), (2.4, 8.8, 2.0), "dark")
    teeth(-8.2, 8.2, 13, 13.6, 103.6, 2.6, -1)
    teeth(-7.6, 7.6, 12, 13.6, 101.2, 2.6, 1)
    top_hat(34, 16.5, 9.5, at=(-1, 0, 126.5), tilt=(0.08, 0, 0))
    render("option_a_ringmaster", (0, 0, 66), 330)
    render("option_a_front", (0, 0, 98), 190, yaw_deg=-8, tilt_deg=6)


# =====================================================================================
# OPTION B: THE FLOATING ILLUSIONIST. He really floats: no legs, the coat dissolves into a smoky tail,
# a cape behind, hands hover free of the sleeves, cards orbit him, a rakish tilted hat.
# =====================================================================================
def option_b():
    clear()
    tail = []
    for i in range(14):
        t = i / 13
        tail.append(ellipsoid("t", (-16 * t ** 1.6, 8 * math.sin(t * 5.2), 66 - 58 * t), (11 * (1 - t) + 1.6,) * 2 + (9 * (1 - t) + 2.2,), 20))
    group(tail, "smoke", "smoke", 0.6, 3)
    body = [ellipsoid("chest", (0, 0, 76), (11.5, 14, 16)), ellipsoid("waist", (1, 0, 62), (9.5, 11, 9))]
    for s in (-1, 1):
        body += [ellipsoid("shoulder", (0, s * 14.5, 87), (7, 7, 6)), ellipsoid("lapel", (10.4, s * 6.4, 80), (1.3, 3.6, 11.5))]
    group(body, "coat", "plum", 0.55, 2)
    group([ellipsoid("bib", (10.8, 0, 75), (3, 6, 12))] + [ellipsoid("stud", (13.6, 0, z), (0.9, 0.9, 0.9), 10) for z in (82, 76, 70)], "shirt", "white", 0.3, 1)
    group([ellipsoid("collar", (-6, s * 5, 93), (4, 4, 7)) for s in (-1, 1)] + [ellipsoid("cloak", (-10, 0, 74), (4, 25, 26))], "cape", "red", 0.7, 3)
    group([ellipsoid("knot", (13, 0, 90), (1.8, 1.8, 1.9), 16)] + [ellipsoid("wing", (12.6, s * 4.4, 90), (1.8, 4.4, 3.6), 16) for s in (-1, 1)], "bow", "gold", 0.28, 1)
    # floating hands, sleeves ending in flared cuffs well short of them
    for s in (-1, 1):
        sh = (0, s * 15, 86)
        wrist = (14 if s > 0 else 8, s * 36, 70 if s > 0 else 54)
        elb = (4, s * 28, 74)
        group([tube("sl", sh, elb, 5, 4.2), cone("cuff", elb, (wrist[0] - elb[0], wrist[1] - elb[1], wrist[2] - elb[2] - 3), 5.2, 6)], "sleeve", "plum", 0.4, 2)
        glove((wrist[0], wrist[1], wrist[2] + 1), spider=False, side=-s, scale=1.4)
    # head: a big round orb with one huge eye and a wide smile
    group([ellipsoid("orb", (0, 0, 108), (15, 15, 15.5)), tube("neck", (0, 0, 91), (0, 0, 98), 4.5, 4.5),
           ellipsoid("brow", (12, 0, 120), (4.5, 12, 2.5))] + [cone("ear", (-1, s * 14, 109), (-0.2, s * 1, 0.5), 3.4, 11) for s in (-1, 1)], "head", "skin", 0.45, 2)
    eye((12.4, 0, 110), 10.5)
    ball("mouth", (13.0, 0, 98.8), (2.6, 11, 2.8), "dark")
    teeth(-10, 10, 15, 14.4, 100.6, 3.0, -1)
    teeth(-9, 9, 13, 14.4, 97.0, 3.0, 1)
    top_hat(22, 14, 9, at=(-4, 4, 121), tilt=(0.32, -0.22, 0.1))
    # orbiting cards
    cards = []
    for k in range(5):
        a = k * 2 * math.pi / 5 + 0.4
        c = box("card", (0, 0, 0), (0.5, 8, 12), bevel=0.15)
        c.rotation_euler = (0.5, 0.3, a); c.location = (math.cos(a) * 31, math.sin(a) * 31, 70 + 11 * math.sin(a * 2))
        cards.append(c)
    group(cards, "cards", "white", 0.3, 1)
    render("option_b_illusionist", (0, 0, 62), 320)
    render("option_b_front", (0, 0, 98), 200, yaw_deg=-8, tilt_deg=6)


# =====================================================================================
# OPTION C: THE SHOWMAN BRUTE. Top heavy and cartoonish: huge head and one huge eye, enormous grin of
# jagged teeth, barrel chest and thick arms on stubby legs, a tiny hat perched on top, a giant case.
# =====================================================================================
def option_c():
    clear()
    legs, shoes = [], []
    for s in (-1, 1):
        legs += [tube("leg", (0, s * 10, 30), (1, s * 10.5, 7), 8, 6.6)]
        shoes += [ellipsoid("shoe", (4.5, s * 10.5, 4.2), (11, 7, 4.4)), ellipsoid("toe", (11, s * 10.5, 4), (4.5, 6.4, 3.8))]
    group(legs, "trousers", "trouser", 0.7, 2)
    group(shoes, "shoes", "coat", 0.6, 2)
    body = [ellipsoid("barrel", (0, 0, 52), (19, 25, 25)), ellipsoid("hips", (0, 0, 32), (14, 18, 8))]
    for s in (-1, 1):
        body += [ellipsoid("shoulder", (0, s * 26, 66), (11, 11, 10)), cone("tail", (-12, s * 6, 40), (-0.5, s * 0.1, -1), 8, 20, verts=20),
                 ellipsoid("lapel", (17, s * 10, 56), (2.2, 5, 15))]
    group(body, "coat", "coat", 0.8, 3)
    group([ellipsoid("vest", (12, 0, 47), (10, 18, 19))] + [ellipsoid("btn", (21.5, 0, z), (1.4, 1.4, 1.4), 12) for z in (58, 50, 42, 34)], "vest", "red", 0.5, 2)
    group([ellipsoid("knot", (20, 0, 66), (3.2, 3.2, 3.4), 16)] + [ellipsoid("wing", (19.4, s * 7.5, 66), (3.2, 7.5, 6.2), 16) for s in (-1, 1)], "bow", "gold", 0.4, 1)
    for s in (-1, 1):
        shoulder, elbow, wrist = (0, s * 28, 64), (3, s * 36, 42), (9, s * 36, 28)
        group([tube("up", shoulder, elbow, 10, 8.5), ellipsoid("elbow", elbow, (8.6, 8.6, 8.6), 16), tube("fore", elbow, wrist, 8.5, 7.5)], "sleeve", "coat", 0.6, 2)
        glove((wrist[0], wrist[1], wrist[2] - 1), spider=False, side=-s, scale=2.0)
    briefcase((9, -36, 17), size=(11, 36, 25))
    # head: huge, wider than the shoulders
    group([ellipsoid("skull", (3, 0, 94), (22, 24, 21)), ellipsoid("jaw", (9, 0, 80), (18, 21, 11)), ellipsoid("chin", (19, 0, 76), (6, 12, 5)),
           tube("neck", (0, 0, 66), (1, 0, 78), 11, 11)]
          + [ellipsoid("cheek", (16, s * 18, 82), (6, 6, 5)) for s in (-1, 1)]
          + [cone("ear", (-2, s * 22, 96), (-0.2, s * 1, 0.3), 5, 12) for s in (-1, 1)]
          + [ellipsoid("brow", (16, 0, 105), (7, 17, 4.2))], "head", "lilac", 0.8, 3)
    eye((19, 0, 96), 14)
    ball("mouth", (26, 0, 81), (3.6, 17, 5.2), "dark")
    teeth(-15, 15, 11, 28.2, 84.4, 6.4, -1)
    teeth(-14, 14, 10, 28.2, 77.8, 5.6, 1)
    top_hat(15, 11, 7.5, at=(-4, 7, 114), tilt=(0.4, -0.2, 0.2))
    render("option_c_showman", (0, 0, 58), 360)
    render("option_c_front", (0, 0, 88), 260, yaw_deg=-8, tilt_deg=6)


stats = {}
for fn in (option_a, option_b, option_c):
    fn()
result = "rendered"
