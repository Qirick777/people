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
var pending := {}                        # 고르는 중인 상호작용 {"id", "target", "third", "actor"}
var shown_crimes_for := -1               # 범죄 묶음을 펼친 상대(사람 번호)
var marry_button: Button
var kin_box: VBoxContainer
var kin_title: Label
var tree_panel: PanelContainer
var tree_box: VBoxContainer
var range_button: Button
var org_panel: PanelContainer
var org_box: VBoxContainer
var org_title: Label
var judge_button: Button
var rise_button: Button
var rise_panel: PanelContainer
var rise_box: VBoxContainer
var judge_panel: PanelContainer
var judge_box: VBoxContainer
var last_player := -1
var autotest := false

var date_label: Label
var speed_buttons := []
var family_box: VBoxContainer
var log_box: VBoxContainer
var hint_label: Label
var boot_panel: PanelContainer
var boot_label: Label
var start_panel: PanelContainer
var started := false
var review := false                      # 검토용 세계
var show_button: Button
var show_panel: PanelContainer
var show_box: VBoxContainer
var hint_age := 0.0
var hint_sticky := false


func _ready() -> void:
	autotest = "--autotest" in OS.get_cmdline_user_args()
	scene = WS.new()
	add_child(scene)
	host = load("res://game/SimHost.cs").new()
	add_child(host)
	_build_ui()
	if autotest:
		_start(0)
	else:
		start_panel.visible = true
		if "--startshot" in OS.get_cmdline_user_args():
			await _snap("live_start.png")
			get_tree().quit()


## 세계를 고른다: 기본은 자리 잡은 세계(지배자와 조직이 다 있는 220년), 또는 개척(150년)
## mode: 0 개척, 1 오래 돌린 세계(검토용), 2 장원 세계 작게, 3 장원 세계 크게
func _start(mode: int) -> void:
	var rev := mode != 0
	review = rev
	started = true
	_set_hint("")
	start_panel.visible = false
	boot_panel.visible = true
	show_button.visible = true
	match mode:
		1: host.Boot(2, 220, 10)
		2: host.BootGenerated(1 + randi() % 1000, 3, 8, 15)
		3: host.BootGenerated(1 + randi() % 1000, 5, 10, 15)
		_: host.Boot(1, 40 if autotest else 150, 10)


func _process(dt: float) -> void:
	if not started: return
	if notice.visible and not hint_sticky:
		hint_age += dt
		if hint_age > 30.0: _set_hint("")
	if not host.Ready:
		boot_label.text = "세계를 만드는 중… %d년째" % host.BootYear
		if host.BootError != "": boot_label.text = host.BootError
		return
	if boot_panel.visible:
		boot_panel.visible = false
		_recenter(host.BiggestVillage())
		if autotest: _run_autotest()
		elif review:
			_set_hint("처지를 골라 맡는다(소작농, 몰락한 지주, 서자, 용병, 지배자 …). 위 막대의 '둘러보기'로 다시 연다.", true)
			_open_showcase()
		else: _set_hint("집을 눌러 '이 집을 맡는다'를 고르면 시작한다.", true)
	if speed > 0 and host.Player >= 0:
		acc += dt * SPEEDS[speed]
		var days := int(acc)
		if days > 0:
			acc -= days
			if host.Advance(days):
				_set_speed(0)
				if int(host.DemandCount) > 0:
					_set_hint("지배자가 우리 집에 요구를 해 왔다. 다툼 창에서 답한다.")
					_open_rise()
				elif int(host.PendingCount) > 0:
					_set_hint("범인을 잡았다. 재판 창에서 판결한다.")
					_open_judge()
				else:
					_set_hint("집에 큰일이 생겨 멈췄다. 연대기를 보고 다시 시작한다.")
			_refresh_judge_button()
			since_draw += days
			if int(host.Player) != last_player and last_player >= 0:
				if int(host.Player) < 0:
					# 혈통이 끊겼다: 맡을 혈족 집이 없다
					last_player = -1
					actor = -1
					pending = {}
					_set_speed(0)
					_set_hint("대가 끊겨 맡은 집이 사라졌다. 이어 맡을 혈족 집이 없다. 다른 집을 눌러 새로 맡을 수 있다.", true)
				else:
					_on_player_changed()
				since_draw = 6
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
	_refresh_judge_button()
	for line in host.DrainChronicle(): _log(line)


func _refresh_card() -> void:
	if shown.is_empty() or not card.visible: return
	if shown["kind"] == "person":
		var pd = scene.find_person(shown["uid"])
		if pd == null: card.visible = false; scene.ring.visible = false; return
		_show_person(pd)
	else:
		var hi: int = scene.house_index(shown["id"]) if int(shown["id"]) >= 0 else _house_at(int(shown["x"]), int(shown["y"]))
		if hi < 0: card.visible = false; scene.ring.visible = false; return
		_show_house(hi)


## 빈집은 번호가 없어 자리로 찾는다.
func _house_at(x: int, y: int) -> int:
	var hs: Array = data.get("houses", [])
	for k in hs.size():
		if origin.x + int(hs[k][0]) == x and origin.y + int(hs[k][1]) == y: return k
	return -1


# ------------------------------------------------------------------ 카드

func _show_person(pd: Dictionary) -> void:
	shown = {"kind": "person", "uid": int(pd["uid"])}
	card.show_person(pd, data["hinfo"][int(pd["h"])])
	scene.mark_person(int(pd["uid"]))
	var me: int = host.Player
	if me < 0: return
	var target := int(pd["i"])
	if actor >= 0 and actor == target:
		var own := []
		for it in host.SelfActions(actor):
			var key: String = it["key"]
			own.append({"text": it["name"], "note": it["note"], "risky": it["risky"], "cb": func(): _do_self(key)})
		card.add_actions("조직 세우기·나가기", own)
		return
	if actor < 0: return
	if not pending.is_empty() and int(pending["target"]) == target and int(pending.get("actor", -1)) == actor:
		_show_choices(target)
		return
	pending = {}
	if int(shown_crimes_for) != target: shown_crimes_for = -1
	var items := []
	var crimes := []
	for it in host.Interactions(actor, target):
		var id := int(it["id"])
		if it.has("crime"):
			var cn := []
			if it.has("take"): cn.append("식량 %d" % int(float(it["take"])))
			cn.append("잡힐 확률 %d%%" % int(round(float(it["caught"]) * 100.0)))
			cn.append("벌: " + it["punish"])
			if float(it["chance"]) < 0.02: continue      # 해낼 리 없는 범죄는 보이지 않는다
			crimes.append({"text": it["name"], "note": " · ".join(cn), "chance": float(it["chance"]), "risky": true, "cb": func(): _do(id, target)})
			continue
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
	if not crimes.is_empty():
		# 범죄는 접어 둔다(카드가 길어 다른 일이 밀리지 않게)
		if shown_crimes_for == target:
			card.add_actions("범죄", crimes, "오른쪽 숫자는 해낼 확률. 잡힐 확률은 들켜서 신고되고 붙잡힐 확률이다")
		else:
			card.add_actions("범죄", [{"text": "범죄 %d가지 펼치기" % crimes.size(), "ghost": true, "cb": func():
				shown_crimes_for = target
				_refresh_card()}])


## 세 번째 사람이나 값을 고르는 중: 후보마다 받아들일 확률을 보여 준다.
func _show_choices(target: int) -> void:
	var id := int(pending["id"])
	var items := []
	if not pending.has("third"):
		for c in host.ThirdCandidates(id, actor, target):
			var t := int(c["i"])
			var ch := -1.0 if host.HasAmounts(id) else float(c["chance"])
			if ch >= 0.0 and ch < 0.01: continue
			items.append({"text": c["name"], "note": c["sub"], "chance": ch, "cb": func(): _pick_third(t)})
		items.append({"text": "그만둔다", "ghost": true, "cb": _cancel})
		card.add_actions(_label_for(id), items)
		card.scroll_end()
		return
	var third := int(pending["third"])
	for o in host.AmountOptions(id, actor, target, third):
		var amt := float(o["amount"])
		if float(o["chance"]) < 0.01: continue
		items.append({"text": o["name"], "note": "식량 %d" % int(amt) if amt > 0 else "", "chance": float(o["chance"]), "cb": func(): _finish(amt)})
	items.append({"text": "그만둔다", "ghost": true, "cb": _cancel})
	card.add_actions("얼마를 줄까" if third < 0 else "얼마를 얹을까", items)
	card.scroll_end()


func _label_for(id: int) -> String:
	for it in host.Interactions(actor, int(pending["target"])):
		if int(it["id"]) == id: return "%s: %s" % [it["name"], it["thirdLabel"]]
	return "누구를"


func _begin_amount(id: int, target: int) -> void:
	pending = {"id": id, "target": target, "third": -1, "actor": actor}
	_refresh_card()


func _begin(id: int, target: int) -> void:
	pending = {"id": id, "target": target, "actor": actor}
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
	var hd: Array = data["houses"][hi]
	shown = {"kind": "house", "id": int(hinfo.get("id", -1)), "x": origin.x + int(hd[0]), "y": origin.y + int(hd[1])}
	card.show_house(data["houses"][hi], hinfo, scene.members_of(hi))
	scene.mark_house(hi)
	if int(data["houses"][hi][2]) > 0 and int(hinfo.get("id", -1)) != host.Player:
		var id := int(hinfo["id"])
		if host.Player < 0:
			card.add_actions("플레이", [{"text": "이 집을 맡는다", "note": "식구들을 내가 이끈다", "cb": func(): _take_house(id)}])
		else:
			var chon: int = host.KinChon(id)
			if chon > 0 and chon <= int(host.KinRange):
				var pend: bool = int(host.PendingSwitch) == id
				card.add_actions("혈족 집", [{"text": "다음 계절에 옮겨 맡는다" if pend else "이 집으로 옮겨 맡기", "note": "%d촌 · 계절이 바뀌면 옮긴다" % chon, "cb": func(): _switch_to(id)}])


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


## 맡은 집이 바뀌었다(혈족 집으로 옮기기, 집이 끊겨 따라감): 행동할 사람을 새 가장으로, 지역이 벗어나면 다시 맞춘다.
func _on_player_changed() -> void:
	last_player = host.Player
	actor = -1
	pending = {}
	_set_hint("")
	var pos: Vector2i = host.HousePos(last_player)
	if pos.x >= 0 and (pos.x - origin.x < 8 or pos.y - origin.y < 8 or pos.x - origin.x >= REGION.x - 8 or pos.y - origin.y >= REGION.y - 8):
		_recenter(pos)


func _do_self(key: String) -> void:
	_set_hint(host.DoSelf(actor, key))
	_redraw()


func _take_house(id: int) -> void:
	host.SetPlayer(id)
	last_player = id
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
		(stat_coin.get_node("Value") as Label).text = "-"
		return
	var members: Array = s["members"]
	var alive := false
	for m in members: if int(m["i"]) == actor: alive = true
	if not alive:
		actor = -1
		for m in members: if m["head"]: actor = int(m["i"])
	_refresh_kin()
	var fw := _food_word(float(s["surplus"]))
	_set_stats(s["family"], "%d명" % int(s["kin"]), "%d필지" % int(s["plots"]), fw)
	(stat_coin.get_node("Value") as Label).text = "%d · 값 %.1f" % [int(float(s.get("coin", 0.0))), float(s.get("foodPrice", 1.0))]
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


## 혈족 집 목록: 가까운 촌수부터 여덟 집. 줄을 누르면 그 집을 보고, '맡기'로 옮기기를 예약한다.
func _refresh_kin() -> void:
	for c in kin_box.get_children(): c.queue_free()
	var kin: Array = host.KinHouses()
	kin_title.text = "혈족 집 · %d촌 안 %d집" % [int(host.KinRange), kin.size()]
	var pend: int = host.PendingSwitch
	for k in mini(kin.size(), 8):
		var e: Dictionary = kin[k]
		var id := int(e["id"])
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 6)
		var b := UI.row_button(false)
		b.custom_minimum_size = Vector2(200, 34)
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var inner := HBoxContainer.new()
		inner.mouse_filter = Control.MOUSE_FILTER_IGNORE
		inner.set_anchors_preset(Control.PRESET_FULL_RECT)
		inner.offset_left = 8; inner.offset_right = -6; inner.offset_top = 4; inner.offset_bottom = -4
		inner.add_theme_constant_override("separation", 6)
		var nm := UI.label(e["head"], 13, UI.INK, true)
		nm.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		inner.add_child(nm)
		var parts := ["%d촌" % int(e["chon"])]
		if e["label"] != "": parts.append(e["label"])
		parts.append("%d명" % int(e["members"]))
		inner.add_child(_center(UI.label(" · ".join(parts), 11, UI.MUTED)))
		b.add_child(inner)
		var hx := int(e["x"]); var hy := int(e["y"])
		b.pressed.connect(func(): _look_at_house(id, hx, hy))
		row.add_child(b)
		if id == pend:
			row.add_child(_center(UI.chip("다음 계절", Color(0.85, 0.9, 0.7), Color(0.3, 0.4, 0.12), 11)))
		else:
			var t := UI.ghost("맡기")
			t.pressed.connect(func(): _switch_to(id))
			row.add_child(t)
		kin_box.add_child(row)


func _look_at_house(id: int, x: int, y: int) -> void:
	var lx := x - origin.x
	var ly := y - origin.y
	if lx < 0 or ly < 0 or lx >= REGION.x or ly >= REGION.y:
		_recenter(Vector2i(x, y))
	var hi: int = scene.house_index(id)
	if hi < 0: return
	scene.aim(scene.house_pos[hi], scene.cam.size)
	_show_house(hi)


func _switch_to(id: int) -> void:
	if host.RequestSwitch(id):
		_set_hint("다음 계절이 시작되면 이 집을 맡는다")
	_refresh_family()
	_refresh_card()


func _open_tree() -> void:
	range_button.text = "범위 %d촌" % int(host.KinRange)
	for c in tree_box.get_children(): c.queue_free()
	var rows: Array = host.KinTree()
	var gen_now := -99
	for e in rows:
		var g := int(e["gen"])
		if g != gen_now:
			gen_now = g
			var title := "같은 대" if g == 0 else ("윗대 %d" % -g if g < 0 else "아랫대 %d" % g)
			tree_box.add_child(UI.label(title, 13, UI.MUTED, true))
		var r := HBoxContainer.new()
		r.add_theme_constant_override("separation", 8)
		r.add_child(UI.avatar(e["name"], e["male"], 22))
		var nm := UI.label(e["name"], 14, UI.INK if e["alive"] else UI.MUTED, e["mine"])
		nm.custom_minimum_size = Vector2(170, 0)
		r.add_child(nm)
		r.add_child(UI.label("%d촌" % int(e["chon"]) if int(e["chon"]) > 0 else "나", 12, UI.MUTED))
		var info := ("%d세" % int(e["age"])) if e["alive"] else ("%d년에 죽음" % int(e["died"]) if int(e["died"]) > 0 else "죽음")
		if e["houseLabel"] != "": info += " · " + e["houseLabel"]
		var il := UI.label(info, 12, UI.MUTED)
		il.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		r.add_child(il)
		if e["mine"]: r.add_child(UI.chip("맡은 집", Color(0.85, 0.92, 0.9), UI.ACT, 11))
		elif e["canTake"]:
			var hid := int(e["house"])
			var t := UI.ghost("맡기")
			t.pressed.connect(func():
				_switch_to(hid)
				tree_panel.visible = false)
			r.add_child(t)
		tree_box.add_child(r)
	_show_panel(tree_panel)


func _open_orgs() -> void:
	org_title.text = "조직"
	for c in org_box.get_children(): c.queue_free()
	var rows: Array = host.OrgPanel()
	if rows.is_empty():
		org_box.add_child(UI.label("내 집과 이어진 조직이 없다. 식구를 골라 자기 자신을 누르면 조직을 세울 수 있다.", 13, UI.MUTED))
	for e in rows:
		var depth := int(e["depth"])
		if depth == 0 and org_box.get_child_count() > 0: org_box.add_child(HSeparator.new())
		var r := HBoxContainer.new()
		r.add_theme_constant_override("separation", 8)
		var pad := Control.new()
		pad.custom_minimum_size = Vector2(22 * depth, 0)
		r.add_child(pad)
		r.add_child(_center(UI.chip(e["label"], Color(0.85, 0.92, 0.9) if not e["manual"] else Color(0.98, 0.88, 0.7), UI.ACT, 11)))
		var tv := VBoxContainer.new()
		tv.add_theme_constant_override("separation", -2)
		tv.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		tv.add_child(UI.label(e["name"] + ("  · 지배자에게 붙음" if e["attached"] else ""), 14, UI.INK, depth == 0))
		var info := "%s · 바로 %d명" % [e["leader"], int(e["members"])]
		if int(e["inner"]) > 0: info += "(측근 %d, 소속원 %d)" % [int(e["inner"]), max(0, int(e["members"]) - int(e["inner"]) - 1)]
		if int(e["total"]) != int(e["members"]): info += " · 모두 %d명" % int(e["total"])
		info += " · 싸움 힘 %.0f" % float(e["strength"])
		if depth == 0: info += " · 권위 %s(%.0f) · %s" % [_authority_word(float(e["authority"])), float(e["authority"]), e["visibilityName"]]
		tv.add_child(UI.label(info, 12, UI.MUTED))
		tv.add_child(UI.label(("정한 일: " if e["manual"] else "맡은 일: ") + e["tasks"], 12, UI.INK))
		if depth == 0 and String(e.get("rep", "")) != "":
			tv.add_child(UI.label(String(e["rep"]), 12, UI.MUTED))
		if depth == 0 and e["shares"] != "":
			tv.add_child(UI.label("정체: %s · 일 비중 %s" % [e["identity"], e["shares"]], 12, UI.MUTED))
		if e["vacant"]:
			tv.add_child(UI.label("지휘자 없음: 일이 저절로 내려가지 않고 가장 큰 일 하나만 한다", 12, UI.RISK))
		if float(e["over"]) > 0.0:
			tv.add_child(UI.label("지휘 범위를 %d%% 넘음: 일과 싸움 힘 %d%%, 해마다 사람이 떠난다. 갈래를 나누고 지휘자를 앉힌다" % [int(float(e["over"]) * 100.0), int(float(e["cmdFactor"]) * 100.0)], 12, UI.RISK))
		if depth == 0 and float(e["threat"]) >= 0.3:
			var stage_names := ["", "경고", "조건 걸기", "끌어들이기나 누르기"]
			var tt := float(e["threat"])
			var stg := 1 if tt < 0.5 else (2 if tt < 0.8 else 3)
			tv.add_child(UI.label("드러난 무력 %.1f · 지배자가 느끼는 위협 %.2f(%s 단계) · 걸린 조건 %s" % [float(e["visForce"]), tt, stage_names[stg], e["condition"]], 12, UI.RISK))
		if float(e["arms"]) > 0.0 or e["fullTime"]:
			tv.add_child(UI.label(("상근 · " if e["fullTime"] else "") + "무구 보급 %d%%" % int(float(e["arms"]) * 100.0), 12, UI.MUTED))
		if depth > 0 and float(e["over"]) <= 0.0 and not e["vacant"]:
			tv.add_child(UI.label("장악도 %d%%(거사 때 이만큼이 지휘자를 따른다)" % int(float(e["grip"]) * 100.0), 12, UI.MUTED))
		var teach: String = "가르침: " + e["lesson"] + (" %d%%" % int(float(e["teachShare"]) * 100.0) if float(e["teachShare"]) > 0.0 else "")
		if e["heir"] != "": teach += " · 후계자 %s(이음 %d%%)" % [e["heir"], int(float(e["inherit"]) * 100.0)]
		tv.add_child(UI.label(teach, 12, UI.MUTED))
		r.add_child(tv)
		if e["lordAct"] != "":
			var lid := int(e["id"])
			var la := UI.ghost("%s (%d%%)" % [e["lordAct"], int(float(e["lordChance"]) * 100.0)])
			la.tooltip_text = "지배자와의 사이를 바꾼다. 붙은 조직은 지배자의 보호와 보탬을 받고 번 것의 30%를 바친다"
			la.pressed.connect(func():
				_set_hint(host.OrgLordAct(lid))
				for line in host.DrainChronicle(): _log(line)
				_open_orgs())
			r.add_child(_center(la))
		if e["command"]:
			var id := int(e["id"])
			if e["canSplit"]:
				var sb := UI.ghost("갈래 나누기")
				sb.tooltip_text = "바로 딸린 사람의 절반으로 새 갈래를 만든다"
				sb.pressed.connect(func():
					host.SplitOrg(id)
					_open_orgs())
				r.add_child(_center(sb))
			var nb := UI.ghost("이름 짓기")
			nb.pressed.connect(func(): _open_org_name(id, e["name"], e["named"]))
			r.add_child(_center(nb))
			var lb := UI.ghost("가르치기")
			lb.tooltip_text = "무엇을 얼마나 가르칠지(가르치는 만큼 일이 준다)"
			lb.pressed.connect(func(): _open_org_lessons(id, e["name"]))
			r.add_child(_center(lb))
			if e["school"]:
				var sb2 := UI.ghost("학교")
				sb2.tooltip_text = "누구를 얼마 동안 무엇을 가르치고, 끝나면 어디로 보낼지"
				sb2.pressed.connect(func(): _open_school(id, e["name"]))
				r.add_child(_center(sb2))
			if e["producer"]:
				var pb := UI.ghost("생산")
				pb.tooltip_text = "무엇을 만들어 어디로 보낼지(보급, 납품, 비축, 직판)"
				pb.pressed.connect(func(): _open_production(id, e["name"]))
				r.add_child(_center(pb))
			var ftb := UI.ghost("상근 풀기" if e["fullTime"] else "상근")
			ftb.tooltip_text = "상근: 집 농사를 하지 않고 이 갈래 일과 싸움에 1.6배로 나선다. 세력 곳간이 한 사람 한 해 식량 24를 대야 한다"
			var ft_on: bool = not e["fullTime"]
			ftb.pressed.connect(func():
				host.SetFullTime(id, ft_on)
				for line in host.DrainChronicle(): _log(line)
				_open_orgs())
			r.add_child(_center(ftb))
			if depth == 0:
				var fb := UI.ghost("시설")
				fb.tooltip_text = "세력이 짓고 갈래에 배정하는 시설(학당, 공방, 방앗간, 여관, 예배당, 창고, 병영, 요새 집)"
				fb.pressed.connect(func(): _open_facilities(id, e["name"]))
				r.add_child(_center(fb))
			if depth == 0 and e.get("faith", false):
				var fb2 := UI.ghost("신앙")
				fb2.tooltip_text = "헌금 회칙과 바치기. 믿음이 깊은 신도는 광신도가 되어 떠나지 않고 굶어도 낸다"
				fb2.pressed.connect(func(): _open_faith(id, e["name"]))
				r.add_child(_center(fb2))
			if depth == 0:
				var wgb := UI.ghost("임금")
				wgb.tooltip_text = "직책마다 임금 단계(없음·적음·보통·많음·초호화). 많이 줄수록 조직 충성이 오른다. 우두머리 집에서 주면 대신 우두머리 개인 충성이 오른다"
				wgb.pressed.connect(func(): _open_wages(id, e["name"]))
				r.add_child(_center(wgb))
			if depth == 0:
				var bb := UI.ghost("예산")
				bb.tooltip_text = "곳간을 갈래마다 나눈다. 몫이 모자란 갈래는 일을 덜 하고 지휘자의 불만이 쌓인다. 감시가 있으면 파벌이 보인다"
				bb.pressed.connect(func(): _open_budget(id, e["name"]))
				r.add_child(_center(bb))
			var wb := UI.ghost("일 비중")
			wb.tooltip_text = "여러 일을 고르고 비중을 나눈다. 비중이 정체(이름)를 정한다"
			wb.pressed.connect(func(): _open_org_weights(id, e["name"]))
			r.add_child(_center(wb))
			if depth > 0:
				var cb := UI.ghost("지휘자")
				cb.tooltip_text = "이 갈래의 지휘자를 앉힌다. 비우면 일이 내려가지 않는다"
				cb.pressed.connect(func(): _open_appoint(id, 0, e["name"] + " · 지휘자"))
				r.add_child(_center(cb))
			var tb := UI.ghost("일 정하기")
			tb.tooltip_text = "이 갈래가 할 일을 직접 정한다"
			tb.pressed.connect(func(): _open_org_tasks(id, e["name"]))
			r.add_child(_center(tb))
			if e["manual"]:
				var ab := UI.ghost("알아서 하게")
				ab.tooltip_text = "정한 일을 풀고 우두머리가 스스로 일을 고르게 한다"
				ab.pressed.connect(func():
					host.OrgAuto(id)
					_open_orgs())
				r.add_child(_center(ab))
		org_box.add_child(r)
		# 맨 위 조직: 직책(곳간지기, 연락책)과 감시 세기
		if depth == 0 and e["command"]:
			var rrow := HBoxContainer.new()
			rrow.add_theme_constant_override("separation", 6)
			var rp := Control.new()
			rp.custom_minimum_size = Vector2(22, 0)
			rrow.add_child(rp)
			var rid := int(e["id"])
			var tr_txt: String = e["treasurer"] if e["treasurer"] != "" else "비어 있음"
			var li_txt: String = e["liaison"] if e["liaison"] != "" else "비어 있음"
			var rl := UI.label("곳간지기 %s · 연락책 %s · 감시 세기 %.1f" % [tr_txt, li_txt, float(e["survey"])], 12, UI.MUTED)
			rl.tooltip_text = "곳간지기가 없으면 큰 조직의 번 것이 샌다. 연락책은 새어 나감을 줄인다. 감시는 감찰 일을 맡은 갈래가 낸다"
			rrow.add_child(_center(rl))
			var tbn := UI.ghost("곳간지기")
			tbn.pressed.connect(func(): _open_appoint(rid, 1, e["name"] + " · 곳간지기"))
			rrow.add_child(_center(tbn))
			var lbn := UI.ghost("연락책")
			lbn.pressed.connect(func(): _open_appoint(rid, 2, e["name"] + " · 연락책"))
			rrow.add_child(_center(lbn))
			org_box.add_child(rrow)
		# 맨 위 조직: 알려진 정도 고르기(숨길수록 의심을 피하고, 드러낼수록 사람이 모인다)
		if depth == 0 and e["command"]:
			var seg := HBoxContainer.new()
			seg.add_theme_constant_override("separation", 0)
			var pad2 := Control.new()
			pad2.custom_minimum_size = Vector2(22, 0)
			seg.add_child(pad2)
			var vl := UI.label("알려진 정도 ", 12, UI.MUTED)
			vl.tooltip_text = "숨길수록 의심을 피하지만 사람을 직접 끌어들여야 한다. 드러낼수록 사람이 스스로 모인다"
			seg.add_child(_center(vl))
			var vnames := ["완전 은폐", "일부 은폐", "중립", "공개", "완전 공개"]
			var gid := int(e["id"])
			for k in 5:
				var b := Button.new()
				b.text = vnames[k]
				b.add_theme_font_size_override("font_size", 12)
				UI.segment(b, int(e["visibility"]) == k, k == 0, k == 4)
				var kk := k
				b.pressed.connect(func():
					host.SetOrgVisibility(gid, kk)
					for line in host.DrainChronicle(): _log(line)
					_open_orgs())
				seg.add_child(b)
			org_box.add_child(seg)
			# 회칙: 누가 들어오나, 번 것을 어떻게 나누나
			for row in [["들기", ["아무나", "혈족만", "신앙 깊은 자만"], "joinRule"], ["나눔", ["우두머리가 다", "고르게", "곳간에 쌓음"], "shareRule"]]:
				var rs := HBoxContainer.new()
				rs.add_theme_constant_override("separation", 0)
				var lab := UI.label("   " + row[0] + " ", 12, UI.MUTED)
				lab.tooltip_text = "누가 들어올 수 있나" if row[0] == "들기" else "번 것을 어떻게 나누나"
				lab.custom_minimum_size = Vector2(60, 0)
				rs.add_child(lab)
				var opts: Array = row[1]
				for k in opts.size():
					var b := Button.new()
					b.text = opts[k]
					b.add_theme_font_size_override("font_size", 12)
					UI.segment(b, int(e[row[2]]) == k, k == 0, k == opts.size() - 1)
					var kk := k
					var key: String = row[2]
					var cur_join := int(e["joinRule"]); var cur_share := int(e["shareRule"])
					b.pressed.connect(func():
						if key == "joinRule": host.SetOrgRules(gid, kk, cur_share)
						else: host.SetOrgRules(gid, cur_join, kk)
						for line in host.DrainChronicle(): _log(line)
						_open_orgs())
					rs.add_child(b)
				org_box.add_child(rs)
			if float(e["treasury"]) > 0.5: org_box.add_child(UI.label("      곳간 은화 %d" % int(float(e["treasury"])), 12, UI.MUTED))
	_show_panel(org_panel)


func _open_showcase() -> void:
	for c in show_box.get_children(): c.queue_free()
	for e in host.Showcase():
		var r := HBoxContainer.new()
		r.add_theme_constant_override("separation", 8)
		var tv := VBoxContainer.new()
		tv.add_theme_constant_override("separation", -2)
		tv.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var top := HBoxContainer.new()
		top.add_theme_constant_override("separation", 6)
		top.add_child(UI.chip(e["what"], Color(0.85, 0.92, 0.9), UI.ACT, 11))
		top.add_child(UI.label(e["name"], 14, UI.INK, true))
		tv.add_child(top)
		tv.add_child(UI.label(e["why"], 12, UI.MUTED))
		r.add_child(tv)
		var hid := int(e["id"]); var hx := int(e["x"]); var hy := int(e["y"])
		var look := UI.ghost("보기")
		look.pressed.connect(func():
			show_panel.visible = false
			_look_at_house(hid, hx, hy))
		r.add_child(_center(look))
		var take := UI.ghost("맡기")
		take.pressed.connect(func():
			show_panel.visible = false
			_recenter(Vector2i(hx, hy))
			_take_house(hid)
			_on_player_changed())
		r.add_child(_center(take))
		show_box.add_child(r)
	_show_panel(show_panel)


func _refresh_judge_button() -> void:
	var n: int = host.PendingCount if host != null and host.Ready else 0
	judge_button.text = "재판 %d" % n if n > 0 else "재판"
	judge_button.add_theme_color_override("font_color", UI.RISK if n > 0 else UI.INK)
	var d: int = host.DemandCount if host != null and host.Ready else 0
	rise_button.text = "다툼 %d" % d if d > 0 else "다툼"
	rise_button.add_theme_color_override("font_color", UI.RISK if d > 0 else UI.INK)


func _open_rise() -> void:
	for c in rise_box.get_children(): c.queue_free()
	var demands: Array = host.Demands()
	if not demands.is_empty():
		rise_box.add_child(UI.label("받은 요구", 13, UI.MUTED, true))
		for e in demands:
			var box := VBoxContainer.new()
			box.add_theme_constant_override("separation", 2)
			var who: String = e["lord"] + (" (%s)" % e["realm"] if e["realm"] != "" else "")
			box.add_child(UI.label(who, 15, UI.INK, true))
			box.add_child(UI.label("%s · %s" % [e["date"], e["text"]], 13))
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 6)
			var id := int(e["id"])
			for o in [[e.get("yes", "받는다"), true, false], [e.get("no", "거절한다"), false, true]]:
				var b := UI.action(o[0], "", -1.0, o[2])
				b.custom_minimum_size = Vector2(140, 40)
				var yes: bool = o[1]
				b.pressed.connect(func():
					host.AnswerDemand(id, yes)
					for line in host.DrainChronicle(): _log(line)
					_refresh_judge_button()
					_refresh_family()
					_open_rise())
				row.add_child(b)
			box.add_child(row)
			rise_box.add_child(box)
		rise_box.add_child(HSeparator.new())
	# 다음 단계 판: 지금 처지와 다음 단계까지 모자란 것
	var pg: Dictionary = host.Progress()
	if not pg.is_empty():
		var top := "지금 %s · 따르는 집 %d · 명예 %+.1f" % [pg["tier"], int(pg["clients"]), float(pg["honor"])]
		if pg["legit"] != "": top += " · 칭호 " + pg["legit"]
		if pg["suspicion"] != "": top += " · 주군이 " + pg["suspicion"]
		rise_box.add_child(UI.label(top, 14, UI.INK, true))
		var parts := []
		for row in pg["rows"]:
			parts.append("%s %s %d/%d" % ["✓" if row["ok"] else "·", row["what"], int(float(row["have"])), int(ceil(float(row["need"])))])
		var pl2 := UI.label("%s까지: %s" % [pg["next"], ", ".join(parts)], 12, UI.MUTED)
		pl2.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		pl2.custom_minimum_size = Vector2(600, 0)
		rise_box.add_child(pl2)
		rise_box.add_child(HSeparator.new())
	# 내가 꾸미는 거사
	var plot: Dictionary = host.PlotInfo()
	if not plot.is_empty():
		rise_box.add_child(UI.label("내가 꾸미는 거사", 13, UI.MUTED, true))
		var yrs := "올해 시작" if int(plot["years"]) == 0 else "%d년째" % int(plot["years"])
		rise_box.add_child(UI.label("%s%s · %s" % [plot["lord"], (" (%s)" % plot["realm"]) if plot["realm"] != "" else "", yrs], 15, UI.INK, true))
		var pinfo := "편든 사람: %s · 아는 사람 %d명(많을수록 새어 나가기 쉽다)" % [plot["backers"] if plot["backers"] != "" else "아직 없음", int(plot["known"])]
		var pl := UI.label(pinfo, 12, UI.MUTED)
		pl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		pl.custom_minimum_size = Vector2(600, 0)
		rise_box.add_child(pl)
		var ratio := float(plot["ratio"])
		var odds := "크게 이길 만하다" if ratio >= 0.6 else ("비등하다(내전이 될 수 있다)" if ratio >= 0.4 else "질 것 같다")
		rise_box.add_child(UI.label("지금 일으키면: 우리 쪽 힘 %d%% · %s · 편들 이 %d, 맞설 이 %d" % [int(ratio * 100.0), odds, int(plot["mine"]), int(plot["theirs"])], 13, UI.INK))
		if plot["exposed"]:
			rise_box.add_child(UI.label("들켰다. 지배자의 처분을 기다린다.", 13, UI.RISK, true))
		else:
			var prow := HBoxContainer.new()
			prow.add_theme_constant_override("separation", 6)
			var sb := UI.action("거사를 일으킨다", "", ratio, true)
			sb.custom_minimum_size = Vector2(200, 44)
			sb.pressed.connect(func():
				host.PlotAct(true)
				for line in host.DrainChronicle(): _log(line)
				_redraw()
				_open_rise())
			prow.add_child(sb)
			var ab := UI.ghost("거사를 접는다")
			ab.pressed.connect(func():
				host.PlotAct(false)
				for line in host.DrainChronicle(): _log(line)
				_open_rise())
			prow.add_child(_center(ab))
			rise_box.add_child(prow)
			var cands: Array = plot["candidates"]
			if not cands.is_empty():
				rise_box.add_child(UI.label("끌어들일 만한 사람(거절하면 지배자에게 알릴 수 있다)", 12, UI.MUTED))
				var grid := GridContainer.new()
				grid.columns = 3
				grid.add_theme_constant_override("h_separation", 6)
				grid.add_theme_constant_override("v_separation", 6)
				var pa := int(plot["actor"])
				for c in cands:
					var b := UI.action(c["name"], "", float(c["chance"]), true)
					b.custom_minimum_size = Vector2(200, 44)
					var ci := int(c["i"])
					b.pressed.connect(func():
						_set_hint(host.Interact(_plot_recruit_id(), pa, ci, -1, 0.0))
						for line in host.DrainChronicle(): _log(line)
						_open_rise())
					grid.add_child(b)
				rise_box.add_child(grid)
		rise_box.add_child(HSeparator.new())
	# 지배자 도구: 세율, 내 땅의 조직
	var lt: Dictionary = host.LordTools()
	if not lt.is_empty():
		var trow := HBoxContainer.new()
		trow.add_theme_constant_override("separation", 0)
		var tl := UI.label("세율 ", 13, UI.MUTED, true)
		tl.tooltip_text = "아래 집 수확에서 걷는 몫. 12%보다 무거우면 아래 집들이 해마다 나를 나쁘게 보고, 가벼우면 좋게 본다"
		trow.add_child(_center(tl))
		var taxes := [8, 10, 12, 15, 20]
		for k in taxes.size():
			var b := Button.new()
			b.text = "%d%%" % taxes[k]
			b.add_theme_font_size_override("font_size", 12)
			UI.segment(b, int(lt["tax"]) == taxes[k], k == 0, k == taxes.size() - 1)
			var pct: int = taxes[k]
			b.pressed.connect(func():
				host.SetTax(pct)
				for line in host.DrainChronicle(): _log(line)
				_open_rise())
			trow.add_child(b)
		var cbtn := UI.ghost("공납: 은화로" if lt["coin"] else "공납: 현물로")
		cbtn.tooltip_text = "현물은 흉년에 강하고, 은화는 품삯을 주기 쉽다. 눌러 바꾼다"
		var now_coin: bool = lt["coin"]
		cbtn.pressed.connect(func():
			host.SetTributeCoin(not now_coin)
			for line in host.DrainChronicle(): _log(line)
			_open_rise())
		trow.add_child(_center(cbtn))
		rise_box.add_child(trow)
		if int(lt["subjects"]) > 0:
			var bl := UI.label("세력원 %d집 · 떠날 듯한 집 %d · 인지도 %.2f · 평판 %+.2f · 공포 %.2f\n섬기는 까닭 평균: %s" % [int(lt["subjects"]), int(lt["wavering"]), float(lt.get("fame", 0.0)), float(lt.get("standing", 0.0)), float(lt["fear"]), lt["belongAvg"]], 12, UI.MUTED)
			bl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			bl.custom_minimum_size = Vector2(600, 0)
			bl.tooltip_text = "집마다 남을 까닭(보호, 생계, 공포, 충성, 혜택, 관습)에서 부담을 뺀 값이 다른 길보다 0.1 넘게 모자라면 해마다 30% 확률로 떠난다"
			rise_box.add_child(bl)
		var lorgs: Array = lt["orgs"]
		if not lorgs.is_empty():
			rise_box.add_child(UI.label("내 땅의 조직", 13, UI.MUTED, true))
			for o in lorgs:
				var orow := HBoxContainer.new()
				orow.add_theme_constant_override("separation", 6)
				var on := UI.label("%s · %s %d명 · %s%s · 위협 %.2f%s" % [o["name"], o["kind"], int(o["total"]), o["leader"], " · 내게 붙음" if o["attached"] else "", float(o["threat"]), "" if o["condition"] == "없음" else " · 조건 " + o["condition"]], 13)
				on.size_flags_horizontal = Control.SIZE_EXPAND_FILL
				orow.add_child(on)
				for m in o["moves"]:
					var b := UI.action(m["name"], "", float(m["chance"]), m["harsh"])
					b.custom_minimum_size = Vector2(170, 38)
					var mid := int(m["id"]); var tgt := int(m["target"]); var act := int(m["actor"])
					b.pressed.connect(func():
						_set_hint(host.Interact(mid, act, tgt, -1, 0.0))
						for line in host.DrainChronicle(): _log(line)
						_open_rise())
					orow.add_child(b)
				for cd in o["conds"]:
					var cb := UI.action(cd["name"], "", float(cd["chance"]), true)
					cb.custom_minimum_size = Vector2(130, 38)
					var cgid := int(cd["gid"]); var ck := int(cd["cond"])
					cb.pressed.connect(func():
						_set_hint(host.ImposeCondition(cgid, ck))
						for line in host.DrainChronicle(): _log(line)
						_open_rise())
					orow.add_child(cb)
				rise_box.add_child(orow)
		var wars: Array = lt["wars"]
		if not wars.is_empty():
			rise_box.add_child(UI.label("싸움을 걸 수 있는 지배자(이기면 내 봉신이 된다. 두 쪽 다 군인을 잃는다)", 13, UI.MUTED, true))
			var wg := GridContainer.new()
			wg.columns = 2
			wg.add_theme_constant_override("h_separation", 6)
			wg.add_theme_constant_override("v_separation", 6)
			for m in wars:
				var b := UI.action(m["name"], "", float(m["chance"]), true)
				b.custom_minimum_size = Vector2(300, 40)
				var mid := int(m["id"]); var tgt := int(m["target"]); var act := int(m["actor"])
				b.pressed.connect(func():
					_set_hint(host.Interact(mid, act, tgt, -1, 0.0))
					for line in host.DrainChronicle(): _log(line)
					_redraw()
					_open_rise())
				wg.add_child(b)
			rise_box.add_child(wg)
		rise_box.add_child(HSeparator.new())
	# 남의 세력: 심어 둔 사람의 자리만큼 보인다
	var intel: Array = host.Intel()
	if not intel.is_empty():
		rise_box.add_child(UI.label("남의 세력(심어 둔 사람이 보는 것)", 13, UI.MUTED, true))
		for it in intel:
			rise_box.add_child(UI.label("%s · %s · %s" % [it["name"], it["kind"], it["agents"]], 13, UI.INK, true))
			var sl := UI.label(it["seen"], 12, UI.MUTED)
			sl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			sl.custom_minimum_size = Vector2(600, 0)
			rise_box.add_child(sl)
		rise_box.add_child(HSeparator.new())
	var rows: Array = host.RiseRows()
	var titles := {"lord": "내가 눈여겨보는 세력", "riser": "나를 눈여겨보는 지배자"}
	for side in ["lord", "riser"]:
		var any := false
		for e in rows:
			if e["side"] != side: continue
			if not any:
				rise_box.add_child(UI.label(titles[side], 13, UI.MUTED, true))
				any = true
			var head := HBoxContainer.new()
			head.add_theme_constant_override("separation", 8)
			var nm := UI.label(e["name"] + (" (%s)" % e["realm"] if e["realm"] != "" else ""), 15, UI.INK, true)
			nm.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			head.add_child(nm)
			head.add_child(UI.chip(e["suspicion"], Color(0.97, 0.86, 0.82), UI.RISK, 11))
			var hid := int(e["house"]); var hx := int(e["x"]); var hy := int(e["y"])
			var look := UI.ghost("보기")
			look.pressed.connect(func():
				rise_panel.visible = false
				_look_at_house(hid, hx, hy))
			head.add_child(look)
			rise_box.add_child(head)
			var info := "따르는 집 %d" % int(e["size"])
			if e["stole"]: info += " · 내 집을 빼 갔다" if side == "lord" else " · 그의 집을 빼 왔다"
			rise_box.add_child(UI.label(info, 12, UI.MUTED))
			var grid := GridContainer.new()
			grid.columns = 3
			grid.add_theme_constant_override("h_separation", 6)
			grid.add_theme_constant_override("v_separation", 6)
			for m in e["moves"]:
				var b := UI.action(m["name"], "", float(m["chance"]), m["harsh"])
				b.custom_minimum_size = Vector2(200, 44)
				var mid := int(m["id"]); var tgt := int(m["target"]); var act := int(m["actor"])
				b.pressed.connect(func():
					_set_hint(host.Interact(mid, act, tgt, -1, 0.0))
					for line in host.DrainChronicle(): _log(line)
					_refresh_judge_button()
					_open_rise())
				grid.add_child(b)
			rise_box.add_child(grid)
			rise_box.add_child(HSeparator.new())
	if demands.is_empty() and rows.is_empty() and plot.is_empty() and int(pg.get("clients", 0)) == 0:
		rise_box.add_child(UI.label("지금은 다툼이 없다. 세를 늘리면 위의 지배자가 알아챌 수 있고, 내 아래에서 세를 늘리는 집이 생기면 여기에 나온다.", 13, UI.MUTED))
	_show_panel(rise_panel)


func _open_judge() -> void:
	for c in judge_box.get_children(): c.queue_free()
	# 벌의 세기와 직접 판결할 사건
	judge_box.add_child(UI.label("벌의 세기: 내 땅 사람들이 보는 두려움과 맡겨 둔 사건의 판결을 정한다", 12, UI.MUTED))
	var seg := HBoxContainer.new()
	seg.add_theme_constant_override("separation", 0)
	var names := ["너그러움", "보통", "엄함"]
	for k in 3:
		var b := Button.new()
		b.text = names[k]
		UI.segment(b, int(host.Severity) == k, k == 0, k == 2)
		var kk := k
		b.pressed.connect(func():
			host.Severity = kk
			_open_judge())
		seg.add_child(b)
	judge_box.add_child(seg)
	var jr := HBoxContainer.new()
	jr.add_theme_constant_override("separation", 8)
	jr.add_child(_center(UI.label("직접 판결: " + ("모든 사건" if host.JudgeAll else "혈족·신하·살인만"), 13, UI.INK)))
	var all := UI.ghost("혈족·신하·살인만으로 바꾸기" if host.JudgeAll else "모든 사건으로 바꾸기")
	all.pressed.connect(func():
		host.JudgeAll = not host.JudgeAll
		_open_judge())
	jr.add_child(all)
	judge_box.add_child(jr)
	judge_box.add_child(HSeparator.new())
	var cases: Array = host.Cases()
	if cases.is_empty():
		judge_box.add_child(UI.label("판결을 기다리는 사건이 없다. 한 해 안에 판결하지 않은 사건은 정한 세기로 판결된다.", 13, UI.MUTED))
	for e in cases:
		var box := VBoxContainer.new()
		box.add_theme_constant_override("separation", 2)
		var head := HBoxContainer.new()
		head.add_theme_constant_override("separation", 8)
		head.add_child(UI.chip(e["crime"], Color(0.97, 0.86, 0.82), UI.RISK, 12))
		head.add_child(UI.label("범인 " + e["culprit"], 15, UI.INK, true))
		if e["mine"] != "": head.add_child(UI.chip(e["mine"], Color(0.85, 0.92, 0.9), UI.ACT, 11))
		box.add_child(head)
		var info := "%s · 피해 %s · 증인 %d" % [e["date"], e["victim"], int(e["witnesses"])]
		if float(e["amount"]) > 0.5: info += " · 식량 %d" % int(float(e["amount"]))
		box.add_child(UI.label(info, 12, UI.MUTED))
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 6)
		var id := int(e["id"])
		var opts := [["벌금", 3, false], ["매질", 4, false], ["처형", 5, true], ["풀어 주기", 7, false]]
		if e["canCover"]: opts.append(["덮기", 6, true])
		for o in opts:
			var b := UI.action(o[0], "", -1.0, o[2])
			b.custom_minimum_size = Vector2(110, 40)
			var v: int = o[1]
			b.pressed.connect(func():
				host.JudgeCase(id, v)
				for line in host.DrainChronicle(): _log(line)
				_refresh_judge_button()
				_open_judge())
			row.add_child(b)
		box.add_child(row)
		judge_box.add_child(box)
		judge_box.add_child(HSeparator.new())
	var recent: Array = host.RecentCases()
	if not recent.is_empty():
		judge_box.add_child(UI.label("최근 사건", 13, UI.MUTED, true))
		for e in recent:
			var r := HBoxContainer.new()
			r.add_theme_constant_override("separation", 8)
			var d := UI.label(e["date"], 12, UI.MUTED)
			d.custom_minimum_size = Vector2(52, 0)
			r.add_child(d)
			var t := UI.label("%s · 범인 %s · 피해 %s" % [e["crime"], e["culprit"], e["victim"]], 13)
			t.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			r.add_child(t)
			r.add_child(UI.label(e["verdict"], 12, UI.MUTED, true))
			judge_box.add_child(r)
	_show_panel(judge_panel)


func _open_org_name(id: int, name: String, named: bool) -> void:
	org_title.text = name + " · 이름 짓기"
	for c in org_box.get_children(): c.queue_free()
	org_box.add_child(UI.label("세력이 커져도 지은 이름은 그대로 간다. 갈래 이름은 위 조직 이름을 따라간다.", 12, UI.MUTED))
	var edit := LineEdit.new()
	edit.text = name
	edit.max_length = 20
	edit.custom_minimum_size = Vector2(400, 36)
	edit.add_theme_font_override("font", UI.font())
	edit.add_theme_font_size_override("font_size", 16)
	edit.add_theme_color_override("font_color", UI.INK)
	edit.add_theme_color_override("caret_color", UI.INK)
	edit.add_theme_color_override("font_selected_color", UI.INK)
	edit.add_theme_color_override("selection_color", Color(0.24, 0.42, 0.45, 0.35))
	var es := UI.box(Color(1, 0.98, 0.93), 6, UI.BORDER, 1)
	es.content_margin_left = 10; es.content_margin_right = 10
	edit.add_theme_stylebox_override("normal", es)
	edit.add_theme_stylebox_override("focus", es)
	org_box.add_child(edit)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	var ok := UI.action("이 이름으로", "", -1.0, false)
	ok.custom_minimum_size = Vector2(200, 0)
	var apply := func(t: String):
		host.RenameOrg(id, t)
		for line in host.DrainChronicle(): _log(line)
		_open_orgs()
	ok.pressed.connect(func(): apply.call(edit.text))
	edit.text_submitted.connect(func(t: String): apply.call(t))
	row.add_child(ok)
	if named:
		var back := UI.ghost("저절로 붙는 이름으로")
		back.pressed.connect(func(): apply.call(""))
		row.add_child(_center(back))
	var cancel := UI.ghost("그만두기")
	cancel.pressed.connect(_open_orgs)
	row.add_child(_center(cancel))
	org_box.add_child(row)
	edit.grab_focus()
	edit.select_all()


func _open_org_lessons(id: int, name: String) -> void:
	org_title.text = name + " · 가르치기"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	var note := UI.label("가르치는 몫만큼 갈래의 일이 줄고, 한 사람에 한 해 식량 8 × 몫이 든다. 교리는 충성을 0.8까지만 올린다. 이음은 후계자에게 우두머리가 아는 것을 넘긴다.", 12, UI.MUTED)
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.custom_minimum_size = Vector2(600, 0)
	org_box.add_child(note)
	var names := ["없음", "무예", "글", "장사", "공예", "교리", "이음"]
	for k in names.size():
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 6)
		var lab := UI.label(names[k], 14, UI.INK, true)
		lab.custom_minimum_size = Vector2(90, 0)
		row.add_child(_center(lab))
		var shares := [0] if k == 0 else [10, 20, 30]
		for pct in shares:
			var b := UI.ghost("정하기" if k == 0 else "%d%%" % pct)
			var kk := k; var pp: int = pct
			b.pressed.connect(func():
				host.SetUnitLesson(id, kk, pp)
				for line in host.DrainChronicle(): _log(line)
				_open_orgs())
			row.add_child(b)
		org_box.add_child(row)


func _open_org_tasks(id: int, name: String) -> void:
	org_title.text = name + " · 일 정하기"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	var verb_now := -1
	for t in host.OrgTaskTargets(id):
		var verb := int(t["verb"])
		if verb != verb_now:
			verb_now = verb
			org_box.add_child(UI.label(t["verbName"], 13, UI.MUTED, true))
		if t["locked"] != "":
			var lk := UI.label("   잠김: " + t["locked"], 12, UI.MUTED)
			org_box.add_child(lk)
			continue
		var b := UI.action(t["verbName"] + " · " + t["place"], "%d집" % int(t["houses"]), -1.0, t["harsh"])
		var vv := int(t["village"])
		b.pressed.connect(func():
			host.SetOrgTask(id, verb, vv)
			for line in host.DrainChronicle(): _log(line)
			_open_orgs())
		org_box.add_child(b)


## 일 비중: 일마다 0~100%를 고르고 적용한다. 비중이 정체를 정한다.
func _open_org_weights(id: int, name: String) -> void:
	org_title.text = name + " · 일 비중"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	var note := UI.label("고른 일에 비중대로 사람과 시간을 나눈다. 가장 큰 일이 절반을 넘으면 그 이름을 받고, 둘이 비슷하면 합친 이름을 받는다(두 해 연속일 때). 지휘자가 있는 갈래로 일이 저절로 내려간다.", 12, UI.MUTED)
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.custom_minimum_size = Vector2(600, 0)
	org_box.add_child(note)
	var rows: Array = host.OrgWeights(id)
	var pick := {}
	for e in rows: pick[int(e["verb"])] = int(e["weight"])
	var steps := [0, 25, 50, 75, 100]
	for e in rows:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 0)
		var lab := UI.label(e["name"], 13, UI.RISK if e["harsh"] else UI.INK, true)
		lab.custom_minimum_size = Vector2(90, 0)
		row.add_child(_center(lab))
		var vb := int(e["verb"])
		if e["locked"] != "":
			row.add_child(_center(UI.label("잠김: " + e["locked"], 12, UI.MUTED)))
			org_box.add_child(row)
			continue
		var btns := []
		for k in steps.size():
			var b := Button.new()
			b.text = "%d" % steps[k]
			b.add_theme_font_size_override("font_size", 12)
			var cur: int = int(pick[vb])
			var near: int = steps[0]
			for st in steps:
				if abs(st - cur) < abs(near - cur): near = st
			UI.segment(b, near == steps[k], k == 0, k == steps.size() - 1)
			btns.append(b)
			row.add_child(b)
		for k in btns.size():
			var val: int = steps[k]
			var b2: Button = btns[k]
			b2.pressed.connect(func():
				pick[vb] = val
				for j in btns.size(): UI.segment(btns[j], j == k, j == 0, j == btns.size() - 1))
		org_box.add_child(row)
	var apply := UI.action("이대로 정한다", "", -1.0, false)
	apply.pressed.connect(func():
		var pairs := []
		for v in pick.keys():
			if int(pick[v]) > 0: pairs.append([int(v), float(pick[v])])
		host.SetOrgWeights(id, pairs)
		for line in host.DrainChronicle(): _log(line)
		_open_orgs())
	org_box.add_child(apply)
	_show_panel(org_panel)


## 학교 설정: 받는 사람, 기간, 과목 비중, 수업료, 졸업 뒤 갈 곳.
func _open_school(id: int, name: String) -> void:
	var info: Dictionary = host.SchoolInfo(id)
	if info.is_empty():
		_open_orgs()
		return
	org_title.text = name + " · 학교"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	org_box.add_child(UI.label("학생 %d / 받을 수 있는 수 %d · 교사 %d명 · 올해 들어옴 %d, 졸업 %d, 달아남 %d" % [int(info["students"]), int(info["capacity"]), int(info["teachers"]), int(info["enrolled"]), int(info["graduated"]), int(info["fled"])], 13, UI.INK, true))
	org_box.add_child(UI.label("이름: %s(졸업 뒤 갈 곳으로 정해진다) · 올해 질 비용 은화 %d" % [info.get("label", "학교"), int(float(info.get("cost", 0.0)))], 12, UI.INK))
	var note := UI.label("교사 한 명이 4 + 6 × 말솜씨 명을 받는다. 과목 비중이 25% 아래면 0.4까지, 50% 넘으면 0.8까지 오른다. 질이 높으면 더 빨리, 더 깊이 배운다. 교사 솜씨가 위를 막는다. 배우는 동안 일손은 3/4이다.", 12, UI.MUTED)
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.custom_minimum_size = Vector2(600, 0)
	org_box.add_child(note)
	var st := info.duplicate(true)
	var wmap := {}
	for wv in info["weights"]: wmap[int(wv["lesson"])] = float(wv["weight"])
	# 고르기 줄 하나: 이름, 보기, 고른 값을 st 에 넣는다
	var seg := func(label: String, opts: Array, cur: int, on_pick: Callable) -> void:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 0)
		var lab := UI.label(label + " ", 12, UI.MUTED)
		lab.custom_minimum_size = Vector2(80, 0)
		row.add_child(_center(lab))
		var btns := []
		for k in opts.size():
			var b := Button.new()
			b.text = str(opts[k])
			b.add_theme_font_size_override("font_size", 12)
			UI.segment(b, k == cur, k == 0, k == opts.size() - 1)
			btns.append(b)
			row.add_child(b)
		for k in btns.size():
			var bk: Button = btns[k]
			bk.pressed.connect(func():
				on_pick.call(k)
				for j in btns.size(): UI.segment(btns[j], j == k, j == 0, j == btns.size() - 1))
		org_box.add_child(row)
	# 숫자 줄: 이름, 최소~최대, 지금 값, 바뀌면 st 에 넣는다
	var num := func(label: String, lo: float, hi: float, cur: float, on_set: Callable) -> void:
		var row := HBoxContainer.new()
		var lab := UI.label(label + " ", 12, UI.MUTED)
		lab.custom_minimum_size = Vector2(80, 0)
		row.add_child(_center(lab))
		var sp := SpinBox.new()
		sp.min_value = lo; sp.max_value = hi; sp.step = 1; sp.value = cur
		sp.value_changed.connect(func(v): on_set.call(v))
		row.add_child(sp)
		org_box.add_child(row)
	num.call("받는 나이(부터)", 5, 40, int(info["minAge"]), func(v): st["minAge"] = int(v))
	num.call("받는 나이(까지)", 5, 40, int(info["maxAge"]), func(v): st["maxAge"] = int(v))
	num.call("수업료(세력 안)", 0, 50, float(info["fee"]), func(v): st["fee"] = float(v))
	num.call("수업료(바깥)", 0, 50, float(info.get("feeOut", info["fee"])), func(v): st["feeOut"] = float(v))
	seg.call("질", ["없음", "적음", "보통", "많음", "초호화"], int(info.get("quality", 2)), func(k): st["quality"] = k)
	org_box.add_child(UI.label("질: 학생 한 명 한 해 은화 0·1·3·6·12와 비품 0·0.1·0.2·0.4·0.8. 배우는 속도 ×0.5~1.6, 깊이 −0.2~+0.2. 못 대면 한 단계 내려간다", 12, UI.MUTED))
	seg.call("학력", ["아무나", "글 모르는 사람만"], 0 if float(info["maxLetters"]) >= 1.0 else 1, func(k): st["maxLetters"] = 1.0 if k == 0 else 0.2)
	var tal := [0.0, 0.5, 0.7]
	seg.call("재능", ["아무나", "0.5 이상", "0.7 이상"], tal.find(snappedf(float(info["talentMin"]), 0.1)) if tal.has(snappedf(float(info["talentMin"]), 0.1)) else 0, func(k): st["talentMin"] = tal[k])
	seg.call("받는 곳", ["세력 안만", "바깥도"], 1 if info["outsiders"] else 0, func(k): st["outsiders"] = k == 1)
	var yrs := [1, 2, 3, 4, 6]
	seg.call("기간", ["1년", "2년", "3년", "4년", "6년"], max(0, yrs.find(int(info["years"]))), func(k): st["years"] = yrs[k])
	org_box.add_child(UI.label("과목 비중(여럿 고를 수 있다. 합이 100이 되게 나눈다)", 12, UI.MUTED, true))
	for wv in info["weights"]:
		var lsn := int(wv["lesson"])
		num.call(String(wv["name"]), 0, 100, int(wv["weight"]), func(v): wmap[lsn] = float(v))
	var dests := ["세력원으로", "정한 조직으로", "성적대로", "의무 복무 3년", "교사로 남음"]
	seg.call("졸업 뒤", dests, int(info["dest"]), func(k): st["dest"] = k)
	var units: Array = info["units"]
	if not units.is_empty():
		var names := []
		var cur_u := 0
		for k in units.size():
			names.append(units[k]["name"])
			if int(units[k]["id"]) == int(info["destUnit"]): cur_u = k
		seg.call("보낼 조직", names, cur_u, func(k): st["destUnit"] = int(units[k]["id"]))
		if int(info["destUnit"]) < 0: st["destUnit"] = int(units[0]["id"])
	var apply := UI.action("이대로 정한다", "", -1.0, false)
	apply.pressed.connect(func():
		st["weights"] = wmap
		host.SetSchool(id, st)
		for line in host.DrainChronicle(): _log(line)
		_open_school(id, name))
	org_box.add_child(apply)
	_show_panel(org_panel)


## 생산: 만드는 것과 공급 선.
func _open_production(id: int, name: String) -> void:
	var info: Dictionary = host.ProductionInfo(id)
	if info.is_empty():
		_open_orgs()
		return
	org_title.text = name + " · 생산"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	var note := UI.label("장인(공예·목공 0.2)이 만든다. 재료는 세력 곳간, 모자라면 장인 집에서 쓴다. 무구는 공방을 이 갈래에 배정해야 만든다. 채집(기초 재료)과 농사(식량)는 아무나 한다. 농사는 세력 땅에서 짓고 거둔 곡식은 우두머리 집에 든다. 세력 곳간: " + (info["store"] if info["store"] != "" else "비어 있음"), 12, UI.MUTED)
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.custom_minimum_size = Vector2(600, 0)
	org_box.add_child(note)
	org_box.add_child(UI.label("세력 땅: 산 필지 %d · 우두머리 집 필지 %d · 곳간 은화 %d" % [int(info.get("landPlots", 0)), int(info.get("homePlots", 0)), int(float(info.get("treasury", 0.0)))], 12, UI.INK))
	for sl in info.get("sellers", []):
		var sh := int(sl["house"])
		var lb := UI.action("%s의 필지 사기 · 은화 %d" % [sl["name"], int(float(sl["price"]))], "세력 곳간 은화로 산다. 땅은 우두머리 집에 들고, 우두머리가 바뀌면 새 우두머리 집으로 넘어간다", -1.0, false)
		lb.pressed.connect(func():
			_set_hint(host.BuyFactionLand(id, sh))
			_open_production(id, name))
		org_box.add_child(lb)
	var st := {"product": int(info["product"])}
	var prow := HBoxContainer.new()
	prow.add_theme_constant_override("separation", 0)
	prow.add_child(_center(UI.label("만들 것 ", 12, UI.MUTED)))
	var pbtns := []
	var goods: Array = info["goods"]
	for k in goods.size():
		var b := Button.new()
		b.text = goods[k]["name"] + (" (잠김)" if goods[k]["locked"] != "" else "")
		b.tooltip_text = goods[k]["locked"]
		b.disabled = goods[k]["locked"] != ""
		b.add_theme_font_size_override("font_size", 12)
		UI.segment(b, int(goods[k]["good"]) == int(info["product"]), k == 0, k == goods.size() - 1)
		pbtns.append(b)
		prow.add_child(b)
	for k in pbtns.size():
		var gk := int(goods[k]["good"])
		var bk: Button = pbtns[k]
		bk.pressed.connect(func():
			st["product"] = gk
			for j in pbtns.size(): UI.segment(pbtns[j], j == k, j == 0, j == pbtns.size() - 1))
	org_box.add_child(prow)
	org_box.add_child(UI.label("보낼 곳과 비중", 12, UI.MUTED, true))
	var pick := {}
	var steps := [0, 25, 50, 75, 100]
	for tg in info["targets"]:
		var tid := int(tg["target"])
		pick[tid] = int(tg["weight"])
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 0)
		var lab := UI.label("%s · %s " % [tg["name"], tg["what"]], 12, UI.INK)
		lab.custom_minimum_size = Vector2(330, 0)
		row.add_child(_center(lab))
		var btns := []
		var near: int = 0
		for x in steps:
			if abs(x - int(tg["weight"])) < abs(near - int(tg["weight"])): near = x
		for k in steps.size():
			var b := Button.new()
			b.text = str(steps[k])
			b.add_theme_font_size_override("font_size", 12)
			UI.segment(b, steps[k] == near, k == 0, k == steps.size() - 1)
			btns.append(b)
			row.add_child(b)
		for k in btns.size():
			var val: int = steps[k]
			var b2: Button = btns[k]
			b2.pressed.connect(func():
				pick[tid] = val
				for j in btns.size(): UI.segment(btns[j], j == k, j == 0, j == btns.size() - 1))
		org_box.add_child(row)
	var apply := UI.action("이대로 정한다", "", -1.0, false)
	apply.pressed.connect(func():
		var lines := []
		for t in pick.keys():
			if int(pick[t]) > 0: lines.append([int(t), float(pick[t])])
		host.SetProduction(id, int(st["product"]), lines)
		for line in host.DrainChronicle(): _log(line)
		_open_orgs())
	org_box.add_child(apply)
	_show_panel(org_panel)


## 시설: 가진 것(배정 바꾸기)과 지을 것.
func _open_facilities(id: int, name: String) -> void:
	var info: Dictionary = host.FacilityInfo(id)
	if info.is_empty():
		_open_orgs()
		return
	org_title.text = name + " · 시설"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	org_box.add_child(UI.label("쓸 수 있는 은화 %d(곳간과 우두머리 집의 남는 몫) · 짓는 곳 %d번 마을" % [int(float(info["money"])), int(info["village"])], 12, UI.MUTED))
	var have: Array = info["have"]
	if not have.is_empty():
		org_box.add_child(UI.label("가진 시설", 13, UI.MUTED, true))
		for f in have:
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 6)
			var lab := UI.label("%s · %s · %s · 배정 %s · 유지 %d/해\n%s" % [f["name"], f["village"], f["state"], f["assigned"] if f["assigned"] != "" else "없음", int(float(f["upkeep"])), f["effect"]], 12, UI.INK)
			lab.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			row.add_child(lab)
			var fid := int(f["id"])
			var mb := MenuButton.new()
			mb.text = "배정"
			var units: Array = info["units"]
			for k in units.size(): mb.get_popup().add_item(units[k]["name"], k)
			mb.get_popup().add_item("배정 풀기", units.size())
			mb.get_popup().id_pressed.connect(func(k):
				host.AssignFacility(id, fid, int(units[k]["id"]) if k < units.size() else -1)
				_open_facilities(id, name))
			row.add_child(_center(mb))
			org_box.add_child(row)
	org_box.add_child(UI.label("지을 것", 13, UI.MUTED, true))
	for b in info["build"]:
		var btn := UI.action("%s 짓기 · 은화 %d, %d년, 유지 %d/해" % [b["name"], int(float(b["cost"])), int(b["years"]), int(float(b["upkeep"]))], b["effect"], -1.0, false)
		btn.disabled = not b["can"]
		var kk := int(b["kind"])
		btn.pressed.connect(func():
			_set_hint(host.BuildFacility(id, kk))
			for line in host.DrainChronicle(): _log(line)
			_open_facilities(id, name))
		org_box.add_child(btn)
	_show_panel(org_panel)


## 임금: 갈래마다 직책별 단계를 고른다.
func _open_wages(id: int, name: String) -> void:
	var info: Dictionary = host.WageInfo(id)
	if info.is_empty():
		_open_orgs()
		return
	org_title.text = name + " · 임금"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	var from_leader: bool = info["fromLeader"]
	var src := UI.ghost("지급: 우두머리 집에서 (개인 충성)" if from_leader else "지급: 세력 곳간에서 (조직 충성)")
	src.pressed.connect(func():
		host.SetWageSource(id, not from_leader)
		_open_wages(id, name))
	org_box.add_child(src)
	var levels := ["없음", "적음", "보통", "많음", "초호화"]
	for u in info["units"]:
		org_box.add_child(UI.label(String(u["name"]) + (" (품삯 받는 군인: 지배자가 준다)" if u["paid"] else ""), 13, UI.MUTED, true))
		for r in u["roles"]:
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 6)
			var lab := UI.label("%s %d명 · 지금 %s · 보통이면 한 사람 은화 %.1f" % [r["name"], int(r["n"]), levels[int(r["level"])], float(r["base"])], 12, UI.INK)
			lab.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			row.add_child(lab)
			var mb := MenuButton.new()
			mb.text = "단계"
			for k in levels.size(): mb.get_popup().add_item(levels[k], k)
			var uid := int(u["id"])
			var rk := int(r["kind"])
			mb.get_popup().id_pressed.connect(func(k):
				host.SetWage(id, uid, rk, k)
				_open_wages(id, name))
			row.add_child(_center(mb))
			org_box.add_child(row)
	_show_panel(org_panel)


## 신앙: 종교와 교리(신분별 강도), 권장 헌금, 바치기.
var _found_picks: Array = []

func _open_faith(id: int, name: String) -> void:
	var info: Dictionary = host.FaithInfo(id)
	if info.is_empty():
		_open_orgs()
		return
	org_title.text = name + " · 신앙"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	org_box.add_child(UI.label("종교 %s%s · 이 마을에 알려진 정도 %.2f" % [info["religion"], " (내가 열었다)" if info["founder"] else "", float(info["known"])], 13, UI.INK, true))
	org_box.add_child(UI.label("신도 %d명 · 광신도 %d명 · 믿음 평균 %.2f · 곳간 은화 %d" % [int(info["believers"]), int(info["zealots"]), float(info["faith"]), int(float(info["treasury"]))], 12, UI.INK))
	var snames := ["면제", "약", "중", "강"]
	for dv in info["doctrines"]:
		org_box.add_child(UI.label("%s (%s) · %s" % [dv["name"], snames[int(dv["strength"])], dv["effect"]], 12, UI.INK))
		if info["founder"]:
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 4)
			for rk in dv["ranks"]:
				var mb := MenuButton.new()
				mb.add_theme_color_override("font_color", UI.INK)
				var cur := int(rk["strength"])
				mb.text = "%s: %s" % [rk["name"], "기본" if cur < 0 else snames[cur]]
				mb.get_popup().add_item("기본", 0)
				for k in 4: mb.get_popup().add_item(snames[k], k + 1)
				var rank := int(rk["rank"])
				var dd := int(dv["d"])
				mb.get_popup().id_pressed.connect(func(k):
					if not host.SetRankStrength(id, rank, dd, k - 1): _set_hint("반대 교리가 이미 세게 걸려 있다")
					_open_faith(id, name))
				row.add_child(mb)
			org_box.add_child(row)
			var sb := MenuButton.new()
			sb.add_theme_color_override("font_color", UI.INK)
			sb.text = "기본 강도·바꾸기" + ("" if info["canRevise"] else " (교리 바꾸기는 10년에 한 번)")
			for k in 4: sb.get_popup().add_item("강도 " + snames[k], k)
			if info["canRevise"]:
				for a in info["all"]: sb.get_popup().add_item("→ " + String(a["name"]), 100 + int(a["d"]))
			var slot := int(dv["slot"])
			var d0 := int(dv["d"])
			var s0 := int(dv["strength"])
			sb.get_popup().id_pressed.connect(func(k):
				if k < 100: host.ReviseDoctrine(id, slot, d0, k)
				elif not host.ReviseDoctrine(id, slot, k - 100, s0): _set_hint("바꿀 수 없다")
				_open_faith(id, name))
			org_box.add_child(sb)
	if not info["founder"]:
		org_box.add_child(UI.label("다른 종교로 입교하거나, 새 종교를 연다(새 종교는 알려지지 않아 선교가 어렵다)", 12, UI.MUTED))
		for r in info["religions"]:
			var rid := int(r["id"])
			var jb := UI.action("%s 입교 · 알려진 정도 %.2f" % [r["name"], float(r["known"])], String(r["doctrines"]), -1.0, false)
			jb.pressed.connect(func():
				host.JoinReligion(id, rid)
				_open_faith(id, name))
			org_box.add_child(jb)
		var pick_row := HBoxContainer.new()
		var pmb := MenuButton.new()
		pmb.add_theme_color_override("font_color", UI.INK)
		pmb.text = "교리 고르기 (%d/3)" % _found_picks.size()
		for a in info["all"]:
			for k in [1, 2, 3]: pmb.get_popup().add_item("%s %s" % [a["name"], snames[k]], int(a["d"]) * 10 + k)
		pmb.get_popup().id_pressed.connect(func(k):
			if _found_picks.size() < 3: _found_picks.append([int(k / 10), k % 10])
			_open_faith(id, name))
		pick_row.add_child(pmb)
		var picked := []
		for pk in _found_picks: picked.append("%s %s" % [info["all"][int(pk[0])]["name"], snames[int(pk[1])]])
		pick_row.add_child(UI.label(", ".join(picked), 12, UI.INK))
		org_box.add_child(pick_row)
		var fb := UI.action("새 종교를 연다", "고른 교리로 연다. 신도의 믿음은 절반이 된다", -1.0, true)
		fb.disabled = _found_picks.is_empty()
		fb.pressed.connect(func():
			_set_hint(host.FoundReligion(id, "", _found_picks))
			_found_picks = []
			for line in host.DrainChronicle(): _log(line)
			_open_faith(id, name))
		org_box.add_child(fb)
	org_box.add_child(UI.label("권장 헌금: " + String(info["titheName"]) + " (내는 것은 신도 마음. 믿음·충성·봉헌 교리만큼 낸다)", 13, UI.MUTED, true))
	var names := ["형편껏", "십일조", "다섯에 하나"]
	var tips := ["넉넉한 신도 집만 재산의 2%(10까지)", "소출의 1/10. 부담만큼 낼 확률이 준다", "소출의 1/5. 부담이 커서 믿음 깊은 신도와 광신도가 주로 낸다"]
	for k in 3:
		var tb := UI.action(names[k] + (" (지금)" if int(info["tithe"]) == k else ""), tips[k], -1.0, false)
		var kk := k
		tb.pressed.connect(func():
			host.SetTithe(id, kk)
			_open_faith(id, name))
		org_box.add_child(tb)
	var ob := UI.action("바치기를 청한다", "세 해에 한 번. 광신도는 재산의 0.3, 믿음 깊은 신도는 0.1을 낸다. 나머지는 조금 내고 믿음이 조금 깎인다", -1.0, false)
	ob.disabled = not info["canOffer"]
	ob.pressed.connect(func():
		_set_hint(host.CallOffering(id))
		for line in host.DrainChronicle(): _log(line)
		_open_faith(id, name))
	org_box.add_child(ob)
	_show_panel(org_panel)


## 예산과 파벌: 갈래마다 몫의 비중을 정하고, 보이는 파벌에 수를 쓴다.
func _open_budget(id: int, name: String) -> void:
	var info: Dictionary = host.BudgetInfo(id)
	if info.is_empty():
		_open_orgs()
		return
	org_title.text = name + " · 예산"
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	org_box.add_child(UI.label("곳간 은화 %d · 필요는 녹봉과 상근 군량. 비중은 필요에 곱한다" % int(float(info["treasury"])), 12, UI.MUTED))
	for u in info["units"]:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 6)
		var cmd: String = u["cmd"]
		var lab := UI.label("%s · 필요 %d, 받음 %d(%d%%) · 비중 %.2f%s%s" % [u["name"], int(float(u["need"])), int(float(u["alloc"])), int(100.0 * float(u["fill"])), float(u["weight"]),
			(" · 지휘 " + cmd) if cmd != "" else "", (" · " + u["mood"]) if u["mood"] != "" else ""], 12, UI.INK)
		lab.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(lab)
		var uid := int(u["id"])
		var mb := MenuButton.new()
		mb.text = "비중"
		var ws := [0.5, 1.0, 1.5, 2.0, 3.0]
		for k in ws.size(): mb.get_popup().add_item("%.1f" % ws[k], k)
		mb.get_popup().id_pressed.connect(func(k):
			host.SetBudgetWeight(id, uid, ws[k])
			_open_budget(id, name))
		row.add_child(_center(mb))
		org_box.add_child(row)
	org_box.add_child(UI.label("파벌", 13, UI.MUTED, true))
	if not info["eye"]:
		org_box.add_child(UI.label("감시하는 갈래가 없어 파벌이 보이지 않는다. 불평만 들린다", 12, UI.MUTED))
	elif (info["factions"] as Array).is_empty():
		org_box.add_child(UI.label("뭉친 파벌이 없다", 12, UI.MUTED))
	for f in info["factions"]:
		org_box.add_child(UI.label("힘 %d(세력의 %d%%) · 불만 %.2f" % [int(float(f["power"])), int(100.0 * float(f["share"])), float(f["grievance"])], 12, UI.INK))
		for m in f["members"]:
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 6)
			var lab := UI.label("  %s%s · %s" % [m["name"], " (장)" if m["lead"] else "", m["mood"]], 12, UI.INK)
			lab.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			row.add_child(lab)
			var muid := int(m["uid"])
			var pb := UI.ghost("자리 주기")
			pb.tooltip_text = "측근 자리를 주어 파벌에서 떼어 낸다(불만 −0.3)"
			pb.pressed.connect(func():
				host.FactionMove(id, muid, 0)
				_open_budget(id, name))
			row.add_child(_center(pb))
			var xb := UI.ghost("내쫓기")
			xb.tooltip_text = "세력에서 내쫓는다. 같은 파벌 사람의 불만이 오른다"
			xb.pressed.connect(func():
				host.FactionMove(id, muid, 1)
				for line in host.DrainChronicle(): _log(line)
				_open_budget(id, name))
			row.add_child(_center(xb))
			org_box.add_child(row)
	_show_panel(org_panel)


## 직책에 앉히기: role 0 지휘자, 1 곳간지기, 2 연락책.
func _open_appoint(id: int, role: int, title: String) -> void:
	org_title.text = title
	for c in org_box.get_children(): c.queue_free()
	var back := UI.ghost("← 조직으로")
	back.pressed.connect(_open_orgs)
	org_box.add_child(back)
	var tips := ["지휘 범위(말솜씨·무예·글)가 크면 많이 부린다. 오래 맡을수록 장악도가 올라, 거사 때 갈래가 그를 따른다", "글이 높을수록 번 것이 덜 샌다", "말솜씨와 눈치가 높을수록 새어 나감을 막는다"]
	var tl := UI.label(tips[role], 12, UI.MUTED)
	tl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	tl.custom_minimum_size = Vector2(600, 0)
	org_box.add_child(tl)
	var none := UI.ghost("비운다")
	none.pressed.connect(func():
		host.Appoint(id, role, -1)
		for line in host.DrainChronicle(): _log(line)
		_open_orgs())
	org_box.add_child(none)
	for e in host.OrgPeople(id):
		var b := UI.action(e["name"], e["note"], -1.0, false)
		var who := int(e["i"])
		b.pressed.connect(func():
			host.Appoint(id, role, who)
			for line in host.DrainChronicle(): _log(line)
			_open_orgs())
		org_box.add_child(b)
	_show_panel(org_panel)


## '거사에 끌어들이기' 상호작용 번호(SimHost 가 이름으로 알려 준다).
func _plot_recruit_id() -> int:
	return int(host.PlotRecruitId)


## 가운데 창은 한 번에 하나만 띄운다.
func _show_panel(p: PanelContainer) -> void:
	for q in [tree_panel, org_panel, show_panel, rise_panel, judge_panel]:
		if q != p: q.visible = false
	p.visible = true
	_refresh_judge_button()


## Esc: 띄운 가운데 창을 닫고, 없으면 카드를 닫는다.
func _close_top() -> void:
	for q in [tree_panel, org_panel, show_panel, rise_panel, judge_panel]:
		if q.visible:
			q.visible = false
			return
	if not pending.is_empty():
		_cancel()
		return
	if card.visible:
		card.visible = false
		scene.ring.visible = false
		shown = {}


func _center(c: Control) -> Control:
	c.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	return c


func _pick_member(i: int) -> void:
	if int(_age_of(i)) >= 12 and actor != i:
		actor = i
		pending = {}
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


## 권위(돈, 따르는 집, 싸움 힘, 충성을 합친 값)를 말로.
func _authority_word(a: float) -> String:
	if a < 10: return "보잘것없음"
	if a < 40: return "작은 세력"
	if a < 120: return "알아주는 세력"
	if a < 300: return "큰 세력"
	return "누구나 아는 세력"


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
			KEY_ESCAPE: _close_top()


func _physics_process(dt: float) -> void:
	if get_viewport().gui_get_focus_owner() is LineEdit: return   # 이름을 치는 동안은 화면을 움직이지 않는다
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
var stat_coin: VBoxContainer
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
	var ob := UI.ghost("조직")
	ob.tooltip_text = "내 집의 군대와 조직, 그 갈래"
	ob.pressed.connect(_open_orgs)
	tr.add_child(_center(ob))
	judge_button = UI.ghost("재판")
	judge_button.tooltip_text = "내 땅에서 잡은 범인의 판결과 벌의 세기"
	judge_button.pressed.connect(_open_judge)
	tr.add_child(_center(judge_button))
	show_button = UI.ghost("둘러보기")
	show_button.tooltip_text = "볼 만한 집 목록(지배자, 봉신, 조직 우두머리 들)"
	show_button.pressed.connect(_open_showcase)
	show_button.visible = false
	tr.add_child(_center(show_button))
	rise_button = UI.ghost("다툼")
	rise_button.tooltip_text = "세력 다툼: 받은 요구, 내가 눈여겨보는 세력, 나를 눈여겨보는 지배자"
	rise_button.pressed.connect(_open_rise)
	tr.add_child(_center(rise_button))
	tr.add_child(_vsep())
	stat_family = UI.stat("가문", "맡은 집 없음")
	stat_kin = UI.stat("성이 같은 사람", "-")
	stat_land = UI.stat("땅", "-")
	stat_food = UI.stat("식량", "-")
	stat_coin = UI.stat("은화", "-")
	stat_coin.tooltip_text = "은화와 마을 식량 값(은화로 식량 한 단위)"
	for st in [stat_family, stat_kin, stat_land, stat_food, stat_coin]: tr.add_child(st)
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
	lv.add_child(HSeparator.new())
	var kh := HBoxContainer.new()
	kin_title = UI.label("혈족 집", 13, UI.MUTED, true)
	kin_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	kh.add_child(kin_title)
	var tb := UI.ghost("족보")
	tb.pressed.connect(_open_tree)
	kh.add_child(tb)
	lv.add_child(kh)
	kin_box = VBoxContainer.new()
	kin_box.add_theme_constant_override("separation", 4)
	lv.add_child(kin_box)
	rootc.add_child(left)

	# 족보 창
	tree_panel = UI.panel()
	tree_panel.set_anchors_preset(Control.PRESET_CENTER)
	tree_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	tree_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	tree_panel.custom_minimum_size = Vector2(560, 0)
	var tv := VBoxContainer.new()
	tv.add_theme_constant_override("separation", 6)
	tree_panel.add_child(tv)
	var th := HBoxContainer.new()
	var tt := UI.label("족보", 20, UI.INK, true)
	tt.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	th.add_child(tt)
	range_button = UI.ghost("범위 4촌")
	range_button.pressed.connect(func():
		host.KinRange = 6 if int(host.KinRange) == 4 else 4
		_refresh_family()
		_open_tree())
	th.add_child(range_button)
	var tc := UI.ghost("닫기")
	tc.pressed.connect(func(): tree_panel.visible = false)
	th.add_child(tc)
	tv.add_child(th)
	tv.add_child(UI.label("지금 맡은 가장에서 혈족 범위 안의 사람들. 맡을 수 있는 집은 다음 계절부터 옮겨 맡는다.", 12, UI.MUTED))
	var sc := ScrollContainer.new()
	sc.custom_minimum_size = Vector2(540, 520)
	sc.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	tree_box = VBoxContainer.new()
	tree_box.add_theme_constant_override("separation", 3)
	tree_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	sc.add_child(tree_box)
	tv.add_child(sc)
	tree_panel.visible = false
	rootc.add_child(tree_panel)

	# 조직 창
	org_panel = UI.panel()
	org_panel.set_anchors_preset(Control.PRESET_CENTER)
	org_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	org_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	org_panel.custom_minimum_size = Vector2(640, 0)
	var ov := VBoxContainer.new()
	ov.add_theme_constant_override("separation", 6)
	org_panel.add_child(ov)
	var oh := HBoxContainer.new()
	org_title = UI.label("조직", 20, UI.INK, true)
	org_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	oh.add_child(org_title)
	var oc := UI.ghost("닫기")
	oc.pressed.connect(func(): org_panel.visible = false)
	oh.add_child(oc)
	ov.add_child(oh)
	ov.add_child(UI.label("갈래마다 우두머리가 바로 부리는 사람과 맡은 일. 일을 정한 갈래는 우두머리가 바꾸지 않는다.", 12, UI.MUTED))
	var osc := ScrollContainer.new()
	osc.custom_minimum_size = Vector2(620, 520)
	osc.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	org_box = VBoxContainer.new()
	org_box.add_theme_constant_override("separation", 4)
	org_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	osc.add_child(org_box)
	ov.add_child(osc)
	org_panel.visible = false
	rootc.add_child(org_panel)

	# 재판 창
	judge_panel = UI.panel()
	judge_panel.set_anchors_preset(Control.PRESET_CENTER)
	judge_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	judge_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	judge_panel.custom_minimum_size = Vector2(640, 0)
	var jv := VBoxContainer.new()
	jv.add_theme_constant_override("separation", 6)
	judge_panel.add_child(jv)
	var jh := HBoxContainer.new()
	var jt := UI.label("재판", 20, UI.INK, true)
	jt.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	jh.add_child(jt)
	var jc := UI.ghost("닫기")
	jc.pressed.connect(func(): judge_panel.visible = false)
	jh.add_child(jc)
	jv.add_child(jh)
	var jsc := ScrollContainer.new()
	jsc.custom_minimum_size = Vector2(620, 460)
	jsc.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	judge_box = VBoxContainer.new()
	judge_box.add_theme_constant_override("separation", 6)
	judge_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	jsc.add_child(judge_box)
	jv.add_child(jsc)
	judge_panel.visible = false
	rootc.add_child(judge_panel)

	# 다툼 창
	rise_panel = UI.panel()
	rise_panel.set_anchors_preset(Control.PRESET_CENTER)
	rise_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	rise_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	rise_panel.custom_minimum_size = Vector2(660, 0)
	var rv := VBoxContainer.new()
	rv.add_theme_constant_override("separation", 6)
	rise_panel.add_child(rv)
	var rh := HBoxContainer.new()
	var rt := UI.label("세력 다툼", 20, UI.INK, true)
	rt.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	rh.add_child(rt)
	var rc := UI.ghost("닫기")
	rc.pressed.connect(func(): rise_panel.visible = false)
	rh.add_child(rc)
	rv.add_child(rh)
	var rsc := ScrollContainer.new()
	rsc.custom_minimum_size = Vector2(640, 500)
	rsc.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	rise_box = VBoxContainer.new()
	rise_box.add_theme_constant_override("separation", 6)
	rise_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	rsc.add_child(rise_box)
	rv.add_child(rsc)
	rise_panel.visible = false
	rootc.add_child(rise_panel)

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

	# 둘러보기 창(검토용)
	show_panel = UI.panel()
	show_panel.set_anchors_preset(Control.PRESET_CENTER)
	show_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	show_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	show_panel.custom_minimum_size = Vector2(660, 0)
	var shv := VBoxContainer.new()
	shv.add_theme_constant_override("separation", 6)
	show_panel.add_child(shv)
	var shh := HBoxContainer.new()
	var sht := UI.label("둘러보기", 20, UI.INK, true)
	sht.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	shh.add_child(sht)
	var shc := UI.ghost("닫기")
	shc.pressed.connect(func(): show_panel.visible = false)
	shh.add_child(shc)
	shv.add_child(shh)
	shv.add_child(UI.label("맡으면 그 집의 가족이 된다. 다른 집으로 옮기려면 여기서 다시 고른다.", 12, UI.MUTED))
	show_box = VBoxContainer.new()
	show_box.add_theme_constant_override("separation", 4)
	show_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var shsc := ScrollContainer.new()
	shsc.custom_minimum_size = Vector2(640, 520)
	shsc.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	shsc.add_child(show_box)
	shv.add_child(shsc)
	show_panel.visible = false
	rootc.add_child(show_panel)

	# 시작: 세계 고르기
	start_panel = UI.panel()
	start_panel.set_anchors_preset(Control.PRESET_CENTER)
	start_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	start_panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	var sv := VBoxContainer.new()
	sv.add_theme_constant_override("separation", 10)
	start_panel.add_child(sv)
	sv.add_child(UI.label("어떤 세계로 시작할까", 20, UI.INK, true))
	for o in [["장원 세계 · 작게", "군주 셋, 마을 스물넷. 영주와 봉신, 소작과 지주가 갖춰진 세계에서 처지를 골라 시작", 2],
			["장원 세계 · 크게", "군주 다섯, 마을 쉰. 만드는 데 조금 더 걸린다", 3],
			["개척", "작은 무리가 빈 땅에 자리 잡은 뒤(150년). 지배자가 아직 드물다", 0],
			["오래 돌린 세계", "개척에서 220년을 돌린 검토용 세계", 1]]:
		var b := UI.action(o[0], o[1], -1.0, false)
		b.custom_minimum_size = Vector2(460, 56)
		var m: int = o[2]
		b.pressed.connect(func(): _start(m))
		sv.add_child(b)
	start_panel.visible = false
	rootc.add_child(start_panel)

	# 처음 세계를 만드는 동안
	boot_panel = UI.panel()
	boot_panel.set_anchors_preset(Control.PRESET_CENTER)
	boot_label = UI.label("세계를 만드는 중…", 20, UI.INK, true)
	boot_panel.add_child(boot_label)
	boot_panel.visible = false
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


## 알림 띠. 오래된 알림은 30초 뒤 저절로 지운다(sticky 는 남긴다).
func _set_hint(t: String, sticky := false) -> void:
	hint_label.text = t
	notice.visible = t != ""
	hint_age = 0.0
	hint_sticky = sticky


# ------------------------------------------------------------------ 자동 점검

func _run_autotest() -> void:
	# 영역 안에서 식구가 가장 많은 집을 맡고, 반년 돌린 뒤 화면을 찍는다
	var best := -1
	var most := -1
	for k in data["hinfo"].size():
		if int(data["houses"][k][2]) == 0: continue
		var n: int = 10 * int(host.KinCountIf(int(data["hinfo"][k]["id"]))) + scene.members_of(k).size()
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
	# 모임: 내 카드에서 자경단을 세우고, 이웃 몇을 끌어들인다
	var me_pd := _pd_of(actor)
	if not me_pd.is_empty():
		_show_person(me_pd)
		await _snap("live_self.png")
		_do_self("found:0")
		var recruited := 0
		for e in scene.pickables:
			var pd: Dictionary = e["pd"]
			if mine.has(int(pd["i"])) or int(pd["age"]) < 16: continue
			for t in 3:
				var r: String = host.Interact(_id_of("조직에 들이기", int(pd["i"])), actor, int(pd["i"]), -1, 0.0) if _id_of("조직에 들이기", int(pd["i"])) >= 0 else ""
				if r.ends_with("받아들였다"): recruited += 1; break
			if recruited >= 3: break
		_redraw()
		print("autotest: 자경단에 들인 이웃 %d명" % recruited)
		_show_person(_pd_of(actor))
		await _snap("live_groups.png")
		# 조직 창: 갈래 나무와 일 정하기
		card.visible = false
		_open_orgs()
		await _snap("live_orgs.png")
		var orgs: Array = host.OrgPanel().filter(func(o): return o["command"] and int(o["depth"]) == 0)
		print("autotest: 내가 부리는 조직 %d개" % orgs.size())
		if orgs.size() > 0 and orgs[0]["command"]:
			_open_org_tasks(int(orgs[0]["id"]), orgs[0]["name"])
			await _snap("live_org_tasks.png")
			var tt: Array = host.OrgTaskTargets(int(orgs[0]["id"]))
			if tt.size() > 1:
				host.SetOrgTask(int(orgs[0]["id"]), int(tt[1]["verb"]), int(tt[1]["village"]))
				_open_orgs()
				print("autotest: 일 정함 ", host.OrgPanel().filter(func(o): return int(o["id"]) == int(orgs[0]["id"]))[0]["tasks"])
		if orgs.size() > 0 and orgs[0]["command"]:
			_open_org_name(int(orgs[0]["id"]), orgs[0]["name"], false)
			await _snap("live_org_name.png")
			host.RenameOrg(int(orgs[0]["id"]), "북쪽 개울 형제단")
			_open_orgs()
			await _snap("live_orgs_named.png")
			print("autotest: 이름 지음 ", host.OrgPanel().filter(func(o): return int(o["id"]) == int(orgs[0]["id"]))[0]["name"])
			host.SetOrgVisibility(int(orgs[0]["id"]), 1)
			_open_orgs()
			await _snap("live_orgs_vis.png")
			var me_org: Dictionary = host.OrgPanel().filter(func(o): return int(o["id"]) == int(orgs[0]["id"]))[0]
			print("autotest: 알려진 정도 ", me_org["visibilityName"], " · 권위 ", me_org["authority"])
			host.SetOrgRules(int(orgs[0]["id"]), 0, 2)
			_open_org_lessons(int(orgs[0]["id"]), orgs[0]["name"])
			await _snap("live_org_lessons.png")
			host.SetUnitLesson(int(orgs[0]["id"]), 1, 20)
			_open_orgs()
			await _snap("live_orgs_rules.png")
			# 일 비중과 직책
			var oid := int(orgs[0]["id"])
			_open_org_weights(oid, orgs[0]["name"])
			await _snap("live_org_weights.png")
			host.SetOrgWeights(oid, [[0, 60.0], [16, 40.0]])
			_open_appoint(oid, 1, "곳간지기")
			await _snap("live_org_appoint.png")
			var ppl: Array = host.OrgPeople(oid)
			if ppl.size() > 0: host.Appoint(oid, 1, int(ppl[0]["i"]))
			_open_orgs()
			await _snap("live_orgs_roles.png")
			var o2: Dictionary = host.OrgPanel().filter(func(o): return int(o["id"]) == oid)[0]
			print("autotest: 일 비중 ", o2["tasks"], " · 곳간지기 ", o2["treasurer"])
			_open_facilities(oid, orgs[0]["name"])
			await _snap("live_facilities.png")
			print("autotest: 지을 수 있는 시설 ", host.FacilityInfo(oid)["build"].filter(func(b): return b["can"]).size())

		org_panel.visible = false
	# 재판: 주군이 없는 집이 당한 사건은 마을 자경단 우두머리가 맡는다. 방금 세운 자경단의 집으로 모든 사건을 직접 판결한다
	host.JudgeAll = true
	# 이웃끼리 훔치게 해 사건을 만든다(신고와 수사는 확률이라 여러 번)
	var folk := []
	for e in scene.pickables:
		var pd: Dictionary = e["pd"]
		if not mine.has(int(pd["i"])) and int(pd["age"]) >= 16: folk.append(int(pd["i"]))
	for k in 400:
		if int(host.PendingCount) > 0 or folk.size() < 2: break
		var a: int = folk[k % folk.size()]
		var b: int = folk[(k * 7 + 3) % folk.size()]
		if a != b:
			var sid := -1
			for it in host.Interactions(a, b):
				if it["name"] == "훔치기": sid = int(it["id"])
			if sid >= 0: host.Interact(sid, a, b, -1, 0.0)
	_on_player_changed()
	_redraw()
	_refresh_judge_button()
	card.visible = false
	_open_judge()
	await _snap("live_judge.png")
	var cs: Array = host.Cases()
	print("autotest: 판결 기다리는 사건 %d · %s · 최근 사건 %d" % [cs.size(), host.DateText, host.RecentCases().size()])
	if cs.size() > 0:
		host.JudgeCase(int(cs[0]["id"]), 3)
		print("autotest: 판결 뒤 %d · %s" % [int(host.PendingCount), host.RecentCases()[0]])
	judge_panel.visible = false
	host.JudgeAll = false
	# 혈족 집: 족보 창을 열어 찍고, 첫 혈족 집으로 옮기기를 예약해 계절이 바뀌면 옮겨지는지 본다
	card.visible = false
	_refresh_family()
	_open_tree()
	await _snap("live_kin_tree.png")
	tree_panel.visible = false
	var kin: Array = host.KinHouses()
	print("autotest: 혈족 집 %d곳" % kin.size())
	if kin.size() > 0:
		var to := int(kin[0]["id"])
		_switch_to(to)
		await _snap("live_kin.png")
		var before: int = host.Player
		host.Advance(6)
		_on_player_changed()
		_redraw()
		print("autotest: 옮기기 %d → %d (예약 %d, 촌수 %d)" % [before, int(host.Player), to, int(kin[0]["chon"])])
	# 범죄: 이웃 카드의 범죄 묶음을 찍고, 한 번 훔쳐 본다
	_refresh_family()
	var mine2 := _members().map(func(m): return int(m["i"]))
	for m in _members():
		if m["head"]: actor = int(m["i"])
	for e in scene.pickables:
		var pd: Dictionary = e["pd"]
		if mine2.has(int(pd["i"])) or int(pd["age"]) < 16: continue
		var sid := _id_of("훔치기", int(pd["i"]))
		if sid < 0: continue
		_show_person(pd)
		scene.aim(e["node"].global_position, 60.0)
		for f in 4: await get_tree().process_frame
		card.scroll_end()
		await _snap("live_crime.png")
		print("autotest: 훔치기 ", host.Interact(sid, actor, int(pd["i"]), -1, 0.0))
		break
	# 집 카드: 재화와 마을 값
	_redraw()
	var hi_me: int = scene.house_index(int(host.Player))
	if hi_me >= 0:
		_show_house(hi_me)
		await _snap("live_house_goods.png")
		print("autotest: 집 재화 ", data["hinfo"][hi_me].get("goods", []), " · 마을 값 ", data["hinfo"][hi_me].get("prices", []))
	# 다툼: 지배자가 있는 큰 세계를 새로 띄워, 의심받는 집을 맡고 요구가 올 때까지 돌린다
	host.Boot(2, 200, 10)
	while not host.Ready: await get_tree().create_timer(0.5).timeout
	var riser: int = host.TopRiser()
	var asked := false
	for k in 200:
		if riser < 0:
			host.Advance(24)
			riser = host.TopRiser()
			continue
		if int(host.Player) != riser:
			host.SetPlayer(riser)
		host.Advance(24)
		if int(host.DemandCount) > 0:
			asked = true
			break
		riser = host.TopRiser()
	print("autotest: 다툼 상대 줄 %d · 요구 %d · %s" % [host.RiseRows().size(), int(host.DemandCount), host.DateText])
	var sc: Array = host.Showcase()
	print("autotest: 볼 만한 집 %d곳: %s" % [sc.size(), ", ".join(sc.map(func(x): return x["what"]))])
	card.visible = false
	_open_showcase()
	await _snap("live_showcase.png")
	show_panel.visible = false
	var pos: Vector2i = host.HousePos(int(host.Player))
	if pos.x >= 0: _recenter(pos)
	_refresh_family()
	_refresh_judge_button()
	card.visible = false
	_open_rise()
	await _snap("live_rise.png")
	if asked:
		var ds: Array = host.Demands()
		host.AnswerDemand(int(ds[0]["id"]), false)
		print("autotest: 요구 거절 → 남은 요구 %d, 줄 %d" % [int(host.DemandCount), host.RiseRows().size()])
	rise_panel.visible = false
	# 거사: 봉신 영주를 맡아 주군의 자리를 노린다
	for e in sc:
		if e["what"] != "봉신 영주": continue
		_take_house(int(e["id"]))
		_on_player_changed()
		for m in _members(): if m["head"]: actor = int(m["i"])
		var liege: int = host.LiegeHeadOf(int(e["id"]))
		var names := []
		for it in host.Interactions(actor, liege): names.append(it["name"])
		print("autotest: 주군에게 할 수 있는 일 ", ", ".join(names))
		var pid := _id_of("거사 꾸미기", liege)
		if pid >= 0:
			print("autotest: ", host.Interact(pid, actor, liege, -1, 0.0))
			var pl: Dictionary = host.PlotInfo()
			print("autotest: 거사 힘 %.2f, 끌어들일 사람 %d" % [float(pl.get("ratio", 0.0)), pl.get("candidates", []).size()])
		_set_speed(0)
		card.visible = false
		_open_rise()
		await _snap("live_plot.png")
		rise_panel.visible = false
		break
	# 장원 세계: 만들고 처지 목록을 본다
	host.BootGenerated(3, 3, 8, 10)
	while not host.Ready: await get_tree().create_timer(0.5).timeout
	var gsc: Array = host.Showcase()
	print("autotest: 장원 세계 처지 %d곳: %s" % [gsc.size(), ", ".join(gsc.map(func(x): return x["what"]))])
	_recenter(host.BiggestVillage())
	card.visible = false
	_open_showcase()
	await _snap("live_gen_showcase.png")
	show_panel.visible = false
	for e in gsc:
		if e["what"] == "소작농 가족":
			_take_house(int(e["id"]))
			_on_player_changed()
			_recenter(host.HousePos(int(e["id"])))
			_set_speed(0)
			await _snap("live_gen_tenant.png")
			_open_rise()
			await _snap("live_gen_progress.png")
			rise_panel.visible = false
			break
	# 자리 없는 배운 사람이 학교를 세운다
	for e in gsc:
		if e["what"] == "자리 없는 배운 사람의 집":
			_take_house(int(e["id"]))
			_on_player_changed()
			_set_speed(0)
			while not host.Ready: await get_tree().create_timer(0.2).timeout
			print("autotest: ", host.DoSelf(actor, "found:7"))
			var sorgs: Array = host.OrgPanel().filter(func(o): return o["command"] and int(o["depth"]) == 0 and o["school"])
			if sorgs.size() > 0:
				_open_school(int(sorgs[0]["id"]), sorgs[0]["name"])
				await _snap("live_school.png")
				org_panel.visible = false
				print("autotest: 학교 설정 ", host.SchoolInfo(int(sorgs[0]["id"])).get("years", -1), "년")
				print("autotest: ", host.DoSelf(actor, "found:2"))
				var bro: Array = host.OrgPanel().filter(func(o): return o["command"] and int(o["depth"]) == 0 and o.get("faith", false))
				print("autotest: 신앙 조직 ", bro.size())
				if bro.size() > 0:
					print("autotest: ", host.FoundReligion(int(bro[0]["id"]), "", [[12, 3], [0, 2], [15, 1]]))
					_open_faith(int(bro[0]["id"]), bro[0]["name"])
					await _snap("live_faith.png")
					org_panel.visible = false
				_open_wages(int(sorgs[0]["id"]), sorgs[0]["name"])
				await _snap("live_wages.png")
				org_panel.visible = false
				print("autotest: 임금 갈래 ", (host.WageInfo(int(sorgs[0]["id"])).get("units", []) as Array).size())
				_open_budget(int(sorgs[0]["id"]), sorgs[0]["name"])
				await _snap("live_budget.png")
				org_panel.visible = false
				print("autotest: 예산 갈래 ", (host.BudgetInfo(int(sorgs[0]["id"])).get("units", []) as Array).size())
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
