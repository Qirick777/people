extends SceneTree
## 건물 시안을 렌더링해 docs/images/에 저장한다.
## 실행: xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 --script prototypes/building_look/render_preview.gd

const BB := preload("res://prototypes/building_look/building_builder.gd")
const BM := preload("res://prototypes/building_look/materials.gd")
const HB := preload("res://prototypes/human_look/human_builder.gd")
const OF := preload("res://prototypes/human_look/outfits.gd")
const OUT := "res://docs/images/"
const ISO := Vector3(-1.0, 1.25, 1.0)

var cam: Camera3D
var world: Node3D

const GREEN := Color(0.33, 0.42, 0.26)
const RED := Color(0.5, 0.18, 0.14)
const BLUE := Color(0.24, 0.32, 0.45)


# ---------------------------------------------------------------- 사양

func hut() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 4, "d": 3}], "ground": "planks", "roof_mat": "thatch", "pitch": 50.0, "plinth": 0.15, "seed": 1}

func cottage(age := 0.0) -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 4}], "ground": "daub", "roof_mat": "thatch", "pitch": 50.0, "chimney": true, "shutter": GREEN, "age": age, "seed": 2}

func timber2() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 5, "floors": 2}], "ground": "timber", "roof_mat": "shingle", "pitch": 52.0, "chimney": true, "shutter": RED, "seed": 3}

func stone2() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 7, "d": 5, "floors": 2}], "ground": "stone", "roof_mat": "slate", "pitch": 42.0, "chimney": true, "seed": 4}

func mixed2() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 5, "floors": 2}], "ground": "stone", "upper": "timber", "roof_mat": "tile", "pitch": 45.0, "shutter": BLUE, "seed": 5}

func cottage_annex() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 4}, {"x": 6, "z": 1, "w": 3, "d": 3, "roof": "lean", "lean": Vector2i(1, 0), "ground": "planks"}],
		"ground": "daub", "roof_mat": "thatch", "pitch": 50.0, "chimney": true, "shutter": GREEN, "seed": 6}

func farm_l() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 8, "d": 5, "floors": 2}, {"x": 5, "z": 5, "w": 3, "d": 4, "ridge": "z"}],
		"ground": "stone", "upper": "timber", "gable": "timber", "roof_mat": "shingle", "pitch": 50.0, "chimney": true, "shutter": RED, "seed": 7}

func house_u() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 10, "d": 5, "floors": 2}, {"x": 0, "z": 5, "w": 3, "d": 4, "floors": 2, "ridge": "z"},
		{"x": 7, "z": 5, "w": 3, "d": 4, "floors": 2, "ridge": "z"}],
		"ground": "stone", "upper": "timber", "gable": "timber", "roof_mat": "tile", "pitch": 48.0, "chimney": true, "shutter": BLUE, "seed": 8}

func manor() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 12, "d": 5, "floors": 2}, {"x": 0, "z": 5, "w": 3, "d": 5, "floors": 2, "ridge": "z"},
		{"x": 9, "z": 5, "w": 3, "d": 5, "floors": 2, "ridge": "z"}, {"x": 0, "z": 10, "w": 12, "d": 3}],
		"doors": [[5, 4, 0, 1], [6, 12, 0, 1]],
		"ground": "stone", "upper": "timber", "gable": "timber", "roof_mat": "slate", "pitch": 48.0, "chimney": true, "shutter": RED, "seed": 9}

func town_hall() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 14, "d": 5, "floors": 2}, {"x": 0, "z": 5, "w": 4, "d": 5, "floors": 2, "ridge": "z"},
		{"x": 10, "z": 5, "w": 4, "d": 5, "floors": 2, "ridge": "z"}, {"x": 0, "z": 10, "w": 14, "d": 4, "floors": 2},
		{"x": 6, "z": 12, "w": 3, "d": 3, "floors": 4, "roof": "pyramid", "ground": "stone_light", "upper": "stone_light", "no_windows": false}],
		"doors": [[7, 14, 0, 1]], "ground": "stone_light", "upper": "timber", "gable": "timber", "roof_mat": "slate", "pitch": 50.0,
		"shutter": RED, "seed": 10}

func cut_house() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 7, "d": 5}], "ground": "timber", "roof_mat": "thatch", "cutaway": true, "seed": 11,
		"partitions": [[4.0, 0.2, 4.0, 3.2]],
		"furniture": [
			[Vector3(1.0, 0.45, 2.0), Vector3(5.4, 0.22, 1.3), "wood"], [Vector3(0.9, 0.12, 1.8), Vector3(5.4, 0.5, 1.3), "linen"],
			[Vector3(1.0, 0.45, 2.0), Vector3(6.4, 0.22, 3.6), "wood"], [Vector3(0.9, 0.12, 1.8), Vector3(6.4, 0.5, 3.6), "linen"],
			[Vector3(1.6, 0.75, 0.8), Vector3(2.0, 0.37, 2.8), "wood"], [Vector3(1.6, 0.42, 0.3), Vector3(2.0, 0.21, 3.5), "wood"],
			[Vector3(1.6, 0.42, 0.3), Vector3(2.0, 0.21, 2.1), "wood"],
			[Vector3(1.3, 0.7, 0.9), Vector3(0.9, 0.35, 0.65), "stone"], [Vector3(0.7, 0.08, 0.5), Vector3(0.9, 0.72, 0.65), "ember"],
			[Vector3(0.5, 0.8, 0.5), Vector3(3.4, 0.4, 0.6), "wood"], [Vector3(0.5, 0.8, 0.5), Vector3(3.4, 0.4, 1.2), "wood"]]}


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

	await _row("building_styles.png", [hut(), cottage(), timber2(), stone2(), mixed2(), cottage(0.9)], Vector3(0, 0, 0))
	await _row("building_growth.png", [hut(), cottage(), cottage_annex(), farm_l(), house_u(), manor()], Vector3(0, 0, 200))
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
	var th := BB.build(town_hall())
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


func _tree(pos: Vector3, rng: RandomNumberGenerator) -> void:
	var h := rng.randf_range(2.2, 3.4)
	var trunk := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = 0.14
	cm.bottom_radius = 0.22
	cm.height = h
	cm.radial_segments = 7
	trunk.mesh = cm
	trunk.material_override = BM.get_mat("wood")
	trunk.position = pos + Vector3(0, h * 0.5, 0)
	world.add_child(trunk)
	for k in 3:
		var leaf := MeshInstance3D.new()
		var sm := SphereMesh.new()
		var r := rng.randf_range(1.1, 1.7) * (1.0 - k * 0.2)
		sm.radius = r
		sm.height = r * 1.6
		sm.radial_segments = 7
		sm.rings = 4
		leaf.mesh = sm
		leaf.material_override = BM.get_mat("leaf")
		leaf.position = pos + Vector3(rng.randf_range(-0.5, 0.5), h + k * 0.9, rng.randf_range(-0.5, 0.5))
		world.add_child(leaf)


func _person(pos: Vector3, rng: RandomNumberGenerator, i: int) -> void:
	var female := rng.randf() < 0.5
	var cloth := [OF.LINEN, OF.BROWN, OF.RUSSET, OF.WOAD, OF.GREEN, OF.GREY]
	var c1: Color = cloth[rng.randi() % cloth.size()]
	var c2: Color = cloth[rng.randi() % cloth.size()]
	var hairs := [Color(0.1, 0.08, 0.07), Color(0.3, 0.2, 0.12), Color(0.78, 0.64, 0.38), Color(0.62, 0.26, 0.12)]
	var skins := [Color(0.8, 0.63, 0.53), Color(0.75, 0.58, 0.46), Color(0.68, 0.5, 0.38)]
	var d := {"outfit": OF.peasant_woman(c1) if female else OF.peasant_man(c1, c2), "female": female, "seed": 300 + i,
		"skin": skins[rng.randi() % skins.size()], "hair": "bun" if female else "short",
		"hair_color": hairs[rng.randi() % hairs.size()], "height": rng.randf_range(0.94, 1.06)}
	if female and rng.randf() < 0.5: d["hat"] = "coif"
	var p := HB.build(d)
	p.position = pos
	p.rotation.y = rng.randf_range(-PI, PI)
	world.add_child(p)


func _village(base: Vector3) -> void:
	var road := MeshInstance3D.new()
	var rb := BoxMesh.new()
	rb.size = Vector3(70, 0.04, 4)
	road.mesh = rb
	road.material_override = BM.get_mat("dirt")
	road.position = base + Vector3(25, 0.02, 2)
	world.add_child(road)
	var north := [cottage(), timber2(), cut_house(), farm_l(), stone2()]
	var south := [hut(), cottage_annex(), mixed2(), cottage(0.9), timber2()]
	var x := 0.0
	for sp in north:
		var b: Dictionary = sp["blocks"][0]
		var n := BB.build(sp)
		n.position = base + Vector3(x, 0, -1 - b["d"])
		world.add_child(n)
		x += b["w"] + 4.0
	x = 2.0
	for sp in south:
		var w := 0
		var d := 0
		for bl in sp["blocks"]:
			w = max(w, bl["x"] + bl["w"])
			d = max(d, bl["z"] + bl["d"])
		var n := BB.build(sp)
		n.rotation.y = PI
		n.position = base + Vector3(x + w, 0, 5 + d)
		world.add_child(n)
		x += w + 4.0
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	for i in 18:
		var tx := rng.randf_range(-6, 56)
		var tz := rng.randf_range(-22, -12) if rng.randf() < 0.5 else rng.randf_range(16, 24)
		_tree(base + Vector3(tx, 0, tz), rng)
	for i in 16:
		_person(base + Vector3(rng.randf_range(0, 50), 0, rng.randf_range(0.5, 3.5)), rng, i)
	_aim(base + Vector3(23, 1, 1), 30.0)
	await _snap("building_village.png")
	_aim(base + Vector3(23.5, 0.5, -3.5), 9.0)
	await _snap("building_cutaway.png")
