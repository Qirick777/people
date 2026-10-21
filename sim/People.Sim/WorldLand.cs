namespace People.Sim;

/// <summary>가구가 어디서 먹고사는지 읽은 이름표(수입원이 가장 큰 쪽).</summary>
public enum LandClass : byte { Owner, Landlord, Tenant, Laborer, Ruler, None }

/// <summary>
/// 4단계: 땅과 일손의 거래. 소작(땅을 빌려준다), 품삯(일손을 산다), 흉년의 식량 빌리기와 땅 팔기.
/// 모든 가구가 같은 규칙으로 정한다. 따로 짠 AI는 없고, 지금보다 나은가를 비교하며 성격이 기울기를 바꾼다.
/// </summary>
public sealed partial class World
{
    // 소작: 해마다 새로 맺는 1년 계약. 필지 → 부치는 가구
    private readonly Dictionary<int, int> _tiller = new();
    private readonly Dictionary<int, List<int>> _leasedIn = new();
    private readonly List<float> _rentRate = new(), _wageRate = new();
    private readonly List<int> _leaseSlack = new();
    private readonly Dictionary<int, int> _unmetLease = new();   // 마을 → 지난해 빌리려다 못 빌린 필지

    // 품삯: 고용주 → 일꾼 계약
    private readonly List<(int employer, int worker, float units, float wage)> _hires = new();
    private float[] _hiredIn = new float[256], _hiredOut = new float[256];

    // 한 해 수입원(수확 때 정산)과 이름표
    private float[] _incOwn = new float[256], _incRent = new float[256], _incTenant = new float[256], _incWage = new float[256];
    private byte[] _class = new byte[256];

    private struct Debt { public int Debtor, DebtorFounded, Creditor, CreditorFounded, Years; public float Owed; public int Org1; }   // Org1: 빌려준 세력(맨 위 조직 id + 1, 0이면 집끼리)
    private readonly List<Debt> _debts = new();

    /// <summary>진단: 지난 수확에서 제 땅 수확, 빌린 땅 수확(소작료 뺀 것), 받은 소작료.</summary>
    public (float own, float tenant, float rent) IncomeOf(int h) => _lastIncome.TryGetValue(h, out var x) ? x : (0f, 0f, 0f);
    private readonly Dictionary<int, (float, float, float)> _lastIncome = new();

    public LandClass ClassOf(int h) => h >= 0 && h < _class.Length ? (LandClass)_class[h] : LandClass.None;
    public int LeasedInCount(int h) => _leasedIn.TryGetValue(h, out var l) ? l.Count : 0;
    public int LeasedOutCount(int h)
    {
        int n = 0;
        foreach (var (plot, t) in _tiller) if (t != h && Map.Owner(plot) == h) n++;
        return n;
    }
    public float DebtOf(int h)
    {
        float s = 0;
        foreach (var d in _debts) if (d.Debtor == h && Households.IsAlive(h) && Households.FoundedDay[h] == d.DebtorFounded) s += d.Owed;
        return s;
    }
    /// <summary>소작농이 땅을 가장 많이 빌린 지주 가구(없으면 -1).</summary>
    public int MainLandlordOf(int h)
    {
        if (!_leasedIn.TryGetValue(h, out var l) || l.Count == 0) return -1;
        return l.GroupBy(p => Map.Owner(p)).OrderByDescending(g => g.Count()).First().Key;
    }
    public static string ClassName(LandClass c) => c switch
    {
        LandClass.Owner => "자작농", LandClass.Landlord => "지주", LandClass.Tenant => "소작농", LandClass.Laborer => "일꾼", LandClass.Ruler => "지배자", _ => "",
    };

    private void EnsureLandArrays()
    {
        int n = Households.HighWater;
        if (_class.Length < n)
        {
            int c = Math.Max(n, _class.Length * 2);
            Array.Resize(ref _hiredIn, c); Array.Resize(ref _hiredOut, c);
            Array.Resize(ref _incOwn, c); Array.Resize(ref _incRent, c); Array.Resize(ref _incTenant, c); Array.Resize(ref _incWage, c);
            int old = _class.Length;
            Array.Resize(ref _class, c);
            for (int k = old; k < c; k++) _class[k] = (byte)LandClass.None;
        }
        while (_rentRate.Count < VillageList.Count) { _rentRate.Add(0); _wageRate.Add(0); _leaseSlack.Add(0); }
    }

    private int VillageOf(int h) => Households.IsAlive(h) ? Households.Village[h] : -1;

    /// <summary>가구가 올해 부칠 필지: 빌려주지 않은 내 땅 + 빌린 땅.</summary>
    private List<int> WorkPlots(int h)
    {
        var list = new List<int>();
        foreach (int id in Map.PlotsOf(h)) if (!_tiller.ContainsKey(id)) list.Add(id);
        if (_leasedIn.TryGetValue(h, out var l)) list.AddRange(l);
        return list;
    }

    /// <summary>
    /// 소작 시장. 일손보다 땅이 많은 집은 남는 땅(덜 비옥한 것부터)을 내놓고, 일손에 비해 땅이 모자란 집이 빌린다.
    /// 소작료는 마을 안 수요와 공급으로 정한다. 빌릴 사람이 많으면 오르고 적으면 내린다.
    /// </summary>
    private void LeaseMarket()
    {
        EnsureLandArrays();
        _tiller.Clear();
        _leasedIn.Clear();
        var hh = Households;
        var vl = VillageList;
        float ppw = Config.PlotsPerWorker;
        var offers = FactionLeaseOffers();   // 임대하는 세력 땅을 먼저 내놓는다(R7)
        var needs = new Dictionary<int, List<(int h, int n)>>();
        foreach (int h in ShuffledHouseholds())
        {
            int v = hh.Village[h];
            if (v < 0) continue;
            int keep = (int)MathF.Ceiling(hh.Labor[h] * ppw * Config.LeaseKeep);
            int want = (int)MathF.Ceiling(hh.Labor[h] * ppw * Config.TenantWant);
            int own = hh.PlotCount[h];
            if (own > keep)
            {
                // 지금 지력이 좋은 땅부터(지친 땅은 내놓지 않고 쉬게 둔다)
                var mine = Map.PlotsOf(h);
                mine.Sort((a, b) => Map.Fert(b).CompareTo(Map.Fert(a)));
                var o = offers.TryGetValue(v, out var ol) ? ol : offers[v] = new();
                for (int k = keep; k < mine.Count; k++) if (!_leasePlotRoot.ContainsKey(mine[k])) o.Add(mine[k]);
            }
            else if (own < want)
            {
                (needs.TryGetValue(v, out var nl) ? nl : needs[v] = new()).Add((h, want - own));
            }
        }
        _unmetLease.Clear();
        foreach (var (v, dl) in needs) _unmetLease[v] = Math.Max(0, dl.Sum(d => d.n) - (offers.TryGetValue(v, out var sl) ? sl.Count : 0));
        for (int v = 0; v < vl.Count; v++)
        {
            _rentRate[v] = 0; _leaseSlack[v] = 0;
            if (!offers.TryGetValue(v, out var supply) || supply.Count == 0) continue;
            if (!needs.TryGetValue(v, out var demand) || demand.Count == 0) { _leaseSlack[v] = supply.Count; continue; }
            int want = demand.Sum(d => d.n);
            float ratio = want / (float)supply.Count;
            float rate = Math.Clamp(Config.RentBase + Config.RentSlope * (ratio - 1f), Config.RentMin, Config.RentMax);
            _rentRate[v] = rate;
            // 남은 땅 목록에서 집에 가까운 것부터 하나씩 집어 낸다(집어 낸 땅은 맨 끝 땅과 자리를 바꿔 지운다)
            int left = supply.Count;
            var px = new int[left]; var py = new int[left]; var pid = supply.ToArray();
            for (int k = 0; k < left; k++) (px[k], py[k]) = Map.Pos(pid[k]);
            foreach (var (t, n) in demand)
            {
                int got = 0;
                int tx = hh.X[t], ty = hh.Y[t];
                for (; got < n && left > 0; got++)
                {
                    int best = 0; long bd = long.MaxValue;
                    for (int k = 0; k < left; k++)
                    {
                        long dx = px[k] - tx, dy = py[k] - ty, d = dx * dx + dy * dy;
                        if (d < bd) { bd = d; best = k; }
                    }
                    int plot = pid[best];
                    left--;
                    pid[best] = pid[left]; px[best] = px[left]; py[best] = py[left];
                    _tiller[plot] = t;
                    (_leasedIn.TryGetValue(t, out var li) ? li : _leasedIn[t] = new()).Add(plot);
                }
                Stats.LeasedPlots += got;
                if (left == 0) break;
            }
            _leaseSlack[v] = left;
            Stats.RentRateSum += rate; Stats.RentMarkets++;
        }
    }

    /// <summary>빌려주지 못하고 일손으로도 못 부치는 땅은 덜 비옥한 것부터 내놓는다(예전 ClaimLand 의 내놓기).</summary>
    private void ReleaseExcess()
    {
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || TierOf(h) >= 1) continue;   // 지배자는 직영지를 내놓지 않고 빌려준다
            int keep = (int)MathF.Ceiling(hh.Labor[h] * Config.PlotsPerWorker * Config.LandRelease);
            var free = Map.PlotsOf(h).Where(p => !_tiller.ContainsKey(p)).ToList();
            if (free.Count <= keep) continue;
            free.Sort((a, b) => Map.BaseFert(a).CompareTo(Map.BaseFert(b)));
            for (int k = 0; k < free.Count - keep; k++) Map.Release(free[k], hh, true);
        }
    }

    /// <summary>
    /// 품삯 시장. 부칠 땅이 일손보다 많고 식량에 여유가 있는 집이, 땅보다 일손이 많은 집 사람을 쓴다.
    /// 품삯은 한 사람이 한 해 일해 내는 수확에 마을 일손 수요·공급 비율을 곱해 정한다.
    /// </summary>
    private void WageMarket()
    {
        EnsureLandArrays();
        _hires.Clear();
        Array.Clear(_hiredIn); Array.Clear(_hiredOut);
        var hh = Households;
        float ppw = Config.PlotsPerWorker;
        var buyers = new Dictionary<int, List<(int h, float u)>>();
        var sellers = new Dictionary<int, List<(int h, float u)>>();
        foreach (int h in ShuffledHouseholds())
        {
            int v = hh.Village[h];
            if (v < 0) continue;
            float plots = WorkPlots(h).Count;
            float cap = hh.Labor[h] * ppw;
            if (plots - cap >= ppw * 0.5f && Surplus(h) >= 0.8f)
                (buyers.TryGetValue(v, out var b) ? b : buyers[v] = new()).Add((h, MathF.Min(2f, (plots - cap) / ppw)));
            else if (cap - plots >= ppw * 0.5f)
                (sellers.TryGetValue(v, out var s) ? s : sellers[v] = new()).Add((h, MathF.Min(2f, (cap - plots) / ppw)));
        }
        foreach (var (v, bl) in buyers)
        {
            _wageRate[v] = 0;
            if (!sellers.TryGetValue(v, out var sl)) continue;
            float d = bl.Sum(x => x.u), s = sl.Sum(x => x.u);
            float product = ppw * Config.PlotYield * 0.85f;
            float wage = product * Math.Clamp(Config.WageBase + Config.WageSlope * (d / s - 1f), Config.WageMin, Config.WageMax);
            _wageRate[v] = wage;
            int si = 0;
            float left = sl.Count > 0 ? sl[0].u : 0;
            foreach (var (e, need) in bl)
            {
                float want = need;
                while (want > 0.05f && si < sl.Count)
                {
                    float take = MathF.Min(want, left);
                    int wkr = sl[si].h;
                    _hires.Add((e, wkr, take, wage));
                    _hiredIn[e] += take; _hiredOut[wkr] += take;
                    Stats.HiredUnits += take;
                    want -= take; left -= take;
                    if (left <= 0.05f) { si++; left = si < sl.Count ? sl[si].u : 0; }
                }
                if (si >= sl.Count) break;
            }
            Stats.WageSum += wage; Stats.WageMarkets++;
        }
    }

    /// <summary>수확 뒤 품삯을 치른다. 고용주가 모자라면 있는 만큼.</summary>
    private void PayWages()
    {
        var hh = Households;
        foreach (var (e, w, u, wage) in _hires)
        {
            if (!hh.IsAlive(e) || !hh.IsAlive(w)) continue;
            float pay = MathF.Min(u * wage, MathF.Max(0f, hh.Food[e]));
            hh.Food[e] -= pay;
            hh.Food[w] += pay;
            _incWage[w] += pay;
            _incOwn[e] -= pay;
        }
    }

    /// <summary>
    /// 계절마다: 수확까지 먹을 것이 모자란 집은 이웃에게 식량을 빌린다(이자 붙은 빚).
    /// 빌릴 데가 없으면 밭을 판다. 흉년에는 땅이 식량 남는 집으로 모인다.
    /// </summary>
    private void Distress()
    {
        EnsureLandArrays();
        var hh = Households;
        var p = People;
        int days = DaysToHarvest();
        var byVillage = new Dictionary<int, List<int>>();
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && hh.Village[h] >= 0) (byVillage.TryGetValue(hh.Village[h], out var l) ? l : byVillage[hh.Village[h]] = new()).Add(h);
        foreach (var (v, list) in byVillage)
        {
            // 여유가 가장 큰 두 집을 한 번 찾아 두고, 누군가의 식량이 움직였을 때만 다시 찾는다
            int top1 = -1, top2 = -1;
            float sp1 = 0f, sp2 = 0f;
            bool dirty = true;
            foreach (int h in list)
            {
                float need = hh.Need[h] * days;
                if (need <= 0 || hh.Food[h] >= need * Config.DistressLevel) continue;
                float deficit = need * Config.DistressCover - hh.Food[h];
                if (deficit <= 1f) continue;
                if (dirty)
                {
                    top1 = top2 = -1; sp1 = sp2 = 0f;
                    foreach (int o in list)
                    {
                        float sp = hh.Food[o] - hh.Need[o] * days * 1.3f;
                        if (sp > sp1) { top2 = top1; sp2 = sp1; top1 = o; sp1 = sp; }
                        else if (sp > sp2) { top2 = o; sp2 = sp; }
                    }
                    dirty = false;
                }
                // 1) 빌리기: 여유가 가장 큰 이웃. 사교성이 높은 가장일수록 잘 빌려준다.
                int lender = top1 != h ? top1 : top2;
                float spare = top1 != h ? sp1 : sp2;
                int raidsBefore = Stats.Raids;
                dirty = true;
                if (lender >= 0 && _rng.Chance(0.3f + 0.5f * HeadTrait(lender, Trait.Sociability)))
                {
                    float amount = MathF.Min(deficit, spare);
                    hh.Food[lender] -= amount;
                    hh.Food[h] += amount;
                    _debts.Add(new Debt { Debtor = h, DebtorFounded = hh.FoundedDay[h], Creditor = lender, CreditorFounded = hh.FoundedDay[lender], Owed = amount * (1f + Config.LoanInterest) });
                    HeadsRelate(h, lender, 5f, 0f, 20f, MemoryType.LoanTaken, 15);
                    Stats.Loans++;
                    continue;
                }
                // 2) 밭 팔기: 가장 여유 있는 집이 산다. 욕심 많은 가장일수록 잘 산다. 흉값이라 싸다.
                // 3) 그래도 모자라면: 거친 가장은 남의 곡식을 털러 간다.
                if (lender < 0 || hh.PlotCount[h] <= 1 || !_rng.Chance(0.3f + 0.6f * HeadTrait(lender, Trait.Ambition)))
                {
                    TryRaid(h, deficit);
                    dirty = Stats.Raids != raidsBefore;   // 털지 않았으면 아무 식량도 움직이지 않았다
                    continue;
                }
                var mine = Map.PlotsOf(h).Where(x => !_tiller.ContainsKey(x)).ToList();
                mine.Sort((a, b) => Map.BaseFert(a).CompareTo(Map.BaseFert(b)));
                foreach (int plot in mine)
                {
                    if (deficit <= 0 || hh.PlotCount[h] <= 1) break;
                    float price = Map.PlotValue(plot) * Config.DistressPrice;
                    if (hh.Food[lender] - price < hh.Need[lender] * days * 1.3f) break;
                    hh.Food[lender] -= price;
                    hh.Food[h] += price;
                    deficit -= price;
                    Map.GivePlot(plot, lender, hh);
                    Stats.LandSales++;
                }
                if (deficit > 1f) TryRaid(h, deficit);
            }
        }
    }

    /// <summary>두 집 가장 사이 관계: a 의 가장이 b 의 가장을 이렇게 보게 되고, 기억을 남긴다.</summary>
    private void HeadsRelate(int a, int b, float aff, float fear, float debt, MemoryType mem, int weight)
    {
        var ha = Households.Head[a]; var hb = Households.Head[b];
        if (!People.IsValid(ha) || !People.IsValid(hb)) return;
        Social.Add(People, ha.Index, People.Uid[hb.Index], aff, fear, debt);
        Social.Remember(People, ha.Index, mem, People.Uid[hb.Index], Day, weight);
    }

    private float HeadTrait(int h, Trait t)
    {
        var head = Households.Head[h];
        return People.IsValid(head) ? People.Get(head.Index, t) : 0.5f;
    }

    /// <summary>수확 뒤 빚을 갚는다. 두 해 넘게 못 갚으면 빚만큼 밭을 넘긴다.</summary>
    private void SettleDebts()
    {
        var hh = Households;
        for (int k = _debts.Count - 1; k >= 0; k--)
        {
            var d = _debts[k];
            bool debtorOk = hh.IsAlive(d.Debtor) && hh.FoundedDay[d.Debtor] == d.DebtorFounded;
            bool creditorOk = hh.IsAlive(d.Creditor) && hh.FoundedDay[d.Creditor] == d.CreditorFounded;
            if (!debtorOk || !creditorOk) { _debts.RemoveAt(k); continue; }
            float keep = hh.Need[d.Debtor] * Config.DaysPerYear * 0.6f;
            float pay = MathF.Min(d.Owed, MathF.Max(0f, hh.Food[d.Debtor] - keep));
            hh.Food[d.Debtor] -= pay;
            var lender = d.Org1 > 0 ? GroupById(d.Org1 - 1) : null;
            if (lender != null) Earn(lender, pay);   // 세력이 빌려준 빚은 세력 곳간으로
            else hh.Food[d.Creditor] += pay;
            d.Owed -= pay;
            if (d.Owed <= 0.5f) { _debts.RemoveAt(k); continue; }
            d.Years++;
            if (d.Years >= Config.ForecloseYears)
            {
                var mine = Map.PlotsOf(d.Debtor).Where(x => !_tiller.ContainsKey(x)).ToList();
                mine.Sort((a, b) => Map.BaseFert(b).CompareTo(Map.BaseFert(a)));   // 빚쟁이는 좋은 땅부터 가져간다
                foreach (int plot in mine)
                {
                    if (d.Owed <= 0) break;
                    d.Owed -= Map.PlotValue(plot);
                    Map.GivePlot(plot, d.Creditor, hh);
                    if (lender != null) lender.FactionPlots.Add(plot);   // 세력 땅이 된다
                    Stats.Foreclosed++;
                }
                if (lender != null) AddRepRoot(lender, 0.01f, -0.02f, 0f);
                HeadsRelate(d.Debtor, d.Creditor, -30f, 10f, 0f, MemoryType.Foreclosed, -50);
                _debts.RemoveAt(k);
                continue;
            }
            _debts[k] = d;
        }
    }

    /// <summary>한 해 수입원으로 이름표를 정하고 수입 기록을 비운다.</summary>
    private void Classify()
    {
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            float own = _incOwn[h], rent = _incRent[h], ten = _incTenant[h], wage = _incWage[h];
            LandClass c;
            if (IsRuler(h)) c = LandClass.Ruler;
            else if (rent > own && rent > ten && rent > wage) c = LandClass.Landlord;
            else if (ten > own && ten >= wage) c = LandClass.Tenant;
            else if (wage > own && wage > ten) c = LandClass.Laborer;
            else if (own > 0) c = LandClass.Owner;
            else c = LandClass.None;
            _class[h] = (byte)c;
            _lastIncome[h] = (own, ten, rent);
            _incOwn[h] = _incRent[h] = _incTenant[h] = _incWage[h] = 0;
        }
    }

    /// <summary>마을에 빌려줄 땅이 남아 있는가(분가하는 가구가 남을지 정할 때).</summary>
    private int LeaseSlack(int v) => v >= 0 && v < _leaseSlack.Count ? _leaseSlack[v] : 0;
}
