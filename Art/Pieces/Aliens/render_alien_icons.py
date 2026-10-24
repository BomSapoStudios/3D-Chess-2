"""
Renders the Close Encounters bench/promotion icons exactly like the cyberpunk ones (render_icons.py):
same scene, lights, camera angle and framing, transparent background. Each piece is the
Unity FBX (Resources/Pieces/Aliens) with a Blender copy of the in-game material: the grayscale
detail map between the side's two tints, plus a rim glow.
Output: Resources/PieceIcons/Aliens/<Light|Dark>_<Piece>.png
Run:  Blender -b ../cyberpunk_pieces.blend --python render_alien_icons.py -- <aliens_dir> <out_dir>
"""
import bpy, sys, os, math
from mathutils import Vector

SRC, OUT = sys.argv[sys.argv.index("--") + 1:][:2]
UNITY_SCALE = 1.6  # export_unity.py scaled the cyberpunk pieces by this; undo it to match the scene

def lin(c): return tuple(x ** 2.2 for x in c) + (1.0,)  # the game's colors are sRGB

TEAMS = {
    "Light": (lin((0.42, 0.5, 0.5)), lin((0.95, 0.98, 0.96)), lin((0.16, 0.94, 1.0))),
    "Dark": (lin((0.03, 0.01, 0.05)), lin((0.28, 0.12, 0.4)), lin((1.0, 0.2, 0.8))),
}

scene = bpy.context.scene
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = 512
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
cam = scene.camera
cam.data.type = 'ORTHO'
cam.data.ortho_scale = 1.75
for o in scene.objects:
    if o.type == 'MESH': o.hide_render = True

def material(team, piece):
    dark, light, rim = TEAMS[team]
    m = bpy.data.materials.new(f"Alien_{team}_{piece}"); m.use_nodes = True
    nt = m.node_tree; p = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(SRC, f"{piece}_Detail.png"))
    tex.image.colorspace_settings.name = 'Non-Color'
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'
    mix.inputs[6].default_value = dark; mix.inputs[7].default_value = light
    nt.links.new(tex.outputs["Color"], mix.inputs[0])
    nt.links.new(mix.outputs[2], p.inputs["Base Color"])
    p.inputs["Metallic"].default_value = 0.2
    p.inputs["Roughness"].default_value = 0.45
    # Rim glow: facing ratio, tightened, drives the emission strength
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    pw = nt.nodes.new("ShaderNodeMath"); pw.operation = 'POWER'; pw.inputs[1].default_value = 2.6
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = 1.3
    nt.links.new(lw.outputs["Facing"], pw.inputs[0]); nt.links.new(pw.outputs[0], mul.inputs[0])
    p.inputs["Emission Color"].default_value = rim
    nt.links.new(mul.outputs[0], p.inputs["Emission Strength"])
    return m

for team in TEAMS:
    for piece in ["Pawn", "Knight", "Bishop", "Rook", "Queen", "King"]:
        spot = bpy.data.objects[f"{team}_{piece}_Body"].location.copy()
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, f"{piece}.fbx"))
        new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
        for o in new:
            o.scale = [s / UNITY_SCALE for s in o.scale]
            o.location = spot
            o.data.materials.clear(); o.data.materials.append(material(team, piece))
            o.hide_render = False
        target = spot + Vector((0, 0, 0.68))
        direction = Vector((math.cos(math.radians(-70)), math.sin(math.radians(-70)), 0.45)).normalized()
        cam.location = target + direction * 6
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(OUT, f"{team}_{piece}.png")
        bpy.ops.render.render(write_still=True)
        for o in new: bpy.data.objects.remove(o, do_unlink=True)
        print("ICON", team, piece)
