"""
Rocky Shark (Risk of Copyright, Godot): a stone-plated sea serpent for issue #29.
Builds and exports to godot/assets/models/creatures/rockyshark/:
  head.glb      wedge skull with rocky plates, two glowing eyes, green radiation holes, upper teeth, a dark
                mouth, and a separate "Jaw" (lower jaw with its teeth) pivoted at the hinge so the game can bite
  segment0/1.glb  an armoured body section with dorsal plates and a glowing hole on each flank
  tail.glb      the last section with a tall caudal fin
  fin_l/r.glb   the pair of pectoral fins (the game flaps them as it swims)

Run in Blender (its own scene, "Rocky Shark"):
    exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/rocky_shark.py").read())

Modelled in s&box units around the unit "sphere" each section replaces (radius R = 32, +X forward, +Y left,
+Z up), like bloober_serpent.py, then exported at 1/64 scale turned to Godot axes, so each part is a
radius-0.5 shape facing -Z. The game scales it per section (radius inches) and paints the meshes by name:
"Rock" is tinted in game, "Glow" and "Eye" are the emissive weak points, "Teeth" and "Mouth" are kept.
"""
import bpy
import math
import os
from mathutils import Vector

exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/creatures/rockyshark"
R = 32.0
GREEN = (0.22, 1.0, 0.08)   # neon green, the glow on every hole, pore and eye


def mat(name, color, rough=0.5, metal=0.0, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit > 0:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m


def skin_fuse(parts, name, voxel=0.5, smooth=2, noise=0.2, target=2200):
    """Fused, smoothed and decimated to about `target` vertices (several can swim at once)."""
    o = fuse(parts, name, voxel=voxel, smooth=smooth, noise=noise, noise_scale=4, decimate=None)
    dec = o.modifiers.new("dec", "DECIMATE")
    dec.ratio = min(1.0, target / max(1, len(o.data.vertices)))
    bpy.ops.object.modifier_apply(modifier="dec")
    bpy.ops.object.shade_smooth()
    o.data.materials.clear()
    return o


def profile_fin(name, pts, thickness, plane="xz"):
    """A crisp thin fin from a 2D outline (s&box units), given a little thickness."""
    import bmesh
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    verts = [bm.verts.new((x, 0, z) if plane == "xz" else (x, z, 0)) for x, z in pts]
    face = bm.faces.new(verts)
    bmesh.ops.triangulate(bm, faces=[face])
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    sol = o.modifiers.new("solid", "SOLIDIFY")
    sol.thickness = thickness
    sol.offset = 0.0
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.object.modifier_apply(modifier="solid")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    return o


def plate(name, at, size, angle=0.0):
    """A flattened rock plate, slightly turned, to break up the silhouette."""
    o = ellipsoid(name, at, size, segs=20)
    o.rotation_euler = (0.0, angle, 0.0)
    return o


def teeth_row(name, x0, x1, count, z, y_half, up, length, taper=0.9, radius=2.6):
    """A row of big white fangs along +X, either side, the front ones longest."""
    out = []
    for k in range(count):
        t = k / max(1, count - 1)
        x = x0 + (x1 - x0) * t
        for side in (-1, 1):
            y = side * y_half * (1 - t * taper)
            ln = length * (0.8 + 0.5 * t)
            out.append(cone(f"{name}{k}{'L' if side > 0 else 'R'}", (x, y, z), (0.08, side * 0.12, 1 if up else -1), radius, ln, 8))
    return out


def lip_teeth(name, bvh, x0, x1, count, up, length, radius=2.6, z_probe=-0.20 * R, inset=0.82):
    """A row of fangs set right on the lip: for each tooth, find how wide the head is there and where its underside
    (or, for the jaw, its top) is, and hang the tooth from that edge so it shows instead of sinking into the rock."""
    out = []
    for k in range(count):
        t = k / max(1, count - 1)
        x = x0 + (x1 - x0) * t
        side_hit, _ = snap(bvh, x, 1, z_probe)
        if side_hit is None:
            continue
        for side in (-1, 1):
            y = side * abs(side_hit.y) * inset
            origin = Vector((x, y, (3 * R) if up else (-3 * R)))
            hit, _, _, _ = bvh.ray_cast(origin, Vector((0, 0, -1 if up else 1)))
            if hit is None:
                continue
            base = hit + Vector((0, 0, -0.05 * R if up else 0.05 * R))
            ln = length * (0.75 + 0.5 * (1 - abs(t - 0.6)))
            out.append(cone(f"{name}{k}{'L' if side > 0 else 'R'}", tuple(base), (0.05, side * 0.18, 1 if up else -1), radius, ln, 8))
    return out


def surface_bvh(body):
    import bmesh
    from mathutils.bvhtree import BVHTree
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.transform(body.matrix_world)
    bvh = BVHTree.FromBMesh(bm)
    pts = [v.co.copy() for v in bm.verts]
    bm.free()
    return bvh, pts


def snap(bvh, x, side, z):
    """The point on the skin straight out from the body's axis at (x, z), on one side, and its normal."""
    hit, nrm, _, _ = bvh.ray_cast(Vector((x, side * 4 * R, z)), Vector((0, -side, 0)))
    return hit, nrm


def crater(name, hit, nrm, r, glow_mat, rims, depth=0.35):
    """A little meteor-strike crater: a raised, rough rock rim (collected in `rims`, joined into one mesh) and a
    glowing neon bowl sunk inside it. Returns the glowing bowl."""
    rot = nrm.to_track_quat("Z", "Y").to_euler()
    bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.34 * r, major_segments=12, minor_segments=6,
                                     location=tuple(hit + nrm * 0.02 * r), rotation=tuple(rot))
    rim = bpy.context.active_object
    rim.name = name + "Rim"
    rim.scale = (1.0, 1.0, 0.6)
    rims.append(rim)
    bowl = ellipsoid(name, tuple(hit - nrm * depth * r * 0.5), (0.86 * r, 0.86 * r, depth * r), segs=12)
    bowl.rotation_euler = rot
    bowl.data.materials.append(glow_mat)
    return bowl


def adopt(child, parent):
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()


def finish_craters(body, rims, pores, tag, rock, glow):
    """Joins every rim into one rock mesh and every small pore into one glowing mesh, both on the body."""
    if rims:
        r = assemble(rims, f"Rims{tag}", smooth=True)
        r.data.materials.clear()
        r.data.materials.append(rock)
        adopt(r, body)
    if pores:
        p = assemble(pores, f"Pores{tag}", smooth=True)
        p.data.materials.clear()
        p.data.materials.append(glow)
        adopt(p, body)


def scatter(body, bvh, seed, tag, glow, rims, rows, cols, xlim, avoid=(), keep=lambda p: True, rmin=0.05, rmax=0.10, gap=0.20):
    """Many small craters spread evenly over the whole body: a grid of rays along the body and round it, each jittered,
    so the pores cover every side instead of bunching up."""
    import random
    rnd = random.Random(seed)
    lo, hi = xlim
    out, placed = [], []
    for i in range(rows):
        for j in range(cols):
            x = lo + (hi - lo) * (i + rnd.uniform(0.15, 0.85)) / rows
            a = math.tau * (j + rnd.uniform(0.1, 0.9)) / cols + i * 0.37
            d = Vector((0, math.cos(a), math.sin(a)))
            hit, nrm, _, _ = bvh.ray_cast(Vector((x, 0, 0)) + d * 4 * R, -d)
            if hit is None or not keep(hit):
                continue
            if any((hit - Vector(p)).length < 0.26 * R for p in avoid) or any((hit - q).length < gap * R for q in placed):
                continue
            out.append(crater(f"Pore{tag}{len(out)}", hit, nrm, rnd.uniform(rmin, rmax) * R, glow, rims))
            placed.append(hit)
    return out


def head():
    clear()
    rock = mat("Rock", (0.30, 0.29, 0.28), 0.85)
    teeth = mat("Teeth", (1.0, 0.98, 0.92), 0.25)
    mouth = mat("Mouth", (0.10, 0.01, 0.02), 0.7)
    glow = mat("Glow", GREEN, 0.35, emit=2.5)
    eye = mat("Eye", GREEN, 0.2, emit=3.0)

    # A long low skull, a blunt snout, heavy brows (lifted clear of the big eyes), cheeks and cracked slabs on top.
    p = [
        ellipsoid("skull", (0.55 * R, 0, 0.06 * R), (1.18 * R, 0.70 * R, 0.58 * R)),
        ellipsoid("snout", (1.42 * R, 0, -0.02 * R), (0.68 * R, 0.46 * R, 0.36 * R)),
        ellipsoid("snoutTip", (1.92 * R, 0, -0.04 * R), (0.34 * R, 0.26 * R, 0.20 * R)),
        ellipsoid("browL", (0.84 * R, 0.46 * R, 0.56 * R), (0.46 * R, 0.18 * R, 0.13 * R)),
        ellipsoid("browR", (0.84 * R, -0.46 * R, 0.56 * R), (0.46 * R, 0.18 * R, 0.13 * R)),
        ellipsoid("cheekL", (0.50 * R, 0.62 * R, -0.18 * R), (0.52 * R, 0.16 * R, 0.30 * R)),
        ellipsoid("cheekR", (0.50 * R, -0.62 * R, -0.18 * R), (0.52 * R, 0.16 * R, 0.30 * R)),
        plate("slabA", (0.15 * R, 0, 0.50 * R), (0.36 * R, 0.32 * R, 0.10 * R), 0.10),
        plate("slabB", (0.72 * R, 0, 0.58 * R), (0.30 * R, 0.24 * R, 0.10 * R), -0.12),
        plate("slabC", (1.22 * R, 0, 0.36 * R), (0.24 * R, 0.22 * R, 0.09 * R), 0.08),
        plate("slabL", (0.10 * R, 0.55 * R, 0.24 * R), (0.30 * R, 0.10 * R, 0.20 * R), 0.25),
        plate("slabR", (0.10 * R, -0.55 * R, 0.24 * R), (0.30 * R, 0.10 * R, 0.20 * R), -0.25),
    ]
    body = skin_fuse(p, "Head", voxel=0.48, noise=0.24, target=2600)
    body.data.materials.append(rock)
    bvh, _ = surface_bvh(body)

    # Big white fangs: a long upper row that hangs past the lip, and a lower row set inside it.
    upper = assemble(lip_teeth("ToothU", bvh, 0.70 * R, 2.08 * R, 9, False, 0.26 * R), "TeethUpper", smooth=True)
    upper.data.materials.clear()
    upper.data.materials.append(teeth)
    adopt(upper, body)

    cav = ellipsoid("Mouth", (1.35 * R, 0, -0.26 * R), (0.72 * R, 0.36 * R, 0.10 * R), segs=24)
    bpy.ops.object.transform_apply(scale=True)
    cav.data.materials.append(mouth)
    adopt(cav, body)

    # The lower jaw: modelled around its hinge at the origin, then moved under the skull. The game rotates
    # this node on X to open and close the bite.
    jp = [
        ellipsoid("jawbar", (0.75 * R, 0, -0.02 * R), (0.82 * R, 0.36 * R, 0.12 * R)),
        ellipsoid("jawchin", (1.40 * R, 0, -0.04 * R), (0.34 * R, 0.25 * R, 0.10 * R)),
    ]
    jaw = skin_fuse(jp, "Jaw", voxel=0.4, smooth=1, noise=0.10, target=800)
    jaw.data.materials.append(rock)
    jbvh, _ = surface_bvh(jaw)
    lower = assemble(lip_teeth("ToothJ", jbvh, 0.30 * R, 1.62 * R, 8, True, 0.22 * R, radius=2.2, z_probe=0.0, inset=0.9), "TeethLower", smooth=True)
    lower.data.materials.clear()
    lower.data.materials.append(teeth)
    adopt(lower, jaw)
    jaw.location = (0.45 * R, 0, -0.22 * R)
    adopt(jaw, body)

    # A glowing, spiky tongue lolling out of the right corner of the mouth (right is -Y).
    path = [Vector((1.05 * R, -0.12 * R, -0.27 * R)), Vector((1.25 * R, -0.50 * R, -0.30 * R)),
            Vector((1.38 * R, -0.82 * R, -0.36 * R)), Vector((1.42 * R, -1.02 * R, -0.48 * R))]
    radii = [0.12 * R, 0.11 * R, 0.09 * R, 0.055 * R]
    bits = []
    for k in range(len(path) - 1):
        bits.append(tube(f"tongue{k}", path[k], path[k + 1], radii[k], radii[k + 1], 14))
        bits.append(ellipsoid(f"tongueJ{k}", tuple(path[k + 1]), (radii[k + 1],) * 3, segs=12))
    bits.append(ellipsoid("tongueTip", tuple(path[-1]), (0.07 * R, 0.07 * R, 0.07 * R), segs=12))
    for k in range(len(path) - 1):
        for s in range(3):
            t = (s + 0.5) / 3
            at = path[k].lerp(path[k + 1], t)
            rr = radii[k] + (radii[k + 1] - radii[k]) * t
            along = (path[k + 1] - path[k]).normalized()
            for out in (Vector((0, 0, 1)), Vector((0, -0.7, 0.7)).normalized(), Vector((0, 0.7, 0.7)).normalized()):
                n = (out - along * out.dot(along)).normalized()
                bits.append(cone(f"spike{k}{s}", at + n * rr * 0.7, n + along * 0.35, 0.045 * R, 0.16 * R, 6))
    tongue = assemble(bits, "Tongue", smooth=True)
    tongue.data.materials.clear()
    tongue.data.materials.append(glow)
    adopt(tongue, body)

    # Classic alien eyes like the Kraken's and the Squilien's: a big slanted almond that tilts down toward the snout,
    # glowing neon green with a thin dark slit, standing proud under the lifted brow.
    pupil = mat("Pupil", (0.01, 0.01, 0.015), 0.1)
    eyes = []
    for side, name in ((1, "EyeL"), (-1, "EyeR")):
        at = (0.86 * R, side * 0.62 * R, 0.30 * R)
        eyes.append(at)
        e = ellipsoid(name, at, (0.36 * R, 0.12 * R, 0.19 * R), segs=24)
        e.rotation_euler = (0.0, math.radians(30), side * math.radians(-8))
        e.data.materials.append(eye)
        adopt(e, body)
        slit = ellipsoid("Pupil" + name[-1], (at[0], side * 0.72 * R, at[2]), (0.06 * R, 0.035 * R, 0.15 * R), segs=14)
        slit.rotation_euler = e.rotation_euler.copy()
        slit.data.materials.append(pupil)
        adopt(slit, e)

    # Radiation holes (the weak points): three bigger craters per flank, sat right on the skin.
    rims, weak = [], []
    for side, tag in ((1, "L"), (-1, "R")):
        for i, x in enumerate((0.40, 0.02, -0.34)):
            hit, nrm = snap(bvh, x * R, side, 0.10 * R)
            if hit is None:
                continue
            adopt(crater(f"Hole{tag}{i}", hit, nrm, (0.15 if i == 0 else 0.13) * R, glow, rims, depth=0.45), body)
            weak.append(hit)
    # The pores: small craters all over, except the mouth and jaw line and right on the eyes.
    pores = scatter(body, bvh, 7, "H", glow, rims, 11, 12, (-0.55 * R, 1.95 * R), avoid=eyes + weak,
                    keep=lambda h: not (h.z < -0.16 * R and h.x > 0.35 * R) and not (h.z < -0.05 * R and h.x > 1.6 * R))
    finish_craters(body, rims, pores, "H", rock, glow)
    return body


def segment(variant, fin=False):
    clear()
    rock = mat("Rock", (0.30, 0.29, 0.28), 0.85)
    glow = mat("Glow", GREEN, 0.35, emit=2.5)
    p = [
        ellipsoid("core", (0, 0, 0), (1.58 * R, 0.86 * R, 0.80 * R)),
        ellipsoid("belly", (0, 0, -0.32 * R), (1.34 * R, 0.64 * R, 0.42 * R)),
        plate("ridgeA", (-0.85 * R, 0, 0.56 * R), (0.26 * R, 0.24 * R, 0.09 * R), 0.10),
        plate("ridgeB", (0.05 * R, 0, 0.60 * R), (0.28 * R, 0.22 * R, 0.10 * R), -0.08),
        plate("ridgeC", (0.90 * R, 0, 0.54 * R), (0.24 * R, 0.20 * R, 0.08 * R), 0.12),
    ]
    if variant == 1:
        p.append(plate("ridgeD", (-1.30 * R, 0, 0.34 * R), (0.22 * R, 0.20 * R, 0.08 * R), 0.0))
    body = skin_fuse(p, f"Segment{variant}" if not fin else "SegmentFin", voxel=0.5, noise=0.22, target=2200)
    body.data.materials.append(rock)
    bvh, _ = surface_bvh(body)
    if fin:
        # The sharp dorsal fin on the back of the first section: tall, swept back, a hooked trailing edge.
        dfin = mat("Fin", (0.16, 0.42, 0.24), 0.55)
        outline = [(0.80 * R, 0.52 * R), (0.45 * R, 1.05 * R), (-0.05 * R, 1.75 * R), (-0.42 * R, 2.35 * R),
                   (-0.52 * R, 1.55 * R), (-0.72 * R, 1.05 * R), (-1.10 * R, 0.52 * R)]
        f = profile_fin("FinDorsal", outline, 0.07 * R)
        f.data.materials.append(dfin)
        adopt(f, body)
    rims, weak = [], []
    for side, tag in ((1, "L"), (-1, "R")):
        hit, nrm = snap(bvh, 0.0, side, 0.08 * R)
        if hit is not None:
            adopt(crater(f"Hole{tag}{variant}", hit, nrm, 0.16 * R, glow, rims, depth=0.45), body)
            weak.append(hit)
    # Keep the pores off the dorsal fin's root and the pectoral fin roots.
    keep = (lambda h: not (fin and h.z > 0.45 * R and -1.15 * R < h.x < 0.85 * R and abs(h.y) < 0.2 * R))
    pores = scatter(body, bvh, 11 + variant + (3 if fin else 0), "S", glow, rims, 9, 12, (-1.45 * R, 1.45 * R), avoid=weak, keep=keep)
    finish_craters(body, rims, pores, "S", rock, glow)
    return body


def tail():
    clear()
    rock = mat("Rock", (0.30, 0.29, 0.28), 0.85)
    fin = mat("Fin", (0.16, 0.42, 0.24), 0.55)
    line = mat("FinLine", (0.05, 0.14, 0.08), 0.6)
    glow = mat("Glow", GREEN, 0.35, emit=2.5)
    p = [
        ellipsoid("core", (0.15 * R, 0, 0), (0.85 * R, 0.60 * R, 0.52 * R)),
        tube("taper", (0.25 * R, 0, 0), (-1.55 * R, 0, 0.02 * R), 0.55 * R, 0.14 * R, 24),
        plate("ridge", (-0.55 * R, 0, 0.34 * R), (0.36 * R, 0.20 * R, 0.08 * R), 0.06),
    ]
    body = skin_fuse(p, "Tail", voxel=0.45, noise=0.18, target=1700)
    body.data.materials.append(rock)
    bvh, _ = surface_bvh(body)

    # A tall shark caudal fin: upper lobe long, lower lobe short, scalloped trailing edge.
    fan = [(-1.05 * R, 0.08 * R), (-1.35 * R, 0.95 * R), (-2.10 * R, 1.45 * R), (-1.95 * R, 0.80 * R),
           (-2.30 * R, 0.45 * R), (-1.95 * R, 0.05 * R), (-2.20 * R, -0.25 * R), (-1.95 * R, -0.60 * R),
           (-2.05 * R, -1.00 * R), (-1.45 * R, -0.60 * R), (-1.05 * R, -0.05 * R)]
    f = profile_fin("TailFin", fan, 0.07 * R)
    f.data.materials.append(fin)
    adopt(f, body)

    # Cartoon fin lines: dark rays fanning from the root out to the tips, raised a touch on both faces.
    root = Vector((-1.12 * R, 0, 0.0))
    tips = [(-1.98 * R, 1.30 * R), (-1.85 * R, 0.85 * R), (-2.12 * R, 0.45 * R), (-1.95 * R, 0.08 * R),
            (-2.08 * R, -0.25 * R), (-1.92 * R, -0.62 * R), (-1.95 * R, -0.90 * R)]
    rays = []
    for k, (tx, tz) in enumerate(tips):
        end = Vector((tx, 0, tz))
        end = root + (end - root) * 0.88
        for y in (0.045 * R, -0.045 * R):
            rays.append(tube(f"ray{k}{'a' if y > 0 else 'b'}", root + Vector((0, y, 0)), end + Vector((0, y, 0)), 0.035 * R, 0.012 * R, 6))
    lines = assemble(rays, "FinLines", smooth=True)
    lines.data.materials.clear()
    lines.data.materials.append(line)
    adopt(lines, body)

    rims, weak = [], []
    for side, tag in ((1, "L"), (-1, "R")):
        hit, nrm = snap(bvh, -0.20 * R, side, 0.06 * R)
        if hit is not None:
            adopt(crater(f"Hole{tag}t", hit, nrm, 0.12 * R, glow, rims, depth=0.45), body)
            weak.append(hit)
    pores = scatter(body, bvh, 5, "T", glow, rims, 8, 9, (-1.45 * R, 0.95 * R), avoid=weak, rmin=0.04, rmax=0.075, gap=0.17)
    finish_craters(body, rims, pores, "T", rock, glow)
    return body


def side_fin(side):
    clear()
    fin = mat("Fin", (0.16, 0.42, 0.24), 0.55)
    # A big swept pectoral fin for swimming: root at the origin, spreading back (-X) and out (side * Y).
    k = 1.9
    pts = [(0.10 * R, 0.0), (0.0, 0.30 * R), (-0.35 * R, 0.62 * R), (-0.85 * R, 0.78 * R),
           (-1.25 * R, 0.72 * R), (-1.05 * R, 0.50 * R), (-1.35 * R, 0.34 * R), (-1.00 * R, 0.18 * R),
           (-1.10 * R, 0.02 * R), (-0.55 * R, -0.04 * R)]
    o = profile_fin("FinL" if side > 0 else "FinR", [(x * k, side * y * k) for x, y in pts], 0.09 * R, plane="xy")
    o.data.materials.append(fin)
    return o


def name_data(o):
    for x in [o] + list(o.children_recursive):
        if x.data is not None:
            x.data.name = x.name


def export(root, file):
    """Turn to Godot axes (s&box +X forward -> Blender +Y), scale R=32 to 0.5, export with its children."""
    os.makedirs(OUT, exist_ok=True)
    name_data(root)
    pivot = bpy.data.objects.new("Export", None)
    bpy.context.scene.collection.objects.link(pivot)
    root.parent = pivot
    pivot.rotation_euler = (0, 0, math.pi / 2)
    pivot.scale = (1 / 64,) * 3
    bpy.ops.object.select_all(action="DESELECT")
    pivot.select_set(True)
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, file), use_selection=True, export_format="GLB", export_yup=True)
    root.parent = None
    bpy.data.objects.remove(pivot, do_unlink=True)


def build():
    made = {}
    for name, fn in (("head.glb", head), ("segment0.glb", lambda: segment(0)), ("segment1.glb", lambda: segment(1)), ("segment_fin.glb", lambda: segment(0, fin=True)),
                     ("tail.glb", tail), ("fin_l.glb", lambda: side_fin(1)), ("fin_r.glb", lambda: side_fin(-1))):
        o = fn()
        export(o, name)
        made[name] = len(o.data.vertices)
    print("[rocky_shark] wrote", OUT)
    for name, verts in made.items():
        print(f"  {name:16s} {verts:5d} verts")
    return made


if __name__ == "__main__":
    build()