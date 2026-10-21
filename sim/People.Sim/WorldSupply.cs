namespace People.Sim;

public enum FacilityKind : byte { Hall, Workshop, Mill, Inn, Chapel, Warehouse, Barracks, Fort }

/// <summary>시설 하나. 세력(맨 위 조직)이 가지고 갈래에 배정한다. 마을에 선다.</summary>
public sealed class Facility
{
    public int Id;
    public FacilityKind Kind;
    public int Village;
    public int Owner = -1;          // 가진 세력의 맨 위 조직 id(-1: 주인 없음)
    public int Assigned = -1;       // 배정받은 갈래 id
    public float Built;             // 지은 해 수(다 지으면 BuildYears)
    public int Unpaid;              // 유지비를 못 낸 해 수
    public bool Alive = true;
}

/// <summary>
/// 생산과 공급, 시설(plan_faction 4장, 11장).
/// - 생산: 생산 일을 맡은 갈래의 장인(공예·목공 0.2)이 만든다. 한 사람 (4 + 10 × 솜씨) × 무게 × 0.5(제 집 농사와 나눔), 상근이면 두 배.
///   재료(기초 재료)는 세력 곳간에서, 모자라면 장인 집에서 쓴다. 무구는 공방 시설이 배정된 갈래만 만든다.
/// - 공급: 만든 것을 비중대로 보낸다. 갈래(보급: 무구는 그 갈래가 지니고, 다른 것은 조직원 집에), 장사 갈래(납품: 그 마을에 판다),
///   곳간(비축), 시장(직판: 15% 덜 받는다). 파는 양은 마을에서 바라는 집이 살 수 있는 만큼이다.
/// - 무구: 갈래 사람 하나에 하나면 그 갈래 싸움 힘 1.3배(보급률만큼). 다섯 해에 닳는다.
/// - 상근: 상근 갈래 사람은 집 농사를 하지 않고(일손 0) 그 갈래 일과 싸움에 1.6배로 나선다. 세력 곳간이 한 해 식량 24를 대고,
///   모자라면 제 집이 대며, 제 집도 어려우면 갈래를 떠난다.
/// - 시설: 세력이 짓고 갈래에 배정한다. 짓는 데 식량과 자재와 해 수가 들고, 해마다 유지비가 든다. 세 해 못 내면 무너진다.
///   배정받은 갈래가 세력을 떠나면 그 마을에서 더 셀 때 점거해 가져간다. 주인 세력이 사라지면 배정받은 갈래의 세력이 잇는다.
/// </summary>
public sealed partial class World
{
    private readonly List<Facility> _facilities = new();
    public IEnumerable<Facility> Facilities => _facilities.Where(f => f.Alive);
    private readonly HashSet<int> _fullTimeUids = new();

    public static string FacilityName(FacilityKind k) => k switch
    {
        FacilityKind.Hall => "학당", FacilityKind.Workshop => "공방", FacilityKind.Mill => "방앗간", FacilityKind.Inn => "여관",
        FacilityKind.Chapel => "예배당", FacilityKind.Warehouse => "창고", FacilityKind.Barracks => "병영", _ => "요새 집",
    };

    public static string FacilityEffect(FacilityKind k) => k switch
    {
        FacilityKind.Hall => "배정한 학교: 교사 한 명이 4명 더 받고 배우는 속도 +20%",
        FacilityKind.Workshop => "배정한 생산 갈래: 만드는 양 +30%, 무구를 만든다",
        FacilityKind.Mill => "그 마을 집들의 소출 3%를 삯으로 받는다",
        FacilityKind.Inn => "그 마을 교역 이문 +10%",
        FacilityKind.Chapel => "그 마을 설교 헌금 +30%, 교리 배우는 속도 +20%",
        FacilityKind.Warehouse => "세력 곳간의 재화가 닳지 않는다",
        FacilityKind.Barracks => "배정한 갈래: 상근 군량 −20%, 훈련 +20%",
        _ => "그 마을 지킴 힘 1.5배, 숨긴 조직이 새는 몫 −30%",
    };

    /// <summary>짓는 값(식량), 짓는 해 수, 한 해 유지비.</summary>
    public static (float cost, int years, float upkeep) FacilityCost(FacilityKind k) => k switch
    {
        FacilityKind.Hall => (60f, 2, 3f), FacilityKind.Workshop => (80f, 1, 6f), FacilityKind.Mill => (100f, 1, 4f), FacilityKind.Inn => (80f, 1, 6f),
        FacilityKind.Chapel => (60f, 2, 3f), FacilityKind.Warehouse => (50f, 1, 2f), FacilityKind.Barracks => (120f, 2, 10f), _ => (200f, 3, 10f),
    };

    private bool Ready(Facility f) => f.Alive && f.Built >= FacilityCost(f.Kind).years;

    /// <summary>세력 root 가 마을 v 에 가진, 다 지은 그 종류 시설(갈래 배정을 따지면 그 갈래에 배정된 것만).</summary>
    private Facility? FacilityOf(Group root, FacilityKind k, int v = -1, Group? unit = null)
    {
        foreach (var f in _facilities)
            if (Ready(f) && f.Kind == k && f.Owner == root.Id && (v < 0 || f.Village == v) && (unit == null || f.Assigned == unit.Id)) return f;
        return null;
    }

    // ------------------------------------------------------------------ 생산

    /// <summary>만들 줄 아는 사람(공예나 목공 0.2, 또는 장인·목수).</summary>
    private bool IsMaker(int m) => People.JobOf[m] is (byte)Job.Crafter or (byte)Job.Carpenter
                                   || MathF.Max(People.GetSkill(m, Skill.Craft), People.GetSkill(m, Skill.Carpentry)) >= 0.2f;

    private void Produce(Group root, Group g, float w)
    {
        var p = People; var hh = Households;
        var good = g.Product;
        bool shop = FacilityOf(root, FacilityKind.Workshop, -1, g) != null;
        if (good == Good.Arms && !shop) good = Good.Tools;   // 무구는 공방이 있어야
        if (!g.ProductManual && g.Village >= 0)
        {
            // NPC 갈래: 마을에서 값이 가장 잘 쳐지는 것
            float best = 0f;
            foreach (var x in new[] { Good.Tools, Good.Cloth, Good.Fine, Good.Build, Good.Arms })
            {
                if (x == Good.Arms && !shop) continue;
                float r = PriceAt(g.Village, x) / BasePrice[(int)x];
                if (r > best) { best = r; good = x; }
            }
            g.Product = good;
        }
        var makers = MembersFast(g).Where(IsMaker).ToList();
        if (makers.Count == 0) return;
        float cap = 0f;
        foreach (int m in makers)
        {
            float sk = good == Good.Build ? p.GetSkill(m, Skill.Carpentry) : p.GetSkill(m, Skill.Craft);
            cap += (4f + 10f * sk) * 0.5f * (g.FullTime ? 2f : 1f);
        }
        cap *= w * (shop ? 1.3f : 1f);
        // 재료: 곳간부터, 모자라면 장인 집의 남는 기초 재료
        float need = cap * Input[(int)good];
        float use = MathF.Min(need, root.Store[(int)Good.Basic]);
        root.Store[(int)Good.Basic] -= use;
        foreach (int m in makers)
        {
            if (use >= need) break;
            int h = p.Household[m];
            if (!hh.IsAlive(h)) continue;
            float spare = MathF.Max(0f, G(h, Good.Basic) - KeepOf(h, Good.Basic));
            float take = MathF.Min(spare, need - use);
            G(h, Good.Basic) -= take;
            use += take;
        }
        float made = use / Input[(int)good];
        if (made <= 0.01f) return;
        Stats.OrgProduced += made;
        Deliver(root, g, good, made);
    }

    /// <summary>만든 것을 공급 선의 비중대로 보낸다. 선이 없으면 시장에 판다.</summary>
    private void Deliver(Group root, Group from, Good good, float amt)
    {
        var lines = from.Supply.Where(x => x.weight > 0f).ToList();
        if (lines.Count == 0) lines.Add((-2, 1f));
        float tot = lines.Sum(x => x.weight);
        foreach (var (target, wt) in lines)
        {
            float a = amt * wt / tot;
            if (target == -1) { root.Store[(int)good] += a; continue; }
            if (target == -2) { SellToVillage(root, from.Village, good, a, 0.85f); continue; }
            var t = GroupById(target);
            if (t == null || RootOf(t).Id != root.Id) { root.Store[(int)good] += a; continue; }
            if (t.Tasks.Any(x => x.Verb == Verb.Trade))
            {
                // 납품: 장사 갈래가 그 마을에 판다. 장사 대장의 솜씨가 값을 ±20% 바꾼다
                int tl = FindAlive(t.LeaderUid);
                float mult = 0.8f + 0.4f * (tl >= 0 ? People.GetSkill(tl, Skill.Trade) : 0.3f);
                if (FacilityOf(root, FacilityKind.Inn, t.Village) != null) mult *= 1.1f;
                SellToVillage(root, t.Village, good, a, mult);
                continue;
            }
            // 보급: 무구는 갈래가 지니고, 다른 것은 조직원 집에 나눈다(학교면 비품으로 남긴다)
            if (good == Good.Arms || IsSchool(t)) { t.Kit[(int)good] += a; Stats.Supplied += a; continue; }
            var homes = MembersFast(t).Select(m => People.Household[m]).Where(Households.IsAlive).Distinct().ToList();
            if (homes.Count == 0) { root.Store[(int)good] += a; continue; }
            foreach (int h in homes) G(h, good) += a / homes.Count;
            Stats.Supplied += a;
        }
    }

    /// <summary>마을 v 에서 바라는 집에게 판다(값 × mult). 판 값은 세력이 번다. 못 판 것은 곳간에 둔다. 판 양.</summary>
    private float SellToVillage(Group root, int v, Good g, float amt, float mult)
    {
        var hh = Households;
        float price = PriceAt(v, g) * mult, sold = 0f;
        int days = DaysToHarvest();
        if (v >= 0)
            foreach (int h in VillageHouses(v))
            {
                if (sold >= amt) break;
                if (!hh.IsAlive(h)) continue;
                float want = Want(h, g);
                if (want <= 0.05f) continue;
                float afford = MathF.Max(0f, hh.Food[h] - hh.Need[h] * days) * 0.5f / MathF.Max(0.1f, price);
                float give = MathF.Min(MathF.Min(want, afford), amt - sold);
                if (give <= 0f) continue;
                G(h, g) += give; hh.Food[h] -= give * price; sold += give;
            }
        if (sold > 0f) { Earn(root, sold * price); Stats.OrgGoodsSold += sold * price; }
        if (amt - sold > 0f) root.Store[(int)g] += amt - sold;
        return sold;
    }

    // ------------------------------------------------------------------ 무구와 상근

    /// <summary>갈래의 무구 보급률(0~1).</summary>
    public float ArmsCov(Group g) => g.Members <= 0 ? 0f : MathF.Min(1f, g.Kit[(int)Good.Arms] / g.Members);

    /// <summary>갈래 싸움 힘에 곱하는 몫: 무구 1 + 0.3 × 보급률, 상근 1.6.</summary>
    private float ForceFactor(Group g) => (1f + 0.3f * ArmsCov(g)) * (g.FullTime ? 1.6f : 1f);

    /// <summary>훈련 속도(배정한 병영 +20%).</summary>
    private float TrainBoost(Group g) => FacilityOf(RootOf(g), FacilityKind.Barracks, -1, g) != null ? 1.2f : 1f;

    public bool IsFullTime(int i) => _fullTimeUids.Contains(People.Uid[i]);

    /// <summary>해마다(일 하기 전): 갈래 재화가 닳고, 상근 갈래를 먹이고, 상근 사람 목록을 다시 만든다.</summary>
    private void SupplyYear()
    {
        var p = People; var hh = Households;
        _fullTimeUids.Clear();
        foreach (var g in _groups)
        {
            if (!g.Alive) continue;
            for (int k = 0; k < GoodCount; k++) g.Kit[k] *= 1f - Wear[k];
            if (IsTop(g))
            {
                bool keep = FacilityOf(g, FacilityKind.Warehouse) != null;
                for (int k = 0; k < GoodCount; k++) if (!keep) g.Store[k] *= 1f - Wear[k];
            }
            if (!g.FullTime || g.Home >= 0) continue;
            var root = RootOf(g);
            float ration = 24f * (FacilityOf(root, FacilityKind.Barracks, -1, g) != null ? 0.8f : 1f);
            foreach (int m in MembersFast(g).ToList())
            {
                int h = p.Household[m];
                if (!hh.IsAlive(h)) continue;
                float fromT = MathF.Min(ration, root.Treasury);
                root.Treasury -= fromT;
                float rest = ration - fromT;
                if (rest > 0f)
                {
                    // 세력이 못 대면 제 집이 댄다. 제 집도 어려우면 갈래를 떠난다
                    if (Surplus(h) < 0.5f) { LeaveGroup(m, g); Stats.RationLeft++; continue; }
                    hh.Food[h] -= MathF.Min(rest, MathF.Max(0f, hh.Food[h]));
                }
                hh.Food[h] += ration;   // 상근 사람은 먹을 것을 받는다(일손 대신)
                _fullTimeUids.Add(p.Uid[m]);
            }
        }
        Stats.FullTimers = _fullTimeUids.Count;
    }

    // ------------------------------------------------------------------ 시설

    /// <summary>시설을 짓기 시작한다. 값은 세력 곳간, 모자라면 우두머리 집 식량에서. 자재는 집 짓기처럼 쓴다.</summary>
    public Facility? BuildFacility(Group root, FacilityKind k, int village, Group? assign = null)
    {
        root = RootOf(root);
        var hh = Households;
        int leader = FindAlive(root.LeaderUid);
        int home = root.Home >= 0 ? root.Home : leader >= 0 ? People.Household[leader] : -1;
        if (!hh.IsAlive(home) || village < 0) return null;
        var (cost, _, _) = FacilityCost(k);
        if (root.Treasury + MathF.Max(0f, hh.Food[home] - hh.Need[home] * DaysToHarvest()) < cost) return null;
        float fromT = MathF.Min(cost, root.Treasury);
        root.Treasury -= fromT;
        hh.Food[home] -= cost - fromT;
        UseBuildMaterials(home, cost);
        var f = new Facility { Id = _facilities.Count, Kind = k, Village = village, Owner = root.Id, Assigned = assign?.Id ?? DefaultAssign(root, k) };
        _facilities.Add(f);
        Stats.FacilitiesBuilt++;
        if (leader >= 0) Chron(leader, $"{VillageNameOf(village)}에 {Obj(FacilityName(k))} 짓기 시작했다");
        return f;
    }

    private string VillageNameOf(int v) => $"{v}번 마을";

    /// <summary>시설 종류에 맞는 갈래(학당은 학교, 공방은 생산, 병영은 상근이나 훈련 갈래).</summary>
    private int DefaultAssign(Group root, FacilityKind k)
    {
        Verb? v = k switch { FacilityKind.Hall => Verb.Teach, FacilityKind.Workshop => Verb.Produce, FacilityKind.Barracks => Verb.Train, _ => null };
        if (v is not Verb vb) return -1;
        foreach (var u in Subtree(root)) if (RootOf(u).Id == root.Id && u.Tasks.Any(t => t.Verb == vb)) return u.Id;
        return -1;
    }

    public bool AssignFacility(Facility f, Group? unit)
    {
        if (!f.Alive) return false;
        if (unit != null && RootOf(unit).Id != f.Owner) return false;
        f.Assigned = unit?.Id ?? -1;
        return true;
    }

    /// <summary>해마다: 짓고, 유지비를 내고, 주인과 배정을 맞추고, 방앗간 삯을 받고, NPC 세력이 짓는다.</summary>
    private void FacilityYear()
    {
        var hh = Households; var p = People;
        foreach (var f in _facilities)
        {
            if (!f.Alive) continue;
            var owner = GroupById(f.Owner);
            var unit = f.Assigned >= 0 ? GroupById(f.Assigned) : null;
            // 주인이 사라졌으면 배정받은 갈래의 세력이 잇는다. 없으면 버려진다
            if (owner == null || !IsTop(owner))
            {
                var heir = unit != null ? RootOf(unit) : owner != null ? RootOf(owner) : null;
                f.Owner = heir?.Id ?? -1;
                owner = heir;
            }
            // 배정받은 갈래가 세력을 떠났으면: 그 마을에서 더 세면 점거한다, 아니면 배정을 푼다
            if (owner != null && unit != null && RootOf(unit).Id != owner.Id)
            {
                var other = RootOf(unit);
                if (unit.Strength > LocalForce(owner, f.Village)) { f.Owner = other.Id; owner = other; Stats.FacilitiesSeized++; if (FindAlive(other.LeaderUid) is int ol && ol >= 0) Chron(ol, $"{Obj(FacilityName(f.Kind))} 점거해 가져왔다"); }
                else f.Assigned = -1;
            }
            if (owner == null) { f.Built = MathF.Max(0f, f.Built - 0.5f); if (f.Built <= 0f) { f.Alive = false; Stats.FacilitiesLost++; } continue; }
            var (cost, years, upkeep) = FacilityCost(f.Kind);
            if (f.Built < years) { f.Built += 1f; if (f.Built >= years) Stats.FacilitiesDone++; continue; }
            // 유지비
            int leader = FindAlive(owner.LeaderUid);
            int home = owner.Home >= 0 ? owner.Home : leader >= 0 ? p.Household[leader] : -1;
            float fromT = MathF.Min(upkeep, owner.Treasury);
            owner.Treasury -= fromT;
            float rest = upkeep - fromT;
            if (rest > 0f && hh.IsAlive(home) && Surplus(home) > 0.8f) { hh.Food[home] -= rest; rest = 0f; }
            f.Unpaid = rest > 0f ? f.Unpaid + 1 : 0;
            if (f.Unpaid >= 3) { f.Alive = false; Stats.FacilitiesLost++; if (leader >= 0) Chron(leader, $"돌보지 못한 {Topic(FacilityName(f.Kind))} 무너졌다"); continue; }
            // 방앗간 삯
            if (f.Kind == FacilityKind.Mill && f.Village >= 0)
            {
                float fee = 0f;
                foreach (int h in VillageHouses(f.Village))
                {
                    if (!hh.IsAlive(h) || h == home) continue;
                    var inc = IncomeOf(h);
                    float x = MathF.Min(0.03f * MathF.Max(0f, inc.own + inc.tenant), MathF.Max(0f, hh.Food[h]));
                    hh.Food[h] -= x; fee += x;
                }
                if (fee > 0f) Earn(owner, fee);
            }
        }
        // NPC 세력이 짓는다: 넉넉하면 그 세력의 큰 일에 맞는 시설 하나
        foreach (var root in _groups.ToList())
        {
            if (!root.Alive || !IsTop(root) || root.Home >= 0 || root.Total < 6) continue;
            int leader = FindAlive(root.LeaderUid);
            if (leader < 0 || IsPlayer(leader) || !_rng.Chance(0.05f)) continue;
            int home = p.Household[leader];
            FacilityKind? want = null;
            foreach (var t in Subtree(root).SelectMany(u => u.Tasks).OrderByDescending(t => t.Weight))
            {
                want = t.Verb switch
                {
                    Verb.Teach => FacilityKind.Hall, Verb.Produce => FacilityKind.Workshop, Verb.Preach => FacilityKind.Chapel,
                    Verb.Trade => FacilityKind.Inn, Verb.Train => FacilityKind.Barracks, Verb.Guard when root.Visibility <= Visibility.Covert => FacilityKind.Fort, _ => null,
                };
                if (want != null) break;
            }
            if (want is not FacilityKind k || _facilities.Any(f => f.Alive && f.Owner == root.Id && f.Kind == k)) continue;
            if (hh.Food[home] - hh.Need[home] * Config.DaysPerYear < 3f * FacilityCost(k).cost) continue;
            BuildFacility(root, k, root.Village);
        }
        Stats.Facilities = _facilities.Count(f => f.Alive);
    }

    /// <summary>세력 root 가 마을 v 에 둔 힘(그 마을을 맡은 갈래의 힘).</summary>
    private float LocalForce(Group root, int v)
    {
        float s = 0f;
        foreach (var u in Subtree(root)) if (RootOf(u).Id == root.Id && (u.Village == v || u.Tasks.Any(t => t.Village == v))) s += u.OwnStrength;
        return s;
    }
}
