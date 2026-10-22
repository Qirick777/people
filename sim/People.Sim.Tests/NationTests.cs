using People.Sim;
using Xunit;

public class NationTests
{
    [Fact]
    public void SmallRealmsCannotFoundANation()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var reqs = w.NationReqs(lord);
        Assert.Equal(7, reqs.Count);
        Assert.False(w.CanFoundNation(lord));
        Assert.Null(w.FoundNation(lord, "시험 왕국", -1));
    }

    [Fact]
    public void ANationThatFallsShortForThreeYearsIsLost()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        w.ForceNation(lord, "시험 왕국");
        Assert.NotNull(w.NationOf(lord));
        for (int y = 0; y < 3; y++) w.RecountNations();
        Assert.Null(w.NationOf(lord));
    }

    [Fact]
    public void PactsStopWars()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households;
        int other = Enumerable.Range(0, hh.HighWater).FirstOrDefault(h => hh.IsAlive(h) && h != lord && w.CanWarNow(lord, h), -1);
        if (other < 0) return;   // 싸울 만한 이웃이 없는 세계
        w.AddPactNow(lord, other, PactKind.NonAggression);
        Assert.True(w.HasPact(other, lord));
        Assert.False(w.CanWarNow(lord, other));
    }

    [Fact]
    public void NationAtFollowsTheTree()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        w.ForceNation(lord, "시험 왕국");
        var hh = w.Households;
        int client = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && w.PatronOf(h) == lord);
        Assert.Equal("시험 왕국", w.NationAt(client)!.Name);
    }
}
