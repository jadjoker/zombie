# Assembles the exported mc61g ghost parts and renders a preview PNG.
import bpy, math, mathutils
from mathutils import Matrix, Euler
D = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/mc61g/"
OUT = globals().get("PREVIEW_OUT", r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/mc61_ghost_preview.png")
CAM = globals().get("PREVIEW_CAM", (190, -140, 50))
glow = globals().get("PREVIEW_GLOW", 0.0)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
cols = {"White": (0.7, 0.78, 0.93, 1), "Blk": (0.02, 0.02, 0.03, 1), "Glove": (1, 1, 1, 1), "Eye": (1, 1, 1, 1)}
mats = {}
for k, c in cols.items():
    m = bpy.data.materials.get(k) or bpy.data.materials.new(k); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs[0].default_value = c; b.inputs["Roughness"].default_value = 0.5
    mats[k] = m
eye = mats["Eye"].node_tree.nodes["Principled BSDF"]
eye.inputs[0].default_value = (1, 1 - glow, 1 - glow, 1)
eye.inputs["Emission Color"].default_value = (1, 1, 1, 1); eye.inputs["Emission Strength"].default_value = 0.9   # the eye reads as bright white
if glow > 0:
    eye.inputs["Emission Color"].default_value = (1, 0.05, 0.02, 1); eye.inputs["Emission Strength"].default_value = 6 * glow
def T(x, y, z): return Matrix.Translation((x, y, z))
def RX(d): return Matrix.Rotation(math.radians(d), 4, 'X')
def RY(d): return Matrix.Rotation(math.radians(d), 4, 'Y')
def RZ(d): return Matrix.Rotation(math.radians(d), 4, 'Z')
def place(name, mtx, col):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=D + name + ".fbx", axis_forward='X', axis_up='Z', global_scale=100, use_manual_orientation=True)
    for o in set(bpy.data.objects) - before:
        o.data.materials.clear(); o.data.materials.append(mats[col]); o.matrix_world = mtx @ o.matrix_world
    return list(set(bpy.data.objects) - before)
place("body", Matrix.Identity(4), "White")
place("front", Matrix.Identity(4), "Blk"); place("bow", Matrix.Identity(4), "Blk"); place("jacket", Matrix.Identity(4), "Blk")
import json
LAY = json.load(open(r'C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/mc61g_layout.json'))
EP = LAY['eye']
eyem = T(*EP)
eye_objs = place("eyeball", eyem, "Eye")
EYE_FRAME = globals().get("EYE_FRAME")
if EYE_FRAME:
    # 2D eye: a picture drawn per frame, mapped onto the eyeball dome (this is what the game would do live)
    import numpy as np
    look, lid, gl = EYE_FRAME
    W, H = 512, 384
    uu, vv = np.meshgrid(np.linspace(0, 1, W), np.linspace(0, 1, H))
    nx, ny = (uu - 0.5) * 2, (vv - 0.5) * 2
    img = np.ones((H, W, 4), dtype=np.float32)
    base = np.array([1.0, 1.0 - 0.9 * gl, 1.0 - 0.92 * gl])
    img[..., :3] = base
    # soft red veins creeping in from the edge as it charges
    rr = np.sqrt(nx ** 2 + ny ** 2)
    img[..., 0] = np.clip(img[..., 0] - 0.0, 0, 1)
    veins = np.clip((rr - 0.55) * 1.6, 0, 1) * gl * 0.7
    img[..., 1] -= veins * 0.5; img[..., 2] -= veins * 0.5
    px, py = look[0] * 0.38, look[1] * 0.3
    pup = ((nx - px) / 0.3) ** 2 + ((ny - py) / 0.5) ** 2 < 1
    img[pup, :3] = (0.02, 0.02, 0.03) if gl < 0.5 else (0.25, 0.0, 0.0)
    hi = ((nx - px + 0.1) / 0.07) ** 2 + ((ny - py - 0.2) / 0.09) ** 2 < 1
    img[hi, :3] = 1.0
    cover = ny > (1 - 2.1 * lid)                      # upper lid sweeping down
    edge = np.abs(ny - (1 - 2.1 * lid)) < 0.05
    img[cover & (lid > 0.02), :3] = (0.7, 0.78, 0.93)
    img[edge & (lid > 0.02), :3] = (0.02, 0.02, 0.03)
    im = bpy.data.images.new("eye_frame", W, H, alpha=True)
    im.pixels.foreach_set(img.ravel()); im.update()
    for o in eye_objs:
        m = bpy.data.materials.new("eye_tex"); m.use_nodes = True
        nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = im
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"]); bsdf.inputs["Emission Strength"].default_value = 0.6 + 1.4 * gl
        o.data.materials.clear(); o.data.materials.append(m)
if not EYE_FRAME:
    place("pupil", eyem, "Blk")
place("lids", eyem, "Blk")
place("hat", T(*LAY["hat_pos"]) @ Euler([math.radians(a) for a in LAY["hat_euler_deg"]], "XYZ").to_matrix().to_4x4(), "Blk")
# tail: hangs from the body base and curls to one side, like the sketch
m = T(-16.6, 0, -25)
for i, (yaw, pit) in enumerate(((0, 42), (0, 52), (0, 58), (0, 60))):
    m = m @ RX(-14) @ RY(pit * 1.0 if i == 0 else 12)
    place(f"tail{i}", m, "White")
    m = m @ T(0, 0, -10)
HL = globals().get("HAND_POSE_L", "open"); HR = globals().get("HAND_POSE_R", "open")
POSES = {
    #        index        middle       ring         pinky       thumb
    "open": [(8, 6, 4), (6, 5, 3), (8, 6, 4), (12, 8, 5), (-10, 8)],
    "fist": [(85, 95, 60), (88, 98, 62), (85, 95, 60), (80, 92, 58), (35, 40)],
    "point": [(4, 2, 2), (88, 98, 62), (86, 96, 60), (82, 94, 58), (30, 20)],
    "pinch": [(52, 50, 35), (30, 28, 20), (20, 24, 18), (22, 24, 18), (40, 24)],
    "wave": [(20, 14, 10), (16, 12, 8), (22, 14, 10), (28, 16, 12), (-18, 10)],
}
LAYH = LAY
def hand(base, side, pose):
    place("palm_l" if side > 0 else "palm_r", base, "Glove")
    P = POSES[pose]
    for i, y in enumerate(LAYH["finger_y"]):
        m = base @ T(0, y, LAYH["finger_z"])
        for j, nm in enumerate(("f_prox", "f_mid", "f_dist")):
            m = m @ RY(P[i][j]); place(nm, m, "Glove"); m = m @ T(0, 0, LAYH["finger_segs"][j][0])
    m = base @ T(0, side * 4.7, 4.4) @ RX(-side * 38)
    for j, nm in enumerate(("t_base", "t_tip")):
        m = m @ RY(P[4][j] * 1.0); place(nm, m, "Glove"); m = m @ T(0, 0, LAYH["thumb"][j][0])
hand(T(4, 40, 12) @ RX(-28) @ RZ(10), 1, HL)
hand(T(8, -38, 24) @ RX(30) @ RZ(-10), -1, HR)
cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data); bpy.context.scene.collection.objects.link(cam)
cam.location = CAM; cam.rotation_euler = (mathutils.Vector((0, 0, 20)) - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam_data.lens = 45
bpy.context.scene.camera = cam
ld = bpy.data.lights.new("sun", 'SUN'); ld.energy = 4; l = bpy.data.objects.new("sun", ld); bpy.context.scene.collection.objects.link(l); l.rotation_euler = (0.7, 0.3, 0.8)
w = bpy.context.scene.world or bpy.data.worlds.new("w"); bpy.context.scene.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.5, 0.62, 1)
s = bpy.context.scene; s.render.resolution_x = 800; s.render.resolution_y = 800; s.render.filepath = OUT
bpy.ops.render.render(write_still=True)
