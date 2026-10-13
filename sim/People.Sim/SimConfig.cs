namespace People.Sim;

/// <summary>
/// 조정 가능한 수치를 한곳에 모은다. 값과 근거는 docs/spec.md 를 따른다.
/// </summary>
public sealed class SimConfig
{
    // 0. 시간
    public int DaysPerSeason { get; init; } = 6;
    public int SeasonsPerYear { get; init; } = 4;
    public int DaysPerYear => DaysPerSeason * SeasonsPerYear;

    // 1. 유전
    public float MutationRate { get; init; } = 0.002f;

    // 2. 사망률 (연간, 나이 구간별). 매일 1/DaysPerYear 씩 판정한다.
    public float MortalityInfant { get; init; } = 0.08f;     // 0~1세
    public float MortalityToddler { get; init; } = 0.03f;    // 2~4세
    public float MortalityChild { get; init; } = 0.008f;     // 5~15세
    public float MortalityAdult { get; init; } = 0.007f;     // 16~39세
    public float MortalityAgingBase { get; init; } = 1.085f; // 40세 이후 0.7% × 1.085^(나이-40)

    // 4. 임신
    public float ConceptionBasePerDay { get; init; } = 0.05f;
    public int PregnancyDays { get; init; } = 18;
    public int PostpartumCooldownDays { get; init; } = 24;
    public float TwinBase { get; init; } = 0.01f;
    public float TwinWithTrait { get; init; } = 0.06f;
    public float TripletRate { get; init; } = 0.001f;

    public static SimConfig Default { get; } = new();
}
