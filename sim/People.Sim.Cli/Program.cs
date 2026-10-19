using System.Diagnostics;
using System.Globalization;
using People.Sim;

// 화면 없는 실행기.
//   bench   [--people N] [--days D] [--seed S]      하루 진행 시간 측정
//   births  [--n N]                                  출생(유전 계산) 속도
//   island  [--couples N] [--plots P] [--years Y] [--seed S] [--out file.csv]
//           섬 하나에 부부 몇 쌍. 연도별 통계 CSV 와 남성 재산·자식 CSV 를 남긴다
//   sexsel  [--years Y] [--seed S] [--pref high|neutral] [--out file.csv]
//           모두가 키 큰 상대를 선호하게(high) 또는 중립(neutral)으로 두고 돌린다
//   inbreed [--singles N] [--plots P] [--years Y] [--seed S] [--out file.csv]
//           유전병 A 빈도를 높여 놓고 돌린다(작은 섬 / 큰 섬 비교용)
var a = args.ToList();
string cmd = a.Count > 0 ? a[0] : "bench";
int Arg(string name, int def) { int i = a.IndexOf(name); return i >= 0 && i + 1 < a.Count ? int.Parse(a[i + 1]) : def; }
string SArg(string name, string def) { int i = a.IndexOf(name); return i >= 0 && i + 1 < a.Count ? a[i + 1] : def; }

// --set 이름=값 (여러 번 가능): SimConfig 수치를 덮어쓴다. 조정 실험용.
SimConfig Config()
{
    var c = new SimConfig();
    for (int i = 0; i < a.Count - 1; i++)
    {
        if (a[i] != "--set") continue;
        var kv = a[i + 1].Split('=');
        var prop = typeof(SimConfig).GetProperty(kv[0]) ?? throw new ArgumentException($"SimConfig 에 {kv[0]} 없음");
        object v = prop.PropertyType == typeof(int) ? (object)int.Parse(kv[1], CultureInfo.InvariantCulture) : float.Parse(kv[1], CultureInfo.InvariantCulture);
        prop.SetValue(c, v);
    }
    return c;
}

void RunYears(World w, int years, string label)
{
    var sw = Stopwatch.StartNew();
    int days = years * w.Config.DaysPerYear;
    for (int d = 0; d < days; d++)
    {
        w.Step();
        if (w.People.AliveCount == 0) { Console.WriteLine($"{label}: {w.Calendar.Year + 1}년에 절멸"); break; }
    }
    Console.WriteLine($"{label}: {years}년, 인구 {w.People.AliveCount}, 가구 {w.Households.AliveCount}, 태어난 사람 {w.Lineage.Count}, {sw.Elapsed.TotalSeconds:F1}초");
}

// 조정 목표(spec 15)와 비교할 요약. 시작 50년은 빼고 계산한다.
void Summary(World w)
{
    var L = w.Lineage;
    // 시작 50년 이후 태어나, 기록이 끝날 때 이미 90살이 넘었을 사람만 센다(아직 살아 있는 사람 때문에 생기는 편향을 없앤다).
    int dpy = w.Config.DaysPerYear, from = 50 * dpy, until = w.Day - 90 * dpy;
    int born = 0, reached16 = 0, adults = 0; double adultDeathAge = 0;
    int women = 0; double kids = 0;
    for (int u = 0; u < L.Count; u++)
    {
        int b = L.Birth(u), d = L.Death(u);
        if (b < from || b > until) continue;
        born++;
        int life = (d < 0 ? w.Day : d) - b;
        if (life >= 16 * dpy)
        {
            reached16++;
            adults++; adultDeathAge += life / (double)dpy;
            if (!L.Male(u) && (d < 0 || life >= 45 * dpy)) { women++; kids += L.Children(u); }
        }
    }
    Console.WriteLine($"  성인까지 생존 {reached16 / (double)Math.Max(1, born):P0} (목표 55~70%), " +
        $"성인 평균 사망 나이 {adultDeathAge / Math.Max(1, adults):F1}세 (목표 55~65), " +
        $"16세 넘긴 여성 평균 출산 {kids / Math.Max(1, women):F1}명 (목표 5~7)");
}

void Save(World w, string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, w.Stats.Csv);
    var men = path.Replace(".csv", "_men.csv");
    using var f = new StreamWriter(men);
    f.WriteLine("wealth,children,max_wives");
    foreach (var m in w.MenAtDeath) f.WriteLine(string.Join(",", m.wealth.ToString(CultureInfo.InvariantCulture), m.children, m.maxWives));
    Console.WriteLine($"저장: {path}, {men}");
}

switch (cmd)
{
    case "bench":
    {
        // 실제 규칙 그대로: 미혼 어른들이 2~4명씩 가구를 이루고, 1인당 4필지의 땅에서 시작한다.
        int people = Arg("--people", 50000), days = Arg("--days", 240);
        var w = new World((ulong)Arg("--seed", 1), people * 4, Config());
        w.SeedSingles(people);
        for (int i = 0; i < 24; i++) w.Step();
        w.Profiling = true;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < days; i++) w.Step();
        Console.WriteLine($"사람 {people}명으로 시작, {days}일: 하루 평균 {sw.Elapsed.TotalMilliseconds / days:F3} ms, 끝 인원 {w.People.AliveCount}");
        foreach (var kv in w.Profile.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Key}: 하루 평균 {kv.Value / days:F3} ms");
        break;
    }
    case "births":
    {
        int n = Arg("--n", 100000);
        var w = new World((ulong)Arg("--seed", 1));
        w.SeedPopulation(1000);
        var ids = Enumerable.Range(0, w.People.HighWater).Select(w.People.IdAt).ToArray();
        var men = ids.Where(i => w.People.Male[i.Index]).ToArray();
        var women = ids.Where(i => !w.People.Male[i.Index]).ToArray();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) w.Birth(men[i % men.Length], women[i % women.Length], 0);
        Console.WriteLine($"출생 {n}건: 건당 {sw.Elapsed.TotalMilliseconds * 1000 / n:F2} µs");
        break;
    }
    case "island":
    {
        var w = new World((ulong)Arg("--seed", 1), Arg("--plots", 400), Config());
        w.SeedCouples(Arg("--couples", 5));
        RunYears(w, Arg("--years", 300), "island");
        Summary(w);
        Save(w, SArg("--out", "out/island.csv"));
        break;
    }
    case "sexsel":
    {
        var w = new World((ulong)Arg("--seed", 1), Arg("--plots", 600), Config());
        w.SeedSingles(Arg("--singles", 120));
        bool high = SArg("--pref", "high") == "high";
        // 선호 유전자를 고정: high = 모두 3 (가중치 +1), neutral = 1,2 (가중치 0)
        for (int i = 0; i < w.People.HighWater; i++)
        {
            var g = w.People.Genome(i);
            for (int k = 0; k < GenomeLayout.Loci(Trait.PrefHeight); k++)
            {
                int l = GenomeLayout.Locus(Trait.PrefHeight, k);
                g[l * 2] = (byte)(high ? 3 : 1);
                g[l * 2 + 1] = (byte)(high ? 3 : 2);
            }
            w.People.ComputePhenotype(i);
        }
        RunYears(w, Arg("--years", 600), $"sexsel-{(high ? "high" : "neutral")}");
        Save(w, SArg("--out", $"out/sexsel_{(high ? "high" : "neutral")}.csv"));
        break;
    }
    case "inbreed":
    {
        var w = new World((ulong)Arg("--seed", 1), Arg("--plots", 60), Config());
        w.SeedSingles(Arg("--singles", 20));
        var rng = new Rng((ulong)Arg("--seed", 1) + 99);
        int l = GenomeLayout.Locus(Marker.DiseaseA);
        for (int i = 0; i < w.People.HighWater; i++)
        {
            var g = w.People.Genome(i);
            g[l * 2] = (byte)(rng.Chance(0.15f) ? 1 : 0);
            g[l * 2 + 1] = (byte)(rng.Chance(0.15f) ? 1 : 0);
        }
        RunYears(w, Arg("--years", 300), "inbreed");
        Save(w, SArg("--out", "out/inbreed.csv"));
        break;
    }
    case "matediag":
    {
        // 섬을 몇 년 돌린 뒤, 미혼 어른 남녀 쌍의 짝 점수·혈연·수락 확률 분포를 본다.
        var w = new World((ulong)Arg("--seed", 1), Arg("--plots", 400), Config());
        w.SeedCouples(Arg("--couples", 5));
        RunYears(w, Arg("--years", 40), "matediag");
        var p = w.People;
        var single = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16
            && (p.Male[i] ? p.WifeCount[i] == 0 : !p.IsValid(p.Husband[i]))).ToList();
        var men = single.Where(i => p.Male[i]).ToList();
        var women = single.Where(i => !p.Male[i]).ToList();
        Console.WriteLine($"미혼 어른: 남 {men.Count}, 여 {women.Count}");
        int pairs = 0, coRaised = 0; double sc = 0, kin = 0, acc = 0;
        foreach (int m in men) foreach (int f in women)
        {
            float r = w.Lineage.Relatedness(p.Uid[m], p.Uid[f]);
            if (w.CoRaisedPenalty(m, f) > 0) coRaised++;
            float s1 = w.MateScore(m, f), s2 = w.MateScore(f, m);
            float ac = 1f / (1f + MathF.Exp(-(s2 - w.Config.AcceptMidpoint) / w.Config.AcceptSpread));
            pairs++; sc += s1; kin += r; acc += ac;
        }
        Console.WriteLine($"쌍 {pairs}, 함께 자란 쌍 {coRaised}, 평균 점수 {sc / Math.Max(1, pairs):F3}, 평균 혈연 {kin / Math.Max(1, pairs):F3}, 평균 수락 {acc / Math.Max(1, pairs):F3}");
        break;
    }
    case "worldmap":
    {
        // 시드로 대륙 전체를 만들어 그린다.
        int size = Arg("--size", 48);
        var sw = Stopwatch.StartNew();
        var map = WorldMap.Generate((uint)Arg("--seed", 1), size);
        Console.WriteLine($"대략 지도 {size}×{size}: {sw.ElapsedMilliseconds} ms");
        sw.Restart();
        string path = SArg("--out", "out/world.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        MapRender.Save(null, map, path, 0, 0, map.Width, map.Height, 1, generateAll: true);
        Console.WriteLine($"세부 {map.GeneratedChunks}청크 생성·그리기: {sw.ElapsedMilliseconds} ms → {path}");
        // 강이 흐르는 곳 하나를 확대 (청크 경계선 확인용)
        var c = map.Coarse!;
        int ri = Enumerable.Range(0, c.W * c.H).Where(i => c.River[i]).OrderByDescending(i => c.Flow[i]).Skip(3).First();
        int zx = (ri % c.W) * WorldMap.CS - 64, zy = (ri / c.W) * WorldMap.CS - 48;
        MapRender.Save(null, map, path.Replace(".png", "_zoom.png"), zx, zy, 160, 112, 4, generateAll: true);
        break;
    }
    case "settle":
    {
        // 생성된 대륙에 부부 몇 쌍을 놓고 정착이 퍼지는 모습을 본다.
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        int years = Arg("--years", 200), every = Arg("--snap", 0);
        string path = SArg("--out", "out/settle.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sw = Stopwatch.StartNew();
        for (int y = 1; y <= years; y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            if (every > 0 && y % every == 0)
                MapRender.Save(w, map, path.Replace(".csv", $"_y{y:D4}.png"), 0, 0, map.Width, map.Height, 1);
            if (w.People.AliveCount == 0) { Console.WriteLine($"{y}년에 절멸"); break; }
        }
        Console.WriteLine($"settle: {years}년, 인구 {w.People.AliveCount}, 가구 {w.Households.AliveCount}, 마을 {w.Villages().count}, 청크 {map.GeneratedChunks}, {sw.Elapsed.TotalSeconds:F1}초");
        Console.WriteLine($"  물에서 80 m 안: 가구 {w.NearWaterFraction():P0}, 경작 가능 땅 {map.FarmableNearWaterFraction(10):P0}");
        Summary(w);
        Save(w, path);
        MapRender.Save(w, map, path.Replace(".csv", ".png"), 0, 0, map.Width, map.Height, 1);
        // 시작 지점 주변 확대
        MapRender.Save(w, map, path.Replace(".csv", "_zoom.png"), w.SpawnX - 120, w.SpawnY - 80, 240, 160, 4);
        break;
    }
    case "people":
    {
        // 정착을 돌린 뒤 가장 큰 마을 사람들의 일, 이름표, 기술, 재능을 본다.
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        int years = Arg("--years", 150);
        for (int d = 0; d < years * w.Config.DaysPerYear; d++) w.Step();
        var vl = w.VillageList;
        var p = w.People;
        int best = Enumerable.Range(0, vl.Count).Where(v => vl.Alive[v]).OrderByDescending(v => vl.Households[v]).First();
        var folk = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && w.Households.IsAlive(p.Household[i]) && w.Households.Village[p.Household[i]] == best).ToList();
        Console.WriteLine($"마을 {best}: {vl.Households[best]}가구, {folk.Count}명");
        string Sk(int i) => string.Join(" ", Enumerable.Range(0, (int)Skill.Count).Where(s => p.GetSkill(i, (Skill)s) >= 0.15f)
            .Select(s => $"{Skills.SkillName((Skill)s)} {p.GetSkill(i, (Skill)s):F2}(재능 {Skills.Talent(p, i, (Skill)s):F2})"));
        Console.WriteLine("-- 일을 맡은 사람");
        foreach (int i in folk.Where(i => p.JobOf[i] != 0))
        {
            var job = (Job)p.JobOf[i];
            var fj = w.Lineage.JobOf(w.Lineage.Father(p.Uid[i])); var mj = w.Lineage.JobOf(w.Lineage.Mother(p.Uid[i]));
            string from = fj == job || mj == job ? "가업" : "새로";
            Console.WriteLine($"  {Skills.JobName(job)} {(p.Male[i] ? "남" : "여")} {(w.Day - p.BirthDay[i]) / w.Config.DaysPerYear}세 [{from}] {string.Join(", ", Skills.Labels(p, i))} | {Sk(i)}");
        }
        Console.WriteLine("-- 도제");
        foreach (int i in folk.Where(i => !p.Master[i].IsNone && p.IsValid(p.Master[i])))
        {
            int m = p.Master[i].Index;
            Console.WriteLine($"  {(w.Day - p.BirthDay[i]) / w.Config.DaysPerYear}세 → {Skills.JobName((Job)p.JobOf[m])} 밑 | {Sk(i)}");
        }
        var adultsV = folk.Where(i => (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16).ToList();
        Console.WriteLine($"-- 성격 축 평균(어른): 활동성 {adultsV.Average(i => p.Get(i, Trait.Diligence)):F2}, 신중함 {adultsV.Average(i => p.Get(i, Trait.Caution)):F2}, 사교성 {adultsV.Average(i => p.Get(i, Trait.Sociability)):F2}, 공격성 {adultsV.Average(i => p.Get(i, Trait.Aggression)):F2}, 욕심 {adultsV.Average(i => p.Get(i, Trait.Ambition)):F2}");
        Console.WriteLine("-- 어른 몇 명");
        foreach (int i in folk.Where(i => p.JobOf[i] == 0 && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16).Take(Arg("--n", 12)))
            Console.WriteLine($"  농부 {(p.Male[i] ? "남" : "여")} {(w.Day - p.BirthDay[i]) / w.Config.DaysPerYear}세 {string.Join(", ", Skills.Labels(p, i))} | {Sk(i)}");
        break;
    }
    case "export":
    {
        // 정착을 돌린 뒤 가장 큰 마을 주변을 화면용 JSON 으로 내보낸다(Godot 이 읽어 그린다).
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        int years = Arg("--years", 150);
        for (int d = 0; d < years * w.Config.DaysPerYear; d++) w.Step();
        var vl = w.VillageList;
        int best = Enumerable.Range(0, vl.Count).Where(v => vl.Alive[v]).OrderByDescending(v => vl.Households[v]).First();
        int rw = Arg("--w", 120), rh = Arg("--h", 90);
        int x0 = vl.CenterX[best] - rw / 2, y0 = vl.CenterY[best] - rh / 2;
        map.EnsureAround(vl.CenterX[best], vl.CenterY[best], Math.Max(rw, rh));
        var inv = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append($"{{\"x0\":{x0},\"y0\":{y0},\"w\":{rw},\"h\":{rh},\"tiles\":[");
        for (int y = y0; y < y0 + rh; y++)
            for (int x = x0; x < x0 + rw; x++)
            {
                if (x != x0 || y != y0) sb.Append(',');
                if (!map.InBounds(x, y)) { sb.Append("[0,0,-1,0,0]"); continue; }
                int id = map.Id(x, y);
                int owner = map.Owner(id);
                byte fl = map.Flags(id);
                int road = (fl & WorldMap.FlagRoad) == 0 ? 0 : (fl & WorldMap.FlagMajor) != 0 ? 2 : 1;
                // 묵은 정도: 0 = 해당 없음, 1 + 해 수 = 버려진 밭(주인이 다시 잡았지만 아직 개간 전이면 그대로)
                int wild = (fl & WorldMap.FlagField) != 0 && (owner < 0 || map.Wild(id) > 0) ? 1 + map.Wild(id) : 0;
                sb.Append($"[{(int)map.Type(id)},{map.ElevationMeters(x, y).ToString("0.##", inv)},{owner},{road},{wild}]");
            }
        sb.Append("],\"houses\":[");
        bool first = true;
        var hh = w.Households;
        var houseIndex = new Dictionary<int, int>();
        var hinfo = new System.Text.StringBuilder();
        var p = w.People;
        var lin = w.Lineage;
        string J(string t) => System.Text.Json.JsonSerializer.Serialize(t);
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            int hx = hh.X[h] - x0, hy = hh.Y[h] - y0;
            if (hx < 0 || hy < 0 || hx >= rw || hy >= rh) continue;
            if (!first) { sb.Append(','); hinfo.Append(','); }
            first = false;
            houseIndex[h] = houseIndex.Count;
            sb.Append($"[{hx},{hy},{hh.Members[h]},{hh.Capacity[h]},{(hh.Food[h] + hh.LandValue[h]).ToString("0", inv)},1]");
            int head = p.IsValid(hh.Head[h]) ? p.Uid[hh.Head[h].Index] : -1;
            int lord = w.MainLandlordOf(h);
            string lordName = lord >= 0 && p.IsValid(hh.Head[lord]) ? Names.Family(lin.FamilyOf(p.Uid[hh.Head[lord].Index])) : "";
            hinfo.Append($"{{\"head\":{J(Names.Full(lin, head))},\"family\":{J(Names.Family(lin.FamilyOf(head)))},\"food\":{hh.Food[h].ToString("0", inv)},\"plots\":{hh.PlotCount[h]},\"cap\":{hh.Capacity[h]},\"village\":{hh.Village[h]},\"founded\":{hh.FoundedDay[h] / w.Config.DaysPerYear},");
            hinfo.Append($"\"class\":{J(World.ClassName(w.ClassOf(h)))},\"leasedOut\":{w.LeasedOutCount(h)},\"leasedIn\":{w.LeasedInCount(h)},\"lord\":{J(lordName)},\"debt\":{w.DebtOf(h).ToString("0", inv)}}}");
        }
        // 빈집: 사는 사람 0, 마지막 값은 상태(1 멀쩡 ~ 0 무너짐)
        foreach (var vh in w.Vacant)
        {
            int hx = vh.X - x0, hy = vh.Y - y0;
            if (hx < 0 || hy < 0 || hx >= rw || hy >= rh) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"[{hx},{hy},0,{vh.Capacity},0,{vh.Condition.ToString("0.##", inv)}]");
            hinfo.Append((hinfo.Length > 0 ? "," : "") + $"{{\"head\":\"\",\"family\":\"\",\"food\":0,\"plots\":0,\"cap\":{vh.Capacity},\"village\":{vh.Village},\"founded\":0}}");
        }
        sb.Append("],\"hinfo\":[").Append(hinfo);
        // 사람: 집 안 식구들. 카드에 보일 것만 내보낸다(재능 수치는 숨기고 겉으로 보이는 짐작만).
        sb.Append("],\"people\":[");
        var wives = new Dictionary<int, List<int>>();
        for (int i = 0; i < p.HighWater; i++)
            if (p.IsAliveIndex(i) && !p.Male[i] && p.IsValid(p.Husband[i])) (wives.TryGetValue(p.Husband[i].Index, out var l) ? l : wives[p.Husband[i].Index] = new()).Add(i);
        bool firstP = true;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || !houseIndex.TryGetValue(p.Household[i], out int hi)) continue;
            int uid = p.Uid[i];
            int age = (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear;
            var g = p.Genome(i);
            bool male = p.Male[i];
            var job = (Job)p.JobOf[i];
            var skills = Enumerable.Range(0, (int)Skill.Count).Select(k => (Skill)k).Where(k => p.GetSkill(i, k) >= 0.1f)
                .OrderByDescending(k => p.GetSkill(i, k)).Select(k => $"[{J(Skills.SkillName(k))},{p.GetSkill(i, k).ToString("0.00", inv)}]");
            var hints = new List<string>();
            foreach (Skill k in Enum.GetValues<Skill>())
            {
                if (k == Skill.Count || p.GetSkill(i, k) < 0.3f) continue;
                float t = Skills.Talent(p, i, k);
                if (t >= 0.72f) hints.Add($"{Skills.SkillName(k)}에 재능이 남다르다");
                else if (t <= 0.3f) hints.Add($"{Skills.SkillName(k)}는 애써 익혔다");
            }
            if (age >= 16 && p.Get(i, Trait.Strength) > 0.75f) hints.Add("힘이 세다");
            if (p.Get(i, Trait.Constitution) < 0.3f) hints.Add("몸이 약하다");
            if (p.Get(i, Trait.Looks) > 0.75f) hints.Add("얼굴이 곱다");
            var health = new List<string>();
            if (Genetics.Expressed(g, Marker.Frailty, male)) health.Add("허약 체질");
            if (Genetics.Expressed(g, Marker.ShortLife, male)) health.Add("단명 기질");
            if (Genetics.Expressed(g, Marker.DiseaseA, male)) health.Add("유전병");
            if (Genetics.Expressed(g, Marker.ColorBlind, male)) health.Add("색맹");
            if (p.LoadHits[i] > 0) health.Add($"타고난 병약함 {p.LoadHits[i]}가지");
            if (p.Inbreeding[i] >= 0.1f) health.Add("가까운 친척 사이의 자식");
            string hair = age >= 58 ? "grey" : Genetics.Expressed(g, Marker.RedHair, male) ? "red" : Genetics.Expressed(g, Marker.Blond, male) ? "blond" : p.Get(i, Trait.HairDark) > 0.5f ? "dark" : "brown";
            string spouse = !male && p.IsValid(p.Husband[i]) ? Names.Full(lin, p.Uid[p.Husband[i].Index])
                : male && wives.TryGetValue(i, out var ws) ? string.Join(", ", ws.Select(x => Names.Full(lin, p.Uid[x]))) : "";
            string master = !p.Master[i].IsNone && p.IsValid(p.Master[i]) ? $"{Names.Full(lin, p.Uid[p.Master[i].Index])} ({Skills.JobName((Job)p.JobOf[p.Master[i].Index])})" : "";
            int f = lin.Father(uid), m = lin.Mother(uid);
            string Parent(int u) => u < 0 ? "" : Names.Full(lin, u) + (lin.Death(u) >= 0 ? " (죽음)" : "") + (lin.JobOf(u) != Job.Farmer ? $" · {Skills.JobName(lin.JobOf(u))}" : "");
            if (!firstP) sb.Append(',');
            firstP = false;
            sb.Append($"{{\"h\":{hi},\"name\":{J(Names.Full(lin, uid))},\"family\":{J(Names.Family(lin.FamilyOf(uid)))},\"male\":{(male ? "true" : "false")},\"age\":{age},");
            string cls = World.ClassName(w.ClassOf(p.Household[i]));
            string jobName = age < 16 ? (master != "" ? "도제" : "아이") : job != Job.Farmer ? Skills.JobName(job) : cls != "" ? cls : "농부";
            sb.Append($"\"job\":{J(jobName)},\"head\":{(hh.Head[p.Household[i]].Index == i ? "true" : "false")},");
            sb.Append($"\"labels\":[{string.Join(",", Skills.Labels(p, i).Select(J))}],\"skills\":[{string.Join(",", skills)}],\"hints\":[{string.Join(",", hints.Select(J))}],");
            sb.Append($"\"health\":[{string.Join(",", health.Select(J))}],\"hair\":\"{hair}\",\"bald\":{(Genetics.Expressed(g, Marker.Baldness, male) && age >= 30 ? "true" : "false")},");
            sb.Append($"\"height\":{p.Get(i, Trait.Height).ToString("0.00", inv)},\"father\":{J(Parent(f))},\"mother\":{J(Parent(m))},\"spouse\":{J(spouse)},");
            sb.Append($"\"children\":{lin.Children(uid)},\"master\":{J(master)}}}");
        }
        sb.Append("]}");
        string path = SArg("--out", "out/region.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"내보냄: {path}, 마을 {best} ({vl.Households[best]}가구), 영역 {rw}×{rh} 타일, 인구 {w.People.AliveCount}");
        break;
    }
    default:
        Console.WriteLine("명령: bench | births | island | sexsel | inbreed | matediag | worldmap | settle");
        break;
}
