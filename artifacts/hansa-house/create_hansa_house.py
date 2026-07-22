import math
import os
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(r"C:\Users\EJOHDAV\source\repos\Virtual Company\artifacts\hansa-house")
TEXTURE_DIR = ROOT / "textures"
ATLAS = TEXTURE_DIR / "hansa_citizen_house_atlas.png"
OUT_BLEND = ROOT / "hansa_citizen_house.blend"
OUT_GLB = ROOT / "hansa_citizen_house.glb"
OUT_PREVIEW = ROOT / "hansa_citizen_house_preview.png"


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    bpy.context.scene.unit_settings.system = "METRIC"
    engines = {item.identifier for item in bpy.context.scene.render.bl_rna.properties["engine"].enum_items}
    bpy.context.scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    if hasattr(bpy.context.scene, "eevee"):
        bpy.context.scene.eevee.taa_render_samples = 64
    bpy.context.scene.world.color = (0.66, 0.73, 0.78)


def crop_atlas():
    image = bpy.data.images.load(str(ATLAS), check_existing=True)
    width, height = image.size
    pixels = list(image.pixels[:])
    quads = {
        "plaster": (0, height // 2, width // 2, height),
        "timber": (width // 2, height // 2, width, height),
        "roof_tiles": (0, 0, width // 2, height // 2),
        "stone": (width // 2, 0, width, height // 2),
    }

    outputs = {}
    for name, (x0, y0, x1, y1) in quads.items():
        cw, ch = x1 - x0, y1 - y0
        cropped = bpy.data.images.new(name=f"hansa_{name}", width=cw, height=ch, alpha=True)
        out = [0.0] * (cw * ch * 4)
        for y in range(ch):
            src_y = y0 + y
            for x in range(cw):
                src_x = x0 + x
                src = (src_y * width + src_x) * 4
                dst = (y * cw + x) * 4
                out[dst:dst + 4] = pixels[src:src + 4]
        cropped.pixels.foreach_set(out)
        cropped.filepath_raw = str(TEXTURE_DIR / f"hansa_{name}.png")
        cropped.file_format = "PNG"
        cropped.save()
        outputs[name] = cropped.filepath_raw
    return outputs


def textured_material(name, image_path, roughness=0.75, scale=(1.0, 1.0, 1.0)):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = roughness

    image = nodes.new("ShaderNodeTexImage")
    image.image = bpy.data.images.load(str(image_path), check_existing=True)
    image.extension = "REPEAT"
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = scale
    coords = nodes.new("ShaderNodeTexCoord")
    mat.node_tree.links.new(coords.outputs["Generated"], mapping.inputs["Vector"])
    mat.node_tree.links.new(mapping.outputs["Vector"], image.inputs["Vector"])
    mat.node_tree.links.new(image.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def color_material(name, color, roughness=0.7):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def cube(name, loc, scale, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if mat:
        obj.data.materials.append(mat)
    bevel = obj.modifiers.new("soft_game_edges", "BEVEL")
    bevel.width = 0.025
    bevel.segments = 1
    obj.modifiers.new("weighted_normals", "WEIGHTED_NORMAL")
    return obj


def roof_prism(name, y, z, width, depth, height, mat):
    verts = [
        (-width / 2, -depth / 2, 0), (width / 2, -depth / 2, 0), (0, -depth / 2, height),
        (-width / 2, depth / 2, 0), (width / 2, depth / 2, 0), (0, depth / 2, height),
    ]
    faces = [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)]
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = (0, y, z)
    obj.data.materials.append(mat)
    bevel = obj.modifiers.new("worn_roof_edges", "BEVEL")
    bevel.width = 0.02
    bevel.segments = 1
    obj.modifiers.new("weighted_normals", "WEIGHTED_NORMAL")
    return obj


def add_beam(name, loc, scale, rotation_z, mat):
    obj = cube(name, loc, scale, mat)
    obj.rotation_euler[2] = math.radians(rotation_z)
    return obj


def add_window(name, x, y, z, mat_frame, mat_glass):
    cube(name + "_recess", (x, y - 0.011, z), (0.54, 0.045, 0.68), mat_frame)
    pane = cube(name + "_glass", (x, y - 0.038, z), (0.38, 0.035, 0.48), mat_glass)
    pane.modifiers["soft_game_edges"].width = 0.01
    cube(name + "_mullion_v", (x, y - 0.06, z), (0.045, 0.05, 0.55), mat_frame)
    cube(name + "_mullion_h", (x, y - 0.061, z), (0.42, 0.05, 0.045), mat_frame)
    cube(name + "_left_shutter", (x - 0.42, y - 0.03, z), (0.16, 0.045, 0.62), mat_frame)
    cube(name + "_right_shutter", (x + 0.42, y - 0.03, z), (0.16, 0.045, 0.62), mat_frame)


def build_house(materials):
    stone = materials["stone"]
    plaster = materials["plaster"]
    timber = materials["timber"]
    roof = materials["roof"]
    door = materials["door"]
    glass = materials["glass"]
    metal = materials["metal"]

    cube("stone_foundation", (0, 0, 0.35), (4.2, 3.3, 0.7), stone)
    cube("lower_plaster_walls", (0, 0, 1.55), (3.75, 2.9, 1.75), plaster)
    cube("upper_overhanging_walls", (0, -0.08, 3.0), (4.15, 3.1, 1.45), plaster)
    roof_prism("steep_red_tile_gable_roof", 0, 3.65, 4.75, 3.55, 1.7, roof)

    for x in (-1.95, 0, 1.95):
        add_beam(f"front_vertical_beam_{x}", (x, -1.595, 2.7), (0.16, 0.13, 2.85), 0, timber)
    for z in (1.05, 2.25, 3.45):
        add_beam(f"front_horizontal_beam_{z}", (0, -1.61, z), (4.25, 0.14, 0.14), 0, timber)
    add_beam("front_left_diagonal_brace", (-0.95, -1.62, 2.85), (0.15, 0.14, 1.95), -31, timber)
    add_beam("front_right_diagonal_brace", (0.95, -1.62, 2.85), (0.15, 0.14, 1.95), 31, timber)

    for x in (-2.1, 2.1):
        add_beam(f"side_corner_post_{x}", (x, 0, 2.45), (0.18, 3.25, 0.18), 0, timber)
    for y in (-0.85, 0.85):
        add_beam(f"side_roof_tie_{y}", (0, y, 3.52), (4.25, 0.15, 0.14), 0, timber)

    cube("arched_front_door", (0, -1.66, 1.04), (0.78, 0.13, 1.28), door)
    cube("door_lintel", (0, -1.71, 1.72), (0.95, 0.15, 0.14), timber)
    cube("door_handle", (0.27, -1.75, 1.08), (0.06, 0.035, 0.06), metal)

    add_window("lower_left_window", -1.18, -1.66, 1.48, timber, glass)
    add_window("lower_right_window", 1.18, -1.66, 1.48, timber, glass)
    add_window("upper_left_window", -1.1, -1.69, 2.9, timber, glass)
    add_window("upper_right_window", 1.1, -1.69, 2.9, timber, glass)
    add_window("small_gable_window", 0, -1.73, 3.95, timber, glass)

    cube("stone_front_step", (0, -2.02, 0.43), (1.15, 0.55, 0.18), stone)
    cube("chimney_stack", (1.28, 0.65, 4.85), (0.42, 0.42, 1.2), stone)
    cube("chimney_cap", (1.28, 0.65, 5.5), (0.58, 0.58, 0.16), stone)
    cube("iron_hanging_bracket", (-1.85, -1.76, 2.25), (0.07, 0.08, 0.7), metal)
    cube("plain_wood_shop_sign", (-1.55, -1.87, 2.08), (0.48, 0.08, 0.34), door)

    cube("roof_ridge_cap", (0, 0, 5.32), (0.36, 3.52, 0.16), roof)

    empty = bpy.data.objects.new("asset_notes", None)
    empty["asset"] = "Hanseatic citizen living house"
    empty["style"] = "stylized low-poly/isometric game prop"
    empty["texture_source"] = str(ATLAS)
    bpy.context.collection.objects.link(empty)


def setup_camera_and_light():
    bpy.ops.object.light_add(type="SUN", location=(0, -4, 7))
    sun = bpy.context.object
    sun.name = "soft_northern_sun"
    sun.data.energy = 2.2
    sun.rotation_euler = (math.radians(42), 0, math.radians(32))

    bpy.ops.object.camera_add(location=(5.6, -7.2, 5.2), rotation=(math.radians(60), 0, math.radians(40)))
    camera = bpy.context.object
    direction = Vector((0, 0, 2.35)) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 45
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 6.4
    bpy.context.scene.camera = camera


def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)
    reset_scene()
    crops = crop_atlas()
    mats = {
        "plaster": textured_material("lime_plaster_from_imagegen", crops["plaster"], 0.88, (1.5, 1.5, 1)),
        "timber": textured_material("dark_oak_from_imagegen", crops["timber"], 0.7, (1.3, 1.3, 1)),
        "roof": textured_material("red_clay_roof_tiles_from_imagegen", crops["roof_tiles"], 0.82, (1.0, 2.0, 1)),
        "stone": textured_material("cobblestone_foundation_from_imagegen", crops["stone"], 0.86, (1.4, 1.4, 1)),
        "door": color_material("aged_plain_door_wood", (0.30, 0.16, 0.08, 1), 0.74),
        "glass": color_material("small_leaded_window_glass", (0.36, 0.53, 0.61, 0.72), 0.3),
        "metal": color_material("dark_wrought_iron", (0.035, 0.032, 0.03, 1), 0.55),
    }
    build_house(mats)
    setup_camera_and_light()

    bpy.ops.wm.save_as_mainfile(filepath=str(OUT_BLEND))
    bpy.ops.export_scene.gltf(filepath=str(OUT_GLB), export_format="GLB", export_yup=True)
    bpy.context.scene.render.resolution_x = 1600
    bpy.context.scene.render.resolution_y = 1200
    bpy.context.scene.render.filepath = str(OUT_PREVIEW)
    bpy.ops.render.render(write_still=True)
    print(f"Created {OUT_BLEND}")
    print(f"Created {OUT_GLB}")
    print(f"Created {OUT_PREVIEW}")


if __name__ == "__main__":
    main()
