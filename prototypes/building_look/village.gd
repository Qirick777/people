class_name VillageSpecs
extends RefCounted
## 시안용 건물 사양과 마을 배치. 건물 시안과 도트 시안이 같이 쓴다.

const BB := preload("res://prototypes/building_look/building_builder.gd")
const BM := preload("res://prototypes/building_look/materials.gd")
const HB := preload("res://prototypes/human_look/human_builder.gd")
const OF := preload("res://prototypes/human_look/outfits.gd")

const GREEN := Color(0.33, 0.42, 0.26)
const RED := Color(0.5, 0.18, 0.14)
const BLUE := Color(0.24, 0.32, 0.45)


# ---------------------------------------------------------------- 사양

static func hut() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 4, "d": 3}], "ground": "planks", "roof_mat": "thatch", "pitch": 50.0, "plinth": 0.15, "seed": 1}

static func cottage(age := 0.0) -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 4}], "ground": "daub", "roof_mat": "thatch", "pitch": 50.0, "chimney": true, "shutter": GREEN, "age": age, "seed": 2}

static func timber2() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 5, "floors": 2}], "ground": "timber", "roof_mat": "shingle", "pitch": 52.0, "chimney": true, "shutter": RED, "seed": 3}

static func stone2() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 7, "d": 5, "floors": 2}], "ground": "stone", "roof_mat": "slate", "pitch": 42.0, "chimney": true, "seed": 4}

static func mixed2() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 5, "floors": 2}], "ground": "stone", "upper": "timber", "roof_mat": "tile", "pitch": 45.0, "shutter": BLUE, "seed": 5}

static func cottage_annex() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 6, "d": 4}, {"x": 6, "z": 1, "w": 3, "d": 3, "roof": "lean", "lean": Vector2i(1, 0), "ground": "planks"}],
		"ground": "daub", "roof_mat": "thatch", "pitch": 50.0, "chimney": true, "shutter": GREEN, "seed": 6}

static func farm_l() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 8, "d": 5, "floors": 2}, {"x": 5, "z": 5, "w": 3, "d": 4, "ridge": "z"}],
		"ground": "stone", "upper": "timber", "gable": "timber", "roof_mat": "shingle", "pitch": 50.0, "chimney": true, "shutter": RED, "seed": 7}

static func house_u() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 10, "d": 5, "floors": 2}, {"x": 0, "z": 5, "w": 3, "d": 4, "floors": 2, "ridge": "z"},
		{"x": 7, "z": 5, "w": 3, "d": 4, "floors": 2, "ridge": "z"}],
		"ground": "stone", "upper": "timber", "gable": "timber", "roof_mat": "tile", "pitch": 48.0, "chimney": true, "shutter": BLUE, "seed": 8}

static func manor() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 12, "d": 5, "floors": 2}, {"x": 0, "z": 5, "w": 3, "d": 5, "floors": 2, "ridge": "z"},
		{"x": 9, "z": 5, "w": 3, "d": 5, "floors": 2, "ridge": "z"}, {"x": 0, "z": 10, "w": 12, "d": 3}],
		"doors": [[5, 4, 0, 1], [6, 12, 0, 1]],
		"ground": "stone", "upper": "timber", "gable": "timber", "roof_mat": "slate", "pitch": 48.0, "chimney": true, "shutter": RED, "seed": 9}

static func town_hall() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 14, "d": 5, "floors": 2}, {"x": 0, "z": 5, "w": 4, "d": 5, "floors": 2, "ridge": "z"},
		{"x": 10, "z": 5, "w": 4, "d": 5, "floors": 2, "ridge": "z"}, {"x": 0, "z": 10, "w": 14, "d": 4, "floors": 2},
		{"x": 6, "z": 12, "w": 3, "d": 3, "floors": 4, "roof": "pyramid", "ground": "stone_light", "upper": "stone_light", "no_windows": false}],
		"doors": [[7, 14, 0, 1]], "ground": "stone_light", "upper": "timber", "gable": "timber", "roof_mat": "slate", "pitch": 50.0,
		"shutter": RED, "seed": 10}

static func cut_house() -> Dictionary:
	return {"blocks": [{"x": 0, "z": 0, "w": 7, "d": 5}], "ground": "timber", "roof_mat": "thatch", "cutaway": true, "seed": 11,
		"partitions": [[4.0, 0.2, 4.0, 3.2]],
		"furniture": [
			[Vector3(1.0, 0.45, 2.0), Vector3(5.4, 0.22, 1.3), "wood"], [Vector3(0.9, 0.12, 1.8), Vector3(5.4, 0.5, 1.3), "linen"],
			[Vector3(1.0, 0.45, 2.0), Vector3(6.4, 0.22, 3.6), "wood"], [Vector3(0.9, 0.12, 1.8), Vector3(6.4, 0.5, 3.6), "linen"],
			[Vector3(1.6, 0.75, 0.8), Vector3(2.0, 0.37, 2.8), "wood"], [Vector3(1.6, 0.42, 0.3), Vector3(2.0, 0.21, 3.5), "wood"],
			[Vector3(1.6, 0.42, 0.3), Vector3(2.0, 0.21, 2.1), "wood"],
			[Vector3(1.3, 0.7, 0.9), Vector3(0.9, 0.35, 0.65), "stone"], [Vector3(0.7, 0.08, 0.5), Vector3(0.9, 0.72, 0.65), "ember"],
			[Vector3(0.5, 0.8, 0.5), Vector3(3.4, 0.4, 0.6), "wood"], [Vector3(0.5, 0.8, 0.5), Vector3(3.4, 0.4, 1.2), "wood"]]}


static func _tree(world: Node3D, pos: Vector3, rng: RandomNumberGenerator) -> void:
	if BM.flat:
		_block_tree(world, pos, rng)
		return
	var h := rng.randf_range(2.2, 3.4)
	var trunk := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = 0.14
	cm.bottom_radius = 0.22
	cm.height = h
	cm.radial_segments = 7
	trunk.mesh = cm
	trunk.material_override = BM.get_mat("wood")
	trunk.position = pos + Vector3(0, h * 0.5, 0)
	world.add_child(trunk)
	for k in 3:
		var leaf := MeshInstance3D.new()
		var sm := SphereMesh.new()
		var r := rng.randf_range(1.1, 1.7) * (1.0 - k * 0.2)
		sm.radius = r
		sm.height = r * 1.6
		sm.radial_segments = 7
		sm.rings = 4
		leaf.mesh = sm
		leaf.material_override = BM.get_mat("leaf")
		leaf.position = pos + Vector3(rng.randf_range(-0.5, 0.5), h + k * 0.9, rng.randf_range(-0.5, 0.5))
		world.add_child(leaf)


static func _person(world: Node3D, pos: Vector3, rng: RandomNumberGenerator, i: int) -> void:
	var female := rng.randf() < 0.5
	var cloth := [OF.LINEN, OF.BROWN, OF.RUSSET, OF.WOAD, OF.GREEN, OF.GREY]
	var c1: Color = cloth[rng.randi() % cloth.size()]
	var c2: Color = cloth[rng.randi() % cloth.size()]
	var hairs := [Color(0.1, 0.08, 0.07), Color(0.3, 0.2, 0.12), Color(0.78, 0.64, 0.38), Color(0.62, 0.26, 0.12)]
	var skins := [Color(0.8, 0.63, 0.53), Color(0.75, 0.58, 0.46), Color(0.68, 0.5, 0.38)]
	var d := {"outfit": OF.peasant_woman(c1) if female else OF.peasant_man(c1, c2), "female": female, "seed": 300 + i,
		"skin": skins[rng.randi() % skins.size()], "hair": "bun" if female else "short",
		"hair_color": hairs[rng.randi() % hairs.size()], "height": rng.randf_range(0.94, 1.06)}
	if female and rng.randf() < 0.5: d["hat"] = "coif"
	var p := HB.build(d)
	p.position = pos
	p.rotation.y = rng.randf_range(-PI, PI)
	world.add_child(p)



static func build(world: Node3D, base: Vector3, make_person := Callable()) -> void:
	var road := MeshInstance3D.new()
	var rb := BoxMesh.new()
	rb.size = Vector3(70, 0.04, 4)
	road.mesh = rb
	road.material_override = BM.get_mat("dirt")
	road.position = base + Vector3(25, 0.02, 2)
	world.add_child(road)
	var north := [cottage(), timber2(), cut_house(), farm_l(), stone2()]
	var south := [hut(), cottage_annex(), mixed2(), cottage(0.9), timber2()]
	var x := 0.0
	for sp in north:
		var b: Dictionary = sp["blocks"][0]
		var n := BB.build(sp)
		n.position = base + Vector3(x, 0, -1 - b["d"])
		world.add_child(n)
		x += b["w"] + 4.0
	x = 2.0
	for sp in south:
		var w := 0
		var d := 0
		for bl in sp["blocks"]:
			w = max(w, bl["x"] + bl["w"])
			d = max(d, bl["z"] + bl["d"])
		var n := BB.build(sp)
		n.rotation.y = PI
		n.position = base + Vector3(x + w, 0, 5 + d)
		world.add_child(n)
		x += w + 4.0
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	for i in 18:
		var tx := rng.randf_range(-6, 56)
		var tz := rng.randf_range(-22, -12) if rng.randf() < 0.5 else rng.randf_range(16, 24)
		_tree(world, base + Vector3(tx, 0, tz), rng)
	for i in 16:
		var pos := base + Vector3(rng.randf_range(0, 50), 0, rng.randf_range(0.5, 3.5))
		if make_person.is_valid():
			var p: Node3D = make_person.call(rng, i)
			p.position = pos
			p.rotation.y = rng.randf_range(-PI, PI)
			world.add_child(p)
		else:
			_person(world, pos, rng, i)


static func _block_tree(world: Node3D, pos: Vector3, rng: RandomNumberGenerator) -> void:
	var h := rng.randf_range(1.8, 2.8)
	BB.box(world, Vector3(0.32, h, 0.32), pos + Vector3(0, h * 0.5, 0), BM.get_mat("wood"))
	var leaf := BM.FLAT["leaf"] as Color
	if rng.randf() < 0.35:
		for k in 3:
			var w := 1.9 - k * 0.55
			BB.box(world, Vector3(w, 0.9, w), pos + Vector3(0, h + k * 0.75, 0), BM.flat_mat(leaf.darkened(0.2 + k * 0.03)))
	else:
		for k in 3:
			var w := rng.randf_range(1.2, 1.9) * (1.0 - k * 0.15)
			var c := leaf.lightened(rng.randf_range(-0.05, 0.1))
			BB.box(world, Vector3(w, w * 0.85, w), pos + Vector3(rng.randf_range(-0.4, 0.4), h + 0.3 + k * 0.6, rng.randf_range(-0.4, 0.4)), BM.flat_mat(c))
