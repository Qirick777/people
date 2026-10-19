namespace People.Sim;

/// <summary>
/// C 묶음: 첩과 서자, 자식 인정, 후계자 지정, 분가 명령, 의절, 억지 혼담, 입막음, 약초꾼의 피임·낙태 약, 몸 파는 일.
/// 성적인 일은 어른끼리, 받아들일 때만 일어나고, 압박은 쓰지 않는다. 화면에는 글 한 줄과 수치로만 나온다.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, (int uid, int founded)> _heir = new();   // 집 → 지정 후계자
    private readonly Dictionary<int, List<int>> _healers = new();             // 마을 → 약초꾼(해마다 다시 센다)
    private readonly List<(int woman, int healer)> _abortCheck = new();

    /// <summary>집 h 의 지정 후계자(사람 번호, 없거나 집에 없으면 -1).</summary>
    public int HeirOf(int h)
    {
        if (!_heir.TryGetValue(h, out var e) || !Households.IsAlive(h) || Households.FoundedDay[h] != e.founded) return -1;
        foreach (int k in MembersOf(h)) if (People.Uid[k] == e.uid) return k;
        return -1;
    }

    private bool IsHead(int i) => Households.IsAlive(People.Household[i]) && Households.Head[People.Household[i]].Index == i;
    private bool SameHouse(int a, int b) => People.Household[a] == People.Household[b];
    private bool IsHealer(int i) => People.IsAliveIndex(i) && People.JobOf[i] == (byte)Job.Healer;

    private void RebuildHealers()
    {
        _healers.Clear();
        var p = People;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!IsHealer(i) || !Households.IsAlive(p.Household[i])) continue;
            int v = Households.Village[p.Household[i]];
            if (v >= 0) (_healers.TryGetValue(v, out var l) ? l : _healers[v] = new()).Add(i);
        }
    }

    private int HealerNear(int i)
    {
        int h = People.Household[i];
        if (!Households.IsAlive(h) || !_healers.TryGetValue(Households.Village[h], out var l)) return -1;
        foreach (int k in l) if (IsHealer(k) && k != i) return k;
        return -1;
    }

    private bool Rich(int h) => ClientCount(h) > 0 || Wealth(h) >= Config.ArrangeWealth;

    private void Pay(int from, int to, float amount)
    {
        var hh = Households;
        int a = People.Household[from], b = People.Household[to];
        amount = MathF.Min(amount, hh.IsAlive(a) ? MathF.Max(0f, hh.Food[a]) : 0f);
        if (hh.IsAlive(a)) hh.Food[a] -= amount;
        if (hh.IsAlive(b)) hh.Food[b] += amount;
    }

    private void AddFamilyDefs(InteractionDef[] d)
    {
        d[(int)InteractionId.TakeConcubine] = new InteractionDef
        {
            Id = InteractionId.TakeConcubine, Name = "첩으로 들이기", Category = InteractionCategory.Family, Sexual = true,
            Can = (w, c) =>
            {
                var p = w.People;
                if (!p.Male[c.Actor] || p.Male[c.Target] || p.WifeCount[c.Actor] == 0 || w.SameHouse(c.Actor, c.Target)) return false;
                if (p.IsValid(p.Husband[c.Target]) || w.AgeYears(c.Target) > 40) return false;
                int h = p.Household[c.Actor];
                return w.Households.IsAlive(h) && (w.ClientCount(h) > 0 || w.Wealth(h) >= w.Config.PolygynyWealthPerWife * (p.WifeCount[c.Actor] + 1));
            },
            // 여자 쪽: 짝으로 끌리는 정도(청혼과 같은 셈)에 형편과 상대의 재산을 더하고, 신앙심이 막는다.
            Accept = (w, c) =>
            {
                var p = w.People;
                int th = p.Household[c.Target];
                float need = w.Households.IsAlive(th) ? Math.Clamp(1f - w.Surplus(th), 0f, 1f) : 1f;
                float a = w.MarryAccept(c.Target, c.Actor) + 0.3f * need + 0.3f * (w.WealthIndex(c.Actor) - w.WealthIndex(c.Target)) - 0.3f * p.Piety[c.Target];
                return Math.Clamp(a, 0f, 0.95f);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                int man = c.Actor, woman = c.Target;
                foreach (int k in w.MembersOf(p.Household[man]))
                    if (!p.Male[k] && p.IsValid(p.Husband[k]) && p.Husband[k].Index == man)
                    {
                        Social.Add(p, k, p.Uid[man], -15f);   // 아내들의 시샘
                        Social.Add(p, k, p.Uid[woman], -20f);
                    }
                w.Wed(man, woman);
                p.Honor[man] -= 0.03f;   // 교회는 첩을 꾸짖는다
                w.Chron(man, $"{w.SubjName(man)} {Names.Full(w.Lineage, p.Uid[woman])}을(를) 첩으로 들였다");
            },
            Fail = (w, c) => w.RememberRejection(c.Actor, w.People.Uid[c.Target]),
        };
        d[(int)InteractionId.Acknowledge] = new InteractionDef
        {
            Id = InteractionId.Acknowledge, Name = "자식으로 인정", Category = InteractionCategory.Family,
            Can = (w, c) =>
            {
                var p = w.People; var lin = w.Lineage;
                int u = p.Uid[c.Target], me = p.Uid[c.Actor];
                var r = lin.RightOf(u);
                if (!p.Male[c.Actor] || r == Birthright.Legit) return false;
                return r == Birthright.Concubine ? lin.Father(u) == me : lin.BioFather(u) == me && lin.Father(u) < 0;
            },
            Success = (w, c, _) =>
            {
                var p = w.People; var lin = w.Lineage;
                int u = p.Uid[c.Target];
                bool bastard = lin.RightOf(u) == Birthright.Bastard;
                lin.Legitimize(u, p.Uid[c.Actor]);
                if (bastard)
                {
                    // 밖에서 낳은 아이를 받아들이면 아내가 서운해하고 소문이 난다
                    p.Honor[c.Actor] -= 0.05f;
                    int sp = w.SpouseOf(c.Actor);
                    if (sp >= 0) Social.Add(p, sp, p.Uid[c.Actor], -30f);
                    if (w.AgeYears(c.Target) < 16 && !w.SameHouse(c.Actor, c.Target)) w.MoveTo(c.Target, p.Household[c.Actor]);
                }
                Social.AddFrom(p, c.Target, c.Actor, 30f);
                w.Chron(c.Actor, $"{w.SubjName(c.Actor)} {Names.Full(w.Lineage, u)}을(를) 자식으로 인정했다");
                w.Stats.Acknowledged++;
            },
        };
        d[(int)InteractionId.NameHeir] = new InteractionDef
        {
            Id = InteractionId.NameHeir, Name = "후계자로 정하기", Category = InteractionCategory.Family,
            Can = (w, c) => w.IsHead(c.Actor) && w.SameHouse(c.Actor, c.Target) && !w.Lineage.Disowned(w.People.Uid[c.Target])
                            && w.HeirOf(w.People.Household[c.Actor]) != c.Target,
            Success = (w, c, _) =>
            {
                var p = w.People;
                int h = p.Household[c.Actor];
                w._heir[h] = (p.Uid[c.Target], w.Households.FoundedDay[h]);
                // 건너뛴 형들은 서운해한다
                int age = w.AgeYears(c.Target);
                foreach (int k in w.MembersOf(h))
                    if (k != c.Target && k != c.Actor && p.Male[k] && w.AgeYears(k) > age && w.Lineage.Father(p.Uid[k]) == p.Uid[c.Actor])
                    {
                        Social.Add(p, k, p.Uid[c.Actor], -15f);
                        Social.Add(p, k, p.Uid[c.Target], -15f);
                    }
                Social.AddFrom(p, c.Target, c.Actor, 15f);
                w.Chron(c.Actor, $"{Subj(Names.Full(w.Lineage, p.Uid[c.Target]))} 후계자로 정해졌다");
                w.Stats.HeirsNamed++;
            },
        };
        d[(int)InteractionId.OrderSplit] = new InteractionDef
        {
            Id = InteractionId.OrderSplit, Name = "분가시키기", Category = InteractionCategory.Family,
            Can = (w, c) => w.IsHead(c.Actor) && w.SameHouse(c.Actor, c.Target) && w.People.Male[c.Target] && w.Adult(c.Target)
                            && w.People.WifeCount[c.Target] > 0,
            Success = (w, c, _) =>
            {
                var p = w.People;
                int h = p.Household[c.Actor];
                bool poor = w.Surplus(h) < 0.8f;
                if (w.SplitOff(c.Target, h) >= 0)
                {
                    Social.AddFrom(p, c.Target, c.Actor, poor ? -8f : 5f);   // 가난한 집에서 내보내면 서운하다
                    w.Stats.OrderedSplits++;
                    w.RecountHouseholds();
                }
            },
        };
        d[(int)InteractionId.Disown] = new InteractionDef
        {
            Id = InteractionId.Disown, Name = "의절", Category = InteractionCategory.Family, Harsh = true,
            Can = (w, c) => w.IsHead(c.Actor) && w.SameHouse(c.Actor, c.Target) && w.AgeYears(c.Target) >= 12,
            Success = (w, c, _) => w.DoDisown(c.Actor, c.Target),
        };
        var am = d[(int)InteractionId.ArrangeMatch];
        d[(int)InteractionId.ForcedMatch] = new InteractionDef
        {
            // 혼담과 같되 내 식구의 뜻을 묻지 않는다. 대신 그 식구의 마음이 떠난다.
            Id = InteractionId.ForcedMatch, Name = "억지 혼담", Category = InteractionCategory.Family, NeedsThird = true, Harsh = true, ThirdLabel = "누구를 억지로 보낼까",
            Amounts = am.Amounts, AmountNames = am.AmountNames, Can = am.Can,
            Accept = (w, c) => w.MatchAccept(c.Actor, c.Target, c.Third, c.Amount),
            Success = (w, c, r) =>
            {
                var p = w.People;
                bool unwilling = w.Consent(c.Third, c.Target) < 0.6f;
                am.Success!(w, c, r);
                if (!unwilling) return;
                Social.Add(p, c.Third, p.Uid[c.Actor], -30f);
                Social.Remember(p, c.Third, MemoryType.Forced, p.Uid[c.Actor], w.Day, -40);
                if (p.LoyalUid[c.Third] == p.Uid[c.Actor]) p.Loyalty[c.Third] = MathF.Max(0f, p.Loyalty[c.Third] - 0.15f);
                w.Stats.Forced++;
            },
        };
        d[(int)InteractionId.Hush] = new InteractionDef
        {
            Id = InteractionId.Hush, Name = "입막음", Category = InteractionCategory.Scheme,
            Amounts = new[] { 10f, 40f, 120f }, AmountNames = new[] { "조금 쥐여 준다", "넉넉히 준다", "크게 준다" },
            Can = (w, c) => w.KnowsMySecret(c.Actor, c.Target) && w.Households.IsAlive(w.People.Household[c.Actor])
                            && w.Households.Food[w.People.Household[c.Actor]] >= c.Amount,
            Accept = (w, c) =>
            {
                var p = w.People;
                int th = p.Household[c.Target];
                float amt = c.Amount;
                float a = 0.3f + 0.6f * amt / (amt + 0.3f * (w.Households.IsAlive(th) ? MathF.Max(0f, w.Households.Food[th]) : 0f) + 15f)
                        + Social.Opinion(p, c.Target, p.Uid[c.Actor]) / 200f - 0.3f * p.Piety[c.Target] + 0.2f * Social.Fear(p, c.Target, p.Uid[c.Actor]) / 100f;
                return Math.Clamp(a, 0.05f, 0.95f);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                w.Pay(c.Actor, c.Target, c.Amount);
                int me = p.Uid[c.Actor], them = p.Uid[c.Target];
                foreach (var s in w._secrets) if (!s.Exposed && (s.A == me || s.B == me)) s.Known.Remove(them);
                Social.Add(p, c.Target, me, 3f);
                w.Stats.Hushes++;
            },
            Fail = (w, c) =>
            {
                // 거절한 사람은 떠벌릴 수 있다
                if (!w._rng.Chance(0.35f)) return;
                int me = w.People.Uid[c.Actor];
                var s = w._secrets.FirstOrDefault(x => !x.Exposed && (x.A == me || x.B == me) && x.Known.Contains(w.People.Uid[c.Target]));
                if (s != null) w.ExposeNow(s);
            },
        };
        d[(int)InteractionId.BuyContraception] = new InteractionDef
        {
            Id = InteractionId.BuyContraception, Name = "피임 약 사기", Category = InteractionCategory.Trade,
            Can = (w, c) => w.Adult(c.Actor) && w.IsHealer(c.Target) && w.Households.IsAlive(w.People.Household[c.Actor])
                            && w.Households.Food[w.People.Household[c.Actor]] >= w.Config.ContraPrice,
            Accept = (w, c) => Math.Clamp(0.9f - 0.3f * w.People.Piety[c.Target], 0.3f, 0.95f),
            Success = (w, c, _) =>
            {
                w.Pay(c.Actor, c.Target, w.Config.ContraPrice);
                w.People.ContraUntil[c.Actor] = w.Day + w.Config.DaysPerYear;
                w.Stats.ContraSold++;
            },
        };
        d[(int)InteractionId.BuyAbortion] = new InteractionDef
        {
            Id = InteractionId.BuyAbortion, Name = "낙태 약 사기", Category = InteractionCategory.Trade, Secret = true,
            Can = (w, c) => !w.People.Male[c.Actor] && w.People.PregnantUntil[c.Actor] >= 0 && w.IsHealer(c.Target)
                            && w.Households.IsAlive(w.People.Household[c.Actor]) && w.Households.Food[w.People.Household[c.Actor]] >= w.Config.AbortionPrice,
            Accept = (w, c) => Math.Clamp(0.75f - 0.6f * w.People.Piety[c.Target], 0.05f, 0.9f),
            Success = (w, c, _) => w.TakeAbortion(c.Actor, c.Target),
        };
    }

    public bool KnowsMySecret(int me, int other)
    {
        int mu = People.Uid[me], ou = People.Uid[other];
        foreach (var s in _secrets)
            if (!s.Exposed && (s.A == mu || s.B == mu) && s.A != ou && s.B != ou && s.Known.Contains(ou)) return true;
        return false;
    }

    private void ExposeNow(Secret s)
    {
        var byUid = new Dictionary<int, int>();
        for (int i = 0; i < People.HighWater; i++) if (People.IsAliveIndex(i)) byUid[People.Uid[i]] = i;
        int victim = -1;
        if (byUid.TryGetValue(s.A, out int a)) { int sp = SpouseOf(a); if (sp >= 0) victim = sp; }
        if (victim < 0 && byUid.TryGetValue(s.B, out int b)) { int sp = SpouseOf(b); if (sp >= 0) victim = sp; }
        Expose(s, byUid, victim);
        _secrets.Remove(s);
    }

    private void TakeAbortion(int woman, int healer)
    {
        var p = People;
        Pay(woman, healer, Config.AbortionPrice);
        AddSecret(SecretType.Abortion, woman, healer);
        Stats.Abortions++;
        if (_rng.Chance(Config.AbortionDeath))
        {
            Stats.AbortionDeaths++;
            Die(woman, false);
            return;
        }
        if (!_rng.Chance(Config.AbortionWorks)) return;
        p.PregnantUntil[woman] = -1;
        p.CooldownUntil[woman] = Day + Config.DaysPerSeason;
        // 숨은 아버지 비밀은 아이가 없으니 묻힌다
        int u = p.Uid[woman];
        _secrets.RemoveAll(s => s.Type == SecretType.HiddenFather && s.A == u && s.Child < 0);
    }

    /// <summary>의절: 집에서 내보내고 상속에서 뺀다. 혼인한 아들이면 따로 살림을 내 주고, 아니면 가까운 혈족 집으로 간다.</summary>
    private void DoDisown(int head, int k)
    {
        var p = People;
        int h = p.Household[head];
        Lineage.SetDisowned(p.Uid[k], true);
        Social.Add(p, k, p.Uid[head], -60f);
        Social.Add(p, head, p.Uid[k], -40f);
        Social.Remember(p, k, MemoryType.Disowned, p.Uid[head], Day, -70);
        Social.Remember(p, head, MemoryType.Disowned, p.Uid[k], Day, -40);
        if (HeirOf(h) == k) _heir.Remove(h);
        Chron(head, $"{SubjName(head)} {Names.Full(Lineage, p.Uid[k])}와(과) 의절했다");
        if (p.Male[k] && p.WifeCount[k] > 0 && SplitOff(k, h) >= 0) { RecountHouseholds(); }
        else
        {
            int to = ClosestKinHousehold(k, h);
            if (to >= 0 && to != h) MoveTo(k, to);
        }
        Stats.Disowned++;
    }

    /// <summary>
    /// 해마다: 잘사는 남자의 첩, 자식 인정, 후계자 지정, 입막음, 피임 약, 몸 파는 일.
    /// </summary>
    private void FamilyYear()
    {
        var p = People; var hh = Households;
        RebuildHealers();
        var byVillage = new Dictionary<int, List<int>>();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!Adult(i) || !hh.IsAlive(p.Household[i]) || hh.Village[p.Household[i]] < 0) continue;
            (byVillage.TryGetValue(hh.Village[p.Household[i]], out var l) ? l : byVillage[hh.Village[p.Household[i]]] = new()).Add(i);
        }
        foreach (var (v, folk) in byVillage)
        {
            int workers = 0;
            foreach (int i in folk) if (p.JobOf[i] == (byte)Job.SexWorker) workers++;
            int maxWorkers = VillageList.Households[v] >= Config.TradeVillage ? VillageList.Households[v] / 25 : 0;
            foreach (int i in folk)
            {
                if (IsPlayer(i)) continue;
                int h = p.Household[i];
                bool head = hh.Head[h].Index == i;
                // 첩: 잘살고 욕심 많고 신앙심 옅은 남자
                if (p.Male[i] && p.WifeCount[i] > 0 && Rich(h) && p.Get(i, Trait.Ambition) > 0.5f && p.Piety[i] < 0.5f && _rng.Chance(0.25f))
                {
                    int best = -1;
                    float bs = 0.1f;
                    for (int t = 0; t < 12; t++)
                    {
                        int c = folk[_rng.NextInt(folk.Count)];
                        if (p.Male[c] || p.IsValid(p.Husband[c]) || AgeYears(c) > 40 || SameHouse(c, i)) continue;
                        float sc = MateScoreNoKin(i, c);
                        if (sc > bs) { bs = sc; best = c; }
                    }
                    if (best >= 0) Interact(InteractionId.TakeConcubine, new InteractionContext { Actor = i, Target = best, Third = -1 });
                }
                if (head && p.Male[i]) NpcHeirAndKids(i, h);
                // 입막음: 아는 사람이 늘어난 비밀
                if (hh.Food[h] > 50f && _rng.Chance(0.3f)) NpcHush(i, h);
                // 피임 약: 몰래 만나는 사람, 몸 파는 사람, 가난한데 아이 많은 집의 여자
                bool want = _lover.ContainsKey(p.Uid[i]) || p.JobOf[i] == (byte)Job.SexWorker
                            || (!p.Male[i] && p.IsValid(p.Husband[i]) && Lineage.Children(p.Uid[i]) >= 6 && Surplus(h) < 0.8f && p.Piety[i] < 0.5f);
                if (want && !p.Male[i] && p.ContraUntil[i] < Day)
                {
                    int healer = HealerNear(i);
                    if (healer >= 0) Interact(InteractionId.BuyContraception, new InteractionContext { Actor = i, Target = healer, Third = -1 });
                }
                // 몸 파는 일: 큰 마을의 가난한 미혼, 신앙심이 옅고 낙인이 있거나 욕심이 큰 사람
                if (p.JobOf[i] == (byte)Job.SexWorker)
                {
                    if (AgeYears(i) > 40 || (!p.Male[i] && p.IsValid(p.Husband[i])) || Surplus(h) > 1.6f) { p.JobOf[i] = 0; workers--; }
                }
                else if (workers < maxWorkers && p.JobOf[i] == 0 && !head && AgeYears(i) <= 35 && Surplus(h) < 0.7f && p.Piety[i] < 0.4f
                         && (p.Male[i] ? p.Get(i, Trait.Looks) > Config.MaleAppealFloor && p.WifeCount[i] == 0 : !p.IsValid(p.Husband[i]))
                         && (p.Stigma[i] > 0.15f || p.Get(i, Trait.Ambition) > 0.6f) && _rng.Chance(0.3f))
                {
                    p.JobOf[i] = (byte)Job.SexWorker;
                    Lineage.SetJob(p.Uid[i], Job.SexWorker);
                    workers++;
                    Stats.NewSexWorkers++;
                }
            }
        }
    }

    /// <summary>NPC 가장: 서자·사생아를 인정하고, 잘사는 집이면 가장 나은 아들을 후계자로 정한다.</summary>
    private void NpcHeirAndKids(int head, int h)
    {
        var p = People;
        var kids = MembersOf(h).Where(k => k != head && p.Male[k] && Lineage.Father(p.Uid[k]) == p.Uid[head]).ToList();
        bool legitSon = kids.Any(k => Lineage.RightOf(p.Uid[k]) == Birthright.Legit);
        // 마흔이 넘도록 적자 아들이 없으면 서자를 인정해 대를 잇는다
        if (!legitSon && AgeYears(head) >= 40)
        {
            int bastard = kids.Where(k => Lineage.RightOf(p.Uid[k]) == Birthright.Concubine).OrderByDescending(AgeYears).DefaultIfEmpty(-1).First();
            if (bastard >= 0 && _rng.Chance(0.3f)) Interact(InteractionId.Acknowledge, new InteractionContext { Actor = head, Target = bastard, Third = -1 });
        }
        if (!Rich(h) || HeirOf(h) >= 0 || !_rng.Chance(0.1f)) return;
        var adults = kids.Where(k => AgeYears(k) >= 16 && !Lineage.Disowned(p.Uid[k]) && Lineage.RightOf(p.Uid[k]) == Birthright.Legit).ToList();
        if (adults.Count < 2) return;
        float Ability(int k) => p.Get(k, Trait.Intelligence) + p.Get(k, Trait.Charisma) + p.GetSkill(k, Skill.Letters) + 0.5f * p.GetSkill(k, Skill.Arms);
        int eldest = adults.OrderByDescending(AgeYears).First();
        int best = adults.OrderByDescending(Ability).First();
        if (best != eldest && Ability(best) > Ability(eldest) + 0.25f)
            Interact(InteractionId.NameHeir, new InteractionContext { Actor = head, Target = best, Third = -1 });
    }

    private void NpcHush(int i, int h)
    {
        var p = People;
        int u = p.Uid[i];
        foreach (var s in _secrets)
        {
            if (s.Exposed || (s.A != u && s.B != u) || s.Known.Count <= 2) continue;
            foreach (int ku in s.Known)
            {
                if (ku == s.A || ku == s.B || !_uidIndex.TryGetValue(ku, out int k) || !p.IsAliveIndex(k) || p.Uid[k] != ku) continue;
                float amt = Households.Food[h] > 300f ? 120f : Households.Food[h] > 100f ? 40f : 10f;
                Interact(InteractionId.Hush, new InteractionContext { Actor = i, Target = k, Third = -1, Amount = amt });
                return;
            }
        }
    }

    /// <summary>남편이 아닌 사람의 아이가 들어섰다(불륜·거래·혼인 밖): 신앙심이 옅으면 약초꾼을 찾는다.</summary>
    private void MaybeAbort(int woman)
    {
        if (IsPlayer(woman) || !People.IsAliveIndex(woman)) return;
        int healer = HealerNear(woman);
        if (healer < 0) return;
        float want = MathF.Max(0f, 0.6f - People.Piety[woman]) + (People.JobOf[woman] == (byte)Job.SexWorker ? 0.3f : 0f);
        if (_rng.Chance(want)) Interact(InteractionId.BuyAbortion, new InteractionContext { Actor = woman, Target = healer, Third = -1 });
    }
}
