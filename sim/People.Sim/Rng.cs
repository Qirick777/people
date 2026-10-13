namespace People.Sim;

/// <summary>
/// 시드 고정 난수(xoshiro256**). 같은 시드면 같은 수열이 나온다.
/// System.Random 은 런타임 버전마다 구현이 바뀔 수 있어 쓰지 않는다.
/// </summary>
public struct Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        // splitmix64 로 상태를 채운다.
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    private static ulong SplitMix(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextU64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>[0, 1) 실수.</summary>
    public float NextFloat() => (NextU64() >> 40) * (1.0f / (1UL << 24));

    /// <summary>[0, n) 정수.</summary>
    public int NextInt(int n) => (int)((NextU64() >> 33) % (ulong)n);

    public bool Chance(float p) => NextFloat() < p;

    public bool NextBool() => (NextU64() & 1UL) != 0;

    /// <summary>같은 시드에서 갈라져 나온 독립 수열. 하위 시스템마다 따로 쓴다.</summary>
    public Rng Split(ulong stream) => new(NextU64() ^ (stream * 0xD1B54A32D192ED03UL));
}
