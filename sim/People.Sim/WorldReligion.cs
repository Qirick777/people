namespace People.Sim;

/// <summary>교리. 짝을 이룬 것은 서로 반대다(한 신분에 둘 다 약보다 세게 걸 수 없다).</summary>
public enum Doctrine : byte
{
    Fertility, Celibacy, Cult, Order, Equality, Poverty, Prosperity, HolyWar, NonViolence,
    Tolerance, Exclusive, Charity, Chastity, Liberty, Ancestors, Learning, Doom, Offering, Count,
}

/// <summary>교리 강도: 면제 0, 약 0.5, 중 1, 강 1.5.</summary>
public enum Strength : byte { Exempt, Weak, Mid, Strong }

/// <summary>교리를 받는 신분: 등록부의 직책에 우두머리, 신도(직책 없는 조직원), 바깥 신자를 더했다.</summary>
public enum Rank : byte { Leader, Believer, Lay, Role }

/// <summary>종교 하나: 이름, 창시자, 교리 셋과 강도, 신분별 덮어쓰기, 마을마다 알려진 정도.</summary>
public sealed class Religion
{
    public int Id;
    public string Name = "";
    public int FounderUid = -1;
    public bool Folk;                                                     // 토속 신앙(교리가 없다)
    public readonly List<(Doctrine d, Strength s)> Doctrines = new();
    public readonly Dictionary<(int rank, Doctrine d), Strength> Overrides = new();   // rank: Rank 값, 직책은 100 + RoleKind
    public readonly List<float> Known = new();                            // 마을 → 0~1
    public int LastRevised = -1;
}

/// <summary>
/// 세력 개편 R3(plan_reform): 종교와 교단.
/// - 세계에는 큰 종교 둘(지역마다 알려진 정도 0.5~0.9, 교리 강도 약)과 토속 신앙이 있다. 사람은 믿는 종교가 따로 적혀 있지 않으면 그 마을에서 가장 알려진 종교를 믿는다.
/// - 교단(옛 신도회)은 종교 하나에 속한다. 창시하면 교리를 고르지만 알려진 정도가 0(근거지 0.1)에서 시작한다. 입교하면 교리를 못 고르지만 이미 알려져 있다.
/// - 교리 강도는 신분마다 덮어쓸 수 있다(창시자만). 교단에 들지 않은 바깥 신자는 기본 강도의 절반을 받는다.
/// - 선교(설교) 성공률 × (0.2 + 0.8 × 그 마을 알려진 정도). 다른 종교를 깊이 믿는 사람은 덜 넘어온다(관용 +, 배타 −).
/// - 알려진 정도: 신자가 사는 몫 × 2 쪽으로 해마다 5%씩, 입교 한 사람마다 +0.01. 큰 종교는 처음 값 아래로 내려가지 않는다.
/// - 창시자는 10년에 한 번 교리 하나를 바꾼다. 신도 믿음 −0.2, 광신도 의심 +0.3.
/// </summary>
public sealed partial class World
{
    private readonly List<Religion> _religions = new();
    private readonly Dictionary<int, int> _religionOf = new();   // uid → 종교 id(적힌 사람만)
    private readonly List<int> _villageReligion = new();         // 마을 → 가장 알려진 종교(해마다 센다)

    public IReadOnlyList<Religion> Religions => _religions;

    public static readonly float[] StrengthMult = { 0f, 0.5f, 1f, 1.5f };
    public static string StrengthName(Strength s) => s switch { Strength.Weak => "약", Strength.Mid => "중", Strength.Strong => "강", _ => "면제" };

    public static string DoctrineName(Doctrine d) => d switch
    {
        Doctrine.Fertility => "다산", Doctrine.Celibacy => "금욕", Doctrine.Cult => "개인숭배", Doctrine.Order => "신분 질서", Doctrine.Equality => "평등",
        Doctrine.Poverty => "청빈", Doctrine.Prosperity => "번영", Doctrine.HolyWar => "성전", Doctrine.NonViolence => "불살", Doctrine.Tolerance => "관용",
        Doctrine.Exclusive => "배타", Doctrine.Charity => "구휼", Doctrine.Chastity => "순결", Doctrine.Liberty => "분방", Doctrine.Ancestors => "조상 숭배",
        Doctrine.Learning => "학문", Doctrine.Doom => "종말", _ => "봉헌",
    };

    public static string DoctrineEffect(Doctrine d) => d switch
    {
        Doctrine.Fertility => "임신 +10%(강도당), 피임·낙태를 꺼린다",
        Doctrine.Celibacy => "몸이 닿는 일 수락 −, 임신 −, 믿음이 빨리 자란다",
        Doctrine.Cult => "우두머리를 향한 믿음이 빨리 자라고 광신 문턱이 낮다. 우두머리가 바뀌면 믿음이 무너진다",
        Doctrine.Order => "지배자 아래 집이 더 충성한다. 기득권 경계 −",
        Doctrine.Equality => "소작농·일꾼이 잘 넘어온다. 공납을 끊기 쉽다. 기득권 경계 +",
        Doctrine.Poverty => "권장 헌금이 낮고 걷은 것의 절반은 굶는 신도에게 간다",
        Doctrine.Prosperity => "헌금 낼 확률 +",
        Doctrine.HolyWar => "털기 힘 +, 기득권 경계 +",
        Doctrine.NonViolence => "털기에서 사람을 죽이지 않는다. 신도의 폭력 범죄 −",
        Doctrine.Tolerance => "다른 종교 신자 선교 +, 믿음 성장 −",
        Doctrine.Exclusive => "다른 종교 신자 선교 −, 믿음 성장 +, 광신 문턱이 낮다",
        Doctrine.Charity => "해마다 굶는 신도를 돕는다(평판 +)",
        Doctrine.Chastity => "몰래 만나기·하룻밤 거래 수락 −60/35/15%, 들키면 비난 ×2/1.5/1.2",
        Doctrine.Liberty => "들켰을 때 비난 ×0.1/0.4/0.7",
        Doctrine.Ancestors => "집안 충성이 빨리 쌓인다",
        Doctrine.Learning => "글을 빨리 배운다",
        Doctrine.Doom => "믿음이 빨리 자라고 바치기를 잘 받아들인다. 광신도 의심이 해마다 쌓인다",
        _ => "헌금 낼 확률 +",
    };

    public static Doctrine? Opposite(Doctrine d) => d switch
    {
        Doctrine.Fertility => Doctrine.Celibacy, Doctrine.Celibacy => Doctrine.Fertility, Doctrine.Order => Doctrine.Equality, Doctrine.Equality => Doctrine.Order,
        Doctrine.Poverty => Doctrine.Prosperity, Doctrine.Prosperity => Doctrine.Poverty, Doctrine.HolyWar => Doctrine.NonViolence, Doctrine.NonViolence => Doctrine.HolyWar,
        Doctrine.Tolerance => Doctrine.Exclusive, Doctrine.Exclusive => Doctrine.Tolerance, Doctrine.Chastity => Doctrine.Liberty, Doctrine.Liberty => Doctrine.Chastity,
        _ => null,
    };

    // ------------------------------------------------------------------ 세계의 종교

    private static readonly string[] RelHead = { "성", "해", "청", "광", "만", "태", "천", "백" };
    private static readonly string[] RelTail = { "천교", "원교", "명교", "화교", "도교", "음교" };

    /// <summary>종교가 없으면 만든다: 토속 신앙과 큰 종교 둘(무작위 마을 근처에서 퍼짐, 교리 셋 약).</summary>
    private void EnsureReligions()
    {
        while (_villageReligion.Count < VillageList.Count) _villageReligion.Add(0);
        foreach (var r in _religions) while (r.Known.Count < VillageList.Count) r.Known.Add(r.Folk ? 0.3f : 0f);
        if (_religions.Count > 0 || VillageList.Count == 0) return;
        var folk = new Religion { Id = 0, Name = "토속 신앙", Folk = true };
        for (int v = 0; v < VillageList.Count; v++) folk.Known.Add(0.3f);
        _religions.Add(folk);
        var used = new HashSet<Doctrine>();
        for (int k = 0; k < 2; k++)
        {
            var r = new Religion { Id = _religions.Count, Name = RelHead[_rng.NextInt(RelHead.Length)] + RelTail[_rng.NextInt(RelTail.Length)] };
            while (r.Doctrines.Count < 3)
            {
                var d = (Doctrine)_rng.NextInt((int)Doctrine.Count);
                if (used.Contains(d) || r.Doctrines.Any(x => x.d == d || Opposite(x.d) == d)) continue;
                used.Add(d); r.Doctrines.Add((d, Strength.Weak));
            }
            int c = _rng.NextInt(VillageList.Count);
            var (cx, cy) = VillageCenter(c);
            for (int v = 0; v < VillageList.Count; v++)
            {
                var (vx, vy) = VillageCenter(v);
                float dist = MathF.Sqrt((vx - cx) * (vx - cx) + (vy - cy) * (vy - cy));
                r.Known.Add(Math.Clamp(0.9f - dist / 400f, 0.2f, 0.9f));
            }
            _religions.Add(r);
        }
        RecountVillageReligion();
    }

    private (float x, float y) VillageCenter(int v)
    {
        float sx = 0f, sy = 0f; int n = 0;
        foreach (int h in VillageHouses(v)) { sx += Households.X[h]; sy += Households.Y[h]; if (++n >= 20) break; }
        return n > 0 ? (sx / n, sy / n) : (0f, 0f);
    }

    private void RecountVillageReligion()
    {
        for (int v = 0; v < _villageReligion.Count; v++)
        {
            int best = 0; float bk = -1f;
            foreach (var r in _religions) if (v < r.Known.Count && r.Known[v] > bk) { bk = r.Known[v]; best = r.Id; }
            _villageReligion[v] = best;
        }
    }

    public float KnownAt(Religion r, int v) => v >= 0 && v < r.Known.Count ? r.Known[v] : 0f;

    /// <summary>사람 i 가 믿는 종교: 교단에 들었으면 그 교단의 종교, 적혀 있으면 그것, 아니면 그 마을에서 가장 알려진 종교.</summary>
    public Religion? ReligionOf(int i)
    {
        if (_religions.Count == 0) return null;
        var p = People;
        foreach (var g in GroupsOf(i)) { var root = RootOf(g); if (root.ReligionId >= 0 && root.ReligionId < _religions.Count) return _religions[root.ReligionId]; }
        if (_religionOf.TryGetValue(p.Uid[i], out int id) && id < _religions.Count) return _religions[id];
        int h = p.Household[i];
        int v = Households.IsAlive(h) ? Households.Village[h] : -1;
        return v >= 0 && v < _villageReligion.Count ? _religions[_villageReligion[v]] : _religions[0];
    }

    /// <summary>i 의 교단(종교가 있는 맨 위 조직)과 거기서의 신분.</summary>
    private (Group? root, int rank) FaithRank(int i)
    {
        var p = People;
        foreach (var g in GroupsOf(i))
        {
            var root = RootOf(g);
            if (root.ReligionId < 0) continue;
            if (root.LeaderUid == p.Uid[i]) return (root, (int)Rank.Leader);
            var r = RoleOf(i, g);
            return (root, r == null || r.Kind == RoleKind.Member ? (int)Rank.Believer : 100 + (int)r.Kind);
        }
        return (null, (int)Rank.Lay);
    }

    /// <summary>사람 i 에게 걸린 교리 d 의 강도 배율(0, 0.5, 1, 1.5). 바깥 신자는 기본 강도의 절반.</summary>
    public float DoctrineOn(int i, Doctrine d)
    {
        if (_religions.Count == 0 || !People.IsAliveIndex(i)) return 0f;
        var rel = ReligionOf(i);
        if (rel == null || rel.Folk) return 0f;
        int k = rel.Doctrines.FindIndex(x => x.d == d);
        if (k < 0) return 0f;
        var (_, rank) = FaithRank(i);
        var s = rel.Overrides.TryGetValue((rank, d), out var o) ? o : rel.Doctrines[k].s;
        return StrengthMult[(int)s] * (rank == (int)Rank.Lay ? 0.5f : 1f);
    }

    // ------------------------------------------------------------------ 창시와 입교

    /// <summary>교단 root 를 기존 종교 rel 에 들인다(입교). 신도의 종교가 그것으로 바뀐다.</summary>
    public void JoinReligion(Group root, int relId)
    {
        root = RootOf(root);
        if (relId < 0 || relId >= _religions.Count) return;
        root.ReligionId = relId;
    }

    /// <summary>
    /// 새 종교를 창시한다. 교리 셋(반대끼리는 한 신분에 함께 세게 걸 수 없으니, 고를 때는 서로 반대가 아닌 것만)과 기본 강도.
    /// 알려진 정도는 근거지 마을 0.1, 나머지 0에서 시작한다. 신도의 믿음은 절반으로 줄어든다(새 가르침).
    /// </summary>
    public Religion? FoundReligion(Group root, string name, IReadOnlyList<(Doctrine d, Strength s)> doctrines)
    {
        root = RootOf(root);
        EnsureReligions();
        if (doctrines.Count == 0 || doctrines.Count > 3) return null;
        var r = new Religion { Id = _religions.Count, Name = string.IsNullOrWhiteSpace(name) ? RelHead[_rng.NextInt(RelHead.Length)] + RelTail[_rng.NextInt(RelTail.Length)] : name, FounderUid = root.LeaderUid, LastRevised = Day };
        foreach (var x in doctrines) if (!r.Doctrines.Any(y => y.d == x.d)) r.Doctrines.Add(x);
        for (int v = 0; v < VillageList.Count; v++) r.Known.Add(v == root.Village ? 0.1f : 0f);
        _religions.Add(r);
        root.ReligionId = r.Id;
        foreach (var (uid, dv) in _devote) if (dv.Root == root.Id) dv.Faith *= 0.5f;
        int leader = FindAlive(root.LeaderUid);
        if (leader >= 0) Chron(leader, $"{SubjName(leader)} 새 가르침 {Obj(r.Name)} 열었다({string.Join("·", r.Doctrines.Select(x => DoctrineName(x.d)))})");
        return r;
    }

    /// <summary>창시자가 교리 하나를 바꿀 수 있나(10년에 한 번).</summary>
    public bool CanRevise(Group root)
    {
        root = RootOf(root);
        return root.ReligionId > 0 && root.ReligionId < _religions.Count && _religions[root.ReligionId] is { Folk: false } r
               && r.FounderUid == root.LeaderUid && (r.LastRevised < 0 || Day - r.LastRevised >= 10 * Config.DaysPerYear);
    }

    /// <summary>교리 하나를 바꾸거나(slot 자리에 d) 강도를 바꾼다. 신도 믿음 −0.2, 광신도 의심 +0.3. 강도만 바꾸는 것은 언제든 되고 대가도 없다.</summary>
    public bool ReviseDoctrine(Group root, int slot, Doctrine d, Strength s)
    {
        root = RootOf(root);
        if (root.ReligionId < 0 || root.ReligionId >= _religions.Count) return false;
        var r = _religions[root.ReligionId];
        if (r.FounderUid != root.LeaderUid || slot < 0 || slot >= r.Doctrines.Count) return false;
        if (r.Doctrines[slot].d == d) { r.Doctrines[slot] = (d, s); return true; }
        if (!CanRevise(root) || r.Doctrines.Any(x => x.d == d)) return false;
        r.Doctrines[slot] = (d, s);
        r.LastRevised = Day;
        foreach (var (_, dv) in _devote) if (dv.Root == root.Id) { if (dv.Zealot) dv.Doubt += 0.3f; else dv.Faith = MathF.Max(0f, dv.Faith - 0.2f); }
        return true;
    }

    /// <summary>
    /// 신분별 강도를 덮어쓴다(창시자만). 반대 교리가 함께 들어 있으면, 그 신분에 둘 다 약보다 세게 걸 수 없다.
    /// rank: Rank 값, 직책은 100 + RoleKind. s 가 null 이면 덮어쓰기를 지운다.
    /// </summary>
    public bool SetRankStrength(Group root, int rank, Doctrine d, Strength? s)
    {
        root = RootOf(root);
        if (root.ReligionId < 0 || root.ReligionId >= _religions.Count) return false;
        var r = _religions[root.ReligionId];
        if (r.FounderUid != root.LeaderUid || !r.Doctrines.Any(x => x.d == d)) return false;
        if (s == null) { r.Overrides.Remove((rank, d)); return true; }
        if (s > Strength.Weak && Opposite(d) is Doctrine od && r.Doctrines.Any(x => x.d == od))
        {
            var other = r.Overrides.TryGetValue((rank, od), out var o2) ? o2 : r.Doctrines.First(x => x.d == od).s;
            if (other > Strength.Weak) return false;
        }
        r.Overrides[(rank, d)] = s.Value;
        return true;
    }

    // ------------------------------------------------------------------ 해마다

    /// <summary>해마다: 알려진 정도가 신자 몫 쪽으로 움직이고, 마을마다 가장 알려진 종교를 다시 센다.</summary>
    private void ReligionYear()
    {
        EnsureReligions();
        if (_religions.Count == 0) return;
        var p = People; var hh = Households;
        var count = new Dictionary<(int rel, int v), int>();
        var houses = new int[VillageList.Count];
        for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h) && hh.Village[h] >= 0 && hh.Village[h] < houses.Length) houses[hh.Village[h]]++;
        foreach (var g in _groups)
        {
            if (!g.Alive || !IsTop(g) || g.ReligionId <= 0) continue;
            foreach (var u in UnitsOf(g))
                foreach (int m in MembersFast(u))
                {
                    int h = p.Household[m];
                    if (hh.IsAlive(h) && hh.Village[h] >= 0) count[(g.ReligionId, hh.Village[h])] = count.GetValueOrDefault((g.ReligionId, hh.Village[h])) + 1;
                }
        }
        foreach (var r in _religions)
        {
            if (r.Folk) continue;
            for (int v = 0; v < r.Known.Count && v < houses.Length; v++)
            {
                float target = houses[v] > 0 ? MathF.Min(1f, 2f * count.GetValueOrDefault((r.Id, v)) / houses[v]) : 0f;
                float floor = r.FounderUid < 0 ? 0.2f : 0f;   // 큰 종교는 바닥이 있다
                r.Known[v] = MathF.Max(floor, r.Known[v] + 0.05f * (target - r.Known[v]));
            }
        }
        RecountVillageReligion();
    }

    /// <summary>선교 한 사람: 그 마을의 알려진 정도, 그 사람이 믿던 종교와 신앙심, 교리(관용·배타·평등·신분 질서)로 넘어올 확률에 곱하는 수.</summary>
    private float ConvertMult(Group root, int i, int v)
    {
        if (root.ReligionId < 0 || root.ReligionId >= _religions.Count) return 1f;
        var rel = _religions[root.ReligionId];
        float m = 0.2f + 0.8f * KnownAt(rel, v);
        var cur = ReligionOf(i);
        if (cur != null && cur.Id != rel.Id && !cur.Folk)
        {
            float tol = RelStrength(rel, Doctrine.Tolerance) - RelStrength(rel, Doctrine.Exclusive);
            m *= Math.Clamp(1f - 0.5f * People.Piety[i] + 0.3f * tol, 0.1f, 1.5f);
        }
        var cls = ClassOf(People.Household[i]);
        if (cls is LandClass.Tenant or LandClass.Laborer) m *= 1f + 0.3f * RelStrength(rel, Doctrine.Equality);
        if (cls is LandClass.Landlord or LandClass.Ruler or LandClass.Owner) m *= 1f + 0.3f * RelStrength(rel, Doctrine.Order);
        return m;
    }

    private static float RelStrength(Religion r, Doctrine d) { int k = r.Doctrines.FindIndex(x => x.d == d); return k < 0 ? 0f : StrengthMult[(int)r.Doctrines[k].s]; }

    /// <summary>사람 i 가 교단 root 에 들었다: 종교를 그것으로 적고 알려진 정도를 조금 올린다.</summary>
    private void Converted(Group root, int i)
    {
        if (root.ReligionId < 0 || root.ReligionId >= _religions.Count) return;
        _religionOf[People.Uid[i]] = root.ReligionId;
        int h = People.Household[i];
        int v = Households.IsAlive(h) ? Households.Village[h] : -1;
        var r = _religions[root.ReligionId];
        if (v >= 0 && v < r.Known.Count) r.Known[v] = MathF.Min(1f, r.Known[v] + 0.01f);
    }

    /// <summary>교리 효과 가운데 수락 보정으로 거는 것(순결·금욕·분방, 다산의 피임·낙태, 불살의 폭력).</summary>
    private void InitDoctrineMods()
    {
        AcceptMods.Add((w, def, c, acc) =>
        {
            if (w._religions.Count == 0) return acc;
            if (def.Id is InteractionId.Affair or InteractionId.SexTrade)
            {
                float ch = w.DoctrineOn(c.Target, Doctrine.Chastity), ce = w.DoctrineOn(c.Target, Doctrine.Celibacy), li = w.DoctrineOn(c.Target, Doctrine.Liberty);
                acc *= ch >= 1.5f ? 0.4f : ch >= 1f ? 0.65f : ch >= 0.5f ? 0.85f : 1f;
                acc *= 1f - 0.3f * ce;
                acc *= 1f + 0.15f * li;
            }
            if (def.Id is InteractionId.BuyContraception or InteractionId.BuyAbortion) acc *= 1f - 0.3f * w.DoctrineOn(c.Actor, Doctrine.Fertility);
            if (def.Id is InteractionId.Assault or InteractionId.Murder) acc *= 1f - 0.4f * w.DoctrineOn(c.Actor, Doctrine.NonViolence);
            return acc;
        });
    }

    /// <summary>들킨 정을 통함에 붙는 비난 배율: 순결 강 ×2, 중 ×1.5, 약 ×1.2, 분방 강 ×0.1, 중 ×0.4, 약 ×0.7.</summary>
    private float BlameMult(int i)
    {
        float ch = DoctrineOn(i, Doctrine.Chastity), li = DoctrineOn(i, Doctrine.Liberty);
        float m = ch >= 1.5f ? 2f : ch >= 1f ? 1.5f : ch >= 0.5f ? 1.2f : 1f;
        if (li >= 1.5f) m *= 0.1f; else if (li >= 1f) m *= 0.4f; else if (li >= 0.5f) m *= 0.7f;
        return m;
    }
}
