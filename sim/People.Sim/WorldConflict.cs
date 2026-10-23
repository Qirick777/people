namespace People.Sim;

public enum ConflictKind : byte { Coup, Independence, War }

/// <summary>분쟁의 한쪽 진영. 싸우는 사람은 uid 로 적는다.</summary>
public sealed class ConflictSide
{
    public int House, HouseFounded, LeaderUid;
    public readonly HashSet<int> Fighters = new();
    public float Morale = 0.7f;
    public bool Holding;               // 거점(성·저택)에서 버틴다
}

/// <summary>분쟁 기록 한 장(진단·재생용): 그 진행이 끝난 뒤의 양쪽 모습과 그때 일어난 일.</summary>
public sealed class ConflictFrame
{
    public int Tick, Day, AN, DN, ADead, AWounded, DDead, DWounded, ADeserted, DDeserted;
    public float AP, DP, AM, DM, Food;
    public bool Holding, Clashed;
    public readonly List<string> Notes = new();
}

/// <summary>분쟁 하나: 거사, 독립 진압, 전쟁. 하루(약 보름)마다 한 번 진행한다.</summary>
public sealed class Conflict
{
    public int Id, Since, Ticks;
    public ConflictKind Kind;
    public readonly ConflictSide A = new(), D = new();   // 일으킨 쪽, 맞선 쪽
    public Plot? Plot;
    public readonly List<int> Backers = new();          // 거사에 편든 세력 안 사람(uid)
    public readonly HashSet<int> Asked = new();         // 소집을 이미 물은 봉신 집
    public int Deaths, Wounded, Deserted, Battles;
    public bool Over;
    public string Last = "";                            // 지난 교전 한 줄
    public int ADead, AWounded, DDead, DWounded, ADeserted, DDeserted;
    public string AName = "", DName = "", Realm = "", Outcome = "";
    public readonly List<ConflictFrame> Frames = new();  // 기록을 켰을 때만 쌓는다(KeepConflictLog)
    public readonly List<string> Notes = new();
}

/// <summary>
/// 분쟁 계획 C1(plan_conflict): 분쟁 틀과 거사.
/// - 거사를 일으키면 판정 한 번이 아니라 분쟁이 선다. 하루(한 해 24일이니 약 보름)마다 한 번 진행하고, 거사는 반년(12번)이 지나면 교착으로 끝난다.
/// - 발단(첫날): 꾸민 쪽이 지배자 집을 기습한다. 막는 쪽은 지배자 집안, 친위대, 지배자 편으로 기운 지배자 조직 사람, 지배자를 따르는 갈래다.
///   거점 보정은 기습이라 반(1 + 0.15 × 큰 집 단계)만 받는다. 막는 쪽이 꾸민 쪽 힘의 0.3 아래로 무너지면 지배자를 사로잡거나(거친 꾸민 사람은 죽인다) 거점을 빼앗는다.
///   그래도 지배자 집이 남아 있으면 분쟁은 이어진다(사기 −0.2).
/// - 소집(처음 두 번, 한 달): 봉신은 충성 + 작위 0.2 + 나라 0.1 − 0.3 × 거리 확률로 진압군을 보낸다(가장 집안과 군인). 꾸민 쪽으로 크게 기운 봉신은 일어선다.
///   한쪽이 힘의 70%를 넘으면 지켜보던 세력 안 사람이 30%로 그쪽에 붙는다.
/// - 교전: 막는 쪽은 힘이 꾸민 쪽의 0.8 아래면 거점에서 버틴다(방어 1 + 0.3 × 큰 집 단계). 꾸민 쪽은 힘이 그 방어의 0.6을 넘으면 친다.
///   맞붙으면 양쪽은 상대 힘 × 0.05 × 운(0.5~1.5)만큼 사람을 잃는다. 잃은 사람의 40%는 죽고 나머지는 다쳐 빠진다. 우두머리도 낮은 확률(0.3배)로 맞는다.
///   버티는 동안 포위되면 거점 식량이 하루(약 보름)마다 5% 준다. 식량이 한 달 치 아래면 사기 −0.15.
/// - 사기: 진 쪽 −0.1, 이긴 쪽 +0.05. 0.3 아래면 싸우는 사람이 0.2 × (1 − 조직 충성)으로 달아난다(광신도는 남는다).
/// - 결말: 한쪽 힘이 상대의 0.3 아래, 사기 0.15 아래, 꾸민 우두머리의 죽음은 지는 것. 지배자 집에 어른이 없으면 꾸민 쪽이 이긴다.
///   꾸민 쪽이 이기면 찬탈(R13 계승·흡수), 막는 쪽이 이기면 꾸민 사람을 벌하고, 교착이면 편든 이들과 떨어져 나간다.
/// - 대비: 지배자 군인의 30%(나라는 40%)는 친위대로 언제나 지배자 편이다. 거사는 마음먹은 해에는 일으키지 못한다. NPC도 흔들린 어림으로 정한다.
/// </summary>
public sealed partial class World
{
    private readonly List<Conflict> _conflicts = new();
    private int _conflictIds;

    /// <summary>진단용: 켜면 분쟁마다 하루(약 보름) 단위 기록을 남기고, 끝난 분쟁도 ConflictLog 에 둔다.</summary>
    public bool KeepConflictLog;
    public readonly List<Conflict> ConflictLog = new();

    private void Note(Conflict c, string s) { if (KeepConflictLog) c.Notes.Add(s); }

    private void Snap(Conflict c, bool clashed)
    {
        if (!KeepConflictLog) return;
        var f = new ConflictFrame
        {
            Tick = c.Ticks, Day = Day, AN = c.A.Fighters.Count(u => FindAlive(u) >= 0), DN = c.D.Fighters.Count(u => FindAlive(u) >= 0),
            AP = SidePower(c.A), DP = SidePower(c.D), AM = c.A.Morale, DM = c.D.Morale, Holding = c.D.Holding, Clashed = clashed,
            ADead = c.ADead, AWounded = c.AWounded, DDead = c.DDead, DWounded = c.DWounded, ADeserted = c.ADeserted, DDeserted = c.DDeserted,
            Food = Households.IsAlive(c.D.House) ? Households.Food[c.D.House] : 0f,
        };
        f.Notes.AddRange(c.Notes);
        c.Notes.Clear();
        c.Frames.Add(f);
    }

    public IReadOnlyList<Conflict> Conflicts => _conflicts;
    public bool InConflict(int house) => _conflicts.Any(c => !c.Over && (c.A.House == house || c.D.House == house));

    /// <summary>지배자가 부리는 군인(사람 번호).</summary>
    private List<int> SoldiersOf(int lord) => StaffOf(lord).Where(i => People.IsAliveIndex(i) && People.JobOf[i] == (byte)Job.Soldier && _employer.GetValueOrDefault(i, -1) == lord).ToList();

    /// <summary>친위대: 군인 가운데 가장 충성하는 30%(나라는 40%).</summary>
    private List<int> GuardOf(int lord)
    {
        var p = People;
        var s = SoldiersOf(lord);
        int head = HeadIdx(lord);
        int n = (int)MathF.Ceiling(s.Count * (NationOf(lord) != null ? 0.4f : 0.3f));
        return s.OrderByDescending(i => (head >= 0 && p.LoyalUid[i] == p.Uid[head] ? p.Loyalty[i] : 0f) + (head >= 0 ? Social.Opinion(p, i, p.Uid[head]) / 200f : 0f)).Take(n).ToList();
    }

    private float SidePower(ConflictSide s)
    {
        float f = 0f;
        foreach (int uid in s.Fighters) if (FindAlive(uid) is int i && i >= 0) f += FightPower(i);
        return f * (1f + 0.3f * WeaponCov(s.House));
    }

    private void Enlist(Conflict c, ConflictSide s, int i)
    {
        if (!People.IsAliveIndex(i) || FightPower(i) <= 0f || IsCaptive(i)) return;
        int uid = People.Uid[i];
        var other = s == c.A ? c.D : c.A;
        if (other.Fighters.Contains(uid)) return;
        s.Fighters.Add(uid);
    }

    private void EnlistHouse(Conflict c, ConflictSide s, int h, bool withSoldiers)
    {
        if (!Households.IsAlive(h)) return;
        foreach (int m in MembersOf(h)) Enlist(c, s, m);
        if (withSoldiers) foreach (int m in SoldiersOf(h)) Enlist(c, s, m);
    }

    // ------------------------------------------------------------------ 거사

    /// <summary>거사를 분쟁으로 세운다. 첫날 기습까지 한다.</summary>
    private Conflict StartCoup(Plot x, int pi, int lh)
    {
        var p = People; var hh = Households;
        var c = new Conflict { Id = ++_conflictIds, Kind = ConflictKind.Coup, Since = Day, Plot = x };
        c.A.House = x.PlotterHouse; c.A.HouseFounded = hh.FoundedDay[x.PlotterHouse]; c.A.LeaderUid = x.Plotter;
        c.D.House = x.Lord; c.D.HouseFounded = hh.FoundedDay[x.Lord]; c.D.LeaderUid = p.Uid[lh];
        _conflicts.Add(c);
        Stats.Conflicts++;
        var (est, mine, theirs) = StrikeOdds(x, false);
        foreach (int b in mine) c.Backers.Add(p.Uid[b]);
        foreach (int u in x.Backers) if (!c.Backers.Contains(u) && FindAlive(u) >= 0) c.Backers.Add(u);
        // 막는 쪽: 지배자 집안과 친위대 먼저
        EnlistHouse(c, c.D, x.Lord, false);
        foreach (int g in GuardOf(x.Lord)) Enlist(c, c.D, g);
        // 꾸민 쪽: 꾸민 집, 편든 사람의 집과 군인
        EnlistHouse(c, c.A, x.PlotterHouse, true);
        foreach (int u in c.Backers) if (FindAlive(u) is int b && b >= 0) EnlistHouse(c, c.A, p.Household[b], ClientCount(p.Household[b]) > 0 && HeadIdx(p.Household[b]) == b);
        // 꾸민 사람이 이끄는 바깥 조직의 조직원도 나선다(우두머리를 따르는 만큼)
        if (PlotterOrg(pi) is { Home: < 0 } po)
            foreach (var u in Subtree(po).ToList())
                if (u == po || RootOf(u).Id == po.Id)
                    foreach (int m in Members(u))
                        if (OrgLoyalty(po, p.Uid[m]) + RawLoyalty(m, x.Plotter) >= 0.3f) Enlist(c, c.A, m);
        // 지배자 조직: 바로 든 사람은 기울기대로, 갈래는 지휘자를 따른다(따르지 않는 사람은 지배자에게 남는다)
        var lo = LordOrg(x.Lord, false);
        if (lo != null)
        {
            foreach (int m in Members(lo))
            {
                if (m == pi || p.Household[m] == x.Lord) continue;
                float l = Lean(m, pi, x.Lord);
                if (l > 0.6f) Enlist(c, c.A, m); else if (l < 0.3f) Enlist(c, c.D, m);
            }
            foreach (var u in Subtree(lo).ToList())
            {
                if (u == lo || RootOf(u).Id != lo.Id) continue;
                int cmd = FindAlive(u.LeaderUid);
                bool rebel = cmd >= 0 && (c.Backers.Contains(u.LeaderUid) || u.LeaderUid == x.Plotter);
                bool loyal = cmd >= 0 && !rebel && Lean(cmd, pi, x.Lord) < 0.3f;
                foreach (int m in Members(u))
                {
                    if (m == cmd) { if (rebel) Enlist(c, c.A, m); else if (loyal) Enlist(c, c.D, m); continue; }
                    bool follows = cmd >= 0 && RawLoyalty(m, u.LeaderUid) >= OrgLoyalty(lo, p.Uid[m]);
                    if (rebel) { if (follows) Enlist(c, c.A, m); else Enlist(c, c.D, m); }
                    else if (loyal || !follows) Enlist(c, c.D, m);   // 지휘자가 지켜보면 지휘자를 따르지 않는 사람은 지배자에게 간다
                }
            }
        }
        foreach (int t in theirs) EnlistHouse(c, c.D, p.Household[t], ClientCount(p.Household[t]) > 0 && HeadIdx(p.Household[t]) == t);
        if (IsPlayer(pi) || IsPlayer(lh)) _fork = true;
        float pa0 = SidePower(c.A), pd0 = SidePower(c.D);
        if (KeepConflictLog)
        {
            c.AName = Names.Full(Lineage, x.Plotter); c.DName = Names.Full(Lineage, p.Uid[lh]); c.Realm = RealmName(x.Lord);
            ConflictLog.Add(c);
            Note(c, $"{c.AName}이(가) 거사를 일으켰다. 편든 세력 안 사람 {c.Backers.Count}명, 모인 사람 {c.A.Fighters.Count}명");
            Note(c, $"{c.DName} 곁에 친위대와 집안, 따르는 갈래 {c.D.Fighters.Count}명이 모였다");
            Note(c, $"어림 {est:0.00}, 실제 힘의 몫 {pa0 / MathF.Max(0.01f, pa0 + pd0):0.00}");
        }
        Chron(pi, $"{SubjName(pi)} 거사를 일으켰다(우리 {c.A.Fighters.Count}명, 지배자 곁 {c.D.Fighters.Count}명, 어림 {est:0.00}, 실제 {pa0 / MathF.Max(0.01f, pa0 + pd0):0.00})");
        Chron(lh, $"{Names.Full(Lineage, x.Plotter)}의 무리가 거사를 일으켰다(저쪽 {c.A.Fighters.Count}명, 곁 {c.D.Fighters.Count}명)");
        Surprise(c);
        return c;
    }

    /// <summary>첫날 기습: 거점 보정을 반만 받는다. 막는 쪽이 무너지면 지배자를 사로잡거나 죽인다.</summary>
    private void Surprise(Conflict c)
    {
        var p = People;
        float fort = 1f + 0.15f * ResidenceOf(c.D.House);
        Note(c, $"첫날 기습: 지배자 집을 덮쳤다(거점 보정 반만, ×{fort:0.00})");
        Clash(c, fort);
        float pa = SidePower(c.A), pd = SidePower(c.D) * fort;
        int lh = FindAlive(c.D.LeaderUid), pi = FindAlive(c.A.LeaderUid);
        if (pi < 0) { EndConflict(c, false); return; }
        if (pd < 0.3f * pa && lh >= 0)
        {
            Stats.SurpriseWins++;
            c.D.Morale -= 0.2f;
            int chon = Lineage.Chon(c.A.LeaderUid, c.D.LeaderUid);
            bool kin = chon > 0 && chon <= 3;
            Note(c, "기습이 지배자 곁을 무너뜨렸다");
            if (!IsPlayer(lh) && !kin && p.Get(pi, Trait.Aggression) > 0.6f)
            {
                Note(c, $"{c.DName}이(가) 기습에 죽었다");
                Chron(lh, $"{SubjName(lh)} 기습에 죽임을 당했다");
                Stats.Executions++;
                Die(lh, false);
            }
            else if (PlotterOrg(pi) is { } po && Capture(po, lh, 1))
            {
                Note(c, $"{c.DName}이(가) 사로잡혔다");
                Chron(pi, $"{SubjName(pi)} 기습으로 {Obj(Names.Full(Lineage, c.D.LeaderUid))} 사로잡았다");
            }
            // 지배자 집에 싸울 어른이 남지 않으면 끝난다
            if (!Households.IsAlive(c.D.House) || !MembersOf(c.D.House).Any(m => AgeYears(m) >= 16 && !IsCaptive(m))) { EndConflict(c, true); return; }
            if (HeadIdx(c.D.House) is int nh && nh >= 0) c.D.LeaderUid = p.Uid[nh];
        }
        Snap(c, true);
    }

    /// <summary>한 번 맞붙는다. fort: 막는 쪽 방어 배율. 진 쪽 사기는 내려가고 이긴 쪽은 오른다.</summary>
    private void Clash(Conflict c, float fort)
    {
        float pa = SidePower(c.A), pd = SidePower(c.D);
        if (pa <= 0f || pd <= 0f) return;
        c.Battles++; Stats.ConflictBattles++;
        float lossA = 0.05f * pd * fort * (0.5f + _rng.NextFloat());
        float lossD = 0.05f * pa / fort * (0.5f + _rng.NextFloat());
        int ad0 = c.ADead, aw0 = c.AWounded, dd0 = c.DDead, dw0 = c.DWounded;
        float la = Casualties(c, c.A, lossA), ld = Casualties(c, c.D, lossD);
        bool aWon = ld / MathF.Max(0.01f, pd) > la / MathF.Max(0.01f, pa);
        (aWon ? c.A : c.D).Morale = MathF.Min(1f, (aWon ? c.A : c.D).Morale + 0.05f);
        (aWon ? c.D : c.A).Morale -= 0.1f;
        c.Last = $"{(aWon ? "일으킨 쪽" : "막는 쪽")}이 이겼다(잃은 힘 {la:0.0} 대 {ld:0.0})";
        Note(c, $"맞붙었다{(fort > 1.01f ? $"(막는 쪽 방어 ×{fort:0.00})" : "")}: {(aWon ? "일으킨 쪽" : "막는 쪽")}이 이겼다. 일으킨 쪽 죽음 {c.ADead - ad0}·다침 {c.AWounded - aw0}, 막는 쪽 죽음 {c.DDead - dd0}·다침 {c.DWounded - dw0}");
    }

    /// <summary>side 에서 힘 loss 만큼 사람을 뺀다. 40%는 죽고 나머지는 다친다. 뺀 힘.</summary>
    private float Casualties(Conflict c, ConflictSide side, float loss)
    {
        var p = People;
        var list = side.Fighters.Where(u => FindAlive(u) >= 0).ToList();
        float removed = 0f;
        int guard = 0;
        while (removed < loss && list.Count > 0 && guard++ < 200)
        {
            int k = _rng.NextInt(list.Count);
            int uid = list[k];
            if (uid == side.LeaderUid && !_rng.Chance(0.3f)) continue;   // 우두머리는 뒤에 있다
            int i = FindAlive(uid);
            list.RemoveAt(k);
            if (i < 0) continue;
            removed += FightPower(i);
            side.Fighters.Remove(uid);
            bool att = side == c.A;
            if (_rng.Chance(0.4f)) { c.Deaths++; if (att) c.ADead++; else c.DDead++; Stats.ConflictDeaths++; Stats.RaidDeaths++; Die(i, false); }
            else { c.Wounded++; if (att) c.AWounded++; else c.DWounded++; Stats.ConflictWounded++; }
        }
        return removed;
    }

    /// <summary>하루(약 보름)마다: 진행 중인 분쟁마다 소집, 교전, 사기, 결말.</summary>
    private void ConflictTick()
    {
        foreach (var c in _conflicts.ToList())
        {
            if (c.Over) { _conflicts.Remove(c); continue; }
            c.Ticks++;
            switch (c.Kind)
            {
                case ConflictKind.Coup: CoupTick(c); break;
                case ConflictKind.Independence: IndepTick(c); break;
            }
        }
    }

    private void CoupTick(Conflict c)
    {
        var p = People; var hh = Households;
        int pi = FindAlive(c.A.LeaderUid);
        if (pi < 0) { EndConflict(c, false); return; }
        if (!hh.IsAlive(c.D.House) || hh.FoundedDay[c.D.House] != c.D.HouseFounded || !MembersOf(c.D.House).Any(m => AgeYears(m) >= 16 && !IsCaptive(m))) { EndConflict(c, true); return; }
        if (HeadIdx(c.D.House) is int nh && nh >= 0 && !IsCaptive(nh)) c.D.LeaderUid = p.Uid[nh];
        int lord = c.D.House;
        if (c.Ticks > 3 && SidePower(c.A) <= 0f) { Note(c, "일으킨 쪽에 싸울 사람이 남지 않았다"); EndConflict(c, false); return; }
        // 소집: 처음 두 번(한 달)은 봉신이 진압군을 보내거나 일어선다
        if (c.Ticks <= 2)
        {
            int lh = HeadIdx(lord);
            foreach (int v in DirectClients(lord).ToList())
            {
                if (!hh.IsAlive(v) || PatronOf(v) != lord || ClientCount(v) == 0 || v == c.A.House) continue;
                int vh = HeadIdx(v);
                if (vh < 0 || !c.Asked.Add(v)) continue;   // 봉신마다 한 번만 묻는다
                float lean = Lean(vh, pi, lord);
                if (lean > 0.6f) { int n0 = c.A.Fighters.Count; EnlistHouse(c, c.A, v, true); Note(c, $"봉신 {VassalName(v, vh)}이(가) 거사 쪽으로 일어섰다(+{c.A.Fighters.Count - n0}명)"); continue; }
                float answer = (lh >= 0 && p.LoyalUid[vh] == p.Uid[lh] ? p.Loyalty[vh] : 0.3f) + (IsTitled(v) ? 0.2f : 0f) + (NationOf(lord) != null ? 0.1f : 0f)
                               - 0.3f * MathF.Min(1f, MathF.Sqrt(Dist2(v, lord)) / Config.VassalRange);
                if (_rng.Chance(Math.Clamp(answer, 0f, 0.95f)))
                {
                    int n0 = c.D.Fighters.Count;
                    EnlistHouse(c, c.D, v, true);
                    if (c.D.Fighters.Count == n0) { Note(c, $"봉신 {VassalName(v, vh)}은(는) 응했지만 보낼 사람이 없었다"); continue; }
                    Stats.LeviesAnswered++;
                    Note(c, $"봉신 {VassalName(v, vh)}이(가) 진압군을 보냈다(+{c.D.Fighters.Count - n0}명, 응할 확률 {Math.Clamp(answer, 0f, 0.95f):0.00})");
                }
                else Note(c, $"봉신 {VassalName(v, vh)}은(는) 소집에 응하지 않았다(응할 확률 {Math.Clamp(answer, 0f, 0.95f):0.00})");
            }
        }
        float pa = SidePower(c.A), pd = SidePower(c.D);
        // 지켜보던 세력 안 사람은 크게 기운 쪽에 붙는다
        float share = pa / MathF.Max(0.01f, pa + pd);
        if (share > 0.7f || share < 0.3f)
        {
            var win = share > 0.7f ? c.A : c.D;
            int w0 = win.Fighters.Count;
            foreach (int ins in RealmInsiders(lord))
            {
                int u = p.Uid[ins];
                if (c.A.Fighters.Contains(u) || c.D.Fighters.Contains(u) || !_rng.Chance(0.3f)) continue;
                float l = Lean(ins, pi, lord);
                if (l is >= 0.3f and <= 0.6f) EnlistHouse(c, win, p.Household[ins], ClientCount(p.Household[ins]) > 0 && HeadIdx(p.Household[ins]) == ins);
            }
            if (win.Fighters.Count > w0) Note(c, $"지켜보던 사람들이 {(win == c.A ? "일으킨 쪽" : "막는 쪽")}에 붙었다(+{win.Fighters.Count - w0}명)");
            pa = SidePower(c.A); pd = SidePower(c.D);
        }
        Battle(c, 12);   // 반년(하루 = 약 보름)
    }

    /// <summary>교전 한 번(공통): 막는 쪽은 밀리면 제 거점에서 버티고, 일으킨 쪽은 칠 만하면 친다. 포위, 탈영, 기록, 결말까지. maxTicks 가 지나면 교착.</summary>
    private void Battle(Conflict c, int maxTicks)
    {
        var hh = Households;
        int seat = c.D.House;
        float pa = SidePower(c.A), pd = SidePower(c.D);
        float fort = 1f + 0.3f * ResidenceOf(seat);
        c.D.Holding = pd < 0.8f * pa;
        float defend = pd * (c.D.Holding ? fort : 1f);
        bool clashed = pa >= 0.6f * defend || !c.D.Holding;
        if (c.D.Holding) Note(c, $"막는 쪽이 거점에서 버틴다(방어 ×{fort:0.00})");
        if (clashed) Clash(c, c.D.Holding ? fort : 1f);
        else Note(c, "일으킨 쪽은 칠 만하지 않아 둘러싸고 기다린다");
        if (c.D.Holding)
        {
            hh.Food[seat] *= 0.95f;   // 포위: 거점 식량이 준다
            if (hh.Food[seat] < hh.Need[seat] * 30f) { c.D.Morale -= 0.15f; Note(c, "거점 식량이 한 달 치 아래로 떨어져 사기가 꺾였다"); }
        }
        int da0 = c.ADeserted, dd0 = c.DDeserted;
        Desert(c, c.A); Desert(c, c.D);
        if (c.ADeserted > da0) Note(c, $"일으킨 쪽에서 {c.ADeserted - da0}명이 달아났다");
        if (c.DDeserted > dd0) Note(c, $"막는 쪽에서 {c.DDeserted - dd0}명이 달아났다");
        Snap(c, clashed);
        // 결말
        pa = SidePower(c.A); pd = SidePower(c.D) * (c.D.Holding ? fort : 1f);
        if (FindAlive(c.A.LeaderUid) < 0 || pa < 0.3f * pd || c.A.Morale < 0.15f) { EndConflict(c, false); return; }
        if (pd < 0.3f * pa || c.D.Morale < 0.15f) { EndConflict(c, true); return; }
        if (c.Ticks >= maxTicks) EndConflict(c, null);
    }

    // ------------------------------------------------------------------ 독립 진압(C2)

    /// <summary>
    /// 주군(lord)이 독립을 선언한 봉신(rebel)을 진압하러 나선다. 일으킨 쪽 = 주군(진압군), 막는 쪽 = 봉신(제 거점에서 버틴다).
    /// 주군 쪽: 주군 집안, 군인 전부, 소집에 응한 다른 봉신. 봉신 쪽: 봉신 집안과 군인, 봉신을 따르는 그 아래 봉신, 독립 쪽으로 기운 다른 봉신.
    /// 한 해(24번)가 지나면 교착이고, 교착은 협상(주군이 선물을 주고 붙잡음)으로 끝난다.
    /// </summary>
    private Conflict StartIndependence(int rebel, int lord)
    {
        var p = People; var hh = Households;
        var c = new Conflict { Id = ++_conflictIds, Kind = ConflictKind.Independence, Since = Day };
        int lh = HeadIdx(lord), rh = HeadIdx(rebel);
        c.A.House = lord; c.A.HouseFounded = hh.FoundedDay[lord]; c.A.LeaderUid = p.Uid[lh];
        c.D.House = rebel; c.D.HouseFounded = hh.FoundedDay[rebel]; c.D.LeaderUid = p.Uid[rh];
        _conflicts.Add(c);
        Stats.Conflicts++;
        EnlistHouse(c, c.A, lord, true);
        EnlistHouse(c, c.D, rebel, true);
        // 봉신 아래 봉신은 봉신을 따르는 만큼 그쪽에 선다
        foreach (int v in DirectClients(rebel).ToList())
        {
            int vh = HeadIdx(v);
            if (!hh.IsAlive(v) || PatronOf(v) != rebel || ClientCount(v) == 0 || vh < 0) continue;
            float follow = p.LoyalUid[vh] == p.Uid[rh] ? p.Loyalty[vh] : 0.3f;
            if (_rng.Chance(follow)) EnlistHouse(c, c.D, v, true);
        }
        if (IsPlayer(lh) || IsPlayer(rh)) _fork = true;
        if (KeepConflictLog)
        {
            c.AName = Names.Full(Lineage, p.Uid[lh]); c.DName = Names.Full(Lineage, p.Uid[rh]); c.Realm = RealmName(rebel);
            ConflictLog.Add(c);
            Note(c, $"{c.AName}이(가) 독립을 선언한 {c.DName}을(를) 진압하러 나섰다(진압군 {c.A.Fighters.Count}명, 봉신 쪽 {c.D.Fighters.Count}명)");
        }
        Chron(lh, $"{SubjName(lh)} 독립을 선언한 {Obj(Names.Full(Lineage, p.Uid[rh]))} 진압하러 나섰다");
        Chron(rh, $"{Names.Full(Lineage, p.Uid[lh])}의 진압군이 왔다");
        Snap(c, false);
        return c;
    }

    private void IndepTick(Conflict c)
    {
        var p = People; var hh = Households;
        int lord = c.A.House, rebel = c.D.House;
        if (!hh.IsAlive(lord) || hh.FoundedDay[lord] != c.A.HouseFounded) { EndConflict(c, false); return; }
        if (!hh.IsAlive(rebel) || hh.FoundedDay[rebel] != c.D.HouseFounded || !MembersOf(rebel).Any(m => AgeYears(m) >= 16 && !IsCaptive(m))) { EndConflict(c, true); return; }
        if (HeadIdx(lord) is int lh && lh >= 0) c.A.LeaderUid = p.Uid[lh];
        if (HeadIdx(rebel) is int rh && rh >= 0) c.D.LeaderUid = p.Uid[rh];
        // 소집: 처음 두 번(한 달)은 주군의 다른 봉신이 진압군을 보내거나 독립 쪽에 선다
        if (c.Ticks <= 2)
        {
            int head = HeadIdx(lord);
            foreach (int v in DirectClients(lord).ToList())
            {
                if (!hh.IsAlive(v) || PatronOf(v) != lord || ClientCount(v) == 0 || v == rebel) continue;
                int vh = HeadIdx(v);
                if (vh < 0 || !c.Asked.Add(v)) continue;
                float loy = head >= 0 && p.LoyalUid[vh] == p.Uid[head] ? p.Loyalty[vh] : 0.3f;
                // 주군을 미워하고 독립하는 봉신을 좋게 보면 그쪽에 선다
                if (head >= 0 && loy < 0.3f && Social.Opinion(p, vh, c.D.LeaderUid) > 20f && Social.Opinion(p, vh, p.Uid[head]) < 0f)
                {
                    int n0 = c.D.Fighters.Count; EnlistHouse(c, c.D, v, true);
                    Note(c, $"봉신 {VassalName(v, vh)}이(가) 독립 쪽에 섰다(+{c.D.Fighters.Count - n0}명)");
                    continue;
                }
                float answer = loy + (IsTitled(v) ? 0.2f : 0f) + (NationOf(lord) != null ? 0.1f : 0f) - 0.3f * MathF.Min(1f, MathF.Sqrt(Dist2(v, lord)) / Config.VassalRange);
                if (_rng.Chance(Math.Clamp(answer, 0f, 0.95f)))
                {
                    int n0 = c.A.Fighters.Count;
                    EnlistHouse(c, c.A, v, true);
                    if (c.A.Fighters.Count == n0) { Note(c, $"봉신 {VassalName(v, vh)}은(는) 응했지만 보낼 사람이 없었다"); continue; }
                    Stats.LeviesAnswered++;
                    Note(c, $"봉신 {VassalName(v, vh)}이(가) 진압군에 합류했다(+{c.A.Fighters.Count - n0}명, 응할 확률 {Math.Clamp(answer, 0f, 0.95f):0.00})");
                }
                else Note(c, $"봉신 {VassalName(v, vh)}은(는) 소집에 응하지 않았다(응할 확률 {Math.Clamp(answer, 0f, 0.95f):0.00})");
            }
        }
        if (c.Ticks > 2 && SidePower(c.A) <= 0f)
        {
            // 양쪽 다 싸울 사람이 없으면 지친 채 협상한다
            if (SidePower(c.D) <= 0f) { Note(c, "양쪽 다 싸울 사람이 남지 않았다"); EndConflict(c, null); return; }
            Note(c, "진압군에 싸울 사람이 남지 않았다"); EndConflict(c, false); return;
        }
        Battle(c, 24);   // 한 해
    }

    /// <summary>사기가 0.3 아래면 싸우는 사람이 0.2 × (1 − 조직 충성)으로 달아난다(광신도는 남는다).</summary>
    private void Desert(Conflict c, ConflictSide s)
    {
        if (s.Morale >= 0.3f) return;
        var p = People;
        var root = LordOrg(s.House, false);
        foreach (int uid in s.Fighters.ToList())
        {
            if (uid == s.LeaderUid) continue;
            int i = FindAlive(uid);
            if (i < 0) { s.Fighters.Remove(uid); continue; }
            if (p.Fanatic[i] != 0 || (root != null && IsZealot(uid, root))) continue;
            float loy = root != null ? OrgLoyalty(root, uid) : 0.3f;
            if (_rng.Chance(0.2f * (1f - loy))) { s.Fighters.Remove(uid); c.Deserted++; if (s == c.A) c.ADeserted++; else c.DDeserted++; Stats.ConflictDeserted++; }
        }
    }

    /// <summary>분쟁을 끝낸다. attackerWon: true 일으킨 쪽, false 막는 쪽, null 교착.</summary>
    private void EndConflict(Conflict c, bool? attackerWon)
    {
        if (c.Over) return;
        c.Over = true;
        bool indep = c.Kind == ConflictKind.Independence;
        c.Outcome = indep ? (attackerWon == true ? "진압" : attackerWon == false ? "독립" : "협상") : (attackerWon == true ? "찬탈" : attackerWon == false ? "진압" : "교착");
        Note(c, indep ? (attackerWon == true ? "진압군이 이겼다" : attackerWon == false ? "봉신이 진압군을 물리치고 홀로 섰다" : "한 해가 지나 협상으로 끝났다")
                      : (attackerWon == true ? "일으킨 쪽이 이겼다" : attackerWon == false ? "막는 쪽이 진압했다" : "반년이 지나 교착에 빠졌다"));
        Snap(c, false);
        var p = People;
        int pi = FindAlive(c.A.LeaderUid), lh = HeadIdx(c.D.House);
        if (c.Kind == ConflictKind.Coup && c.Plot is { } x)
        {
            var backers = c.Backers.Select(u => FindAlive(u)).Where(i => i >= 0).ToList();
            if (IsPlayer(pi) || IsPlayer(lh)) _fork = true;
            string sum = $"(싸움 {c.Battles}번, 죽음 {c.Deaths}, 다침 {c.Wounded}, 달아남 {c.Deserted})";
            // 사로잡힌 지배자는 결말에 따라 풀려나거나 새 주인 손에 넘어간다
            if (lh >= 0 && IsCaptive(lh)) Release(lh);
            if (attackerWon == true && pi >= 0 && (!Households.IsAlive(x.Lord) || Households.FoundedDay[x.Lord] != c.D.HouseFounded || lh < 0))
            {
                // 지배자 집이 끊겼다(지배권은 혈족 집이 이었다): 꾸민 사람은 그 아래에서 벗어나 홀로 선다
                Stats.CoupWins++;
                int ph = p.Household[pi];
                if (Households.IsAlive(ph) && PatronOf(ph) >= 0) { _patron[ph] = -1; Resnap(ph); }
                EnsureRealmArrays();
                if (Households.IsAlive(ph)) _legit[ph] = (byte)Legitimacy.Self;
                Chron(pi, $"{SubjName(pi)} 거사로 지배자 집을 끊고 홀로 섰다{sum}");
            }
            else if (attackerWon == true && pi >= 0)
            {
                Stats.CoupWins++;
                string ln = lh >= 0 ? Names.Full(Lineage, p.Uid[lh]) : RealmName(x.Lord);
                Chron(pi, $"{SubjName(pi)} 거사 끝에 {ln}의 자리를 빼앗았다{sum}");
                if (lh >= 0) Chron(lh, $"{SubjName(lh)} {Names.Full(Lineage, x.Plotter)}의 거사에 자리를 빼앗겼다{sum}");
                if (lh >= 0) TakeSeat(pi, x.Lord, backers, x.Absorb);
            }
            else if (attackerWon == false)
            {
                Stats.CoupFails++;
                if (lh >= 0) Chron(lh, $"{Names.Full(Lineage, x.Plotter)}의 거사를 진압했다{sum}");
                foreach (int b in backers) { if (lh >= 0) { Social.Add(p, lh, p.Uid[b], -40f); Social.Add(p, b, p.Uid[lh], -20f, 30f); } }
                if (pi >= 0) PunishPlotter(x, pi, lh, "거사에 실패해");
            }
            else if (pi >= 0)
            {
                Stats.CivilWars++;
                Chron(pi, $"{SubjName(pi)} 거사가 교착에 빠져, 편든 이들과 떨어져 나왔다{sum}");
                if (lh >= 0) Chron(lh, $"{Names.Full(Lineage, x.Plotter)}의 거사가 교착에 빠졌고, 그가 편든 이들과 떨어져 나갔다{sum}");
                if (lh >= 0) Split(pi, x.Lord, backers);
            }
            if (lh >= 0 && p.IsAliveIndex(lh)) foreach (var uid in c.D.Fighters) if (FindAlive(uid) is int d && d >= 0 && d != lh) Social.Add(p, lh, uid, 5f);
        }
        else if (c.Kind == ConflictKind.Independence)
        {
            // 일으킨 쪽 = 주군(진압군), 막는 쪽 = 독립을 선언한 봉신
            int lordHead = HeadIdx(c.A.House), rebelHead = HeadIdx(c.D.House);
            if (IsPlayer(lordHead) || IsPlayer(rebelHead)) _fork = true;
            string sum = $"(싸움 {c.Battles}번, 죽음 {c.Deaths}, 다침 {c.Wounded}, 달아남 {c.Deserted})";
            if (lordHead >= 0 && rebelHead >= 0)
            {
                if (attackerWon == true) { Stats.IndepSuppressed++; SuppressWon(lordHead, rebelHead); Chron(lordHead, $"독립을 진압했다{sum}"); }
                else if (attackerWon == false) { Stats.Independences++; SuppressLost(lordHead, rebelHead); Chron(rebelHead, $"진압군을 물리쳤다{sum}"); }
                else { Negotiated(rebelHead, lordHead); Chron(lordHead, $"진압이 교착에 빠져 협상으로 끝냈다{sum}"); }
            }
            else if (rebelHead >= 0 && attackerWon != true && Households.IsAlive(c.D.House) && PatronOf(c.D.House) == c.A.House)
            {
                // 주군 집이 끊겼다: 봉신은 홀로 선다
                _patron[c.D.House] = -1; Resnap(c.D.House);
                Stats.Independences++;
            }
        }
        RecountClients(); RecountTree();
    }

    /// <summary>거사를 일으킬 수 있나: 마음먹은 해에는 못 한다.</summary>
    public bool CanStrike(Plot x) => PlotLive(x) && Day - x.Since >= Config.DaysPerYear && !InConflict(x.Lord);

    private string VassalName(int v, int vh) => RealmName(v) is { Length: > 0 } r ? r : Names.Full(Lineage, People.Uid[vh]);

    // 시험용
    public void TickConflicts() => ConflictTick();
}
