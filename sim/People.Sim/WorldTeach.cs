namespace People.Sim;

/// <summary>갈래가 가르치는 것.</summary>
public enum Lesson : byte { None, Arms, Letters, Trade, Craft, Doctrine, Succession, Loyalty, Medicine, Carpentry, Farming, Etiquette, Espionage, Count }

/// <summary>
/// O3 교육.
/// - 갈래마다 무엇을(무예, 글, 장사, 공예, 교리, 이음) 얼마나(조직원 시간의 0~30%) 가르칠지 고른다. 가르치는 만큼 일이 줄고 비용이 든다.
/// - 기술은 기존 학습 공식(재능과 나이의 한계)을 그대로 쓴다.
/// - 교리는 충성을 0.8까지만 올린다. 광신은 우두머리를 직접 겪어야 생긴다. 글을 배운 사람은 광신의 의심이 빨리 쌓인다(Social.Add).
/// - 이음: 갈래마다 후계자와 이음 값. 모자라면 우두머리가 바뀔 때 조직이 흔들리고, 완전 은폐 조직은 갈래가 끊긴다.
///   공개된 조직은 글 아는 조직원이 충분하면 기록이 이음을 대신한다.
/// - 지식인 동요: 자리 없는 배운 사람은 지배자를 원망하고, 포섭과 선동에 잘 넘어간다.
/// - 학당: 영주 이상인 지배자가 자기 마을 아이들에게 글을 가르친다.
/// </summary>
public sealed partial class World
{
    private readonly HashSet<int> _schools = new();   // 학당을 연 지배자 집
    private readonly Dictionary<int, int> _schoolFounded = new();

    public static string LessonName(Lesson l) => l switch
    {
        Lesson.Arms => "무예", Lesson.Letters => "글", Lesson.Trade => "장사", Lesson.Craft => "공예", Lesson.Doctrine => "교리", Lesson.Succession => "이음", Lesson.Loyalty => "충성",
        Lesson.Medicine => "의술", Lesson.Carpentry => "목공", Lesson.Farming => "농사", Lesson.Etiquette => "예법", Lesson.Espionage => "첩보", _ => "없음",
    };

    private static Skill? LessonSkill(Lesson l) => l switch
    {
        Lesson.Arms => Skill.Arms, Lesson.Letters => Skill.Letters, Lesson.Trade => Skill.Trade, Lesson.Craft => Skill.Craft,
        Lesson.Medicine => Skill.Herbalism, Lesson.Carpentry => Skill.Carpentry, Lesson.Farming => Skill.Farming, _ => null,
    };

    /// <summary>가르치는 몫만큼 갈래의 일이 준다.</summary>
    private static float WorkFactor(Group g) => 1f - g.TeachShare;

    /// <summary>갈래가 맡은 가르침 일의 무게(0~1).</summary>
    private static float TeachWeight(Group g)
    {
        float w = 0f;
        foreach (var t in g.Tasks) if (t.Verb == Verb.Teach) w += t.Weight;
        return w;
    }

    private const float ScholarLetters = 0.6f;   // 이만큼 글을 알아야 '배운 사람'(집에서 어깨너머로 배운 것보다 위)

    /// <summary>자리 없이 놀고 있는 배운 사람인가(글 0.6 넘음, 장사꾼·약초꾼·장인·징수관이 아니고, 갈래 우두머리나 측근도 아님).</summary>
    public bool IsIdleScholar(int i)
    {
        var p = People;
        if (!Adult(i) || p.GetSkill(i, Skill.Letters) < ScholarLetters) return false;
        if (p.JobOf[i] is (byte)Job.Collector or (byte)Job.Healer or (byte)Job.Trader or (byte)Job.Crafter) return false;
        int h = p.Household[i];
        if (Households.IsAlive(h) && ClientCount(h) > 0 && HeadIdx(h) == i) return false;
        foreach (var g in GroupsOf(i))
        {
            if (IsLeader(i, g)) return false;
            if (RootOf(g).Inner.Contains(p.Uid[i])) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ 해마다

    private void TeachYear()
    {
        var p = People; var hh = Households;
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive) continue;
            var root = RootOf(g);
            if (!g.TeachManual) AutoLesson(g, root);
            KeepHeir(g);
            // 조직원끼리 모여 배우는 몫. 학생을 받는 학교 일은 EducationYear 가 따로 한다
            float share = g.TeachShare;
            if (g.Lesson == Lesson.None || share <= 0f) { if (g.Home < 0 && root.Home < 0) g.Inherit = MathF.Max(0f, g.Inherit - 0.05f); continue; }
            var ms = MembersFast(g);
            if (ms.Count == 0) continue;
            // 비용: 가르치는 동안 먹여야 한다(곳간이 먼저, 모자라면 우두머리 집)
            float cost = ms.Count * g.TeachShare * 8f;   // 일을 떼어 모여 배우는 몫만 먹인다(학교 일은 제 집에서 먹으며 한다)
            float fromT = MathF.Min(root.Treasury, cost);
            root.Treasury -= fromT;
            int leader = FindAlive(root.LeaderUid);
            int home = root.Home >= 0 ? root.Home : leader >= 0 ? p.Household[leader] : -1;
            if (hh.IsAlive(home) && cost > fromT) PayCoin(home, -1, cost - fromT, false);
            var sk = LessonSkill(g.Lesson);
            if (sk is Skill s)
            {
                // 갈래에서 가장 잘하는 사람(우두머리 포함)이 가르친다
                int unitLeader = FindAlive(g.LeaderUid);
                int teacher = unitLeader;
                foreach (int m in ms) if (teacher < 0 || p.GetSkill(m, s) > p.GetSkill(teacher, s)) teacher = m;
                foreach (int m in ms.Take(40))
                {
                    if (m == teacher) continue;
                    Learn(m, s, teacher, 2f * share);
                    Stats.Taught++;
                }
            }
            else if (g.Lesson is Lesson.Doctrine or Lesson.Loyalty)
            {
                // 충성은 신앙 없이 우두머리를 보는 눈만 올린다
                bool faith = g.Lesson == Lesson.Doctrine && (root.Kind == GroupKind.Brotherhood || Subtree(root).Any(x => x.Tasks.Any(t => t.Verb == Verb.Preach)));
                foreach (int m in ms.Take(40))
                {
                    if (p.Uid[m] == root.LeaderUid) continue;
                    // 교리만으로는 0.8까지(광신은 우두머리를 직접 겪어야 생긴다)
                    bool cap = p.LoyalUid[m] == root.LeaderUid ? p.Loyalty[m] >= 0.8f : Social.Opinion(p, m, root.LeaderUid) >= 60f;
                    if (!cap) Social.Add(p, m, root.LeaderUid, 30f * share);
                    if (faith) p.Piety[m] = MathF.Min(1f, p.Piety[m] + 0.1f * share);
                    Stats.Taught++;
                }
            }
            else if (g.Lesson == Lesson.Succession && g.HeirUid >= 0)
            {
                g.Inherit = MathF.Min(1f, g.Inherit + 1.5f * share);
                Stats.Taught++;
            }
        }
        // 기록: 공개된 조직은 글 아는 조직원이 충분하면 이음이 저절로 찬다
        foreach (var root in _groups)
        {
            if (!root.Alive || !IsTop(root) || root.Home >= 0 || root.Visibility < Visibility.Public) continue;
            int literate = Subtree(root).SelectMany(MembersFast).Count(m => p.GetSkill(m, Skill.Letters) >= 0.4f);
            if (literate < Math.Max(2, root.Total / 20)) continue;
            foreach (var g in Subtree(root)) g.Inherit = MathF.Max(g.Inherit, 0.8f);
        }
        SchoolYear();
        ScholarYear();
    }

    /// <summary>AI 갈래가 가르칠 것: 조직의 성격과 처지에 맞게.</summary>
    private void AutoLesson(Group g, Group root)
    {
        (Lesson l, float s) pick = (Lesson.None, 0f);
        bool top = IsTop(g);
        if (root.Home >= 0) pick = g.Paid ? (Lesson.Arms, 0.1f) : (Lesson.None, 0f);
        else if (top && root.Visibility <= Visibility.Covert && ChildrenOf(root).Any()) pick = (Lesson.Succession, 0.1f);
        else pick = root.Kind switch
        {
            GroupKind.Brotherhood => (Lesson.Doctrine, 0.15f),
            GroupKind.Company => (Lesson.Trade, 0.1f),
            GroupKind.Sellswords => (Lesson.Arms, 0.2f),
            GroupKind.Bandits or GroupKind.Watch => (Lesson.Arms, 0.05f),
            GroupKind.School => (Lesson.Letters, 0f),   // 학교는 가르침 일의 무게로 가르친다
            GroupKind.Drill => (Lesson.Arms, 0.1f),
            _ => (Lesson.None, 0f),
        };
        g.Lesson = pick.l;
        g.TeachShare = pick.s;
    }

    /// <summary>후계자: 우두머리를 가장 좋게 보고 말솜씨와 힘이 있는 조직원. 바뀌면 이음은 처음부터.</summary>
    private void KeepHeir(Group g)
    {
        var p = People;
        if (g.HeirUid >= 0)
        {
            int h = FindAlive(g.HeirUid);
            if (h >= 0 && InGroup(h, g) && p.Uid[h] != g.LeaderUid) return;
        }
        var ms = MembersFast(g).Where(m => Adult(m) && p.Uid[m] != g.LeaderUid).ToList();
        if (ms.Count == 0) { g.HeirUid = -1; return; }
        int best = ms.OrderByDescending(m => Social.Opinion(p, m, g.LeaderUid) / 50f + p.Get(m, Trait.Charisma) + FightPower(m)).First();
        g.HeirUid = p.Uid[best];
        g.Inherit = 0f;
    }

    /// <summary>
    /// 우두머리가 바뀌었다(GroupYear 가 부른다). 이음이 넉넉한 후계자가 이으면 매끄럽다.
    /// 아니면 조직원이 새 우두머리를 낮게 보고 일부가 떠난다. 완전 은폐 조직은 우두머리만 알던 갈래가 끊겨 떨어져 나간다.
    /// </summary>
    private int PickSuccessor(Group g, List<int> members)
    {
        var p = People;
        if (g.HeirUid >= 0 && g.Inherit >= 0.5f)
        {
            int h = FindAlive(g.HeirUid);
            if (h >= 0 && members.Contains(h)) { Stats.SuccessionSmooth++; g.HeirUid = -1; return h; }
        }
        int next = members.Where(Adult).OrderByDescending(m => p.Get(m, Trait.Charisma) + FightPower(m)).DefaultIfEmpty(-1).First();
        if (next < 0) return -1;
        Stats.SuccessionForced++;
        // 억지 계승은 조직을 흔든다(계획서 7장): 몇 해 동안 권위가 깎인다
        RootOf(g).Shaken = MathF.Min(1f, RootOf(g).Shaken + 0.5f);
        foreach (int m in members.Take(30))
        {
            if (m == next) continue;
            Social.Add(p, m, p.Uid[next], -10f);
            if (g.Home < 0 && _rng.Chance(0.15f)) LeaveGroup(m, g);
        }
        if (IsTop(g) && g.Visibility == Visibility.Hidden)
            foreach (var c in ChildrenOf(g).ToList())
                if (_rng.Chance(0.5f)) { c.Parent = -1; Stats.CellsLost++; }
        g.HeirUid = -1;
        g.Inherit = 0f;
        return next;
    }

    /// <summary>학당: 영주 이상인 지배자가 한번 열면 해마다 자기 마을 8~15세 아이들 가운데 정원만큼 글을 배운다.</summary>
    private void SchoolYear()
    {
        var p = People; var hh = Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || TierOf(h) < 2) continue;
            bool open = _schools.Contains(h) && _schoolFounded.GetValueOrDefault(h) == hh.FoundedDay[h];
            if (!open)
            {
                int head = HeadIdx(h);
                if (head < 0 || IsPlayer(head) || !_rng.Chance(0.05f + 0.15f * p.Get(head, Trait.Intelligence))) continue;
                _schools.Add(h); _schoolFounded[h] = hh.FoundedDay[h];
                Chron(head, $"{SubjName(head)} 학당을 열었다");
            }
            SchoolOf(h);
        }
    }

    /// <summary>지배자 집 h 의 학당: 마을에서 글을 가장 잘 아는 어른이 가르친다.</summary>
    private void SchoolOf(int lord)
    {
        var p = People; var hh = Households;
        int v = hh.Village[lord];
        int teacher = -1;
        var kids = new List<int>();
        foreach (int h in VillageHouses(v))
        {
            if (!hh.IsAlive(h) || hh.Village[h] != v) continue;
            for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
            {
                int a = AgeYears(k);
                if (a >= 8 && a <= 15) kids.Add(k);
                else if (a >= 18 && (teacher < 0 || p.GetSkill(k, Skill.Letters) > p.GetSkill(teacher, Skill.Letters))) teacher = k;
            }
        }
        if (teacher < 0 || p.GetSkill(teacher, Skill.Letters) < 0.3f || kids.Count == 0) return;
        // 정원(4 + 단계 × 3)만큼 재능 있는 아이부터. 아이 하나에 식량 3
        int seats = 4 + 3 * TierOf(lord);
        float cost = MathF.Min(MathF.Max(0f, hh.Food[lord]), 3f * Math.Min(seats, kids.Count));
        hh.Food[lord] -= cost;
        int n = (int)(cost / 3f);
        foreach (int k in kids.OrderByDescending(k => Skills.Talent(p, k, Skill.Letters)).Take(n)) { Learn(k, Skill.Letters, teacher, 0.5f); Stats.Schooled++; }
    }

    /// <summary>지식인: 자리 있는 배운 사람은 세력 꼭대기를 좋게, 자리 없는 배운 사람은 나쁘게 본다.</summary>
    private void ScholarYear()
    {
        var p = People; var hh = Households;
        int idle = 0;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || p.GetSkill(i, Skill.Letters) < ScholarLetters || !Adult(i)) continue;
            int h = p.Household[i];
            if (!hh.IsAlive(h)) continue;
            int top = TopOf(h), lord = PatronOf(h);
            int th = top != h ? HeadIdx(top) : -1, lh = lord >= 0 ? HeadIdx(lord) : -1;
            if (IsIdleScholar(i))
            {
                idle++;
                if (th >= 0) Social.Add(p, i, p.Uid[th], -2f);
                if (lh >= 0 && lh != th) Social.Add(p, i, p.Uid[lh], -2f);
            }
            else if (th >= 0) Social.Add(p, i, p.Uid[th], 1f);
        }
        Stats.IdleScholars = idle;
    }
}
