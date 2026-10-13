namespace People.Sim;

public enum TileType : byte { Sea, Lake, River, Grass, Forest, Marsh, Hills, Mountain, Road }

/// <summary>
/// 대략 지도: 한 칸 = 청크 하나 (spec world.md "두 단계 생성").
/// 높낮이, 바다, 기온, 강수, 물길(흐름 누적), 호수, 생물군계를 시작할 때 한 번 만든다.
/// </summary>
public sealed class CoarseMap
{
    public readonly int W, H;
    public readonly uint Seed;
    public readonly float[] Elevation, Filled, Temperature, Rain, Flow;
    public readonly int[] Downstream;        // 물이 흘러가는 이웃 칸, 바다·끝이면 -1
    public readonly bool[] Sea, Lake, River;
    public readonly float[] LakeMask, SeaMask;   // 보간용 0/1
    public float SeaLevel { get; }
    public float HillLevel { get; }       // 육지 높이 상위 25% 부터 언덕
    public float MountainLevel { get; }   // 상위 10% 부터 산
    public float RiverThreshold { get; }

    public CoarseMap(uint seed, int w, int h)
    {
        Seed = seed; W = w; H = h;
        int n = w * h;
        Elevation = new float[n]; Filled = new float[n]; Temperature = new float[n]; Rain = new float[n]; Flow = new float[n];
        Downstream = new int[n]; Sea = new bool[n]; Lake = new bool[n]; River = new bool[n];
        LakeMask = new float[n]; SeaMask = new float[n];

        // 1) 높낮이: 노이즈 − 가장자리로 갈수록 낮게 (섬 대륙)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = x / (float)w - 0.5f, ny = y / (float)h - 0.5f;
                float d = MathF.Sqrt(nx * nx + ny * ny) * 2f;
                float e = Noise.Fbm(seed, x / 9f, y / 9f, 5);
                Elevation[y * w + x] = e - 0.55f * d * d + 0.12f;
            }
        var sorted = (float[])Elevation.Clone();
        Array.Sort(sorted);
        SeaLevel = sorted[(int)(n * 0.42f)];   // 약 42%는 바다
        for (int i = 0; i < n; i++)
        {
            int x = i % w, y = i / w;
            Sea[i] = Elevation[i] < SeaLevel || x == 0 || y == 0 || x == w - 1 || y == h - 1;
        }

        var landElev = Elevation.Where(e => e >= SeaLevel).OrderBy(e => e).ToArray();
        HillLevel = landElev.Length > 0 ? landElev[(int)(landElev.Length * 0.75f)] - SeaLevel : 0.2f;
        MountainLevel = landElev.Length > 0 ? landElev[(int)(landElev.Length * 0.90f)] - SeaLevel : 0.3f;

        // 2) 웅덩이 메우기(priority flood): 바다에서 거꾸로 채워 모든 땅이 바다로 흐르게 한다.
        var pq = new PriorityQueue<int, float>();
        var done = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (!Sea[i]) continue;
            Filled[i] = Elevation[i];
            done[i] = true;
            pq.Enqueue(i, Elevation[i]);
        }
        Span<int> nb = stackalloc int[8];
        while (pq.TryDequeue(out int c, out float ce))
        {
            int k = Neighbors(c, nb);
            for (int j = 0; j < k; j++)
            {
                int m = nb[j];
                if (done[m]) continue;
                done[m] = true;
                Filled[m] = MathF.Max(Elevation[m], ce + 0.0005f);
                Downstream[m] = c;
                pq.Enqueue(m, Filled[m]);
            }
        }
        for (int i = 0; i < n; i++) if (Sea[i]) Downstream[i] = -1;

        // 3) 호수: 메운 깊이가 충분한 곳
        for (int i = 0; i < n; i++) Lake[i] = !Sea[i] && Filled[i] - Elevation[i] > 0.025f;

        // 4) 기온(남쪽이 따뜻, 높으면 춥다)과 강수(노이즈 + 바다 가까이 많음)
        for (int i = 0; i < n; i++)
        {
            int x = i % w, y = i / w;
            float land = MathF.Max(0f, Elevation[i] - SeaLevel);
            Temperature[i] = Math.Clamp(0.25f + 0.75f * y / (float)h - land * 1.2f + (Noise.Value(seed + 7, x / 6f, y / 6f) - 0.5f) * 0.2f, 0f, 1f);
            float coast = 0f;
            int k = Neighbors(i, nb);
            for (int j = 0; j < k; j++) if (Sea[nb[j]]) coast = 0.15f;
            Rain[i] = Math.Clamp(Noise.Fbm(seed + 13, x / 8f, y / 8f, 3) + coast, 0f, 1f);
        }

        // 5) 흐름 누적: 높은 칸부터 아래로 빗물을 넘긴다
        var order = Enumerable.Range(0, n).Where(i => !Sea[i]).OrderByDescending(i => Filled[i]).ToArray();
        foreach (int i in order) Flow[i] += 0.5f + Rain[i];
        foreach (int i in order) if (Downstream[i] >= 0) Flow[Downstream[i]] += Flow[i];
        RiverThreshold = 9f;
        for (int i = 0; i < n; i++)
        {
            River[i] = !Sea[i] && !Lake[i] && Flow[i] >= RiverThreshold;
            LakeMask[i] = Lake[i] ? 1f : 0f;
            SeaMask[i] = Sea[i] ? 1f : 0f;
        }
    }

    public int Neighbors(int c, Span<int> nb)
    {
        int x = c % W, y = c / W, k = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                nb[k++] = yy * W + xx;
            }
        return k;
    }

    /// <summary>칸 사이 쌍선형 보간.</summary>
    public float Sample(float[] field, float cx, float cy)
    {
        cx = Math.Clamp(cx, 0f, W - 1.001f);
        cy = Math.Clamp(cy, 0f, H - 1.001f);
        int x0 = (int)cx, y0 = (int)cy;
        float fx = cx - x0, fy = cy - y0;
        float a = field[y0 * W + x0], b = field[y0 * W + x0 + 1], c = field[(y0 + 1) * W + x0], d = field[(y0 + 1) * W + x0 + 1];
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
}
