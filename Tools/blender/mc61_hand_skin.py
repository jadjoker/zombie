# MC61 skinned hands: ONE smooth watertight mesh per hand (palm, fingers, thumb, cuff) with a small skeleton,
# so the game can bend every finger as a continuous surface instead of stacking rigid pieces.
# Exports Assets/models/creatures/mc61g/hand_l.fbx and hand_r.fbx (with armature) and writes the bone layout json.
# Pose: palm faces +X, fingers point +Z, thumb splayed outward 38 degrees, as the code expects.
import math, json
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())
import bmesh
from mathutils import Vector

C = "mc61g"
# A natural glove hand: a flat palm that narrows to the wrist, long tapering fingers on an arched knuckle line,
# a thumb that grows from low on the palm side, soft tendon ridges on the back. Finger order: index, middle, ring, pinky.
FYB = (3.6, 1.2, -1.2, -3.45)             # finger roots across the hand (times side: the index sits next to the thumb)
FZS = (8.6, 9.0, 8.7, 8.0)                 # knuckle heights: middle highest, pinky lowest
FS = (0.95, 1.0, 0.95, 0.78)                # finger length scale
SEG = (4.5, 3.0, 2.5)                       # proximal, middle, distal lengths (before FS)
R0 = (1.12, 1.02, 0.92, 0.8)                # radius at the joints 0, 1, 2 and the tip
THUMB_BASE = 3.0                            # thumb root offset across the palm (times side)
THUMB_Z = 3.8
THUMB_SPLAY = math.radians(38)
T_SEG = (3.6, 2.9)
T_R = (1.35, 1.15, 0.92)


def smooth01(x, a, b):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def finger_joints(i, side):
    s = FS[i]
    j0 = Vector((0, side * FYB[i], FZS[i]))
    j1 = j0 + Vector((0, 0, SEG[0] * s))
    j2 = j1 + Vector((0, 0, SEG[1] * s))
    tip = j2 + Vector((0, 0, SEG[2] * s))
    return j0, j1, j2, tip


def thumb_joints(side):
    t0 = Vector((0.5, side * THUMB_BASE, THUMB_Z))
    d = Vector((0, side * math.sin(THUMB_SPLAY), math.cos(THUMB_SPLAY)))
    t1 = t0 + d * T_SEG[0]
    tip = t1 + d * T_SEG[1]
    return t0, t1, tip, d


MB_K = 0.574   # a metaball's visible radius is this times its influence radius (stiffness 2, threshold 0.6)


def build_mesh(side):
    """One metaball field for the whole glove, so palm, fingers, thumb and wrist melt into each other with no seams,
    corners or shelves (a voxel union of primitives always left creases where they met)."""
    clear()
    mb = bpy.data.metaballs.new("glove")
    mb.resolution = 0.09; mb.render_resolution = 0.09; mb.threshold = 0.6

    def blob(c, r):                      # ellipsoid with visible radii r
        e = mb.elements.new(type='ELLIPSOID'); e.co = c; e.radius = 1.0 / MB_K; e.stiffness = 2.0
        e.size_x, e.size_y, e.size_z = r
        return e

    def capsule(a, b, r):                # a rounded rod from a to b with visible radius r
        a, b = Vector(a), Vector(b)
        e = mb.elements.new(type='CAPSULE'); e.co = (a + b) / 2; e.radius = r / MB_K; e.stiffness = 2.0
        e.size_x = (b - a).length / 2
        e.rotation = Vector((1, 0, 0)).rotation_difference((b - a).normalized())
        return e

    def rod(a, b, r0, r1, flat=1.0):     # a smooth tapered rod: a dense chain of balls (each joint bulged when built from capsules)
        a, b = Vector(a), Vector(b)
        L = (b - a).length
        t = 0.0
        while True:
            r = r0 + (r1 - r0) * min(t / L, 1.0)
            e = mb.elements.new(type='ELLIPSOID'); e.co = a + (b - a) * min(t / L, 1.0)
            e.radius = r / (MB_K * 1.33); e.stiffness = 2.0
            e.size_x, e.size_y, e.size_z = flat, 1.0, 1.0
            if t >= L: break
            t = min(L, t + 0.25 * r)

    # palm: flat and wide at the knuckles, narrowing to the wrist; the back is slightly domed
    blob((0, 0, 6.4), (0.95, 3.6, 2.2))
    blob((0, 0, 4.3), (0.95, 3.1, 2.0))
    blob((-0.15, 0, 5.4), (1.0, 2.8, 2.6))
    capsule((0, side * 3.3, 7.2), (0, side * -3.0, 7.2), 0.95)        # knuckle bar the fingers grow from
    blob((0, 0, 1.6), (1.25, 2.6, 1.9))                               # wrist
    for k in range(9):                                                # glove cuff: a short flared tube
        z = -3.0 + k * 0.4
        blob((0, 0, z), (1.75 + 0.08 * (8 - k) / 8, 2.75 + 0.12 * (8 - k) / 8, 0.45))
    blob((0.45, side * -2.3, 3.6), (0.75, 1.3, 2.2))                  # heel pad under the pinky
    for i in range(4):
        j0, j1, j2, tip = finger_joints(i, side)
        r = [x * (1.0 if i < 3 else 0.88) for x in R0]
        rod((0, j0.y * 0.92, j0.z - 1.8), tip - Vector((0, 0, r[3] * 0.6)), r[0], r[3], flat=0.92)
        # soft tendon ridge on the back of the hand
        capsule((-0.85, side * FYB[i] * 0.4, 3.2), (-0.85, side * FYB[i] * 0.9, j0.z - 1.0), 0.32)
    t0, t1, tip, d = thumb_joints(side)
    blob((0.35, side * 2.2, 4.0), (0.95, 1.5, 2.1))                   # thenar pad, the meat at the thumb's base
    rod(t0 - d * 0.6, tip - d * (T_R[2] * 0.6), T_R[0], T_R[2], flat=0.9)
    obj = bpy.data.objects.new("glove", mb)
    bpy.context.scene.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    obj = bpy.context.active_object
    return fuse([obj], f"mc61_hand_{'l' if side > 0 else 'r'}", voxel=0.2, smooth=2)   # even quads, no decimate: long decimated triangles spanning a joint fold into sheets when the finger bends


BONES = ["wrist"] + [f"f{i}_{j}" for i in range(4) for j in range(3)] + ["th_0", "th_1"]


def weights_for(v, side):
    """Smooth skin weights: no hard region edges anywhere (hard edges tear when a finger bends), blended in y between
    neighbouring fingers, along each finger's length at its joints, and between palm and finger at the knuckle line."""
    infl = [math.exp(-((v.y - side * FYB[i]) / 1.4) ** 2) for i in range(4)]
    total = sum(infl)
    fingerw = {}
    if total > 1e-4:
        reach = min(1.0, total * 1.6)
        for i in range(4):
            share = infl[i] / total
            s_ = FS[i]
            z = v.z - FZS[i]
            blend = smooth01(z, -3.0, 2.0) * reach
            sm1 = smooth01(z, SEG[0] * s_ - 1.3, SEG[0] * s_ + 1.3)
            sm2 = smooth01(z, (SEG[0] + SEG[1]) * s_ - 1.2, (SEG[0] + SEG[1]) * s_ + 1.2)
            fingerw[f"f{i}_0"] = share * blend * (1 - sm1)
            fingerw[f"f{i}_1"] = share * blend * sm1 * (1 - sm2)
            fingerw[f"f{i}_2"] = share * blend * sm1 * sm2
    t0, t1, tip, d = thumb_joints(side)
    rel = v - t0
    along = rel.dot(d)
    radial = (rel - d * along).length
    tb = math.exp(-(radial / 2.0) ** 2) * smooth01(side * v.y, 2.0, 3.6) * smooth01(along, -1.8, 1.4)
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
        j0, j1, j2, tip = finger_joints(i, side)
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
    "finger_y_times_side": list(FYB), "finger_z": list(FZS), "finger_scale": list(FS), "finger_segs": list(SEG),
    "thumb_base": THUMB_BASE, "thumb_z": THUMB_Z, "thumb_splay_deg": math.degrees(THUMB_SPLAY), "thumb_segs": list(T_SEG),
}
json.dump(layout, open(r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures/mc61g_hand_layout.json", "w"))
result = stats
