"""
Sea floor props (Risk of Copyright, Godot): anemone, starfish, shell, urchin, a small fish, a big ship's anchor with
its chain and a sunken rowboat. Simple rounded shapes in flat colours (the game paints them and adds its outline).
Run headless:  blender -b --factory-startup --python Tools/blender/sea_props.py
Writes godot/assets/models/sea/<name>.glb: metres, Y up, origin on the sea bed under the prop's middle. The game
recolours by material name (see godot/Code/Game/SeaDressing.cs).
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector

OUT = os.environ.get("SEA_OUT", r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/sea")


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for coll in (bpy.data.meshes, bpy.data.materials):
        for d in list(coll):
            coll.remove(d)


def mat(name, color, rough=0.7, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def finish(o, material, smooth=True):
    o.data.materials.clear()
    o.data.materials.append(material)
    if smooth:
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.shade_smooth()
    return o


def ball(center, radii, material, segs=20):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=max(6, segs // 2), radius=1, location=center)
    o = bpy.context.active_object
    o.scale = radii if isinstance(radii, (tuple, list)) else (radii, radii, radii)
    bpy.ops.object.transform_apply(scale=True)
    return finish(o, material)


def tube(a, b, r0, r1, material, verts=12):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bpy.ops.object.transform_apply(rotation=True)
    return finish(o, material)


def box(center, size, material, bevel=0.02, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    o = bpy.context.active_object
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = min(bevel, min(size) * 0.4)
        m.segments = 2
        bpy.ops.object.modifier_apply(modifier="Bevel")
    o.rotation_euler = tuple(math.radians(a) for a in rot)
    o.location = center
    bpy.ops.object.transform_apply(location=True, rotation=True)
    return finish(o, material, smooth=False)


def torus(center, major, minor, material, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=20, minor_segments=8, location=center, rotation=tuple(math.radians(a) for a in rot))
    return finish(bpy.context.active_object, material)


def export(name):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), use_selection=True, export_format="GLB", export_yup=True, export_apply=True)
    print("exported", name, len(o.data.polygons), "polys")


def anemone():
    clear()
    base = mat("AnemoneBase", (0.45, 0.25, 0.5))
    arm = mat("AnemoneArm", (1.0, 0.45, 0.55), 0.5)
    tube((0, 0, 0), (0, 0, 0.2), 0.2, 0.15, base, 16)
    ball((0, 0, 0.2), (0.16, 0.16, 0.06), base, 16)
    for ring, count, lean, length in ((0.12, 12, 38, 0.3), (0.06, 7, 16, 0.26)):
        for k in range(count):
            a = k * math.tau / count + ring * 9
            foot = Vector((math.cos(a) * ring, math.sin(a) * ring, 0.22))
            out = Vector((math.cos(a), math.sin(a), 0)) * math.sin(math.radians(lean)) + Vector((0, 0, math.cos(math.radians(lean))))
            tip = foot + out * length
            tube(foot, tip, 0.032, 0.02, arm, 8)
            ball(tip, 0.027, arm, 8)
    export("anemone")


def starfish():
    clear()
    skin = mat("Starfish", (0.95, 0.45, 0.2))
    dot = mat("StarfishDot", (1.0, 0.8, 0.55))
    ball((0, 0, 0.035), (0.1, 0.1, 0.045), skin, 16)
    for k in range(5):
        a = k * math.tau / 5
        d = Vector((math.cos(a), math.sin(a), 0))
        tube(d * 0.05 + Vector((0, 0, 0.035)), d * 0.3 + Vector((0, 0, 0.02)), 0.062, 0.018, skin, 10)
        ball(d * 0.3 + Vector((0, 0, 0.02)), 0.018, skin, 8)
        for t in (0.11, 0.17, 0.23):
            ball(d * t + Vector((0, 0, 0.075 - t * 0.12)), 0.014, dot, 6)
    ball((0, 0, 0.078), 0.02, dot, 8)
    # Flatten the underside onto the sand.
    for o in bpy.context.scene.objects:
        for v in o.data.vertices:
            if v.co.z < 0.004:
                v.co.z = 0.004
    export("starfish")


def shell():
    clear()
    body = mat("Shell", (0.95, 0.82, 0.7), 0.45)
    lip = mat("ShellLip", (1.0, 0.6, 0.6), 0.4)
    # A conch: whorls shrinking along a spiral to a point, a flared pink lip at the opening.
    pos = Vector((0, 0, 0.09))
    r = 0.1
    for k in range(6):
        a = k * 1.15
        ball(pos, (r, r, r * 0.9), body, 14)
        pos = pos + Vector((math.cos(a) * r * 0.55, math.sin(a) * r * 0.55, r * 0.5)) * 0.9 + Vector((-r * 0.75, 0, 0))
        r *= 0.72
    tube(pos - Vector((0.01, 0, 0.01)), pos + Vector((-0.05, 0, 0.03)), r * 1.1, 0.004, body, 8)
    ball((0.07, -0.05, 0.07), (0.07, 0.03, 0.085), lip, 12)
    export("shell")


def urchin():
    clear()
    body = mat("Urchin", (0.16, 0.08, 0.22), 0.5)
    spine = mat("UrchinSpine", (0.3, 0.16, 0.4), 0.5)
    ball((0, 0, 0.1), (0.12, 0.12, 0.1), body, 16)
    n = 46
    for i in range(n):
        z = 1 - (i + 0.5) / n * 1.35                     # mostly the top, a few just under the middle
        rad = math.sqrt(max(0.0, 1 - z * z))
        a = i * math.pi * (3 - math.sqrt(5))
        d = Vector((math.cos(a) * rad, math.sin(a) * rad, z))
        foot = Vector((0, 0, 0.1)) + Vector((d.x * 0.11, d.y * 0.11, d.z * 0.09))
        tube(foot, foot + d * 0.17, 0.012, 0.002, spine, 5)
    export("urchin")


def fish():
    clear()
    skin = mat("Fish", (1.0, 0.6, 0.15), 0.45)
    fin = mat("FishFin", (1.0, 0.85, 0.4), 0.45)
    eye = mat("FishEye", (0.03, 0.03, 0.04), 0.2)
    # Nose toward -Y (which exports as the game's forward).
    ball((0, 0, 0), (0.035, 0.13, 0.07), skin, 16)
    tube((0, 0.1, 0), (0, 0.2, 0), 0.02, 0.075, fin, 4)                  # tail, a flat wedge
    tube((0, -0.02, 0.05), (0, 0.05, 0.11), 0.045, 0.01, fin, 4)          # dorsal fin
    for o in bpy.context.scene.objects:
        if o.data.materials[0] is fin:
            for v in o.data.vertices:
                v.co.x *= 0.18
    for sx in (-1, 1):
        ball((sx * 0.027, -0.085, 0.018), 0.012, eye, 8)
    export("fish")


def anchor():
    clear()
    iron = mat("AnchorIron", (0.2, 0.15, 0.13), 0.75, 0.3)
    rust = mat("AnchorRust", (0.42, 0.22, 0.12), 0.85, 0.1)
    # Standing upright here, crown at the bottom; the game lays it over and half buries it.
    tube((0, 0, 0.25), (0, 0, 2.6), 0.1, 0.085, iron, 14)                # shank
    torus((0, 0, 2.78), 0.2, 0.05, iron, rot=(90, 0, 0))                  # ring
    tube((-0.75, 0, 2.35), (0.75, 0, 2.35), 0.07, 0.07, rust, 12)         # stock
    for sx in (-1, 1):
        ball((sx * 0.75, 0, 2.35), 0.1, rust, 10)
        tube((0, 0, 0.2), (sx * 0.85, 0, 0.75), 0.13, 0.09, iron, 12)     # arm
        box((sx * 0.98, 0, 0.9), (0.42, 0.07, 0.5), rust, 0.03, rot=(0, sx * -32, 0))   # fluke
    ball((0, 0, 0.2), (0.17, 0.14, 0.17), iron, 12)                       # crown
    # Chain carrying on from the ring in line with the shank (it lies along the sand once the anchor is laid over).
    for k in range(9):
        z = 3.02 + k * 0.27
        torus((0.1 * math.sin(k * 0.9), 0, z), 0.1, 0.03, rust, rot=(90, 0, 0 if k % 2 == 0 else 90))
    export("anchor")


def rowboat():
    clear()
    wood = mat("WreckWood", (0.3, 0.2, 0.12), 0.85)
    dark = mat("WreckDark", (0.14, 0.1, 0.07), 0.9)
    moss = mat("WreckMoss", (0.2, 0.36, 0.2), 0.9)
    # The hull: the lower half of a stretched ball, hollowed into a shell, pointed toward the bow.
    bpy.ops.mesh.primitive_uv_sphere_add(segments=28, ring_count=14, radius=1)
    hull = bpy.context.active_object
    bm = bmesh.new()
    bm.from_mesh(hull.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(0, 0, 1), clear_outer=True)
    for v in bm.verts:
        x, y, z = v.co
        taper = 1.0 - 0.55 * max(0.0, x) ** 2                              # the bow narrows, the stern stays full
        v.co = (x * 2.5, y * 0.95 * taper, z * 0.78 + 0.78)
    bm.to_mesh(hull.data)
    bm.free()
    s = hull.modifiers.new("Solid", "SOLIDIFY")
    s.thickness = 0.07
    s.offset = -1
    bpy.ops.object.modifier_apply(modifier="Solid")
    finish(hull, wood)
    # Benches, a keel, the rim, and a snapped mast.
    for x in (-1.2, 0.0, 1.1):
        w = 1.7 * (1.0 - 0.55 * max(0.0, x / 2.5) ** 2)
        box((x, 0, 0.56), (0.3, w, 0.05), dark, 0.015)
    box((0, 0, 0.03), (4.4, 0.09, 0.1), dark, 0.02)
    for sy in (-1, 1):
        for x0, x1 in ((-2.2, -1.1), (-1.1, 0.0), (0.0, 1.1), (1.1, 2.0)):
            def rim(x):
                t = 1.0 - 0.55 * max(0.0, x / 2.5) ** 2
                edge = math.sqrt(max(0.0, 1 - (x / 2.5) ** 2))
                return (x, sy * 0.95 * t * edge, 0.79)
            tube(rim(x0), rim(x1), 0.05, 0.05, dark, 8)
    tube((0.5, 0, 0.1), (0.75, 0.25, 1.5), 0.085, 0.07, wood, 10)
    tube((0.75, 0.25, 1.5), (0.8, 0.3, 1.62), 0.07, 0.02, dark, 6)         # the splintered break
    for p in ((-1.6, 0.5, 0.74), (0.4, -0.75, 0.76), (1.5, 0.3, 0.72), (-0.5, 0.82, 0.77)):
        ball(p, (0.26, 0.14, 0.05), moss, 10)
    export("rowboat")


if __name__ == "__main__":
    for make in (anemone, starfish, shell, urchin, fish, anchor, rowboat):
        make()
