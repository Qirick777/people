namespace People.Sim;

/// <summary>
/// 시뮬레이션 전체 상태와 하루 진행. Godot 에 의존하지 않는다.
/// 지금은 0단계 뼈대: 사람 저장소, 나이, 사망. 번식·식량은 1단계에서 붙인다.
/// </summary>
public sealed class World
{
    public SimConfig Config { get; }
    public PersonStore People { get; } = new();
    public Mortality Mortality { get; }
    public int Day { get; private set; }
    public Calendar Calendar => new(Day, Config);

    private Rng _rng;
    public int DeathsTotal { get; private set; }

    public World(ulong seed, SimConfig? config = null)
    {
        Config = config ?? SimConfig.Default;
        Mortality = new Mortality(Config);
        _rng = new Rng(seed);
    }

    /// <summary>시작 인구를 무작위 유전체로 만든다. 나이는 0~maxAge 고르게.</summary>
    public void SeedPopulation(int count, int maxAgeYears = 50)
    {
        for (int k = 0; k < count; k++)
        {
            bool male = _rng.NextBool();
            int birth = Day - _rng.NextInt(maxAgeYears * Config.DaysPerYear + 1);
            var id = People.Add(male, birth, PersonId.None, PersonId.None, household: -1);
            Genetics.Randomize(People.Genome(id.Index), male, ref _rng);
            People.ComputePhenotype(id.Index);
        }
    }

    /// <summary>두 사람의 아이를 만든다.</summary>
    public PersonId Birth(PersonId father, PersonId mother, int household)
    {
        bool male = _rng.NextBool();
        var child = People.Add(male, Day, father, mother, household);
        Genetics.Inherit(People.Genome(father.Index), People.Genome(mother.Index), People.Genome(child.Index),
            male, Config.MutationRate, ref _rng);
        People.ComputePhenotype(child.Index);
        return child;
    }

    /// <summary>하루 진행.</summary>
    public void Step()
    {
        var p = People;
        int dpy = Config.DaysPerYear;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            int age = (Day - p.BirthDay[i]) / dpy;
            float d = Mortality.Daily(age, p.Get(i, Trait.Longevity), p.Get(i, Trait.Constitution));
            if (_rng.Chance(d))
            {
                p.Remove(i);
                DeathsTotal++;
            }
        }
        Day++;
    }
}
