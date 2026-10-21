namespace People.Sim;

/// <summary>
/// 화폐(plan_faction 7장). 집과 세력 곳간은 은화를 가진다. 식량은 먹는 재화다.
/// - 마을마다 곡물 장터(곳간과 은화를 가진 장꾼들)가 식량을 사고판다. 값 = √(목표 ÷ 곳간), 0.5~3. 목표는 마을이 한 계절 먹을 양의 4분의 1이다.
/// - 내야 할 은화가 모자라면 그 자리에서 식량을 장터에 판다(값의 0.9). 은화를 가진 집은 먹을 것이 모자라면 계절마다 장터에서 산다(값의 1.1).
/// - 은화로 셈하는 것: 품삯, 공납(지배자가 현물과 은화 가운데 고른다), 재화 시장, 땅값, 수업료, 시설 값과 유지비, 조직 곳간.
///   현물로 남는 것: 소작료, 빚, 약탈, 돌봄.
/// - 큰 지배자(단계 2 이상)는 걷은 공납의 5%만큼 은화를 찍는다. 장터는 사람이 늘면 은화를 조금씩 들인다.
/// </summary>
public sealed partial class World
{
    private readonly List<float> _granFood = new(), _granCoin = new(), _foodPrice = new();
    private readonly Dictionary<int, bool> _coinTribute = new();   // 플레이어가 정한 공납 방식(은화면 true)

    private void EnsureGranary()
    {
        while (_granFood.Count < VillageList.Count)
        {
            int v = _granFood.Count;
            int houses = v < VillageList.Count ? Math.Max(1, VillageList.Households[v]) : 1;
            _granFood.Add(10f * houses); _granCoin.Add(15f * houses); _foodPrice.Add(1f);
        }
    }

    /// <summary>마을 v 의 식량 한 단위 값(은화).</summary>
    public float FoodPrice(int v) { EnsureGranary(); return v >= 0 && v < _foodPrice.Count ? _foodPrice[v] : 1f; }

    /// <summary>집 h 가 은화로 가진 것과 식량을 함께 센 살림(식량으로).</summary>
    public float WealthOf(int h) => Households.Food[h] + Households.Coin[h] / MathF.Max(0.1f, FoodPrice(Households.Village[h]));

    /// <summary>집 h 의 식량을 장터에 판다(값의 0.9). 받은 은화.</summary>
    private float SellFood(int h, float food)
    {
        var hh = Households;
        int v = hh.Village[h];
        EnsureGranary();
        if (v < 0 || v >= _granFood.Count || food <= 0f) return 0f;
        float price = 0.9f * _foodPrice[v];
        food = MathF.Min(food, MathF.Min(MathF.Max(0f, hh.Food[h]), _granCoin[v] / price));
        if (food <= 0f) return 0f;
        hh.Food[h] -= food; _granFood[v] += food;
        float coin = food * price;
        _granCoin[v] -= coin; hh.Coin[h] += coin;
        Stats.FoodSold += food;
        return coin;
    }

    /// <summary>집 h 가 장터에서 식량을 산다(값의 1.1, 가진 은화만큼). 산 식량.</summary>
    private float BuyFood(int h, float food)
    {
        var hh = Households;
        int v = hh.Village[h];
        EnsureGranary();
        if (v < 0 || v >= _granFood.Count || food <= 0f) return 0f;
        float price = 1.1f * _foodPrice[v];
        food = MathF.Min(food, MathF.Min(_granFood[v], hh.Coin[h] / price));
        if (food <= 0f) return 0f;
        hh.Food[h] += food; _granFood[v] -= food;
        float coin = food * price;
        hh.Coin[h] -= coin; _granCoin[v] += coin;
        Stats.FoodBought += food;
        return food;
    }

    /// <summary>
    /// 집 from 이 집 to 에게 은화 amount 를 낸다. 모자라면 식량을 팔아 채운다(all 이면 굶을 만큼까지, 아니면 다음 수확까지 먹을 것은 남긴다).
    /// to 가 -1 이면 받는 집 없이 쓴다(장터에 낸다). 실제로 낸 은화.
    /// </summary>
    private float PayCoin(int from, int to, float amount, bool all = true)
    {
        var hh = Households;
        if (amount <= 0f || !hh.IsAlive(from)) return 0f;
        if (hh.Coin[from] < amount)
        {
            float price = 0.9f * FoodPrice(hh.Village[from]);
            float spare = all ? hh.Food[from] : MathF.Max(0f, hh.Food[from] - hh.Need[from] * DaysToHarvest());
            SellFood(from, MathF.Min(spare, (amount - hh.Coin[from]) / MathF.Max(0.05f, price)));
        }
        float paid = MathF.Min(amount, MathF.Max(0f, hh.Coin[from]));
        hh.Coin[from] -= paid;
        // to: 집이면 그 집에, -1 이면 장터(그 마을 장꾼의 품삯으로), -2 면 부른 쪽이 받아 나눈다
        if (to >= 0 && hh.IsAlive(to)) hh.Coin[to] += paid;
        else if (to == -1 && hh.Village[from] >= 0) { EnsureGranary(); _granCoin[hh.Village[from]] += paid; }
        return paid;
    }

    /// <summary>세력 곳간(은화)에서 낸다. 모자라면 우두머리 집이 낸다. 실제로 낸 것.</summary>
    private float PayFromTreasury(Group root, int to, float amount)
    {
        float fromT = MathF.Min(amount, root.Treasury);
        root.Treasury -= fromT;
        if (to >= 0 && Households.IsAlive(to)) Households.Coin[to] += fromT;
        float rest = amount - fromT;
        if (rest <= 0f) return amount;
        int leader = FindAlive(root.LeaderUid);
        int home = root.Home >= 0 ? root.Home : leader >= 0 ? People.Household[leader] : -1;
        return fromT + (Households.IsAlive(home) ? PayCoin(home, to, rest, false) : 0f);
    }

    /// <summary>지배자 lord 의 공납이 은화인가. 플레이어가 정했으면 그것, 아니면 큰 지배자(단계 2 이상)는 은화.</summary>
    public bool TributeInCoin(int lord) => _coinTribute.TryGetValue(lord, out bool c) ? c : TierOf(lord) >= 2;

    public void SetTributeInCoin(int lord, bool coin) => _coinTribute[lord] = coin;

    /// <summary>계절마다: 은화를 가진 집은 이번 계절과 다음 계절에 먹을 것이 모자라면 장터에서 산다. 장터 식량은 조금 상한다.</summary>
    private void GrainSeason()
    {
        var hh = Households;
        EnsureGranary();
        int season = Config.DaysPerSeason;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Coin[h] <= 0.05f) continue;
            float want = hh.Need[h] * season * 2f - hh.Food[h];
            if (want > 0f) BuyFood(h, want);
        }
        for (int v = 0; v < _granFood.Count; v++) _granFood[v] *= 1f - 0.5f * Config.SpoilagePerSeason;
    }

    /// <summary>
    /// 해마다: 장터 값을 곳간 대 먹을 양으로 다시 정한다(목표: 마을이 한 계절 먹을 양의 4분의 1). 큰 지배자가 은화를 찍고,
    /// 장터는 사람 수만큼 은화를 조금씩 들인다.
    /// </summary>
    private void MoneyYear()
    {
        var hh = Households;
        EnsureGranary();
        var needs = new float[_granFood.Count];
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && hh.Village[h] >= 0 && hh.Village[h] < needs.Length) needs[hh.Village[h]] += hh.Need[h];
        for (int v = 0; v < _granFood.Count; v++)
        {
            float target = 0.25f * needs[v] * Config.DaysPerSeason + 1f;   // 장꾼이 늘 두려는 곳간: 마을이 한 계절 먹을 양의 4분의 1
            float raw = MathF.Sqrt(target / (_granFood[v] + 1f));
            _foodPrice[v] = 0.7f * _foodPrice[v] + 0.3f * Math.Clamp(raw, 0.5f, 3f);
            // 장꾼이 은화를 들인다(마을이 클수록). 빈 장터가 굳지 않게
            _granCoin[v] += 0.5f * needs[v];
        }
        Stats.FoodPriceMean = _foodPrice.Count > 0 ? _foodPrice.Average() : 1f;
        float coinSum = 0f; int n = 0;
        for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h)) { coinSum += hh.Coin[h]; n++; }
        Stats.CoinMean = n > 0 ? coinSum / n : 0f;
    }
}
