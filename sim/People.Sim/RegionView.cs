using System.Globalization;
using System.Text;

namespace People.Sim;

/// <summary>
/// 화면용 지역 JSON. 타일, 집, 집 정보, 사람(카드에 보일 것만)을 담는다. CLI 내보내기와 Godot 실시간 화면이 같이 쓴다.
/// 사람마다 "i"(사람 번호)와 "uid", 집마다 hinfo 의 "id"(가구 번호)를 넣어 화면에서 시뮬레이션 쪽을 가리킬 수 있게 한다.
/// </summary>
public static class RegionView
{
    public static string TalentWord(float t) => t >= 0.72f ? "남다르다" : t >= 0.55f ? "좋다" : t >= 0.4f ? "보통" : "모자라다";

    public static string Json(World w, int x0, int y0, int rw, int rh)
    {
        var map = w.Map;
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
            sb.Append($"[{hx},{hy},{hh.Members[h]},{hh.Capacity[h]},{(hh.Food[h] + hh.LandValue[h]).ToString("0", inv)},1,{w.ResidenceOf(h)}]");
            int head = p.IsValid(hh.Head[h]) ? p.Uid[hh.Head[h].Index] : -1;
            int lord = w.MainLandlordOf(h);
            string lordName = lord >= 0 && p.IsValid(hh.Head[lord]) ? Names.Family(lin.FamilyOf(p.Uid[hh.Head[lord].Index])) : "";
            hinfo.Append($"{{\"id\":{h},\"player\":{(h == w.PlayerHousehold ? "true" : "false")},\"head\":{J(Names.Full(lin, head))},\"family\":{J(Names.Family(lin.FamilyOf(head)))},\"food\":{hh.Food[h].ToString("0", inv)},\"plots\":{hh.PlotCount[h]},\"cap\":{hh.Capacity[h]},\"village\":{hh.Village[h]},\"founded\":{hh.FoundedDay[h] / w.Config.DaysPerYear},");
            int patron = w.PatronOf(h);
            string patronName = patron >= 0 && p.IsValid(hh.Head[patron]) ? Names.Family(lin.FamilyOf(p.Uid[hh.Head[patron].Index])) : "";
            string policy = head >= 0 && lin.PolicyOf(lin.FamilyOf(head)) == InheritPolicy.Partible ? "균분" : "장자";
            hinfo.Append($"\"patron\":{J(patronName)},\"clients\":{w.ClientCount(h)},\"soldiers\":{w.RetainerCount(h, Job.Soldier)},\"collectors\":{w.RetainerCount(h, Job.Collector)},\"policy\":\"{policy}\",");
            hinfo.Append($"\"class\":{J(World.ClassName(w.ClassOf(h)))},\"leasedOut\":{w.LeasedOutCount(h)},\"leasedIn\":{w.LeasedInCount(h)},\"lord\":{J(lordName)},\"debt\":{w.DebtOf(h).ToString("0", inv)}}}");
        }
        // 빈집: 사는 사람 0, 마지막 값은 상태(1 멀쩡 ~ 0 무너짐)
        foreach (var vh in w.Vacant)
        {
            int hx = vh.X - x0, hy = vh.Y - y0;
            if (hx < 0 || hy < 0 || hx >= rw || hy >= rh) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"[{hx},{hy},0,{vh.Capacity},0,{vh.Condition.ToString("0.##", inv)},{vh.Residence}]");
            hinfo.Append((hinfo.Length > 0 ? "," : "") + $"{{\"head\":\"\",\"family\":\"\",\"food\":0,\"plots\":0,\"cap\":{vh.Capacity},\"village\":{vh.Village},\"founded\":0}}");
        }
        sb.Append("],\"hinfo\":[").Append(hinfo);
        // 사람: 집 안 식구들. 카드에 보일 것만 내보낸다(재능 수치는 숨기고 겉으로 보이는 짐작만).
        sb.Append("],\"people\":[");
        var wives = new Dictionary<int, List<int>>();
        for (int i = 0; i < p.HighWater; i++)
            if (p.IsAliveIndex(i) && !p.Male[i] && p.IsValid(p.Husband[i])) (wives.TryGetValue(p.Husband[i].Index, out var l) ? l : wives[p.Husband[i].Index] = new()).Add(i);
        bool firstP = true;
        int ph = w.PlayerHousehold;
        int playerHead = ph >= 0 && p.IsValid(hh.Head[ph]) ? p.Uid[hh.Head[ph].Index] : -1;
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
            sb.Append($"{{\"h\":{hi},\"i\":{i},\"uid\":{uid},\"pregnant\":{(p.PregnantUntil[i] >= 0 ? "true" : "false")},\"hungry\":{(p.HungryToday[i] ? "true" : "false")},\"loyalty\":{p.Loyalty[i].ToString("0.00", inv)},\"fanatic\":{(p.Fanatic[i] != 0 ? "true" : "false")},\"honor\":{p.Honor[i].ToString("0.00", inv)},\"name\":{J(Names.Full(lin, uid))},\"family\":{J(Names.Family(lin.FamilyOf(uid)))},\"male\":{(male ? "true" : "false")},\"age\":{age},");
            string cls = World.ClassName(w.ClassOf(p.Household[i]));
            string jobName = age < 16 ? (master != "" ? "도제" : "아이") : job == Job.SexWorker && male ? "남창" : job != Job.Farmer ? Skills.JobName(job) : cls != "" ? cls : "농부";
            sb.Append($"\"job\":{J(jobName)},\"head\":{(hh.Head[p.Household[i]].Index == i ? "true" : "false")},");
            sb.Append($"\"labels\":[{string.Join(",", Skills.Labels(p, i).Select(J))}],\"skills\":[{string.Join(",", skills)}],\"hints\":[{string.Join(",", hints.Select(J))}],");
            sb.Append($"\"health\":[{string.Join(",", health.Select(J))}],\"hair\":\"{hair}\",\"bald\":{(Genetics.Expressed(g, Marker.Baldness, male) && age >= 30 ? "true" : "false")},");
            sb.Append($"\"height\":{p.Get(i, Trait.Height).ToString("0.00", inv)},\"father\":{J(Parent(f))},\"mother\":{J(Parent(m))},\"spouse\":{J(spouse)},");
            var right = lin.RightOf(uid);
            sb.Append($"\"right\":\"{(right == Birthright.Concubine ? "서자" : right == Birthright.Bastard && lin.Father(uid) < 0 && (f >= 0 || m >= 0) ? "사생아" : "")}\",");
            sb.Append($"\"heir\":{(w.HeirOf(p.Household[i]) == i ? "true" : "false")},\"concubine\":{(!male && p.Concubine[i] && p.IsValid(p.Husband[i]) ? "true" : "false")},\"disowned\":{(lin.Disowned(uid) ? "true" : "false")},");
            if (playerHead >= 0 && p.Household[i] != ph)
                sb.Append($"\"regard\":{Social.Opinion(p, i, playerHead).ToString("0", inv)},");
            if (w.PlayerProbed(uid))
            {
                // 떠본 결과: 숨은 재능과 겉으로 안 드러나는 성격
                var tal = Enum.GetValues<Skill>().Where(k => k != Skill.Count).Select(k => $"[{J(Skills.SkillName(k))},{J(TalentWord(Skills.Talent(p, i, k)))}]");
                var nat = new List<string>();
                void N(float v, string hi, string lo) { if (v > 0.65f) nat.Add(hi); else if (v < 0.35f) nat.Add(lo); }
                N(p.Get(i, Trait.Stubbornness), "고집이 세다", "잘 굽힌다");
                N(p.Get(i, Trait.Perception), "눈치가 빠르다", "눈치가 없다");
                N(p.Piety[i], "신앙심이 깊다", "신앙에 무심하다");
                N(p.Get(i, Trait.Intelligence), "영리하다", "머리가 둔하다");
                sb.Append($"\"talents\":[{string.Join(",", tal)}],\"nature\":[{string.Join(",", nat.Select(J))}],");
            }
            sb.Append($"\"children\":{lin.Children(uid)},\"master\":{J(master)}}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }
}
