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
        var def = w.Def(InteractionId.BuyLand);
        // 지배자 땅을 살 수 있는 아래 집(살 땅이 남는 마을인지는 역사에 따라 다르다)
        var clients = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && w.People.IsValid(hh.Head[h])).ToList();
        foreach (int h in clients) { hh.Food[h] = 1000f; hh.Coin[h] = 1000f; }
        var sellers = new[] { lord }.Concat(clients).ToList();
        var (buyer, seller) = clients.SelectMany(b => sellers.Select(s => (b, s)))
            .First(x => x.b != x.s && def.Can(w, new InteractionContext { Actor = hh.Head[x.b].Index, Target = hh.Head[x.s].Index, Third = -1 }));
        lord = seller; lh = hh.Head[seller].Index;
        var c = new InteractionContext { Actor = hh.Head[buyer].Index, Target = lh, Third = -1 };
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
