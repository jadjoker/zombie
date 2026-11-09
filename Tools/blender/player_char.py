# Builds the third-person survivor: a cartoon character, one rigid piece per bone, exported as a skinned bind-pose model.
# Animation is done in game by posing the bones from code (see Code/PlayerRig.cs).
#   blender -b --factory-startup --python player_char.py
import bpy, math, os, sys
from mathutils import Vector, Matrix

HELPERS = open(r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/fps_rifle.py", encoding="utf-8").read().split("# ---------------------------------------------------------------- rig layout")[0]
exec(HELPERS)

OUT = r"C:/Users/smitt/Documents/zombie_survival/Assets/models/characters/survivor"
NAME = "survivor"
MATSET = "materials/characters/survivor"

PALETTE = {
    "body": (0.74, 0.78, 0.82), "frame": (0.17, 0.18, 0.21), "metal": (0.88, 0.90, 0.93),
    "accent": (0.95, 0.52, 0.14), "glow": (0.35, 0.95, 0.9),
}

def build_materials():
    for k, c in PALETTE.items():
        MATS[k] = mat(k, (*c, 1), 0.6)

S = (1, -1)   # +Y is the character's left
SIDE = {"L": 1, "R": -1}

SHOULDER = {s: Vector((0, SIDE[s] * 9.5, 56.0)) for s in SIDE}
ELBOW = {s: SHOULDER[s] + Vector((0, SIDE[s] * 1.8, -12.4)) for s in SIDE}
WRIST = {s: ELBOW[s] + Vector((0, SIDE[s] * 1.6, -11.4)) for s in SIDE}
HIP = {s: Vector((0, SIDE[s] * 4.8, 34.5)) for s in SIDE}
KNEE = {s: Vector((0.6, SIDE[s] * 4.8, 18.8)) for s in SIDE}
ANKLE = {s: Vector((0, SIDE[s] * 4.8, 5.2)) for s in SIDE}

def cyl_y(center, r, length, bone, material, verts=20):
    c = Vector(center)
    return tube(c - Vector((0, length / 2, 0)), c + Vector((0, length / 2, 0)), r, r, bone, material, verts)

def build_meshes():
    # pelvis and waist
    bevel_box((0, 0, 36.5), (9.0, 13.0, 7.0), 1.6, "pelvis", "frame")
    tube((0, 0, 39.5), (0, 0, 43.5), 4.4, 4.0, "pelvis", "frame", 16)                     # waist column
    bevel_box((1.2, 0, 36.2), (8.0, 11.5, 4.6), 1.2, "pelvis", "body")                    # hip plate
    # chest, power pack
    bevel_box((0, 0, 49.0), (10.8, 16.0, 14.0), 2.8, "spine", "body")                     # chest armour
    bevel_box((5.4, 0, 49.6), (1.2, 12.5, 3.2), 0.5, "spine", "accent")                   # chest stripe
    bevel_box((5.9, 0, 52.6), (0.9, 5.0, 2.2), 0.4, "spine", "glow")                      # chest light
    bevel_box((5.2, 0, 45.2), (1.0, 11.0, 4.0), 0.5, "spine", "frame")                    # grille
    bevel_box((-7.2, 0, 49.5), (4.6, 11.5, 12.0), 1.4, "spine", "frame")                  # power pack
    for y in (-3.2, 3.2):
        tube((-8.6, y, 55.0), (-8.6, y, 61.0), 1.3, 1.3, "spine", "metal", 14)           # exhausts
        ball((-8.6, y, 61.2), (1.4, 1.4, 0.6), "spine", "glow", 12)
    # head
    tube((0, 0, 55.5), (0, 0, 59.5), 2.4, 2.4, "head", "frame", 14)                       # neck
    bevel_box((0.4, 0, 65.6), (12.6, 13.6, 11.6), 3.6, "head", "body")                    # head shell
    bevel_box((6.0, 0, 65.8), (3.0, 11.0, 5.0), 1.3, "head", "frame")                     # visor
    bevel_box((7.3, 0, 65.8), (0.9, 9.0, 2.6), 0.4, "head", "glow")                       # eye strip
    bevel_box((0.4, 0, 71.8), (8.0, 8.0, 1.6), 0.6, "head", "accent")                     # crest
    for s_ in (-1, 1):
        cyl_y((0.2, s_ * 7.2, 65.4), 2.6, 1.8, "head", "metal", 20)                       # ear discs
    tube((-1.0, 3.6, 72.0), (-1.0, 3.6, 77.5), 0.55, 0.55, "head", "metal", 10)           # antenna
    ball((-1.0, 3.6, 78.0), (1.3, 1.3, 1.3), "head", "glow", 14)
    # arms
    for s in SIDE:
        sh, el, wr = SHOULDER[s], ELBOW[s], WRIST[s]
        ball(sh, (4.6, 4.8, 4.4), f"upper_{s}", "accent", 24)                              # pauldron
        tube(sh, el, 2.3, 2.1, f"upper_{s}", "frame", 14)                                  # piston
        tube(sh + (el - sh) * 0.08, sh + (el - sh) * 0.62, 3.3, 3.0, f"upper_{s}", "body", 18)   # sleeve armour
        ball(el, (3.2, 3.2, 3.2), f"lower_{s}", "metal", 20)                               # elbow joint
        tube(el, wr - Vector((0, 0, 1.0)), 2.4, 2.0, f"lower_{s}", "frame", 14)            # forearm core
        tube(el + (wr - el) * 0.18, el + (wr - el) * 0.78, 3.0, 2.7, f"lower_{s}", "body", 18)   # bracer
        tube(el + (wr - el) * 0.74, el + (wr - el) * 0.82, 3.1, 3.1, f"lower_{s}", "glow", 18)   # wrist light
        bevel_box(wr + Vector((0.5, 0, -2.4)), (4.2, 4.6, 5.0), 1.1, f"hand_{s}", "metal")       # palm
        bevel_box(wr + Vector((2.7, 0, -4.4)), (2.6, 3.4, 3.2), 0.8, f"hand_{s}", "frame")      # fingers
    # legs
    for s in SIDE:
        y = SIDE[s]
        hp, kn, an = HIP[s], KNEE[s], ANKLE[s]
        ball(hp, (4.4, 4.4, 4.4), f"thigh_{s}", "metal", 22)                               # hip joint
        tube(hp, kn, 2.8, 2.5, f"thigh_{s}", "frame", 14)                                  # piston
        tube(hp + (kn - hp) * 0.06, hp + (kn - hp) * 0.72, 4.1, 3.5, f"thigh_{s}", "body", 20)   # thigh armour
        ball(kn, (3.6, 3.6, 3.6), f"shin_{s}", "metal", 22)                                # knee joint
        bevel_box(kn + Vector((2.6, 0, 0.3)), (2.6, 5.4, 5.0), 1.0, f"shin_{s}", "accent")      # knee cap
        tube(kn, an + Vector((0, 0, 2.0)), 2.6, 2.2, f"shin_{s}", "frame", 14)             # shin core
        tube(kn + (an - kn) * 0.18, kn + (an - kn) * 0.82, 3.5, 3.0, f"shin_{s}", "body", 20)    # shin armour
        bevel_box((2.4, y * 4.8, 3.4), (12.6, 6.8, 6.4), 1.8, f"foot_{s}", "body")          # foot
        bevel_box((6.6, y * 4.8, 3.0), (4.4, 6.9, 3.6), 1.0, f"foot_{s}", "frame")          # toe
        bevel_box((2.6, y * 4.8, 0.7), (13.0, 7.1, 1.5), 0.6, f"foot_{s}", "frame")         # sole

BONES = ["root", "pelvis", "spine", "head",
         "upper_L", "lower_L", "hand_L", "upper_R", "lower_R", "hand_R",
         "thigh_L", "shin_L", "foot_L", "thigh_R", "shin_R", "foot_R"]
REST = {}

def build_armature():
    arm_data = bpy.data.armatures.new("rig")
    arm = bpy.data.objects.new("rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def add(name, head, tail, parent=None, up=Vector((1, 0, 0))):
        b = eb.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent:
            b.parent = eb[parent]
        b.align_roll(up)
        return b

    add("root", (0, 0, 0), (0, 0, 3))
    add("pelvis", (0, 0, 36.5), (0, 0, 41), "root")
    add("spine", (0, 0, 41), (0, 0, 56), "pelvis")
    add("head", (0, 0, 56), (0, 0, 73), "spine")
    for s in SIDE:
        add(f"upper_{s}", SHOULDER[s], ELBOW[s], "spine")
        add(f"lower_{s}", ELBOW[s], WRIST[s], f"upper_{s}")
        add(f"hand_{s}", WRIST[s], WRIST[s] + Vector((0, 0, -6.0)), f"lower_{s}")
        add(f"thigh_{s}", HIP[s], KNEE[s], "pelvis")
        add(f"shin_{s}", KNEE[s], ANKLE[s], f"thigh_{s}")
        add(f"foot_{s}", ANKLE[s], ANKLE[s] + Vector((9.0, 0, 0)), f"shin_{s}")
    bpy.ops.object.mode_set(mode='OBJECT')
    for n in BONES:
        REST[n] = arm.data.bones[n].matrix_local.copy()
    return arm

def join_and_skin(arm):
    for o, bone, _ in PARTS:
        vg = o.vertex_groups.new(name=bone)
        vg.add([v.index for v in o.data.vertices], 1.0, 'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    for o, _, _ in PARTS:
        o.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0][0]
    bpy.ops.object.join()
    mesh = bpy.context.active_object
    mesh.name = "mesh"
    mod = mesh.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    mesh.parent = arm
    bpy.ops.object.shade_smooth()
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    return mesh

W = Matrix.Rotation(-math.pi / 2, 4, 'Z')

def rotate_rig(arm, mesh):
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    for o in (arm, mesh):
        o.matrix_world = W @ o.matrix_world
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    mesh.parent = arm
    mesh.matrix_parent_inverse = Matrix.Identity(4)

def export_fbx(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, global_scale=0.01, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Y', axis_up='Z', bake_space_transform=True,
                             object_types={'ARMATURE', 'MESH'}, mesh_smooth_type='FACE', add_leaf_bones=False,
                             use_armature_deform_only=False, bake_anim=False)

def write_vmdl(path):
    remaps = ""
    for m in MATS:
        remaps += f'''							{{
								from = "{m}.vmat"
								to = "{MATSET}/{m}.vmat"
							}},
'''
    att = ""
    for name, bone, off in (("hand_r", "hand_R", (0, 0, -5.0)), ("hand_l", "hand_L", (0, 0, -5.0)), ("head", "head", (0, 0, 10.0))):
        att += f'''						{{
							_class = "Attachment"
							name = "{name}"
							parent_bone = "{bone}"
							relative_origin = [ {off[0]}, {off[1]}, {off[2]} ]
							relative_angles = [ 0.0, 0.0, 0.0 ]
							weight = 1.0
							ignore_rotation = false
						}},
'''
    open(path, "w", encoding="utf-8", newline="").write(f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc29:version{{3cec427c-1b0e-4d48-a90a-0436f33a6041}} -->
{{
	rootNode =
	{{
		_class = "RootNode"
		children =
		[
			{{
				_class = "MaterialGroupList"
				children =
				[
					{{
						_class = "DefaultMaterialGroup"
						remaps =
						[
{remaps}						]
						use_global_default = false
						global_default_material = ""
					}},
				]
			}},
			{{
				_class = "RenderMeshList"
				children =
				[
					{{
						_class = "RenderMeshFile"
						filename = "models/characters/{NAME}/{NAME}.fbx"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = 1.0
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{{
							exclude_by_default = false
							exception_list = [  ]
						}}
					}},
				]
			}},
			{{
				_class = "AttachmentList"
				children =
				[
{att}				]
			}},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}}
}}
''')

def main():
    os.makedirs(OUT, exist_ok=True)
    clear()
    build_materials()
    build_meshes()
    arm = build_armature()
    mesh = join_and_skin(arm)
    rotate_rig(arm, mesh)
    export_fbx(f"{OUT}/{NAME}.fbx", [arm, mesh])
    write_vmdl(f"{OUT}/{NAME}.vmdl")
    bpy.ops.wm.save_as_mainfile(filepath=r"C:/Users/smitt/Documents/zombie_survival/Tools/blender/player_char.blend")
    print("built", len(mesh.data.polygons), "polys")

if __name__ == "__main__":
    main()
