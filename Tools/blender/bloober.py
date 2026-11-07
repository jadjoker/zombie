# Builds Bloober's head and body segments and exports them to Assets/models/creatures/bloober.
# They're modelled at the same size as the dev sphere they replace (radius 32 along every axis,
# facing +X), because Bloober.cs scales each segment's skin every frame as if it were that sphere.
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

R = 32.0
stats = {}


def segment(variant):
    clear()
    p = [ellipsoid("core", (0, 0, 0), (R, R * 0.97, R * 0.9))]
    # Overlapping armour bands round the body, slightly raised, so it reads as segmented and tough.
    for x in (-0.55, 0.0, 0.55):
        p.append(ellipsoid("band", (x * R, 0, 0), (R * 0.2, R * 1.02, R * 0.93)))
    # A dorsal ridge of short swept-back spines.
    for x in ((-0.45, 0.05, 0.55) if variant == 0 else (-0.25, 0.3)):
        p.append(cone("dorsal", (x * R, 0, R * 0.82), (-0.45, 0, 1), R * 0.13, R * 0.42))
    # Little lateral bumps (where the glowing spots sit).
    for side in (-1, 1):
        for x in ((-0.35, 0.35) if variant == 0 else (0.0,)):
            p.append(ellipsoid("bump", (x * R, side * R * 0.88, R * 0.15), (R * 0.16, R * 0.16, R * 0.16)))
    # Flatter belly.
    p.append(ellipsoid("belly", (0, 0, -R * 0.45), (R * 0.9, R * 0.8, R * 0.42)))
    return fuse(p, f"bloober_seg{variant}", voxel=0.7, smooth=4, noise=0.7, noise_scale=6, decimate=0.077)


stats["segment0"] = export(segment(0), "bloober", "segment0")
stats["segment1"] = export(segment(1), "bloober", "segment1")

# ---- Head: eel/snake skull. Heavy jaw underneath, brow ridges where the eyes go
# (eyes sit at about +0.75R forward, +-0.45R side, +0.5R up), nostril bumps, cheek frills, crest.
clear()
p = [
    ellipsoid("skull", (0, 0, 0.05 * R), (R, R * 0.92, R * 0.82)),
    ellipsoid("snout", (0.55 * R, 0, 0.05 * R), (R * 0.55, R * 0.62, R * 0.52)),
    ellipsoid("jaw", (0.35 * R, 0, -0.38 * R), (R * 0.75, R * 0.7, R * 0.4)),
    ellipsoid("chin", (0.8 * R, 0, -0.32 * R), (R * 0.3, R * 0.4, R * 0.22)),
]
for side in (-1, 1):
    p.append(ellipsoid("brow", (0.62 * R, side * 0.45 * R, 0.68 * R), (R * 0.3, R * 0.24, R * 0.13)))
    p.append(ellipsoid("cheek", (0.1 * R, side * 0.82 * R, -0.1 * R), (R * 0.35, R * 0.18, R * 0.3)))
    p.append(cone("frill", (-0.2 * R, side * 0.8 * R, 0.05 * R), (-0.6, side * 0.8, 0.2), R * 0.14, R * 0.55))
    p.append(cone("frill2", (-0.35 * R, side * 0.75 * R, -0.25 * R), (-0.7, side * 0.7, -0.1), R * 0.12, R * 0.45))
    p.append(ellipsoid("nostril", (1.02 * R, side * 0.18 * R, 0.22 * R), (R * 0.08, R * 0.07, R * 0.06)))
for x, h in ((-0.1, 1.0), (-0.45, 0.85), (0.2, 0.7)):
    p.append(cone("crest", (x * R, 0, 0.72 * R), (-0.5, 0, 1), R * 0.12 * h, R * 0.45 * h))
stats["head"] = export(fuse(p, "bloober_head", voxel=0.6, smooth=4, noise=0.55, noise_scale=6, decimate=0.115), "bloober", "head")
result = stats
