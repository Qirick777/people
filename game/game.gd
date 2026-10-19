extends Node
## 실시간 게임 화면. SimHost(C#)가 세계를 돌리고, 이 스크립트는 그리고 묻고 시킨다.
## 위: 날짜·속도·가문. 왼쪽: 내 가족. 오른쪽: 인물·집 카드와 할 수 있는 일. 아래: 연대기.
## 조작: 왼쪽 클릭 고르기, 오른쪽 끌기·WASD 화면 옮기기, 휠 확대, 스페이스 멈춤, 1·2·3 속도.
## 자동 점검: godot --path . -- --autotest  (짧게 세계를 만들고 집을 골라 몇 달 돌린 뒤 스크린샷을 찍는다)

const WS := preload("res://game/world_scene.gd")
const Card := preload("res://prototypes/world_view/info_card.gd")
const UI := preload("res://game/ui_kit.gd")
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
var pending := {}                        # 고르는 중인 상호작용 {"id", "target", "third"}
var marry_button: Button
var autotest := false

var date_label: Label
var speed_buttons := []
var family_box: VBoxContainer
var log_box: VBoxContainer
var hint_label: Label
var boot_panel: PanelContainer
var boot_label: Label


func _ready() -> void:
	autotest = "--autotest" in OS.get_cmdline_user_args()
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
	if not pending.is_empty() and int(pending["target"]) == target:
		_show_choices(target)
		return
	pending = {}
	var items := []
	for it in host.Interactions(actor, target):
		var id := int(it["id"])
		if it["needsThird"]:
			items.append({"text": it["name"], "note": it["thirdLabel"], "risky": it["harsh"], "cb": func(): _begin(id, target)})
			continue
		if it["hasAmounts"]:
			items.append({"text": it["name"], "note": "얼마를 줄지 고른다", "risky": it["secret"], "cb": func(): _begin_amount(id, target)})
			continue
		if float(it["chance"]) < 0.01: continue       # 받아들일 리 없는 일은 보이지 않는다
		var notes := []
		if it.has("price"): notes.append("값 %d" % int(float(it["price"])))
		if it.has("risk"): notes.append("한 해 들킬 위험 %d%%" % int(round(float(it["risk"]) * 100.0)))
		elif it["secret"]: notes.append("몰래 하는 일")
		items.append({"text": it["name"], "note": " · ".join(notes), "chance": float(it["chance"]), "risky": it["secret"] or it["harsh"], "cb": func(): _do(id, target)})
	card.add_actions("할 수 있는 일", items, "%s 하는 일 · 오른쪽 숫자는 받아들일 확률" % World_subj(_actor_name()))


## 세 번째 사람이나 값을 고르는 중: 후보마다 받아들일 확률을 보여 준다.
func _show_choices(target: int) -> void:
	var id := int(pending["id"])
	var items := []
	if not pending.has("third"):
		for c in host.ThirdCandidates(id, actor, target):
			var t := int(c["i"])
			var ch := -1.0 if host.HasAmounts(id) else float(c["chance"])
			items.append({"text": c["name"], "note": c["sub"], "chance": ch, "cb": func(): _pick_third(t)})
		items.append({"text": "그만둔다", "ghost": true, "cb": _cancel})
		card.add_actions(_label_for(id), items)
		card.scroll_end()
		return
	var third := int(pending["third"])
	for o in host.AmountOptions(id, actor, target, third):
		var amt := float(o["amount"])
		items.append({"text": o["name"], "note": "식량 %d" % int(amt) if amt > 0 else "", "chance": float(o["chance"]), "cb": func(): _finish(amt)})
	items.append({"text": "그만둔다", "ghost": true, "cb": _cancel})
	card.add_actions("얼마를 줄까" if third < 0 else "얼마를 얹을까", items)
	card.scroll_end()


func _label_for(id: int) -> String:
	for it in host.Interactions(actor, int(pending["target"])):
		if int(it["id"]) == id: return "%s: %s" % [it["name"], it["thirdLabel"]]
	return "누구를"


func _begin_amount(id: int, target: int) -> void:
	pending = {"id": id, "target": target, "third": -1}
	_refresh_card()


func _begin(id: int, target: int) -> void:
	pending = {"id": id, "target": target}
	_refresh_card()


func _pick_third(t: int) -> void:
	pending["third"] = t
	if host.HasAmounts(int(pending["id"])): _refresh_card()
	else: _finish(0.0)


func _finish(amount: float) -> void:
	var p := pending
	pending = {}
	_do(int(p["id"]), int(p["target"]), int(p.get("third", -1)), amount)


func _cancel() -> void:
	pending = {}
	_refresh_card()


func _show_house(hi: int) -> void:
	var hinfo: Dictionary = data["hinfo"][hi]
	shown = {"kind": "house", "id": int(hinfo.get("id", -1))}
	card.show_house(data["houses"][hi], hinfo, scene.members_of(hi))
	scene.mark_house(hi)
	if int(data["houses"][hi][2]) > 0 and int(hinfo.get("id", -1)) != host.Player:
		var id := int(hinfo["id"])
		card.add_actions("플레이", [{"text": "이 집을 맡는다", "note": "식구들을 내가 이끈다", "cb": func(): _take_house(id)}])


func World_subj(n: String) -> String:
	if n == "": return ""
	var c := n.unicode_at(n.length() - 1)
	var batchim := c >= 0xAC00 and c <= 0xD7A3 and (c - 0xAC00) % 28 != 0
	return n + ("이" if batchim else "가")


func _actor_name() -> String:
	for m in _members(): if int(m["i"]) == actor: return m["name"]
	return ""


func _do(id: int, target: int, third := -1, amount := 0.0) -> void:
	var text: String = host.Interact(id, actor, target, third, amount)
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
		_set_stats("", "", "", "")
		return
	var members: Array = s["members"]
	var alive := false
	for m in members: if int(m["i"]) == actor: alive = true
	if not alive:
		actor = -1
		for m in members: if m["head"]: actor = int(m["i"])
	var fw := _food_word(float(s["surplus"]))
	_set_stats(s["family"], "%d명" % int(s["kin"]), "%d필지" % int(s["plots"]), fw)
	members.sort_custom(func(a, b):
		if a["head"] != b["head"]: return a["head"]
		return int(a["age"]) > int(b["age"]))
	for m in members:
		var i := int(m["i"])
		var on := i == actor
		var b := UI.row_button(on)
		b.custom_minimum_size = Vector2(268, 44)
		var row := HBoxContainer.new()
		row.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.set_anchors_preset(Control.PRESET_FULL_RECT)
		row.offset_left = 8; row.offset_right = -8; row.offset_top = 6; row.offset_bottom = -6
		row.add_theme_constant_override("separation", 8)
		row.add_child(UI.avatar(m["name"], m["male"], 30))
		var txt := VBoxContainer.new()
		txt.mouse_filter = Control.MOUSE_FILTER_IGNORE
		txt.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		txt.add_theme_constant_override("separation", -4)
		txt.add_child(UI.label(m["name"], 14, UI.INK, true))
		txt.add_child(UI.label("%d세%s" % [int(m["age"]), " · 행동하는 사람" if on else ""], 12, UI.ACT if on else UI.MUTED))
		row.add_child(txt)
		if m["head"]: row.add_child(_center(UI.chip("가장", Color(0.95, 0.85, 0.55), Color(0.42, 0.3, 0.08), 11)))
		if m["heir"]: row.add_child(_center(UI.chip("후계자", Color(0.85, 0.9, 0.7), Color(0.3, 0.4, 0.12), 11)))
		if m["concubine"]: row.add_child(_center(UI.chip("첩", Color(0.9, 0.85, 0.95), Color(0.4, 0.25, 0.5), 11)))
		if m["right"] != "": row.add_child(_center(UI.chip(m["right"], Color(0.9, 0.87, 0.82), Color(0.4, 0.33, 0.25), 11)))
		if m["pregnant"]: row.add_child(_center(UI.chip("임신", Color(0.97, 0.82, 0.86), Color(0.55, 0.2, 0.3), 11)))
		if m["hungry"]: row.add_child(_center(UI.chip("굶주림", Color(0.98, 0.8, 0.62), Color(0.55, 0.25, 0.1), 11)))
		b.add_child(row)
		b.pressed.connect(func(): _pick_member(i))
		family_box.add_child(b)


func _center(c: Control) -> Control:
	c.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	return c


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
		"person":
			pending = {}
			_show_person(r["pd"])
		"house": _show_house(r["hi"])
		_:
			card.visible = false
			shown = {}


## 식구들이 스스로 짝을 찾을지(알아서), 내가 청혼·혼담으로 맺을지(직접).
func _toggle_marry() -> void:
	host.AutoMarry = not host.AutoMarry
	_style_marry()


func _style_marry() -> void:
	var auto: bool = host.AutoMarry if host != null and host.Ready else true
	marry_button.text = "혼인: 알아서" if auto else "혼인: 내가 정함"
	marry_button.tooltip_text = "식구들이 스스로 짝을 찾는다. 누르면 청혼·혼담을 내가 정한다." if auto else "식구들은 스스로 청혼하지 않는다. 누르면 알아서 하게 둔다."
	UI.segment(marry_button, not auto, true, true)


func _set_speed(s: int) -> void:
	speed = s
	for k in speed_buttons.size(): UI.segment(speed_buttons[k], k == s, k == 0, k == speed_buttons.size() - 1)


# ------------------------------------------------------------------ 화면 틀

var stat_family: VBoxContainer
var stat_kin: VBoxContainer
var stat_land: VBoxContainer
var stat_food: VBoxContainer
var notice: PanelContainer


func _set_stats(fam: String, kin: String, land: String, food: String) -> void:
	(stat_family.get_node("Value") as Label).text = fam if fam != "" else "맡은 집 없음"
	(stat_kin.get_node("Value") as Label).text = kin if kin != "" else "-"
	(stat_land.get_node("Value") as Label).text = land if land != "" else "-"
	var fl := stat_food.get_node("Value") as Label
	fl.text = food if food != "" else "-"
	fl.add_theme_color_override("font_color", UI.RISK if food in ["바닥남", "빠듯함"] else UI.INK)


func _vsep() -> VSeparator:
	var v := VSeparator.new()
	var st := StyleBoxLine.new()
	st.color = UI.LINE
	st.vertical = true
	st.thickness = 1
	v.add_theme_stylebox_override("separator", st)
	return v


func _build_ui() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	var rootc := Control.new()
	rootc.set_anchors_preset(Control.PRESET_FULL_RECT)
	rootc.mouse_filter = Control.MOUSE_FILTER_IGNORE
	rootc.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR    # 프로젝트 기본(가장 가까운 점)으로 줄이면 글자 획이 빠진다
	layer.add_child(rootc)

	# 위: 날짜, 속도(붙은 단추 묶음), 혼인 방침, 가문 정보 칸
	var top := UI.panel()
	top.position = Vector2(16, 12)
	var tr := HBoxContainer.new()
	tr.add_theme_constant_override("separation", 14)
	top.add_child(tr)
	var dv := VBoxContainer.new()
	dv.add_theme_constant_override("separation", -3)
	dv.add_child(UI.label("날짜", 12, UI.MUTED))
	date_label = UI.label("", 17, UI.INK, true)
	date_label.custom_minimum_size = Vector2(130, 0)
	dv.add_child(date_label)
	tr.add_child(dv)
	var seg := HBoxContainer.new()
	seg.add_theme_constant_override("separation", 0)
	for k in 4:
		var b := Button.new()
		b.text = ["멈춤", "▶", "▶▶", "▶▶▶"][k]
		b.tooltip_text = ["멈춤 (스페이스)", "보통 (1)", "빠르게 (2)", "아주 빠르게 (3)"][k]
		var kk := k
		b.pressed.connect(func(): _set_speed(kk))
		speed_buttons.append(b)
		seg.add_child(b)
	tr.add_child(_center(seg))
	marry_button = Button.new()
	marry_button.pressed.connect(_toggle_marry)
	tr.add_child(_center(marry_button))
	tr.add_child(_vsep())
	stat_family = UI.stat("가문", "맡은 집 없음")
	stat_kin = UI.stat("같은 성", "-")
	stat_land = UI.stat("땅", "-")
	stat_food = UI.stat("식량", "-")
	for st in [stat_family, stat_kin, stat_land, stat_food]: tr.add_child(st)
	rootc.add_child(top)
	_set_speed(0)
	_style_marry()

	# 왼쪽: 내 가족
	var left := UI.panel()
	left.position = Vector2(16, 84)
	var lv := VBoxContainer.new()
	lv.add_theme_constant_override("separation", 6)
	left.add_child(lv)
	lv.add_child(UI.label("내 가족", 13, UI.MUTED, true))
	lv.add_child(UI.label("눌러서 행동할 사람을 고른다", 12, UI.MUTED))
	family_box = VBoxContainer.new()
	family_box.add_theme_constant_override("separation", 5)
	lv.add_child(family_box)
	rootc.add_child(left)

	# 오른쪽: 카드
	card = Card.new()
	card.on_close = func(): scene.ring.visible = false; shown = {}
	card.anchor_left = 1.0; card.anchor_right = 1.0
	card.offset_left = -398; card.offset_right = -18; card.offset_top = 18
	card.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	card.max_height = 760.0
	rootc.add_child(card)

	# 아래: 알림(방금 일어난 일)과 연대기
	var bottom := VBoxContainer.new()
	bottom.anchor_top = 1.0; bottom.anchor_bottom = 1.0
	bottom.offset_left = 16; bottom.offset_right = 760; bottom.offset_bottom = -14
	bottom.grow_vertical = Control.GROW_DIRECTION_BEGIN
	bottom.add_theme_constant_override("separation", 6)
	notice = PanelContainer.new()
	var nsb := UI.box(Color(0.99, 0.9, 0.62), 8, Color(0.75, 0.55, 0.2), 1)
	nsb.content_margin_left = 12; nsb.content_margin_right = 12; nsb.content_margin_top = 5; nsb.content_margin_bottom = 6
	notice.add_theme_stylebox_override("panel", nsb)
	hint_label = UI.label("", 14, Color(0.42, 0.25, 0.08), true)
	hint_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	notice.add_child(hint_label)
	notice.visible = false
	bottom.add_child(notice)
	var chron := UI.panel()
	chron.custom_minimum_size = Vector2(744, 0)
	var bv := VBoxContainer.new()
	bv.add_theme_constant_override("separation", 3)
	chron.add_child(bv)
	bv.add_child(UI.label("연대기", 13, UI.MUTED, true))
	log_box = VBoxContainer.new()
	log_box.add_theme_constant_override("separation", 2)
	bv.add_child(log_box)
	bottom.add_child(chron)
	rootc.add_child(bottom)

	# 처음 세계를 만드는 동안
	boot_panel = UI.panel()
	boot_panel.set_anchors_preset(Control.PRESET_CENTER)
	boot_label = UI.label("세계를 만드는 중…", 20, UI.INK, true)
	boot_panel.add_child(boot_label)
	rootc.add_child(boot_panel)


## 연대기 한 줄: 날짜는 옅게, 일은 진하게. 가장 새 줄이 맨 아래.
func _log(line: String) -> void:
	var parts := line.split(" · ", true, 1)
	var r := HBoxContainer.new()
	r.add_theme_constant_override("separation", 10)
	var d := UI.label(parts[0] if parts.size() > 1 else "", 12, UI.MUTED)
	d.custom_minimum_size = Vector2(86, 0)
	r.add_child(d)
	r.add_child(UI.label(parts[1] if parts.size() > 1 else line, 14))
	log_box.add_child(r)
	while log_box.get_child_count() > 6:
		var c := log_box.get_child(0)
		log_box.remove_child(c)
		c.queue_free()


func _set_hint(t: String) -> void:
	hint_label.text = t
	notice.visible = t != ""


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
		var ti := int(target["i"])
		for it in host.Interactions(actor, ti): print("autotest: 할 수 있는 일 ", it["name"])
		# 떠보기를 성공할 때까지 몇 번 해 보고 카드를 찍는다
		for k in 5:
			_do(_id_of("떠보기", ti), ti)
			print("autotest: ", hint_label.text)
			var pd2 = scene.find_person(int(target["uid"]))
			if pd2 != null and pd2.has("talents"): break
		_show_person(scene.find_person(int(target["uid"])))
		await _snap("live_probe.png")
		# 설득: 세 번째 사람 고르기
		var pid := _id_of("설득", ti)
		if pid >= 0:
			_begin(pid, ti)
			await _snap("live_choose.png")
			_cancel()
	# 내 식구 카드: 집안 일(후계자, 분가, 의절 등)
	for m in _members():
		if int(m["i"]) != actor and int(m["age"]) >= 12:
			_pick_member(int(m["i"]))
			actor = -1
			for mm in _members(): if mm["head"]: actor = int(mm["i"])
			_refresh_family()
			_show_person(scene.find_person(int(m["uid"])) if m.has("uid") else _pd_of(int(m["i"])))
			for it in host.Interactions(actor, int(m["i"])): print("autotest: 식구에게 할 수 있는 일 ", it["name"])
			await _snap("live_household.png")
			break
	# 혼담: 내 집의 혼인할 사람과 이웃의 혼인할 사람을 찾아 지참금 고르기를 띄운다
	for e in scene.pickables:
		var pd: Dictionary = e["pd"]
		if mine.has(int(pd["i"])) or int(pd["age"]) < 16: continue
		var aid := _id_of("혼담", int(pd["i"]))
		if aid < 0: continue
		var cands: Array = host.ThirdCandidates(aid, actor, int(pd["i"]))
		if cands.is_empty(): continue
		_show_person(pd)
		_begin(aid, int(pd["i"]))
		_pick_third(int(cands[0]["i"]))
		scene.aim(e["node"].global_position, 60.0)
		await _snap("live_dowry.png")
		print("autotest: 혼담 상대 ", pd["name"], " / 내 식구 ", cands[0]["name"])
		break
	get_tree().quit()


func _pd_of(i: int) -> Dictionary:
	for pd in data.get("people", []):
		if int(pd["i"]) == i: return pd
	return {}


func _id_of(name: String, target: int) -> int:
	for it in host.Interactions(actor, target):
		if it["name"] == name: return int(it["id"])
	return -1


func _snap(file: String) -> void:
	for i in 8: await get_tree().process_frame
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(ProjectSettings.globalize_path("res://docs/images/" + file))
