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
            foreach (GroupKind k in new[] { GroupKind.Watch, GroupKind.Brotherhood, GroupKind.Bandits })
                if (!mine.Any(g => g.Kind == k))
                    a.Add(new Godot.Collections.Dictionary { ["key"] = $"found:{(int)k}", ["name"] = $"{World.GroupKindName(k)} 세우기", ["note"] = k == GroupKind.Bandits ? "숨어서 함께 턴다. 들키면 진압당한다" : k == GroupKind.Watch ? "마을을 함께 지킨다" : "신앙으로 묶이고 굶는 회원을 돕는다", ["risky"] = k == GroupKind.Bandits });
        foreach (var g in mine)
            a.Add(new Godot.Collections.Dictionary { ["key"] = $"leave:{g.Id}", ["name"] = $"{g.Name}에서 나가기", ["note"] = w.IsLeader(actor, g) ? "우두머리가 나가면 다른 사람이 잇는다" : "", ["risky"] = false });
        return a;
    }

    public string DoSelf(int actor, string key)
    {
        if (_w == null) return "";
        var w = _w;
        var parts = key.Split(':');
        if (parts[0] == "found")
        {
            var g = w.FoundGroup(actor, (GroupKind)int.Parse(parts[1]));
            return $"{g.Name}을(를) 세웠다. 이웃을 눌러 '모임에 들이기'로 사람을 모은다";
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
        if (def.Id is InteractionId.ArrangeMatch or InteractionId.ForcedMatch)
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
        string about = third >= 0 && p.IsAliveIndex(third) ? $"({Names.Full(w.Lineage, p.Uid[third])}" + (amount > 0 ? $", {amount:0})" : ")") : "";
        var r = w.Interact((InteractionId)id, new InteractionContext { Actor = actor, Target = target, Third = third, Amount = amount });
        string outcome = r.Accepted ? "받아들였다" : "거절당했다";
        if (def.Id == InteractionId.Enfeoff && r.Accepted) outcome = $"집 {r.Amount:0}곳을 맡겼다";
        else if (def.Id == InteractionId.Revoke) outcome = r.Accepted ? "집을 돌려받았다" : "거부하고 떨어져 나갔다";
        else if (def.Id is InteractionId.Steal or InteractionId.Assault or InteractionId.Extort or InteractionId.Murder)
        {
            var last = w.Crimes.Count > 0 ? w.Crimes[^1] : null;
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
            void Walk(Group g, int depth)
            {
                int leader = FindAlive(g.LeaderUid);
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
                    ["authority"] = depth == 0 ? g.Authority : 0f,
                    ["leak"] = g.Leak,
                    ["inner"] = g.Inner.Count,
                    ["lesson"] = World.LessonName(g.Lesson),
                    ["teachShare"] = g.TeachShare,
                    ["heir"] = g.HeirUid >= 0 && FindAlive(g.HeirUid) >= 0 ? Names.Full(w.Lineage, g.HeirUid) : "",
                    ["inherit"] = g.Inherit,
                    ["joinRule"] = (int)g.JoinRule,
                    ["shareRule"] = (int)g.ShareRule,
                    ["treasury"] = g.Treasury,
                    ["command"] = command,
                    ["canSplit"] = command && g.Members >= 9,
                    ["mineIn"] = mine.Any(i => w.InGroup(i, g)),
                });
                foreach (var c in w.ChildrenOf(g).ToList()) Walk(c, depth + 1);
            }
            Walk(root, 0);
        }
        return a;
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
        _w!.Chronicle.Add((_w.Day, $"{g!.Name}에서 {c.Name}을(를) 떼어 냈다"));
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
        _w.Chronicle.Add((_w.Day, $"{g.Name}에 {World.VerbName((Verb)verb)}({VillageName(g.Village, village)})을(를) 맡겼다"));
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
        _w.Chronicle.Add((_w.Day, $"{_w.RootOf(g).Name}을(를) '{World.VisibilityName((Visibility)v)}'(으)로 두었다"));
        return true;
    }

    /// <summary>조직 이름을 짓는다. 빈 글이면 저절로 붙는 이름으로 돌아간다.</summary>
    public bool RenameOrg(int id, string name)
    {
        var g = Commandable(id);
        if (g == null) return false;
        string old = g.Name;
        _w!.RenameGroup(g, name);
        if (g.Name != old) _w.Chronicle.Add((_w.Day, $"{old}의 이름을 {g.Name}(으)로 지었다"));
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
        _w!.Chronicle.Add((_w.Day, $"{g.Name}: {World.LessonName(g.Lesson)}을(를) {(int)(g.TeachShare * 100)}% 가르친다"));
        return true;
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
        foreach (var c in w.Crimes.Reverse().Where(c => c.Verdict != Verdict.Pending && (c.Lord == me || mine.Contains(c.Culprit) || mine.Contains(c.Victim))).Take(12))
        {
            bool known = c.VictimKnows || c.Witnesses.Count > 0 || mine.Contains(c.Culprit) || c.Verdict is Verdict.Fine or Verdict.Flog or Verdict.Execute or Verdict.Covered;
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
            var head = w.Households.Head[d.Lord];
            a.Add(new Godot.Collections.Dictionary
            {
                ["id"] = d.Id,
                ["lord"] = p.IsValid(head) ? Names.Full(w.Lineage, p.Uid[head.Index]) : "",
                ["realm"] = w.RealmName(d.Lord),
                ["text"] = w.DemandTextOf(d),
                ["date"] = $"{d.Day / w.Config.DaysPerYear + 1}년",
            });
        }
        return a;
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
                ["suspicion"] = SuspicionWord(r.Suspicion),
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
