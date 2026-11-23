"""
The Pinball-Bot's guns for the Godot port: the Pinball Launcher and the Flipper Arms.

Built in gun space as GunKit uses it (1 Blender unit = 1 inch, +X along the barrel, +Y to the left, +Z up, the grip at
the origin) and exported without the Y-up turn, so the numbers land in Godot as they are written here.
Moving parts are their own nodes, pivoted where they turn: "Plunger" (slides back), "Spring" (stretches along -X from
the housing), "Loaded" (the ball in the lane), "Ball Light N Lit" (the ball count), "Deck", "Left Flipper",
"Right Flipper" (turn about their own Z).

Run:  blender -b --python Tools/blender/pinball_guns.py
Writes godot/assets/models/weapons/pinball_launcher.glb and pinball_flippers.glb.
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "godot", "assets", "models", "weapons")

# ---- Palette (the robot's: purple cabinet, chrome, yellow, neon pink) ----
PALETTE = {
    "Purple": dict(color=(0.30, 0.10, 0.70), rough=0.35),
    "DeepPurple": dict(color=(0.12, 0.04, 0.30), rough=0.5),
    "Chrome": dict(color=(0.90, 0.92, 0.96), metal=1.0, rough=0.08),
    "Yellow": dict(color=(1.0, 0.80, 0.10), rough=0.35),
    "Red": dict(color=(0.90, 0.10, 0.15), rough=0.3),
    "Rubber": dict(color=(0.05, 0.05, 0.06), rough=0.8),
    "White": dict(color=(0.95, 0.95, 0.95), rough=0.5),
    "RedRubber": dict(color=(0.85, 0.08, 0.12), rough=0.7),
    "Navy": dict(color=(0.03, 0.05, 0.16), rough=0.4),
    "Neon": dict(color=(1.0, 0.25, 0.85), emit=4.0),
    "NeonCyan": dict(color=(0.25, 0.95, 1.0), emit=4.0),
    "LampYellow": dict(color=(1.0, 0.85, 0.2), emit=5.0),
    "LampOff": dict(color=(0.25, 0.2, 0.08), rough=0.3),
    "Score": dict(color=(1.0, 0.45, 0.05), emit=3.0),
    "Glass": dict(color=(0.55, 0.85, 1.0), rough=0.05, alpha=0.45),
}
MATS = {}


def material(name):
    if name in MATS:
        return MATS[name]
    spec = PALETTE[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    c = spec["color"]
    b.inputs["Base Color"].default_value = (c[0], c[1], c[2], 1.0)
    b.inputs["Metallic"].default_value = spec.get("metal", 0.0)
    b.inputs["Roughness"].default_value = spec.get("rough", 0.45)
    if spec.get("emit"):
        b.inputs["Emission Color"].default_value = (c[0], c[1], c[2], 1.0)
        b.inputs["Emission Strength"].default_value = spec["emit"]
    if spec.get("alpha", 1.0) < 1.0:
        b.inputs["Alpha"].default_value = spec["alpha"]
        m.blend_method = "BLEND"
    MATS[name] = m
    return m


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.curves, bpy.data.materials):
        for x in list(block):
            if x.users == 0:
                block.remove(x)
    MATS.clear()


def finish(obj, mat, name, parent=None, bevel=0.0, smooth=True):
    obj.name = name
    obj.data.materials.clear()
    obj.data.materials.append(material(mat))
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        mod.limit_method = "ANGLE"
    if smooth:
        for poly in obj.data.polygons:
            poly.use_smooth = True
        mod = obj.modifiers.new("Normals", "WEIGHTED_NORMAL") if bevel > 0 else None
        if mod:
            mod.keep_sharp = True
    if parent is not None:
        obj.parent = parent
    return obj


def box(name, mat, centre, size, parent=None, rot=(0, 0, 0), bevel=0.25):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    o = bpy.context.active_object
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    o.location = centre
    o.rotation_euler = Euler([math.radians(a) for a in rot])
    return finish(o, mat, name, parent, bevel, smooth=bevel > 0)


def cyl_x(name, mat, centre, radius, length, parent=None, front=None, sides=24, bevel=0.0):
    """A cylinder along +X (radius at the back, `front` at the front)."""
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=radius, radius2=radius if front is None else front, depth=length, location=(0, 0, 0))
    o = bpy.context.active_object
    o.rotation_euler = (0, math.radians(90), 0)
    bpy.ops.object.transform_apply(rotation=True)
    o.location = centre
    return finish(o, mat, name, parent, bevel)


def cyl_z(name, mat, centre, radius, height, parent=None, sides=24, top=None, bevel=0.0):
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=radius, radius2=radius if top is None else top, depth=height, location=centre)
    return finish(bpy.context.active_object, mat, name, parent, bevel)


def ball(name, mat, centre, radius, parent=None, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=radius, location=centre)
    o = bpy.context.active_object
    o.scale = scale
    return finish(o, mat, name, parent)


def torus_x(name, mat, centre, major, minor, parent=None):
    """A ring round the +X axis."""
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=32, minor_segments=10, location=(0, 0, 0))
    o = bpy.context.active_object
    o.rotation_euler = (0, math.radians(90), 0)
    bpy.ops.object.transform_apply(rotation=True)
    o.location = centre
    return finish(o, mat, name, parent)


def empty(name, at, parent=None, rot=(0, 0, 0)):
    o = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(o)
    o.location = at
    o.rotation_euler = Euler([math.radians(a) for a in rot])
    if parent is not None:
        o.parent = parent
    return o


def prism(name, mat, outline, z0, z1, parent=None, bevel=0.15):
    """A flat shape (list of (x, y)) pushed up from z0 to z1."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bottom = [bm.verts.new((x, y, z0)) for x, y in outline]
    top = [bm.verts.new((x, y, z1)) for x, y in outline]
    bm.faces.new(list(reversed(bottom)))
    bm.faces.new(top)
    n = len(outline)
    for i in range(n):
        bm.faces.new([bottom[i], bottom[(i + 1) % n], top[(i + 1) % n], top[i]])
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    return finish(o, mat, name, parent, bevel, smooth=False)


def star_outline(cx, cy, r_out, r_in, points=5, turn=90):
    pts = []
    for i in range(points * 2):
        a = math.radians(turn) + i * math.pi / points
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def side_panel(name, mat, outline_xz, y, thickness, parent=None):
    """A flat shape in the X-Z plane (outline of (x, z)), standing on a side of the gun at `y`, `thickness` thick."""
    o = prism(name, mat, outline_xz, 0, thickness, parent, bevel=0.05)
    o.rotation_euler = (math.radians(90 if y > 0 else -90), 0, 0)
    o.location = (0, y, 0)
    return o


def grip(parent=None):
    """The grip and trigger every Pinball-Bot gun shares (where the rig's hand closes)."""
    box("Grip", "Rubber", (-0.6, 0, -4.0), (3.0, 2.7, 7.8), parent, rot=(0, 18, 0), bevel=0.6)
    for i in range(4):
        box(f"Grip Rib {i}", "DeepPurple", (-2.25 + i * -0.25, 0, -2.2 - i * 1.6), (0.5, 2.85, 0.45), parent, rot=(0, 18, 0), bevel=0.15)
    # The trigger is a flipper button: red, in a chrome bezel, in a yellow guard.
    cyl_x("Trigger Bezel", "Chrome", (1.15, 0, -0.7), 0.85, 0.35, parent)
    cyl_x("Trigger", "Red", (1.55, 0, -0.7), 0.6, 0.6, parent, bevel=0.15)
    box("Guard", "Yellow", (2.1, 0, -2.3), (3.6, 0.55, 0.45), parent, bevel=0.15)
    box("Guard Front", "Yellow", (3.75, 0, -1.2), (0.45, 0.55, 2.4), parent, bevel=0.15)


# ---------------------------------------------------------------- The Pinball Launcher

def launcher():
    gun = empty("Pinball Launcher", (0, 0, 0))
    grip(gun)
    # The cabinet: a purple body with yellow trim rails and a chrome lockdown bar, lower and narrower at the front.
    box("Cabinet", "Purple", (6.5, 0, 2.7), (19.0, 4.6, 4.2), gun, bevel=0.55)
    for y in (2.35, -2.35):
        box(f"Rail {y:+.0f}", "Yellow", (6.5, y, 4.75), (19.4, 0.35, 0.35), gun, bevel=0.12)
        box(f"Neon Strip {y:+.0f}", "Neon", (6.8, y * 1.01, 1.5), (16.0, 0.12, 0.3), gun, bevel=0.0)
    box("Lockdown Bar", "Chrome", (-2.9, 0, 4.7), (0.9, 5.0, 0.7), gun, bevel=0.2)
    # The nose: tapering to the bumper muzzle, with a yellow band, and a flipper either side as fins.
    cyl_x("Nose", "Purple", (19.5, 0, 3.0), 2.3, 6.0, gun, front=1.6, bevel=0.2)
    torus_x("Nose Band", "Yellow", (17.4, 0, 3.0), 2.15, 0.28, gun)
    for s in (1, -1):
        fin = prism(f"Nose Fin {s:+d}", "Yellow", [(0, 0), (6.5, -0.6 * s), (6.5, -1.4 * s), (0, -1.0 * s)], -0.35, 0.35, gun, bevel=0.15)
        fin.rotation_euler = (math.radians(90 * s), 0, 0)
        fin.location = (15.5, 2.0 * s, 1.6)
        fin.rotation_euler = (0, 0, math.radians(-12 * s))
    # The muzzle: a pop bumper (red rubber ring, white skirt, yellow cap with a neon star) the ball goes through.
    cyl_x("Bumper Skirt", "White", (23.0, 0, 3.0), 1.9, 1.2, gun)
    torus_x("Bumper Rubber", "RedRubber", (23.8, 0, 3.0), 1.75, 0.42, gun)
    cyl_x("Bumper Cap", "Yellow", (24.7, 0, 3.0), 1.5, 0.9, gun, front=1.25, bevel=0.15)
    torus_x("Muzzle Glow", "NeonCyan", (25.2, 0, 3.0), 1.15, 0.16, gun)
    # The launch lane on top: a glass tube on chrome rails, the loaded ball sitting at its front.
    # (A glass tube drew as nothing in the game's shading: a cage of chrome wires reads better.)
    for i, (y, z) in enumerate(((1.05, 5.2), (-1.05, 5.2), (0.75, 6.85), (-0.75, 6.85))):
        cyl_x(f"Lane Wire {i}", "Chrome", (11.0, y, z), 0.16, 19.0, gun, sides=10)
    box("Lane Floor", "Navy", (11.0, 0, 4.85), (19.0, 1.6, 0.25), gun, bevel=0.05)
    for i, x in enumerate((4.0, 8.0, 12.0, 16.0)):
        box(f"Lane Arrow {i}", "NeonCyan" if i % 2 == 0 else "Neon", (x, 0, 5.0), (0.9, 0.5, 0.1), gun, rot=(0, 0, 45), bevel=0.0)
    for x in (1.6, 11.0, 20.4):
        torus_x(f"Lane Clamp {x:.0f}", "Chrome", (x, 0, 6.0), 1.25, 0.2, gun)
    loaded = empty("Loaded", (19.2, 0, 5.95), gun)
    ball("Loaded Ball", "Chrome", (0, 0, 0), 0.95, loaded)
    # The left side, which you see: a big neon star, a lightning bolt, the ball count lights and a score window.
    side = 2.32
    star = prism("Side Star", "Neon", star_outline(0, 0, 1.6, 0.68), 0, 0.12, gun, bevel=0.03)
    star.rotation_euler = (math.radians(90), 0, 0)
    star.location = (12.6, side, 2.75)
    bolt = prism("Side Bolt", "Yellow", [(0, 0.9), (2.4, 0.9), (1.4, 0.1), (3.4, 0.1), (0.4, -1.3), (1.2, -0.3), (-0.6, -0.3)], 0, 0.12, gun, bevel=0.03)
    bolt.rotation_euler = (math.radians(90), 0, 0)
    bolt.location = (8.0, side, 3.0)
    box("Score Window", "Rubber", (2.5, side, 3.45), (4.6, 0.2, 1.25), gun, bevel=0.1)
    box("Score", "Score", (2.5, side + 0.08, 3.45), (4.1, 0.12, 0.8), gun, bevel=0.0)
    for i in range(5):
        x = 0.8 + i * 0.85
        light = empty(f"Ball Light {i + 1}", (x, side + 0.05, 1.95), gun)
        cyl_z(f"Ball Light {i + 1} Rim", "Chrome", (0, 0, 0), 0.36, 0.2, light).rotation_euler = (math.radians(90), 0, 0)
        ball(f"Ball Light {i + 1} Off", "LampOff", (0, 0.08, 0), 0.27, light, scale=(1, 0.6, 1))
        ball(f"Ball Light {i + 1} Lit", "LampYellow", (0, 0.1, 0), 0.29, light, scale=(1, 0.65, 1))
    # The right side gets a star too (the third person view sees it).
    star2 = prism("Side Star R", "Neon", star_outline(0, 0, 1.6, 0.68), 0, 0.12, gun, bevel=0.03)
    star2.rotation_euler = (math.radians(-90), 0, 0)
    star2.location = (12.6, -side, 2.75)
    # The plunger at the back: a chrome housing, the rod through it with a red knob, the spring round the rod.
    cyl_x("Plunger Housing", "Chrome", (-3.6, 0, 3.2), 1.05, 1.6, gun, bevel=0.1)
    plunger = empty("Plunger", (0, 0, 0), gun)
    cyl_x("Plunger Rod", "Chrome", (-5.6, 0, 3.2), 0.32, 5.0, plunger)
    ball("Plunger Knob", "Red", (-8.1, 0, 3.2), 1.05, plunger, scale=(0.85, 1, 1))
    cyl_x("Plunger Collar", "Chrome", (-7.1, 0, 3.2), 0.55, 0.4, plunger)
    spring = empty("Spring", (-4.4, 0, 3.2), gun)
    for i in range(7):
        t = torus_x(f"Spring Coil {i}", "Chrome", (-0.38 - i * 0.37, 0, 0), 0.58, 0.09, spring)
        t.rotation_euler = (0, math.radians(8), 0)
    return gun


# ---------------------------------------------------------------- The Flipper Arms

def flipper_paddle(name, hinge, side, parent):
    """A flipper on its hinge: a tapered yellow paddle with a red rubber band, resting pointed down and in."""
    pivot = empty(name, hinge, parent)
    length, r0, r1 = 5.0, 1.05, 0.5
    rest = math.radians(-122 if side > 0 else 122)   # down the table and in; a flip turns it 60 degrees up
    d = Vector((math.cos(rest), math.sin(rest), 0))
    n = Vector((-d.y, d.x, 0))
    outline = []
    steps = 10
    for i in range(steps + 1):   # round end at the hinge
        a = math.pi * 0.5 + math.pi * i / steps
        v = d * math.cos(a) * r0 + n * math.sin(a) * r0
        outline.append((v.x, v.y))
    for i in range(steps + 1):   # round end at the tip
        a = -math.pi * 0.5 + math.pi * i / steps
        v = d * (length + math.cos(a) * r1) + n * math.sin(a) * r1
        outline.append((v.x, v.y))
    prism(f"{name} Body", "Yellow", outline, -0.45, 0.45, pivot, bevel=0.2)
    band = prism(f"{name} Rubber", "RedRubber", outline, -0.22, 0.22, pivot, bevel=0.05)
    band.scale = (1.07, 1.07, 1.0)
    cyl_z(f"{name} Cap", "Chrome", (0, 0, 0.5), 0.6, 0.35, pivot, bevel=0.08)
    return pivot


def flippers():
    gun = empty("Flipper Arms", (0, 0, 0))
    grip(gun)
    # The apron housing the hands hold: purple, yellow trim, a neon strip.
    box("Apron", "Purple", (3.5, 0, 2.3), (13.0, 6.0, 3.4), gun, bevel=0.55)
    for y in (3.05, -3.05):
        box(f"Apron Trim {y:+.0f}", "Yellow", (3.5, y, 3.95), (13.2, 0.3, 0.3), gun, bevel=0.1)
        box(f"Apron Neon {y:+.0f}", "Neon", (3.7, y * 1.01, 1.5), (10.0, 0.12, 0.28), gun, bevel=0.0)
    # The deck: a bit of pinball table tipped up toward you, with the two flippers, slingshots and a centre post.
    deck = empty("Deck", (8.5, 0, 4.0), gun, rot=(0, -24, 0))
    deck.scale = (1.3, 1.3, 1.3)
    prism("Deck Plate", "Navy", [(-3.0, -6.6), (9.5, -6.6), (13.5, -3.5), (13.5, 3.5), (9.5, 6.6), (-3.0, 6.6)], -0.6, 0.0, deck, bevel=0.2)
    prism("Deck Edge", "Chrome", [(-3.0, -6.9), (9.6, -6.9), (13.8, -3.6), (13.8, 3.6), (9.6, 6.9), (-3.0, 6.9), (-3.0, 6.6), (9.4, 6.6), (13.2, 3.4), (13.2, -3.4), (9.4, -6.6), (-3.0, -6.6)], -0.6, 0.55, deck, bevel=0.08)
    # Neon arrows up the middle lane.
    for i, x in enumerate((7.0, 9.0, 11.0)):
        prism(f"Lane Arrow {i}", "NeonCyan" if i % 2 == 0 else "Neon", [(x, -0.7), (x + 0.9, 0), (x, 0.7), (x + 0.35, 0)], 0.0, 0.08, deck, bevel=0.0)
    # The slingshots: white triangles with red rubber, either side above the flippers.
    for s in (1, -1):
        sling = [(3.2, 6.3 * s), (7.6, 6.3 * s), (6.6, 4.0 * s)]
        if s < 0:
            sling = list(reversed(sling))
        prism(f"Slingshot {s:+d}", "White", sling, 0.0, 1.0, deck, bevel=0.15)
        box(f"Sling Rubber {s:+d}", "RedRubber", (6.0, 5.3 * s, 0.55), (2.6, 0.3, 0.7), deck, rot=(0, 0, -62 * s), bevel=0.1)
        cyl_z(f"Sling Post {s:+d}", "Chrome", (7.6, 6.3 * s, 0.6), 0.32, 1.2, deck)
    cyl_z("Centre Post", "Chrome", (5.2, 0, 0.6), 0.28, 1.2, deck)
    cyl_z("Centre Rubber", "RedRubber", (5.2, 0, 0.9), 0.55, 0.35, deck)
    # The flippers on their hinges (their own Z is the deck's up, so a turn about it swings them across the deck).
    flipper_paddle("Left Flipper", (2.5, 5.4, 0.5), 1, deck)
    flipper_paddle("Right Flipper", (2.5, -5.4, 0.5), -1, deck)
    # A pop bumper at the top of the deck, the muzzle where a flipped ball goes out.
    cyl_z("Top Bumper Skirt", "White", (11.2, 0, 0.5), 1.25, 1.0, deck)
    cyl_z("Top Bumper Rubber", "RedRubber", (11.2, 0, 0.75), 1.35, 0.35, deck)
    cyl_z("Top Bumper Cap", "Yellow", (11.2, 0, 1.25), 1.05, 0.5, deck, top=0.85)
    prism("Top Bumper Star", "Neon", star_outline(11.2, 0, 0.75, 0.32), 1.5, 1.6, deck, bevel=0.0)
    return gun


def export(root, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.context.scene.objects:
        o.select_set(True)
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_format="GLB", export_yup=False, export_apply=True)
    print("wrote", path)


def preview(name, cam_at, look_at):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 38
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = cam_at
    direction = Vector(look_at) - Vector(cam_at)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4
    sun.rotation_euler = (math.radians(40), math.radians(-30), math.radians(60))
    scene.collection.objects.link(sun)
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.1, 0.12, 0.18, 1)
    scene.render.filepath = os.path.join(os.environ.get("PREVIEW_DIR", OUT), name)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    bpy.data.objects.remove(sun)


if __name__ == "__main__":
    clear()
    launcher()
    export(None, "pinball_launcher.glb")
    if os.environ.get("PREVIEW_DIR"):
        preview("launcher_side.png", (4, 40, 12), (9, 0, 2.5))
        preview("launcher_view.png", (-22, 10, 14), (12, 0, 2))
    clear()
    flippers()
    export(None, "pinball_flippers.glb")
    if os.environ.get("PREVIEW_DIR"):
        preview("flippers_view.png", (-20, 8, 18), (12, 0, 4))
        preview("flippers_top.png", (12, 0, 40), (12, 0, 4))
