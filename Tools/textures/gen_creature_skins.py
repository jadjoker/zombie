# Generates tileable creature skins (color, normal, roughness) with numpy only, replacing the old rocky
# "scales" plates. Albedo stays light and near-grey: each creature's renderer tint sets its colour.
# Run: python Tools/textures/gen_creature_skins.py  (writes into Assets/materials/creature)
import os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "gen_ground.py"), encoding="utf-8").read()
exec(src[:src.index("# ---- forest floor")])          # reuse the noise and png helpers
OUT = os.path.join(HERE, "..", "..", "Assets", "materials", "creature")


def chitin():
	"""Junk Crab shell: hard, glossy chitin like a real crab's back. Raised tubercles (small rounded bumps,
	lighter on top), a soft undulation, faint growth bands, fine pores, and only a gentle mottle."""
	broad = band(201, 2, 10, 1.4)
	mid = band(202, 8, 40, 1.0)
	bands = band(203, 6, 60, 0.8, aniso=(0.35, 9.0))
	tub, tub_m, tub_id = scatter(204, 520, 3.0, 8.0)                 # tubercles
	tub_small, _, _ = scatter(208, 1400, 1.4, 3.0)
	pore_h, _, _ = scatter(205, 1800, 0.7, 1.4)
	mottle = smooth(-0.6, 1.6, band(206, 3, 14, 1.2))
	speck = smooth(1.7, 2.6, band(207, 60, 220, 0.4))

	bumps = tub * (0.6 + 0.4 * tub_id) + 0.45 * tub_small
	h = 0.5 + 0.08 * broad + 0.02 * mid + 0.015 * bands + 0.22 * bumps - 0.05 * pore_h
	shade = 0.84 - 0.08 * mottle - 0.03 * norm01(bands) + 0.12 * bumps - 0.1 * blur(pore_h, 0.6) - 0.06 * speck
	col = np.clip(shade, 0, 1)[..., None] * rgb(1.0, 0.97, 0.94)
	rough = np.clip(0.3 + 0.06 * mottle - 0.1 * bumps + 0.15 * blur(pore_h, 0.6) + 0.04 * mid, 0.14, 0.8)
	save_png("chitin_color", col)
	save_png("chitin_normal", normal_from(blur(h, 0.7), 9))
	save_png("chitin_rough", rough)


def eelskin():
	"""Bloober: smooth wet eel/salamander skin. A fine net of slightly paler veins, small soft spots,
	tiny pores and soft stretch wrinkles. Very glossy. (Its big spots are separate shapes in game.)"""
	_, spots, _ = scatter(301, 160, 6, 15, elong=1.5)
	wobble = band(302, 6, 30, 1.0)
	spots_soft = smooth(0.3, 0.7, blur(spots, 2.0) + 0.12 * wobble)
	vein_src = band(307, 6, 26, 1.0)
	veins = smooth(0.82, 0.97, 1 - np.abs(vein_src) / (np.abs(vein_src).max() * 0.35 + 1e-9))
	wrinkles = band(303, 20, 120, 0.7, aniso=(1.5708, 6.0))
	pore_h, _, _ = scatter(304, 2600, 0.8, 1.6)
	soft = band(306, 2, 8, 1.3)

	h = 0.5 + 0.05 * soft + 0.025 * wrinkles - 0.05 * pore_h + 0.03 * veins
	shade = 0.9 - 0.22 * spots_soft + 0.08 * veins - 0.04 * norm01(wrinkles) - 0.05 * blur(pore_h, 0.5)
	col = np.clip(shade, 0, 1)[..., None] * rgb(1.0, 0.98, 0.97)
	rough = np.clip(0.15 + 0.06 * norm01(wrinkles) + 0.1 * blur(pore_h, 0.5) + 0.05 * spots_soft, 0.1, 0.6)
	save_png("eelskin_color", col)
	save_png("eelskin_normal", normal_from(blur(h, 0.5), 5))
	save_png("eelskin_rough", rough)


def jelly():
	"""Squilien: jellyfish bell. Smooth and very glossy, soft radial-ish canals, faint milky clouds and tiny
	bright specks inside, almost no surface bumps."""
	canals_src = band(401, 4, 22, 1.0, aniso=(0.0, 4.0))
	canals = smooth(0.85, 0.98, 1 - np.abs(canals_src) / (np.abs(canals_src).max() * 0.3 + 1e-9))
	clouds = smooth(-0.5, 1.5, band(402, 2, 10, 1.3))
	speck_h, _, _ = scatter(403, 700, 0.8, 1.8)
	ripple = band(404, 10, 50, 0.9)

	h = 0.5 + 0.02 * ripple + 0.03 * canals + 0.02 * clouds
	shade = 0.82 + 0.1 * clouds + 0.12 * canals + 0.15 * speck_h
	col = np.clip(shade, 0, 1)[..., None] * rgb(0.98, 1.0, 1.0)
	rough = np.clip(0.08 + 0.05 * clouds + 0.03 * norm01(ripple), 0.05, 0.3)
	save_png("jelly_color", col)
	save_png("jelly_normal", normal_from(blur(h, 0.8), 4))
	save_png("jelly_rough", rough)


if __name__ == "__main__":
	which = sys.argv[1:] or ["chitin", "eelskin", "jelly"]
	for w in which:
		globals()[w]()
