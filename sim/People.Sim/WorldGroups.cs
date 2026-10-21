namespace People.Sim;

public enum GroupKind : byte { Watch, Bandits, Brotherhood, Army, Company, Sellswords, Brothel, School, Drill, Inspectors, Workshop }

/// <summary>
/// 모임 하나. 섬김 나무와 따로, 사람들이 가로로 묶인다. 우두머리는 uid 로 적는다.
/// 지배자의 태도: 인정(Sanction 에 지배자 집), 억누르기(진압), 눈감기(아무것도 안 함).
/// </summary>
public sealed class Group
{
    public int Id;
    public GroupKind Kind;
    public int LeaderUid, Founded, Village;
    public int Sanction = -1, SanctionFounded = -1;   // 인정한 지배자 집
    public bool Exposed;                              // 도적 떼: 정체가 알려졌다
    public bool Alive = true;
    public int Members;                               // 이 갈래에 바로 딸린 사람
    public int Total;                                 // 아래 갈래까지 모두
    public float Strength;                            // 아래 갈래까지 모은 싸움 힘(해마다 다시 센다)
    public float OwnStrength;                         // 바로 딸린 사람의 싸움 힘
    public string Name = "";
    // 조직 틀(P1.5): 위 조직, 붙은 지배자 집, 맡은 일
    public int Parent = -1;                           // 위 조직(없으면 독립)
    public int Home = -1, HomeFounded = -1;           // 이 조직이 붙은 지배자 집(세력의 조직이면)
    public bool Paid;                                 // 품삯 받는 군인 갈래(지배자가 고용한 사람들)
    public bool Manual;                               // 플레이어가 맡은 일을 정했다(AI 가 바꾸지 않는다)
    public bool NameFixed;                            // 지은 이름(단계가 바뀌어도 그대로 간다)
    public int BranchNo, BranchParent = -1;           // 갈래 번호(형 갈래가 사라져도 바뀌지 않는다)와 번호를 받을 때의 위 조직
    // O1 권위와 알려진 정도(맨 위 조직만 쓴다)
    public Visibility Visibility = Visibility.Public;
    public float Leak;                                // 새어 나간 정도(0~1, 넘치면 공개 쪽으로 한 칸)
    public float Authority, Loyalty;                  // 해마다 센다
    public readonly HashSet<int> Inner = new();       // 측근 uid(하위 조직이 있을 때만)
    public readonly HashSet<int> Pinned = new();      // 청해서 받은 측근 uid(해마다 다시 골라도 남는다)
    // O2 회칙과 곳간
    public JoinRule JoinRule;
    public ShareRule ShareRule;
    public float Treasury, Income;                    // 조직 곳간, 올해 번 것
    public float LastIncome;                          // 지난해 번 것(조직원이 받는 몫을 가늠한다)
    // O3 교육과 이음
    public Lesson Lesson;
    public float TeachShare;                          // 조직원 시간 가운데 가르치는 몫(0~0.3)
    public bool TeachManual;                          // 플레이어가 정했다(AI 가 바꾸지 않는다)
    public int HeirUid = -1;
    public float Inherit;                             // 이음(0~1): 후계자가 우두머리가 아는 것을 얼마나 넘겨받았나
    public float Shaken;                              // 억지 계승으로 흔들린 정도(0~1, 해마다 줄고 권위를 깎는다)
    public readonly List<OrgTask> Tasks = new();
    // 세력 틀(plan_faction 1~2장): 직책, 지휘, 정체
    public int TreasurerUid = -1, LiaisonUid = -1;   // 곳간지기, 연락책(맨 위 조직만)
    public int CmdUid = -2, CmdSince;                 // 지금 지휘자가 언제부터 맡았나(-2: 아직 센 적 없음)
    public float CmdFactor = 1f;                      // 지휘 범위를 넘어 줄어든 일의 몫(해마다 센다)
    public float Over;                                // 지휘 범위를 넘은 정도((사람 − 범위) / 범위)
    public float Grip;                                // 장악도(0~1): 지휘자가 조직을 쥔 정도
    public float SurveyOwn;                           // 이 갈래의 감시 세기
    public GroupKind PendingKind;                     // 정체 판정: 새로 나온 종류와 연속 해 수
    public int PendingYears;
    public string NameFamily = "";                    // 이름에 쓰는 가문 이름(세울 때)
    public string IdentityName = "";                  // 비중으로 붙은 이름(무장 신도회 등)
    public string PendingName = "";                   // 이름도 두 해 연속 같아야 바뀐다
    // 생산과 공급(plan_faction 4장)
    public readonly float[] Store = new float[World.GoodCount];   // 세력 곳간의 재화(맨 위 조직)
    public readonly float[] Kit = new float[World.GoodCount];     // 갈래가 보급받아 지닌 재화(무구)
    public Good Product = Good.Tools;                 // 생산 갈래가 만드는 것
    public bool ProductManual;                        // 플레이어가 정했다
    public readonly List<(int target, float weight)> Supply = new();   // 보낼 곳: 갈래 id, -1 곳간(비축), -2 시장(직판)
    public bool FullTime;                             // 상근: 집 농사를 하지 않고 세력이 먹인다
    public int PendingNameYears;
}

/// <summary>
/// P3 모임: 가문(족보로 저절로), 자경단, 도적 떼, 신도회. 사람마다 모임 칸 셋.
/// 생기고, 들고 나고, 우두머리를 잇고, 흩어진다. 도적 떼는 함께 털고, 자경단은 마을을 함께 지킨다.
/// </summary>
public sealed partial class World
{
    public const int GroupSlots = 3;
    private readonly List<Group> _groups = new();
    public IReadOnlyList<Group> Groups => _groups;

    public static string GroupKindName(GroupKind k) => k switch
    {
        GroupKind.Watch => "자경단", GroupKind.Bandits => "도적 떼", GroupKind.Army => "군대", GroupKind.Company => "상단",
        GroupKind.Sellswords => "용병단", GroupKind.Brothel => "유곽", GroupKind.School => "학교", GroupKind.Drill => "수련단", GroupKind.Inspectors => "감찰대", GroupKind.Workshop => "공방", _ => "신도회",
    };

    public Group? GroupById(int id) => id >= 0 && id < _groups.Count && _groups[id].Alive ? _groups[id] : null;

    public IEnumerable<Group> GroupsOf(int i)
    {
        for (int k = 0; k < GroupSlots; k++)
        {
            var g = GroupById(People.GroupIds[i * GroupSlots + k]);
            if (g != null) yield return g;
        }
    }

    public bool InGroup(int i, Group g)
    {
        for (int k = 0; k < GroupSlots; k++) if (People.GroupIds[i * GroupSlots + k] == g.Id) return true;
        return false;
    }

    private Group? GroupOfKind(int i, GroupKind kind)
    {
        foreach (var g in GroupsOf(i)) if (g.Kind == kind) return g;
        return null;
    }

    public bool JoinGroup(int i, Group g)
    {
        if (InGroup(i, g)) return true;
        for (int k = 0; k < GroupSlots; k++)
        {
            int j = i * GroupSlots + k;
            if (GroupById(People.GroupIds[j]) == null) { People.GroupIds[j] = g.Id; g.Members++; g.OwnStrength += FightPower(i); return true; }
        }
        return false;
    }

    public void LeaveGroup(int i, Group g)
    {
        for (int k = 0; k < GroupSlots; k++)
        {
            int j = i * GroupSlots + k;
            if (People.GroupIds[j] == g.Id) { People.GroupIds[j] = -1; g.Members--; g.OwnStrength = MathF.Max(0f, g.OwnStrength - FightPower(i)); }
        }
    }

    public Group FoundGroup(int leader, GroupKind kind)
    {
        int h = People.Household[leader];
        var g = new Group { Id = _groups.Count, Kind = kind, LeaderUid = People.Uid[leader], Founded = Day, Village = Households.IsAlive(h) ? Households.Village[h] : -1 };
        g.NameFamily = Names.Family(Lineage.FamilyOf(People.Uid[leader]));
        g.Name = $"{g.NameFamily} {GroupKindName(kind)}";
        g.Visibility = DefaultVisibility(kind);
        // NPC 우두머리의 성격: 아주 신중한 도적 두목은 조직원끼리도 모르게 숨기고, 고집 센 신앙인은 신도회를 숨긴다(이단)
        if (!IsPlayer(leader))
        {
            if (kind == GroupKind.Bandits && People.Get(leader, Trait.Caution) > 0.7f) g.Visibility = Visibility.Hidden;
            if (kind == GroupKind.Bandits) g.ShareRule = ShareRule.Even;   // 떼는 턴 것을 나눠 갖는다
            else if (kind == GroupKind.Brotherhood && People.Get(leader, Trait.Stubbornness) > 0.75f) g.Visibility = Visibility.Covert;
        }
        _groups.Add(g);
        DefaultTask(g);
        JoinGroup(leader, g);
        Stats.GroupsFounded++;
        Chron(leader, $"{SubjName(leader)} {Obj(g.Name)} 세웠다");
        return g;
    }

    public bool IsLeader(int i, Group g) => People.Uid[i] == g.LeaderUid;

    /// <summary>i 가 이끄는 모임 중 target 이 아직 들지 않은 첫 모임.</summary>
    public Group? LedGroupFor(int i, int target)
    {
        foreach (var g in GroupsOf(i)) if (IsLeader(i, g) && !InGroup(target, g) && GroupOfKind(target, g.Kind) == null) return g;
        return null;
    }

    private void AddGroupDefs(InteractionDef[] d)
    {
        d[(int)InteractionId.Recruit] = new InteractionDef
        {
            Id = InteractionId.Recruit, Name = "조직에 들이기", Category = InteractionCategory.Rule,
            Can = (w, c) => w.Adult(c.Target) && w.LedGroupFor(c.Actor, c.Target) is { } lg && !w.CapFull(lg) && w.GroupsOf(c.Target).Count() < GroupSlots,
            // 그 모임을 바라는 마음 + 우두머리를 보는 눈
            Accept = (w, c) =>
            {
                var g = w.LedGroupFor(c.Actor, c.Target)!;
                float gw = w.GroupWant(c.Target, g.Kind);
                float a = gw + (gw > 0f ? w.PayPull(w.RootOf(g)) : 0f) + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 100f - 0.1f;
                return Math.Clamp(a, 0.02f, 0.95f);
            },
            Success = (w, c, _) =>
            {
                var g = w.LedGroupFor(c.Actor, c.Target)!;
                w.JoinGroup(c.Target, g);
                Social.AddFrom(w.People, c.Target, c.Actor, 5f);
            },
        };
    }

    // ------------------------------------------------------------------ 가문(족보로 저절로 정해지는 모임)

    /// <summary>가문마다 가문장(가장 나이 많은 남자 가장, 없으면 가장 나이 많은 가장)과 산 사람 수.</summary>
    public Dictionary<int, (int head, int count)> Clans()
    {
        var p = People; var hh = Households;
        var map = new Dictionary<int, (int head, int count)>();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            int fam = Lineage.FamilyOf(p.Uid[i]);
            var e = map.TryGetValue(fam, out var old) ? old : (head: -1, count: 0);
            e.count++;
            bool head = hh.IsAlive(p.Household[i]) && hh.Head[p.Household[i]].Index == i;
            if (head)
            {
                int cur = e.head;
                bool better = cur < 0 || (p.Male[i] && !p.Male[cur]) || (p.Male[i] == p.Male[cur] && p.BirthDay[i] < p.BirthDay[cur]);
                if (better) e.head = i;
            }
            map[fam] = e;
        }
        return map;
    }

    // ------------------------------------------------------------------ 해마다

    public float GroupWantPublic(int i, GroupKind kind) => GroupWant(i, kind);

    private float GroupWant(int i, GroupKind kind)
    {
        var p = People;
        int h = p.Household[i];
        int age = AgeYears(i);
        float need = Households.IsAlive(h) ? Math.Clamp(1f - Surplus(h), 0f, 1f) : 1f;
        return kind switch
        {
            GroupKind.Watch => p.Male[i] && age >= 16 && age <= 50 ? 0.3f + 0.4f * p.Get(i, Trait.Aggression) + 0.3f * (1f - p.Get(i, Trait.Caution)) : 0f,
            // 늘 가난한 처지(일꾼·소작·땅 없는 집)이거나 지금 굶는 거친 남자
            GroupKind.Bandits => p.Male[i] && age >= 16 && age <= 45
                ? MathF.Max(need, ClassOf(h) is LandClass.Laborer or LandClass.Tenant or LandClass.None ? 0.7f : 0f)
                  * Math.Clamp((p.Get(i, Trait.Aggression) - 0.35f) * 2.5f, 0f, 1f) * (1f - 0.8f * p.Piety[i]) : 0f,
            GroupKind.Company => age >= 16 ? (p.JobOf[i] == (byte)Job.Trader ? 0.5f : 0f) + 0.6f * p.GetSkill(i, Skill.Trade) + 0.1f * p.Get(i, Trait.Ambition) : 0f,
            GroupKind.Sellswords => p.Male[i] && age >= 16 && age <= 40
                // 싸움 솜씨가 있어야 칼을 팔 마음이 든다
                ? (0.8f * p.GetSkill(i, Skill.Arms) + 0.3f * p.Get(i, Trait.Aggression) + 0.2f * p.Get(i, Trait.Ambition) - 0.15f) * (ClassOf(h) is LandClass.Laborer or LandClass.Tenant or LandClass.None ? 1.2f : 0.8f) : 0f,
            // 유곽: 스스로 몸 파는 일을 하는 어른, 또는 신앙이 옅고 욕심 있는 어른(포주)
            GroupKind.Brothel => age >= 16 ? (p.JobOf[i] == (byte)Job.SexWorker ? 0.6f : 0f) + MathF.Max(0f, p.Get(i, Trait.Ambition) - 0.5f) * (1f - p.Piety[i]) * 2f : 0f,
            // 학교: 배우고 싶은 젊은이와 가르칠 만한 배운 사람
            GroupKind.School => age >= 16 ? (age <= 30 ? 0.4f * p.Get(i, Trait.Intelligence) + 0.3f * p.Get(i, Trait.Ambition) - 0.1f : 0f)
                                            + (p.GetSkill(i, Skill.Letters) >= 0.5f ? 0.4f : 0f) : 0f,
            GroupKind.Drill => p.Male[i] && age >= 16 && age <= 40 ? 0.2f + 0.4f * p.Get(i, Trait.Aggression) + 0.3f * p.GetSkill(i, Skill.Arms) : 0f,
            GroupKind.Inspectors => age >= 18 ? 0.6f * p.Get(i, Trait.Perception) + 0.3f * p.Get(i, Trait.Caution) - 0.2f : 0f,
            // 공방: 손재주 있는 장인과 목수
            GroupKind.Workshop => age >= 16 ? (p.JobOf[i] is (byte)Job.Crafter or (byte)Job.Carpenter ? 0.5f : 0f) + 0.6f * MathF.Max(p.GetSkill(i, Skill.Craft), p.GetSkill(i, Skill.Carpentry)) - 0.05f : 0f,
            _ => age >= 16 ? MathF.Max(0f, p.Piety[i] - 0.6f) * 2.5f : 0f,
        };
    }

    /// <summary>조직원 한 명이 지난해 받은 몫으로 끄는 힘(0~0.4). 한 사람 한 해 먹을 것(24)을 받으면 0.4. 우두머리가 다 가지면 없다.</summary>
    private float PayPull(Group root)
    {
        if (root.Home >= 0 || root.LastIncome <= 0f) return 0f;
        float share = root.ShareRule switch { ShareRule.Even => 1f, ShareRule.Treasury => 0.5f, _ => 0f };
        float per = share * root.LastIncome / MathF.Max(1, root.Total);
        return MathF.Min(0.4f, 0.4f * per / Config.DaysPerYear);
    }

    private void GroupYear()
    {
        var p = People; var hh = Households;
        // 1) 사람 수와 힘을 다시 세고, 우두머리가 없으면 잇거나 흩어진다
        foreach (var g in _groups) if (g.Alive) { g.Members = 0; g.Strength = 0f; g.OwnStrength = 0f; }
        var leaderAlive = new HashSet<int>();
        var byGroup = new Dictionary<int, List<int>>();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            for (int k = 0; k < GroupSlots; k++)
            {
                var g = GroupById(p.GroupIds[i * GroupSlots + k]);
                if (g == null) { p.GroupIds[i * GroupSlots + k] = -1; continue; }
                g.Members++;
                g.OwnStrength += FightPower(i);
                if (p.Uid[i] == g.LeaderUid) leaderAlive.Add(g.Id);
                (byGroup.TryGetValue(g.Id, out var l) ? l : byGroup[g.Id] = new()).Add(i);
            }
        }
        foreach (var g in _groups) if (g.Alive) g.OwnStrength *= ForceFactor(g);   // 무구와 상근
        SumSubtrees();
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive) continue;
            var members = byGroup.GetValueOrDefault(g.Id);
            if (g.Home >= 0)
            {
                // 지배자의 조직: 우두머리는 지배자 가장(회원 칸 밖). 집이 끊기면 닫는다.
                if (!hh.IsAlive(g.Home) || hh.FoundedDay[g.Home] != g.HomeFounded) CloseGroup(g);
                continue;
            }
            // 바로 딸린 사람이 없는 맨 위 조직은 가장 센 갈래를 끌어올려 몸통으로 삼는다
            if (IsTop(g) && (members == null || members.Count == 0) && ChildrenOf(g).OrderByDescending(c => c.Members).FirstOrDefault() is { } big)
            {
                var bm = byGroup.GetValueOrDefault(big.Id) ?? new List<int>();
                MergeInto(big, g, bm);
                members = new List<int>(bm);
                byGroup[g.Id] = members;
                byGroup.Remove(big.Id);
            }
            // 갈래 우두머리가 갈래를 떠났으면(군인을 그만둠 등) 남은 사람 가운데서 새로 세운다
            bool lost = members == null || !members.Any(m => p.Uid[m] == g.LeaderUid);
            // 플레이어 세력의 갈래는 지휘자가 빠지면 빈자리로 남는다(플레이어가 앉힌다)
            int rl = IsTop(g) ? -1 : FindAlive(RootOf(g).LeaderUid);
            bool playerBranch = rl >= 0 && IsPlayer(rl);
            if (lost && playerBranch) { if (g.LeaderUid >= 0) { g.LeaderUid = -1; Chron(rl, $"{g.Name}의 지휘자 자리가 비었다"); } lost = false; }
            if (lost && members != null && members.Count > 0)
            {
                // 이음이 넉넉한 후계자가 잇거나, 아니면 말솜씨와 싸움 힘이 큰 어른이 억지로 잇는다(O3)
                int next = PickSuccessor(g, members);
                if (next >= 0) g.LeaderUid = p.Uid[next];
            }
            // 맨 위 조직은 갈래까지 모두 세어 둘이 안 되면 흩어진다
            bool tooSmall = !IsTop(g) ? g.Members == 0 || (g.Members < 2 && Day - g.Founded >= 2 * Config.DaysPerYear) : g.Total < 2;
            if (tooSmall || members == null || (g.LeaderUid >= 0 && !members.Any(m => p.Uid[m] == g.LeaderUid)))
            {
                if (!IsTop(g) && GroupById(g.Parent) is { } up && members != null) MergeInto(g, up, members);
                else CloseGroup(g);
                continue;
            }
            if (g.Sanction >= 0 && (!hh.IsAlive(g.Sanction) || hh.FoundedDay[g.Sanction] != g.SanctionFounded))
            {
                // 지배권이 다른 집으로 옮겨 갔으면(상속, 거사) 그 군대를 따라 새 집의 인정을 받는다. 아니면 홀로 선다
                if (IsAttached(g) && GroupById(g.Parent) is { Home: >= 0 } up2 && hh.IsAlive(up2.Home) && hh.FoundedDay[up2.Home] == up2.HomeFounded)
                { g.Sanction = up2.Home; g.SanctionFounded = up2.HomeFounded; }
                else { if (IsAttached(g)) g.Parent = -1; g.Sanction = -1; }
            }
        }
        SumSubtrees();
        // 2) 마을마다: 생기고, 들고, 나간다
        var byVillage = new Dictionary<int, List<int>>();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!Adult(i) || !hh.IsAlive(p.Household[i]) || hh.Village[p.Household[i]] < 0) continue;
            (byVillage.TryGetValue(hh.Village[p.Household[i]], out var l) ? l : byVillage[hh.Village[p.Household[i]]] = new()).Add(i);
        }
        foreach (var (v, folk) in byVillage)
        {
            float danger = DangerAt(v);
            int traders = 0, workers = 0;
            foreach (int i in folk) { if (p.JobOf[i] == (byte)Job.Trader) traders++; else if (p.JobOf[i] == (byte)Job.SexWorker) workers++; }
            int houses = VillageList.Households[v];
            foreach (GroupKind kind in new[] { GroupKind.Watch, GroupKind.Bandits, GroupKind.Brotherhood, GroupKind.Company, GroupKind.Sellswords, GroupKind.Brothel, GroupKind.School, GroupKind.Drill, GroupKind.Inspectors, GroupKind.Workshop })
            {
                // 이 마을의 그 종류 모임(가장 큰 것)
                Group? here = null;
                foreach (var g in _groups) if (g.Alive && g.Kind == kind && g.Village == v && (here == null || g.Members > here.Members)) here = g;
                // 생길 조건: 자경단은 위험한 마을, 신도회는 신앙심 깊은 사람이 여럿, 도적 떼는 거칠고 가난한 사람이 여럿
                if (here == null)
                {
                    // 빈 땅에서는 도적 떼가 쉽게 서고, 기존 세력이 꽉 잡은 땅에서는 어렵다
                    float est = Established(v);
                    float pressure = kind switch
                    {
                        GroupKind.Watch => MathF.Max(0f, danger - 0.6f) * 0.5f,
                        GroupKind.Bandits => 0.25f * (1.3f - est),
                        // 상단은 장사꾼이 여럿인 큰 마을, 용병단은 위험한 땅 근처, 유곽은 몸 파는 이가 여럿인 큰 마을
                        GroupKind.Company => traders >= 2 && houses >= 20 ? 0.15f : 0f,
                        GroupKind.Sellswords => danger > 0.4f ? 0.1f * (1f - 0.5f * est) : 0f,
                        GroupKind.Brothel => workers >= 2 && houses >= Config.TradeVillage ? 0.1f : 0f,
                        // 학교는 놀고 있는 배운 사람이 있는 큰 마을에서 선다. 수련단과 감찰대는 저절로 서지 않는다(세력이 만든다)
                        GroupKind.School => houses >= 15 && folk.Any(IsIdleScholar) ? 0.08f : 0f,
                        GroupKind.Drill or GroupKind.Inspectors => 0f,
                        // 공방은 장인이 여럿인 큰 마을에서 선다
                        GroupKind.Workshop => houses >= 15 && folk.Count(i => p.JobOf[i] is (byte)Job.Crafter or (byte)Job.Carpenter) >= 3 ? 0.05f : 0f,
                        _ => 0.25f,
                    };
                    if (!_rng.Chance(pressure)) continue;
                    var cand = folk.Where(i => !IsPlayer(i) && GroupWant(i, kind) > 0.35f && GroupOfKind(i, kind) == null).ToList();
                    if (cand.Count < 3) continue;
                    // 야심가가 앞장선다(빈 땅일수록)
                    int leader = cand.OrderByDescending(i => GroupWant(i, kind) + p.Get(i, Trait.Charisma) + (kind == GroupKind.Watch ? FightPower(i) : 0f)
                                                         + p.Get(i, Trait.Ambition) * (1f - est)).First();
                    here = FoundGroup(leader, kind);
                    // 뜻이 맞는 후보 몇이 창립 일원으로 함께 든다(숨긴 조직도 처음 모인 사람은 있다)
                    foreach (int c in cand.Where(c => c != leader).OrderByDescending(c => GroupWant(c, kind)).Take(3))
                        if (GroupsOf(c).Count() < GroupSlots && RuleAllows(here, c)) JoinGroup(c, here);
                    if (kind == GroupKind.Bandits) Stats.BanditGroups++;
                }
                // 들기와 나가기(하나의 사람이 한 해에 한 번)
                int cap = kind == GroupKind.Brotherhood ? 40 : 12 + folk.Count / 20;
                foreach (int i in folk)
                {
                    if (IsPlayer(i)) continue;
                    float want = GroupWant(i, kind);
                    if (want > 0f) want += PayPull(RootOf(here));   // 몫이 넉넉하면 들고 남는다(들 만한 사람만)
                    bool inIt = InGroup(i, here);
                    // 숨긴 조직은 스스로 찾아오지 않는다(포섭으로만), 공개할수록 잘 모인다
                    // 스스로 드는 사람은 그 조직이 센 줄 알수록 잘 든다(아는 권위)
                    if (!inIt && here.Members < cap && GroupOfKind(i, kind) == null && want > 0.35f && RuleAllows(RootOf(here), i)
                        && _rng.Chance((want - 0.35f) * 0.5f * JoinFactor(RootOf(here), i) * (0.75f + 0.5f * AuthorityPull(i, here)))) JoinGroup(i, here);
                    else if (inIt && !IsLeader(i, here) && !_students.ContainsKey(p.Uid[i]) && want + Social.Opinion(p, i, here.LeaderUid) / 200f < 0.15f && _rng.Chance(0.3f)) LeaveGroup(i, here);   // 우두머리를 좋게 보면 남는다
                }
                if (kind == GroupKind.Watch && danger < 0.1f && here.Sanction < 0 && _rng.Chance(0.1f)) CloseGroup(here);
            }
        }
        // 3) 모임 안: 서로 가까워지고, 신도회는 굶는 회원을 돕는다
        foreach (var (gid, members) in byGroup)
        {
            var g = _groups[gid];
            if (!g.Alive || members.Count < 2) continue;
            for (int t = 0; t < Math.Min(6, members.Count); t++)
            {
                int a = members[_rng.NextInt(members.Count)], b = members[_rng.NextInt(members.Count)];
                if (a == b || !p.IsAliveIndex(a) || !p.IsAliveIndex(b)) continue;
                Social.Add(p, a, p.Uid[b], 3f);
                Social.Add(p, b, p.Uid[a], 3f);
            }
            if (g.Tasks.Any(t => t.Verb == Verb.Charity)) Charity(members);
        }
        // 4) 지배자의 태도, 지배자의 군대, 일 나누기와 갈래 짜기
        OfferYear();
        OrgYear();
        Stats.Watches = _groups.Count(g => g.Alive && IsTop(g) && g.Kind == GroupKind.Watch);
        Stats.Bandits = _groups.Count(g => g.Alive && IsTop(g) && g.Kind == GroupKind.Bandits);
        Stats.Brotherhoods = _groups.Count(g => g.Alive && IsTop(g) && g.Kind == GroupKind.Brotherhood);
        Stats.GroupMembers = _groups.Where(g => g.Alive).Sum(g => g.Members);
    }

    /// <summary>신도회: 넉넉한 회원이 굶는 회원 집에 식량을 나눈다.</summary>
    private void Charity(List<int> members)
    {
        var p = People; var hh = Households;
        var hungry = members.Where(m => p.IsAliveIndex(m) && hh.IsAlive(p.Household[m]) && Surplus(p.Household[m]) < 0.6f).Select(m => p.Household[m]).Distinct().ToList();
        if (hungry.Count == 0) return;
        foreach (int m in members)
        {
            if (!p.IsAliveIndex(m)) continue;
            int h = p.Household[m];
            if (!hh.IsAlive(h) || Surplus(h) < 1.5f) continue;
            int to = hungry[_rng.NextInt(hungry.Count)];
            if (to == h) continue;
            float give = MathF.Min(hh.Food[h] * 0.05f, 30f);
            hh.Food[h] -= give;
            hh.Food[to] += give;
            Stats.CharityFood += give;
            var head = hh.Head[to];
            if (p.IsValid(head)) Social.Add(p, head.Index, p.Uid[m], 8f, 0f, 5f);
        }
    }

    /// <summary>모임이 있는 곳의 지배자(우두머리 집의 주군, 없으면 우두머리 집 자체가 지배자일 수 있다).</summary>
    private int ConcernedLord(Group g)
    {
        int leader = FindAlive(g.LeaderUid);
        if (leader < 0) return -1;
        return PatronOf(People.Household[leader]);
    }

    private int FindAlive(int uid)
    {
        if (uid < 0) return -1;
        if (_uidIndex.TryGetValue(uid, out int k) && People.IsAliveIndex(k) && People.Uid[k] == uid) return k;
        if (_uidIndexReady) return -1;
        RebuildUidIndex();
        return _uidIndex.TryGetValue(uid, out k) && People.IsAliveIndex(k) && People.Uid[k] == uid ? k : -1;
    }

    private readonly Dictionary<int, float> _watchPow = new();
    private int _watchPowDay = -1;

    /// <summary>마을 자경단의 지킬 힘(인정받았으면 더 단단하다). 하루에 한 번 모든 마을 몫을 센다.</summary>
    private float WatchPower(int v)
    {
        if (_watchPowDay != Day)
        {
            _watchPow.Clear();
            foreach (var g in _groups)
            {
                if (!g.Alive) continue;
                foreach (var t in g.Tasks)
                    if (t.Verb == Verb.Guard)
                        _watchPow[t.Village] = _watchPow.GetValueOrDefault(t.Village) + g.OwnStrength * t.Weight * WorkFactor(g) * g.CmdFactor * (FacilityOf(RootOf(g), FacilityKind.Fort, t.Village) != null ? 1.5f : 1f) * (g.Paid || g.Parent >= 0 || g.Sanction >= 0 ? 0.6f : 0.4f);
            }
            foreach (var (mv, ms) in _mercGuard) _watchPow[mv] = _watchPow.GetValueOrDefault(mv) + 0.6f * ms;   // 용병 계약
            _watchPowDay = Day;
        }
        return _watchPow.GetValueOrDefault(v);
    }

    /// <summary>계절마다: 도적 떼가 함께 턴다(굶지 않아도).</summary>
    private void BanditSeason()
    {
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive) continue;
            float w = 0f;
            foreach (var t in g.Tasks) if (t.Verb == Verb.Raid) w += t.Weight;
            if (w <= 0f) continue;
            int leader = FindAlive(g.LeaderUid);
            if (leader < 0 || (IsPlayer(leader) && !g.Manual)) continue;
            // 큰 떼는 한 계절에 여러 번 나간다(네 명마다 한 번 더)
            for (int k = 0, tries = 1 + g.Members / 4; k < tries; k++)
            {
                if (!g.Alive || !_rng.Chance(0.5f * w * WorkFactor(g) * g.CmdFactor)) continue;
                leader = FindAlive(g.LeaderUid);   // 앞선 털기에서 두목이 죽었을 수 있다
                if (leader < 0 || !Households.IsAlive(People.Household[leader])) break;
                // 맡은 털기 마을 가운데 무게대로 하나
                float roll = _rng.NextFloat() * w; int village = -1;
                foreach (var t in g.Tasks) { if (t.Verb != Verb.Raid) continue; village = t.Village; roll -= t.Weight; if (roll <= 0f) break; }
                TryRaid(People.Household[leader], 10f + 8f * g.Members, g, village);
            }
        }
    }
}
