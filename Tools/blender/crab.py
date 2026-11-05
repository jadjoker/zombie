# Builds every Junk Crab part and exports it to Assets/models/creatures/crab.
# Run inside Blender (Scripting tab, or through the Blender MCP connector). Each part is modelled in
# its own local space with the pivot where the crab code attaches it (see JunkCrab.cs BuildLimbs).
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

stats = {}

# ---- Body: broad carapace, ridged rim, plated belly, brow ridge over the eyes, spikes on top and rim.
clear()
pieces = [
    ellipsoid("cara", (0, 0, 1.2), (15.5, 20, 7.8)),
    ellipsoid("rim", (0, 0, -0.4), (16.2, 20.6, 3.2)),
    ellipsoid("belly", (0, 0, -3.6), (13.5, 17, 5)),
    ellipsoid("brow", (12.5, 0, 3.2), (3.5, 9.5, 2.6)),
    ellipsoid("snout", (14.2, 0, -1.2), (3.2, 6.5, 3)),
]
for side in (-1, 1):
    pieces.append(ellipsoid("lobe", (2, side * 15, 0.5), (9, 6, 5.5)))
    for x, y in [(9, 16), (3, 19.5), (-4, 19.5), (-10, 16)]:
        pieces.append(cone("rimspike", (x, side * y, -0.3), (x * 0.03, side * 1, 0.15), 1.6, 4.2))
for x, y in [(0, 0), (-5, 6), (-5, -6), (4, 7), (4, -7), (-9, 0), (-1, 11), (-1, -11)]:
    pieces.append(cone("topspike", (x, y, 7.2 - abs(y) * 0.12), (x * 0.02 - 0.2, y * 0.04, 1), 1.4, 4.5))
stats["body"] = export(fuse(pieces, "crab_body", voxel=0.55, smooth=3, noise=0.45, noise_scale=2.2, decimate=0.35), "crab", "body")

# ---- Leg (pivot at the hip): thigh with a spur, knobbly knee, long shin to a hooked point.
clear()
pieces = [
    tube("thigh", (0, 0, 2), (15.5, 0, 3.3), 2.8, 2.1),
    ellipsoid("hip", (0.5, 0, 2), (3, 3, 3)),
    ellipsoid("knee", (15.5, 0, 3.2), (2.9, 2.6, 2.7)),
    tube("shin", (15.8, 0, 3), (19.2, 0, -10), 2.3, 1.1),
    cone("tip", (19.2, 0, -10), (0.15, 0, -1), 1.1, 4.2),
    cone("spur", (8, 0, 4.2), (0.3, 0, 1), 0.9, 2.6),
    cone("kneespur", (16.5, 0, 5.2), (0.5, 0, 1), 0.8, 2.2),
]
stats["leg"] = export(fuse(pieces, "crab_leg", voxel=0.4, smooth=2, noise=0.18, noise_scale=1.4, decimate=0.4), "crab", "leg")

# ---- Arm (pivot at the shoulder, along +X): knobbly segment with spines.
clear()
pieces = [tube("arm", (0, 0, 0), (14, 0, 0.5), 3.6, 3.0), ellipsoid("shoulder", (0.5, 0, 0), (3.6, 3.6, 3.6)),
          ellipsoid("elbow", (13.5, 0, 0.5), (3.3, 3.1, 3.2)), cone("armspine", (7, 0, 3.2), (0.3, 0, 1), 1.0, 3.0),
          cone("armspine2", (10, 0, 2.9), (0.4, 0, 1), 0.8, 2.4)]
stats["arm"] = export(fuse(pieces, "crab_arm", voxel=0.4, smooth=2, noise=0.18, noise_scale=1.4, decimate=0.45), "crab", "arm")

# ---- Pincer (pivot at the wrist): swollen palm and the fixed finger, toothed on the inside.
clear()
pieces = [ellipsoid("palm", (5.5, 0, 0), (7.5, 5, 6)), ellipsoid("palmhump", (4.5, 0, 2.5), (5, 4, 3.5)),
          tube("finger", (10.5, 0, -2), (19, 0, -2.6), 2.6, 1.2), cone("fingertip", (19, 0, -2.6), (1, 0, 0.35), 1.2, 3.2)]
for x in (12.5, 14.5, 16.5):
    pieces.append(cone("tooth", (x, 0, -0.9), (0, 0, 1), 0.7, 1.6))
for x, y in ((3, 4), (6, 4.5), (3, -4), (6, -4.5)):
    pieces.append(cone("palmspike", (x, y, 2), (0, y * 0.2, 1), 0.8, 2.2))
stats["pincer"] = export(fuse(pieces, "crab_pincer", voxel=0.4, smooth=2, noise=0.15, noise_scale=1.3, decimate=0.45), "crab", "pincer")

# ---- Jaw (pivot at its hinge): the moving finger, hooked, toothed underneath.
clear()
pieces = [ellipsoid("hinge", (0.5, 0, 0), (2.4, 2.2, 2.2)), tube("jawfinger", (0, 0, 0), (9.5, 0, 0.6), 2.2, 1.1),
          cone("jawtip", (9.5, 0, 0.6), (1, 0, -0.45), 1.1, 3.0)]
for x in (3.5, 5.5, 7.5):
    pieces.append(cone("jawtooth", (x, 0, -1.3), (0, 0, -1), 0.6, 1.4))
stats["jaw"] = export(fuse(pieces, "crab_jaw", voxel=0.3, smooth=2, noise=0.1, noise_scale=1.2, decimate=0.5), "crab", "jaw")

# ---- Eye stalk (pivot at the socket, up +Z): ringed stalk with a cup for the eyeball (eyeball stays a code shape).
clear()
pieces = [tube("stalk", (0, 0, 0), (0, 0, 9.5), 1.6, 1.0), ellipsoid("base", (0, 0, 0.5), (2.4, 2.4, 1.8)),
          ellipsoid("cup", (0, 0, 9.8), (2.2, 2.2, 1.6))]
for z in (2.5, 5, 7.5):
    pieces.append(ellipsoid("ring", (0, 0, z), (1.6 - z * 0.05, 1.6 - z * 0.05, 0.6)))
stats["eyestalk"] = export(fuse(pieces, "crab_eyestalk", voxel=0.22, smooth=2, noise=0.05, noise_scale=1.0, decimate=0.5), "crab", "eyestalk")

result = stats
