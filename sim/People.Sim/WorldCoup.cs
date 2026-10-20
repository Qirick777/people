namespace People.Sim;

/// <summary>몰래 꾸미는 쿠데타 하나. 꾸미는 사람(uid)과 그 집, 노리는 지배자 집. 집 번호와 세운 날로 같은 집인지 확인한다.</summary>
public sealed class Plot
{
    public int Id, Plotter, PlotterHouse, PlotterHouseFounded, Lord, LordFounded, Since;
    public readonly HashSet<int> Backers = new();   // 편든 사람 uid(봉신 가장, 갈래 우두머리, 측근)
    public readonly HashSet<int> Known = new();     // 아는 사람 uid(꾸미는 사람 포함)
    public bool Exposed;                            // 지배자가 알았다(플레이어 지배자의 답을 기다린다)
}

/// <summary>
/// O4 쿠데타와 독립(plan_orgs 14장).
/// - 쿠데타: 지배자 L의 봉신 가장, L 조직의 측근과 갈래 우두머리가 몰래 꾸민다. 편을 모으고, 새어 나가면 숙청당하고,
///   이길 만하면 거사한다. 크게 이기면 L의 자리를 넘겨받고, 비등하면 내전 끝에 떨어져 나가고, 지면 벌을 받는다.
/// - 독립: 봉신이 드러내 놓고 선언한다. 주군은 진압, 협상, 체념 가운데 고른다.
/// 둘 다 상호작용이라 플레이어도 쓴다. 플레이어 지배자에게는 요구로 온다.
/// </summary>
public sealed partial class World
{
    private readonly List<Plot> _plots = new();
    private int _plotIds;

    public IEnumerable<Plot> Plots => _plots.Where(PlotLive);

    private bool PlotLive(Plot x) =>
        Households.IsAlive(x.PlotterHouse) && Households.FoundedDay[x.PlotterHouse] == x.PlotterHouseFounded
        && Households.IsAlive(x.Lord) && Households.FoundedDay[x.Lord] == x.LordFounded && FindAlive(x.Plotter) >= 0;

    private Plot? PlotOf(int plotterHouse) => _plots.FirstOrDefault(x => x.PlotterHouse == plotterHouse && PlotLive(x));

    private Plot? PlotBy(int plotterHouse, int lord) => _plots.FirstOrDefault(x => x.PlotterHouse == plotterHouse && x.Lord == lord && PlotLive(x));

    // ------------------------------------------------------------------ 세력 안의 사람들

    /// <summary>지배자 L 세력 안에서 편을 고를 사람들: 바로 아래 봉신 가장, L 조직의 측근과 갈래 우두머리. L 집 식구는 뺀다.</summary>
    private List<int> RealmInsiders(int lord)
    {
        var p = People; var hh = Households;
        var set = new HashSet<int>();
        foreach (int h in DirectClients(lord))
            if (hh.IsAlive(h) && PatronOf(h) == lord && ClientCount(h) > 0 && HeadIdx(h) is int vh && vh >= 0) set.Add(vh);
        var org = LordOrg(lord, false);
        if (org != null)
        {
            foreach (int u in org.Inner) if (FindAlive(u) is int x && x >= 0) set.Add(x);
            foreach (var g in Subtree(org)) if (g != org && FindAlive(g.LeaderUid) is int y && y >= 0) set.Add(y);
        }
        set.RemoveWhere(i => !p.IsAliveIndex(i) || p.Household[i] == lord || !Adult(i));
        return set.ToList();
    }

    /// <summary>i 가 거사에 가져오는 힘: 봉신이면 그 세력의 0.6, 갈래 우두머리면 그 갈래, 측근이면 제 몸.</summary>
    private float SidePower(int i, int lord)
    {
        var p = People;
        int h = p.Household[i];
        float s = 0f;
        if (PatronOf(h) == lord && ClientCount(h) > 0 && HeadIdx(h) == i) s += 0.6f * LordPower(h);
        var org = LordOrg(lord, false);
        if (org != null)
        {
            foreach (var g in Subtree(org)) if (g != org && g.LeaderUid == p.Uid[i]) s += g.OwnStrength;
            if (org.Inner.Contains(p.Uid[i])) s += FightPower(i);
        }
        return s;
    }

    /// <summary>꾸미는 사람 쪽 조직(그 집의 지배자 조직, 아니면 그가 이끄는 맨 위 조직).</summary>
    private Group? PlotterOrg(int i)
    {
        var own = LordOrg(People.Household[i], false);
        if (own != null) return own;
        foreach (var g in GroupsOf(i)) if (g.Parent < 0 && g.Home < 0 && g.LeaderUid == People.Uid[i]) return g;
        return null;
    }

    /// <summary>
    /// 세력 안 사람 c 가 꾸미는 사람 P 와 지배자 L 사이에서 기우는 정도(0~1). 0.6 넘으면 P 편, 0.3 아래면 L 편, 그 사이는 지켜본다.
    /// 두 사람을 보는 눈, L에 대한 충성, 두 쪽의 아는 권위, 야망.
    /// </summary>
    private float Lean(int c, int plotter, int lord)
    {
        var p = People;
        int lh = HeadIdx(lord);
        if (lh < 0) return 1f;
        if (p.Fanatic[c] != 0 && p.LoyalUid[c] == p.Uid[lh]) return 0f;
        var po = PlotterOrg(plotter);
        var lo = LordOrg(lord, false);
        float pullP = po != null ? AuthorityPull(c, po) : 0.2f, pullL = lo != null ? AuthorityPull(c, lo) : 0.5f;
        float s = 0.5f + (Social.Opinion(p, c, p.Uid[plotter]) - Social.Opinion(p, c, p.Uid[lh])) / 200f
                  + 0.15f * (pullP - pullL) + 0.2f * (p.Get(c, Trait.Ambition) - 0.5f);
        if (p.LoyalUid[c] == p.Uid[lh]) s -= 0.1f + 0.5f * (p.Loyalty[c] - 0.5f);   // 섬기는 사람은 불만이 없으면 의무로 지킨다
        // 꾸미는 사람이 이끄는 조직의 조직원은 그를 따른다(양다리)
        if (po != null && po.Home < 0 && GroupsOf(c).Any(g => RootOf(g).Id == po.Id)) s += 0.3f;
        return Math.Clamp(s, 0f, 1f);
    }

    /// <summary>쿠데타를 꾸밀 발판이 있나: L의 봉신 가장, L 조직의 측근·갈래 우두머리, 또는 그런 자리에 제 조직원을 심어 둔 우두머리.</summary>
    public bool CoupFoothold(int i, int lord)
    {
        var p = People;
        if (!p.IsAliveIndex(i) || !Adult(i) || p.Household[i] == lord || ClientCount(lord) == 0) return false;
        // 봉신은 주군이 이름뿐이어도(단계가 없어도) 노릴 수 있다
        int h = p.Household[i];
        if (PatronOf(h) == lord && ClientCount(h) > 0 && HeadIdx(h) == i) return true;
        if (LordOrg(lord, false) == null) return false;
        var insiders = RealmInsiders(lord);
        if (insiders.Contains(i)) return true;
        var mine = PlotterOrg(i);
        return mine != null && mine.Home < 0 && insiders.Any(x => GroupsOf(x).Any(g => RootOf(g).Id == mine.Id));
    }

    /// <summary>거사했을 때의 힘의 비(꾸미는 쪽 몫). noise 면 그날의 운을 섞는다. 편을 고른 결과도 돌려준다.</summary>
    private (float ratio, List<int> mine, List<int> theirs) StrikeOdds(Plot x, bool noise)
    {
        var p = People;
        int pi = FindAlive(x.Plotter);
        var mine = new List<int>(); var theirs = new List<int>();
        float pf = HouseholdPower(x.PlotterHouse);
        if (pi >= 0) { pf += SidePower(pi, x.Lord); if (PlotterOrg(pi) is { Home: < 0 } po) pf += 0.5f * po.Strength; }
        var lo = LordOrg(x.Lord, false);
        float lf = HouseholdPower(x.Lord);
        var insiders = RealmInsiders(x.Lord);
        // 지배자 조직에 바로 딸린 군인과 사람들도 한 사람씩 편을 고른다(갈래의 군인은 갈래 우두머리를 따른다)
        if (lo != null)
        {
            var ins = new HashSet<int>(insiders);
            foreach (int m in Members(lo))
            {
                if (m == pi || ins.Contains(m) || p.Household[m] == x.Lord) continue;
                float lean = Lean(m, pi, x.Lord);
                if (lean > 0.6f) pf += FightPower(m);
                else if (lean < 0.3f) lf += FightPower(m);
            }
        }
        foreach (int c in insiders)
        {
            if (c == pi) continue;
            float sp = SidePower(c, x.Lord);
            float lean = x.Backers.Contains(p.Uid[c]) ? 0.9f : Lean(c, pi, x.Lord);
            if (lean > 0.6f) { pf += sp; mine.Add(c); }
            else if (lean < 0.3f) { lf += sp; theirs.Add(c); }
        }
        if (noise) { pf *= 0.7f + 0.6f * _rng.NextFloat(); lf *= 0.7f + 0.6f * _rng.NextFloat(); }
        return (pf / (pf + lf + 0.01f), mine, theirs);
    }

    // ------------------------------------------------------------------ 플레이어

    /// <summary>플레이어 집이 꾸미는 거사(없으면 null).</summary>
    public Plot? PlayerPlot => PlayerHousehold >= 0 ? PlotOf(PlayerHousehold) : null;

    /// <summary>지금 일으키면 꾸미는 쪽 힘의 몫(운을 뺀 어림)과 편들 사람·맞설 사람 수.</summary>
    public (float ratio, int mine, int theirs) StrikeEstimate(Plot x)
    {
        var (r, m, t) = StrikeOdds(x, false);
        return (r, m.Count, t.Count);
    }

    /// <summary>끌어들일 만한 사람: 세력 안 사람 가운데 아직 모르는 사람과 받아들일 확률.</summary>
    public List<(int person, float chance)> PlotCandidates(Plot x)
    {
        var p = People;
        int pi = FindAlive(x.Plotter);
        var list = new List<(int, float)>();
        if (pi < 0) return list;
        foreach (int c in RealmInsiders(x.Lord))
            if (c != pi && !x.Known.Contains(p.Uid[c]))
                list.Add((c, Math.Clamp((Lean(c, pi, x.Lord) - 0.35f) * 2.5f, 0.02f, 0.9f)));
        return list.OrderByDescending(e => e.Item2).ToList();
    }

    /// <summary>거사를 접는다. 아는 사람들은 그대로 안다.</summary>
    public void AbandonPlot(Plot x)
    {
        _plots.Remove(x);
        if (FindAlive(x.Plotter) is int pi && pi >= 0) Chron(pi, $"{SubjName(pi)} 거사를 접었다");
    }

    // ------------------------------------------------------------------ 해마다

    private void CoupYear()
    {
        var p = People; var hh = Households;
        _plots.RemoveAll(x => !PlotLive(x));
        // 1) NPC가 마음먹는다(지배자마다 한 번에 하나)
        foreach (var (lord, _) in _lordOrg.ToList())
        {
            if (!hh.IsAlive(lord) || TierOf(lord) < 2 || _plots.Any(x => x.Lord == lord)) continue;
            int lh = HeadIdx(lord);
            if (lh < 0) continue;
            float weak = (LordOrg(lord, false)?.Shaken ?? 0f) * 0.5f + (LegitOf(lord) == Legitimacy.Self ? 0.2f : 0f)
                         + (AgeYears(lh) > 65 ? 0.2f : 0f) + (p.Honor[lh] < -0.3f ? 0.2f : 0f);
            foreach (int i in RealmInsiders(lord))
            {
                if (IsPlayer(i) || PlotOf(p.Household[i]) != null) continue;
                if (p.Fanatic[i] != 0 && p.LoyalUid[i] == p.Uid[lh]) continue;
                float loy = p.LoyalUid[i] == p.Uid[lh] ? p.Loyalty[i] : 0.3f;
                float hate = MathF.Max(0f, -Social.Opinion(p, i, p.Uid[lh]) / 100f);
                float press = 0.7f * p.Get(i, Trait.Ambition) + 0.3f * p.Get(i, Trait.Aggression) + 0.5f * (1f - loy) + 0.4f * hate + 0.4f * weak;
                int chon = Lineage.Chon(p.Uid[i], p.Uid[lh]);
                float th = 1.2f + 0.4f * p.Get(i, Trait.Caution) + 0.2f * p.Piety[i] + (chon > 0 && chon <= 3 ? 0.3f : 0f);
                if (!_rng.Chance(0.02f * Sigmoid((press - th) / 0.1f))) continue;
                StartPlot(i, lord);
                break;
            }
        }
        // 2) 꾸미는 일: 끌어들이고, 새어 나가고, 거사한다
        foreach (var x in _plots.ToList())
        {
            if (!PlotLive(x) || x.Exposed) continue;
            int pi = FindAlive(x.Plotter);
            if (pi < 0) continue;
            if (!IsPlayer(pi))
            {
                // 한 해에 한 사람, 가장 기울어 보이는 사람부터 조심스럽게 떠본다(기운 사람이 없으면 기다린다)
                int best = -1; float bl = 0.4f;
                foreach (int c in RealmInsiders(x.Lord))
                {
                    if (c == pi || x.Backers.Contains(p.Uid[c]) || x.Known.Contains(p.Uid[c]) || IsPlayer(c)) continue;
                    float l = Lean(c, pi, x.Lord) + 0.1f * (_rng.NextFloat() - 0.5f);   // 겉으로 보이는 기울기는 어림이다
                    if (l > bl) { bl = l; best = c; }
                }
                if (best >= 0) TryRecruit(x, pi, best);
                if (!PlotLive(x) || x.Exposed) continue;
            }
            // 새어 나감: 아는 사람 한 명당 4%, 신중한 꾸미는 사람은 덜
            float leak = MathF.Min(0.5f, 0.025f * x.Known.Count * (1.2f - 0.5f * p.Get(pi, Trait.Caution)));
            if (_rng.Chance(leak)) { Expose(x); continue; }
            if (IsPlayer(pi)) continue;   // 플레이어는 거사할 때를 스스로 고른다
            int years = (Day - x.Since) / Config.DaysPerYear;
            var (ratio, _, _) = StrikeOdds(x, false);
            float need = 0.55f + 0.15f * p.Get(pi, Trait.Caution) - 0.1f * p.Get(pi, Trait.Ambition);
            // 오래 끌수록 새어 나갈 위험이 커진다: 야심가는 덜 확실해도 일으킨다
            float amb = p.Get(pi, Trait.Ambition);
            if (ratio >= need || (years >= 3 && ratio >= 0.45f - 0.1f * amb) || (years >= 5 && amb > 0.7f && ratio >= 0.3f)) Strike(x);
            else if (years >= 7) { _plots.Remove(x); Chron(pi, $"{SubjName(pi)} 거사를 접었다"); }
        }
        // 3) 독립 선언(NPC 봉신)
        foreach (var (lord, _) in _lordOrg.ToList())
        {
            if (!hh.IsAlive(lord)) continue;
            int lh = HeadIdx(lord);
            if (lh < 0) continue;
            foreach (int h in DirectClients(lord).ToList())
            {
                if (!hh.IsAlive(h) || PatronOf(h) != lord || ClientCount(h) < 5) continue;
                int vh = HeadIdx(h);
                if (vh < 0 || IsPlayer(vh) || PlotOf(h) != null) continue;
                if (p.Fanatic[vh] != 0 && p.LoyalUid[vh] == p.Uid[lh]) continue;
                float loy = p.LoyalUid[vh] == p.Uid[lh] ? p.Loyalty[vh] : 0.3f;
                float hate = MathF.Max(0f, -Social.Opinion(p, vh, p.Uid[lh]) / 100f);
                float mine = RisePower(h), theirs = LordPower(lord);
                float far = MathF.Min(1.5f, MathF.Sqrt(Dist2(h, lord)) / Config.VassalRange);
                float press = 0.4f * p.Get(vh, Trait.Ambition) + 0.5f * hate + 0.4f * (1f - loy) + 0.6f * mine / (mine + theirs + 0.01f) + 0.3f * far;
                float th = 1.0f + 0.3f * p.Get(vh, Trait.Caution);
                if (!_rng.Chance(0.05f * Sigmoid((press - th) / 0.1f))) continue;
                Stats.Declared++;
                if (IsPlayer(lh))
                {
                    if (!_demands.Any(d => d.Kind == InteractionId.Declare && d.From == h)) AskPlayer(InteractionId.Declare, h, lord);
                }
                else Interact(InteractionId.Declare, new InteractionContext { Actor = vh, Target = lh, Third = -1 });
            }
        }
        _plots.RemoveAll(x => !PlotLive(x));
    }

    private Plot StartPlot(int i, int lord)
    {
        var p = People; var hh = Households;
        var x = new Plot
        {
            Id = ++_plotIds, Plotter = p.Uid[i], PlotterHouse = p.Household[i], PlotterHouseFounded = hh.FoundedDay[p.Household[i]],
            Lord = lord, LordFounded = hh.FoundedDay[lord], Since = Day,
        };
        x.Known.Add(p.Uid[i]);
        _plots.Add(x);
        Stats.Plots++;
        int lh = HeadIdx(lord);
        if (lh >= 0) Chron(i, $"{SubjName(i)} 몰래 {Names.Full(Lineage, p.Uid[lh])}의 자리를 노리기 시작했다");
        return x;
    }

    /// <summary>c 를 음모에 끌어들인다. 거절하면 지배자에게 충성하는 만큼 알린다. 받아들였으면 true.</summary>
    private bool TryRecruit(Plot x, int pi, int c)
    {
        var p = People;
        float lean = Lean(c, pi, x.Lord);
        float yes = Math.Clamp((lean - 0.35f) * 2.5f, 0.02f, 0.9f);
        x.Known.Add(p.Uid[c]);
        if (_rng.Chance(yes))
        {
            x.Backers.Add(p.Uid[c]);
            Chron(pi, $"{Subj(Names.Full(Lineage, p.Uid[c]))} 거사에 함께하기로 했다");
            Chron(c, $"{SubjName(c)} {Names.Full(Lineage, x.Plotter)}의 거사에 함께하기로 했다");
            return true;
        }
        int lh = HeadIdx(x.Lord);
        float report = lh >= 0 && p.LoyalUid[c] == p.Uid[lh] ? (p.Fanatic[c] != 0 ? 0.9f : 0.2f + 0.6f * p.Loyalty[c]) : 0.1f;
        Chron(pi, $"{Subj(Names.Full(Lineage, p.Uid[c]))} 거사에 끼기를 거절했다");
        if (_rng.Chance(report)) Expose(x);
        return false;
    }

    /// <summary>음모가 지배자에게 들켰다. NPC 지배자는 숙청하거나 눈감고, 플레이어 지배자에게는 묻는다.</summary>
    private void Expose(Plot x)
    {
        Stats.PlotsExposed++;
        x.Exposed = true;
        int lh = HeadIdx(x.Lord), pi = FindAlive(x.Plotter);
        if (lh < 0 || pi < 0) { _plots.Remove(x); return; }
        Chron(pi, $"{SubjName(pi)} 꾸민 거사가 {Names.Full(Lineage, People.Uid[lh])}에게 들켰다");
        if (IsPlayer(lh)) { AskPlayer(InteractionId.PlotPurge, x.PlotterHouse, x.Lord); return; }
        var c = new InteractionContext { Actor = pi, Target = lh, Third = -1 };
        if (_rng.Chance(Def(InteractionId.PlotPurge).Accept(this, c))) Purge(x);
        else Forgive(x);
    }

    /// <summary>숙청: 꾸민 사람은 처형되거나 쫓겨나고, 편든 사람들은 지배자의 미움을 산다.</summary>
    private void Purge(Plot x)
    {
        var p = People;
        _plots.Remove(x);
        Stats.Purges++;
        int lh = HeadIdx(x.Lord), pi = FindAlive(x.Plotter);
        if (pi < 0) return;
        PunishPlotter(x, pi, lh, "숙청");
        if (lh >= 0)
            foreach (int u in x.Backers)
                if (FindAlive(u) is int b && b >= 0) { Social.Add(p, lh, u, -40f); Social.Add(p, b, p.Uid[lh], -20f, 30f); }
    }

    private void Forgive(Plot x)
    {
        var p = People;
        _plots.Remove(x);
        int lh = HeadIdx(x.Lord), pi = FindAlive(x.Plotter);
        if (pi < 0 || lh < 0) return;
        Social.Add(p, pi, p.Uid[lh], 15f, 20f);
        Social.Add(p, lh, p.Uid[pi], -30f);
        if (ClientCount(x.PlotterHouse) > 0) Suspect(x.Lord, x.PlotterHouse, 30f);
        Chron(pi, $"{Subj(Names.Full(Lineage, p.Uid[lh]))} 꾸민 일을 알고도 눈감아 주었다");
        Chron(lh, $"{Names.Full(Lineage, p.Uid[pi])}의 음모를 알았지만 눈감아 주었다");
    }

    /// <summary>꾸민 사람을 벌한다: 엄하거나 거친 지배자는 처형하고(3촌 안이나 플레이어 지배자는 빼고), 아니면 자리를 빼앗아 내쫓는다.</summary>
    private void PunishPlotter(Plot x, int pi, int lh, string why)
    {
        var p = People; var hh = Households;
        int chon = lh >= 0 ? Lineage.Chon(p.Uid[lh], p.Uid[pi]) : -1;
        bool kin = chon > 0 && chon <= 3;
        bool kill = lh >= 0 && !kin && !IsPlayer(lh) && (Severity(x.Lord) > 0.5f || p.Get(lh, Trait.Aggression) > 0.6f);
        if (kill)
        {
            Stats.Executions++;
            Chron(pi, $"{SubjName(pi)} {Ro(why)} 처형되었다");
            if (lh >= 0) Chron(lh, $"{Obj(Names.Full(Lineage, p.Uid[pi]))} {Ro(why)} 처형했다");
            Die(pi, false);
            return;
        }
        // 쫓아냄: 봉신이면 집을 거두고 내치며, 조직의 자리와 고용에서 뺀다
        int h = p.Household[pi];
        if (hh.IsAlive(h) && PatronOf(h) == x.Lord)
        {
            if (ClientCount(h) > 0 && HeadIdx(h) == pi) TakeBack(x.Lord, h);
            _patron[h] = -1;
            Resnap(h);
        }
        var org = LordOrg(x.Lord, false);
        if (org != null)
        {
            org.Inner.Remove(p.Uid[pi]);
            foreach (var g in GroupsOf(pi).ToList()) if (RootOf(g).Id == org.Id) LeaveGroup(pi, g);
        }
        if (_employer.TryGetValue(pi, out int boss) && boss == x.Lord) { p.JobOf[pi] = 0; _employer.Remove(pi); _employerVer++; }
        p.Honor[pi] -= 0.3f;
        if (lh >= 0) Social.Add(p, pi, p.Uid[lh], -40f, 30f);
        Chron(pi, $"{SubjName(pi)} {Ro(why)} 자리를 잃고 쫓겨났다");
        if (lh >= 0) Chron(lh, $"{Obj(Names.Full(Lineage, p.Uid[pi]))} {Ro(why)} 내쫓았다");
        RecountClients();
    }

    // ------------------------------------------------------------------ 거사

    /// <summary>거사: 편이 갈리고 힘을 겨룬다. 0.6 넘으면 자리를 빼앗고, 0.4~0.6이면 내전 끝에 떨어져 나가고, 그 아래면 진다.</summary>
    public void Strike(Plot x)
    {
        var p = People; var hh = Households;
        if (!PlotLive(x)) return;
        _plots.Remove(x);
        Stats.CoupStrikes++;
        int pi = FindAlive(x.Plotter), lh = HeadIdx(x.Lord);
        if (pi < 0 || lh < 0) return;
        var (ratio, mine, theirs) = StrikeOdds(x, true);
        if (IsPlayer(pi) || IsPlayer(lh)) _fork = true;
        string pn = Names.Full(Lineage, x.Plotter), ln = Names.Full(Lineage, p.Uid[lh]);
        if (ratio >= 0.6f)
        {
            Stats.CoupWins++;
            Chron(pi, $"{SubjName(pi)} 거사를 일으켜 {ln}의 자리를 빼앗았다(편든 이 {mine.Count}명)");
            Chron(lh, $"{SubjName(lh)} {pn}의 거사에 자리를 빼앗겼다");
            TakeSeat(pi, x.Lord, mine);
        }
        else if (ratio >= 0.4f)
        {
            Stats.CivilWars++;
            Chron(pi, $"{SubjName(pi)} 거사를 일으켰으나 {With(ln)} 맞서 내전이 되었고, 편든 이들과 떨어져 나왔다");
            Chron(lh, $"{pn}의 거사로 내전이 벌어졌고, 그가 편든 이들과 떨어져 나갔다");
            Split(pi, x.Lord, mine);
        }
        else
        {
            Stats.CoupFails++;
            Chron(lh, $"{pn}의 거사를 꺾었다");
            foreach (int c in mine) { Social.Add(p, lh, p.Uid[c], -40f); Social.Add(p, c, p.Uid[lh], -20f, 30f); }
            PunishPlotter(x, pi, lh, "거사에 실패해");
        }
        // 지배자를 지킨 사람은 더 믿음을 얻는다
        if (p.IsAliveIndex(lh)) foreach (int c in theirs) Social.Add(p, lh, p.Uid[c], 10f);
        RecountClients(); RecountTree();
    }

    /// <summary>꾸민 사람 집이 L의 자리를 넘겨받는다: 따르는 집, 군인, 땅, 곡식, 성. L의 주군 아래 그 자리에 앉는다(찬탈).</summary>
    private void TakeSeat(int pi, int lord, List<int> backers)
    {
        var p = People; var hh = Households;
        int ph = p.Household[pi], lh = HeadIdx(lord);
        int liege = PatronOf(lord);
        if (PatronOf(ph) >= 0) { _patron[ph] = -1; Resnap(ph); }
        // L의 조직(군대)도 넘어온다. 꾸민 집에 군대가 있으면 L의 군대가 그 갈래가 된다
        var lo = LordOrg(lord, false);
        var po = LordOrg(ph, false);
        InheritLordship(lord, ph);
        Stats.LordshipsInherited--;   // 물려받은 것이 아니다
        if (lo != null)
        {
            _lordOrg.Remove(lord);
            if (po != null && po != lo) { lo.Home = -1; lo.HomeFounded = -1; lo.Parent = po.Id; }
            else { lo.Home = ph; lo.HomeFounded = hh.FoundedDay[ph]; _lordOrg[ph] = lo.Id; lo.LeaderUid = p.Uid[pi]; }
            lo.Inner.Remove(p.Uid[pi]);
            lo.Shaken = MathF.Min(1f, lo.Shaken + 0.5f);
        }
        if (liege >= 0 && liege != ph && hh.IsAlive(liege) && !WouldLoop(ph, liege)) { _patron[ph] = liege; _patronFounded[ph] = hh.FoundedDay[liege]; }
        EnsureRealmArrays();
        _legit[ph] = (byte)Legitimacy.Self;
        Resnap(ph);
        p.Honor[pi] -= 0.2f;
        p.Notoriety[pi] += 0.3f;
        // 옛 지배자: 거친 찬탈자면 죽이고, 아니면 새 주인을 따른다. 그 집안 광신자는 원수가 된다
        if (lh >= 0)
        {
            for (int i = 0; i < p.HighWater; i++)
                if (p.IsAliveIndex(i) && p.Fanatic[i] != 0 && p.LoyalUid[i] == p.Uid[lh]) Social.Add(p, i, p.Uid[pi], -80f);
            int chon = Lineage.Chon(p.Uid[pi], p.Uid[lh]);
            if (p.Get(pi, Trait.Aggression) > 0.6f && !(chon > 0 && chon <= 3) && !IsPlayer(lh)) { Stats.Executions++; Chron(lh, $"{SubjName(lh)} 거사 끝에 죽임을 당했다"); Die(lh, false); }
            else Social.Add(p, lh, p.Uid[pi], -60f);
        }
        if (hh.IsAlive(lord) && !WouldLoop(lord, ph)) { _patron[lord] = ph; _patronFounded[lord] = hh.FoundedDay[ph]; Resnap(lord); }
        foreach (int c in backers) if (p.IsAliveIndex(c)) Social.Add(p, c, p.Uid[pi], 15f);
    }

    /// <summary>내전 끝에 갈라선다: 꾸민 사람은 홀로 서고, 편든 봉신은 그를 섬기고, 편든 갈래는 그의 군대로 간다.</summary>
    private void Split(int pi, int lord, List<int> backers)
    {
        var p = People; var hh = Households;
        int ph = p.Household[pi];
        if (PatronOf(ph) >= 0 && (PatronOf(ph) == lord || InTreeOf(ph, lord))) { _patron[ph] = -1; Resnap(ph); }
        EnsureRealmArrays();
        _legit[ph] = (byte)Legitimacy.Self;
        var lo = LordOrg(lord, false);
        Group? po = null;
        foreach (int c in backers)
        {
            int ch = p.Household[c];
            if (ch != ph && PatronOf(ch) == lord && ClientCount(ch) > 0 && HeadIdx(ch) == c && !WouldLoop(ch, ph))
            {
                _patron[ch] = ph; _patronFounded[ch] = hh.FoundedDay[ph]; Resnap(ch);
            }
            if (lo == null) continue;
            foreach (var g in Subtree(lo).ToList())
            {
                if (g == lo || g.LeaderUid != p.Uid[c]) continue;
                po ??= LordOrg(ph);
                if (po == null) break;
                g.Parent = po.Id;
                // 그 갈래의 군인은 이제 꾸민 집이 부린다
                foreach (int m in Members(g)) if (_employer.TryGetValue(m, out int b) && b == lord) { _employer[m] = ph; _employerVer++; }
            }
        }
        for (int k = 0; k < 2; k++) { Casualty(lord, 0f); Casualty(ph, 0f); }
        if (p.IsAliveIndex(pi)) p.Notoriety[pi] += 0.2f;
        int lh = HeadIdx(lord);
        if (lh >= 0 && p.IsAliveIndex(pi)) { Social.Add(p, lh, p.Uid[pi], -60f); Social.Add(p, pi, p.Uid[lh], -40f); }
    }

    // ------------------------------------------------------------------ 독립

    /// <summary>
    /// 독립 선언에 주군이 고를 무게: 진압(공격성, 이길 만할 때), 협상(말솜씨), 체념(신중, 내가 약할 때).
    /// 진압에서 봉신이 이길 확률과, 협상에서 봉신이 붙잡힐 확률도 돌려준다.
    /// </summary>
    private (float sup, float neg, float let, float rebelWins, float negHolds) DeclareOdds(int rebel, int lord)
    {
        var p = People;
        int lh = HeadIdx(lord), rh = HeadIdx(rebel);
        float ratio = PowerRatio(lord, rebel);
        float agg = lh >= 0 ? p.Get(lh, Trait.Aggression) : 0.5f, cha = lh >= 0 ? p.Get(lh, Trait.Charisma) : 0.5f, cau = lh >= 0 ? p.Get(lh, Trait.Caution) : 0.5f;
        float sup = 0.2f + 1.5f * agg * agg * Math.Clamp((ratio - 0.3f) * 3f, 0f, 1.5f);
        float neg = 0.2f + 1.2f * cha * cha;
        float let = 0.1f + 0.8f * cau * (1f - ratio);
        float rebelWins = Math.Clamp(1.2f * (1f - ratio) - 0.1f, 0.05f, 0.95f);
        float negHolds = Math.Clamp(0.5f + 0.3f * (1f - (rh >= 0 ? p.Get(rh, Trait.Ambition) : 0.5f)) + Social.Opinion(p, rh >= 0 ? rh : 0, lh >= 0 ? p.Uid[lh] : -1) / 200f, 0.05f, 0.95f);
        return (sup, neg, let, rebelWins, negHolds);
    }

    private float FreeOdds(int rebel, int lord)
    {
        var (sup, neg, let, win, hold) = DeclareOdds(rebel, lord);
        return (let + sup * win + neg * (1f - hold)) / (sup + neg + let);
    }

    /// <summary>봉신이 떨어져 나간다(체념, 진압 실패, 협상 실패). 다른 봉신의 충성이 조금 깎인다.</summary>
    private void GoFree(int rh, int lh, string how)
    {
        var p = People; var hh = Households;
        int a = p.Household[rh], t = p.Household[lh];
        if (PatronOf(a) == t) { _patron[a] = -1; Resnap(a); }
        EnsureRealmArrays();
        _legit[a] = (byte)Legitimacy.Self;
        _rivals.Remove((t, a));
        Stats.Independences++;
        foreach (int h in DirectClients(t))
            if (hh.IsAlive(h) && h != a && PatronOf(h) == t && ClientCount(h) > 0 && HeadIdx(h) is int vh && vh >= 0 && p.LoyalUid[vh] == p.Uid[lh])
                p.Loyalty[vh] = MathF.Max(0f, p.Loyalty[vh] - 0.05f);
        RecountClients(); RecountTree();
        RiseChron(new InteractionContext { Actor = rh, Target = lh }, n => $"{how} {Obj(n)} 떠나 홀로 섰다", n => $"{Subj(Names.Full(Lineage, p.Uid[rh]))} {how} 떨어져 나갔다");
    }

    private void Negotiated(int rh, int lh)
    {
        var p = People; var hh = Households;
        int a = p.Household[rh], t = p.Household[lh];
        float gift = MathF.Min(MathF.Max(0f, hh.Food[t]) * 0.1f, 300f);
        hh.Food[t] -= gift; hh.Food[a] += gift;
        Social.Add(p, rh, p.Uid[lh], 20f); Social.Add(p, lh, p.Uid[rh], 10f);
        EnsureRealmArrays();
        _legit[a] = (byte)Legitimacy.Granted;
        Stats.IndepNegotiated++;
        RiseChron(new InteractionContext { Actor = rh, Target = lh }, n => $"독립을 선언했으나 {n}의 선물(식량 {gift:0})을 받고 남기로 했다", n => $"독립을 선언한 {Obj(Names.Full(Lineage, p.Uid[rh]))} 식량 {gift:0}으로 달래 붙잡았다");
    }

    private void AddCoupDefs(InteractionDef[] d)
    {
        int H(World w, int i) => w.People.Household[i];
        d[(int)InteractionId.PlotCoup] = new InteractionDef
        {
            // 쿠데타를 꾸미기 시작한다(대상은 노리는 지배자 가장). 몰래 하는 일이다
            Id = InteractionId.PlotCoup, Name = "거사 꾸미기", Category = InteractionCategory.Scheme, Secret = true, Harsh = true,
            Can = (w, c) =>
            {
                int t = H(w, c.Target);
                return w.HeadIdx(t) == c.Target && w.PlotOf(H(w, c.Actor)) == null && w.CoupFoothold(c.Actor, t);
            },
            Accept = (w, c) => 1f,
            Success = (w, c, _) => w.StartPlot(c.Actor, H(w, c.Target)),
        };
        d[(int)InteractionId.PlotRecruit] = new InteractionDef
        {
            // 꾸미는 거사에 끌어들인다. 거절하면 알릴 수 있다
            Id = InteractionId.PlotRecruit, Name = "거사에 끌어들이기", Category = InteractionCategory.Scheme, Secret = true,
            Can = (w, c) =>
            {
                var x = w.PlotOf(H(w, c.Actor));
                return x != null && !x.Exposed && w.People.Uid[c.Actor] == x.Plotter && !x.Backers.Contains(w.People.Uid[c.Target])
                       && w.RealmInsiders(x.Lord).Contains(c.Target);
            },
            Accept = (w, c) =>
            {
                var x = w.PlotOf(H(w, c.Actor))!;
                return Math.Clamp((w.Lean(c.Target, c.Actor, x.Lord) - 0.35f) * 2.5f, 0.02f, 0.9f);
            },
            // 확률 굴림은 TryRecruit 안에서 한 번 더 하지 않도록, 결과에 맞춰 바로 적는다
            Success = (w, c, _) =>
            {
                var x = w.PlotOf(H(w, c.Actor));
                if (x == null) return;
                x.Known.Add(w.People.Uid[c.Target]); x.Backers.Add(w.People.Uid[c.Target]);
                w.Chron(c.Target, $"{w.SubjName(c.Target)} {Names.Full(w.Lineage, x.Plotter)}의 거사에 함께하기로 했다");
            },
            Fail = (w, c) =>
            {
                var x = w.PlotOf(H(w, c.Actor));
                if (x == null) return;
                x.Known.Add(w.People.Uid[c.Target]);
                var p = w.People;
                int lh = w.HeadIdx(x.Lord);
                float report = lh >= 0 && p.LoyalUid[c.Target] == p.Uid[lh] ? (p.Fanatic[c.Target] != 0 ? 0.9f : 0.2f + 0.6f * p.Loyalty[c.Target]) : 0.1f;
                if (w._rng.Chance(report)) w.Expose(x);
            },
        };
        d[(int)InteractionId.PlotPurge] = new InteractionDef
        {
            // 들킨 음모(지배자가 받는 요구): 받으면 숙청, 거절하면 눈감음. 받아들일 확률 = NPC 지배자가 숙청할 확률
            Id = InteractionId.PlotPurge, Name = "음모 숙청", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) => !w.IsPlayer(c.Actor) && w.PlotBy(H(w, c.Actor), H(w, c.Target)) is { Exposed: true },
            Accept = (w, c) =>
            {
                var p = w.People;
                int chon = w.Lineage.Chon(p.Uid[c.Actor], p.Uid[c.Target]);
                return Math.Clamp(0.4f + 0.4f * p.Get(c.Target, Trait.Aggression) + 0.3f * w.Severity(H(w, c.Target)) - 0.3f * p.Get(c.Target, Trait.Charisma)
                                  - (chon > 0 && chon <= 3 ? 0.4f : 0f), 0.05f, 0.95f);
            },
            Success = (w, c, _) => { if (w.PlotBy(H(w, c.Actor), H(w, c.Target)) is { } x) w.Purge(x); },
            Fail = (w, c) => { if (w.PlotBy(H(w, c.Actor), H(w, c.Target)) is { } x) w.Forgive(x); },
        };
        d[(int)InteractionId.Declare] = new InteractionDef
        {
            // 독립 선언(대상은 주군 가장). 받아들일 확률 = 결국 홀로 서게 될 확률
            Id = InteractionId.Declare, Name = "독립 선언", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) =>
            {
                int a = H(w, c.Actor), t = H(w, c.Target);
                return w.HeadIdx(a) == c.Actor && w.HeadIdx(t) == c.Target && w.PatronOf(a) == t && w.ClientCount(a) > 0;
            },
            Accept = (w, c) => w.FreeOdds(H(w, c.Actor), H(w, c.Target)),
            Success = (w, c, _) =>
            {
                int a = H(w, c.Actor), t = H(w, c.Target);
                if (w.IsPlayer(c.Target)) { w.Stats.IndepLetGo++; w.GoFree(c.Actor, c.Target, "주군이 놓아주어"); return; }
                var (sup, neg, let, win, hold) = w.DeclareOdds(a, t);
                float r = w._rng.NextFloat() * (let + sup * win + neg * (1f - hold));
                if (r < let) { w.Stats.IndepLetGo++; w.GoFree(c.Actor, c.Target, "주군이 체념해"); }
                else if (r < let + sup * win) { w.Stats.Independences++; w.SuppressLost(c.Target, c.Actor); }
                else w.GoFree(c.Actor, c.Target, "협상이 깨져");
            },
            Fail = (w, c) =>
            {
                int a = H(w, c.Actor), t = H(w, c.Target);
                if (w.IsPlayer(c.Target))
                {
                    // 플레이어 주군이 진압을 골랐다
                    var (_, _, _, win, _) = w.DeclareOdds(a, t);
                    if (w._rng.Chance(win)) { w.Stats.Independences++; w.SuppressLost(c.Target, c.Actor); }
                    else { w.Stats.IndepSuppressed++; w.SuppressWon(c.Target, c.Actor); }
                    return;
                }
                var (sup, neg, _, win2, hold) = w.DeclareOdds(a, t);
                float r = w._rng.NextFloat() * (sup * (1f - win2) + neg * hold);
                if (r < neg * hold) w.Negotiated(c.Actor, c.Target);
                else { w.Stats.IndepSuppressed++; w.SuppressWon(c.Target, c.Actor); }
            },
        };
    }
}
