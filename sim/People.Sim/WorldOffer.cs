namespace People.Sim;

/// <summary>
/// 조직과 지배자. 지배자는 조직을 묻지 않고 거두지 않는다. 쓸모 있는 큰 공개 조직에 편입을 제안하고, 조직이 받거나 거절한다.
/// - 제안: 제 땅에서 여덟 명 넘는 공개 조직, 쓸모가 있을 때만(위험한 마을의 자경단, 신앙 깊은 지배자의 신도회, 용병단, 상단).
///   지배자 한 집이 한 해에 한 번, 거절당하면 그 조직에는 다섯 해 동안 다시 묻지 않는다.
/// - 붙은 조직: 제 이름과 회칙과 곳간을 지킨다. 숨을 수 없고 보호세를 걷지 못한다. 번 것의 30%를 지배자에게 바치고 지배자는 조직원 수만큼 보탠다.
///   우두머리는 지배자 조직 안 사람이 되어(갈래 우두머리) 거사를 꾸밀 발판을 얻는다.
/// - 거절: 지배자가 의심한다. 거듭 거절당한 거친 지배자는 누르고, 신중한 지배자는 경고만 한다.
/// - 조직 바치기: 조직이 먼저 청한다. 떨어져 나가기: 붙은 조직이 홀로 선다. 지배자는 놓아주거나 누른다.
/// 넷 다 상호작용이라 플레이어도 같은 길을 쓴다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, int> _offerRefused = new();   // 조직 id → 거절한 날
    private readonly Dictionary<int, int> _orgRefusals = new();    // 조직 id → 거절한 횟수
    private const int OfferMinTotal = 8;
    private const float OfferCut = 0.3f;        // 붙은 조직이 바치는 몫
    private const float OfferSubsidy = 3f;      // 지배자가 조직원 한 명에 한 해 보태는 식량

    /// <summary>i 가 이끄는 홀로 선 공개 조직(도적 떼는 빼고). 여럿이면 가장 큰 것.</summary>
    public Group? LedFreeOrg(int i)
    {
        Group? best = null;
        foreach (var g in GroupsOf(i))
            if (IsLeader(i, g) && g.Parent < 0 && g.Home < 0 && g.Sanction < 0 && g.Kind != GroupKind.Bandits && g.Visibility >= Visibility.Public
                && (best == null || g.Total > best.Total)) best = g;
        return best;
    }

    /// <summary>i 가 이끄는, 지배자 집 lord 에 붙은 조직.</summary>
    public Group? LedAttachedOrg(int i, int lord)
    {
        foreach (var g in GroupsOf(i)) if (IsLeader(i, g) && IsAttached(g) && g.Sanction == lord) return g;
        return null;
    }

    /// <summary>i 가 이끄는 조직 가운데 지배자 lord 가 누를 수 있는 것(제 땅의 조직, 붙은 조직, 정체가 드러난 도적 떼).</summary>
    public Group? SuppressibleOrg(int lord, int i)
    {
        Group? best = null;
        foreach (var g in GroupsOf(i))
        {
            if (!IsLeader(i, g) || !IsTop(g) || g.Home >= 0) continue;
            bool mine = g.Sanction == lord || (g.Sanction < 0 && ConcernedLord(g) == lord);
            if (!mine) continue;
            if (g.Kind == GroupKind.Bandits ? !g.Exposed : g.Visibility < Visibility.Public) continue;
            if (best == null || g.Total > best.Total) best = g;
        }
        return best;
    }

    /// <summary>지배자 lord 의 땅에서 쓸모 있는 조직인가(제안할 만한가).</summary>
    private bool OrgUseful(Group g, int lord)
    {
        if (g.Strength > 0.5f * LordPower(lord)) return false;   // 제 힘의 절반을 넘는 조직은 품에 들이지 않는다
        var head = Households.Head[lord];
        float piety = People.IsValid(head) ? People.Piety[head.Index] : 0.5f;
        return g.Kind switch
        {
            GroupKind.Watch => DangerAt(g.Village) > 0.3f,
            GroupKind.Brotherhood => piety > 0.6f,
            GroupKind.Sellswords => DangerAt(g.Village) > 0.4f || PlotOf(lord) != null,
            GroupKind.Company => true,
            _ => false,
        };
    }

    /// <summary>조직 우두머리가 편입을 받아들일 확률: 지배자가 셀수록, 마을이 위험할수록, 지배자를 좋게 볼수록 받는다. 야심이 크면 버틴다.</summary>
    private float OfferOdds(Group g, int lord)
    {
        int leader = FindAlive(g.LeaderUid), lh = HeadIdx(lord);
        if (leader < 0 || lh < 0) return 0f;
        var p = People;
        float lp = LordPower(lord), gp = MathF.Max(0.5f, g.Strength);
        float ratio = lp / (lp + gp);
        float opin = Social.Opinion(p, leader, p.Uid[lh]) / 100f;
        float s = 0.3f + 0.6f * (ratio - 0.5f) + 0.3f * MathF.Min(1f, DangerAt(g.Village)) - 0.5f * (p.Get(leader, Trait.Ambition) - 0.5f) + 0.3f * opin;
        if (g.Kind == GroupKind.Brotherhood) s += 0.2f * (p.Piety[leader] - 0.5f);
        return Math.Clamp(s, 0.05f, 0.9f);
    }

    /// <summary>지배자가 조직의 바침을 받아 줄 확률: 쓸모 있으면 잘 받고, 조직이 너무 세면 꺼린다.</summary>
    private float PledgeOdds(Group g, int lord)
    {
        float lp = LordPower(lord), gp = MathF.Max(0.5f, g.Strength);
        if (gp > 0.5f * lp) return 0.05f;   // 너무 센 조직은 받지 않는다
        float s = (OrgUseful(g, lord) ? 0.6f : 0.25f) + 0.3f * HeadTrait(lord, Trait.Charisma);
        if (g.Kind == GroupKind.Brothel) s -= 0.4f;   // 유곽을 거두면 체면이 깎인다
        return Math.Clamp(s, 0.05f, 0.9f);
    }

    /// <summary>지배자가 떨어져 나가는 조직을 놓아줄 확률. 조직이 셀수록, 지배자가 신중할수록 놓아준다.</summary>
    private float LetGoOdds(Group g, int lord)
    {
        float win = SuppressOdds(g, lord);
        return Math.Clamp(0.3f + 0.4f * HeadTrait(lord, Trait.Caution) - 0.4f * HeadTrait(lord, Trait.Aggression) + 0.5f * (0.5f - win), 0.05f, 0.95f);
    }

    /// <summary>지배자가 조직을 눌러 이길 확률(힘의 제곱 비).</summary>
    public float SuppressOdds(Group g, int lord)
    {
        float lp = LordPower(lord), gp = MathF.Max(0.3f, g.Strength);
        return lp * lp / (lp * lp + gp * gp + 0.01f);
    }

    /// <summary>조직을 지배자 아래에 붙인다. 숨은 조직도 붙으면 드러낸다.</summary>
    private void Attach(Group g, int lord)
    {
        g.Sanction = lord;
        g.SanctionFounded = Households.FoundedDay[lord];
        var up = LordOrg(lord);
        if (up != null && TopOf(up).Id != g.Id) g.Parent = up.Id;
        if (g.Visibility < Visibility.Public) g.Visibility = Visibility.Public;
        foreach (var t in g.Tasks.ToList()) if (t.Verb is Verb.Collect or Verb.Agitate) g.Tasks.Remove(t);
        if (g.Tasks.Count == 0) DefaultTask(g);
        _offerRefused.Remove(g.Id);
        int leader = FindAlive(g.LeaderUid);
        if (leader >= 0) HeadsRelate(People.Household[leader], lord, 10f, 0f, 10f, MemoryType.Protected, 10);
        Stats.Sanctioned++;
    }

    /// <summary>붙은 조직이 홀로 선다.</summary>
    private void Detach(Group g)
    {
        if (!IsAttached(g)) return;
        g.Sanction = -1; g.SanctionFounded = -1;
        g.Parent = -1;
        Stats.OrgBreaks++;
    }

    /// <summary>제안을 거절당했다: 지배자가 의심하고 언짢아한다. 거듭 거절당한 거친 지배자는 누른다.</summary>
    private void Refused(Group g, int lord)
    {
        _offerRefused[g.Id] = Day;
        int n = _orgRefusals[g.Id] = _orgRefusals.GetValueOrDefault(g.Id) + 1;
        int leader = FindAlive(g.LeaderUid);
        if (leader < 0) return;
        int lh = People.Household[leader];
        HeadsRelate(lord, lh, -10f, 0f, 0f, MemoryType.Insulted, -10);
        Suspect(lord, lh, 10f + 2f * g.Total);
        int lhd0 = HeadIdx(lord);
        if (lhd0 < 0 || IsPlayer(lhd0)) return;   // 플레이어 지배자는 스스로 정한다
        float agg = HeadTrait(lord, Trait.Aggression), cau = HeadTrait(lord, Trait.Caution);
        if (n >= 2 && agg > 0.55f && SuppressOdds(g, lord) > 0.6f && _rng.Chance(0.3f + 0.5f * (agg - cau)))
            Interact(InteractionId.OrgSuppress, new InteractionContext { Actor = HeadIdx(lord), Target = leader, Third = -1 });
        else if (cau > 0.5f && HeadIdx(lord) is int lhd && lhd >= 0)
            Chron(leader, $"{SubjName(lhd)} 거절을 잊지 않겠다고 경고했다");
    }

    /// <summary>
    /// 해마다: 붙은 조직의 위를 맞추고 바침과 보탬을 셈하고, 지배자가 제안하고, 조직이 바치거나 떨어져 나가고, 드러난 도적 떼를 누른다.
    /// </summary>
    private void OfferYear()
    {
        var hh = Households; var p = People;
        var offered = new HashSet<int>();
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive || g.Home >= 0) continue;
            int leader = FindAlive(g.LeaderUid);
            if (leader < 0) continue;
            int lh = p.Household[leader];
            if (IsAttached(g))
            {
                int lord = g.Sanction;
                var up = LordOrg(lord, false);
                if (up == null) { Detach(g); continue; }
                if (TopOf(up).Id != g.Id) g.Parent = up.Id;
                // 지배자의 보탬
                // 품 안에서 너무 커지면 의심한다
                if (g.Strength > 0.6f * LordPower(lord)) Suspect(lord, lh, 10f * g.Strength / MathF.Max(1f, LordPower(lord)));
                float sub = MathF.Min(OfferSubsidy * g.Total, hh.Food[lord] * 0.02f);
                if (sub > 0f && Surplus(lord) > 1.2f) { hh.Food[lord] -= sub; Pay(g, sub); }
                // 떨어져 나가기: 지배자가 미운데 야심이 크거나, 조직이 지배자만큼 세다
                int lhd = HeadIdx(lord);
                if (IsPlayer(leader) || lhd < 0) continue;
                float opin = Social.Opinion(p, leader, p.Uid[lhd]);
                float amb = p.Get(leader, Trait.Ambition);
                bool reason = opin < -20f || g.Strength > 0.8f * LordPower(lord);
                if (!reason || !_rng.Chance(0.08f * amb)) continue;
                if (IsPlayer(lhd)) AskPlayer(InteractionId.OrgBreak, lh, lord);
                else Interact(InteractionId.OrgBreak, new InteractionContext { Actor = leader, Target = lhd, Third = -1 });
                continue;
            }
            if (g.Parent >= 0) continue;
            int lord2 = ConcernedLord(g);
            if (lord2 < 0 || !hh.IsAlive(lord2) || lord2 == lh) continue;
            int lordHead = HeadIdx(lord2);
            if (lordHead < 0) continue;
            // 드러난 도적 떼: 힘이 되면 친다
            if (g.Kind == GroupKind.Bandits)
            {
                if (!g.Exposed || IsPlayer(lordHead)) continue;
                if (_rng.Chance(0.3f + 0.4f * HeadTrait(lord2, Trait.Aggression)))
                    Interact(InteractionId.OrgSuppress, new InteractionContext { Actor = lordHead, Target = leader, Third = -1 });
                continue;
            }
            if (g.Visibility < Visibility.Public) continue;
            // 조직이 먼저 바친다: 작고 위험한 마을, 지배자가 훨씬 셀 때
            if (!IsPlayer(leader) && g.Total >= 4 && DangerAt(g.Village) > 0.6f && LordPower(lord2) > 3f * g.Strength
                && _rng.Chance(0.06f * (1.5f - p.Get(leader, Trait.Ambition))))
            {
                if (IsPlayer(lordHead)) { AskPlayer(InteractionId.OrgPledge, lh, lord2); continue; }
                Interact(InteractionId.OrgPledge, new InteractionContext { Actor = leader, Target = lordHead, Third = -1 });
                if (IsAttached(g)) continue;
            }
            if (IsPlayer(lordHead)) continue;   // 플레이어 지배자는 스스로 제안한다
            // 지배자의 제안
            if (g.Total < OfferMinTotal || offered.Contains(lord2) || !OrgUseful(g, lord2)) continue;
            if (_offerRefused.TryGetValue(g.Id, out int rd) && Day - rd < 5 * Config.DaysPerYear) continue;
            if (!_rng.Chance(0.15f + 0.25f * HeadTrait(lord2, Trait.Charisma))) continue;
            offered.Add(lord2);
            Stats.OrgOffers++;
            if (IsPlayer(leader)) AskPlayer(InteractionId.OrgOffer, lord2, lh);
            else Interact(InteractionId.OrgOffer, new InteractionContext { Actor = lordHead, Target = leader, Third = -1 });
        }
        Stats.AttachedOrgs = _groups.Count(g => g.Alive && IsAttached(g));
        Stats.FreePublicOrgs = _groups.Count(g => g.Alive && g.Parent < 0 && g.Home < 0 && g.Kind != GroupKind.Bandits && g.Visibility >= Visibility.Public);
    }

    /// <summary>지배자가 조직을 누른 결과. 이기면 조직이 흩어지고(도적 두목은 죽는다), 지면 지배자가 체면을 잃는다.</summary>
    private void SuppressResult(Group g, int lord, bool won)
    {
        var p = People;
        int leader = FindAlive(g.LeaderUid), lh = HeadIdx(lord);
        Stats.Suppressions++; Stats.OrgSuppressions++;
        Detach(g);
        if (won)
        {
            Stats.SuppressWins++; Stats.OrgSuppressWins++;
            if (leader >= 0)
            {
                foreach (int m in MembersFast(g).Take(30)) if (lh >= 0 && m != leader) Social.Add(p, m, p.Uid[lh], -10f, 15f);
                if (g.Kind == GroupKind.Bandits) { Stats.RaidDeaths++; Die(leader, false); }
                else { p.Honor[leader] -= 0.1f; HeadsRelate(p.Household[leader], lord, -30f, 20f, 0f, MemoryType.Punished, -30); }
            }
            CloseGroup(g);
        }
        else
        {
            if (lh >= 0) p.Honor[lh] -= 0.05f;
            g.Authority *= 1.2f;
            if (leader >= 0) HeadsRelate(lord, p.Household[leader], -20f, 10f, 0f, MemoryType.Insulted, -20);
        }
    }

    private void AddOfferDefs(InteractionDef[] d)
    {
        int H(World w, int i) => w.People.Household[i];
        d[(int)InteractionId.OrgOffer] = new InteractionDef
        {
            // 지배자(Actor) → 제 땅 조직 우두머리(Target). 받을 확률은 조직 쪽 셈
            Id = InteractionId.OrgOffer, Name = "조직 거두기 제안", Category = InteractionCategory.Rule,
            Can = (w, c) => w.HeadIdx(H(w, c.Actor)) == c.Actor && w.LedFreeOrg(c.Target) is { } g && w.ConcernedLord(g) == H(w, c.Actor)
                            && !(w._offerRefused.TryGetValue(g.Id, out int rd) && w.Day - rd < 5 * w.Config.DaysPerYear),
            Accept = (w, c) => w.LedFreeOrg(c.Target) is { } g ? w.OfferOdds(g, H(w, c.Actor)) : 0f,
            Success = (w, c, _) =>
            {
                var g = w.LedFreeOrg(c.Target)!;
                w.Stats.OrgOffersTaken++;
                w.Attach(g, H(w, c.Actor));
                w.RiseChron(c, x => $"{Topic(g.Name)} 거두었다", x => $"{Obj(g.Name)} {x}의 아래에 두기로 했다");
            },
            Fail = (w, c) =>
            {
                var g = w.LedFreeOrg(c.Target);
                if (g == null) return;
                w.RiseChron(c, x => $"{Obj(g.Name)} 거두려 했으나 {Subj(x)} 거절했다", x => $"{x}의 거두기 제안을 거절했다");
                w.Refused(g, H(w, c.Actor));
            },
        };
        d[(int)InteractionId.OrgPledge] = new InteractionDef
        {
            // 조직 우두머리(Actor) → 제 지배자(Target)
            Id = InteractionId.OrgPledge, Name = "조직 바치기", Category = InteractionCategory.Rule,
            Can = (w, c) => w.HeadIdx(H(w, c.Target)) == c.Target && w.LedFreeOrg(c.Actor) is { } g && w.ConcernedLord(g) == H(w, c.Target),
            Accept = (w, c) => w.LedFreeOrg(c.Actor) is { } g ? w.PledgeOdds(g, H(w, c.Target)) : 0f,
            Success = (w, c, _) =>
            {
                var g = w.LedFreeOrg(c.Actor)!;
                w.Stats.OrgPledges++;
                w.Attach(g, H(w, c.Target));
                w.RiseChron(c, x => $"{Obj(g.Name)} {x}에게 바쳤다", x => $"{Obj(g.Name)} 받아 거두었다");
            },
            Fail = (w, c) =>
            {
                w._offerRefused[w.LedFreeOrg(c.Actor)?.Id ?? -1] = w.Day;
                w.RiseChron(c, x => $"조직을 바치려 했으나 {Subj(x)} 받지 않았다", x => "조직을 바치겠다는 청을 물렸다");
            },
        };
        d[(int)InteractionId.OrgBreak] = new InteractionDef
        {
            // 붙은 조직 우두머리(Actor) → 지배자(Target). 받아들임 = 지배자가 놓아준다, 아니면 누르러 온다
            Id = InteractionId.OrgBreak, Name = "조직 떼어 나가기", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) => w.HeadIdx(H(w, c.Target)) == c.Target && w.LedAttachedOrg(c.Actor, H(w, c.Target)) != null,
            Accept = (w, c) => w.LedAttachedOrg(c.Actor, H(w, c.Target)) is { } g ? w.LetGoOdds(g, H(w, c.Target)) : 0f,
            Success = (w, c, _) =>
            {
                var g = w.LedAttachedOrg(c.Actor, H(w, c.Target))!;
                w.Detach(g);
                w.RiseChron(c, x => $"{Obj(g.Name)} 이끌고 {x}에게서 떨어져 나왔다", x => $"{Obj(g.Name)} 놓아주었다");
            },
            Fail = (w, c) =>
            {
                var g = w.LedAttachedOrg(c.Actor, H(w, c.Target));
                if (g == null) return;
                int lord = H(w, c.Target);
                bool won = w._rng.Chance(w.SuppressOdds(g, lord));
                w.RiseChron(c, x => won ? $"떨어져 나가려다 {x}에게 눌려 {Obj(g.Name)} 잃었다" : $"떨어져 나가려 하자 {Subj(x)} 눌렀지만 물리쳤다",
                               x => won ? $"떨어져 나가려는 {Obj(g.Name)} 눌러 흩었다" : $"떨어져 나가려는 {Obj(g.Name)} 누르려다 졌다");
                w.SuppressResult(g, lord, won);
            },
        };
        d[(int)InteractionId.OrgSuppress] = new InteractionDef
        {
            // 지배자(Actor) → 제 땅 조직 우두머리(Target). 받아들임 = 이겼다
            Id = InteractionId.OrgSuppress, Name = "조직 누르기", Category = InteractionCategory.Rule, Harsh = true,
            Can = (w, c) => w.HeadIdx(H(w, c.Actor)) == c.Actor && w.SuppressibleOrg(H(w, c.Actor), c.Target) != null,
            Accept = (w, c) => w.SuppressibleOrg(H(w, c.Actor), c.Target) is { } g ? w.SuppressOdds(g, H(w, c.Actor)) : 0f,
            Success = (w, c, _) =>
            {
                var g = w.SuppressibleOrg(H(w, c.Actor), c.Target)!;
                w.RiseChron(c, x => $"{Obj(g.Name)} 눌러 흩었다", x => $"이끌던 {Obj(g.Name)} {x}에게 잃었다");
                w.SuppressResult(g, H(w, c.Actor), true);
            },
            Fail = (w, c) =>
            {
                var g = w.SuppressibleOrg(H(w, c.Actor), c.Target);
                if (g == null) return;
                w.RiseChron(c, x => $"{Obj(g.Name)} 누르려다 졌다", x => $"{x}의 진압을 물리쳤다");
                w.SuppressResult(g, H(w, c.Actor), false);
            },
        };
    }
}
