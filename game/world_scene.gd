class_name WorldScene
extends Node3D
## 시뮬레이션 지역(RegionView JSON)을 블록풍으로 그린다. show_data 로 몇 번이고 다시 그릴 수 있다.
## 땅·밭·길·나무는 통째로 다시 깔고, 집과 사람은 모양이 그대로면 만들어 둔 것을 다시 쓴다(바뀐 것만 새로 만든다).
## 타일 하나 = 8 m. 지형은 높이를 1 m 단위로 끊어 계단처럼 보이게 한다.

const BB := preload("res://prototypes/building_look/building_builder.gd")
const BM := preload("res://prototypes/building_look/materials.gd")
const BH := preload("res://prototypes/blocky_look/blocky_human.gd")
const VS := preload("res://prototypes/building_look/village.gd")
const T := 8.0                       # 타일 크기(m)
const ISO := Vector3(-1.0, 1.25, 1.0)
const SCRUB := 12                   # 이만큼 묵으면 덤불 (SimConfig.ScrubYears)
const FOREST_YEARS := 35

enum Tile { SEA, LAKE, RIVER, GRASS, FOREST, MARSH, HILLS, MOUNTAIN, ROAD }

var cam: Camera3D
var data: Dictionary
var W := 0
var H := 0
var heights: PackedFloat32Array
var types: PackedInt32Array
var fields: PackedInt32Array      # 밭 주인 가구 id, 없으면 -1
var roads: PackedByteArray        # 길 0 없음 / 1 오솔길 / 2 큰길 (물 위면 다리)
var wild: PackedInt32Array        # 버려진 밭이 묵은 해 + 1 (0 = 해당 없음)
var rng := RandomNumberGenerator.new()
var layer: Node3D                    # 땅·밭·길·나무(다시 그릴 때마다 새로)
var house_root: Node3D
var people_root: Node3D
var house_cache := {}                # 모양 열쇠 → 집 노드
var person_cache := {}               # 모양 열쇠 → 사람 노드
var pickables := []                  # {node, pd} 화면에 나온 사람
var house_pos := []                  # 집마다 세계 좌표
var ring: MeshInstance3D
var _terrain_key := ""


func _init() -> void:
	BM.flat = true
	BB.round_r = 0.07
	_setup()
	house_root = Node3D.new(); add_child(house_root)
	people_root = Node3D.new(); add_child(people_root)
	ring = MeshInstance3D.new()
	var tm := TorusMesh.new()
	tm.inner_radius = 0.75; tm.outer_radius = 1.05
	ring.mesh = tm
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.albedo_color = Color(1.0, 0.85, 0.25)
	ring.material_override = m
	ring.visible = false
	add_child(ring)


## 새 자료로 다시 그린다. 같은 영역을 다시 그리면 집·사람 노드는 모양이 같은 한 그대로 둔다.
func show_data(d: Dictionary) -> void:
	data = d
	W = int(d["w"]); H = int(d["h"])
	var tiles: Array = d["tiles"]
	heights.resize(W * H); types.resize(W * H); fields.resize(W * H); roads.resize(W * H); wild.resize(W * H)
	for i in tiles.size():
		types[i] = int(tiles[i][0]); heights[i] = float(tiles[i][1]); fields[i] = int(tiles[i][2]); roads[i] = int(tiles[i][3])
		wild[i] = int(tiles[i][4]) if tiles[i].size() > 4 else 0
	# 땅은 타일이나 집 자리가 바뀌었을 때만 다시 깐다(나무가 집을 피해야 해서 집 자리도 본다)
	_house_spots()
	var key := str(hash(tiles)) + ":" + str(hash(_spots))
	if key != _terrain_key:
		_terrain_key = key
		if layer: layer.queue_free()
		layer = Node3D.new()
		add_child(layer)
		rng.seed = 7
		_ruin_walls.clear(); _ruin_beams.clear()
		_terrain()
		_trees()
		_paths()
		_scrub()
		_crops()
		_ruins()
		_multi(_box(Vector3(1, 1, 1)), Color(0.55, 0.52, 0.47), _ruin_walls)
		_multi(_box(Vector3(1, 1, 1)), Color(0.36, 0.27, 0.19), _ruin_beams)
	_houses()
	_people()


var _spots := []                     # [중심, 반경] 집과 큰 집 터. 나무와 덤불은 이 안에 심지 않는다


func _house_spots() -> void:
	_spots.clear()
	for hd in data["houses"]:
		var cap := int(hd[3])
		var res: int = int(hd[6]) if hd.size() > 6 else 0
		var r := 6.0 + (2.0 if cap > 8 else 0.0) + (2.0 if cap > 12 else 0.0)
		if res > 0: r = 9.0 + 8.0 * res
		_spots.append([pos(int(hd[0]), int(hd[1])), r])


func _clear_of_houses(p: Vector3, extra := 0.0) -> bool:
	for s in _spots:
		var c: Vector3 = s[0]
		var r: float = s[1] + extra
		if absf(p.x - c.x) < r and absf(p.z - c.z) < r and Vector2(p.x - c.x, p.z - c.z).length() < r: return false
	return true


func _hash2(x: int, y: int) -> int: return ((x * 73856093) ^ (y * 19349663)) & 0x7fffffff


func _ruins() -> void:
	for hd in data["houses"]:
		var cond: float = float(hd[5]) if hd.size() > 5 else 1.0
		if int(hd[2]) == 0 and cond < 0.45: _ruin(int(hd[0]), int(hd[1]), cond, int(hd[6]) if hd.size() > 6 else 0)


## 집: 모양 열쇠(자리, 크기, 등급, 살림 정도)가 같으면 전에 만든 노드를 그대로 쓴다.
func _houses() -> void:
	var houses: Array = data["houses"]
	var keep := {}
	house_pos.clear()
	for hd in houses:
		var x := int(hd[0]); var y := int(hd[1]); var cap := int(hd[3]); var wealth := float(hd[4])
		house_pos.append(pos(x, y))
		var cond: float = float(hd[5]) if hd.size() > 5 else 1.0
		var res: int = int(hd[6]) if hd.size() > 6 else 0
		if int(hd[2]) == 0 and cond < 0.45: continue      # 폐허는 땅과 함께 그린다
		var spec_name := ""
		if res > 0: spec_name = "res%d" % res
		elif cap <= 4: spec_name = "hut" if wealth < 120 else "cottage"
		elif cap <= 6: spec_name = "cottage"
		elif cap <= 8: spec_name = "timber2" if wealth < 400 else "mixed2"
		elif cap <= 12: spec_name = "cottage_annex" if wealth < 400 else "farm_l"
		elif cap <= 16: spec_name = "farm_l"
		else: spec_name = "house_u"
		var key := "%d,%d,%s" % [x + int(data["x0"]), y + int(data["y0"]), spec_name]
		keep[key] = true
		if house_cache.has(key):
			house_cache[key].position = pos(x, y) if res == 0 else Vector3.ZERO
			continue
		var node: Node3D
		if res > 0:
			rng.seed = _hash2(x, y)
			node = _residence(x, y, res)
		else:
			var spec: Dictionary = _spec(spec_name)
			var b := BB.build(spec)
			var ext := 0
			for bl in spec["blocks"]: ext = max(ext, max(bl["x"] + bl["w"], bl["z"] + bl["d"]))
			node = Node3D.new()
			node.add_child(b)
			b.position = Vector3(-ext * 0.5, 0, -ext * 0.5)
			node.rotation.y = (_hash2(x, y) % 4) * PI * 0.5
			node.position = pos(x, y)
		house_root.add_child(node)
		house_cache[key] = node
	for key in house_cache.keys():
		if not keep.has(key):
			house_cache[key].queue_free()
			house_cache.erase(key)


func _spec(n: String) -> Dictionary:
	match n:
		"hut": return VS.hut()
		"cottage": return VS.cottage()
		"timber2": return VS.timber2()
		"mixed2": return VS.mixed2()
		"cottage_annex": return VS.cottage_annex()
		"farm_l": return VS.farm_l()
	return VS.house_u()


## 사람: 집마다 가장부터 셋(플레이어 집은 여섯)까지 집 둘레에 세운다. 자리는 사람마다 정해져 있어 다시 그려도 그대로다.
func _people() -> void:
	var houses: Array = data["houses"]
	var people: Array = data.get("people", [])
	var hinfo: Array = data["hinfo"]
	var by_house := {}
	for pd in people:
		var hi := int(pd["h"])
		if not by_house.has(hi): by_house[hi] = []
		by_house[hi].append(pd)
	pickables.clear()
	var keep := {}
	for hi in houses.size():
		var hd: Array = houses[hi]
		var folk: Array = by_house.get(hi, [])
		folk.sort_custom(func(a, b): return a["head"] and not b["head"])
		var limit := 6 if hinfo[hi].get("player", false) else 3
		for k in mini(folk.size(), limit):
			var pd: Dictionary = folk[k]
			rng.seed = int(pd["uid"]) * 7919 + 13
			var x := clampi(int(hd[0]) + rng.randi_range(-2, 2), 0, W - 1)
			var y := clampi(int(hd[1]) + rng.randi_range(-2, 2), 0, H - 1)
			if types[idx(x, y)] in [Tile.RIVER, Tile.LAKE, Tile.SEA]: x = int(hd[0]); y = int(hd[1])
			var p := pos(x, y) + Vector3(rng.randf_range(-3, 3), 0, rng.randf_range(-3, 3))
			var rot := rng.randf_range(-PI, PI)
			var age := int(pd["age"])
			var key := "%d:%s:%d:%s" % [int(pd["uid"]), pd["job"], age if age < 14 else (14 if age < 58 else 58), str(pd["bald"])]
			keep[key] = true
			var n: Node3D
			if person_cache.has(key):
				n = person_cache[key]
			else:
				rng.seed = int(pd["uid"]) * 104729 + 1
				n = _person_node(pd)
				people_root.add_child(n)
				person_cache[key] = n
			n.position = p
			n.rotation.y = rot
			pickables.append({"node": n, "pd": pd})
	for key in person_cache.keys():
		if not keep.has(key):
			person_cache[key].queue_free()
			person_cache.erase(key)


## 화면 좌표로 고른다: 가까운 사람(22 px 안) → 가까운 집(45 px 안). 고른 것에 고리를 두른다.
## 돌려주는 것: {"kind": "person", "pd": ...} / {"kind": "house", "hi": ...} / {}
func pick(sp: Vector2) -> Dictionary:
	var best := -1
	var bd := 22.0
	for k in pickables.size():
		var n: Node3D = pickables[k]["node"]
		var d := cam.unproject_position(n.global_position + Vector3(0, 1.0, 0)).distance_to(sp)
		if d < bd: bd = d; best = k
	if best >= 0:
		mark_person(int(pickables[best]["pd"]["uid"]))
		return {"kind": "person", "pd": pickables[best]["pd"]}
	var bh := -1
	bd = 45.0
	for k in house_pos.size():
		var d := cam.unproject_position(house_pos[k] + Vector3(0, 2.5, 0)).distance_to(sp)
		if d < bd: bd = d; bh = k
	if bh >= 0:
		mark_house(bh)
		return {"kind": "house", "hi": bh}
	ring.visible = false
	return {}


func mark_person(uid: int) -> void:
	for e in pickables:
		if int(e["pd"]["uid"]) == uid:
			ring.visible = true
			ring.position = e["node"].global_position + Vector3(0, 0.1, 0)
			ring.scale = Vector3.ONE
			return
	ring.visible = false


func mark_house(hi: int) -> void:
	if hi < 0 or hi >= house_pos.size(): ring.visible = false; return
	ring.visible = true
	ring.position = house_pos[hi] + Vector3(0, 0.1, 0)
	ring.scale = Vector3(4.5, 1, 4.5)


func person_world_pos(uid: int) -> Variant:
	for e in pickables:
		if int(e["pd"]["uid"]) == uid: return e["node"].global_position
	return null


## 집 번호(hinfo 의 id)로 지역 안 집 순번을 찾는다.
func house_index(id: int) -> int:
	var hinfo: Array = data.get("hinfo", [])
	for k in hinfo.size():
		if int(hinfo[k].get("id", -1)) == id: return k
	return -1


func members_of(hi: int) -> Array:
	var members := []
	for pd in data.get("people", []):
		if int(pd["h"]) == hi: members.append(pd)
	members.sort_custom(func(a, b):
		if a["head"] != b["head"]: return a["head"]
		return int(a["age"]) > int(b["age"]))
	return members


func find_person(uid: int) -> Variant:
	for pd in data.get("people", []):
		if int(pd["uid"]) == uid: return pd
	return null


func aim(center: Vector3, size: float) -> void:
	cam.size = size
	cam.look_at_from_position(center + ISO.normalized() * 2000.0, center)


## 카메라가 보는 땅 위 점.
func focus_point() -> Vector3:
	var o := cam.global_position
	var dir := -cam.global_transform.basis.z
	var t := (0.0 - o.y) / dir.y if absf(dir.y) > 1e-4 else 0.0
	return o + dir * t


func pan(screen_delta: Vector2) -> void:
	var vs := get_viewport().get_visible_rect().size
	var m := cam.size / vs.y
	var right := cam.global_transform.basis.x
	var fwd := Vector3(-cam.global_transform.basis.z.x, 0, -cam.global_transform.basis.z.z).normalized()
	cam.global_position += (-right * screen_delta.x + fwd * screen_delta.y * 1.4) * m


func zoom(f: float) -> void:
	cam.size = clampf(cam.size * f, 30.0, 900.0)


func _setup() -> void:
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
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, -30, 0)
	sun.light_energy = 0.75
	sun.light_color = Color(1.0, 0.95, 0.86)
	sun.shadow_enabled = true
	sun.shadow_opacity = 0.6
	sun.directional_shadow_max_distance = 1200.0
	add_child(sun)
	cam = Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.far = 5000.0
	add_child(cam)


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
	layer.add_child(mi)


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
				var tall := k > 0.5 and rng.randf() < 0.4
				var h := rng.randf_range(1.5, 2.5)
				if not _clear_of_houses(p, s * 0.5): continue
				if tall:
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
				var s := rng.randf_range(2.6, 3.6)
				var pine := rng.randf() < 0.35
				var side := rng.randf() < 0.5
				if not _clear_of_houses(p, s * 0.5): continue
				trunks.append(Transform3D(Basis.from_scale(Vector3(0.45, h, 0.45)), p + Vector3(0, h * 0.5, 0)))
				if pine:
					for j in 3:
						var w := s * (1.0 - j * 0.28)
						pines.append(Transform3D(Basis.from_scale(Vector3(w, 1.3, w)), p + Vector3(0, h + j * 1.1, 0)))
				else:
					(leaves_a if side else leaves_b).append(Transform3D(Basis.from_scale(Vector3(s, s * 0.85, s)), p + Vector3(0, h + s * 0.35, 0)))
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


## 무너져 가는 빈집: 낮은 벽 조각과 쓰러진 들보. 낡을수록 벽이 낮고 듬성하다.
var _ruin_walls := []
var _ruin_beams := []

## 지배자의 집: 1 영주 저택, 2 망루 달린 저택, 3 성(성벽과 모서리 탑).
func _residence(x: int, y: int, res: int) -> Node3D:
	var box := Node3D.new()
	var spec: Dictionary = VS.manor() if res == 1 else VS.manor_tower() if res == 2 else VS.castle()
	var b := BB.build(spec)
	var ext := 0
	for bl in spec["blocks"]: ext = max(ext, max(bl["x"] + bl["w"], bl["z"] + bl["d"]))
	if res >= 3:
		var stone := BM.get_mat("stone", 0.0)
		var e := float(ext)
		BB.curtain(b, Vector2(2, 2), Vector2(e - 2, 2), 1.2, 5.0, stone)
		BB.curtain(b, Vector2(2, e - 2), Vector2(e * 0.5 - 2.5, e - 2), 1.2, 5.0, stone)
		BB.curtain(b, Vector2(e * 0.5 + 2.5, e - 2), Vector2(e - 2, e - 2), 1.2, 5.0, stone)
		BB.curtain(b, Vector2(2, 2), Vector2(2, e - 2), 1.2, 5.0, stone)
		BB.curtain(b, Vector2(e - 2, 2), Vector2(e - 2, e - 2), 1.2, 5.0, stone)
	var node := Node3D.new()
	node.add_child(b)
	b.position = Vector3(-ext * 0.5, 0, -ext * 0.5)
	node.rotation.y = (rng.randi() % 4) * PI * 0.5
	node.position = pos(x, y)
	box.add_child(node)
	# 깃발: 지배자 집 표시
	var pole := MeshInstance3D.new()
	pole.mesh = _box(Vector3(0.25, 1, 0.25))
	var top := 9.0 if res == 1 else 15.0 if res == 2 else 19.0
	pole.scale = Vector3(1, 4.0, 1)
	pole.position = pos(x, y) + Vector3(0, top + 2.0, 0)
	pole.material_override = BM.flat_mat(Color(0.35, 0.25, 0.17))
	box.add_child(pole)
	var flag := MeshInstance3D.new()
	flag.mesh = _box(Vector3(2.4, 1.4, 0.12))
	flag.position = pos(x, y) + Vector3(1.3, top + 3.3, 0)
	flag.material_override = BM.flat_mat(Color(0.72, 0.18, 0.16))
	box.add_child(flag)
	return box


func _ruin(x: int, y: int, cond: float, res := 0) -> void:
	var c := pos(x, y)
	var h := (0.4 + cond * 2.4) * (1.0 + res * 0.8)
	var sc := 1.0 + res * 1.5
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
			sz = Vector3(sz.x * sc if sz.x > 1 else sz.x * (1.0 + res * 0.5), hh, sz.z * sc if sz.z > 1 else sz.z * (1.0 + res * 0.5))
			_ruin_walls.append(Transform3D(Basis.from_scale(sz), c + p * sc + Vector3(0, hh * 0.5, 0)))
	for k in 2:
		var b := Basis(Vector3.UP, rng.randf_range(0, PI)).scaled(Vector3(4.5, 0.35, 0.35))
		_ruin_beams.append(Transform3D(b, c + Vector3(rng.randf_range(-1.5, 1.5), 0.2, rng.randf_range(-1.5, 1.5))))


## 데이터에 맞춘 사람 모습: 성별, 나이(아이), 머리색, 대머리, 키, 하는 일에 따른 옷.
func _person_node(pd: Dictionary) -> Node3D:
	var female: bool = not pd["male"]
	var hair_col := {"red": Color(0.72, 0.32, 0.16), "blond": Color(0.88, 0.74, 0.45), "dark": Color(0.13, 0.1, 0.09),
		"brown": Color(0.38, 0.25, 0.15), "grey": Color(0.78, 0.76, 0.72)}
	if pd["job"] == "군인":
		var sd := {"female": false, "skin": Color(0.86, 0.68, 0.55), "hair_color": hair_col.get(pd["hair"], hair_col["brown"]),
			"height": 0.98 + 0.1 * float(pd["height"]), "build": 1.1, "outfit": BH.outfit("soldier", BH.MADDER), "hat": "kettle", "hair": "short"}
		return BH.build(sd, true)
	var skins := [Color(0.93, 0.78, 0.67), Color(0.86, 0.68, 0.55), Color(0.78, 0.6, 0.46)]
	var d := {"female": female, "skin": skins[rng.randi() % skins.size()], "hair_color": hair_col.get(pd["hair"], hair_col["brown"]),
		"height": 0.94 + 0.12 * float(pd["height"]), "build": rng.randf_range(0.9, 1.1)}
	var c1: Color = [BH.LINEN, BH.BROWN, BH.RUSSET, BH.GREY][rng.randi() % 4]
	match pd["job"]:
		"목수": c1 = BH.LEATHER
		"약초꾼": c1 = BH.GREEN
		"장사꾼": c1 = BH.WOAD
	if female:
		d["outfit"] = BH.outfit("peasant_f", c1, BH.LINEN)
		d["hair"] = ["short", "long", "bun", "braid"][rng.randi() % 4]
		if int(pd["age"]) >= 30 and rng.randf() < 0.5: d["hat"] = "coif"
	else:
		d["outfit"] = BH.outfit("peasant_m", c1, BH.GREY)
		d["hair"] = "bald" if pd["bald"] else ["short", "short", "long"][rng.randi() % 3]
		if int(pd["age"]) >= 20 and rng.randf() < 0.45: d["beard"] = "full" if rng.randf() < 0.5 else "short"
	if int(pd["age"]) < 14:
		d["child"] = true
		d["height"] = 0.55 + 0.012 * int(pd["age"])
		d["build"] = 0.8
	return BH.build(d, true)


