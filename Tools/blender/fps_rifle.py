# Builds the first-person Rifle viewmodel: a cartoon rifle held by the player robot's arms, rigged and animated.
# Run headless:  blender -b --factory-startup --python fps_rifle.py
# Output: Assets/models/weapons/rifle/{rifle.fbx, rifle@<anim>.fbx, rifle.vmdl}
#
# Camera space: the player's eye is the origin, +X is forward, +Y is left, +Z is up.
# Every piece of mesh is rigid on one bone, so there is no weight painting: the arms are the robot's (plated
# segments, dark ball joints, an orange shoulder and cuff, fingers of two rigid links posed round the grip), and
# everything is animated by solving the arms to the hands in Python.
import bpy, math, os, sys
from mathutils import Vector, Matrix, Quaternion, Euler

OUT = os.environ.get("RIFLE_OUT", r"C:/Users/smitt/Documents/zombie_survival/Assets/models/weapons/rifle")
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
    MATS["dark"] = mat("dark", (0.06, 0.065, 0.07, 1), 0.6)
    MATS["glow"] = mat("glow", (1.0, 0.2, 0.1, 1), 0.3)
    # The player robot: pale plates, dark joints, orange trim (as on the third person model).
    MATS["plate"] = mat("plate", (0.72, 0.75, 0.78, 1), 0.4)
    MATS["joint"] = mat("joint", (0.07, 0.075, 0.08, 1), 0.6)
    MATS["trim"] = mat("trim", (0.95, 0.5, 0.12, 1), 0.45)

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
    b = o.modifiers.new("bevel", 'BEVEL'); b.width = min(bevel, min(size) * 0.4); b.segments = 3; b.limit_method = 'NONE'
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


# ---------------------------------------------------------------- the robot's arms and hands
def robot_arm(s, sh, el, wr):
    """Shoulder to wrist: an orange shoulder ball, a plated upper arm, a dark elbow joint, a forearm that flares
    into a gauntlet with an orange cuff, and a dark wrist joint. Points are in camera space."""
    ball(sh, (4.4, 4.4, 4.4), f"upper_{s}", "trim")
    tube(sh, el, 3.2, 2.8, f"upper_{s}", "plate")
    ball(el, (3.2, 3.2, 3.2), f"lower_{s}", "joint")
    mid = el.lerp(wr, 0.22)
    tube(mid, wr.lerp(el, 0.12), 2.5, 3.2, f"lower_{s}", "plate")
    tube(el, mid, 2.2, 2.2, f"lower_{s}", "joint")                        # the bare strut below the elbow
    tube(wr.lerp(el, 0.2), wr.lerp(el, 0.11), 3.45, 3.45, f"lower_{s}", "trim")   # cuff
    ball(wr, (2.5, 2.5, 2.5), f"hand_{s}", "joint")


def robot_finger(s, pts, r=0.62):
    """One finger: rigid links between the given gun-space points, a dark knuckle at each joint, a rounded tip."""
    p = [gl(v) for v in pts]
    for a, b in zip(p, p[1:]):
        tube(a, b, r, r * 0.92, f"hand_{s}", "plate", 10)
    for q in p[:-1]:
        ball(q, (r * 1.18,) * 3, f"hand_{s}", "joint", 10)
    ball(p[-1], (r * 0.95,) * 3, f"hand_{s}", "plate", 10)


def robot_hand(s, palm, palm_r, fingers, thumb):
    """A hand closed round something: a rounded palm block, fingers and a thumb (each a list of gun-space points
    from knuckle to tip)."""
    ball(gl(palm), palm_r, f"hand_{s}", "plate", 16)
    for f in fingers:
        robot_finger(s, f)
    robot_finger(s, thumb, 0.72)


def grip_hand_right():
    """The right hand on a pistol grip at the gun's origin: three fingers wrapped round the front, the index
    finger along the trigger, the thumb over the near side."""
    fingers = [[(1.0, -1.7, -0.6), (2.8, -0.9, -0.5), (3.1, 0.3, -0.7)]]          # index, on the trigger
    for z in (-2.3, -3.9, -5.5):
        dx = 0.3 * (z + 4.0)                                                       # the grip leans back toward its foot
        fingers.append([(0.8 + dx, -1.75, z), (1.9 + dx, 0.5, z - 0.1), (0.5 + dx, 1.85, z - 0.2)])
    robot_hand("R", (-1.7, -1.0, -3.3), (2.3, 2.2, 2.9), fingers, [(-1.5, 0.5, -1.6), (0.1, 1.85, -1.1), (1.5, 1.9, -0.9)])


def cradle_hand_left(x=15.5, half_width=2.9, top=4.0):
    """The left hand cupped under a fore-end: four fingers up the far side, the thumb up the near side."""
    fingers = [[(fx, -1.6, -0.7), (fx, -half_width, 1.7), (fx, -half_width + 0.6, top)] for fx in (x - 2.1, x - 0.7, x + 0.7, x + 2.1)]
    robot_hand("L", (x, 0.1, -1.5), (3.0, 2.4, 1.6), fingers, [(x - 0.5, 1.6, -0.7), (x + 0.1, half_width, 1.5), (x + 0.9, half_width - 0.5, top - 0.6)])

# ---------------------------------------------------------------- the rifle
def y_cyl(center, r, length, bone, material, verts=16):
    c = Vector(center)
    return tube(c - Vector((0, length / 2, 0)), c + Vector((0, length / 2, 0)), r, r, bone, material, verts)

def z_cyl(center, r, length, bone, material, verts=16):
    c = Vector(center)
    return tube(c - Vector((0, 0, length / 2)), c + Vector((0, 0, length / 2)), r, r, bone, material, verts)

def build_rifle():
    """A chunky stylised carbine with a scope: simple shapes, but layered (panels, rails, vents, screws) so it
    reads as a real gun up close. Gun space, inches: grip at origin, +X forward, muzzle tip at x 36, z 3."""
    # Receiver: an upper and a lower, an ejection port and a forward assist.
    bevel_box((6.5, 0, 4.4), (16, 3.6, 3.2), 0.5, "gun", "metal")               # upper receiver
    bevel_box((5.0, 0, 1.6), (13, 3.3, 2.8), 0.6, "gun", "metal")               # lower receiver
    bevel_box((7.5, -1.82, 4.3), (5.0, 0.12, 1.5), 0.1, "gun", "dark")          # ejection port (right side)
    bevel_box((7.5, -1.86, 5.25), (5.4, 0.12, 0.25), 0.05, "gun", "metal2")     # port cover hinge
    y_cyl((1.6, -2.0, 4.6), 0.55, 0.9, "gun", "metal2", 12)                     # forward assist
    bevel_box((4.5, 0, 0.2), (8, 3.5, 0.35), 0.1, "gun", "metal2")              # seam between upper and lower
    for x in (1.5, 10.5):                                                       # takedown pins
        y_cyl((x, 0, 1.8), 0.32, 3.6, "gun", "metal2", 10)
    y_cyl((-0.3, 1.75, 2.0), 0.6, 0.3, "gun", "accent", 12)                     # safety selector
    bevel_box((-0.1, 1.9, 2.0), (1.1, 0.2, 0.35), 0.08, "gun", "accent")
    # Picatinny rail along the top, teeth and all.
    bevel_box((10.0, 0, 6.25), (20, 2.0, 0.55), 0.12, "gun", "metal")
    for i in range(19):
        bevel_box((0.6 + i * 1.0, 0, 6.65), (0.5, 2.1, 0.3), 0.06, "gun", "dark")
    # Handguard: an eight sided tube with vent slots, a rail underneath and a bottom grip panel.
    hg = x_cyl((18.5, 0, 3.2), 2.35, 13.0, "gun", "metal2", 8)
    for side in (-1, 1):
        for i in range(4):
            bevel_box((14.2 + i * 2.7, side * 2.18, 3.2), (1.7, 0.2, 0.75), 0.08, "gun", "dark")
    bevel_box((18.5, 0, 0.75), (12.6, 1.6, 0.45), 0.1, "gun", "metal")         # under rail
    x_cyl((12.4, 0, 3.2), 2.55, 0.7, "gun", "metal", 8)                         # handguard nut
    # Barrel, gas block, front sight and a ported muzzle brake.
    x_cyl((28.5, 0, 3.0), 0.75, 11.0, "gun", "metal", 16)
    bevel_box((26.0, 0, 3.4), (1.6, 1.9, 2.2), 0.25, "gun", "metal")            # gas block
    bevel_box((26.0, 0, 5.4), (0.9, 0.5, 1.8), 0.15, "gun", "metal")            # front sight post
    x_cyl((33.2, 0, 3.0), 1.15, 0.5, "gun", "metal2", 16)                       # crush washer
    x_cyl((34.7, 0, 3.0), 1.2, 2.6, "gun", "accent", 16)                        # brake body
    for x in (34.0, 35.0):
        for side in (-1, 1):
            bevel_box((x, side * 1.05, 3.0), (0.45, 0.35, 1.4), 0.05, "gun", "dark")   # brake ports
    x_cyl((36.05, 0, 3.0), 0.55, 0.12, "gun", "dark", 16)                       # bore
    # Stock: a buffer tube, an adjustable stock with a cheek rest, a rubber pad.
    x_cyl((-5.0, 0, 3.6), 1.05, 9.0, "gun", "metal", 16)
    x_cyl((-0.6, 0, 3.6), 1.25, 0.6, "gun", "metal2", 16)                       # castle nut
    bevel_box((-8.0, 0, 2.6), (7.0, 2.8, 4.2), 0.9, "gun", "accent", rot=(0, 3, 0))    # stock body
    bevel_box((-7.6, 0, 5.0), (6.0, 2.4, 1.2), 0.5, "gun", "accent", rot=(0, 3, 0))    # cheek rest
    bevel_box((-7.4, 0, 0.0), (5.0, 2.2, 1.1), 0.4, "gun", "accent", rot=(0, 3, 0))    # stock tail
    bevel_box((-11.75, 0, 2.2), (0.9, 3.0, 6.6), 0.4, "gun", "metal", rot=(0, 3, 0))   # butt pad
    y_cyl((-10.0, 0, 0.4), 0.45, 3.1, "gun", "metal2", 10)                      # sling loop pin
    # Pistol grip with finger bumps, trigger and a proper trigger guard.
    bevel_box((-0.6, 0, -4.0), (3.2, 2.9, 8.0), 1.0, "gun", "accent", rot=(0, -18, 0))
    for i in range(3):
        bevel_box((0.9 - i * 0.55, 0, -2.6 - i * 1.9), (0.9, 2.7, 1.1), 0.4, "gun", "accent", rot=(0, -18, 0))
    bevel_box((-1.1, 0, -7.9), (3.6, 3.1, 0.6), 0.25, "gun", "dark", rot=(0, -18, 0))  # grip cap
    bevel_box((3.0, 0, -0.6), (0.4, 0.5, 2.0), 0.12, "gun", "dark", rot=(0, 18, 0))    # trigger
    bevel_box((3.3, 0, -1.95), (5.4, 1.0, 0.35), 0.1, "gun", "metal")           # guard bottom
    bevel_box((5.85, 0, -0.9), (0.35, 1.0, 2.3), 0.1, "gun", "metal")           # guard front
    # Magazine: two tilted halves for the curve, ribs, a floor plate and a witness hole.
    bevel_box((8.0, 0, -3.3), (3.4, 2.4, 5.2), 0.5, "mag", "metal2", rot=(0, -4, 0))
    bevel_box((8.6, 0, -7.4), (3.4, 2.4, 3.8), 0.5, "mag", "metal2", rot=(0, -14, 0))
    for i in range(3):
        bevel_box((8.15 + i * 0.12, 0, -2.4 - i * 1.6), (2.6, 2.55, 0.35), 0.1, "mag", "metal", rot=(0, -4 - i * 3, 0))
    bevel_box((9.15, 0, -9.5), (4.0, 2.8, 0.8), 0.3, "mag", "accent", rot=(0, -14, 0))
    bevel_box((7.2, 1.22, -4.4), (0.8, 0.1, 0.8), 0.1, "mag", "dark")
    bevel_box((7.6, 0, -0.6), (4.0, 2.9, 0.9), 0.2, "gun", "metal")             # mag well flare
    # Scope: slimmer tube on proper rings, cylinder turrets, tapered bell and a sunshade, glass both ends.
    SC = 9.4
    for x in (2.0, 10.0):
        bevel_box((x, 0, 7.15), (1.6, 2.2, 1.0), 0.2, "gun", "metal")           # ring base
        x_cyl((x, 0, SC), 1.75, 1.4, "gun", "metal", 20)                        # ring
        for side in (-1, 1):
            y_cyl((x, side * 1.75, SC - 1.2), 0.3, 0.4, "gun", "metal2", 8)     # ring screws
    x_cyl((6.0, 0, SC), 1.4, 13.0, "gun", "metal", 24)                          # tube
    x_cyl((6.0, 0, SC), 1.7, 2.6, "gun", "metal", 24)                           # turret saddle
    z_cyl((6.0, 0, SC + 1.9), 0.85, 1.4, "gun", "accent", 16)                   # elevation turret
    z_cyl((6.0, 0, SC + 2.65), 0.95, 0.25, "gun", "metal2", 16)
    y_cyl((6.0, -1.9, SC), 0.85, 1.4, "gun", "accent", 16)                      # windage turret
    y_cyl((6.0, -2.65, SC), 0.95, 0.25, "gun", "metal2", 16)
    tube((12.5, 0, SC), (15.5, 0, SC), 1.4, 2.1, "gun", "metal", 24)            # objective bell
    x_cyl((16.6, 0, SC), 2.1, 2.2, "gun", "metal", 24)                          # sunshade
    x_cyl((15.65, 0, SC), 2.2, 0.25, "gun", "accent", 24)                       # accent band
    x_cyl((17.6, 0, SC), 1.85, 0.12, "gun", "lens", 24)                         # front glass (recessed)
    tube((-0.5, 0, SC), (-2.5, 0, SC), 1.4, 1.85, "gun", "metal", 24)           # eyepiece
    x_cyl((-2.9, 0, SC), 1.95, 0.8, "gun", "accent", 24)                        # eye cup
    x_cyl((-3.36, 0, SC), 1.6, 0.12, "gun", "lens", 24)                         # rear glass, proud of the cup
    # A tiny glowing dot on the turret so it reads at a glance.
    bevel_box((6.0, 0.97, SC + 2.0), (0.3, 0.1, 0.3), 0.05, "gun", "glow")
    # Charging handle (rides the bolt bone).
    bevel_box((-2.2, 0, 5.4), (2.0, 2.4, 0.7), 0.2, "bolt", "metal")
    bevel_box((-3.1, 0, 5.4), (0.6, 3.6, 0.8), 0.2, "bolt", "accent")


# ---------------------------------------------------------------- build geometry
def build_meshes():
    # Rifle (gun space: grip at origin, +X forward)
    def g(v): return v
    build_rifle()

    # The rifle was modelled in gun space; move it to where the rest pose holds it.
    for obj, _, _ in PARTS:
        obj.matrix_world = G_REST @ obj.matrix_world
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)

    # The robot's arms and hands, built where the rest pose puts them.
    for s in ("R", "L"):
        sh = SHOULDER[s]
        el, wr = solve_elbow(sh, G_REST @ HAND_LOCAL[s], POLE[s])
        robot_arm(s, sh, el, wr)
    grip_hand_right()
    cradle_hand_left()

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
						relative_origin = [ 0.0, 36.2, 3.0 ]
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
