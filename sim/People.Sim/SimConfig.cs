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
    public float ConceptionBasePerDay { get; init; } = 0.045f;
    public int PregnancyDays { get; init; } = 18;
    public int PostpartumCooldownDays { get; init; } = 24;
    public float TwinBase { get; init; } = 0.01f;
    public float TwinWithTrait { get; init; } = 0.06f;
    public float TripletRate { get; init; } = 0.001f;
    public float InbreedingDepression { get; init; } = 2.5f;   // 표현형 × (1 − 이 값 × F). 사촌 자녀 F=1/16 → −16%
    // 다산 특성: 쿨타임 × (CooldownFertBase − CooldownFertSlope × 다산), 쌍둥이 확률 × (0.5 + 다산)
    public float CooldownFertBase { get; init; } = 1.3f;
    public float CooldownFertSlope { get; init; } = 0.6f;

    public float HungerMortality { get; init; } = 3f;           // 굶은 날 사망률 배수
    public float HungerMortalityVulnerable { get; init; } = 5f; // 아기·노인
    public float CrowdingMortality { get; init; } = 0.3f;     // 북적임 1 초과분당 사망률 증가
    public float StillbirthBase { get; init; } = 0.04f;
    public float MaternalDeathBase { get; init; } = 0.015f;

    // 3. 짝짓기
    public int CandidateSample { get; init; } = 16;
    public float KinForbidden { get; init; } = 0.13f;       // r > 1/8 이면 불가
    public float AcceptMidpoint { get; init; } = 0.4f;
    public float AcceptSpread { get; init; } = 0.15f;
    public float PolygynyWealthPerWife { get; init; } = 300f;
    public float PreferenceStrength { get; init; } = 1.5f;   // 선호(평균과의 차이) 항의 세기
    public float WifePenalty { get; init; } = 0.35f;
    public float KinPenalty { get; init; } = 2f;

    // 5. 식량과 땅
    public float PlotYield { get; init; } = 14f;
    public float PlotsPerWorker { get; init; } = 3.5f;
    public float LandReserve { get; init; } = 1.6f;          // 일손보다 넉넉히 잡는 땅 배수
    public float LandRelease { get; init; } = 2.2f;          // 이 배수를 넘는 땅은 내놓는다
    public float FertilityDeclinePerYear { get; init; } = 0.08f;
    public float FertilityRecoverPerYear { get; init; } = 0.15f;
    public float SpoilagePerSeason { get; init; } = 0.08f;
    public float WeatherSd { get; init; } = 0.15f;
    public float WeatherCorrelation { get; init; } = 0.4f;
    public int HarvestDayOfYear { get; init; } = 17;        // 가을 마지막 날

    // 2단계: 공간
    public int FarmRadius { get; init; } = 24;              // 마을 중심에서 이 거리(타일, 약 200 m) 안의 땅만 부친다
    public int HouseRadius { get; init; } = 4;              // 집은 마을 중심에서 이 거리(+ 0.7×√가구 수) 안에 모인다
    public int MigrationRadius { get; init; } = 160;        // 이주할 곳을 찾는 최대 거리(약 1.3 km)
    public int NewVillageMinDist { get; init; } = 56;       // 새 마을은 다른 마을 중심에서 이만큼 떨어져야 한다
    public float SwapTolerance { get; init; } = 0.08f;     // 이웃끼리 필지를 맞바꿀 때 허용하는 비옥도 차이
    public int MinFreePlotsToStay { get; init; } = 25;      // 마을에 빈 필지가 이보다 적으면 떠난다(새 가구가 자라며 쓸 땅까지 본다)
    public float FloodChance { get; init; } = 0.05f;        // 강이 흐르는 대략 칸마다 해마다
    public int CourtshipBucket { get; init; } = 128;        // 구애 상대를 찾는 지역 칸 크기(타일). 주변 3×3 칸

    // 6. 가구
    public int HutCapacity { get; init; } = 4;
    public int MaxCapacity { get; init; } = 20;
    public float HouseUpgradeCost { get; init; } = 40f;

    public static SimConfig Default { get; } = new();
}
