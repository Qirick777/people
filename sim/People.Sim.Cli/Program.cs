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
        object v = prop.PropertyType == typeof(int) ? int.Parse(kv[1], CultureInfo.InvariantCulture) : float.Parse(kv[1], CultureInfo.InvariantCulture);
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
        int pairs = 0, forbidden = 0; double sc = 0, kin = 0, acc = 0;
        foreach (int m in men) foreach (int f in women)
        {
            float r = w.Lineage.Relatedness(p.Uid[m], p.Uid[f]);
            if (r > w.Config.KinForbidden) { forbidden++; continue; }
            float s1 = w.MateScore(m, f), s2 = w.MateScore(f, m);
            float ac = 1f / (1f + MathF.Exp(-(s2 - w.Config.AcceptMidpoint) / w.Config.AcceptSpread));
            pairs++; sc += s1; kin += r; acc += ac;
        }
        Console.WriteLine($"가능한 쌍 {pairs}, 금지 쌍 {forbidden}, 평균 점수 {sc / Math.Max(1, pairs):F3}, 평균 혈연 {kin / Math.Max(1, pairs):F3}, 평균 수락 {acc / Math.Max(1, pairs):F3}");
        break;
    }
    default:
        Console.WriteLine("명령: bench | births | island | sexsel | inbreed | matediag");
        break;
}
