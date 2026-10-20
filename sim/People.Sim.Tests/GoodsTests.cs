using People.Sim;
using Xunit;

public class GoodsTests
{
    [Fact]
    public void HouseholdsGatherWeaveAndKeepTools()
    {
        var w = new World(3, 400);
        w.SeedCouples(5);
        for (int d = 0; d < 80 * w.Config.DaysPerYear; d++) w.Step();
        var hh = w.Households;
        var alive = Enumerable.Range(0, hh.HighWater).Where(hh.IsAlive).ToList();
        Assert.NotEmpty(alive);
        // 집에서 짠 옷과 스스로 깎은 도구로 평소에는 크게 모자라지 않는다
        Assert.True(alive.Average(h => w.ToolCov(h)) > 0.3f);
        Assert.True(alive.Sum(h => w.GoodsOf(h, Good.Cloth)) > 0f);
        Assert.True(w.Stats.ClothShortMean < 0.5f);
        // 마을 값은 기본값의 0.4~3배 안
        for (int v = 0; v < w.VillageList.Count; v++)
            foreach (var g in new[] { Good.Tools, Good.Cloth })
                Assert.InRange(w.PriceAt(v, g), 0.39f * World.BasePrice[(int)g], 3.01f * World.BasePrice[(int)g]);
    }
}
