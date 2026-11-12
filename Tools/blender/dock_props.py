"""
Dock props for the water level (Risk of Copyright, Godot): a floating barrel, the pier lamp, a rope coil and the radio.
Run in Blender:  exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/dock_props.py").read())
Exports godot/assets/models/dock/{barrel,lamp,rope_coil,radio}.glb (set DOCK_ONLY=radio to write just one) (metres, origin at the centre for the barrel,
at the bottom for the lamp and the coil). The lamp's glass is the material "LampGlow" (made emissive in code).
"""
import bpy
import math
import os

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/dock"
COLL = "DockProps"


def mat(name, color, metal=0.0, rough=0.7):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    return m


def collection():
    if COLL in bpy.data.collections:
        c = bpy.data.collections[COLL]
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        c = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(c)
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[COLL]
    return c


def done(o, material, bevel=0.0, smooth=True):
    if bevel:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = 2
        m.limit_method = "ANGLE"
    o.data.materials.append(material)
    if smooth:
        bpy.ops.object.shade_smooth_by_angle()
    return o


def join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
        bpy.context.view_layer.objects.active = p
        for m in list(p.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    return o


def barrel():
    """A bellied wooden barrel with iron hoops, about 66 x 86 cm (the old box was 26 x 34 inches)."""
    wood = mat("BarrelWood", (0.55, 0.36, 0.2), 0.0, 0.75)
    iron = mat("BarrelIron", (0.2, 0.2, 0.22), 0.8, 0.45)
    h, r = 0.86, 0.3
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=h, location=(0, 0, 0))
    body = bpy.context.active_object
    # Rings up the side, so the belly can bulge.
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(body.data)
    sides = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > h * 0.5]
    bmesh.ops.subdivide_edges(bm, edges=sides, cuts=9, use_grid_fill=True)
    bm.to_mesh(body.data)
    bm.free()
    # Belly: push the middle rings outward.
    for v in body.data.vertices:
        t = v.co.z / (h / 2)
        k = 1.0 + 0.14 * (1 - t * t)
        v.co.x *= k
        v.co.y *= k
    m = body.modifiers.new("Bevel", "BEVEL")
    m.width = 0.02
    m.segments = 2
    body.data.materials.append(wood)
    bpy.ops.object.shade_smooth()
    parts = [body]
    for z in (-0.36, -0.2, 0.2, 0.36):
        t = z / (h / 2)
        rr = r * (1.0 + 0.14 * (1 - t * t)) + 0.006
        bpy.ops.mesh.primitive_torus_add(major_radius=rr, minor_radius=0.012, major_segments=32, minor_segments=8, location=(0, 0, z))
        parts.append(done(bpy.context.active_object, iron))
    # Lid: a slightly inset disc with a bung.
    for s in (-1, 1):
        bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r * 0.92, depth=0.02, location=(0, 0, s * (h / 2 - 0.01)))
        parts.append(done(bpy.context.active_object, iron, 0.004))
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.04, depth=0.03, location=(0.12, 0, h / 2 + 0.005))
    parts.append(done(bpy.context.active_object, wood, 0.004))
    return join(parts, "Barrel")


def lamp():
    """The pier lamp: a lantern on an arm, sized for the 80 inch post's top (origin at its base)."""
    dark = mat("LampMetal", (0.12, 0.12, 0.13), 0.8, 0.4)
    glass = mat("LampGlow", (1.0, 0.85, 0.5), 0.0, 0.2)
    parts = []
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.02))
    b = bpy.context.active_object
    b.scale = (0.2, 0.2, 0.04)
    bpy.ops.object.transform_apply(scale=True)
    parts.append(done(b, dark, 0.006))
    # Glass body with four corner bars.
    bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.11, depth=0.22, location=(0, 0, 0.15), rotation=(0, 0, math.pi / 4))
    parts.append(done(bpy.context.active_object, glass, 0.004, smooth=False))
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.012, depth=0.24, location=(math.cos(a) * 0.08, math.sin(a) * 0.08, 0.15))
        parts.append(done(bpy.context.active_object, dark))
    # Roof: a little pyramid with a ring on top.
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.16, radius2=0.02, depth=0.1, location=(0, 0, 0.31), rotation=(0, 0, math.pi / 4))
    parts.append(done(bpy.context.active_object, dark, 0.006, smooth=False))
    bpy.ops.mesh.primitive_torus_add(major_radius=0.035, minor_radius=0.008, location=(0, 0, 0.385), rotation=(math.pi / 2, 0, 0))
    parts.append(done(bpy.context.active_object, dark))
    return join(parts, "Lamp")


def rope_coil():
    """A coil of rope lying on the deck (origin at its base): stacked rings and a loose end."""
    rope = mat("Rope", (0.72, 0.6, 0.4), 0.0, 0.9)
    parts = []
    for i, (r, z) in enumerate(((0.21, 0.03), (0.17, 0.03), (0.13, 0.03), (0.19, 0.075), (0.15, 0.075))):
        bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.025, major_segments=36, minor_segments=10, location=(0, 0, z))
        t = bpy.context.active_object
        t.rotation_euler = (0.04 * (i % 2), 0.03 * ((i + 1) % 2), i)
        parts.append(done(t, rope))
    # The loose end trailing off.
    pts = [(0.21, 0.0, 0.03), (0.3, -0.06, 0.025), (0.4, -0.04, 0.025), (0.48, 0.03, 0.025)]
    curve = bpy.data.curves.new("RopeEnd", "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = 0.024
    curve.bevel_resolution = 3
    spline = curve.splines.new("NURBS")
    spline.points.add(len(pts) - 1)
    for p, c in zip(spline.points, pts):
        p.co = (*c, 1)
    spline.use_endpoint_u = True
    end = bpy.data.objects.new("Rope End", curve)
    bpy.context.collection.objects.link(end)
    bpy.context.view_layer.objects.active = end
    end.select_set(True)
    bpy.ops.object.convert(target="MESH")
    parts.append(done(bpy.context.active_object, rope))
    return join(parts, "Rope Coil")


def radio():
    """
    An old-timey tabletop radio (origin at the middle of its base, the front toward -Y): a wooden cabinet with an
    arched top, a cloth speaker with cream slats, a dial with a glowing strip, two knobs and little feet. The strip is
    the material "RadioGlow" (made emissive in code and lit while it plays).
    """
    wood = mat("RadioWood", (0.3, 0.14, 0.07), 0.0, 0.55)
    cloth = mat("RadioCloth", (0.16, 0.1, 0.07), 0.0, 0.9)
    cream = mat("RadioTrim", (0.9, 0.82, 0.62), 0.0, 0.5)
    brass = mat("RadioBrass", (0.78, 0.6, 0.24), 0.7, 0.35)
    knob = mat("RadioKnob", (0.13, 0.08, 0.05), 0.0, 0.4)
    glow = mat("RadioGlow", (1.0, 0.72, 0.28), 0.0, 0.3)
    w, d, h, b = 0.36, 0.17, 0.17, 0.012
    front = -d / 2
    parts = []

    def box(size, at, material, bevel=0.0):
        bpy.ops.mesh.primitive_cube_add(size=1, location=at)
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        parts.append(done(o, material, bevel, smooth=False))

    def cyl(radius, depth, at, material, axis_y=True, bevel=0.0, verts=32):
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=at, rotation=(math.pi / 2, 0, 0) if axis_y else (0, 0, 0))
        parts.append(done(bpy.context.active_object, material, bevel))

    box((w, d, h), (0, 0, b + h / 2), wood, 0.012)
    cyl(w / 2, d, (0, 0, b + h), wood, bevel=0.008)
    # Speaker: cloth under an arch, cream slats across it, a brass rim.
    box((0.25, 0.012, 0.115), (0, front - 0.002, b + 0.115), cloth)
    cyl(0.125, 0.012, (0, front - 0.002, b + 0.172), cloth)
    for i in range(-2, 3):
        box((0.008, 0.01, 0.17), (i * 0.045, front - 0.008, b + 0.15), cream)
    cyl(0.135, 0.008, (0, front + 0.003, b + 0.172), brass, verts=32)
    # Dial and its glowing strip, two knobs.
    box((0.24, 0.01, 0.034), (0, front - 0.004, b + 0.03), cream, 0.004)
    box((0.19, 0.006, 0.008), (0, front - 0.011, b + 0.03), glow)
    for x in (-0.145, 0.145):
        cyl(0.024, 0.022, (x, front - 0.011, b + 0.03), knob, bevel=0.004, verts=20)
        cyl(0.012, 0.006, (x, front - 0.024, b + 0.03), brass, verts=16)
    # Feet.
    for x in (-0.15, 0.15):
        for y in (-0.06, 0.06):
            cyl(0.016, b, (x, y, b / 2), brass, axis_y=False, verts=12)
    return join(parts, "Radio")


def export(obj, name):
    os.makedirs(OUT, exist_ok=True)
    loc = obj.location.copy()
    obj.location = (0, 0, 0)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name), use_selection=True, export_format="GLB", export_yup=True, export_apply=True)
    obj.location = loc


def build():
    collection()
    makers = [(barrel, "barrel.glb", (-0.8, 0, 0.43)), (lamp, "lamp.glb", (0, 0, 0)), (rope_coil, "rope_coil.glb", (0.8, 0, 0)), (radio, "radio.glb", (1.6, 0, 0))]
    only = os.environ.get("DOCK_ONLY")
    names = []
    for make, name, at in makers:
        if only and not name.startswith(only):
            continue
        obj = make()
        export(obj, name)
        obj.location = at
        names.append(name)
    return names


if __name__ == "__main__":
    build()
