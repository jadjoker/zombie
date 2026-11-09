extends SceneTree
# Prints a skinned model: node tree, every bone with its global rest position, and each mesh surface material name.
func _init():
	var scene = load(OS.get_cmdline_user_args()[0]).instantiate()
	get_root().add_child(scene)
	for n in scene.find_children("*", "Node3D", true, false):
		print(n.name, " [", n.get_class(), "] rot=", n.rotation_degrees, " scale=", n.scale)
		if n is Skeleton3D:
			for b in n.get_bone_count():
				print("  bone ", b, " ", n.get_bone_name(b), " parent=", n.get_bone_parent(b), " rest=", n.get_bone_global_rest(b).origin)
		if n is MeshInstance3D:
			for i in n.mesh.get_surface_count():
				var m = n.mesh.surface_get_material(i)
				print("  surface ", i, " ", m.resource_name if m else "none")
	quit()
