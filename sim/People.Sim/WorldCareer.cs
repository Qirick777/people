namespace People.Sim;

/// <summary>
/// 살림과 자리. 밑바닥에서 올라가는 수를 상호작용으로 둔다(플레이어와 NPC가 같은 길을 쓴다).
/// - 땅 사기: 값은 땅값의 1.3배(급매가 아니다). 파는 쪽은 형편이 어렵거나 땅이 남을수록 판다.
/// - 도제 보내기: 내 집 아이(10~15세)를 일 가진 사람에게 맡긴다. 사례 식량 10.
/// - 군인·징수관 자원: 지배자에게 일자리를 청한다. 자리가 비었을수록 받는다.
/// - 측근 청하기: 갈래가 있는 조직의 우두머리에게 측근 자리를 청한다. 받으면 해가 바뀌어도 측근이다.
/// - 봉토 청하기: 지배자의 아래 집 가장이 마을 몇 집을 맡겠다고 청한다.
/// </summary>
public sealed partial class World
{
    private const float BuyPremium = 1.3f;
    private const float ApprenticeFee = 10f;

    // ------------------------------------------------------------------ 땅

    /// <summary>집 seller 가 buyer 에게 팔 만한 필지(남이 부치지 않는 것 가운데 가장 나쁜 땅, buyer 가 부치던 땅은 먼저). 없으면 -1.</summary>
    private int PlotForSale(int seller, int buyer)
    {
        int best = -1;
        float bf = float.MaxValue;
        foreach (int id in Map.PlotsOf(seller))
        {
            bool tilledByBuyer = _tiller.TryGetValue(id, out int t) && t == buyer;
            if (_tiller.ContainsKey(id) && !tilledByBuyer) continue;
            float f = Map.BaseFert(id) - (tilledByBuyer ? 10f : 0f);
            if (f < bf) { bf = f; best = id; }
        }
        return best;
    }

    public float LandPrice(int seller, int buyer)
    {
        int id = PlotForSale(seller, buyer);
        return id < 0 ? -1f : Map.PlotValue(id) * BuyPremium;
    }

    /// <summary>파는 쪽이 받아들일 확률: 식량이 모자랄수록, 땅이 많을수록, 사는 쪽을 좋게 볼수록. 마지막 한 필지는 팔지 않는다.</summary>
    private float SellOdds(int seller, int buyer)
    {
        var hh = Households;
        int n = hh.PlotCount[seller];
        if (n <= 1) return 0f;
        float want = Math.Clamp(1.2f - Surplus(seller), 0f, 1f);
        float spare = MathF.Min(1f, n / 12f);
        int sh = HeadIdx(seller), bh = HeadIdx(buyer);
        float opin = sh >= 0 && bh >= 0 ? Social.Opinion(People, sh, People.Uid[bh]) / 200f : 0f;
        float s = 0.05f + 0.45f * want + 0.3f * spare + opin;
        if (TierOf(seller) >= 1) s -= 0.15f;   // 지배자는 땅을 아낀다
        return Math.Clamp(s, 0f, 0.9f);
    }

    private void BuyPlot(int seller, int buyer)
    {
        var hh = Households;
        int id = PlotForSale(seller, buyer);
        if (id < 0) return;
        float price = Map.PlotValue(id) * BuyPremium;
        PayCoin(buyer, seller, price);   // 땅값은 은화
        _tiller.Remove(id);
        Map.GivePlot(id, buyer, hh);
        Stats.LandBought++;
    }

    // ------------------------------------------------------------------ 일자리

    /// <summary>지배자 lord 가 j(군인, 징수관)를 한 사람 더 쓸 마음이 있나(0~1).</summary>
    private float HireRoom(int lord, Job j)
    {
        int n = ClientCount(lord);
        if (n <= 0) return 0f;
        int have = RetainerCount(lord, j);
        int want;
        if (j == Job.Soldier)
        {
            float tribute = (lord < _incTribute.Length ? _incTribute[lord] : 0f) + IncomeOf(lord).rent;
            want = Math.Min(n / 2, (int)((tribute * 0.5f + 0.05f * MathF.Max(0f, Households.Food[lord])) / Config.SoldierWage));
        }
        else want = Math.Max(0, (int)MathF.Ceiling((n - DirectSpan(lord)) / (float)Config.CollectorSpanBase));
        return have < want ? 1f : have < want + 1 + n / 20 ? 0.35f : 0.05f;
    }

    private bool CanServe(int i, int lord, Job j)
    {
        var p = People; var hh = Households;
        if (!Adult(i) || p.JobOf[i] != 0 || _employer.ContainsKey(i) || !hh.IsAlive(lord)) return false;
        int h = p.Household[i];
        if (h == lord || ClientCount(h) > 0) return false;
        if (PatronOf(h) != lord && hh.Village[h] != hh.Village[lord]) return false;
        int age = AgeYears(i);
        return j == Job.Soldier ? p.Male[i] && age <= 40 : age >= 18 && age <= 55 && p.GetSkill(i, Skill.Letters) >= 0.2f;
    }

    private float ServeOdds(int i, int lord, Job j)
    {
        var p = People;
        float fit = j == Job.Soldier ? 0.5f * Skills.Talent(p, i, Skill.Arms) + 0.3f * p.GetSkill(i, Skill.Arms) : 0.6f * p.GetSkill(i, Skill.Letters) + 0.2f * p.Get(i, Trait.Charisma);
        int lh = HeadIdx(lord);
        float opin = lh >= 0 ? Social.Opinion(p, lh, p.Uid[i]) / 200f : 0f;
        return Math.Clamp(HireRoom(lord, j) * (0.4f + fit) + opin, 0.02f, 0.95f);
    }

    private void TakeService(int i, int lord, Job j)
    {
        var p = People;
        p.JobOf[i] = (byte)j;
        Lineage.SetJob(p.Uid[i], j);
        _employer[i] = lord;
        _employerVer++;
        if (j == Job.Soldier) Stats.NewSoldiers++; else Stats.NewCollectors++;
        Stats.Volunteered++;
    }

    // ------------------------------------------------------------------ 측근과 봉토

    /// <summary>i 가 직접 딸린, 갈래가 있는 맨 위 조직 가운데 아직 측근이 아닌 곳(우두머리가 target).</summary>
    private Group? InnerSeekOrg(int i, int target)
    {
        var p = People;
        foreach (var g in GroupsOf(i))
            if (IsTop(g) && g.LeaderUid == p.Uid[target] && g.LeaderUid != p.Uid[i] && HasChildren(g) && !g.Inner.Contains(p.Uid[i])) return g;
        // 지배자 군대의 갈래에 있는 군인은 군대 맨 위의 측근을 청할 수 있다
        foreach (var g in GroupsOf(i))
        {
            var r = RootOf(g);
            if (r.Home >= 0 && r.LeaderUid == p.Uid[target] && HasChildren(r) && !r.Inner.Contains(p.Uid[i])) return r;
        }
        return null;
    }

    private float InnerOdds(int i, int leader)
    {
        var p = People;
        float s = 0.15f + Social.Opinion(p, leader, p.Uid[i]) / 100f + 0.3f * FightPower(i) + 0.2f * p.GetSkill(i, Skill.Letters) + 0.1f * p.Get(i, Trait.Charisma);
        if (p.LoyalUid[i] == p.Uid[leader]) s += 0.2f;
        return Math.Clamp(s, 0.02f, 0.9f);
    }

    /// <summary>봉토는 섬긴 집만 청한다: 식구가 그 지배자의 군인·징수관이거나 그 군대의 측근이어야 한다.</summary>
    private bool CanPetitionFief(int i, int lord)
    {
        int h = People.Household[i];
        if (HeadIdx(h) != i || PatronOf(h) != lord || ClientCount(h) > 0 || ClientCount(lord) < 8 || HeadIdx(lord) < 0) return false;
        var org = LordOrg(lord, false);
        foreach (int m in MembersOf(h))
            if (_employer.GetValueOrDefault(m, -1) == lord || (org != null && org.Inner.Contains(People.Uid[m]))) return true;
        return false;
    }

    /// <summary>봉토를 내줄 확률: 지배자가 거느리기 벅찰수록, 청하는 집을 좋게 볼수록, 충성할수록. 야심 큰 사람은 꺼린다.</summary>
    private float FiefOdds(int i, int lord)
    {
        var p = People;
        int lh = HeadIdx(lord);
        float over = Math.Clamp((ClientCount(lord) - DirectSpan(lord)) / 20f, 0f, 1f);
        float opin = Social.Opinion(p, lh, p.Uid[i]) / 100f;
        float s = 0.05f + 0.4f * over + 0.4f * opin + (p.LoyalUid[i] == p.Uid[lh] ? 0.25f * p.Loyalty[i] : 0f) - 0.25f * (p.Get(i, Trait.Ambition) - 0.5f)
                  - 0.2f * HeadTrait(lord, Trait.Caution);
        return Math.Clamp(s, 0.02f, 0.85f);
    }

    // ------------------------------------------------------------------ NPC

    /// <summary>해마다 NPC가 같은 수를 쓴다: 넉넉한 야심가가 땅을 사고, 충성스러운 큰 집 가장이 봉토를 청한다.</summary>
    private void CareerYear()
    {
        var hh = Households; var p = People;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            int head = HeadIdx(h);
            if (head < 0 || IsPlayer(head)) continue;
            float amb = p.Get(head, Trait.Ambition);
            // 땅 사기: 두 해 먹을 것이 넘게 남는 야심가
            if (amb > 0.45f && Surplus(h) > 2f && _rng.Chance(0.15f * amb))
            {
                int seller = -1; float sw = 0f;
                foreach (int o in VillageHouses(hh.Village[h]))
                {
                    if (o == h || !hh.IsAlive(o) || hh.PlotCount[o] <= 1) continue;
                    float w = SellOdds(o, h);
                    if (w > sw) { sw = w; seller = o; }
                }
                int sh = seller >= 0 ? HeadIdx(seller) : -1;
                if (sh >= 0 && LandPrice(seller, h) is float pr && pr > 0f && WealthOf(h) - pr > hh.Need[h] * Config.DaysPerYear)
                    Interact(InteractionId.BuyLand, new InteractionContext { Actor = head, Target = sh, Third = -1 });
            }
            // 봉토 청하기: 지배자를 좋게 보는 야심가, 드물게
            int lord = PatronOf(h);
            if (lord >= 0 && amb > 0.6f && CanPetitionFief(head, lord) && _rng.Chance(0.02f) && HeadIdx(lord) is int lh && lh >= 0 && !IsPlayer(lh))
                Interact(InteractionId.PetitionFief, new InteractionContext { Actor = head, Target = lh, Third = -1 });
        }
    }

    private void AddCareerDefs(InteractionDef[] d)
    {
        int H(World w, int i) => w.People.Household[i];
        d[(int)InteractionId.BuyLand] = new InteractionDef
        {
            Id = InteractionId.BuyLand, Name = "땅 사기", Category = InteractionCategory.Rule,
            Can = (w, c) =>
            {
                int a = H(w, c.Actor), t = H(w, c.Target);
                if (w.HeadIdx(a) != c.Actor || w.HeadIdx(t) != c.Target || a == t || w.Households.PlotCount[t] <= 1) return false;
                if (w.Households.Village[a] != w.Households.Village[t] && w.PatronOf(a) != t) return false;
                float pr = w.LandPrice(t, a);
                return pr > 0f && w.WealthOf(a) >= pr;
            },
            Accept = (w, c) => w.SellOdds(H(w, c.Target), H(w, c.Actor)),
            Success = (w, c, r) =>
            {
                r.Amount = w.LandPrice(H(w, c.Target), H(w, c.Actor));
                w.BuyPlot(H(w, c.Target), H(w, c.Actor));
                w.RiseChron(c, x => $"{x}에게서 땅 한 필지를 식량 {r.Amount:0}에 샀다", x => $"땅 한 필지를 {x}에게 팔았다");
            },
            Fail = (w, c) => w.RiseChron(c, x => $"땅을 사려 했지만 {Subj(x)} 팔지 않았다", null),
        };
        d[(int)InteractionId.Apprentice] = new InteractionDef
        {
            // 내 집 아이(Third)를 일 가진 사람(Target)에게 맡긴다
            Id = InteractionId.Apprentice, Name = "도제로 보내기", Category = InteractionCategory.Family, NeedsThird = true, ThirdLabel = "누구를 맡길까",
            Can = (w, c) =>
            {
                var p = w.People;
                int j = c.Target >= 0 ? p.JobOf[c.Target] : 0;
                if (j is 0 or (int)Job.SexWorker || !w.Adult(c.Target) || H(w, c.Target) == H(w, c.Actor)) return false;
                if (c.Third < 0) return w.MembersOf(H(w, c.Actor)).Any(k => w.AgeYears(k) is >= 10 and <= 15 && p.Master[k].IsNone);
                return p.IsAliveIndex(c.Third) && H(w, c.Third) == H(w, c.Actor) && w.AgeYears(c.Third) is >= 10 and <= 15 && p.Master[c.Third].IsNone
                       && w.WealthOf(H(w, c.Actor)) >= ApprenticeFee;
            },
            Accept = (w, c) =>
            {
                var p = w.People;
                if (c.Third < 0) return 0.5f;
                var sk = Skills.SkillOf((Job)p.JobOf[c.Target]);
                bool busy = false;
                for (int i = 0; i < p.HighWater && !busy; i++) if (p.IsAliveIndex(i) && p.Master[i].Index == c.Target && !p.Master[i].IsNone) busy = true;
                return Math.Clamp((busy ? 0.05f : 0.35f) + 0.6f * Skills.Talent(p, c.Third, sk) + Social.Opinion(p, c.Target, p.Uid[c.Actor]) / 200f, 0.02f, 0.95f);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                int j = p.JobOf[c.Target];
                p.Master[c.Third] = p.IdAt(c.Target);
                w._apprenticeOf.Add(((long)p.Uid[c.Third] << 8) | (byte)j);
                w.PayCoin(H(w, c.Actor), H(w, c.Target), ApprenticeFee);
                w.Stats.Apprentices++;
                w.RiseChron(c, x => $"{Obj(Names.Full(w.Lineage, p.Uid[c.Third]))} {x}에게 도제로 보냈다", x => $"{Obj(Names.Full(w.Lineage, p.Uid[c.Third]))} 도제로 받았다");
            },
            Fail = (w, c) => w.RiseChron(c, x => $"{Subj(x)} 아이를 받지 않았다", null),
        };
        foreach (var (id, job, name) in new[] { (InteractionId.ServeSoldier, Job.Soldier, "군인 자원"), (InteractionId.ServeCollector, Job.Collector, "징수관 자원") })
        {
            var jj = job;
            d[(int)id] = new InteractionDef
            {
                // 사람(Actor)이 지배자 가장(Target)에게 일자리를 청한다
                Id = id, Name = name, Category = InteractionCategory.Rule,
                Can = (w, c) => w.HeadIdx(H(w, c.Target)) == c.Target && w.ClientCount(H(w, c.Target)) > 0 && w.CanServe(c.Actor, H(w, c.Target), jj),
                Accept = (w, c) => w.ServeOdds(c.Actor, H(w, c.Target), jj),
                Success = (w, c, _) =>
                {
                    w.TakeService(c.Actor, H(w, c.Target), jj);
                    w.RiseChron(c, x => $"{x}의 {(jj == Job.Soldier ? "군인" : "징수관")}이 되었다", null);
                },
                Fail = (w, c) => w.RiseChron(c, x => $"{x}에게 일자리를 청했지만 받아 주지 않았다", null),
            };
        }
        d[(int)InteractionId.AskInner] = new InteractionDef
        {
            Id = InteractionId.AskInner, Name = "측근 청하기", Category = InteractionCategory.Rule,
            Can = (w, c) => w.Adult(c.Actor) && w.InnerSeekOrg(c.Actor, c.Target) != null,
            Accept = (w, c) => w.InnerOdds(c.Actor, c.Target),
            Success = (w, c, _) =>
            {
                var g = w.InnerSeekOrg(c.Actor, c.Target)!;
                g.Pinned.Add(w.People.Uid[c.Actor]);
                g.Inner.Add(w.People.Uid[c.Actor]);
                w.Stats.InnerAsked++;
                w.RiseChron(c, x => $"{x}의 측근이 되었다({g.Name})", x => "측근을 하나 더 두었다");
            },
            Fail = (w, c) =>
            {
                Social.AddFrom(w.People, c.Target, c.Actor, -2f);
                w.RiseChron(c, x => $"{x}에게 측근 자리를 청했지만 거절당했다", null);
            },
        };
        d[(int)InteractionId.PetitionFief] = new InteractionDef
        {
            Id = InteractionId.PetitionFief, Name = "봉토 청하기", Category = InteractionCategory.Rule,
            Can = (w, c) => w.HeadIdx(H(w, c.Target)) == c.Target && w.CanPetitionFief(c.Actor, H(w, c.Target)),
            Accept = (w, c) => w.FiefOdds(c.Actor, H(w, c.Target)),
            Success = (w, c, r) =>
            {
                int n = w.Enfeoff(H(w, c.Target), H(w, c.Actor), -1, 5);
                r.Amount = n;
                w.Stats.FiefsAsked++;
            },
            Fail = (w, c) =>
            {
                // 거듭 청하면 욕심으로 본다
                Suspect0(w, H(w, c.Target), H(w, c.Actor));
                Social.AddFrom(w.People, c.Target, c.Actor, -8f);
                w.RiseChron(c, x => $"{x}에게 봉토를 청했지만 거절당했다", null);
            },
        };
        static void Suspect0(World w, int lord, int h) => w.Suspect(lord, h, 5f);
    }
}
