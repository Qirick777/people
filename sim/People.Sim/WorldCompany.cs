namespace People.Sim;

/// <summary>납품 계약 하나: 파는 세력이 사는 지배자 집에 해마다 정한 양을 정한 값(은화)으로 댄다.</summary>
public sealed class Contract
{
    public int Seller, Buyer, BuyerFounded;   // 파는 맨 위 조직 id, 사는 지배자 집
    public Good Good;
    public float Qty, Price;                  // 한 해 양, 한 단위 값
    public int Until;                         // 끝나는 날
    public int Missed;                        // 다 못 댄 해 수
}

/// <summary>
/// 세력 개편 R7(plan_reform): 상단. 밑천(곳간 은화)이 들고 굴러가면 해마다 꾸준히 들어온다.
/// - 임대: 세력 땅(R5)을 소작 시장에 먼저 내놓는다(그 땅 집의 일손과 상관없이). 소작료는 세력 곳간으로 들어간다(곡식을 장터에 팔아 은화로).
///   NPC 상단은 곳간이 땅값의 1.5배를 넘고 소작료가 땅값의 8%를 넘을 만하면 땅을 산다.
/// - 대부: 수확까지 먹을 것이 모자란 집, 또는 땅을 사려는 소작농에게 곳간 은화를 빌려준다(이자는 세력이 정한다, 기본 20%).
///   빚은 지금의 빚 틀을 쓴다. 갚은 것은 세력 곳간으로, 두 해 넘게 못 갚으면 땅을 가져가 세력 땅이 된다(평판 −0.02).
/// - 납품 계약: 세력 곳간의 물건을 지배자 집에 해마다 정한 값으로 댄다. 다 못 대면 받은 만큼만 받고 평판 −0.05, 두 해 넘으면 계약이 깨진다.
///   NPC 지배자는 군인 무구가 모자라면 제 세력 안 무구를 만드는 조직과 다섯 해 계약을 맺는다(값 = 기본 값).
/// </summary>
public sealed partial class World
{
    private readonly List<Contract> _contracts = new();
    private readonly Dictionary<int, int> _leasePlotRoot = new();   // 소작 시장에 내놓은 세력 땅 → 맨 위 조직 id(올해)

    public IReadOnlyList<Contract> Contracts => _contracts;

    // ------------------------------------------------------------------ 임대

    /// <summary>소작 시장에 먼저 내놓을 세력 땅(임대 일을 맡은 세력의 것). 마을별로 돌려준다.</summary>
    private Dictionary<int, List<int>> FactionLeaseOffers()
    {
        _leasePlotRoot.Clear();
        var by = new Dictionary<int, List<int>>();
        foreach (var root in _groups)
        {
            if (!root.Alive || !IsTop(root) || root.FactionPlots.Count == 0) continue;
            if (!Subtree(root).Any(u => u.Tasks.Any(t => t.Verb == Verb.Lease))) continue;
            int home = LandHomeOf(root);
            if (!Households.IsAlive(home)) continue;
            foreach (int id in root.FactionPlots)
            {
                if (Map.Owner(id) != home) continue;
                int v = Households.Village[home];
                if (v < 0) continue;
                (by.TryGetValue(v, out var l) ? l : by[v] = new()).Add(id);
                _leasePlotRoot[id] = root.Id;
            }
        }
        return by;
    }

    /// <summary>세력 땅에서 나온 소작료면 세력 곳간으로 보낸다(곡식을 장터에 팔아 은화로). 보냈으면 true.</summary>
    private bool FactionRent(int plot, float rent)
    {
        if (!_leasePlotRoot.TryGetValue(plot, out int rid) || GroupById(rid) is not { } root) return false;
        Earn(root, rent);
        Stats.FactionRent += rent;
        return true;
    }

    /// <summary>NPC 상단: 곳간이 넉넉하고 소작료가 땅값의 8%를 넘을 만하면 세력 땅을 산다.</summary>
    private void LeaseInvest(Group root)
    {
        int home = LandHomeOf(root);
        var hh = Households;
        if (!hh.IsAlive(home) || hh.Village[home] < 0) return;
        int v = hh.Village[home];
        float rate = v < _rentRate.Count ? _rentRate[v] : 0f;
        if (rate <= 0f) return;
        int best = -1; float bestPrice = float.MaxValue;
        foreach (int h in VillageHouses(v))
        {
            if (h == home || !hh.IsAlive(h) || hh.PlotCount[h] <= 1) continue;
            float pr = LandPrice(h, home);
            if (pr > 0f && pr < bestPrice) { bestPrice = pr; best = h; }
        }
        if (best < 0 || root.Treasury < 1.5f * bestPrice) return;
        float rentGuess = Config.PlotYield * 0.8f * rate * FoodPrice(v);
        if (rentGuess < 0.08f * bestPrice) return;
        BuyFactionLand(root, best);
    }

    // ------------------------------------------------------------------ 대부

    /// <summary>
    /// 대부: 마을에서 수확까지 먹을 것이 모자란 집(빚이 없는)에 모자란 만큼, 땅을 사려는 소작농(일손이 있고 땅값의 30%를 가진)에게 나머지를 빌려준다.
    /// 한 해에 곳간의 절반까지, 일 비중만큼. 빌려준 수.
    /// </summary>
    private int Lend(Group root, Group unit, int v, float weight)
    {
        var hh = Households; var p = People;
        int home = LandHomeOf(root);
        if (!hh.IsAlive(home) || v < 0) return 0;
        float budget = 0.5f * root.Treasury * weight;
        int n = 0;
        int days = DaysToHarvest();
        float price = FoodPrice(v);
        foreach (int h in VillageHouses(v).ToList())
        {
            if (budget < 5f) break;
            if (!hh.IsAlive(h) || h == home || DebtOf(h) > 0f) continue;
            float need = hh.Need[h] * days;
            float gap = need * 1.1f - hh.Food[h] - hh.Coin[h] / MathF.Max(0.1f, price);
            float amount = 0f;
            bool forLand = false;
            if (gap > 2f && hh.Food[h] < need) amount = MathF.Min(gap * price, budget);
            else if (ClassOf(h) == LandClass.Tenant && hh.Labor[h] >= 1f)
            {
                // 소작농이 제 땅을 사도록: 땅값의 30%를 가졌으면 나머지를 빌려준다
                int seller = VillageHouses(v).FirstOrDefault(x => x != h && hh.IsAlive(x) && hh.PlotCount[x] > 1 && LandPrice(x, h) > 0f, -1);
                if (seller < 0) continue;
                float lp = LandPrice(seller, h);
                if (hh.Coin[h] < 0.3f * lp || lp - hh.Coin[h] > budget) continue;
                amount = lp - hh.Coin[h];
                forLand = true;
                hh.Coin[h] += amount;
                root.Treasury -= amount; budget -= amount;
                _debts.Add(new Debt { Debtor = h, DebtorFounded = hh.FoundedDay[h], Creditor = home, CreditorFounded = hh.FoundedDay[home], Owed = amount / MathF.Max(0.1f, price) * (1f + root.LendRate), Org1 = root.Id + 1 });
                if (HeadIdx(seller) is int sh && sh >= 0 && HeadIdx(h) is int bh && bh >= 0)
                    Interact(InteractionId.BuyLand, new InteractionContext { Actor = bh, Target = sh, Third = -1 });
                n++; Stats.LoansForLand++;
                continue;
            }
            if (amount <= 1f || forLand) continue;
            root.Treasury -= amount; budget -= amount;
            hh.Coin[h] += amount;
            _debts.Add(new Debt { Debtor = h, DebtorFounded = hh.FoundedDay[h], Creditor = home, CreditorFounded = hh.FoundedDay[home], Owed = amount / MathF.Max(0.1f, price) * (1f + root.LendRate), Org1 = root.Id + 1 });
            n++;
        }
        Stats.OrgLoans += n;
        return n;
    }

    /// <summary>이자를 정한다(0~50%).</summary>
    public void SetLendRate(Group root, float r) => RootOf(root).LendRate = Math.Clamp(r, 0f, 0.5f);

    // ------------------------------------------------------------------ 납품 계약

    /// <summary>계약을 맺는다(플레이어나 NPC). 같은 둘 사이 같은 물건이면 새로 고친다.</summary>
    public Contract? MakeContract(Group seller, int buyer, Good good, float qty, float price, int years)
    {
        seller = RootOf(seller);
        if (!Households.IsAlive(buyer) || qty <= 0f || price <= 0f || years <= 0) return null;
        _contracts.RemoveAll(c => c.Seller == seller.Id && c.Buyer == buyer && c.Good == good);
        var k = new Contract { Seller = seller.Id, Buyer = buyer, BuyerFounded = Households.FoundedDay[buyer], Good = good, Qty = qty, Price = price, Until = Day + years * Config.DaysPerYear };
        _contracts.Add(k);
        Stats.ContractsMade++;
        return k;
    }

    /// <summary>지배자 집이 계약을 받아들일 확률: 그 물건이 모자랄수록, 값이 장터보다 쌀수록, 파는 쪽 이름이 좋을수록.</summary>
    public float ContractOdds(Group seller, int buyer, Good good, float price)
    {
        var hh = Households;
        if (!hh.IsAlive(buyer)) return 0f;
        int v = hh.Village[buyer];
        float market = v >= 0 ? PriceAt(v, good) : BasePrice[(int)good];
        float cheap = Math.Clamp((market - price) / MathF.Max(0.1f, market), -1f, 1f);
        float want = good == Good.Arms ? 1f - ArmsOfLord(buyer) : 0.3f;
        return Math.Clamp(0.3f + 0.5f * cheap + 0.4f * want + 0.2f * Renown(seller), 0.02f, 0.95f);
    }

    /// <summary>지배자 군인이 가진 무구 몫(0~1, 지배자 집 재고 ÷ 군인 수).</summary>
    private float ArmsOfLord(int lord)
    {
        int soldiers = _soldiersOf.GetValueOrDefault(lord);
        if (soldiers == 0) return 1f;
        return MathF.Min(1f, G(lord, Good.Arms) / soldiers);
    }

    /// <summary>계약 한 해 치(시험용).</summary>
    public void RecountContracts() => ContractYear();

    /// <summary>집 h 가 가진 물건(시험·화면용).</summary>
    public float GoodOfHouse(int h, Good g) => G(h, g);

    /// <summary>대부 한 번(시험용).</summary>
    public int LendNow(Group root, int v) => Lend(RootOf(root), RootOf(root), v, 1f);

    /// <summary>해마다: 계약대로 물건을 대고 값을 받는다. NPC 지배자가 무구 계약을 맺는다.</summary>
    private void ContractYear()
    {
        var hh = Households;
        foreach (var c in _contracts.ToList())
        {
            var seller = GroupById(c.Seller);
            if (seller == null || !hh.IsAlive(c.Buyer) || hh.FoundedDay[c.Buyer] != c.BuyerFounded || Day > c.Until) { _contracts.Remove(c); continue; }
            float give = MathF.Min(c.Qty, seller.Store[(int)c.Good]);
            seller.Store[(int)c.Good] -= give;
            G(c.Buyer, c.Good) += give;
            float pay = PayCoin(c.Buyer, -2, give * c.Price, false);
            Earn(seller, pay, true);
            Stats.ContractPaid += pay;
            if (give < c.Qty - 0.01f)
            {
                c.Missed++;
                AddRepRoot(seller, 0f, -0.05f, 0f);
                if (c.Missed >= 2) { _contracts.Remove(c); Stats.ContractsBroken++; }
            }
        }
        // NPC 지배자: 무구가 반도 안 되면 제 세력 안 무구 조직과 계약
        foreach (var g in _groups)
        {
            if (!g.Alive || g.FarmFood || g.Product != Good.Arms || !g.Tasks.Any(t => t.Verb == Verb.Produce)) continue;
            var root = RootOf(g);
            int leader = FindAlive(root.LeaderUid);
            if (leader >= 0 && IsPlayer(leader)) continue;
            int lord = root.Home >= 0 ? root.Home : root.Sanction;
            if (lord < 0 || !hh.IsAlive(lord)) continue;
            int lh = HeadIdx(lord);
            if (lh < 0 || IsPlayer(lh)) continue;
            if (ArmsOfLord(lord) >= 0.5f || _contracts.Any(c => c.Seller == root.Id && c.Buyer == lord)) continue;
            int soldiers = _soldiersOf.GetValueOrDefault(lord);
            if (soldiers == 0) continue;
            float price = BasePrice[(int)Good.Arms];
            if (_rng.Chance(ContractOdds(root, lord, Good.Arms, price))) MakeContract(root, lord, Good.Arms, MathF.Max(1f, 0.25f * soldiers), price, 5);
        }
    }
}
