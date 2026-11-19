"""Builds "blaster-mariachi" (a minigun disguised as a mariachi guitar case) in
assets/custom-weapons/custom-weapons.blend.

Run inside Blender with that file open:
    exec(open("tools/gen_mariachi_case.py").read())

Conventions match blaster-mug / blaster-flame: Blender +Y is the muzzle (glTF -Z),
+Z up, X is the thickness. Every face samples a flat cell of the shared Kenney
colormap (material "colormap_custom"); flat shading.
"""
import bpy, bmesh, math
from mathutils import Matrix, Vector

NAME = "blaster-mariachi"
# UVs of uniform cells in the Kenney blaster-kit colormap
PAL = {
    "black": (0.0713, 0.0244),   # 45404d
    "gold": (0.3838, 0.2588),    # ffc044
    "red": (0.6963, 0.2588),     # d1544f
    "lav": (0.5713, 0.165),      # aab2d5
    "lavdark": (0.5713, 0.0088), # 777d96
    "gun": (0.3838, 0.0088),     # 4f5260
    "dark": (0.1338, 0.0088),    # 38383d
    "orange": (0.5713, 0.2588),  # ff7244
    "leather": (0.9463, 0.165),  # a05c41
    "cream": (0.1338, 0.2588),   # fde4c7
    "copper": (0.8213, 0.0088),  # b26243
}
MAT = bpy.data.materials["colormap_custom"]
scn = bpy.context.scene
coll = bpy.data.collections.get("mariachi-wip") or bpy.data.collections.new("mariachi-wip")
if coll.name not in scn.collection.children:
    scn.collection.children.link(coll)
for o in list(coll.objects):
    bpy.data.objects.remove(o)
PARTS = []

T = Matrix.Translation
RX = lambda a: Matrix.Rotation(math.radians(a), 4, 'X')
RY = lambda a: Matrix.Rotation(math.radians(a), 4, 'Y')
AX_Y = RX(-90)  # local Z -> +Y (barrels)
AX_X = RY(90)   # local Z -> +X (studs, decals)


def finish(name, bm, color, bevel=0.0, seg=1):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    uv = me.uv_layers.new(name="UVMap")
    for l in uv.data:
        l.uv = PAL[color]
    me.materials.append(MAT)
    o = bpy.data.objects.new(name, me)
    coll.objects.link(o)
    if bevel > 0:
        m = o.modifiers.new("bev", "BEVEL")
        m.width = bevel
        m.segments = seg
        m.limit_method = 'ANGLE'
        m.angle_limit = math.radians(35)
    PARTS.append(o)
    return o


def cyl(name, r1, r2, depth, segs, color, mat, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r1, radius2=r2, depth=depth)
    bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
    return finish(name, bm, color, bevel)


def box(name, size, mat, color, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
    return finish(name, bm, color, bevel)


def torus(name, major, minor, mat, color, segs=20, ring=6):
    bm = bmesh.new()
    rows = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        row = []
        for j in range(ring):
            b = 2 * math.pi * j / ring
            r = major + minor * math.cos(b)
            row.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        rows.append(row)
    for i in range(segs):
        for j in range(ring):
            a, b = rows[i], rows[(i + 1) % segs]
            bm.faces.new((a[j], b[j], b[(j + 1) % ring], a[(j + 1) % ring]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
    return finish(name, bm, color)


# ------------------------------------------------------------- case silhouette
ZC = 0.05             # case centre line height
HALF_X = 0.075        # half thickness
BUTT_Y, BUTT_R = -0.33, 0.17
Y1 = 0.35             # neck end, barrels stick out past here


def cosb(a, b, t):
    t = max(0.0, min(1.0, t))
    return a + (b - a) * (1 - math.cos(math.pi * t)) / 2


def hh(y):
    """Half height of the guitar outline at y (lower bout -> waist -> upper bout -> neck)."""
    if y <= BUTT_Y:
        return math.sqrt(max(0.0, BUTT_R ** 2 - (y - BUTT_Y) ** 2))
    if y <= -0.13:
        return cosb(0.17, 0.12, (y - BUTT_Y) / 0.2)
    if y <= 0.0:
        return cosb(0.12, 0.14, (y + 0.13) / 0.13)
    if y <= 0.15:
        return cosb(0.14, 0.085, y / 0.15)
    return 0.085 - 0.01 * (y - 0.15) / 0.2


def outline(off=0.0, n=48):
    ys = [BUTT_Y + (Y1 - BUTT_Y) * i / n for i in range(1, n + 1)]
    top = [(y, ZC + hh(y) + off) for y in ys]
    bot = [(y, ZC - hh(y) - off) for y in ys]
    r = BUTT_R + off
    arc = [(BUTT_Y + r * math.cos(math.pi / 2 + math.pi * i / 12), ZC + r * math.sin(math.pi / 2 + math.pi * i / 12))
           for i in range(1, 12)]
    return ([(BUTT_Y, ZC + r)] + top + [(Y1 + off, top[-1][1]), (Y1 + off, bot[-1][1])]
            + bot[::-1] + [(BUTT_Y, ZC - r)] + arc[::-1])


def slab(name, pts, x0, x1, color, bevel=0.0, seg=1):
    bm = bmesh.new()
    f = bm.faces.new([bm.verts.new((x0, y, z)) for y, z in pts])
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    bmesh.ops.translate(bm, vec=(x1 - x0, 0, 0), verts=[e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return finish(name, bm, color, bevel, seg)


slab("case", outline(), -HALF_X, HALF_X, "black", bevel=0.022, seg=2)
slab("piping", outline(0.006), -0.012, 0.012, "gold")   # gold seam where the lid closes

# ------------------------------------------------------------- side ornaments (both faces)
for s in (1, -1):
    sx = s * (HALF_X + 0.002)
    face = Matrix.Scale(s, 4, Vector((1, 0, 0)))  # mirror for the -X side
    # rosette on the lower bout: gold ring, red petals, gold heart
    c = Vector((sx, -0.32, ZC))
    torus(f"ros_ring{s}", 0.075, 0.009, T(c) @ AX_X, "gold")
    for i in range(6):
        a = 2 * math.pi * i / 6
        p = c + Vector((0, 0.038 * math.cos(a), 0.038 * math.sin(a)))
        cyl(f"petal{s}{i}", 0.024, 0.024, 0.008, 8, "red", T(p) @ AX_X)
    cyl(f"ros_heart{s}", 0.02, 0.016, 0.014, 8, "gold", T(c + Vector((s * 0.004, 0, 0))) @ face @ AX_X)
    # diamond emblem on the upper bout
    cyl(f"diamond{s}", 0.04, 0.03, 0.01, 4, "gold", T((sx, 0.0, ZC)) @ face @ AX_X @ Matrix.Rotation(math.pi / 4, 4, 'Z') @ Matrix.Scale(1, 4))
    cyl(f"dia_gem{s}", 0.014, 0.01, 0.016, 6, "red", T((sx, 0.0, ZC)) @ face @ AX_X)
    # charro studs following the outline
    for y in (-0.42, -0.2, -0.12, -0.04, 0.06, 0.15, 0.23, 0.31):
        h = hh(y) - 0.04
        for z in (ZC + h, ZC - h):
            cyl(f"stud{s}", 0.011, 0.007, 0.01, 6, "gold", T((sx, y, z)) @ face @ AX_X)
    # latches on the lid seam
    for y in (-0.36, 0.12):
        box(f"latch{s}", (0.012, 0.045, 0.03), T((s * 0.082, y, ZC + hh(y) - 0.015)), "gold", bevel=0.003)

# ------------------------------------------------------------- carry handle (leather) on top
top = lambda y: ZC + hh(y)
for y in (-0.2, -0.05):
    box("handle_post", (0.04, 0.03, 0.06), T((0, y, top(y) + 0.02)), "gold", bevel=0.004)
box("handle_grip", (0.04, 0.2, 0.035), T((0, -0.125, max(top(-0.2), top(-0.05)) + 0.06)), "leather", bevel=0.008)

# ------------------------------------------------------------- minigun out of the neck
NY = Y1
cyl("housing", 0.07, 0.075, 0.12, 12, "gun", T((0, NY + 0.02, ZC)) @ AX_Y, bevel=0.006)
cyl("collar", 0.082, 0.082, 0.025, 12, "lavdark", T((0, NY + 0.085, ZC)) @ AX_Y, bevel=0.004)
cyl("spindle", 0.02, 0.02, 0.27, 8, "dark", T((0, NY + 0.2, ZC)) @ AX_Y)
for i in range(6):
    a = 2 * math.pi * i / 6 + math.pi / 6
    off = Vector((0.044 * math.cos(a), 0, 0.044 * math.sin(a)))
    base = Vector((0, 0, ZC)) + off
    cyl(f"barrel{i}", 0.017, 0.017, 0.27, 8, "lav", T(base + Vector((0, NY + 0.215, 0))) @ AX_Y)
    cyl(f"tip{i}", 0.021, 0.021, 0.03, 8, "orange", T(base + Vector((0, NY + 0.34, 0))) @ AX_Y)
cyl("clamp", 0.07, 0.07, 0.025, 12, "lavdark", T((0, NY + 0.24, ZC)) @ AX_Y, bevel=0.004)

# ------------------------------------------------------------- pistol grip + trigger under the waist
under = lambda y: ZC - hh(y)
box("grip", (0.055, 0.07, 0.19), T((0, -0.12, under(-0.12) - 0.085)) @ RX(-14), "dark", bevel=0.01)
box("guard_bot", (0.03, 0.11, 0.016), T((0, -0.035, under(-0.04) - 0.075)), "dark", bevel=0.004)
box("guard_front", (0.03, 0.016, 0.08), T((0, 0.018, under(0.0) - 0.04)), "dark", bevel=0.004)
box("trigger", (0.018, 0.018, 0.05), T((0, -0.045, under(-0.04) - 0.03)) @ RX(12), "orange", bevel=0.003)

# ------------------------------------------------------------- ammo belt spilling from the case seam
for i in range(7):
    t = i / 6
    y = -0.24 - 0.13 * t
    z = under(-0.24) + 0.02 - 0.06 * t - 0.11 * t * t
    rot = RX(-35 * t)
    cyl(f"round{i}", 0.014, 0.014, 0.075, 8, "gold", T((0.0, y, z)) @ rot @ AX_X)
    cyl(f"slug{i}", 0.014, 0.004, 0.025, 8, "copper", T((0.0475, y, z)) @ rot @ AX_X)
    box(f"link{i}", (0.03, 0.03, 0.012), T((-0.02, y, z)) @ rot, "dark")

# ------------------------------------------------------------- join, export, store like the other custom guns
GLB = bpy.path.abspath("//Models/GLB format/" + NAME + ".glb")
old = bpy.data.objects.get(NAME)
if old:
    bpy.data.objects.remove(old)
dg = bpy.context.evaluated_depsgraph_get()
bm = bmesh.new()
for o in PARTS:
    m = o.evaluated_get(dg).to_mesh()
    m.transform(o.matrix_world)
    bm.from_mesh(m)
    o.evaluated_get(dg).to_mesh_clear()
for f in bm.faces:
    f.smooth = False
me = bpy.data.meshes.get(NAME) or bpy.data.meshes.new(NAME)
bm.to_mesh(me)
bm.free()
me.materials.clear()
me.materials.append(MAT)
for o in PARTS:
    data = o.data
    bpy.data.objects.remove(o)
    bpy.data.meshes.remove(data)
gun = bpy.data.objects.new(NAME, me)
coll.objects.link(gun)
bpy.ops.object.select_all(action='DESELECT')
gun.select_set(True)
bpy.context.view_layer.objects.active = gun
bpy.ops.export_scene.gltf(filepath=GLB, use_selection=True, export_format='GLB', export_apply=True)
# keep it in the file but out of the (empty) scene, like blaster-mug / blaster-flame
coll.objects.unlink(gun)
gun.use_fake_user = True
bpy.data.collections.remove(coll)

result = {"parts": len(PARTS), "verts": len(me.vertices), "faces": len(me.polygons), "glb": GLB,
          "dims": [round(v, 3) for v in gun.dimensions]}
