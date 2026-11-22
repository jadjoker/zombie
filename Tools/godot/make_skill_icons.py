"""
Icons for the skill trees (godot/assets/textures/pickups/<name>.png, 128x128), in the same badge style as the old
pickup icons (make_pickup_icons.py, whose helpers this uses; the skills that were pickups keep those icons).
Rerun after changing one:  python Tools/godot/make_skill_icons.py      Needs Pillow.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from make_pickup_icons import OUT, W, Image, badge, bullet, circle, line, p, poly, trident  # noqa: E402

DARK = (20, 24, 40, 255)


def star(d, cx, cy, r_out, r_in, n=5, fill=W, turn=-math.pi / 2):
    pts = []
    for i in range(n * 2):
        a = turn + i * math.pi / n
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    poly(d, pts, fill)


def arrow(d, x0, y0, x1, y1, width=7, head=12, fill=W):
    line(d, [(x0, y0), (x1, y1)], width, fill)
    a = math.atan2(y1 - y0, x1 - x0)
    tip = (x1 + math.cos(a) * head * 0.6, y1 + math.sin(a) * head * 0.6)
    left = (x1 + math.cos(a + 2.4) * head, y1 + math.sin(a + 2.4) * head)
    right = (x1 + math.cos(a - 2.4) * head, y1 + math.sin(a - 2.4) * head)
    poly(d, [tip, left, right], fill)


def plus(d, x, y, s=12, w=6):
    line(d, [(x, y - s), (x, y + s)], w)
    line(d, [(x - s, y), (x + s, y)], w)


def crosshair(d, x, y, r, w=5, fill=W):
    circle(d, x, y, r, fill=None, outline=fill, width=w)
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        line(d, [(x + dx * (r - 8), y + dy * (r - 8)), (x + dx * (r + 10), y + dy * (r + 10))], w, fill)


def spiral(d, cx, cy, r, turns=2.5, width=6, fill=W, start=0.0):
    pts = []
    for i in range(120):
        t = i / 119
        a = start + t * turns * math.tau
        rr = r * t
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    line(d, pts, width, fill)


def shell(d, x, y, fill=W):
    d.rounded_rectangle((p(x - 8, y - 18), p(x + 8, y + 18)), radius=3 * S_, fill=fill)
    d.rectangle((p(x - 8, y + 8), p(x + 8, y + 18)), fill=(230, 180, 60, 255))


S_ = 4


def deepmag():
    img, d = badge((150, 170, 200))
    d.rounded_rectangle((p(44, 20), p(84, 108)), radius=6 * S_, fill=W)
    for y in (34, 50, 66, 82):
        bullet(d, 64, y + 6, 14, 22, fill=(150, 170, 200, 255))
    plus(d, 100, 28, 10, 5)
    return img


def headhunter():
    img, d = badge((255, 205, 60))
    circle(d, 64, 60, 26)
    circle(d, 54, 56, 6, fill=(255, 205, 60, 255))
    circle(d, 74, 56, 6, fill=(255, 205, 60, 255))
    crosshair(d, 64, 60, 40, 5)
    return img


def overclock():
    img, d = badge((255, 140, 40))
    circle(d, 64, 66, 40, fill=None, outline=W, width=8)
    d.arc((p(24, 26), p(104, 106)), 200, 340, fill=(255, 235, 80, 255), width=10 * S_)
    line(d, [(64, 66), (92, 42)], 7)
    circle(d, 64, 66, 8)
    poly(d, [(84, 14), (66, 34), (78, 34), (70, 50), (92, 28), (80, 28)], fill=(255, 235, 80, 255))
    return img


def wideload():
    img, d = badge((255, 140, 40))
    for i in range(7):
        a = math.radians(-60 + i * 20)
        x, y = 30 + math.cos(a) * 64, 64 + math.sin(a) * 64
        line(d, [(30, 64), (x, y)], 4)
        circle(d, x, y, 7)
    circle(d, 26, 64, 12)
    return img


def pointblank():
    img, d = badge((230, 60, 50))
    poly(d, [(18, 50), (62, 50), (62, 78), (18, 78)])
    star(d, 88, 64, 30, 12, 8)
    circle(d, 88, 64, 9, fill=(230, 60, 50, 255))
    return img


def shellshuffle():
    img, d = badge((70, 150, 255))
    for i, x in enumerate((38, 58, 78)):
        shell(d, x, 66 - i * 4)
    d.arc((p(22, 18), p(106, 110)), 200, 330, fill=W, width=7 * S_)
    poly(d, [(98, 26), (110, 46), (90, 46)])
    return img


def kickback():
    img, d = badge((40, 190, 255))
    circle(d, 80, 64, 20)
    for y in (40, 64, 88):
        arrow(d, 30, y, 60 if y == 64 else 52, y, 6, 12)
    arrow(d, 96, 64, 116, 64, 6, 10)
    return img


def dragon():
    img, d = badge((255, 120, 30))
    poly(d, [(64, 112), (36, 88), (34, 60), (48, 70), (46, 40), (62, 56), (66, 18), (80, 48), (88, 36), (94, 66), (92, 90)], fill=(255, 220, 80, 255))
    poly(d, [(64, 104), (50, 88), (52, 72), (60, 80), (64, 62), (72, 78), (80, 70), (80, 92)], fill=W)
    return img


def limbripper():
    img, d = badge((255, 100, 130))
    line(d, [(20, 92), (56, 56)], 14)
    line(d, [(72, 40), (106, 18)], 14)
    for x, y in ((60, 50), (66, 46), (64, 56)):
        circle(d, x, y, 5)
    poly(d, [(58, 60), (54, 48), (66, 52), (62, 40), (74, 46)], fill=W)
    return img


def fullchoke():
    img, d = badge((255, 200, 60))
    d.rounded_rectangle((p(14, 52), p(70, 76)), radius=6 * S_, fill=W)
    poly(d, [(70, 54), (90, 58), (90, 70), (70, 74)])
    line(d, [(94, 64), (118, 64)], 8)
    for x in (100, 112):
        circle(d, x, 64, 9, fill=None, outline=W, width=4)
    return img


def plating():
    img, d = badge((150, 170, 200))
    poly(d, [(64, 18), (104, 34), (100, 78), (64, 110), (28, 78), (24, 34)])
    for x, y in ((44, 42), (84, 42), (44, 78), (84, 78), (64, 60)):
        circle(d, x, y, 5, fill=(150, 170, 200, 255))
    line(d, [(30, 60), (98, 60)], 4, fill=(150, 170, 200, 255))
    return img


def heavyhitter():
    img, d = badge((230, 60, 50))
    d.rounded_rectangle((p(36, 34), p(92, 92)), radius=14 * S_, fill=W)
    for y in (48, 62, 76):
        line(d, [(42, y), (86, y)], 3, fill=(230, 60, 50, 255))
    for i in range(3):
        line(d, [(18, 40 + i * 24), (30, 44 + i * 20)], 5)
    star(d, 104, 30, 14, 6, 6)
    return img


def quickcharge():
    img, d = badge((40, 190, 255))
    d.rounded_rectangle((p(28, 36), p(96, 96)), radius=8 * S_, fill=None, outline=W, width=6 * S_)
    d.rectangle((p(96, 54), p(104, 78)), fill=W)
    poly(d, [(68, 40), (44, 70), (60, 70), (54, 92), (80, 60), (64, 60)], fill=(255, 235, 80, 255))
    return img


def lumberjack():
    img, d = badge((60, 200, 110))
    line(d, [(34, 104), (80, 30)], 9, fill=(150, 95, 45, 255))
    poly(d, [(70, 26), (104, 22), (110, 54), (86, 48)])
    plus(d, 32, 34, 12, 6)
    return img


def unstoppable():
    img, d = badge((230, 60, 50))
    poly(d, [(18, 44), (70, 44), (70, 26), (110, 64), (70, 102), (70, 84), (18, 84)])
    for x in (30, 46):
        line(d, [(x, 52), (x - 8, 76)], 4, fill=(230, 60, 50, 255))
    return img


def quickdraw():
    img, d = badge((30, 190, 185))
    trident(d, 52, 20, 110, 16, 22, 7, 12)
    circle(d, 94, 74, 20, fill=None, outline=W, width=5)
    line(d, [(94, 74), (94, 60)], 5)
    line(d, [(94, 74), (104, 80)], 5)
    return img


def riptide():
    img, d = badge((40, 190, 255))
    for i, y in enumerate((40, 64, 88)):
        pts = [(18 + t * 4, y + math.sin(t * 0.6 + i) * 8) for t in range(24)]
        line(d, pts, 6)
    poly(d, [(108, 64), (92, 52), (92, 76)])
    return img


def tidalburst():
    img, d = badge((40, 190, 255))
    d.chord((p(18, 50), p(110, 130)), 180, 360, fill=W)
    for x in (38, 64, 90):
        circle(d, x, 40, 9)
    circle(d, 52, 24, 6)
    circle(d, 78, 22, 5)
    return img


def fork():
    img, d = badge((255, 200, 60))
    for a, ox in ((-0.35, -6), (0, 0), (0.35, 6)):
        cx, cy = 64, 112
        tx, ty = cx + math.sin(a) * 90, cy - math.cos(a) * 90
        line(d, [(cx, cy), (tx, ty)], 6)
        poly(d, [(tx - 8, ty + 10), (tx, ty - 4), (tx + 8, ty + 10)])
    trident(d, 64, 18, 60, 10, 12, 5, 8)
    return img


def longspear():
    img, d = badge((30, 190, 185))
    line(d, [(14, 100), (96, 30)], 7)
    poly(d, [(92, 22), (116, 12), (106, 36)])
    arrow(d, 40, 110, 110, 110, 4, 8)
    return img


def barbed():
    img, d = badge((230, 60, 50))
    line(d, [(18, 104), (84, 38)], 7)
    poly(d, [(80, 30), (108, 20), (98, 48)])
    for t in (0.45, 0.65):
        x, y = 18 + 66 * t, 104 - 66 * t
        poly(d, [(x, y), (x - 14, y - 4), (x - 4, y - 14)])
    for x, y in ((92, 70), (100, 88), (84, 92)):
        circle(d, x, y, 6, fill=(150, 10, 20, 255))
    return img


def bash():
    img, d = badge((40, 190, 255))
    poly(d, [(52, 22), (84, 32), (84, 64), (52, 104), (24, 64), (24, 32)])
    for i in range(3):
        d.arc((p(70 + i * 10, 34), p(110 + i * 6, 94)), 300, 60, fill=W, width=4 * S_)
    return img


def riposte():
    img, d = badge((255, 200, 60))
    line(d, [(24, 104), (104, 24)], 7)
    line(d, [(24, 24), (104, 104)], 7)
    poly(d, [(96, 16), (112, 14), (110, 30)])
    poly(d, [(96, 112), (112, 114), (110, 98)])
    star(d, 64, 64, 14, 6, 6, fill=(255, 235, 80, 255))
    return img


def mirror():
    img, d = badge((150, 80, 255))
    poly(d, [(64, 20), (98, 32), (98, 66), (64, 108), (30, 66), (30, 32)])
    line(d, [(10, 30), (54, 58)], 5, fill=(255, 235, 80, 255))
    arrow(d, 54, 58, 96, 92, 5, 12, fill=(255, 235, 80, 255))
    return img


def spartan():
    img, d = badge((230, 60, 50))
    d.pieslice((p(26, 26), p(102, 102)), 180, 360, fill=W)
    d.rectangle((p(26, 62), p(102, 92)), fill=W)
    d.rectangle((p(52, 62), p(76, 100)), fill=(230, 60, 50, 255))
    d.rectangle((p(36, 56), p(92, 66)), fill=(230, 60, 50, 255))
    poly(d, [(48, 30), (64, 6), (80, 30)], fill=(255, 205, 60, 255))
    return img


def sealegs():
    img, d = badge((60, 200, 110))
    for x in (40, 72):
        d.rounded_rectangle((p(x, 30), p(x + 16, 86)), radius=6 * S_, fill=W)
        d.rounded_rectangle((p(x - 4, 82), p(x + 26, 98)), radius=6 * S_, fill=W)
    for i in range(3):
        line(d, [(16, 44 + i * 18), (30, 44 + i * 18)], 4)
    return img


def longlunge():
    img, d = badge((30, 190, 185))
    for i, x in enumerate((24, 44, 64)):
        circle(d, x, 64, 8 + i * 3, fill=None if i < 2 else W, outline=W, width=3)
    arrow(d, 72, 64, 106, 64, 8, 16)
    return img


def violetedge():
    img, d = badge((150, 80, 255))
    trident(d, 64, 18, 110, 18, 24, 7, 14)
    for a in range(6):
        ang = a / 6 * math.tau
        line(d, [(64 + math.cos(ang) * 22, 30 + math.sin(ang) * 14), (64 + math.cos(ang) * 32, 30 + math.sin(ang) * 22)], 3, fill=(220, 180, 255, 255))
    return img


def undertow():
    img, d = badge((40, 190, 255))
    arrow(d, 20, 64, 92, 64, 8, 14)
    for x, y in ((40, 36), (56, 92), (30, 94)):
        circle(d, x, y, 8, fill=None, outline=W, width=4)
        arrow(d, x + 10, y, x + 26, y + (64 - y) * 0.4, 3, 7)
    return img


def plecos():
    img, d = badge((60, 200, 110))
    d.ellipse((p(18, 54), p(82, 90)), fill=W)
    poly(d, [(80, 72), (104, 56), (104, 88)])
    circle(d, 32, 66, 5, fill=DARK)
    for x, y, r in ((86, 30, 10), (104, 20, 7)):
        circle(d, x - r * 0.5, y, r * 0.6, fill=(255, 100, 130, 255))
        circle(d, x + r * 0.5, y, r * 0.6, fill=(255, 100, 130, 255))
        poly(d, [(x - r, y + 2), (x, y + r * 1.3), (x + r, y + 2)], fill=(255, 100, 130, 255))
    return img


def kraken():
    img, d = badge((150, 80, 255))
    spiral(d, 64, 64, 46, 2.4, 7)
    circle(d, 64, 64, 8)
    return img


def belly():
    img, d = badge((140, 255, 70))
    d.rounded_rectangle((p(36, 22), p(92, 108)), radius=16 * S_, fill=W)
    d.rounded_rectangle((p(42, 56), p(86, 102)), radius=12 * S_, fill=(90, 190, 40, 255))
    plus(d, 64, 38, 9, 5)
    return img


def suction():
    img, d = badge((40, 190, 255))
    poly(d, [(16, 30), (66, 56), (66, 72), (16, 98)])
    d.rounded_rectangle((p(66, 52), p(110, 76)), radius=6 * S_, fill=W)
    for x, y in ((28, 50), (40, 76), (24, 66)):
        circle(d, x, y, 4, fill=(40, 190, 255, 255))
    return img


def gourmet():
    img, d = badge((255, 100, 130))
    d.chord((p(20, 46), p(108, 120)), 180, 360, fill=W)
    d.rectangle((p(20, 80), p(108, 88)), fill=W)
    circle(d, 64, 40, 7)
    for x in (44, 64, 84):
        circle(d, x, 70, 6, fill=(255, 100, 130, 255))
    return img


def shredder():
    img, d = badge((230, 60, 50))
    circle(d, 64, 64, 34)
    for i in range(10):
        a = i / 10 * math.tau
        poly(d, [(64 + math.cos(a) * 30, 64 + math.sin(a) * 30), (64 + math.cos(a + 0.2) * 48, 64 + math.sin(a + 0.2) * 48), (64 + math.cos(a + 0.45) * 30, 64 + math.sin(a + 0.45) * 30)])
    circle(d, 64, 64, 12, fill=(230, 60, 50, 255))
    return img


def cluster():
    img, d = badge((255, 200, 60))
    circle(d, 64, 64, 18)
    for i in range(7):
        a = i / 7 * math.tau
        x, y = 64 + math.cos(a) * 40, 64 + math.sin(a) * 40
        line(d, [(64 + math.cos(a) * 20, 64 + math.sin(a) * 20), (x, y)], 4)
        circle(d, x, y, 8)
    return img


def blackhole():
    img, d = badge((90, 50, 160))
    d.ellipse((p(12, 50), p(116, 82)), fill=(255, 170, 70, 255))
    d.ellipse((p(26, 56), p(102, 76)), fill=(255, 235, 170, 255))
    circle(d, 64, 64, 24, fill=(190, 140, 255, 255))
    circle(d, 64, 64, 20, fill=(0, 0, 0, 255))
    d.chord((p(12, 50), p(116, 82)), 0, 180, fill=(255, 170, 70, 255))
    d.chord((p(26, 56), p(102, 76)), 0, 180, fill=(255, 235, 170, 255))
    d.chord((p(44, 44), p(84, 84)), 0, 180, fill=(0, 0, 0, 255))
    return img


def bubble(d, x, y, r, w=4):
    circle(d, x, y, r, fill=None, outline=W, width=w)
    d.arc((p(x - r * 0.6, y - r * 0.6), p(x + r * 0.2, y + r * 0.2)), 190, 260, fill=W, width=w * S_)


def lasting():
    img, d = badge((40, 190, 255))
    bubble(d, 56, 64, 36)
    circle(d, 92, 92, 18, fill=None, outline=W, width=5)
    line(d, [(92, 92), (92, 80)], 4)
    line(d, [(92, 92), (100, 96)], 4)
    return img


def bouncy():
    img, d = badge((255, 100, 130))
    line(d, [(14, 104), (114, 104)], 5)
    pts = [(18 + t, 104 - abs(math.sin(t / 30 * math.pi)) * (70 - t * 0.4)) for t in range(0, 92, 2)]
    line(d, pts, 4)
    bubble(d, 106, 58, 12)
    return img


def biggulp():
    img, d = badge((30, 190, 185))
    bubble(d, 64, 64, 44, 5)
    for x, y in ((50, 54), (76, 58), (62, 80)):
        circle(d, x, y, 9)
    return img


def highlift():
    img, d = badge((40, 190, 255))
    bubble(d, 64, 46, 26)
    arrow(d, 64, 112, 64, 80, 7, 12)
    line(d, [(24, 112), (104, 112)], 4)
    return img


def chain():
    img, d = badge((255, 140, 40))
    for i, (x, y) in enumerate(((30, 84), (60, 54), (92, 30))):
        bubble(d, x, y, 14 + i * 3)
    for (x0, y0), (x1, y1) in (((42, 72), (48, 66)), ((74, 44), (78, 40))):
        line(d, [(x0, y0), (x1, y1)], 4)
    star(d, 98, 92, 16, 7, 8)
    return img


def prison():
    img, d = badge((255, 100, 130))
    bubble(d, 64, 64, 42, 5)
    for x in (48, 64, 80):
        line(d, [(x, 32), (x, 96)], 5)
    return img


def jets():
    img, d = badge((40, 190, 255))
    d.rounded_rectangle((p(44, 18), p(84, 72)), radius=10 * S_, fill=W)
    poly(d, [(46, 72), (82, 72), (72, 90), (56, 90)])
    poly(d, [(56, 92), (72, 92), (64, 116)], fill=(255, 235, 80, 255))
    line(d, [(92, 92), (112, 92)], 5)
    return img


def afterburner():
    img, d = badge((255, 140, 40))
    d.rounded_rectangle((p(66, 44), p(112, 84)), radius=10 * S_, fill=W)
    poly(d, [(66, 46), (14, 64), (66, 82)], fill=(255, 220, 80, 255))
    poly(d, [(66, 54), (34, 64), (66, 74)], fill=W)
    return img


def toxic():
    img, d = badge((140, 255, 70))
    for i in range(3):
        a = math.radians(-90 + i * 120)
        cx, cy = 64 + math.cos(a) * 24, 64 + math.sin(a) * 24
        circle(d, cx, cy, 20)
    circle(d, 64, 64, 12, fill=(90, 190, 40, 255))
    circle(d, 64, 64, 6)
    return img


def slipstream():
    img, d = badge((30, 190, 185))
    for i, y in enumerate((38, 60, 82)):
        d.arc((p(14, y - 16), p(94, y + 16)), 180, 360, fill=W, width=6 * S_)
    poly(d, [(110, 60), (90, 46), (90, 74)])
    return img


def phase():
    img, d = badge((150, 80, 255))
    for i, x in enumerate((36, 58, 82)):
        a = 90 + i * 60
        col = (255, 255, 255, a)
        circle(d, x, 40, 12, fill=col)
        d.rounded_rectangle((p(x - 14, 56), p(x + 14, 100)), radius=10 * S_, fill=col)
    return img


ICONS = {
    "deepmag": deepmag, "headhunter": headhunter, "overclock": overclock, "wideload": wideload, "pointblank": pointblank,
    "shellshuffle": shellshuffle, "kickback": kickback, "dragon": dragon, "limbripper": limbripper, "fullchoke": fullchoke,
    "plating": plating, "heavyhitter": heavyhitter, "quickcharge": quickcharge, "lumberjack": lumberjack, "unstoppable": unstoppable,
    "quickdraw": quickdraw, "riptide": riptide, "tidalburst": tidalburst, "fork": fork, "longspear": longspear,
    "barbed": barbed, "bash": bash, "riposte": riposte, "mirror": mirror, "spartan": spartan,
    "sealegs": sealegs, "longlunge": longlunge, "violetedge": violetedge, "undertow": undertow, "plecos": plecos,
    "kraken": kraken, "belly": belly, "suction": suction, "gourmet": gourmet, "shredder": shredder,
    "cluster": cluster, "blackhole": blackhole, "lasting": lasting, "bouncy": bouncy, "biggulp": biggulp,
    "highlift": highlift, "chain": chain, "prison": prison, "jets": jets, "afterburner": afterburner,
    "toxic": toxic, "slipstream": slipstream, "phase": phase,
}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for name, make in ICONS.items():
        make().resize((128, 128), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))
    cols = 12
    rows = (len(ICONS) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * 140 + 12, rows * 140 + 12), (16, 24, 36, 255))
    for i, name in enumerate(ICONS):
        icon = Image.open(os.path.join(OUT, name + ".png"))
        sheet.paste(icon, (12 + (i % cols) * 140, 12 + (i // cols) * 140), icon)
    sheet.save(os.path.join(os.path.dirname(__file__), "skill_icons_sheet.png"))
    print("wrote", len(ICONS), "icons")
