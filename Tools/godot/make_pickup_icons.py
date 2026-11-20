"""
Icons for the pickups creatures drop (godot/assets/textures/pickups/<name>.png, 128x128): a rounded badge in the
pickup's colour with a white pictogram. Rerun after changing one:  python Tools/godot/make_pickup_icons.py
Needs Pillow.
"""
import math
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "godot", "assets", "textures", "pickups")
S = 4          # supersampling
N = 128 * S
W = (255, 255, 255, 255)


def badge(colour):
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    dark = tuple(int(c * 0.45) for c in colour) + (255,)
    d.rounded_rectangle((4 * S, 4 * S, N - 4 * S, N - 4 * S), radius=26 * S, fill=dark)
    d.rounded_rectangle((10 * S, 10 * S, N - 10 * S, N - 10 * S), radius=21 * S, fill=tuple(colour) + (255,))
    return img, d


def p(x, y):
    return (x * S, y * S)


def poly(d, pts, fill=W):
    d.polygon([p(x, y) for x, y in pts], fill=fill)


def line(d, pts, width=7, fill=W):
    d.line([p(x, y) for x, y in pts], fill=fill, width=width * S, joint="curve")


def circle(d, x, y, r, fill=W, outline=None, width=0):
    d.ellipse((p(x - r, y - r), p(x + r, y + r)), fill=fill, outline=outline, width=width * S)


def bullet(d, x, y, h=44, w=16, fill=W):
    d.rounded_rectangle((p(x - w / 2, y - h / 2 + w / 2), p(x + w / 2, y + h / 2)), radius=3 * S, fill=fill)
    d.pieslice((p(x - w / 2, y - h / 2), p(x + w / 2, y - h / 2 + w)), 180, 360, fill=fill)


def reload_icon():
    img, d = badge((70, 150, 255))
    d.arc((p(30, 30), p(98, 98)), 40, 320, fill=W, width=10 * S)
    poly(d, [(88, 22), (106, 46), (78, 50)])
    bullet(d, 64, 66, 34, 13)
    return img


def firerate_icon():
    img, d = badge((255, 140, 40))
    for i, x in enumerate((42, 64, 86)):
        bullet(d, x, 60 + i * 0, 46, 15)
    for y in (96, 106):
        line(d, [(34, y), (94, y)], 4)
    return img


def explosive_icon():
    img, d = badge((230, 60, 50))
    pts = []
    for i in range(16):
        a = i / 16 * math.tau
        r = 44 if i % 2 == 0 else 24
        pts.append((64 + math.cos(a) * r, 62 + math.sin(a) * r))
    poly(d, pts)
    d.rounded_rectangle((p(52, 46), p(76, 86)), radius=4 * S, fill=(230, 60, 50, 255))
    for y in (56, 66, 76):
        line(d, [(56, y), (72, y)], 3)
    return img


def electric_icon():
    img, d = badge((150, 80, 255))
    poly(d, [(72, 18), (38, 70), (60, 70), (50, 110), (92, 54), (68, 54)], fill=(255, 235, 80, 255))
    return img


def chest_icon():
    img, d = badge((150, 95, 45))
    gold = (255, 205, 60, 255)
    d.rounded_rectangle((p(26, 58), p(102, 100)), radius=5 * S, fill=W)
    d.pieslice((p(26, 30), p(102, 86)), 180, 360, fill=W)
    line(d, [(26, 60), (102, 60)], 5, fill=(150, 95, 45, 255))
    d.rounded_rectangle((p(56, 52), p(72, 72)), radius=3 * S, fill=gold)
    for x, y in ((40, 26), (64, 20), (88, 26)):
        circle(d, x, y, 7, fill=gold)
    return img


def shield_icon():
    img, d = badge((40, 190, 255))
    poly(d, [(64, 22), (100, 34), (100, 64), (64, 108), (28, 64), (28, 34)])
    poly(d, [(64, 34), (89, 42), (89, 62), (64, 93), (39, 62), (39, 42)], fill=(40, 190, 255, 255))
    line(d, [(64, 46), (64, 78)], 7)
    line(d, [(48, 62), (80, 62)], 7)
    return img


def move_icon():
    img, d = badge((60, 200, 110))
    for x in (30, 56):
        poly(d, [(x, 34), (x + 30, 64), (x, 94), (x + 14, 94), (x + 44, 64), (x + 14, 34)])
    return img


def blast_icon():
    img, d = badge((40, 90, 230))
    for r, w in ((44, 6), (30, 6)):
        circle(d, 64, 64, r, fill=None, outline=W, width=w)
    circle(d, 64, 64, 14)
    for i in range(8):
        a = i / 8 * math.tau + 0.39
        line(d, [(64 + math.cos(a) * 46, 64 + math.sin(a) * 46), (64 + math.cos(a) * 56, 64 + math.sin(a) * 56)], 5)
    return img


def pierce_icon():
    img, d = badge((30, 190, 185))
    # A trident run through three enemies.
    for x in (32, 52, 72):
        circle(d, x, 64, 11, fill=None, outline=W, width=5)
    line(d, [(12, 64), (90, 64)], 7)
    line(d, [(90, 44), (90, 84)], 7)
    for y, tip in ((44, 108), (64, 116), (84, 108)):
        line(d, [(90, y), (tip - 8, y)], 6)
        poly(d, [(tip - 10, y - 7), (tip, y), (tip - 10, y + 7)])
    return img


def reach_icon():
    img, d = badge((160, 100, 255))
    # A trident with range rings spreading out ahead of its prongs.
    for r in (26, 40, 54):
        d.arc((p(64 - r, 50 - r), p(64 + r, 50 + r)), 215, 325, fill=W, width=5 * S)
    line(d, [(64, 112), (64, 60)], 7)
    line(d, [(46, 66), (82, 66)], 7)
    for x in (46, 64, 82):
        line(d, [(x, 66), (x, 48)], 6)
        poly(d, [(x - 7, 50), (x, 38), (x + 7, 50)])
    return img


def trident(d, x, top, bottom, half, prong, width, tip):
    line(d, [(x, bottom), (x, top + prong)], width)
    line(d, [(x - half, top + prong), (x + half, top + prong)], width)
    for px in (x - half, x, x + half):
        line(d, [(px, top + prong), (px, top + tip)], max(2, width - 1))
        poly(d, [(px - tip * 0.6, top + tip), (px, top), (px + tip * 0.6, top + tip)])


def swarm_icon():
    img, d = badge((240, 190, 40))
    # The big trident with three small ones flying off it.
    trident(d, 48, 22, 110, 16, 22, 7, 12)
    for x, y in ((84, 26), (100, 54), (84, 82)):
        trident(d, x, y, y + 30, 7, 10, 4, 7)
    return img


ICONS = {
    "reload": reload_icon, "firerate": firerate_icon, "explosive": explosive_icon, "electric": electric_icon,
    "chest": chest_icon, "shield": shield_icon, "move": move_icon, "blast": blast_icon,
    "pierce": pierce_icon, "reach": reach_icon, "swarm": swarm_icon,
}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for name, make in ICONS.items():
        make().resize((128, 128), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))
    sheet = Image.new("RGBA", (128 * len(ICONS) + (len(ICONS) + 1) * 12, 152), (16, 24, 36, 255))
    for i, name in enumerate(ICONS):
        sheet.paste(Image.open(os.path.join(OUT, name + ".png")), (12 + i * 140, 12), Image.open(os.path.join(OUT, name + ".png")))
    sheet.save(os.path.join(os.path.dirname(__file__), "pickup_icons_sheet.png"))
    print("wrote", len(ICONS), "icons")
