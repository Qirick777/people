extends SceneTree
## 건물 시안을 렌더링해 docs/images/에 저장한다.
## 실행: xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 --script prototypes/building_look/render_preview.gd

const BB := preload("res://prototypes/building_look/building_builder.gd")
const BM := preload("res://prototypes/building_look/materials.gd")
const HB := preload("res://prototypes/human_look/human_builder.gd")
const OF := preload("res://prototypes/human_look/outfits.gd")
const VS := preload("res://prototypes/building_look/village.gd")
const OUT := "res://docs/images/"
const ISO := Vector3(-1.0, 1.25, 1.0)

var cam: Camera3D
var world: Node3D

# ---------------------------------------------------------------- 장면

func _init() -> void:
	world = Node3D.new()
	root.add_child(world)
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.3, 0.34, 0.28)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.72, 0.74, 0.8)
	e.ambient_light_energy = 0.42
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.environment = e
	world.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-48, -25, 0)
	sun.light_energy = 0.85
	sun.light_color = Color(1.0, 0.95, 0.86)
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 250.0
	world.add_child(sun)
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(1200, 1200)
	ground.mesh = pm
	var gm := StandardMaterial3D.new()
	gm.albedo_texture = HB.tex(HB.fabric(Color(0.3, 0.37, 0.21), 99, 256, 256, 0.0))
	gm.uv1_scale = Vector3(240, 240, 1)
	gm.roughness = 1.0
	ground.material_override = gm
	world.add_child(ground)
	cam = Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.far = 2000.0
	world.add_child(cam)

	await _row("building_styles.png", [VS.hut(), VS.cottage(), VS.timber2(), VS.stone2(), VS.mixed2(), VS.cottage(0.9)], Vector3(0, 0, 0))
	await _row("building_growth.png", [VS.hut(), VS.cottage(), VS.cottage_annex(), VS.farm_l(), VS.house_u(), VS.manor()], Vector3(0, 0, 200))
	await _civic(Vector3(200, 0, 0))
	await _village(Vector3(-200, 0, -200))
	quit()


func _snap(file: String) -> void:
	for i in 4:
		await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png(ProjectSettings.globalize_path(OUT + file))


func _aim(center: Vector3, size: float) -> void:
	cam.size = size
	cam.look_at_from_position(center + ISO.normalized() * 300.0, center)


## 건물을 화면 가로 방향(+x+z 대각선)으로 줄 세운다.
func _row(file: String, specs: Array, base: Vector3) -> void:
	var diag := Vector3(1, 0, 1).normalized()
	var s := 0.0
	var centers := []
	for sp in specs:
		var b: Dictionary = sp["blocks"][0]
		var ext := 0.0
		for bl in sp["blocks"]:
			ext = maxf(ext, maxf(bl["x"] + bl["w"], bl["z"] + bl["d"]))
		var node := BB.build(sp)
		node.position = base + diag * s - Vector3(ext * 0.5, 0, ext * 0.5) + diag * (ext * 0.7)
		world.add_child(node)
		centers.append(node.position + Vector3(ext * 0.5, 0, ext * 0.5))
		s += ext * 1.4 + 3.0
	var c: Vector3 = (centers[0] + centers[centers.size() - 1]) * 0.5
	_aim(c + Vector3(0, 3, 0), s * 0.58)
	await _snap(file)


func _tower(x: int, z: int, w: int, d: int, floors: int) -> Dictionary:
	return {"blocks": [{"x": x, "z": z, "w": w, "d": d, "floors": floors, "roof": "crenel"}],
		"ground": "stone", "upper": "stone", "narrow_windows": true, "plinth": 0.4, "seed": x * 7 + z}


func _civic(base: Vector3) -> void:
	var th := BB.build(VS.town_hall())
	th.position = base
	world.add_child(th)
	var castle := Node3D.new()
	castle.position = base + Vector3(24, 0, -4)
	world.add_child(castle)
	var wall := BM.get_mat("stone")
	BB.curtain(castle, Vector2(0, 0), Vector2(30, 0), 1.6, 7.0, wall)
	BB.curtain(castle, Vector2(0, 0), Vector2(0, 24), 1.6, 7.0, wall)
	BB.curtain(castle, Vector2(30, 0), Vector2(30, 24), 1.6, 7.0, wall)
	BB.curtain(castle, Vector2(0, 24), Vector2(12, 24), 1.6, 7.0, wall)
	BB.curtain(castle, Vector2(18, 24), Vector2(30, 24), 1.6, 7.0, wall)
	for t in [[-2, -2], [28, -2], [-2, 22], [28, 22]]:
		castle.add_child(BB.build(_tower(t[0], t[1], 4, 4, 4)))
	var gate := _tower(12, 22, 6, 4, 3)
	gate["doors"] = [[14, 25, 0, 1], [15, 25, 0, 1]]
	castle.add_child(BB.build(gate))
	var keep := _tower(11, 4, 9, 9, 4)
	keep["blocks"][0]["inner_roof"] = true
	keep["roof_mat"] = "slate"
	castle.add_child(BB.build(keep))
	castle.add_child(BB.build({"blocks": [{"x": 2, "z": 3, "w": 7, "d": 5}], "ground": "stone", "gable": "stone",
		"roof_mat": "slate", "pitch": 45.0, "chimney": true, "seed": 31}))
	castle.add_child(BB.build({"blocks": [{"x": 1, "z": 14, "w": 3, "d": 7, "roof": "lean", "lean": Vector2i(1, 0)}],
		"ground": "planks", "roof_mat": "shingle", "seed": 32}))
	castle.add_child(BB.build({"blocks": [{"x": 23, "z": 15, "w": 5, "d": 4}], "ground": "timber", "roof_mat": "shingle", "pitch": 50.0, "seed": 33}))
	_aim(base + Vector3(25, 5, 3), 44.0)
	await _snap("building_civic.png")


func _village(base: Vector3) -> void:
	VS.build(world, base)
	_aim(base + Vector3(23, 1, 1), 30.0)
	await _snap("building_village.png")
	_aim(base + Vector3(23.5, 0.5, -3.5), 9.0)
	await _snap("building_cutaway.png")
