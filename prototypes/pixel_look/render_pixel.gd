extends SceneTree
## 도트 그래픽 흉내 시안.
## 1) 3D를 저해상도(480x270)로 렌더링하고 최근접 필터로 3배 키운다.
## 2) 카메라를 픽셀(텍셀) 격자에 맞춰 움직여 화면이 기어가지 않게 한다.
## 3) 맞추느라 생긴 1픽셀 미만 오차는 확대한 화면을 그만큼 밀어서 메운다. 그래서 스크롤은 부드럽다.
## 실행: xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1440x810 --script prototypes/pixel_look/render_pixel.gd

const VS := preload("res://prototypes/building_look/village.gd")
const HB := preload("res://prototypes/human_look/human_builder.gd")
const OUT := "res://docs/images/"
const FRAMES := "/tmp/claude-0/pixel_frames/"
const LOW := Vector2i(480, 270)
const MARGIN := 4
const SCALE := 3
const ISO := Vector3(-1.0, 1.25, 1.0)

var vp: SubViewport
var cam: Camera3D


func _init() -> void:
	vp = SubViewport.new()
	vp.size = LOW + Vector2i(MARGIN * 2, MARGIN * 2)
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	vp.own_world_3d = true
	root.add_child(vp)
	var world := Node3D.new()
	vp.add_child(world)
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
	world.add_child(sun)
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(400, 400)
	ground.mesh = pm
	var gm := StandardMaterial3D.new()
	gm.albedo_texture = HB.tex(HB.fabric(Color(0.3, 0.37, 0.21), 99, 256, 256, 0.0))
	gm.uv1_scale = Vector3(80, 80, 1)
	ground.material_override = gm
	world.add_child(ground)
	VS.build(world, Vector3.ZERO)
	_nearest(world)
	cam = Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.far = 1000.0
	world.add_child(cam)
	DirAccess.make_dir_recursive_absolute(FRAMES)

	await _frames(20)
	# 정지 화면 두 장
	cam.size = 18.0 * vp.size.y / LOW.y
	_place(Vector3(23, 1, 1), true)
	await _save_still("pixel_village.png")
	cam.size = 9.0 * vp.size.y / LOW.y
	_place(Vector3(22, 1, -3), true)
	await _save_still("pixel_close.png")

	# 카메라 이동 비교: 맞춤 없음 / 맞춤 + 오차 보정
	cam.size = 12.0 * vp.size.y / LOW.y
	var f := FileAccess.open(FRAMES + "offsets.txt", FileAccess.WRITE)
	for snap in [false, true]:
		for i in 48:
			var target := Vector3(14, 1, 0) + Vector3(1, 0, 1).normalized() * (i * 0.023)
			var frac := _place(target, snap)
			await _frames(2)
			vp.get_texture().get_image().save_png(FRAMES + "%s_%02d.png" % ["snap" if snap else "raw", i])
			f.store_line("%s %d %f %f" % ["snap" if snap else "raw", i, frac.x, frac.y])
	f.close()
	quit()


## 모든 재질의 텍스처 필터를 최근접으로 바꾼다. 텍스처 픽셀이 뭉개지지 않는다.
func _nearest(n: Node) -> void:
	if n is MeshInstance3D and n.material_override is BaseMaterial3D:
		(n.material_override as BaseMaterial3D).texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	for c in n.get_children():
		_nearest(c)


## 카메라를 놓는다. snap이면 화면 평면에서 텍셀 단위로 반올림하고, 남은 오차(픽셀 단위)를 돌려준다.
func _place(target: Vector3, snap: bool) -> Vector2:
	var pos := target + ISO.normalized() * 300.0
	cam.look_at_from_position(pos, target)
	if not snap:
		return Vector2.ZERO
	var texel := cam.size / vp.size.y
	var right := cam.global_transform.basis.x
	var up := cam.global_transform.basis.y
	var a := pos.dot(right)
	var b := pos.dot(up)
	var sa := roundf(a / texel) * texel
	var sb := roundf(b / texel) * texel
	cam.global_position = pos + right * (sa - a) + up * (sb - b)
	return Vector2((a - sa) / texel, (b - sb) / texel)


func _frames(n: int) -> void:
	for i in n:
		await process_frame
	await RenderingServer.frame_post_draw


func _save_still(file: String) -> void:
	await _frames(4)
	var img := vp.get_texture().get_image()
	img = img.get_region(Rect2i(MARGIN, MARGIN, LOW.x, LOW.y))
	img.save_png(FRAMES + "low_" + file)
	img.resize(LOW.x * SCALE, LOW.y * SCALE, Image.INTERPOLATE_NEAREST)
	img.save_png(ProjectSettings.globalize_path(OUT + file))
