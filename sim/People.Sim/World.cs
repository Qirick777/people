namespace People.Sim;

/// <summary>
/// 시뮬레이션 전체 상태와 하루 진행. Godot 에 의존하지 않는다.
/// 1단계: 사람, 유전, 사망, 짝짓기, 임신과 출산, 가구, 땅, 농사, 굶주림.
/// </summary>
public sealed partial class World
{
    public SimConfig Config { get; }
    public PersonStore People { get; } = new();
    public HouseholdStore Households { get; } = new();
    public Lineage Lineage { get; } = new();
    public VillageStore VillageList { get; } = new();
    public WorldMap Map { get; }
    public Mortality Mortality { get; }
    public YearStats Stats { get; } = new();
    public int Day { get; private set; }
    public Calendar Calendar => new(Day, Config);
    public float Weather { get; private set; } = 1f;

    /// <summary>남성이 죽을 때(40세 이상) 남기는 기록. 재산과 자식 수의 관계를 본다.</summary>
    public List<(float wealth, int children, int maxWives)> MenAtDeath { get; } = new();

    private Rng _rng;

    /// <summary>평평한 섬(필지 plots 개)에서 시작한다. 1단계 실험용.</summary>
    public World(ulong seed, int plots = 400, SimConfig? config = null)
        : this(seed, WorldMap.Flat((uint)seed, plots), config) { }

    public World(ulong seed, WorldMap map, SimConfig? config = null)
    {
        Config = config ?? SimConfig.Default;
        Mortality = new Mortality(Config);
        _rng = new Rng(seed);
        Map = map;
        if (map.Coarse == null) { SpawnX = map.Width / 2; SpawnY = map.Height / 2; }
        else (SpawnX, SpawnY) = ChooseSpawn();
        map.EnsureAround(SpawnX, SpawnY, WorldMap.CS * 2);
    }

    public int SpawnX { get; private set; }
    public int SpawnY { get; private set; }

    /// <summary>시작 자리: 강이 흐르고, 기온이 알맞고, 가운데에 가까운 대략 칸.</summary>
    private (int, int) ChooseSpawn()
    {
        var c = Map.Coarse!;
        int best = -1;
        float bestS = float.MinValue;
        for (int i = 0; i < c.W * c.H; i++)
        {
            if (c.Sea[i] || c.Lake[i]) continue;
            int x = i % c.W, y = i / c.W;
            float land = c.Elevation[i] - c.SeaLevel;
            if (land > 0.2f) continue;
            float dc = MathF.Sqrt(MathF.Pow(x - c.W / 2f, 2) + MathF.Pow(y - c.H / 2f, 2)) / c.W;
            float s = (c.River[i] ? 1f : 0f) - MathF.Abs(c.Temperature[i] - 0.6f) * 2f - dc * 2f + c.Rain[i] * 0.3f;
            if (s > bestS) { bestS = s; best = i; }
        }
        return ((best % c.W) * WorldMap.CS + WorldMap.CS / 2, (best / c.W) * WorldMap.CS + WorldMap.CS / 2);
    }

    // ------------------------------------------------------------------ 가구 구성원 목록

    /// <summary>사람을 가구 구성원 목록에 넣는다. 사람의 가구가 바뀌는 곳은 모두 이 함수와 Leave 를 쓴다.</summary>
    private void Join(int person, int h)
    {
        var p = People;
        p.Household[person] = h;
        if (h < 0) return;
        int first = Households.FirstMember[h];
        p.HhPrev[person] = -1;
        p.HhNext[person] = first;
        if (first >= 0) p.HhPrev[first] = person;
        Households.FirstMember[h] = person;
    }

    private void Leave(int person)
    {
        var p = People;
        int h = p.Household[person];
        if (h < 0) return;
        int prev = p.HhPrev[person], next = p.HhNext[person];
        if (prev >= 0) p.HhNext[prev] = next; else Households.FirstMember[h] = next;
        if (next >= 0) p.HhPrev[next] = prev;
        p.HhPrev[person] = p.HhNext[person] = -1;
        p.Household[person] = -1;
    }

    /// <summary>가구 구성원 번호들(복사본).</summary>
    public List<int> MembersOf(int h)
    {
        var list = new List<int>();
        for (int k = Households.FirstMember[h]; k >= 0; k = People.HhNext[k]) list.Add(k);
        return list;
    }

    // ------------------------------------------------------------------ 만들기

    /// <summary>부모 없는 사람(시작 인구). 유전체는 무작위.</summary>
    public PersonId CreateFounder(bool male, int ageYears, int household)
    {
        int birth = Day - ageYears * Config.DaysPerYear - _rng.NextInt(Config.DaysPerYear);
        var id = People.Add(male, birth, PersonId.None, PersonId.None, household);
        Join(id.Index, household);
        People.Uid[id.Index] = Lineage.Add(male, birth, -1, -1);
        _uidIndex[People.Uid[id.Index]] = id.Index;
        // 시작 가문마다 상속 방식을 정한다(반반). 자손은 가문의 방식을 따른다.
        Lineage.SetPolicy(Lineage.FamilyOf(People.Uid[id.Index]), _rng.NextBool() ? InheritPolicy.Partible : InheritPolicy.Primogeniture);
        Genetics.Randomize(People.Genome(id.Index), male, ref _rng);
        People.ComputePhenotype(id.Index);
        SeedSkills(id.Index, ageYears);
        People.Piety[id.Index] = 0.2f + 0.6f * _rng.NextFloat();   // 시작 인구의 신앙심은 사람마다 다르다
        if (ageYears >= 16) People.GrownUp[id.Index] = true;
        return id;
    }

    // ------------------------------------------------------------------ 가구 위치

    private Dictionary<long, List<int>> _hhGrid = new();
    private const int HhCell = 32;
    private static long Key(int x, int y, int cell) => ((long)(x / cell) << 32) | (uint)(y / cell);

    private void RebuildHouseholdGrid()
    {
        _hhGrid = new Dictionary<long, List<int>>();
        for (int h = 0; h < Households.HighWater; h++) if (Households.IsAlive(h)) GridAdd(h);
    }

    private void GridAdd(int h)
    {
        long k = Key(Households.X[h], Households.Y[h], HhCell);
        if (!_hhGrid.TryGetValue(k, out var l)) _hhGrid[k] = l = new List<int>();
        l.Add(h);
    }

    /// <summary>(x, y) 에서 30 타일 안의 가구 수.</summary>
    private int NeighborsAt(int x, int y)
    {
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!_hhGrid.TryGetValue(Key(x + dx * HhCell, y + dy * HhCell, HhCell), out var l)) continue;
                foreach (int h in l)
                {
                    if (!Households.IsAlive(h)) continue;
                    int ddx = Households.X[h] - x, ddy = Households.Y[h] - y;
                    if (ddx * ddx + ddy * ddy <= 900) n++;
                }
            }
        return n;
    }

    /// <summary>살아 있는 마을 수와 가장 큰 마을의 가구 수.</summary>
    public (int count, int largest) Villages()
    {
        int count = 0, largest = 0;
        for (int v = 0; v < VillageList.Count; v++)
        {
            if (!VillageList.Alive[v]) continue;
            count++;
            largest = Math.Max(largest, VillageList.Households[v]);
        }
        return (count, largest);
    }

    /// <summary>가구 무리(서로 40 타일 안에 이어진 집들). 마을 경계가 실제로 나뉘는지 확인용.</summary>
    public (int count, int largest, int[] villageOf) Clusters()
    {
        var hh = Households;
        int n = hh.HighWater;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        const int link = 40;
        var grid = new Dictionary<long, List<int>>();
        for (int h = 0; h < n; h++)
        {
            if (!hh.IsAlive(h)) continue;
            long k = Key(hh.X[h], hh.Y[h], link);
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
            l.Add(h);
        }
        for (int h = 0; h < n; h++)
        {
            if (!hh.IsAlive(h)) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!grid.TryGetValue(Key(hh.X[h] + dx * link, hh.Y[h] + dy * link, link), out var l)) continue;
                    foreach (int o in l)
                    {
                        if (o <= h) continue;
                        int ddx = hh.X[o] - hh.X[h], ddy = hh.Y[o] - hh.Y[h];
                        if (ddx * ddx + ddy * ddy <= link * link) parent[Find(o)] = Find(h);
                    }
                }
        }
        var size = new Dictionary<int, int>();
        for (int h = 0; h < n; h++) if (hh.IsAlive(h)) { int r = Find(h); size[r] = size.GetValueOrDefault(r) + 1; }
        var villageOf = new int[n];
        for (int h = 0; h < n; h++) villageOf[h] = hh.IsAlive(h) && size[Find(h)] >= 5 ? Find(h) : -1;
        int count = size.Values.Count(v => v >= 5);
        int largest = size.Count > 0 ? size.Values.Max() : 0;
        return (count, largest, villageOf);
    }

    /// <summary>집이 물(강·호수·바다)에서 10 타일(80 m) 안에 있는 가구 비율.</summary>
    public float NearWaterFraction()
    {
        int n = 0, near = 0;
        for (int h = 0; h < Households.HighWater; h++)
        {
            if (!Households.IsAlive(h)) continue;
            n++;
            if (Map.WaterDist(Map.Id(Households.X[h], Households.Y[h])) <= 10) near++;
        }
        return n > 0 ? near / (float)n : 0f;
    }

    /// <summary>가구를 만들고 (x, y) 에 집을 놓는다.</summary>
    private int NewHousehold(int x, int y, float food, int capacity, int village)
    {
        int h = Households.Add(village, food, capacity, Day);
        VillageList.Join(village);
        Households.X[h] = x;
        Households.Y[h] = y;
        Map.MarkHouse(x, y, true);
        GridAdd(h);
        return h;
    }

    /// <summary>빈집. 대가 끊긴 가구의 집은 바로 사라지지 않고 남아 낡는다.</summary>
    public struct VacantHouse
    {
        public int X, Y, Capacity, Village, Heir, HeirFounded, Residence;
        public float Condition;   // 1 = 멀쩡, 0 = 무너짐
        public List<int>? Footprint;   // 큰 집이 차지한 둘레 칸
    }

    private readonly List<VacantHouse> _vacant = new();
    public IReadOnlyList<VacantHouse> Vacant => _vacant;

    /// <summary>가구를 없앤다. 집은 빈집으로 남기고, 팔리면 값은 heir 가구가 받는다.</summary>
    private void RemoveHousehold(int h, int heir = -1, bool keepHouse = false)
    {
        var hh = Households;
        VillageList.Leave(hh.Village[h]);
        Map.Release(h, hh);
        _deadHead.Remove(h);
        var (level, fp) = TakeResidence(h);
        PassGoods(h, heir);
        if (keepHouse) { hh.Remove(h); return; }   // 집(성)은 상속자가 들어가 산다
        if (Layout && hh.Village[h] >= 0)
            _vacant.Add(new VacantHouse
            {
                X = hh.X[h], Y = hh.Y[h], Capacity = hh.Capacity[h], Village = hh.Village[h], Condition = 1f,
                Heir = heir, HeirFounded = heir >= 0 && hh.IsAlive(heir) ? hh.FoundedDay[heir] : -1,
                Residence = level, Footprint = fp,
            });
        else
        {
            Map.MarkHouse(hh.X[h], hh.Y[h], false);
            if (fp != null) foreach (int id in fp) { var (fx, fy) = Map.Pos(id); Map.MarkHouse(fx, fy, false); }
        }
        hh.Remove(h);
    }

    /// <summary>마을 밭 반경(평평한 섬과 집터 고르기용).</summary>
    private int FarmRadiusOf(int village) => Config.FarmRadius;

    // 마을별 걸음 지도. 길이 바뀌면 다음 해에 다시 만든다.
    private readonly Dictionary<int, (WorldMap.Reach reach, int year)> _reach = new();

    private WorldMap.Reach ReachOf(int v)
    {
        var vl = VillageList;
        if (_reach.TryGetValue(v, out var e) && e.reach.CX == vl.CenterX[v] && e.reach.CY == vl.CenterY[v] &&
            (e.reach.RoadVersion == Map.RoadVersion || e.year == Calendar.Year))
            return e.reach;
        var r = Map.BuildReach(vl.CenterX[v], vl.CenterY[v], Config.FarmTravel);
        _reach[v] = (r, Calendar.Year);
        return r;
    }

    /// <summary>가구가 땅을 잡는 중심. 생성된 대륙은 마을 중심, 평평한 섬(1단계 실험)은 자기 집.</summary>
    private (int x, int y) ClaimCenter(int h)
    {
        int v = Households.Village[h];
        if (Map.Coarse == null || v < 0) return (Households.X[h], Households.Y[h]);
        return (VillageList.CenterX[v], VillageList.CenterY[v]);
    }

    /// <summary>(x, y) 주변 radius 안에서 집을 지을 수 있는 가장 가까운 타일.</summary>
    private (int, int) NearestBuildable(int x, int y, int radius)
    {
        for (int r = 0; r <= radius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (Map.Buildable(x + dx, y + dy)) return (x + dx, y + dy);
                }
        return (-1, -1);
    }

    /// <summary>시작 상황: 젊은 부부 couples 쌍, 가구마다 필지와 한 계절 식량.</summary>
    public void SeedCouples(int couples, int plotsEach = 3)
    {
        int v = FoundVillage(SpawnX, SpawnY, -1);
        for (int k = 0; k < couples; k++)
        {
            var (x, y) = Layout ? HouseSpotOnRoad(v) : NearestBuildable(SpawnX + _rng.NextInt(13) - 6, SpawnY + _rng.NextInt(13) - 6, 20);
            int h = NewHousehold(x, y, 6f * 2 * Config.DaysPerSeason, Config.HutCapacity, v);
            var man = CreateFounder(true, 18 + _rng.NextInt(8), h);
            var woman = CreateFounder(false, 16 + _rng.NextInt(8), h);
            Households.Head[h] = man;
            Marry(man.Index, woman.Index, 0.5f);
            ClaimFields(h, plotsEach);
        }
    }

    /// <summary>시작 상황: 미혼 어른 count 명, 각자 부모 집처럼 2~4명씩 가구를 이룬다.</summary>
    public void SeedSingles(int count)
    {
        int made = 0;
        int v0 = VillageList.Add(SpawnX, SpawnY, Day);
        while (made < count)
        {
            int size = Math.Min(2 + _rng.NextInt(3), count - made);
            var (x, y) = NearestBuildable(SpawnX + _rng.NextInt(41) - 20, SpawnY + _rng.NextInt(41) - 20, 30);
            for (int t = 0; x < 0 && t < 2000; t++)
            {
                int rx = _rng.NextInt(Map.Width), ry = _rng.NextInt(Map.Height);
                if (Map.Buildable(rx, ry)) (x, y) = (rx, ry);
            }
            if (x < 0) throw new InvalidOperationException("시작 인구를 놓을 땅이 없다");
            int h = NewHousehold(x, y, 8f * size * Config.DaysPerSeason, Config.HutCapacity + 2, v0);
            for (int i = 0; i < size; i++)
            {
                var p = CreateFounder(_rng.NextBool(), 16 + _rng.NextInt(10), h);
                if (i == 0) Households.Head[h] = p;
            }
            ClaimFields(h, (int)MathF.Ceiling(size * 1.5f));
            made += size;
        }
    }

    /// <summary>
    /// 시작 인구를 무작위로 만든다(벤치마크용). 혼인·가구 없이 나이만 고르게.
    /// </summary>
    public void SeedPopulation(int count, int maxAgeYears = 50)
    {
        // 벤치마크용: 식량이 끝없는 큰 가구 하나
        int v = VillageList.Add(SpawnX, SpawnY, Day);
        int h = NewHousehold(SpawnX, SpawnY, 1e12f, 999999, v);
        for (int k = 0; k < count; k++)
        {
            var p = CreateFounder(_rng.NextBool(), _rng.NextInt(maxAgeYears + 1), h);
            if (k == 0) Households.Head[h] = p;
        }
    }

    /// <summary>아이 하나를 만든다. 아버지 유전체는 어머니가 임신할 때 복사해 둔 것을 쓴다.</summary>
    private PersonId BirthChild(int mother, int household)
    {
        bool male = _rng.NextBool();
        var p = People;
        var child = p.Add(male, Day, p.PregnancyFather[mother], p.IdAt(mother), household);
        Join(child.Index, household);
        int legal = p.PregnancyLegalFatherUid[mother];
        p.Uid[child.Index] = Lineage.Add(male, Day, p.PregnancyFatherUid[mother], p.Uid[mother], legal);
        _uidIndex[p.Uid[child.Index]] = child.Index;
        if (legal != p.PregnancyFatherUid[mother]) Stats.HiddenFatherBirths += legal >= 0 ? 1 : 0;
        if (legal < 0) Stats.Bastards++;
        if (legal >= 0 && p.Concubine[mother] && p.IsValid(p.Husband[mother]) && p.Uid[p.Husband[mother].Index] == legal)
        {
            Lineage.SetRight(p.Uid[child.Index], Birthright.Concubine);   // 첩의 자식은 서자
            Stats.ConcubineBirths++;
        }
        if (legal != p.PregnancyFatherUid[mother]) LinkHiddenChild(p.Uid[mother], p.PregnancyFatherUid[mother], p.Uid[child.Index]);
        Social.Remember(p, mother, MemoryType.ChildBorn, p.Uid[child.Index], Day, 30);
        Social.Add(p, child.Index, p.Uid[mother], 40f);
        if (legal >= 0) Social.Add(p, child.Index, legal, 30f);
        // 가치는 집안에서 배운다: 부모 신앙심의 평균 근처
        float fp = legal >= 0 && p.IsValid(p.PregnancyFather[mother]) ? p.Piety[p.PregnancyFather[mother].Index] : p.Piety[mother];
        p.Piety[child.Index] = Math.Clamp(0.5f * (fp + p.Piety[mother]) + (_rng.NextFloat() - 0.5f) * 0.5f, 0f, 1f);   // 집마다·사람마다 크게 다르다(퍼짐 약 0.2)
        Genetics.Inherit(p.FatherGenomeBuffer(mother), p.Genome(mother), p.Genome(child.Index),
            male, Config.MutationRate, ref _rng, Config.LoadMutationRate);
        int fatherUid = p.PregnancyFatherUid[mother];
        p.Inbreeding[child.Index] = fatherUid >= 0 ? Lineage.Relatedness(fatherUid, p.Uid[mother]) * 0.5f : 0f;
        p.ComputePhenotype(child.Index, Config.InbreedingDepression);
        Stats.InbreedingSum += p.Inbreeding[child.Index];
        if (Genetics.Expressed(p.Genome(child.Index), Marker.Frailty, male) || Genetics.Expressed(p.Genome(child.Index), Marker.ShortLife, male))
            Stats.RecessiveBirths++;
        Stats.Births++;
        if (Genetics.Expressed(p.Genome(child.Index), Marker.DiseaseA, male)) Stats.DiseaseABirths++;
        if (p.LoadHits[child.Index] > 0) Stats.LoadBirths++;
        Chron(child.Index, $"{Subj(Names.Full(Lineage, p.Uid[child.Index]))} 태어났다 ({(male ? "아들" : "딸")})");
        float fi = p.Inbreeding[child.Index];
        bool hit = p.LoadHits[child.Index] > 0;
        if (fi >= 0.1f) { Stats.CloseKinBirths++; if (hit) Stats.CloseKinLoadBirths++; }        // 형제·부모·숙질 (F ≥ 1/8)
        else if (fi >= 0.05f) { Stats.CousinBirths++; if (hit) Stats.CousinLoadBirths++; }  // 사촌 (F = 1/16)
        return child;
    }

    /// <summary>벤치마크·테스트용: 두 사람 사이에 바로 아이를 만든다.</summary>
    public PersonId Birth(PersonId father, PersonId mother, int household)
    {
        Genome(father.Index).CopyTo(People.FatherGenomeBuffer(mother.Index));
        People.PregnancyFather[mother.Index] = father;
        People.PregnancyFatherUid[mother.Index] = People.Uid[father.Index];
        People.PregnancyLegalFatherUid[mother.Index] = People.Uid[father.Index];
        return BirthChild(mother.Index, household);
    }

    private Span<byte> Genome(int i) => People.Genome(i);

    // ------------------------------------------------------------------ 하루

    /// <summary>단계별 누적 시간(ms). 성능 측정용.</summary>
    public Dictionary<string, double> Profile { get; } = new();
    public bool Profiling { get; set; }

    private void Timed(string name, Action a)
    {
        if (!Profiling) { a(); return; }
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        a();
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        Profile[name] = Profile.GetValueOrDefault(name) + ms;
    }

    public void Step()
    {
        var cal = Calendar;
        int doy = cal.DayOfYear;
        if (doy == 0) Timed("한 해 시작(분가·땅·집)", BeginYear);
        if (cal.DayOfSeason == 0)
        {
            ApplySwitch();   // 예약한 혈족 집 옮겨 맡기는 계절이 바뀔 때
            Timed("부패", SpoilWithTrade);
            Timed("빚과 땅 팔기", Distress);
            Timed("구애", Courtship);
            Timed("상호작용", SocialSeason);
            Timed("범죄", CrimeSeason);
            BanditSeason();
            KidnapSeason();
        }
        Timed("식량 소비", Consume);
        Timed("삶(사망·임신·출산)", Life);
        if (doy == Config.HarvestDayOfYear) Timed("수확", Harvest);
        if (doy == Config.DaysPerYear - 1) Timed("통계", () => Stats.Close(this));
        Day++;
    }

    private int AgeYears(int i) => (Day - People.BirthDay[i]) / Config.DaysPerYear;

    private void BeginYear()
    {
        // 날씨: 전년과 상관 있는 정규분포
        float z = Gaussian();
        float prev = (Weather - 1f) / Config.WeatherSd;
        float zz = Config.WeatherCorrelation * prev + MathF.Sqrt(1 - Config.WeatherCorrelation * Config.WeatherCorrelation) * z;
        Weather = Math.Clamp(1f + Config.WeatherSd * zz, 0.3f, 1.4f);
        Stats.Begin(Day, Weather);
        Timed("해/RebuildHouseholdGrid", RebuildHouseholdGrid);
        Timed("해/ChooseFloods", ChooseFloods);
        Timed("해/RecountHouseholds", RecountHouseholds);
        Timed("해/DecayVacant", DecayVacant);
        Timed("해/SocialYear", SocialYear);
        Timed("해/YearOfWork", YearOfWork);
        Timed("해/GoodsYear", GoodsYear);
        if (Layout) Stats.RegrownForest += Map.AgeAbandoned(Config.ForestYears);
        Timed("해/Splits", Splits);
        Timed("해/ClaimLand", ClaimLand);
        if (Layout) Stats.Swaps += Map.Consolidate(Households, Households.HighWater, Config.SwapTolerance);
        Timed("해/LeaseMarket", LeaseMarket);
        Timed("해/ReleaseExcess", ReleaseExcess);
        Timed("해/WageMarket", WageMarket);
        Timed("해/ProtectionYear", ProtectionYear);
        Timed("해/ArrangeYear", ArrangeYear);
        Timed("해/FamilyYear", FamilyYear);
        Timed("해/GroupYear", GroupYear);
        Timed("해/RiseYear", RiseYear);
        Timed("해/CoupYear", CoupYear);
        Timed("해/CrimeYear", CrimeYear);
        if (Layout) UpdateTraffic();
        Timed("해/UpgradeHouses", UpgradeHouses);
    }

    private HashSet<int> _flooded = new();

    /// <summary>강이 흐르는 대략 칸마다 해마다 홍수 판정.</summary>
    private void ChooseFloods()
    {
        _flooded = new HashSet<int>();
        var c = Map.Coarse;
        if (c == null) return;
        for (int i = 0; i < c.W * c.H; i++) if (c.River[i] && _rng.Chance(Config.FloodChance)) _flooded.Add(i);
    }

    private float Gaussian()
    {
        float u1 = MathF.Max(_rng.NextFloat(), 1e-6f), u2 = _rng.NextFloat();
        return MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
    }

    // ------------------------------------------------------------------ 식량

    private static float NeedByAge(int age) => age switch { <= 4 => 0.3f, <= 11 => 0.5f, <= 15 => 0.8f, <= 54 => 1f, _ => 0.8f };
    private static float LaborByAge(int age) => age switch { <= 4 => 0f, <= 11 => 0.3f, <= 15 => 0.7f, <= 54 => 1f, _ => 0.5f };

    private void RecountHouseholds()
    {
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++) { hh.Members[h] = 0; hh.Need[h] = 0; hh.Labor[h] = 0; }
        var p = People;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            int h = p.Household[i];
            if (!hh.IsAlive(h)) continue;
            int age = AgeYears(i);
            hh.Members[h]++;
            // 부지런하고 힘센 사람은 일을 더 하지만 더 먹는다(유전자 대가).
            float dil = p.Get(i, Trait.Diligence), str = MathF.Min(1f, p.Get(i, Trait.Strength));
            hh.Need[h] += NeedByAge(age) * (0.8f + 0.2f * dil + 0.2f * str);
            float lab = LaborByAge(age) * (0.7f + 0.6f * dil) * (0.8f + 0.4f * str);
            if (p.JobOf[i] != 0) lab *= Config.JobFarmShare;
            hh.Labor[h] += lab;
        }
    }

    private void Consume()
    {
        RecountHouseholds();
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Need[h] <= 0) continue;
            float ratio = MathF.Min(1f, hh.Food[h] / hh.Need[h]);
            hh.Food[h] = MathF.Max(0f, hh.Food[h] - hh.Need[h]);
            hh.Hungry[h] = ratio < 0.5f;
        }
    }

    private void Spoil()
    {
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h)) hh.Food[h] *= 1f - Config.SpoilagePerSeason;
    }

    private int DaysToHarvest()
    {
        int doy = Calendar.DayOfYear;
        return (Config.HarvestDayOfYear - doy + Config.DaysPerYear) % Config.DaysPerYear + 1;
    }

    /// <summary>잉여 식량 S = 창고 ÷ 다음 수확까지 먹을 양.</summary>
    public float Surplus(int h)
    {
        float need = Households.Need[h] * DaysToHarvest();
        return need <= 0 ? 2f : Households.Food[h] / need;
    }

    private readonly HashSet<int> _depleted = new();   // 지력이 기본값보다 낮은 필지

    private void Harvest()
    {
        var hh = Households;
        var map = Map;
        float total = 0;
        var worked = new HashSet<int>();
        EnsureLandArrays();
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            // 부치는 땅 = 빌려주지 않은 내 땅 + 빌린 땅. 일손 = 식구 + 산 품 − 판 품
            var mine = WorkPlots(h);
            if (mine.Count == 0) continue;
            float capacity = MathF.Max(0f, hh.Labor[h] + _hiredIn[h] - _hiredOut[h]) * Config.PlotsPerWorker;
            float farmK = (1f + Config.FarmSkillEffect * (FarmSkillOf(h) - Config.FarmSkillBaseline)) * ToolFactor(h);   // 도구가 없으면 덜 거둔다
            mine.Sort((a, b) => map.Fert(b).CompareTo(map.Fert(a)));
            float yield = 0, tenantYield = 0;
            int hv = hh.Village[h];
            float rate = hv >= 0 && hv < _rentRate.Count ? _rentRate[hv] : 0f;
            for (int k = 0; k < mine.Count && capacity > 0; k++)
            {
                float part = MathF.Min(1f, capacity);
                capacity -= 1f;
                int plot = mine[k];
                if (part > 0.5f) worked.Add(plot);
                // 숲과 오래 묵어 덤불이 된 땅은 첫해에 개간만 한다(수확 없음).
                int wild = map.Wild(plot);
                if (wild > 0) map.SetWild(plot, 0);
                if (wild >= Config.ScrubYears)
                {
                    map.SetFlags(plot, (byte)(map.Flags(plot) | WorldMap.FlagField));
                    continue;
                }
                if (map.Type(plot) == TileType.Forest)
                {
                    map.SetType(plot, TileType.Grass);
                    map.SetFlags(plot, (byte)(map.Flags(plot) | WorldMap.FlagField));
                    continue;
                }
                map.SetFlags(plot, (byte)(map.Flags(plot) | WorldMap.FlagField));
                if (_flooded.Count > 0 && map.WaterDist(plot) <= 2)
                {
                    var (x, y) = map.Pos(plot);
                    int ci = (y / WorldMap.CS) * map.ChunksX + x / WorldMap.CS;
                    if (_flooded.Contains(ci))
                    {
                        map.SetBaseFert(plot, MathF.Min(1.6f, map.BaseFert(plot) + 0.03f));
                        Stats.FloodedPlots++;
                        continue;
                    }
                }
                float got = Config.PlotYield * map.Fert(plot) * Weather * part * farmK;
                int owner = map.Owner(plot);
                if (owner != h && owner >= 0)
                {
                    // 빌린 땅: 소작료를 땅 주인에게
                    float rent = got * rate;
                    hh.Food[owner] += rent;
                    _incRent[owner] += rent;
                    tenantYield += got - rent;
                }
                else yield += got;
            }
            hh.Food[h] += yield + tenantYield;
            _incOwn[h] += yield;
            _incTenant[h] += tenantYield;
            total += yield + tenantYield;
        }
        PayWages();
        CollectTribute();
        SettleDebts();
        Classify();
        foreach (int plot in worked)
        {
            map.SetFert(plot, MathF.Max(0.1f, map.Fert(plot) - Config.FertilityDeclinePerYear));
            _depleted.Add(plot);
        }
        foreach (int plot in _depleted.ToArray())
        {
            if (worked.Contains(plot)) continue;
            float f = MathF.Min(map.BaseFert(plot), map.Fert(plot) + Config.FertilityRecoverPerYear);
            map.SetFert(plot, f);
            if (f >= map.BaseFert(plot)) _depleted.Remove(plot);
        }
        Stats.Harvest += total;
    }

    // ------------------------------------------------------------------ 땅, 분가, 집

    private float Wealth(int h) => Households.Food[h] + Households.LandValue[h];

    private void ClaimLand()
    {
        var hh = Households;
        var order = ShuffledHouseholds();
        // 영주의 땅으로 묶인 마을: 빌리려는 집이 늘면 그 마을 지배자(땅이 가장 많은 지배자 집)가 빈 땅을 직영지로 더 잡는다
        if (_enclosed.Count > 0)
        {
            var landlord = new Dictionary<int, int>();
            foreach (int h in order)
            {
                int v = hh.Village[h];
                if (v < 0 || !_enclosed.Contains(v) || TierOf(h) < 1) continue;
                if (!landlord.TryGetValue(v, out int cur) || hh.PlotCount[h] > hh.PlotCount[cur]) landlord[v] = h;
            }
            foreach (var (v, l) in landlord)
                if (_unmetLease.TryGetValue(v, out int miss) && miss > 0) TookPlots(v, ClaimFields(l, (int)(miss * 1.2f) + 1));
        }
        // 땅이 모자라기 시작한 마을에서는 넉넉한 집(마을에서 재산 상위 5분의 1, 또는 지배자)이 남은 땅을 먼저,
        // 일손보다 많이(내놓기 문턱 바로 아래까지) 잡는다. 남는 땅은 소작으로 나가 지주와 소작농이 생긴다
        var byVillage = new Dictionary<int, List<int>>();
        foreach (int h in order) if (hh.Village[h] >= 0) (byVillage.TryGetValue(hh.Village[h], out var l) ? l : byVillage[hh.Village[h]] = new()).Add(h);
        foreach (var (v, list) in byVillage)
        {
            if (list.Count < 8 || FreeInVillage(v) >= Config.MinFreePlotsToStay) continue;
            var rich = list.OrderByDescending(Wealth).Take(Math.Max(1, list.Count / 5)).ToList();
            foreach (int h in list) if (TierOf(h) >= 1 && !rich.Contains(h)) rich.Add(h);
            foreach (int h in rich)
            {
                int want = (int)(hh.Labor[h] * Config.PlotsPerWorker * Config.LandRelease * 0.95f);
                if (hh.PlotCount[h] < want) TookPlots(v, ClaimFields(h, want - hh.PlotCount[h]));
            }
        }
        foreach (int h in order)
        {
            // 일할 수 있는 양보다 넉넉히 잡는다. 남는 땅은 쉬게 되어 비옥도가 돌아온다(돌려짓기가 저절로 생긴다).
            int want = (int)MathF.Ceiling(hh.Labor[h] * Config.PlotsPerWorker * Config.LandReserve);
            if (hh.PlotCount[h] < want) ClaimFields(h, want - hh.PlotCount[h]);
            // 남는 땅은 소작 시장에 내놓고, 그래도 남으면 ReleaseExcess 가 내놓는다.
        }
    }

    private List<int> ShuffledHouseholds()
    {
        var list = new List<int>();
        for (int h = 0; h < Households.HighWater; h++) if (Households.IsAlive(h)) list.Add(h);
        for (int i = list.Count - 1; i > 0; i--) { int j = _rng.NextInt(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        return list;
    }

    /// <summary>혼인한 아들이 아이가 있거나 집이 북적이면 분가한다 (spec 6.3).</summary>
    private void Splits()
    {
        var p = People;
        var hh = Households;
        int hcount = hh.HighWater;
        // 같은 가구에 16세 미만 자식이 있는 아버지 표시
        var hasChild = new bool[p.HighWater];
        for (int k = 0; k < p.HighWater; k++)
        {
            if (!p.IsAliveIndex(k) || AgeYears(k) >= 16) continue;
            var f = p.Father[k];
            if (p.IsValid(f) && p.Household[f.Index] == p.Household[k]) hasChild[f.Index] = true;
        }
        for (int h0 = 0; h0 < hcount; h0++)
        {
            if (!hh.IsAlive(h0)) continue;
            var list = MembersOf(h0);
            foreach (int i in list)
            {
                if (!p.IsAliveIndex(i) || !p.Male[i] || p.WifeCount[i] == 0) continue;
                int h = p.Household[i];
                if (h != h0 || !hh.IsAlive(h) || hh.Head[h].Index == i) continue;
                if (!hasChild[i] && hh.Crowding(h) <= 1.5f) continue;

                if (SplitOff(i, h, list) < 0) continue;
            }
        }
        RecountHouseholds();
    }

    /// <summary>
    /// i 가 아내들과 그 사이 아이들을 데리고 집 h 에서 나와 새 집을 꾸린다. 새 집 번호(못 하면 -1).
    /// 집터: 마을에 빈 땅이 있으면 마을 안, 없으면 다른 마을이나 새 마을로. 다 안 되면 본가 땅을 나눈다.
    /// </summary>
    private int SplitOff(int i, int h, List<int>? list = null)
    {
        var p = People;
        var hh = Households;
        list ??= MembersOf(h);
        int nh = SettleNewHousehold(h);
        if (nh < 0) return -1;
        StarterGoods(nh, h);
        // 같은 마을에 분가하면 본가의 주군을 섬긴다(그 땅의 관습). 본가가 묶였으면 새 집도 묶인다
        EnsurePowerArrays();
        int lord = PatronOf(h);
        if (lord >= 0 && Households.Village[nh] == Households.Village[h] && !WouldLoop(nh, lord))
        {
            _patron[nh] = lord; _patronFounded[nh] = Households.FoundedDay[lord];
            Resnap(nh);
            if (Bound(h)) Bind(nh);
        }
        // 이 남자, 아내들, 그 사이 아이들을 옮긴다.
        int moved = 0;
        var me = p.IdAt(i);
        foreach (int k in list)
        {
            if (!p.IsAliveIndex(k) || p.Household[k] != h) continue;
            bool wife = !p.Male[k] && p.Husband[k] == me;
            bool child = AgeYears(k) < 16 && (p.Father[k] == me || (p.IsValid(p.Mother[k]) && p.Husband[p.Mother[k].Index] == me));
            if (k == i || wife || child) { MoveTo(k, nh); moved++; }
        }
        float n = MathF.Max(1, hh.Members[h]);
        float food = hh.Food[h] * moved / n;
        hh.Food[h] -= food;
        hh.Food[nh] = food;
        hh.Members[h] -= moved;
        hh.Head[nh] = me;
        int sonsLeft = 0;
        for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
            if (p.Male[k] && AgeYears(k) >= 16 && hh.Head[h].Index != k) sonsLeft++;
        InheritOnSplit(h, nh, sonsLeft);
        if (h == PlayerHousehold) Chron(i, $"{Subj(Names.Full(Lineage, p.Uid[i]))} 분가해 새 집을 꾸렸다");
        Stats.Splits++;
        return nh;
    }

    // ------------------------------------------------------------------ 마을 배치와 길

    private bool Layout => Map.Coarse != null;

    /// <summary>집이 들어서는 마을 중심 반경.</summary>
    private int HouseZone(int v) => Config.HouseRadius + (int)(0.9f * MathF.Sqrt(Math.Max(1, VillageList.Households[v])));

    /// <summary>
    /// 마을을 세운다. 큰길(옛 마을 쪽 방향)과 직각 길을 내고, 들로 뻗는 밭길을 낸 뒤, 옛 마을과 길로 잇는다.
    /// </summary>
    private int FoundVillage(int x, int y, int parentVillage)
    {
        var vl = VillageList;
        int v = vl.Add(x, y, Day);
        if (!Layout) return v;
        // 큰길 방향: 옛 마을 쪽(없으면 무작위) 을 상하좌우 중 하나로
        int dx = 1, dy = 0;
        if (parentVillage >= 0)
        {
            int px = vl.CenterX[parentVillage] - x, py = vl.CenterY[parentVillage] - y;
            if (Math.Abs(px) >= Math.Abs(py)) { dx = Math.Sign(px); dy = 0; } else { dx = 0; dy = Math.Sign(py); }
            if (dx == 0 && dy == 0) dx = 1;
        }
        else if (_rng.NextBool()) { dx = 0; dy = 1; }
        vl.DirX[v] = dx; vl.DirY[v] = dy;
        // 계획하는 길은 큰길 하나뿐. 집이 늘어설 자리다. 나머지 길은 사람이 다니며 생긴다.
        int R = Config.MainStreetLength;
        RoadLine(x, y, dx, dy, R, true, Config.PlannedWear);
        RoadLine(x, y, -dx, -dy, R, true, Config.PlannedWear);
        // 옛 마을과 잇는다. 왕래가 없으면 이 길도 풀에 덮인다.
        if (parentVillage >= 0)
        {
            var path = Map.FindPath(x, y, vl.CenterX[parentVillage], vl.CenterY[parentVillage]);
            foreach (var (px, py) in path) { Map.MakeRoad(px, py, Households); Map.AddWear(px, py, Config.PlannedWear); }
            if (path.Count > 0) Stats.RoadTiles += path.Count;
            Contact(v, parentVillage);
        }
        return v;
    }

    /// <summary>(x, y) 에서 (dx, dy) 방향으로 len 칸 길을 낸다. 물이나 집을 만나면 멈춘다(다리는 마을 잇기 길에서만).</summary>
    private void RoadLine(int x, int y, int dx, int dy, int len, bool bridge = false, float wear = 0f)
    {
        for (int k = 0; k <= len; k++)
        {
            int xx = x + dx * k, yy = y + dy * k;
            if (!Map.InBounds(xx, yy)) return;
            var t = Map.Type(Map.Id(xx, yy));
            if (t is TileType.Sea or TileType.Lake or TileType.Mountain) return;
            if (t == TileType.River)
            {
                // 큰길은 강폭이 3칸 이하면 다리를 놓고 건넌다. 골목은 강에서 멈춘다.
                if (!bridge) return;
                int w = 0;
                while (w <= 3 && Map.InBounds(xx + dx * w, yy + dy * w) && Map.Type(Map.Id(xx + dx * w, yy + dy * w)) == TileType.River) w++;
                if (w > 3 || !Map.InBounds(xx + dx * w, yy + dy * w)) return;
                var far = Map.Type(Map.Id(xx + dx * w, yy + dy * w));
                if (far is TileType.Sea or TileType.Lake or TileType.Mountain) return;
            }
            Map.MakeRoad(xx, yy, Households);
            if (wear > 0f) Map.AddWear(xx, yy, wear);
        }
    }

    // ------------------------------------------------------------------ 왕래와 길

    // 그해 두 마을 사이 왕래(혼인, 이주) 횟수. 해가 바뀔 때 길을 닳게 하고 비운다.
    private readonly Dictionary<long, int> _contacts = new();

    private void Contact(int a, int b)
    {
        if (a < 0 || b < 0 || a == b) return;
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        _contacts[key] = _contacts.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// 한 해 동안 사람이 다닌 만큼 땅을 닳게 하고, 길을 만들거나 지운다.
    /// 가구마다 집 → 마을 중심, 밭 → 마을 중심을 한 번씩 걷는다. 왕래한 마을끼리는 길찾기 경로를 걷는다.
    /// </summary>
    private void UpdateTraffic()
    {
        var hh = Households;
        var vl = VillageList;
        var trips = new Dictionary<int, WorldMap.Reach>();
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            int v = hh.Village[h];
            if (v < 0 || !vl.Alive[v]) continue;
            if (!trips.TryGetValue(v, out var R))
            {
                R = Map.BuildTrips(vl.CenterX[v], vl.CenterY[v], Config.FarmTravel + 8f);
                trips[v] = R;
            }
            Map.WalkToCenter(R, hh.X[h], hh.Y[h], Config.HomeTripWear);
            int plot = RepresentativePlot(h);
            if (plot >= 0)
            {
                var (fx, fy) = Map.Pos(plot);
                Map.WalkToCenter(R, fx, fy, Config.FieldTripWear);
            }
        }
        foreach (var (key, n) in _contacts)
        {
            int a = (int)(key >> 32), b = (int)(key & 0xffffffff);
            if (!vl.Alive[a] || !vl.Alive[b]) continue;
            var path = Map.FindPath(vl.CenterX[a], vl.CenterY[a], vl.CenterX[b], vl.CenterY[b]);
            foreach (var (px, py) in path) Map.AddWear(px, py, n * Config.ContactWear);
        }
        _contacts.Clear();
        var (made, lost) = Map.UpdateRoads(Config.WearKeep, Config.WearRoadOn, Config.WearRoadOff, Config.WearMajor, hh);
        Stats.RoadTiles += made;
        Stats.RoadsLost += lost;
    }

    /// <summary>가구 밭 덩어리의 가운데에 가장 가까운 필지.</summary>
    private int RepresentativePlot(int h)
    {
        var plots = Map.PlotsOf(h);
        if (plots.Count == 0) return -1;
        float mx = 0, my = 0;
        foreach (int id in plots) { var (x, y) = Map.Pos(id); mx += x; my += y; }
        mx /= plots.Count; my /= plots.Count;
        int best = plots[0];
        float bd = float.MaxValue;
        foreach (int id in plots)
        {
            var (x, y) = Map.Pos(id);
            float d = (x - mx) * (x - mx) + (y - my) * (y - my);
            if (d < bd) { bd = d; best = id; }
        }
        return best;
    }

    /// <summary>
    /// 길가 집터: 마을 반경 안에서 길에 붙은 빈 칸. 중심에 가까운 곳부터. 없으면 큰길에서 골목을 하나 낸다.
    /// </summary>
    private (int x, int y) HouseSpotOnRoad(int v)
    {
        var vl = VillageList;
        int cx = vl.CenterX[v], cy = vl.CenterY[v];
        if (!Layout) return Map.HouseSpot(cx, cy, HouseZone(v), ref _rng);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int R = HouseZone(v);
            (int x, int y) best = (-1, -1);
            float bestS = float.MaxValue;
            for (int y = cy - R; y <= cy + R; y++)
                for (int x = cx - R; x <= cx + R; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > R * R || !Map.BuildableOrField(x, y)) continue;
                    if (!(Map.IsRoad(x + 1, y) || Map.IsRoad(x - 1, y) || Map.IsRoad(x, y + 1) || Map.IsRoad(x, y - 1))) continue;
                    // 빈 칸을 먼저 쓰고, 없으면 길가 밭을 집터로 내준다
                    float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + _rng.NextFloat() * 1.5f
                              + (Map.Buildable(x, y) ? 0f : 4f);
                    if (d < bestS) { bestS = d; best = (x, y); }
                }
            if (best.x >= 0)
            {
                if (Map.Evict(best.x, best.y, Households) >= 0) Stats.Evicted++;
                return best;
            }
            // 골목: 큰길이나 직각 길 위 한 점에서 직각으로 짧게
            int k = 2 + _rng.NextInt(Math.Max(1, R - 2));
            int sign = _rng.NextBool() ? 1 : -1;
            int ddx = vl.DirX[v], ddy = vl.DirY[v];
            if (_rng.NextBool()) (ddx, ddy) = (-ddy, ddx);
            int sx = cx + ddx * k * sign, sy = cy + ddy * k * sign;
            int side = _rng.NextBool() ? 1 : -1;
            RoadLine(sx, sy, -ddy * side, ddx * side, Math.Max(3, R / 2), false, Config.PlannedWear * 0.5f);
            Stats.Lanes++;
        }
        return Map.HouseSpot(cx, cy, HouseZone(v), ref _rng);
    }

    /// <summary>가구 h 가 마을 v 에서 밭 n 칸을 더 잡는다(덩어리로).</summary>
    private int ClaimFields(int h, int n)
    {
        var (cx, cy) = ClaimCenter(h);
        int v = Households.Village[h];
        // 영주의 땅으로 묶인 마을: 빈 땅은 지배자 것이다. 다른 집은 빌려 부친다
        if (v >= 0 && _enclosed.Contains(v) && TierOf(h) < 1 && ClientCount(h) == 0) return 0;
        if (!Layout || v < 0) return Map.Claim(h, n, Households, cx, cy, FarmRadiusOf(v));
        return Map.ClaimContiguous(h, n, Households, ReachOf(v), HouseZone(v) + 1);
    }

    /// <summary>분가하는 가구의 자리를 정하고 가구를 만든다. 실패하면 -1.</summary>
    private int SettleNewHousehold(int parent)
    {
        var hh = Households;
        var vl = VillageList;
        int v = hh.Village[parent];
        if (Map.Coarse == null)
        {
            // 평평한 섬(1단계 실험): 본가 근처에 집을 짓고 그 둘레 빈 땅을 잡는다.
            var (fx, fy) = NearestBuildable(hh.X[parent], hh.Y[parent], 8);
            if (fx >= 0 && Map.CountFreeNear(fx, fy, Config.FarmRadius) >= 2)
            {
                int nh = NewHousehold(fx, fy, 0f, Config.HutCapacity, v);
                Map.Claim(nh, 3, hh, fx, fy, Config.FarmRadius);
                return nh;
            }
            return SplitParentLand(parent, v);
        }
        // 1) 지금 마을에 빈 땅이 넉넉하면 마을 안에 집을 짓는다.
        if (v >= 0 && FreeInVillage(v) >= Config.MinFreePlotsToStay)
        {
            int nh = SettleIn(v, parent);
            if (nh >= 0) return nh;
        }

        // 1-1) 땅이 찼어도 혈족 곁에 남는 집이 있다. 남의 땅을 빌리거나 품을 팔아 산다(소작농·일꾼)
        if (v >= 0 && _rng.Chance(Config.StayLandless))
        {
            int nh = SettleIn(v, parent);
            if (nh >= 0) { Stats.LandlessSettlers++; return nh; }
        }

        // 2) 근처 다른 마을 중 빈 땅이 있는 곳, 3) 새 마을 자리. 더 나은 쪽으로 간다.
        int px = hh.X[parent], py = hh.Y[parent];
        int bestV = -1;
        float bestVScore = float.MinValue;
        for (int o = 0; o < vl.Count; o++)
        {
            if (!vl.Alive[o] || o == v) continue;
            float d = MathF.Sqrt(MathF.Pow(vl.CenterX[o] - px, 2) + MathF.Pow(vl.CenterY[o] - py, 2));
            if (d > Config.MigrationRadius) continue;
            int free = FreeInVillage(o);
            if (free < Config.MinFreePlotsToStay * 2) continue;
            float sc = MathF.Min(free, 60) - d * 0.1f;
            if (sc > bestVScore) { bestVScore = sc; bestV = o; }
        }
        (int x, int y) bestSite = (-1, -1);
        float bestSiteScore = float.MinValue;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float r = Config.NewVillageMinDist + _rng.NextFloat() * (Config.MigrationRadius - Config.NewVillageMinDist);
            float a = _rng.NextFloat() * MathF.PI * 2;
            int x = (int)(px + MathF.Cos(a) * r), y = (int)(py + MathF.Sin(a) * r);
            if (!Map.InBounds(x, y) || NearestVillageDist(x, y) < Config.NewVillageMinDist) continue;
            float sc = Map.VillageSiteScore(x, y, Config.FarmRadius) - r * 0.05f;
            if (sc > bestSiteScore) { bestSiteScore = sc; bestSite = (x, y); }
        }
        // 이미 있는 마을은 이웃과 혼인 상대가 있어서 조금 더 끌린다.
        if (bestV >= 0 && (bestSite.x < 0 || bestVScore + 15f > bestSiteScore * 0.5f))
        {
            int nh = SettleIn(bestV, parent);
            if (nh >= 0)
            {
                Contact(v, bestV);
                Stats.Migrations++;
                return nh;
            }
        }
        if (bestSite.x >= 0)
        {
            int nv = FoundVillage(bestSite.x, bestSite.y, v);
            var (hx, hy) = HouseSpotOnRoad(nv);
            if (hx < 0) (hx, hy) = NearestBuildable(bestSite.x, bestSite.y, 6);
            int nh = NewHousehold(hx, hy, 0f, Config.HutCapacity, nv);
            PayBuild(parent);
            ClaimFields(nh, 3);
            Stats.Migrations++;
            Stats.NewVillages++;
            return nh;
        }
        // 4) 갈 곳이 없으면: 빌릴 땅이 남은 마을에서 땅 없이 시작한다(소작농). 그것도 없으면 본가 땅을 나눈다.
        if (v >= 0 && LeaseSlack(v) >= Config.StayLeaseSlack)
        {
            int nh = SettleIn(v, parent);
            if (nh >= 0) { Stats.LandlessSettlers++; return nh; }
        }
        return SplitParentLand(parent, v);
    }

    /// <summary>
    /// 마을 v 에 자리를 잡는다. 살 만한 빈집이 있고 값을 치를 수 있으면 사서 들어가고,
    /// 아니면 길가에 새로 짓는다(짓는 값은 본가가 낸다). 실패하면 -1.
    /// </summary>
    private int SettleIn(int v, int parent)
    {
        var hh = Households;
        int best = -1;
        float bestScore = 0f;
        for (int k = 0; k < _vacant.Count; k++)
        {
            var vh = _vacant[k];
            if (vh.Village != v || vh.Condition < Config.VacantMinCondition) continue;
            float sc = vh.Capacity * vh.Condition;
            if (sc > bestScore) { bestScore = sc; best = k; }
        }
        if (best >= 0)
        {
            var vh = _vacant[best];
            float price = HousePrice(vh);
            float reserve = hh.Need[parent] * DaysToHarvest();
            if (hh.Food[parent] - price >= reserve)
            {
                hh.Food[parent] -= price;
                if (hh.IsAlive(vh.Heir) && hh.FoundedDay[vh.Heir] == vh.HeirFounded) hh.Food[vh.Heir] += price;
                _vacant[best] = _vacant[^1];
                _vacant.RemoveAt(_vacant.Count - 1);
                int nh = NewHousehold(vh.X, vh.Y, 0f, vh.Capacity, v);
                if (vh.Residence > 0)
                {
                    if (_residence.Length <= nh) Array.Resize(ref _residence, Math.Max(nh + 1, _residence.Length * 2));
                    _residence[nh] = (byte)vh.Residence;
                    if (vh.Footprint != null) _footprint[nh] = vh.Footprint;
                }
                TookPlots(v, ClaimFields(nh, 3));
                Stats.HouseSales++;
                return nh;
            }
        }
        var (x, y) = HouseSpotOnRoad(v);
        if (x < 0) return -1;
        int h = NewHousehold(x, y, 0f, Config.HutCapacity, v);
        PayBuild(parent);
        TookPlots(v, ClaimFields(h, 3));
        Stats.HousesBuilt++;
        return h;
    }

    /// <summary>빈집 값: 새로 짓는 값 × 크기 비율 × 상태 × 할인.</summary>
    private float HousePrice(VacantHouse vh) =>
        Config.HouseBuildCost * vh.Capacity / (float)Config.HutCapacity * vh.Condition * Config.VacantPriceFactor;

    /// <summary>새 집을 짓는 값. 본가가 낼 수 있는 만큼 낸다(모자라면 손으로 지은 셈).</summary>
    private void PayBuild(int parent)
    {
        if (Households.IsAlive(parent)) PayCarpenter(parent, Config.HouseBuildCost);
    }

    /// <summary>빈집은 해마다 낡는다. 다 낡으면 무너져 터가 빈다.</summary>
    private void DecayVacant()
    {
        for (int k = _vacant.Count - 1; k >= 0; k--)
        {
            var vh = _vacant[k];
            vh.Condition -= 1f / (Config.VacantLifeYears * (1 + vh.Residence));   // 돌로 지은 큰 집은 오래 버틴다
            if (vh.Condition > 0f) { _vacant[k] = vh; continue; }
            Map.MarkHouse(vh.X, vh.Y, false);
            if (vh.Footprint != null) foreach (int id in vh.Footprint) { var (fx, fy) = Map.Pos(id); Map.MarkHouse(fx, fy, false); }
            _vacant[k] = _vacant[^1];
            _vacant.RemoveAt(_vacant.Count - 1);
            Stats.HousesCollapsed++;
        }
    }

    /// <summary>갈 곳이 없으면 본가 옆에 집을 짓고 본가 땅을 반 나눈다.</summary>
    private int SplitParentLand(int parent, int v)
    {
        var hh = Households;
        int share = hh.PlotCount[parent] / 2;
        var (bx, by) = NearestBuildable(hh.X[parent], hh.Y[parent], 6);
        if (share < 2 || bx < 0) return -1;
        int nh = NewHousehold(bx, by, 0f, Config.HutCapacity, v);
        Map.Transfer(parent, nh, share, hh);
        return nh;
    }

    // 마을별 빈 필지 수. 해마다 처음 물을 때 세고, 이후 가져간 만큼 뺀다(근사).
    private readonly Dictionary<int, int> _freeCache = new();
    private int _freeCacheYear = -1;

    private int FreeInVillage(int v)
    {
        if (_freeCacheYear != Calendar.Year) { _freeCache.Clear(); _freeCacheYear = Calendar.Year; }
        if (_freeCache.TryGetValue(v, out int n)) return n;
        var vl = VillageList;
        n = Layout ? Map.CountClaimable(ReachOf(v), HouseZone(v) + 1, Config.StayTravel)
                   : Map.CountFreeNear(vl.CenterX[v], vl.CenterY[v], FarmRadiusOf(v));
        _freeCache[v] = n;
        return n;
    }

    private void TookPlots(int v, int n)
    {
        if (_freeCache.TryGetValue(v, out int f)) _freeCache[v] = Math.Max(0, f - n);
    }

    private float NearestVillageDist(int x, int y)
    {
        float best = float.MaxValue;
        for (int o = 0; o < VillageList.Count; o++)
        {
            if (!VillageList.Alive[o]) continue;
            float d = MathF.Sqrt(MathF.Pow(VillageList.CenterX[o] - x, 2) + MathF.Pow(VillageList.CenterY[o] - y, 2));
            if (d < best) best = d;
        }
        return best;
    }

    private void UpgradeHouses()
    {
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Capacity[h] >= Config.MaxCapacity) continue;
            if (hh.Crowding(h) > 1.2f && Surplus(h) > 1.3f && hh.Food[h] > Config.HouseUpgradeCost * 2)
            {
                PayCarpenter(h, Config.HouseUpgradeCost);
                hh.Capacity[h] = Math.Min(Config.MaxCapacity, hh.Capacity[h] + 2);
            }
        }
    }

    // ------------------------------------------------------------------ 삶: 사망, 성장, 임신, 출산

    private void Life()
    {
        var p = People;
        var hh = Households;
        int dpy = Config.DaysPerYear;
        int hi = p.HighWater;
        for (int i = 0; i < hi; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            int age = AgeYears(i);
            int h = p.Household[i];
            bool hungry = hh.IsAlive(h) && hh.Hungry[h];
            p.HungryToday[i] = hungry;
            if (hungry && age < 16) p.ChildhoodHungryDays[i]++;
            if (age >= 16 && !p.GrownUp[i])
            {
                p.ApplyChildhoodHunger(i, dpy);
                ApplyExperience(i);
                p.GrownUp[i] = true;
            }

            float situ = 1f;
            if (hungry) situ *= age < 5 || age >= 55 ? Config.HungerMortalityVulnerable : Config.HungerMortality;
            if (hh.IsAlive(h)) situ *= 1f + Config.CrowdingMortality * MathF.Max(0f, hh.Crowding(h) - 1f);
            if (age < 5 && Genetics.Expressed(p.Genome(i), Marker.DiseaseA, p.Male[i])) situ *= 4f;
            if (age < 5 && p.LoadInfant[i] > 0) situ *= MathF.Pow(Config.LoadInfantMortality, p.LoadInfant[i]);
            if (age < 5) situ *= 1f - Config.HealerEffect * HealerSkillAt(h);
            // 겨울에 입을 것이 모자라면 아이와 노인이 더 죽는다
            if (Calendar.Season == Season.Winter) situ *= 1f + (age < 5 || age >= 55 ? 0.3f : 0.05f) * ClothShort(h);
            else if (age >= 16 && age < 60) situ *= 1.15f - 0.3f * p.Get(i, Trait.Caution);   // 신중한 사람은 덜 다친다
            if (age == 6 && p.RaisedBy[i] < 0) RecordRaisedBy(i);
            float d = Mortality.Daily(age, p.Get(i, Trait.Longevity), p.Get(i, Trait.Constitution), situ);
            if (_rng.Chance(d))
            {
                Die(i, hungry);
                continue;
            }

            if (!p.Male[i])
            {
                if (p.PregnantUntil[i] >= 0 && Day >= p.PregnantUntil[i]) GiveBirth(i, age);
                else if (p.PregnantUntil[i] < 0 && age >= 16 && age <= 42) TryConceive(i, age);
            }
        }
    }

    private void TryConceive(int w, int age)
    {
        var p = People;
        var husband = p.Husband[w];
        if (!p.IsValid(husband)) return;
        int m = husband.Index;
        int h = p.Household[w];
        if (!Households.IsAlive(h)) return;
        // 같이 사는 부부: 꾸준한 관계를 하루 단위로 묶어 날마다 판정한다.
        float coupleK = 0.7f + 0.3f * Math.Clamp(p.CoupleScore[w], 0f, 1f);
        float prob = Config.ConceptionBasePerDay * coupleK * ConceptionFactor(w, m);
        if (_rng.Chance(prob)) Conceive(w, m, p.Uid[m]);
    }

    /// <summary>
    /// 임신 계수(여자·남자 몸과 형편). 부부의 날마다 판정과 그 밖의 행위 한 번 판정이 같은 계수를 쓴다. 0이면 임신할 수 없다.
    /// </summary>
    public float ConceptionFactor(int w, int m)
    {
        var p = People;
        int age = AgeYears(w);
        if (age < 16 || age > 42 || p.PregnantUntil[w] >= 0 || Day < p.CooldownUntil[w]) return 0f;
        if (Genetics.Expressed(p.Genome(w), Marker.DiseaseB, false) || Genetics.Expressed(p.Genome(m), Marker.DiseaseB, true)) return 0f;
        int h = p.Household[w];
        float ageK = age <= 30 ? 1f : 1f - 0.8f * (age - 30) / 12f;
        float foodK = 1f, crowdK = 1f;
        if (Households.IsAlive(h))
        {
            float s = Surplus(h);
            foodK = s < 0.3f ? 0.05f : s < 1f ? 0.05f + 0.95f * (s - 0.3f) / 0.7f : s < 2f ? 1f + 0.2f * (s - 1f) : 1.2f;
            crowdK = 1f / MathF.Max(1f, Households.Crowding(h));
        }
        // 남자: 다산 유전과 나이(천천히 줄어든다)
        int mage = AgeYears(m);
        float maleK = (0.75f + 0.5f * p.Get(m, Trait.Fertility)) * (mage >= 60 ? 0.5f : mage >= 50 ? 0.7f : 1f);
        float contraK = p.ContraUntil[w] >= Day ? 1f - Config.ContraEffect : 1f;   // 약초꾼의 피임 약
        return (0.5f + p.Get(w, Trait.Fertility)) * ageK * foodK * crowdK * maleK * contraK;
    }

    /// <summary>행위 한 번(부부 밖): 임신 판정. 되면 true. legalFather 는 족보에 적힐 아버지(-1 = 사생아).</summary>
    public bool ActPregnancy(int w, int m, int legalFatherUid, float protection = 0f)
    {
        float prob = Config.ConceptionPerAct * ConceptionFactor(w, m) * (1f - protection);
        if (!_rng.Chance(prob)) return false;
        Conceive(w, m, legalFatherUid);
        return true;
    }

    /// <summary>임신시킨다. 유전체는 진짜 아버지(m) 것, 족보 아버지는 legalFatherUid.</summary>
    private void Conceive(int w, int m, int legalFatherUid)
    {
        var p = People;
        p.PregnantUntil[w] = Day + Config.PregnancyDays;
        p.PregnancyFatherUid[w] = p.Uid[m];
        p.PregnancyLegalFatherUid[w] = legalFatherUid;
        p.PregnancyFather[w] = legalFatherUid == p.Uid[m] ? p.IdAt(m) : legalFatherUid >= 0 && p.IsValid(p.Husband[w]) && p.Uid[p.Husband[w].Index] == legalFatherUid ? p.Husband[w] : PersonId.None;
        p.Genome(m).CopyTo(p.FatherGenomeBuffer(w));
    }

    private void GiveBirth(int w, int age)
    {
        var p = People;
        p.PregnantUntil[w] = -1;
        float fert = p.Get(w, Trait.Fertility);
        p.CooldownUntil[w] = Day + (int)MathF.Round(Config.PostpartumCooldownDays * (Config.CooldownFertBase - Config.CooldownFertSlope * fert));
        int h = p.Household[w];
        float con = p.Get(w, Trait.Constitution);
        int n = 1;
        float twin = (Genetics.Expressed(p.Genome(w), Marker.Twinning, false) ? Config.TwinWithTrait : Config.TwinBase) * (0.5f + fert);
        if (_rng.Chance(Config.TripletRate)) n = 3;
        else if (_rng.Chance(twin)) n = 2;
        if (n > 1) Stats.MultipleBirths++;
        PayHealer(h);
        for (int k = 0; k < n; k++)
        {
            if (_rng.Chance(Config.StillbirthBase * (1.5f - con) * (n > 1 ? 2f : 1f))) { Stats.Stillbirths++; continue; }
            BirthChild(w, h);
            if (p.BirthsGiven[w] < 255) p.BirthsGiven[w]++;
        }
        float maternal = (1f - Config.HealerEffect * HealerSkillAt(p.Household[w])) * Config.MaternalDeathBase * (age >= 35 ? 2f : 1f) * (1.5f - con) * (p.BirthsGiven[w] >= 6 ? 1.5f : 1f) * (n > 1 ? 1.5f : 1f);
        if (_rng.Chance(maternal))
        {
            Stats.MaternalDeaths++;
            Die(w, false);
        }
    }

    private void Die(int i, bool hungry)
    {
        var p = People;
        var hh = Households;
        int age = AgeYears(i);
        Stats.Deaths++;
        if (hungry) Stats.HungerDeaths++;
        if (age < 1) Stats.InfantDeaths++;
        Chron(i, $"{Subj(Names.Full(Lineage, p.Uid[i]))} {(age == 0 ? "돌을 넘기지 못하고" : $"{age}세에")} 죽었다{(hungry ? " (굶주림)" : "")}");
        Lineage.MarkDeath(p.Uid[i], Day);
        if (p.Fanatic[i] != 0) Stats.FanaticsDied++;

        int h = p.Household[i];
        if (p.Male[i])
        {
            // 아내들의 Husband 참조는 세대 카운터 때문에 저절로 무효가 된다(과부).
            if (age >= 40 && hh.IsAlive(h)) MenAtDeath.Add((Wealth(h), Lineage.Children(p.Uid[i]), p.WifeCount[i]));
        }
        else
        {
            var husband = p.Husband[i];
            if (p.IsValid(husband) && p.WifeCount[husband.Index] > 0) p.WifeCount[husband.Index]--;
            Stats.RecordWomanDeath(age, Lineage.Children(p.Uid[i]));
        }
        bool wasHead = hh.IsAlive(h) && hh.Head[h].Index == i;
        if (wasHead) _deadHead[h] = p.Uid[i];
        if (wasHead && h == PlayerHousehold) _fork = true;
        Leave(i);
        p.Remove(i);
        if (wasHead) Succession(h);
    }

    /// <summary>가장이 죽으면: 가장 나이 많은 어른 남자 → 어른 → 아무나. 어른이 없으면 아이들은 친척 집으로.</summary>
    private void Succession(int h)
    {
        var p = People;
        var hh = Households;
        int best = -1, bestScore = int.MinValue;
        bool anyAdult = false;
        int named = HeirOf(h);
        for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
        {
            int age = AgeYears(k);
            if (age >= 12) anyAdult = true;
            // 지정 후계자 → 어른 남자 → 어른 → 적자가 서자·사생아보다 → 나이. 의절한 사람은 맨 뒤.
            var right = Lineage.RightOf(p.Uid[k]);
            int score = age + (p.Male[k] && age >= 16 ? 1000 : 0) + (age >= 16 ? 500 : 0) + (right == Birthright.Legit ? 200 : right == Birthright.Concubine ? 100 : 0)
                      + (k == named && age >= 12 ? 5000 : 0) - (Lineage.Disowned(p.Uid[k]) ? 3000 : 0);
            if (score > bestScore) { bestScore = score; best = k; }
        }
        if (best >= 0 && anyAdult)
        {
            hh.Head[h] = p.IdAt(best);
            _heir.Remove(h);
            if (ClientCount(h) > 0) ShakeLordship(h, Config.HeadChangeLoss);   // 새 가장이 약하면 떠나는 집이 생긴다
            return;
        }
        // 지배자 집이 끊긴다: 가장 가까운 혈족 집이 지배권·땅·성을 잇는다.
        bool keepHouse = false;
        if (ClientCount(h) > 0 || ResidenceOf(h) > 0)
        {
            int lordHeir = FindLordHeir(h);
            if (lordHeir >= 0) keepHouse = InheritLordship(h, lordHeir);
        }
        // 어른이 없다: 아이들을 혈연이 가장 가까운 가구로 보내고 가구를 없앤다.
        int heir = -1;
        foreach (int k in MembersOf(h))
        {
            int target = ClosestKinHousehold(k, h);
            if (heir < 0 && target >= 0) heir = target;
            Leave(k);
            Join(k, target);
        }
        // 남은 사람이 없거나 아이만 있던 가구는 없앤다. 아이들을 맡은 집이 빈집을 물려받는다.
        if (h == PlayerHousehold) FollowPlayer(heir);
        RemoveHousehold(h, heir, keepHouse);
        Stats.Dissolved++;
    }

    private int ClosestKinHousehold(int person, int exclude)
    {
        var p = People;
        var hh = Households;
        int best = -1;
        float bestR = -1f;
        int biggest = -1, biggestN = -1;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || h == exclude || !p.IsValid(hh.Head[h])) continue;
            float r = Lineage.Relatedness(p.Uid[person], p.Uid[hh.Head[h].Index]);
            if (r > bestR) { bestR = r; best = h; }
            if (hh.Members[h] > biggestN) { biggestN = hh.Members[h]; biggest = h; }
        }
        return bestR >= 0.0625f ? best : biggest;
    }

    // ------------------------------------------------------------------ 짝짓기 (spec 3)

    private void Marry(int man, int woman, float coupleScore)
    {
        var p = People;
        p.Husband[woman] = p.IdAt(man);
        p.WifeCount[man]++;
        Social.Add(p, man, p.Uid[woman], 40f);
        Social.Add(p, woman, p.Uid[man], 40f);
        Social.Remember(p, man, MemoryType.Married, p.Uid[woman], Day, 40);
        Social.Remember(p, woman, MemoryType.Married, p.Uid[man], Day, 40);
        p.CoupleScore[woman] = coupleScore;
        p.Concubine[woman] = false;
        if (IsPlayer(man) || IsPlayer(woman)) Chron(IsPlayer(man) ? man : woman, $"{With(Names.Full(Lineage, p.Uid[man]))} {Subj(Names.Full(Lineage, p.Uid[woman]))} 혼인했다");
        int target = p.Household[man], old = p.Household[woman];
        if (Households.IsAlive(old) && Households.IsAlive(target)) Contact(Households.Village[old], Households.Village[target]);
        if (old != target && Households.IsAlive(old))
        {
            // 재혼하는 어머니는 같이 살던 12세 미만 자식을 데려간다.
            var me = p.IdAt(woman);
            foreach (int k in MembersOf(old))
                if (k != woman && p.Mother[k] == me && AgeYears(k) < 12) MoveTo(k, target);
        }
        MoveTo(woman, target);
    }

    /// <summary>사람을 다른 가구로 옮긴다. 원래 가구의 가장이었으면 승계를 처리한다.</summary>
    private void MoveTo(int person, int household)
    {
        var p = People;
        int old = p.Household[person];
        Leave(person);
        Join(person, household);
        if (old != household && Households.IsAlive(old) && Households.Head[old].Index == person) Succession(old);
    }

    private float WealthIndex(int person)
    {
        int h = People.Household[person];
        return Households.IsAlive(h) ? MathF.Log10(1f + Wealth(h)) / 4f : 0f;
    }

    /// <summary>A 가 B 를 짝으로 얼마나 원하는가.</summary>
    public float MateScore(int a, int b) => MateScoreNoKin(a, b) - CoRaisedPenalty(a, b);

    /// <summary>
    /// 같은 사람 밑에서 비슷한 나이로 자란 사이는 서로 끌리지 않는다(혈연을 직접 보지 않는다).
    /// 친형제·이복형제·의붓형제가 걸리고, 따로 자란 친척은 걸리지 않는다.
    /// </summary>
    public float CoRaisedPenalty(int a, int b)
    {
        var p = People;
        int ra = p.RaisedBy[a];
        if (ra < 0 || ra != p.RaisedBy[b]) return 0f;
        int gap = Math.Abs(p.BirthDay[a] - p.BirthDay[b]) / Config.DaysPerYear;
        return gap <= Config.CoRaisedMaxAgeGap ? Config.CoRaisedPenalty : 0f;
    }

    private void RecordRaisedBy(int i)
    {
        int h = People.Household[i];
        if (!Households.IsAlive(h)) return;
        var head = Households.Head[h];
        if (People.IsValid(head)) People.RaisedBy[i] = People.Uid[head.Index];
    }

    /// <summary>혈연 감점을 뺀 짝 점수. 후보를 먼저 거르는 데 쓴다(혈연 계산이 비싸다).</summary>
    public float MateScoreNoKin(int a, int b)
    {
        var p = People;
        bool bMale = p.Male[b];
        float looks = p.Get(b, Trait.Looks), height = p.Get(b, Trait.Height), cha = p.Get(b, Trait.Charisma);
        float body = bMale ? p.Get(b, Trait.Strength) : p.Get(b, Trait.Constitution);
        float attract = 0.4f * looks + 0.2f * height + 0.2f * body + 0.2f * cha;
        float W(Trait t) => (p.Get(a, t) - 0.5f) * 2f;
        float pref = W(Trait.PrefLooks) * (looks - 0.5f) + W(Trait.PrefHeight) * (height - 0.5f) + W(Trait.PrefStrength) * (p.Get(b, Trait.Strength) - 0.5f)
                     + W(Trait.PrefCharisma) * (cha - 0.5f) + W(Trait.PrefWealth) * (WealthIndex(b) - 0.5f);
        float ageA = AgeYears(a), ageB = AgeYears(b);
        float gap = p.Male[a] ? ageA - ageB : ageB - ageA;
        int wives = bMale ? p.WifeCount[b] : 0;
        return attract + Config.PreferenceStrength * pref - wives * Config.WifePenalty - MathF.Abs(gap - 3f) * 0.03f;
    }

    // 구애 계절마다 한 번 계산해 두는 값 (사람 번호별)
    private float[] _attract = Array.Empty<float>();
    private float[] _wealthIdx = Array.Empty<float>();
    private bool[] _taken = Array.Empty<bool>();

    private void PrepareCourtship()
    {
        var p = People;
        int n = p.HighWater;
        if (_attract.Length < n) { _attract = new float[n * 2]; _wealthIdx = new float[n * 2]; _taken = new bool[n * 2]; }
        Array.Clear(_taken, 0, n);
        for (int b = 0; b < n; b++)
        {
            if (!p.IsAliveIndex(b)) continue;
            float body = p.Male[b] ? p.Get(b, Trait.Strength) : p.Get(b, Trait.Constitution);
            _attract[b] = 0.4f * p.Get(b, Trait.Looks) + 0.2f * p.Get(b, Trait.Height) + 0.2f * body + 0.2f * p.Get(b, Trait.Charisma);
            _wealthIdx[b] = WealthIndex(b);
        }
    }

    /// <summary>PrepareCourtship 뒤에만 쓸 수 있는 빠른 짝 점수(혈연 제외). MateScoreNoKin 과 같은 값.</summary>
    private float FastScore(int a, int b, int ageA, int ageB)
    {
        var p = People;
        // 선호 = 가중치(−1~+1) × 상대 특성이 평균에서 벗어난 정도. 후보끼리 차이를 만든다.
        float W(Trait t) => (p.Get(a, t) - 0.5f) * 2f;
        float pref = W(Trait.PrefLooks) * (p.Get(b, Trait.Looks) - 0.5f) + W(Trait.PrefHeight) * (p.Get(b, Trait.Height) - 0.5f)
                     + W(Trait.PrefStrength) * (p.Get(b, Trait.Strength) - 0.5f) + W(Trait.PrefCharisma) * (p.Get(b, Trait.Charisma) - 0.5f)
                     + W(Trait.PrefWealth) * (_wealthIdx[b] - 0.5f);
        float gap = p.Male[a] ? ageA - ageB : ageB - ageA;
        int wives = p.Male[b] ? p.WifeCount[b] : 0;
        return _attract[b] + Config.PreferenceStrength * pref - wives * Config.WifePenalty - MathF.Abs(gap - 3f) * 0.03f;
    }

    private bool Rejected(int a, int bUid) =>
        (People.RejectUid0[a] == bUid && Day < People.RejectUntil0[a]) || (People.RejectUid1[a] == bUid && Day < People.RejectUntil1[a]);

    private void RememberRejection(int a, int bUid)
    {
        var p = People;
        p.RejectUid1[a] = p.RejectUid0[a]; p.RejectUntil1[a] = p.RejectUntil0[a];
        p.RejectUid0[a] = bUid; p.RejectUntil0[a] = Day + Config.DaysPerYear;
    }

    private void Courtship()
    {
        var p = People;
        var hh = Households;
        int cell = Config.CourtshipBucket;
        // 지역 칸별 미혼 남녀 목록
        var menBy = new Dictionary<long, List<int>>();
        var womenBy = new Dictionary<long, List<int>>();
        var courters = new List<int>();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || !hh.IsAlive(p.Household[i])) continue;
            int age = AgeYears(i);
            if (age < 16) continue;
            int h = p.Household[i];
            Dictionary<long, List<int>>? target = null;
            if (p.Male[i])
            {
                if (p.WifeCount[i] == 0 || Wealth(h) >= Config.PolygynyWealthPerWife * (p.WifeCount[i] + 1)) target = menBy;
            }
            else if (!p.IsValid(p.Husband[i]) && age <= 45) target = womenBy;
            if (target == null) continue;
            long k = Key(hh.X[h], hh.Y[h], cell);
            if (!target.TryGetValue(k, out var l)) target[k] = l = new List<int>();
            l.Add(i);
            courters.Add(i);
        }
        if (menBy.Count == 0 || womenBy.Count == 0) return;
        for (int i = courters.Count - 1; i > 0; i--) { int j = _rng.NextInt(i + 1); (courters[i], courters[j]) = (courters[j], courters[i]); }

        PrepareCourtship();
        var taken = _taken;
        Span<int> cand = stackalloc int[Config.CandidateSample];
        Span<float> score = stackalloc float[Config.CandidateSample];
        var near = new List<List<int>>(9);
        foreach (int a in courters)
        {
            if (taken[a]) continue;
            if (!_rng.Chance(0.3f + 0.7f * p.Get(a, Trait.Ambition))) continue;
            if (!PlayerAutoMarry && IsPlayer(a)) continue;   // 플레이어가 직접 맺기로 했으면 스스로 청혼하지 않는다
            // 주변 3×3 지역 칸의 이성 목록
            near.Clear();
            int total = 0;
            var src = p.Male[a] ? womenBy : menBy;
            int ha = p.Household[a];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (src.TryGetValue(Key(hh.X[ha] + dx * cell, hh.Y[ha] + dy * cell, cell), out var l)) { near.Add(l); total += l.Count; }
            if (total == 0) continue;
            // 1) 점수를 매겨 상위 몇 명만 남긴다. 2) 그들만 함께 자랐는지 본다.
            int n = Math.Min(Config.CandidateSample, total);
            int m = 0;
            int ageA = AgeYears(a);
            for (int k = 0; k < n; k++)
            {
                int pick = _rng.NextInt(total), li = 0;
                while (pick >= near[li].Count) { pick -= near[li].Count; li++; }
                int b = near[li][pick];
                if (taken[b] || b == a || Rejected(a, p.Uid[b])) continue;
                cand[m] = b;
                score[m] = FastScore(a, b, ageA, AgeYears(b));
                m++;
            }
            int best = -1;
            float bestScore = float.MinValue;
            for (int tries = 0; tries < 4 && m > 0; tries++)
            {
                int top = 0;
                for (int k = 1; k < m; k++) if (score[k] > score[top]) top = k;
                if (score[top] <= bestScore) break;
                int b = cand[top];
                float sc = score[top] - CoRaisedPenalty(a, b);
                if (sc > bestScore) { bestScore = sc; best = b; }
                score[top] = float.MinValue;
            }
            if (best < 0) continue;
            // 청혼: 받아들일지는 상호작용 정의가 정한다(플레이어 청혼과 같은 셈). 아내 있는 남자면 첩으로 들인다.
            int man = p.Male[a] ? a : best, woman = p.Male[a] ? best : a;
            var res = p.WifeCount[man] > 0
                ? Interact(InteractionId.TakeConcubine, new InteractionContext { Actor = man, Target = woman, Third = -1 })
                : Interact(InteractionId.Propose, new InteractionContext { Actor = a, Target = best, Third = -1 });
            if (res.Accepted)
            {
                taken[a] = true;
                taken[best] = true;
            }
        }
    }
}
