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
        string text = $"{World.Subj(an)} {tn}에게 {def.Name}{about}: " + (r.Accepted ? "받아들였다" : "거절당했다") + (r.Pregnant ? " (아이가 들어섰다)" : "");
        w.Chronicle.Add((w.Day, text));
        return text;
    }
}
