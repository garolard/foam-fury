"""Shared helpers for the low-poly custom models in assets/custom-weapons/custom-weapons.blend.

Load from a generator script running inside Blender with that file open:
    exec(open(bpy.path.abspath("//../../tools/blender_lowpoly.py")).read())

Every part is flat-shaded and samples one uniform cell of the Kenney blaster-kit colormap
(material "colormap_custom"), so models match the kit's look. Build parts with
box / cyl / frustum_y / sphere / torus / star, then join_and_export(name).
"""
import bpy, bmesh, math, random
from mathutils import Matrix, Vector

# UVs of uniform cells in the Kenney blaster-kit colormap
PAL = {
    "black": (0.2588, 0.5088), "dark": (0.1338, 0.0088), "gun": (0.3838, 0.0088), "plum": (0.0713, 0.0244),
    "lav": (0.5713, 0.165), "lavdark": (0.5713, 0.0088), "white": (0.6338, 0.0088), "skyblue": (0.8838, 0.2588),
    "cream": (0.1338, 0.2588), "tan": (0.1963, 0.2588), "brown": (0.0713, 0.2588), "leather": (0.9463, 0.165),
    "red": (0.6963, 0.2588), "orange": (0.4463, 0.2588), "coral": (0.5713, 0.2588), "yellow": (0.3838, 0.2588),
    "gold": (0.3838, 0.2588), "lightyellow": (0.4463, 0.4932), "teal": (0.2588, 0.2588), "green": (0.3213, 0.2588),
    "blue": (0.7588, 0.2588), "purple": (0.0088, 0.5088), "pink": (0.1338, 0.5088), "copper": (0.8213, 0.0088),
}
FIESTA = ["pink", "yellow", "teal", "orange", "purple", "blue"]

MAT = bpy.data.materials["colormap_custom"]
T = Matrix.Translation
RX = lambda a: Matrix.Rotation(math.radians(a), 4, 'X')
RY = lambda a: Matrix.Rotation(math.radians(a), 4, 'Y')
RZ = lambda a: Matrix.Rotation(math.radians(a), 4, 'Z')
S = lambda x, y, z: Matrix.Diagonal((x, y, z, 1))
AX_Y = RX(-90)  # local +Z -> +Y
AX_X = RY(90)   # local +Z -> +X


class Builder:
    def __init__(self, wip="lowpoly-wip"):
        scn = bpy.context.scene
        self.coll = bpy.data.collections.get(wip) or bpy.data.collections.new(wip)
        if self.coll.name not in scn.collection.children:
            scn.collection.children.link(self.coll)
        for o in list(self.coll.objects):
            data = o.data
            bpy.data.objects.remove(o)
            if data and data.users == 0:
                bpy.data.meshes.remove(data)
        self.parts = []

    def finish(self, name, bm, color, bevel=0.0, seg=1):
        me = bpy.data.meshes.new(name)
        bm.normal_update()
        bm.to_mesh(me)
        bm.free()
        uv = me.uv_layers.new(name="UVMap")
        for l in uv.data:
            l.uv = PAL[color]
        me.materials.append(MAT)
        o = bpy.data.objects.new(name, me)
        self.coll.objects.link(o)
        if bevel > 0:
            m = o.modifiers.new("bev", "BEVEL")
            m.width, m.segments, m.limit_method, m.angle_limit = bevel, seg, 'ANGLE', math.radians(35)
        self.parts.append(o)
        return o

    def box(self, name, size, mat, color, bevel=0.0):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
        bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
        return self.finish(name, bm, color, bevel)

    def cyl(self, name, r1, r2, depth, segs, color, mat, bevel=0.0, caps=True):
        """Cone/cylinder along local Z (r1 at -Z end, r2 at +Z end), centred, then `mat`."""
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=caps, segments=segs, radius1=r1, radius2=r2, depth=depth)
        bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
        return self.finish(name, bm, color, bevel)

    def between(self, name, a, b, r1, r2, segs, color, bevel=0.0):
        """Cylinder from point a (radius r1) to point b (radius r2)."""
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        return self.cyl(name, r1, r2, d.length, segs, color, T((a + b) / 2) @ rot, bevel)

    def sphere(self, name, r, mat, color, segs=10, rings=6):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=r)
        bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
        return self.finish(name, bm, color)

    def torus(self, name, major, minor, mat, color, segs=16, ring=6):
        bm = bmesh.new()
        rows = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            rows.append([bm.verts.new(((major + minor * math.cos(2 * math.pi * j / ring)) * math.cos(a),
                                       (major + minor * math.cos(2 * math.pi * j / ring)) * math.sin(a),
                                       minor * math.sin(2 * math.pi * j / ring))) for j in range(ring)])
        for i in range(segs):
            a, b = rows[i], rows[(i + 1) % segs]
            for j in range(ring):
                bm.faces.new((a[j], b[j], b[(j + 1) % ring], a[(j + 1) % ring]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
        return self.finish(name, bm, color)

    def star(self, name, r_out, r_in, depth, mat, color, points=5):
        """Extruded star in the local XY plane, thickness along Z."""
        bm = bmesh.new()
        vs = []
        for i in range(points * 2):
            a = math.pi / 2 + math.pi * i / points
            r = r_out if i % 2 == 0 else r_in
            vs.append(bm.verts.new((r * math.cos(a), r * math.sin(a), -depth / 2)))
        f = bm.faces.new(vs)
        ext = bmesh.ops.extrude_face_region(bm, geom=[f])
        bmesh.ops.translate(bm, vec=(0, 0, depth), verts=[e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
        return self.finish(name, bm, color)

    def join_and_export(self, name):
        """Bake every part into one flat-shaded mesh `name`, export it to Models/GLB format/<name>.glb
        and keep it in the file with a fake user, outside the (empty) scene like the other guns."""
        glb = bpy.path.abspath("//Models/GLB format/" + name + ".glb")
        old = bpy.data.objects.get(name)
        if old:
            bpy.data.objects.remove(old)
        dg = bpy.context.evaluated_depsgraph_get()
        bm = bmesh.new()
        for o in self.parts:
            m = o.evaluated_get(dg).to_mesh()
            m.transform(o.matrix_world)
            bm.from_mesh(m)
            o.evaluated_get(dg).to_mesh_clear()
        for f in bm.faces:
            f.smooth = False
        me = bpy.data.meshes.get(name) or bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.clear()
        me.materials.append(MAT)
        for o in self.parts:
            data = o.data
            bpy.data.objects.remove(o)
            bpy.data.meshes.remove(data)
        obj = bpy.data.objects.new(name, me)
        self.coll.objects.link(obj)
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.export_scene.gltf(filepath=glb, use_selection=True, export_format='GLB', export_apply=True)
        self.coll.objects.unlink(obj)
        obj.use_fake_user = True
        bpy.data.collections.remove(self.coll)
        co = [v.co for v in me.vertices]
        return {"name": name, "glb": glb, "faces": len(me.polygons),
                "min": [round(min(c[i] for c in co), 3) for i in range(3)],
                "max": [round(max(c[i] for c in co), 3) for i in range(3)]}
