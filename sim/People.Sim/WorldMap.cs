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
        public int Free;                                         // 주인 없고 집터 아닌 경작 가능 타일 수
    }
    public const byte FlagField = 1, FlagHouse = 2;

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

    public List<int> PlotsOf(int household) =>
        household < _byOwner.Length && _byOwner[household] != null ? new List<int>(_byOwner[household]!) : new List<int>();

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
