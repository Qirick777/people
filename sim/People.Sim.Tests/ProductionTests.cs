using People.Sim;
using Xunit;

public class ProductionTests
{
    private static (World w, Group g, int leader, int lord) WithWorkshop()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Workshop);
        foreach (int i in heads.Skip(1).Take(6)) w.JoinGroup(i, g);
        w.RecountOrgs();
        return (w, g, heads[0], lord);
    }

    [Fact]
    public void TreasuryBuysFactionLandForTheLeaderHouse()
    {
        var (w, g, leader, lord) = WithWorkshop();
        var hh = w.Households; var p = w.People;
        g.Treasury = 10000f;
        int home = p.Household[leader];
        int seller = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && h != home && hh.PlotCount[h] > 1).OrderByDescending(h => hh.PlotCount[h])
            .First(h => w.LandPrice(h, home) > 0f);
        int before = hh.PlotCount[home];
        int id = w.BuyFactionLand(g, seller);
        Assert.True(id >= 0);
        Assert.Equal(before + 1, hh.PlotCount[home]);
        Assert.Contains(id, g.FactionPlots);
        Assert.True(g.Treasury < 10000f);
    }

    [Fact]
    public void GatheringUnitStocksBasicMaterials()
    {
        var (w, g, _, _) = WithWorkshop();
        g.Product = Good.Basic; g.ProductManual = true; g.FarmFood = false;
        g.Supply.Clear(); g.Supply.Add((-1, 1f));
        float before = g.Store[(int)Good.Basic];
        for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        if (g.Alive) Assert.True(g.Store[(int)Good.Basic] > before);
    }
}
