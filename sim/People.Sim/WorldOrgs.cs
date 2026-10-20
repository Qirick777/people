namespace People.Sim;

/// <summary>조직이 맡는 일(동사). 조직의 종류는 이름일 뿐이고, 하는 일은 이것으로 정한다.</summary>
public enum Verb : byte { Guard, Chase, Raid, Attack, Collect, Charity, Teach, Preach, Trade, Escort, Mercenary, Brothel, Agitate, Kidnap, Recruit }

/// <summary>맡은 일 하나: 무엇을, 어느 마을에서, 얼마의 무게로.</summary>
public struct OrgTask
{
    public Verb Verb;
    public int Village;
    public float Weight;
}

/// <summary>
/// P1.5 조직과 분봉. 모든 조직은 같은 모양(우두머리, 위 조직, 사람, 맡은 일)이다.
/// 해마다 조직마다: 할 일을 세고 → 일을 나누고 → 거느릴 수 있는 수를 넘으면 갈래를 쪼개고, 너무 작은 갈래는 합친다.
/// 지배자의 군인은 지배자 조직(군대)의 회원이고, 봉신의 군대는 주군 군대의 갈래가 된다.
/// 분봉은 집 나무의 일이다: 내 따르는 집 일부를 믿는 사람에게 맡겨 내 아래 세력으로 세운다.
/// </summary>
public sealed partial class World
{
    public static string VerbName(Verb v) => v switch
    {
        Verb.Guard => "지킴", Verb.Chase => "쫓기", Verb.Raid => "털기", Verb.Attack => "치기", Verb.Collect => "보호세", Verb.Charity => "돌봄",
        Verb.Preach => "설교", Verb.Trade => "교역", Verb.Escort => "호송", Verb.Mercenary => "용병", Verb.Brothel => "유곽", Verb.Agitate => "선동",
        Verb.Kidnap => "납치", Verb.Recruit => "포섭", _ => "가르침",
    };

    /// <summary>지금 하는 일로 붙는 이름.</summary>
    public string UnitLabel(Group g)
    {
        if (g.Tasks.Count == 0) return GroupKindName(g.Kind);
        var top = g.Tasks.OrderByDescending(t => t.Weight).First().Verb;
        return top switch
        {
            Verb.Guard => g.Paid || g.Parent >= 0 || g.Sanction >= 0 ? "경비대" : "자경단",
            Verb.Raid => "도적 떼", Verb.Chase => "치안대", Verb.Attack => "군대", Verb.Collect => g.Home >= 0 || RootOf(g).Home >= 0 ? "징수대" : "보호세 패",
            Verb.Charity => "신도회", Verb.Preach => "교단", Verb.Trade => "상단", Verb.Escort => "호송대", Verb.Mercenary => "용병단",
            Verb.Brothel => "유곽", Verb.Agitate => "선동대", Verb.Kidnap => "납치단", Verb.Recruit => "포섭조", _ => "학당",
        };
    }

    /// <summary>우두머리가 직접 부릴 수 있는 사람 수. 말솜씨, 싸움, 글.</summary>
    public int CommandSpan(int leader)
    {
        var p = People;
        return 4 + (int)(8f * p.Get(leader, Trait.Charisma) + 6f * p.GetSkill(leader, Skill.Arms) + 4f * p.GetSkill(leader, Skill.Letters));
    }

    public Group RootOf(Group g)
    {
        var x = g;
        for (int k = 0; k < 16 && x.Parent >= 0; k++)
        {
            var up = GroupById(x.Parent);
            if (up == null) break;
            x = up;
        }
        return x;
    }

    public IEnumerable<Group> ChildrenOf(Group g)
    {
        foreach (var c in _groups) if (c.Alive && c.Parent == g.Id) yield return c;
    }

    /// <summary>스스로 선 조직이 지배자의 인정을 받아 그 아래 붙은 것(지배자 군대의 갈래와 다르다).</summary>
    private static bool IsAttached(Group g) => g.Sanction >= 0 && g.Home < 0 && g.Kind != GroupKind.Army;

    private static void DefaultTask(Group g)
    {
        g.Tasks.Clear();
        var v = g.Kind switch
        {
            GroupKind.Bandits => Verb.Raid, GroupKind.Brotherhood => Verb.Charity, GroupKind.Company => Verb.Trade,
            GroupKind.Sellswords => Verb.Mercenary, GroupKind.Brothel => Verb.Brothel, _ => Verb.Guard,
        };
        g.Tasks.Add(new OrgTask { Verb = v, Village = g.Village, Weight = 1f });
    }

    /// <summary>갈래 g 의 사람들을 위 조직으로 옮기고 갈래를 닫는다.</summary>
    private void MergeInto(Group g, Group parent, List<int> members)
    {
        foreach (int m in members)
        {
            if (!People.IsAliveIndex(m)) continue;
            LeaveGroup(m, g);
            JoinGroup(m, parent);
        }
        foreach (var c in ChildrenOf(g).ToList()) c.Parent = parent.Id;
        g.Alive = false;
        Stats.UnitMerges++;
    }

    /// <summary>
    /// 조직을 닫는다. 갈래는 닫힌 조직의 위 조직으로 올라가고, 위가 없으면 홀로 선다.
    /// 홀로 서는 갈래는 맨 위 조직의 알려진 정도와 회칙, 가르침을 물려받는다. 품삯은 끊긴다.
    /// </summary>
    private void CloseGroup(Group g)
    {
        if (!g.Alive) return;
        var root = RootOf(g);
        var up = g.Parent >= 0 ? GroupById(g.Parent) : null;
        g.Alive = false;
        Stats.GroupsDissolved++;
        foreach (var c in _groups)
        {
            if (!c.Alive || c.Parent != g.Id) continue;
            if (up != null) { c.Parent = up.Id; continue; }
            c.Parent = -1;
            c.Visibility = root.Visibility;
            c.JoinRule = root.JoinRule;
            c.ShareRule = root.ShareRule;
            c.Lesson = root.Lesson;
            c.Paid = false;
            if (c.Kind == GroupKind.Army && c.Home < 0) c.Kind = GroupKind.Watch;
            if (!c.NameFixed && FindAlive(c.LeaderUid) is int cl && cl >= 0)
                c.Name = $"{Names.Family(Lineage.FamilyOf(c.LeaderUid))} {GroupKindName(c.Kind)}";
        }
    }

    /// <summary>아래 갈래까지 사람 수와 힘을 더한다. 깊은 갈래부터 더한다(번호 순서는 믿지 않는다: 봉신 군대가 나중에 생긴 주군 군대 아래로 가기도 한다).</summary>
    private void SumSubtrees()
    {
        var order = new List<(int depth, Group g)>();
        foreach (var g in _groups)
        {
            if (!g.Alive) continue;
            g.Strength = g.OwnStrength; g.Total = g.Members;
            if (g.Parent < 0) continue;
            int d = 0;
            for (var x = g; x.Parent >= 0 && d < 16; d++)
            {
                var up = GroupById(x.Parent);
                if (up == null) { x.Parent = -1; break; }
                x = up;
            }
            order.Add((d, g));
        }
        order.Sort((a, b) => b.depth.CompareTo(a.depth));
        foreach (var (_, g) in order)
        {
            if (g.Parent < 0) continue;
            var up = GroupById(g.Parent);
            if (up == null) { g.Parent = -1; continue; }
            up.Strength += g.Strength;
            up.Total += g.Total;
        }
    }

    /// <summary>사람 수와 힘을 지금 다시 센다(화면에서 조직 창을 열 때).</summary>
    public void RecountOrgs()
    {
        var p = People;
        foreach (var g in _groups) if (g.Alive) { g.Members = 0; g.OwnStrength = 0f; }
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            for (int k = 0; k < GroupSlots; k++)
            {
                var g = GroupById(p.GroupIds[i * GroupSlots + k]);
                if (g == null) continue;
                g.Members++;
                g.OwnStrength += FightPower(i);
            }
        }
        SumSubtrees();
        foreach (var g in _groups) if (g.Alive && g.Parent < 0) ComputeAuthority(g);
        RefreshNames();
    }

    // ------------------------------------------------------------------ 이름

    /// <summary>세력 단계마다 붙는 말. 지은 이름이 없으면 단계가 오를 때 이름도 바뀐다.</summary>
    public static string RealmWord(int tier) => tier switch { 2 => "영지", 3 => "대영지", 4 => "왕국", 1 => "무리", _ => "가문 군대" };

    /// <summary>
    /// 지은 이름이 없는 조직의 이름을 다시 붙인다.
    /// 지배자의 조직은 '성 + 단계 말'(노스우드 영지), 갈래는 '위 조직 이름 + 몇째 갈래'.
    /// </summary>
    private void AutoName(Group g)
    {
        if (g.NameFixed) return;
        // 지배자 집의 조직은 주군 군대 아래에 매달려도 자기 세력 이름을 쓴다(봉신의 영지)
        if (g.Home >= 0 && Households.IsAlive(g.Home) && People.IsValid(Households.Head[g.Home]))
        {
            string fam0 = Names.Family(Lineage.FamilyOf(People.Uid[Households.Head[g.Home].Index]));
            g.Name = $"{fam0} {RealmWord(TierOf(g.Home))}";
            return;
        }
        if (IsAttached(g))
        {
            // 붙은 조직은 제 이름을 지킨다
            if (g.Name.Length == 0 || g.Name.EndsWith("갈래")) g.Name = $"{Names.Family(Lineage.FamilyOf(g.LeaderUid))} {GroupKindName(g.Kind)}";
            return;
        }
        if (g.Parent >= 0 && GroupById(g.Parent) is { } up)
        {
            // 번호는 처음 받은 것을 지킨다. 위 조직이 바뀌면 새로 받는다
            if (g.BranchNo == 0 || g.BranchParent != up.Id)
            {
                int n = 0;
                foreach (var c in _kids != null ? _kids.GetValueOrDefault(up.Id) ?? new List<Group>() : ChildrenOf(up))
                    if (c != g && c.BranchParent == up.Id) n = Math.Max(n, c.BranchNo);
                g.BranchNo = n + 1;
                g.BranchParent = up.Id;
            }
            // 이름은 이름 있는 조직(맨 위, 지배자 조직, 붙은 조직) + 갈래 번호 줄: '밀러 영지 2-1갈래'
            var nums = new List<int> { g.BranchNo };
            var x = up;
            for (int d = 0; d < 16 && x.Parent >= 0 && x.Home < 0 && !IsAttached(x) && !x.NameFixed && GroupById(x.Parent) is { } xu; d++)
            {
                nums.Insert(0, x.BranchNo);
                x = xu;
            }
            g.Name = $"{x.Name} {string.Join("-", nums)}갈래";
        }
        else if (g.Home >= 0 && Households.IsAlive(g.Home) && People.IsValid(Households.Head[g.Home]))
        {
            string fam = Names.Family(Lineage.FamilyOf(People.Uid[Households.Head[g.Home].Index]));
            g.Name = $"{fam} {RealmWord(TierOf(g.Home))}";
        }
    }

    /// <summary>맨 위부터 아래로 이름을 맞춘다(위 이름이 바뀌면 갈래 이름도 따라간다).</summary>
    public void RefreshNames()
    {
        // 갈래 목록을 한 번 만들어 둔다(조직마다 전체를 훑지 않게)
        _kids = new Dictionary<int, List<Group>>();
        foreach (var g in _groups) if (g.Alive && g.Parent >= 0) (_kids.TryGetValue(g.Parent, out var l) ? l : _kids[g.Parent] = new()).Add(g);
        void Walk(Group g, int d)
        {
            AutoName(g);
            if (d < 16 && _kids.TryGetValue(g.Id, out var cs)) foreach (var c in cs) Walk(c, d + 1);
        }
        foreach (var g in _groups.ToList()) if (g.Alive && g.Parent < 0) Walk(g, 0);
        _kids = null;
    }
    private Dictionary<int, List<Group>>? _kids;

    /// <summary>이름을 짓는다. 빈 이름이면 지은 이름을 버리고 저절로 붙는 이름으로 돌아간다.</summary>
    public void RenameGroup(Group g, string name)
    {
        var clean = new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length > 20) clean = clean[..20];
        if (clean.Length == 0) g.NameFixed = false;
        else { g.Name = clean; g.NameFixed = true; }
        RefreshNames();
    }

    /// <summary>집 h 의 세력 이름(조직이 없으면 빈 글).</summary>
    public string RealmName(int h) => LordOrg(h, false)?.Name ?? "";

    // ------------------------------------------------------------------ 지배자의 조직

    private readonly Dictionary<int, int> _lordOrg = new();   // 지배자 집 → 그 집의 조직(맨 위)

    /// <summary>지배자 집 h 의 조직(없으면 만든다). 우두머리는 그 집 가장.</summary>
    public Group? LordOrg(int h, bool create = true)
    {
        if (_lordOrg.TryGetValue(h, out int id))
        {
            var g = GroupById(id);
            if (g != null && g.Home == h && Households.IsAlive(h) && Households.FoundedDay[h] == g.HomeFounded) return g;
            _lordOrg.Remove(h);
        }
        if (!create || !Households.IsAlive(h) || !People.IsValid(Households.Head[h])) return null;
        int head = Households.Head[h].Index;
        var o = new Group
        {
            Id = _groups.Count, Kind = GroupKind.Army, LeaderUid = People.Uid[head], Founded = Day, Village = Households.Village[h],
            Home = h, HomeFounded = Households.FoundedDay[h], Paid = true,
        };
        AutoName(o);
        _groups.Add(o);
        DefaultTask(o);
        _lordOrg[h] = o.Id;
        Stats.LordOrgs++;
        return o;
    }

    /// <summary>
    /// 지배자마다 군대를 맞춘다: 고용한 군인은 군대(또는 그 갈래)의 회원, 그만둔 사람은 빠진다.
    /// 우두머리는 지배자 집 가장. 봉신의 군대는 주군 군대 아래로 간다.
    /// </summary>
    private void SyncLordOrgs()
    {
        var p = People; var hh = Households;
        // 군인 → 고용한 지배자
        var soldiers = new Dictionary<int, List<int>>();
        foreach (var (person, l) in _employer)
            if (p.IsAliveIndex(person) && p.JobOf[person] == (byte)Job.Soldier)
                (soldiers.TryGetValue(l, out var list) ? list : soldiers[l] = new()).Add(person);
        // 무리 이상인 집은 군인이 없어도 조직(세력의 몸통)을 가진다
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && TierOf(h) >= 1 && !soldiers.ContainsKey(h)) LordOrg(h);
        foreach (var (lord, list) in soldiers)
        {
            var o = LordOrg(lord);
            if (o == null) continue;
            var head = hh.Head[lord];
            if (p.IsValid(head)) o.LeaderUid = p.Uid[head.Index];
            foreach (int s in list)
            {
                bool inOrg = false;
                foreach (var g in GroupsOf(s)) if (RootOf(g).Id == o.Id && g.Paid) { inOrg = true; break; }
                if (!inOrg) JoinGroup(s, o);
            }
        }
        // 품삯 갈래에서 이제 그 지배자의 군인이 아닌 사람은 뺀다(우두머리는 남는다)
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            foreach (var g in GroupsOf(i).ToList())
            {
                if (!g.Paid || IsLeader(i, g)) continue;
                var root = RootOf(g);
                if (root.Home < 0) continue;
                bool ok = p.JobOf[i] == (byte)Job.Soldier && _employer.TryGetValue(i, out int l) && l == root.Home;
                if (!ok) LeaveGroup(i, g);
            }
        }
        // 봉신의 군대는 주군 군대의 갈래
        foreach (var (h, id) in _lordOrg.ToList())
        {
            var o = GroupById(id);
            if (o == null || !hh.IsAlive(h)) { _lordOrg.Remove(h); continue; }
            // 군인이 없어도 우두머리는 지금 가장이다
            var head = hh.Head[h];
            if (p.IsValid(head)) o.LeaderUid = p.Uid[head.Index];
            int liege = PatronOf(h);
            var up = liege >= 0 ? LordOrg(liege, false) : null;
            o.Parent = up != null && RootOf(up).Id != o.Id ? up.Id : -1;
        }
    }

    // ------------------------------------------------------------------ 일 나누기와 갈래 짜기

    private Dictionary<int, Dictionary<int, int>>? _domainVillages;   // 지배자 집 → 마을 → 아래 집 수

    private void BuildDomains()
    {
        var hh = Households;
        _domainVillages = new();
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            int v = hh.Village[h];
            for (int l = h, d = 0; l >= 0 && d < 32; l = PatronOf(l), d++)
            {
                if (!_lordOrg.ContainsKey(l)) continue;
                var m = _domainVillages.TryGetValue(l, out var x) ? x : _domainVillages[l] = new();
                m[v] = m.GetValueOrDefault(v) + 1;
            }
        }
    }

    /// <summary>맨 위 조직의 할 일: (일, 마을, 크기). 우두머리 성격이 무게를 바꾼다.</summary>
    private List<OrgTask> Needs(Group root)
    {
        var p = People;
        var list = new List<OrgTask>();
        int leader = FindAlive(root.LeaderUid);
        float caution = leader >= 0 ? p.Get(leader, Trait.Caution) : 0.5f, aggr = leader >= 0 ? p.Get(leader, Trait.Aggression) : 0.5f;
        float piety = leader >= 0 ? p.Piety[leader] : 0.5f;
        if (root.Home >= 0 && _domainVillages != null && _domainVillages.TryGetValue(root.Home, out var dom))
        {
            foreach (var (v, n) in dom)
            {
                list.Add(new OrgTask { Verb = Verb.Guard, Village = v, Weight = n * (0.1f + DangerAt(v)) * (0.5f + caution) });
                // 범죄가 잦은 마을에는 쫓기를 둔다
                float heat = CrimeHeat(v);
                if (heat > 0.5f) list.Add(new OrgTask { Verb = Verb.Chase, Village = v, Weight = heat * 1.5f * (0.5f + aggr) });
            }
        }
        else
        {
            KindNeeds(root, leader, list);
        }
        list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        // 지배자 조직은 다스리는 마을마다 일이 있다. 다른 조직은 큰 일 넷까지
        int cap = root.Home >= 0 ? 24 : 4;
        if (list.Count > cap) list.RemoveRange(cap, list.Count - cap);
        float tot = list.Sum(t => t.Weight);
        for (int k = 0; k < list.Count; k++) { var t = list[k]; t.Weight = tot > 0 ? t.Weight / tot : 1f / list.Count; list[k] = t; }
        return list;
    }

    /// <summary>
    /// 갈래에 일을 나눈다: 큰 갈래부터 가장 큰 일 하나씩. 남은 일은 이 조직에 바로 딸린 사람이 맡는다.
    /// 그 뒤 바로 딸린 사람이 거느릴 수를 넘으면 갈래를 하나 떼어 내고, 너무 작은 갈래는 합친다.
    /// </summary>
    private void PlanUnit(Group g, List<OrgTask> tasks, int depth)
    {
        var children = ChildrenOf(g).OrderByDescending(c => c.Strength).ToList();
        var left = new List<OrgTask>(tasks);
        if (!g.Manual)
        {
            // 인정받아 붙은 조직은 제 일(자경단은 제 마을 지킴)을 한다. 지배자 군대의 일을 넘겨받지 않는다
            foreach (var c in children.Where(c => IsAttached(c) && !c.Manual).ToList()) DefaultTask(c);
            children = children.Where(c => !IsAttached(c)).ToList();
            foreach (var c in children)
            {
                if (c.Manual) continue;
                if (left.Count == 0) { c.Tasks.Clear(); c.Tasks.AddRange(tasks.Take(1).Select(t => { t.Weight = 1f; return t; })); continue; }
                var t = left[0];
                left.RemoveAt(0);
                c.Tasks.Clear();
                t.Weight = 1f;
                c.Tasks.Add(t);
            }
            if (depth == 0 && children.Count > 0)
            {
                // 하위 조직이 있는 맨 위 조직은 일을 갈래에 넘긴다. 남은 일은 가장 센 갈래가 더 맡는다
                var top = children.FirstOrDefault(c => !c.Manual);
                if (top != null && left.Count > 0)
                {
                    // 제 일 1, 남은 일 0.5씩 두고 합이 1이 되게 나눈다(힘이 무에서 생기지 않게)
                    foreach (var t in left) top.Tasks.Add(new OrgTask { Verb = t.Verb, Village = t.Village, Weight = 0.5f });
                    float tw = top.Tasks.Sum(t => t.Weight);
                    for (int k = 0; k < top.Tasks.Count; k++) { var t = top.Tasks[k]; t.Weight /= tw; top.Tasks[k] = t; }
                }
                g.Tasks.Clear();
            }
            else
            {
                if (left.Count == 0 && tasks.Count > 0) left.Add(tasks[0]);
                float tot = left.Sum(t => t.Weight);
                g.Tasks.Clear();
                foreach (var t in left) g.Tasks.Add(new OrgTask { Verb = t.Verb, Village = t.Village, Weight = tot > 0 ? t.Weight / tot : 1f });
            }
        }
        int leader = FindAlive(g.LeaderUid);
        if (leader >= 0 && g.Members > CommandSpan(leader) + 4 && depth < 4 && !g.Manual)
        {
            // 남는 사람은 먼저 자리가 있는 갈래로 보내고, 그래도 넘치면 새 갈래를 뗀다
            int over = g.Members - CommandSpan(leader);
            foreach (var c in children)
            {
                if (over <= 0) break;
                int cl = FindAlive(c.LeaderUid);
                int room = cl >= 0 ? CommandSpan(cl) - c.Members : 0;
                if (room <= 0) continue;
                foreach (int m in Members(g))
                {
                    if (room <= 0 || over <= 0) break;
                    if (IsLeader(m, g) || !Adult(m)) continue;
                    LeaveGroup(m, g); JoinGroup(m, c);
                    room--; over--;
                }
            }
            if (over > 4) SplitUnit(g);
        }
        foreach (var c in ChildrenOf(g).ToList())
        {
            // 갓 뗀 갈래는 두 해 동안 합치지 않는다(사람이 잠깐 빠져도 갈래를 지킨다)
            if (c.Members < 2 && !ChildrenOf(c).Any() && !c.Manual && Day - c.Founded >= 2 * Config.DaysPerYear)
            {
                var ms = Members(c);
                MergeInto(c, g, ms);
                continue;
            }
            PlanUnit(c, c.Tasks.ToList(), depth + 1);
        }
    }

    public List<int> Members(Group g)
    {
        var list = new List<int>();
        for (int i = 0; i < People.HighWater; i++)
        {
            if (!People.IsAliveIndex(i)) continue;
            for (int k = 0; k < GroupSlots; k++) if (People.GroupIds[i * GroupSlots + k] == g.Id) { list.Add(i); break; }
        }
        return list;
    }

    /// <summary>바로 딸린 사람의 절반으로 새 갈래를 만든다. 갈래 우두머리는 우두머리를 가장 따르고 힘 있는 사람.</summary>
    public Group? SplitUnit(Group g)
    {
        var p = People;
        var ms = Members(g).Where(m => !IsLeader(m, g) && Adult(m)).ToList();
        if (ms.Count < 8) return null;   // 새 갈래가 넷은 되어야 쪼갠다
        int lu = g.LeaderUid;
        int sub = ms.OrderByDescending(m => Social.Opinion(p, m, lu) / 50f + FightPower(m) + p.Get(m, Trait.Charisma)).First();
        var c = new Group
        {
            Id = _groups.Count, Kind = g.Kind, LeaderUid = p.Uid[sub], Founded = Day, Village = g.Village, Parent = g.Id, Paid = g.Paid,
        };
        _groups.Add(c);
        AutoName(c);
        foreach (var t in g.Tasks) c.Tasks.Add(t);
        int move = ms.Count / 2;
        LeaveGroup(sub, g); JoinGroup(sub, c);
        foreach (int m in ms)
        {
            if (move <= 1) break;
            if (m == sub) continue;
            LeaveGroup(m, g); JoinGroup(m, c);
            move--;
        }
        Stats.UnitSplits++;
        return c;
    }

    // ------------------------------------------------------------------ 분봉과 회수

    /// <summary>
    /// 분봉: lord 집이 직접 거느린 집 가운데 village 의 집들(village &lt; 0 이면 vassal 집에서 가까운 count 집)을
    /// vassal 집에 맡기고, vassal 은 lord 를 섬긴다. 옮긴 집 수.
    /// </summary>
    public int Enfeoff(int lord, int vassal, int village, int count)
    {
        var hh = Households;
        if (!hh.IsAlive(lord) || !hh.IsAlive(vassal) || lord == vassal || WouldLoop(vassal, lord)) return 0;
        var mine = new List<int>();
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && h != vassal && PatronOf(h) == lord && (village < 0 || hh.Village[h] == village)) mine.Add(h);
        if (village < 0)
        {
            mine.Sort((a, b) => Dist2(a, vassal).CompareTo(Dist2(b, vassal)));
            if (mine.Count > count) mine.RemoveRange(count, mine.Count - count);
        }
        if (mine.Count == 0) return 0;
        _patron[vassal] = lord;
        _patronFounded[vassal] = hh.FoundedDay[lord];
        foreach (int h in mine)
        {
            if (WouldLoop(h, vassal)) continue;
            _patron[h] = vassal;
            _patronFounded[h] = hh.FoundedDay[vassal];
            Resnap(h);   // 주군이 스스로 맡긴 집: 오름으로 보지 않는다
        }
        Resnap(vassal);
        EnsureRealmArrays();
        _legit[vassal] = (byte)Legitimacy.Granted;
        HeadsRelate(vassal, lord, 20f, 0f, 20f, MemoryType.Protected, 20);
        RecountClients();
        RecountTree();
        Stats.Enfeoffs++;
        var vh = hh.Head[vassal];
        if (People.IsValid(vh)) Chron(vh.Index, $"{SubjName(vh.Index)} 집 {mine.Count}곳을 맡아 봉신이 되었다");
        var lh = hh.Head[lord];
        if (People.IsValid(lh) && People.IsValid(vh)) Chron(lh.Index, $"집 {mine.Count}곳을 {Names.Full(Lineage, People.Uid[vh.Index])}에게 맡겼다");
        return mine.Count;
    }

    /// <summary>회수 요구를 봉신이 따를 확률: 충성, 힘의 차이, 야망.</summary>
    public float RevokeComply(int lord, int vassal)
    {
        var hh = Households; var p = People;
        var vh = hh.Head[vassal];
        float loyal = p.IsValid(vh) && p.LoyalUid[vh.Index] >= 0 ? p.Loyalty[vh.Index] : 0.5f;
        float lp = LordPower(lord), vp = LordPower(vassal);
        return Math.Clamp(0.3f + loyal + 0.5f * lp / (lp + vp + 0.01f) - 0.3f * HeadTrait(vassal, Trait.Ambition), 0.05f, 0.98f);
    }

    /// <summary>봉신이 따른다: 봉신이 거느린 집이 주군에게 돌아온다. 봉신은 서운하고 충성이 깎인다.</summary>
    public void TakeBack(int lord, int vassal)
    {
        var hh = Households; var p = People;
        int n = 0;
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && PatronOf(h) == vassal) { _patron[h] = lord; _patronFounded[h] = hh.FoundedDay[lord]; Resnap(h); n++; }
        HeadsRelate(vassal, lord, -40f, 10f, 0f, MemoryType.Betrayed, -40);
        var vh = hh.Head[vassal];
        if (p.IsValid(vh)) p.Loyalty[vh.Index] = MathF.Max(0f, p.Loyalty[vh.Index] - 0.3f);
        Stats.Revokes++;
        RecountClients(); RecountTree();
        var lh = hh.Head[lord];
        if (p.IsValid(lh) && p.IsValid(vh)) Chron(lh.Index, $"{Names.Full(Lineage, p.Uid[vh.Index])}에게서 집 {n}곳을 거두었다");
        if (p.IsValid(vh)) Chron(vh.Index, $"{SubjName(vh.Index)} 맡았던 집 {n}곳을 주군에게 돌려주었다");
    }

    /// <summary>봉신이 거부한다: 거느린 집을 쥔 채 떨어져 나가 홀로 선다.</summary>
    public void BreakAway(int lord, int vassal)
    {
        var hh = Households; var p = People;
        _patron[vassal] = -1;
        EnsureRealmArrays();
        _legit[vassal] = (byte)Legitimacy.Self;
        HeadsRelate(lord, vassal, -40f, 0f, 0f, MemoryType.Betrayed, -50);
        Stats.RevokeRefused++;
        RecountClients(); RecountTree();
        var lh = hh.Head[lord]; var vh = hh.Head[vassal];
        if (p.IsValid(lh) && p.IsValid(vh)) Chron(lh.Index, $"{Subj(Names.Full(Lineage, p.Uid[vh.Index]))} 집을 내놓지 않고 떨어져 나갔다");
        if (p.IsValid(vh)) Chron(vh.Index, $"{SubjName(vh.Index)} 주군의 회수를 거부하고 홀로 섰다");
    }

    /// <summary>회수(NPC): 따르면 되찾고, 거부하면 떨어져 나간다.</summary>
    public bool Revoke(int lord, int vassal)
    {
        var hh = Households;
        if (!hh.IsAlive(lord) || !hh.IsAlive(vassal) || PatronOf(vassal) != lord) return false;
        if (_rng.Chance(RevokeComply(lord, vassal))) { TakeBack(lord, vassal); return true; }
        BreakAway(lord, vassal);
        return false;
    }

    /// <summary>h 집이 lord 집의 봉신(lord 를 섬기고 아래 집을 거느림)인가.</summary>
    public bool IsVassalOf(int h, int lord) => Households.IsAlive(h) && PatronOf(h) == lord && ClientCount(h) > 0;

    /// <summary>lord 가 직접 거느린 집 가운데 village 에 사는 집 수(vassal 집은 빼고).</summary>
    public int DirectInVillage(int lord, int village, int except)
    {
        var hh = Households;
        int n = 0;
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && h != except && PatronOf(h) == lord && hh.Village[h] == village) n++;
        return n;
    }

    private void AddOrgDefs(InteractionDef[] d)
    {
        // 값: 0 이면 대상이 사는 마을의 내 집 전부, 아니면 대상 집에서 가까운 그 수만큼
        d[(int)InteractionId.Enfeoff] = new InteractionDef
        {
            Id = InteractionId.Enfeoff, Name = "봉토 맡기기", Category = InteractionCategory.Rule,
            Amounts = new[] { 0f, 5f, 15f, 40f }, AmountNames = new[] { "그 마을 전부", "가까운 5집", "가까운 15집", "가까운 40집" },
            Can = (w, c) =>
            {
                var p = w.People;
                int lh = p.Household[c.Actor], th = p.Household[c.Target];
                return w.IsHead(c.Actor) && w.IsHead(c.Target) && lh != th && w.ClientCount(lh) >= 3 && w.PatronOf(th) == lh;
            },
            // 봉토는 대개 반긴다. 주군을 싫어하거나 겁 많은 사람은 짐으로 여긴다
            Accept = (w, c) => Math.Clamp(0.75f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 200f
                                          + 0.15f * w.People.Get(c.Target, Trait.Ambition) - 0.1f * w.People.Get(c.Target, Trait.Caution), 0.1f, 0.97f),
            Success = (w, c, r) =>
            {
                var p = w.People;
                int lh = p.Household[c.Actor], th = p.Household[c.Target];
                int n = c.Amount <= 0f ? w.Enfeoff(lh, th, w.Households.Village[th], 0) : w.Enfeoff(lh, th, -1, (int)c.Amount);
                r.Amount = n;
            },
            Fail = (w, c) => Social.AddFrom(w.People, c.Target, c.Actor, -2f),
        };
        d[(int)InteractionId.Revoke] = new InteractionDef
        {
            // 받아들일 확률 = 봉신이 따를 확률. 따르지 않으면 집을 쥔 채 떨어져 나간다
            Id = InteractionId.Revoke, Name = "봉토 거두기", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) =>
            {
                var p = w.People;
                return w.IsHead(c.Actor) && w.IsHead(c.Target) && w.IsVassalOf(p.Household[c.Target], p.Household[c.Actor]);
            },
            Accept = (w, c) => w.RevokeComply(w.People.Household[c.Actor], w.People.Household[c.Target]),
            Success = (w, c, _) => w.TakeBack(w.People.Household[c.Actor], w.People.Household[c.Target]),
            Fail = (w, c) => w.BreakAway(w.People.Household[c.Actor], w.People.Household[c.Target]),
        };
    }

    /// <summary>
    /// NPC 지배자의 분봉과 회수.
    /// 직접 거느린 집이 챙길 수 있는 수(혼자 + 징수관)를 크게 넘으면, 가장 믿는 혈족이나 신하 집에 먼 마을을 맡긴다.
    /// 균분 상속 가문은 더 쉽게 나눈다. 충성이 바닥인 봉신은 거친 주군이 회수하려 한다.
    /// </summary>
    private void DelegationYear()
    {
        var hh = Households; var p = People;
        // 지배자마다 직접 거느린 집 목록
        var clients = new Dictionary<int, List<int>>();
        for (int h = 0; h < hh.HighWater; h++)
        {
            int l = hh.IsAlive(h) ? PatronOf(h) : -1;
            if (l >= 0) (clients.TryGetValue(l, out var list) ? list : clients[l] = new()).Add(h);
        }
        foreach (var (lord, mine) in clients)
        {
            if (TierOf(lord) < 2 || !hh.IsAlive(lord) || !p.IsValid(hh.Head[lord]) || IsPlayer(hh.Head[lord].Index)) continue;
            int head = hh.Head[lord].Index;
            int capacity = DirectSpan(lord) + _collectorsOf.GetValueOrDefault(lord) * Config.CollectorSpanBase;
            bool partible = Lineage.PolicyOf(Lineage.FamilyOf(p.Uid[head])) == InheritPolicy.Partible;
            // 나눌 까닭: 혼자 챙길 수를 넘음, 또는 먼 마을(내 마을이 아닌 곳)에 따르는 집이 여럿
            int far = mine.Count(h => hh.Village[h] != hh.Village[lord]);
            float want = (mine.Count > capacity * 1.3f + 4 ? 0.3f : 0f) + (far >= 5 ? (partible ? 0.2f : 0.08f) : 0f);
            if (!_rng.Chance(want)) continue;
            // 맡길 사람: 내가 거느린 집 가운데 혈족(3촌 안)이나 내 군인·징수관의 집, 충성과 능력이 높은 쪽(먼 마을 우선)
            int best = -1;
            float bs = 0.45f;
            foreach (int k in mine)
            {
                if (!p.IsValid(hh.Head[k]) || ClientCount(k) > 0 || WouldLoop(k, lord)) continue;
                int kh = hh.Head[k].Index;
                int chon = Lineage.Chon(p.Uid[head], p.Uid[kh]);
                bool kin = chon > 0 && chon <= 3;
                bool retainer = false;
                foreach (int m in MembersOf(k)) if (_employer.TryGetValue(m, out int l) && l == lord) { retainer = true; break; }
                if (!kin && !retainer) continue;
                float loyal = p.LoyalUid[kh] == p.Uid[head] ? p.Loyalty[kh] : 0.5f;
                float sc = loyal + (kin ? 0.2f : 0f) + 0.3f * p.Get(kh, Trait.Charisma) + 0.2f * p.GetSkill(kh, Skill.Arms) + (hh.Village[k] != hh.Village[lord] ? 0.2f : 0f);
                if (sc > bs) { bs = sc; best = k; }
            }
            if (best < 0) continue;
            int give = Math.Max(5, (mine.Count - capacity) / 2);
            Enfeoff(lord, best, hh.Village[best] != hh.Village[lord] ? hh.Village[best] : -1, give);
        }
        // 회수: 충성이 바닥인 봉신
        for (int v = 0; v < hh.HighWater; v++)
        {
            if (!hh.IsAlive(v) || ClientCount(v) == 0) continue;
            int lord = PatronOf(v);
            if (lord < 0 || !p.IsValid(hh.Head[lord]) || IsPlayer(hh.Head[lord].Index)) continue;
            var vh = hh.Head[v];
            if (!p.IsValid(vh) || p.LoyalUid[vh.Index] < 0 || p.Loyalty[vh.Index] > 0.25f) continue;
            if (_rng.Chance(0.2f * HeadTrait(lord, Trait.Aggression))) Revoke(lord, v);
        }
    }

    // ------------------------------------------------------------------ 해마다

    private void OrgYear()
    {
        SyncLordOrgs();
        SumSubtrees();
        BuildDomains();
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive || g.Parent >= 0) continue;
            var needs = Needs(g);
            if (needs.Count == 0) { if (g.Tasks.Count == 0) DefaultTask(g); needs = g.Tasks.ToList(); }
            PlanUnit(g, needs, 0);
        }
        _domainVillages = null;
        DelegationYear();
        BuildMemberIndex();
        foreach (var g in _groups) if (g.Alive && g.Parent < 0) PickInner(g);
        AuthorityYear();
        VisibilityYear();
        WorkYear();
        TeachYear();
        AuthorityStats();
        _memberIdx = null;
        RefreshNames();
        Stats.OrgUnits = _groups.Count(g => g.Alive && g.Parent >= 0);
        Stats.LordArmies = _lordOrg.Count;
    }

    // ------------------------------------------------------------------ 플레이어

    /// <summary>플레이어가 갈래에 일을 정한다(그 갈래는 AI 가 바꾸지 않는다).</summary>
    public void SetUnitTask(Group g, Verb verb, int village)
    {
        g.Tasks.Clear();
        g.Tasks.Add(new OrgTask { Verb = verb, Village = village, Weight = 1f });
        g.Manual = true;
    }

    public void ClearManual(Group g) => g.Manual = false;
}
