namespace People.Sim;

/// <summary>
/// 바탕 A: 관계, 기억, 평판, 비밀, 상호작용. 모든 사람이 같은 상호작용을 성격대로 쓴다.
/// 플레이어 집은 나중에(F) NPC 고르기 대신 플레이어 선택을 쓴다.
/// </summary>
public sealed partial class World
{
    private InteractionDef[]? _defs;
    private readonly List<Secret> _secrets = new();
    private readonly Dictionary<int, int> _lover = new();   // 몰래 만나는 상대(uid → uid). 들키면 끊긴다
    public IReadOnlyList<Secret> Secrets => _secrets;

    public InteractionDef Def(InteractionId id) => (_defs ??= BuildDefs())[(int)id];

    private bool Adult(int i) => People.IsAliveIndex(i) && AgeYears(i) >= 16;

    private int SpouseOf(int i)
    {
        var p = People;
        if (!p.Male[i]) return p.IsValid(p.Husband[i]) ? p.Husband[i].Index : -1;
        if (p.WifeCount[i] == 0) return -1;
        // 남자: 같은 집의 아내 중 하나
        for (int k = Households.IsAlive(p.Household[i]) ? Households.FirstMember[p.Household[i]] : -1; k >= 0; k = p.HhNext[k])
            if (!p.Male[k] && p.Husband[k].Index == i && p.IsValid(p.Husband[k])) return k;
        return -1;
    }

    private bool Married(int i) => People.Male[i] ? People.WifeCount[i] > 0 : People.IsValid(People.Husband[i]);

    /// <summary>
    /// 성 거래에서 사는 사람(buyer)이 파는 사람(seller)을 얼마로 치나. 사는 사람의 유전 선호로 외모·젊음·건강을 잰다.
    /// 남자가 팔면 외모가 아주 높을 때만 값이 생긴다.
    /// </summary>
    public float SexAppeal(int buyer, int seller)
    {
        var p = People;
        float looks = p.Get(seller, Trait.Looks);
        if (p.Male[seller]) return MathF.Max(0f, looks - Config.MaleAppealFloor) * 5f;
        int age = AgeYears(seller);
        float youth = age <= 22 ? 1f : age <= 35 ? 1f - (age - 22) / 26f : 0.3f;
        float wl = 0.6f + 0.6f * p.Get(buyer, Trait.PrefLooks);
        float v = wl * looks + 0.25f * youth + 0.15f * p.Get(seller, Trait.Constitution);
        return MathF.Max(0f, v - 0.25f);
    }

    public float SexPrice(int buyer, int seller)
    {
        int bh = People.Household[buyer];
        float wealth = Households.IsAlive(bh) ? MathF.Log10(1f + MathF.Max(0f, Households.Food[bh])) : 0f;
        return Config.SexTradeBase * SexAppeal(buyer, seller) * (0.5f + 0.4f * wealth);
    }

    private InteractionDef[] BuildDefs()
    {
        var d = new InteractionDef[(int)InteractionId.Count];
        d[(int)InteractionId.Chat] = new InteractionDef
        {
            Id = InteractionId.Chat, Name = "이야기", Category = InteractionCategory.Talk,
            Accept = (w, c) => Math.Clamp(0.8f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 200f, 0.1f, 1f),
            Success = (w, c, _) =>
            {
                var p = w.People;
                float k = 2f + 4f * p.Get(c.Actor, Trait.Sociability);
                Social.AddFrom(p, c.Actor, c.Target, k);
                Social.AddFrom(p, c.Target, c.Actor, k);
                w.Stats.Chats++;
            },
        };
        d[(int)InteractionId.Compliment] = new InteractionDef
        {
            Id = InteractionId.Compliment, Name = "칭찬", Category = InteractionCategory.Talk,
            Success = (w, c, _) =>
            {
                var p = w.People;
                // 눈썰미 좋은 사람은 빈말을 알아챈다
                float k = (4f + 8f * p.Get(c.Actor, Trait.Charisma)) * (1.2f - 0.6f * p.Get(c.Target, Trait.Perception));
                Social.AddFrom(p, c.Target, c.Actor, k);
                Social.Remember(p, c.Target, MemoryType.Compliment, p.Uid[c.Actor], w.Day, (int)k);
            },
        };
        d[(int)InteractionId.Gossip] = new InteractionDef
        {
            Id = InteractionId.Gossip, Name = "험담", Category = InteractionCategory.Talk,
            Can = (w, c) => c.Third >= 0 && w.People.IsAliveIndex(c.Third) && c.Third != c.Target,
            Accept = (w, c) => Math.Clamp(0.5f + Social.Opinion(w.People, c.Target, w.People.Uid[c.Actor]) / 100f, 0.05f, 0.95f),
            Success = (w, c, _) =>
            {
                var p = w.People;
                int third = p.Uid[c.Third];
                Social.Add(p, c.Target, third, -(4f + 10f * p.Get(c.Actor, Trait.Charisma)));
                Social.AddFrom(p, c.Target, c.Actor, 2f);
                w.SpreadRumor(c.Actor, c.Target, c.Third);
                w.Stats.Gossips++;
                // 험담은 돌고 돌아 당사자 귀에 들어간다
                if (w._rng.Chance(0.15f))
                {
                    Social.Add(p, c.Third, p.Uid[c.Actor], -20f);
                    Social.Remember(p, c.Third, MemoryType.Insulted, p.Uid[c.Actor], w.Day, -20);
                }
            },
        };
        d[(int)InteractionId.Gift] = new InteractionDef
        {
            Id = InteractionId.Gift, Name = "선물", Category = InteractionCategory.Talk,
            Can = (w, c) => w.Households.IsAlive(w.People.Household[c.Actor]) && w.Households.Food[w.People.Household[c.Actor]] > 20f,
            Success = (w, c, r) =>
            {
                var p = w.People;
                var hh = w.Households;
                int ah = p.Household[c.Actor], th = p.Household[c.Target];
                float amount = c.Amount > 0 ? c.Amount : MathF.Max(3f, hh.Food[ah] * 0.05f);
                amount = MathF.Min(amount, hh.Food[ah]);
                hh.Food[ah] -= amount;
                if (hh.IsAlive(th)) hh.Food[th] += amount;
                Social.AddFrom(p, c.Target, c.Actor, MathF.Min(15f, amount * 0.5f), 0f, MathF.Min(20f, amount * 0.5f));
                Social.Remember(p, c.Target, MemoryType.Gift, p.Uid[c.Actor], w.Day, (int)MathF.Min(30f, amount));
                w.Stats.Gifts++;
            },
        };
        d[(int)InteractionId.Affair] = new InteractionDef
        {
            Id = InteractionId.Affair, Name = "몰래 만나기", Category = InteractionCategory.Courtship, Secret = true, Sexual = true,
            Can = (w, c) => w.Adult(c.Actor) && w.Adult(c.Target) && w.People.Male[c.Actor] != w.People.Male[c.Target]
                            && (w.Married(c.Actor) || w.Married(c.Target)) && w.SpouseOf(c.Actor) != c.Target,
            Accept = (w, c) =>
            {
                var p = w.People;
                float want = w.MateScoreNoKin(c.Target, c.Actor);
                float a = 1f / (1f + MathF.Exp(-(want - Config.AffairAcceptMid) / 0.12f));
                int sp = w.SpouseOf(c.Target);
                if (sp >= 0)
                {
                    float love = Math.Clamp(Social.Opinion(p, c.Target, p.Uid[sp]) / 100f, -1f, 1f);
                    a *= (1f - 0.6f * p.Piety[c.Target]) * (0.8f - 0.6f * love);
                }
                a *= 1f - 0.5f * p.Get(c.Target, Trait.Caution);
                a += Social.Opinion(p, c.Target, p.Uid[c.Actor]) / 300f;
                return Math.Clamp(a, 0f, 0.9f);
            },
            Success = (w, c, r) =>
            {
                var p = w.People;
                Social.AddFrom(p, c.Actor, c.Target, 8f);
                Social.AddFrom(p, c.Target, c.Actor, 8f);
                Social.Remember(p, c.Actor, MemoryType.Affair, p.Uid[c.Target], w.Day, 15);
                Social.Remember(p, c.Target, MemoryType.Affair, p.Uid[c.Actor], w.Day, 15);
                w.AddSecret(SecretType.Affair, c.Actor, c.Target);
                w._lover[p.Uid[c.Actor]] = p.Uid[c.Target];
                w._lover[p.Uid[c.Target]] = p.Uid[c.Actor];
                r.Pregnant = w.SexualAct(c.Actor, c.Target, w.Config.AffairActs);   // 한 계절 동안 여러 번 만난 것으로 본다
                w.Stats.Affairs++;
            },
            Fail = (w, c) => Social.AddFrom(w.People, c.Target, c.Actor, -5f),
        };
        d[(int)InteractionId.SexTrade] = new InteractionDef
        {
            Id = InteractionId.SexTrade, Name = "하룻밤 거래", Category = InteractionCategory.Trade, Secret = true, Sexual = true,
            // Actor = 사는 사람, Target = 파는 사람
            Can = (w, c) => w.Adult(c.Actor) && w.Adult(c.Target) && w.People.Male[c.Actor] != w.People.Male[c.Target]
                            && w.SpouseOf(c.Actor) != c.Target && w.SexAppeal(c.Actor, c.Target) > 0.05f,
            Accept = (w, c) =>
            {
                // 파는 쪽: 형편이 어려울수록, 값이 클수록 받아들인다. 신앙심·혼인·평판 걱정이 막는다. 압박은 쓸 수 없다.
                var p = w.People;
                int th = p.Household[c.Target];
                float need = w.Households.IsAlive(th) ? Math.Clamp(1f - w.Surplus(th), 0f, 1f) : 1f;
                float price = c.Amount > 0 ? c.Amount : w.SexPrice(c.Actor, c.Target);
                float a = 0.05f + 0.7f * need + 0.2f * p.Get(c.Target, Trait.Ambition) - 0.35f * p.Piety[c.Target]
                        - (w.Married(c.Target) ? 0.25f : 0f) + price / 30f + 0.3f * p.Stigma[c.Target]
                        + (p.JobOf[c.Target] == (byte)Job.SexWorker ? 0.5f : 0f);   // 업으로 하는 사람
                return Math.Clamp(a, 0f, 0.95f);
            },
            Success = (w, c, r) =>
            {
                var p = w.People;
                var hh = w.Households;
                int ah = p.Household[c.Actor], th = p.Household[c.Target];
                float price = MathF.Min(c.Amount > 0 ? c.Amount : w.SexPrice(c.Actor, c.Target), hh.IsAlive(ah) ? hh.Food[ah] : 0f);
                if (hh.IsAlive(ah)) hh.Food[ah] -= price;
                if (hh.IsAlive(th)) hh.Food[th] += price;
                Social.Remember(p, c.Actor, MemoryType.SexTrade, p.Uid[c.Target], w.Day, 0);
                Social.Remember(p, c.Target, MemoryType.SexTrade, p.Uid[c.Actor], w.Day, 0);
                // 업으로 하는 사람과의 거래는 다들 아는 일이라 비밀이 아니다. 대신 파는 쪽에 낙인이 쌓인다.
                if (p.JobOf[c.Target] == (byte)Job.SexWorker) p.Stigma[c.Target] = MathF.Min(1f, p.Stigma[c.Target] + 0.02f);
                else w.AddSecret(SecretType.SexTrade, c.Actor, c.Target);
                r.Pregnant = w.SexualAct(c.Actor, c.Target, 1);
                w.Stats.SexTrades++;
                w.Stats.SexTradesTotal++;
                int sv = w.Households.IsAlive(th) ? w.Households.Village[th] : -1;
                if (sv >= 0 && w.VillageList.Households[sv] >= w.Config.TradeVillage) w.Stats.SexTradesBig++;
                w.Stats.SexTradeValue += price;
            },
        };
        AddTalkDefs(d);
        AddFamilyDefs(d);
        AddGroupDefs(d);
        AddOrgDefs(d);
        return d;
    }

    /// <summary>상호작용을 시도한다. NPC와 플레이어가 같은 길을 쓴다.</summary>
    public InteractionResult Interact(InteractionId id, InteractionContext c)
    {
        var def = Def(id);
        var r = new InteractionResult();
        if (!People.IsAliveIndex(c.Actor) || !People.IsAliveIndex(c.Target) || c.Actor == c.Target) return r;
        if (def.Sexual && (!Adult(c.Actor) || !Adult(c.Target))) return r;   // 어른끼리만
        if (!def.Can(this, c)) return r;
        if (_rng.Chance(def.Accept(this, c)))
        {
            r.Accepted = true;
            def.Success?.Invoke(this, c, r);
        }
        else def.Fail?.Invoke(this, c);
        return r;
    }

    /// <summary>성적 행위 한 번: 임신 판정. 족보 아버지는 여자의 남편(없으면 사생아).</summary>
    private bool SexualAct(int a, int b, int acts)
    {
        var p = People;
        int w = p.Male[a] ? b : a, m = p.Male[a] ? a : b;
        int husband = p.IsValid(p.Husband[w]) ? p.Husband[w].Index : -1;
        int legal = husband >= 0 ? p.Uid[husband] : -1;
        bool preg = false;
        for (int k = 0; k < acts && !preg; k++) preg = ActPregnancy(w, m, legal);
        if (preg && legal != p.Uid[m])
        {
            if (legal >= 0) AddSecret(SecretType.HiddenFather, w, m);
            _abortCheck.Add((w, p.Uid[w]));   // 혼인 밖에서 든 아이: 약초꾼을 찾을지 계절 끝에 정한다
        }
        return preg;
    }

    /// <summary>비밀을 만들거나(같은 둘 사이면) 다시 쓴다. 당사자는 안다.</summary>
    private void AddSecret(SecretType t, int a, int b)
    {
        var p = People;
        int ua = p.Uid[a], ub = p.Uid[b];
        // 혼인하지 않은 둘의 몰래 만남은 비밀로 두지 않는다(들켜도 혼인 이야기로 끝난다)
        if (t == SecretType.Affair && !Married(a) && !Married(b)) return;
        foreach (var s in _secrets)
            if (!s.Exposed && s.Type == t && ((s.A == ua && s.B == ub) || (s.A == ub && s.B == ua)) && t != SecretType.HiddenFather)
            {
                s.Day = Day;
                return;
            }
        var n = new Secret { Type = t, A = ua, B = ub, Day = Day };
        n.Known.Add(ua); n.Known.Add(ub);
        _secrets.Add(n);
    }

    /// <summary>숨은 아버지 비밀에 태어난 아이를 잇는다.</summary>
    private void LinkHiddenChild(int motherUid, int bioUid, int childUid)
    {
        for (int k = _secrets.Count - 1; k >= 0; k--)
        {
            var s = _secrets[k];
            if (s.Type == SecretType.HiddenFather && s.Child < 0 && s.A == motherUid && s.B == bioUid) { s.Child = childUid; return; }
        }
    }

    /// <summary>
    /// 해마다 비밀이 들킬지 굴린다. 배우자의 눈썰미, 동거인 수, 오래된 정도가 높인다.
    /// 숨은 아버지는 아이가 족보 아버지와 안 닮을수록(붉은 머리 등) 잘 들킨다.
    /// 들키면 배우자의 마음이 크게 떠나고, 당사자는 명예를 잃고, 몸을 판 쪽은 낙인이 남는다.
    /// </summary>
    private void DetectSecrets()
    {
        var p = People;
        var byUid = new Dictionary<int, int>();
        for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i)) byUid[p.Uid[i]] = i;
        for (int k = _secrets.Count - 1; k >= 0; k--)
        {
            var s = _secrets[k];
            bool aAlive = byUid.TryGetValue(s.A, out int a), bAlive = byUid.TryGetValue(s.B, out int b);
            if (s.Exposed || (!aAlive && !bAlive) || Day - s.Day > Config.SecretForgetYears * Config.DaysPerYear) { _secrets.RemoveAt(k); continue; }
            float risk = Config.SecretBaseRisk + MathF.Min(0.3f, 0.03f * MathF.Max(0, s.Known.Count - 2));   // 소문이 돌수록
            int victim = -1;
            // 함께 사는 사람이 눈치챈다
            if (aAlive && _rng.Chance(0.1f) && Households.IsAlive(p.Household[a]))
            {
                var mem = MembersOf(p.Household[a]);
                int w = mem.Count > 0 ? mem[_rng.NextInt(mem.Count)] : -1;
                if (w >= 0 && Adult(w) && !s.Known.Contains(p.Uid[w])) s.Known.Add(p.Uid[w]);
            }
            if (s.Type == SecretType.HiddenFather)
            {
                int legal = s.Child >= 0 ? Lineage.Father(s.Child) : -1;
                if (legal >= 0 && byUid.TryGetValue(legal, out int lf)) { victim = lf; risk += 0.3f * p.Get(lf, Trait.Perception) * Unlike(s.Child, byUid); }
                risk *= 0.5f;
            }
            else
            {
                int sa = aAlive ? SpouseOf(a) : -1, sb = bAlive ? SpouseOf(b) : -1;
                victim = sa >= 0 ? sa : sb;
                if (victim >= 0) risk += 0.25f * p.Get(victim, Trait.Perception);
                if (aAlive && Households.IsAlive(p.Household[a])) risk += 0.02f * Households.Members[p.Household[a]];
            }
            if (!_rng.Chance(risk)) continue;
            Expose(s, byUid, victim);
            _secrets.RemoveAt(k);
        }
    }

    /// <summary>아이가 족보 아버지와 얼마나 안 닮았나(0~1). 붉은 머리·금발이 둘 다에게 없는데 아이에게 있으면 크게.</summary>
    private float Unlike(int childUid, Dictionary<int, int> byUid)
    {
        if (!byUid.TryGetValue(childUid, out int c)) return 0.3f;
        int legal = Lineage.Father(childUid), mother = Lineage.Mother(childUid);
        if (!byUid.TryGetValue(legal, out int f)) return 0.3f;
        var p = People;
        float u = 0.3f;
        foreach (var m in new[] { Marker.RedHair, Marker.Blond })
        {
            bool cx = Genetics.Expressed(p.Genome(c), m, p.Male[c]);
            bool fx = Genetics.Expressed(p.Genome(f), m, true);
            bool mx = byUid.TryGetValue(mother, out int mo) && Genetics.Expressed(p.Genome(mo), m, false);
            if (cx && !fx && !mx) u += 0.5f;
        }
        u += MathF.Abs(p.Get(c, Trait.Height) - p.Get(f, Trait.Height));
        return MathF.Min(1f, u);
    }

    private void Expose(Secret s, Dictionary<int, int> byUid, int victim)
    {
        var p = People;
        s.Exposed = true;
        Stats.SecretsExposed++;
        foreach (int u in new[] { s.A, s.B })
            if (byUid.TryGetValue(u, out int who)) Chron(who, $"{Names.Full(Lineage, u)}의 비밀({SecretName(s.Type)})이 드러났다");
        if (s.Type == SecretType.Affair) { _lover.Remove(s.A); _lover.Remove(s.B); }
        byUid.TryGetValue(s.A, out int a);
        byUid.TryGetValue(s.B, out int b);
        bool aAlive = byUid.ContainsKey(s.A), bAlive = byUid.ContainsKey(s.B);
        if (victim >= 0)
        {
            // 속은 배우자는 갈라서려 할 수 있다(아래 기억을 먼저 남겨 까닭이 되게 한다)
            int cheat = s.Type == SecretType.HiddenFather ? a : (aAlive && SpouseOf(a) == victim ? a : bAlive && SpouseOf(b) == victim ? b : -1);
            Social.Remember(p, victim, s.Type == SecretType.HiddenFather ? MemoryType.Betrayed : MemoryType.SpouseAffair, s.A, Day, -70);
            if (cheat >= 0 && cheat != victim) _divorceCheck.Add((victim, cheat));
            Social.Add(p, victim, s.A, -50f);
            if (s.B != p.Uid[victim]) { Social.Add(p, victim, s.B, -40f); Social.Remember(p, victim, MemoryType.Betrayed, s.B, Day, -50); }
        }
        if (aAlive) { p.Honor[a] -= 0.2f; Social.Remember(p, a, MemoryType.Exposed, s.B, Day, -30); }
        if (bAlive) { p.Honor[b] -= s.Type == SecretType.SexTrade ? 0.05f : 0.2f; Social.Remember(p, b, MemoryType.Exposed, s.A, Day, -30); }
        // 몸을 판 쪽(B)은 낙인, 낙태한 여자(A)도 낙인
        if (s.Type == SecretType.SexTrade && bAlive) p.Stigma[b] = MathF.Min(1f, p.Stigma[b] + 0.3f);
        if (s.Type == SecretType.Abortion && aAlive) p.Stigma[a] = MathF.Min(1f, p.Stigma[a] + 0.25f);
        // 신앙심 깊은 가장은 집안 망신을 시킨 식구와 의절할 수 있다
        foreach (int k in new[] { aAlive ? a : -1, bAlive ? b : -1 })
            if (k >= 0 && Households.IsAlive(p.Household[k]) && Households.Head[p.Household[k]].Index != k && s.Type != SecretType.HiddenFather)
                _disownCheck.Add(k);
    }

    /// <summary>
    /// 계절마다 어른 일부가 상호작용을 하나씩 고른다. 상대는 아는 사람(관계 칸)이나 같은 마을 사람.
    /// 무엇을 할지는 성격과 처지로 정한다.
    /// </summary>
    private void SocialSeason()
    {
        var p = People;
        var hh = Households;
        var byVillage = new Dictionary<int, List<int>>();
        _uidIndex.Clear();
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!Adult(i)) continue;
            _uidIndex[p.Uid[i]] = i;
            int h = p.Household[i];
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            (byVillage.TryGetValue(hh.Village[h], out var l) ? l : byVillage[hh.Village[h]] = new()).Add(i);
        }
        Span<float> wts = stackalloc float[(int)InteractionId.Count];
        foreach (var (v, folk) in byVillage)
        {
            foreach (int i in folk)
            {
                if (IsPlayer(i) || !_rng.Chance(Config.SocialRate)) continue;
                // 몰래 만나는 사이면 자주 다시 만난다
                if (_lover.TryGetValue(p.Uid[i], out int lu) && _rng.Chance(0.5f))
                {
                    int lv = FindAlive(lu, v);
                    if (lv >= 0) { Interact(InteractionId.Affair, new InteractionContext { Actor = i, Target = lv, Third = -1 }); continue; }
                    _lover.Remove(p.Uid[i]);
                }
                // 형편이 어려운 사람은 스스로 살 사람을 찾는다(흉년에 늘어난다). 본인이 고르는 일이고, 사는 쪽도 받아들여야 한다.
                if (Adult(i) && TryOfferSelf(i, folk)) continue;
                // 상대: 절반은 아는 사람, 절반은 마을 사람
                int target = -1;
                if (_rng.NextBool())
                {
                    int sl = i * PersonStore.RelSlots + _rng.NextInt(PersonStore.RelSlots);
                    int uid = p.RelUid[sl];
                    if (uid >= 0) target = FindAlive(uid, v);
                }
                if (target < 0) target = folk[_rng.NextInt(folk.Count)];
                if (target == i) continue;
                float soc = p.Get(i, Trait.Sociability), opin = Social.Opinion(p, i, p.Uid[target]);
                bool opposite = p.Male[i] != p.Male[target];
                wts.Clear();
                wts[(int)InteractionId.Chat] = 2f + 3f * soc;
                wts[(int)InteractionId.Compliment] = 0.6f * (soc + p.Get(i, Trait.Charisma)) + (ClientCount(p.Household[target]) > 0 ? 1f : 0f);
                wts[(int)InteractionId.Gossip] = 0.8f * (1f - p.Piety[i]) * (0.5f + p.Get(i, Trait.Aggression));
                int ih = p.Household[i];
                wts[(int)InteractionId.Gift] = hh.Food[ih] > 30f && opin > 15f ? 0.6f * soc : 0f;
                wts[(int)InteractionId.Persuade] = 0.3f * p.Get(i, Trait.Charisma);
                wts[(int)InteractionId.Threaten] = opin < -20f ? 0.8f * p.Get(i, Trait.Aggression) * (1f - p.Piety[i]) : 0f;
                if (opposite)
                {
                    int sp = SpouseOf(i);
                    float cold = sp >= 0 ? Math.Clamp(1f - Social.Opinion(p, i, p.Uid[sp]) / 60f, 0f, 1f) : 0.5f;
                    float loose = (1f - p.Piety[i]) * (1f - 0.5f * p.Get(i, Trait.Caution));
                    float attract = Math.Clamp((MateScoreNoKin(i, target) - Config.AffairAttractFloor) * 2f, 0f, 1f);
                    wts[(int)InteractionId.Affair] = Config.AffairDrive * loose * cold * attract;
                    wts[(int)InteractionId.SexTrade] = hh.Food[ih] > 40f ? Config.SexTradeDrive * loose * (sp >= 0 ? cold : 1f) * SexAppeal(i, target) : 0f;
                }
                float tot = 0;
                for (int k = 0; k < wts.Length; k++) tot += wts[k];
                float roll = _rng.NextFloat() * tot;
                int pick = 0;
                for (; pick < wts.Length - 1; pick++) { roll -= wts[pick]; if (roll <= 0) break; }
                var ctx = new InteractionContext { Actor = i, Target = target, Third = -1 };
                if ((InteractionId)pick == InteractionId.Gossip)
                {
                    int st = _rng.NextBool() ? SecretTarget(i, v) : -1;
                    ctx.Third = st >= 0 && st != target ? st : Disliked(i, v);
                }
                if ((InteractionId)pick == InteractionId.Persuade) ctx.Third = Liked(i, v);
                // 하룻밤 거래: 몸 파는 사람이 있으면 그쪽을, 없으면 형편이 어려운 집 사람 중에서 찾는다
                if ((InteractionId)pick == InteractionId.SexTrade)
                {
                    int best = -1;
                    float bs = 0f;
                    for (int t = 0; t < 12; t++)
                    {
                        int c2 = folk[_rng.NextInt(folk.Count)];
                        if (p.Male[c2] == p.Male[i] || !hh.IsAlive(p.Household[c2])) continue;
                        if (p.JobOf[c2] == (byte)Job.SexWorker && SexAppeal(i, c2) > 0.05f) { best = c2; break; }
                        if (Surplus(p.Household[c2]) > 0.9f) continue;
                        float ap = SexAppeal(i, c2);
                        if (ap > bs) { bs = ap; best = c2; }
                    }
                    if (best < 0) continue;
                    ctx.Target = best;
                }
                Interact((InteractionId)pick, ctx);
            }
        }
        AbortChecks();
    }

    private readonly Dictionary<int, int> _uidIndex = new();
    private readonly List<int> _disownCheck = new();

    private bool TryOfferSelf(int i, List<int> folk)
    {
        var p = People; var hh = Households;
        int ih = p.Household[i];
        float need = hh.IsAlive(ih) ? Math.Clamp(1f - Surplus(ih), 0f, 1f) : 0f;
        bool worker = p.JobOf[i] == (byte)Job.SexWorker;
        if (!worker && need < 0.4f) return false;
        float drive = Config.SexOfferRate * (worker ? 1f + need : need) * (1f - p.Piety[i]) * (worker ? 3f : 1f) * (Married(i) ? 0.4f : 1f);
        if (!_rng.Chance(drive)) return false;
        int buyer = -1;
        float most = 60f;
        for (int t = 0; t < 12; t++)
        {
            int b = folk[_rng.NextInt(folk.Count)];
            if (p.Male[b] == p.Male[i] || b == i || SpouseOf(b) == i || !hh.IsAlive(p.Household[b]) || SexAppeal(b, i) < 0.05f) continue;
            if (hh.Food[p.Household[b]] > most) { most = hh.Food[p.Household[b]]; buyer = b; }
        }
        if (buyer < 0 || !_rng.Chance((1f - p.Piety[buyer]) * 0.6f * MathF.Min(1f, SexAppeal(buyer, i) * 2f))) return false;
        Interact(InteractionId.SexTrade, new InteractionContext { Actor = buyer, Target = i, Third = -1 });
        return true;
    }

    private void MaybeDisown(int k)
    {
        var p = People;
        if (!p.IsAliveIndex(k) || !Households.IsAlive(p.Household[k])) return;
        var head = Households.Head[p.Household[k]];
        if (!p.IsValid(head) || head.Index == k || IsPlayer(k)) return;
        int hi = head.Index;
        float want = 0.25f * p.Piety[hi] * (0.5f + p.Get(hi, Trait.Stubbornness));
        if (p.Piety[hi] > 0.6f && _rng.Chance(want)) Interact(InteractionId.Disown, new InteractionContext { Actor = hi, Target = k, Third = -1 });
    }

    private void AbortChecks()
    {
        foreach (var (w, u) in _abortCheck)
            if (People.IsAliveIndex(w) && People.Uid[w] == u && People.PregnantUntil[w] >= 0) MaybeAbort(w);
        _abortCheck.Clear();
    }

    /// <summary>uid 의 사람이 살아서 마을 v 에 있으면 그 번호(없으면 -1).</summary>
    private int FindAlive(int uid, int v)
    {
        if (!_uidIndex.TryGetValue(uid, out int k)) return -1;
        int h = People.Household[k];
        return Households.IsAlive(h) && Households.Village[h] == v ? k : -1;
    }

    /// <summary>i 가 가장 싫어하는, 같은 마을의 사람(없으면 -1).</summary>
    private int Disliked(int i, int v)
    {
        var p = People;
        int b = i * PersonStore.RelSlots, worst = -1;
        float w = -5f;
        for (int k = 0; k < PersonStore.RelSlots; k++)
        {
            if (p.RelUid[b + k] < 0 || p.RelAff[b + k] >= w) continue;
            int t = FindAlive(p.RelUid[b + k], v);
            if (t >= 0) { w = p.RelAff[b + k]; worst = t; }
        }
        return worst;
    }

    /// <summary>i 가 가장 좋아하는, 같은 마을의 사람(가족 제외, 없으면 -1).</summary>
    private int Liked(int i, int v)
    {
        var p = People;
        int b = i * PersonStore.RelSlots, best = -1;
        float w = 10f;
        for (int k = 0; k < PersonStore.RelSlots; k++)
        {
            if (p.RelUid[b + k] < 0 || p.RelAff[b + k] <= w) continue;
            int t = FindAlive(p.RelUid[b + k], v);
            if (t >= 0 && p.Household[t] != p.Household[i]) { w = p.RelAff[b + k]; best = t; }
        }
        return best;
    }

    private readonly List<(int victim, int spouse)> _divorceCheck = new();

    /// <summary>해마다: 관계가 흐려지고, 비밀이 들킬지 굴리고, 명예·악명이 조금씩 돌아온다.</summary>
    private void SocialYear()
    {
        var p = People;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            Social.Decay(p, i, Config.RelationKeep);
            p.Honor[i] *= 0.95f; p.Notoriety[i] *= 0.95f; p.Stigma[i] *= 0.97f;
        }
        LoyaltyYear();
        DetectSecrets();
        foreach (var (v, s) in _divorceCheck) MaybeDivorce(v, s);
        _divorceCheck.Clear();
        foreach (int k in _disownCheck) MaybeDisown(k);
        _disownCheck.Clear();
    }
}
