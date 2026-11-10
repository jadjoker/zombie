"""
Bloober as a sea serpent (Risk of Copyright, Godot). Builds and exports to godot/assets/models/creatures/bloober/:
  serpent_head.glb      skull with eye sockets, crest and frills, upper teeth, a dark mouth, and a separate "Jaw"
                        (lower jaw with its teeth) pivoted at the hinge so the game can open it
  serpent_segment0/1.glb  a scaled body section with a fin ribbon along the back (two variants)
  serpent_tail.glb      the last section with a tall tail fin
  serpent_fin.glb       a side fin (the game adds a pair behind the head and flaps them)

Run in Blender (works in its own scene, "Bloober", inside the open file):
    exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/bloober_serpent.py").read())

Modelled in s&box units around the unit "sphere" each section replaces (radius R = 32, +X forward, +Y left,
+Z up), like the old bloober.py, then exported at 1/64 scale turned to Godot axes, so each part is a
radius-0.5 shape facing -Z. The game scales it per section (Stretch x r along its length, r across).
Materials: "Skin" is replaced by the creature's tinted skin in game; "Teeth" and "Mouth" are kept.
"""
import bpy
import math
import os
from mathutils import Vector

exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

OUT = r"C:/Users/smitt/Documents/zombie_survival/godot/assets/models/creatures/bloober"
R = 32.0


def mat(name, color, rough=0.45, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def flat_fin(name, base, length, height, thickness, sweep=0.0, rays=0):
    """A thin fin: a flattened ellipsoid standing up from base, leaning back by sweep, with optional ray ridges."""
    parts = [ellipsoid(name, (base[0] - sweep * 0.5, base[1], base[2] + height * 0.5), (length, thickness, height * 0.55))]
    for k in range(rays):
        t = (k + 0.5) / rays
        x = base[0] - length * 0.8 + t * length * 1.6
        parts.append(tube(name + "_ray", (x, base[1], base[2]), (x - sweep * 0.6, base[1], base[2] + height * 0.95 * math.sin(t * math.pi) + 1), thickness * 1.6, thickness * 0.6, 8))
    return parts


def profile_fin(name, pts, thickness, plane="xz", at=(0, 0, 0), rot=(0, 0, 0)):
    """A crisp thin fin from a 2D outline (s&box units): pts in the x/z plane (or x/y with plane="xy"),
    given a little thickness. Not remeshed, so the edges stay sharp."""
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
    o.location = at
    o.rotation_euler = rot
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.object.modifier_apply(modifier="solid")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    return o


def sail(x0, x1, base, height, rays, sweep=0.35, scallop=0.18):
    """Outline of a fin ribbon from x0 (back) to x1 (front): rises from base, a scalloped top edge
    (one bump per ray), swept back."""
    pts = [(x1, base)]
    n = rays * 6
    for k in range(n + 1):
        t = 1 - k / n                                    # front to back
        x = x0 + (x1 - x0) * t
        env = math.sin(min(1.0, max(0.0, t)) * math.pi) ** 0.7
        bump = 1 - scallop * (0.5 - 0.5 * math.cos(k / n * rays * math.tau))
        h = height * env * bump
        pts.append((x - sweep * h, base + h))
    pts.append((x0, base))
    return pts


def skin_fuse(parts, name, voxel=0.55, smooth=3, noise=0.25, target=2200):
    """Fused, smoothed and decimated to about `target` vertices (dozens of these swim at once)."""
    o = fuse(parts, name, voxel=voxel, smooth=smooth, noise=noise, noise_scale=5, decimate=None)
    dec = o.modifiers.new("dec", "DECIMATE")
    dec.ratio = min(1.0, target / max(1, len(o.data.vertices)))
    bpy.ops.object.modifier_apply(modifier="dec")
    bpy.ops.object.shade_smooth()
    o.data.materials.clear()
    return o


def teeth_row(name, x0, x1, count, z, y_half, pointing, length, curve_y=0.0):
    out = []
    for k in range(count):
        t = k / max(1, count - 1)
        x = x0 + (x1 - x0) * t
        for side in (-1, 1):
            # Teeth follow the jaw's curve: wider apart at the back, meeting at the tip.
            y = side * (y_half * (1 - t * 0.85) + curve_y)
            ln = length * (1.35 if k in (count - 2, count - 1) else 1.0)
            out.append(cone(name, (x, y, z), (0.15, 0, pointing), 1.6 if k < count - 2 else 2.1, ln, 8))
    return out


def head():
    clear()
    skin, teeth, mouth = mat("Skin", (0.2, 0.4, 0.35)), mat("Teeth", (0.92, 0.88, 0.76), 0.3), mat("Mouth", (0.12, 0.01, 0.03), 0.6)
    fin = mat("FinSkin", (0.3, 0.7, 0.65), 0.35)
    # Skull: long, low and narrow (a serpent, not a blob), flat on top, tapering to a blunt snout.
    p = [
        ellipsoid("cranium", (-0.15 * R, 0, 0.08 * R), (R * 0.9, R * 0.56, R * 0.5)),
        ellipsoid("skull top", (0.35 * R, 0, 0.18 * R), (R * 0.95, R * 0.4, R * 0.26)),
        ellipsoid("snout", (0.95 * R, 0, 0.03 * R), (R * 0.75, R * 0.34, R * 0.24)),
        ellipsoid("muzzle", (1.55 * R, 0, 0.0), (R * 0.24, R * 0.22, R * 0.17)),
        ellipsoid("upper lip", (0.85 * R, 0, -0.14 * R), (R * 0.9, R * 0.38, R * 0.1)),
        ellipsoid("throat", (-0.3 * R, 0, -0.22 * R), (R * 0.7, R * 0.5, R * 0.36)),
    ]
    for side in (-1, 1):
        # A low ridge over each eye and a small socket rim; the game puts the glowing eye in it.
        p.append(ellipsoid("brow", (0.75 * R, side * 0.33 * R, 0.36 * R), (R * 0.3, R * 0.09, R * 0.07)))
        p.append(ellipsoid("eye ring", (0.78 * R, side * 0.37 * R, 0.3 * R), (R * 0.15, R * 0.07, R * 0.11)))
        p.append(ellipsoid("cheek", (-0.1 * R, side * 0.46 * R, -0.08 * R), (R * 0.42, R * 0.12, R * 0.24)))
        p.append(ellipsoid("nostril", (1.5 * R, side * 0.11 * R, 0.12 * R), (R * 0.08, R * 0.05, R * 0.04)))
    skull = skin_fuse(p, "Head", voxel=0.45, target=3200, noise=0.15)
    skull.data.materials.append(skin)
    # Crest along the top of the skull, frills fanning back behind the cheeks.
    crest = profile_fin("Crest", sail(-1.1 * R, 0.55 * R, 0.4 * R, 0.55 * R, 4, sweep=0.5), 0.07 * R)
    frills = []
    fan = [(0.0, 0.0), (-0.35 * R, 0.5 * R), (-0.7 * R, 0.55 * R), (-0.6 * R, 0.3 * R), (-0.85 * R, 0.2 * R),
           (-0.62 * R, 0.02 * R), (-0.85 * R, -0.18 * R), (-0.55 * R, -0.25 * R), (-0.2 * R, -0.12 * R)]
    for side in (-1, 1):
        frills.append(profile_fin("Frill", fan, 0.06 * R, at=(-0.6 * R, side * 0.45 * R, 0.05 * R), rot=(0, 0, side * math.radians(28))))
    for f in [crest] + frills:
        f.data.materials.append(fin)
        f.parent = skull
        f.matrix_parent_inverse = skull.matrix_world.inverted()

    # The inside of the mouth, dark, between the jaws.
    cav = ellipsoid("MouthInside", (0.7 * R, 0, -0.24 * R), (R * 0.75, R * 0.3, R * 0.1))
    bpy.ops.object.transform_apply(scale=True)
    cav.data.materials.append(mouth)
    bpy.ops.object.shade_smooth()
    upper = teeth_row("tooth", 0.3 * R, 1.5 * R, 7, -0.2 * R, 0.3 * R, -1, R * 0.22)
    for t in upper:
        t.data.materials.append(teeth)
    upper_teeth = assemble_simple(upper, "UpperTeeth")

    # Lower jaw: its own object, pivoted at the hinge under the cheeks.
    hinge = Vector((-0.2 * R, 0, -0.2 * R))
    j = [
        ellipsoid("jaw", (0.7 * R, 0, -0.34 * R), (R * 0.9, R * 0.34, R * 0.15)),
        ellipsoid("chin", (1.4 * R, 0, -0.32 * R), (R * 0.22, R * 0.2, R * 0.12)),
        ellipsoid("jaw back", (0.0, 0, -0.3 * R), (R * 0.35, R * 0.44, R * 0.18)),
    ]
    jaw = skin_fuse(j, "Jaw", voxel=0.5, target=900)
    jaw.data.materials.append(skin)
    lower = teeth_row("tooth", 0.4 * R, 1.45 * R, 6, -0.24 * R, 0.27 * R, 1, R * 0.18)
    for t in lower:
        t.data.materials.append(teeth)
    lower_teeth = assemble_simple(lower, "LowerTeeth")
    for o in (jaw, lower_teeth):
        set_origin(o, hinge)
    lower_teeth.parent = jaw
    lower_teeth.matrix_parent_inverse = jaw.matrix_world.inverted()
    for o in (jaw, cav, upper_teeth):
        o.parent = skull
        o.matrix_parent_inverse = skull.matrix_world.inverted()
    return skull


def set_origin(o, point):
    bpy.context.scene.cursor.location = point
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = (0, 0, 0)


def assemble_simple(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_smooth()
    return o


def segment(variant):
    clear()
    skin, fin = mat("Skin", (0.2, 0.4, 0.35)), mat("FinSkin", (0.3, 0.7, 0.65), 0.35)
    # A smooth spindle (tapering at both ends so neighbours blend into one body), a slightly flat belly,
    # and a faint ridge along the back.
    p = [
        ellipsoid("core", (0, 0, 0), (R * 1.4, R * 0.8, R * 0.76)),
        ellipsoid("belly", (0, 0, -0.3 * R), (R * 1.2, R * 0.66, R * 0.42)),
        ellipsoid("ridge", (0, 0, 0.52 * R), (R * 1.1, R * 0.16, R * 0.22)),
    ]
    # Side bumps where the glowing spots sit.
    for side in (-1, 1):
        for x in ((-0.5, 0.0, 0.5) if variant == 0 else (-0.25, 0.25)):
            p.append(ellipsoid("bump", (x * R, side * R * 0.76, 0.1 * R), (R * 0.12, R * 0.08, R * 0.12)))
    body = skin_fuse(p, f"Segment{variant}", voxel=0.55, noise=0.2)
    body.data.materials.append(skin)
    # The fin ribbon along the back: overlaps the next section's so it reads as one continuous fin.
    height = 0.62 if variant == 0 else 0.5
    f = profile_fin("Dorsal", sail(-1.45 * R, 1.25 * R, 0.5 * R, height * R, 4 if variant == 0 else 3), 0.07 * R)
    f.data.materials.append(fin)
    f.parent = body
    f.matrix_parent_inverse = body.matrix_world.inverted()
    return body


def tail():
    clear()
    skin, fin = mat("Skin", (0.2, 0.4, 0.35)), mat("FinSkin", (0.3, 0.7, 0.65), 0.35)
    p = [
        ellipsoid("core", (0.35 * R, 0, 0), (R * 1.1, R * 0.72, R * 0.68)),
        tube("taper", (0.0, 0, 0), (-1.45 * R, 0, 0.03 * R), R * 0.6, R * 0.12, 24),
    ]
    body = skin_fuse(p, "Tail", voxel=0.5, noise=0.15, target=1800)
    body.data.materials.append(skin)
    # Tail fan: a tall swept fin, upper lobe longer, scalloped trailing edge.
    fan = [(-1.2 * R, 0.1 * R), (-1.55 * R, 0.9 * R), (-2.25 * R, 1.35 * R), (-2.05 * R, 0.85 * R), (-2.35 * R, 0.55 * R),
           (-2.05 * R, 0.2 * R), (-2.3 * R, -0.15 * R), (-2.05 * R, -0.45 * R), (-2.15 * R, -0.85 * R), (-1.55 * R, -0.55 * R), (-1.2 * R, -0.05 * R)]
    f1 = profile_fin("TailFan", fan, 0.07 * R)
    f2 = profile_fin("Dorsal", sail(-1.2 * R, 1.0 * R, 0.45 * R, 0.4 * R, 3), 0.07 * R)
    for f in (f1, f2):
        f.data.materials.append(fin)
        f.parent = body
        f.matrix_parent_inverse = body.matrix_world.inverted()
    return body


def side_fin():
    clear()
    fin = mat("FinSkin", (0.3, 0.7, 0.65), 0.35)
    # A swept leaf from its root at the origin, spreading back (-X) and out (+Y), scalloped trailing edge.
    pts = [(0.05 * R, 0.0), (-0.1 * R, 0.35 * R), (-0.45 * R, 0.62 * R), (-0.75 * R, 0.66 * R), (-0.7 * R, 0.5 * R),
           (-0.92 * R, 0.42 * R), (-0.78 * R, 0.28 * R), (-0.95 * R, 0.15 * R), (-0.7 * R, 0.05 * R), (-0.35 * R, -0.05 * R)]
    o = profile_fin("Fin", pts, 0.07 * R, plane="xy")
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


def use_scene(name):
    scene = bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
    bpy.context.window.scene = scene
    return scene


def build(save=True):
    use_scene("Bloober Build")
    made = {}
    for name, fn in (("serpent_head.glb", head), ("serpent_segment0.glb", lambda: segment(0)), ("serpent_segment1.glb", lambda: segment(1)),
                     ("serpent_tail.glb", tail), ("serpent_fin.glb", side_fin)):
        o = fn()
        export(o, name)
        made[name] = len(o.data.vertices)
    if save:
        # A "Bloober" scene with every part lined up (head, sections, tail, fin) for viewing and tweaking.
        use_scene("Bloober")
        clear()
        x = 0.0
        for name in made:
            before = set(bpy.data.objects)
            bpy.ops.import_scene.gltf(filepath=os.path.join(OUT, name))
            for o in set(bpy.data.objects) - before:
                if o.parent is None:
                    o.location.x = x
            x += 1.6
    return made


if __name__ == "__main__":
    build()
