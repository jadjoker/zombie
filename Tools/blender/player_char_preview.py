# Renders the character from a few angles. blender -b --factory-startup --python player_char_preview.py
import sys, math
src = open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/player_char.py", encoding="utf-8").read().replace('if __name__ == "__main__":\n    main()', "")
exec(src)
clear(); build_materials(); build_meshes()
arm = build_armature(); mesh = join_and_skin(arm)
s = bpy.context.scene
cam_data = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cam_data); s.collection.objects.link(cam); s.camera = cam
cam_data.lens = 70
for n, e, r in (("sun", 3.5, (0.8, 0.3, 0.5)), ("fill", 1.4, (1.2, -0.4, -2.2))):
    d = bpy.data.lights.new(n, 'SUN'); d.energy = e
    o = bpy.data.objects.new(n, d); s.collection.objects.link(o); o.rotation_euler = r
w = bpy.data.worlds.new("w"); s.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.65, 0.75, 1)
s.render.resolution_x, s.render.resolution_y = 700, 800
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: s.render.engine = eng; break
    except TypeError: pass
for name, ang in (("front", 35), ("side", 100), ("back", 215)):
    a = math.radians(ang); r = 190
    cam.location = (math.cos(a) * r, math.sin(a) * r, 52)
    d = Vector((0, 0, 38)) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    s.render.filepath = f"C:/Users/smitt/Documents/zombie_survival/Tools/blender/char_{name}.png"
    bpy.ops.render.render(write_still=True)
