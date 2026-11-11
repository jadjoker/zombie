"""
The Kraken (Risk of Copyright, Godot, issue #30): a bulb head held up off the sea bed, its brain showing under a
spacesuit glass dome with a steel collar, hoses and an air tank; big slanted black eyes under brow ridges, glowing
green holes, siphons, a round toothy mouth underneath facing down and a little forward, and eight sucker tentacles.
Run headless:  blender -b --factory-startup --python Tools/blender/kraken.py
Writes godot/assets/models/creatures/kraken/<part>.glb: metres, Y up, the mouth facing Godot forward (-Z).
  head.glb     origin on the ground under the head
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
COLLAR = Vector((0.0, -0.25, 3.55))
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


def head():
    clear()
    cranium_c, cranium_r = (0, -0.25, 2.7), (1.1, 1.15, 1.1)
    face_c, face_r = (0, 0.02, 1.9), (0.9, 0.92, 0.95)
    mantle_c, mantle_r = (0, 0.1, 1.2), (1.0, 1.0, 0.6)
    belly_c, belly_r = (0, 0.15, 1.02), (0.8, 0.8, 0.5)
    cut(ball(cranium_c, cranium_r, SKIN, segs=36), COLLAR.z)
    ball(face_c, face_r, SKIN, segs=32)
    ball(mantle_c, mantle_r, SKIN, segs=32)
    # A paler underside, where the mouth is.
    ball(belly_c, belly_r, BELLY, segs=24)

    # The steel collar the dome seals onto, with bolts, and a ribbed seal under it.
    torus(COLLAR, 0.74, 0.1, COLLAR_M)
    for i in range(10):
        a = i / 10 * math.tau
        ball(COLLAR + Vector((math.cos(a) * 0.83, math.sin(a) * 0.83, 0.0)), 0.045, COLLAR_M, segs=10)
    torus(COLLAR + Vector((0, 0, -0.15)), 0.88, 0.05, COLLAR_M)
    torus(COLLAR + Vector((0, 0, -0.3)), 0.97, 0.045, LIP)
    # The cut is capped dark, under the brain.
    ball(COLLAR + Vector((0, 0, -0.02)), (0.68, 0.68, 0.05), MOUTH, segs=24)
    # An air tank on its back, with two hoses up to the collar.
    ball((0, -1.38, 2.55), (0.3, 0.22, 0.46), COLLAR_M, segs=18)
    ball((0, -1.38, 3.02), (0.12, 0.12, 0.1), COLLAR_M, segs=12)
    for side in (-1, 1):
        cone((side * 0.2, -1.34, 2.95), (side * 0.5, -1.05, 3.4), 0.06, 0.06, LIP, verts=10)
        cone((side * 0.5, -1.05, 3.4), (side * 0.52, -0.78, 3.52), 0.06, 0.06, LIP, verts=10)
        ball((side * 0.5, -1.05, 3.4), 0.062, LIP, segs=10)

    # Big slanted alien eyes with a thin glowing rim, under heavy brow ridges.
    for side in (-1, 1):
        p, n = on_ball(cranium_c, cranium_r, (side * 0.47, 1.0, -0.14))
        yaw = math.degrees(math.atan2(n.x, n.y))
        rot = (4, side * 38, -yaw)
        ball(p - n * 0.02, (0.25, 0.07, 0.54), GLOW, rot=rot)
        ball(p + n * 0.014, (0.215, 0.078, 0.5), EYE, rot=rot)
        ball(p + n * 0.05 + Vector((side * 0.02, 0, 0.5)), (0.38, 0.1, 0.085), LIP, rot=(0, side * 24, -yaw))

    # Glowing holes: a dark crater ring with green light inside.
    holes = [
        (cranium_c, cranium_r, (-0.55, 0.6, 0.62), 0.11), (cranium_c, cranium_r, (0.5, 0.7, 0.66), 0.1),
        (cranium_c, cranium_r, (0.98, 0.3, 0.1), 0.12), (cranium_c, cranium_r, (-0.98, 0.2, -0.05), 0.1),
        (cranium_c, cranium_r, (0.6, -0.8, 0.3), 0.13), (cranium_c, cranium_r, (-0.5, -0.85, 0.1), 0.11),
        (cranium_c, cranium_r, (0.0, 1.0, 0.5), 0.085),
        (face_c, face_r, (-0.8, 0.55, -0.15), 0.1), (face_c, face_r, (0.85, 0.45, -0.3), 0.09),
        (face_c, face_r, (0.0, 1.0, -0.35), 0.08), (face_c, face_r, (-0.3, 1.0, -0.6), 0.06),
        (face_c, face_r, (0.35, 1.0, -0.62), 0.065),
        (mantle_c, mantle_r, (-0.9, 0.3, 0.45), 0.09), (mantle_c, mantle_r, (0.7, -0.6, 0.5), 0.1),
        (mantle_c, mantle_r, (-0.5, -0.8, 0.5), 0.1), (mantle_c, mantle_r, (0.9, 0.35, 0.4), 0.08),
    ]
    for c, r, d, size in holes:
        p, n = on_ball(c, r, d)
        torus(p + n * 0.005, size, size * 0.3, LIP, rot=facing(n))
        ball(p - n * size * 0.35, size * 0.95, GLOW, segs=14)

    # Lumps on the back and sides of the head (same skin, just raised).
    for _ in range(18):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 0.15), random.uniform(-0.5, 0.75)))
        p, n = on_ball(cranium_c, cranium_r, d)
        if p.z > COLLAR.z - 0.4:
            continue
        ball(p - n * 0.02, random.uniform(0.05, 0.1), SKIN, segs=10)

    # Creases round the mantle, a siphon each side, and a skirt of webbing where the tentacles join.
    for dz in (0.22, -0.02):
        rr = math.sqrt(max(0.0, 1 - (dz / mantle_r[2]) ** 2))
        torus((mantle_c[0], mantle_c[1], mantle_c[2] + dz), mantle_r[0] * rr + 0.005, 0.035, LIP)
    for side in (-1, 1):
        p, n = on_ball(mantle_c, mantle_r, (side, -0.25, 0.55))
        cone(p - n * 0.1, p + n * 0.34, 0.15, 0.2, SKIN, verts=14)
        torus(p + n * 0.34, 0.2, 0.045, LIP, rot=facing(n))
        ball(p + n * 0.3, (0.17, 0.17, 0.17), MOUTH, segs=12)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.86, minor_radius=0.24, major_segments=32, minor_segments=10, location=(0, 0, 0))
    skirt = bpy.context.active_object
    skirt.scale = (1, 1, 0.55)
    skirt.location = (0, 0.02, 1.02)
    bpy.ops.object.transform_apply(location=True, scale=True)
    finish(skirt, TENTACLE)

    # The mouth: underneath, facing down and a little forward. A round lip, dark inside, a ring of teeth.
    tilt = math.radians(28)
    normal = Vector((0, math.sin(tilt), -math.cos(tilt)))     # out of the mouth
    centre, _ = on_ball(belly_c, belly_r, normal)
    centre = centre + normal * 0.02
    across = Vector((1, 0, 0))
    up = normal.cross(across).normalized()
    torus(centre, 0.4, 0.12, LIP, rot=facing(normal))
    ball(centre - normal * 0.14, (0.38, 0.38, 0.38), MOUTH, segs=20)
    for i in range(12):
        a = i / 12 * math.tau
        rim = centre + (across * math.cos(a) + up * math.sin(a)) * 0.38 + normal * 0.04
        tip = centre + (across * math.cos(a) + up * math.sin(a)) * 0.15 + normal * 0.12
        cone(rim, tip, 0.075, 0.008, TOOTH, verts=8)
    export("head")


def brain():
    clear()
    # Two lobes with a groove between, and darker folds laid over them.
    for side in (-1, 1):
        ball(COLLAR + Vector((side * 0.26, 0, 0.16)), (0.33, 0.52, 0.36), BRAIN, segs=24)
        for k in range(7):
            y = -0.4 + k * 0.135
            x = side * (0.28 + 0.07 * math.sin(k * 1.7))
            z = 0.16 + 0.345 * math.sqrt(max(0.0, 1 - (y / 0.52) ** 2))
            torus(COLLAR + Vector((x, y, z - 0.07)), 0.14, 0.035, FOLD, rot=(90 + random.uniform(-25, 25), 0, random.uniform(-35, 35)))
    ball(COLLAR + Vector((0, -0.42, 0.06)), (0.22, 0.18, 0.16), BRAIN, segs=16)    # the stem at the back
    for o in list(bpy.context.scene.objects):
        cut(o, COLLAR.z + 0.01, keep_below=False)
    export("brain", COLLAR)


def helmet():
    clear()
    dome = ball(COLLAR + Vector((0, 0, 0.12)), (0.76, 0.76, 0.86), GLASS, segs=36)
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
brain()
helmet()
for i in range(SEGMENTS):
    segment(i)
