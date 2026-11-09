"""
Goo splatters for Risk of Copyright (Godot): thick wet splats rendered to transparent PNGs.
Used on the camera (screen goo when a creature bursts near you) and as floor/wall goo decals.

Run inside Blender (Text editor > Run, or via the MCP connector):
    exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/goo_splats.py").read())

Each splat is metaballs (a body, satellite drops, drips running down, a few flung specks) melted into one
mesh, flattened like goo on glass, with a glossy white material: the game tints it per creature, so the
renders stay white with grey shading and bright highlights. Seeds make each variant different but repeatable.
"""
import bpy
import math
import random
import os

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/textures/goo"
VARIANTS = 8
SIZE = 512
COLL = "GooSplats"


def clear():
    if COLL in bpy.data.collections:
        c = bpy.data.collections[COLL]
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        c = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(c)
    return c


def material():
    m = bpy.data.materials.get("GooWet") or bpy.data.materials.new("GooWet")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (0.16, 0.16, 0.16, 1)   # dark grey: the tint keeps the shading
    bsdf.inputs["Roughness"].default_value = 0.08
    bsdf.inputs["Coat Weight"].default_value = 1.0
    bsdf.inputs["Coat Roughness"].default_value = 0.03
    bsdf.inputs["Subsurface Weight"].default_value = 0.0
    bsdf.inputs["Subsurface Radius"].default_value = (0.4, 0.4, 0.4)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m


def splat(seed, coll, mat, drips=True, flat=0.45):
    rng = random.Random(seed)
    mb = bpy.data.metaballs.new(f"splat_{seed}")
    mb.resolution = 0.02
    mb.render_resolution = 0.012
    mb.threshold = 0.6

    def ball(x, y, r, sx=1.0, sy=1.0):
        e = mb.elements.new()
        e.co = (x, y, 0)
        e.radius = r
        e.type = "ELLIPSOID"
        e.size_x, e.size_y, e.size_z = sx, sy, flat   # shape, relative to the radius

    # Body: one round core with fat lobes bulging out of its edge (a paint-splat silhouette).
    core = rng.uniform(0.36, 0.42)
    ball(0, 0, core)
    for _ in range(rng.randint(9, 14)):
        a = rng.uniform(0, math.tau)
        d = core * rng.uniform(0.6, 0.85)
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.13, 0.2))
    # Short stubby splashes: a thick neck running out to a round bead.
    for _ in range(rng.randint(2, 4)):
        a = rng.uniform(0, math.tau)
        reach = rng.uniform(0.08, 0.18)
        steps = 8
        for k in range(steps):
            t = k / (steps - 1)
            d = core * 0.8 + t * reach
            ball(math.cos(a) * d, math.sin(a) * d, 0.1 * (1 - 0.3 * t))
        d = core * 0.8 + reach + 0.04
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.08, 0.11))
    # Round droplets thrown round the edge.
    for _ in range(rng.randint(6, 11)):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0.55, 0.8)
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.035, 0.08))
    # A drip or two running down, smooth, ending in a fat bead.
    for _ in range(rng.randint(0, 2) if drips else 0):
        x = rng.uniform(-0.15, 0.15)
        y = -core * 0.7
        length = rng.uniform(0.2, 0.42)
        steps = 24
        for k in range(steps):
            t = k / (steps - 1)
            ball(x, y - t * length, 0.055 * (1 - 0.25 * t), sx=0.9)
        ball(x, y - length - 0.03, 0.08)
    # Fine specks, further out.
    for _ in range(rng.randint(6, 12)):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0.7, 0.92)
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.015, 0.03))

    obj = bpy.data.objects.new(f"Splat {seed}", mb)
    coll.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def stage(coll):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = SIZE
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    cam = bpy.data.objects.get("SplatCam")
    if cam is None:
        cam = bpy.data.objects.new("SplatCam", bpy.data.cameras.new("SplatCam"))
        coll.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.0
    cam.location = (0, 0, 5)
    cam.rotation_euler = (0, 0, 0)
    scene.camera = cam
    # Key light from the top left (the highlight), soft fill so the body reads.
    for name, loc, energy, size in (("SplatKey", (-2.5, 3, 2.5), 700, 0.5), ("SplatFill", (3, -2, 3), 25, 4.0)):
        light = bpy.data.objects.get(name)
        if light is None:
            light = bpy.data.objects.new(name, bpy.data.lights.new(name, "AREA"))
            coll.objects.link(light)
        light.location = loc
        light.data.energy = energy
        light.data.size = size
        direction = -light.location.normalized()
        light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.05, 0.05, 1)
    return cam


def build(render=True, show=None):
    for o in bpy.context.scene.objects:
        if o.name in ("Cube",):
            o.hide_render = o.hide_viewport = True
    coll = clear()
    mat = material()
    stage(coll)
    os.makedirs(OUT, exist_ok=True)
    splats = [splat(100 + i, coll, mat) for i in range(VARIANTS)]
    if render:
        for i, s in enumerate(splats):
            for o in splats:
                o.hide_render = o is not s
            bpy.context.scene.render.filepath = os.path.join(OUT, f"goo_splat_{i}.png")
            bpy.ops.render.render(write_still=True)
    # Lay them out in a row for viewing in the viewport.
    for i, s in enumerate(splats):
        s.hide_render = False
        s.location = ((i % 4) * 2.2 - 3.3, -(i // 4) * 2.2 + 1.1, 0) if show else (0, 0, 0)
    return [s.name for s in splats]


def normal_material():
    """Emits the surface normal as colour (tangent space as seen from the camera, OpenGL style): a normal map."""
    m = bpy.data.materials.get("GooNormal") or bpy.data.materials.new("GooNormal")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    xform = nt.nodes.new("ShaderNodeVectorTransform")
    xform.vector_type = "NORMAL"
    xform.convert_from = "WORLD"
    xform.convert_to = "CAMERA"
    mapr = nt.nodes.new("ShaderNodeVectorMath")
    mapr.operation = "MULTIPLY_ADD"
    mapr.inputs[1].default_value = (0.5, 0.5, -0.5)   # camera space looks down -Z: flip Z so facing us is +
    mapr.inputs[2].default_value = (0.5, 0.5, 0.5)
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(geo.outputs["Normal"], xform.inputs["Vector"])
    nt.links.new(xform.outputs["Vector"], mapr.inputs[0])
    nt.links.new(mapr.outputs["Vector"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    return m


def build_decals(count=6):
    """Puddle splats for floors and walls (no drips, flatter): goo_decal_N.png and goo_decal_N_n.png (normal map)."""
    coll = clear()
    mat = material()
    stage(coll)
    nmat = normal_material()
    scene = bpy.context.scene
    view = scene.view_settings.view_transform
    decals = [splat(200 + i, coll, mat, drips=False, flat=0.28) for i in range(count)]
    for i, d in enumerate(decals):
        for o in decals:
            o.hide_render = o is not d
        d.data.materials[0] = mat
        scene.view_settings.view_transform = view
        scene.render.filepath = os.path.join(OUT, f"goo_decal_{i}.png")
        bpy.ops.render.render(write_still=True)
        d.data.materials[0] = nmat
        scene.view_settings.view_transform = "Raw"
        scene.render.filepath = os.path.join(OUT, f"goo_decal_{i}_n.png")
        bpy.ops.render.render(write_still=True)
        d.data.materials[0] = mat
    scene.view_settings.view_transform = view
    for i, d in enumerate(decals):
        d.hide_render = False
        d.location = ((i % 3) * 2.2 - 2.2, -(i // 3) * 2.2 + 1.1, 0)
    return [d.name for d in decals]


if __name__ == "__main__":
    build()
    build_decals()
