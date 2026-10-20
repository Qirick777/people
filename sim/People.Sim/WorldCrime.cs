namespace People.Sim;

public enum CrimeType : byte { Theft, Assault, Extortion, Murder, Embezzle, Raid, Kidnap }

/// <summary>사건의 끝. 아직 안 끝났으면 Open, 플레이어 판결을 기다리면 Pending.</summary>
public enum Verdict : byte { Open, Unreported, Escaped, Fine, Flog, Execute, Covered, Released, Pending, NoLord }

/// <summary>범죄 한 건. 사람은 uid 로 적는다.</summary>
public sealed class Crime
{
    public int Id;
    public CrimeType Type;
    public int Culprit, Victim, VictimHouse = -1, Village = -1, Day;
    public float Amount;
    public bool Attempt;                       // 실패한 시도
    public readonly List<int> Witnesses = new();   // 범인을 아는 사람(피해자 빼고)
    public bool VictimKnows;
    public int Lord = -1;                      // 맡은 지배자 집
    public bool Reported;
    public bool Counted;                       // 맡은 땅의 사건으로 이미 셌다(다시 열린 사건을 두 번 세지 않게)
    public Verdict Verdict;
}

/// <summary>
/// P4 범죄, 신고, 수사, 처벌.
/// - NPC는 압박이 성격 문턱을 넘을수록 저지른다(시그모이드, 정확한 조건 대신 확률).
/// - 범죄마다 목격자가 생기고, 범인을 알면 범인을 보는 눈이 나빠진다.
/// - 피해자와 목격자가 지배자를 믿으면 신고한다. 지배자는 조직의 쫓기 갈래, 경비, 증인으로 잡는다.
/// - 잡으면 벌하고, 혈족이나 신하면 덮을 수 있다. 작은 세력은 잡을 때 크게 얻고, 큰 세력은 놓칠 때 크게 잃는다.
/// </summary>
public sealed partial class World
{
    private readonly List<Crime> _crimes = new();          // 최근 사건(화면과 통계)
    private int _crimeIds;
    private readonly Dictionary<int, float> _catchRate = new(), _harshness = new();   // 지배자 집 → 잡는 비율, 벌의 세기(지난 사건들)
    private readonly Dictionary<int, float> _crimeHeat = new();
    private readonly Dictionary<int, int> _priors = new();          // 사람 uid → 벌받은 횟수(거듭하면 벌이 무거워진다)    // 마을 → 최근 범죄(해마다 반으로)
    private readonly Dictionary<int, (int committed, int crimes, int caught, int covered)> _lordLedger = new();   // 진단용: 맡은 땅에서 일어난 것, 신고된 것, 잡은 것, 덮은 것
    public readonly Dictionary<int, (int n, int caught, float opinion)> CrimeSizeLedger = new();   // 진단용: 세력 크기 구간 → 사건, 잡음, 피해자 가장의 눈 변화 합
    public IReadOnlyList<Crime> Crimes => _crimes;
    public IReadOnlyDictionary<int, (int committed, int crimes, int caught, int covered)> LordLedger => _lordLedger;

    // 플레이어가 지배자일 때
    public int PlayerSeverity = 1;                          // 0 너그러움, 1 보통, 2 엄함
    public bool PlayerJudgesAll;                            // 모든 사건을 직접 판결(아니면 혈족·신하·살인만)

    public static string CrimeName(CrimeType t) => t switch
    {
        CrimeType.Theft => "도둑질", CrimeType.Assault => "폭행", CrimeType.Extortion => "갈취", CrimeType.Murder => "살인",
        CrimeType.Embezzle => "공납 빼돌리기", CrimeType.Kidnap => "납치", _ => "약탈",
    };

    public static string VerdictName(Verdict v) => v switch
    {
        Verdict.Unreported => "신고 없음", Verdict.Escaped => "못 잡음", Verdict.Fine => "벌금", Verdict.Flog => "매질", Verdict.Execute => "처형",
        Verdict.Covered => "덮음", Verdict.Released => "풀어 줌", Verdict.Pending => "판결 기다림", Verdict.NoLord => "맡을 이 없음", _ => "",
    };

    private static float CrimeWeight(CrimeType t) => t switch
    {
        CrimeType.Murder => 2.5f, CrimeType.Kidnap => 1.8f, CrimeType.Raid => 1.5f, CrimeType.Assault => 1.3f, CrimeType.Embezzle => 0.5f, _ => 1f,
    };

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    // ------------------------------------------------------------------ 지배자의 법

    /// <summary>지배자가 벌을 얼마나 세게 내리는 사람인가(0~1). 플레이어 집은 고른 세기.</summary>
    public float Severity(int lord)
    {
        if (lord == PlayerHousehold && _player >= 0) return PlayerSeverity switch { 0 => 0.15f, 2 => 0.9f, _ => 0.5f };
        return Math.Clamp(0.45f + 1.4f * (HeadTrait(lord, Trait.Aggression) - 0.5f) - 0.6f * (HeadPiety(lord) - 0.5f) + 0.6f * (HeadTrait(lord, Trait.Stubbornness) - 0.5f), 0f, 1f);
    }

    private float HeadPiety(int h)
    {
        var head = Households.Head[h];
        return People.IsValid(head) ? People.Piety[head.Index] : 0.5f;
    }

    /// <summary>이 집 사람이 느끼는 처벌의 두려움: 주군이 잡는 비율 × (0.2 + 벌의 세기에 대한 평판).</summary>
    public float LawFear(int h)
    {
        int lord = Households.IsAlive(h) ? PatronOf(h) : -1;
        if (lord < 0) return 0.05f;
        LawOwner(lord);
        // 지배자의 성품은 소문으로 알려지고, 실제 판결이 그 소문을 고친다
        float sev = Severity(lord);
        float rep = 0.5f * sev + 0.5f * _harshness.GetValueOrDefault(lord, sev);
        return _catchRate.GetValueOrDefault(lord, 0.3f) * (0.1f + 1.4f * rep);   // 엄한 땅일수록 훨씬 두렵다
    }

    /// <summary>맡은 지배자: 피해자 집의 주군. 없으면 마을 자경단 우두머리의 집(-1이면 아무도 없다).</summary>
    private int ResponsibleLord(int victimHouse)
    {
        if (!Households.IsAlive(victimHouse)) return -1;
        int l = PatronOf(victimHouse);
        if (l >= 0) return l;
        int v = Households.Village[victimHouse];
        foreach (var g in _groups)
            if (g.Alive && g.Kind == GroupKind.Watch && g.Village == v && IsTop(g))
            {
                int leader = FindAlive(g.LeaderUid);
                if (leader >= 0 && Households.IsAlive(People.Household[leader])) return People.Household[leader];
            }
        return -1;
    }

    /// <summary>지배자가 마을 v 에서 범인을 쫓는 힘: 쫓기 갈래 + 지킴 갈래의 일부 + (작은 세력이면) 가장이 직접.</summary>
    private float PolicePower(int lord, int v)
    {
        float s = 0f;
        var root = LordOrg(lord, false);
        if (root != null)
        {
            void Walk(Group g, int d)
            {
                foreach (var t in g.Tasks)
                    if (t.Village == v) s += g.OwnStrength * t.Weight * (t.Verb == Verb.Chase ? 1f : t.Verb == Verb.Guard ? 0.3f : 0f);
                if (d < 8) foreach (var c in ChildrenOf(g)) Walk(c, d + 1);
            }
            Walk(root, 0);
        }
        // 지배자 조직이 아닌 자경단(지배자가 없는 마을)
        foreach (var g in _groups)
            if (g.Alive && g.Home < 0 && g.Village == v && (g.Kind == GroupKind.Watch))
                foreach (var t in g.Tasks) if (t.Village == v) s += g.OwnStrength * t.Weight * (t.Verb == Verb.Chase ? 0.8f : 0.3f);
        if (BelowOf(lord) < 20) s += 0.4f * HouseholdPower(lord);   // 작은 세력은 가장이 직접 쫓는다
        return s;
    }

    /// <summary>범인을 잡을 확률.</summary>
    private float CatchChance(Crime c, int culprit)
    {
        int lord = c.Lord;
        if (lord < 0) return 0f;
        float police = PolicePower(lord, c.Village);
        float pc = police / (police + 1.5f);
        bool known = c.VictimKnows || c.Witnesses.Count > 0;
        float ident = known ? 1f : 0.3f;
        // 관리 한계를 넘으면 잘 못 잡는다
        int span = DirectSpan(lord) + _collectorsOf.GetValueOrDefault(lord) * Config.CollectorSpanBase + 2 * _soldiersOf.GetValueOrDefault(lord);
        int n = Math.Max(1, ClientCount(lord));
        float over = n > span ? (float)span / n : 1f;
        float caution = culprit >= 0 ? People.Get(culprit, Trait.Caution) : 0.5f;
        float p = (0.15f + 0.65f * pc) * ident * over * (1.2f - 0.5f * caution) + 0.05f * MathF.Min(4, c.Witnesses.Count);
        return Math.Clamp(p, 0.02f, 0.95f);
    }

    // ------------------------------------------------------------------ 저지르기(NPC)

    /// <summary>
    /// 계절마다 어른마다 범죄 압박을 문턱과 견준다. 확률 = 기회 × 시그모이드((압박 − 문턱) / 0.08).
    /// 정한 범죄는 상호작용으로 한다(플레이어와 같은 길).
    /// </summary>
    private void CrimeSeason()
    {
        var p = People; var hh = Households;
        var byVillage = new Dictionary<int, List<int>>();
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && hh.Village[h] >= 0)
                (byVillage.TryGetValue(hh.Village[h], out var l) ? l : byVillage[hh.Village[h]] = new()).Add(h);
        _knownBy = new Dictionary<int, List<Secret>>();
        foreach (var s in _secrets)
            if (!s.Exposed)
                foreach (int ku in s.Known) (_knownBy.TryGetValue(ku, out var kl) ? kl : _knownBy[ku] = new()).Add(s);
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!Adult(i) || IsPlayer(i)) continue;
            int h = p.Household[i];
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            float fear = LawFear(h);
            float caution = p.Get(i, Trait.Caution), aggr = p.Get(i, Trait.Aggression), amb = p.Get(i, Trait.Ambition);
            float piety = p.Piety[i], stub = p.Get(i, Trait.Stubbornness);
            float bodyChance = p.Male[i] ? 1f : 0.3f;

            // 도둑질: 굶주림과 가난
            float hunger = Math.Clamp(1f - Surplus(h), 0f, 1f);
            var cls = ClassOf(h);
            float poor = cls is LandClass.Laborer or LandClass.Tenant or LandClass.None ? 0.15f : 0f;
            float theftP = MathF.Min(1f, hunger + poor);
            float theftT = 0.55f + 0.25f * caution + 0.2f * piety - 0.25f * amb + 0.7f * fear;
            if (_rng.Chance(0.25f * Sigmoid((theftP - theftT) / 0.08f)))
            {
                int t = TheftTarget(h, byVillage[hh.Village[h]]);
                if (t >= 0) { Interact(InteractionId.Steal, new InteractionContext { Actor = i, Target = t, Third = -1 }); continue; }
            }

            // 폭행·살인: 같은 마을의 가장 미운 사람
            var (enemy, grudge) = WorstEnemy(i, hh.Village[h]);
            if (enemy >= 0)
            {
                float murderT = 0.8f + 0.3f * piety + 0.2f * caution - 0.45f * aggr + 0.8f * fear;
                if (grudge > 0.6f && _rng.Chance(0.06f * bodyChance * Sigmoid((grudge - murderT) / 0.08f)))
                {
                    Interact(InteractionId.Murder, new InteractionContext { Actor = i, Target = enemy, Third = -1 });
                    continue;
                }
                float assaultT = 0.55f + 0.3f * piety + 0.15f * caution - 0.35f * aggr - 0.1f * stub + 0.6f * fear;
                if (_rng.Chance(0.15f * bodyChance * Sigmoid((grudge - assaultT) / 0.08f)))
                {
                    Interact(InteractionId.Assault, new InteractionContext { Actor = i, Target = enemy, Third = -1 });
                    continue;
                }
            }

            // 갈취: 남의 비밀을 안다
            int mark = ExtortTarget(i);
            if (mark >= 0)
            {
                float extP = 0.5f + 0.5f * amb;
                float extT = 0.55f + 0.25f * p.Honor[i] + 0.2f * piety - 0.25f * aggr + 0.6f * fear;
                if (_rng.Chance(0.15f * Sigmoid((extP - extT) / 0.08f)))
                    Interact(InteractionId.Extort, new InteractionContext { Actor = i, Target = mark, Third = -1 });
            }
        }
        _knownBy = null;
    }
    private Dictionary<int, List<Secret>>? _knownBy;

    /// <summary>같은 마을의 집 넷 가운데 남는 식량이 가장 많은 집의 가장.</summary>
    private int TheftTarget(int own, List<int> houses)
    {
        var hh = Households;
        int best = -1;
        float bs = 5f;
        int days = DaysToHarvest();
        for (int k = 0; k < 4; k++)
        {
            int t = houses[_rng.NextInt(houses.Count)];
            if (t == own || !People.IsValid(hh.Head[t])) continue;
            float spare = hh.Food[t] - hh.Need[t] * days;
            if (spare > bs) { bs = spare; best = t; }
        }
        return best >= 0 ? hh.Head[best].Index : -1;
    }

    /// <summary>i 가 가장 미워하는 같은 마을 어른과 미움(0~1).</summary>
    private (int who, float grudge) WorstEnemy(int i, int v)
    {
        var p = People;
        int b = i * PersonStore.RelSlots;
        int worst = -1;
        float wa = -30f;
        for (int k = 0; k < PersonStore.RelSlots; k++)
            if (p.RelUid[b + k] >= 0 && p.RelAff[b + k] < wa) { wa = p.RelAff[b + k]; worst = p.RelUid[b + k]; }
        if (worst < 0) return (-1, 0f);
        int t = FindAlive(worst, v);
        if (t < 0 || !Adult(t)) return (-1, 0f);
        return (t, MathF.Min(1f, -wa / 100f));
    }

    /// <summary>i 가 알고 있는 남의 비밀의 당사자(산 어른, 다른 집, 형편이 되는). 없으면 -1.</summary>
    public int ExtortTarget(int i)
    {
        var p = People;
        int u = p.Uid[i];
        // 계절 중에는 'uid → 아는 비밀' 목록을 쓴다(사람마다 비밀 전체를 훑지 않게)
        IEnumerable<Secret> known = _knownBy != null ? _knownBy.GetValueOrDefault(u) ?? (IEnumerable<Secret>)Array.Empty<Secret>() : _secrets;
        foreach (var s in known)
        {
            if (s.Exposed || s.A == u || s.B == u || !s.Known.Contains(u)) continue;
            int a = FindAlive(s.A);
            if (a >= 0 && Adult(a) && p.Household[a] != p.Household[i] && Households.IsAlive(p.Household[a]) && Households.Food[p.Household[a]] > 20f) return a;
        }
        return -1;
    }

    /// <summary>i 가 target 에 대해 아는 비밀이 있는가.</summary>
    public bool KnowsSecretOf(int i, int target)
    {
        int u = People.Uid[i], tu = People.Uid[target];
        foreach (var s in _secrets) if (!s.Exposed && (s.A == tu || s.B == tu) && s.A != u && s.B != u && s.Known.Contains(u)) return true;
        return false;
    }

    // ------------------------------------------------------------------ 상호작용(NPC와 플레이어가 같이 쓴다)

    /// <summary>훔칠 양: 대상 집의 남는 식량의 30%, 많아야 내 집 반 년치 + 5.</summary>
    public float TheftAmount(int actor, int target)
    {
        var hh = Households;
        int th = People.Household[target], ah = People.Household[actor];
        if (!hh.IsAlive(th)) return 0f;
        float spare = MathF.Max(0f, hh.Food[th] - hh.Need[th] * DaysToHarvest());
        return MathF.Min(spare * 0.3f, (hh.IsAlive(ah) ? hh.Need[ah] * Config.DaysPerYear * 0.5f : 20f) + 5f);
    }

    private void AddCrimeDefs(InteractionDef[] d)
    {
        bool Other(World w, InteractionContext c) => w.Adult(c.Actor) && w.Adult(c.Target) && w.People.Household[c.Actor] != w.People.Household[c.Target];
        d[(int)InteractionId.Steal] = new InteractionDef
        {
            Id = InteractionId.Steal, Name = "훔치기", Category = InteractionCategory.Scheme, Secret = true,
            Can = (w, c) => Other(w, c) && w.TheftAmount(c.Actor, c.Target) >= 3f,
            Accept = (w, c) => Math.Clamp(0.8f - 0.35f * w.People.Get(c.Target, Trait.Perception) + 0.15f * w.People.Get(c.Actor, Trait.Dexterity) - 0.05f * w.Households.Members[w.People.Household[c.Target]] / 2f, 0.1f, 0.9f),
            Success = (w, c, r) =>
            {
                float take = w.TheftAmount(c.Actor, c.Target);
                w.Households.Food[w.People.Household[c.Target]] -= take;
                w.Households.Food[w.People.Household[c.Actor]] += take;
                r.Amount = take;
                w.CommitCrime(CrimeType.Theft, c.Actor, c.Target, take, false);
            },
            Fail = (w, c) => w.CommitCrime(CrimeType.Theft, c.Actor, c.Target, 0f, true),
        };
        d[(int)InteractionId.Assault] = new InteractionDef
        {
            Id = InteractionId.Assault, Name = "때리기", Category = InteractionCategory.Scheme, Harsh = true,
            Can = (w, c) => Other(w, c),
            Accept = (w, c) =>
            {
                float a = w.FightPower(c.Actor) + 0.1f, b = w.FightPower(c.Target) + 0.1f;
                return Math.Clamp(a / (a + b), 0.1f, 0.9f);
            },
            Success = (w, c, _) =>
            {
                var p = w.People;
                Social.Add(p, c.Target, p.Uid[c.Actor], -25f, 15f);
                Social.Remember(p, c.Target, MemoryType.Assaulted, p.Uid[c.Actor], w.Day, -40);
                p.Honor[c.Target] -= 0.03f;
                w.Stats.Assaults++;
                bool dead = w._rng.Chance(0.05f);   // 의사도 약도 없어 매질 끝에 죽는 일이 잦다
                w.CommitCrime(dead ? CrimeType.Murder : CrimeType.Assault, c.Actor, c.Target, 0f, false, faceToFace: true);
                if (dead) w.Die(c.Target, false);
            },
            // 지면 내가 맞는다
            Fail = (w, c) =>
            {
                var p = w.People;
                Social.Add(p, c.Target, p.Uid[c.Actor], -20f);
                Social.Add(p, c.Actor, p.Uid[c.Target], -10f, 10f);
                w.CommitCrime(CrimeType.Assault, c.Actor, c.Target, 0f, true);
            },
        };
        d[(int)InteractionId.Extort] = new InteractionDef
        {
            Id = InteractionId.Extort, Name = "갈취", Category = InteractionCategory.Scheme, Harsh = true,
            Can = (w, c) => Other(w, c) && w.KnowsSecretOf(c.Actor, c.Target) && w.Households.Food[w.People.Household[c.Target]] > 10f,
            // 비밀이 무거울수록, 나를 두려워할수록 낸다. 명예를 아끼지 않는 사람은 버틴다
            Accept = (w, c) => Math.Clamp(0.4f + Social.Fear(w.People, c.Target, w.People.Uid[c.Actor]) / 150f + 0.3f * w.People.Get(c.Target, Trait.Caution)
                                          - 0.3f * w.People.Get(c.Target, Trait.Stubbornness), 0.1f, 0.9f),
            Success = (w, c, r) =>
            {
                var p = w.People; var hh = w.Households;
                int th = p.Household[c.Target];
                float take = MathF.Min(30f, hh.Food[th] * 0.2f);
                hh.Food[th] -= take;
                hh.Food[p.Household[c.Actor]] += take;
                r.Amount = take;
                Social.Add(p, c.Target, p.Uid[c.Actor], -30f, 25f);
                Social.Remember(p, c.Target, MemoryType.Threatened, p.Uid[c.Actor], w.Day, -40);
                w.CommitCrime(CrimeType.Extortion, c.Actor, c.Target, take, false);
            },
            // 거절당하면 그 사람이 신고할 수 있다
            Fail = (w, c) =>
            {
                Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -30f);
                w.CommitCrime(CrimeType.Extortion, c.Actor, c.Target, 0f, true);
            },
        };
        d[(int)InteractionId.Murder] = new InteractionDef
        {
            Id = InteractionId.Murder, Name = "해치기", Category = InteractionCategory.Scheme, Harsh = true, Secret = true,
            Can = (w, c) => Other(w, c),
            Accept = (w, c) =>
            {
                float a = w.FightPower(c.Actor) + 0.2f, b = w.FightPower(c.Target) + 0.1f;
                return Math.Clamp(0.25f + 0.5f * a / (a + b) - 0.2f * w.People.Get(c.Target, Trait.Perception), 0.05f, 0.85f);
            },
            Success = (w, c, _) =>
            {
                w.CommitCrime(CrimeType.Murder, c.Actor, c.Target, 0f, false);
                w.Die(c.Target, false);
            },
            Fail = (w, c) =>
            {
                Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -60f, 30f);
                w.CommitCrime(CrimeType.Assault, c.Actor, c.Target, 0f, true);
            },
        };
    }

    // ------------------------------------------------------------------ 사건: 목격, 신고, 수사, 처벌

    /// <summary>
    /// 범죄가 일어났다. 피해자가 알아보는지, 누가 봤는지 정하고, 신고와 수사를 한다.
    /// 피해자는 아직 살아 있을 때 부른다(살인도 죽기 전에).
    /// </summary>
    public Crime CommitCrime(CrimeType type, int culprit, int victim, float amount, bool attempt, bool faceToFace = false)
    {
        var p = People; var hh = Households;
        int vh = p.Household[victim];
        var c = new Crime
        {
            Id = ++_crimeIds, Type = type, Culprit = p.Uid[culprit], Victim = p.Uid[victim], VictimHouse = vh,
            Village = hh.IsAlive(vh) ? hh.Village[vh] : -1, Day = Day, Amount = amount, Attempt = attempt,
        };
        // 피해자가 범인을 알아보는가
        // 빼돌리기는 지배자가 알아챘을 때만 사건이 되므로 범인을 안다. 맞대고 한 일(폭행이 살인으로 번짐)도 안다
        float know = attempt || faceToFace ? 0.8f : type switch { CrimeType.Theft => 0.25f, CrimeType.Murder => 0f, CrimeType.Embezzle => 1f, _ => 1f };
        if (faceToFace) know = 1f;
        c.VictimKnows = _rng.Chance(know);
        // 목격자: 피해자 집 사람, 마을 사람 몇(갈취는 둘만 있을 때 한다)
        float see = type switch
        {
            CrimeType.Theft => 0.12f, CrimeType.Murder => 0.2f, CrimeType.Raid => 0.8f, CrimeType.Embezzle or CrimeType.Extortion => 0f, _ => 0.3f,
        };
        if (hh.IsAlive(vh) && see > 0f)
            foreach (int m in MembersOf(vh))
                if (m != victim && Adult(m) && _rng.Chance(see)) c.Witnesses.Add(p.Uid[m]);
        if (c.Village >= 0 && see > 0f)
            for (int k = 0; k < 3; k++)
            {
                int t = RandomVillager(c.Village);
                if (t >= 0 && t != culprit && t != victim && _rng.Chance(0.15f * p.Get(t, Trait.Perception) + 0.05f) && !c.Witnesses.Contains(p.Uid[t])) c.Witnesses.Add(p.Uid[t]);
            }
        // 범인을 아는 사람의 눈이 나빠진다. 피해자와 가까울수록 크게(범인이 그의 주군이면 충성 필터를 탄다)
        float w = CrimeWeight(type);
        if (c.VictimKnows) Social.Add(p, victim, c.Culprit, -20f * w, type is CrimeType.Assault or CrimeType.Extortion ? 10f : 0f);
        foreach (int u in c.Witnesses)
        {
            int k = FindAlive(u);
            if (k < 0) continue;
            float close = Math.Clamp(Social.Opinion(p, k, c.Victim) / 50f, 0f, 1f);
            Social.Add(p, k, c.Culprit, -8f * w * (1f + close));
        }
        // 아무도 모르면 범인만 아는 비밀이 된다
        if (!c.VictimKnows && c.Witnesses.Count == 0 && !attempt)
        {
            var s = new Secret { Type = SecretType.Crime, A = c.Culprit, B = c.Victim, Day = Day };
            s.Known.Add(c.Culprit);
            _secrets.Add(s);
        }
        Stats.Crimes++;
        CountCrime(type);
        if (c.Village >= 0) _crimeHeat[c.Village] = _crimeHeat.GetValueOrDefault(c.Village) + w;
        _crimes.Add(c);
        if (_crimes.Count > 3000) _crimes.RemoveRange(0, 1000);
        string vn = Names.Full(Lineage, c.Victim);
        Chron(culprit, $"{SubjName(culprit)} {DidText(type, vn, attempt)}");
        Chron(victim, $"{SubjName(victim)} {SufferText(type, attempt)}" + (c.VictimKnows ? $"(범인 {Names.Full(Lineage, c.Culprit)})" : ""));
        LastCrime = c;
        // 살해당한 사람은 신고하지 못한다
        ReportCrime(c, type == CrimeType.Murder && !attempt ? -1 : victim);
        return c;
    }

    /// <summary>가장 최근에 일어난 사건(화면이 행동 결과를 보여 줄 때).</summary>
    public Crime? LastCrime;

    /// <summary>범인 쪽 연대기 문장.</summary>
    public static string DidText(CrimeType t, string v, bool attempt) => (t, attempt) switch
    {
        (CrimeType.Theft, false) => $"{v}의 식량을 훔쳤다",
        (CrimeType.Theft, true) => $"{v}의 식량을 훔치려다 들켰다",
        (CrimeType.Assault, false) => $"{Obj(v)} 때렸다",
        (CrimeType.Assault, true) => $"{Obj(v)} 때리려다 오히려 얻어맞았다",
        (CrimeType.Extortion, false) => $"{v}에게서 식량을 갈취했다",
        (CrimeType.Extortion, true) => $"{v}에게서 식량을 갈취하려다 거절당했다",
        (CrimeType.Murder, false) => $"{Obj(v)} 해쳤다",
        (CrimeType.Murder, true) => $"{Obj(v)} 해치려다 실패했다",
        (CrimeType.Embezzle, _) => $"{v}의 공납을 빼돌렸다",
        (CrimeType.Raid, _) => $"{v}의 집을 털었다",
        _ => $"{Obj(v)} 납치했다",
    };

    /// <summary>피해자 쪽 연대기 문장.</summary>
    private static string SufferText(CrimeType t, bool attempt) => t switch
    {
        CrimeType.Theft => attempt ? "도둑을 막았다" : "도둑을 맞았다",
        CrimeType.Assault => attempt ? "덤벼든 사람을 물리쳤다" : "얻어맞았다",
        CrimeType.Extortion => attempt ? "갈취를 거절했다" : "갈취를 당했다",
        CrimeType.Murder => attempt ? "습격을 물리쳤다" : "습격을 받았다",
        CrimeType.Embezzle => "징수관이 공납을 빼돌린 것을 알아냈다",
        CrimeType.Raid => "약탈을 당했다",
        _ => "식구를 납치당했다",
    };

    private static string VerdictDone(Verdict v, string n) => v switch
    {
        Verdict.Fine => $"{n}에게 벌금을 물렸다", Verdict.Flog => $"{Obj(n)} 매질했다", Verdict.Execute => $"{Obj(n)} 처형했다",
        Verdict.Covered => $"{n}의 일을 덮어 주었다", _ => $"{Obj(n)} 풀어 주었다",
    };

    // 지배자 집마다 쌓는 기록(잡는 비율, 벌의 세기, 사건 장부, 경고)이 그 집 것인지(집 번호를 다시 쓰면 비운다)
    private readonly Dictionary<int, int> _lawOwner = new();

    private void LawOwner(int h)
    {
        if (h < 0 || !Households.IsAlive(h)) return;
        int f = Households.FoundedDay[h];
        if (_lawOwner.TryGetValue(h, out int o) && o == f) return;
        _catchRate.Remove(h); _harshness.Remove(h); _lordLedger.Remove(h); _restrained.Remove(h);
        _lawOwner[h] = f;
    }

    private void CountCrime(CrimeType t)
    {
        switch (t)
        {
            case CrimeType.Theft: Stats.Thefts++; break;
            case CrimeType.Extortion: Stats.Extortions++; break;
            case CrimeType.Murder: Stats.Murders++; break;
            case CrimeType.Embezzle: Stats.Embezzles++; break;
            case CrimeType.Raid: Stats.RaidCrimes++; break;
        }
    }

    private int RandomVillager(int v)
    {
        for (int k = 0; k < 6; k++)
        {
            int h = RandomHouseIn(v);
            if (h < 0) return -1;
            var mem = MembersOf(h);
            if (mem.Count == 0) continue;
            int i = mem[_rng.NextInt(mem.Count)];
            if (Adult(i)) return i;
        }
        return -1;
    }

    // 마을 → 집 목록. 하루에 한 번 다시 만든다(그날 끊긴 집은 고를 때 거른다)
    private List<int>[]? _villageHouses;
    private int _villageHousesDay = -1;

    /// <summary>마을 v 의 집 목록(오늘 아침 기준, 고른 뒤 살아 있는지 다시 본다).</summary>
    private List<int> VillageHouses(int v)
    {
        var hh = Households;
        if (_villageHouses == null || _villageHousesDay != Day || _villageHouses.Length < VillageList.Count)
        {
            if (_villageHouses == null || _villageHouses.Length < VillageList.Count) _villageHouses = new List<int>[VillageList.Count + 16];
            foreach (var l in _villageHouses) l?.Clear();
            for (int h = 0; h < hh.HighWater; h++)
            {
                if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
                (_villageHouses[hh.Village[h]] ??= new()).Add(h);
            }
            _villageHousesDay = Day;
        }
        return v >= 0 && v < _villageHouses.Length && _villageHouses[v] is { } l2 ? l2 : _noHouses;
    }
    private static readonly List<int> _noHouses = new();

    /// <summary>마을 v 의 살아 있는 집 하나(없으면 -1).</summary>
    private int RandomHouseIn(int v)
    {
        var hh = Households;
        var list = VillageHouses(v);
        if (list.Count == 0) return -1;
        for (int k = 0; k < 4; k++)
        {
            int h = list[_rng.NextInt(list.Count)];
            if (hh.IsAlive(h) && hh.Village[h] == v) return h;
        }
        return -1;
    }

    /// <summary>
    /// 신고: 피해자 집 가장, 피해자, 목격자 가운데 누구라도 지배자를 믿으면 알린다. 범인이 무서우면 덜 알린다.
    /// 신고되면 바로 수사한다.
    /// </summary>
    private void ReportCrime(Crime c, int victimIdx)
    {
        var p = People; var hh = Households;
        c.Lord = c.Type == CrimeType.Embezzle ? c.VictimHouse : ResponsibleLord(c.VictimHouse);
        if (c.Lord < 0) { c.Verdict = Verdict.NoLord; return; }
        LawOwner(c.Lord);
        if (!c.Counted)
        {
            var lg = _lordLedger.GetValueOrDefault(c.Lord);
            lg.committed++;
            _lordLedger[c.Lord] = lg;
            c.Counted = true;
        }
        int ci = FindAlive(c.Culprit);
        if (ci >= 0 && p.Household[ci] == c.Lord)
        {
            // 지배자 집 사람의 짓: 신고받을 사람이 범인 쪽이다. 아는 피해자는 덮였다고 여긴다
            c.Verdict = Verdict.Covered;
            if (c.VictimKnows) CrimeLoyalty(c, false, true);
            return;
        }
        var reporters = new List<int>();
        if (victimIdx >= 0 && p.IsAliveIndex(victimIdx)) reporters.Add(victimIdx);
        if (hh.IsAlive(c.VictimHouse) && p.IsValid(hh.Head[c.VictimHouse])) reporters.Add(hh.Head[c.VictimHouse].Index);
        foreach (int u in c.Witnesses) { int k = FindAlive(u); if (k >= 0) reporters.Add(k); }
        int lordHead = p.IsValid(hh.Head[c.Lord]) ? hh.Head[c.Lord].Index : -1;
        var lawOrg = LordOrg(c.Lord, false);
        foreach (int r in reporters)
        {
            if (r == lordHead) { c.Reported = true; break; }   // 지배자 집이 당했으면 스스로 안다
            float trust = p.LoyalUid[r] >= 0 && lordHead >= 0 && p.LoyalUid[r] == p.Uid[lordHead] ? p.Loyalty[r] : 0.35f;
            float fear = Social.Fear(p, r, c.Culprit) / 100f;
            // 맡은 지배자가 센 줄 알수록 신고한다(잡아 줄 거라 믿는다)
            float pull = lawOrg != null ? AuthorityPull(r, lawOrg) : 0.5f;
            float pr = 0.25f + 0.6f * trust - 0.5f * fear + (c.Type == CrimeType.Murder ? 0.4f : 0f) + 0.2f * (pull - 0.5f);
            if (_rng.Chance(Math.Clamp(pr, 0.05f, 0.95f))) { c.Reported = true; break; }
        }
        if (!c.Reported) { c.Verdict = Verdict.Unreported; return; }
        Stats.Reported++;
        Investigate(c);
    }

    /// <summary>수사: 잡으면 벌하거나 덮고, 못 잡으면 놓친다. 플레이어가 지배자면 판결을 기다리는 사건이 될 수 있다.</summary>
    private void Investigate(Crime c)
    {
        var p = People;
        int culprit = FindAlive(c.Culprit);
        bool caught = culprit >= 0 && _rng.Chance(CatchChance(c, culprit));
        var led = _lordLedger.GetValueOrDefault(c.Lord);
        led.crimes++;
        _catchRate[c.Lord] = 0.85f * _catchRate.GetValueOrDefault(c.Lord, 0.3f) + 0.15f * (caught ? 1f : 0f);
        if (!caught)
        {
            c.Verdict = Verdict.Escaped;
            Stats.Escaped++;
            _lordLedger[c.Lord] = led;
            CrimeLoyalty(c, false, false);
            return;
        }
        led.caught++;
        _lordLedger[c.Lord] = led;
        Stats.Caught++;
        Interrogated(culprit);   // 잡힌 범인이 든 숨긴 조직의 비밀이 새어 나간다
        bool close = CloseToLord(culprit, c.Lord) && c.Type != CrimeType.Embezzle;   // 빼돌리기는 지배자 자신이 당한 일이라 덮을 것이 없다
        // 플레이어가 직접 판결할 사건: 모두 고른 경우, 혈족(3촌 안, 같은 집), 살인. 부리는 사람의 일은 정한 세기로 판결한다
        if (c.Lord == PlayerHousehold && _player >= 0 && (PlayerJudgesAll || KinOfLord(culprit, c.Lord) || c.Type == CrimeType.Murder))
        {
            c.Verdict = Verdict.Pending;
            _fork = true;
            Chronicle.Add((Day, $"{CrimeName(c.Type)} 사건의 범인 {Obj(Names.Full(Lineage, c.Culprit))} 잡았다. 판결을 기다린다"));
            return;
        }
        Judge(c, ChooseVerdict(c, culprit, close));
    }

    /// <summary>범인이 지배자의 3촌 안 혈족이거나 그 집 사람인가.</summary>
    private bool KinOfLord(int culprit, int lord)
    {
        var p = People; var hh = Households;
        if (p.Household[culprit] == lord) return true;
        if (!p.IsValid(hh.Head[lord])) return false;
        int chon = Lineage.Chon(p.Uid[hh.Head[lord].Index], p.Uid[culprit]);
        return chon > 0 && chon <= 3;
    }

    /// <summary>판결 기다리는 사건의 범인을 플레이어가 덮어 줄 수 있나(혈족이나 부리는 사람, 빼돌리기는 빼고).</summary>
    public bool CanCover(int crimeId)
    {
        var c = CrimeById(crimeId);
        if (c == null || c.Type == CrimeType.Embezzle) return false;
        int culprit = FindAlive(c.Culprit);
        return culprit >= 0 && CloseToLord(culprit, c.Lord);
    }

    /// <summary>범인이 지배자의 3촌 안 혈족이거나 그 집 사람이거나 그가 부리는 사람인가.</summary>
    private bool CloseToLord(int culprit, int lord)
    {
        var p = People; var hh = Households;
        if (p.Household[culprit] == lord) return true;
        if (_employer.TryGetValue(culprit, out int l) && l == lord) return true;
        if (!p.IsValid(hh.Head[lord])) return false;
        int chon = Lineage.Chon(p.Uid[hh.Head[lord].Index], p.Uid[culprit]);
        return chon > 0 && chon <= 3;
    }

    /// <summary>NPC 지배자(또는 플레이어의 정한 세기)가 벌을 고른다. 가까운 사람이면 덮기도 한다.</summary>
    private Verdict ChooseVerdict(Crime c, int culprit, bool close)
    {
        float sev = Severity(c.Lord);
        bool player = c.Lord == PlayerHousehold && _player >= 0;
        if (close && !player)
        {
            float cover = 0.35f + 0.4f * HeadTrait(c.Lord, Trait.Ambition) - 0.3f * HeadPiety(c.Lord);
            if (_rng.Chance(Math.Clamp(cover, 0.05f, 0.9f))) return Verdict.Covered;
        }
        float w = CrimeWeight(c.Type) * (c.Attempt ? 0.5f : 1f) * (1f + 0.5f * MathF.Min(3, _priors.GetValueOrDefault(c.Culprit)));
        float x = sev * w + 0.15f * (_rng.NextFloat() - 0.5f);
        return x > 1.2f ? Verdict.Execute : x > 0.45f ? Verdict.Flog : Verdict.Fine;
    }

    /// <summary>판결을 집행한다.</summary>
    public void Judge(Crime c, Verdict v)
    {
        var p = People; var hh = Households;
        LawOwner(c.Lord);
        int culprit = FindAlive(c.Culprit);
        c.Verdict = v;
        int lordHead = hh.IsAlive(c.Lord) && p.IsValid(hh.Head[c.Lord]) ? hh.Head[c.Lord].Index : -1;
        if (culprit < 0 && v is Verdict.Fine or Verdict.Flog or Verdict.Execute) { c.Verdict = Verdict.Escaped; return; }
        switch (v)
        {
            case Verdict.Fine:
            {
                int ch = p.Household[culprit];
                float fine = MathF.Min(MathF.Max(0f, hh.Food[ch]) * 0.3f, c.Amount * 2f + 10f);
                hh.Food[ch] -= fine;
                if (hh.IsAlive(c.VictimHouse)) hh.Food[c.VictimHouse] += fine * 0.5f;
                if (hh.IsAlive(c.Lord)) hh.Food[c.Lord] += fine * 0.5f;
                Stats.Fines++;
                break;
            }
            case Verdict.Flog:
                p.Honor[culprit] -= 0.2f;
                if (lordHead >= 0) Social.Add(p, culprit, p.Uid[lordHead], -20f, 20f);
                Social.Remember(p, culprit, MemoryType.Punished, lordHead >= 0 ? p.Uid[lordHead] : -1, Day, -50);
                Stats.Floggings++;
                if (_rng.Chance(0.02f))
                {
                    Chron(culprit, $"{SubjName(culprit)} {Ro(CrimeName(c.Type))} 매를 맞다 죽었다");
                    Die(culprit, false);
                }
                break;
            case Verdict.Execute:
                // 범인 집 사람들은 지배자를 원망한다
                if (lordHead >= 0 && hh.IsAlive(p.Household[culprit]))
                    foreach (int m in MembersOf(p.Household[culprit])) if (m != culprit) Social.Add(p, m, p.Uid[lordHead], -40f, 20f);
                Stats.Executions++;
                Chron(culprit, $"{SubjName(culprit)} {Ro(CrimeName(c.Type))} 처형되었다");
                Die(culprit, false);
                break;
            case Verdict.Covered:
            {
                // 덮은 일은 비밀이 된다. 피해자가 범인을 알면 덮인 것도 안다
                var s = new Secret { Type = SecretType.CoverUp, A = lordHead >= 0 ? p.Uid[lordHead] : c.Culprit, B = c.Culprit, Day = Day };
                s.Known.Add(s.A); s.Known.Add(c.Culprit);
                if (c.VictimKnows) s.Known.Add(c.Victim);
                _secrets.Add(s);
                var led = _lordLedger.GetValueOrDefault(c.Lord);
                led.covered++;
                _lordLedger[c.Lord] = led;
                Stats.Covered++;
                break;
            }
        }
        float harsh = v switch { Verdict.Execute => 1f, Verdict.Flog => 0.6f, Verdict.Fine => 0.3f, _ => 0f };
        _harshness[c.Lord] = 0.85f * _harshness.GetValueOrDefault(c.Lord, Severity(c.Lord)) + 0.15f * harsh;
        bool stillAlive = culprit >= 0 && p.IsAliveIndex(culprit) && p.Uid[culprit] == c.Culprit;
        if (stillAlive && v is Verdict.Fine or Verdict.Flog)
            Chron(culprit, $"{SubjName(culprit)} {Ro(CrimeName(c.Type))} 잡혀 {Obj(VerdictName(v))} 받았다");
        bool punished = v is Verdict.Fine or Verdict.Flog or Verdict.Execute;
        if (punished) _priors[c.Culprit] = _priors.GetValueOrDefault(c.Culprit) + 1;
        // 빼돌린 징수관은 벌을 받으면 자리에서 쫓겨난다
        if (stillAlive && c.Type == CrimeType.Embezzle && v is Verdict.Fine or Verdict.Flog
            && _employer.TryGetValue(culprit, out int boss) && boss == c.Lord)
        {
            p.JobOf[culprit] = 0;
            _employer.Remove(culprit);
            _employerVer++;
        }
        CrimeLoyalty(c, punished, v is Verdict.Covered or Verdict.Released);
    }

    /// <summary>
    /// 사건이 지배자를 보는 눈을 바꾼다. 아래 집 수 N, K = 15.
    /// 잡아 벌하면 피해자 집 가장 +20 × K/(K+N), 같은 마을 가장들 +5 × K/(K+N).
    /// 놓치거나 덮으면 피해자 집 가장 −20 × N/(K+N), 같은 마을 가장들 −5 × N/(K+N). 덮은 걸 알면 1.5배.
    /// </summary>
    private void CrimeLoyalty(Crime c, bool punished, bool covered)
    {
        var p = People; var hh = Households;
        if (c.Type == CrimeType.Embezzle || !hh.IsAlive(c.Lord) || !p.IsValid(hh.Head[c.Lord])) return;
        int lu = p.Uid[hh.Head[c.Lord].Index];
        const float K = 15f;
        float n = BelowOf(c.Lord);
        float w = CrimeWeight(c.Type) * (c.Attempt ? 0.5f : 1f);
        float big, small;
        if (punished) { big = 20f * w * K / (K + n); small = 5f * w * K / (K + n); }
        else { float m = covered && c.VictimKnows ? 1.5f : 1f; big = -20f * w * m * n / (K + n); small = -5f * w * n / (K + n); }
        if (hh.IsAlive(c.VictimHouse) && p.IsValid(hh.Head[c.VictimHouse]) && hh.Head[c.VictimHouse].Index != hh.Head[c.Lord].Index)
            Social.Add(p, hh.Head[c.VictimHouse].Index, lu, big);
        if (c.Village < 0) return;
        // 같은 마을에서 이 지배자를 섬기는 가장 몇이 소식을 듣는다
        for (int k = 0; k < 6; k++)
        {
            int h = _rng.NextInt(hh.HighWater);
            if (!hh.IsAlive(h) || hh.Village[h] != c.Village || h == c.VictimHouse || !p.IsValid(hh.Head[h])) continue;
            int head = hh.Head[h].Index;
            if (p.LoyalUid[head] == lu) Social.Add(p, head, lu, small);
        }
        Stats.CrimeOpinion += big;
        int bucket = n < 5 ? 0 : n < 20 ? 1 : n < 100 ? 2 : 3;
        var e = CrimeSizeLedger.GetValueOrDefault(bucket);
        CrimeSizeLedger[bucket] = (e.n + 1, e.caught + (punished ? 1 : 0), e.opinion + big);
    }

    /// <summary>
    /// 드러난 범죄 비밀: 범인이 알려졌다. 신고되지 않았던 사건은 다시 신고될 수 있고, 놓친 사건은 범인을 알고 다시 쫓는다.
    /// 맡은 땅의 사건 수는 다시 세지 않는다.
    /// </summary>
    private void ReopenCrime(Secret s)
    {
        int culprit = FindAlive(s.A), victim = FindAlive(s.B);
        foreach (var c in _crimes)
        {
            if (c.Culprit != s.A || c.Victim != s.B || c.Verdict is not (Verdict.Unreported or Verdict.Escaped or Verdict.NoLord)) continue;
            if (culprit < 0) return;
            c.VictimKnows = true;   // 이제 범인이 알려졌다
            Stats.Reopened++;
            if (c.Verdict == Verdict.Escaped && c.Lord >= 0) { c.Verdict = Verdict.Open; Investigate(c); }
            else { c.Verdict = Verdict.Open; ReportCrime(c, c.Type == CrimeType.Murder ? -1 : victim); }
            return;
        }
    }

    /// <summary>
    /// 범죄 비밀이 드러났다: 범인은 명예를 잃고 사건이 다시 열린다.
    /// 덮은 사건이 드러나면 지배자가 명예를 잃고, 그를 섬기는 사람들이 그를 나쁘게 본다.
    /// </summary>
    private void ExposeCrime(Secret s, Dictionary<int, int> byUid)
    {
        var p = People;
        if (s.Type == SecretType.Crime)
        {
            if (byUid.TryGetValue(s.A, out int a)) p.Honor[a] -= 0.2f;
            ReopenCrime(s);
            return;
        }
        if (byUid.TryGetValue(s.A, out int lord))
        {
            p.Honor[lord] -= 0.15f;
            int h = p.Household[lord];
            // 소문을 들은 가장들(아는 사람 + 그를 섬기는 가장 몇)
            foreach (int u in s.Known) if (u != s.A && u != s.B && byUid.TryGetValue(u, out int k)) Social.Add(p, k, s.A, -15f);
            for (int t = 0; t < 8; t++)
            {
                int x = _rng.NextInt(Households.HighWater);
                if (!Households.IsAlive(x) || PatronOf(x) != h || !p.IsValid(Households.Head[x])) continue;
                Social.Add(p, Households.Head[x].Index, s.A, -8f);
            }
        }
        if (byUid.TryGetValue(s.B, out int b)) p.Honor[b] -= 0.1f;
    }

    // ------------------------------------------------------------------ 해마다

    /// <summary>범죄 열기를 식히고, 아직 판결하지 않은 플레이어 사건은 한 해가 지나면 정한 세기로 판결한다.</summary>
    private void CrimeYear()
    {
        foreach (var v in _crimeHeat.Keys.ToList()) { _crimeHeat[v] *= 0.5f; if (_crimeHeat[v] < 0.05f) _crimeHeat.Remove(v); }
        foreach (var c in _crimes)
            if (c.Verdict == Verdict.Pending && Day - c.Day >= Config.DaysPerYear)
            {
                int culprit = FindAlive(c.Culprit);
                if (culprit < 0) { c.Verdict = Verdict.Escaped; continue; }
                Judge(c, ChooseVerdict(c, culprit, false));
            }
    }

    public float CrimeHeat(int v) => _crimeHeat.GetValueOrDefault(v);

    public IEnumerable<Crime> PendingCases() => _crimes.Where(c => c.Verdict == Verdict.Pending && c.Lord == PlayerHousehold);

    public Crime? CrimeById(int id) => _crimes.FirstOrDefault(c => c.Id == id);

    /// <summary>플레이어 판결. 벌금, 매질, 처형, 풀어 주기는 언제나, 덮기는 내 사람일 때만.</summary>
    public bool PlayerJudge(int id, Verdict v)
    {
        var c = CrimeById(id);
        if (c == null || c.Verdict != Verdict.Pending || c.Lord != PlayerHousehold) return false;
        if (v is not (Verdict.Fine or Verdict.Flog or Verdict.Execute or Verdict.Released or Verdict.Covered)) return false;
        if (v == Verdict.Covered && !CanCover(id)) return false;
        string name = Names.Full(Lineage, c.Culprit);
        Judge(c, v);
        Chronicle.Add((Day, $"{CrimeName(c.Type)} 사건: {VerdictDone(v, name)}"));
        return true;
    }

    /// <summary>플레이어가 저지를 때 보는 숫자: 들켜서 신고되고 잡힐 확률(대략)과 맡은 지배자의 벌.</summary>
    public (float caught, string punish) CrimeOutlook(InteractionId id, int actor, int target)
    {
        var p = People;
        var type = id switch { InteractionId.Steal => CrimeType.Theft, InteractionId.Assault => CrimeType.Assault, InteractionId.Extort => CrimeType.Extortion, _ => CrimeType.Murder };
        int vh = p.Household[target];
        int lord = ResponsibleLord(vh);
        if (lord < 0) return (0f, "맡은 지배자 없음");
        float know = type switch { CrimeType.Theft => 0.25f, CrimeType.Murder => 0f, _ => 1f };
        float report = Math.Clamp(0.25f + 0.6f * 0.5f + (type == CrimeType.Murder ? 0.4f : 0f), 0.05f, 0.95f);
        var c = new Crime { Type = type, Lord = lord, Village = Households.Village[vh], VictimKnows = know >= 1f };
        float catchKnown = CatchChance(c, actor);
        c.VictimKnows = false;
        float catchUnknown = CatchChance(c, actor);
        float caught = report * (know * catchKnown + (1f - know) * catchUnknown);
        float x = Severity(lord) * CrimeWeight(type);
        string punish = CloseToLord(actor, lord) ? "아마 덮어 줌(지배자와 가까움)" : x > 1.2f ? "처형" : x > 0.45f ? "매질" : "벌금";
        return (caught, punish);
    }
}
