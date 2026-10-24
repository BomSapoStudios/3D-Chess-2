"""
Imports the Mushroom Grove pieces (GLB files, untextured) and exports each to an FBX that
Unity loads from Resources/Pieces/Mushroom/<Piece>.fbx. The game colors each side solid.
Pieces are sized to the cyberpunk set (king 2.26 units tall, heights relative to the king
below), stand on z=0 centred on their base, and face +X like the other models.
Run:  Blender -b --python export_mushroom.py -- <glb_dir> <out_dir>
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

SRC, OUT = sys.argv[sys.argv.index("--") + 1:][:2]
KING = 2.26
HEIGHT = {"King": 1.0, "Queen": 0.92, "Bishop": 0.80, "Knight": 0.75, "Rook": 0.68, "Pawn": 0.55}
MAX_BASE = 1.6    # widest base allowed (a cell is 2.8)
MAX_SPAN = 2.2    # widest any part may reach
FILES = {"King": "mushroom king.glb", "Queen": "mushroom queen.glb", "Bishop": "mushroom bishop.glb", "Knight": "mushroom knight (1).glb", "Rook": "mushroom rook.glb", "Pawn": "mushroom pawn.glb"}

for name, file in FILES.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, file))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    # Bake parent transforms into the mesh
    ob.parent = None
    me = ob.data
    me.transform(ob.matrix_world); ob.matrix_world = Matrix.Identity(4)
    if name == "Knight":
        me.transform(Matrix.Rotation(math.pi / 2, 4, 'Z'))  # the frog looks along -Y; turn it to +X

    co = np.array([v.co[:] for v in me.vertices])
    zmin, zmax = co[:, 2].min(), co[:, 2].max()
    h = zmax - zmin
    low = co[co[:, 2] < zmin + 0.05 * h]
    cx, cy = (low[:, 0].min() + low[:, 0].max()) / 2, (low[:, 1].min() + low[:, 1].max()) / 2
    base = max(np.ptp(low[:, 0]), np.ptp(low[:, 1]))
    span = max(np.ptp(co[:, 0]), np.ptp(co[:, 1]))
    s = min(KING * HEIGHT[name] / h, MAX_BASE / base, MAX_SPAN / span)
    me.transform(Matrix.Translation((-cx, -cy, -zmin)))
    me.transform(Matrix.Scale(s, 4))
    print(f"PIECE {name}: height {h*s:.2f} base {base*s:.2f} span {span*s:.2f} scale {s:.3f}")

    # Detail map: luminance of the base color, 1024 px
    img = next((i for i in bpy.data.images if i.size[0] > 0), None)
    if img is not None:
        img.scale(1024, 1024)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(-1, 4)
        lum = 0.2126 * px[:, 0] + 0.7152 * px[:, 1] + 0.0722 * px[:, 2]
        lo, hi = np.percentile(lum, 2), np.percentile(lum, 98)
        lum = np.clip((lum - lo) / max(hi - lo, 1e-4), 0, 1)  # stretch so every piece uses the full range
        out = bpy.data.images.new(f"{name}_Detail", 1024, 1024)
        out.pixels[:] = np.repeat(lum[:, None], 4, axis=1).clip(0, 1).flatten() * np.tile([1, 1, 1, 0], 1024 * 1024) + np.tile([0, 0, 0, 1], 1024 * 1024)
        out.filepath_raw = os.path.join(OUT, f"{name}_Detail.png")
        out.file_format = 'PNG'
        out.save()

    ob.name = "Body"
    me.materials.clear()
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, f"{name}.fbx"), use_selection=True,
        apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
        axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True,
        mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False)
    print("EXPORTED", name)
