using People.Sim;
using Xunit;

public class LineageTests
{
    // 가계도: g1 g2 (조부모) → f, u (형제) ; f + m → a, b ; u + w → c ; a ↔ c 사촌
    private static (Lineage L, int g1, int g2, int f, int m, int u, int w, int a, int b, int c, int h) Build()
    {
        var L = new Lineage();
        int g1 = L.Add(true, 0, -1, -1), g2 = L.Add(false, 0, -1, -1);
        int f = L.Add(true, 1, g1, g2), u = L.Add(true, 1, g1, g2);
        int m = L.Add(false, 1, -1, -1), w = L.Add(false, 1, -1, -1);
        int x = L.Add(false, 1, -1, -1);
        int a = L.Add(true, 2, f, m), b = L.Add(false, 2, f, m);
        int c = L.Add(true, 2, u, w);
        int h = L.Add(true, 2, f, x); // a 의 이복형제
        return (L, g1, g2, f, m, u, w, a, b, c, h);
    }

    [Fact]
    public void KnownRelationships()
    {
        var t = Build();
        Assert.Equal(0.5f, t.L.Relatedness(t.a, t.b), 3);     // 형제
        Assert.Equal(0.5f, t.L.Relatedness(t.a, t.f), 3);     // 부모-자식
        Assert.Equal(0.5f, t.L.Relatedness(t.f, t.a), 3);
        Assert.Equal(0.25f, t.L.Relatedness(t.a, t.g1), 3);   // 조부
        Assert.Equal(0.25f, t.L.Relatedness(t.a, t.u), 3);    // 삼촌
        Assert.Equal(0.125f, t.L.Relatedness(t.a, t.c), 3);   // 사촌
        Assert.Equal(0.25f, t.L.Relatedness(t.a, t.h), 3);    // 이복형제
        Assert.Equal(0f, t.L.Relatedness(t.a, t.w), 3);       // 남
    }
}
