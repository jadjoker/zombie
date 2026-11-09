"""
Particle sprites for Risk of Copyright (Godot): a water bubble and a sand/silt puff, rendered to transparent PNGs.
Run in Blender:  exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/sprites.py").read())
Writes godot/assets/textures/fx/bubble.png and puff.png (256 px). The puff renders grey: the particle colour
tints it in the shader (assets/shaders/sprite_puff.gdshader).
"""
import bpy
import math
import os

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/textures/fx"
COLL = "Sprites"
SIZE = 256


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


def bubble_material():
    """See-through in the middle, a bright cyan-white rim (fresnel), drawn as emission over transparency."""
    m = bpy.data.materials.get("SpriteBubble") or bpy.data.materials.new("SpriteBubble")
    m.use_nodes = True
    m.surface_render_method = "BLENDED"
    m.use_backface_culling = True   # only the near side: a clear middle
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    weight = nt.nodes.new("ShaderNodeLayerWeight")
    weight.inputs["Blend"].default_value = 0.35
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.45
    ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
    ramp.color_ramp.elements[1].position = 0.97
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (0.75, 0.95, 1.0, 1)
    emit.inputs["Strength"].default_value = 1.1
    glossy = nt.nodes.new("ShaderNodeBsdfGlossy")
    glossy.inputs["Roughness"].default_value = 0.02
    add = nt.nodes.new("ShaderNodeAddShader")
    clear = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(weight.outputs["Facing"], ramp.inputs["Fac"])
    nt.links.new(emit.outputs["Emission"], add.inputs[0])
    nt.links.new(glossy.outputs["BSDF"], add.inputs[1])
    nt.links.new(ramp.outputs["Color"], mix.inputs["Fac"])
    nt.links.new(clear.outputs["BSDF"], mix.inputs[1])
    nt.links.new(add.outputs["Shader"], mix.inputs[2])
    # A faint body so the middle isn't fully empty.
    body = nt.nodes.new("ShaderNodeMixShader")
    body.inputs["Fac"].default_value = 0.035
    nt.links.new(mix.outputs["Shader"], body.inputs[1])
    nt.links.new(add.outputs["Shader"], body.inputs[2])
    nt.links.new(body.outputs["Shader"], out.inputs["Surface"])
    return m


def puff_material():
    """A soft lumpy cloud: a volume with noisy density, lit from above, grey so the game can tint it."""
    m = bpy.data.materials.get("SpritePuff") or bpy.data.materials.new("SpritePuff")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Color"].default_value = (0.8, 0.8, 0.8, 1)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 2.2
    noise.inputs["Detail"].default_value = 4
    coords = nt.nodes.new("ShaderNodeTexCoord")
    grad = nt.nodes.new("ShaderNodeTexGradient")
    grad.gradient_type = "SPHERICAL"
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    sharpen = nt.nodes.new("ShaderNodeMapRange")
    sharpen.inputs["From Min"].default_value = 0.08
    sharpen.inputs["From Max"].default_value = 0.42
    sharpen.inputs["To Max"].default_value = 10.0
    nt.links.new(coords.outputs["Object"], noise.inputs["Vector"])
    nt.links.new(coords.outputs["Object"], grad.inputs["Vector"])
    nt.links.new(noise.outputs["Fac"], mul.inputs[0])
    nt.links.new(grad.outputs["Fac"], mul.inputs[1])
    nt.links.new(mul.outputs["Value"], sharpen.inputs["Value"])
    nt.links.new(sharpen.outputs["Result"], vol.inputs["Density"])
    nt.links.new(vol.outputs["Volume"], out.inputs["Volume"])
    return m


def stage(coll):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = SIZE
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    cam = bpy.data.objects.new("SpriteCam", bpy.data.cameras.new("SpriteCam"))
    coll.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.1
    cam.location = (0, 0, 5)
    scene.camera = cam
    key = bpy.data.objects.new("SpriteKey", bpy.data.lights.new("SpriteKey", "AREA"))
    coll.objects.link(key)
    key.location = (-2, 2.5, 4)
    key.data.energy = 400
    key.data.size = 1.0
    key.rotation_euler = (-key.location).to_track_quat("-Z", "Y").to_euler()
    return cam


def render(obj, others, name):
    for o in others:
        o.hide_render = o is not obj
    bpy.context.scene.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)


def build():
    os.makedirs(OUT, exist_ok=True)
    coll = collection()
    stage(coll)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=0.95, location=(0, 0, 0))
    bubble = bpy.context.active_object
    bubble.name = "Bubble"
    bubble.data.materials.append(bubble_material())
    bpy.ops.object.shade_smooth()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1.0, location=(0, 0, 0))
    puff = bpy.context.active_object
    puff.name = "Puff"
    puff.data.materials.append(puff_material())
    things = [bubble, puff]
    render(bubble, things, "bubble.png")
    render(puff, things, "puff.png")
    bubble.location = (-1.2, 0, 0)
    puff.location = (1.2, 0, 0)
    for o in things:
        o.hide_render = False
    return [o.name for o in things]


if __name__ == "__main__":
    build()
