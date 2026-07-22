import bpy


bpy.ops.preferences.addon_enable(module="blender_mcp")
bpy.ops.wm.save_userpref()

bpy.context.scene.blendermcp_port = 9876
if not bpy.context.scene.blendermcp_server_running:
    bpy.ops.blendermcp.start_server()

print("BlenderMCP socket server requested on port 9876")
