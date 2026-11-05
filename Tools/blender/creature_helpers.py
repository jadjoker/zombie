# Helpers for modelling creature parts in Blender for Risk of Copyright.
# Each part is built from simple pieces, fused into one organic surface (voxel remesh),
# smoothed, given a little surface noise, UV-unwrapped and exported as FBX for s&box.
#
# Export convention: 1 Blender unit = 1 s&box unit, modelled in the part's local space
# (its pivot at the origin, +X forward, +Z up). The matching .vmdl uses import_rotation [0, -90, 0].
import bpy, os
from mathutils import Vector

ROOT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/creatures"


def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)


def ellipsoid(name, center, radii, segs=32):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=segs // 2, radius=1, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = radii
    return o


def tube(name, a, b, r0, r1, verts=16):
    """A tapered cylinder from point a to point b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.name = name
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    return o


def cone(name, base, direction, radius, length, verts=10):
    direction = Vector(direction).normalized()
    return tube(name, Vector(base), Vector(base) + direction * length, radius, 0.02, verts)


def fuse(objs, name, voxel=0.6, smooth=2, noise=0.0, noise_scale=3.0, decimate=None):
    """Join pieces into one continuous organic surface."""
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    m = o.modifiers.new("remesh", 'REMESH')
    m.mode = 'VOXEL'
    m.voxel_size = voxel
    m.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier="remesh")
    if smooth:
        s = o.modifiers.new("smooth", 'SMOOTH')
        s.iterations = smooth
        s.factor = 0.6
        bpy.ops.object.modifier_apply(modifier="smooth")
    if noise > 0:
        tex = bpy.data.textures.new(name + "_noise", 'VORONOI')
        tex.noise_scale = noise_scale
        dm = o.modifiers.new("displace", 'DISPLACE')
        dm.texture = tex
        dm.strength = noise
        dm.mid_level = 0.5
        bpy.ops.object.modifier_apply(modifier="displace")
    if decimate:
        dc = o.modifiers.new("dec", 'DECIMATE')
        dc.ratio = decimate
        bpy.ops.object.modifier_apply(modifier="dec")
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    return o


def export(o, creature, part):
    out = f"{ROOT}/{creature}"
    os.makedirs(out, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=f"{out}/{part}.fbx", use_selection=True, global_scale=0.01, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_NONE', axis_forward='X', axis_up='Z', bake_space_transform=True,
                             object_types={'MESH'}, mesh_smooth_type='FACE')
    return len(o.data.polygons)
