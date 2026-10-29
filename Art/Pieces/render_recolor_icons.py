"""
Renders a textured set's bench/promotion icons exactly like the cyberpunk ones (render_icons.py):
same scene, lights, camera angle and framing, transparent background. Each piece is the Unity FBX
with a Blender copy of the in-game recolour (Shaders/RecolorPiece): the original colours from
<Piece>_Color.jpg, with the main colour (<Piece>_Mask.png red) swapped for the side's colour at the
original's shading (green, halved), plus a rim glow.
Output: Resources/PieceIcons/<Set>/<Light|Dark>_<Piece>.png
Run:  Blender -b cyberpunk_pieces.blend --python render_recolor_icons.py -- <Resources/Pieces/Set> <Resources/PieceIcons/Set>
"""
import bpy, sys, os, math
from mathutils import Vector

SRC, OUT = sys.argv[sys.argv.index("--") + 1:][:2]
UNITY_SCALE = 1.6  # export_unity.py scaled the cyberpunk pieces by this; undo it to match the scene

def lin(c): return tuple(x ** 2.2 for x in c) + (1.0,)  # the game's colors are sRGB

# Side colour (PieceSets.Tint for Neon Glow) and rim glow
TEAMS = {
    "Light": (lin((0.88, 0.98, 1.0)), lin((0.16, 0.94, 1.0))),
    "Dark": (lin((1.0, 0.18, 0.6)), lin((1.0, 0.2, 0.8))),
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
    tint, rim = TEAMS[team]
    m = bpy.data.materials.new(f"Recolor_{team}_{piece}"); m.use_nodes = True
    nt = m.node_tree; p = nt.nodes["Principled BSDF"]
    colours = nt.nodes.new("ShaderNodeTexImage")
    colours.image = bpy.data.images.load(os.path.join(SRC, f"{piece}_Color.jpg"))
    mask = nt.nodes.new("ShaderNodeTexImage")
    mask.image = bpy.data.images.load(os.path.join(SRC, f"{piece}_Mask.png"))
    mask.image.colorspace_settings.name = 'Non-Color'
    split = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(mask.outputs["Color"], split.inputs[0])
    # The side's colour at the original's shading: tint * green * 2
    shade = nt.nodes.new("ShaderNodeMath"); shade.operation = 'MULTIPLY'; shade.inputs[1].default_value = 2.0
    nt.links.new(split.outputs[1], shade.inputs[0])
    side = nt.nodes.new("ShaderNodeMix"); side.data_type = 'RGBA'; side.blend_type = 'MULTIPLY'
    side.inputs[0].default_value = 1.0; side.inputs[6].default_value = tint
    nt.links.new(shade.outputs[0], side.inputs[7])
    swap = nt.nodes.new("ShaderNodeMix"); swap.data_type = 'RGBA'
    nt.links.new(split.outputs[0], swap.inputs[0])
    nt.links.new(colours.outputs["Color"], swap.inputs[6])
    nt.links.new(side.outputs[2], swap.inputs[7])
    nt.links.new(swap.outputs[2], p.inputs["Base Color"])
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
