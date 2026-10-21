namespace People.Sim;

/// <summary>
/// 세력 개편 R8(plan_reform): 모집.
/// - 모집 일을 맡은 갈래는 데려갈 곳(`RecruitTarget`)을 고른다: 같은 세력의 갈래, 세력 자체(-1: 세력원 집), 또는 자기 갈래(-2: 예전 포섭).
///   이름은 데려갈 곳의 성격으로 붙는다: 무력 모병단, 교단 선교단, 학교 학생 모집, 상단·생산 일꾼 모집, 세력 모집단, 자기 갈래 포섭조.
/// - 해마다 일의 양만큼(2 × 일 + 1명, 스물까지) 후보를 고른다. 숨긴 조직(반쯤 숨김 이하)은 조직원의 지인만, 아니면 그 마을 사람.
/// - 미끼는 데려갈 곳이 정한다(모두 × 모집 배율(인지도·평판) × 공개 정도의 받아들임).
///   무력: 0.1 + 0.3 × 조직원 임금 배율 ÷ 3 + 상근 0.1 + 0.2 × 공격성 + 가난 0.2 − 0.2 × 신중(16~40세).
///   교단: 신앙심을 올리고 0.6 넘으면 (0.15 + 0.1 × 권위) × 선교 배율(알려진 정도 등)로 든다.
///   학교: 그 학교 나이에 맞는 아이·사람을 학생으로(집이 넉넉하면 0.5, 수업료를 못 내면 0.1).
///   상단·생산: 0.1 + 0.4 × 조직원 임금 배율 ÷ 3 + 0.3 × 솜씨(장사·공예) + 가난 0.2.
///   세력: 아무도 섬기지 않는 집을 0.1 + 0.3 × 마을 위험 + 0.3 × 인지도 × 평판으로 세력원으로.
/// - 강제 징집: 영주 이상 지배자 세력의 모병단만. 지배자 나무의 16~40세 남자를 억지로 들인다. 조직 충성 0.2에서 시작하고,
///   해마다 0.15 × (1 − 조직 충성)로 달아난다. 조직 충성이 0.5를 넘으면 더는 징집병이 아니다.
/// - 스스로 드는 사람은 예전의 절반 세기로 남긴다(모임 들기).
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, int> _conscripts = new();   // 징집병 uid → 들인 세력 Id

    public bool IsConscript(int i) => People.IsAliveIndex(i) && _conscripts.ContainsKey(People.Uid[i]);

    /// <summary>모집 갈래가 데려갈 곳(같은 세력 갈래, 없으면 세력 자체 null, -2면 자기 갈래).</summary>
    private Group? RecruitDest(Group g)
    {
        var root = RootOf(g);
        if (g.RecruitTarget == -2) return g;
        if (g.RecruitTarget >= 0 && GroupById(g.RecruitTarget) is { } t && RootOf(t).Id == root.Id) return t;
        return null;
    }

    /// <summary>모집 갈래 이름.</summary>
    public string RecruitLabel(Group g)
    {
        if (g.RecruitTarget == -2) return "포섭조";
        var t = RecruitDest(g);
        if (t == null) return "모집단";
        return DestKind(t) switch
        {
            GroupKind.Watch => "모병단", GroupKind.Brotherhood => "선교단", GroupKind.School => "학생 모집", GroupKind.Company or GroupKind.Workshop or GroupKind.Brothel => "일꾼 모집", _ => "모집단",
        };
    }

    public void SetRecruit(Group g, int target, bool conscript) { g.RecruitTarget = target; g.Conscript = conscript; }

    /// <summary>강제 징집을 할 수 있나: 영주 이상 지배자 세력의 모병단.</summary>
    public bool CanConscript(Group g)
    {
        var root = RootOf(g);
        var t = RecruitDest(g);
        return root.Home >= 0 && TierOf(root.Home) >= 2 && t != null && DestKind(t) == GroupKind.Watch;
    }

    private void RecruitWork(Group root, Group g, int v, float effort)
    {
        if (g.RecruitTarget == -2) { for (int k = 0; k < Math.Min(6, (int)MathF.Ceiling(effort / 2f)); k++) RecruitOne(root, g); return; }
        if (CapFull(root)) return;
        var p = People; var hh = Households;
        var dest = RecruitDest(g);
        if (g.Conscript && CanConscript(g)) { Conscript(root, dest!, effort); return; }
        int n = Math.Min(20, (int)(2 * effort) + 1);
        bool covert = root.Visibility <= Visibility.Covert;
        var kind = dest != null ? DestKind(dest) : GroupKind.Army;   // Army: 세력원 집
        float mult = RecruitMult(root);
        for (int k = 0; k < n; k++)
        {
            int i = covert ? AcquaintanceOf(g) : RandomVillager(v);
            if (i < 0 || IsPlayer(i) || !p.IsAliveIndex(i)) continue;
            int h = p.Household[i];
            if (!hh.IsAlive(h)) continue;
            float poor = Surplus(h) < 1f ? 0.2f : 0f;
            if (dest == null)
            {
                // 세력원 집: 아무도 섬기지 않는 집
                int home = LandHomeOf(root);
                if (home < 0 || h == home || PatronOf(h) >= 0 || ClientCount(h) > 0 || WouldLoop(h, home) || HeadIdx(h) != i) continue;
                float a = (0.1f + 0.3f * MathF.Min(1f, DangerAt(hh.Village[h])) + 0.3f * Renown(root)) * mult;
                if (!_rng.Chance(Math.Clamp(a, 0f, 0.9f))) continue;
                _patron[h] = home; _patronFounded[h] = hh.FoundedDay[home];
                _clientsOf[home] = _clientsOf.GetValueOrDefault(home) + 1;
                Stats.Recruited++;
                continue;
            }
            if (InGroup(i, dest) || GroupsOf(i).Count() >= GroupSlots || !RuleAllows(root, i) || JoinFactor(root, i) <= 0f) continue;
            float wage = WageMult[(int)WageOf(dest, RoleKind.Member)] / 3f;
            float pr;
            switch (kind)
            {
                case GroupKind.Watch:
                    if (AgeYears(i) is < 16 or > 40) continue;
                    pr = 0.1f + 0.3f * wage + (dest.FullTime ? 0.1f : 0f) + 0.2f * p.Get(i, Trait.Aggression) + poor - 0.2f * p.Get(i, Trait.Caution);
                    break;
                case GroupKind.Brotherhood:
                    p.Piety[i] = MathF.Min(1f, p.Piety[i] + 0.01f);
                    if (p.Piety[i] <= 0.6f) continue;
                    pr = (0.15f + 0.1f * AuthorityPull(i, root)) * ConvertMult(root, i, v);
                    break;
                case GroupKind.School:
                    {
                        var plan = PlanOf(dest);
                        int age = AgeYears(i);
                        if (age < plan.MinAge || age > plan.MaxAge || _students.ContainsKey(p.Uid[i]) || _alumni.Contains((dest.Id, p.Uid[i]))) continue;
                        float fee = FactionHouses(root).Contains(h) ? plan.Fee : plan.FeeOut;
                        pr = Surplus(h) >= 0.8f && WealthOf(h) > 3f * fee ? 0.5f : 0.1f;
                        if (_rng.Chance(Math.Clamp(pr * mult * JoinFactor(root, i), 0f, 0.9f)) && JoinGroup(i, dest))
                        {
                            _students[p.Uid[i]] = (dest.Id, Day, Day + plan.Years * Config.DaysPerYear);
                            plan.Enrolled++; Stats.Enrolled++; Stats.Recruited++;
                        }
                        continue;
                    }
                case GroupKind.Brothel:
                    // 스스로 드는 어른만: 붙잡힌 사람, 빚에 묶인 사람, 16세 아래는 들이지 않는다
                    if (AgeYears(i) is < 16 or > 40 || IsCaptive(i) || DebtOf(h) > 0f) continue;
                    pr = 0.03f + 0.3f * dest.WorkerShare + 0.75f * poor + (p.JobOf[i] == (byte)Job.SexWorker ? 0.3f : 0f)
                         - 0.3f * DoctrineOn(i, Doctrine.Chastity) + 0.2f * DoctrineOn(i, Doctrine.Liberty);
                    if (!_rng.Chance(Math.Clamp(pr * mult * JoinFactor(root, i), 0f, 0.9f)) || !JoinGroup(i, dest)) continue;
                    if (p.JobOf[i] != (byte)Job.SexWorker) { p.JobOf[i] = (byte)Job.SexWorker; Stats.NewSexWorkers++; }
                    Stats.Recruited++;
                    continue;
                default:
                    if (!Adult(i)) continue;
                    float sk = kind == GroupKind.Company ? p.GetSkill(i, Skill.Trade) : MathF.Max(p.GetSkill(i, Skill.Craft), p.GetSkill(i, Skill.Carpentry));
                    pr = 0.1f + 0.4f * wage + 0.3f * sk + poor;
                    break;
            }
            if (!Adult(i) && kind != GroupKind.School) continue;
            if (!_rng.Chance(Math.Clamp(pr * mult * JoinFactor(root, i), 0f, 0.9f))) continue;
            if (!JoinGroup(i, dest)) continue;
            if (kind == GroupKind.Brotherhood) { Stats.Converts++; Converted(root, i); }
            Stats.Recruited++;
        }
    }

    /// <summary>조직원 하나의 지인(보는 눈 10 넘음) 하나. 없으면 -1.</summary>
    private int AcquaintanceOf(Group g)
    {
        var ms = MembersFast(g);
        if (ms.Count == 0) return -1;
        int r = ms[_rng.NextInt(ms.Count)];
        int sl = r * PersonStore.RelSlots + _rng.NextInt(PersonStore.RelSlots);
        int uid = People.RelUid[sl];
        return uid < 0 || People.RelAff[sl] < 10f ? -1 : FindAlive(uid);
    }

    /// <summary>강제 징집: 지배자 나무의 16~40세 남자를 일의 양만큼 들인다.</summary>
    private void Conscript(Group root, Group dest, float effort)
    {
        var p = People; var hh = Households;
        int lord = root.Home;
        int n = Math.Min(10, (int)effort + 1);
        for (int k = 0; k < n * 4 && n > 0; k++)
        {
            int h = _rng.NextInt(hh.HighWater);
            if (!hh.IsAlive(h) || h == lord || (PatronOf(h) != lord && !InTreeOf(h, lord))) continue;
            foreach (int m in MembersOf(h))
            {
                if (!p.Male[m] || AgeYears(m) is < 16 or > 40 || IsPlayer(m) || InGroup(m, dest) || GroupsOf(m).Count() >= GroupSlots) continue;
                if (!JoinGroup(m, dest)) continue;
                _conscripts[p.Uid[m]] = root.Id;
                _orgLoyal[(root.Id, p.Uid[m])] = 0.2f;
                if (HeadIdx(lord) is int lh && lh >= 0) Social.Add(p, m, p.Uid[lh], -10f);
                Stats.Conscripted++;
                n--;
                break;
            }
        }
    }

    // 시험용
    public void RecruitNow(Group g, float effort) => RecruitWork(RootOf(g), g, g.Village, effort);
    public void MakeConscript(int i, Group g)
    {
        if (!InGroup(i, g) && !JoinGroup(i, g)) return;
        _conscripts[People.Uid[i]] = RootOf(g).Id;
        _orgLoyal[(RootOf(g).Id, People.Uid[i])] = 0.2f;
    }
    public void RecountConscripts() => ConscriptYear();

    /// <summary>해마다: 징집병이 달아나거나(0.15 × (1 − 조직 충성)) 자리를 잡는다(조직 충성 0.5 넘음).</summary>
    private void ConscriptYear()
    {
        var p = People;
        foreach (var (uid, rid) in _conscripts.ToList())
        {
            int i = FindAlive(uid);
            var root = GroupById(rid);
            if (i < 0 || root == null || !root.Alive || !GroupsOf(i).Any(x => RootOf(x).Id == rid)) { _conscripts.Remove(uid); continue; }
            float loy = OrgLoyalty(root, uid);
            if (loy > 0.5f) { _conscripts.Remove(uid); continue; }
            if (_rng.Chance(0.15f * (1f - loy)))
            {
                foreach (var x in GroupsOf(i).ToList()) if (RootOf(x).Id == root.Id) LeaveGroup(i, x);
                _conscripts.Remove(uid);
                Stats.Deserted++;
            }
        }
    }
}
