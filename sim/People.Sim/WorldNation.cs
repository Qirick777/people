namespace People.Sim;

/// <summary>나라 안 유곽에 대한 법.</summary>
public enum BrothelLaw : byte { Free, Licensed, Banned }

/// <summary>나라끼리의 약속.</summary>
public enum PactKind : byte { NonAggression, Alliance }

/// <summary>선포한 나라 하나. 맨 위 홀로 선 지배자 집의 지위다.</summary>
public sealed class Nation
{
    public int House, HouseFounded, Capital = -1, ReligionId = -1, Since, Short;
    public string Name = "";
    public BrothelLaw BrothelLaw;
}

/// <summary>
/// 세력 개편 R14(plan_reform): 건국.
/// - 요건: 홀로 섬, 마을 3곳 이상에서 과반이 내 나무의 집, 아래 집 100, 군인이 가장 센 이웃(홀로 선 지배자)의 절반 이상,
///   걷는 사람(징수관이나 걷기 갈래)과 곳간지기, 수도에 성(큰 집 3단계), 인지도 0.5·평판 0 이상.
/// - 선포: 국호, 수도(지배자 집 마을), 국교(고르지 않을 수 있음). 지배자 조직이 흔들린다(+0.3).
///   이웃 홀로 선 지배자는 0.5 + 0.3 × 보는 눈 ÷ 100 − 0.3 × 공격성 확률로 승인하고, 아니면 다섯 해 적대한다(적대 세력).
///   국교와 같은 종교 교단의 우두머리는 나라 주인을 보는 눈 +10, 나라는 이름 +0.05·평판 +0.02.
/// - 열리는 것: 작위(바로 아래 봉신에게. 충성 +0.1, 보는 눈 +20, 내려받은 정당성), 유곽 법(자유·허가·금지),
///   국교(같은 종교 가장은 해마다 충성 +0.02), 불가침·동맹(불가침이면 서로 전쟁하지 않고, 동맹이면 전쟁 힘에 동맹 힘의 0.3을 더한다).
///   허가: 나라 안 유곽은 주인 몫의 20%를 나라 집에 낸다. 금지: 나라 안 마을에서 유곽이 영업하지 못한다.
/// - 영역(마을 3곳)이나 군인 요건이 세 해 잇달아 모자라면 나라 지위를 잃는다.
/// - NPC: 요건을 채운 홀로 선 지배자는 가장의 야망이 0.4 넘으면 해마다 30%로 선포한다. 국교는 가장의 종교다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, Nation> _nations = new();
    private readonly HashSet<(int a, int b, PactKind k)> _pacts = new();
    private readonly HashSet<int> _titled = new();

    public IEnumerable<Nation> Nations => _nations.Values.Where(NationLive);
    private bool NationLive(Nation n) => Households.IsAlive(n.House) && Households.FoundedDay[n.House] == n.HouseFounded;
    public Nation? NationOf(int h) => h >= 0 && _nations.TryGetValue(h, out var n) && NationLive(n) ? n : null;
    public bool IsTitled(int h) => _titled.Contains(h);
    public static string BrothelLawName(BrothelLaw l) => l switch { BrothelLaw.Licensed => "허가", BrothelLaw.Banned => "금지", _ => "자유" };

    /// <summary>집 h 가 다스리는 나라(그 집이나 그 집이 섬기는 나무 위의 나라).</summary>
    public Nation? NationAt(int h)
    {
        for (int k = 0, x = h; k < 12 && x >= 0; k++, x = PatronOf(x))
            if (NationOf(x) is { } n) return n;
        return null;
    }

    private Nation? NationOfVillage(int v)
    {
        if (_nations.Count == 0 || v < 0) return null;
        var hs = VillageHouses(v);
        foreach (int h in hs) if (NationAt(h) is { } n) return n;
        return null;
    }

    /// <summary>마을 v 의 유곽 법.</summary>
    private BrothelLaw BrothelLawAt(int v) => NationOfVillage(v)?.BrothelLaw ?? BrothelLaw.Free;

    private bool InRealm(int x, int h) => x == h || InTreeOf(x, h);

    /// <summary>이웃: 보호 거리 네 배 안의 다른 홀로 선 지배자.</summary>
    private List<int> NeighborLords(int h)
    {
        var hh = Households;
        float r = 4f * Config.ProtectRange;
        var list = new List<int>();
        foreach (var (l, _) in _clientsOf)
            if (l != h && hh.IsAlive(l) && PatronOf(l) < 0 && TierOf(l) >= 1 && !InRealm(l, h) && Dist2(l, h) <= r * r) list.Add(l);
        return list;
    }

    private int RealmVillages(int h)
    {
        var vl = VillageList;
        int n = 0;
        for (int v = 0; v < vl.Count; v++)
        {
            if (!vl.Alive[v]) continue;
            var hs = VillageHouses(v);
            if (hs.Count >= 3 && hs.Count(x => InRealm(x, h)) * 2 > hs.Count) n++;
        }
        return n;
    }

    private (int mine, int best) SoldierBalance(int h)
    {
        int mine = RetainerCount(h, Job.Soldier), best = 0;
        foreach (int l in NeighborLords(h)) best = Math.Max(best, RetainerCount(l, Job.Soldier));
        return (mine, best);
    }

    /// <summary>건국 요건: 이름, 채웠나, 지금 값.</summary>
    public List<(string name, bool ok, string now)> NationReqs(int h)
    {
        var list = new List<(string, bool, string)>();
        if (!Households.IsAlive(h)) return list;
        int vill = RealmVillages(h), below = BelowOf(h);
        var (mine, best) = SoldierBalance(h);
        var org = LordOrg(h, false);
        bool collect = RetainerCount(h, Job.Collector) > 0 || OrgCollectors(h).Count > 0;
        bool treas = org != null && org.TreasurerUid >= 0 && FindAlive(org.TreasurerUid) >= 0;
        var (fame, standing, _) = RepOfLord(h);
        list.Add(("홀로 섬", PatronOf(h) < 0, PatronOf(h) < 0 ? "홀로 섰다" : "주군이 있다"));
        list.Add(("마을 3곳에서 과반", vill >= 3, $"{vill}곳"));
        list.Add(("아래 집 100", below >= 100, $"{below}집"));
        list.Add(("군인이 가장 센 이웃의 절반", mine * 2 >= best && mine > 0, $"{mine}명(이웃 최대 {best})"));
        list.Add(("걷는 사람과 곳간지기", collect && treas, $"{(collect ? "걷는 사람 있음" : "걷는 사람 없음")}, {(treas ? "곳간지기 있음" : "곳간지기 없음")}"));
        list.Add(("수도에 성", ResidenceOf(h) >= 3, $"큰 집 {ResidenceOf(h)}단계"));
        list.Add(("인지도 0.5, 평판 0", fame >= 0.5f && standing >= 0f, $"인지도 {fame:0.00}, 평판 {standing:0.00}"));
        return list;
    }

    public bool CanFoundNation(int h) => NationOf(h) == null && NationReqs(h).All(r => r.ok);

    /// <summary>나라를 선포한다. religionId 가 -1이면 국교를 두지 않는다.</summary>
    public Nation? FoundNation(int h, string name, int religionId)
    {
        var p = People; var hh = Households;
        if (!CanFoundNation(h)) return null;
        int head = HeadIdx(h);
        if (head < 0) return null;
        if (string.IsNullOrWhiteSpace(name)) name = $"{Names.Family(Lineage.FamilyOf(p.Uid[head]))} 왕국";
        var n = new Nation { House = h, HouseFounded = hh.FoundedDay[h], Name = name, Capital = hh.Village[h], ReligionId = religionId, Since = Day };
        _nations[h] = n;
        Stats.NationsFounded++;
        if (LordOrg(h, false) is { } org) org.Shaken = MathF.Min(1f, org.Shaken + 0.3f);
        Chron(head, $"{SubjName(head)} {Obj(name)} 세웠다");
        // 이웃은 승인하거나 적대한다
        foreach (int l in NeighborLords(h))
        {
            int lh = HeadIdx(l);
            if (lh < 0) continue;
            float yes = 0.5f + 0.3f * Social.Opinion(p, lh, p.Uid[head]) / 100f - 0.3f * p.Get(lh, Trait.Aggression);
            if (_rng.Chance(Math.Clamp(yes, 0.05f, 0.95f))) { Stats.NationsRecognized++; continue; }
            MarkWar(l, h);
            Stats.NationsOpposed++;
            Chron(head, $"{Subj(RealmName(l) is { Length: > 0 } rn ? rn : Names.Full(Lineage, p.Uid[lh]))} {Obj(name)} 인정하지 않았다");
        }
        // 같은 종교 교단은 지지한다
        if (religionId >= 0)
            foreach (var g in _groups)
                if (g.Alive && IsTop(g) && g.ReligionId == religionId && g.LeaderUid >= 0 && FindAlive(g.LeaderUid) is int gl && gl >= 0)
                {
                    Social.Add(p, gl, p.Uid[head], 10f);
                    AddRepLord(h, 0.05f, 0.02f, 0f);
                }
        return n;
    }

    public void SetBrothelLaw(int h, BrothelLaw law) { if (NationOf(h) is { } n) n.BrothelLaw = law; }
    public void SetStateReligion(int h, int religionId) { if (NationOf(h) is { } n) n.ReligionId = religionId; }

    public bool HasPact(int a, int b, PactKind? k = null)
    {
        foreach (var x in _pacts)
            if (((x.a == a && x.b == b) || (x.a == b && x.b == a)) && (k == null || x.k == k)) return true;
        return false;
    }

    /// <summary>전쟁 힘에 더할 동맹의 힘(0.3배).</summary>
    private float PactAllyPower(int h)
    {
        float s = 0f;
        foreach (var x in _pacts)
        {
            if (x.k != PactKind.Alliance) continue;
            int o = x.a == h ? x.b : x.b == h ? x.a : -1;
            if (o >= 0 && Households.IsAlive(o)) s += 0.3f * RealmPower(o);
        }
        return s;
    }

    /// <summary>해마다: 나라 지위 지키기, 국교, NPC 건국.</summary>
    private void NationYear()
    {
        var p = People; var hh = Households;
        foreach (var n in _nations.Values.ToList())
        {
            if (!NationLive(n)) { _nations.Remove(n.House); continue; }
            var (mine, best) = SoldierBalance(n.House);
            bool short_ = RealmVillages(n.House) < 3 || mine * 2 < best || PatronOf(n.House) >= 0;
            n.Short = short_ ? n.Short + 1 : 0;
            if (short_) Stats.NationShortVill += RealmVillages(n.House) < 3 ? 1 : 0;
            if (n.Short >= 3)
            {
                _nations.Remove(n.House);
                Stats.NationsLost++;
                if (HeadIdx(n.House) is int h0 && h0 >= 0) Chron(h0, $"{n.Name}의 나라 지위를 잃었다(영역이나 무력이 세 해 모자랐다)");
                continue;
            }
            if (n.ReligionId >= 0)
            {
                int head = HeadIdx(n.House);
                foreach (int h in DirectClients(n.House))
                    if (hh.IsAlive(h) && HeadIdx(h) is int ch && ch >= 0 && head >= 0 && p.LoyalUid[ch] == p.Uid[head] && ReligionOf(ch)?.Id == n.ReligionId)
                        p.Loyalty[ch] = MathF.Min(1f, p.Loyalty[ch] + 0.02f);
            }
        }
        _pacts.RemoveWhere(x => !hh.IsAlive(x.a) || !hh.IsAlive(x.b));
        _titled.RemoveWhere(h => !hh.IsAlive(h));
        // NPC 건국
        if (Config.NpcNations > 0)
        foreach (var (l, _) in _clientsOf.ToList())
        {
            if (!hh.IsAlive(l) || PatronOf(l) >= 0 || TierOf(l) < 2 || NationOf(l) != null || l == PlayerHousehold) continue;
            int head = HeadIdx(l);
            if (head < 0 || p.Get(head, Trait.Ambition) <= 0.4f || !_rng.Chance(0.3f) || !CanFoundNation(l)) continue;
            FoundNation(l, "", ReligionOf(head)?.Id ?? -1);
        }
        Stats.Nations = Nations.Count();
    }

    private void AddNationDefs(InteractionDef[] d)
    {
        d[(int)InteractionId.GrantTitle] = new InteractionDef
        {
            // 나라 주인(Actor) → 바로 아래 봉신 가장(Target)
            Id = InteractionId.GrantTitle, Name = "작위 내리기", Category = InteractionCategory.Rule,
            Can = (w, c) =>
            {
                int a = w.People.Household[c.Actor], t = w.People.Household[c.Target];
                return w.HeadIdx(a) == c.Actor && w.HeadIdx(t) == c.Target && w.NationOf(a) != null && w.PatronOf(t) == a && w.ClientCount(t) > 0 && !w._titled.Contains(t);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                int t = p.Household[c.Target];
                w._titled.Add(t);
                if (p.LoyalUid[c.Target] == p.Uid[c.Actor]) p.Loyalty[c.Target] = MathF.Min(1f, p.Loyalty[c.Target] + 0.1f);
                Social.Add(p, c.Target, p.Uid[c.Actor], 20f);
                w.EnsureRealmArrays();
                w._legit[t] = (byte)Legitimacy.Granted;
                w.Stats.TitlesGranted++;
                w.Chron(c.Actor, $"{w.SubjName(c.Actor)} {Names.Full(w.Lineage, p.Uid[c.Target])}에게 작위를 내렸다");
            },
        };
        d[(int)InteractionId.NationPact] = new InteractionDef
        {
            // 나라 주인(Actor) → 다른 홀로 선 지배자 가장(Target). 값 0 불가침, 1 동맹
            Id = InteractionId.NationPact, Name = "약조 맺기", Category = InteractionCategory.Rule,
            Amounts = new[] { 0f, 1f }, AmountNames = new[] { "불가침", "동맹" },
            Can = (w, c) =>
            {
                int a = w.People.Household[c.Actor], t = w.People.Household[c.Target];
                return a != t && w.HeadIdx(a) == c.Actor && w.HeadIdx(t) == c.Target && w.NationOf(a) != null && w.PatronOf(t) < 0 && w.ClientCount(t) > 0
                       && !w.HasPact(a, t, c.Amount >= 1f ? PactKind.Alliance : PactKind.NonAggression);
            },
            Accept = (w, c) => Math.Clamp(0.3f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 100f + (c.Amount >= 1f ? 0f : 0.2f)
                                          - 0.2f * w.People.Get(c.Target, Trait.Aggression), 0.05f, 0.95f),
            Success = (w, c, _) =>
            {
                int a = w.People.Household[c.Actor], t = w.People.Household[c.Target];
                var k = c.Amount >= 1f ? PactKind.Alliance : PactKind.NonAggression;
                w._pacts.Add((a, t, k));
                w.Stats.Pacts++;
                w.Chron(c.Actor, $"{w.SubjName(c.Actor)} {Names.Full(w.Lineage, w.People.Uid[c.Target])}와 {(k == PactKind.Alliance ? "동맹" : "불가침")}을 맺었다");
            },
        };
    }

    // 시험용
    public void RecountNations() => NationYear();
    public void AddPactNow(int a, int b, PactKind k) => _pacts.Add((a, b, k));
    public bool CanWarNow(int a, int b) => CanWar(a, b);
    public Nation ForceNation(int h, string name) { var n = new Nation { House = h, HouseFounded = Households.FoundedDay[h], Name = name, Capital = Households.Village[h], Since = Day }; _nations[h] = n; return n; }
}
