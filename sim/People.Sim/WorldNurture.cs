namespace People.Sim;

/// <summary>
/// 세력 개편 R12(plan_reform): 육아와 피임.
/// - 피임 약초: 임신 확률을 25% 줄인다(예전 60%). 다섯 해 넘게 쓰면 살 때마다 3%로 아이를 못 갖게 된다.
/// - 질외사정: 혼인 밖 관계에서 남자가 따르면 임신 확률을 40% 줄인다. 따를 확률 = 0.3 + 0.7 × 남자의 신중.
///   아이가 다섯 넘고 형편이 모자란 부부도 같은 방법으로 터울을 둔다.
/// - 오래 젖 먹이기: 아이가 여섯 넘는 어머니는 낳은 뒤 60일 더 아이가 들지 않는다(180일은 인구를 크게 줄였다).
/// - 기를 곳: 어머니 집(기본), 친아버지 집(인정. 어머니가 아버지를 좋게 보지 않으면 0.5 − 보는 눈 ÷ 100 확률로 거부해 아이는 남는다),
///   맡김(다른 집 가장에게 수양), 보육원(양육 일을 맡은 갈래), 버림.
/// - 버림: 버린 사람 명예 −0.1. 그 마을 보육원이 있으면 거두고, 없으면 40%로 넉넉한 이웃이 거두고 아니면 죽는다.
///   NPC: 혼인하지 않은 가난한 어머니(형편 0.5 아래, 신앙 0.5 아래)는 사생아를 10%로 버린다.
/// - 보육원(양육 일): 원장(우두머리)과 보모. 아이 한 명당 한 해 식량 12(상근 군량의 절반)를 원장 집이 낸다.
///   이름은 세력의 성격을 따른다: 교단이면 수도원 보육원, 무력이면 유년 병영, 그 밖은 보육원.
///   맡은 아이는 원장 집에서 살고 해마다 조직 충성 +0.05(많아야 0.8), 원장을 보는 눈 +3. 교단이면 신앙 +0.02, 무력이면 무예 +0.02.
///   16세가 되면 조직 충성만큼의 확률로 그 세력에 든다(교단이면 그 종교로).
/// - 유곽 아이: 일꾼이 낳은 아이는 같은 세력에 보육원이 있으면 반은 그리로 간다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, int> _wards = new();        // 맡은 아이 uid → 보육원 갈래 Id
    private readonly Dictionary<int, int> _contraYears = new();  // 피임 약초를 산 해 수
    private readonly HashSet<int> _barren = new();               // 오래 쓴 약초로 아이를 못 갖는 사람 uid

    public bool IsWard(int i) => People.IsAliveIndex(i) && _wards.ContainsKey(People.Uid[i]);
    public int WardCount(Group u) => _wards.Values.Count(x => x == u.Id);
    public static bool IsNursery(Group u) => u.Tasks.Any(t => t.Verb == Verb.Nurture);

    public string NurseryLabel(Group u)
    {
        var root = RootOf(u);
        if (root.ReligionId >= 0 || root.Kind == GroupKind.Brotherhood) return "수도원 보육원";
        if (IsForce(root.Kind)) return "유년 병영";
        return "보육원";
    }

    private void InitNurtureRoles()
    {
        int at = Roles.FindIndex(r => r.Kind == RoleKind.Member);
        Roles.Insert(at, new RoleDef { Kind = RoleKind.Nanny, Name = "보모", AppliesTo = (w, u) => IsNursery(u), Holds = (w, i, u) => IsNursery(u) && w.Adult(i) });
    }

    /// <summary>피임 약초를 한 해 치 샀다.</summary>
    private void TookHerb(int i)
    {
        int u = People.Uid[i];
        int n = _contraYears.GetValueOrDefault(u) + 1;
        _contraYears[u] = n;
        if (n > 5 && _rng.Chance(0.03f) && _barren.Add(u)) Stats.HerbBarren++;
    }

    private bool Barren(int i) => _barren.Contains(People.Uid[i]);

    /// <summary>질외사정으로 줄이는 몫(남자가 따를 때 0.4).</summary>
    private float Withdrawal(int m) => _rng.Chance(0.3f + 0.7f * People.Get(m, Trait.Caution)) ? Config.WithdrawalCut : 0f;

    /// <summary>터울을 두려는 부부인가(아이 다섯 넘고 형편이 모자란 집).</summary>
    private bool Spacing(int w) => Lineage.Children(People.Uid[w]) >= Config.SpacingChildren && Households.IsAlive(People.Household[w]) && Surplus(People.Household[w]) < 0.8f;

    /// <summary>보육원 unit 이 아이 i 를 맡는다(원장 집으로 옮긴다).</summary>
    public bool Entrust(int i, Group unit)
    {
        var p = People;
        int leader = FindAlive(unit.LeaderUid);
        if (!p.IsAliveIndex(i) || AgeYears(i) >= 12 || leader < 0 || !IsNursery(unit)) return false;
        int home = p.Household[leader];
        if (!Households.IsAlive(home)) return false;
        if (p.Household[i] != home) MoveTo(i, home);
        _wards[p.Uid[i]] = unit.Id;
        Stats.Wards++;
        return true;
    }

    /// <summary>아이를 버린다. 보육원이 거두거나, 이웃이 거두거나, 죽는다.</summary>
    public void Abandon(int child, int by)
    {
        var p = People; var hh = Households;
        if (!p.IsAliveIndex(child)) return;
        if (by >= 0 && p.IsAliveIndex(by)) p.Honor[by] -= 0.1f;
        Stats.Abandoned++;
        int v = hh.Village[p.Household[child]];
        var nursery = _groups.FirstOrDefault(g => g.Alive && IsNursery(g) && g.Tasks.Any(t => t.Verb == Verb.Nurture && t.Village == v));
        if (nursery != null && Entrust(child, nursery)) { Stats.Foundlings++; return; }
        if (v >= 0 && _rng.Chance(0.4f))
        {
            var hs = VillageHouses(v).Where(h => h != p.Household[child] && Surplus(h) >= 1f && HeadIdx(h) >= 0).ToList();
            if (hs.Count > 0)
            {
                int h = hs[_rng.NextInt(hs.Count)];
                MoveTo(child, h);
                Social.Add(p, child, p.Uid[HeadIdx(h)], 30f);
                Stats.Foundlings++;
                return;
            }
        }
        Die(child, true);
    }

    /// <summary>낳은 뒤: NPC 어머니가 아이를 어디서 기를지 정한다.</summary>
    private void PlaceNewborn(int mother, PersonId child)
    {
        var p = People;
        if (!p.IsValid(child) || IsPlayer(mother) || mother == _player) return;
        int c = child.Index;
        int h = p.Household[mother];
        // 유곽 일꾼의 아이: 같은 세력 보육원이 있으면 반은 그리로
        if (p.JobOf[mother] == (byte)Job.SexWorker)
        {
            var nursery = GroupsOf(mother).Select(RootOf).SelectMany(r => Subtree(r)).FirstOrDefault(IsNursery);
            if (nursery != null && _rng.Chance(0.5f) && Entrust(c, nursery)) return;
        }
        // 혼인하지 않은 가난한 어머니는 사생아를 버리기도 한다
        if (Lineage.Father(p.Uid[c]) < 0 && !p.IsValid(p.Husband[mother]) && Households.IsAlive(h) && Surplus(h) < 0.5f && p.Piety[mother] < 0.5f && _rng.Chance(0.1f))
            Abandon(c, mother);
    }

    /// <summary>해마다: 보육원의 먹이기와 기르기, 16세가 된 아이.</summary>
    private void NurtureYear()
    {
        var p = People; var hh = Households;
        foreach (var (uid, uid2) in _wards.ToList())
        {
            int i = FindAlive(uid);
            var unit = GroupById(uid2);
            if (i < 0) { _wards.Remove(uid); continue; }
            if (unit == null || !IsNursery(unit)) { _wards.Remove(uid); continue; }   // 보육원이 없어지면 그 집 아이로 남는다
            var root = RootOf(unit);
            int leader = FindAlive(unit.LeaderUid);
            int home = leader >= 0 ? p.Household[leader] : -1;
            if (home >= 0 && hh.IsAlive(home)) hh.Food[home] = MathF.Max(0f, hh.Food[home] - 12f);
            float loy = MathF.Min(0.8f, OrgLoyalty(root, uid) + 0.05f);
            _orgLoyal[(root.Id, uid)] = loy;
            if (leader >= 0) Social.Add(p, i, unit.LeaderUid, 3f);
            if (root.ReligionId >= 0) p.Piety[i] = MathF.Min(1f, p.Piety[i] + 0.02f);
            else if (IsForce(root.Kind)) p.SetSkill(i, Skill.Arms, p.GetSkill(i, Skill.Arms) + 0.02f);
            if (AgeYears(i) < 16) continue;
            _wards.Remove(uid);
            if (_rng.Chance(loy) && GroupsOf(i).Count() < GroupSlots && JoinGroup(i, unit))
            {
                if (root.ReligionId >= 0) Converted(root, i);
                Stats.WardsJoined++;
            }
        }
    }

    private void AddNurtureDefs(InteractionDef[] d)
    {
        d[(int)InteractionId.Foster] = new InteractionDef
        {
            // 내 집 아이(Third)를 다른 집 가장(Target)에게 맡긴다. 값(은화)을 얹을 수 있다
            Id = InteractionId.Foster, Name = "아이 맡기기", Category = InteractionCategory.Family, NeedsThird = true, ThirdLabel = "누구를 맡길까",
            Amounts = new[] { 0f, 10f, 30f }, AmountNames = new[] { "그냥", "은화 10", "은화 30" },
            Can = (w, c) => c.Third >= 0 && w.AgeYears(c.Third) < 12 && w.SameHouse(c.Actor, c.Third) && !w.SameHouse(c.Actor, c.Target)
                            && w.HeadIdx(w.People.Household[c.Target]) == c.Target && w.Adult(c.Actor),
            Accept = (w, c) => Math.Clamp(0.2f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 100f
                                          + (w.Surplus(w.People.Household[c.Target]) >= 1.2f ? 0.2f : -0.2f) + c.Amount / 60f, 0f, 0.95f),
            Success = (w, c, _) =>
            {
                var p = w.People;
                if (c.Amount > 0f) w.PayCoin(p.Household[c.Actor], p.Household[c.Target], c.Amount);
                w.MoveTo(c.Third, p.Household[c.Target]);
                Social.Add(p, c.Third, p.Uid[c.Target], 20f);
                w.Stats.Fostered++;
                w.Chron(c.Actor, $"{w.SubjName(c.Actor)} {Obj(Names.Full(w.Lineage, p.Uid[c.Third]))} {Names.Full(w.Lineage, p.Uid[c.Target])}에게 맡겼다");
            },
        };
        d[(int)InteractionId.EntrustNursery] = new InteractionDef
        {
            // 내 집 아이(Third)를 보육원 우두머리(Target)에게 맡긴다
            Id = InteractionId.EntrustNursery, Name = "보육원에 맡기기", Category = InteractionCategory.Family, NeedsThird = true, ThirdLabel = "누구를 맡길까",
            Can = (w, c) => c.Third >= 0 && w.AgeYears(c.Third) < 12 && w.SameHouse(c.Actor, c.Third) && w.NurseryOf(c.Target) != null && w.Adult(c.Actor),
            Accept = (w, c) => Math.Clamp(0.5f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 100f - 0.05f * w.WardCount(w.NurseryOf(c.Target)!), 0.05f, 0.95f),
            Success = (w, c, _) =>
            {
                var u = w.NurseryOf(c.Target)!;
                if (w.Entrust(c.Third, u)) w.Chron(c.Actor, $"{w.SubjName(c.Actor)} {Obj(Names.Full(w.Lineage, w.People.Uid[c.Third]))} {u.Name}에 맡겼다");
            },
        };
        d[(int)InteractionId.Abandon] = new InteractionDef
        {
            // 제 아이(Target, 6세 아래)를 버린다
            Id = InteractionId.Abandon, Name = "아이 버리기", Category = InteractionCategory.Family, Harsh = true,
            Can = (w, c) => w.AgeYears(c.Target) < 6 && w.SameHouse(c.Actor, c.Target) && w.Adult(c.Actor) && w.IsParentOf(c.Actor, c.Target),
            Success = (w, c, _) =>
            {
                string name = Names.Full(w.Lineage, w.People.Uid[c.Target]);
                w.Abandon(c.Target, c.Actor);
                w.Chron(c.Actor, $"{w.SubjName(c.Actor)} {Obj(name)} 버렸다");
            },
        };
    }

    private bool IsParentOf(int a, int c)
    {
        var p = People;
        var id = p.IdAt(a);
        return (p.IsValid(p.Mother[c]) && p.Mother[c] == id) || (p.IsValid(p.Father[c]) && p.Father[c] == id);
    }

    /// <summary>사람 i 가 우두머리인 보육원 갈래.</summary>
    private Group? NurseryOf(int i) => _groups.FirstOrDefault(g => g.Alive && g.LeaderUid == People.Uid[i] && IsNursery(g));

    // 시험용
    public void RecountNurture() => NurtureYear();
    public void TakeHerbNow(int i, int years) { for (int k = 0; k < years; k++) TookHerb(i); }
    public bool IsBarren(int i) => Barren(i);
}
