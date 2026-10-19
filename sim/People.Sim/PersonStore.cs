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
    public byte[] Experience;        // 경험 태그 비트(ExperienceTag)           // 6살 때 가구 주인의 uid. 같은 사람 밑에서 비슷한 나이로 자라면 서로 끌리지 않는다

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
