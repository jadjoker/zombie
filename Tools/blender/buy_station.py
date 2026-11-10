"""
Buy station (Risk of Copyright, Godot): a supply terminal that replaces the coloured box (its collider stays).
Run in Blender:  exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/buy_station.py").read())
Exports godot/assets/models/props/buy_station.glb: metres, centred like the 1.5 x 1.5 x 1.0 m box it replaces.
Materials the game recolours per station: StationPaint (the body), StationGlow (screens and light ring) and
the hologram icons above it (IconAmmo, IconHealth, IconDamage: the game shows the one for its kind and spins it).
"""
import bpy
import bmesh
import math
import os

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/props/buy_station.glb"
COLL = "BuyStation"


def mat(name, color, metal=0.0, rough=0.5, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m


parts = []


def box(name, size, loc, material, bevel=0.012, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = 2
        m.limit_method = "ANGLE"
    o.data.materials.append(material)
    parts.append(o)
    return o


def cyl(name, r, depth, loc, material, verts=32, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    parts.append(o)
    return o


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        for m in list(o.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    return o


def extrude_profile(name, pts, depth, material, at=(0, 0, 0)):
    """A flat 2D outline (x/z) extruded along y: the hologram icons."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = [bm.verts.new((x, 0, z)) for x, z in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]:
        v.co.y += depth
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, -depth / 2, 0))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.location = at
    o.data.materials.append(material)
    return o


def build():
    global parts
    if COLL in bpy.data.collections:
        for o in list(bpy.data.collections[COLL].objects):
            bpy.data.objects.remove(o, do_unlink=True)
        coll = bpy.data.collections[COLL]
    else:
        coll = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(coll)
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[COLL]

    paint = mat("StationPaint", (0.75, 0.6, 0.2), 0.4, 0.45)
    steel = mat("StationSteel", (0.14, 0.15, 0.17), 0.85, 0.35)
    glass = mat("StationGlass", (0.03, 0.05, 0.07), 0.2, 0.08)
    glow = mat("StationGlow", (1.0, 0.8, 0.3), 0.0, 0.3, emit=3.0)
    holo = mat("StationHolo", (1.0, 0.85, 0.35), 0.0, 0.3, emit=4.0)

    parts = []
    # Plinth, body, cap.
    box("Plinth", (1.5, 1.5, 0.14), (0, 0, -0.43), steel, 0.025)
    box("Body", (1.28, 1.28, 0.66), (0, 0, -0.03), paint, 0.03)
    box("Cap", (1.42, 1.42, 0.08), (0, 0, 0.34), steel, 0.02)
    box("Cap Top", (1.1, 1.1, 0.05), (0, 0, 0.4), paint, 0.015)
    # Corner posts.
    for sx in (-1, 1):
        for sy in (-1, 1):
            box("Post", (0.13, 0.13, 0.82), (sx * 0.66, sy * 0.66, -0.04), steel, 0.02)
    # Four faces: an inset screen with a glowing frame, vents below.
    for k in range(4):
        a = k * math.pi / 2
        nx, ny = math.cos(a), math.sin(a)
        tx, ty = -ny, nx                                  # along the face
        rot = (0, 0, a)
        box("Screen", (0.04, 0.78, 0.4), (nx * 0.645, ny * 0.645, 0.05), glass, 0.01, rot=rot)
        for z in (0.265, -0.165):                         # top and bottom of the frame
            box("Screen Frame", (0.05, 0.86, 0.03), (nx * 0.65, ny * 0.65, z), glow, 0.005, rot=rot)
        for s in (-1, 1):
            box("Screen Frame", (0.05, 0.03, 0.45), (nx * 0.65 + tx * s * 0.43, ny * 0.65 + ty * s * 0.43, 0.05), glow, 0.005, rot=rot)
        for i in range(4):
            box("Vent", (0.03, 0.7, 0.025), (nx * 0.648, ny * 0.648, -0.24 - i * 0.045), steel, 0.004, rot=rot)
    # The light ring round the cap and the hologram emitter on top.
    bpy.ops.mesh.primitive_torus_add(major_radius=0.55, minor_radius=0.018, major_segments=48, minor_segments=8, location=(0, 0, 0.44))
    t = bpy.context.active_object
    t.name = "Light Ring"
    t.data.materials.append(glow)
    parts.append(t)
    cyl("Emitter", 0.16, 0.06, (0, 0, 0.45), steel)
    cyl("Emitter Lens", 0.11, 0.07, (0, 0, 0.46), glow)
    station = join(parts, "Station")

    # Hologram icons (separate, hidden or shown by the game), floating above the emitter.
    icons = []
    ammo_parts = []
    for i, x in enumerate((-0.12, 0.0, 0.12)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.04, depth=0.2, location=(x, 0, 0.8))
        c = bpy.context.active_object
        c.data.materials.append(holo)
        bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=0.04, radius2=0.0, depth=0.1, location=(x, 0, 0.95))
        tip = bpy.context.active_object
        tip.data.materials.append(holo)
        ammo_parts += [c, tip]
    icons.append(join_plain(ammo_parts, "IconAmmo"))
    cross = [(-0.07, 0.6), (0.07, 0.6), (0.07, 0.75), (0.22, 0.75), (0.22, 0.89), (0.07, 0.89), (0.07, 1.04),
             (-0.07, 1.04), (-0.07, 0.89), (-0.22, 0.89), (-0.22, 0.75), (-0.07, 0.75)]
    icons.append(extrude_profile("IconHealth", cross, 0.08, holo))
    bolt = [(0.04, 1.05), (-0.14, 0.8), (-0.02, 0.8), (-0.08, 0.6), (0.14, 0.88), (0.02, 0.88), (0.1, 1.05)]
    icons.append(extrude_profile("IconDamage", bolt, 0.08, holo))
    for ic in icons:
        ic.parent = station
        ic.matrix_parent_inverse = station.matrix_world.inverted()
    return station


def join_plain(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.shade_smooth()
    return o


def export(station):
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    station.select_set(True)
    for c in station.children:
        c.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT, use_selection=True, export_format="GLB", export_yup=True, export_apply=True)


if __name__ == "__main__":
    export(build())
