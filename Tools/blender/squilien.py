# Builds Squilien's bell and tentacle piece and exports them to Assets/models/creatures/squilien.
# Both are modelled at the size of the dev sphere they replace (radius 32), because Squilien.cs
# scales them every frame as if they were that sphere (the bell squeezes on each swim stroke,
# tentacle pieces are stretched along +X between joints).
import math
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

R = 32.0
stats = {}


def on_sphere(azimuth_deg, elevation_deg, radius):
    a, e = math.radians(azimuth_deg), math.radians(elevation_deg)
    return (math.cos(a) * math.cos(e) * radius, math.sin(a) * math.cos(e) * radius, math.sin(e) * radius)


# ---- Bell: a dome with knobbly radial ribs and a scalloped rim. The front (where the big eyes sit,
# about 25 degrees either side of forward, slightly below the middle) is left smooth.
clear()
p = [ellipsoid("dome", (0, 0, 0), (R, R, R))]
for az in (0, 60, 120, 180, 240, 300):
    for el in (15, 30, 45, 60, 72):
        if az == 0 and el < 40:
            continue  # keep the face clear
        size = R * (0.11 - el * 0.0006)
        p.append(ellipsoid("rib", on_sphere(az, el, R * 0.99), (size, size, size)))
for k in range(12):
    az = k * 30 + 15
    p.append(ellipsoid("lobe", on_sphere(az, -18, R * 0.95), (R * 0.2, R * 0.2, R * 0.15)))
p.append(ellipsoid("cap", (0, 0, R * 0.78), (R * 0.42, R * 0.42, R * 0.3)))
stats["bell"] = export(fuse(p, "squilien_bell", voxel=0.6, smooth=4, noise=0.5, noise_scale=5, decimate=0.1), "squilien", "bell")

# ---- Tentacle piece (along +X, stretched by the code): ringed, with suckers underneath.
clear()
p = [ellipsoid("core", (0, 0, 0), (R, R * 0.88, R * 0.88))]
for x in (-0.55, 0.0, 0.55):
    p.append(ellipsoid("ring", (x * R, 0, 0), (R * 0.16, R * 0.97, R * 0.97)))
for x in (-0.5, 0.0, 0.5):
    p.append(ellipsoid("sucker", (x * R, 0, -R * 0.82), (R * 0.2, R * 0.2, R * 0.16)))
stats["tentacle"] = export(fuse(p, "squilien_tentacle", voxel=0.9, smooth=4, noise=0.4, noise_scale=5, decimate=0.06), "squilien", "tentacle")
# ---- Eyes: classic alien eyes. Big black almonds that taper to a point on the outer side, which
# droops. Modelled facing +X with the pivot at the eye centre; one per side (mirrored).
def alien_eye(side):
    """Round tilted oval, classic cartoon alien eye."""
    import bmesh
    clear()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32, radius=1)
    o = bpy.context.active_object
    o.name = f"squilien_eye_{'l' if side > 0 else 'r'}"
    L, H, T, K = 11.0, 8.2, 4.2, 3.2
    bm = bmesh.new(); bm.from_mesh(o.data)
    for v in bm.verts:
        x, y, z = v.co
        t = y * side  # outer side positive
        # plump tilted oval: fuller on the outer-top, flatter bottom on the inner side
        hf = 1.0 + 0.12 * t
        tf = max(0.0, 1 - t * t) ** 0.5
        zz = z * H * hf
        if zz < 0: zz *= 1 - 0.15 * min(1.0, max(0.0, -t)) ** 2
        v.co = (x * T * (0.4 + 0.6 * tf), y * L, zz + K * t)
    bm.to_mesh(o.data); bm.free()
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02); bpy.ops.object.mode_set(mode='OBJECT')
    return o


stats["eye_l"] = export(alien_eye(1), "squilien", "eye_l")
stats["eye_r"] = export(alien_eye(-1), "squilien", "eye_r")
result = stats
