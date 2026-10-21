using People.Sim;
using Xunit;

public class CareerTests
{
    [Fact]
    public void BuyLandMovesPlotAndFood()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households;
        int lh = hh.Head[lord].Index;
        int buyer = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && w.PatronOf(h) == lord && w.People.IsValid(hh.Head[h]));
        hh.Food[buyer] = 1000f;
        var c = new InteractionContext { Actor = hh.Head[buyer].Index, Target = lh, Third = -1 };
        var def = w.Def(InteractionId.BuyLand);
        Assert.True(def.Can(w, c));
        int p0 = hh.PlotCount[buyer], s0 = hh.PlotCount[lord];
        float price = w.LandPrice(lord, buyer);
        float sellerCoin = hh.Coin[lord];
        def.Success!(w, c, new InteractionResult { Accepted = true });
        Assert.Equal(p0 + 1, hh.PlotCount[buyer]);
        Assert.Equal(s0 - 1, hh.PlotCount[lord]);
        Assert.Equal(sellerCoin + price, hh.Coin[lord], 1);   // 땅값은 은화로 판 쪽에 간다
    }

    [Fact]
    public void ServiceComesBeforeFief()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int lh = hh.Head[lord].Index;
        int h = Enumerable.Range(0, hh.HighWater).First(x => hh.IsAlive(x) && w.PatronOf(x) == lord && w.ClientCount(x) == 0 && p.IsValid(hh.Head[x])
            && w.MembersOf(x).Any(m => p.Male[m] && p.JobOf[m] == 0 && (w.Day - p.BirthDay[m]) / w.Config.DaysPerYear is >= 16 and <= 40));
        int head = hh.Head[h].Index;
        var fief = new InteractionContext { Actor = head, Target = lh, Third = -1 };
        Assert.False(w.Def(InteractionId.PetitionFief).Can(w, fief));   // 아직 섬기지 않았다
        int man = w.MembersOf(h).First(m => p.Male[m] && p.JobOf[m] == 0 && (w.Day - p.BirthDay[m]) / w.Config.DaysPerYear is >= 16 and <= 40);
        var serve = new InteractionContext { Actor = man, Target = lh, Third = -1 };
        Assert.True(w.Def(InteractionId.ServeSoldier).Can(w, serve));
        w.Def(InteractionId.ServeSoldier).Success!(w, serve, new InteractionResult { Accepted = true });
        Assert.Equal((byte)Job.Soldier, p.JobOf[man]);
        Assert.True(w.Def(InteractionId.PetitionFief).Can(w, fief));
    }

    [Fact]
    public void TaxRateIsClamped()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        w.SetTaxRate(lord, 0.5f);
        Assert.Equal(0.2f, w.TaxRate(lord), 3);
        w.SetTaxRate(lord, 0f);
        Assert.Equal(0.08f, w.TaxRate(lord), 3);
    }
}
