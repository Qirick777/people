class_name BlockyHuman
extends RefCounted
## 블록풍 사람. 모든 부위가 단색 상자다. 텍스처가 없다.
## cute = true면 머리를 키우고 다리를 줄이고 모서리를 깎는다.

const BB := preload("res://prototypes/building_look/building_builder.gd")
const BM := preload("res://prototypes/building_look/materials.gd")

const LINEN := Color(0.9, 0.86, 0.76)
const BROWN := Color(0.5, 0.36, 0.24)
const RUSSET := Color(0.66, 0.36, 0.22)
const MADDER := Color(0.7, 0.22, 0.18)
const WOAD := Color(0.3, 0.42, 0.66)
const GREEN := Color(0.38, 0.52, 0.3)
const GREY := Color(0.58, 0.58, 0.55)
const BLACK := Color(0.2, 0.19, 0.19)
const GOLD := Color(0.88, 0.7, 0.3)
const LEATHER := Color(0.38, 0.26, 0.17)
const PADDED := Color(0.74, 0.66, 0.5)
const STEEL := Color(0.62, 0.64, 0.68)


static func _b(parent: Node3D, size: Vector3, c: Vector3, col: Color, rot := Vector3.ZERO) -> void:
	BB.box(parent, size, c, BM.flat_mat(col), rot)


## 위아래 크기가 다른 상자(치마, 망토). 각진 사다리꼴 기둥.
static func _taper(parent: Node3D, top: Vector2, bottom: Vector2, y_top: float, y_bot: float, z: float, col: Color) -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var t := [Vector3(-top.x, y_top, -top.y), Vector3(top.x, y_top, -top.y), Vector3(top.x, y_top, top.y), Vector3(-top.x, y_top, top.y)]
	var b := [Vector3(-bottom.x, y_bot, -bottom.y), Vector3(bottom.x, y_bot, -bottom.y), Vector3(bottom.x, y_bot, bottom.y), Vector3(-bottom.x, y_bot, bottom.y)]
	var quads := [[t[0], t[1], t[2], t[3]], [b[3], b[2], b[1], b[0]]]
	for k in 4:
		quads.append([t[k], b[k], b[(k + 1) % 4], t[(k + 1) % 4]])
	for q in quads:
		var nrm: Vector3 = (q[1] - q[0]).cross(q[2] - q[0]).normalized()
		var c: Vector3 = (q[0] + q[2]) * 0.5
		c.y -= (y_top + y_bot) * 0.5
		if nrm.dot(c) < 0.0:
			q.reverse()
			nrm = -nrm
		for p in [q[0], q[2], q[1], q[0], q[3], q[2]]:
			st.set_normal(nrm)
			st.add_vertex(p + Vector3(0, 0, z))
	var mi := MeshInstance3D.new()
	mi.mesh = st.commit()
	mi.material_override = BM.flat_mat(col)
	parent.add_child(mi)


## 옷 사양: top, sleeve, legs, feet, belt, skirt(색, 단 높이 비율, 퍼짐), apron, tabard, cloak, trim, sleeve_len
static func outfit(kind: String, c1 := BROWN, c2 := GREY) -> Dictionary:
	match kind:
		"peasant_m":
			return {"top": c1, "legs": c2, "feet": LEATHER, "belt": LEATHER, "skirt": [c1, 0.72, 1.1]}
		"peasant_f":
			return {"top": c1, "legs": BLACK, "feet": LEATHER, "belt": BROWN, "skirt": [c1, 0.02, 1.45], "apron": c2, "collar": LINEN}
		"smith":
			return {"top": LINEN.darkened(0.12), "legs": BROWN.darkened(0.3), "feet": LEATHER.darkened(0.3), "sleeve_len": 0.5,
				"skirt": [LINEN.darkened(0.25), 0.62, 1.05], "apron": LEATHER, "apron_high": true}
		"soldier":
			return {"top": PADDED, "legs": BROWN, "feet": LEATHER, "belt": LEATHER, "skirt": [PADDED, 0.68, 1.1], "tabard": c1}
		"noble_m":
			return {"top": c1, "legs": BLACK, "feet": BLACK, "belt": BLACK, "skirt": [c1, 0.8, 1.05], "trim": GOLD, "cloak": c2}
		"noble_f":
			return {"top": c1, "legs": BLACK, "feet": BLACK, "belt": GOLD, "skirt": [c1, 0.0, 1.6], "trim": GOLD, "collar": LINEN}
		"monk":
			var robe := BROWN.darkened(0.12)
			return {"top": robe, "legs": robe, "feet": LEATHER, "belt": Color(0.78, 0.68, 0.48), "skirt": [robe, 0.02, 1.35], "hood": robe}
	return {"top": c1, "legs": c2, "feet": LEATHER}


static func build(d: Dictionary, cute := false) -> Node3D:
	var root := Node3D.new()
	var s: float = d.get("height", 1.0)
	var w: float = d.get("build", 1.0)
	var female: bool = d.get("female", false)
	var child: bool = d.get("child", false)
	var skin: Color = d.get("skin", Color(0.86, 0.68, 0.55))
	var hair: Color = d.get("hair_color", Color(0.35, 0.24, 0.15))
	var o: Dictionary = d.get("outfit", {})
	if female: s *= 0.94
	var old_r := BB.round_r
	BB.round_r = 0.035 if cute else 0.0
	# 비율 (m, 키 1.0 기준 사람 1.75 m)
	var hs := (0.34 if cute else 0.25) * ((1.08 if cute else 1.2) if child else 1.0)
	var leg_h := 0.6 if cute else 0.8
	var torso_h := 0.52 if cute else 0.58
	var sw := (0.36 if female else 0.4) * w * (1.1 if cute else 1.0)
	var td := 0.22 * w
	var P := func(x: float, y: float, z := 0.0) -> Vector3: return Vector3(x, y * s, z)
	var S := func(x: float, y: float, z: float) -> Vector3: return Vector3(x, y * s, z)
	# 발, 다리
	var foot_h := 0.08
	for sx in [-1.0, 1.0]:
		var lx: float = sx * 0.085 * w
		_b(root, S.call(0.12 * w, foot_h, 0.24), P.call(lx, foot_h * 0.5, 0.03), o.get("feet", LEATHER))
		_b(root, S.call(0.13 * w, leg_h - foot_h, 0.15 * w), P.call(lx, foot_h + (leg_h - foot_h) * 0.5), o.get("legs", GREY))
	# 몸통
	var ty := leg_h + torso_h * 0.5
	_b(root, S.call(sw, torso_h, td), P.call(0, ty), o.get("top", BROWN))
	var top_y := leg_h + torso_h
	if o.has("collar"):
		_b(root, S.call(sw * 0.45, 0.08, 0.02), P.call(0, top_y - 0.05, td * 0.5 + 0.005), o["collar"])
	if o.has("trim"):
		_b(root, S.call(sw + 0.01, 0.04, td + 0.01), P.call(0, top_y - 0.02), o["trim"])
	# 치마형
	if o.has("skirt"):
		var sk: Array = o["skirt"]
		var hem_y: float = lerpf(0.0, leg_h, sk[1]) * s
		var wy: float = (leg_h + 0.08) * s
		_taper(root, Vector2(sw * 0.52, td * 0.56), Vector2(sw * 0.52 * sk[2], td * 0.56 * sk[2]), wy, hem_y, 0.0, sk[0])
		if o.has("trim"):
			_taper(root, Vector2(sw * 0.53 * sk[2], td * 0.57 * sk[2]), Vector2(sw * 0.53 * sk[2], td * 0.57 * sk[2]), hem_y + 0.05, hem_y, 0.0, o["trim"])
	if o.has("belt"):
		_b(root, S.call(sw + 0.02, 0.05, td + 0.02), P.call(0, leg_h + 0.1), o["belt"])
	if o.has("apron"):
		var ay_top: float = (leg_h + torso_h * 0.75) if o.get("apron_high", false) else (leg_h + 0.08)
		var ay_bot: float = leg_h * 0.35
		_b(root, S.call(sw * 0.62, ay_top - ay_bot, 0.02), P.call(0, (ay_top + ay_bot) * 0.5, td * 0.5 + 0.06), o["apron"])
	if o.has("tabard"):
		_b(root, S.call(sw * 0.55, torso_h + leg_h * 0.3, 0.02), P.call(0, top_y - (torso_h + leg_h * 0.3) * 0.5, td * 0.5 + 0.05), o["tabard"])
		_b(root, S.call(sw * 0.55, torso_h + leg_h * 0.3, 0.02), P.call(0, top_y - (torso_h + leg_h * 0.3) * 0.5, -td * 0.5 - 0.05), o["tabard"])
	if o.has("cloak"):
		_taper(root, Vector2(sw * 0.55, 0.02), Vector2(sw * 0.75, 0.02), top_y * s, leg_h * 0.3 * s, -td * 0.5 - 0.04, o["cloak"])
	# 팔
	var arm_h := torso_h * 1.02
	var sl: float = o.get("sleeve_len", 1.0)
	for sx in [-1.0, 1.0]:
		var ax: float = sx * (sw * 0.5 + 0.055 * w)
		var a_top := top_y - 0.02
		var sleeve_h := arm_h * sl
		_b(root, S.call(0.1 * w, sleeve_h, 0.12 * w), P.call(ax, a_top - sleeve_h * 0.5), o.get("sleeve", o.get("top", BROWN)))
		if sl < 1.0:
			_b(root, S.call(0.09 * w, arm_h - sleeve_h, 0.11 * w), P.call(ax, a_top - sleeve_h - (arm_h - sleeve_h) * 0.5), skin)
		_b(root, S.call(0.09 * w, 0.09, 0.1 * w), P.call(ax, a_top - arm_h - 0.045), skin)
	# 머리
	var hy := top_y * s + 0.02 + hs * 0.5
	var head_c := Vector3(0, hy, 0)
	_b(root, Vector3(hs, hs, hs), head_c, skin)
	_b(root, Vector3(hs * 0.14, hs * 0.18, hs * 0.12), head_c + Vector3(0, -hs * 0.05, hs * 0.53), skin.darkened(0.06))
	var style: String = d.get("hair", "short")
	var e := 0.03
	var hb := hs + e
	if style in ["short", "long", "bun", "braid"]:
		_b(root, Vector3(hb, hs * 0.3, hb), head_c + Vector3(0, hs * 0.37, 0), hair)
		var back_h := hs * (1.5 if style == "long" else 0.8)
		_b(root, Vector3(hb, back_h, hs * 0.22), head_c + Vector3(0, hs * 0.52 - back_h * 0.5, -hs * 0.43), hair)
		for sx in [-1.0, 1.0]:
			_b(root, Vector3(e + 0.02, hs * 0.5, hs * 0.65), head_c + Vector3(sx * hs * 0.5, hs * 0.2, -hs * 0.15), hair)
		if style == "bun":
			_b(root, Vector3(hs * 0.42, hs * 0.38, hs * 0.3), head_c + Vector3(0, hs * 0.25, -hs * 0.65), hair)
		if style == "braid":
			for k in 4:
				_b(root, Vector3(hs * 0.2, hs * 0.22, hs * 0.2), head_c + Vector3(0, -hs * 0.35 - k * hs * 0.23, -hs * 0.6), hair)
	elif style == "tonsure":
		_b(root, Vector3(hb, hs * 0.22, hb), head_c + Vector3(0, hs * 0.18, 0), hair)
	var beard: String = d.get("beard", "")
	if beard != "":
		var bh := hs * (0.62 if beard == "full" else 0.42)
		_b(root, Vector3(hb, bh, hs * 0.2), head_c + Vector3(0, -hs * 0.5 + bh * 0.5 - (hs * 0.15 if beard == "full" else 0.0), hs * 0.45), hair)
	var hat: String = d.get("hat", "")
	if hat == "coif":
		var cc := LINEN
		_b(root, Vector3(hb + 0.01, hs * 0.36, hb + 0.01), head_c + Vector3(0, hs * 0.36, -0.005), cc)
		_b(root, Vector3(hb + 0.01, hs * 1.0, hs * 0.25), head_c + Vector3(0, hs * 0.1, -hs * 0.42), cc)
		for sx in [-1.0, 1.0]:
			_b(root, Vector3(e + 0.025, hs * 0.85, hs * 0.75), head_c + Vector3(sx * hs * 0.51, hs * 0.05, -hs * 0.12), cc)
	elif hat == "kettle":
		_b(root, Vector3(hs * 1.65, 0.035, hs * 1.65), head_c + Vector3(0, hs * 0.38, 0), STEEL)
		_b(root, Vector3(hs * 1.08, hs * 0.38, hs * 1.08), head_c + Vector3(0, hs * 0.58, 0), STEEL)
	if o.has("hood"):
		_b(root, Vector3(hs * 1.1, hs * 0.45, hs * 0.4), Vector3(0, top_y * s + 0.04, -td * 0.5 - hs * 0.1), o["hood"])
	BB.round_r = old_r
	if cute:
		root.scale = Vector3.ONE * 1.1
	return root


## 줄 세우기용 열 명
static func lineup() -> Array:
	var tan := Color(0.72, 0.53, 0.4)
	return [
		{"outfit": outfit("peasant_m", BROWN, GREY), "hair": "short", "beard": "short", "hair_color": Color(0.38, 0.25, 0.15)},
		{"outfit": outfit("peasant_f", RUSSET, LINEN), "female": true, "hat": "coif"},
		{"outfit": outfit("smith"), "skin": tan, "hair": "bald", "beard": "full", "hair_color": Color(0.13, 0.1, 0.09), "build": 1.15},
		{"outfit": outfit("soldier", MADDER), "hat": "kettle", "hair": "short", "hair_color": Color(0.5, 0.36, 0.2)},
		{"outfit": outfit("noble_m", WOAD, MADDER), "hair": "short", "hair_color": Color(0.88, 0.74, 0.45), "height": 1.04, "skin": Color(0.92, 0.76, 0.64)},
		{"outfit": outfit("noble_f", GREEN), "female": true, "hair": "braid", "hair_color": Color(0.72, 0.32, 0.16), "skin": Color(0.93, 0.78, 0.67)},
		{"outfit": outfit("monk"), "hair": "tonsure", "hair_color": Color(0.45, 0.42, 0.38), "build": 0.92},
		{"outfit": outfit("peasant_f", WOAD, LINEN.darkened(0.06)), "female": true, "hair": "bun", "hair_color": Color(0.78, 0.76, 0.72), "build": 1.08},
		{"outfit": outfit("peasant_m", GREEN, BROWN), "hair": "long", "hair_color": Color(0.24, 0.17, 0.11), "skin": Color(0.78, 0.6, 0.46), "build": 0.9, "height": 1.05},
		{"outfit": outfit("peasant_m", LINEN.darkened(0.08), BROWN), "child": true, "hair": "short", "hair_color": Color(0.9, 0.78, 0.5), "height": 0.62, "build": 0.8},
	]


static func random(rng: RandomNumberGenerator, cute: bool) -> Node3D:
	var female := rng.randf() < 0.5
	var cloth := [LINEN, BROWN, RUSSET, WOAD, GREEN, GREY, MADDER]
	var c1: Color = cloth[rng.randi() % cloth.size()]
	var c2: Color = cloth[rng.randi() % cloth.size()]
	var hairs := [Color(0.13, 0.1, 0.09), Color(0.38, 0.25, 0.15), Color(0.88, 0.74, 0.45), Color(0.72, 0.32, 0.16), Color(0.78, 0.76, 0.72)]
	var skins := [Color(0.93, 0.78, 0.67), Color(0.86, 0.68, 0.55), Color(0.78, 0.6, 0.46), Color(0.66, 0.48, 0.35)]
	var d := {"female": female, "skin": skins[rng.randi() % skins.size()], "hair_color": hairs[rng.randi() % hairs.size()],
		"height": rng.randf_range(0.93, 1.07), "build": rng.randf_range(0.88, 1.15)}
	var roll := rng.randf()
	if roll < 0.08:
		d["outfit"] = outfit("soldier", MADDER)
		d["hat"] = "kettle"
	elif female:
		d["outfit"] = outfit("peasant_f", c1, LINEN)
		d["hair"] = ["short", "long", "bun", "braid"][rng.randi() % 4]
		if rng.randf() < 0.35: d["hat"] = "coif"
	else:
		d["outfit"] = outfit("peasant_m", c1, c2)
		d["hair"] = ["short", "short", "long", "bald"][rng.randi() % 4]
		if rng.randf() < 0.4: d["beard"] = "full" if rng.randf() < 0.5 else "short"
	if rng.randf() < 0.15:
		d["child"] = true
		d["height"] = rng.randf_range(0.55, 0.72)
		d["build"] = 0.8
	return build(d, cute)
