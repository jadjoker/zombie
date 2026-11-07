# Builds forest dressing models for Risk of Copyright: trees (trunk + canopy), boulders, bushes
# and grass tufts. Exported to Assets/models/forest as FBX, with a matching .vmdl beside each one.
# The .vmdl uses a plain default material; the scene tints each instance (bark brown, leaves green...).
import math, random, os
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

ROOT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models"
OUT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/forest"
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
						filename = "models/forest/__PART__.fbx"
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
    stats[name] = export(obj, "forest", name)
    with open(f"{OUT}/{name}.vmdl", "w") as f:
        f.write(VMDL.replace("__PART__", name).replace("__MAT__", MAT))
    return stats[name]


# ---------------- Models ----------------
def trunk(seed, height, r0, r1, lean=0.0):
    r = random.Random(seed)
    p = [tube("trunk", (0, 0, 0), (lean, 0, height), r0, r1, verts=14)]
    for k in range(4):
        a = k * 1.57 + r.uniform(-0.3, 0.3)
        p.append(ellipsoid("root", (math.cos(a) * r0 * 1.15, math.sin(a) * r0 * 1.15, r0 * 0.5), (r0 * 0.75, r0 * 0.75, r0 * 0.55)))
    for k in range(3):
        a = r.uniform(0, 2 * math.pi)
        base = (math.cos(a) * r1 * 0.4, math.sin(a) * r1 * 0.4, height * r.uniform(0.55, 0.85))
        p.append(cone("branch", base, (math.cos(a), math.sin(a), r.uniform(0.5, 1.0)), r1 * 0.8, height * 0.2, verts=8))
    return fuse(p, "trunk", voxel=max(1.8, r0 * 0.085), smooth=2, noise=r0 * 0.04, noise_scale=6, decimate=0.4)


def canopy(seed, w, h, lumps):
    r = random.Random(seed)
    p = [ellipsoid("core", (0, 0, 0), (w * 0.5, w * 0.47, h * 0.5))]
    for k in range(lumps):
        a = r.uniform(0, 2 * math.pi)
        d = r.uniform(0.18, 0.5) * w
        s = r.uniform(0.28, 0.46) * w
        p.append(ellipsoid("lump", (math.cos(a) * d, math.sin(a) * d, r.uniform(-0.28, 0.3) * h), (s, s * r.uniform(0.85, 1.1), s * r.uniform(0.7, 1.0))))
    return fuse(p, "canopy", voxel=max(3.0, w * 0.035), smooth=2, noise=w * 0.04, noise_scale=9, decimate=0.3)


def rock(seed, size, lumps):
    r = random.Random(seed)
    p = [ellipsoid("core", (0, 0, 0), (size * 0.5, size * 0.47, size * 0.42))]
    for k in range(lumps):
        a = r.uniform(0, 2 * math.pi)
        d = r.uniform(0.15, 0.34) * size
        s = r.uniform(0.22, 0.34) * size
        p.append(ellipsoid("lump", (math.cos(a) * d, math.sin(a) * d, r.uniform(-0.08, 0.2) * size), (s * r.uniform(0.9, 1.3), s, s * r.uniform(0.7, 1.0))))
    return fuse(p, "rock", voxel=max(0.9, size * 0.03), smooth=2, noise=size * 0.012, noise_scale=4, decimate=0.2)


def bush(seed, w):
    r = random.Random(seed)
    p = [ellipsoid("core", (0, 0, w * 0.3), (w * 0.5, w * 0.5, w * 0.4))]
    for k in range(4):
        a = r.uniform(0, 2 * math.pi)
        d = r.uniform(0.2, 0.5) * w
        s = r.uniform(0.28, 0.42) * w
        p.append(ellipsoid("lump", (math.cos(a) * d, math.sin(a) * d, r.uniform(0.1, 0.5) * w), (s, s, s * r.uniform(0.7, 0.95))))
    return fuse(p, "bush", voxel=max(2.5, w * 0.05), smooth=2, noise=w * 0.03, noise_scale=7, decimate=0.3)


def grass(seed, blades, h):
    r = random.Random(seed)
    p = [ellipsoid("base", (0, 0, h * 0.12), (16, 16, h * 0.14))]
    for k in range(blades):
        a = r.uniform(0, 2 * math.pi)
        d = r.uniform(2, 11)
        base = (math.cos(a) * d, math.sin(a) * d, h * 0.1)
        top = Vector(base) + Vector((r.uniform(-16, 16), r.uniform(-16, 16), h * r.uniform(0.7, 1.1)))
        p.append(tube("blade", base, tuple(top), r.uniform(3.5, 5.0), 0.8, verts=6))
    return fuse(p, "grass", voxel=max(1.6, h * 0.03), smooth=2, noise=h * 0.02, noise_scale=5, decimate=0.3)


clear(); emit(trunk(101, 430, 34, 16), "tree_trunk_a")
clear(); emit(trunk(102, 560, 30, 13, lean=26), "tree_trunk_b")
clear(); emit(canopy(201, 320, 250, 7), "tree_canopy_a")
clear(); emit(canopy(202, 430, 300, 9), "tree_canopy_b")
clear(); emit(rock(301, 130, 5), "rock_a")
clear(); emit(rock(302, 210, 7), "rock_b")
clear(); emit(bush(401, 150), "bush_a")
clear(); emit(grass(501, 14, 72), "grass_a")

def pine_trunk(seed, height, r0):
    r = random.Random(seed)
    p = [tube("trunk", (0, 0, 0), (0, 0, height), r0, r0 * 0.4, verts=12)]
    for k in range(3):
        a = k * 2.09
        p.append(ellipsoid("root", (math.cos(a) * r0 * 1.1, math.sin(a) * r0 * 1.1, r0 * 0.4), (r0 * 0.6, r0 * 0.6, r0 * 0.4)))
    for k in range(4):
        a = r.uniform(0, 2 * math.pi)
        base = (math.cos(a) * r0 * 0.3, math.sin(a) * r0 * 0.3, height * r.uniform(0.3, 0.7))
        p.append(cone("branch", base, (math.cos(a), math.sin(a), -0.3), r0 * 0.4, height * 0.1, verts=6))
    return fuse(p, "trunk", voxel=max(1.8, r0 * 0.09), smooth=2, noise=r0 * 0.05, noise_scale=6, decimate=0.4)


def pine_canopy(seed, w, h):
    p = [tube("cone", (0, 0, -h * 0.5), (0, 0, h * 0.5), w * 0.5, w * 0.05, verts=16)]
    tiers = 5
    for k in range(tiers):
        t = k / (tiers - 1)
        z = -h * 0.45 + h * 0.9 * (0.1 + 0.9 * t)
        rad = w * 0.5 * (0.3 + 0.7 * (1 - t))
        p.append(ellipsoid("tier", (0, 0, z), (rad, rad, rad * 0.42)))
    return fuse(p, "pine", voxel=max(3.0, w * 0.03), smooth=2, noise=w * 0.02, noise_scale=8, decimate=0.3)


def log_model(seed, length, radius):
    r = random.Random(seed)
    p = [tube("log", (-length * 0.5, 0, radius), (length * 0.5, 0, radius), radius, radius * 0.85, verts=14)]
    for k in range(2):
        x = r.uniform(-length * 0.3, length * 0.3)
        a = r.uniform(0, 2 * math.pi)
        base = (x, math.cos(a) * radius * 0.5, radius + math.sin(a) * radius * 0.5)
        p.append(cone("branch", base, (r.uniform(-0.5, 0.5), math.cos(a), 0.7), radius * 0.4, radius * 2.2, verts=8))
    return fuse(p, "log", voxel=max(2.0, radius * 0.12), smooth=2, noise=radius * 0.08, noise_scale=5, decimate=0.35)


def stump(seed, radius, height):
    r = random.Random(seed)
    p = [tube("stump", (0, 0, 0), (0, 0, height), radius, radius * 0.9, verts=14)]
    for k in range(5):
        a = k * 1.2566 + r.uniform(-0.2, 0.2)
        p.append(ellipsoid("root", (math.cos(a) * radius * 1.2, math.sin(a) * radius * 1.2, 6), (radius * 0.7, radius * 0.7, radius * 0.5)))
    return fuse(p, "stump", voxel=max(1.8, radius * 0.12), smooth=2, noise=radius * 0.08, noise_scale=4, decimate=0.35)


def fern(seed, fronds, length):
    r = random.Random(seed)
    p = [ellipsoid("crown", (0, 0, length * 0.12), (16, 16, 12))]
    for k in range(fronds):
        a = k / fronds * 2 * math.pi + r.uniform(-0.15, 0.15)
        prev = Vector((0, 0, length * 0.12))
        for j in range(1, 5):
            t = j / 4.0
            nxt = Vector((math.cos(a) * length * t, math.sin(a) * length * t, length * 0.12 + length * 0.5 * math.sin(t * 1.3)))
            p.append(tube("frond", tuple(prev), tuple(nxt), 9 - 5 * t, 7 - 5 * t, verts=8))
            side = Vector((-math.sin(a), math.cos(a), 0)) * (12 * (1 - t))
            p.append(ellipsoid("leaf", tuple(nxt + side), (10 * (1 - t) + 5, 7 * (1 - t) + 4, 4)))
            p.append(ellipsoid("leaf", tuple(nxt - side), (10 * (1 - t) + 5, 7 * (1 - t) + 4, 4)))
            prev = nxt
    return fuse(p, "fern", voxel=max(1.8, length * 0.022), smooth=2, noise=length * 0.02, noise_scale=6, decimate=0.3)


def flower_bush(seed, w):
    r = random.Random(seed)
    p = [ellipsoid("core", (0, 0, w * 0.28), (w * 0.5, w * 0.5, w * 0.34))]
    for k in range(6):
        a = r.uniform(0, 2 * math.pi)
        d = r.uniform(0.15, 0.5) * w
        s = r.uniform(0.24, 0.4) * w
        p.append(ellipsoid("lump", (math.cos(a) * d, math.sin(a) * d, r.uniform(0.15, 0.6) * w), (s, s, s * 0.8)))
    return fuse(p, "flowerbush", voxel=max(2.5, w * 0.05), smooth=2, noise=w * 0.03, noise_scale=7, decimate=0.3)


clear(); emit(pine_trunk(103, 620, 26), "tree_trunk_c")
clear(); emit(pine_canopy(203, 300, 460), "tree_canopy_c")
clear(); emit(log_model(701, 360, 46), "log_a")
clear(); emit(stump(702, 46, 92), "stump_a")
clear(); emit(fern(703, 7, 110), "fern_a")
clear(); emit(flower_bush(704, 130), "flowerbush_a")
clear(); emit(rock(303, 72, 4), "rock_c")

result = stats
