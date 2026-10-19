namespace People.Sim;

/// <summary>지배 칭호를 어떻게 얻었나.</summary>
public enum Legitimacy : byte { None, Granted, Inherited, Bought, Self }

/// <summary>
/// P1: 섬김 나무와 세력 단계. 집마다 주군 하나(PatronOf). 지배자도 더 큰 지배자를 섬길 수 있다.
/// 세력의 크기는 그 아래 집 수(Below), 단계는 크기와 실력 문턱(무력, 살림, 거처, 봉신)으로 매긴다.
/// 오를 때 문턱 N, 내려갈 때 0.7N 으로 단계가 문턱 근처에서 오르내리지 않게 한다.
/// </summary>
public sealed partial class World
{
    private static readonly int[] TierUp = { 0, 5, 20, 100, 400 };
    private int[] _below = new int[256], _top = new int[256], _depth = new int[256];
    private byte[] _tier = new byte[256], _weak = new byte[256], _legit = new byte[256];
    private int[] _realmFounded = new int[256];   // 이 칸의 가구가 세워진 날(번호를 다시 쓰면 세력 기록을 비운다)
    private readonly Dictionary<int, int> _soldiersOf = new(), _collectorsOf = new();

    public int BelowOf(int h) => h >= 0 && h < _below.Length ? _below[h] : 0;
    public int TopOf(int h) => h >= 0 && h < _top.Length && Households.IsAlive(h) ? _top[h] : h;
    public int TierOf(int h) => h >= 0 && h < _tier.Length ? _tier[h] : 0;
    public Legitimacy LegitOf(int h) => h >= 0 && h < _legit.Length ? (Legitimacy)_legit[h] : Legitimacy.None;

    public static string TierName(int t) => t switch { 1 => "무리", 2 => "영주", 3 => "대영주", 4 => "군주", _ => "" };
    public static string LegitName(Legitimacy l) => l switch
    {
        Legitimacy.Granted => "주군이 내림", Legitimacy.Inherited => "물려받음", Legitimacy.Bought => "사들임", Legitimacy.Self => "스스로 섬", _ => "",
    };

    private void EnsureRealmArrays()
    {
        int n = Households.HighWater;
        if (_below.Length >= n) return;
        int c = Math.Max(n, _below.Length * 2);
        Array.Resize(ref _below, c); Array.Resize(ref _top, c); Array.Resize(ref _depth, c);
        Array.Resize(ref _tier, c); Array.Resize(ref _weak, c); Array.Resize(ref _legit, c); Array.Resize(ref _realmFounded, c);
    }

    /// <summary>h 를 주군으로 삼으면 고리가 생기는가(l 이 h 의 아래에 있는가).</summary>
    private bool WouldLoop(int vassal, int liege)
    {
        int x = liege;
        for (int k = 0; k < 32 && x >= 0; k++)
        {
            if (x == vassal) return true;
            x = PatronOf(x);
        }
        return false;
    }

    /// <summary>
    /// 나무를 훑는다: 집마다 아래 집 수, 꼭대기, 깊이. 그리고 단계와 실력 문턱을 매긴다.
    /// </summary>
    private void RecountTree()
    {
        EnsurePowerArrays();
        EnsureRealmArrays();
        var hh = Households;
        int n = hh.HighWater;
        BreakLoops();
        Array.Clear(_below, 0, n);
        int maxDepth = 0, vassals = 0;
        for (int h = 0; h < n; h++)
        {
            if (!hh.IsAlive(h)) continue;
            if (_realmFounded[h] != hh.FoundedDay[h]) { _realmFounded[h] = hh.FoundedDay[h]; _tier[h] = 0; _weak[h] = 0; _legit[h] = 0; }
            int x = h, d = 0;
            for (int l = PatronOf(h); l >= 0 && d < 32; l = PatronOf(l))
            {
                if (l == h) { _patron[h] = -1; break; }   // 고리는 끊는다
                _below[l]++;
                x = l;
                d++;
            }
            _top[h] = x;
            _depth[h] = d;
            if (d > maxDepth) maxDepth = d;
        }
        // 지배자마다 군인·징수관 수
        _soldiersOf.Clear(); _collectorsOf.Clear();
        foreach (var (person, l) in _employer)
        {
            if (!People.IsAliveIndex(person)) continue;
            if (People.JobOf[person] == (byte)Job.Soldier) _soldiersOf[l] = _soldiersOf.GetValueOrDefault(l) + 1;
            else if (People.JobOf[person] == (byte)Job.Collector) _collectorsOf[l] = _collectorsOf.GetValueOrDefault(l) + 1;
        }
        // 봉신 수(지난해 단계로 센다): 영주 이상인 집이 섬기는 주군에게 하나씩
        var vassalsOf = new Dictionary<int, int>();
        for (int h = 0; h < n; h++)
        {
            if (!hh.IsAlive(h) || _tier[h] < 2) continue;
            int l = PatronOf(h);
            if (l >= 0) { vassalsOf[l] = vassalsOf.GetValueOrDefault(l) + 1; vassals++; }
        }
        Span<int> count = stackalloc int[5];
        for (int h = 0; h < n; h++)
        {
            if (!hh.IsAlive(h)) { _tier[h] = 0; continue; }
            int cur = _tier[h], b = _below[h];
            if (cur == 0 && b < TierUp[1]) continue;
            int t = cur;
            int v = vassalsOf.GetValueOrDefault(h);
            while (t < 4 && b >= TierUp[t + 1] && Capable(h, t + 1, v)) t++;
            while (t > 0 && b < (int)(0.7f * TierUp[t])) t--;
            // 실력이 무너진 해가 이어지면 한 단계 내려간다
            if (t >= 2 && t == cur)
            {
                if (Failing(h, t, v) >= 2) { if (++_weak[h] >= 2) { t--; _weak[h] = 0; } }
                else _weak[h] = 0;
            }
            if (t >= 2 && cur < 2 && _legit[h] == 0)
                _legit[h] = (byte)(PatronOf(h) >= 0 ? Legitimacy.Granted : Legitimacy.Self);
            if (t > cur) Stats.TierRises++;
            else if (t < cur) Stats.TierFalls++;
            _tier[h] = (byte)t;
            count[t]++;
        }
        Stats.Tier1 = count[1]; Stats.Tier2 = count[2]; Stats.Tier3 = count[3]; Stats.Tier4 = count[4];
        Stats.Vassals = vassals; Stats.MaxDepth = maxDepth;
    }

    /// <summary>섬김에 고리(A→B→…→A)가 있으면 그 고리의 한 곳을 끊는다(그 집은 독립한다).</summary>
    private void BreakLoops()
    {
        var hh = Households;
        int n = hh.HighWater;
        var state = new byte[n];   // 0 아직, 1 지금 걷는 중, 2 끝(고리 없음)
        var path = new List<int>();
        for (int h = 0; h < n; h++)
        {
            if (!hh.IsAlive(h) || state[h] != 0) continue;
            path.Clear();
            int x = h;
            while (x >= 0 && state[x] == 0)
            {
                state[x] = 1;
                path.Add(x);
                x = PatronOf(x);
            }
            if (x >= 0 && state[x] == 1) { _patron[x] = -1; Stats.LoopsBroken++; }
            foreach (int k in path) state[k] = 2;
        }
    }

    /// <summary>단계 t 의 실력 문턱을 넘는가.</summary>
    private bool Capable(int h, int t, int vassals) => t < 2 || Failing(h, t, vassals) == 0;

    /// <summary>실력 문턱 중 몇 개가 깨졌나: 무력, 살림, 거처, 봉신(대영주부터), 징수관(대영주부터).</summary>
    private int Failing(int h, int t, int vassals)
    {
        int soldiers = _soldiersOf.GetValueOrDefault(h);
        int fail = 0;
        if (soldiers < Config.LordSoldiers && HouseholdPower(h) + 1.5f * soldiers < Config.LordGuardPower) fail++;
        if (_incTribute[h] < 0.8f * soldiers * Config.SoldierWage) fail++;
        if (ResidenceOf(h) < t - 1) fail++;
        if (t >= 3 && vassals < (t == 3 ? 2 : 5)) fail++;
        if (t >= 3 && _collectorsOf.GetValueOrDefault(h) < 1) fail++;
        return fail;
    }

    /// <summary>
    /// 작은 지배자가 더 큰 지배자를 섬긴다. 위험할수록, 욕심이 작을수록, 상대가 셀수록 들어간다.
    /// </summary>
    private void SeekLieges(Dictionary<int, float> power, List<int> lords)
    {
        var hh = Households;
        foreach (int l in lords)
        {
            if (PatronOf(l) >= 0 || ClientCount(l) == 0) continue;
            float danger = hh.Village[l] >= 0 ? _danger[hh.Village[l]] : 0f;
            float want = Config.VassalRate * (1f - 0.6f * HeadTrait(l, Trait.Ambition)) * (0.5f + MathF.Min(2f, danger));
            if (!_rng.Chance(want)) continue;
            float own = power[l] + 0.3f * BelowOf(l);
            int best = -1;
            float bs = 0f;
            foreach (int o in lords)
            {
                if (o == l) continue;
                float other = power[o] + 0.3f * BelowOf(o);
                if (other < 2f * own || WouldLoop(l, o)) continue;
                float dx = hh.X[o] - hh.X[l], dy = hh.Y[o] - hh.Y[l];
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > Config.VassalRange) continue;
                float sc = other * (0.5f + HeadTrait(o, Trait.Charisma)) / (1f + d / 80f);
                if (sc > bs) { bs = sc; best = o; }
            }
            if (best < 0) continue;
            _patron[l] = best;
            _patronFounded[l] = hh.FoundedDay[best];
            HeadsRelate(l, best, 5f, 10f, 10f, MemoryType.Protected, 10);
            Stats.NewVassals++;
        }
    }

    /// <summary>공납을 위로 올린다: 아래 층부터 받은 공납의 일부를 자기 주군에게.</summary>
    private void PassTributeUp()
    {
        var hh = Households;
        var lords = new List<int>();
        for (int h = 0; h < hh.HighWater; h++)
            if (hh.IsAlive(h) && _incTribute[h] > 0f && PatronOf(h) >= 0) lords.Add(h);
        lords.Sort((a, b) => BelowOf(a).CompareTo(BelowOf(b)));
        foreach (int l in lords)
        {
            int up = PatronOf(l);
            float share = MathF.Min(_incTribute[l] * Config.UpshareRate, MathF.Max(0f, hh.Food[l]));
            hh.Food[l] -= share;
            hh.Food[up] += share;
            _incTribute[l] -= share;
            _incTribute[up] += share;
            Stats.Upshare += share;
        }
    }
}
