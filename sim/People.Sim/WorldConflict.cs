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

/// <summary>분쟁 하나: 거사, 독립 진압, 전쟁. 열흘마다 한 번 진행한다.</summary>
public sealed class Conflict
{
    public int Id, Since, Ticks;
    public ConflictKind Kind;
    public readonly ConflictSide A = new(), D = new();   // 일으킨 쪽, 맞선 쪽
    public Plot? Plot;
    public readonly List<int> Backers = new();          // 거사에 편든 세력 안 사람(uid)
    public int Deaths, Wounded, Deserted, Battles;
    public bool Over;
    public string Last = "";                            // 지난 교전 한 줄
}

/// <summary>
/// 분쟁 계획 C1(plan_conflict): 분쟁 틀과 거사.
/// - 거사를 일으키면 판정 한 번이 아니라 분쟁이 선다. 열흘마다 한 번 진행하고, 거사는 반년(18번)이 지나면 교착으로 끝난다.
/// - 발단(첫날): 꾸민 쪽이 지배자 집을 기습한다. 막는 쪽은 지배자 집안, 친위대, 지배자 편으로 기운 지배자 조직 사람, 지배자를 따르는 갈래다.
///   거점 보정은 기습이라 반(1 + 0.15 × 큰 집 단계)만 받는다. 막는 쪽이 꾸민 쪽 힘의 0.3 아래로 무너지면 지배자를 사로잡거나(거친 꾸민 사람은 죽인다) 거점을 빼앗는다.
///   그래도 지배자 집이 남아 있으면 분쟁은 이어진다(사기 −0.2).
/// - 소집(처음 세 번): 봉신은 충성 + 작위 0.2 + 나라 0.1 − 0.3 × 거리 확률로 진압군을 보낸다(가장 집안과 군인). 꾸민 쪽으로 크게 기운 봉신은 일어선다.
///   한쪽이 힘의 70%를 넘으면 지켜보던 세력 안 사람이 30%로 그쪽에 붙는다.
/// - 교전: 막는 쪽은 힘이 꾸민 쪽의 0.8 아래면 거점에서 버틴다(방어 1 + 0.3 × 큰 집 단계). 꾸민 쪽은 힘이 그 방어의 0.6을 넘으면 친다.
///   맞붙으면 양쪽은 상대 힘 × 0.05 × 운(0.5~1.5)만큼 사람을 잃는다. 잃은 사람의 40%는 죽고 나머지는 다쳐 빠진다. 우두머리도 낮은 확률(0.3배)로 맞는다.
///   버티는 동안 포위되면 거점 식량이 열흘마다 5% 준다. 식량이 한 달 치 아래면 사기 −0.15.
/// - 사기: 진 쪽 −0.1, 이긴 쪽 +0.05. 0.3 아래면 싸우는 사람이 0.2 × (1 − 조직 충성)으로 달아난다(광신도는 남는다).
/// - 결말: 한쪽 힘이 상대의 0.3 아래, 사기 0.15 아래, 꾸민 우두머리의 죽음은 지는 것. 지배자 집에 어른이 없으면 꾸민 쪽이 이긴다.
///   꾸민 쪽이 이기면 찬탈(R13 계승·흡수), 막는 쪽이 이기면 꾸민 사람을 벌하고, 교착이면 편든 이들과 떨어져 나간다.
/// - 대비: 지배자 군인의 30%(나라는 40%)는 친위대로 언제나 지배자 편이다. 거사는 마음먹은 해에는 일으키지 못한다. NPC도 흔들린 어림으로 정한다.
/// </summary>
public sealed partial class World
{
    private readonly List<Conflict> _conflicts = new();
    private int _conflictIds;

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
            if (!IsPlayer(lh) && !kin && p.Get(pi, Trait.Aggression) > 0.6f)
            {
                Chron(lh, $"{SubjName(lh)} 기습에 죽임을 당했다");
                Stats.Executions++;
                Die(lh, false);
            }
            else if (PlotterOrg(pi) is { } po && Capture(po, lh, 1))
                Chron(pi, $"{SubjName(pi)} 기습으로 {Obj(Names.Full(Lineage, c.D.LeaderUid))} 사로잡았다");
            // 지배자 집에 싸울 어른이 남지 않으면 끝난다
            if (!Households.IsAlive(c.D.House) || !MembersOf(c.D.House).Any(m => AgeYears(m) >= 16 && !IsCaptive(m))) { EndConflict(c, true); return; }
            if (HeadIdx(c.D.House) is int nh && nh >= 0) c.D.LeaderUid = p.Uid[nh];
        }
    }

    /// <summary>한 번 맞붙는다. fort: 막는 쪽 방어 배율. 진 쪽 사기는 내려가고 이긴 쪽은 오른다.</summary>
    private void Clash(Conflict c, float fort)
    {
        float pa = SidePower(c.A), pd = SidePower(c.D);
        if (pa <= 0f || pd <= 0f) return;
        c.Battles++; Stats.ConflictBattles++;
        float lossA = 0.05f * pd * fort * (0.5f + _rng.NextFloat());
        float lossD = 0.05f * pa / fort * (0.5f + _rng.NextFloat());
        float la = Casualties(c, c.A, lossA), ld = Casualties(c, c.D, lossD);
        bool aWon = ld / MathF.Max(0.01f, pd) > la / MathF.Max(0.01f, pa);
        (aWon ? c.A : c.D).Morale = MathF.Min(1f, (aWon ? c.A : c.D).Morale + 0.05f);
        (aWon ? c.D : c.A).Morale -= 0.1f;
        c.Last = $"{(aWon ? "일으킨 쪽" : "막는 쪽")}이 이겼다(잃은 힘 {la:0.0} 대 {ld:0.0})";
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
            if (_rng.Chance(0.4f)) { c.Deaths++; Stats.ConflictDeaths++; Stats.RaidDeaths++; Die(i, false); }
            else { c.Wounded++; Stats.ConflictWounded++; }
        }
        return removed;
    }

    /// <summary>열흘마다: 진행 중인 분쟁마다 소집, 교전, 사기, 결말.</summary>
    private void ConflictTick()
    {
        foreach (var c in _conflicts.ToList())
        {
            if (c.Over) { _conflicts.Remove(c); continue; }
            c.Ticks++;
            switch (c.Kind)
            {
                case ConflictKind.Coup: CoupTick(c); break;
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
        // 소집: 처음 세 번은 봉신이 진압군을 보내거나 일어선다
        if (c.Ticks <= 3)
        {
            int lh = HeadIdx(lord);
            foreach (int v in DirectClients(lord).ToList())
            {
                if (!hh.IsAlive(v) || PatronOf(v) != lord || ClientCount(v) == 0 || v == c.A.House) continue;
                int vh = HeadIdx(v);
                if (vh < 0 || c.A.Fighters.Contains(p.Uid[vh]) || c.D.Fighters.Contains(p.Uid[vh])) continue;
                float lean = Lean(vh, pi, lord);
                if (lean > 0.6f) { EnlistHouse(c, c.A, v, true); continue; }
                float answer = (lh >= 0 && p.LoyalUid[vh] == p.Uid[lh] ? p.Loyalty[vh] : 0.3f) + (IsTitled(v) ? 0.2f : 0f) + (NationOf(lord) != null ? 0.1f : 0f)
                               - 0.3f * MathF.Min(1f, MathF.Sqrt(Dist2(v, lord)) / Config.VassalRange);
                if (_rng.Chance(Math.Clamp(answer, 0f, 0.95f))) { EnlistHouse(c, c.D, v, true); Stats.LeviesAnswered++; }
            }
        }
        float pa = SidePower(c.A), pd = SidePower(c.D);
        // 지켜보던 세력 안 사람은 크게 기운 쪽에 붙는다
        float share = pa / MathF.Max(0.01f, pa + pd);
        if (share > 0.7f || share < 0.3f)
        {
            var win = share > 0.7f ? c.A : c.D;
            foreach (int ins in RealmInsiders(lord))
            {
                int u = p.Uid[ins];
                if (c.A.Fighters.Contains(u) || c.D.Fighters.Contains(u) || !_rng.Chance(0.3f)) continue;
                float l = Lean(ins, pi, lord);
                if (l is >= 0.3f and <= 0.6f) EnlistHouse(c, win, p.Household[ins], ClientCount(p.Household[ins]) > 0 && HeadIdx(p.Household[ins]) == ins);
            }
            pa = SidePower(c.A); pd = SidePower(c.D);
        }
        // 교전: 막는 쪽은 밀리면 거점에서 버티고, 일으킨 쪽은 칠 만하면 친다
        float fort = 1f + 0.3f * ResidenceOf(lord);
        c.D.Holding = pd < 0.8f * pa;
        float defend = pd * (c.D.Holding ? fort : 1f);
        if (pa >= 0.6f * defend || !c.D.Holding) Clash(c, c.D.Holding ? fort : 1f);
        if (c.D.Holding)
        {
            hh.Food[lord] *= 0.95f;   // 포위: 거점 식량이 준다
            if (hh.Food[lord] < hh.Need[lord] * 30f) c.D.Morale -= 0.15f;
        }
        Desert(c, c.A); Desert(c, c.D);
        // 결말
        pa = SidePower(c.A); pd = SidePower(c.D) * (c.D.Holding ? fort : 1f);
        if (FindAlive(c.A.LeaderUid) < 0 || pa < 0.3f * pd || c.A.Morale < 0.15f) { EndConflict(c, false); return; }
        if (pd < 0.3f * pa || c.D.Morale < 0.15f) { EndConflict(c, true); return; }
        if (c.Ticks >= 18) EndConflict(c, null);
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
            if (_rng.Chance(0.2f * (1f - loy))) { s.Fighters.Remove(uid); c.Deserted++; Stats.ConflictDeserted++; }
        }
    }

    /// <summary>분쟁을 끝낸다. attackerWon: true 일으킨 쪽, false 막는 쪽, null 교착.</summary>
    private void EndConflict(Conflict c, bool? attackerWon)
    {
        if (c.Over) return;
        c.Over = true;
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
        RecountClients(); RecountTree();
    }

    /// <summary>거사를 일으킬 수 있나: 마음먹은 해에는 못 한다.</summary>
    public bool CanStrike(Plot x) => PlotLive(x) && Day - x.Since >= Config.DaysPerYear && !InConflict(x.Lord);

    // 시험용
    public void TickConflicts() => ConflictTick();
}
