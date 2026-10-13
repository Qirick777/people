namespace People.Sim;

/// <summary>
/// 가구: 한 집에 사는 사람들. 식량과 땅을 공유한다 (spec 6.1).
/// 구성원 수, 하루 필요량, 일손은 매일 사람을 한 번 훑어서 다시 계산한다.
/// </summary>
public sealed class HouseholdStore
{
    private int[] _gen = new int[256];
    private bool[] _alive = new bool[256];
    private readonly Stack<int> _free = new();
    public int HighWater { get; private set; }
    public int AliveCount { get; private set; }

    public PersonId[] Head = new PersonId[256];
    public int[] Village = new int[256];
    public float[] Food = new float[256];
    public int[] Capacity = new int[256];
    public int[] PlotCount = new int[256];
    public float[] LandValue = new float[256];
    public int[] FirstMember = new int[256];
    public int[] FoundedDay = new int[256];

    // 매일 다시 계산하는 값
    public int[] Members = new int[256];
    public float[] Need = new float[256];
    public float[] Labor = new float[256];
    public bool[] Hungry = new bool[256];

    public bool IsAlive(int h) => h >= 0 && h < HighWater && _alive[h];

    public int Add(int village, float food, int capacity, int day)
    {
        int h;
        if (_free.Count > 0) h = _free.Pop();
        else
        {
            if (HighWater == _alive.Length) Grow(_alive.Length * 2);
            h = HighWater++;
        }
        _gen[h]++;
        _alive[h] = true;
        Head[h] = PersonId.None;
        Village[h] = village;
        Food[h] = food;
        Capacity[h] = capacity;
        PlotCount[h] = 0;
        LandValue[h] = 0;
        FirstMember[h] = -1;
        FoundedDay[h] = day;
        Members[h] = 0;
        Need[h] = 0;
        Labor[h] = 0;
        Hungry[h] = false;
        AliveCount++;
        return h;
    }

    public void Remove(int h)
    {
        if (!_alive[h]) return;
        _alive[h] = false;
        _free.Push(h);
        AliveCount--;
    }

    /// <summary>북적임 = 인원 / 수용 인원.</summary>
    public float Crowding(int h) => Members[h] / (float)Math.Max(1, Capacity[h]);

    private void Grow(int cap)
    {
        Array.Resize(ref _gen, cap);
        Array.Resize(ref _alive, cap);
        Array.Resize(ref Head, cap);
        Array.Resize(ref Village, cap);
        Array.Resize(ref Food, cap);
        Array.Resize(ref Capacity, cap);
        Array.Resize(ref PlotCount, cap);
        Array.Resize(ref LandValue, cap);
        Array.Resize(ref FirstMember, cap);
        Array.Resize(ref FoundedDay, cap);
        Array.Resize(ref Members, cap);
        Array.Resize(ref Need, cap);
        Array.Resize(ref Labor, cap);
        Array.Resize(ref Hungry, cap);
    }
}

/// <summary>필지 (spec 5.1). 1단계에서는 위치 없이 비옥도와 주인만 다룬다.</summary>
public sealed class Land
{
    public readonly float[] BaseFertility;
    public readonly float[] Fertility;
    public readonly int[] Owner;
    public int Count => Owner.Length;
    public int UnownedCount { get; private set; }

    // 가구별 필지 목록 (가구 번호 → 필지들)
    private List<int>?[] _byOwner = new List<int>?[256];

    // 주인 없는 필지를 비옥한 순서로 꺼내기 위한 목록. 내놓은 필지는 끝에 붙고, 꺼낼 때 다시 정렬한다.
    private readonly List<int> _free = new();
    private bool _freeSorted;

    public Land(int plots, ref Rng rng)
    {
        BaseFertility = new float[plots];
        Fertility = new float[plots];
        Owner = new int[plots];
        for (int i = 0; i < plots; i++)
        {
            // 강가(20%)는 비옥하고, 나머지는 0.5~1.1
            float b = rng.Chance(0.2f) ? 1.1f + rng.NextFloat() * 0.4f : 0.5f + rng.NextFloat() * 0.6f;
            BaseFertility[i] = b;
            Fertility[i] = b;
            Owner[i] = -1;
            _free.Add(i);
        }
        UnownedCount = plots;
    }

    public int Unowned() => UnownedCount;

    public float PlotValue(int i) => 30f * BaseFertility[i];

    private void SetOwner(int plot, int to, HouseholdStore hh)
    {
        int from = Owner[plot];
        if (from == to) return;
        if (from >= 0) { hh.PlotCount[from]--; hh.LandValue[from] -= PlotValue(plot); _byOwner[from]!.Remove(plot); }
        else UnownedCount--;
        if (to >= 0)
        {
            hh.PlotCount[to]++;
            hh.LandValue[to] += PlotValue(plot);
            if (to >= _byOwner.Length) Array.Resize(ref _byOwner, Math.Max(to + 1, _byOwner.Length * 2));
            (_byOwner[to] ??= new List<int>()).Add(plot);
        }
        else { UnownedCount++; _free.Add(plot); _freeSorted = false; }
        Owner[plot] = to;
    }

    /// <summary>주인 없는 필지 중 비옥한 것부터 n개를 가구에 준다. 실제로 준 수를 돌려준다.</summary>
    public int Claim(int household, int n, HouseholdStore hh)
    {
        if (!_freeSorted)
        {
            _free.RemoveAll(i => Owner[i] >= 0);
            _free.Sort((a, b) => BaseFertility[a].CompareTo(BaseFertility[b])); // 끝이 가장 비옥
            _freeSorted = true;
        }
        int got = 0;
        while (got < n && _free.Count > 0)
        {
            int plot = _free[^1];
            _free.RemoveAt(_free.Count - 1);
            if (Owner[plot] >= 0) continue;
            SetOwner(plot, household, hh);
            got++;
        }
        return got;
    }

    /// <summary>from 가구의 필지 n개를 to 가구로 옮긴다(좋은 땅과 나쁜 땅을 번갈아).</summary>
    public int Transfer(int from, int to, int n, HouseholdStore hh)
    {
        var mine = PlotsOf(from);
        mine.Sort((a, b) => BaseFertility[b].CompareTo(BaseFertility[a]));
        int moved = 0;
        for (int k = 0; k < mine.Count && moved < n; k += 2) { SetOwner(mine[k], to, hh); moved++; }
        for (int k = 1; k < mine.Count && moved < n; k += 2) { SetOwner(mine[k], to, hh); moved++; }
        return moved;
    }

    public void ReleaseWorst(int household, int n, HouseholdStore hh)
    {
        var mine = PlotsOf(household);
        mine.Sort((a, b) => BaseFertility[a].CompareTo(BaseFertility[b]));
        for (int k = 0; k < n && k < mine.Count; k++) SetOwner(mine[k], -1, hh);
    }

    public void Release(int household, HouseholdStore hh)
    {
        foreach (int i in PlotsOf(household)) SetOwner(i, -1, hh);
    }

    /// <summary>가구의 필지 목록 복사본.</summary>
    public List<int> PlotsOf(int household) =>
        household < _byOwner.Length && _byOwner[household] != null ? new List<int>(_byOwner[household]!) : new List<int>();

    public float Value(int household, HouseholdStore hh) => hh.LandValue[household];
}
