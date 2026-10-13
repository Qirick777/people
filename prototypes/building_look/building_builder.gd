class_name BuildingBuilder
extends RefCounted
## 건물 시안 생성기. docs/systems/building.md 의 구조를 따른다.
## 격자 1칸 = 1 m. 건물은 직사각형 블록의 조합이고, 블록마다 자기 지붕을 얹는다.
## 벽은 칸 경계 위에 서고, 바깥면이 경계선에 맞도록 칸 안쪽으로 두께를 둔다.

const BM := preload("res://prototypes/building_look/materials.gd")
const H := BM.FLOOR_H
const DIRS := [Vector2i(0, 1), Vector2i(0, -1), Vector2i(1, 0), Vector2i(-1, 0)]


static func box(parent: Node3D, size: Vector3, center: Vector3, mat: Material, rot := Vector3.ZERO) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = mat
	mi.position = center
	mi.rotation = rot
	parent.add_child(mi)
	return mi


static func tri_mesh(parent: Node3D, tris: Array, mat: Material) -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for p in tris:
		st.add_vertex(p)
	st.generate_normals()
	var mi := MeshInstance3D.new()
	mi.mesh = st.commit()
	mi.material_override = mat
	parent.add_child(mi)


static func _hash(a: int, b: int, c: int, d: int) -> int:
	var h := a * 73856093 ^ b * 19349663 ^ c * 83492791 ^ d * 2654435
	return absi(h)


# ---------------------------------------------------------------- 벽

## 칸 경계 한 칸짜리 벽. start: 경계 시작점, along: 경계 방향, n: 바깥 방향.
static func _segment(root: Node3D, start: Vector3, along: Vector3, n: Vector3, y0: float, h: float, t: float,
		kind: String, mat: Material, sp: Dictionary) -> void:
	var rects := []
	var age: float = sp.get("age", 0.0)
	var wood := BM.get_mat("wood", age)
	var narrow: bool = sp.get("narrow_windows", false)
	var w0 := 0.42 if narrow else 0.28
	var w1 := 0.58 if narrow else 0.72
	var sill := 1.0 if narrow else 0.95
	var head := 1.9
	match kind:
		"plain":
			rects = [[0.0, 1.0, 0.0, h]]
		"window":
			rects = [[0.0, 1.0, 0.0, sill], [0.0, 1.0, head, h], [0.0, w0, sill, head], [w1, 1.0, sill, head]]
		"door":
			rects = [[0.0, 0.2, 0.0, h], [0.8, 1.0, 0.0, h], [0.2, 0.8, 2.05, h]]
		"low":
			rects = [[0.0, 1.0, 0.0, h]]
		"low_door":
			rects = [[0.0, 0.2, 0.0, h], [0.8, 1.0, 0.0, h]]
	for r in rects:
		_piece(root, start, along, n, y0, r[0], r[1], r[2], r[3], t, 0.0, mat)
	if kind == "window":
		_piece(root, start, along, n, y0, w0, w1, sill, head, 0.04, -t * 0.5, BM.get_mat("glass"))
		_piece(root, start, along, n, y0, w0 - 0.04, w1 + 0.04, sill - 0.06, sill, t + 0.08, 0.04, wood)
		if sp.has("shutter") and not narrow:
			var sm := BM.get_mat("shutter", age, sp["shutter"])
			_piece(root, start, along, n, y0, w0 - 0.22, w0 - 0.02, sill, head, 0.04, 0.03, sm)
			_piece(root, start, along, n, y0, w1 + 0.02, w1 + 0.22, sill, head, 0.04, 0.03, sm)
	elif kind == "door":
		_piece(root, start, along, n, y0, 0.2, 0.8, 0.0, 2.05, 0.06, -t * 0.4, wood)


## 벽 조각 직사각형 하나를 상자로 만든다. off: 바깥 방향으로 밀어내는 거리(벽 바깥면 기준).
static func _piece(root: Node3D, start: Vector3, along: Vector3, n: Vector3, y0: float,
		u0: float, u1: float, v0: float, v1: float, t: float, off: float, mat: Material) -> void:
	if u1 - u0 <= 0.001 or v1 - v0 <= 0.001:
		return
	var c := start + along * (u0 + u1) * 0.5 + Vector3.UP * (y0 + (v0 + v1) * 0.5) - n * (t * 0.5) + n * off
	var size := Vector3(u1 - u0, v1 - v0, t) if absf(along.x) > 0.5 else Vector3(t, v1 - v0, u1 - u0)
	box(root, size, c, mat)


# ---------------------------------------------------------------- 지붕

static func _gable(node: Node3D, along: float, span: float, p: float, th: float, oh: float, og: float,
		roof_mat: Material, gable_mat: Material, ridge_mat: Material) -> float:
	var tp := tan(p)
	var r := span * 0.5 * tp
	for side in [1.0, -1.0]:
		var top := Vector3(0, r, 0)
		var eave := Vector3(0, -oh * tp, side * (span * 0.5 + oh))
		var mid := (top + eave) * 0.5
		var L := top.distance_to(eave)
		var nrm := Vector3(0, cos(p), side * sin(p))
		box(node, Vector3(along + og * 2.0, th, L), mid + nrm * th * 0.5, roof_mat, Vector3(side * p, 0, 0))
	box(node, Vector3(along + og * 2.0 + 0.1, th * 0.9, th * 1.6), Vector3(0, r + th * 0.75, 0), ridge_mat)
	for sx in [-1.0, 1.0]:
		var x: float = sx * (along * 0.5 - 0.1)
		tri_mesh(node, [Vector3(x, 0, -span * 0.5), Vector3(x, r, 0), Vector3(x, 0, span * 0.5)], gable_mat)
	return r


static func _lean(node: Node3D, along: float, span: float, p: float, th: float, oh: float,
		roof_mat: Material, side_mat: Material) -> void:
	var tp := tan(p)
	var r := span * tp
	var top := Vector3(0, r, -span * 0.5 - 0.05)
	var eave := Vector3(0, -oh * tp, span * 0.5 + oh)
	var nrm := Vector3(0, cos(p), sin(p))
	box(node, Vector3(along + 0.3, th, top.distance_to(eave)), (top + eave) * 0.5 + nrm * th * 0.5, roof_mat, Vector3(p, 0, 0))
	for sx in [-1.0, 1.0]:
		var x: float = sx * (along * 0.5 - 0.1)
		tri_mesh(node, [Vector3(x, 0, -span * 0.5), Vector3(x, r, -span * 0.5), Vector3(x, 0, span * 0.5)], side_mat)


static func _pyramid(node: Node3D, w: float, d: float, p: float, oh: float, mat: Material) -> void:
	var r := minf(w, d) * 0.5 * tan(p)
	var a := Vector3(0, r, 0)
	var x := w * 0.5 + oh
	var z := d * 0.5 + oh
	var y := -oh * tan(p)
	var c := [Vector3(-x, y, -z), Vector3(x, y, -z), Vector3(x, y, z), Vector3(-x, y, z)]
	var tris := []
	for i in 4:
		tris.append_array([c[i], a, c[(i + 1) % 4]])
	tri_mesh(node, tris, mat)


static func _crenel(root: Node3D, x0: float, z0: float, w: float, d: float, y: float, mat: Material) -> void:
	var t := 0.45
	var ph := 0.7
	box(root, Vector3(w, 0.25, d), Vector3(x0 + w * 0.5, y + 0.12, z0 + d * 0.5), mat)
	for e in [[Vector3(x0, 0, z0), Vector3(1, 0, 0), w, Vector3(0, 0, -1)], [Vector3(x0, 0, z0 + d), Vector3(1, 0, 0), w, Vector3(0, 0, 1)],
			[Vector3(x0, 0, z0), Vector3(0, 0, 1), d, Vector3(-1, 0, 0)], [Vector3(x0 + w, 0, z0), Vector3(0, 0, 1), d, Vector3(1, 0, 0)]]:
		var s: Vector3 = e[0]
		var al: Vector3 = e[1]
		var len: float = e[2]
		var n: Vector3 = e[3]
		var c := s + al * len * 0.5 + Vector3.UP * (y + ph * 0.5) - n * t * 0.5
		var size := Vector3(len, ph, t) if al.x > 0.5 else Vector3(t, ph, len)
		box(root, size, c, mat)
		var k := 0.0
		while k + 0.5 <= len:
			var mc := s + al * (k + 0.25) + Vector3.UP * (y + ph + 0.3) - n * t * 0.5
			box(root, Vector3(0.5, 0.6, t) if al.x > 0.5 else Vector3(t, 0.6, 0.5), mc, mat)
			k += 1.0


## 성벽 한 구간. a, b: 바닥 평면 위 양 끝점(축 정렬).
static func curtain(root: Node3D, a: Vector2, b: Vector2, t: float, h: float, mat: Material) -> void:
	var along := Vector3(b.x - a.x, 0, b.y - a.y)
	var len := along.length()
	along = along / len
	var c := Vector3((a.x + b.x) * 0.5, h * 0.5, (a.y + b.y) * 0.5)
	box(root, Vector3(len, h, t) if absf(along.x) > 0.5 else Vector3(t, h, len), c, mat)
	var k := 0.25
	while k + 0.5 <= len:
		for side in [-1.0, 1.0]:
			var off: Vector3 = Vector3(-along.z, 0, along.x) * side * (t * 0.5 - 0.15)
			var mc: Vector3 = Vector3(a.x, 0, a.y) + along * (k + 0.25) + off + Vector3.UP * (h + 0.35)
			box(root, Vector3(0.5, 0.7, 0.3) if absf(along.x) > 0.5 else Vector3(0.3, 0.7, 0.5), mc, mat)
		k += 1.0


# ---------------------------------------------------------------- 건물

static func build(sp: Dictionary) -> Node3D:
	var root := Node3D.new()
	var age: float = sp.get("age", 0.0)
	var blocks: Array = sp["blocks"]
	var plinth: float = sp.get("plinth", 0.3)
	var cut: bool = sp.get("cutaway", false)
	var cut_n := [Vector2i(0, 1), Vector2i(-1, 0)]
	var seed_v: int = sp.get("seed", 1)
	var stone := BM.get_mat(sp.get("plinth_mat", "stone"), age)

	# 층별 점유 칸
	var occ := {}
	var max_f := 0
	for bi in blocks.size():
		var b: Dictionary = blocks[bi]
		var fl: int = b.get("floors", 1)
		max_f = max(max_f, fl)
		for f in fl:
			if not occ.has(f):
				occ[f] = {}
			for cx in range(b["x"], b["x"] + b["w"]):
				for cz in range(b["z"], b["z"] + b["d"]):
					var key := Vector2i(cx, cz)
					if not occ[f].has(key):
						occ[f][key] = bi
	# 기단과 바닥
	for b in blocks:
		var bx := float(b["x"])
		var bz := float(b["z"])
		box(root, Vector3(b["w"] + 0.1, plinth, b["d"] + 0.1), Vector3(bx + b["w"] * 0.5, plinth * 0.5, bz + b["d"] * 0.5), stone)
		box(root, Vector3(b["w"] - 0.1, 0.06, b["d"] - 0.1), Vector3(bx + b["w"] * 0.5, plinth + 0.03, bz + b["d"] * 0.5), BM.get_mat("floor", age))
	# 문 위치: 첫 블록의 앞쪽(+z) 바깥벽 중 가운데
	var doors := {}
	if sp.has("doors"):
		for dd in sp["doors"]:
			doors[Vector3i(dd[0], dd[1], 0)] = Vector2i(dd[2], dd[3])
	else:
		var b0: Dictionary = blocks[0]
		var front := []
		for cx in range(b0["x"], b0["x"] + b0["w"]):
			var cz: int = b0["z"] + b0["d"] - 1
			if not occ[0].has(Vector2i(cx, cz + 1)):
				front.append(cx)
		if front.size() > 0:
			doors[Vector3i(front[front.size() / 2], b0["z"] + b0["d"] - 1, 0)] = Vector2i(0, 1)
	# 바깥벽
	var floors_to_build := 1 if cut else max_f
	for f in floors_to_build:
		for cell in occ[f]:
			var bi: int = occ[f][cell]
			var b: Dictionary = blocks[bi]
			var wall_kind: String = b.get("ground", sp.get("ground", "timber")) if f == 0 else b.get("upper", sp.get("upper", sp.get("ground", "timber")))
			var mat := BM.get_mat(wall_kind, age)
			var t: float = b.get("wall_t", 0.35 if wall_kind.begins_with("stone") else 0.22)
			var y0 := plinth + f * H
			for dir in DIRS:
				if occ[f].has(cell + dir):
					continue
				var n := Vector3(dir.x, 0, dir.y)
				var along := Vector3(absf(dir.y), 0, absf(dir.x))
				var start := Vector3(cell.x, 0, cell.y)
				if dir == Vector2i(0, 1): start.z += 1
				if dir == Vector2i(1, 0): start.x += 1
				var is_door: bool = f == 0 and doors.get(Vector3i(cell.x, cell.y, 0), Vector2i(9, 9)) == dir
				var side_a: Vector2i = cell + Vector2i(int(along.x), int(along.z))
				var side_b: Vector2i = cell - Vector2i(int(along.x), int(along.z))
				var corner: bool = not occ[f].has(side_a) or not occ[f].has(side_b) or occ[f].has(side_a + dir) or occ[f].has(side_b + dir)
				var kind := "plain"
				if is_door:
					kind = "door"
				elif not corner and _hash(cell.x, cell.y, f, dir.x * 3 + dir.y + seed_v) % 3 != 0:
					kind = "window"
				if b.get("no_windows", false) and kind == "window":
					kind = "plain"
				var hh := H
				if cut and dir in cut_n:
					kind = "low_door" if is_door else "low"
					hh = 0.9
				_segment(root, start, along, n, y0, hh, t, kind, mat, sp)
				if is_door and not cut:
					box(root, Vector3(1.0, 0.18, 0.5) if absf(n.z) > 0.5 else Vector3(0.5, 0.18, 1.0),
						start + along * 0.5 + n * 0.25 + Vector3.UP * 0.09, stone)
	# 칸막이와 가구 (단면 보기용)
	if cut:
		var wmat := BM.get_mat(sp.get("ground", "timber"), age)
		for pt in sp.get("partitions", []):
			var a := Vector3(pt[0], 0, pt[1])
			var bpt := Vector3(pt[2], 0, pt[3])
			var al := (bpt - a).normalized()
			var len := a.distance_to(bpt)
			var c := (a + bpt) * 0.5 + Vector3.UP * (plinth + 0.45)
			box(root, Vector3(len, 0.9, 0.12) if absf(al.x) > 0.5 else Vector3(0.12, 0.9, len), c, wmat)
		for fu in sp.get("furniture", []):
			box(root, fu[0], fu[1] + Vector3.UP * plinth, BM.get_mat(fu[2], age))
		return root
	# 지붕
	var roof_kind: String = sp.get("roof_mat", "thatch")
	var roof_mat := BM.get_mat(roof_kind, age)
	var ridge_mat := BM.get_mat(roof_kind, minf(age + 0.6, 1.0))
	var th := 0.32 if roof_kind == "thatch" else 0.09
	var oh := 0.5 if roof_kind == "thatch" else 0.35
	for bi in blocks.size():
		var b: Dictionary = blocks[bi]
		var fl: int = b.get("floors", 1)
		var y_top := plinth + fl * H
		var cx: float = b["x"] + b["w"] * 0.5
		var cz: float = b["z"] + b["d"] * 0.5
		var kind: String = b.get("roof", "gable")
		var p := deg_to_rad(b.get("pitch", sp.get("pitch", 45.0)))
		var upper_kind: String = b.get("upper", sp.get("upper", sp.get("ground", "timber"))) if fl > 1 else b.get("ground", sp.get("ground", "timber"))
		var gmat := BM.get_mat(b.get("gable", sp.get("gable", upper_kind)), age)
		var rm := BM.get_mat(b.get("roof_mat", roof_kind), age)
		var node := Node3D.new()
		node.position = Vector3(cx, y_top, cz)
		root.add_child(node)
		match kind:
			"gable":
				var ridge_x: bool = b.get("ridge", "x" if b["w"] >= b["d"] else "z") == "x"
				if not ridge_x:
					node.rotation.y = PI * 0.5
				var along: float = b["w"] if ridge_x else b["d"]
				var span: float = b["d"] if ridge_x else b["w"]
				var r := _gable(node, along, span, p, th, oh, 0.3, rm, gmat, ridge_mat)
				if bi == 0 and sp.get("chimney", false):
					var chx: float = along * 0.5 - 0.9
					box(node, Vector3(0.7, r + 1.6, 0.7), Vector3(chx, (r + 1.6) * 0.5 - 0.3, span * 0.15), stone)
			"lean":
				var dir: Vector2i = b.get("lean", Vector2i(0, 1))
				node.rotation.y = atan2(dir.x, dir.y)
				var along: float = b["w"] if dir.x == 0 else b["d"]
				var span: float = b["d"] if dir.x == 0 else b["w"]
				_lean(node, along, span, deg_to_rad(b.get("pitch", 25.0)), th, oh, rm, gmat)
			"pyramid":
				_pyramid(node, b["w"], b["d"], deg_to_rad(b.get("pitch", 58.0)), 0.3, rm)
			"crenel":
				node.queue_free()
				_crenel(root, b["x"], b["z"], b["w"], b["d"], y_top, BM.get_mat(b.get("ground", "stone"), age))
				if b.get("inner_roof", false):
					var inner := Node3D.new()
					inner.position = Vector3(cx, y_top + 0.25, cz)
					root.add_child(inner)
					_pyramid(inner, b["w"] - 1.4, b["d"] - 1.4, deg_to_rad(50.0), 0.0, rm)
	return root
