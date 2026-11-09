# First-person fire axe held in both gloved hands, same cartoon style and rig as the guns.
# Reuses fps_rifle.py's rig, IK, export and vmdl writer. The "gun" bone is the axe.
#   blender -b --factory-startup --python fps_axe.py
#
# Animations: swing_r (from over the right shoulder down to the lower left) and swing_l (its mirror), 0.55 s each,
# the blade landing at 0.24 s to match PlayerMelee.WindUp.
import math

src = open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_rifle.py", encoding="utf-8").read()
src = src.replace("\nmain()\n", "\n")
exec(src)

OUT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/weapons/axe"
NAME = "axe"


def aim(loc, haft, edge):
    """Axe pose in camera space: grip at loc, haft (local +X) along `haft`, the blade's edge (local -Z) toward `edge`."""
    x = Vector(haft).normalized()
    e = Vector(edge)
    e = (e - x * e.dot(x)).normalized()
    z = -e
    y = z.cross(x).normalized()
    m = Matrix.Identity(4)
    for i in range(3):
        m[i][0], m[i][1], m[i][2] = x[i], y[i], z[i]
    m.translation = Vector(loc)
    return m


# Key poses for the right-hand swing (camera space: +X ahead, +Y left, +Z up).
LOW = aim((24, -12, -32), (0.55, 0.1, 0.55), (1.0, 0.0, 0.2))          # below the screen
READY = aim((30, -13, -10), (0.5, 0.12, 0.85), (1.0, 0.25, -0.1))      # held up at the right
RAISED = aim((24, -10, 0), (-0.4, -0.25, 0.88), (0.6, 0.35, 0.5))     # hauled back over the shoulder
STRIKE = aim((34, -2, -6), (0.85, 0.3, 0.25), (0.05, 0.55, -0.85))     # blade lands, ahead and a touch left
FOLLOW = aim((30, 11, -14), (0.5, 0.65, -0.55), (0.0, 0.7, -0.7))      # carried through, low left
GONE = aim((20, 8, -34), (0.5, 0.3, -0.6), (0.0, 0.5, -0.8))           # dropped away

G_REST = READY
GUN = READY.translation.copy()

# Two hands on the haft: right at the end, left a hand's width up.
HAND_LOCAL = {"R": Vector((0.8, 0.0, -1.4)), "L": Vector((9.5, 0.0, -1.4))}
POLE = {"R": Vector((0.0, -0.7, -1.0)).normalized(), "L": Vector((0.0, 0.7, -1.0)).normalized()}


def build_materials_axe():
    build_materials()
    MATS["wood"] = mat("wood", (0.55, 0.34, 0.18, 1))
    MATS["axered"] = mat("axered", (0.82, 0.12, 0.1, 1))


def build_meshes():
    # Axe, in axe space (grip at origin, haft along +X, edge toward -Z)
    x_cyl((15.0, 0, 0), 1.15, 32.0, "gun", "wood", 16)                     # haft
    x_cyl((3.0, 0, 0), 1.4, 7.0, "gun", "accent", 16)                      # grip wrap
    x_cyl((-0.8, 0, 0), 1.7, 1.2, "gun", "metal", 16)                      # end cap
    bevel_box((30.0, 0, 0.0), (5.0, 2.8, 4.6), 0.6, "gun", "axered")       # head block
    bevel_box((30.4, 0, -5.4), (6.4, 1.6, 6.4), 0.5, "gun", "axered")      # blade body
    bevel_box((30.6, 0, -8.6), (7.6, 0.8, 1.6), 0.3, "gun", "metal2")      # bright edge
    bevel_box((30.0, 0, 4.4), (3.2, 1.8, 5.0), 0.5, "gun", "metal", rot=(0, 18, 0))   # back spike
    ball((32.6, 0, 0.0), (1.0, 1.6, 1.0), "gun", "metal", 12)              # head cap

    for obj, _, _ in PARTS:
        obj.matrix_world = G_REST @ obj.matrix_world
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)

    for s in ("R", "L"):
        sh = SHOULDER[s]
        wr = G_REST @ HAND_LOCAL[s]
        el, wr = solve_elbow(sh, wr, POLE[s])
        ball(sh, (4.2, 4.2, 4.2), f"upper_{s}", "sleeve")
        tube(sh, el, 4.0, 3.5, f"upper_{s}", "sleeve")
        ball(el, (3.7, 3.7, 3.7), f"lower_{s}", "sleeve")
        tube(el, wr, 3.5, 2.8, f"lower_{s}", "sleeve")
        ball(wr, (3.4, 3.4, 3.4), f"hand_{s}", "glove")
        # a fist wrapped round the haft
        c = gl(HAND_LOCAL[s] + Vector((0.4, 0, 1.2)))
        ball(c, (3.0, 3.6, 3.4), f"hand_{s}", "glove")
        ball(c + G_REST.to_3x3() @ Vector((0.0, 2.0 if s == "R" else -2.0, 1.8)), (2.4, 1.4, 1.6), f"hand_{s}", "glove")   # thumb over


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def blend(a, b, t):
    k = ease(t)
    loc = a.translation.lerp(b.translation, k)
    q = a.to_quaternion().slerp(b.to_quaternion(), k)
    m = q.to_matrix().to_4x4()
    m.translation = loc
    return m


# (seconds, pose): blade lands at 0.24
KEYS = [(0.0, LOW), (0.07, READY), (0.15, RAISED), (0.24, STRIKE), (0.31, FOLLOW), (0.55, GONE)]


def catmull(p0, p1, p2, p3, t):
    t2, t3 = t * t, t * t * t
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


def swing_pose(seconds, mirror):
    """The axe's pose at a time, on a smooth curve through the keys (so it never stops dead at one)."""
    n = len(KEYS)
    i = 0
    while i < n - 2 and seconds > KEYS[i + 1][0]:
        i += 1
    k0, k1, k2, k3 = KEYS[max(i - 1, 0)], KEYS[i], KEYS[i + 1], KEYS[min(i + 2, n - 1)]
    u = max(0.0, min(1.0, (seconds - k1[0]) / max(1e-4, k2[0] - k1[0])))
    def parts(m):
        r = m.to_3x3()
        return m.translation.copy(), Vector((r[0][0], r[1][0], r[2][0])), -Vector((r[0][2], r[1][2], r[2][2]))
    P = [parts(k[1]) for k in (k0, k1, k2, k3)]
    loc = catmull(*[p[0] for p in P], u)
    haft = catmull(*[p[1] for p in P], u)
    edge = catmull(*[p[2] for p in P], u)
    m = aim(loc, haft, edge)
    if mirror:
        f = Matrix.Diagonal((1, -1, 1, 1))
        m = f @ m @ f
    return m


def make_swing(mirror):
    def fn(i, n):
        seconds = i / FPS
        pose = swing_pose(seconds, mirror)
        return dict(gun_off=G_REST.inverted() @ pose)
    return fn


ANIMS = {
    "swing_r": (17, False, make_swing(False)),
    "swing_l": (17, False, make_swing(True)),
}


def main():
    import os
    os.makedirs(OUT, exist_ok=True)
    clear()
    build_materials_axe()
    build_meshes()
    arm = build_armature()
    mesh = join_and_skin(arm)
    rotate_rig(arm, mesh)
    for b in arm.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
    export_fbx(f"{OUT}/{NAME}.fbx", [arm, mesh], anim=False)
    for name in ANIMS:
        build_action(arm, name)
        export_fbx(f"{OUT}/{NAME}@{name}.fbx", [arm, mesh], anim=True)
        arm.animation_data.action = None
        for b in arm.pose.bones:
            b.matrix_basis = Matrix.Identity(4)
    write_vmdl(f"{OUT}/{NAME}.vmdl")
    print("built", len(mesh.data.polygons), "polys")


if __name__ == "__main__":
    main()
