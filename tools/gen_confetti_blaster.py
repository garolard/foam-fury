"""Builds "blaster-confetti" (JO's Fiesta Popper: a giant party popper that fires confetti)
in assets/custom-weapons/custom-weapons.blend and exports its GLB.

Run inside Blender with that file open:
    exec(open("tools/gen_confetti_blaster.py").read())
Set FINAL = False first to leave the loose parts in the scene for a look instead of exporting.

Blender +Y is the muzzle (glTF -Z, the kit convention), +Z up.
"""
import bpy, math, random
from mathutils import Vector

exec(open(bpy.path.abspath("//../../tools/blender_lowpoly.py")).read())
FINAL = globals().get("FINAL", True)
random.seed(7)
b = Builder("confetti-wip")

# ---- receiver: kit-lavender body with a pink back cap
b.box("body", (0.12, 0.40, 0.15), T((0, -0.05, 0.06)), "lav", bevel=0.02)
b.box("rail", (0.05, 0.30, 0.03), T((0, -0.05, 0.145)), "lavdark", bevel=0.008)
b.cyl("backcap", 0.075, 0.07, 0.06, 10, "pink", T((0, -0.27, 0.06)) @ AX_Y, bevel=0.006)
b.torus("backring", 0.07, 0.012, T((0, -0.24, 0.06)) @ AX_Y, "yellow", segs=12)

# ---- the popper bell: a flared cone in fiesta stripes, gold lip, confetti spilling out
Y0, Y1, R0, R1 = 0.13, 0.55, 0.06, 0.165
n = len(FIESTA)
for i, c in enumerate(FIESTA):
    t0, t1 = i / n, (i + 1) / n
    ya, yb = Y0 + (Y1 - Y0) * t0, Y0 + (Y1 - Y0) * t1
    ra, rb = R0 + (R1 - R0) * t0 ** 1.4, R0 + (R1 - R0) * t1 ** 1.4
    b.cyl(f"bell{i}", ra, rb, yb - ya, 14, c, T((0, (ya + yb) / 2, 0.06)) @ AX_Y, caps=i < n - 1)  # open mouth
b.torus("lip", R1 + 0.004, 0.016, T((0, Y1, 0.06)) @ AX_Y, "gold", segs=18)
b.cyl("mouth", R1 - 0.01, R1 - 0.01, 0.01, 14, "plum", T((0, Y1 - 0.03, 0.06)) @ AX_Y)
for i in range(16):
    a = random.uniform(0, 2 * math.pi)
    r = random.uniform(0.02, R1 - 0.035)
    p = Vector((r * math.cos(a), Y1 - 0.02 + random.uniform(-0.01, 0.025), 0.06 + r * math.sin(a)))
    rot = RX(random.uniform(0, 180)) @ RZ(random.uniform(0, 180))
    b.box(f"bit{i}", (0.035, 0.008, 0.022), T(p) @ rot, random.choice(FIESTA + ["white", "lightyellow"]))

# ---- confetti hopper on top: a pale dome full of sprinkles, star topper
HC = Vector((0, -0.07, 0.2))
b.cyl("hopper_base", 0.085, 0.09, 0.04, 12, "gun", T(HC + Vector((0, 0, -0.03))))
b.sphere("hopper", 0.1, T(HC + Vector((0, 0, 0.03))) @ S(1, 1, 0.9), "skyblue", segs=12, rings=8)
for i in range(22):
    u, v = random.uniform(0, 2 * math.pi), random.uniform(0.1, 1.35)
    d = Vector((math.sin(v) * math.cos(u), math.sin(v) * math.sin(u), math.cos(v) * 0.9))
    p = HC + Vector((0, 0, 0.03)) + d * 0.098
    rot = Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4() @ RZ(random.uniform(0, 180))
    b.box(f"sprinkle{i}", (0.028, 0.012, 0.01), T(p) @ rot, random.choice(FIESTA))
b.between("stem", HC + Vector((0, 0, 0.12)), HC + Vector((0, 0, 0.17)), 0.008, 0.008, 6, "gold")
b.star("star", 0.05, 0.022, 0.02, T(HC + Vector((0, 0, 0.2))) @ AX_X, "yellow")

# ---- grip, trigger and the pull-string ring of a real party popper
b.box("grip", (0.06, 0.075, 0.2), T((0, -0.16, -0.08)) @ RX(-14), "dark", bevel=0.01)
b.box("guard", (0.03, 0.12, 0.016), T((0, -0.06, -0.075)), "dark", bevel=0.004)
b.box("guard_front", (0.03, 0.016, 0.07), T((0, -0.005, -0.045)), "dark", bevel=0.004)
b.box("trigger", (0.018, 0.018, 0.05), T((0, -0.07, -0.04)) @ RX(12), "orange", bevel=0.003)
b.box("foregrip", (0.05, 0.07, 0.11), T((0, 0.09, -0.05)), "pink", bevel=0.01)
b.between("string", (0, -0.3, 0.02), (0, -0.36, -0.1), 0.007, 0.007, 6, "white")
b.torus("pullring", 0.035, 0.009, T((0, -0.375, -0.13)) @ AX_X, "yellow", segs=12)
for i, c in enumerate(("pink", "teal", "purple")):  # tassel ribbons on the ring
    a = math.radians(-25 + 25 * i)
    b.box(f"ribbon{i}", (0.012, 0.02, 0.09), T((0.0, -0.375 - 0.04 * math.sin(a), -0.2)) @ RX(math.degrees(a)), c)

result = b.join_and_export("blaster-confetti") if FINAL else {"parts": len(b.parts)}
