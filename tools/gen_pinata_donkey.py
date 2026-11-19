"""Builds "pinata-donkey": the little donkey pinata a Fiesta Popper blast turns a zombie into.

Run inside Blender with assets/custom-weapons/custom-weapons.blend open:
    exec(open("tools/gen_pinata_donkey.py").read())
Set FINAL = False first to leave the loose parts in the scene for a look instead of exporting.

Faces Blender -Y (Godot +Z, the way characters face). Origin at the hooves; ~1.05 tall at scale 1.
Body, neck and legs are stacks of overlapping paper-fringe bands in fiesta colours.
"""
import bpy, math, random
from mathutils import Vector

exec(open(bpy.path.abspath("//../../tools/blender_lowpoly.py")).read())
FINAL = globals().get("FINAL", True)
random.seed(3)
b = Builder("pinata-wip")


def fringe(name, a, c, r_a, r_c, bands, colors, segs=10, flare=0.018):
    """Banded tube from a to c: each band flares at its leading edge like a layer of crepe-paper fringe."""
    a, c = Vector(a), Vector(c)
    for i in range(bands):
        p0 = a.lerp(c, i / bands)
        p1 = a.lerp(c, (i + 1) / bands)
        r0 = r_a + (r_c - r_a) * i / bands
        r1 = r_a + (r_c - r_a) * (i + 1) / bands
        b.between(f"{name}{i}", p0, p1, r0 - flare * 0.4, r1 + flare, segs, colors[i % len(colors)])


# ---- body: barrel of fringe bands, rounded chest and rump
BZ = 0.52
fringe("body", (0, 0.36, BZ), (0, -0.30, BZ), 0.2, 0.2, 7, ["purple", "yellow", "teal", "pink", "orange", "blue", "pink"], segs=12)
b.sphere("rump", 0.205, T((0, 0.36, BZ)) @ S(1, 0.55, 1), "purple", segs=12, rings=6)
b.sphere("chest", 0.205, T((0, -0.31, BZ)) @ S(1, 0.5, 1), "pink", segs=12, rings=6)
# saddle blanket with a gold trim on top
b.box("blanket", (0.3, 0.26, 0.03), T((0, 0.02, BZ + 0.2)), "red", bevel=0.01)
b.box("blanket_trim", (0.31, 0.27, 0.012), T((0, 0.02, BZ + 0.185)), "gold")

# ---- legs: fringe tubes with dark hooves
for lx in (-0.11, 0.11):
    for ly in (-0.22, 0.24):
        fringe(f"leg{lx}{ly}", (lx, ly, 0.42), (lx, ly, 0.06), 0.06, 0.055, 3, ["yellow", "teal", "pink"], segs=8, flare=0.012)
        b.cyl(f"hoof{lx}{ly}", 0.062, 0.055, 0.06, 8, "dark", T((lx, ly, 0.03)))

# ---- neck and head (looking slightly up, -Y forward)
NECK0, NECK1 = Vector((0, -0.3, 0.62)), Vector((0, -0.44, 0.88))
fringe("neck", NECK0, NECK1, 0.1, 0.09, 3, ["orange", "teal", "pink"], segs=10, flare=0.014)
HC = Vector((0, -0.5, 0.92))
b.sphere("head", 0.12, T(HC) @ S(0.95, 1.15, 0.95), "yellow", segs=10, rings=6)
b.between("snout", HC + Vector((0, -0.05, -0.02)), HC + Vector((0, -0.24, -0.06)), 0.1, 0.085, 10, "cream")
b.cyl("snout_tip", 0.085, 0.07, 0.03, 10, "tan", T(HC + Vector((0, -0.25, -0.062))) @ RX(-78))
for s in (-1, 1):
    b.sphere(f"nostril{s}", 0.014, T(HC + Vector((s * 0.035, -0.265, -0.04))), "black", segs=6, rings=4)
    b.sphere(f"eye{s}", 0.03, T(HC + Vector((s * 0.085, -0.08, 0.04))), "black", segs=8, rings=5)
    b.sphere(f"glint{s}", 0.01, T(HC + Vector((s * 0.105, -0.095, 0.058))), "white", segs=6, rings=4)
    # tall ears, tipped outward, pink inside
    base = HC + Vector((s * 0.06, 0.03, 0.09))
    tip = base + Vector((s * 0.08, 0.05, 0.24))
    b.between(f"ear{s}", base, tip, 0.045, 0.012, 6, "yellow")
    b.between(f"ear_in{s}", base + Vector((0, -0.02, 0.02)), tip + Vector((0, -0.02, -0.03)), 0.028, 0.008, 6, "pink")
# mane: a crest of paper tufts down the neck
for i in range(6):
    t = i / 5
    p = (HC + Vector((0, 0.07, 0.1))).lerp(NECK0 + Vector((0, 0.06, 0.1)), t)
    b.box(f"mane{i}", (0.03, 0.05, 0.07), T(p) @ RX(-30 + 15 * t), FIESTA[i % len(FIESTA)])

# ---- tassel tail
TB = Vector((0, 0.47, BZ + 0.06))
b.between("tail", TB, TB + Vector((0, 0.08, -0.06)), 0.02, 0.016, 6, "purple")
for i, c in enumerate(("pink", "yellow", "teal", "orange", "blue")):
    a = math.radians(-30 + 15 * i)
    top = TB + Vector((0, 0.08, -0.06))
    b.box(f"streamer{i}", (0.014, 0.022, 0.2), T(top + Vector((math.sin(a) * 0.04, 0.02, -0.1))) @ RY(math.degrees(a) * 0.6), c)

result = b.join_and_export("pinata-donkey") if FINAL else {"parts": len(b.parts)}
