"""Generate stylized low-poly fire meshes for the game.

Run headless:  blender -b -P tools/gen_fire_meshes.py
Writes GLBs to assets/fire/Models/GLB format/ plus assets/fire/fire.blend.

All meshes are flat-shaded with a COLOR_0 vertex-color gradient (hot base -> cool tip);
the game renders them unshaded with vertex color as albedo, so a material/particle tint
multiplies on top. Origins sit at the base of each flame (fireball: centre).
"""
import bpy, bmesh, math, os, random
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "assets", "fire", "Models", "GLB format")
os.makedirs(OUT, exist_ok=True)

# hot -> cool, values in 0..1 (the game multiplies to HDR for bloom)
FIRE = [(0.0, (1.0, 0.95, 0.65)), (0.3, (1.0, 0.72, 0.18)), (0.65, (1.0, 0.38, 0.06)), (1.0, (0.85, 0.12, 0.03))]
# near-white ramp for meshes that get tinted by a particle colour ramp
PALE = [(0.0, (1.0, 1.0, 1.0)), (1.0, (0.82, 0.78, 0.74))]


def ramp(stops, u):
    u = max(0.0, min(1.0, u))
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if u <= b:
            k = (u - a) / (b - a)
            return tuple(ca[i] + (cb[i] - ca[i]) * k for i in range(3)) + (1.0,)
    return stops[-1][1] + (1.0,)


def tongue(bm, height=1.0, radius=0.32, segs=7, rings=7, twist=1.3, lean=(0.12, 0.0),
           offset=(0, 0, 0), tilt=(0.0, 0.0), stops=FIRE, wobble=0.12, seed=0):
    """Twisted teardrop flame: rounded bulb low, tapering to a curling tip."""
    rnd = random.Random(seed)
    col = bm.loops.layers.color.get("Color") or bm.loops.layers.color.new("Color")
    ox, oy, oz = offset
    rows, us = [], []
    for r in range(rings):
        u = (r + 0.35) / rings  # skip u=0 so the bottom is a small cap, not a point
        # bulb peaks low (u~0.18) then tapers concavely to a fine tip
        prof = (u / 0.18) ** 0.5 if u < 0.18 else ((1.0 - u) / 0.82) ** 1.35
        rr = radius * prof
        ang0 = twist * u
        # S-shaped sway so the tongue looks like it's licking, not a cone
        sway = 0.22 * height * math.sin(math.pi * 1.4 * u + seed) * u
        cx = lean[0] * u * u + tilt[0] * u * height + sway * math.cos(seed * 2.1)
        cy = lean[1] * u * u + tilt[1] * u * height + sway * math.sin(seed * 2.1)
        cx += rnd.uniform(-0.02, 0.02) * height; cy += rnd.uniform(-0.02, 0.02) * height
        z = height * (u ** 0.9)
        row = []
        for s in range(segs):
            a = ang0 + s * 2 * math.pi / segs
            w = 1.0 + rnd.uniform(-wobble, wobble)
            row.append(bm.verts.new((ox + cx + math.cos(a) * rr * w, oy + cy + math.sin(a) * rr * w, oz + z)))
        rows.append(row)
        us.append(u)
    sway = 0.22 * height * math.sin(math.pi * 1.4 + seed)
    tip = bm.verts.new((ox + lean[0] + tilt[0] * height + sway * math.cos(seed * 2.1),
                        oy + lean[1] + tilt[1] * height + sway * math.sin(seed * 2.1), oz + height))
    bot = bm.verts.new((ox, oy, oz + height * 0.015))

    def face(vs, u):
        f = bm.faces.new(vs)
        for l in f.loops:
            l[col] = ramp(stops, u)
        return f

    for r in range(rings - 1):
        a, b = rows[r], rows[r + 1]
        for s in range(segs):
            t = (s + 1) % segs
            face((a[s], a[t], b[t], b[s]), (us[r] + us[r + 1]) / 2)
    top = rows[-1]
    for s in range(segs):
        face((top[s], top[(s + 1) % segs], tip), 1.0)
        face((rows[0][(s + 1) % segs], rows[0][s], bot), 0.0)


def make(name, build):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(preview_material())
    return ob


_mat = None


def preview_material():
    """Emissive vertex-colour material so the .blend previews like the game."""
    global _mat
    if _mat:
        return _mat
    _mat = bpy.data.materials.new("FireVC")
    _mat.use_nodes = True
    nt = _mat.node_tree
    nt.nodes.clear()
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Color"
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = 2.0
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(attr.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return _mat


def flame_cluster(bm):
    # tall main tongue + three shorter ones leaning out around it: reads as a flame from any angle
    tongue(bm, 1.0, 0.27, 8, 9, twist=2.2, lean=(0.06, 0.03), seed=1)
    for i, (h, rad) in enumerate([(0.7, 0.19), (0.6, 0.17), (0.48, 0.15)]):
        a = i * 2 * math.pi / 3 + 0.4
        d = 0.13
        tongue(bm, h, rad, 6, 6, twist=-1.2 if i % 2 else 1.2,
               offset=(math.cos(a) * d, math.sin(a) * d, 0.0),
               tilt=(math.cos(a) * 0.32, math.sin(a) * 0.32), wobble=0.15, seed=10 + i)


def flame_small(bm):
    # twin tongues for candles / small torches
    tongue(bm, 1.0, 0.26, 7, 8, twist=2.0, lean=(0.05, 0.0), seed=3)
    tongue(bm, 0.6, 0.17, 6, 6, twist=-1.1, offset=(0.1, -0.04, 0), tilt=(0.25, -0.1), seed=4)


def flame_wisp(bm):
    # single cheap tongue for particles; pale so the particle ColorRamp does the colouring
    tongue(bm, 1.0, 0.32, 5, 5, twist=1.6, lean=(0.08, 0.0), stops=PALE, wobble=0.1, seed=5)


def fireball(bm):
    """Lumpy blob with flame spikes, for the flamethrower puffs (pale, tinted in game)."""
    rnd = random.Random(7)
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.42)
    col = bm.loops.layers.color.new("Color")
    for v in bm.verts:
        n = v.co.normalized()
        spike = 1.0 + (0.55 if rnd.random() < 0.3 else rnd.uniform(0.0, 0.18))
        spike += max(0.0, n.z) * 0.25  # flames lick upward
        v.co = n * 0.42 * spike
    for f in bm.faces:
        c = f.calc_center_median()
        u = min(1.0, max(0.0, (c.length - 0.35) / 0.45))
        for l in f.loops:
            l[col] = ramp(PALE, u)


bpy.ops.wm.read_factory_settings(use_empty=True)
objs = {
    "flame-cluster": make("flame-cluster", flame_cluster),
    "flame-small": make("flame-small", flame_small),
    "flame-wisp": make("flame-wisp", flame_wisp),
    "fireball": make("fireball", fireball),
}
for i, ob in enumerate(objs.values()):
    ob.location.x = i * 1.5  # side by side in the .blend for preview

for name, ob in objs.items():
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    loc = ob.location.copy()
    ob.location = (0, 0, 0)
    kw = dict(filepath=os.path.join(OUT, name + ".glb"), export_format="GLB", use_selection=True,
              export_apply=True, export_materials="NONE")
    try:
        bpy.ops.export_scene.gltf(**kw, export_vertex_color="ACTIVE")
    except TypeError:
        bpy.ops.export_scene.gltf(**kw, export_colors=True)
    ob.location = loc
    print("exported", name, len(ob.data.polygons), "faces")

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "assets", "fire", "fire.blend"))
