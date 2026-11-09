"""
The ammo pickup for Risk of Copyright (Godot): a chunky military ammo can.
Run in Blender:  exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/ammo_box.py").read())
Exports godot/assets/models/props/ammo_box.glb (metres, origin at the bottom centre, +Y up in Godot).
Materials are named so the game can find them: AmmoBody, AmmoTrim, AmmoGlow (the glow is made emissive in code).
"""
import bpy
import bmesh
import os

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/props/ammo_box.glb"
COLL = "AmmoBox"
# Size in metres (about 36 x 18 x 22 cm, a little bigger than real so it reads in game).
W, D, H = 0.36, 0.2, 0.22


def mat(name, color, metal, rough):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    return m


def box(name, size, loc, material, bevel=0.012, segments=3):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = segments
        m.limit_method = "ANGLE"
    o.data.materials.append(material)
    bpy.ops.object.shade_smooth_by_angle()
    return o


def build():
    if COLL in bpy.data.collections:
        for o in list(bpy.data.collections[COLL].objects):
            bpy.data.objects.remove(o, do_unlink=True)
        coll = bpy.data.collections[COLL]
    else:
        coll = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(coll)
    layer = bpy.context.view_layer.layer_collection.children[COLL]
    bpy.context.view_layer.active_layer_collection = layer

    body = mat("AmmoBody", (0.36, 0.4, 0.2), 0.35, 0.55)     # olive drab
    trim = mat("AmmoTrim", (0.12, 0.12, 0.13), 0.8, 0.35)    # dark steel
    glow = mat("AmmoGlow", (1.0, 0.72, 0.18), 0.0, 0.4)      # the band that glows

    parts = [
        box("Body", (W, D, H * 0.78), (0, 0, H * 0.39), body, 0.018),
        box("Lid", (W + 0.012, D + 0.012, H * 0.2), (0, 0, H * 0.86), body, 0.02),
        box("Lid Rim", (W + 0.02, D + 0.02, 0.012), (0, 0, H * 0.77), trim, 0.004),
        box("Band", (W + 0.004, D + 0.004, 0.03), (0, 0, H * 0.42), glow, 0.006),
        # Carry handle: two posts and a grip.
        box("Handle Post L", (0.018, 0.03, 0.04), (-0.07, 0, H * 0.98), trim, 0.004),
        box("Handle Post R", (0.018, 0.03, 0.04), (0.07, 0, H * 0.98), trim, 0.004),
        box("Handle Grip", (0.17, 0.034, 0.022), (0, 0, H * 1.08), trim, 0.008),
        # Latch on the front, hinge strip on the back.
        box("Latch", (0.05, 0.02, 0.07), (W * 0.36, -D * 0.52, H * 0.74), trim, 0.005),
        box("Hinge", (W * 0.8, 0.014, 0.02), (0, D * 0.52, H * 0.78), trim, 0.004),
        # Side ribs for detail.
        box("Rib L", (0.012, D * 0.9, H * 0.6), (-W * 0.5, 0, H * 0.38), trim, 0.003),
        box("Rib R", (0.012, D * 0.9, H * 0.6), (W * 0.5, 0, H * 0.38), trim, 0.003),
    ]
    # Bullet tips peeking out of the open corner of the lid.
    brass = mat("AmmoBrass", (0.85, 0.6, 0.22), 1.0, 0.25)
    for i in range(4):
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.011, depth=0.06, location=(-W * 0.32 + i * 0.026, 0.0, H * 0.99))
        b = bpy.context.active_object
        b.name = f"Round {i}"
        b.data.materials.append(brass)
        bpy.ops.object.shade_smooth()
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.011, location=(-W * 0.32 + i * 0.026, 0.0, H * 0.99 + 0.03))
        t = bpy.context.active_object
        t.name = f"Tip {i}"
        t.scale.z = 1.8
        t.data.materials.append(brass)
        bpy.ops.object.shade_smooth()
        parts += [b, t]

    # One mesh: join, apply the bevels.
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
        bpy.context.view_layer.objects.active = p
        for m in p.modifiers:
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    can = bpy.context.active_object
    can.name = "Ammo Box"
    return can


def export(can):
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    can.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT, use_selection=True, export_format="GLB", export_yup=True, export_apply=True)


if __name__ == "__main__":
    export(build())
