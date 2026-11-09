"""
The robot arms and saw blades you see during the opening dive (Risk of Copyright, Godot).
Run in Blender:  exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/dive_arms.py").read())

Exports (metres; Blender +Y = Godot forward, +Z = up):
  godot/assets/models/dive/dive_arm.glb   one right arm: shoulder at the origin, the hand's grip at y = 0.88
                                          (the left arm is the same model mirrored in code)
  godot/assets/models/dive/saw_blade.glb  a saw disc centred on the origin, in the Y/Z plane, spinning about X
Material names match the survivor's paint slots (frame, body, accent, metal) so skins can recolour them.
"""
import bpy
import math
import os

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/dive"
COLL = "DiveArms"
REACH = 0.88   # shoulder to grip (the old box arms: 34.7 inches)


def mat(name, color, metal, rough):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    return m


def finish(o, material, bevel=0.0, segments=2, smooth=True):
    if bevel:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = segments
        m.limit_method = "ANGLE"
    o.data.materials.append(material)
    if smooth:
        bpy.ops.object.shade_smooth_by_angle()
    return o


def box(name, size, loc, material, bevel=0.006, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    return finish(o, material, bevel)


def tube(name, r1, r2, y0, y1, material, x=0.0, z=0.0, verts=20, bevel=0.004):
    """A tapered cylinder along +Y from y0 to y1."""
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=y1 - y0,
                                    location=(x, (y0 + y1) / 2, z), rotation=(-math.pi / 2, 0, 0))
    o = bpy.context.active_object
    o.name = name
    return finish(o, material, bevel)


def ball(name, r, loc, material, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=r, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(scale=True)
    return finish(o, material)


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


def build_arm():
    frame = mat("frame", (0.17, 0.18, 0.21), 0.2, 0.45)
    body = mat("body", (0.74, 0.78, 0.82), 0.5, 0.35)
    accent = mat("accent", (0.95, 0.52, 0.14), 0.2, 0.4)
    metal = mat("metal", (0.88, 0.9, 0.93), 0.9, 0.25)
    p = []
    # Upper sleeve: armoured, tapering toward the elbow.
    p.append(tube("Sleeve", 0.07, 0.058, 0.0, 0.42, body, verts=24, bevel=0.01))
    p.append(box("Shoulder Plate", (0.11, 0.3, 0.035), (0, 0.2, 0.062), body, 0.012))
    p.append(box("Plate Stripe", (0.112, 0.08, 0.012), (0, 0.27, 0.082), accent, 0.004))
    p.append(box("Plate Rivets", (0.09, 0.012, 0.01), (0, 0.08, 0.082), frame, 0.002))
    # Elbow joint.
    p.append(ball("Elbow", 0.06, (0, 0.44, 0), frame))
    p.append(tube("Elbow Ring", 0.064, 0.064, 0.42, 0.46, metal, verts=24, bevel=0.003))
    # Forearm: a slimmer core with two pistons underneath and a guard on top.
    p.append(tube("Forearm", 0.05, 0.045, 0.46, 0.74, frame, verts=20, bevel=0.006))
    p.append(box("Forearm Guard", (0.085, 0.24, 0.028), (0, 0.6, 0.048), body, 0.01))
    p.append(box("Guard Stripe", (0.087, 0.05, 0.01), (0, 0.66, 0.064), accent, 0.003))
    for side in (-1, 1):
        p.append(tube(f"Piston {side}", 0.012, 0.012, 0.48, 0.72, metal, x=side * 0.03, z=-0.045, verts=12, bevel=0.002))
        p.append(tube(f"Piston Sleeve {side}", 0.017, 0.017, 0.48, 0.58, frame, x=side * 0.03, z=-0.045, verts=12, bevel=0.002))
    # Wrist.
    p.append(ball("Wrist", 0.042, (0, 0.76, 0), metal))
    # Hand gripping a handle that runs across (X): a chunky palm, curled fingers, thumb over the top.
    p.append(box("Palm", (0.13, 0.11, 0.075), (0, 0.835, 0.008), frame, 0.018))
    p.append(box("Back Plate", (0.12, 0.08, 0.02), (0, 0.83, 0.05), body, 0.008))
    p.append(box("Knuckles", (0.14, 0.04, 0.05), (0, 0.9, 0.028), body, 0.012))
    for i in range(4):
        x = -0.048 + i * 0.032
        p.append(box(f"Finger {i} A", (0.026, 0.05, 0.03), (x, 0.93, 0.018), body, 0.006, rot=(math.radians(25), 0, 0)))
        p.append(box(f"Finger {i} B", (0.025, 0.045, 0.028), (x, 0.95, -0.022), body, 0.006, rot=(math.radians(80), 0, 0)))
        p.append(box(f"Finger {i} C", (0.024, 0.035, 0.026), (x, 0.935, -0.055), frame, 0.006, rot=(math.radians(140), 0, 0)))
    p.append(box("Thumb", (0.03, 0.075, 0.03), (0.078, 0.9, 0.04), body, 0.008, rot=(0, 0, math.radians(-30))))
    # The blade's handle and mount sticking out of the fist.
    p.append(tube("Grip", 0.022, 0.022, 0.9, 1.02, metal, verts=14, bevel=0.003))
    p.append(box("Mount", (0.035, 0.24, 0.028), (0, 1.04, 0.0), frame, 0.006))   # runs to the blade hub at y = 1.16
    arm = join(p, "Dive Arm")
    return arm


def build_blade():
    steel = mat("steel", (0.82, 0.85, 0.9), 1.0, 0.18)
    dark = mat("dark", (0.16, 0.17, 0.2), 0.4, 0.4)
    accent = bpy.data.materials["accent"]
    p = []
    # Disc in the Y/Z plane (thin along X), radius 14 cm.
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.13, depth=0.008, location=(0, 0, 0), rotation=(0, math.pi / 2, 0))
    p.append(finish(bpy.context.active_object, steel, 0.002))
    bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=0.085, depth=0.012, location=(0, 0, 0), rotation=(0, math.pi / 2, 0))
    p.append(finish(bpy.context.active_object, dark, 0.002))
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.03, depth=0.03, location=(0, 0, 0), rotation=(0, math.pi / 2, 0))
    p.append(finish(bpy.context.active_object, accent, 0.004))
    # Hooked teeth round the rim.
    for i in range(24):
        a = i * math.tau / 24
        r = 0.138
        bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=0.016, radius2=0.0, depth=0.03,
                                        location=(0, math.cos(a) * r, math.sin(a) * r), rotation=(a - math.pi / 2 + math.radians(25), 0, 0))
        t = bpy.context.active_object
        t.scale = (0.35, 1, 1)
        bpy.ops.object.transform_apply(scale=True)
        p.append(finish(t, steel, smooth=False))
    # Vent slots.
    for i in range(3):
        a = i * math.tau / 3
        p.append(box(f"Slot {i}", (0.014, 0.04, 0.012), (0, math.cos(a) * 0.058, math.sin(a) * 0.058), dark, 0.003, rot=(a, 0, 0)))
    return join(p, "Saw Blade")


def export(obj, name):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    loc = obj.location.copy()
    obj.location = (0, 0, 0)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name), use_selection=True, export_format="GLB", export_yup=True, export_apply=True)
    obj.location = loc


def build():
    collection()
    arm = build_arm()
    blade = build_blade()
    export(arm, "dive_arm.glb")
    export(blade, "saw_blade.glb")
    blade.location = (0.0, 1.16, 0.0)   # where the game holds it, for viewing
    return arm, blade


if __name__ == "__main__":
    build()
