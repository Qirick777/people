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
