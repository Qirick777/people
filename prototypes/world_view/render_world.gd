extends SceneTree
## 시뮬레이션이 내보낸 지역(region_*.json)을 WorldScene 으로 그려 스크린샷을 찍는다.
## 실행: xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 --script prototypes/world_view/render_world.gd -- region_s1.json

const WS := preload("res://game/world_scene.gd")
const Card := preload("res://prototypes/world_view/info_card.gd")
const OUT := "res://docs/images/"

var scene
var data: Dictionary
var card


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var file: String = args[0] if args.size() > 0 else "region_s1.json"
	data = JSON.parse_string(FileAccess.get_file_as_string("res://prototypes/world_view/" + file))
	scene = WS.new()
	root.add_child(scene)
	scene.show_data(data)
	var layer := CanvasLayer.new()
	root.add_child(layer)
	card = Card.new()
	card.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR
	card.anchor_left = 1.0; card.anchor_right = 1.0
	card.offset_left = -398; card.offset_right = -18; card.offset_top = 18
	card.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	layer.add_child(card)
	await _shots(file.get_basename())
	await _ui_shots(file.get_basename())
	quit()


func click(sp: Vector2) -> void:
	var r: Dictionary = scene.pick(sp)
	if r.get("kind", "") == "person":
		card.show_person(r["pd"], data["hinfo"][int(r["pd"]["h"])])
	elif r.get("kind", "") == "house":
		var hi: int = r["hi"]
		card.show_house(data["houses"][hi], data["hinfo"][hi], scene.members_of(hi))
	else:
		card.visible = false


## 대상이 화면 가로 frac 자리(카드에 가리지 않는 왼쪽)에 오도록 카메라를 맞춘다.
func _aim_left(target: Vector3, size: float, frac := 0.36) -> void:
	scene.aim(target, size)
	var vs := root.get_visible_rect().size
	var px_to_m := size / vs.y
	var dx := (vs.x * 0.5 - vs.x * frac) * px_to_m
	scene.aim(target + scene.cam.global_transform.basis.x * dx, size)


## 카드 스크린샷: 일 가진 사람 한 명과 식구 많은 집 하나를 "클릭"해 찍는다.
func _ui_shots(tag: String) -> void:
	var target := -1
	var mid: Vector3 = scene.pos(scene.W / 2, scene.H / 2)
	var bd := 1e9
	var pk: Array = scene.pickables
	for k in pk.size():
		var pd: Dictionary = pk[k]["pd"]
		if pd["job"] in ["농부", "아이", "자작농", "도제"]: continue
		var d: float = pk[k]["node"].global_position.distance_to(mid)
		if pd["job"] == "군인": d -= 2000.0     # 군인을 먼저 보여 준다
		elif pd["job"] == "소작농": d -= 1000.0
		if d < bd: bd = d; target = k
	if target < 0: return
	var tp: Vector3 = pk[target]["node"].global_position
	_aim_left(tp, 34.0)
	await process_frame
	click(scene.cam.unproject_position(tp + Vector3(0, 1.0, 0)))
	await _snap("game_%s_ui_person.png" % tag)
	var best_h := -1
	var most := 0
	var hp: Array = scene.house_pos
	for k in hp.size():
		var n: int = scene.members_of(k).size()
		if data["hinfo"][k].get("class", "") == "지배자": n += 200 + 100 * (int(data["houses"][k][6]) if data["houses"][k].size() > 6 else 0)   # 지배자 집, 큰 집을 먼저
		elif data["hinfo"][k].get("class", "") == "지주": n += 100
		if n > most and hp[k].distance_to(mid) < 160: most = n; best_h = k
	if best_h < 0: return
	_aim_left(hp[best_h], 46.0)
	await process_frame
	click(scene.cam.unproject_position(hp[best_h] + Vector3(0, 2.5, 0)))
	await _snap("game_%s_ui_house.png" % tag)


func _snap(file: String) -> void:
	for i in 6: await process_frame
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png(ProjectSettings.globalize_path(OUT + file))


func _shots(tag: String) -> void:
	# 마을 중심 = 집들의 평균 위치
	var c := Vector3.ZERO
	var near := []
	var mid: Vector3 = scene.pos(scene.W / 2, scene.H / 2)
	for hd in data["houses"]:
		var p: Vector3 = scene.pos(int(hd[0]), int(hd[1]))
		if p.distance_to(mid) < 220: near.append(p)
	for p in near: c += p
	c /= max(1, near.size())
	scene.aim(c, 520.0)
	await _snap("game_%s_far.png" % tag)
	scene.aim(c, 230.0)
	await _snap("game_%s_mid.png" % tag)
	scene.aim(c, 70.0)
	await _snap("game_%s_near.png" % tag)
