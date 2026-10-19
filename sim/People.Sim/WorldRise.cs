namespace People.Sim;

/// <summary>지배자가 눈여겨보는 아래 세력 하나. 집 번호와 세운 날로 같은 집인지 확인한다.</summary>
public sealed class Rival
{
    public int Lord, LordFounded, Riser, RiserFounded;
    public float Suspicion;
    public bool Stole;          // 내 집을 빼 갔다(끼어든 것보다 무겁다)
    public int Since;           // 처음 알아챈 날
}

/// <summary>NPC 지배자가 플레이어 집에 한 요구. 받거나 거절할 때까지 기다린다.</summary>
public sealed class Demand
{
    public int Id;
    public InteractionId Kind;
    public int Lord, LordFounded, Day;
}

/// <summary>
/// P5 오름의 다툼. 섬김의 바뀜을 해마다 지난해와 견준다.
/// - 내 집을 빼 가거나(빼 오기) 내 아래에서 따르는 집을 늘리면(끼어들기) 지배자가 알아챌 수 있다.
/// - 지배자는 아래 세력마다 의심을 쌓는다. 충성과 혼맥이 깎고, 해마다 식는다.
/// - 의심이 넘치면 상대 크기와 지배자 성격에 따라 반응한다: 끌어들이기, 경고, 흔들기, 서약 요구, 깎아내리기, 진압, 서임.
/// - 올라서는 쪽도 수가 있다: 스스로 서약하기, 주군의 주군에게 서약하기(머리 위로 가기).
/// 반응과 수는 모두 상호작용이라 플레이어도 쓴다. 플레이어 집에 온 요구는 플레이어가 받거나 거절한다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<(int lord, int riser), Rival> _rivals = new();
    private readonly Dictionary<int, int> _restrained = new();     // 경고를 받아들인 집 → 이 날까지 새 집을 받지 않는다
    private readonly List<Demand> _demands = new();
    private int _demandIds;
    private int[] _prevPatron = new int[256], _prevPatronOf = new int[256], _prevFounded = new int[256];
    private bool _riseSnapped;

    public IEnumerable<Rival> Rivals => _rivals.Values;
    public readonly Dictionary<(int trait, InteractionId act), (int n, int ok)> RiseActLedger = new();   // 진단용

    /// <summary>0 말솜씨, 1 신중, 2 공격성 가운데 가장 높은 것.</summary>
    private int DominantTrait(int i)
    {
        float a = People.Get(i, Trait.Charisma), b = People.Get(i, Trait.Caution), c = People.Get(i, Trait.Aggression);
        return a >= b && a >= c ? 0 : b >= c ? 1 : 2;
    }

    private int HeadIdx(int h) => Households.IsAlive(h) && People.IsValid(Households.Head[h]) ? Households.Head[h].Index : -1;

    public bool Restrained(int h) => _restrained.TryGetValue(h, out int until) && Day < until;

    private bool SameHouse(Rival r) =>
        Households.IsAlive(r.Lord) && Households.FoundedDay[r.Lord] == r.LordFounded && Households.IsAlive(r.Riser) && Households.FoundedDay[r.Riser] == r.RiserFounded;

    public Rival? RivalOf(int lord, int riser) => _rivals.TryGetValue((lord, riser), out var r) && SameHouse(r) ? r : null;

    /// <summary>집 h 가 lord 의 나무 안(섬김을 따라 올라가면 lord)인가.</summary>
    public bool InTreeOf(int h, int lord)
    {
        for (int x = PatronOf(h), d = 0; x >= 0 && d < 32; x = PatronOf(x), d++) if (x == lord) return true;
        return false;
    }

    /// <summary>R 집의 세력 크기(아래 집 수와 직접 따르는 집 가운데 큰 쪽).</summary>
    private int RiseSize(int r) => Math.Max(BelowOf(r), ClientCount(r));

    private float Notoriety(int h)
    {
        int head = HeadIdx(h);
        return (head >= 0 ? People.Notoriety[head] : 0f) + 0.02f * ClientCount(h);
    }

    /// <summary>바뀐 섬김을 지난해 기록에 맞춘다(지배자가 스스로 한 일: 분봉, 회수, 서약 받기 등).</summary>
    private void Resnap(int h)
    {
        EnsureRiseArrays();
        if (h < 0 || h >= _prevPatron.Length || !Households.IsAlive(h)) return;
        int p = PatronOf(h);
        _prevPatron[h] = p;
        _prevPatronOf[h] = p >= 0 ? Households.FoundedDay[p] : -1;
        _prevFounded[h] = Households.FoundedDay[h];
    }

    private void EnsureRiseArrays()
    {
        int n = Households.HighWater;
        if (_prevPatron.Length >= n) return;
        int c = Math.Max(n, _prevPatron.Length * 2);
        int old = _prevPatron.Length;
        Array.Resize(ref _prevPatron, c); Array.Resize(ref _prevPatronOf, c); Array.Resize(ref _prevFounded, c);
        for (int k = old; k < c; k++) { _prevPatron[k] = -1; _prevFounded[k] = -1; }
    }

    /// <summary>집 riser 의 가장을 lord 의 봉신으로 앉힌다(끌어들이기, 서약, 서임이 같이 쓴다).</summary>
    private void MakeVassal(int lord, int riser, Legitimacy legit)
    {
        var hh = Households;
        if (lord == riser || WouldLoop(riser, lord)) return;
        _patron[riser] = lord;
        _patronFounded[riser] = hh.FoundedDay[lord];
        EnsureRealmArrays();
        _legit[riser] = (byte)legit;
        _rivals.Remove((lord, riser));
        Resnap(riser);
        RecountClients();
    }

    // ------------------------------------------------------------------ 해마다

    private void RiseYear()
    {
        var hh = Households; var p = People;
        EnsureRiseArrays();
        EnsurePowerArrays();
        if (_riseSnapped) DetectRises();
        // 의심이 식고, 충성과 혼맥이 깎는다
        foreach (var key in _rivals.Keys.ToList())
        {
            var r = _rivals[key];
            if (!SameHouse(r) || ClientCount(r.Riser) == 0 || (PatronOf(r.Riser) == r.Lord && LegitOf(r.Riser) == Legitimacy.Granted)) { _rivals.Remove(key); continue; }
            r.Suspicion *= 0.85f;
            int lh = HeadIdx(r.Lord), rh = HeadIdx(r.Riser);
            if (lh >= 0 && rh >= 0)
            {
                if (p.LoyalUid[rh] == p.Uid[lh]) r.Suspicion -= 20f * MathF.Max(0f, p.Loyalty[rh] - 0.5f);   // 보통 충성은 의심을 깎지 않는다
                int chon = Lineage.Chon(p.Uid[lh], p.Uid[rh]);
                if (chon > 0 && chon <= 3) r.Suspicion -= 5f;
            }
            if (r.Suspicion < 1f) _rivals.Remove(key);
        }
        // 플레이어가 한 해 동안 답하지 않은 요구는 NPC처럼 정한다
        foreach (var d in _demands.ToList())
            if (Day - d.Day >= Config.DaysPerYear) AnswerDemand(d.Id, null);
        // 반응
        foreach (var r in _rivals.Values.ToList())
        {
            if (!SameHouse(r) || r.Suspicion < 15f) continue;
            int lh = HeadIdx(r.Lord), rh = HeadIdx(r.Riser);
            if (lh < 0 || rh < 0) continue;
            // 올라서는 쪽(NPC)이 먼저 수를 둘 수 있다
            if (!IsPlayer(rh) && RiserMove(r, rh)) continue;
            if (IsPlayer(lh) || !_rng.Chance(Math.Min(0.9f, 0.4f + 0.01f * r.Suspicion))) continue;
            var act = ChooseReaction(r, lh, rh);
            if (act == null) continue;
            Stats.RiseActs++;
            if (IsPlayer(rh) && act is InteractionId.RiseCoopt or InteractionId.RiseWarn or InteractionId.RiseOath or InteractionId.RiseInvest)
            {
                _demands.Add(new Demand { Id = ++_demandIds, Kind = act.Value, Lord = r.Lord, LordFounded = hh.FoundedDay[r.Lord], Day = Day });
                _fork = true;
                Chronicle.Add((Day, $"{Names.Full(Lineage, p.Uid[lh])}이(가) {DemandText(act.Value)}"));
                continue;
            }
            var res = Interact(act.Value, new InteractionContext { Actor = lh, Target = rh, Third = -1 });
            // 진단: 지배자의 두드러진 성격(말솜씨, 신중, 공격성)별로 무엇을 골랐고 됐는지
            int trait = DominantTrait(lh);
            var key = (trait, act.Value);
            var e = RiseActLedger.GetValueOrDefault(key);
            RiseActLedger[key] = (e.n + 1, e.ok + (res.Accepted ? 1 : 0));
        }
        foreach (var k in _restrained.Keys.ToList()) if (!Restrained(k)) _restrained.Remove(k);
        Snapshot();
        Stats.Rivals = _rivals.Count;
    }

    private void Snapshot()
    {
        var hh = Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) { _prevPatron[h] = -1; _prevFounded[h] = -1; continue; }
            Resnap(h);
        }
        _riseSnapped = true;
    }

    /// <summary>지난해와 견줘 새로 따르게 된 집마다 잃은 지배자(빼 오기)나 위의 지배자(끼어들기)가 알아챌지 굴린다.</summary>
    private void DetectRises()
    {
        var hh = Households; var p = People;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            int cur = PatronOf(h);
            if (cur < 0) continue;
            bool same = _prevFounded[h] == hh.FoundedDay[h];
            int prev = same && _prevPatron[h] >= 0 && hh.IsAlive(_prevPatron[h]) && hh.FoundedDay[_prevPatron[h]] == _prevPatronOf[h] ? _prevPatron[h] : -1;
            if (same && prev == cur) continue;
            int lord;
            bool stole;
            if (prev >= 0 && prev != cur && !InTreeOf(cur, prev) && ClientCount(prev) + 1 >= 1) { lord = prev; stole = true; }
            else { lord = PatronOf(cur); stole = false; }
            if (lord < 0 || lord == cur || InTreeOf(lord, cur)) continue;
            if (RiseSize(lord) < RiseSize(cur)) continue;   // 아래에서 올라오는 세력만
            // 알아챔: 그 마을에서 지배자에게 충성하는 가장, 징수관, 악명, 거리
            int lh = HeadIdx(lord);
            if (lh < 0) continue;
            int lu = p.Uid[lh], loyal = 0, v = hh.Village[h];
            for (int k = 0; k < 24; k++)
            {
                int x = _rng.NextInt(hh.HighWater);
                if (hh.IsAlive(x) && hh.Village[x] == v && HeadIdx(x) is int xh && xh >= 0 && p.LoyalUid[xh] == lu && p.Loyalty[xh] > 0.5f) loyal++;
            }
            float dist = MathF.Sqrt(Dist2(lord, cur));
            float notice = 0.2f + 0.06f * MathF.Min(6, loyal) + (_collectorsOf.GetValueOrDefault(lord) > 0 ? 0.15f : 0f)
                         + 0.3f * Notoriety(cur) - 0.4f * MathF.Min(1f, dist / Config.VassalRange);
            if (!_rng.Chance(Math.Clamp(notice, 0.05f, 0.95f))) continue;
            Stats.RiseNoticed++;
            if (!_rivals.TryGetValue((lord, cur), out var r) || !SameHouse(r))
                _rivals[(lord, cur)] = r = new Rival { Lord = lord, LordFounded = hh.FoundedDay[lord], Riser = cur, RiserFounded = hh.FoundedDay[cur], Since = Day };
            r.Stole |= stole;
            r.Suspicion += 8f * (1f + 3f * RiseSize(cur) / MathF.Max(5f, RiseSize(lord))) * (stole ? 1.5f : 1f);
            int rh = HeadIdx(cur);
            if (rh >= 0) Chron(rh, $"{Names.Full(Lineage, lu)}이(가) 우리 집이 세를 늘리는 것을 알아챘다");
        }
    }

    /// <summary>NPC 지배자의 반응: 상대 크기에 따라 할 수 있는 일 가운데 성격, 힘, 관계로 무게를 매겨 고른다.</summary>
    private InteractionId? ChooseReaction(Rival r, int lh, int rh)
    {
        var p = People;
        int size = RiseSize(r.Riser);
        float cha = p.Get(lh, Trait.Charisma), cau = p.Get(lh, Trait.Caution), agg = p.Get(lh, Trait.Aggression);
        float lp = LordPower(r.Lord), rp = RisePower(r.Riser);
        float ratio = lp / (lp + rp + 0.01f);
        float rel = Math.Clamp(Social.Opinion(p, lh, p.Uid[rh]) / 50f, -1f, 1f);
        var w = new List<(InteractionId id, float w)>();
        void Add(InteractionId id, float x) { if (x > 0f) w.Add((id, x)); }
        bool near = size >= (int)(0.7f * TierUp[2]) || TierOf(r.Riser) >= 2;
        if (size < 5)
        {
            w.Add((InteractionId.Count, 1.2f - 0.6f * agg));   // 내버려 둔다
            Add(InteractionId.RiseCoopt, 0.2f + 1.2f * cha * cha + 0.3f * rel);
            Add(InteractionId.RiseWarn, 0.8f * agg * agg);
        }
        else if (size < 10)
        {
            Add(InteractionId.RiseWarn, 1.2f * cau * cau + 0.6f * agg * agg + 0.1f);
            Add(InteractionId.RiseSway, 1.2f * cha * cha + 0.1f);
            Add(InteractionId.RiseCoopt, 1.0f * cha * cha + 0.3f * rel);
            Add(InteractionId.RiseOath, 0.6f * cau * cau);
        }
        else if (!near)
        {
            Add(InteractionId.RiseOath, 1.5f * cau * cau + 0.1f);
            Add(InteractionId.RiseSlander, 1.5f * agg * (1f - p.Piety[lh]) * (1f - cha) - 0.3f * rel);
            Add(InteractionId.RiseSway, 1.2f * cha * cha + 0.1f);
            Add(InteractionId.RiseInvest, 0.8f * cha * cha + 0.3f * rel);
            Add(InteractionId.RiseSuppress, 1.5f * agg * agg * Math.Clamp((ratio - 0.4f) * 3f, 0f, 1.5f) - 0.3f * rel);
        }
        else
        {
            // 문턱 근처: 성격이 크게 가른다. 공격성은 이길 만하면 누르고, 말솜씨는 서임하고, 신중은 서약을 받는다
            float edge = Math.Clamp((ratio - 0.3f) * 3f, 0f, 1.5f);
            Add(InteractionId.RiseSuppress, 2f * agg * agg * edge + 0.1f - 0.4f * rel);
            Add(InteractionId.RiseInvest, 1.5f * cha * cha + 0.4f * cau * (1f - ratio) + 0.3f * rel);
            Add(InteractionId.RiseOath, 1.5f * cau * cau + 0.1f);
        }
        // 같은 해에 할 수 없는 일(이미 봉신이 된 집에 서임 등)은 뺀다
        var c = new InteractionContext { Actor = lh, Target = rh, Third = -1 };
        w.RemoveAll(x => x.id != InteractionId.Count && !Def(x.id).Can(this, c));
        float tot = w.Sum(x => x.w);
        if (tot <= 0f) return null;
        float roll = _rng.NextFloat() * tot;
        foreach (var x in w) { roll -= x.w; if (roll <= 0f) return x.id == InteractionId.Count ? null : x.id; }
        return null;
    }

    /// <summary>올라서는 집(NPC)의 수: 의심이 커지면 신중한 가장은 스스로 서약하고, 야심찬 가장은 머리 위로 간다.</summary>
    private bool RiserMove(Rival r, int rh)
    {
        var p = People;
        if (r.Suspicion < 30f) return false;
        int lh = HeadIdx(r.Lord);
        var c = new InteractionContext { Actor = rh, Target = lh, Third = -1 };
        if (_rng.Chance(0.15f * p.Get(rh, Trait.Caution)) && Def(InteractionId.RiseSwear).Can(this, c))
        {
            Interact(InteractionId.RiseSwear, c);
            return true;
        }
        int g = PatronOf(r.Lord), gh = HeadIdx(g);
        if (gh >= 0 && _rng.Chance(0.06f * p.Get(rh, Trait.Ambition)))
        {
            var c2 = new InteractionContext { Actor = rh, Target = gh, Third = -1 };
            if (Def(InteractionId.RiseOverHead).Can(this, c2)) { Interact(InteractionId.RiseOverHead, c2); return true; }
        }
        return false;
    }

    /// <summary>올라서는 세력의 힘: 그 집 + 고용한 군인 + 따르는 집 장정의 절반.</summary>
    private float RisePower(int r)
    {
        float s = LordPower(r);
        var hh = Households;
        int n = 0;
        for (int h = 0; h < hh.HighWater && n < 60; h++)
            if (hh.IsAlive(h) && PatronOf(h) == r) { s += 0.5f * HouseholdPower(h); n++; }
        return s;
    }

    // ------------------------------------------------------------------ 상호작용

    /// <summary>지배자 쪽 수를 쓸 수 있나: 대상 집이 따르는 집을 거느리고, 내가 그를 눈여겨보거나 그가 내 나무 안에 있다.</summary>
    private bool CanRule(InteractionContext c)
    {
        var p = People;
        int a = p.Household[c.Actor], t = p.Household[c.Target];
        if (a == t || HeadIdx(a) != c.Actor || HeadIdx(t) != c.Target || ClientCount(t) == 0) return false;
        if (PatronOf(a) == t || InTreeOf(a, t)) return false;   // 내 위에는 쓰지 않는다(P7)
        return RivalOf(a, t) != null || InTreeOf(t, a);
    }

    private float PowerRatio(int a, int b)
    {
        float x = LordPower(a), y = RisePower(b);
        return x / (x + y + 0.01f);
    }

    private void Suspect(int lord, int riser, float add)
    {
        var r = RivalOf(lord, riser);
        if (r == null)
        {
            if (!Households.IsAlive(lord) || !Households.IsAlive(riser)) return;
            _rivals[(lord, riser)] = r = new Rival { Lord = lord, LordFounded = Households.FoundedDay[lord], Riser = riser, RiserFounded = Households.FoundedDay[riser], Since = Day };
        }
        r.Suspicion = MathF.Max(0f, r.Suspicion + add);
    }

    private void AddRiseDefs(InteractionDef[] d)
    {
        int H(World w, int i) => w.People.Household[i];
        d[(int)InteractionId.RiseCoopt] = new InteractionDef
        {
            Id = InteractionId.RiseCoopt, Name = "끌어들이기", Category = InteractionCategory.Rule,
            Can = (w, c) => w.CanRule(c) && w.PatronOf(H(w, c.Target)) != H(w, c.Actor) && w.RiseSize(H(w, c.Target)) < 20,
            // 자리를 주고 내 사람으로 만든다. 욕심이 작고 내가 셀수록 받는다
            Accept = (w, c) => Math.Clamp(0.25f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 100f + 0.3f * (1f - w.People.Get(c.Target, Trait.Ambition))
                                          + 0.3f * w.PowerRatio(H(w, c.Actor), H(w, c.Target)) - 0.02f * w.ClientCount(H(w, c.Target)), 0.03f, 0.95f),
            Success = (w, c, _) =>
            {
                w.MakeVassal(H(w, c.Actor), H(w, c.Target), Legitimacy.Granted);
                Social.AddFrom(w.People, c.Target, c.Actor, 10f);
                w.Stats.Coopted++;
                w.RiseChron(c, "끌어들여 봉신으로 삼았다", "자리를 받고 봉신이 되었다");
            },
            Fail = (w, c) => w.Suspect(H(w, c.Actor), H(w, c.Target), 10f),
        };
        d[(int)InteractionId.RiseWarn] = new InteractionDef
        {
            Id = InteractionId.RiseWarn, Name = "경고", Category = InteractionCategory.Rule,
            Can = (w, c) => w.CanRule(c),
            // 받아들이면 두 해 동안 새 집을 받지 않는다
            Accept = (w, c) => Math.Clamp(0.3f + 0.4f * w.PowerRatio(H(w, c.Actor), H(w, c.Target)) + 0.2f * w.People.Get(c.Target, Trait.Caution)
                                          - 0.3f * w.People.Get(c.Target, Trait.Ambition), 0.05f, 0.95f),
            Success = (w, c, _) =>
            {
                w._restrained[H(w, c.Target)] = w.Day + 2 * w.Config.DaysPerYear;
                Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -5f, 20f);
                if (w.RivalOf(H(w, c.Actor), H(w, c.Target)) is { } r) r.Suspicion *= 0.5f;
                w.Stats.Warned++;
                w.RiseChron(c, "경고했고, 상대가 물러섰다", "경고를 받고 두 해 동안 새 집을 받지 않기로 했다");
            },
            Fail = (w, c) =>
            {
                Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -10f);
                w.Suspect(H(w, c.Actor), H(w, c.Target), 15f);
            },
        };
        d[(int)InteractionId.RiseSway] = new InteractionDef
        {
            // 상대를 따르는 집에 공납을 깎아 주고 내 쪽으로 오라고 흔든다. 받아들일 확률 대신 해낼 확률
            Id = InteractionId.RiseSway, Name = "따르는 집 흔들기", Category = InteractionCategory.Rule,
            Can = (w, c) => w.CanRule(c),
            Accept = (w, c) => Math.Clamp(0.3f + 0.5f * w.People.Get(c.Actor, Trait.Charisma), 0.1f, 0.9f),
            Success = (w, c, r) =>
            {
                var hh = w.Households; var p = w.People;
                int a = H(w, c.Actor), t = H(w, c.Target), n = 0;
                for (int h = 0; h < hh.HighWater; h++)
                {
                    if (!hh.IsAlive(h) || w.PatronOf(h) != t || h == a) continue;
                    int hd = w.HeadIdx(h);
                    float loyal = hd >= 0 && p.LoyalUid[hd] == p.Uid[c.Target] ? p.Loyalty[hd] : 0.5f;
                    if (loyal > 0.6f || !w._rng.Chance(0.4f)) continue;
                    w._patron[h] = a;
                    w._patronFounded[h] = hh.FoundedDay[a];
                    w.Resnap(h);
                    if (hd >= 0) Social.Add(p, hd, p.Uid[c.Actor], 10f);
                    n++;
                }
                w.RecountClients();
                Social.Add(p, c.Target, p.Uid[c.Actor], -20f);
                r.Amount = n;
                w.Stats.Swayed += n;
                w.RiseChron(c, $"따르는 집 {n}곳을 내 쪽으로 돌렸다", $"따르는 집 {n}곳을 빼앗겼다");
            },
            Fail = (w, c) => Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -10f),
        };
        d[(int)InteractionId.RiseOath] = new InteractionDef
        {
            Id = InteractionId.RiseOath, Name = "서약 요구", Category = InteractionCategory.Rule,
            Can = (w, c) => w.CanRule(c) && !(w.PatronOf(H(w, c.Target)) == H(w, c.Actor) && w.LegitOf(H(w, c.Target)) == Legitimacy.Granted),
            Accept = (w, c) => Math.Clamp(0.2f + 0.5f * w.PowerRatio(H(w, c.Actor), H(w, c.Target)) + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 150f
                                          + 0.2f * w.People.Get(c.Target, Trait.Caution) - 0.3f * w.People.Get(c.Target, Trait.Ambition), 0.03f, 0.95f),
            Success = (w, c, _) =>
            {
                w.MakeVassal(H(w, c.Actor), H(w, c.Target), Legitimacy.Granted);
                Social.AddFrom(w.People, c.Target, c.Actor, 15f);
                w.Stats.OathsTaken++;
                w.RiseChron(c, "서약을 받았다", "서약하고 봉신이 되었다");
            },
            Fail = (w, c) =>
            {
                Social.Add(w.People, c.Actor, w.People.Uid[c.Target], -20f);
                w.Suspect(H(w, c.Actor), H(w, c.Target), 25f);
                w.RiseChron(c, "서약을 요구했으나 거절당했다", "서약 요구를 거절했다");
            },
        };
        d[(int)InteractionId.RiseSlander] = new InteractionDef
        {
            // 험담과 누명으로 상대의 이름을 깎는다. 해낼 확률. 들키면 내 이름이 깎인다
            Id = InteractionId.RiseSlander, Name = "깎아내리기", Category = InteractionCategory.Scheme, Harsh = true, Secret = true,
            Can = (w, c) => w.CanRule(c),
            Accept = (w, c) => Math.Clamp(0.4f + 0.4f * w.People.Get(c.Actor, Trait.Charisma) - 0.3f * w.People.Honor[c.Target], 0.1f, 0.9f),
            Success = (w, c, _) =>
            {
                var hh = w.Households; var p = w.People;
                int t = H(w, c.Target);
                p.Honor[c.Target] -= 0.1f;
                for (int h = 0; h < hh.HighWater; h++)
                    if (hh.IsAlive(h) && w.PatronOf(h) == t && w.HeadIdx(h) is int hd && hd >= 0) Social.Add(p, hd, p.Uid[c.Target], -10f);
                w.Stats.Slanders++;
                w.RiseChron(c, "상대의 이름을 깎았다", "나쁜 소문이 돌아 따르는 집들의 눈이 차가워졌다");
            },
            Fail = (w, c) =>
            {
                Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -30f);
                w.People.Honor[c.Actor] -= 0.05f;
            },
        };
        d[(int)InteractionId.RiseSuppress] = new InteractionDef
        {
            // 무력 진압. 받아들일 확률 대신 이길 확률. 지면 상대가 홀로 서고 내 집들이 흔들린다
            Id = InteractionId.RiseSuppress, Name = "무력 진압", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) => w.CanRule(c) && w.RiseSize(H(w, c.Target)) >= 5,
            Accept = (w, c) => Math.Clamp(w.PowerRatio(H(w, c.Actor), H(w, c.Target)) * 1.1f - 0.05f, 0.05f, 0.95f),
            Success = (w, c, _) => w.SuppressWon(c.Actor, c.Target),
            Fail = (w, c) => w.SuppressLost(c.Actor, c.Target),
        };
        d[(int)InteractionId.RiseInvest] = new InteractionDef
        {
            // 서임: 영주로 인정하고 봉신으로 받는다
            Id = InteractionId.RiseInvest, Name = "서임", Category = InteractionCategory.Rule,
            Can = (w, c) => w.CanRule(c) && w.RiseSize(H(w, c.Target)) >= 10 && w.LegitOf(H(w, c.Target)) != Legitimacy.Granted,
            Accept = (w, c) => Math.Clamp(0.6f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 200f + 0.2f * (1f - w.People.Get(c.Target, Trait.Ambition))
                                          + 0.2f * w.PowerRatio(H(w, c.Actor), H(w, c.Target)), 0.1f, 0.97f),
            Success = (w, c, _) =>
            {
                w.MakeVassal(H(w, c.Actor), H(w, c.Target), Legitimacy.Granted);
                Social.AddFrom(w.People, c.Target, c.Actor, 30f);
                w.Stats.Invested++;
                w.RiseChron(c, "영주로 서임했다", "영주로 서임받았다");
            },
            Fail = (w, c) => w.Suspect(H(w, c.Actor), H(w, c.Target), 10f),
        };
        d[(int)InteractionId.RiseSwear] = new InteractionDef
        {
            // 올라서는 쪽: 스스로 서약해 봉신이 된다(의심을 끄는 수)
            Id = InteractionId.RiseSwear, Name = "서약하기", Category = InteractionCategory.Rule,
            Can = (w, c) =>
            {
                int a = H(w, c.Actor), t = H(w, c.Target);
                return a != t && w.HeadIdx(a) == c.Actor && w.HeadIdx(t) == c.Target && w.ClientCount(a) > 0 && w.ClientCount(t) >= 3
                       && !(w.PatronOf(a) == t && w.LegitOf(a) == Legitimacy.Granted) && !w.InTreeOf(t, a) && w.RiseSize(t) > w.RiseSize(a);
            },
            Accept = (w, c) => Math.Clamp(0.55f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 100f
                                          - 0.3f * (1f - w.PowerRatio(H(w, c.Target), H(w, c.Actor))), 0.05f, 0.97f),
            Success = (w, c, _) =>
            {
                w.MakeVassal(H(w, c.Target), H(w, c.Actor), Legitimacy.Granted);
                Social.AddFrom(w.People, c.Target, c.Actor, 10f);
                w.Stats.SworeUp++;
                w.RiseChron(c, "서약하고 봉신이 되었다", "서약을 받았다");
            },
        };
        d[(int)InteractionId.RiseOverHead] = new InteractionDef
        {
            // 머리 위로 가기: 내 주군의 주군에게 바로 서약한다. 옛 주군과 원수가 된다
            Id = InteractionId.RiseOverHead, Name = "주군의 주군에게 서약", Category = InteractionCategory.Rule,
            Can = (w, c) =>
            {
                int a = H(w, c.Actor), t = H(w, c.Target);
                int l = w.PatronOf(a);
                return l >= 0 && w.PatronOf(l) == t && w.HeadIdx(a) == c.Actor && w.HeadIdx(t) == c.Target && w.ClientCount(a) > 0;
            },
            Accept = (w, c) =>
            {
                var p = w.People;
                int l = w.PatronOf(H(w, c.Actor)), lh = w.HeadIdx(l);
                float grudge = lh >= 0 && Social.Opinion(p, c.Target, p.Uid[lh]) < 0f ? 0.3f : 0f;
                return Math.Clamp(0.3f + Social.Opinion(p, c.Target, p.Uid[c.Actor]) / 100f + grudge + 0.02f * w.ClientCount(H(w, c.Actor)), 0.05f, 0.95f);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                int a = H(w, c.Actor), l = w.PatronOf(a), lh = w.HeadIdx(l);
                w.MakeVassal(H(w, c.Target), a, Legitimacy.Granted);
                if (lh >= 0) { Social.Add(p, lh, p.Uid[c.Actor], -40f); Social.Remember(p, lh, MemoryType.Betrayed, p.Uid[c.Actor], w.Day, -50); }
                w.Suspect(l, a, 30f);
                w.Stats.OverHead++;
                w.RiseChron(c, "옛 주군을 건너뛰어 주군의 주군에게 서약했다", "봉신의 봉신에게서 직접 서약을 받았다");
            },
            Fail = (w, c) =>
            {
                // 거절당하면 옛 주군이 알게 된다
                int a = H(w, c.Actor), l = w.PatronOf(a);
                if (l >= 0) w.Suspect(l, a, 20f);
            },
        };
    }

    private void RiseChron(InteractionContext c, string actorText, string targetText)
    {
        var p = People;
        Chron(c.Actor, $"{SubjName(c.Actor)} {Names.Full(Lineage, p.Uid[c.Target])}을(를) 두고: {actorText}");
        Chron(c.Target, $"{SubjName(c.Target)} {Names.Full(Lineage, p.Uid[c.Actor])}에게서: {targetText}");
    }

    /// <summary>진압에 이겼다: 상대를 따르는 집이 내게 오고, 상대는 내 아래로 들어오며 벌을 받는다.</summary>
    private void SuppressWon(int lh, int rh)
    {
        var hh = Households; var p = People;
        int a = p.Household[lh], t = p.Household[rh];
        int n = 0;
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && PatronOf(h) == t) { _patron[h] = a; _patronFounded[h] = hh.FoundedDay[a]; Resnap(h); n++; }
        Casualty(t, HouseholdPower(t));
        Stats.RiseSuppressWins++;
        RiseChron(new InteractionContext { Actor = lh, Target = rh }, $"무력으로 눌렀다(집 {n}곳을 거둠)", $"무력 진압에 져서 집 {n}곳을 잃었다");
        if (p.IsAliveIndex(rh))
        {
            Social.Add(p, rh, p.Uid[lh], -40f, 40f);
            if (Severity(a) > 0.7f && _rng.Chance(0.5f)) { Stats.Executions++; Die(rh, false); }
            else p.Honor[rh] -= 0.3f;
        }
        if (hh.IsAlive(t) && !WouldLoop(t, a)) { _patron[t] = a; _patronFounded[t] = hh.FoundedDay[a]; EnsureRealmArrays(); _legit[t] = 0; Resnap(t); }
        _rivals.Remove((a, t));
        RecountClients();
    }

    /// <summary>진압에 졌다: 상대가 홀로 서고(참주), 내 이름이 깎이고, 근처 내 집 몇이 상대에게 간다.</summary>
    private void SuppressLost(int lh, int rh)
    {
        var hh = Households; var p = People;
        int a = p.Household[lh], t = p.Household[rh];
        Casualty(a, LordPower(a));
        if (PatronOf(t) == a || InTreeOf(t, a)) { _patron[t] = -1; Resnap(t); }
        EnsureRealmArrays();
        _legit[t] = (byte)Legitimacy.Self;
        if (p.IsAliveIndex(lh)) p.Honor[lh] -= 0.1f;
        Social.Add(p, rh, p.Uid[lh], -60f);
        if (p.IsAliveIndex(lh)) Social.Add(p, lh, p.Uid[rh], -60f);
        int moved = 0;
        for (int h = 0; h < hh.HighWater && moved < 5; h++)
        {
            if (!hh.IsAlive(h) || PatronOf(h) != a || h == t || Dist2(h, t) > Config.ProtectRange * Config.ProtectRange) continue;
            if (!_rng.Chance(0.3f * LeaveFactor(h))) continue;
            _patron[h] = t; _patronFounded[h] = hh.FoundedDay[t]; Resnap(h);
            moved++;
        }
        _rivals.Remove((a, t));
        Stats.Tyrants++;
        RecountClients();
        RiseChron(new InteractionContext { Actor = lh, Target = rh }, $"무력 진압에 실패했다(집 {moved}곳이 떠남)", "진압을 물리치고 홀로 섰다");
    }

    // ------------------------------------------------------------------ 플레이어에게 온 요구

    public IEnumerable<Demand> Demands => _demands.Where(d => Households.IsAlive(d.Lord) && Households.FoundedDay[d.Lord] == d.LordFounded);

    private static string DemandText(InteractionId k) => k switch
    {
        InteractionId.RiseCoopt => "자리를 줄 테니 봉신이 되라고 한다",
        InteractionId.RiseWarn => "더는 집을 끌어들이지 말라고 경고한다",
        InteractionId.RiseOath => "서약하라고 요구한다",
        _ => "영주로 서임하겠다고 한다",
    };

    public string DemandTextOf(Demand d) => DemandText(d.Kind);

    /// <summary>요구에 답한다. accept 가 null 이면 NPC처럼 확률로 정한다.</summary>
    public bool AnswerDemand(int id, bool? accept)
    {
        var d = _demands.FirstOrDefault(x => x.Id == id);
        if (d == null) return false;
        _demands.Remove(d);
        int lh = HeadIdx(d.Lord), me = HeadIdx(PlayerHousehold);
        if (lh < 0 || me < 0 || !Households.IsAlive(d.Lord) || Households.FoundedDay[d.Lord] != d.LordFounded) return false;
        var def = Def(d.Kind);
        var c = new InteractionContext { Actor = lh, Target = me, Third = -1 };
        if (!def.Can(this, c)) return false;
        bool yes = accept ?? _rng.Chance(def.Accept(this, c));
        if (yes) def.Success?.Invoke(this, c, new InteractionResult { Accepted = true });
        else def.Fail?.Invoke(this, c);
        return true;
    }
}
