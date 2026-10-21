namespace People.Sim;

/// <summary>붙잡힌 상태의 이름. 붙잡은 갈래의 정체를 따른다.</summary>
public enum CaptiveKind : byte { Detained, Abducted, Prisoner }

/// <summary>붙잡힌 사람 하나. 붙잡힌 사람은 제 집에 그대로 적혀 있고, 상태로만 붙잡혀 있다.</summary>
public sealed class Captive
{
    public int Uid, Root, Unit, Since, Home = -1, HomeFounded;
    public CaptiveKind Kind;
    public int Placed = -1;          // 강제로 둔 갈래 Id(-1이면 가둬 둠)
    public float Comply;             // 순응(강제 배치)
    public float Owed;               // 세금 구금이면 밀린 것
}

/// <summary>
/// 세력 개편 R10(plan_reform): 포획과 붙잡힌 사람.
/// - 포획: 포획을 고른 갈래가 털기·치기에 이기면 25%로 맞선 어른 하나를 붙잡는다. 납치 일은 아이를 붙잡는다. 미납 대응이 구금이면 가장을 붙잡는다.
///   상태 이름: 경비대·자경단·치안대·지배자 조직은 구금, 도적 떼·납치단은 납치, 군대·용병단은 포로.
/// - 붙잡힌 사람은 제 집에 적힌 채로 붙잡혀 있다. 부부 사이에 아이가 생기지 않고, 몸이 닿는 상호작용(성관계·혼인)을 걸지도 받지도 않는다.
/// - 가둬 둠: 붙잡은 세력의 집이 한 사람 한 해 식량 12를 낸다(못 내면 풀어 준다). 해마다 0.15 × (1 − 지키는 힘)으로 달아난다(지키는 힘 = 세력 사람 × 0.05, 많아야 0.8).
/// - 처리: 가둬 둠, 강제 배치(같은 세력 어느 갈래든 `CanPlace`), 요구(그 집 가장에게 몸값), 풀어 줌, 처형.
///   16세 아래는 가둬 둠·풀어 줌·몸값만 한다. 잡혀 있는 동안 그 집 가장의 원한이 해마다 쌓인다. 아이 몸값은 평판을 0.2 깎는다.
/// - 강제 배치: 순응 = 0.1 + 같은 종교 0.2 + 공포 × 0.3. 해마다 +0.05 + 대우(임금 없음 0, 적음·보통 0.03, 많음 이상 0.06) + 예배 0.03 + 학생 0.05.
///   해마다 0.2 × (1 − 순응)으로 달아나고, 0.7을 넘으면 스스로 남는 조직원이 된다(조직 충성 0.5).
/// - 지렛대: 붙잡은 세력 사람이 그 집 사람에게 거는 상호작용은 수락 = 1 − (1 − 기본)^(1 + 3 × 지렛대 × 굽힘).
///   지렛대 = 붙잡힌 사람의 값(본인·가장 1, 배우자·자식 0.8, 형제 0.5, 같은 집 0.2) + 잡힌 햇수 × 0.2(많아야 0.6) + 공포 × 0.5.
///   굽힘 = 0.5 + 신중 × 0.5 − 고집 × 0.3 − 공격성 × 0.2. 16세 아래 붙잡힌 사람은 지렛대가 되지 않는다. 몸이 닿는 상호작용에는 걸리지 않는다.
/// - 평판: 민간인을 붙잡으면 평판 −0.05·공포 +0.02, 적대 세력 사람은 이름 +0.01, 세금 구금은 평판 −0.01·공포 +0.01. 범인을 잡으면 평판 +0.005.
/// - NPC: 해마다 몸값을 청하고, 거절되면 거친 우두머리(공격성 0.75)는 처형하기도(20%), 야심 있는 무력 우두머리는 제 갈래에 두기도(30%) 한다. 두 해면 풀어 준다(세금 구금·아이는 한 해·두 해).
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, Captive> _captives = new();

    public IEnumerable<Captive> Captives => _captives.Values;
    public Captive? CaptiveOf(int i) => People.IsAliveIndex(i) && _captives.TryGetValue(People.Uid[i], out var c) ? c : null;
    public bool IsCaptive(int i) => People.IsAliveIndex(i) && _captives.ContainsKey(People.Uid[i]);

    public static string CaptiveKindName(CaptiveKind k) => k switch { CaptiveKind.Abducted => "납치", CaptiveKind.Prisoner => "포로", _ => "구금" };

    private CaptiveKind KindByUnit(Group unit)
    {
        string label = UnitLabel(unit);
        if (label is "도적 떼" or "납치단") return CaptiveKind.Abducted;
        if (label is "군대" or "용병단") return CaptiveKind.Prisoner;
        return unit.Kind switch { GroupKind.Bandits => CaptiveKind.Abducted, GroupKind.Sellswords => CaptiveKind.Prisoner, _ => CaptiveKind.Detained };
    }

    /// <summary>붙잡은 세력이 먹이는 집(지배자 집이나 우두머리 집).</summary>
    private int CaptorHouse(Group root)
    {
        root = RootOf(root);
        if (root.Home >= 0) return root.Home;
        int l = FindAlive(root.LeaderUid);
        return l >= 0 ? People.Household[l] : -1;
    }

    /// <summary>갈래 unit 이 사람 i 를 붙잡는다. why: 0 민간인, 1 적대 세력, 2 세금.</summary>
    public bool Capture(Group unit, int i, int why = 0, float owed = 0f)
    {
        var p = People; var hh = Households;
        if (!p.IsAliveIndex(i) || IsCaptive(i) || IsPlayer(i)) return false;
        var root = RootOf(unit);
        int leader = FindAlive(root.LeaderUid);
        if (leader < 0 || p.Household[i] == CaptorHouse(root)) return false;
        int h = p.Household[i];
        _captives[p.Uid[i]] = new Captive
        {
            Uid = p.Uid[i], Root = root.Id, Unit = unit.Id, Since = Day, Kind = why == 2 ? CaptiveKind.Detained : KindByUnit(unit),
            Home = h, HomeFounded = hh.IsAlive(h) ? hh.FoundedDay[h] : 0, Owed = owed,
        };
        // 붙잡힌 사람이 맡던 조직 일은 놓는다
        foreach (var g in GroupsOf(i).ToList()) if (RootOf(g).Id != root.Id) LeaveGroup(i, g);
        Social.Add(p, i, root.LeaderUid, -30f, 20f);
        int head = HeadIdx(h);
        if (head >= 0 && head != i) Social.Add(p, head, root.LeaderUid, -25f, 10f);
        if (why == 1) AddRepRoot(root, 0.01f, 0f, 0.01f);
        else if (why == 2) AddRepRoot(root, 0f, -0.01f, 0.01f);
        else AddRepRoot(root, 0.01f, -0.05f, 0.02f);
        Stats.Captured++;
        Chron(i, $"{SubjName(i)} {root.Name}에 붙잡혔다({CaptiveKindName(_captives[p.Uid[i]].Kind)})");
        return true;
    }

    /// <summary>풀어 준다. 붙잡혀 있던 일은 원한과 두려움으로 남는다.</summary>
    public void Release(int i, bool escaped = false)
    {
        var p = People;
        if (CaptiveOf(i) is not { } c) return;
        _captives.Remove(c.Uid);
        var root = GroupById(c.Root);
        if (c.Placed >= 0 && GroupById(c.Placed) is { } u && InGroup(i, u)) LeaveGroup(i, u);
        if (root != null && root.LeaderUid >= 0) Social.Remember(p, i, MemoryType.Raided, root.LeaderUid, Day, -40);
        if (escaped) Stats.CaptivesEscaped++; else Stats.CaptivesFreed++;
        Chron(i, escaped ? $"{SubjName(i)} 붙잡혀 있다가 달아났다" : $"{SubjName(i)} 풀려났다");
    }

    /// <summary>처형한다(16세부터).</summary>
    public bool Execute(int i)
    {
        var p = People;
        if (CaptiveOf(i) is not { } c || AgeYears(i) < 16) return false;
        var root = GroupById(c.Root);
        _captives.Remove(c.Uid);
        int h = p.Household[i];
        if (root != null)
        {
            foreach (int m in MembersOf(h)) if (m != i) Social.Add(p, m, root.LeaderUid, -60f, 30f);
            AddRepRoot(root, 0.02f, c.Kind == CaptiveKind.Detained ? -0.02f : -0.08f, 0.04f);
        }
        Stats.CaptivesExecuted++;
        Chron(i, $"{SubjName(i)} 붙잡혀 있다가 처형되었다");
        Die(i, false);
        return true;
    }

    /// <summary>같은 세력의 갈래 unit 에 강제로 둔다(16세부터, 배치 규칙을 따른다).</summary>
    public bool Place(int i, Group unit)
    {
        var p = People;
        if (CaptiveOf(i) is not { } c || AgeYears(i) < 16 || RootOf(unit).Id != c.Root) return false;
        if (!CanPlace(i, unit, RoleDefOf(RoleKind.Member))) return false;
        if (c.Placed >= 0 && GroupById(c.Placed) is { } old && InGroup(i, old)) LeaveGroup(i, old);
        if (!InGroup(i, unit) && !JoinGroup(i, unit)) return false;
        var root = RootOf(unit);
        if (c.Placed < 0)
            c.Comply = 0.1f + (root.ReligionId >= 0 && ReligionOf(i) is { } r && r.Id == root.ReligionId ? 0.2f : 0f) + 0.3f * RepOfRoot(root).dread;
        c.Placed = unit.Id;
        Stats.CaptivesPlaced++;
        return true;
    }

    /// <summary>붙잡힌 사람 c 가 사람 t 에게 얼마나 값진가(본인·가장 1, 배우자·자식 0.8, 형제 0.5, 같은 집 0.2).</summary>
    private float CaptiveValue(int ci, int t)
    {
        var p = People;
        if (ci == t) return 1f;
        if (HeadIdx(p.Household[t]) == ci && p.Household[t] == p.Household[ci]) return 1f;
        if (SpouseOf(t) == ci) return 0.8f;
        var tid = p.IdAt(t);
        if ((p.IsValid(p.Mother[ci]) && p.Mother[ci] == tid) || (p.IsValid(p.Father[ci]) && p.Father[ci] == tid)) return 0.8f;
        if ((p.IsValid(p.Mother[ci]) && p.Mother[ci] == p.Mother[t]) || (p.IsValid(p.Father[ci]) && p.Father[ci] == p.Father[t])) return 0.5f;
        if (p.Household[ci] == p.Household[t]) return 0.2f;
        return 0f;
    }

    /// <summary>actor 의 세력이 붙잡고 있는 어른 가운데 target 에게 가장 무거운 지렛대(없으면 0).</summary>
    public float Leverage(int actor, int target)
    {
        var p = People;
        if (_captives.Count == 0 || !p.IsAliveIndex(actor) || !p.IsAliveIndex(target)) return 0f;
        float best = 0f;
        foreach (var c in _captives.Values)
        {
            var root = GroupById(c.Root);
            if (root == null || (root.LeaderUid != p.Uid[actor] && !GroupsOf(actor).Any(g => RootOf(g).Id == root.Id))) continue;
            int ci = FindAlive(c.Uid);
            if (ci < 0 || AgeYears(ci) < 16) continue;   // 아이는 지렛대가 되지 않는다(몸값만)
            float v = CaptiveValue(ci, target);
            if (v <= 0f) continue;
            float lev = v + MathF.Min(0.6f, 0.2f * (Day - c.Since) / Config.DaysPerYear) + 0.5f * RepOfRoot(root).dread;
            best = MathF.Max(best, lev);
        }
        return best;
    }

    private float Bend(int t)
    {
        var p = People;
        return Math.Clamp(0.5f + 0.5f * p.Get(t, Trait.Caution) - 0.3f * p.Get(t, Trait.Stubbornness) - 0.2f * p.Get(t, Trait.Aggression), 0f, 1f);
    }

    private void InitCaptiveRules()
    {
        // 붙잡고 있으면 그 집 사람에게 거는 요구가 잘 받아들여진다. 몸이 닿는 상호작용(성관계·혼인)에는 걸지 않는다
        AcceptMods.Add((w, def, c, acc) =>
        {
            if (def.Sexual || def.Intimate || def.Id == InteractionId.Ransom) return acc;
            float lev = w.Leverage(c.Actor, c.Target);
            if (lev <= 0f) return acc;
            return 1f - MathF.Pow(1f - Math.Clamp(acc, 0f, 1f), 1f + 3f * lev * w.Bend(c.Target));
        });
        // 붙잡힌 사람과 빚에 묶인 사람과 16세 아래는 유곽 일을 하는 갈래에 두지 않는다
        PlacementRules.Add((w, i, u, r) => !(u.Tasks.Any(t => t.Verb == Verb.Brothel) || u.Kind == GroupKind.Brothel)
                                           || (!w.IsCaptive(i) && w.AgeYears(i) >= 16 && w.DebtOf(w.People.Household[i]) <= 0f));
    }

    /// <summary>actor 세력이 붙잡고 있는, 가장 target 의 집 사람.</summary>
    private Captive? RansomCaptive(int actor, int target)
    {
        var p = People;
        int th = p.Household[target];
        if (HeadIdx(th) != target) return null;
        foreach (var c in _captives.Values)
        {
            var root = GroupById(c.Root);
            if (root == null || root.LeaderUid != p.Uid[actor]) continue;
            int ci = FindAlive(c.Uid);
            if (ci >= 0 && ci != target && p.Household[ci] == th) return c;
        }
        return null;
    }

    private void AddCaptiveDefs(InteractionDef[] d)
    {
        d[(int)InteractionId.Ransom] = new InteractionDef
        {
            // 붙잡은 세력 우두머리(Actor) → 붙잡힌 사람의 집 가장(Target). 받아들이면 은화를 내고 풀려난다
            Id = InteractionId.Ransom, Name = "몸값 요구", Category = InteractionCategory.Scheme, Harsh = true,
            Amounts = new[] { 20f, 50f, 100f, 200f }, AmountNames = new[] { "은화 20", "은화 50", "은화 100", "은화 200" },
            Can = (w, c) => w.RansomCaptive(c.Actor, c.Target) != null,
            Accept = (w, c) =>
            {
                var cap = w.RansomCaptive(c.Actor, c.Target);
                if (cap == null) return 0f;
                var p = w.People; var hh = w.Households;
                int th = p.Household[c.Target], ci = w.FindAlive(cap.Uid);
                float amt = c.Amount > 0f ? c.Amount : 20f;
                float wealth = hh.Coin[th] + hh.Food[th] * w.FoodPrice(hh.Village[th]);
                float acc = 0.6f * MathF.Min(1f, wealth / (2f * amt)) + 0.2f * p.Get(c.Target, Trait.Caution) - 0.2f * p.Get(c.Target, Trait.Stubbornness)
                            + 0.2f * w.CaptiveValue(ci, c.Target) + (w.AgeYears(ci) < 16 ? 0.2f : 0f);
                // 어른이 붙잡혀 있으면 지렛대가 더한다(아이는 위에서 따로 더했다)
                float lev = w.AgeYears(ci) >= 16 ? w.Leverage(c.Actor, c.Target) : 0f;
                acc = Math.Clamp(acc, 0f, 1f);
                return lev > 0f ? 1f - MathF.Pow(1f - acc, 1f + 3f * lev * w.Bend(c.Target)) : acc;
            },
            Success = (w, c, _) =>
            {
                var cap = w.RansomCaptive(c.Actor, c.Target)!;
                int ci = w.FindAlive(cap.Uid);
                var root = w.GroupById(cap.Root)!;
                float paid = w.PayCoin(w.People.Household[c.Target], -2, c.Amount > 0f ? c.Amount : 20f);
                w.Earn(root, paid, true);
                w.Stats.Ransoms++;
                // 아이 몸값은 평판을 크게 깎는다
                if (w.AgeYears(ci) < 16) w.AddRepRoot(root, 0.02f, -0.2f, 0.03f);
                else w.AddRepRoot(root, 0.01f, -0.03f, 0.02f);
                Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -30f, 30f);
                w.Release(ci);
            },
            Fail = (w, c) => Social.Add(w.People, c.Target, w.People.Uid[c.Actor], -10f),
        };
    }

    /// <summary>해마다: 먹이기, 달아나기, 강제 배치의 순응, 아이의 원한, NPC의 처리.</summary>
    private void CaptiveYear()
    {
        var p = People; var hh = Households;
        int held = 0;
        foreach (var c in _captives.Values.ToList())
        {
            int i = FindAlive(c.Uid);
            var root = GroupById(c.Root);
            if (i < 0) { _captives.Remove(c.Uid); continue; }
            if (root == null || FindAlive(root.LeaderUid) < 0) { Release(i); continue; }
            float years = (Day - c.Since) / (float)Config.DaysPerYear;
            int head = HeadIdx(p.Household[i]);
            if (AgeYears(i) < 16 && head >= 0 && head != i) Social.Add(p, head, root.LeaderUid, -5f, 5f);   // 아이가 잡혀 있는 동안 원한이 쌓인다
            if (c.Placed >= 0)
            {
                var u = GroupById(c.Placed);
                if (u == null || !InGroup(i, u)) { c.Placed = -1; }
                else
                {
                    float treat = WageOf(u, RoleKind.Member) switch { WageLevel.None => 0f, WageLevel.Low or WageLevel.Normal => 0.03f, _ => 0.06f };
                    c.Comply += 0.05f + treat + (Subtree(root).Any(g => g.Tasks.Any(t => t.Verb == Verb.Worship)) ? 0.03f : 0f) + (IsStudent(i) ? 0.05f : 0f);
                    if (c.Comply > 0.7f)
                    {
                        _captives.Remove(c.Uid);
                        _orgLoyal[(root.Id, c.Uid)] = 0.5f;
                        Stats.CaptivesConverted++;
                        Chron(i, $"{SubjName(i)} {root.Name}에 스스로 남기로 했다");
                        continue;
                    }
                    if (_rng.Chance(0.2f * (1f - c.Comply))) { Release(i, true); continue; }
                    held++;
                    continue;
                }
            }
            // 가둬 둠: 먹이고, 지키는 힘만큼 덜 달아난다
            int ch = CaptorHouse(root);
            if (ch < 0 || hh.Food[ch] < 12f) { Release(i); continue; }
            hh.Food[ch] -= 12f;
            float guard = MathF.Min(0.8f, 0.05f * root.Total);
            if (_rng.Chance(0.15f * (1f - guard))) { Release(i, true); continue; }
            int leader = FindAlive(root.LeaderUid);
            if (!IsPlayer(leader)) { if (NpcCaptive(c, i, root, leader, years)) continue; }
            held++;
        }
        Stats.CaptivesHeld = held;
    }

    /// <summary>NPC 우두머리의 처리. 붙잡힌 사람이 더는 없으면 true.</summary>
    private bool NpcCaptive(Captive c, int i, Group root, int leader, float years)
    {
        var p = People;
        int head = HeadIdx(p.Household[i]);
        if (head >= 0 && head != i)
        {
            float amt = c.Owed > 0f ? MathF.Max(20f, c.Owed) : 50f;
            if (Interact(InteractionId.Ransom, new InteractionContext { Actor = leader, Target = head, Third = -1, Amount = amt }).Accepted) return true;
        }
        if (AgeYears(i) < 16) { if (years >= 2f) { Release(i); return true; } return false; }
        if (c.Kind == CaptiveKind.Detained && c.Owed > 0f && years >= 1f) { Release(i); return true; }
        if (years >= 1f && p.Get(leader, Trait.Aggression) > 0.75f && _rng.Chance(0.2f)) { Execute(i); return true; }
        if (IsForce(root.Kind) && p.Get(leader, Trait.Ambition) > 0.5f && _rng.Chance(0.3f))
        {
            var unit = Subtree(root).Where(u => u.Alive && (u == root || RootOf(u).Id == root.Id)).FirstOrDefault(u => CanPlace(i, u, RoleDefOf(RoleKind.Member)));
            if (unit != null && Place(i, unit)) return false;
        }
        if (years >= 2f) { Release(i); return true; }
        return false;
    }

    // 시험용
    public void RecountCaptives() => CaptiveYear();
    public void BackdateCaptive(int i, int days) { if (CaptiveOf(i) is { } c) c.Since -= days; }
}
