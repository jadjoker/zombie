# First-person pump shotgun with the same gloved arms as the rifle, in the same cartoon style.
# Reuses fps_rifle.py's rig, solver, export and vmdl writer; only the gun, its parts and the animations differ.
#   blender -b --factory-startup --python fps_shotgun.py
#
# Bones: "bolt" is the pump (it slides back and forward, the left hand riding it), "mag" is a loose shell that only
# shows while the left hand is feeding it into the loading port.
import math

src = open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_rifle.py", encoding="utf-8").read()
src = src.replace("\nmain()\n", "\n")
src = src.replace("relative_origin = [ 0.0, 36.0, 3.0 ]", "relative_origin = [ 0.0, 40.0, 5.2 ]")
exec(src)

OUT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/weapons/shotgun"
NAME = "shotgun"


def build_meshes():
    # Shotgun (gun space: grip at origin, +X forward)
    bevel_box((4.5, 0, 3.0), (13, 3.8, 5.4), 0.8, "gun", "metal")                      # receiver
    bevel_box((4.5, -1.95, 1.4), (6.0, 0.4, 2.0), 0.15, "gun", "metal2")               # ejection port plate
    x_cyl((24.0, 0, 5.0), 1.05, 31.0, "gun", "metal")                                  # barrel
    x_cyl((21.5, 0, 2.5), 1.25, 25.0, "gun", "metal2")                                 # magazine tube
    x_cyl((34.6, 0, 2.5), 1.45, 1.4, "gun", "metal")                                   # tube cap
    bevel_box((33.0, 0, 3.8), (1.4, 2.8, 3.6), 0.4, "gun", "metal")                    # barrel clamp
    ball((38.7, 0, 6.4), (0.6, 0.6, 0.6), "gun", "accent", 12)                         # bead sight
    x_cyl((39.4, 0, 5.0), 1.35, 1.2, "gun", "metal2")                                  # muzzle ring
    bevel_box((-6.0, 0, 1.8), (8.5, 3.3, 5.6), 1.2, "gun", "accent", rot=(0, 5, 0))    # stock
    bevel_box((-10.4, 0, 1.4), (1.4, 3.5, 6.6), 0.6, "gun", "metal", rot=(0, 5, 0))   # butt pad
    bevel_box((-0.6, 0, -4.2), (3.4, 3.2, 8.4), 1.0, "gun", "accent", rot=(0, -16, 0)) # pistol grip
    bevel_box((3.2, 0, -0.9), (2.4, 0.8, 2.6), 0.3, "gun", "metal2")                   # trigger guard
    # the pump: chunky grooved fore-end
    bevel_box((17.0, 0, 2.3), (10.5, 4.6, 4.4), 1.4, "bolt", "accent")
    for x in (13.4, 15.2, 17.0, 18.8, 20.6):
        bevel_box((x, 0, 2.3), (0.6, 4.9, 4.7), 0.25, "bolt", "metal")
    # the loose shell (sits in the left fist; hidden unless it's being loaded)
    x_cyl((15.0, 0, -6.4), 1.0, 3.8, "mag", "accent", 16)
    x_cyl((17.1, 0, -6.4), 1.05, 0.7, "mag", "metal2", 16)

    for obj, _, _ in PARTS:
        obj.matrix_world = G_REST @ obj.matrix_world
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)

    # Arms and hands: the same as the rifle's (right on the grip, left under the pump)
    for s in ("R", "L"):
        sh = SHOULDER[s]
        wr = G_REST @ HAND_LOCAL[s]
        el, wr = solve_elbow(sh, wr, POLE[s])
        ball(sh, (4.2, 4.2, 4.2), f"upper_{s}", "sleeve")
        tube(sh, el, 4.0, 3.5, f"upper_{s}", "sleeve")
        ball(el, (3.7, 3.7, 3.7), f"lower_{s}", "sleeve")
        tube(el, wr, 3.5, 2.8, f"lower_{s}", "sleeve")
        ball(wr, (3.4, 3.4, 3.4), f"hand_{s}", "glove")
        if s == "R":
            c = gl((0.6, 0.0, -2.5))
            ball(c, (3.5, 3.1, 4.1), f"hand_{s}", "glove")
            ball(c + G_REST.to_3x3() @ Vector((2.4, 0.0, 0.6)), (2.3, 3.0, 2.4), f"hand_{s}", "glove")
            ball(c + G_REST.to_3x3() @ Vector((1.0, 2.7, 1.6)), (2.4, 1.3, 1.5), f"hand_{s}", "glove")
        else:
            c = gl((15.5, 0.0, -2.2))
            ball(c, (4.6, 3.4, 3.4), f"hand_{s}", "glove")
            ball(c + G_REST.to_3x3() @ Vector((0.0, -2.6, 2.6)), (3.6, 1.6, 1.8), f"hand_{s}", "glove")
            ball(c + G_REST.to_3x3() @ Vector((1.0, 2.6, 1.4)), (2.6, 1.3, 1.5), f"hand_{s}", "glove")


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
