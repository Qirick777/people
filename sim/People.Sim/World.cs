namespace People.Sim;

/// <summary>
/// 시뮬레이션 전체 상태와 하루 진행. Godot 에 의존하지 않는다.
/// 1단계: 사람, 유전, 사망, 짝짓기, 임신과 출산, 가구, 땅, 농사, 굶주림.
/// </summary>
public sealed class World
{
    public SimConfig Config { get; }
    public PersonStore People { get; } = new();
    public HouseholdStore Households { get; } = new();
    public Lineage Lineage { get; } = new();
    public Land Land { get; }
    public Mortality Mortality { get; }
    public YearStats Stats { get; } = new();
    public int Day { get; private set; }
    public Calendar Calendar => new(Day, Config);
    public float Weather { get; private set; } = 1f;

    /// <summary>남성이 죽을 때(40세 이상) 남기는 기록. 재산과 자식 수의 관계를 본다.</summary>
    public List<(float wealth, int children, int maxWives)> MenAtDeath { get; } = new();

    private Rng _rng;

    public World(ulong seed, int plots = 400, SimConfig? config = null)
    {
        Config = config ?? SimConfig.Default;
        Mortality = new Mortality(Config);
        _rng = new Rng(seed);
        Land = new Land(plots, ref _rng);
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
        Genetics.Randomize(People.Genome(id.Index), male, ref _rng);
        People.ComputePhenotype(id.Index);
        if (ageYears >= 16) People.GrownUp[id.Index] = true;
        return id;
    }

    /// <summary>시작 상황: 젊은 부부 couples 쌍, 가구마다 필지와 한 계절 식량.</summary>
    public void SeedCouples(int couples, int plotsEach = 3)
    {
        for (int k = 0; k < couples; k++)
        {
            int h = Households.Add(0, 6f * 2 * Config.DaysPerSeason, Config.HutCapacity, Day);
            var man = CreateFounder(true, 18 + _rng.NextInt(8), h);
            var woman = CreateFounder(false, 16 + _rng.NextInt(8), h);
            Households.Head[h] = man;
            Marry(man.Index, woman.Index, 0.5f);
            Land.Claim(h, plotsEach, Households);
        }
    }

    /// <summary>시작 상황: 미혼 어른 count 명, 각자 부모 집처럼 2~4명씩 가구를 이룬다.</summary>
    public void SeedSingles(int count)
    {
        int made = 0;
        while (made < count)
        {
            int size = Math.Min(2 + _rng.NextInt(3), count - made);
            int h = Households.Add(0, 8f * size * Config.DaysPerSeason, Config.HutCapacity + 2, Day);
            for (int i = 0; i < size; i++)
            {
                var p = CreateFounder(_rng.NextBool(), 16 + _rng.NextInt(10), h);
                if (i == 0) Households.Head[h] = p;
            }
            Land.Claim(h, (int)MathF.Ceiling(size * 1.5f), Households);
            made += size;
        }
    }

    /// <summary>
    /// 시작 인구를 무작위로 만든다(벤치마크용). 혼인·가구 없이 나이만 고르게.
    /// </summary>
    public void SeedPopulation(int count, int maxAgeYears = 50)
    {
        // 벤치마크용: 식량이 끝없는 큰 가구 하나
        int h = Households.Add(0, 1e12f, 999999, Day);
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
        p.Uid[child.Index] = Lineage.Add(male, Day, p.PregnancyFatherUid[mother], p.Uid[mother]);
        Genetics.Inherit(p.FatherGenomeBuffer(mother), p.Genome(mother), p.Genome(child.Index),
            male, Config.MutationRate, ref _rng);
        p.ComputePhenotype(child.Index);
        Stats.Births++;
        if (Genetics.Expressed(p.Genome(child.Index), Marker.DiseaseA, male)) Stats.DiseaseABirths++;
        return child;
    }

    /// <summary>벤치마크·테스트용: 두 사람 사이에 바로 아이를 만든다.</summary>
    public PersonId Birth(PersonId father, PersonId mother, int household)
    {
        Genome(father.Index).CopyTo(People.FatherGenomeBuffer(mother.Index));
        People.PregnancyFather[mother.Index] = father;
        People.PregnancyFatherUid[mother.Index] = People.Uid[father.Index];
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
            Timed("부패", Spoil);
            Timed("구애", Courtship);
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
        RecountHouseholds();
        Splits();
        ClaimLand();
        UpgradeHouses();
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
            hh.Need[h] += NeedByAge(age);
            hh.Labor[h] += LaborByAge(age) * (0.7f + 0.6f * p.Get(i, Trait.Diligence)) * (0.8f + 0.4f * MathF.Min(1f, p.Get(i, Trait.Strength)));
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

    private void Harvest()
    {
        var hh = Households;
        var land = Land;
        float total = 0;
        // 가구마다 일할 수 있는 필지 수만큼, 비옥한 것부터 부친다.
        var worked = new bool[land.Count];
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.PlotCount[h] == 0) continue;
            float capacity = hh.Labor[h] * Config.PlotsPerWorker;
            var mine = land.PlotsOf(h);
            mine.Sort((a, b) => land.Fertility[b].CompareTo(land.Fertility[a]));
            float yield = 0;
            for (int k = 0; k < mine.Count && capacity > 0; k++)
            {
                float part = MathF.Min(1f, capacity);
                int plot = mine[k];
                yield += Config.PlotYield * land.Fertility[plot] * Weather * part;
                worked[plot] = part > 0.5f;
                capacity -= 1f;
            }
            hh.Food[h] += yield;
            total += yield;
        }
        for (int i = 0; i < land.Count; i++)
        {
            if (worked[i]) land.Fertility[i] = MathF.Max(0.1f, land.Fertility[i] - Config.FertilityDeclinePerYear);
            else land.Fertility[i] = MathF.Min(land.BaseFertility[i], land.Fertility[i] + Config.FertilityRecoverPerYear);
        }
        Stats.Harvest += total;
    }

    // ------------------------------------------------------------------ 땅, 분가, 집

    private float Wealth(int h) => Households.Food[h] + Households.LandValue[h];

    private void ClaimLand()
    {
        var hh = Households;
        var order = ShuffledHouseholds();
        foreach (int h in order)
        {
            // 일할 수 있는 양보다 넉넉히 잡는다. 남는 땅은 쉬게 되어 비옥도가 돌아온다(돌려짓기가 저절로 생긴다).
            int want = (int)MathF.Ceiling(hh.Labor[h] * Config.PlotsPerWorker * Config.LandReserve);
            if (hh.PlotCount[h] < want) Land.Claim(h, want - hh.PlotCount[h], hh);
            // 일손이 줄어 부칠 수 없는 땅이 너무 많으면 덜 비옥한 것부터 내놓는다.
            int keep = (int)MathF.Ceiling(hh.Labor[h] * Config.PlotsPerWorker * Config.LandRelease);
            if (hh.PlotCount[h] > keep) Land.ReleaseWorst(h, hh.PlotCount[h] - keep, hh);
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

                int nh = hh.Add(hh.Village[h], 0f, Config.HutCapacity, Day);
                int got = Land.Unowned() >= 2 ? Land.Claim(nh, 3, hh) : 0;
                if (got < 2)
                {
                    // 빈 땅이 없으면 본가 땅을 나눠 받는다(균분 관습).
                    int share = hh.PlotCount[h] / 2;
                    if (share + got >= 2) Land.Transfer(h, nh, share, hh);
                    else { Land.Release(nh, hh); hh.Remove(nh); continue; }
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
                Stats.Splits++;
            }
        }
        RecountHouseholds();
    }

    private void UpgradeHouses()
    {
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Capacity[h] >= Config.MaxCapacity) continue;
            if (hh.Crowding(h) > 1.2f && Surplus(h) > 1.3f && hh.Food[h] > Config.HouseUpgradeCost * 2)
            {
                hh.Food[h] -= Config.HouseUpgradeCost;
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
                p.GrownUp[i] = true;
            }

            float situ = 1f;
            if (hungry) situ *= age < 5 || age >= 55 ? Config.HungerMortalityVulnerable : Config.HungerMortality;
            if (hh.IsAlive(h)) situ *= 1f + Config.CrowdingMortality * MathF.Max(0f, hh.Crowding(h) - 1f);
            if (age < 5 && Genetics.Expressed(p.Genome(i), Marker.DiseaseA, p.Male[i])) situ *= 4f;
            float d = Mortality.Daily(age, p.Get(i, Trait.Longevity), p.Get(i, Trait.Constitution), situ);
            if (_rng.Chance(d))
            {
                Die(i, hungry);
                continue;
            }

            if (!p.Male[i])
            {
                if (p.PregnantUntil[i] >= 0 && Day >= p.PregnantUntil[i]) GiveBirth(i, age);
                else if (p.PregnantUntil[i] < 0) TryConceive(i, age);
            }
        }
    }

    private void TryConceive(int w, int age)
    {
        var p = People;
        if (age < 16 || age > 42 || Day < p.CooldownUntil[w]) return;
        var husband = p.Husband[w];
        if (!p.IsValid(husband)) return;
        int m = husband.Index;
        int h = p.Household[w];
        if (!Households.IsAlive(h)) return;
        if (Genetics.Expressed(p.Genome(w), Marker.DiseaseB, false) || Genetics.Expressed(p.Genome(m), Marker.DiseaseB, true)) return;

        float ageK = age <= 30 ? 1f : 1f - 0.8f * (age - 30) / 12f;
        float s = Surplus(h);
        float foodK = s < 0.3f ? 0.05f : s < 1f ? 0.05f + 0.95f * (s - 0.3f) / 0.7f : s < 2f ? 1f + 0.2f * (s - 1f) : 1.2f;
        float crowdK = 1f / MathF.Max(1f, Households.Crowding(h));
        float coupleK = 0.7f + 0.3f * Math.Clamp(p.CoupleScore[w], 0f, 1f);
        float husbandK = AgeYears(m) >= 50 ? 0.7f : 1f;
        float prob = Config.ConceptionBasePerDay * (0.5f + p.Get(w, Trait.Fertility)) * ageK * foodK * crowdK * coupleK * husbandK;
        if (!_rng.Chance(prob)) return;
        p.PregnantUntil[w] = Day + Config.PregnancyDays;
        p.PregnancyFather[w] = husband;
        p.PregnancyFatherUid[w] = p.Uid[m];
        p.Genome(m).CopyTo(p.FatherGenomeBuffer(w));
    }

    private void GiveBirth(int w, int age)
    {
        var p = People;
        p.PregnantUntil[w] = -1;
        p.CooldownUntil[w] = Day + Config.PostpartumCooldownDays;
        int h = p.Household[w];
        float con = p.Get(w, Trait.Constitution);
        int n = 1;
        float twin = Genetics.Expressed(p.Genome(w), Marker.Twinning, false) ? Config.TwinWithTrait : Config.TwinBase;
        if (_rng.Chance(Config.TripletRate)) n = 3;
        else if (_rng.Chance(twin)) n = 2;
        if (n > 1) Stats.MultipleBirths++;
        for (int k = 0; k < n; k++)
        {
            if (_rng.Chance(Config.StillbirthBase * (1.5f - con) * (n > 1 ? 2f : 1f))) { Stats.Stillbirths++; continue; }
            BirthChild(w, h);
            if (p.BirthsGiven[w] < 255) p.BirthsGiven[w]++;
        }
        float maternal = Config.MaternalDeathBase * (age >= 35 ? 2f : 1f) * (1.5f - con) * (p.BirthsGiven[w] >= 6 ? 1.5f : 1f) * (n > 1 ? 1.5f : 1f);
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
        Lineage.MarkDeath(p.Uid[i], Day);

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
        for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
        {
            int age = AgeYears(k);
            if (age >= 12) anyAdult = true;
            int score = age + (p.Male[k] && age >= 16 ? 1000 : 0) + (age >= 16 ? 500 : 0);
            if (score > bestScore) { bestScore = score; best = k; }
        }
        if (best >= 0 && anyAdult)
        {
            hh.Head[h] = p.IdAt(best);
            return;
        }
        // 어른이 없다: 아이들을 혈연이 가장 가까운 가구로 보내고 가구를 없앤다.
        foreach (int k in MembersOf(h))
        {
            int target = ClosestKinHousehold(k, h);
            Leave(k);
            Join(k, target);
        }
        // 남은 사람이 없거나 아이만 있던 가구는 없앤다.
        Land.Release(h, hh);
        hh.Remove(h);
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
        p.CoupleScore[woman] = coupleScore;
        int target = p.Household[man], old = p.Household[woman];
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
    public float MateScore(int a, int b) => MateScoreNoKin(a, b) - Lineage.Relatedness(People.Uid[a], People.Uid[b]) * Config.KinPenalty;

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
        var men = new List<int>();
        var women = new List<int>();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || !Households.IsAlive(p.Household[i])) continue;
            int age = AgeYears(i);
            if (age < 16) continue;
            if (p.Male[i])
            {
                if (p.WifeCount[i] == 0 || Wealth(p.Household[i]) >= Config.PolygynyWealthPerWife * (p.WifeCount[i] + 1)) men.Add(i);
            }
            else if (!p.IsValid(p.Husband[i]) && age <= 45) women.Add(i);
        }
        if (men.Count == 0 || women.Count == 0) return;
        var courters = new List<int>(men.Count + women.Count);
        courters.AddRange(men);
        courters.AddRange(women);
        for (int i = courters.Count - 1; i > 0; i--) { int j = _rng.NextInt(i + 1); (courters[i], courters[j]) = (courters[j], courters[i]); }

        PrepareCourtship();
        var taken = _taken;
        Span<int> cand = stackalloc int[Config.CandidateSample];
        Span<float> score = stackalloc float[Config.CandidateSample];
        foreach (int a in courters)
        {
            if (taken[a]) continue;
            if (!_rng.Chance(0.3f + 0.7f * p.Get(a, Trait.Ambition))) continue;
            var pool = p.Male[a] ? women : men;
            // 1) 혈연을 빼고 점수를 매겨 상위 몇 명만 남긴다. 2) 그들만 혈연을 계산한다.
            int n = Math.Min(Config.CandidateSample, pool.Count);
            int m = 0;
            int ageA = AgeYears(a);
            for (int k = 0; k < n; k++)
            {
                int b = pool[_rng.NextInt(pool.Count)];
                if (taken[b] || b == a || Rejected(a, p.Uid[b])) continue;
                cand[m] = b;
                score[m] = FastScore(a, b, ageA, AgeYears(b));
                m++;
            }
            int best = -1;
            float bestScore = float.MinValue, bestR = 0f;
            for (int tries = 0; tries < 4 && m > 0; tries++)
            {
                int top = 0;
                for (int k = 1; k < m; k++) if (score[k] > score[top]) top = k;
                if (score[top] <= bestScore) break;
                int b = cand[top];
                float r = Lineage.Relatedness(p.Uid[a], p.Uid[b]);
                float sc = score[top] - r * Config.KinPenalty;
                if (r <= Config.KinForbidden && sc > bestScore) { bestScore = sc; best = b; bestR = r; }
                score[top] = float.MinValue;
            }
            if (best < 0) continue;
            float back = FastScore(best, a, AgeYears(best), ageA) - bestR * Config.KinPenalty;
            float accept = 1f / (1f + MathF.Exp(-(back - Config.AcceptMidpoint) / Config.AcceptSpread));
            if (_rng.Chance(accept))
            {
                int man = p.Male[a] ? a : best, woman = p.Male[a] ? best : a;
                if (p.WifeCount[man] > 0) Stats.Polygynous++;
                Stats.Marriages++;
                Stats.MarriageKinSum += bestR;
                Marry(man, woman, Math.Clamp((bestScore + back) * 0.5f, 0f, 1f));
                taken[woman] = true;
                taken[man] = true;
            }
            else RememberRejection(a, p.Uid[best]);
        }
    }
}
