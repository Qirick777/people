namespace People.Sim;

/// <summary>세력 안 직책. 새 직책은 여기에 값을 더하고 아래 등록부에 정의를 더한다.</summary>
public enum RoleKind : byte { Member, Commander, Treasurer, Liaison, Teacher, Student, Crafter, Priest, Worker, Bouncer, Tout, Nanny }

/// <summary>임금 다섯 단계.</summary>
public enum WageLevel : byte { None, Low, Normal, High, Lavish }

/// <summary>직책 하나의 정의. 배치 창, 임명 창, 임금 표, 신분별 교리 강도가 모두 이 등록부를 읽는다.</summary>
public sealed class RoleDef
{
    public RoleKind Kind;
    public string Name = "";
    public Func<World, Group, bool> AppliesTo = (_, _) => true;   // 이 갈래에 생기는 직책인가
    public int MinAge = 16;
    public float WageWeight = 1f;                                   // 기준 임금에 곱하는 무게(0이면 임금이 없는 직책)
    public Func<World, int, Group, bool> Holds = (_, _, _) => false; // i 가 갈래 u 에서 이 직책인가
}

/// <summary>
/// 세력 개편 R1(plan_reform): 직책 등록부, 신분별 임금, 조직 충성과 개인 충성.
/// - 직책: 갈래의 일로 생기는 자리. 사람의 직책은 등록부를 차례로 훑어 처음 맞는 것이다(없으면 조직원).
/// - 배치: 생기는 갈래인가 → 최소 나이 → 예외 규칙 목록. 기본은 어디든 보낼 수 있다.
/// - 상호작용: 평소 조건 + 걸러 내기 규칙. 수락 확률은 보정 목록을 차례로 거친다.
/// - 임금: 갈래마다 직책별로 다섯 단계(0, 0.5, 1, 1.5, 3배). 기준은 직책 무게 × 2 은화(상근이면 ×3).
///   받은 만큼 조직 충성이 해마다 오르고(초호화면 크게), 0.3 쪽으로 10%씩 돌아간다. 우두머리 집에서 주면 대신 우두머리 개인 충성이 오른다.
/// - 개인 충성(사람을 향함) = 함께한 햇수 × 0.1 + 보는 눈 ÷ 200 + 쌓인 덤. 장악도는 조직원의 지휘자 개인 충성 평균이다.
/// </summary>
public sealed partial class World
{
    public readonly List<RoleDef> Roles = new();
    public readonly List<Func<World, int, Group, RoleDef, bool>> PlacementRules = new();
    public readonly List<Func<World, InteractionDef, InteractionContext, bool>> OfferRules = new();
    public readonly List<Func<World, InteractionDef, InteractionContext, float, float>> AcceptMods = new();

    private readonly Dictionary<(int root, int uid), float> _orgLoyal = new();
    private readonly Dictionary<(int uid, int target), float> _plBonus = new();

    public static readonly float[] WageMult = { 0f, 0.5f, 1f, 1.5f, 3f };
    public static string WageName(WageLevel l) => l switch { WageLevel.Low => "적음", WageLevel.Normal => "보통", WageLevel.High => "많음", WageLevel.Lavish => "초호화", _ => "없음" };

    private void InitRoles()
    {
        // 앞에 있는 직책이 먼저 맞는다
        Roles.Add(new RoleDef { Kind = RoleKind.Commander, Name = "지휘자", WageWeight = 2f, AppliesTo = (w, u) => !IsTop(u),
            Holds = (w, i, u) => !IsTop(u) && u.LeaderUid == w.People.Uid[i] });
        Roles.Add(new RoleDef { Kind = RoleKind.Treasurer, Name = "곳간지기", WageWeight = 1.5f, AppliesTo = (w, u) => IsTop(u),
            Holds = (w, i, u) => IsTop(u) && u.TreasurerUid == w.People.Uid[i] });
        Roles.Add(new RoleDef { Kind = RoleKind.Liaison, Name = "연락책", WageWeight = 1.5f, AppliesTo = (w, u) => IsTop(u),
            Holds = (w, i, u) => IsTop(u) && u.LiaisonUid == w.People.Uid[i] });
        Roles.Add(new RoleDef { Kind = RoleKind.Student, Name = "학생", MinAge = 5, WageWeight = 0f, AppliesTo = (w, u) => IsSchool(u),
            Holds = (w, i, u) => w._students.TryGetValue(w.People.Uid[i], out var s) && s.unit == u.Id });
        Roles.Add(new RoleDef { Kind = RoleKind.Teacher, Name = "교사", WageWeight = 1.5f, AppliesTo = (w, u) => IsSchool(u),
            Holds = (w, i, u) => IsSchool(u) && w.Adult(i) && !w._students.ContainsKey(w.People.Uid[i]) && w.CanTeach(i, w.PlanOf(u), w.RootOf(u)) });
        Roles.Add(new RoleDef { Kind = RoleKind.Priest, Name = "사제", WageWeight = 1.5f, AppliesTo = (w, u) => u.Tasks.Any(t => t.Verb == Verb.Worship),
            Holds = (w, i, u) => u.Tasks.Any(t => t.Verb == Verb.Worship) && w.Adult(i) && w.People.Piety[i] >= 0.6f });
        Roles.Add(new RoleDef { Kind = RoleKind.Crafter, Name = "장인", AppliesTo = (w, u) => u.Tasks.Any(t => t.Verb == Verb.Produce),
            Holds = (w, i, u) => u.Tasks.Any(t => t.Verb == Verb.Produce) && w.IsMaker(i) });
        Roles.Add(new RoleDef { Kind = RoleKind.Member, Name = "조직원", AppliesTo = (w, u) => true, Holds = (w, i, u) => true });
        InitBrothelRoles();
        InitNurtureRoles();
        // 붙잡힌 사람은 몸이 닿는 상호작용(성관계, 혼인)을 걸지도 받지도 않는다
        OfferRules.Add((w, def, c) => !((def.Sexual || def.Intimate) && (w.IsCaptive(c.Actor) || w.IsCaptive(c.Target) || (c.Third >= 0 && w.IsCaptive(c.Third)))));
    }

    public RoleDef RoleDefOf(RoleKind k) => Roles.First(r => r.Kind == k);

    /// <summary>i 가 갈래 u 에서 맡은 직책(등록부 순서로 처음 맞는 것). 맨 위 조직 우두머리는 직책이 없다(null).</summary>
    public RoleDef? RoleOf(int i, Group u)
    {
        if (IsTop(u) && u.LeaderUid == People.Uid[i]) return null;
        foreach (var r in Roles) if (r.AppliesTo(this, u) && r.Holds(this, i, u)) return r;
        return null;
    }

    /// <summary>사람 i 를 갈래 u 의 직책 r 에 둘 수 있나: 생기는 갈래인가 → 최소 나이 → 예외 규칙.</summary>
    public bool CanPlace(int i, Group u, RoleDef r)
    {
        if (!People.IsAliveIndex(i) || !r.AppliesTo(this, u) || AgeYears(i) < r.MinAge) return false;
        foreach (var rule in PlacementRules) if (!rule(this, i, u, r)) return false;
        return true;
    }

    /// <summary>상호작용을 지금 이 대상에게 걸 수 있나: 평소 조건 + 걸러 내기 규칙.</summary>
    public bool CanOffer(InteractionDef def, InteractionContext c)
    {
        if (!def.Can(this, c)) return false;
        foreach (var rule in OfferRules) if (!rule(this, def, c)) return false;
        return true;
    }

    /// <summary>수락 확률: 정의의 확률에 보정을 차례로 건다.</summary>
    public float AcceptChance(InteractionDef def, InteractionContext c)
    {
        float acc = def.Accept(this, c);
        foreach (var mod in AcceptMods) acc = mod(this, def, c, acc);
        return Math.Clamp(acc, 0f, 1f);
    }

    // ------------------------------------------------------------------ 임금

    public WageLevel WageOf(Group u, RoleKind k) => u.Wages.TryGetValue(k, out var l) ? l : WageLevel.None;

    public void SetWage(Group u, RoleKind k, WageLevel l) { u.Wages[k] = l; RootOf(u).WagesManual = true; }

    /// <summary>직책 k 의 기준 임금(보통일 때 한 사람 한 해 은화).</summary>
    public float BaseWage(Group u, RoleDef r) => r.WageWeight * 2f * (u.FullTime ? 3f : 1f);

    /// <summary>i 가 갈래 u 에서 받을 한 해 임금(단계대로). 품삯 받는 군인 갈래는 지배자가 따로 준다.</summary>
    private float WageFor(int i, Group u)
    {
        if (u.Paid) return 0f;
        var r = RoleOf(i, u);
        return r == null ? 0f : BaseWage(u, r) * WageMult[(int)WageOf(u, r.Kind)];
    }

    /// <summary>
    /// NPC 세력의 임금: 번 것의 0.25 안에서 직책 무게가 큰 자리부터 보통 → 적음 순으로 채운다. 조직원은 남으면 적음.
    /// </summary>
    private void NpcWages(Group root, List<Group> units)
    {
        float earned = MathF.Max(0f, root.LastIncome) + (root.Home >= 0 && root.Home < _incTribute.Length ? _incTribute[root.Home] : 0f);
        float cap = 0.25f * earned + 0.1f * root.Treasury;
        var heads = new Dictionary<(Group, RoleKind), int>();
        foreach (var u in units)
            foreach (int m in MembersFast(u))
                if (RoleOf(m, u) is { } r && r.WageWeight > 0f) heads[(u, r.Kind)] = heads.GetValueOrDefault((u, r.Kind)) + 1;
        foreach (var u in units) u.Wages.Clear();
        foreach (var ((u, k), n) in heads.OrderByDescending(x => RoleDefOf(x.Key.Item2).WageWeight))
        {
            if (k == RoleKind.Member) continue;
            float b = BaseWage(u, RoleDefOf(k)) * n;
            if (b <= cap) { u.Wages[k] = WageLevel.Normal; cap -= b; }
            else if (0.5f * b <= cap) { u.Wages[k] = WageLevel.Low; cap -= 0.5f * b; }
        }
        foreach (var ((u, k), n) in heads)
        {
            if (k != RoleKind.Member) continue;
            float b = 0.5f * BaseWage(u, RoleDefOf(k)) * n;
            if (b <= cap) { u.Wages[k] = WageLevel.Low; cap -= b; }
        }
    }

    // ------------------------------------------------------------------ 충성

    /// <summary>세력 root 를 향한 i 의 조직 충성(0~1, 처음 0.3). 광신도는 0.8 아래로 내려가지 않는다.</summary>
    public float OrgLoyalty(Group root, int uid)
    {
        root = RootOf(root);
        float v = _orgLoyal.TryGetValue((root.Id, uid), out var x) ? x : 0.3f;
        return IsZealot(uid, root) ? MathF.Max(0.8f, v) : v;
    }

    private void AddOrgLoyalty(int root, int uid, float d) => _orgLoyal[(root, uid)] = Math.Clamp(_orgLoyal.GetValueOrDefault((root, uid), 0.3f) + d, 0f, 1f);

    /// <summary>받은 임금 배율(단계 배율 × 채운 비율)로 한 해에 오르는 조직 충성: 1.5배까지는 고르게, 그 위(초호화)는 가파르게.</summary>
    private static float WageLoyalty(float mult) => mult <= 1.5f ? 0.0267f * mult : 0.04f + 0.04f * (mult - 1.5f);

    /// <summary>i 가 uid 사람을 향한 개인 충성(0~1): 함께한 햇수 × 0.1 + 보는 눈 ÷ 200 + 쌓인 덤.</summary>
    public float PersonalLoyalty(int i, int targetUid, float years)
    {
        if (!People.IsAliveIndex(i) || targetUid < 0) return 0f;
        return Math.Clamp(0.1f * years + Social.Opinion(People, i, targetUid) / 200f + _plBonus.GetValueOrDefault((People.Uid[i], targetUid)), 0f, 1f);
    }

    private void AddPersonalBonus(int uid, int target, float d) => _plBonus[(uid, target)] = Math.Clamp(_plBonus.GetValueOrDefault((uid, target)) + d, -1f, 1f);

    /// <summary>
    /// 해마다(예산을 나눈 뒤): 임금을 치르고 충성을 움직인다. 받은 배율만큼 조직 충성(또는 우두머리 개인 충성)이 오르고,
    /// 조직 충성은 0.3 쪽으로 10%씩 돌아간다. 굶는 집 사람은 −0.02. 개인 충성의 덤은 해마다 10%씩 줄어든다.
    /// </summary>
    private void PayWages(Group root, List<Group> units, ref float paidSum)
    {
        var p = People; var hh = Households;
        int leader = FindAlive(root.LeaderUid);
        int leaderHome = leader >= 0 ? p.Household[leader] : -1;
        var seen = new HashSet<int>();
        foreach (var u in units)
            foreach (int m in MembersFast(u).ToList())
            {
                if (m == leader || !seen.Add(p.Uid[m])) continue;
                int h = p.Household[m];
                if (!hh.IsAlive(h)) continue;
                float want = WageFor(m, u);
                float got = 0f;
                if (want > 0f)
                {
                    if (root.WageFromLeader && leaderHome >= 0) got = PayCoin(leaderHome, h, want, false);
                    else { got = MathF.Min(u.Purse, want * u.Fill); u.Purse -= got; hh.Coin[h] += got; }
                    paidSum += got;
                }
                float mult = want > 0f ? WageMult[(int)WageOf(u, RoleOf(m, u)!.Kind)] * (got / want) : 0f;
                float cur = _orgLoyal.GetValueOrDefault((root.Id, p.Uid[m]), 0.3f);
                float d = 0.1f * (0.3f - cur) - (Surplus(h) < 0.3f ? 0.02f : 0f);
                if (root.WageFromLeader) { if (root.LeaderUid >= 0) AddPersonalBonus(p.Uid[m], root.LeaderUid, WageLoyalty(mult)); }
                else d += WageLoyalty(mult);
                AddOrgLoyalty(root.Id, p.Uid[m], d);
            }
    }

    private void DecayBonuses()
    {
        foreach (var k in _plBonus.Keys.ToList())
        {
            float v = _plBonus[k] * 0.9f;
            if (MathF.Abs(v) < 0.005f || FindAlive(k.uid) < 0) _plBonus.Remove(k); else _plBonus[k] = v;
        }
        foreach (var k in _orgLoyal.Keys.ToList())
            if (GroupById(k.root) == null || FindAlive(k.uid) < 0) _orgLoyal.Remove(k);
        foreach (var k in _polish.Keys.ToList()) if (FindAlive(k.uid) < 0) _polish.Remove(k);
    }

    /// <summary>임금을 세력 곳간에서 줄지(조직 충성), 우두머리 집에서 줄지(우두머리 개인 충성).</summary>
    public void SetWageSource(Group root, bool fromLeader) => RootOf(root).WageFromLeader = fromLeader;
}
