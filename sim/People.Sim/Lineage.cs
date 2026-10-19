namespace People.Sim;

/// <summary>
/// 태어난 모든 사람의 기록(족보). 죽어도 남는다. 번호는 Uid(태어난 순서).
/// 혈연 계산을 위해 4대 조상(2+4+8+16 = 30명)을 미리 펼쳐 둔다.
/// </summary>
/// <summary>가문 상속 방식. 장자 상속은 땅을 지키고, 균분은 분가하는 아들에게 땅을 나눠 준다.</summary>
public enum InheritPolicy : byte { Primogeniture, Partible }

public sealed class Lineage
{
    public const int AncestorSlots = 30;
    private static readonly int[] DepthStart = { 0, 2, 6, 14, 30 };

    public int Count { get; private set; }
    private int[] _father = new int[1024];
    private int[] _mother = new int[1024];
    private bool[] _male = new bool[1024];
    private int[] _birth = new int[1024];
    private int[] _death = new int[1024];
    private int[] _children = new int[1024];
    private byte[] _job = new byte[1024];
    private int[] _family = new int[1024];   // 가문(성). 아버지에게서, 아버지가 없으면 어머니에게서. 시작 인구는 저마다 새 가문
    public int Families { get; private set; }
    private readonly List<byte> _policy = new();   // 가문 상속 방식(Inheritance)
    public InheritPolicy PolicyOf(int family) => family >= 0 && family < _policy.Count ? (InheritPolicy)_policy[family] : InheritPolicy.Primogeniture;
    public void SetPolicy(int family, InheritPolicy p) { while (_policy.Count <= family) _policy.Add(0); _policy[family] = (byte)p; }   // 살면서 마지막으로 맡은 일(족보에 남는 직업)
    private int[] _ancestors = new int[1024 * AncestorSlots];

    public int Father(int uid) => _father[uid];
    public int Mother(int uid) => _mother[uid];
    public bool Male(int uid) => _male[uid];
    public int Birth(int uid) => _birth[uid];
    public int Death(int uid) => _death[uid];
    public int Children(int uid) => _children[uid];
    public int FamilyOf(int uid) => uid >= 0 ? _family[uid] : -1;
    public Job JobOf(int uid) => uid >= 0 ? (Job)_job[uid] : Job.Farmer;
    public void SetJob(int uid, Job j) { if (uid >= 0) _job[uid] = (byte)j; }
    public ReadOnlySpan<int> Ancestors(int uid) => _ancestors.AsSpan(uid * AncestorSlots, AncestorSlots);

    public int Add(bool male, int birthDay, int fatherUid, int motherUid)
    {
        if (Count == _father.Length) Grow(Count * 2);
        int u = Count++;
        _father[u] = fatherUid;
        _mother[u] = motherUid;
        _male[u] = male;
        _birth[u] = birthDay;
        _death[u] = -1;
        _children[u] = 0;
        _job[u] = 0;
        _family[u] = fatherUid >= 0 ? _family[fatherUid] : motherUid >= 0 ? _family[motherUid] : Families++;
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
        if (fatherUid >= 0) _children[fatherUid]++;
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
        Array.Resize(ref _mother, cap);
        Array.Resize(ref _male, cap);
        Array.Resize(ref _birth, cap);
        Array.Resize(ref _death, cap);
        Array.Resize(ref _children, cap);
        Array.Resize(ref _job, cap);
        Array.Resize(ref _family, cap);
        Array.Resize(ref _ancestors, cap * AncestorSlots);
    }
}
