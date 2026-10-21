namespace People.Sim;

/// <summary>권장 헌금: 형편껏(넉넉한 집만 조금), 십일조(소출의 1/10), 다섯에 하나(소출의 1/5). 내는 것은 신도 마음이다.</summary>
public enum TitheRule : byte { Free, Tenth, Fifth }

/// <summary>
/// 세력 틀 8단계(plan_faction 13장 8) + 개편 R3: 믿음, 광신도, 헌금(자발, 권장액), 바치기.
/// - 믿음: 설교하는 세력(신앙 세력)의 신도는 그 우두머리에게 믿음(0~1)을 쌓는다. 우두머리 말솜씨, 신앙, 우두머리를 보는 눈, 예배당이 키우고, 무거운 헌금이 깎는다.
///   믿는 만큼 해마다 우두머리를 좋게 본다.
///   집안 충성(LoyalUid)과 따로 센다. 믿음이 0.9를 넘고 우두머리를 50 넘게 보며 다섯 해 넘게 신도였으면 광신도가 된다.
/// - 광신도: 떠나지 않고, 파벌에 끼지 않고, 넉넉하지 않아도 헌금을 낸다. 설교에 힘을 보탠다. 굶주림과 무거운 헌금은 의심으로 쌓이고, 넘치면 광신이 깨진다.
/// - 헌금: 우두머리는 권장액(형편껏, 1/10, 1/5)만 정한다. 신도는 믿음·충성·교리로 스스로 낸다(R3).
/// - 바치기 요구: 세 해에 한 번 신도 집에 특별 헌납을 청한다. 광신도는 재산의 0.3, 믿음 깊은 신도는 0.1(수확까지 먹을 것은 남긴다), 나머지는 조금 내고 믿음이 깎인다.
/// - NPC 우두머리는 광신도가 많아지면 회칙을 올리고, 곳간이 비면 바치기를 청한다.
/// </summary>
public sealed partial class World
{
    private sealed class Devote { public int Root, Since; public float Faith, Doubt; public bool Zealot; }

    private readonly Dictionary<int, Devote> _devote = new();         // uid → 믿음
    private readonly Dictionary<int, float> _tithePaid = new();       // uid → 올해 낸 헌금(식량 값으로 친 것)

    /// <summary>교단인가: 종교에 속한 맨 위 조직(교단이거나 예배·설교를 맡은 갈래가 있으면 해마다 종교가 정해진다).</summary>
    public bool IsFaith(Group root) { root = RootOf(root); return root.ReligionId >= 0 || WantsFaith(root); }

    /// <summary>종교를 가질 조직인가.</summary>
    private bool WantsFaith(Group root) => root.Kind == GroupKind.Brotherhood || Subtree(root).Any(u => u.Tasks.Any(t => t.Verb is Verb.Preach or Verb.Worship));

    public float FaithOf(int uid, Group root) => _devote.TryGetValue(uid, out var d) && d.Root == RootOf(root).Id ? d.Faith : 0f;
    public bool IsZealot(int uid, Group root) => _devote.TryGetValue(uid, out var d) && d.Root == RootOf(root).Id && d.Zealot;
    private bool ZealotOfAny(int uid) => _devote.TryGetValue(uid, out var d) && d.Zealot;

    /// <summary>i 의 믿음 기록(없으면 root 쪽으로 새로 만든다. 다른 세력을 믿고 있으면 null).</summary>
    private Devote? DevoteOf(int i, Group root, bool create)
    {
        int uid = People.Uid[i];
        if (_devote.TryGetValue(uid, out var d))
        {
            if (d.Root == root.Id) return d;
            // 아직 그 세력에 있으면 그쪽 믿음이 먼저다
            if (GroupById(d.Root) is { } old && GroupsOf(i).Any(g => RootOf(g).Id == old.Id)) return null;
        }
        if (!create) return null;
        d = new Devote { Root = root.Id, Since = Day, Faith = 0.2f + 0.3f * People.Piety[i] };
        _devote[uid] = d;
        return d;
    }

    /// <summary>
    /// 헌금 하나(자발): 권장액을 낼 확률 = 0.1 + 0.5 × 믿음 + 0.2 × 조직 충성 + 0.2 × 봉헌 + 0.1 × 번영 − 권장액 부담(1/10이면 0.05, 1/5이면 0.15).
    /// 예배가 닿지 않는 신도는 확률이 0.3배. 광신도는 권장액을 낸다. 내지 않아도 벌은 없다. 곡식으로 내고, 수확까지 먹을 것은 남긴다. 낸 양.
    /// </summary>
    private float CollectTithe(Group root, int m, bool covered = true)
    {
        var p = People; var hh = Households;
        int h = p.Household[m];
        if (!hh.IsAlive(h) || p.Uid[m] == root.LeaderUid) return 0f;
        var d = DevoteOf(m, root, true);
        bool zealot = d?.Zealot ?? false;
        float faith = d?.Faith ?? 0f;
        var inc = IncomeOf(h);
        float income = MathF.Max(5f, inc.own + inc.tenant);
        float want, burden;
        switch (root.Tithe)
        {
            case TitheRule.Tenth: want = 0.1f * income; burden = 0.05f; break;
            case TitheRule.Fifth: want = 0.2f * income; burden = 0.15f; break;
            default:
                if (Surplus(h) < 1f && !zealot) return 0f;
                want = MathF.Min(10f, 0.02f * hh.Food[h]); burden = 0f; break;
        }
        // 청빈: 권장액 상한(강 1/20, 중 1/10, 약 1/5)
        float pov = DoctrineOn(m, Doctrine.Poverty);
        if (pov > 0f) want = MathF.Min(want, income * (pov >= 1.5f ? 0.05f : pov >= 1f ? 0.1f : 0.2f));
        if (!zealot)
        {
            if (Surplus(h) < 0.5f) return 0f;
            float pr = 0.1f + 0.5f * faith + 0.2f * OrgLoyalty(root, p.Uid[m]) + 0.2f * DoctrineOn(m, Doctrine.Offering) + 0.1f * DoctrineOn(m, Doctrine.Prosperity) - burden;
            if (!covered) pr *= 0.3f;
            if (!_rng.Chance(pr)) { Stats.TitheRefused++; return 0f; }
        }
        float paid = MathF.Min(want, MathF.Max(0f, hh.Food[h] - hh.Need[h] * DaysToHarvest()));
        if (paid <= 0f) return 0f;
        hh.Food[h] -= paid;
        PayDues(h, paid);
        _tithePaid[p.Uid[m]] = _tithePaid.GetValueOrDefault(p.Uid[m]) + paid;
        Stats.TithePaid += paid;
        if (d != null && !d.Zealot) d.Faith = MathF.Min(1f, d.Faith + 0.01f);   // 내 것을 바쳤다
        return paid;
    }

    /// <summary>
    /// 해마다(가르침 뒤): 교단마다 예배가 닿는 신도를 세고, 믿음이 자라거나 식고, 광신이 생기거나 깨진다. 헌금을 걷는다(자발).
    /// - 예배: 사제(신앙심 0.6 넘는 예배 갈래 어른, 우두머리 포함) 한 명이 지휘 범위 × 예배 비중만큼 돌본다. 닿지 않는 신도는 해마다 −0.02.
    /// - 자람 = 0.015 × (0.5 + 우두머리 말솜씨) + 0.02 × 신앙심 + 예배당 0.015 + 보는 눈 ÷ 3000 − 무거운 헌금 − 굶주림
    ///   + 교리(금욕 0.01, 배타 0.01, 종말 0.02, 관용 −0.01, 강도당) × 개인숭배면 (1 + 0.5 × 강도).
    /// - 광신 문턱: 믿음 0.9 − 0.1 × 개인숭배 − 0.05 × 배타, 보는 눈 50, 다섯 해.
    /// - 광신도 의심: 무거운 헌금, 굶주림 0.3, 우두머리를 미워함, 예배가 닿지 않음 0.1, 세력 평판 −0.3 아래 0.1, 종말 0.03 × 강도.
    /// - 개인숭배 교단의 우두머리가 바뀌면 신도 믿음 −0.3 × 강도, 광신도 의심 +0.5 × 강도.
    /// </summary>
    private void DevotionYear()
    {
        var p = People; var hh = Households;
        int zealots = 0, believers = 0; float faithSum = 0f;
        var seen = new HashSet<int>();
        foreach (var root in _groups.ToList())
        {
            if (!root.Alive || !IsTop(root)) continue;
            if (root.ReligionId < 0 && WantsFaith(root)) AssignReligion(root);
            if (root.ReligionId < 0) continue;
            int leader = FindAlive(root.LeaderUid);
            if (leader < 0) continue;
            float cha = p.Get(leader, Trait.Charisma);
            var units = UnitsOf(root);
            // 우두머리가 바뀌었다(개인숭배는 무너진다)
            bool newHead = root.FaithLeaderUid >= 0 && root.FaithLeaderUid != root.LeaderUid;
            root.FaithLeaderUid = root.LeaderUid;
            // 예배가 닿는 사람 수
            float cap = 0f;
            foreach (var u in units)
            {
                float ww = u.Tasks.Where(t => t.Verb == Verb.Worship).Sum(t => t.Weight);
                if (ww <= 0f) continue;
                foreach (int m in MembersFast(u)) if (Adult(m) && p.Piety[m] >= 0.6f) cap += CommandSpan(m) * ww * u.CmdFactor;
            }
            if (p.Piety[leader] >= 0.6f && units.Any(u => u.Tasks.Any(t => t.Verb == Verb.Worship))) cap += CommandSpan(leader);
            int rootZ = 0, rootN = 0;
            float tithe = 0f;
            var hungryBelievers = new List<int>();
            var paidHouses = new HashSet<int>();
            foreach (var u in units.OrderByDescending(x => x.Tasks.Any(t => t.Verb == Verb.Worship)))
                foreach (int m in MembersFast(u).ToList())
                {
                    if (m == leader || !Adult(m) || !seen.Add(p.Uid[m])) continue;
                    var d = DevoteOf(m, root, true);
                    if (d == null) continue;
                    bool covered = cap >= 1f;
                    if (covered) cap -= 1f;
                    int h = p.Household[m];
                    var inc = IncomeOf(h);
                    float income = MathF.Max(5f, inc.own + inc.tenant);
                    float burden = _tithePaid.GetValueOrDefault(p.Uid[m]) / income;
                    bool hungry = hh.IsAlive(h) && Surplus(h) < 0.3f;
                    if (hungry) hungryBelievers.Add(m);
                    float cult = DoctrineOn(m, Doctrine.Cult), excl = DoctrineOn(m, Doctrine.Exclusive);
                    if (newHead && cult > 0f) { if (d.Zealot) d.Doubt += 0.5f * cult; else d.Faith = MathF.Max(0f, d.Faith - 0.3f * cult); }
                    if (d.Zealot)
                    {
                        d.Doubt = 0.8f * d.Doubt + (burden > 0.3f ? 0.15f : burden > 0.15f ? 0.05f : 0f) + (hungry ? 0.3f : 0f)
                                  + MathF.Max(0f, -Social.Opinion(p, m, root.LeaderUid) / 200f) + (covered ? 0f : 0.1f)
                                  + (RepOfRoot(root).standing < -0.3f ? 0.1f : 0f) + 0.03f * DoctrineOn(m, Doctrine.Doom);
                        if (d.Doubt >= 1f)
                        {
                            d.Zealot = false; d.Doubt = 0f; d.Faith = 0.3f;
                            Social.Add(p, m, root.LeaderUid, -50f);
                            Stats.ZealotsBroken++;
                            if (_rng.Chance(0.5f)) foreach (var g in GroupsOf(m).ToList()) if (RootOf(g).Id == root.Id) LeaveGroup(m, g);
                        }
                    }
                    else if (!covered) d.Faith = MathF.Max(0f, d.Faith - 0.02f);
                    else
                    {
                        bool chapel = FacilityOf(root, FacilityKind.Chapel, hh.IsAlive(h) ? hh.Village[h] : -1) != null;
                        float grow = 0.015f * (0.5f + cha) + 0.02f * p.Piety[m] + (chapel ? 0.015f : 0f) + Social.Opinion(p, m, root.LeaderUid) / 3000f
                                     - 0.3f * MathF.Max(0f, burden - 0.05f) - (hungry ? 0.03f : 0f)
                                     + 0.01f * DoctrineOn(m, Doctrine.Celibacy) + 0.01f * excl + 0.02f * DoctrineOn(m, Doctrine.Doom) - 0.01f * DoctrineOn(m, Doctrine.Tolerance);
                        if (grow > 0f) grow *= 1f + 0.5f * cult;
                        d.Faith = Math.Clamp(d.Faith + grow, 0f, 1f);
                        Social.Add(p, m, root.LeaderUid, 3f * d.Faith);   // 믿는 만큼 우두머리를 좋게 본다
                        if (d.Faith >= 0.9f - 0.1f * cult - 0.05f * excl && Social.Opinion(p, m, root.LeaderUid) >= 50f && Day - d.Since >= 5 * Config.DaysPerYear)
                        {
                            d.Zealot = true; d.Doubt = 0f;
                            Stats.NewZealots++;
                        }
                    }
                    // 헌금(집마다 한 번)
                    if (hh.IsAlive(h) && paidHouses.Add(h)) tithe += CollectTithe(root, m, covered);
                    if (d.Zealot) { zealots++; rootZ++; }
                    believers++; rootN++; faithSum += d.Faith;
                }
            if (FacilityOf(root, FacilityKind.Chapel) != null) tithe *= 1.3f;   // 예배당
            // 청빈: 걷은 것의 절반은 굶는 신도에게
            var rel = _religions[root.ReligionId];
            if (RelStrength(rel, Doctrine.Poverty) > 0f && hungryBelievers.Count > 0 && tithe > 0f)
            {
                float give = 0.5f * tithe; tithe -= give;
                foreach (int m in hungryBelievers) hh.Food[p.Household[m]] += give / hungryBelievers.Count;
                Stats.CharityFood += give;
            }
            Earn(root, tithe);
            // 구휼 교리: 해마다 넉넉한 신도가 굶는 신도를 돕는다
            if (RelStrength(rel, Doctrine.Charity) > 0f)
            {
                var ms = units.SelectMany(MembersFast).Distinct().ToList();
                float before = Stats.CharityFood;
                if (ms.Count >= 2) Charity(ms);
                if (Stats.CharityFood > before) AddRepRoot(root, 0.005f, 0.02f, 0f);
            }
            if (!IsPlayer(leader)) FaithAI(root, leader, rootZ, rootN);
        }
        // 세력을 떠난 사람의 믿음은 지운다
        foreach (var (uid, d) in _devote.ToList())
        {
            int i = FindAlive(uid);
            if (i < 0 || GroupById(d.Root) is not { } r || !GroupsOf(i).Any(g => RootOf(g).Id == r.Id)) _devote.Remove(uid);
        }
        _tithePaid.Clear();
        Stats.Zealots = zealots; Stats.Believers = believers;
        Stats.MeanFaith = believers > 0 ? faithSum / believers : 0f;
    }

    /// <summary>
    /// 교단에 종교를 정한다: 대개 우두머리가 믿는 종교로 입교한다. 말솜씨와 야심이 아주 큰 NPC 우두머리는 드물게(5%) 새 종교를 연다.
    /// </summary>
    private void AssignReligion(Group root)
    {
        EnsureReligions();
        if (_religions.Count == 0) return;
        int leader = FindAlive(root.LeaderUid);
        var p = People;
        if (leader >= 0 && !IsPlayer(leader) && p.Get(leader, Trait.Charisma) > 0.8f && p.Get(leader, Trait.Ambition) > 0.7f && _rng.Chance(0.05f))
        {
            var ds = new List<(Doctrine, Strength)>();
            while (ds.Count < 3)
            {
                var d = (Doctrine)_rng.NextInt((int)Doctrine.Count);
                if (ds.Any(x => x.Item1 == d || Opposite(x.Item1) == d)) continue;
                ds.Add((d, (Strength)(1 + _rng.NextInt(3))));
            }
            FoundReligion(root, "", ds);
            return;
        }
        var rel = leader >= 0 ? ReligionOf(leader) : null;
        root.ReligionId = rel?.Id ?? 0;
    }

    /// <summary>NPC 우두머리: 광신도가 많을수록 회칙을 올린다(욕심이 크면 더 빨리). 곳간이 비면 바치기를 청한다.</summary>
    private void FaithAI(Group root, int leader, int zealots, int n)
    {
        var p = People;
        if (n == 0) return;
        float z = zealots / (float)n, greed = p.Get(leader, Trait.Ambition);
        var want = z > 0.45f - 0.15f * greed ? TitheRule.Fifth : z > 0.2f - 0.1f * greed ? TitheRule.Tenth : TitheRule.Free;
        if (want != root.Tithe) { root.Tithe = want; Stats.TitheChanges++; }
        if (root.Treasury < 20f && greed > 0.6f && _rng.Chance(0.2f)) CallOffering(root);
    }

    /// <summary>믿음 한 해 치(시험용).</summary>
    public void RecountDevotion() => DevotionYear();

    /// <summary>신도가 된 날을 years 해 앞당긴다(시험용).</summary>
    public void BackdateFaith(int uid, int years) { if (_devote.TryGetValue(uid, out var d)) d.Since -= years * Config.DaysPerYear; }

    /// <summary>헌금 하나를 걷는다(시험용).</summary>
    public float TitheFrom(Group root, int m) => CollectTithe(RootOf(root), m);

    /// <summary>광신도로 만든다(시험용).</summary>
    public void MakeZealot(int uid, Group root)
    {
        int i = FindAlive(uid);
        if (i < 0 || DevoteOf(i, RootOf(root), true) is not { } d) return;
        d.Faith = 1f; d.Zealot = true;
    }

    /// <summary>회칙을 정한다.</summary>
    public void SetTithe(Group root, TitheRule r) => RootOf(root).Tithe = r;

    public static string TitheName(TitheRule r) => r switch { TitheRule.Tenth => "십일조", TitheRule.Fifth => "다섯에 하나", _ => "형편껏" };

    /// <summary>바치기를 청할 수 있나(세 해에 한 번).</summary>
    public bool CanCallOffering(Group root)
    {
        root = RootOf(root);
        return IsFaith(root) && (root.LastOffering < 0 || Day - root.LastOffering >= 3 * Config.DaysPerYear);
    }

    /// <summary>
    /// 바치기 요구: 신도 집마다 특별 헌납. 광신도는 재산의 0.3(먹을 것까지), 믿음 0.6 넘는 신도는 0.1, 나머지는 0.03을 내고 믿음이 0.1 깎인다.
    /// 광신도도 의심이 조금 쌓인다. 걷은 은화(-1: 청할 수 없다).
    /// </summary>
    public float CallOffering(Group root)
    {
        root = RootOf(root);
        if (!CanCallOffering(root)) return -1f;
        var p = People; var hh = Households;
        root.LastOffering = Day;
        float total = 0f;
        var houses = new HashSet<int>();
        foreach (var u in UnitsOf(root))
            foreach (int m in MembersFast(u).ToList())
            {
                if (p.Uid[m] == root.LeaderUid || !Adult(m)) continue;
                int h = p.Household[m];
                if (!hh.IsAlive(h) || !houses.Add(h)) continue;
                var d = DevoteOf(m, root, true);
                float price = FoodPrice(hh.Village[h]);
                float share = d is { Zealot: true } ? 0.3f : d != null && d.Faith > 0.6f - 0.2f * DoctrineOn(m, Doctrine.Doom) ? 0.1f : 0.03f;   // 종말 교리는 바치기를 잘 받아들이게 한다
                float paid = PayCoin(h, -2, share * MathF.Max(0f, WealthOf(h)) * price, false);
                total += paid;
                PayDues(h, paid / MathF.Max(0.1f, price));
                if (d == null) continue;
                if (d.Zealot) d.Doubt += 0.2f;
                else if (share < 0.1f) d.Faith = MathF.Max(0f, d.Faith - 0.1f);
            }
        Earn(root, total, true);
        Stats.Offerings++; Stats.OfferingTotal += total;
        int leader = FindAlive(root.LeaderUid);
        if (leader >= 0) Chron(leader, $"{Obj(root.Name)} 두고 신도들에게 바치기를 청해 은화 {total:0}을 거두었다");
        return total;
    }

    /// <summary>학교 졸업생: 신앙 세력의 학교에서 교리와 충성을 배운 만큼 우두머리를 믿고 나온다.</summary>
    private void FaithFromSchool(int st, SchoolPlan plan, Group root)
    {
        if (!IsFaith(root)) return;
        float w = plan.Weights[(int)Lesson.Doctrine] + plan.Weights[(int)Lesson.Loyalty];
        if (w <= 0f) return;
        var d = DevoteOf(st, root, true);
        if (d != null && !d.Zealot) d.Faith = MathF.Min(1f, d.Faith + 0.3f * w);
    }
}
