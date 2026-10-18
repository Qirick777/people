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
const SCRUB := 12                   # 이만큼 묵으면 덤불 (SimConfig.ScrubYears)
const FOREST_YEARS := 35

enum Tile { SEA, LAKE, RIVER, GRASS, FOREST, MARSH, HILLS, MOUNTAIN, ROAD }

var world: Node3D
var cam: Camera3D
var data: Dictionary
var W: int
var H: int
var heights: PackedFloat32Array
var types: PackedInt32Array
var fields: PackedInt32Array      # 밭 주인 가구 id, 없으면 -1
var roads: PackedByteArray        # 길 0 없음 / 1 오솔길 / 2 큰길 (물 위면 다리)
var wild: PackedInt32Array        # 버려진 밭이 묵은 해 + 1 (0 = 해당 없음)
var rng := RandomNumberGenerator.new()


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var file: String = args[0] if args.size() > 0 else "region_s1.json"
	data = JSON.parse_string(FileAccess.get_file_as_string("res://prototypes/world_view/" + file))
	W = int(data["w"]); H = int(data["h"])
	var tiles: Array = data["tiles"]
	heights.resize(W * H); types.resize(W * H); fields.resize(W * H); roads.resize(W * H); wild.resize(W * H)
	for i in tiles.size():
		types[i] = int(tiles[i][0]); heights[i] = float(tiles[i][1]); fields[i] = int(tiles[i][2]); roads[i] = int(tiles[i][3])
		wild[i] = int(tiles[i][4]) if tiles[i].size() > 4 else 0
	rng.seed = 7
	BM.flat = true
	BB.round_r = 0.07
	_setup()
	_terrain()
	_trees()
	_paths()
	_scrub()
	_crops()
	_houses()
	_multi(_box(Vector3(1, 1, 1)), Color(0.55, 0.52, 0.47), _ruin_walls)
	_multi(_box(Vector3(1, 1, 1)), Color(0.36, 0.27, 0.19), _ruin_beams)
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
		Tile.LAKE: Color(0.36, 0.56, 0.72), Tile.RIVER: Color(0.38, 0.6, 0.76), Tile.ROAD: Color(0.69, 0.58, 0.41),
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
			if t == Tile.ROAD:
				key = Tile.GRASS                                  # 길은 풀밭 위에 흙 띠로 따로 그린다
				c = col[Tile.GRASS]
			var yrs := wild[i] - 1
			if fields[i] >= 0 and t != Tile.FOREST and yrs < SCRUB:
				key = "field"
				c = Color(0.5, 0.38, 0.26)
			elif wild[i] > 0 and yrs < 3:
				key = "fallow"                                    # 갓 버려진 밭: 잡초 난 흙
				c = Color(0.5, 0.47, 0.3)
			elif wild[i] > 0:
				key = "meadow"                                    # 묵은 풀밭
				c = Color(0.52, 0.62, 0.33)
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
	_bridges()


## 물 위의 길은 나무 다리. 이어진 다리 칸을 한 덩어리로 묶어 둑 높이에 맞추고,
## 칸마다 가운데 판과 이웃 다리 칸 쪽으로 뻗는 판을 깔아 지그재그 길도 끊기지 않게 한다.
func _bridges() -> void:
	var planks := []
	var seen := {}
	var dirs := [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]
	for y in H:
		for x in W:
			var i := idx(x, y)
			if seen.has(i) or not _bridge(x, y): continue
			# 다리 한 덩어리 모으기
			var comp := [Vector2i(x, y)]
			seen[i] = true
			var hi := -1e9
			var k := 0
			while k < comp.size():
				var c: Vector2i = comp[k]
				k += 1
				for d in dirs:
					var n: Vector2i = c + d
					if n.x < 0 or n.y < 0 or n.x >= W or n.y >= H: continue
					var ni := idx(n.x, n.y)
					if _bridge(n.x, n.y):
						if not seen.has(ni):
							seen[ni] = true
							comp.append(n)
					elif roads[ni] > 0 or not (types[ni] in [Tile.RIVER, Tile.LAKE, Tile.SEA]):
						hi = maxf(hi, H_at(n.x, n.y))
			if hi < -1e8: hi = H_at(x, y) + 0.6
			for c in comp:
				var center := Vector3((c.x + 0.5) * T, hi + 0.1, (c.y + 0.5) * T)
				planks.append(Transform3D(Basis.from_scale(Vector3(4.0, 0.5, 4.0)), center))
				for d in dirs:
					var n: Vector2i = c + d
					if n.x < 0 or n.y < 0 or n.x >= W or n.y >= H or roads[idx(n.x, n.y)] == 0: continue
					var sz := Vector3(T * 0.5, 0.5, 4.0) if d.x != 0 else Vector3(4.0, 0.5, T * 0.5)
					planks.append(Transform3D(Basis.from_scale(sz), center + Vector3(d.x, 0, d.y) * T * 0.25))
	_multi(_box(Vector3(1, 1, 1)), Color(0.5, 0.36, 0.23), planks)


func _bridge(x: int, y: int) -> bool:
	var i := idx(x, y)
	return roads[i] > 0 and types[i] in [Tile.RIVER, Tile.LAKE, Tile.SEA]


## 길: 풀밭 위에 흙 띠. 오솔길은 좁고 큰길은 넓다. 가운데 판과 이웃 길 칸 쪽으로 뻗는 띠를 깐다.
func _paths() -> void:
	var path_xf := []
	var major_xf := []
	var dirs := [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]
	for y in H:
		for x in W:
			var i := idx(x, y)
			if roads[i] == 0 or types[i] in [Tile.RIVER, Tile.LAKE, Tile.SEA]: continue
			var wdt := 6.5 if roads[i] == 2 else 3.2
			var list: Array = major_xf if roads[i] == 2 else path_xf
			var top := H_at(x, y) + 0.06
			var center := Vector3((x + 0.5) * T, top, (y + 0.5) * T)
			list.append(Transform3D(Basis.from_scale(Vector3(wdt, 0.12, wdt)), center))
			for d in dirs:
				var n: Vector2i = Vector2i(x, y) + d
				if n.x < 0 or n.y < 0 or n.x >= W or n.y >= H or roads[idx(n.x, n.y)] == 0: continue
				var w2 := minf(wdt, 6.5 if roads[idx(n.x, n.y)] == 2 else 3.2)
				var sz := Vector3(T * 0.5, 0.12, w2) if d.x != 0 else Vector3(w2, 0.12, T * 0.5)
				list.append(Transform3D(Basis.from_scale(sz), center + Vector3(d.x, 0, d.y) * T * 0.25))
	var unit := _box(Vector3(1, 1, 1))
	_multi(unit, Color(0.71, 0.6, 0.42), path_xf)
	_multi(unit, Color(0.66, 0.53, 0.36), major_xf)


## 덤불: 오래 묵은 밭에 키 작은 나무 덩어리. 숲이 될 때가 가까울수록 빽빽하다.
func _scrub() -> void:
	var bushes := []
	var saplings := []
	for y in H:
		for x in W:
			var i := idx(x, y)
			var yrs := wild[i] - 1
			if wild[i] == 0 or yrs < SCRUB or types[i] == Tile.FOREST: continue
			var k := clampf(float(yrs - SCRUB) / float(FOREST_YEARS - SCRUB), 0.0, 1.0)
			var n := 1 + int(k * 4.0)
			for j in n:
				var p := pos(x, y) + Vector3(rng.randf_range(-3, 3), 0, rng.randf_range(-3, 3))
				var s := rng.randf_range(1.0, 1.8) * (0.7 + k * 0.6)
				if k > 0.5 and rng.randf() < 0.4:
					var h := rng.randf_range(1.5, 2.5)
					saplings.append(Transform3D(Basis.from_scale(Vector3(s * 1.2, s * 1.1, s * 1.2)), p + Vector3(0, h, 0)))
					bushes.append(Transform3D(Basis.from_scale(Vector3(0.3, h, 0.3)), p + Vector3(0, h * 0.5, 0)))
				else:
					bushes.append(Transform3D(Basis.from_scale(Vector3(s, s * 0.7, s)), p + Vector3(0, s * 0.35, 0)))
	var unit := _box(Vector3(1, 1, 1))
	_multi(unit, Color(0.4, 0.55, 0.27), bushes)
	_multi(unit, Color(0.38, 0.58, 0.3), saplings)


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


## 한 가구의 밭은 한 덩어리로 보이게: 이랑 방향과 작물 상태를 주인 가구로 정하고,
## 같은 주인 밭끼리는 이랑을 이어 붙인다. 주인이 바뀌는 경계에는 두렁(틈)을 남긴다.
func _crops() -> void:
	var kinds := [[], [], []]
	var unit := _box(Vector3(1, 1, 1))
	for y in H:
		for x in W:
			var i := idx(x, y)
			var o := fields[i]
			if o < 0 or types[i] == Tile.FOREST or wild[i] - 1 >= SCRUB: continue
			var hsh := (o * 2654435761) & 0x7fffffff
			var along_x := (hsh % 2) == 0
			var kind := (hsh / 2) % 3                       # 익은 밀, 푸른 작물, 어린 싹
			var dx := 1 if along_x else 0
			var dy := 0 if along_x else 1
			var a := -T * 0.5 + (0.0 if _same(x - dx, y - dy, o) else 0.7)
			var b := T * 0.5 - (0.0 if _same(x + dx, y + dy, o) else 0.7)
			var hgt := 0.7 if kind < 2 else 0.35
			for r in 4:
				var off := -3.0 + r * 2.0
				if r == 0 and not _same(x - dy, y - dx, o): off += 0.4
				if r == 3 and not _same(x + dy, y + dx, o): off -= 0.4
				var mid := (a + b) * 0.5
				var p := pos(x, y) + (Vector3(mid, hgt * 0.5, off) if along_x else Vector3(off, hgt * 0.5, mid))
				var sz := Vector3(b - a, hgt, 1.0) if along_x else Vector3(1.0, hgt, b - a)
				kinds[kind].append(Transform3D(Basis.from_scale(sz), p))
	_multi(unit, Color(0.82, 0.7, 0.36), kinds[0])
	_multi(unit, Color(0.5, 0.64, 0.3), kinds[1])
	_multi(unit, Color(0.58, 0.7, 0.36), kinds[2])


func _same(x: int, y: int, o: int) -> bool:
	return x >= 0 and y >= 0 and x < W and y < H and fields[idx(x, y)] == o


func _houses() -> void:
	var houses: Array = data["houses"]
	for hd in houses:
		var x := int(hd[0]); var y := int(hd[1]); var cap := int(hd[3]); var wealth := float(hd[4])
		var cond: float = float(hd[5]) if hd.size() > 5 else 1.0
		if int(hd[2]) == 0 and cond < 0.45:
			_ruin(x, y, cond)
			continue
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


## 무너져 가는 빈집: 낮은 벽 조각과 쓰러진 들보. 낡을수록 벽이 낮고 듬성하다.
var _ruin_walls := []
var _ruin_beams := []

func _ruin(x: int, y: int, cond: float) -> void:
	var c := pos(x, y)
	var h := 0.4 + cond * 2.4
	for side in 4:
		for seg in 3:
			if rng.randf() > 0.35 + cond: continue
			var off := -2.0 + seg * 2.0
			var p: Vector3
			var sz: Vector3
			match side:
				0: p = Vector3(off, 0, -2.6); sz = Vector3(1.9, 0, 0.45)
				1: p = Vector3(off, 0, 2.6); sz = Vector3(1.9, 0, 0.45)
				2: p = Vector3(-2.6, 0, off); sz = Vector3(0.45, 0, 1.9)
				_: p = Vector3(2.6, 0, off); sz = Vector3(0.45, 0, 1.9)
			var hh := h * rng.randf_range(0.5, 1.0)
			sz.y = hh
			_ruin_walls.append(Transform3D(Basis.from_scale(sz), c + p + Vector3(0, hh * 0.5, 0)))
	for k in 2:
		var b := Basis(Vector3.UP, rng.randf_range(0, PI)).scaled(Vector3(4.5, 0.35, 0.35))
		_ruin_beams.append(Transform3D(b, c + Vector3(rng.randf_range(-1.5, 1.5), 0.2, rng.randf_range(-1.5, 1.5))))


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
