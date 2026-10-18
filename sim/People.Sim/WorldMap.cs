namespace People.Sim;

/// <summary>
/// 타일 지도. 타일 하나 = 필지 하나(8 m × 8 m). 청크(32 × 32 타일) 단위로 필요할 때 세부를 만든다.
/// 대략 지도 한 칸 = 청크 하나. 세부는 시드와 좌표만으로 정해져서 언제 만들어도 같다.
/// 땅 소유(가구별 필지 목록)도 여기서 관리한다.
/// </summary>
public sealed class WorldMap
{
    public const int CS = 32;               // 청크 크기(타일)
    public readonly int ChunksX, ChunksY, Width, Height;
    public readonly CoarseMap? Coarse;      // 평평한 섬 모드면 null
    private readonly uint _seed;

    private sealed class Chunk
    {
        public readonly TileType[] Type = new TileType[CS * CS];
        public readonly float[] BaseFert = new float[CS * CS];
        public readonly float[] Fert = new float[CS * CS];
        public readonly byte[] WaterDist = new byte[CS * CS];   // 가장 가까운 물(강·호수·바다)까지 타일 수, 최대 15
        public readonly byte[] Temp = new byte[CS * CS];        // 기온 0~255
        public readonly int[] Owner = new int[CS * CS];
        public readonly byte[] Flags = new byte[CS * CS];       // 1 = 밭(개간됨), 2 = 집터
        public readonly byte[] Wild = new byte[CS * CS];        // 개간한 땅이 버려진 지 몇 해
        public readonly float[] Wear = new float[CS * CS];      // 사람이 다녀 닳은 정도. 해마다 줄어든다
        public readonly TileType[] Under = new TileType[CS * CS]; // 길이 되기 전 땅 종류
        public int Free;                                         // 주인 없고 집터 아닌 경작 가능 타일 수
    }
    public const byte FlagField = 1, FlagHouse = 2, FlagRoad = 4, FlagMajor = 8;   // 큰길: 많이 다녀 넓어진 길   // 길: 땅이면 타입도 Road, 물이면 다리

    private readonly Chunk?[] _chunks;
    public int GeneratedChunks { get; private set; }

    // 가구별 필지 목록
    private List<int>?[] _byOwner = new List<int>?[256];

    private WorldMap(uint seed, int cx, int cy, CoarseMap? coarse)
    {
        _seed = seed;
        ChunksX = cx; ChunksY = cy;
        Width = cx * CS; Height = cy * CS;
        Coarse = coarse;
        _chunks = new Chunk?[cx * cy];
    }

    /// <summary>시드로 대륙을 만든다. 세부 청크는 아직 만들지 않는다.</summary>
    public static WorldMap Generate(uint seed, int coarseSize = 48) =>
        new(seed, coarseSize, coarseSize, new CoarseMap(seed, coarseSize, coarseSize));

    /// <summary>1단계용 평평한 섬: 필지 plots 개, 모두 풀밭. 강가(20%)는 비옥하다.</summary>
    public static WorldMap Flat(uint seed, int plots)
    {
        int side = (int)MathF.Ceiling(MathF.Sqrt(plots));
        int chunks = (side + CS - 1) / CS;
        var m = new WorldMap(seed, chunks, chunks, null);
        var rng = new Rng(seed ^ 0xF1A7);
        for (int i = 0; i < m._chunks.Length; i++)
        {
            var c = new Chunk();
            for (int t = 0; t < CS * CS; t++)
            {
                int x = (i % chunks) * CS + t % CS, y = (i / chunks) * CS + t / CS;
                bool inside = y * side + x < plots && x < side;
                float b = rng.Chance(0.2f) ? 1.1f + rng.NextFloat() * 0.4f : 0.5f + rng.NextFloat() * 0.6f;
                c.Type[t] = inside ? TileType.Grass : TileType.Sea;
                c.BaseFert[t] = c.Fert[t] = inside ? b : 0f;
                c.WaterDist[t] = 15;
                c.Temp[t] = 160;
                c.Owner[t] = -1;
                if (inside) c.Free++;
            }
            m._chunks[i] = c;
        }
        m.GeneratedChunks = m._chunks.Length;
        return m;
    }

    // ------------------------------------------------------------------ 타일 접근

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public int Id(int x, int y) => y * Width + x;
    public (int x, int y) Pos(int id) => (id % Width, id / Width);

    private Chunk Get(int x, int y, out int local)
    {
        int ci = (y / CS) * ChunksX + x / CS;
        local = (y % CS) * CS + x % CS;
        return _chunks[ci] ?? Build(x / CS, y / CS);
    }

    public bool IsGenerated(int cx, int cy) => _chunks[cy * ChunksX + cx] != null;

    public TileType Type(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).Type[l]; }
    public float BaseFert(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).BaseFert[l]; }
    public float Fert(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).Fert[l]; }
    public void SetFert(int id, float v) { var (x, y) = Pos(id); Get(x, y, out int l).Fert[l] = v; }
    public void SetBaseFert(int id, float v) { var (x, y) = Pos(id); Get(x, y, out int l).BaseFert[l] = v; }
    public int WaterDist(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).WaterDist[l]; }
    public float Temp(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).Temp[l] / 255f; }
    public int Owner(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).Owner[l]; }
    public byte Flags(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).Flags[l]; }
    public void SetFlags(int id, byte f) { var (x, y) = Pos(id); Get(x, y, out int l).Flags[l] = f; }
    public void SetType(int id, TileType t) { var (x, y) = Pos(id); Get(x, y, out int l).Type[l] = t; }
    public int Wild(int id) { var (x, y) = Pos(id); return Get(x, y, out int l).Wild[l]; }
    public void SetWild(int id, int v) { var (x, y) = Pos(id); Get(x, y, out int l).Wild[l] = (byte)Math.Clamp(v, 0, 255); }

    // 버려진 밭(개간했지만 주인 없는 땅). 해마다 거칠어지다가 숲이 된다.
    private readonly HashSet<int> _abandoned = new();
    public int AbandonedCount => _abandoned.Count;

    /// <summary>버려진 밭이 한 해 묵는다. forestYears 가 되면 숲으로 돌아간다. 숲이 된 칸 수를 돌려준다.</summary>
    public int AgeAbandoned(int forestYears)
    {
        int toForest = 0;
        var done = new List<int>();
        foreach (int id in _abandoned)
        {
            var (x, y) = Pos(id);
            var c = Get(x, y, out int l);
            if (c.Owner[l] >= 0 || (c.Flags[l] & (FlagHouse | FlagRoad)) != 0) { done.Add(id); continue; }
            if (c.Wild[l] < 255) c.Wild[l]++;
            if (c.Wild[l] < forestYears) continue;
            c.Type[l] = TileType.Forest;
            c.Flags[l] = (byte)(c.Flags[l] & ~FlagField);
            c.Wild[l] = 0;
            done.Add(id);
            toForest++;
        }
        foreach (int id in done) _abandoned.Remove(id);
        return toForest;
    }

    public static bool Farmable(TileType t) => t is TileType.Grass or TileType.Forest or TileType.Marsh or TileType.Hills;

    /// <summary>만들어진 청크의 경작 가능 땅 중 물에서 limit 타일 안인 비율(비교 기준).</summary>
    public float FarmableNearWaterFraction(int limit)
    {
        long n = 0, near = 0;
        for (int i = 0; i < _chunks.Length; i++)
        {
            var c = _chunks[i];
            if (c == null) continue;
            for (int t = 0; t < CS * CS; t++)
            {
                if (!Farmable(c.Type[t])) continue;
                n++;
                if (c.WaterDist[t] <= limit) near++;
            }
        }
        return n > 0 ? near / (float)n : 0f;
    }

    /// <summary>필지 가치(지참금·재산 계산용).</summary>
    public float PlotValue(int id) => 30f * BaseFert(id);

    /// <summary>주변 radius 안의 청크를 미리 만든다.</summary>
    public void EnsureAround(int x, int y, int radius)
    {
        int cx0 = Math.Max(0, (x - radius) / CS), cx1 = Math.Min(ChunksX - 1, (x + radius) / CS);
        int cy0 = Math.Max(0, (y - radius) / CS), cy1 = Math.Min(ChunksY - 1, (y + radius) / CS);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                if (_chunks[cy * ChunksX + cx] == null) Build(cx, cy);
    }

    // ------------------------------------------------------------------ 청크 세부 생성

    private const int Margin = 8;

    /// <summary>타일 높이(m). 화면용. 바다면 0 이하.</summary>
    public float ElevationMeters(int x, int y)
    {
        if (Coarse == null) return 1f;
        var c = Coarse;
        float u = (x + 0.5f) / CS - 0.5f, v = (y + 0.5f) / CS - 0.5f;
        float e = c.Sample(c.Elevation, u, v) + (Noise.Fbm(_seed + 21, x / 20f, y / 20f, 3) - 0.5f) * 0.08f;
        return (e - c.SeaLevel) * 160f;
    }

    private TileType BaseType(int x, int y, out float temp, out float rain)
    {
        var c = Coarse!;
        float u = (x + 0.5f) / CS - 0.5f, v = (y + 0.5f) / CS - 0.5f;
        float e = c.Sample(c.Elevation, u, v) + (Noise.Fbm(_seed + 21, x / 20f, y / 20f, 3) - 0.5f) * 0.08f;
        temp = c.Sample(c.Temperature, u, v);
        rain = c.Sample(c.Rain, u, v);
        float sea = c.Sample(c.SeaMask, u, v) + (Noise.Value(_seed + 23, x / 9f, y / 9f) - 0.5f) * 0.3f;
        if (e < c.SeaLevel || sea > 0.75f) return TileType.Sea;
        float lake = c.Sample(c.LakeMask, u, v) + (Noise.Value(_seed + 25, x / 7f, y / 7f) - 0.5f) * 0.45f;
        if (lake > 0.55f) return TileType.Lake;
        float land = e - c.SeaLevel;
        if (land > c.MountainLevel) return TileType.Mountain;
        if (land > c.HillLevel) return TileType.Hills;
        float fn = Noise.Fbm(_seed + 31, x / 14f, y / 14f, 4);
        if (rain > 0.6f && land < 0.06f && Noise.Value(_seed + 33, x / 10f, y / 10f) > 0.55f) return TileType.Marsh;
        if (rain + (fn - 0.5f) * 0.7f > 0.52f) return TileType.Forest;
        return TileType.Grass;
    }

    /// <summary>강 곡선: 대략 지도 칸 a → 아래 칸 b. 가운데를 해시만큼 비튼 2차 곡선.</summary>
    private (float ax, float ay, float mx, float my, float bx, float by, float width) RiverCurve(int a)
    {
        var c = Coarse!;
        int b = c.Downstream[a];
        float ax = (a % c.W + 0.5f) * CS, ay = (a / c.W + 0.5f) * CS;
        float bx = (b % c.W + 0.5f) * CS, by = (b / c.W + 0.5f) * CS;
        float dx = bx - ax, dy = by - ay, len = MathF.Sqrt(dx * dx + dy * dy);
        float off = (Noise.Lattice(_seed + 41, a, b) - 0.5f) * 0.7f * len;
        float mx = (ax + bx) * 0.5f - dy / len * off, my = (ay + by) * 0.5f + dx / len * off;
        float width = Math.Clamp(1f + 0.7f * MathF.Log2(c.Flow[a] / c.RiverThreshold), 1f, 3.5f);
        return (ax, ay, mx, my, bx, by, width);
    }

    private Chunk Build(int cx, int cy)
    {
        var chunk = new Chunk();
        _chunks[cy * ChunksX + cx] = chunk;
        GeneratedChunks++;
        var co = Coarse!;
        int S = CS + 2 * Margin;
        int ox = cx * CS - Margin, oy = cy * CS - Margin;
        var type = new TileType[S * S];
        var temp = new float[S * S];
        var rain = new float[S * S];
        for (int j = 0; j < S; j++)
            for (int i = 0; i < S; i++)
                type[j * S + i] = BaseType(ox + i, oy + j, out temp[j * S + i], out rain[j * S + i]);

        // 강: 주변 대략 칸에서 시작하는 물길을 곡선으로 찍는다
        for (int gy = cy - 2; gy <= cy + 2; gy++)
            for (int gx = cx - 2; gx <= cx + 2; gx++)
            {
                if (gx < 0 || gy < 0 || gx >= co.W || gy >= co.H) continue;
                int a = gy * co.W + gx;
                if (!co.River[a] || co.Downstream[a] < 0) continue;
                var (ax, ay, mx, my, bx, by, w) = RiverCurve(a);
                int steps = (int)(CS * 3);
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps, it = 1 - t;
                    float px = it * it * ax + 2 * it * t * mx + t * t * bx;
                    float py = it * it * ay + 2 * it * t * my + t * t * by;
                    float r = w * 0.5f;
                    int x0 = (int)MathF.Floor(px - r - ox), x1 = (int)MathF.Ceiling(px + r - ox);
                    int y0 = (int)MathF.Floor(py - r - oy), y1 = (int)MathF.Ceiling(py + r - oy);
                    for (int yy = Math.Max(0, y0); yy <= Math.Min(S - 1, y1); yy++)
                        for (int xx = Math.Max(0, x0); xx <= Math.Min(S - 1, x1); xx++)
                        {
                            float ddx = xx + ox + 0.5f - px, ddy = yy + oy + 0.5f - py;
                            if (ddx * ddx + ddy * ddy > r * r) continue;
                            ref var tt = ref type[yy * S + xx];
                            if (tt != TileType.Sea && tt != TileType.Lake) tt = TileType.River;
                        }
                }
            }

        // 물까지 거리: 여유 테두리까지 포함해 너비 우선 탐색
        var dist = new byte[S * S];
        var q = new Queue<int>();
        for (int k = 0; k < S * S; k++)
        {
            bool water = type[k] is TileType.Sea or TileType.Lake or TileType.River;
            dist[k] = water ? (byte)0 : (byte)15;
            if (water) q.Enqueue(k);
        }
        while (q.Count > 0)
        {
            int k = q.Dequeue();
            int i = k % S, j = k / S;
            byte nd = (byte)(dist[k] + 1);
            if (nd >= 15) continue;
            if (i > 0 && dist[k - 1] > nd) { dist[k - 1] = nd; q.Enqueue(k - 1); }
            if (i < S - 1 && dist[k + 1] > nd) { dist[k + 1] = nd; q.Enqueue(k + 1); }
            if (j > 0 && dist[k - S] > nd) { dist[k - S] = nd; q.Enqueue(k - S); }
            if (j < S - 1 && dist[k + S] > nd) { dist[k + S] = nd; q.Enqueue(k + S); }
        }

        // 안쪽만 저장하고 비옥도를 정한다
        for (int j = 0; j < CS; j++)
            for (int i = 0; i < CS; i++)
            {
                int k = (j + Margin) * S + (i + Margin);
                int l = j * CS + i;
                var t = type[k];
                float tp = temp[k];
                chunk.Type[l] = t;
                chunk.WaterDist[l] = dist[k];
                chunk.Temp[l] = (byte)(Math.Clamp(tp, 0f, 1f) * 255);
                chunk.Owner[l] = -1;
                float b = t switch { TileType.Grass => 0.85f, TileType.Forest => 0.95f, TileType.Marsh => 0.5f, TileType.Hills => 0.6f, _ => 0f };
                if (b > 0)
                {
                    b *= Math.Clamp(0.45f + 0.65f * tp, 0.3f, 1.05f);                       // 추우면 덜 자란다
                    if (dist[k] < 4) b += 0.4f * (1f - dist[k] / 4f);                         // 범람원
                    b += (Noise.Value(_seed + 51, (ox + i + Margin) / 5f, (oy + j + Margin) / 5f) - 0.5f) * 0.24f;
                    b = Math.Clamp(b, 0.15f, 1.6f);
                }
                chunk.BaseFert[l] = chunk.Fert[l] = b;
                if (Farmable(t)) chunk.Free++;
            }
        return chunk;
    }

    // ------------------------------------------------------------------ 땅 소유

    private void SetOwner(int id, int to, HouseholdStore hh)
    {
        var (x, y) = Pos(id);
        var c = Get(x, y, out int l);
        int from = c.Owner[l];
        if (from == to) return;
        if (from >= 0) { hh.PlotCount[from]--; hh.LandValue[from] -= PlotValue(id); _byOwner[from]!.Remove(id); }
        if (to >= 0) _abandoned.Remove(id);
        else if ((c.Flags[l] & FlagField) != 0 && (c.Flags[l] & (FlagHouse | FlagRoad)) == 0) _abandoned.Add(id);
        bool farm = Farmable(c.Type[l]) && (c.Flags[l] & FlagHouse) == 0;
        if (farm && from < 0 && to >= 0) c.Free--;
        if (farm && from >= 0 && to < 0) c.Free++;
        if (to >= 0)
        {
            hh.PlotCount[to]++;
            hh.LandValue[to] += PlotValue(id);
            if (to >= _byOwner.Length) Array.Resize(ref _byOwner, Math.Max(to + 1, _byOwner.Length * 2));
            (_byOwner[to] ??= new List<int>()).Add(id);
        }
        c.Owner[l] = to;
    }

    /// <summary>
    /// 땅 바꾸기: 덩어리에서 떨어진 필지를 내 밭 가장자리의 땅과 바꾼다. 가장자리 땅이 비었으면 옮겨 가고,
    /// 남의 것이면 두 집 밭이 합쳐서 더 뭉칠 때만 맞바꾼다. 비옥도가 거의 같은 땅끼리만 바꾸므로
    /// 수확은 그대로 두고 흩어진 밭이 해마다 덩어리로 모인다.
    /// </summary>
    public int Consolidate(HouseholdStore hh, int households, float tolerance)
    {
        int swaps = 0;
        var frontier = new HashSet<int>();
        for (int a = 0; a < households && a < _byOwner.Length; a++)
        {
            var list = _byOwner[a];
            if (list == null || list.Count < 2) continue;
            foreach (int p in list.ToArray())
            {
                if (Owner(p) != a) continue;
                var (px, py) = Pos(p);
                int sameA = SameOwnerAround(px, py, a, -1);
                if (sameA >= 2) continue;
                // 내 다른 필지에 붙은 칸들
                frontier.Clear();
                foreach (int r in list)
                {
                    if (r == p) continue;
                    var (rx, ry) = Pos(r);
                    if (InBounds(rx + 1, ry)) frontier.Add(Id(rx + 1, ry));
                    if (InBounds(rx - 1, ry)) frontier.Add(Id(rx - 1, ry));
                    if (InBounds(rx, ry + 1)) frontier.Add(Id(rx, ry + 1));
                    if (InBounds(rx, ry - 1)) frontier.Add(Id(rx, ry - 1));
                }
                int best = -1, bestGain = 0;
                foreach (int q in frontier)
                {
                    if (q == p) continue;
                    int b = Owner(q);
                    if (b == a || BaseFert(q) < BaseFert(p) - tolerance) continue;
                    var (qx, qy) = Pos(q);
                    int gain;
                    if (b < 0)
                    {
                        // 빈 땅: 경작 가능하고 집터나 길가가 아니어야 한다
                        if (!Farmable(Type(q)) || Type(q) == TileType.Forest || (Flags(q) & (FlagHouse | FlagRoad)) != 0) continue;
                        if (IsRoad(qx + 1, qy) || IsRoad(qx - 1, qy) || IsRoad(qx, qy + 1) || IsRoad(qx, qy - 1)) continue;
                        gain = SameOwnerAround(qx, qy, a, p) - sameA;
                    }
                    else
                    {
                        if (MathF.Abs(BaseFert(p) - BaseFert(q)) > tolerance || MathF.Abs(Fert(p) - Fert(q)) > tolerance) continue;
                        int before = sameA + SameOwnerAround(qx, qy, b, -1);
                        int after = SameOwnerAround(qx, qy, a, p) + SameOwnerAround(px, py, b, q);
                        gain = after - before;
                    }
                    if (gain > bestGain) { bestGain = gain; best = q; }
                }
                if (best < 0) continue;
                int ob = Owner(best);
                SetOwner(p, ob, hh);
                SetOwner(best, a, hh);
                swaps++;
            }
        }
        return swaps;
    }

    private int SameOwnerAround(int x, int y, int owner, int skip)
    {
        int n = 0;
        if (InBounds(x + 1, y) && Id(x + 1, y) != skip && Owner(Id(x + 1, y)) == owner) n++;
        if (InBounds(x - 1, y) && Id(x - 1, y) != skip && Owner(Id(x - 1, y)) == owner) n++;
        if (InBounds(x, y + 1) && Id(x, y + 1) != skip && Owner(Id(x, y + 1)) == owner) n++;
        if (InBounds(x, y - 1) && Id(x, y - 1) != skip && Owner(Id(x, y - 1)) == owner) n++;
        return n;
    }

    public List<int> PlotsOf(int household) =>
        household < _byOwner.Length && _byOwner[household] != null ? new List<int>(_byOwner[household]!) : new List<int>();

    // ------------------------------------------------------------------ 길

    /// <summary>길이 바뀔 때마다 오른다. 마을 걸음 지도를 다시 만들지 정할 때 쓴다.</summary>
    public int RoadVersion { get; private set; }

    public bool IsRoad(int x, int y) => InBounds(x, y) && (Flags(Id(x, y)) & FlagRoad) != 0;

    /// <summary>타일을 길로 만든다. 밭이었으면 주인을 지운다. 물 위는 다리(타입 유지).</summary>
    public void MakeRoad(int x, int y, HouseholdStore hh)
    {
        if (!InBounds(x, y)) return;
        int id = Id(x, y);
        var c = Get(x, y, out int l);
        if ((c.Flags[l] & FlagRoad) != 0 || (c.Flags[l] & FlagHouse) != 0) return;
        if (c.Owner[l] >= 0) SetOwner(id, -1, hh);
        bool water = c.Type[l] is TileType.Sea or TileType.Lake or TileType.River;
        if (!water)
        {
            if (Farmable(c.Type[l])) c.Free--;
            c.Under[l] = c.Type[l];
            c.Type[l] = TileType.Road;
        }
        c.Flags[l] |= FlagRoad;
        RoadVersion++;
    }

    /// <summary>길이 풀에 덮여 사라진다. 땅은 묵은 풀밭이 되어 천이를 따른다(물 위면 다리가 없어진다).</summary>
    public void RemoveRoad(int x, int y)
    {
        var c = Get(x, y, out int l);
        if ((c.Flags[l] & FlagRoad) == 0) return;
        c.Flags[l] = (byte)(c.Flags[l] & ~(FlagRoad | FlagMajor));
        if (c.Type[l] == TileType.Road)
        {
            var t = c.Under[l];
            if (t is TileType.Road or TileType.Sea or TileType.Lake or TileType.River) t = TileType.Grass;
            c.Type[l] = t;
            if (Farmable(t) && c.Owner[l] < 0 && (c.Flags[l] & FlagHouse) == 0)
            {
                c.Free++;
                c.Flags[l] |= FlagField;
                c.Wild[l] = 3;
                _abandoned.Add(Id(x, y));
            }
        }
        RoadVersion++;
    }

    // ------------------------------------------------------------------ 닳음과 길의 생성·소멸

    private readonly HashSet<int> _worn = new();

    public void AddWear(int x, int y, float amount)
    {
        if (!InBounds(x, y)) return;
        var c = Get(x, y, out int l);
        c.Wear[l] += amount;
        _worn.Add(Id(x, y));
    }

    public float WearAt(int x, int y) => InBounds(x, y) ? Get(x, y, out int l).Wear[l] : 0f;

    /// <summary>
    /// 한 해 닳음을 정리한다. 남은 닳음 × keep. on 을 넘으면 길이 생기고, off 아래로 내려가면 길이 사라진다.
    /// major 를 넘으면 큰길로 표시한다. (생긴 길, 사라진 길) 수를 돌려준다.
    /// </summary>
    public (int made, int lost) UpdateRoads(float keep, float on, float off, float major, HouseholdStore hh)
    {
        int made = 0, lost = 0;
        var drop = new List<int>();
        foreach (int id in _worn)
        {
            var (x, y) = Pos(id);
            var c = Get(x, y, out int l);
            float w = c.Wear[l];
            bool road = (c.Flags[l] & FlagRoad) != 0;
            if (!road && w >= on && (c.Flags[l] & FlagHouse) == 0 && c.Type[l] != TileType.Mountain)
            {
                MakeRoad(x, y, hh);
                made++;
                road = true;
            }
            else if (road && w < off)
            {
                RemoveRoad(x, y);
                lost++;
                road = false;
            }
            if (road)
                c.Flags[l] = w >= major ? (byte)(c.Flags[l] | FlagMajor) : (byte)(c.Flags[l] & ~FlagMajor);
            c.Wear[l] = w * keep;
            if (c.Wear[l] < 0.05f && !road) { c.Wear[l] = 0f; drop.Add(id); }
        }
        foreach (int id in drop) _worn.Remove(id);
        return (made, lost);
    }

    /// <summary>
    /// 사람이 걷는 수고(상하좌우). 길은 빠르고, 남의 밭은 가로지르기 꺼리며, 물은 다리 없이 못 건넌다.
    /// </summary>
    private float TripCost(int x, int y)
    {
        var c = Get(x, y, out int l);
        if ((c.Flags[l] & FlagRoad) != 0) return (c.Flags[l] & FlagMajor) != 0 ? 0.4f : 0.5f;
        if ((c.Flags[l] & FlagHouse) != 0) return float.PositiveInfinity;
        float b = c.Type[l] switch
        {
            TileType.Grass => 1f, TileType.Hills => 1.6f, TileType.Forest => 2f, TileType.Marsh => 2.2f,
            _ => float.PositiveInfinity,
        };
        if (c.Owner[l] >= 0) b += 0.8f;
        return b;
    }

    /// <summary>마을 중심에서 걷는 수고 지도(상하좌우, 길과 밭을 따진다). 닳음 경로를 그리는 데 쓴다.</summary>
    public Reach BuildTrips(int cx, int cy, float maxCost)
    {
        int r = (int)MathF.Ceiling(maxCost / 0.4f) + 1;
        EnsureAround(cx, cy, r);
        var R = new Reach { CX = cx, CY = cy, Max = maxCost, RoadVersion = RoadVersion };
        R.X0 = Math.Max(0, cx - r); R.Y0 = Math.Max(0, cy - r);
        R.W = Math.Min(Width - 1, cx + r) - R.X0 + 1; R.H = Math.Min(Height - 1, cy + r) - R.Y0 + 1;
        R.Cost = new float[R.W * R.H];
        Array.Fill(R.Cost, float.PositiveInfinity);
        var open = new PriorityQueue<int, float>();
        int start = (cy - R.Y0) * R.W + (cx - R.X0);
        R.Cost[start] = 0;
        open.Enqueue(start, 0);
        Span<int> dx = stackalloc int[] { 1, -1, 0, 0 };
        Span<int> dy = stackalloc int[] { 0, 0, 1, -1 };
        while (open.TryDequeue(out int cur, out float g))
        {
            if (g > R.Cost[cur]) continue;
            int x = cur % R.W, y = cur / R.W;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + dx[k], ny = y + dy[k];
                if (nx < 0 || ny < 0 || nx >= R.W || ny >= R.H) continue;
                float step = TripCost(nx + R.X0, ny + R.Y0);
                if (float.IsInfinity(step)) continue;
                float ng = g + step;
                int ni = ny * R.W + nx;
                if (ng > maxCost || ng >= R.Cost[ni]) continue;
                R.Cost[ni] = ng;
                open.Enqueue(ni, ng);
            }
        }
        return R;
    }

    /// <summary>(x, y) 에서 걸음 지도를 따라 중심까지 내려가며 지나는 칸을 닳게 한다. 닿지 못하면 false.</summary>
    public bool WalkToCenter(Reach R, int x, int y, float amount)
    {
        float cur = R.At(x, y);
        for (int step = 0; step < 600; step++)
        {
            int bx = -1, by = -1;
            float best = cur;
            if (InBounds(x + 1, y) && R.At(x + 1, y) < best) { best = R.At(x + 1, y); bx = x + 1; by = y; }
            if (InBounds(x - 1, y) && R.At(x - 1, y) < best) { best = R.At(x - 1, y); bx = x - 1; by = y; }
            if (InBounds(x, y + 1) && R.At(x, y + 1) < best) { best = R.At(x, y + 1); bx = x; by = y + 1; }
            if (InBounds(x, y - 1) && R.At(x, y - 1) < best) { best = R.At(x, y - 1); bx = x; by = y - 1; }
            if (bx < 0) return false;
            x = bx; y = by; cur = best;
            AddWear(x, y, amount);
            if (cur <= 0f) return true;
        }
        return false;
    }

    /// <summary>길을 내는 비용. 이미 길이면 싸고, 밭은 피하고, 물은 다리라 비싸다.</summary>
    private float StepCost(int x, int y)
    {
        var c = Get(x, y, out int l);
        if ((c.Flags[l] & FlagRoad) != 0) return 0.3f;
        if ((c.Flags[l] & FlagHouse) != 0) return float.PositiveInfinity;
        float b = c.Type[l] switch
        {
            TileType.Grass => 1f, TileType.Forest => 2.5f, TileType.Hills => 2.5f, TileType.Marsh => 3f,
            TileType.River => 6f, TileType.Lake => 25f, TileType.Mountain => 8f, _ => float.PositiveInfinity,
        };
        if (c.Owner[l] >= 0) b += 1.5f;
        return b;
    }

    /// <summary>A* 길찾기(상하좌우). 두 점을 감싸는 상자 + 여유 안에서만 찾는다. 못 찾으면 빈 목록.</summary>
    public List<(int x, int y)> FindPath(int ax, int ay, int bx, int by, int margin = 30)
    {
        int x0 = Math.Max(0, Math.Min(ax, bx) - margin), x1 = Math.Min(Width - 1, Math.Max(ax, bx) + margin);
        int y0 = Math.Max(0, Math.Min(ay, by) - margin), y1 = Math.Min(Height - 1, Math.Max(ay, by) + margin);
        EnsureAround((x0 + x1) / 2, (y0 + y1) / 2, Math.Max(x1 - x0, y1 - y0) / 2 + 1);
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        var g = new float[bw * bh];
        var from = new int[bw * bh];
        Array.Fill(g, float.PositiveInfinity);
        var open = new PriorityQueue<int, float>();
        int start = (ay - y0) * bw + (ax - x0), goal = (by - y0) * bw + (bx - x0);
        g[start] = 0; from[start] = -1;
        open.Enqueue(start, 0);
        Span<int> dx = stackalloc int[] { 1, -1, 0, 0 };
        Span<int> dy = stackalloc int[] { 0, 0, 1, -1 };
        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == goal) break;
            int cx = cur % bw, cy = cur / bw;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if (nx < 0 || ny < 0 || nx >= bw || ny >= bh) continue;
                int nid = ny * bw + nx;
                bool isGoal = nid == goal;
                float step = isGoal ? 1f : StepCost(nx + x0, ny + y0);
                if (float.IsInfinity(step)) continue;
                float ng = g[cur] + step;
                if (ng >= g[nid]) continue;
                g[nid] = ng;
                from[nid] = cur;
                float h = 0.3f * (Math.Abs(nx + x0 - bx) + Math.Abs(ny + y0 - by));
                open.Enqueue(nid, ng + h);
            }
        }
        var path = new List<(int, int)>();
        if (float.IsInfinity(g[goal])) return path;
        for (int k = goal; k != -1; k = from[k]) path.Add((k % bw + x0, k / bw + y0));
        path.Reverse();
        return path;
    }

    /// <summary>마을 중심에서 각 칸까지 걸어가는 수고. 길은 빠르고, 숲·언덕·늪은 느리고, 물(다리 밖)과 산은 못 지난다.</summary>
    public sealed class Reach
    {
        public int CX, CY, X0, Y0, W, H, RoadVersion;
        public float Max;
        public float[] Cost = Array.Empty<float>();
        public float At(int x, int y)
        {
            int lx = x - X0, ly = y - Y0;
            return lx < 0 || ly < 0 || lx >= W || ly >= H ? float.PositiveInfinity : Cost[ly * W + lx];
        }
    }

    private float WalkCost(int x, int y)
    {
        var c = Get(x, y, out int l);
        if ((c.Flags[l] & FlagRoad) != 0) return 0.5f;
        return c.Type[l] switch
        {
            TileType.Grass => 1f, TileType.Road => 0.5f, TileType.Forest => 1.8f, TileType.Hills => 1.6f, TileType.Marsh => 2.2f,
            _ => float.PositiveInfinity,
        };
    }

    /// <summary>(cx, cy) 에서 수고 maxCost 안에 닿는 칸들을 다익스트라(8방향)로 구한다.</summary>
    public Reach BuildReach(int cx, int cy, float maxCost)
    {
        int r = (int)MathF.Ceiling(maxCost / 0.5f) + 1;
        EnsureAround(cx, cy, r);
        var R = new Reach { CX = cx, CY = cy, Max = maxCost, RoadVersion = RoadVersion };
        R.X0 = Math.Max(0, cx - r); R.Y0 = Math.Max(0, cy - r);
        R.W = Math.Min(Width - 1, cx + r) - R.X0 + 1; R.H = Math.Min(Height - 1, cy + r) - R.Y0 + 1;
        R.Cost = new float[R.W * R.H];
        Array.Fill(R.Cost, float.PositiveInfinity);
        var open = new PriorityQueue<int, float>();
        // 중심이 물 위면 가장 가까운 땅들에서 시작한다
        for (int d = 0; d <= 8 && open.Count == 0; d++)
            for (int y = cy - d; y <= cy + d; y++)
                for (int x = cx - d; x <= cx + d; x++)
                {
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != d || x < R.X0 || y < R.Y0 || x >= R.X0 + R.W || y >= R.Y0 + R.H) continue;
                    if (float.IsInfinity(WalkCost(x, y))) continue;
                    int si = (y - R.Y0) * R.W + (x - R.X0);
                    R.Cost[si] = d;
                    open.Enqueue(si, d);
                }
        while (open.TryDequeue(out int cur, out float g))
        {
            if (g > R.Cost[cur]) continue;
            int x = cur % R.W, y = cur / R.W;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= R.W || ny >= R.H) continue;
                    float step = WalkCost(nx + R.X0, ny + R.Y0);
                    if (float.IsInfinity(step)) continue;
                    if (dx != 0 && dy != 0)
                    {
                        // 대각선은 양옆 두 칸이 다 지나갈 수 있어야 한다(강 모서리를 비껴 건너지 않게)
                        if (float.IsInfinity(WalkCost(x + dx + R.X0, y + R.Y0)) || float.IsInfinity(WalkCost(x + R.X0, y + dy + R.Y0))) continue;
                        step *= 1.414f;
                    }
                    float ng = g + step;
                    int ni = ny * R.W + nx;
                    if (ng > maxCost || ng >= R.Cost[ni]) continue;
                    R.Cost[ni] = ng;
                    open.Enqueue(ni, ng);
                }
        }
        return R;
    }

    /// <summary>걸음 지도 안의 빈 경작 가능 칸마다 visit(id, 수고). reserve 안쪽 길가 칸은 집터로 남긴다.</summary>
    private void ForEachReachable(Reach R, int reserve, Action<int, float> visit)
    {
        int x0 = R.X0, y0 = R.Y0, x1 = R.X0 + R.W - 1, y1 = R.Y0 + R.H - 1;
        for (int cy = y0 / CS; cy <= y1 / CS; cy++)
            for (int cx = x0 / CS; cx <= x1 / CS; cx++)
            {
                var c = _chunks[cy * ChunksX + cx]!;
                if (c.Free <= 0) continue;
                int ys = Math.Max(y0, cy * CS), ye = Math.Min(y1, cy * CS + CS - 1);
                int xs = Math.Max(x0, cx * CS), xe = Math.Min(x1, cx * CS + CS - 1);
                for (int yy = ys; yy <= ye; yy++)
                    for (int xx = xs; xx <= xe; xx++)
                    {
                        float cost = R.Cost[(yy - y0) * R.W + (xx - x0)];
                        if (float.IsInfinity(cost)) continue;
                        int l = (yy - cy * CS) * CS + (xx - cx * CS);
                        if (c.Owner[l] >= 0 || (c.Flags[l] & (FlagHouse | FlagRoad)) != 0 || !Farmable(c.Type[l])) continue;
                        if (reserve > 0 && (xx - R.CX) * (xx - R.CX) + (yy - R.CY) * (yy - R.CY) <= reserve * reserve &&
                            (IsRoad(xx + 1, yy) || IsRoad(xx - 1, yy) || IsRoad(xx, yy + 1) || IsRoad(xx, yy - 1))) continue;
                        visit(yy * Width + xx, cost);
                    }
            }
    }

    /// <summary>ClaimContiguous 가 실제로 잡을 수 있는 칸 수(집터로 남긴 길가 칸은 뺀다).</summary>
    public int CountClaimable(Reach R, int reserve, float maxCost = float.MaxValue)
    {
        int n = 0;
        ForEachReachable(R, reserve, (_, c) => { if (c <= maxCost) n++; });
        return n;
    }

    /// <summary>
    /// 마을 밭 잡기: 비옥하고 덜 걸어가는 땅부터. 이미 가진 밭에 붙은 칸에 작은 가산점. reserve 안쪽의 길가 칸은 집터로 남긴다.
    /// </summary>
    public int ClaimContiguous(int household, int n, HouseholdStore hh, Reach R, int reserve)
    {
        if (n <= 0) return 0;
        var cand = new List<(int id, float q)>();
        ForEachReachable(R, reserve, (id, cost) => cand.Add((id, BaseFert(id) - 0.02f * cost)));
        int got = 0;
        var taken = new HashSet<int>();
        while (got < n && taken.Count < cand.Count)
        {
            int best = -1;
            float bestS = float.MinValue;
            foreach (var (id, q) in cand)
            {
                if (taken.Contains(id)) continue;
                var (x, y) = Pos(id);
                int adj = 0;
                if (InBounds(x + 1, y) && Owner(Id(x + 1, y)) == household) adj++;
                if (InBounds(x - 1, y) && Owner(Id(x - 1, y)) == household) adj++;
                if (InBounds(x, y + 1) && Owner(Id(x, y + 1)) == household) adj++;
                if (InBounds(x, y - 1) && Owner(Id(x, y - 1)) == household) adj++;
                float sc = q + (adj > 0 ? 0.12f + 0.04f * adj : 0f);
                if (sc > bestS) { bestS = sc; best = id; }
            }
            if (best < 0) break;
            taken.Add(best);
            SetOwner(best, household, hh);
            got++;
        }
        return got;
    }

    /// <summary>필지의 좋음 점수: 비옥도와 물, 집에서 먼 것은 깎는다.</summary>
    private float Quality(int id, int hx, int hy)
    {
        var (x, y) = Pos(id);
        float d = MathF.Sqrt((x - hx) * (x - hx) + (y - hy) * (y - hy));
        return BaseFert(id) - 0.02f * d;
    }

    /// <summary>(x, y) 주변 radius 안의 주인 없는 경작 가능 필지를 좋은 순서로. take 개만 필요하면 그만큼만 고른다.</summary>
    /// <summary>원 안의 빈 경작 가능 타일마다 visit 을 부른다. 빈 땅이 없는 청크는 통째로 건너뛴다.</summary>
    private void ForEachFree(int x, int y, int radius, Action<int> visit)
    {
        EnsureAround(x, y, radius);
        int x0 = Math.Max(0, x - radius), x1 = Math.Min(Width - 1, x + radius);
        int y0 = Math.Max(0, y - radius), y1 = Math.Min(Height - 1, y + radius);
        for (int cy = y0 / CS; cy <= y1 / CS; cy++)
            for (int cx = x0 / CS; cx <= x1 / CS; cx++)
            {
                var c = _chunks[cy * ChunksX + cx]!;
                if (c.Free <= 0) continue;
                int ys = Math.Max(y0, cy * CS), ye = Math.Min(y1, cy * CS + CS - 1);
                int xs = Math.Max(x0, cx * CS), xe = Math.Min(x1, cx * CS + CS - 1);
                for (int yy = ys; yy <= ye; yy++)
                    for (int xx = xs; xx <= xe; xx++)
                    {
                        if ((xx - x) * (xx - x) + (yy - y) * (yy - y) > radius * radius) continue;
                        int l = (yy - cy * CS) * CS + (xx - cx * CS);
                        if (c.Owner[l] >= 0 || (c.Flags[l] & FlagHouse) != 0 || !Farmable(c.Type[l])) continue;
                        visit(yy * Width + xx);
                    }
            }
    }

    public List<int> FreePlotsNear(int x, int y, int radius, int take = int.MaxValue)
    {
        var list = new List<int>();
        ForEachFree(x, y, radius, list.Add);
        if (take >= list.Count)
        {
            list.Sort((a, b) => Quality(b, x, y).CompareTo(Quality(a, x, y)));
            return list;
        }
        // 상위 take 개만 고른다(작은 힙).
        var heap = new PriorityQueue<int, float>(take + 1);
        foreach (int id in list)
        {
            heap.Enqueue(id, Quality(id, x, y));
            if (heap.Count > take) heap.Dequeue();
        }
        var top = new List<int>(take);
        while (heap.Count > 0) top.Add(heap.Dequeue());
        top.Reverse();
        return top;
    }

    /// <summary>radius 안의 빈 경작 가능 필지 수(정렬 없이 센다).</summary>
    public int CountFreeNear(int x, int y, int radius)
    {
        int n = 0;
        ForEachFree(x, y, radius, _ => n++);
        return n;
    }

    /// <summary>(cx, cy) 를 중심으로 radius 안의 빈 필지 중 좋은 것을 n 개까지 가진다.</summary>
    public int Claim(int household, int n, HouseholdStore hh, int cx, int cy, int radius)
    {
        if (n <= 0) return 0;
        var free = FreePlotsNear(cx, cy, radius, n);
        int got = 0;
        foreach (int id in free)
        {
            if (got >= n) break;
            SetOwner(id, household, hh);
            got++;
        }
        return got;
    }

    public int Transfer(int from, int to, int n, HouseholdStore hh)
    {
        var mine = PlotsOf(from);
        mine.Sort((a, b) => BaseFert(b).CompareTo(BaseFert(a)));
        int moved = 0;
        for (int k = 0; k < mine.Count && moved < n; k += 2) { SetOwner(mine[k], to, hh); moved++; }
        for (int k = 1; k < mine.Count && moved < n; k += 2) { SetOwner(mine[k], to, hh); moved++; }
        return moved;
    }

    public void ReleaseWorst(int household, int n, HouseholdStore hh)
    {
        var mine = PlotsOf(household);
        mine.Sort((a, b) => BaseFert(a).CompareTo(BaseFert(b)));
        for (int k = 0; k < n && k < mine.Count; k++) SetOwner(mine[k], -1, hh);
    }

    public void Release(int household, HouseholdStore hh)
    {
        foreach (int id in PlotsOf(household)) SetOwner(id, -1, hh);
    }

    /// <summary>집터로 쓸 수 있는 칸: 비었거나 밭(밭은 집에 내준다).</summary>
    public bool BuildableOrField(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        var c = Get(x, y, out int l);
        return Farmable(c.Type[l]) && c.Type[l] != TileType.Forest && (c.Flags[l] & (FlagHouse | FlagRoad)) == 0;
    }

    /// <summary>(x, y) 가 누군가의 밭이면 주인에게서 떼어 낸다. 주인은 다음 해 땅 잡기에서 다른 땅을 찾는다.</summary>
    public int Evict(int x, int y, HouseholdStore hh)
    {
        int id = Id(x, y);
        int o = Owner(id);
        if (o >= 0) SetOwner(id, -1, hh);
        return o;
    }

    public void MarkHouse(int x, int y, bool on)
    {
        var c = Get(x, y, out int l);
        bool was = (c.Flags[l] & FlagHouse) != 0;
        if (was == on) return;
        if (Farmable(c.Type[l]) && c.Owner[l] < 0) c.Free += on ? -1 : 1;
        c.Flags[l] = (byte)(on ? c.Flags[l] | FlagHouse : c.Flags[l] & ~FlagHouse);
    }

    /// <summary>집을 지을 수 있는 땅인가.</summary>
    public bool Buildable(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        var c = Get(x, y, out int l);
        return Farmable(c.Type[l]) && c.Owner[l] < 0 && (c.Flags[l] & FlagHouse) == 0;
    }

    /// <summary>(cx, cy) 주변 radius 안에서 집을 지을 수 있는 타일 하나(가운데 가까운 것부터, 무작위 섞음).</summary>
    public (int x, int y) HouseSpot(int cx, int cy, int radius, ref Rng rng)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            float r = MathF.Sqrt(rng.NextFloat()) * radius;
            float a = rng.NextFloat() * MathF.PI * 2;
            int x = (int)(cx + MathF.Cos(a) * r), y = (int)(cy + MathF.Sin(a) * r);
            if (Buildable(x, y)) return (x, y);
        }
        return (-1, -1);
    }

    /// <summary>새 마을 자리 점수: 둘레 좋은 빈 땅 + 물 가까움.</summary>
    public float VillageSiteScore(int x, int y, int radius)
    {
        EnsureAround(x, y, radius);
        if (!Buildable(x, y)) return float.MinValue;
        if (CountFreeNear(x, y, radius) < 40) return float.MinValue;
        var free = FreePlotsNear(x, y, radius, 60);
        float land = 0;
        for (int k = 0; k < Math.Min(60, free.Count); k++) land += BaseFert(free[k]);
        int wd = WaterDist(Id(x, y));
        float water = wd <= 1 ? 0f : wd <= 5 ? 22f : wd <= 10 ? 12f : 0f;   // 물을 길을 수 있는 곳이 크게 끌린다
        return land + water;
    }
}
