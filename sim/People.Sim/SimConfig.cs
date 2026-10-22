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
    public float ConceptionPerAct { get; init; } = 0.04f;    // 부부 밖 행위 한 번의 기본 임신 확률(몸·형편 계수를 곱한다)
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
    // 3단계: 일과 배움
    public float LearnRate { get; init; } = 0.1f;            // 혼자 연습: 해마다 (재능 높이 − 지금) × 이 값 × 나이·재능 계수
    public float TeachRate { get; init; } = 0.25f;           // 스승 곁: 해마다 (스승 − 지금) × 이 값 × 나이 계수
    public float SkillRust { get; init; } = 0.03f;           // 손 놓은 기술이 해마다 녹는 비율
    public float JobFarmShare { get; init; } = 0.4f;         // 일을 맡은 사람이 밭일에 쓰는 몫
    public float HealerFee { get; init; } = 3f;              // 아이 하나 받을 때 약초꾼 사례(식량)
    public float HealerEffect { get; init; } = 0.3f;         // 아기·산모 사망 × (1 − 이 값 × 약초 솜씨)
    public float TradeSaving { get; init; } = 0.6f;          // 썩는 양 × (1 − 이 값 × 장사 솜씨)
    public float TradeFee { get; init; } = 0.3f;             // 아낀 양 중 장사꾼 몫
    public float FarmSkillEffect { get; init; } = 0.5f;      // 수확 × (1 + 이 값 × (가구 농사 솜씨 − 기준))
    public float FarmSkillBaseline { get; init; } = 0.68f;   // 보통 농가의 솜씨(이때 수확이 기술 넣기 전과 같다)
    // 4단계: 소작, 품삯, 빚
    public float LeaseKeep { get; init; } = 1.6f;            // 땅 주인은 일손 × 필지 × 이 배수(쉬는 땅 포함)만 남기고 나머지를 내놓는다
    public float TenantWant { get; init; } = 1.6f;           // 일손 × 필지 × 이 배수보다 땅이 적으면 빌린다
    public float RentBase { get; init; } = 0.2f;             // 소작료(수확 몫) = 기준 + 기울기 × (빌리려는 양/내놓은 양 − 1)
    public float RentSlope { get; init; } = 0.2f;
    public float RentMin { get; init; } = 0.1f;
    public float RentMax { get; init; } = 0.4f;
    public float WageBase { get; init; } = 0.45f;            // 품삯 = 한 사람 한 해 수확 × (기준 + 기울기 × (수요/공급 − 1))
    public float WageSlope { get; init; } = 0.2f;
    public float WageMin { get; init; } = 0.3f;
    public float WageMax { get; init; } = 0.75f;
    public float DistressLevel { get; init; } = 0.4f;        // 수확까지 먹을 양의 이 몫보다 식량이 적으면 빌리거나 판다
    public float DistressCover { get; init; } = 0.7f;        // 이 몫까지 채우려 한다
    public float LoanInterest { get; init; } = 0.25f;
    public int ForecloseYears { get; init; } = 2;            // 두 수확 넘게 못 갚으면 밭으로 받는다
    public float DistressPrice { get; init; } = 0.6f;        // 급히 파는 땅값 = 땅 가치 × 이 값
    public float StayLandless { get; init; } = 0.35f;        // 마을 땅이 찼을 때 떠나지 않고 땅 없이 남는 분가의 몫
    public int StayLeaseSlack { get; init; } = 3;            // 빌릴 땅이 이만큼 남아 있으면 분가해도 마을에 남는다
    // 5단계: 약탈, 보호, 공납
    public float RaidAggression { get; init; } = 0.55f;      // 가장의 공격성이 이보다 높아야 약탈을 생각한다
    public float RaidRange { get; init; } = 120f;            // 약탈 거리(타일, 약 1 km)
    public float RaidDeath { get; init; } = 0.3f;            // 진 쪽에서 한 사람이 죽을 확률
    public float DangerKeep { get; init; } = 0.6f;           // 마을 위험이 해마다 남는 비율
    public float DangerThreshold { get; init; } = 0.8f;      // 위험이 이보다 크면 보호를 찾는다
    public float LordMinPower { get; init; } = 1.5f;         // 보호해 줄 수 있는 집의 싸움 힘(남자 서너 명)
    public float ProtectRange { get; init; } = 160f;         // 보호 거리(타일)
    public float ProtectShare { get; init; } = 0.4f;         // 약탈 때 지배자 힘 중 한 집을 지키러 오는 몫
    public float TributeRate { get; init; } = 0.12f;         // 공납: 수확의 몫
    public float SoldierWage { get; init; } = 30f;           // 군인 한 해 품삯(식량)
    public int CollectorSpanBase { get; init; } = 10;        // 징수관 하나가 맡는 집(글을 알면 +15까지)
    public float CollectorCut { get; init; } = 0.1f;         // 징수관 몫(욕심만큼 더 떼어 먹는다)
    public int RulerClients { get; init; } = 5;              // 따르는 집이 이만큼이면 무리(1단계)
    public int ManorBelow { get; init; } = 15;               // 아래 집이 이만큼이면 영주 저택을 짓는다(영주 단계의 거처)
    public int LordSoldiers { get; init; } = 5;              // 영주 실력 문턱: 군인 수(또는 지킬 힘)
    public float LordGuardPower { get; init; } = 6f;         // 군인이 모자라도 집안 장정과 군인 힘이 이만큼이면 지킬 수 있다
    public float VassalRate { get; init; } = 0.15f;          // 작은 지배자가 해마다 큰 지배자를 섬기려 할 확률(욕심·위험으로 바뀐다)
    public float VassalRange { get; init; } = 320f;          // 섬길 주군을 찾는 거리(타일)
    public float UpshareRate { get; init; } = 0.3f;          // 받은 공납 중 자기 주군에게 올리는 몫
    public float HeirLoss { get; init; } = 0.35f;            // 다른 집이 지배권을 이을 때 따르는 집이 떠날 확률(말솜씨로 줄어든다)
    public float HeadChangeLoss { get; init; } = 0.15f;      // 같은 집 안에서 가장이 바뀔 때
    public int TowerClients { get; init; } = 80;             // 아래 집이 이만큼이면 망루 달린 저택
    public int CastleClients { get; init; } = 300;           // 아래 집이 이만큼이면 성
    public float ManorCost { get; init; } = 200f;            // 영주 저택 짓는 값(식량)
    public float TowerCost { get; init; } = 800f;
    public float CastleCost { get; init; } = 2500f;
    // 바탕 A: 관계, 비밀, 상호작용
    public float SocialRate { get; init; } = 0.25f;          // 계절마다 어른이 상호작용을 하나 고를 확률
    public float RelationKeep { get; init; } = 0.85f;        // 관계가 해마다 남는 비율
    public float AffairDrive { get; init; } = 40f;         // 몰래 만나기를 고르는 기본 무게
    public float AffairAcceptMid { get; init; } = 0.25f;     // 짝 점수가 이쯤이면 반쯤 받아들인다(혼인 안 한 쪽 기준)
    public float AffairAttractFloor { get; init; } = 0.1f;   // 짝 점수가 이보다 높아야 끌린다
    public int AffairActs { get; init; } = 4;                // 몰래 만나기 한 번(한 계절)에 든 행위 수
    public float SexTradeDrive { get; init; } = 6f;        // 하룻밤 거래를 고르는 기본 무게
    public float SexTradeBase { get; init; } = 20f;
    public float SexOfferRate { get; init; } = 0.4f;         // 형편이 어려운 사람이 스스로 살 사람을 찾는 정도(모자람 × 이 값)           // 하룻밤 값의 기준(식량)
    public float MaleAppealFloor { get; init; } = 0.8f;      // 남자가 팔면 외모가 이보다 높을 때만 값이 생긴다
    public float SecretBaseRisk { get; init; } = 0.12f;      // 비밀이 해마다 들킬 기본 확률
    public int SecretForgetYears { get; init; } = 15;        // 이만큼 지난 비밀은 묻힌다
    public float ArrangeWealth { get; init; } = 600f;
    // C 묶음: 첩, 약초꾼의 약, 몸 파는 일
    public int NpcNations { get; init; } = 1;                // NPC가 나라를 세우나(0이면 아니다, 시험용)
    public int NursingDays { get; init; } = 60;              // 오래 젖 먹이기로 늘어나는 터울(R12)
    public int NursingChildren { get; init; } = 6;           // 아이가 이만큼이면 오래 젖을 먹인다
    public int SpacingChildren { get; init; } = 5;           // 아이가 이만큼이고 형편이 모자라면 부부가 터울을 둔다
    public float WithdrawalCut { get; init; } = 0.4f;        // 질외사정이 줄이는 몫
    public float ContraEffect { get; init; } = 0.25f;        // 피임 약초가 임신 확률을 줄이는 몫(약초라 완전하지 않다, R12)
    public float ContraPrice { get; init; } = 5f;           // 한 해 치 값(식량)
    public float AbortionPrice { get; init; } = 15f;
    public float AbortionWorks { get; init; } = 0.7f;       // 낙태 약이 듣는 확률
    public float AbortionDeath { get; init; } = 0.06f;      // 약을 먹은 여자가 죽을 확률
    public int TradeVillage { get; init; } = 30;            // 이만큼 큰 마을에서 몸 파는 일이 업으로 생긴다(25집에 한 사람까지)        // 이만큼 가진 집(과 지배자)은 자식을 혼담으로 맺으려 한다
    // 충성: 관계가 충성을 밀고, 충성이 관계를 받아들이는 눈을 바꾼다
    public float LoyaltyScale { get; init; } = 60f;          // 이만큼의 관계를 "아주 좋다"로 본다
    public float LoyaltyLinear { get; init; } = 0.03f;       // 충성 변화 = (선형 × x + 세제곱 × x³), x = 관계/LoyaltyScale(±1에서 자름)
    public float LoyaltyCubic { get; init; } = 0.25f;
    public float FanaticAt { get; init; } = 0.9f;            // 충성이 이만큼이면 광신
    public float FanaticBreak { get; init; } = 80f;          // 광신을 깨는 의심(대상에게 당한 나쁜 일의 합)
    public float DoubtKeep { get; init; } = 0.8f;            // 의심이 해마다 남는 비율
    public float FanaticFallTo { get; init; } = 0.3f;        // 광신이 깨지면 충성이 여기로 떨어진다
    public int ScrubYears { get; init; } = 12;              // 버려진 밭이 이만큼 묵으면 덤불: 다시 부치려면 개간 한 해
    public int ForestYears { get; init; } = 35;             // 이만큼 묵으면 숲으로 돌아간다
    public float HouseBuildCost { get; init; } = 30f;        // 오두막 하나 짓는 값(식량). 본가가 낸다
    public float VacantPriceFactor { get; init; } = 0.6f;     // 빈집 값 = 짓는 값 × 크기 비율 × 상태 × 이 값
    public float VacantMinCondition { get; init; } = 0.3f;    // 이보다 낡은 빈집은 사지 않는다
    public float VacantLifeYears { get; init; } = 30f;        // 빈집이 무너지기까지 걸리는 해
    public float HouseUpgradeCost { get; init; } = 40f;

    public static SimConfig Default { get; } = new();
}
