# Tileable textures for forest props (used by shaders/triplanar_nature.shader).
# Run: python Tools/textures/gen_nature.py  (writes into Assets/materials/nature)
# Base maps (bark, foliage, rock) are near-neutral so the renderer tint sets the hue;
# moss is fully coloured because the top layer is not tinted.
import os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
import gen_ground as g

g.OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "materials", "nature")
os.makedirs(g.OUT, exist_ok=True)
N = g.N
fbm, band, norm01, smooth, blur, scatter, normal_from, mix, rgb, save = (
	g.fbm, g.band, g.norm01, g.smooth, g.blur, g.scatter, g.normal_from, g.mix, g.rgb, g.save_png)


def bark():
	# Deep vertical fissures between flat plates, plus fine fibre.
	ridges = band(101, 6, 90, 0.5, (0.0, 6.0)) + 0.3 * band(105, 2, 12, 1.0, (0.0, 3.0))  # vertical streaks
	plates = norm01(ridges)
	q = np.quantile(plates, [0.15, 0.4])
	fiss = smooth(q[1], q[0], plates)  # 1 in the cracks
	cross = smooth(1.6, 2.4, band(102, 6, 40, 0.8, (np.pi / 2, 5.0)))  # horizontal breaks
	fibre = band(103, 120, 500, 0.2, (0.0, 12.0))
	lich = smooth(1.3, 1.9, fbm(104, 3, 40))
	h = plates * 0.8 - fiss * 0.5 - cross * 0.15 + 0.05 * fibre
	lum = 0.62 + 0.18 * (plates - 0.5) + 0.04 * fibre
	lum = lum * (1 - 0.6 * fiss) * (1 - 0.2 * cross)
	col = np.stack([lum, lum * 0.97, lum * 0.93], -1)
	col = mix(col, rgb(0.62, 0.66, 0.52), lich * 0.35)  # pale lichen flecks
	rough = np.clip(0.82 + 0.1 * fiss - 0.08 * plates, 0.4, 1)
	save("bark_color", col)
	save("bark_normal", normal_from(h, 16))
	save("bark_rough", rough)


def foliage():
	# Dense overlapping leaves, darker in the gaps between them.
	lh, lm, lid = scatter(201, 2600, 7, 15, 1.9)
	lh2, lm2, lid2 = scatter(202, 1800, 6, 12, 1.9)
	top = lh2 > lh * 0.85
	h = np.where(top, 0.6 + 0.4 * lh2, lh)
	idv = np.where(top, lid2, lid)
	cover = np.maximum(lm, lm2)
	vein = smooth(0.12, 0.0, np.abs(h - 0.82)) * 0.15
	lum = (0.55 + 0.35 * idv) * (0.55 + 0.45 * h) - vein
	lum = lum * (0.4 + 0.6 * cover)  # gaps are shade
	col = np.stack([lum * 0.95, lum, lum * 0.9], -1)
	rough = np.clip(0.55 + 0.3 * (1 - cover) + 0.1 * idv, 0.3, 1)
	save("foliage_color", col)
	save("foliage_normal", normal_from(h * cover, 10))
	save("foliage_rough", rough)


def rock():
	big = fbm(301, 1, 10)
	mid = fbm(302, 10, 80)
	grain = fbm(303, 200, 512, 0.0)
	cr = 1 - np.abs(fbm(304, 2, 10)) / 3
	cr = blur(cr, 1.5) + 0.02 * fbm(307, 40, 200)
	q = np.quantile(cr, [0.96, 0.99])
	cracks = smooth(q[0], q[1], cr)
	strata = smooth(1.0, 2.2, band(305, 4, 50, 0.7, (np.pi / 2, 6.0)))
	sh, sm, sid = scatter(306, 900, 1, 3.5)
	h = 0.5 * norm01(big) + 0.3 * norm01(mid) + 0.05 * grain - 0.4 * cracks - 0.1 * strata
	lum = 0.6 + 0.08 * big + 0.05 * mid + 0.04 * grain
	lum = lum * (1 - 0.55 * cracks) * (1 - 0.12 * strata)
	col = np.stack([lum, lum * 0.98, lum * 0.95], -1)
	col = mix(col, mix(rgb(0.78, 0.76, 0.66), rgb(0.62, 0.66, 0.50), sid), sm * 0.5)  # lichen dots
	rough = np.clip(0.78 + 0.08 * mid + 0.1 * cracks, 0.4, 1)
	save("rock_color", col)
	save("rock_normal", normal_from(h, 9))
	save("rock_rough", rough)


def moss():
	tone = norm01(fbm(401, 2, 30))
	tufts = norm01(band(402, 80, 400, 0.3))
	clumps = norm01(fbm(403, 10, 60))
	col = mix(rgb(0.20, 0.30, 0.07), rgb(0.42, 0.52, 0.14), tone)
	col = mix(col, rgb(0.50, 0.50, 0.20), smooth(0.7, 0.95, clumps) * 0.4)
	col = col * (0.6 + 0.6 * tufts[..., None])
	h = 0.6 * clumps + 0.4 * tufts
	save("moss_color", col)
	save("moss_normal", normal_from(h, 8))


if __name__ == "__main__":
	bark()
	foliage()
	rock()
	moss()
