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
        int profFrom = Arg("--profile", 0);   // 마지막 몇 해의 단계별 시간을 잰다
        for (int y = 1; y <= years; y++)
        {
            if (profFrom > 0 && y == years - profFrom + 1) w.Profiling = true;
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            if (every > 0 && y % every == 0)
                MapRender.Save(w, map, path.Replace(".csv", $"_y{y:D4}.png"), 0, 0, map.Width, map.Height, 1);
            if (w.People.AliveCount == 0) { Console.WriteLine($"{y}년에 절멸"); break; }
        }
        Console.WriteLine($"settle: {years}년, 인구 {w.People.AliveCount}, 가구 {w.Households.AliveCount}, 마을 {w.Villages().count}, 청크 {map.GeneratedChunks}, {sw.Elapsed.TotalSeconds:F1}초");
        Console.WriteLine($"  물에서 80 m 안: 가구 {w.NearWaterFraction():P0}, 경작 가능 땅 {map.FarmableNearWaterFraction(10):P0}");
        Summary(w);
        {
            // 문해율: 어른 가운데 글 0.3, 0.6 넘는 사람
            var pp = w.People; int ad = 0, l3 = 0, l6 = 0;
            for (int i = 0; i < pp.HighWater; i++)
            {
                if (!pp.IsAliveIndex(i) || (w.Day - pp.BirthDay[i]) / w.Config.DaysPerYear < 16) continue;
                ad++;
                float lv = pp.GetSkill(i, Skill.Letters);
                if (lv >= 0.3f) l3++;
                if (lv >= 0.6f) l6++;
            }
            var cls = new int[8]; int hn = 0;
            for (int h = 0; h < w.Households.HighWater; h++) if (w.Households.IsAlive(h)) { cls[(int)w.ClassOf(h)]++; hn++; }
            Console.WriteLine("  계층: " + string.Join(", ", Enum.GetValues<LandClass>().Where(c => cls[(int)c] > 0).Select(c => $"{(World.ClassName(c) is { Length: > 0 } nm ? nm : "없음")} {cls[(int)c] * 100.0 / Math.Max(1, hn):F0}%")));
            Console.WriteLine($"  문해율(어른): 글 0.3 넘음 {l3 / (double)Math.Max(1, ad):P0}, 0.6 넘음 {l6 / (double)Math.Max(1, ad):P0}");
        }
        if (profFrom > 0)
            foreach (var kv in w.Profile.OrderByDescending(k => k.Value).Take(15))
                Console.WriteLine($"  {kv.Key}: 한 해 평균 {kv.Value / profFrom:F1} ms");
        long tot = w.Stats.PopYears, big = w.Stats.PopBigYears;
        int allTrades = w.Stats.SexTradesTotal;
        if (big > 0 && tot > big) Console.WriteLine($"  하룻밤 거래(사람·해 천 명당): 30집 넘는 마을 {w.Stats.SexTradesBig * 1000.0 / big:F2}, 작은 마을 {(allTrades - w.Stats.SexTradesBig) * 1000.0 / (tot - big):F2}");
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
    case "families":
    {
        // 상속 방식별로 가문이 어떻게 됐는지 비교한다(시작 가문 = 성 하나).
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        int years = Arg("--years", 300);
        for (int d = 0; d < years * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People; var hh = w.Households; var lin = w.Lineage;
        var members = new Dictionary<int, int>();
        for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i)) { int f = lin.FamilyOf(p.Uid[i]); members[f] = members.GetValueOrDefault(f) + 1; }
        var rows = new Dictionary<int, (int hhs, int plots, int landlords, int rulers, int tenants)>();
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || !p.IsValid(hh.Head[h])) continue;
            int f = lin.FamilyOf(p.Uid[hh.Head[h].Index]);
            var r = rows.GetValueOrDefault(f);
            var c = w.ClassOf(h);
            rows[f] = (r.hhs + 1, r.plots + hh.PlotCount[h], r.landlords + (c == LandClass.Landlord ? 1 : 0), r.rulers + (c == LandClass.Ruler ? 1 : 0), r.tenants + (c == LandClass.Tenant ? 1 : 0));
        }
        Console.WriteLine($"{years}년, 인구 {p.AliveCount}");
        foreach (var pol in new[] { InheritPolicy.Primogeniture, InheritPolicy.Partible })
        {
            var fams = Enumerable.Range(0, lin.Families).Where(f => lin.PolicyOf(f) == pol).ToList();
            int alive = fams.Count(f => members.GetValueOrDefault(f) > 0);
            int people = fams.Sum(f => members.GetValueOrDefault(f));
            var rs = fams.Where(rows.ContainsKey).Select(f => rows[f]).ToList();
            int hhs = rs.Sum(r => r.hhs), plots = rs.Sum(r => r.plots);
            Console.WriteLine($"{(pol == InheritPolicy.Partible ? "균분" : "장자")}: 시작 가문 {fams.Count}, 살아남은 가문 {alive}, 사람 {people}, 가장 수 {hhs}, 가구당 땅 {(hhs > 0 ? plots / (float)hhs : 0):F1}, 지주 {rs.Sum(r => r.landlords)}, 지배자 {rs.Sum(r => r.rulers)}, 소작농 {rs.Sum(r => r.tenants)}");
        }
        Console.WriteLine("지배자:");
        foreach (int h in Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.ClientCount(h) > 0).OrderByDescending(w.ClientCount).Take(8))
        {
            int hu = p.IsValid(hh.Head[h]) ? p.Uid[hh.Head[h].Index] : -1;
            Console.WriteLine($"  {Names.Full(lin, hu)} ({(w.Day - p.BirthDay[hh.Head[h].Index]) / w.Config.DaysPerYear}세): 따르는 집 {w.ClientCount(h)}, 집 등급 {w.ResidenceOf(h)}, 집 세운 해 {hh.FoundedDay[h] / w.Config.DaysPerYear}");
        }
        foreach (var f in members.OrderByDescending(kv => kv.Value).Take(6))
        {
            var r = rows.GetValueOrDefault(f.Key);
            Console.WriteLine($"  {Names.Family(f.Key)} ({(lin.PolicyOf(f.Key) == InheritPolicy.Partible ? "균분" : "장자")}): {f.Value}명, 가구 {r.hhs}, 땅 {r.plots}, 지주 {r.landlords}, 지배자 {r.rulers}, 소작농 {r.tenants}");
        }
        break;
    }
    case "socialdiag":
    {
        // 같은 마을 이성 어른 쌍을 뽑아 짝 점수, 몰래 만나기·하룻밤 거래의 받아들일 확률 분포를 본다.
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        for (int d = 0; d < Arg("--years", 120) * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People; var hh = w.Households;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16 && hh.IsAlive(p.Household[i])).ToList();
        var rng = new Random(1);
        var ms = new List<float>(); var aa = new List<float>(); var sa = new List<float>(); var pr = new List<float>();
        for (int k = 0; k < 4000; k++)
        {
            int x1 = adults[rng.Next(adults.Count)], x2 = adults[rng.Next(adults.Count)];
            if (p.Male[x1] == p.Male[x2] || hh.Village[p.Household[x1]] != hh.Village[p.Household[x2]]) continue;
            ms.Add(w.MateScoreNoKin(x1, x2));
            var c = new InteractionContext { Actor = x1, Target = x2, Third = -1 };
            var af = w.Def(InteractionId.Affair);
            if (af.Can(w, c)) aa.Add(af.Accept(w, c));
            var st = w.Def(InteractionId.SexTrade);
            if (p.Male[x1] && st.Can(w, c)) { sa.Add(st.Accept(w, c)); pr.Add(w.SexPrice(x1, x2)); }
        }
        string Q(List<float> l) { if (l.Count == 0) return "-"; l.Sort(); return $"n={l.Count} p10 {l[l.Count / 10]:F2} p50 {l[l.Count / 2]:F2} p90 {l[l.Count * 9 / 10]:F2} 평균 {l.Average():F3}"; }
        Console.WriteLine($"짝 점수: {Q(ms)}");
        Console.WriteLine($"몰래 만나기 받아들임: {Q(aa)}");
        Console.WriteLine($"하룻밤 거래 받아들임: {Q(sa)}");
        Console.WriteLine($"하룻밤 값: {Q(pr)}");
        break;
    }
    case "loyaldiag":
    {
        // 충성 대상이 있는 어른을 가장(지배자를 섬김)과 식구(자기 집 가장을 섬김)로 나눠 관계·충성 분포를 본다.
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        long nf = 0, nb = 0, nd = 0, nl = 0, nx = 0;
        for (int y = 0; y < Arg("--years", 250); y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            nf += w.Stats.NewFanatics; nb += w.Stats.FanaticsBroken; nd += w.Stats.FanaticsDied; nl += w.Stats.DisloyalLeft; nx += w.Stats.FanaticsLost;
        }
        Console.WriteLine($"광신: 새로 {nf}, 깨짐 {nb}, 광신인 채 죽음 {nd}, 섬길 집이 사라짐 {nx}, 광신까지 평균 {(nf > 0 ? w.Stats.FanaticYearsSum / (double)nf : 0):F1}년 · 충성이 낮아 떠난 집 {nl}");
        var p = w.People; var hh = w.Households;
        var op = new[] { new List<float>(), new List<float>() }; var ly = new[] { new List<float>(), new List<float>() };
        var fan = new int[2];
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || p.LoyalUid[i] < 0) continue;
            int g = hh.Head[p.Household[i]].Index == i ? 0 : 1;
            op[g].Add(Social.Opinion(p, i, p.LoyalUid[i])); ly[g].Add(p.Loyalty[i]);
            if (p.Fanatic[i] != 0) fan[g]++;
        }
        string Q(List<float> l) { if (l.Count == 0) return "-"; l.Sort(); return $"n={l.Count} p10 {l[l.Count / 10]:F2} p50 {l[l.Count / 2]:F2} p90 {l[l.Count * 9 / 10]:F2} p99 {l[l.Count * 99 / 100]:F2}"; }
        string[] name = { "가장→지배자/고용주", "식구→가장" };
        for (int g = 0; g < 2; g++)
            Console.WriteLine($"{name[g]}: 관계 {Q(op[g])}\n    충성 {Q(ly[g])}  광신 {fan[g]}");
        break;
    }
    case "realmdiag":
    {
        // 세력 균형 진단(plan_orgs 11장, 14.3): 큰 세력 셋의 몫, 새로 영주가 된 가문, 꺾이거나 쪼개진 독립 세력, 쿠데타와 독립
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        bool gen = Arg("--gen", 0) > 0;
        if (gen) w.GenerateSettled(new GenOptions { Rulers = Arg("--rulers", 4), VillagesPerRuler = Arg("--vpr", 7) });
        else w.SeedCouples(Arg("--couples", 10));
        int years = Arg("--years", 300);
        var hh = w.Households; var p = w.People;
        if (gen)
        {
            var cl = new int[8];
            for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h)) cl[(int)w.ClassOf(h)]++;
            int wom = 0, mar = 0, kids = 0, old = 0, men = 0;
            var ages = new int[10];
            for (int i = 0; i < p.HighWater; i++)
            {
                if (!p.IsAliveIndex(i)) continue;
                int ag = (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear;
                ages[Math.Min(9, ag / 10)]++;
                if (!p.Male[i] && ag >= 16 && ag <= 42) { wom++; if (p.IsValid(p.Husband[i])) mar++; }
            }
            Console.WriteLine($"  여자 16~42세 {wom}, 혼인 {mar}, 나이(10년 단위) {string.Join("/", ages)}");
            Console.WriteLine($"생성: 사람 {p.AliveCount}, 집 {hh.AliveCount}, 마을 {w.Villages().count}, 단계 {w.Stats.Tier1}/{w.Stats.Tier2}/{w.Stats.Tier3}/{w.Stats.Tier4}");
        }
        var lordFams = new HashSet<int>();
        int Fam0(int h) => p.IsValid(hh.Head[h]) ? w.Lineage.FamilyOf(p.Uid[hh.Head[h].Index]) : -1;
        if (gen) for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h) && w.TierOf(h) >= 1 && Fam0(h) >= 0) lordFams.Add(Fam0(h));   // 생성 때의 지배자 가문은 새 가문이 아니다
        int newLordFams = 0; double houseYears = 0;
        long plots = 0, exposed = 0, purges = 0, strikes = 0, wins = 0, wars = 0, fails = 0, declared = 0, supp = 0, nego = 0, letgo = 0, indep = 0;
        long natF = 0, natR = 0, natO = 0, natL = 0, natSV = 0;
        // 독립 세력 표본: (해, 집, 세운 날, 아래 집 수)
        var samples = new List<(int year, int h, int founded, int below)>();
        var sampleDay = new Dictionary<(int year, int h), int>();   // 집의 세운 날(같은 집인지)
        int broken = 0, judged = 0;
        var whyBroken = new Dictionary<string, int>();
        int brokenBig = 0, judgedBig = 0;
        var flow = new Dictionary<string, long>();
        int Fam(int h) => p.IsValid(hh.Head[h]) ? w.Lineage.FamilyOf(p.Uid[hh.Head[h].Index]) : -1;
        var ends = new Dictionary<string, int>();
        var endAges = new List<int>(); var endBorn = new List<int>(); int endKid = 0, endAdult = 0, endAlive = 0, endNoSon = 0;
        w.LordHouseEnded = (h, heir, below) =>
        {
            if (below < 20) return;
            string k = heir < 0 ? "이을 집 없음" : "혈족 집이 이음";
            ends[k] = ends.GetValueOrDefault(k) + 1;
            int dead = w.DeadHeadOf(h);
            int sons = 0, sonsAway = 0;
            for (int i = 0; i < p.HighWater; i++)
                if (p.IsAliveIndex(i) && p.Male[i] && w.Lineage.Father(p.Uid[i]) == dead && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16) { sons++; if (p.Household[i] != h) sonsAway++; }
            string why = sons == 0 ? "어른 아들 없음" : sonsAway == sons ? "아들이 모두 딴 집" : "아들이 집에 있었음";
            ends[why] = ends.GetValueOrDefault(why) + 1;
            // 대가 끊긴 까닭: 가장의 나이, 낳은 아들, 그 아들들의 끝
            var L = w.Lineage;
            int yr = w.Config.DaysPerYear;
            int born = 0, diedKid = 0, diedAdult = 0, alive = 0, wives = 0;
            for (int u = 0; u < L.Count; u++)
            {
                if (L.Father(u) != dead) continue;
                if (!L.Male(u)) continue;
                born++;
                int dd = L.Death(u);
                if (dd > 0 && dd >= L.Birth(u)) { if ((dd - L.Birth(u)) / yr < 16) diedKid++; else diedAdult++; }
                else alive++;
            }
            int age = dead >= 0 ? (w.Day - L.Birth(dead)) / yr : -1;
            endAges.Add(age); endBorn.Add(born); endKid += diedKid; endAdult += diedAdult; endAlive += alive;
            if (born == 0) endNoSon++;
            ends["아래 집 합"] = ends.GetValueOrDefault("아래 집 합") + below;
        };
        for (int y = 1; y <= years; y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            var st = w.Stats;
            natF += st.NationsFounded; natR += st.NationsRecognized; natO += st.NationsOpposed; natL += st.NationsLost; natSV += st.NationShortVill;
            plots += st.Plots; exposed += st.PlotsExposed; purges += st.Purges; strikes += st.CoupStrikes; wins += st.CoupWins; wars += st.CivilWars; fails += st.CoupFails;
            declared += st.Declared; supp += st.IndepSuppressed; nego += st.IndepNegotiated; letgo += st.IndepLetGo; indep += st.Independences;
            houseYears += hh.AliveCount;
            flow["저울로 떠남"] = flow.GetValueOrDefault("저울로 떠남") + st.BelongLeft; flow["그중 다른 지배자로"] = flow.GetValueOrDefault("그중 다른 지배자로") + st.BelongSwitched;
            flow["충성이 낮아 떠남"] = flow.GetValueOrDefault("충성이 낮아 떠남") + st.DisloyalLeft; flow["독립"] = flow.GetValueOrDefault("독립") + st.Independences;
            flow["전쟁 승리"] = flow.GetValueOrDefault("전쟁 승리") + st.WarWins; flow["새로 따름"] = flow.GetValueOrDefault("새로 따름") + st.NewClients;
            flow["찬탈 뒤 떠남"] = flow.GetValueOrDefault("찬탈 뒤 떠남") + st.UsurpLeft; flow["분가"] = flow.GetValueOrDefault("분가") + st.Splits;
            flow["봉신이 조용히 떠남"] = flow.GetValueOrDefault("봉신이 조용히 떠남") + st.VassalDrift; flow["그 아래 집"] = flow.GetValueOrDefault("그 아래 집") + st.VassalDriftHouses;
            for (int h = 0; h < hh.HighWater; h++)
            {
                if (!hh.IsAlive(h) || w.TierOf(h) < 2) continue;
                int f = Fam(h);
                if (f >= 0 && lordFams.Add(f) && y > 50) newLordFams++;
            }
            if (Arg("--diag", gen ? 1 : 0) > 0 && (y <= 50 ? y % 10 == 0 : y % 25 == 0))
            {
                var cl = new int[8];
                for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h)) cl[(int)w.ClassOf(h)]++;
                int n = Math.Max(1, hh.AliveCount);
                int few = 0, leased = 0, plotsAll = 0, lordPlots = 0;
                for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h)) { plotsAll += hh.PlotCount[h]; if (hh.PlotCount[h] < 2) few++; leased += w.LeasedInCount(h); if (w.TierOf(h) >= 1) lordPlots += hh.PlotCount[h]; }
                float foodAll = 0, foodLords = 0; for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h)) { foodAll += hh.Food[h]; if (w.TierOf(h) >= 1) foodLords += hh.Food[h]; }
                {
                    int wom = 0, mar = 0, preg = 0; float crowd = 0; int hn2 = 0;
                    for (int i = 0; i < p.HighWater; i++)
                    {
                        if (!p.IsAliveIndex(i) || p.Male[i]) continue;
                        int ag = (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear;
                        if (ag < 16 || ag > 42) continue;
                        wom++; if (p.IsValid(p.Husband[i])) mar++; if (p.PregnantUntil[i] >= 0) preg++;
                    }
                    for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h)) { crowd += hh.Crowding(h); hn2++; }
                    Console.WriteLine($"  여자 16~42 {wom}, 혼인 {mar}, 임신 {preg}, 비좁음 평균 {crowd / Math.Max(1, hn2):F2}");
                }
                {
                    var sum = new double[8]; var lab = new double[8]; var cnt = new int[8]; var plotsC = new double[8]; var leasedN = new double[8];
                    for (int h = 0; h < hh.HighWater; h++)
                    {
                        if (!hh.IsAlive(h)) continue;
                        int c = (int)w.ClassOf(h);
                        var (io, it, ir) = w.IncomeOf(h);
                        sum[c] += io + it; lab[c] += hh.Labor[h]; cnt[c]++; plotsC[c] += hh.PlotCount[h]; leasedN[c] += w.LeasedInCount(h);
                    }
                    Console.WriteLine("  일손당 수확: " + string.Join(", ", Enumerable.Range(0, 6).Where(c => cnt[c] > 0).Select(c => $"{(LandClass)c} {sum[c] / Math.Max(0.1, lab[c]):F1}(집 {cnt[c]}, 제 땅 {plotsC[c] / cnt[c]:F1}, 빌림 {leasedN[c] / cnt[c]:F1}, 일손 {lab[c] / cnt[c]:F1})")));
                }
                Console.WriteLine($"  수확/사람 {st.Harvest / Math.Max(1, p.AliveCount):F1}, 식량 {foodAll:F0}(지배자 {foodLords:F0}), 공납 {st.Tribute:F0}, 출생 {st.Births}, 죽음 {st.Deaths}");
                Console.WriteLine($"  땅: 필지 {plotsAll}, 지배자 {lordPlots}, 2필지 못 가진 집 {few}, 빌린 필지 {leased}, 빌린 땅 해 {st.LeasedPlots}");
                Console.WriteLine($"  {y}년: 사람 {p.AliveCount}, 굶어 죽음 {st.HungerDeaths}, 계층 자작 {cl[0] * 100 / n}% 지주 {cl[1] * 100 / n}% 소작 {cl[2] * 100 / n}% 일꾼 {cl[3] * 100 / n}% 지배자 {cl[4] * 100 / n}%");
            }
            if (y % 25 == 0)
            {
                foreach (var sm in samples.Where(s => s.year == y - 100))
                {
                    judged++;
                    bool bigR = sm.below >= 100;
                    if (bigR) judgedBig++;
                    // 꺾임: 그 가문의 어느 집도 홀로 서서 옛 세력의 절반 넘게 쥐고 있지 않다(같은 가문이 이으면 이어진 것)
                    int fam = sm.founded;
                    bool gone = !Enumerable.Range(0, hh.HighWater).Any(k => hh.IsAlive(k) && Fam(k) == fam && w.PatronOf(k) < 0 && w.BelowOf(k) >= 0.5 * sm.below);
                    if (gone)
                    {
                        broken++;
                        if (bigR)
                        {
                            brokenBig++;
                            // 큰 세력이 어떻게 꺾였나: 그 집이 그대로인지, 가장의 가문, 지금 아래 집, 섬기는 곳
                            int h0 = sm.h;
                            bool same = hh.IsAlive(h0) && hh.FoundedDay[h0] == sampleDay[(sm.year, h0)];
                            string now = same ? $"집 그대로, 가장 가문 {(Fam(h0) == fam ? "같음" : "바뀜")}, 아래 {w.BelowOf(h0)}/{sm.below}, {(w.PatronOf(h0) < 0 ? "홀로" : "섬김")}" : "집 없어짐";
                            var famH = Enumerable.Range(0, hh.HighWater).Where(k => hh.IsAlive(k) && Fam(k) == fam).ToList();
                            var bestF = famH.Where(k => w.PatronOf(k) < 0).OrderByDescending(k => w.BelowOf(k)).FirstOrDefault(-1);
                            Console.WriteLine($"  큰 세력 꺾임({sm.year}→{y}년): {now} · 가문 집 {famH.Count}, 홀로 선 가문 집 가운데 가장 큰 아래 {(bestF >= 0 ? w.BelowOf(bestF) : 0)}");
                        }
                        // 갈래: 가문 끊김 / 가문 집이 모두 남 아래 / 홀로 선 가문 집이 여럿(나뉨) / 하나뿐인데 줄어듦
                        var famHouses = Enumerable.Range(0, hh.HighWater).Where(k => hh.IsAlive(k) && Fam(k) == fam).ToList();
                        var freeF = famHouses.Where(k => w.PatronOf(k) < 0 && w.BelowOf(k) > 0).ToList();
                        string why = famHouses.Count == 0 ? "끊김" : freeF.Count == 0 ? (famHouses.Any(k => w.BelowOf(k) > 0) ? "봉신됨" : "세력 잃음")
                                   : freeF.Count > 1 && freeF.Sum(k => w.BelowOf(k)) >= 0.5 * sm.below ? "나뉨" : "줄어듦";
                        whyBroken[why] = whyBroken.GetValueOrDefault(why) + 1;
                    }
                }
                for (int h = 0; h < hh.HighWater; h++)
                    if (hh.IsAlive(h) && w.TierOf(h) >= 2 && w.PatronOf(h) < 0 && Fam(h) >= 0) { samples.Add((y, h, Fam(h), w.BelowOf(h))); sampleDay[(y, h)] = hh.FoundedDay[h]; }   // founded 칸에 가문을 적는다
                // 큰 세력 셋의 몫. 아래 집 수는 해 중간(지배자 셈)에 센 것이라, 그 뒤 떠난 봉신이 두 번 세지 않게 다시 센다
                // 세계를 건드리지 않고 지금 나무를 따라 맨 위마다 센다
                int staleTop = 0;
                for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h) && w.PatronOf(h) < 0 && w.BelowOf(h) > 0) staleTop += w.BelowOf(h) + 1;
                var under = new Dictionary<int, int>();
                for (int h = 0; h < hh.HighWater; h++)
                {
                    if (!hh.IsAlive(h)) continue;
                    int x = h;
                    for (int d = 0; d < 32 && w.PatronOf(x) >= 0; d++) x = w.PatronOf(x);
                    under[x] = under.GetValueOrDefault(x) + 1;
                }
                var tops = under.Values.Where(c => c > 1).ToList();
                tops.Sort((a, b) => b.CompareTo(a));
                double share = tops.Take(3).Sum() / (double)Math.Max(1, hh.AliveCount);
                Console.WriteLine($"{y}년: 집 {hh.AliveCount}, 독립 세력(영주 이상) {samples.Count(s => s.year == y)}, 큰 셋 몫 {share:P0}(다시 세기 전 맨 위 아래 집 합 {staleTop}), 단계 {st.Tier1}/{st.Tier2}/{st.Tier3}/{st.Tier4}");
            }
        }
        if (gen)
            foreach (int h in Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h)).OrderByDescending(h => w.BelowOf(h)).Take(4))
                Console.WriteLine($"  큰 집 {h}: 단계 {w.TierOf(h)}, 아래 {w.BelowOf(h)}, " + string.Join(", ", w.TierProgress(h).Select(r => $"{r.what} {r.have:0}/{r.need:0}{(r.ok ? "✓" : "✗")}")));
        double perK = houseYears > 0 ? newLordFams / (houseYears / 1000.0) * 100.0 : 0;
        Console.WriteLine($"새로 영주가 된 가문(50년 뒤): {newLordFams} → 1,000집·100년당 {perK:F2} (목표 1~3)");
        Console.WriteLine($"100년 동안 꺾이거나 쪼개진 독립 세력: {broken}/{judged} = {(judged > 0 ? broken / (double)judged : 0):P0} (목표 15~40%)");
        Console.WriteLine($"  그중 아래 집 100 넘던 세력: {brokenBig}/{judgedBig} = {(judgedBig > 0 ? brokenBig / (double)judgedBig : 0):P0}, 작은 세력: {broken - brokenBig}/{judged - judgedBig}");
        if (endAges.Count > 0)
        {
            endAges.Sort();
            Console.WriteLine($"  끊긴 집의 가장: 죽은 나이 중앙 {endAges[endAges.Count / 2]}(p10 {endAges[endAges.Count / 10]}, p90 {endAges[endAges.Count * 9 / 10]}), 낳은 아들 평균 {endBorn.Average():F2}, 아들 하나도 못 낳음 {endNoSon}/{endAges.Count}, 아들 끝: 어려서 죽음 {endKid}, 커서 죽음 {endAdult}, 살아 있음 {endAlive}");
        }
        Console.WriteLine($"  아래 20 넘는 지배자 집이 끊김: {string.Join(", ", ends.Select(x => $"{x.Key} {x.Value}"))}");
        Console.WriteLine($"  집의 흐름(모두 더함): {string.Join(", ", flow.Select(x => $"{x.Key} {x.Value}"))}");
        Console.WriteLine($"  꺾인 갈래: {string.Join(", ", whyBroken.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))}");
        Console.WriteLine($"건국: 세움 {natF}, 승인 {natR}, 적대 {natO}, 잃음 {natL}(마을 모자람 해 {natSV}), 지금 {w.Nations.Count()}" + string.Join("", w.Nations.Select(n => $" | {n.Name}")));
        Console.WriteLine($"쿠데타: 꾸밈 {plots}, 들킴 {exposed}, 숙청 {purges}, 거사 {strikes} (자리 빼앗음 {wins}, 내전 {wars}, 실패 {fails})");
        Console.WriteLine($"독립: 선언 {declared}, 진압 {supp}, 협상 {nego}, 체념 {letgo}, 홀로 섬 {indep}");
        break;
    }
    case "orgdiag":
    {
        // 조직 편입 진단: 지배자의 제안, 받음, 바침, 떨어져 나감, 누름. 공개 조직 가운데 붙은 몫
        int seed = Arg("--seed", 3), years = Arg("--years", 40);
        var map = WorldMap.Generate((uint)seed, 48);
        var w = new World((ulong)seed, map, Config());
        w.GenerateSettled(new GenOptions { Rulers = Arg("--rulers", 4), VillagesPerRuler = Arg("--vpr", 7) });
        var trackAtt = new Dictionary<int, (string, int)>();
        long bl = 0, bs = 0, ret = 0;
        long fdm = 0, fmet = 0, fsl = 0, fic = 0, ficw = 0, fsp = 0, fpl = 0, frot = 0, fex = 0;
        long tsn = 0, twn = 0, tcd = 0, tcp = 0, tdf = 0, tco = 0, tsp = 0, spy = 0, dbl = 0;
        long off = 0, taken = 0, pl = 0, br = 0, sup = 0, supw = 0, raids = 0, won = 0, rd = 0, gang = 0, gt = 0, gn = 0; double loot = 0;
        long bcl = 0, bint = 0, bhost = 0;
        long ccap = 0, cfree = 0, cesc = 0, cexe = 0, cpl = 0, ccv = 0, crs = 0, catt = 0, ckill = 0;
        for (int y = 0; y < years; y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            var st = w.Stats;
            if (Arg("--track", 0) > 0)
            {
                foreach (var g in w.Groups.Where(g => g.Alive && World.IsAttached(g))) trackAtt[g.Id] = (g.Name, g.Total);
                foreach (var (id, nm) in trackAtt.ToList())
                {
                    var g = w.GroupById(id);
                    if (g == null) { Console.WriteLine($"  {y + 1}년 붙은 조직 사라짐: {nm.Item1} ({nm.Item2}명)"); trackAtt.Remove(id); }
                    else if (!World.IsAttached(g)) { Console.WriteLine($"  {y + 1}년 붙은 조직 떨어짐: {nm.Item1}, 위 {g.Parent}, 인정 {g.Sanction}"); trackAtt.Remove(id); }
                }
            }
            bl += st.BelongLeft; bs += st.BelongSwitched; ret += st.Retaliations;
            fdm += st.FactionDemands; fmet += st.DemandsMet; fsl += st.FactionSlanders; fic += st.InnerCoups; ficw += st.InnerCoupWins; fsp += st.FactionSplits; fpl += st.Placated; frot += st.CmdRotated; fex += st.Expelled;
            tsn += st.ThreatSeen; twn += st.ThreatWarned; tcd += st.ThreatConditions; tcp += st.ThreatComplied; tdf += st.ThreatDefied; tco += st.ThreatCoopt; tsp += st.ThreatSuppress; spy += st.SpiesFound; dbl += st.DoubleAgents;
            raids += st.Raids; won += st.RaidsWon; rd += st.RaidDeaths; gang += st.BanditRaids; gt += st.GangTries; gn += st.GangNoTarget; loot += st.GangLoot;
            bcl += st.BrothelClients; bint += st.IntelHeard; bhost += st.Hosted;
            ccap += st.Captured; cfree += st.CaptivesFreed; cesc += st.CaptivesEscaped; cexe += st.CaptivesExecuted; cpl += st.CaptivesPlaced; ccv += st.CaptivesConverted; crs += st.Ransoms; catt += st.Attacks; ckill += st.ChaseKills;
            off += st.OrgOffers; taken += st.OrgOffersTaken; pl += st.OrgPledges; br += st.OrgBreaks; sup += st.OrgSuppressions; supw += st.OrgSuppressWins;
            if (y % 5 == 4)
            {
                var big = w.Groups.Where(g => g.Alive && World.IsTop(g) && g.Home < 0 && g.Kind != GroupKind.Bandits && g.Visibility >= Visibility.Public).ToList();
                int att = big.Count(World.IsAttached), big8 = big.Count(g => g.Total >= 8), att8 = big.Count(g => g.Total >= 8 && World.IsAttached(g));
                var ten = Enumerable.Range(0, w.Households.HighWater).Where(h => w.Households.IsAlive(h) && w.ClassOf(h) == LandClass.Tenant).Select(h => w.IncomeOf(h)).ToList();
                var gangs = w.Groups.Where(g => g.Alive && g.Kind == GroupKind.Bandits && World.IsTop(g)).ToList();
                Console.WriteLine($"   소작 집 한 해 소출 {(ten.Count > 0 ? ten.Average(x => x.own + x.tenant - x.rent) : 0):0}(식구 {(ten.Count > 0 ? Enumerable.Range(0, w.Households.HighWater).Where(h => w.Households.IsAlive(h) && w.ClassOf(h) == LandClass.Tenant).Average(h => w.Households.Members[h]) : 0):0.0}), 도적 떼 {gangs.Count}: " + string.Join(" ", gangs.Select(g => $"{g.Total}명/{g.LastIncome:0}")));
                int clients = Enumerable.Range(0, w.Households.HighWater).Count(h => w.Households.IsAlive(h) && w.PatronOf(h) >= 0);
                Console.WriteLine($"   섬기는 집 {clients}/{w.Households.AliveCount}, 떠남 누적 {bl}(옮김 {bs}, 보복 {ret})");
                Console.WriteLine($"   예산: 모자란 갈래 {st.BudgetShort}, 녹봉 {st.Stipends:0} | 파벌 {st.Factions}, 불만 평균 {st.MeanGrievance:0.00}, 요구 {fdm}(들어줌 {fmet}), 헐뜯기 {fsl}, 거사 {fic}(이김 {ficw}), 떨어져 나감 {fsp}, 갈라치기 {fpl}, 지휘자 교대 {frot}, 쫓아냄 {fex}");
                Console.WriteLine($"   신앙: 신도 {st.Believers}, 광신도 {st.Zealots}(올해 새로 {st.NewZealots}, 깨짐 {st.ZealotsBroken}), 믿음 평균 {st.MeanFaith:0.00}, 헌금 {st.TithePaid:0}(거절 {st.TitheRefused}), 바치기 {st.Offerings}번 {st.OfferingTotal:0} | 회칙 " +
                                  string.Join(" ", w.Groups.Where(g => g.Alive && World.IsTop(g) && w.IsFaith(g)).GroupBy(g => g.Tithe).Select(k => $"{World.TitheName(k.Key)} {k.Count()}")));
                Console.WriteLine("   상단: " + string.Join(" | ", w.Groups.Where(g => g.Alive && World.IsTop(g) && g.Kind == GroupKind.Company).Select(g => $"{g.Name} {g.Total}명 곳간 {g.Treasury:0} 번 것 {g.LastIncome:0} 일 {string.Join("·", w.Groups.Where(u => u.Alive && w.RootOf(u).Id == g.Id).SelectMany(u => u.Tasks).Select(t => World.VerbName(t.Verb)).Distinct())} 땅 {g.FactionPlots.Count}")));
                Console.WriteLine($"   붙잡기: 붙잡음 {ccap}, 풀어 줌 {cfree}, 달아남 {cesc}, 처형 {cexe}, 갈래에 둠 {cpl}, 전향 {ccv}, 몸값 {crs}, 지금 {st.CaptivesHeld} | 치기 {catt}, 쫓다 죽임 {ckill} | 유곽 손님 {bcl}, 소식 {bint}, 접대 {bhost}");
                Console.WriteLine($"   기득권: 위협 본 해 {tsn}, 경고 {twn}, 조건 {tcd}(따름 {tcp}, 거역 {tdf}), 끌어들이기 {tco}, 누르기 {tsp} | 첩자 들킴 {spy}, 이중 첩자 {dbl}");
                var bels = Enumerable.Range(0, w.Households.HighWater).Where(h => w.Households.IsAlive(h) && w.PatronOf(h) >= 0 && w.ClientCount(h) == 0).Select(h => w.BelongOf(h, w.PatronOf(h))).ToList();
                if (bels.Count > 0) Console.WriteLine($"   저울 평균: 보호 {bels.Average(b => b.Protect):0.00} 생계 {bels.Average(b => b.Living):0.00} 공포 {bels.Average(b => b.Fear):0.00} 충성 {bels.Average(b => b.Loyal):0.00} 혜택 {bels.Average(b => b.Benefit):0.00} 관습 {bels.Average(b => b.Habit):0.00} 부담 {bels.Average(b => b.Burden):0.00} = {bels.Average(b => b.Total):0.00}, 0.1 아래 {bels.Count(b => b.Total < 0.1f)}");
                Console.WriteLine($"   학교 {w.Groups.Count(World.IsSchool)}곳, 학생 {w.Stats.Students}, 올해 졸업 {w.Stats.Graduated}, 들어옴 {w.Stats.Enrolled}, 감시 세기 합 {w.Groups.Where(g => g.Alive).Sum(g => g.SurveyOwn):0.0}, 빈 지휘 {w.Stats.VacantUnits}, 장악도 평균 {w.Stats.MeanGrip:0.00}");
                Console.WriteLine($"{y + 1}년: 사람 {w.People.AliveCount} 약탈 {raids}(성공 {won}, 떼 {gang}/{gt}번, 목표 없음 {gn}, 떼 약탈량 {loot:0}, 죽음 {rd}) 제안 {off} 받음 {taken} 바침 {pl} 떨어짐 {br} 누름 {sup}(이김 {supw}) | 공개 조직 {big.Count} 중 붙음 {att}, 8명 넘는 것 {big8} 중 붙음 {att8} | " +
                                  string.Join(" ", big.GroupBy(g => g.Kind).Select(k => $"{World.GroupKindName(k.Key)} {k.Count(World.IsAttached)}/{k.Count()}")));
            }
        }
        break;
    }
    case "coupstory":
    {
        // 거사 이야기: 장원 세계를 돌리며 거사가 시작될 때 처지를 적고, 거사·숙청·독립·건국 사건을 해마다 늘어놓는다
        int seed = Arg("--seed", 4), years = Arg("--years", 150);
        var map = WorldMap.Generate((uint)seed, Arg("--size", 48));
        var w = new World((ulong)seed, map, Config());
        w.GenerateSettled(new GenOptions { Rulers = Arg("--rulers", 5), VillagesPerRuler = Arg("--vpr", 10) });
        var keys = new[] { "거사", "자리를 노리", "숙청", "독립", "세웠다", "인정하지 않았다", "나라 지위", "함께하기로", "끼기를 거절", "들켰다", "떨어져 나", "죽임을 당했다", "용서", "쫓겨", "내전" };
        var seen = new HashSet<int>();
        w.StoryLog = (day, text) =>
        {
            if (!keys.Any(text.Contains)) return;
            Console.WriteLine($"  {day / w.Config.DaysPerYear}년: {text}");
            // 거사가 시작되는 그 순간의 처지(같은 해에 바로 일으키는 거사도 남긴다)
            if (text.Contains("자리를 노리기 시작했다"))
                foreach (var x in w.Plots) if (seen.Add(x.Id)) Console.WriteLine($"* {day / w.Config.DaysPerYear}년 거사 #{x.Id} 시작: {w.PlotBrief(x)}");
        };
        for (int y = 0; y < years; y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++)
            {
                w.Step();
                foreach (var x in w.Plots) if (seen.Add(x.Id)) Console.WriteLine($"* {w.Day / w.Config.DaysPerYear}년 거사 #{x.Id} 시작: {w.PlotBrief(x)}");
            }
        }
        break;
    }
    case "conflictdump":
    {
        // 분쟁 기록: 장원 세계를 돌리며 분쟁마다 하루(약 보름) 단위 기록을 남겨 JSON 으로 쓴다(재생 화면용)
        int seed = Arg("--seed", 6), years = Arg("--years", 150);
        var map = WorldMap.Generate((uint)seed, Arg("--size", 48));
        var w = new World((ulong)seed, map, Config()) { KeepConflictLog = true };
        w.GenerateSettled(new GenOptions { Rulers = Arg("--rulers", 5), VillagesPerRuler = Arg("--vpr", 10) });
        for (int y = 0; y < years; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        var list = w.ConflictLog.Select(c => new
        {
            id = c.Id, kind = c.Kind.ToString(), year = c.Since / w.Config.DaysPerYear, attacker = c.AName, defender = c.DName, realm = c.Realm, outcome = c.Outcome,
            frames = c.Frames.Select(f => new
            {
                tick = f.Tick, day = f.Day, an = f.AN, dn = f.DN, ap = MathF.Round(f.AP, 2), dp = MathF.Round(f.DP, 2), am = MathF.Round(f.AM, 2), dm = MathF.Round(f.DM, 2),
                holding = f.Holding, clashed = f.Clashed, aDead = f.ADead, aWounded = f.AWounded, dDead = f.DDead, dWounded = f.DWounded,
                aDeserted = f.ADeserted, dDeserted = f.DDeserted, food = MathF.Round(f.Food), notes = f.Notes,
            }),
        });
        var json = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(SArg("--out", "conflicts.json"), json);
        foreach (var c in w.ConflictLog) Console.WriteLine($"#{c.Id} {(c.Kind == ConflictKind.Independence ? "독립 진압" : c.Kind == ConflictKind.War ? "전쟁" : "거사")} {c.Since / w.Config.DaysPerYear}년 {c.AName} → {c.DName}({c.Realm}): {c.Outcome}, 기록 {c.Frames.Count}장, 죽음 {c.ADead + c.DDead}");
        break;
    }
    case "strategy":
    {
        // 플레이어 전략 실험(여덟 길): 장원 세계의 소작농 가족을 맡아 같은 수만 쓰며 years 년. 길과 시드마다 세계를 새로 만든다
        var seeds = SArg("--seeds", "3,5,7,9,11,13").Split(',').Select(int.Parse).ToArray();
        int years = Arg("--years", 40), tries = Arg("--tries", 8), only = Arg("--only", -1);
        bool trace = Arg("--trace", 0) > 0;
        string[] names = { "가만히", "도적 떼", "보호세", "신도회", "용병", "자리 청하기", "땅 모으기", "편입 뒤 쿠데타",
                           "교단 창시", "상단 대부", "상단 임대", "농장", "공방", "학교", "유곽", "납치단", "포획 도적", "사살 도적",
                           "치안대", "보호세 패", "신학원", "땅+자리", "구휼 보호세", "용병 모병" };
        // 갈래 종류(없으면 조직을 세우지 않는다)와 맡길 일
        GroupKind? KindOf(int s) => s switch
        {
            1 or 15 or 16 or 17 or 19 => GroupKind.Bandits, 3 or 8 or 20 => GroupKind.Brotherhood, 2 or 4 or 7 or 18 or 22 or 23 => GroupKind.Watch,
            9 or 10 => GroupKind.Company, 11 or 12 => GroupKind.Workshop, 13 => GroupKind.School, 14 => GroupKind.Brothel, _ => null,
        };
        Verb[] VerbsOf(int s) => s switch
        {
            1 or 16 or 17 => new[] { Verb.Raid }, 2 => new[] { Verb.Guard, Verb.Collect }, 3 => new[] { Verb.Preach, Verb.Charity }, 4 => new[] { Verb.Mercenary, Verb.Guard },
            8 => new[] { Verb.Worship, Verb.Preach, Verb.Charity }, 9 => new[] { Verb.Trade, Verb.Lend }, 10 => new[] { Verb.Trade, Verb.Lease },
            11 or 12 => new[] { Verb.Produce }, 13 => new[] { Verb.Teach }, 14 => new[] { Verb.Brothel }, 15 => new[] { Verb.Kidnap, Verb.Raid },
            18 => new[] { Verb.Chase, Verb.Guard }, 19 => new[] { Verb.Raid, Verb.Collect }, 20 => new[] { Verb.Worship, Verb.Teach },
            22 => new[] { Verb.Guard, Verb.Collect, Verb.Charity }, 23 => new[] { Verb.Mercenary, Verb.Recruit }, _ => new[] { Verb.Guard },
        };
        var rows = new List<(int strat, int seed, bool alive, float wealth, int below, int tier, int org, int kin, string note, int desc, float standing, int zeal)>();
        foreach (int strat in Enumerable.Range(0, names.Length))
        {
            if (only >= 0 && strat != only) continue;
            foreach (int seed in seeds)
            {
                var map = WorldMap.Generate((uint)seed, 48);
                var w = new World((ulong)seed, map, Config());
                w.GenerateSettled(new GenOptions { Rulers = 3, VillagesPerRuler = 8 });
                for (int y = 0; y < 15; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
                var hh = w.Households; var p = w.People;
                int Age(int i) => (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear;
                int Men(int h) { int n = 0; for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k]) if (p.Male[k] && Age(k) is >= 16 and <= 40) n++; return n; }
                int me = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.ClassOf(h) == LandClass.Tenant && w.PatronOf(h) >= 0).OrderByDescending(h => Men(h) * 10 + hh.Members[h]).First();
                w.PlayerHousehold = me;
                int startUid = p.Uid[hh.Head[me].Index];
                Group? org = null;
                int kin = 0, acts = 0, ok = 0;
                bool Try(InteractionId id, int actor, int target, int third = -1)
                {
                    if (actor < 0 || target < 0 || actor == target) return false;
                    var c = new InteractionContext { Actor = actor, Target = target, Third = third };
                    if (!w.Def(id).Can(w, c)) return false;
                    acts++;
                    bool r = w.Interact(id, c).Accepted;
                    if (r) ok++;
                    return r;
                }
                int HeadOf(int h) => h >= 0 && hh.IsAlive(h) && p.IsValid(hh.Head[h]) ? hh.Head[h].Index : -1;
                for (int y = 0; y < years; y++)
                {
                    int h = w.PlayerHousehold;
                    if (h < 0) break;
                    int head = HeadOf(h);
                    if (head < 0) break;
                    // 온 요구: 편입 제안은 '편입 뒤 쿠데타'만 받는다. 나머지는 NPC처럼
                    foreach (var dm in w.Demands.ToList()) w.AnswerDemand(dm.Id, dm.Kind == InteractionId.OrgOffer ? strat == 7 : null);
                    int lordH = w.PatronOf(h), lordHead = HeadOf(lordH);
                    var mine = w.MembersOf(h).ToList();
                    if (KindOf(strat) != null)
                    {
                        // 남의 손에 넘어간 조직은 버리고 새로 세운다
                        if (org != null && org.Alive && (w.GroupById(org.Id) == null || !mine.Any(m => w.IsLeader(m, org)))) org = null;
                        if (org == null || !org.Alive)
                        {
                            var kind = KindOf(strat)!.Value;
                            org = w.GroupsOf(head).FirstOrDefault(g => g.Kind == kind && w.IsLeader(head, g)) ?? w.FoundGroup(head, kind);
                            if (strat is 8 or 18 or 22) w.SetVisibility(org, Visibility.Public);
                            if (strat == 8) w.FoundReligion(org, "", new[] { (Doctrine.Charity, Strength.Mid), (Doctrine.Order, Strength.Mid), (Doctrine.Ancestors, Strength.Weak) });
                            if (strat is 9 or 10) org.ShareRule = ShareRule.Treasury;
                            if (strat == 11) org.FarmFood = true;
                            if (strat is 15 or 16) org.Kill = false;
                            if (strat is 17 or 19) org.Kill = true;
                            if (strat is 2 or 7) w.SetVisibility(org, Visibility.Public);
                            if (strat == 1) org.ShareRule = ShareRule.Even;
                            if (strat == 3) { w.SetVisibility(org, Visibility.Proclaimed); org.Lesson = Lesson.Doctrine; org.TeachShare = 0.2f; org.TeachManual = true; }
                            if (strat == 4) { org.Lesson = Lesson.Arms; org.TeachShare = 0.2f; org.TeachManual = true; }
                        }
                        int ld = mine.FirstOrDefault(m => w.IsLeader(m, org), head);
                        int v = hh.Village[h];
                        var cands = new List<(int i, float a)>();
                        for (int i = 0; i < p.HighWater; i++)
                        {
                            if (!p.IsAliveIndex(i) || !hh.IsAlive(p.Household[i]) || hh.Village[p.Household[i]] != v || i == ld) continue;
                            var c = new InteractionContext { Actor = ld, Target = i, Third = -1 };
                            var def = w.Def(InteractionId.Recruit);
                            if (def.Can(w, c)) cands.Add((i, def.Accept(w, c)));
                        }
                        foreach (var (i, _) in cands.OrderByDescending(x => x.a).Take(tries)) Try(InteractionId.Recruit, ld, i);
                        var root = w.RootOf(org);
                        var verbs = VerbsOf(strat);
                        // 붙잡은 사람은 해마다 몸값을 청한다(아이도)
                        foreach (var cap in w.Captives.Where(c => c.Root == root.Id).ToList())
                        {
                            int ci = Enumerable.Range(0, p.HighWater).FirstOrDefault(k => p.IsAliveIndex(k) && p.Uid[k] == cap.Uid, -1);
                            int fh = ci >= 0 ? HeadOf(p.Household[ci]) : -1;
                            if (fh >= 0 && fh != ci) Try(InteractionId.Ransom, ld, fh);
                        }
                        org.Tasks.Clear();
                        foreach (var vb in verbs) if (w.CanUseVerb(root, vb, out _)) org.Tasks.Add(new OrgTask { Verb = vb, Village = v, Weight = 1f });
                        if (org.Tasks.Count == 0) org.Tasks.Add(new OrgTask { Verb = verbs[^1], Village = v, Weight = 1f });
                        float tw = org.Tasks.Count; for (int k = 0; k < org.Tasks.Count; k++) { var t = org.Tasks[k]; t.Weight = 1f / tw; org.Tasks[k] = t; }
                        org.Manual = true;
                        if (strat == 7)
                        {
                            // 붙지 않았으면 여섯 명이 넘을 때 스스로 바친다. 붙었으면 거사를 꾸미고, 힘이 넉넉하면 친다
                            if (!World.IsAttached(org) && org.Total >= 6) Try(InteractionId.OrgPledge, ld, lordHead);
                            if (World.IsAttached(org) && HeadOf(org.Sanction) is int sh && sh >= 0)
                            {
                                if (w.PlayerPlot == null) Try(InteractionId.PlotCoup, ld, sh);
                                if (w.PlayerPlot is { } plot)
                                {
                                    foreach (var (c, _) in w.PlotCandidates(plot).OrderByDescending(x => x.chance).Take(3)) Try(InteractionId.PlotRecruit, ld, c);
                                    if (w.StrikeEstimate(plot).ratio > 0.6f) w.Strike(plot);   // 우리 쪽 몫이 60%를 넘으면
                                }
                            }
                        }
                    }
                    if (strat is 5 or 21 && lordHead >= 0)
                    {
                        foreach (int m in mine) if (p.Male[m]) Try(InteractionId.ServeSoldier, m, lordHead);
                        foreach (int m in mine) Try(InteractionId.ServeCollector, m, lordHead);
                        foreach (int m in w.MembersOf(h).ToList()) Try(InteractionId.AskInner, m, lordHead);
                        Try(InteractionId.PetitionFief, head, lordHead);
                    }
                    if (strat is 5 or 6 or 21)
                    {
                        // 아이는 마을에서 일 가진 사람 가운데 가장 잘 받아 줄 사람에게 맡긴다
                        int v = hh.Village[h];
                        foreach (int kid in w.MembersOf(h).Where(k => Age(k) is >= 10 and <= 15 && p.Master[k].IsNone).ToList())
                        {
                            var masters = new List<(int m, float a)>();
                            for (int i = 0; i < p.HighWater; i++)
                            {
                                if (!p.IsAliveIndex(i) || p.JobOf[i] == 0 || !hh.IsAlive(p.Household[i]) || hh.Village[p.Household[i]] != v) continue;
                                var c = new InteractionContext { Actor = head, Target = i, Third = kid };
                                var def = w.Def(InteractionId.Apprentice);
                                if (def.Can(w, c)) masters.Add((i, def.Accept(w, c)));
                            }
                            foreach (var (m, _) in masters.OrderByDescending(x => x.a).Take(2)) if (Try(InteractionId.Apprentice, head, m, kid)) break;
                        }
                    }
                    if (strat is 6 or 21)
                    {
                        // 한 해 먹을 것을 남기고 살 수 있으면 산다(마을에서 팔 만한 집 셋에게 묻는다)
                        int v = hh.Village[h];
                        var sellers = new List<(int s, float a)>();
                        for (int o = 0; o < hh.HighWater; o++)
                        {
                            int oh = HeadOf(o);
                            if (oh < 0 || o == h || (hh.Village[o] != v && o != lordH)) continue;
                            var c = new InteractionContext { Actor = head, Target = oh, Third = -1 };
                            var def = w.Def(InteractionId.BuyLand);
                            if (def.Can(w, c)) sellers.Add((oh, def.Accept(w, c)));
                        }
                        foreach (var (s0, _) in sellers.OrderByDescending(x => x.a).Take(3))
                        {
                            float price = w.LandPrice(p.Household[s0], h);
                            if (w.WealthOf(h) - price < 0.5f * hh.Need[h] * w.Config.DaysPerYear) break;   // 반년 먹을 것은 남긴다
                            Try(InteractionId.BuyLand, head, s0);
                        }
                    }
                    for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
                    if (w.PlayerHousehold != h && w.PlayerHousehold >= 0) kin++;
                    if (trace && y % 5 == 4 && w.PlayerHousehold >= 0)
                    {
                        int ph = w.PlayerHousehold;
                        Console.WriteLine($"   [{names[strat]} {seed}] {y + 1}년: 식량 {hh.Food[ph]:0}, 땅 {hh.PlotCount[ph]}, 아래 {w.BelowOf(ph)}, 조직 {(org != null && org.Alive ? $"{w.RootOf(org).Name} {w.RootOf(org).Total}명{(World.IsAttached(org) ? "(붙음)" : "")}" : "-")}, 일자리 {string.Join("/", w.MembersOf(ph).Where(m => p.JobOf[m] != 0).Select(m => ((Job)p.JobOf[m]).ToString()))}, 수 {ok}/{acts}");
                    }
                }
                int hf = w.PlayerHousehold;
                w.RecountOrgs();
                var o2 = org != null && org.Alive ? w.RootOf(org) : null;
                bool alive = hf >= 0 && hh.IsAlive(hf);
                                // 살아 있는 후손(몇 대든)
                var kids = w.Lineage.ChildrenIndex();
                int desc = 0;
                var q = new Queue<int>(); q.Enqueue(startUid);
                var seen = new HashSet<int> { startUid };
                while (q.Count > 0)
                    foreach (int c1 in kids.GetValueOrDefault(q.Dequeue()) ?? new List<int>())
                    {
                        if (!seen.Add(c1)) continue;
                        if (w.Lineage.Death(c1) < 0) desc++;
                        q.Enqueue(c1);
                    }
                float standing = alive && w.ClientCount(hf) > 0 ? w.RepOfLord(hf).standing : o2 != null ? w.RepOfRoot(o2).standing : 0f;
                int zeal = o2 != null ? w.Members(o2).Count(m => w.IsZealot(p.Uid[m], o2)) : 0;
                rows.Add((strat, seed, alive, alive ? w.WealthOf(hf) + 30f * hh.PlotCount[hf] : 0f, alive ? w.BelowOf(hf) : 0, alive ? w.TierOf(hf) : -1, o2?.Total ?? 0, kin,
                          alive ? $"{World.ClassName(w.ClassOf(hf))}{(w.MembersOf(hf).Any(m => p.JobOf[m] is (byte)Job.Soldier or (byte)Job.Collector) ? "+자리" : "")}{(w.PlayerPlot != null ? "+거사" : "")}" : "끊김", desc, standing, zeal));
                var r = rows[^1];
                Console.WriteLine($"[{names[strat]} {seed}] {(r.alive ? $"재산 {r.wealth:0}, 아래 {r.below}, 단계 {r.tier}, 조직 {r.org}, 후손 {r.desc}, 평판 {r.standing:0.00}, 광신 {r.zeal}, {r.note}" : "대가 끊김")}{(kin > 0 ? $", 혈족 집으로 {kin}번 옮김" : "")}, 수 {ok}/{acts}");
            }
        }
        Console.WriteLine();
        // 점수: 재산 ÷ 100 + 아래 집 ÷ 5 + 단계 × 3 + 조직 ÷ 5 + 후손 ÷ 2 (끊기면 0)
        float Score((int strat, int seed, bool alive, float wealth, int below, int tier, int org, int kin, string note, int desc, float standing, int zeal) r) => r.alive ? r.wealth / 100f + r.below / 5f + Math.Max(0, r.tier) * 3f + r.org / 5f + r.desc / 2f : 0f;
        Console.WriteLine("길 | 살아남음 | 재산 중앙값(최소~최대) | 아래 집 평균(최대) | 단계 오른 판 | 조직 평균 | 후손 평균 | 평판 평균 | 광신 평균 | 점수 중앙값 | 으뜸 판");
        var best = rows.GroupBy(r => r.seed).ToDictionary(g => g.Key, g => g.OrderByDescending(r => Score(r)).First().strat);
        var idle = rows.Where(r => r.strat == 0).Select(r => Score(r)).OrderBy(x => x).ToList();
        float idleMed = idle.Count > 0 ? idle[idle.Count / 2] : 0f;
        int better = 0;
        foreach (var g in rows.GroupBy(r => r.strat))
        {
            var l = g.ToList(); var al = l.Where(x => x.alive).ToList();
            var ws = al.Select(x => x.wealth).OrderBy(x => x).ToList();
            var sc = l.Select(x => Score(x)).OrderBy(x => x).ToList();
            float med = sc[sc.Count / 2];
            if (g.Key != 0 && med > idleMed * 1.2f) better++;
            string W = al.Count > 0 ? $"{ws[ws.Count / 2]:0}({ws[0]:0}~{ws[^1]:0})" : "-";
            Console.WriteLine($"{names[g.Key]} | {al.Count}/{l.Count} | {W} | {(al.Count > 0 ? al.Average(x => x.below) : 0):0.0}({(al.Count > 0 ? al.Max(x => x.below) : 0)}) | {al.Count(x => x.tier >= 1)} | {(l.Average(x => x.org)):0.0} | {(al.Count > 0 ? al.Average(x => x.desc) : 0):0.0} | {(al.Count > 0 ? al.Average(x => x.standing) : 0):0.00} | {l.Average(x => x.zeal):0.0} | {med:0.0} | {best.Count(b => b.Value == g.Key)}");
        }
        Console.WriteLine($"가만히(점수 중앙값 {idleMed:0.0})보다 20% 넘게 나은 길 {better}개, 시드별 으뜸: " + string.Join(" ", best.Select(b => $"{b.Key}:{names[b.Value]}")));
        break;
    }
    case "crimediag":
    {
        // 범죄 진단: 지배자의 벌 세기에 따라 맡은 땅의 범죄가 줄어드는가, 세력 크기에 따라 사건이 충성에 주는 영향
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        var sevCrimes = new double[3]; var sevHouseYears = new double[3]; var sevCaught = new double[3]; var sevRep = new double[3];
        var prev = new Dictionary<int, (int committed, int crimes, int caught, int covered)>();
        long crimes = 0, rep = 0, caught = 0, exec = 0, covered = 0, thefts = 0, assaults = 0, ext = 0, murders = 0, emb = 0, raids = 0;
        int years = Arg("--years", 250);
        for (int y = 0; y < years; y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            var st = w.Stats;
            crimes += st.Crimes; rep += st.Reported; caught += st.Caught; exec += st.Executions; covered += st.Covered;
            thefts += st.Thefts; assaults += st.Assaults; ext += st.Extortions; murders += st.Murders; emb += st.Embezzles; raids += st.RaidCrimes;
            if (y < years / 3) { prev = w.LordLedger.ToDictionary(e => e.Key, e => e.Value); continue; }
            foreach (var (l, e) in w.LordLedger)
            {
                if (!w.Households.IsAlive(l) || w.ClientCount(l) == 0) continue;
                var pe = prev.GetValueOrDefault(l);
                float sev = w.Severity(l);
                int b = sev < 0.35f ? 0 : sev < 0.6f ? 1 : 2;
                sevCrimes[b] += e.committed - pe.committed; sevRep[b] += e.crimes - pe.crimes; sevCaught[b] += e.caught - pe.caught;
                sevHouseYears[b] += w.ClientCount(l);
            }
            prev = w.LordLedger.ToDictionary(e => e.Key, e => e.Value);
        }
        Console.WriteLine($"{years}년: 범죄 {crimes} (도둑질 {thefts}, 폭행 {assaults}, 갈취 {ext}, 살인 {murders}, 빼돌리기 {emb}, 약탈 {raids})");
        Console.WriteLine($"신고 {rep}, 잡음 {caught}, 처형 {exec}, 덮음 {covered}, 사람 {w.People.AliveCount}");
        foreach (var g in w.Crimes.GroupBy(c => c.Type))
            Console.WriteLine($"  {World.CrimeName(g.Key)}: " + string.Join(", ", g.GroupBy(c => c.Verdict).Select(x => $"{World.VerdictName(x.Key)} {x.Count()}")));
        string[] sn = { "너그러움(<0.35)", "보통", "엄함(≥0.6)" };
        for (int b = 0; b < 3; b++)
            Console.WriteLine($"{sn[b]}: 따르는 집 100곳·한 해 범죄 {(sevHouseYears[b] > 0 ? 100 * sevCrimes[b] / sevHouseYears[b] : 0):F2}, 잡는 비율 {(sevRep[b] > 0 ? sevCaught[b] / sevRep[b] : 0):P0} (집·해 {sevHouseYears[b]:F0})");
        string[] zn = { "아래 5집 미만", "5~19집", "20~99집", "100집 이상" };
        for (int b = 0; b < 4; b++)
        {
            var e = w.CrimeSizeLedger.GetValueOrDefault(b);
            Console.WriteLine($"{zn[b]}: 사건 {e.n}, 벌함 {(e.n > 0 ? e.caught / (double)e.n : 0):P0}, 피해자 가장의 눈 평균 {(e.n > 0 ? e.opinion / e.n : 0):+0.0;-0.0}");
        }
        break;
    }
    case "risediag":
    {
        // 오름의 다툼 진단: 알아챔, 반응, 끝(서임, 진압, 참주, 서약)과 지배자 성격별 선택
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        int years = Arg("--years", 300);
        var sum = new Dictionary<string, long>();
        void S(string k, long v) => sum[k] = sum.GetValueOrDefault(k) + v;
        for (int y = 0; y < years; y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            var st = w.Stats;
            S("알아챔", st.RiseNoticed); S("반응", st.RiseActs); S("끌어들임", st.Coopted); S("경고 받아들임", st.Warned); S("흔들어 빼앗은 집", st.Swayed);
            S("서약 받음", st.OathsTaken); S("깎아내림", st.Slanders); S("진압 이김", st.RiseSuppressWins); S("진압 실패(참주)", st.Tyrants); S("서임", st.Invested);
            S("스스로 서약", st.SworeUp); S("머리 위로", st.OverHead);
        }
        Console.WriteLine($"{years}년, 사람 {w.People.AliveCount}, 지금 눈여겨보는 세력 {w.Stats.Rivals}");
        Console.WriteLine(string.Join(", ", sum.Select(e => $"{e.Key} {e.Value}")));
        string[] tn = { "말솜씨", "신중", "공격성" };
        for (int t = 0; t < 3; t++)
        {
            var row = w.RiseActLedger.Where(e => e.Key.trait == t).OrderByDescending(e => e.Value.n);
            Console.WriteLine($"{tn[t]}: " + string.Join(", ", row.Select(e => $"{w.Def(e.Key.act).Name} {e.Value.n}(된 것 {e.Value.ok})")));
        }
        break;
    }
    case "orgprobe":
    {
        // 조직 진단: 해마다 맨 위 조직의 종류, 크기, 맡은 일
        var map = WorldMap.Generate((uint)Arg("--seed", 2), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 2), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        int years = Arg("--years", 300);
        for (int y = 0; y < years; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        foreach (var g in w.Groups.Where(g => g.Alive && g.Parent < 0 && g.Home < 0).OrderBy(g => g.Kind).Take(Arg("--top", 40)))
        {
            w.CanUseVerb(g, Verb.Kidnap, out var why);
            Console.WriteLine($"{World.GroupKindName(g.Kind)} {g.Name} 사람 {g.Total} 갈래 {w.ChildrenOf(g).Count()} {World.VisibilityName(g.Visibility)} 권위 {g.Authority:0} 일: "
                + string.Join(", ", g.Tasks.Select(t => $"{World.VerbName(t.Verb)} {t.Weight:0.00}")) + " / 갈래 일: "
                + string.Join(", ", w.ChildrenOf(g).SelectMany(c => c.Tasks).Select(t => World.VerbName(t.Verb))) + (why != "" ? $" (납치 막힘: {why})" : ""));
        }
        break;
    }
    case "audit":
    {
        // 검토: 깨지면 안 되는 조건을 세고, 여러 집을 맡아 연대기 문장을 모은다
        var map = WorldMap.Generate((uint)Arg("--seed", 2), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 2), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        for (int y = 0; y < Arg("--years", 220); y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        var p = w.People; var hh = w.Households;
        void Check(string what)
        {
            int negFood = 0, negGoods = 0, deadLeader = 0, deadHome = 0, cycles = 0, overSlots = 0, badMembers = 0, negTreasury = 0, patronDead = 0, loops = 0;
            for (int h = 0; h < hh.HighWater; h++)
            {
                if (!hh.IsAlive(h)) continue;
                if (hh.Food[h] < -0.01f) negFood++;
                for (int g = 0; g < World.GoodCount; g++) if (w.GoodsOf(h, (Good)g) < -0.01f) negGoods++;
                int x = h, k = 0;
                while (x >= 0 && k < 40) { x = w.PatronOf(x); k++; }
                if (k >= 40) loops++;
            }
            var memberCount = new Dictionary<int, int>();
            for (int i = 0; i < p.HighWater; i++)
            {
                if (!p.IsAliveIndex(i)) continue;
                int n = w.GroupsOf(i).Count();
                if (n > World.GroupSlots) overSlots++;
                foreach (var g in w.GroupsOf(i)) memberCount[g.Id] = memberCount.GetValueOrDefault(g.Id) + 1;
            }
            foreach (var g in w.Groups.Where(g => g.Alive))
            {
                bool leaderAlive = false;
                for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i) && p.Uid[i] == g.LeaderUid) { leaderAlive = true; break; }
                if (!leaderAlive) { deadLeader++; if (what == "220년") Console.WriteLine($"   죽은 우두머리: {World.GroupKindName(g.Kind)} {g.Name} 지배자 집 {g.Home} 위 {g.Parent} 사람 {g.Members}"); }
                if (g.Home >= 0 && !hh.IsAlive(g.Home)) deadHome++;
                var seen = new HashSet<int>(); var c = g; int depth = 0;
                while (c.Parent >= 0 && depth++ < 40) { if (!seen.Add(c.Id)) { cycles++; break; } c = w.GroupById(c.Parent) ?? c; if (c.Parent < 0) break; }
                if (memberCount.GetValueOrDefault(g.Id) != g.Members) badMembers++;
                if (g.Treasury < -0.01f) negTreasury++;
            }
            Console.WriteLine($"[{what}] 음수 식량 {negFood}, 음수 재화 {negGoods}, 섬김 고리 {loops}, 죽은 우두머리 조직 {deadLeader}, 집 끊긴 지배자 조직 {deadHome}, 조직 고리 {cycles}, 칸 넘침 {overSlots}, 사람 수 어긋난 조직 {badMembers}(해마다 다시 셈), 음수 곳간 {negTreasury}");
        }
        Check("220년");
        // 볼 만한 집을 차례로 맡아 15년씩
        int Best(Func<int, bool> ok, Func<int, float> sc) { int b = -1; float bs = float.MinValue; for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h) && ok(h)) { float v = sc(h); if (v > bs) { bs = v; b = h; } } return b; }
        var picks = new List<(string, Func<int>)>
        {
            ("가장 큰 지배자", () => Best(h => w.PatronOf(h) < 0 && w.ClientCount(h) > 0, h => w.BelowOf(h))),
            ("봉신 영주", () => Best(h => w.TierOf(h) >= 2 && w.PatronOf(h) >= 0, h => w.BelowOf(h))),
            ("무리", () => Best(h => w.TierOf(h) == 1, h => w.ClientCount(h))),
            ("의심받는 세력", () => w.Rivals.Where(r => hh.IsAlive(r.Riser)).OrderByDescending(r => r.Suspicion).Select(r => r.Riser).DefaultIfEmpty(-1).First()),
            ("소작", () => Best(h => w.ClassOf(h) == LandClass.Tenant, h => hh.Members[h])),
            ("상단", () => { var g = w.Groups.Where(x => x.Alive && x.Parent < 0 && x.Kind == GroupKind.Company).OrderByDescending(x => x.Total).FirstOrDefault(); int l = -1; if (g != null) for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i) && p.Uid[i] == g.LeaderUid) l = p.Household[i]; return l; }),
        };
        var lines = new List<string>();
        foreach (var (name, pick) in picks)
        {
            int h = pick();
            if (h < 0) { Console.WriteLine($"{name}: 없음"); continue; }
            w.PlayerHousehold = h;
            int from = w.Chronicle.Count;
            int forks = 0, demands = 0;
            for (int y = 0; y < Arg("--each", 15); y++)
            {
                for (int d = 0; d < w.Config.DaysPerYear; d++) { w.Step(); if (w.TakeFork()) forks++; }
                foreach (var dm in w.Demands.ToList()) { w.AnswerDemand(dm.Id, null); demands++; }
                foreach (var c in w.PendingCases().ToList()) w.PlayerJudge(c.Id, Verdict.Fine);
            }
            Console.WriteLine($"{name}: 집 {h}, 갈림길 {forks}, 요구 {demands}, 연대기 {w.Chronicle.Count - from}줄, 지금 플레이어 집 {w.PlayerHousehold}");
            foreach (var (day, text) in w.Chronicle.Skip(from)) lines.Add($"{name}\t{day / w.Config.DaysPerYear}\t{text}");
            Check(name + " 뒤");
        }
        { int oi = a.IndexOf("--out"); File.WriteAllLines(oi >= 0 && oi + 1 < a.Count ? a[oi + 1] : "audit.txt", lines); }
        break;
    }
    case "familydiag":
    {
        // C 묶음 진단: 첩을 들일 만한 남자, 미혼·과부 여자, 받아들일 확률, 약초꾼, 신앙심 분포
        var map = WorldMap.Generate((uint)Arg("--seed", 2), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 2), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        for (int d = 0; d < Arg("--years", 150) * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People; var hh = w.Households;
        int Age(int i) => (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && Age(i) >= 16 && hh.IsAlive(p.Household[i])).ToList();
        var men = adults.Where(i => p.Male[i] && p.WifeCount[i] > 0).ToList();
        var richMen = men.Where(i => { int h = p.Household[i]; return w.ClientCount(h) > 0 || hh.Food[h] + hh.LandValue[h] >= 500f * (p.WifeCount[i] + 1); }).ToList();
        var single = adults.Where(i => !p.Male[i] && !p.IsValid(p.Husband[i]) && Age(i) <= 40).ToList();
        var piety = adults.Select(i => p.Piety[i]).OrderBy(x => x).ToList();
        Console.WriteLine($"어른 {adults.Count}, 혼인한 남자 {men.Count}, 첩 들일 재산 {richMen.Count} (욕심>0.5·신앙<0.5: {richMen.Count(i => p.Get(i, Trait.Ambition) > 0.5f && p.Piety[i] < 0.5f)})");
        Console.WriteLine($"40세 이하 미혼·과부 여자 {single.Count}, 신앙심 p10 {piety[piety.Count / 10]:F2} p50 {piety[piety.Count / 2]:F2} p90 {piety[piety.Count * 9 / 10]:F2}");
        var acc = new List<float>();
        var cd = w.Def(InteractionId.TakeConcubine);
        foreach (int m in richMen) foreach (int f in single.Take(40))
        {
            var c = new InteractionContext { Actor = m, Target = f, Third = -1 };
            if (cd.Can(w, c)) acc.Add(cd.Accept(w, c));
        }
        acc.Sort();
        if (acc.Count > 0) Console.WriteLine($"첩 받아들임: n={acc.Count} p50 {acc[acc.Count / 2]:F2} p90 {acc[acc.Count * 9 / 10]:F2}");
        int healers = adults.Count(i => p.JobOf[i] == (byte)Job.Healer), workers = adults.Count(i => p.JobOf[i] == (byte)Job.SexWorker);
        int bigV = Enumerable.Range(0, w.VillageList.Count).Count(v => w.VillageList.Alive[v] && w.VillageList.Households[v] >= 30);
        Console.WriteLine($"약초꾼 {healers}, 몸 파는 이 {workers}, 30집 넘는 마을 {bigV}, 임신 중 {adults.Count(i => p.PregnantUntil[i] >= 0)}");
        break;
    }
    case "groupdiag":
    {
        // 모임 진단: 마을마다 도적 떼·자경단·신도회 후보 수(뜻이 0.35를 넘는 어른)
        var map = WorldMap.Generate((uint)Arg("--seed", 1), Arg("--size", 48));
        var w = new World((ulong)Arg("--seed", 1), map, Config());
        w.SeedCouples(Arg("--couples", 10));
        for (int d = 0; d < Arg("--years", 200) * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People; var hh = w.Households;
        var cls = new Dictionary<string, int>();
        var cand = new int[3];
        var aggr = new List<float>();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear < 16 || !hh.IsAlive(p.Household[i])) continue;
            string c = World.ClassName(w.ClassOf(p.Household[i]));
            cls[c] = cls.GetValueOrDefault(c) + 1;
            if (p.Male[i]) aggr.Add(p.Get(i, Trait.Aggression));
            for (int k = 0; k < 3; k++) if (w.GroupWantPublic(i, (GroupKind)k) > 0.35f) cand[k]++;
        }
        aggr.Sort();
        Console.WriteLine("계층: " + string.Join(", ", cls.Select(kv => $"{(kv.Key == "" ? "없음" : kv.Key)} {kv.Value}")));
        Console.WriteLine($"남자 공격성 p50 {aggr[aggr.Count / 2]:F2} p90 {aggr[aggr.Count * 9 / 10]:F2}");
        Console.WriteLine($"뜻 0.35 넘는 어른: 자경단 {cand[0]}, 도적 떼 {cand[1]}, 신도회 {cand[2]}");
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
        int fx = vl.CenterX[best], fy = vl.CenterY[best];
        if (SArg("--focus", "village") == "ruler")
        {
            // 가장 많이 거느린 지배자의 집을 가운데로
            int lord = Enumerable.Range(0, w.Households.HighWater).Where(w.Households.IsAlive).OrderByDescending(w.ClientCount).First();
            fx = w.Households.X[lord]; fy = w.Households.Y[lord];
            Console.WriteLine($"지배자 집: 따르는 집 {w.ClientCount(lord)}, 집 등급 {w.ResidenceOf(lord)}, 식량 {w.Households.Food[lord]:F0}");
        }
        int x0 = fx - rw / 2, y0 = fy - rh / 2;
        map.EnsureAround(fx, fy, Math.Max(rw, rh));
        string json = RegionView.Json(w, x0, y0, rw, rh);
        string path = SArg("--out", "out/region.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, json);
        Console.WriteLine($"내보냄: {path}, 마을 {best} ({vl.Households[best]}가구), 영역 {rw}×{rh} 타일, 인구 {w.People.AliveCount}");
        break;
    }
    default:
        Console.WriteLine("명령: bench | births | island | sexsel | inbreed | matediag | worldmap | settle");
        break;
}
