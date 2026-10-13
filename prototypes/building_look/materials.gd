class_name BuildingMaterials
extends RefCounted
## 건물 재질 시안. 텍스처를 코드로 그리고, 월드 좌표 삼면 투영(triplanar)으로 입힌다.
## 그래서 벽 조각마다 UV를 따로 맞추지 않아도 무늬가 이어진다.

const FLOOR_H := 2.6

static var _cache := {}


static func _img(w: int, h: int, base: Color, seed_v: int, jitter := 0.05) -> Image:
	var img := Image.create(w, h, false, Image.FORMAT_RGBA8)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_v
	for y in h:
		for x in w:
			var k := 1.0 + rng.randf_range(-jitter, jitter)
			img.set_pixel(x, y, Color(base.r * k, base.g * k, base.b * k))
	return img


static func _rect(img: Image, x0: int, y0: int, x1: int, y1: int, c: Color) -> void:
	for y in range(max(y0, 0), min(y1, img.get_height())):
		for x in range(max(x0, 0), min(x1, img.get_width())):
			img.set_pixel(x, y, c)


static func _mul(img: Image, x0: int, y0: int, x1: int, y1: int, k: float) -> void:
	for y in range(max(y0, 0), min(y1, img.get_height())):
		for x in range(max(x0, 0), min(x1, img.get_width())):
			var c := img.get_pixel(x, y)
			img.set_pixel(x, y, Color(c.r * k, c.g * k, c.b * k))


static func _line(img: Image, a: Vector2, b: Vector2, wpx: int, c: Color) -> void:
	var n := int(a.distance_to(b))
	for i in n + 1:
		var p := a.lerp(b, float(i) / max(n, 1))
		_rect(img, int(p.x) - wpx / 2, int(p.y) - wpx / 2, int(p.x) + wpx / 2 + 1, int(p.y) + wpx / 2 + 1, c)


## 목골조: 텍스처 한 장 = 가로 2 m × 세로 한 층.
static func _timber(seed_v: int, plaster: Color, beam: Color) -> Image:
	var s := 256
	var img := _img(s, s, plaster, seed_v, 0.035)
	var bw := 10
	for x in [0, s / 2]:
		_rect(img, x - bw / 2, 0, x + bw / 2, s, beam)
	_rect(img, 0, 0, s, bw, beam)
	_rect(img, 0, s - bw, s, s, beam)
	_rect(img, 0, int(s * 0.62), s, int(s * 0.62) + bw - 2, beam)
	_line(img, Vector2(6, s - 8), Vector2(s / 2 - 6, int(s * 0.64)), 8, beam)
	_line(img, Vector2(s / 2 + 6, int(s * 0.62)), Vector2(s - 6, 8), 8, beam)
	return img


static func _stone(seed_v: int, base: Color) -> Image:
	var s := 256
	var img := _img(s, s, base.darkened(0.35), seed_v, 0.02)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_v
	var rows := 8
	for r in rows:
		var y0 := r * s / rows
		var x := -rng.randi_range(0, 40)
		while x < s:
			var w := rng.randi_range(36, 80)
			var k := rng.randf_range(0.85, 1.12)
			var c := Color(base.r * k, base.g * k, base.b * k)
			_rect(img, x + 2, y0 + 2, x + w - 2, y0 + s / rows - 2, c)
			_mul(img, x + 2, y0 + s / rows - 6, x + w - 2, y0 + s / rows - 2, 0.85)
			x += w
	return img


static func _rows(seed_v: int, base: Color, rows: int, gap: float, jitter := 0.1) -> Image:
	var s := 128
	var img := _img(s, s, base, seed_v, 0.04)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_v
	for r in rows:
		var y0 := r * s / rows
		_mul(img, 0, y0, s, y0 + 2, gap)
		var x := (r % 2) * 8
		while x < s:
			_mul(img, x, y0, x + 1, y0 + s / rows, gap)
			_mul(img, x + 1, y0, x + 16, y0 + s / rows, rng.randf_range(1.0 - jitter, 1.0 + jitter))
			x += 16
	return img


static func _thatch(seed_v: int) -> Image:
	var s := 128
	var img := _img(s, s, Color(0.55, 0.45, 0.28), seed_v, 0.02)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_v
	for i in 900:
		var x := rng.randi_range(0, s - 1)
		var y := rng.randi_range(0, s - 1)
		var k := rng.randf_range(0.7, 1.2)
		_mul(img, x, y, x + 1, y + rng.randi_range(6, 20), k)
	for r in 6:
		_mul(img, 0, r * s / 6, s, r * s / 6 + 3, 0.82)
	return img


static func _planks(seed_v: int, base: Color, vertical := true) -> Image:
	var s := 128
	var img := _img(s, s, base, seed_v, 0.06)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_v
	var x := 0
	while x < s:
		var w := rng.randi_range(12, 20)
		var k := rng.randf_range(0.88, 1.1)
		if vertical:
			_mul(img, x, 0, x + w, s, k)
			_mul(img, x, 0, x + 2, s, 0.55)
		else:
			_mul(img, 0, x, s, x + w, k)
			_mul(img, 0, x, s, x + 2, 0.55)
		x += w
	return img


static func _make(kind: String) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.roughness = 0.95
	var img: Image
	var scale := Vector3(0.5, 0.5, 0.5)
	match kind:
		"timber":
			img = _timber(11, Color(0.86, 0.82, 0.72), Color(0.24, 0.16, 0.1))
			scale = Vector3(0.5, 1.0 / FLOOR_H, 0.5)
		"daub":
			img = _timber(12, Color(0.74, 0.64, 0.46), Color(0.36, 0.27, 0.18))
			scale = Vector3(0.5, 1.0 / FLOOR_H, 0.5)
		"stone":
			img = _stone(13, Color(0.6, 0.58, 0.53))
		"stone_light":
			img = _stone(14, Color(0.72, 0.68, 0.58))
		"planks":
			img = _planks(15, Color(0.45, 0.33, 0.22))
		"wood":
			img = _planks(16, Color(0.36, 0.25, 0.16))
			scale = Vector3(1.0, 1.0, 1.0)
		"floor":
			img = _planks(17, Color(0.5, 0.38, 0.26), false)
		"thatch":
			img = _thatch(18)
			scale = Vector3(0.6, 0.6, 0.6)
		"shingle":
			img = _rows(19, Color(0.4, 0.3, 0.22), 8, 0.6, 0.15)
			scale = Vector3(0.8, 0.8, 0.8)
		"slate":
			img = _rows(20, Color(0.3, 0.32, 0.36), 10, 0.55, 0.12)
			scale = Vector3(0.8, 0.8, 0.8)
		"tile":
			img = _rows(21, Color(0.62, 0.3, 0.2), 9, 0.6, 0.12)
			scale = Vector3(0.8, 0.8, 0.8)
		"glass":
			img = _img(8, 8, Color(0.1, 0.1, 0.11), 22, 0.0)
		"linen":
			img = _img(16, 16, Color(0.82, 0.78, 0.68), 23)
		"ember":
			img = _img(8, 8, Color(0.9, 0.42, 0.15), 24, 0.1)
			m.emission_enabled = true
			m.emission = Color(0.9, 0.35, 0.1)
			m.emission_energy_multiplier = 0.6
		"dirt":
			img = _img(128, 128, Color(0.42, 0.34, 0.24), 25, 0.08)
			scale = Vector3(0.25, 0.25, 0.25)
		"leaf":
			img = _img(64, 64, Color(0.24, 0.38, 0.18), 26, 0.12)
		_:
			img = _img(8, 8, Color.MAGENTA, 0)
	img.generate_mipmaps()
	m.albedo_texture = ImageTexture.create_from_image(img)
	m.uv1_triplanar = true
	m.uv1_world_triplanar = true
	m.uv1_scale = scale
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	return m


## 재질 하나를 낡음 정도별로 공유한다. age 0 = 새것, 1 = 아주 낡음.
static func get_mat(kind: String, age := 0.0, shutter := Color()) -> StandardMaterial3D:
	var key := "%s_%.1f_%s" % [kind, age, shutter.to_html()]
	if _cache.has(key):
		return _cache[key]
	var m: StandardMaterial3D
	if kind == "shutter":
		m = _make("wood").duplicate()
		m.albedo_color = shutter
	else:
		m = _make(kind)
	if age > 0.0:
		m.albedo_color = m.albedo_color * Color(1.0 - age * 0.25, 1.0 - age * 0.2, 1.0 - age * 0.28)
	_cache[key] = m
	return m
