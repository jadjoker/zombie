"""
Rocky Shark (Risk of Copyright, Godot), issue #29: a boulder shark, like the sandstone rock shark on the Greek
coast that people paint. A chunky wedge of sandstone with a tan top and a white painted belly and lip, a long
grin full of small teeth, big neon green alien eyes, glowing neon craters, a tall stone dorsal fin and big fins.

Builds and exports to godot/assets/models/creatures/rockyshark/:
  head.glb        the boulder head ("Head" tan, "HeadBelly" white lip and nose), the "Jaw" (white, with its teeth)
                  pivoted at its hinge so the game can bite, the dark "Mouth", two alien eyes, holes and craters
  segment_fin.glb the first body section with the dorsal fin
  segment0/1.glb  the other body sections
  tail.glb        the last section with the caudal fin and its cartoon lines
  fin_l/r.glb     the pectoral fins (the game flaps them as it swims)

Run headless: blender -b --factory-startup --python Tools/blender/rocky_shark.py

Modelled in s&box units around the unit "sphere" each section replaces (radius R = 32, +X forward, +Y left,
+Z up), then exported at 1/64 scale turned to Godot axes, so each part is a radius-0.5 shape facing -Z. The game
scales it per section and paints the meshes by name (RockyShark.Sculpt): names with "Belly" and the Jaw are the
white paint, the rest of the rock is tan; "Hole*" are weak points, "Pores*" and "Eye*" glow, "Pupil*" are dark.
"""
import bpy
import bmesh
import math
import os
import random
from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree

exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/creatures/rockyshark"
R = 32.0
GREEN = (0.22, 1.0, 0.08)   # neon green: holes, crater glow and eyes
TAN = (0.62, 0.48, 0.32)
WHITE = (0.93, 0.91, 0.86)


def mat(name, color, rough=0.5, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    if emit > 0:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m


def mats():
    return dict(rock=mat("Rock", TAN, 0.9), belly=mat("Belly", WHITE, 0.85), glow=mat("Glow", GREEN, 0.35, emit=2.5),
                fin=mat("Fin", (0.55, 0.42, 0.28), 0.9), line=mat("FinLine", (0.30, 0.21, 0.13), 0.9),
                teeth=mat("Teeth", (1.0, 0.98, 0.92), 0.3), mouth=mat("Mouth", (0.07, 0.02, 0.02), 0.8),
                eye=mat("Eye", GREEN, 0.2, emit=3.0), pupil=mat("Pupil", (0.01, 0.01, 0.015), 0.1))


def adopt(child, parent):
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()


# ---------------------------------------------------------------- shapes

def loft(name, x0, x1, shape, stations=20, ring=28):
    """A boulder from cross sections along X. shape(u) gives (half width, top, bottom, squareness) at u in 0..1;
    each section is a superellipse, so a squareness above 2 makes the soft-cornered block of a weathered rock."""
    bm = bmesh.new()
    rings = []
    for k in range(stations):
        u = 0.5 - 0.5 * math.cos(math.pi * k / (stations - 1))   # denser at the ends, where it rounds off
        w, top, bot, e = shape(u)
        mid, h = (top + bot) / 2, (top - bot) / 2
        x = x0 + (x1 - x0) * u
        verts = []
        for i in range(ring):
            a = math.tau * i / ring
            c, s = math.cos(a), math.sin(a)
            y = w * math.copysign(abs(c) ** (2 / e), c)
            z = mid + h * math.copysign(abs(s) ** (2 / e), s)
            verts.append(bm.verts.new((x, y, z)))
        rings.append(verts)
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(ring):
            bm.faces.new((r0[i], r0[(i + 1) % ring], r1[(i + 1) % ring], r1[i]))
    for verts, flip in ((rings[0], True), (rings[-1], False)):
        c = bm.verts.new(sum((v.co for v in verts), Vector()) / len(verts))
        for i in range(ring):
            f = (verts[i], verts[(i + 1) % ring], c)
            bm.faces.new(f[::-1] if flip else f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    sub = o.modifiers.new("sub", "SUBSURF")
    sub.levels = 1
    bpy.ops.object.modifier_apply(modifier="sub")
    return o


def profile_fin(name, pts, thickness, plane="xz"):
    """A crisp fin slab from a 2D outline, given thickness, with its edges softly bevelled like worn stone."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    verts = [bm.verts.new((x, 0, z) if plane == "xz" else (x, z, 0)) for x, z in pts]
    face = bm.faces.new(verts)
    bmesh.ops.triangulate(bm, faces=[face])
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    sol = o.modifiers.new("solid", "SOLIDIFY")
    sol.thickness = thickness
    sol.offset = 0.0
    bev = o.modifiers.new("bevel", "BEVEL")
    bev.width = thickness * 0.3
    bev.segments = 2
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.object.modifier_apply(modifier="solid")
    bpy.ops.object.modifier_apply(modifier="bevel")
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    return o


def rounding(u, back, front, floor=0.0):
    """1 in the middle, easing to `floor` over the first `back` and the last `front` of the length."""
    f = 1.0
    if back > 0 and u < back:
        f = min(f, floor + (1 - floor) * math.sqrt(max(0.0, u / back)))
    if front > 0 and u > 1 - front:
        f = min(f, floor + (1 - floor) * math.sqrt(max(0.0, (1 - u) / front)))
    return f


def weather(o, seed, big=0.05, fine=0.012):
    """Weathered sandstone: broad lumps and a fine grainy roughness pushed along the normals."""
    me = o.data
    me.update()
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    for v in me.vertices:
        p = v.co / R
        d = big * noise.noise(p * 1.6 + off) + fine * noise.noise(p * 9.0 + off * 2)
        v.co += v.normal * d * R
    me.update()
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.object.shade_smooth()


def bvh_of(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.transform(o.matrix_world)
    t = BVHTree.FromBMesh(bm)
    bm.free()
    return t


def snap(bvh, x, side, z):
    """The skin straight out to one side at (x, z), and its normal."""
    hit, nrm, _, _ = bvh.ray_cast(Vector((x, side * 4 * R, z)), Vector((0, -side, 0)))
    return hit, nrm


def paint_split(o, white, belly_mat):
    """Paints faces where white(centre) is true with the belly material and splits them off as `<name>Belly`."""
    o.data.materials.clear()
    o.data.materials.append(o.get("rock_mat") or bpy.data.materials["Rock"])
    o.data.materials.append(belly_mat)
    mw = o.matrix_world
    any_white = False
    for f in o.data.polygons:
        f.material_index = 1 if white(mw @ f.center) else 0
        any_white |= f.material_index == 1
    if not any_white:
        o.data.materials.pop(index=1)
        return None
    name = o.name
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    o.active_material_index = 1
    bpy.ops.object.material_slot_select()
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    belly = [x for x in bpy.context.selected_objects if x != o][0]
    belly.name = name + "Belly"
    for x, keep in ((o, "Rock"), (belly, "Belly")):
        for i in reversed(range(len(x.data.materials))):
            if x.data.materials[i].name != keep and len(x.data.materials) > 1:
                x.data.materials.pop(index=i)
    o.name = name
    adopt(belly, o)
    return belly


# ---------------------------------------------------------------- details

def crater(name, hit, nrm, r, m, rims, depth=0.4):
    """A small meteor-strike crater: a raised rock rim and a glowing neon bowl sunk inside."""
    rot = nrm.to_track_quat("Z", "Y").to_euler()
    bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.32 * r, major_segments=10, minor_segments=4,
                                     location=tuple(hit + nrm * 0.02 * r), rotation=tuple(rot))
    rim = bpy.context.active_object
    rim.name = name + "Rim"
    rim.scale = (1.0, 1.0, 0.55)
    rims.append(rim)
    bowl = ellipsoid(name, tuple(hit - nrm * depth * r * 0.5), (0.84 * r, 0.84 * r, depth * r), segs=8)
    bowl.rotation_euler = rot
    bowl.data.materials.append(m["glow"])
    return bowl


def scatter(bvh, seed, tag, m, rims, rows, cols, x0, x1, avoid=(), keep=lambda p: True, rmin=0.045, rmax=0.085, gap=0.24):
    """Craters spread evenly over every side: a jittered grid of rays along the body and round it."""
    rnd = random.Random(seed)
    out, placed = [], []
    for i in range(rows):
        for j in range(cols):
            x = x0 + (x1 - x0) * (i + rnd.uniform(0.15, 0.85)) / rows
            a = math.tau * (j + rnd.uniform(0.1, 0.9)) / cols + i * 0.41
            d = Vector((0, math.cos(a), math.sin(a)))
            hit, nrm, _, _ = bvh.ray_cast(Vector((x, 0, 0)) + d * 4 * R, -d)
            if hit is None or not keep(hit):
                continue
            if any((hit - Vector(p)).length < 0.28 * R for p in avoid) or any((hit - q).length < gap * R for q in placed):
                continue
            out.append(crater(f"Pore{tag}{len(out)}", hit, nrm, rnd.uniform(rmin, rmax) * R, m, rims))
            placed.append(hit)
    return out


def finish_craters(body, rims, pores, tag, m, white):
    """Every rim joined into one rock mesh (split tan and white like the skin under it), every bowl into one glow."""
    if rims:
        r = assemble(rims, f"Rims{tag}", smooth=True)
        r.data.materials.clear()
        r.data.materials.append(m["rock"])
        adopt(r, body)
        paint_split(r, white, m["belly"])
    if pores:
        p = assemble(pores, f"Pores{tag}", smooth=True)
        p.data.materials.clear()
        p.data.materials.append(m["glow"])
        adopt(p, body)


def teeth_along(name, bvh, x0, x1, count, up, length, seed, inset=0.86, probe_z=0.0, radius=1.5):
    """A row of small, uneven teeth along a lip: each set at the lip's edge (found by rays) and pointing across the gap."""
    rnd = random.Random(seed)
    out = []
    for k in range(count):
        x = x0 + (x1 - x0) * (k + 0.5) / count
        side_hit, _ = snap(bvh, x, 1, probe_z)
        if side_hit is None:
            continue
        for side in (-1, 1):
            y = side * abs(side_hit.y) * inset
            hit, _, _, _ = bvh.ray_cast(Vector((x, y, 3 * R if up else -3 * R)), Vector((0, 0, -1 if up else 1)))
            if hit is None:
                continue
            base = hit + Vector((0, 0, -0.03 * R if up else 0.03 * R))
            ln = length * rnd.uniform(0.75, 1.2)
            tilt = Vector((rnd.uniform(-0.1, 0.1), side * 0.12, 1 if up else -1))
            out.append(cone(f"{name}{k}{'L' if side > 0 else 'R'}", tuple(base), tuple(tilt), radius * rnd.uniform(0.85, 1.2), ln, 6))
    return out


def alien_eye(name, bvh, x, side, z, m, length=0.32, height=0.17, slant=24):
    """The big slanted almond eye of the Kraken and the Squilien, laid on the skin and glowing neon green, with a
    thin dark slit. Tilted down toward the snout."""
    hit, nrm = snap(bvh, x, side, z)
    fwd = Vector((1, 0, 0))
    fwd = (fwd - nrm * fwd.dot(nrm)).normalized()
    up = nrm.cross(fwd).normalized() * side
    rot = Matrix.Rotation(math.radians(-slant * side), 3, nrm)
    fwd, up = rot @ fwd, rot @ up
    basis = Matrix((fwd, up, nrm)).transposed()
    centre = hit + nrm * 0.02 * R
    e = ellipsoid(name, tuple(centre), (length * R, height * R, 0.08 * R), segs=24)
    e.rotation_euler = basis.to_euler()
    e.data.materials.append(m["eye"])
    p = ellipsoid("Pupil" + name[-1], tuple(centre + nrm * 0.06 * R), (0.035 * R, 0.13 * R, 0.03 * R), segs=14)
    p.rotation_euler = basis.to_euler()
    p.data.materials.append(m["pupil"])
    adopt(p, e)
    return e, centre


# ---------------------------------------------------------------- parts

def head():
    clear()
    m = mats()

    # The upper head: a big sandstone wedge. Broad and high at the back, the top sloping down to a blunt, rounded
    # nose; flat underneath, where the long grin is.
    def upper(u):
        f = rounding(u, 0.10, 0.12, 0.35)
        w = (0.80 - 0.36 * u ** 1.2) * R
        top = (0.60 + 0.20 * math.sin(math.pi * min(1.0, u * 1.2)) - 0.36 * u * u) * R
        bot = (-0.04 + 0.05 * u) * R
        mid = (top + bot) / 2
        h = (top - bot) / 2 * (0.4 + 0.6 * f)
        return w * f, mid + h, mid - h, 3.6
    body = loft("Head", -0.62 * R, 2.10 * R, upper)
    weather(body, 1)
    bvh = bvh_of(body)

    # The lower jaw: a massive white chin, modelled around its hinge at the back corner of the grin and then
    # placed there. The game rotates this node on X to bite.
    def lower(u):
        f = rounding(u, 0.10, 0.20, 0.55)
        w = (0.74 - 0.32 * u ** 1.3) * R
        top = 0.0
        bot = -(0.58 - 0.24 * u ** 1.6) * R
        h = (top - bot) / 2
        mid = (top + bot) / 2
        return w * f, mid + h * (0.5 + 0.5 * f), mid - h * (0.5 + 0.5 * f), 3.8
    jaw = loft("Jaw", -0.60 * R, 1.75 * R, lower)
    weather(jaw, 2, big=0.035)
    jaw.data.materials.append(m["belly"])
    jbvh = bvh_of(jaw)
    lower_teeth = assemble(teeth_along("ToothJ", jbvh, 0.05 * R, 1.62 * R, 11, True, 0.075 * R, 3, probe_z=-0.06 * R), "TeethLower", smooth=True)
    lower_teeth.data.materials.clear()
    lower_teeth.data.materials.append(m["teeth"])
    adopt(lower_teeth, jaw)
    # Hang it just under the lip: measure where the head's underside and the jaw's top really are (the smoothing
    # pulls both in), and leave a narrow dark gap for the teeth.
    under = [bvh.ray_cast(Vector((x * R, 0, -3 * R)), Vector((0, 0, 1)))[0] for x in (0.4, 0.9, 1.4)]
    over = [jbvh.ray_cast(Vector((x * R, 0, 3 * R)), Vector((0, 0, -1)))[0] for x in (0.4, 0.9, 1.4)]
    lip = sum(h.z for h in under if h) / max(1, sum(1 for h in under if h))
    chin = sum(h.z for h in over if h) / max(1, sum(1 for h in over if h))
    jaw.location = (0.0, 0.0, lip - chin - 0.035 * R)
    print("[rocky_shark] lip", round(lip / R, 3), "jaw top", round(chin / R, 3), "jaw at", round(jaw.location.z / R, 3))
    adopt(jaw, body)

    # The dark inside of the grin, between the lips.
    mouth = loft("Mouth", 0.0, 1.90 * R, lambda u: ((0.68 - 0.31 * u) * R * rounding(u, 0.05, 0.12, 0.3), lip + 0.04 * R, lip - 0.16 * R, 3.0), stations=10, ring=16)
    mouth.data.materials.append(m["mouth"])
    adopt(mouth, body)

    upper_teeth = assemble(teeth_along("ToothU", bvh, 0.05 * R, 1.95 * R, 12, False, 0.09 * R, 4, probe_z=0.05 * R), "TeethUpper", smooth=True)
    upper_teeth.data.materials.clear()
    upper_teeth.data.materials.append(m["teeth"])
    adopt(upper_teeth, body)

    # Alien eyes high on the sides, behind the slope of the nose.
    eyes = []
    for side, name in ((1, "EyeL"), (-1, "EyeR")):
        e, at = alien_eye(name, bvh, 0.78 * R, side, 0.46 * R, m)
        adopt(e, body)
        eyes.append(at)
        print(f"[rocky_shark] {name} at", tuple(round(c / R, 3) for c in at), "R")

    # Weak points: two bigger neon craters per flank. Then small craters all over.
    rims, weak = [], []
    for side, tag in ((1, "L"), (-1, "R")):
        for i, (x, z) in enumerate(((0.05, 0.22), (1.30, 0.14))):
            hit, nrm = snap(bvh, x * R, side, z * R)
            if hit is not None:
                adopt(crater(f"Hole{tag}{i}", hit, nrm, 0.14 * R, m, rims, depth=0.5), body)
                weak.append(hit)
    pores = scatter(bvh, 7, "H", m, rims, 9, 10, -0.45 * R, 1.95 * R, avoid=eyes + weak, keep=lambda h: h.z > 0.06 * R)

    # Painted white: the upper lip band, rising toward the nose, and the nose itself, like the rock in the photo.
    def white(p):
        return p.z < (0.10 + 0.22 * max(0.0, min(1.0, (p.x / R - 1.0) / 0.9))) * R
    finish_craters(body, rims, pores, "H", m, white)
    paint_split(body, white, m["belly"])
    return body


def body_section(name, seed, fin=False):
    clear()
    m = mats()

    # A chunky block of sandstone, a little narrower at the back so the chain steps down to the tail. The ends
    # stay blunt: each tucks inside its neighbour.
    def shape(u):
        f = rounding(u, 0.14, 0.14, 0.55)
        taper = 1.0 - 0.12 * u
        w = 0.88 * R * taper
        top = 0.80 * R * taper
        bot = -0.76 * R * taper
        return w * f, top * f, bot * f, 2.35
    body = loft(name, -1.50 * R, 1.50 * R, shape, stations=16)
    weather(body, seed)
    bvh = bvh_of(body)

    if fin:
        # The tall, sharp dorsal fin: a thick stone slab, swept back with a hooked trailing edge.
        outline = [(0.85 * R, 0.55 * R), (0.45 * R, 1.10 * R), (-0.05 * R, 1.80 * R), (-0.45 * R, 2.45 * R),
                   (-0.55 * R, 1.60 * R), (-0.78 * R, 1.05 * R), (-1.15 * R, 0.55 * R)]
        f = profile_fin("FinDorsal", outline, 0.16 * R)
        f.data.materials.append(m["fin"])
        adopt(f, body)

    rims, weak = [], []
    for side, tag in ((1, "L"), (-1, "R")):
        hit, nrm = snap(bvh, 0.0, side, 0.10 * R)
        if hit is not None:
            adopt(crater(f"Hole{tag}{seed}", hit, nrm, 0.16 * R, m, rims, depth=0.5), body)
            weak.append(hit)
    keep = (lambda h: not (fin and h.z > 0.5 * R and -1.2 * R < h.x < 0.9 * R and abs(h.y) < 0.25 * R))
    pores = scatter(bvh, 11 + seed, "S", m, rims, 8, 11, -1.30 * R, 1.30 * R, avoid=weak, keep=keep)

    def white(p):
        return p.z < (-0.18 + 0.08 * math.sin(p.x / R * 2.2 + seed)) * R
    finish_craters(body, rims, pores, "S", m, white)
    paint_split(body, white, m["belly"])
    return body


def tail():
    clear()
    m = mats()

    def shape(u):
        # u runs from the tip (0) to the front (1).
        f = rounding(u, 0.0, 0.08, 0.7)
        s = 0.22 + 0.78 * u ** 0.8
        return 0.62 * R * s * f, 0.56 * R * s * f, -0.50 * R * s * f, 2.6
    body = loft("Tail", -1.55 * R, 0.95 * R, shape, stations=16)
    weather(body, 5, big=0.04)
    bvh = bvh_of(body)

    # A tall shark caudal fin: upper lobe long, lower lobe short.
    fan = [(-1.25 * R, 0.10 * R), (-1.50 * R, 0.95 * R), (-2.25 * R, 1.55 * R), (-2.05 * R, 0.85 * R),
           (-2.35 * R, 0.45 * R), (-2.00 * R, 0.05 * R), (-2.25 * R, -0.30 * R), (-2.00 * R, -0.62 * R),
           (-2.10 * R, -1.05 * R), (-1.55 * R, -0.62 * R), (-1.25 * R, -0.08 * R)]
    f = profile_fin("TailFin", fan, 0.12 * R)
    f.data.materials.append(m["fin"])
    adopt(f, body)

    # Cartoon fin lines fanning from the root to the tips, raised a touch on both faces.
    root = Vector((-1.30 * R, 0, 0.0))
    tips = [(-2.12 * R, 1.38 * R), (-1.98 * R, 0.85 * R), (-2.22 * R, 0.45 * R), (-2.05 * R, 0.06 * R),
            (-2.15 * R, -0.28 * R), (-2.00 * R, -0.62 * R), (-2.02 * R, -0.95 * R)]
    rays = []
    for k, (tx, tz) in enumerate(tips):
        end = root + (Vector((tx, 0, tz)) - root) * 0.86
        for y in (0.065 * R, -0.065 * R):
            rays.append(tube(f"ray{k}{'a' if y > 0 else 'b'}", root + Vector((0, y, 0)), end + Vector((0, y, 0)), 0.035 * R, 0.012 * R, 6))
    lines = assemble(rays, "FinLines", smooth=True)
    lines.data.materials.clear()
    lines.data.materials.append(m["line"])
    adopt(lines, body)

    rims, weak = [], []
    for side, tag in ((1, "L"), (-1, "R")):
        hit, nrm = snap(bvh, 0.10 * R, side, 0.06 * R)
        if hit is not None:
            adopt(crater(f"Hole{tag}t", hit, nrm, 0.12 * R, m, rims, depth=0.5), body)
            weak.append(hit)
    pores = scatter(bvh, 5, "T", m, rims, 6, 8, -1.20 * R, 0.80 * R, avoid=weak, rmin=0.04, rmax=0.07, gap=0.2)

    def white(p):
        return p.z < -0.12 * R
    finish_craters(body, rims, pores, "T", m, white)
    paint_split(body, white, m["belly"])
    return body


def side_fin(side):
    clear()
    m = mats()
    # A big swept pectoral fin for swimming: a stone slab, root at the origin, spreading back (-X) and out.
    k = 2.0
    pts = [(0.10 * R, 0.0), (0.0, 0.30 * R), (-0.35 * R, 0.62 * R), (-0.85 * R, 0.78 * R),
           (-1.25 * R, 0.72 * R), (-1.05 * R, 0.50 * R), (-1.35 * R, 0.34 * R), (-1.00 * R, 0.18 * R),
           (-1.10 * R, 0.02 * R), (-0.55 * R, -0.04 * R)]
    o = profile_fin("FinL" if side > 0 else "FinR", [(x * k, side * y * k) for x, y in pts], 0.11 * R, plane="xy")
    o.data.materials.append(m["fin"])
    return o


# ---------------------------------------------------------------- export

def name_data(o):
    for x in [o] + list(o.children_recursive):
        if x.data is not None:
            x.data.name = x.name


def export(root, file):
    """Turn to Godot axes (s&box +X forward -> Blender +Y), scale R=32 to 0.5, export with its children."""
    os.makedirs(OUT, exist_ok=True)
    name_data(root)
    pivot = bpy.data.objects.new("Export", None)
    bpy.context.scene.collection.objects.link(pivot)
    root.parent = pivot
    pivot.rotation_euler = (0, 0, math.pi / 2)
    pivot.scale = (1 / 64,) * 3
    bpy.ops.object.select_all(action="DESELECT")
    pivot.select_set(True)
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, file), use_selection=True, export_format="GLB", export_yup=True)
    root.parent = None
    bpy.data.objects.remove(pivot, do_unlink=True)


def build():
    made = {}
    for name, fn in (("head.glb", head), ("segment_fin.glb", lambda: body_section("SegmentFin", 3, fin=True)),
                     ("segment0.glb", lambda: body_section("Segment0", 0)), ("segment1.glb", lambda: body_section("Segment1", 1)),
                     ("tail.glb", tail), ("fin_l.glb", lambda: side_fin(1)), ("fin_r.glb", lambda: side_fin(-1))):
        o = fn()
        export(o, name)
        made[name] = sum(len(x.data.vertices) for x in [o] + list(o.children_recursive) if x.type == "MESH")
    print("[rocky_shark] wrote", OUT)
    for name, verts in made.items():
        print(f"  {name:16s} {verts:5d} verts")
    return made


if __name__ == "__main__":
    build()
