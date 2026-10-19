class_name UiKit
## 화면 부품 모양. 원칙: 누를 수 있는 것은 색이 찬 입체 버튼(손가락 커서), 정보는 바탕 없는 글과 작은 꼬리표.

const PAPER := Color(0.965, 0.93, 0.85)
const PAPER_DEEP := Color(0.92, 0.86, 0.74)
const INK := Color(0.27, 0.19, 0.12)
const MUTED := Color(0.5, 0.41, 0.31)
const LINE := Color(0.78, 0.68, 0.53)
const BORDER := Color(0.45, 0.33, 0.2)
const ACT := Color(0.24, 0.42, 0.45)        # 보통 행동: 짙은 청록
const RISK := Color(0.6, 0.24, 0.2)         # 몰래 하는 일·위험한 일: 꼭두서니
const MALE := Color(0.36, 0.5, 0.66)
const FEMALE := Color(0.72, 0.42, 0.48)

static var _fonts := {}


static func font(bold := false) -> FontFile:
	var key := "b" if bold else "r"
	if not _fonts.has(key):
		var f := FontFile.new()
		f.load_dynamic_font(ProjectSettings.globalize_path("res://assets/fonts/" + ("NotoSansKR-Bold.otf" if bold else "NotoSansKR-Medium.otf")))
		f.subpixel_positioning = TextServer.SUBPIXEL_POSITIONING_DISABLED
		f.hinting = TextServer.HINTING_LIGHT
		_fonts[key] = f
	return _fonts[key]


static func label(text: String, size := 15, col := INK, bold := false) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_override("font", font(bold))
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", col)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


static func box(bg: Color, radius := 8, border := Color(0, 0, 0, 0), bw := 0) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.set_corner_radius_all(radius)
	if bw > 0:
		sb.border_color = border
		sb.set_border_width_all(bw)
	return sb


static func panel(col := PAPER) -> PanelContainer:
	var p := PanelContainer.new()
	var sb := box(col, 10, BORDER, 2)
	sb.content_margin_left = 14; sb.content_margin_right = 14
	sb.content_margin_top = 10; sb.content_margin_bottom = 12
	sb.shadow_color = Color(0, 0, 0, 0.22); sb.shadow_size = 6; sb.shadow_offset = Vector2(0, 3)
	p.add_theme_stylebox_override("panel", sb)
	return p


## 입체 버튼의 세 상태(아래 테두리가 두꺼워 떠 보이고, 누르면 내려앉는다).
static func _raised(b: Button, col: Color, h_pad := 12, v_pad := 7) -> void:
	var n := box(col, 8, col.darkened(0.35), 0)
	n.border_width_bottom = 3
	n.border_color = col.darkened(0.35)
	n.content_margin_left = h_pad; n.content_margin_right = h_pad
	n.content_margin_top = v_pad; n.content_margin_bottom = v_pad
	var hv := n.duplicate()
	hv.bg_color = col.lightened(0.1)
	var pr := n.duplicate()
	pr.bg_color = col.darkened(0.1)
	pr.border_width_bottom = 1
	pr.content_margin_top = v_pad + 2
	b.add_theme_stylebox_override("normal", n)
	b.add_theme_stylebox_override("hover", hv)
	b.add_theme_stylebox_override("pressed", pr)
	b.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	b.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND


## 행동 버튼: 이름(굵게), 아래에 덧말, 오른쪽에 받아들일 확률 막대. chance < 0 이면 막대 대신 ›.
static func action(title: String, note := "", chance := -1.0, risky := false) -> Button:
	var b := Button.new()
	var col := RISK if risky else ACT
	_raised(b, col)
	var row := HBoxContainer.new()
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.set_anchors_preset(Control.PRESET_FULL_RECT)
	row.offset_left = 12; row.offset_right = -12; row.offset_top = 5; row.offset_bottom = -7
	row.add_theme_constant_override("separation", 10)
	var txt := VBoxContainer.new()
	txt.mouse_filter = Control.MOUSE_FILTER_IGNORE
	txt.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	txt.add_theme_constant_override("separation", -2)
	txt.alignment = BoxContainer.ALIGNMENT_CENTER
	txt.add_child(label(title, 15, Color(1, 0.98, 0.93), true))
	if note != "": txt.add_child(label(note, 12, Color(1, 0.95, 0.85, 0.8)))
	row.add_child(txt)
	if chance >= 0.0:
		var c := VBoxContainer.new()
		c.mouse_filter = Control.MOUSE_FILTER_IGNORE
		c.alignment = BoxContainer.ALIGNMENT_CENTER
		c.add_theme_constant_override("separation", 1)
		var pct := label("%d%%" % int(round(chance * 100.0)), 14, Color(1, 0.98, 0.93), true)
		pct.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		c.add_child(pct)
		c.add_child(meter(chance, 64, Color(1, 0.97, 0.88), col.darkened(0.3)))
		row.add_child(c)
	else:
		var arrow := label("›", 22, Color(1, 0.98, 0.93), true)
		arrow.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		row.add_child(arrow)
	b.add_child(row)
	b.custom_minimum_size = Vector2(0, 50 if note != "" else 40)
	return b


## 덜 중요한 버튼(그만두기 등): 테두리만 있는 버튼.
static func ghost(text: String) -> Button:
	var b := Button.new()
	b.text = text
	var n := box(Color(0, 0, 0, 0), 8, BORDER, 2)
	n.content_margin_left = 12; n.content_margin_right = 12; n.content_margin_top = 5; n.content_margin_bottom = 6
	var hv := n.duplicate()
	hv.bg_color = Color(0.45, 0.33, 0.2, 0.1)
	b.add_theme_stylebox_override("normal", n)
	b.add_theme_stylebox_override("hover", hv)
	b.add_theme_stylebox_override("pressed", hv)
	b.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	b.add_theme_font_override("font", font(true))
	b.add_theme_font_size_override("font_size", 14)
	for k in ["font_color", "font_hover_color", "font_pressed_color"]: b.add_theme_color_override(k, BORDER)
	b.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	return b


## 붙어 있는 단추 묶음(속도). 고른 것은 짙게 차고 나머지는 옅다.
static func segment(b: Button, on: bool, first: bool, last: bool) -> void:
	var col := INK if on else PAPER_DEEP
	var n := box(col, 0, BORDER, 0)
	n.corner_radius_top_left = 8 if first else 0; n.corner_radius_bottom_left = 8 if first else 0
	n.corner_radius_top_right = 8 if last else 0; n.corner_radius_bottom_right = 8 if last else 0
	n.content_margin_left = 12; n.content_margin_right = 12; n.content_margin_top = 4; n.content_margin_bottom = 5
	var hv := n.duplicate()
	hv.bg_color = col.lightened(0.15) if on else col.darkened(0.06)
	b.add_theme_stylebox_override("normal", n)
	b.add_theme_stylebox_override("hover", hv)
	b.add_theme_stylebox_override("pressed", hv)
	b.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	b.add_theme_font_override("font", font(true))
	b.add_theme_font_size_override("font_size", 14)
	var fc := PAPER if on else INK
	for k in ["font_color", "font_hover_color", "font_pressed_color"]: b.add_theme_color_override(k, fc)
	b.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND


## 정보 칸: 작은 이름표 위에 값. 바탕이 없다.
static func stat(caption: String, value: String, col := INK) -> VBoxContainer:
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", -3)
	v.add_child(label(caption, 12, MUTED))
	var l := label(value, 15, col, true)
	l.name = "Value"
	v.add_child(l)
	return v


## 꼬리표(정보): 둥근 옅은 바탕, 테두리·그림자 없음.
static func chip(text: String, bg: Color, fg: Color, size := 12) -> PanelContainer:
	var p := PanelContainer.new()
	var sb := box(bg, 9)
	sb.content_margin_left = 7; sb.content_margin_right = 7; sb.content_margin_top = 1; sb.content_margin_bottom = 2
	p.add_theme_stylebox_override("panel", sb)
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	p.add_child(label(text, size, fg))
	return p


## 얇은 막대(0~1).
static func meter(v: float, width: int, fill: Color, back: Color) -> ProgressBar:
	var bar := ProgressBar.new()
	bar.min_value = 0; bar.max_value = 1; bar.value = v
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(width, 6)
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bar.add_theme_stylebox_override("background", box(back, 3))
	bar.add_theme_stylebox_override("fill", box(fill, 3))
	return bar


## 동그란 얼굴 자리: 성별 색 바탕에 이름 첫 글자.
static func avatar(name: String, male: bool, size := 30) -> PanelContainer:
	var p := PanelContainer.new()
	p.add_theme_stylebox_override("panel", box(MALE if male else FEMALE, size / 2))
	p.custom_minimum_size = Vector2(size, size)
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var l := label(name.substr(0, 1), int(size * 0.45), Color(1, 0.98, 0.94), true)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	p.add_child(l)
	return p


## 고를 수 있는 줄(가족 목록): 옅은 바탕, 마우스를 올리면 짙어지고, 고른 줄은 청록 테두리.
static func row_button(selected: bool) -> Button:
	var b := Button.new()
	var n := box(Color(1, 0.98, 0.93) if not selected else Color(0.88, 0.94, 0.92), 8, ACT if selected else LINE, 2 if selected else 1)
	var hv := n.duplicate()
	hv.bg_color = n.bg_color.darkened(0.05)
	b.add_theme_stylebox_override("normal", n)
	b.add_theme_stylebox_override("hover", hv)
	b.add_theme_stylebox_override("pressed", hv)
	b.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	b.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	return b
