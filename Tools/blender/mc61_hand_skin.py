# MC61 skinned hands: ONE smooth watertight mesh per hand (palm, fingers, thumb, cuff) with a small skeleton,
# so the game can bend every finger as a continuous surface instead of stacking rigid pieces.
# Exports Assets/models/creatures/mc61g/hand_l.fbx and hand_r.fbx (with armature) and writes the bone layout json.
# Pose: palm faces +X, fingers point +Z, thumb splayed outward 38 degrees, as the code expects.
import math, json
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())
import bmesh
from mathutils import Vector

C = "mc61g"
FZ = 9.2                                    # knuckle height
FY = (-3.9, -1.3, 1.3, 3.9)                 # finger roots across the hand
FS = (0.93, 1.0, 0.95, 0.8)                 # finger length scale: index, middle, ring, pinky
SEG = (3.7, 3.1, 2.7)                       # proximal, middle, distal lengths (before FS)
R0 = (1.3, 1.18, 1.06, 0.88)                   # radius at the joints 0, 1, 2 and the tip
THUMB_BASE = 4.0                            # thumb root offset across the palm (times side)
THUMB_Z = 4.2
THUMB_SPLAY = math.radians(38)
T_SEG = (3.5, 3.0)
T_R = (1.45, 1.3, 1.05)


def smooth01(x, a, b):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def finger_joints(i):
    s = FS[i]
    j0 = Vector((0, FY[i], FZ))
    j1 = j0 + Vector((0, 0, SEG[0] * s))
    j2 = j1 + Vector((0, 0, SEG[1] * s))
    tip = j2 + Vector((0, 0, SEG[2] * s))
    return j0, j1, j2, tip


def thumb_joints(side):
    t0 = Vector((0, side * THUMB_BASE, THUMB_Z))
    d = Vector((0, side * math.sin(THUMB_SPLAY), math.cos(THUMB_SPLAY)))
    t1 = t0 + d * T_SEG[0]
    tip = t1 + d * T_SEG[1]
    return t0, t1, tip, d


def build_mesh(side):
    clear()
    # A slimmer, flatter palm that narrows toward the wrist (it read as swollen before).
    p = [ellipsoid("palm", (0, 0, 5.9), (2.0, 4.6, 3.9), 40),
         ellipsoid("heel", (0.1, 0, 3.0), (2.1, 3.5, 2.6), 32),
         cyl("cuff", (0, 0, -1.2), 3.1, 3.4, 3.6, verts=40),
         ellipsoid("cuffrim", (0, 0, -3.0), (3.4, 3.4, 0.7), 32)]   # no knuckle shelf: fingers grow straight out of the palm
    for i in range(4):
        j0, j1, j2, tip = finger_joints(i)
        rad = [r * (1.0 if i < 3 else 1.0) for r in R0]
        p += [tube("f", (j0.x, j0.y, j0.z - 0.5), j1, rad[0], rad[1], verts=20), ellipsoid("k", j1, (rad[1],) * 3, 16),
              tube("f", j1, j2, rad[1], rad[2], verts=20), ellipsoid("k", j2, (rad[2],) * 3, 16),
              tube("f", j2, tip, rad[2], rad[3], verts=20), ellipsoid("t", tip, (rad[3],) * 3, 16)]
    t0, t1, tip, d = thumb_joints(side)
    p += [ellipsoid("thumbpad", (0.4, side * 3.6, 4.6), (1.6, 1.9, 2.3), 24),
          tube("t", t0 - d * 0.6, t1, T_R[0], T_R[1], verts=20), ellipsoid("k", t1, (T_R[1],) * 3, 16),
          tube("t", t1, tip, T_R[1], T_R[2], verts=20), ellipsoid("t", tip, (T_R[2],) * 3, 16)]
    return fuse(p, f"mc61_hand_{'l' if side > 0 else 'r'}", voxel=0.11, smooth=6, decimate=0.1)


BONES = ["wrist"] + [f"f{i}_{j}" for i in range(4) for j in range(3)] + ["th_0", "th_1"]


def weights_for(v, side):
    """Smooth skin weights: no hard region edges anywhere (hard edges tear when a finger bends), blended in y between
    neighbouring fingers, along each finger's length at its joints, and between palm and finger at the knuckle line."""
    # --- fingers ---
    infl = [math.exp(-((v.y - FY[i]) / 1.5) ** 2) for i in range(4)]
    total = sum(infl)
    fingerw = {}
    blend = 0.0
    if total > 1e-4:
        z = v.z - FZ
        sm0 = smooth01(z, -3.2, 2.2)
        blend = sm0 * min(1.0, total * 1.6)
        for i in range(4):
            share = infl[i] / total
            s_ = FS[i]
            sm1 = smooth01(z, SEG[0] * s_ - 1.4, SEG[0] * s_ + 1.4)
            sm2 = smooth01(z, (SEG[0] + SEG[1]) * s_ - 1.3, (SEG[0] + SEG[1]) * s_ + 1.3)
            fingerw[f"f{i}_0"] = share * blend * (1 - sm1)
            fingerw[f"f{i}_1"] = share * blend * sm1 * (1 - sm2)
            fingerw[f"f{i}_2"] = share * blend * sm1 * sm2
    # --- thumb ---
    t0, t1, tip, d = thumb_joints(side)
    rel = v - t0
    along = rel.dot(d)
    radial = (rel - d * along).length
    tb = math.exp(-(radial / 2.1) ** 2) * smooth01(side * v.y, 2.4, 3.8) * smooth01(along, -2.0, 1.3)
    sm1t = smooth01(along, T_SEG[0] - 0.9, T_SEG[0] + 0.9)
    w = {}
    for name, val in fingerw.items():
        w[name] = val * (1 - tb)
    w["th_0"] = tb * (1 - sm1t)
    w["th_1"] = tb * sm1t
    w["wrist"] = max(0.0, 1.0 - sum(w.values()))
    return w


def make_armature(side):
    arm_data = bpy.data.armatures.new("hand_arm")
    arm = bpy.data.objects.new("hand_arm", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def bone(name, head, tail, parent=None):
        b = eb.new(name)
        b.head = head; b.tail = tail
        b.roll = 0.0
        if parent is not None:
            b.parent = eb[parent]
        return b

    bone("wrist", Vector((0, 0, -2.0)), Vector((0, 0, 3.0)))
    for i in range(4):
        j0, j1, j2, tip = finger_joints(i)
        bone(f"f{i}_0", j0, j1, "wrist")
        bone(f"f{i}_1", j1, j2, f"f{i}_0")
        bone(f"f{i}_2", j2, tip, f"f{i}_1")
    t0, t1, tip, d = thumb_joints(side)
    bone("th_0", t0, t1, "wrist")
    bone("th_1", t1, tip, "th_0")
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def export_hand(side):
    mesh = build_mesh(side)
    arm = make_armature(side)
    for name in BONES:
        mesh.vertex_groups.new(name=name)
    for v in mesh.data.vertices:
        for name, wt in weights_for(v.co.copy(), side).items():
            if wt > 0.001:
                mesh.vertex_groups[name].add([v.index], wt, 'REPLACE')
    # The engine only keeps a few influences per vertex: trim to the 4 strongest and renormalise, otherwise the extras
    # are dropped unevenly and the skin tears near the knuckles.
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    mesh.parent = arm
    mod = mesh.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
    bpy.ops.object.shade_smooth()
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02); bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    out_dir = f"{ROOT}/{C}"
    os.makedirs(out_dir, exist_ok=True)
    name = "hand_l" if side > 0 else "hand_r"
    bpy.ops.export_scene.fbx(filepath=f"{out_dir}/{name}.fbx", use_selection=True, global_scale=0.01, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_NONE', axis_forward='X', axis_up='Z', bake_space_transform=True,
                             object_types={'MESH', 'ARMATURE'}, mesh_smooth_type='FACE', add_leaf_bones=False,
                             bake_anim=False, use_armature_deform_only=False)
    return len(mesh.data.polygons)


stats = {}
stats["hand_l"] = export_hand(1)
stats["hand_r"] = export_hand(-1)
layout = {
    "finger_y": list(FY), "finger_z": FZ, "finger_scale": list(FS), "finger_segs": list(SEG),
    "thumb_base": THUMB_BASE, "thumb_z": THUMB_Z, "thumb_splay_deg": math.degrees(THUMB_SPLAY), "thumb_segs": list(T_SEG),
}
json.dump(layout, open(r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/mc61g_hand_layout.json", "w"))
result = stats
