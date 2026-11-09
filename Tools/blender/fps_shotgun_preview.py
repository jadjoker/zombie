# Renders the shotgun viewmodel from the player's camera at chosen animation frames.
#   blender -b --factory-startup --python fps_shotgun_preview.py -- idle:1 fire:3 reload:40 ...
import sys
src = open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_shotgun.py", encoding="utf-8").read()
src = src.replace("if __name__ == \"__main__\":", "if False:")
exec(src)

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["idle:1"]
clear()
build_materials()
build_meshes()
arm = build_armature()
mesh = join_and_skin(arm)
rotate_rig(arm, mesh)

# camera at the player's eye
cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
cam.location = (0, 0, 0)
cam.rotation_euler = (math.radians(90), 0, math.radians(-90))     # looks along +X, up is +Z
cam_data.sensor_fit = 'HORIZONTAL'
cam_data.angle = math.radians(90)
cam_data.clip_start = 0.5
bpy.context.scene.camera = cam
ld = bpy.data.lights.new("sun", 'SUN'); ld.energy = 3.5
l = bpy.data.objects.new("sun", ld); bpy.context.scene.collection.objects.link(l); l.rotation_euler = (0.8, 0.3, 0.5)
ld2 = bpy.data.lights.new("fill", 'SUN'); ld2.energy = 1.2
l2 = bpy.data.objects.new("fill", ld2); bpy.context.scene.collection.objects.link(l2); l2.rotation_euler = (1.2, -0.4, -2.2)
w = bpy.data.worlds.new("w"); bpy.context.scene.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.38, 0.55, 0.68, 1)
s = bpy.context.scene
s.render.resolution_x = 1024
s.render.resolution_y = 576
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        s.render.engine = eng
        break
    except TypeError:
        pass

for a in args:
    name, frame = a.split(":")
    frame = int(frame)
    n, loop, fn = ANIMS[name]
    new_action(arm, "preview")
    pose_frame(arm, frame, **fn(frame - 1, n))
    # the preview camera looks along +X, so undo the export rotation for display
    arm.rotation_euler = (0, 0, math.pi / 2)
    mesh.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    s.render.filepath = f"C:/Users/smitt/Documents/zombie_survival/Tools/blender/shotgun_{name}_{frame}.png"
    bpy.ops.render.render(write_still=True)
    print("rendered", s.render.filepath)
