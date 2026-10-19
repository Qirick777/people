extends Node
## 실시간 게임 화면. SimHost(C#)가 세계를 돌리고, 이 스크립트는 그리고 묻고 시킨다.
## 위: 날짜·속도·가문. 왼쪽: 내 가족. 오른쪽: 인물·집 카드와 할 수 있는 일. 아래: 연대기.
## 조작: 왼쪽 클릭 고르기, 오른쪽 끌기·WASD 화면 옮기기, 휠 확대, 스페이스 멈춤, 1·2·3 속도.
## 자동 점검: godot --path . -- --autotest  (짧게 세계를 만들고 집을 골라 몇 달 돌린 뒤 스크린샷을 찍는다)

const WS := preload("res://game/world_scene.gd")
const Card := preload("res://prototypes/world_view/info_card.gd")
const SPEEDS := [0, 1, 4, 12]           # 초당 날 수(한 해 = 24일, 계절 = 6일)
const REGION := Vector2i(120, 90)       # 그리는 영역(타일)
const PAPER := Color(0.965, 0.93, 0.85)
const INK := Color(0.27, 0.19, 0.12)
const MUTED := Color(0.5, 0.41, 0.31)

var host: Node
var scene
var card
var data: Dictionary
var origin := Vector2i.ZERO             # 영역 왼쪽 위(세계 타일 좌표)
var speed := 0
var acc := 0.0
var since_draw := 0
var actor := -1                          # 상호작용을 할 내 가족(사람 번호)
var shown := {}                          # 카드에 띄운 것 {"kind", "uid" | "id"}
var autotest := false

var date_label: Label
var family_label: Label
var speed_buttons := []
var family_box: VBoxContainer
var log_box: VBoxContainer
var hint_label: Label
var boot_panel: PanelContainer
var boot_label: Label
var font_r: FontFile
var font_b: FontFile


func _ready() -> void:
	autotest = "--autotest" in OS.get_cmdline_user_args()
	font_r = Card._font("NotoSansKR-Medium.otf")
	font_b = Card._font("NotoSansKR-Bold.otf")
	scene = WS.new()
	add_child(scene)
	host = load("res://game/SimHost.cs").new()
	add_child(host)
	_build_ui()
	host.Boot(1, 40 if autotest else 150, 10)


func _process(dt: float) -> void:
	if not host.Ready:
		boot_label.text = "세계를 만드는 중… %d년째" % host.BootYear
		if host.BootError != "": boot_label.text = host.BootError
		return
	if boot_panel.visible:
		boot_panel.visible = false
		_recenter(host.BiggestVillage())
		if autotest: _run_autotest()
		else: _set_hint("집을 눌러 '이 집을 맡는다'를 고르면 시작한다.")
	if speed > 0 and host.Player >= 0:
		acc += dt * SPEEDS[speed]
		var days := int(acc)
		if days > 0:
			acc -= days
			if host.Advance(days):
				_set_speed(0)
				_set_hint("집에 큰일이 생겨 멈췄다. 연대기를 보고 다시 시작한다.")
			since_draw += days
			if since_draw >= 6:
				since_draw = 0
				_redraw()
	date_label.text = host.DateText


# ------------------------------------------------------------------ 그리기

func _recenter(center: Vector2i) -> void:
	origin = center - REGION / 2
	_redraw()
	scene.aim(scene.pos(REGION.x / 2, REGION.y / 2), 160.0)


## 영역을 다시 받아 그리고, 열려 있는 카드와 가족 목록을 새 값으로 바꾼다.
func _redraw() -> void:
	var s: String = host.Region(origin.x, origin.y, REGION.x, REGION.y)
	if s == "": return
	data = JSON.parse_string(s)
	scene.show_data(data)
	_refresh_family()
	_refresh_card()
	for line in host.DrainChronicle(): _log(line)


func _refresh_card() -> void:
	if shown.is_empty() or not card.visible: return
	if shown["kind"] == "person":
		var pd = scene.find_person(shown["uid"])
		if pd == null: card.visible = false; scene.ring.visible = false; return
		_show_person(pd)
	else:
		var hi: int = scene.house_index(shown["id"])
		if hi < 0: card.visible = false; scene.ring.visible = false; return
		_show_house(hi)


# ------------------------------------------------------------------ 카드

func _show_person(pd: Dictionary) -> void:
	shown = {"kind": "person", "uid": int(pd["uid"])}
	card.show_person(pd, data["hinfo"][int(pd["h"])])
	scene.mark_person(int(pd["uid"]))
	var me: int = host.Player
	if me < 0: return
	var target := int(pd["i"])
	if actor < 0 or actor == target: return
	var items := []
	for it in host.Interactions(actor, target):
		if float(it["chance"]) < 0.01: continue       # 받아들일 리 없는 일은 보이지 않는다
		var sub := "받아들일 듯 %d%%" % int(round(float(it["chance"]) * 100.0))
		if it.has("price"): sub += " · 값 %d" % int(float(it["price"]))
		if it["secret"]: sub += " · 몰래"
		var id := int(it["id"])
		items.append({"text": it["name"], "sub": sub, "warn": it["secret"], "cb": func(): _do(id, target)})
	card.add_actions("%s(이)가 할 수 있는 일" % _actor_name(), items)


func _show_house(hi: int) -> void:
	var hinfo: Dictionary = data["hinfo"][hi]
	shown = {"kind": "house", "id": int(hinfo.get("id", -1))}
	card.show_house(data["houses"][hi], hinfo, scene.members_of(hi))
	scene.mark_house(hi)
	if int(data["houses"][hi][2]) > 0 and int(hinfo.get("id", -1)) != host.Player:
		var id := int(hinfo["id"])
		card.add_actions("플레이", [{"text": "이 집을 맡는다", "sub": "식구들을 내가 이끈다", "cb": func(): _take_house(id)}])


func _actor_name() -> String:
	for m in _members(): if int(m["i"]) == actor: return m["name"]
	return ""


func _do(id: int, target: int) -> void:
	var text: String = host.Interact(id, actor, target)
	_set_hint(text)
	_redraw()


func _take_house(id: int) -> void:
	host.SetPlayer(id)
	actor = -1
	for m in _members():
		if m["head"]: actor = int(m["i"])
	_set_speed(1)
	_set_hint("")
	_redraw()


# ------------------------------------------------------------------ 가족

func _members() -> Array:
	var s: Dictionary = host.PlayerSummary()
	return s.get("members", [])


func _refresh_family() -> void:
	for c in family_box.get_children(): c.queue_free()
	var s: Dictionary = host.PlayerSummary()
	if s.is_empty():
		family_label.text = "맡은 집 없음"
		return
	var members: Array = s["members"]
	var alive := false
	for m in members: if int(m["i"]) == actor: alive = true
	if not alive:
		actor = -1
		for m in members: if m["head"]: actor = int(m["i"])
	family_label.text = "%s 가문 · 같은 성 %d명 · 땅 %d필지 · 식량 %s" % [s["family"], int(s["kin"]), int(s["plots"]), _food_word(float(s["surplus"]))]
	members.sort_custom(func(a, b):
		if a["head"] != b["head"]: return a["head"]
		return int(a["age"]) > int(b["age"]))
	for m in members:
		var b := Button.new()
		var tags := []
		if m["head"]: tags.append("가장")
		if m["pregnant"]: tags.append("임신")
		if m["hungry"]: tags.append("굶주림")
		b.text = "%s%s  %d세%s" % ["▶ " if int(m["i"]) == actor else "", m["name"], int(m["age"]), ("  · " + ", ".join(tags)) if tags.size() > 0 else ""]
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		_style_button(b, int(m["i"]) == actor)
		var i := int(m["i"])
		b.pressed.connect(func(): _pick_member(i))
		family_box.add_child(b)


func _pick_member(i: int) -> void:
	if int(_age_of(i)) >= 12: actor = i
	_refresh_family()
	for pd in data.get("people", []):
		if int(pd["i"]) == i:
			_show_person(pd)
			var wp = scene.person_world_pos(int(pd["uid"]))
			if wp != null: scene.aim(wp, scene.cam.size)
			return


func _age_of(i: int) -> int:
	for m in _members(): if int(m["i"]) == i: return int(m["age"])
	return 0


func _food_word(s: float) -> String:
	if s < 0.5: return "바닥남"
	if s < 1.0: return "빠듯함"
	if s < 2.0: return "넉넉함"
	return "남아돎"


# ------------------------------------------------------------------ 입력

var dragging := false

func _unhandled_input(ev: InputEvent) -> void:
	if ev is InputEventMouseButton:
		if ev.button_index == MOUSE_BUTTON_LEFT and ev.pressed: _click(ev.position)
		elif ev.button_index in [MOUSE_BUTTON_RIGHT, MOUSE_BUTTON_MIDDLE]: dragging = ev.pressed
		elif ev.button_index == MOUSE_BUTTON_WHEEL_UP and ev.pressed: scene.zoom(0.88)
		elif ev.button_index == MOUSE_BUTTON_WHEEL_DOWN and ev.pressed: scene.zoom(1.14)
	elif ev is InputEventMouseMotion and dragging:
		scene.pan(ev.relative)
	elif ev is InputEventKey and ev.pressed and not ev.echo:
		match ev.keycode:
			KEY_SPACE: _set_speed(0 if speed > 0 else 1)
			KEY_1: _set_speed(1)
			KEY_2: _set_speed(2)
			KEY_3: _set_speed(3)


func _physics_process(dt: float) -> void:
	var v := Vector2.ZERO
	if Input.is_physical_key_pressed(KEY_A): v.x -= 1
	if Input.is_physical_key_pressed(KEY_D): v.x += 1
	if Input.is_physical_key_pressed(KEY_W): v.y -= 1
	if Input.is_physical_key_pressed(KEY_S): v.y += 1
	if v != Vector2.ZERO: scene.pan(-v * 600.0 * dt)


func _click(sp: Vector2) -> void:
	if data.is_empty(): return
	var r: Dictionary = scene.pick(sp)
	match r.get("kind", ""):
		"person": _show_person(r["pd"])
		"house": _show_house(r["hi"])
		_:
			card.visible = false
			shown = {}


func _set_speed(s: int) -> void:
	speed = s
	for k in speed_buttons.size(): _style_button(speed_buttons[k], k == s)


# ------------------------------------------------------------------ 화면 틀

func _panel(col := PAPER) -> PanelContainer:
	var p := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = col
	sb.border_color = Color(0.45, 0.33, 0.2)
	sb.set_border_width_all(2)
	sb.set_corner_radius_all(10)
	sb.content_margin_left = 14; sb.content_margin_right = 14
	sb.content_margin_top = 8; sb.content_margin_bottom = 10
	sb.shadow_color = Color(0, 0, 0, 0.22); sb.shadow_size = 6
	p.add_theme_stylebox_override("panel", sb)
	return p


func _label(text: String, size := 15, col := INK, bold := false) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_override("font", font_b if bold else font_r)
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", col)
	return l


func _style_button(b: Button, on: bool) -> void:
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0.55, 0.38, 0.22) if on else Color(0.92, 0.85, 0.72)
	sb.set_corner_radius_all(7)
	sb.content_margin_left = 10; sb.content_margin_right = 10
	sb.content_margin_top = 4; sb.content_margin_bottom = 5
	for st in ["normal", "hover", "pressed", "focus"]: b.add_theme_stylebox_override(st, sb)
	var fc := Color(1, 0.97, 0.9) if on else INK
	for st in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color"]: b.add_theme_color_override(st, fc)
	b.add_theme_font_override("font", font_r)
	b.add_theme_font_size_override("font_size", 14)


func _build_ui() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	var rootc := Control.new()
	rootc.set_anchors_preset(Control.PRESET_FULL_RECT)
	rootc.mouse_filter = Control.MOUSE_FILTER_IGNORE
	rootc.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR    # 프로젝트 기본(가장 가까운 점)으로 줄이면 글자 획이 빠진다
	layer.add_child(rootc)

	# 위: 날짜, 속도, 가문
	var top := _panel()
	top.position = Vector2(16, 12)
	var tr := HBoxContainer.new()
	tr.add_theme_constant_override("separation", 14)
	top.add_child(tr)
	date_label = _label("", 17, INK, true)
	date_label.custom_minimum_size = Vector2(150, 0)
	tr.add_child(date_label)
	for k in 4:
		var b := Button.new()
		b.text = ["멈춤", "▶", "▶▶", "▶▶▶"][k]
		_style_button(b, k == 0)
		var kk := k
		b.pressed.connect(func(): _set_speed(kk))
		speed_buttons.append(b)
		tr.add_child(b)
	family_label = _label("맡은 집 없음", 15, MUTED)
	tr.add_child(family_label)
	rootc.add_child(top)

	# 왼쪽: 내 가족
	var left := _panel()
	left.position = Vector2(16, 76)
	left.custom_minimum_size = Vector2(300, 0)
	var lv := VBoxContainer.new()
	lv.add_theme_constant_override("separation", 5)
	left.add_child(lv)
	lv.add_child(_label("내 가족", 13, MUTED, true))
	family_box = VBoxContainer.new()
	family_box.add_theme_constant_override("separation", 4)
	lv.add_child(family_box)
	rootc.add_child(left)

	# 오른쪽: 카드
	card = Card.new()
	card.on_close = func(): scene.ring.visible = false; shown = {}
	card.anchor_left = 1.0; card.anchor_right = 1.0
	card.offset_left = -398; card.offset_right = -18; card.offset_top = 18
	card.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	rootc.add_child(card)

	# 아래: 연대기
	var bottom := _panel()
	bottom.anchor_top = 1.0; bottom.anchor_bottom = 1.0
	bottom.offset_left = 16; bottom.offset_top = -190; bottom.offset_bottom = -14; bottom.offset_right = 760
	var bv := VBoxContainer.new()
	bv.add_theme_constant_override("separation", 2)
	bottom.add_child(bv)
	hint_label = _label("", 14, Color(0.55, 0.25, 0.15), true)
	bv.add_child(hint_label)
	bv.add_child(_label("연대기", 13, MUTED, true))
	log_box = VBoxContainer.new()
	bv.add_child(log_box)
	rootc.add_child(bottom)

	# 처음 세계를 만드는 동안
	boot_panel = _panel()
	boot_panel.set_anchors_preset(Control.PRESET_CENTER)
	boot_label = _label("세계를 만드는 중…", 20, INK, true)
	boot_panel.add_child(boot_label)
	rootc.add_child(boot_panel)


func _log(line: String) -> void:
	log_box.add_child(_label(line, 14))
	while log_box.get_child_count() > 7:
		var c := log_box.get_child(0)
		log_box.remove_child(c)
		c.queue_free()


func _set_hint(t: String) -> void:
	hint_label.text = t
	hint_label.visible = t != ""


# ------------------------------------------------------------------ 자동 점검

func _run_autotest() -> void:
	# 영역 안에서 식구가 가장 많은 집을 맡고, 반년 돌린 뒤 화면을 찍는다
	var best := -1
	var most := 0
	for k in data["hinfo"].size():
		var n: int = scene.members_of(k).size()
		if n > most: most = n; best = k
	_take_house(int(data["hinfo"][best]["id"]))
	for i in 6:
		var t0 := Time.get_ticks_msec()
		host.Advance(6)
		var t1 := Time.get_ticks_msec()
		_redraw()
		print("autotest: 한 계절 %d ms, 다시 그리기 %d ms" % [t1 - t0, Time.get_ticks_msec() - t1])
	_set_speed(0)
	var hp: Vector3 = scene.house_pos[scene.house_index(host.Player)]
	scene.aim(hp, 70.0)
	await _snap("live_family.png")
	# 내 가족이 아닌 어른 하나를 골라 카드와 할 수 있는 일을 띄운다
	var mine := {}
	for m in _members(): mine[int(m["i"])] = true
	var target = null
	var bd := 1e9
	for e in scene.pickables:
		var pd: Dictionary = e["pd"]
		if mine.has(int(pd["i"])) or int(pd["age"]) < 18: continue
		var d: float = e["node"].global_position.distance_to(hp)
		if d < bd: bd = d; target = pd
	if target != null:
		_show_person(target)
		var wp = scene.person_world_pos(int(target["uid"]))
		scene.aim(wp, 60.0)
		await _snap("live_person.png")
		var acts: Array = host.Interactions(actor, int(target["i"]))
		if acts.size() > 0:
			_do(int(acts[0]["id"]), int(target["i"]))
			print("autotest: ", hint_label.text)
	get_tree().quit()


func _snap(file: String) -> void:
	for i in 8: await get_tree().process_frame
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(ProjectSettings.globalize_path("res://docs/images/" + file))
