namespace People.Sim;

/// <summary>기득권이 거는 조건.</summary>
public enum OrgCondition : byte { None, Cap, Disarm, Reveal }

/// <summary>
/// 드러난 무력과 기득권(plan_faction 6장), 남의 세력 감시(12.2).
/// - 드러난 무력 V = 맨 위 조직 나무의 싸움 힘(무구·상근을 센 힘) × 공개도 × 정체 무게.
///   공개도: 완전 은폐 0.1, 일부 은폐 0.3, 중립 0.5, 공개 0.8, 완전 공개 1. 정체 무게: 도적 떼 1.3, 수련단·용병단 1.2, 무장 신도회 1.5,
///   자경단 1, 신도회 0.8, 학교·상단·공방·감찰대·유곽 0.5.
/// - 위협 T = V ÷ 그 땅 지배자 세력의 힘(봉신 힘의 0.6까지) + 병영·요새 집마다 0.1. 붙은 조직과 지배자 군대는 셈하지 않는다.
/// - 지배자는 해마다 T로 단계를 고른다. 0.3 아래 지켜봄, 0.3~0.5 경고, 0.5~0.8 조건 걸기(사람 수 제한, 무구 내놓기, 드러내기),
///   0.8 넘으면 말솜씨가 거칠기보다 크면 끌어들이기(거두기 제안), 아니면 이웃 지배자와 손잡고 이길 만할 때(0.5) 해마다 30% 확률로 누르기.
///   거절과 어김은 다음 해 한 단계 올린다. 사람 수 제한을 받아들인 조직은 그 수까지만 들인다.
/// - 남의 세력: 내 세력 사람이 남의 세력 조직에도 들어 있으면(심은 사람) 그 자리만큼 본다. 조직원은 그 갈래, 지휘자는 그 조직과 장악도,
///   곳간지기는 곳간, 측근·연락책은 거사와 받은 요구. 그 세력 감시 세기만큼 해마다 들킨다. 들킨 사람은 쫓겨나거나,
///   그쪽 우두머리를 더 좋게 보면 이중 첩자가 되어 틀린 값을 흘린다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, (OrgCondition cond, int lord, int until, int cap, bool accepted)> _conditions = new();   // 조직 id → 걸린 조건(받아들였으면 다섯 해 지킨다)
    private readonly Dictionary<int, int> _threatStage = new();   // 조직 id → 지난해 단계(0~3)
    private readonly Dictionary<int, int> _defiance = new();      // 조직 id → 거절·어김 횟수
    private readonly HashSet<int> _doubleAgents = new();          // 이중 첩자 uid

    public static float VisFactor(Visibility v) => v switch
    {
        Visibility.Hidden => 0.1f, Visibility.Covert => 0.3f, Visibility.Neutral => 0.5f, Visibility.Public => 0.8f, _ => 1f,
    };

    /// <summary>정체가 기득권의 눈에 주는 무게.</summary>
    public float KindWeight(Group root)
    {
        if (root.IdentityName.Contains("무장")) return 1.5f;
        return root.Kind switch
        {
            GroupKind.Bandits => 1.3f, GroupKind.Drill or GroupKind.Sellswords => 1.2f, GroupKind.Watch => 1f, GroupKind.Brotherhood => 0.8f, _ => 0.5f,
        };
    }

    /// <summary>드러난 무력.</summary>
    public float VisibleForce(Group root)
    {
        float s = 0f;
        foreach (var u in Subtree(root)) if (u == root || RootOf(u).Id == root.Id) s += u.OwnStrength;
        return s * VisFactor(root.Visibility) * KindWeight(root);
    }

    /// <summary>그 땅 지배자가 느끼는 위협(지배자 없으면 0).</summary>
    public float ThreatTo(Group root, out int lord)
    {
        lord = -1;
        if (!root.Alive || !IsTop(root) || root.Home >= 0 || IsAttached(root)) return 0f;
        lord = ConcernedLord(root);
        if (lord < 0 || !Households.IsAlive(lord)) return 0f;
        float t = VisibleForce(root) / MathF.Max(1f, RealmPower(lord));   // 봉신까지 센 세력 힘에 견준다
        t += 0.1f * _facilities.Count(f => f.Alive && f.Owner == root.Id && f.Kind is FacilityKind.Barracks or FacilityKind.Fort);
        return t * AlarmMult(root);   // 악평은 경계를 키우고 좋은 평판은 줄인다
    }

    /// <summary>사람 수 제한이 걸려 꽉 찼는가(맨 위 조직 기준).</summary>
    public bool CapFull(Group g)
    {
        var r = RootOf(g);
        return _conditions.TryGetValue(r.Id, out var c) && c.accepted && c.until > Day && c.cond == OrgCondition.Cap && r.Total >= c.cap;
    }

    public OrgCondition ConditionOf(Group g) => _conditions.TryGetValue(g.Id, out var c) && c.until > Day && c.accepted ? c.cond : OrgCondition.None;

    public static string ConditionName(OrgCondition c) => c switch
    {
        OrgCondition.Cap => "사람 수 제한", OrgCondition.Disarm => "무구 내놓기", OrgCondition.Reveal => "드러내기", _ => "없음",
    };

    /// <summary>이웃 지배자의 도움: 보호 거리 두 배 안에서 이 지배자를 좋게 보는(20 넘음) 홀로 선 지배자와 주군, 힘의 절반씩.</summary>
    private float AllyPower(int lord)
    {
        var hh = Households; var p = People;
        int lh = HeadIdx(lord);
        if (lh < 0) return 0f;
        float s = 0f, r = 2f * Config.ProtectRange;
        int liege = PatronOf(lord);
        if (liege >= 0) s += 0.5f * LordPower(liege);
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (h == lord || h == liege || !hh.IsAlive(h) || TierOf(h) < 1 || Dist2(h, lord) > r * r) continue;
            int oh = HeadIdx(h);
            if (oh >= 0 && Social.Opinion(p, oh, p.Uid[lh]) >= 20f) s += 0.5f * LordPower(h);
        }
        return s;
    }

    /// <summary>해마다: 지배자가 드러난 무력을 보고 단계를 고른다. 걸린 조건을 지키는지 본다.</summary>
    private void EstablishmentYear()
    {
        var p = People;
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive || !IsTop(g) || g.Home >= 0 || IsAttached(g)) continue;
            int leader = FindAlive(g.LeaderUid);
            if (leader < 0) continue;
            float t = ThreatTo(g, out int lord);
            if (lord < 0) continue;
            int lh = HeadIdx(lord);
            if (lh < 0) continue;
            // 걸린 조건을 어겼는가
            var cond = ConditionOf(g);
            bool broke = cond != OrgCondition.None && Violates(g, cond);
            if (broke) { Defy(g, lord); t = MathF.Max(t, 0.8f); }
            if (IsPlayer(lh))
            {
                // 플레이어 지배자는 다툼 창에서 스스로 정한다. 어긴 것만 알린다
                if (broke) Chron(lh, $"{Topic(g.Name)} 건 조건({ConditionName(cond)})을 어겼다");
                continue;
            }
            int stage = t < 0.3f ? 0 : t < 0.5f ? 1 : t < 0.8f ? 2 : 3;
            stage = Math.Min(3, stage + Math.Min(1, _defiance.GetValueOrDefault(g.Id)));   // 거역한 조직은 한 단계 위로 본다
            _threatStage[g.Id] = stage;
            if (stage == 0) continue;
            Stats.ThreatSeen++;
            // 답하지 않은 조건이 한 해를 넘기면 거절로 친다
            if (_conditions.TryGetValue(g.Id, out var pend) && !pend.accepted && pend.until <= Day) Defy(g, lord);
            int lhouse = p.Household[leader];
            switch (stage)
            {
                case 1:
                    Suspect(lord, lhouse, 5f);
                    if (_rng.Chance(0.3f))
                    {
                        Chron(leader, $"{SubjName(lh)} {Obj(g.Name)} 눈여겨본다고 경고했다");
                        // 신중한 우두머리는 숨긴다(덜 드러나게). 드러내기를 받아들였으면 숨기지 않는다
                        if (!IsPlayer(leader) && p.Get(leader, Trait.Caution) > 0.6f && g.Visibility > Visibility.Neutral && cond != OrgCondition.Reveal) g.Visibility--;
                        Stats.ThreatWarned++;
                    }
                    break;
                case 2:
                    if (cond == OrgCondition.None && !_conditions.ContainsKey(g.Id) && _rng.Chance(0.5f)) DemandCondition(g, lord, leader);
                    break;
                default:
                    float cha = HeadTrait(lord, Trait.Charisma), agg = HeadTrait(lord, Trait.Aggression);
                    if (cha > agg && LedFreeOrg(leader) == g && !(_offerRefused.TryGetValue(g.Id, out int rd) && Day - rd < 5 * Config.DaysPerYear))
                    {
                        Stats.ThreatCoopt++;
                        if (IsPlayer(leader)) AskPlayer(InteractionId.OrgOffer, lord, lhouse);
                        else Interact(InteractionId.OrgOffer, new InteractionContext { Actor = lh, Target = leader, Third = -1 });
                    }
                    else
                    {
                        // 이웃과 손잡고 누른다. 이길 만할 때만(승산 0.5 넘음), 아니면 조건을 건다
                        float lp = LordPower(lord) + AllyPower(lord), gp = MathF.Max(0.3f, g.Strength);
                        float odds = lp * lp / (lp * lp + gp * gp + 0.01f);
                        if (odds < 0.5f) { if (cond == OrgCondition.None && !_conditions.ContainsKey(g.Id) && _rng.Chance(0.5f)) DemandCondition(g, lord, leader); break; }
                        if (!_rng.Chance(0.3f)) break;
                        bool won = _rng.Chance(odds);
                        Stats.ThreatSuppress++;
                        Chron(leader, won ? $"{SubjName(lh)} 이웃과 손잡고 {Obj(g.Name)} 눌렀다" : $"{SubjName(lh)} 이웃과 손잡고 눌렀지만 {Topic(g.Name)} 버텼다");
                        SuppressResult(g, lord, won);
                    }
                    break;
            }
        }
    }

    private bool Violates(Group g, OrgCondition c)
    {
        var x = _conditions[g.Id];
        return c switch
        {
            OrgCondition.Cap => g.Total > x.cap,
            OrgCondition.Disarm => Subtree(g).Any(u => u.Kit[(int)Good.Arms] > 0.5f),
            OrgCondition.Reveal => g.Visibility < Visibility.Public,
            _ => false,
        };
    }

    private void Defy(Group g, int lord)
    {
        _defiance[g.Id] = _defiance.GetValueOrDefault(g.Id) + 1;
        _conditions.Remove(g.Id);
        if (FindAlive(g.LeaderUid) is int ld && ld >= 0) Suspect(lord, People.Household[ld], 20f);
        Stats.ThreatDefied++;
    }

    /// <summary>조건을 고른다: 무구가 많으면 내놓기, 숨겼으면 드러내기, 아니면 사람 수 제한(지금의 8할).</summary>
    private OrgCondition PickCondition(Group g)
    {
        float arms = Subtree(g).Sum(u => u.Kit[(int)Good.Arms]);
        if (arms > 0.3f * MathF.Max(1, g.Total)) return OrgCondition.Disarm;
        if (g.Visibility < Visibility.Public) return OrgCondition.Reveal;
        return OrgCondition.Cap;
    }

    private void DemandCondition(Group g, int lord, int leader)
    {
        var c = PickCondition(g);
        _conditions[g.Id] = (c, lord, Day + Config.DaysPerYear, Math.Max(3, (int)(g.Total * 0.8f)), false);   // 답할 때까지 한 해
        Stats.ThreatConditions++;
        if (IsPlayer(leader)) AskPlayer(InteractionId.OrgCondition, lord, People.Household[leader]);
        else Interact(InteractionId.OrgCondition, new InteractionContext { Actor = HeadIdx(lord), Target = leader, Third = -1 });
    }

    /// <summary>시험용: 조건을 건다(답을 기다리는 상태).</summary>
    public void ForceCondition(Group g, OrgCondition c, int lord, int cap) => _conditions[g.Id] = (c, lord, Day + Config.DaysPerYear, cap, false);

    /// <summary>조건을 받아들인다: 사람 수는 넘는 만큼 내보내고, 무구는 지배자에게 넘기고, 숨긴 조직은 드러낸다. 다섯 해 동안 지킨다.</summary>
    private void Comply(Group g, int lord)
    {
        var x = _conditions[g.Id];
        _conditions[g.Id] = (x.cond, lord, Day + 5 * Config.DaysPerYear, x.cap, true);
        switch (x.cond)
        {
            case OrgCondition.Cap:
                int over = g.Total - x.cap;
                foreach (var u in Subtree(g).Where(u => RootOf(u).Id == g.Id).ToList())
                    foreach (int m in Members(u))
                    {
                        if (over <= 0) break;
                        if (People.Uid[m] == g.LeaderUid || People.Uid[m] == u.LeaderUid) continue;
                        LeaveGroup(m, u); over--;
                    }
                break;
            case OrgCondition.Disarm:
                float arms = 0f;
                foreach (var u in Subtree(g)) { arms += u.Kit[(int)Good.Arms]; u.Kit[(int)Good.Arms] = 0f; }
                G(lord, Good.Arms) += arms;
                break;
            case OrgCondition.Reveal:
                g.Visibility = Visibility.Public;
                break;
        }
        _threatStage[g.Id] = 1;
        if (FindAlive(g.LeaderUid) is int ld && ld >= 0) Suspect(lord, People.Household[ld], -15f);
    }

    public float ConditionOdds(Group g, int lord) => ComplyOdds(g, lord);

    /// <summary>NPC 우두머리가 조건을 받아들일 확률: 지배자가 셀수록, 신중할수록 받고 야심이 크면 버틴다.</summary>
    private float ComplyOdds(Group g, int lord)
    {
        int leader = FindAlive(g.LeaderUid);
        if (leader < 0) return 0f;
        float lp = LordPower(lord) + AllyPower(lord), gp = MathF.Max(0.3f, g.Strength);
        float s = 0.3f + 0.5f * lp / (lp + gp) + 0.2f * People.Get(leader, Trait.Caution) - 0.3f * People.Get(leader, Trait.Ambition);
        return Math.Clamp(s, 0.05f, 0.95f);
    }

    // ------------------------------------------------------------------ 남의 세력 감시

    /// <summary>세력 root 가 심어 둔 사람: (사람, 남의 세력 맨 위, 그 갈래, 자리). 자리: 0 조직원, 1 지휘자, 2 곳간지기, 3 측근·연락책.</summary>
    public List<(int person, Group other, Group unit, int role)> Agents(Group root)
    {
        var p = People;
        var list = new List<(int, Group, Group, int)>();
        var mine = new HashSet<int>();
        foreach (var u in Subtree(root)) if (u == root || RootOf(u).Id == root.Id) foreach (int m in MembersFast(u)) mine.Add(m);
        foreach (int m in mine)
            foreach (var g in GroupsOf(m))
            {
                var o = RootOf(g);
                if (o.Id == root.Id || (IsAttached(root) && o.Id == RootOf(GroupById(root.Parent) ?? root).Id)) continue;
                int uid = p.Uid[m];
                int role = o.TreasurerUid == uid ? 2 : o.LiaisonUid == uid || o.Inner.Contains(uid) ? 3 : g.LeaderUid == uid ? 1 : 0;
                list.Add((m, o, g, role));
            }
        return list;
    }

    public bool IsDoubleAgent(int i) => _doubleAgents.Contains(People.Uid[i]);

    /// <summary>해마다: 남의 세력 감시 세기만큼 심은 사람이 들킨다. 들키면 쫓겨나거나 이중 첩자가 된다.</summary>
    private void SpyYear()
    {
        var p = People;
        foreach (var root in _groups.ToList())
        {
            if (!root.Alive || !IsTop(root)) continue;
            int myLeader = FindAlive(root.LeaderUid);
            if (myLeader < 0) continue;
            foreach (var (m, other, unit, _) in Agents(root))
            {
                if (_doubleAgents.Contains(p.Uid[m])) continue;
                float eye = SurveyOf(other) * (1f - LiaisonGuard(root));
                if (eye <= 0f || !_rng.Chance(MathF.Min(0.5f, 0.02f * eye))) continue;
                Stats.SpiesFound++;
                // 그쪽 우두머리를 더 좋게 보면 돌아선다(이중 첩자), 아니면 쫓겨난다
                if (Social.Opinion(p, m, other.LeaderUid) > Social.Opinion(p, m, root.LeaderUid) + 20f)
                {
                    _doubleAgents.Add(p.Uid[m]);
                    Stats.DoubleAgents++;
                }
                else
                {
                    foreach (var g in GroupsOf(m).ToList()) if (RootOf(g).Id == other.Id) LeaveGroup(m, g);
                    if (FindAlive(other.LeaderUid) is int ol && ol >= 0) Social.Add(p, ol, root.LeaderUid, -20f);
                    Chron(myLeader, $"{Names.Full(Lineage, p.Uid[m])}{(Batchim(Names.Full(Lineage, p.Uid[m])) ? "이" : "가")} {other.Name}에 심어 둔 것이 들켜 쫓겨났다");
                }
            }
        }
        _doubleAgents.RemoveWhere(u => FindAlive(u) < 0);
    }

    private void AddThreatDefs(InteractionDef[] d)
    {
        int H(World w, int i) => w.People.Household[i];
        d[(int)InteractionId.OrgCondition] = new InteractionDef
        {
            // 지배자(Actor)가 조직 우두머리(Target)에게 조건을 건다. 받아들임 = 따른다
            Id = InteractionId.OrgCondition, Name = "조건 걸기", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) => w.LedFreeOrgAny(c.Target) is { } g && w._conditions.ContainsKey(g.Id) && w.HeadIdx(H(w, c.Actor)) == c.Actor,
            Accept = (w, c) => w.LedFreeOrgAny(c.Target) is { } g ? w.ComplyOdds(g, H(w, c.Actor)) : 0f,
            Success = (w, c, _) =>
            {
                var g = w.LedFreeOrgAny(c.Target)!;
                var cond = w._conditions[g.Id].cond;
                w.Comply(g, H(w, c.Actor));
                w.Stats.ThreatComplied++;
                w.RiseChron(c, x => $"{Obj(g.Name)} 두고 건 조건({ConditionName(cond)})을 {Subj(x)} 받아들였다", x => $"{x}의 조건({ConditionName(cond)})을 받아들였다");
            },
            Fail = (w, c) =>
            {
                var g = w.LedFreeOrgAny(c.Target);
                if (g == null) return;
                var cond = w._conditions[g.Id].cond;
                w.Defy(g, H(w, c.Actor));
                w.RiseChron(c, x => $"{Obj(g.Name)} 두고 건 조건({ConditionName(cond)})을 {Subj(x)} 거절했다", x => $"{x}의 조건({ConditionName(cond)})을 거절했다");
            },
        };
    }

    /// <summary>i 가 이끄는 홀로 선 조직(숨긴 것과 도적 떼도).</summary>
    public Group? LedFreeOrgAny(int i)
    {
        Group? best = null;
        foreach (var g in GroupsOf(i))
            if (IsLeader(i, g) && g.Parent < 0 && g.Home < 0 && (best == null || g.Total > best.Total)) best = g;
        return best;
    }
}
