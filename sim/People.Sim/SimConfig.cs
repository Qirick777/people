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
    public float InbreedingDepression { get; init; } = 0.5f;   // 작은 효과 유전자들의 근교약세. 표현형 × (1 − 이 값 × F). 큰 손해는 유전 부하(Genetics.LoadLoci)가 낸다
    public float LoadMutationRate { get; init; } = 0.0003f;   // 유전 부하 자리마다 배우자 하나당 새 해로운 변이 확률
    public float LoadInfantMortality { get; init; } = 3f;      // 아기 허약 부하 하나당 5세 전 사망률 배수
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
    public float AcceptMidpoint { get; init; } = 0.4f;
    public float AcceptSpread { get; init; } = 0.15f;
    public float PolygynyWealthPerWife { get; init; } = 300f;
    public float PreferenceStrength { get; init; } = 1.5f;   // 선호(평균과의 차이) 항의 세기
    public float WifePenalty { get; init; } = 0.35f;
    public float CoRaisedPenalty { get; init; } = 3f;       // 함께 자란 사이의 짝 점수 감점(근친 금지 대신)
    public int CoRaisedMaxAgeGap { get; init; } = 8;       // 나이 차가 이 안이어야 함께 자란 것으로 본다

    // 5. 식량과 땅
    public float PlotYield { get; init; } = 28f;              // 필지를 크게 잡아 밭 면적을 줄였다(14 × 2)
    public float PlotsPerWorker { get; init; } = 1.75f;
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
    public float FarmTravel { get; init; } = 26f;           // 마을 중심에서 걸어가는 수고가 이보다 큰 땅은 부치지 않는다(풀밭 1, 길 0.5, 숲 1.8)
    public float StayTravel { get; init; } = 10f;           // 새 가구는 이 수고 안에 빈 땅이 넉넉해야 마을에 남는다(먼 땅은 기존 가구가 넓힐 때만)
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
    // 길: 다니는 만큼 닳아 생기고, 안 다니면 풀에 덮인다
    public int MainStreetLength { get; init; } = 8;          // 마을을 세울 때 계획하는 큰길(중심에서 양쪽으로)
    public float PlannedWear { get; init; } = 12f;           // 계획한 길에 처음 주는 닳음
    public float HomeTripWear { get; init; } = 1f;           // 가구가 해마다 집 → 중심을 다니며 남기는 닳음
    public float FieldTripWear { get; init; } = 1f;          // 밭 → 중심
    public float ContactWear { get; init; } = 2f;            // 마을 사이 왕래 한 번(혼인, 이주)
    public float WearKeep { get; init; } = 0.7f;             // 해마다 남는 닳음 비율(꾸준히 n 만큼 다니면 n/0.3 에 수렴)
    public float WearRoadOn { get; init; } = 4f;             // 이 닳음을 넘으면 길이 생긴다(한 가구만으로는 3.3이라 안 생긴다)
    public float WearRoadOff { get; init; } = 1.5f;          // 이 아래로 내려가면 길이 사라진다
    public float WearMajor { get; init; } = 25f;             // 큰길
    public int ScrubYears { get; init; } = 12;              // 버려진 밭이 이만큼 묵으면 덤불: 다시 부치려면 개간 한 해
    public int ForestYears { get; init; } = 35;             // 이만큼 묵으면 숲으로 돌아간다
    public float HouseBuildCost { get; init; } = 30f;        // 오두막 하나 짓는 값(식량). 본가가 낸다
    public float VacantPriceFactor { get; init; } = 0.6f;     // 빈집 값 = 짓는 값 × 크기 비율 × 상태 × 이 값
    public float VacantMinCondition { get; init; } = 0.3f;    // 이보다 낡은 빈집은 사지 않는다
    public float VacantLifeYears { get; init; } = 30f;        // 빈집이 무너지기까지 걸리는 해
    public float HouseUpgradeCost { get; init; } = 40f;

    public static SimConfig Default { get; } = new();
}
