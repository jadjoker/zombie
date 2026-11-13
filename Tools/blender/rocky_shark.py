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
GREEN = (0.12, 0.85, 0.25)


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


def teeth_row(name, x0, x1, count, z, y_half, up, length, taper=0.9):
    """A row of teeth along +X, either side, with the last two a little longer."""
    out = []
    for k in range(count):
        t = k / max(1, count - 1)
        x = x0 + (x1 - x0) * t
        for side in (-1, 1):
            y = side * y_half * (1 - t * taper)
            ln = length * (1.4 if k >= count - 2 else 1.0)
            out.append(cone(f"{name}{k}{'L' if side > 0 else 'R'}", (x, y, z), (0.05, 0, 1 if up else -1), 1.6, ln, 8))
    return out


def hole(name, at, side, size=1.0):
    """A glowing radiation hole on a flank: a flattened sphere that pokes out of the rock."""
    o = ellipsoid(name, at, (0.10 * R * size, 0.055 * R * size, 0.12 * R * size), segs=16)
    o.rotation_euler = (0.0, 0.0, side * math.radians(12))
    return o


def head():
    clear()
    rock = mat("Rock", (0.30, 0.29, 0.28), 0.85)
    teeth = mat("Teeth", (0.92, 0.88, 0.76), 0.3)
    mouth = mat("Mouth", (0.10, 0.01, 0.02), 0.7)
    glow = mat("Glow", GREEN, 0.35, emit=2.5)
    eye = mat("Eye", (0.45, 1.0, 0.35), 0.2, emit=3.0)

    # A long low skull, a blunt snout, heavy brows, cheek plates and a few cracked slabs on top.
    p = [
        ellipsoid("skull", (0.55 * R, 0, 0.06 * R), (1.18 * R, 0.70 * R, 0.58 * R)),
        ellipsoid("snout", (1.42 * R, 0, -0.02 * R), (0.68 * R, 0.46 * R, 0.36 * R)),
        ellipsoid("snoutTip", (1.92 * R, 0, -0.04 * R), (0.34 * R, 0.26 * R, 0.20 * R)),
        ellipsoid("browL", (0.88 * R, 0.50 * R, 0.30 * R), (0.48 * R, 0.20 * R, 0.18 * R)),
        ellipsoid("browR", (0.88 * R, -0.50 * R, 0.30 * R), (0.48 * R, 0.20 * R, 0.18 * R)),
        ellipsoid("cheekL", (0.55 * R, 0.64 * R, -0.10 * R), (0.56 * R, 0.16 * R, 0.34 * R)),
        ellipsoid("cheekR", (0.55 * R, -0.64 * R, -0.10 * R), (0.56 * R, 0.16 * R, 0.34 * R)),
        plate("slabA", (0.15 * R, 0, 0.50 * R), (0.36 * R, 0.32 * R, 0.10 * R), 0.10),
        plate("slabB", (0.72 * R, 0, 0.54 * R), (0.30 * R, 0.28 * R, 0.10 * R), -0.12),
        plate("slabC", (1.22 * R, 0, 0.36 * R), (0.24 * R, 0.22 * R, 0.09 * R), 0.08),
        plate("slabL", (0.30 * R, 0.55 * R, 0.24 * R), (0.34 * R, 0.10 * R, 0.20 * R), 0.25),
        plate("slabR", (0.30 * R, -0.55 * R, 0.24 * R), (0.34 * R, 0.10 * R, 0.20 * R), -0.25),
    ]
    body = skin_fuse(p, "Head", voxel=0.48, noise=0.24, target=2600)
    body.data.materials.append(rock)

    # Upper teeth and the dark inside of the mouth.
    upper = assemble(teeth_row("ToothU", 0.95 * R, 2.10 * R, 5, -0.16 * R, 0.32 * R, False, 0.20 * R), "TeethUpper", smooth=True)
    upper.data.materials.append(teeth)
    upper.parent = body
    upper.matrix_parent_inverse = body.matrix_world.inverted()

    cav = ellipsoid("Mouth", (1.35 * R, 0, -0.26 * R), (0.72 * R, 0.36 * R, 0.10 * R), segs=24)
    bpy.ops.object.transform_apply(scale=True)
    cav.data.materials.append(mouth)
    cav.parent = body
    cav.matrix_parent_inverse = body.matrix_world.inverted()

    # The lower jaw: modelled around its hinge at the origin, then moved under the skull. The game rotates
    # this node on X to open and close the bite.
    jp = [
        ellipsoid("jawbar", (0.75 * R, 0, -0.02 * R), (0.82 * R, 0.36 * R, 0.12 * R)),
        ellipsoid("jawchin", (1.40 * R, 0, -0.04 * R), (0.34 * R, 0.25 * R, 0.10 * R)),
    ]
    jaw = skin_fuse(jp, "Jaw", voxel=0.4, smooth=1, noise=0.10, target=800)
    jaw.data.materials.append(rock)
    lower = assemble(teeth_row("ToothJ", 0.55 * R, 1.60 * R, 4, 0.06 * R, 0.23 * R, True, 0.16 * R), "TeethLower", smooth=True)
    lower.data.materials.append(teeth)
    lower.parent = jaw
    lower.matrix_parent_inverse = jaw.matrix_world.inverted()
    jaw.location = (0.45 * R, 0, -0.22 * R)
    jaw.parent = body
    jaw.matrix_parent_inverse = body.matrix_world.inverted()

    # Eyes in the sockets under the brows: bright green, the weak point the game watches.
    for side, name in ((1, "EyeL"), (-1, "EyeR")):
        e = ellipsoid(name, (0.92 * R, side * 0.46 * R, 0.22 * R), (0.15 * R, 0.15 * R, 0.15 * R), segs=20)
        e.data.materials.append(eye)
        e.parent = body
        e.matrix_parent_inverse = body.matrix_world.inverted()

    # Radiation holes: three per flank behind the eyes, glowing green.
    for side, tag in ((1, "L"), (-1, "R")):
        for i, x in enumerate((0.40, 0.02, -0.34)):
            h = hole(f"Hole{tag}{i}", (x * R, side * 0.66 * R, 0.10 * R), side, 1.0 if i else 1.15)
            h.data.materials.append(glow)
            h.parent = body
            h.matrix_parent_inverse = body.matrix_world.inverted()
    return body


def segment(variant):
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
    body = skin_fuse(p, f"Segment{variant}", voxel=0.5, noise=0.22, target=2200)
    body.data.materials.append(rock)
    for side, tag in ((1, "L"), (-1, "R")):
        h = hole(f"Hole{tag}{variant}", (0.0, side * 0.64 * R, 0.08 * R), side, 1.15)
        h.data.materials.append(glow)
        h.parent = body
        h.matrix_parent_inverse = body.matrix_world.inverted()
    return body


def tail():
    clear()
    rock = mat("Rock", (0.30, 0.29, 0.28), 0.85)
    fin = mat("Fin", (0.16, 0.42, 0.24), 0.55)
    glow = mat("Glow", GREEN, 0.35, emit=2.5)
    p = [
        ellipsoid("core", (0.15 * R, 0, 0), (0.85 * R, 0.60 * R, 0.52 * R)),
        tube("taper", (0.25 * R, 0, 0), (-1.55 * R, 0, 0.02 * R), 0.55 * R, 0.14 * R, 24),
        plate("ridge", (-0.55 * R, 0, 0.34 * R), (0.36 * R, 0.20 * R, 0.08 * R), 0.06),
    ]
    body = skin_fuse(p, "Tail", voxel=0.45, noise=0.18, target=1700)
    body.data.materials.append(rock)

    # A tall shark caudal fin: upper lobe long, lower lobe short, scalloped trailing edge.
    fan = [(-1.05 * R, 0.08 * R), (-1.35 * R, 0.95 * R), (-2.10 * R, 1.45 * R), (-1.95 * R, 0.80 * R),
           (-2.30 * R, 0.45 * R), (-1.95 * R, 0.05 * R), (-2.20 * R, -0.25 * R), (-1.95 * R, -0.60 * R),
           (-2.05 * R, -1.00 * R), (-1.45 * R, -0.60 * R), (-1.05 * R, -0.05 * R)]
    f = profile_fin("TailFin", fan, 0.07 * R)
    f.data.materials.append(fin)
    f.parent = body
    f.matrix_parent_inverse = body.matrix_world.inverted()

    for side, tag in ((1, "L"), (-1, "R")):
        h = hole(f"Hole{tag}t", (-0.20 * R, side * 0.44 * R, 0.06 * R), side, 0.8)
        h.data.materials.append(glow)
        h.parent = body
        h.matrix_parent_inverse = body.matrix_world.inverted()
    return body


def side_fin(side):
    clear()
    fin = mat("Fin", (0.16, 0.42, 0.24), 0.55)
    # A swept pectoral fin: root at the origin, spreading back (-X) and out (side * Y).
    pts = [(0.10 * R, 0.0), (0.0, 0.30 * R), (-0.35 * R, 0.62 * R), (-0.85 * R, 0.78 * R),
           (-1.25 * R, 0.72 * R), (-1.05 * R, 0.50 * R), (-1.35 * R, 0.34 * R), (-1.00 * R, 0.18 * R),
           (-1.10 * R, 0.02 * R), (-0.55 * R, -0.04 * R)]
    o = profile_fin("FinL" if side > 0 else "FinR", [(x, side * y) for x, y in pts], 0.07 * R, plane="xy")
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
    for name, fn in (("head.glb", head), ("segment0.glb", lambda: segment(0)), ("segment1.glb", lambda: segment(1)),
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