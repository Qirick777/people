namespace People.Sim;

/// <summary>가문 상속 방식. 장자 상속은 땅을 지키고, 균분은 분가하는 아들에게 땅을 나눠 준다.</summary>
public enum InheritPolicy : byte { Primogeniture, Partible }

/// <summary>태어난 자리: 적자(혼인한 아내의 자식), 서자(첩의 자식), 사생아(족보 아버지가 없음).</summary>
public enum Birthright : byte { Legit, Concubine, Bastard }

/// <summary>
/// 태어난 모든 사람의 기록(족보). 죽어도 남는다. 번호는 Uid(태어난 순서).
/// 아버지는 둘을 둔다. 족보에 적힌 아버지(성·가문·상속이 따른다)와 진짜 아버지(유전과 혈연 계산이 따른다).
/// 혈연 계산을 위해 진짜 조상 4대(2+4+8+16 = 30명)를 미리 펼쳐 둔다.
/// </summary>
public sealed class Lineage
{
    public const int AncestorSlots = 30;
    private static readonly int[] DepthStart = { 0, 2, 6, 14, 30 };

    public int Count { get; private set; }
    private int[] _father = new int[1024];   // 족보에 적힌 아버지
    private int[] _bio = new int[1024];      // 진짜 아버지(다르면 숨은 혈통)
    private int[] _mother = new int[1024];
    private bool[] _male = new bool[1024];
    private int[] _birth = new int[1024];
    private int[] _death = new int[1024];
    private int[] _children = new int[1024];
    private byte[] _job = new byte[1024];
    private int[] _family = new int[1024];   // 가문(성). 아버지에게서, 아버지가 없으면 어머니에게서. 시작 인구는 저마다 새 가문
    private byte[] _right = new byte[1024];  // Birthright
    private bool[] _disowned = new bool[1024];
    public int Families { get; private set; }
    private readonly List<byte> _policy = new();   // 가문 상속 방식(Inheritance)
    public InheritPolicy PolicyOf(int family) => family >= 0 && family < _policy.Count ? (InheritPolicy)_policy[family] : InheritPolicy.Primogeniture;
    public void SetPolicy(int family, InheritPolicy p) { while (_policy.Count <= family) _policy.Add(0); _policy[family] = (byte)p; }   // 살면서 마지막으로 맡은 일(족보에 남는 직업)
    private int[] _ancestors = new int[1024 * AncestorSlots];

    public int Father(int uid) => _father[uid];
    public int BioFather(int uid) => _bio[uid];
    public bool HiddenFather(int uid) => uid >= 0 && _bio[uid] != _father[uid];
    public int Mother(int uid) => _mother[uid];
    public bool Male(int uid) => _male[uid];
    public int Birth(int uid) => _birth[uid];
    public int Death(int uid) => _death[uid];
    public int Children(int uid) => _children[uid];
    public int FamilyOf(int uid) => uid >= 0 ? _family[uid] : -1;
    public Birthright RightOf(int uid) => uid >= 0 ? (Birthright)_right[uid] : Birthright.Bastard;
    public void SetRight(int uid, Birthright r) { if (uid >= 0) _right[uid] = (byte)r; }
    public bool Disowned(int uid) => uid >= 0 && _disowned[uid];
    public void SetDisowned(int uid, bool v) { if (uid >= 0) _disowned[uid] = v; }

    /// <summary>
    /// 촌수(족보 기준). 족보 아버지와 어머니를 타고 4대까지 올라가 가장 가까운 공통 조상을 찾는다.
    /// 부모 1, 형제 2, 삼촌 3, 사촌 4. 4대 안에 공통 조상이 없으면 -1.
    /// </summary>
    public int Chon(int a, int b)
    {
        if (a < 0 || b < 0) return -1;
        if (a == b) return 0;
        var up = new Dictionary<int, int>();
        Climb(a, 0, up);
        var other = new Dictionary<int, int>();
        Climb(b, 0, other);
        int best = int.MaxValue;
        foreach (var (x, d2) in other)
            if (up.TryGetValue(x, out int d1) && d1 + d2 < best) best = d1 + d2;
        return best == int.MaxValue ? -1 : best;
    }

    private void Climb(int u, int d, Dictionary<int, int> seen)
    {
        if (u < 0 || d > 4) return;
        if (seen.TryGetValue(u, out int old) && old <= d) return;
        seen[u] = d;
        Climb(_father[u], d + 1, seen);
        Climb(_mother[u], d + 1, seen);
    }

    /// <summary>족보의 자식 목록(족보 아버지와 어머니 기준). 족보 화면을 그릴 때만 만든다.</summary>
    public Dictionary<int, List<int>> ChildrenIndex()
    {
        var map = new Dictionary<int, List<int>>();
        for (int u = 0; u < Count; u++)
        {
            foreach (int par in new[] { _father[u], _mother[u] })
                if (par >= 0) (map.TryGetValue(par, out var l) ? l : map[par] = new()).Add(u);
        }
        return map;
    }

    /// <summary>자식으로 인정한다: 족보 아버지를 father 로 적고 성을 따르게 하며 적자로 올린다.</summary>
    public void Legitimize(int uid, int father)
    {
        if (uid < 0 || father < 0) return;
        if (_father[uid] != father)
        {
            if (_father[uid] >= 0) _children[_father[uid]]--;
            _father[uid] = father;
            _children[father]++;
            _family[uid] = _family[father];
        }
        _right[uid] = (byte)Birthright.Legit;
    }
    public Job JobOf(int uid) => uid >= 0 ? (Job)_job[uid] : Job.Farmer;
    public void SetJob(int uid, Job j) { if (uid >= 0) _job[uid] = (byte)j; }
    public ReadOnlySpan<int> Ancestors(int uid) => _ancestors.AsSpan(uid * AncestorSlots, AncestorSlots);

    /// <summary>부모를 모르는 사람을 이미 있는 가문(성)에 넣는다. 새 가문을 만들지 않는다.</summary>
    public int AddInto(bool male, int birthDay, int family)
    {
        int f0 = Families;
        int u = Add(male, birthDay, -1, -1);
        Families = f0;
        _family[u] = family;
        return u;
    }

    /// <summary>legalFatherUid 를 주지 않으면 진짜 아버지가 족보 아버지다. -1 이면 사생아.</summary>
    public int Add(bool male, int birthDay, int fatherUid, int motherUid, int legalFatherUid = int.MinValue)
    {
        if (legalFatherUid == int.MinValue) legalFatherUid = fatherUid;
        if (Count == _father.Length) Grow(Count * 2);
        int u = Count++;
        _father[u] = legalFatherUid;
        _bio[u] = fatherUid;
        _mother[u] = motherUid;
        _male[u] = male;
        _birth[u] = birthDay;
        _death[u] = -1;
        _children[u] = 0;
        _job[u] = 0;
        _family[u] = legalFatherUid >= 0 ? _family[legalFatherUid] : motherUid >= 0 ? _family[motherUid] : Families++;
        _right[u] = (byte)(legalFatherUid >= 0 || (fatherUid < 0 && motherUid < 0) ? Birthright.Legit : Birthright.Bastard);
        _disowned[u] = false;
        var a = _ancestors.AsSpan(u * AncestorSlots, AncestorSlots);
        a.Fill(-1);
        a[0] = fatherUid;
        a[1] = motherUid;
        // 깊이 d+1 조상 = 아버지의 깊이 d 조상들 + 어머니의 깊이 d 조상들
        for (int d = 1; d < 4; d++)
        {
            int len = DepthStart[d] - DepthStart[d - 1];
            int dst = DepthStart[d];
            CopyDepth(fatherUid, d - 1, len, a.Slice(dst, len));
            CopyDepth(motherUid, d - 1, len, a.Slice(dst + len, len));
        }
        if (legalFatherUid >= 0) _children[legalFatherUid]++;
        if (motherUid >= 0) _children[motherUid]++;
        return u;
    }

    private void CopyDepth(int parent, int depth, int len, Span<int> dst)
    {
        if (parent < 0) return;
        _ancestors.AsSpan(parent * AncestorSlots + DepthStart[depth], len).CopyTo(dst);
    }

    public void MarkDeath(int uid, int day) => _death[uid] = day;

    private static int DepthOf(int slot) => slot < 2 ? 1 : slot < 6 ? 2 : slot < 14 ? 3 : 4;

    /// <summary>
    /// 혈연 계수 r. 가장 가까운 공통 조상마다, 양쪽에서 그 조상까지 가는 경로 쌍에 (1/2)^(길이 합)을 더한다.
    /// 다른 공통 조상의 조상은 세지 않는다(그 경로는 가까운 쪽에 이미 들어 있다).
    /// 형제 0.5, 부모-자식 0.5, 이복형제 0.25, 사촌 0.125.
    /// </summary>
    public float Relatedness(int a, int b)
    {
        if (a == b) return 1f;
        Span<int> common = stackalloc int[AncestorSlots + 1];
        int n = 0;
        var aa = Ancestors(a);
        var bb = Ancestors(b);
        // 후보: b 자신 또는 b의 조상 중 a 쪽에도 있는 것, 그리고 a 자신이 b의 조상인 경우
        if (Contains(aa, b)) common[n++] = b;
        if (Contains(bb, a)) common[n++] = a;
        for (int j = 0; j < AncestorSlots; j++)
        {
            int x = bb[j];
            if (x >= 0 && Contains(aa, x) && !Contains(common[..n], x)) common[n++] = x;
        }
        float r = 0f;
        for (int k = 0; k < n; k++)
        {
            int x = common[k];
            bool shadowed = false;
            for (int m = 0; m < n && !shadowed; m++)
            {
                if (m != k && Contains(Ancestors(common[m]), x)) shadowed = true;
            }
            if (shadowed) continue;
            r += PathSum(a, aa, x) * PathSum(b, bb, x);
        }
        return r;
    }

    /// <summary>self 에서 조상 x 까지 가는 모든 경로의 (1/2)^길이 합. x 가 self 면 1.</summary>
    private static float PathSum(int self, ReadOnlySpan<int> anc, int x)
    {
        if (self == x) return 1f;
        float s = 0f;
        for (int i = 0; i < AncestorSlots; i++)
        {
            if (anc[i] == x) s += MathF.Pow(0.5f, DepthOf(i));
        }
        return s;
    }

    private static bool Contains(ReadOnlySpan<int> list, int x)
    {
        if (x < 0) return false;
        foreach (int v in list) if (v == x) return true;
        return false;
    }

    private void Grow(int cap)
    {
        Array.Resize(ref _father, cap);
        Array.Resize(ref _bio, cap);
        Array.Resize(ref _mother, cap);
        Array.Resize(ref _male, cap);
        Array.Resize(ref _birth, cap);
        Array.Resize(ref _death, cap);
        Array.Resize(ref _children, cap);
        Array.Resize(ref _job, cap);
        Array.Resize(ref _family, cap);
        Array.Resize(ref _right, cap);
        Array.Resize(ref _disowned, cap);
        Array.Resize(ref _ancestors, cap * AncestorSlots);
    }
}
