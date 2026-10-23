namespace People.Sim;

/// <summary>
/// 세력 개편 R13(plan_reform): 갈라서기. 쿠데타(지배자 세력의 거사), 파벌 거사, 독립 선언, 떼어 나가기가 같은 편 고르기를 쓴다.
/// - 편 고르기: 0.5 + 나를 향한 개인 충성 − 옛 우두머리를 향한 개인 충성 − (옛 세력 조직 충성 − 0.3) + (내 세력에 들어 있으면 내 세력 조직 충성 − 0.3)
///   + 0.2 × (야망 − 0.5). 개인 충성 = 함께한 햇수(그가 지휘자일 때) × 0.1 + 보는 눈 ÷ 200 + 쌓인 덤(여기서는 0~1로 자르지 않는다).
///   광신도와 신앙 세력 광신도는 믿는 쪽에 고정이다. 지배자 쿠데타는 여기에 아는 권위와 섬김의 의무를 더한다(예전과 같다).
/// - 갈래 따라오기: 지휘자가 내 편이면, 지휘자를 향한 개인 충성이 옛 세력 조직 충성보다 큰 조직원만 따라온다. 나머지는 옛 세력에 남는다.
///   지휘자가 남아도, 나를 향한 개인 충성이 옛 세력 조직 충성 + 0.1보다 큰 조직원은 넘어온다.
///   쿠데타 내전, 파벌 떼어 나가기에 쓰고, 파벌 거사에서는 넘어올 사람의 힘을 거사 쪽에 더한다.
/// - 떠보기: 거사하는 사람이 세력 안 사람의 기울기를 어림한다. 어림의 흔들림은 ±0.25(내 세력에 감찰대가 있으면 ±0.08).
///   떠볼 때마다 15%로 그 사람이 거사를 알게 된다(알면 거절할 때처럼 알릴 수 있다).
/// - 찬탈 뒤: 계승(옛 세력의 조직·이름·지위·충성을 이어받고 흔들림 +0.5, 주군 아래 그 자리에 앉는다. 예전과 같다)
///   또는 흡수(옛 조직은 와해되고 그 사람들은 내 조직에 조직 충성 0.2로 들어온다. 옛 주군 아래 자리는 잃고 홀로 선다).
///   NPC는 계승한다. 플레이어는 거사 창에서 고른다. 바깥에서 이긴 전쟁은 지금처럼 봉신으로 삼는다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<(int plot, int uid), float> _leanEst = new();

    /// <summary>i 가 target 을 향한 개인 충성(자르지 않음). 그가 i 의 지휘자면 지휘한 햇수를 센다.</summary>
    private float RawLoyalty(int i, int targetUid)
    {
        var p = People;
        if (!p.IsAliveIndex(i) || targetUid < 0) return 0f;
        float years = 0f;
        foreach (var g in GroupsOf(i)) if (g.LeaderUid == targetUid && g.CmdUid == targetUid) years = MathF.Max(years, (Day - g.CmdSince) / (float)Config.DaysPerYear);
        return 0.1f * years + Social.Opinion(p, i, targetUid) / 200f + _plBonus.GetValueOrDefault((p.Uid[i], targetUid));
    }

    /// <summary>편 고르기의 바탕(0.5가 가운데, 자르지 않음). 광신이면 0이나 1.</summary>
    private float SideLean(int c, int rebelUid, int oldLeaderUid, Group? oldRoot, Group? rebelRoot)
    {
        var p = People;
        int uid = p.Uid[c];
        if (oldRoot != null && IsZealot(uid, oldRoot)) return 0f;
        if (rebelRoot != null && IsZealot(uid, rebelRoot)) return 1f;
        if (p.Fanatic[c] != 0) { if (p.LoyalUid[c] == oldLeaderUid) return 0f; if (p.LoyalUid[c] == rebelUid) return 1f; }
        float s = 0.5f + RawLoyalty(c, rebelUid) - RawLoyalty(c, oldLeaderUid) + 0.2f * (p.Get(c, Trait.Ambition) - 0.5f);
        if (oldRoot != null) s -= OrgLoyalty(oldRoot, uid) - 0.3f;
        if (rebelRoot != null && GroupsOf(c).Any(g => RootOf(g).Id == rebelRoot.Id)) s += OrgLoyalty(rebelRoot, uid) - 0.3f;
        return s;
    }

    /// <summary>떼어 가는 갈래 g 에서 지휘자를 따르지 않고 옛 세력에 남을 사람.</summary>
    private List<int> Stayers(Group g, Group oldRoot)
    {
        var p = People;
        var list = new List<int>();
        int cmd = g.LeaderUid;
        foreach (int m in Members(g))
            if (p.Uid[m] != cmd && !IsCaptive(m) && RawLoyalty(m, cmd) < OrgLoyalty(oldRoot, p.Uid[m])) list.Add(m);
        return list;
    }

    /// <summary>옛 세력에 남는 갈래 g 에서 지휘자를 두고 rebel 에게 넘어올 사람.</summary>
    private List<int> Defectors(Group g, Group oldRoot, int rebelUid)
    {
        var p = People;
        var list = new List<int>();
        foreach (int m in Members(g))
            if (p.Uid[m] != g.LeaderUid && p.Uid[m] != rebelUid && !IsCaptive(m) && RawLoyalty(m, rebelUid) > OrgLoyalty(oldRoot, p.Uid[m]) + 0.1f) list.Add(m);
        return list;
    }

    /// <summary>
    /// 떼어 나간 뒤 사람을 다시 나눈다. taken: 떼어 간 갈래들, head: 새 세력의 맨 위 갈래.
    /// 남을 사람은 옛 세력 맨 위로, 넘어올 사람은 head 로 옮긴다. 옮긴 사람 수.
    /// </summary>
    private (int stayed, int defected) SortFollowers(Group oldRoot, List<Group> taken, Group head, int rebelUid, Action<int, bool>? moved = null)
    {
        int stayed = 0, defected = 0;
        foreach (var g in taken)
            foreach (int m in Stayers(g, oldRoot))
            {
                LeaveGroup(m, g);
                if (!InGroup(m, oldRoot)) JoinGroup(m, oldRoot);
                moved?.Invoke(m, false);
                stayed++;
            }
        var takenIds = taken.Select(t => t.Id).ToHashSet();
        foreach (var g in Subtree(oldRoot).ToList())
        {
            if (takenIds.Contains(g.Id) || (g != oldRoot && RootOf(g).Id != oldRoot.Id)) continue;
            foreach (int m in Defectors(g, oldRoot, rebelUid))
            {
                LeaveGroup(m, g);
                if (!InGroup(m, head)) JoinGroup(m, head);
                moved?.Invoke(m, true);
                defected++;
            }
        }
        Stats.SecedeStayed += stayed; Stats.SecedeDefected += defected;
        return (stayed, defected);
    }

    /// <summary>파벌 거사에서 지휘자를 두고 넘어올 사람의 힘.</summary>
    private float DefectorPower(Group root, Faction f)
    {
        float s = 0f;
        foreach (var g in UnitsOf(root))
        {
            if (f.Members.Contains(g.LeaderUid)) continue;
            foreach (int m in Defectors(g, root, f.Leader)) s += FightPower(m);
        }
        return s;
    }

    /// <summary>떠보기: 거사하는 사람이 c 의 기울기를 어림한다. 떠볼 때마다 새어 나갈 수 있다.</summary>
    public float ProbeLean(Plot x, int c)
    {
        var p = People;
        int pi = FindAlive(x.Plotter);
        if (pi < 0 || !p.IsAliveIndex(c)) return 0.5f;
        float lean = Lean(c, pi, x.Lord);
        bool eyes = PlotterOrg(pi) is { } po && SurveyOf(po) > 0f;
        float est = Math.Clamp(lean + (_rng.NextFloat() * 2f - 1f) * (eyes ? 0.08f : 0.25f), 0f, 1f);
        _leanEst[(x.Id, p.Uid[c])] = est;
        Stats.Probes++;
        if (!x.Known.Contains(p.Uid[c]) && _rng.Chance(0.15f))
        {
            x.Known.Add(p.Uid[c]);
            int lh = HeadIdx(x.Lord);
            float report = lh >= 0 && p.LoyalUid[c] == p.Uid[lh] ? (p.Fanatic[c] != 0 ? 0.9f : 0.2f + 0.6f * p.Loyalty[c]) : 0.1f;
            if (lean < 0.3f && _rng.Chance(report)) Expose(x);
        }
        return est;
    }

    /// <summary>떠본 어림(없으면 -1).</summary>
    public float LeanEstimate(Plot x, int c) => People.IsAliveIndex(c) && _leanEst.TryGetValue((x.Id, People.Uid[c]), out var e) ? e : -1f;

    /// <summary>흡수: 옛 지배자 조직 lo 는 와해되고 사람들은 내 조직 po 에 조직 충성 0.2로 들어온다.</summary>
    private void Absorb(Group lo, Group po)
    {
        var p = People;
        foreach (var g in Subtree(lo).ToList())
        {
            if (g == lo || g == po) continue;
            if (RootOf(g).Id != lo.Id) continue;
            if (!SetParent(g, po)) continue;
            foreach (int m in Members(g)) _orgLoyal[(po.Id, p.Uid[m])] = 0.2f;
        }
        foreach (int m in Members(lo).ToList())
        {
            LeaveGroup(m, lo);
            if (GroupsOf(m).Count() < GroupSlots && JoinGroup(m, po)) _orgLoyal[(po.Id, p.Uid[m])] = 0.2f;
        }
        CloseGroup(lo);
        Stats.Absorbed++;
    }

    // 시험용
    public void SetOrgLoyaltyNow(Group root, int uid, float v) => _orgLoyal[(RootOf(root).Id, uid)] = v;
    public (int stayed, int defected) SortNow(Group oldRoot, List<Group> taken, Group head, int rebelUid) => SortFollowers(oldRoot, taken, head, rebelUid);
    public void AbsorbNow(Group lo, Group po) => Absorb(lo, po);
    public float SideLeanNow(int c, int rebelUid, int oldLeaderUid, Group? oldRoot) => SideLean(c, rebelUid, oldLeaderUid, oldRoot, null);
}
