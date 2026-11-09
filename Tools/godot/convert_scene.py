"""
Converts an s&box .scene into a level file for the Godot port (godot/levels/<name>.json), which
LevelLoader builds at runtime. Copies the models and textures it needs into godot/assets.

Transforms are converted from s&box (inches, x forward, y left, z up) to Godot (metres, -z forward, x right, y up).
Usage: python Tools/godot/convert_scene.py water_level
"""
import json, os, re, shutil, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Assets")
DST = os.path.join(ROOT, "godot")
IN = 0.0254


def vec(s):
    return [float(x) for x in s.split(",")] if isinstance(s, str) else list(s)


def pos(s):
    x, y, z = vec(s)
    return [-y * IN, z * IN, -x * IN]


def quat(s):
    x, y, z, w = vec(s)
    return [-y, z, -x, w]


def scl(s):
    x, y, z = vec(s)
    return [y, z, x]


def color(s):
    return vec(s) if s else [1, 1, 1, 1]


def path_of(v):
    if isinstance(v, dict):
        return v.get("Path")
    return v


def copy(rel):
    """Copies an asset (and for a .vmat its textures) into godot/assets, returns its res:// path."""
    src = os.path.join(SRC, rel)
    if not os.path.exists(src):
        return None
    dst = os.path.join(DST, "assets", rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if not os.path.exists(dst) or os.path.getmtime(dst) < os.path.getmtime(src):
        shutil.copy2(src, dst)
    return "res://assets/" + rel.replace("\\", "/")


def material(rel):
    """Reads a .vmat: tint, metalness, texture scale and textures (copied over)."""
    if not rel:
        return None
    src = os.path.join(SRC, rel)
    if not os.path.exists(src):
        return {"name": os.path.basename(rel)}
    text = open(src, encoding="utf-8").read()

    def get(key):
        m = re.search(r'"' + key + r'"\s+"([^"]+)"', text)
        return m.group(1) if m else None

    out = {"name": os.path.splitext(os.path.basename(rel))[0]}
    shader = get("shader") or ""
    if "triplanar_nature" in shader:
        # The nature shader: every parameter and texture, for godot/assets/shaders/nature.gdshader.
        nature = {}
        for key, name in (("BaseColor", "base_color"), ("BaseNormal", "base_normal"), ("BaseRough", "base_rough"),
                          ("TopColor", "top_color"), ("TopNormal", "top_normal")):
            t = get(key)
            if t:
                res = copy(t)
                if res:
                    nature[name] = res
        for key, name in (("g_flBaseScale", "base_scale"), ("g_flNormalStrength", "normal_strength"), ("g_flTopScale", "top_scale"),
                          ("g_flTopAmount", "top_amount"), ("g_flTopSharpness", "top_sharpness"), ("g_flVariationAmount", "variation_amount"),
                          ("g_flVariationScale", "variation_scale"), ("g_flVariationContrast", "variation_contrast"),
                          ("g_flBottomDarken", "bottom_darken"), ("g_flTopLight", "top_light"), ("g_flTopRoughness", "top_roughness")):
            v = get(key)
            if v:
                nature[name] = float(v)
        for key, name in (("g_vVariationColor", "variation_color"), ("g_vTopLightColor", "top_light_color")):
            v = get(key)
            if v:
                nature[name] = [float(x) for x in v.strip("[]").split()][:3]
        out["nature"] = nature
        return out
    if shader and "complex" not in shader:
        out["shader"] = os.path.splitext(os.path.basename(shader))[0]
    tint = get("g_vColorTint")
    if tint:
        out["tint"] = [float(x) for x in tint.strip("[]").split()][:3]
    metal = get("g_flMetalness")
    if metal:
        out["metal"] = float(metal)
    scale = get("g_vTexCoordScale")
    if scale:
        out["uv"] = float(scale.strip("[]").split()[0])
    for key, name in (("TextureColor", "albedo"), ("TextureNormal", "normal"), ("TextureRoughness", "rough")):
        t = get(key)
        if t and "smooth_" not in t:
            res = copy(t)
            if res:
                out[name] = res
    return out


def vmdl(rel):
    """The FBX and default material behind a .vmdl."""
    src = os.path.join(SRC, rel)
    if not os.path.exists(src):
        return None, None
    text = open(src, encoding="utf-8").read()
    fbx = re.search(r'filename = "([^"]+\.fbx)"', text)
    mat = re.search(r'global_default_material = "([^"]*)"', text)
    fbx_res = copy(fbx.group(1)) if fbx else None
    return fbx_res, (mat.group(1) if mat and mat.group(1) else None)


def convert(name):
    scene = json.load(open(os.path.join(SRC, "scenes", name + ".scene"), encoding="utf-8"))
    objects = []
    settings = {}

    def walk(go, parent_path):
        if not go.get("Enabled", True):
            return
        path = parent_path + "/" + go.get("Name", "")
        entry = {"name": go.get("Name", ""), "path": path, "parent": parent_path, "pos": pos(go.get("Position", "0,0,0")),
                 "rot": quat(go.get("Rotation", "0,0,0,1")), "scale": scl(go.get("Scale", "1,1,1")), "children": []}
        for c in go.get("Components", []):
            t = c["__type"].split(".")[-1]
            if t == "ModelRenderer":
                model = path_of(c.get("Model"))
                entry["tint"] = color(c.get("Tint"))
                override = path_of(c.get("MaterialOverride"))
                if model == "models/dev/box.vmdl":
                    entry["box"] = True
                elif model == "models/dev/sphere.vmdl":
                    entry["sphere"] = True
                elif model:
                    fbx, default_mat = vmdl(model)
                    entry["model"] = fbx
                    entry["material"] = material(default_mat)
                if override:
                    entry["material"] = material(override)
                entry["shadows"] = c.get("RenderType", "On") != "Off"
            elif t == "BoxCollider":
                entry["collider"] = {"size": scl(c.get("Scale", "50,50,50")), "center": pos(c.get("Center", "0,0,0"))}
            elif t == "Sway":
                entry["sway"] = {"amount": c.get("Amount", 6), "period": c.get("Period", 5)}
            elif t == "ZombieSpawnPoint":
                entry["spawn_point"] = True
            elif t == "DirectionalLight":
                settings["sun"] = {"rot": entry["rot"], "color": color(c.get("LightColor")), "shadows": c.get("Shadows", True)}
            elif t == "AmbientLight":
                settings["ambient"] = color(c.get("Color"))
            elif t == "GradientFog":
                settings["fog"] = {"color": color(c.get("Color")), "start": c.get("StartDistance", 0) * IN, "end": c.get("EndDistance", 4000) * IN,
                                   "falloff": c.get("FalloffExponent", 1), "height": c.get("Height", 0) * IN}
            elif t == "GlobalStylize":
                ink = [float(v) for v in str(c.get("InkColor", "0.05,0.05,0.08,1")).split(",")]
                settings["stylize"] = {"style": c.get("Style", "Flat"), "strength": c.get("Strength", 1), "outline": c.get("Outline", 0.9),
                                       "outline_width": c.get("OutlineWidth", 1.6), "ink": ink[:3], "bands": c.get("Bands", 4), "pixel_size": c.get("PixelSize", 3)}
            elif t == "SkyBox2D":
                settings["sky_tint"] = color(c.get("Tint"))
            elif t == "DockHouse":
                entry["dock_house"] = {k: v for k, v in c.items() if not k.startswith("__") and k not in ("Flags",) and not k.startswith("On")}
            elif t == "BuyStation":
                entry["buy_station"] = {k: v for k, v in c.items() if not k.startswith("__") and k != "Flags" and not k.startswith("On") and not isinstance(v, dict)}
            elif t == "TextRenderer":
                scope = c.get("TextScope") or {}
                entry["text"] = {"text": scope.get("Text", c.get("Text", "")), "color": color(scope.get("TextColor") or c.get("Color")),
                                 "scale": c.get("Scale", 1), "size": scope.get("FontSize", 32), "billboard": c.get("Billboard", "None") != "None"}
            elif t == "PointLight":
                entry["point_light"] = {"color": color(c.get("LightColor")), "radius": c.get("Radius", 400) * IN, "shadows": c.get("Shadows", False),
                                        "attenuation": c.get("Attenuation", 1)}
            elif t == "WaveDirector":
                # Every plain setting, applied by name in Level.cs.
                entry["wave_director"] = {k: v for k, v in c.items() if not k.startswith("__") and not k.startswith("On") and k != "Flags"
                                          and isinstance(v, (int, float, bool, str))}
            elif t == "ProceduralMap":
                entry["procedural_map"] = {k: v for k, v in c.items() if not k.startswith("__") and not k.startswith("On") and k != "Flags"}
            elif t == "ParticleEffect" and "Snow" in go.get("Name", ""):
                entry["marine_snow"] = True
        if go.get("Name") == "Player Template":
            settings["player_spawn"] = entry["pos"]
            return
        objects.append(entry)
        for child in go.get("Children", []):
            walk(child, path)

    for go in scene["GameObjects"]:
        walk(go, "")
    # Keep every object (containers too) so children keep their parents' transforms.
    out = {"name": name, "settings": settings, "objects": objects}
    os.makedirs(os.path.join(DST, "levels"), exist_ok=True)
    json.dump(out, open(os.path.join(DST, "levels", name + ".json"), "w"), indent=1)
    print(f"{name}: {len(out['objects'])} objects, settings {list(settings.keys())}")


if __name__ == "__main__":
    for n in sys.argv[1:] or ["water_level"]:
        convert(n)
