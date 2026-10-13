class_name Outfits
extends RefCounted
## 서양 중세 옷 시안. 옷마다 부위별 텍스처를 그리는 함수 묶음을 돌려준다.
## v 기준: 몸통 0 = 목(1.5m), 1 = 사타구니(0.87m). 팔 0 = 어깨, 1 = 손목.
## 다리 0 = 엉덩이, 1 = 발목. 치마 0 = 허리, 1 = 단.

const HB := preload("res://prototypes/human_look/human_builder.gd")

const LINEN := Color(0.84, 0.8, 0.7)
const BROWN := Color(0.44, 0.32, 0.21)
const RUSSET := Color(0.55, 0.29, 0.18)
const MADDER := Color(0.6, 0.16, 0.13)
const WOAD := Color(0.24, 0.34, 0.55)
const GREEN := Color(0.31, 0.42, 0.24)
const GREY := Color(0.5, 0.5, 0.47)
const BLACK := Color(0.15, 0.14, 0.14)
const GOLD := Color(0.82, 0.66, 0.26)
const LEATHER := Color(0.3, 0.2, 0.13)
const PADDED := Color(0.66, 0.58, 0.42)


static func _t(img: Image) -> ImageTexture:
	return HB.tex(img)


static func _belt(img: Image, col: Color, buckle: Color, v := 0.77) -> void:
	HB.band(img, v, v + 0.05, col)
	HB.band(img, v - 0.005, v + 0.055, buckle, 0.48, 0.52)


static func _quilt(img: Image) -> void:
	for i in 16:
		HB.tint(img, 0.0, 1.0, 0.82, i / 16.0, i / 16.0 + 0.012)
	for i in 10:
		HB.tint(img, i / 10.0, i / 10.0 + 0.015, 0.85)


static func _feet(col: Color) -> Callable:
	return func(s: int) -> Texture2D: return _t(HB.fabric(col, s + 40, 32, 32, 0.0))


static func peasant_man(tunic := BROWN, hose := GREY) -> Dictionary:
	return {
		"torso": func(s: int) -> Texture2D:
			var i := HB.fabric(tunic, s)
			HB.tint(i, 0.0, 0.05, 0.75)
			HB.band(i, 0.04, 0.28, tunic.darkened(0.45), 0.493, 0.507)
			_belt(i, LEATHER, Color(0.6, 0.55, 0.4))
			return _t(i),
		"arm": func(s: int) -> Texture2D:
			var i := HB.fabric(tunic, s + 1)
			HB.tint(i, 0.9, 1.0, 0.8)
			return _t(i),
		"leg": func(s: int) -> Texture2D:
			var i := HB.fabric(hose, s + 2)
			for k in 8:
				HB.tint(i, 0.6 + k * 0.05, 0.6 + k * 0.05 + 0.02, 0.8)
			return _t(i),
		"foot": _feet(LEATHER),
		"skirt": func(s: int) -> Texture2D:
			var i := HB.fabric(tunic, s + 3)
			HB.tint(i, 0.93, 1.0, 0.78)
			HB.band(i, 0.0, 0.08, LEATHER)
			return _t(i),
		"skirt_hem": 0.6,
		"skirt_flare": 0.22,
	}


static func peasant_woman(dress := RUSSET, apron := LINEN) -> Dictionary:
	return {
		"torso": func(s: int) -> Texture2D:
			var i := HB.fabric(dress, s)
			HB.band(i, 0.0, 0.12, LINEN, 0.4, 0.6)
			HB.band(i, 0.12, 0.14, dress.darkened(0.3), 0.4, 0.6)
			for k in 5:
				HB.band(i, 0.16 + k * 0.08, 0.17 + k * 0.08, Color(0.2, 0.15, 0.1), 0.47, 0.53)
			HB.band(i, 0.7, 0.735, BROWN.darkened(0.2))
			return _t(i),
		"arm": func(s: int) -> Texture2D:
			var i := HB.fabric(dress, s + 1)
			HB.band(i, 0.86, 1.0, LINEN)
			return _t(i),
		"leg": func(s: int) -> Texture2D: return _t(HB.fabric(BLACK.lightened(0.15), s + 2)),
		"foot": _feet(LEATHER),
		"skirt": func(s: int) -> Texture2D:
			var i := HB.fabric(dress, s + 3)
			HB.band(i, 0.0, 0.03, apron.darkened(0.1))
			HB.band(i, 0.02, 0.8, apron, 0.37, 0.63)
			HB.tint(i, 0.0, 0.8, 0.94, 0.37, 0.39)
			HB.tint(i, 0.0, 0.8, 0.94, 0.61, 0.63)
			HB.tint(i, 0.95, 1.0, 0.8)
			return _t(i),
		"skirt_hem": 0.04,
		"skirt_flare": 0.27,
	}


static func smith(skin: Color) -> Dictionary:
	return {
		"torso": func(s: int) -> Texture2D:
			var i := HB.fabric(LINEN.darkened(0.12), s)
			HB.band(i, 0.0, 0.05, skin, 0.42, 0.58)
			HB.band(i, 0.22, 1.0, LEATHER, 0.3, 0.7)
			HB.band(i, 0.0, 0.22, LEATHER.darkened(0.2), 0.34, 0.36)
			HB.band(i, 0.0, 0.22, LEATHER.darkened(0.2), 0.64, 0.66)
			return _t(i),
		"arm": func(s: int) -> Texture2D:
			var i := HB.fabric(LINEN.darkened(0.12), s + 1)
			HB.band(i, 0.4, 0.47, LINEN.darkened(0.25))
			HB.band(i, 0.47, 1.0, skin)
			return _t(i),
		"leg": func(s: int) -> Texture2D:
			var i := HB.fabric(BROWN.darkened(0.25), s + 2)
			HB.band(i, 0.72, 1.0, LEATHER.darkened(0.3))
			return _t(i),
		"foot": _feet(LEATHER.darkened(0.3)),
		"skirt": func(s: int) -> Texture2D:
			var i := HB.fabric(LINEN.darkened(0.25), s + 3)
			HB.band(i, 0.0, 1.0, LEATHER, 0.3, 0.7)
			HB.band(i, 0.0, 0.07, BLACK)
			return _t(i),
		"skirt_hem": 0.5,
		"skirt_flare": 0.21,
	}


## lord_color: 섬기는 영주의 색. 겉옷(타바드)으로 소속을 보여 준다.
static func soldier(lord_color := MADDER) -> Dictionary:
	return {
		"torso": func(s: int) -> Texture2D:
			var i := HB.fabric(PADDED, s)
			_quilt(i)
			HB.band(i, 0.02, 1.0, lord_color, 0.36, 0.64)
			HB.band(i, 0.02, 1.0, lord_color, 0.0, 0.14)
			HB.band(i, 0.02, 1.0, lord_color, 0.86, 1.0)
			_belt(i, LEATHER, Color(0.6, 0.6, 0.62))
			return _t(i),
		"arm": func(s: int) -> Texture2D:
			var i := HB.fabric(PADDED, s + 1)
			_quilt(i)
			return _t(i),
		"leg": func(s: int) -> Texture2D:
			var i := HB.fabric(BROWN, s + 2)
			HB.band(i, 0.68, 1.0, LEATHER.darkened(0.15))
			HB.tint(i, 0.68, 0.71, 0.7)
			return _t(i),
		"foot": _feet(LEATHER.darkened(0.15)),
		"skirt": func(s: int) -> Texture2D:
			var i := HB.fabric(PADDED, s + 3)
			_quilt(i)
			HB.band(i, 0.0, 1.0, lord_color, 0.38, 0.62)
			HB.band(i, 0.0, 0.07, LEATHER)
			return _t(i),
		"skirt_hem": 0.58,
		"skirt_flare": 0.23,
	}


static func noble_man(doublet := WOAD, cloak := MADDER) -> Dictionary:
	return {
		"torso": func(s: int) -> Texture2D:
			var i := HB.fabric(doublet, s, 64, 64, 0.015)
			HB.band(i, 0.0, 0.05, GOLD)
			for k in 9:
				HB.dot(i, 0.5, 0.08 + k * 0.08, 0.012, 0.015, GOLD)
			_belt(i, BLACK, GOLD, 0.8)
			return _t(i),
		"arm": func(s: int) -> Texture2D:
			var i := HB.fabric(doublet, s + 1, 64, 64, 0.015)
			HB.band(i, 0.93, 1.0, GOLD)
			return _t(i),
		"leg": func(s: int) -> Texture2D: return _t(HB.fabric(BLACK.lightened(0.1), s + 2, 64, 64, 0.01)),
		"foot": _feet(BLACK),
		"skirt": func(s: int) -> Texture2D:
			var i := HB.fabric(doublet, s + 3, 64, 64, 0.015)
			HB.band(i, 0.9, 1.0, GOLD)
			return _t(i),
		"skirt_hem": 0.7,
		"skirt_flare": 0.22,
		"cloak": func(s: int) -> Texture2D:
			var i := HB.fabric(cloak, s + 4, 64, 64, 0.015)
			HB.band(i, 0.95, 1.0, GOLD)
			HB.band(i, 0.0, 1.0, GOLD, 0.655, 0.675)
			HB.band(i, 0.0, 1.0, GOLD, 0.325, 0.345)
			return _t(i),
	}


static func noble_woman(gown := GREEN) -> Dictionary:
	return {
		"torso": func(s: int) -> Texture2D:
			var i := HB.fabric(gown, s, 64, 64, 0.015)
			HB.band(i, 0.0, 0.1, LINEN, 0.38, 0.62)
			HB.band(i, 0.1, 0.125, GOLD, 0.38, 0.62)
			HB.band(i, 0.86, 0.9, GOLD)
			return _t(i),
		"arm": func(s: int) -> Texture2D:
			var i := HB.fabric(gown, s + 1, 64, 64, 0.015)
			HB.band(i, 0.9, 1.0, GOLD)
			return _t(i),
		"leg": func(s: int) -> Texture2D: return _t(HB.fabric(BLACK, s + 2)),
		"foot": _feet(BLACK),
		"skirt": func(s: int) -> Texture2D:
			var i := HB.fabric(gown, s + 3, 64, 64, 0.015)
			HB.band(i, 0.0, 0.03, GOLD)
			HB.band(i, 0.0, 0.45, GOLD, 0.495, 0.505)
			HB.band(i, 0.95, 1.0, GOLD)
			return _t(i),
		"skirt_hem": 0.02,
		"skirt_flare": 0.31,
	}


static func monk() -> Dictionary:
	var robe := BROWN.darkened(0.1)
	return {
		"torso": func(s: int) -> Texture2D:
			var i := HB.fabric(robe, s)
			HB.band(i, 0.76, 0.81, Color(0.72, 0.63, 0.45))
			return _t(i),
		"arm": func(s: int) -> Texture2D: return _t(HB.fabric(robe, s + 1)),
		"leg": func(s: int) -> Texture2D: return _t(HB.fabric(robe.darkened(0.2), s + 2)),
		"foot": _feet(LEATHER),
		"skirt": func(s: int) -> Texture2D:
			var i := HB.fabric(robe, s + 3)
			HB.band(i, 0.0, 0.35, Color(0.72, 0.63, 0.45), 0.52, 0.535)
			return _t(i),
		"skirt_hem": 0.04,
		"skirt_flare": 0.25,
		"cloak": func(s: int) -> Texture2D: return _t(HB.fabric(robe.darkened(0.08), s + 4)),
	}
