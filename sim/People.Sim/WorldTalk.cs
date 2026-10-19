namespace People.Sim;

/// <summary>
/// B 묶음: 대화(설득, 위협, 떠보기), 청혼, 혼담, 이혼, 소문.
/// 자동 구애도 청혼 상호작용을 거친다. 플레이어와 NPC가 같은 정의를 쓴다.
/// </summary>
public sealed partial class World
{
    /// <summary>플레이어 집 식구가 스스로 짝을 찾는가. 끄면 플레이어가 청혼·혼담으로 직접 맺는다.</summary>
    public bool PlayerAutoMarry { get; set; } = true;

    private readonly HashSet<int> _probed = new();   // 플레이어 집이 떠본 사람(uid)
    public bool PlayerProbed(int uid) => _probed.Contains(uid);

    private bool CanMarry(int man, int woman)
    {
        var p = People;
        if (!Adult(man) || !Adult(woman) || !p.Male[man] || p.Male[woman]) return false;
        if (p.IsValid(p.Husband[woman]) || AgeYears(woman) > 45) return false;
        int h = p.Household[man];
        return p.WifeCount[man] == 0 || (Households.IsAlive(h) && Wealth(h) >= Config.PolygynyWealthPerWife * (p.WifeCount[man] + 1));
    }

    private bool IsWed(int a, int b)
    {
        var (man, woman) = Pair(a, b);
        return People.IsValid(People.Husband[woman]) && People.Husband[woman].Index == man;
    }

    private (int man, int woman) Pair(int a, int b) => People.Male[a] ? (a, b) : (b, a);

    /// <summary>a 가 b 를 짝으로 받아들일 확률(자유 구애). 짝 점수에 관계·명예·낙인을 더한다.</summary>
    private float MarryAccept(int a, int b)
    {
        var p = People;
        float back = MateScoreNoKin(a, b) - CoRaisedPenalty(a, b)
                   + Social.Opinion(p, a, p.Uid[b]) / 200f + 0.2f * p.Honor[b] - 0.3f * p.Stigma[b];
        return 1f / (1f + MathF.Exp(-(back - Config.AcceptMidpoint) / Config.AcceptSpread));
    }

    private void Wed(int a, int b)
    {
        var p = People;
        var (man, woman) = Pair(a, b);
        if (p.WifeCount[man] > 0) Stats.Polygynous++;
        Stats.Marriages++;
        Stats.MarriageKinSum += Lineage.Relatedness(p.Uid[man], p.Uid[woman]);
        float couple = Math.Clamp((MateScore(man, woman) + MateScore(woman, man)) * 0.5f, 0f, 1f);
        Marry(man, woman, couple);
    }

    /// <summary>두 사람을 갈라 세운다. 아내는 가장 가까운 혈족 집으로 가고, 아이들은 남편 집에 남는다.</summary>
    private void DoDivorce(int a, int b)
    {
        var p = People;
        var (man, woman) = Pair(a, b);
        if (!p.IsValid(p.Husband[woman]) || p.Husband[woman].Index != man) return;
        Chron(man, $"{With(Names.Full(Lineage, p.Uid[man]))} {Subj(Names.Full(Lineage, p.Uid[woman]))} 갈라섰다");
        Chron(woman, $"{With(Names.Full(Lineage, p.Uid[man]))} {Subj(Names.Full(Lineage, p.Uid[woman]))} 갈라섰다");
        p.Husband[woman] = PersonId.None;
        if (p.WifeCount[man] > 0) p.WifeCount[man]--;
        Social.Add(p, man, p.Uid[woman], -40f);
        Social.Add(p, woman, p.Uid[man], -40f);
        Social.Remember(p, man, MemoryType.Divorced, p.Uid[woman], Day, -40);
        Social.Remember(p, woman, MemoryType.Divorced, p.Uid[man], Day, -40);
        int h = p.Household[woman];
        int to = ClosestKinHousehold(woman, h);
        if (to >= 0 && to != h) MoveTo(woman, to);
        Stats.Divorces++;
    }

    private bool HasGrounds(int i, int spouse)
    {
        foreach (var (t, other, _, _) in Social.Memories(People, i))
            if ((t == MemoryType.SpouseAffair || t == MemoryType.Betrayed) && other == People.Uid[spouse]) return true;
        return false;
    }

    private void AddTalkDefs(InteractionDef[] d)
    {
        d[(int)InteractionId.Persuade] = new InteractionDef
        {
            Id = InteractionId.Persuade, Name = "설득", Category = InteractionCategory.Talk, NeedsThird = true, ThirdLabel = "누구를 좋게 보게 할까",
            Can = (w, c) => c.Third >= 0 && w.People.IsAliveIndex(c.Third) && c.Third != c.Target && c.Third != c.Actor,
            Accept = (w, c) =>
            {
                var p = w.People;
                float a = 0.4f + Social.Opinion(p, c.Target, p.Uid[c.Actor]) / 100f + 0.3f * (p.Get(c.Actor, Trait.Charisma) - p.Get(c.Target, Trait.Stubbornness));
                return Math.Clamp(a, 0.05f, 0.95f);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                Social.Add(p, c.Target, p.Uid[c.Third], 5f + 15f * p.Get(c.Actor, Trait.Charisma));
                w.Stats.Persuades++;
            },
            Fail = (w, c) => Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -3f),
        };
        d[(int)InteractionId.Threaten] = new InteractionDef
        {
            Id = InteractionId.Threaten, Name = "위협", Category = InteractionCategory.Talk,
            Can = (w, c) => w.Adult(c.Actor),
            Accept = (w, c) =>
            {
                // 겁을 먹는가: 집안 힘과 악명이 받는 쪽의 힘과 고집을 넘어야 한다
                var p = w.People;
                float pa = w.HouseholdPower(p.Household[c.Actor]) + 3f * p.Notoriety[c.Actor] + (w.PatronOf(p.Household[c.Target]) == p.Household[c.Actor] ? 2f : 0f);
                float pt = w.HouseholdPower(p.Household[c.Target]) + p.Get(c.Target, Trait.Stubbornness);
                float a = 1f / (1f + MathF.Exp(-(pa - pt) / 0.8f)) * (1f - 0.5f * p.Get(c.Target, Trait.Stubbornness));
                return Math.Clamp(a, 0.02f, 0.95f);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                Social.Add(p, c.Target, p.Uid[c.Actor], -15f, 25f);
                Social.Remember(p, c.Target, MemoryType.Threatened, p.Uid[c.Actor], w.Day, -25);
                p.Notoriety[c.Actor] = MathF.Min(1f, p.Notoriety[c.Actor] + 0.05f);
                w.Stats.Threats++;
            },
            Fail = (w, c) =>
            {
                var p = w.People;
                Social.Add(p, c.Target, p.Uid[c.Actor], -25f);
                Social.Remember(p, c.Target, MemoryType.Insulted, p.Uid[c.Actor], w.Day, -20);
                p.Honor[c.Actor] -= 0.03f;
            },
        };
        d[(int)InteractionId.Probe] = new InteractionDef
        {
            Id = InteractionId.Probe, Name = "떠보기", Category = InteractionCategory.Talk,
            Accept = (w, c) => Math.Clamp(0.6f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 200f + 0.3f * w.People.Get(c.Actor, Trait.Sociability), 0.1f, 0.98f),
            Success = (w, c, _) =>
            {
                var p = w.People;
                Social.Add(p, c.Target, p.Uid[c.Actor], 1f);
                if (w.IsPlayer(c.Actor)) w._probed.Add(p.Uid[c.Target]);
            },
        };
        d[(int)InteractionId.Propose] = new InteractionDef
        {
            Id = InteractionId.Propose, Name = "청혼", Category = InteractionCategory.Courtship,
            Can = (w, c) => w.People.Male[c.Actor] != w.People.Male[c.Target] && w.CanMarry(w.Pair(c.Actor, c.Target).man, w.Pair(c.Actor, c.Target).woman),
            Accept = (w, c) => w.MarryAccept(c.Target, c.Actor),
            Success = (w, c, _) =>
            {
                var p = w.People;
                if (w.IsPlayer(c.Target) && !w.IsPlayer(c.Actor))
                    w.Chron(c.Target, $"{w.SubjName(c.Actor)} {Names.Full(w.Lineage, p.Uid[c.Target])}에게 청혼했고 받아들였다");
                w.Wed(c.Actor, c.Target);
            },
            Fail = (w, c) => w.RememberRejection(c.Actor, w.People.Uid[c.Target]),
        };
        d[(int)InteractionId.ArrangeMatch] = new InteractionDef
        {
            // Actor = 가장, Target = 상대 집의 혼인할 사람, Third = 내 집의 혼인할 사람, Amount = 내가 얹는 지참금
            Id = InteractionId.ArrangeMatch, Name = "혼담", Category = InteractionCategory.Family, NeedsThird = true, ThirdLabel = "누구를 짝지을까",
            Amounts = new[] { 0f, 30f, 100f, 300f }, AmountNames = new[] { "지참금 없이", "지참금 조금", "지참금 넉넉히", "지참금 많이" },
            Can = (w, c) =>
            {
                var p = w.People; var hh = w.Households;
                int ah = p.Household[c.Actor];
                if (!hh.IsAlive(ah) || hh.Head[ah].Index != c.Actor || c.Third < 0 || !p.IsAliveIndex(c.Third)) return false;
                if (p.Household[c.Third] != ah || p.Household[c.Target] == ah || p.Male[c.Third] == p.Male[c.Target]) return false;
                var (m, f) = w.Pair(c.Third, c.Target);
                return w.CanMarry(m, f) && hh.Food[ah] >= c.Amount;
            },
            Accept = (w, c) => w.MatchAccept(c.Actor, c.Target, c.Third, c.Amount),
            Success = (w, c, _) =>
            {
                var p = w.People; var hh = w.Households;
                int ah = p.Household[c.Actor], th = p.Household[c.Target];
                if (c.Amount > 0 && hh.IsAlive(th)) { hh.Food[ah] -= c.Amount; hh.Food[th] += c.Amount; }
                var th2 = hh.Head[th];
                if (p.IsValid(th2) && th2.Index != c.Target)
                {
                    Social.Add(p, c.Actor, p.Uid[th2.Index], 15f);
                    Social.Add(p, th2.Index, p.Uid[c.Actor], 15f, 0f, MathF.Min(30f, c.Amount * 0.2f));
                }
                w.Chron(c.Third, $"혼담이 맺어졌다: {With(Names.Full(w.Lineage, p.Uid[c.Third]))} {Names.Full(w.Lineage, p.Uid[c.Target])}" + (c.Amount > 0 ? $" (지참금 {c.Amount:0})" : ""));
                w.Chron(c.Target, $"혼담이 맺어졌다: {With(Names.Full(w.Lineage, p.Uid[c.Target]))} {Names.Full(w.Lineage, p.Uid[c.Third])}");
                w.Wed(c.Third, c.Target);
                w.Stats.Arranged++;
            },
            Fail = (w, c) =>
            {
                var p = w.People;
                var th = w.Households.Head[p.Household[c.Target]];
                if (p.IsValid(th)) Social.Add(p, c.Actor, p.Uid[th.Index], -5f);
            },
        };
        d[(int)InteractionId.Divorce] = new InteractionDef
        {
            Id = InteractionId.Divorce, Name = "이혼", Category = InteractionCategory.Family,
            Can = (w, c) => w.People.Male[c.Actor] != w.People.Male[c.Target] && w.IsWed(c.Actor, c.Target),
            // 교회와 이웃의 눈: 신앙심이 크면 어렵고, 배우자의 불륜 같은 까닭이 있으면 쉽다
            Accept = (w, c) => Math.Clamp(0.55f - 0.5f * w.People.Piety[c.Actor] + (w.HasGrounds(c.Actor, c.Target) ? 0.4f : 0f), 0.05f, 0.95f),
            Success = (w, c, _) =>
            {
                if (!w.HasGrounds(c.Actor, c.Target)) w.People.Honor[c.Actor] -= 0.1f;
                w.DoDivorce(c.Actor, c.Target);
            },
        };
    }

    private string SubjName(int i) => Subj(Names.Full(Lineage, People.Uid[i]));

    /// <summary>
    /// 혼담을 받는 쪽(상대 집 가장)의 셈: 당사자의 마음(반쯤), 집안 격(잘사는 집과 맺으면 좋다), 가장끼리의 사이, 명예, 낙인, 지참금.
    /// </summary>
    public float MatchAccept(int actor, int target, int mine, float dowry)
    {
        var p = People; var hh = Households;
        int th = p.Household[target];
        var head = hh.Head[th];
        int hi = p.IsValid(head) ? head.Index : target;
        float v = 0.5f * MateScoreNoKin(target, mine) - CoRaisedPenalty(target, mine)
                + 0.6f * (WealthIndex(mine) - WealthIndex(target))
                + Social.Opinion(p, hi, p.Uid[actor]) / 150f + 0.4f * p.Honor[actor] - 0.4f * p.Stigma[mine]
                + 0.6f * dowry / (dowry + 0.5f * MathF.Max(0f, hh.Food[th]) + 20f)
                + (ClientCount(p.Household[actor]) > 0 ? 0.2f : 0f);
        return 1f / (1f + MathF.Exp(-(v - 0.25f) / 0.12f));
    }

    /// <summary>
    /// 해마다: 잘사는 집(지배자·지주와 식량이 많은 집)의 가장은 혼인할 나이의 자식을 비슷한 집과 혼담으로 맺으려 한다.
    /// 딸을 보내는 쪽이 지참금을 얹는다.
    /// </summary>
    private void ArrangeYear()
    {
        var p = People; var hh = Households;
        var rich = new List<int>();
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && hh.Village[h] >= 0 && (ClientCount(h) > 0 || Wealth(h) >= Config.ArrangeWealth)) rich.Add(h);
        if (rich.Count < 2) return;
        var single = new List<int>();
        foreach (int h in rich)
        {
            var head = hh.Head[h];
            if (!p.IsValid(head) || (h == PlayerHousehold)) continue;
            single.Clear();
            for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
            {
                int age = AgeYears(k);
                if (k == head.Index || age < 16 || age > 25) continue;
                if (p.Male[k] ? p.WifeCount[k] == 0 : !p.IsValid(p.Husband[k])) single.Add(k);
            }
            foreach (int mine in single)
            {
                if (!_rng.Chance(0.5f)) continue;
                // 비슷한 집의 혼인할 이성 몇을 보고 가장 받아들일 법한 쪽에 넣는다
                float dowry = p.Male[mine] ? 0f : DowryFor(h);
                int best = -1;
                float bs = 0.15f;
                for (int t = 0; t < 20; t++)
                {
                    int o = rich[_rng.NextInt(rich.Count)];
                    if (o == h || Dist2(o, h) > Config.ProtectRange * Config.ProtectRange) continue;
                    for (int k = hh.FirstMember[o]; k >= 0; k = p.HhNext[k])
                    {
                        if (p.Male[k] == p.Male[mine] || AgeYears(k) < 16) continue;
                        var (m, f) = Pair(mine, k);
                        if (!CanMarry(m, f)) continue;
                        float a = MatchAccept(head.Index, k, mine, dowry);
                        if (a > bs) { bs = a; best = k; }
                    }
                }
                if (best < 0) continue;
                Interact(InteractionId.ArrangeMatch, new InteractionContext { Actor = head.Index, Target = best, Third = mine, Amount = dowry });
            }
        }
    }

    private float DowryFor(int h)
    {
        float f = Households.Food[h] * 0.1f;
        float pick = 0f;
        foreach (float a in Def(InteractionId.ArrangeMatch).Amounts) if (a <= f) pick = a;
        return pick;
    }

    /// <summary>들킨 불륜·숨은 아이: 속은 배우자가 갈라서려 한다. 고집이 세면 더, 신앙심이 크면 덜.</summary>
    private void MaybeDivorce(int victim, int spouse)
    {
        if (victim < 0 || spouse < 0 || !People.IsAliveIndex(victim) || !People.IsAliveIndex(spouse) || IsPlayer(victim)) return;
        var p = People;
        if (p.Male[victim] == p.Male[spouse] || !IsWed(victim, spouse)) return;
        float want = Math.Clamp(0.12f + 0.25f * p.Get(victim, Trait.Stubbornness) - 0.4f * p.Piety[victim], 0f, 0.6f);
        if (_rng.Chance(want)) Interact(InteractionId.Divorce, new InteractionContext { Actor = victim, Target = spouse, Third = -1 });
    }

    /// <summary>험담할 때 아는 비밀이 있으면 듣는 사람도 알게 된다. 아는 사람이 늘면 들킬 위험이 커진다.</summary>
    private void SpreadRumor(int teller, int listener, int about)
    {
        var p = People;
        int tu = p.Uid[teller], lu = p.Uid[listener], au = p.Uid[about];
        foreach (var s in _secrets)
        {
            if (s.Exposed || (s.A != au && s.B != au) || !s.Known.Contains(tu) || s.Known.Contains(lu)) continue;
            s.Known.Add(lu);
            Stats.Rumors++;
        }
    }

    /// <summary>i 가 아는 남의 비밀의 당사자 하나(같은 마을, 없으면 -1).</summary>
    private int SecretTarget(int i, int v)
    {
        int u = People.Uid[i];
        foreach (var s in _secrets)
        {
            if (s.Exposed || s.A == u || s.B == u || !s.Known.Contains(u)) continue;
            int t = FindAlive(s.A, v);
            if (t >= 0) return t;
        }
        return -1;
    }

    /// <summary>a 와 b 가 몰래 만나면 한 해에 들킬 위험(화면 표시용, DetectSecrets 와 같은 셈).</summary>
    public float AffairRisk(int a, int b)
    {
        float risk = Config.SecretBaseRisk;
        int sa = SpouseOf(a), sb = SpouseOf(b);
        int victim = sa >= 0 ? sa : sb;
        if (victim >= 0) risk += 0.25f * People.Get(victim, Trait.Perception);
        if (Households.IsAlive(People.Household[a])) risk += 0.02f * Households.Members[People.Household[a]];
        return MathF.Min(1f, risk);
    }
}
