extends PanelContainer
## 클릭하면 오른쪽에 뜨는 정보 카드. 사람 카드와 집 카드 두 가지.
## 양피지 느낌의 밝은 판에 진한 갈색 글씨. 수치 대신 말(서툼·보통·능숙·뛰어남)로 보여 준다.

const PAPER := Color(0.965, 0.93, 0.85)
const INK := Color(0.27, 0.19, 0.12)
const MUTED := Color(0.5, 0.41, 0.31)
const LINE := Color(0.78, 0.68, 0.53)
const BORDER := Color(0.45, 0.33, 0.2)
const SKILL_COL := {
	"농사": Color(0.55, 0.66, 0.3), "목공": Color(0.66, 0.46, 0.27), "약초": Color(0.36, 0.6, 0.45),
	"장사": Color(0.82, 0.62, 0.25), "글": Color(0.36, 0.45, 0.66),
}
const EXPERIENCE := ["굶주린 어린 시절", "일찍 부모를 잃음", "대가족"]

var font_r: FontFile
var font_b: FontFile
var body: VBoxContainer
var scroll: ScrollContainer
var on_close: Callable
var max_height := 860.0                 # 이보다 길면 카드 안에서 스크롤한다


func _init() -> void:
	font_r = _font("NotoSansKR-Medium.otf")
	font_b = _font("NotoSansKR-Bold.otf")
	var th := Theme.new()
	th.default_font = font_r
	th.default_font_size = 15
	theme = th
	var sb := StyleBoxFlat.new()
	sb.bg_color = PAPER
	sb.border_color = BORDER
	sb.set_border_width_all(2)
	sb.set_corner_radius_all(12)
	sb.shadow_color = Color(0, 0, 0, 0.28)
	sb.shadow_size = 10
	sb.shadow_offset = Vector2(0, 4)
	sb.content_margin_left = 20; sb.content_margin_right = 20
	sb.content_margin_top = 16; sb.content_margin_bottom = 18
	add_theme_stylebox_override("panel", sb)
	custom_minimum_size = Vector2(380, 0)
	scroll = ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	body = VBoxContainer.new()
	body.add_theme_constant_override("separation", 8)
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.resized.connect(_fit)
	scroll.add_child(body)
	visible = false


## 맨 아래(방금 붙인 고르기 목록)가 보이게 내린다.
func scroll_end() -> void:
	await get_tree().process_frame
	await get_tree().process_frame
	scroll.scroll_vertical = int(body.size.y)


## 내용 높이에 맞추되 max_height 를 넘으면 그만큼만 보이고 스크롤한다.
func _fit() -> void:
	var h := body.get_combined_minimum_size().y
	scroll.custom_minimum_size = Vector2(340, minf(h, max_height))
	size.y = 0


static func _font(file: String) -> FontFile:
	var f := FontFile.new()
	f.load_dynamic_font(ProjectSettings.globalize_path("res://assets/fonts/" + file))
	f.subpixel_positioning = TextServer.SUBPIXEL_POSITIONING_DISABLED
	f.hinting = TextServer.HINTING_LIGHT
	return f


func _clear() -> void:
	for c in body.get_children():
		body.remove_child(c)
		c.queue_free()


func _label(text: String, size := 15, col := INK, bold := false, wrap := false) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_override("font", font_b if bold else font_r)
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", col)
	if wrap:
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.custom_minimum_size = Vector2(330, 0)
	return l


func _header(title: String, sub: String) -> void:
	var row := HBoxContainer.new()
	var col := VBoxContainer.new()
	col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	col.add_theme_constant_override("separation", 0)
	col.add_child(_label(title, 24, INK, true))
	col.add_child(_label(sub, 14, MUTED))
	row.add_child(col)
	var x := Button.new()
	x.text = "✕"
	x.flat = true
	x.add_theme_font_override("font", font_b)
	x.add_theme_font_size_override("font_size", 18)
	x.add_theme_color_override("font_color", MUTED)
	x.pressed.connect(_close)
	row.add_child(x)
	body.add_child(row)


func _close() -> void:
	visible = false
	if on_close.is_valid(): on_close.call()


func _section(title: String) -> void:
	var sep := HSeparator.new()
	var st := StyleBoxLine.new()
	st.color = LINE
	st.thickness = 1
	sep.add_theme_stylebox_override("separator", st)
	body.add_child(sep)
	body.add_child(_label(title, 13, MUTED, true))


func _chip(text: String, bg: Color, fg: Color) -> Control:
	var p := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.set_corner_radius_all(9)
	sb.content_margin_left = 9; sb.content_margin_right = 9
	sb.content_margin_top = 2; sb.content_margin_bottom = 3
	p.add_theme_stylebox_override("panel", sb)
	p.add_child(_label(text, 13, fg))
	return p


func _row(key: String, value: String) -> void:
	if value == "": return
	var r := HBoxContainer.new()
	var k := _label(key, 14, MUTED)
	k.custom_minimum_size = Vector2(64, 0)
	r.add_child(k)
	var v := _label(value, 14)
	v.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	r.add_child(v)
	body.add_child(r)


static func level_word(v: float) -> String:
	if v < 0.3: return "서툼"
	if v < 0.5: return "보통"
	if v < 0.7: return "능숙"
	return "뛰어남"


func _skill_bar(name: String, v: float) -> void:
	var r := HBoxContainer.new()
	r.add_theme_constant_override("separation", 10)
	var k := _label(name, 14)
	k.custom_minimum_size = Vector2(40, 0)
	r.add_child(k)
	var bar := ProgressBar.new()
	bar.min_value = 0; bar.max_value = 1; bar.value = v
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(0, 12)
	bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bar.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0.86, 0.8, 0.68)
	bg.set_corner_radius_all(6)
	var fg := StyleBoxFlat.new()
	fg.bg_color = SKILL_COL.get(name, Color(0.6, 0.5, 0.4))
	fg.set_corner_radius_all(6)
	bar.add_theme_stylebox_override("background", bg)
	bar.add_theme_stylebox_override("fill", fg)
	r.add_child(bar)
	var w := _label(level_word(v), 13, MUTED)
	w.custom_minimum_size = Vector2(46, 0)
	w.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	r.add_child(w)
	body.add_child(r)


## 사람 카드
func show_person(pd: Dictionary, house: Dictionary) -> void:
	_clear()
	var sex := "남" if pd["male"] else "여"
	var role: String = pd["job"]
	if pd["head"]: role += " · 가장"
	_header(pd["name"], "%s · %d세 · %s" % [sex, int(pd["age"]), role])
	var labels: Array = pd["labels"]
	if labels.size() > 0:
		var flow := HFlowContainer.new()
		flow.add_theme_constant_override("h_separation", 6)
		flow.add_theme_constant_override("v_separation", 6)
		for t in labels:
			var lived: bool = t in EXPERIENCE
			flow.add_child(_chip(t, Color(0.9, 0.82, 0.7) if lived else Color(0.84, 0.87, 0.9), Color(0.45, 0.3, 0.17) if lived else Color(0.22, 0.3, 0.42)))
		body.add_child(flow)
	var skills: Array = pd["skills"]
	if skills.size() > 0:
		_section("기술")
		for s in skills: _skill_bar(s[0], float(s[1]))
	var hints: Array = pd["hints"]
	for h in hints: body.add_child(_label("· " + h, 13, MUTED, false, true))
	_section("가족")
	_row("아버지", pd["father"])
	_row("어머니", pd["mother"])
	_row("배우자", pd["spouse"])
	if int(pd["children"]) > 0: _row("자녀", "%d명" % int(pd["children"]))
	_row("스승", pd["master"])
	var health: Array = pd["health"]
	if health.size() > 0:
		_section("건강")
		body.add_child(_label(", ".join(health), 14, Color(0.6, 0.25, 0.2), false, true))
	if pd.has("loyalty") and int(pd["age"]) >= 16:
		_section("마음")
		var lw := "광신" if pd["fanatic"] else loyalty_word(float(pd["loyalty"]))
		_row("충성", lw)
		if float(pd.get("honor", 0)) < -0.05: _row("평판", "추문이 돈다")
		if pd.has("regard"): _row("나를", regard_word(float(pd["regard"])))
	if pd.has("talents"):
		_section("떠본 결과")
		for t in pd["talents"]: _row(t[0], "재능 " + t[1])
		var nat: Array = pd.get("nature", [])
		if nat.size() > 0: body.add_child(_label(", ".join(nat), 14, INK, false, true))
	_section("집")
	var line := "%s 가문의 집 · %s · 땅 %d필지 · 식량 %s" % [house["family"], house.get("class", ""), int(house["plots"]), _food_word(house)]
	if int(house.get("leasedIn", 0)) > 0: line += " · %s 가문 땅 %d필지를 부침" % [house.get("lord", ""), int(house["leasedIn"])]
	if float(house.get("debt", 0)) > 0.5: line += " · 빚 %d" % int(float(house["debt"]))
	if house.get("patron", "") != "": line += " · %s 가문의 보호" % house["patron"]
	body.add_child(_label(line, 14, INK, false, true))
	visible = true


static func regard_word(v: float) -> String:
	if v < -30: return "미워한다"
	if v < -8: return "꺼린다"
	if v < 8: return "잘 모른다"
	if v < 30: return "좋게 본다"
	return "아주 좋아한다"


static func loyalty_word(v: float) -> String:
	if v < 0.25: return "등을 돌림"
	if v < 0.45: return "미덥지 않음"
	if v < 0.6: return "보통"
	if v < 0.75: return "믿음직함"
	return "깊이 따름"


## 카드 아래에 버튼 묶음을 붙인다. items: [{"text", "sub", "cb": Callable, "warn": bool}]
func add_actions(title: String, items: Array) -> void:
	if items.is_empty(): return
	_section(title)
	for it in items:
		var b := Button.new()
		b.text = it["text"] + ("   " + it["sub"] if it.get("sub", "") != "" else "")
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.add_theme_font_override("font", font_r)
		b.add_theme_font_size_override("font_size", 14)
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color(0.93, 0.86, 0.74) if not it.get("warn", false) else Color(0.94, 0.8, 0.74)
		sb.set_corner_radius_all(7)
		sb.content_margin_left = 10; sb.content_margin_right = 10
		sb.content_margin_top = 5; sb.content_margin_bottom = 6
		var hv := sb.duplicate()
		hv.bg_color = sb.bg_color.darkened(0.08)
		b.add_theme_stylebox_override("normal", sb)
		b.add_theme_stylebox_override("hover", hv)
		b.add_theme_stylebox_override("pressed", hv)
		b.add_theme_color_override("font_color", INK)
		b.add_theme_color_override("font_hover_color", INK)
		b.pressed.connect(it["cb"])
		body.add_child(b)


func _food_word(house: Dictionary) -> String:
	var f := float(house["food"])
	if f < 20: return "바닥남"
	if f < 80: return "빠듯함"
	if f < 250: return "넉넉함"
	return "남아돎"


## 집 카드
func show_house(hd: Array, house: Dictionary, members: Array) -> void:
	_clear()
	var vacant := int(hd[2]) == 0
	var cap := int(hd[3])
	var kind := "오두막" if cap <= 4 else "살림집" if cap <= 8 else "큰 농가" if cap <= 12 else "대저택"
	var res: int = int(hd[6]) if hd.size() > 6 else 0
	if res > 0: kind = ["", "영주 저택", "망루 달린 저택", "성"][res]
	if vacant:
		var cond := float(hd[5])
		_header("빈 성" if res >= 3 else "빈집", "%s · 정원 %d명" % [kind, cap])
		_section("상태")
		_skill_bar("상태", cond)
		body.add_child(_label("대가 끊겨 비었다. 사는 사람이 없으면 해마다 낡아 무너진다.", 13, MUTED, false, true))
		visible = true
		return
	_header("%s 가문의 집" % house["family"], "%s · 정원 %d명 · %d년에 세움" % [kind, cap, int(house["founded"])])
	_row("가장", house["head"])
	_row("신분", house.get("class", ""))
	var land := "%d필지" % int(house["plots"])
	if int(house.get("leasedOut", 0)) > 0: land += " (그중 %d필지 소작 줌)" % int(house["leasedOut"])
	_row("땅", land)
	if int(house.get("leasedIn", 0)) > 0:
		_row("빌린 땅", "%d필지 · 지주 %s 가문" % [int(house["leasedIn"]), house.get("lord", "")])
	_row("식량", _food_word(house))
	if float(house.get("debt", 0)) > 0.5: _row("빚", "식량 %d" % int(float(house["debt"])))
	_row("상속", "맏이가 잇는다" if house.get("policy", "장자") == "장자" else "아들들에게 나눈다")
	if int(house.get("clients", 0)) > 0:
		_row("거느림", "따르는 집 %d · 군인 %d · 징수관 %d" % [int(house["clients"]), int(house.get("soldiers", 0)), int(house.get("collectors", 0))])
	if house.get("patron", "") != "":
		_row("보호", "%s 가문에 공납" % house["patron"])
	_section("식구 %d명" % members.size())
	for m in members:
		var r := HBoxContainer.new()
		var n := _label(m["name"], 14, INK, m["head"])
		n.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		r.add_child(n)
		r.add_child(_label("%d세 · %s" % [int(m["age"]), m["job"]], 13, MUTED))
		body.add_child(r)
	visible = true
