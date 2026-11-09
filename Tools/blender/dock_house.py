"""
The water level's dock: the pier and the stilt house (Risk of Copyright, Godot).
Run in Blender:  exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/dock_house.py").read())

Built to the same layout as DockHouse.cs (which keeps its boxes as invisible colliders), from the same numbers in
s&box inches relative to the dock origin at deck height. s&box (x forward, y left, z up) maps to Blender as
(-y, x, z) so the glTF export lands on Godot's axes. Exports godot/assets/models/dock/dock_house.glb and the
wood textures it uses into godot/assets/textures/dock/.
"""
import bpy
import bmesh
import math
import os
import random

OUT_MODEL = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/dock/dock_house.glb"
OUT_TEX = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/textures/dock"
COLL = "DockHouse"
IN = 0.0254
PIER = 460.0   # DockHouse.PierLength
rng = random.Random(11)


def S(x, y, z):
    """s&box inches (relative to the dock origin, deck height) to Blender metres."""
    return (-y * IN, x * IN, z * IN)


def Ssize(x, y, z):
    return (y * IN, x * IN, z * IN)


# ---- textures: wood grain rendered from a procedural shader, flat lit -------------------------------------

def render_textures():
    os.makedirs(OUT_TEX, exist_ok=True)
    scene = bpy.context.scene
    keep = (scene.camera, scene.render.resolution_x, scene.render.resolution_y, scene.render.film_transparent, scene.view_settings.view_transform, scene.render.filepath)
    coll = bpy.data.collections.get("TexStage") or bpy.data.collections.new("TexStage")
    if coll.name not in scene.collection.children:
        scene.collection.children.link(coll)
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    hidden = []
    for c in scene.collection.children:
        if c is not coll and not c.hide_render:
            c.hide_render = True
            hidden.append(c)
    bpy.ops.mesh.primitive_plane_add(size=2, location=(0, 0, 0))
    plane = bpy.context.active_object
    coll.objects.link(plane)
    for c in plane.users_collection:
        if c is not coll:
            c.objects.unlink(plane)
    cam = bpy.data.objects.new("TexCam", bpy.data.cameras.new("TexCam"))
    coll.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.0
    cam.location = (0, 0, 3)
    scene.camera = cam
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"

    def grain(name, base, dark, scale, rings):
        m = bpy.data.materials.get("Tex_" + name) or bpy.data.materials.new("Tex_" + name)
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        emit = nt.nodes.new("ShaderNodeEmission")
        coord = nt.nodes.new("ShaderNodeTexCoord")
        noise = nt.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 3.0
        noise.inputs["Detail"].default_value = 6
        wave = nt.nodes.new("ShaderNodeTexWave")
        wave.wave_type = "BANDS"
        wave.bands_direction = "Y"
        wave.inputs["Scale"].default_value = rings
        wave.inputs["Distortion"].default_value = 6.0
        wave.inputs["Detail"].default_value = 3
        fine = nt.nodes.new("ShaderNodeTexNoise")
        fine.inputs["Scale"].default_value = 60.0
        fine.inputs["Detail"].default_value = 2
        mapping = nt.nodes.new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (1, scale, 1)
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (*dark, 1)
        ramp.color_ramp.elements[1].color = (*base, 1)
        mix = nt.nodes.new("ShaderNodeMath")
        mix.operation = "MULTIPLY_ADD"
        mix.inputs[1].default_value = 0.85
        nt.links.new(coord.outputs["Generated"], mapping.inputs["Vector"])
        nt.links.new(mapping.outputs["Vector"], wave.inputs["Vector"])
        nt.links.new(mapping.outputs["Vector"], fine.inputs["Vector"])
        nt.links.new(wave.outputs["Fac"], mix.inputs[0])
        nt.links.new(fine.outputs["Fac"], mix.inputs[2])
        nt.links.new(mix.outputs["Value"], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], emit.inputs["Color"])
        nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
        plane.data.materials.clear()
        plane.data.materials.append(m)
        scene.render.filepath = os.path.join(OUT_TEX, name + ".png")
        bpy.ops.render.render(write_still=True)

    # Weathered boards in three shades, posts, and the roof's painted shingles.
    grain("wood_pale", (0.62, 0.47, 0.31), (0.42, 0.3, 0.18), 1.0, 2.5)
    grain("wood", (0.5, 0.34, 0.2), (0.32, 0.21, 0.12), 1.0, 2.5)
    grain("wood_dark", (0.33, 0.22, 0.13), (0.2, 0.13, 0.08), 1.0, 2.5)
    grain("roof", (0.6, 0.2, 0.16), (0.38, 0.12, 0.1), 0.4, 1.2)

    for c in hidden:
        c.hide_render = False
    scene.camera, scene.render.resolution_x, scene.render.resolution_y, scene.render.film_transparent, scene.view_settings.view_transform, scene.render.filepath = keep
    bpy.data.objects.remove(plane, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)


def textured(name, tex, rough=0.85):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = rough
    img = nt.nodes.new("ShaderNodeTexImage")
    path = os.path.join(OUT_TEX, tex + ".png")
    img.image = bpy.data.images.load(path, check_existing=True)
    img.image.reload()
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m


def plain(name, color, metal=0.0, rough=0.6, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    b.inputs["Alpha"].default_value = alpha
    if alpha < 1:
        m.surface_render_method = "BLENDED"
    return m


# ---- geometry helpers (all in s&box inches) ----------------------------------------------------------------

parts = []


def box(center, size, material, bevel=0.6, rot=None):
    """A box at an s&box centre/size (inches). rot = (pitch, yaw, roll) degrees in s&box terms."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=S(*center))
    o = bpy.context.active_object
    o.scale = Ssize(*size)
    if rot:
        p, y, r = rot
        # s&box pitch turns about its y (left) axis = Blender -X, yaw about z, roll about x = Blender Y.
        o.rotation_euler = (math.radians(-p), math.radians(r), math.radians(y))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel * IN
        m.segments = 1
        m.limit_method = "ANGLE"
    o.data.materials.append(material)
    parts.append(o)
    return o


def build_geometry():
    wood = textured("DockWood", "wood")
    pale = textured("DockWoodPale", "wood_pale")
    dark = textured("DockWoodDark", "wood_dark")
    roof = textured("DockRoof", "roof", 0.7)
    glass = plain("DockGlass", (0.55, 0.78, 0.92), 0.0, 0.05, 0.45)
    iron = plain("DockIron", (0.16, 0.16, 0.17), 0.8, 0.45)
    shades = [wood, pale, dark]

    # Pier: a dark under-floor, loose deck planks in mixed shades with small gaps.
    box((PIER * 0.5, 0, -4.2), (PIER, 116, 2.4), dark, 0)
    x = 6.0
    while x < PIER:
        w = 13.0 + rng.uniform(-0.6, 0.6)
        box((x, rng.uniform(-1.2, 1.2), -1.4), (w, 121 + rng.uniform(-3, 3), 2.6), rng.choice(shades), 0.5, rot=(rng.uniform(-0.4, 0.4), rng.uniform(-0.5, 0.5), 0))
        x += 16.0
    # Joists under the planks, stilts with cross bracing.
    for side in (-1, 1):
        box((PIER * 0.5, side * 52, -7.5), (PIER, 5, 6), dark, 0.4)
    for sx in range(20, int(PIER) + 1, 90):
        for side in (-1, 1):
            box((sx, side * 58, -70), (9, 9, 150), dark, 1.2)
            box((sx, side * 58, -60), (11, 11, 3), iron, 0.3)   # iron band
        box((sx, 0, -40), (5, 116, 5), dark, 0.5, rot=(0, 0, 32))
        box((sx, 0, -40), (5, 116, 5), dark, 0.5, rot=(0, 0, -32))
        # Rail posts and a top rail with a mid rail.
        if sx < PIER - 60:
            for side in (-1, 1):
                box((sx, side * 58, 18), (5, 5, 36), wood, 0.6)
                box((sx, side * 58, 36.5), (6, 6, 1.5), dark, 0.4)
    for side in (-1, 1):
        box(((PIER - 60) * 0.5 + 10, side * 58, 34), (PIER - 60, 4, 4), pale, 0.8)
        box(((PIER - 60) * 0.5 + 10, side * 58, 18), (PIER - 60, 2.5, 3), wood, 0.5)

    # Diving board on its stand, a spring block underneath.
    box((PIER + 22, 0, 4), (70, 34, 3), plain("DockBoard", (0.92, 0.9, 0.84), 0, 0.5), 1.0, rot=(-4, 0, 0))
    box((PIER + 26, 0, 7.3), (60, 30, 0.6), plain("DockGrip", (0.25, 0.55, 0.65), 0, 0.9), 0.2, rot=(-4, 0, 0))
    box((PIER - 4, 0, 1), (14, 30, 6), iron, 0.8)
    box((PIER + 8, 0, -1), (8, 20, 6), plain("DockSpring", (0.75, 0.2, 0.15), 0.2, 0.5), 0.8)
    # Lamp post with a little arm.
    box((PIER - 30, -50, 40), (5, 5, 80), dark, 0.8)
    box((PIER - 30, -50, 2), (9, 9, 4), iron, 0.4)

    # ---- the house ----
    hx = -150.0
    wallH = 110.0
    box((hx, 0, -3), (300, 280, 6), dark, 0.8)
    for dx in range(-140, 141, 70):
        for dy in (-130, 130):
            box((hx + dx, dy, -70), (11, 11, 150), dark, 1.4)
            box((hx + dx, dy, -60), (13, 13, 3), iron, 0.3)
    # Porch planks over the deck.
    y = -130.0
    while y < 130:
        box((hx, y + 5, 0.4), (296, 9.4, 1.2), rng.choice(shades), 0.3)
        y += 10.0

    def wall(cx, cy, length, along_x, height=wallH, z0=0.0, skip=None):
        """Board-and-batten: a backing panel, vertical boards and thin battens over the seams."""
        sz = (length, 8, height) if along_x else (8, length, height)
        box((cx, cy, z0 + height * 0.5), sz, dark, 0.4)
        n = int(length / 11)
        for i in range(n):
            t = -length * 0.5 + (i + 0.5) * length / n
            if skip and skip(t):
                continue
            board = (cx + t, cy, z0 + height * 0.5) if along_x else (cx, cy + t, z0 + height * 0.5)
            bsz = (length / n - 0.6, 9.2, height - 0.5) if along_x else (9.2, length / n - 0.6, height - 0.5)
            box(board, bsz, rng.choice(shades[:2]), 0.4)
        # Top and bottom trim.
        for z in (z0 + 2, z0 + height - 2):
            tsz = (length + 2, 10.5, 4) if along_x else (10.5, length + 2, 4)
            box((cx, cy, z), tsz, dark, 0.5)

    wall(hx - 110, 0, 200, False)                                         # back
    wall(hx, 100, 220, True)                                              # left side
    wall(hx, -100, 220, True, skip=lambda t: abs(t) < 27)                 # right side, with the window opening
    wall(hx + 110, -65, 70, False)                                        # front, either side of the door
    wall(hx + 110, 65, 70, False)
    box((hx + 110, 0, wallH - 12), (8, 60, 24), wood, 0.6)                # lintel over the door
    # Door frame and the window: frame, sill, glass, cross bars.
    for dy in (-31, 31):
        box((hx + 112, dy, (wallH - 24) * 0.5), (11, 5, wallH - 24), pale, 0.6)
    box((hx + 112, 0, wallH - 25), (11, 66, 5), pale, 0.6)
    box((hx, -104, 75), (50, 2, 30), glass, 0)
    for dz in (-16, 16):
        box((hx, -105, 75 + dz), (56, 5, 4), pale, 0.6)
    for dx in (-27, 27):
        box((hx + dx, -105, 75), (4, 5, 36), pale, 0.6)
    box((hx, -106, 58), (60, 8, 3), pale, 0.6)                             # sill
    box((hx, -105.5, 75), (50, 3, 1.5), pale, 0.3)                         # cross bars
    box((hx, -105.5, 75), (1.5, 3, 30), pale, 0.3)

    # Roof: two slopes of overlapping shingle rows, an overhang, gable boards and a ridge beam.
    for side in (-1, 1):
        rows = 9
        for r in range(rows):
            t = (r + 0.5) / rows
            # From the eave up to the ridge along the slope (24 degrees, 132 long).
            yy = side * (118 - t * 120)
            zz = wallH + 26 + (0.5 - t) * 132 * math.sin(math.radians(24)) * -1 * -1
            zz = wallH - 1 + t * 120 * math.tan(math.radians(24))
            box((hx, yy, zz + 2.5), (262, 16, 2.4), roof, 0.4, rot=(0, 0, -side * 24 + rng.uniform(-1.2, 1.2)))
        box((hx, side * 120, wallH - 2), (264, 3, 5), dark, 0.4, rot=(0, 0, -side * 24))   # fascia at the eave
    ridge_z = wallH - 1 + 120 * math.tan(math.radians(24)) + 4
    box((hx, 0, ridge_z), (266, 9, 7), dark, 1.0)
    # Gable triangles front and back, from stacked boards.
    for gx in (hx - 110, hx + 110):
        layers = 8
        for k in range(layers):
            t = (k + 0.5) / layers
            width = 200 * (1 - t)
            box((gx, 0, wallH + t * (ridge_z - wallH)), (8, width, (ridge_z - wallH) / layers + 0.4), rng.choice(shades[:2]), 0.3)

    # Inside: a table on legs and a slatted crate.
    tx, ty = hx - 60, 50
    box((tx, ty, 24), (50, 34, 3), pale, 0.6)
    for lx in (-21, 21):
        for ly in (-13, 13):
            box((tx + lx, ty + ly, 11), (3, 3, 22), wood, 0.4)
    cx, cy = hx - 70, -60
    box((cx, cy, 15), (28, 28, 28), dark, 0.6)
    for dz in (-9, 0, 9):
        for dy in (-14.5, 14.5):
            box((cx, cy + dy, 15 + dz), (30, 1.5, 6), pale, 0.4)
        for dx in (-14.5, 14.5):
            box((cx + dx, cy, 15 + dz), (1.5, 30, 6), pale, 0.4)


def finish():
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
        bpy.context.view_layer.objects.active = p
        for m in list(p.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    house = bpy.context.active_object
    house.name = "Dock House"
    # Box-project UVs: about one texture repeat per 1.5 m, in world space so grain runs along every board.
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.5)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    return house


def build(textures=True):
    global parts
    parts = []
    if textures:
        render_textures()
    if COLL in bpy.data.collections:
        for o in list(bpy.data.collections[COLL].objects):
            bpy.data.objects.remove(o, do_unlink=True)
        coll = bpy.data.collections[COLL]
    else:
        coll = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(coll)
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[COLL]
    build_geometry()
    house = finish()
    os.makedirs(os.path.dirname(OUT_MODEL), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    house.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT_MODEL, use_selection=True, export_format="GLB", export_yup=True, export_apply=True)
    return house


if __name__ == "__main__":
    build()
