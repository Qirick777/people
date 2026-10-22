namespace People.Sim;

/// <summary>세력 안 파벌 하나(해마다 다시 센다). 직책을 가진 사람들 가운데 서로 좋게 보고 함께 불만을 가진 이들.</summary>
public sealed class Faction
{
    public int Root;                                  // 맨 위 조직 id
    public readonly List<int> Members = new();        // uid
    public int Leader = -1;                           // uid
    public float Power, Grievance;                    // 이끄는 갈래의 힘 × 장악도 합, 불만 평균
}

/// <summary>
/// 세력 틀 7단계(plan_faction 10장): 예산과 파벌.
/// - 곳간에서 갈래마다 몫을 나눈다. 필요 = 지휘자 녹봉 + 상근 군량(+ 맨 위는 곳간지기·연락책 녹봉). 비중을 정하지 않으면 필요만큼 나눈다.
/// - 받은 몫이 필요의 0.8에 못 미치면 지휘자 불만이 오르고, 일의 몫도 받은 만큼 준다(0.5 아래로는 아니다).
/// - 불만을 가진 직책끼리 서로 좋게 보면 파벌이 된다. 파벌은 예산을 요구하고, 경쟁 파벌을 헐뜯고, 거사하거나 떨어져 나간다.
/// - 우두머리는 비중을 다시 나누고, 지휘자를 바꾸고, 한 사람에게 자리를 주어 파벌을 가르고, 쫓아낸다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<(int root, int uid), float> _grievance = new();
    private readonly List<Faction> _factions = new();

    public IReadOnlyList<Faction> Factions => _factions;

    public float GrievanceOf(Group root, int uid) => _grievance.GetValueOrDefault((RootOf(root).Id, uid));
    private void AddGrievance(int root, int uid, float d)
    {
        if (uid < 0) return;
        _grievance[(root, uid)] = Math.Clamp(_grievance.GetValueOrDefault((root, uid)) + d, 0f, 1f);
    }

    public IEnumerable<Faction> FactionsOf(Group root) { int id = RootOf(root).Id; return _factions.Where(f => f.Root == id); }

    private List<Group> UnitsOf(Group root) => Subtree(root).Where(u => u == root || RootOf(u).Id == root.Id).ToList();

    /// <summary>갈래 하나가 한 해에 쓸 은화: 직책별 임금(우두머리 집에서 주면 빼고)과 상근 군량.</summary>
    public float UnitNeed(Group u)
    {
        var p = People; var hh = Households;
        float need = 0f;
        var root = RootOf(u);
        if (!root.WageFromLeader)
            foreach (int m in MembersFast(u)) if (p.Uid[m] != root.LeaderUid) need += WageFor(m, u);
        need += SchoolCost(u);   // 학교의 질 비용(R4)
        if (u.FullTime && u.Home < 0)
        {
            float ration = 24f * (FacilityOf(RootOf(u), FacilityKind.Barracks, -1, u) != null ? 0.8f : 1f);
            foreach (int m in MembersFast(u)) { int h = p.Household[m]; if (hh.IsAlive(h)) need += ration * FoodPrice(hh.Village[h]); }
        }
        return need;
    }

    /// <summary>예산 한 해 치(시험용).</summary>
    public void RecountBudget() => BudgetYear();

    /// <summary>파벌만 다시 묶는다(움직이지 않는다). 화면과 시험에서 부른다.</summary>
    public void RecountFactions() => BuildFactions();

    /// <summary>플레이어가 갈래의 예산 비중을 정한다(필요에 곱하는 수, 0~4).</summary>
    public void SetBudgetWeight(Group u, float w) => u.BudgetWeight = Math.Clamp(w, 0f, 4f);

    /// <summary>
    /// 해마다(지휘 셈 전): 지난해 남은 몫을 곳간에 돌리고, 갈래마다 필요를 세어 곳간에서 나눈다. 녹봉을 치르고 불만을 센다.
    /// </summary>
    private void BudgetYear()
    {
        var p = People; var hh = Households;
        int shortUnits = 0; float stip = 0f;
        foreach (var root in _groups.ToList())
        {
            if (!root.Alive || !IsTop(root)) continue;
            var units = UnitsOf(root);
            foreach (var u in units) { root.Treasury += u.Purse; u.Purse = 0f; }
            int rl = FindAlive(root.LeaderUid);
            if (!root.WagesManual && (rl < 0 || !IsPlayer(rl))) NpcWages(root, units);
            float total = 0f;
            foreach (var u in units) { u.Need = UnitNeed(u); total += u.Need; }
            // 곳간이 모자라면 우두머리 집이 번 것의 일부로 댄다(번 것을 우두머리가 다 가지는 조직이 많다)
            if (root.Treasury < total)
            {
                int home = root.Home >= 0 ? root.Home : FindAlive(root.LeaderUid) is int li && li >= 0 ? p.Household[li] : -1;
                float share = root.Home >= 0 ? 0.3f : root.ShareRule switch { ShareRule.Leader => 0.5f, ShareRule.Even => 0.2f, _ => 0f };
                float earned = MathF.Max(0f, root.LastIncome) + (root.Home >= 0 && home >= 0 && home < _incTribute.Length ? _incTribute[home] : 0f);
                if (home >= 0 && hh.IsAlive(home) && share > 0f)
                {
                    float want = MathF.Min(total - root.Treasury, share * earned);
                    // 지배자는 가진 은화에서만 낸다(쌓아 둔 곡식을 풀면 장원 전체 먹을 것이 바뀐다). 홀로 선 조직 우두머리는 남는 곡식을 팔아서라도 낸다
                    if (root.Home >= 0) { float pay = MathF.Min(MathF.Max(0f, hh.Coin[home]), want); hh.Coin[home] -= pay; root.Treasury += pay; }
                    else root.Treasury += PayCoin(home, -2, want, false);
                }
            }
            float pot = MathF.Min(root.Treasury, total);
            // 비중대로 나누되 필요를 넘지 않게, 남는 것은 다른 갈래에 다시 나눈다
            foreach (var u in units) u.Alloc = 0f;
            float left = pot;
            for (int it = 0; it < 4 && left > 0.01f; it++)
            {
                float wsum = 0f;
                foreach (var u in units) if (u.Alloc < u.Need) wsum += u.Need * u.BudgetWeight;
                if (wsum <= 0f) break;
                float given = 0f;
                foreach (var u in units)
                {
                    if (u.Alloc >= u.Need) continue;
                    float add = MathF.Min(u.Need - u.Alloc, left * u.Need * u.BudgetWeight / wsum);
                    u.Alloc += add; given += add;
                }
                left -= given;
                if (given <= 0.01f) break;
            }
            root.Treasury -= pot - left;
            foreach (var u in units)
            {
                u.Fill = u.Need > 0f ? u.Alloc / u.Need : 1f;
                u.Purse = u.Alloc;
                if (u.Need > 0f && u.Fill < 0.8f) shortUnits++;
                // 직책자 불만: 받을 임금이 있는데 몫이 0.8에 못 미치면 오르고, 아니면 식는다
                if (!IsTop(u) && FindAlive(u.LeaderUid) is int cmd && cmd >= 0)
                    AddGrievance(root.Id, u.LeaderUid, WageFor(cmd, u) > 0f && u.Fill < 0.8f ? 0.1f : -0.05f);
                if (IsTop(u))
                    foreach (int uid in new[] { u.TreasurerUid, u.LiaisonUid })
                        if (FindAlive(uid) is int r && r >= 0) AddGrievance(root.Id, uid, WageFor(r, u) > 0f && u.Fill < 0.8f ? 0.1f : -0.05f);
            }
            PayWages(root, units, ref stip);
            RivalUnits(root, units);
        }
        Stats.BudgetShort = shortUnits; Stats.Stipends = stip;
        DecayBonuses();
        Stats.MeanOrgLoyalty = _orgLoyal.Count > 0 ? _orgLoyal.Values.Average() : 0.3f;
        // 사라진 세력·사람의 불만은 지운다
        foreach (var k in _grievance.Keys.ToList())
            if (GroupById(k.root) == null || FindAlive(k.uid) < 0) _grievance.Remove(k);
    }

    /// <summary>같은 마을에서 같은 일을 하는 갈래 둘은 실적을 견준다. 진 쪽 지휘자의 불만이 오른다.</summary>
    private void RivalUnits(Group root, List<Group> units)
    {
        var seen = new Dictionary<(Verb, int), Group>();
        foreach (var u in units)
        {
            if (u == root || FindAlive(u.LeaderUid) < 0 || u.Tasks.Count == 0) continue;
            var t = u.Tasks.OrderByDescending(x => x.Weight).First();
            var key = (t.Verb, t.Village);
            if (!seen.TryGetValue(key, out var o)) { seen[key] = u; continue; }
            float a = (u.OwnStrength + u.Members) * u.CmdFactor, b = (o.OwnStrength + o.Members) * o.CmdFactor;
            AddGrievance(root.Id, a < b ? u.LeaderUid : o.LeaderUid, 0.05f);
            if (a > b) seen[key] = u;
        }
    }

    /// <summary>예산이 모자란 갈래가 일을 덜 하는 몫(0.5~1).</summary>
    private static float BudgetFactor(Group u) => u.Need > 0f ? Math.Clamp(u.Fill, 0.5f, 1f) : 1f;

    // ------------------------------------------------------------------ 파벌

    /// <summary>직책을 가진 사람(갈래 지휘자, 곳간지기, 연락책). uid → 그가 지휘하는 갈래(없으면 null).</summary>
    private List<(int uid, Group? unit)> RoleHolders(Group root)
    {
        var list = new List<(int, Group?)>();
        foreach (var u in UnitsOf(root)) if (u != root && FindAlive(u.LeaderUid) >= 0 && u.LeaderUid != root.LeaderUid) list.Add((u.LeaderUid, u));
        foreach (int uid in new[] { root.TreasurerUid, root.LiaisonUid })
            if (FindAlive(uid) >= 0 && uid != root.LeaderUid && !list.Any(x => x.Item1 == uid)) list.Add((uid, null));
        return list;
    }

    /// <summary>해마다 파벌을 다시 묶는다: 불만 0.3 넘는 직책끼리 서로 보는 눈이 30을 넘으면 한 파벌. 혼자라도 불만이 0.5를 넘으면 파벌로 센다.</summary>
    private void BuildFactions()
    {
        var p = People;
        _factions.Clear();
        foreach (var root in _groups)
        {
            if (!root.Alive || !IsTop(root)) continue;
            var roles = RoleHolders(root).Where(x => GrievanceOf(root, x.uid) > 0.3f && !IsZealot(x.uid, root)).ToList();   // 광신도는 파벌에 끼지 않는다
            if (roles.Count == 0) continue;
            int n = roles.Count;
            var par = Enumerable.Range(0, n).ToArray();
            int Find(int a) { while (par[a] != a) a = par[a] = par[par[a]]; return a; }
            for (int a = 0; a < n; a++)
            {
                int ia = FindAlive(roles[a].uid);
                for (int b = a + 1; b < n; b++)
                {
                    int ib = FindAlive(roles[b].uid);
                    if (Social.Opinion(p, ia, roles[b].uid) > 30f && Social.Opinion(p, ib, roles[a].uid) > 30f) par[Find(a)] = Find(b);
                }
            }
            foreach (var grp in Enumerable.Range(0, n).GroupBy(Find))
            {
                var ms = grp.Select(k => roles[k]).ToList();
                float gr = ms.Average(x => GrievanceOf(root, x.uid));
                if (ms.Count == 1 && gr <= 0.5f) continue;
                var f = new Faction { Root = root.Id, Grievance = gr };
                float best = -1f;
                foreach (var (uid, unit) in ms)
                {
                    f.Members.Add(uid);
                    float grip = unit?.Grip ?? 0f;
                    if (unit != null) f.Power += unit.OwnStrength * grip;
                    float s = grip + p.Get(FindAlive(uid), Trait.Charisma);
                    if (s > best) { best = s; f.Leader = uid; }
                }
                _factions.Add(f);
            }
        }
        Stats.Factions = _factions.Count;
        Stats.MeanGrievance = _grievance.Count > 0 ? _grievance.Values.Average() : 0f;
    }

    /// <summary>파벌이 이끄는 갈래들.</summary>
    private List<Group> FactionUnits(Faction f)
    {
        var root = GroupById(f.Root);
        if (root == null) return new();
        return UnitsOf(root).Where(u => u != root && f.Members.Contains(u.LeaderUid)).ToList();
    }

    /// <summary>해마다(지휘 셈 뒤): 파벌이 움직이고, NPC 우두머리가 대처한다.</summary>
    private void FactionYear()
    {
        BuildFactions();
        var p = People;
        foreach (var f in _factions.ToList())
        {
            var root = GroupById(f.Root);
            if (root == null || !root.Alive) continue;
            int fl = FindAlive(f.Leader), leader = FindAlive(root.LeaderUid);
            if (fl < 0 || leader < 0 || IsPlayer(fl)) continue;
            float rootPow = MathF.Max(1f, root.Strength);
            float maxG = f.Members.Max(u => GrievanceOf(root, u));
            // 1) 거사: 몹시 불만이고 세력 힘의 0.4를 넘는다
            if (maxG > 0.8f && f.Power > 0.4f * rootPow) { InnerCoup(f, root, fl, leader); continue; }
            // 2) 거사가 어려우면 갈래를 이끌고 떨어져 나간다
            if (maxG > 0.8f && _rng.Chance(0.3f)) { if (FactionSplit(f, root, fl)) continue; }
            // 3) 경쟁 파벌을 헐뜯는다
            var rival = _factions.FirstOrDefault(o => o != f && o.Root == f.Root);
            if (rival != null && f.Grievance > 0.5f && FindAlive(rival.Leader) is int rl && rl >= 0 && _rng.Chance(0.3f))
            {
                Social.Add(p, leader, rival.Leader, -5f);
                Stats.FactionSlanders++;
                if (!IsPlayer(leader) && _rng.Chance(0.3f)) foreach (var u in FactionUnits(rival)) u.BudgetWeight = MathF.Max(0.25f, u.BudgetWeight * 0.9f);
            }
            // 4) 예산과 자리를 요구한다
            if (f.Grievance > 0.5f && _rng.Chance(0.5f))
            {
                Stats.FactionDemands++;
                int from = p.Household[fl], to = p.Household[leader];
                if (IsPlayer(leader))
                {
                    if (HeadIdx(from) is int fh && fh >= 0 && !_demands.Any(d => d.Kind == InteractionId.FactionDemand && d.From == from)) AskPlayer(InteractionId.FactionDemand, from, to);
                }
                else if (_rng.Chance(DemandOdds(f, root, leader))) MeetDemand(f, root);
                else RefuseDemand(f, root, leader);
            }
        }
        // NPC 우두머리의 수: 센 파벌은 한 사람에게 자리를 주어 가르고, 거친 우두머리는 몹시 불만인 지휘자를 바꾼다
        foreach (var f in _factions)
        {
            var root = GroupById(f.Root);
            int leader = root != null ? FindAlive(root.LeaderUid) : -1;
            if (root == null || leader < 0 || IsPlayer(leader)) continue;
            if (f.Members.Count >= 2 && f.Grievance > 0.5f && _rng.Chance(0.3f * p.Get(leader, Trait.Charisma) + 0.1f))
            {
                int pick = f.Members.Where(u => u != f.Leader).OrderBy(u => GrievanceOf(root, u)).First();
                Placate(root, pick);
                continue;
            }
            if (p.Get(leader, Trait.Aggression) > 0.6f && _rng.Chance(0.3f))
                foreach (var u in FactionUnits(f))
                {
                    if (GrievanceOf(root, u.LeaderUid) <= 0.8f) continue;
                    int next = MembersFast(u).Where(m => p.Uid[m] != u.LeaderUid && Adult(m)).OrderByDescending(m => Social.Opinion(p, m, root.LeaderUid)).FirstOrDefault(-1);
                    if (next < 0) continue;
                    int old = FindAlive(u.LeaderUid);
                    u.LeaderUid = p.Uid[next];
                    if (old >= 0) Social.Add(p, old, root.LeaderUid, -20f);
                    Stats.CmdRotated++;
                    break;
                }
        }
    }

    /// <summary>NPC 우두머리가 파벌의 요구를 들어줄 확률: 파벌이 셀수록, 곳간에 여유가 있을수록, 말솜씨가 좋을수록.</summary>
    private float DemandOdds(Faction f, Group root, int leader)
    {
        float share = f.Power / MathF.Max(1f, root.Strength);
        float spare = root.Treasury > 0f ? 0.2f : 0f;
        return Math.Clamp(0.2f + share + spare + 0.2f * People.Get(leader, Trait.Charisma) - 0.2f * People.Get(leader, Trait.Aggression), 0.05f, 0.9f);
    }

    private void MeetDemand(Faction f, Group root)
    {
        foreach (var u in FactionUnits(f)) u.BudgetWeight = MathF.Min(4f, u.BudgetWeight * 1.5f);
        foreach (int uid in f.Members) AddGrievance(root.Id, uid, -0.2f);
        Stats.DemandsMet++;
    }

    private void RefuseDemand(Faction f, Group root, int leader)
    {
        foreach (int uid in f.Members)
        {
            AddGrievance(root.Id, uid, 0.1f);
            if (FindAlive(uid) is int m && m >= 0) Social.Add(People, m, People.Uid[leader], -10f);
        }
    }

    /// <summary>파벌 갈라치기: 한 사람을 측근 자리에 앉혀 불만을 0.3 낮춘다.</summary>
    public bool Placate(Group root, int uid)
    {
        root = RootOf(root);
        int i = FindAlive(uid);
        if (i < 0 || uid == root.LeaderUid || !GroupsOf(i).Any(x => RootOf(x).Id == root.Id)) return false;
        root.Pinned.Add(uid); root.Inner.Add(uid);
        AddGrievance(root.Id, uid, -0.3f);
        Social.Add(People, i, root.LeaderUid, 10f);
        Stats.Placated++;
        return true;
    }

    /// <summary>세력 안 사람을 내쫓는다: 그 세력의 모든 갈래에서 빼고 자리를 거둔다. 같은 파벌 사람은 불만이 오른다.</summary>
    public bool Expel(Group root, int uid)
    {
        root = RootOf(root);
        int i = FindAlive(uid);
        if (i < 0 || uid == root.LeaderUid) return false;
        foreach (var g in GroupsOf(i).ToList()) if (RootOf(g).Id == root.Id) { if (g.LeaderUid == uid) g.LeaderUid = -1; LeaveGroup(i, g); }
        root.Inner.Remove(uid); root.Pinned.Remove(uid);
        if (root.TreasurerUid == uid) root.TreasurerUid = -1;
        if (root.LiaisonUid == uid) root.LiaisonUid = -1;
        foreach (var f in _factions) if (f.Root == root.Id && f.Members.Contains(uid)) foreach (int o in f.Members) if (o != uid) AddGrievance(root.Id, o, 0.1f);
        _grievance.Remove((root.Id, uid));
        Social.Add(People, i, root.LeaderUid, -40f, 20f);
        Stats.Expelled++;
        return true;
    }

    /// <summary>
    /// 세력 안 거사. 지배자 세력이면 쿠데타 틀(꾸밈, 새어 나감, 거사)에 올린다. 홀로 선 조직이면 그 자리에서 힘을 겨룬다:
    /// 감시가 있으면 먼저 캐낼 수 있고, 이기면 파벌의 장이 우두머리가 되고, 지면 쫓겨난다.
    /// </summary>
    private void InnerCoup(Faction f, Group root, int fl, int leader)
    {
        var p = People;
        Stats.InnerCoups++;
        if (root.Home >= 0)
        {
            int lord = root.Home;
            if (PlotOf(p.Household[fl]) != null || !CoupFoothold(fl, lord)) return;
            var x = StartPlot(fl, lord);
            foreach (int uid in f.Members) if (uid != f.Leader) { x.Backers.Add(uid); x.Known.Add(uid); }
            return;
        }
        if (_rng.Chance(MathF.Min(0.6f, 0.1f * SurveyOf(root))))
        {
            Chron(leader, $"{Names.Full(Lineage, f.Leader)}의 무리가 거사를 꾸미는 것을 미리 캐냈다");
            Expel(root, f.Leader);
            return;
        }
        float turn = DefectorPower(root, f);   // 지휘자를 두고 넘어올 사람(R13)
        float mine = f.Power + turn, theirs = MathF.Max(0.1f, root.Strength - f.Power - turn) * (0.8f + 0.4f * p.Get(leader, Trait.Aggression));
        float ratio = mine / (mine + theirs) + 0.2f * (_rng.NextFloat() - 0.5f);
        if (IsPlayer(leader)) _fork = true;
        string fn = Names.Full(Lineage, f.Leader), ln = Names.Full(Lineage, root.LeaderUid);
        if (ratio >= 0.5f)
        {
            Stats.InnerCoupWins++;
            int oldUid = root.LeaderUid;
            root.LeaderUid = f.Leader;
            foreach (var u in UnitsOf(root)) if (u.LeaderUid == f.Leader && u != root) u.LeaderUid = -1;
            JoinGroup(fl, root);
            root.Shaken = MathF.Min(1f, root.Shaken + 0.5f);
            foreach (int uid in f.Members) _grievance.Remove((root.Id, uid));
            if (FindAlive(oldUid) is int o && o >= 0) Social.Add(p, o, f.Leader, -60f);
            Chron(fl, $"{SubjName(fl)} 거사를 일으켜 {ln}에게서 {Obj(root.Name)} 빼앗았다");
            Chron(leader, $"{Subj(fn)} 거사를 일으켜 {Obj(root.Name)} 빼앗겼다");
        }
        else
        {
            Chron(leader, $"{fn}의 거사를 꺾었다");
            foreach (int uid in f.Members.ToList()) if (FindAlive(uid) >= 0) Expel(root, uid);
        }
        RecountTree();
    }

    /// <summary>파벌이 이끄는 갈래들을 데리고 떨어져 나간다(품삯 받는 갈래는 고용이 붙잡는다). 떨어져 나갔으면 true.</summary>
    private bool FactionSplit(Faction f, Group root, int fl)
    {
        var units = FactionUnits(f).Where(u => !u.Paid && u.Members >= 3).ToList();
        if (units.Count == 0) return false;
        var head = units.FirstOrDefault(u => u.LeaderUid == f.Leader) ?? units[0];
        head.Parent = -1; head.Sanction = -1; head.SanctionFounded = -1;
        head.Home = -1; head.HomeFounded = -1;
        foreach (var u in units) if (u != head) u.Parent = head.Id;
        foreach (int uid in f.Members) _grievance.Remove((root.Id, uid));
        SortFollowers(root, units, head, f.Leader);   // 지휘자를 따르지 않는 사람은 남고, 넘어오는 사람도 있다(R13)
        Stats.FactionSplits++;
        int leader = FindAlive(root.LeaderUid);
        if (leader >= 0)
        {
            if (IsPlayer(leader)) _fork = true;
            Chron(leader, $"{Subj(Names.Full(Lineage, head.LeaderUid))} {Obj(head.Name)} 이끌고 떨어져 나갔다");
            Social.Add(People, leader, head.LeaderUid, -40f);
        }
        RecountTree();
        return true;
    }

    private void AddFactionDefs(InteractionDef[] d)
    {
        int H(World w, int i) => w.People.Household[i];
        d[(int)InteractionId.FactionDemand] = new InteractionDef
        {
            // 파벌의 장 집 가장(Actor) → 세력 우두머리(Target). 받아들임 = 예산을 늘려 준다
            Id = InteractionId.FactionDemand, Name = "파벌의 요구", Category = InteractionCategory.Rule,
            Can = (w, c) => w.DemandFaction(H(w, c.Actor), c.Target) != null,
            Accept = (w, c) => w.DemandFaction(H(w, c.Actor), c.Target) is { } f && w.GroupById(f.Root) is { } r ? w.DemandOdds(f, r, c.Target) : 0f,
            Success = (w, c, _) =>
            {
                var f = w.DemandFaction(H(w, c.Actor), c.Target)!;
                w.MeetDemand(f, w.GroupById(f.Root)!);
                w.RiseChron(c, x => $"{Subj(x)} 우리 갈래의 몫을 늘려 주었다", x => $"{x}의 파벌에게 몫을 늘려 주었다");
            },
            Fail = (w, c) =>
            {
                var f = w.DemandFaction(H(w, c.Actor), c.Target);
                if (f == null) return;
                w.RefuseDemand(f, w.GroupById(f.Root)!, c.Target);
                w.RiseChron(c, x => $"몫을 늘려 달라 했지만 {Subj(x)} 거절했다", x => $"{x}의 파벌이 몫을 늘려 달라는 것을 거절했다");
            },
        };
    }

    /// <summary>집 h 사람이 이끄는 파벌 가운데, 우두머리가 leader 인 세력의 것.</summary>
    private Faction? DemandFaction(int h, int leader)
    {
        if (leader < 0) return null;
        int luid = People.Uid[leader];
        foreach (var f in _factions)
        {
            if (GroupById(f.Root) is not { } r || r.LeaderUid != luid) continue;
            if (FindAlive(f.Leader) is int fl && fl >= 0 && People.Household[fl] == h) return f;
        }
        return null;
    }
}
