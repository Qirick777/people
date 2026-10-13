# people

좀보이드 식 반 3D 탑뷰 생존 게임. Godot 4.7.2로 만든다. 지금은 기획 단계다.

## 열기

1. [Godot 4.7.2](https://godotengine.org/download)를 받는다. (C#을 쓰기로 하면 .NET 판을 받는다.)
2. Godot 프로젝트 매니저에서 `Import` → 이 폴더의 `project.godot`를 고른다.
3. F5로 실행하면 출력 창에 준비 완료 메시지가 찍힌다.

## 폴더

| 경로 | 용도 |
|---|---|
| `scenes/` | 씬(.tscn) |
| `scripts/` | GDScript |
| `assets/art`, `assets/audio`, `assets/fonts` | 리소스 |
| `docs/` | 기획 문서 |

## 미리 잡아 둔 입력

WASD 이동, Shift 달리기, E 상호작용, 좌클릭 공격, 우클릭 조준, Tab 인벤토리.

## 사람 외형 시안 다시 렌더링

```
xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 \
  --script prototypes/human_look/render_preview.gd
```

결과는 `docs/images/human_*.png`에 저장된다. 화면이 있는 환경이면 `xvfb-run` 없이 실행한다.
