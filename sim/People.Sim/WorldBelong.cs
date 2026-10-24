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
        // 신분 질서 교리: 같은 종교를 믿는 지배자를 더 섬긴다
        if (head >= 0 && lh >= 0 && ReligionOf(head) is { } hr && ReligionOf(lh)?.Id == hr.Id) b.Loyal = MathF.Min(1f, b.Loyal + 0.1f * DoctrineOn(head, Doctrine.Order));
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

    // ------------------------------------------------------------------ 떠남의 값(고증)
    // 당대에 섬길 주인을 바꾸기 어려웠던 까닭을 저울에 넣는다.
    // - 땅: 부치는 땅은 주인의 땅이다. 주인을 바꾸면 그 땅을 두고 떠나야 하고, 옛 주인 나무는 다섯 해 동안 그 집에 땅을 빌려주지 않는다.
    // - 매인 몸: 대대로 매인 집(Bound)이 떠나면 도망이다. 옛 주인이 새 주인보다 세면 데려오고 벌한다.
    // - 떠날 때 내는 값: 곳간의 20%를 옛 주인에게 두고 간다.
    // - 서약: 같은 종교의 주인을 버리는 것은 맹세를 깨는 일이다. 신앙이 깊을수록 무겁고, 버리면 명예가 깎인다.
    // - 친족과 마을: 같은 마을에서 같은 주인을 섬기는 3촌 안 친족 집을 두고 간다.
    // - 받아 주는 쪽: 남의 사람을 받는 것은 그 주인과 원수지는 일이다. 옛 주인 힘의 0.7이 안 되는 영주는 받지 않는다. 받으면 옛 주인의 눈이 나빠진다.
    private readonly Dictionary<int, (int lord, int founded, int day)> _leftLord = new();   // 집 → 떠나온 주인(다섯 해 동안 그 나무의 땅을 빌리지 못한다)

    /// <summary>떠나온 주인 나무의 땅이라 빌릴 수 없나(다섯 해).</summary>
    private bool Shunned(int t, int owner)
    {
        if (!_leftLord.TryGetValue(t, out var x) || Day - x.day >= 5 * Config.DaysPerYear) return false;
        return owner == x.lord || InTreeOf(owner, x.lord);
    }

    /// <summary>집 h 가 주인 cur 을 떠나는 값(0~1.6쯤). 고증 까닭의 합.</summary>
    public float ExitCost(int h, int cur)
    {
        var hh = Households; var p = People;
        var lf = (_leaseFrom ??= BuildLeaseFromNow()).GetValueOrDefault(h);
        float land = lf.all > 0 ? 0.5f * lf.fromLord / lf.all : 0f;
        float bound = Bound(h) ? 0.4f : 0f;
        int head = HeadIdx(h), lh = HeadIdx(cur);
        float oath = head >= 0 && lh >= 0 && ReligionOf(head) is { } r && ReligionOf(lh)?.Id == r.Id ? 0.3f * p.Piety[head] : 0f;
        int kin = 0;
        int v = hh.Village[h];
        if (head >= 0 && v >= 0)
            foreach (int o in VillageHouses(v))
            {
                if (o == h || PatronOf(o) != cur) continue;
                int oh = HeadIdx(o);
                if (oh < 0) continue;
                int c = Lineage.Chon(p.Uid[head], p.Uid[oh]);
                if (c > 0 && c <= 3 && ++kin >= 5) break;
            }
        return land + bound + oath + 0.06f * kin + 0.1f;   // 0.1: 떠날 때 내는 값과 길의 위험
    }

    /// <summary>
    /// 세력원 집 h(지배자 cur)의 한 해 판단. 다른 길의 값에서 떠남의 값을 뺀 것이 남을 까닭보다 0.1 넘게 크면 30% 확률로 움직인다.
    /// 아무도 섬기지 않는 길이면 납부를 끊고, 다른 지배자가 낫으면 그리로 옮긴다.
    /// </summary>
    private void BelongDecide(int h, int cur, List<int> lords)
    {
        var hh = Households;
        var b = BelongOf(h, cur);
        float stay = b.Total;
        float exit = ExitCost(h, cur);
        float best = 0f; int to = -1;   // 아무도 섬기지 않음
        float curPow = LordPower(cur);
        foreach (int l in lords)
        {
            if (l == h || l == cur || Restrained(l) || WouldLoop(h, l)) continue;
            float dx = hh.X[l] - hh.X[h], dy = hh.Y[l] - hh.Y[h];
            if (dx * dx + dy * dy > Config.ProtectRange * Config.ProtectRange) continue;
            if (LordPower(l) < 0.7f * curPow) continue;   // 약한 영주는 남의 사람을 받아 원수지지 않는다
            float a = AltValue(h, l);
            if (a > best) { best = a; to = l; }
        }
        if (best - exit <= stay + 0.1f || !_rng.Chance(0.3f)) return;
        var p = People;
        int head = HeadIdx(h), lh = HeadIdx(cur);
        // 매인 집의 도망: 옛 주인이 새 주인보다 세면 뒤쫓아 데려온다
        if (Bound(h) && to >= 0 && curPow > LordPower(to) && _rng.Chance(0.5f))
        {
            Stats.FugitivesReturned++;
            Casualty(h, 0.5f * HouseholdPower(h));
            if (head >= 0 && lh >= 0) { HeadsRelate(h, cur, -20f, 20f, 0f, MemoryType.Punished, -20); Chron(head, $"{SubjName(head)} {Obj(Names.Full(Lineage, p.Uid[lh]))} 떠나려다 붙잡혀 돌아왔다"); }
            if (HeadIdx(to) is int th && th >= 0 && lh >= 0) Social.Add(p, lh, p.Uid[th], -10f);
            return;
        }
        Stats.BelongLeft++;
        // 떠날 때 내는 값, 서약을 깬 값
        float fee = 0.2f * MathF.Max(0f, hh.Food[h]);
        hh.Food[h] -= fee; hh.Food[cur] += fee;
        if (head >= 0 && lh >= 0 && ReligionOf(head) is { } r && ReligionOf(lh)?.Id == r.Id) p.Honor[head] -= 0.05f;
        _leftLord[h] = (cur, hh.FoundedDay[cur], Day);
        if (to >= 0)
        {
            _patron[h] = to; _patronFounded[h] = hh.FoundedDay[to]; Stats.BelongSwitched++; HeadsRelate(h, to, 5f, 0f, 0f, MemoryType.Protected, 5);
            if (HeadIdx(to) is int th && th >= 0 && lh >= 0) Social.Add(p, lh, p.Uid[th], -10f);   // 내 사람을 받아 갔다
        }
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
        foreach (var k in _lordRep.Keys.ToList())
        {
            var r = _lordRep[k];
            if (!Households.IsAlive(k) || Households.FoundedDay[k] != r.founded) { _lordRep.Remove(k); continue; }
            // 공포는 식고, 세금을 무겁게 매기지 않은 지배자는 평판이 조금씩 쌓인다(신용 = 0.3 + 평판)
            float c = (0.3f + r.standing) * 0.95f + (TaxRate(k) <= Config.TributeRate + 0.001f ? 0.02f : 0f);
            _lordRep[k] = (r.founded, r.fame, Math.Clamp(c - 0.3f, -1f, 1f), r.dread * 0.9f);
        }
        BuildLeaseFrom();
    }
}
