namespace People.Sim;

/// <summary>회칙: 누가 들어오나.</summary>
public enum JoinRule : byte { Anyone, Kin, Pious }

/// <summary>회칙: 번 것을 어떻게 나누나.</summary>
public enum ShareRule : byte { Leader, Even, Treasury }

/// <summary>
/// O2 일, 회칙, 체면, 단계별 열림.
/// - 조직의 성격은 종류가 아니라 맡은 일의 조합이다. 일마다 열리는 조건이 있다.
/// - 번 것은 회칙대로 우두머리 집, 조직원 집들, 조직 곳간 가운데로 간다.
/// - 높은 단계의 세력이 드러내 놓고 낮은 일(털기, 납치, 유곽)을 하면 체면을 잃는다.
/// </summary>
public sealed partial class World
{
    public static string JoinRuleName(JoinRule r) => r switch { JoinRule.Kin => "혈족만", JoinRule.Pious => "신앙 깊은 자만", _ => "아무나" };
    public static string ShareRuleName(ShareRule r) => r switch { ShareRule.Even => "고르게", ShareRule.Treasury => "곳간에 쌓음", _ => "우두머리가 다" };

    private readonly Dictionary<int, float> _sexTradeByVillage = new();   // 마을 → 한 해 하룻밤 거래 값(유곽 몫)
    private readonly List<(int village, float strength)> _mercGuard = new(); // 한 해 용병 계약(마을 방어에 더한다)

    // ------------------------------------------------------------------ 열림

    /// <summary>맨 위 조직 root 가 일 v 를 쓸 수 있나. 못 쓰면 까닭을 돌려준다.</summary>
    public bool CanUseVerb(Group root, Verb v, out string why)
    {
        why = "";
        var p = People;
        int leader = FindAlive(root.LeaderUid);
        int size = root.Home >= 0 ? BelowOf(root.Home) + root.Total : root.Total;
        switch (v)
        {
            case Verb.Guard: case Verb.Raid: case Verb.Charity: case Verb.Recruit: case Verb.Train: return true;
            case Verb.Produce:
                if (Subtree(root).SelectMany(MembersFast).Any(IsMaker)) return true;
                why = "만들 줄 아는 사람(공예·목공 0.2)이 있어야"; return false;
            case Verb.Survey:
                if (size >= 3) return true;
                why = "조직원 3명부터"; return false;
            case Verb.Teach:
                if (Subtree(root).SelectMany(MembersFast).Any(m => p.GetSkill(m, Skill.Letters) >= 0.4f || p.GetSkill(m, Skill.Arms) >= 0.4f || p.GetSkill(m, Skill.Trade) >= 0.4f || p.GetSkill(m, Skill.Craft) >= 0.4f)) return true;
                why = "가르칠 만한 사람(글·무예·장사·공예 0.4)이 있어야"; return false;
            case Verb.Chase:
                if (root.Home >= 0 || root.Kind == GroupKind.Watch) return true;
                why = "자경단이나 지배자 조직만"; return false;
            case Verb.Preach:
                if (leader >= 0 && People.Piety[leader] >= 0.6f) return true;
                why = "우두머리의 신앙이 깊어야(0.6)"; return false;
            case Verb.Kidnap:
                if (size >= 3) return true;
                why = "조직원 3명부터"; return false;
            case Verb.Collect:
                if (IsAttached(root)) { why = "지배자에게 붙은 조직은 걷지 못한다"; return false; }
                if (size >= 5) return true;
                why = "조직원 5명부터"; return false;
            case Verb.Trade:
                if (size < 5) { why = "조직원 5명부터"; return false; }
                if (Traders(root).Count == 0) { why = "장사꾼 조직원이 있어야"; return false; }
                return true;
            case Verb.Escort: case Verb.Mercenary:
                if (size >= 10) return true;
                why = "조직원 10명부터"; return false;
            case Verb.Agitate:
                if (root.Visibility > Visibility.Covert) { why = "숨긴 조직만(일부 은폐 이하)"; return false; }
                if (size < 10) { why = "조직원 10명부터"; return false; }
                return true;
            case Verb.Brothel:
                if (root.Village >= 0 && root.Village < VillageList.Count && VillageList.Households[root.Village] >= Config.TradeVillage) return true;
                why = $"{Config.TradeVillage}집 넘는 마을에서만"; return false;
            default:
                why = "아직 없는 일"; return false;
        }
    }

    private List<int> Traders(Group root)
    {
        var p = People;
        var list = new List<int>();
        foreach (var g in Subtree(root))
            foreach (int m in MembersFast(g))
                if (Adult(m) && (p.JobOf[m] == (byte)Job.Trader || p.GetSkill(m, Skill.Trade) >= 0.3f)) list.Add(m);
        return list;
    }

    private IEnumerable<Group> Subtree(Group root)
    {
        var stack = new Stack<Group>();
        stack.Push(root);
        int guard = 0;
        while (stack.Count > 0 && guard++ < 512)
        {
            var g = stack.Pop();
            yield return g;
            foreach (var c in ChildrenOf(g)) stack.Push(c);
        }
    }

    /// <summary>회칙에 맞는 사람인가(들기, 포섭, 설교로 들일 때).</summary>
    private bool RuleAllows(Group root, int i)
    {
        var p = People;
        switch (root.JoinRule)
        {
            case JoinRule.Kin:
                int leader = FindAlive(root.LeaderUid);
                if (leader < 0) return false;
                int chon = Lineage.Chon(p.Uid[leader], p.Uid[i]);
                return chon > 0 && chon <= 4;
            case JoinRule.Pious: return p.Piety[i] >= 0.6f;
            default: return true;
        }
    }

    // ------------------------------------------------------------------ 번 것 나누기

    /// <summary>조직이 번 것. 붙은 조직은 30%를 지배자에게 바치고 나머지를 나눈다.</summary>
    private void Earn(Group root, float amount)
    {
        if (amount <= 0f) return;
        root.Income += amount;
        Stats.OrgIncome += amount;
        float loss = ManageLoss(root);   // 큰 조직은 곳간지기 없이 새어 나간다
        if (loss > 0f) { Stats.ManageLost += amount * loss; amount *= 1f - loss; }
        if (IsAttached(root) && Households.IsAlive(root.Sanction))
        {
            float cut = amount * OfferCut;
            Households.Food[root.Sanction] += cut;
            amount -= cut;
        }
        Pay(root, amount);
    }

    /// <summary>훈련: 갈래 사람의 무예가 오른다(0.6까지. 더 깊은 것은 가르침으로).</summary>
    private void Drill(Group g, float w)
    {
        var p = People;
        foreach (int m in MembersFast(g))
        {
            float a = p.GetSkill(m, Skill.Arms);
            if (a < 0.6f) p.SetSkill(m, Skill.Arms, a + 0.06f * w * (0.6f - a) / 0.6f + 0.01f * w);
        }
        Stats.Trained++;
    }

    /// <summary>조직 몫을 회칙대로 우두머리 집, 조직원 집들, 곳간으로.</summary>
    private void Pay(Group root, float amount)
    {
        if (amount <= 0f) return;
        var hh = Households; var p = People;
        int leader = FindAlive(root.LeaderUid);
        int home = root.Home >= 0 ? root.Home : leader >= 0 ? p.Household[leader] : -1;
        switch (root.ShareRule)
        {
            case ShareRule.Treasury: root.Treasury += amount; return;
            case ShareRule.Even:
                var houses = Subtree(root).SelectMany(MembersFast).Select(m => p.Household[m]).Where(hh.IsAlive).Distinct().ToList();
                if (houses.Count == 0) break;
                foreach (int h in houses) hh.Food[h] += amount / houses.Count;
                return;
        }
        if (hh.IsAlive(home)) hh.Food[home] += amount;
    }

    // ------------------------------------------------------------------ 해마다 일

    /// <summary>
    /// 해마다 갈래마다 맡은 일을 한다(털기와 납치는 계절마다 따로). 힘 = 바로 딸린 사람 수 × 그 일의 무게.
    /// 그 뒤 회칙의 나눔이 충성에 미치고, 곳간이 굶는 조직원을 먹이고, 체면을 따진다.
    /// </summary>
    private void WorkYear()
    {
        _mercGuard.Clear();
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive || g.Tasks.Count == 0) continue;
            var root = RootOf(g);
            int leader = FindAlive(root.LeaderUid);
            if (leader < 0) continue;
            bool playerAuto = IsPlayer(leader) && !g.Manual;
            foreach (var t in g.Tasks.ToList())
            {
                // 플레이어 조직의 해를 끼치는 일은 플레이어가 직접 정했을 때만 한다
                if (playerAuto && t.Verb is Verb.Collect or Verb.Agitate) continue;
                float cf = WorkFactor(g) * g.CmdFactor;   // 가르치는 몫만큼, 지휘 범위를 넘은 만큼 덜 한다
                float effort = g.Members * t.Weight * cf * (g.FullTime ? 1.6f : 1f), strength = g.OwnStrength * t.Weight * cf;   // 상근은 일에 1.6배(힘은 이미 셌다)
                if (effort <= 0.1f || !CanUseVerb(root, t.Verb, out _)) continue;
                switch (t.Verb)
                {
                    case Verb.Collect: if (root.Home < 0) TakeProtection(root, g, t.Village, effort, strength); break;
                    case Verb.Guard: if (root.Home < 0 && root.Visibility >= Visibility.Public) SeekProtection(root, t.Village, effort); break;
                    case Verb.Preach: Preach(root, g, t.Village, effort, leader); break;
                    case Verb.Trade: Trade(root, g, t.Village, t.Weight); break;
                    case Verb.Escort: Escort(root, t.Village, effort); break;
                    case Verb.Mercenary: Hireout(root, g, effort, strength, leader); break;
                    case Verb.Brothel: BrothelCut(root, g, t.Village, t.Weight, leader); break;
                    case Verb.Agitate: Agitate(root, t.Village, effort); break;
                    case Verb.Recruit: for (int k = 0; k < Math.Min(6, (int)MathF.Ceiling(effort / 2f)); k++) RecruitOne(root, g); break;
                    case Verb.Train: Drill(g, t.Weight * cf * TrainBoost(g)); break;
                    case Verb.Produce: Produce(root, g, t.Weight * cf); break;
                }
            }
        }
        // 회칙의 나눔과 곳간, 체면
        var p = People; var hh = Households;
        foreach (var root in _groups.ToList())
        {
            if (!root.Alive || !IsTop(root)) continue;
            if (root.Income > 0f && root.Home < 0)
            {
                float d = root.ShareRule switch { ShareRule.Even => 2f, ShareRule.Leader => -1f, _ => 0f };
                if (d != 0f) foreach (int m in MembersFast(root).Take(30)) if (p.Uid[m] != root.LeaderUid) Social.Add(p, m, root.LeaderUid, d);
            }
            if (root.Treasury > 0f)
                foreach (int m in Subtree(root).SelectMany(MembersFast).Take(60))
                {
                    int h = p.Household[m];
                    if (!hh.IsAlive(h) || Surplus(h) >= 0.5f || root.Treasury < 5f) continue;
                    float give = MathF.Min(20f, root.Treasury);
                    root.Treasury -= give; hh.Food[h] += give;
                    Social.Add(p, m, root.LeaderUid, 3f);
                }
            root.LastIncome = root.Income;
            root.Income = 0f;
            Face(root);
        }
        _sexTradeByVillage.Clear();
    }

    /// <summary>체면: 무리를 넘은 세력이 드러내 놓고 털기·납치·유곽을 하면 우두머리 명예와 조직 권위가 깎인다.</summary>
    private void Face(Group root)
    {
        if (root.Visibility < Visibility.Neutral) return;
        int leader = FindAlive(root.LeaderUid);
        int home = root.Home >= 0 ? root.Home : leader >= 0 ? People.Household[leader] : -1;
        if (home < 0 || TierOf(home) < 2) return;
        bool low = Subtree(root).Any(g => g.Tasks.Any(t => t.Verb is Verb.Raid or Verb.Kidnap));   // 계획서 5.4: 털기와 납치만
        if (!low) return;
        if (leader >= 0) People.Honor[leader] -= 0.05f;
        root.Authority *= 0.9f;
        Stats.FaceLost++;
    }

    /// <summary>
    /// 보호를 바라는 집(계획서 3.1): 공개한 조직이 지키는 위험한 마을에서, 따르는 곳 없는 집이 스스로 찾아와
    /// 우두머리 집을 섬긴다. 위험할수록, 지키는 힘이 클수록 잘 온다.
    /// </summary>
    private void SeekProtection(Group root, int v, float effort)
    {
        var hh = Households;
        int leader = FindAlive(root.LeaderUid);
        if (leader < 0) return;
        int lh = People.Household[leader];
        float danger = DangerAt(v);
        if (danger < 0.3f) return;
        for (int k = 0; k < Math.Min(4, (int)MathF.Ceiling(effort / 4f)); k++)
        {
            int h = RandomHouseIn(v);
            if (h < 0 || h == lh || PatronOf(h) >= 0 || ClientCount(h) > 0 || WouldLoop(h, lh)) continue;
            if (!_rng.Chance(MathF.Min(0.5f, 0.15f * danger * MathF.Sqrt(effort)))) continue;
            _patron[h] = lh;
            _patronFounded[h] = hh.FoundedDay[lh];
            _clientsOf[lh] = _clientsOf.GetValueOrDefault(lh) + 1;
            HeadsRelate(h, lh, 10f, 0f, 10f, MemoryType.Protected, 10);
            Stats.ProtectionTaken++;
        }
    }

    /// <summary>보호세: 지켜 주지 않는 집(또는 힘없는 주군의 집)에 보호를 팔아 내 우두머리 집을 섬기게 한다. 공납은 기존 규칙대로 흐른다.</summary>
    private void TakeProtection(Group root, Group unit, int v, float effort, float strength)
    {
        var hh = Households; var p = People;
        int leader = FindAlive(root.LeaderUid);
        if (leader < 0) return;
        int lh = p.Household[leader];
        float est = Established(v);
        int tries = Math.Min(8, (int)MathF.Ceiling(effort / 2f));
        for (int k = 0; k < tries; k++)
        {
            int h = RandomHouseIn(v);
            if (h < 0 || h == lh || PatronOf(h) == lh || WouldLoop(h, lh) || ClientCount(h) > 0) continue;
            int cur = PatronOf(h);
            if (cur >= 0 && LordPower(cur) > 2f * strength) continue;   // 센 주군이 있는 집은 건드리지 않는다
            float accept = 0.15f + 0.4f * MathF.Min(1f, DangerAt(v)) + 0.3f * MathF.Min(1f, strength / (HouseholdPower(h) + 1f))
                           - (cur >= 0 ? 0.3f : 0f) - 0.2f * est;
            if (!_rng.Chance(Math.Clamp(accept, 0.02f, 0.9f))) continue;
            _patron[h] = lh;
            _patronFounded[h] = hh.FoundedDay[lh];
            // 따르는 집 수는 그 자리에서 고친다(집 전체를 다시 세지 않는다)
            if (cur >= 0) _clientsOf[cur] = Math.Max(0, _clientsOf.GetValueOrDefault(cur) - 1);
            _clientsOf[lh] = _clientsOf.GetValueOrDefault(lh) + 1;
            HeadsRelate(h, lh, 0f, 15f, 0f, MemoryType.Protected, 5);
            Stats.ProtectionTaken++;
        }
    }

    /// <summary>설교: 마을 사람의 신앙을 올리고, 깊어진 사람을 신도로 들인다. 넉넉한 신도 집은 헌금을 낸다.</summary>
    private void Preach(Group root, Group unit, int v, float effort, int leader)
    {
        var p = People; var hh = Households;
        float cha = p.Get(leader, Trait.Charisma);
        int n = Math.Min(30, (int)(3 * effort) + 2);
        for (int k = 0; k < n; k++)
        {
            int i = RandomVillager(v);
            if (i < 0) continue;
            p.Piety[i] = MathF.Min(1f, p.Piety[i] + 0.01f * (0.5f + cha));
            if (p.Piety[i] > 0.6f && !InGroup(i, root) && GroupsOf(i).Count() < GroupSlots && RuleAllows(root, i) && JoinFactor(root, i) > 0f && _rng.Chance(0.15f + 0.1f * AuthorityPull(i, root)))
                if (JoinGroup(i, root)) Stats.Converts++;
        }
        // 헌금은 이 갈래에 바로 딸린 신도에게서만 받는다(갈래마다 같은 신도에게 거두지 않게)
        float gift = 0f;
        foreach (int m in MembersFast(unit).Take(40))
        {
            int h = p.Household[m];
            if (!hh.IsAlive(h) || Surplus(h) < 1f) continue;
            float g = MathF.Min(10f, hh.Food[h] * 0.02f);
            hh.Food[h] -= g; gift += g;
            PayDues(h, g);
        }
        if (FacilityOf(root, FacilityKind.Chapel, v) != null) gift *= 1.3f;   // 예배당
        Earn(root, gift);
    }

    /// <summary>교역: 장사꾼 조직원마다 차익을 낸다. 길이 위험하면 털릴 수 있고, 호송이 막아 준다. 장사 솜씨가 는다.</summary>
    private void Trade(Group root, Group unit, int v, float weight)
    {
        var p = People;
        // 이 갈래에 바로 딸린 장사꾼만 짐을 진다(갈래마다 조직 전체 장사꾼을 세면 이익이 불어난다)
        var traders = MembersFast(unit).Where(m => Adult(m) && (p.JobOf[m] == (byte)Job.Trader || p.GetSkill(m, Skill.Trade) >= 0.3f)).ToList();
        if (traders.Count == 0) return;
        // 짐: 장사꾼 솜씨만큼. 이웃 마을과의 값 차이로 실제 물건을 사고팔아 번다
        float capacity = 0f;
        foreach (int m in traders.Take(20))
        {
            float s = p.GetSkill(m, Skill.Trade);
            capacity += (4f + 10f * s) * weight;
            p.SetSkill(m, Skill.Trade, s + 0.01f * (1f - s));
        }
        float profit = TradeGoods(v, capacity);
        if (FacilityOf(root, FacilityKind.Inn, v) != null) profit *= 1.1f;   // 여관
        float escort = Subtree(root).Where(g => g.Tasks.Any(t => t.Verb == Verb.Escort)).Sum(g => g.OwnStrength);
        float robbed = Math.Clamp(0.1f + 0.2f * DangerAt(v) - 0.05f * escort, 0f, 0.6f);
        if (_rng.Chance(robbed))
        {
            // 털린 몫은 그 마을을 터는 떼에게 간다(떼가 없으면 길에서 잃는다)
            float lost = profit * 0.5f;
            profit -= lost;
            Stats.TradeRobbed++;
            foreach (var b in _groups)
                if (b.Alive && b.Tasks.Any(t => t.Verb == Verb.Raid && t.Village == v)) { Earn(RootOf(b), lost); break; }
        }
        Stats.TradeProfit += profit;
        Earn(root, profit);
    }

    /// <summary>호송: 마을 장사꾼 집에서 삯을 받는다.</summary>
    private void Escort(Group root, int v, float effort)
    {
        var p = People; var hh = Households;
        float pay = 0f;
        int n = 0;
        foreach (int h in VillageHouses(v))
        {
            if (n >= (int)(effort * 2) + 1) break;
            if (!hh.IsAlive(h) || hh.Village[h] != v) continue;
            bool trader = false;
            foreach (int m in MembersOf(h)) if (p.JobOf[m] == (byte)Job.Trader) { trader = true; break; }
            if (!trader || hh.Food[h] < 20f) continue;
            hh.Food[h] -= 4f; pay += 4f; n++;
        }
        Stats.EscortPay += pay;
        Earn(root, pay);
    }

    /// <summary>용병: 위험한 땅의 넉넉한 지배자에게 싸움 힘을 빌려주고 삯을 받는다. 그 마을 방어에 힘이 더해진다.</summary>
    private void Hireout(Group root, Group unit, float effort, float strength, int leader)
    {
        var hh = Households; var p = People;
        int best = -1;
        float bs = 0f;
        int home = p.Household[leader];
        for (int k = 0; k < 40; k++)
        {
            int l = _rng.NextInt(hh.HighWater);
            if (!hh.IsAlive(l) || ClientCount(l) == 0 || l == home || hh.Village[l] < 0) continue;
            if (Dist2(l, home) > Config.VassalRange * Config.VassalRange) continue;
            float need = DangerAt(hh.Village[l]);
            if (need < 0.3f) continue;
            float sc = need * hh.Food[l];
            if (sc > bs) { bs = sc; best = l; }
        }
        if (best < 0) return;
        float wage = effort * Config.SoldierWage * 0.8f;
        if (hh.Food[best] < 2f * wage) return;
        hh.Food[best] -= wage;
        _mercGuard.Add((hh.Village[best], strength));
        HeadsRelate(best, home, 5f, 0f, 5f, MemoryType.Protected, 5);
        Stats.MercPay += wage;
        Stats.MercContracts++;
        Earn(root, wage);
    }

    /// <summary>유곽: 스스로 몸 파는 일을 하는 어른에게 손님을 잇고 보호하며, 그 마을 거래 값의 몫을 받는다. 강압은 없다.</summary>
    private void BrothelCut(Group root, Group unit, int v, float weight, int leader)
    {
        var p = People; var hh = Households;
        float value = _sexTradeByVillage.GetValueOrDefault(v);
        if (value <= 0f) return;
        float cut = 0.2f * value * MathF.Min(1f, weight * 2f);
        // 몫은 이 조직에 든 일꾼(스스로 든 어른)의 집에서만 나간다. 대신 보호받아 우두머리를 좋게 본다
        var workers = new List<int>();
        foreach (var g in Subtree(root))
            foreach (int m in MembersFast(g))
                if (p.JobOf[m] == (byte)Job.SexWorker && AgeYears(m) >= 16 && hh.IsAlive(p.Household[m]) && hh.Village[p.Household[m]] == v && !workers.Contains(m)) workers.Add(m);
        if (workers.Count == 0) return;
        foreach (int w in workers)
        {
            int h = p.Household[w];
            float take = MathF.Min(cut / workers.Count, MathF.Max(0f, hh.Food[h] * 0.3f));
            hh.Food[h] -= take;
            Social.Add(p, w, p.Uid[leader], 3f);
            Stats.BrothelCut += take;
            Earn(root, take);
        }
    }

    /// <summary>선동: 남의 세력의 마을에서 그 지배자를 보는 가장들의 눈을 깎는다. 하는 만큼 새어 나간다.</summary>
    private void Agitate(Group root, int v, float effort)
    {
        var hh = Households; var p = People;
        int n = Math.Min(12, (int)(2 * effort) + 1);
        for (int k = 0; k < n; k++)
        {
            int h = RandomHouseIn(v);
            if (h < 0) continue;
            int lord = PatronOf(h);
            int hd = HeadIdx(h), lh = HeadIdx(lord);
            if (lord < 0 || hd < 0 || lh < 0) continue;
            Social.Add(p, hd, p.Uid[lh], IsIdleScholar(hd) ? -6f : -4f);
            Stats.Agitations++;
        }
        root.Leak += 0.02f * MathF.Sqrt(effort);
    }

    /// <summary>포섭 일: 조직원이 아는 사람(남의 조직원도)을 하나 끌어들인다.</summary>
    private void RecruitOne(Group root, Group unit)
    {
        var p = People;
        var ms = MembersFast(unit);
        if (ms.Count == 0) return;
        int r = ms[_rng.NextInt(ms.Count)];
        if (!Adult(r)) return;
        int sl = r * PersonStore.RelSlots + _rng.NextInt(PersonStore.RelSlots);
        int uid = p.RelUid[sl];
        if (uid < 0 || p.RelAff[sl] < 10f) return;
        int t = FindAlive(uid);
        if (t < 0 || !Adult(t) || IsPlayer(t) || InGroup(t, root) || GroupOfKind(t, root.Kind) != null || GroupsOf(t).Count() >= GroupSlots || !RuleAllows(root, t)) return;
        float a = 0.2f + Social.Opinion(p, t, p.Uid[r]) / 100f + 0.2f * p.Get(t, Trait.Ambition) + (IsIdleScholar(t) ? 0.2f : 0f)   // 자리 없는 배운 사람은 잘 넘어온다
                + 0.2f * (AuthorityPull(t, root) - 0.5f);
        if (_rng.Chance(Math.Clamp(a, 0.02f, 0.9f)) && JoinGroup(t, unit)) Stats.SecretRecruits++;
    }

    // ------------------------------------------------------------------ 계절마다: 납치

    /// <summary>납치: 넉넉한 집의 식구를 잡아 몸값을 받는다. 못 받으면 풀어 주거나(거친 우두머리면 드물게) 해친다. 사건으로 남는다.</summary>
    private void KidnapSeason()
    {
        var p = People; var hh = Households;
        foreach (var g in _groups.ToList())
        {
            if (!g.Alive) continue;
            float w = 0f;
            foreach (var t in g.Tasks) if (t.Verb == Verb.Kidnap) w += t.Weight;
            if (w <= 0f || !_rng.Chance(0.2f * w)) continue;
            var root = RootOf(g);
            int leader = FindAlive(root.LeaderUid);
            if (leader < 0 || (IsPlayer(leader) && !g.Manual) || !CanUseVerb(root, Verb.Kidnap, out _)) continue;
            int home = p.Household[leader];
            // 근처에서 넉넉하고 지킴이 약한 집
            int target = -1;
            float bs = 0f;
            for (int k = 0; k < 24; k++)
            {
                int h = _rng.NextInt(hh.HighWater);
                if (!hh.IsAlive(h) || h == home || hh.Village[h] < 0 || hh.Food[h] < 60f || Dist2(h, home) > Config.RaidRange * Config.RaidRange) continue;
                float sc = hh.Food[h] / (1f + DefensePower(h));
                if (sc > bs) { bs = sc; target = h; }
            }
            if (target < 0) continue;
            var mem = MembersOf(target);
            if (mem.Count < 2) continue;
            int victim = mem.OrderBy(m => AgeYears(m)).First(m => m != hh.Head[target].Index);   // 어린 식구부터
            float atk = g.OwnStrength * WorkFactor(g) * (0.7f + 0.6f * _rng.NextFloat());   // 갈래가 통째로 나선다
            float def = DefensePower(target) * 0.5f * (0.7f + 0.6f * _rng.NextFloat()) + 0.2f;
            Stats.Kidnaps++;
            root.Leak += 0.15f;
            if (atk <= def) { Casualty(home, g.OwnStrength); continue; }
            int head = HeadIdx(target);
            float ransom = MathF.Min(hh.Food[target] * 0.3f, 80f);
            float pay = head >= 0 ? 0.5f + 0.3f * p.Get(head, Trait.Caution) + (AgeYears(victim) < 16 ? 0.2f : 0f) - 0.3f * p.Get(head, Trait.Stubbornness) : 0.5f;
            // 사건이 되기 전에 두목을 적어 둔다(그 자리에서 잡혀 처형될 수 있다)
            int leaderUid = p.Uid[leader];
            float aggr = p.Get(leader, Trait.Aggression);
            int victimUid = p.Uid[victim];
            var c = CommitCrime(CrimeType.Kidnap, leader, victim, ransom, false);
            // 두목이 잡혔으면 아이는 돌아오고 몸값은 없다
            if (c.Verdict is Verdict.Fine or Verdict.Flog or Verdict.Execute or Verdict.Pending) continue;
            bool victimAlive = p.IsAliveIndex(victim) && p.Uid[victim] == victimUid;
            if (_rng.Chance(Math.Clamp(pay, 0.1f, 0.95f)))
            {
                hh.Food[target] -= ransom;
                Stats.Ransoms++;
                Earn(root, ransom);
                if (head >= 0 && p.IsAliveIndex(head)) Social.Add(p, head, leaderUid, -40f, 30f);
            }
            else if (victimAlive && _rng.Chance(0.15f * aggr))
            {
                Stats.KidnapKills++;
                Die(victim, false);
            }
            else if (head >= 0 && p.IsAliveIndex(head)) Social.Add(p, head, leaderUid, -30f);
        }
    }

    // ------------------------------------------------------------------ AI가 고르는 일

    /// <summary>지배자 조직이 아닌 맨 위 조직의 할 일. 종류의 기본 일에 우두머리 성격과 처지가 더한다. 열리지 않은 일은 뺀다.</summary>
    /// <summary>마을 v 가 우두머리 집이 아닌 다른 지배자의 땅인가(선동할 곳).</summary>
    private bool OthersRealm(int v, int leader)
    {
        if (leader < 0) return false;
        int lh = People.Household[leader];
        var hs = VillageHouses(v);
        for (int k = 0; k < Math.Min(6, hs.Count); k++)
        {
            int l = PatronOf(hs[_rng.NextInt(hs.Count)]);
            if (l >= 0 && l != lh && !InTreeOf(l, lh)) return true;
        }
        return false;
    }

    private void KindNeeds(Group root, int leader, List<OrgTask> list)
    {
        var p = People;
        float caution = leader >= 0 ? p.Get(leader, Trait.Caution) : 0.5f, aggr = leader >= 0 ? p.Get(leader, Trait.Aggression) : 0.5f;
        float amb = leader >= 0 ? p.Get(leader, Trait.Ambition) : 0.5f, piety = leader >= 0 ? p.Piety[leader] : 0.5f;
        int v = root.Village;
        void Add(Verb vb, float w) { if (w > 0f && CanUseVerb(root, vb, out _)) list.Add(new OrgTask { Verb = vb, Village = v, Weight = w }); }
        switch (root.Kind)
        {
            case GroupKind.Watch:
                Add(Verb.Guard, (1f + DangerAt(v)) * (0.5f + caution));
                if (CrimeHeat(v) > 0.5f) Add(Verb.Chase, 0.4f * CrimeHeat(v) * (0.5f + aggr));
                if (aggr > 0.7f && leader >= 0 && Surplus(p.Household[leader]) < 0.5f) Add(Verb.Raid, 0.5f * aggr);
                break;
            case GroupKind.Bandits:
                Add(Verb.Raid, 0.5f + aggr);
                // 야심가 두목은 남의 세력 마을에서 선동하고, 숨긴 떼는 사람을 직접 끌어들인다
                if (root.Visibility <= Visibility.Covert) Add(Verb.Recruit, 0.3f);
                if (amb > 0.6f && OthersRealm(v, leader)) Add(Verb.Agitate, 0.5f * amb);
                Add(Verb.Kidnap, 0.2f + 0.6f * MathF.Max(0f, aggr - 0.4f));
                Add(Verb.Collect, amb > 0.5f ? amb : 0f);   // 야심가 두목은 보호세로 터를 잡는다
                break;
            case GroupKind.Brotherhood:
                Add(Verb.Charity, 0.5f + piety);
                Add(Verb.Preach, piety);
                // 숨긴 신도회(이단)는 지배자를 깎아내린다
                if (root.Visibility <= Visibility.Covert && OthersRealm(v, leader)) Add(Verb.Agitate, 0.4f * amb);
                break;
            case GroupKind.Company:
                Add(Verb.Trade, 1.5f);
                Add(Verb.Escort, 0.5f * caution);
                break;
            case GroupKind.Sellswords:
                Add(Verb.Mercenary, 1.5f);
                Add(Verb.Guard, 0.3f);
                if (DangerAt(v) > 0.4f) Add(Verb.Escort, 0.3f);
                Add(Verb.Collect, amb > 0.6f ? 0.6f * amb : 0f);
                break;
            case GroupKind.Brothel:
                Add(Verb.Brothel, 1.5f);
                Add(Verb.Guard, 0.3f);
                break;
            case GroupKind.School:
                Add(Verb.Teach, 1.5f);
                break;
            case GroupKind.Drill:
                Add(Verb.Train, 1.5f);
                Add(Verb.Guard, 0.4f);
                break;
            case GroupKind.Inspectors:
                Add(Verb.Survey, 1.5f);
                break;
            case GroupKind.Workshop:
                Add(Verb.Produce, 1.5f);
                break;
        }
        if (list.Count == 0) Add(Verb.Guard, 1f);
    }
}
