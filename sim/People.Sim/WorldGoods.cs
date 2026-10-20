namespace People.Sim;

/// <summary>재화. 기초 재료(나무, 섬유, 돌), 고급 재료(쇠, 다듬은 목재), 도구, 의류, 자재.</summary>
public enum Good : byte { Basic, Fine, Tools, Cloth, Build, Count }

/// <summary>
/// E1 수공업과 재화. 물건 하나하나가 아니라 집마다의 양으로 다룬다.
/// - 해마다 한 번: 닳고 쓰고 → 기초 재료를 모으고 → 기초 재료 시장 → 장인·목수가 만들고 → 나머지 시장.
/// - 시장은 마을마다 재화마다 한꺼번에 정산한다. 값은 식량으로 매기고, 모자람/남음으로 오르내린다.
/// - 효과: 도구는 수확과 생산, 의류는 겨울 사망, 자재는 집 짓기, 고급 재료는 군인의 무장.
/// </summary>
public sealed partial class World
{
    public const int GoodCount = (int)Good.Count;
    public static readonly float[] BasePrice = { 1f, 6f, 4f, 3f, 3f };
    private const float ClothNeed = 0.4f;   // 한 사람이 한 해에 닳리는 옷
    private static readonly float[] Wear = { 0.10f, 0.10f, 0.15f, 0f, 0.05f };   // 해마다 닳는 몫(의류는 입어서 쓴다)
    private static readonly float[] Input = { 0f, 3f, 2f, 2f, 2f };              // 한 단위에 드는 기초 재료

    private float[] _goods = new float[256 * GoodCount];
    private float[] _clothShort = new float[256];
    private int[] _goodsFounded = Enumerable.Repeat(-1, 256).ToArray();   // 재고를 쓴 집이 같은 집인지(번호를 다시 쓰면 비운다)
    private readonly List<float> _price = new();     // 마을 × 재화 → 값(식량)
    private readonly List<float> _resid = new();     // 마을 × 재화 → 시장이 끝난 뒤 남은 공급(+) 또는 못 채운 수요(−)

    public static string GoodName(Good g) => g switch
    {
        Good.Basic => "기초 재료", Good.Fine => "고급 재료", Good.Tools => "도구", Good.Cloth => "의류", _ => "자재",
    };

    private void EnsureGoods()
    {
        int n = Households.HighWater;
        if (_clothShort.Length < n)
        {
            int c = Math.Max(n, _clothShort.Length * 2);
            Array.Resize(ref _goods, c * GoodCount);
            Array.Resize(ref _clothShort, c);
            int old = _goodsFounded.Length;
            Array.Resize(ref _goodsFounded, c);
            for (int k = old; k < c; k++) _goodsFounded[k] = -1;
        }
        while (_price.Count < VillageList.Count * GoodCount)
        {
            int g = _price.Count % GoodCount;
            _price.Add(BasePrice[g]);
            _resid.Add(0f);
        }
    }

    public float GoodsOf(int h, Good g) => h >= 0 && h * GoodCount < _goods.Length && Households.IsAlive(h) ? G(h, g) : 0f;

    private ref float G(int h, Good g)
    {
        EnsureGoods();
        int f = Households.FoundedDay[h];
        if (_goodsFounded[h] != f)
        {
            for (int k = 0; k < GoodCount; k++) _goods[h * GoodCount + k] = 0f;
            // 새 집은 부모 집에서 도구와 옷가지를 조금 들고 나온다
            _goods[h * GoodCount + (int)Good.Tools] = 1f;
            _goods[h * GoodCount + (int)Good.Cloth] = 1f;
            _goodsFounded[h] = f;
        }
        return ref _goods[h * GoodCount + (int)g];
    }
    public float PriceAt(int v, Good g) => v >= 0 && v * GoodCount + (int)g < _price.Count ? _price[v * GoodCount + (int)g] : BasePrice[(int)g];

    /// <summary>집의 일손(16~60세).</summary>
    private int Workers(int h)
    {
        var p = People;
        int n = 0;
        for (int k = Households.FirstMember[h]; k >= 0; k = p.HhNext[k]) { int a = AgeYears(k); if (a >= 16 && a <= 60) n++; }
        return n;
    }

    /// <summary>도구 갖춤(0~1): 일손 하나에 도구 하나.</summary>
    public float ToolCov(int h)
    {
        if (!Households.IsAlive(h) || h * GoodCount >= _goods.Length) return 0.5f;
        int w = Workers(h);
        return w == 0 ? 1f : MathF.Min(1f, G(h, Good.Tools) / w);
    }

    /// <summary>수확에 곱한다. 도구가 없으면 0.85배.</summary>
    private float ToolFactor(int h) => 0.85f + 0.15f * ToolCov(h);

    /// <summary>겨울에 의류가 모자란 정도(0~1).</summary>
    private float ClothShort(int h) => h >= 0 && h < _clothShort.Length ? _clothShort[h] : 0f;

    /// <summary>지배자 군인의 무장(0~1): 군인 하나에 고급 재료 하나.</summary>
    private float WeaponCov(int lord)
    {
        int s = _soldiersOf.GetValueOrDefault(lord);
        return s == 0 || lord * GoodCount >= _goods.Length ? 0f : MathF.Min(1f, G(lord, Good.Fine) / s);
    }

    // ------------------------------------------------------------------ 해마다

    private void GoodsYear()
    {
        EnsureGoods();
        var p = People; var hh = Households;
        // 1) 닳고, 입고
        float shortSum = 0f; int shortN = 0;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            for (int g = 0; g < GoodCount; g++) G(h, (Good)g) *= 1f - Wear[g];
            float need = ClothNeed * hh.Members[h];
            float use = MathF.Min(G(h, Good.Cloth), need);
            G(h, Good.Cloth) -= use;
            _clothShort[h] = need > 0f ? 1f - use / need : 0f;
            shortSum += _clothShort[h]; shortN++;
        }
        Stats.ClothShortMean = shortN > 0 ? shortSum / shortN : 0f;
        // 2) 기초 재료 모으기(농한기에 나무, 섬유, 돌). 도구가 있으면 더 모은다
        float basic = 0f, covSum = 0f;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            float cov = ToolCov(h);
            covSum += cov;
            int w = Workers(h);
            float got = 3f * w * (0.6f + 0.4f * cov);   // 나무, 섬유, 돌은 흔하다
            G(h, Good.Basic) += got;
            basic += got;
            // 장인이 없어도 조악한 도구는 스스로 깎는다(일손 하나에 한 해 0.12, 도구가 0.7 넘게 갖춰지면 하지 않는다)
            if (cov < 0.7f)
            {
                float self = MathF.Min(0.12f * w, G(h, Good.Basic) / Input[(int)Good.Tools]);
                G(h, Good.Basic) -= self * Input[(int)Good.Tools];
                G(h, Good.Tools) += self;
            }
            // 옷은 집에서 짠다. 도구(물레, 베틀)가 있어야 잘 짠다. 한 해 쓸 만큼과 조금 더까지만
            float clothWant = 1.2f * ClothNeed * hh.Members[h] - G(h, Good.Cloth);
            if (clothWant > 0f)
            {
                float weave = MathF.Min(MathF.Min(clothWant, 0.45f * hh.Members[h] * (0.4f + 0.6f * cov)), G(h, Good.Basic) / Input[(int)Good.Cloth]);
                G(h, Good.Basic) -= weave * Input[(int)Good.Cloth];
                G(h, Good.Cloth) += weave;
            }
        }
        Stats.BasicGathered += basic;
        Stats.ToolCovMean = shortN > 0 ? covSum / shortN : 0f;
        var byVillage = HousesByVillage();
        // 3) 기초 재료 시장(장인과 목수가 재료를 산다)
        foreach (var (v, list) in byVillage) Market(v, list, Good.Basic);
        // 4) 만들기
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            var job = (Job)p.JobOf[i];
            if (job != Job.Crafter && job != Job.Carpenter) continue;
            int h = p.Household[i];
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            float cov = ToolCov(h);
            Good what;
            float skill;
            if (job == Job.Carpenter) { what = Good.Build; skill = p.GetSkill(i, Skill.Carpentry); }
            else
            {
                // 장인: 마을에서 값이 가장 잘 쳐지는 것(도구 없이는 도구만 만든다)
                skill = p.GetSkill(i, Skill.Craft);
                int v = hh.Village[h];
                what = Good.Tools;
                float best = PriceAt(v, Good.Tools) / BasePrice[(int)Good.Tools];
                foreach (var g in new[] { Good.Cloth, Good.Fine })
                {
                    if (cov < 0.2f) continue;
                    float r = PriceAt(v, g) / BasePrice[(int)g];
                    if (r > best) { best = r; what = g; }
                }
            }
            if (what != Good.Tools && cov < 0.2f) continue;   // 의류, 고급 재료, 자재는 도구가 있어야 만든다
            float cap = (4f + 10f * skill) * (what == Good.Tools ? 0.6f + 0.4f * cov : cov);
            float made = MathF.Min(cap, G(h, Good.Basic) / Input[(int)what]);
            if (made <= 0f) continue;
            G(h, Good.Basic) -= made * Input[(int)what];
            G(h, what) += made;
            switch (what)
            {
                case Good.Tools: Stats.ToolsMade += made; break;
                case Good.Cloth: Stats.ClothMade += made; break;
                case Good.Fine: Stats.FineMade += made; break;
                default: Stats.BuildMade += made; break;
            }
        }
        // 5) 나머지 시장
        foreach (var (v, list) in byVillage)
            foreach (var g in new[] { Good.Tools, Good.Cloth, Good.Fine, Good.Build })
                Market(v, list, g);
    }

    private Dictionary<int, List<int>> HousesByVillage()
    {
        var hh = Households;
        var map = new Dictionary<int, List<int>>();
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && hh.Village[h] >= 0)
                (map.TryGetValue(hh.Village[h], out var l) ? l : map[hh.Village[h]] = new()).Add(h);
        return map;
    }

    /// <summary>집 h 가 재화 g 를 얼마나 바라는가(가진 것을 뺀 모자람).</summary>
    private float Want(int h, Good g)
    {
        var hh = Households; var p = People;
        float target = g switch
        {
            Good.Tools => Workers(h),
            Good.Cloth => 1.2f * ClothNeed * hh.Members[h],
            Good.Basic => 6f * CraftersIn(h),
            Good.Fine => _soldiersOf.GetValueOrDefault(h) + (ClientCount(h) > 0 ? 1f : 0f),
            _ => 0f,   // 자재는 지을 때 산다
        };
        return MathF.Max(0f, target - G(h, g));
    }

    private int CraftersIn(int h)
    {
        int n = 0;
        var p = People;
        for (int k = Households.FirstMember[h]; k >= 0; k = p.HhNext[k]) if (p.JobOf[k] is (byte)Job.Crafter or (byte)Job.Carpenter) n++;
        return n;
    }

    /// <summary>
    /// 마을 시장 한 번: 남는 집(바라는 만큼은 남기고)과 모자란 집을 모아 값을 정하고, 살 수 있는 만큼 한꺼번에 맞바꾼다.
    /// 값 = 기본값 × (수요/공급, 0.4~3). 사는 집은 다음 수확까지 먹을 것을 남긴 식량의 절반까지 쓴다.
    /// </summary>
    private void Market(int v, List<int> houses, Good g)
    {
        var hh = Households;
        int gi = (int)g;
        float supply = 0f, demand = 0f;
        var sellers = new List<(int h, float amt)>();
        var buyers = new List<(int h, float amt)>();
        int days = DaysToHarvest();
        foreach (int h in houses)
        {
            float want = Want(h, g);
            float keep = g switch { Good.Tools => Workers(h), Good.Cloth => 1.2f * ClothNeed * hh.Members[h], Good.Basic => 6f * CraftersIn(h), Good.Fine => _soldiersOf.GetValueOrDefault(h), _ => 0f };
            float spare = G(h, g) - keep;
            if (spare > 0.05f) { sellers.Add((h, spare)); supply += spare; }
            else if (want > 0.05f) { buyers.Add((h, want)); demand += want; }
        }
        float price = supply > 0f ? BasePrice[gi] * Math.Clamp(demand / supply, 0.4f, 3f) : BasePrice[gi] * 3f;
        int pi = v * GoodCount + gi;
        if (pi < _price.Count) _price[pi] = 0.7f * _price[pi] + 0.3f * price;   // 값은 천천히 움직인다
        if (supply <= 0f || demand <= 0f) { if (pi < _resid.Count) _resid[pi] = supply - demand; return; }
        // 사는 쪽: 모자람 비율대로, 쓸 수 있는 식량만큼
        float wanted = MathF.Min(supply, demand);
        float bought = 0f;
        foreach (var (h, amt) in buyers)
        {
            float share = wanted * amt / demand;
            float budget = MathF.Max(0f, hh.Food[h] - hh.Need[h] * days) * 0.5f;
            float take = MathF.Min(share, budget / price);
            if (take <= 0f) continue;
            G(h, g) += take;
            hh.Food[h] -= take * price;
            bought += take;
        }
        // 파는 쪽: 남는 양 비율대로 내놓고 값을 받는다
        foreach (var (h, amt) in sellers)
        {
            float give = bought * amt / supply;
            G(h, g) -= give;
            hh.Food[h] += give * price;
        }
        Stats.GoodsSold += bought * price;
        if (pi < _resid.Count) _resid[pi] = (supply - bought) - (demand - bought);
    }

    // ------------------------------------------------------------------ 짓기

    /// <summary>집 짓기·늘리기에 드는 자재. 가진 것을 쓰고, 모자라면 마을에서 사고, 그래도 없으면 식량으로 두 배 값을 치른다(직접 마련).</summary>
    private void UseBuildMaterials(int payer, float baseCost)
    {
        EnsureGoods();
        var hh = Households;
        if (!hh.IsAlive(payer)) return;
        float need = baseCost / 15f;
        float own = MathF.Min(need, G(payer, Good.Build));
        G(payer, Good.Build) -= own;
        need -= own;
        if (need <= 0f) return;
        int v = hh.Village[payer];
        float price = PriceAt(v, Good.Build);
        for (int h = 0; h < hh.HighWater && need > 0.01f; h++)
        {
            if (!hh.IsAlive(h) || h == payer || hh.Village[h] != v || G(h, Good.Build) < 0.5f) continue;
            float take = MathF.Min(need, MathF.Min(G(h, Good.Build), MathF.Max(0f, hh.Food[payer]) / price));
            if (take <= 0f) break;
            G(h, Good.Build) -= take;
            hh.Food[h] += take * price;
            hh.Food[payer] -= take * price;
            need -= take;
        }
        if (need > 0.01f)
        {
            float extra = MathF.Min(MathF.Max(0f, hh.Food[payer]), need * BasePrice[(int)Good.Build] * 2f);
            hh.Food[payer] -= extra;
            Stats.BuildShort += need;
        }
    }

    // ------------------------------------------------------------------ 교역(상단)

    /// <summary>
    /// 상단의 교역: 이웃 마을과 값 차이가 가장 큰 재화를, 싼 마을에서 남는 집에게 사서 비싼 마을의 모자란 집에 판다.
    /// 옮기는 양 = 장사꾼 솜씨로 정한 짐 × 일의 무게, 남은 공급과 못 채운 수요만큼. 이익 = 값 차이 × 옮긴 양.
    /// </summary>
    private float TradeGoods(int v, float capacity)
    {
        EnsureGoods();
        var vl = VillageList;
        if (v < 0 || v >= vl.Count) return 0f;
        int bestU = -1, bestG = -1;
        bool outward = true;
        float bestGap = 0.5f;
        for (int u = 0; u < vl.Count; u++)
        {
            if (u == v || !vl.Alive[u]) continue;
            float dx = vl.CenterX[u] - vl.CenterX[v], dy = vl.CenterY[u] - vl.CenterY[v];
            if (dx * dx + dy * dy > 220f * 220f) continue;
            for (int g = 0; g < GoodCount; g++)
            {
                float gap = PriceAt(u, (Good)g) - PriceAt(v, (Good)g);
                // 우리 마을에 남고 저쪽이 모자라면 내다 팔고, 반대면 들여온다
                if (gap > bestGap && _resid[v * GoodCount + g] > 0.5f && _resid[u * GoodCount + g] < -0.5f) { bestGap = gap; bestU = u; bestG = g; outward = true; }
                if (-gap > bestGap && _resid[u * GoodCount + g] > 0.5f && _resid[v * GoodCount + g] < -0.5f) { bestGap = -gap; bestU = u; bestG = g; outward = false; }
            }
        }
        if (bestU < 0) return 0f;
        int from = outward ? v : bestU, to = outward ? bestU : v;
        var good = (Good)bestG;
        float vol = MathF.Min(capacity, MathF.Min(_resid[from * GoodCount + bestG], -_resid[to * GoodCount + bestG]));
        float lo = PriceAt(from, good), hi = PriceAt(to, good);
        float moved = MoveGoods(from, to, good, vol, lo, hi);
        _resid[from * GoodCount + bestG] -= moved;
        _resid[to * GoodCount + bestG] += moved;
        Stats.GoodsCarried += moved;
        return moved * (hi - lo);
    }

    /// <summary>싼 마을의 남는 집에서 lo 값에 사서, 비싼 마을의 모자란 집에 hi 값에 판다. 옮긴 양.</summary>
    private float MoveGoods(int from, int to, Good g, float vol, float lo, float hi)
    {
        var hh = Households;
        float got = 0f;
        for (int h = 0; h < hh.HighWater && got < vol; h++)
        {
            if (!hh.IsAlive(h) || hh.Village[h] != from) continue;
            float spare = G(h, g) - KeepOf(h, g);
            if (spare <= 0.05f) continue;
            float take = MathF.Min(spare, vol - got);
            G(h, g) -= take; hh.Food[h] += take * lo; got += take;
        }
        float sold = 0f;
        for (int h = 0; h < hh.HighWater && sold < got; h++)
        {
            if (!hh.IsAlive(h) || hh.Village[h] != to) continue;
            float want = Want(h, g);
            if (want <= 0.05f) continue;
            float afford = MathF.Max(0f, hh.Food[h] - hh.Need[h] * DaysToHarvest()) * 0.5f / hi;
            float give = MathF.Min(MathF.Min(want, afford), got - sold);
            if (give <= 0f) continue;
            G(h, g) += give; hh.Food[h] -= give * hi; sold += give;
        }
        // 못 판 짐은 산 값에 되돌려 놓는다(장사꾼이 떠안지 않는다)
        if (sold < got)
            for (int h = 0; h < hh.HighWater && sold < got; h++)
            {
                if (!hh.IsAlive(h) || hh.Village[h] != from) continue;
                float back = got - sold;
                G(h, g) += back; hh.Food[h] -= back * lo;
                got = sold;
                break;
            }
        return sold;
    }

    private float KeepOf(int h, Good g) => g switch
    {
        Good.Tools => Workers(h), Good.Cloth => 1.2f * ClothNeed * Households.Members[h], Good.Basic => 6f * CraftersIn(h),
        Good.Fine => _soldiersOf.GetValueOrDefault(h), _ => 0f,
    };

    /// <summary>약탈: 식량과 함께 도구, 의류, 고급 재료의 30%를 가져간다.</summary>
    private void LootGoods(int from, int to)
    {
        EnsureGoods();
        foreach (var g in new[] { Good.Tools, Good.Cloth, Good.Fine })
        {
            float take = G(from, g) * 0.3f;
            G(from, g) -= take;
            G(to, g) += take;
        }
    }
}
