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
    public int[] X = new int[256], Y = new int[256];   // 집 위치(타일)
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
        X[h] = Y[h] = 0;
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
        Array.Resize(ref X, cap);
        Array.Resize(ref Y, cap);
        Array.Resize(ref FoundedDay, cap);
        Array.Resize(ref Members, cap);
        Array.Resize(ref Need, cap);
        Array.Resize(ref Labor, cap);
        Array.Resize(ref Hungry, cap);
    }
}


/// <summary>마을: 집이 모인 중심과 그 둘레 밭. 땅이 차면 사람들이 떠나 새 마을을 연다 (spec 8.4).</summary>
public sealed class VillageStore
{
    public readonly List<int> CenterX = new(), CenterY = new(), Founded = new(), Households = new();
    public readonly List<bool> Alive = new();
    public int Count => CenterX.Count;
    public int AliveCount { get; private set; }

    public int Add(int x, int y, int day)
    {
        CenterX.Add(x); CenterY.Add(y); Founded.Add(day); Households.Add(0); Alive.Add(true);
        AliveCount++;
        return CenterX.Count - 1;
    }

    public void Join(int v) { if (v >= 0) Households[v]++; }

    public void Leave(int v)
    {
        if (v < 0) return;
        Households[v]--;
        if (Households[v] <= 0 && Alive[v]) { Alive[v] = false; AliveCount--; }
    }
}
