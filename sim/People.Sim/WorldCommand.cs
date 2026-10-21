namespace People.Sim;

/// <summary>
/// 세력 틀(plan_faction 1~2장, 12.1). 조직의 정체는 하는 일의 비중에서 나온다. 사람은 직책으로 일한다.
/// - 정체: 맨 위 조직 나무 전체의 일 비중(사람 × 무게)으로 가장 큰 종류를 고른다. 두 해 연속 같아야 바뀐다.
///   위 둘이 각각 30%를 넘고 15%p 안이면 합친 이름(무장 신도회 등)을 붙인다.
/// - 지휘: 지휘자는 제 지휘 범위만큼 부린다. 넘는 사람은 절반만 일한다. 지휘자가 빈 갈래는 우두머리의 남는 범위를 나눠 쓰고,
///   일이 저절로 내려가지 않으며(가장 큰 일 하나만), 넘친 만큼 사람이 떠난다. 지휘자가 바뀐 해는 일이 20% 준다.
/// - 장악도: 지휘자가 맡은 해 × 0.1 + 조직원이 지휘자를 보는 눈/200. 거사 때 갈래 힘의 이만큼이 지휘자를 따른다.
/// - 곳간지기: 조직이 우두머리 지휘 범위를 넘으면 번 것이 새는데(5%, 넘친 만큼 더), 곳간지기의 글만큼 막는다.
/// - 연락책: 조직원 사이로 새어 나가는 것을 말솜씨와 눈치만큼 줄인다.
/// - 감시: 감시 일을 맡은 갈래의 세기(사람 × 무게 × 눈치). 제 세력 안의 거사 누설과 빼돌림 적발을 올린다.
/// </summary>
public sealed partial class World
{
    // ------------------------------------------------------------------ 정체

    /// <summary>일이 정체에 던지는 표. 보호세와 포섭은 어느 쪽도 아니다.</summary>
    private static GroupKind? VerbKind(Verb v) => v switch
    {
        Verb.Guard or Verb.Chase => GroupKind.Watch,
        Verb.Raid or Verb.Kidnap or Verb.Agitate => GroupKind.Bandits,
        Verb.Preach or Verb.Charity or Verb.Worship => GroupKind.Brotherhood,
        Verb.Trade or Verb.Escort => GroupKind.Company,
        Verb.Mercenary => GroupKind.Sellswords,
        Verb.Brothel => GroupKind.Brothel,
        Verb.Teach => GroupKind.School,
        Verb.Train => GroupKind.Drill,
        Verb.Survey => GroupKind.Inspectors,
        Verb.Produce => GroupKind.Workshop,
        _ => null,
    };

    /// <summary>맨 위 조직 root 나무 전체의 종류별 일 비중(합 1). 붙은 조직은 제 나무만 센다.</summary>
    public List<(GroupKind kind, float share)> KindShares(Group root)
    {
        var d = new Dictionary<GroupKind, float>();
        float tot = 0f;
        foreach (var u in Subtree(root))
        {
            if (u != root && RootOf(u).Id != root.Id) continue;
            float n = MathF.Max(1, u.Members);
            foreach (var t in u.Tasks)
                if (VerbKind(t.Verb) is GroupKind k) { d[k] = d.GetValueOrDefault(k) + n * t.Weight; tot += n * t.Weight; }
        }
        return d.Select(x => (x.Key, tot > 0f ? x.Value / tot : 0f)).OrderByDescending(x => x.Item2).ToList();
    }

    /// <summary>두 종류를 합친 이름.</summary>
    public static string ComboName(GroupKind a, GroupKind b)
    {
        bool Has(GroupKind x, GroupKind y) => (a == x && b == y) || (a == y && b == x);
        if (Has(GroupKind.Brotherhood, GroupKind.Drill) || Has(GroupKind.Brotherhood, GroupKind.Watch) || Has(GroupKind.Brotherhood, GroupKind.Sellswords)) return "무장 교단";
        if (Has(GroupKind.Watch, GroupKind.Drill)) return "무사단";
        if (Has(GroupKind.School, GroupKind.Brotherhood)) return "신학원";
        if (Has(GroupKind.School, GroupKind.Drill)) return "무예 학당";
        if (Has(GroupKind.Company, GroupKind.Sellswords) || Has(GroupKind.Company, GroupKind.Watch)) return "무장 상단";
        if (Has(GroupKind.Bandits, GroupKind.Brotherhood)) return "무장 교단";
        if (Has(GroupKind.Watch, GroupKind.Inspectors)) return "치안대";
        return $"{GroupKindName(a)}·{GroupKindName(b)}";
    }

    /// <summary>해마다: 홀로 선(또는 붙은) 조직의 정체와 이름을 비중으로 다시 정한다.</summary>
    private void IdentityYear()
    {
        foreach (var g in _groups)
        {
            if (!g.Alive || !IsTop(g) || g.Home >= 0 || g.Kind == GroupKind.Army) continue;
            var sh = KindShares(g);
            if (sh.Count == 0) continue;
            var (k1, s1) = sh[0];
            if (k1 != g.Kind)
            {
                if (g.PendingKind == k1) g.PendingYears++;
                else { g.PendingKind = k1; g.PendingYears = 1; }
                if (g.PendingYears >= 2)
                {
                    var old = g.Kind;
                    g.Kind = k1;
                    g.PendingYears = 0;
                    Stats.IdentityShifts++;
                    if (FindAlive(g.LeaderUid) is int ld && ld >= 0) Chron(ld, $"{Topic(g.Name)} 하는 일이 바뀌어 {GroupKindName(old)}에서 {Ro(GroupKindName(k1))} 여겨진다");
                }
            }
            else g.PendingYears = 0;
            string want = GroupKindName(g.Kind);
            if (sh.Count > 1 && s1 <= 0.5f && sh[1].share >= 0.3f && s1 - sh[1].share <= 0.15f) want = ComboName(k1, sh[1].kind);
            if (want != g.IdentityName)
            {
                if (g.PendingName == want) g.PendingNameYears++;
                else { g.PendingName = want; g.PendingNameYears = 1; }
                // 홑 이름은 종류가 이미 두 해를 기다려 바뀌었으니 바로 따른다. 합친 이름만 두 해를 기다린다
                bool single = want == GroupKindName(g.Kind);
                if (single || g.PendingNameYears >= 2 || g.IdentityName.Length == 0) { g.IdentityName = want; g.PendingNameYears = 0; }
            }
            if (g.NameFixed || g.IdentityName.Length == 0) continue;
            if (g.NameFamily.Length == 0) g.NameFamily = Names.Family(Lineage.FamilyOf(g.LeaderUid));
            g.Name = $"{g.NameFamily} {g.IdentityName}";
        }
    }

    // ------------------------------------------------------------------ 지휘와 직책

    /// <summary>지휘자가 빈 갈래인가(맨 위 조직은 늘 우두머리가 있다).</summary>
    public bool NoCommander(Group g) => !IsTop(g) && g.Home < 0 && FindAlive(g.LeaderUid) < 0;

    /// <summary>
    /// 해마다(일 하기 전): 갈래마다 지휘 범위를 넘은 몫, 교대 벌점, 장악도, 감시 세기를 센다. 넘친 갈래의 사람은 떠날 수 있다.
    /// NPC 조직은 곳간지기와 연락책을 스스로 앉힌다.
    /// </summary>
    private void CommandYear() => ComputeCommand(true);

    /// <summary>지휘 셈만 다시 한다(사람을 떠나보내지 않는다). 화면과 시험에서 부른다.</summary>
    public void RecountCommand() => ComputeCommand(false);

    /// <summary>정체 판정을 한 해 치 한다(시험용).</summary>
    public void RecountIdentity() => IdentityYear();

    private void ComputeCommand(bool yearly)
    {
        var p = People;
        foreach (var root in _groups.ToList())
        {
            if (!root.Alive || !IsTop(root)) continue;
            int leader = FindAlive(root.LeaderUid);
            var units = Subtree(root).Where(u => u == root || RootOf(u).Id == root.Id).ToList();
            int L = leader >= 0 ? CommandSpan(leader) : 4;
            int rootN = MembersFast(root).Count(m => m != leader);
            var vacant = units.Where(u => u != root && NoCommander(u)).ToList();
            float shareSpan = vacant.Count > 0 ? MathF.Max(1f, (L - rootN) / (float)vacant.Count) : 0f;
            foreach (var u in units)
            {
                int cmd = u == root ? leader : FindAlive(u.LeaderUid);
                float span = u == root ? L : cmd >= 0 ? CommandSpan(cmd) : shareSpan;
                var ms = MembersFast(u);
                int n = ms.Count(m => m != cmd);
                u.Over = n > span ? (n - span) / span : 0f;
                u.CmdFactor = n > span ? (span + 0.5f * (n - span)) / n : 1f;
                int cuid = cmd >= 0 ? p.Uid[cmd] : -1;
                if (u.CmdUid != cuid) { u.CmdSince = u.CmdUid == -2 ? u.Founded : Day; u.CmdUid = cuid; }
                // 교대한 해는 손발이 안 맞는다(세운 해는 빼고)
                if (cuid >= 0 && u.CmdSince != u.Founded && Day - u.CmdSince < Config.DaysPerYear) u.CmdFactor *= 0.8f;
                u.CmdFactor *= BudgetFactor(u);   // 예산이 모자라면 덜 한다
                if (cuid < 0) u.Grip = 0f;
                else
                {
                    // 장악도 = 조직원(스물까지)이 지휘자를 향한 개인 충성의 평균
                    float yrs = (Day - u.CmdSince) / (float)Config.DaysPerYear, sum = 0f; int k = 0;
                    foreach (int m in ms) { if (m == cmd) continue; sum += PersonalLoyalty(m, cuid, yrs); if (++k >= 20) break; }
                    u.Grip = k > 0 ? sum / k : Math.Clamp(0.1f * yrs, 0f, 1f);
                }
                u.SurveyOwn = 0f;
                foreach (var t in u.Tasks)
                {
                    if (t.Verb != Verb.Survey || ms.Count == 0) continue;
                    float eye = 0f; foreach (int m in ms) eye += p.Get(m, Trait.Perception);
                    u.SurveyOwn += eye * t.Weight * u.CmdFactor * WorkFactor(u);
                }
                // 기강: 넘친 갈래의 사람이 떠난다(품삯 받는 군인은 고용이 붙잡는다)
                if (yearly && u.Over > 0f && !u.Paid)
                {
                    float leave = MathF.Min(0.3f, 0.1f * u.Over);
                    foreach (int m in ms.ToList())
                        if (m != cmd && m != leader && _rng.Chance(leave)) { LeaveGroup(m, u); Stats.DisciplineLeft++; }
                }
            }
            if (yearly) KeepRoles(root, leader, L);
        }
        var branches = _groups.Where(u => u.Alive && !IsTop(u)).ToList();
        Stats.VacantUnits = branches.Count(NoCommander);
        Stats.MeanGrip = branches.Count > 0 ? branches.Average(u => u.Grip) : 0f;
    }

    /// <summary>곳간지기와 연락책: 사라진 사람은 비우고, NPC 조직은 커지면 스스로 앉힌다.</summary>
    private void KeepRoles(Group root, int leader, int span)
    {
        var p = People;
        bool InTree(int uid) => FindAlive(uid) is int i && i >= 0 && GroupsOf(i).Any(x => RootOf(x).Id == root.Id || x.Id == root.Id);
        if (root.TreasurerUid >= 0 && !InTree(root.TreasurerUid)) root.TreasurerUid = -1;
        if (root.LiaisonUid >= 0 && !InTree(root.LiaisonUid)) root.LiaisonUid = -1;
        if (leader < 0 || IsPlayer(leader) || root.Total <= span) return;
        var cand = Subtree(root).Where(u => u == root || RootOf(u).Id == root.Id).SelectMany(MembersFast).Where(m => m != leader && Adult(m)).Distinct().ToList();
        if (root.TreasurerUid < 0 && cand.Where(m => p.GetSkill(m, Skill.Letters) >= 0.2f).OrderByDescending(m => p.GetSkill(m, Skill.Letters)).FirstOrDefault(-1) is int tr && tr >= 0)
            root.TreasurerUid = p.Uid[tr];
        if (root.LiaisonUid < 0 && cand.Where(m => p.Uid[m] != root.TreasurerUid).OrderByDescending(m => p.Get(m, Trait.Charisma) + p.Get(m, Trait.Perception)).FirstOrDefault(-1) is int li && li >= 0)
            root.LiaisonUid = p.Uid[li];
    }

    /// <summary>번 것이 관리 손실로 새는 몫: 우두머리 지휘 범위를 넘는 조직만. 곳간지기가 있으면 5% × (1 − 글).</summary>
    public float ManageLoss(Group root)
    {
        if (root.Home >= 0) return 0f;
        int leader = FindAlive(root.LeaderUid);
        int L = leader >= 0 ? CommandSpan(leader) : 4;
        if (root.Total <= L) return 0f;
        int tr = FindAlive(root.TreasurerUid);
        if (tr >= 0) return 0.05f * (1f - People.GetSkill(tr, Skill.Letters));
        return 0.05f + 0.05f * MathF.Min(1f, (root.Total - L) / (float)L);
    }

    /// <summary>연락책이 새어 나감을 줄이는 몫(0~0.5).</summary>
    private float LiaisonGuard(Group root)
    {
        int li = FindAlive(root.LiaisonUid);
        return li < 0 ? 0f : 0.25f * (People.Get(li, Trait.Charisma) + People.Get(li, Trait.Perception));
    }

    /// <summary>조직 g 나무의 감시 세기.</summary>
    public float SurveyOf(Group g)
    {
        float s = 0f;
        foreach (var u in Subtree(g)) s += u.SurveyOwn;
        return s;
    }

    // ------------------------------------------------------------------ 플레이어가 앉히기

    /// <summary>갈래의 지휘자를 앉힌다(그 갈래나 맨 위 조직 나무의 사람). 비우려면 -1.</summary>
    public bool AppointCommander(Group u, int person)
    {
        if (IsTop(u)) return false;   // 맨 위 조직은 우두머리가 지휘한다
        if (person < 0) { u.LeaderUid = -1; return true; }
        var root = RootOf(u);
        if (!People.IsAliveIndex(person) || !Adult(person) || !GroupsOf(person).Any(x => RootOf(x).Id == root.Id)) return false;
        foreach (var x in GroupsOf(person).ToList()) if (RootOf(x).Id == root.Id && x.Id != u.Id) LeaveGroup(person, x);
        JoinGroup(person, u);
        u.LeaderUid = People.Uid[person];
        return true;
    }

    /// <summary>곳간지기(0)나 연락책(1)을 앉힌다. 비우려면 -1.</summary>
    public bool AppointRole(Group root, int role, int person)
    {
        root = RootOf(root);
        int uid = -1;
        if (person >= 0)
        {
            if (!People.IsAliveIndex(person) || !Adult(person) || People.Uid[person] == root.LeaderUid || !GroupsOf(person).Any(x => RootOf(x).Id == root.Id)) return false;
            uid = People.Uid[person];
        }
        if (role == 0) { root.TreasurerUid = uid; if (uid >= 0 && root.LiaisonUid == uid) root.LiaisonUid = -1; }
        else { root.LiaisonUid = uid; if (uid >= 0 && root.TreasurerUid == uid) root.TreasurerUid = -1; }
        return true;
    }

    /// <summary>플레이어가 갈래의 일을 비중으로 정한다: (일, 무게). 마을은 이미 맡은 곳이 있으면 그대로, 없으면 갈래 마을.</summary>
    public void SetUnitWeights(Group g, IReadOnlyList<(Verb verb, float weight)> list)
    {
        var old = g.Tasks.ToList();
        g.Tasks.Clear();
        float tot = list.Where(x => x.weight > 0f).Sum(x => x.weight);
        if (tot <= 0f) { DefaultTask(g); g.Manual = true; return; }
        foreach (var (v, w) in list)
        {
            if (w <= 0f) continue;
            int village = old.FirstOrDefault(t => t.Verb == v, new OrgTask { Village = g.Village }).Village;
            g.Tasks.Add(new OrgTask { Verb = v, Village = village, Weight = w / tot });
        }
        g.Manual = true;
    }
}
