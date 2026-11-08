# Assembles the exported Sea Spider parts in the game's rest pose and renders a preview PNG.
#   blender -b --factory-startup --python spider_preview.py -- <out.png> <cam x> <cam y> <cam z>
import bpy, math, sys
from mathutils import Matrix, Vector

D = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/spider/"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = args[0] if args else r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/spider_preview.png"
CAM = Vector((float(args[1]), float(args[2]), float(args[3]))) if len(args) >= 4 else Vector((150, -170, 90))

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()


def material(name, color, rough=0.45):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs[0].default_value = color
    b.inputs["Roughness"].default_value = rough
    return m


shell = material("shell", (0.85, 0.74, 0.76, 1))
dark = material("dark", (0.42, 0.22, 0.38, 1))
white = material("white", (0.98, 0.96, 0.84, 1), 0.2)
black = material("black", (0.01, 0.01, 0.015, 1), 0.1)
floor_m = material("floor", (0.55, 0.55, 0.48, 1), 0.9)


def place(name, mtx, mat):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=D + name + ".fbx", axis_forward='X', axis_up='Z', global_scale=100, use_manual_orientation=True)
    for o in set(bpy.data.objects) - before:
        o.data.materials.clear()
        o.data.materials.append(mat)
        o.matrix_world = mtx @ o.matrix_world
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.shade_smooth()


BODY_Z = 42.0
place("body", Matrix.Translation((0, 0, BODY_Z)), shell)

# Eye, as the game builds it (ball, big pupil, no lids here).
bpy.ops.mesh.primitive_uv_sphere_add(radius=8.5, segments=48, ring_count=24, location=(12, 0, BODY_Z + 4.5))
eye = bpy.context.active_object
eye.data.materials.append(white)
bpy.ops.object.shade_smooth()
bpy.ops.mesh.primitive_uv_sphere_add(radius=1, segments=32, ring_count=16, location=(12 + 8.5 * 0.9, 0, BODY_Z + 4.5))
pupil = bpy.context.active_object
pupil.scale = (0.9, 3.6, 4.1)
pupil.data.materials.append(black)
bpy.ops.object.shade_smooth()

for yaw in (48, 92, 132, -48, -92, -132):
    out = Vector((math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), 0))
    hip = Vector((0, 0, BODY_Z)) + out * 12 + Vector((0, 0, 2))
    thigh_dir = (out * 0.62 + Vector((0, 0, 0.78))).normalized()
    shin_dir = (out * 0.42 + Vector((0, 0, -0.9))).normalized()
    t_rot = thigh_dir.to_track_quat('X', 'Z').to_matrix().to_4x4()
    knee = hip + thigh_dir * 22
    s_rot = shin_dir.to_track_quat('X', 'Z').to_matrix().to_4x4()
    place("thigh", Matrix.Translation(hip) @ t_rot, shell)
    place("shin", Matrix.Translation(knee) @ s_rot, dark)

bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, -1))
bpy.context.active_object.data.materials.append(floor_m)

cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
cam.location = CAM
target = Vector((0, 0, 38))
cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 55
bpy.context.scene.camera = cam
for rot, energy in (((0.9, 0.2, 0.6), 3.5), ((1.2, -0.3, -2.0), 1.5)):
    ld = bpy.data.lights.new("sun", 'SUN')
    ld.energy = energy
    l = bpy.data.objects.new("sun", ld)
    bpy.context.scene.collection.objects.link(l)
    l.rotation_euler = rot
w = bpy.data.worlds.new("w")
bpy.context.scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.62, 0.72, 1)
s = bpy.context.scene
s.render.engine = 'BLENDER_EEVEE_NEXT' if hasattr(bpy.types, "SceneEEVEE") and 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
s.render.resolution_x = 1000
s.render.resolution_y = 720
s.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("rendered", OUT)
