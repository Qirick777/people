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
    public void NoMarriageCloserThanCousins()
    {
        var w = Run(5, 120);
        var p = w.People;
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i) || p.Male[i] || !p.IsValid(p.Husband[i])) continue;
            float r = w.Lineage.Relatedness(p.Uid[i], p.Uid[p.Husband[i].Index]);
            Assert.True(r <= w.Config.KinForbidden, $"혈연 {r} 부부");
        }
    }
}
