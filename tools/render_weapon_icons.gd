# Renders HUD icons (128x128, transparent) for the custom weapon models, matching the
# Kenney blaster-kit preview style. Needs a rendering driver (not --headless):
#   Godot --path . --script tools/render_weapon_icons.gd
extends SceneTree

const DIR := "res://assets/custom-weapons/"
const MODELS := ["blaster-mug", "blaster-flame", "blaster-mariachi"]
const SIZE := 512

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var vp := SubViewport.new()
	vp.size = Vector2i(SIZE, SIZE)
	vp.transparent_bg = true
	vp.msaa_3d = Viewport.MSAA_4X
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	vp.own_world_3d = true
	root.add_child(vp)

	var env := Environment.new()
	env.background_mode = Environment.BG_CLEAR_COLOR
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(1, 1, 1)
	env.ambient_light_energy = 0.55
	var we := WorldEnvironment.new()
	we.environment = env
	vp.add_child(we)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 30, 0)
	sun.light_energy = 1.1
	vp.add_child(sun)

	var cam := Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	vp.add_child(cam)

	# optional: render only the named models, e.g. `... --script tools/render_weapon_icons.gd -- blaster-mariachi`
	var models: Array = OS.get_cmdline_user_args()
	if models.is_empty():
		models = MODELS
	for name in models:
		var model: Node3D = load(DIR + "Models/GLB format/%s.glb" % name).instantiate()
		vp.add_child(model)
		var aabb := _aabb(model)
		var c := aabb.get_center()
		# 3/4 side view, muzzle (-Z) pointing right, slightly from above
		cam.position = c + Vector3(2.0, 1.1, -0.9)
		cam.look_at(c, Vector3.UP)
		cam.size = aabb.get_longest_axis_size() * 1.0
		for i in 4:
			await process_frame
		await RenderingServer.frame_post_draw
		var img := vp.get_texture().get_image()
		img.resize(128, 128, Image.INTERPOLATE_LANCZOS)
		var out := ProjectSettings.globalize_path(DIR + "Previews/%s.png" % name)
		print("saved ", out, " err=", img.save_png(out))
		model.queue_free()
		await process_frame
	quit()

func _aabb(n: Node) -> AABB:
	var box := AABB()
	var first := true
	for mi in n.find_children("*", "MeshInstance3D", true, false):
		var b: AABB = mi.global_transform * mi.get_aabb()
		box = b if first else box.merge(b)
		first = false
	return box
