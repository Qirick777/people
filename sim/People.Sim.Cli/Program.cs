using System.Diagnostics;
using People.Sim;

// 화면 없는 실행기.
//   bench  [--people N] [--days D] [--seed S]   : 하루 진행 시간 측정
//   births [--n N] [--seed S]                    : 유전 계산(출생) 속도 측정
var argsList = args.ToList();
string cmd = argsList.Count > 0 ? argsList[0] : "bench";
int Arg(string name, int def)
{
    int i = argsList.IndexOf(name);
    return i >= 0 && i + 1 < argsList.Count ? int.Parse(argsList[i + 1]) : def;
}

switch (cmd)
{
    case "bench":
    {
        int people = Arg("--people", 50000);
        int days = Arg("--days", 240);
        var w = new World((ulong)Arg("--seed", 1));
        w.SeedPopulation(people);
        for (int i = 0; i < 24; i++) w.Step(); // 예열
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < days; i++) w.Step();
        sw.Stop();
        Console.WriteLine($"사람 {people}명, {days}일: 하루 평균 {sw.Elapsed.TotalMilliseconds / days:F3} ms, 남은 인원 {w.People.AliveCount}");
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
        for (int i = 0; i < n; i++) w.Birth(men[i % men.Length], women[i % women.Length], -1);
        sw.Stop();
        Console.WriteLine($"출생 {n}건: 건당 {sw.Elapsed.TotalMilliseconds * 1000 / n:F2} µs");
        break;
    }
    default:
        Console.WriteLine("명령: bench | births");
        break;
}
