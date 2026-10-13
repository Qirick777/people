namespace People.Sim;

/// <summary>시드 고정 값 노이즈(value noise)와 프랙털 합. 좌표만으로 값이 정해져서 청크를 언제 만들어도 같다.</summary>
public static class Noise
{
    public static uint Hash(uint seed, int x, int y)
    {
        uint h = seed ^ (uint)x * 0x27d4eb2dU ^ (uint)y * 0x165667b1U;
        h ^= h >> 15; h *= 0x85ebca6bU;
        h ^= h >> 13; h *= 0xc2b2ae35U;
        h ^= h >> 16;
        return h;
    }

    /// <summary>[0, 1) 정수 격자 값.</summary>
    public static float Lattice(uint seed, int x, int y) => (Hash(seed, x, y) >> 8) * (1f / (1 << 24));

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    public static float Value(uint seed, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = Smooth(x - x0), fy = Smooth(y - y0);
        float a = Lattice(seed, x0, y0), b = Lattice(seed, x0 + 1, y0);
        float c = Lattice(seed, x0, y0 + 1), d = Lattice(seed, x0 + 1, y0 + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>옥타브를 겹친 노이즈. 결과 대략 0~1.</summary>
    public static float Fbm(uint seed, float x, float y, int octaves = 5)
    {
        float sum = 0, amp = 0.5f, norm = 0, f = 1;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Value(seed + (uint)o * 1013, x * f, y * f);
            norm += amp;
            amp *= 0.5f;
            f *= 2f;
        }
        return sum / norm;
    }
}
