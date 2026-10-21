using Godot;
using People.Sim;
using System.Threading.Tasks;

namespace People.Game;

/// <summary>
/// Godot 안에서 시뮬레이션을 돌리는 노드. 화면(GDScript)은 이 노드에 묻고 시킨다.
/// 처음 세계를 만들고 정착을 몇백 년 돌리는 일은 뒤에서(스레드) 하고, 그 뒤로는 화면 프레임마다 날짜를 나아가게 한다.
/// </summary>
public partial class SimHost : Node
{
    private World? _w;
    private Task? _boot;
    private volatile int _bootYear;
    private int _chronRead;

    public bool Ready => _w != null && (_boot == null || _boot.IsCompleted);
    public int BootYear => _bootYear;
    public int Day => _w?.Day ?? 0;
    public string DateText => _w?.DateText ?? "";
    public int Population => _w?.People.AliveCount ?? 0;

    /// <summary>세계를 만들고 years 년 동안 정착을 돌린다(뒤에서).</summary>
    public void Boot(int seed, int years, int couples)
    {
        _boot = Task.Run(() =>
        {
            var map = WorldMap.Generate((uint)seed, 48);
            var w = new World((ulong)seed, map, SimConfig.Default);
            w.SeedCouples(couples);
            for (int y = 0; y < years; y++)
            {
                for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
                _bootYear = y + 1;
            }
            _w = w;
        });
    }

    /// <summary>
    /// 다음 단계 판: 지금 단계, 따르는 집, 무력, 살림, 명성(명예), 칭호, 주군의 의심(눈치 빠른 가장만 짐작),
    /// 그리고 다음 단계까지 줄마다(가진 것, 필요한 것, 채웠나).
    /// </summary>
    public Godot.Collections.Dictionary Progress()
    {
        var e = new Godot.Collections.Dictionary();
        if (!Ready) return e;
        var w = _w!; var p = w.People; var hh = w.Households;
        int h = w.PlayerHousehold;
        if (!hh.IsAlive(h) || !p.IsValid(hh.Head[h])) return e;
        int head = hh.Head[h].Index;
        int t = w.TierOf(h);
        e["tier"] = t >= 1 ? World.TierName(t) : "평민";
        e["next"] = World.TierName(Math.Min(4, t + 1));
        e["clients"] = w.ClientCount(h);
        e["honor"] = p.Honor[head];
        e["legit"] = t >= 2 ? World.LegitName(w.LegitOf(h)) : "";
        int liege = w.PatronOf(h);
        var r = liege >= 0 ? w.RivalOf(liege, h) : null;
        e["suspicion"] = r == null ? "" : p.Get(head, Trait.Perception) > 0.6f ? (r.Suspicion < 15 ? "눈여겨봄" : r.Suspicion < 35 ? "의심함" : r.Suspicion < 70 ? "크게 의심함" : "칼을 갈고 있음") : "눈여겨보는 듯함";
        var rows = new Godot.Collections.Array();
        foreach (var (what, have, need, ok) in w.TierProgress(h))
            rows.Add(new Godot.Collections.Dictionary { ["what"] = what, ["have"] = have, ["need"] = need, ["ok"] = ok });
        e["rows"] = rows;
        return e;
    }

    /// <summary>자리 잡은 세계를 만들어(spec 34절) warm 해 동안 돌린다.</summary>
    public void BootGenerated(int seed, int rulers, int villagesPerRuler, int warm)
    {
        _boot = Task.Run(() =>
        {
            var map = WorldMap.Generate((uint)seed, 48);
            var w = new World((ulong)seed, map, SimConfig.Default);
            w.GenerateSettled(new GenOptions { Rulers = rulers, VillagesPerRuler = villagesPerRuler });
            for (int y = 0; y < warm; y++)
            {
                for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
                _bootYear = y + 1;
            }
            _w = w;
        });
    }

    public string BootError => _boot?.Exception?.InnerException?.ToString() ?? "";

    /// <summary>날짜를 days 만큼 나아가게 한다. 플레이어 집에 갈림길이 생기면 멈추고 true.</summary>
    public bool Advance(int days)
    {
        if (!Ready) return false;
        for (int d = 0; d < days; d++)
        {
            _w!.Step();
            if (_w.TakeFork()) return true;
        }
        return false;
    }

    /// <summary>화면용 지역 JSON(타일 좌표 x0, y0부터 w × h).</summary>
    public string Region(int x0, int y0, int w, int h)
    {
        if (!Ready) return "";
        _w!.Map.EnsureAround(x0 + w / 2, y0 + h / 2, Math.Max(w, h));
        return RegionView.Json(_w, x0, y0, w, h);
    }

    /// <summary>가장 큰 마을의 가운데(타일 좌표).</summary>
    public Vector2I BiggestVillage()
    {
        var vl = _w!.VillageList;
        int best = -1;
        for (int v = 0; v < vl.Count; v++) if (vl.Alive[v] && (best < 0 || vl.Households[v] > vl.Households[best])) best = v;
        return best < 0 ? new Vector2I(_w.SpawnX, _w.SpawnY) : new Vector2I(vl.CenterX[best], vl.CenterY[best]);
    }

    public Vector2I HousePos(int h) => _w != null && _w.Households.IsAlive(h) ? new Vector2I(_w.Households.X[h], _w.Households.Y[h]) : new Vector2I(-1, -1);

    // ------------------------------------------------------------------ 플레이어 가족

    public int Player => _w?.PlayerHousehold ?? -1;

    public void SetPlayer(int h)
    {
        if (_w == null || !_w.Households.IsAlive(h)) return;
        _w.PlayerHousehold = h;
        _chronRead = _w.ChronicleCount;
        var head = _w.Households.Head[h];
        string name = _w.People.IsValid(head) ? Names.Full(_w.Lineage, _w.People.Uid[head.Index]) : "";
        _w.Chronicle.Add((_w.Day, $"{name}의 집을 맡았다"));
    }

    /// <summary>플레이어 집 요약: 가문, 식량, 땅, 같은 성을 쓰는 산 사람 수, 식구 목록.</summary>
    public Godot.Collections.Dictionary PlayerSummary()
    {
        var d = new Godot.Collections.Dictionary();
        int h = Player;
        if (h < 0) return d;
        var w = _w!; var p = w.People; var hh = w.Households;
        var head = hh.Head[h];
        int fam = p.IsValid(head) ? w.Lineage.FamilyOf(p.Uid[head.Index]) : -1;
        int kin = 0;
        for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i) && w.Lineage.FamilyOf(p.Uid[i]) == fam) kin++;
        d["family"] = fam >= 0 ? Names.Family(fam) : "";
        d["food"] = hh.Food[h];
        d["coin"] = hh.Coin[h];
        d["foodPrice"] = w.FoodPrice(hh.Village[h]);
        d["surplus"] = w.Surplus(h);
        d["plots"] = hh.PlotCount[h];
        d["kin"] = kin;
        d["honor"] = p.IsValid(head) ? p.Honor[head.Index] : 0f;
        var members = new Godot.Collections.Array();
        for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
        {
            var m = new Godot.Collections.Dictionary
            {
                ["i"] = k,
                ["name"] = Names.Full(w.Lineage, p.Uid[k]),
                ["age"] = (w.Day - p.BirthDay[k]) / w.Config.DaysPerYear,
                ["male"] = p.Male[k],
                ["head"] = head.Index == k,
                ["pregnant"] = p.PregnantUntil[k] >= 0,
                ["heir"] = w.HeirOf(h) == k,
                ["concubine"] = !p.Male[k] && p.Concubine[k] && p.IsValid(p.Husband[k]),
                ["right"] = w.Lineage.RightOf(p.Uid[k]) == Birthright.Concubine ? "서자" : "",
                ["hungry"] = p.HungryToday[k],
            };
            members.Add(m);
        }
        d["members"] = members;
        return d;
    }

    /// <summary>새로 생긴 연대기 줄들("날짜 · 글").</summary>
    public string[] DrainChronicle()
    {
        if (_w == null) return System.Array.Empty<string>();
        var list = _w.Chronicle;
        if (_chronRead > list.Count) _chronRead = 0;
        var o = new List<string>();
        for (; _chronRead < list.Count; _chronRead++)
        {
            var (day, text) = list[_chronRead];
            var c = new Calendar(day, _w.Config);
            o.Add($"{c.Year + 1}년 {World.SeasonName(c.Season)} · {text}");
        }
        return o.ToArray();
    }

    // ------------------------------------------------------------------ 혈족 옮겨 맡기, 족보

    public int KinRange
    {
        get => _w?.KinRange ?? 4;
        set { if (_w != null) _w.KinRange = Math.Clamp(value, 2, 6); }
    }

    public int PendingSwitch => _w?.PendingSwitch ?? -1;

    /// <summary>집 h 를 맡는다면 혈족 집이 몇인가(자동 점검용, 맡은 집은 그대로 둔다).</summary>
    public int KinCountIf(int h)
    {
        if (_w == null) return 0;
        int keep = _w.PlayerHousehold;
        _w.PlayerHousehold = h;
        int n = _w.KinHouseholds().Count;
        _w.PlayerHousehold = keep;
        return n;
    }

    public bool RequestSwitch(int h) => _w != null && _w.RequestSwitch(h);

    /// <summary>집 h 가 지금 맡은 가장에서 몇 촌인가(혈족이 아니면 -1).</summary>
    public int KinChon(int h) => _w?.KinChonOfHousehold(h) ?? -1;

    private string HouseLabel(int h)
    {
        var w = _w!;
        int t = w.TierOf(h);
        return t >= 1 ? World.TierName(t) : World.ClassName(w.ClassOf(h));
    }

    /// <summary>옮겨 맡을 수 있는 혈족 집 목록: 집 번호, 촌수, 가장 이름, 가문, 이름표, 식구 수.</summary>
    public Godot.Collections.Array KinHouses()
    {
        var a = new Godot.Collections.Array();
        if (_w == null) return a;
        var w = _w; var p = w.People; var hh = w.Households;
        foreach (var (h, chon) in w.KinHouseholds())
        {
            var head = hh.Head[h];
            int hu = p.IsValid(head) ? p.Uid[head.Index] : -1;
            a.Add(new Godot.Collections.Dictionary
            {
                ["id"] = h, ["chon"] = chon, ["head"] = Names.Full(w.Lineage, hu), ["family"] = Names.Family(w.Lineage.FamilyOf(hu)),
                ["label"] = HouseLabel(h), ["members"] = hh.Members[h], ["x"] = hh.X[h], ["y"] = hh.Y[h],
            });
        }
        return a;
    }

    /// <summary>
    /// 족보: 지금 맡은 가장에서 혈족 범위 안의 사람들(죽은 조상 포함). 위로 올라갔다가 아래로만 내려가며 촌수를 센다.
    /// 사람마다 이름, 촌수, 대(위는 음수), 살았는지, 나이 또는 죽은 해, 사는 집, 그 집을 맡을 수 있는지.
    /// </summary>
    public Godot.Collections.Array KinTree()
    {
        var a = new Godot.Collections.Array();
        if (_w == null || _w.PlayerHousehold < 0) return a;
        var w = _w; var p = w.People; var hh = w.Households; var lin = w.Lineage;
        var head = hh.Head[w.PlayerHousehold];
        if (!p.IsValid(head)) return a;
        int anchor = p.Uid[head.Index];
        var kids = lin.ChildrenIndex();
        var alive = new Dictionary<int, int>();
        for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i)) alive[p.Uid[i]] = i;
        var best = new Dictionary<int, (int chon, int gen)>();
        var q = new Queue<(int u, int chon, int gen, bool up)>();
        q.Enqueue((anchor, 0, 0, true));
        while (q.Count > 0)
        {
            var (u, chon, gen, up) = q.Dequeue();
            if (best.TryGetValue(u, out var b) && b.chon <= chon) continue;
            best[u] = (chon, gen);
            if (chon >= w.KinRange) continue;
            if (up)
            {
                if (lin.Father(u) >= 0) q.Enqueue((lin.Father(u), chon + 1, gen - 1, true));
                if (lin.Mother(u) >= 0) q.Enqueue((lin.Mother(u), chon + 1, gen - 1, true));
            }
            if (kids.TryGetValue(u, out var cs)) foreach (int c in cs) q.Enqueue((c, chon + 1, gen + 1, false));
        }
        var kinHouses = new HashSet<int>(w.KinHouseholds().Select(x => x.h));
        foreach (var (u, (chon, gen)) in best.OrderBy(x => x.Value.gen).ThenBy(x => x.Value.chon))
        {
            bool live = alive.TryGetValue(u, out int i);
            int h = live ? p.Household[i] : -1;
            a.Add(new Godot.Collections.Dictionary
            {
                ["uid"] = u, ["name"] = Names.Full(lin, u), ["chon"] = chon, ["gen"] = gen, ["male"] = lin.Male(u), ["alive"] = live,
                ["age"] = live ? (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear : 0,
                ["died"] = !live && lin.Death(u) >= 0 ? lin.Death(u) / w.Config.DaysPerYear + 1 : 0,
                ["house"] = h, ["houseLabel"] = h >= 0 && hh.IsAlive(h) ? HouseLabel(h) : "",
                ["canTake"] = h >= 0 && kinHouses.Contains(h), ["mine"] = h >= 0 && h == w.PlayerHousehold,
            });
        }
        return a;
    }

    // ------------------------------------------------------------------ 모임(내 카드에서)

    /// <summary>행동하는 사람이 스스로 할 수 있는 일: 모임 세우기, 나가기. key 는 "found:종류" 또는 "leave:번호".</summary>
    public Godot.Collections.Array SelfActions(int actor)
    {
        var a = new Godot.Collections.Array();
        if (_w == null || !_w.People.IsAliveIndex(actor) || Age(actor) < 16) return a;
        var w = _w;
        var mine = w.GroupsOf(actor).ToList();
        if (mine.Count < World.GroupSlots)
            foreach (GroupKind k in new[] { GroupKind.Watch, GroupKind.Brotherhood, GroupKind.Bandits, GroupKind.School, GroupKind.Drill, GroupKind.Inspectors, GroupKind.Company, GroupKind.Sellswords, GroupKind.Brothel })
                if (!mine.Any(g => g.Kind == k))
                    a.Add(new Godot.Collections.Dictionary { ["key"] = $"found:{(int)k}", ["name"] = $"{World.GroupKindName(k)} 세우기", ["note"] = FoundNote(k), ["risky"] = k is GroupKind.Bandits or GroupKind.Brothel });
        foreach (var g in mine)
            a.Add(new Godot.Collections.Dictionary { ["key"] = $"leave:{g.Id}", ["name"] = $"{g.Name}에서 나가기", ["note"] = w.IsLeader(actor, g) ? "우두머리가 나가면 다른 사람이 잇는다" : "", ["risky"] = false });
        return a;
    }

    private static string FoundNote(GroupKind k) => k switch
    {
        GroupKind.Bandits => "숨어서 함께 턴다. 들키면 진압당한다",
        GroupKind.Watch => "마을을 함께 지킨다",
        GroupKind.Brotherhood => "신앙으로 묶이고 굶는 회원을 돕는다",
        GroupKind.School => "가르친다. 가르칠 만한 사람(글·무예·장사·공예 0.4)이 있어야 한다",
        GroupKind.Drill => "함께 무예를 닦는다(0.6까지)",
        GroupKind.Inspectors => "감시한다. 세력 안의 거사와 빼돌림을 캔다",
        GroupKind.Company => "함께 장사한다. 장사꾼이 있어야 한다",
        GroupKind.Sellswords => "칼을 판다. 열 명부터 계약한다",
        _ => "원하는 어른만 든다. 큰 마을에서만 열린다",
    };

    public string DoSelf(int actor, string key)
    {
        if (_w == null) return "";
        var w = _w;
        var parts = key.Split(':');
        if (parts[0] == "found")
        {
            var g = w.FoundGroup(actor, (GroupKind)int.Parse(parts[1]));
            return $"{World.Obj(g.Name)} 세웠다. 이웃을 눌러 '조직에 들이기'로 사람을 모은다";
        }
        var gr = w.GroupById(int.Parse(parts[1]));
        if (gr == null) return "";
        w.LeaveGroup(actor, gr);
        return $"{gr.Name}에서 나왔다";
    }

    // ------------------------------------------------------------------ 상호작용

    private int Age(int i) => (_w!.Day - _w.People.BirthDay[i]) / _w.Config.DaysPerYear;

    /// <summary>actor 가 target 에게 할 수 있는 상호작용 목록: id, 이름, 묶음, 받아들일 확률, 비밀 여부.</summary>
    public Godot.Collections.Array Interactions(int actor, int target)
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People;
        if (!p.IsAliveIndex(actor) || !p.IsAliveIndex(target) || actor == target) return a;
        var c = new InteractionContext { Actor = actor, Target = target, Third = -1 };
        for (int k = 0; k < (int)InteractionId.Count; k++)
        {
            var def = w.Def((InteractionId)k);
            if (def.Sexual && (Age(actor) < 16 || Age(target) < 16)) continue;
            var e = new Godot.Collections.Dictionary
            {
                ["id"] = k,
                ["name"] = def.Name,
                ["secret"] = def.Secret,
                ["harsh"] = def.Harsh,
                ["needsThird"] = def.NeedsThird,
                ["thirdLabel"] = def.ThirdLabel,
                ["hasAmounts"] = def.Amounts.Length > 0,
            };
            if (def.NeedsThird)
            {
                // 세 번째 사람을 고를 수 있을 때만 보인다. 확률은 고른 뒤에 보인다.
                if (ThirdCandidates(k, actor, target).Count == 0) continue;
                e["chance"] = -1f;
            }
            else
            {
                if (!def.Can(w, c)) continue;
                e["chance"] = def.Accept(w, c);
            }
            if (def.Id == InteractionId.SexTrade) e["price"] = w.SexPrice(actor, target);
            if (def.Id == InteractionId.Affair) e["risk"] = w.AffairRisk(actor, target);
            if (def.Id is InteractionId.Steal or InteractionId.Assault or InteractionId.Extort or InteractionId.Murder)
            {
                var (caught, punish) = w.CrimeOutlook(def.Id, actor, target);
                e["crime"] = true;
                e["caught"] = caught;
                e["punish"] = punish;
                if (def.Id == InteractionId.Steal) e["take"] = w.TheftAmount(actor, target);
            }
            a.Add(e);
        }
        return a;
    }

    /// <summary>
    /// 세 번째 사람 후보: 험담·설득은 행동하는 사람이 아는 사람(관계 칸), 혼담은 내 집의 혼인할 사람.
    /// 저마다 받아들일 확률(값이 있으면 가장 작은 값으로)을 붙인다.
    /// </summary>
    public Godot.Collections.Array ThirdCandidates(int id, int actor, int target)
    {
        var a = new Godot.Collections.Array();
        var w = _w!; var p = w.People; var hh = w.Households;
        var def = w.Def((InteractionId)id);
        var cands = new List<int>();
        if (def.Id is InteractionId.ArrangeMatch or InteractionId.ForcedMatch or InteractionId.Apprentice)
        {
            int h = p.Household[actor];
            if (hh.IsAlive(h)) cands.AddRange(w.MembersOf(h));
        }
        else
        {
            var byUid = new Dictionary<int, int>();
            for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i)) byUid[p.Uid[i]] = i;
            var scored = new List<(int i, float s)>();
            for (int k = 0; k < PersonStore.RelSlots; k++)
            {
                int sl = actor * PersonStore.RelSlots + k;
                if (p.RelUid[sl] >= 0 && byUid.TryGetValue(p.RelUid[sl], out int t)) scored.Add((t, MathF.Abs(p.RelAff[sl])));
            }
            scored.Sort((x, y) => y.s.CompareTo(x.s));
            foreach (var (t, _) in scored.Take(10)) cands.Add(t);
        }
        float amount0 = def.Amounts.Length > 0 ? def.Amounts[0] : 0f;
        foreach (int t in cands)
        {
            var c = new InteractionContext { Actor = actor, Target = target, Third = t, Amount = amount0 };
            if (t == actor || t == target || !def.Can(w, c)) continue;
            a.Add(new Godot.Collections.Dictionary
            {
                ["i"] = t,
                ["name"] = Names.Full(w.Lineage, p.Uid[t]),
                ["sub"] = $"{Age(t)}세 · {OpinionWord(Social.Opinion(p, actor, p.Uid[t]))}",
                ["chance"] = def.Accept(w, c),
            });
        }
        return a;
    }

    private static string OpinionWord(float v) => v < -30 ? "미워함" : v < -8 ? "꺼림" : v < 8 ? "그저 그럼" : v < 30 ? "좋게 봄" : "아주 좋아함";

    /// <summary>값 고르기(지참금 등): 낼 수 있는 값마다 받아들일 확률.</summary>
    public Godot.Collections.Array AmountOptions(int id, int actor, int target, int third)
    {
        var a = new Godot.Collections.Array();
        var w = _w!;
        var def = w.Def((InteractionId)id);
        for (int k = 0; k < def.Amounts.Length; k++)
        {
            var c = new InteractionContext { Actor = actor, Target = target, Third = third, Amount = def.Amounts[k] };
            if (!def.Can(w, c)) continue;
            a.Add(new Godot.Collections.Dictionary { ["amount"] = def.Amounts[k], ["name"] = def.AmountNames[k], ["chance"] = def.Accept(w, c) });
        }
        return a;
    }

    public bool HasAmounts(int id) => _w != null && _w.Def((InteractionId)id).Amounts.Length > 0;

    public bool AutoMarry
    {
        get => _w?.PlayerAutoMarry ?? true;
        set { if (_w != null) _w.PlayerAutoMarry = value; }
    }

    /// <summary>상호작용을 한다. 결과를 연대기에 적고 글로 돌려준다.</summary>
    public string Interact(int id, int actor, int target, int third = -1, float amount = 0f)
    {
        if (!Ready) return "";
        var w = _w!; var p = w.People;
        var def = w.Def((InteractionId)id);
        string an = Names.Full(w.Lineage, p.Uid[actor]), tn = Names.Full(w.Lineage, p.Uid[target]);
        string about = third >= 0 && p.IsAliveIndex(third) ? $"({Names.Full(w.Lineage, p.Uid[third])}" + (amount > 0 ? $", 식량 {amount:0})" : ")") : "";
        w.LastCrime = null;   // 이번 행동이 만든 사건만 본다
        var r = w.Interact((InteractionId)id, new InteractionContext { Actor = actor, Target = target, Third = third, Amount = amount });
        string outcome = r.Accepted ? "받아들였다" : "거절당했다";
        if (def.Id == InteractionId.Enfeoff && r.Accepted) outcome = $"집 {r.Amount:0}곳을 맡겼다";
        else if (def.Id == InteractionId.Revoke) outcome = r.Accepted ? "집을 돌려받았다" : "거부하고 떨어져 나갔다";
        else if (def.Id is InteractionId.Steal or InteractionId.Assault or InteractionId.Extort or InteractionId.Murder)
        {
            var last = w.LastCrime;
            outcome = r.Accepted ? "해냈다" + (r.Amount > 0.5f ? $"(식량 {r.Amount:0})" : "") : "실패했다";
            if (last != null && last.Culprit == p.Uid[actor])
                outcome += last.Verdict switch
                {
                    Verdict.Fine or Verdict.Flog or Verdict.Execute => $" · 잡혀서 {World.VerdictName(last.Verdict)}",
                    Verdict.Escaped => " · 신고되었지만 잡히지 않았다",
                    Verdict.Covered => " · 지배자가 덮어 주었다",
                    Verdict.Pending => " · 잡혀서 판결을 기다린다",
                    _ => last.VictimKnows ? " · 상대가 나를 알아보았다" : "",
                };
        }
        if (def.Id is InteractionId.Steal or InteractionId.Assault or InteractionId.Extort or InteractionId.Murder)
        {
            // 범죄는 무엇을 했는지 동사로 쓴다. 사건이 생겼으면 연대기에는 이미 적혔다
            var last = w.LastCrime;
            if (last != null && last.Culprit == p.Uid[actor])
                return $"{World.Subj(an)} {World.DidText(last.Type, tn, last.Attempt)}" + (r.Accepted && r.Amount > 0.5f ? $"(식량 {r.Amount:0})" : "") + outcome[(outcome.IndexOf(" · ") is int k && k >= 0 ? k : outcome.Length)..];
            string none = $"{World.Subj(an)} {tn}에게 {World.Obj(def.Name)} 해 보려 했지만 틈이 없었다";
            w.Chronicle.Add((w.Day, none));
            return none;
        }
        string text = $"{World.Subj(an)} {tn}에게 {def.Name}{about}: " + outcome + (r.Pregnant ? " (아이가 들어섰다)" : "");
        w.Chronicle.Add((w.Day, text));
        return text;
    }

    // ------------------------------------------------------------------ 조직 창

    /// <summary>
    /// 내 집과 이어진 조직들(내 집의 군대, 식구가 들거나 이끄는 모임)의 맨 위부터 갈래까지.
    /// 줄마다 깊이, 이름, 우두머리, 사람 수, 힘, 맡은 일, 내가 부릴 수 있는지.
    /// </summary>
    public Godot.Collections.Array OrgPanel()
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People;
        int h = w.PlayerHousehold;
        if (!w.Households.IsAlive(h)) return a;
        w.RecountOrgs();
        var mine = w.MembersOf(h).ToList();
        var mineUids = new HashSet<int>(mine.Select(i => p.Uid[i]));
        var roots = new List<Group>();
        void AddRoot(Group g) { var r = w.RootOf(g); if (!roots.Contains(r)) roots.Add(r); }
        var lo = w.LordOrg(h, false);
        if (lo != null) AddRoot(lo);
        foreach (int i in mine) foreach (var g in w.GroupsOf(i)) AddRoot(g);
        foreach (var root in roots)
        {
            bool command = mineUids.Contains(root.LeaderUid) || root.Home == h;
            // 권위는 우리 집 사람이 아는 만큼. 완전 은폐 조직의 조직원은 자기 갈래만 안다
            float known = mine.Count > 0 ? mine.Max(i => w.PerceivedAuthority(i, root)) : 0f;
            bool onlyOwn = !command && root.Visibility == Visibility.Hidden;
            var ownUnits = mine.SelectMany(i => w.GroupsOf(i)).Where(g => w.RootOf(g).Id == root.Id).Distinct().ToList();
            void Walk(Group g, int depth)
            {
                int leader = FindAlive(g.LeaderUid);
                bool cmd = command && w.RootOf(g).Id == root.Id;   // 내 군대 아래 붙은 남의 조직은 부리지 못한다
                var act = LordActOf(g, cmd, leader, h);
                var tasks = string.Join(", ", g.Tasks.OrderByDescending(t => t.Weight).Select(t => $"{World.VerbName(t.Verb)}({VillageName(g.Village, t.Village)})"));
                a.Add(new Godot.Collections.Dictionary
                {
                    ["id"] = g.Id,
                    ["depth"] = depth,
                    ["label"] = w.UnitLabel(g),
                    ["name"] = g.Name,
                    ["leader"] = leader >= 0 ? Names.Full(w.Lineage, g.LeaderUid) : "우두머리 없음",
                    ["members"] = g.Members,
                    ["total"] = g.Total,
                    ["strength"] = g.Strength,
                    ["tasks"] = tasks == "" ? "맡은 일 없음" : tasks,
                    ["manual"] = g.Manual,
                    ["named"] = g.NameFixed,
                    ["visibility"] = depth == 0 ? (int)g.Visibility : -1,
                    ["visibilityName"] = World.VisibilityName(g.Visibility),
                    ["authority"] = depth == 0 ? known : 0f,
                    ["leak"] = g.Leak,
                    ["inner"] = g.Inner.Count,
                    ["lesson"] = World.LessonName(g.Lesson),
                    ["teachShare"] = g.TeachShare,
                    ["heir"] = g.HeirUid >= 0 && FindAlive(g.HeirUid) >= 0 ? Names.Full(w.Lineage, g.HeirUid) : "",
                    ["inherit"] = g.Inherit,
                    ["joinRule"] = (int)g.JoinRule,
                    ["shareRule"] = (int)g.ShareRule,
                    ["treasury"] = g.Treasury,
                    ["command"] = cmd,
                    ["canSplit"] = cmd && g.Members >= 9,
                    ["attached"] = World.IsAttached(g),
                    ["identity"] = depth == 0 && g.IdentityName.Length > 0 ? g.IdentityName : World.GroupKindName(g.Kind),
                    ["shares"] = depth == 0 ? string.Join(" · ", w.KindShares(g).Where(x => x.share >= 0.05f).Select(x => $"{World.GroupKindName(x.kind)} {(int)MathF.Round(x.share * 100f)}%")) : "",
                    ["vacant"] = w.NoCommander(g),
                    ["cmdFactor"] = g.CmdFactor,
                    ["over"] = g.Over,
                    ["grip"] = g.Grip,
                    ["treasurer"] = depth == 0 && g.TreasurerUid >= 0 ? Names.Full(w.Lineage, g.TreasurerUid) : "",
                    ["liaison"] = depth == 0 && g.LiaisonUid >= 0 ? Names.Full(w.Lineage, g.LiaisonUid) : "",
                    ["survey"] = depth == 0 ? w.SurveyOf(g) : 0f,
                    ["school"] = World.IsSchool(g),
                    ["faith"] = World.IsTop(g) && w.IsFaith(g),
                    ["producer"] = g.Tasks.Any(t => t.Verb == Verb.Produce),
                    ["fullTime"] = g.FullTime,
                    ["arms"] = w.ArmsCov(g),
                    ["threat"] = depth == 0 ? w.ThreatTo(g, out _) : 0f,
                    ["visForce"] = depth == 0 ? w.VisibleForce(g) : 0f,
                    ["condition"] = depth == 0 ? World.ConditionName(w.ConditionOf(g)) : "없음",
                    ["lordAct"] = act?.name ?? "",
                    ["lordChance"] = act?.chance ?? 0f,
                    ["mineIn"] = mine.Any(i => w.InGroup(i, g)),
                });
                foreach (var c in w.ChildrenOf(g).ToList()) Walk(c, depth + 1);
            }
            if (onlyOwn) foreach (var u in ownUnits) WalkOne(u);
            else Walk(root, 0);
            // 완전 은폐: 내 갈래 한 줄만(위아래 갈래는 모른다)
            void WalkOne(Group g)
            {
                int leader = FindAlive(g.LeaderUid);
                a.Add(new Godot.Collections.Dictionary
                {
                    ["id"] = g.Id, ["depth"] = 0, ["label"] = w.UnitLabel(g), ["name"] = g.Name,
                    ["leader"] = leader >= 0 ? Names.Full(w.Lineage, g.LeaderUid) : "우두머리 없음",
                    ["members"] = g.Members, ["total"] = g.Members, ["strength"] = g.OwnStrength,
                    ["tasks"] = string.Join(", ", g.Tasks.OrderByDescending(t => t.Weight).Select(t => $"{World.VerbName(t.Verb)}({VillageName(g.Village, t.Village)})")) is { Length: > 0 } ts ? ts : "맡은 일 없음",
                    ["manual"] = g.Manual, ["named"] = g.NameFixed, ["visibility"] = -1, ["visibilityName"] = World.VisibilityName(root.Visibility),
                    ["authority"] = known, ["leak"] = 0f, ["inner"] = 0, ["lesson"] = World.LessonName(g.Lesson), ["teachShare"] = g.TeachShare,
                    ["heir"] = "", ["inherit"] = 0f, ["joinRule"] = (int)root.JoinRule, ["shareRule"] = (int)root.ShareRule, ["treasury"] = 0f,
                    ["command"] = false, ["canSplit"] = false, ["mineIn"] = true, ["attached"] = false, ["lordAct"] = "", ["lordChance"] = 0f,
                    ["identity"] = World.GroupKindName(g.Kind), ["shares"] = "", ["vacant"] = false, ["cmdFactor"] = 1f, ["over"] = 0f, ["grip"] = 0f,
                    ["treasurer"] = "", ["liaison"] = "", ["survey"] = 0f, ["school"] = false, ["faith"] = false, ["producer"] = false, ["fullTime"] = false, ["arms"] = 0f, ["threat"] = 0f, ["visForce"] = 0f, ["condition"] = "없음",
                });
            }
        }
        return a;
    }

    /// <summary>
    /// 조직 줄에서 지배자와 얽힌 수 하나: 내가 이끄는 홀로 선 조직은 바치기, 붙은 조직은 떨어져 나가기,
    /// 내 군대 아래 붙은 남의 조직은 누르기. (상호작용, 하는 사람, 받는 사람, 이름, 확률)
    /// </summary>
    private (InteractionId id, int actor, int target, string name, float chance)? LordActOf(Group g, bool cmd, int leader, int h)
    {
        var w = _w!; var hh = w.Households;
        if (leader < 0 || !World.IsTop(g) || g.Home >= 0) return null;
        (InteractionId, int, int)? pick = null;
        if (cmd)
        {
            int lord = World.IsAttached(g) ? g.Sanction : w.PatronOf(w.People.Household[leader]);
            if (lord >= 0 && hh.IsAlive(lord) && w.People.IsValid(hh.Head[lord]))
                pick = (World.IsAttached(g) ? InteractionId.OrgBreak : InteractionId.OrgPledge, leader, hh.Head[lord].Index);
        }
        else if (World.IsAttached(g) && g.Sanction == h && w.People.IsValid(hh.Head[h])) pick = (InteractionId.OrgSuppress, hh.Head[h].Index, leader);
        if (pick is not { } x) return null;
        var c = new InteractionContext { Actor = x.Item2, Target = x.Item3, Third = -1 };
        var def = w.Def(x.Item1);
        if (x.Item2 == x.Item3 || !def.Can(w, c)) return null;
        return (x.Item1, x.Item2, x.Item3, def.Name, def.Accept(w, c));
    }

    /// <summary>조직 줄의 지배자 수를 한다. 결과 글(없으면 빈 글).</summary>
    public string OrgLordAct(int id)
    {
        if (!Ready) return "";
        var w = _w!;
        var g = w.GroupById(id);
        int h = w.PlayerHousehold;
        if (g == null || !w.Households.IsAlive(h)) return "";
        int leader = FindAlive(g.LeaderUid);
        bool mine = leader >= 0 && w.People.Household[leader] == h;
        if (LordActOf(g, mine, leader, h) is not { } a) return "";
        var r = w.Interact(a.id, new InteractionContext { Actor = a.actor, Target = a.target, Third = -1 });
        return r.Accepted ? "뜻대로 되었다" : "뜻대로 되지 않았다";
    }

    private int FindAlive(int uid)
    {
        var p = _w!.People;
        for (int i = 0; i < p.HighWater; i++) if (p.IsAliveIndex(i) && p.Uid[i] == uid) return i;
        return -1;
    }

    /// <summary>마을 이름: 번호와, 기준 마을에서 본 방향·거리.</summary>
    private string VillageName(int from, int v)
    {
        var vl = _w!.VillageList;
        if (v < 0 || v >= vl.Count) return "마을 없음";
        if (v == from) return "제 마을";
        if (from < 0 || from >= vl.Count) return $"{v}번 마을";
        int dx = vl.CenterX[v] - vl.CenterX[from], dy = vl.CenterY[v] - vl.CenterY[from];
        int d = (int)MathF.Sqrt(dx * dx + dy * dy);
        string dir = MathF.Abs(dx) > 2 * MathF.Abs(dy) ? (dx > 0 ? "동" : "서") : MathF.Abs(dy) > 2 * MathF.Abs(dx) ? (dy > 0 ? "남" : "북")
                   : (dy > 0 ? "남" : "북") + (dx > 0 ? "동" : "서");
        return $"{v}번 마을, {dir}쪽 {d}칸";
    }

    private Group? Commandable(int id)
    {
        if (!Ready) return null;
        var w = _w!; var p = w.People;
        var g = w.GroupById(id);
        int h = w.PlayerHousehold;
        if (g == null || !w.Households.IsAlive(h)) return null;
        var root = w.RootOf(g);
        if (root.Home == h) return g;
        foreach (int i in w.MembersOf(h)) if (p.Uid[i] == root.LeaderUid) return g;
        return null;
    }

    /// <summary>갈래를 쪼갠다(바로 딸린 사람의 절반). 새 갈래 번호, 못 쪼개면 -1.</summary>
    public int SplitOrg(int id)
    {
        var g = Commandable(id);
        var c = g != null ? _w!.SplitUnit(g) : null;
        if (c == null) return -1;
        _w!.RecountOrgs();
        _w!.Chronicle.Add((_w.Day, $"{g!.Name}에서 {World.Obj(c.Name)} 떼어 냈다"));
        return c.Id;
    }

    /// <summary>맡길 수 있는 일: (일, 마을, 이름). 지킴·쫓기·돌봄·털기를 가까운 마을 여섯 곳에.</summary>
    public Godot.Collections.Array OrgTaskTargets(int id)
    {
        var a = new Godot.Collections.Array();
        var g = Commandable(id);
        if (g == null) return a;
        var vl = _w!.VillageList;
        int from = g.Village;
        var vs = new List<(int v, float d)>();
        for (int v = 0; v < vl.Count; v++)
        {
            if (!vl.Alive[v]) continue;
            float d = from >= 0 && from < vl.Count ? MathF.Sqrt(MathF.Pow(vl.CenterX[v] - vl.CenterX[from], 2) + MathF.Pow(vl.CenterY[v] - vl.CenterY[from], 2)) : 0f;
            vs.Add((v, d));
        }
        vs.Sort((x, y) => x.d.CompareTo(y.d));
        var root = _w.RootOf(g);
        var verbs = new[] { Verb.Guard, Verb.Chase, Verb.Charity, Verb.Preach, Verb.Recruit, Verb.Collect, Verb.Trade, Verb.Escort,
                            Verb.Mercenary, Verb.Brothel, Verb.Agitate, Verb.Raid, Verb.Kidnap };
        foreach (var verb in verbs)
        {
            bool harsh = verb is Verb.Raid or Verb.Kidnap or Verb.Agitate or Verb.Collect;
            if (!_w.CanUseVerb(root, verb, out var why))
            {
                // 잠긴 일도 보여 준다(무엇이 있어야 열리는지)
                a.Add(new Godot.Collections.Dictionary
                {
                    ["verb"] = (int)verb, ["village"] = -1, ["verbName"] = World.VerbName(verb), ["place"] = "", ["houses"] = 0, ["harsh"] = harsh, ["locked"] = why,
                });
                continue;
            }
            // 용병은 계약할 곳을 스스로 찾으니 마을을 고르지 않는다
            foreach (var (v, _) in verb == Verb.Mercenary ? vs.Take(1) : vs.Take(4))
                a.Add(new Godot.Collections.Dictionary
                {
                    ["verb"] = (int)verb, ["village"] = v, ["verbName"] = World.VerbName(verb),
                    ["place"] = verb == Verb.Mercenary ? "계약할 곳을 찾는다" : VillageName(from, v), ["houses"] = vl.Households[v], ["harsh"] = harsh, ["locked"] = "",
                });
        }
        return a;
    }

    public bool SetOrgTask(int id, int verb, int village)
    {
        var g = Commandable(id);
        if (g == null) return false;
        _w!.SetUnitTask(g, (Verb)verb, village);
        _w.Chronicle.Add((_w.Day, $"{g.Name}에 {World.VerbName((Verb)verb)}({VillageName(g.Village, village)}) 일을 맡겼다"));
        return true;
    }

    /// <summary>회칙: 누가 들어오나(0 아무나, 1 혈족만, 2 신앙 깊은 자만), 어떻게 나누나(0 우두머리, 1 고르게, 2 곳간).</summary>
    public bool SetOrgRules(int id, int join, int share)
    {
        var g = Commandable(id);
        if (g == null) return false;
        var root = _w!.RootOf(g);
        root.JoinRule = (JoinRule)Math.Clamp(join, 0, 2);
        root.ShareRule = (ShareRule)Math.Clamp(share, 0, 2);
        _w.Chronicle.Add((_w.Day, $"{root.Name}의 회칙: {World.JoinRuleName(root.JoinRule)}, 번 것은 {World.ShareRuleName(root.ShareRule)}"));
        return true;
    }

    /// <summary>맨 위 조직의 알려진 정도를 고른다(0 완전 은폐 ~ 4 완전 공개).</summary>
    public bool SetOrgVisibility(int id, int v)
    {
        var g = Commandable(id);
        if (g == null || v < 0 || v > 4) return false;
        _w!.SetVisibility(g, (Visibility)v);
        _w.Chronicle.Add((_w.Day, $"{World.Obj(_w.RootOf(g).Name)} {World.Ro($"'{World.VisibilityName((Visibility)v)}'")} 두었다"));
        return true;
    }

    /// <summary>조직 이름을 짓는다. 빈 글이면 저절로 붙는 이름으로 돌아간다.</summary>
    public bool RenameOrg(int id, string name)
    {
        var g = Commandable(id);
        if (g == null) return false;
        string old = g.Name;
        _w!.RenameGroup(g, name);
        if (g.Name != old) _w.Chronicle.Add((_w.Day, $"{old}의 이름을 {World.Ro(g.Name)} 지었다"));
        return true;
    }

    /// <summary>갈래가 가르칠 것(0 없음, 1 무예, 2 글, 3 장사, 4 공예, 5 교리, 6 이음)과 몫(0~30%).</summary>
    public bool SetUnitLesson(int id, int lesson, int pct)
    {
        var g = Commandable(id);
        if (g == null) return false;
        g.Lesson = (Lesson)Math.Clamp(lesson, 0, 6);
        g.TeachShare = g.Lesson == Lesson.None ? 0f : Math.Clamp(pct, 0, 30) / 100f;
        g.TeachManual = true;
        _w!.Chronicle.Add((_w.Day, $"{g.Name}: 조직원 시간의 {(int)(g.TeachShare * 100)}%를 {World.LessonName(g.Lesson)} 가르치기에 쓴다"));
        return true;
    }

    /// <summary>앉힐 수 있는 사람: 그 조직 나무의 어른. (번호, 이름, 한 줄 설명)</summary>
    public Godot.Collections.Array OrgPeople(int id)
    {
        var a = new Godot.Collections.Array();
        var g = Commandable(id);
        if (g == null) return a;
        var w = _w!; var p = w.People;
        var root = w.RootOf(g);
        var seen = new HashSet<int>();
        foreach (var u in w.Groups.Where(x => x.Alive && w.RootOf(x).Id == root.Id))
            foreach (int m in w.Members(u))
            {
                if (!seen.Add(m) || Age(m) < 16 || p.Uid[m] == root.LeaderUid) continue;
                a.Add(new Godot.Collections.Dictionary
                {
                    ["i"] = m, ["name"] = Names.Full(w.Lineage, p.Uid[m]),
                    ["note"] = $"{u.Name} · 지휘 범위 {w.CommandSpan(m)} · 글 {p.GetSkill(m, Skill.Letters):0.0} · 무예 {p.GetSkill(m, Skill.Arms):0.0} · 눈치 {p.Get(m, Trait.Perception):0.0} · 우두머리를 보는 눈 {Social.Opinion(p, m, root.LeaderUid):+0;-0}",
                    ["here"] = u.Id == g.Id,
                });
                if (a.Count >= 40) return a;
            }
        return a;
    }

    /// <summary>직책에 앉힌다. role: 0 지휘자(그 갈래), 1 곳간지기, 2 연락책. person -1 이면 비운다.</summary>
    public bool Appoint(int id, int role, int person)
    {
        var g = Commandable(id);
        if (g == null) return false;
        var w = _w!;
        bool ok = role == 0 ? w.AppointCommander(g, person) : w.AppointRole(g, role - 1, person);
        if (ok)
        {
            string who = person >= 0 ? Names.Full(w.Lineage, w.People.Uid[person]) : "아무도";
            string what = role == 0 ? $"{g.Name}의 지휘자" : role == 1 ? "곳간지기" : "연락책";
            w.Chronicle.Add((w.Day, person >= 0 ? $"{World.Obj(who)} {what}로 앉혔다" : $"{what} 자리를 비웠다"));
        }
        return ok;
    }

    /// <summary>갈래의 일 비중 고르기: 일마다 지금 무게(0~100)와 잠긴 까닭.</summary>
    public Godot.Collections.Array OrgWeights(int id)
    {
        var a = new Godot.Collections.Array();
        var g = Commandable(id);
        if (g == null) return a;
        var w = _w!;
        var root = w.RootOf(g);
        foreach (var v in new[] { Verb.Guard, Verb.Chase, Verb.Train, Verb.Survey, Verb.Teach, Verb.Preach, Verb.Charity, Verb.Trade, Verb.Escort,
                                  Verb.Mercenary, Verb.Recruit, Verb.Collect, Verb.Brothel, Verb.Agitate, Verb.Raid, Verb.Kidnap })
        {
            bool open = w.CanUseVerb(root, v, out var why);
            float cur = g.Tasks.Where(t => t.Verb == v).Sum(t => t.Weight);
            a.Add(new Godot.Collections.Dictionary
            {
                ["verb"] = (int)v, ["name"] = World.VerbName(v), ["weight"] = (int)MathF.Round(cur * 100f), ["locked"] = open ? "" : why,
                ["harsh"] = v is Verb.Raid or Verb.Kidnap or Verb.Agitate or Verb.Collect,
            });
        }
        return a;
    }

    /// <summary>갈래의 일 비중을 정한다. pairs: [일, 무게(0~100)] 목록.</summary>
    public bool SetOrgWeights(int id, Godot.Collections.Array pairs)
    {
        var g = Commandable(id);
        if (g == null) return false;
        var w = _w!;
        var root = w.RootOf(g);
        var list = new List<(Verb, float)>();
        foreach (var o in pairs)
        {
            var e = o.AsGodotArray();
            var v = (Verb)(int)e[0];
            float wt = (float)e[1];
            if (wt > 0f && w.CanUseVerb(root, v, out _)) list.Add((v, wt));
        }
        w.SetUnitWeights(g, list);
        w.Chronicle.Add((w.Day, $"{g.Name}의 일: {string.Join(", ", g.Tasks.Select(t => $"{World.VerbName(t.Verb)} {(int)MathF.Round(t.Weight * 100f)}%"))}"));
        return true;
    }

    /// <summary>학교 설정과 지금(가르침 일이 있는 갈래만). 비면 학교가 아니다.</summary>
    public Godot.Collections.Dictionary SchoolInfo(int id)
    {
        var e = new Godot.Collections.Dictionary();
        var g = Commandable(id);
        if (g == null || !World.IsSchool(g)) return e;
        var w = _w!;
        var plan = w.PlanOf(g);
        var (st, te, cap) = w.SchoolCount(g);
        var weights = new Godot.Collections.Array();
        foreach (var l in World.SchoolSubjects)
            weights.Add(new Godot.Collections.Dictionary { ["lesson"] = (int)l, ["name"] = World.LessonName(l), ["weight"] = (int)MathF.Round(plan.Weights[(int)l] * 100f), ["cap"] = World.DepthCap(plan.Weights[(int)l]) });
        var units = new Godot.Collections.Array();
        var root = w.RootOf(g);
        foreach (var u in w.Groups.Where(x => x.Alive && x.Id != g.Id && w.RootOf(x).Id == root.Id))
            units.Add(new Godot.Collections.Dictionary { ["id"] = u.Id, ["name"] = u.Name });
        e["minAge"] = plan.MinAge; e["maxAge"] = plan.MaxAge; e["maxLetters"] = plan.MaxLetters; e["talentMin"] = plan.TalentMin;
        e["outsiders"] = plan.Outsiders; e["years"] = plan.Years; e["fee"] = plan.Fee; e["dest"] = (int)plan.Dest; e["destUnit"] = plan.DestUnit;
        e["meritLine"] = plan.MeritLine; e["serviceYears"] = plan.ServiceYears; e["weights"] = weights; e["units"] = units;
        e["students"] = st; e["teachers"] = te; e["capacity"] = cap; e["graduated"] = plan.Graduated; e["enrolled"] = plan.Enrolled; e["fled"] = plan.Fled;
        return e;
    }

    /// <summary>학교 설정을 바꾼다. d 의 열쇠는 SchoolInfo 와 같다(weights 는 lesson → 0~100).</summary>
    public bool SetSchool(int id, Godot.Collections.Dictionary d)
    {
        var g = Commandable(id);
        if (g == null || !World.IsSchool(g)) return false;
        var w = _w!;
        var plan = new SchoolPlan
        {
            MinAge = (int)d["minAge"], MaxAge = (int)d["maxAge"], MaxLetters = (float)d["maxLetters"], TalentMin = (float)d["talentMin"],
            Outsiders = (bool)d["outsiders"], Years = (int)d["years"], Fee = (float)d["fee"], Dest = (SchoolDest)(int)d["dest"], DestUnit = (int)d["destUnit"],
            MeritLine = (float)d["meritLine"], ServiceYears = (int)d["serviceYears"],
        };
        var ws = d["weights"].AsGodotDictionary();
        foreach (var l in World.SchoolSubjects) if (ws.ContainsKey((int)l)) plan.Weights[(int)l] = (float)ws[(int)l];
        w.SetPlan(g, plan);
        var pl = w.PlanOf(g);
        string dest = pl.Dest switch
        {
            SchoolDest.Unit => $"{w.GroupById(pl.DestUnit)?.Name ?? "정한 조직"}으로",
            SchoolDest.Merit => $"성적이 {pl.MeritLine:0.0} 넘으면 {w.GroupById(pl.DestUnit)?.Name ?? "정한 조직"}으로",
            SchoolDest.Service => $"{w.GroupById(pl.DestUnit)?.Name ?? "정한 조직"}에서 {pl.ServiceYears}년 복무",
            SchoolDest.Teacher => "교사로 남음", _ => "세력원으로",
        };
        w.Chronicle.Add((w.Day, $"{g.Name}: {pl.MinAge}~{pl.MaxAge}세, {pl.Years}년, {string.Join("·", World.SchoolSubjects.Where(l => pl.Weights[(int)l] > 0f).Select(l => $"{World.LessonName(l)} {(int)MathF.Round(pl.Weights[(int)l] * 100f)}%"))}, 졸업 뒤 {dest}"));
        return true;
    }

    /// <summary>생산 갈래: 만드는 것(고를 수 있는 것과 잠긴 까닭)과 공급 선(보낼 곳과 비중).</summary>
    public Godot.Collections.Dictionary ProductionInfo(int id)
    {
        var e = new Godot.Collections.Dictionary();
        var g = Commandable(id);
        if (g == null) return e;
        var w = _w!;
        var root = w.RootOf(g);
        bool shop = w.Facilities.Any(f => f.Owner == root.Id && f.Kind == FacilityKind.Workshop && f.Assigned == g.Id && f.Built >= World.FacilityCost(f.Kind).years);
        var goods = new Godot.Collections.Array();
        foreach (var x in new[] { Good.Tools, Good.Cloth, Good.Fine, Good.Build, Good.Arms })
            goods.Add(new Godot.Collections.Dictionary { ["good"] = (int)x, ["name"] = World.GoodName(x), ["locked"] = x == Good.Arms && !shop ? "공방 시설을 이 갈래에 배정해야" : "" });
        var targets = new Godot.Collections.Array();
        void T(int tid, string name, string what) => targets.Add(new Godot.Collections.Dictionary
        {
            ["target"] = tid, ["name"] = name, ["what"] = what, ["weight"] = (int)MathF.Round(g.Supply.Where(x => x.target == tid).Sum(x => x.weight) * 100f / MathF.Max(0.0001f, g.Supply.Sum(x => x.weight))),
        });
        T(-2, "시장", "직판: 15% 덜 받는다");
        T(-1, "세력 곳간", "비축");
        foreach (var u in w.Groups.Where(x => x.Alive && x.Id != g.Id && w.RootOf(x).Id == root.Id))
            T(u.Id, u.Name, u.Tasks.Any(t => t.Verb == Verb.Trade) ? "납품: 장사 갈래가 판다" : World.IsSchool(u) ? "비품: 배우는 속도 +10%" : "보급: 무구는 갈래가, 다른 것은 조직원 집이");
        e["product"] = (int)g.Product; e["manual"] = g.ProductManual; e["goods"] = goods; e["targets"] = targets;
        e["store"] = string.Join(", ", Enumerable.Range(0, World.GoodCount).Where(k => root.Store[k] >= 0.5f).Select(k => $"{World.GoodName((Good)k)} {root.Store[k]:0}"));
        return e;
    }

    /// <summary>만드는 것과 공급 선을 정한다. lines: [보낼 곳, 비중(0~100)].</summary>
    public bool SetProduction(int id, int product, Godot.Collections.Array lines)
    {
        var g = Commandable(id);
        if (g == null) return false;
        g.Product = (Good)Math.Clamp(product, 0, World.GoodCount - 1);
        g.ProductManual = true;
        g.Supply.Clear();
        foreach (var o in lines)
        {
            var a = o.AsGodotArray();
            float wt = (float)a[1];
            if (wt > 0f) g.Supply.Add(((int)a[0], wt));
        }
        _w!.Chronicle.Add((_w.Day, $"{g.Name}: {World.Obj(World.GoodName(g.Product))} 만든다"));
        return true;
    }

    /// <summary>상근을 켜고 끈다.</summary>
    public bool SetFullTime(int id, bool on)
    {
        var g = Commandable(id);
        if (g == null || g.Home >= 0) return false;
        g.FullTime = on;
        _w!.Chronicle.Add((_w.Day, on ? $"{Topic(g.Name)} 상근으로 돌린다(세력이 먹이고, 집 농사를 하지 않는다)" : $"{Topic(g.Name)} 상근을 푼다"));
        return true;
    }

    private static string Topic(string s) => World.Topic(s);

    /// <summary>세력의 시설과 지을 수 있는 시설.</summary>
    public Godot.Collections.Dictionary FacilityInfo(int id)
    {
        var e = new Godot.Collections.Dictionary();
        var g = Commandable(id);
        if (g == null) return e;
        var w = _w!; var hh = w.Households;
        var root = w.RootOf(g);
        var have = new Godot.Collections.Array();
        foreach (var f in w.Facilities.Where(f => f.Owner == root.Id))
        {
            var (cost, years, upkeep) = World.FacilityCost(f.Kind);
            have.Add(new Godot.Collections.Dictionary
            {
                ["id"] = f.Id, ["name"] = World.FacilityName(f.Kind), ["village"] = $"{f.Village}번 마을",
                ["state"] = f.Built < years ? $"짓는 중 {(int)f.Built}/{years}년" : f.Unpaid > 0 ? $"유지비 못 냄 {f.Unpaid}년" : "다 지음",
                ["assigned"] = w.GroupById(f.Assigned)?.Name ?? "", ["effect"] = World.FacilityEffect(f.Kind), ["upkeep"] = upkeep,
            });
        }
        var build = new Godot.Collections.Array();
        int leader = FindAlive(root.LeaderUid);
        int home = root.Home >= 0 ? root.Home : leader >= 0 ? w.People.Household[leader] : -1;
        float money = root.Treasury + (hh.IsAlive(home) ? hh.Coin[home] + MathF.Max(0f, hh.Food[home] - hh.Need[home] * w.DaysToHarvestPublic) * w.FoodPrice(hh.Village[home]) : 0f);
        foreach (FacilityKind k in Enum.GetValues<FacilityKind>())
        {
            var (cost, years, upkeep) = World.FacilityCost(k);
            build.Add(new Godot.Collections.Dictionary { ["kind"] = (int)k, ["name"] = World.FacilityName(k), ["cost"] = cost, ["years"] = years, ["upkeep"] = upkeep, ["effect"] = World.FacilityEffect(k), ["can"] = money >= cost });
        }
        var units = new Godot.Collections.Array();
        foreach (var u in w.Groups.Where(x => x.Alive && w.RootOf(x).Id == root.Id)) units.Add(new Godot.Collections.Dictionary { ["id"] = u.Id, ["name"] = u.Name });
        e["have"] = have; e["build"] = build; e["units"] = units; e["money"] = money; e["village"] = root.Village;
        return e;
    }

    /// <summary>예산 창: 갈래마다 필요·받은 몫·비중, 지휘자 불만. 감시가 있으면 파벌까지 보인다(없으면 불만이 큰 사람만 낌새로).</summary>
    public Godot.Collections.Dictionary BudgetInfo(int id)
    {
        var e = new Godot.Collections.Dictionary();
        var g = Commandable(id);
        if (g == null) return e;
        var w = _w!;
        var root = w.RootOf(g);
        bool eye = w.SurveyOf(root) > 0f;
        string Mood(int uid) { float v = w.GrievanceOf(root, uid); return eye ? $"불만 {v:0.00}" : v > 0.5f ? "불평이 들린다" : ""; }
        var units = new Godot.Collections.Array();
        foreach (var u in w.Groups.Where(x => x.Alive && (x.Id == root.Id || w.RootOf(x).Id == root.Id && !World.IsTop(x))))
        {
            int cmd = FindAlive(u.LeaderUid);
            units.Add(new Godot.Collections.Dictionary
            {
                ["id"] = u.Id, ["name"] = u.Name, ["need"] = u.Need, ["alloc"] = u.Alloc, ["fill"] = u.Fill, ["weight"] = u.BudgetWeight,
                ["cmd"] = u.Id != root.Id && cmd >= 0 ? Names.Full(w.Lineage, u.LeaderUid) : "", ["mood"] = u.Id != root.Id && cmd >= 0 ? Mood(u.LeaderUid) : "",
            });
        }
        var facs = new Godot.Collections.Array();
        if (eye)
            foreach (var f in w.FactionsOf(root))
            {
                var ms = new Godot.Collections.Array();
                foreach (int uid in f.Members) ms.Add(new Godot.Collections.Dictionary { ["uid"] = uid, ["name"] = Names.Full(w.Lineage, uid), ["mood"] = Mood(uid), ["lead"] = uid == f.Leader });
                facs.Add(new Godot.Collections.Dictionary { ["power"] = f.Power, ["share"] = f.Power / MathF.Max(1f, root.Strength), ["grievance"] = f.Grievance, ["members"] = ms });
            }
        e["units"] = units; e["factions"] = facs; e["eye"] = eye; e["treasury"] = root.Treasury;
        return e;
    }

    /// <summary>신앙 창: 회칙, 신도와 광신도, 믿음 평균, 바치기를 청할 수 있나.</summary>
    public Godot.Collections.Dictionary FaithInfo(int id)
    {
        var e = new Godot.Collections.Dictionary();
        var g = Commandable(id);
        if (g == null) return e;
        var w = _w!; var p = w.People;
        var root = w.RootOf(g);
        if (!w.IsFaith(root)) return e;
        int n = 0, z = 0; float sum = 0f;
        var seen = new HashSet<int>();
        foreach (var u in w.Groups.Where(x => x.Alive && (x.Id == root.Id || w.RootOf(x).Id == root.Id)))
            foreach (int m in w.Members(u))
            {
                if (p.Uid[m] == root.LeaderUid || !seen.Add(p.Uid[m])) continue;
                n++; sum += w.FaithOf(p.Uid[m], root);
                if (w.IsZealot(p.Uid[m], root)) z++;
            }
        e["tithe"] = (int)root.Tithe; e["titheName"] = World.TitheName(root.Tithe);
        e["believers"] = n; e["zealots"] = z; e["faith"] = n > 0 ? sum / n : 0f;
        e["canOffer"] = w.CanCallOffering(root); e["treasury"] = root.Treasury;
        return e;
    }

    public bool SetTithe(int id, int rule)
    {
        var g = Commandable(id);
        if (g == null) return false;
        _w!.SetTithe(g, (TitheRule)rule);
        return true;
    }

    public string CallOffering(int id)
    {
        var g = Commandable(id);
        if (g == null) return "";
        float got = _w!.CallOffering(g);
        return got < 0f ? "바치기는 세 해에 한 번만 청할 수 있다" : $"바치기로 은화 {got:0}을 거두었다";
    }

    public bool SetBudgetWeight(int id, int unit, float weight)
    {
        var g = Commandable(id);
        var u = _w?.GroupById(unit);
        if (g == null || u == null || _w!.RootOf(u).Id != _w.RootOf(g).Id) return false;
        _w.SetBudgetWeight(u, weight);
        return true;
    }

    /// <summary>파벌 사람 하나에게 측근 자리를 주어 떼어 낸다(0) 또는 세력에서 내쫓는다(1).</summary>
    public bool FactionMove(int id, int uid, int move)
    {
        var g = Commandable(id);
        if (g == null) return false;
        return move == 0 ? _w!.Placate(g, uid) : _w!.Expel(g, uid);
    }

    public string BuildFacility(int id, int kind)
    {
        var g = Commandable(id);
        if (g == null) return "";
        var f = _w!.BuildFacility(g, (FacilityKind)kind, _w.RootOf(g).Village);
        return f == null ? "모자라서 지을 수 없다" : $"{World.Obj(World.FacilityName(f.Kind))} 짓기 시작했다";
    }

    public bool AssignFacility(int id, int facility, int unit)
    {
        var g = Commandable(id);
        if (g == null) return false;
        var f = _w!.Facilities.FirstOrDefault(x => x.Id == facility);
        return f != null && _w.AssignFacility(f, unit >= 0 ? _w.GroupById(unit) : null);
    }

    public bool OrgAuto(int id)
    {
        var g = Commandable(id);
        if (g == null) return false;
        _w!.ClearManual(g);
        g.TeachManual = false;
        return true;
    }

    // ------------------------------------------------------------------재판

    public int PendingCount => _w == null ? 0 : _w.PendingCases().Count();

    /// <summary>판결을 기다리는 사건: 번호, 범죄, 범인, 피해자, 증인 수, 범인이 내 사람인지, 날짜.</summary>
    public Godot.Collections.Array Cases()
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People;
        foreach (var c in w.PendingCases())
        {
            int ci = FindAlive(c.Culprit);
            int head = w.Households.IsAlive(w.PlayerHousehold) && p.IsValid(w.Households.Head[w.PlayerHousehold]) ? w.Households.Head[w.PlayerHousehold].Index : -1;
            int chon = head >= 0 ? w.Lineage.Chon(p.Uid[head], c.Culprit) : -1;
            string who = ci >= 0 && p.Household[ci] == w.PlayerHousehold ? "내 식구" : chon > 0 && chon <= 4 ? $"내 {chon}촌" : "";
            a.Add(new Godot.Collections.Dictionary
            {
                ["id"] = c.Id,
                ["crime"] = World.CrimeName(c.Type) + (c.Attempt ? " 시도" : ""),
                ["culprit"] = Names.Full(w.Lineage, c.Culprit),
                ["victim"] = Names.Full(w.Lineage, c.Victim),
                ["witnesses"] = c.Witnesses.Count,
                ["amount"] = c.Amount,
                ["mine"] = who,
                ["canCover"] = w.CanCover(c.Id),
                ["date"] = $"{c.Day / w.Config.DaysPerYear + 1}년",
            });
        }
        return a;
    }

    /// <summary>내 땅(내가 맡은 사건)과 내 식구가 얽힌 최근 사건 열둘: 범죄, 범인(알려졌으면), 피해자, 끝.</summary>
    public Godot.Collections.Array RecentCases()
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People;
        int me = w.PlayerHousehold;
        if (me < 0) return a;
        var mine = me >= 0 ? new HashSet<int>(w.MembersOf(me).Select(i => p.Uid[i])) : new HashSet<int>();
        // 내 땅의 사건은 신고된 것만 안다. 내 식구가 얽힌 일은 식구가 겪었으니 안다
        foreach (var c in w.Crimes.Reverse().Where(c => c.Verdict != Verdict.Pending && (mine.Contains(c.Culprit) || mine.Contains(c.Victim) || (c.Lord == me && c.Reported))).Take(12))
        {
            // 범인을 아는 경우: 잡았거나(벌, 덮음, 풀어 줌), 내 식구가 범인이거나, 내 식구가 당했고 범인을 봤다
            bool known = mine.Contains(c.Culprit) || (mine.Contains(c.Victim) && c.VictimKnows)
                         || (c.Lord == me && c.Verdict is Verdict.Fine or Verdict.Flog or Verdict.Execute or Verdict.Covered or Verdict.Released);
            a.Add(new Godot.Collections.Dictionary
            {
                ["crime"] = World.CrimeName(c.Type) + (c.Attempt ? " 시도" : ""),
                ["culprit"] = known ? Names.Full(w.Lineage, c.Culprit) : "모름",
                ["victim"] = Names.Full(w.Lineage, c.Victim),
                ["verdict"] = World.VerdictName(c.Verdict),
                ["date"] = $"{c.Day / w.Config.DaysPerYear + 1}년",
            });
        }
        return a;
    }

    /// <summary>판결: verdict 는 World.Verdict 번호(벌금 3, 매질 4, 처형 5, 덮음 6, 풀어 줌 7).</summary>
    public bool JudgeCase(int id, int verdict) => _w != null && _w.PlayerJudge(id, (Verdict)verdict);

    public int Severity
    {
        get => _w?.PlayerSeverity ?? 1;
        set { if (_w != null) _w.PlayerSeverity = Math.Clamp(value, 0, 2); }
    }

    public bool JudgeAll
    {
        get => _w?.PlayerJudgesAll ?? false;
        set { if (_w != null) _w.PlayerJudgesAll = value; }
    }

    public int ClientsOf(int h) => _w?.ClientCount(h) ?? 0;

    /// <summary>따르는 집이 가장 많은 집(자동 점검에서 지배자 집을 맡아 볼 때). 없으면 -1.</summary>
    public int BiggestLord()
    {
        if (!Ready) return -1;
        var w = _w!;
        int best = -1;
        for (int h = 0; h < w.Households.HighWater; h++)
            if (w.Households.IsAlive(h) && w.ClientCount(h) > 0 && (best < 0 || w.ClientCount(h) > w.ClientCount(best))) best = h;
        return best;
    }

    // ------------------------------------------------------------------ 오름의 다툼

    public int DemandCount => _w == null ? 0 : _w.Demands.Count();

    private static readonly InteractionId[] LordMoves = { InteractionId.RiseCoopt, InteractionId.RiseWarn, InteractionId.RiseSway, InteractionId.RiseOath, InteractionId.RiseSlander, InteractionId.RiseSuppress, InteractionId.RiseInvest };
    private static readonly InteractionId[] RiserMoves = { InteractionId.RiseSwear, InteractionId.RiseOverHead };

    private static string SuspicionWord(float s) => s < 15 ? "눈여겨봄" : s < 35 ? "의심함" : s < 70 ? "크게 의심함" : "칼을 갈고 있음";

    /// <summary>내 집에 온 요구: 번호, 지배자, 요구 글, 날짜.</summary>
    public Godot.Collections.Array Demands()
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People;
        foreach (var d in w.Demands)
        {
            var head = w.Households.Head[d.From];
            a.Add(new Godot.Collections.Dictionary
            {
                ["id"] = d.Id,
                ["lord"] = p.IsValid(head) ? Names.Full(w.Lineage, p.Uid[head.Index]) : "",
                ["realm"] = w.RealmName(d.From),
                ["text"] = w.DemandTextOf(d),
                ["date"] = $"{d.Day / w.Config.DaysPerYear + 1}년",
                ["yes"] = d.Kind switch { InteractionId.PlotPurge => "숙청한다", InteractionId.Declare or InteractionId.OrgBreak => "놓아준다", InteractionId.OrgOffer => "들어간다", InteractionId.OrgCondition => "따른다", _ => "받는다" },
                ["no"] = d.Kind switch { InteractionId.PlotPurge => "눈감아 준다", InteractionId.Declare => "진압한다", InteractionId.OrgBreak => "누른다", _ => "거절한다" },
            });
        }
        return a;
    }

    /// <summary>
    /// 내 집이 꾸미는 거사: 노리는 지배자, 해, 편든 사람, 아는 사람 수, 지금 일으키면 힘의 몫(어림),
    /// 끌어들일 만한 사람(받아들일 확률). 없으면 빈 사전.
    /// </summary>
    /// <summary>
    /// 지배자 도구(플레이어 집이 따르는 집을 가질 때만): 세율과 내 땅의 조직 목록. 조직마다 쓸 수 있는 수(거두기 제안, 누르기).
    /// </summary>
    public Godot.Collections.Dictionary LordTools()
    {
        var e = new Godot.Collections.Dictionary();
        if (!Ready) return e;
        var w = _w!; var p = w.People; var hh = w.Households;
        int h = w.PlayerHousehold;
        if (!hh.IsAlive(h) || w.ClientCount(h) == 0 || !p.IsValid(hh.Head[h])) return e;
        int me = hh.Head[h].Index;
        e["tax"] = (int)MathF.Round(w.TaxRate(h) * 100f);
        e["coin"] = w.TributeInCoin(h);
        // 소속 저울: 내 세력원 집 가운데 떠날 듯한 집(남을 까닭이 0.1 아래)
        var subs = Enumerable.Range(0, hh.HighWater).Where(x => hh.IsAlive(x) && w.PatronOf(x) == h && w.ClientCount(x) == 0).Select(x => w.BelongOf(x, h)).ToList();
        e["subjects"] = subs.Count;
        e["wavering"] = subs.Count(b => b.Total < 0.1f);
        e["belongAvg"] = subs.Count > 0 ? $"보호 {subs.Average(b => b.Protect):0.00} · 생계 {subs.Average(b => b.Living):0.00} · 공포 {subs.Average(b => b.Fear):0.00} · 충성 {subs.Average(b => b.Loyal):0.00} · 혜택 {subs.Average(b => b.Benefit):0.00} · 관습 {subs.Average(b => b.Habit):0.00} − 부담 {subs.Average(b => b.Burden):0.00}" : "";
        var (fearR, creditR) = w.RepOf(h);
        e["fear"] = fearR; e["credit"] = creditR;
        var orgs = new Godot.Collections.Array();
        foreach (var g in w.Groups)
        {
            if (!g.Alive || !World.IsTop(g) || g.Home >= 0) continue;
            int leader = FindAlive(g.LeaderUid);
            if (leader < 0 || p.Household[leader] == h) continue;
            bool mine = g.Sanction == h || (g.Sanction < 0 && w.PatronOf(p.Household[leader]) == h);
            if (!mine || !w.PlayerSeesMembership(leader, g)) continue;
            var moves = new Godot.Collections.Array();
            foreach (var id in new[] { InteractionId.OrgOffer, InteractionId.OrgSuppress })
            {
                var def = w.Def(id);
                var c = new InteractionContext { Actor = me, Target = leader, Third = -1 };
                if (!def.Can(w, c)) continue;
                moves.Add(new Godot.Collections.Dictionary { ["id"] = (int)id, ["name"] = def.Name, ["chance"] = def.Accept(w, c), ["harsh"] = def.Harsh, ["actor"] = me, ["target"] = leader });
            }
            // 조건 걸기(홀로 선 조직, 걸린 조건이 없을 때)
            var conds = new Godot.Collections.Array();
            if (!World.IsAttached(g) && w.ConditionOf(g) == OrgCondition.None)
                foreach (var cd in new[] { OrgCondition.Cap, OrgCondition.Disarm, OrgCondition.Reveal })
                {
                    if (cd == OrgCondition.Reveal && g.Visibility >= Visibility.Public) continue;
                    conds.Add(new Godot.Collections.Dictionary { ["cond"] = (int)cd, ["name"] = World.ConditionName(cd), ["chance"] = w.ConditionOdds(g, h), ["gid"] = g.Id });
                }
            orgs.Add(new Godot.Collections.Dictionary
            {
                ["conds"] = conds, ["threat"] = w.ThreatTo(g, out _), ["condition"] = World.ConditionName(w.ConditionOf(g)),
                ["name"] = g.Name, ["kind"] = World.GroupKindName(g.Kind), ["total"] = g.Total, ["attached"] = World.IsAttached(g),
                ["leader"] = Names.Full(w.Lineage, g.LeaderUid), ["moves"] = moves,
            });
        }
        e["orgs"] = orgs;
        // 싸움을 걸 수 있는 홀로 선 지배자
        var wars = new Godot.Collections.Array();
        var wdef = w.Def(InteractionId.War);
        for (int t = 0; t < hh.HighWater; t++)
        {
            if (!hh.IsAlive(t) || t == h || !p.IsValid(hh.Head[t])) continue;
            var c = new InteractionContext { Actor = me, Target = hh.Head[t].Index, Third = -1 };
            if (!wdef.Can(w, c)) continue;
            wars.Add(new Godot.Collections.Dictionary { ["id"] = (int)InteractionId.War, ["name"] = $"{Names.Full(w.Lineage, p.Uid[hh.Head[t].Index])}({w.RealmName(t)}, 아래 {w.BelowOf(t)})", ["chance"] = wdef.Accept(w, c), ["actor"] = me, ["target"] = hh.Head[t].Index });
        }
        e["wars"] = wars;
        return e;
    }

    /// <summary>내 땅의 조직에 조건을 건다. 우두머리가 받아들일지 정한다. 결과 글.</summary>
    public string ImposeCondition(int gid, int cond)
    {
        if (!Ready) return "";
        var w = _w!; var hh = w.Households;
        int h = w.PlayerHousehold;
        var g = w.GroupById(gid);
        if (g == null || !hh.IsAlive(h) || !w.People.IsValid(hh.Head[h])) return "";
        int leader = FindAlive(g.LeaderUid);
        if (leader < 0) return "";
        w.ForceCondition(g, (OrgCondition)cond, h, Math.Max(3, (int)(g.Total * 0.8f)));
        var r = w.Interact(InteractionId.OrgCondition, new InteractionContext { Actor = hh.Head[h].Index, Target = leader, Third = -1 });
        return r.Accepted ? $"{g.Name}: 조건을 받아들였다" : $"{g.Name}: 조건을 거절했다";
    }

    /// <summary>공납을 은화로 받을지 현물로 받을지.</summary>
    public void SetTributeCoin(bool coin)
    {
        if (!Ready || _w!.PlayerHousehold < 0) return;
        _w.SetTributeInCoin(_w.PlayerHousehold, coin);
        _w.Chronicle.Add((_w.Day, coin ? "공납을 은화로 받는다" : "공납을 현물로 받는다"));
    }

    /// <summary>세율을 정한다(8~20%). 12%보다 무거우면 아래 집들이 해마다 나를 나쁘게 본다.</summary>
    public void SetTax(int pct)
    {
        if (!Ready || _w!.PlayerHousehold < 0) return;
        _w.SetTaxRate(_w.PlayerHousehold, pct / 100f);
        _w.Chronicle.Add((_w.Day, $"세율을 {pct}%로 정했다"));
    }

    /// <summary>
    /// 남의 세력 정보: 내 세력이 심어 둔 사람의 자리만큼 보인다. 이중 첩자가 있으면 값이 비틀린다.
    /// 줄마다 세력 이름, 심은 사람과 자리, 보이는 것.
    /// </summary>
    public Godot.Collections.Array Intel()
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People;
        int h = w.PlayerHousehold;
        if (!w.Households.IsAlive(h)) return a;
        var roots = new List<Group>();
        foreach (int m in w.MembersOf(h)) foreach (var g in w.GroupsOf(m)) { var r = w.RootOf(g); if (r.LeaderUid == p.Uid[m] && !roots.Contains(r)) roots.Add(r); }
        if (w.LordOrg(h, false) is { } lo && !roots.Contains(w.RootOf(lo))) roots.Add(w.RootOf(lo));
        string[] roleName = { "조직원", "지휘자", "곳간지기", "측근" };
        foreach (var root in roots)
            foreach (var grp in w.Agents(root).GroupBy(x => x.other.Id))
            {
                var other = grp.First().other;
                int best = grp.Max(x => x.role);
                bool bent = grp.Any(x => w.IsDoubleAgent(x.person));
                // 이중 첩자가 흘리는 값: 해시로 0.5~1.5배 비튼다
                float Bend(float v) => bent ? v * (0.5f + ((uint)(other.Id * 2654435761u) % 1000) / 1000f) : v;
                var seen = new List<string>();
                foreach (var x in grp) seen.Add($"{x.unit.Name} {(int)Bend(x.unit.Members)}명 · 일 {string.Join("/", x.unit.Tasks.Select(t => World.VerbName(t.Verb)))}");
                if (best >= 1) foreach (var x in grp.Where(x => x.role == 1)) seen.Add($"{x.unit.Name} 장악도 {Bend(x.unit.Grip):0.00}, 지휘 몫 {x.unit.CmdFactor:0.00}");
                if (best >= 2) seen.Add($"곳간 식량 {Bend(other.Treasury):0}, 지난해 번 것 {Bend(other.LastIncome):0}");
                if (best >= 3)
                {
                    int ol = FindAlive(other.LeaderUid);
                    var plot = ol >= 0 ? w.Plots.FirstOrDefault(x => x.PlotterHouse == p.Household[ol]) : null;
                    seen.Add(bent ? "거사: 없음(이라고 한다)" : plot != null && p.IsValid(w.Households.Head[plot.Lord]) ? $"거사를 꾸민다: {Names.Full(w.Lineage, p.Uid[w.Households.Head[plot.Lord].Index])}에게" : "거사: 없음");
                    seen.Add($"모두 {(int)Bend(other.Total)}명, 드러난 무력 {Bend(w.VisibleForce(other)):0.0}, 지배자 위협 {Bend(w.ThreatTo(other, out _)):0.00}");
                }
                a.Add(new Godot.Collections.Dictionary
                {
                    ["name"] = other.Name, ["kind"] = World.GroupKindName(other.Kind),
                    ["agents"] = string.Join(", ", grp.Select(x => $"{Names.Full(w.Lineage, p.Uid[x.person])}({roleName[x.role]})")),
                    ["seen"] = string.Join("\n", seen),
                });
            }
        return a;
    }

    public Godot.Collections.Dictionary PlotInfo()
    {
        var e = new Godot.Collections.Dictionary();
        if (!Ready) return e;
        var w = _w!; var p = w.People;
        var x = w.PlayerPlot;
        if (x == null) return e;
        var (ratio, mine, theirs) = w.StrikeEstimate(x);
        int lh = w.Households.IsAlive(x.Lord) && p.IsValid(w.Households.Head[x.Lord]) ? w.Households.Head[x.Lord].Index : -1;
        e["lord"] = lh >= 0 ? Names.Full(w.Lineage, p.Uid[lh]) : "";
        e["realm"] = w.RealmName(x.Lord);
        e["years"] = (w.Day - x.Since) / w.Config.DaysPerYear;
        e["backers"] = string.Join(", ", x.Backers.Select(u => Names.Full(w.Lineage, u)));
        e["known"] = x.Known.Count;
        e["ratio"] = ratio;
        e["mine"] = mine;
        e["theirs"] = theirs;
        e["exposed"] = x.Exposed;
        e["actor"] = FindAlive(x.Plotter);
        var cands = new Godot.Collections.Array();
        foreach (var (c, ch) in w.PlotCandidates(x).Take(8))
            cands.Add(new Godot.Collections.Dictionary { ["i"] = c, ["name"] = Names.Full(w.Lineage, p.Uid[c]), ["chance"] = ch });
        e["candidates"] = cands;
        return e;
    }

    public int PlotRecruitId => (int)InteractionId.PlotRecruit;

    /// <summary>집 h 가 섬기는 주군 집의 가장(사람 번호). 없으면 -1.</summary>
    public int LiegeHeadOf(int h)
    {
        if (!Ready) return -1;
        int l = _w!.PatronOf(h);
        return l >= 0 && _w.Households.IsAlive(l) && _w.People.IsValid(_w.Households.Head[l]) ? _w.Households.Head[l].Index : -1;
    }

    /// <summary>거사를 일으킨다(true) 또는 접는다(false).</summary>
    public void PlotAct(bool strike)
    {
        if (!Ready || _w!.PlayerPlot is not { } x) return;
        if (strike) _w.Strike(x); else _w.AbandonPlot(x);
    }

    public bool AnswerDemand(int id, bool accept) => _w != null && _w.AnswerDemand(id, accept);

    /// <summary>
    /// 다툼 상대: 내가 지배자로서 눈여겨보는 세력(side "lord")과 나를 눈여겨보는 지배자(side "riser").
    /// 줄마다 상대 가장, 크기, 의심, 내가 쓸 수 있는 수(id, 이름, 확률, 험한 일).
    /// </summary>
    public Godot.Collections.Array RiseRows()
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People; var hh = w.Households;
        int me = w.PlayerHousehold;
        if (me < 0 || !p.IsValid(hh.Head[me])) return a;
        int myHead = hh.Head[me].Index;
        foreach (var r in w.Rivals.ToList())
        {
            if (r.Lord != me && r.Riser != me) continue;
            bool asLord = r.Lord == me;
            int other = asLord ? r.Riser : r.Lord;
            if (!hh.IsAlive(other) || !p.IsValid(hh.Head[other])) continue;
            int oh = hh.Head[other].Index;
            var moves = new Godot.Collections.Array();
            var ids = asLord ? LordMoves : RiserMoves;
            var targets = new List<int> { oh };
            if (!asLord && w.PatronOf(other) >= 0 && p.IsValid(hh.Head[w.PatronOf(other)])) targets.Add(hh.Head[w.PatronOf(other)].Index);   // 머리 위로 갈 곳
            foreach (int t in targets)
                foreach (var id in ids)
                {
                    var def = w.Def(id);
                    var c = new InteractionContext { Actor = myHead, Target = t, Third = -1 };
                    if (!def.Can(w, c)) continue;
                    moves.Add(new Godot.Collections.Dictionary
                    {
                        ["id"] = (int)id, ["target"] = t, ["actor"] = myHead, ["name"] = def.Name + (t != oh ? $"({Names.Full(w.Lineage, p.Uid[t])})" : ""),
                        ["chance"] = def.Accept(w, c), ["harsh"] = def.Harsh || def.Secret,
                    });
                }
            a.Add(new Godot.Collections.Dictionary
            {
                ["side"] = asLord ? "lord" : "riser",
                ["name"] = Names.Full(w.Lineage, p.Uid[oh]),
                ["realm"] = w.RealmName(other),
                ["size"] = Math.Max(w.BelowOf(other), w.ClientCount(other)),
                // 나를 의심하는 정도는 모른다. 눈치 빠른 가장만 짐작한다
                ["suspicion"] = asLord || p.Get(myHead, Trait.Perception) > 0.6f ? SuspicionWord(r.Suspicion) : "눈여겨봄",
                ["stole"] = r.Stole,
                ["moves"] = moves,
                ["x"] = hh.X[other], ["y"] = hh.Y[other], ["house"] = other,
            });
        }
        return a;
    }

    /// <summary>의심이 가장 큰 다툼의 올라서는 집(자동 점검용). 없으면 -1.</summary>
    public int TopRiser()
    {
        if (!Ready) return -1;
        var r = _w!.Rivals.Where(x => _w.Households.IsAlive(x.Riser)).OrderByDescending(x => x.Suspicion).FirstOrDefault();
        return r?.Riser ?? -1;
    }

    // ------------------------------------------------------------------ 검토용: 볼 만한 집

    /// <summary>
    /// 둘러볼 만한 집: 가장 큰 지배자, 봉신 영주, 무리, 의심받는 세력, 도적 두목, 상단·용병단·신도회 우두머리,
    /// 장인 집, 자리 없는 배운 사람의 집, 소작 집. 줄마다 집 번호, 무엇인지, 한 줄 설명, 위치.
    /// </summary>
    public Godot.Collections.Array Showcase()
    {
        var a = new Godot.Collections.Array();
        if (!Ready) return a;
        var w = _w!; var p = w.People; var hh = w.Households;
        var seen = new HashSet<int>();
        void Add(int h, string what, string why)
        {
            if (h < 0 || !hh.IsAlive(h) || !p.IsValid(hh.Head[h]) || !seen.Add(h)) return;
            string head = Names.Full(w.Lineage, p.Uid[hh.Head[h].Index]);
            string realm = w.RealmName(h);
            a.Add(new Godot.Collections.Dictionary
            {
                ["id"] = h, ["what"] = what, ["name"] = head + (realm != "" ? $" ({realm})" : ""), ["why"] = why,
                ["x"] = hh.X[h], ["y"] = hh.Y[h],
            });
        }
        int Best(Func<int, bool> ok, Func<int, float> score)
        {
            int best = -1; float bs = float.MinValue;
            for (int h = 0; h < hh.HighWater; h++)
                if (hh.IsAlive(h) && ok(h)) { float s = score(h); if (s > bs) { bs = s; best = h; } }
            return best;
        }
        // 시작 처지(plan_power 8.2): 소작농 가족, 몰락한 지주, 영주 집안의 서자, 떠돌이 용병
        int Men(int h) => w.MembersOf(h).Count(m => p.Male[m] && Age(m) >= 16 && Age(m) <= 40);
        int poorMany = Best(h => w.ClassOf(h) == LandClass.Tenant && w.PatronOf(h) >= 0, h => Men(h) * 10 + hh.Members[h]);
        if (poorMany >= 0) Add(poorMany, "소작농 가족", $"장정 {Men(poorMany)}명 · 남의 땅을 빌려 짓는다 · 무력이나 충신의 길");
        int fallen = Best(h => w.DebtOf(h) > 20f && hh.PlotCount[h] >= 3 && w.TierOf(h) == 0, h => w.DebtOf(h) / (hh.Food[h] + 50f));
        if (fallen >= 0) Add(fallen, "몰락한 지주 가족", $"땅 {hh.PlotCount[fallen]}필지, 빚 식량 {w.DebtOf(fallen):0} · 재력의 길");
        var lordFams = new HashSet<int>();
        for (int h = 0; h < hh.HighWater; h++) if (hh.IsAlive(h) && w.TierOf(h) >= 2 && p.IsValid(hh.Head[h])) lordFams.Add(w.Lineage.FamilyOf(p.Uid[hh.Head[h].Index]));
        int Bastard(int h)
        {
            int u = p.Uid[hh.Head[h].Index];
            int bio = w.Lineage.BioFather(u);
            return w.Lineage.RightOf(u) != Birthright.Legit && bio >= 0 && lordFams.Contains(w.Lineage.FamilyOf(bio)) ? 1 : 0;
        }
        int bastard = Best(h => p.IsValid(hh.Head[h]) && w.TierOf(h) == 0 && Bastard(h) == 1, h => hh.Members[h]);
        if (bastard >= 0) Add(bastard, "영주 집안의 서자 가족", "아버지가 지배자 가문이다 · 혼맥이나 음모의 길");
        int merc = Best(h => w.MembersOf(h).Any(m => w.GroupsOf(m).Any(g => w.RootOf(g).Kind == GroupKind.Sellswords)) && w.TierOf(h) == 0,
                        h => w.MembersOf(h).Count(m => w.GroupsOf(m).Any(g => w.RootOf(g).Kind == GroupKind.Sellswords)) * 10 - hh.PlotCount[h]);
        if (merc >= 0) Add(merc, "떠돌이 용병 가족", "용병단에 든 식구가 있다 · 무력의 길(고용되거나 도적)");
        int top = Best(h => w.PatronOf(h) < 0 && w.ClientCount(h) > 0, h => w.BelowOf(h));
        Add(top, "가장 큰 지배자", $"{World.TierName(w.TierOf(top))} · 아래 {w.BelowOf(top)}집 · 재판, 다툼, 분봉, 군대");
        int vassal = Best(h => w.TierOf(h) >= 2 && w.PatronOf(h) >= 0, h => w.BelowOf(h));
        Add(vassal, "봉신 영주", $"{World.TierName(w.TierOf(vassal))} · 아래 {w.BelowOf(vassal)}집 · 주군을 섬기는 영주");
        int band = Best(h => w.TierOf(h) == 1, h => w.ClientCount(h));
        Add(band, "무리", $"따르는 집 {w.ClientCount(band)} · 영주가 되기 직전");
        var rival = w.Rivals.Where(r => hh.IsAlive(r.Riser)).OrderByDescending(r => r.Suspicion).FirstOrDefault();
        if (rival != null) Add(rival.Riser, "의심받는 세력", $"따르는 집 {w.ClientCount(rival.Riser)} · 위 지배자가 눈여겨본다(다툼 창)");
        foreach (var (kind, what, why) in new[] { (GroupKind.Bandits, "도적 두목", "털기, 납치, 보호세"), (GroupKind.Company, "상단 우두머리", "교역과 호송"),
                                                  (GroupKind.Sellswords, "용병단 우두머리", "용병 계약"), (GroupKind.Brotherhood, "신도회 우두머리", "돌봄, 설교, 교리") })
        {
            var g = w.Groups.Where(x => x.Alive && x.Parent < 0 && x.Kind == kind).OrderByDescending(x => x.Total).FirstOrDefault();
            int lead = g != null ? FindAlive(g.LeaderUid) : -1;
            if (lead >= 0) Add(p.Household[lead], what, $"{g!.Name} · {g.Total}명 · {why}");
        }
        int crafter = Best(h => w.MembersOf(h).Any(m => p.JobOf[m] == (byte)Job.Crafter), h => w.GoodsOf(h, Good.Tools) + w.GoodsOf(h, Good.Cloth));
        Add(crafter, "장인 집", "도구와 옷을 만들어 마을 시장에 판다");
        int scholar = Best(h => w.MembersOf(h).Any(w.IsIdleScholar), h => w.MembersOf(h).Count(w.IsIdleScholar));
        Add(scholar, "자리 없는 배운 사람의 집", "지배자를 원망하고 포섭·선동에 약하다");
        int tenant = Best(h => w.ClassOf(h) == LandClass.Tenant, h => hh.Members[h]);
        Add(tenant, "소작 집", "남의 땅을 빌려 짓는 밑바닥에서 시작");
        return a;
    }
}
