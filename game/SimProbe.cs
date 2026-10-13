using Godot;
using People.Sim;

namespace People.Game;

/// <summary>
/// 시뮬레이션 라이브러리가 Godot 안에서 도는지 확인하는 임시 노드.
/// 시작 인구를 만들고 1년을 돌린 뒤 결과를 출력한다.
/// </summary>
public partial class SimProbe : Node
{
    [Export] public int Population { get; set; } = 1000;
    [Export] public ulong Seed { get; set; } = 1;

    public override void _Ready()
    {
        var world = new World(Seed);
        world.SeedPopulation(Population);
        for (int i = 0; i < world.Config.DaysPerYear; i++) world.Step();
        GD.Print($"SimProbe: {world.Calendar}, 인구 {world.People.AliveCount}, 사망 {world.DeathsTotal}");
    }
}
