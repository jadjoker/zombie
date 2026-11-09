extends SceneTree
# Prints the UV range and vertex bounds of the first mesh in a model.
func _init():
	var scene = load(OS.get_cmdline_user_args()[0]).instantiate()
	get_root().add_child(scene)
	for n in scene.find_children("*", "MeshInstance3D", true, false):
		var arr = n.mesh.surface_get_arrays(0)
		var uv = arr[Mesh.ARRAY_TEX_UV]
		if uv == null:
			print(n.name, " no uv")
			continue
		var mn = Vector2(1e9, 1e9)
		var mx = Vector2(-1e9, -1e9)
		for v in uv:
			mn = Vector2(min(mn.x, v.x), min(mn.y, v.y))
			mx = Vector2(max(mx.x, v.x), max(mx.y, v.y))
		print(n.name, " verts ", uv.size(), " uv min ", mn, " max ", mx)
	quit()
