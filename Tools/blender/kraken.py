"""
The Kraken (Risk of Copyright, Godot, issue #30): a bulb head held up off the sea bed, its brain showing under a
spacesuit glass dome with a steel collar, hoses and an air tank; big slanted black eyes under brow ridges; the dome is the whole top half of the head, glowing
green holes, siphons, a round toothy mouth underneath facing down and a little forward, and eight sucker tentacles.
Run headless:  blender -b --factory-startup --python Tools/blender/kraken.py
Writes godot/assets/models/creatures/kraken/<part>.glb: metres, Y up, the mouth facing Godot forward (-Z).
  head.glb     origin on the ground under the head
  mouth.glb    the toothy mouth, origin at its middle, opening forward (the game shows it only while it sucks)
  chunk0..N    what the head bursts into when it dies, each still in the head's own space
  brain.glb    origin at the collar centre
  helmet.glb   origin at the collar centre
  seg0..7.glb  one tentacle segment each, origin at its joint, running forward, suckers underneath
Each glb holds one mesh per material, named after it: the game paints by that name (godot/Code/Creatures/Kraken.cs).
"""
import math
import os
import random

import bpy
from mathutils import Euler, Vector

OUT = os.environ.get("KRAKEN_OUT", r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/creatures/kraken")

# Shared with Kraken.cs.
COLLAR = Vector((0.0, -0.25, 2.78))
COLLAR_RADIUS = 1.1
SEGMENTS = 8
SEG_LENGTH = 0.85
ROOT_RADIUS = 0.34
TIP_RADIUS = 0.07
TAPER = (TIP_RADIUS / ROOT_RADIUS) ** (1.0 / SEGMENTS)

random.seed(30)


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for coll in (bpy.data.meshes, bpy.data.materials):
        for d in list(coll):
            coll.remove(d)


def mat(name, color):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


def finish(o, material, smooth=True):
    o.data.materials.clear()
    o.data.materials.append(mat(*material))
    if smooth:
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.shade_smooth()
    return o


def ball(center, radii, material, rot=(0, 0, 0), segs=28):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=max(8, segs // 2), radius=1, location=(0, 0, 0))
    o = bpy.context.active_object
    o.scale = radii if isinstance(radii, (tuple, list)) else (radii, radii, radii)
    bpy.ops.object.transform_apply(scale=True)
    o.rotation_euler = Euler(tuple(math.radians(a) for a in rot), "XYZ")
    o.location = center
    bpy.ops.object.transform_apply(location=True, rotation=True)
    return finish(o, material)


def cone(a, b, r0, r1, material, verts=16):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bpy.ops.object.transform_apply(rotation=True)
    return finish(o, material)


def torus(center, major, minor, material, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=32, minor_segments=10,
                                     location=center, rotation=tuple(math.radians(a) for a in rot))
    # Baked into the mesh: an object left with its own position drags its whole material group with it when joined.
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return finish(bpy.context.active_object, material)


def cut(o, z, keep_below=True):
    """Slices an object flat at a height and caps it."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.bisect(plane_co=(0, 0, z), plane_no=(0, 0, 1), use_fill=True, clear_outer=keep_below, clear_inner=not keep_below)
    bpy.ops.object.mode_set(mode="OBJECT")
    return o


def on_ball(center, radii, direction):
    """The point on an ellipsoid's surface along a direction from its centre, and the outward normal there."""
    d = Vector(direction).normalized()
    t = 1.0 / math.sqrt(sum((d[i] / radii[i]) ** 2 for i in range(3)))
    p = Vector(center) + d * t
    n = Vector([(p[i] - center[i]) / radii[i] ** 2 for i in range(3)]).normalized()
    return p, n


def facing(normal):
    """Euler (degrees) turning +Z onto a normal."""
    e = Vector((0, 0, 1)).rotation_difference(Vector(normal)).to_euler("XYZ")
    return tuple(math.degrees(a) for a in e)


def export(name, offset=Vector((0, 0, 0))):
    """Joins what's in the scene by material, moves the origin, and writes the glb."""
    os.makedirs(OUT, exist_ok=True)
    groups = {}
    for o in list(bpy.context.scene.objects):
        groups.setdefault(o.data.materials[0].name, []).append(o)
    polys = 0
    for material, objects in groups.items():
        bpy.ops.object.select_all(action="DESELECT")
        for o in objects:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objects[0]
        if len(objects) > 1:
            bpy.ops.object.join()
        o = bpy.context.active_object
        o.name = material
        o.data.name = material
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        o.location = -Vector(offset)
        bpy.ops.object.transform_apply(location=True)
        polys += len(o.data.polygons)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), use_selection=True, export_format="GLB", export_yup=True, export_apply=True)
    print("exported", name, polys, "polys")


SKIN = ("skin", (0.2, 0.3, 0.3))
BELLY = ("belly", (0.5, 0.56, 0.46))
EYE = ("eye", (0.01, 0.01, 0.015))
GLOW = ("glow", (0.3, 1.0, 0.35))
LIP = ("lip", (0.1, 0.15, 0.16))
MOUTH = ("mouth", (0.03, 0.02, 0.04))
TOOTH = ("tooth", (0.93, 0.9, 0.78))
COLLAR_M = ("collar", (0.72, 0.74, 0.78))
BRAIN = ("brain", (0.95, 0.55, 0.62))
FOLD = ("fold", (0.7, 0.32, 0.42))
GLASS = ("glass", (0.75, 0.92, 1.0))
TENTACLE = ("tentacle", (0.07, 0.11, 0.1))
SUCKER = ("sucker", (0.3, 1.0, 0.35))
FLESH = ("flesh", (0.72, 0.28, 0.4))

CRANIUM = ((0, -0.25, 2.7), (1.1, 1.1, 1.1))
FACE = ((0, 0.02, 1.9), (0.9, 0.92, 0.95))
MANTLE = ((0, 0.1, 1.2), (1.0, 1.0, 0.6))
CHUNKS = 0


def slice_off(o, co, no):
    """Cuts away the side of a plane that its normal points to, and caps the hole."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.bisect(plane_co=tuple(co), plane_no=tuple(no), use_fill=True, clear_outer=True, clear_inner=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return o


def eye(side):
    """One big slanted eye with its thin glowing rim, just under the collar."""
    # On the face, which bulges out past the head just under the collar (on the head itself they were half buried).
    c, r = FACE
    p, n = on_ball(c, r, (side * 0.46, 1.0, 0.3))
    yaw = math.degrees(math.atan2(n.x, n.y))
    # Laid flat on the skin there (it slopes inward below the collar) and standing a little proud of it.
    rot = (-math.degrees(math.asin(-n.z)) - 2, side * 38, -yaw)
    ball(p + n * 0.025, (0.25, 0.07, 0.54), GLOW, rot=rot)
    ball(p + n * 0.06, (0.215, 0.078, 0.5), EYE, rot=rot)


def crater(c, r, d, size):
    p, n = on_ball(c, r, d)
    torus(p + n * 0.005, size, size * 0.3, LIP, rot=facing(n))
    ball(p - n * size * 0.35, size * 0.95, GLOW, segs=14)


def mouth_parts(centre, normal, scale=1.0):
    """A round lip, dark inside, and a ring of teeth, opening along a direction."""
    centre, normal = Vector(centre), Vector(normal).normalized()
    across = Vector((1, 0, 0))
    up = normal.cross(across).normalized()
    torus(centre, 0.42 * scale, 0.13 * scale, LIP, rot=facing(normal))
    ball(centre - normal * 0.14 * scale, (0.4 * scale,) * 3, MOUTH, segs=20)
    for i in range(12):
        a = i / 12 * math.tau
        rim = centre + (across * math.cos(a) + up * math.sin(a)) * 0.4 * scale + normal * 0.04 * scale
        tip = centre + (across * math.cos(a) + up * math.sin(a)) * 0.16 * scale + normal * 0.13 * scale
        cone(rim, tip, 0.08 * scale, 0.008, TOOTH, verts=8)


def shell_chunk(ballspec, d, extras=None, seed=0):
    """
    A piece of the head for when it bursts: a thick slab of hide cut out round a direction, cracked skin outside
    and raw flesh on every cut face, with whatever was on that part of the head still on it.
    """
    global CHUNKS
    clear()
    rnd = random.Random(seed)
    c, r = ballspec
    c = Vector(c)
    d = Vector(d).normalized()
    u = d.orthogonal().normalized()
    v = d.cross(u).normalized()
    turn = rnd.uniform(0, math.tau)
    sides = rnd.choice((4, 5, 5, 6))
    planes = []
    for i in range(sides):
        a = turn + i / sides * math.tau + rnd.uniform(-0.25, 0.25)
        out = u * math.cos(a) + v * math.sin(a)
        spread = math.radians(rnd.uniform(30, 46))
        planes.append(out * math.cos(spread) - d * math.sin(spread))
    depth = min(r) * rnd.uniform(0.42, 0.55)
    skin = ball(c, r, SKIN, segs=32)
    flesh = ball(c, tuple(x * 0.93 for x in r), FLESH, segs=24)
    for n in planes:
        slice_off(skin, c, n)
        slice_off(flesh, c + n * 0.02, n)
    slice_off(skin, c + d * depth, -d)
    slice_off(flesh, c + d * (depth - 0.03), -d)
    # Gristle on the torn underside, and lumps on the hide.
    for _ in range(5):
        at = c + d * (depth - 0.02) + (u * rnd.uniform(-0.3, 0.3) + v * rnd.uniform(-0.3, 0.3))
        ball(at, rnd.uniform(0.05, 0.11), FLESH, segs=10)
    for _ in range(3):
        spot, n = on_ball(c, r, d + u * rnd.uniform(-0.35, 0.35) + v * rnd.uniform(-0.35, 0.35))
        ball(spot - n * 0.02, rnd.uniform(0.05, 0.09), SKIN, segs=10)
    if extras:
        extras()
    export(f"chunk{CHUNKS}")
    CHUNKS += 1


def loose_chunk(build):
    global CHUNKS
    clear()
    build()
    export(f"chunk{CHUNKS}")
    CHUNKS += 1





def head():
    clear()
    cranium_c, cranium_r = CRANIUM
    face_c, face_r = FACE
    mantle_c, mantle_r = MANTLE
    belly_c, belly_r = (0, 0.15, 1.02), (0.8, 0.8, 0.5)
    cut(ball(cranium_c, cranium_r, SKIN, segs=36), COLLAR.z)
    ball(face_c, face_r, SKIN, segs=32)
    ball(mantle_c, mantle_r, SKIN, segs=32)
    # A paler underside.
    ball(belly_c, belly_r, BELLY, segs=24)

    # The steel collar the dome seals onto, right round the widest part of the head, with bolts and a seal.
    torus(COLLAR, COLLAR_RADIUS + 0.02, 0.1, COLLAR_M)
    for i in range(14):
        a = i / 14 * math.tau
        ball(COLLAR + Vector((math.cos(a) * (COLLAR_RADIUS + 0.11), math.sin(a) * (COLLAR_RADIUS + 0.11), 0.0)), 0.05, COLLAR_M, segs=10)
    torus(COLLAR + Vector((0, 0, -0.15)), COLLAR_RADIUS + 0.02, 0.05, COLLAR_M)
    # The cut is capped dark, under the brain.
    ball(COLLAR + Vector((0, 0, -0.02)), (COLLAR_RADIUS - 0.06, COLLAR_RADIUS - 0.06, 0.05), MOUTH, segs=24)
    # An air tank on its back, with two hoses up to the collar.
    tank()

    # The big slanted alien eyes (the mouth is its own part, below them: mouth()).
    for side in (-1, 1):
        eye(side)

    # Glowing holes: a dark crater ring with green light inside.
    for spec in HOLES:
        crater(*spec)

    # Lumps on the back and sides of the head (same skin, just raised).
    for _ in range(22):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 0.1), random.uniform(-0.9, -0.1)))
        p, n = on_ball(cranium_c, cranium_r, d)
        ball(p - n * 0.02, random.uniform(0.05, 0.1), SKIN, segs=10)

    # Creases round the mantle, a siphon each side, and a skirt of webbing where the tentacles join.
    for dz in (0.22, -0.02):
        rr = math.sqrt(max(0.0, 1 - (dz / mantle_r[2]) ** 2))
        torus((mantle_c[0], mantle_c[1], mantle_c[2] + dz), mantle_r[0] * rr + 0.005, 0.035, LIP)
    for side in (-1, 1):
        siphon(side)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.86, minor_radius=0.24, major_segments=32, minor_segments=10, location=(0, 0, 0))
    skirt = bpy.context.active_object
    skirt.scale = (1, 1, 0.55)
    skirt.location = (0, 0.02, 1.02)
    bpy.ops.object.transform_apply(location=True, scale=True)
    finish(skirt, TENTACLE)
    export("head")


HOLES = [
    (CRANIUM[0], CRANIUM[1], (0.98, 0.3, -0.28), 0.12), (CRANIUM[0], CRANIUM[1], (-0.98, 0.2, -0.33), 0.1),
    (CRANIUM[0], CRANIUM[1], (0.6, -0.8, -0.22), 0.13), (CRANIUM[0], CRANIUM[1], (-0.5, -0.85, -0.38), 0.11),
    (CRANIUM[0], CRANIUM[1], (-0.95, -0.4, -0.6), 0.09), (CRANIUM[0], CRANIUM[1], (0.9, -0.3, -0.65), 0.1),
    (FACE[0], FACE[1], (-0.8, 0.55, -0.15), 0.1), (FACE[0], FACE[1], (0.85, 0.45, -0.3), 0.09),
    (MANTLE[0], MANTLE[1], (-0.9, 0.3, 0.45), 0.09), (MANTLE[0], MANTLE[1], (0.7, -0.6, 0.5), 0.1),
    (MANTLE[0], MANTLE[1], (-0.5, -0.8, 0.5), 0.1), (MANTLE[0], MANTLE[1], (0.9, 0.35, 0.4), 0.08),
]


def tank():
    ball((0, -1.5, 2.05), (0.3, 0.22, 0.46), COLLAR_M, segs=18)
    ball((0, -1.5, 2.52), (0.12, 0.12, 0.1), COLLAR_M, segs=12)
    for side in (-1, 1):
        cone((side * 0.2, -1.46, 2.42), (side * 0.55, -1.32, 2.62), 0.06, 0.06, LIP, verts=10)
        cone((side * 0.55, -1.32, 2.62), (side * 0.6, -1.12, 2.76), 0.06, 0.06, LIP, verts=10)
        ball((side * 0.55, -1.32, 2.62), 0.062, LIP, segs=10)


def siphon(side):
    p, n = on_ball(MANTLE[0], MANTLE[1], (side, -0.25, 0.55))
    cone(p - n * 0.1, p + n * 0.34, 0.15, 0.2, SKIN, verts=14)
    torus(p + n * 0.34, 0.2, 0.045, LIP, rot=facing(n))
    ball(p + n * 0.3, (0.17, 0.17, 0.17), MOUTH, segs=12)


def mouth():
    """The mouth on its own, opening along +Y from the origin, so the game can open and close it."""
    clear()
    mouth_parts((0, 0, 0), (0, 1, 0), scale=1.15)
    # A fleshy rim behind the lip, so it reads as pushing out of the face.
    torus((0, -0.1, 0), 0.5, 0.16, SKIN, rot=facing(Vector((0, 1, 0))))
    export("mouth")


def chunks():
    """What the head bursts into. Each is exported in the head's own space, so the game starts it where it was."""
    for side in (-1, 1):
        shell_chunk(CRANIUM, (side * 0.75, 0.6, -0.42), lambda side=side: eye(side), seed=10 + side)
        shell_chunk(CRANIUM, (side * 0.8, -0.55, -0.45), lambda side=side: [crater(*h) for h in HOLES[2:6] if h[2][0] * side > 0], seed=20 + side)
        shell_chunk(FACE, (side * 1.0, 0.1, -0.1), lambda side=side: [crater(*h) for h in HOLES[6:8] if h[2][0] * side > 0], seed=30 + side)
        shell_chunk(MANTLE, (side * 0.8, 0.5, -0.1), lambda side=side: siphon(side), seed=40 + side)
    shell_chunk(CRANIUM, (0, -1, -0.3), tank, seed=50)
    shell_chunk(FACE, (0, 1, -0.3), lambda: mouth_parts(on_ball(FACE[0], FACE[1], (0, 1, -0.55))[0], (0, 0.85, -0.5), scale=0.8), seed=51)
    shell_chunk(MANTLE, (0, -1, 0.1), lambda: [crater(*h) for h in HOLES[9:11]], seed=52)

    # The collar in three bent arcs, bolts and all.
    for i in range(3):
        def arc(i=i):
            ring = torus(COLLAR, COLLAR_RADIUS + 0.02, 0.1, COLLAR_M)
            seal = torus(COLLAR + Vector((0, 0, -0.15)), COLLAR_RADIUS + 0.02, 0.05, COLLAR_M)
            a0, a1 = i / 3 * math.tau, (i + 1) / 3 * math.tau - 0.12
            for o in (ring, seal):
                slice_off(o, COLLAR, Vector((math.sin(a0), -math.cos(a0), 0)))
                slice_off(o, COLLAR, Vector((-math.sin(a1), math.cos(a1), 0)))
            for k in range(14):
                a = k / 14 * math.tau
                if a0 + 0.1 < a < a1 - 0.1:
                    ball(COLLAR + Vector((math.cos(a) * (COLLAR_RADIUS + 0.11), math.sin(a) * (COLLAR_RADIUS + 0.11), 0.0)), 0.05, COLLAR_M, segs=10)
        loose_chunk(arc)

    # The brain in two halves, torn down the middle.
    for side in (-1, 1):
        def half(side=side):
            k = 1.55
            lobe = ball(COLLAR + Vector((side * 0.26, 0, 0.16)) * k, (0.33 * k, 0.52 * k, 0.36 * k), BRAIN, segs=24)
            slice_off(lobe, COLLAR, Vector((-side, 0, 0)))
            for i in range(7):
                y = -0.4 + i * 0.135
                x = side * (0.3 + 0.07 * math.sin(i * 1.7))
                z = 0.16 + 0.345 * math.sqrt(max(0.0, 1 - (y / 0.52) ** 2))
                torus(COLLAR + Vector((x, y, z - 0.07)) * k, 0.14 * k, 0.035 * k, FOLD, rot=(90 + random.uniform(-25, 25), 0, random.uniform(-35, 35)))
            ball(COLLAR + Vector((side * 0.08, 0, 0.2)) * k, (0.06, 0.6, 0.4), FLESH, segs=14)
        loose_chunk(half)
    print("chunks", CHUNKS)


def brain():
    clear()
    k = 1.55     # sized up to fill the dome
    # Two lobes with a groove between, and darker folds laid over them.
    for side in (-1, 1):
        ball(COLLAR + Vector((side * 0.26, 0, 0.16)) * k, (0.33 * k, 0.52 * k, 0.36 * k), BRAIN, segs=24)
        for i in range(7):
            y = -0.4 + i * 0.135
            x = side * (0.28 + 0.07 * math.sin(i * 1.7))
            z = 0.16 + 0.345 * math.sqrt(max(0.0, 1 - (y / 0.52) ** 2))
            torus(COLLAR + Vector((x, y, z - 0.07)) * k, 0.14 * k, 0.035 * k, FOLD, rot=(90 + random.uniform(-25, 25), 0, random.uniform(-35, 35)))
    ball(COLLAR + Vector((0, -0.42, 0.06)) * k, (0.22 * k, 0.18 * k, 0.16 * k), BRAIN, segs=16)    # the stem at the back
    for o in list(bpy.context.scene.objects):
        cut(o, COLLAR.z + 0.01, keep_below=False)
    export("brain", COLLAR)


def helmet():
    clear()
    dome = ball(COLLAR + Vector((0, 0, 0.1)), (COLLAR_RADIUS + 0.03, COLLAR_RADIUS + 0.03, 1.22), GLASS, segs=40)
    cut(dome, COLLAR.z + 0.04, keep_below=False)
    export("helmet", COLLAR)


def segment(i):
    clear()
    r0 = ROOT_RADIUS * TAPER ** i
    r1 = r0 * TAPER
    cone((0, 0, 0), (0, SEG_LENGTH, 0), r0, r1, TENTACLE, verts=18)
    ball((0, 0, 0), r0, TENTACLE, segs=18)
    if i == SEGMENTS - 1:
        ball((0, SEG_LENGTH, 0), r1, TENTACLE, segs=14)
    # Two rows of glowing suckers underneath.
    count = 4
    for k in range(count):
        t = (k + 0.5) / count
        r = r0 + (r1 - r0) * t
        for side in (-1, 1):
            a = math.radians(side * 24)
            n = Vector((math.sin(a), 0, -math.cos(a)))
            p = Vector((0, SEG_LENGTH * t, 0)) + n * r * 0.97
            s = r * 0.3
            torus(p, s, s * 0.38, SUCKER, rot=facing(n))
    export(f"seg{i}")


head()
mouth()
chunks()
brain()
helmet()
for i in range(SEGMENTS):
    segment(i)
