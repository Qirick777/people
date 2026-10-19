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

    [Fact]
    public void HiddenFatherKeepsSurnameButGenesFollowTrueFather()
    {
        // 남편(족보 아버지)과 다른 진짜 아버지: 성은 남편 가문, 혈연은 진짜 아버지 쪽으로 잡혀야 한다.
        var l = new Lineage();
        int husband = l.Add(true, 0, -1, -1);
        int lover = l.Add(true, 0, -1, -1);
        int wife = l.Add(false, 0, -1, -1);
        int child = l.Add(true, 100, lover, wife, husband);
        Assert.Equal(husband, l.Father(child));
        Assert.Equal(lover, l.BioFather(child));
        Assert.True(l.HiddenFather(child));
        Assert.Equal(l.FamilyOf(husband), l.FamilyOf(child));
        Assert.Equal(0.5f, l.Relatedness(child, lover), 3);
        Assert.Equal(0f, l.Relatedness(child, husband), 3);
        // 사생아(족보 아버지 없음)는 어머니 성을 따른다
        int bastard = l.Add(false, 100, lover, wife, -1);
        Assert.Equal(-1, l.Father(bastard));
        Assert.Equal(l.FamilyOf(wife), l.FamilyOf(bastard));
    }
}
