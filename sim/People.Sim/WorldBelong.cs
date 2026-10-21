namespace People.Sim;

/// <summary>세력원 집 하나가 지금 섬기는 지배자에게 붙어 있는 까닭(0~1씩)과 부담.</summary>
public struct Belonging
{
    public float Protect, Living, Fear, Loyal, Benefit, Habit, Burden;
    public float Total => Protect + Living + Fear + Loyal + Benefit + Habit - Burden;
}

/// <summary>
/// 소속 저울(plan_faction 5장). 세력원 집(아래 집이 없는, 지배자를 섬기는 집)은 해마다 왜 남는지 센다.
/// - 보호: 마을 위험 × 지배자가 지켜 줄 힘의 몫. 생계: 그 세력에게서 빌린 땅, 식구의 일자리, 빚, 대대로 묶임.
/// - 공포: 지배자의 공포 평판 × 보복할 힘의 몫. 충성: 가장의 충성 × 0.6(광신이면 1). 혜택: 그 마을의 세력 학교·시설, 신용.
/// - 관습: 자리 잡은 땅일수록 섬김이 버릇이다. 부담: 지난해 낸 것(공납, 헌금, 수업료) ÷ 소출 × 4.
/// - 다른 길: 아무도 섬기지 않음(0), 또는 보호 거리 안의 다른 지배자(옮기는 비용 0.2를 뺀다). 0.1 넘게 나으면 그해 30% 확률로 움직인다.
/// - 떠나는 집에게 지배자는 보복하거나(공포 오름, 신용과 본 집들의 눈은 깎임) 놓아준다(신용 오름).
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, (int founded, float fear, float credit)> _rep = new();   // 지배자 집 → 공포, 신용
    private float[] _dues = new float[256];          // 집마다 올해 낸 것(공납, 헌금, 수업료)
    private float[] _lastDues = new float[256];      // 지난해 낸 것
    private Dictionary<int, (int fromLord, int all)>? _leaseFrom;   // 해마다: 집 → (그 집 주군 나무에게서 빌린 필지, 빌린 필지)

    private void EnsureDues()
    {
        int n = Households.HighWater;
        if (_dues.Length < n) { Array.Resize(ref _dues, Math.Max(n, _dues.Length * 2)); Array.Resize(ref _lastDues, _dues.Length); }
    }

    /// <summary>집 h 가 낸 것을 적는다(부담 셈).</summary>
    private void PayDues(int h, float amount)
    {
        if (h < 0 || amount <= 0f) return;
        EnsureDues();
        _dues[h] += amount;
    }

    public (float fear, float credit) RepOf(int lord)
    {
        if (_rep.TryGetValue(lord, out var r) && Households.IsAlive(lord) && Households.FoundedDay[lord] == r.founded) return (r.fear, r.credit);
        return (0.1f, 0.3f);
    }

    private void AddRep(int lord, float fear, float credit)
    {
        if (!Households.IsAlive(lord)) return;
        var (f, c) = RepOf(lord);
        _rep[lord] = (Households.FoundedDay[lord], Math.Clamp(f + fear, 0f, 1f), Math.Clamp(c + credit, 0f, 1f));
    }

    private void BuildLeaseFrom()
    {
        _leaseFrom = new();
        foreach (var (plot, t) in _tiller)
        {
            int owner = Map.Owner(plot);
            int lord = PatronOf(t);
            var x = _leaseFrom.GetValueOrDefault(t);
            bool fromLord = owner >= 0 && lord >= 0 && (owner == lord || InTreeOf(owner, lord));
            _leaseFrom[t] = (x.fromLord + (fromLord ? 1 : 0), x.all + 1);
        }
    }

    /// <summary>집 h 가 지배자 lord 에게 붙어 있는 까닭.</summary>
    public Belonging BelongOf(int h, int lord)
    {
        var hh = Households; var p = People;
        var b = new Belonging();
        if (!hh.IsAlive(h) || !hh.IsAlive(lord)) return b;
        int v = hh.Village[h];
        float lp = LordPower(lord);
        float guard = Config.ProtectShare * lp;
        b.Protect = MathF.Min(1f, DangerAt(v)) * guard / (guard + 2f);
        var lf = (_leaseFrom ??= BuildLeaseFromNow()).GetValueOrDefault(h);
        float living = lf.all > 0 ? 0.6f * lf.fromLord / lf.all : 0f;
        foreach (int m in MembersOf(h)) if (_employer.GetValueOrDefault(m, -1) == lord) { living += 0.3f; break; }
        foreach (var d in _debts) if (d.Debtor == h && d.Creditor == lord && hh.FoundedDay[h] == d.DebtorFounded) { living += 0.2f; break; }
        if (Bound(h)) living += 0.3f;
        b.Living = MathF.Min(1f, living);
        var (fear, credit) = RepOf(lord);
        b.Fear = fear * lp / (lp + HouseholdPower(h) + 0.5f);
        int head = HeadIdx(h), lh = HeadIdx(lord);
        if (head >= 0 && lh >= 0)
            b.Loyal = p.Fanatic[head] != 0 && p.LoyalUid[head] == p.Uid[lh] ? 1f
                    : p.LoyalUid[head] == p.Uid[lh] ? 0.6f * p.Loyalty[head] : Math.Clamp(Social.Opinion(p, head, p.Uid[lh]) / 200f, 0f, 0.3f);
        // 지배자가 이끄는 신앙 세력의 광신도인 가장은 떠나지 않는다
        if (head >= 0 && lh >= 0 && _devote.TryGetValue(p.Uid[head], out var dv) && dv.Zealot && GroupById(dv.Root) is { } fr && fr.LeaderUid == p.Uid[lh]) b.Loyal = 1f;
        int perks = 0;
        if (LordOrg(lord, false) is { } lo)
            foreach (var u in Subtree(lo)) if (u.Village == v && IsSchool(u)) perks++;
        perks += _facilities.Count(f => f.Alive && f.Village == v && LordOrg(lord, false) is { } lo2 && f.Owner == RootOf(lo2).Id);
        b.Benefit = MathF.Min(0.3f, 0.1f * perks) + 0.3f * credit;
        b.Habit = 0.3f * Established(v);
        var inc = IncomeOf(h);
        float income = MathF.Max(5f, inc.own + inc.tenant);
        EnsureDues();
        float paid = _lastDues[h] > 0f ? _lastDues[h] : TaxRate(lord) * income;
        b.Burden = MathF.Min(1.5f, 4f * paid / income);
        return b;
    }

    private Dictionary<int, (int, int)> BuildLeaseFromNow() { BuildLeaseFrom(); return _leaseFrom!; }

    /// <summary>다른 지배자 alt 를 섬길 때의 값(옮기는 비용 0.2를 뺀다). 생계와 관습은 새로 쌓아야 한다.</summary>
    private float AltValue(int h, int alt)
    {
        var hh = Households; var p = People;
        float lp = LordPower(alt), guard = Config.ProtectShare * lp;
        float protect = MathF.Min(1f, DangerAt(hh.Village[h])) * guard / (guard + 2f);
        int head = HeadIdx(h), ah = HeadIdx(alt);
        float like = head >= 0 && ah >= 0 ? Math.Clamp(Social.Opinion(p, head, p.Uid[ah]) / 200f, 0f, 0.5f) : 0f;
        return protect + like + 0.3f * RepOf(alt).credit - 4f * TaxRate(alt) - 0.2f;
    }

    /// <summary>
    /// 세력원 집 h(지배자 cur)의 한 해 판단. 남을 까닭이 다른 길보다 0.1 넘게 모자라면 30% 확률로 움직인다.
    /// 아무도 섬기지 않는 길이면 납부를 끊고, 다른 지배자가 낫으면 그리로 옮긴다.
    /// </summary>
    private void BelongDecide(int h, int cur, List<int> lords)
    {
        var hh = Households;
        var b = BelongOf(h, cur);
        float stay = b.Total;
        float best = 0f; int to = -1;   // 아무도 섬기지 않음
        foreach (int l in lords)
        {
            if (l == h || l == cur || Restrained(l) || WouldLoop(h, l)) continue;
            float dx = hh.X[l] - hh.X[h], dy = hh.Y[l] - hh.Y[h];
            if (dx * dx + dy * dy > Config.ProtectRange * Config.ProtectRange) continue;
            float a = AltValue(h, l);
            if (a > best) { best = a; to = l; }
        }
        if (best <= stay + 0.1f || !_rng.Chance(0.3f)) return;
        Stats.BelongLeft++;
        if (to >= 0) { _patron[h] = to; _patronFounded[h] = hh.FoundedDay[to]; Stats.BelongSwitched++; HeadsRelate(h, to, 5f, 0f, 0f, MemoryType.Protected, 5); }
        else _patron[h] = -1;
        LeaveResponse(h, cur);
    }

    /// <summary>떠나는 집에게: 거친 지배자는 보복하고(공포 +0.1, 신용 −0.05, 본 이웃의 눈 −3), 아니면 놓아준다(신용 +0.02).</summary>
    private void LeaveResponse(int h, int lord)
    {
        var hh = Households; var p = People;
        int lh = HeadIdx(lord), head = HeadIdx(h);
        if (lh < 0) return;
        if (IsPlayer(lh))
        {
            if (head >= 0) Chron(lh, $"{Subj(Names.Full(Lineage, p.Uid[head]))} 우리를 떠났다");
            return;
        }
        float lp = LordPower(lord);
        if (HeadTrait(lord, Trait.Aggression) > 0.6f && lp > 2f * HouseholdPower(h) && _rng.Chance(0.5f))
        {
            float take = 0.3f * MathF.Max(0f, hh.Food[h]);
            hh.Food[h] -= take; hh.Food[lord] += take;
            Casualty(h, HouseholdPower(h));
            HeadsRelate(h, lord, -30f, 30f, 0f, MemoryType.Punished, -30);
            if (hh.Village[h] >= 0)
                foreach (int o in VillageHouses(hh.Village[h]).Take(12))
                    if (o != h && HeadIdx(o) is int oh && oh >= 0) Social.Add(p, oh, p.Uid[lh], -3f, 3f);
            AddRep(lord, 0.1f, -0.05f);
            Stats.Retaliations++;
            if (head >= 0) Chron(head, $"{SubjName(head)} 섬기기를 그만두자 {Subj(Names.Full(Lineage, p.Uid[lh]))} 보복했다");
        }
        else AddRep(lord, 0f, 0.02f);
    }

    /// <summary>해마다(보호 고르기 앞): 낸 것을 넘기고, 평판이 식고, 빌린 땅을 센다.</summary>
    private void BelongYear()
    {
        EnsureDues();
        (_lastDues, _dues) = (_dues, _lastDues);
        Array.Clear(_dues);
        foreach (var k in _rep.Keys.ToList())
        {
            var r = _rep[k];
            if (!Households.IsAlive(k) || Households.FoundedDay[k] != r.founded) { _rep.Remove(k); continue; }
            // 공포는 식고, 세금을 무겁게 매기지 않은 지배자는 신용이 조금씩 쌓인다
            float c = r.credit * 0.95f + (TaxRate(k) <= Config.TributeRate + 0.001f ? 0.02f : 0f);
            _rep[k] = (r.founded, r.fear * 0.9f, Math.Clamp(c, 0f, 1f));
        }
        BuildLeaseFrom();
    }
}
