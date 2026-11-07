# Generates tileable ground textures (color, normal, roughness) with numpy only.
# Run: python Tools/textures/gen_ground.py  (writes into Assets/materials/ground)
# Everything is built from wrap-around noise, so every map tiles seamlessly.
import os, zlib, struct
import numpy as np

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "materials", "ground")
os.makedirs(OUT, exist_ok=True)
N = 1024


# ---- io ----
def save_png(name, img):
	img = np.clip(img, 0, 1)
	if img.ndim == 2:
		img = np.repeat(img[..., None], 3, -1)
	a = (img * 255 + 0.5).astype(np.uint8)
	h, w, c = a.shape
	raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))
	def chunk(t, d):
		return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
	png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
	png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
	with open(os.path.join(OUT, name + ".png"), "wb") as f:
		f.write(png)
	print("wrote", name)


# ---- noise ----
F = np.fft.fftfreq(N) * N  # integer frequencies, so everything wraps


def band(seed, lo, hi, power=1.0, aniso=None):
	"""Noise with energy between frequencies lo..hi (cycles per tile). aniso=(angle, stretch) makes streaks."""
	g = np.random.default_rng(seed)
	fx, fy = F[None, :], F[:, None]
	if aniso:
		ang, st = aniso
		c, s = np.cos(ang), np.sin(ang)
		fx, fy = (fx * c + fy * s), (-fx * s + fy * c) * st
	k = np.sqrt(fx ** 2 + fy ** 2)
	k[0, 0] = 1
	w = np.exp(-((k / hi) ** 2)) * (1 - np.exp(-((k / max(lo, 0.5)) ** 2))) / k ** power
	w[0, 0] = 0
	spec = (g.normal(size=(N, N)) + 1j * g.normal(size=(N, N))) * w
	n = np.real(np.fft.ifft2(spec))
	n -= n.mean()
	return n / (n.std() + 1e-9)


def fbm(seed, lo=1, hi=200, power=1.0):
	return band(seed, lo, hi, power)


def norm01(x):
	return (x - x.min()) / (x.max() - x.min() + 1e-9)


def smooth(e0, e1, x):
	t = np.clip((x - e0) / (e1 - e0), 0, 1)
	return t * t * (3 - 2 * t)


def blur(x, sigma):
	k = np.sqrt(F[None, :] ** 2 + F[:, None] ** 2)
	return np.real(np.fft.ifft2(np.fft.fft2(x) * np.exp(-0.5 * (k * sigma * 2 * np.pi / N) ** 2)))


def scatter(seed, count, rmin, rmax, elong=1.0):
	"""Tileable field of random ellipses: returns (height dome, mask, per-blob random value)."""
	g = np.random.default_rng(seed)
	yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
	hgt = np.zeros((N, N), np.float32)
	ids = np.zeros((N, N), np.float32)
	for _ in range(count):
		cx, cy = g.uniform(0, N, 2)
		r = g.uniform(rmin, rmax)
		a = g.uniform(0, np.pi)
		e = g.uniform(1, elong)
		x0, x1 = int(cx - r * e - 2), int(cx + r * e + 3)
		y0, y1 = int(cy - r * e - 2), int(cy + r * e + 3)
		ys = np.arange(y0, y1) % N
		xs = np.arange(x0, x1) % N
		dx = (np.arange(x0, x1) - cx)[None, :]
		dy = (np.arange(y0, y1) - cy)[:, None]
		u = (dx * np.cos(a) + dy * np.sin(a)) / (r * e)
		v = (-dx * np.sin(a) + dy * np.cos(a)) / r
		d = 1 - (u * u + v * v)
		dome = np.sqrt(np.clip(d, 0, 1))
		sub = hgt[np.ix_(ys, xs)]
		take = dome > sub
		hgt[np.ix_(ys, xs)] = np.where(take, dome, sub)
		idsub = ids[np.ix_(ys, xs)]
		ids[np.ix_(ys, xs)] = np.where(take, g.uniform(), idsub)
	return hgt, (hgt > 0).astype(np.float32), ids


def normal_from(h, strength):
	dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
	dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
	n = np.stack([-dx, dy, np.ones_like(h)], -1)
	n /= np.linalg.norm(n, axis=-1, keepdims=True)
	return n * 0.5 + 0.5


def mix(a, b, t):
	t = t[..., None] if np.ndim(t) == 2 else t
	return a * (1 - t) + b * t


def rgb(*c):
	return np.array(c, np.float32)[None, None, :]


# ---- forest floor: grass and clover over dirt, pebbles, fallen leaves ----
def forest():
	macro = fbm(1, 1, 6, 1.0)
	dirt = smooth(0.6, 1.4, macro + 0.4 * fbm(2, 6, 40))  # 0 grass .. 1 bare dirt
	# Grass blades: thin streaks at a few angles.
	blades = sum(band(10 + i, 60, 420, 0.3, (a, 7.0)) for i, a in enumerate([0.3, 1.4, 2.4, 3.0]))
	blades = norm01(blades)
	clumps = norm01(fbm(3, 8, 60))
	gtone = norm01(fbm(4, 3, 30))
	grass_col = mix(rgb(0.15, 0.20, 0.07), rgb(0.34, 0.37, 0.13), gtone)
	grass_col = mix(grass_col, rgb(0.42, 0.40, 0.18), smooth(0.65, 0.95, clumps) * 0.6)  # dry patches
	grass_col = grass_col * (0.35 + 1.15 * blades[..., None] ** 1.4)
	soil_tone = norm01(fbm(5, 4, 80))
	grain = fbm(6, 150, 500, 0.2)
	dirt_col = mix(rgb(0.20, 0.14, 0.09), rgb(0.33, 0.25, 0.17), soil_tone) * (1 + 0.12 * grain[..., None])
	# Grass thins out toward dirt, with tufts surviving.
	cover = np.clip((1 - dirt) * (0.75 + 0.6 * (blades - 0.5)) + 0.25 * (clumps - 0.5), 0, 1)
	cover = smooth(0.25, 0.6, cover)
	col = mix(dirt_col, grass_col, cover)
	h = 0.35 * soil_tone + 0.1 * grain + cover * (0.4 + 0.5 * blades)

	# Pebbles, mostly in the dirt.
	ph, pm, pid = scatter(7, 700, 1.8, 5.0, 1.6)
	pm = pm * smooth(0.25, 0.7, dirt + 0.2 * fbm(8, 10, 50))
	pcol = mix(rgb(0.22, 0.21, 0.19), rgb(0.40, 0.38, 0.34), pid) * (0.7 + 0.4 * ph[..., None])
	col = mix(col, pcol, pm)
	h = np.where(pm > 0, np.maximum(h, 0.5 + 0.5 * ph), h)

	# Fallen leaves: elongated, autumn-ish, scattered everywhere, darker where in grass.
	lh, lm, lid = scatter(9, 380, 3, 6.5, 2.2)
	leaf_col = mix(rgb(0.28, 0.17, 0.08), rgb(0.42, 0.32, 0.13), lid)
	leaf_col = mix(leaf_col, rgb(0.22, 0.13, 0.06), smooth(0.7, 1, lid))
	lm = lm * (lid > 0.12) * smooth(0.1, 0.6, dirt + 0.15)
	col = mix(col, leaf_col * (0.8 + 0.3 * lh[..., None]), lm * 0.85)
	h = np.where(lm > 0, np.maximum(h, 0.45 + 0.15 * lh), h)

	# Soft cavity darkening keeps it from looking flat.
	cav = h - blur(h, 6)
	col = col * (0.85 + 0.6 * np.clip(cav, -0.25, 0.25)[..., None])
	col = col * 1.6  # colours above were picked dark; lift into a sane sRGB albedo range
	rough = np.clip(0.86 - 0.12 * cover + 0.1 * dirt - 0.2 * pm * ph + 0.04 * grain, 0.5, 1)
	save_png("forest_ground_color", col)
	save_png("forest_ground_normal", normal_from(h, 9))
	save_png("forest_ground_rough", rough)


# ---- indoor floor: worn concrete tiles with grout, stains and scuffs ----
def indoor():
	T = 4  # tiles per texture
	yy, xx = np.mgrid[0:N, 0:N] / (N / T)
	fx, fy = xx % 1, yy % 1
	tx, ty = np.floor(xx).astype(int), np.floor(yy).astype(int)
	g = np.random.default_rng(21)
	tile_val = g.uniform(size=(T, T))[ty, tx]
	tile_tilt = g.uniform(-1, 1, size=(T, T, 2))
	edge = np.minimum(np.minimum(fx, 1 - fx), np.minimum(fy, 1 - fy)) * (N / T)  # px to tile edge
	gw = 1.6
	grout = 1 - smooth(gw, gw + 3.5, edge)  # 1 in the grout line
	bevel = smooth(gw, gw + 10, edge)

	big = fbm(22, 1, 8, 1.0)
	mid = fbm(23, 8, 60, 1.0)
	aggr = fbm(24, 200, 512, 0.0)
	speck_h, speck_m, speck_id = scatter(25, 1800, 0.6, 1.6)
	tone = 0.47 + 0.05 * (tile_val - 0.5) + 0.035 * big + 0.02 * mid + 0.015 * aggr
	col = np.stack([tone * 0.99, tone * 0.98, tone * 1.0], -1)
	col = mix(col, mix(rgb(0.30, 0.30, 0.30), rgb(0.62, 0.61, 0.58), speck_id), speck_m * 0.35)
	# Stains: oily dark blotches and water marks.
	stain = smooth(0.6, 2.6, blur(fbm(26, 2, 12), 4) + 0.25 * fbm(27, 12, 50))
	col = mix(col, rgb(0.26, 0.25, 0.23), stain * 0.4)
	rings = np.zeros((N, N))
	col = col * (1 - rings[..., None])
	# Scuffs: fine streaks.
	scuff = smooth(1.8, 3.2, band(29, 30, 400, 0.4, (0.6, 12.0)))
	scuff = scuff * smooth(0.0, 1.0, fbm(30, 2, 10))
	col = mix(col, rgb(0.62, 0.62, 0.60), scuff * 0.35)
	# Edge wear lightens tile edges, grout is dark and dirty.
	col = col * (1 + 0.08 * (1 - smooth(gw + 1, gw + 14, edge)))[..., None]
	col = mix(col, rgb(0.22, 0.21, 0.20) * (0.8 + 0.4 * norm01(mid)[..., None]), grout)

	slope = (tile_tilt[ty, tx, 0] * (fx - 0.5) + tile_tilt[ty, tx, 1] * (fy - 0.5)) * 0.08
	h = 0.6 * bevel + slope + 0.02 * mid + 0.012 * aggr + 0.03 * speck_h - 0.015 * scuff
	rough = 0.62 + 0.08 * (tile_val - 0.5) + 0.06 * mid - 0.22 * stain + 0.15 * scuff + 0.3 * grout
	rough = np.clip(rough, 0.3, 1)
	save_png("indoor_floor_color", col)
	save_png("indoor_floor_normal", normal_from(h, 14))
	save_png("indoor_floor_rough", rough)


# ---- seabed: rippled sand with shells, grit and dark silt ----
def seabed():
	yy, xx = np.mgrid[0:N, 0:N] / N
	warp = 0.012 * fbm(41, 1, 4) + 0.003 * fbm(42, 4, 16)
	# Ripple wavefront: integer cycles across the tile so it wraps.
	phase = 2 * np.pi * (14 * xx + 3 * yy + warp * 14)
	rip = np.sin(phase + 0.5 * np.sin(phase))  # sharper crests
	amp = 0.6 + 0.4 * norm01(fbm(43, 1, 6))
	grain = fbm(44, 250, 512, 0.0)
	h = 0.5 + 0.35 * rip * amp + 0.15 * fbm(45, 2, 20) + 0.05 * grain
	tone = norm01(fbm(46, 1, 12))
	col = mix(rgb(0.66, 0.58, 0.44), rgb(0.82, 0.75, 0.60), tone)
	col = col * (0.88 + 0.16 * (rip * amp)[..., None] + 0.06 * grain[..., None])
	# Silt collects in the troughs.
	silt = smooth(0.0, -0.7, rip) * smooth(0.3, 0.8, norm01(fbm(47, 2, 15)))
	col = mix(col, rgb(0.42, 0.40, 0.33), silt * 0.45)
	# Shells and grit.
	sh, sm, sid = scatter(48, 120, 2.5, 6.0, 1.5)
	shell = mix(rgb(0.86, 0.82, 0.74), rgb(0.70, 0.60, 0.50), sid)
	col = mix(col, shell * (0.8 + 0.3 * sh[..., None]), sm * 0.9)
	gh, gm, gid = scatter(49, 1400, 0.8, 2.0)
	col = mix(col, mix(rgb(0.25, 0.24, 0.22), rgb(0.45, 0.40, 0.33), gid), gm * 0.6)
	h = np.where(sm > 0, np.maximum(h, 0.7 + 0.3 * sh), h) + 0.1 * gh
	rough = np.clip(0.88 + 0.05 * grain - 0.25 * sm * sh, 0.5, 1)
	save_png("seabed_color", col)
	save_png("seabed_normal", normal_from(h, 6))
	save_png("seabed_rough", rough)


# ---- indoor walls and ceiling: cast concrete panels. The wall map covers the full wall height
# (top of the image = top of the wall): painted upper part, dark lower band, grime at the floor.
def panels(name, painted, seed):
	yy, xx = np.mgrid[0:N, 0:N] / N
	PX = 2  # panels across
	fx = (xx * PX) % 1
	edge_x = np.minimum(fx, 1 - fx) * N / PX
	if painted:
		edge_y = np.abs(yy - 0.42) * N  # one horizontal joint
	else:
		fy = (yy * 2) % 1
		edge_y = np.minimum(fy, 1 - fy) * N / 2
	seam = 1 - smooth(1.5, 4.0, np.minimum(edge_x, edge_y))
	# Form-tie holes: 3 across, 3 down per panel.
	hx = (xx * PX * 3) % 1 - 0.5
	hy = (yy * (3 if painted else 6)) % 1 - 0.5
	hole_r = np.sqrt((hx * N / (PX * 3)) ** 2 + (hy * N / (3 if painted else 6)) ** 2)
	hole = 1 - smooth(5, 7, hole_r)
	ring = smooth(5, 7, hole_r) * (1 - smooth(8, 12, hole_r))
	big = fbm(seed, 1, 8)
	mid = fbm(seed + 1, 8, 70)
	grain = fbm(seed + 2, 200, 512, 0.0)
	streak = band(seed + 3, 4, 200, 0.6, (0.0, 14.0))  # vertical runs
	h = 0.5 - 0.5 * seam - 0.4 * hole + 0.05 * ring + 0.02 * mid + 0.01 * grain
	if painted:
		base = mix(rgb(0.60, 0.59, 0.56), rgb(0.66, 0.65, 0.62), norm01(big))
		col = base * (1 + 0.04 * mid[..., None] + 0.02 * grain[..., None])
		col = col * (1 - 0.06 * smooth(0.5, 2.0, streak) * (1 - yy))[..., None]
		# Lower band (bottom 22%) in dark blue-grey, with a chipped top edge and a thin stripe.
		chip = 0.025 * fbm(seed + 4, 20, 150)
		band_m = smooth(0.776, 0.782, yy + chip * smooth(1.2, 2.0, fbm(seed + 5, 10, 80)))
		col = mix(col, rgb(0.30, 0.36, 0.40) * (1 + 0.05 * mid[..., None]), band_m)
		stripe = smooth(0.765, 0.768, yy) * (1 - smooth(0.772, 0.775, yy))
		col = mix(col, rgb(0.62, 0.48, 0.14), stripe)
		grime = smooth(0.85, 1.0, yy) * (0.6 + 0.4 * norm01(mid))
		col = col * (1 - 0.3 * grime)[..., None]
		rough = 0.7 - 0.2 * band_m + 0.1 * grime + 0.03 * grain
	else:
		col = mix(rgb(0.36, 0.36, 0.37), rgb(0.46, 0.46, 0.46), norm01(big)) * (1 + 0.05 * mid[..., None] + 0.03 * grain[..., None])
		rough = 0.9 + 0.03 * grain
	col = col * (1 - 0.6 * seam - 0.6 * hole + 0.05 * ring)[..., None]
	save_png(name + "_color", col)
	save_png(name + "_normal", normal_from(h, 10))
	save_png(name + "_rough", np.clip(rough + 0.3 * seam, 0.3, 1))


# ---- painted steel for door frames: worn paint over bare metal, scuffs. Isotropic, so it
# survives being stretched across thin box faces.
def steel():
	big = fbm(80, 1, 10)
	mid = fbm(81, 10, 80)
	grain = fbm(82, 200, 512, 0.0)
	chips = smooth(2.1, 2.5, fbm(83, 15, 140) + 0.5 * fbm(84, 2, 12))
	scuff = smooth(1.8, 3.0, band(85, 30, 400, 0.4, (0.3, 10.0)))
	paint = mix(rgb(0.16, 0.19, 0.22), rgb(0.22, 0.25, 0.28), norm01(big)) * (1 + 0.06 * mid[..., None] + 0.03 * grain[..., None])
	metal = rgb(0.45, 0.45, 0.46) * (1 + 0.1 * grain[..., None])
	col = mix(paint, metal, chips)
	col = mix(col, rgb(0.35, 0.36, 0.38), scuff * 0.4)
	h = 0.5 - 0.3 * chips + 0.02 * mid + 0.01 * grain
	rough = np.clip(0.55 + 0.08 * mid - 0.2 * chips + 0.1 * scuff, 0.25, 1)
	save_png("steel_color", col)
	save_png("steel_normal", normal_from(h, 10))
	save_png("steel_rough", rough)


if __name__ == "__main__":
	forest()
	indoor()
	seabed()
	panels("indoor_wall", True, 60)
	panels("indoor_ceiling", False, 70)
	steel()
