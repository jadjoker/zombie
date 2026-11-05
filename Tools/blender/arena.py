# Builds the sea-floor arena dressing: tileable sand and rock textures, plus rock, reef, cliff,
# coral, kelp, pebble and sand-floor models, exported to Assets/models/arena and Assets/materials/arena.
# Collision stays on the scene's original boxes; these are visuals (see the scene dressing script).
import math, random
import numpy as np
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

ROOT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models"  # export() writes ROOT/<creature>/<part>
MAT = r"C:/Users/smitt/Documents/zombie_survival/Assets/materials/arena"
os.makedirs(MAT, exist_ok=True)
stats = {}
rng = np.random.default_rng(7)
random.seed(7)


# ---------------- Textures (tileable: noise is made in frequency space) ----------------
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


def save(name, rgb):
    img = bpy.data.images.new(name, N, N, alpha=True)
    px = np.ones((N, N, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px[::-1].ravel())
    img.filepath_raw = f"{MAT}/{name}.png"
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def normal_from(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


y, x = np.mgrid[0:N, 0:N] / N
# Sand: soft ripples warped by noise, fine grain, a few darker specks.
warp = fbm(2.2, 1)
ripple = 0.5 + 0.5 * np.sin((y * 6 + warp * 1.6) * 2 * math.pi * 2)
grain = fbm(0.6, 2)
h = ripple * 0.7 + grain * 0.3
base = np.array([0.86, 0.78, 0.6])
shade = 0.86 + 0.14 * ripple[..., None] + (grain[..., None] - 0.5) * 0.12
specks = (fbm(0.3, 3) > 0.82)[..., None] * -0.12
save("sand_color", base * shade + specks)
save("sand_normal", normal_from(h, 6))
save("sand_rough", np.full((N, N, 3), 0.85))

# Rock: mottled, with crack lines and patches of algae (green) in the hollows.
big = fbm(2.0, 4)
mid = fbm(1.4, 5)
cracks = 1 - np.abs(fbm(1.8, 6) - 0.5) * 2
cracks = np.clip((cracks - 0.94) * 12, 0, 1)
h = big * 0.5 + mid * 0.5 - cracks * 0.4
rock = np.array([0.62, 0.66, 0.66]) * (0.75 + 0.35 * h[..., None])
algae = np.clip((fbm(1.6, 8) - 0.6) * 4, 0, 1)[..., None] * (1 - big[..., None])
rock = rock * (1 - algae * 0.6) + np.array([0.35, 0.55, 0.3]) * algae * 0.6
rock -= cracks[..., None] * 0.08
save("rock_color", rock)
save("rock_normal", normal_from(h, 9))
save("rock_rough", np.full((N, N, 3), 0.9))


# ---------------- Models ----------------
def rand_rock(size, lumps, voxel, noise, seed, flat=1.0):
    r = random.Random(seed)
    p = [ellipsoid("core", (0, 0, 0), (size * 0.5, size * 0.47, size * 0.42 * flat))]
    for _ in range(lumps):
        a = r.uniform(0, 2 * math.pi)
        d = r.uniform(0.15, 0.35) * size
        s = r.uniform(0.22, 0.34) * size
        p.append(ellipsoid("lump", (math.cos(a) * d, math.sin(a) * d, r.uniform(-0.1, 0.2) * size * flat),
                           (s * r.uniform(0.9, 1.3), s, s * r.uniform(0.7, 1.0) * flat)))
    return fuse(p, "rock", voxel=voxel, smooth=1, noise=noise, noise_scale=size * 0.18, decimate=0.12)


# Boulders: a unit crate (100 across, centred) becomes a boulder slightly bigger than the box.
for i in range(3):
    clear()
    stats[f"boulder{i}"] = export(rand_rock(112, 5, 2.2, 7, 100 + i), "arena", f"boulder{i}")

# Reef ridge: 300 long along X, ~60 wide, ~75 tall, base at z=0.
clear()
r = random.Random(11)
p = []
for k in range(9):
    xk = -140 + k * 35 + r.uniform(-8, 8)
    s = r.uniform(30, 44)
    p.append(ellipsoid("lump", (xk, r.uniform(-6, 6), s * 0.55), (s * 0.9, s * 0.8, s)))
stats["reef"] = export(fuse(p, "reef", voxel=2.4, smooth=1, noise=6, noise_scale=16, decimate=0.12), "arena", "reef")

# Cliff segment: 620 long along Y, ~170 deep, ~330 tall, base at z=0, inner face toward -X.
for i in range(2):
    clear()
    r = random.Random(20 + i)
    p = []
    for k in range(14):
        yk = r.uniform(-280, 280)
        s = r.uniform(70, 130)
        zk = r.uniform(0.2, 1.0) * 220
        p.append(ellipsoid("mass", (r.uniform(-20, 50), yk, zk), (s * 0.7, s, s * r.uniform(0.8, 1.2))))
    p.append(ellipsoid("base", (10, 0, 30), (90, 330, 90)))
    stats[f"cliff{i}"] = export(fuse(p, "cliff", voxel=6, smooth=1, noise=14, noise_scale=45, decimate=0.1), "arena", f"cliff{i}")


# Branching coral: base at z=0, ~70 tall.
def branch(p, a, direction, length, radius, depth, r):
    b = a + direction * length
    p.append(tube("br", tuple(a), tuple(b), radius, radius * 0.8))
    p.append(ellipsoid("knob", tuple(b), (radius * 0.95,) * 3))
    if depth == 0:
        return
    for _ in range(2 if depth > 1 else 3):
        d = (direction + Vector((r.uniform(-0.8, 0.8), r.uniform(-0.8, 0.8), r.uniform(0.1, 0.6)))).normalized()
        branch(p, b, d, length * 0.75, radius * 0.75, depth - 1, r)


for i in range(2):
    clear()
    r = random.Random(30 + i)
    p = [ellipsoid("foot", (0, 0, 2), (10, 10, 5))]
    for _ in range(3):
        d = Vector((r.uniform(-0.4, 0.4), r.uniform(-0.4, 0.4), 1)).normalized()
        branch(p, Vector((0, 0, 0)), d, 22, 3.6, 2, r)
    stats[f"coral_branch{i}"] = export(fuse(p, "coral", voxel=0.9, smooth=2, noise=0.6, noise_scale=3, decimate=0.15), "arena", f"coral_branch{i}")

# Brain coral: a lumpy dome, ~60 across.
clear()
p = [ellipsoid("dome", (0, 0, 8), (30, 28, 24))]
for k in range(10):
    a = k * 0.63
    p.append(ellipsoid("fold", (math.cos(a) * 16, math.sin(a) * 15, 18), (9, 9, 9)))
stats["coral_brain"] = export(fuse(p, "brain", voxel=1.0, smooth=2, noise=2.2, noise_scale=4, decimate=0.15), "arena", "coral_brain")

# Tube sponges: a cluster of fat tubes with rims, ~80 tall.
clear()
r = random.Random(40)
p = []
for k in range(5):
    a = k * 1.3
    base = Vector((math.cos(a) * 9, math.sin(a) * 9, 0))
    top = base + Vector((math.cos(a) * 8, math.sin(a) * 8, r.uniform(40, 80)))
    p.append(tube("tube", tuple(base), tuple(top), 7, 6))
    p.append(ellipsoid("rim", tuple(top), (7.5, 7.5, 2.5)))
stats["sponge"] = export(fuse(p, "sponge", voxel=0.9, smooth=2, noise=0.5, noise_scale=3, decimate=0.15), "arena", "sponge")

# Kelp: a wavy strand with alternating leaves, base at z=0, ~260 tall (the code sways it).
for i in range(2):
    clear()
    r = random.Random(50 + i)
    p = []
    prev = Vector((0, 0, 0))
    for k in range(1, 14):
        z = k * 20
        cur = Vector((math.sin(k * 0.7 + i) * 6, math.cos(k * 0.5) * 4, z))
        p.append(tube("stem", tuple(prev), tuple(cur), 2.2, 2.0))
        side = 1 if k % 2 else -1
        leaf_at = cur + Vector((0, side * 7, 2))
        leaf = ellipsoid("leaf", tuple(leaf_at), (1.4, 5, 19))
        leaf.rotation_euler = (math.radians(-side * 28), 0, 0)
        p.append(leaf)
        prev = cur
    p.append(ellipsoid("float", tuple(prev + Vector((0, 0, 4))), (4, 4, 6)))
    stats[f"kelp{i}"] = export(fuse(p, "kelp", voxel=1.0, smooth=2, noise=0.3, noise_scale=3, decimate=0.12), "arena", f"kelp{i}")

# Pebble patch: small stones scattered in a 70-wide patch, base at z=0.
clear()
r = random.Random(60)
p = []
for k in range(9):
    s = r.uniform(4, 10)
    p.append(ellipsoid("pebble", (r.uniform(-35, 35), r.uniform(-35, 35), s * 0.3), (s, s * 0.85, s * 0.6)))
stats["pebbles"] = export(fuse(p, "pebbles", voxel=0.8, smooth=1, noise=0.8, noise_scale=3, decimate=0.2), "arena", "pebbles")

# Sand floor: 3000 square, flat in the play area (0..3 high, it sits on the collision floor),
# rising into dunes past the zombie spawns at the walls. UVs tile every 220 units.
clear()
bpy.ops.mesh.primitive_grid_add(x_subdivisions=120, y_subdivisions=120, size=3000)
floor = bpy.context.active_object
floor.name = "sand"
dn = fbm(2.4, 70)
for v in floor.data.vertices:
    u, w = (v.co.x / 3000 + 0.5) % 1, (v.co.y / 3000 + 0.5) % 1
    n = dn[int(w * (N - 1)), int(u * (N - 1))]
    edge = max(abs(v.co.x), abs(v.co.y))
    dune = max(0.0, (edge - 1380) / 120) ** 2 * 45
    v.co.z = 0.4 + n * 2.6 + dune * (0.6 + n)
uv = floor.data.uv_layers.new(name="UVMap")
for loop in floor.data.loops:
    co = floor.data.vertices[loop.vertex_index].co
    uv.data[loop.index].uv = (co.x / 220, co.y / 220)
bpy.ops.object.shade_smooth()
stats["sand_floor"] = export(floor, "arena", "sand_floor")
result = stats
