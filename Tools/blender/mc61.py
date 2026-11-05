# Builds every MC61 (boss) piece and exports it to Assets/models/creatures/mc61.
# Each shootable part (see MC61.cs Build) is made of a few pieces so each keeps its own colour:
# a jacket and a shirt on the torso, a hat body and a red band, a sleeve and a glove, and so on.
# Everything is modelled in its part's local space (pivot at the origin, +X forward, +Z up).
import math
exec(open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/creature_helpers.py").read())

C = "mc61"
stats = {}


def carve(o, cutters):
    """Boolean-subtract cutters from o (used before remeshing, so the cut edges get smoothed)."""
    for c in cutters:
        bpy.ops.object.select_all(action='DESELECT')
        c.select_set(True)
        bpy.context.view_layer.objects.active = c
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        m = o.modifiers.new("cut", 'BOOLEAN')
        m.operation = 'DIFFERENCE'
        m.object = c
        m.solver = 'EXACT'
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier="cut")
        bpy.data.objects.remove(c)


def sculpt(pieces, name, voxel, decimate, cutters=(), smooth=3, noise=0.0):
    """Join pieces, carve, then fuse into one smooth surface."""
    bpy.ops.object.select_all(action='DESELECT')
    for o in pieces:
        o.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    if cutters:
        # Remesh once first so the boolean works on a clean closed surface.
        m = o.modifiers.new("pre", 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = voxel
        bpy.ops.object.modifier_apply(modifier="pre")
        carve(o, cutters)
    return fuse([o], name, voxel=voxel, smooth=smooth, noise=noise, noise_scale=4, decimate=decimate)


def torus(name, center, major, minor, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, location=center, rotation=rot,
                                     major_segments=40, minor_segments=12)
    o = bpy.context.active_object
    o.name = name
    return o


def bevel_obj(o, width, segs=3):
    b = o.modifiers.new("bevel", 'BEVEL'); b.width = width; b.segments = segs; b.limit_method = 'ANGLE'
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier="bevel")
    return o


def out(o, part):
    stats[part] = export(o, C, part)


# ================= TORSO (pivot at the chest centre) =================
# Tailcoat: barrel chest, squared shoulders, raised lapels framing the shirt, two coat-tails at the back.
clear()
p = [ellipsoid("chest", (0, 0, 0), (12, 16, 21)),
     ellipsoid("belly", (1.5, 0, -8), (11.5, 14.5, 12))]
for s in (-1, 1):
    p.append(ellipsoid("shoulder", (-1, s * 13, 13), (8, 7.5, 6.5)))
    p.append(ellipsoid("lapel", (11.2, s * 6.6, 8), (1.1, 3.2, 10)))
    p.append(ellipsoid("lapelpeak", (10.6, s * 8.4, 15.5), (1.1, 3, 2.2)))
    p.append(ellipsoid("tail", (-9.5, s * 4.8, -21), (3, 5, 11)))
out(sculpt(p, "mc61_jacket", voxel=0.55, decimate=0.18, cutters=[
    ellipsoid("vcut", (15.5, 0, 6), (4.5, 6, 13.5))]), "torso_jacket")

# Shirt front with collar wings and three studs.
clear()
p = [ellipsoid("bib", (8.5, 0, 5), (4.6, 7, 13.5))]
for s in (-1, 1):
    p.append(ellipsoid("collar", (10.2, s * 3.6, 19.2), (2.6, 3.2, 2)))
for z in (9.5, 4.5, -0.5):
    p.append(ellipsoid("stud", (12.9, 0, z), (0.9, 0.9, 0.9)))
out(sculpt(p, "mc61_shirt", voxel=0.3, decimate=0.25), "torso_shirt")

# Bow tie: two pinched wings and a knot.
clear()
p = [ellipsoid("knot", (12.6, 0, 17), (1.6, 1.6, 1.7))]
for s in (-1, 1):
    p.append(tube("wing", (12.2, s * 0.8, 17), (12.2, s * 4.6, 17), 1.0, 2.6))
    p.append(ellipsoid("wingend", (12.2, s * 4.6, 17), (1.8, 1.2, 2.8)))
out(sculpt(p, "mc61_bowtie", voxel=0.2, decimate=0.35), "torso_bowtie")

# ================= HEAD (pivot at the neck, on top of the torso) =================
# One-eyed grinning head: deep eye socket with a raised rim, heavy brow ridge, cheekbones,
# a recessed grin with lips, a heavy jaw and pointed ears.
clear()
p = [ellipsoid("skull", (0, 0, 19), (19, 19, 20)),
     ellipsoid("jaw", (5, 0, 7), (13, 15.5, 9)),
     ellipsoid("chin", (13.5, 0, 3), (5, 8, 4)),
     tube("neck", (0, 0, -1), (0, 0, 6), 9, 10),
     torus("rim", (17.6, 0, 21), 9.7, 1.9, (0, math.radians(90), 0)),
     ellipsoid("brow", (15, 0, 31.5), (6, 12.5, 3.6)),
     ellipsoid("upperlip", (17.2, 0, 12.6), (2.6, 9.5, 1.5)),
     ellipsoid("lowerlip", (16.8, 0, 4.4), (2.6, 8.5, 1.6))]
for s in (-1, 1):
    p.append(ellipsoid("cheek", (13, s * 11, 14.5), (4.5, 4.5, 3.5)))
    p.append(cone("ear", (-1, s * 17.5, 22), (-0.35, s * 1, 0.6), 4.2, 11))
    p.append(ellipsoid("earbase", (-1, s * 17.8, 21.5), (3.4, 2.2, 4.4)))
    p.append(ellipsoid("mouthcorner", (15, s * 9.4, 8.8), (2, 1.6, 2.4)))
head = sculpt(p, "mc61_head", voxel=0.5, decimate=0.14, cutters=[
    ellipsoid("socket", (22.5, 0, 21), (9.6, 9.6, 9.6)),
    ellipsoid("mouth", (19.5, 0, 8.6), (4.2, 8.8, 3.3))])
out(head, "head")

# ================= HAT (pivot at the brim centre) =================
clear()
crown = bevel_obj(cyl("crown", (0, 0, 14), 11, 11.8, 24, verts=48), 0.9)
brim = cyl("brim", (0, 0, 1), 16.5, 16.5, 1.6, verts=64)
sub = brim.modifiers.new("subd", 'SUBSURF'); sub.levels = 0; sub.render_levels = 0  # placeholder, keeps ring verts
bpy.context.view_layer.objects.active = brim
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.inset(thickness=3.5)
bpy.ops.mesh.inset(thickness=2)
bpy.ops.object.mode_set(mode='OBJECT')
brim.modifiers.clear()
for v in brim.data.vertices:  # curl the brim up at the sides, like a proper topper
    r = math.hypot(v.co.x, v.co.y)
    if r > 11:
        v.co.z += 0.022 * (v.co.y ** 2) * min(1, (r - 11) / 5)
bevel_obj(brim, 0.5, 2)
out(assemble([crown, brim], "mc61_hat"), "hat_body")
clear()
out(assemble([bevel_obj(cyl("band", (0, 0, 4.6), 11.45, 11.55, 5, verts=48), 0.4, 2)], "mc61_band"), "hat_band")

# ================= ARM (pivot at the shoulder, hanging down -Z) =================
clear()
p = [tube("sleeve", (0, 0, 0), (0, 0, -20), 6.1, 5.2),
     ellipsoid("cap", (0, 0, -0.5), (6.6, 6.6, 6)),
     ellipsoid("elbow", (-0.5, 0, -19.5), (5.4, 5.3, 4.2))]
for z, r in ((-14.5, 5.9), (-17, 5.8)):
    p.append(ellipsoid("crease", (0.3, 0, z), (r, r, 0.9)))
out(sculpt(p, "mc61_arm", voxel=0.45, decimate=0.2), "arm_sleeve")

# ================= FOREARM (pivot at the elbow) =================
clear()
p = [tube("sleeve", (0, 0, 0), (0, 0, -17), 5.3, 4.7),
     ellipsoid("elbowcap", (-0.4, 0, 0), (5.2, 5.2, 4)),
     ellipsoid("crease", (0.4, 0, -6), (5.4, 5.4, 0.8))]
out(sculpt(p, "mc61_forearm", voxel=0.45, decimate=0.2), "forearm_sleeve")
clear()
out(assemble([bevel_obj(cyl("cuff", (0, 0, -18), 5.3, 5.5, 3.6, verts=32), 0.6, 2)], "mc61_cuff"), "forearm_cuff")


def glove(thumb_side):
    """A cartoon four-finger glove curled round the briefcase handle (handle sits at z -28.5)."""
    clear()
    p = [ellipsoid("palm", (0.4, 0, -23.5), (3.8, 4.8, 4.6)),
         ellipsoid("wrist", (0, 0, -20), (4.6, 4.6, 2.2)),
         ellipsoid("knuckles", (1.4, 0, -26.2), (3.2, 4.9, 1.8))]
    for k, y in enumerate((-3.3, -1.1, 1.1, 3.3)):
        drop = 0.4 * abs(k - 1.5)
        a, b, c = (2, y, -26), (3.4, y, -29.6 + drop), (0.6, y, -31 + drop)
        p += [tube("f1", a, b, 1.25, 1.15), ellipsoid("j", b, (1.25, 1.2, 1.25)),
              tube("f2", b, c, 1.15, 1.05), ellipsoid("tip", c, (1.1, 1.05, 1.1))]
    for z in (-24.5, -23):  # seam stitch lines on the back of the hand
        p.append(ellipsoid("seam", (-3.4, 0, z), (0.5, 3.2, 0.3)))
    t0 = (2.4, thumb_side * 3.6, -22.5)
    p += [tube("thumb", t0, (4.6, thumb_side * 1.8, -26.5), 1.5, 1.25), ellipsoid("thumbtip", (4.6, thumb_side * 1.8, -26.5), (1.3, 1.3, 1.3))]
    return sculpt(p, "mc61_glove", voxel=0.32, decimate=0.18)


out(glove(-1), "glove_l")  # left arm sits at +Y, so its thumb points inward (-Y)
out(glove(1), "glove_r")

# ================= BRIEFCASE (pivot at the handle top) =================
clear()
body = bevel_obj(box("case", (0, 0, -11), (7, 24, 15)), 1.0)
seam = bevel_obj(box("seam", (0, 0, -11), (7.4, 24.4, 0.6)), 0.25, 2)
out(assemble([body, seam], "mc61_case"), "case_body")
clear()
p = [tube("h1", (0, -3.2, -3.6), (0, -3.2, -1.6), 0.9, 0.9), tube("h2", (0, 3.2, -3.6), (0, 3.2, -1.6), 0.9, 0.9),
     tube("h3", (0, -3.4, -1.3), (0, 3.4, -1.3), 1.1, 1.1),
     ellipsoid("hk1", (0, -3.2, -1.4), (1.1, 1.1, 1.1)), ellipsoid("hk2", (0, 3.2, -1.4), (1.1, 1.1, 1.1))]
out(sculpt(p, "mc61_handle", voxel=0.18, decimate=0.3, smooth=2), "case_handle")
clear()
metal = []
for y in (-7, 7):
    metal.append(bevel_obj(box("clasp", (3.7, y, -5.2), (1.2, 2.8, 2.8)), 0.35, 2))
    metal.append(bevel_obj(box("clasp_lock", (4.4, y, -5.2), (0.6, 1.2, 1.2)), 0.15, 2))
for y in (-3.2, 3.2):
    metal.append(bevel_obj(box("mount", (0, y, -3.7), (3, 1.8, 0.6)), 0.2, 2))
for y in (-11.4, 11.4):
    for z in (-4.2, -17.8):
        metal.append(bevel_obj(box("corner", (0, y, z), (7.6, 1.8, 1.8)), 0.5, 2))
out(assemble(metal, "mc61_brass"), "case_metal")
clear()
lid = bevel_obj(box("lid", (0, 0, -7.2), (1.2, 22.6, 13.6)), 0.4, 2)
stitch = bevel_obj(box("stitch", (0.55, 0, -7.2), (0.3, 20.6, 11.6)), 0.1, 1)
out(assemble([lid, stitch], "mc61_lid"), "case_lid")

# ================= LEG (pivot at the hip) =================
clear()
p = [tube("thigh", (0, 0, 0), (0, 0, -22), 7.2, 6.1),
     ellipsoid("hip", (0, 0, -1), (7.6, 7.6, 6)),
     ellipsoid("knee", (0.6, 0, -21), (6.3, 6.1, 4)),
     ellipsoid("crease", (6.6, 0, -11), (0.9, 0.6, 10))]
out(sculpt(p, "mc61_leg", voxel=0.45, decimate=0.2), "leg_trouser")

# ================= SHIN (pivot at the knee) =================
clear()
p = [tube("shin", (0, 0, 0), (0, 0, -18.5), 6.1, 5.5),
     ellipsoid("kneecap", (0.6, 0, 0), (6.1, 6, 3.5)),
     ellipsoid("hem", (0.4, 0, -18.6), (6.4, 6.2, 1.8)),
     ellipsoid("crease", (5.9, 0, -9), (0.8, 0.55, 8.5))]
out(sculpt(p, "mc61_shin", voxel=0.45, decimate=0.2), "shin_trouser")
clear()
p = [ellipsoid("toe", (6, 0, -21.2), (6.5, 4.8, 3.1)),
     ellipsoid("vamp", (2.5, 0, -20.4), (5, 5, 3.5)),
     ellipsoid("heel", (-2, 0, -21), (3.8, 4.4, 3.2)),
     ellipsoid("toecap", (10.2, 0, -21.6), (2.6, 4.2, 2.4))]
shoe = sculpt(p, "mc61_shoe_upper", voxel=0.35, decimate=None, smooth=2)
sole = bevel_obj(box("sole", (4.2, 0, -23.8), (16.4, 9.6, 1.4)), 0.6, 2)
heelblock = bevel_obj(box("heelblock", (-2.2, 0, -24.8), (5, 8.4, 2.2)), 0.4, 2)
dec = shoe.modifiers.new("dec", 'DECIMATE'); dec.ratio = 0.25
bpy.context.view_layer.objects.active = shoe
bpy.ops.object.modifier_apply(modifier="dec")
out(assemble([shoe, sole, heelblock], "mc61_shoe"), "shoe")

result = stats
