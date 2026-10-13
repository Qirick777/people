extends SceneTree
## 블록풍 시안. 실행 인자로 --cute 를 주면 귀여움형으로 그린다.
## 실행: xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 --script prototypes/blocky_look/render_blocky.gd -- [--cute]

const BH := preload("res://prototypes/blocky_look/blocky_human.gd")
const BB := preload("res://prototypes/building_look/building_builder.gd")
const BM := preload("res://prototypes/building_look/materials.gd")
const VS := preload("res://prototypes/building_look/village.gd")
const OUT := "/tmp/claude-0/blocky/"
const ISO := Vector3(-1.0, 1.25, 1.0)

var cam: Camera3D
var world: Node3D
var cute := false


func _init() -> void:
	cute = "--cute" in OS.get_cmdline_user_args()
	var tag := "cute" if cute else "basic"
	BM.flat = true
	BB.round_r = 0.07 if cute else 0.0
	DirAccess.make_dir_recursive_absolute(OUT)
	world = Node3D.new()
	root.add_child(world)
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.4, 0.54, 0.29)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.78, 0.8, 0.88)
	e.ambient_light_energy = 0.32
	e.tonemap_mode = Environment.TONE_MAPPER_ACES
	e.tonemap_exposure = 0.95
	env.environment = e
	world.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, -30, 0)
	sun.light_energy = 0.72
	sun.light_color = Color(1.0, 0.95, 0.86)
	sun.shadow_enabled = true
	sun.shadow_opacity = 0.6
	world.add_child(sun)
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(800, 800)
	ground.mesh = pm
	ground.material_override = BM.flat_mat(Color(0.4, 0.54, 0.29))
	world.add_child(ground)
	cam = Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.far = 2000.0
	world.add_child(cam)

	# 사람 줄
	var specs := BH.lineup()
	for i in specs.size():
		var p := BH.build(specs[i], cute)
		p.position = Vector3(100 + i * 0.85, 0, 100)
		world.add_child(p)
	var mid := Vector3(100 + 4.5 * 0.85, 0.8, 100)
	cam.projection = Camera3D.PROJECTION_PERSPECTIVE
	cam.fov = 22
	cam.look_at_from_position(mid + Vector3(0, 2.2, 12.5), mid)
	await _snap("people_front_%s.png" % tag)
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.size = 5.0
	cam.look_at_from_position(mid + ISO.normalized() * 300.0, mid)
	await _snap("people_iso_%s.png" % tag)

	# 마을
	var rng := RandomNumberGenerator.new()
	rng.seed = 5
	VS.build(world, Vector3.ZERO, func(r: RandomNumberGenerator, i: int) -> Node3D: return BH.random(r, cute))
	_aim(Vector3(23, 1, 1), 30.0)
	await _snap("village_%s.png" % tag)
	_aim(Vector3(22, 0.5, -3), 11.0)
	await _snap("close_%s.png" % tag)
	quit()


func _aim(center: Vector3, size: float) -> void:
	cam.size = size
	cam.look_at_from_position(center + ISO.normalized() * 300.0, center)


func _snap(file: String) -> void:
	for i in 6:
		await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png(OUT + file)
