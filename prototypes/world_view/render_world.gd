extends SceneTree
## 시뮬레이션이 내보낸 지역(region_*.json)을 블록풍 게임 화면으로 그려 스크린샷을 찍는다.
## 타일 하나 = 8 m. 지형은 높이를 1 m 단위로 끊어 계단처럼 보이게 한다.
## 실행: xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 --script prototypes/world_view/render_world.gd -- region_s1.json

const BB := preload("res://prototypes/building_look/building_builder.gd")
const BM := preload("res://prototypes/building_look/materials.gd")
const BH := preload("res://prototypes/blocky_look/blocky_human.gd")
const VS := preload("res://prototypes/building_look/village.gd")
const OUT := "res://docs/images/"
const T := 8.0                       # 타일 크기(m)
const ISO := Vector3(-1.0, 1.25, 1.0)

enum Tile { SEA, LAKE, RIVER, GRASS, FOREST, MARSH, HILLS, MOUNTAIN }

var world: Node3D
var cam: Camera3D
var data: Dictionary
var W: int
var H: int
var heights: PackedFloat32Array
var types: PackedInt32Array
var fields: PackedInt32Array
var rng := RandomNumberGenerator.new()


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var file: String = args[0] if args.size() > 0 else "region_s1.json"
	data = JSON.parse_string(FileAccess.get_file_as_string("res://prototypes/world_view/" + file))
	W = int(data["w"]); H = int(data["h"])
	var tiles: Array = data["tiles"]
	heights.resize(W * H); types.resize(W * H); fields.resize(W * H)
	for i in tiles.size():
		types[i] = int(tiles[i][0]); heights[i] = float(tiles[i][1]); fields[i] = int(tiles[i][2])
	rng.seed = 7
	BM.flat = true
	BB.round_r = 0.07
	_setup()
	_terrain()
	_trees()
	_crops()
	_houses()
	_people()
	await _shots(file.get_basename())
	quit()


func _setup() -> void:
	world = Node3D.new()
	root.add_child(world)
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.6, 0.72, 0.82)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.78, 0.8, 0.88)
	e.ambient_light_energy = 0.34
	e.tonemap_mode = Environment.TONE_MAPPER_ACES
	e.tonemap_exposure = 0.95
	env.environment = e
	world.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, -30, 0)
	sun.light_energy = 0.75
	sun.light_color = Color(1.0, 0.95, 0.86)
	sun.shadow_enabled = true
	sun.shadow_opacity = 0.6
	sun.directional_shadow_max_distance = 1200.0
	world.add_child(sun)
	cam = Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.far = 5000.0
	world.add_child(cam)


func idx(x: int, y: int) -> int: return y * W + x
func H_at(x: int, y: int) -> float: return floorf(heights[idx(x, y)])          # 1 m 계단
func pos(x: int, y: int) -> Vector3: return Vector3((x + 0.5) * T, H_at(x, y), (y + 0.5) * T)


## 같은 모양을 여러 개 그리는 MultiMesh 하나를 만든다.
func _multi(mesh: Mesh, color: Color, xforms: Array) -> void:
	if xforms.is_empty(): return
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = mesh
	mm.instance_count = xforms.size()
	for i in xforms.size(): mm.set_instance_transform(i, xforms[i])
	var mi := MultiMeshInstance3D.new()
	mi.multimesh = mm
	mi.material_override = BM.flat_mat(color)
	world.add_child(mi)


func _box(size: Vector3) -> BoxMesh:
	var b := BoxMesh.new()
	b.size = size
	return b


func _terrain() -> void:
	var unit := _box(Vector3(1, 1, 1))
	var col := {
		Tile.GRASS: Color(0.45, 0.6, 0.3), Tile.FOREST: Color(0.33, 0.47, 0.25), Tile.MARSH: Color(0.42, 0.5, 0.36),
		Tile.HILLS: Color(0.58, 0.57, 0.42), Tile.MOUNTAIN: Color(0.55, 0.53, 0.5), Tile.SEA: Color(0.3, 0.48, 0.66),
		Tile.LAKE: Color(0.36, 0.56, 0.72), Tile.RIVER: Color(0.38, 0.6, 0.76),
	}
	var groups := {}
	var base := 1e9
	for i in heights.size(): base = minf(base, floorf(heights[i]))
	base -= 4.0
	for y in H:
		for x in W:
			var i := idx(x, y)
			var t := types[i]
			var top := H_at(x, y)
			var key: Variant = t
			var c: Color = col.get(t, Color.MAGENTA)
			if fields[i] == 1 and t != Tile.FOREST:
				key = "field"
				c = Color(0.6, 0.48, 0.33)
			if t in [Tile.RIVER, Tile.LAKE, Tile.SEA]:
				# 물은 둑보다 낮게: 주변 땅 높이에서 0.6 m 아래
				var lo := top
				for d in [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]:
					var xx: int = x + d.x
					var yy: int = y + d.y
					if xx >= 0 and yy >= 0 and xx < W and yy < H and not (types[idx(xx, yy)] in [Tile.RIVER, Tile.LAKE, Tile.SEA]):
						lo = minf(lo, H_at(xx, yy))
				top = lo - 0.6
			var hgt := top - base
			var xf := Transform3D(Basis.from_scale(Vector3(T, hgt, T)), Vector3((x + 0.5) * T, base + hgt * 0.5, (y + 0.5) * T))
			if not groups.has(key): groups[key] = [c, []]
			groups[key][1].append(xf)
	for k in groups: _multi(unit, groups[k][0], groups[k][1])


func _trees() -> void:
	var trunks := []
	var leaves_a := []
	var leaves_b := []
	var pines := []
	for y in H:
		for x in W:
			var i := idx(x, y)
			if types[i] != Tile.FOREST: continue
			var n := 2 + rng.randi() % 2
			for k in n:
				var p := pos(x, y) + Vector3(rng.randf_range(-3, 3), 0, rng.randf_range(-3, 3))
				var h := rng.randf_range(2.5, 4.0)
				trunks.append(Transform3D(Basis.from_scale(Vector3(0.45, h, 0.45)), p + Vector3(0, h * 0.5, 0)))
				var s := rng.randf_range(2.6, 3.6)
				if rng.randf() < 0.35:
					for j in 3:
						var w := s * (1.0 - j * 0.28)
						pines.append(Transform3D(Basis.from_scale(Vector3(w, 1.3, w)), p + Vector3(0, h + j * 1.1, 0)))
				else:
					(leaves_a if rng.randf() < 0.5 else leaves_b).append(Transform3D(Basis.from_scale(Vector3(s, s * 0.85, s)), p + Vector3(0, h + s * 0.35, 0)))
	var unit := _box(Vector3(1, 1, 1))
	_multi(unit, Color(0.42, 0.29, 0.19), trunks)
	_multi(unit, Color(0.36, 0.56, 0.28), leaves_a)
	_multi(unit, Color(0.42, 0.62, 0.3), leaves_b)
	_multi(unit, Color(0.26, 0.44, 0.24), pines)


func _crops() -> void:
	var rows := []
	var rows2 := []
	var unit := _box(Vector3(1, 1, 1))
	for y in H:
		for x in W:
			var i := idx(x, y)
			if fields[i] != 1 or types[i] == Tile.FOREST: continue
			var along_x := ((x / 3 + y / 3) % 2) == 0       # 밭마다 이랑 방향이 다르게
			var ripe := ((x * 7 + y * 13) % 5) < 3
			for r in 4:
				var off := -3.0 + r * 2.0
				var p := pos(x, y) + (Vector3(0, 0.35, off) if along_x else Vector3(off, 0.35, 0))
				var sz := Vector3(7.2, 0.7, 1.0) if along_x else Vector3(1.0, 0.7, 7.2)
				(rows if ripe else rows2).append(Transform3D(Basis.from_scale(sz), p))
	_multi(unit, Color(0.82, 0.7, 0.36), rows)
	_multi(unit, Color(0.5, 0.64, 0.3), rows2)


func _houses() -> void:
	var houses: Array = data["houses"]
	for hd in houses:
		var x := int(hd[0]); var y := int(hd[1]); var cap := int(hd[3]); var wealth := float(hd[4])
		var spec: Dictionary
		if cap <= 4: spec = VS.hut() if wealth < 120 else VS.cottage()
		elif cap <= 6: spec = VS.cottage()
		elif cap <= 8: spec = VS.timber2() if wealth < 400 else VS.mixed2()
		elif cap <= 12: spec = VS.cottage_annex() if wealth < 400 else VS.farm_l()
		elif cap <= 16: spec = VS.farm_l()
		else: spec = VS.house_u()
		var b := BB.build(spec)
		var ext := 0
		for bl in spec["blocks"]: ext = max(ext, max(bl["x"] + bl["w"], bl["z"] + bl["d"]))
		var node := Node3D.new()
		node.add_child(b)
		b.position = Vector3(-ext * 0.5, 0, -ext * 0.5)
		node.rotation.y = (rng.randi() % 4) * PI * 0.5
		node.position = pos(x, y)
		world.add_child(node)


func _people() -> void:
	var houses: Array = data["houses"]
	for hd in houses:
		var n := mini(int(hd[2]), 3)
		for k in n:
			var x := clampi(int(hd[0]) + rng.randi_range(-2, 2), 0, W - 1)
			var y := clampi(int(hd[1]) + rng.randi_range(-2, 2), 0, H - 1)
			if types[idx(x, y)] in [Tile.RIVER, Tile.LAKE, Tile.SEA]: continue
			var p := BH.random(rng, true)
			p.position = pos(x, y) + Vector3(rng.randf_range(-3, 3), 0, rng.randf_range(-3, 3))
			p.rotation.y = rng.randf_range(-PI, PI)
			world.add_child(p)


func _snap(file: String) -> void:
	for i in 6: await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png(ProjectSettings.globalize_path(OUT + file))


func _aim(center: Vector3, size: float) -> void:
	cam.size = size
	cam.look_at_from_position(center + ISO.normalized() * 2000.0, center)


func _shots(tag: String) -> void:
	# 마을 중심 = 집들의 평균 위치
	var c := Vector3.ZERO
	var houses: Array = data["houses"]
	var near := []
	var mid := pos(W / 2, H / 2)
	for hd in houses:
		var p := pos(int(hd[0]), int(hd[1]))
		if p.distance_to(mid) < 220: near.append(p)
	for p in near: c += p
	c /= max(1, near.size())
	_aim(c, 520.0)
	await _snap("game_%s_far.png" % tag)
	_aim(c, 230.0)
	await _snap("game_%s_mid.png" % tag)
	_aim(c + Vector3(0, 0, 0), 70.0)
	await _snap("game_%s_near.png" % tag)
