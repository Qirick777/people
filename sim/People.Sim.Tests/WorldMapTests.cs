using People.Sim;
using Xunit;

public class WorldMapTests
{
    [Fact]
    public void ChunkContentDoesNotDependOnGenerationOrder()
    {
        var a = WorldMap.Generate(11, 24);
        var b = WorldMap.Generate(11, 24);
        // a: 왼쪽 → 오른쪽, b: 오른쪽 → 왼쪽 순서로 만든다
        for (int cx = 6; cx <= 14; cx++) a.EnsureAround(cx * WorldMap.CS + 16, 12 * WorldMap.CS + 16, 0);
        for (int cx = 14; cx >= 6; cx--) b.EnsureAround(cx * WorldMap.CS + 16, 12 * WorldMap.CS + 16, 0);
        for (int x = 6 * WorldMap.CS; x < 15 * WorldMap.CS; x++)
            for (int y = 12 * WorldMap.CS; y < 13 * WorldMap.CS; y++)
            {
                int id = a.Id(x, y);
                Assert.Equal(a.Type(id), b.Type(id));
                Assert.Equal(a.BaseFert(id), b.BaseFert(id));
            }
    }

    [Fact]
    public void ContinentHasSeaLandRiversAndAllLandDrainsToSea()
    {
        var m = WorldMap.Generate(5, 32);
        var c = m.Coarse!;
        int sea = 0, river = 0;
        for (int i = 0; i < c.W * c.H; i++)
        {
            if (c.Sea[i]) { sea++; continue; }
            if (c.River[i]) river++;
            // 물길을 따라가면 바다에 닿는다
            int k = i, steps = 0;
            while (k >= 0 && !c.Sea[k] && steps++ < c.W * c.H) k = c.Downstream[k];
            Assert.True(k >= 0 && c.Sea[k], $"칸 {i} 이 바다로 흐르지 않는다");
        }
        Assert.InRange(sea / (float)(c.W * c.H), 0.3f, 0.6f);
        Assert.True(river > 5);
    }

    [Fact]
    public void SettlementOnGeneratedWorldIsDeterministic()
    {
        World Run()
        {
            var w = new World(4, WorldMap.Generate(4, 24));
            w.SeedCouples(6);
            for (int d = 0; d < 40 * w.Config.DaysPerYear; d++) w.Step();
            return w;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.Stats.Csv, b.Stats.Csv);
        Assert.True(a.People.AliveCount > 20);
    }
}
