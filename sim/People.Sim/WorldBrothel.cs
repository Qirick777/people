namespace People.Sim;

/// <summary>유곽에서 얻은 소식 하나.</summary>
public sealed class Intel
{
    public int Day, Kind;            // 0 비밀, 1 세력 수치, 2 속사정, 3 계획
    public string Text = "";
}

/// <summary>
/// 세력 개편 R11(plan_reform): 유곽.
/// - 직책: 주인(우두머리), 일꾼(몸 파는 일을 하는 어른), 경호(무예 0.3), 호객꾼(그 밖). 일꾼은 16세부터이고, 붙잡힌 사람과 빚에 묶인 사람은 두지 않는다(배치 규칙).
/// - 질 다섯 단계(0~4, 기본 2): 마을 하룻밤 거래가 0.8 + 0.2 × 질 배로 늘고, 주인 몫이 0.5 + 0.25 × 질 배가 된다. 일꾼 한 사람에 한 해 은화 1.5 × 질이 든다.
/// - 일꾼 몫(20~80%, 기본 50%): 주인 몫 = (1 − 몫) × 0.4 × 마을 하룻밤 거래 값. 몫이 높을수록 일꾼이 주인을 좋게 본다.
///   몫이 50% 아래면 해마다 0.4 × (0.5 − 몫)으로 떠난다(경호가 있으면 반).
/// - 일꾼 모집: 16~40세, 붙잡히지 않고 빚이 없는 어른이 스스로 든다. 확률 = 0.03 + 0.3 × 몫 + 가난 0.15 + 이미 이 일을 하면 0.3 − 순결 교리 0.3 + 분방 교리 0.2.
/// - 손님: 일꾼마다 한 해 3 + 질 + 호객꾼(많아야 2)명. 손님 한 명당 0.1 × (질 ÷ 2) × (일꾼 말솜씨 + 눈치) 확률로 그가 아는 것 하나를 듣는다.
///   비밀(우두머리가 그 비밀을 알게 되어 갈취·폭로를 쓸 수 있다), 세력 수치, 속사정(파벌·불만·장악도), 계획(아는 거사). 소식은 두 해가 지나면 낡아 사라진다.
///   일꾼의 조직 충성이 0.3 아래면 손님에게 우리 일이 샌다(새어 나감 +0.05).
/// - 접대: 손님이 남의 세력 우두머리나 지휘자면 우리 우두머리를 보는 눈이 3 오른다.
/// - 교리: 우두머리가 순결 교리를 따르면 유곽을 가진 세력의 평판이 해마다 0.02 × 강도 깎인다. 분방은 없다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, List<Intel>> _intel = new();
    private readonly Dictionary<int, float> _brothelBoost = new();   // 마을 → 하룻밤 거래 배율
    public static readonly float[] BrothelShares = { 0.2f, 0.35f, 0.5f, 0.65f, 0.8f };

    public IReadOnlyList<Intel> IntelOf(Group root) => _intel.TryGetValue(RootOf(root).Id, out var l) ? l : (IReadOnlyList<Intel>)Array.Empty<Intel>();
    public static bool IsBrothelUnit(Group u) => u.Tasks.Any(t => t.Verb == Verb.Brothel);

    public void SetBrothel(Group u, int quality, float share)
    {
        u.BrothelQuality = (byte)Math.Clamp(quality, 0, 4);
        u.WorkerShare = Math.Clamp(share, 0.2f, 0.8f);
    }

    private float BrothelBoost(int v) => _brothelBoost.GetValueOrDefault(v, 1f);

    private void InitBrothelRoles()
    {
        int at = Roles.FindIndex(r => r.Kind == RoleKind.Member);
        Roles.Insert(at, new RoleDef { Kind = RoleKind.Worker, Name = "일꾼", WageWeight = 0f, AppliesTo = (w, u) => IsBrothelUnit(u),
            Holds = (w, i, u) => IsBrothelUnit(u) && w.People.JobOf[i] == (byte)Job.SexWorker && w.AgeYears(i) >= 16 });
        Roles.Insert(at + 1, new RoleDef { Kind = RoleKind.Bouncer, Name = "경호", WageWeight = 1.5f, AppliesTo = (w, u) => IsBrothelUnit(u),
            Holds = (w, i, u) => IsBrothelUnit(u) && w.People.GetSkill(i, Skill.Arms) >= 0.3f });
        Roles.Insert(at + 2, new RoleDef { Kind = RoleKind.Tout, Name = "호객꾼", AppliesTo = (w, u) => IsBrothelUnit(u),
            Holds = (w, i, u) => IsBrothelUnit(u) });
    }

    /// <summary>해마다(일 앞에): 유곽이 있는 마을의 하룻밤 거래 배율.</summary>
    private void BrothelBoostYear()
    {
        _brothelBoost.Clear();
        foreach (var g in _groups)
            if (g.Alive && IsBrothelUnit(g))
                foreach (var t in g.Tasks)
                    if (t.Verb == Verb.Brothel && t.Village >= 0) _brothelBoost[t.Village] = MathF.Max(_brothelBoost.GetValueOrDefault(t.Village, 1f), 0.8f + 0.2f * g.BrothelQuality);
    }

    /// <summary>유곽 갈래의 한 해: 손님과 소식, 접대, 떠남, 비품 값, 교리.</summary>
    private void BrothelYear(Group root, Group unit, int v, List<int> workers, int leader)
    {
        var p = People; var hh = Households;
        int q = unit.BrothelQuality;
        var ms = MembersFast(unit);
        int touts = 0, bouncers = 0;
        foreach (int m in ms)
        {
            var r = RoleOf(m, unit);
            if (r?.Kind == RoleKind.Tout) touts++; else if (r?.Kind == RoleKind.Bouncer) bouncers++;
        }
        // 비품 값
        if (q > 0) PayFromTreasury(root, -1, 1.5f * q * workers.Count);
        var folk = VillageHouses(v);
        if (folk.Count == 0) return;
        var list = _intel.TryGetValue(root.Id, out var l0) ? l0 : _intel[root.Id] = new List<Intel>();
        foreach (int w in workers)
        {
            float loy = OrgLoyalty(root, p.Uid[w]);
            float listen = 0.1f * (q / 2f) * (p.Get(w, Trait.Charisma) + p.Get(w, Trait.Perception));
            int clients = 3 + q + Math.Min(2, touts);
            for (int k = 0; k < clients; k++)
            {
                int ch = folk[_rng.NextInt(folk.Count)];
                int c = HeadIdx(ch);
                if (c < 0 || c == w || !p.Male[c] || IsCaptive(c) || GroupsOf(c).Any(g => RootOf(g).Id == root.Id)) continue;
                Stats.BrothelClients++;
                // 접대: 남의 세력 우두머리·지휘자
                if (GroupsOf(c).Any(g => g.LeaderUid == p.Uid[c])) { Social.Add(p, c, root.LeaderUid, 3f); Stats.Hosted++; }
                if (loy < 0.3f && _rng.Chance(0.1f)) root.Leak += 0.05f;   // 충성이 낮은 일꾼은 거꾸로 흘린다
                if (_rng.Chance(listen) && Hear(c, root, leader) is { } it) { list.Add(it); Stats.IntelHeard++; }
            }
            // 떠남: 몫이 낮으면(경호가 있으면 덜)
            if (unit.WorkerShare < 0.5f && _rng.Chance(0.4f * (0.5f - unit.WorkerShare) * (bouncers > 0 ? 0.5f : 1f))) { LeaveGroup(w, unit); continue; }
            Social.Add(p, w, root.LeaderUid, 3f * unit.WorkerShare / 0.5f);
        }
        if (leader >= 0 && DoctrineOn(leader, Doctrine.Chastity) is var ch2 && ch2 > 0f) AddRepRoot(root, 0f, -0.02f * ch2, 0f);
    }

    /// <summary>손님 c 가 아는 것 하나.</summary>
    private Intel? Hear(int c, Group root, int leader)
    {
        var p = People; var hh = Households;
        int cu = p.Uid[c];
        string who = Names.Full(Lineage, cu);
        var opts = new List<Func<Intel?>>();
        // 비밀
        var sec = _secrets.FirstOrDefault(s => !s.Exposed && s.Known.Contains(cu) && leader >= 0 && !s.Known.Contains(p.Uid[leader]) && s.A != p.Uid[leader] && s.B != p.Uid[leader]);
        if (sec != null)
            opts.Add(() =>
            {
                sec.Known.Add(p.Uid[leader]);
                int other = sec.A == cu ? sec.B : sec.A;
                string about = sec.A == cu || sec.B == cu ? who : Names.Full(Lineage, sec.A);
                return new Intel { Day = Day, Kind = 0, Text = $"{about}의 비밀({SecretName(sec.Type)})" + (other >= 0 && other != cu && sec.A == cu ? $", 상대 {Names.Full(Lineage, other)}" : "") };
            });
        // 세력 수치
        int h = p.Household[c];
        int lord = TierOf(h) >= 1 ? h : PatronOf(h);
        if (lord >= 0 && hh.IsAlive(lord))
            opts.Add(() => new Intel { Day = Day, Kind = 1, Text = $"{RealmName(lord)}: 아래 집 {BelowOf(lord)}, 힘 {LordPower(lord):0}, 식량 {hh.Food[lord]:0}, 은화 {hh.Coin[lord]:0}" });
        // 속사정
        var org = GroupsOf(c).Select(RootOf).FirstOrDefault(r => r.Id != root.Id);
        if (org != null)
            opts.Add(() => new Intel { Day = Day, Kind = 2, Text = $"{org.Name}: 사람 {org.Total}, 장악도 {org.Grip:0.00}, 곳간 {org.Treasury:0}" + (org.Need > 0f ? $", 예산 채움 {org.Fill:0%}" : "") });
        // 계획
        var plot = _plots.FirstOrDefault(x => PlotLive(x) && x.Known.Contains(cu) && !x.Exposed);
        if (plot != null)
            opts.Add(() => new Intel { Day = Day, Kind = 3, Text = $"{RealmName(plot.Lord)}을 노리는 거사가 있다(꾸미는 사람 {Names.Full(Lineage, plot.Plotter)}, 편 {plot.Backers.Count}명)" });
        if (opts.Count == 0) return null;
        return opts[_rng.NextInt(opts.Count)]();
    }

    /// <summary>해마다: 두 해 지난 세력 소식은 지운다(비밀은 우두머리가 이미 안다).</summary>
    private void IntelYear()
    {
        foreach (var (rid, l) in _intel.ToList())
        {
            if (GroupById(rid) is not { Alive: true }) { _intel.Remove(rid); continue; }
            l.RemoveAll(x => Day - x.Day > 2 * Config.DaysPerYear);
            if (l.Count > 40) l.RemoveRange(0, l.Count - 40);
        }
    }

    // 시험용
    public void BrothelNow(Group unit)
    {
        var root = RootOf(unit);
        var ws = MembersFast(unit).Where(m => People.JobOf[m] == (byte)Job.SexWorker && AgeYears(m) >= 16 && !IsCaptive(m)).ToList();
        BrothelYear(root, unit, unit.Village, ws, FindAlive(root.LeaderUid));
    }
}
