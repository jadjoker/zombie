# Assembles the exported MC61 pieces at their in-game pivots and renders a preview PNG.
import bpy, math, mathutils
from mathutils import Matrix
D = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/mc61/"
OUT = globals().get("PREVIEW_OUT", r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/mc61_preview.png")
CAM = globals().get("PREVIEW_CAM", (190, -110, 10))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
cols = {"Tux": (0.03, 0.03, 0.04, 1), "Glove": (0.95, 0.95, 0.92, 1), "Red": (0.9, 0.05, 0.1, 1), "Skin": (0.42, 0.47, 0.4, 1),
        "Brown": (0.32, 0.16, 0.07, 1), "Gold": (0.85, 0.7, 0.2, 1), "Dark": (0.2, 0.03, 0.08, 1), "Iris": (1, 0.12, 0.08, 1), "Blk": (0, 0, 0, 1)}
mats = {}
for k, c in cols.items():
    m = bpy.data.materials.get(k) or bpy.data.materials.new(k); m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs[0].default_value = c; mats[k] = m
def T(x, y, z): return Matrix.Translation((x, y, z))
def R(roll): return Matrix.Rotation(math.radians(roll), 4, 'X')
def place(name, mtx, col):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=D + name + ".fbx", axis_forward='X', axis_up='Z', global_scale=100, use_manual_orientation=True)
    for o in set(bpy.data.objects) - before:
        o.data.materials.clear(); o.data.materials.append(mats[col]); o.matrix_world = mtx @ o.matrix_world
def sph(at, r, col):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=at); e = bpy.context.active_object
    e.scale = r; e.data.materials.append(mats[col]); bpy.ops.object.shade_smooth()
for n, c in (("torso_jacket", "Tux"), ("torso_shirt", "Glove"), ("torso_bowtie", "Red")): place(n, Matrix.Identity(4), c)
head = T(0, 0, 20); place("head", head, "Skin")
hat = head @ T(-1, 0, 35) @ R(9); place("hat_body", hat, "Tux"); place("hat_band", hat, "Red")
for side, g in ((1, "glove_l"), (-1, "glove_r")):
    arm = T(0, side * 17, 12) @ R(-side * 12); place("arm_sleeve", arm, "Tux")
    fore = arm @ T(0, 0, -20); place("forearm_sleeve", fore, "Tux"); place("forearm_cuff", fore, "Glove"); place(g, fore, "Glove")
    if side < 0:
        case = fore @ T(0, 0, -27)
        for n, c in (("case_body", "Brown"), ("case_handle", "Tux"), ("case_metal", "Gold")): place(n, case, c)
        place("case_lid", case @ T(3.6, 0, -4), "Brown")
    leg = T(0, side * 7, -18); place("leg_trouser", leg, "Glove"); shin = leg @ T(0, 0, -22)
    place("shin_trouser", shin, "Glove"); place("shoe", shin, "Tux")
sph((15.5, 0, 41), (5, 9.5, 9.5), "Glove"); sph((19.1, 0, 41), (2, 5.5, 5.5), "Iris"); sph((20.5, 0, 41), (1.2, 2.8, 2.8), "Blk")
sph((15.5, 0, 28.5), (3, 8.5, 3.4), "Dark")
cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data); bpy.context.scene.collection.objects.link(cam)
cam.location = CAM; cam.rotation_euler = (mathutils.Vector((0, 0, 5)) - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam_data.lens = 45
bpy.context.scene.camera = cam
ld = bpy.data.lights.new("sun", 'SUN'); ld.energy = 4; l = bpy.data.objects.new("sun", ld); bpy.context.scene.collection.objects.link(l); l.rotation_euler = (0.7, 0.3, 0.8)
w = bpy.context.scene.world or bpy.data.worlds.new("w"); bpy.context.scene.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.4, 0.6, 0.7, 1)
s = bpy.context.scene; s.render.resolution_x = 700; s.render.resolution_y = 900; s.render.filepath = OUT
bpy.ops.render.render(write_still=True)
