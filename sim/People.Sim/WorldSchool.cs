namespace People.Sim;

/// <summary>졸업 뒤 갈 곳.</summary>
public enum SchoolDest : byte { Faction, Unit, Merit, Service, Teacher }

/// <summary>학교 설정 하나(가르침 일을 맡은 갈래마다).</summary>
public sealed class SchoolPlan
{
    public int MinAge = 8, MaxAge = 14;
    public float MaxLetters = 1f;        // 이 글 솜씨 넘는 사람은 받지 않는다(1이면 아무나)
    public float TalentMin;              // 주 과목 재능 문턱
    public bool Outsiders = true;        // 세력 밖 사람도 받는다
    public int Years = 3;
    public readonly float[] Weights = new float[(int)Lesson.Count];   // Lesson 마다 비중(합 1)
    public float Fee = 2f;               // 한 해 수업료(세력 안 학생 집이 낸다)
    public float FeeOut = 2f;            // 세력 밖 학생 집의 수업료
    public WageLevel Quality = WageLevel.Normal;   // 교육의 질(없음~초호화): 비용·비품이 들고 그만큼 빨리, 깊이 배운다
    public SchoolDest Dest;
    public int DestUnit = -1;            // 정한 조직(같은 세력)
    public float MeritLine = 0.4f;       // 성적대로: 이 솜씨 넘으면 정한 조직으로
    public int ServiceYears = 3;         // 의무 복무 해 수
    public int Graduated, Fled, Enrolled;   // 올해 센 것(화면용)
}

/// <summary>
/// 교육 조직(plan_faction 3장). 가르침 일을 맡은 갈래가 학교다. 학생은 그 갈래의 조직원이 되어 기간을 채우고, 끝나면 정한 곳으로 간다.
/// - 받는 사람: 나이, 학력 문턱, 재능 문턱, 세력 밖도 받을지. 집은 수업료와 일손을 따져 보낸다.
/// - 교사: 학생이 아닌 갈래 어른. 한 명이 4 + 6 × 말솜씨 명을 받는다. 넘치면 배우는 속도가 준다.
/// - 과목 비중: 시간을 비중대로 나눈다. 비중 25% 아래는 0.4까지, 25~50%는 0.6까지, 50% 넘으면 0.8까지 오른다. 교사 솜씨가 위를 막는다.
/// - 배우는 동안 일손은 3/4이고, 교사와 동기 사이가 가까워진다. 교리는 신앙과 우두머리를, 충성은 우두머리를 보는 눈을 올린다.
/// - 졸업: 세력원으로 돌아감, 정한 조직으로 감, 성적대로, 의무 복무 뒤 풀어 줌, 교사로 남음. 정한 조직을 싫어하는 졸업생은 달아날 수 있다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, SchoolPlan> _schoolPlans = new();               // 갈래 id → 설정
    private readonly Dictionary<int, (int unit, int start, int end)> _students = new();  // uid → 학교 갈래, 들어온 날, 끝나는 날
    private readonly Dictionary<int, (int unit, int until)> _service = new();        // uid → 의무 복무 갈래, 끝나는 날
    private readonly Dictionary<int, (int unit, int years)> _placeLater = new();     // uid → 열여섯이 되면 갈 갈래

    public static readonly Lesson[] SchoolSubjects = { Lesson.Arms, Lesson.Letters, Lesson.Trade, Lesson.Craft, Lesson.Doctrine, Lesson.Loyalty,
        Lesson.Medicine, Lesson.Carpentry, Lesson.Farming, Lesson.Etiquette, Lesson.Espionage };

    // 교육의 질(R4): 한 학생 한 해 은화, 비품(재화), 배우는 속도, 깊이 상한에 더하는 몫
    public static readonly float[] QualityCost = { 0f, 1f, 3f, 6f, 12f };
    public static readonly float[] QualityKit = { 0f, 0.1f, 0.2f, 0.4f, 0.8f };
    public static readonly float[] QualitySpeed = { 0.5f, 0.8f, 1f, 1.3f, 1.6f };
    public static readonly float[] QualityDepth = { -0.2f, -0.1f, 0f, 0.1f, 0.2f };

    private readonly Dictionary<(int uid, Lesson l), float> _polish = new();   // 기술이 아닌 배움(예법, 첩보) 0~1

    /// <summary>예법·첩보처럼 기술 칸이 없는 배움의 깊이(0~1).</summary>
    public float Polish(int i, Lesson l) => People.IsAliveIndex(i) ? _polish.GetValueOrDefault((People.Uid[i], l)) : 0f;

    /// <summary>학교 갈래의 한 해 비용: 학생 수 × 질 단계 비용.</summary>
    public float SchoolCost(Group u) => IsSchool(u) ? _students.Count(x => x.Value.unit == u.Id) * QualityCost[(int)PlanOf(u).Quality] : 0f;

    /// <summary>졸업 뒤 갈 곳으로 붙는 학교 이름.</summary>
    public string SchoolLabel(Group g)
    {
        var plan = PlanOf(g);
        if (plan.Dest == SchoolDest.Teacher) return "교육학교";
        if (plan.Dest == SchoolDest.Faction || GroupById(plan.DestUnit) is not { } t) return "학교";
        return DestKind(t) switch
        {
            GroupKind.Brotherhood => "신학당", GroupKind.School => "교육학교", GroupKind.Company => "상업학교", GroupKind.Workshop => "공예학교",
            GroupKind.Inspectors => "첩보학교", GroupKind.Watch or GroupKind.Bandits or GroupKind.Army or GroupKind.Sellswords or GroupKind.Drill => "사관학교", _ => "학교",
        };
    }

    /// <summary>갈래의 성격(가장 큰 일의 종류).</summary>
    private GroupKind DestKind(Group t)
    {
        if (t.Tasks.Count == 0) return t.Kind;
        var top = t.Tasks.OrderByDescending(x => x.Weight).First().Verb;
        return top switch
        {
            Verb.Guard or Verb.Chase or Verb.Raid or Verb.Attack or Verb.Mercenary or Verb.Train or Verb.Escort => GroupKind.Watch,
            Verb.Worship or Verb.Preach or Verb.Charity => GroupKind.Brotherhood, Verb.Teach => GroupKind.School, Verb.Trade => GroupKind.Company,
            Verb.Produce => GroupKind.Workshop, Verb.Survey => GroupKind.Inspectors, _ => t.Kind,
        };
    }

    /// <summary>갈래 성격에 맞는 과목(사관학교면 무예, 신학당이면 교리 등).</summary>
    private static Lesson FitLesson(GroupKind k) => k switch
    {
        GroupKind.Brotherhood => Lesson.Doctrine, GroupKind.School => Lesson.Letters, GroupKind.Company => Lesson.Trade, GroupKind.Workshop => Lesson.Craft,
        GroupKind.Inspectors => Lesson.Espionage, _ => Lesson.Arms,
    };

    public bool IsStudent(int i) => _students.ContainsKey(People.Uid[i]);
    private readonly HashSet<(int school, int uid)> _alumni = new();   // 졸업한 학교에는 다시 들지 않는다

    /// <summary>갈래가 학교인가(가르침 일이 있다).</summary>
    public static bool IsSchool(Group g) => g.Alive && g.Tasks.Any(t => t.Verb == Verb.Teach && t.Weight > 0f);

    /// <summary>갈래의 학교 설정(없으면 우두머리 솜씨대로 하나 만든다).</summary>
    public SchoolPlan PlanOf(Group g)
    {
        if (_schoolPlans.TryGetValue(g.Id, out var plan)) return plan;
        plan = new SchoolPlan();
        var lesson = g.Lesson is Lesson.None or Lesson.Succession ? Lesson.Letters : g.Lesson;
        if (g.Lesson == Lesson.None && FindAlive(RootOf(g).LeaderUid) is int ld && ld >= 0)
        {
            var p = People;
            var best = new[] { (Lesson.Letters, p.GetSkill(ld, Skill.Letters)), (Lesson.Arms, p.GetSkill(ld, Skill.Arms)), (Lesson.Trade, p.GetSkill(ld, Skill.Trade)), (Lesson.Craft, p.GetSkill(ld, Skill.Craft)) }
                .OrderByDescending(x => x.Item2).First();
            lesson = best.Item1;
        }
        plan.Weights[(int)lesson] = 1f;
        _schoolPlans[g.Id] = plan;
        return plan;
    }

    /// <summary>과목 비중이 오를 수 있게 하는 높이.</summary>
    public static float DepthCap(float w) => w < 0.25f ? 0.4f : w < 0.5f ? 0.6f : 0.8f;

    /// <summary>가르칠 수 있는 사람: 비중이 있는 과목 하나라도 기술 0.4, 교리는 신앙 0.6, 충성은 우두머리 본인이거나 우두머리를 40 넘게 좋게 본다.</summary>
    private bool CanTeach(int t, SchoolPlan plan, Group root)
    {
        var p = People;
        foreach (var l in SchoolSubjects)
        {
            if (plan.Weights[(int)l] <= 0f) continue;
            if (LessonSkill(l) is Skill s) { if (p.GetSkill(t, s) >= 0.4f) return true; }
            else if (l == Lesson.Doctrine) { if (p.Piety[t] >= 0.6f) return true; }
            else if (l == Lesson.Etiquette) { if (p.Get(t, Trait.Charisma) >= 0.6f) return true; }
            else if (l == Lesson.Espionage) { if (p.Get(t, Trait.Perception) >= 0.6f || Polish(t, Lesson.Espionage) >= 0.4f) return true; }
            else if (p.Uid[t] == root.LeaderUid || Social.Opinion(p, t, root.LeaderUid) >= 40f) return true;
        }
        return false;
    }

    /// <summary>교사 한 명이 받는 학생 수.</summary>
    private int TeacherLoad(int t) => 4 + (int)(6f * People.Get(t, Trait.Charisma));

    /// <summary>세력에 속한 집들: 지배자 세력이면 그 나무의 집, 아니면 조직원의 집.</summary>
    private HashSet<int> FactionHouses(Group root)
    {
        var set = new HashSet<int>();
        var hh = Households; var p = People;
        if (root.Home >= 0 && hh.IsAlive(root.Home))
        {
            set.Add(root.Home);
            for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h) && InTreeOf(h, root.Home)) set.Add(h);
        }
        foreach (var u in Subtree(root))
            foreach (int m in MembersFast(u)) if (hh.IsAlive(p.Household[m])) set.Add(p.Household[m]);
        return set;
    }

    /// <summary>해마다(조직 일 뒤): 학교마다 졸업시키고, 가르치고, 새로 받는다. 의무 복무와 미룬 배치를 푼다.</summary>
    private void EducationYear()
    {
        var p = People; var hh = Households;
        // 학교를 떠났거나 학교가 사라진 학생은 학생이 아니다
        foreach (var (uid, s0) in _students.ToList())
        {
            int i = FindAlive(uid);
            if (i < 0 || GroupById(s0.unit) is not { } su || !InGroup(i, su) || !IsSchool(su)) _students.Remove(uid);   // 학교 일을 그만둔 갈래의 학생도
        }
        _alumni.RemoveWhere(a => FindAlive(a.uid) < 0 || GroupById(a.school) is not { Alive: true });
        // 1) 학교마다
        foreach (var u in _groups.ToList())
        {
            if (!IsSchool(u)) continue;
            var plan = PlanOf(u);
            plan.Graduated = plan.Fled = plan.Enrolled = 0;
            var root = RootOf(u);
            int rootLeader = FindAlive(root.LeaderUid);
            var ms = MembersFast(u).ToList();
            var students = ms.Where(m => _students.TryGetValue(p.Uid[m], out var s) && s.unit == u.Id).ToList();
            var teachers = ms.Where(m => Adult(m) && !_students.ContainsKey(p.Uid[m]) && CanTeach(m, plan, root)).ToList();
            // 졸업
            foreach (int st in students.ToList())
            {
                if (_students[p.Uid[st]].end > Day) continue;
                Graduate(st, u, plan, root);
                students.Remove(st);
            }
            if (teachers.Count == 0) continue;
            float tw = u.Tasks.Where(t => t.Verb == Verb.Teach).Sum(t => t.Weight);
            bool hall = FacilityOf(root, FacilityKind.Hall, -1, u) != null;
            int capacity = teachers.Sum(TeacherLoad) + (hall ? 4 * teachers.Count : 0);   // 학당은 정원만 늘린다
            // 질: 예산을 못 채우면 한 단계, 비품이 모자라면 한 단계 내려간다. 비품은 한 해에 다 쓴다
            int q = (int)plan.Quality;
            if (u.Need > 0f && u.Fill < 0.999f) q--;
            if (students.Count > 0 && u.Kit.Sum() < QualityKit[Math.Max(0, (int)plan.Quality)] * students.Count - 0.001f) q--;
            q = Math.Clamp(q, 0, 4);
            for (int k = 0; k < GoodCount; k++) u.Kit[k] = 0f;
            // 가르치기: 시간 = 가르침 무게 × 지휘 몫 × 질, 교사가 모자라면 그만큼 느리다
            float speed = tw * u.CmdFactor * (students.Count > capacity ? capacity / (float)students.Count : 1f) * QualitySpeed[q];
            var insiders = FactionHouses(root);
            bool chapel = FacilityOf(root, FacilityKind.Chapel, u.Village) != null;
            float wsum = SchoolSubjects.Sum(l => plan.Weights[(int)l]);
            if (wsum <= 0f) { plan.Weights[(int)Lesson.Letters] = 1f; wsum = 1f; }
            foreach (int st in students)
            {
                foreach (var l in SchoolSubjects)
                {
                    float w = plan.Weights[(int)l] / wsum;
                    if (w <= 0f) continue;
                    if (LessonSkill(l) is Skill s)
                    {
                        int best = teachers.OrderByDescending(t => p.GetSkill(t, s)).First();
                        float before = p.GetSkill(st, s);
                        Learn(st, s, best, 3f * w * speed);
                        float cap = MathF.Min(DepthCap(w) + QualityDepth[q], p.GetSkill(best, s) + 0.05f);
                        if (p.GetSkill(st, s) > cap) p.SetSkill(st, s, MathF.Max(before, cap));
                    }
                    else if (l is Lesson.Etiquette or Lesson.Espionage)
                    {
                        // 예법은 말솜씨, 첩보는 눈치가 좋은 교사만큼, 깊이 상한까지
                        int best = teachers.OrderByDescending(t => l == Lesson.Etiquette ? p.Get(t, Trait.Charisma) : p.Get(t, Trait.Perception) + Polish(t, l)).First();
                        float cap = MathF.Min(DepthCap(w) + QualityDepth[q], (l == Lesson.Etiquette ? p.Get(best, Trait.Charisma) : p.Get(best, Trait.Perception)) + 0.05f);
                        var key = (p.Uid[st], l);
                        _polish[key] = MathF.Min(cap, MathF.Max(_polish.GetValueOrDefault(key), _polish.GetValueOrDefault(key) + 0.12f * w * speed));
                    }
                    else if (rootLeader >= 0 && st != rootLeader)
                    {
                        // 교리: 신앙과 우두머리, 충성: 우두머리만. 교리만으로 광신까지는 가지 않는다(눈 60, 충성 80까지)
                        float limit = l == Lesson.Doctrine ? 60f : 80f;
                        float sp2 = speed * (l == Lesson.Doctrine && chapel ? 1.2f : 1f);
                        if (Social.Opinion(p, st, root.LeaderUid) < limit) Social.Add(p, st, root.LeaderUid, 40f * w * sp2);
                        if (l == Lesson.Doctrine) p.Piety[st] = MathF.Min(1f, p.Piety[st] + 0.12f * w * sp2);
                    }
                }
                // 교사와 동기 사이
                int tch = teachers[_rng.NextInt(teachers.Count)];
                Social.Add(p, st, p.Uid[tch], 5f);
                if (students.Count > 1) { int mate = students[_rng.NextInt(students.Count)]; if (mate != st) { Social.Add(p, st, p.Uid[mate], 3f); Social.Add(p, mate, p.Uid[st], 3f); } }
                // 수업료(세력 안·밖 따로)
                int sh = p.Household[st];
                float feeAsk = insiders.Contains(sh) ? plan.Fee : plan.FeeOut;
                if (feeAsk > 0f && hh.IsAlive(sh))
                {
                    float fee = PayCoin(sh, -2, feeAsk, false);   // 수업료는 은화
                    PayDues(sh, fee);
                    Earn(root, fee, true);
                }
                Stats.Studied++;
            }
            // 받기
            int room = capacity - students.Count;
            if (room > 0 && !CapFull(root)) Enroll(u, plan, root, room);
        }
        // 2) 의무 복무가 끝난 사람, 열여섯이 되어 갈 곳에 가는 사람
        foreach (var (uid, sv) in _service.ToList())
        {
            int i = FindAlive(uid);
            if (i < 0) { _service.Remove(uid); continue; }
            if (sv.until > Day) continue;
            if (GroupById(sv.unit) is { } g) LeaveGroup(i, g);
            _service.Remove(uid);
        }
        foreach (var (uid, pl) in _placeLater.ToList())
        {
            int i = FindAlive(uid);
            if (i < 0 || GroupById(pl.unit) is not { } g) { _placeLater.Remove(uid); continue; }
            if (!Adult(i)) continue;
            if (GroupsOf(i).Count() < GroupSlots) JoinGroup(i, g);
            if (pl.years > 0) _service[uid] = (g.Id, Day + pl.years * Config.DaysPerYear);
            _placeLater.Remove(uid);
        }
        foreach (var uid in _students.Keys.ToList()) if (FindAlive(uid) < 0 || GroupById(_students[uid].unit) == null) _students.Remove(uid);
        Stats.Students = _students.Count;
    }

    private void Enroll(Group u, SchoolPlan plan, Group root, int room)
    {
        var p = People; var hh = Households;
        var faction = FactionHouses(root);
        var pool = new HashSet<int>(faction);
        if (plan.Outsiders && u.Village >= 0) foreach (int h in VillageHouses(u.Village)) pool.Add(h);
        int leader = FindAlive(root.LeaderUid);
        bool playerSchool = leader >= 0 && IsPlayer(leader);
        var main = SchoolSubjects.OrderByDescending(l => plan.Weights[(int)l]).First();
        var mainSkill = LessonSkill(main) ?? Skill.Letters;
        var cands = new List<(int i, float pr)>();
        foreach (int h in pool)
        {
            if (!hh.IsAlive(h)) continue;
            int head = HeadIdx(h);
            if (head >= 0 && IsPlayer(head) && !playerSchool) continue;   // 플레이어 집 아이는 남의 학교에 저절로 보내지 않는다
            for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
            {
                int age = AgeYears(k);
                if (age < plan.MinAge || age > plan.MaxAge || _students.ContainsKey(p.Uid[k]) || _alumni.Contains((u.Id, p.Uid[k])) || GroupsOf(k).Count() >= GroupSlots || InGroup(k, u)) continue;
                if (p.GetSkill(k, Skill.Letters) > plan.MaxLetters || Skills.Talent(p, k, mainSkill) < plan.TalentMin) continue;
                // 집이 보낼지: 배움을 아는 집(가장의 글, 야심, 넉넉함), 재능, 학교 우두머리를 보는 눈, 수업료와 일손
                float pr = 0.05f + 0.2f * Skills.Talent(p, k, mainSkill);
                if (head >= 0) pr += 0.3f * p.GetSkill(head, Skill.Letters) + 0.15f * p.Get(head, Trait.Ambition);
                if (Surplus(h) >= 1.5f) pr += 0.15f;
                if (head >= 0 && leader >= 0) pr += Social.Opinion(p, head, root.LeaderUid) / 200f;
                float fee = faction.Contains(h) ? plan.Fee : plan.FeeOut;
                pr -= 0.25f * MathF.Min(1.5f, fee / (5f * MathF.Max(0.3f, Surplus(h))));   // 수업료는 형편만큼 무겁다
                if (Surplus(h) < 0.8f) pr -= 0.3f;   // 먹을 것이 모자란 집은 일손을 내놓지 못한다
                if (fee <= 0f) pr += 0.15f;
                pr += 0.05f * ((int)plan.Quality - 2);   // 좋은 학교일수록 보낸다
                if (age >= 12) pr -= 0.1f;
                if (!faction.Contains(h)) pr -= 0.1f;
                if (pr > 0f) cands.Add((k, pr));
            }
        }
        foreach (var (k, pr) in cands.OrderByDescending(x => x.pr).Take(room * 2))
        {
            if (room <= 0) break;
            if (!_rng.Chance(MathF.Min(0.9f, pr))) continue;
            if (!JoinGroup(k, u)) continue;
            _students[p.Uid[k]] = (u.Id, Day, Day + plan.Years * Config.DaysPerYear);
            plan.Enrolled++; Stats.Enrolled++;
            room--;
        }
    }

    private void Graduate(int st, Group u, SchoolPlan plan, Group root)
    {
        var p = People;
        _students.Remove(p.Uid[st]);
        _alumni.Add((u.Id, p.Uid[st]));
        plan.Graduated++; Stats.Graduated++;
        FaithFromSchool(st, plan, root);
        if (plan.Dest == SchoolDest.Teacher) return;   // 갈래에 남아 가르친다
        LeaveGroup(st, u);
        if (plan.Dest == SchoolDest.Faction) return;
        var target = GroupById(plan.DestUnit);
        if (target == null || target.Id == u.Id || RootOf(target).Id != root.Id) return;
        if (plan.Dest == SchoolDest.Merit)
        {
            float score = 0f, ws = 0f;
            foreach (var l in SchoolSubjects) if (LessonSkill(l) is Skill s && plan.Weights[(int)l] > 0f) { score += plan.Weights[(int)l] * p.GetSkill(st, s); ws += plan.Weights[(int)l]; }
            if (ws > 0f && score / ws < plan.MeritLine) return;
        }
        // 그 조직을 싫어하고 우두머리도 좋게 보지 않으면 달아난다
        if (GroupWant(st, target.Kind) < 0.15f && Social.Opinion(p, st, root.LeaderUid) < 10f && _rng.Chance(0.3f))
        {
            plan.Fled++; Stats.GradFled++;
            Social.Add(p, st, root.LeaderUid, -10f);
            return;
        }
        // 갈 곳에 맞는 과목을 25% 넘게 배웠으면 그 솜씨가 조금 더 붙고, 아니면 그 조직이 반기지 않는다(조직 충성 −0.05)
        var fit = FitLesson(DestKind(target));
        if (plan.Weights[(int)fit] >= 0.25f)
        {
            if (LessonSkill(fit) is Skill fs) p.SetSkill(st, fs, MathF.Min(1f, p.GetSkill(st, fs) + 0.05f));
            else if (fit is Lesson.Espionage) _polish[(p.Uid[st], fit)] = MathF.Min(1f, _polish.GetValueOrDefault((p.Uid[st], fit)) + 0.05f);
        }
        else AddOrgLoyalty(root.Id, p.Uid[st], -0.05f);
        int years = plan.Dest == SchoolDest.Service ? plan.ServiceYears : 0;
        if (!Adult(st)) { _placeLater[p.Uid[st]] = (target.Id, years); return; }
        if (GroupsOf(st).Count() < GroupSlots) JoinGroup(st, target);
        if (years > 0) _service[p.Uid[st]] = (target.Id, Day + years * Config.DaysPerYear);
        Stats.GradPlaced++;
    }

    /// <summary>학교 설정을 바꾼다(비중은 합 1로 맞춘다).</summary>
    public void SetPlan(Group u, SchoolPlan src)
    {
        var plan = PlanOf(u);
        plan.MinAge = Math.Clamp(src.MinAge, 5, 40); plan.MaxAge = Math.Clamp(src.MaxAge, plan.MinAge, 40);
        plan.MaxLetters = Math.Clamp(src.MaxLetters, 0f, 1f); plan.TalentMin = Math.Clamp(src.TalentMin, 0f, 1f);
        plan.Outsiders = src.Outsiders; plan.Years = Math.Clamp(src.Years, 1, 10); plan.Fee = Math.Clamp(src.Fee, 0f, 50f); plan.FeeOut = Math.Clamp(src.FeeOut, 0f, 50f);
        plan.Quality = src.Quality;
        plan.Dest = src.Dest; plan.DestUnit = src.DestUnit; plan.MeritLine = src.MeritLine; plan.ServiceYears = Math.Clamp(src.ServiceYears, 1, 10);
        float tot = SchoolSubjects.Sum(l => MathF.Max(0f, src.Weights[(int)l]));
        for (int k = 0; k < plan.Weights.Length; k++) plan.Weights[k] = 0f;
        foreach (var l in SchoolSubjects) plan.Weights[(int)l] = tot > 0f ? MathF.Max(0f, src.Weights[(int)l]) / tot : (l == Lesson.Letters ? 1f : 0f);
    }

    /// <summary>학교의 지금: 학생, 교사, 받을 수 있는 수.</summary>
    public (int students, int teachers, int capacity) SchoolCount(Group u)
    {
        var p = People;
        var ms = Members(u);
        int s = ms.Count(m => _students.TryGetValue(p.Uid[m], out var x) && x.unit == u.Id);
        var plan = PlanOf(u); var root = RootOf(u);
        var t = ms.Where(m => Adult(m) && !_students.ContainsKey(p.Uid[m]) && CanTeach(m, plan, root)).ToList();
        return (s, t.Count, t.Sum(TeacherLoad));
    }
}
