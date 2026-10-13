# people

시조 부부 한 쌍으로 시작해 혈통을 최대한 많이 불리는 게임. Godot 4.7.2(.NET)와 C#으로 만든다.

- 규칙: `docs/rules.md`
- 구현 명세(수치, 공식): `docs/spec.md`
- 구현 순서: `docs/roadmap.md`

## 열기

1. [Godot 4.7.2 .NET 판](https://godotengine.org/download)과 [.NET 8 SDK](https://dotnet.microsoft.com/download)를 설치한다.
2. Godot 프로젝트 매니저에서 `Import` → 이 폴더의 `project.godot`를 고른다.
3. 오른쪽 위 Build를 누른 뒤 F5로 실행한다. 출력 창에 SimProbe 결과(1년 뒤 인구)가 찍힌다.

## 폴더

| 경로 | 용도 |
|---|---|
| `sim/` | 시뮬레이션 (순수 C#, Godot 의존 없음), 테스트, 화면 없는 실행기 |
| `game/` | Godot C# 스크립트 (화면, 입력) |
| `scenes/` | 씬(.tscn) |
| `scripts/` | GDScript |
| `prototypes/` | 그래픽 시안 (GDScript) |
| `assets/art`, `assets/audio`, `assets/fonts` | 리소스 |
| `docs/` | 기획 문서 |

## 미리 잡아 둔 입력

WASD 이동, Shift 달리기, E 상호작용, 좌클릭 공격, 우클릭 조준, Tab 인벤토리.

## 시안 다시 렌더링

```
xvfb-run godot --path . --rendering-method gl_compatibility --resolution 1800x800 \
  --script prototypes/human_look/render_preview.gd
```

건물 시안은 스크립트 경로를 `prototypes/building_look/render_preview.gd`로 바꿔 실행한다.

결과는 `docs/images/`에 저장된다. 화면이 있는 환경이면 `xvfb-run` 없이 실행한다.

## 시뮬레이션 테스트와 벤치마크

```
dotnet test sim/People.Sim.sln
dotnet run -c Release --project sim/People.Sim.Cli -- bench --people 50000 --days 240
dotnet run -c Release --project sim/People.Sim.Cli -- births --n 200000
```
