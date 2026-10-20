using People.Sim;
using Xunit;

public class GenTests
{
    private static World Make(int years)
    {
        var map = WorldMap.Generate(3, 48);
        var w = new World(3, map, SimConfig.Default);
        w.GenerateSettled(new GenOptions { Rulers = 2, VillagesPerRuler = 3 });
        for (int y = 0; y < years; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        return w;
    }

    [Fact]
    public void GeneratedWorldHasLordsKinAndTenants()
    {
        var w = Make(0);
        var hh = w.Households; var p = w.People;
        Assert.True(hh.AliveCount >= 60, $"집이 {hh.AliveCount}뿐이다");
        // 지배자가 있고 대부분의 집이 누군가를 섬긴다
        int lords = 0, served = 0;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            if (w.ClientCount(h) > 0) lords++;
            if (w.PatronOf(h) >= 0) served++;
        }
        Assert.True(lords >= 4);
        Assert.True(served > hh.AliveCount * 0.8);
        // 족보: 다른 집 가장과 4촌 안인 가장이 많다(혈족 집이 있다)
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && p.IsValid(hh.Head[h])).Select(h => p.Uid[hh.Head[h].Index]).ToList();
        int withKin = heads.Count(u => heads.Any(o => o != u && w.Lineage.Chon(u, o) is > 0 and <= 4));
        Assert.True(withKin > heads.Count / 2, $"혈족이 있는 가장 {withKin}/{heads.Count}");
        // 죽은 조상이 족보에 남는다
        Assert.True(w.Lineage.Count > p.AliveCount);
    }

    [Fact]
    public void GeneratedWorldHoldsTogether()
    {
        var w = Make(15);
        var hh = w.Households; var p = w.People;
        Assert.True(p.AliveCount > 400);
        int tenants = 0, lords = 0;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            if (w.ClassOf(h) == LandClass.Tenant) tenants++;
            if (w.ClientCount(h) > 0) lords++;
        }
        Assert.True(tenants > 0, "소작농이 없다");
        Assert.True(lords >= 3, "세력이 흩어졌다");
    }
}
