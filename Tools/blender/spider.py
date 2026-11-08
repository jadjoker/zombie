# Builds every Sea Spider part and exports it to Assets/models/creatures/spider.
# Run headless:  blender -b --factory-startup --python spider.py
# Each part is modelled in its own local space with the pivot where SeaSpider.cs attaches it:
#   body  - the egg, centred on the origin (+X forward, eye socket on the front)
#   thigh - pivot at the hip, 22 long along +X, ending in the knee ball
#   shin  - pivot at the knee, 58 long along +X, ending in a hooked toe
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())
import math

stats = {}
THIGH, SHIN = 22.0, 58.0
LEG_YAWS = (48, 92, 132, -48, -92, -132)

# ---- Body: smooth ball with a lipped eye socket, six hip sockets and a few knobs down the back.
clear()
pieces = [
    ellipsoid("egg", (0, 0, 0), (16.0, 16.0, 16.0), segs=48),
    ellipsoid("socket", (10.8, 0, 4.2), (5.5, 11.5, 11.5), segs=40),     # raised lip round the eye
]
for yaw in LEG_YAWS:
    d = (math.cos(math.radians(yaw)), math.sin(math.radians(yaw)))
    pieces.append(ellipsoid("hip", (d[0] * 11.8, d[1] * 11.8, 2.0), (5.2, 5.2, 5.2), segs=28))   # socket the thigh grows from
for x, z, r in ((-6, 14.5, 2.4), (-10.5, 11.5, 2.1), (-13.5, 6.5, 1.9), (-15.5, 0, 1.7), (-13.5, -7, 1.5)):
    pieces.append(ellipsoid("knob", (x, 0, z), (r, r * 1.1, r), segs=20))
stats["body"] = export(fuse(pieces, "spider_body", voxel=0.45, smooth=4, noise=0.0, decimate=0.45), "spider", "body")

# ---- Thigh: ball hip, a gently swollen tapered segment with a few soft rings, a round knee.
clear()
pieces = [
    ellipsoid("hip", (0, 0, 0), (3.2, 3.2, 3.2), segs=28),
    tube("thigh", (0, 0, 0), (THIGH, 0, 0), 2.5, 1.9, verts=24),
    ellipsoid("muscle", (8.5, 0, 0), (9.0, 2.9, 2.9), segs=28),
    ellipsoid("knee", (THIGH, 0, 0), (3.3, 3.1, 3.1), segs=28),
]
for x in (5.0, 11.0, 17.0):
    pieces.append(ellipsoid("ring", (x, 0, 0), (0.8, 2.95 - x * 0.03, 2.95 - x * 0.03), segs=24))
stats["thigh"] = export(fuse(pieces, "spider_thigh", voxel=0.28, smooth=3, noise=0.0, decimate=0.5), "spider", "thigh")

# ---- Shin: knee ball, long taper to a fine hooked toe, two soft joints on the way.
clear()
pieces = [
    ellipsoid("knee", (0, 0, 0), (2.8, 2.6, 2.6), segs=28),
    tube("upper", (0, 0, 0), (SHIN * 0.5, 0, -0.4), 2.2, 1.45, verts=20),
    tube("lower", (SHIN * 0.5, 0, -0.4), (SHIN - 3.0, 0, -1.0), 1.45, 0.6, verts=16),
    ellipsoid("joint", (SHIN * 0.5, 0, -0.4), (1.5, 2.0, 2.0), segs=20),
    ellipsoid("joint2", (SHIN * 0.78, 0, -0.8), (1.1, 1.3, 1.3), segs=16),
    cone("toe", (SHIN - 3.0, 0, -1.0), (1.0, 0, -0.55), 0.62, 4.2, verts=12),
]
stats["shin"] = export(fuse(pieces, "spider_shin", voxel=0.2, smooth=3, noise=0.0, decimate=0.4), "spider", "shin")

result = stats
