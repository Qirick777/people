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
var kin_box: VBoxContainer
var kin_title: Label
var tree_panel: PanelContainer
var tree_box: VBoxContainer
var range_button: Button
var org_panel: PanelContainer
var org_box: VBoxContainer
var org_title: Label
var judge_button: Button
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
				if int(host.PendingCount) > 0:
					_set_hint("범인을 잡았다. 재판 창에서 판결한다.")
					_open_judge()
				else:
					_set_hint("집에 큰일이 생겨 멈췄다. 연대기를 보고 다시 시작한다.")
			_refresh_judge_button()
			since_draw += days
			if int(host.Player) != last_player and last_player >= 0:
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
	if actor >= 0 and actor == target:
		var own := []
		for it in host.SelfActions(actor):
			var key: String = it["key"]
			own.append({"text": it["name"], "note": it["note"], "risky": it["risky"], "cb": func(): _do_self(key)})
		card.add_actions("모임 세우기·나가기", own)
		return
	if actor < 0: return
	if not pending.is_empty() and int(pending["target"]) == target:
		_show_choices(target)
		return
	pending = {}
	var items := []
	var crimes := []
	for it in host.Interactions(actor, target):
		var id := int(it["id"])
		if it.has("crime"):
			var cn := []
			if it.has("take"): cn.append("식량 %d" % int(float(it["take"])))
			cn.append("잡힐 확률 %d%%" % int(round(float(it["caught"]) * 100.0)))
			cn.append("벌: " + it["punish"])
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
	if not crimes.is_empty(): card.add_actions("범죄", crimes, "오른쪽 숫자는 해낼 확률. 잡힐 확률은 들켜서 신고되고 붙잡힐 확률이다")


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
		inner.add_child(_center(UI.label("%d촌 · %s · %d명" % [int(e["chon"]), e["label"], int(e["members"])], 11, UI.MUTED)))
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
	tree_panel.visible = true


func _open_orgs() -> void:
	org_title.text = "조직"
	for c in org_box.get_children(): c.queue_free()
	var rows: Array = host.OrgPanel()
	if rows.is_empty():
		org_box.add_child(UI.label("내 집과 이어진 조직이 없다. 식구를 골라 자기 자신을 누르면 모임을 세울 수 있다.", 13, UI.MUTED))
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
		tv.add_child(UI.label(e["name"], 14, UI.INK, depth == 0))
		var info := "%s · 바로 %d명" % [e["leader"], int(e["members"])]
		if int(e["total"]) != int(e["members"]): info += " · 모두 %d명" % int(e["total"])
		info += " · 힘 %.0f" % float(e["strength"])
		tv.add_child(UI.label(info, 12, UI.MUTED))
		tv.add_child(UI.label(("정한 일: " if e["manual"] else "맡은 일: ") + e["tasks"], 12, UI.INK))
		r.add_child(tv)
		if e["command"]:
			var id := int(e["id"])
			if e["canSplit"]:
				var sb := UI.ghost("갈래 나누기")
				sb.pressed.connect(func():
					host.SplitOrg(id)
					_open_orgs())
				r.add_child(_center(sb))
			var nb := UI.ghost("이름 짓기")
			nb.pressed.connect(func(): _open_org_name(id, e["name"], e["named"]))
			r.add_child(_center(nb))
			var tb := UI.ghost("일 정하기")
			tb.pressed.connect(func(): _open_org_tasks(id, e["name"]))
			r.add_child(_center(tb))
			if e["manual"]:
				var ab := UI.ghost("AI에게")
				ab.pressed.connect(func():
					host.OrgAuto(id)
					_open_orgs())
				r.add_child(_center(ab))
		org_box.add_child(r)
	org_panel.visible = true


func _refresh_judge_button() -> void:
	var n: int = host.PendingCount if host != null and host.Ready else 0
	judge_button.text = "재판 %d" % n if n > 0 else "재판"
	judge_button.add_theme_color_override("font_color", UI.RISK if n > 0 else UI.INK)


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
	var all := UI.ghost("모든 사건을 직접 판결" if host.JudgeAll else "혈족·신하·살인만 직접 판결")
	all.pressed.connect(func():
		host.JudgeAll = not host.JudgeAll
		_open_judge())
	judge_box.add_child(all)
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
		if e["mine"] != "": opts.append(["덮기", 6, true])
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
	judge_panel.visible = true


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
		var b := UI.action(t["verbName"] + " · " + t["place"], "%d집" % int(t["houses"]), -1.0, t["harsh"])
		var vv := int(t["village"])
		b.pressed.connect(func():
			host.SetOrgTask(id, verb, vv)
			for line in host.DrainChronicle(): _log(line)
			_open_orgs())
		org_box.add_child(b)


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
	ob.tooltip_text = "내 집의 군대와 모임, 그 갈래"
	ob.pressed.connect(_open_orgs)
	tr.add_child(_center(ob))
	judge_button = UI.ghost("재판")
	judge_button.tooltip_text = "내 땅에서 잡은 범인의 판결과 벌의 세기"
	judge_button.pressed.connect(_open_judge)
	tr.add_child(_center(judge_button))
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
	ov.add_child(UI.label("갈래마다 우두머리가 바로 부리는 사람과 맡은 일. 일을 정한 갈래는 AI 가 바꾸지 않는다.", 12, UI.MUTED))
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
				var r: String = host.Interact(_id_of("모임에 들이기", int(pd["i"])), actor, int(pd["i"]), -1, 0.0) if _id_of("모임에 들이기", int(pd["i"])) >= 0 else ""
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
		var orgs: Array = host.OrgPanel()
		print("autotest: 조직 줄 %d개" % orgs.size())
		if orgs.size() > 0 and orgs[0]["command"]:
			_open_org_tasks(int(orgs[0]["id"]), orgs[0]["name"])
			await _snap("live_org_tasks.png")
			var tt: Array = host.OrgTaskTargets(int(orgs[0]["id"]))
			if tt.size() > 1:
				host.SetOrgTask(int(orgs[0]["id"]), int(tt[1]["verb"]), int(tt[1]["village"]))
				_open_orgs()
				print("autotest: 일 정함 ", host.OrgPanel()[0]["tasks"])
		if orgs.size() > 0 and orgs[0]["command"]:
			_open_org_name(int(orgs[0]["id"]), orgs[0]["name"], false)
			await _snap("live_org_name.png")
			host.RenameOrg(int(orgs[0]["id"]), "북쪽 개울 형제단")
			_open_orgs()
			await _snap("live_orgs_named.png")
			print("autotest: 이름 지음 ", host.OrgPanel()[0]["name"])
		org_panel.visible = false
	# 재판: 주군이 없는 집이 당한 사건은 마을 자경단 우두머리가 맡는다. 방금 세운 자경단의 집으로 모든 사건을 직접 판결한다
	host.JudgeAll = true
	# 이웃끼리 훔치게 해 사건을 만든다(신고와 수사는 확률이라 여러 번)
	var folk := []
	for e in scene.pickables:
		var pd: Dictionary = e["pd"]
		if not mine.has(int(pd["i"])) and int(pd["age"]) >= 16: folk.append(int(pd["i"]))
	for k in 200:
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
