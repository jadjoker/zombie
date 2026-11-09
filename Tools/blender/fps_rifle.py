# Builds the first-person Rifle viewmodel: a cartoon rifle and a pair of gloved arms, rigged and animated.
# Run headless:  blender -b --factory-startup --python fps_rifle.py
# Output: Assets/models/weapons/rifle/{rifle.fbx, rifle@<anim>.fbx, rifle.vmdl}
#
# Camera space: the player's eye is the origin, +X is forward, +Y is left, +Z is up.
# Every piece of mesh is rigid on one bone, so there is no weight painting: the arms are rubber-hose sleeves
# with round joints, the hands are mittens, and everything is animated by solving the arms to the hands in Python.
import bpy, math, os, sys
from mathutils import Vector, Matrix, Quaternion, Euler

OUT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/weapons/rifle"
NAME = "rifle"
FPS = 30
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- scene helpers
def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials):
        for d in list(coll):
            coll.remove(d)

def mat(name, color, rough=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs[0].default_value = color
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = color
    return m

MATS = {}
def build_materials():
    MATS["metal"] = mat("metal", (0.22, 0.24, 0.26, 1))
    MATS["metal2"] = mat("metal2", (0.50, 0.52, 0.54, 1))
    MATS["accent"] = mat("accent", (0.27, 0.30, 0.18, 1))
    MATS["glove"] = mat("glove", (0.20, 0.17, 0.14, 1))
    MATS["sleeve"] = mat("sleeve", (0.15, 0.18, 0.12, 1))
    MATS["lens"] = mat("lens", (0.08, 0.2, 0.3, 1), 0.1)

PARTS = []   # (object, bone, material)

def finish(o, bone, material):
    o.data.materials.clear()
    o.data.materials.append(MATS[material])
    PARTS.append((o, bone, material))
    return o

def bevel_box(center, size, bevel, bone, material, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    o = bpy.context.active_object
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    b = o.modifiers.new("bevel", 'BEVEL'); b.width = bevel; b.segments = 3; b.limit_method = 'NONE'
    bpy.ops.object.modifier_apply(modifier="bevel")
    o.rotation_euler = tuple(math.radians(a) for a in rot)
    o.location = center
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
    bpy.ops.object.shade_smooth()
    return finish(o, bone, material)

def ball(center, radii, bone, material, segs=24):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=segs // 2, radius=1, location=center)
    o = bpy.context.active_object
    o.scale = radii
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    bpy.ops.object.shade_smooth()
    return finish(o, bone, material)

def tube(a, b, r0, r1, bone, material, verts=20):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
    bpy.ops.object.shade_smooth()
    return finish(o, bone, material)

def x_cyl(center, r, length, bone, material, verts=24):
    c = Vector(center)
    return tube(c - Vector((length / 2, 0, 0)), c + Vector((length / 2, 0, 0)), r, r, bone, material, verts)

# ---------------------------------------------------------------- rig layout (camera space, rest pose = hip hold)
GUN = Vector((25.0, -9.0, -9.5))            # where the grip sits
GUN_YAW = 8.0                                 # gun points a touch toward the middle of the screen
G_REST = Matrix.Translation(GUN) @ Matrix.Rotation(math.radians(GUN_YAW), 4, 'Z')

def gl(v):                                    # gun-local point -> camera space (rest pose)
    return G_REST @ Vector(v)

SHOULDER = {"R": Vector((5.0, -14.0, -17.0)), "L": Vector((5.0, 14.0, -17.0))}
UPPER, LOWER = 21.0, 21.0
HAND_LOCAL = {"R": Vector((-0.5, 0.0, -1.5)), "L": Vector((15.5, 0.8, -3.2))}   # wrist, in gun space
POLE = {"R": Vector((0.0, -0.7, -1.0)).normalized(), "L": Vector((0.0, 0.7, -1.0)).normalized()}

def solve_elbow(shoulder, wrist, pole):
    d = wrist - shoulder
    dist = max(min(d.length, UPPER + LOWER - 0.05), abs(UPPER - LOWER) + 0.05)
    dirv = d.normalized()
    wrist = shoulder + dirv * dist
    along = (dist * dist + UPPER * UPPER - LOWER * LOWER) / (2 * dist)
    h = math.sqrt(max(UPPER * UPPER - along * along, 0.0))
    p = pole - dirv * pole.dot(dirv)
    p = p.normalized() if p.length > 1e-5 else Vector((0, 0, -1))
    return shoulder + dirv * along + p * h, wrist

def basis(y, up_hint):
    """Matrix columns x, y, z with y as the bone direction and z leaning toward up_hint."""
    y = y.normalized()
    x = y.cross(up_hint)             # z ends up toward up_hint, the same way the bones' roll was set, so the pose rotation stays near identity
    if x.length < 1e-5:
        x = y.cross(Vector((1, 0, 0)))
    x = x.normalized()
    z = x.cross(y).normalized()      # right-handed: x cross y = z
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2] = x[i], y[i], z[i]
    return m

# ---------------------------------------------------------------- build geometry
def build_meshes():
    # Rifle (gun space: grip at origin, +X forward)
    def g(v): return v
    bevel_box((6, 0, 3.2), (17, 3.8, 5.6), 0.7, "gun", "metal")                 # receiver
    bevel_box((6.5, 0, 6.5), (13, 2.2, 1.2), 0.3, "gun", "metal2")              # top rail
    # Scope: tube on two rings, a wide objective bell up front, an eyepiece at the back, glass at both ends.
    SC = 9.6                                                                    # height of the scope's axis
    for x in (2.5, 10.0):
        bevel_box((x, 0, 7.9), (2.2, 2.6, 2.6), 0.4, "gun", "metal")            # rings
    x_cyl((6.0, 0, SC), 1.55, 12.0, "gun", "metal")                             # tube
    x_cyl((6.0, 0, SC), 1.75, 2.4, "gun", "metal2")                             # turret band
    ball((6.0, 0, SC + 1.9), (1.0, 1.0, 0.8), "gun", "accent", 14)              # elevation turret
    ball((6.0, -1.9, SC), (1.0, 0.8, 1.0), "gun", "accent", 14)                 # windage turret
    tube((12.0, 0, SC), (16.0, 0, SC), 1.6, 2.35, "gun", "metal", 24)           # objective bell
    x_cyl((16.4, 0, SC), 2.35, 0.8, "gun", "accent", 24)                        # objective ring
    x_cyl((16.75, 0, SC), 2.0, 0.15, "gun", "lens", 24)                         # front glass
    x_cyl((-1.0, 0, SC), 1.9, 2.4, "gun", "metal2", 24)                         # eyepiece
    x_cyl((-2.25, 0, SC), 1.6, 0.15, "gun", "lens", 24)                         # rear glass
    bevel_box((21.5, 0, 5.6), (1.0, 1.0, 1.4), 0.3, "gun", "accent")            # low front sight post
    bevel_box((17.5, 0, 2.6), (13, 4.6, 4.6), 1.4, "gun", "metal2")             # handguard
    x_cyl((29.5, 0, 3.0), 0.95, 9.0, "gun", "metal")                            # barrel
    x_cyl((34.5, 0, 3.0), 1.45, 2.2, "gun", "accent")                           # muzzle brake
    bevel_box((-6.0, 0, 2.2), (8, 3.2, 6.0), 1.0, "gun", "metal", rot=(0, 4, 0))   # stock
    bevel_box((-10.4, 0, 1.8), (1.4, 3.4, 6.8), 0.6, "gun", "accent", rot=(0, 4, 0))   # butt pad
    bevel_box((-0.6, 0, -4.2), (3.4, 3.2, 8.6), 1.0, "gun", "metal", rot=(0, -16, 0))  # pistol grip
    bevel_box((3.5, 0, -0.9), (2.2, 0.8, 2.4), 0.3, "gun", "accent")            # trigger guard bit
    bevel_box((8.0, 0, -5.2), (3.6, 3.0, 9.4), 0.9, "mag", "metal2", rot=(0, -6, 0))   # magazine
    bevel_box((8.0, 0, -9.6), (3.8, 3.2, 1.0), 0.4, "mag", "accent", rot=(0, -6, 0))   # mag floor plate
    bevel_box((-1.5, 0, 7.8), (3.2, 2.8, 1.4), 0.4, "bolt", "accent")           # charging handle

    # The rifle was modelled in gun space; move it to where the rest pose holds it.
    for obj, _, _ in PARTS:
        obj.matrix_world = G_REST @ obj.matrix_world
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)

    # Arms and hands, built where the rest pose puts them
    for s, sg in (("R", -1), ("L", 1)):
        sh = SHOULDER[s]
        wr = G_REST @ HAND_LOCAL[s]
        el, wr = solve_elbow(sh, wr, POLE[s])
        # sleeve segments with round joints
        ball(sh, (4.2, 4.2, 4.2), f"upper_{s}", "sleeve")
        tube(sh, el, 4.0, 3.5, f"upper_{s}", "sleeve")
        ball(el, (3.7, 3.7, 3.7), f"lower_{s}", "sleeve")
        tube(el, wr, 3.5, 2.8, f"lower_{s}", "sleeve")
        # cuff and mitten glove
        fwd = (Vector((1, 0, 0)) @ G_REST.to_3x3().transposed()) if False else (G_REST.to_3x3() @ Vector((1, 0, 0)))
        cuff = wr
        ball(cuff, (3.4, 3.4, 3.4), f"hand_{s}", "glove")
        if s == "R":
            c = gl((0.6, 0.0, -2.5))
            ball(c, (3.5, 3.1, 4.1), f"hand_{s}", "glove")                       # fist round the grip
            ball(c + G_REST.to_3x3() @ Vector((2.4, 0.0, 0.6)), (2.3, 3.0, 2.4), f"hand_{s}", "glove")   # fingers
            ball(c + G_REST.to_3x3() @ Vector((1.0, 2.7, 1.6)), (2.4, 1.3, 1.5), f"hand_{s}", "glove")   # thumb
        else:
            c = gl((15.5, 0.0, -2.2))
            ball(c, (4.6, 3.4, 3.4), f"hand_{s}", "glove")                       # fist under the handguard
            ball(c + G_REST.to_3x3() @ Vector((0.0, -2.6, 2.6)), (3.6, 1.6, 1.8), f"hand_{s}", "glove")   # fingers over
            ball(c + G_REST.to_3x3() @ Vector((1.0, 2.6, 1.4)), (2.6, 1.3, 1.5), f"hand_{s}", "glove")    # thumb

# ---------------------------------------------------------------- armature
BONES = ["root", "gun", "mag", "bolt",
         "upper_R", "lower_R", "hand_R", "upper_L", "lower_L", "hand_L"]
REST = {}   # bone -> armature-space rest matrix (as built)

def build_armature():
    arm_data = bpy.data.armatures.new("rig")
    arm = bpy.data.objects.new("rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def add(name, head, tail, parent=None, up=Vector((0, 0, 1))):
        b = eb.new(name)
        b.head, b.tail = head, tail
        if parent:
            b.parent = eb[parent]
        # roll so z points toward `up`
        b.align_roll(up)
        return b

    add("root", Vector((0, 0, 0)), Vector((3, 0, 0)))
    gh = GUN
    gdir = G_REST.to_3x3() @ Vector((1, 0, 0))
    add("gun", gh, gh + gdir * 6, "root")
    add("mag", gl((8.0, 0, -4.2)), gl((8.0, 0, -9.0)), "gun")
    add("bolt", gl((-1.5, 0, 7.8)), gl((-4.5, 0, 7.8)), "gun")
    for s in ("R", "L"):
        sh = SHOULDER[s]
        el, wr = solve_elbow(sh, G_REST @ HAND_LOCAL[s], POLE[s])
        add(f"upper_{s}", sh, el, "root", POLE[s])
        add(f"lower_{s}", el, wr, f"upper_{s}", POLE[s])
        add(f"hand_{s}", wr, wr + gdir * 4, f"lower_{s}", POLE[s])
    bpy.ops.object.mode_set(mode='OBJECT')
    for n in BONES:
        REST[n] = arm.data.bones[n].matrix_local.copy()
    return arm

def join_and_skin(arm):
    # one vertex group per bone, every part fully weighted to its own bone
    for o, bone, _ in PARTS:
        vg = o.vertex_groups.new(name=bone)
        vg.add([v.index for v in o.data.vertices], 1.0, 'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    for o, _, _ in PARTS:
        o.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0][0]
    bpy.ops.object.join()
    mesh = bpy.context.active_object
    mesh.name = "mesh"
    mod = mesh.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    mesh.parent = arm
    bpy.ops.object.shade_smooth()
    # UVs (not textured, but the engine expects them)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    return mesh

def rotate_rig(arm, mesh):
    """Turn the whole finished rig a quarter turn about Z (so it imports facing +X), without disturbing the solver's unrotated rest matrices."""
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    for o in (arm, mesh):
        o.matrix_world = W @ o.matrix_world
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    mesh.parent = arm
    mesh.matrix_parent_inverse = Matrix.Identity(4)

# ---------------------------------------------------------------- animation (solve the arms to the hands, per frame)
def trs(loc=(0, 0, 0), rot=(0, 0, 0), scale=1.0):
    """A gun-space offset: rotation in degrees (x roll, y pitch up-negative, z yaw), then a translation."""
    r = Euler(tuple(math.radians(a) for a in rot), 'XYZ').to_matrix().to_4x4()
    return Matrix.Translation(Vector(loc)) @ r @ Matrix.Scale(scale, 4)

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)

def lerp(a, b, t): return a + (b - a) * t

W = Matrix.Rotation(-math.pi / 2, 4, 'Z')   # the engine turns imported models a quarter turn, so the whole rig is exported rotated back

def set_matrix(pb, m):
    pb.matrix = W @ m

def pose_frame(arm, frame, gun_off, mag_off=None, bolt_off=None, l_hand_world=None, l_hand_gun_off=None):
    """gun_off: offset applied to the gun (gun space). l_hand_gun_off: a free offset for the left hand in gun space."""
    pb = arm.pose.bones
    Gm = G_REST @ gun_off @ G_REST.inverted()             # delta in camera space applied around the rest pose
    # Gun bone: absolute matrix
    gun_m = Gm @ REST["gun"]
    set_matrix(pb["gun"], gun_m)
    bpy.context.view_layer.update()
    # parts attached to the gun
    mag_m = gun_m @ REST["gun"].inverted() @ REST["mag"]
    if mag_off is not None:
        mag_m = Gm @ G_REST @ mag_off @ G_REST.inverted() @ REST["mag"] if False else (gun_m @ REST["gun"].inverted() @ (G_REST @ mag_off @ G_REST.inverted()) @ REST["mag"])
    set_matrix(pb["mag"], mag_m)
    bolt_m = gun_m @ REST["gun"].inverted() @ REST["bolt"]
    if bolt_off is not None:
        bolt_m = gun_m @ REST["gun"].inverted() @ (G_REST @ bolt_off @ G_REST.inverted()) @ REST["bolt"]
    set_matrix(pb["bolt"], bolt_m)
    # hands: right rides the gun, left rides the gun unless it was told otherwise
    for s in ("R", "L"):
        hand_rest = REST[f"hand_{s}"]
        hm = gun_m @ REST["gun"].inverted() @ hand_rest
        if s == "L" and l_hand_gun_off is not None:
            hm = gun_m @ REST["gun"].inverted() @ (G_REST @ l_hand_gun_off @ G_REST.inverted()) @ hand_rest
        if s == "L" and l_hand_world is not None:
            hm = l_hand_world @ hand_rest if False else hm
        wrist = hm.translation.copy()
        sh = SHOULDER[s]
        el, wrist = solve_elbow(sh, wrist, POLE[s])
        up = POLE[s]
        um = basis(el - sh, up); um.translation = sh
        lm = basis(wrist - el, up); lm.translation = el
        # Each child's pose is worked out against its parent's, so the parent has to be updated first.
        set_matrix(pb[f"upper_{s}"], um)
        bpy.context.view_layer.update()
        set_matrix(pb[f"lower_{s}"], lm)
        bpy.context.view_layer.update()
        hmat = hm.copy(); hmat.translation = wrist
        set_matrix(pb[f"hand_{s}"], hmat)
        bpy.context.view_layer.update()

PREV_Q = {}

def key_all(arm, frame):
    for b in arm.pose.bones:
        b.rotation_mode = 'QUATERNION'
        # keep each bone's rotation in the same hemisphere as the frame before, so blending never takes the long way round
        q = b.matrix_basis.to_quaternion()
        prev = PREV_Q.get(b.name)
        if prev is not None and q.dot(prev) < 0:
            q.negate()
        PREV_Q[b.name] = q.copy()
        b.rotation_quaternion = q
        b.keyframe_insert("location", frame=frame)
        b.keyframe_insert("rotation_quaternion", frame=frame)
        b.keyframe_insert("scale", frame=frame)

def new_action(arm, name):
    PREV_Q.clear()
    act = bpy.data.actions.new(name)
    arm.animation_data_create()
    arm.animation_data.action = act
    for b in arm.pose.bones:
        b.location = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0); b.scale = (1, 1, 1)
    return act

ANIMS = {}   # name -> (frames, loop, builder(frame_index, t) -> kwargs)

def anim_idle(i, n):
    t = i / n * 2 * math.pi
    return dict(gun_off=trs((0.15 * math.sin(t), 0.1 * math.sin(t * 2), 0.28 * math.sin(t)), (0, 0.5 * math.sin(t), 0.3 * math.sin(t * 2))))

def anim_fire(i, n):
    t = i / (n - 1)
    k = (1 - t) ** 2 if t > 0.12 else t / 0.12
    kick = 2.4 * k
    bolt = trs((-2.6 * math.sin(math.pi * min(1.0, t * 2.2)), 0, 0)) if t < 0.46 else None
    return dict(gun_off=trs((-kick, 0, 0.5 * k), (0, -3.2 * k, 0)), bolt_off=bolt)

def anim_draw(i, n):
    t = smooth(i / (n - 1))
    return dict(gun_off=trs((lerp(-6, 0, t), 0, lerp(-17, 0, t)), (0, lerp(38, 0, t), lerp(-10, 0, t))))

def anim_sprint(i, n):
    t = i / n * 2 * math.pi
    return dict(gun_off=trs((-4, 3.5, -4 + 0.5 * math.sin(t * 2)), (18, 22, 12)))

def anim_reload(i, n):
    t = i / (n - 1)
    # phases: tilt gun, left hand to mag, mag out and drop, new mag in, slap, back
    tilt = smooth(min(1, t / 0.15)) * (1 - smooth((t - 0.88) / 0.12))
    gun = trs((-1.2 * tilt, 0, 0.6 * tilt), (25 * tilt, 6 * tilt, 0))
    mag_pos = Vector((0, 0, 0)); left = Vector((0, 0, 0)); mag_scale = 1.0
    to_mag = smooth((t - 0.10) / 0.12)
    pull = smooth((t - 0.28) / 0.12)
    away = smooth((t - 0.40) / 0.10)
    fresh = smooth((t - 0.56) / 0.12)
    seat = smooth((t - 0.70) / 0.08)
    back = smooth((t - 0.84) / 0.12)
    # left hand: handguard -> magazine -> pouch -> magazine -> handguard
    mag_world = Vector((-7.5, 0, -5.0))        # in gun space relative to its fist rest
    lh = Vector((0, 0, 0))
    lh = lh.lerp(Vector((-7.5, 0, -3.0)), to_mag)           # reach back and down to the mag
    lh = lh.lerp(Vector((-7.5, 0, -11.0)), pull)            # pull it out downward
    lh = lh.lerp(Vector((-3.0, 1, -13.0)), away)            # toward the pouch
    lh = lh.lerp(Vector((-7.5, 0, -11.0)), fresh)           # bring the new mag up
    lh = lh.lerp(Vector((-7.5, 0, -3.0)), seat)             # push it home
    lh = lh.lerp(Vector((0, 0, 0)), back)                   # return to the handguard
    mag_off = Vector((0, 0, 0))
    if pull < 1.0 and away == 0:
        mag_off = Vector((0, 0, -11.0 * pull))
    elif away > 0 and fresh == 0:
        mag_off = Vector((0, 0, -30.0)); mag_scale = 0.001   # gone while the hand swaps it
    else:
        mag_off = Vector((0, 0, -11.0 * (1 - seat)))
        mag_scale = 1.0
    bolt = None
    slap = smooth((t - 0.80) / 0.04) * (1 - smooth((t - 0.86) / 0.10))
    if slap > 0:
        bolt = trs((-2.4 * slap, 0, 0))
    return dict(gun_off=gun, mag_off=trs(tuple(mag_off), (0, 0, 0), mag_scale), bolt_off=bolt, l_hand_gun_off=trs(tuple(lh)))

ANIMS = {
    "idle": (60, True, anim_idle),
    "fire": (8, False, anim_fire),
    "draw": (24, False, anim_draw),
    "sprint": (30, True, anim_sprint),
    "reload": (100, False, anim_reload),
}

def build_action(arm, name):
    n, loop, fn = ANIMS[name]
    new_action(arm, name)
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = n
    for i in range(n):
        pose_frame(arm, i + 1, **fn(i, n))
        key_all(arm, i + 1)

# ---------------------------------------------------------------- export
def export_fbx(path, objs, anim=False):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    kwargs = dict(filepath=path, use_selection=True, global_scale=0.01, apply_unit_scale=True,
                  apply_scale_options='FBX_SCALE_ALL', axis_forward='-Y', axis_up='Z', bake_space_transform=True,
                  object_types={'ARMATURE', 'MESH'}, mesh_smooth_type='FACE', add_leaf_bones=False,
                  use_armature_deform_only=False, bake_anim=anim)
    if anim:
        kwargs.update(bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                      bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
    bpy.ops.export_scene.fbx(**kwargs)

def write_vmdl(path):
    anims = ""
    for name, (n, loop, _) in ANIMS.items():
        anims += f'''					{{
						_class = "AnimFile"
						name = "{name}"
						activity_name = ""
						activity_weight = 1
						weight_list_name = ""
						fade_in_time = 0.1
						fade_out_time = 0.1
						looping = {"true" if loop else "false"}
						delta = false
						worldSpace = false
						hidden = false
						anim_markup_ordered = false
						disable_compression = false
						disable_interpolation = false
						enable_scale = true
						source_filename = "models/weapons/{NAME}/{NAME}@{name}.fbx"
						start_frame = -1
						end_frame = -1
						framerate = {FPS}.0
						take = 0
						reverse = false
					}},
'''
    remaps = ""
    for m in MATS:
        remaps += f'''							{{
								from = "{m}.vmat"
								to = "materials/weapons/{m}.vmat"
							}},
'''
    open(path, "w", encoding="utf-8", newline="").write(f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc29:version{{3cec427c-1b0e-4d48-a90a-0436f33a6041}} -->
{{
	rootNode =
	{{
		_class = "RootNode"
		children =
		[
			{{
				_class = "MaterialGroupList"
				children =
				[
					{{
						_class = "DefaultMaterialGroup"
						remaps =
						[
{remaps}						]
						use_global_default = false
						global_default_material = ""
					}},
				]
			}},
			{{
				_class = "RenderMeshList"
				children =
				[
					{{
						_class = "RenderMeshFile"
						filename = "models/weapons/{NAME}/{NAME}.fbx"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = 1.0
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{{
							exclude_by_default = false
							exception_list = [  ]
						}}
					}},
				]
			}},
			{{
				_class = "AnimationList"
				children =
				[
{anims}				]
				default_root_bone_name = "root"
			}},
			{{
				_class = "AttachmentList"
				children =
				[
					{{
						_class = "Attachment"
						name = "muzzle"
						parent_bone = "gun"
						relative_origin = [ 0.0, 36.0, 3.0 ]
						relative_angles = [ 0.0, 0.0, 0.0 ]
						weight = 1.0
						ignore_rotation = false
					}},
				]
			}},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}}
}}
''')

def main():
    clear()
    build_materials()
    build_meshes()
    arm = build_armature()
    mesh = join_and_skin(arm)
    rotate_rig(arm, mesh)
    # model file (bind pose)
    for b in arm.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
    export_fbx(f"{OUT}/{NAME}.fbx", [arm, mesh], anim=False)
    # one file per animation
    for name in ANIMS:
        build_action(arm, name)
        export_fbx(f"{OUT}/{NAME}@{name}.fbx", [arm, mesh], anim=True)
        arm.animation_data.action = None
        for b in arm.pose.bones:
            b.matrix_basis = Matrix.Identity(4)
    write_vmdl(f"{OUT}/{NAME}.vmdl")
    # keep a .blend for previews
    arm.animation_data.action = None
    bpy.ops.wm.save_as_mainfile(filepath=r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_rifle.blend")
    print("built", len(mesh.data.polygons), "polys")

main()
