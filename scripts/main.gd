extends Node
## 진입점. 기획이 정해지면 시점(2D 아이소/3D 직교)에 맞춰 월드 씬을 붙인다.


func _ready() -> void:
	print("people: 프로젝트 준비 완료 (Godot %s)" % Engine.get_version_info().string)
