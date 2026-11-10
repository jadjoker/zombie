# First-person pump shotgun held by the same robot arms as the rifle, in the same cartoon style.
# Reuses fps_rifle.py's rig, solver, export and vmdl writer; only the gun, its parts and the animations differ.
#   blender -b --factory-startup --python fps_shotgun.py
#
# Bones: "bolt" is the pump (it slides back and forward, the left hand riding it), "mag" is a loose shell that only
# shows while the left hand is feeding it into the loading port.
import math

src = open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_rifle.py", encoding="utf-8").read()
src = src.replace("\nmain()\n", "\n")
src = src.replace("relative_origin = [ 0.0, 36.2, 3.0 ]", "relative_origin = [ 0.0, 40.0, 5.2 ]")
exec(src)

OUT = os.environ.get("SHOTGUN_OUT", r"C:/Users/smitt/Documents/zombie_survival/Assets/models/weapons/shotgun")
NAME = "shotgun"


def build_meshes():
    """A pump shotgun with a wooden stock and pump: simple shapes, layered so it reads up close. Gun space,
    inches: grip at origin, +X forward, muzzle at x 40, z 5."""
    MATS["wood"] = mat("wood", (0.55, 0.34, 0.18, 1))
    MATS["axered"] = mat("axered", (0.82, 0.12, 0.1, 1))
    # Receiver: body, a flat top, the ejection port, the loading port underneath, pins and a safety.
    bevel_box((4.5, 0, 3.0), (13, 3.6, 5.2), 0.7, "gun", "metal")
    bevel_box((4.5, 0, 5.75), (12.2, 2.4, 0.5), 0.15, "gun", "metal")                 # flat top
    bevel_box((5.5, -1.82, 3.6), (5.6, 0.14, 1.7), 0.05, "gun", "dark")                # ejection port (right)
    bevel_box((5.5, -1.86, 4.6), (6.0, 0.14, 0.25), 0.05, "gun", "metal2")
    bevel_box((6.0, 0, 0.35), (6.5, 2.2, 0.25), 0.08, "gun", "dark")                   # loading port (underneath)
    for x in (0.2, 9.4):
        y_cyl((x, 0, 2.6), 0.3, 3.75, "gun", "metal2", 10)                             # receiver pins
    y_cyl((-0.6, 1.8, 4.2), 0.5, 0.35, "gun", "axered", 12)                            # safety button
    # Shell holder on the near side: four red shells with brass heads.
    bevel_box((5.0, 1.95, 3.0), (8.6, 0.5, 2.6), 0.15, "gun", "dark")
    for i in range(4):
        z_cyl((2.0 + i * 2.0, 2.45, 3.2), 0.78, 3.0, "gun", "axered", 12)
        z_cyl((2.0 + i * 2.0, 2.45, 1.55), 0.84, 0.5, "gun", "metal2", 12)
    # Barrel with a vent rib and bead, the magazine tube under it, the clamp between them.
    x_cyl((24.0, 0, 5.0), 1.05, 31.0, "gun", "metal", 20)
    bevel_box((23.0, 0, 6.35), (30.0, 0.7, 0.3), 0.08, "gun", "metal")                 # rib
    for i in range(7):
        bevel_box((10.5 + i * 4.2, 0, 6.12), (0.5, 0.5, 0.3), 0.05, "gun", "dark")     # rib posts
    ball((37.6, 0, 6.75), (0.5, 0.5, 0.5), "gun", "axered", 12)                        # bead sight
    x_cyl((39.2, 0, 5.0), 1.3, 1.4, "gun", "metal2", 20)                               # muzzle ring
    x_cyl((39.95, 0, 5.0), 0.8, 0.12, "gun", "dark", 16)                               # bore
    x_cyl((22.5, 0, 2.5), 1.25, 23.0, "gun", "metal", 20)                              # magazine tube
    x_cyl((34.4, 0, 2.5), 1.45, 1.6, "gun", "metal2", 20)                              # tube cap
    bevel_box((32.0, 0, 3.8), (1.4, 2.9, 3.9), 0.4, "gun", "metal")                    # barrel clamp
    y_cyl((32.0, 0, 3.75), 0.3, 3.1, "gun", "metal2", 10)
    # Wooden stock with a comb and a rubber pad, pistol grip, trigger and guard.
    bevel_box((-5.8, 0, 2.2), (8.6, 3.0, 5.0), 1.1, "gun", "wood", rot=(0, 5, 0))
    bevel_box((-6.4, 0, 4.7), (6.4, 2.4, 1.2), 0.5, "gun", "wood", rot=(0, 5, 0))      # comb
    bevel_box((-10.5, 0, 1.6), (1.2, 3.2, 6.6), 0.5, "gun", "dark", rot=(0, 5, 0))     # recoil pad
    bevel_box((-9.6, 0, 1.7), (0.4, 3.1, 6.4), 0.1, "gun", "metal2", rot=(0, 5, 0))    # spacer
    bevel_box((-0.6, 0, -4.0), (3.2, 2.9, 8.0), 1.0, "gun", "wood", rot=(0, -18, 0))   # pistol grip
    bevel_box((-1.1, 0, -7.9), (3.6, 3.1, 0.6), 0.25, "gun", "dark", rot=(0, -18, 0))  # grip cap
    bevel_box((3.0, 0, -0.6), (0.4, 0.5, 2.0), 0.12, "gun", "dark", rot=(0, 18, 0))    # trigger
    bevel_box((3.3, 0, -1.95), (5.4, 1.0, 0.35), 0.1, "gun", "metal")                  # guard bottom
    bevel_box((5.85, 0, -0.9), (0.35, 1.0, 2.3), 0.1, "gun", "metal")                  # guard front
    # The pump: a wooden fore-end with dark grooves, on its action bars.
    bevel_box((17.0, 0, 2.3), (10.5, 4.6, 4.4), 1.3, "bolt", "wood")
    for x in (13.4, 15.2, 17.0, 18.8, 20.6):
        bevel_box((x, 0, 2.3), (0.45, 4.75, 4.55), 0.15, "bolt", "dark")
    for side in (-1, 1):
        bevel_box((11.5, side * 1.5, 2.6), (5.0, 0.3, 0.7), 0.08, "bolt", "metal2")    # action bars
    # The loose shell (sits in the left hand; hidden unless it is being loaded).
    x_cyl((15.0, 0, -6.4), 1.0, 3.8, "mag", "axered", 16)
    x_cyl((17.1, 0, -6.4), 1.05, 0.7, "mag", "metal2", 16)

    for obj, _, _ in PARTS:
        obj.matrix_world = G_REST @ obj.matrix_world
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)

    # The robot's arms and hands: right on the grip, left under the pump.
    for s in ("R", "L"):
        sh = SHOULDER[s]
        el, wr = solve_elbow(sh, G_REST @ HAND_LOCAL[s], POLE[s])
        robot_arm(s, sh, el, wr)
    grip_hand_right()
    cradle_hand_left(half_width=2.9, top=4.3)


HIDE = trs((0, 0, 0), (0, 0, 0), 0.001)
PUMP = 5.5                                     # how far the pump racks back

# The loading pose: gun rolled so the port faces the left hand, left hand under the receiver.
HOLD_GUN = ((-0.8, 0.0, 0.6), (24, 5, 0))
LH_HOLD = Vector((-9.0, 1.2, -3.8))           # left hand, relative to its rest on the pump


def hold_gun(k=1.0, sway=0.0):
    (x, y, z), (r, p, w) = HOLD_GUN
    return trs((x * k, y * k, z * k + sway), (r * k, p * k, w * k))


def anim_fire(i, n):
    t = i / (n - 1)
    k = (1 - t / 0.3) ** 2 if t < 0.3 else 0.0
    k = min(1.0, t / 0.04) * k if t < 0.04 else k
    kick = 3.8 * k
    rack = smooth((t - 0.32) / 0.18) - smooth((t - 0.62) / 0.2)
    gun = trs((-kick - 0.6 * rack, 0, 0.9 * k), (3 * rack, -7.5 * k - 2 * rack, 1.5 * rack))
    pump = trs((-PUMP * rack, 0, 0))
    return dict(gun_off=gun, bolt_off=pump, l_hand_gun_off=pump, mag_off=HIDE)


def anim_reload_start(i, n):
    t = smooth(i / (n - 1))
    return dict(gun_off=hold_gun(t), l_hand_gun_off=trs(tuple(Vector((0, 0, 0)).lerp(LH_HOLD, t))), mag_off=HIDE)


def anim_reload_hold(i, n):
    s = math.sin(i / n * 2 * math.pi) * 0.15
    return dict(gun_off=hold_gun(1.0, s), l_hand_gun_off=trs(tuple(LH_HOLD + Vector((0, 0, s)))), mag_off=HIDE)


def anim_reload_shell(i, n):
    t = i / (n - 1)
    pouch = LH_HOLD + Vector((3.0, 5.5, -9.0))     # down and out toward the belt
    push = LH_HOLD + Vector((-0.5, -0.8, 2.2))      # up into the loading port
    a = smooth(t / 0.32)
    b = smooth((t - 0.34) / 0.3)
    c = smooth((t - 0.66) / 0.12)
    d = smooth((t - 0.8) / 0.2)
    lh = LH_HOLD.lerp(pouch, a).lerp(LH_HOLD + Vector((0, 0, -1.0)), b).lerp(push, c).lerp(LH_HOLD, d)
    showing = 0.26 < t < 0.8
    shell = trs(tuple(lh)) if showing else HIDE
    nudge = 0.4 * (c - d)                            # the gun bumps as the shell clicks in
    return dict(gun_off=hold_gun(1.0, nudge), l_hand_gun_off=trs(tuple(lh)), mag_off=shell)


def anim_reload_end(i, n):
    t = i / (n - 1)
    back = smooth(t / 0.45)
    rack = smooth((t - 0.5) / 0.2) - smooth((t - 0.75) / 0.2)
    gun = hold_gun(1.0 - back) @ trs((-0.5 * rack, 0, 0), (2 * rack, -2 * rack, 0))
    lh = LH_HOLD.lerp(Vector((0, 0, 0)), back)
    hand = trs(tuple(lh + Vector((-PUMP * rack, 0, 0))))
    return dict(gun_off=gun, bolt_off=trs((-PUMP * rack, 0, 0)), l_hand_gun_off=hand, mag_off=HIDE)


def with_hidden_shell(fn):
    def wrapped(i, n):
        d = fn(i, n)
        d.setdefault("mag_off", HIDE)
        return d
    return wrapped


ANIMS = {
    "idle": (60, True, with_hidden_shell(anim_idle)),
    "fire": (21, False, anim_fire),
    "draw": (24, False, with_hidden_shell(anim_draw)),
    "sprint": (30, True, with_hidden_shell(anim_sprint)),
    "reload_start": (12, False, anim_reload_start),
    "reload_hold": (20, True, anim_reload_hold),
    "reload_shell": (15, False, anim_reload_shell),
    "reload_end": (18, False, anim_reload_end),
}


def main():
    import os
    os.makedirs(OUT, exist_ok=True)
    clear()
    build_materials()
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
    arm.animation_data.action = None
    bpy.ops.wm.save_as_mainfile(filepath=r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_shotgun.blend")
    print("built", len(mesh.data.polygons), "polys")


if __name__ == "__main__":
    main()
