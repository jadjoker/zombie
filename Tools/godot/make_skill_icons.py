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


# ---- Hook-Bot ----
ROPE = (230, 200, 140, 255)
NAVY = (20, 32, 60, 255)


def hook_shape(d, x, y, s=1.0, fill=W, w=7):
    # A fish hook: shank down, curl round, barbed point.
    line(d, [(x, y - 34 * s), (x, y + 14 * s)], w, fill)
    pts = []
    for i in range(30):
        a = math.pi * i / 29
        pts.append((x - 14 * s + math.cos(a) * 14 * s, y + 14 * s + math.sin(a) * 14 * s))
    line(d, pts, w, fill)
    poly(d, [(x - 28 * s, y + 14 * s), (x - 34 * s, y - 2 * s), (x - 20 * s, y + 6 * s)], fill)
    circle(d, x, y - 36 * s, 6 * s, fill=None, outline=fill, width=4)


def reel():
    img, d = badge((60, 170, 255))
    circle(d, 64, 64, 32, fill=ROPE)
    circle(d, 64, 64, 24, fill=(190, 150, 90, 255))
    for r in (18, 12):
        circle(d, 64, 64, r, fill=None, outline=ROPE, width=3)
    circle(d, 64, 64, 6, fill=NAVY)
    line(d, [(64, 64), (100, 40)], 6, NAVY)
    circle(d, 102, 38, 7, fill=W)
    return img


def towline():
    img, d = badge((255, 140, 40))
    line(d, [(18, 64), (86, 64)], 5, ROPE)
    circle(d, 96, 64, 16)
    for cx, cy in ((50, 40), (54, 90)):
        circle(d, cx, cy, 11, fill=(255, 220, 160, 255))
    arrow(d, 72, 40, 50, 22, 5, 10)
    arrow(d, 72, 88, 54, 108, 5, 10)
    return img


def gutting():
    img, d = badge((220, 60, 50))
    hook_shape(d, 70, 56, 1.0)
    for x, y in ((40, 100), (56, 108), (88, 104)):
        poly(d, [(x, y - 10), (x - 6, y + 2), (x + 6, y + 2)], (140, 10, 20, 255))
        circle(d, x, y + 3, 6, fill=(140, 10, 20, 255))
    return img


def trawler():
    img, d = badge((255, 200, 60))
    for i in range(6):
        x = 26 + i * 15
        line(d, [(x, 26), (x - 10 + i * 4, 104)], 3, NAVY)
    for j in range(5):
        y = 30 + j * 18
        line(d, [(22, y), (106, y + (4 if j % 2 else -4))], 3, NAVY)
    for cx, cy in ((50, 70), (80, 82), (66, 50)):
        circle(d, cx, cy, 9, fill=(255, 120, 60, 255))
    return img


def bulb():
    img, d = badge((255, 205, 60))
    line(d, [(40, 110), (40, 60), (60, 34), (80, 42)], 7, NAVY)
    circle(d, 84, 54, 22, fill=(160, 255, 120, 255))
    circle(d, 84, 54, 12, fill=W)
    for a in range(8):
        r = math.radians(a * 45)
        line(d, [(84 + math.cos(r) * 28, 54 + math.sin(r) * 28), (84 + math.cos(r) * 36, 54 + math.sin(r) * 36)], 4)
    return img


def pockets():
    img, d = badge((150, 170, 200))
    d.rounded_rectangle((p(28, 44), p(100, 108)), radius=12 * 4, fill=NAVY)
    d.rectangle((p(28, 44), p(100, 60)), fill=(60, 80, 120, 255))
    for x in (46, 64, 82):
        circle(d, x, 36, 9, fill=(160, 255, 120, 255))
    plus(d, 64, 84, 14, 7)
    return img


def bloom():
    img, d = badge((140, 255, 70))
    # An angler's jaw gaping up out of the sand, its lantern above.
    d.rectangle((p(14, 92), p(114, 112)), fill=(210, 180, 120, 255))
    poly(d, [(18, 96), (36, 46), (60, 92)], (20, 70, 95, 255))
    poly(d, [(110, 96), (92, 46), (68, 92)], (20, 70, 95, 255))
    for x in (30, 40, 50):
        poly(d, [(x - 4, 88), (x + 6, 60 + (x - 30)), (x + 4, 90)], W)
    for x in (98, 88, 78):
        poly(d, [(x + 4, 88), (x - 6, 60 + (98 - x)), (x - 4, 90)], W)
    line(d, [(64, 92), (64, 40), (74, 26)], 4, (20, 70, 95, 255))
    circle(d, 78, 22, 9, fill=(200, 255, 160, 255))
    return img


def seadog():
    img, d = badge((255, 200, 60))
    # A pirate hat with a heart on it.
    poly(d, [(16, 76), (34, 40), (64, 32), (94, 40), (112, 76), (64, 66)], NAVY)
    heart = [(64, 68), (46, 52), (46, 42), (55, 36), (64, 44), (73, 36), (82, 42), (82, 52)]
    poly(d, heart, (230, 60, 70, 255))
    d.rectangle((p(26, 80), p(102, 90)), fill=NAVY)
    return img


def leviathan():
    img, d = badge((90, 50, 160))
    hook_shape(d, 64, 54, 0.9, (220, 190, 255, 255), 8)
    for r in (40, 50):
        d.arc((p(64 - r, 92 - r * 0.4), p(64 + r, 92 + r * 0.4)), 0, 180, fill=W, width=4 * 4)
    return img


# ---- Coral-Bot ----
CORAL = (255, 115, 135, 255)
BONE = (245, 235, 215, 255)


def coral_branch(d, x, y, s=1.0, fill=CORAL, w=8):
    line(d, [(x, y), (x, y - 40 * s)], w, fill)
    line(d, [(x, y - 18 * s), (x - 20 * s, y - 38 * s)], w - 2, fill)
    line(d, [(x, y - 26 * s), (x + 18 * s, y - 48 * s)], w - 2, fill)
    for cx, cy in ((x, y - 42 * s), (x - 21 * s, y - 40 * s), (x + 19 * s, y - 50 * s)):
        circle(d, cx, cy, 5 * s + 2, fill=fill)


def spines():
    img, d = badge((230, 60, 50))
    for i, (x, y) in enumerate(((30, 90), (52, 70), (74, 50), (96, 30))):
        poly(d, [(x - 14, y + 14), (x + 10, y - 10), (x - 4, y + 18)], BONE if i % 2 else W)
    return img


def growth():
    img, d = badge((60, 200, 110))
    coral_branch(d, 64, 106, 1.3)
    arrow(d, 100, 100, 100, 40, 6, 12)
    return img


def clusters():
    img, d = badge((150, 170, 200))
    circle(d, 64, 70, 28, fill=(70, 80, 110, 255))
    for a in range(7):
        r = math.radians(a * 51)
        circle(d, 64 + math.cos(r) * 26, 70 + math.sin(r) * 26, 10, fill=CORAL)
    poly(d, [(40, 30), (88, 30), (80, 40), (48, 40)], W)
    return img


def ricochet():
    img, d = badge((40, 190, 255))
    line(d, [(18, 96), (62, 40), (110, 92)], 6)
    poly(d, [(110, 92), (94, 86), (104, 76)])
    circle(d, 62, 40, 9, fill=CORAL)
    return img


def bleach():
    img, d = badge((235, 230, 215))
    coral_branch(d, 64, 108, 1.4, (150, 145, 135, 255), 10)
    for x, y in ((30, 40), (100, 36), (94, 92)):
        poly(d, [(x, y - 8), (x + 8, y), (x, y + 8), (x - 8, y)], (120, 115, 108, 255))
    return img


def heal():
    img, d = badge((60, 200, 110))
    plus(d, 64, 64, 26, 14)
    return img


def spikedwall():
    img, d = badge((230, 60, 50))
    d.rounded_rectangle((p(18, 60), p(110, 104)), radius=10 * 4, fill=CORAL)
    for x in range(26, 108, 16):
        poly(d, [(x - 7, 62), (x, 30), (x + 7, 62)], BONE)
    return img


def anemone():
    img, d = badge((255, 105, 140))
    for i in range(9):
        a = math.radians(200 + i * 17.5)
        line(d, [(64, 92), (64 + math.cos(a) * 44, 92 + math.sin(a) * 44)], 6, (255, 200, 230, 255))
        circle(d, 64 + math.cos(a) * 46, 92 + math.sin(a) * 46, 5, fill=W)
    circle(d, 64, 96, 16, fill=(200, 50, 120, 255))
    return img


def livingreef():
    img, d = badge((150, 80, 255))
    pts = ((30, 96), (64, 40), (98, 92))
    for a, b in ((0, 1), (1, 2), (0, 2)):
        line(d, [pts[a], pts[b]], 5, (255, 140, 230, 255))
    for x, y in pts:
        circle(d, x, y, 13, fill=CORAL)
        circle(d, x, y, 6, fill=W)
    return img


def dig():
    img, d = badge((215, 175, 110))
    d.rectangle((p(14, 70), p(114, 114)), fill=(160, 120, 70, 255))
    for x, y in ((30, 82), (70, 96), (96, 80)):
        circle(d, x, y, 5, fill=(230, 200, 150, 255))
    arrow(d, 64, 22, 64, 92, 8, 16)
    return img


def upheaval():
    img, d = badge((255, 140, 40))
    d.rectangle((p(14, 92), p(114, 114)), fill=(160, 120, 70, 255))
    for x, y in ((40, 70), (64, 50), (88, 70)):
        poly(d, [(x - 12, y + 12), (x, y - 10), (x + 12, y + 12)], (230, 200, 150, 255))
    arrow(d, 64, 92, 64, 22, 7, 14)
    arrow(d, 50, 92, 22, 44, 6, 12)
    arrow(d, 78, 92, 106, 44, 6, 12)
    return img


def calcify():
    img, d = badge((235, 230, 215))
    poly(d, [(64, 18), (104, 34), (98, 80), (64, 110), (30, 80), (24, 34)], (150, 145, 135, 255))
    poly(d, [(64, 30), (92, 42), (88, 76), (64, 98), (40, 76), (36, 42)], BONE)
    line(d, [(50, 52), (64, 66), (58, 80)], 4, (150, 145, 135, 255))
    return img


def barrier():
    img, d = badge((255, 105, 140))
    for i in range(8):
        a = math.radians(i * 45)
        x, y = 64 + math.cos(a) * 36, 64 + math.sin(a) * 36
        circle(d, x, y, 12, fill=CORAL)
        poly(d, [(x - 5, y - 6), (x, y - 20), (x + 5, y - 6)], BONE)
    circle(d, 64, 64, 10)
    return img


# ---- Magnet-Bot, Plunger-Bot, Pinball-Bot ----
RED = (230, 50, 45, 255)
BLUE = (60, 130, 255, 255)
STEEL = (200, 205, 215, 255)
RUBBER = (200, 25, 30, 255)
WOOD = (170, 115, 60, 255)
CHROME = (225, 230, 240, 255)
NEON = (255, 80, 220, 255)
YEL = (255, 220, 40, 255)


def horseshoe(d, x, y, s=1.0, w=14):
    pts = [(x + math.cos(a) * 26 * s, y + math.sin(a) * 26 * s) for a in [math.pi * i / 29 for i in range(30)]]
    line(d, [(x - 26 * s, y - 24 * s), (x - 26 * s, y)] , w, RED)
    line(d, [(x + 26 * s, y - 24 * s), (x + 26 * s, y)], w, RED)
    line(d, pts, w, RED)
    for sx in (-1, 1):
        d.rectangle((p(x + sx * 26 * s - w / 2, y - 36 * s), p(x + sx * 26 * s + w / 2, y - 22 * s)), fill=STEEL)


def scrapball(d, x, y, r):
    circle(d, x, y, r, fill=(120, 80, 60, 255))
    for i in range(7):
        a = i * 2.4
        circle(d, x + math.cos(a) * r * 0.55, y + math.sin(a) * r * 0.55, r * 0.3, fill=STEEL if i % 2 else (180, 60, 60, 255))


def heavymetal():
    img, d = badge((150, 160, 175))
    scrapball(d, 64, 70, 30)
    arrow(d, 64, 18, 64, 34, 6, 12)
    return img


def shrapnel():
    img, d = badge((230, 70, 50))
    circle(d, 64, 64, 14, fill=(120, 80, 60, 255))
    for i in range(8):
        a = i * math.tau / 8
        x0, y0 = 64 + math.cos(a) * 20, 64 + math.sin(a) * 20
        x1, y1 = 64 + math.cos(a) * 44, 64 + math.sin(a) * 44
        poly(d, [(x1, y1), (x0 + math.cos(a + 1.6) * 5, y0 + math.sin(a + 1.6) * 5), (x0 - math.cos(a + 1.6) * 5, y0 - math.sin(a + 1.6) * 5)], STEEL)
    return img


def wreckingball():
    img, d = badge((140, 150, 170))
    line(d, [(64, 12), (82, 50)], 4, DARK)
    circle(d, 86, 74, 30, fill=DARK)
    circle(d, 76, 64, 8, fill=(90, 95, 110, 255))
    return img


def junkmagnet():
    img, d = badge((60, 130, 255))
    horseshoe(d, 64, 54, 1.0)
    for x, y in ((40, 100), (64, 108), (88, 100)):
        circle(d, x, y, 7, fill=STEEL)
        arrow(d, x, y - 10, x, y - 22, 3, 7)
    return img


def katamari():
    img, d = badge((255, 200, 60))
    scrapball(d, 64, 64, 26)
    for i, a in enumerate((0.3, 2.2, 4.2)):
        cx, cy = 64 + math.cos(a) * 34, 64 + math.sin(a) * 34
        circle(d, cx, cy, 12, fill=(255, 150, 90, 255))
        circle(d, cx - 4, cy - 3, 3, fill=DARK)
        circle(d, cx + 4, cy - 3, 3, fill=DARK)
    return img


def smash():
    img, d = badge((230, 60, 50))
    circle(d, 34, 64, 16, fill=RED)
    circle(d, 94, 64, 16, fill=BLUE)
    star(d, 64, 64, 18, 8, 8, W)
    arrow(d, 12, 40, 30, 50, 4, 8)
    arrow(d, 116, 40, 98, 50, 4, 8)
    return img


def arcweld():
    img, d = badge((255, 200, 60))
    circle(d, 40, 70, 14, fill=RED)
    circle(d, 88, 70, 14, fill=BLUE)
    line(d, [(40, 52), (50, 30), (60, 44), (72, 22), (80, 40), (88, 52)], 5, W)
    return img


def magnetar():
    img, d = badge((100, 60, 180))
    spiral(d, 64, 64, 46, 2.2, 5, BLUE)
    circle(d, 64, 64, 14, fill=W)
    star(d, 64, 64, 10, 4, 4, RED)
    return img


def scraparmour():
    img, d = badge((150, 160, 175))
    poly(d, [(64, 18), (100, 32), (96, 76), (64, 108), (32, 76), (28, 32)], STEEL)
    for x, y in ((52, 46), (76, 52), (58, 74), (78, 80)):
        d.rectangle((p(x - 6, y - 5), p(x + 6, y + 5)), fill=(150, 90, 60, 255))
    return img


def polereversal():
    img, d = badge((60, 130, 255))
    horseshoe(d, 64, 60, 0.9)
    arrow(d, 20, 100, 44, 100, 5, 10)
    arrow(d, 108, 100, 84, 100, 5, 10)
    arrow(d, 64, 110, 64, 92, 5, 10)
    return img


def plunger_shape(d, x, y, s=1.0):
    line(d, [(x, y - 40 * s), (x, y + 6 * s)], 7, WOOD)
    d.chord((p(x - 26 * s, y), p(x + 26 * s, y + 44 * s)), 180, 360, fill=RUBBER)
    d.rectangle((p(x - 28 * s, y + 20 * s), p(x + 28 * s, y + 25 * s)), fill=(150, 15, 20, 255))


def plunger():
    img, d = badge((90, 200, 255))
    plunger_shape(d, 64, 56)
    return img


def shlorp():
    img, d = badge((90, 200, 255))
    for dx, a in ((-26, -0.5), (0, 0), (26, 0.5)):
        plunger_shape(d, 64 + dx, 50 - abs(dx) * 0.4, 0.55)
    for x, y in ((40, 100), (64, 104), (88, 100)):
        circle(d, x, y, 7, fill=(140, 10, 20, 255))
    return img


def nightmare():
    img, d = badge((140, 230, 60))
    plunger_shape(d, 44, 60, 0.6)
    arrow(d, 60, 50, 96, 30, 4, 9)
    arrow(d, 60, 64, 98, 70, 4, 9)
    circle(d, 104, 28, 8, fill=RUBBER)
    circle(d, 106, 72, 8, fill=RUBBER)
    return img


def toilet(d, x, y, s=1.0):
    d.rectangle((p(x - 16 * s, y - 34 * s), p(x + 16 * s, y - 10 * s)), fill=W)
    d.chord((p(x - 30 * s, y - 20 * s), p(x + 30 * s, y + 24 * s)), 0, 180, fill=W)
    d.rectangle((p(x - 30 * s, y - 4 * s), p(x + 30 * s, y + 2 * s)), fill=(220, 220, 215, 255))
    d.rectangle((p(x - 10 * s, y + 18 * s), p(x + 10 * s, y + 30 * s)), fill=W)


def flush():
    img, d = badge((60, 130, 255))
    toilet(d, 64, 64)
    spiral(d, 64, 66, 18, 2.0, 4, (60, 130, 255, 255))
    return img


def geyser():
    img, d = badge((70, 190, 255))
    toilet(d, 64, 84, 0.7)
    for dx in (-10, 0, 10):
        line(d, [(64 + dx, 70), (64 + dx * 1.6, 20)], 6, W)
    circle(d, 64, 18, 10, fill=W)
    return img


def clogged():
    img, d = badge((160, 130, 50))
    toilet(d, 64, 72, 0.8)
    for x, y in ((40, 34), (64, 24), (88, 34), (52, 46), (78, 46)):
        circle(d, x, y, 9, fill=(110, 85, 30, 255))
    return img


def cannonball():
    img, d = badge((60, 130, 255))
    circle(d, 64, 54, 24, fill=DARK)
    circle(d, 56, 46, 6, fill=(90, 95, 110, 255))
    for i in range(7):
        a = math.pi * (0.1 + 0.8 * i / 6)
        line(d, [(64 - math.cos(a) * 30, 104 - math.sin(a) * 6), (64 - math.cos(a) * 46, 104 - math.sin(a) * 26)], 5, W)
    return img


def spring():
    img, d = badge((255, 200, 60))
    pts = [(64 + (18 if i % 2 else -18), 100 - i * 9) for i in range(9)]
    line(d, pts, 6, CHROME)
    circle(d, 64, 22, 12, fill=(230, 40, 50, 255))
    return img


def bumper():
    img, d = badge((255, 80, 220))
    circle(d, 64, 64, 34, fill=W)
    circle(d, 64, 64, 26, fill=NEON)
    circle(d, 64, 64, 10, fill=W)
    for i in range(6):
        a = i * math.tau / 6
        line(d, [(64 + math.cos(a) * 40, 64 + math.sin(a) * 40), (64 + math.cos(a) * 52, 64 + math.sin(a) * 52)], 5, YEL)
    return img


def jackpot():
    img, d = badge((255, 200, 60))
    for i, (x, y) in enumerate(((34, 80), (64, 56), (94, 32))):
        star(d, x, y, 12 + i * 4, 5 + i * 2, 5, W)
    return img


def multiball():
    img, d = badge((255, 80, 220))
    for x, y in ((40, 76), (64, 46), (88, 76)):
        circle(d, x, y, 16, fill=CHROME)
        circle(d, x - 5, y - 5, 5, fill=W)
    return img


def flipper():
    img, d = badge((255, 150, 40))
    poly(d, [(26, 60), (102, 84), (100, 96), (24, 76)], YEL)
    circle(d, 26, 68, 12, fill=CHROME)
    circle(d, 96, 40, 12, fill=CHROME)
    arrow(d, 72, 66, 92, 50, 4, 8)
    return img


def combo():
    img, d = badge((255, 200, 60))
    for i in range(3):
        x = 30 + i * 30
        poly(d, [(x - 10, 92), (x + 10, 92), (x + 10, 92 - 20 - i * 18), (x - 10, 92 - 20 - i * 18)], W)
    return img


def ramp():
    img, d = badge((150, 80, 255))
    poly(d, [(18, 104), (96, 104), (96, 58)], W)
    pts = [(96 + math.cos(a) * 0, 0) for a in []]
    arc = [(96 - 30 * math.cos(math.pi * t / 20), 58 - 40 * math.sin(math.pi * t / 20)) for t in range(21)]
    line(d, [(x + 0, y) for x, y in arc], 4, YEL)
    circle(d, 66, 58, 10, fill=CHROME)
    return img


def highscore():
    img, d = badge((255, 200, 60))
    d.rectangle((p(18, 44), p(110, 84)), fill=DARK)
    for i in range(5):
        x = 26 + i * 18
        for seg in ((0, -12, 0, 12),):
            line(d, [(x, 54), (x + 8, 54), (x + 8, 74), (x, 74), (x, 54)], 3, YEL)
    star(d, 64, 26, 14, 6, 5, W)
    return img


# ---- Goo-Bot ----
GRAPE = (140, 35, 200, 255)
MAG = (240, 70, 190, 255)
PINKY = (255, 170, 235, 255)


def goo(d, x, y, r, fill=GRAPE, drips=2):
    """A wobbly blob of goo with a shine and drips hanging off it."""
    pts = [(x + math.cos(a) * r * (1 + 0.08 * math.sin(a * 5)), y + math.sin(a) * r * (1 + 0.08 * math.sin(a * 5))) for a in [math.tau * i / 40 for i in range(40)]]
    poly(d, pts, fill)
    for k in range(drips):
        dx = x - r * 0.5 + k * r * 0.8
        line(d, [(dx, y + r * 0.6), (dx, y + r * (1.1 + 0.3 * k))], max(3, int(r * 0.25)), fill)
        circle(d, dx, y + r * (1.1 + 0.3 * k), r * 0.18, fill=fill)
    circle(d, x - r * 0.35, y - r * 0.35, r * 0.2, fill=PINKY)


def thickgoo():
    img, d = badge((150, 60, 220))
    goo(d, 64, 58, 32)
    plus(d, 96, 96, 10, 5)
    return img


def slowset():
    img, d = badge((60, 180, 255))
    goo(d, 52, 60, 24)
    circle(d, 88, 76, 22, fill=None, outline=W, width=5)
    line(d, [(88, 76), (88, 62)], 4)
    line(d, [(88, 76), (98, 82)], 4)
    return img


def tarpit():
    img, d = badge((90, 50, 150))
    d.ellipse((p(16, 80), p(112, 110)), fill=GRAPE)
    for x in (40, 64, 88):
        line(d, [(x, 30), (x, 72)], 6)
        arrow(d, x, 46, x, 70, 4, 9)
    return img


def splatter():
    img, d = badge((240, 80, 190))
    goo(d, 64, 64, 18, drips=0)
    for i in range(7):
        a = i * math.tau / 7
        circle(d, 64 + math.cos(a) * 40, 64 + math.sin(a) * 40, 7 + (i % 3) * 2, fill=GRAPE)
    return img


def heavygoo():
    img, d = badge((150, 160, 175))
    goo(d, 64, 54, 30)
    d.rectangle((p(30, 96), p(98, 106)), fill=DARK)
    arrow(d, 64, 18, 64, 30, 5, 10)
    return img


def feedback():
    img, d = badge((255, 200, 60))
    goo(d, 46, 60, 20, drips=0)
    arrow(d, 70, 44, 100, 44, 5, 10)
    bullet(d, 100, 80, 34, 14, GRAPE)
    return img


def bigball():
    img, d = badge((255, 120, 200))
    goo(d, 70, 62, 36, drips=0)
    for y in (40, 62, 84):
        line(d, [(10, y), (28, y)], 4)
    return img


def jellyhop():
    img, d = badge((240, 80, 190))
    for x in (28, 64, 100):
        circle(d, x, 96, 10, fill=DARK)
    line(d, [(28, 86), (46, 50), (64, 86), (82, 50), (100, 86)], 4)
    goo(d, 82, 44, 13, MAG, drips=0)
    return img


def longhop():
    img, d = badge((60, 180, 255))
    pts = [(18 + i * 4.6, 96 - math.sin(i / 20 * math.pi) * 64) for i in range(21)]
    line(d, pts, 4)
    goo(d, 18, 90, 11, MAG, drips=0)
    goo(d, 110, 90, 11, MAG, drips=0)
    return img


def jiggle():
    img, d = badge((255, 120, 200))
    goo(d, 64, 66, 26, MAG, drips=0)
    for sx in (-1, 1):
        for k in range(2):
            x = 64 + sx * (38 + k * 10)
            line(d, [(x, 46), (x - sx * 6, 66), (x, 86)], 3)
    return img


def sticky():
    img, d = badge((150, 60, 220))
    d.ellipse((p(14, 74), p(114, 108)), fill=MAG)
    for x in (44, 84):
        line(d, [(x, 86), (x, 50)], 4, PINKY)
    circle(d, 64, 46, 16, fill=DARK)
    return img


def fatjelly():
    img, d = badge((255, 140, 50))
    goo(d, 64, 62, 38, MAG, drips=0)
    circle(d, 64, 62, 16, fill=GRAPE)
    return img


def volatile():
    img, d = badge((230, 60, 50))
    star(d, 64, 64, 46, 22, 10, (255, 220, 60, 255))
    goo(d, 64, 64, 18, MAG, drips=0)
    return img


def motherjelly():
    img, d = badge((255, 200, 60))
    goo(d, 64, 58, 30, MAG, drips=0)
    circle(d, 54, 54, 6, fill=DARK)
    circle(d, 74, 54, 6, fill=DARK)
    line(d, [(52, 72), (64, 78), (76, 72)], 4, DARK)
    for x in (26, 102):
        goo(d, x, 96, 9, MAG, drips=0)
    return img


def superbounce():
    img, d = badge((60, 180, 255))
    goo(d, 64, 40, 18, MAG, drips=0)
    arrow(d, 64, 104, 64, 66, 6, 12)
    d.ellipse((p(30, 100), p(98, 112)), fill=GRAPE)
    return img


def softlanding():
    img, d = badge((255, 120, 200))
    d.ellipse((p(18, 78), p(110, 108)), fill=GRAPE)
    poly(d, [(64, 20), (92, 32), (88, 62), (64, 78), (40, 62), (36, 32)], W)
    return img


def gooshell():
    img, d = badge((150, 60, 220))
    poly(d, [(64, 16), (100, 30), (96, 74), (64, 108), (32, 74), (28, 30)], PINKY)
    goo(d, 64, 56, 18, GRAPE)
    return img


def puddle():
    img, d = badge((90, 50, 150))
    d.ellipse((p(12, 62), p(116, 104)), fill=GRAPE)
    d.ellipse((p(30, 70), p(70, 84)), fill=PINKY)
    for x in (40, 80):
        circle(d, x, 40, 6, fill=MAG)
    return img


def gooquake():
    img, d = badge((240, 80, 190))
    for r, w in ((50, 4), (36, 5), (22, 6)):
        d.arc((p(64 - r, 80 - r * 0.45), p(64 + r, 80 + r * 0.45)), 0, 360, fill=W, width=w * S_)
    goo(d, 64, 40, 16, GRAPE, drips=0)
    return img



def sweetspot():
    # A flipper with its tip lit up like a target.
    img, d = badge((255, 150, 40))
    poly(d, [(22, 84), (94, 52), (100, 62), (26, 98)], YEL)
    circle(d, 24, 90, 12, fill=CHROME)
    circle(d, 98, 56, 18, fill=None, outline=W, width=5)
    circle(d, 98, 56, 7, fill=W)
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
    "reel": reel, "towline": towline, "gutting": gutting, "trawler": trawler, "bulb": bulb, "pockets": pockets,
    "bloom": bloom, "seadog": seadog, "leviathan": leviathan, "spines": spines, "growth": growth, "clusters": clusters,
    "ricochet": ricochet, "bleach": bleach, "heal": heal, "spikedwall": spikedwall, "anemone": anemone,
    "livingreef": livingreef, "dig": dig, "upheaval": upheaval, "calcify": calcify, "barrier": barrier,
    "heavymetal": heavymetal, "shrapnel": shrapnel, "wreckingball": wreckingball, "junkmagnet": junkmagnet,
    "katamari": katamari, "smash": smash, "arcweld": arcweld, "magnetar": magnetar, "scraparmour": scraparmour,
    "polereversal": polereversal, "plunger": plunger, "shlorp": shlorp, "nightmare": nightmare, "flush": flush,
    "geyser": geyser, "clogged": clogged, "cannonball": cannonball, "spring": spring, "bumper": bumper,
    "jackpot": jackpot, "multiball": multiball, "flipper": flipper, "combo": combo, "ramp": ramp, "highscore": highscore,
    "thickgoo": thickgoo, "slowset": slowset, "tarpit": tarpit, "splatter": splatter, "heavygoo": heavygoo, "feedback": feedback,
    "bigball": bigball, "jellyhop": jellyhop, "longhop": longhop, "jiggle": jiggle, "sticky": sticky, "fatjelly": fatjelly,
    "volatile": volatile, "motherjelly": motherjelly, "superbounce": superbounce, "softlanding": softlanding, "gooshell": gooshell,
    "puddle": puddle, "gooquake": gooquake,
    "sweetspot": sweetspot,
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
