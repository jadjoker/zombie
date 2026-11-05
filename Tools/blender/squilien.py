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
stats["bell"] = export(fuse(p, "squilien_bell", voxel=1.0, smooth=3, noise=0.6, noise_scale=6, decimate=0.18), "squilien", "bell")

# ---- Tentacle piece (along +X, stretched by the code): ringed, with suckers underneath.
clear()
p = [ellipsoid("core", (0, 0, 0), (R, R * 0.88, R * 0.88))]
for x in (-0.55, 0.0, 0.55):
    p.append(ellipsoid("ring", (x * R, 0, 0), (R * 0.16, R * 0.97, R * 0.97)))
for x in (-0.5, 0.0, 0.5):
    p.append(ellipsoid("sucker", (x * R, 0, -R * 0.82), (R * 0.2, R * 0.2, R * 0.16)))
stats["tentacle"] = export(fuse(p, "squilien_tentacle", voxel=1.6, smooth=3, noise=0.5, noise_scale=6, decimate=0.08), "squilien", "tentacle")
result = stats
