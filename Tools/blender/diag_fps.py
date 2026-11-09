import sys, math
src = open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_rifle.py", encoding="utf-8").read().replace("\nmain()\n", "\n")
exec(src)
clear(); build_materials(); build_meshes()
arm = build_armature(); mesh = join_and_skin(arm); rotate_rig(arm, mesh)
for name in ANIMS:
    n = ANIMS[name][0]
    new_action(arm, name)
    out = {}
    prev = {}
    worst = {}
    for i in range(n):
        pose_frame(arm, i + 1, **ANIMS[name][2](i, n))
        for b in arm.pose.bones:
            q = b.matrix_basis.to_quaternion()
            e = b.matrix_basis.to_euler('XYZ')
            if i == 0:
                out[b.name] = (tuple(round(math.degrees(a)) for a in e), tuple(round(v, 2) for v in q))
            if b.name in prev:
                d = (q.inverted() @ prev[b.name]).angle
                worst[b.name] = max(worst.get(b.name, 0), math.degrees(d))
            prev[b.name] = q.copy()
    print("ANIM", name, "frame0:")
    for k in ("upper_L", "lower_L", "hand_L", "upper_R", "lower_R", "gun"):
        print("  ", k, out[k], "max step deg", round(worst.get(k, 0), 1))
