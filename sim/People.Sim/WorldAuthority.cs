namespace People.Sim;

/// <summary>조직이 얼마나 알려졌나. 우두머리가 고르고, 새어 나가면 공개 쪽으로 한 칸씩 내려간다.</summary>
public enum Visibility : byte { Hidden, Covert, Neutral, Public, Proclaimed }

/// <summary>조직 안에서의 자리. 하위 조직이 생기면 최상위에 남은 사람은 측근과 소속원이 된다.</summary>
public enum OrgRole : byte { None, Leader, Inner, Worker, Member }

/// <summary>
/// O1 권위와 알려진 정도.
/// - 권위 = (자금 + 권력 + 무력) × (0.5 + 조직원 충성). 해마다 맨 위 조직마다 한 번 센다.
/// - 사람마다 아는 권위는 저장하지 않는다. 공개 수준과 그 사람의 자리로 그때그때 계산한다.
/// - 자리 잡은 정도: 마을마다 지배자 조직의 (밖에서 보이는) 권위가 미치는 크기. 빈 땅에서는 새 세력이 쉽게 서고, 꽉 찬 곳에서는 어렵다.
/// - 숨긴 조직은 포섭으로만 커지고, 완전 공개 조직은 조직원의 자식이 저절로 소속원이 된다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, float> _established = new();   // 마을 → 그 마을에 미치는 지배자 권위
    private Dictionary<int, List<int>>? _memberIdx;                   // 조직 → 조직원(해마다 한 번 만든다)

    private void BuildMemberIndex()
    {
        var p = People;
        _memberIdx = new();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            for (int k = 0; k < GroupSlots; k++)
            {
                int id = p.GroupIds[i * GroupSlots + k];
                if (id >= 0) (_memberIdx.TryGetValue(id, out var l) ? l : _memberIdx[id] = new()).Add(i);
            }
        }
    }

    private List<int> MembersFast(Group g) => _memberIdx != null ? _memberIdx.GetValueOrDefault(g.Id) ?? new List<int>() : Members(g);

    public static string VisibilityName(Visibility v) => v switch
    {
        Visibility.Hidden => "완전 은폐", Visibility.Covert => "일부 은폐", Visibility.Neutral => "중립", Visibility.Public => "공개", _ => "완전 공개",
    };

    /// <summary>모임 종류마다 처음 알려진 정도. 도적 떼는 숨고, 자경단은 드러내고, 신도회는 중립.</summary>
    private static Visibility DefaultVisibility(GroupKind k) => k switch
    {
        GroupKind.Bandits => Visibility.Covert, GroupKind.Brotherhood or GroupKind.Brothel => Visibility.Neutral, _ => Visibility.Public,
    };

    /// <summary>자리 잡은 정도(0~1). 0이면 빈 땅, 1에 가까울수록 기존 세력이 꽉 잡은 땅.</summary>
    public float Established(int v)
    {
        float a = _established.GetValueOrDefault(v);
        return a / (a + 100f);   // 100집 세력 곁이 0.6쯤, 20집 세력 곁이 0.2쯤
    }

    // ------------------------------------------------------------------ 자리

    /// <summary>사람 i 가 조직 g 의 맨 위 조직 안에서 어떤 자리인가.</summary>
    public OrgRole RoleIn(int i, Group root)
    {
        var p = People;
        if (p.Uid[i] == root.LeaderUid) return OrgRole.Leader;
        if (root.Home >= 0 && _employer.TryGetValue(i, out int l) && l == root.Home) return OrgRole.Worker;
        foreach (var g in GroupsOf(i))
        {
            if (RootOf(g).Id != root.Id) continue;
            if (g.Id != root.Id) return OrgRole.Worker;
            if (!HasChildren(root)) return OrgRole.Worker;
            return root.Inner.Contains(p.Uid[i]) ? OrgRole.Inner : OrgRole.Member;
        }
        // 지배자 조직: 그 세력의 땅에 사는 사람은 소속원(국민)
        if (root.Home >= 0 && Households.IsAlive(p.Household[i]) && (p.Household[i] == root.Home || InTreeOf(p.Household[i], root.Home))) return OrgRole.Member;
        return OrgRole.None;
    }

    /// <summary>
    /// 하위 조직이 있는 맨 위 조직은 일을 갈래에 넘기고, 바로 딸린 사람 가운데 우두머리를 가장 좋게 보는 사람들을
    /// 거느릴 수 있는 만큼 측근으로 둔다. 나머지는 소속원이다.
    /// </summary>
    private void PickInner(Group root)
    {
        root.Inner.Clear();
        if (!ChildrenOf(root).Any()) return;
        int leader = FindAlive(root.LeaderUid);
        if (leader < 0) return;
        int span = CommandSpan(leader);
        var p = People;
        foreach (int m in MembersFast(root).Where(m => Adult(m) && m != leader).OrderByDescending(m => Social.Opinion(p, m, root.LeaderUid)).Take(span))
            root.Inner.Add(p.Uid[m]);
    }

    // ------------------------------------------------------------------ 권위

    /// <summary>맨 위 조직 하나의 권위(자금, 권력, 무력, 충성). 무작위를 쓰지 않는다(화면에서 불러도 역사가 바뀌지 않게).</summary>
    private Dictionary<int, List<int>>? _directClients;
    private int _directClientsDay = -1;

    /// <summary>지배자 집을 바로 따르는 집들(하루에 한 번 만든다. 쓰는 쪽에서 지금도 따르는지 본다).</summary>
    private List<int> DirectClients(int lord)
    {
        if (_directClients == null || _directClientsDay != Day)
        {
            _directClients ??= new();
            foreach (var l in _directClients.Values) l.Clear();
            for (int h = 0; h < Households.HighWater; h++)
            {
                int l = Households.IsAlive(h) ? PatronOf(h) : -1;
                if (l >= 0) (_directClients.TryGetValue(l, out var list) ? list : _directClients[l] = new()).Add(h);
            }
            _directClientsDay = Day;
        }
        return _directClients.TryGetValue(lord, out var x) ? x : _noHouses;
    }

    private void ComputeAuthority(Group g)
    {
        var p = People; var hh = Households;
        int leader = FindAlive(g.LeaderUid);
        int home = g.Home >= 0 ? g.Home : leader >= 0 ? p.Household[leader] : -1;
        float money = (hh.IsAlive(home) ? MathF.Max(0f, hh.Food[home]) / 50f + (home < _incTribute.Length ? _incTribute[home] / 20f : 0f) : 0f) + g.Treasury / 50f;
        float power = g.Home >= 0 ? BelowOf(g.Home) + 1 : g.Total * 0.5f;
        float force = 2f * g.Strength;
        // 충성: 지배자 조직은 따르는 가장의 충성, 모임은 조직원이 우두머리를 보는 눈
        float loyal = 0.5f;
        if (g.Home >= 0)
        {
            float s = 0f; int n = 0;
            foreach (int h in DirectClients(g.Home))
            {
                if (!hh.IsAlive(h) || PatronOf(h) != g.Home || HeadIdx(h) is not (int hd and >= 0)) continue;
                s += p.LoyalUid[hd] == g.LeaderUid ? p.Loyalty[hd] : 0.4f;
                n++;
            }
            if (n > 0) loyal = s / n;
        }
        else
        {
            var ms = MembersFast(g);
            if (ms.Count > 0) loyal = ms.Take(20).Average(m => Math.Clamp((Social.Opinion(p, m, g.LeaderUid) + 50f) / 100f, 0f, 1f));
        }
        g.Loyalty = loyal;
        g.Authority = (money + power + force) * (0.5f + loyal) * (1f - 0.3f * g.Shaken);
    }

    /// <summary>해마다 맨 위 조직마다 권위를 세고, 마을마다 자리 잡은 정도를 잰다.</summary>
    private void AuthorityYear()
    {
        var hh = Households;
        foreach (var g in _groups) if (g.Alive && IsTop(g)) { ComputeAuthority(g); g.Shaken *= 0.7f; }
        // 자리 잡은 정도: 지배자 조직의 밖에서 보이는 권위가 마을마다 미치는 크기(멀수록 줄어든다)
        _established.Clear();
        var vl = VillageList;
        foreach (var g in _groups)
        {
            if (!g.Alive || g.Parent >= 0 || g.Home < 0 || !hh.IsAlive(g.Home)) continue;
            float seen = g.Authority * OutsideFactor(g, 0f);
            if (seen <= 0.5f) continue;
            int hx = hh.X[g.Home], hy = hh.Y[g.Home];
            for (int v = 0; v < vl.Count; v++)
            {
                if (!vl.Alive[v]) continue;
                float dx = vl.CenterX[v] - hx, dy = vl.CenterY[v] - hy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > Config.VassalRange) continue;
                float a = seen / (1f + d / 100f);
                if (a > _established.GetValueOrDefault(v)) _established[v] = a;
            }
        }
    }

    private void AuthorityStats()
    {
        Stats.SecretOrgs = _groups.Count(g => g.Alive && IsTop(g) && g.Visibility <= Visibility.Covert);
        Stats.PublicOrgs = _groups.Count(g => g.Alive && IsTop(g) && g.Visibility >= Visibility.Public);
        Stats.Companies = _groups.Count(g => g.Alive && IsTop(g) && g.Kind == GroupKind.Company);
        Stats.SellswordBands = _groups.Count(g => g.Alive && IsTop(g) && g.Kind == GroupKind.Sellswords);
        Stats.Brothels = _groups.Count(g => g.Alive && IsTop(g) && g.Kind == GroupKind.Brothel);
        var vl = VillageList;
        float s = 0f; int n = 0;
        for (int v = 0; v < vl.Count; v++) if (vl.Alive[v]) { s += Established(v); n++; }
        Stats.EstablishedMean = n > 0 ? s / n : 0f;
    }

    /// <summary>바깥사람에게 보이는 권위의 몫(공개 수준, 새어 나간 정도, 거리).</summary>
    private static float OutsideFactor(Group g, float dist) => g.Visibility switch
    {
        Visibility.Hidden => 0.5f * g.Leak,
        Visibility.Covert => 0.5f * g.Leak,
        Visibility.Neutral => g.Leak,
        Visibility.Public => 1f / (1f + dist / 200f),
        _ => 1.3f / (1f + dist / 400f),
    };

    private readonly HashSet<int> _hasKids = new();
    private int _hasKidsDay = -1, _hasKidsCount = -1;

    /// <summary>갈래가 있는 조직인가(하루에 한 번, 또는 조직 수가 바뀌면 다시 센다).</summary>
    private bool HasChildren(Group g)
    {
        if (_hasKidsDay != Day || _hasKidsCount != _groups.Count)
        {
            _hasKids.Clear();
            foreach (var c in _groups) if (c.Alive && c.Parent >= 0) _hasKids.Add(c.Parent);
            _hasKidsDay = Day; _hasKidsCount = _groups.Count;
        }
        return _hasKids.Contains(g.Id);
    }

    /// <summary>
    /// 플레이어 집이 i 의 조직 g 소속을 아는가. 공개 이상은 누구나, 중립은 새어 나간 만큼(0.3),
    /// 숨긴 조직은 정체가 드러났거나 플레이어 식구가 같은 조직(완전 은폐는 같은 갈래)일 때만.
    /// 플레이어 집이 없으면(지켜보기) 다 보인다.
    /// </summary>
    public bool PlayerSeesMembership(int i, Group g)
    {
        int ph = PlayerHousehold;
        if (ph < 0 || !Households.IsAlive(ph) || People.Household[i] == ph) return true;
        var root = RootOf(g);
        if (root.Visibility >= Visibility.Public || root.Exposed) return true;
        if (root.Visibility == Visibility.Neutral && root.Leak >= 0.3f) return true;
        foreach (int m in MembersOf(ph))
        {
            if (InGroup(m, g)) return true;
            if (root.Visibility != Visibility.Hidden && RoleIn(m, root) != OrgRole.None) return true;
        }
        return false;
    }

    /// <summary>i 가 아는 권위가 끄는 힘(0~1). 30쯤이면 0.5. 들기, 포섭, 신고가 이것을 본다.</summary>
    public float AuthorityPull(int i, Group g)
    {
        float a = PerceivedAuthority(i, g);
        return a / (a + 30f);
    }

    /// <summary>사람 i 가 아는 조직 g(맨 위)의 권위. 저장하지 않고 자리와 공개 수준으로 계산한다. 사람마다 ±20%는 해시로 흔든다.</summary>
    public float PerceivedAuthority(int i, Group g)
    {
        var root = RootOf(g);
        var role = RoleIn(i, root);
        float f;
        if (role == OrgRole.Leader) f = 1f;
        else if (role != OrgRole.None)
        {
            // 완전 은폐: 자기 갈래만 안다
            if (root.Visibility == Visibility.Hidden)
            {
                var unit = GroupsOf(i).FirstOrDefault(x => RootOf(x).Id == root.Id);
                f = unit != null && root.Total > 0 ? (float)unit.Total / root.Total : 0.2f;
            }
            else f = 1f;
        }
        else
        {
            int leader = FindAlive(root.LeaderUid);
            int home = root.Home >= 0 ? root.Home : leader >= 0 ? People.Household[leader] : -1;
            float d = Households.IsAlive(home) && Households.IsAlive(People.Household[i]) ? MathF.Sqrt(Dist2(home, People.Household[i])) : 300f;
            f = OutsideFactor(root, d);
        }
        uint hsh = (uint)(People.Uid[i] * 73856093) ^ (uint)(root.Id * 19349663);
        hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
        float noise = 1f + 0.2f * ((hsh % 2001) / 1000f - 1f);
        return root.Authority * f * noise;
    }

    // ------------------------------------------------------------------ 새어 나감, 포섭, 국민

    /// <summary>
    /// 해마다: 숨긴 조직의 비밀이 새어 나가고, 숨긴 조직은 조직원이 직접 사람을 끌어들이고,
    /// 완전 공개 조직은 조직원의 자식이 열여섯이 되면 저절로 소속원이 된다.
    /// </summary>
    private void VisibilityYear()
    {
        var p = People; var hh = Households;
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive || !IsTop(g)) continue;
            if (g.Visibility <= Visibility.Neutral)
            {
                var ms = MembersFast(g);
                int low = 0, dual = 0;
                foreach (int m in ms)
                {
                    if (Social.Opinion(p, m, g.LeaderUid) < 0f) low++;
                    if (GroupsOf(m).Count(x => RootOf(x).Id != g.Id) > 0) dual++;
                }
                float n = MathF.Max(1, ms.Count);
                float leak = 0.01f * MathF.Sqrt(g.Total) + 0.1f * low / n + 0.05f * dual / n + (g.Visibility == Visibility.Neutral ? 0.05f : 0f);
                if (g.Visibility == Visibility.Hidden) leak *= 0.5f;
                g.Leak += leak;
                if (g.Leak >= 1f && g.Visibility < Visibility.Public)
                {
                    g.Visibility++;
                    g.Leak = 0.3f;
                    Stats.OrgLeaks++;
                    if (FindAlive(g.LeaderUid) is int ld && ld >= 0) Chron(ld, $"{g.Name}의 비밀이 새어 나가 이제 {Subj($"'{VisibilityName(g.Visibility)}'")} 되었다");
                }
            }
            if (g.Visibility <= Visibility.Covert && g.Home < 0) SecretRecruit(g);
        }
        // 완전 공개 조직: 조직원 자식이 열여섯이 되면 소속원
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || AgeYears(i) != 16 || GroupsOf(i).Count() >= GroupSlots) continue;
            foreach (int pu in new[] { Lineage.Father(p.Uid[i]), Lineage.Mother(p.Uid[i]) })
            {
                int par = pu >= 0 ? FindAlive(pu) : -1;
                if (par < 0) continue;
                var g = GroupsOf(par).Select(RootOf).FirstOrDefault(r => r.Visibility == Visibility.Proclaimed && r.Home < 0);
                if (g == null || InGroup(i, g) || GroupOfKind(i, g.Kind) != null || !RuleAllows(g, i)) continue;
                if (JoinGroup(i, g)) { Stats.BornMembers++; break; }
            }
        }
    }

    /// <summary>숨긴 조직은 사람이 저절로 모이지 않는다. 우두머리와 측근이 아는 사람을 직접 끌어들인다.</summary>
    private void SecretRecruit(Group g)
    {
        var p = People;
        var recruiters = new List<int>();
        int leader = FindAlive(g.LeaderUid);
        if (leader >= 0) recruiters.Add(leader);
        foreach (int u in g.Inner) if (FindAlive(u) is int x && x >= 0) recruiters.Add(x);
        if (recruiters.Count == 0) recruiters.AddRange(MembersFast(g).Take(2));
        foreach (int r in recruiters.Take(4))
        {
            if (!Adult(r)) continue;
            // 아는 사람이나, 같은 마을 사람을 직접 찾아가 설득한다
            int t = -1;
            int sl = r * PersonStore.RelSlots + _rng.NextInt(PersonStore.RelSlots);
            if (_rng.NextBool() && p.RelUid[sl] >= 0 && p.RelAff[sl] >= 10f) t = FindAlive(p.RelUid[sl]);
            else if (Households.IsAlive(p.Household[r]) && Households.Village[p.Household[r]] >= 0) t = RandomVillager(Households.Village[p.Household[r]]);
            if (t < 0 || !Adult(t) || IsPlayer(t) || InGroup(t, g) || GroupsOf(t).Count() >= GroupSlots || GroupOfKind(t, g.Kind) != null || !RuleAllows(g, t)) continue;
            float a = GroupWant(t, g.Kind) + Social.Opinion(p, t, p.Uid[r]) / 100f - 0.1f + (IsIdleScholar(t) ? 0.2f : 0f)
                    + 0.2f * (AuthorityPull(t, g) - 0.5f);   // 센 줄 아는 조직에는 잘 넘어온다
            if (_rng.Chance(Math.Clamp(a, 0.02f, 0.9f)) && JoinGroup(t, g)) Stats.SecretRecruits++;
        }
    }

    /// <summary>스스로 찾아와 드는 사람에게 곱하는 값. 숨긴 조직은 0, 일부 은폐는 조직원 가족만.</summary>
    private float JoinFactor(Group g, int i)
    {
        switch (g.Visibility)
        {
            case Visibility.Hidden: return 0f;
            case Visibility.Covert:
                // 조직원 가족의 소개로만 든다
                foreach (int m in MembersOf(People.Household[i])) if (m != i && InGroup(m, g)) return 0.5f;
                return 0f;
            case Visibility.Neutral: return 0.4f;
            case Visibility.Public: return 1f;
            default: return 1.3f;
        }
    }

    /// <summary>조직원이 범죄로 잡히면 그가 든 숨긴 조직의 비밀이 새어 나간다(심문).</summary>
    private void Interrogated(int culprit)
    {
        foreach (var g in GroupsOf(culprit).Select(RootOf).Distinct().ToList())
            if (g.Visibility <= Visibility.Neutral) g.Leak += 0.2f;
    }

    /// <summary>플레이어가 조직의 알려진 정도를 고른다. 숨기는 쪽으로 가면 새어 나간 정도는 그대로 남는다(이미 아는 사람의 기억).</summary>
    public void SetVisibility(Group g, Visibility v)
    {
        var root = RootOf(g);
        if (IsAttached(root) && v < Visibility.Public) v = Visibility.Public;   // 붙은 조직은 숨지 못한다
        if (v < root.Visibility) root.Leak = MathF.Max(root.Leak, 0.5f);
        root.Visibility = v;
    }
}
