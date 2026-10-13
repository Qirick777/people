class_name HumanBuilder
extends RefCounted
## 사람 외형 시안 생성기.
## 몸은 부위별 로프트(단면 고리를 이어 붙인 관) 메시로 만들고,
## 옷과 얼굴은 부위마다 코드로 그린 텍스처로 입힌다.
## 텍스처 좌표: u는 몸을 한 바퀴 도는 값(0 = 등, 0.5 = 정면), v는 위(0)에서 아래(1).

const SIDES := 10


# ---------------------------------------------------------------- 메시

## 점 목록을 따라 타원 단면을 이어 관 모양 메시를 만든다.
## ref: 단면의 "앞"을 정하는 기준 방향. arc: 단면 각도 범위(0~1, 등에서 시작).
static func loft(points: Array, radii: Array, ref: Vector3, cap_top := true, cap_bottom := true,
		arc := Vector2(0.0, 1.0), sides := SIDES) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var n := points.size()
	var lengths: Array[float] = [0.0]
	for i in range(1, n):
		lengths.append(lengths[i - 1] + (points[i] - points[i - 1]).length())
	var total: float = max(lengths[n - 1], 0.0001)
	var frames := []
	for i in n:
		var t: Vector3 = (points[min(i + 1, n - 1)] - points[max(i - 1, 0)]).normalized()
		var zf := (ref - t * ref.dot(t)).normalized()
		var xf := zf.cross(t).normalized()
		frames.append([t, xf, zf])
	var full := arc.y - arc.x >= 0.999
	for i in n:
		var c: Vector3 = points[i]
		var r: Vector2 = radii[i]
		var xf: Vector3 = frames[i][1]
		var zf: Vector3 = frames[i][2]
		for j in sides + 1:
			var u := arc.x + (arc.y - arc.x) * float(j) / sides
			var th := u * TAU
			var p := c + xf * sin(th) * r.x - zf * cos(th) * r.y
			var nr: Vector3 = (xf * sin(th) / max(r.x, 0.001) - zf * cos(th) / max(r.y, 0.001)).normalized()
			st.set_normal(nr)
			st.set_uv(Vector2(u, lengths[i] / total))
			st.add_vertex(p)
	var row := sides + 1
	for i in n - 1:
		for j in sides:
			var a := i * row + j
			var b := a + 1
			var cc := a + row
			var d := cc + 1
			st.add_index(a); st.add_index(cc); st.add_index(b)
			st.add_index(b); st.add_index(cc); st.add_index(d)
	if full:
		var base := n * row
		var caps := []
		if cap_top: caps.append([0, -1.0])
		if cap_bottom: caps.append([n - 1, 1.0])
		for cap in caps:
			var i: int = cap[0]
			var t: Vector3 = frames[i][0] * cap[1]
			st.set_normal(t)
			st.set_uv(Vector2(0.5, lengths[i] / total))
			st.add_vertex(points[i])
			for j in sides:
				var a: int = i * row + j
				if cap[1] < 0.0:
					st.add_index(base); st.add_index(a); st.add_index(a + 1)
				else:
					st.add_index(base); st.add_index(a + 1); st.add_index(a)
			base += 1
	return st.commit()


static func part(parent: Node3D, mesh: Mesh, tex: Texture2D, two_sided := false) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	var mat := StandardMaterial3D.new()
	mat.albedo_texture = tex
	mat.roughness = 0.92
	if two_sided:
		mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mi.material_override = mat
	parent.add_child(mi)
	return mi


# ---------------------------------------------------------------- 텍스처

static func fabric(base: Color, seed_v: int, w := 64, h := 64, weave := 0.035) -> Image:
	var img := Image.create(w, h, false, Image.FORMAT_RGBA8)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_v
	for y in h:
		for x in w:
			var k := 1.0 + rng.randf_range(-0.045, 0.045)
			if y % 2 == 0: k -= weave
			img.set_pixel(x, y, Color(base.r * k, base.g * k, base.b * k))
	return img


static func band(img: Image, v0: float, v1: float, col: Color, u0 := 0.0, u1 := 1.0) -> void:
	var w := img.get_width()
	var h := img.get_height()
	for y in range(int(v0 * h), int(ceil(v1 * h))):
		for x in range(int(u0 * w), int(ceil(u1 * w))):
			if x >= 0 and x < w and y >= 0 and y < h:
				img.set_pixel(x, y, col)


static func tint(img: Image, v0: float, v1: float, k: float, u0 := 0.0, u1 := 1.0) -> void:
	var w := img.get_width()
	var h := img.get_height()
	for y in range(int(v0 * h), int(ceil(v1 * h))):
		for x in range(int(u0 * w), int(ceil(u1 * w))):
			if x >= 0 and x < w and y >= 0 and y < h:
				var c := img.get_pixel(x, y)
				img.set_pixel(x, y, Color(c.r * k, c.g * k, c.b * k))


static func dot(img: Image, u: float, v: float, ru: float, rv: float, col: Color) -> void:
	var w := img.get_width()
	var h := img.get_height()
	for y in range(int((v - rv) * h), int((v + rv) * h) + 1):
		for x in range(int((u - ru) * w), int((u + ru) * w) + 1):
			var du := (float(x) / w - u) / ru
			var dv := (float(y) / h - v) / rv
			if du * du + dv * dv <= 1.0 and x >= 0 and x < w and y >= 0 and y < h:
				img.set_pixel(x, y, col)


static func tex(img: Image) -> ImageTexture:
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


## 얼굴은 이목구비 없이 피부만 칠한다. 옆면을 살짝 어둡게 해 입체감만 준다.
static func face(skin: Color, seed_v: int) -> ImageTexture:
	var img := fabric(skin, seed_v, 64, 64, 0.0)
	tint(img, 0.3, 0.75, 0.92, 0.0, 0.22)
	tint(img, 0.3, 0.75, 0.92, 0.78, 1.0)
	return tex(img)


# ---------------------------------------------------------------- 사람

## d: 외형 사양 사전. 키(height), 체격(build), 성별, 피부, 머리, 옷 종류.
static func build(d: Dictionary) -> Node3D:
	var root := Node3D.new()
	var s: float = d.get("height", 1.0)
	var w: float = d.get("build", 1.0)
	var female: bool = d.get("female", false)
	var child: bool = d.get("child", false)
	var skin: Color = d.get("skin", Color(0.76, 0.59, 0.48))
	var hair_c: Color = d.get("hair_color", Color(0.3, 0.2, 0.12))
	var seed_v: int = d.get("seed", 1)
	var o: Dictionary = d.get("outfit", {})
	var head_k := 1.25 if child else 1.0
	if female: s *= 0.94
	var P := func(x: float, y: float, z := 0.0) -> Vector3: return Vector3(x * w, y, z * w) * s
	var R := func(x: float, z: float) -> Vector2: return Vector2(x * w, z * w) * s
	var skin_tex := tex(fabric(skin, seed_v, 32, 32, 0.0))

	# 몸통
	var sh := 0.17 if female else 0.19
	var t_pts := [P.call(0, 1.5), P.call(0, 1.45), P.call(0, 1.31, 0.005), P.call(0, 1.09), P.call(0, 0.96), P.call(0, 0.87)]
	var t_rad := [R.call(0.06, 0.06), R.call(sh, 0.105), R.call(sh - 0.01, 0.13 if female else 0.12),
		R.call(0.13 if female else 0.15, 0.095), R.call(0.19 if female else 0.17, 0.115), R.call(0.155, 0.09)]
	part(root, loft(t_pts, t_rad, Vector3.BACK), o["torso"].call(seed_v))
	# 목, 머리
	part(root, loft([P.call(0, 1.6), P.call(0, 1.48)], [R.call(0.05, 0.055), R.call(0.055, 0.06)], Vector3.BACK, false, false), skin_tex)
	var hy := 1.665
	var H := func(y: float, z := 0.0) -> Vector3: return Vector3(0, hy + y * head_k, z * head_k) * s
	var HR := func(x: float, z: float) -> Vector2: return Vector2(x, z) * head_k * s
	var head_pts := [H.call(0.125), H.call(0.11), H.call(0.07), H.call(0.02, 0.005), H.call(-0.035, 0.008),
		H.call(-0.075, 0.015), H.call(-0.105, 0.03), H.call(-0.115, 0.035)]
	var head_rad := [HR.call(0.02, 0.025), HR.call(0.06, 0.07), HR.call(0.083, 0.098), HR.call(0.088, 0.102),
		HR.call(0.083, 0.098), HR.call(0.068, 0.082), HR.call(0.042, 0.052), HR.call(0.02, 0.025)]
	part(root, loft(head_pts, head_rad, Vector3.BACK), face(skin, seed_v))
	part(root, loft([H.call(-0.002, 0.098), H.call(-0.032, 0.114)], [HR.call(0.007, 0.007), HR.call(0.012, 0.011)], Vector3.UP), skin_tex)
	# 팔
	for sx in [-1.0, 1.0]:
		var a_pts := [P.call(sx * (sh + 0.01), 1.43), P.call(sx * (sh + 0.045), 1.15), P.call(sx * (sh + 0.075), 0.87)]
		var a_rad := [R.call(0.055, 0.058), R.call(0.042, 0.045), R.call(0.034, 0.036)]
		part(root, loft(a_pts, a_rad, Vector3.BACK), o["arm"].call(seed_v))
		var wr: Vector3 = a_pts[2]
		part(root, loft([wr, wr + Vector3(sx * 0.008, -0.05, 0.01) * s, wr + Vector3(sx * 0.01, -0.095, 0.012) * s],
			[R.call(0.03, 0.022), R.call(0.036, 0.02), R.call(0.022, 0.014)], Vector3.BACK), skin_tex)
	# 다리, 발
	for sx in [-1.0, 1.0]:
		var l_pts := [P.call(sx * 0.085, 0.92), P.call(sx * 0.09, 0.5, 0.01), P.call(sx * 0.095, 0.09)]
		var l_rad := [R.call(0.082, 0.085), R.call(0.056, 0.06), R.call(0.042, 0.044)]
		part(root, loft(l_pts, l_rad, Vector3.BACK), o["leg"].call(seed_v))
		part(root, loft([P.call(sx * 0.095, 0.05, -0.05), P.call(sx * 0.097, 0.045, 0.06), P.call(sx * 0.1, 0.03, 0.17)],
			[R.call(0.045, 0.045), R.call(0.048, 0.04), R.call(0.038, 0.025)], Vector3.UP), o["foot"].call(seed_v))
	# 치마형 (튜닉 자락, 드레스, 로브)
	if o.has("skirt"):
		var hem: float = o["skirt_hem"]
		var flare: float = o.get("skirt_flare", 0.24)
		var k_pts := [P.call(0, 1.02), P.call(0, 0.9), P.call(0, hem)]
		var k_rad := [R.call(0.165 if female else 0.172, 0.12), R.call(0.205, 0.14), R.call(flare, flare * 0.8)]
		part(root, loft(k_pts, k_rad, Vector3.BACK, false, false), o["skirt"].call(seed_v), true)
	# 망토
	if o.has("cloak"):
		var c_pts := [P.call(0, 1.47, -0.01), P.call(0, 1.38, -0.02), P.call(0, 0.35, -0.04)]
		var c_rad := [R.call(0.21, 0.12), R.call(0.235, 0.15), R.call(0.31, 0.21)]
		part(root, loft(c_pts, c_rad, Vector3.BACK, false, false, Vector2(-0.33, 0.33)), o["cloak"].call(seed_v), true)
	# 머리카락
	var hair_tex := tex(fabric(hair_c, seed_v + 7, 64, 64, 0.06))
	var style: String = d.get("hair", "short")
	var cap_low := 0.03
	if style != "bald" and style != "tonsure":
		var cap_pts := [H.call(0.132), H.call(0.115), H.call(0.075), H.call(cap_low, -0.004)]
		var cap_rad := [HR.call(0.025, 0.03), HR.call(0.066, 0.077), HR.call(0.09, 0.106), HR.call(0.095, 0.11)]
		part(root, loft(cap_pts, cap_rad, Vector3.BACK, true, false), hair_tex, true)
		var back_low := {"short": -0.06, "long": -0.33, "bun": -0.04, "braid": -0.05}.get(style, -0.06) as float
		var b_pts := [H.call(cap_low, -0.004), H.call(-0.04, -0.01), H.call(back_low, -0.02 if style != "long" else -0.04)]
		var b_rad := [HR.call(0.095, 0.11), HR.call(0.092, 0.105), HR.call(0.08 if style != "long" else 0.11, 0.085 if style != "long" else 0.07)]
		part(root, loft(b_pts, b_rad, Vector3.BACK, false, false, Vector2(-0.3, 0.3)), hair_tex, true)
		if style == "bun":
			part(root, loft([H.call(0.06, -0.13), H.call(0.02, -0.15), H.call(-0.02, -0.13)],
				[HR.call(0.025, 0.02), HR.call(0.045, 0.035), HR.call(0.025, 0.02)], Vector3.UP), hair_tex)
		if style == "braid":
			var bp := []
			var br := []
			for i in 7:
				bp.append(H.call(-0.05 - i * 0.045, -0.1 - i * 0.004))
				br.append(HR.call(0.022 - i * 0.0015, 0.02 - i * 0.0012))
			part(root, loft(bp, br, Vector3.BACK), hair_tex)
	elif style == "tonsure":
		part(root, loft([H.call(0.08), H.call(0.035, -0.004)], [HR.call(0.088, 0.103), HR.call(0.095, 0.11)],
			Vector3.BACK, false, false), hair_tex, true)
		part(root, loft([H.call(0.035, -0.004), H.call(-0.05, -0.01)], [HR.call(0.095, 0.11), HR.call(0.09, 0.104)],
			Vector3.BACK, false, false, Vector2(-0.3, 0.3)), hair_tex, true)
	# 수염
	var beard: String = d.get("beard", "")
	if beard != "":
		var low := -0.165 if beard == "full" else -0.13
		var bd_pts := [H.call(-0.03, 0.006), H.call(-0.075, 0.018), H.call(-0.105, 0.035), H.call(low, 0.045)]
		var bd_rad := [HR.call(0.089, 0.103), HR.call(0.074, 0.088), HR.call(0.05, 0.062), HR.call(0.022, 0.03)]
		part(root, loft(bd_pts, bd_rad, Vector3.BACK, false, true, Vector2(0.2, 0.8)), hair_tex, true)
		part(root, loft([H.call(-0.048, 0.1), H.call(-0.06, 0.1)], [HR.call(0.035, 0.012), HR.call(0.04, 0.012)],
			Vector3.BACK, true, true, Vector2(0.0, 1.0), 6), hair_tex)
	# 쓰개
	var hat: String = d.get("hat", "")
	if hat == "coif":
		var cf_tex := tex(fabric(o.get("coif_color", Color(0.9, 0.88, 0.82)), seed_v + 3))
		part(root, loft([H.call(0.14), H.call(0.12), H.call(0.07), H.call(0.02, -0.01), H.call(-0.07, -0.03)],
			[HR.call(0.03, 0.035), HR.call(0.072, 0.083), HR.call(0.097, 0.113), HR.call(0.1, 0.116), HR.call(0.095, 0.1)],
			Vector3.BACK, true, false, Vector2(-0.37, 0.37)), cf_tex, true)
	elif hat == "kettle":
		var steel := tex(fabric(Color(0.55, 0.56, 0.58), seed_v + 5, 32, 32, 0.0))
		part(root, loft([H.call(0.17), H.call(0.15), H.call(0.09), H.call(0.05)],
			[HR.call(0.03, 0.035), HR.call(0.08, 0.09), HR.call(0.105, 0.12), HR.call(0.108, 0.122)], Vector3.BACK, true, false), steel, true)
		part(root, loft([H.call(0.052), H.call(0.05), H.call(0.03)],
			[HR.call(0.108, 0.122), HR.call(0.16, 0.175), HR.call(0.17, 0.185)], Vector3.BACK, false, false), steel, true)
	elif hat == "hood_down":
		var hd_tex: Texture2D = o["cloak"].call(seed_v) if o.has("cloak") else o["torso"].call(seed_v)
		part(root, loft([P.call(0, 1.6, -0.06), P.call(0, 1.5, -0.08), P.call(0, 1.42, -0.06)],
			[R.call(0.1, 0.08), R.call(0.14, 0.12), R.call(0.17, 0.13)], Vector3.BACK, false, false, Vector2(-0.32, 0.32)), hd_tex, true)
	return root
