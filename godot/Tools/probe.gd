extends SceneTree
# Prints what an imported model contains: node tree, mesh bounds, materials and animations.
func _init():
	var path = OS.get_cmdline_user_args()[0]
	var scene = load(path).instantiate()
	get_root().add_child(scene)
	_dump(scene, 0)
	quit()

func _dump(n, depth):
	var line = "  ".repeat(depth) + n.name + " [" + n.get_class() + "]"
	if n is Node3D:
		line += " pos=" + str(n.position) + " rot=" + str(n.rotation_degrees) + " scale=" + str(n.scale)
	if n is MeshInstance3D:
		line += " aabb=" + str(n.get_aabb())
		for i in n.mesh.get_surface_count():
			var m = n.mesh.surface_get_material(i)
			line += " mat" + str(i) + "=" + (m.resource_name if m else "none")
	if n is Skeleton3D:
		line += " bones=" + str(n.get_bone_count())
		for b in min(n.get_bone_count(), 40):
			line += "\n" + "  ".repeat(depth+2) + str(b) + " " + n.get_bone_name(b) + " parent=" + str(n.get_bone_parent(b))
	if n is AnimationPlayer:
		for a in n.get_animation_list():
			var anim = n.get_animation(a)
			line += "\n" + "  ".repeat(depth+2) + "anim " + a + " len=" + str(anim.length) + " tracks=" + str(anim.get_track_count()) + " first=" + (str(anim.track_get_path(0)) if anim.get_track_count() > 0 else "")
	print(line)
	for c in n.get_children():
		_dump(c, depth + 1)
