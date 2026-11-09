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


def splat(seed, coll, mat):
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
        e.size_x, e.size_y, e.size_z = sx, sy, 0.45   # shape, relative to the radius

    # Body: a few overlapping lobes.
    for _ in range(rng.randint(5, 8)):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0, 0.2)
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.17, 0.27))
    # Splash streaks thrown outward from the impact: tapering arms, each ending in a drop.
    for _ in range(rng.randint(5, 9)):
        a = rng.uniform(0, math.tau)
        reach = rng.uniform(0.35, 0.62)
        steps = 14
        for k in range(steps):
            t = k / (steps - 1)
            d = 0.15 + t * reach
            ball(math.cos(a) * d, math.sin(a) * d, 0.07 * (1 - 0.7 * t) + 0.012)
        d = 0.15 + reach + 0.03
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.035, 0.06))
    # Satellite drops round the edge.
    for _ in range(rng.randint(4, 8)):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0.35, 0.6)
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.03, 0.07))
    # Drips running down: dense chains that melt into one smooth run, ending in a fat bead.
    for _ in range(rng.randint(1, 3)):
        x = rng.uniform(-0.2, 0.2)
        y = -0.1
        length = rng.uniform(0.3, 0.6)
        steps = 30
        wobble = rng.uniform(0, math.tau)
        for k in range(steps):
            t = k / (steps - 1)
            ball(x + 0.012 * math.sin(wobble + t * 7), y - t * length, 0.05 * (1 - 0.3 * t), sx=0.85)
        ball(x, y - length - 0.03, 0.075)
    # Flung specks, further out.
    for _ in range(rng.randint(8, 16)):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0.6, 0.9)
        ball(math.cos(a) * d, math.sin(a) * d, rng.uniform(0.012, 0.03))

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


if __name__ == "__main__":
    build()
