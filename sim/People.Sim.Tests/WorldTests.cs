using People.Sim;
using Xunit;

public class WorldTests
{
    private static World Run(ulong seed, int years)
    {
        var w = new World(seed, 400);
        w.SeedCouples(5);
        for (int d = 0; d < years * w.Config.DaysPerYear; d++) w.Step();
        return w;
    }

    [Fact]
    public void SameSeedSameHistory()
    {
        var a = Run(7, 60);
        var b = Run(7, 60);
        Assert.Equal(a.Lineage.Count, b.Lineage.Count);
        Assert.Equal(a.People.AliveCount, b.People.AliveCount);
        Assert.Equal(a.Stats.Csv, b.Stats.Csv);
    }

    [Fact]
    public void SmallIslandGrowsFromFiveCouples()
    {
        var w = Run(3, 60);
        Assert.True(w.People.AliveCount > 30, $"60년 뒤 인구 {w.People.AliveCount}");
    }

    [Fact]
    public void SiblingMarriagesAreRare()
    {
        // 금지하지 않지만 함께 자란 사이는 끌리지 않아 형제혼은 드물어야 한다.
        var w = Run(5, 120);
        var p = w.People;
        int couples = 0, siblings = 0;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || p.Male[i] || !p.IsValid(p.Husband[i])) continue;
            couples++;
            if (w.Lineage.Relatedness(p.Uid[i], p.Uid[p.Husband[i].Index]) >= 0.5f) siblings++;
        }
        Assert.True(couples > 20, $"부부 {couples}");
        Assert.True(siblings <= couples * 0.03, $"형제 부부 {siblings}/{couples}");
    }
}
