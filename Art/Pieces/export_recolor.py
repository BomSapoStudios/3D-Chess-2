"""
Recolour maps for the textured piece sets, so a piece wears its side's colour in place of its own
main colour and keeps every other colour of its original design (eyes, suits, spots, stripes).

For each piece it writes, next to the model in Resources/Pieces/<Set>/:
  <Piece>_Color.jpg  the original base-colour texture, 1024 px
  <Piece>_Mask.png   R: how much a texel is the piece's main colour (0..1, soft at the edges)
                     G: its brightness relative to the main colour, halved (0.5 = as bright), so
                        the side's colour keeps the original shading
The main colour is the biggest of a few colour clusters over the model's surface (points spread
over it by area, coloured from the texture), unless PRIMARY picks another (by cluster rank,
0 = biggest) for a piece where the biggest isn't the body.
Run:  Blender -b --python export_recolor.py -- <chess-sets dir> <Assets/Resources/Pieces dir> [Set ...]
"""
import bpy, sys, os, json, struct
import numpy as np

SETS = {
    "Aliens": ("aliens", {"King": "alien king.glb", "Queen": "alien queen.glb", "Bishop": "alien bishop.glb", "Knight": "alien knight.glb", "Rook": "alien rook.glb", "Pawn": "alien pawn.glb"}),
    "Abyss": ("abyss", {"King": "king.glb", "Queen": "Queen.glb", "Bishop": "bishop.glb", "Knight": "knight.glb", "Rook": "rook.glb", "Pawn": "pawn.glb"}),
    "Mushroom": ("mushroom", {"King": "mushroom king.glb", "Queen": "mushroom queen.glb", "Bishop": "mushroom bishop.glb", "Knight": "mushroom knight (1).glb", "Rook": "mushroom rook.glb", "Pawn": "mushroom pawn.glb"}),
    "Wild": ("animals", {"King": "animal king.glb", "Queen": "animal queen.glb", "Bishop": "animal bishop.glb", "Knight": "animal knight.glb", "Rook": "animal rook.glb", "Pawn": "animal pawn.glb"}),
    "Robots": ("robot", {"King": "robot king.glb", "Queen": "robot queen.glb", "Bishop": "robot bishop.glb", "Knight": "robot knight.glb", "Rook": "robot rook.glb", "Pawn": "robot pawn.glb"}),
}
# ("Set", "Piece"): cluster rank to use instead of the biggest (CLUSTERS in the output lists them)
PRIMARY = {
    ("Aliens", "Queen"): 1,   # the gown, not the dark base
    ("Aliens", "Pawn"): 2,    # the green alien, not the grey base
}
K = 6              # colour clusters per texture
NEAR, FAR = 0.045, 0.09   # colour distance (OKLab) fully in / fully out of the main colour
# ("Set", "Piece"): NEAR and FAR scaled, for a main colour spread over several shades
TOLERANCE = {
    ("Aliens", "King"): 2.2,  # the robe runs from tan to deep brown
}

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
ONLY = args[2:] or list(SETS)


def glb_image(path, tmp):
    """The GLB's (only) embedded base-colour image, written to tmp"""
    b = open(path, "rb").read()
    n = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + n])
    off = 20 + n
    blob = b[off + 8:off + 8 + struct.unpack("<I", b[off:off + 4])[0]]
    view = j["bufferViews"][j["images"][0]["bufferView"]]
    start = view.get("byteOffset", 0)
    open(tmp, "wb").write(blob[start:start + view["byteLength"]])
    return tmp


def oklab(rgb):
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    m1 = np.array([[0.4122214708, 0.5363325363, 0.0514459929], [0.2119034982, 0.6806995451, 0.1073969566], [0.0883024619, 0.2817188376, 0.6299787005]])
    m2 = np.array([[0.2104542553, 0.7936177850, -0.0040720468], [1.9779984951, -2.4285922050, 0.4505937099], [0.0259040371, 0.7827717662, -0.8086757660]])
    return np.cbrt(lin @ m1.T) @ m2.T


def kmeans(x, k, iters=20):
    rng = np.random.default_rng(1)
    c = x[rng.choice(len(x), k, replace=False)]
    for _ in range(iters):
        lab = np.argmin(((x[:, None, :] - c[None]) ** 2).sum(-1), axis=1)
        c = np.array([x[lab == i].mean(0) if (lab == i).any() else c[i] for i in range(k)])
    return c, np.bincount(lab, minlength=k)


for set_name in ONLY:
    folder, files = SETS[set_name]
    for piece, file in files.items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = os.path.join(SRC, folder, file)
        img = bpy.data.images.load(glb_image(path, os.path.join(bpy.app.tempdir, "tex_" + piece)))
        img.scale(1024, 1024)
        rgb = np.array(img.pixels[:], dtype=np.float32).reshape(-1, 4)[:, :3]
        lab = oklab(np.clip(rgb, 0, 1))
        # Points over the model's surface, by area, coloured from the texture
        bpy.ops.import_scene.gltf(filepath=path)
        ob = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
        me = ob.data
        me.calc_loop_triangles()
        uv = np.array([d.uv[:] for d in me.uv_layers.active.data])
        co = np.array([v.co[:] for v in me.vertices])
        tris = np.array([t.loops[:] for t in me.loop_triangles])
        tv = np.array([t.vertices[:] for t in me.loop_triangles])
        p = co[tv]
        area = np.linalg.norm(np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]), axis=1)
        rng = np.random.default_rng(2)
        pick_t = rng.choice(len(tris), 20000, p=area / area.sum())
        a, b = rng.random(20000), rng.random(20000)
        flip = a + b > 1
        a[flip], b[flip] = 1 - a[flip], 1 - b[flip]
        t_uv = uv[tris[pick_t]]
        at = t_uv[:, 0] + a[:, None] * (t_uv[:, 1] - t_uv[:, 0]) + b[:, None] * (t_uv[:, 2] - t_uv[:, 0])
        px = (np.clip(at[:, 1] % 1, 0, 0.9999) * 1024).astype(int) * 1024 + (np.clip(at[:, 0] % 1, 0, 0.9999) * 1024).astype(int)
        # Cluster on colour with brightness counting less, so a colour's shades stay together
        w = np.array([0.5, 1, 1])
        centres, sizes = kmeans(lab[px] * w, K)
        order = np.argsort(-sizes)
        pick = order[PRIMARY.get((set_name, piece), 0)]
        main = centres[pick] / w
        d = np.sqrt((((lab - main) * np.array([0.5, 1, 1])) ** 2).sum(1))
        k = TOLERANCE.get((set_name, piece), 1.0)
        mask = np.clip((FAR * k - d) / ((FAR - NEAR) * k), 0, 1)
        lum = 0.2126 * rgb[:, 0] + 0.7152 * rgb[:, 1] + 0.0722 * rgb[:, 2]
        main_lum = np.median(lum[mask > 0.9]) if (mask > 0.9).any() else lum.mean()
        shade = np.clip(lum / max(main_lum, 1e-3) / 2, 0, 1)
        share = (mask[px] > 0.5).mean()
        main_rgb = np.median(rgb[mask > 0.9], axis=0) if (mask > 0.9).any() else rgb.mean(0)
        labels = np.argmin((((lab[px] * w)[:, None, :] - centres[None]) ** 2).sum(-1), axis=1)
        srgb = lambda c: np.round(np.median(rgb[px][labels == c], axis=0) * 255).astype(int).tolist() if (labels == c).any() else None
        print("CLUSTERS", set_name, piece, [(r, srgb(c), int(sizes[c] * 100 // sizes.sum())) for r, c in enumerate(order)])
        print(f"RECOLOR {set_name} {piece}: main colour {np.round(main_rgb * 255).astype(int)} covers {share:.0%} of the surface; clusters {sizes[order] * 100 // sizes.sum()}")

        dest = os.path.join(OUT, set_name)
        colour = bpy.data.images.new(f"{piece}_Color", 1024, 1024)
        colour.pixels[:] = np.concatenate([rgb, np.ones((len(rgb), 1), np.float32)], 1).flatten()
        colour.filepath_raw = os.path.join(dest, f"{piece}_Color.jpg")
        colour.file_format = 'JPEG'
        bpy.context.scene.render.image_settings.quality = 92
        colour.save()
        m = bpy.data.images.new(f"{piece}_Mask", 1024, 1024)
        m.pixels[:] = np.stack([mask, shade, np.zeros_like(mask), np.ones_like(mask)], 1).astype(np.float32).flatten()
        m.filepath_raw = os.path.join(dest, f"{piece}_Mask.png")
        m.file_format = 'PNG'
        m.save()
