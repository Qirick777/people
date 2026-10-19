namespace People.Sim;

/// <summary>
/// 사람 참조. 번호(Index)와 세대 카운터(Gen)를 같이 들고 있어서,
/// 그 칸이 다른 사람에게 재사용되면 옛 참조는 무효가 된다.
/// </summary>
public readonly record struct PersonId(int Index, int Gen)
{
    public static readonly PersonId None = new(-1, 0);
    public bool IsNone => Index < 0;
}

/// <summary>
/// 살아 있는 사람을 속성별 배열(SoA)로 저장한다. 노드나 객체를 쓰지 않는다.
/// 죽은 사람은 칸을 비우고, 필요한 기록은 따로 남긴다(족보 저장소는 다음 단계).
/// </summary>
public sealed class PersonStore
{
    public int Capacity { get; private set; }
    public int AliveCount { get; private set; }

    // 칸 관리
    private int[] _gen;
    private bool[] _alive;
    private readonly Stack<int> _free = new();
    private int _highWater;

    // 기본 속성
    public bool[] Male;
    public int[] BirthDay;
    public PersonId[] Father;
    public PersonId[] Mother;
    public int[] Household;
    public int[] HhNext, HhPrev;     // 같은 가구 구성원 연결 목록 (World 가 관리)
    public byte[] Genomes;           // Capacity × BytesPerGenome
    public float[] Phenotype;        // Capacity × Trait.Count
    public ushort[] ChildhoodHungryDays;
    public float[] Inbreeding;       // 근교 계수 F = 부모 혈연 / 2
    public byte[] LoadInfant;        // 드러난 유전 부하 중 아기 허약 자리 수
    public byte[] LoadHits;          // 드러난 유전 부하 자리 수(통계·화면용)
    public int[] RaisedBy;
    public float[] Skills;           // Capacity × Skill.Count, 0~1. 연습으로 오르고 손 놓으면 녹슨다
    public byte[] JobOf;             // Job
    public PersonId[] Master;        // 도제로 배우는 스승(없으면 None)
    public byte[] Experience;        // 경험 태그 비트(ExperienceTag)

    // 관계: 사람마다 RelSlots 칸. 대상 uid(-1 = 빈 칸), 호감, 두려움(존경 포함), 은혜(+)·빚(-)
    public const int RelSlots = 24;
    public int[] RelUid;
    public float[] RelAff, RelFear, RelDebt;
    // 기억: 사람마다 MemSlots 칸을 돌려 쓴다. 종류, 상대 uid, 날짜, 무게(-100~100)
    public const int MemSlots = 16;
    public byte[] MemType;
    public int[] MemOther, MemDay;
    public sbyte[] MemWeight;
    public byte[] MemNext;
    // 배워서 생기는 가치와 평판(유전되지 않는다)
    public float[] Piety, Loyalty, Honor, Notoriety, Stigma;
    // 충성 대상: 대상 가문, 그 가문의 지금 가장 uid, 광신 여부, 광신을 흔드는 의심(쌓이면 깨진다)
    public int[] LoyalFam, LoyalUid, LoyalIdx, LoyalSince, LoyalHouse, LoyalHouseFounded;   // LoyalIdx: 섬기는 가장의 사람 번호(LoyalUid 와 맞을 때만 유효)
    public byte[] Fanatic;
    public bool[] Concubine;         // 여성: 남편의 첩(아내가 아님). 자식은 서자
    public int[] ContraUntil;        // 피임약이 듣는 마지막 날(-1 = 없음)
    public float[] Doubt;
    public int[] PregnancyLegalFatherUid;   // 족보에 아버지로 적힐 사람(-1 = 사생아). 유전체는 진짜 아버지 것           // 6살 때 가구 주인의 uid. 같은 사람 밑에서 비슷한 나이로 자라면 서로 끌리지 않는다

    // 족보 번호 (Lineage)
    public int[] Uid;

    // 혼인
    public PersonId[] Husband;       // 여성: 남편. 남성은 None
    public byte[] WifeCount;         // 남성: 아내 수
    public float[] CoupleScore;      // 여성: 혼인할 때 두 사람이 서로 매긴 점수 평균
    public int[] RejectUid0, RejectUid1, RejectUntil0, RejectUntil1;

    // 번식 상태
    public int[] PregnantUntil;      // 출산 예정일, 아니면 -1
    public int[] CooldownUntil;
    public PersonId[] PregnancyFather;
    public int[] PregnancyFatherUid;
    public byte[] PregnancyFatherGenome; // 임신할 때 아버지 유전체를 복사해 둔다(아버지가 먼저 죽어도 된다)
    public byte[] BirthsGiven;

    // 상태
    public bool[] HungryToday;
    public bool[] GrownUp;           // 16세 처리(어린 시절 굶주림 반영)를 했는가

    public PersonStore(int capacity = 1024)
    {
        Capacity = capacity;
        _gen = new int[capacity];
        _alive = new bool[capacity];
        Male = new bool[capacity];
        BirthDay = new int[capacity];
        Father = new PersonId[capacity];
        Mother = new PersonId[capacity];
        Household = new int[capacity];
        HhNext = new int[capacity];
        HhPrev = new int[capacity];
        Genomes = new byte[capacity * GenomeLayout.BytesPerGenome];
        Phenotype = new float[capacity * (int)Trait.Count];
        ChildhoodHungryDays = new ushort[capacity];
        Inbreeding = new float[capacity];
        LoadInfant = new byte[capacity];
        LoadHits = new byte[capacity];
        RaisedBy = new int[capacity];
        Skills = new float[capacity * (int)Skill.Count];
        JobOf = new byte[capacity];
        Master = new PersonId[capacity];
        Experience = new byte[capacity];
        RelUid = new int[capacity * RelSlots]; Array.Fill(RelUid, -1);
        RelAff = new float[capacity * RelSlots]; RelFear = new float[capacity * RelSlots]; RelDebt = new float[capacity * RelSlots];
        MemType = new byte[capacity * MemSlots]; MemOther = new int[capacity * MemSlots]; MemDay = new int[capacity * MemSlots];
        MemWeight = new sbyte[capacity * MemSlots]; MemNext = new byte[capacity];
        Piety = new float[capacity]; Loyalty = new float[capacity]; Honor = new float[capacity];
        Notoriety = new float[capacity]; Stigma = new float[capacity];
        Concubine = new bool[capacity]; ContraUntil = new int[capacity];
        LoyalFam = new int[capacity]; LoyalUid = new int[capacity]; LoyalIdx = new int[capacity]; LoyalSince = new int[capacity]; LoyalHouse = new int[capacity]; LoyalHouseFounded = new int[capacity]; Fanatic = new byte[capacity]; Doubt = new float[capacity];
        PregnancyLegalFatherUid = new int[capacity];
        Uid = new int[capacity];
        Husband = new PersonId[capacity];
        WifeCount = new byte[capacity];
        CoupleScore = new float[capacity];
        RejectUid0 = new int[capacity]; RejectUid1 = new int[capacity];
        RejectUntil0 = new int[capacity]; RejectUntil1 = new int[capacity];
        PregnantUntil = new int[capacity];
        CooldownUntil = new int[capacity];
        PregnancyFather = new PersonId[capacity];
        PregnancyFatherUid = new int[capacity];
        PregnancyFatherGenome = new byte[capacity * GenomeLayout.BytesPerGenome];
        BirthsGiven = new byte[capacity];
        HungryToday = new bool[capacity];
        GrownUp = new bool[capacity];
    }

    public Span<byte> FatherGenomeBuffer(int i) =>
        PregnancyFatherGenome.AsSpan(i * GenomeLayout.BytesPerGenome, GenomeLayout.BytesPerGenome);

    public int HighWater => _highWater;
    public bool IsAliveIndex(int i) => _alive[i];
    public bool IsValid(PersonId id) => !id.IsNone && id.Index < _highWater && _alive[id.Index] && _gen[id.Index] == id.Gen;
    public PersonId IdAt(int i) => new(i, _gen[i]);

    public Span<byte> Genome(int i) => Genomes.AsSpan(i * GenomeLayout.BytesPerGenome, GenomeLayout.BytesPerGenome);
    public float GetSkill(int i, Skill s) => Skills[i * (int)Skill.Count + (int)s];
    public void SetSkill(int i, Skill s, float v) => Skills[i * (int)Skill.Count + (int)s] = Math.Clamp(v, 0f, 1f);
    public float Get(int i, Trait t) => Phenotype[i * (int)Trait.Count + (int)t];
    public void Set(int i, Trait t, float v) => Phenotype[i * (int)Trait.Count + (int)t] = v;

    public PersonId Add(bool male, int birthDay, PersonId father, PersonId mother, int household)
    {
        int i;
        if (_free.Count > 0) i = _free.Pop();
        else
        {
            if (_highWater == Capacity) Grow(Capacity * 2);
            i = _highWater++;
        }
        _gen[i]++;
        _alive[i] = true;
        Male[i] = male;
        BirthDay[i] = birthDay;
        Father[i] = father;
        Mother[i] = mother;
        Household[i] = -1;          // World.Join 으로 넣는다
        HhNext[i] = HhPrev[i] = -1;
        ChildhoodHungryDays[i] = 0;
        Inbreeding[i] = 0f;
        LoadInfant[i] = LoadHits[i] = 0;
        RaisedBy[i] = -1;
        Skills.AsSpan(i * (int)Skill.Count, (int)Skill.Count).Clear();
        JobOf[i] = 0;
        Master[i] = PersonId.None;
        Experience[i] = 0;
        RelUid.AsSpan(i * RelSlots, RelSlots).Fill(-1);
        RelAff.AsSpan(i * RelSlots, RelSlots).Clear(); RelFear.AsSpan(i * RelSlots, RelSlots).Clear(); RelDebt.AsSpan(i * RelSlots, RelSlots).Clear();
        MemType.AsSpan(i * MemSlots, MemSlots).Clear(); MemNext[i] = 0;
        Piety[i] = 0.5f; Loyalty[i] = 0.5f; Honor[i] = 0f; Notoriety[i] = 0f; Stigma[i] = 0f;
        Concubine[i] = false; ContraUntil[i] = -1;
        LoyalFam[i] = -1; LoyalUid[i] = -1; LoyalIdx[i] = -1; LoyalHouse[i] = -1; Fanatic[i] = 0; Doubt[i] = 0f;
        PregnancyLegalFatherUid[i] = -1;
        Uid[i] = -1;
        Husband[i] = PersonId.None;
        WifeCount[i] = 0;
        CoupleScore[i] = 0f;
        RejectUid0[i] = RejectUid1[i] = -1;
        RejectUntil0[i] = RejectUntil1[i] = -1;
        PregnantUntil[i] = -1;
        CooldownUntil[i] = -1;
        PregnancyFather[i] = PersonId.None;
        PregnancyFatherUid[i] = -1;
        BirthsGiven[i] = 0;
        HungryToday[i] = false;
        GrownUp[i] = false;
        AliveCount++;
        return new PersonId(i, _gen[i]);
    }

    public void Remove(int i)
    {
        if (!_alive[i]) return;
        _alive[i] = false;
        _free.Push(i);
        AliveCount--;
    }

    /// <summary>유전체, 근교 계수, 유전병으로 표현형을 계산한다 (spec 1.2, 1.3).</summary>
    public void ComputePhenotype(int i, float inbreedingDepression = 2.5f)
    {
        var g = Genome(i);
        bool male = Male[i];
        float depress = 1f - inbreedingDepression * Inbreeding[i];
        for (int t = 0; t < (int)Trait.Count; t++)
        {
            var trait = (Trait)t;
            float v = Genetics.Score(g, trait) * Genetics.SexFactor(trait, male);
            if (Genetics.AffectedByInbreeding(trait)) v *= depress;
            Set(i, trait, v);
        }
        // 한 유전자가 여러 곳에 걸친다: 다산 유전자는 수명을 조금 깎는다.
        Set(i, Trait.Longevity, Get(i, Trait.Longevity) * (1.1f - 0.2f * Genetics.Score(g, Trait.Fertility)));
        Span<int> load = stackalloc int[4];
        int hits = Genetics.LoadHits(g, load);
        LoadHits[i] = (byte)hits;
        LoadInfant[i] = (byte)load[0];
        if (load[1] > 0) Set(i, Trait.Constitution, Get(i, Trait.Constitution) * MathF.Pow(0.75f, load[1]));
        if (load[2] > 0) Set(i, Trait.Fertility, Get(i, Trait.Fertility) * MathF.Pow(0.6f, load[2]));
        if (load[3] > 0) Set(i, Trait.Longevity, Get(i, Trait.Longevity) * MathF.Pow(0.75f, load[3]));
        if (Genetics.Expressed(g, Marker.Frailty, male)) Set(i, Trait.Constitution, Get(i, Trait.Constitution) * 0.6f);
        if (Genetics.Expressed(g, Marker.ShortLife, male)) Set(i, Trait.Longevity, Get(i, Trait.Longevity) * 0.5f);
    }

    /// <summary>16세가 될 때 어린 시절 굶주림을 반영한다.</summary>
    public void ApplyChildhoodHunger(int i, int daysPerYear)
    {
        float fraction = ChildhoodHungryDays[i] / (float)(16 * daysPerYear);
        float k = 1f - 0.3f * Math.Clamp(fraction, 0f, 1f);
        for (int t = 0; t < (int)Trait.Count; t++)
        {
            if (Genetics.AffectedByChildhoodHunger((Trait)t)) Set(i, (Trait)t, Get(i, (Trait)t) * k);
        }
    }

    private void Grow(int cap)
    {
        Array.Resize(ref _gen, cap);
        Array.Resize(ref _alive, cap);
        Array.Resize(ref Male, cap);
        Array.Resize(ref BirthDay, cap);
        Array.Resize(ref Father, cap);
        Array.Resize(ref Mother, cap);
        Array.Resize(ref Household, cap);
        Array.Resize(ref HhNext, cap);
        Array.Resize(ref HhPrev, cap);
        Array.Resize(ref Genomes, cap * GenomeLayout.BytesPerGenome);
        Array.Resize(ref Phenotype, cap * (int)Trait.Count);
        Array.Resize(ref ChildhoodHungryDays, cap);
        Array.Resize(ref Inbreeding, cap);
        Array.Resize(ref LoadInfant, cap);
        Array.Resize(ref LoadHits, cap);
        Array.Resize(ref RaisedBy, cap);
        Array.Resize(ref Skills, cap * (int)Skill.Count);
        Array.Resize(ref JobOf, cap);
        Array.Resize(ref Master, cap);
        Array.Resize(ref Experience, cap);
        int oldRel = RelUid.Length;
        Array.Resize(ref RelUid, cap * RelSlots); RelUid.AsSpan(oldRel).Fill(-1);
        Array.Resize(ref RelAff, cap * RelSlots); Array.Resize(ref RelFear, cap * RelSlots); Array.Resize(ref RelDebt, cap * RelSlots);
        Array.Resize(ref MemType, cap * MemSlots); Array.Resize(ref MemOther, cap * MemSlots); Array.Resize(ref MemDay, cap * MemSlots);
        Array.Resize(ref MemWeight, cap * MemSlots); Array.Resize(ref MemNext, cap);
        Array.Resize(ref Piety, cap); Array.Resize(ref Loyalty, cap); Array.Resize(ref Honor, cap);
        Array.Resize(ref Notoriety, cap); Array.Resize(ref Stigma, cap);
        Array.Resize(ref Concubine, cap); Array.Resize(ref ContraUntil, cap);
        Array.Resize(ref LoyalFam, cap); Array.Resize(ref LoyalUid, cap); Array.Resize(ref LoyalIdx, cap); Array.Resize(ref LoyalSince, cap); Array.Resize(ref LoyalHouse, cap); Array.Resize(ref LoyalHouseFounded, cap); Array.Resize(ref Fanatic, cap); Array.Resize(ref Doubt, cap);
        Array.Resize(ref PregnancyLegalFatherUid, cap);
        Array.Resize(ref Uid, cap);
        Array.Resize(ref Husband, cap);
        Array.Resize(ref WifeCount, cap);
        Array.Resize(ref CoupleScore, cap);
        Array.Resize(ref RejectUid0, cap); Array.Resize(ref RejectUid1, cap);
        Array.Resize(ref RejectUntil0, cap); Array.Resize(ref RejectUntil1, cap);
        Array.Resize(ref PregnantUntil, cap);
        Array.Resize(ref CooldownUntil, cap);
        Array.Resize(ref PregnancyFather, cap);
        Array.Resize(ref PregnancyFatherUid, cap);
        Array.Resize(ref PregnancyFatherGenome, cap * GenomeLayout.BytesPerGenome);
        Array.Resize(ref BirthsGiven, cap);
        Array.Resize(ref HungryToday, cap);
        Array.Resize(ref GrownUp, cap);
        Capacity = cap;
    }
}
