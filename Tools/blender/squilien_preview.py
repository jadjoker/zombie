# Assembles the exported Squilien bell and eyes (as the game places them) and renders a preview PNG.
import bpy, math, mathutils
from mathutils import Matrix, Vector
D = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/squilien/"
OUT = globals().get("PREVIEW_OUT", r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/squilien_preview.png")
CAM = globals().get("PREVIEW_CAM", (120, -20, 10))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
cols = {"Body": (0.25, 0.08, 0.32, 1), "Eye": (0.01, 0.01, 0.015, 1), "Tent": (0.2, 0.06, 0.26, 1)}
mats = {}
for k, c in cols.items():
    m = bpy.data.materials.get(k) or bpy.data.materials.new(k); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs[0].default_value = c
    if k == "Eye": b.inputs["Roughness"].default_value = 0.08
    mats[k] = m
def place(name, mtx, col):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=D + name + ".fbx", axis_forward='X', axis_up='Z', global_scale=100, use_manual_orientation=True)
    for o in set(bpy.data.objects) - before:
        o.data.materials.clear(); o.data.materials.append(mats[col]); o.matrix_world = mtx @ o.matrix_world
place("bell", Matrix.Identity(4), "Body")
R = 32
for side, n in ((1, "eye_l"), (-1, "eye_r")):
    d = Vector((0.82, side * 0.52, 0.14)).normalized()
    pos = d * R * 0.96
    rot = d.to_track_quat('X', 'Z').to_matrix().to_4x4()
    place(n, Matrix.Translation(pos) @ rot, "Eye")
cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data); bpy.context.scene.collection.objects.link(cam)
cam.location = CAM; cam.rotation_euler = (Vector((0, 0, 0)) - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam_data.lens = 50
bpy.context.scene.camera = cam
ld = bpy.data.lights.new("sun", 'SUN'); ld.energy = 4; l = bpy.data.objects.new("sun", ld); bpy.context.scene.collection.objects.link(l); l.rotation_euler = (0.9, 0.2, 0.6)
w = bpy.context.scene.world or bpy.data.worlds.new("w"); bpy.context.scene.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.4, 0.6, 0.7, 1)
s = bpy.context.scene; s.render.resolution_x = 800; s.render.resolution_y = 700; s.render.filepath = OUT
bpy.ops.render.render(write_still=True)
