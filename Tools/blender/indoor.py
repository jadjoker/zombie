# Builds indoor props for Risk of Copyright: crates, barrels and pillars.
# Exported to Assets/models/indoor as FBX, with a matching .vmdl beside each one.
# The .vmdl uses a plain default material; the scene tints each instance.
import math, os
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

ROOT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models"
OUT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/indoor"
os.makedirs(OUT, exist_ok=True)
MAT = "materials/default/white.vmat"
stats = {}

VMDL = '''<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->
{
	rootNode = 
	{
		_class = "RootNode"
		children = 
		[
			{
				_class = "MaterialGroupList"
				children = 
				[
					{
						_class = "DefaultMaterialGroup"
						remaps = [  ]
						use_global_default = true
						global_default_material = "__MAT__"
					},
				]
			},
			{
				_class = "RenderMeshList"
				children = 
				[
					{
						_class = "RenderMeshFile"
						filename = "models/indoor/__PART__.fbx"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, -90.0, 0.0 ]
						import_scale = 1.0
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter = 
						{
							exclude_by_default = false
							exception_list = [  ]
						}
					},
				]
			},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}
}
'''


def emit(obj, name):
    stats[name] = export(obj, "indoor", name)
    with open(f"{OUT}/{name}.vmdl", "w") as f:
        f.write(VMDL.replace("__PART__", name).replace("__MAT__", MAT))
    return stats[name]


# ---- Concrete textures (tileable, generated in frequency space like arena.py) ----
import numpy as np
TEXDIR = r"C:/Users/smitt/Documents/zombie_survival/Assets/materials/indoor"
os.makedirs(TEXDIR, exist_ok=True)
N = 512


def fbm(power=2.0, seed=0):
    g = np.random.default_rng(seed)
    f = np.fft.fftfreq(N)
    k = np.sqrt(f[:, None] ** 2 + f[None, :] ** 2)
    k[0, 0] = 1
    spec = (g.normal(size=(N, N)) + 1j * g.normal(size=(N, N))) / k ** power
    spec[0, 0] = 0
    n = np.real(np.fft.ifft2(spec))
    return (n - n.min()) / (n.max() - n.min())


def save_tex(name, rgb):
    img = bpy.data.images.new(name, N, N, alpha=True)
    px = np.ones((N, N, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px[::-1].ravel())
    img.filepath_raw = f"{TEXDIR}/{name}.png"
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def normal_from(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


big = fbm(2.2, 11)
mid = fbm(1.4, 12)
grain = fbm(0.5, 13)
cracks = 1 - np.abs(fbm(1.8, 14) - 0.5) * 2
cracks = np.clip((cracks - 0.955) * 16, 0, 1)
h = big * 0.5 + mid * 0.4 + grain * 0.1 - cracks * 0.5
base = np.array([0.58, 0.58, 0.6])
col = base + (big[..., None] - 0.5) * 0.16 + (mid[..., None] - 0.5) * 0.1 + (grain[..., None] - 0.5) * 0.05
col -= cracks[..., None] * 0.18
save_tex("concrete_color", col)
save_tex("concrete_normal", normal_from(h, 5))
save_tex("concrete_rough", np.full((N, N, 3), 0.92))


# ---- Models (each sits on z=0, origin at its base) ----
def crate(seed, s):
    t = s * 0.09
    p = [box("body", (0, 0, s * 0.5), (s - t, s - t, s - t), bevel=s * 0.03)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(box("batten", (sx * (s * 0.5 - t * 0.5), sy * (s * 0.5 - t * 0.5), s * 0.5), (t, t, s), bevel=2))
    for z in (s * 0.07, s * 0.93):
        for sy in (-1, 1):
            p.append(box("railX", (0, sy * (s * 0.5 - t * 0.5), z), (s - t * 0.5, t, t * 1.1), bevel=1))
        for sx in (-1, 1):
            p.append(box("railY", (sx * (s * 0.5 - t * 0.5), 0, z), (t, s - t * 0.5, t * 1.1), bevel=1))
    for sy in (-1, 1):
        br = box("brace", (0, sy * (s * 0.5 - t * 0.4), s * 0.5), (s * 0.95, t * 0.7, t * 0.9), bevel=1)
        br.rotation_euler = (0, math.radians(45), 0)
        p.append(br)
    for sx in (-1, 1):
        br = box("brace", (sx * (s * 0.5 - t * 0.4), 0, s * 0.5), (t * 0.7, s * 0.95, t * 0.9), bevel=1)
        br.rotation_euler = (math.radians(45), 0, 0)
        p.append(br)
    return assemble(p, "crate", smooth=False)


def barrel(seed, r, h):
    body = fuse([tube("body", (0, 0, 0), (0, 0, h), r * 0.9, r * 0.9, verts=28),
                 ellipsoid("belly", (0, 0, h * 0.5), (r * 1.08, r * 1.08, h * 0.44))],
                "barrel", voxel=max(1.4, r * 0.06), smooth=2, noise=r * 0.03, noise_scale=4, decimate=0.3)
    parts = [body]
    for z in (h * 0.16, h * 0.5, h * 0.84):
        parts.append(cyl("hoop", (0, 0, z), r * 1.1, r * 1.1, h * 0.05, verts=28))
    parts.append(cyl("lid", (0, 0, h * 0.99), r * 0.8, r * 0.8, h * 0.03, verts=28))
    return assemble(parts, "barrel", smooth=True)


def pillar(seed, r, h):
    p = [box("plinth", (0, 0, h * 0.03), (r * 3.4, r * 3.4, h * 0.06), bevel=r * 0.1),
         cyl("base", (0, 0, h * 0.1), r * 1.3, r * 1.15, h * 0.08, verts=24),
         cyl("shaft", (0, 0, h * 0.5), r, r * 0.9, h * 0.76, verts=24),
         cyl("echinus", (0, 0, h * 0.9), r * 0.9, r * 1.25, h * 0.06, verts=24),
         box("abacus", (0, 0, h * 0.955), (r * 2.9, r * 2.9, h * 0.05), bevel=r * 0.08)]
    return assemble(p, "pillar", smooth=True)


def shelf(seed, w, h, d):
    t = d * 0.1
    p = [box("back", (0, -d * 0.5 + t * 0.5, h * 0.5), (w, t, h), bevel=1),
         box("sideL", (-w * 0.5 + t * 0.5, 0, h * 0.5), (t, d, h), bevel=1),
         box("sideR", (w * 0.5 - t * 0.5, 0, h * 0.5), (t, d, h), bevel=1),
         box("top", (0, 0, h - t * 0.5), (w, d, t * 1.2), bevel=1),
         box("plinth", (0, 0, t * 0.5), (w, d, t), bevel=1)]
    for i in range(3):
        z = h * (0.3 + i * 0.26)
        p.append(box("board", (0, 0, z), (w - t * 2, d * 0.92, t), bevel=1))
    return assemble(p, "shelf", smooth=False)


def table(seed, w, d, h):
    t = h * 0.07
    lr = 9
    p = [box("top", (0, 0, h - t * 0.5), (w, d, t), bevel=2)]
    p.append(box("apronX1", (0, d * 0.5 - 9, h - t - 10), (w - lr * 2, 12, 18)))
    p.append(box("apronX2", (0, -(d * 0.5 - 9), h - t - 10), (w - lr * 2, 12, 18)))
    p.append(box("apronY1", (w * 0.5 - 9, 0, h - t - 10), (12, d - lr * 2, 18)))
    p.append(box("apronY2", (-(w * 0.5 - 9), 0, h - t - 10), (12, d - lr * 2, 18)))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(box("leg", (sx * (w * 0.5 - 12), sy * (d * 0.5 - 12), (h - t) * 0.5), (lr, lr, h - t), bevel=1))
    p.append(box("stretchX", (0, 0, (h - t) * 0.3), (w - 30, 8, 8)))
    p.append(box("stretchY", (0, 0, (h - t) * 0.3), (8, d - 30, 8)))
    return assemble(p, "table", smooth=False)


def locker(seed, w, h, d):
    p = [box("body", (0, 0, h * 0.5), (w, h, d), bevel=2),
         box("foot", (0, 0, 4), (w * 1.02, d * 1.02, 8)),
         box("door", (0, -d * 0.5 - 1, h * 0.5), (w * 0.9, 3, h * 0.92), bevel=1),
         box("handle", (w * 0.32, -d * 0.5 - 5, h * 0.5), (5, 6, 30))]
    for z in (h * 0.72, h * 0.3):
        p.append(box("vent", (0, -d * 0.5 - 2.5, z), (w * 0.55, 2, 5)))
    return assemble(p, "locker", smooth=False)


clear(); emit(crate(801, 72), "crate_a")
clear(); emit(crate(802, 48), "crate_b")
clear(); emit(barrel(803, 30, 92), "barrel_a")
clear(); emit(pillar(804, 42, 420), "pillar_a")
clear(); emit(shelf(901, 130, 200, 34), "shelf_a")
clear(); emit(table(902, 170, 90, 78), "table_a")
clear(); emit(locker(903, 70, 200, 40), "locker_a")

result = stats
