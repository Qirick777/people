extends SceneTree
## 사람 외형 시안을 렌더링해 docs/images/에 저장한다.
## 실행: xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 --script prototypes/human_look/render_preview.gd

const HB := preload("res://prototypes/human_look/human_builder.gd")
const OF := preload("res://prototypes/human_look/outfits.gd")
const OUT := "res://docs/images/"

var cam: Camera3D
var people: Array[Node3D] = []


func _lineup() -> Array:
	var tan := Color(0.62, 0.45, 0.33)
	return [
		{"outfit": OF.peasant_man(), "hair": "short", "beard": "short", "hair_color": Color(0.33, 0.22, 0.13), "seed": 1},
		{"outfit": OF.peasant_woman(), "female": true, "hat": "coif", "hair": "short", "hair_color": Color(0.3, 0.2, 0.12), "seed": 2},
		{"outfit": OF.smith(tan), "skin": tan, "hair": "bald", "beard": "full", "hair_color": Color(0.1, 0.08, 0.07), "build": 1.15, "seed": 3},
		{"outfit": OF.soldier(), "hat": "kettle", "hair": "short", "hair_color": Color(0.45, 0.32, 0.18), "skin": Color(0.74, 0.57, 0.45), "seed": 4},
		{"outfit": OF.noble_man(), "hair": "short", "hair_color": Color(0.78, 0.64, 0.38), "skin": Color(0.8, 0.64, 0.54), "height": 1.04, "seed": 5},
		{"outfit": OF.noble_woman(), "female": true, "hair": "braid", "hair_color": Color(0.62, 0.26, 0.12), "skin": Color(0.81, 0.65, 0.55), "seed": 6},
		{"outfit": OF.monk(), "hat": "hood_down", "hair": "tonsure", "hair_color": Color(0.4, 0.38, 0.35), "build": 0.92, "seed": 7},
		{"outfit": OF.peasant_woman(OF.WOAD, OF.LINEN.darkened(0.08)), "female": true, "hair": "bun", "hair_color": Color(0.7, 0.68, 0.64), "skin": Color(0.74, 0.58, 0.47), "build": 1.08, "seed": 8},
		{"outfit": OF.peasant_man(OF.GREEN, OF.BROWN), "hair": "long", "hair_color": Color(0.2, 0.14, 0.09), "skin": Color(0.68, 0.5, 0.38), "build": 0.9, "height": 1.05, "seed": 9},
		{"outfit": OF.peasant_man(OF.LINEN.darkened(0.1), OF.BROWN), "child": true, "hair": "short", "hair_color": Color(0.82, 0.7, 0.45), "height": 0.62, "build": 0.8, "seed": 10},
	]


func _init() -> void:
	var world := Node3D.new()
	root.add_child(world)
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.3, 0.34, 0.28)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.72, 0.74, 0.8)
	e.ambient_light_energy = 0.38
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.environment = e
	world.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 35, 0)
	sun.light_energy = 0.82
	sun.light_color = Color(1.0, 0.95, 0.86)
	sun.shadow_enabled = true
	world.add_child(sun)
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(160, 160)
	ground.mesh = pm
	var gm := StandardMaterial3D.new()
	var gi := HB.fabric(Color(0.3, 0.37, 0.21), 99, 256, 256, 0.0)
	gm.albedo_texture = HB.tex(gi)
	gm.uv1_scale = Vector3(32, 32, 1)
	gm.roughness = 1.0
	ground.material_override = gm
	world.add_child(ground)

	var specs := _lineup()
	for i in specs.size():
		var p := HB.build(specs[i])
		p.position = Vector3(i * 0.78, 0, 0)
		world.add_child(p)
		people.append(p)

	_crowd(world)
	cam = Camera3D.new()
	world.add_child(cam)
	await _shots()
	quit()


## 옷, 색, 머리, 피부, 키, 체격을 무작위로 섞은 군중.
func _crowd(world: Node3D) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 2026
	var cloth := [OF.LINEN, OF.BROWN, OF.RUSSET, OF.MADDER, OF.WOAD, OF.GREEN, OF.GREY]
	var hairs := [Color(0.1, 0.08, 0.07), Color(0.3, 0.2, 0.12), Color(0.45, 0.32, 0.18), Color(0.78, 0.64, 0.38),
		Color(0.62, 0.26, 0.12), Color(0.7, 0.68, 0.64)]
	var skins := [Color(0.8, 0.63, 0.53), Color(0.75, 0.58, 0.46), Color(0.68, 0.5, 0.38), Color(0.58, 0.41, 0.3)]
	for i in 70:
		var female := rng.randf() < 0.5
		var c1: Color = cloth[rng.randi() % cloth.size()]
		var c2: Color = cloth[rng.randi() % cloth.size()]
		var sk: Color = skins[rng.randi() % skins.size()]
		var roll := rng.randf()
		var outfit: Dictionary
		if roll < 0.08: outfit = OF.soldier(OF.MADDER)
		elif roll < 0.12: outfit = OF.smith(sk)
		elif roll < 0.14: outfit = OF.monk()
		elif female: outfit = OF.peasant_woman(c1, OF.LINEN.darkened(rng.randf() * 0.15))
		else: outfit = OF.peasant_man(c1, c2)
		var styles := ["short", "long", "bun", "braid"] if female else ["short", "short", "long", "bald"]
		var d := {"outfit": outfit, "female": female, "skin": sk, "seed": 100 + i,
			"hair": styles[rng.randi() % styles.size()], "hair_color": hairs[rng.randi() % hairs.size()],
			"height": rng.randf_range(0.93, 1.07), "build": rng.randf_range(0.88, 1.15)}
		if not female and rng.randf() < 0.4: d["beard"] = "full" if rng.randf() < 0.5 else "short"
		if female and rng.randf() < 0.35: d["hat"] = "coif"
		if roll < 0.08: d["hat"] = "kettle"
		if rng.randf() < 0.15:
			d["child"] = true
			d["height"] = rng.randf_range(0.55, 0.75)
			d["build"] = 0.8
		var p := HB.build(d)
		p.position = Vector3(rng.randf_range(-7, 7), 0, rng.randf_range(-5, 5)) + Vector3(40, 0, 0)
		p.rotation.y = rng.randf_range(-PI, PI)
		world.add_child(p)


func _snap(file: String) -> Image:
	for i in 4:
		await process_frame
	await RenderingServer.frame_post_draw
	var img := root.get_texture().get_image()
	if file != "":
		img.save_png(ProjectSettings.globalize_path(OUT + file))
	return img


func _shots() -> void:
	var mid := Vector3(4.5 * 0.78, 0.85, 0)
	# 1. 정면 줄 세우기
	cam.projection = Camera3D.PROJECTION_PERSPECTIVE
	cam.fov = 24
	cam.look_at_from_position(mid + Vector3(0, 1.2, 12.0), mid)
	await _snap("human_lineup.png")
	# 2. 게임 시점 (직교, 비스듬히 내려다봄) 가까이
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.size = 5.5
	var iso_dir := Vector3(-0.75, 1.0, 1.0).normalized()
	cam.look_at_from_position(mid + iso_dir * 30.0, mid)
	await _snap("human_game_near.png")
	# 3. 게임 시점 멀리, 무작위 군중
	cam.size = 14.0
	var cc := Vector3(40, 0.8, 0)
	cam.look_at_from_position(cc + iso_dir * 30.0, cc)
	await _snap("human_game_far.png")
	# 4. 얼굴 근접 4명
	cam.projection = Camera3D.PROJECTION_PERSPECTIVE
	cam.fov = 20
	var sheet := Image.create(1800, 800, false, Image.FORMAT_RGBA8)
	var picks := [0, 1, 4, 5]
	for k in picks.size():
		var hp: Vector3 = people[picks[k]].position + Vector3(0, 1.6, 0)
		cam.look_at_from_position(hp + Vector3(0.45, 0.12, 1.85), hp)
		var img: Image = await _snap("")
		img.convert(Image.FORMAT_RGBA8)
		sheet.blit_rect(img, Rect2i(img.get_width() / 2 - 225, 0, 450, 800), Vector2i(k * 450, 0))
	sheet.save_png(ProjectSettings.globalize_path(OUT + "human_faces.png"))
