# Renders creature parts wearing a creature skin texture set, for comparing skins side by side.
# Headless: blender -b --factory-startup --python Tools/blender/skin_preview.py -- <out.png> <skin> [<skin> ...]
# Each skin gets one row: crab body, crab leg, crab pincer, bloober head, bloober segment, squilien bell.
import bpy, sys, math
from mathutils import Vector

ROOT = "C:/Users/smitt/Documents/zombie_survival/Assets/"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "C:/tmp/skins.png"
SKINS = argv[1:] or ["scales", "chitin"]
# (model, tint, scale of the texture coords like the vmat's g_vTexCoordScale)
PARTS = [
    ("crab/body", (0.62, 0.2, 0.08)),
    ("crab/leg", (0.62, 0.2, 0.08)),
    ("crab/pincer", (0.62, 0.2, 0.08)),
    ("bloober/head", (0.3, 0.04, 0.26)),
    ("bloober/segment0", (0.3, 0.04, 0.26)),
    ("squilien/bell", (0.1, 0.45, 0.55)),
]
SKIN_FOR = {"crab": None, "bloober": None, "squilien": None}   # None = use the row's skin

bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
sc = bpy.context.scene


def material(skin, tint):
    m = bpy.data.materials.new(f"{skin}_{tint}")
    m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    uv = nt.nodes.new("ShaderNodeTexCoord"); mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (2, 1, 1)
    nt.links.new(uv.outputs["UV"], mp.inputs["Vector"])
    def tex(kind, non_color):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(f"{ROOT}materials/creature/{skin}_{kind}.png", check_existing=True)
        if non_color: t.image.colorspace_settings.name = "Non-Color"
        nt.links.new(mp.outputs["Vector"], t.inputs["Vector"])
        return t
    col = tex("color", False)
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(col.outputs["Color"], mul.inputs[6]); mul.inputs[7].default_value = (*tint, 1)
    nt.links.new(mul.outputs[2], b.inputs["Base Color"])
    nt.links.new(tex("rough", True).outputs["Color"], b.inputs["Roughness"])
    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = 1.0
    nt.links.new(tex("normal", True).outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Coat Weight"].default_value = 0.15
    return m


def place(model, mat, at):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=f"{ROOT}models/creatures/{model}.fbx", axis_forward='X', axis_up='Z', global_scale=100, use_manual_orientation=True)
    objs = [o for o in set(bpy.data.objects) - before if o.type == 'MESH']
    for o in objs:
        o.data.materials.clear(); o.data.materials.append(mat)
        bpy.context.view_layer.objects.active = o; o.select_set(True)
        bpy.ops.object.shade_smooth(); o.select_set(False)
    # centre each part on its slot
    lo = Vector((min(min((o.matrix_world @ Vector(c)).x for c in o.bound_box) for o in objs),
                 min(min((o.matrix_world @ Vector(c)).y for c in o.bound_box) for o in objs),
                 min(min((o.matrix_world @ Vector(c)).z for c in o.bound_box) for o in objs)))
    hi = Vector((max(max((o.matrix_world @ Vector(c)).x for c in o.bound_box) for o in objs),
                 max(max((o.matrix_world @ Vector(c)).y for c in o.bound_box) for o in objs),
                 max(max((o.matrix_world @ Vector(c)).z for c in o.bound_box) for o in objs)))
    size = max(hi - lo)
    s = 40.0 / max(size, 1e-3)
    for o in objs:
        o.location = (o.location - (lo + hi) / 2) * s + Vector(at)
        o.scale = o.scale * s
        o.rotation_euler.z += math.radians(25)


for r, skin in enumerate(SKINS):
    for c, (model, tint) in enumerate(PARTS):
        place(model, material(skin, tint), (0, c * 50 - 125, -r * 50 + 25 * (len(SKINS) - 1)))

cam_d = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_d); sc.collection.objects.link(cam)
cam_d.type = 'ORTHO'; cam_d.ortho_scale = 320
cam.location = (300, -60, 90); cam.rotation_euler = (Vector((0, 0, 0)) - Vector(cam.location)).to_track_quat('-Z', 'Y').to_euler()
sc.camera = cam
for name, loc, e, colr in (("key", (250, -250, 300), 5, (1, 0.96, 0.9)), ("rim", (-300, 200, 150), 3, (0.7, 0.85, 1))):
    ld = bpy.data.lights.new(name, 'SUN'); ld.energy = e; ld.color = colr
    l = bpy.data.objects.new(name, ld); sc.collection.objects.link(l)
    l.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
w = sc.world or bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.32, 0.36, 0.4, 1)
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x = 1800; sc.render.resolution_y = int(300 * len(SKINS) + 100)
sc.view_settings.view_transform = 'AgX'
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
