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
    public byte[] Genomes;           // Capacity × BytesPerGenome
    public float[] Phenotype;        // Capacity × Trait.Count
    public ushort[] ChildhoodHungryDays;

    // 번식 상태
    public int[] PregnantUntil;      // 출산 예정일, 아니면 -1
    public int[] CooldownUntil;
    public PersonId[] PregnancyFather;

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
        Genomes = new byte[capacity * GenomeLayout.BytesPerGenome];
        Phenotype = new float[capacity * (int)Trait.Count];
        ChildhoodHungryDays = new ushort[capacity];
        PregnantUntil = new int[capacity];
        CooldownUntil = new int[capacity];
        PregnancyFather = new PersonId[capacity];
    }

    public int HighWater => _highWater;
    public bool IsAliveIndex(int i) => _alive[i];
    public bool IsValid(PersonId id) => !id.IsNone && id.Index < _highWater && _alive[id.Index] && _gen[id.Index] == id.Gen;
    public PersonId IdAt(int i) => new(i, _gen[i]);

    public Span<byte> Genome(int i) => Genomes.AsSpan(i * GenomeLayout.BytesPerGenome, GenomeLayout.BytesPerGenome);
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
        Household[i] = household;
        ChildhoodHungryDays[i] = 0;
        PregnantUntil[i] = -1;
        CooldownUntil[i] = -1;
        PregnancyFather[i] = PersonId.None;
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

    /// <summary>유전체와 성장 환경으로 표현형을 계산한다 (spec 1.2).</summary>
    public void ComputePhenotype(int i)
    {
        var g = Genome(i);
        bool male = Male[i];
        for (int t = 0; t < (int)Trait.Count; t++)
        {
            var trait = (Trait)t;
            float v = Genetics.Score(g, trait) * Genetics.SexFactor(trait, male);
            Set(i, trait, v);
        }
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
        Array.Resize(ref Genomes, cap * GenomeLayout.BytesPerGenome);
        Array.Resize(ref Phenotype, cap * (int)Trait.Count);
        Array.Resize(ref ChildhoodHungryDays, cap);
        Array.Resize(ref PregnantUntil, cap);
        Array.Resize(ref CooldownUntil, cap);
        Array.Resize(ref PregnancyFather, cap);
        Capacity = cap;
    }
}
